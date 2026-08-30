using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // MH-Zombie does not expose a helicopter-health API.  Keep multiplayer
    // combat self-contained so it cannot corrupt the game's flight systems.
    public sealed class LocalPlayerCombat : MonoBehaviour
    {
        public const float MaxHealth = 100f;

        private readonly Dictionary<string, float> _receivedProjectiles = new Dictionary<string, float>();
        private readonly List<BehaviourState> _disabledBehaviours = new List<BehaviourState>();
        private Rigidbody _rigidbody;

        public float Health { get; private set; } = MaxHealth;
        public bool IsAlive => Health > 0f;

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
            EnsureHitbox();
            MultiplayerPlugin.Log.LogInfo("[PvP] Local helicopter combat receiver ready (100 health).");
        }

        private void Update()
        {
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

            var collider = hitbox.GetComponent<BoxCollider>();
            if (collider == null) collider = hitbox.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(3.5f, 2.2f, 5.5f);
            collider.center = new Vector3(0f, 1f, 0f);

            if (hitbox.GetComponent<LocalPlayerHitbox>() == null)
                hitbox.AddComponent<LocalPlayerHitbox>();
        }

        public bool ReceiveRemoteHit(ulong attackerId, int projectileInstanceId, float damage, ProjectileKind kind)
        {
            if (!IsAlive || attackerId == SteamUser.GetSteamID().m_SteamID)
                return false;

            damage = Mathf.Clamp(damage, 1f, 100f);
            string key = attackerId + ":" + projectileInstanceId;
            if (_receivedProjectiles.ContainsKey(key))
                return false;
            _receivedProjectiles[key] = Time.time;

            Health = Mathf.Max(0f, Health - damage);
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