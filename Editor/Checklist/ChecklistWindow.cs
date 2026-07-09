// ChecklistWindow.cs  —  Tools → GD Checklist
// Scans project .asset files and compares against expected SDK configuration.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GDCodeShield.Brand;

namespace GDChecklist
{
    public class ChecklistWindow : EditorWindow
    {
        // ── Tabs ─────────────────────────────────────────────────────────────────
        private static readonly string[] Tabs = { "AppLovin / AdMob", "Metica", "Adjust", "AppMetrica", "Firebase", "Ad Units", "Pre-Release", "Build", "Manual" };
        private int _tab = 0;
        private SDKScanConfig _homeConfig  = null; // cached per screen-switch, not per frame
        private bool          _transitioning = false; // shows loading flash on screen change

        // ── Scroll ────────────────────────────────────────────────────────────────
        private Vector2 _scroll;

        // ── Tab indices ───────────────────────────────────────────────────────────
        // SDK tabs use 0-5 matching FieldResult.Tab values.
        // SDK Versions is a synthesis tab — sentinel index that doesn't match any FieldResult.
        private const int TAB_SDK_VERSIONS = -100;

        // ── Sub-tabs (Adjust only) ───────────────────────────────────────────────
        private int _adjustSubTab = 0;
        private static readonly string[] AdjustSubTabs =
            { "Config", "Init Path", "Manifest", "Dependencies", "iOS", "Ad Revenue" };

        // ── Scan results ──────────────────────────────────────────────────────────
        private ScanResult _scan = null;
        private bool _scanning = false;

        // ── GD Theme — editorial cream/navy/gold (v1.3.0 redesign)
        //    Field row renderers reference these names; map to new palette.
        private static readonly Color C_BG     = new Color32(238, 237, 230, 255); // Cream
        private static readonly Color C_SURF   = new Color32(238, 237, 230, 255); // Cream (no card lift)
        private static readonly Color C_SURF2  = new Color32(247, 246, 240, 255); // Slightly lighter cream for hover
        private static readonly Color C_BORDER = new Color32(211, 209, 199, 255); // Taupe hairline
        private static readonly Color C_ACCENT = new Color32(244, 196, 48,  255); // Gold #F4C430
        private static readonly Color C_GREEN  = new Color32(111, 167, 111, 255); // Sage — passing
        private static readonly Color C_RED    = new Color32(192, 57,  43,  255); // Overdue — failing
        private static readonly Color C_ORANGE = new Color32(207, 119, 24,  255); // Warm orange — warnings
        private static readonly Color C_TEXT   = new Color32(14,  26,  51,  255); // Navy
        private static readonly Color C_MUTED  = new Color32(107, 107, 102, 255); // WarmGray
        private static readonly Color C_BLUE   = new Color32(133, 183, 235, 255); // Sky — manual items

        private GUIStyle _sTitle, _sBody, _sMuted, _sCode;
        private bool _stylesReady;

        // ── Setup screen state ────────────────────────────────────────────────────
        private enum AppScreen { Welcome, Setup, Home, Results }
        private AppScreen _appScreen = AppScreen.Welcome;

        // Temporary toggles while on setup screen (not saved until confirmed)
        private bool _tmp_AppLovin, _tmp_Metica, _tmp_Adjust,
                     _tmp_AppMetrica, _tmp_Firebase, _tmp_AdUnits;

        public static void Open()
        {
            var w = GetWindow<ChecklistWindow>("  GD Checklist");
            w.minSize = new Vector2(900, 600);
            w.maxSize = new Vector2(4000, 4000); // resizable
            w._appScreen = AppScreen.Welcome;   // always start fresh
            w.Show();
        }

        private const string TOOL_VERSION    = "2.0"; // bump this to force re-setup
        private const string VERSION_PREF_KEY = "GDChecklist_Version";

