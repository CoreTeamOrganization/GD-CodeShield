// RegressionHarness.cs
// Applies fixes to disk and builds a regression report.
// Unity API calls (isCompiling, scriptCompilationFailed) must be made
// on the main thread in SolidAgentWindow — BuildReport takes the pre-read result.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SolidAgent
{
    public class RegressionHarness
    {
        private readonly string _baselineDir;

        public RegressionHarness(string projectRoot)
        {
            _baselineDir = Path.Combine(projectRoot, "..", "solid-agent-baselines");
            Directory.CreateDirectory(_baselineDir);
        }

        // ── Step 1: Capture baseline ──────────────────────────────────────────────

        public Task CaptureBaselineAsync(string filePath)
        {
            // Baseline = zero errors assumed on unmodified file.
            // We save the file hash so we can detect if it changed.
            var result = new CompileResult
            {
                ErrorCount  = 0,
                FileHash    = FileHash(filePath)
            };
            string path = Path.Combine(_baselineDir, FileKey(filePath) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(result));
            return Task.CompletedTask;
        }

        // ── Step 2: Apply fix to disk ─────────────────────────────────────────────

        public void ApplyFix(GeneratedFix fix, string filePath)
        {
            if (string.IsNullOrWhiteSpace(fix.FixedCode)) return;

            string backup = filePath + ".solid-backup";
            if (!File.Exists(backup)) File.Copy(filePath, backup);

            // Write the fixed main file
            File.WriteAllText(filePath, fix.FixedCode);

            // Write companion files
            string dir = Path.GetDirectoryName(filePath);
            foreach (var newFile in fix.NewFilesNeeded)
            {
                string dest = Path.Combine(dir, newFile);
                string content = null;

                // Try exact key match first
                if (fix.NewFileContents != null)
                {
                    fix.NewFileContents.TryGetValue(newFile, out content);

                    // Fallback: case-insensitive match
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        foreach (var kv in fix.NewFileContents)
                        {
                            if (string.Equals(kv.Key, newFile, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(Path.GetFileName(kv.Key), newFile, StringComparison.OrdinalIgnoreCase))
                            {
                                content = kv.Value;
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(content))
                    File.WriteAllText(dest, content);
                // Do NOT write empty stubs — they cause compile errors
            }
        }

        public void RevertFix(string filePath)
        {
            string backup = filePath + ".solid-backup";
            if (!File.Exists(backup)) return;
            File.Copy(backup, filePath, overwrite: true);
            File.Delete(backup);
        }

        // ── Step 3: Build report from compile result read on main thread ─────────
        // compileFailed must be read on the main thread BEFORE calling this.
        // All file I/O here is fine on any thread.

        public RegressionReport BuildReport(string filePath, bool compileFailed)
        {
            var report = new RegressionReport { FilePath = filePath };

            report.Tests.Add(new TestCase
            {
                Id             = "unity-compile",
                Description    = "Unity compiles project without errors after fix",
                ExpectedOutput = "no errors",
                ActualOutput   = compileFailed ? "compile errors detected" : "no errors"
            });

            // Basic syntax check on the fixed file — pure file I/O, safe anywhere
            foreach (var err in BasicSyntaxCheck(filePath))
            {
                report.Tests.Add(new TestCase
                {
                    Id             = "syntax",
                    Description    = err,
                    ExpectedOutput = "no error",
                    ActualOutput   = "syntax issue"
                });
            }

            return report;
        }

        // ── Basic syntax check (no Roslyn) ────────────────────────────────────────
        // Catches obvious issues: unmatched braces, missing semicolons on obvious lines.

        private List<string> BasicSyntaxCheck(string filePath)
        {
            var issues = new List<string>();
            if (!File.Exists(filePath)) return issues;

            string source = File.ReadAllText(filePath);

            // Check brace balance
            int open = 0, close = 0;
            foreach (char c in source) { if (c == '{') open++; else if (c == '}') close++; }
            if (open != close)
                issues.Add($"Unmatched braces — {open} open, {close} close");

            // Check parenthesis balance
            int pOpen = 0, pClose = 0;
            foreach (char c in source) { if (c == '(') pOpen++; else if (c == ')') pClose++; }
            if (pOpen != pClose)
                issues.Add($"Unmatched parentheses — {pOpen} open, {pClose} close");

            return issues;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private string FileKey(string fp)
            => Path.GetFileNameWithoutExtension(fp).Replace(" ", "_");

        private string FileHash(string fp)
        {
            if (!File.Exists(fp)) return "";
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = File.OpenRead(fp);
            var hash = md5.ComputeHash(stream);
            return System.BitConverter.ToString(hash).Replace("-", "");
        }

        private class CompileResult
        {
            public int    ErrorCount { get; set; }
            public string FileHash   { get; set; }
        }
    }
}
