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
        private CursorLockMode _prevLockState;
        private bool _prevCursorVisible;
        private GameObject _localHeli;
        private Rigidbody _heliRb;
        private bool _heliRbWasKinematic;
        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private readonly List<Behaviour> _disabledScripts = new List<Behaviour>();
        private readonly List<Behaviour> _hiddenCanvases = new List<Behaviour>();
        private readonly List<Camera> _disabledCameras = new List<Camera>();

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
            _warnedNoCam = false;

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

            // every game HUD canvas off - clean screen. found by type name so
            // we don't have to reference UnityEngine.UIModule at compile time.
            foreach (Behaviour b in FindObjectsOfType<Behaviour>())
            {
                if (b == null || !b.enabled) continue;
                if (b.GetType().Name == "Canvas")
                    { b.enabled = false; _hiddenCanvases.Add(b); }
            }

            // scene-level scripts that would drag us out of spectator: the
            // camera controllers fight us for the view, and the death/reset
            // handlers will happily throw a game over while our heli sits
            // parked with its scripts off.
            foreach (Behaviour b in FindObjectsOfType<Behaviour>())
            {
                if (b == null || !b.enabled) continue;
                string t = b.GetType().Name;
                bool cameraScript = t.StartsWith("RW_") && t.Contains("Camera");
                bool endGameScript = t == "RW_On_Death" || t == "LevelReset" ||
                                     t == "ForcedReset" || t == "RW_End_Game";
                if (cameraScript || endGameScript)
                {
                    b.enabled = false;
                    _disabledScripts.Add(b);
                    if (endGameScript)
                        MultiplayerPlugin.Log.LogInfo($"[Spectator] Disabled end-game script: {t}");
                }
            }

            // grab the camera that's actually rendering right now
            _prevCamera = Camera.main;
            if (_prevCamera == null || !_prevCamera.enabled || !_prevCamera.gameObject.activeInHierarchy)
            {
                foreach (Camera c in Camera.allCameras)
                    if (c != null && c.enabled) { _prevCamera = c; break; }
            }

            _freeCam = new GameObject("SpectatorCamera");
            Camera cam = _freeCam.AddComponent<Camera>();

            if (_prevCamera != null)
            {
                // clear flags, skybox, culling mask, depth - copy it all, or we
                // end up staring at a flat coloured void instead of the world
                cam.CopyFrom(_prevCamera);
                _freeCam.transform.position = _prevCamera.transform.position;
                _freeCam.transform.rotation = _prevCamera.transform.rotation;
            }
            else if (_localHeli != null)
            {
                _freeCam.transform.position = _localHeli.transform.position + Vector3.up * 5f;
            }

            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 15000f;
            cam.depth = 100f; // draw on top of anything left enabled
            cam.enabled = true;

            // turn off every other camera so ours is the one you see
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null || c == cam || !c.enabled) continue;
                c.enabled = false;
                _disabledCameras.Add(c);
            }

            _yaw = _freeCam.transform.eulerAngles.y;
            _pitch = _freeCam.transform.eulerAngles.x;
            if (_pitch > 180f) _pitch -= 360f;

            // the game locks the cursor while flying; we need it back for the list
            _prevLockState = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            MultiplayerPlugin.Log.LogInfo("[Spectator] ON - WASD/QE move, hold right mouse to look, shift/ctrl speed, F8 player list, F7 exit.");
            MultiplayerPlugin.Log.LogInfo(
                $"[Spectator] heli={(_localHeli != null ? _localHeli.name : "NOT FOUND")} " +
                $"renderersHidden={_hiddenRenderers.Count} scriptsDisabled={_disabledScripts.Count} " +
                $"canvasesHidden={_hiddenCanvases.Count} otherCamerasOff={_disabledCameras.Count}");
            if (_prevCamera == null)
                MultiplayerPlugin.Log.LogWarning("[Spectator] No active camera found to copy - view may look wrong. Report this log.");
            if (_localHeli == null)
                MultiplayerPlugin.Log.LogWarning("[Spectator] Local heli not found - your heli may still be visible/flying. Report this log.");
            if (_disabledScripts.Count == 0)
                MultiplayerPlugin.Log.LogWarning("[Spectator] No game scripts were disabled - death/reset handlers may still fire. Report this log.");
        }

        public void Exit()
        {
            if (!IsSpectating) return;
            IsSpectating = false;
            Following = null;

            int restoredR = 0, restoredS = 0, restoredC = 0, restoredCam = 0;

            foreach (Renderer r in _hiddenRenderers) if (r != null) { r.enabled = true; restoredR++; }
            _hiddenRenderers.Clear();

            foreach (Behaviour b in _disabledScripts) if (b != null) { b.enabled = true; restoredS++; }
            _disabledScripts.Clear();

            foreach (Behaviour c in _hiddenCanvases) if (c != null) { c.enabled = true; restoredC++; }
            _hiddenCanvases.Clear();

            if (_heliRb != null) _heliRb.isKinematic = _heliRbWasKinematic;
            _heliRb = null;

            if (_freeCam != null) Destroy(_freeCam);
            _freeCam = null;
            foreach (Camera c in _disabledCameras) if (c != null) { c.enabled = true; restoredCam++; }
            _disabledCameras.Clear();
            if (_prevCamera != null) _prevCamera.enabled = true;
            _prevCamera = null;

            Cursor.lockState = _prevLockState;
            Cursor.visible = _prevCursorVisible;

            HeliLocator.Invalidate();
            MultiplayerPlugin.Log.LogInfo(
                $"[Spectator] OFF - restored {restoredR} renderers, {restoredS} scripts, " +
                $"{restoredC} canvases, {restoredCam} cameras");
        }

        private bool _justStartedFollowing;

        public void Follow(RemotePlayer player)
        {
            Following = player;
            _justStartedFollowing = true;
            MultiplayerPlugin.Log.LogInfo($"[Spectator] Following {(player != null ? player.DisplayName : "nobody")}");
        }

        public void StopFollowing()
        {
            Following = null;
        }

        // called by the scoreboard when a remote player crosses the line
        public void OnPlayerFinished(string playerName)
        {
            if (!IsSpectating || Following == null) return;
            if (Following.DisplayName != playerName) return;

            RemotePlayer next = NextPlayerAfter(Following);
            if (next != null)
            {
                MultiplayerPlugin.Log.LogInfo($"[Spectator] {playerName} finished - switching to {next.DisplayName}");
                Follow(next);
            }
            else
            {
                MultiplayerPlugin.Log.LogInfo($"[Spectator] {playerName} finished - nobody else to watch, free camera");
                Following = null;
            }
        }

        private static bool StillInLobby(RemotePlayer rp)
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return false;
            foreach (var kv in nm.RemotePlayers)
                if (kv.Value == rp) return true;
            return false;
        }

        // next player in the lobby, skipping the one we just lost
        private static RemotePlayer NextPlayerAfter(RemotePlayer previous)
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return null;
            foreach (var kv in nm.RemotePlayers)
            {
                RemotePlayer rp = kv.Value;
                if (rp != null && rp != previous && rp.gameObject != null)
                    return rp;
            }
            return null;
        }

        private bool _warnedNoCam;

        private void Update()
        {
            if (!IsSpectating) return;
            if (_freeCam == null)
            {
                if (!_warnedNoCam)
                {
                    _warnedNoCam = true;
                    MultiplayerPlugin.Log.LogError("[Spectator] Camera object vanished while spectating - exiting. Report this log.");
                }
                Exit();
                return;
            }

            if (Following != null)
            {
                // they restarted, crashed out, finished, or left - move on to
                // whoever else is flying instead of staring at nothing
                if (Following == null || Following.gameObject == null || !StillInLobby(Following))
                {
                    RemotePlayer next = NextPlayerAfter(Following);
                    if (next != null)
                    {
                        MultiplayerPlugin.Log.LogInfo("[Spectator] Target gone - switching to " + next.DisplayName);
                        Follow(next);
                    }
                    else
                    {
                        MultiplayerPlugin.Log.LogInfo("[Spectator] Target gone - back to free camera");
                        Following = null;
                    }
                    return;
                }
                Vector3 offset = Following.transform.rotation * new Vector3(0f, 5f, -15f);
                Vector3 wanted = Following.transform.position + offset;
                // snap on the first frame, glide after - otherwise you spend a
                // second flying across the map every time you switch target
                _freeCam.transform.position = _justStartedFollowing
                    ? wanted
                    : Vector3.Lerp(_freeCam.transform.position, wanted, 10f * Time.unscaledDeltaTime);
                _justStartedFollowing = false;
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

            _freeCam.transform.position += move * speed * Time.unscaledDeltaTime;
        }
    }
}
