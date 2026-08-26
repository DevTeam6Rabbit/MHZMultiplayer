using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    /// <summary>
    /// Simple IMGUI overlay: lobby panel + chat.
    /// Toggle with F8. Host with F9. Leave with F10.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        public static LobbyUI Instance { get; private set; }

        private bool _showPanel = false;
        private bool _showChat  = false;
        private bool _showScoreboard = false;

        // Lobby input
        private string _joinLobbyIdInput = "";

        // Chat
        private List<string> _chatMessages = new List<string>();
        private string _chatInput = "";
        private Vector2 _chatScroll;

        // Panel rect
        private Rect _panelRect = new Rect(20, 20, 360, 480);
        private Rect _chatRect  = new Rect(20, 520, 360, 200);
        private Rect _scoreRect = new Rect(-1, -1, 280, 340); // positioned on first draw

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                _showPanel = !_showPanel;

            if (Input.GetKeyDown(KeyCode.F9) && NetworkManager.Instance != null)
                NetworkManager.Instance.HostLobby();

            if (Input.GetKeyDown(KeyCode.F10) && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.LeaveLobby();
                _showChat = false;
                _chatMessages.Clear();
            }
        }

        private void OnGUI()
        {
            if (_showPanel)
                _panelRect = GUILayout.Window(9001, _panelRect, DrawLobbyPanel, "MHZ Multiplayer");

            if (_showChat)
                _chatRect = GUILayout.Window(9002, _chatRect, DrawChatPanel, "Chat");

            if (_showScoreboard)
            {
                if (_scoreRect.x < 0) // dock to the right edge on first draw
                    _scoreRect = new Rect(Screen.width - _scoreRect.width - 20, 20,
                                          _scoreRect.width, _scoreRect.height);
                _scoreRect = GUILayout.Window(9003, _scoreRect, DrawScoreboardPanel, "Time Trial Scoreboard");
            }
        }

        private void DrawScoreboardPanel(int id)
        {
            var entries = ScoreboardManager.Entries;
            if (entries.Count == 0)
            {
                GUILayout.Label("No finishes yet.");
                GUILayout.Label("Complete a time trial run to post a time!");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{i + 1}.", GUILayout.Width(24));
                    GUILayout.Label(entries[i].Name);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(ScoreboardManager.FormatTime(entries[i].TimeSeconds));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear"))
                ScoreboardManager.Clear();
            if (GUILayout.Button("Hide"))
                _showScoreboard = false;
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        /// <summary>Pops the scoreboard open (called when a new time is posted).</summary>
        public void ShowScoreboard() => _showScoreboard = true;

        private void DrawLobbyPanel(int id)
        {
            var nm = NetworkManager.Instance;
            if (nm == null) { GUILayout.Label("NetworkManager not loaded."); GUI.DragWindow(); return; }

            GUILayout.Label($"Your Steam name: {SteamFriends.GetPersonaName()}");
            GUILayout.Space(8);

            if (!nm.IsConnected)
            {
                // HOST
                if (GUILayout.Button("Host Lobby (F9)"))
                    nm.HostLobby();

                GUILayout.Space(8);
                GUILayout.Label("— OR join a friend's lobby —");

                GUILayout.BeginHorizontal();
                GUILayout.Label("Lobby ID:", GUILayout.Width(70));
                _joinLobbyIdInput = GUILayout.TextField(_joinLobbyIdInput);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Join"))
                {
                    if (ulong.TryParse(_joinLobbyIdInput, out ulong id64))
                        nm.JoinLobby(new CSteamID(id64));
                    else
                        AddChatMessage("[Error] Invalid lobby ID.");
                }

                GUILayout.Space(8);
                GUILayout.Label("Tip: your friend can invite you via Steam overlay too.");
            }
            else
            {
                // CONNECTED
                GUILayout.Label($"Status: {(nm.IsHost ? "HOSTING" : "CONNECTED")}");
                GUILayout.Label($"Lobby ID: {nm.LobbyId}");

                if (nm.IsHost)
                {
                    GUILayout.Label("Share this ID with friends, or invite via Steam overlay.");
                    if (GUILayout.Button("Copy Lobby ID to Clipboard"))
                        GUIUtility.systemCopyBuffer = nm.LobbyId.ToString();
                }

                GUILayout.Space(8);
                GUILayout.Label($"Players in lobby: {SteamMatchmaking.GetNumLobbyMembers(nm.LobbyId)}");

                int count = SteamMatchmaking.GetNumLobbyMembers(nm.LobbyId);
                for (int i = 0; i < count; i++)
                {
                    CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(nm.LobbyId, i);
                    string self = member == SteamUser.GetSteamID() ? " (You)" : "";
                    GUILayout.Label($"  • {SteamFriends.GetFriendPersonaName(member)}{self}");
                }

                GUILayout.Space(8);
                if (GUILayout.Button("Toggle Chat"))
                    _showChat = !_showChat;

                if (GUILayout.Button("Toggle Scoreboard"))
                    _showScoreboard = !_showScoreboard;

                if (GUILayout.Button("Leave Lobby (F10)"))
                {
                    nm.LeaveLobby();
                    _showChat = false;
                    _chatMessages.Clear();
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("F8 = toggle this panel");

            GUI.DragWindow();
        }

        private void DrawChatPanel(int id)
        {
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(130));
            foreach (string msg in _chatMessages)
                GUILayout.Label(msg);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            _chatInput = GUILayout.TextField(_chatInput);
            if (GUILayout.Button("Send", GUILayout.Width(50)) && !string.IsNullOrEmpty(_chatInput))
            {
                NetworkManager.Instance?.SendChatMessage(_chatInput);
                AddChatMessage($"{SteamFriends.GetPersonaName()}: {_chatInput}");
                _chatInput = "";
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        public void ShowHostedLobby(CSteamID lobbyId)
        {
            _showPanel = true;
            _showChat  = true;
            AddChatMessage($"[System] Lobby created! ID: {lobbyId}");
        }

        public void AddChatMessage(string msg)
        {
            _chatMessages.Add(msg);
            if (_chatMessages.Count > 100) _chatMessages.RemoveAt(0);
            _chatScroll.y = float.MaxValue; // scroll to bottom
        }
    }
}
