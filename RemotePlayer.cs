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

            // Create floating name label
            GameObject labelObj = new GameObject("NameLabel");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            _nameLabel = labelObj.AddComponent<TextMesh>();
            _nameLabel.text = DisplayName ?? "Player";
            _nameLabel.fontSize = 80;
            _nameLabel.characterSize = 0.45f;
            _nameLabel.alignment = TextAlignment.Center;
            _nameLabel.anchor = TextAnchor.LowerCenter;
            _nameLabel.color = Color.cyan;
            _nameLabel.fontStyle = FontStyle.Bold;
            _nameLabel.offsetZ = 0.2f;
            labelObj.transform.localPosition = new Vector3(0f, 5.5f, 0f);
            labelObj.transform.localScale = Vector3.one * 0.6f;
            labelObj.GetComponent<Renderer>().material = new Material(Shader.Find("GUI/Text Shader"));

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
        }

        private readonly HashSet<int> _recentProjectileHits = new HashSet<int>();
        private readonly HashSet<int> _recentDamagePackets = new HashSet<int>();

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || other.transform == null || other.transform.IsChildOf(transform))
                return;

            if (!IsAlive)
                return;

            if (TryResolveProjectileHit(other, out float damage, out int projectileInstanceId))
            {
                int hitKey = projectileInstanceId > 0 ? projectileInstanceId : other.GetInstanceID();
                if (!_recentProjectileHits.Add(hitKey))
                    return;

                ApplyDamage(damage);
                if (NetworkManager.Instance != null)
                    NetworkManager.Instance.SendPlayerDamage(SteamId, damage, hitKey);
            }
            else if (other.bounds != null && other.bounds.size.magnitude > 1.5f)
            {
                ApplyDamage(10f);
            }
        }

        private static bool TryResolveProjectileHit(Collider other, out float damage, out int projectileInstanceId)
        {
            damage = 0f;
            projectileInstanceId = 0;

            if (other == null)
                return false;

            if (other.GetComponentInParent<Raulworks.RW_Base_Projectile>() != null)
            {
                damage = 35f;
                projectileInstanceId = other.GetComponentInParent<Raulworks.RW_Base_Projectile>().GetInstanceID();
                return true;
            }
            if (other.GetComponentInParent<Raulworks.RW_Gat_Projectile>() != null)
            {
                damage = 35f;
                projectileInstanceId = other.GetComponentInParent<Raulworks.RW_Gat_Projectile>().GetInstanceID();
                return true;
            }
            if (other.GetComponentInParent<Raulworks.RW_RocketProjectile>() != null)
            {
                damage = 45f;
                projectileInstanceId = other.GetComponentInParent<Raulworks.RW_RocketProjectile>().GetInstanceID();
                return true;
            }

            return false;
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

        public void ApplyDamage(float damage)
        {
            if (damage <= 0f || !IsAlive)
                return;

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

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }
    }
}
