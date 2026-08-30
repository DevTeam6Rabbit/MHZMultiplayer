using System.Collections.Generic;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // Hitmarkers for PvP. Fires only on a confirmed hit - the victim's client
    // is what tells us we connected - so it can't lie to you the way a purely
    // local "did my bullet touch something" check would.
    //
    // Sounds are generated in code (a short click, pitched by weapon) so there
    // are no audio files to ship and nothing to load.
    public class Hitmarker : MonoBehaviour
    {
        public static Hitmarker Instance { get; private set; }

        // one marker on screen, plus damage numbers that drift up and fade
        const float MarkerLife = 0.35f;
        const float KillMarkerLife = 0.7f;
        const float NumberLife = 1.1f;

        // player-adjustable, tweaked from the multiplayer panel
        public static float Volume = 0.55f;
        public static bool SoundEnabled = true;
        public static int NumberSize = 22;

        float _shownAt = -99f;
        float _life = MarkerLife;
        bool _wasKill;

        struct FloatingNumber
        {
            public string Text;
            public float Born;
            public float X, Y;      // screen offset from centre
            public bool Kill;
        }
        readonly List<FloatingNumber> _numbers = new List<FloatingNumber>();

        AudioSource _audio;
        AudioClip _hitClip, _bigHitClip, _killClip;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;   // 2D, always the same volume
            _audio.volume = Volume;

            _hitClip    = MakeClick(1400f, 0.045f, 0.35f);   // 7.62 / light hit
            _bigHitClip = MakeClick(900f,  0.075f, 0.5f);    // 30mm / rocket
            _killClip   = MakeKillChime();

            MultiplayerPlugin.Log.LogInfo("[Hitmarker] Ready.");
        }

        /// Fires a sample hit so settings changes can be previewed.
        public static void Preview()
        {
            Instance?.ShowInternal(20f, false);
        }

        /// Call when a hit on another player is confirmed.
        public static void Show(float damage, bool killed)
        {
            if (Instance == null) return;
            Instance.ShowInternal(damage, killed);
        }

        void ShowInternal(float damage, bool killed)
        {
            _shownAt = Time.unscaledTime;
            _life = killed ? KillMarkerLife : MarkerLife;
            _wasKill = killed;

            // scatter the numbers a little so rapid minigun hits don't stack
            _numbers.Add(new FloatingNumber
            {
                Text = Mathf.RoundToInt(damage).ToString(),
                Born = Time.unscaledTime,
                X = Random.Range(-38f, 38f),
                Y = Random.Range(-14f, 14f),
                Kill = killed,
            });
            if (_numbers.Count > 12) _numbers.RemoveAt(0);

            if (_audio != null && SoundEnabled && Volume > 0.001f)
            {
                AudioClip clip = killed ? _killClip : (damage >= 18f ? _bigHitClip : _hitClip);
                // slight pitch jitter so repeated hits don't sound robotic
                _audio.pitch = killed ? 1f : Random.Range(0.94f, 1.06f);
                _audio.PlayOneShot(clip, Volume);
            }
        }

        void OnGUI()
        {
            float now = Time.unscaledTime;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            // ── the marker itself: four ticks angled around the crosshair ──
            float age = now - _shownAt;
            if (age < _life)
            {
                float t = age / _life;
                float alpha = 1f - t * t;               // quick fade
                float spread = 7f + t * 5f;             // ticks push outward
                float len = _wasKill ? 13f : 10f;
                float thick = _wasKill ? 3f : 2f;
                Color c = _wasKill ? new Color(1f, 0.25f, 0.2f, alpha)
                                   : new Color(1f, 1f, 1f, alpha);

                DrawTick(cx, cy, spread, len, thick, c,  1,  1);
                DrawTick(cx, cy, spread, len, thick, c, -1,  1);
                DrawTick(cx, cy, spread, len, thick, c,  1, -1);
                DrawTick(cx, cy, spread, len, thick, c, -1, -1);
            }

            // ── floating damage numbers ──
            if (_numbers.Count == 0) return;

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Clamp(NumberSize, 10, 60),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            for (int i = _numbers.Count - 1; i >= 0; i--)
            {
                FloatingNumber n = _numbers[i];
                float nAge = now - n.Born;
                if (nAge > NumberLife) { _numbers.RemoveAt(i); continue; }

                float t = nAge / NumberLife;
                float alpha = 1f - t;
                float rise = 34f * t;
                float w = style.fontSize * 4f;
                float hgt = style.fontSize * 1.6f;
                Rect r = new Rect(cx + n.X - w * 0.5f, cy + n.Y - 34f - rise, w, hgt);

                // outline in all 4 directions, not a single drop shadow - reads
                // as genuinely bold against bright sky and light terrain
                float o = Mathf.Max(1f, style.fontSize / 14f);
                style.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.85f);
                GUI.Label(new Rect(r.x - o, r.y, r.width, r.height), n.Text, style);
                GUI.Label(new Rect(r.x + o, r.y, r.width, r.height), n.Text, style);
                GUI.Label(new Rect(r.x, r.y - o, r.width, r.height), n.Text, style);
                GUI.Label(new Rect(r.x, r.y + o, r.width, r.height), n.Text, style);

                style.normal.textColor = n.Kill
                    ? new Color(1f, 0.3f, 0.25f, alpha)
                    : new Color(1f, 0.93f, 0.55f, alpha);
                GUI.Label(r, n.Text, style);
            }
        }

        // one angled tick of the X, drawn as a small rotated rect
        void DrawTick(float cx, float cy, float spread, float len, float thick, Color c, int sx, int sy)
        {
            Matrix4x4 saved = GUI.matrix;
            Color savedColor = GUI.color;

            float x = cx + spread * sx;
            float y = cy + spread * sy;
            float angle = (sx * sy > 0) ? 45f : -45f;

            GUIUtility.RotateAroundPivot(angle, new Vector2(x, y));
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - thick * 0.5f, y - len * 0.5f, thick, len), Texture2D.whiteTexture);

            GUI.color = savedColor;
            GUI.matrix = saved;
        }

        // ── generated audio: no files, no loading ──

        static AudioClip MakeClick(float freq, float seconds, float punch)
        {
            int sr = 44100;
            int n = Mathf.Max(64, (int)(sr * seconds));
            float[] data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = Mathf.Exp(-t / (seconds * 0.28f));      // sharp attack, fast decay
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t);
                float body = Mathf.Sin(2f * Mathf.PI * freq * 0.5f * t) * 0.4f;
                data[i] = (tone + body) * env * punch;
            }
            AudioClip clip = AudioClip.Create("mhz_hit", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // two quick rising notes for an elimination
        static AudioClip MakeKillChime()
        {
            int sr = 44100;
            int n = (int)(sr * 0.30f);
            float[] data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float freq = t < 0.09f ? 880f : 1320f;
                float local = t < 0.09f ? t : t - 0.09f;
                float env = Mathf.Exp(-local / 0.07f);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.5f;
            }
            AudioClip clip = AudioClip.Create("mhz_kill", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
