using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // pure p2p over steam, no server. one player hosts a lobby, the rest
    // join, then it's just packets flying between everyone.
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

        private readonly Dictionary<string, RemoteProjectile> _remoteProjectiles = new Dictionary<string, RemoteProjectile>();
        private readonly Dictionary<int, ProjectileStateSnapshot> _knownProjectiles = new Dictionary<int, ProjectileStateSnapshot>();

        // 20/sec is plenty, the lerp on the receiving side smooths the rest.
        // careful raising it - everyone sends to everyone, so it's n^2.
        private const float SendRate = 0.05f; // 20 times/sec
        private float _sendTimer;

        // Packet channel
        private const int Channel = 0;

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
                BroadcastProjectileState();
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
            // first packet from someone new lands here. only accept lobby members.
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

        // first byte = packet type. unknown types just get dropped, which
        // keeps old versions from choking on packets they don't know.
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
                    case PacketType.ProjectileState:
                        HandleProjectileState(PacketSerializer.DeserializeProjectileState(data), sender);
                        break;
                    case PacketType.PlayerDamage:
                        HandlePlayerDamage(PacketSerializer.DeserializePlayerDamage(data), sender);
                        break;
                }
            }
        }

        private void HandleHeliState(HeliStatePacket packet, CSteamID sender)
        {
            if (RemotePlayers.TryGetValue(sender, out RemotePlayer remote))
                remote.ApplyState(packet);
        }

        private void HandleProjectileState(ProjectileStatePacket packet, CSteamID sender)
        {
            string key = sender.m_SteamID + ":" + packet.InstanceId;
            if (!_remoteProjectiles.TryGetValue(key, out RemoteProjectile projectile))
            {
                projectile = SpawnRemoteProjectile(sender, packet);
                if (projectile != null)
                    _remoteProjectiles[key] = projectile;
            }

            if (projectile != null)
                projectile.ApplyState(packet);
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

        private void HandlePlayerDamage(PlayerDamagePacket packet, CSteamID sender)
        {
            CSteamID victim = new CSteamID(packet.TargetSteamId);
            if (RemotePlayers.TryGetValue(victim, out RemotePlayer remote))
            {
                remote.ApplyDamage(packet.Damage);
                MultiplayerPlugin.Log.LogInfo($"[NetworkManager] {SteamFriends.GetFriendPersonaName(sender)} hit {SteamFriends.GetFriendPersonaName(victim)} for {packet.Damage} damage");
            }
        }

        public void SendPlayerDamage(CSteamID target, float damage, int projectileInstanceId)
        {
            if (!IsConnected) return;

            var packet = new PlayerDamagePacket
            {
                PacketType = PacketType.PlayerDamage,
                TargetSteamId = target.m_SteamID,
                AttackerSteamId = SteamUser.GetSteamID().m_SteamID,
                Damage = damage,
                ProjectileInstanceId = projectileInstanceId,
            };

            byte[] data = PacketSerializer.Serialize(packet);
            int count = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
                if (member != SteamUser.GetSteamID())
                    SteamNetworking.SendP2PPacket(member, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable, Channel);
            }
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

        private RemoteProjectile SpawnRemoteProjectile(CSteamID sender, ProjectileStatePacket packet)
        {
            GameObject go = new GameObject($"RemoteProjectile_{sender.m_SteamID}_{packet.InstanceId}");
            go.transform.position = packet.Position;
            go.transform.rotation = packet.Rotation;
            UnityEngine.Object.DontDestroyOnLoad(go);

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.detectCollisions = false;

            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.4f;

            RemoteProjectile projectile = go.AddComponent<RemoteProjectile>();
            projectile.SteamId = sender.m_SteamID;
            projectile.InstanceId = packet.InstanceId;
            projectile.ApplyState(packet);
            return projectile;
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

        private void BroadcastProjectileState()
        {
            if (!IsConnected) return;

            foreach (var projectile in FindLocalProjectiles())
            {
                var state = new ProjectileStatePacket
                {
                    PacketType = PacketType.ProjectileState,
                    SteamId = SteamUser.GetSteamID().m_SteamID,
                    InstanceId = projectile.InstanceId,
                    Position = projectile.Position,
                    Rotation = projectile.Rotation,
                    Velocity = projectile.Velocity,
                    LifeSeconds = projectile.LifeSeconds,
                };

                byte[] data = PacketSerializer.Serialize(state);
                int count = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
                for (int i = 0; i < count; i++)
                {
                    CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
                    if (member != SteamUser.GetSteamID())
                        SteamNetworking.SendP2PPacket(member, data, (uint)data.Length, EP2PSend.k_EP2PSendUnreliable, Channel);
                }
            }
        }

        private System.Collections.Generic.List<LocalProjectileSnapshot> FindLocalProjectiles()
        {
            var output = new System.Collections.Generic.List<LocalProjectileSnapshot>();

            foreach (var projectile in FindObjectsOfType<Raulworks.RW_Base_Projectile>())
            {
                if (projectile == null || projectile.gameObject == null || !projectile.gameObject.activeInHierarchy)
                    continue;

                GameObject go = projectile.gameObject;
                if (go.name.Contains("RemoteProjectile_") || go.name.Contains("RemoteHeli_"))
                    continue;

                var rb = go.GetComponent<Rigidbody>();
                output.Add(new LocalProjectileSnapshot
                {
                    InstanceId = go.GetInstanceID(),
                    Position = go.transform.position,
                    Rotation = go.transform.rotation,
                    Velocity = rb != null ? rb.velocity : Vector3.zero,
                    LifeSeconds = Mathf.Max(0.1f, projectile.timeoutTime > 0f ? projectile.timeoutTime : 1f),
                });
            }

            foreach (var projectile in FindObjectsOfType<Raulworks.RW_Gat_Projectile>())
            {
                if (projectile == null || projectile.gameObject == null || !projectile.gameObject.activeInHierarchy)
                    continue;

                GameObject go = projectile.gameObject;
                if (go.name.Contains("RemoteProjectile_") || go.name.Contains("RemoteHeli_"))
                    continue;

                var rb = go.GetComponent<Rigidbody>();
                output.Add(new LocalProjectileSnapshot
                {
                    InstanceId = go.GetInstanceID(),
                    Position = go.transform.position,
                    Rotation = go.transform.rotation,
                    Velocity = rb != null ? rb.velocity : Vector3.zero,
                    LifeSeconds = 1f,
                });
            }

            foreach (var projectile in FindObjectsOfType<Raulworks.RW_RocketProjectile>())
            {
                if (projectile == null || projectile.gameObject == null || !projectile.gameObject.activeInHierarchy)
                    continue;

                GameObject go = projectile.gameObject;
                if (go.name.Contains("RemoteProjectile_") || go.name.Contains("RemoteHeli_"))
                    continue;

                var rb = go.GetComponent<Rigidbody>();
                output.Add(new LocalProjectileSnapshot
                {
                    InstanceId = go.GetInstanceID(),
                    Position = go.transform.position,
                    Rotation = go.transform.rotation,
                    Velocity = rb != null ? rb.velocity : Vector3.zero,
                    LifeSeconds = 1.5f,
                });
            }

            return output;
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

    public class RemoteProjectile : MonoBehaviour
    {
        public ulong SteamId;
        public int InstanceId;

        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private Vector3 _velocity;
        private float _lifeSeconds;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public Vector3 Velocity => _velocity;
        public float LifeSeconds => _lifeSeconds;

        private void Start()
        {
            _targetPosition = transform.position;
            _targetRotation = transform.rotation;
            _lifeSeconds = 1f;
        }

        private void Update()
        {
            if (_lifeSeconds <= 0f)
            {
                if (gameObject != null)
                    Destroy(gameObject);
                return;
            }

            _lifeSeconds -= Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, _targetPosition, 20f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, 20f * Time.deltaTime);

            if (_lifeSeconds <= 0f && gameObject != null)
                Destroy(gameObject);
        }

        public void ApplyState(ProjectileStatePacket packet)
        {
            _targetPosition = packet.Position;
            _targetRotation = packet.Rotation;
            _velocity = packet.Velocity;
            _lifeSeconds = packet.LifeSeconds;

            if (gameObject != null)
                gameObject.SetActive(true);
        }
    }

    public struct ProjectileStateSnapshot
    {
        public int InstanceId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public float LifeSeconds;
    }

    public struct LocalProjectileSnapshot
    {
        public int InstanceId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public float LifeSeconds;
        public ProjectileKind Kind;
        public float Damage;
    }
}
