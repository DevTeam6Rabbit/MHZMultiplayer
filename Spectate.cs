using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // chase-cam spectating. we borrow Camera.main, park it behind the target's
    // ghost heli, and disable the game's own camera scripts so they stop
    // fighting us for the transform. everything is re-enabled on stop.
    public static class SpectateManager
    {
        public static bool IsSpectating { get; private set; }
        public static string TargetName { get; private set; } = "";

        private static CSteamID _target;
        private static readonly List<Behaviour> _disabledCams = new List<Behaviour>();
        private static SpectateCamera _driver;

        // the game's camera scripts, by type name (can't reference them directly)
        private static readonly string[] CameraScripts =
        {
            "RW_Camera_Manager", "RW_Advanced_HeliCamera", "RW_Base_HeliCamera",
            "RW_Basic_HeliCamera", "RW_Cockpit_HeliCamera",
        };

        public static void Start(CSteamID target)
        {
            var nm = NetworkManager.Instance;
            if (nm == null || !nm.RemotePlayers.ContainsKey(target)) return;
            if (Camera.main == null)
            {
                MultiplayerPlugin.Log.LogWarning("[Spectate] No main camera found.");
                return;
            }

            if (IsSpectating) Stop(); // switching targets: clean slate first

            // benched, not destroyed - Stop() flips these back on
            foreach (Behaviour b in Object.FindObjectsOfType<Behaviour>())
            {
                if (b == null || !b.enabled) continue;
                string tn = b.GetType().Name;
                foreach (string s in CameraScripts)
                    if (tn == s) { b.enabled = false; _disabledCams.Add(b); break; }
            }

            _target = target;
            TargetName = SteamFriends.GetFriendPersonaName(target);
            _driver = Camera.main.gameObject.AddComponent<SpectateCamera>();
            IsSpectating = true;

            LobbyUI.Instance?.AddChatMessage($"[Spectate] Watching {TargetName} - press Stop to return.");
            MultiplayerPlugin.Log.LogInfo($"[Spectate] Started on {TargetName}, disabled {_disabledCams.Count} camera script(s)");
        }

        public static void Stop()
        {
            if (_driver != null) Object.Destroy(_driver);
            _driver = null;

            foreach (Behaviour b in _disabledCams)
                if (b != null) b.enabled = true;
            _disabledCams.Clear();

            IsSpectating = false;
            TargetName = "";
        }

        // the ghost we're chasing, or null if they left / we stopped
        public static Transform TargetTransform()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return null;
            if (!nm.RemotePlayers.TryGetValue(_target, out RemotePlayer rp)) return null;
            return rp != null ? rp.transform : null;
        }
    }

    // lives on the main camera while spectating. LateUpdate so we win the
    // frame - position behind the target, look slightly above their heli.
    public class SpectateCamera : MonoBehaviour
    {
        private const float Distance = 18f;
        private const float Height = 6f;
        private const float FollowSpeed = 5f;

        private void LateUpdate()
        {
            Transform target = SpectateManager.TargetTransform();
            if (target == null)
            {
                // target left the lobby - bail out gracefully
                SpectateManager.Stop();
                return;
            }

            Vector3 wanted = target.position - target.forward * Distance + Vector3.up * Height;
            transform.position = Vector3.Lerp(transform.position, wanted, FollowSpeed * Time.deltaTime);
            transform.LookAt(target.position + Vector3.up * 2f);
        }
    }

    // "hide my heli" toggle - turns off every renderer on your own heli so
    // nothing blocks the view. purely local and purely visual: other players
    // still see your heli fine, physics and controls untouched. we keep
    // re-applying every second because the game re-enables renderers on
    // respawn and scene loads.
    public static class HeliVisibility
    {
        public static bool Hidden { get; private set; }
        private static readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private static float _nextReapply;

        public static void Toggle()
        {
            if (Hidden) Show(); else Hide();
        }

        private static void Hide()
        {
            Hidden = true;
            Apply();
            LobbyUI.Instance?.AddChatMessage("[View] Heli model hidden (only for you).");
        }

        private static void Show()
        {
            Hidden = false;
            foreach (Renderer r in _hiddenRenderers)
                if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();
            LobbyUI.Instance?.AddChatMessage("[View] Heli model visible again.");
        }

        // called from LobbyUI.Update - cheap no-op unless hidden
        public static void Tick()
        {
            if (!Hidden || Time.time < _nextReapply) return;
            _nextReapply = Time.time + 1f;
            Apply();
        }

        private static void Apply()
        {
            GameObject heli = HeliLocator.GetLocalHeli();
            if (heli == null) return;

            // renderers live all over the hierarchy, same walk-up as the ghost factory
            Transform root = heli.transform;
            while (root.parent != null && root.GetComponentsInChildren<Renderer>(true).Length == 0)
                root = root.parent;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                _hiddenRenderers.Add(r);
            }
        }
    }
}
