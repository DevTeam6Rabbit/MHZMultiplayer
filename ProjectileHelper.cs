using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
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
        // PvP balance values live here so local collision damage and networked
        // projectile damage cannot drift apart.
        public const float ThirtyMmDamage = 20f;
        public const float SevenSixTwoDamage = 10f;
        public const float DefaultRocketDamage = 50f;

        // MH-Zombie pools and reuses projectile GameObjects. GetInstanceID()
        // therefore identifies the pooled object, not an individual shot. Keep
        // one network ID per activation (startTime changes in OnEnable) so a new
        // 7.62 round cannot be mistaken for an update to an earlier round.
        private sealed class ProjectileActivation
        {
            public float StartTime;
            public int NetworkId;
        }

        private static readonly Dictionary<int, ProjectileActivation> ProjectileActivations =
            new Dictionary<int, ProjectileActivation>();
        private static readonly HashSet<int> GunProjectileObjects = new HashSet<int>();
        private static int _nextNetworkProjectileId;

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

        private static readonly FieldInfo GatlingLastTube =
            typeof(Raulworks.RW_Gatling_Gun).GetField("lastTube",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo GatlingCurrentProjectile =
            typeof(Raulworks.RW_Gatling_Gun).GetField("currentProjectile",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo GatlingProjectileObject1 =
            typeof(Raulworks.RW_Gatling_Gun).GetField("projectileObj1",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo GatlingProjectileObject2 =
            typeof(Raulworks.RW_Gatling_Gun).GetField("projectileObj2",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static float GetDamageForKind(ProjectileKind kind)
        {
            switch (kind)
            {
                case ProjectileKind.Base: return ThirtyMmDamage;
                case ProjectileKind.Gat: return SevenSixTwoDamage;
                case ProjectileKind.Rocket: return DefaultRocketDamage;
                default: return 0f;
            }
        }

        // Gun rounds are emitted from RW_Gatling_Gun itself rather than inferred
        // from its pooled projectile objects. This gives every trigger event a
        // unique shot and lets the weapon's thirtyMM flag authoritatively select
        // 30mm versus 7.62 ammunition.
        public static bool TryCreateGunShot(Raulworks.RW_Gatling_Gun gun, out LocalProjectileSnapshot snapshot)
        {
            snapshot = default;
            if (gun == null || gun.gameObject == null || !gun.gameObject.activeInHierarchy)
                return false;

            // Remember the actual pooled objects created by this gun. Some gun
            // prefabs also contain a rocket component; those must not enter the
            // separate rocket-launcher network path.
            RegisterGunProjectile(ReadObjectField<GameObject>(GatlingProjectileObject1, gun));
            RegisterGunProjectile(ReadObjectField<GameObject>(GatlingProjectileObject2, gun));

            bool thirtyMm = gun.thirtyMM;
            if (!thirtyMm && ReadObjectField<GameObject>(GatlingCurrentProjectile, gun) != gun.projectile)
                return false; // Laser mode is not a 7.62 projectile.

            float lastTube = ReadFloatField(GatlingLastTube, gun);
            Transform muzzle = lastTube > 0.5f ? gun.muzzlePos : gun.secondMuzzlePos;
            if (muzzle == null)
                muzzle = gun.muzzlePos != null ? gun.muzzlePos : gun.transform;

            ProjectileKind kind = thirtyMm ? ProjectileKind.Base : ProjectileKind.Gat;
            snapshot = new LocalProjectileSnapshot
            {
                InstanceId = NextNetworkProjectileId(),
                Position = muzzle.position,
                Rotation = muzzle.rotation,
                Velocity = muzzle.forward * 200f,
                LifeSeconds = thirtyMm ? 8f : 2f,
                Kind = kind,
                Damage = GetDamageForKind(kind),
            };
            return true;
        }

        public static bool TrySnapshot(MonoBehaviour projectile, out LocalProjectileSnapshot snapshot)
        {
            snapshot = default;
            if (projectile == null || projectile.gameObject == null)
                return false;

            GameObject go = projectile.gameObject;
            if (!go.activeInHierarchy || go.name.Contains("RemoteProjectile_"))
                return false;
            if (GunProjectileObjects.Contains(go.GetInstanceID()))
                return false;

            Vector3 velocity = ReadProjectileVelocity(projectile, go);
            ProjectileKind kind;
            float lifeSeconds;
            float damage;
            float activationTime;

            if (projectile is Raulworks.RW_Base_Projectile baseProj)
            {
                kind = ProjectileKind.Base;
                activationTime = ReadStartTime(BaseStartTime, baseProj);
                lifeSeconds = RemainingLife(baseProj.timeoutTime, activationTime);
                // The 30mm cannon fires this projectile; it reads ~50 from the
                // game, but we define its PvP damage here at 20.
                damage = ThirtyMmDamage;
            }
            else if (projectile is Raulworks.RW_Gat_Projectile gatProj)
            {
                kind = ProjectileKind.Gat;
                activationTime = ReadStartTime(GatStartTime, gatProj);
                lifeSeconds = RemainingLife(2f, activationTime);
                // 7.62 minigun bullets have no damage field of their own, so we
                // define their PvP damage here.
                damage = SevenSixTwoDamage;
            }
            else if (projectile is Raulworks.RW_RocketProjectile rocketProj)
            {
                kind = ProjectileKind.Rocket;
                activationTime = ReadStartTime(RocketStartTime, rocketProj);
                lifeSeconds = RemainingLife(ReadFloatValue(rocketProj, "timeoutTime", "lifeTime", "timeout"), activationTime);
                damage = SafeFloatValue(rocketProj, "damageAmount", "damage", "explosionDamage");
                if (damage <= 0f) damage = DefaultRocketDamage;
            }
            else
            {
                return false;
            }

            snapshot = new LocalProjectileSnapshot
            {
                InstanceId = GetNetworkProjectileId(go.GetInstanceID(), activationTime),
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
                LogHit(other, "Base", ThirtyMmDamage);
                return ThirtyMmDamage; // 30mm cannon (RW_Base_Projectile)
            }

            if (other.GetComponentInParent<Raulworks.RW_Gat_Projectile>() != null)
            {
                LogHit(other, "Gat", SevenSixTwoDamage);
                return SevenSixTwoDamage; // 7.62 minigun (RW_Gat_Projectile)
            }

            var rocketProj = other.GetComponentInParent<Raulworks.RW_RocketProjectile>();
            if (rocketProj != null)
            {
                float damage = SafeFloatValue(rocketProj, "damageAmount", "damage", "explosionDamage");
                if (damage <= 0f) damage = DefaultRocketDamage;
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

        private static int GetNetworkProjectileId(int unityInstanceId, float activationTime)
        {
            if (ProjectileActivations.TryGetValue(unityInstanceId, out ProjectileActivation activation) &&
                activation.StartTime == activationTime)
                return activation.NetworkId;

            int networkId = NextNetworkProjectileId();
            ProjectileActivations[unityInstanceId] = new ProjectileActivation
            {
                StartTime = activationTime,
                NetworkId = networkId,
            };
            return networkId;
        }

        private static int NextNetworkProjectileId()
        {
            int networkId = Interlocked.Increment(ref _nextNetworkProjectileId);
            if (networkId <= 0)
            {
                Interlocked.Exchange(ref _nextNetworkProjectileId, 1);
                networkId = 1;
            }
            return networkId;
        }

        private static float RemainingLife(float timeout, float startTime)
        {
            if (timeout <= 0f) timeout = 2f;
            return Mathf.Max(0.05f, timeout - (Time.time - startTime));
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

        private static float ReadFloatField(FieldInfo field, object instance)
        {
            if (field == null || instance == null) return 0f;
            try
            {
                object value = field.GetValue(instance);
                if (value is float f) return f;
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[ProjectileHelper] field read failed: {ex.Message}");
            }
            return 0f;
        }

        private static T ReadObjectField<T>(FieldInfo field, object instance) where T : class
        {
            if (field == null || instance == null) return null;
            try
            {
                return field.GetValue(instance) as T;
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[ProjectileHelper] object field read failed: {ex.Message}");
                return null;
            }
        }

        private static void RegisterGunProjectile(GameObject projectile)
        {
            if (projectile != null)
                GunProjectileObjects.Add(projectile.GetInstanceID());
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
