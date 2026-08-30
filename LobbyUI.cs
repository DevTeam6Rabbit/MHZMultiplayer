using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // Old-school IMGUI HUD, themed by UiTheme so it can't clash with the game's
    // own UI. All windows are permanently visible (no hotkeys / buttons to show
    // or hide them) and are not draggable - they sit in fixed spots that don't
    // overlap:
    //   - Multiplayer  : top-left       - connection + players + actions
    //   - Scoreboard   : right edge     - PvP combat + time trial tables
    //   - Chat         : bottom-left    - message log + input
    //   - PvP health bar : bottom-center - always visible
    public class LobbyUI : MonoBehaviour
    {
        public static LobbyUI Instance { get; private set; }

        private bool _showUI = true; // F8 shows/hides the windows

        private string _joinLobbyIdInput = "";

        private readonly List<string> _chatMessages = new List<string>();
        private string _chatInput = "";
        private Vector2 _chatScroll;

        private readonly Rect _panelRect = new Rect(20, 20, 360, 320);
        private readonly Rect _chatRect = new Rect(20, Screen.height - 240 - 20, 360, 240);
        private readonly Rect _scoreRect = new Rect(Screen.width - 320 - 20, 20, 320, 380);

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                _showUI = !_showUI;

            if (Input.GetKeyDown(KeyCode.F9) && NetworkManager.Instance != null)
                NetworkManager.Instance.HostLobby();

            if (Input.GetKeyDown(KeyCode.F10) && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.LeaveLobby();
                _chatMessages.Clear();
            }
        }

        private void OnGUI()
        {
            UiTheme.Apply();
            DrawPvPHealthBar();

            if (!_showUI) return; // F8 hides/shows the windows

            GUILayout.Window(9001, _panelRect, DrawLobbyPanel, "", UiTheme.Window);
            UiTheme.DrawOutline(_panelRect, UiTheme.Outline);

            GUILayout.Window(9002, _chatRect, DrawChatPanel, "", UiTheme.Window);
            UiTheme.DrawOutline(_chatRect, UiTheme.Outline);

            GUILayout.Window(9003, _scoreRect, DrawScoreboardPanel, "", UiTheme.Window);
            UiTheme.DrawOutline(_scoreRect, UiTheme.Outline);
        }

        // A window's title band + accent underline, drawn as the first element
        // of the window's content. No close button - windows are permanent.
        private void DrawWindowHeader(string title)
        {
            GUILayout.BeginHorizontal(UiTheme.HeaderBar);
            GUILayout.Label(title, UiTheme.HeaderTitle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Box("", UiTheme.Hr);
            GUILayout.Space(6);
        }

        // ── Multiplayer ─────────────────────────────────────────────────────
        private void DrawLobbyPanel(int id)
        {
            DrawWindowHeader("Multiplayer");

            var nm = NetworkManager.Instance;
            if (nm == null) { GUILayout.Label("NetworkManager not loaded."); return; }

            GUILayout.Label($"Player: {SteamFriends.GetPersonaName()}");
            SectionSpace();

            if (!nm.IsConnected)
                DrawConnectionSection(nm);
            else
                DrawLobbySection(nm);

            SectionSpace();
            DrawHitmarkerSettings();

            SectionSpace();
            GUILayout.Label("F8 show/hide · F9 host · F10 leave", UiTheme.Dim);
        }

        private void DrawConnectionSection(NetworkManager nm)
        {
            if (GUILayout.Button("Host Lobby  (F9)"))
                nm.HostLobby();

            SectionSpace();
            GUILayout.Label("— or join a friend's lobby —", UiTheme.Dim);

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
        }

        private void DrawLobbySection(NetworkManager nm)
        {
            GUILayout.Label($"Status: {(nm.IsHost ? "HOSTING" : "CONNECTED")}");
            GUILayout.Label($"Lobby ID: {nm.LobbyId}");

            if (nm.IsHost)
            {
                if (GUILayout.Button("Copy Lobby ID"))
                    GUIUtility.systemCopyBuffer = nm.LobbyId.ToString();
            }

            SectionSpace();
            GUILayout.Label("Players", UiTheme.Header);
            int count = SteamMatchmaking.GetNumLobbyMembers(nm.LobbyId);
            if (count == 0)
                GUILayout.Label("No one here yet.", UiTheme.Dim);
            else
                for (int i = 0; i < count; i++)
                {
                    CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(nm.LobbyId, i);
                    string self = member == SteamUser.GetSteamID() ? "  (you)" : "";
                    GUILayout.Label($"•  {SteamFriends.GetFriendPersonaName(member)}{self}");
                }

            SectionSpace();
            if (GUILayout.Button("Leave Lobby  (F10)"))
            {
                nm.LeaveLobby();
                _chatMessages.Clear();
            }
        }

        // ── Hitmarker settings ──────────────────────────────────────────────
        private void DrawHitmarkerSettings()
        {
            GUILayout.Label("Hitmarkers", UiTheme.Header);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Hitmarker.SoundEnabled ? "Sound: ON" : "Sound: OFF", GUILayout.Width(110)))
                Hitmarker.SoundEnabled = !Hitmarker.SoundEnabled;
            if (GUILayout.Button("Test", GUILayout.Width(50)))
                Hitmarker.Preview();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Volume", UiTheme.Dim, GUILayout.Width(60));
            float vol = GUILayout.HorizontalSlider(Hitmarker.Volume, 0f, 1f);
            GUILayout.Label($"{Mathf.RoundToInt(vol * 100f)}%", UiTheme.Dim, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(vol, Hitmarker.Volume))
            {
                Hitmarker.Volume = vol;
                Hitmarker.Preview(); // hear the level you're setting
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Number", UiTheme.Dim, GUILayout.Width(60));
            int size = Mathf.RoundToInt(GUILayout.HorizontalSlider(Hitmarker.NumberSize, 10f, 60f));
            GUILayout.Label($"{size}px", UiTheme.Dim, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            if (size != Hitmarker.NumberSize)
            {
                Hitmarker.NumberSize = size;
                Hitmarker.Preview(); // see the size you're setting
            }
        }

        // ── Scoreboard ──────────────────────────────────────────────────────
        private void DrawScoreboardPanel(int id)
        {
            DrawWindowHeader("Scoreboard");

            GUILayout.Label("PvP Combat", UiTheme.Header);
            var pvp = ScoreboardManager.GetPvPEntries();
            if (pvp.Count == 0)
                GUILayout.Label("No PvP data yet.", UiTheme.Dim);
            else
            {
                HeaderRow("Player", "K", "D", "K/D");
                foreach (var e in pvp)
                {
                    string kd = e.Deaths > 0
                        ? (e.Kills / (float)e.Deaths).ToString("0.00")
                        : e.Kills.ToString("0");
                    ValueRow(e.Name, e.Kills.ToString(), e.Deaths.ToString(), kd);
                }
            }

            SectionSpace();
            GUILayout.Label("Time Trial", UiTheme.Header);
            var tt = ScoreboardManager.Entries;
            if (tt.Count == 0)
            {
                GUILayout.Label("No finishes yet.");
                GUILayout.Label("Complete a time trial run to post a time!", UiTheme.Dim);
            }
            else
            {
                for (int i = 0; i < tt.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{i + 1}.", GUILayout.Width(28));
                    GUILayout.Label(tt[i].Name);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(ScoreboardManager.FormatTime(tt[i].TimeSeconds));
                    GUILayout.EndHorizontal();
                }
            }

            SectionSpace();
            if (GUILayout.Button("Clear"))
                ScoreboardManager.Clear();
        }

        private static void HeaderRow(string c1, string c2, string c3, string c4)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(c1, UiTheme.Dim, GUILayout.Width(150));
            GUILayout.Label(c2, UiTheme.Dim, GUILayout.Width(34));
            GUILayout.Label(c3, UiTheme.Dim, GUILayout.Width(34));
            GUILayout.Label(c4, UiTheme.Dim, GUILayout.Width(48));
            GUILayout.EndHorizontal();
        }

        private static void ValueRow(string name, string k, string d, string kd)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, GUILayout.Width(150));
            GUILayout.Label(k, GUILayout.Width(34));
            GUILayout.Label(d, GUILayout.Width(34));
            GUILayout.Label(kd, GUILayout.Width(48));
            GUILayout.EndHorizontal();
        }

        // ── Chat ────────────────────────────────────────────────────────────
        private void DrawChatPanel(int id)
        {
            DrawWindowHeader("Chat");

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
        }

        // ── PvP health bar (always visible) ─────────────────────────────────
        private void DrawPvPHealthBar()
        {
            LocalPlayerCombat combat = LocalPlayerCombat.EnsureAttached();
            if (combat == null) return;

            const int barWidth = 240;
            const int barHeight = 18;
            const int gap = 10;
            const int labelWidth = 110;
            const int marginBottom = 28;

            float hp = Mathf.Clamp(combat.Health, 0f, LocalPlayerCombat.MaxHealth);
            float frac = hp / LocalPlayerCombat.MaxHealth;

            int totalWidth = barWidth + gap + labelWidth;
            float x = (Screen.width - totalWidth) / 2f;
            float y = Screen.height - marginBottom - barHeight;

            Rect back = new Rect(x, y, barWidth, barHeight);
            UiTheme.DrawFrame(back, UiTheme.Border, UiTheme.Bg);

            float innerWidth = Mathf.Max(0f, (barWidth - 4f) * frac);
            Rect fill = new Rect(back.x + 2f, back.y + 2f, innerWidth, barHeight - 4f);
            Color hpColor = Color.Lerp(Color.red, Color.green, frac);
            UiTheme.DrawRect(fill, hpColor);

            Rect labelRect = new Rect(back.x + barWidth + gap, y, labelWidth, barHeight);
            GUI.Label(labelRect, $"HP {hp:F0}/{LocalPlayerCombat.MaxHealth:F0}");
        }

        private static void SectionSpace() => GUILayout.Space(8);

        // ── Public API (kept for compatibility; windows are permanent) ──────
        public void ShowScoreboard() { }
        public void ShowChat() { }

        public void ShowHostedLobby(CSteamID lobbyId)
        {
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
