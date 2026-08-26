using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Steamworks;

namespace MHZombieMultiplayer
{
    /// <summary>
    /// Session scoreboard for time trial results. Keeps each player's best
    /// time, sorted fastest first. Local finishes are broadcast to the lobby.
    /// </summary>
    public static class ScoreboardManager
    {
        public struct ScoreEntry
        {
            public string Name;
            public float TimeSeconds;
        }

        public static readonly List<ScoreEntry> Entries = new List<ScoreEntry>();

        /// <summary>Called when the LOCAL player finishes a run.</summary>
        public static void ReportLocalFinish(float timeSeconds)
        {
            string name = SteamFriends.GetPersonaName();
            AddEntry(name, timeSeconds);
            NetworkManager.Instance?.SendRaceFinish(timeSeconds);
            LobbyUI.Instance?.AddChatMessage($"[Race] {name} finished in {FormatTime(timeSeconds)}!");
            LobbyUI.Instance?.ShowScoreboard();
        }

        /// <summary>Called when a REMOTE player's finish packet arrives.</summary>
        public static void ReportRemoteFinish(string name, float timeSeconds)
        {
            AddEntry(name, timeSeconds);
            LobbyUI.Instance?.AddChatMessage($"[Race] {name} finished in {FormatTime(timeSeconds)}!");
            LobbyUI.Instance?.ShowScoreboard();
        }

        // one row per player, best time wins. a slower run never replaces a
        // faster one - if you want a full run history instead, delete the
        // FindIndex block below and every finish becomes its own row.
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

        public static void Clear() => Entries.Clear();

        public static string FormatTime(float seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}"
                : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}";
        }
    }

    /// <summary>
    /// Hooks the game's race controller to capture time trial results.
    ///
    /// Verified against the game's Assembly-CSharp: the RW_Race component runs
    /// races. Its "time" field is the live timer (reset in OnEnable), and
    /// EndRound() copies it into "endTime" when the run completes - so we
    /// postfix EndRound and read endTime for the final result.
    /// </summary>
    // How we know a race finished: the game's RW_Race.EndRound() copies the
    // live timer field ("time") into "endTime" when a run completes, so we
    // harmony-postfix EndRound and read endTime. Careful if you touch this:
    // the same class also has maxTime (the par time), which is NOT the
    // player's result.
    public static class TimeTrialHook
    {
        private static bool _installed;
        private static float _lastReportedAt = -999f;
        private static FieldInfo _endTimeField;
        private static FieldInfo _timeField;

        /// <summary>Finds a game type by full name, falling back to a scan by simple name.</summary>
        // Every game class lives in the "Raulworks" namespace, so lookups
        // need the full name: asm.GetType("RW_Race") returns null, it has to
        // be "Raulworks.RW_Race". The fallback scan by simple name is there so
        // a game update moving things around doesn't silently kill the mod.
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
