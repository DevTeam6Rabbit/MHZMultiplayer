using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    public class RemotePlayer : MonoBehaviour
    {
        public CSteamID SteamId;
        public string DisplayName;

        public float Health = 100f;
        public bool IsAlive => Health > 0f;

        // Interpolation targets
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private Vector3 _velocity;

        // higher = snappier but jerkier. 15 feels right at 20 packets/sec.
        private const float InterpSpeed = 15f;
        private const float MinHeliSeparation = 2.8f;

        // Name label above the heli
        private TextMesh _nameLabel;

        private float _lastUpdateTime;
        private const float TimeoutSeconds = 5f; // hide heli if no updates for 5s

        // Retry building the real heli visuals until a local heli exists to copy
        private bool _visualsBuilt;
        private float _nextVisualRetry;

        private void Start()
        {
            _targetPosition = transform.position;
            _targetRotation = transform.rotation;
            _lastUpdateTime = Time.time;

            // Create a floating name label as a crisp TextMesh. The old version
            // used a bare `new Material(Shader.Find("GUI/Text Shader"))` (which
            // has no font atlas texture and may be missing from the build) and a
            // low fontSize, so names rendered as blocky solid quads. Here we load
            // the real built-in font, clone its material so _MainTex is the font
            // atlas, and use a high fontSize for sharp glyphs up close.
            GameObject labelObj = new GameObject("NameLabel");
            labelObj.transform.SetParent(transform, false);
            _nameLabel = labelObj.AddComponent<TextMesh>();
            _nameLabel.text = DisplayName ?? "Player";
            _nameLabel.fontSize = 200;
            _nameLabel.characterSize = 0.3f;
            _nameLabel.alignment = TextAlignment.Center;
            _nameLabel.anchor = TextAnchor.MiddleCenter;
            _nameLabel.fontStyle = FontStyle.Bold;
            _nameLabel.offsetZ = 0.2f;

            // The built-in font name differs across Unity versions; try both.
            Font labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (labelFont == null)
                labelFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (labelFont != null)
            {
                _nameLabel.font = labelFont;
                // Clone the font's own material so _MainTex is the font atlas;
                // a fresh Material(...) has no texture and draws solid blocks.
                labelObj.GetComponent<MeshRenderer>().material = new Material(labelFont.material);
            }
            _nameLabel.color = Color.cyan;

            labelObj.transform.localPosition = new Vector3(0f, 5.5f, 0f);
            labelObj.transform.localScale = Vector3.one * 0.6f;

            // If the factory managed a real copy at spawn, there's no placeholder
            _visualsBuilt = transform.Find("PlaceholderBox") == null;

            if (GetComponent<BoxCollider>() != null)
            {
                var cc = GetComponent<BoxCollider>();
                cc.isTrigger = true;
                cc.size = new Vector3(Mathf.Max(cc.size.x, 2f), Mathf.Max(cc.size.y, 1.5f), Mathf.Max(cc.size.z, 2.5f));
            }
        }

        private void Update()
        {
            // can't build the model until we have our own heli to copy from,
            // so keep retrying until one exists
            if (!_visualsBuilt && Time.time >= _nextVisualRetry)
            {
                _nextVisualRetry = Time.time + 2f;
                if (GhostHeliFactory.TryBuildVisuals(transform) > 0)
                {
                    _visualsBuilt = true;
                    Transform box = transform.Find("PlaceholderBox");
                    if (box != null) Destroy(box.gameObject);
                    GhostHeliFactory.EnsurePvPHitbox(gameObject);
                    MultiplayerPlugin.Log.LogInfo($"[RemotePlayer] Upgraded {DisplayName}'s placeholder to the real heli model");
                }
            }

            // Smooth movement toward received state
            transform.position = Vector3.Lerp(transform.position, _targetPosition, InterpSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, InterpSpeed * Time.deltaTime);

            ResolveNearbyHeliCollisions();

            // Keep name label facing the camera
            if (Camera.main != null)
            {
                _nameLabel.transform.rotation = Quaternion.LookRotation(
                    _nameLabel.transform.position - Camera.main.transform.position);
            }

            // Hide if we haven't heard from this player in a while
            bool active = (Time.time - _lastUpdateTime) < TimeoutSeconds;
            if (gameObject.activeSelf != active)
                gameObject.SetActive(active);

            if (_recentProjectileHits.Count > 0)
            {
                float cutoff = Time.time - 10f;
                var expired = new List<int>();
                foreach (var pair in _recentProjectileHits)
                    if (pair.Value < cutoff) expired.Add(pair.Key);
                foreach (int key in expired)
                    _recentProjectileHits.Remove(key);
            }
        }

        private readonly Dictionary<int, float> _recentProjectileHits = new Dictionary<int, float>();

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || other.transform == null || other.transform.IsChildOf(transform))
                return;

            if (!IsAlive)
                return;

            if (TryResolveProjectileHit(other, out float damage, out int projectileInstanceId))
            {
                int hitKey = projectileInstanceId > 0 ? projectileInstanceId : other.GetInstanceID();
                if (_recentProjectileHits.ContainsKey(hitKey))
                    return;

                // This is only shooter-side prediction for the ghost model.
                // The target client authoritatively confirms its own hit.
                ApplyDamage(damage, hitKey);
            }
        }

        private static bool TryResolveProjectileHit(Collider other, out float damage, out int projectileInstanceId)
        {
            damage = 0f;
            projectileInstanceId = 0;

            if (other == null)
                return false;

            if (!ProjectileHelper.IsGameProjectile(other)) return false;
            damage = ProjectileHelper.GetDamageFromCollider(other);
            projectileInstanceId = ProjectileHelper.GetProjectileInstanceId(other);
            return damage > 0f;
        }

        private void OnCollisionEnter(Collision collision)
        {
            OnTriggerEnter(collision.collider);
        }

        private void ResolveNearbyHeliCollisions()
        {
            foreach (var other in FindObjectsOfType<RemotePlayer>())
            {
                if (other == null || other == this || other.gameObject == null || !other.gameObject.activeSelf)
                    continue;

                Vector3 delta = transform.position - other.transform.position;
                float distance = delta.magnitude;
                if (distance <= 0.001f)
                {
                    delta = new Vector3(UnityEngine.Random.value - 0.5f, 0f, UnityEngine.Random.value - 0.5f).normalized;
                    distance = 0f;
                }

                if (distance >= MinHeliSeparation)
                    continue;

                float push = (MinHeliSeparation - distance) * 0.5f;
                transform.position += delta.normalized * push;
            }
        }

        public void ApplyDamage(float damage, int projectileInstanceId = 0)
        {
            if (damage <= 0f || !IsAlive)
                return;

            if (projectileInstanceId != 0)
            {
                if (_recentProjectileHits.ContainsKey(projectileInstanceId)) return;
                _recentProjectileHits[projectileInstanceId] = Time.time;
            }

            Health = Mathf.Max(0f, Health - damage);
            MultiplayerPlugin.Log.LogInfo($"[RemotePlayer] {DisplayName} took {damage} damage. Health={Health}");

            if (Health <= 0f)
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                MultiplayerPlugin.Log.LogInfo($"[RemotePlayer] {DisplayName} was eliminated.");
            }
        }

        public void ApplyState(HeliStatePacket packet)
        {
            _targetPosition = packet.Position;
            _targetRotation = packet.Rotation;
            _velocity = packet.Velocity;
            _lastUpdateTime = Time.time;
            Health = Mathf.Clamp(packet.Health, 0f, LocalPlayerCombat.MaxHealth);

            if (Health <= 0f)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
            }
            else if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }
    }
}
