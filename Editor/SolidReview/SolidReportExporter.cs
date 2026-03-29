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
