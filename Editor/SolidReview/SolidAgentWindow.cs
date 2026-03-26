// Editor/SolidAgentWindow.cs  —  SOLID Review  —  Tools → SOLID Review

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
        private Dictionary<string, GeneratedFix>   _fixes     = new();
        private Dictionary<string, ReviewDecision> _decisions = new();
        private SolidReport                        _report    = null;

        private string _activeId          = null; // "FilePath||ViolationId"
        private int    _activeTab         = 0;
        private bool   _isFixing          = false;
        private string _statusMsg         = "";
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
        private string _scanRoot  = "";   // empty = whole Assets folder
        private bool   _showSettings = false;

        private string ApiKey
        {
            get { if (_apiKey == null) _apiKey = EditorPrefs.GetString("SolidAgent_ApiKey", ""); return _apiKey; }
            set { _apiKey = value ?? ""; EditorPrefs.SetString("SolidAgent_ApiKey", _apiKey); }
        }

        // ── Scroll ───────────────────────────────────────────────────────────────
        private Vector2 _sidebarScroll;
        private Vector2 _mainScroll;

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
            _scanRoot    = EditorPrefs.GetString("SolidAgent_ScanRoot", "");
        }

        private void OnDisable()
        {
            EditorPrefs.SetString("SolidAgent_ScanRoot", _scanRoot ?? "");
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
            bool hasFolder = !string.IsNullOrEmpty(_scanRoot);

            // Card height grows if no API key
            float cw = 460f, ch = hasKey ? 330f : 430f;
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

            // Folder display box
            string displayPath = hasFolder
                ? _scanRoot.Replace(Application.dataPath, "Assets")
                : "All Assets  (entire project)";

            Bg(new Rect(card.x + 20, iy, cw - 40, 30), C_SURF2);
            Outline(new Rect(card.x + 20, iy, cw - 40, 30),
                hasFolder ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.6f) : C_BORDER);
            if (hasFolder) Bg(new Rect(card.x + 20, iy, 3, 30), C_ACCENT);

            GUI.Label(new Rect(card.x + 30, iy + 7, cw - 130, 16), displayPath,
                new GUIStyle(_sMuted) { fontSize = 10,
                    normal = { textColor = hasFolder ? C_ACCENT : C_MUTED } });

            // Select Folder button
            if (Btn(new Rect(card.x + cw - 118, iy + 4, 96, 22), "Select Folder", C_ACCENT))
            {
                string picked = EditorUtility.OpenFolderPanel("Select folder to scan", Application.dataPath, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    if (picked.StartsWith(Application.dataPath))
                        _scanRoot = picked;
                    else
                        _statusMsg = "⚠  Folder must be inside Assets.";
                }
            }
            iy += 30;

            // Reset link — only show when folder is selected
            if (hasFolder)
            {
                if (Btn(new Rect(card.x + 20, iy, 130, 18), "✕  Reset to All Assets", C_MUTED))
                    _scanRoot = "";
            }
            iy += hasFolder ? 26 : 14;

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
                if (Btn(new Rect(r.x + 10, ry, r.width - 20, 32), "⬇  Download PDF Report", C_ACCENT))
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
                        _activeId = key; _activeTab = 0; _lastRegression = null;
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
            float hh = 58f;
            Bg(new Rect(r.x, r.y, r.width, hh), C_SURF);
            HRule(new Rect(r.x, r.y + hh - 1, r.width, 1), C_BORDER);

            // Principle + severity badges
            DrawPill(new Rect(r.x + 16, r.y + 10, 38, 18), v.Principle.ToString(), PrinColor(v.Principle));
            DrawPill(new Rect(r.x + 60, r.y + 10, 56, 18), v.Severity.ToString(), SevColor(v.Severity));

            // Title
            GUI.Label(new Rect(r.x + 16, r.y + 30, r.width - 200, 20), v.Title, _sBody);

            // File + line
            GUI.Label(new Rect(r.x + 16 + (r.width - 200), r.y + 34, 180, 16),
                v.Location.FileName + "  :" + v.Location.StartLine,
                new GUIStyle(_sMuted) { alignment = TextAnchor.MiddleRight, fontSize = 10 });

            // ── Processing overlay — shown prominently when fixing ────────────────
            if (_isFixing)
            {
                float oy = r.y + hh + 34;
                float oh = r.height - hh - 34 - 54;
                // Dim background
                Bg(new Rect(r.x, oy, r.width, oh), new Color(0, 0, 0, 0.55f));

                // Centered status card
                float cw2 = 360f, ch2 = 90f;
                float ccx = r.x + r.width  / 2f - cw2 / 2f;
                float ccy = oy  + oh / 2f - ch2 / 2f;
                Bg(new Rect(ccx, ccy, cw2, ch2), C_SURF);
                Outline(new Rect(ccx, ccy, cw2, ch2), C_ACCENT);

                // Animated dots
                int dots = (int)(EditorApplication.timeSinceStartup * 2) % 4;
                string ellipsis = new string('.', dots);

                GUI.Label(new Rect(ccx, ccy + 18, cw2, 24), _statusMsg,
                    new GUIStyle(_sBody)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize  = 13,
                        fontStyle = FontStyle.Bold,
                        normal    = { textColor = C_TEXT }
                    });
                GUI.Label(new Rect(ccx, ccy + 50, cw2, 18),
                    "Please wait" + ellipsis,
                    new GUIStyle(_sMuted) { alignment = TextAnchor.MiddleCenter });

                Repaint(); // keep animating dots
            }

            // Subtle status line in header (always visible)
            if (!string.IsNullOrEmpty(_statusMsg) && !_isFixing)
                GUI.Label(new Rect(r.x + 124, r.y + 10, r.width - 140, 18),
                    _statusMsg, new GUIStyle(_sMuted)
                    {
                        fontSize = 10,
                        normal   = { textColor = _statusMsg.StartsWith("✓") ? C_GREEN :
                                                  _statusMsg.StartsWith("✗") ? C_RED : C_MUTED }
                    });

            // ── Tabs ────────────────────────────────────────────────────────────
            string[] tabs = { "Violation", "Proposed Fix", "Regression" };
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
                    if (Click(tr)) { _activeTab = i; Repaint(); }
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
                new Rect(0, 0, r.width - 16, 2400));

            float py = 16; float pw = r.width - 48; float px = 24;

            if (_activeTab == 0) DrawViolationTab(v, px, ref py, pw);
            if (_activeTab == 1) DrawFixTab(v, px, ref py, pw);
            if (_activeTab == 2) DrawRegressionTab(px, ref py, pw);

            GUI.EndScrollView();

            // ── Action bar ───────────────────────────────────────────────────────
            DrawActionBar(new Rect(r.x, r.y + r.height - 54, r.width, 54), v);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TAB: VIOLATION
        // ════════════════════════════════════════════════════════════════════════

        private void DrawViolationTab(Violation v, float x, ref float y, float w)
        {
            // Description card
            SecLabel(x, ref y, "Description");
            DrawCard(x, ref y, w, v.Description, _sBody);

            // Evidence chip
            SecLabel(x, ref y, "Evidence");
            float ew = w, eh = 30;
            Bg(new Rect(x, y, ew, eh), C_SURF2);
            Outline(new Rect(x, y, ew, eh), C_BORDER);
            // Left accent strip
            Bg(new Rect(x, y, 3, eh), C_YELLOW);
            GUI.Label(new Rect(x + 14, y + 7, ew - 20, 16), v.Evidence,
                new GUIStyle(_sBody)
                {
                    fontSize = 11,
                    normal   = { textColor = new Color(C_YELLOW.r * 1.3f, C_YELLOW.g * 1.2f, C_YELLOW.b, 1f) }
                });
            y += eh + 14;

            // Code snippet
            SecLabel(x, ref y, $"Affected Code  —  line {v.Location.StartLine}");
            DrawCodeBlock(v.OriginalCode, x, ref y, w);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TAB: FIX
        // ════════════════════════════════════════════════════════════════════════

        private void DrawFixTab(Violation v, float x, ref float y, float w)
        {
            string key    = MakeKey(v);
            bool   hasKey = !string.IsNullOrEmpty(ApiKey);

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
                GUI.Label(new Rect(x, y, w, 20),
                    "Claude API will analyse this violation and generate a fix.", _sMuted);
                y += 30;

                GUI.enabled = !_isFixing;
                if (Btn(new Rect(x, y, 152, 36), "⚡  Generate Fix", C_ACCENT))
                {
                    _pendingFixKey  = key;
                    _showCostPrompt = true;
                }
                GUI.enabled = true;
                y += 46;

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
            SecLabel(x, ref y, "What Changes");
            DrawCard(x, ref y, w, fix.DiffSummary, _sBody);

            // Explanation
            SecLabel(x, ref y, "Why This Is Correct");
            var mutedBody = new GUIStyle(_sBody) { normal = { textColor = C_MUTED } };
            DrawCard(x, ref y, w, fix.Explanation, mutedBody);

            // Fixed code
            SecLabel(x, ref y, "Fixed Code");
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

                // If fix splits into multiple files, note it
                if (fix.NewFilesNeeded != null && fix.NewFilesNeeded.Count > 0)
                {
                    y += 4;
                    GUI.Label(new Rect(x, y, w, 16),
                        $"ℹ  Fix will create {fix.NewFilesNeeded.Count} new file(s): " +
                        string.Join(", ", fix.NewFilesNeeded),
                        new GUIStyle(_sMuted) { fontSize = 10,
                            normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.8f) } });
                    y += 20;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TAB: REGRESSION
        // ════════════════════════════════════════════════════════════════════════

        private void DrawRegressionTab(float x, ref float y, float w)
        {
            if (_lastRegression == null)
            {
                GUI.Label(new Rect(x, y, w, 32),
                    "Apply the fix to run regression tests. Results appear here.", _sMuted);
                return;
            }

            SecLabel(x, ref y, "Test Results — Before vs After");

            foreach (var t in _lastRegression.Tests)
            {
                Color bg  = t.Passed ? new Color(C_GREEN.r, C_GREEN.g, C_GREEN.b, 0.06f)
                                     : new Color(C_RED.r,   C_RED.g,   C_RED.b,   0.08f);
                Color col = t.Passed ? C_GREEN : C_RED;

                Bg(new Rect(x, y, w, 32), bg);
                Outline(new Rect(x, y, w, 32), C_BORDER);
                Bg(new Rect(x, y, 3, 32), col);

                GUI.Label(new Rect(x + 12, y + 8, 16, 16), t.Passed ? "✓" : "✗",
                    new GUIStyle(_sBody) { normal = { textColor = col } });
                GUI.Label(new Rect(x + 32, y + 9, w * .5f, 14), t.Description, _sBody);
                GUI.Label(new Rect(x + w * .55f, y + 9, w * .42f, 14),
                    t.ExpectedOutput + " → " + t.ActualOutput, _sMuted);
                y += 34;
            }

            y += 10;
            Color sc = _lastRegression.AllPassed ? C_GREEN : C_RED;
            string sm = _lastRegression.AllPassed
                ? $"✓  All {_lastRegression.PassCount} test(s) passed"
                : $"✗  {_lastRegression.FailCount} test(s) failed — fix reverted";
            GUI.Label(new Rect(x, y, w, 20), sm,
                new GUIStyle(_sBody) { normal = { textColor = sc }, fontStyle = FontStyle.Bold });
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
            if (Btn(new Rect(r.x + 134, r.y + 11, 66, 32), "Skip", C_SURF2))
                SkipViolation(key);

            GUI.enabled = hasFix;
            if (Btn(new Rect(r.x + 208, r.y + 11, 110, 32), "View Full Code", C_SURF2))
                _activeTab = 1;

            // Per-file PDF export
            GUI.enabled = true;
            if (Btn(new Rect(r.x + 326, r.y + 11, 110, 32), "⬇  File PDF", C_ACCENT))
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
            GUI.Label(new Rect(px, py, pw, 16), "Scan Folder", _sBody); py += 18;

            // Current folder display
            bool hasFolder    = !string.IsNullOrEmpty(_scanRoot);
            string folderDisplay = hasFolder
                ? _scanRoot.Replace(Application.dataPath, "Assets")
                : "Entire Assets folder  (default)";
            Color folderCol = hasFolder ? C_ACCENT : C_MUTED;

            // Folder box
            Bg(new Rect(px, py, pw, 28), C_SURF2);
            Outline(new Rect(px, py, pw, 28), hasFolder ? C_ACCENT : C_BORDER);
            if (hasFolder) Bg(new Rect(px, py, 3, 28), C_ACCENT);
            GUI.Label(new Rect(px + 10, py + 6, pw - 120, 16), folderDisplay,
                new GUIStyle(_sMuted) { fontSize = 10, normal = { textColor = folderCol } });
            py += 36;

            // Buttons row
            if (Btn(new Rect(px, py, 148, 28), "Select Folder", C_ACCENT))
            {
                string picked = EditorUtility.OpenFolderPanel(
                    "Select folder to scan", Application.dataPath, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    // Must be inside Assets
                    if (picked.StartsWith(Application.dataPath))
                        _scanRoot = picked;
                    else
                        _statusMsg = "⚠  Folder must be inside your Assets folder.";
                }
            }

            if (hasFolder && Btn(new Rect(px + 156, py, 130, 28), "Reset to All Assets", C_SURF2))
                _scanRoot = "";

            py += 42;

            GUI.Label(new Rect(px, py, pw, 16),
                "API key stored in EditorPrefs only — never committed to version control.", _sMuted);
            py += 32;

            if (Btn(new Rect(px, py, 116, 32), "Save & Close", C_ACCENT))
            { EditorPrefs.SetString("SolidAgent_ScanRoot", _scanRoot ?? ""); _showSettings = false; }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SYNTAX-HIGHLIGHTED CODE BLOCK
        // ════════════════════════════════════════════════════════════════════════

        private void DrawCodeBlock(string code, float x, ref float y, float w)
        {
            if (string.IsNullOrEmpty(code)) return;

            var   lines   = code.Split('\n');
            float lineH   = 18f;
            float padV    = 10f;
            float gutterW = 40f;
            float totalH  = lines.Length * lineH + padV * 2;

            // Dark container background
            Bg(new Rect(x, y, w, totalH), new Color32(13, 17, 23, 255));
            Outline(new Rect(x, y, w, totalH), C_BORDER);

            // Gutter background + separator
            Bg(new Rect(x, y, gutterW, totalH), new Color32(20, 25, 32, 255));
            Bg(new Rect(x + gutterW, y, 1, totalH), C_BORDER);

            for (int i = 0; i < lines.Length; i++)
            {
                float ly  = y + padV + i * lineH;
                string raw = lines[i].TrimEnd('\r');

                // Subtle alternating row
                if (i % 2 == 0)
                    Bg(new Rect(x + gutterW + 1, ly, w - gutterW - 1, lineH),
                       new Color(1, 1, 1, 0.015f));

                // Line number
                GUI.Label(new Rect(x + 2, ly, gutterW - 5, lineH),
                    (i + 1).ToString(), _sLineNum);

                // Syntax-highlighted line using richText
                string highlighted = BuildRichTextLine(raw);
                GUI.Label(new Rect(x + gutterW + 6, ly, w - gutterW - 10, lineH),
                    highlighted, _sCode);
            }

            y += totalH + 14;
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
                string root = !string.IsNullOrEmpty(_scanRoot) && Directory.Exists(_scanRoot)
                    ? _scanRoot
                    : Application.dataPath;
                foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string n    = f.Replace('\\', '/');
                    string name = Path.GetFileName(f);

                    // Skip by folder
                    if (n.Contains("/Plugins/"))          continue;
                    if (n.Contains("/PackageCache/"))      continue;
                    if (n.Contains("/TextMesh Pro/"))      continue;
                    if (n.Contains("/Editor/"))            continue;
                    if (n.Contains("/ThirdParty/"))        continue;
                    if (n.Contains("/Packages/"))          continue;
                    if (n.Contains("/SDK/"))               continue;
                    if (n.Contains("/Sdk/"))               continue;
                    if (n.Contains(".Generated.cs"))       continue;
                    if (n.Contains("AssemblyInfo.cs"))     continue;

                    // Skip known SDK file prefixes
                    string nameLower = name.ToLower();
                    if (nameLower.StartsWith("adjust"))        continue;
                    if (nameLower.StartsWith("appmetrica"))    continue;
                    if (nameLower.StartsWith("maxsdk"))        continue;
                    if (nameLower.StartsWith("firebase"))      continue;
                    if (nameLower.StartsWith("googlemobile"))  continue;
                    if (nameLower.StartsWith("ironsource"))    continue;
                    if (nameLower.StartsWith("appsflyer"))     continue;
                    if (nameLower.StartsWith("gameanalytics")) continue;
                    if (nameLower.StartsWith("chartboost"))    continue;
                    if (nameLower.StartsWith("vungle"))        continue;
                    if (nameLower.StartsWith("unityads"))      continue;
                    if (nameLower.StartsWith("metica"))        continue;
                    if (nameLower.StartsWith("ireporter"))     continue;
                    if (nameLower.StartsWith("applovin"))      continue;
                    if (nameLower.StartsWith("admob"))         continue;

                    // Skip files over 200KB — likely generated
                    try { if (new FileInfo(f).Length > 200 * 1024) continue; } catch { continue; }

                    files.Add(f);
                }
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

            // Step 1
            _statusMsg = "📡  Connecting to Claude API…"; Repaint();
            await Task.Delay(200);

            // Step 2
            _statusMsg = "📂  Reading source file…"; Repaint();
            var v      = FindViolation(key);
            string src = File.ReadAllText(v.Location.FilePath);

            // Step 3
            _statusMsg = "🔍  Analysing violation…"; Repaint();
            await Task.Delay(100);

            // Step 4
            _statusMsg = "⚙️  Generating fix with Claude…"; Repaint();

            GeneratedFix fix = null;
            string error = null;
            try
            {
                fix = await new AIFixGenerator(ApiKey).GenerateFixAsync(v, src);
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
            }

            if (error != null)
            {
                _statusMsg = $"✗  Error: {error}";
                _isFixing  = false; Repaint();
                return;
            }

            // Step 5
            _statusMsg = "🔬  Checking behavioral contract…"; Repaint();
            await Task.Delay(200);

            // Contract check — compare public API including any new files the fix creates
            string newFilesContent = "";
            if (fix.NewFilesNeeded != null && fix.NewFilesNeeded.Count > 0)
                newFilesContent = string.Join("\n", fix.NewFilesNeeded
                    .Where(f => !string.IsNullOrEmpty(f)));

            var contractCheck = await Task.Run(() =>
                ContractChecker.Check(src, fix.FixedCode ?? "", newFilesContent));
            _contractChecks[key] = contractCheck;

            _statusMsg = contractCheck.Passed
                ? "✓  Fix generated — contract check passed"
                : "⚠  Fix generated — review contract warnings";
            _fixes[key]  = fix;
            _isFixing    = false;
            _activeTab   = 1;
            Repaint();
        }

        private async void ApplyFixAsync(string key)
        {
            if (!_fixes.TryGetValue(key, out var fix)) return;
            var v = FindViolation(key);
            _isFixing = true; _statusMsg = "Running regression…"; Repaint();

            RegressionReport report = null;
            await Task.Run(() => {
                var h = new RegressionHarness(Application.dataPath);
                h.CaptureBaselineAsync(v.Location.FilePath).Wait();
                h.ApplyFix(fix, v.Location.FilePath);
                report = h.CompareAsync(v.Location.FilePath).Result;
                if (!report.AllPassed) h.RevertFix(v.Location.FilePath);
            });

            _lastRegression = report; _activeTab = 2;
            if (report.AllPassed)
            { _decisions[key] = ReviewDecision.Applied; AssetDatabase.Refresh(); _statusMsg = "✓ Applied."; }
            else
            { _statusMsg = "✗ Regression failed — reverted."; }
            _isFixing = false; Repaint();
        }

        private void SkipViolation(string key)
        { _decisions[key] = ReviewDecision.Skipped; Repaint(); }

        private void ResetToHome()
        {
            _screen = Screen.Home; _results.Clear(); _fixes.Clear();
            _decisions.Clear(); _activeId = null; _statusMsg = "";
            _report = null; _contractChecks.Clear(); Repaint();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════════

        private string MakeKey(Violation v)           => v.Location.FilePath + "||" + v.Id;

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
                _statusMsg = "✓ PDF saved to SolidReports/";
                // Open the PDF file directly
                System.Diagnostics.Process.Start(path);
            }
            catch (System.Exception ex)
            {
                _statusMsg = $"✗ PDF export failed: {ex.Message}";
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
                _statusMsg = "\u2713 PDF saved — " + System.IO.Path.GetFileNameWithoutExtension(filePath);
                System.Diagnostics.Process.Start(path);
            }
            catch (System.Exception ex)
            {
                _statusMsg = "\u2717 PDF failed: " + ex.Message;
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
