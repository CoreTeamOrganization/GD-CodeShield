// SolidReportExporter.cs
// Two export modes:
//   Export(SolidReport)              — full project summary PDF
//   ExportFile(FileAnalysisResult)   — per-file detailed PDF matching DriveToDeliver format

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SolidAgent
{
    public static class SolidReportExporter
    {
        private static string OutputFolder()
        {
            string f = Path.Combine(Application.dataPath, "..", "SolidReports");
            Directory.CreateDirectory(f);
            return f;
        }

        // ── Full project summary PDF ──────────────────────────────────────────────

        public static string Export(SolidReport report)
        {
            string ts   = report.GeneratedAt.ToString("yyyy-MM-dd_HH-mm");
            string path = Path.Combine(OutputFolder(), $"SOLID_Report_{ts}.pdf");
            string proj = string.IsNullOrEmpty(report.ProjectName) ? "Unity Project" : report.ProjectName;

            var pdf = new PdfWriter();

            // ── Page 1: Summary ────────────────────────────────────────────────────
            pdf.BeginPage();
            pdf.YellowHeader($"SOLID Review — {proj}", report.GeneratedAt.ToString("MMMM dd, yyyy"));

            float y = 690;
            pdf.SectionLabel("FILES SCANNED", 40, y); pdf.BigValue(report.TotalFiles.ToString(), 40, y - 18);
            pdf.SectionLabel("TOTAL VIOLATIONS", 200, y); pdf.BigValue(report.TotalViolations.ToString(), 200, y - 18);
            pdf.SectionLabel("DATE", 360, y); pdf.SmallText(report.GeneratedAt.ToString("yyyy-MM-dd HH:mm"), 360, y - 14);
            y -= 40;

            pdf.Divider(y); y -= 16;

            // Overall score
            pdf.SectionLabel("OVERALL SCORE", 40, y); y -= 18;
            float[] oc = ScoreColor(report.OverallScore);
            pdf.ColorText($"{report.OverallScore:F1} / 5.0", 40, y, 36, true, oc);
            pdf.ColorText(report.OverallLabel.ToUpper(), 140, y + 8, 11, true, oc);
            y -= 14;
            pdf.Bar(40, y, 515, 8, oc, report.OverallScore / 5f);
            y -= 22;

            // Principle grid
            pdf.SectionLabel("PRINCIPLE RATINGS", 40, y); y -= 6;
            float gx = 40;
            foreach (var r in report.Ratings)
            {
                float[] col = ScoreColor(r.Score);
                float   cw  = 120f;
                pdf.FilledRect(gx, y - 56, cw - 4, 60, 0.13f, 0.13f, 0.13f);
                pdf.FilledRect(gx, y + 2,  cw - 4, 4,  col[0], col[1], col[2]);
                pdf.ColorText(r.Principle.ToString(), gx + 8, y - 8,  11, true,  col);
                pdf.ColorText(ScoreStars(r.Score),    gx + 8, y - 22, 11, false, col);
                pdf.ColorText(r.Label,                gx + 8, y - 36, 8,  false, col);
                pdf.SmallText($"{r.Violations} violations", gx + 8, y - 50, 0.5f);
                gx += cw;
            }
            y -= 70;

            // Rating guide
            pdf.Divider(y); y -= 14;
            pdf.SectionLabel("RATING GUIDE", 40, y); y -= 14;
            float lx = 40;
            float[] sc2 = { 5, 4, 3, 2, 1 };
            string[] sl = { "5 - Excellent", "4 - Very Good", "3 - Acceptable", "2 - Weak", "1 - Poor" };
            for (int i = 0; i < 5; i++)
            {
                float[] lc = ScoreColor(sc2[i]);
                pdf.FilledRect(lx, y - 2, 8, 8, lc[0], lc[1], lc[2]);
                pdf.SmallText(sl[i], lx + 12, y + 4, 0.75f);
                lx += 102f;
            }
            y -= 20;

            pdf.Divider(y); y -= 14;
            pdf.SmallText("This report is rule-based — no API key required.", 40, y, 0.7f);
            y -= 13;
            pdf.SmallText("For AI-generated code fixes open the SOLID Review tool and click Generate Fix.", 40, y, 0.55f);

            pdf.Footer();
            pdf.EndPage();

            // ── Page 2: Violation breakdown ────────────────────────────────────────
            pdf.BeginPage();
            pdf.YellowHeader("Violation Breakdown", "");
            y = 730;

            foreach (var r in report.Ratings)
            {
                if (y < 80) break;
                float[] col = ScoreColor(r.Score);
                pdf.FilledRect(40, y - 2, 515, 22, 0.13f, 0.13f, 0.13f);
                pdf.FilledRect(40, y - 2, 4,   22, col[0], col[1], col[2]);
                pdf.ColorText($"{r.Principle}  —  Score: {r.Score}/5  —  {r.Label}", 52, y + 10, 10, true, col);
                pdf.SmallText($"{r.Violations} violation(s)", 460, y + 10, 0.65f);
                y -= 26;
                pdf.SmallText(r.Reason, 52, y + 8, 0.72f);
                y -= 16;
                pdf.Bar(52, y, 460, 4, col, r.Score / 5f);
                y -= 14;

                foreach (var v in report.FileResults.SelectMany(f => f.Violations)
                    .Where(v => v.Principle == r.Principle).Take(6))
                {
                    if (y < 80) break;
                    float[] sc3 = SevColor(v.Severity);
                    pdf.FilledRect(56, y + 2, 5, 5, sc3[0], sc3[1], sc3[2]);
                    string t = v.Title.Length > 70 ? v.Title.Substring(0, 70) + "..." : v.Title;
                    pdf.SmallText(t, 68, y + 8, 0.85f);
                    pdf.SmallText(v.Location.FileName + " line " + v.Location.StartLine, 68, y, 0.5f);
                    y -= 18;
                }

                int rem = r.Violations - report.FileResults.SelectMany(f => f.Violations)
                    .Count(v => v.Principle == r.Principle && true);
                y -= 8;
                pdf.Divider(y + 4);
                y -= 8;
            }

            pdf.Footer();
            pdf.EndPage();
            pdf.Save(path);
            return path;
        }

        // ── Per-file detailed PDF — matches DriveToDeliver format ──────────────────

        public static string ExportFile(FileAnalysisResult file, SolidReport report)
        {
            string name = Path.GetFileNameWithoutExtension(file.FileName);
            string ts   = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            string path = Path.Combine(OutputFolder(), $"SOLID_{name}_{ts}.pdf");

            var pdf = new PdfWriter();

            // Group violations by principle
            var byPrinciple = new Dictionary<SolidPrinciple, List<Violation>>();
            foreach (SolidPrinciple p in System.Enum.GetValues(typeof(SolidPrinciple)))
                byPrinciple[p] = file.Violations.Where(v => v.Principle == p).ToList();

            // Compute per-principle scores for this file
            var fileRatings = new Dictionary<SolidPrinciple, PrincipleRating>();
            if (report != null)
            {
                // Temporary single-file report
                var tmpResult = new List<FileAnalysisResult> { file };
                var tmpReport = RatingEngine.GenerateReport(tmpResult, name);
                foreach (var r in tmpReport.Ratings)
                    fileRatings[r.Principle] = r;
            }

            // ── Page 1: Cover + Scores at a Glance ────────────────────────────────
            pdf.BeginPage();
            pdf.YellowHeader($"SOLID Review — {name}", System.DateTime.Now.ToString("MMMM dd, yyyy"));

            float y = 700;

            // Scores at a Glance table
            pdf.SectionLabel("SCORES AT A GLANCE", 40, y); y -= 18;

            // Table header
            pdf.FilledRect(40, y - 2, 515, 20, 0.18f, 0.18f, 0.18f);
            pdf.BoldText("Principle", 48, y + 10, 9, new float[]{ 1f, 0.816f, 0f });
            pdf.BoldText("Score",     220, y + 10, 9, new float[]{ 1f, 0.816f, 0f });
            pdf.BoldText("Summary",   290, y + 10, 9, new float[]{ 1f, 0.816f, 0f });
            y -= 24;

            foreach (SolidPrinciple p in System.Enum.GetValues(typeof(SolidPrinciple)))
            {
                fileRatings.TryGetValue(p, out var rating);
                int    score  = rating?.Score  ?? 5;
                string label  = rating?.Label  ?? "Excellent";
                string reason = rating?.Reason ?? "No violations found.";
                float[] col   = ScoreColor(score);

                pdf.FilledRect(40, y - 4, 515, 22, 0.12f, 0.12f, 0.12f);
                pdf.FilledRect(40, y - 4, 3, 22, col[0], col[1], col[2]);

                string pName = PrincipleName(p);
                pdf.SmallText(pName, 48, y + 8, 0.9f);
                pdf.ColorText($"{score} / 5", 220, y + 8, 10, true, col);

                string shortReason = reason.Length > 50 ? reason.Substring(0, 50) + "..." : reason;
                pdf.SmallText(shortReason, 290, y + 8, 0.75f);
                y -= 26;
            }

            y -= 10;
            pdf.Divider(y);
            y -= 20;

            // Overall for this file
            if (report != null)
            {
                float fileAvg = fileRatings.Values.Count > 0
                    ? (float)fileRatings.Values.Sum(r => r.Score) / fileRatings.Values.Count
                    : 5f;
                string fileLabel = fileAvg >= 4.5f ? "Excellent" : fileAvg >= 3.5f ? "Very Good"
                    : fileAvg >= 2.5f ? "Acceptable" : fileAvg >= 1.5f ? "Weak" : "Poor";
                float[] oc2 = ScoreColor(fileAvg);
                pdf.SectionLabel("OVERALL FILE SCORE", 40, y); y -= 18;
                pdf.ColorText($"{fileAvg:F1} / 5.0  —  {fileLabel}", 40, y, 18, true, oc2);
                y -= 16;
                pdf.Bar(40, y, 515, 6, oc2, fileAvg / 5f);
                y -= 20;
            }

            pdf.Footer();
            pdf.EndPage();

            // ── Pages per principle ────────────────────────────────────────────────
            foreach (SolidPrinciple p in System.Enum.GetValues(typeof(SolidPrinciple)))
            {
                var viols = byPrinciple[p];
                fileRatings.TryGetValue(p, out var pRating);
                int    score = pRating?.Score ?? (viols.Count == 0 ? 5 : 3);
                float[] col  = ScoreColor(score);

                pdf.BeginPage();

                // Principle header
                pdf.FilledRect(0, 762, 595, 80, col[0] * 0.8f, col[1] * 0.8f, col[2] * 0.8f);
                pdf.BoldText(PrincipleName(p), 40, 818, 20, new float[]{ 0.05f, 0.05f, 0.05f });
                pdf.SmallText(PrincipleRule(p), 40, 796, 0.15f);
                pdf.FilledRect(0, 0, 595, 762, 0.086f, 0.086f, 0.086f);

                // Score badge
                pdf.FilledRect(460, 770, 95, 68, 0.1f, 0.1f, 0.1f);
                pdf.ColorText($"{score}/5", 480, 820, 28, true, col);
                pdf.ColorText(pRating?.Label ?? "", 472, 800, 9, false, col);

                y = 740;

                if (viols.Count == 0)
                {
                    // No violations — green pass
                    pdf.FilledRect(40, y - 30, 515, 40, 0.12f, 0.2f, 0.12f);
                    pdf.FilledRect(40, y - 30, 4, 40, col[0], col[1], col[2]);
                    pdf.ColorText("No violations found in this file.", 52, y - 5, 12, true, col);
                    y -= 50;
                }
                else
                {
                    // ── THE PROBLEM ─────────────────────────────────────────────
                    pdf.BoldText("THE PROBLEM", 40, y, 10, new float[]{ 1f, 0.816f, 0f });
                    y -= 18;

                    foreach (var v in viols)
                    {
                        if (y < 80) break;

                        // Violation title block
                        pdf.FilledRect(40, y - 4, 515, 22, 0.13f, 0.13f, 0.13f);
                        float[] sc3 = SevColor(v.Severity);
                        pdf.FilledRect(40, y - 4, 4, 22, sc3[0], sc3[1], sc3[2]);
                        pdf.BoldText(v.Title, 52, y + 8, 10, new float[]{ 0.95f, 0.95f, 0.95f });
                        pdf.SmallText(v.Location.FileName + "  line " + v.Location.StartLine, 460, y + 8, 0.55f);
                        y -= 26;

                        // Description
                        var descLines = WrapText(v.Description, 90);
                        foreach (var line in descLines)
                        {
                            if (y < 80) break;
                            pdf.SmallText(line, 52, y, 0.8f);
                            y -= 14;
                        }

                        // Evidence
                        if (!string.IsNullOrEmpty(v.Evidence))
                        {
                            pdf.FilledRect(52, y - 4, 490, 18, 0.15f, 0.13f, 0.05f);
                            pdf.SmallText("Evidence: " + v.Evidence, 60, y + 4, 0.9f);
                            y -= 22;
                        }

                        y -= 6;
                    }

                    // ── WHAT TO FIX ──────────────────────────────────────────────
                    if (y > 120)
                    {
                        pdf.Divider(y); y -= 14;
                        pdf.BoldText("WHAT TO FIX", 40, y, 10, new float[]{ 1f, 0.816f, 0f });
                        y -= 16;

                        // Static guidance note — no API needed
                        pdf.FilledRect(40, y - 4, 515, 18, 0.12f, 0.14f, 0.10f);
                        pdf.FilledRect(40, y - 4, 3, 18, 0.31f, 0.78f, 0.39f);
                        pdf.SmallText("These are rule-based suggestions. No API key needed. For AI-generated code fixes open the tool and click Generate Fix.", 52, y + 4, 0.75f);
                        y -= 24;

                        // Fix table header
                        pdf.FilledRect(40, y - 2, 515, 18, 0.18f, 0.18f, 0.18f);
                        pdf.BoldText("File",        48,  y + 8, 8, new float[]{ 1f, 0.816f, 0f });
                        pdf.BoldText("What to do",  200, y + 8, 8, new float[]{ 1f, 0.816f, 0f });
                        y -= 22;

                        foreach (var v in viols)
                        {
                            if (y < 80) break;
                            pdf.FilledRect(40, y - 4, 515, 22, 0.12f, 0.12f, 0.12f);
                            pdf.FilledRect(40, y - 4, 3, 22, col[0], col[1], col[2]);
                            pdf.SmallText(v.Location.FileName, 48, y + 8, 0.85f);
                            // Use principle-based static guidance instead of raw evidence
                            string guidance = StaticGuidance(p, v);
                            string guidanceShort = guidance.Length > 58 ? guidance.Substring(0, 58) + "..." : guidance;
                            pdf.SmallText(guidanceShort, 200, y + 8, 0.75f);
                            y -= 26;
                        }
                    }
                }

                // Score bar at bottom of principle section
                if (y > 60)
                {
                    pdf.Divider(y); y -= 12;
                    pdf.Bar(40, y, 515, 6, col, score / 5f);
                    pdf.ColorText($"Score: {score}/5 — {pRating?.Label ?? ""}", 40, y - 14, 9, false, col);
                }

                pdf.Footer();
                pdf.EndPage();
            }

            // ── Priority page ─────────────────────────────────────────────────────
            if (file.Violations.Count > 0)
            {
                pdf.BeginPage();
                pdf.YellowHeader("What to Fix First", "");
                pdf.FilledRect(0, 0, 595, 762, 0.086f, 0.086f, 0.086f);

                y = 720;
                pdf.SectionLabel("IN ORDER OF IMPACT", 40, y); y -= 20;

                // Table header
                pdf.FilledRect(40, y - 2, 515, 20, 0.18f, 0.18f, 0.18f);
                pdf.BoldText("#",     48,  y + 10, 9, new float[]{ 1f, 0.816f, 0f });
                pdf.BoldText("File",  72,  y + 10, 9, new float[]{ 1f, 0.816f, 0f });
                pdf.BoldText("Issue", 230, y + 10, 9, new float[]{ 1f, 0.816f, 0f });
                pdf.BoldText("Sev",   510, y + 10, 9, new float[]{ 1f, 0.816f, 0f });
                y -= 24;

                // Sort by severity: High first
                var sorted = file.Violations
                    .OrderByDescending(v => (int)v.Severity)
                    .ToList();

                for (int i = 0; i < sorted.Count && y > 80; i++)
                {
                    var v = sorted[i];
                    float[] sc3 = SevColor(v.Severity);
                    pdf.FilledRect(40, y - 4, 515, 22, 0.12f, 0.12f, 0.12f);
                    pdf.FilledRect(40, y - 4, 3, 22, sc3[0], sc3[1], sc3[2]);
                    pdf.SmallText((i + 1).ToString(), 48, y + 8, 0.75f);
                    pdf.SmallText(v.Location.FileName, 72, y + 8, 0.85f);
                    string issue = v.Title.Length > 42 ? v.Title.Substring(0, 42) + "..." : v.Title;
                    pdf.SmallText(issue, 230, y + 8, 0.75f);
                    pdf.ColorText(v.Severity.ToString(), 505, y + 8, 8, true, sc3);
                    y -= 26;
                }

                pdf.Footer();
                pdf.EndPage();
            }

            pdf.Save(path);
            return path;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static string StaticGuidance(SolidPrinciple p, Violation v) => p switch
        {
            SolidPrinciple.SRP =>
                "Split into focused classes — one responsibility per class. Use GetComponent to wire them.",
            SolidPrinciple.OCP =>
                "Replace type switch/if-else with an interface or base class. New types extend, not modify.",
            SolidPrinciple.LSP =>
                "Remove NotImplementedException — implement the method or split the interface (see ISP).",
            SolidPrinciple.ISP =>
                "Split the fat interface into smaller focused ones. Classes implement only what they need.",
            _ => "Review and refactor to follow the principle."
        };

        private static string PrincipleName(SolidPrinciple p) => p switch
        {
            SolidPrinciple.SRP => "S — Single Responsibility",
            SolidPrinciple.OCP => "O — Open / Closed",
            SolidPrinciple.LSP => "L — Liskov Substitution",
            SolidPrinciple.ISP => "I — Interface Segregation",
            _ => p.ToString()
        };

        private static string PrincipleRule(SolidPrinciple p) => p switch
        {
            SolidPrinciple.SRP => "Rule: one class = one job.",
            SolidPrinciple.OCP => "Rule: add new stuff without changing old code.",
            SolidPrinciple.LSP => "Rule: subclasses should work in place of their parent.",
            SolidPrinciple.ISP => "Rule: don't force a class to depend on stuff it doesn't need.",
            _ => ""
        };

        private static string ScoreStars(int score)
        {
            string s = "";
            for (int i = 1; i <= 5; i++) s += i <= score ? "*" : "-";
            return s;
        }

        private static float[] ScoreColor(float score)
        {
            if (score >= 4.5f) return new float[]{ 0.31f, 0.78f, 0.39f };
            if (score >= 3.5f) return new float[]{ 0.18f, 0.65f, 0.95f };
            if (score >= 2.5f) return new float[]{ 1f,    0.75f, 0f };
            if (score >= 1.5f) return new float[]{ 1f,    0.49f, 0.15f };
            return new float[]{ 0.86f, 0.24f, 0.24f };
        }

        private static float[] SevColor(Severity s) => s switch
        {
            Severity.High   => new float[]{ 0.86f, 0.24f, 0.24f },
            Severity.Medium => new float[]{ 1f,    0.7f,  0f },
            _               => new float[]{ 0.31f, 0.78f, 0.39f }
        };

        private static List<string> WrapText(string text, int maxChars)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            var words = text.Split(' ');
            var line  = new StringBuilder();
            foreach (var word in words)
            {
                if (line.Length + word.Length + 1 > maxChars)
                {
                    lines.Add(line.ToString().TrimEnd());
                    line.Clear();
                }
                line.Append(word + " ");
            }
            if (line.Length > 0) lines.Add(line.ToString().TrimEnd());
            return lines;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  PDF WRITER — valid PDF output, no external deps
    // ════════════════════════════════════════════════════════════════════════════

    public class PdfWriter
    {
        private readonly StringBuilder _all   = new StringBuilder();
        private readonly List<int>     _xref  = new List<int>();
        private readonly List<int>     _pages = new List<int>();
        private readonly List<string>  _objs  = new StringBuilder[0] is var _ ? new List<string>() : new List<string>();
        private StringBuilder          _cs    = new StringBuilder();

        private int AddObj(string body) { _objs.Add(body); return _objs.Count; }

        public void BeginPage() { _cs = new StringBuilder(); }

        public void EndPage()
        {
            string content = _cs.ToString();
            int ci = AddObj($"<< /Length {Encoding.UTF8.GetByteCount(content)} >>\nstream\n{content}\nendstream");
            int pi = AddObj(""); // placeholder
            _objs[pi - 1] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] " +
                             $"/Contents {ci + 4} 0 R " +
                             $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>";
            _pages.Add(pi + 4);
        }

        public void Save(string path)
        {
            var sb  = new StringBuilder();
            sb.Append("%PDF-1.4\n");

            var offsets = new List<int>();

            void WriteObj(int n, string body)
            {
                offsets.Add(sb.Length);
                sb.Append($"{n} 0 obj\n{body}\nendobj\n");
            }

            string kids = string.Join(" ", _pages.Select(p => $"{p} 0 R"));
            WriteObj(1, "<< /Type /Catalog /Pages 2 0 R >>");
            WriteObj(2, $"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>");
            WriteObj(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            WriteObj(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

            for (int i = 0; i < _objs.Count; i++)
                WriteObj(5 + i, _objs[i]);

            int xpos = sb.Length;
            int total = 4 + _objs.Count;
            sb.Append($"xref\n0 {total + 1}\n");
            sb.Append("0000000000 65535 f \n");
            foreach (var o in offsets)
                sb.Append($"{o:D10} 00000 n \n");
            sb.Append($"trailer\n<< /Size {total + 1} /Root 1 0 R >>\n");
            sb.Append($"startxref\n{xpos}\n%%EOF\n");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // ── Drawing helpers ───────────────────────────────────────────────────────

        public void YellowHeader(string title, string subtitle)
        {
            FilledRect(0, 762, 595, 80, 1.0f, 0.816f, 0f);
            PdfText(title,    40, 818, 20, true,  0.05f, 0.05f, 0.05f);
            if (!string.IsNullOrEmpty(subtitle))
                PdfText(subtitle, 40, 797, 10, false, 0.15f, 0.15f, 0.15f);
            FilledRect(0, 0, 595, 762, 0.086f, 0.086f, 0.086f);
        }

        public void SectionLabel(string text, float x, float y)
            => PdfText(text, x, y, 8, true, 1f, 0.816f, 0f);

        public void BoldText(string text, float x, float y, float size, float[] col)
            => PdfText(text, x, y, size, true, col[0], col[1], col[2]);

        public void ColorText(string text, float x, float y, float size, bool bold, float[] col)
            => PdfText(text, x, y, size, bold, col[0], col[1], col[2]);

        public void BigValue(string text, float x, float y)
            => PdfText(text, x, y, 18, true, 0.95f, 0.95f, 0.95f);

        public void SmallText(string text, float x, float y, float brightness = 0.8f)
            => PdfText(text, x, y, 9, false, brightness, brightness, brightness);

        public void Divider(float y)
            => FilledRect(40, y, 515, 1, 0.3f, 0.3f, 0.3f);

        public void Bar(float x, float y, float w, float h, float[] col, float pct)
        {
            FilledRect(x, y, w, h, 0.2f, 0.2f, 0.2f);
            if (pct > 0) FilledRect(x, y, w * pct, h, col[0], col[1], col[2]);
        }

        public void Footer()
        {
            FilledRect(0, 0, 595, 28, 0.067f, 0.067f, 0.067f);
            PdfText("GAME DISTRICT  -  SOLID REVIEW TOOL", 40, 11, 8, false, 1f, 0.816f, 0f);
            PdfText("AI can make mistakes. Always review before applying.", 300, 11, 7, false, 0.4f, 0.4f, 0.4f);
        }

        public void FilledRect(float x, float y, float w, float h, float r, float g, float b)
            => _cs.Append($"{r:F3} {g:F3} {b:F3} rg {x:F1} {y:F1} {w:F1} {h:F1} re f\n");

        private void PdfText(string text, float x, float y, float size, bool bold,
                              float r, float g, float b)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if (c == '(' || c == ')' || c == '\\') sb.Append('\\');
                sb.Append(c > 127 ? '?' : c);
            }
            string font = bold ? "/F2" : "/F1";
            _cs.Append($"BT {font} {size:F1} Tf {r:F3} {g:F3} {b:F3} rg {x:F1} {y:F1} Td ({sb}) Tj ET\n");
        }
    }
}
