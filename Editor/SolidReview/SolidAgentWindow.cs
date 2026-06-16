// Editor/SolidReview/SolidAgentWindow.cs
// SOLID Review — editorial redesign (v1.3.0)
// Spec: cream page, 6px gold left-bar, navy text, Fraunces + Inter, no framed boxes.
//
// Screens:
//   Home     — eyebrow, H1, principle checkboxes, scan target list, START button
//   Scanning — big gold % numeral, progress bar, activity tail (last 6 files)
//   Results  — sidebar (stats, principle stars, grouped violations) + detail pane
//   Detail   — Violation / Proposed fix / Claude Code tabs
//
// Preserved behavior:
//   - StartScan logic, exclusion rules, telemetry, Word report export
//   - Folder picker (multi-select scan roots)
//   - Rescan, Skip, File doc, Download report, Copy prompt, Run in Tool
// Dropped: embedded terminal, contract checks, regression tracking — out of scope for redesign.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using GDCodeShield.Brand;

namespace SolidAgent
{
    public class SolidAgentWindow : EditorWindow
    {
        // ─── State machine ─────────────────────────────────────────────────────
        private enum Screen { Home, Scanning, Results }
        private Screen _screen = Screen.Home;

        // ─── Scan inputs ───────────────────────────────────────────────────────
        private HashSet<SolidPrinciple> _enabledPrinciples = new HashSet<SolidPrinciple> {
            SolidPrinciple.SRP, SolidPrinciple.OCP, SolidPrinciple.LSP, SolidPrinciple.ISP
        };
        private HashSet<string> _scanRoots = new HashSet<string>();
        private bool _showFolderPicker = false;
        private Vector2 _folderPickerScroll;
        private HashSet<string> _expandedFolders = new HashSet<string>();

        // ─── Scan output ───────────────────────────────────────────────────────
        private List<FileAnalysisResult> _results = new List<FileAnalysisResult>();
        private Dictionary<string, ReviewDecision> _decisions = new Dictionary<string, ReviewDecision>();
        private SolidReport _report = null;
        private string _statusMsg = "";
        private float _scanProgress = 0f;
        private List<(string fileName, int flags)> _recentFiles = new List<(string, int)>();

        // ─── Detail pane state ─────────────────────────────────────────────────
        private string _activeId = null; // "filePath||violationId"
        private int _activeTab = 0;      // 0=Violation, 1=Proposed Fix, 2=Claude Code
        private float _mainContentH = 600f; // measured each frame so ScrollView grows with code blocks

        // ─── Scroll ────────────────────────────────────────────────────────────
        private Vector2 _sidebarScroll, _mainScroll, _homeScroll;
        private SolidPrinciple? _principleFilter = null;

        // ─── Styles ────────────────────────────────────────────────────────────
        private GUIStyle _sBrand, _sCrumbs, _sH1, _sH2, _sH3, _sLede, _sEyebrow,
                         _sBody, _sMuted, _sStatNum, _sStatLabel, _sFootnote,
                         _sMono, _sBtn, _sBtnGold, _sPrincipleLetter, _sStars, _sFraunces;
        private bool _stylesReady;

        // ═══════════════════════════════════════════════════════════════════════
        //  OPEN
        // ═══════════════════════════════════════════════════════════════════════
        public static void Open()
        {
            var w = GetWindow<SolidAgentWindow>("  SOLID Review");
            w.minSize = new Vector2(900, 600);
            w.maxSize = new Vector2(4000, 4000); // resizable
            w.Show();
        }

        private void OnEnable()
        {
            _stylesReady = false;
            if (_results == null || _results.Count == 0)
            {
                _screen   = Screen.Home;
                _activeId = null;
                _statusMsg = "";
            }
        }

        private void OnInspectorUpdate() => Repaint();

        // ─── Style setup ───────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            var fraunces = BrandTokens.Fraunces;
            var italics  = BrandTokens.FrauncesItalic ?? fraunces;
            var inter    = BrandTokens.Inter;

