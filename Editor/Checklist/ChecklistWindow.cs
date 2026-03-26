// ChecklistWindow.cs  —  Tools → GD Checklist
// Scans project .asset files and compares against expected SDK configuration.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GDChecklist
{
    public class ChecklistWindow : EditorWindow
    {
        // ── Tabs ─────────────────────────────────────────────────────────────────
        private static readonly string[] Tabs = { "AppLovin / AdMob", "Metica", "Adjust", "AppMetrica", "Firebase", "Ad Units" };
        private int _tab = 0;

        // ── Scroll ────────────────────────────────────────────────────────────────
        private Vector2 _scroll;

        // ── Scan results ──────────────────────────────────────────────────────────
        private ScanResult _scan = null;
        private bool _scanning = false;

        // ── GD Theme ─────────────────────────────────────────────────────────────
        private static readonly Color C_BG     = new Color32(10,  10,  10,  255);
        private static readonly Color C_SURF   = new Color32(22,  22,  22,  255);
        private static readonly Color C_SURF2  = new Color32(32,  32,  32,  255);
        private static readonly Color C_BORDER = new Color32(58,  58,  58,  255);
        private static readonly Color C_ACCENT = new Color32(255, 208, 0,   255); // GD Yellow
        private static readonly Color C_GREEN  = new Color32(80,  200, 100, 255);
        private static readonly Color C_RED    = new Color32(220, 60,  60,  255);
        private static readonly Color C_ORANGE = new Color32(255, 140, 40,  255);
        private static readonly Color C_TEXT   = new Color32(240, 240, 240, 255);
        private static readonly Color C_MUTED  = new Color32(140, 140, 140, 255);

        private GUIStyle _sTitle, _sBody, _sMuted, _sCode;
        private bool _stylesReady;

        // ── JSON import state ─────────────────────────────────────────────────────
        private string _jsonInput    = "";
        private bool   _showJsonInput = false;

        // ── Setup screen state ────────────────────────────────────────────────────
        private enum AppScreen { Welcome, Setup, Home, Results }
        private AppScreen _appScreen = AppScreen.Welcome;

        // Temporary toggles while on setup screen (not saved until confirmed)
        private bool _tmp_AppLovin, _tmp_Metica, _tmp_Adjust,
                     _tmp_AppMetrica, _tmp_Firebase, _tmp_AdUnits;

        public static void Open()
        {
            var w = GetWindow<ChecklistWindow>("GD Checklist");
            w.minSize = new Vector2(900, 560);
            w.Show();
        }

        private const string TOOL_VERSION    = "2.0"; // bump this to force re-setup
        private const string VERSION_PREF_KEY = "GDChecklist_Version";

        private void OnEnable()
        {
            _stylesReady = false;

            // If stored version differs from current — wipe old prefs and show welcome
            string storedVersion = EditorPrefs.GetString(VERSION_PREF_KEY, "");
            if (storedVersion != TOOL_VERSION)
            {
                SDKConfig.ResetSetup();
                EditorPrefs.SetString(VERSION_PREF_KEY, TOOL_VERSION);
                _appScreen = AppScreen.Welcome;
            }
            else if (!SDKConfig.IsSetupDone || !EditorPrefs.HasKey("GDChecklist_IsGDSDK"))
            {
                _appScreen = AppScreen.Welcome;
            }
            else
            {
                _appScreen = AppScreen.Home;
            }

            // Pre-fill temp toggles from saved state
            _tmp_AppLovin   = SDKConfig.ManualAppLovin;
            _tmp_Metica     = SDKConfig.ManualMetica;
            _tmp_Adjust     = SDKConfig.ManualAdjust;
            _tmp_AppMetrica = SDKConfig.ManualAppMetrica;
            _tmp_Firebase   = SDKConfig.ManualFirebase;
            _tmp_AdUnits    = SDKConfig.ManualAdUnits;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DRAW
        // ════════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            InitStyles();
            Bg(new Rect(0, 0, position.width, position.height), C_BG);

            DrawTopBar();

            float bodyY = 50f;
            var body = new Rect(0, bodyY, position.width, position.height - bodyY);

            switch (_appScreen)
            {
                case AppScreen.Welcome: DrawWelcomeScreen(body); break;
                case AppScreen.Setup:   DrawSetupScreen(body);   break;
                case AppScreen.Home:    DrawHome(body);          break;
                case AppScreen.Results: DrawResults(body);       break;
            }
        }

        // ── Top bar ───────────────────────────────────────────────────────────────

        private void DrawTopBar()
        {
            Bg(new Rect(0, 0, position.width, 50), C_SURF);
            Bg(new Rect(0, 0, position.width, 3), C_ACCENT);
            HRule(new Rect(0, 49, position.width, 1), C_BORDER);

            GUI.Label(new Rect(16, 11, 20, 26), "⚡",
                new GUIStyle(_sTitle) { fontSize = 18, normal = { textColor = C_ACCENT } });
            GUI.Label(new Rect(36, 13, 220, 24), "GD CHECKLIST",
                new GUIStyle(_sTitle) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = C_TEXT } });

            // Status pill — only on results screen
            if (_appScreen == AppScreen.Results && _scan != null)
            {
                int issues = _scan.AllFields.Count(f => f.Status == FieldStatus.Mismatch || f.Status == FieldStatus.Empty);
                int ok     = _scan.AllFields.Count(f => f.Status == FieldStatus.Match);
                Color pc   = issues == 0 ? C_GREEN : C_RED;
                string pt  = issues == 0 ? $"✓  {ok} OK" : $"⚠  {issues} mismatch(es)";
                var pr = new Rect(220, 14, 110, 22);
                Bg(pr, new Color(pc.r, pc.g, pc.b, 0.15f));
                Outline(pr, new Color(pc.r, pc.g, pc.b, 0.5f));
                GUI.Label(pr, pt, new GUIStyle(_sMuted)
                {
                    fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = pc }
                });
            }

            // GAME DISTRICT label
            GUI.Label(new Rect(position.width - 130, 16, 120, 18), "GAME DISTRICT",
                new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.55f) } });

            float rx = position.width - 14;

            // Rescan button — results screen
            if (_appScreen == AppScreen.Results)
            {
                if (TopBtn(new Rect(rx - 82, 11, 74, 28), "↺  Rescan"))
                { _scan = null; _appScreen = AppScreen.Home; Repaint(); }
                rx -= 90;
            }

            // Change SDKs / setup button — visible for all devs on home + results
            if (_appScreen == AppScreen.Home || _appScreen == AppScreen.Results)
            {
                if (TopBtn(new Rect(rx - 114, 11, 106, 28), "⚙  Change Setup"))
                {
                    SDKConfig.ResetSetup();
                    _appScreen = AppScreen.Welcome;
                    Repaint();
                }
            }
        }

        // ── Home screen ───────────────────────────────────────────────────────────

        private void DrawHome(Rect body)
        {
            float cx = body.x + body.width / 2f;
            float cy = body.y + body.height / 2f;
            float cw = 480f, ch = _showJsonInput ? 480f : 280f;

            Bg(new Rect(cx - cw/2 - 5, cy - ch/2 - 5, cw + 10, ch + 10), C_ACCENT);
            var card = new Rect(cx - cw/2, cy - ch/2, cw, ch);
            Bg(card, C_SURF);

            float iy = card.y + 20;
            Bg(new Rect(card.x, iy, cw, 3), C_ACCENT); iy += 10;

            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 13), "C H E C K P O I N T",
                new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_ACCENT } });
            iy += 18;

            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 32), "GD CHECKLIST",
                new GUIStyle(_sTitle) { fontSize = 22, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_TEXT } });
            iy += 34;

            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 16),
                "Scans project .asset files and validates SDK configuration",
                new GUIStyle(_sMuted) { fontSize = 10 });
            iy += 28;

            Bg(new Rect(card.x + 20, iy, cw - 40, 1),
               new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f)); iy += 14;

            // Scan button
            var btnR = new Rect(card.x + 20, iy, cw - 40, 44);
            Bg(btnR, C_ACCENT);
            if (btnR.Contains(Event.current.mousePosition)) Bg(btnR, new Color(1,1,1,0.08f));
            GUI.Label(btnR, "▶  SCAN PROJECT",
                new GUIStyle(_sTitle) { fontSize = 13, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color32(10, 10, 10, 255) } });
            if (Click(btnR)) { RunScan(); _appScreen = AppScreen.Results; }
            iy += 52;

            // JSON paste toggle
            if (TopBtn(new Rect(card.x + 20, iy, 200, 26), _showJsonInput ? "▲  Hide JSON Import" : "▼  Import from JSON"))
            { _showJsonInput = !_showJsonInput; Repaint(); }
            iy += 34;

            if (_showJsonInput)
            {
                GUI.Label(new Rect(card.x + 20, iy, cw - 40, 14),
                    "Paste expected config JSON — tool will compare against project:",
                    new GUIStyle(_sMuted) { fontSize = 10 }); iy += 18;

                _jsonInput = EditorGUI.TextArea(new Rect(card.x + 20, iy, cw - 40, 100), _jsonInput);
                iy += 108;

                if (TopBtn(new Rect(card.x + 20, iy, 130, 26), "Import & Scan"))
                { RunScanWithJson(_jsonInput); }
            }

            // Footer
            float sy = card.y + ch - 28;
            Bg(new Rect(card.x, sy, cw, 1), C_BORDER);
            GUI.Label(new Rect(card.x + 20, sy + 7, 220, 14),
                "ESTD. 2016  ·  STAY HUNGRY · STAY FOOLISH",
                new GUIStyle(_sMuted) { fontSize = 7,
                    normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.45f) } });
            GUI.Label(new Rect(card.x, sy + 6, cw - 18, 16), "⚡ GAME DISTRICT",
                new GUIStyle(_sTitle) { fontSize = 10, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.65f) } });
        }

        // ── Results ───────────────────────────────────────────────────────────────

        private void DrawResults(Rect body)
        {
            if (_scan == null) { _appScreen = AppScreen.Home; return; }

            // Build active tab list from current scan config
            var cfg = SDKConfig.BuildScanConfig();
            var activeTabs = new List<(string label, int index)>();
            if (cfg.AppLovin)   activeTabs.Add(("AppLovin / AdMob", 0));
            if (cfg.Metica)     activeTabs.Add(("Metica",           1));
            if (cfg.Adjust)     activeTabs.Add(("Adjust",           2));
            if (cfg.AppMetrica) activeTabs.Add(("AppMetrica",       3));
            if (cfg.Firebase)   activeTabs.Add(("Firebase",         4));
            if (cfg.AdUnits)    activeTabs.Add(("Ad Units",         5));

            // Clamp active tab to valid range
            if (_tab >= activeTabs.Count) _tab = 0;

            // Tab bar
            float ty = body.y;
            Bg(new Rect(body.x, ty, body.width, 36), C_SURF);

            float tx = body.x + 12;
            for (int i = 0; i < activeTabs.Count; i++)
            {
                var (tabLabel, tabIndex) = activeTabs[i];
                int issueCount = _scan.AllFields
                    .Count(f => f.Tab == tabIndex && (f.Status == FieldStatus.Mismatch || f.Status == FieldStatus.Empty));

                string label = issueCount > 0 ? $"{tabLabel}  ⚠{issueCount}" : tabLabel;
                float tw = 140f;
                bool active = i == _tab;

                if (active)
                {
                    Bg(new Rect(tx, ty + 32, tw, 4), C_ACCENT);
                    GUI.Label(new Rect(tx, ty, tw, 36), label,
                        new GUIStyle(_sBody) { alignment = TextAnchor.MiddleCenter,
                            fontStyle = FontStyle.Bold,
                            normal = { textColor = issueCount > 0 ? C_ORANGE : C_ACCENT } });
                }
                else
                {
                    GUI.Label(new Rect(tx, ty, tw, 36), label,
                        new GUIStyle(_sMuted) { alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = issueCount > 0 ? C_ORANGE : C_MUTED } });
                    if (Click(new Rect(tx, ty, tw, 36))) { _tab = i; Repaint(); }
                }
                tx += tw;
            }
            HRule(new Rect(body.x, ty + 35, body.width, 1), C_BORDER);

            // Get fields for the currently active tab
            int currentTabIndex = activeTabs.Count > 0 ? activeTabs[_tab].index : -1;
            var fields = currentTabIndex >= 0
                ? _scan.AllFields.Where(f => f.Tab == currentTabIndex).ToList()
                : new List<FieldResult>();

            // Calculate actual content height so scroll always covers all fields
            // Each field row: 36 normal, 72 mismatch, 60 editing + 4 gap
            // Section headers: 18px each, plus 6px gap after each section
            float contentH = 16; // top padding
            string lastSec = null;
            foreach (var f in fields)
            {
                if (f.Section != lastSec) { contentH += 18 + 6; lastSec = f.Section; } // section label
                bool editing = _editingFieldKey == FieldKey(f);
                bool isEmpty = f.Status == FieldStatus.Empty;
                contentH += (editing ? 60f : f.Status == FieldStatus.Mismatch ? 72f : isEmpty ? 60f : 36f) + 4f;
            }
            contentH += 32; // bottom padding

            _scroll = GUI.BeginScrollView(
                new Rect(body.x, ty + 36, body.width, body.height - 36),
                _scroll, new Rect(0, 0, body.width - 16, contentH));

            float y = 16;

            if (fields.Count == 0)
                GUI.Label(new Rect(24, y, body.width - 48, 24), "No fields scanned for this SDK.", _sMuted);
            else
                DrawFieldGroup(fields, 24, ref y, body.width - 48);

            GUI.EndScrollView();
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
                    DrawFieldRow(f, x, ref y, w);
                }
                y += 6;
            }
        }

        // ── Edit state ────────────────────────────────────────────────────────────
        private string _editingFieldKey = null;
        private string _editBuffer      = "";

        private string FieldKey(FieldResult f) => $"{f.Tab}_{f.Section}_{f.FieldName}_{f.Platform}";

        private void DrawFieldRow(FieldResult f, float x, ref float y, float w)
        {
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
            float rowH = editing ? 60f
                       : f.Status == FieldStatus.Mismatch ? 72f
                       : isEmpty ? 60f
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
            // ── Empty — file not found or key is blank (external dev, read-only) ──
            else if (isEmpty)
            {
                // Tell them exactly what we looked for and where
                string hint = string.IsNullOrEmpty(f.AssetPath)
                    ? $"File not found in project  —  looking for: {f.YamlKey} in {f.FieldName} asset"
                    : $"Key '{f.YamlKey}' is empty in {System.IO.Path.GetFileName(f.AssetPath)}";

                GUI.Label(new Rect(x + 30, y + 24, w - 180, 14), hint,
                    new GUIStyle(_sMuted) { fontSize = 10,
                        normal = { textColor = C_ORANGE } });

                // Locate File button — lets them browse to the asset
                if (Btn(new Rect(x + w - 148, y + 24, 96, 22), "📂  Locate File", C_ORANGE))
                {
                    string located = EditorUtility.OpenFilePanel(
                        $"Locate asset file for {f.FieldName}",
                        Application.dataPath, "asset");

                    if (!string.IsNullOrEmpty(located) && System.IO.File.Exists(located))
                    {
                        // Read the key from the located file
                        string foundValue = AssetScanner.ReadYamlKey(located, f.YamlKey);
                        f.AssetPath   = located;
                        f.ProjectValue = foundValue;
                        f.Status = string.IsNullOrEmpty(foundValue)
                            ? FieldStatus.Missing   // file found but key still empty
                            : FieldStatus.Match;    // found and has a value — show it
                        Repaint();
                    }
                }

                // Rescan button — in case they just added the SDK
                if (Btn(new Rect(x + w - 46, y + 24, 36, 22), "↺", C_MUTED))
                {
                    _scan = null;
                    RunScan();
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
            else if (f.Status == FieldStatus.Missing)
            {
                GUI.Label(new Rect(x + 30, y + 24, w - 100, 14),
                    "Asset file not found in project",
                    new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = C_ORANGE } });
            }

            y += rowH + 4;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  WELCOME SCREEN — first question: GD SDK or not?
        // ════════════════════════════════════════════════════════════════════════

        private void DrawWelcomeScreen(Rect body)
        {
            float cx = body.x + body.width  / 2f;
            float cy = body.y + body.height / 2f;
            float cw = 520f, ch = 320f;

            // Card
            Bg(new Rect(cx - cw/2 - 4, cy - ch/2 - 4, cw + 8, ch + 8), C_ACCENT);
            var card = new Rect(cx - cw/2, cy - ch/2, cw, ch);
            Bg(card, C_SURF);

            float iy = card.y + 20;
            Bg(new Rect(card.x, iy, cw, 3), C_ACCENT); iy += 12;

            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 13), "W E L C O M E",
                new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_ACCENT } }); iy += 18;

            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 30), "Are you using the GD SDK?",
                new GUIStyle(_sTitle) { fontSize = 20, normal = { textColor = C_TEXT } }); iy += 38;

            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 16),
                "This determines which SDKs and settings we check for your project.",
                new GUIStyle(_sMuted) { fontSize = 10 }); iy += 30;

            Bg(new Rect(card.x + 20, iy, cw - 40, 1),
               new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f)); iy += 16;

            // Two big option buttons side by side
            float btnW = (cw - 60) / 2f;

            // ── YES — using GD SDK ───────────────────────────────────────────────
            var yesRect = new Rect(card.x + 20, iy, btnW, 72);
            bool yesHover = yesRect.Contains(Event.current.mousePosition);
            Bg(yesRect, yesHover
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.22f)
                : new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.1f));
            Outline(yesRect, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b,
                yesHover ? 0.9f : 0.45f));
            Bg(new Rect(yesRect.x, yesRect.y, 3, yesRect.height), C_ACCENT);

            GUI.Label(new Rect(yesRect.x + 14, yesRect.y + 10, btnW - 20, 20),
                "✓  Yes, I use GD SDK",
                new GUIStyle(_sBody) { fontSize = 12, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_ACCENT } });
            GUI.Label(new Rect(yesRect.x + 14, yesRect.y + 34, btnW - 20, 30),
                "Auto-detects all SDKs\nfrom your project files",
                new GUIStyle(_sMuted) { fontSize = 10 });

            if (Click(yesRect))
            {
                // Mark as GD SDK dev — auto-detect handles the rest
                SDKConfig.IsGDSDK    = true;
                SDKConfig.IsSetupDone = true;
                _appScreen = AppScreen.Home;
                Repaint();
            }

            // ── NO — not using GD SDK ────────────────────────────────────────────
            var noRect = new Rect(card.x + 40 + btnW, iy, btnW, 72);
            bool noHover = noRect.Contains(Event.current.mousePosition);
            Bg(noRect, noHover
                ? new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.18f)
                : new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.08f));
            Outline(noRect, new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b,
                noHover ? 0.7f : 0.3f));
            Bg(new Rect(noRect.x, noRect.y, 3, noRect.height), C_MUTED);

            GUI.Label(new Rect(noRect.x + 14, noRect.y + 10, btnW - 20, 20),
                "✗  No, using my own setup",
                new GUIStyle(_sBody) { fontSize = 12, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_TEXT } });
            GUI.Label(new Rect(noRect.x + 14, noRect.y + 34, btnW - 20, 30),
                "I'll select which SDKs\nmy project uses manually",
                new GUIStyle(_sMuted) { fontSize = 10 });

            if (Click(noRect))
            {
                // Send to SDK picker screen
                SDKConfig.IsGDSDK = false;
                _appScreen = AppScreen.Setup;
                Repaint();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SETUP SCREEN — first launch SDK picker
        // ════════════════════════════════════════════════════════════════════════

        private void DrawSetupScreen(Rect body)
        {
            float cx = body.x + body.width  / 2f;
            float cy = body.y + body.height / 2f;
            float cw = 500f, ch = 490f;

            // Glowing card border
            Bg(new Rect(cx - cw/2 - 4, cy - ch/2 - 4, cw + 8, ch + 8), C_ACCENT);
            var card = new Rect(cx - cw/2, cy - ch/2, cw, ch);
            Bg(card, C_SURF);

            float iy = card.y + 20;
            Bg(new Rect(card.x, iy, cw, 3), C_ACCENT); iy += 12;

            // Header
            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 13), "F I R S T   T I M E   S E T U P",
                new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_ACCENT } }); iy += 18;

            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 28), "Which SDKs does this game use?",
                new GUIStyle(_sTitle) { fontSize = 18, normal = { textColor = C_TEXT } }); iy += 34;

            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 16),
                "Only selected SDKs will be scanned. You can change this anytime via ⚙ SDKs.",
                new GUIStyle(_sMuted) { fontSize = 10 }); iy += 26;

            Bg(new Rect(card.x + 20, iy, cw - 40, 1),
               new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f)); iy += 14;

            // SDK toggles
            DrawSDKToggle(card.x + 20, ref iy, cw - 40, "AppLovin / AdMob",
                "Ad mediation + AdMob App IDs", ref _tmp_AppLovin, C_ACCENT);

            DrawSDKToggle(card.x + 20, ref iy, cw - 40, "Metica",
                "Monetization analytics & config", ref _tmp_Metica, C_ORANGE);

            DrawSDKToggle(card.x + 20, ref iy, cw - 40, "Adjust",
                "Attribution & event tracking", ref _tmp_Adjust, C_GREEN);

            DrawSDKToggle(card.x + 20, ref iy, cw - 40, "AppMetrica",
                "Yandex analytics platform", ref _tmp_AppMetrica,
                new Color(0.4f, 0.6f, 1f));

            DrawSDKToggle(card.x + 20, ref iy, cw - 40, "Firebase",
                "Remote config, analytics, crash reporting", ref _tmp_Firebase,
                new Color(1f, 0.6f, 0.2f));

            DrawSDKToggle(card.x + 20, ref iy, cw - 40, "Ad Units Settings",
                "AdUnitsSettings.asset — ad unit IDs", ref _tmp_AdUnits, C_MUTED);

            iy += 8;
            Bg(new Rect(card.x + 20, iy, cw - 40, 1),
               new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f)); iy += 14;

            // Confirm button
            bool anySelected = _tmp_AppLovin || _tmp_Metica || _tmp_Adjust ||
                               _tmp_AppMetrica || _tmp_Firebase || _tmp_AdUnits;

            GUI.enabled = anySelected;
            var confirmRect = new Rect(card.x + 20, iy, cw - 40, 40);
            Bg(confirmRect, anySelected
                ? C_ACCENT
                : new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.3f));
            GUI.Label(confirmRect, anySelected ? "✓  Confirm & Continue" : "Select at least one SDK",
                new GUIStyle(_sTitle)
                {
                    fontSize = 13, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = anySelected
                        ? new Color32(10, 10, 10, 255)
                        : new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.6f) }
                });
            GUI.enabled = true;

            if (anySelected && Click(confirmRect))
            {
                // Save selections
                SDKConfig.ManualAppLovin   = _tmp_AppLovin;
                SDKConfig.ManualMetica     = _tmp_Metica;
                SDKConfig.ManualAdjust     = _tmp_Adjust;
                SDKConfig.ManualAppMetrica = _tmp_AppMetrica;
                SDKConfig.ManualFirebase   = _tmp_Firebase;
                SDKConfig.ManualAdUnits    = _tmp_AdUnits;
                SDKConfig.IsSetupDone      = true;
                _appScreen = AppScreen.Home;
                Repaint();
            }
        }

        private void DrawSDKToggle(float x, ref float y, float w,
                                    string name, string desc, ref bool value, Color col)
        {
            var rowRect = new Rect(x, y, w, 46);
            bool hover  = rowRect.Contains(Event.current.mousePosition);

            Bg(rowRect, value
                ? new Color(col.r, col.g, col.b, 0.1f)
                : hover ? new Color(1,1,1,0.03f) : Color.clear);
            Outline(rowRect, value
                ? new Color(col.r, col.g, col.b, 0.45f)
                : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));

            if (value)
                Bg(new Rect(x, y, 3, 46), col);

            // Checkbox
            var checkRect = new Rect(x + 12, y + 13, 20, 20);
            Bg(checkRect, value
                ? col
                : new Color(C_SURF2.r, C_SURF2.g, C_SURF2.b, 1f));
            Outline(checkRect, value
                ? col
                : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 1f));
            if (value)
                GUI.Label(checkRect, "✓", new GUIStyle(_sBody)
                    { fontSize = 11, alignment = TextAnchor.MiddleCenter,
                      fontStyle = FontStyle.Bold,
                      normal = { textColor = new Color32(10,10,10,255) } });

            // Name + description
            GUI.Label(new Rect(x + 40, y + 6, w - 60, 16), name,
                new GUIStyle(_sBody)
                    { fontSize = 12, fontStyle = FontStyle.Bold,
                      normal = { textColor = value ? C_TEXT : C_MUTED } });
            GUI.Label(new Rect(x + 40, y + 24, w - 60, 14), desc,
                new GUIStyle(_sMuted) { fontSize = 10 });

            // Toggle on click
            if (Click(rowRect)) { value = !value; Repaint(); }

            y += 50;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SCANNER
        // ════════════════════════════════════════════════════════════════════════

        private void RunScan() => RunScanWithJson(null);

        private void RunScanWithJson(string json)
        {
            var config = SDKConfig.BuildScanConfig();
            _scan = AssetScanner.Scan(Application.dataPath, json, config);
            _tab  = 0;
            _appScreen = AppScreen.Results;
            Repaint();
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
            Bg(r, hover ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f)
                        : new Color(0.22f, 0.22f, 0.22f, 1f));
            Outline(r, hover ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.9f)
                             : new Color(0.45f, 0.45f, 0.45f, 1f));
            GUI.Label(r, text, new GUIStyle(_sBody)
            {
                fontSize = 11, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = hover ? C_ACCENT : C_TEXT }
            });
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
            _sTitle = new GUIStyle(EditorStyles.boldLabel)  { fontSize = 14, normal = { textColor = C_TEXT } };
            _sBody  = new GUIStyle(EditorStyles.label)      { fontSize = 12, wordWrap = true,  normal = { textColor = C_TEXT } };
            _sMuted = new GUIStyle(EditorStyles.label)      { fontSize = 11, wordWrap = true,  normal = { textColor = C_MUTED } };
            _sCode  = new GUIStyle(EditorStyles.label)      { fontSize = 11, wordWrap = false, normal = { textColor = C_TEXT },
                font = Font.CreateDynamicFontFromOSFont(new[]{"Consolas","Courier New","Monaco"}, 11) };
            _stylesReady = true;
        }
    }
}
