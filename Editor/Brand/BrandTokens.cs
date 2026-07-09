// Editor/Brand/BrandTokens.cs
// Game District Builder Notes design tokens — single source of truth for the
// cream/navy/gold editorial language. Reused across Hub, SOLID Review, Checklist.
//
// Colors come straight from the Builder Notes design system. Don't invent new ones.
// Fonts are loaded lazily from the bundled TTFs in Editor/Brand/Fonts/.

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace GDCodeShield.Brand
{
    public static class BrandTokens
    {
        // ═══════════════════════════════════════════════════════════════════════
        //  COLORS
        // ═══════════════════════════════════════════════════════════════════════
        public static readonly Color Cream    = new Color32(238, 237, 230, 255); // #EEEDE6 page bg
        public static readonly Color Navy     = new Color32(14,  26,  51,  255); // #0E1A33 primary text
        public static readonly Color Gold     = new Color32(244, 196, 48,  255); // #F4C430 accent
        public static readonly Color WarmGray = new Color32(107, 107, 102, 255); // #6B6B66 muted text
        public static readonly Color Taupe    = new Color32(211, 209, 199, 255); // #D3D1C7 hairlines
        public static readonly Color Ink      = new Color32(61,  61,  58,  255); // #3D3D3A body
        public static readonly Color Sky      = new Color32(133, 183, 235, 255); // #85B7EB ambient info
        public static readonly Color Overdue  = new Color32(192, 57,  43,  255); // #C0392B failing
        public static readonly Color Shipped  = new Color32(111, 167, 111, 255); // #6FA76F passing

        // Soft tints (for hover surfaces and pill backgrounds)
        public static readonly Color GoldTint    = new Color(244f/255f, 196f/255f, 48f/255f,  0.10f);
        public static readonly Color OverdueTint = new Color(192f/255f, 57f/255f,  43f/255f,  0.10f);
        public static readonly Color ShippedTint = new Color(111f/255f, 167f/255f, 111f/255f, 0.14f);
        public static readonly Color SkyTint     = new Color(133f/255f, 183f/255f, 235f/255f, 0.14f);

        // ═══════════════════════════════════════════════════════════════════════
        //  FONTS — lazy-loaded from Editor/Brand/Fonts/
        // ═══════════════════════════════════════════════════════════════════════
        private static Font _fraunces;
        private static Font _frauncesItalic;
        private static Font _inter;
        private static bool _loadAttempted;

        public static Font Fraunces       { get { EnsureLoaded(); return _fraunces; } }
        public static Font FrauncesItalic { get { EnsureLoaded(); return _frauncesItalic; } }
        public static Font Inter          { get { EnsureLoaded(); return _inter; } }

        public static bool FontsAvailable
        {
            get { EnsureLoaded(); return _fraunces != null && _inter != null; }
        }

        private static void EnsureLoaded()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            _fraunces       = LoadFont("Fraunces");
            _frauncesItalic = LoadFont("Fraunces-Italic");
            _inter          = LoadFont("Inter");

            // Silent fallback — if the package wasn't shipped with fonts, we use built-in.
            // No console warning; the design just degrades to system font automatically.
        }

        private static Font LoadFont(string nameWithoutExt)
        {
            // Try standard AssetDatabase path inside the package
            string[] paths = {
                "Packages/com.gamedistrict.codeshield/Editor/Brand/Fonts/" + nameWithoutExt + ".ttf",
                "Packages/com.gamedistrict.codeshield/Editor/Brand/Fonts/" + nameWithoutExt + ".otf",
                "Assets/Editor/Brand/Fonts/" + nameWithoutExt + ".ttf",
            };
            foreach (var p in paths)
            {
                if (!File.Exists(p)) continue;
                var f = AssetDatabase.LoadAssetAtPath<Font>(p);
                if (f != null) return f;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  BRAND ASSETS — Game District logo
        // ═══════════════════════════════════════════════════════════════════════
        private static Texture2D _gdLogo;
        private static bool _gdLogoAttempted;

        public static Texture2D GDLogo
        {
            get
            {
                if (_gdLogoAttempted) return _gdLogo;
                _gdLogoAttempted = true;

                string[] paths = {
                    "Packages/com.gamedistrict.codeshield/Editor/Brand/gd-logo.png",
                    "Assets/Editor/Brand/gd-logo.png",
                };
                foreach (var p in paths)
                {
                    if (!File.Exists(p)) continue;
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                    if (t != null) { _gdLogo = t; return _gdLogo; }
                }
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  TYPE SCALE — sizes match SPEC.md
        // ═══════════════════════════════════════════════════════════════════════
        public const int SizeH1       = 38;  // Fraunces 38px (down from 44px for IMGUI density)
        public const int SizeH2       = 26;
        public const int SizeH3       = 18;
        public const int SizeLede     = 16;  // Fraunces italic
        public const int SizeBody     = 13;
        public const int SizeUI       = 12;
        public const int SizeEyebrow  = 11;  // Inter 600 + tracking
        public const int SizeFootnote = 10;
        public const int SizeStatNum  = 22;  // Fraunces — stat numerals
        public const int SizeMono     = 12;

        // ═══════════════════════════════════════════════════════════════════════
        //  LAYOUT
        // ═══════════════════════════════════════════════════════════════════════
        public const float GoldBarWidth = 6f;   // full-height gold left-bar on every window
        public const float Hairline     = 1f;
        public const float PadEdge      = 36f;  // primary section padding
        public const float PadTop       = 24f;
        public const float EyebrowSquare = 7f;  // gold square prefix size

        // ═══════════════════════════════════════════════════════════════════════
        //  PRIMITIVE DRAWING — fills + hairlines
        // ═══════════════════════════════════════════════════════════════════════
        private static Texture2D _whiteTex;
        public static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null)
                {
                    _whiteTex = new Texture2D(1, 1);
                    _whiteTex.SetPixel(0, 0, Color.white);
                    _whiteTex.Apply();
                    _whiteTex.hideFlags = HideFlags.HideAndDontSave;
                }
                return _whiteTex;
            }
        }

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, WhiteTex);
            GUI.color = prev;
        }

        public static void HairlineH(float x, float y, float w, Color c)
        {
            Fill(new Rect(x, y, w, Hairline), c);
        }

        public static void HairlineV(float x, float y, float h, Color c)
        {
            Fill(new Rect(x, y, Hairline, h), c);
        }

        public static void Outline(Rect r, Color c)
        {
            HairlineH(r.x, r.y, r.width, c);
            HairlineH(r.x, r.y + r.height - 1, r.width, c);
            HairlineV(r.x, r.y, r.height, c);
            HairlineV(r.x + r.width - 1, r.y, r.height, c);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  STYLE FACTORIES — create GUIStyles with proper font + size + color
        // ═══════════════════════════════════════════════════════════════════════
        // Style cache — MakeStyle is called from per-frame IMGUI draw code across every
        // CodeShield window (120+ call sites). Allocating a GUIStyle per call meant
        // dozens of allocations per repaint → GC hitches felt as UI lag. Cached
        // instances are SHARED: callers must treat them as immutable and copy with
        // `new GUIStyle(s)` if they need a variant.
        private static readonly Dictionary<(int font, int size, uint color, FontStyle fs, TextAnchor anchor, bool wrap), GUIStyle>
            _styleCache = new Dictionary<(int, int, uint, FontStyle, TextAnchor, bool), GUIStyle>();

        public static GUIStyle MakeStyle(Font font, int size, Color color, FontStyle fontStyle = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft)
            => CachedStyle(font, size, color, fontStyle, anchor, wrap: false);

        public static GUIStyle MakeWrappedStyle(Font font, int size, Color color, FontStyle fontStyle = FontStyle.Normal)
            => CachedStyle(font, size, color, fontStyle, TextAnchor.UpperLeft, wrap: true);

        private static GUIStyle CachedStyle(Font font, int size, Color color, FontStyle fontStyle, TextAnchor anchor, bool wrap)
        {
            Color32 c = color;
            uint packed = (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
            var key = (font != null ? font.GetInstanceID() : 0, size, packed, fontStyle, anchor, wrap);
            if (_styleCache.TryGetValue(key, out var cached)) return cached;

            var s = new GUIStyle();
            if (font != null) s.font = font;
            s.fontSize  = size;
            s.fontStyle = fontStyle;
            s.alignment = anchor;
            s.normal.textColor = color;
            s.hover.textColor  = color;
            s.active.textColor = color;
            s.wordWrap  = wrap;
            s.richText  = false;
            _styleCache[key] = s;
            return s;
        }
    }
}