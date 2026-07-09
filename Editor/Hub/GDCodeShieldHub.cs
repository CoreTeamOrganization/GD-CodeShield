// Editor/Hub/GDCodeShieldHub.cs
// GD CodeShield — editorial launcher (v1.3.0 redesign)
// Spec: cream page, 6px gold left-bar, navy text, Fraunces + Inter, eyebrows + tool entries.
//
// Behaviour preserved from prior version:
//   - Tools → GD CodeShield menu item
//   - Opens SOLID Review window (SolidAgentWindow.Open)
//   - Opens SDK Checklist window (ChecklistWindow.Open)
//   - Opens Contact Support window (ContactSupportWindow.Open)
//   - Live package version read from PackageManager

using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using GDCodeShield.Brand;

namespace GDCodeShield
{
    public class GDCodeShieldHub : EditorWindow
    {
        // ─── State ─────────────────────────────────────────────────────────────
        private string _packageVersion = null;
        private int    _hover = -1;   // 0=SOLID, 1=Checklist, 2=Contact

        // Cached styles
        private GUIStyle _sBrand, _sCrumbs, _sH1, _sLede, _sEyebrow, _sBody, _sMuted,
                         _sStatNum, _sStatLabel, _sFootnote,
                         _sToolNum, _sToolTitle, _sToolLede, _sToolMeta, _sToolOpen;
        private bool _stylesReady;

        // ─── Open ──────────────────────────────────────────────────────────────
        [MenuItem("Tools/GD CodeShield", false, 0)]
        public static void Open()
        {
            var w = GetWindow<GDCodeShieldHub>("  GD CodeShield");
            w.minSize = new Vector2(900, 600);
            w.maxSize = new Vector2(900, 600); // fixed launcher feel
            w.Show();
        }

        private void OnEnable()
        {
            _stylesReady = false;
            _packageVersion = null;
            FetchPackageVersion();
            UpdateChecker.CheckAsync(); // daily GitHub releases check — silent, throttled
        }

        // ─── Live version read ─────────────────────────────────────────────────
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

        // ─── Style setup ───────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            var fraunces = BrandTokens.Fraunces;
            var inter    = BrandTokens.Inter;

