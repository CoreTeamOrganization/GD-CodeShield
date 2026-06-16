// Editor/SolidReview/SolidReportPreviewWindow.cs
// In-editor visual preview of a SOLID scan report — modelled on Unity's Memory
// Profiler "Summary" tab: titled sections, stacked distribution bars, and legend
// rows with counts + percentages. Opened from the "Preview report" button on the
// SOLID Review results screen, alongside "Download Word report".
//
// Read-only. Renders from the in-memory SolidReport; no files written. A
// "Download Word report" shortcut is offered in the header so preview → export
// is one click.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GDCodeShield.Brand;

namespace SolidAgent
{
    public class SolidReportPreviewWindow : EditorWindow
    {
        private SolidReport _report;
        private Vector2 _scroll;
        private float _contentH = 1200f;

        private GUIStyle _sH1, _sH2, _sBody, _sMuted, _sEyebrow, _sLegend, _sLegendVal;
        private bool _stylesReady;

        // Principle → colour (stable across the window)
        private static readonly Dictionary<SolidPrinciple, Color> PrincipleColors =
            new Dictionary<SolidPrinciple, Color>
        {
            { SolidPrinciple.SRP, new Color32(14,  26,  51,  255) }, // Navy
            { SolidPrinciple.OCP, new Color32(244, 196, 48,  255) }, // Gold
            { SolidPrinciple.LSP, new Color32(133, 183, 235, 255) }, // Sky
            { SolidPrinciple.ISP, new Color32(111, 167, 111, 255) }, // Shipped/green
        };

        public static void Open(SolidReport report)
        {
            var w = GetWindow<SolidReportPreviewWindow>(true, "  SOLID Report Preview", true);
            w._report = report;
            w.minSize = new Vector2(840, 600);
            w._stylesReady = false;
            w.Show();
        }

        private void OnInspectorUpdate() => Repaint();

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            var fraunces = BrandTokens.Fraunces;
            var inter    = BrandTokens.Inter;

