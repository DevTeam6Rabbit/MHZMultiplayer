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

        public static void EnsureHitboxVisual(Transform parent, BoxCollider hitbox, Color color)
        {
            if (parent == null || hitbox == null) return;

            Transform existing = parent.Find("PvPHitboxDebug");
            GameObject debug = existing != null ? existing.gameObject :
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            debug.name = "PvPHitboxDebug";
            if (existing == null)
                debug.transform.SetParent(parent, false);

            Collider debugCollider = debug.GetComponent<Collider>();
            if (debugCollider != null) Object.Destroy(debugCollider);

            debug.transform.localPosition = hitbox.center;
            debug.transform.localRotation = Quaternion.identity;
            debug.transform.localScale = LocalPlayerCombat.GetEffectiveHitboxSize(
                hitbox, RemoteProjectile.CollisionRadius);

            Renderer renderer = debug.GetComponent<Renderer>();
            Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Standard");
            if (renderer != null && shader != null)
            {
                renderer.material = new Material(shader);
                renderer.material.color = color;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            PvPHitboxDebugVisual visual = debug.GetComponent<PvPHitboxDebugVisual>();
            if (visual == null)
                visual = debug.AddComponent<PvPHitboxDebugVisual>();
            visual.Initialize(renderer);
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