            _sBrand     = BrandTokens.MakeStyle(fraunces, 16, BrandTokens.Navy, FontStyle.Bold);
            _sCrumbs    = BrandTokens.MakeStyle(inter,    11, BrandTokens.WarmGray);
            _sH1        = BrandTokens.MakeStyle(fraunces, BrandTokens.SizeH1, BrandTokens.Navy, FontStyle.Bold);
            _sLede      = BrandTokens.MakeWrappedStyle(BrandTokens.FrauncesItalic ?? fraunces, BrandTokens.SizeLede, BrandTokens.Ink, FontStyle.Italic);
            _sEyebrow   = BrandTokens.MakeStyle(inter, BrandTokens.SizeEyebrow, BrandTokens.Navy, FontStyle.Bold);
            _sBody      = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Ink);
            _sMuted     = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.WarmGray);
            _sStatNum   = BrandTokens.MakeStyle(fraunces, BrandTokens.SizeStatNum, BrandTokens.Navy, FontStyle.Bold);
            _sStatLabel = BrandTokens.MakeStyle(inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray);
            _sFootnote  = BrandTokens.MakeStyle(inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray);
            _sToolNum   = BrandTokens.MakeStyle(fraunces, 30, BrandTokens.Gold, FontStyle.Bold);
            _sToolTitle = BrandTokens.MakeStyle(fraunces, 22, BrandTokens.Navy, FontStyle.Bold);
            _sToolLede  = BrandTokens.MakeStyle(BrandTokens.FrauncesItalic ?? fraunces, 14, BrandTokens.WarmGray, FontStyle.Italic);
            _sToolMeta  = BrandTokens.MakeStyle(inter, 10, BrandTokens.WarmGray);
            _sToolOpen  = BrandTokens.MakeStyle(inter, 12, BrandTokens.Navy, FontStyle.Bold);
        }

        // ─── Repaint while hover changes ───────────────────────────────────────
        private void OnInspectorUpdate() => Repaint();

        // ═══════════════════════════════════════════════════════════════════════
        //  PAINT
        // ═══════════════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            // Immediate repaint on scroll/move so scrolling and hover track the mouse
            wantsMouseMove = true;
            var evtType = Event.current.type;
            if (evtType == EventType.ScrollWheel || evtType == EventType.MouseMove || evtType == EventType.MouseDrag)
                Repaint();

            EnsureStyles();
            var W = position.width;
            var H = position.height;

            // Cream page
            BrandTokens.Fill(new Rect(0, 0, W, H), BrandTokens.Cream);

            // 6px gold left-bar
            BrandTokens.Fill(new Rect(0, 0, BrandTokens.GoldBarWidth, H), BrandTokens.Gold);

            // Content padding starts after gold bar
            float padL = BrandTokens.GoldBarWidth + 30;
            float padR = 36;
            float contentW = W - padL - padR;

            // ── Topbar ─────────────────────────────────────────────────────────
            float ty = 18;
            DrawBrandMark(padL, ty);
            GUI.Label(new Rect(padL + 32, ty + 4, 260, 22), "CodeShield", _sBrand);

            // Right-aligned crumbs
            string crumbs = "Game District  ·  Workstation";
            var crumbsSize = _sCrumbs.CalcSize(new GUIContent(crumbs));
            GUI.Label(new Rect(W - padR - crumbsSize.x, ty + 7, crumbsSize.x, 18), crumbs, _sCrumbs);

            // Topbar hairline below
            float afterTopbar = ty + 18 + 22;
            BrandTokens.HairlineH(padL, afterTopbar, contentW, BrandTokens.Taupe);

            // ── Body ───────────────────────────────────────────────────────────
            float bodyY = afterTopbar + 50;

            // Two-column split: 40% / 60%
            float leftW  = contentW * 0.42f;
            float gapBtw = 50f;
            float rightX = padL + leftW + gapBtw;
            float rightW = contentW - leftW - gapBtw;

            DrawLeftRail(padL, bodyY, leftW);
            DrawRightColumn(rightX, bodyY, rightW);

            // ── Update banner — shown only when GitHub has a newer release tag ──
            float footerY = H - 42;
            if (UpdateChecker.UpdateAvailable(out string latestVer))
            {
                var banner = new Rect(padL, footerY - 36, contentW, 28);
                BrandTokens.Fill(banner, BrandTokens.GoldTint);
                BrandTokens.Fill(new Rect(banner.x, banner.y, 3, banner.height), BrandTokens.Gold);

                GUI.Label(new Rect(banner.x + 14, banner.y + 6, banner.width - 140, 16),
                    $"v{latestVer} is available — update GD CodeShield from the Package Manager.",
                    BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, BrandTokens.Navy, FontStyle.Bold));

                // Open Package Manager on click (banner body)
                var openArea = new Rect(banner.x, banner.y, banner.width - 40, banner.height);
                EditorGUIUtility.AddCursorRect(openArea, MouseCursor.Link);
                if (openArea.Contains(Event.current.mousePosition) &&
                    Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    UnityEditor.PackageManager.UI.Window.Open("com.gamedistrict.codeshield");
                    Event.current.Use();
                }

                // Dismiss ✕ — per version; reappears only for the next release
                var closeR = new Rect(banner.xMax - 28, banner.y + 5, 18, 18);
                GUI.Label(closeR, "✕", BrandTokens.MakeStyle(BrandTokens.Inter, 11, BrandTokens.WarmGray, FontStyle.Normal, TextAnchor.MiddleCenter));
                EditorGUIUtility.AddCursorRect(closeR, MouseCursor.Link);
                if (closeR.Contains(Event.current.mousePosition) &&
                    Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    UpdateChecker.Dismiss(latestVer);
                    Event.current.Use();
                    Repaint();
                }
            }

            // ── Footer ─────────────────────────────────────────────────────────
            BrandTokens.HairlineH(padL, footerY, contentW, BrandTokens.Taupe);

            string ver = _packageVersion != null ? "v" + _packageVersion : "v—";
            GUI.Label(new Rect(padL, footerY + 14, contentW, 14),
                "GAME DISTRICT  ·  CODESHIELD  " + ver, _sFootnote);

            string credit = "Built by Ghulam Mohyuddin";
            var creditSize = _sFootnote.CalcSize(new GUIContent(credit));
            GUI.Label(new Rect(W - padR - creditSize.x, footerY + 14, creditSize.x, 14), credit, _sFootnote);
        }

        // ─── Brand mark — shield with gold check, drawn from primitives ───────
        private void DrawBrandMark(float x, float y)
        {
            // Shield silhouette — taller than wide, rounded bottom point.
            // Approximated with layered fills since IMGUI has no curve primitive.
            float cx = x + 12;  // center x
            float sw = 22, sh = 26;

            // Outer outline — top rectangle + bottom triangle approximation
            // Top portion (rectangular shoulders)
            BrandTokens.HairlineH(x + 1,  y,         sw - 2, BrandTokens.Navy);
            BrandTokens.HairlineV(x,      y,         sh - 6, BrandTokens.Navy);
            BrandTokens.HairlineV(x + sw - 1, y,     sh - 6, BrandTokens.Navy);

            // Tapered bottom — two angled lines made of stepped 1px segments
            int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)steps;
                float leftX  = x + t * (sw / 2);
                float rightX = x + sw - 1 - t * (sw / 2);
                float yy = y + sh - 6 + i;
                BrandTokens.Fill(new Rect(leftX,  yy, 1, 1.4f), BrandTokens.Navy);
                BrandTokens.Fill(new Rect(rightX, yy, 1, 1.4f), BrandTokens.Navy);
            }

            // Gold check inside — proper checkmark with two strokes
            float ckX = x + 5, ckY = y + 13;
            // Short diagonal (low-left to mid)
            for (int i = 0; i < 4; i++)
                BrandTokens.Fill(new Rect(ckX + i, ckY + i, 2, 2), BrandTokens.Gold);
            // Long diagonal (mid to upper-right)
            for (int i = 0; i < 7; i++)
                BrandTokens.Fill(new Rect(ckX + 3 + i, ckY + 3 - i, 2, 2), BrandTokens.Gold);
        }

        // ─── LEFT: eyebrow → H1 → lede → 3 stats ───────────────────────────────
        private void DrawLeftRail(float x, float y, float w)
        {
            DrawEyebrow(x, y, "WORKSTATION");
            float cy = y + 28;

            // H1
            GUI.Label(new Rect(x, cy, w, 50), "A second pair", _sH1);
            cy += 42;
            GUI.Label(new Rect(x, cy, w, 50), "of eyes,", _sH1);
            cy += 42;
            GUI.Label(new Rect(x, cy, w, 50), "before you ship.", _sH1);
            cy += 52;

            // Lede — moved up to reduce dead air
            var ledeRect = new Rect(x, cy, w - 20, 80);
            GUI.Label(ledeRect, "Two tools, one workspace. Run a code review or sweep your release checklist before the build button.", _sLede);
            cy += 78;

            // Stats row — tightened up against lede with shorter divider gap
            BrandTokens.HairlineH(x, cy + 14, w, BrandTokens.Taupe);
            float statsY = cy + 30;
            DrawStat(x,       statsY, "25", "STUDIOS");
            DrawStat(x + 90,  statsY, "4",  "PRINCIPLES");
            DrawStat(x + 200, statsY, "0",  "MANUAL PASSES");

            // Game District logo — anchors the brand without breaking editorial language
            DrawGDLogo(x, statsY + 70, w);
        }

        // ─── Game District logo, rendered at modest scale ──────────────────────
        private void DrawGDLogo(float x, float y, float w)
        {
            var logo = BrandTokens.GDLogo;
            if (logo == null) return; // gracefully skip if asset missing

            // Logo aspect ratio comes straight from the texture itself
            float logoH = 64f;
            float logoW = logoH * (logo.width / (float)logo.height);

            var prev = GUI.color;
            // Slight desaturation to soften the dark logo against cream — feels native to the editorial look
            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            GUI.DrawTexture(new Rect(x, y, logoW, logoH), logo, ScaleMode.ScaleToFit);
            GUI.color = prev;
        }

        private void DrawStat(float x, float y, string num, string label)
        {
            GUI.Label(new Rect(x, y, 80, 30), num, _sStatNum);
            GUI.Label(new Rect(x, y + 26, 100, 14), label, _sStatLabel);
        }

        // ─── RIGHT: eyebrow → 2 tool entries → footnote ────────────────────────
        private void DrawRightColumn(float x, float y, float w)
        {
            DrawEyebrow(x, y, "TOOLS");
            float cy = y + 24;

            // Tool entry 1 — SOLID Review
            BrandTokens.HairlineH(x, cy, w, BrandTokens.Taupe);
            DrawToolEntry(0, x, cy + 6, w, "01", "SOLID review",
                "Four principles. Honest scores. Word docs to share.",
                "SRP · OCP · LSP · ISP · ~30s scan",
                () => SolidAgent.SolidAgentWindow.Open());
            cy += 96;
            BrandTokens.HairlineH(x, cy, w, BrandTokens.Taupe);

            // Tool entry 2 — SDK Checklist
            DrawToolEntry(1, x, cy + 6, w, "02", "SDK checklist",
                "Version drift, manifest holes, and release-day misses.",
                "10 tabs · 6 SDKs · auto-detect",
                () => GDChecklist.ChecklistWindow.Open());
            cy += 96;
            BrandTokens.HairlineH(x, cy, w, BrandTokens.Taupe);
            cy += 18;

            // Dashed placeholder for future tools
            DrawDashedPlaceholder(new Rect(x, cy, w, 38), "More checks land here");
            cy += 52;

            // AI warning footnote
            var warnRect = new Rect(x, cy, w, 30);
            BrandTokens.Fill(new Rect(x, cy, 3, 30), BrandTokens.Overdue);
            GUI.Label(new Rect(x + 12, cy + 2, w - 24, 30),
                "Results may contain AI suggestions. Always review before applying.",
                BrandTokens.MakeWrappedStyle(BrandTokens.Inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray));

            // Contact Support — small ghost link bottom-right of this column
            cy += 40;
            var contactRect = new Rect(x + w - 130, cy, 130, 18);
            bool contactHover = contactRect.Contains(Event.current.mousePosition);
            EditorGUIUtility.AddCursorRect(contactRect, MouseCursor.Link);
            GUI.Label(contactRect, contactHover ? "Contact support  →" : "Contact support",
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeFootnote,
                    contactHover ? BrandTokens.Navy : BrandTokens.WarmGray,
                    FontStyle.Bold, TextAnchor.MiddleRight));
            if (contactHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                ContactSupportWindow.Open();
                Event.current.Use();
            }
        }

        // ─── Tool entry — gold index + Fraunces title + italic lede + meta + OPEN → ─
        private void DrawToolEntry(int id, float x, float y, float w, string num, string title, string lede, string meta, System.Action onClick)
        {
            var entryRect = new Rect(x, y, w, 84);
            bool hover = entryRect.Contains(Event.current.mousePosition);

            // Hover surface — subtle cream lift
            if (hover) BrandTokens.Fill(entryRect, BrandTokens.GoldTint);

            EditorGUIUtility.AddCursorRect(entryRect, MouseCursor.Link);

            // Layout: [num | content | OPEN →]
            float numW   = 54f;
            float openW  = 64f;
            float contentX = x + numW;
            float contentW = w - numW - openW;

            GUI.Label(new Rect(x + 6, y + 4, numW, 40), num, _sToolNum);

            GUI.Label(new Rect(contentX, y + 6,  contentW, 28), title, _sToolTitle);
            GUI.Label(new Rect(contentX, y + 36, contentW, 18), lede, _sToolLede);
            GUI.Label(new Rect(contentX, y + 58, contentW, 14), meta, _sToolMeta);

            // OPEN → label — slides on hover, hairline underline at rest for affordance
            float openX = x + w - openW + (hover ? 4 : 0);
            string openLabel = hover ? "OPEN  →" : "OPEN";
            GUI.Label(new Rect(openX, y + 8, openW, 22), openLabel, _sToolOpen);
            // Hairline underline to signal it's a link
            var openTextSize = _sToolOpen.CalcSize(new GUIContent(openLabel));
            BrandTokens.HairlineH(openX, y + 8 + openTextSize.y + 1, openTextSize.x,
                hover ? BrandTokens.Navy : BrandTokens.Taupe);

            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                onClick?.Invoke();
                Event.current.Use();
            }

            _hover = hover ? id : (_hover == id ? -1 : _hover);
        }

        // ─── Eyebrow with 8px gold square + tracking ───────────────────────────
        private void DrawEyebrow(float x, float y, string label)
        {
            // Gold square (drawn as 7×7 to fit naturally with 11pt Inter caps)
            BrandTokens.Fill(new Rect(x, y + 4, BrandTokens.EyebrowSquare, BrandTokens.EyebrowSquare), BrandTokens.Gold);

            // Manually space the label letters for that 0.22em tracking feel.
            // IMGUI doesn't support letter-spacing — insert hair spaces (\u200A) between chars.
            string spaced = "";
            for (int i = 0; i < label.Length; i++)
            {
                spaced += label[i];
                if (i < label.Length - 1) spaced += "\u2009"; // thin space
            }
            GUI.Label(new Rect(x + BrandTokens.EyebrowSquare + 10, y, 300, 18), spaced, _sEyebrow);
        }

        // ─── Dashed border placeholder ─────────────────────────────────────────
        private void DrawDashedPlaceholder(Rect r, string label)
        {
            // Top + bottom dashed lines (12px on / 6px off)
            DashH(r.x, r.y,                 r.width, BrandTokens.Taupe);
            DashH(r.x, r.y + r.height - 1,  r.width, BrandTokens.Taupe);
            DashV(r.x,                 r.y, r.height, BrandTokens.Taupe);
            DashV(r.x + r.width - 1,   r.y, r.height, BrandTokens.Taupe);

            var labelStyle = BrandTokens.MakeStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                14, BrandTokens.WarmGray, FontStyle.Italic, TextAnchor.MiddleCenter);
            GUI.Label(r, label, labelStyle);
        }

        private void DashH(float x, float y, float w, Color c)
        {
            for (float dx = 0; dx < w; dx += 18)
                BrandTokens.Fill(new Rect(x + dx, y, Mathf.Min(12, w - dx), 1), c);
        }
        private void DashV(float x, float y, float h, Color c)
        {
            for (float dy = 0; dy < h; dy += 18)
                BrandTokens.Fill(new Rect(x, y + dy, 1, Mathf.Min(12, h - dy)), c);
        }
    }
}