using System;
using System.Reflection;
using UnityEngine;

namespace MHZombieMultiplayer
{
    public enum ProjectileKind : byte
    {
        Base   = 0,
        Gat    = 1,
        Rocket = 2,
    }

    public static class ProjectileHelper
    {
        private static readonly FieldInfo BaseStartTime =
            typeof(Raulworks.RW_Base_Projectile).GetField("startTime",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo GatStartTime =
            typeof(Raulworks.RW_Gat_Projectile).GetField("startTime",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo RocketStartTime =
            typeof(Raulworks.RW_RocketProjectile).GetField("startTime",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // First-seen-per-instance logging so a short test produces a readable
        // [PvP-Snap]/[PvP-Hit] trail without spamming hundreds of lines.
        private static readonly System.Collections.Generic.HashSet<int> _loggedSnapshots =
            new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.HashSet<int> _loggedHits =
            new System.Collections.Generic.HashSet<int>();

        public static bool TrySnapshot(MonoBehaviour projectile, out LocalProjectileSnapshot snapshot)
        {
            snapshot = default;
            if (projectile == null || projectile.gameObject == null)
                return false;

            GameObject go = projectile.gameObject;
            if (!go.activeInHierarchy || go.name.Contains("RemoteProjectile_"))
                return false;

            Vector3 velocity = ReadProjectileVelocity(projectile, go);
            ProjectileKind kind;
            float lifeSeconds;
            float damage;

            if (projectile is Raulworks.RW_Base_Projectile baseProj)
            {
                kind = ProjectileKind.Base;
                lifeSeconds = RemainingLife(baseProj.timeoutTime, BaseStartTime, baseProj);
                // The 30mm cannon fires this projectile; it reads ~50 from the
                // game, but we define its PvP damage here at 20.
                damage = 20f;
            }
            else if (projectile is Raulworks.RW_Gat_Projectile gatProj)
            {
                kind = ProjectileKind.Gat;
                lifeSeconds = RemainingLife(2f, GatStartTime, gatProj);
                // 7.62 minigun bullets have no damage field of their own, so we
                // define their PvP damage here.
                damage = 10f;
            }
            else if (projectile is Raulworks.RW_RocketProjectile rocketProj)
            {
                kind = ProjectileKind.Rocket;
                lifeSeconds = RemainingLife(ReadFloatValue(rocketProj, "timeoutTime", "lifeTime", "timeout"), RocketStartTime, rocketProj);
                damage = SafeFloatValue(rocketProj, "damageAmount", "damage", "explosionDamage");
                if (damage <= 0f) damage = 50f;
            }
            else
            {
                return false;
            }

            snapshot = new LocalProjectileSnapshot
            {
                InstanceId = go.GetInstanceID(),
                Position = go.transform.position,
                Rotation = go.transform.rotation,
                Velocity = velocity,
                LifeSeconds = lifeSeconds,
                Kind = kind,
                Damage = damage,
            };

            if (_loggedSnapshots.Add(go.GetInstanceID()))
                MultiplayerPlugin.Log.LogInfo($"[PvP-Snap] {go.name} kind={kind} dmg={damage:F0} speed={velocity.magnitude:F1} life={lifeSeconds:F2}");
            return true;
        }

        // The Rigidbody is normally on the projectile root, but the gun can
        // wire it up via the serialized `rb` field instead. Try both so remote
        // 7.62 visuals get the real velocity and travel to the target rather
        // than sitting still at the muzzle (invisible + never reaching you).
        private static Vector3 ReadProjectileVelocity(MonoBehaviour projectile, GameObject go)
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                rb = go.GetComponentInChildren<Rigidbody>();
            if (rb == null)
            {
                try
                {
                    FieldInfo field = projectile.GetType().GetField("rb",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && field.GetValue(projectile) is Rigidbody fieldRb)
                        rb = fieldRb;
                }
                catch { /* reflection only; fall through */ }
            }

            Vector3 vel = rb != null ? rb.velocity : Vector3.zero;
            if (vel.sqrMagnitude > 0.001f)
                return vel;

            // At the instant of fire the Rigidbody may not have velocity yet.
            // Derive it from the projectile's own speed field + facing direction
            // so remote bullets always travel forward and actually reach you.
            float speed = SafeFloatValue(projectile, "projectileSpeed");
            if (speed > 0f)
                return projectile.transform.forward * speed;

            return vel;
        }

        public static float GetDamageFromCollider(Collider other)
        {
            if (other == null) return 0f;

            var baseProj = other.GetComponentInParent<Raulworks.RW_Base_Projectile>();
            if (baseProj != null)
            {
                LogHit(other, "Base", 20f);
                return 20f; // 30mm cannon (RW_Base_Projectile)
            }

            if (other.GetComponentInParent<Raulworks.RW_Gat_Projectile>() != null)
            {
                LogHit(other, "Gat", 10f);
                return 10f; // 7.62 minigun (RW_Gat_Projectile)
            }

            var rocketProj = other.GetComponentInParent<Raulworks.RW_RocketProjectile>();
            if (rocketProj != null)
            {
                float damage = SafeFloatValue(rocketProj, "damageAmount", "damage", "explosionDamage");
                if (damage <= 0f) damage = 50f;
                LogHit(other, "Rocket", damage);
                return damage;
            }

            return 0f;
        }

        private static void LogHit(Collider other, string kindName, float damage)
        {
            if (other == null || !_loggedHits.Add(other.GetInstanceID())) return;
            MultiplayerPlugin.Log.LogInfo($"[PvP-Hit] {other.name} -> {kindName} dmg={damage:F0}");
        }

        public static int GetProjectileInstanceId(Collider other)
        {
            if (other == null) return 0;

            Component proj = other.GetComponentInParent<Raulworks.RW_Base_Projectile>()
                ?? (Component)other.GetComponentInParent<Raulworks.RW_Gat_Projectile>()
                ?? other.GetComponentInParent<Raulworks.RW_RocketProjectile>();

            return proj != null ? proj.gameObject.GetInstanceID() : other.gameObject.GetInstanceID();
        }

        public static bool IsGameProjectile(Collider other)
        {
            if (other == null) return false;
            if (other.GetComponentInParent<Raulworks.RW_Base_Projectile>() != null) return true;
            if (other.GetComponentInParent<Raulworks.RW_Gat_Projectile>() != null) return true;
            if (other.GetComponentInParent<Raulworks.RW_RocketProjectile>() != null) return true;
            return false;
        }

        private static float RemainingLife(float timeout, FieldInfo startTimeField, object instance)
        {
            if (timeout <= 0f) timeout = 2f;
            float start = ReadStartTime(startTimeField, instance);
            return Mathf.Max(0.05f, timeout - (Time.time - start));
        }

        private static float ReadStartTime(FieldInfo field, object instance)
        {
            if (field == null || instance == null) return Time.time;
            try
            {
                object value = field.GetValue(instance);
                if (value is float f) return f;
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[ProjectileHelper] startTime read failed: {ex.Message}");
            }
            return Time.time;
        }

        private static float SafeAverageDamage(object instance, string minName, string maxName, string fallbackName)
        {
            float min = SafeFloatValue(instance, minName, fallbackName);
            float max = SafeFloatValue(instance, maxName, fallbackName);
            if (min > 0f && max > 0f)
                return (min + max) * 0.5f;
            if (min > 0f) return min;
            if (max > 0f) return max;
            return SafeFloatValue(instance, fallbackName, "damage");
        }

        private static float SafeFloatValue(object instance, params string[] names)
        {
            if (instance == null) return 0f;
            Type type = instance.GetType();

            foreach (string name in names)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    object value = field.GetValue(instance);
                    if (value is float f) return f;
                    if (value is int i) return i;
                    if (value is double d) return (float)d;
                    if (value is long l) return l;
                    if (value is short s) return s;
                }

                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    object value = property.GetValue(instance, null);
                    if (value is float f) return f;
                    if (value is int i) return i;
                    if (value is double d) return (float)d;
                    if (value is long l) return l;
                    if (value is short s) return s;
                }
            }

            return 0f;
        }

        private static float ReadFloatValue(object instance, params string[] names)
        {
            return SafeFloatValue(instance, names);
        }
    }
}