            _sH1        = BrandTokens.MakeStyle(fraunces, BrandTokens.SizeH1, BrandTokens.Navy, FontStyle.Bold);
            _sH2        = BrandTokens.MakeStyle(fraunces, BrandTokens.SizeH3, BrandTokens.Navy, FontStyle.Bold);
            _sBody      = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Ink);
            _sMuted     = BrandTokens.MakeWrappedStyle(inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray);
            _sEyebrow   = BrandTokens.MakeStyle(inter, BrandTokens.SizeEyebrow, BrandTokens.Navy, FontStyle.Bold);
            _sLegend    = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Ink);
            _sLegendVal = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Ink, FontStyle.Bold, TextAnchor.MiddleRight);
        }

        private void OnGUI()
        {
            EnsureStyles();
            float W = position.width, H = position.height;

            BrandTokens.Fill(new Rect(0, 0, W, H), BrandTokens.Cream);
            BrandTokens.Fill(new Rect(0, 0, BrandTokens.GoldBarWidth, H), BrandTokens.Gold);

            float padL = BrandTokens.GoldBarWidth + 30, padR = 36;

            if (_report == null)
            {
                GUI.Label(new Rect(padL, H / 2 - 16, W - padL - padR, 24),
                    "No report to preview. Run a scan first.",
                    BrandTokens.MakeStyle(BrandTokens.FrauncesItalic ?? BrandTokens.Fraunces,
                        16, BrandTokens.WarmGray, FontStyle.Italic, TextAnchor.MiddleCenter));
                return;
            }

            // ── Header ──────────────────────────────────────────────────────────
            DrawEyebrow(padL, 22, "SUMMARY");
            GUI.Label(new Rect(padL, 44, W - padL - padR - 330, 44), "Report preview.", _sH1);
            string sub = $"{(_report.ProjectName ?? "Unity Project")}  ·  generated {_report.GeneratedAt:yyyy-MM-dd HH:mm}";
            GUI.Label(new Rect(padL, 86, W - padL - padR, 16), sub,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray));

            // Download shortcuts top-right — Word (.docx) + HTML (.html)
            var wordR = new Rect(W - padR - 310, 40, 150, 30);
            var htmlR = new Rect(W - padR - 150, 40, 150, 30);
            if (Btn(wordR, "↓  Word (.docx)")) DownloadWord();
            if (Btn(htmlR, "↓  HTML (.html)")) DownloadHtml();

            BrandTokens.HairlineH(padL, 108, W - padL - padR, BrandTokens.Taupe);

            // ── Scrollable body ─────────────────────────────────────────────────
            float top = 120f, padB = 16f;
            var view = new Rect(padL, top, W - padL - padR, H - top - padB);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 16, _contentH));

            float x = 0, y = 0, w = view.width - 16;

            DrawOverallSection(x, ref y, w);
            DrawScoresByPrinciple(x, ref y, w);
            DrawPrincipleDistribution(x, ref y, w);
            DrawSeverityDistribution(x, ref y, w);
            DrawTopFiles(x, ref y, w);

            if (Mathf.Abs(_contentH - y) > 2f) { _contentH = y; Repaint(); }

            GUI.EndScrollView();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            { Close(); Event.current.Use(); }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SECTIONS
        // ═══════════════════════════════════════════════════════════════════════

        // Overall score — single fill bar (like "Memory Usage On Device").
        private void DrawOverallSection(float x, ref float y, float w)
        {
            DrawSectionHeader(x, ref y, w, "Overall SOLID Score",
                "Average of the four principle scores, 1–5. Target is 4.0.");

            float score = _report.OverallScore;
            float frac  = Mathf.Clamp01(score / 5f);
            Color sc    = score >= 4f ? BrandTokens.Shipped : score >= 3f ? BrandTokens.Gold : BrandTokens.Overdue;

            float barH = 26;
            BrandTokens.Fill(new Rect(x, y, w, barH), new Color(0, 0, 0, 0.06f));
            BrandTokens.Fill(new Rect(x, y, w * frac, barH), sc);
            y += barH + 4;

            GUI.Label(new Rect(x, y, w * 0.6f, 14), $"{score:F1} / 5  ·  {_report.OverallLabel}",
                BrandTokens.MakeStyle(BrandTokens.Inter, 11, sc, FontStyle.Bold));
            GUI.Label(new Rect(x, y, w, 14),
                $"{_report.TotalViolations} violations  ·  {_report.TotalFiles} files",
                BrandTokens.MakeStyle(BrandTokens.Inter, 10, BrandTokens.WarmGray, FontStyle.Normal, TextAnchor.MiddleRight));
            y += 30;
        }

        // Per-principle score bars (1–5 each).
        private void DrawScoresByPrinciple(float x, ref float y, float w)
        {
            DrawSectionHeader(x, ref y, w, "Scores by Principle",
                "Each principle scored 1–5 from the violations found.");

            if (_report.Ratings == null || _report.Ratings.Count == 0)
            {
                GUI.Label(new Rect(x, y, w, 16), "No ratings.", _sMuted);
                y += 24;
            }
            else
            {
                foreach (var r in _report.Ratings)
                {
                    Color c = r.Score >= 4 ? BrandTokens.Shipped : r.Score >= 3 ? BrandTokens.Gold : BrandTokens.Overdue;
                    GUI.Label(new Rect(x, y, 60, 16), r.Principle.ToString(),
                        BrandTokens.MakeStyle(BrandTokens.Fraunces, 13, BrandTokens.Navy, FontStyle.Bold));

                    float bx = x + 70, bw = w - 70 - 150;
                    BrandTokens.Fill(new Rect(bx, y + 3, bw, 10), new Color(0, 0, 0, 0.06f));
                    BrandTokens.Fill(new Rect(bx, y + 3, bw * (r.Score / 5f), 10), c);

                    GUI.Label(new Rect(x + w - 150, y, 150, 16), $"{r.Score}/5  ·  {r.Label}",
                        BrandTokens.MakeStyle(BrandTokens.Inter, 11, c, FontStyle.Bold, TextAnchor.MiddleRight));

                    BrandTokens.HairlineH(x, y + 22, w, BrandTokens.Taupe);
                    y += 26;
                }
            }
            y += 18;
        }

        // Violations split by principle (like "Allocated Memory Distribution").
        private void DrawPrincipleDistribution(float x, ref float y, float w)
        {
            var items = (_report.Ratings ?? new List<PrincipleRating>())
                .Select(r => (r.Principle.ToString(), r.Violations,
                    PrincipleColors.TryGetValue(r.Principle, out var c) ? c : BrandTokens.WarmGray))
                .ToList();

            DrawDistribution(x, ref y, w, "Violation Distribution by Principle",
                "How the total violations break down across SRP / OCP / LSP / ISP.",
                items, "violations");
        }

        // Violations split by severity (like "Managed Heap Utilization").
        private void DrawSeverityDistribution(float x, ref float y, float w)
        {
            var all = _report.FileResults.SelectMany(f => f.Violations).ToList();
            var items = new List<(string, int, Color)>
            {
                ("High severity",   all.Count(v => v.Severity == Severity.High),   BrandTokens.Overdue),
                ("Medium severity", all.Count(v => v.Severity == Severity.Medium), BrandTokens.Gold),
                ("Low severity",    all.Count(v => v.Severity == Severity.Low),    BrandTokens.Sky),
            };
            DrawDistribution(x, ref y, w, "Severity Breakdown",
                "Severity weighting of every violation found in the scan.",
                items, "violations");
        }

        // Top files by violation count (like "Top Unity Objects Categories").
        private void DrawTopFiles(float x, ref float y, float w)
        {
            var withV = _report.FileResults
                .Where(f => f.Violations.Count > 0)
                .OrderByDescending(f => f.Violations.Count)
                .ToList();

            var palette = new[]
            {
                BrandTokens.Navy, BrandTokens.Gold, BrandTokens.Sky,
                BrandTokens.Shipped, BrandTokens.Overdue, new Color(0.55f, 0.55f, 0.52f, 1f)
            };

            var items = new List<(string, int, Color)>();
            int take = Mathf.Min(6, withV.Count);
            for (int i = 0; i < take; i++)
                items.Add((withV[i].FileName, withV[i].Violations.Count, palette[i % palette.Length]));

            int others = withV.Skip(take).Sum(f => f.Violations.Count);
            if (others > 0)
                items.Add(($"Others ({withV.Count - take} files)", others, new Color(0.72f, 0.72f, 0.68f, 1f)));

            DrawDistribution(x, ref y, w, "Top Files by Violations",
                "Which files carry the most violations — fix these first.",
                items, "violations");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  PRIMITIVES
        // ═══════════════════════════════════════════════════════════════════════

        // Stacked bar + legend rows with count and percentage. Empty → green "None".
        private void DrawDistribution(float x, ref float y, float w, string title, string desc,
            List<(string label, int value, Color color)> items, string unit)
        {
            DrawSectionHeader(x, ref y, w, title, desc);

            int total = items.Sum(i => i.value);
            float barH = 26;

            if (total <= 0)
            {
                BrandTokens.Fill(new Rect(x, y, w, barH), BrandTokens.ShippedTint);
                GUI.Label(new Rect(x, y + 4, w, 18), "None found",
                    BrandTokens.MakeStyle(BrandTokens.Inter, 11, BrandTokens.Shipped, FontStyle.Bold, TextAnchor.MiddleCenter));
            }
            else
            {
                float cx = x;
                foreach (var it in items)
                {
                    if (it.value <= 0) continue;
                    float sw = w * ((float)it.value / total);
                    BrandTokens.Fill(new Rect(cx, y, sw, barH), it.color);
                    cx += sw;
                }
            }
            y += barH + 4;
            GUI.Label(new Rect(x, y, w, 14), $"Total: {total} {unit}",
                BrandTokens.MakeStyle(BrandTokens.Inter, 10, BrandTokens.WarmGray, FontStyle.Normal, TextAnchor.MiddleRight));
            y += 22;

            foreach (var it in items)
            {
                BrandTokens.Fill(new Rect(x, y + 4, 11, 11), it.color);
                GUI.Label(new Rect(x + 22, y, w - 180, 16), it.label, _sLegend);

                string pct = total > 0 ? $"  ({Mathf.RoundToInt(100f * it.value / total)}%)" : "";
                GUI.Label(new Rect(x + w - 180, y, 180, 16), it.value + pct, _sLegendVal);

                BrandTokens.HairlineH(x, y + 22, w, BrandTokens.Taupe);
                y += 24;
            }
            y += 18;
        }

        private void DrawSectionHeader(float x, ref float y, float w, string title, string desc)
        {
            GUI.Label(new Rect(x, y, w, 26), title, _sH2);
            y += 28;
            if (!string.IsNullOrEmpty(desc))
            {
                float dh = _sMuted.CalcHeight(new GUIContent(desc), w);
                GUI.Label(new Rect(x, y, w, dh), desc, _sMuted);
                y += dh + 10;
            }
        }

        private void DrawEyebrow(float x, float y, string label)
        {
            BrandTokens.Fill(new Rect(x, y + 4, BrandTokens.EyebrowSquare, BrandTokens.EyebrowSquare), BrandTokens.Gold);
            string spaced = "";
            for (int i = 0; i < label.Length; i++)
            {
                spaced += label[i];
                if (i < label.Length - 1) spaced += " ";
            }
            GUI.Label(new Rect(x + BrandTokens.EyebrowSquare + 10, y, 300, 18), spaced, _sEyebrow);
        }

        private bool Btn(Rect r, string label)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            BrandTokens.Outline(r, hover ? BrandTokens.Navy : BrandTokens.Taupe);
            if (hover) BrandTokens.Fill(r, BrandTokens.GoldTint);
            GUI.Label(r, label,
                BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody,
                    hover ? BrandTokens.Navy : BrandTokens.Ink, FontStyle.Bold, TextAnchor.MiddleCenter));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            { Event.current.Use(); return true; }
            return false;
        }

        private void DownloadWord() => ExportAndOpen(() => SolidReportExporter.Export(_report), "Word");
        private void DownloadHtml() => ExportAndOpen(() => SolidReportExporter.ExportHtml(_report), "HTML");

        private void ExportAndOpen(System.Func<string> exporter, string kind)
        {
            if (_report == null) return;
            try
            {
                string path = exporter();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    System.Diagnostics.Process.Start(path);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Export failed",
                    $"Could not generate {kind} report.\n\n" + ex.Message, "OK");
            }
        }
    }
}
