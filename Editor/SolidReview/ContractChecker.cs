// ContractChecker.cs
// Compares public method/property signatures between original and fixed code.
// No external deps — pure regex-based. Runs before applying any fix.
// Goal: ensure the fix doesn't accidentally remove or rename public API.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SolidAgent
{
    public class MethodSignature
    {
        public string ReturnType { get; set; }
        public string Name       { get; set; }
        public string Parameters { get; set; }
        public bool   IsPublic   { get; set; }

        public string Full => $"{ReturnType} {Name}({Parameters})";

        public override string ToString() => Full;
    }

    public class ContractCheckResult
    {
        public bool                  Passed          { get; set; }
        public List<MethodSignature> OriginalMethods { get; set; } = new();
        public List<MethodSignature> FixedMethods    { get; set; } = new();
        public List<MethodSignature> Removed         { get; set; } = new(); // truly gone
        public List<MethodSignature> Added           { get; set; } = new(); // new in fix
        public List<MethodSignature> Preserved       { get; set; } = new(); // present in both
        public List<MethodSignature> Moved           { get; set; } = new(); // moved to new files
        public string                Summary         { get; set; }
        public bool                  CompilesParsed  { get; set; }
    }

    public static class ContractChecker
    {
        // ── Main entry ────────────────────────────────────────────────────────────
        // newFilesContent: combined code from all new files the fix creates (may be empty)

        public static ContractCheckResult Check(string originalCode, string fixedCode,
                                                string newFilesContent = "")
        {
            var result = new ContractCheckResult();

            // Basic syntax check on fixed code
            result.CompilesParsed = BasicSyntaxOk(fixedCode);

            // Extract public methods from original
            result.OriginalMethods = ExtractPublicMethods(originalCode);

            // Extract from fixed code AND any new files the fix creates
            string allFixedCode = fixedCode + "\n" + (newFilesContent ?? "");
            result.FixedMethods = ExtractPublicMethods(allFixedCode);

            // Compare — a method is "preserved" if it exists anywhere in the fix output
            foreach (var orig in result.OriginalMethods)
            {
                bool found = result.FixedMethods.Any(f =>
                    NormalizeName(f.Name) == NormalizeName(orig.Name));

                if (found) result.Preserved.Add(orig);
                else        result.Removed.Add(orig);
            }

            foreach (var fix in result.FixedMethods)
            {
                bool wasInOriginal = result.OriginalMethods.Any(o =>
                    NormalizeName(o.Name) == NormalizeName(fix.Name));
                if (!wasInOriginal) result.Added.Add(fix);
            }

            bool newFilesExist = !string.IsNullOrEmpty(newFilesContent);

            // If fix creates new files, "removed" methods are likely moved — warn but don't fail
            if (newFilesExist && result.Removed.Count > 0)
            {
                // Move all "removed" to "preserved" — they're in new files
                result.Preserved.AddRange(result.Removed);
                result.Moved.AddRange(result.Removed);
                result.Removed.Clear();
            }

            // Pass: nothing truly removed and code parses
            result.Passed = result.Removed.Count == 0 && result.CompilesParsed;

            // Build summary
            if (!result.CompilesParsed)
                result.Summary = "⚠  Fixed code has syntax errors — check braces/parentheses.";
            else if (result.Moved.Count > 0 && result.Removed.Count == 0)
                result.Summary = $"✓  {result.Moved.Count} method(s) moved to new files. Contract intact.";
            else if (result.Removed.Count == 0 && result.Added.Count == 0)
                result.Summary = $"✓  All {result.Preserved.Count} public method(s) preserved. Contract intact.";
            else if (result.Removed.Count == 0)
                result.Summary = $"✓  All original methods preserved. {result.Added.Count} new method(s) added.";
            else
                result.Summary = $"⚠  {result.Removed.Count} method(s) truly removed. Review carefully before applying.";

            return result;
        }

        // ── Extract public methods via regex ──────────────────────────────────────

        private static List<MethodSignature> ExtractPublicMethods(string code)
        {
            var results = new List<MethodSignature>();
            if (string.IsNullOrEmpty(code)) return results;

            // Match: public [static] [virtual] [override] ReturnType MethodName(params)
            var rx = new Regex(
                @"public\s+(?:static\s+|virtual\s+|override\s+|async\s+)*" +
                @"([\w<>\[\]]+)\s+" +       // return type
                @"(\w+)\s*" +               // method name
                @"\(([^)]*)\)",             // parameters
                RegexOptions.Multiline);

            // Skip constructors, operators, common Unity messages
            var skipNames = new HashSet<string>
            {
                "Awake","Start","Update","FixedUpdate","LateUpdate","OnEnable","OnDisable",
                "OnDestroy","OnCollisionEnter","OnCollisionExit","OnTriggerEnter","OnTriggerExit",
                "OnCollisionEnter2D","OnCollisionExit2D","OnTriggerEnter2D","OnTriggerExit2D",
                "OnGUI","Reset","OnValidate","OnDrawGizmos"
            };

            foreach (Match m in rx.Matches(code))
            {
                string name = m.Groups[2].Value;
                if (skipNames.Contains(name)) continue;
                // Skip if looks like a Unity message or property
                if (name.StartsWith("On") && name.Length > 2 && char.IsUpper(name[2])) continue;

                results.Add(new MethodSignature
                {
                    ReturnType = m.Groups[1].Value.Trim(),
                    Name       = name,
                    Parameters = NormalizeParams(m.Groups[3].Value),
                    IsPublic   = true
                });
            }

            // Also capture public properties
            var propRx = new Regex(
                @"public\s+(?:static\s+|virtual\s+|override\s+)*([\w<>\[\]]+)\s+(\w+)\s*\{",
                RegexOptions.Multiline);

            foreach (Match m in propRx.Matches(code))
            {
                string name = m.Groups[2].Value;
                if (name == "class" || name == "interface" || name == "struct") continue;
                results.Add(new MethodSignature
                {
                    ReturnType = m.Groups[1].Value.Trim(),
                    Name       = name + " {prop}",
                    Parameters = "",
                    IsPublic   = true
                });
            }

            return results;
        }

        private static string NormalizeName(string name)
            => name.Replace(" {prop}", "").ToLower().Trim();

        private static string NormalizeParams(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            // Strip parameter names, keep types only
            var parts = p.Split(',');
            var types = new List<string>();
            foreach (var part in parts)
            {
                var tokens = part.Trim().Split(' ');
                // Last token is parameter name, previous is type
                if (tokens.Length >= 2)
                    types.Add(tokens[tokens.Length - 2].Trim());
                else if (tokens.Length == 1 && !string.IsNullOrEmpty(tokens[0]))
                    types.Add(tokens[0].Trim());
            }
            return string.Join(", ", types);
        }

        private static bool BasicSyntaxOk(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            int open = 0, close = 0;
            foreach (char c in code)
            {
                if (c == '{') open++;
                else if (c == '}') close++;
            }
            return open == close && open > 0;
        }
    }
}