            _sBrand     = BrandTokens.MakeStyle(fraunces, 16, BrandTokens.Navy, FontStyle.Bold);
            _sCrumbs    = BrandTokens.MakeStyle(inter, 11, BrandTokens.WarmGray);
            _sH1        = BrandTokens.MakeStyle(fraunces, BrandTokens.SizeH1, BrandTokens.Navy, FontStyle.Bold);
            _sH2        = BrandTokens.MakeStyle(fraunces, BrandTokens.SizeH2, BrandTokens.Navy, FontStyle.Bold);
            _sH3        = BrandTokens.MakeStyle(fraunces, BrandTokens.SizeH3, BrandTokens.Navy, FontStyle.Bold);
            _sLede      = BrandTokens.MakeWrappedStyle(italics, BrandTokens.SizeLede, BrandTokens.Ink, FontStyle.Italic);
            _sFraunces  = BrandTokens.MakeStyle(fraunces, 14, BrandTokens.Navy, FontStyle.Bold);
            _sEyebrow   = BrandTokens.MakeStyle(inter, BrandTokens.SizeEyebrow, BrandTokens.Navy, FontStyle.Bold);
            _sBody      = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Ink);
            _sMuted     = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.WarmGray);
            _sStatNum   = BrandTokens.MakeStyle(fraunces, BrandTokens.SizeStatNum, BrandTokens.Navy, FontStyle.Bold);
            _sStatLabel = BrandTokens.MakeStyle(inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray);
            _sFootnote  = BrandTokens.MakeStyle(inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray);
            _sMono      = BrandTokens.MakeStyle(inter, BrandTokens.SizeMono, BrandTokens.Ink);
            _sBtn       = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Navy, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sBtnGold   = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Navy, FontStyle.Bold, TextAnchor.MiddleCenter);
            _sPrincipleLetter = BrandTokens.MakeStyle(fraunces, 32, BrandTokens.Gold, FontStyle.Bold);
            _sStars     = BrandTokens.MakeStyle(fraunces, 16, BrandTokens.Gold, FontStyle.Bold);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  PAINT
        // ═══════════════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            EnsureStyles();
            float W = position.width, H = position.height;

            BrandTokens.Fill(new Rect(0, 0, W, H), BrandTokens.Cream);
            BrandTokens.Fill(new Rect(0, 0, BrandTokens.GoldBarWidth, H), BrandTokens.Gold);

            float padL = BrandTokens.GoldBarWidth + 30, padR = 36;

            // Topbar
            DrawTopBar(padL, padR, W);

            float bodyY = 78f;
            float footerH = 42f;
            var body = new Rect(padL, bodyY, W - padL - padR, H - bodyY - footerH);

            switch (_screen)
            {
                case Screen.Home:     DrawHomeScreen(body);     break;
                case Screen.Scanning: DrawScanningScreen(body); break;
                case Screen.Results:  DrawResultsScreen(body);  break;
            }

            DrawFooter(padL, padR, W, H, footerH);
        }

        // ─── Top bar (brand + crumbs) ─────────────────────────────────────────
        private void DrawTopBar(float padL, float padR, float W)
        {
            float ty = 18;
            DrawBrandMark(padL, ty);
            GUI.Label(new Rect(padL + 32, ty + 4, 240, 22), "CodeShield", _sBrand);

            string crumbs = _screen == Screen.Home     ? "Workstation  /  SOLID review"
                          : _screen == Screen.Scanning ? "SOLID review  /  Scanning"
                          :                              "SOLID review  /  Results";
            var crumbsSize = _sCrumbs.CalcSize(new GUIContent(crumbs));
            GUI.Label(new Rect(W - padR - crumbsSize.x, ty + 7, crumbsSize.x, 18), crumbs, _sCrumbs);

            BrandTokens.HairlineH(padL, 56, W - padL - padR, BrandTokens.Taupe);
        }

        // ─── Brand mark — tapered shield with gold check ──────────────────────
        private void DrawBrandMark(float x, float y)
        {
            float sw = 22, sh = 26;
            BrandTokens.HairlineH(x + 1, y, sw - 2, BrandTokens.Navy);
            BrandTokens.HairlineV(x, y, sh - 6, BrandTokens.Navy);
            BrandTokens.HairlineV(x + sw - 1, y, sh - 6, BrandTokens.Navy);
            for (int i = 0; i < 6; i++)
            {
                float t = i / 6f;
                BrandTokens.Fill(new Rect(x + t * (sw / 2), y + sh - 6 + i, 1, 1.4f), BrandTokens.Navy);
                BrandTokens.Fill(new Rect(x + sw - 1 - t * (sw / 2), y + sh - 6 + i, 1, 1.4f), BrandTokens.Navy);
            }
            float ckX = x + 5, ckY = y + 13;
            for (int i = 0; i < 4; i++) BrandTokens.Fill(new Rect(ckX + i, ckY + i, 2, 2), BrandTokens.Gold);
            for (int i = 0; i < 7; i++) BrandTokens.Fill(new Rect(ckX + 3 + i, ckY + 3 - i, 2, 2), BrandTokens.Gold);
        }

        // ─── Footer ────────────────────────────────────────────────────────────
        private void DrawFooter(float padL, float padR, float W, float H, float footerH)
        {
            float fy = H - footerH;
            BrandTokens.HairlineH(padL, fy, W - padL - padR, BrandTokens.Taupe);

            string left = _screen == Screen.Home     ? "GAME DISTRICT  ·  SOLID REVIEW · CHECKPOINT 01"
                        : _screen == Screen.Scanning ? "GAME DISTRICT  ·  SOLID REVIEW · IN PROGRESS"
                        :                              "GAME DISTRICT  ·  SOLID REVIEW · COMPLETE";
            GUI.Label(new Rect(padL, fy + 14, 400, 14), left, _sFootnote);

            if (_screen == Screen.Results && _report != null)
            {
                string right = $"{_report.TotalViolations} VIOLATIONS  ·  {_report.OverallScore:F1} / 5";
                var sz = _sFootnote.CalcSize(new GUIContent(right));
                GUI.Label(new Rect(W - padR - sz.x, fy + 14, sz.x, 14), right, _sFootnote);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SCREEN 1 — HOME
        // ═══════════════════════════════════════════════════════════════════════
        private void DrawHomeScreen(Rect body)
        {
            // Two columns: left = principles, right = scan target + start
            float gap = 56f;
            float leftW = (body.width - gap) * 0.5f;
            float rightX = body.x + leftW + gap;
            float rightW = body.width - leftW - gap;

            DrawHomeLeft(body.x, body.y, leftW);
            DrawHomeRight(rightX, body.y, rightW);
        }

        private void DrawHomeLeft(float x, float y, float w)
        {
            DrawEyebrow(x, y, "CHECKPOINT");
            float cy = y + 32;

            GUI.Label(new Rect(x, cy, w, 50), "SOLID review.", _sH1);
            cy += 56;

            var ledeRect = new Rect(x, cy, w, 80);
            GUI.Label(ledeRect, "Pick the principles to enforce. We'll score each file 1-5 and bundle a Word report when the scan finishes.", _sLede);
            cy += 80;

            // Principle 2x2 grid
            float cellW = (w - 14) / 2;
            float cellH = 88;
            DrawPrincipleCell(SolidPrinciple.SRP, "S", "Single Responsibility", "One class, one reason to change.",
                new Rect(x, cy, cellW, cellH));
            DrawPrincipleCell(SolidPrinciple.OCP, "O", "Open / Closed", "Open to extension, closed to edits.",
                new Rect(x + cellW + 14, cy, cellW, cellH));
            cy += cellH + 14;
            DrawPrincipleCell(SolidPrinciple.LSP, "L", "Liskov Substitution", "Subtypes act like their parents.",
                new Rect(x, cy, cellW, cellH));
            DrawPrincipleCell(SolidPrinciple.ISP, "I", "Interface Segregation", "Don't force unused methods.",
                new Rect(x + cellW + 14, cy, cellW, cellH));
        }

        private void DrawPrincipleCell(SolidPrinciple p, string letter, string title, string sub, Rect r)
        {
            bool enabled = _enabledPrinciples.Contains(p);
            bool hover = r.Contains(Event.current.mousePosition);

            if (hover) BrandTokens.Fill(r, BrandTokens.GoldTint);
            BrandTokens.Outline(r, BrandTokens.Taupe);

            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            // Layout: [16px checkbox] [24px letter] [title + sub]
            //         padding ~14, content top ~14
            float padX = 14;
            float topY = r.y + 14;

            // Checkbox
            var cb = new Rect(r.x + padX, topY + 2, 16, 16);
            BrandTokens.Outline(cb, BrandTokens.Navy);
            if (enabled)
            {
                BrandTokens.Fill(new Rect(cb.x + 2, cb.y + 2, 12, 12), BrandTokens.Navy);
                for (int i = 0; i < 3; i++) BrandTokens.Fill(new Rect(cb.x + 3 + i, cb.y + 7 + i, 2, 2), BrandTokens.Gold);
                for (int i = 0; i < 5; i++) BrandTokens.Fill(new Rect(cb.x + 6 + i, cb.y + 10 - i, 2, 2), BrandTokens.Gold);
            }

            // Big gold Fraunces letter — sits next to checkbox, sized to align with title baseline
            float letterX = cb.xMax + 12;
            GUI.Label(new Rect(letterX, topY - 6, 28, 44), letter,
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 30, BrandTokens.Gold, FontStyle.Bold));

            // Title + sub start after letter
            float textX = letterX + 32;
            float textW = r.x + r.width - textX - padX;

            GUI.Label(new Rect(textX, topY, textW, 18),
                title,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, BrandTokens.Navy, FontStyle.Bold));

            GUI.Label(new Rect(textX, topY + 22, textW, 32),
                sub,
                BrandTokens.MakeWrappedStyle(BrandTokens.Inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray));

            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (enabled) _enabledPrinciples.Remove(p);
                else _enabledPrinciples.Add(p);
                Event.current.Use();
                Repaint();
            }
        }

        private void DrawHomeRight(float x, float y, float w)
        {
            DrawEyebrow(x, y, "SCAN TARGET");
            float cy = y + 32;

            // Current selection summary
            bool hasFolder = _scanRoots.Count > 0;
            string display = !hasFolder
                ? "All Assets  (entire project)"
                : _scanRoots.Count == 1
                    ? _scanRoots.First().Replace(Application.dataPath, "Assets")
                    : $"{_scanRoots.Count} folders selected";

            // Selection display row
            var dispRect = new Rect(x, cy, w, 38);
            BrandTokens.Outline(dispRect, hasFolder ? BrandTokens.Gold : BrandTokens.Taupe);
            if (hasFolder) BrandTokens.Fill(new Rect(x, cy, 3, 38), BrandTokens.Gold);

            GUI.Label(new Rect(x + 14, cy + 11, w - 130, 16), display,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody,
                    hasFolder ? BrandTokens.Navy : BrandTokens.WarmGray));

            // Pick / Close button
            string pickLabel = _showFolderPicker ? "Close ▲" : "Pick folders ▼";
            var pickRect = new Rect(x + w - 116, cy + 7, 102, 24);
            if (TextButton(pickRect, pickLabel, _showFolderPicker))
            {
                _showFolderPicker = !_showFolderPicker;
                Repaint();
            }
            cy += 50;

            // Inline folder picker
            if (_showFolderPicker)
            {
                float pickerH = 180;
                DrawFolderPicker(x, cy, w, pickerH);
                cy += pickerH + 14;
            }

            // Hint
            GUI.Label(new Rect(x, cy, w, 32),
                "Tip: leave blank to scan the entire project. Pick folders to narrow scope.",
                BrandTokens.MakeWrappedStyle(BrandTokens.Inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray));
            cy += 32;

            // START button — big gold primary
            var startRect = new Rect(x, cy, w, 56);
            bool canStart = _enabledPrinciples.Count > 0;
            bool startHover = startRect.Contains(Event.current.mousePosition);

            BrandTokens.Fill(startRect, canStart ? BrandTokens.Gold : BrandTokens.Taupe);
            if (canStart && startHover) BrandTokens.Fill(startRect, new Color(244f/255f, 196f/255f, 48f/255f, 0.85f));

            GUI.Label(startRect, canStart ? "START SOLID REVIEW  →" : "SELECT AT LEAST ONE PRINCIPLE",
                BrandTokens.MakeStyle(BrandTokens.Inter, 14,
                    canStart ? BrandTokens.Navy : BrandTokens.WarmGray,
                    FontStyle.Bold, TextAnchor.MiddleCenter));

            if (canStart)
            {
                EditorGUIUtility.AddCursorRect(startRect, MouseCursor.Link);
                if (startHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    StartScan();
                    Event.current.Use();
                }
            }
            cy += 70;

            GUI.Label(new Rect(x, cy, w, 18),
                "No API key needed. Scoring runs locally; Claude Code optional for fixes.",
                _sFootnote);
        }

        // ─── Folder picker (inline) ────────────────────────────────────────────
        private void DrawFolderPicker(float x, float y, float w, float h)
        {
            BrandTokens.Outline(new Rect(x, y, w, h), BrandTokens.Taupe);

            // Top row: Clear all
            var clearRect = new Rect(x + 10, y + 8, 80, 22);
            if (_scanRoots.Count > 0 && TextButton(clearRect, "✕  Clear", false))
            {
                _scanRoots.Clear();
                Repaint();
            }

            // Scrollable folder list
            var viewRect = new Rect(x + 6, y + 36, w - 12, h - 44);
            var folders = GetTopLevelAssetFolders();

            // Content height must include every *expanded* subfolder row, not just the
            // top-level count — otherwise scrolling stops short of the revealed children.
            // Each row is 24px tall (see DrawFolderRow), starting at ly = 6.
            int visibleRows = 0;
            foreach (var folder in folders)
                visibleRows += CountVisibleFolderRows(folder);
            float contentH = 6 + visibleRows * 24 + 12;
            _folderPickerScroll = GUI.BeginScrollView(viewRect, _folderPickerScroll,
                new Rect(0, 0, viewRect.width - 14, contentH));

            float ly = 6;
            foreach (var folder in folders)
            {
                DrawFolderRow(folder, 0, ref ly, viewRect.width - 14);
            }

            GUI.EndScrollView();
        }

        private List<string> GetTopLevelAssetFolders()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(Application.dataPath)) return list;
                foreach (var d in Directory.GetDirectories(Application.dataPath))
                {
                    string name = Path.GetFileName(d);
                    if (name.StartsWith(".")) continue;
                    if (name == "Editor")    continue;
                    list.Add(d.Replace('\\', '/'));
                }
            }
            catch { }
            return list;
        }

        private void DrawFolderRow(string folderPath, int depth, ref float y, float w)
        {
            bool selected = _scanRoots.Contains(folderPath);
            bool expanded = _expandedFolders.Contains(folderPath);
            string name = Path.GetFileName(folderPath);

            var row = new Rect(8 + depth * 16, y, w - 16, 22);
            bool hover = row.Contains(Event.current.mousePosition);

            if (hover) BrandTokens.Fill(row, BrandTokens.GoldTint);

            // Expander (only if has subfolders)
            bool hasSubs = HasSubfolders(folderPath);
            if (hasSubs)
            {
                var exp = new Rect(row.x, row.y + 4, 12, 14);
                GUI.Label(exp, expanded ? "▾" : "▸",
                    BrandTokens.MakeStyle(BrandTokens.Inter, 9, BrandTokens.Ink));
                if (exp.Contains(Event.current.mousePosition) &&
                    Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    if (expanded) _expandedFolders.Remove(folderPath);
                    else _expandedFolders.Add(folderPath);
                    Event.current.Use();
                    Repaint();
                }
            }

            // Checkbox
            var cb = new Rect(row.x + 16, row.y + 4, 14, 14);
            BrandTokens.Outline(cb, BrandTokens.Navy);
            if (selected)
            {
                BrandTokens.Fill(new Rect(cb.x + 2, cb.y + 2, 10, 10), BrandTokens.Navy);
                for (int i = 0; i < 5; i++) BrandTokens.Fill(new Rect(cb.x + 5 + i, cb.y + 9 - i, 1.5f, 1.5f), BrandTokens.Gold);
            }
            if (cb.Contains(Event.current.mousePosition) &&
                Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (selected) _scanRoots.Remove(folderPath);
                else _scanRoots.Add(folderPath);
                Event.current.Use();
                Repaint();
            }

            // Name
            GUI.Label(new Rect(row.x + 36, row.y + 4, row.width - 40, 14), name,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody,
                    selected ? BrandTokens.Navy : BrandTokens.Ink));

            y += 24;

            // Children
            if (expanded && hasSubs)
            {
                try
                {
                    foreach (var sub in Directory.GetDirectories(folderPath))
                    {
                        string sn = Path.GetFileName(sub);
                        if (sn.StartsWith(".")) continue;
                        DrawFolderRow(sub.Replace('\\', '/'), depth + 1, ref y, w);
                    }
                }
                catch { }
            }
        }

        private bool HasSubfolders(string path)
        {
            try { return Directory.GetDirectories(path).Length > 0; }
            catch { return false; }
        }

        // Counts visible rows for one folder subtree, mirroring DrawFolderRow's
        // expansion + filtering so the scroll content height matches what's drawn.
        private int CountVisibleFolderRows(string folderPath)
        {
            int count = 1; // the folder's own row
            if (_expandedFolders.Contains(folderPath) && HasSubfolders(folderPath))
            {
                try
                {
                    foreach (var sub in Directory.GetDirectories(folderPath))
                    {
                        string sn = Path.GetFileName(sub);
                        if (sn.StartsWith(".")) continue;
                        count += CountVisibleFolderRows(sub.Replace('\\', '/'));
                    }
                }
                catch { }
            }
            return count;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SCREEN 2 — SCANNING
        // ═══════════════════════════════════════════════════════════════════════
        private void DrawScanningScreen(Rect body)
        {
            float gap = 56f;
            float leftW = (body.width - gap) * 0.58f;
            float rightX = body.x + leftW + gap;
            float rightW = body.width - leftW - gap;

            DrawScanningLeft(body.x, body.y, leftW);
            DrawScanningRight(rightX, body.y, rightW);
        }

        private void DrawScanningLeft(float x, float y, float w)
        {
            DrawEyebrow(x, y, "SCANNING");
            float cy = y + 36;

            GUI.Label(new Rect(x, cy, w, 16), "Currently on", _sMuted);
            cy += 18;
            string current = string.IsNullOrEmpty(_statusMsg) ? "Reading scripts..." : _statusMsg;
            GUI.Label(new Rect(x, cy, w, 20), current, _sMono);
            cy += 44;

            // Huge gold % numeral
            int pct = Mathf.RoundToInt(_scanProgress * 100);
            string pctText = pct + "%";
            var bigStyle = BrandTokens.MakeStyle(BrandTokens.Fraunces, 80, BrandTokens.Gold, FontStyle.Bold);
            GUI.Label(new Rect(x, cy, w, 90), pctText, bigStyle);
            cy += 100;

            // Thin progress bar
            BrandTokens.Fill(new Rect(x, cy, w, 2), BrandTokens.Taupe);
            BrandTokens.Fill(new Rect(x, cy, w * _scanProgress, 2), BrandTokens.Gold);
            cy += 14;

            int filesProcessed = _results.Count + _recentFiles.Count;
            int filesTotal = Mathf.Max(filesProcessed, 1);
            GUI.Label(new Rect(x, cy, w, 16),
                $"{_recentFiles.Count} files processed",
                _sMono);
            cy += 32;

            // Cancel button
            var cancelRect = new Rect(x, cy, 120, 32);
            if (TextButton(cancelRect, "Cancel", false))
            {
                _screen = Screen.Home;
                _scanProgress = 0f;
                _recentFiles.Clear();
            }
        }

        private void DrawScanningRight(float x, float y, float w)
        {
            DrawEyebrow(x, y, "ACTIVITY");
            float cy = y + 30;

            // Show last 8 files in reverse order
            var recent = _recentFiles.Count > 8
                ? _recentFiles.GetRange(_recentFiles.Count - 8, 8)
                : _recentFiles;

            for (int i = recent.Count - 1; i >= 0; i--)
            {
                var (fileName, flags) = recent[i];
                var rowRect = new Rect(x, cy, w, 30);
                BrandTokens.HairlineH(x, cy + 29, w, BrandTokens.Taupe);

                GUI.Label(new Rect(x, cy + 8, 30, 14), (i + 1).ToString(),
                    BrandTokens.MakeStyle(BrandTokens.Inter, 10, BrandTokens.WarmGray));
                GUI.Label(new Rect(x + 36, cy + 8, w - 120, 14), fileName, _sMono);

                if (flags == 0)
                {
                    GUI.Label(new Rect(x + w - 80, cy + 8, 80, 14), "CLEAN",
                        BrandTokens.MakeStyle(BrandTokens.Inter, 10, BrandTokens.Shipped, FontStyle.Bold, TextAnchor.MiddleRight));
                }
                else
                {
                    GUI.Label(new Rect(x + w - 80, cy + 8, 80, 14), flags + " flag" + (flags == 1 ? "" : "s"),
                        BrandTokens.MakeStyle(BrandTokens.Inter, 10, BrandTokens.Overdue, FontStyle.Bold, TextAnchor.MiddleRight));
                }
                cy += 30;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SCREEN 3 — RESULTS
        // ═══════════════════════════════════════════════════════════════════════
        private void DrawResultsScreen(Rect body)
        {
            float sidebarW = Mathf.Min(340, body.width * 0.32f);
            float gap = 32f;
            DrawResultsSidebar(new Rect(body.x, body.y, sidebarW, body.height));
            DrawResultsMain(new Rect(body.x + sidebarW + gap, body.y, body.width - sidebarW - gap, body.height));
        }

        private void DrawResultsSidebar(Rect r)
        {
            float contentH = ComputeSidebarHeight();
            _sidebarScroll = GUI.BeginScrollView(r, _sidebarScroll,
                new Rect(0, 0, r.width - 14, contentH));

            float x = 0, y = 0, w = r.width - 14;

            // Stat strip
            DrawStat(x,        y, _report?.TotalViolations.ToString() ?? "0", "TOTAL");
            DrawStat(x + 80,   y, _decisions.Values.Count(d => d == ReviewDecision.Applied).ToString(), "APPLIED");
            DrawStat(x + 170,  y, _decisions.Values.Count(d => d == ReviewDecision.Skipped).ToString(), "SKIPPED");
            y += 56;
            BrandTokens.HairlineH(x, y, w, BrandTokens.Taupe);
            y += 16;

            // Principle ratings
            DrawEyebrow(x, y, "PRINCIPLES");
            y += 24;

            if (_report?.Ratings != null)
            {
                foreach (var rating in _report.Ratings)
                {
                    bool isFilter = _principleFilter == rating.Principle;
                    var rowRect = new Rect(x, y, w, 26);

                    if (isFilter) BrandTokens.Fill(rowRect, BrandTokens.GoldTint);

                    GUI.Label(new Rect(x + 4, y + 5, 60, 18),
                        rating.Principle.ToString(),
                        BrandTokens.MakeStyle(BrandTokens.Fraunces, 14, BrandTokens.Navy, FontStyle.Bold));

                    string stars = BuildStars(rating.Score);
                    GUI.Label(new Rect(x + w - 130, y + 6, 120, 18),
                        stars,
                        BrandTokens.MakeStyle(BrandTokens.Fraunces, 14, BrandTokens.Gold, FontStyle.Bold, TextAnchor.MiddleRight));

                    EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
                    if (rowRect.Contains(Event.current.mousePosition) &&
                        Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        _principleFilter = isFilter ? (SolidPrinciple?)null : rating.Principle;
                        Event.current.Use();
                        Repaint();
                    }
                    y += 28;
                }
            }
            y += 8;
            BrandTokens.HairlineH(x, y, w, BrandTokens.Taupe);
            y += 16;

            // Overall
            DrawEyebrow(x, y, "OVERALL");
            y += 22;
            float overall = _report?.OverallScore ?? 0;
            Color barColor = overall >= 4f ? BrandTokens.Shipped : (overall >= 3f ? BrandTokens.Gold : BrandTokens.Overdue);
            GUI.Label(new Rect(x, y, w, 30), $"{overall:F1} / 5",
                BrandTokens.MakeStyle(BrandTokens.Fraunces, 24, barColor, FontStyle.Bold));
            y += 28;
            BrandTokens.Fill(new Rect(x, y, w, 3), BrandTokens.Taupe);
            BrandTokens.Fill(new Rect(x, y, w * (overall / 5f), 3), barColor);
            y += 10;
            GUI.Label(new Rect(x, y, w, 14),
                (_report?.OverallLabel ?? "").ToUpper(),
                BrandTokens.MakeStyle(BrandTokens.Inter, 10, barColor, FontStyle.Bold));
            y += 24;

            // Download report button — sits right under OVERALL info
            var dlRect = new Rect(x, y, w, 36);
            if (TextButton(dlRect, "↓  Download Word report", false))
            {
                if (_report != null)
                {
                    try
                    {
                        string path = SolidReportExporter.Export(_report);
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            System.Diagnostics.Process.Start(path);
                    }
                    catch (System.Exception ex)
                    {
                        EditorUtility.DisplayDialog("Export failed",
                            "Could not generate Word report.\n\n" + ex.Message, "OK");
                    }
                }
            }
            y += 50;
            BrandTokens.HairlineH(x, y, w, BrandTokens.Taupe);
            y += 16;

            // Violation list
            var filtered = FilteredViolations();
            GUI.Label(new Rect(x, y, w, 14),
                $"Showing {filtered.Count} violation{(filtered.Count == 1 ? "" : "s")}" +
                (_principleFilter.HasValue ? $" · {_principleFilter}" : ""),
                _sFootnote);
            y += 22;

            // Group by file
            var byFile = filtered.GroupBy(v => v.Location.FilePath).OrderBy(g => g.Key).ToList();
            foreach (var grp in byFile)
            {
                GUI.Label(new Rect(x, y, w, 16),
                    Path.GetFileName(grp.Key),
                    BrandTokens.MakeStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                        12, BrandTokens.WarmGray, FontStyle.Italic));
                y += 18;

                foreach (var v in grp)
                {
                    string key = MakeKey(v);
                    bool active = key == _activeId;
                    var vRect = new Rect(x, y, w, 44);

                    if (active)
                    {
                        BrandTokens.Fill(vRect, BrandTokens.GoldTint);
                        BrandTokens.Fill(new Rect(x, y, 2, 44), BrandTokens.Gold);
                    }

                    EditorGUIUtility.AddCursorRect(vRect, MouseCursor.Link);

                    DrawPrinciplePill(new Rect(x + 8, y + 6, 32, 16), v.Principle);
                    GUI.Label(new Rect(x + 46, y + 6, 80, 14),
                        "L" + v.Location.StartLine,
                        BrandTokens.MakeStyle(BrandTokens.Inter, 10, BrandTokens.WarmGray));
                    GUI.Label(new Rect(x + 8, y + 24, w - 16, 16),
                        v.Title,
                        BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody,
                            active ? BrandTokens.Navy : BrandTokens.Ink,
                            active ? FontStyle.Bold : FontStyle.Normal));

                    var dec = _decisions.GetValueOrDefault(key);
                    if (dec == ReviewDecision.Skipped)
                    {
                        GUI.Label(new Rect(x + w - 70, y + 24, 64, 14), "SKIPPED",
                            BrandTokens.MakeStyle(BrandTokens.Inter, 9, BrandTokens.WarmGray, FontStyle.Bold, TextAnchor.MiddleRight));
                    }

                    BrandTokens.HairlineH(x, y + 44, w, BrandTokens.Taupe);

                    if (vRect.Contains(Event.current.mousePosition) &&
                        Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        _activeId = key;
                        _activeTab = 0;
                        _mainScroll = Vector2.zero;
                        _mainContentH = 600f; // re-measure
                        Event.current.Use();
                        Repaint();
                    }
                    y += 46;
                }
                y += 8;
            }
            y += 14;

            GUI.EndScrollView();
        }

        private float ComputeSidebarHeight()
        {
            float h = 56 + 16 + 24; // stats + line + eyebrow
            h += (_report?.Ratings?.Count ?? 0) * 28;
            h += 8 + 16 + 22 + 28 + 10 + 28 + 16;       // overall block
            h += 22; // showing N text
            var filtered = FilteredViolations();
            var byFile = filtered.GroupBy(v => v.Location.FilePath).Count();
            h += byFile * 26 + filtered.Count * 46;
            h += 50 + 80;
            return h;
        }

        // ─── Main (detail) pane ────────────────────────────────────────────────
        private void DrawResultsMain(Rect r)
        {
            var v = FindViolation(_activeId);
            if (v == null)
            {
                GUI.Label(new Rect(r.x, r.y + 100, r.width, 30),
                    "Select a violation on the left.",
                    BrandTokens.MakeStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                        16, BrandTokens.WarmGray, FontStyle.Italic, TextAnchor.MiddleCenter));
                return;
            }

            float contentH = Mathf.Max(_mainContentH, 600);
            _mainScroll = GUI.BeginScrollView(r, _mainScroll,
                new Rect(0, 0, r.width - 14, contentH));

            float x = 0, y = 0, w = r.width - 14;

            // Header row: pills + nav
            DrawPrinciplePill(new Rect(x, y, 36, 18), v.Principle);
            DrawSeverityPill(new Rect(x + 44, y, 100, 18), v.Severity);

            // Prev/Next
            var all = FilteredViolations();
            int idx = all.FindIndex(vv => MakeKey(vv) == _activeId);
            string counter = $"Showing {idx + 1} of {all.Count}";
            var cSize = _sFootnote.CalcSize(new GUIContent(counter));
            GUI.Label(new Rect(x + w - cSize.x - 140, y + 2, cSize.x, 18), counter, _sFootnote);

            var prevR = new Rect(x + w - 130, y - 2, 60, 24);
            var nextR = new Rect(x + w - 66,  y - 2, 60, 24);
            if (TextButton(prevR, "← Prev", false) && idx > 0)
            { _activeId = MakeKey(all[idx - 1]); _activeTab = 0; _mainScroll = Vector2.zero; _mainContentH = 600f; Repaint(); }
            if (TextButton(nextR, "Next →", false) && idx < all.Count - 1)
            { _activeId = MakeKey(all[idx + 1]); _activeTab = 0; _mainScroll = Vector2.zero; _mainContentH = 600f; Repaint(); }

            y += 32;

            // H2 title
            GUI.Label(new Rect(x, y, w, 36), v.Title, _sH2);
            y += 36;
            string meta = $"{Path.GetFileName(v.Location.FilePath)}  ·  line {v.Location.StartLine}";
            GUI.Label(new Rect(x, y, w, 16), meta, _sMono);
            y += 30;

            // Tabs
            DrawTabBar(x, y, w);
            y += 38;

            if (_activeTab == 0) DrawViolationTab(v, x, ref y, w);
            else if (_activeTab == 1) DrawProposedFixTab(v, x, ref y, w);
            else if (_activeTab == 2) DrawClaudeCodeTab(v, x, ref y, w);

            y += 30;
            // Action bar
            BrandTokens.HairlineH(x, y, w, BrandTokens.Taupe);
            y += 12;
            DrawActionBar(v, x, y, w);
            y += 60; // bottom padding so action bar isn't flush against scroll edge

            // Update measured height so next frame's ScrollView accommodates the full pane.
            // Only repaint if changed significantly to avoid feedback loops.
            if (Mathf.Abs(_mainContentH - y) > 2f)
            {
                _mainContentH = y;
                Repaint();
            }

            GUI.EndScrollView();
        }

        private void DrawTabBar(float x, float y, float w)
        {
            string[] tabs = { "Violation", "Proposed fix", "Claude Code" };
            float tx = x;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool active = _activeTab == i;
                var size = BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, BrandTokens.Navy, FontStyle.Bold)
                    .CalcSize(new GUIContent(tabs[i]));
                var r = new Rect(tx, y, size.x + 6, 30);

                GUI.Label(new Rect(tx, y + 8, size.x + 6, 18), tabs[i],
                    BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody,
                        active ? BrandTokens.Navy : BrandTokens.WarmGray,
                        active ? FontStyle.Bold : FontStyle.Normal));

                if (active)
                    BrandTokens.Fill(new Rect(tx, y + 28, size.x + 6, 2), BrandTokens.Navy);

                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                if (r.Contains(Event.current.mousePosition) &&
                    Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _activeTab = i;
                    _mainContentH = 600f; // re-measure
                    Event.current.Use();
                    Repaint();
                }
                tx += size.x + 32;
            }
            BrandTokens.HairlineH(x, y + 30, w, BrandTokens.Taupe);
        }

        // ─── Violation tab ─────────────────────────────────────────────────────
        private void DrawViolationTab(Violation v, float x, ref float y, float w)
        {
            DrawEyebrow(x, y, "WHAT IS WRONG");
            y += 24;
            GUI.Label(new Rect(x, y, w - 20, 80),
                v.Description ?? "(no description)",
                BrandTokens.MakeWrappedStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                    16, BrandTokens.Ink, FontStyle.Italic));
            y += MeasureWrap(v.Description ?? "", w - 20, 16) + 24;

            if (!string.IsNullOrEmpty(v.Evidence))
            {
                DrawEyebrow(x, y, "EVIDENCE");
                y += 22;
                BrandTokens.Fill(new Rect(x, y, 2, 60), BrandTokens.Taupe);
                GUI.Label(new Rect(x + 14, y, w - 30, 80), v.Evidence,
                    BrandTokens.MakeWrappedStyle(BrandTokens.Inter, BrandTokens.SizeBody, BrandTokens.Ink));
                y += MeasureWrap(v.Evidence, w - 30, BrandTokens.SizeBody) + 24;
            }

            if (!string.IsNullOrEmpty(v.OriginalCode))
            {
                DrawEyebrow(x, y, "AFFECTED CODE");
                y += 22;
                DrawCodeBlock(v.OriginalCode, x, ref y, w, v.Location.StartLine);
            }
        }

        // ─── Proposed Fix tab ──────────────────────────────────────────────────
        private void DrawProposedFixTab(Violation v, float x, ref float y, float w)
        {
            DrawEyebrow(x, y, "WHAT CLAUDE PROPOSES");
            y += 22;
            GUI.Label(new Rect(x, y, w - 20, 60),
                "Run via Claude Code to generate the fix. The Claude Code tab has the prompt and a one-click launcher.",
                BrandTokens.MakeWrappedStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                    15, BrandTokens.Ink, FontStyle.Italic));
            y += 50;

            GUI.Label(new Rect(x, y, w - 20, 18),
                "Switch to the Claude Code tab to copy the prompt or launch Claude Code directly.",
                _sMuted);
            y += 30;
        }

        // ─── Claude Code tab ───────────────────────────────────────────────────
        private void DrawClaudeCodeTab(Violation v, float x, ref float y, float w)
        {
            DrawEyebrow(x, y, "READY-TO-RUN PROMPT");
            y += 22;
            GUI.Label(new Rect(x, y, w - 20, 40),
                "Paste this into Claude Code, or click Run in Tool to launch automatically with the prompt pre-filled.",
                BrandTokens.MakeWrappedStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                    14, BrandTokens.Ink, FontStyle.Italic));
            y += 50;

            string prompt = BuildClaudeCodePrompt(v);
            DrawSunkCodeBlock(prompt, x, ref y, w);
            y += 16;

            var copyR = new Rect(x, y, 150, 32);
            var runR  = new Rect(x + 162, y, 200, 32);

            if (TextButton(copyR, "📋  Copy prompt", false))
            {
                EditorGUIUtility.systemCopyBuffer = prompt;
            }
            if (GoldButton(runR, "🚀  Run in Claude Code →"))
            {
                OpenInClaudeCode(prompt);
            }
            y += 40;
        }

        // ─── Action bar at bottom of detail pane ───────────────────────────────
        private void DrawActionBar(Violation v, float x, float y, float w)
        {
            string key = MakeKey(v);
            var dec = _decisions.GetValueOrDefault(key);

            // Left: Skip
            var skipR = new Rect(x, y, 90, 32);
            if (dec == ReviewDecision.Skipped)
            {
                GUI.Label(skipR, "SKIPPED",
                    BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, BrandTokens.WarmGray, FontStyle.Bold, TextAnchor.MiddleCenter));
            }
            else
            {
                if (TextButton(skipR, "Skip", false))
                {
                    _decisions[key] = ReviewDecision.Skipped;
                    Repaint();
                }
            }

            // Right: File Doc
            var fileR = new Rect(x + w - 130, y, 130, 32);
            if (GoldButton(fileR, "↓  File doc"))
            {
                var fileResult = _results.FirstOrDefault(r => r.FilePath == v.Location.FilePath);
                if (fileResult != null && _report != null)
                {
                    try
                    {
                        string path = SolidReportExporter.ExportFile(fileResult, _report);
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            System.Diagnostics.Process.Start(path);
                    }
                    catch (System.Exception ex)
                    {
                        EditorUtility.DisplayDialog("Export failed",
                            "Could not generate file report.\n\n" + ex.Message, "OK");
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHARED PRIMITIVES
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawEyebrow(float x, float y, string label)
        {
            BrandTokens.Fill(new Rect(x, y + 4, BrandTokens.EyebrowSquare, BrandTokens.EyebrowSquare), BrandTokens.Gold);
            string spaced = "";
            for (int i = 0; i < label.Length; i++)
            {
                spaced += label[i];
                if (i < label.Length - 1) spaced += "\u2009";
            }
            GUI.Label(new Rect(x + BrandTokens.EyebrowSquare + 10, y, 400, 18), spaced, _sEyebrow);
        }

        private void DrawStat(float x, float y, string num, string label)
        {
            GUI.Label(new Rect(x, y, 80, 28), num, _sStatNum);
            GUI.Label(new Rect(x, y + 26, 100, 14), label, _sStatLabel);
        }

        private bool TextButton(Rect r, string label, bool active)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color borderC = (active || hover) ? BrandTokens.Navy : BrandTokens.Taupe;
            BrandTokens.Outline(r, borderC);
            if (hover) BrandTokens.Fill(r, BrandTokens.GoldTint);

            GUI.Label(r, label,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody,
                    active ? BrandTokens.Navy : (hover ? BrandTokens.Navy : BrandTokens.Ink),
                    FontStyle.Bold, TextAnchor.MiddleCenter));

            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private bool GoldButton(Rect r, string label)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            BrandTokens.Fill(r, hover ? new Color(244f/255f, 196f/255f, 48f/255f, 0.85f) : BrandTokens.Gold);
            GUI.Label(r, label,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, BrandTokens.Navy, FontStyle.Bold, TextAnchor.MiddleCenter));

            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private void DrawPrinciplePill(Rect r, SolidPrinciple p)
        {
            string label = p.ToString();
            BrandTokens.Fill(r, BrandTokens.OverdueTint);
            GUI.Label(r, label,
                BrandTokens.MakeStyle(BrandTokens.Inter, 10, BrandTokens.Overdue, FontStyle.Bold, TextAnchor.MiddleCenter));
        }

        private void DrawSeverityPill(Rect r, Severity s)
        {
            string text = s.ToString().ToUpper() + " SEVERITY";
            Color c = s == Severity.High ? BrandTokens.Overdue
                    : s == Severity.Medium ? BrandTokens.Gold
                    : BrandTokens.WarmGray;
            Color tint = s == Severity.High ? BrandTokens.OverdueTint
                    : s == Severity.Medium ? BrandTokens.GoldTint
                    : new Color(0.6f, 0.6f, 0.6f, 0.1f);
            BrandTokens.Fill(r, tint);
            GUI.Label(r, text,
                BrandTokens.MakeStyle(BrandTokens.Inter, 9, c, FontStyle.Bold, TextAnchor.MiddleCenter));
        }

        private void DrawCodeBlock(string code, float x, ref float y, float w, int startLine)
        {
            var lines = (code ?? "").Replace("\r", "").Split('\n');
            float lineH = 16;
            float blockH = lines.Length * lineH + 16;

            BrandTokens.Outline(new Rect(x, y, w, blockH), BrandTokens.Taupe);
            BrandTokens.Fill(new Rect(x, y, 36, blockH), new Color(0.95f, 0.94f, 0.88f));

            for (int i = 0; i < lines.Length; i++)
            {
                GUI.Label(new Rect(x, y + 8 + i * lineH, 32, lineH),
                    (startLine + i).ToString(),
                    BrandTokens.MakeStyle(BrandTokens.Inter, 10, BrandTokens.WarmGray, FontStyle.Normal, TextAnchor.MiddleRight));
                GUI.Label(new Rect(x + 44, y + 8 + i * lineH, w - 50, lineH),
                    lines[i], _sMono);
            }
            y += blockH;
        }

        private void DrawSunkCodeBlock(string code, float x, ref float y, float w)
        {
            float lineH = 16;
            var lines = (code ?? "").Replace("\r", "").Split('\n');
            int displayLines = Mathf.Min(lines.Length, 14);
            float blockH = displayLines * lineH + 20;

            BrandTokens.Fill(new Rect(x, y, w, blockH), new Color(0.95f, 0.94f, 0.88f));
            BrandTokens.Outline(new Rect(x, y, w, blockH), BrandTokens.Taupe);
            for (int i = 0; i < displayLines; i++)
            {
                GUI.Label(new Rect(x + 14, y + 10 + i * lineH, w - 28, lineH),
                    lines[i], _sMono);
            }
            if (lines.Length > displayLines)
            {
                GUI.Label(new Rect(x + 14, y + blockH - 16, w - 28, 14),
                    $"... ({lines.Length - displayLines} more lines)",
                    _sFootnote);
            }
            y += blockH;
        }

        private float MeasureWrap(string text, float w, int size)
        {
            var s = BrandTokens.MakeWrappedStyle(BrandTokens.Inter, size, BrandTokens.Ink);
            return s.CalcHeight(new GUIContent(text), w);
        }

        private string BuildStars(int score)
        {
            string s = "";
            for (int i = 1; i <= 5; i++) s += (i <= score ? "★ " : "☆ ");
            return s.TrimEnd();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SCAN LOGIC (preserved from original)
        // ═══════════════════════════════════════════════════════════════════════
        private async void StartScan()
        {
            _screen = Screen.Scanning;
            _statusMsg = "Reading scripts...";
            _scanProgress = 0f;
            _results.Clear();
            _decisions.Clear();
            _recentFiles.Clear();
            _activeId = null;
            _principleFilter = null;
            Repaint();

            var files = new List<string>();
            await Task.Run(() =>
            {
                var roots = _scanRoots.Count > 0
                    ? _scanRoots.ToList()
                    : new List<string> { Application.dataPath };
                var seen = new HashSet<string>();
                foreach (var root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                    {
                        if (!seen.Add(f)) continue;
                        string n = f.Replace('\\', '/');
                        if (n.Contains("/PackageCache/")) continue;
                        if (n.Contains("/TextMesh Pro/")) continue;
                        if (n.Contains("/Editor/")) continue;
                        if (n.Contains("/Packages/")) continue;
                        if (n.Contains(".Generated.cs")) continue;
                        if (n.Contains("AssemblyInfo.cs")) continue;
                        try { if (new FileInfo(f).Length > 200 * 1024) continue; } catch { continue; }
                        files.Add(f);
                    }
                }
            });

            var analyzer = new SolidAnalyzer();
            for (int i = 0; i < files.Count; i++)
            {
                string f = files[i];
                _statusMsg = Path.GetFileName(f);
                _scanProgress = (float)(i + 1) / files.Count;

                FileAnalysisResult result = null;
                var t = Task.Run(() => { try { result = analyzer.AnalyzeFile(f); } catch { } });
                if (!t.Wait(5000)) result = null;

                if (result != null)
                {
                    // Filter to enabled principles
                    result.Violations = result.Violations
                        .Where(v => _enabledPrinciples.Contains(v.Principle)).ToList();
                    _results.Add(result);

                    _recentFiles.Add((Path.GetFileName(f), result.Violations.Count));
                    if (_recentFiles.Count > 40) _recentFiles.RemoveAt(0);
                }
                Repaint();
                await Task.Delay(8);
            }

            int total = _results.Sum(r => r.Violations.Count);
            _statusMsg = total == 0 ? "No violations found." : $"Found {total} violation(s).";
            _screen = Screen.Results;

            string projName = Path.GetFileName(Application.dataPath.TrimEnd('/').TrimEnd('\\'));
            _report = RatingEngine.GenerateReport(_results, projName);

            // Telemetry
            SolidTelemetry.ReportScanCompleted(_report);

            var first = _results.SelectMany(r => r.Violations).FirstOrDefault();
            if (first != null) _activeId = MakeKey(first);
            Repaint();
        }

        // ─── Helpers ───────────────────────────────────────────────────────────
        private List<Violation> FilteredViolations()
        {
            var all = _results.SelectMany(r => r.Violations);
            if (_principleFilter.HasValue) all = all.Where(v => v.Principle == _principleFilter.Value);
            return all.ToList();
        }

        private string MakeKey(Violation v) => v.Location.FilePath + "||" + v.Id;

        private Violation FindViolation(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var fr in _results)
                foreach (var v in fr.Violations)
                    if (MakeKey(v) == key) return v;
            return null;
        }

        private string BuildClaudeCodePrompt(Violation v)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Fix SOLID violation in {Path.GetFileName(v.Location.FilePath)}");
            sb.AppendLine();
            sb.AppendLine($"**File:** {v.Location.FilePath}");
            sb.AppendLine($"**Line:** {v.Location.StartLine}");
            sb.AppendLine($"**Principle:** {v.Principle}");
            sb.AppendLine($"**Severity:** {v.Severity}");
            sb.AppendLine();
            sb.AppendLine("## The problem");
            sb.AppendLine(v.Description);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(v.Evidence))
            {
                sb.AppendLine("## Evidence");
                sb.AppendLine(v.Evidence);
                sb.AppendLine();
            }
            sb.AppendLine("## What to do");
            sb.AppendLine("Refactor the code to resolve this violation while preserving existing behavior.");
            sb.AppendLine("Keep changes minimal — do not introduce DIP (Dependency Inversion).");
            return sb.ToString();
        }

        private void OpenInClaudeCode(string prompt)
        {
            string folder  = Path.GetDirectoryName(Application.dataPath);
            string tmpFile = Path.Combine(Path.GetTempPath(), "gd_solid_prompt.md");
            File.WriteAllText(tmpFile, prompt);

            // Always copy to clipboard as a fallback
            EditorGUIUtility.systemCopyBuffer = prompt;

            try
            {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                string scriptFile = Path.Combine(Path.GetTempPath(), "gd_open_claude.sh");
                File.WriteAllText(scriptFile,
                    "#!/bin/bash\n" +
                    "cd \"" + folder + "\"\n" +
                    "echo ''\n" +
                    "echo '=== GD CodeShield — SOLID Fix ==='\n" +
                    "echo 'Prompt loaded from: " + tmpFile + "'\n" +
                    "echo 'Press ENTER to run claude, or Ctrl+C to cancel.'\n" +
                    "echo ''\n" +
                    "read\n" +
                    "claude \"$(cat '" + tmpFile + "')\"\n"
                );
                System.Diagnostics.Process.Start("chmod", "+x \"" + scriptFile + "\"");

                var osa = new System.Diagnostics.Process();
                osa.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "osascript",
                    Arguments       = "-e 'tell application \"Terminal\" to do script \"" + scriptFile +
                                      "\"' -e 'tell application \"Terminal\" to activate'",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };
                osa.Start();
#else
                string batFile = Path.Combine(Path.GetTempPath(), "gd_open_claude.bat");
                File.WriteAllText(batFile,
                    "@echo off\r\n" +
                    "cd /d \"" + folder + "\"\r\n" +
                    "echo.\r\n" +
                    "echo === GD CodeShield - SOLID Fix ===\r\n" +
                    "echo Prompt loaded from: " + tmpFile + "\r\n" +
                    "echo Press ENTER to run claude, or Ctrl+C to cancel.\r\n" +
                    "echo.\r\n" +
                    "pause\r\n" +
                    "claude < \"" + tmpFile + "\"\r\n"
                );
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = batFile,
                    UseShellExecute = true,
                    WorkingDirectory = folder
                });
#endif
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Claude Code",
                    "Could not open Terminal automatically.\n\n" +
                    "Prompt has been copied to your clipboard — paste it into Claude Code manually.\n\n" +
                    "Error: " + ex.Message,
                    "OK");
            }
        }
    }

    public enum ReviewDecision { None, Applied, Skipped }
}