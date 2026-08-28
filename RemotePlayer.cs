using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    public class RemotePlayer : MonoBehaviour
    {
        public CSteamID SteamId;
        public string DisplayName;

        // Interpolation targets
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private Vector3 _velocity;

        // higher = snappier but jerkier. 15 feels right at 20 packets/sec.
        private const float InterpSpeed = 15f;

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
            labelObj.transform.SetParent(transform);
            labelObj.transform.localPosition = new Vector3(0, 3f, 0);
            _nameLabel = labelObj.AddComponent<TextMesh>();
            _nameLabel.text = DisplayName ?? "Player";
            _nameLabel.fontSize = 24;
            _nameLabel.alignment = TextAlignment.Center;
            _nameLabel.anchor = TextAnchor.LowerCenter;
            _nameLabel.color = Color.cyan;
            labelObj.transform.localScale = Vector3.one * 0.1f;

            // If the factory managed a real copy at spawn, there's no placeholder
            _visualsBuilt = transform.Find("PlaceholderBox") == null;
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
                    MultiplayerPlugin.Log.LogInfo($"[RemotePlayer] Upgraded {DisplayName}'s placeholder to the real heli model");
                }
            }

            // Smooth movement toward received state
            transform.position = Vector3.Lerp(transform.position, _targetPosition, InterpSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, InterpSpeed * Time.deltaTime);

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
