using System.Collections.Generic;
using UnityEngine;

namespace MHZombieMultiplayer
{
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
