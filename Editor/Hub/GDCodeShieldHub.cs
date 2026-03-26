// Editor/Hub/GDCodeShieldHub.cs  —  GD CodeShield  —  Tools → GD CodeShield
// Central launcher for all GD developer tools.

using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using System.Linq;
using UnityEngine.Networking;

namespace GDCodeShield
{
    public class GDCodeShieldHub : EditorWindow
    {
        // ════════════════════════════════════════════════════════════════════════
        //  PALETTE  (matches gamedistrict.co + logo)
        // ════════════════════════════════════════════════════════════════════════
        private static readonly Color C_BG      = new Color32(42,  42,  42,  255); // logo charcoal #3A3A3A
        private static readonly Color C_SURF    = new Color32(15,  15,  15,  255); // near-black card surface
        private static readonly Color C_SURF2   = new Color32(22,  22,  22,  255);
        private static readonly Color C_BORDER  = new Color32(55,  55,  55,  255);
        private static readonly Color C_ACCENT  = new Color32(255, 211, 0,   255); // #FFD300
        private static readonly Color C_TEXT    = new Color32(255, 255, 255, 255);
        private static readonly Color C_MUTED   = new Color32(155, 155, 155, 255);
        private static readonly Color C_GREEN   = new Color32(80,  200, 100, 255);

        // ════════════════════════════════════════════════════════════════════════
        //  GAME ICON WALLPAPER
        // ════════════════════════════════════════════════════════════════════════
        private static readonly string[] GameIconUrls = new[]
        {
            "https://framerusercontent.com/images/ncJq9t1KKlK4f3bhC1a6YkJ3Nc.png",
            "https://framerusercontent.com/images/HOOT9MvVgJxkuAS5vVkYXiWA.png",
            "https://framerusercontent.com/images/7MXDQOXu94W3dRCjRZn0utA1pNU.png",
            "https://framerusercontent.com/images/irWxw1nbH9SgmJkIi07CvNKGVg.png",
            "https://framerusercontent.com/images/eQrrhoI1E23l204WFQw92ztieA.png",
            "https://framerusercontent.com/images/p5Ck7ZyhUy50uQhuUlSNba47aF0.png",
            "https://framerusercontent.com/images/2W5q0ypQ1z5Kgees5wz1vcYCA.png",
            "https://framerusercontent.com/images/LgygPKhXivBpAEeFZTzw3X5WUM.png",
            "https://framerusercontent.com/images/wPpDAg2I3PdAkof034Ryj9v0M.png",
            "https://framerusercontent.com/images/TPktNAhw1aJ1YLLJq0lLQz23DuM.png",
            "https://framerusercontent.com/images/o5SwEJoSzrOz5shjAt1Tecz3Q.png",
            "https://framerusercontent.com/images/Ol0GKMITPRA1byATfRCzLTP14.png",
            "https://framerusercontent.com/images/37scNa59MXqgBm1VIW5wYFqo8Eg.png",
            "https://framerusercontent.com/images/TERpfiluKDRS4Q0iDIfo0BrA6fk.png",
            "https://framerusercontent.com/images/WXFzyh42Q0bzYEpfcy7e8lk99vU.png",
            "https://framerusercontent.com/images/aT2btDiTphBU27PEJnAoic571qo.png",
        };

        private Texture2D[] _icons       = null;
        private int         _iconsLoaded = 0;
        private bool        _iconsStarted= false;
        private Texture2D   _hatchTex      = null;


        // ════════════════════════════════════════════════════════════════════════
        //  HOVER STATE
        // ════════════════════════════════════════════════════════════════════════
        private int  _hoverCard = -1; // 0 = SOLID, 1 = Checklist
        private float _solidPulse    = 0f;
        private float _checkPulse    = 0f;

        // ════════════════════════════════════════════════════════════════════════
        //  STYLES
        // ════════════════════════════════════════════════════════════════════════
        private GUIStyle _sTitle, _sMuted;
        private bool     _stylesReady;
        private string   _packageVersion = null; // loaded from package.json via PackageManager