        private void OnEnable()
        {
            _stylesReady = false;
            _appScreen   = AppScreen.Welcome;   // always reset to welcome on open — no cached scan state

            // If stored version differs — wipe old prefs
            string storedVersion = EditorPrefs.GetString(VERSION_PREF_KEY, "");
            if (storedVersion != TOOL_VERSION)
            {
                SDKConfig.ResetSetup();
                EditorPrefs.SetString(VERSION_PREF_KEY, TOOL_VERSION);
            }

            // Pre-fill temp toggles from saved state
            _tmp_AppLovin   = SDKConfig.ManualAppLovin;
            _tmp_Metica     = SDKConfig.ManualMetica;
            _tmp_Adjust     = SDKConfig.ManualAdjust;
            _tmp_AppMetrica = SDKConfig.ManualAppMetrica;
            _tmp_Firebase   = SDKConfig.ManualFirebase;
            _tmp_AdUnits    = SDKConfig.ManualAdUnits;

            // Pre-build the SDK config cache in the background while the user reads the welcome screen.
            // By the time any button is tapped, _cachedConfig is already populated — DrawHome is instant.
            var dataPath = Application.dataPath;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // Warm OS filesystem cache
                    Directory.GetFiles(dataPath, "*.asset", SearchOption.AllDirectories);
                    // Pre-build the config — SDKConfig.BuildScanConfig() is thread-safe for reads
                    SDKConfig.PrebuildConfigCache();
                }
                catch { /* silent — best-effort only */ }
            });
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DRAW
        // ════════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            // Immediate repaint on scroll/move so scrolling and hover track the mouse
            wantsMouseMove = true;
            var evtType = Event.current.type;
            if (evtType == EventType.ScrollWheel || evtType == EventType.MouseMove || evtType == EventType.MouseDrag)
                Repaint();

            InitStyles();
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            // 6px gold left-bar — full window height
            EditorGUI.DrawRect(new Rect(0, 0, BrandTokens.GoldBarWidth, position.height), C_ACCENT);

            // If we're mid-transition, show a loading overlay for exactly one frame,
            // then clear the flag and draw the real screen next frame.
            // This gives instant visual feedback on any button tap.
            if (_transitioning)
            {
                _transitioning = false;
                DrawTransitionOverlay();
                Repaint(); // immediately queue the real screen
                return;
            }

            DrawTopBar();

            float bodyY = 56f;
            var body = new Rect(BrandTokens.GoldBarWidth + 30, bodyY,
                                position.width - (BrandTokens.GoldBarWidth + 30) - 36,
                                position.height - bodyY - 42);

            switch (_appScreen)
            {
                case AppScreen.Welcome: DrawWelcomeScreen(body); break;
                case AppScreen.Setup:   DrawSetupScreen(body);   break;
                case AppScreen.Home:    DrawHome(body);          break;
                case AppScreen.Results: DrawResults(body);       break;
            }

            // Footer
            float fy = position.height - 42;
            float padL = BrandTokens.GoldBarWidth + 30;
            EditorGUI.DrawRect(new Rect(padL, fy, position.width - padL - 36, 1), C_BORDER);
            GUI.Label(new Rect(padL, fy + 14, 500, 14),
                "GAME DISTRICT  ·  GD CHECKLIST",
                BrandTokens.MakeStyle(BrandTokens.Inter, 10, C_MUTED));
        }

        private void DrawTransitionOverlay()
        {
            float w = position.width;
            float h = position.height;

            EditorGUI.DrawRect(new Rect(0, 0, w, h), new Color(238f/255f, 237f/255f, 230f/255f, 0.96f));

            float barW = 220f;
            float barH = 3f;
            float bx   = (w - barW) * 0.5f;
            float by   = h * 0.5f + 20f;

            GUI.Label(new Rect(0, h * 0.5f - 16f, w, 20f),
                "Loading…",
                BrandTokens.MakeStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                    14, C_MUTED, FontStyle.Italic, TextAnchor.MiddleCenter));

            EditorGUI.DrawRect(new Rect(bx, by, barW, barH), new Color(0.78f, 0.78f, 0.74f, 1f));
            float fill = (float)(EditorApplication.timeSinceStartup % 1.0);
            EditorGUI.DrawRect(new Rect(bx, by, barW * fill, barH), C_ACCENT);
        }

        // ── Top bar ───────────────────────────────────────────────────────────────

        private void DrawTopBar()
        {
            float padL = BrandTokens.GoldBarWidth + 30;
            float padR = 36;
            float ty = 18;

            // Brand mark + name
            DrawBrandMark(padL, ty);
            GUI.Label(new Rect(padL + 32, ty + 4, 240, 22), "CodeShield",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 16, C_TEXT, FontStyle.Bold));

            // Right side: action buttons + crumbs
            float rx = position.width - padR;

            // Rescan + Change Setup — results screen
            if (_appScreen == AppScreen.Results)
            {
                if (TopBtn(new Rect(rx - 80, 14, 76, 24), "↺ Rescan"))
                { _scan = null; _homeConfig = null; _appScreen = AppScreen.Home; Repaint(); }
                rx -= 88;
            }
            if (_appScreen == AppScreen.Home || _appScreen == AppScreen.Results)
            {
                if (TopBtn(new Rect(rx - 112, 14, 108, 24), "⚙ Change setup"))
                {
                    SDKConfig.ResetSetup();
                    _appScreen = AppScreen.Welcome;
                    Repaint();
                }
                rx -= 120;
            }

            // Crumbs — right aligned to remaining space
            string crumbs = _appScreen switch {
                AppScreen.Welcome => "Workstation  /  SDK checklist",
                AppScreen.Setup   => "SDK checklist  /  First-time setup",
                AppScreen.Home    => "SDK checklist  /  Ready",
                AppScreen.Results => "SDK checklist  /  Report",
                _ => ""
            };
            var crumbsStyle = BrandTokens.MakeStyle(BrandTokens.Inter, 11, C_MUTED, FontStyle.Normal, TextAnchor.MiddleRight);
            GUI.Label(new Rect(padL + 280, 22, rx - padL - 290, 18), crumbs, crumbsStyle);

            // Topbar hairline
            EditorGUI.DrawRect(new Rect(padL, 50, position.width - padL - padR, 1), C_BORDER);
        }

        // Tapered shield with gold check — drawn from primitives
        private void DrawBrandMark(float x, float y)
        {
            float sw = 22, sh = 26;
            EditorGUI.DrawRect(new Rect(x + 1, y, sw - 2, 1), C_TEXT);
            EditorGUI.DrawRect(new Rect(x, y, 1, sh - 6), C_TEXT);
            EditorGUI.DrawRect(new Rect(x + sw - 1, y, 1, sh - 6), C_TEXT);
            for (int i = 0; i < 6; i++)
            {
                float t = i / 6f;
                EditorGUI.DrawRect(new Rect(x + t * (sw / 2), y + sh - 6 + i, 1, 1.4f), C_TEXT);
                EditorGUI.DrawRect(new Rect(x + sw - 1 - t * (sw / 2), y + sh - 6 + i, 1, 1.4f), C_TEXT);
            }
            float ckX = x + 5, ckY = y + 13;
            for (int i = 0; i < 4; i++) EditorGUI.DrawRect(new Rect(ckX + i, ckY + i, 2, 2), C_ACCENT);
            for (int i = 0; i < 7; i++) EditorGUI.DrawRect(new Rect(ckX + 3 + i, ckY + 3 - i, 2, 2), C_ACCENT);
        }

        // Editorial eyebrow with gold square + tracked text
        private void DrawEyebrow(float x, float y, string label)
        {
            EditorGUI.DrawRect(new Rect(x, y + 4, BrandTokens.EyebrowSquare, BrandTokens.EyebrowSquare), C_ACCENT);
            string spaced = "";
            for (int i = 0; i < label.Length; i++)
            {
                spaced += label[i];
                if (i < label.Length - 1) spaced += "\u2009";
            }
            GUI.Label(new Rect(x + BrandTokens.EyebrowSquare + 10, y, 400, 18), spaced,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeEyebrow, C_TEXT, FontStyle.Bold));
        }

        // ── Home screen ───────────────────────────────────────────────────────────

        private void DrawHome(Rect body)
        {
            // Build config once per screen visit — never per frame
            if (_homeConfig == null) _homeConfig = SDKConfig.BuildScanConfig();

            float gap = 56f;
            float leftW = (body.width - gap) * 0.5f;
            float rightX = body.x + leftW + gap;
            float rightW = body.width - leftW - gap;

            // LEFT — title + lede + meta
            DrawEyebrow(body.x, body.y, "READY");
            float cy = body.y + 32;
            GUI.Label(new Rect(body.x, cy, leftW, 50), "Run the checks.",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, BrandTokens.SizeH1, C_TEXT, FontStyle.Bold));
            cy += 56;
            GUI.Label(new Rect(body.x, cy, leftW, 80),
                "We'll read your project's .asset files, manifests, and SDK configs, then compare against expected values.",
                BrandTokens.MakeWrappedStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                    BrandTokens.SizeLede, BrandTokens.Ink, FontStyle.Italic));
            cy += 90;

            // What it scans
            DrawEyebrow(body.x, cy, "WHAT WE SCAN");
            cy += 24;
            string[] items = new[] {
                _homeConfig.AppLovin   ? "→ AppLovin / AdMob mediation"   : null,
                _homeConfig.Metica     ? "→ Metica configuration"          : null,
                _homeConfig.Adjust     ? "→ Adjust attribution"            : null,
                _homeConfig.AppMetrica ? "→ AppMetrica analytics"          : null,
                _homeConfig.Firebase   ? "→ Firebase remote config"        : null,
                _homeConfig.AdUnits    ? "→ Ad units configuration"        : null,
                "→ Pre-release + build readiness",
                "→ Manual release checks"
            };
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item)) continue;
                GUI.Label(new Rect(body.x, cy, leftW, 18), item,
                    BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, BrandTokens.Ink));
                EditorGUI.DrawRect(new Rect(body.x, cy + 22, leftW, 1), C_BORDER);
                cy += 26;
            }

            // RIGHT — scan target summary + big scan button
            DrawEyebrow(rightX, body.y, "SCAN TARGET");
            float ry = body.y + 32;
            bool isNonGD = !_homeConfig.IsGDSDK;

            // Non-GD warning
            if (isNonGD)
            {
                var warnRect = new Rect(rightX, ry, rightW, 56);
                EditorGUI.DrawRect(new Rect(rightX, ry, 3, 56), C_ORANGE);
                GUI.Label(new Rect(rightX + 12, ry + 6, rightW - 16, 18),
                    "Extensive scan — non-GD SDK mode",
                    BrandTokens.MakeStyle(BrandTokens.Inter, 11, C_ORANGE, FontStyle.Bold));
                GUI.Label(new Rect(rightX + 12, ry + 24, rightW - 16, 30),
                    "Reads all .cs files to reverse-engineer SDK configuration.\nMay take 5–15 seconds on large projects.",
                    BrandTokens.MakeWrappedStyle(BrandTokens.Inter, 10, C_MUTED));
                ry += 72;
            }
            else
            {
                GUI.Label(new Rect(rightX, ry, rightW, 18),
                    "GD SDK mode — fast config lookups via known paths",
                    BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, C_MUTED));
                ry += 26;
            }

            ry += 10;
            EditorGUI.DrawRect(new Rect(rightX, ry, rightW, 1), C_BORDER);
            ry += 24;

            // Big gold scan button
            var btnR = new Rect(rightX, ry, rightW, 56);
            bool hover = btnR.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(btnR, hover ? new Color(244f/255f, 196f/255f, 48f/255f, 0.85f) : C_ACCENT);
            GUI.Label(btnR, "▶  SCAN PROJECT  →",
                BrandTokens.MakeStyle(BrandTokens.Inter, 14, C_TEXT, FontStyle.Bold, TextAnchor.MiddleCenter));
            EditorGUIUtility.AddCursorRect(btnR, MouseCursor.Link);
            if (Click(btnR)) { RunScan(); _appScreen = AppScreen.Results; }
            ry += 70;

            GUI.Label(new Rect(rightX, ry, rightW, 18),
                "No network calls. All checks run locally against your project files.",
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeFootnote, C_MUTED));
        }

        // ── Results ───────────────────────────────────────────────────────────────

        private void DrawResults(Rect body)
        {
            if (_scan == null) { _homeConfig = null; _appScreen = AppScreen.Home; return; }

            // Build active tab list from current scan config
            var cfg = _homeConfig ?? SDKConfig.BuildScanConfig();
            var activeTabs = new List<(string label, int index)>();
            // SDK Versions tab — always first, sentinel index -100 so it doesn't collide with FieldResult.Tab values
            activeTabs.Add(("SDK Versions", TAB_SDK_VERSIONS));
            if (cfg.AppLovin)   activeTabs.Add(("AppLovin / AdMob", 0));
            if (cfg.Metica)     activeTabs.Add(("Metica",           1));
            if (cfg.Adjust)     activeTabs.Add(("Adjust",           2));
            if (cfg.AppMetrica) activeTabs.Add(("AppMetrica",       3));
            if (cfg.Firebase)   activeTabs.Add(("Firebase",         4));
            if (cfg.AdUnits)    activeTabs.Add(("Ad Units",         5));
            // Always present — release checks
            activeTabs.Add(("Pre-Release", GDChecklist.ReleaseScanner.TAB_PRERELEASE));
            activeTabs.Add(("Build",       GDChecklist.ReleaseScanner.TAB_BUILD));
            activeTabs.Add(("Manual",      GDChecklist.ReleaseScanner.TAB_MANUAL));

            // Clamp active tab to valid range
            if (_tab >= activeTabs.Count) _tab = 0;

            // Tab bar — editorial
            float ty = body.y;
            float tx = body.x;
            for (int i = 0; i < activeTabs.Count; i++)
            {
                var (tabLabel, tabIndex) = activeTabs[i];
                bool isManualTab    = tabIndex == GDChecklist.ReleaseScanner.TAB_MANUAL;
                bool isReleaseTab   = tabIndex == GDChecklist.ReleaseScanner.TAB_PRERELEASE
                                   || tabIndex == GDChecklist.ReleaseScanner.TAB_BUILD;
                Color tabAccent = isManualTab ? C_BLUE : isReleaseTab ? C_ORANGE : C_RED;

                int issueCount = isManualTab
                    ? _scan.AllFields.Count(f => f.Tab == tabIndex && !_confirmed.Contains(f.FieldName))
                    : _scan.AllFields.Count(f => f.Tab == tabIndex
                        && (f.Status == FieldStatus.Mismatch || f.Status == FieldStatus.Empty
                            || f.Status == FieldStatus.Missing));

                string label = issueCount > 0
                    ? (isManualTab ? $"{tabLabel}  ☐{issueCount}" : $"{tabLabel}  ⚠{issueCount}")
                    : tabLabel;
                float tw = Mathf.Max(label.Length * 7.4f + 20f, 90f);
                bool active = i == _tab;

                Color labelC = active ? C_TEXT : (issueCount > 0 ? tabAccent : C_MUTED);
                FontStyle fs = active ? FontStyle.Bold : FontStyle.Normal;

                GUI.Label(new Rect(tx, ty, tw, 36), label,
                    BrandTokens.MakeStyle(BrandTokens.Inter, 12, labelC, fs, TextAnchor.MiddleCenter));

                if (active)
                    EditorGUI.DrawRect(new Rect(tx, ty + 33, tw, 2), C_TEXT);

                EditorGUIUtility.AddCursorRect(new Rect(tx, ty, tw, 36), MouseCursor.Link);
                if (!active && Click(new Rect(tx, ty, tw, 36))) { _tab = i; Repaint(); }
                tx += tw + 4;
            }
            EditorGUI.DrawRect(new Rect(body.x, ty + 35, body.width, 1), C_BORDER);

            // Get fields for the currently active tab
            int currentTabIndex = activeTabs.Count > 0 ? activeTabs[_tab].index : -1;

            // ── Adjust sub-tabs ─────────────────────────────────────────────────────
            // When the Adjust main tab is active, draw a sub-tab bar that splits results
            // into Config (existing) + 5 Integration sub-tabs (new validator output).
            const int TAB_ADJUST = 2;
            float subTabH = 0f;
            string activeSubTab = null;
            if (currentTabIndex == TAB_ADJUST)
            {
                subTabH = 30f;
                activeSubTab = DrawAdjustSubTabBar(body, ty + 36);
            }

            var fields = currentTabIndex >= 0
                ? _scan.AllFields.Where(f => f.Tab == currentTabIndex).ToList()
                : new List<FieldResult>();

            // Filter by active Adjust sub-tab
            if (currentTabIndex == TAB_ADJUST && activeSubTab != null)
            {
                if (activeSubTab == "Config")
                {
                    // Config = anything without an integration SubTab (legacy ScanAdjust output)
                    fields = fields.Where(f => string.IsNullOrEmpty(f.SubTab)).ToList();
                }
                else
                {
                    fields = fields.Where(f => f.SubTab == activeSubTab).ToList();
                }
            }

            // Calculate actual content height so scroll always covers all fields
            // Each field row: 36 normal, 72 mismatch, 60 editing + 4 gap
            // Section headers: 18px each, plus 6px gap after each section
            float contentH = 16; // top padding

            // Reserve scroll height for SDK Versions synthesis tab (header + stats + sections)
            if (currentTabIndex == TAB_SDK_VERSIONS)
            {
                int totalLines = 0;
                int sectionCount = 5;

                var gdMon = SDKVersionDetector.GetVersionForTab(-1);
                if (gdMon != null)
                {
                    sectionCount++;
                    int gdMods = gdMon.Modules?.Count ?? 0;
                    totalLines += gdMods > 0 ? gdMods : 1;
                }

                for (int i = 0; i <= 4; i++)
                {
                    var sv = SDKVersionDetector.GetVersionForTab(i);
                    int modCount = sv?.Modules?.Count ?? 0;
                    totalLines += modCount > 0 ? modCount : 1;
                }
                // New layout: header block (28 eyebrow + 52 H1 + 56 stats + 18 divider) +
                //             each section: name (32) + lines * 30 + section gap (12)
                contentH += 28f + 52f + 56f + 18f + sectionCount * (32f + 12f) + totalLines * 30f;
            }

            string lastSec = null;
            foreach (var f in fields)
            {
                if (f.Section != lastSec) { contentH += 18 + 6; lastSec = f.Section; } // section label
                bool editing = _editingFieldKey == FieldKey(f);
                bool isEmpty = f.Status == FieldStatus.Empty;
                bool isManual = f.YamlKey == "manual";
                bool isIntegration = f.YamlKey == "integration";
                bool isRelease = f.Tab == GDChecklist.ReleaseScanner.TAB_PRERELEASE
                              || f.Tab == GDChecklist.ReleaseScanner.TAB_BUILD;
                bool editingMissing = editing && (f.Status == FieldStatus.Missing || isEmpty);
                float rh;
                if (isIntegration)
                {
                    // Estimate integration row height similarly to DrawIntegrationRow
                    string detail = f.ExpectedValue ?? "";
                    bool hasSnippet = !string.IsNullOrEmpty(f.FixSnippet);
                    bool hasPath    = !string.IsNullOrEmpty(f.AssetPath);
                    int detailLines = string.IsNullOrEmpty(detail) ? 0 : Mathf.CeilToInt(detail.Length / 85f) + detail.Count(c => c == '\n');
                    float detailH  = detailLines * 13f;
                    float snippetH = hasSnippet ? Mathf.Max(40f, f.FixSnippet.Count(c => c == '\n') * 12f + 24f) : 0f;
                    float pathH    = hasPath ? 18f : 0f;
                    rh = 36f + detailH + snippetH + pathH + 8f;
                }
                else
                {
                    rh = isManual ? 54f
                       : isRelease ? (f.Status == FieldStatus.Mismatch || f.Status == FieldStatus.Missing ? 66f : 54f)
                       : (editingMissing ? 72f : editing ? 60f : f.Status == FieldStatus.Mismatch ? 72f : (f.Status == FieldStatus.Missing || isEmpty) ? 60f : 36f);
                }
                contentH += rh + 4f;
            }
            contentH += 80; // bottom padding — generous buffer to keep last row fully visible above the scroll edge

            _scroll = GUI.BeginScrollView(
                new Rect(body.x, ty + 36 + subTabH, body.width, body.height - 36 - subTabH),
                _scroll, new Rect(0, 0, body.width - 16, contentH));

            float y = 16;

            // SDK Versions synthesis tab — separate from any SDK's field list.
            if (currentTabIndex == TAB_SDK_VERSIONS)
            {
                DrawSdkVersionsTab(24, ref y, body.width - 48);
            }
            else if (fields.Count == 0)
                GUI.Label(new Rect(24, y, body.width - 48, 24), "No fields scanned for this SDK.", _sMuted);
            else
                DrawFieldGroup(fields, 24, ref y, body.width - 48);

            GUI.EndScrollView();
        }

        // ── SDK Versions tab renderer ─────────────────────────────────────────────
        // Lists each SDK as a full-width section. Replaces per-tab version headers.
        private void DrawSdkVersionsTab(float x, ref float y, float w)
        {
            // Collect all SDKs once so we can compute totals + render
            var allSdks = new List<SDKVersion>();
            var gdMon = SDKVersionDetector.GetVersionForTab(-1);
            if (gdMon != null) allSdks.Add(gdMon);
            for (int tabIndex = 0; tabIndex <= 4; tabIndex++)
            {
                var sv = SDKVersionDetector.GetVersionForTab(tabIndex);
                if (sv == null) sv = new SDKVersion { Name = SdkNameForTab(tabIndex) };
                allSdks.Add(sv);
            }

            // Tally: modules detected, on target, drift
            int modulesDetected = 0, modulesOnTarget = 0, modulesDrift = 0;
            foreach (var sdk in allSdks)
            {
                if (sdk.Modules != null && sdk.Modules.Count > 0)
                {
                    foreach (var m in sdk.Modules)
                    {
                        if (string.IsNullOrEmpty(m.version)) continue;
                        modulesDetected++;
                        modulesOnTarget++; // any detected version is "on target" until drift detection lands
                    }
                }
                else if (!string.IsNullOrEmpty(sdk.Version))
                {
                    modulesDetected++;
                    modulesOnTarget++;
                }
            }

            // ── Editorial header — eyebrow + H1 ──────────────────────────────
            EditorGUI.DrawRect(new Rect(x, y + 4, BrandTokens.EyebrowSquare, BrandTokens.EyebrowSquare), C_ACCENT);
            string spacedEyebrow = "I\u2009N\u2009S\u2009T\u2009A\u2009L\u2009L\u2009E\u2009D";
            GUI.Label(new Rect(x + BrandTokens.EyebrowSquare + 10, y, 300, 18), spacedEyebrow,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeEyebrow, C_TEXT, FontStyle.Bold));
            y += 28;

            // H1 — Fraunces
            GUI.Label(new Rect(x, y, w, 50), "SDK versions.",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 32, C_TEXT, FontStyle.Bold));
            y += 52;

            // Stats row — Total / On target / Drift (matches HTML mock)
            DrawSdkStat(x,        y, modulesDetected.ToString(),                   "MODULES DETECTED", C_TEXT);
            DrawSdkStat(x + 200,  y, modulesOnTarget.ToString(),                   "ON TARGET",         C_GREEN);
            DrawSdkStat(x + 360,  y, modulesDrift.ToString(),                      "DRIFT",             C_RED);
            y += 56;

            EditorGUI.DrawRect(new Rect(x, y, w, 1), C_BORDER);
            y += 18;

            // ── SDK sections — render in order, GD Monetization first ──────
            foreach (var sdk in allSdks)
            {
                DrawSdkSection(sdk, x, ref y, w);
                y += 12;
            }
        }

        // Editorial stat — big serif numeral over small uppercase label
        private void DrawSdkStat(float x, float y, string num, string label, Color numColor)
        {
            GUI.Label(new Rect(x, y, 180, 32), num,
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 30, numColor, FontStyle.Bold));
            GUI.Label(new Rect(x, y + 36, 180, 14), label,
                BrandTokens.MakeStyle(BrandTokens.Inter, 10, C_MUTED, FontStyle.Bold));
        }

        // Renders one SDK section — editorial: gold left-bar, Fraunces name, mono rows
        private void DrawSdkSection(SDKVersion v, float x, ref float y, float w)
        {
            int modCount = v.Modules?.Count ?? 0;
            bool hasSingleVersion = modCount == 0 && !string.IsNullOrEmpty(v.Version);
            int lines = modCount > 0 ? modCount : 1;

            const float nameH    = 32f;
            const float lineH    = 30f;
            const float padLeft  = 18f;
            float sectionH = nameH + lines * lineH + 4f;

            // 3px gold left-bar — full section height
            EditorGUI.DrawRect(new Rect(x, y, 3, sectionH), C_ACCENT);

            // SDK name in Fraunces
            GUI.Label(new Rect(x + padLeft, y, w - padLeft, nameH), v.Name,
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 17, C_TEXT, FontStyle.Bold));
            float ly = y + nameH;

            // Module rows — mono name on left, mono version + green check on right
            if (modCount > 0)
            {
                for (int i = 0; i < v.Modules.Count; i++)
                {
                    var m = v.Modules[i];
                    bool isLast = (i == v.Modules.Count - 1);
                    DrawSdkRow(x + padLeft, ly, w - padLeft, m.label, "v" + m.version, isLast);
                    ly += lineH;
                }
            }
            else if (hasSingleVersion)
            {
                // Single version (e.g. GD Monetization) — package id on left, version on right
                string packageId = !string.IsNullOrEmpty(v.Source)
                    ? "com.gamedistrict.monetization"
                    : v.Name.ToLower().Replace(" ", "");
                DrawSdkRow(x + padLeft, ly, w - padLeft, packageId, "v" + v.Version, true);
                ly += lineH;
            }
            else
            {
                GUI.Label(new Rect(x + padLeft, ly + 4, w - padLeft, lineH),
                    "Not detected",
                    BrandTokens.MakeStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                        13, C_MUTED, FontStyle.Italic));
                EditorGUI.DrawRect(new Rect(x + padLeft, ly + lineH - 1, w - padLeft, 1), C_BORDER);
                ly += lineH;
            }

            y = ly;
        }

        // One row of an SDK section — mono label + mono version + green check, hairline below
        private void DrawSdkRow(float x, float y, float w, string label, string version, bool isLast)
        {
            // Hairline below row (visual separator between modules)
            EditorGUI.DrawRect(new Rect(x, y + 29, w, 1), C_BORDER);

            // Label on the left (mono)
            GUI.Label(new Rect(x, y + 7, w * 0.6f, 18), label,
                new GUIStyle(_sCode) { fontSize = 12, normal = { textColor = C_TEXT } });

            // Version + check on right
            float checkW = 18f;
            var versionStyle = new GUIStyle(_sCode) {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = C_TEXT },
                alignment = TextAnchor.MiddleRight
            };
            GUI.Label(new Rect(x + w * 0.6f, y + 7, w * 0.4f - checkW - 8, 18), version, versionStyle);

            // Green checkmark on the far right
            GUI.Label(new Rect(x + w - checkW, y + 7, checkW, 18), "✓",
                BrandTokens.MakeStyle(BrandTokens.Inter, 13, C_GREEN, FontStyle.Bold, TextAnchor.MiddleRight));
        }

        // ── SDK name helper (used by SDK Versions tab) ────────────────────────────
        private string SdkNameForTab(int tabIndex)
        {
            switch (tabIndex)
            {
                case 0: return "AppLovin MAX";
                case 1: return "Metica";
                case 2: return "Adjust";
                case 3: return "AppMetrica";
                case 4: return "Firebase";
                default: return "SDK";
            }
        }

        // ── Adjust sub-tab bar ─────────────────────────────────────────────────────
        // Drawn directly under the main tab bar when Adjust main tab is active.
        // Returns the label of the currently selected sub-tab (so DrawResults can filter).
        private string DrawAdjustSubTabBar(Rect body, float yStart)
        {
            // Build per-subtab issue counts
            var counts = new int[AdjustSubTabs.Length];
            foreach (var f in _scan.AllFields)
            {
                if (f.Tab != 2) continue;
                if (f.Status != FieldStatus.Mismatch && f.Status != FieldStatus.Missing
                    && f.Status != FieldStatus.Empty) continue;

                string sub = string.IsNullOrEmpty(f.SubTab) ? "Config" : f.SubTab;
                for (int i = 0; i < AdjustSubTabs.Length; i++)
                {
                    if (AdjustSubTabs[i] == sub) { counts[i]++; break; }
                }
            }

            Bg(new Rect(body.x, yStart, body.width, 30), C_SURF2);

            float sx = body.x + 16;
            for (int i = 0; i < AdjustSubTabs.Length; i++)
            {
                string lbl = AdjustSubTabs[i];
                int issues = counts[i];
                bool active = i == _adjustSubTab;
                Color col = issues > 0 ? C_ORANGE : (active ? C_ACCENT : C_MUTED);

                string text = issues > 0 ? $"{lbl}  ⚠{issues}" : lbl;
                float tw = Mathf.Max(text.Length * 6.6f + 18f, 70f);

                if (active)
                {
                    Bg(new Rect(sx, yStart + 26, tw, 3), C_ACCENT);
                    GUI.Label(new Rect(sx, yStart, tw, 30), text,
                        new GUIStyle(_sBody)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = 10,
                            fontStyle = FontStyle.Bold,
                            normal = { textColor = col }
                        });
                }
                else
                {
                    GUI.Label(new Rect(sx, yStart, tw, 30), text,
                        new GUIStyle(_sMuted)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = 10,
                            normal = { textColor = col }
                        });
                    if (Click(new Rect(sx, yStart, tw, 30)))
                    {
                        _adjustSubTab = i;
                        _scroll = Vector2.zero;
                        Repaint();
                    }
                }
                sx += tw + 2;
            }

            HRule(new Rect(body.x, yStart + 29, body.width, 1), C_BORDER);

            // Clamp sub-tab index if out of bounds
            if (_adjustSubTab < 0 || _adjustSubTab >= AdjustSubTabs.Length) _adjustSubTab = 0;
            return AdjustSubTabs[_adjustSubTab];
        }

        private void DrawFieldGroup(List<FieldResult> fields, float x, ref float y, float w)
        {
            // Group by section
            var sections = fields.GroupBy(f => f.Section).ToList();

            foreach (var sec in sections)
            {
                // Section header
                GUI.Label(new Rect(x, y, w, 14), sec.Key.ToUpper(),
                    new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.65f) } });
                y += 18;

                foreach (var f in sec)
                {
                    if (f.YamlKey == "manual")
                        DrawManualRow(f, x, ref y, w);
                    else if (f.Tab == GDChecklist.ReleaseScanner.TAB_PRERELEASE
                          || f.Tab == GDChecklist.ReleaseScanner.TAB_BUILD)
                        DrawReleaseRow(f, x, ref y, w);
                    else
                        DrawFieldRow(f, x, ref y, w);
                }
                y += 6;
            }
        }

        // ── Edit state ────────────────────────────────────────────────────────────
        private string _editingFieldKey = null;
        private string _editBuffer      = "";
        private System.Collections.Generic.HashSet<string> _confirmed = new System.Collections.Generic.HashSet<string>();

        private string FieldKey(FieldResult f) => $"{f.Tab}_{f.Section}_{f.FieldName}_{f.Platform}";

        private void DrawFieldRow(FieldResult f, float x, ref float y, float w)
        {
            // Integration rows (Adjust Integration scanner) use a different layout —
            // they have rich detail text + optional copy-fix button, no Expected/Found diff.
            if (f.YamlKey == "integration")
            {
                DrawIntegrationRow(f, x, ref y, w);
                return;
            }

            bool editing = _editingFieldKey == FieldKey(f);
            bool isEmpty = f.Status == FieldStatus.Empty;

            Color rowBg  = f.Status == FieldStatus.Match    ? new Color(C_GREEN.r,  C_GREEN.g,  C_GREEN.b,  0.05f)
                         : f.Status == FieldStatus.Mismatch ? new Color(C_RED.r,    C_RED.g,    C_RED.b,    0.07f)
                         : f.Status == FieldStatus.Missing  ? new Color(C_ORANGE.r, C_ORANGE.g, C_ORANGE.b, 0.06f)
                         : isEmpty                          ? new Color(C_ORANGE.r, C_ORANGE.g, C_ORANGE.b, 0.05f)
                         :                                    new Color(C_MUTED.r,  C_MUTED.g,  C_MUTED.b,  0.04f);

            Color sidCol = f.Status == FieldStatus.Match    ? C_GREEN
                         : f.Status == FieldStatus.Mismatch ? C_RED
                         : f.Status == FieldStatus.Missing  ? C_ORANGE
                         : isEmpty                          ? C_ORANGE
                         : C_MUTED;

            string icon = f.Status == FieldStatus.Match    ? "✓"
                        : f.Status == FieldStatus.Mismatch ? "✗"
                        : f.Status == FieldStatus.Missing  ? "?"
                        : isEmpty                          ? "⊘"
                        : "—";

            // Row height: mismatch = 72, empty = 60 (has paste field), editing = 60, default = 36
            bool editingMissing = _editingFieldKey == FieldKey(f) && (f.Status == FieldStatus.Missing || isEmpty);
            float rowH = editingMissing ? 72f
                       : editing ? 60f
                       : f.Status == FieldStatus.Mismatch ? 72f
                       : (f.Status == FieldStatus.Missing || isEmpty) ? 60f
                       : 36f;

            Bg(new Rect(x, y, w, rowH), editing ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.06f) : rowBg);
            Outline(new Rect(x, y, w, rowH), editing
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f)
                : new Color(sidCol.r, sidCol.g, sidCol.b, 0.25f));
            Bg(new Rect(x, y, 3, rowH), editing ? C_ACCENT : sidCol);

            // Icon
            GUI.Label(new Rect(x + 10, y + (rowH/2) - 8, 16, 16), editing ? "✏" : icon,
                new GUIStyle(_sBody) { normal = { textColor = editing ? C_ACCENT : sidCol } });

            // Field name
            GUI.Label(new Rect(x + 30, y + 8, 200, 16), f.FieldName,
                new GUIStyle(_sBody) { fontSize = 11, fontStyle = FontStyle.Bold });

            // Platform badge
            if (!string.IsNullOrEmpty(f.Platform))
                DrawPill(new Rect(x + 234, y + 9, 50, 16), f.Platform,
                    f.Platform == "Android" ? C_GREEN : C_ACCENT);

            // ── Edit button — show for normal fields that have an asset path ────────
            bool canEdit = !string.IsNullOrEmpty(f.AssetPath) && !string.IsNullOrEmpty(f.YamlKey);
            if (canEdit && !editing && !isEmpty)
            {
                if (Btn(new Rect(x + w - 58, y + 8, 48, 20), "✏ Edit", C_ACCENT))
                {
                    _editingFieldKey = FieldKey(f);
                    _editBuffer      = f.ProjectValue ?? "";
                    Repaint();
                }
            }

            // Status label
            if ((!canEdit || editing) && !isEmpty)
                GUI.Label(new Rect(x + w - 100, y + 9, 90, 16), f.Status.ToString(),
                    new GUIStyle(_sMuted) { fontSize = 10, alignment = TextAnchor.MiddleRight,
                        normal = { textColor = sidCol } });

            // ── Inline edit mode ─────────────────────────────────────────────────
            if (editing)
            {
                GUI.Label(new Rect(x + 30, y + 26, 50, 14), "Value:",
                    new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = C_ACCENT } });

                _editBuffer = EditorGUI.TextField(
                    new Rect(x + 84, y + 24, w - 260, 20), _editBuffer);

                if (Btn(new Rect(x + w - 166, y + 24, 72, 22), "✓  Save", C_GREEN))
                {
                    SaveEditedValue(f, _editBuffer);
                    _editingFieldKey = null;
                    _editBuffer = "";
                }
                if (Btn(new Rect(x + w - 88, y + 24, 72, 22), "Cancel", C_MUTED))
                {
                    _editingFieldKey = null;
                    _editBuffer = "";
                    Repaint();
                }
            }
            // ── Not found / empty — offer locate, paste, or rescan ───────────────
            else if (isEmpty || f.Status == FieldStatus.Missing)
            {
                bool editingThis = _editingFieldKey == FieldKey(f);

                if (!editingThis)
                {
                    // Hint line
                    string hint = f.Status == FieldStatus.Missing
                        ? "Not found — locate the .asset or .cs file, or paste the value directly"
                        : $"Key '{f.YamlKey}' is empty in {System.IO.Path.GetFileName(f.AssetPath)}";
                    GUI.Label(new Rect(x + 30, y + 24, w - 270, 14), hint,
                        new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = C_ORANGE } });

                    // 📂 Locate File — browse to .asset or .cs file
                    if (Btn(new Rect(x + w - 264, y + 24, 96, 22), "📂  Locate File", C_ORANGE))
                    {
                        string located = EditorUtility.OpenFilePanel(
                            $"Locate file for {f.FieldName}",
                            Application.dataPath, "asset,cs");
                        if (!string.IsNullOrEmpty(located) && System.IO.File.Exists(located))
                        {
                            string ext = System.IO.Path.GetExtension(located).ToLower();
                            string foundValue = ext == ".cs"
                                ? AssetScanner.ReadCsValue(located, f.YamlKey)
                                : AssetScanner.ReadYamlKey(located, f.YamlKey);
                            f.AssetPath    = located;
                            f.ProjectValue = foundValue;
                            f.Status = string.IsNullOrEmpty(foundValue)
                                ? FieldStatus.Missing : FieldStatus.Match;
                            Repaint();
                        }
                    }

                    // ✎ Enter Value — paste from code
                    if (Btn(new Rect(x + w - 162, y + 24, 100, 22), "✎  Enter Value", C_ORANGE))
                    {
                        _editingFieldKey = FieldKey(f);
                        _editBuffer = f.ProjectValue ?? "";
                        Repaint();
                    }

                    // ↺ Rescan
                    if (Btn(new Rect(x + w - 56, y + 24, 46, 22), "↺", C_MUTED))
                    { _scan = null; RunScan(); }
                }
                else
                {
                    GUI.Label(new Rect(x + 30, y + 24, w - 160, 14),
                        "Paste the value from your file or script:",
                        new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = C_ORANGE } });

                    _editBuffer = EditorGUI.TextField(
                        new Rect(x + 30, y + 42, w - 140, 22), _editBuffer);

                    if (Btn(new Rect(x + w - 104, y + 42, 46, 22), "Save", C_GREEN))
                    {
                        if (!string.IsNullOrWhiteSpace(_editBuffer))
                        {
                            f.ProjectValue = _editBuffer.Trim();
                            f.Status = FieldStatus.Match;
                        }
                        _editingFieldKey = null;
                        Repaint();
                    }
                    if (Btn(new Rect(x + w - 52, y + 42, 46, 22), "✕", C_MUTED))
                    { _editingFieldKey = null; Repaint(); }
                }
            }
            else if (f.Status == FieldStatus.Match)
            {
                string val = f.ProjectValue?.Length > 72 ? f.ProjectValue.Substring(0, 72) + "…" : f.ProjectValue ?? "—";
                GUI.Label(new Rect(x + 30, y + 24, w - 100, 14), val,
                    new GUIStyle(_sMuted) { fontSize = 10 });
            }
            else if (f.Status == FieldStatus.Mismatch)
            {
                GUI.Label(new Rect(x + 30, y + 24, 60, 12), "Expected:",
                    new GUIStyle(_sMuted) { fontSize = 9, normal = { textColor = C_GREEN } });
                string exp = f.ExpectedValue?.Length > 55 ? f.ExpectedValue.Substring(0, 55) + "…" : f.ExpectedValue ?? "—";
                GUI.Label(new Rect(x + 94, y + 24, w - 280, 12), exp,
                    new GUIStyle(_sCode) { fontSize = 9, normal = { textColor = C_GREEN } });

                GUI.Label(new Rect(x + 30, y + 38, 60, 12), "Found:",
                    new GUIStyle(_sMuted) { fontSize = 9, normal = { textColor = C_RED } });
                string found = f.ProjectValue?.Length > 55 ? f.ProjectValue.Substring(0, 55) + "…" : f.ProjectValue ?? "(empty)";
                GUI.Label(new Rect(x + 94, y + 38, w - 280, 12), found,
                    new GUIStyle(_sCode) { fontSize = 9, normal = { textColor = C_RED } });

                if (Btn(new Rect(x + w - 214, y + 24, 100, 22), "Use Expected", C_GREEN))
                    ApplyValue(f, useExpected: true);
                if (Btn(new Rect(x + w - 108, y + 24, 96, 22), "Keep Existing", C_MUTED))
                { f.Status = FieldStatus.Ignored; Repaint(); }
            }

            y += rowH + 4;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  INTEGRATION ROW — for Adjust Integration scanner output
        // ════════════════════════════════════════════════════════════════════════

        private void DrawIntegrationRow(FieldResult f, float x, ref float y, float w)
        {
            Color sidCol = f.Status == FieldStatus.Match    ? C_GREEN
                         : f.Status == FieldStatus.Mismatch ? C_RED
                         : f.Status == FieldStatus.Missing  ? C_ORANGE
                         :                                    C_MUTED;
            Color bgCol  = new Color(sidCol.r, sidCol.g, sidCol.b, 0.06f);

            string icon  = f.Status == FieldStatus.Match    ? "✓"
                         : f.Status == FieldStatus.Mismatch ? "✗"
                         : f.Status == FieldStatus.Missing  ? "?"
                         :                                    "—";

            // Compute dynamic height based on content
            // ─ Field name line: 18 ─ Project value line: 14 ─ Detail (wrapped): per ~70 chars
            string detail = f.ExpectedValue ?? "";
            bool hasSnippet = !string.IsNullOrEmpty(f.FixSnippet);
            bool hasPath    = !string.IsNullOrEmpty(f.AssetPath);

            // Rough wrap calc — assume ~85 chars per line at 10px font
            int detailLines = string.IsNullOrEmpty(detail) ? 0 : Mathf.CeilToInt(detail.Length / 85f) + detail.Count(c => c == '\n');
            float detailH   = detailLines * 13f;
            float snippetH  = hasSnippet ? Mathf.Max(40f, f.FixSnippet.Count(c => c == '\n') * 12f + 24f) : 0f;
            float pathH     = hasPath ? 18f : 0f;
            float rowH      = 36f + detailH + snippetH + pathH + 8f;

            Bg(new Rect(x, y, w, rowH), bgCol);
            Outline(new Rect(x, y, w, rowH), new Color(sidCol.r, sidCol.g, sidCol.b, 0.30f));
            Bg(new Rect(x, y, 3, rowH), sidCol);

            // Icon + Field name
            GUI.Label(new Rect(x + 12, y + 8, 20, 20), icon,
                new GUIStyle(_sBody) { fontSize = 13, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter, normal = { textColor = sidCol } });
            GUI.Label(new Rect(x + 36, y + 7, w - 120, 18), f.FieldName,
                new GUIStyle(_sBody) { fontSize = 11, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_TEXT } });

            // Status pill
            string pillText = f.Status == FieldStatus.Match    ? "Match"
                            : f.Status == FieldStatus.Mismatch ? "Issue"
                            : f.Status == FieldStatus.Missing  ? "Missing"
                            :                                    "—";
            DrawPill(new Rect(x + w - 74, y + 8, 64, 16), pillText, sidCol);

            // Project value (one-liner summary)
            string projVal = f.ProjectValue ?? "";
            if (projVal.Length > 110) projVal = projVal.Substring(0, 110) + "…";
            GUI.Label(new Rect(x + 36, y + 26, w - 60, 14), projVal,
                new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = sidCol } });

            float dy = y + 42f;

            // Detail (multi-line wrapped)
            if (!string.IsNullOrEmpty(detail))
            {
                GUI.Label(new Rect(x + 36, dy, w - 60, detailH + 4),
                    detail,
                    new GUIStyle(_sMuted) { fontSize = 10, wordWrap = true });
                dy += detailH;
            }

            // Fix snippet (XML to copy)
            if (hasSnippet)
            {
                dy += 4;
                var snipRect = new Rect(x + 36, dy, w - 160, snippetH - 4);
                Bg(snipRect, new Color(0.05f, 0.05f, 0.05f, 0.8f));
                Outline(snipRect, new Color(sidCol.r, sidCol.g, sidCol.b, 0.4f));
                GUI.Label(new Rect(snipRect.x + 6, snipRect.y + 4, snipRect.width - 12, snipRect.height - 8),
                    f.FixSnippet,
                    new GUIStyle(_sCode) { fontSize = 9, wordWrap = true,
                        normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } });

                if (Btn(new Rect(x + w - 116, dy + (snippetH - 28) / 2, 100, 24),
                        "📋  Copy Fix", C_ACCENT))
                {
                    EditorGUIUtility.systemCopyBuffer = f.FixSnippet;
                    ShowNotification(new GUIContent("Copied to clipboard"));
                }
                dy += snippetH;
            }

            // Asset / file path with Open button
            if (hasPath)
            {
                string pathText = f.AssetPath;
                if (f.LineNumber > 0) pathText += $":{f.LineNumber}";
                GUI.Label(new Rect(x + 36, dy + 2, w - 130, 14), pathText,
                    new GUIStyle(_sMuted) { fontSize = 9,
                        normal = { textColor = new Color(C_BLUE.r, C_BLUE.g, C_BLUE.b, 0.85f) } });
                if (Btn(new Rect(x + w - 86, dy, 70, 18), "Open", C_MUTED))
                {
                    var assetPath = f.AssetPath;
                    // If absolute path under Application.dataPath, convert to relative
                    string projRoot = Application.dataPath.Replace('\\', '/');
                    string normPath = assetPath.Replace('\\', '/');
                    if (normPath.StartsWith(projRoot))
                        assetPath = "Assets" + normPath.Substring(projRoot.Length);
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    if (asset != null)
                    {
                        if (f.LineNumber > 0) AssetDatabase.OpenAsset(asset, f.LineNumber);
                        else AssetDatabase.OpenAsset(asset);
                    }
                    else
                    {
                        EditorUtility.RevealInFinder(f.AssetPath);
                    }
                }
            }

            y += rowH + 4;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  MANUAL ROW — device verification item with Confirm button
        // ════════════════════════════════════════════════════════════════════════

        private void DrawManualRow(FieldResult f, float x, ref float y, float w)
        {
            bool done  = _confirmed.Contains(f.FieldName);
            Color col  = done ? C_GREEN : C_BLUE;
            float rowH = 54f;

            Bg(new Rect(x, y, w, rowH),      new Color(col.r, col.g, col.b, done ? 0.07f : 0.04f));
            Outline(new Rect(x, y, w, rowH), new Color(col.r, col.g, col.b, 0.25f));
            Bg(new Rect(x, y, 3, rowH), col);

            GUI.Label(new Rect(x + 10, y + 8, 20, 20), done ? "✓" : "☐",
                new GUIStyle(_sBody) { fontSize = 14, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = col } });

            GUI.Label(new Rect(x + 34, y + 7, w - 200, 18), f.FieldName,
                new GUIStyle(_sBody) { fontSize = 11, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_TEXT } });

            DrawPill(new Rect(x + w - 74, y + 8, 64, 16), done ? "Done" : "Manual",
                done ? C_GREEN : C_BLUE);

            GUI.Label(new Rect(x + 34, y + 27, w - 120, 14), f.ExpectedValue,
                new GUIStyle(_sMuted) { fontSize = 10,
                    normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.8f) } });

            if (!done)
            {
                if (Btn(new Rect(x + w - 106, y + rowH - 26, 96, 20), "✓  Confirm", C_BLUE))
                { _confirmed.Add(f.FieldName); Repaint(); }
            }
            else
            {
                if (Btn(new Rect(x + w - 80, y + rowH - 26, 70, 20), "↺ Undo", C_MUTED))
                { _confirmed.Remove(f.FieldName); Repaint(); }
            }
            y += rowH + 4;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  RELEASE ROW — Pre-Release / Build settings auto-check
        // ════════════════════════════════════════════════════════════════════════

        private void DrawReleaseRow(FieldResult f, float x, ref float y, float w)
        {
            Color col  = f.Status == FieldStatus.Match    ? C_GREEN
                       : f.Status == FieldStatus.Mismatch ? C_RED
                       : f.Status == FieldStatus.Missing  ? C_ORANGE
                       : C_MUTED;

            string icon = f.Status == FieldStatus.Match    ? "✓"
                        : f.Status == FieldStatus.Mismatch ? "✗"
                        : f.Status == FieldStatus.Missing  ? "⚠"
                        : "—";

            bool hasFix = f.Status != FieldStatus.Match && !string.IsNullOrEmpty(f.ExpectedValue);
            float rowH  = hasFix ? 66f : 54f;

            Bg(new Rect(x, y, w, rowH),      new Color(col.r, col.g, col.b,
                f.Status == FieldStatus.Mismatch ? 0.07f : 0.04f));
            Outline(new Rect(x, y, w, rowH), new Color(col.r, col.g, col.b, 0.25f));
            Bg(new Rect(x, y, 3, rowH), col);

            GUI.Label(new Rect(x + 10, y + (rowH/2) - 8, 16, 16), icon,
                new GUIStyle(_sBody) { normal = { textColor = col } });

            GUI.Label(new Rect(x + 30, y + 8, w - 160, 16), f.FieldName,
                new GUIStyle(_sBody) { fontSize = 11, fontStyle = FontStyle.Bold });

            DrawPill(new Rect(x + w - 80, y + 8, 70, 16),
                f.Status == FieldStatus.Match ? "Pass" :
                f.Status == FieldStatus.Mismatch ? "Fail" :
                f.Status == FieldStatus.Missing ? "Warn" : "—", col);

            // Detail value
            GUI.Label(new Rect(x + 30, y + 26, w - 100, 14), f.ProjectValue ?? "",
                new GUIStyle(_sMuted) { fontSize = 10,
                    normal = { textColor = new Color(col.r, col.g, col.b, 0.85f) } });

            // Fix instruction on fail/warn
            if (hasFix)
                GUI.Label(new Rect(x + 30, y + 42, w - 100, 13), "Fix: " + f.ExpectedValue,
                    new GUIStyle(_sMuted) { fontSize = 9,
                        normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.65f) } });

            y += rowH + 4;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  WELCOME SCREEN — first question: GD SDK or not?
        // ════════════════════════════════════════════════════════════════════════

        private void DrawWelcomeScreen(Rect body)
        {
            float gap = 56f;
            float leftW = (body.width - gap) * 0.45f;
            float rightX = body.x + leftW + gap;
            float rightW = body.width - leftW - gap;

            // LEFT
            DrawEyebrow(body.x, body.y, "WELCOME");
            float cy = body.y + 32;
            GUI.Label(new Rect(body.x, cy, leftW, 52), "Are you using",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, BrandTokens.SizeH1, C_TEXT, FontStyle.Bold));
            cy += 46;
            GUI.Label(new Rect(body.x, cy, leftW, 52), "the GD SDK?",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, BrandTokens.SizeH1, C_TEXT, FontStyle.Bold));
            cy += 56;
            GUI.Label(new Rect(body.x, cy, leftW, 80),
                "We'll tailor the checklist to your setup — GD-wrapped paths and raw SDK installs read configuration from different places.",
                BrandTokens.MakeWrappedStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                    BrandTokens.SizeLede, BrandTokens.Ink, FontStyle.Italic));
            cy += 90;

            DrawEyebrow(body.x, cy, "WHAT THIS AFFECTS");
            cy += 24;
            string[] items = {
                "→ Which config paths the scanner reads from",
                "→ Required vs optional manifest entries",
                "→ GD-wrapper specific warnings",
                "→ Default expected version ranges"
            };
            foreach (var item in items)
            {
                GUI.Label(new Rect(body.x, cy, leftW, 18), item,
                    BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, BrandTokens.Ink));
                EditorGUI.DrawRect(new Rect(body.x, cy + 22, leftW, 1), C_BORDER);
                cy += 26;
            }

            // RIGHT — two big editorial choice rows
            float ry = body.y + 48;

            // YES option
            var yesRect = new Rect(rightX, ry, rightW, 110);
            bool yesHover = yesRect.Contains(Event.current.mousePosition);
            if (yesHover) EditorGUI.DrawRect(yesRect, BrandTokens.GoldTint);
            DrawRectOutline(yesRect, yesHover ? C_ACCENT : C_BORDER);
            EditorGUI.DrawRect(new Rect(yesRect.x, yesRect.y, 4, yesRect.height), C_ACCENT);

            // Shield icon
            DrawShieldGlyph(yesRect.x + 24, yesRect.y + 24);
            GUI.Label(new Rect(yesRect.x + 76, yesRect.y + 22, rightW - 180, 24),
                "Yes, using GD SDK",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 18, C_TEXT, FontStyle.Bold));
            GUI.Label(new Rect(yesRect.x + 76, yesRect.y + 48, rightW - 180, 50),
                "Read configs from GDMonetization wrapper. Auto-detect Metica + Adjust + Firebase wiring.",
                BrandTokens.MakeWrappedStyle(BrandTokens.Inter, BrandTokens.SizeBody, C_MUTED));
            string yesLabel = yesHover ? "USE  →" : "USE";
            GUI.Label(new Rect(yesRect.x + rightW - 80, yesRect.y + 44, 70, 22),
                yesLabel,
                BrandTokens.MakeStyle(BrandTokens.Inter, 12, C_TEXT, FontStyle.Bold, TextAnchor.MiddleRight));
            EditorGUIUtility.AddCursorRect(yesRect, MouseCursor.Link);

            if (Click(yesRect))
            {
                SDKConfig.IsGDSDK = true;
                SDKConfig.InvalidateConfigCache();
                _tmp_AppLovin   = SDKConfig.HasAppLovinFast();
                _tmp_Metica     = SDKConfig.HasMeticaFast();
                _tmp_Adjust     = SDKConfig.HasAdjustFast();
                _tmp_AppMetrica = SDKConfig.HasAppMetricaFast();
                _tmp_Firebase   = SDKConfig.HasFirebaseFast();
                _tmp_AdUnits    = SDKConfig.HasAdUnitsFast();
                if (!_tmp_AppLovin && !_tmp_Metica && !_tmp_Adjust &&
                    !_tmp_AppMetrica && !_tmp_Firebase && !_tmp_AdUnits)
                {
                    _tmp_AppLovin = _tmp_Metica = _tmp_Adjust =
                    _tmp_AppMetrica = _tmp_Firebase = _tmp_AdUnits = true;
                }
                _transitioning = true;
                _appScreen = AppScreen.Setup;
                Repaint();
            }

            ry += 130;

            // NO option
            var noRect = new Rect(rightX, ry, rightW, 110);
            bool noHover = noRect.Contains(Event.current.mousePosition);
            if (noHover) EditorGUI.DrawRect(noRect, BrandTokens.GoldTint);
            DrawRectOutline(noRect, noHover ? C_ACCENT : C_BORDER);
            EditorGUI.DrawRect(new Rect(noRect.x, noRect.y, 4, noRect.height), C_MUTED);

            // List/stack glyph
            DrawListGlyph(noRect.x + 24, noRect.y + 24);
            GUI.Label(new Rect(noRect.x + 76, noRect.y + 22, rightW - 180, 24),
                "No, raw SDKs",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 18, C_TEXT, FontStyle.Bold));
            GUI.Label(new Rect(noRect.x + 76, noRect.y + 48, rightW - 180, 50),
                "Read configs directly from each SDK's own files. Manual setup for tokens and keys.",
                BrandTokens.MakeWrappedStyle(BrandTokens.Inter, BrandTokens.SizeBody, C_MUTED));
            string noLabel = noHover ? "USE  →" : "USE";
            GUI.Label(new Rect(noRect.x + rightW - 80, noRect.y + 44, 70, 22),
                noLabel,
                BrandTokens.MakeStyle(BrandTokens.Inter, 12, C_TEXT, FontStyle.Bold, TextAnchor.MiddleRight));
            EditorGUIUtility.AddCursorRect(noRect, MouseCursor.Link);

            if (Click(noRect))
            {
                SDKConfig.IsGDSDK = false;
                _transitioning = true;
                _appScreen = AppScreen.Setup;
                Repaint();
            }
        }

        // ── Decorative glyphs for the welcome choice cards ───────────────────────
        private void DrawShieldGlyph(float x, float y)
        {
            float sw = 36, sh = 42;
            EditorGUI.DrawRect(new Rect(x + 2, y, sw - 4, 1.5f), C_TEXT);
            EditorGUI.DrawRect(new Rect(x, y, 1.5f, sh - 10), C_TEXT);
            EditorGUI.DrawRect(new Rect(x + sw - 1.5f, y, 1.5f, sh - 10), C_TEXT);
            for (int i = 0; i < 10; i++)
            {
                float t = i / 10f;
                EditorGUI.DrawRect(new Rect(x + t * (sw / 2), y + sh - 10 + i, 1.5f, 1.5f), C_TEXT);
                EditorGUI.DrawRect(new Rect(x + sw - 1.5f - t * (sw / 2), y + sh - 10 + i, 1.5f, 1.5f), C_TEXT);
            }
            float ckX = x + 8, ckY = y + 20;
            for (int i = 0; i < 6; i++) EditorGUI.DrawRect(new Rect(ckX + i, ckY + i, 3, 3), C_ACCENT);
            for (int i = 0; i < 11; i++) EditorGUI.DrawRect(new Rect(ckX + 5 + i, ckY + 5 - i, 3, 3), C_ACCENT);
        }

        private void DrawListGlyph(float x, float y)
        {
            float w = 36;
            for (int i = 0; i < 3; i++)
            {
                EditorGUI.DrawRect(new Rect(x - 6, y + 6 + i * 12, 2, 2), C_ACCENT);
                EditorGUI.DrawRect(new Rect(x,     y + 6 + i * 12, w, 2), C_TEXT);
            }
        }

        private void DrawRectOutline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x + r.width - 1, r.y, 1, r.height), c);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SETUP SCREEN — first launch SDK picker
        // ════════════════════════════════════════════════════════════════════════

        private void DrawSetupScreen(Rect body)
        {
            float gap = 56f;
            float leftW = (body.width - gap) * 0.4f;
            float rightX = body.x + leftW + gap;
            float rightW = body.width - leftW - gap;

            // LEFT — header + question + stats
            DrawEyebrow(body.x, body.y, "FIRST-TIME SETUP");
            float cy = body.y + 32;
            GUI.Label(new Rect(body.x, cy, leftW, 36), "Which SDKs",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 28, C_TEXT, FontStyle.Bold));
            cy += 32;
            GUI.Label(new Rect(body.x, cy, leftW, 36), "do you have",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 28, C_TEXT, FontStyle.Bold));
            cy += 32;
            GUI.Label(new Rect(body.x, cy, leftW, 36), "installed?",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 28, C_TEXT, FontStyle.Bold));
            cy += 48;
            GUI.Label(new Rect(body.x, cy, leftW, 60),
                "We've auto-detected what we could find. Confirm or adjust — you can change this any time from the Change setup button.",
                BrandTokens.MakeWrappedStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                    15, BrandTokens.Ink, FontStyle.Italic));

            // Stats row at bottom
            int selected = (_tmp_AppLovin ? 1 : 0) + (_tmp_Metica ? 1 : 0) + (_tmp_Adjust ? 1 : 0)
                         + (_tmp_AppMetrica ? 1 : 0) + (_tmp_Firebase ? 1 : 0) + (_tmp_AdUnits ? 1 : 0);
            float statsY = body.y + body.height - 80;
            EditorGUI.DrawRect(new Rect(body.x, statsY - 14, leftW, 1), C_BORDER);
            DrawStat(body.x,       statsY, selected.ToString(), "SELECTED");
            DrawStat(body.x + 100, statsY, "~10s",               "EST. SCAN");

            // RIGHT — SDK toggles as 2-column grid
            float ry = body.y + 4;
            float colGap = 24f;
            float colW = (rightW - colGap) / 2f;

            DrawSDKToggleRow(rightX,             ry, colW, "AppLovin / AdMob",
                "Ad mediation + AdMob App IDs", ref _tmp_AppLovin);
            DrawSDKToggleRow(rightX + colW + colGap, ry, colW, "Metica",
                "Monetization analytics & config", ref _tmp_Metica);
            ry += 76;

            DrawSDKToggleRow(rightX,             ry, colW, "Adjust",
                "Attribution & event tracking", ref _tmp_Adjust);
            DrawSDKToggleRow(rightX + colW + colGap, ry, colW, "AppMetrica",
                "Yandex analytics platform", ref _tmp_AppMetrica);
            ry += 76;

            DrawSDKToggleRow(rightX,             ry, colW, "Firebase",
                "Remote config, analytics, crash", ref _tmp_Firebase);
            DrawSDKToggleRow(rightX + colW + colGap, ry, colW, "Ad Units",
                "AdUnitsSettings.asset", ref _tmp_AdUnits);
            ry += 92;

            // Back + Confirm
            bool anySelected = _tmp_AppLovin || _tmp_Metica || _tmp_Adjust ||
                               _tmp_AppMetrica || _tmp_Firebase || _tmp_AdUnits;

            var backR = new Rect(rightX, ry, 100, 36);
            if (TopBtn(backR, "← Back"))
            {
                _transitioning = true;
                _appScreen = AppScreen.Welcome;
                Repaint();
            }

            var confirmR = new Rect(rightX + rightW - 220, ry, 220, 36);
            bool hover = confirmR.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(confirmR, anySelected
                ? (hover ? new Color(244f/255f, 196f/255f, 48f/255f, 0.85f) : C_ACCENT)
                : new Color(211f/255f, 209f/255f, 199f/255f, 1f));
            GUI.Label(confirmR, anySelected ? "Confirm & continue  →" : "Select at least one SDK",
                BrandTokens.MakeStyle(BrandTokens.Inter, 12,
                    anySelected ? C_TEXT : C_MUTED, FontStyle.Bold, TextAnchor.MiddleCenter));
            if (anySelected) EditorGUIUtility.AddCursorRect(confirmR, MouseCursor.Link);

            if (anySelected && Click(confirmR))
            {
                SDKConfig.ManualAppLovin   = _tmp_AppLovin;
                SDKConfig.ManualMetica     = _tmp_Metica;
                SDKConfig.ManualAdjust     = _tmp_Adjust;
                SDKConfig.ManualAppMetrica = _tmp_AppMetrica;
                SDKConfig.ManualFirebase   = _tmp_Firebase;
                SDKConfig.ManualAdUnits    = _tmp_AdUnits;
                SDKConfig.IsSetupDone      = true;
                SDKConfig.InvalidateConfigCache();
                _homeConfig = null;
                _transitioning = true;
                _appScreen = AppScreen.Home;
                Repaint();
            }
        }

        // Editorial SDK toggle — square checkbox + name + description
        private void DrawSDKToggleRow(float x, float y, float w, string name, string desc, ref bool value)
        {
            var rowRect = new Rect(x, y, w, 60);
            bool hover = rowRect.Contains(Event.current.mousePosition);
            if (hover) EditorGUI.DrawRect(rowRect, BrandTokens.GoldTint);
            EditorGUI.DrawRect(new Rect(x, y + 60, w, 1), C_BORDER);

            // Square checkbox
            var cb = new Rect(x + 4, y + 18, 18, 18);
            DrawRectOutline(cb, C_TEXT);
            if (value)
            {
                EditorGUI.DrawRect(new Rect(cb.x + 2, cb.y + 2, 14, 14), C_TEXT);
                for (int i = 0; i < 4; i++) EditorGUI.DrawRect(new Rect(cb.x + 3 + i, cb.y + 8 + i, 2, 2), C_ACCENT);
                for (int i = 0; i < 6; i++) EditorGUI.DrawRect(new Rect(cb.x + 6 + i, cb.y + 11 - i, 2, 2), C_ACCENT);
            }

            GUI.Label(new Rect(x + 32, y + 12, w - 40, 18), name,
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 15, C_TEXT, FontStyle.Bold));
            GUI.Label(new Rect(x + 32, y + 32, w - 40, 16), desc,
                BrandTokens.MakeStyle(BrandTokens.Inter, 11, C_MUTED));

            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
            if (Click(rowRect)) { value = !value; Repaint(); }
        }

        private void DrawStat(float x, float y, string num, string label)
        {
            GUI.Label(new Rect(x, y, 90, 28), num,
                BrandTokens.MakeStyle(BrandTokens.Fraunces, BrandTokens.SizeStatNum, C_TEXT, FontStyle.Bold));
            GUI.Label(new Rect(x, y + 26, 100, 14), label,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeFootnote, C_MUTED));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SCANNER
        // ════════════════════════════════════════════════════════════════════════

        private void RunScan()
        {
            ScanProgress.Begin("GD Checklist");
            SDKVersionDetector.ClearCache();
            try
            {
                ScanProgress.Report("Detecting SDKs…", 0.05f);
                // BuildScanConfigWithProgress shows per-SDK steps — safe here since we're not in OnGUI
                var config   = SDKConfig.BuildScanConfigWithProgress();
                var dataPath = Application.dataPath;

                ScanProgress.Report("Scanning SDK configurations…", 0.4f);
                var result = AssetScanner.Scan(dataPath, null, config);

                ScanProgress.Report("Running release checks…", 0.88f);
                GDChecklist.ReleaseScanner.AppendChecks(result, dataPath);

                ScanProgress.Report("Done", 1f);
                _scan      = result;
                _scanning  = false;
                _confirmed.Clear();
                _tab       = 0;
                _appScreen = AppScreen.Results;
                Repaint();

                // Fire silent telemetry — SDK versions for the GD studio dashboard.
                // Runs after detector cache is warm; non-blocking, errors swallowed.
                ChecklistTelemetry.ReportChecklistScanCompleted();
            }
            finally
            {
                ScanProgress.End();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  APPLY VALUE
        // ════════════════════════════════════════════════════════════════════════

        private void SaveEditedValue(FieldResult f, string newValue)
        {
            if (string.IsNullOrEmpty(f.AssetPath) || string.IsNullOrEmpty(f.YamlKey)) return;
            if (!File.Exists(f.AssetPath)) return;

            try
            {
                string content = File.ReadAllText(f.AssetPath);
                string pattern = $@"(^\s*{Regex.Escape(f.YamlKey)}:\s*)(.+)$";
                string replaced = Regex.Replace(content, pattern, $"${{1}}{newValue}", RegexOptions.Multiline);

                if (replaced == content)
                {
                    Debug.LogWarning($"[GD Checklist] Key '{f.YamlKey}' not found in {f.AssetPath}");
                    return;
                }

                File.WriteAllText(f.AssetPath, replaced);
                f.ProjectValue = newValue;
                f.Status = string.IsNullOrEmpty(f.ExpectedValue) || f.ExpectedValue.Trim() == newValue.Trim()
                    ? FieldStatus.Match
                    : FieldStatus.Mismatch;

                AssetDatabase.Refresh();
                Repaint();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GD Checklist] Failed to save value: {ex.Message}");
            }
        }

        private void ApplyValue(FieldResult f, bool useExpected)
        {
            if (string.IsNullOrEmpty(f.AssetPath) || string.IsNullOrEmpty(f.YamlKey)) return;

            string val = useExpected ? f.ExpectedValue : f.ProjectValue;
            if (val == null) return;

            try
            {
                string content = File.ReadAllText(f.AssetPath);
                // Replace the yaml key value
                string pattern = $@"({Regex.Escape(f.YamlKey)}:\s*)(.+)";
                string replaced = Regex.Replace(content, pattern, $"${{1}}{val}");
                File.WriteAllText(f.AssetPath, replaced);
                f.ProjectValue = val;
                f.Status = FieldStatus.Match;
                AssetDatabase.Refresh();
                Repaint();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GD Checklist] Failed to apply value: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  UI HELPERS
        // ════════════════════════════════════════════════════════════════════════

        private void DrawPill(Rect r, string text, Color col)
        {
            Bg(r, new Color(col.r, col.g, col.b, 0.15f));
            Outline(r, new Color(col.r, col.g, col.b, 0.45f));
            GUI.Label(r, text, new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = col }
            });
        }

        private bool Btn(Rect r, string text, Color col)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Bg(r, new Color(col.r, col.g, col.b, hover ? 0.25f : 0.15f));
            Outline(r, new Color(col.r, col.g, col.b, hover ? 0.6f : 0.35f));
            GUI.Label(r, text, new GUIStyle(_sMuted)
            {
                fontSize = 9, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = col }
            });
            return Click(r);
        }

        private bool TopBtn(Rect r, string text)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover) EditorGUI.DrawRect(r, BrandTokens.GoldTint);
            DrawRectOutline(r, hover ? C_TEXT : C_BORDER);
            GUI.Label(r, text,
                BrandTokens.MakeStyle(BrandTokens.Inter, 11, C_TEXT, FontStyle.Bold, TextAnchor.MiddleCenter));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            return Click(r);
        }

        private void Bg(Rect r, Color c)    => EditorGUI.DrawRect(r, c);
        private void HRule(Rect r, Color c) => EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
        private void Outline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x,           r.y,            r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x,           r.y+r.height-1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x,           r.y,            1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x+r.width-1, r.y,            1, r.height), c);
        }
        private bool Click(Rect r)
        {
            if (Event.current.type == EventType.MouseDown
                && r.Contains(Event.current.mousePosition) && Event.current.button == 0)
            { Event.current.Use(); return true; }
            return false;
        }

        private void InitStyles()
        {
            if (_stylesReady) return;
            var fraunces = BrandTokens.Fraunces;
            var inter    = BrandTokens.Inter;

            _sTitle = BrandTokens.MakeStyle(fraunces, 14, C_TEXT, FontStyle.Bold);
            _sBody  = BrandTokens.MakeWrappedStyle(inter, 12, C_TEXT);
            _sMuted = BrandTokens.MakeWrappedStyle(inter, 11, C_MUTED);
            _sCode  = new GUIStyle(EditorStyles.label) {
                fontSize = 11, wordWrap = false,
                normal = { textColor = C_TEXT },
                font = Font.CreateDynamicFontFromOSFont(new[]{"Consolas","Courier New","Monaco"}, 11)
            };
            _stylesReady = true;
        }
    }
}