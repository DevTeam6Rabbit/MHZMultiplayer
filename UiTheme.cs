using System.Collections.Generic;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // Builds a clean, dark IMGUI skin at runtime. No asset files needed - all
    // background textures are generated as 1x1 / 3x3 textures, so it can't
    // clash with the game's own UI systems (same rationale as the rest of the
    // mod's old-school OnGUI). Call UiTheme.Apply() at the top of OnGUI().
    public static class UiTheme
    {
        // --- Palette (clean dark, cyan accent to match the nametags) ---
        public static readonly Color Bg        = Hex("1B1C1F");
        public static readonly Color BgRaised  = Hex("26272C");
        public static readonly Color BgField   = Hex("131417");
        public static readonly Color Border    = Hex("37383F");
        public static readonly Color Text      = Hex("E6E6EA");
        public static readonly Color TextDim   = Hex("9A9AA3");
        public static readonly Color Accent    = Hex("2BC4DE");
        public static readonly Color Btn       = Hex("2C2D33");
        public static readonly Color BtnHover  = Hex("383940");
        public static readonly Color BtnActive = Hex("4A4B54");
        public static readonly Color Outline   = Hex("4C4D57");

        // Semi-transparent window backgrounds (low opacity so the game shows
        // through). These match Bg / BgRaised at ~55% alpha.
        public static readonly Color WindowBg = new Color(0.106f, 0.110f, 0.122f, 0.55f); // ~#1B1C1F
        public static readonly Color HeaderBg = new Color(0.149f, 0.153f, 0.173f, 0.55f); // ~#26272C

        private static GUISkin _skin;
        private static bool _ready;

        // Styles callers can reference directly.
        public static GUIStyle Header    { get; private set; }
        public static GUIStyle Dim       { get; private set; }
        public static GUIStyle Window    { get; private set; }
        public static GUIStyle HeaderBar { get; private set; }
        public static GUIStyle Hr        { get; private set; }

        public static void Apply()
        {
            if (!_ready) Build();
            GUI.skin = _skin;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;
        }

        private static void Build()
        {
            _skin = ScriptableObject.CreateInstance<GUISkin>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null) _skin.font = font;

            _skin.label          = MakeLabel();
            _skin.button         = MakeButton();
            _skin.textField      = MakeTextField();
            Window               = MakeWindow();
            _skin.window         = Window;
            _skin.box            = MakeBox();
            _skin.scrollView     = MakeScrollView();

            _skin.verticalScrollbar          = MakeTrack(true);
            _skin.verticalScrollbarThumb     = MakeThumb();
            _skin.horizontalScrollbar        = MakeTrack(false);
            _skin.horizontalScrollbarThumb   = MakeThumb();
            _skin.verticalSlider             = MakeTrack(true);
            _skin.verticalSliderThumb        = MakeThumb();
            _skin.horizontalSlider           = MakeTrack(false);
            _skin.horizontalSliderThumb      = MakeThumb();

            // Text-field cursor / selection colors
            _skin.settings.cursorColor = Accent;
            _skin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);

            Header    = MakeHeader();
            Dim       = MakeDim();
            HeaderBar = MakeHeaderBar();
            Hr        = MakeHr();

            _skin.customStyles = new GUIStyle[] { Header, Dim, HeaderBar, Hr };
            _ready = true;
        }

        private static GUIStyle MakeLabel()
        {
            return new GUIStyle
            {
                normal = { textColor = Text },
                hover = { textColor = Text },
                active = { textColor = Text },
                fontSize = 13,
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        private static GUIStyle MakeDim()
        {
            return new GUIStyle
            {
                normal = { textColor = TextDim },
                hover = { textColor = TextDim },
                active = { textColor = TextDim },
                fontSize = 12,
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 1, 1)
            };
        }

        private static GUIStyle MakeHeader()
        {
            return new GUIStyle
            {
                normal = { textColor = Accent },
                hover = { textColor = Accent },
                active = { textColor = Accent },
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 4, 2)
            };
        }

        private static GUIStyle MakeButton()
        {
            return new GUIStyle
            {
                normal = { background = Solid(Btn), textColor = Text },
                hover = { background = Solid(BtnHover), textColor = Text },
                active = { background = Solid(BtnActive), textColor = Color.white },
                focused = { background = Solid(BtnHover), textColor = Text },
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(10, 10, 5, 5)
            };
        }

        private static GUIStyle MakeTextField()
        {
            return new GUIStyle
            {
                normal = { background = Frame(Border, BgField), textColor = Text },
                focused = { background = Frame(Accent, BgField), textColor = Text },
                hover = { background = Frame(Border, BgField), textColor = Text },
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(6, 6, 4, 4)
            };
        }

        private static GUIStyle MakeBox()
        {
            return new GUIStyle
            {
                normal = { background = Frame(Border, BgRaised), textColor = Text },
                fontSize = 13,
                wordWrap = true,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(6, 6, 5, 5)
            };
        }

        private static GUIStyle MakeWindow()
        {
            // The window title bar is drawn manually in each window (see
            // LobbyUI.Draw*Panel) so it can't overlap content or be clipped.
            // This style just supplies the background + a uniform 1px border.
            return new GUIStyle
            {
                normal = { background = Frame(Border, WindowBg), textColor = Text },
                fontSize = 13,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(8, 8, 8, 8)
            };
        }

        // A full-width band that shows a window's title, and a thin accent
        // underline beneath it (the window "header").
        private static GUIStyle MakeHeaderBar()
        {
            return new GUIStyle
            {
                normal = { background = Solid(HeaderBg), textColor = Text },
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 7, 7),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        private static GUIStyle MakeHr()
        {
            return new GUIStyle
            {
                normal = { background = Solid(Accent), textColor = Accent },
                fixedHeight = 2,
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        private static GUIStyle MakeScrollView()
        {
            return new GUIStyle
            {
                normal = { background = Solid(WindowBg) },
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        private static GUIStyle MakeTrack(bool vertical)
        {
            var s = new GUIStyle { normal = { background = Solid(BgField) } };
            if (vertical) s.fixedWidth = 8f; else s.fixedHeight = 8f;
            return s;
        }

        private static GUIStyle MakeThumb()
        {
            return new GUIStyle
            {
                normal = { background = Solid(BtnHover) },
                hover = { background = Solid(Btn) },
                active = { background = Solid(Accent) }
            };
        }

        // --- texture helpers ---

        private static readonly Dictionary<Color, Texture2D> _solidCache = new Dictionary<Color, Texture2D>();

        private static Texture2D Solid(Color c)
        {
            if (_solidCache.TryGetValue(c, out Texture2D tex)) return tex;
            tex = new Texture2D(1, 1) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixel(0, 0, c);
            tex.Apply();
            _solidCache[c] = tex;
            return tex;
        }

        // Public HUD helpers: draw a solid rect, or a bordered rect (1px ring
        // of `edge` around a `fill`). Uses the shared texture caches.
        public static void DrawRect(Rect r, Color c)
            => GUI.DrawTexture(r, Solid(c));

        public static void DrawFrame(Rect r, Color edge, Color fill)
            => GUI.DrawTexture(r, Frame(edge, fill));

        // Draw a 1px (or thicker) outline around a rect, no fill - used to give
        // the windows a crisp visible border.
        public static void DrawOutline(Rect r, Color c, int thickness = 1)
        {
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Solid(c));
            GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), Solid(c));
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Solid(c));
            GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), Solid(c));
        }

        // 3x3 texture with a 1px edge ring of `edge` and `fill` in the middle,
        // so it slices (GUIStyle.border = 1) into a clean 1px window border.
        private static Texture2D Frame(Color edge, Color fill)
        {
            var tex = new Texture2D(3, 3) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    bool isEdge = x == 0 || x == 2 || y == 0 || y == 2;
                    tex.SetPixel(x, y, isEdge ? edge : fill);
                }
            tex.Apply();
            return tex;
        }

        private static Color Hex(string hex)
        {
            byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            return new Color32(r, g, b, 255);
        }
    }
}