        // ════════════════════════════════════════════════════════════════════════
        //  OPEN
        // ════════════════════════════════════════════════════════════════════════
        [MenuItem("Tools/GD CodeShield", false, 0)]
        public static void Open()
        {
            var w = GetWindow<GDCodeShieldHub>("  GD CodeShield");
            w.minSize = new Vector2(640, 420);
            w.maxSize = new Vector2(640, 420); // fixed size — launcher feel
            w.Show();
        }

        private void OnEnable()
        {
            _stylesReady    = false;
            _packageVersion = null;
            _icons          = new Texture2D[GameIconUrls.Length];
            _iconsLoaded    = 0;
            _iconsStarted   = false;
            StartIconDownloads();
            FetchPackageVersion();
        }

        private void OnDisable()
        {
            if (_icons != null)
                foreach (var t in _icons)
                    if (t != null) DestroyImmediate(t);

        }

        // ════════════════════════════════════════════════════════════════════════
        //  PACKAGE VERSION  — read live from PackageManager, never hardcoded
        // ════════════════════════════════════════════════════════════════════════
        private void FetchPackageVersion()
        {
            var listReq = Client.List(offlineMode: true, includeIndirectDependencies: false);
            EditorApplication.update += Poll;
            void Poll()
            {
                if (!listReq.IsCompleted) return;
                EditorApplication.update -= Poll;
                if (listReq.Status == StatusCode.Success)
                    foreach (var pkg in listReq.Result)
                        if (pkg.name == "com.gamedistrict.codeshield")
                        { _packageVersion = pkg.version; Repaint(); break; }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ICON LOADING  (silent, parallel, 8s timeout)
        // ════════════════════════════════════════════════════════════════════════
        private void StartIconDownloads()
        {
            if (_iconsStarted) return;
            _iconsStarted = true;

            var requests = new UnityWebRequest[GameIconUrls.Length];
            double t0    = EditorApplication.timeSinceStartup;

            for (int i = 0; i < GameIconUrls.Length; i++)
            {
                try
                {
                    requests[i] = UnityWebRequestTexture.GetTexture(GameIconUrls[i] + "?width=128&height=128");
                    requests[i].SendWebRequest();
                }
                catch { requests[i] = null; }
            }

            EditorApplication.update += Poll;
            void Poll()
            {
                bool timedOut = EditorApplication.timeSinceStartup - t0 > 8.0;
                for (int i = 0; i < requests.Length; i++)
                {
                    var req = requests[i];
                    if (req == null) continue;
                    if (!req.isDone && !timedOut) continue;
                    if (!timedOut && req.result == UnityWebRequest.Result.Success)
                    {
                        try { _icons[i] = DownloadHandlerTexture.GetContent(req); _iconsLoaded++; Repaint(); }
                        catch { }
                    }
                    try { req.Dispose(); } catch { }
                    requests[i] = null;
                }
                if (requests.All(r => r == null))
                    EditorApplication.update -= Poll;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DRAW
        // ════════════════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            InitStyles();
            var full = new Rect(0, 0, position.width, position.height);

            DrawBackground(full);
            DrawTopBar();
            DrawCards();
            DrawFooter();

            // Animate hover pulses
            if (_hoverCard >= 0) { Repaint(); }
        }

        // ── Background ────────────────────────────────────────────────────────────
        private void DrawBackground(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG);

            // Game icon wallpaper
            if (_iconsLoaded > 0 && _icons != null)
            {
                const float sz   = 110f;
                const float gap  = 14f;
                float step = sz + gap;
                int cols = Mathf.CeilToInt(r.width  / step) + 2;
                int rows = Mathf.CeilToInt(r.height / step) + 2;
                int slot = 0, total = GameIconUrls.Length;

                for (int row = 0; row < rows; row++)
                {
                    float xOff = (row % 2 == 0) ? 0f : step * 0.5f;
                    for (int col = 0; col < cols; col++)
                    {
                        var tex = _icons[slot % total]; slot++;
                        if (tex == null) continue;
                        GUI.color = new Color(1f, 1f, 1f, 0.55f); // more visible on charcoal
                        GUI.DrawTexture(new Rect(r.x + col * step + xOff - step * 0.6f,
                                                 r.y + row * step - step * 0.6f, sz, sz),
                                        tex, ScaleMode.ScaleToFit, true);
                    }
                }
                GUI.color = Color.white;
                // Dark scrim
                EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.38f)); // lighter scrim on charcoal bg
            }

            // Diagonal hatch
            if (_hatchTex == null) _hatchTex = BuildHatchTex();
            if (_hatchTex != null)
            {
                GUI.color = Color.white;
                for (float tx = r.x; tx < r.xMax; tx += _hatchTex.width)
                    for (float ty = r.y; ty < r.yMax; ty += _hatchTex.height)
                        GUI.DrawTexture(new Rect(tx, ty, _hatchTex.width, _hatchTex.height), _hatchTex, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }

            // Yellow radial glow at center
            float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f;
            for (int i = 8; i >= 1; i--)
            {
                float f = (float)i / 8f;
                float alpha = 0.025f * (1f - f);
                EditorGUI.DrawRect(new Rect(cx - r.width * 0.55f * f, cy - r.height * 0.55f * f,
                                            r.width * 1.1f * f, r.height * 1.1f * f),
                                   new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, alpha));
            }
        }

