// AssetScanner.cs
// Finds all relevant .asset files in the project and extracts field values.
// Compares against expected values from package SOs or pasted JSON.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GDChecklist
{
    public enum FieldStatus { Match, Mismatch, Missing, NotConfigured, Ignored, Empty }

    public class FieldResult
    {
        public int         Tab           { get; set; }
        public string      Section       { get; set; }
        public string      FieldName     { get; set; }
        public string      Platform      { get; set; }
        public string      ExpectedValue { get; set; }
        public string      ProjectValue  { get; set; }
        public FieldStatus Status        { get; set; }
        public string      AssetPath     { get; set; }
        public string      YamlKey       { get; set; }
        public bool        IsExternalDev { get; set; } // show paste field when empty
    }

    public class ScanResult
    {
        public List<FieldResult> AllFields    { get; set; } = new();
        public string            ScannedRoot  { get; set; }
        public System.DateTime   ScannedAt    { get; set; }
    }

    // Which SDKs to scan — passed from the setup screen selection
    public class SDKScanConfig
    {
        public bool AppLovin   { get; set; }
        public bool Metica     { get; set; }
        public bool Adjust     { get; set; }
        public bool AppMetrica { get; set; }
        public bool Firebase   { get; set; }
        public bool AdUnits    { get; set; }
        public bool IsGDSDK    { get; set; } // true = GD SDK detected, paths are known
    }

    public static class AssetScanner
    {
        // Tab indices — match ChecklistWindow.Tabs order
        private const int TAB_APPLOVIN    = 0;
        private const int TAB_METICA      = 1;
        private const int TAB_ADJUST      = 2;
        private const int TAB_APPMETRICA  = 3;
        private const int TAB_FIREBASE    = 4;
        private const int TAB_ADUNITS     = 5;

        public static ScanResult Scan(string dataPath, string overrideJson = null,
                                       SDKScanConfig config = null)
        {
            // Default: scan everything if no config provided
            config = config ?? new SDKScanConfig
            {
                AppLovin = true, Metica = true, Adjust = true,
                AppMetrica = true, Firebase = true, AdUnits = true
            };

            var result = new ScanResult
            {
                ScannedRoot = dataPath,
                ScannedAt   = System.DateTime.Now
            };

            var jsonOverrides = string.IsNullOrEmpty(overrideJson)
                ? new Dictionary<string, string>()
                : ParseJson(overrideJson);

            var allAssets = Directory.GetFiles(dataPath, "*.asset", SearchOption.AllDirectories)
                .Select(f => f.Replace('\\', '/'))
                .ToList();

            if (config.AppLovin)   ScanAppLovin   (allAssets, result, jsonOverrides, config.IsGDSDK);
            if (config.Metica)     ScanMetica     (allAssets, result, jsonOverrides, !config.IsGDSDK);
            if (config.Adjust)     ScanAdjust     (allAssets, result, jsonOverrides, !config.IsGDSDK);
            if (config.AppMetrica) ScanAppMetrica (allAssets, result, jsonOverrides, !config.IsGDSDK);
            if (config.Firebase)   ScanFirebase   (allAssets, result, jsonOverrides);
            if (config.AdUnits)    ScanAdUnits    (allAssets, result, jsonOverrides);

            return result;
        }

        // ── AppLovin / AdMob ──────────────────────────────────────────────────────

        private static void ScanAppLovin(List<string> assets, ScanResult result,
                                          Dictionary<string, string> overrides,
                                          bool isGDSDK = false)
        {
            string file;
            if (isGDSDK)
            {
                // GD SDK projects: AppLovin is always at Assets/MaxSdk/Resources/
                string path = Path.Combine(Application.dataPath, "MaxSdk", "Resources", "AppLovinSettings.asset");
                file = File.Exists(path) ? path.Replace('\\', '/') : null;
            }
            else
            {
                // External devs: search anywhere in project
                file = FindAsset(assets, "AppLovinSettings");
            }

            // Also check AdUnitsSettings for cross-validation
            var adUnitsFile = FindAsset(assets, "AdUnitsSettings");

            AddField(result, TAB_APPLOVIN, "AppLovin", "SDK Key", "Android",
                "sdkKey", file,
                GetYaml(file, "sdkKey"),
                overrides.GetValueOrDefault("applovin_sdk_key"));

            // AdMob App IDs from AppLovinSettings
            AddField(result, TAB_APPLOVIN, "AdMob (via AppLovin)", "App ID", "Android",
                "adMobAndroidAppId", file,
                GetYaml(file, "adMobAndroidAppId"),
                overrides.GetValueOrDefault("admob_android_app_id"));

            AddField(result, TAB_APPLOVIN, "AdMob (via AppLovin)", "App ID", "iOS",
                "adMobIosAppId", file,
                GetYaml(file, "adMobIosAppId"),
                overrides.GetValueOrDefault("admob_ios_app_id"));

            // Cross-check: AdUnitsSettings.Applovin.AndroidAppKey should match AppLovinSettings.sdkKey
            if (adUnitsFile != null && file != null)
            {
                string adUnitsKey = GetYaml(adUnitsFile, "AndroidAppKey");
                string appLovinKey = GetYaml(file, "sdkKey");

                var crossField = new FieldResult
                {
                    Tab           = TAB_APPLOVIN,
                    Section       = "Cross-Check",
                    FieldName     = "SDK Key matches AdUnitsSettings",
                    Platform      = "Android",
                    ExpectedValue = appLovinKey,
                    ProjectValue  = adUnitsKey,
                    AssetPath     = adUnitsFile,
                    YamlKey       = "AndroidAppKey"
                };
                crossField.Status = Compare(appLovinKey, adUnitsKey);
                result.AllFields.Add(crossField);
            }
        }

        // ── Metica ────────────────────────────────────────────────────────────────

        private static void ScanMetica(List<string> assets, ScanResult result,
                                        Dictionary<string, string> overrides, bool isExternalDev = false)
        {
            var file = FindAsset(assets, "MeticaSettings");

            AddField(result, TAB_METICA, "API Keys", "Android API Key", "Android",
                "AndroidApiKey", file, GetYaml(file, "AndroidApiKey"),
                overrides.GetValueOrDefault("metica_android_api_key"), isExternalDev);

            AddField(result, TAB_METICA, "API Keys", "iOS API Key", "iOS",
                "iOSApiKey", file, GetYaml(file, "iOSApiKey"),
                overrides.GetValueOrDefault("metica_ios_api_key"), isExternalDev);

            AddField(result, TAB_METICA, "App IDs", "Android App ID", "Android",
                "AndroidAppID", file, GetYaml(file, "AndroidAppID"),
                overrides.GetValueOrDefault("metica_android_app_id"), isExternalDev);

            AddField(result, TAB_METICA, "App IDs", "iOS App ID", "iOS",
                "iOSAppID", file, GetYaml(file, "iOSAppID"),
                overrides.GetValueOrDefault("metica_ios_app_id"), isExternalDev);
        }

        // ── Adjust ────────────────────────────────────────────────────────────────

        private static void ScanAdjust(List<string> assets, ScanResult result,
                                        Dictionary<string, string> overrides, bool isExternalDev = false)
        {
            var file = FindAsset(assets, "AdjustToken");

            AddField(result, TAB_ADJUST, "Tokens", "Android Token", "Android",
                "Android", file, GetYaml(file, "Android"),
                overrides.GetValueOrDefault("adjust_android_token"), isExternalDev);

            AddField(result, TAB_ADJUST, "Tokens", "iOS Token", "iOS",
                "iOS", file, GetYaml(file, "iOS"),
                overrides.GetValueOrDefault("adjust_ios_token"), isExternalDev);

            // Environment field — read raw value
            // Adjust SDK: AdjustEnvironment.Production = 0, AdjustEnvironment.Sandbox = 1
            string env = GetYaml(file, "Environment");

            string envDisplay = env switch
            {
                "0" => "Sandbox",
                "1" => "Production",
                null => "(not found)",
                _ => $"Unknown ({env})"
            };

            var envField = new FieldResult
            {
                Tab           = TAB_ADJUST,
                Section       = "Settings",
                FieldName     = "Environment",
                Platform      = "",
                ProjectValue  = $"{envDisplay}  (raw value: {env ?? "?"})",
                ExpectedValue = "Production  (raw value: 1)",
                AssetPath     = file,
                YamlKey       = "Environment"
            };

            if (file == null)
                envField.Status = FieldStatus.Missing;
            else if (env == "1")
                envField.Status = FieldStatus.Match;      // Production — correct for release
            else if (env == "0")
                envField.Status = FieldStatus.Mismatch;   // Sandbox — warn developer
            else
                envField.Status = FieldStatus.Missing;

            result.AllFields.Add(envField);

            // LogLevel — 3 = Verbose (too noisy for prod), 5 = Error (ideal for prod)
            string logLevel = GetYaml(file, "LogLevel");
            // Adjust LogLevel: Verbose=0, Debug=1, Info=2, Warn=3, Error=4, Assert=5, Suppress=6
            // Note: Inspector showed "Info" for value 3 in some SDK versions — keeping both mappings
            string logDisplay = logLevel switch
            {
                "0" => "Verbose",
                "1" => "Debug",
                "2" => "Info",
                "3" => "Warn",
                "4" => "Error",
                "5" => "Assert",
                "6" => "Suppress",
                null => "(not found)",
                _ => $"Level {logLevel}"
            };

            // Warn if verbose/debug logging is on — not ideal for production
            bool logOkForProd = logLevel == "3" || logLevel == "4" || logLevel == "5" || logLevel == "6";

            result.AllFields.Add(new FieldResult
            {
                Tab           = TAB_ADJUST,
                Section       = "Settings",
                FieldName     = "Log Level",
                Platform      = "",
                ProjectValue  = logDisplay + $"  (raw: {logLevel ?? "?"})",
                ExpectedValue = "",
                AssetPath     = file,
                YamlKey       = "LogLevel",
                Status        = string.IsNullOrEmpty(logLevel) ? FieldStatus.Missing
                              : logOkForProd ? FieldStatus.Match
                              : FieldStatus.Mismatch
            });
        }

        // ── AppMetrica ────────────────────────────────────────────────────────────

        private static void ScanAppMetrica(List<string> assets, ScanResult result,
                                            Dictionary<string, string> overrides, bool isExternalDev = false)
        {
            var file = FindAsset(assets, "AppMetricaSettings");

            AddField(result, TAB_APPMETRICA, "API Keys", "Android API Key", "Android",
                "Android", file, GetYaml(file, "Android"),
                overrides.GetValueOrDefault("appmetrica_android_key"), isExternalDev);

            AddField(result, TAB_APPMETRICA, "API Keys", "iOS API Key", "iOS",
                "iOS", file, GetYaml(file, "iOS"),
                overrides.GetValueOrDefault("appmetrica_ios_key"), isExternalDev);

            // Booleans
            AddBoolField(result, TAB_APPMETRICA, "Settings", "Crash Reporting", "CrashReporting", file, true);
            AddBoolField(result, TAB_APPMETRICA, "Settings", "Native Crash Reporting", "NativeCrashReporting", file, true);
            AddBoolField(result, TAB_APPMETRICA, "Settings", "Sessions Auto Tracking", "SessionsAutoTracking", file, true);
            AddBoolField(result, TAB_APPMETRICA, "Settings", "Logs (should be OFF in production)", "Logs", file, false);
        }

        // ── Firebase ──────────────────────────────────────────────────────────────

        private static void ScanFirebase(List<string> assets, ScanResult result,
                                          Dictionary<string, string> overrides)
        {
            // Firebase uses google-services.json and GoogleService-Info.plist
            string dataPath = Application.dataPath;

            string androidJson = FindFile(dataPath, "google-services.json");
            string iosPlist    = FindFile(dataPath, "GoogleService-Info.plist");

            var androidField = new FieldResult
            {
                Tab       = TAB_FIREBASE,
                Section   = "Config Files",
                FieldName = "google-services.json",
                Platform  = "Android",
                AssetPath = androidJson,
                YamlKey   = ""
            };
            if (androidJson != null)
            {
                androidField.ProjectValue  = "Found at: " + androidJson.Replace(dataPath, "Assets");
                androidField.ExpectedValue = "Present";
                androidField.Status        = FieldStatus.Match;
            }
            else
            {
                androidField.ProjectValue  = "(not found)";
                androidField.ExpectedValue = "Present";
                androidField.Status        = FieldStatus.Missing;
            }
            result.AllFields.Add(androidField);

            var iosField = new FieldResult
            {
                Tab       = TAB_FIREBASE,
                Section   = "Config Files",
                FieldName = "GoogleService-Info.plist",
                Platform  = "iOS",
                AssetPath = iosPlist,
                YamlKey   = ""
            };
            if (iosPlist != null)
            {
                iosField.ProjectValue  = "Found at: " + iosPlist.Replace(dataPath, "Assets");
                iosField.ExpectedValue = "Present";
                iosField.Status        = FieldStatus.Match;
            }
            else
            {
                iosField.ProjectValue  = "(not found)";
                iosField.ExpectedValue = "Present";
                iosField.Status        = FieldStatus.Missing;
            }
            result.AllFields.Add(iosField);

            // FirebaseNetworkSO
            var networkFile = FindAsset(assets, "FirebaseNetworkSO");
            AddBoolField(result, TAB_FIREBASE, "Network Settings", "Enable Initialization", "EnableInitialization", networkFile, true);
            AddBoolField(result, TAB_FIREBASE, "Network Settings", "Enable Events",         "EnableEvents",         networkFile, true);
            AddBoolField(result, TAB_FIREBASE, "Network Settings", "Enable Revenue",        "EnableRevenue",        networkFile, true);
        }

        // ── Ad Units ──────────────────────────────────────────────────────────────

        private static void ScanAdUnits(List<string> assets, ScanResult result,
                                         Dictionary<string, string> overrides)
        {
            var file = FindAsset(assets, "AdUnitsSettings");

            if (file == null)
            {
                result.AllFields.Add(new FieldResult
                {
                    Tab = TAB_ADUNITS, Section = "Error", FieldName = "AdUnitsSettings.asset",
                    Status = FieldStatus.Missing,
                    ProjectValue = "AdUnitsSettings.asset not found in project"
                });
                return;
            }

            string content = File.ReadAllText(file);

            // General settings
            string testAds = GetYaml(file, "TestAds");
            result.AllFields.Add(new FieldResult
            {
                Tab           = TAB_ADUNITS,
                Section       = "General",
                FieldName     = "Test Ads",
                Platform      = "",
                AssetPath     = file,
                YamlKey       = "TestAds",
                ProjectValue  = testAds == "1" ? "ENABLED ⚠" : testAds == "0" ? "Disabled" : testAds,
                ExpectedValue = "Disabled",
                Status        = testAds == "0" ? FieldStatus.Match : FieldStatus.Mismatch
            });

            string showAppopen = GetYaml(file, "ShowAppopenOnLoad");
            result.AllFields.Add(new FieldResult
            {
                Tab           = TAB_ADUNITS,
                Section       = "General",
                FieldName     = "Show AppOpen On Load",
                Platform      = "",
                AssetPath     = file,
                YamlKey       = "ShowAppopenOnLoad",
                ProjectValue  = showAppopen == "1" ? "Enabled" : "Disabled",
                ExpectedValue = "",
                Status        = string.IsNullOrEmpty(showAppopen) ? FieldStatus.Missing : FieldStatus.Match
            });

            // Parse each network block separately
            ScanNetworkBlock(result, file, content, "Applovin",  TAB_ADUNITS, overrides);
            ScanNetworkBlock(result, file, content, "Admob",     TAB_ADUNITS, overrides);
            ScanNetworkBlock(result, file, content, "Metica",    TAB_ADUNITS, overrides);
        }

        private static void ScanNetworkBlock(ScanResult result, string file, string content,
                                              string network, int tab,
                                              Dictionary<string, string> overrides)
        {
            // AdType enum from the SO:
            // 0 = Interstitial, 1 = Rewarded, 2 = AppOpen, 3 = Banner, 4 = MRec
            var adTypeNames = new Dictionary<int, string>
            {
                { 0, "Interstitial" },
                { 1, "Rewarded" },
                { 2, "AppOpen" },
                { 3, "Banner" },
                { 4, "MRec" }
            };

            // Find the start of this network block
            int blockStart = content.IndexOf($"  {network}:\n");
            if (blockStart < 0) blockStart = content.IndexOf($"  {network}:\r\n");

            if (blockStart < 0)
            {
                result.AllFields.Add(new FieldResult
                {
                    Tab = tab, Section = $"{network} — Ad Units",
                    FieldName = $"{network} section not found", Platform = "",
                    Status = FieldStatus.Missing,
                    ProjectValue = $"No {network} block in AdUnitsSettings.asset"
                });
                return;
            }

            // Find the end — next top-level key (2-space indent key that is NOT part of this block)
            int blockEnd = content.Length;
            var nextBlockMatch = Regex.Match(content.Substring(blockStart + network.Length + 4),
                @"^\s{2}\w", RegexOptions.Multiline);
            if (nextBlockMatch.Success)
                blockEnd = blockStart + network.Length + 4 + nextBlockMatch.Index;

            string block = content.Substring(blockStart, blockEnd - blockStart);

            // App keys
            var androidKeyMatch = Regex.Match(block, @"AndroidAppKey:\s*(.+)");
            var iosKeyMatch      = Regex.Match(block, @"iOSAppKey:\s*(.+)");
            string androidKey    = androidKeyMatch.Success ? androidKeyMatch.Groups[1].Value.Trim() : null;
            string iosKey        = iosKeyMatch.Success     ? iosKeyMatch.Groups[1].Value.Trim()     : null;

            AddField(result, tab, $"{network} — App Keys", "Android App Key", "Android",
                $"{network}.AndroidAppKey", file, androidKey,
                overrides.GetValueOrDefault($"{network.ToLower()}_android_app_key"));

            AddField(result, tab, $"{network} — App Keys", "iOS App Key", "iOS",
                $"{network}.iOSAppKey", file, iosKey,
                overrides.GetValueOrDefault($"{network.ToLower()}_ios_app_key"));

            // Ad unit entries — strictly within this block only
            var adUnitMatches = Regex.Matches(block,
                @"-\s+AdType:\s*(\d+)\s*\r?\n\s+Android:\s*(\S+)\s*\r?\n\s+iOS:\s*(\S+)");

            if (adUnitMatches.Count == 0)
            {
                result.AllFields.Add(new FieldResult
                {
                    Tab = tab, Section = $"{network} — Ad Units",
                    FieldName = "No ad units configured", Platform = "",
                    Status = FieldStatus.Missing, AssetPath = file
                });
                return;
            }

            foreach (Match m in adUnitMatches)
            {
                int    adType   = int.Parse(m.Groups[1].Value);
                string android  = m.Groups[2].Value.Trim();
                string ios      = m.Groups[3].Value.Trim();
                string typeName = adTypeNames.ContainsKey(adType) ? adTypeNames[adType] : $"Type {adType}";

                AddField(result, tab, $"{network} — Ad Units", $"{typeName} ID", "Android",
                    $"{network}.AdUnit.{adType}.Android", file, android,
                    overrides.GetValueOrDefault($"{network.ToLower()}_adunit_{adType}_android"));

                AddField(result, tab, $"{network} — Ad Units", $"{typeName} ID", "iOS",
                    $"{network}.AdUnit.{adType}.iOS", file, ios,
                    overrides.GetValueOrDefault($"{network.ToLower()}_adunit_{adType}_ios"));
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void AddField(ScanResult result, int tab, string section,
                                      string fieldName, string platform, string yamlKey,
                                      string assetPath, string projectValue, string expectedValue,
                                      bool isExternalDev = false)
        {
            var f = new FieldResult
            {
                Tab           = tab,
                Section       = section,
                FieldName     = fieldName,
                Platform      = platform,
                YamlKey       = yamlKey,
                AssetPath     = assetPath,
                ProjectValue  = projectValue,
                ExpectedValue = expectedValue,
                IsExternalDev = isExternalDev
            };

            if (assetPath == null)
            {
                // File not found — for external devs show paste field, for GD devs show missing
                f.Status = isExternalDev ? FieldStatus.Empty : FieldStatus.Missing;
            }
            else if (string.IsNullOrEmpty(projectValue))
            {
                // File found but value is empty — show paste field for external devs
                f.Status = isExternalDev ? FieldStatus.Empty : FieldStatus.Missing;
            }
            else if (string.IsNullOrEmpty(expectedValue))
            {
                // No expected value — just show what's in the project
                f.Status = FieldStatus.Match;
            }
            else
            {
                // Expected value provided — compare
                f.Status = Compare(expectedValue, projectValue);
            }

            result.AllFields.Add(f);
        }

        private static void AddBoolField(ScanResult result, int tab, string section,
                                          string fieldName, string yamlKey, string assetPath,
                                          bool expectedTrue)
        {
            string raw = GetYaml(assetPath, yamlKey);
            bool val = raw == "1";
            string expected = expectedTrue ? "1 (enabled)" : "0 (disabled)";
            string found    = raw == "1" ? "1 (enabled)" : raw == "0" ? "0 (disabled)" : raw ?? "(empty)";

            var f = new FieldResult
            {
                Tab           = tab,
                Section       = section,
                FieldName     = fieldName,
                Platform      = "",
                YamlKey       = yamlKey,
                AssetPath     = assetPath,
                ProjectValue  = found,
                ExpectedValue = expected
            };

            if (assetPath == null) f.Status = FieldStatus.Missing;
            else f.Status = (val == expectedTrue) ? FieldStatus.Match : FieldStatus.Mismatch;

            result.AllFields.Add(f);
        }

        private static FieldStatus Compare(string expected, string actual)
        {
            if (string.IsNullOrEmpty(actual))   return FieldStatus.Missing;
            if (string.IsNullOrEmpty(expected)) return FieldStatus.Match; // no expectation = just show it
            return expected.Trim() == actual.Trim() ? FieldStatus.Match : FieldStatus.Mismatch;
        }

        // Find asset file by name (case-insensitive, anywhere in project)
        private static string FindAsset(List<string> assets, string name)
            => assets.FirstOrDefault(a =>
                System.IO.Path.GetFileNameWithoutExtension(a)
                    .Equals(name, System.StringComparison.OrdinalIgnoreCase));

        // Find any file by name recursively
        private static string FindFile(string root, string name)
        {
            try {
                return Directory.GetFiles(root, name, SearchOption.AllDirectories)
                    .FirstOrDefault()?.Replace('\\', '/');
            } catch { return null; }
        }

        // Read a YAML field value — public so ChecklistWindow can call after file locate
        public static string ReadYamlKey(string filePath, string key)
            => GetYaml(filePath, key);

        // Read a YAML field value (internal)
        private static string GetYaml(string filePath, string key)
        {
            if (filePath == null || !File.Exists(filePath)) return null;
            try
            {
                string content = File.ReadAllText(filePath);
                var match = Regex.Match(content, $@"^\s*{Regex.Escape(key)}:\s*(.+)$", RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch { return null; }
        }

        private static string GetRegex(string content, string pattern)
        {
            try {
                var m = Regex.Match(content, pattern, RegexOptions.Multiline);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            } catch { return null; }
        }

        // Very simple JSON key-value extractor
        private static Dictionary<string, string> ParseJson(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;
            try
            {
                var matches = Regex.Matches(json, @"""(\w+)""\s*:\s*""([^""]*)""");
                foreach (Match m in matches)
                    result[m.Groups[1].Value.ToLower()] = m.Groups[2].Value;
            }
            catch { }
            return result;
        }
    }
}
