// SolidReportExporter.cs
// Generates .docx reports via solid_report.js (Node.js + docx npm package).
// Two export modes:
//   Export(SolidReport)              — full project summary .docx
//   ExportFile(FileAnalysisResult)   — per-file detailed .docx

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

        private static string ScriptPath()
        {
            // 1. Use AssetDatabase — works for Assets/ and Packages/ installs alike
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("solid_report");
            foreach (string guid in guids)
            {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith("solid_report.js", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Convert project-relative path to absolute
                    string abs = Path.GetFullPath(Path.Combine(
                        Application.dataPath, "..", p));
                    if (File.Exists(abs)) return abs;
                }
            }
#endif
            // 2. Derive path from the location of this .cs file at compile time
            //    SolidReportExporter.cs lives in   …/Editor/SolidReview/
            //    solid_report.js lives in           …/Editor/SolidReview/DocxGen/
            string thisFile = GetThisFilePath();   // set by [CallerFilePath] below
            if (!string.IsNullOrEmpty(thisFile))
            {
                string candidate = Path.Combine(
                    Path.GetDirectoryName(thisFile), "DocxGen", "solid_report.js");
                if (File.Exists(candidate)) return candidate;
            }

            // 3. Walk up from Application.dataPath — covers Assets/ installs
            foreach (string root in new[]
            {
                Application.dataPath,
                Path.Combine(Application.dataPath, "..", "Packages"),
                Path.Combine(Application.dataPath, "..", "Library", "PackageCache"),
            })
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    string[] found = Directory.GetFiles(root, "solid_report.js",
                        SearchOption.AllDirectories);
                    if (found.Length > 0) return found[0];
                }
                catch { /* skip inaccessible dirs */ }
            }

            throw new FileNotFoundException(
                "solid_report.js not found. " +
                "Make sure the DocxGen folder was included when you imported the package.");
        }

        // CallerFilePath gives us the absolute path of this source file at compile time.
        private static string GetThisFilePath(
            [System.Runtime.CompilerServices.CallerFilePath] string path = "")
            => path;

        private static string RunNode(string mode, string outputPath, string jsonData)
        {
            string jsonPath = Path.Combine(Path.GetTempPath(),
                $"solid_report_input_{Guid.NewGuid().ToString("N")}.json");
            File.WriteAllText(jsonPath, jsonData, new UTF8Encoding(false));

            try
            {
                string script   = ScriptPath();
                string nodePath = FindNodeExecutable();

                // Ensure docx is available — install into writable cache if needed
                EnsureDocxInstalled(script, nodePath);

                var psi = new ProcessStartInfo
                {
                    FileName               = nodePath,
                    Arguments              = $"\"{script}\" {mode} \"{outputPath}\" \"{jsonPath}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    if (proc.ExitCode != 0)
                        throw new Exception(
                            $"Node error (exit {proc.ExitCode}):\n{stderr}\n{stdout}");

                    if (!File.Exists(outputPath))
                        throw new Exception(
                            $"Output file not created.\nstdout: {stdout}\nstderr: {stderr}");

                    return outputPath;
                }
            }
            finally
            {
                if (File.Exists(jsonPath)) File.Delete(jsonPath);
            }
        }

        // Installs the docx npm package into Library/DocxGen/node_modules if not present.
        // Library/ is writable and persistent across reimports — perfect for npm cache.
        private static string _docxModulesPath = null;
        private static void EnsureDocxInstalled(string scriptPath, string nodePath)
        {
            if (_docxModulesPath != null && Directory.Exists(_docxModulesPath)) return;

            // Prefer bundled node_modules~ next to the script (committed to repo)
            string scriptDir   = Path.GetDirectoryName(scriptPath);
            string bundledPath = Path.Combine(scriptDir, "node_modules~");
            if (Directory.Exists(Path.Combine(bundledPath, "docx")))
            {
                _docxModulesPath = bundledPath;
                return;
            }
            // Also accept plain node_modules next to script
            string plainBundled = Path.Combine(scriptDir, "node_modules");
            if (Directory.Exists(Path.Combine(plainBundled, "docx")))
            {
                _docxModulesPath = plainBundled;
                return;
            }

            // Not bundled — install into Library/DocxGen/ (writable, persistent)
            string libraryDocxDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Library", "DocxGen"));
            string installedPath = Path.Combine(libraryDocxDir, "node_modules");

            if (Directory.Exists(Path.Combine(installedPath, "docx")))
            {
                _docxModulesPath = installedPath;
                return;
            }

            // Run npm install docx in Library/DocxGen/
            Directory.CreateDirectory(libraryDocxDir);

            string npmPath = FindNpmExecutable(nodePath);

            // On Windows npm is a .cmd batch wrapper — it cannot be launched directly
            // with UseShellExecute=false (CreateProcess rejects batch files with
            // "%1 is not a valid Win32 application"), so route it through cmd.exe /c.
            // On macOS/Linux npm is a normal executable script and launches directly.
            bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            var psi = new ProcessStartInfo
            {
                WorkingDirectory       = libraryDocxDir,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            if (isWindows)
            {
                psi.FileName  = "cmd.exe";
                // cmd /c strips the outermost quote pair, leaving "<npm path>" as the
                // command and the rest as its arguments — handles spaces in the path.
                psi.Arguments = $"/c \"\"{npmPath}\" install docx --prefix .\"";
            }
            else
            {
                psi.FileName  = npmPath;
                psi.Arguments = "install docx --prefix .";
            }
            // Inherit PATH so npm can find node
            string pathEnv = System.Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.EnvironmentVariables["PATH"] = pathEnv + Path.PathSeparator +
                "/usr/local/bin:/usr/bin:/opt/homebrew/bin:/opt/homebrew/opt/node/bin";

            using (var proc = Process.Start(psi))
            {
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0 || !Directory.Exists(Path.Combine(installedPath, "docx")))
                    throw new Exception(
                        $"Failed to install docx npm package.\n{stderr}\n{stdout}\n" +
                        "Make sure Node.js and npm are installed: https://nodejs.org");
            }

            _docxModulesPath = installedPath;
        }

        private static string FindNpmExecutable(string nodePath)
        {
            // npm lives in the same bin directory as node
            string nodeDir = Path.GetDirectoryName(nodePath) ?? "";

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // On Windows npm ships as npm.cmd (a batch wrapper). The extensionless
                // "npm" file in the same folder is a Unix shell script — launching it
                // throws "%1 is not a valid Win32 application", so prefer .cmd / .exe.
                foreach (var name in new[] { "npm.cmd", "npm.bat", "npm.exe" })
                {
                    string p = Path.Combine(nodeDir, name);
                    if (File.Exists(p)) return p;
                }
                return "npm.cmd"; // last resort — resolved via PATH by cmd.exe
            }

            // macOS / Linux — npm is an executable shell script
            string npmInSameDir = Path.Combine(nodeDir, "npm");
            if (File.Exists(npmInSameDir)) return npmInSameDir;

            // Fallback: common locations
            string[] candidates = {
                "/usr/local/bin/npm", "/usr/bin/npm",
                "/opt/homebrew/bin/npm", "/opt/homebrew/opt/node/bin/npm",
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;

            return "npm"; // last resort
        }

        private static string _cachedNodePath = null;
        private static string FindNodeExecutable()
        {
            if (_cachedNodePath != null) return _cachedNodePath;

            // On macOS/Linux Unity strips the user's shell PATH.
            // Check all common install locations explicitly.
            string[] candidates = {
                // Direct common paths
                "/usr/local/bin/node",
                "/usr/bin/node",
                "/opt/homebrew/bin/node",
                "/opt/homebrew/opt/node/bin/node",
                // nvm — scan all installed versions, pick the latest
            };

            // Add nvm paths dynamically
            string home = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.UserProfile);
            string nvmDir = Path.Combine(home, ".nvm", "versions", "node");
            if (Directory.Exists(nvmDir))
            {
                try
                {
                    var versions = Directory.GetDirectories(nvmDir)
                        .OrderByDescending(d => d)
                        .ToArray();
                    var nvmCandidates = versions
                        .Select(v => Path.Combine(v, "bin", "node"))
                        .ToArray();
                    candidates = nvmCandidates.Concat(candidates).ToArray();
                }
                catch { }
            }

            // Also check PATH entries explicitly
            string pathEnv = System.Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                string full = Path.Combine(dir.Trim(), "node");
                if (File.Exists(full))
                {
                    _cachedNodePath = full;
                    return full;
                }
                // Windows
                string fullExe = full + ".exe";
                if (File.Exists(fullExe))
                {
                    _cachedNodePath = fullExe;
                    return fullExe;
                }
            }

            foreach (string c in candidates)
                if (File.Exists(c)) { _cachedNodePath = c; return c; }

            // Last resort: just use 'node' and hope the OS finds it
            _cachedNodePath = "node";
            return "node";
        }

        // ── Full project summary ──────────────────────────────────────────────────
        public static string Export(SolidReport report)
        {
            string ts   = report.GeneratedAt.ToString("yyyy-MM-dd_HH-mm");
            string path = Path.Combine(OutputFolder(), $"SOLID_Report_{ts}.docx");
            string proj = string.IsNullOrEmpty(report.ProjectName) ? "Unity Project" : report.ProjectName;

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"projectName\":{JsonStr(proj)},");
            sb.Append($"\"gameName\":{JsonStr(PlayerSettings.productName ?? string.Empty)},");
            sb.Append($"\"bundleId\":{JsonStr(Application.identifier ?? string.Empty)},");
            sb.Append($"\"generatedAt\":{JsonStr(report.GeneratedAt.ToString("yyyy-MM-dd HH:mm"))},");
            sb.Append($"\"totalFiles\":{report.TotalFiles},");
            sb.Append($"\"totalViolations\":{report.TotalViolations},");
            sb.Append($"\"overallScore\":{report.OverallScore:F2},");
            sb.Append($"\"overallLabel\":{JsonStr(report.OverallLabel)},");
            sb.Append("\"ratings\":[");
            sb.Append(string.Join(",", report.Ratings.Select(r =>
                "{\"principle\":" + JsonStr(r.Principle.ToString()) +
                ",\"score\":" + r.Score +
                ",\"violations\":" + r.Violations +
                ",\"label\":" + JsonStr(r.Label) +
                ",\"reason\":" + JsonStr(r.Reason) + "}")));
            sb.Append("],");
            sb.Append("\"fileResults\":[");
            sb.Append(string.Join(",", report.FileResults.Select(f =>
                "{\"fileName\":" + JsonStr(f.FileName) +
                ",\"violations\":[" + string.Join(",", f.Violations.Select(SerialiseViolation)) + "]}")));
            sb.Append("]}");

            return RunNode("summary", path, sb.ToString());
        }

        // ── Per-file detailed report ──────────────────────────────────────────────
        public static string ExportFile(FileAnalysisResult file, SolidReport report)
        {
            string name = Path.GetFileNameWithoutExtension(file.FileName);
            string ts   = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            string path = Path.Combine(OutputFolder(), $"SOLID_{name}_{ts}.docx");

            var tmpResult = new List<FileAnalysisResult> { file };
            var tmpReport = RatingEngine.GenerateReport(tmpResult, name);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"fileName\":" + JsonStr(name) + ",");
            sb.Append("\"gameName\":" + JsonStr(PlayerSettings.productName ?? string.Empty) + ",");
            sb.Append("\"bundleId\":" + JsonStr(Application.identifier ?? string.Empty) + ",");
            sb.Append("\"ratings\":[");
            sb.Append(string.Join(",", tmpReport.Ratings.Select(r =>
                "{\"principle\":" + JsonStr(r.Principle.ToString()) +
                ",\"score\":" + r.Score +
                ",\"label\":" + JsonStr(r.Label) +
                ",\"reason\":" + JsonStr(r.Reason) + "}")));
            sb.Append("],");
            sb.Append("\"violations\":[" + string.Join(",", file.Violations.Select(SerialiseViolation)) + "]");
            sb.Append("}");

            return RunNode("file", path, sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HTML EXPORT — pure C#, no Node.js / Word / npm required.
        //  Self-contained .html (inline CSS) that opens in any browser. Useful for
        //  anyone without Word installed, or where the Node/docx toolchain is missing.
        // ════════════════════════════════════════════════════════════════════════

        public static string ExportHtml(SolidReport report)
        {
            string ts   = report.GeneratedAt.ToString("yyyy-MM-dd_HH-mm");
            string path = Path.Combine(OutputFolder(), $"SOLID_Report_{ts}.html");
            string html = BuildHtmlDocument("SOLID Review — Project Report", report, report.FileResults);
            File.WriteAllText(path, html, new UTF8Encoding(false));
            return path;
        }

        public static string ExportFileHtml(FileAnalysisResult file, SolidReport report)
        {
            string name = Path.GetFileNameWithoutExtension(file.FileName);
            string ts   = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            string path = Path.Combine(OutputFolder(), $"SOLID_{name}_{ts}.html");

            var single = new List<FileAnalysisResult> { file };
            var rep    = RatingEngine.GenerateReport(single, name);
            string html = BuildHtmlDocument("SOLID Review — " + name, rep, single);
            File.WriteAllText(path, html, new UTF8Encoding(false));
            return path;
        }

        private static string BuildHtmlDocument(string heading, SolidReport report, List<FileAnalysisResult> files)
        {
            var all   = files.SelectMany(f => f.Violations).ToList();
            int high  = all.Count(v => v.Severity == Severity.High);
            int med   = all.Count(v => v.Severity == Severity.Medium);
            int low   = all.Count(v => v.Severity == Severity.Low);
            int total = all.Count;

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>").Append(HtmlEscape(heading)).Append("</title><style>").Append(HtmlCss).Append("</style></head>");
            sb.Append("<body><div class=\"bar\"></div><div class=\"wrap\">");

            // Header
            sb.Append("<header><div class=\"eyebrow\">GD CODESHIELD &middot; SOLID REVIEW</div>");
            sb.Append("<h1>").Append(HtmlEscape(heading)).Append("</h1>");
            sb.Append("<p class=\"sub\">").Append(HtmlEscape(report.ProjectName ?? "Unity Project"))
              .Append(" &middot; generated ").Append(HtmlEscape(report.GeneratedAt.ToString("yyyy-MM-dd HH:mm")))
              .Append("</p></header>");

            // Summary cards
            string sc = ScoreColor(report.OverallScore);
            sb.Append("<div class=\"cards\">");
            sb.Append("<div class=\"card\" style=\"border-left-color:").Append(sc).Append("\"><div class=\"n\" style=\"color:")
              .Append(sc).Append("\">").Append(report.OverallScore.ToString("F1")).Append(" / 5</div><div class=\"l\">")
              .Append(HtmlEscape(report.OverallLabel ?? "")).Append("</div></div>");
            sb.Append("<div class=\"card\"><div class=\"n\">").Append(report.TotalFiles).Append("</div><div class=\"l\">Files scanned</div></div>");
            sb.Append("<div class=\"card\"><div class=\"n\">").Append(total).Append("</div><div class=\"l\">Violations</div></div>");
            sb.Append("</div>");

            // Scores by principle
            sb.Append("<h2>Scores by Principle</h2><ul class=\"legend\">");
            if (report.Ratings != null)
                foreach (var r in report.Ratings)
                {
                    sb.Append("<li><span class=\"sw\" style=\"background:").Append(PrincipleHex(r.Principle)).Append("\"></span>")
                      .Append("<b style=\"color:#0E1A33;margin-right:8px\">").Append(r.Principle).Append("</b>")
                      .Append("<span style=\"color:#6B6B66\">").Append(HtmlEscape(r.Reason ?? "")).Append("</span>")
                      .Append("<span class=\"val\" style=\"color:").Append(ScoreColor(r.Score)).Append("\">")
                      .Append(r.Score).Append("/5 &middot; ").Append(HtmlEscape(r.Label ?? "")).Append("</span></li>");
                }
            sb.Append("</ul>");

            // Distribution by principle
            AppendDistribution(sb, "Violation Distribution by Principle",
                "How the total violations break down across SRP / OCP / LSP / ISP.",
                (report.Ratings ?? new List<PrincipleRating>())
                    .Select(r => (r.Principle.ToString(), r.Violations, PrincipleHex(r.Principle))).ToList());

            // Severity
            AppendDistribution(sb, "Severity Breakdown",
                "Severity weighting of every violation found in the scan.",
                new List<(string, int, string)>
                {
                    ("High severity",   high, "#C0392B"),
                    ("Medium severity", med,  "#C79A20"),
                    ("Low severity",    low,  "#85B7EB"),
                });

            // Violations grouped by file (worst first)
            sb.Append("<h2>Violations</h2>");
            if (total == 0)
            {
                sb.Append("<p class=\"desc\">No violations found — every scanned file is clean.</p>");
            }
            else
            {
                sb.Append("<div class=\"vlist\">");
                foreach (var f in files.Where(f => f.Violations.Count > 0).OrderByDescending(f => f.Violations.Count))
                {
                    sb.Append("<div class=\"file\">").Append(HtmlEscape(f.FileName)).Append(" &mdash; ")
                      .Append(f.Violations.Count).Append(" violation(s)</div>");
                    foreach (var v in f.Violations)
                    {
                        string sev = v.Severity == Severity.High ? "#C0392B"
                                   : v.Severity == Severity.Medium ? "#C79A20" : "#85B7EB";
                        sb.Append("<div class=\"v\" style=\"border-left-color:").Append(sev).Append("\"><div class=\"vtop\">");
                        sb.Append("<span class=\"pill\" style=\"background:rgba(107,107,102,.12);color:").Append(sev).Append("\">")
                          .Append(v.Principle).Append(" &middot; ").Append(v.Severity).Append("</span>");
                        int line = v.Location != null ? v.Location.StartLine : 0;
                        sb.Append("<span class=\"ln\">line ").Append(line).Append("</span></div>");
                        sb.Append("<div class=\"t\">").Append(HtmlEscape(v.Title)).Append("</div>");
                        if (!string.IsNullOrEmpty(v.Description))
                            sb.Append("<div class=\"d\">").Append(HtmlEscape(v.Description)).Append("</div>");
                        if (!string.IsNullOrEmpty(v.Evidence))
                            sb.Append("<div class=\"ev\">").Append(HtmlEscape(v.Evidence)).Append("</div>");
                        sb.Append("</div>");
                    }
                }
                sb.Append("</div>");
            }

            sb.Append("<footer>Generated by GD CodeShield &middot; SOLID Review</footer>");
            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        private static void AppendDistribution(StringBuilder sb, string title, string desc,
            List<(string label, int value, string color)> items)
        {
            int total = items.Sum(i => i.value);
            sb.Append("<h2>").Append(HtmlEscape(title)).Append("</h2><p class=\"desc\">").Append(HtmlEscape(desc)).Append("</p>");
            sb.Append("<div class=\"stack\">");
            if (total > 0)
                foreach (var it in items)
                {
                    if (it.value <= 0) continue;
                    double pct = 100.0 * it.value / total;
                    sb.Append("<span style=\"width:").Append(pct.ToString("F2")).Append("%;background:").Append(it.color).Append("\"></span>");
                }
            sb.Append("</div><ul class=\"legend\">");
            foreach (var it in items)
            {
                int pct = total > 0 ? (int)System.Math.Round(100.0 * it.value / total) : 0;
                sb.Append("<li><span class=\"sw\" style=\"background:").Append(it.color).Append("\"></span>")
                  .Append(HtmlEscape(it.label)).Append("<span class=\"val\">").Append(it.value)
                  .Append(" (").Append(pct).Append("%)</span></li>");
            }
            sb.Append("</ul>");
        }

        private static string ScoreColor(double s) => s >= 4 ? "#6FA76F" : s >= 3 ? "#C79A20" : "#C0392B";

        private static string PrincipleHex(SolidPrinciple p)
        {
            switch (p)
            {
                case SolidPrinciple.SRP: return "#0E1A33";
                case SolidPrinciple.OCP: return "#C79A20";
                case SolidPrinciple.LSP: return "#85B7EB";
                case SolidPrinciple.ISP: return "#6FA76F";
                default:                 return "#6B6B66";
            }
        }

        private static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private const string HtmlCss =
@"*{box-sizing:border-box;margin:0;padding:0}
body{background:#EEEDE6;color:#3D3D3A;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;line-height:1.5}
.bar{position:fixed;left:0;top:0;bottom:0;width:6px;background:#F4C430}
.wrap{max-width:920px;margin:0 auto;padding:0 44px 60px}
header{padding:40px 0 18px;border-bottom:1px solid #D3D1C7;margin-bottom:24px}
.eyebrow{font-size:11px;font-weight:700;letter-spacing:2px;color:#0E1A33}
h1{font-family:Georgia,'Times New Roman',serif;font-size:34px;color:#0E1A33;margin:10px 0 6px}
.sub{font-size:12px;color:#6B6B66}
h2{font-family:Georgia,serif;font-size:20px;color:#0E1A33;margin:30px 0 4px}
.desc{font-size:12px;color:#6B6B66;margin-bottom:10px}
.cards{display:flex;gap:12px;flex-wrap:wrap;margin:16px 0 6px}
.card{flex:1 1 130px;border:1px solid #D3D1C7;border-left:4px solid #F4C430;padding:12px 14px;background:#F7F6F0}
.card .n{font-family:Georgia,serif;font-size:26px;color:#0E1A33;font-weight:700}
.card .l{font-size:10px;letter-spacing:1px;color:#6B6B66;text-transform:uppercase;margin-top:2px}
.stack{display:flex;height:24px;width:100%;border:1px solid #D3D1C7;overflow:hidden;background:rgba(0,0,0,.05);margin:8px 0}
.stack span{display:block;height:100%}
.legend{list-style:none}
.legend li{display:flex;align-items:center;padding:7px 0;border-bottom:1px solid #D3D1C7;font-size:13px}
.legend .sw{width:11px;height:11px;margin-right:10px;flex:0 0 auto}
.legend .val{margin-left:auto;font-weight:700;color:#0E1A33;padding-left:12px}
.vlist{margin-top:8px}
.file{font-family:Georgia,serif;font-style:italic;color:#6B6B66;font-size:13px;margin:20px 0 6px}
.v{border:1px solid #D3D1C7;border-left:4px solid #C0392B;background:#F7F6F0;padding:10px 12px;margin-bottom:8px}
.v .vtop{display:flex;gap:10px;align-items:center;margin-bottom:4px}
.pill{font-size:10px;font-weight:700;padding:2px 8px;border-radius:2px}
.ln{font-size:11px;color:#6B6B66}
.v .t{font-weight:700;color:#0E1A33;font-size:13px}
.v .d{font-size:12px;color:#3D3D3A;margin-top:2px}
.v .ev{font-size:11px;color:#6B6B66;margin-top:4px;font-family:Menlo,Consolas,monospace}
footer{margin-top:44px;padding-top:16px;border-top:1px solid #D3D1C7;font-size:10px;color:#6B6B66;letter-spacing:1px}";

        // ── Helpers ───────────────────────────────────────────────────────────────
        private static string SerialiseViolation(Violation v)
        {
            return "{\"principle\":" + JsonStr(v.Principle.ToString()) +
                   ",\"severity\":" + JsonStr(v.Severity.ToString()) +
                   ",\"title\":" + JsonStr(v.Title) +
                   ",\"description\":" + JsonStr(v.Description) +
                   ",\"evidence\":" + JsonStr(v.Evidence) +
                   ",\"location\":{\"fileName\":" + JsonStr(v.Location != null ? v.Location.FileName : "") +
                   ",\"startLine\":" + (v.Location != null ? v.Location.StartLine : 0) + "}}";
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "")
                .Replace("\t", "  ")
                + "\"";
        }
    }
}