        // ── Top bar ───────────────────────────────────────────────────────────────
        private void DrawTopBar()
        {
            float w = position.width;
            // Yellow top stripe
            EditorGUI.DrawRect(new Rect(0, 0, w, 3), C_ACCENT);
            // Bar bg
            EditorGUI.DrawRect(new Rect(0, 3, w, 44), new Color(C_SURF.r, C_SURF.g, C_SURF.b, 0.92f));
            EditorGUI.DrawRect(new Rect(0, 47, w, 1), new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f));

            // ⚡ bolt
            GUI.Label(new Rect(14, 10, 24, 28), "⚡",
                new GUIStyle(_sTitle) { fontSize = 18, normal = { textColor = C_ACCENT } });
            // Title — GD in accent, CODESHIELD in white
            GUI.Label(new Rect(36, 13, 46, 24), "GD ",
                new GUIStyle(_sTitle) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = C_ACCENT } });
            GUI.Label(new Rect(58, 13, 160, 24), "CODESHIELD",
                new GUIStyle(_sTitle) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = C_TEXT } });

            // Version badge — read from package.json at runtime
            string verLabel = _packageVersion != null ? "  v" + _packageVersion : "";
            if (!string.IsNullOrEmpty(verLabel))
            {
                Rect vb = new Rect(148, 17, 58, 16);
                EditorGUI.DrawRect(vb, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.12f));
                GUI.Label(vb, verLabel, new GUIStyle(_sMuted) { fontSize = 9, normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.8f) } });
            }

            // Right: "GAME DISTRICT"
            GUI.Label(new Rect(0, 14, w - 14, 20), "GAME DISTRICT",
                new GUIStyle(_sMuted) { fontSize = 9, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f) } });
        }

        // ── Cards ─────────────────────────────────────────────────────────────────
        private void DrawCards()
        {
            float bodyY  = 48f;
            float bodyH  = position.height - bodyY - 38f; // leave footer room
            float cx     = position.width * 0.5f;
            float cardW  = 240f;
            float cardH  = 280f;
            float gap    = 24f;

            float leftX  = cx - gap * 0.5f - cardW;
            float rightX = cx + gap * 0.5f;
            float cardY  = bodyY + (bodyH - cardH) * 0.5f;

            // Animate pulse on hover
            _solidPulse  = Mathf.Lerp(_solidPulse,  _hoverCard == 0 ? 1f : 0f, 0.18f);
            _checkPulse  = Mathf.Lerp(_checkPulse,  _hoverCard == 1 ? 1f : 0f, 0.18f);

            DrawToolCard(new Rect(leftX,  cardY, cardW, cardH), 0,
                true, "SOLID REVIEW",
                "Scans C# scripts for\nSRP · OCP · LSP · ISP\nviolations and generates\nAI-powered fixes.",
                new Color32(255, 211, 0, 255),   // yellow accent
                _solidPulse);

            DrawToolCard(new Rect(rightX, cardY, cardW, cardH), 1,
                false, "SDK CHECKLIST",
                "Verifies all SDK keys,\nApp IDs and network\nconfiguration across\nyour project.",
                new Color32(80, 200, 100, 255),  // green accent
                _checkPulse);

            // Track hover
            var e = Event.current;
            Rect solidR = new Rect(leftX,  cardY, cardW, cardH);
            Rect checkR = new Rect(rightX, cardY, cardW, cardH);

            int newHover = -1;
            if (solidR.Contains(e.mousePosition)) newHover = 0;
            if (checkR.Contains(e.mousePosition)) newHover = 1;
            if (newHover != _hoverCard) { _hoverCard = newHover; Repaint(); }

            // Click handling
            if (e.type == EventType.MouseDown)
            {
                if (solidR.Contains(e.mousePosition)) { e.Use(); LaunchSolidReview(); }
                if (checkR.Contains(e.mousePosition)) { e.Use(); LaunchChecklist();   }
            }
        }

        private void DrawToolCard(Rect r, int idx, bool isSolid, string title, string desc,
                                  Color accent, float hoverT)
        {
            // Outer glow on hover — draw first (behind card)
            if (hoverT > 0.01f)
            {
                float expand = hoverT * 6f;
                EditorGUI.DrawRect(new Rect(r.x - expand, r.y - expand,
                                            r.width + expand * 2, r.height + expand * 2),
                                   new Color(accent.r, accent.g, accent.b, hoverT * 0.25f));
            }

            // Accent border frame (always visible, brighter on hover)
            float borderAlpha = Mathf.Lerp(0.6f, 1f, hoverT);
            EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4),
                               new Color(accent.r, accent.g, accent.b, borderAlpha));

            // Card body
            EditorGUI.DrawRect(r, new Color(C_SURF.r, C_SURF.g, C_SURF.b, 0.96f));

            // Top accent band
            float bandH = 5f + hoverT * 3f;
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, bandH), accent);

            // Card icon — drawn directly with IMGUI rects (no texture needed)
            float iconY  = r.y + 24f;
            float iconSz = 68f;
            float iconCx = r.x + r.width * 0.5f;
            float iconCy = iconY + iconSz * 0.5f;

            // Soft glow square behind icon
            float glowSz = iconSz + 20f + hoverT * 10f;
            EditorGUI.DrawRect(new Rect(iconCx - glowSz*0.5f, iconCy - glowSz*0.5f, glowSz, glowSz),
                               new Color(accent.r, accent.g, accent.b, 0.10f + hoverT * 0.08f));

            if (isSolid) DrawSolidIcon(iconCx, iconCy, iconSz, accent);
            else          DrawChecklistIcon(iconCx, iconCy, iconSz, accent);

            // Title
            float titleY = iconY + 64f;
            GUI.Label(new Rect(r.x + 12, titleY, r.width - 24, 26f), title,
                new GUIStyle(_sTitle)
                {
                    fontSize  = 15, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = Color.white }
                });

            // Accent underline
            float ulY = titleY + 28f;
            float ulW = Mathf.Lerp(40f, r.width - 48f, hoverT);
            EditorGUI.DrawRect(new Rect(r.x + (r.width - ulW) * 0.5f, ulY, ulW, 2f), accent);

            // Description
            GUI.Label(new Rect(r.x + 16, ulY + 10f, r.width - 32, 80f), desc,
                new GUIStyle(_sMuted)
                {
                    fontSize  = 10, alignment = TextAnchor.UpperCenter,
                    wordWrap  = true,
                    normal    = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b,
                                                        Mathf.Lerp(0.7f, 1f, hoverT)) }
                });

            // Bottom CTA button
            float btnY  = r.y + r.height - 48f;
            float btnW  = r.width - 32f;
            Rect  btnR  = new Rect(r.x + 16, btnY, btnW, 34f);

            // Button fill: accent on hover, outline-only at rest
            if (hoverT > 0.05f)
                EditorGUI.DrawRect(btnR, new Color(accent.r, accent.g, accent.b, hoverT));
            else
            {
                // outline button
                EditorGUI.DrawRect(btnR, new Color(accent.r, accent.g, accent.b, 0.08f));
                EditorGUI.DrawRect(new Rect(btnR.x,                  btnR.y,                  btnR.width, 1), new Color(accent.r, accent.g, accent.b, 0.5f));
                EditorGUI.DrawRect(new Rect(btnR.x,                  btnR.yMax - 1,            btnR.width, 1), new Color(accent.r, accent.g, accent.b, 0.5f));
                EditorGUI.DrawRect(new Rect(btnR.x,                  btnR.y,                  1, btnR.height), new Color(accent.r, accent.g, accent.b, 0.5f));
                EditorGUI.DrawRect(new Rect(btnR.xMax - 1,           btnR.y,                  1, btnR.height), new Color(accent.r, accent.g, accent.b, 0.5f));
            }

            Color labelCol = hoverT > 0.4f
                ? new Color32(8, 8, 8, 255)   // dark text on filled btn
                : new Color(accent.r, accent.g, accent.b, 1f);

            GUI.Label(btnR, "▶  OPEN TOOL",
                new GUIStyle(_sTitle)
                {
                    fontSize  = 11, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = labelCol }
                });
        }

        // ── Footer ────────────────────────────────────────────────────────────────
        private void DrawFooter()
        {
            float w  = position.width;
            float fy = position.height - 34f;
            EditorGUI.DrawRect(new Rect(0, fy, w, 1), new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.2f));
            EditorGUI.DrawRect(new Rect(0, fy + 1, w, 33), new Color(C_SURF.r, C_SURF.g, C_SURF.b, 0.88f));

            GUI.Label(new Rect(14, fy + 7, 300, 18),
                "ESTD. 2016  ·  STAY HUNGRY  ·  STAY FOOLISH",
                new GUIStyle(_sMuted) { fontSize = 8,
                    normal = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.45f) } });

            GUI.Label(new Rect(0, fy + 7, w - 14, 18), "⚡ GAME DISTRICT",
                new GUIStyle(_sTitle) { fontSize = 10, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    normal    = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.65f) } });
        }

        // ════════════════════════════════════════════════════════════════════════
        //  LAUNCHERS
        // ════════════════════════════════════════════════════════════════════════
        private static void LaunchSolidReview()
        {
            SolidAgent.SolidAgentWindow.Open();
        }

        private static void LaunchChecklist()
        {
            GDChecklist.ChecklistWindow.Open();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════════
        private void InitStyles()
        {
            if (_stylesReady) return;
            _sTitle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold, normal = { textColor = C_TEXT } };
            _sMuted = new GUIStyle(EditorStyles.label) { normal = { textColor = C_MUTED } };
            _stylesReady = true;
        }

        // ── SOLID Review icon: bold "S" letterform inside a shield outline ────────
        private void DrawSolidIcon(float cx, float cy, float sz, Color accent)
        {
            float u = sz / 68f; // scale unit

            // ── Shield outline (5 segments: top-left, top-right, sides, point) ──
            float sw  = sz * 0.82f;          // shield width
            float sh  = sz * 0.90f;          // shield height
            float sx  = cx - sw * 0.5f;
            float sy  = cy - sh * 0.5f;
            float t   = 3f * u;              // stroke thickness
            Color ac  = accent;

            // Top bar
            EditorGUI.DrawRect(new Rect(sx,           sy,            sw,   t),   ac);
            // Left side (2/3 height then angled — faked with 2 rects)
            EditorGUI.DrawRect(new Rect(sx,            sy,            t,    sh * 0.65f), ac);
            // Right side
            EditorGUI.DrawRect(new Rect(sx + sw - t,  sy,            t,    sh * 0.65f), ac);
            // Bottom-left diagonal (angled approximation — 3 stacked rects)
            for (int i = 0; i < 8; i++)
            {
                float prog = i / 7f;
                float rx   = sx       + prog * (sw * 0.5f - t);
                float ry   = sy + sh * 0.65f + prog * (sh * 0.35f - t);
                EditorGUI.DrawRect(new Rect(rx, ry, t, t * 1.6f), ac);
            }
            // Bottom-right diagonal
            for (int i = 0; i < 8; i++)
            {
                float prog = i / 7f;
                float rx   = sx + sw - t - prog * (sw * 0.5f - t);
                float ry   = sy + sh * 0.65f + prog * (sh * 0.35f - t);
                EditorGUI.DrawRect(new Rect(rx, ry, t, t * 1.6f), ac);
            }

            // ── Bold "S" letterform centred inside shield ─────────────────────
            float lx  = cx - sz * 0.18f;   // letter left edge
            float lw  = sz * 0.36f;        // letter width
            float lt  = 3.5f * u;          // letter stroke
            float lsy = cy - sz * 0.22f;   // letter top y

            // Top horizontal bar of S
            EditorGUI.DrawRect(new Rect(lx,        lsy,              lw,      lt), ac);
            // Middle horizontal bar
            EditorGUI.DrawRect(new Rect(lx,        lsy + lw * 0.45f, lw,      lt), ac);
            // Bottom horizontal bar
            EditorGUI.DrawRect(new Rect(lx,        lsy + lw * 0.90f, lw,      lt), ac);
            // Top-left vertical (top half of S)
            EditorGUI.DrawRect(new Rect(lx,        lsy,              lt,      lw * 0.47f), ac);
            // Bottom-right vertical (bottom half of S)
            EditorGUI.DrawRect(new Rect(lx + lw - lt, lsy + lw * 0.45f, lt,  lw * 0.47f), ac);
        }

        // ── SDK Checklist icon: 3-row checklist with tick marks ───────────────
        private void DrawChecklistIcon(float cx, float cy, float sz, Color accent)
        {
            float u   = sz / 68f;
            float t   = 2.5f * u;    // line thickness
            float rowH = sz * 0.22f; // spacing between rows
            float lw   = sz * 0.52f; // line width
            float bsz  = sz * 0.13f; // checkbox size
            Color ac  = accent;
            Color dim = new Color(accent.r, accent.g, accent.b, 0.35f);

            float startY = cy - rowH;

            for (int row = 0; row < 3; row++)
            {
                float ry = startY + row * rowH;
                float bx = cx - lw * 0.5f;

                // Checkbox square outline
                EditorGUI.DrawRect(new Rect(bx,          ry,           bsz, t),   ac); // top
                EditorGUI.DrawRect(new Rect(bx,          ry,           t,   bsz), ac); // left
                EditorGUI.DrawRect(new Rect(bx,          ry + bsz - t, bsz, t),   ac); // bottom
                EditorGUI.DrawRect(new Rect(bx + bsz - t,ry,           t,   bsz), ac); // right

                // Tick inside checkbox (all rows checked)
                float tx = bx + bsz * 0.15f;
                float ty = ry + bsz * 0.42f;
                // Short left arm of tick (down-right)
                EditorGUI.DrawRect(new Rect(tx,              ty,              t * 0.9f, t * 0.9f), ac);
                EditorGUI.DrawRect(new Rect(tx + t,          ty + t * 0.8f,   t * 0.9f, t * 0.9f), ac);
                // Long right arm (up-right)
                EditorGUI.DrawRect(new Rect(tx + t * 2f,     ty,              t * 0.9f, t * 0.9f), ac);
                EditorGUI.DrawRect(new Rect(tx + t * 3f,     ty - t * 0.8f,   t * 0.9f, t * 0.9f), ac);
                EditorGUI.DrawRect(new Rect(tx + t * 4f,     ty - t * 1.6f,   t * 0.9f, t * 0.9f), ac);

                // Horizontal text line next to checkbox
                float lineX = bx + bsz + sz * 0.06f;
                float lineW = lw - bsz - sz * 0.06f;
                // Full line
                EditorGUI.DrawRect(new Rect(lineX, ry + bsz * 0.3f, lineW,       t), ac);
                // Short second line (looks like 2 lines of text)
                EditorGUI.DrawRect(new Rect(lineX, ry + bsz * 0.6f, lineW * 0.65f, t), dim);
            }
        }

        private Texture2D BuildHatchTex()
        {
            const int S = 16;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode   = TextureWrapMode.Repeat;
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
            var lc  = new Color32(255, 211, 0, 28);
            var lc2 = new Color32(255, 211, 0, 12);
            for (int i = 0; i < S; i++)
            {
                int x = i, y = S - 1 - i;
                px[y * S + x] = lc;
                if (y + 1 < S) px[(y + 1) * S + x] = lc2;
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
