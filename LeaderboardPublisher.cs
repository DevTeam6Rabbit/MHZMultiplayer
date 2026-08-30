using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // Writes time trial results to a json file and, if a publish token is
    // present, pushes them to the website's leaderboard.json.
    //
    // The token is NOT in this code and never ships with the mod. Only the
    // person running the tournament drops a file next to the game exe:
    //   mhz_publish.txt   line 1: github token   line 2: owner/repo (optional)
    // Everyone else just gets the local json file, which they can send you.
    public static class LeaderboardPublisher
    {
        const string DefaultRepo = "DevTeam6Rabbit/MHZMultiplayer";
        const string Branch = "gh-pages";
        const string PathInRepo = "leaderboard.json";

        static string _token;
        static string _repo = DefaultRepo;
        static bool _checkedForToken;
        static float _nextAllowedPush;

        public static string LocalFilePath =>
            Path.Combine(Application.dataPath ?? ".", "..\\leaderboard.json");

        // called after any finish (local or remote) once the entry is stored
        public static void OnResultsChanged()
        {
            try
            {
                string json = BuildJson();
                WriteLocal(json);

                if (!HasToken()) return;
                if (Time.realtimeSinceStartup < _nextAllowedPush) return; // don't spam the api
                _nextAllowedPush = Time.realtimeSinceStartup + 20f;

                MultiplayerPlugin.Instance?.StartCoroutine(PushToGitHub(json));
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[Leaderboard] Could not save results: {ex.Message}");
            }
        }

        static string BuildJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"updated\": \"")
              .Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
              .Append("\",\n  \"results\": [\n");

            List<ScoreboardManager.ScoreEntry> entries = ScoreboardManager.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append("    {\"name\": \"").Append(Escape(entries[i].Name))
                  .Append("\", \"time\": ")
                  .Append(entries[i].TimeSeconds.ToString("F3", CultureInfo.InvariantCulture))
                  .Append("}");
                if (i < entries.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", " ").Replace("\r", " ");
        }

        static void WriteLocal(string json)
        {
            string path = Path.GetFullPath(LocalFilePath);
            File.WriteAllText(path, json);
        }

        static bool HasToken()
        {
            if (_checkedForToken) return _token != null;
            _checkedForToken = true;

            try
            {
                string file = Path.GetFullPath(Path.Combine(Application.dataPath ?? ".", "..\\mhz_publish.txt"));
                if (!File.Exists(file))
                {
                    MultiplayerPlugin.Log.LogInfo($"[Leaderboard] Results saved locally only ({Path.GetFullPath(LocalFilePath)}). Drop mhz_publish.txt next to the game exe to publish to the website.");
                    return false;
                }

                string[] lines = File.ReadAllLines(file);
                if (lines.Length > 0 && lines[0].Trim().Length > 0) _token = lines[0].Trim();
                if (lines.Length > 1 && lines[1].Trim().Length > 0) _repo = lines[1].Trim();
                MultiplayerPlugin.Log.LogInfo($"[Leaderboard] Publish token found - results will go to {_repo}");
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[Leaderboard] Token read failed: {ex.Message}");
            }
            return _token != null;
        }

        // GitHub contents API: needs the current file sha to update it
        static System.Collections.IEnumerator PushToGitHub(string json)
        {
            string url = $"https://api.github.com/repos/{_repo}/contents/{PathInRepo}?ref={Branch}";

            var get = new WWW(url, null, AuthHeaders());
            yield return get;

            string sha = null;
            if (string.IsNullOrEmpty(get.error))
                sha = ExtractJsonString(get.text, "sha");
            else
                MultiplayerPlugin.Log.LogWarning($"[Leaderboard] Could not read current file: {get.error}");

            string body = "{\"message\":\"Update time trial leaderboard\",\"branch\":\"" + Branch +
                          "\",\"content\":\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) + "\"" +
                          (sha != null ? ",\"sha\":\"" + sha + "\"" : "") + "}";

            var headers = AuthHeaders();
            headers["X-HTTP-Method-Override"] = "PUT";
            headers["Content-Type"] = "application/json";

            var put = new WWW($"https://api.github.com/repos/{_repo}/contents/{PathInRepo}",
                              Encoding.UTF8.GetBytes(body), headers);
            yield return put;

            if (string.IsNullOrEmpty(put.error))
                MultiplayerPlugin.Log.LogInfo("[Leaderboard] Published to the website.");
            else
                MultiplayerPlugin.Log.LogWarning($"[Leaderboard] Publish failed: {put.error}");
        }

        static Dictionary<string, string> AuthHeaders()
        {
            return new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + _token },
                { "Accept", "application/vnd.github+json" },
                { "User-Agent", "MHZMultiplayer" },
            };
        }

        static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string needle = "\"" + key + "\":\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            int end = json.IndexOf('"', i);
            return end > i ? json.Substring(i, end - i) : null;
        }
    }
}
