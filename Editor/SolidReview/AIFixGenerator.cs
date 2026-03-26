using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SolidAgent
{
    public class AIFixGenerator
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly string _apiKey;
        private const string API_URL   = "https://api.anthropic.com/v1/messages";
        private const string MODEL     = "claude-sonnet-4-20250514";
        private const int    MAX_TOKENS = 4096;

        public AIFixGenerator(string apiKey)
        {
            _apiKey = apiKey;
            if (!_http.DefaultRequestHeaders.Contains("x-api-key"))
            {
                _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
                _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
        }

        public async Task<GeneratedFix> GenerateFixAsync(Violation v, string fullSource)
        {
            string system = BuildSystem(v.Principle);
            string user   = BuildUser(v, fullSource);

            var body = JsonConvert.SerializeObject(new
            {
                model      = MODEL,
                max_tokens = MAX_TOKENS,
                system     = system,
                messages   = new[] { new { role = "user", content = user } }
            });

            var req = new HttpRequestMessage(HttpMethod.Post, API_URL)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-api-key", _apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");

            var resp = await _http.SendAsync(req);
            var raw  = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new System.Exception($"API error {resp.StatusCode}: {raw}");

            var json = JObject.Parse(raw);
            var text = json["content"]?[0]?["text"]?.ToString() ?? "";

            return Parse(text, v.Id);
        }

        // ── Prompts ───────────────────────────────────────────────────────────────

        private string BuildSystem(SolidPrinciple p)
        {
            string core = @"You are a Unity C# refactoring assistant. Fix SOLID violations in casual mobile game scripts.
Rules:
- Keep MonoBehaviour lifecycle methods intact (Start, Update, Awake, OnDestroy, FixedUpdate, etc.)
- NO dependency injection or IoC containers — use GetComponent, FindObjectOfType, UnityEvents
- Minimal targeted fix — don't refactor beyond the violation
- Preserve all existing behavior exactly
- Output ONLY this JSON (no markdown, no preamble):
{""fixedCode"":""..."",""newFiles"":[""FileName.cs""],""diffSummary"":""..."",""explanation"":""...""}";

            string guide = p switch
            {
                SolidPrinciple.SRP =>
                    "\nSRP: Split into focused MonoBehaviours. Wire with GetComponent or [RequireComponent].",
                SolidPrinciple.OCP =>
                    "\nOCP: Replace type switch/if-else with interface + subclasses. ScriptableObjects work well for data-driven variants.",
                SolidPrinciple.LSP =>
                    "\nLSP: Never throw NotImplementedException. Either implement the method properly or apply ISP first to remove it from the interface.",
                SolidPrinciple.ISP =>
                    "\nISP: Split fat interface into 2-4 small ones (IMovable, IDamageable, IAttacker). Classes implement only what they need.",
                _ => ""
            };

            return core + guide;
        }

        private string BuildUser(Violation v, string src) =>
            $"Fix this {v.Principle} violation.\n\n" +
            $"ID: {v.Id}\nSeverity: {v.Severity}\nIssue: {v.Title}\n" +
            $"Description: {v.Description}\nEvidence: {v.Evidence}\n\n" +
            $"Affected code (line {v.Location.StartLine}):\n```csharp\n{v.OriginalCode}\n```\n\n" +
            $"Full file:\n```csharp\n{src}\n```\n\nOutput JSON only.";

        // ── Parse response ────────────────────────────────────────────────────────

        private GeneratedFix Parse(string text, string id)
        {
            text = text.Trim();
            if (text.StartsWith("```json")) text = text.Substring(7);
            else if (text.StartsWith("```")) text = text.Substring(3);
            if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);
            text = text.Trim();

            try
            {
                var obj = JObject.Parse(text);
                var fix = new GeneratedFix
                {
                    ViolationId = id,
                    FixedCode   = obj["fixedCode"]?.ToString()   ?? text,
                    DiffSummary = obj["diffSummary"]?.ToString() ?? "",
                    Explanation = obj["explanation"]?.ToString() ?? ""
                };
                var newFiles = obj["newFiles"] as JArray;
                if (newFiles != null)
                    foreach (var f in newFiles)
                        fix.NewFilesNeeded.Add(f.ToString());
                return fix;
            }
            catch
            {
                return new GeneratedFix
                {
                    ViolationId = id,
                    FixedCode   = text,
                    DiffSummary = "Fix generated — review before applying.",
                    Explanation = "Review the code above."
                };
            }
        }
    }
}
