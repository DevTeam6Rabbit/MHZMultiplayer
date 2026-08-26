using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    /// <summary>
    /// Manages Steam P2P networking between players.
    /// One player hosts; others connect via Steam lobby.
    /// </summary>
    // The whole thing is peer to peer over steam - nobody runs a server.
    // One player makes a steam lobby, the rest join it, and then everyone
    // just fires UDP-style packets at everyone else through steam's relay.
    // The lobby is only used for discovery and for shared settings (steam
    // lobby data), the actual game traffic is SendP2PPacket below.
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        // Steam callbacks
        private Callback<LobbyCreated_t> _lobbyCreated;
        private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
        private Callback<LobbyEnter_t> _lobbyEntered;
        private Callback<P2PSessionRequest_t> _p2pSessionRequest;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdate;

        public bool IsHost { get; private set; }
        public bool IsConnected { get; private set; }
        public CSteamID LobbyId { get; private set; }

        // All connected remote players: SteamID -> their ghost helicopter
        public Dictionary<CSteamID, RemotePlayer> RemotePlayers { get; private set; }
            = new Dictionary<CSteamID, RemotePlayer>();

        // How often (seconds) we broadcast our helicopter state.
        // 20/sec is the sweet spot - slower gets visibly choppy even with the
        // lerp smoothing on the other end, faster is wasted bandwidth since the
        // interpolation hides it anyway. Each packet is only 49 bytes so even a
        // full 16 player lobby is peanuts, but note every player sends to every
        // other player (full mesh, no server), so traffic grows with the square
        // of the player count.
        private const float SendRate = 0.05f; // 20 times/sec
        private float _sendTimer;

        // Packet channel
        private const int Channel = 0;

        /// <summary>Maximum players per lobby.</summary>
        public const int MaxPlayers = 16;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (!SteamManager.Initialized)
            {
                MultiplayerPlugin.Log.LogError("Steam not initialized — multiplayer unavailable.");
                return;
            }

            _lobbyCreated      = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            _lobbyEntered      = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _p2pSessionRequest = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
            _lobbyChatUpdate   = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);

            MultiplayerPlugin.Log.LogInfo("NetworkManager ready. F8 = host, F9 = join.");
        }

        private void Update()
        {
            if (!IsConnected) return;

            // Send our helicopter state on a timer
            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = SendRate;
                BroadcastHeliState();
            }

            // Read incoming packets every frame
            ReceivePackets();
        }

        // ─── Hosting ──────────────────────────────────────────────────────────

        public void HostLobby()
        {
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxPlayers);
            MultiplayerPlugin.Log.LogInfo("Creating lobby...");
        }

        private void OnLobbyCreated(LobbyCreated_t cb)
        {
            if (cb.m_eResult != EResult.k_EResultOK)
            {
                MultiplayerPlugin.Log.LogError($"Lobby creation failed: {cb.m_eResult}");
                return;
            }

            LobbyId = new CSteamID(cb.m_ulSteamIDLobby);
            IsHost = true;
            IsConnected = true;

            // Tag the lobby so other mods/players can find it
            SteamMatchmaking.SetLobbyData(LobbyId, "game", "MHZombie");
            SteamMatchmaking.SetLobbyData(LobbyId, "mod",  "multiplayer");

            MultiplayerPlugin.Log.LogInfo($"Lobby created! ID: {LobbyId}  Share via Steam overlay → Invite Friends.");
            LobbyUI.Instance?.ShowHostedLobby(LobbyId);
        }

        // ─── Joining ──────────────────────────────────────────────────────────

        /// <summary>Called when a friend clicks "Join Game" in the Steam overlay.</summary>
        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t cb)
        {
            SteamMatchmaking.JoinLobby(cb.m_steamIDLobby);
        }

        public void JoinLobby(CSteamID lobbyId)
        {
            SteamMatchmaking.JoinLobby(lobbyId);
        }

        private void OnLobbyEntered(LobbyEnter_t cb)
        {
            LobbyId = new CSteamID(cb.m_ulSteamIDLobby);
            IsConnected = true;

            // Determine role
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(LobbyId);
            IsHost = (owner == SteamUser.GetSteamID());

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            MultiplayerPlugin.Log.LogInfo($"Joined lobby {LobbyId}. Members: {memberCount}. IsHost: {IsHost}");

            // Spawn ghost helis for anyone already in the lobby
            for (int i = 0; i < memberCount; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
                if (member != SteamUser.GetSteamID())
                    SpawnRemotePlayer(member);
            }
        }

        // ─── Member join/leave ────────────────────────────────────────────────

        private void OnLobbyChatUpdate(LobbyChatUpdate_t cb)
        {
            CSteamID changed = new CSteamID(cb.m_ulSteamIDUserChanged);

            if ((cb.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
            {
                if (changed != SteamUser.GetSteamID())
                {
                    MultiplayerPlugin.Log.LogInfo($"Player joined: {SteamFriends.GetFriendPersonaName(changed)}");
                    SpawnRemotePlayer(changed);
                }
            }
            else
            {
                MultiplayerPlugin.Log.LogInfo($"Player left: {SteamFriends.GetFriendPersonaName(changed)}");
                RemoveRemotePlayer(changed);
            }
        }

        // ─── P2P session ──────────────────────────────────────────────────────

        private void OnP2PSessionRequest(P2PSessionRequest_t cb)
        {
            // steam won't let packets through until both sides accept the
            // session, so the first packet from a new player lands here.
            // Accept anyone in our lobby - and ONLY the lobby, otherwise
            // random people could send us junk packets
            int count = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            for (int i = 0; i < count; i++)
            {
                if (SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i) == cb.m_steamIDRemote)
                {
                    SteamNetworking.AcceptP2PSessionWithUser(cb.m_steamIDRemote);
                    return;
                }
            }
        }

        // ─── Packet sending ───────────────────────────────────────────────────

        private void BroadcastHeliState()
        {
            GameObject localHeli = HeliLocator.GetLocalHeli();
            if (localHeli == null) return;

            HeliStatePacket packet = new HeliStatePacket
            {
                PacketType = PacketType.HeliState,
                SteamId    = SteamUser.GetSteamID().m_SteamID,
                Position   = localHeli.transform.position,
                Rotation   = localHeli.transform.rotation,
                Velocity   = localHeli.GetComponent<Rigidbody>()?.velocity ?? Vector3.zero
            };

            byte[] data = PacketSerializer.Serialize(packet);

            int count = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
                if (member != SteamUser.GetSteamID())
                {
                    SteamNetworking.SendP2PPacket(member, data, (uint)data.Length,
                        EP2PSend.k_EP2PSendUnreliable, Channel);
                }
            }
        }

        public void SendChatMessage(string message)
        {
            if (!IsConnected) return;

            ChatPacket packet = new ChatPacket
            {
                PacketType = PacketType.Chat,
                SteamId    = SteamUser.GetSteamID().m_SteamID,
                Message    = message
            };

            byte[] data = PacketSerializer.Serialize(packet);
            int count = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
                if (member != SteamUser.GetSteamID())
                    SteamNetworking.SendP2PPacket(member, data, (uint)data.Length,
                        EP2PSend.k_EP2PSendReliable, Channel);
            }
        }

        public void SendRaceFinish(float timeSeconds)
        {
            if (!IsConnected) return;

            RaceFinishPacket packet = new RaceFinishPacket
            {
                PacketType  = PacketType.RaceFinish,
                SteamId     = SteamUser.GetSteamID().m_SteamID,
                TimeSeconds = timeSeconds
            };

            byte[] data = PacketSerializer.Serialize(packet);
            int count = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
                if (member != SteamUser.GetSteamID())
                    SteamNetworking.SendP2PPacket(member, data, (uint)data.Length,
                        EP2PSend.k_EP2PSendReliable, Channel);
            }
        }

        // ─── Packet receiving ─────────────────────────────────────────────────

        // drain everything steam has queued up for us this frame. the first
        // byte of every packet is its type, so we peek that and hand the rest
        // to the right deserializer. anything we don't recognize just gets
        // dropped on the floor, which conveniently means old versions of the
        // mod won't crash when a newer version sends them packet types they
        // don't know about.
        private void ReceivePackets()
        {
            uint msgSize;
            while (SteamNetworking.IsP2PPacketAvailable(out msgSize, Channel))
            {
                byte[] data = new byte[msgSize];
                uint bytesRead;
                CSteamID sender;

                if (!SteamNetworking.ReadP2PPacket(data, msgSize, out bytesRead, out sender, Channel))
                    continue;

                PacketType type = PacketSerializer.PeekType(data);
                switch (type)
                {
                    case PacketType.HeliState:
                        HandleHeliState(PacketSerializer.DeserializeHeliState(data), sender);
                        break;
                    case PacketType.Chat:
                        HandleChat(PacketSerializer.DeserializeChat(data));
                        break;
                    case PacketType.RaceFinish:
                        HandleRaceFinish(PacketSerializer.DeserializeRaceFinish(data));
                        break;
                }
            }
        }

        private void HandleHeliState(HeliStatePacket packet, CSteamID sender)
        {
            if (RemotePlayers.TryGetValue(sender, out RemotePlayer remote))
                remote.ApplyState(packet);
        }

        private void HandleChat(ChatPacket packet)
        {
            CSteamID sender = new CSteamID(packet.SteamId);
            string name = SteamFriends.GetFriendPersonaName(sender);
            LobbyUI.Instance?.AddChatMessage($"{name}: {packet.Message}");
        }

        private void HandleRaceFinish(RaceFinishPacket packet)
        {
            string name = SteamFriends.GetFriendPersonaName(new CSteamID(packet.SteamId));
            ScoreboardManager.ReportRemoteFinish(name, packet.TimeSeconds);
        }

        // ─── Remote player management ─────────────────────────────────────────

        private void SpawnRemotePlayer(CSteamID steamId)
        {
            if (RemotePlayers.ContainsKey(steamId)) return;

            GameObject ghostHeli = GhostHeliFactory.Create(steamId);
            RemotePlayer rp = ghostHeli.AddComponent<RemotePlayer>();
            rp.SteamId = steamId;
            rp.DisplayName = SteamFriends.GetFriendPersonaName(steamId);
            RemotePlayers[steamId] = rp;

            MultiplayerPlugin.Log.LogInfo($"Spawned ghost heli for {rp.DisplayName}");
        }

        private void RemoveRemotePlayer(CSteamID steamId)
        {
            if (RemotePlayers.TryGetValue(steamId, out RemotePlayer rp))
            {
                if (rp != null && rp.gameObject != null)
                    Destroy(rp.gameObject);
                RemotePlayers.Remove(steamId);
            }
        }

        public void LeaveLobby()
        {
            if (LobbyId.IsValid())
                SteamMatchmaking.LeaveLobby(LobbyId);

            foreach (var rp in RemotePlayers.Values)
                if (rp?.gameObject != null) Destroy(rp.gameObject);

            RemotePlayers.Clear();
            IsConnected = false;
            IsHost = false;
            LobbyId = default;
        }
    }
}
