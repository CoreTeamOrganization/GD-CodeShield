// SolidAnalyzer.cs
// Zero external dependencies — uses plain C# regex + string parsing.
// Detects SRP, OCP, LSP, ISP violations in Unity C# scripts.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SolidAgent
{
    // ── Data models ───────────────────────────────────────────────────────────────

    public enum SolidPrinciple { SRP, OCP, LSP, ISP }
    public enum Severity       { Low, Medium, High }

    public class CodeLocation
    {
        public string FilePath   { get; set; }
        public string FileName   { get; set; }
        public string ClassName  { get; set; }
        public string MemberName { get; set; }
        public int    StartLine  { get; set; }
        public int    EndLine    { get; set; }
    }

    public class Violation
    {
        public string         Id           { get; set; }
        public SolidPrinciple Principle    { get; set; }
        public Severity       Severity     { get; set; }
        public string         Title        { get; set; }
        public string         Description  { get; set; }
        public CodeLocation   Location     { get; set; }
        public string         OriginalCode { get; set; }
        public string         Evidence     { get; set; }
    }

    public class GeneratedFix
    {
        public string       ViolationId    { get; set; }
        public string       FixedCode      { get; set; }
        public string       DiffSummary    { get; set; }
        public string       Explanation    { get; set; }
        public List<string> NewFilesNeeded { get; set; } = new List<string>();
    }

    public class FileAnalysisResult
    {
        public string          FilePath   { get; set; }
        public string          FileName   { get; set; }
        public List<Violation> Violations { get; set; } = new List<Violation>();
    }

    public class TestCase
    {
        public string Id             { get; set; }
        public string Description    { get; set; }
        public string ExpectedOutput { get; set; }
        public string ActualOutput   { get; set; }
        public bool   Passed         => ExpectedOutput == ActualOutput;
    }

    public class RegressionReport
    {
        public string         FilePath  { get; set; }
        public List<TestCase> Tests     { get; set; } = new List<TestCase>();
        public bool           AllPassed => Tests.TrueForAll(t => t.Passed);
        public int            PassCount => Tests.FindAll(t => t.Passed).Count;
        public int            FailCount => Tests.FindAll(t => !t.Passed).Count;
    }

    // ── Analyzer ──────────────────────────────────────────────────────────────────

    public class SolidAnalyzer
    {
        private const int SRP_MAX_METHODS       = 10;
        private const int ISP_MAX_INTERFACE_MTH = 5;

        // ── Scan folder ───────────────────────────────────────────────────────────

        public List<FileAnalysisResult> AnalyzeFolder(string folderPath)
        {
            var results = new List<FileAnalysisResult>();
            if (!Directory.Exists(folderPath)) return results;

            foreach (var file in Directory.GetFiles(folderPath, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(".meta")) continue;
                try { results.Add(AnalyzeFile(file)); }
                catch { }
            }
            return results;
        }

        // ── Analyze single file ───────────────────────────────────────────────────

        public FileAnalysisResult AnalyzeFile(string filePath)
        {
            var result = new FileAnalysisResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };

            string source = File.ReadAllText(filePath);
            string[] lines = source.Split('\n');
            int idx = 1;

            var classes    = FindClasses(source, lines);
            var interfaces = FindInterfaces(source, lines);

            foreach (var cls in classes)
            {
                CheckSRP(cls, filePath, result, ref idx);
                CheckOCP(cls, filePath, result, ref idx);
                CheckLSP(cls, filePath, result, ref idx);
                CheckISP_Implementor(cls, filePath, result, ref idx);
            }

            foreach (var iface in interfaces)
                CheckISP_Interface(iface, filePath, result, ref idx);

            return result;
        }

        // ── Class/Interface extraction ────────────────────────────────────────────

        private class ClassInfo
        {
            public string Name      { get; set; }
            public int    StartLine { get; set; }
            public int    EndLine   { get; set; }
            public string Body      { get; set; }
            public List<string> Methods    { get; set; } = new List<string>();
            public List<string> Interfaces { get; set; } = new List<string>();
        }

        private List<ClassInfo> FindClasses(string source, string[] lines)
        {
            var result  = new List<ClassInfo>();
            var classRx = new Regex(@"(public|private|protected|internal)?\s*(abstract\s+|sealed\s+)?class\s+(\w+)(\s*:\s*([^\{]+))?", RegexOptions.Multiline);

            foreach (Match m in classRx.Matches(source))
            {
                string name  = m.Groups[3].Value;
                int    start = LineOf(source, m.Index);
                string body  = ExtractBlock(source, m.Index);

                var info = new ClassInfo
                {
                    Name      = name,
                    StartLine = start,
                    EndLine   = start + body.Split('\n').Length,
                    Body      = body
                };

                // Extract implemented interfaces
                if (m.Groups[5].Success)
                {
                    foreach (var part in m.Groups[5].Value.Split(','))
                    {
                        string t = part.Trim();
                        if (t.StartsWith("I") && t.Length > 1) // convention: interfaces start with I
                            info.Interfaces.Add(t);
                    }
                }

                // Extract method names
                var methodRx = new Regex(@"(public|private|protected|internal|override|virtual|static)\s+[\w<>\[\]]+\s+(\w+)\s*\(");
                foreach (Match mm in methodRx.Matches(body))
                    info.Methods.Add(mm.Groups[2].Value);

                result.Add(info);
            }
            return result;
        }

        private List<ClassInfo> FindInterfaces(string source, string[] lines)
        {
            var result  = new List<ClassInfo>();
            var ifaceRx = new Regex(@"(public|internal)?\s*interface\s+(\w+)", RegexOptions.Multiline);

            foreach (Match m in ifaceRx.Matches(source))
            {
                string name = m.Groups[2].Value;
                string body = ExtractBlock(source, m.Index);
                int start   = LineOf(source, m.Index);

                var info = new ClassInfo
                {
                    Name      = name,
                    StartLine = start,
                    EndLine   = start + body.Split('\n').Length,
                    Body      = body
                };

                var methodRx = new Regex(@"[\w<>\[\]]+\s+(\w+)\s*\(");
                foreach (Match mm in methodRx.Matches(body))
                    info.Methods.Add(mm.Groups[1].Value);

                result.Add(info);
            }
            return result;
        }

        // Extract the { ... } block starting from a position
        private string ExtractBlock(string source, int fromPos)
        {
            int start = source.IndexOf('{', fromPos);
            if (start < 0) return "";
            int depth = 0, i = start;
            while (i < source.Length)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') { depth--; if (depth == 0) return source.Substring(start, i - start + 1); }
                i++;
            }
            return source.Substring(start);
        }

        private int LineOf(string source, int charIndex)
        {
            int line = 1;
            for (int i = 0; i < charIndex && i < source.Length; i++)
                if (source[i] == '\n') line++;
            return line;
        }

        // ── SRP ───────────────────────────────────────────────────────────────────
        // Flag classes with too many methods or multiple distinct concern groups

        private void CheckSRP(ClassInfo cls, string filePath, FileAnalysisResult result, ref int idx)
        {
            var concerns = DetectConcerns(cls.Methods);
            bool tooMany      = cls.Methods.Count > SRP_MAX_METHODS;
            bool multiConcern = concerns.Count >= 3;

            if (!tooMany && !multiConcern) return;

            result.Violations.Add(new Violation
            {
                Id          = $"SRP-{idx++:D3}",
                Principle   = SolidPrinciple.SRP,
                Severity    = multiConcern ? Severity.High : Severity.Medium,
                Title       = $"{cls.Name} has multiple responsibilities",
                Description = $"'{cls.Name}' handles more than one concern. Each class should have a single reason to change.",
                Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = cls.Name, StartLine = cls.StartLine, EndLine = cls.EndLine },
                OriginalCode = Trim(cls.Body, 400),
                Evidence     = multiConcern
                    ? "Concern groups: " + string.Join(", ", concerns.Select(c => $"{c.Key}({c.Value.Count})"))
                    : $"{cls.Methods.Count} methods in one class"
            });
        }

        private Dictionary<string, List<string>> DetectConcerns(List<string> methods)
        {
            var map = new Dictionary<string, string[]>
            {
                ["Movement"]  = new[] { "Move","Jump","Walk","Run","Teleport","Dash" },
                ["Combat"]    = new[] { "Attack","Hit","Shoot","Fire","Damage","Kill","Die" },
                ["Audio"]     = new[] { "Play","Sound","Music","Audio","Volume","Mute" },
                ["UI"]        = new[] { "Show","Hide","Display","Render","Draw","UpdateUI","UpdateScore","UpdateHUD" },
                ["Scoring"]   = new[] { "Score","Points","AddScore","ResetScore" },
                ["Saving"]    = new[] { "Save","Load","Persist","Serialize" },
                ["Animation"] = new[] { "Animate","SetTrigger","SetBool","SetFloat" }
            };
            var groups = new Dictionary<string, List<string>>();
            foreach (var method in methods)
            {
                foreach (var kv in map)
                {
                    if (kv.Value.Any(p => method.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!groups.ContainsKey(kv.Key)) groups[kv.Key] = new List<string>();
                        groups[kv.Key].Add(method);
                        break;
                    }
                }
            }
            return groups;
        }

        // ── OCP ───────────────────────────────────────────────────────────────────
        // Flag switch statements or long if/else chains on type strings

        private void CheckOCP(ClassInfo cls, string filePath, FileAnalysisResult result, ref int idx)
        {
            // Find switch on type-like variable
            var switchRx = new Regex(@"switch\s*\(\s*(\w+)\s*\)", RegexOptions.Multiline);
            foreach (Match m in switchRx.Matches(cls.Body))
            {
                string varName = m.Groups[1].Value.ToLower();
                if (!IsTypeVar(varName)) continue;

                // Count cases
                int cases = Regex.Matches(cls.Body.Substring(m.Index), @"\bcase\b").Count;
                if (cases < 3) continue;

                int line = cls.StartLine + LineOf(cls.Body, m.Index) - 1;
                result.Violations.Add(new Violation
                {
                    Id          = $"OCP-{idx++:D3}",
                    Principle   = SolidPrinciple.OCP,
                    Severity    = Severity.High,
                    Title       = $"Type switch in {cls.Name}",
                    Description = $"Switch on '{m.Groups[1].Value}' with {cases} cases. New types require editing this method. Use polymorphism instead.",
                    Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = cls.Name, StartLine = line },
                    OriginalCode = Trim(m.Value, 200),
                    Evidence    = $"switch({m.Groups[1].Value}) — {cases} cases"
                });
            }

            // Find if/else chains on string equality
            var ifChainRx = new Regex(@"if\s*\(.+==\s*""(\w+)""\)", RegexOptions.Multiline);
            var matches   = ifChainRx.Matches(cls.Body);
            if (matches.Count >= 3)
            {
                int line = cls.StartLine + LineOf(cls.Body, matches[0].Index) - 1;
                result.Violations.Add(new Violation
                {
                    Id          = $"OCP-{idx++:D3}",
                    Principle   = SolidPrinciple.OCP,
                    Severity    = Severity.Medium,
                    Title       = $"Type if/else chain in {cls.Name}",
                    Description = "Long if/else chain comparing string types. Each new type means editing this method. Use polymorphism instead.",
                    Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = cls.Name, StartLine = line },
                    OriginalCode = Trim(matches[0].Value, 200),
                    Evidence    = $"{matches.Count} string-comparison branches"
                });
            }
        }

        private bool IsTypeVar(string name)
        {
            var keywords = new[] { "type", "kind", "category", "variant", "mode", "tag", "enemy", "item", "name" };
            return keywords.Any(k => name.Contains(k));
        }

        // ── LSP ───────────────────────────────────────────────────────────────────
        // Flag methods whose entire body is throw new NotImplementedException

        private void CheckLSP(ClassInfo cls, string filePath, FileAnalysisResult result, ref int idx)
        {
            // Match: any method body that contains only a throw new NotImplementedException
            var rx = new Regex(
                @"(public|private|protected|override)\s+[\w<>\[\]]+\s+(\w+)\s*\([^)]*\)\s*\{\s*throw\s+new\s+Not(Implemented|Supported)Exception[^}]*\}",
                RegexOptions.Multiline | RegexOptions.Singleline);

            foreach (Match m in rx.Matches(cls.Body))
            {
                string methodName = m.Groups[2].Value;
                int    line       = cls.StartLine + LineOf(cls.Body, m.Index) - 1;

                result.Violations.Add(new Violation
                {
                    Id          = $"LSP-{idx++:D3}",
                    Principle   = SolidPrinciple.LSP,
                    Severity    = Severity.High,
                    Title       = $"{cls.Name}.{methodName}() throws NotImplementedException",
                    Description = "Method only throws. Any code calling this via the base interface will crash. Implement it or split the interface (ISP).",
                    Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = cls.Name, MemberName = methodName, StartLine = line },
                    OriginalCode = Trim(m.Value, 200),
                    Evidence    = $"throw new NotImplementedException in {methodName}"
                });
            }
        }

        // ── ISP — Interface too large ─────────────────────────────────────────────

        private void CheckISP_Interface(ClassInfo iface, string filePath, FileAnalysisResult result, ref int idx)
        {
            if (iface.Methods.Count <= ISP_MAX_INTERFACE_MTH) return;

            result.Violations.Add(new Violation
            {
                Id          = $"ISP-{idx++:D3}",
                Principle   = SolidPrinciple.ISP,
                Severity    = Severity.Medium,
                Title       = $"{iface.Name} has {iface.Methods.Count} methods — too large",
                Description = $"'{iface.Name}' forces all implementors to implement {iface.Methods.Count} methods. Split into smaller focused interfaces.",
                Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = iface.Name, StartLine = iface.StartLine },
                OriginalCode = Trim(iface.Body, 400),
                Evidence    = "Methods: " + string.Join(", ", iface.Methods)
            });
        }

        // ── ISP — Implementor throws too many methods ─────────────────────────────

        private void CheckISP_Implementor(ClassInfo cls, string filePath, FileAnalysisResult result, ref int idx)
        {
            var throwRx   = new Regex(@"(public|override)\s+[\w<>\[\]]+\s+(\w+)\s*\([^)]*\)\s*\{\s*throw\s+new\s+NotImplementedException[^}]*\}", RegexOptions.Singleline);
            var throwMatches = throwRx.Matches(cls.Body);
            int throwCount   = throwMatches.Count;

            if (throwCount < 3) return;
            if (cls.Methods.Count == 0) return;

            double ratio = (double)throwCount / cls.Methods.Count * 100;
            if (ratio < 50) return;

            var throwingNames = new List<string>();
            foreach (Match m in throwMatches)
                throwingNames.Add(m.Groups[2].Value);

            result.Violations.Add(new Violation
            {
                Id          = $"ISP-{idx++:D3}",
                Principle   = SolidPrinciple.ISP,
                Severity    = Severity.High,
                Title       = $"{cls.Name} implements {throwCount}/{cls.Methods.Count} methods it doesn't use",
                Description = $"Forced to implement a fat interface. {throwCount} methods just throw. Split the interface.",
                Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = cls.Name, StartLine = cls.StartLine },
                OriginalCode = Trim(cls.Body, 400),
                Evidence    = "Unused: " + string.Join(", ", throwingNames)
            });
        }

        private string Trim(string s, int max)
            => s ?? ""; // no truncation — show full code
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  RATING ENGINE
    //  Based on SOLID Rating Easy Guide (1–5 per principle)
    // ════════════════════════════════════════════════════════════════════════════

    public class PrincipleRating
    {
        public SolidPrinciple Principle   { get; set; }
        public int            Score       { get; set; } // 1–5
        public string         Label       { get; set; } // Excellent / Very Good / etc.
        public string         Reason      { get; set; } // why this score
        public int            Violations  { get; set; }
        public int            FilesScanned { get; set; }
    }

    public class SolidReport
    {
        public List<PrincipleRating>      Ratings       { get; set; } = new List<PrincipleRating>();
        public List<FileAnalysisResult>   FileResults   { get; set; } = new List<FileAnalysisResult>();
        public int                        TotalFiles    { get; set; }
        public int                        TotalViolations { get; set; }
        public float                      OverallScore  { get; set; } // average of all principles
        public string                     OverallLabel  { get; set; }
        public System.DateTime            GeneratedAt   { get; set; }
        public string                     ProjectName   { get; set; }
    }

    public static class RatingEngine
    {
        // Score map from guide:
        // 5 = Excellent  (0 violations)
        // 4 = Very Good  (1–2 violations, Low severity only)
        // 3 = Acceptable (3–5 violations, or any Medium)
        // 2 = Weak       (6–9 violations, or any High)
        // 1 = Poor       (10+ violations, or multiple High)

        private static readonly string[] Labels = { "", "Poor", "Weak", "Acceptable", "Very Good", "Excellent" };

        private static readonly string[] SRP_Reasons =
        {
            "",
            "Classes doing everything — messy and confusing.",
            "Classes doing many unrelated jobs. Hard to maintain.",
            "Some classes handle a few different concerns. Could be clearer.",
            "Mostly one responsibility per class; minor overlap exists.",
            "Each class does exactly one job. Very organized."
        };

        private static readonly string[] OCP_Reasons =
        {
            "",
            "Must rewrite old code every time a new feature is added.",
            "Adding new features requires changes in many existing files.",
            "Some old code needs editing to add new features.",
            "Mostly easy to extend; minor tweaks to existing code needed.",
            "New features can be added without touching any existing code."
        };

        private static readonly string[] LSP_Reasons =
        {
            "",
            "Subclasses completely break the system when substituted.",
            "Subclasses break things if swapped with parent class.",
            "Subclasses work but sometimes behave differently than expected.",
            "Minor differences exist; subclasses mostly work in place of parent.",
            "Subclasses work perfectly as substitutes for their parent."
        };

        private static readonly string[] ISP_Reasons =
        {
            "",
            "Giant interfaces with many unrelated methods everywhere.",
            "Interfaces too large; classes forced to implement unused methods.",
            "Some extra methods included in interfaces.",
            "Mostly focused interfaces; minor unnecessary methods exist.",
            "Interfaces are small and focused — classes implement only what they need."
        };

        public static SolidReport GenerateReport(List<FileAnalysisResult> results, string projectName)
        {
            var report = new SolidReport
            {
                FileResults    = results,
                TotalFiles     = results.Count,
                TotalViolations = results.Sum(r => r.Violations.Count),
                GeneratedAt    = System.DateTime.Now,
                ProjectName    = projectName
            };

            foreach (SolidPrinciple p in System.Enum.GetValues(typeof(SolidPrinciple)))
            {
                var violations = results
                    .SelectMany(r => r.Violations)
                    .Where(v => v.Principle == p)
                    .ToList();

                int score = CalcScore(violations);
                string[] reasons = p switch
                {
                    SolidPrinciple.SRP => SRP_Reasons,
                    SolidPrinciple.OCP => OCP_Reasons,
                    SolidPrinciple.LSP => LSP_Reasons,
                    SolidPrinciple.ISP => ISP_Reasons,
                    _ => SRP_Reasons
                };

                report.Ratings.Add(new PrincipleRating
                {
                    Principle    = p,
                    Score        = score,
                    Label        = Labels[score],
                    Reason       = reasons[score],
                    Violations   = violations.Count,
                    FilesScanned = results.Count
                });
            }

            float avg = report.Ratings.Count > 0
                ? (float)report.Ratings.Sum(r => r.Score) / report.Ratings.Count
                : 0f;
            report.OverallScore = avg;
            report.OverallLabel = avg >= 4.5f ? "Excellent"
                                : avg >= 3.5f ? "Very Good"
                                : avg >= 2.5f ? "Acceptable"
                                : avg >= 1.5f ? "Weak"
                                : "Poor";

            return report;
        }

        private static int CalcScore(List<Violation> violations)
        {
            if (violations.Count == 0) return 5;

            int highCount   = violations.Count(v => v.Severity == Severity.High);
            int medCount    = violations.Count(v => v.Severity == Severity.Medium);
            int total       = violations.Count;

            if (highCount >= 2 || total >= 10) return 1;
            if (highCount >= 1 || total >= 6)  return 2;
            if (medCount  >= 1 || total >= 3)  return 3;
            if (total <= 2)                    return 4;
            return 3;
        }
    }
}
