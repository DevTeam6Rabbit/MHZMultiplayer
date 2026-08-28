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
        private float _targetPitch, _targetYaw;
        private Vector3 _moveVelocity;      // for SmoothDamp on free-cam movement
        private Vector3 _followVelocity;    // for SmoothDamp on the chase cam
        private Vector3 _moveAccel;         // SmoothDamp scratch for movement
        private const float MoveSmooth = 0.12f;   // accel/decel feel
        private const float LookSmooth = 0.05f;   // mouse damping
        private const float FollowSmooth = 0.25f; // chase cam damping

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
        private readonly List<Collider> _disabledColliders = new List<Collider>();
        private Transform _heliRoot;
        private RigidbodyConstraints _heliRbConstraints;
        private bool _heliRbDetect;
        private Vector3 _heliParkedAt;
        private Quaternion _heliParkedRot;
        private float _nextHeliCheck;

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
                // NOT transform.root - that climbs to a scene-wide parent and
                // we end up disabling half the level. Walk up only while the
                // parent still looks like part of the heli (has renderers and
                // isn't the scene root itself).
                _heliRoot = FindHeliRoot(_localHeli.transform);

                foreach (Renderer r in _heliRoot.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.enabled) { r.enabled = false; _hiddenRenderers.Add(r); }

                // only the heli's own flight/input scripts - anything broader
                // takes the rest of the game down with it
                foreach (Behaviour b in _heliRoot.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null || !b.enabled) continue;
                    if (b == this || b is LobbyUI || b is NetworkManager || b is RemotePlayer) continue;
                    string t = b.GetType().Name;
                    if (t.StartsWith("RW_") || t.StartsWith("HeliSim") ||
                        t.Contains("Input") || t.Contains("Controller") || t.Contains("Heli"))
                        { b.enabled = false; _disabledScripts.Add(b); }
                }

                // colliders off so nothing can register a crash against it
                foreach (Collider col in _heliRoot.GetComponentsInChildren<Collider>(true))
                    if (col != null && col.enabled) { col.enabled = false; _disabledColliders.Add(col); }

                // and pin the body in place
                _heliRb = _heliRoot.GetComponentInChildren<Rigidbody>();
                if (_heliRb != null)
                {
                    _heliRbWasKinematic = _heliRb.isKinematic;
                    _heliRbConstraints = _heliRb.constraints;
                    _heliRbDetect = _heliRb.detectCollisions;
                    _heliRb.velocity = Vector3.zero;
                    _heliRb.angularVelocity = Vector3.zero;
                    _heliRb.isKinematic = true;
                    _heliRb.detectCollisions = false;
                    _heliRb.constraints = RigidbodyConstraints.FreezeAll;
                }
                _heliParkedAt = _heliRoot.position;
                _heliParkedRot = _heliRoot.rotation;
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
            _targetYaw = _yaw;
            _targetPitch = _pitch;
            _moveVelocity = Vector3.zero;
            _followVelocity = Vector3.zero;

            // the game locks the cursor while flying; we need it back for the list
            _prevLockState = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            MultiplayerPlugin.Log.LogInfo("[Spectator] ON - WASD/QE move, hold right mouse to look, shift/ctrl speed, F8 player list, F7 exit.");
            MultiplayerPlugin.Log.LogInfo(
                $"[Spectator] heli={(_localHeli != null ? _localHeli.name : "NOT FOUND")} " +
                $"root={(_heliRoot != null ? _heliRoot.name : "-")} " +
                $"renderersHidden={_hiddenRenderers.Count} scriptsDisabled={_disabledScripts.Count} " +
                $"canvasesHidden={_hiddenCanvases.Count} otherCamerasOff={_disabledCameras.Count}");
            if (_prevCamera == null)
                MultiplayerPlugin.Log.LogWarning("[Spectator] No active camera found to copy - view may look wrong. Report this log.");
            if (_localHeli == null)
                MultiplayerPlugin.Log.LogWarning("[Spectator] Local heli not found - your heli may still be visible/flying. Report this log.");
            if (_disabledScripts.Count == 0)
                MultiplayerPlugin.Log.LogWarning("[Spectator] No game scripts were disabled - death/reset handlers may still fire. Report this log.");
            if (_disabledScripts.Count > 80)
                MultiplayerPlugin.Log.LogWarning($"[Spectator] Disabled {_disabledScripts.Count} scripts - that looks like too much of the scene. Report this log.");
        }

        // climb from the heli's control object up to the object that holds the
        // model, but no further - one level past the last thing with renderers
        // and we'd be grabbing the entire scene.
        private static Transform FindHeliRoot(Transform start)
        {
            Transform best = start;
            Transform t = start;
            int hops = 0;
            while (t.parent != null && hops < 4)
            {
                t = t.parent;
                hops++;
                // a parent holding a rigidbody or renderers is still the heli
                if (t.GetComponent<Rigidbody>() != null ||
                    t.GetComponentsInChildren<Renderer>(true).Length > 0)
                {
                    // but bail if it looks like a scene container
                    if (t.GetComponentsInChildren<Behaviour>(true).Length > 120) break;
                    best = t;
                }
            }
            return best;
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

            foreach (Collider col in _disabledColliders) if (col != null) col.enabled = true;
            _disabledColliders.Clear();

            if (_heliRb != null)
            {
                _heliRb.constraints = _heliRbConstraints;
                _heliRb.detectCollisions = _heliRbDetect;
                _heliRb.isKinematic = _heliRbWasKinematic;
                _heliRb.velocity = Vector3.zero;
                _heliRb.angularVelocity = Vector3.zero;
            }
            _heliRb = null;
            _heliRoot = null;

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

        // the game re-enables its own scripts on respawn/scene events, which
        // would hand control back to a heli we're not looking at. so we hold
        // it down: anything that woke up gets switched off again, and the heli
        // gets put back exactly where we parked it.
        private void KeepHeliParked()
        {
            if (_heliRoot == null || Time.unscaledTime < _nextHeliCheck) return;
            _nextHeliCheck = Time.unscaledTime + 0.25f;

            int rewoken = 0;
            foreach (Behaviour b in _disabledScripts)
                if (b != null && b.enabled) { b.enabled = false; rewoken++; }
            if (rewoken > 0)
                MultiplayerPlugin.Log.LogInfo($"[Spectator] {rewoken} game script(s) re-enabled themselves - disabled again");

            foreach (Collider col in _disabledColliders)
                if (col != null && col.enabled) col.enabled = false;

            if (_heliRb != null && !_heliRb.isKinematic)
            {
                _heliRb.isKinematic = true;
                _heliRb.detectCollisions = false;
                MultiplayerPlugin.Log.LogInfo("[Spectator] Heli rigidbody woke up - re-frozen");
            }

            float drift = Vector3.Distance(_heliRoot.position, _heliParkedAt);
            if (drift > 1f)
            {
                _heliRoot.position = _heliParkedAt;
                _heliRoot.rotation = _heliParkedRot;
                MultiplayerPlugin.Log.LogInfo($"[Spectator] Heli drifted {drift:F1}m - moved back to its parking spot");
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

        private void LateUpdate()
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

                if (_justStartedFollowing)
                {
                    // land straight on the shot instead of flying across the map
                    _freeCam.transform.position = wanted;
                    _freeCam.transform.rotation = Quaternion.LookRotation(
                        Following.transform.position - wanted);
                    _followVelocity = Vector3.zero;
                    _justStartedFollowing = false;
                }
                else
                {
                    // SmoothDamp eases in and out, so network jitter on their
                    // position doesn't translate into a twitchy camera
                    _freeCam.transform.position = Vector3.SmoothDamp(
                        _freeCam.transform.position, wanted, ref _followVelocity,
                        FollowSmooth, Mathf.Infinity, Time.unscaledDeltaTime);

                    Vector3 lookAt = Following.transform.position + Vector3.up * 1.5f;
                    Vector3 dir = lookAt - _freeCam.transform.position;
                    if (dir.sqrMagnitude > 0.001f)
                        _freeCam.transform.rotation = Quaternion.Slerp(
                            _freeCam.transform.rotation, Quaternion.LookRotation(dir),
                            1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
                }

                KeepHeliParked();
                return;
            }

            KeepHeliParked();

            float dt = Time.unscaledDeltaTime;

            // hold right mouse to look, so the cursor still works for the list.
            // raw mouse deltas go into a target angle and the camera eases
            // toward it - kills the frame-to-frame twitchiness.
            if (Input.GetMouseButton(1))
            {
                _targetYaw += Input.GetAxis("Mouse X") * LookSens;
                _targetPitch = Mathf.Clamp(_targetPitch - Input.GetAxis("Mouse Y") * LookSens, -89f, 89f);
            }
            float lookT = 1f - Mathf.Exp(-dt / Mathf.Max(LookSmooth, 0.0001f));
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, lookT);
            _pitch = Mathf.LerpAngle(_pitch, _targetPitch, lookT);
            _freeCam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // noclip: the camera has no collider, nothing stops it
            float speed = Input.GetKey(KeyCode.LeftShift) ? FastSpeed
                        : Input.GetKey(KeyCode.LeftControl) ? SlowSpeed : MoveSpeed;
            Vector3 input = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) input += _freeCam.transform.forward;
            if (Input.GetKey(KeyCode.S)) input -= _freeCam.transform.forward;
            if (Input.GetKey(KeyCode.A)) input -= _freeCam.transform.right;
            if (Input.GetKey(KeyCode.D)) input += _freeCam.transform.right;
            if (Input.GetKey(KeyCode.E)) input += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) input -= Vector3.up;
            if (input.sqrMagnitude > 1f) input.Normalize(); // no free speed on diagonals

            // ease into and out of motion instead of snapping on/off
            Vector3 wantedVel = input * speed;
            _moveVelocity = Vector3.SmoothDamp(_moveVelocity, wantedVel, ref _moveAccel, MoveSmooth, Mathf.Infinity, dt);
            _freeCam.transform.position += _moveVelocity * dt;
        }
    }
}
