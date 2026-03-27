// ReleaseScanner.cs
// Extends AssetScanner with Player Settings, Build config, and Manual device checks.
// Produces additional FieldResult rows under TAB_PRERELEASE, TAB_BUILD, TAB_MANUAL
// so everything lands in the same ScanResult used by ChecklistWindow.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GDChecklist
{
    public static class ReleaseScanner
    {
        // New tab indices — appended after the existing SDK tabs (0-5)
        public const int TAB_PRERELEASE = 6;
        public const int TAB_BUILD      = 7;
        public const int TAB_MANUAL     = 8;

        // ════════════════════════════════════════════════════════════════════════
        //  ENTRY — called after AssetScanner.Scan(), appends to same ScanResult
        // ════════════════════════════════════════════════════════════════════════
        public static void AppendChecks(ScanResult result, string dataPath)
        {
            string projectPath  = Path.GetDirectoryName(dataPath);
            string settingsPath = Path.Combine(projectPath, "ProjectSettings", "ProjectSettings.asset");
            string yaml         = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : string.Empty;

            // ── PRE-RELEASE CHECKS ────────────────────────────────────────────
            CheckAppVersion(result, yaml);
            CheckBundleCode(result, yaml);
            CheckGraphicsAPI(result, yaml);
            CheckUnityServices(result, yaml);
            CheckFirebaseFiles(result, dataPath);
            CheckAdjustEnvironment(result, dataPath);
            CheckAppLovinMaxTerms(result, dataPath);
            CheckAppLovinAdReview(result, dataPath);
            CheckAppMetricaSettings(result, dataPath);

            // ── BUILD SETTINGS ────────────────────────────────────────────────
            CheckSymbolsPublic(result, yaml);
            CheckCompressionLZ4HC(result, yaml);

            // ── MANUAL DEVICE CHECKS ──────────────────────────────────────────
            Manual(result, "Monetization",  "Firebase remote config values updated",
                "Verify all remote config keys are current in Firebase Console");
            Manual(result, "Monetization",  "Test & real ads verified on device",
                "Toggle test device mode, confirm banner/interstitial/rewarded load");
            Manual(result, "Monetization",  "AppOpen shows from 2nd launch only",
                "Cold launch twice — AppOpen must NOT appear on first launch");
            Manual(result, "Monetization",  "In-app purchases working on device",
                "Use sandbox account, complete purchase, verify receipt validation");
            Manual(result, "Monetization",  "Consent / privacy policy current",
                "Check GDPR consent popup + privacy policy URL is live and up to date");
            Manual(result, "Debugging",     "Adjust production + sandbox verified",
                "Adjust Dashboard → Testing Console → confirm events arriving");
            Manual(result, "Debugging",     "Internet panel shows correctly",
                "Disable WiFi, launch app, confirm offline/no-internet panel appears");
            Manual(result, "Permissions",   "APK permissions checked via analyzer",
                "Upload APK to APK analyzer — verify no sensitive/unknown permissions");
            Manual(result, "Dashboards",    "Adjust testing console verified",
                "Check installs, revenue and in-app data flowing to Adjust dashboard");
            Manual(result, "Dashboards",    "Firebase DebugView verified",
                "Firebase Console → DebugView → events visible during test session");
            Manual(result, "Dashboards",    "AppMetrica events verified",
                "AppMetrica Dashboard → Events → check event names and counts");
            Manual(result, "Submission",    "Symbol files provided with build",
                "Include .symbols.zip when uploading to Play Console");
            Manual(result, "Post-Release",  "AppMetrica — users & new users normal",
                "AppMetrica → Audience Panel → compare D1/D7 vs previous version");
            Manual(result, "Post-Release",  "AppMetrica — revenue reporting normal",
                "AppMetrica → Revenue → compare ARPU vs previous version");
            Manual(result, "Post-Release",  "Firebase — no revenue discrepancy",
                "Firebase → Analytics → Revenue → compare with AppMetrica figures");
            Manual(result, "Post-Release",  "Firebase — new installs tracking normal",
                "Firebase → Dashboard → New Users (first 24h post-release)");
            Manual(result, "Post-Release",  "Adjust — installs & revenue data normal",
                "Adjust → Overview → installs, sessions, revenue (first 24h)");
            Manual(result, "Post-Release",  "Play Console — no Android vitals spikes",
                "Play Console → Android Vitals → crash & ANR rates vs previous build");
            Manual(result, "Post-Release",  "AppLovin — all ad units active",
                "MAX Dashboard → Ad Units → check fill rate and eCPM per unit");
            Manual(result, "Post-Release",  "AppLovin — ad view rate consistent",
                "MAX Dashboard → Impression rate (D1 post-release vs previous)");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PRE-RELEASE AUTO CHECKS
        // ════════════════════════════════════════════════════════════════════════

        static void CheckAppVersion(ScanResult r, string yaml)
        {
            string ver = PlayerSettings.bundleVersion;
            bool ok    = !string.IsNullOrEmpty(ver) && ver != "1.0" && ver != "0.1" && ver != "1";
            AddReleaseField(r, TAB_PRERELEASE, "Player Settings", "App Version",
                detail:   $"Current: {ver}",
                howToFix: "Project Settings → Player → Version",
                pass: ok, warn: !ok);
        }

        static void CheckBundleCode(ScanResult r, string yaml)
        {
            // Read from YAML directly — works regardless of active build target
            var match = Regex.Match(yaml, @"AndroidBundleVersionCode:\s*(\d+)");
            if (match.Success)
            {
                int code = int.Parse(match.Groups[1].Value);
                bool ok  = code > 1;
                AddReleaseField(r, TAB_PRERELEASE, "Player Settings", "Bundle Version Code (Android)",
                    detail:   $"Current: {code}",
                    howToFix: "Project Settings → Player → Android → Bundle Version Code",
                    pass: ok, warn: !ok);
            }

            var iosMatch = Regex.Match(yaml, @"iOSBuildNumber:\s*(\S+)");
            if (iosMatch.Success)
            {
                string build = iosMatch.Groups[1].Value.Trim('"');
                bool ok = !string.IsNullOrEmpty(build) && build != "0" && build != "1";
                AddReleaseField(r, TAB_PRERELEASE, "Player Settings", "Build Number (iOS)",
                    detail:   $"Current: {build}",
                    howToFix: "Project Settings → Player → iOS → Build",
                    pass: ok, warn: !ok);
            }
        }

        static void CheckGraphicsAPI(ScanResult r, string yaml)
        {
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            bool onlyGLES3 = apis != null && apis.Length == 1
                             && apis[0] == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3;

            string apiNames = apis != null
                ? string.Join(", ", apis.Select(a => a.ToString()))
                : "Could not read";

            AddReleaseField(r, TAB_PRERELEASE, "Player Settings", "Graphics API = OpenGLES3 only",
                detail:   $"APIs: {apiNames}",
                howToFix: "Project Settings → Player → Android → Graphics APIs → keep only OpenGLES3",
                pass: onlyGLES3, warn: false, fail: !onlyGLES3);

            bool es31unchecked = !Regex.IsMatch(yaml, @"AndroidMinimumOpenGLESVersion:\s*[^0\s]");
            AddReleaseField(r, TAB_PRERELEASE, "Player Settings", "Require ES3.1 — unchecked",
                detail:   es31unchecked ? "ES3.1 not required ✓" : "ES3.1 is required — uncheck it",
                howToFix: "Project Settings → Player → Android → Graphics APIs → uncheck Require ES3.1",
                pass: es31unchecked, warn: false, fail: !es31unchecked);
        }

        static void CheckUnityServices(ScanResult r, string yaml)
        {
            var match     = Regex.Match(yaml, @"cloudProjectId:\s*(\S+)");
            bool connected = match.Success
                && !string.IsNullOrEmpty(match.Groups[1].Value)
                && match.Groups[1].Value != "\"\"";

            AddReleaseField(r, TAB_PRERELEASE, "Player Settings", "Unity Services connected",
                detail:   connected ? $"Project ID: {match.Groups[1].Value}" : "No cloudProjectId in ProjectSettings",
                howToFix: "Edit → Project Settings → Services → connect your project",
                pass: connected, warn: !connected);
        }

        static void CheckFirebaseFiles(ScanResult r, string dataPath)
        {
            string gs = FindFile(dataPath, "google-services.json");
            AddReleaseField(r, TAB_PRERELEASE, "Firebase", "google-services.json present",
                detail:   gs != null ? gs.Replace(dataPath, "Assets") : "File not found in project",
                howToFix: "Firebase Console → Project Settings → Android → download google-services.json",
                pass: gs != null, warn: false, fail: gs == null);

            string gp = FindFile(dataPath, "GoogleService-Info.plist");
            AddReleaseField(r, TAB_PRERELEASE, "Firebase", "GoogleService-Info.plist present",
                detail:   gp != null ? gp.Replace(dataPath, "Assets") : "File not found in project",
                howToFix: "Firebase Console → Project Settings → iOS → download GoogleService-Info.plist",
                pass: gp != null, warn: gp == null);
        }

        static void CheckAdjustEnvironment(ScanResult r, string dataPath)
        {
            string asset = FindAssetByName(dataPath, "AdjustToken")
                        ?? FindAssetContaining(dataPath, "environment:");

            if (asset == null)
            {
                AddReleaseField(r, TAB_PRERELEASE, "Adjust", "Adjust environment = Production",
                    detail: "AdjustToken.asset not found — SDK may not be integrated",
                    howToFix: "Ensure Adjust SDK is integrated and AdjustToken.asset exists",
                    pass: false, warn: true);
                return;
            }

            string content = File.ReadAllText(asset);
            var m = Regex.Match(content, @"environment:\s*(\d+)");
            bool isProd = m.Success && m.Groups[1].Value == "0";

            AddReleaseField(r, TAB_PRERELEASE, "Adjust", "Adjust environment = Production",
                detail:   m.Success ? (isProd ? "Production ✓" : "⚠ Still set to Sandbox!") : "Could not read environment field",
                howToFix: "Open AdjustToken.asset → set Environment to Production",
                pass: isProd, warn: false, fail: !isProd);
        }

        static void CheckAppLovinMaxTerms(ScanResult r, string dataPath)
        {
            string asset = FindAssetByName(dataPath, "AppLovinSettings");
            if (asset == null) return;

            string content = File.ReadAllText(asset);
            var m = Regex.Match(content, @"ConsentFlowEnabled:\s*(\d)");
            bool off = !m.Success || m.Groups[1].Value == "0";

            AddReleaseField(r, TAB_PRERELEASE, "AppLovin", "Max Terms & Privacy Flow — unchecked",
                detail:   off ? "Consent flow disabled ✓" : "⚠ Consent flow is enabled",
                howToFix: "AppLovin Integration Manager → uncheck Max Terms and Privacy Policy Flow",
                pass: off, warn: false, fail: !off);
        }

        static void CheckAppLovinAdReview(ScanResult r, string dataPath)
        {
            string asset = FindAssetByName(dataPath, "AppLovinSettings");
            if (asset == null) return;

            string content = File.ReadAllText(asset);
            var m = Regex.Match(content, @"AdReviewEnabled:\s*(\d)");
            bool off = !m.Success || m.Groups[1].Value == "0";

            AddReleaseField(r, TAB_PRERELEASE, "AppLovin", "Max Ad Review — unchecked",
                detail:   off ? "Ad Review disabled ✓" : "⚠ Ad Review is enabled",
                howToFix: "AppLovin Integration Manager → uncheck Max Ad Review",
                pass: off, warn: false, fail: !off);
        }

        static void CheckAppMetricaSettings(ScanResult r, string dataPath)
        {
            string asset = FindAssetByName(dataPath, "AppMetricaSettings")
                        ?? FindAssetByName(dataPath, "YandexMetricaSettings");

            if (asset == null)
            {
                AddReleaseField(r, TAB_PRERELEASE, "AppMetrica", "Auto-collection options unchecked",
                    detail: "AppMetricaSettings.asset not found",
                    howToFix: "Assets → AppMetrica → Settings → uncheck all options → Apply",
                    pass: false, warn: true);
                return;
            }

            string content = File.ReadAllText(asset);
            var toCheck = new Dictionary<string, string>
            {
                { "EnableAppHudIntegration",        "Enable AppHud Integration" },
                { "EnableAutoDetectionOfFeatures",  "Enable Auto Detection Of Features" },
            };

            var issues = new List<string>();
            foreach (var kv in toCheck)
            {
                var m = Regex.Match(content, $@"{kv.Key}:\s*(\d)");
                if (m.Success && m.Groups[1].Value != "0")
                    issues.Add(kv.Value + " is ON");
            }

            bool allOff = issues.Count == 0;
            AddReleaseField(r, TAB_PRERELEASE, "AppMetrica", "Auto-collection options unchecked",
                detail:   allOff ? "All auto-collection options disabled ✓" : string.Join(", ", issues),
                howToFix: "Assets → AppMetrica → Settings → uncheck all options → Apply",
                pass: allOff, warn: false, fail: !allOff);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  BUILD SETTINGS
        // ════════════════════════════════════════════════════════════════════════

        static void CheckSymbolsPublic(ScanResult r, string yaml)
        {
            var m = Regex.Match(yaml, @"AndroidCreateSymbols:\s*(\d)");
            bool isPublic = m.Success && m.Groups[1].Value == "1";

            AddReleaseField(r, TAB_BUILD, "Build Settings", "Create symbols.zip = Public",
                detail:   m.Success ? (isPublic ? "Symbols = Public ✓" : $"Symbols value = {m.Groups[1].Value} (need 1 = Public)") : "Could not read AndroidCreateSymbols",
                howToFix: "Build Settings → Android → Create symbols.zip → Public",
                pass: isPublic, warn: false, fail: !isPublic);
        }

        static void CheckCompressionLZ4HC(ScanResult r, string yaml)
        {
            bool found = yaml.Contains("LZ4HC") ||
                         Regex.IsMatch(yaml, @"compressionType:\s*3");

            AddReleaseField(r, TAB_BUILD, "Build Settings", "Compression Method = LZ4HC",
                detail:   found ? "LZ4HC found in build config ✓" : "LZ4HC not confirmed — check manually",
                howToFix: "Build Settings → Compression Method → LZ4HC",
                pass: found, warn: !found);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS — produce FieldResult rows compatible with ChecklistWindow
        // ════════════════════════════════════════════════════════════════════════

        static void AddReleaseField(ScanResult r, int tab, string section, string name,
                                    string detail, string howToFix,
                                    bool pass, bool warn = false, bool fail = false)
        {
            var status = fail  ? FieldStatus.Mismatch   // red
                       : warn  ? FieldStatus.Missing    // orange
                       : pass  ? FieldStatus.Match      // green
                       :         FieldStatus.NotConfigured;

            r.AllFields.Add(new FieldResult
            {
                Tab          = tab,
                Section      = section,
                FieldName    = name,
                Platform     = "",
                ProjectValue = detail,
                ExpectedValue= howToFix,   // reused as "how to fix" in the UI
                Status       = status,
                AssetPath    = "",
                YamlKey      = "",
            });
        }

        static void Manual(ScanResult r, string section, string name, string howTo)
        {
            r.AllFields.Add(new FieldResult
            {
                Tab          = TAB_MANUAL,
                Section      = section,
                FieldName    = name,
                Platform     = "",
                ProjectValue = "",
                ExpectedValue= howTo,
                Status       = FieldStatus.NotConfigured,
                AssetPath    = "",
                YamlKey      = "manual",   // sentinel — window uses this to detect manual items
            });
        }

        // ── File helpers ──────────────────────────────────────────────────────

        static string FindFile(string root, string name)
        {
            try { return Directory.GetFiles(root, name, SearchOption.AllDirectories)
                    .Select(f => f.Replace('\\', '/')).FirstOrDefault(); }
            catch { return null; }
        }

        static string FindAssetByName(string root, string name)
        {
            try { return Directory.GetFiles(root, $"{name}.asset", SearchOption.AllDirectories)
                    .Select(f => f.Replace('\\', '/')).FirstOrDefault(); }
            catch { return null; }
        }

        static string FindAssetContaining(string root, string key)
        {
            try { return Directory.GetFiles(root, "*.asset", SearchOption.AllDirectories)
                    .Select(f => f.Replace('\\', '/'))
                    .FirstOrDefault(f => { try { return File.ReadAllText(f).Contains(key); } catch { return false; } }); }
            catch { return null; }
        }
    }
}
