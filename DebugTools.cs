using UnityEngine;

namespace MHZombieMultiplayer
{
    // Client-local test setting. It is intentionally not sent over the network,
    // so each player can choose whether debug visuals are shown on their screen.
    public static class DebugTools
    {
        public static bool Enabled { get; private set; }

        public static void Toggle()
        {
            SetEnabled(!Enabled);
        }

        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;

            foreach (PvPHitboxDebugVisual visual in
                Object.FindObjectsOfType<PvPHitboxDebugVisual>())
                visual.SetVisible(enabled);

            if (!enabled)
                ProjectileTraceDebug.Clear();

            MultiplayerPlugin.Log.LogInfo($"[DebugTools] Hitboxes and projectile traces {(enabled ? "enabled" : "disabled")}.");
        }
    }

    // Keep this component active while hiding only its renderer. Unity's normal
    // scene search can then find it again when the lobby button is switched on.
    public sealed class PvPHitboxDebugVisual : MonoBehaviour
    {
        private Renderer _renderer;

        public void Initialize(Renderer target)
        {
            _renderer = target;
            SetVisible(DebugTools.Enabled);
        }

        public void SetVisible(bool visible)
        {
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _renderer.enabled = visible;
        }
    }
}
