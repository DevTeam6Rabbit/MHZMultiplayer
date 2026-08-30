using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // PvP and the game's native helicopter health share the same value. PvP
    // still owns its elimination flow so native and multiplayer deaths cannot
    // both restart the player for the same hit.
    public sealed class LocalPlayerCombat : MonoBehaviour
    {
        public const float MaxHealth = 100f;
        public const float SpawnProtectionSeconds = 10f;

        private const float BaseHitboxHeight = 3f;
        private const float ExtraTopHeight = BaseHitboxHeight * 0.4f;
        // Add the requested 40% entirely above the old box: its bottom remains
        // fixed while height grows from 3.0 to 4.2 and center rises by 0.6.
        public static readonly Vector3 PvPHitboxSize =
            new Vector3(4.5f, BaseHitboxHeight + ExtraTopHeight, 7f);
        public static readonly Vector3 PvPHitboxCenter =
            new Vector3(0f, 1f + ExtraTopHeight * 0.5f, 0f);

        private readonly Dictionary<string, float> _receivedProjectiles = new Dictionary<string, float>();
        private readonly List<BehaviourState> _disabledBehaviours = new List<BehaviourState>();
        private Rigidbody _rigidbody;
        private BoxCollider _hitbox;
        private EmeraldAI.Example.EmeraldAIPlayerHealth _gameHealth;
        private float _nextGameHealthSearch;
        private bool _loggedMissingGameHealth;
        private float _spawnProtectedUntil;
        private bool _loggedProtectedHit;

        public float Health { get; private set; } = MaxHealth;
        public bool IsAlive => Health > 0f;
        public bool IsSpawnProtected => Time.time < _spawnProtectedUntil;
        public float SpawnProtectionRemaining => Mathf.Max(0f, _spawnProtectedUntil - Time.time);

        public static LocalPlayerCombat EnsureAttached()
        {
            GameObject heli = HeliLocator.GetLocalHeli();
            if (heli == null) return null;

            LocalPlayerCombat combat = heli.GetComponent<LocalPlayerCombat>();
            if (combat == null)
                combat = heli.AddComponent<LocalPlayerCombat>();
            return combat;
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            FindGameHealth();
            SyncFromGameHealth();
            EnsureHitbox();
            RefillAllAmmo();
            ActivateSpawnProtection();
            MultiplayerPlugin.Log.LogInfo("[PvP] Local helicopter combat receiver ready (100 health).");
        }

        private void Update()
        {
            SyncFromGameHealth();

            if (_receivedProjectiles.Count == 0) return;
            float cutoff = Time.time - 10f;
            var expired = new List<string>();
            foreach (var pair in _receivedProjectiles)
                if (pair.Value < cutoff) expired.Add(pair.Key);
            foreach (string key in expired)
                _receivedProjectiles.Remove(key);
        }

        private void EnsureHitbox()
        {
            Transform existing = transform.Find("MHZ_PvP_Hitbox");
            GameObject hitbox = existing != null ? existing.gameObject : new GameObject("MHZ_PvP_Hitbox");
            if (existing == null)
                hitbox.transform.SetParent(transform, false);

            _hitbox = hitbox.GetComponent<BoxCollider>();
            if (_hitbox == null) _hitbox = hitbox.AddComponent<BoxCollider>();
            _hitbox.isTrigger = true;
            _hitbox.size = PvPHitboxSize;
            _hitbox.center = PvPHitboxCenter;

            if (hitbox.GetComponent<LocalPlayerHitbox>() == null)
                hitbox.AddComponent<LocalPlayerHitbox>();

            // Green is the authoritative receiver volume on this client.
            DebugTools.EnsureHitboxVisual(hitbox.transform, _hitbox,
                new Color(0.1f, 1f, 0.2f, 0.18f));
        }

        // Trigger callbacks alone can miss a 200 m/s projectile that crosses the
        // entire box between physics ticks. Test the whole travelled segment
        // against this oriented box, expanded by the projectile radius.
        public bool SegmentIntersectsHitbox(Vector3 worldStart, Vector3 worldEnd, float projectileRadius)
        {
            if (_hitbox == null || !_hitbox.enabled || !_hitbox.gameObject.activeInHierarchy)
                return false;

            Transform hitboxTransform = _hitbox.transform;
            Vector3 start = hitboxTransform.InverseTransformPoint(worldStart) - _hitbox.center;
            Vector3 end = hitboxTransform.InverseTransformPoint(worldEnd) - _hitbox.center;
            Vector3 direction = end - start;

            Vector3 halfSize = GetEffectiveHitboxSize(_hitbox, projectileRadius) * 0.5f;

            float enter = 0f;
            float exit = 1f;
            return ClipSegmentAxis(start.x, direction.x, halfSize.x, ref enter, ref exit) &&
                   ClipSegmentAxis(start.y, direction.y, halfSize.y, ref enter, ref exit) &&
                   ClipSegmentAxis(start.z, direction.z, halfSize.z, ref enter, ref exit);
        }

        // SegmentIntersectsHitbox tests a projectile sphere, not a zero-width
        // ray. Its radius expands the box in local space. Debug rendering calls
        // this same method so the shown volume cannot drift from collision math.
        public static Vector3 GetEffectiveHitboxSize(BoxCollider hitbox, float projectileRadius)
        {
            if (hitbox == null) return Vector3.zero;

            Vector3 scale = hitbox.transform.lossyScale;
            float minScale = Mathf.Max(0.0001f,
                Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            return hitbox.size + Vector3.one * (2f * Mathf.Max(0f, projectileRadius) / minScale);
        }

        private static bool ClipSegmentAxis(float origin, float direction, float extent,
            ref float enter, ref float exit)
        {
            if (Mathf.Abs(direction) < 0.000001f)
                return origin >= -extent && origin <= extent;

            float inverse = 1f / direction;
            float first = (-extent - origin) * inverse;
            float second = (extent - origin) * inverse;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }

            enter = Mathf.Max(enter, first);
            exit = Mathf.Min(exit, second);
            return enter <= exit;
        }

        public bool ReceiveRemoteHit(ulong attackerId, int projectileInstanceId, float damage, ProjectileKind kind)
        {
            if (!IsAlive || attackerId == SteamUser.GetSteamID().m_SteamID)
                return false;

            string key = attackerId + ":" + projectileInstanceId;
            if (_receivedProjectiles.ContainsKey(key))
                return false;

            // Consume the projectile so it cannot remain inside the box and deal
            // damage on the first frame after protection expires.
            if (IsSpawnProtected)
            {
                _receivedProjectiles[key] = Time.time;
                if (!_loggedProtectedHit)
                {
                    _loggedProtectedHit = true;
                    MultiplayerPlugin.Log.LogInfo($"[PvP] Spawn protection blocked {kind} shot ({SpawnProtectionRemaining:F1}s remaining).");
                }
                return true;
            }

            damage = ProjectileHelper.GetDamageForKind(kind);
            if (damage <= 0f)
                return false;
            _receivedProjectiles[key] = Time.time;

            Health = Mathf.Max(0f, Health - damage);
            SyncToGameHealth();
            string attacker = SteamFriends.GetFriendPersonaName(new CSteamID(attackerId));
            MultiplayerPlugin.Log.LogInfo($"[PvP] Hit by {attacker}: {damage:F0} {kind} damage. Health={Health:F0}");
            LobbyUI.Instance?.AddChatMessage($"[PvP] {attacker} hit you for {damage:F0}. Health: {Health:F0}/{MaxHealth:F0}");
            LobbyUI.Instance?.ShowScoreboard();
            ScoreboardManager.ReportDamageTaken(attackerId, damage, Health <= 0f);
            NetworkManager.Instance?.SendDamageConfirmation(attackerId, damage, projectileInstanceId, Health);

            if (Health <= 0f)
                Eliminate();
            return true;
        }

        // Reset the local PvP health to full. Called when the player dies in-game
        // (crash / killed by the game) so a respawn starts with full health
        // instead of carrying over whatever was left. Also clears pending damage
        // state and re-enables anything a PvP elimination had disabled.
        public void ResetHealth()
        {
            Health = MaxHealth;
            SyncToGameHealth();
            _receivedProjectiles.Clear();
            RefillAllAmmo();
            ActivateSpawnProtection();

            foreach (var bs in _disabledBehaviours)
                if (bs.Behaviour != null)
                    bs.Behaviour.enabled = bs.Enabled;
            _disabledBehaviours.Clear();

            if (_rigidbody != null)
                _rigidbody.isKinematic = false;

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }

        public void ActivateSpawnProtection()
        {
            _spawnProtectedUntil = Time.time + SpawnProtectionSeconds;
            _loggedProtectedHit = false;
            MultiplayerPlugin.Log.LogInfo($"[PvP] Spawn protection active for {SpawnProtectionSeconds:F0} seconds.");
        }

        private void RefillAllAmmo()
        {
            Transform heliRoot = transform.root;
            if (heliRoot == null) return;

            int refilled = 0;

            foreach (Raulworks.RW_Base_Weapon weapon in
                heliRoot.GetComponentsInChildren<Raulworks.RW_Base_Weapon>(true))
            {
                if (weapon == null) continue;
                weapon.currentAmmoCount = weapon.maxAmmoCount;
                SetInactive(weapon.outOfAmmoMessage);
                TryRefreshAmmoDisplay(weapon.Reloaded, weapon.GetType().Name);
                refilled++;
            }

            foreach (Raulworks.RW_Thirty_MM weapon in
                heliRoot.GetComponentsInChildren<Raulworks.RW_Thirty_MM>(true))
            {
                if (weapon == null) continue;
                weapon.currentAmmoCount = weapon.maxAmmoCount;
                SetInactive(weapon.outOfAmmo);
                TryRefreshAmmoDisplay(weapon.Reloaded, nameof(Raulworks.RW_Thirty_MM));
                refilled++;
            }

            foreach (Raulworks.RW_Rocket_Launcher weapon in
                heliRoot.GetComponentsInChildren<Raulworks.RW_Rocket_Launcher>(true))
            {
                if (weapon == null) continue;
                weapon.currentAmmoCount = weapon.maxAmmoCount;
                SetInactive(weapon.outOfAmmo);
                TryRefreshAmmoDisplay(weapon.Reloaded, nameof(Raulworks.RW_Rocket_Launcher));
                refilled++;
            }

            foreach (Raulworks.RW_MineLayer weapon in
                heliRoot.GetComponentsInChildren<Raulworks.RW_MineLayer>(true))
            {
                if (weapon == null) continue;
                weapon.currentAmmoCount = weapon.maxAmmoCount;
                SetInactive(weapon.outOfAmmo);
                TryRefreshAmmoDisplay(weapon.Reloaded, nameof(Raulworks.RW_MineLayer));
                refilled++;
            }

            MultiplayerPlugin.Log.LogInfo($"[PvP] Refilled {refilled} weapon ammo stores on spawn/reset.");
        }

        private bool FindGameHealth()
        {
            if (_gameHealth != null) return true;
            if (Time.time < _nextGameHealthSearch) return false;
            _nextGameHealthSearch = Time.time + 2f;

            Transform heliRoot = transform.root;
            if (heliRoot != null)
                _gameHealth = heliRoot.GetComponentInChildren<EmeraldAI.Example.EmeraldAIPlayerHealth>(true);

            if (_gameHealth == null)
            {
                if (!_loggedMissingGameHealth)
                {
                    _loggedMissingGameHealth = true;
                    MultiplayerPlugin.Log.LogWarning("[PvP] In-game helicopter health component not found yet; using PvP health until it appears.");
                }
                return false;
            }
            else
            {
                _loggedMissingGameHealth = false;
                MultiplayerPlugin.Log.LogInfo($"[PvP] Linked in-game health at {_gameHealth.CurrentHealth}/{MaxHealth:F0}.");
                return true;
            }
        }

        private void SyncFromGameHealth()
        {
            if (_gameHealth == null)
            {
                if (!FindGameHealth()) return;
            }

            float gameValue = Mathf.Clamp(_gameHealth.CurrentHealth, 0, (int)MaxHealth);
            if (Mathf.Approximately(Health, gameValue)) return;

            Health = gameValue;
            MultiplayerPlugin.Log.LogInfo($"[PvP] Synced game health to PvP: {Health:F0}/{MaxHealth:F0}.");
        }

        private void SyncToGameHealth()
        {
            if (_gameHealth == null)
            {
                if (!FindGameHealth()) return;
            }

            int value = Mathf.Clamp(Mathf.RoundToInt(Health), 0, (int)MaxHealth);
            _gameHealth.CurrentHealth = value;

            // Updating the serialized health text keeps the game's original HUD
            // aligned without invoking DamagePlayer and starting a second death flow.
            if (_gameHealth.health != null)
            {
                _gameHealth.health.text = value.ToString();
                _gameHealth.health.faceColor = value <= 30 ? Color.red : Color.green;
            }
        }

        private static void SetInactive(GameObject obj)
        {
            if (obj != null) obj.SetActive(false);
        }

        private static void TryRefreshAmmoDisplay(Action refresh, string weaponName)
        {
            try
            {
                refresh?.Invoke();
            }
            catch (Exception ex)
            {
                // Ammo is already refilled; a missing/inactive HUD reference
                // should not prevent the respawn itself from completing.
                MultiplayerPlugin.Log.LogWarning($"[PvP] {weaponName} ammo HUD refresh failed: {ex.Message}");
            }
        }

        private void Eliminate()
        {
            _disabledBehaviours.Clear();
            foreach (Behaviour behaviour in GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || behaviour == this || behaviour is LocalPlayerHitbox)
                    continue;
                string typeName = behaviour.GetType().Name;
                if (!typeName.StartsWith("RW_Heli_", StringComparison.Ordinal))
                    continue;
                _disabledBehaviours.Add(new BehaviourState { Behaviour = behaviour, Enabled = behaviour.enabled });
                behaviour.enabled = false;
            }

            if (_rigidbody != null)
            {
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
            }

            LobbyUI.Instance?.AddChatMessage("[PvP] You were eliminated!");

            // Restart exactly like the pause menu's Restart button: it calls
            // RW_Menu.LoadTargetScene(), which reloads the current level through
            // SceneManager. No more 5-second timer + manual respawn.
            RestartLevel();
        }

        // The pause menu's Restart button fires RW_Menu.LoadTargetScene(), which
        // reloads the active scene (and unpauses audio). Replicating that here
        // gives the same hard restart the button performs. The scene's RW_Menu
        // instance may be inactive, so include inactive objects when searching.
        private void RestartLevel()
        {
            var menus = FindObjectsOfType<Raulworks.RW_Menu>(true);
            for (int i = 0; i < menus.Length; i++)
            {
                var menu = menus[i];
                if (menu == null) continue;
                if (menu.gameObject.activeInHierarchy)
                {
                    menu.LoadTargetScene();
                    return;
                }
            }

            // Fallback: no usable RW_Menu in this scene, so restart whatever is loaded.
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        private struct BehaviourState
        {
            public Behaviour Behaviour;
            public bool Enabled;
        }
    }

    public sealed class LocalPlayerHitbox : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            RemoteProjectile projectile = other != null ? other.GetComponentInParent<RemoteProjectile>() : null;
            if (projectile != null)
                projectile.TryHitLocalPlayer();
        }
    }
}
