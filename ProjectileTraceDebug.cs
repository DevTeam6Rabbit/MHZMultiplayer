using System.Collections.Generic;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // Test-branch visualization for the trajectory this client sends and the
    // trajectory it simulates for remote shots. Traces are separate objects so
    // they remain visible briefly after a projectile hits or is destroyed.
    public static class ProjectileTraceDebug
    {
        private const float TraceLifetime = 5f;
        private static readonly Dictionary<string, ProjectileTraceLine> Traces =
            new Dictionary<string, ProjectileTraceLine>();

        public static void RecordLocal(LocalProjectileSnapshot shot)
        {
            if (!DebugTools.Enabled) return;

            string key = "local:" + shot.InstanceId;
            ProjectileTraceLine trace = GetOrCreate(key, shot.Kind, false);
            trace.Append(shot.Position);

            // Gun packets are emitted once rather than updated every frame.
            // Draw the same velocity/lifetime path the receiving client uses.
            if (shot.Kind != ProjectileKind.Rocket && shot.Velocity.sqrMagnitude > 0.01f)
                trace.Append(shot.Position + shot.Velocity * Mathf.Max(0f, shot.LifeSeconds));
        }

        public static void BeginRemote(ulong steamId, int instanceId, ProjectileKind kind, Vector3 position)
        {
            if (!DebugTools.Enabled) return;
            GetOrCreate(RemoteKey(steamId, instanceId), kind, true).Append(position);
        }

        public static void RecordRemote(ulong steamId, int instanceId, ProjectileKind kind, Vector3 position)
        {
            if (!DebugTools.Enabled) return;
            GetOrCreate(RemoteKey(steamId, instanceId), kind, true).Append(position);
        }

        public static void Clear()
        {
            var active = new List<ProjectileTraceLine>(Traces.Values);
            Traces.Clear();
            foreach (ProjectileTraceLine trace in active)
                if (trace != null)
                    Object.Destroy(trace.gameObject);
        }

        internal static void Forget(string key, ProjectileTraceLine trace)
        {
            if (Traces.TryGetValue(key, out ProjectileTraceLine current) && current == trace)
                Traces.Remove(key);
        }

        private static string RemoteKey(ulong steamId, int instanceId)
        {
            return "remote:" + steamId + ":" + instanceId;
        }

        private static ProjectileTraceLine GetOrCreate(string key, ProjectileKind kind, bool remote)
        {
            if (Traces.TryGetValue(key, out ProjectileTraceLine trace) && trace != null)
                return trace;

            GameObject go = new GameObject("PvP_ProjectileTrace_" + key);
            Object.DontDestroyOnLoad(go);
            trace = go.AddComponent<ProjectileTraceLine>();
            trace.Initialize(key, TraceColor(kind, remote), kind == ProjectileKind.Rocket ? 0.14f : 0.07f,
                TraceLifetime);
            Traces[key] = trace;
            return trace;
        }

        private static Color TraceColor(ProjectileKind kind, bool remote)
        {
            if (remote)
            {
                if (kind == ProjectileKind.Rocket) return new Color(1f, 0.1f, 0.05f, 0.95f);
                if (kind == ProjectileKind.Gat) return new Color(1f, 0.65f, 0.05f, 0.95f);
                return new Color(1f, 0.25f, 0.05f, 0.95f);
            }

            if (kind == ProjectileKind.Rocket) return new Color(0.1f, 0.45f, 1f, 0.95f);
            if (kind == ProjectileKind.Gat) return new Color(0.1f, 1f, 0.35f, 0.95f);
            return new Color(0.05f, 0.9f, 1f, 0.95f);
        }
    }

    public sealed class ProjectileTraceLine : MonoBehaviour
    {
        private const float MinimumPointDistanceSquared = 0.0025f;
        private string _key;
        private LineRenderer _line;
        private float _lifetime;
        private float _expiresAt;
        private Vector3 _lastPoint;
        private bool _hasPoint;

        public void Initialize(string key, Color color, float width, float lifetime)
        {
            _key = key;
            _lifetime = lifetime;
            _expiresAt = Time.time + lifetime;
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 0;
            _line.startWidth = width;
            _line.endWidth = width * 0.35f;
            _line.startColor = color;
            _line.endColor = new Color(color.r, color.g, color.b, 0.4f);

            Shader shader = Shader.Find("Sprites/Default") ??
                            Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                            Shader.Find("Standard");
            if (shader != null)
            {
                _line.material = new Material(shader);
                if (_line.material.HasProperty("_Color")) _line.material.color = color;
            }
        }

        public void Append(Vector3 point)
        {
            _expiresAt = Time.time + _lifetime;
            if (_hasPoint && (point - _lastPoint).sqrMagnitude < MinimumPointDistanceSquared)
                return;

            int index = _line.positionCount;
            _line.positionCount = index + 1;
            _line.SetPosition(index, point);
            _lastPoint = point;
            _hasPoint = true;
        }

        private void Update()
        {
            if (Time.time >= _expiresAt)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            ProjectileTraceDebug.Forget(_key, this);
        }
    }
}
