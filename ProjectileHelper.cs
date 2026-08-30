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

        public static bool TrySnapshot(MonoBehaviour projectile, out LocalProjectileSnapshot snapshot)
        {
            snapshot = default;
            if (projectile == null || projectile.gameObject == null)
                return false;

            GameObject go = projectile.gameObject;
            if (!go.activeInHierarchy || go.name.Contains("RemoteProjectile_"))
                return false;

            var rb = go.GetComponent<Rigidbody>();
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
                damage = SafeFloatValue(gatProj, "damageAmount", "damage");
                if (damage <= 0f) damage = 12f;
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
                Velocity = rb != null ? rb.velocity : Vector3.zero,
                LifeSeconds = lifeSeconds,
                Kind = kind,
                Damage = damage,
            };
            return true;
        }

        public static float GetDamageFromCollider(Collider other)
        {
            if (other == null) return 0f;

            var baseProj = other.GetComponentInParent<Raulworks.RW_Base_Projectile>();
            if (baseProj != null)
                return 20f; // 30mm cannon (RW_Base_Projectile)

            if (other.GetComponentInParent<Raulworks.RW_Gat_Projectile>() != null)
                return 12f;

            var rocketProj = other.GetComponentInParent<Raulworks.RW_RocketProjectile>();
            if (rocketProj != null)
            {
                float damage = SafeFloatValue(rocketProj, "damageAmount", "damage", "explosionDamage");
                return damage > 0f ? damage : 50f;
            }

            return 0f;
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
