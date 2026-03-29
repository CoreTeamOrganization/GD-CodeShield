// AIFixGenerator.cs — single API call, fast and within rate limits.
// For better results with full project context, use the Claude Code prompt button.

using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SolidAgent
{
    public class AIFixGenerator
    {
        private static readonly HttpClient _http = CreateHttpClient();
        private readonly string _apiKey;

        private const string API_URL   = "https://api.anthropic.com/v1/messages";
        private const string MODEL     = "claude-sonnet-4-20250514";
        private const int    MAX_TOKENS = 4096;

        private static HttpClient CreateHttpClient()
        {
            var handler = new System.Net.Http.HttpClientHandler();
            try { handler.ServerCertificateCustomValidationCallback = (m, c, ch, e) => true; } catch { }
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 |
                    System.Net.SecurityProtocolType.Tls13;
            }
            catch { }
            return new HttpClient(handler);
        }

        public AIFixGenerator(string apiKey)
        {
            _apiKey = apiKey;
            // Always refresh key — static client persists across instances
            if (_http.DefaultRequestHeaders.Contains("x-api-key"))
                _http.DefaultRequestHeaders.Remove("x-api-key");
            _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            if (!_http.DefaultRequestHeaders.Contains("anthropic-version"))
                _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        public async Task<GeneratedFix> GenerateFixAsync(Violation v, string fullSource,
            List<Violation> allViolations = null, string namespaceContext = "")
        {
            allViolations = allViolations ?? new List<Violation> { v };

            string system  = BuildSystem(allViolations);
            string userMsg = BuildUser(v, fullSource, allViolations, namespaceContext);

            var body = JsonConvert.SerializeObject(new
            {
                model      = MODEL,
                max_tokens = MAX_TOKENS,
                system     = system,
                messages   = new[] { new { role = "user", content = userMsg } }
            });

            var req = new HttpRequestMessage(HttpMethod.Post, API_URL)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            var resp = await _http.SendAsync(req);
            var raw  = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"API error {resp.StatusCode}: {raw}");

            var json = JObject.Parse(raw);
            var text = json["content"]?[0]?["text"]?.ToString() ?? "";

            return Parse(text, v.Id);
        }

        // ── Prompts ───────────────────────────────────────────────────────────────

        private string BuildSystem(List<Violation> violations)
        {
            var principles = violations.Select(vl => vl.Principle).Distinct().ToList();

            string core = @"You are a Unity C# refactoring assistant. Fix ALL listed SOLID violations in one pass.
Rules:
- Fix every violation listed — produce ONE complete fixed file
- Keep MonoBehaviour lifecycle methods intact (Start, Update, Awake, OnDestroy, FixedUpdate, etc.)
- NO dependency injection or IoC containers — use GetComponent, FindObjectOfType, UnityEvents
- Preserve all existing public behavior exactly

COMPILE RULES — a single missing file causes the entire project to fail:
- Every new file MUST use the EXACT same namespace as the original file
- Copy ALL using directives from the original file into every new file
- EVERY class you reference in fixedCode that does not exist in the original source MUST have a NEW_FILE block
- Before writing FIXED_FILE_END, count every new class name in fixedCode — each one needs a NEW_FILE_START block
- Do NOT stop outputting until every new class has been fully defined

SELF-CHECK before FIXED_FILE_END:
Ask yourself: 'Does every GetComponent<X>() call have a corresponding NEW_FILE_START: X.cs block?'
If not — output those missing NEW_FILE blocks before FIXED_FILE_END.

CRITICAL OUTPUT FORMAT — plain text, no JSON, no markdown:
DIFF_SUMMARY: <one sentence>
EXPLANATION: <one sentence>
NEW_FILES: <ALL new .cs filenames, comma-separated — must match every NEW_FILE_START below>

FIXED_FILE_START
<complete fixed C# file>
FIXED_FILE_END

NEW_FILE_START: FileName.cs
<complete compilable C# — same namespace, all usings from original>
NEW_FILE_END

Repeat NEW_FILE_START...NEW_FILE_END for EVERY new class referenced in the fixed file.";

            var guides = new StringBuilder();
            foreach (var p in principles)
            {
                string g = p switch
                {
                    SolidPrinciple.SRP =>
                        "\nSRP: Split into focused MonoBehaviours. Original becomes thin orchestrator with GetComponent wiring." +
                        "\nPattern: private XController _x; void Awake(){ _x = GetComponent<XController>(); } public void Do(){ _x.Do(); }" +
                        "\nCRITICAL FOR SRP: Every controller class you reference with GetComponent<X>() MUST have a NEW_FILE_START: X.cs block." +
                        "\nIf you write GetComponent<AnalyticsEventsController>() you MUST also write NEW_FILE_START: AnalyticsEventsController.cs" +
                        "\nNew classes must NOT reference each other.",
                    SolidPrinciple.OCP =>
                        "\nOCP: Replace type switch/if-else with interface + subclasses.",
                    SolidPrinciple.LSP =>
                        "\nLSP: Never throw NotImplementedException. Implement properly or apply ISP first.",
                    SolidPrinciple.ISP =>
                        "\nISP: Split fat interface into 2-4 small focused interfaces.",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(g)) guides.Append(g);
            }

            return core + guides;
        }

        private string BuildUser(Violation primary, string src, List<Violation> allViolations,
            string namespaceContext = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Fix ALL {allViolations.Count} SOLID violation(s) in {primary.Location.FileName}.");
            sb.AppendLine();

            for (int i = 0; i < allViolations.Count; i++)
            {
                var v = allViolations[i];
                sb.AppendLine($"VIOLATION {i + 1}: {v.Principle} — {v.Title}");
                sb.AppendLine($"  Evidence: {v.Evidence}  (line {v.Location.StartLine})");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(namespaceContext))
            {
                sb.AppendLine(namespaceContext);
                sb.AppendLine("Use ONLY the namespace and types shown above and in the file below.");
                sb.AppendLine();
            }

            sb.AppendLine("Full file source:");
            sb.AppendLine("```csharp");
            sb.AppendLine(src);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("Output using the required plain-text format. Address ALL violations. All new files must compile.");
            return sb.ToString();
        }

        // ── Parse response ────────────────────────────────────────────────────────

        private GeneratedFix Parse(string text, string id)
        {
            text = text.Trim();
            var fix = new GeneratedFix { ViolationId = id };

            try
            {
                var lines      = text.Split('\n');
                var sb         = new StringBuilder();
                bool inFixed   = false;
                bool inNewFile = false;
                string newFileName = null;
                var newFileSb  = new StringBuilder();

                foreach (var rawLine in lines)
                {
                    string line    = rawLine.TrimEnd('\r');
                    string trimmed = line.Trim();

                    if (!inFixed && !inNewFile)
                    {
                        if (trimmed.StartsWith("DIFF_SUMMARY:"))
                        { fix.DiffSummary = trimmed.Substring("DIFF_SUMMARY:".Length).Trim(); continue; }
                        if (trimmed.StartsWith("EXPLANATION:"))
                        { fix.Explanation = trimmed.Substring("EXPLANATION:".Length).Trim(); continue; }
                        if (trimmed.StartsWith("NEW_FILES:"))
                        {
                            string fl = trimmed.Substring("NEW_FILES:".Length).Trim();
                            if (!string.IsNullOrEmpty(fl) && fl != "NONE")
                                foreach (var f in fl.Split(','))
                                { string fn = f.Trim(); if (!string.IsNullOrEmpty(fn)) fix.NewFilesNeeded.Add(fn); }
                            continue;
                        }
                    }

                    if (trimmed == "FIXED_FILE_START") { inFixed = true; continue; }
                    if (trimmed == "FIXED_FILE_END")   { inFixed = false; fix.FixedCode = sb.ToString().TrimEnd(); continue; }

                    if (trimmed.StartsWith("NEW_FILE_START:"))
                    {
                        inNewFile   = true;
                        newFileName = trimmed.Substring("NEW_FILE_START:".Length).Trim();
                        newFileSb.Clear();
                        continue;
                    }
                    if (trimmed == "NEW_FILE_END" && inNewFile)
                    {
                        if (!string.IsNullOrEmpty(newFileName))
                        {
                            fix.NewFileContents[newFileName] = newFileSb.ToString().TrimEnd();
                            if (!fix.NewFilesNeeded.Contains(newFileName))
                                fix.NewFilesNeeded.Add(newFileName);
                        }
                        inNewFile = false; newFileName = null;
                        continue;
                    }

                    if (inFixed)   { sb.AppendLine(line); continue; }
                    if (inNewFile) { newFileSb.AppendLine(line); continue; }
                }

                // Fallback to markdown fences
                if (string.IsNullOrWhiteSpace(fix.FixedCode))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        text, @"```(?:csharp|cs)?\r?\n(.*?)```",
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                    fix.FixedCode = m.Success ? m.Groups[1].Value.TrimEnd() : text;
                }

                if (string.IsNullOrEmpty(fix.DiffSummary)) fix.DiffSummary = "Fix applied.";
                if (string.IsNullOrEmpty(fix.Explanation))  fix.Explanation = "See fixed code.";
            }
            catch
            {
                fix.FixedCode   = text;
                fix.DiffSummary = "Fix generated — review before applying.";
            }

            return fix;
        }
    }
}
