using System.Collections.Generic;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // full spectator mode: your heli vanishes, every bit of UI goes away
    // (ours included), and you get a free noclip camera. F7 in and out,
    // F8 brings up a bare player list so you can click someone to follow.
    public class SpectatorMode : MonoBehaviour
    {
        public static SpectatorMode Instance { get; private set; }

        public bool IsSpectating { get; private set; }
        public RemotePlayer Following { get; private set; }

        private GameObject _freeCam;
        private Camera _prevCamera;
        private const float MoveSpeed = 50f;
        private const float FastSpeed = 150f;
        private const float SlowSpeed = 12f;
        private const float LookSens = 2f;
        private float _pitch, _yaw;

        // saved state so exiting puts everything back exactly as it was
        private GameObject _localHeli;
        private Rigidbody _heliRb;
        private bool _heliRbWasKinematic;
        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private readonly List<Behaviour> _disabledScripts = new List<Behaviour>();
        private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public void Toggle()
        {
            if (IsSpectating) Exit(); else Enter();
        }

        public void Enter()
        {
            if (IsSpectating) return;
            IsSpectating = true;
            Following = null;

            _localHeli = HeliLocator.GetLocalHeli();
            if (_localHeli != null)
            {
                Transform root = _localHeli.transform.root != null ? _localHeli.transform.root : _localHeli.transform;

                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.enabled) { r.enabled = false; _hiddenRenderers.Add(r); }

                // frozen where it sits, so it can't crash while we're away
                _heliRb = root.GetComponentInChildren<Rigidbody>();
                if (_heliRb != null)
                {
                    _heliRbWasKinematic = _heliRb.isKinematic;
                    _heliRb.velocity = Vector3.zero;
                    _heliRb.angularVelocity = Vector3.zero;
                    _heliRb.isKinematic = true;
                }

                foreach (Behaviour b in root.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null || !b.enabled) continue;
                    string t = b.GetType().Name;
                    if (t.StartsWith("RW_") || t.StartsWith("HeliSim"))
                        { b.enabled = false; _disabledScripts.Add(b); }
                }
            }

            // every game HUD canvas off - clean screen
            foreach (Canvas c in FindObjectsOfType<Canvas>())
                if (c != null && c.enabled) { c.enabled = false; _hiddenCanvases.Add(c); }

            // camera scripts elsewhere in the scene would fight us
            foreach (Behaviour b in FindObjectsOfType<Behaviour>())
            {
                if (b == null || !b.enabled) continue;
                string t = b.GetType().Name;
                if (t.StartsWith("RW_") && t.Contains("Camera"))
                    { b.enabled = false; _disabledScripts.Add(b); }
            }

            _prevCamera = Camera.main;
            _freeCam = new GameObject("SpectatorCamera");
            Camera cam = _freeCam.AddComponent<Camera>();
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 15000f;

            if (_prevCamera != null)
            {
                _freeCam.transform.position = _prevCamera.transform.position;
                _freeCam.transform.rotation = _prevCamera.transform.rotation;
                _prevCamera.enabled = false;
            }
            else if (_localHeli != null)
            {
                _freeCam.transform.position = _localHeli.transform.position + Vector3.up * 5f;
            }

            _yaw = _freeCam.transform.eulerAngles.y;
            _pitch = _freeCam.transform.eulerAngles.x;
            if (_pitch > 180f) _pitch -= 360f;

            MultiplayerPlugin.Log.LogInfo("[Spectator] ON - WASD/QE move, hold right mouse to look, shift/ctrl speed, F8 player list, F7 exit.");
        }

        public void Exit()
        {
            if (!IsSpectating) return;
            IsSpectating = false;
            Following = null;

            foreach (Renderer r in _hiddenRenderers) if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();

            foreach (Behaviour b in _disabledScripts) if (b != null) b.enabled = true;
            _disabledScripts.Clear();

            foreach (Canvas c in _hiddenCanvases) if (c != null) c.enabled = true;
            _hiddenCanvases.Clear();

            if (_heliRb != null) _heliRb.isKinematic = _heliRbWasKinematic;
            _heliRb = null;

            if (_freeCam != null) Destroy(_freeCam);
            _freeCam = null;
            if (_prevCamera != null) _prevCamera.enabled = true;
            _prevCamera = null;

            HeliLocator.Invalidate();
            MultiplayerPlugin.Log.LogInfo("[Spectator] OFF");
        }

        public void Follow(RemotePlayer player)
        {
            Following = player;
            MultiplayerPlugin.Log.LogInfo($"[Spectator] Following {(player != null ? player.DisplayName : "nobody")}");
        }

        public void StopFollowing()
        {
            Following = null;
        }

        private void Update()
        {
            if (!IsSpectating || _freeCam == null) return;

            if (Following != null)
            {
                if (Following.gameObject == null) { Following = null; return; }
                Vector3 offset = Following.transform.rotation * new Vector3(0f, 5f, -15f);
                _freeCam.transform.position = Vector3.Lerp(_freeCam.transform.position,
                    Following.transform.position + offset, 10f * Time.deltaTime);
                _freeCam.transform.LookAt(Following.transform.position);
                return;
            }

            // hold right mouse to look, so the cursor still works for the list
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * LookSens;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * LookSens, -89f, 89f);
            }
            _freeCam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // noclip: the camera has no collider, nothing stops it
            float speed = Input.GetKey(KeyCode.LeftShift) ? FastSpeed
                        : Input.GetKey(KeyCode.LeftControl) ? SlowSpeed : MoveSpeed;
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += _freeCam.transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= _freeCam.transform.forward;
            if (Input.GetKey(KeyCode.A)) move -= _freeCam.transform.right;
            if (Input.GetKey(KeyCode.D)) move += _freeCam.transform.right;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

            _freeCam.transform.position += move * speed * Time.deltaTime;
        }
    }
}
