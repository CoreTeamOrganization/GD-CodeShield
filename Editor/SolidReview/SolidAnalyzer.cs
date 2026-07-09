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
        public string                       ViolationId      { get; set; }
        public string                       FixedCode        { get; set; }
        public string                       DiffSummary      { get; set; }
        public string                       Explanation      { get; set; }
        public List<string>                 NewFilesNeeded   { get; set; } = new List<string>();
        // Full source code for each new file — key = filename, value = full C# content
        public Dictionary<string, string>   NewFileContents  { get; set; } = new Dictionary<string, string>();
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
        private const int SRP_MAX_METHODS       = 15; // non-lifecycle methods; size is a Low-severity note, not a violation
        private const int ISP_MAX_INTERFACE_MTH = 5;

        private static readonly string[] UnityLifecycle =
        {
            "Awake","Start","Update","FixedUpdate","LateUpdate","OnEnable","OnDisable",
            "OnDestroy","OnApplicationPause","OnApplicationFocus","OnApplicationQuit",
            "OnValidate","Reset","OnGUI","OnDrawGizmos"
        };

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

            // Strip comments before detection — regex detectors otherwise match
            // commented-out code (e.g. an "override void Foo() { }" example in a
            // comment reads as a real empty override). Offsets/line numbers are
            // preserved: comment characters become spaces, newlines stay.
            string source = StripComments(File.ReadAllText(filePath));
            string[] lines = source.Split('\n');
            int idx = 1;

            var classes    = FindClasses(source, lines);
            var interfaces = FindInterfaces(source, lines);

            foreach (var cls in classes)
            {
                CheckSRP(cls, filePath, result, ref idx);
                CheckOCP(cls, filePath, result, ref idx);
                // ISP runs first: when a fat-interface violation already covers a set of
                // throwing methods, LSP skips those methods so one root cause isn't
                // penalized under two principles.
                var ispCovered = CheckISP_Implementor(cls, filePath, result, ref idx);
                CheckLSP(cls, filePath, result, ref idx, ispCovered);
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

        // Blank out // and /* */ comments, preserving every newline and all
        // offsets so violation line numbers stay correct. String and char
        // literals are left intact (OCP's if-chain detection needs them).
        private static string StripComments(string source)
        {
            var chars = source.ToCharArray();
            bool inStr = false, inChar = false, inVerbatim = false, lineC = false, blockC = false;

            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                char next = i + 1 < chars.Length ? chars[i + 1] : '\0';

                if (lineC)
                {
                    if (c == '\n') lineC = false;
                    else chars[i] = ' ';
                    continue;
                }
                if (blockC)
                {
                    if (c == '*' && next == '/') { chars[i] = ' '; chars[i + 1] = ' '; blockC = false; i++; }
                    else if (c != '\n') chars[i] = ' ';
                    continue;
                }
                if (inVerbatim)
                {
                    if (c == '"' && next == '"') i++;        // escaped quote inside @"..."
                    else if (c == '"') inVerbatim = false;
                    continue;
                }
                if (inStr)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (inChar)
                {
                    if (c == '\\') i++;
                    else if (c == '\'') inChar = false;
                    continue;
                }

                if (c == '@' && next == '"') { inVerbatim = true; i++; }
                else if (c == '"')  inStr  = true;
                else if (c == '\'') inChar = true;
                else if (c == '/' && next == '/') { chars[i] = ' '; chars[i + 1] = ' '; lineC = true; i++; }
                else if (c == '/' && next == '*') { chars[i] = ' '; chars[i + 1] = ' '; blockC = true; i++; }
            }
            return new string(chars);
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
        // Three-signal ladder (v1.5.0):
        //   High   = name concern groups + API families + disjoint cohesion clusters
        //   Medium = names + APIs agree, cohesion not computable (neutral)
        //   Low    = single signal, cohesion-vetoed, or size note
        // Method count alone is NEVER more than a Low informational note.

        // Detect orchestrator/facade after SRP split.
        // Patterns that are NOT violations:
        //   1. Class whose methods are all single-statement field delegations
        //      e.g.  void ShowAd() { _ads.ShowAd(); }
        //   2. Class with injected controller/manager fields (GetComponent wiring)
        //      e.g.  private AdsController _ads; + Awake(){ _ads = GetComponent<...>(); }
        private bool IsDelegationClass(ClassInfo cls)
        {
            if (cls.Methods.Count == 0) return false;

            string body = cls.Body;

            // Heuristic 1 — class has private/serialized fields pointing to controllers
            bool hasControllerFields =
                Regex.IsMatch(body, @"private\s+\w+Controller\s+\w+", RegexOptions.Multiline) ||
                Regex.IsMatch(body, @"private\s+\w+Manager\s+\w+",    RegexOptions.Multiline) ||
                Regex.IsMatch(body, @"private\s+\w+Service\s+\w+",    RegexOptions.Multiline) ||
                Regex.IsMatch(body, @"private\s+\w+Handler\s+\w+",    RegexOptions.Multiline) ||
                Regex.IsMatch(body, @"\[RequireComponent",             RegexOptions.Multiline);

            if (!hasControllerFields) return false;

            // Heuristic 2 — count methods whose bodies contain only delegating statements
            // Split body into method blocks by finding "void/public/private ... { ... }"
            // Use a line-count approach: if a method spans 1-4 non-empty lines it's a delegator
            int delegators    = 0;
            int lifecycleMths = 0; // Awake, Start etc don't count against delegation
            string[] lifecycle = { "Awake", "Start", "Update", "FixedUpdate", "LateUpdate",
                                   "OnEnable", "OnDisable", "OnDestroy", "OnApplicationPause" };

            foreach (var mname in cls.Methods)
            {
                if (lifecycle.Any(l => l == mname)) { lifecycleMths++; continue; }

                // Find method signature + body block
                var mRx = new Regex(
                    @"\b" + Regex.Escape(mname) + @"\s*\([^)]*\)\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}",
                    RegexOptions.Singleline);
                var match = mRx.Match(body);
                if (!match.Success) continue;

                string mBody = match.Groups[1].Value.Trim();
                // Count non-empty, non-brace lines
                var codeLines = mBody.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && l != "{" && l != "}")
                    .ToList();

                bool isDelegate =
                    codeLines.Count <= 4 &&          // short body
                    codeLines.Any(l =>                // at least one delegating call
                        l.Contains("Controller.") || l.Contains("controller.") ||
                        l.Contains("Manager.")    || l.Contains("manager.")    ||
                        l.Contains("Service.")    || l.Contains("service.")    ||
                        l.Contains("Handler.")    || l.Contains("handler.")    ||
                        Regex.IsMatch(l, @"_\w+\.\w+\(")); // private field call pattern

                if (isDelegate) delegators++;
            }

            int scoredMethods = cls.Methods.Count - lifecycleMths;
            if (scoredMethods <= 0) return true;

            // If ≥70% of non-lifecycle methods are pure delegators → orchestrator, not a violation
            return (float)delegators / scoredMethods >= 0.70f;
        }

        private void CheckSRP(ClassInfo cls, string filePath, FileAnalysisResult result, ref int idx)
        {
            // Skip orchestrator classes — they delegate intentionally after an SRP split
            if (IsDelegationClass(cls)) return;

            // Signal 1 — method-name concern groups. A group only counts if it has ≥2
            // methods: one stray PlaySound() in a movement class is not a second job.
            var concerns = DetectConcerns(cls.Methods)
                .Where(g => g.Value.Count >= 2)
                .ToDictionary(g => g.Key, g => g.Value);

            // Signal 2 — which Unity API families the class body actually touches
            // (field types AND calls both count: an AudioSource field is the same
            // evidence as a PlayOneShot call). Closer proxy for "reasons to change"
            // than method names alone.
            var apiFamilies = DetectDependencyClusters(cls.Body);

            // Size note: method count measures size, not responsibility. It never
            // becomes a Medium/High violation on its own. Lifecycle methods excluded.
            int nonLifecycle = cls.Methods.Count(m => !UnityLifecycle.Contains(m));

            bool multiConcern = concerns.Count >= 2 && apiFamilies.Count >= 2; // names + APIs agree
            bool nameConcern  = concerns.Count >= 2;                           // names alone — lower confidence
            bool tooMany      = nonLifecycle > SRP_MAX_METHODS;

            // Signal 3 — structural cohesion (LCOM4). The strongest signal:
            // upgrades name+API agreement to High when method groups work on
            // disjoint field sets, and vetoes it down to Low when all methods
            // share one field cluster (the naming match was coincidence).
            // Only computed when it can change the outcome (multiConcern) — it's
            // O(methods² × regex) and would make large-project scans crawl if it
            // ran on every class.
            var cohesion = multiConcern ? ComputeCohesion(cls) : new CohesionInfo();

            if (!multiConcern && !nameConcern && !tooMany) return;

            string signalEvidence =
                  "Concern groups: " + string.Join(", ", concerns.Select(c => $"{c.Key}({c.Value.Count})"))
                + (apiFamilies.Count > 0 ? " · APIs touched: " + string.Join(", ", apiFamilies) : "");

            Severity sev; string title, desc, evidence;
            if (multiConcern && cohesion.Valid && cohesion.Clusters >= 2)
            {
                // Three independent signals agree — as close to proof as static analysis gets
                sev      = Severity.High;
                title    = $"{cls.Name} has multiple responsibilities";
                desc     = $"'{cls.Name}' names {concerns.Count} concerns, touches {apiFamilies.Count} unrelated API families, AND its methods split into {cohesion.Clusters} groups working on disjoint field sets. This class structurally serves multiple masters — each group is a separate reason to change.";
                evidence = signalEvidence + $" · Cohesion: {cohesion.Clusters} disjoint method-field clusters";
            }
            else if (multiConcern && cohesion.Valid && cohesion.Clusters == 1)
            {
                // Cohesion veto: names/APIs suggested mixed concerns, but every method
                // works on the same data — likely one responsibility after all.
                sev      = Severity.Low;
                title    = $"{cls.Name} may mix concerns (cohesive)";
                desc     = $"Method names and APIs in '{cls.Name}' suggest mixed concerns, but all methods operate on the same fields — likely a single responsibility. Review only if the class keeps growing.";
                evidence = signalEvidence + " · Cohesion veto: all methods share one field cluster";
            }
            else if (multiConcern)
            {
                // Names + APIs agree; cohesion couldn't be computed reliably (neutral)
                sev      = Severity.Medium;
                title    = $"{cls.Name} has multiple responsibilities";
                desc     = $"'{cls.Name}' groups methods around {concerns.Count} concerns and touches {apiFamilies.Count} unrelated API families. Each class should have a single reason to change.";
                evidence = signalEvidence;
            }
            else if (nameConcern)
            {
                sev      = Severity.Low;
                title    = $"{cls.Name} may mix concerns";
                desc     = $"Method names in '{cls.Name}' suggest more than one concern. Review whether these belong in one class — this is a naming-based hint, not a definite violation.";
                evidence = "Concern groups: " + string.Join(", ", concerns.Select(c => $"{c.Key}({c.Value.Count})"));
            }
            else
            {
                sev      = Severity.Low;
                title    = $"{cls.Name} is large ({nonLifecycle} methods)";
                desc     = $"'{cls.Name}' has {nonLifecycle} non-lifecycle methods. Size alone is not an SRP violation, but large classes are worth a look for hidden responsibilities.";
                evidence = $"{nonLifecycle} methods (excluding Unity lifecycle)";
            }

            result.Violations.Add(new Violation
            {
                Id          = $"SRP-{idx++:D3}",
                Principle   = SolidPrinciple.SRP,
                Severity    = sev,
                Title       = title,
                Description = desc,
                Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = cls.Name, StartLine = cls.StartLine, EndLine = cls.EndLine },
                OriginalCode = Trim(cls.Body, 400),
                Evidence     = evidence
            });
        }

        // API-family detection — which unrelated Unity/System subsystems a class touches.
        // Used as an independent confirmation signal for SRP, so a keyword coincidence in
        // method names can't produce a Medium violation on its own.
        private static readonly (string Name, string Pattern)[] DependencySignals =
        {
            ("Audio",       @"\bAudioSource\b|\bAudioClip\b|\bPlayOneShot\b|\bAudioMixer\b"),
            ("UI",          @"\bUnityEngine\.UI\b|\bCanvas\b|\bTextMeshPro\w*|\bTMP_\w+|\bButton\b|\bSlider\b"),
            ("Persistence", @"\bPlayerPrefs\b|\bFile\.\w+|\bJsonUtility\b|\bStreamWriter\b|\bBinaryFormatter\b"),
            ("Animation",   @"\bAnimator\b|\bSetTrigger\b|\bCrossFade\b"),
            ("Physics",     @"\bRigidbody2?D?\b|\bAddForce\b|\bRaycast\b"),
            ("Network",     @"\bUnityWebRequest\b|\bHttpClient\b|\bWebSocket\b"),
            ("SceneFlow",   @"\bSceneManager\b|\bLoadScene\b"),
        };

        private List<string> DetectDependencyClusters(string body)
        {
            var hits = new List<string>();
            foreach (var (name, pattern) in DependencySignals)
                if (Regex.IsMatch(body, pattern))
                    hits.Add(name);
            return hits;
        }

        // ── SRP cohesion signal (LCOM4-style) ─────────────────────────────────────
        // Maps each non-lifecycle method to the instance fields it touches, then
        // clusters methods connected by a shared field OR a direct call between them.
        //   2+ disjoint clusters → the class structurally serves multiple masters
        //   1 cluster            → naming/API signals were likely coincidence (veto)
        // Lifecycle methods (Awake/Start/...) are excluded from clustering — they
        // wire everything up and would glue unrelated clusters together.
        // The verdict is only trusted when the analysis covers most of the class
        // (Valid) — Unity code often works through transform/statics rather than
        // fields, and clustering the remaining sliver would be confident nonsense.

        private class CohesionInfo
        {
            public bool Valid;       // enough coverage to trust Clusters
            public int  Clusters;
            public int  Analyzable;  // methods that touch at least one field
        }

        private CohesionInfo ComputeCohesion(ClassInfo cls)
        {
            var info = new CohesionInfo();

            // Instance fields. Auto-properties and method declarations don't match:
            // they have '{' or '(' where this pattern requires '=' or ';'.
            var fieldRx = new Regex(
                @"(?:private|protected|internal|public)\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\],]+\s+(\w+)\s*(?:=[^;]*)?;");
            var fields = new List<string>();
            foreach (Match m in fieldRx.Matches(cls.Body))
                if (!fields.Contains(m.Groups[1].Value))
                    fields.Add(m.Groups[1].Value);
            if (fields.Count < 2) return info;

            var bodies = new Dictionary<string, string>();
            foreach (var mname in cls.Methods.Distinct())
            {
                if (UnityLifecycle.Contains(mname)) continue;
                string mbody = ExtractMethodBody(cls.Body, mname);
                if (mbody != null) bodies[mname] = mbody;
            }
            if (bodies.Count == 0) return info;

            // Field usage per method. A field mention doesn't count when a local
            // declaration shadows it ("int count = 0;" hides the field 'count').
            var uses = new Dictionary<string, HashSet<string>>();
            foreach (var kv in bodies)
            {
                var used = new HashSet<string>();
                foreach (var f in fields)
                {
                    string esc = Regex.Escape(f);
                    if (!Regex.IsMatch(kv.Value, @"\b" + esc + @"\b")) continue;
                    if (Regex.IsMatch(kv.Value,
                        @"(?:^|[;{}(]\s*)(?:var|int|float|double|bool|string|long|[A-Z][\w<>\[\],]*)\s+" + esc + @"\s*(?:[=;,)]|in\b)"))
                        continue;
                    used.Add(f);
                }
                if (used.Count > 0) uses[kv.Key] = used;
            }

            info.Analyzable = uses.Count;
            int nonLifecycle = cls.Methods.Distinct().Count(m => !UnityLifecycle.Contains(m));
            if (uses.Count < 3 || nonLifecycle == 0 || (float)uses.Count / nonLifecycle < 0.6f)
                return info;

            // Union-find over analyzable methods: connect on shared field or direct call
            var names  = uses.Keys.ToList();
            var parent = Enumerable.Range(0, names.Count).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

            for (int i = 0; i < names.Count; i++)
                for (int j = i + 1; j < names.Count; j++)
                {
                    bool shareField = uses[names[i]].Overlaps(uses[names[j]]);
                    bool calls =
                        Regex.IsMatch(bodies[names[i]], @"\b" + Regex.Escape(names[j]) + @"\s*\(") ||
                        Regex.IsMatch(bodies[names[j]], @"\b" + Regex.Escape(names[i]) + @"\s*\(");
                    if (shareField || calls) parent[Find(i)] = Find(j);
                }

            info.Clusters = Enumerable.Range(0, names.Count).Select(Find).Distinct().Count();
            info.Valid = true;
            return info;
        }

        // Body of a named method: brace block or "=> expression;". Overloads are merged.
        private string ExtractMethodBody(string classBody, string methodName)
        {
            var sigRx = new Regex(@"[\w<>\[\]]+\s+" + Regex.Escape(methodName) + @"\s*\(");
            string combined = null;
            foreach (Match m in sigRx.Matches(classBody))
            {
                int close = classBody.IndexOf(')', m.Index + m.Length - 1);
                if (close < 0) continue;
                int i = close + 1;
                while (i < classBody.Length && char.IsWhiteSpace(classBody[i])) i++;
                string part = null;
                if (i < classBody.Length && classBody[i] == '{')
                    part = ExtractBlock(classBody, i);
                else if (i + 1 < classBody.Length && classBody[i] == '=' && classBody[i + 1] == '>')
                {
                    int semi = classBody.IndexOf(';', i);
                    if (semi > 0) part = classBody.Substring(i + 2, semi - i - 2);
                }
                if (part != null) combined = combined == null ? part : combined + "\n" + part;
            }
            return combined;
        }

        private Dictionary<string, List<string>> DetectConcerns(List<string> methods)
        {
            var map = new Dictionary<string, string[]>
            {
                ["Movement"]  = new[] { "Move","Jump","Walk","Run","Teleport","Dash" },
                ["Combat"]    = new[] { "Attack","Hit","Shoot","Fire","Damage","Kill","Die" },
                ["Audio"]     = new[] { "PlaySound","PlayMusic","PlayClip","PlayAudio","PlaySfx","Sound","Music","Audio","Volume","Mute" },
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

                // Count cases inside THIS switch's block only — counting to the end of
                // the class would let a later switch inflate this one's case count.
                string switchBlock = ExtractBlock(cls.Body, m.Index);
                int cases = Regex.Matches(switchBlock, @"\bcase\b").Count;
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

            // Find if/else chains on string equality. Matches must be ADJACENT (within a
            // few lines of each other) to count as a chain — three unrelated string
            // comparisons scattered across different methods are not an OCP violation.
            var ifChainRx = new Regex(@"if\s*\(.+==\s*""(\w+)""\)", RegexOptions.Multiline);
            var chainHits = ifChainRx.Matches(cls.Body).Cast<Match>()
                .Select(cm => new { M = cm, Line = LineOf(cls.Body, cm.Index) })
                .OrderBy(t => t.Line)
                .ToList();

            int start = 0;
            while (start < chainHits.Count)
            {
                int end = start;
                while (end + 1 < chainHits.Count && chainHits[end + 1].Line - chainHits[end].Line <= 3) end++;

                int chainLen = end - start + 1;
                if (chainLen >= 3)
                {
                    int line = cls.StartLine + chainHits[start].Line - 1;
                    result.Violations.Add(new Violation
                    {
                        Id          = $"OCP-{idx++:D3}",
                        Principle   = SolidPrinciple.OCP,
                        Severity    = Severity.Medium,
                        Title       = $"Type if/else chain in {cls.Name}",
                        Description = "Long if/else chain comparing string types. Each new type means editing this method. Use polymorphism instead.",
                        Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = cls.Name, StartLine = line },
                        OriginalCode = Trim(chainHits[start].M.Value, 200),
                        Evidence    = $"{chainLen} adjacent string-comparison branches"
                    });
                }
                start = end + 1;
            }
        }

        private bool IsTypeVar(string name)
        {
            var keywords = new[] { "type", "kind", "category", "variant", "mode", "tag", "enemy", "item", "name" };
            return keywords.Any(k => name.Contains(k));
        }

        // ── LSP ───────────────────────────────────────────────────────────────────
        // Flag substitutability breaks: methods that only throw NotImplemented/
        // NotSupported (block or expression-bodied), and empty overrides that
        // silently drop base-class behavior.

        private void CheckLSP(ClassInfo cls, string filePath, FileAnalysisResult result, ref int idx,
                              HashSet<string> ispCovered = null)
        {
            ispCovered ??= new HashSet<string>();

            // Throw-only bodies:  { throw new NotImplementedException(); }
            // and expression-bodied:  => throw new NotImplementedException();
            var throwRxs = new[]
            {
                new Regex(@"(public|private|protected|override)\s+[\w<>\[\]]+\s+(\w+)\s*\([^)]*\)\s*\{\s*throw\s+new\s+Not(Implemented|Supported)Exception[^}]*\}",
                    RegexOptions.Multiline | RegexOptions.Singleline),
                new Regex(@"(public|private|protected|override)\s+[\w<>\[\]]+\s+(\w+)\s*\([^)]*\)\s*=>\s*throw\s+new\s+Not(Implemented|Supported)Exception[^;]*;",
                    RegexOptions.Multiline)
            };

            foreach (var rx in throwRxs)
            foreach (Match m in rx.Matches(cls.Body))
            {
                string methodName = m.Groups[2].Value;
                // Already reported as part of a fat-interface (ISP) violation — one
                // root cause should not drag down two principle scores.
                if (ispCovered.Contains(methodName)) continue;
                int line = cls.StartLine + LineOf(cls.Body, m.Index) - 1;

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

            // Empty overrides:  override void Attack() { }
            // Silently dropping inherited behavior means the subclass no longer honors
            // the base contract — callers holding the base type get surprised.
            var emptyOverrideRx = new Regex(
                @"(public|private|protected|internal)?\s*override\s+[\w<>\[\]]+\s+(\w+)\s*\([^)]*\)\s*\{\s*\}",
                RegexOptions.Multiline);

            foreach (Match m in emptyOverrideRx.Matches(cls.Body))
            {
                string methodName = m.Groups[2].Value;
                if (ispCovered.Contains(methodName)) continue;
                int line = cls.StartLine + LineOf(cls.Body, m.Index) - 1;

                result.Violations.Add(new Violation
                {
                    Id          = $"LSP-{idx++:D3}",
                    Principle   = SolidPrinciple.LSP,
                    Severity    = Severity.Medium,
                    Title       = $"{cls.Name}.{methodName}() is an empty override",
                    Description = "Override with an empty body silently removes the base-class behavior. Callers using the base type expect this method to do its job — implement it, call base, or rethink the hierarchy.",
                    Location    = new CodeLocation { FilePath = filePath, FileName = Path.GetFileName(filePath), ClassName = cls.Name, MemberName = methodName, StartLine = line },
                    OriginalCode = Trim(m.Value, 200),
                    Evidence    = $"empty override body in {methodName}"
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

        // Returns the set of throwing method names covered by an ISP violation (empty if
        // none fired) so CheckLSP can skip them — one root cause, one finding.
        private HashSet<string> CheckISP_Implementor(ClassInfo cls, string filePath, FileAnalysisResult result, ref int idx)
        {
            var covered = new HashSet<string>();

            var throwRxs = new[]
            {
                new Regex(@"(public|override)\s+[\w<>\[\]]+\s+(\w+)\s*\([^)]*\)\s*\{\s*throw\s+new\s+Not(Implemented|Supported)Exception[^}]*\}", RegexOptions.Singleline),
                new Regex(@"(public|override)\s+[\w<>\[\]]+\s+(\w+)\s*\([^)]*\)\s*=>\s*throw\s+new\s+Not(Implemented|Supported)Exception[^;]*;", RegexOptions.Multiline)
            };

            var throwingNames = new List<string>();
            foreach (var rx in throwRxs)
                foreach (Match m in rx.Matches(cls.Body))
                    if (!throwingNames.Contains(m.Groups[2].Value))
                        throwingNames.Add(m.Groups[2].Value);

            int throwCount = throwingNames.Count;
            if (throwCount < 3) return covered;
            if (cls.Methods.Count == 0) return covered;

            double ratio = (double)throwCount / cls.Methods.Count * 100;
            if (ratio < 50) return covered;

            foreach (var n in throwingNames) covered.Add(n);

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

            return covered;
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
        // Severity-weighted findings per scanned file (High=3, Med=2, Low=1).
        // This is the continuous number behind Score — it moves on every rescan,
        // so fixing violations shows progress even within the same 1–5 band.
        public float          Density     { get; set; }
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
        // Shown in every report footer and on the results screen. Ratings measure
        // code health for teams/self-assessment — never individual performance.
        public const string Disclaimer =
            "CodeShield ratings track code health for teams and self-assessment — they are not designed for evaluating individual developers.";

        // Score map — density-based (like SonarQube's debt ratio / Code Climate GPA):
        // score comes from severity-weighted findings PER SCANNED FILE, not absolute
        // counts, so a 900-file game isn't judged on the same raw numbers as a 15-file
        // jam project, and fixing violations moves the rating even before reaching zero.
        //
        // density = (High*3 + Medium*2 + Low*1) / files scanned
        // 5 = 0 findings   4 = density ≤ 0.10   3 = ≤ 0.40   2 = ≤ 1.00   1 = > 1.00
        // Small-count floors: a couple of non-High findings never tank a small project.

        // Score → label mapping. Target is 4 — anything below should NOT read as "good enough".
        // 5 = On Target
        // 4 = Meets Target
        // 3 = Below Target
        // 2 = Needs Work
        // 1 = Critical
        private static readonly string[] Labels = { "", "Critical", "Needs Work", "Below Target", "Meets Target", "On Target" };

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

                int score = CalcScore(violations, results.Count);
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
                    FilesScanned = results.Count,
                    Density      = WeightedLoad(violations) / System.Math.Max(1, results.Count)
                });
            }

            float avg = report.Ratings.Count > 0
                ? (float)report.Ratings.Sum(r => r.Score) / report.Ratings.Count
                : 0f;
            report.OverallScore = avg;
            // Thresholds aligned with the exported docx report. Target is 4.0.
            report.OverallLabel = avg >= 4.5f ? "On Target"
                                : avg >= 4.0f ? "Meets Target"
                                : avg >= 3.0f ? "Below Target"
                                : avg >= 2.0f ? "Needs Work"
                                : "Critical";

            return report;
        }

        private static float WeightedLoad(List<Violation> violations)
            => violations.Sum(v => v.Severity == Severity.High   ? 3f
                                 : v.Severity == Severity.Medium ? 2f
                                 : 1f);

        private static int CalcScore(List<Violation> violations, int filesScanned)
        {
            if (violations.Count == 0) return 5;

            float density = WeightedLoad(violations) / System.Math.Max(1, filesScanned);

            int score = density <= 0.10f ? 4
                      : density <= 0.40f ? 3
                      : density <= 1.00f ? 2
                      : 1;

            // Small-count floors: on a small project even one Medium finding produces a
            // high density — a handful of non-High findings shouldn't read as "Critical".
            int highCount = violations.Count(v => v.Severity == Severity.High);
            if (highCount == 0 && violations.Count <= 2) score = System.Math.Max(score, 4);
            else if (highCount == 0 && violations.Count <= 5) score = System.Math.Max(score, 3);

            // Low findings are informational notes/hints, not violations — they may
            // nudge a score but must never sink a principle on their own. A principle
            // with zero Medium/High findings never drops below 4.
            if (violations.All(v => v.Severity == Severity.Low))
                score = System.Math.Max(score, 4);

            return score;
        }
    }
}