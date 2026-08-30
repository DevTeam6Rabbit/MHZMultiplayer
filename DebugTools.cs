using System.Collections.Generic;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // Client-local test setting. It is intentionally not sent over the network,
    // so each player can choose whether debug visuals are shown on their screen.
    public static class DebugTools
    {
        private static readonly HashSet<PvPHitboxDebugVisual> HitboxVisuals =
            new HashSet<PvPHitboxDebugVisual>();

        public static bool Enabled { get; private set; }

        public static void Toggle()
        {
            SetEnabled(!Enabled);
        }

        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;

            var visuals = new List<PvPHitboxDebugVisual>(HitboxVisuals);
            foreach (PvPHitboxDebugVisual visual in visuals)
            {
                if (visual != null)
                    visual.SetVisible(enabled);
                else
                    HitboxVisuals.Remove(visual);
            }

            if (!enabled)
                ProjectileTraceDebug.Clear();

            MultiplayerPlugin.Log.LogInfo($"[DebugTools] Hitboxes and projectile traces {(enabled ? "enabled" : "disabled")}.");
        }

        internal static void Register(PvPHitboxDebugVisual visual)
        {
            if (visual != null)
                HitboxVisuals.Add(visual);
        }

        internal static void Unregister(PvPHitboxDebugVisual visual)
        {
            HitboxVisuals.Remove(visual);
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

        public static void EnsureCompoundHitboxVisual(Transform parent, Vector3 bodyCenter,
            Vector3 bodySize, Vector3 tailCenter, Vector3 tailSize, Color color)
        {
            EnsureLocalShapeVisual(parent, "PvPBodyHitboxDebug", PrimitiveType.Sphere,
                bodyCenter, bodySize, color);
            EnsureLocalShapeVisual(parent, "PvPTailHitboxDebug", PrimitiveType.Cube,
                tailCenter, tailSize, color);
        }

        private static void EnsureLocalShapeVisual(Transform parent, string name,
            PrimitiveType primitive, Vector3 center, Vector3 size, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject debug = existing != null ? existing.gameObject :
                GameObject.CreatePrimitive(primitive);
            debug.name = name;
            if (existing == null)
                debug.transform.SetParent(parent, false);

            Collider debugCollider = debug.GetComponent<Collider>();
            if (debugCollider != null) Object.Destroy(debugCollider);
            debug.transform.localPosition = center;
            debug.transform.localRotation = Quaternion.identity;
            debug.transform.localScale = size;

            Renderer renderer = debug.GetComponent<Renderer>();
            ConfigureDebugRenderer(renderer, color);
            PvPHitboxDebugVisual visual = debug.GetComponent<PvPHitboxDebugVisual>();
            if (visual == null)
                visual = debug.AddComponent<PvPHitboxDebugVisual>();
            visual.Initialize(renderer);
        }

        public static void UpdateReportedHitboxVisual(Transform owner, Vector3 worldCenter,
            Quaternion worldRotation, Vector3 worldSize, Vector3 tailWorldCenter,
            Vector3 tailWorldSize)
        {
            if (owner == null) return;

            UpdateReportedShapeVisual(owner, "ReportedPvPBodyHitboxDebug", PrimitiveType.Sphere,
                worldCenter, worldRotation, worldSize);
            UpdateReportedShapeVisual(owner, "ReportedPvPTailHitboxDebug", PrimitiveType.Cube,
                tailWorldCenter, worldRotation, tailWorldSize);
        }

        private static void UpdateReportedShapeVisual(Transform owner, string name,
            PrimitiveType primitive, Vector3 worldCenter, Quaternion worldRotation, Vector3 worldSize)
        {
            Transform existing = owner.Find(name);
            GameObject debug;
            PvPHitboxDebugVisual visual;
            if (existing == null)
            {
                debug = GameObject.CreatePrimitive(primitive);
                debug.name = name;
                debug.transform.SetParent(owner, false);

                Collider debugCollider = debug.GetComponent<Collider>();
                if (debugCollider != null) Object.Destroy(debugCollider);

                Renderer renderer = debug.GetComponent<Renderer>();
                ConfigureDebugRenderer(renderer, new Color(0f, 1f, 1f, 0.18f));

                visual = debug.AddComponent<PvPHitboxDebugVisual>();
                visual.Initialize(renderer);
            }
            else
            {
                debug = existing.gameObject;
                visual = debug.GetComponent<PvPHitboxDebugVisual>();
                if (visual == null)
                {
                    visual = debug.AddComponent<PvPHitboxDebugVisual>();
                    visual.Initialize(debug.GetComponent<Renderer>());
                }
            }

            visual.SetReportedWorldPose(worldCenter, worldRotation, worldSize);
        }

        private static void ConfigureDebugRenderer(Renderer renderer, Color color)
        {
            Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Standard");
            if (renderer == null || shader == null) return;
            renderer.material = new Material(shader);
            renderer.material.color = color;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    // Keep this component active while hiding only its renderer. Unity's normal
    // scene search can then find it again when the lobby button is switched on.
    public sealed class PvPHitboxDebugVisual : MonoBehaviour
    {
        private Renderer _renderer;
        private bool _holdReportedWorldPose;
        private Vector3 _reportedWorldCenter;
        private Quaternion _reportedWorldRotation;
        private Vector3 _reportedWorldSize;

        public void Initialize(Renderer target)
        {
            _renderer = target;
            DebugTools.Register(this);
            SetVisible(DebugTools.Enabled);
        }

        public void SetVisible(bool visible)
        {
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _renderer.enabled = visible;
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

        public void SetReportedWorldPose(Vector3 center, Quaternion rotation, Vector3 size)
        {
            _holdReportedWorldPose = true;
            _reportedWorldCenter = center;
            _reportedWorldRotation = rotation;
            _reportedWorldSize = size;
            ApplyReportedWorldPose();
        }

        private void LateUpdate()
        {
            // The reported box is parented to the ghost for lifecycle cleanup,
            // but it must not inherit the ghost's interpolation/extrapolation.
            if (_holdReportedWorldPose)
                ApplyReportedWorldPose();
        }

        private void OnDestroy()
        {
            DebugTools.Unregister(this);
        }

        private void ApplyReportedWorldPose()
        {
            transform.position = _reportedWorldCenter;
            transform.rotation = _reportedWorldRotation;

            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(
                _reportedWorldSize.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                _reportedWorldSize.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                _reportedWorldSize.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
        }
    }
}
