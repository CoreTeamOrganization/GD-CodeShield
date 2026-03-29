// Editor/SolidAgentWindow.cs  —  SOLID Review  —  Tools → SOLID Review

using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace SolidAgent
{
    public class SolidAgentWindow : EditorWindow
    {
        // ── State ────────────────────────────────────────────────────────────────
        private enum Screen { Home, Scanning, Results, Detail }
        private Screen _screen = Screen.Home;

        private List<FileAnalysisResult>           _results   = new();
        private Dictionary<string, GeneratedFix>   _fixes      = new();
        private Dictionary<string, ReviewDecision> _decisions  = new();
        private Dictionary<string, string>         _fixErrors  = new();  // key → error message
        private SolidReport                        _report    = null;

        private string _activeId          = null; // "FilePath||ViolationId"
        private int    _activeTab         = 0;
        private bool   _isFixing          = false;
        private string _statusMsg         = "";
        private double _fixStartTime      = 0;   // EditorApplication.timeSinceStartup when fix started
        private float  _scanProgress      = 0f;
        private RegressionReport _lastRegression;

        // ── Dialog state ─────────────────────────────────────────────────────────
        private bool   _showClearKeyWarning = false;
        private bool   _showCostPrompt      = false;
        private string _pendingFixKey       = null;

        // ── Contract check state ─────────────────────────────────────────────────
        private Dictionary<string, ContractCheckResult> _contractChecks = new();

        // ── Settings ─────────────────────────────────────────────────────────────
        private string _apiKey    = null;
        private bool   _showSettings   = false;
        // Multi-select folder scan — empty set = whole Assets folder
        private HashSet<string>  _scanRoots        = new HashSet<string>();
        private HashSet<string>  _expandedFolders  = new HashSet<string>();
        private bool             _showFolderPicker = false;
        private Vector2 _folderPickerScroll;
        private float   _folderTreeContentH = 0f;

        private string ApiKey
        {
            get { if (_apiKey == null) _apiKey = EditorPrefs.GetString("SolidAgent_ApiKey", ""); return _apiKey; }
            set { _apiKey = value ?? ""; EditorPrefs.SetString("SolidAgent_ApiKey", _apiKey); }
        }

        // ── Embedded Claude Code Terminal ─────────────────────────────────────────
        private System.Diagnostics.Process _claudeProcess = null;
        private System.Text.StringBuilder  _terminalOutput = new System.Text.StringBuilder();
        private readonly object            _terminalLock   = new object();
        private bool                       _terminalRunning = false;
        private bool                       _terminalDone    = false;
        private Vector2                    _terminalScroll;
        private string                     _terminalInput  = "";  // user can type input
        private bool                       _terminalScrollToBottom = false;


        private Vector2 _sidebarScroll;
        private Vector2 _mainScroll;
        private float   _mainScrollContentH = 4800f; // grows as content is measured

        // ── Palette — Game District Logo theme ───────────────────────────────────
        // Source: logo bg #3A3A3A charcoal · bolt+text #FFD300 yellow · white wordmark
        private static readonly Color C_BG       = new Color32(5,   5,   5,   255); // gamedistrict.co pure black
        private static readonly Color C_SURF     = new Color32(18,  18,  18,  255); // GD card surface — dark but distinct
        private static readonly Color C_SURF2    = new Color32(28,  28,  28,  255); // input fields / inner panels
        private static readonly Color C_SURF3    = new Color32(40,  40,  40,  255); // hover / alt rows
        private static readonly Color C_BORDER   = new Color32(60,  60,  60,  255); // visible border
        private static readonly Color C_ACCENT   = new Color32(255, 211, 0,   255); // GD Yellow #FFD300
        private static readonly Color C_GREEN    = new Color32(80,  200, 100, 255); // pass / applied
        private static readonly Color C_RED      = new Color32(220, 60,  60,  255); // error / high sev
        private static readonly Color C_YELLOW   = new Color32(255, 185, 0,   255); // warning / medium (warm amber)
        private static readonly Color C_PURPLE   = new Color32(180, 140, 255, 255); // ISP badge
        private static readonly Color C_ORANGE   = new Color32(255, 140, 40,  255); // OCP badge
        private static readonly Color C_TEXT     = new Color32(255, 255, 255, 255); // pure white — logo wordmark
        private static readonly Color C_MUTED    = new Color32(160, 160, 160, 255); // grey muted — readable on charcoal
        private static readonly Color C_LINENUM  = new Color32(85,  85,  85,  255); // line numbers on charcoal

        // Syntax highlight colours
        private static readonly Color SYN_KEYWORD = new Color32(255, 123, 114, 255); // red   — keywords
        private static readonly Color SYN_TYPE    = new Color32(121, 192, 255, 255); // blue  — types
        private static readonly Color SYN_STRING  = new Color32(165, 214, 255, 255); // light blue — strings
        private static readonly Color SYN_COMMENT = new Color32(139, 148, 158, 255); // grey  — comments
        private static readonly Color SYN_NUMBER  = new Color32(121, 192, 255, 255); // blue  — numbers
        private static readonly Color SYN_METHOD  = new Color32(210, 168, 255, 255); // purple — method names
        private static readonly Color SYN_PLAIN   = new Color32(201, 209, 217, 255); // default text

        // ── Styles ───────────────────────────────────────────────────────────────
        private GUIStyle  _sTitle, _sBody, _sMuted, _sCode, _sBadge, _sSec, _sLineNum;
        private bool      _stylesReady;
        private Texture2D _bgDotTex; // hatch tile texture

        // ════════════════════════════════════════════════════════════════════════
        //  OPEN
        // ════════════════════════════════════════════════════════════════════════

        public static void Open()
        {
            var w = GetWindow<SolidAgentWindow>("  SOLID Review");
            w.minSize = new Vector2(980, 600);
            w.Show();
        }

        private void OnEnable()
        {
            _stylesReady = false;
            _scanRoots   = new HashSet<string>();  // never persisted — select fresh each session

            // Always reset to Home when window opens or package reloads.
            // Unity serializes EditorWindow state across domain reloads, so
            // _screen can be Results/Detail with empty _results after a package swap.
            if (_results == null || _results.Count == 0)
            {
                _screen      = Screen.Home;
                _activeId    = null;
                _statusMsg   = "";
                _isFixing    = false;
            }
        }

        private void OnDisable()
        {
            // _scanRoots intentionally not persisted — cleared each session
        }

        // ════════════════════════════════════════════════════════════════════════
        //  MAIN DRAW LOOP
        // ════════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            InitStyles();
            DrawBg(new Rect(0, 0, position.width, position.height));

            DrawTopBar();

            float bodyY = 50f;
            var   body  = new Rect(0, bodyY, position.width, position.height - bodyY);

            if (_showSettings) { DrawSettings(body); return; }

            switch (_screen)
            {
                case Screen.Home:     DrawHome(body);     break;
                case Screen.Scanning: DrawScanning(body); break;
                case Screen.Results:
                case Screen.Detail:   DrawLayout(body);   break;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TOP BAR
        // ════════════════════════════════════════════════════════════════════════

        private void DrawTopBar()
        {
            var bar = new Rect(0, 0, position.width, 50);
            Bg(bar, C_SURF);

            // GD yellow top accent stripe
            Bg(new Rect(0, 0, position.width, 3), C_ACCENT);
            HRule(new Rect(0, 49, position.width, 1), C_BORDER);

            // Tool title
            GUI.Label(new Rect(16, 11, 20, 26), "⚡",
                new GUIStyle(_sTitle) { fontSize = 18, normal = { textColor = C_ACCENT } });
            GUI.Label(new Rect(36, 13, 160, 24), "SOLID REVIEW",
                new GUIStyle(_sTitle) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = C_TEXT } });

            // Principle pills
            float px = 204;
            DrawPill(new Rect(px,       16, 36, 18), "SRP", C_ACCENT);
            DrawPill(new Rect(px + 42,  16, 36, 18), "OCP", C_ORANGE);
            DrawPill(new Rect(px + 84,  16, 36, 18), "LSP", C_RED);
            DrawPill(new Rect(px + 126, 16, 36, 18), "ISP", C_PURPLE);

            // API key pill
            bool hasKey   = !string.IsNullOrEmpty(ApiKey);
            var  keyColor = hasKey ? C_GREEN : C_RED;
            var  keyRect  = new Rect(px + 172, 14, 56, 22);
            Bg(keyRect, new Color(keyColor.r, keyColor.g, keyColor.b, 0.15f));
            Outline(keyRect, new Color(keyColor.r, keyColor.g, keyColor.b, 0.5f));
            GUI.Label(keyRect, hasKey ? "✓ Key" : "✗ Key", new GUIStyle(_sMuted)
            {
                fontSize = 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = keyColor }
            });
            if (!hasKey && Click(keyRect)) { _showSettings = true; Repaint(); }

            // Right side buttons
            float rx = position.width - 14;
            if (TopBarBtn(new Rect(rx - 84, 11, 76, 28), "⚙  Settings"))
                _showSettings = !_showSettings;

            if (_screen == Screen.Results || _screen == Screen.Detail)
                if (TopBarBtn(new Rect(rx - 170, 11, 78, 28), "↺  Rescan"))
                    ResetToHome();

            // GD company name — safely between key pill and buttons
            float gdX = px + 238;  // right of key pill
            float gdMax = (_screen == Screen.Results || _screen == Screen.Detail)
                ? rx - 180  // when Rescan visible
                : rx - 96;  // just Settings visible
            if (gdMax - gdX > 60)
                GUI.Label(new Rect(gdX, 13, gdMax - gdX, 24), "GAME DISTRICT",
                    new GUIStyle(_sMuted)
                    {
                        fontSize  = 10, fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal    = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.55f) }
                    });
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HOME SCREEN
        // ════════════════════════════════════════════════════════════════════════

        private void DrawHome(Rect body)
        {
            float cx = body.x + body.width / 2f;
            float cy = body.y + body.height / 2f;
            bool hasKey    = !string.IsNullOrEmpty(ApiKey);
            bool hasFolder = _scanRoots.Count > 0;

            // Card height grows if no API key or folder picker is open
            float cw = 460f, ch = hasKey ? 330f : 430f;
            if (_showFolderPicker) ch += 220f;
            // Add height for top-level chips only
            if (_scanRoots.Count > 0)
            {
                var sorted2   = _scanRoots.OrderBy(p => p).ToList();
                int topCount  = sorted2.Count(p =>
                    !sorted2.Any(o => o != p && (p.StartsWith(o + "/") || p.StartsWith(o + "\\"))));
                ch += topCount * 26f + 28f;
            }
            Bg(new Rect(cx - cw/2f - 5, cy - ch/2f - 5, cw + 10, ch + 10), C_ACCENT);

            var card = new Rect(cx - cw/2f, cy - ch/2f, cw, ch);
            Bg(card, C_SURF);

            float iy = card.y + 20;

            // Yellow top stripe
            Bg(new Rect(card.x, iy, cw, 3), C_ACCENT); iy += 10;

            // CHECKPOINT label
            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 14), "C H E C K P O I N T",
                new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold, normal = { textColor = C_ACCENT } });
            iy += 18;

            // Title
            GUI.Label(new Rect(card.x + 20, iy, cw - 40, 34), "SOLID REVIEW",
                new GUIStyle(_sTitle) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = C_TEXT } });
            iy += 40;

            // ── SCAN FOLDER SELECTOR ─────────────────────────────────────────────
            GUI.Label(new Rect(card.x + 20, iy, 120, 13), "S C A N   T A R G E T",
                new GUIStyle(_sMuted) { fontSize = 8, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.65f) } });
            iy += 16;

            // Current selection display
            string displayPath = !hasFolder
                ? "All Assets  (entire project)"
                : _scanRoots.Count == 1
                    ? _scanRoots.First().Replace(Application.dataPath, "Assets")
                    : $"{_scanRoots.Count} folders selected";

            Bg(new Rect(card.x + 20, iy, cw - 40, 30), C_SURF2);
            Outline(new Rect(card.x + 20, iy, cw - 40, 30),
                hasFolder ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.6f) : C_BORDER);
            if (hasFolder) Bg(new Rect(card.x + 20, iy, 3, 30), C_ACCENT);

            GUI.Label(new Rect(card.x + 30, iy + 7, cw - 130, 16), displayPath,
                new GUIStyle(_sMuted) { fontSize = 10,
                    normal = { textColor = hasFolder ? C_ACCENT : C_MUTED } });

            // Toggle folder picker
            string btnLabel = _showFolderPicker ? "▲  Close" : "▼  Choose";
            if (Btn(new Rect(card.x + cw - 102, iy + 4, 80, 22), btnLabel, C_ACCENT))
            {
                _showFolderPicker = !_showFolderPicker;
                if (_showFolderPicker) _folderTreeContentH = 0f; // recalculate on first open
            }
            iy += 34;

            // Inline folder tree
            if (_showFolderPicker)
            {
                // Warning banner
                Bg(new Rect(card.x + 20, iy, cw - 40, 24),
                   new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.08f));
                Bg(new Rect(card.x + 20, iy, 3, 24), C_YELLOW);
                GUI.Label(new Rect(card.x + 30, iy + 5, cw - 44, 14),
                    "⚠  Select your own scripts only — avoid third-party SDK folders",
                    new GUIStyle(_sMuted) { fontSize = 8,
                        normal = { textColor = new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.85f) } });
                iy += 26;

                float treeH = 160f;
                Bg(new Rect(card.x + 20, iy, cw - 40, treeH), C_SURF2);
                Outline(new Rect(card.x + 20, iy, cw - 40, treeH),
                    new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f));

                _folderPickerScroll = GUI.BeginScrollView(
                    new Rect(card.x + 20, iy, cw - 40, treeH),
                    _folderPickerScroll,
                    new Rect(0, 0, cw - 58, _folderTreeContentH > 0 ? _folderTreeContentH : treeH));

                var knownSdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "AppMetrica","AppLovin","MaxSdk","Firebase","Adjust","AppsFlyer",
                    "IronSource","GameAnalytics","Chartboost","Vungle","UnityAds",
                    "Metica","AdMob","GoogleMobileAds","Plugins","ThirdParty","SDK","Sdk","Samples"
                };

                float ry = 4;

                // "All Assets" row — checking this clears all selections
                bool allSel = _scanRoots.Count == 0;
                if (allSel) Bg(new Rect(0, ry, cw - 58, 20), new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.1f));
                // Checkbox
                Bg(new Rect(10, ry + 3, 14, 14), allSel ? C_ACCENT : C_SURF);
                Outline(new Rect(10, ry + 3, 14, 14), allSel ? C_ACCENT : C_BORDER);
                if (allSel) GUI.Label(new Rect(10, ry + 1, 14, 16), "✓",
                    new GUIStyle(_sMuted) { fontSize = 9, alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.black } });
                GUI.Label(new Rect(30, ry + 3, cw - 88, 14),
                    "All Assets  (entire project)",
                    new GUIStyle(_sMuted) { fontSize = 10,
                        normal = { textColor = allSel ? C_ACCENT : C_TEXT } });
                if (Click(new Rect(0, ry, cw - 58, 20)))
                    _scanRoots.Clear();
                ry += 22;

                // Recursive tree — draw folder node helper inline
                System.Action<string, int> drawFolder = null;
                drawFolder = (dirPath, depth) =>
                {
                    string fname     = Path.GetFileName(dirPath);
                    bool isSdk       = knownSdks.Contains(fname);
                    bool checked_    = _scanRoots.Contains(dirPath);
                    bool expanded    = _expandedFolders.Contains(dirPath);
                    bool hasChildren = Directory.GetDirectories(dirPath).Length > 0;

                    float indent  = 10 + depth * 16f;
                    float rowY    = ry;   // capture before ry advances
                    float rowW    = cw - 58;
                    float rowH    = 22f;

                    // Row highlight
                    if (checked_)
                        Bg(new Rect(0, rowY, rowW, rowH),
                           new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.12f));

                    // ── Expand arrow (left zone) — click toggles expand only ──────
                    float arrowX = indent;
                    if (hasChildren)
                    {
                        GUI.Label(new Rect(arrowX, rowY + 4, 14, 14),
                            expanded ? "▼" : "▶",
                            new GUIStyle(_sMuted) { fontSize = 8,
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.7f) } });
                    }

                    // ── Checkbox ─────────────────────────────────────────────────
                    float cbx = indent + 16;
                    Bg(new Rect(cbx, rowY + 4, 13, 13), checked_ ? C_ACCENT : C_SURF2);
                    Outline(new Rect(cbx, rowY + 4, 13, 13),
                        checked_ ? C_ACCENT : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.8f));
                    if (checked_)
                        GUI.Label(new Rect(cbx - 1, rowY + 2, 15, 15), "✓",
                            new GUIStyle(_sMuted) { fontSize = 9,
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = Color.black } });

                    // ── Label ─────────────────────────────────────────────────────
                    Color col   = isSdk ? C_YELLOW : (checked_ ? C_ACCENT : C_TEXT);
                    string lbl  = fname + (isSdk ? "  ⚠" : "/");
                    GUI.Label(new Rect(cbx + 17, rowY + 4, rowW - cbx - 20, 14), lbl,
                        new GUIStyle(_sMuted) { fontSize = depth == 0 ? 10 : 9,
                            normal = { textColor = col } });

                    // ── Click handling ────────────────────────────────────────────
                    // Arrow zone — expand/collapse only
                    if (hasChildren && Click(new Rect(arrowX, rowY, 16, rowH)))
                    {
                        if (expanded) _expandedFolders.Remove(dirPath);
                        else          _expandedFolders.Add(dirPath);
                    }
                    // Rest of row (checkbox + label) — toggle selection + all children
                    else if (Click(new Rect(cbx, rowY, rowW - cbx, rowH)))
                    {
                        if (checked_)
                        {
                            // Deselect this folder and all its descendants
                            var toRemove = _scanRoots
                                .Where(p => p == dirPath || p.StartsWith(dirPath + "/") || p.StartsWith(dirPath + "\\"))
                                .ToList();
                            foreach (var p in toRemove) _scanRoots.Remove(p);
                        }
                        else
                        {
                            // Select this folder + auto-select all subfolders recursively
                            _scanRoots.Add(dirPath);
                            try
                            {
                                foreach (var sub in Directory.GetDirectories(dirPath, "*",
                                             SearchOption.AllDirectories))
                                    _scanRoots.Add(sub);
                                // Also auto-expand so user can see and deselect children
                                _expandedFolders.Add(dirPath);
                            }
                            catch { }
                        }
                    }

                    ry += rowH;

                    // Recurse into children if expanded
                    if (expanded && hasChildren)
                    {
                        try
                        {
                            foreach (var sub in Directory.GetDirectories(dirPath)
                                         .OrderBy(d => Path.GetFileName(d)))
                                drawFolder(sub, depth + 1);
                        }
                        catch { }
                    }
                };

                try
                {
                    foreach (var dir in Directory.GetDirectories(Application.dataPath)
                                 .OrderBy(d => Path.GetFileName(d)))
                        drawFolder(dir, 0);
                }
                catch { }

                GUI.EndScrollView();
                // Store the actual rendered height so next frame the scroll rect is exact
                _folderTreeContentH = ry + 4f;
                iy += treeH + 4;
            }

            // Show selected folders summary + warnings
            if (hasFolder)
            {
                var warnSdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "AppMetrica","AppLovin","MaxSdk","Firebase","Adjust","AppsFlyer",
                    "IronSource","GameAnalytics","Chartboost","Vungle","UnityAds",
                    "Metica","AdMob","GoogleMobileAds","Plugins","ThirdParty","SDK","Sdk","Samples"
                };

                // Only show TOP-LEVEL selected folders as chips.
                // A folder is top-level if no ancestor of it is also selected.
                var sorted    = _scanRoots.OrderBy(p => p).ToList();
                var topLevel  = sorted.Where(p =>
                    !sorted.Any(other => other != p &&
                        (p.StartsWith(other + "/") || p.StartsWith(other + "\\"))
                    )).ToList();

                foreach (var sel in topLevel)
                {
                    string sname   = Path.GetFileName(sel.TrimEnd('/', '\\'));
                    bool isBad     = warnSdks.Contains(sname);
                    Color cc       = isBad ? C_RED : C_GREEN;
                    // Count how many total folders this selection covers
                    int childCount = _scanRoots.Count(p => p != sel &&
                        (p.StartsWith(sel + "/") || p.StartsWith(sel + "\\")));
                    string countLabel = childCount > 0 ? $"  (+{childCount} subfolders)" : "";

                    Bg(new Rect(card.x + 20, iy, cw - 40, 22), new Color(cc.r, cc.g, cc.b, 0.06f));
                    Outline(new Rect(card.x + 20, iy, cw - 40, 22), new Color(cc.r, cc.g, cc.b, 0.3f));
                    Bg(new Rect(card.x + 20, iy, 3, 22), cc);
                    GUI.Label(new Rect(card.x + 30, iy + 4, cw - 100, 14),
                        (isBad ? "⚠  " : "✓  ") + sel.Replace(Application.dataPath, "Assets") + countLabel,
                        new GUIStyle(_sMuted) { fontSize = 9,
                            normal = { textColor = new Color(cc.r, cc.g, cc.b, 0.9f) } });
                    // Remove button removes top folder + all its descendants
                    if (Btn(new Rect(card.x + cw - 54, iy + 2, 32, 18), "✕", C_SURF2))
                    {
                        var toRemove = _scanRoots
                            .Where(p => p == sel || p.StartsWith(sel + "/") || p.StartsWith(sel + "\\"))
                            .ToList();
                        foreach (var p in toRemove) _scanRoots.Remove(p);
                    }
                    iy += 24;
                }

                if (Btn(new Rect(card.x + 20, iy, 118, 18), "✕  Clear all", C_MUTED))
                    _scanRoots.Clear();
            }
            iy += hasFolder ? 26 : 10;

            // Yellow divider
            Bg(new Rect(card.x + 20, iy, cw - 40, 1),
               new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.35f));
            iy += 14;

            // ── BIG YELLOW SCAN BUTTON ───────────────────────────────────────────
            var btnR = new Rect(card.x + 20, iy, cw - 40, 46);
            Bg(btnR, C_ACCENT);
            if (btnR.Contains(Event.current.mousePosition))
                Bg(btnR, new Color(1f, 1f, 0.7f, 0.12f));
            GUI.Label(btnR, "▶  START SOLID REVIEW",
                new GUIStyle(_sTitle)
                {
                    fontSize  = 13, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = new Color32(10, 10, 10, 255) }
                });
            if (Click(btnR)) StartScan();
            iy += 54;

            // ── API KEY SECTION ──────────────────────────────────────────────────
            if (hasKey)
            {
                GUI.Label(new Rect(card.x + 20, iy, cw - 40, 16),
                    "✓  API key active — AI fix generation enabled",
                    new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = C_GREEN } });
            }
            else
            {
                GUI.Label(new Rect(card.x + 20, iy, cw - 40, 16),
                    "Scanning is free  ·  AI fixes require a Claude API key",
                    new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = new Color(C_TEXT.r, C_TEXT.g, C_TEXT.b, 0.7f) } });
                iy += 26;

                Bg(new Rect(card.x + 20, iy, cw - 40, 1),
                   new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.25f));
                iy += 12;

                GUI.Label(new Rect(card.x + 20, iy, cw - 40, 13), "A D D   A P I   K E Y   ( O P T I O N A L )",
                    new GUIStyle(_sMuted) { fontSize = 8, fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.75f) } });
                iy += 17;

                string nk = EditorGUI.PasswordField(new Rect(card.x + 20, iy, cw - 108, 28), ApiKey);
                if (nk != ApiKey) { ApiKey = nk; Repaint(); }
                if (Btn(new Rect(card.x + cw - 82, iy, 62, 28), "SAVE", C_ACCENT)) Repaint();
                iy += 38;

                GUI.Label(new Rect(card.x + 20, iy, cw - 40, 14), "console.anthropic.com",
                    new GUIStyle(_sMuted) { fontSize = 9,
                        normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.6f) } });
            }

            // ── Bottom GD strip ──────────────────────────────────────────────────
            float sy = card.y + ch - 32;
            Bg(new Rect(card.x, sy, cw, 1), C_BORDER);
            GUI.Label(new Rect(card.x + 20, sy + 6, 200, 18), "ESTD. 2016  ·  STAY HUNGRY · STAY FOOLISH",
                new GUIStyle(_sMuted) { fontSize = 7,
                    normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.6f) } });
            GUI.Label(new Rect(card.x, sy + 5, cw - 18, 18), "⚡ GAME DISTRICT",
                new GUIStyle(_sTitle) { fontSize = 10, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.65f) } });
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SCANNING SCREEN
        // ════════════════════════════════════════════════════════════════════════

        private void DrawScanning(Rect body)
        {
            float cx = body.x + body.width / 2f;
            float cy = body.y + body.height / 2f;

            // Yellow card border
            Bg(new Rect(cx - 182, cy - 54, 364, 108), C_ACCENT);
            Bg(new Rect(cx - 178, cy - 50, 356, 100), C_SURF);

            GUI.Label(new Rect(cx - 160, cy - 36, 320, 14), "S C A N N I N G",
                new GUIStyle(_sMuted)
                {
                    fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    normal   = { textColor = C_ACCENT }
                });
            GUI.Label(new Rect(cx - 160, cy - 18, 320, 22), _statusMsg,
                new GUIStyle(_sTitle)
                {
                    fontSize  = 12, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = C_TEXT }
                });

            // Yellow progress bar
            float bw = 300f;
            Bg(new Rect(cx - bw/2, cy + 14, bw, 6), C_SURF3);
            Outline(new Rect(cx - bw/2, cy + 14, bw, 6), C_BORDER);
            if (_scanProgress > 0)
                Bg(new Rect(cx - bw/2, cy + 14, bw * _scanProgress, 6), C_ACCENT);

            Repaint();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  RESULTS LAYOUT  — sidebar + detail
        // ════════════════════════════════════════════════════════════════════════

        private void DrawLayout(Rect body)
        {
            float sw = 280f;
            DrawSidebar(new Rect(body.x, body.y, sw, body.height));
            Bg(new Rect(body.x + sw, body.y, 1, body.height), C_BORDER);
            DrawDetail(new Rect(body.x + sw + 1, body.y, body.width - sw - 1, body.height));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SIDEBAR
        // ════════════════════════════════════════════════════════════════════════

        private void DrawSidebar(Rect r)
        {
            Bg(r, C_SURF);
            // GD yellow top stripe
            Bg(new Rect(r.x, r.y, r.width, 3), C_ACCENT);

            // Stats row
            int total   = _results.Sum(x => x.Violations.Count);
            int applied = _decisions.Values.Count(d => d == ReviewDecision.Applied);
            int skipped = _decisions.Values.Count(d => d == ReviewDecision.Skipped);
            int pending = total - applied - skipped;
            float sw    = r.width / 3f;

            Bg(new Rect(r.x, r.y + 3, r.width, 55), C_SURF2);
            StatBox(new Rect(r.x,        r.y + 3, sw, 55), total.ToString(),   "TOTAL",   C_ACCENT);
            StatBox(new Rect(r.x + sw,   r.y + 3, sw, 55), applied.ToString(), "APPLIED", C_GREEN);
            StatBox(new Rect(r.x + sw*2, r.y + 3, sw, 55), skipped.ToString(), "SKIPPED", C_MUTED);
            HRule(new Rect(r.x, r.y + 58, r.width, 1), C_BORDER);

            // Progress bar
            float pct = total > 0 ? (float)(applied + skipped) / total : 0f;
            Bg(new Rect(r.x, r.y + 59, r.width, 3), C_SURF3);
            if (pct > 0) Bg(new Rect(r.x, r.y + 59, r.width * pct, 3), C_ACCENT);

            // ── Ratings panel ────────────────────────────────────────────────────
            if (_report != null)
            {
                float ry = r.y + 70;

                // Section label
                GUI.Label(new Rect(r.x + 10, ry, r.width - 20, 14), "PRINCIPLE RATINGS",
                    new GUIStyle(_sMuted)
                    {
                        fontSize = 8, fontStyle = FontStyle.Bold,
                        normal   = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.65f) }
                    });
                ry += 16;

                foreach (var rating in _report.Ratings)
                {
                    float[] scoreCol = RatingColor(rating.Score);
                    Color sc = new Color(scoreCol[0], scoreCol[1], scoreCol[2]);

                    // Row bg
                    Bg(new Rect(r.x + 8, ry, r.width - 16, 30), C_SURF2);
                    Bg(new Rect(r.x + 8, ry, 3, 30), sc); // left color strip

                    // Principle name
                    GUI.Label(new Rect(r.x + 16, ry + 4, 36, 14), rating.Principle.ToString(),
                        new GUIStyle(_sMuted)
                        { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = C_TEXT } });

                    // Label
                    GUI.Label(new Rect(r.x + 16, ry + 16, 90, 12), rating.Label,
                        new GUIStyle(_sMuted) { fontSize = 8, normal = { textColor = sc } });

                    // Score stars
                    string stars = "";
                    for (int s = 1; s <= 5; s++)
                        stars += s <= rating.Score ? "★" : "☆";
                    GUI.Label(new Rect(r.x + r.width - 74, ry + 8, 64, 16), stars,
                        new GUIStyle(_sMuted) { fontSize = 11, normal = { textColor = sc }, alignment = TextAnchor.MiddleRight });

                    ry += 33;
                }

                // Overall score
                HRule(new Rect(r.x + 8, ry, r.width - 16, 1), C_BORDER);
                ry += 8;

                float[] oc = RatingColor(_report.OverallScore);
                Color overallCol = new Color(oc[0], oc[1], oc[2]);
                GUI.Label(new Rect(r.x + 10, ry, r.width * 0.55f, 14), "OVERALL",
                    new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold, normal = { textColor = C_MUTED } });
                GUI.Label(new Rect(r.x + 10, ry + 14, r.width * 0.6f, 18), $"{_report.OverallScore:F1} / 5  —  {_report.OverallLabel}",
                    new GUIStyle(_sMuted) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = overallCol } });

                // Overall bar
                ry += 36;
                Bg(new Rect(r.x + 10, ry, r.width - 20, 5), C_SURF3);
                Bg(new Rect(r.x + 10, ry, (r.width - 20) * (_report.OverallScore / 5f), 5), overallCol);
                ry += 16;

                // Download PDF button
                HRule(new Rect(r.x + 8, ry, r.width - 16, 1), C_BORDER);
                ry += 10;
                if (Btn(new Rect(r.x + 10, ry, r.width - 20, 32), "⬇  Download Word Report", C_ACCENT))
                    ExportPDF();
            }

            // Violation list — starts below ratings if present, else just below progress bar
            float listStartY = _report != null
                ? r.y + 70 + (_report.Ratings.Count * 33) + 120  // below ratings panel
                : r.y + 62;

            var listRect = new Rect(r.x, listStartY, r.width, r.height - (listStartY - r.y));
            _sidebarScroll = GUI.BeginScrollView(listRect, _sidebarScroll,
                new Rect(0, 0, r.width - 12, SidebarH()));

            float y = 0;
            foreach (var fr in _results)
            {
                if (fr.Violations.Count == 0) continue;

                // File header row
                Bg(new Rect(0, y, r.width, 24), C_SURF3);
                GUI.Label(new Rect(10, y + 4, r.width - 12, 16),
                    "📄 " + fr.FileName,
                    new GUIStyle(_sMuted) { fontSize = 10, fontStyle = FontStyle.Bold });
                y += 24;

                foreach (var v in fr.Violations)
                {
                    string key    = MakeKey(v);
                    bool   active = key == _activeId;
                    var    dec    = _decisions.GetValueOrDefault(key);
                    bool   done   = dec != ReviewDecision.None;

                    // Row background
                    Bg(new Rect(0, y, r.width, 52),
                       active ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.08f) : Color.clear);

                    // Active left bar
                    if (active) Bg(new Rect(0, y, 3, 52), C_ACCENT);

                    // Severity dot
                    Color dot = SevColor(v.Severity);
                    Bg(new Rect(active ? 10 : 8, y + 14, 6, 6), dot);

                    // Principle badge
                    DrawPill(new Rect(22, y + 7, 34, 16), v.Principle.ToString(), PrinColor(v.Principle));

                    // Decision
                    if (dec == ReviewDecision.Applied)
                        DrawPill(new Rect(62, y + 7, 44, 16), "Applied", C_GREEN);
                    else if (dec == ReviewDecision.Skipped)
                        DrawPill(new Rect(62, y + 7, 44, 16), "Skipped", C_MUTED);

                    // Title
                    GUI.color = new Color(1,1,1, done ? 0.42f : 1f);
                    GUI.Label(new Rect(22, y + 26, r.width - 34, 22), v.Title,
                        new GUIStyle(_sMuted)
                        {
                            fontSize = 10, wordWrap = true,
                            normal   = { textColor = active ? C_TEXT : new Color(C_TEXT.r, C_TEXT.g, C_TEXT.b, 0.85f) }
                        });
                    GUI.color = Color.white;

                    if (Click(new Rect(0, y, r.width, 52)))
                    {
                        _activeId = key; _activeTab = 0; _lastRegression = null; _mainScrollContentH = 4800f; _mainScroll = Vector2.zero;
                        _screen = Screen.Detail;
                        _showCostPrompt = false; _pendingFixKey = null; // dismiss prompt
                        Repaint();
                    }

                    y += 52;
                    HRule(new Rect(0, y - 1, r.width, 1), C_BORDER);
                }
            }

            if (_results.Count == 0)
                GUI.Label(new Rect(16, 16, r.width - 32, 40),
                    "No violations found.", _sMuted);

            GUI.EndScrollView();
        }

        private void StatBox(Rect r, string n, string l, Color c)
        {
            GUI.Label(new Rect(r.x, r.y + 6, r.width, 24), n,
                new GUIStyle(_sTitle)
                {
                    fontSize  = 18, alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = c }
                });
            GUI.Label(new Rect(r.x, r.y + 32, r.width, 16), l,
                new GUIStyle(_sMuted) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
        }

        private float SidebarH()
        {
            float h = 0;
            foreach (var r in _results)
            {
                if (r.Violations.Count == 0) continue;
                h += 24 + r.Violations.Count * 53f;
            }
            return Mathf.Max(h, 100);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DETAIL PANEL
        // ════════════════════════════════════════════════════════════════════════

        private void DrawDetail(Rect r)
        {
            var v = FindViolation(_activeId);
            if (v == null)
            {
                GUI.Label(
                    new Rect(r.x + r.width/2 - 160, r.y + r.height/2 - 12, 320, 24),
                    "← Select a violation to review",
                    new GUIStyle(_sMuted) { alignment = TextAnchor.MiddleCenter, fontSize = 13 });
                return;
            }

            // ── Header bar ──────────────────────────────────────────────────────
            float hh = 72f;   // taller header — title + file row without overlap
            Bg(new Rect(r.x, r.y, r.width, hh), C_SURF);
            HRule(new Rect(r.x, r.y + hh - 1, r.width, 1), C_BORDER);

            // Principle + severity badges
            DrawPill(new Rect(r.x + 16, r.y + 10, 38, 18), v.Principle.ToString(), PrinColor(v.Principle));
            DrawPill(new Rect(r.x + 60, r.y + 10, 56, 18), v.Severity.ToString(), SevColor(v.Severity));

            // Title — second row, below badges
            GUI.Label(new Rect(r.x + 16, r.y + 32, r.width - 110, 20), v.Title, _sBody);

            // File + line row — third row at bottom of header
            GUI.Label(new Rect(r.x + 16, r.y + 54, 40, 14), "FILE",
                new GUIStyle(_sMuted) { fontSize = 8, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.55f) } });
            GUI.Label(new Rect(r.x + 58, r.y + 54, r.width - 180, 14),
                v.Location.FileName,
                new GUIStyle(_sMuted) { fontSize = 9,
                    normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.85f) } });
            // Line pill — right side of file row
            DrawPill(new Rect(r.x + r.width - 88, r.y + 51, 76, 16),
                "line " + v.Location.StartLine, C_ACCENT);

            // ── Processing overlay — shown prominently when fixing ────────────────
            if (_isFixing)
            {
                float oy = r.y + hh + 34;
                float oh = r.height - hh - 34 - 54;
                Bg(new Rect(r.x, oy, r.width, oh), new Color(0, 0, 0, 0.6f));

                float cw2 = 420f, ch2 = 180f;
                float ccx = r.x + r.width / 2f - cw2 / 2f;
                float ccy = oy + oh / 2f - ch2 / 2f;
                Bg(new Rect(ccx, ccy, cw2, ch2), C_SURF);
                Outline(new Rect(ccx, ccy, cw2, ch2), C_ACCENT);
                // Yellow top stripe on card
                Bg(new Rect(ccx, ccy, cw2, 3), C_ACCENT);

                int dots = (int)(EditorApplication.timeSinceStartup * 2) % 4;
                string ellipsis = new string('.', dots);

                // Elapsed time
                double elapsed = EditorApplication.timeSinceStartup - _fixStartTime;
                string timeStr = elapsed < 60
                    ? $"{(int)elapsed}s"
                    : $"{(int)(elapsed/60)}m {(int)(elapsed%60)}s";

                // Title
                GUI.Label(new Rect(ccx, ccy + 14, cw2, 22), "Generating Fix" + ellipsis,
                    new GUIStyle(_sBody)
                    {
                        alignment = TextAnchor.MiddleCenter, fontSize = 13,
                        fontStyle = FontStyle.Bold, normal = { textColor = C_ACCENT }
                    });

                // Elapsed
                GUI.Label(new Rect(ccx, ccy + 36, cw2, 16), timeStr,
                    new GUIStyle(_sMuted) { alignment = TextAnchor.MiddleCenter, fontSize = 10,
                        normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.6f) } });

                // Step indicators
                string[] steps = {
                    "Connect to API",
                    "Read source file",
                    "Analyse violations",
                    "Claude reading project files",
                    "Generating fix",
                    "Contract check"
                };
                int currentStep =
                    _statusMsg.Contains("Connecting")              ? 0 :
                    _statusMsg.Contains("Reading source")          ? 1 :
                    _statusMsg.Contains("Analysing")               ? 2 :
                    _statusMsg.Contains("Reading") || _statusMsg.Contains("Exploring") ? 3 :
                    _statusMsg.Contains("Generating")              ? 4 :
                    _statusMsg.Contains("contract")                ? 5 : 3;

                float sy = ccy + 60;
                for (int si = 0; si < steps.Length; si++)
                {
                    bool done    = si < currentStep;
                    bool active  = si == currentStep;
                    Color col    = done ? C_GREEN : active ? C_ACCENT : new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.3f);
                    string icon  = done ? "✓" : active ? "▶" : "○";

                    // Highlight active row
                    if (active)
                        Bg(new Rect(ccx + 12, sy, cw2 - 24, 20),
                           new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.06f));

                    GUI.Label(new Rect(ccx + 18, sy + 2, 16, 16), icon,
                        new GUIStyle(_sMuted) { fontSize = 9, normal = { textColor = col } });
                    GUI.Label(new Rect(ccx + 36, sy + 2, cw2 - 50, 16),
                        active ? steps[si] + ellipsis : steps[si],
                        new GUIStyle(_sMuted) { fontSize = 9, normal = { textColor = col } });
                    sy += 22;
                }

                // Bottom note — only show on slow step
                if (currentStep == 3 && elapsed > 4)
                {
                    GUI.Label(new Rect(ccx + 12, ccy + ch2 - 22, cw2 - 24, 16),
                        "Claude is reading the full file — larger files take longer.",
                        new GUIStyle(_sMuted) { alignment = TextAnchor.MiddleCenter, fontSize = 8,
                            normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.45f) } });
                }

                Repaint();
            }

            // Status line — single line normally, expands to show full error
            if (!string.IsNullOrEmpty(_statusMsg) && !_isFixing)
            {
                bool isError = _statusMsg.StartsWith("✗");
                float msgH   = isError ? 52f : 18f;
                float msgY   = isError ? r.y + 4f : r.y + 10f;
                var msgStyle = new GUIStyle(_sMuted)
                {
                    fontSize  = isError ? 9 : 10,
                    wordWrap  = true,
                    normal    = { textColor = _statusMsg.StartsWith("✓") ? C_GREEN :
                                              isError ? C_RED : C_MUTED }
                };
                GUI.Label(new Rect(r.x + 124, msgY, r.width - 140, msgH), _statusMsg, msgStyle);
            }

            // ── Tabs ────────────────────────────────────────────────────────────
            string[] tabs = { "Violation", "Proposed Fix", "Claude Code" };
            float ty = r.y + hh;
            Bg(new Rect(r.x, ty, r.width, 34), C_SURF);

            for (int i = 0; i < tabs.Length; i++)
            {
                var tr = new Rect(r.x + 16 + i * 128f, ty, 120f, 34);
                bool act = i == _activeTab;
                if (act)
                {
                    Bg(new Rect(tr.x, ty + 30, 120, 3), C_ACCENT);
                    GUI.Label(tr, tabs[i], new GUIStyle(_sBody)
                        { alignment = TextAnchor.MiddleCenter, normal = { textColor = C_ACCENT } });
                }
                else
                {
                    GUI.Label(tr, tabs[i], new GUIStyle(_sMuted) { alignment = TextAnchor.MiddleCenter });
                    if (Click(tr)) { _activeTab = i; _mainScrollContentH = 4800f; _mainScroll = Vector2.zero; Repaint(); }
                }
            }
            HRule(new Rect(r.x, ty + 33, r.width, 1), C_BORDER);

            // ── Scrollable content ───────────────────────────────────────────────
            // AI disclaimer takes 28px on fix tab — adjust scroll rect accordingly
            float disclaimerH = (_activeTab == 1) ? 28f : 0f;

            // Draw disclaimer BEFORE scroll view (it sits above it)
            if (_activeTab == 1)
            {
                Bg(new Rect(r.x, ty + 34, r.width, disclaimerH),
                   new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.08f));
                HRule(new Rect(r.x, ty + 34 + disclaimerH - 1, r.width, 1),
                    new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.3f));
                Bg(new Rect(r.x, ty + 34, 3, disclaimerH), C_YELLOW);
                GUI.Label(new Rect(r.x + 12, ty + 34 + 7, r.width - 20, 14),
                    "⚠  AI-generated fix — AI can make mistakes. Always review before applying.",
                    new GUIStyle(_sMuted)
                    {
                        fontSize = 10,
                        normal   = { textColor = new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.9f) }
                    });
            }

            float cy2 = ty + 34 + disclaimerH;
            float ch   = r.height - hh - 34 - disclaimerH - 54;
            _mainScroll = GUI.BeginScrollView(
                new Rect(r.x, cy2, r.width, ch), _mainScroll,
                new Rect(0, 0, r.width - 16, _mainScrollContentH));

            float py = 16; float pw = r.width - 48; float px = 24;

            if (_activeTab == 0) DrawViolationTab(v, px, ref py, pw);
            if (_activeTab == 1) DrawFixTab(v, px, ref py, pw);
            if (_activeTab == 2) DrawEmbeddedTerminal(px, ref py, pw);

            // Update content height — add padding, never shrink below visible area
            float measured = py + 40f;
            if (measured > _mainScrollContentH || measured < _mainScrollContentH - 400f)
            {
                _mainScrollContentH = Mathf.Max(measured, ch + 100f);
                Repaint();
            }

            GUI.EndScrollView();

            // ── Action bar ───────────────────────────────────────────────────────
            DrawActionBar(new Rect(r.x, r.y + r.height - 54, r.width, 54), v);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TAB: VIOLATION
        // ════════════════════════════════════════════════════════════════════════

        private void DrawViolationTab(Violation v, float x, ref float y, float w)
        {
            // ── What the violation is ────────────────────────────────────────────
            SecLabel(x, ref y, "What is wrong");
            DrawCard(x, ref y, w, v.Description, _sBody);

            // ── Why it matters (evidence) ────────────────────────────────────────
            SecLabel(x, ref y, "Evidence in code");
            float eh = 36;
            Bg(new Rect(x, y, w, eh), new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.06f));
            Outline(new Rect(x, y, w, eh), new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.3f));
            Bg(new Rect(x, y, 3, eh), C_YELLOW);
            GUI.Label(new Rect(x + 16, y + 10, w - 24, 16), v.Evidence,
                new GUIStyle(_sBody)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    normal   = { textColor = new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.95f) }
                });
            y += eh + 14;

            // ── Affected code with real file context ─────────────────────────────
            // Show file name + exact line range so developer knows exactly where to look
            int endLine = v.Location.StartLine + (v.OriginalCode?.Split('\n').Length ?? 1) - 1;
            SecLabel(x, ref y,
                $"Affected code  ·  {v.Location.FileName}  ·  line {v.Location.StartLine}–{endLine}");
            DrawCodeBlock(v.OriginalCode, x, ref y, w, v.Location.StartLine);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TAB: FIX
        // ════════════════════════════════════════════════════════════════════════

        private void DrawFixTab(Violation v, float x, ref float y, float w)
        {
            string key    = MakeKey(v);
            bool   hasKey = !string.IsNullOrEmpty(ApiKey);
            var    allViolationsInFile = GetFileViolations(v.Location.FilePath);

            if (!_fixes.TryGetValue(key, out var fix))
            {
                if (!hasKey)
                {
                    // API key required card
                    float cardH = 96f;
                    Bg(new Rect(x, y, w, cardH), C_SURF2);
                    Outline(new Rect(x, y, w, cardH), C_BORDER);
                    Bg(new Rect(x, y, 3, cardH), C_RED);

                    GUI.Label(new Rect(x + 16, y + 10, w - 32, 18),
                        "API key required for AI fixes",
                        new GUIStyle(_sBody) { fontStyle = FontStyle.Bold });
                    GUI.Label(new Rect(x + 16, y + 32, w - 32, 16),
                        "Violation details are always free. AI fix generation needs a Claude key.",
                        new GUIStyle(_sMuted) { fontSize = 10 });

                    string nk = EditorGUI.PasswordField(
                        new Rect(x + 16, y + 56, w - 120, 26), ApiKey);
                    if (nk != ApiKey) { ApiKey = nk; Repaint(); }
                    if (Btn(new Rect(x + w - 98, y + 56, 66, 26), "Save", C_GREEN))
                        Repaint();

                    y += cardH + 12;
                    return;
                }

                // Generate button — shows cost prompt first
                SecLabel(x, ref y, "No fix generated yet");

                // Show API error inline if one occurred
                if (_fixErrors.TryGetValue(key, out var fixErr))
                {
                    float errH = 72f;
                    Bg(new Rect(x, y, w, errH), new Color(C_RED.r, C_RED.g, C_RED.b, 0.07f));
                    Outline(new Rect(x, y, w, errH), new Color(C_RED.r, C_RED.g, C_RED.b, 0.4f));
                    Bg(new Rect(x, y, 3, errH), C_RED);

                    // Friendly message for common errors
                    string friendly = fixErr;
                    if (fixErr.Contains("429") || fixErr.ToLower().Contains("rate") || fixErr.ToLower().Contains("limit"))
                        friendly = "API rate limit or usage quota reached — wait a moment then try again.";
                    else if (fixErr.Contains("401") || fixErr.ToLower().Contains("auth") || fixErr.ToLower().Contains("key"))
                        friendly = "Invalid API key — check your key in Settings.";
                    else if (fixErr.Contains("503") || fixErr.ToLower().Contains("overload"))
                        friendly = "Claude API is overloaded — try again in a few seconds.";

                    GUI.Label(new Rect(x + 14, y + 8, w - 20, 16),
                        "⚠  " + friendly,
                        new GUIStyle(_sBody) { fontSize = 10, fontStyle = FontStyle.Bold,
                            normal = { textColor = new Color(C_RED.r, C_RED.g, C_RED.b, 0.9f) } });
                    GUI.Label(new Rect(x + 14, y + 28, w - 20, 14),
                        "Technical detail: " + fixErr,
                        new GUIStyle(_sMuted) { fontSize = 8 });

                    // Retry button
                    if (Btn(new Rect(x + 14, y + 46, 90, 22), "↺  Retry", C_ACCENT))
                    {
                        _fixErrors.Remove(key);
                        _pendingFixKey  = key;
                        _showCostPrompt = true;
                    }
                    y += errH + 10;
                }
                else
                {
                    // Check if any violation is SRP — Generate Fix can't reliably handle it
                    bool hasSrp = allViolationsInFile.Any(vl => vl.Principle == SolidPrinciple.SRP);

                    if (hasSrp)
                    {
                        // SRP info card
                        float warnH = 44f;
                        Bg(new Rect(x, y, w, warnH), new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.06f));
                        Outline(new Rect(x, y, w, warnH), new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f));
                        Bg(new Rect(x, y, 3, warnH), C_ACCENT);
                        GUI.Label(new Rect(x + 14, y + 7, w - 20, 16),
                            "SRP fix requires creating new files — use Claude Code for best results",
                            new GUIStyle(_sBody) { fontSize = 10, fontStyle = FontStyle.Bold,
                                normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.95f) } });
                        GUI.Label(new Rect(x + 14, y + 26, w - 20, 14),
                            "Claude Code reads all project files before applying the fix.",
                            new GUIStyle(_sMuted) { fontSize = 9 });
                        y += warnH + 10;

                        // Only show Claude Code options for SRP
                        GUI.enabled = true;
                        if (Btn(new Rect(x, y, 172, 36), "📋  Copy Prompt", C_ACCENT))
                        {
                            string prompt = BuildClaudeCodePrompt(v, allViolationsInFile);
                            EditorGUIUtility.systemCopyBuffer = prompt;
                            _statusMsg = "✓ Prompt copied — paste into Claude Code";
                            Repaint();
                        }
                        if (Btn(new Rect(x + 180, y, 160, 36), "🚀  Run in Tool", C_GREEN))
                            LaunchEmbeddedClaude(v, allViolationsInFile);
                        y += 46;
                    }
                    else
                    {
                        // Non-SRP: Generate Fix works fine (single-file changes only)
                        GUI.Label(new Rect(x, y, w, 20),
                            "Generate Fix — single API call, fixes within this file only.", _sMuted);
                        y += 28;

                        GUI.enabled = !_isFixing;
                        if (Btn(new Rect(x, y, 136, 36), "⚡  Generate Fix", C_ACCENT))
                        {
                            _pendingFixKey  = key;
                            _showCostPrompt = true;
                        }

                        GUI.enabled = true;
                        if (Btn(new Rect(x + 144, y, 148, 36), "📋  Copy Prompt", C_SURF2))
                        {
                            string prompt = BuildClaudeCodePrompt(v, allViolationsInFile);
                            EditorGUIUtility.systemCopyBuffer = prompt;
                            _statusMsg = "✓ Prompt copied — paste into Claude Code";
                            Repaint();
                        }
                        if (Btn(new Rect(x + 300, y, 160, 36), "🚀  Open Claude Code", C_GREEN))
                            OpenInClaudeCode(v, allViolationsInFile);
                        y += 46;
                    }

                    // Explain all options
                    Bg(new Rect(x, y, w, hasSrp ? 42f : 56f),
                        new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.04f));
                    Bg(new Rect(x, y, 3, hasSrp ? 42f : 56f),
                        new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f));
                    if (!hasSrp)
                    {
                        GUI.Label(new Rect(x + 12, y + 5, w - 20, 14),
                            "⚡  Generate Fix  — quick AI fix via API.",
                            new GUIStyle(_sMuted) { fontSize = 9 });
                        GUI.Label(new Rect(x + 12, y + 21, w - 20, 14),
                            "🚀  Run in Tool  — opens Claude Code with your project context.",
                            new GUIStyle(_sMuted) { fontSize = 9 });
                        y += 56;
                    }
                    else
                    {
                        GUI.Label(new Rect(x + 12, y + 5, w - 20, 14),
                            "📋  Copy Prompt  — copy prompt to paste into Claude Code.",
                            new GUIStyle(_sMuted) { fontSize = 9 });
                        GUI.Label(new Rect(x + 12, y + 21, w - 20, 14),
                            "🚀  Run in Tool  — opens Terminal and runs Claude Code in your project automatically.",
                            new GUIStyle(_sMuted) { fontSize = 9 });
                        y += 42;
                    }

                }

                // ── Cost confirmation prompt ──────────────────────────────────────
                if (_showCostPrompt && _pendingFixKey == key)
                {
                    var pr = new Rect(x, y, w, 110);
                    Bg(pr, new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.08f));
                    Outline(pr, new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.45f));
                    Bg(new Rect(x, y, 3, 110), C_YELLOW);

                    GUI.Label(new Rect(x + 14, y + 10, w - 24, 18),
                        "⚠  This will use your Claude API credits",
                        new GUIStyle(_sBody)
                        {
                            fontStyle = FontStyle.Bold,
                            normal    = { textColor = C_YELLOW }
                        });
                    GUI.Label(new Rect(x + 14, y + 32, w - 24, 32),
                        "Generating a fix calls the Claude API and counts toward your\nusage. Typical cost: ~$0.001–$0.01 per fix depending on file size.",
                        new GUIStyle(_sMuted) { fontSize = 10 });

                    if (Btn(new Rect(x + 14, y + 76, 130, 26), "Yes, Generate Fix", C_ACCENT))
                    {
                        _showCostPrompt = false;
                        GenerateFixAsync(_pendingFixKey);
                        _pendingFixKey = null;
                    }
                    if (Btn(new Rect(x + 154, y + 76, 72, 26), "Cancel", C_SURF2))
                    { _showCostPrompt = false; _pendingFixKey = null; }

                    y += 120;
                }
                return;
            }

            // Summary card
            SecLabel(x, ref y, "What changes");
            DrawCard(x, ref y, w, fix.DiffSummary, _sBody);

            // Explanation
            SecLabel(x, ref y, "Why this is correct");
            var mutedBody = new GUIStyle(_sBody) { normal = { textColor = C_MUTED } };
            DrawCard(x, ref y, w, fix.Explanation, mutedBody);

            // Fixed code — with clear FIXED label and green accent
            SecLabel(x, ref y, "Fixed code  ·  review before applying");
            // Green accent bar to distinguish from original code
            Bg(new Rect(x, y, w, 24), new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.06f));
            Bg(new Rect(x, y, 3, 24), C_GREEN);
            GUI.Label(new Rect(x + 14, y + 5, 200, 14), "PROPOSED FIX",
                new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.85f) } });
            y += 24;
            DrawCodeBlock(fix.FixedCode, x, ref y, w);

            // ── Contract Check panel ─────────────────────────────────────────────
            string vkey = MakeKey(v);
            if (_contractChecks.TryGetValue(vkey, out var cc))
            {
                y += 6;
                SecLabel(x, ref y, "Behavioral Contract Check");

                Color checkCol    = cc.Passed ? C_GREEN : C_RED;
                Color checkBg     = new Color(checkCol.r, checkCol.g, checkCol.b, 0.08f);
                Color checkBorder = new Color(checkCol.r, checkCol.g, checkCol.b, 0.45f);

                // ── Calculate exact panel height FIRST ───────────────────────────
                int totalRows = cc.Preserved.Count + cc.Added.Count + cc.Removed.Count + cc.Moved.Count;
                float panelH  = 44f
                              + totalRows * 18f
                              + 14f;

                // Draw background with correct height
                Bg(new Rect(x, y, w, panelH), checkBg);
                Outline(new Rect(x, y, w, panelH), checkBorder);
                Bg(new Rect(x, y, 3, panelH), checkCol);

                // Summary
                GUI.Label(new Rect(x + 12, y + 10, w - 24, 18), cc.Summary,
                    new GUIStyle(_sBody) { fontStyle = FontStyle.Bold, normal = { textColor = checkCol } });

                // Syntax line
                GUI.Label(new Rect(x + 12, y + 26, w - 24, 14),
                    cc.CompilesParsed ? "✓  Syntax valid (braces balanced)" : "✗  Syntax issue detected",
                    new GUIStyle(_sMuted) { fontSize = 10,
                        normal = { textColor = cc.CompilesParsed ? C_GREEN : C_RED } });

                float ry = y + 44;

                // Preserved methods
                foreach (var m in cc.Preserved.Where(p => !cc.Moved.Any(mv => mv.Name == p.Name)))
                {
                    GUI.Label(new Rect(x + 14, ry, 14, 14), "✓",
                        new GUIStyle(_sMuted) { normal = { textColor = C_GREEN } });
                    GUI.Label(new Rect(x + 30, ry, w - 40, 14), m.Name + "()  — preserved",
                        new GUIStyle(_sMuted) { fontSize = 10,
                            normal = { textColor = new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.8f) } });
                    ry += 18;
                }

                // Moved methods (to new files — fine, actually good for SRP)
                foreach (var m in cc.Moved)
                {
                    GUI.Label(new Rect(x + 14, ry, 14, 14), "→",
                        new GUIStyle(_sMuted) { normal = { textColor = C_ACCENT } });
                    GUI.Label(new Rect(x + 30, ry, w - 40, 14), m.Name + "()  — moved to new file (SRP split)",
                        new GUIStyle(_sMuted) { fontSize = 10,
                            normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.8f) } });
                    ry += 18;
                }

                // Added methods
                foreach (var m in cc.Added)
                {
                    GUI.Label(new Rect(x + 14, ry, 14, 14), "+",
                        new GUIStyle(_sMuted) { normal = { textColor = C_ACCENT } });
                    GUI.Label(new Rect(x + 30, ry, w - 40, 14), m.Name + "()  — new method added",
                        new GUIStyle(_sMuted) { fontSize = 10,
                            normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.8f) } });
                    ry += 18;
                }

                // Removed methods — these are truly gone
                foreach (var m in cc.Removed)
                {
                    GUI.Label(new Rect(x + 14, ry, 14, 14), "✗",
                        new GUIStyle(_sMuted) { normal = { textColor = C_RED } });
                    GUI.Label(new Rect(x + 30, ry, w - 40, 14), m.Name + "()  — REMOVED from original",
                        new GUIStyle(_sMuted) { fontSize = 10, fontStyle = FontStyle.Bold,
                            normal = { textColor = C_RED } });
                    ry += 18;
                }

                y = y + panelH + 10;

                // Show new files with their full content for review
                if (fix.NewFilesNeeded != null && fix.NewFilesNeeded.Count > 0)
                {
                    y += 8;
                    SecLabel(x, ref y, $"New files this fix will create  ({fix.NewFilesNeeded.Count})");

                    foreach (var newFile in fix.NewFilesNeeded)
                    {
                        // File header bar
                        Bg(new Rect(x, y, w, 26), new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.08f));
                        Outline(new Rect(x, y, w, 26), new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f));
                        Bg(new Rect(x, y, 3, 26), C_ACCENT);
                        GUI.Label(new Rect(x + 14, y + 5, w - 20, 16), "NEW FILE:  " + newFile,
                            new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold,
                                normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.9f) } });
                        y += 26;

                        // Show content if available
                        bool hasContent = fix.NewFileContents != null
                            && fix.NewFileContents.TryGetValue(newFile, out var newContent)
                            && !string.IsNullOrWhiteSpace(newContent);

                        if (hasContent)
                            DrawCodeBlock(fix.NewFileContents[newFile], x, ref y, w, 1);
                        else
                        {
                            // No content — warn developer
                            Bg(new Rect(x, y, w, 32), new Color(C_RED.r, C_RED.g, C_RED.b, 0.08f));
                            Outline(new Rect(x, y, w, 32), new Color(C_RED.r, C_RED.g, C_RED.b, 0.3f));
                            GUI.Label(new Rect(x + 12, y + 8, w - 24, 16),
                                "⚠  Content for this file was not returned by Claude — applying may cause compile errors",
                                new GUIStyle(_sMuted) { fontSize = 9,
                                    normal = { textColor = new Color(C_RED.r, C_RED.g, C_RED.b, 0.9f) } });
                            y += 38;
                        }
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TAB: APPLIED
        // ════════════════════════════════════════════════════════════════════════

        private void DrawRegressionTab(float x, ref float y, float w)
        {
            string key = _activeId;
            var dec = key != null ? _decisions.GetValueOrDefault(key) : ReviewDecision.None;

            if (dec != ReviewDecision.Applied)
            {
                Bg(new Rect(x, y, w, 48), new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.06f));
                Bg(new Rect(x, y, 3, 48), C_MUTED);
                GUI.Label(new Rect(x + 14, y + 14, w - 20, 18),
                    "Apply the fix first — this tab shows a summary after the fix is written to disk.",
                    new GUIStyle(_sMuted) { fontSize = 10 });
                y += 60;
                return;
            }

            var v   = FindViolation(key);
            var fix = _fixes.ContainsKey(key) ? _fixes[key] : null;

            // ── Green confirmation banner ─────────────────────────────────────
            Bg(new Rect(x, y, w, 52), new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.06f));
            Outline(new Rect(x, y, w, 52), new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.35f));
            Bg(new Rect(x, y, 3, 52), C_GREEN);
            GUI.Label(new Rect(x + 14, y + 8, w - 20, 18), "✓  Fix applied successfully",
                new GUIStyle(_sBody) { fontStyle = FontStyle.Bold,
                    normal = { textColor = C_GREEN } });
            GUI.Label(new Rect(x + 14, y + 28, w - 20, 16),
                "Unity will recompile automatically. Check the Console for any errors.",
                new GUIStyle(_sMuted) { fontSize = 9 });
            y += 62;

            // ── What was written ──────────────────────────────────────────────
            if (fix != null)
            {
                SecLabel(x, ref y, "Files written to disk");

                // Main file
                Bg(new Rect(x, y, w, 28), C_SURF2);
                Outline(new Rect(x, y, w, 28), C_BORDER);
                Bg(new Rect(x, y, 3, 28), C_GREEN);
                GUI.Label(new Rect(x + 14, y + 7, w - 20, 14),
                    "MODIFIED  —  " + (v?.Location.FileName ?? ""),
                    new GUIStyle(_sBody) { fontSize = 9,
                        normal = { textColor = new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.9f) } });
                y += 32;

                // New files
                foreach (var nf in fix.NewFilesNeeded)
                {
                    Bg(new Rect(x, y, w, 28), C_SURF2);
                    Outline(new Rect(x, y, w, 28), C_BORDER);
                    Bg(new Rect(x, y, 3, 28), C_ACCENT);
                    GUI.Label(new Rect(x + 14, y + 7, w - 20, 14),
                        "CREATED   —  " + nf,
                        new GUIStyle(_sBody) { fontSize = 9,
                            normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.9f) } });
                    y += 32;
                }

                // What changed summary
                if (!string.IsNullOrEmpty(fix.DiffSummary))
                {
                    y += 8;
                    SecLabel(x, ref y, "What changed");
                    DrawCard(x, ref y, w, fix.DiffSummary, _sBody);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ACTION BAR
        // ════════════════════════════════════════════════════════════════════════

        private void DrawActionBar(Rect r, Violation v)
        {
            Bg(r, C_SURF);
            HRule(r, C_BORDER);

            string key    = MakeKey(v);
            var    dec    = _decisions.GetValueOrDefault(key);
            bool   done   = dec != ReviewDecision.None;
            bool   hasFix = _fixes.ContainsKey(key);
            bool   hasKey = !string.IsNullOrEmpty(ApiKey);


            GUI.enabled = !done && !_isFixing && hasFix;
            if (Btn(new Rect(r.x + 16, r.y + 11, 110, 32), "✓  Apply Fix", C_GREEN))
                ApplyFixAsync(key);

            GUI.enabled = !done;
            if (Btn(new Rect(r.x + 134, r.y + 11, 56, 32), "Skip", C_SURF2))
                SkipViolation(key);

            GUI.enabled = hasFix;
            if (Btn(new Rect(r.x + 198, r.y + 11, 116, 32), "View Full Code", C_SURF2))
                _activeTab = 1;

            GUI.enabled = true;
            if (Btn(new Rect(r.x + 322, r.y + 11, 100, 32), "⬇  File Doc", C_ACCENT))
                ExportFilePDF(v.Location.FilePath);

            // Right side badge
            if (dec == ReviewDecision.Applied)
                DrawPill(new Rect(r.x + r.width - 88, r.y + 17, 72, 20), "✓ Applied", C_GREEN);
            else if (dec == ReviewDecision.Skipped)
                DrawPill(new Rect(r.x + r.width - 88, r.y + 17, 72, 20), "Skipped", C_MUTED);
            else if (!hasKey && !hasFix)
                GUI.Label(new Rect(r.x + r.width - 200, r.y + 18, 184, 16),
                    "🔒 API key needed for fixes",
                    new GUIStyle(_sMuted)
                    {
                        fontSize  = 10, alignment = TextAnchor.MiddleRight,
                        normal    = { textColor = new Color(C_YELLOW.r, C_YELLOW.g, C_YELLOW.b, 0.9f) }
                    });
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SETTINGS
        // ════════════════════════════════════════════════════════════════════════

        private void DrawSettings(Rect body)
        {
            float cw = 500f, ch = 360f;
            float cx = body.x + body.width  / 2f;
            float cy = body.y + body.height / 2f;
            var card = new Rect(cx - cw/2, cy - ch/2, cw, ch);

            Bg(card, C_SURF);
            Outline(card, C_BORDER);

            float px = card.x + 24, py = card.y + 24, pw = cw - 48;

            GUI.Label(new Rect(px, py, pw, 24), "Settings", _sTitle); py += 36;

            // ── API Key ──────────────────────────────────────────────────────────
            GUI.Label(new Rect(px, py, pw, 16), "Claude API Key", _sBody); py += 20;

            bool hasKey = !string.IsNullOrEmpty(ApiKey);
            Color sc = hasKey ? C_GREEN : C_RED;
            GUI.Label(new Rect(px, py, pw, 16),
                hasKey ? "✓  Key is saved" : "✗  No key — AI fixes disabled",
                new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = sc } });
            py += 20;

            string nk = EditorGUI.PasswordField(new Rect(px, py, pw - 82, 28), ApiKey);
            if (nk != ApiKey) ApiKey = nk;

            if (hasKey && !_showClearKeyWarning)
            {
                if (Btn(new Rect(px + pw - 76, py, 68, 28), "Clear", C_RED))
                    _showClearKeyWarning = true;
            }
            py += 38;

            if (_showClearKeyWarning)
            {
                var warnRect = new Rect(px, py, pw, 82);
                Bg(warnRect, new Color(C_RED.r, C_RED.g, C_RED.b, 0.1f));
                Outline(warnRect, new Color(C_RED.r, C_RED.g, C_RED.b, 0.5f));
                Bg(new Rect(px, py, 3, 82), C_RED);
                GUI.Label(new Rect(px + 12, py + 10, pw - 20, 18),
                    "⚠  Are you sure you want to remove the API key?",
                    new GUIStyle(_sBody) { normal = { textColor = C_RED }, fontStyle = FontStyle.Bold });
                GUI.Label(new Rect(px + 12, py + 30, pw - 20, 16),
                    "You will need to paste it again to generate AI fixes.",
                    new GUIStyle(_sMuted) { fontSize = 10 });
                if (Btn(new Rect(px + 12, py + 52, 100, 22), "Yes, Remove", C_RED))
                { ApiKey = ""; _showClearKeyWarning = false; }
                if (Btn(new Rect(px + 120, py + 52, 72, 22), "Cancel", C_SURF2))
                    _showClearKeyWarning = false;
                py += 92;
            }

            HRule(new Rect(px, py, pw, 1), C_BORDER); py += 16;

            // ── Scan Folder ──────────────────────────────────────────────────────
            GUI.Label(new Rect(px, py, pw, 16), "Scan Folders", _sBody); py += 18;

            // Show selected folders summary
            bool hasFolder = _scanRoots.Count > 0;
            string folderDisplay = !hasFolder
                ? "Entire Assets folder  (default)"
                : _scanRoots.Count == 1
                    ? _scanRoots.First().Replace(Application.dataPath, "Assets")
                    : $"{_scanRoots.Count} folders selected";
            Color folderCol = hasFolder ? C_ACCENT : C_MUTED;

            Bg(new Rect(px, py, pw, 28), C_SURF2);
            Outline(new Rect(px, py, pw, 28), hasFolder ? C_ACCENT : C_BORDER);
            if (hasFolder) Bg(new Rect(px, py, 3, 28), C_ACCENT);
            GUI.Label(new Rect(px + 10, py + 6, pw - 120, 16), folderDisplay,
                new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = folderCol } });
            py += 36;

            GUI.Label(new Rect(px, py, pw, 14),
                "Use the folder picker on the home screen to select specific folders.",
                new GUIStyle(_sMuted) { fontSize = 9 });
            if (hasFolder && Btn(new Rect(px, py + 18, 130, 28), "Clear All Folders", C_SURF2))
                _scanRoots.Clear();
            py += 52;

            GUI.Label(new Rect(px, py, pw, 16),
                "API key stored in EditorPrefs only — never committed to version control.", _sMuted);
            py += 32;

            if (Btn(new Rect(px, py, 116, 32), "Save & Close", C_ACCENT))
                _showSettings = false;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SYNTAX-HIGHLIGHTED CODE BLOCK
        // ════════════════════════════════════════════════════════════════════════

        private void DrawCodeBlock(string code, float x, ref float y, float w, int startLine = 1)
        {
            if (string.IsNullOrEmpty(code)) return;

            var   lines   = code.Split('\n');
            float lineH   = 20f;       // taller rows — easier to read
            float padV    = 8f;
            float gutterW = 52f;       // wider gutter — 4-digit line numbers + padding
            float totalH  = lines.Length * lineH + padV * 2;

            // Outer accent border
            Bg(new Rect(x, y, w, totalH + 2),
               new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f));

            // Code area background — VS Code Dark+ style
            Bg(new Rect(x, y + 1, w, totalH), new Color32(13, 17, 23, 255));

            // Gutter background
            Bg(new Rect(x, y + 1, gutterW, totalH), new Color32(16, 20, 28, 255));

            // Gutter separator — subtle vertical line
            Bg(new Rect(x + gutterW, y + 1, 1, totalH),
               new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.45f));

            for (int i = 0; i < lines.Length; i++)
            {
                float ly  = y + 1 + padV + i * lineH;
                string raw = lines[i].TrimEnd('\r');

                // Alternating row — very subtle
                if (i % 2 == 0)
                    Bg(new Rect(x + gutterW + 1, ly, w - gutterW - 1, lineH),
                       new Color(1, 1, 1, 0.016f));

                // Hover row highlight
                if (new Rect(x, ly, w, lineH).Contains(Event.current.mousePosition))
                    Bg(new Rect(x + gutterW + 1, ly, w - gutterW - 1, lineH),
                       new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.045f));

                // Line number — right-aligned, actual file line number
                GUI.Label(
                    new Rect(x + 4, ly + 1, gutterW - 10, lineH - 2),
                    (startLine + i).ToString(),
                    new GUIStyle(_sLineNum) { alignment = TextAnchor.MiddleRight });

                // Syntax-highlighted code line
                string highlighted = BuildRichTextLine(raw);
                GUI.Label(
                    new Rect(x + gutterW + 10, ly + 1, w - gutterW - 16, lineH - 2),
                    highlighted,
                    new GUIStyle(_sCode) { fontSize = 12 });
            }

            y += totalH + 2 + 16;
        }

        // Build a richText string with <color=#RRGGBB> tags — works reliably in Unity IMGUI
        private string BuildRichTextLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return " ";

            string trimmed = line.TrimStart();

            // Full-line comment
            if (trimmed.StartsWith("///") || trimmed.StartsWith("//"))
                return Colorize(EscapeRich(line), SYN_COMMENT);

            // Build token-by-token
            var sb = new System.Text.StringBuilder();
            var pattern = new Regex(
                @"(""[^""]*""|@""[^""]*"")" +    // string literals
                @"|('.')" +                        // char literals
                @"|(//.*$)" +                      // inline comment
                @"|(\b\d+\.?\d*[fFdDmM]?\b)" +    // numbers
                @"|([A-Za-z_]\w*)" +               // identifiers / keywords
                @"|(\s+)" +                        // whitespace (preserve)
                @"|(.)",                           // any other char
                RegexOptions.Compiled);

            foreach (Match m in pattern.Matches(line))
            {
                string tok = m.Value;
                string escaped = EscapeRich(tok);

                if (m.Groups[1].Success || m.Groups[2].Success)
                    sb.Append(Colorize(escaped, SYN_STRING));
                else if (m.Groups[3].Success)
                    sb.Append(Colorize(escaped, SYN_COMMENT));
                else if (m.Groups[4].Success)
                    sb.Append(Colorize(escaped, SYN_NUMBER));
                else if (m.Groups[5].Success)
                {
                    if (Keywords.Any(k => k == tok))
                        sb.Append(Colorize(escaped, SYN_KEYWORD));
                    else if (BuiltinTypes.Any(t => t == tok))
                        sb.Append(Colorize(escaped, SYN_TYPE));
                    else if (tok.Length > 0 && char.IsUpper(tok[0]))
                        sb.Append(Colorize(escaped, SYN_TYPE));
                    else
                        sb.Append(Colorize(escaped, SYN_PLAIN));
                }
                else
                    sb.Append(Colorize(escaped, SYN_PLAIN));
            }

            return sb.ToString();
        }

        private static string Colorize(string text, Color c)
        {
            string hex = ColorUtility.ToHtmlStringRGB(c);
            return $"<color=#{hex}>{text}</color>";
        }

        private static string EscapeRich(string s)
            => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static readonly string[] Keywords = {
            "public","private","protected","internal","static","readonly","const","abstract",
            "virtual","override","sealed","partial","new","class","interface","enum","struct",
            "namespace","using","return","void","var","if","else","for","foreach","while",
            "switch","case","break","continue","default","null","true","false","this","base",
            "throw","try","catch","finally","async","await","in","out","ref","params",
            "get","set","event","delegate","operator","implicit","explicit","where","yield"
        };

        private static readonly string[] BuiltinTypes = {
            "int","float","double","bool","string","byte","char","long","uint","object",
            "List","Dictionary","Array","Task","Action","Func","IEnumerable","IList",
            "MonoBehaviour","GameObject","Transform","Vector2","Vector3","Quaternion",
            "Rigidbody","Rigidbody2D","Collider","Collider2D","AudioSource","Animator",
            "ScriptableObject","EditorWindow","SerializeField","RequireComponent"
        };

        // ════════════════════════════════════════════════════════════════════════
        //  UI PRIMITIVES
        // ════════════════════════════════════════════════════════════════════════

        // Cards: pass content height upfront so bg draws first, content draws on top
        private void DrawCard(float x, ref float y, float w, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            float pad = 14f;
            float th  = style.CalcHeight(new GUIContent(text), w - 32);
            float h   = th + pad * 2;

            // Draw background FIRST
            Bg(new Rect(x, y, w, h), C_SURF2);
            Outline(new Rect(x, y, w, h), C_BORDER);

            // Draw content ON TOP
            GUI.Label(new Rect(x + 16, y + pad, w - 32, th), text, style);

            y += h + 8;
        }

        private void DrawPill(Rect r, string text, Color col)
        {
            Bg(r, new Color(col.r, col.g, col.b, 0.15f));
            Outline(r, new Color(col.r, col.g, col.b, 0.45f));
            GUI.Label(r, text,
                new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize  = 9, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = col }
                });
        }

        private void SecLabel(float x, ref float y, string text)
        {
            GUI.Label(new Rect(x, y, 500, 16), text.ToUpper(),
                new GUIStyle(_sMuted)
                {
                    fontSize  = 9, fontStyle = FontStyle.Bold,
                    normal    = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.65f) }
                });
            y += 20;
        }

        // ── Background — GD game icon collage + hatch overlay ────────────────────
        private void DrawBg(Rect r)
        {
            // Pure black base
            EditorGUI.DrawRect(r, C_BG);

            // (icon wallpaper moved to GD Quality Hub)

            // ── Diagonal hatch overlay ────────────────────────────────────────────
            if (_bgDotTex == null) _bgDotTex = BuildHatchTex();
            if (_bgDotTex != null)
            {
                GUI.color = Color.white;
                int tileW = _bgDotTex.width;
                int tileH = _bgDotTex.height;
                for (float tx = r.x; tx < r.x + r.width; tx += tileW)
                    for (float ty = r.y; ty < r.y + r.height; ty += tileH)
                        GUI.DrawTexture(new Rect(tx, ty, tileW, tileH), _bgDotTex, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }

            // ── Yellow radial glow at center ──────────────────────────────────────
            float cx = r.x + r.width  * 0.5f;
            float cy = r.y + r.height * 0.5f;
            for (int i = 8; i >= 1; i--)
            {
                float frac  = (float)i / 8f;
                float hw    = r.width  * 0.55f * frac;
                float hh    = r.height * 0.55f * frac;
                float alpha = 0.022f * (1f - frac);
                EditorGUI.DrawRect(
                    new Rect(cx - hw, cy - hh, hw * 2f, hh * 2f),
                    new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, alpha));
            }
        }

        // Builds a 16×16 tile with a single 45° diagonal line — GD site signature pattern
        private Texture2D BuildHatchTex()
        {
            const int S = 16; // tile size — controls line spacing
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode   = TextureWrapMode.Repeat;

            var pixels = new Color32[S * S];
            // Transparent fill
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            // Draw one anti-aliased diagonal pixel stripe at 45° (top-left to bottom-right)
            // GD yellow at ~8% opacity — barely-there, exactly like the site
            var lineCol  = new Color32(255, 211, 0, 20);  // #FFD300 @ 8%
            var lineCol2 = new Color32(255, 211, 0, 8);   // softer neighbour for AA feel

            for (int i = 0; i < S; i++)
            {
                int x = i;
                int y = (S - 1 - i);           // anti-diagonal
                pixels[y * S + x] = lineCol;
                // soft pixel above/below for a subtle AA feel
                if (y + 1 < S) pixels[(y + 1) * S + x] = lineCol2;
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private void Bg(Rect r, Color c)       => EditorGUI.DrawRect(r, c);
        private void HRule(Rect r, Color c)    => EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
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

        private bool Btn(Rect r, string text, Color col)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            float alpha = hover ? 0.25f : 0.15f;
            Bg(r, new Color(col.r, col.g, col.b, alpha));
            Outline(r, new Color(col.r, col.g, col.b, hover ? 0.6f : 0.35f));
            GUI.Label(r, text,
                new GUIStyle(_sBody)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = col }
                });
            return Click(r);
        }

        // Top bar buttons — always visible, solid border, never disappear
        private bool TopBarBtn(Rect r, string text)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            // Always visible solid background
            Bg(r, hover
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f)
                : new Color(0.22f, 0.22f, 0.22f, 1f));
            // Always visible border — yellow on hover, grey at rest
            Outline(r, hover
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.9f)
                : new Color(0.45f, 0.45f, 0.45f, 1f));
            GUI.Label(r, text,
                new GUIStyle(_sBody)
                {
                    fontSize  = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = hover ? C_ACCENT : C_TEXT }
                });
            return Click(r);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ACTIONS
        // ════════════════════════════════════════════════════════════════════════

        private async void StartScan()
        {
            _screen = Screen.Scanning; _statusMsg = "Reading scripts…";
            _scanProgress = 0f;
            _results.Clear(); _fixes.Clear(); _decisions.Clear(); _activeId = null;
            Repaint();

            var files = new List<string>();
            await Task.Run(() =>
            {
                // Collect scan roots — if none selected, scan entire Assets folder
                var roots = _scanRoots.Count > 0
                    ? _scanRoots.ToList()
                    : new List<string> { Application.dataPath };

                var seen = new HashSet<string>();
                foreach (var root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                    {
                        if (!seen.Add(f)) continue; // deduplicate if roots overlap

                        string n    = f.Replace('\\', '/');
                        string name = Path.GetFileName(f);

                        // Skip Unity system / generated folders — not developer code
                        if (n.Contains("/PackageCache/"))      continue;
                        if (n.Contains("/TextMesh Pro/"))      continue;
                        if (n.Contains("/Editor/"))            continue;
                        if (n.Contains("/Packages/"))          continue;
                        if (n.Contains(".Generated.cs"))       continue;
                        if (n.Contains("AssemblyInfo.cs"))     continue;

                        // Skip files over 200KB — almost certainly generated or third-party
                        try { if (new FileInfo(f).Length > 200 * 1024) continue; } catch { continue; }

                    files.Add(f);
                    }  // end foreach file
                }  // end foreach root
            });

            var analyzer = new SolidAnalyzer();

            for (int i = 0; i < files.Count; i++)
            {
                string f = files[i];
                _statusMsg    = Path.GetFileName(f);
                _scanProgress = (float)(i + 1) / files.Count;
                Repaint();

                FileAnalysisResult result = null;
                var t = Task.Run(() => { try { result = analyzer.AnalyzeFile(f); } catch { } });
                if (!t.Wait(5000)) result = null;
                if (result != null) _results.Add(result);
                await Task.Delay(10);
            }

            int total = _results.Sum(r => r.Violations.Count);
            _statusMsg = total == 0 ? "No violations found." : $"Found {total} violation(s).";
            _screen    = Screen.Results;

            // Generate ratings report
            string projName = System.IO.Path.GetFileName(Application.dataPath.TrimEnd('/').TrimEnd('\\'));
            _report = RatingEngine.GenerateReport(_results, projName);

            var first = _results.SelectMany(r => r.Violations).FirstOrDefault();
            if (first != null) { _activeId = MakeKey(first); _screen = Screen.Detail; }
            Repaint();
        }

        private async void GenerateFixAsync(string key)
        {
            if (string.IsNullOrEmpty(ApiKey)) return;
            _isFixing = true;
            _fixStartTime = EditorApplication.timeSinceStartup;
            _statusMsg = "📡  Connecting to Claude API…"; Repaint();
            await Task.Delay(200);

            _statusMsg = "📂  Reading source file…"; Repaint();
            var v      = FindViolation(key);
            string src = File.ReadAllText(v.Location.FilePath);

            // Collect ALL violations for this file — fix them all in one Claude call
            var allViolationsInFile = _results
                .Where(r => r.FilePath == v.Location.FilePath)
                .SelectMany(r => r.Violations)
                .ToList();

            // Collect namespace context from sibling and parent files
            // so Claude can generate correct using directives
            string nsContext = "";
            await Task.Run(() => {
                nsContext = BuildNamespaceContext(v.Location.FilePath);
            });

            _statusMsg = allViolationsInFile.Count > 1
                ? $"🔍  Found {allViolationsInFile.Count} violations in {v.Location.FileName} — fixing all at once…"
                : "🔍  Analysing violation…";
            Repaint();
            await Task.Delay(100);

            _statusMsg = "⚙️  Generating fix with Claude…"; Repaint();

            GeneratedFix fix = null;
            string error = null;
            try
            {
                fix = await new AIFixGenerator(ApiKey).GenerateFixAsync(v, src, allViolationsInFile, nsContext);
            }
            catch (System.Exception ex) { error = ex.Message; }

            if (error != null)
            {
                // Store error against the key so the Fix tab can show it inline
                _fixErrors[key] = error;
                _statusMsg = $"✗  Error: {error}";
                _isFixing  = false; _activeTab = 1; Repaint();
                return;
            }

            _statusMsg = "🔬  Checking behavioral contract…"; Repaint();
            await Task.Delay(200);

            string newFilesContent = "";
            if (fix.NewFilesNeeded != null && fix.NewFilesNeeded.Count > 0)
                newFilesContent = string.Join("\n", fix.NewFilesNeeded
                    .Where(f => !string.IsNullOrEmpty(f)));

            var contractCheck = await Task.Run(() =>
                ContractChecker.Check(src, fix.FixedCode ?? "", newFilesContent));
            _contractChecks[key] = contractCheck;

            // Sanity check — if FixedCode is empty or still contains format markers,
            // the parse failed. Show an error instead of storing a broken fix.
            bool parseOk = !string.IsNullOrWhiteSpace(fix.FixedCode)
                && !fix.FixedCode.Contains("FIXED_FILE_START")
                && !fix.FixedCode.Contains("DIFF_SUMMARY:");

            if (!parseOk)
            {
                _fixErrors[key] = "Claude response could not be parsed correctly. Try generating the fix again.";
                _statusMsg = "✗  Parse error — response format unexpected";
                _isFixing  = false; _activeTab = 1; Repaint();
                return;
            }

            // Detect missing new files — find GetComponent<X>() calls in fixedCode
            // where X.cs is not in NewFilesNeeded or NewFileContents
            var missingFiles = new List<string>();
            var getComponentMatches = System.Text.RegularExpressions.Regex.Matches(
                fix.FixedCode, @"GetComponent<(\w+)>\(\)");
            foreach (System.Text.RegularExpressions.Match m in getComponentMatches)
            {
                string typeName = m.Groups[1].Value;
                string fileName = typeName + ".cs";
                // Skip Unity built-in types
                if (typeName.StartsWith("Unity") || typeName == "Transform" ||
                    typeName == "Rigidbody" || typeName == "Collider") continue;
                // Check if this type exists in original source or as a new file
                bool existsInSource = fix.FixedCode.Contains($"class {typeName}") ||
                    (fix.NewFilesNeeded.Contains(fileName) &&
                     fix.NewFileContents.ContainsKey(fileName) &&
                     !string.IsNullOrWhiteSpace(fix.NewFileContents[fileName]));
                if (!existsInSource && !missingFiles.Contains(fileName))
                    missingFiles.Add(fileName);
            }

            if (missingFiles.Count > 0)
            {
                string missing = string.Join(", ", missingFiles);
                _fixErrors[key] = $"Fix references types that were not generated: {missing}\n" +
                    "The fix would cause compile errors. Try generating again — the prompt now explicitly " +
                    "requires all referenced classes to be included.";
                _statusMsg = $"✗  Fix incomplete — {missingFiles.Count} file(s) missing: {missing}";
                _isFixing  = false; _activeTab = 1; Repaint();
                return;
            }

            _statusMsg = contractCheck.Passed
                ? "✓  Fix generated — contract check passed"
                : "⚠  Fix generated — review contract warnings";

            // Store the fix under all violation keys for this file
            foreach (var viol in allViolationsInFile)
                _fixes[MakeKey(viol)] = fix;

            _isFixing    = false;
            _activeTab   = 1;
            Repaint();
        }

        private async void ApplyFixAsync(string key)
        {
            if (!_fixes.TryGetValue(key, out var fix)) return;
            var v = FindViolation(key);
            _isFixing = true;
            _statusMsg = $"Applying fix to {v.Location.FileName}…"; Repaint();

            // Apply fix on background thread — file I/O only, no Unity APIs
            await Task.Run(() => {
                var h = new RegressionHarness(Application.dataPath);
                h.ApplyFix(fix, v.Location.FilePath);
            });

            _decisions[key] = ReviewDecision.Applied;
            _activeTab = 1;
            AssetDatabase.Refresh();
            _statusMsg = $"✓ Fix applied to {v.Location.FileName} — Unity will recompile automatically.";
            _isFixing = false; Repaint();
        }

        // Scans nearby .cs files to extract namespace declarations and using directives
        // so Claude can generate correct imports in new files
        private string BuildNamespaceContext(string filePath)
        {
            var namespaces = new HashSet<string>();
            var usings     = new HashSet<string>();

            try
            {
                // Scan the file itself, its folder, and up to 2 parent folders
                var dirs = new List<string>();
                string dir = Path.GetDirectoryName(filePath);
                for (int i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
                {
                    dirs.Add(dir);
                    dir = Path.GetDirectoryName(dir);
                }

                foreach (var d in dirs)
                {
                    foreach (var f in Directory.GetFiles(d, "*.cs", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            foreach (var line in File.ReadLines(f))
                            {
                                string t = line.Trim();
                                if (t.StartsWith("namespace "))
                                    namespaces.Add(t.TrimEnd('{').Trim());
                                else if (t.StartsWith("using ") && t.EndsWith(";"))
                                    usings.Add(t);
                                else if (!t.StartsWith("//") && t.Length > 0 && !t.StartsWith("using") && !t.StartsWith("namespace"))
                                    break; // stop at first code line
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (namespaces.Count == 0 && usings.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("CONTEXT — namespaces and usings found in this project area:");
            foreach (var u in usings.OrderBy(x => x))
                sb.AppendLine("  " + u);
            foreach (var n in namespaces.OrderBy(x => x))
                sb.AppendLine("  " + n + " { ... }");
            return sb.ToString();
        }

        private void SkipViolation(string key)
        { _decisions[key] = ReviewDecision.Skipped; Repaint(); }

        private void ResetToHome()
        {
            _screen = Screen.Home; _results.Clear(); _fixes.Clear();
            _decisions.Clear(); _fixErrors.Clear(); _activeId = null; _statusMsg = "";
            _report = null; _contractChecks.Clear();
            _showSettings = false;  // close settings if open
            Repaint();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════════

        // ── Embedded Claude Code terminal ─────────────────────────────────────────

        private void LaunchEmbeddedClaude(Violation v, List<Violation> allViolations)
        {
            string prompt  = BuildClaudeCodePrompt(v, allViolations);
            string tmpFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gd_solid_prompt.md");
            System.IO.File.WriteAllText(tmpFile, prompt);
            EditorGUIUtility.systemCopyBuffer = prompt;

            string projectFolder = System.IO.Path.GetDirectoryName(Application.dataPath);
            string claudePath    = FindClaudePath() ?? "claude";

            string scriptFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gd_open_claude.sh");
            System.IO.File.WriteAllText(scriptFile,
                "#!/bin/zsh\n" +
                $"export PATH=\"$PATH:/usr/local/bin:/opt/homebrew/bin:$HOME/.local/bin:$HOME/.npm-global/bin\"\n" +
                $"cd \"{projectFolder}\"\n" +
                "echo ''\n" +
                "echo '=== GD CodeShield — Claude Code Fix ==='\n" +
                $"echo 'File: {v.Location.FileName}'\n" +
                $"echo 'Violations: {allViolations.Count}'\n" +
                "echo 'Launching Claude Code with prompt...'\n" +
                "echo ''\n" +
                $"\"{claudePath}\" \"$(cat \'{tmpFile}\')\"\n"
            );

            try
            {
                System.Diagnostics.Process.Start("/bin/chmod", $"+x \"{scriptFile}\"")?.WaitForExit(1000);

                var osa = new System.Diagnostics.Process();
                osa.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "osascript",
                    Arguments       = $"-e 'tell application \"Terminal\" to do script \"{scriptFile}\"' " +
                                      $"-e 'tell application \"Terminal\" to activate'",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };
                osa.Start();

                lock (_terminalLock)
                {
                    _terminalOutput.Clear();
                    _terminalOutput.AppendLine("🚀  Claude Code is running in Terminal");
                    _terminalOutput.AppendLine("");
                    _terminalOutput.AppendLine($"File:  {v.Location.FilePath}");
                    _terminalOutput.AppendLine("");
                    _terminalOutput.AppendLine("Claude Code is reading your project files and");
                    _terminalOutput.AppendLine("applying the fix. Watch the Terminal window.");
                    _terminalOutput.AppendLine("");
                    _terminalOutput.AppendLine("When it finishes, switch back to Unity.");
                    _terminalOutput.AppendLine("Unity will recompile automatically.");
                }
                _terminalDone    = false;
                _terminalRunning = false;
                _activeTab       = 2;
                Repaint();
            }
            catch (System.Exception ex)
            {
                lock (_terminalLock)
                {
                    _terminalOutput.Clear();
                    _terminalOutput.AppendLine($"Could not open Terminal: {ex.Message}");
                    _terminalOutput.AppendLine("Prompt copied to clipboard.");
                    _terminalOutput.AppendLine($"Open Terminal manually, cd to your project, then run: claude");
                }
                _activeTab = 2;
                Repaint();
            }
        }


        private string FindClaudePath()
        {
            // Unity Editor child processes don't inherit the shell PATH (zsh/bash)
            // So we ask the shell itself where claude is
            string[] shells = { "/bin/zsh", "/bin/bash" };
            foreach (var shell in shells)
            {
                try
                {
                    var p = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName               = shell,
                            Arguments              = "-l -c \"which claude\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError  = true,
                            UseShellExecute        = false,
                            CreateNoWindow         = true
                        }
                    };
                    p.Start();
                    string result = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(3000);
                    if (!string.IsNullOrEmpty(result) && System.IO.File.Exists(result))
                        return result;
                }
                catch { }
            }

            // Also check common npm global install locations
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            string[] candidates =
            {
                "/usr/local/bin/claude",
                "/opt/homebrew/bin/claude",
                System.IO.Path.Combine(home, ".npm-global/bin/claude"),
                System.IO.Path.Combine(home, "Library/pnpm/claude"),
                System.IO.Path.Combine(home, ".volta/bin/claude"),
                System.IO.Path.Combine(home, ".nvm/versions/node/bin/claude"),
            };
            foreach (var c in candidates)
                if (System.IO.File.Exists(c)) return c;

            return null;
        }

        private void DrawEmbeddedTerminal(float x, ref float y, float w)
        {
            // Header
            Bg(new Rect(x, y, w, 28), new Color(0.08f, 0.08f, 0.08f, 1f));
            Bg(new Rect(x, y, w, 2), _terminalDone && !_terminalRunning ? C_GREEN : C_ACCENT);

            string title = _terminalRunning ? "● Claude Code running…" :
                           _terminalDone    ? "✓ Claude Code finished" :
                                             "Claude Code Terminal";
            GUI.Label(new Rect(x + 10, y + 6, w - 120, 16), title,
                new GUIStyle(_sBody) { fontSize = 10,
                    normal = { textColor = _terminalRunning ? C_ACCENT : _terminalDone ? C_GREEN : C_MUTED } });

            // Stop / Clear buttons
            if (_terminalRunning)
            {
                if (Btn(new Rect(x + w - 80, y + 4, 68, 20), "■  Stop", C_RED))
                {
                    try { _claudeProcess?.Kill(); } catch { }
                    _terminalRunning = false; _terminalDone = true;
                }
            }
            else if (Btn(new Rect(x + w - 80, y + 4, 68, 20), "Clear", C_SURF2))
            {
                lock (_terminalLock) _terminalOutput.Clear();
                _terminalDone = false;
            }
            y += 30;

            // Terminal output area
            float termH = 320f;
            Bg(new Rect(x, y, w, termH), new Color(0.05f, 0.05f, 0.05f, 1f));
            Outline(new Rect(x, y, w, termH), new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.25f));

            string output;
            lock (_terminalLock) output = _terminalOutput.ToString();

            // Measure content height
            var termStyle = new GUIStyle(_sMuted)
            {
                fontSize  = 9,
                wordWrap  = true,
                richText  = false,
                normal    = { textColor = new Color(0.85f, 0.85f, 0.85f, 1f) }
            };
            float contentH = termStyle.CalcHeight(new GUIContent(output), w - 20);
            contentH = Mathf.Max(contentH, termH);

            if (_terminalScrollToBottom)
            {
                _terminalScroll = new Vector2(0, contentH);
                _terminalScrollToBottom = false;
            }

            _terminalScroll = GUI.BeginScrollView(
                new Rect(x, y, w, termH), _terminalScroll,
                new Rect(0, 0, w - 16, contentH));
            GUI.Label(new Rect(4, 4, w - 20, contentH), output, termStyle);
            GUI.EndScrollView();
            y += termH + 6;

            // Refresh Unity button when done
            if (_terminalDone && !_terminalRunning)
            {
                Bg(new Rect(x, y, w, 32), new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.07f));
                Outline(new Rect(x, y, w, 32), new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.3f));
                GUI.Label(new Rect(x + 12, y + 8, w - 130, 16),
                    "Claude Code finished — Unity will recompile automatically.",
                    new GUIStyle(_sMuted) { fontSize = 9 });
                y += 36;
            }
        }

        private void OpenInClaudeCode(Violation v, List<Violation> allViolations)
        {
            string prompt  = BuildClaudeCodePrompt(v, allViolations);
            string folder  = Path.GetDirectoryName(Application.dataPath);
            string tmpFile = Path.Combine(Path.GetTempPath(), "gd_solid_prompt.md");
            File.WriteAllText(tmpFile, prompt);

            // Also copy to clipboard as fallback
            EditorGUIUtility.systemCopyBuffer = prompt;

            try
            {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                // Write a shell script that cd's to the project and runs claude
                string scriptFile = Path.Combine(Path.GetTempPath(), "gd_open_claude.sh");
                File.WriteAllText(scriptFile,
                    $"#!/bin/bash\n" +
                    $"cd \"{folder}\"\n" +
                    $"echo ''\n" +
                    $"echo '=== GD CodeShield — SOLID Fix ==='\n" +
                    $"echo 'Prompt loaded from: {tmpFile}'\n" +
                    $"echo 'Press ENTER to run claude, or Ctrl+C to cancel.'\n" +
                    $"echo ''\n" +
                    $"read\n" +
                    $"claude \"$(cat '{tmpFile}')\"\n"
                );
                System.Diagnostics.Process.Start("chmod", $"+x \"{scriptFile}\"");

                // Open Terminal running the script and bring it to front
                var osa = new System.Diagnostics.Process();
                osa.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "osascript",
                    Arguments       = $"-e 'tell application \"Terminal\" to do script \"{scriptFile}\"' -e 'tell application \"Terminal\" to activate'",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };
                osa.Start();

                _statusMsg = "🚀  Terminal opened — press ENTER to run Claude Code (prompt also copied to clipboard)";
#else
                // Windows — write bat file and open cmd
                string batFile = Path.Combine(Path.GetTempPath(), "gd_open_claude.bat");
                File.WriteAllText(batFile,
                    $"@echo off\r\n" +
                    $"cd /d \"{folder}\"\r\n" +
                    $"echo.\r\n" +
                    $"echo === GD CodeShield - SOLID Fix ===\r\n" +
                    $"echo Prompt loaded from: {tmpFile}\r\n" +
                    $"echo Press ENTER to run claude, or Ctrl+C to cancel.\r\n" +
                    $"echo.\r\n" +
                    $"pause\r\n" +
                    $"claude < \"{tmpFile}\"\r\n"
                );

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = batFile,
                    UseShellExecute = true,
                    WorkingDirectory = folder
                });

                _statusMsg = "🚀  Command Prompt opened — press ENTER to run Claude Code";
#endif
            }
            catch (System.Exception ex)
            {
                EditorGUIUtility.systemCopyBuffer = prompt;
                _statusMsg = $"⚠  Could not open Terminal ({ex.Message}). Prompt copied — run: claude  and paste.";
            }

            Repaint();
        }

        private string MakeKey(Violation v)           => v.Location.FilePath + "||" + v.Id;

        private List<Violation> GetFileViolations(string filePath)
            => _results
                .Where(r => r.FilePath == filePath)
                .SelectMany(r => r.Violations)
                .ToList();

        // Builds a complete prompt for use in Claude Code (claude.ai/code or VS Code extension)
        // The developer copies this and pastes it — Claude Code does the rest
        private string BuildClaudeCodePrompt(Violation primary, List<Violation> allViolations)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("# SOLID Fix Task");
            sb.AppendLine();
            sb.AppendLine("You are fixing SOLID principle violations in a Unity C# project.");
            sb.AppendLine("Use your file system tools to read any files you need before making changes.");
            sb.AppendLine();
            sb.AppendLine("## File to fix");
            sb.AppendLine($"`{primary.Location.FilePath}`");
            sb.AppendLine();
            sb.AppendLine("## Violations to fix");
            sb.AppendLine();

            for (int i = 0; i < allViolations.Count; i++)
            {
                var v = allViolations[i];
                sb.AppendLine($"### {i + 1}. {v.Principle} — {v.Title}");
                sb.AppendLine($"- **Evidence:** {v.Evidence}");
                sb.AppendLine($"- **Line:** {v.Location.StartLine}");
                sb.AppendLine($"- **Description:** {v.Description}");

                // Include the violated code so Claude Code doesn't have to search
                if (!string.IsNullOrWhiteSpace(v.OriginalCode))
                {
                    sb.AppendLine();
                    sb.AppendLine("**Violated code:**");
                    sb.AppendLine("```csharp");
                    sb.AppendLine(v.OriginalCode.Trim());
                    sb.AppendLine("```");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Instructions");
            sb.AppendLine();
            sb.AppendLine("1. Read the file at the path above");
            sb.AppendLine("2. Identify every type, interface, and namespace this file depends on");
            sb.AppendLine("3. Read those files too — understand their full signatures before writing any code");
            sb.AppendLine("4. Fix ALL violations listed above in one pass");
            sb.AppendLine("5. If you need to create new files, place them in the same folder as the original");
            sb.AppendLine("6. **CRITICAL:** Before finishing, check every type referenced in every new file you created.");
            sb.AppendLine("   If any type does not exist as a file on disk, create it now.");
            sb.AppendLine("   Do NOT stop until ALL referenced types exist as actual files.");
            sb.AppendLine("7. After writing all files, confirm the list of files created and that each compiles.");
            sb.AppendLine();
            sb.AppendLine("## Rules");
            sb.AppendLine();
            sb.AppendLine("- Every new file must use the EXACT same namespace as the original file");
            sb.AppendLine("- Copy all `using` directives from the original file into new files");
            sb.AppendLine("- Never reference a type you have not read from an actual file");
            sb.AppendLine("- Preserve all existing public method signatures exactly");
            sb.AppendLine("- Keep all MonoBehaviour lifecycle methods intact (Awake, Start, Update, etc.)");
            sb.AppendLine("- No dependency injection — use GetComponent for wiring between MonoBehaviours");
            sb.AppendLine("- After applying the fix, verify Unity will compile it without errors");
            sb.AppendLine();

            // Principle-specific guidance
            var principles = allViolations.Select(v2 => v2.Principle).Distinct().ToList();
            if (principles.Contains(SolidPrinciple.SRP))
            {
                sb.AppendLine("## SRP guidance");
                sb.AppendLine("Split the class into focused MonoBehaviours. The original class becomes a thin");
                sb.AppendLine("orchestrator that delegates to controllers via GetComponent:");
                sb.AppendLine("```csharp");
                sb.AppendLine("private AdsController _ads;");
                sb.AppendLine("void Awake() { _ads = GetComponent<AdsController>(); }");
                sb.AppendLine("public void ShowAd() { _ads.ShowAd(); }  // pure delegation");
                sb.AppendLine("```");
                sb.AppendLine("The new controller classes must NOT depend on each other.");
                sb.AppendLine();
            }
            if (principles.Contains(SolidPrinciple.OCP))
            {
                sb.AppendLine("## OCP guidance");
                sb.AppendLine("Replace type-based switch statements or long if/else chains with polymorphism.");
                sb.AppendLine("Use interfaces or abstract base classes so new types can be added without");
                sb.AppendLine("modifying existing code.");
                sb.AppendLine();
            }
            if (principles.Contains(SolidPrinciple.ISP))
            {
                sb.AppendLine("## ISP guidance");
                sb.AppendLine("Split the fat interface into 2-4 smaller focused interfaces.");
                sb.AppendLine("Each class should only implement the interface(s) relevant to it.");
                sb.AppendLine();
            }
            if (principles.Contains(SolidPrinciple.LSP))
            {
                sb.AppendLine("## LSP guidance");
                sb.AppendLine("Remove NotImplementedException throws. Either implement the method properly,");
                sb.AppendLine("or remove it from the interface (apply ISP first if needed).");
                sb.AppendLine();
            }

            sb.AppendLine("## Project context");
            sb.AppendLine($"- Unity project, Assets folder: look for files relative to the Assets/ directory");
            sb.AppendLine($"- File being fixed is at: `{primary.Location.FilePath}`");
            sb.AppendLine($"- Folder: `{System.IO.Path.GetDirectoryName(primary.Location.FilePath)?.Replace('\\', '/')}`");

            return sb.ToString();
        }



        private Violation FindViolation(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var fr in _results)
                foreach (var v in fr.Violations)
                    if (MakeKey(v) == key) return v;
            return null;
        }

        private float[] RatingColor(float score)
        {
            if (score >= 4.5f) return new float[]{ 0.31f, 0.78f, 0.39f };
            if (score >= 3.5f) return new float[]{ 0.18f, 0.65f, 0.95f };
            if (score >= 2.5f) return new float[]{ 1f,    0.75f, 0f };
            if (score >= 1.5f) return new float[]{ 1f,    0.49f, 0.15f };
            return new float[]{ 0.86f, 0.24f, 0.24f };
        }

        private void ExportPDF()
        {
            if (_report == null) return;
            try
            {
                string path = SolidReportExporter.Export(_report);
                // Don't overwrite regression status — show alongside
                string prevMsg = _statusMsg;
                _statusMsg = string.IsNullOrEmpty(prevMsg) || prevMsg.StartsWith("✓ Doc") || prevMsg.StartsWith("✓ Word")
                    ? "✓ Word doc saved to SolidReports/"
                    : prevMsg + "  ·  ✓ Doc saved";
                // Open the PDF file directly
                System.Diagnostics.Process.Start(path);
            }
            catch (System.Exception ex)
            {
                _statusMsg = $"✗ Export failed: {ex.Message}";
            }
            Repaint();
        }


        private void ExportFilePDF(string filePath)
        {
            try
            {
                var fileResult = _results.FirstOrDefault(r => r.FilePath == filePath);
                if (fileResult == null) return;
                string path = SolidReportExporter.ExportFile(fileResult, _report);
                string prevMsg2 = _statusMsg;
                _statusMsg = string.IsNullOrEmpty(prevMsg2) || prevMsg2.Contains("Doc saved") || prevMsg2.Contains("Word doc")
                    ? "✓ Doc saved - " + System.IO.Path.GetFileNameWithoutExtension(filePath)
                    : prevMsg2 + "  ·  ✓ Doc saved";
                System.Diagnostics.Process.Start(path);
            }
            catch (System.Exception ex)
            {
                _statusMsg = "\u2717 Export failed: " + ex.Message;
            }
            Repaint();
        }

        private Color PrinColor(SolidPrinciple p) => p switch
        {
            SolidPrinciple.SRP => C_ACCENT,  SolidPrinciple.OCP => C_YELLOW,
            SolidPrinciple.LSP => C_RED,     SolidPrinciple.ISP => C_PURPLE,
            _ => C_MUTED
        };

        private Color SevColor(Severity s) => s switch
        {
            Severity.High => C_RED, Severity.Medium => C_YELLOW, Severity.Low => C_GREEN, _ => C_MUTED
        };

        // ── Logo loading ─────────────────────────────────────────────────────────
        // Tries to find GD logo PNG anywhere in the package folder.
        // Falls back to a drawn version if no image found.

        private void InitStyles()
        {
            if (_stylesReady) return;
            _sTitle   = new GUIStyle(EditorStyles.boldLabel)
                        { fontSize = 14, normal = { textColor = C_TEXT } };
            _sBody    = new GUIStyle(EditorStyles.label)
                        { fontSize = 12, wordWrap = true, normal = { textColor = C_TEXT } };
            _sMuted   = new GUIStyle(EditorStyles.label)
                        { fontSize = 11, wordWrap = true, normal = { textColor = C_MUTED } };
            // Try to load a monospace font — fallback to EditorStyles safely
            Font monoFont = null;
            try { monoFont = Font.CreateDynamicFontFromOSFont(
                new[]{"JetBrains Mono","Cascadia Code","Consolas","Courier New","Lucida Console"}, 11); }
            catch {}

            _sCode    = new GUIStyle(EditorStyles.label)
                        { fontSize = 11, wordWrap = false, richText = true,
                          normal   = { textColor = SYN_PLAIN } };
            if (monoFont != null) _sCode.font = monoFont;

            _sLineNum = new GUIStyle(EditorStyles.label)
                        { fontSize = 10, richText = false, alignment = TextAnchor.MiddleRight,
                          normal   = { textColor = C_LINENUM } };
            if (monoFont != null) _sLineNum.font = monoFont;

            _sBadge   = new GUIStyle(EditorStyles.miniLabel)
                        { fontSize = 10, fontStyle = FontStyle.Bold,
                          alignment = TextAnchor.MiddleCenter };
            _sSec     = new GUIStyle(EditorStyles.miniLabel)
                        { fontSize = 10, fontStyle = FontStyle.Bold,
                          normal   = { textColor = C_MUTED } };
            _stylesReady = true;
        }
    }

    public enum ReviewDecision { None, Applied, Skipped }
}

