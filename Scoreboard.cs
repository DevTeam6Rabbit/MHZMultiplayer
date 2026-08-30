using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Steamworks;

namespace MHZombieMultiplayer
{
    public static class ScoreboardManager
    {
        public struct PvPEntry
        {
            public string Name;
            public float DamageDealt;
            public float DamageTaken;
            public int Kills;
            public int Deaths;
        }

        public struct ScoreEntry
        {
            public string Name;
            public float TimeSeconds;
        }

        public static readonly List<ScoreEntry> Entries = new List<ScoreEntry>();
        public static readonly Dictionary<ulong, PvPEntry> PvPEntries = new Dictionary<ulong, PvPEntry>();

        public static void ReportDamageTaken(ulong attackerId, float damage, bool eliminated)
        {
            ulong localId = SteamUser.GetSteamID().m_SteamID;
            PvPEntry local = GetPvPEntry(localId);
            local.DamageTaken += damage;
            if (eliminated) local.Deaths++;
            PvPEntries[localId] = local;
        }

        public static void ReportDamageDealt(ulong targetId, float damage, bool eliminated)
        {
            ulong localId = SteamUser.GetSteamID().m_SteamID;
            PvPEntry local = GetPvPEntry(localId);
            local.DamageDealt += damage;
            if (eliminated) local.Kills++;
            PvPEntries[localId] = local;
        }

        public static List<PvPEntry> GetPvPEntries()
        {
            var entries = new List<PvPEntry>(PvPEntries.Values);
            entries.Sort((a, b) => b.Kills != a.Kills ? b.Kills.CompareTo(a.Kills) : b.DamageDealt.CompareTo(a.DamageDealt));
            return entries;
        }

        private static PvPEntry GetPvPEntry(ulong steamId)
        {
            if (PvPEntries.TryGetValue(steamId, out PvPEntry entry)) return entry;
            return new PvPEntry { Name = steamId == SteamUser.GetSteamID().m_SteamID ? SteamFriends.GetPersonaName() : SteamFriends.GetFriendPersonaName(new CSteamID(steamId)) };
        }

        public static void ReportLocalFinish(float timeSeconds)
        {
            string name = SteamFriends.GetPersonaName();
            AddEntry(name, timeSeconds);
            NetworkManager.Instance?.SendRaceFinish(timeSeconds);
            LobbyUI.Instance?.AddChatMessage($"[Race] {name} finished in {FormatTime(timeSeconds)}!");
            LobbyUI.Instance?.ShowScoreboard();
        }

        public static void ReportRemoteFinish(string name, float timeSeconds)
        {
            // if a spectator is watching this player, move them along
            SpectatorMode.Instance?.OnPlayerFinished(name);
            AddEntry(name, timeSeconds);
            LobbyUI.Instance?.AddChatMessage($"[Race] {name} finished in {FormatTime(timeSeconds)}!");
            LobbyUI.Instance?.ShowScoreboard();
        }

        // one row per player, best time only. drop the FindIndex block if
        // you ever want every run listed.
        private static void AddEntry(string name, float timeSeconds)
        {
            // Keep only each player's best time
            int existing = Entries.FindIndex(e => e.Name == name);
            if (existing >= 0)
            {
                if (Entries[existing].TimeSeconds <= timeSeconds) return; // not an improvement
                Entries.RemoveAt(existing);
            }

            Entries.Add(new ScoreEntry { Name = name, TimeSeconds = timeSeconds });
            Entries.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            if (Entries.Count > 20) Entries.RemoveAt(Entries.Count - 1);
        }

        public static void Clear()
        {
            Entries.Clear();
            PvPEntries.Clear();
        }

        public static string FormatTime(float seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}"
                : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}";
        }
    }

    // RW_Race.EndRound() writes the final time into endTime, that's our
    // hook. don't grab maxTime by accident - that's the par time.
    public static class TimeTrialHook
    {
        private static bool _installed;
        private static float _lastReportedAt = -999f;
        private static FieldInfo _endTimeField;
        private static FieldInfo _timeField;

        // game classes live in the Raulworks namespace, plain names return
        // null. name-scan fallback in case an update moves things around.
        public static Type FindGameType(string fullName, string simpleName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName);
                if (t != null) return t;
            }
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("Assembly-CSharp")) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (Type t in types)
                    if (t != null && t.Name == simpleName) return t;
            }
            return null;
        }

        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            Type raceType = FindGameType("Raulworks.RW_Race", "RW_Race");

            if (raceType == null)
            {
                MultiplayerPlugin.Log.LogWarning("[TimeTrialHook] RW_Race type not found - scoreboard auto-posting disabled.");
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _endTimeField = raceType.GetField("endTime", flags);
            _timeField    = raceType.GetField("time", flags);

            MethodInfo endRound = raceType.GetMethod("EndRound", flags);
            if (endRound == null)
            {
                MultiplayerPlugin.Log.LogWarning("[TimeTrialHook] RW_Race.EndRound not found - scoreboard auto-posting disabled.");
                return;
            }

            var harmony = new Harmony("com.mhzombie.multiplayer.timetrial");
            harmony.Patch(endRound, postfix: new HarmonyMethod(typeof(TimeTrialHook), nameof(OnRaceEnded)));
            MultiplayerPlugin.Log.LogInfo("[TimeTrialHook] Hooked RW_Race.EndRound.");
        }

        // Harmony postfix: runs right after RW_Race.EndRound()
        public static void OnRaceEnded(object __instance)
        {
            try
            {
                // Debounce in case EndRound fires more than once for a single run
                if (UnityEngine.Time.realtimeSinceStartup - _lastReportedAt < 5f) return;

                float finishTime = ReadFloat(_endTimeField, __instance);
                if (finishTime <= 0.5f)
                    finishTime = ReadFloat(_timeField, __instance); // fallback to the live timer

                if (finishTime <= 0.5f || finishTime >= 36000f)
                {
                    MultiplayerPlugin.Log.LogWarning($"[TimeTrialHook] EndRound fired but time looked implausible ({finishTime}).");
                    return;
                }

                _lastReportedAt = UnityEngine.Time.realtimeSinceStartup;
                MultiplayerPlugin.Log.LogInfo($"[TimeTrialHook] Race finished in {finishTime:F3}s");
                ScoreboardManager.ReportLocalFinish(finishTime);
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[TimeTrialHook] Error reading race result: {ex.Message}");
            }
        }

        private static float ReadFloat(FieldInfo field, object instance)
        {
            if (field == null || instance == null) return 0f;
            try { return Convert.ToSingle(field.GetValue(instance)); }
            catch { return 0f; }
        }
    }
}
