// AssetScanner.cs
// Finds all relevant .asset files in the project and extracts field values.
// Compares against expected values from package SOs or pasted JSON.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
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
        public List<string>      AllAssets    { get; set; } = new(); // cached asset list for ReleaseScanner
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

        // ── File cache — populated once per scan, shared across all scanners ────────
        // Avoids re-reading the filesystem on every GetAllCsFiles() call.
        private static string[]                    _cachedCsFiles  = null;
        private static Dictionary<string, string>  _cachedContents = null;

        private static void InitScanCache(string dataPath)
        {
            ScanProgress.Report("Indexing project files…", 0.1f);
            _cachedCsFiles  = Directory.GetFiles(dataPath, "*.cs", SearchOption.AllDirectories);
            _cachedContents = new Dictionary<string, string>(_cachedCsFiles.Length);
            int total = _cachedCsFiles.Length;
            for (int i = 0; i < total; i++)
            {
                if (i % 50 == 0)
                    ScanProgress.Report($"Reading files… ({i}/{total})", 0.1f + 0.5f * i / total);
                try { _cachedContents[_cachedCsFiles[i]] = File.ReadAllText(_cachedCsFiles[i]); }
                catch { _cachedContents[_cachedCsFiles[i]] = null; }
            }
            ScanProgress.Report("Scanning SDK configurations…", 0.6f);
        }

        private static void ClearScanCache()
        {
            _cachedCsFiles  = null;
            _cachedContents = null;
        }

        public static ScanResult Scan(string dataPath, string overrideJson = null,
                                       SDKScanConfig config = null)
        {
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

            List<string> allAssets;

            if (config.IsGDSDK)
            {
                // GD SDK: all asset paths are known exactly — no filesystem scan needed.
                // Collect only the specific files we actually read, instantly.
                var gdConfigs = Path.Combine(dataPath, "GDMonetization", "Runtime", "Resources", "Configurations");
                var knownPaths = new[]
                {
                    Path.Combine(dataPath,   "MaxSdk", "Resources", "AppLovinSettings.asset"),
                    Path.Combine(gdConfigs,  "MeticaSettings.asset"),
                    Path.Combine(gdConfigs,  "AdjustToken.asset"),
                    Path.Combine(gdConfigs,  "AppMetricaSettings.asset"),
                    Path.Combine(dataPath,   "google-services.json"),
                    Path.Combine(dataPath,   "GoogleService-Info.plist"),
                };
                allAssets = knownPaths
                    .Where(p => File.Exists(p))
                    .Select(p => p.Replace('\\', '/'))
                    .ToList();

                // Also add AdUnitsSettings from a targeted search (only within Configurations folder)
                var adUnitsPath = Path.Combine(gdConfigs, "AdUnitsSettings.asset");
                if (File.Exists(adUnitsPath))
                    allAssets.Add(adUnitsPath.Replace('\\', '/'));
                else
                {
                    // Fallback: search only within known GD folder, not whole project
                    var gdFolder = Path.Combine(dataPath, "GDMonetization");
                    if (Directory.Exists(gdFolder))
                    {
                        var found = Directory.GetFiles(gdFolder, "AdUnitsSettings.asset",
                                        SearchOption.AllDirectories).FirstOrDefault();
                        if (found != null) allAssets.Add(found.Replace('\\', '/'));
                    }
                }

                // For ReleaseScanner: it only needs a few specific assets,
                // do a targeted search in known locations rather than full project scan
                var releaseAssets = new List<string>();
                // AppLovinSettings already in allAssets
                releaseAssets.AddRange(allAssets);
                // AdjustToken may also be at legacy path
                var legacyAdjust = Path.Combine(dataPath, "Configurations", "AdjustToken.asset");
                if (File.Exists(legacyAdjust)) releaseAssets.Add(legacyAdjust.Replace('\\', '/'));
                allAssets = releaseAssets;
            }
            else
            {
                // Non-GD: need to search whole project since paths are unknown
                ScanProgress.Report("Collecting project assets…", 0.05f);
                allAssets = Directory.GetFiles(dataPath, "*.asset", SearchOption.AllDirectories)
                    .Select(f => f.Replace('\\', '/'))
                    .ToList();

                // Build .cs file cache for code scanning
                InitScanCache(dataPath);
            }

            try
            {
                if (config.AppLovin)   ScanAppLovin   (allAssets, result, jsonOverrides, config.IsGDSDK);
                if (config.Metica)     ScanMetica     (allAssets, result, jsonOverrides, !config.IsGDSDK);
                if (config.Adjust)     ScanAdjust     (allAssets, result, jsonOverrides, !config.IsGDSDK);
                if (config.AppMetrica) ScanAppMetrica (allAssets, result, jsonOverrides, !config.IsGDSDK);
                if (config.Firebase)   ScanFirebase   (allAssets, result, jsonOverrides);
                if (config.AdUnits)    ScanAdUnits    (allAssets, result, jsonOverrides, config);
            }
            finally
            {
                if (!config.IsGDSDK) ClearScanCache();
                // ScanProgress.End() called by RunScan finally block
            }

            result.AllAssets = allAssets;
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
                // GD SDK — asset always at known path, no code scan needed
                string path = Path.Combine(Application.dataPath, "MaxSdk", "Resources", "AppLovinSettings.asset");
                file = File.Exists(path) ? path.Replace('\\', '/') : null;
            }
            else
            {
                // Non-GD — code scan first
                file = FindAsset(assets, "AppLovinSettings");
            }

            // Non-GD code scan: find hardcoded values if no asset found
            string codeFile = null, codeSdkKey = null, codeAdmobAndroid = null, codeAdmobIos = null;
            if (!isGDSDK)
            {
                foreach (var cs in GetAllCsFiles())
                {
                    string src = SafeReadFile(cs);
                    if (src == null || (!src.Contains("MaxSdk") && !src.Contains("MobileAds") && !src.Contains("sdkKey"))) continue;
                    var m1 = Regex.Match(src, @"MaxSdk\.SetSdkKey\s*\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (m1.Success && codeSdkKey == null) { codeSdkKey = m1.Groups[1].Value; codeFile = cs; }
                    var m2 = Regex.Match(src, @"\bsdkKey\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (m2.Success && codeSdkKey == null) { codeSdkKey = m2.Groups[1].Value; codeFile = cs; }
                    var admobs = Regex.Matches(src, @"MobileAds\.Initialize\s*\(\s*""(ca-app-pub-[^""]+)""", RegexOptions.IgnoreCase);
                    foreach (Match m in admobs)
                    {
                        bool isAndroid = cs.ToLower().Contains("android") || src.Contains("UNITY_ANDROID");
                        if (isAndroid && codeAdmobAndroid == null) codeAdmobAndroid = m.Groups[1].Value;
                        else if (codeAdmobIos == null) codeAdmobIos = m.Groups[1].Value;
                    }
                    var m4a = Regex.Match(src, @"adMobAndroidAppId\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    var m4b = Regex.Match(src, @"adMobIosAppId\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (m4a.Success && codeAdmobAndroid == null) codeAdmobAndroid = m4a.Groups[1].Value;
                    if (m4b.Success && codeAdmobIos == null) codeAdmobIos = m4b.Groups[1].Value;
                }
            }

            string sourceNote = codeFile != null ? " (from code)" : file != null ? " (from asset)" : "";
            string usedFile   = codeFile ?? file;
            string sdkKeyVal  = codeSdkKey ?? GetYaml(file, "sdkKey");
            string admobAnd   = codeAdmobAndroid ?? GetYaml(file, "adMobAndroidAppId");
            string admobIos   = codeAdmobIos ?? GetYaml(file, "adMobIosAppId");

            var adUnitsFile = FindAsset(assets, "AdUnitsSettings");

            AddField(result, TAB_APPLOVIN, "AppLovin", "SDK Key" + sourceNote, "Android",
                "sdkKey", usedFile, sdkKeyVal, overrides.GetValueOrDefault("applovin_sdk_key"));
            AddField(result, TAB_APPLOVIN, "AdMob (via AppLovin)", "App ID" + sourceNote, "Android",
                "adMobAndroidAppId", usedFile, admobAnd, overrides.GetValueOrDefault("admob_android_app_id"));
            AddField(result, TAB_APPLOVIN, "AdMob (via AppLovin)", "App ID" + sourceNote, "iOS",
                "adMobIosAppId", usedFile, admobIos, overrides.GetValueOrDefault("admob_ios_app_id"));

            if (adUnitsFile != null && file != null)
            {
                string adUnitsKey  = GetYaml(adUnitsFile, "AndroidAppKey");
                string appLovinKey = GetYaml(file, "sdkKey");
                var crossField = new FieldResult
                {
                    Tab = TAB_APPLOVIN, Section = "Cross-Check",
                    FieldName = "SDK Key matches AdUnitsSettings", Platform = "Android",
                    ExpectedValue = appLovinKey, ProjectValue = adUnitsKey,
                    AssetPath = adUnitsFile, YamlKey = "AndroidAppKey"
                };
                crossField.Status = Compare(appLovinKey, adUnitsKey);
                result.AllFields.Add(crossField);
            }
        }

        // ── Metica ────────────────────────────────────────────────────────────────

        private static void ScanMetica(List<string> assets, ScanResult result,
                                        Dictionary<string, string> overrides, bool isExternalDev = false)
        {
            // GD SDK — asset at known path, no code scan needed
            string gdPath = Path.Combine(Application.dataPath,
                "GDMonetization", "Runtime", "Resources", "Configurations", "MeticaSettings.asset");
            var file = File.Exists(gdPath) ? gdPath.Replace('\\', '/') : FindAsset(assets, "MeticaSettings");

            if (!isExternalDev)
            {
                // GD SDK path — read directly from asset
                AddField(result, TAB_METICA, "API Keys", "Android API Key", "Android",
                    "AndroidApiKey", file, GetYaml(file, "AndroidApiKey"),
                    overrides.GetValueOrDefault("metica_android_api_key"));
                AddField(result, TAB_METICA, "API Keys", "iOS API Key", "iOS",
                    "iOSApiKey", file, GetYaml(file, "iOSApiKey"),
                    overrides.GetValueOrDefault("metica_ios_api_key"));
                AddField(result, TAB_METICA, "App IDs", "Android App ID", "Android",
                    "AndroidAppID", file, GetYaml(file, "AndroidAppID"),
                    overrides.GetValueOrDefault("metica_android_app_id"));
                AddField(result, TAB_METICA, "App IDs", "iOS App ID", "iOS",
                    "iOSAppID", file, GetYaml(file, "iOSAppID"),
                    overrides.GetValueOrDefault("metica_ios_app_id"));
                return;
            }

            // ── Non-GD: Reverse engineer MeticaSdk.InitializeAsync / Initialize call ──
            //
            // Step 1: find the init call
            //   MeticaSdk.InitializeAsync(config, mediationInfo)
            //   MeticaSdk.Initialize(config, mediationInfo, callback)
            //
            // Step 2: trace 'config' → new MeticaInitConfig(apiKey, appId, userId)
            //   arg[0] = API key
            //   arg[1] = App ID
            //   arg[2] = User ID
            //
            // Step 3: trace 'mediationInfo' → new MeticaMediationInfo(type, maxSdkKey)
            //   arg[0] = mediation type (MeticaMediationInfo.MeticaMediationType.MAX)
            //   arg[1] = MAX SDK key

            string apiKey = null, appId = null, userId = null, codeFile = null;
            string mediationType = null, mediationKey = null;
            string apiKeySource = null, appIdSource = null, userIdSource = null;
            string mediationTypeSource = null, mediationKeySource = null;
            bool   initCallFound = false;
            bool   mediationInfoFound = false;

            foreach (var cs in GetAllCsFiles())
            {
                string src = SafeReadFile(cs);
                if (src == null || !src.Contains("Metica")) continue;

                string fileName = System.IO.Path.GetFileName(cs);
                string[] lines  = src.Split('\n');

                // ── Find MeticaSdk.InitializeAsync / Initialize call ──────────────
                var initMatch = Regex.Match(src,
                    @"MeticaSdk\s*\.\s*(?:InitializeAsync|Initialize)\s*\(",
                    RegexOptions.IgnoreCase);

                if (initMatch.Success && !initCallFound)
                {
                    initCallFound = true;
                    codeFile = cs;
                    int initLine = src.Substring(0, initMatch.Index).Count(c => c == '\n') + 1;

                    // Extract args of the init call — get text from ( to matching )
                    int parenStart = src.IndexOf('(', initMatch.Index + initMatch.Length - 1);
                    string argsText = parenStart >= 0
                        ? ExtractParenContent(src, parenStart)
                        : null;

                    if (argsText != null)
                    {
                        var initArgs = SplitArgs(argsText);
                        string configArg  = initArgs.Count > 0 ? initArgs[0].Trim() : null;
                        string medArg     = initArgs.Count > 1 ? initArgs[1].Trim() : null;

                        // ── Trace config → MeticaInitConfig(apiKey, appId, userId) ──────
                        if (configArg != null)
                        {
                            // Find: new MeticaInitConfig(arg0, arg1, arg2) — directly or via variable
                            string configSrc = src; string configCs = cs;
                            if (!configArg.StartsWith("new"))
                            {
                                // configArg is a variable — find its assignment
                                var configAssign = Regex.Match(src,
                                    $@"\b{Regex.Escape(configArg)}\b\s*=\s*(new\s+MeticaInitConfig[\s\S]*?\))\s*;");
                                if (configAssign.Success)
                                    configSrc = configAssign.Groups[1].Value + ";";
                                else
                                {
                                    // Search other files
                                    foreach (var otherCs in GetAllCsFiles())
                                    {
                                        string os = SafeReadFile(otherCs);
                                        if (os == null || !os.Contains("MeticaInitConfig")) continue;
                                        var oa = Regex.Match(os,
                                            $@"\b{Regex.Escape(configArg)}\b\s*=\s*(new\s+MeticaInitConfig[\s\S]*?\))\s*;");
                                        if (oa.Success) { configSrc = oa.Groups[1].Value + ";"; configCs = otherCs; break; }
                                    }
                                }
                            }

                            // Now extract MeticaInitConfig(arg0, arg1, arg2)
                            var cfgMatch = Regex.Match(configSrc,
                                @"new\s+MeticaInitConfig\s*\(", RegexOptions.IgnoreCase);
                            if (cfgMatch.Success)
                            {
                                int cfgParen = configSrc.IndexOf('(', cfgMatch.Index + cfgMatch.Length - 1);
                                string cfgArgs = cfgParen >= 0 ? ExtractParenContent(configSrc, cfgParen) : null;
                                if (cfgArgs != null)
                                {
                                    var cfgParts = SplitArgs(cfgArgs);
                                    int cfgLine  = src.Substring(0, src.IndexOf("MeticaInitConfig", System.StringComparison.Ordinal) < 0
                                        ? 0 : src.IndexOf("MeticaInitConfig", System.StringComparison.Ordinal)).Count(c => c == '\n') + 1;

                                    // arg[0] = API key
                                    if (cfgParts.Count > 0)
                                    {
                                        var t = TraceVariable(src, configCs, assets, cfgParts[0].Trim(), cfgLine);
                                        apiKey = t.value; apiKeySource = t.source ?? $"expression '{cfgParts[0].Trim()}'";
                                    }
                                    // arg[1] = App ID
                                    if (cfgParts.Count > 1)
                                    {
                                        var t = TraceVariable(src, configCs, assets, cfgParts[1].Trim(), cfgLine);
                                        appId = t.value; appIdSource = t.source ?? $"expression '{cfgParts[1].Trim()}'";
                                    }
                                    // arg[2] = User ID
                                    if (cfgParts.Count > 2)
                                    {
                                        var t = TraceVariable(src, configCs, assets, cfgParts[2].Trim(), cfgLine);
                                        userId = t.value; userIdSource = t.source ?? $"expression '{cfgParts[2].Trim()}'";
                                    }
                                }
                            }
                        }

                        // ── Trace mediationInfo → MeticaMediationInfo(type, maxKey) ──────
                        if (medArg != null)
                        {
                            string medSrc = src; string medCs = cs;
                            if (!medArg.StartsWith("new"))
                            {
                                var medAssign = Regex.Match(src,
                                    $@"\b{Regex.Escape(medArg)}\b\s*=\s*(new\s+MeticaMediationInfo[\s\S]*?\))\s*;");
                                if (medAssign.Success)
                                    medSrc = medAssign.Groups[1].Value + ";";
                                else
                                {
                                    foreach (var otherCs in GetAllCsFiles())
                                    {
                                        string os = SafeReadFile(otherCs);
                                        if (os == null || !os.Contains("MeticaMediationInfo")) continue;
                                        var oa = Regex.Match(os,
                                            $@"\b{Regex.Escape(medArg)}\b\s*=\s*(new\s+MeticaMediationInfo[\s\S]*?\))\s*;");
                                        if (oa.Success) { medSrc = oa.Groups[1].Value + ";"; medCs = otherCs; break; }
                                    }
                                }
                            }

                            var medMatch = Regex.Match(medSrc,
                                @"new\s+MeticaMediationInfo\s*\(", RegexOptions.IgnoreCase);
                            if (medMatch.Success)
                            {
                                mediationInfoFound = true;
                                int medParen = medSrc.IndexOf('(', medMatch.Index + medMatch.Length - 1);
                                string medArgs = medParen >= 0 ? ExtractParenContent(medSrc, medParen) : null;
                                if (medArgs != null)
                                {
                                    var medParts = SplitArgs(medArgs);
                                    int medLine  = src.Substring(0, src.IndexOf("MeticaMediationInfo", System.StringComparison.Ordinal) < 0
                                        ? 0 : src.IndexOf("MeticaMediationInfo", System.StringComparison.Ordinal)).Count(c => c == '\n') + 1;

                                    // arg[0] = mediation type enum
                                    if (medParts.Count > 0)
                                    {
                                        string typeExpr = medParts[0].Trim();
                                        // e.g. MeticaMediationInfo.MeticaMediationType.MAX
                                        var enumMatch = Regex.Match(typeExpr, @"MeticaMediationType\.(\w+)");
                                        mediationType = enumMatch.Success ? enumMatch.Groups[1].Value : typeExpr;
                                        mediationTypeSource = $"from {System.IO.Path.GetFileName(medCs)}";
                                    }
                                    // arg[1] = MAX SDK key (or other mediation key)
                                    if (medParts.Count > 1)
                                    {
                                        var t = TraceVariable(src, medCs, assets, medParts[1].Trim(), 0);
                                        mediationKey = t.value; mediationKeySource = t.source ?? $"expression '{medParts[1].Trim()}'";
                                    }
                                }
                            }
                            else
                            {
                                // mediationInfo is null or not used — no mediation configured
                                mediationInfoFound = medArg.ToLower() == "null";
                            }
                        }
                    }
                }

                // Also check for MeticaInitConfig directly (without InitializeAsync wrapper)
                if (!initCallFound)
                {
                    var cfgDirect = Regex.Match(src, @"new\s+MeticaInitConfig\s*\(", RegexOptions.IgnoreCase);
                    if (cfgDirect.Success)
                    {
                        codeFile = cs;
                        int cfgParen = src.IndexOf('(', cfgDirect.Index + cfgDirect.Length - 1);
                        string cfgArgs = cfgParen >= 0 ? ExtractParenContent(src, cfgParen) : null;
                        if (cfgArgs != null)
                        {
                            var cfgParts = SplitArgs(cfgArgs);
                            int cfgLine  = src.Substring(0, cfgDirect.Index).Count(c => c == '\n') + 1;
                            if (cfgParts.Count > 0) { var t = TraceVariable(src, cs, assets, cfgParts[0].Trim(), cfgLine); apiKey = t.value; apiKeySource = t.source; }
                            if (cfgParts.Count > 1) { var t = TraceVariable(src, cs, assets, cfgParts[1].Trim(), cfgLine); appId  = t.value; appIdSource  = t.source; }
                            if (cfgParts.Count > 2) { var t = TraceVariable(src, cs, assets, cfgParts[2].Trim(), cfgLine); userId = t.value; userIdSource = t.source; }
                        }
                    }
                }

                if (initCallFound && apiKey != null && appId != null) break;
            }

            // If no code scan found anything, fall back to basic MeticaAPI.Initialize patterns
            if (apiKey == null && appId == null && file == null)
            {
                foreach (var cs in GetAllCsFiles())
                {
                    string src = SafeReadFile(cs);
                    if (src == null || (!src.Contains("Metica") && !src.Contains("metica"))) continue;
                    var m1 = Regex.Match(src, @"MeticaAPI\s*\.\s*Initialize\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (m1.Success) { appId = m1.Groups[1].Value; apiKey = m1.Groups[2].Value; codeFile = cs; break; }
                    var m2a = Regex.Match(src, @"\bAppId\s*=\s*""([^""]+)""");
                    var m2b = Regex.Match(src, @"\bApiKey\s*=\s*""([^""]+)""");
                    if (m2a.Success && appId == null) { appId = m2a.Groups[1].Value; codeFile = cs; }
                    if (m2b.Success && apiKey == null) { apiKey = m2b.Groups[1].Value; codeFile = cs; }
                }
            }

            string usedFile = file ?? codeFile;

            // ── Output API Keys ────────────────────────────────────────────────────
            string keyNote = apiKeySource != null ? $" [{apiKeySource}]" : (file == null && codeFile != null ? " (from code)" : "");
            AddField(result, TAB_METICA, "API Keys", "Android API Key" + keyNote, "Android",
                "AndroidApiKey", usedFile, apiKey ?? GetYaml(file, "AndroidApiKey"),
                overrides.GetValueOrDefault("metica_android_api_key"), isExternalDev);
            AddField(result, TAB_METICA, "API Keys", "iOS API Key" + keyNote, "iOS",
                "iOSApiKey", usedFile, apiKey ?? GetYaml(file, "iOSApiKey"),
                overrides.GetValueOrDefault("metica_ios_api_key"), isExternalDev);

            // ── Output App IDs ─────────────────────────────────────────────────────
            string idNote = appIdSource != null ? $" [{appIdSource}]" : keyNote;
            AddField(result, TAB_METICA, "App IDs", "Android App ID" + idNote, "Android",
                "AndroidAppID", usedFile, appId ?? GetYaml(file, "AndroidAppID"),
                overrides.GetValueOrDefault("metica_android_app_id"), isExternalDev);
            AddField(result, TAB_METICA, "App IDs", "iOS App ID" + idNote, "iOS",
                "iOSAppID", usedFile, appId ?? GetYaml(file, "iOSAppID"),
                overrides.GetValueOrDefault("metica_ios_app_id"), isExternalDev);

            // ── Output User ID ─────────────────────────────────────────────────────
            if (userId != null || userIdSource != null || initCallFound)
            {
                string uidNote = userIdSource != null ? $" [{userIdSource}]" : "";
                result.AllFields.Add(new FieldResult
                {
                    Tab = TAB_METICA, Section = "Config",
                    FieldName    = "User ID" + uidNote, Platform = "",
                    ProjectValue = userId ?? $"(runtime — {userIdSource ?? "passed at runtime"})",
                    ExpectedValue = "",
                    AssetPath = usedFile, YamlKey = "UserId",
                    Status = userId != null ? FieldStatus.Match : FieldStatus.Empty
                });
            }

            // ── Output Mediation Info ──────────────────────────────────────────────
            if (mediationInfoFound || mediationType != null || mediationKey != null)
            {
                result.AllFields.Add(new FieldResult
                {
                    Tab = TAB_METICA, Section = "Mediation",
                    FieldName    = "Mediation Type", Platform = "",
                    ProjectValue = mediationType != null
                        ? $"{mediationType}{(mediationTypeSource != null ? $"  [{mediationTypeSource}]" : "")}"
                        : "(null — no mediation configured)",
                    ExpectedValue = "",
                    AssetPath = usedFile, YamlKey = "",
                    Status = mediationType != null ? FieldStatus.Match : FieldStatus.Empty
                });

                if (mediationType != null)
                {
                    string mkNote = mediationKeySource != null ? $" [{mediationKeySource}]" : "";
                    result.AllFields.Add(new FieldResult
                    {
                        Tab = TAB_METICA, Section = "Mediation",
                        FieldName    = "Mediation SDK Key" + mkNote, Platform = "",
                        ProjectValue = mediationKey ?? $"(not found — {mediationKeySource ?? "check manually"})",
                        ExpectedValue = "",
                        AssetPath = usedFile, YamlKey = "",
                        Status = mediationKey != null ? FieldStatus.Match : FieldStatus.Missing
                    });
                }
            }
            else if (isExternalDev && file == null && !initCallFound)
            {
                // MeticaSdk.InitializeAsync not found at all — SDK not integrated
                result.AllFields.Add(new FieldResult
                {
                    Tab = TAB_METICA, Section = "Config",
                    FieldName    = "MeticaSdk.InitializeAsync", Platform = "",
                    ProjectValue = "Call not found in project — Metica may not be integrated",
                    ExpectedValue = "MeticaSdk.InitializeAsync(config, mediationInfo)",
                    AssetPath = null, YamlKey = "",
                    Status = FieldStatus.Missing
                });
            }
        }

        // ── Adjust ────────────────────────────────────────────────────────────────

        private static void ScanAdjust(List<string> assets, ScanResult result,
                                        Dictionary<string, string> overrides, bool isExternalDev = false)
        {
            string gdPath = Path.Combine(Application.dataPath,
                "GDMonetization", "Runtime", "Resources", "Configurations", "AdjustToken.asset");
            var file = File.Exists(gdPath) ? gdPath.Replace('\\', '/') : FindAsset(assets, "AdjustToken");

            // Non-GD: reverse engineer AdjustConfig initialization
            // Strategy:
            //   1. Find "new AdjustConfig(...)" or "Adjust.InitSdk(...)" anywhere in project
            //   2. Extract the argument expressions (may be literals, variables, properties, fields)
            //   3. For each argument, trace back to its source:
            //      - string literal → use directly
            //      - this.fieldName → find field/property in same class, trace its assignment
            //      - variable → find assignment in same method or class
            //      - SO property (Resources.Load → .field) → find asset file + read value
            //      - PlayerPrefs → flag it

            string codeToken = null, codeEnv = null, codeLogLevel = null, codeFile = null;
            string tokenSource = null, envSource = null, logSource = null; // human-readable source description
            int codeEnvLine = 0, codeLogLine = 0, codeTokenLine = 0;

            if (isExternalDev && file == null)
            {
                foreach (var cs in GetAllCsFiles())
                {
                    string src = SafeReadFile(cs);
                    if (src == null || (!src.Contains("Adjust") && !src.Contains("adjust"))) continue;

                    // ── Step 1: Find new AdjustConfig(...) — possibly multiline ────────────
                    // Match: new AdjustConfig( arg1, arg2 [, arg3] )  across up to 5 lines
                    var cfgMatch = Regex.Match(src,
                        @"new\s+AdjustConfig\s*\(\s*([\s\S]*?)\)",
                        RegexOptions.Multiline);
                    if (!cfgMatch.Success) continue;

                    codeFile = cs;
                    string fileName = System.IO.Path.GetFileName(cs);
                    int cfgLine = src.Substring(0, cfgMatch.Index).Count(c => c == '\n') + 1;

                    // Split arguments — careful with nested parens (e.g. (this.logLevel == AdjustLogLevel.Suppress))
                    string argsRaw = cfgMatch.Groups[1].Value.Trim();
                    var args = SplitArgs(argsRaw); // custom split respecting parens

                    // arg[0] = token, arg[1] = environment, arg[2] = logLevel expression (optional)
                    string tokenArg = args.Count > 0 ? args[0].Trim() : null;
                    string envArg   = args.Count > 1 ? args[1].Trim() : null;
                    string logArg   = args.Count > 2 ? args[2].Trim() : null;

                    codeTokenLine = cfgLine;
                    codeEnvLine   = cfgLine;
                    codeLogLine   = cfgLine;

                    // ── Step 2: Trace token argument ─────────────────────────────────────
                    if (tokenArg != null)
                    {
                        if (tokenArg.StartsWith("\""))
                        {
                            // Hardcoded string literal
                            codeToken   = tokenArg.Trim('"');
                            tokenSource = $"hardcoded in {fileName}:{cfgLine}";
                        }
                        else
                        {
                            // Variable/property — trace it
                            var traced = TraceVariable(src, cs, assets, tokenArg, cfgLine);
                            codeToken   = traced.value;
                            tokenSource = traced.source;
                        }
                    }

                    // ── Step 3: Trace environment argument ───────────────────────────────
                    if (envArg != null)
                    {
                        if (envArg.Contains("AdjustEnvironment.Production"))
                        {
                            codeEnv    = "production";
                            envSource  = $"hardcoded in {fileName}:{cfgLine}";
                        }
                        else if (envArg.Contains("AdjustEnvironment.Sandbox"))
                        {
                            codeEnv    = "sandbox";
                            envSource  = $"hardcoded in {fileName}:{cfgLine}";
                        }
                        else
                        {
                            // Variable/field — trace it
                            var traced = TraceVariable(src, cs, assets, envArg, cfgLine);
                            if (traced.value != null)
                            {
                                codeEnv   = traced.value.ToLower().Contains("production") ? "production" : "sandbox";
                                envSource = traced.source;
                            }
                            else
                            {
                                // Still unknown — look for any AdjustEnvironment assignment to this var
                                var envAssign = Regex.Match(src,
                                    $@"\b{Regex.Escape(envArg.Replace("this.", "").Trim())}\b\s*=\s*(AdjustEnvironment\.\w+)");
                                if (envAssign.Success)
                                {
                                    string ev = envAssign.Groups[1].Value;
                                    codeEnv   = ev.ToLower().Contains("production") ? "production" : "sandbox";
                                    int eLine = src.Substring(0, envAssign.Index).Count(c => c == '\n') + 1;
                                    envSource = $"assigned in {fileName}:{eLine}";
                                }
                                else
                                {
                                    codeEnv   = traced.source?.Contains("asset") == true ? "from-asset" : null;
                                    envSource = traced.source;
                                }
                            }
                        }
                    }

                    // ── Step 4: Trace logLevel argument (optional 3rd arg) ────────────────
                    if (logArg != null)
                    {
                        // Common pattern: (this.logLevel == AdjustLogLevel.Suppress)
                        var logLiteral = Regex.Match(logArg, @"AdjustLogLevel\.(\w+)");
                        if (logLiteral.Success)
                        {
                            codeLogLevel = logLiteral.Groups[1].Value;
                            logSource    = $"hardcoded in {fileName}:{cfgLine}";
                        }
                        else
                        {
                            var traced = TraceVariable(src, cs, assets, logArg, cfgLine);
                            if (traced.value != null)
                            {
                                var lm = Regex.Match(traced.value, @"AdjustLogLevel\.(\w+)");
                                codeLogLevel = lm.Success ? lm.Groups[1].Value : traced.value;
                                logSource    = traced.source;
                            }
                            // Also check for separate logLevel assignment in file
                            if (codeLogLevel == null)
                            {
                                var mLog = Regex.Match(src, @"[Ll]og[Ll]evel\s*=\s*AdjustLogLevel\.(\w+)");
                                if (mLog.Success)
                                {
                                    codeLogLevel = mLog.Groups[1].Value;
                                    int ll = src.Substring(0, mLog.Index).Count(c => c == '\n') + 1;
                                    logSource = $"assigned in {fileName}:{ll}";
                                }
                            }
                        }
                    }

                    break; // found AdjustConfig — done
                }

                // Fallback: if AdjustConfig not found above, look for separate AdjustEnvironment assignment
                if (codeFile == null)
                {
                    foreach (var cs in GetAllCsFiles())
                    {
                        string src = SafeReadFile(cs);
                        if (src == null || !src.Contains("Adjust")) continue;
                        var mEnv = Regex.Match(src, @"AdjustEnvironment\.(Production|Sandbox)", RegexOptions.IgnoreCase);
                        if (mEnv.Success)
                        {
                            codeEnv     = mEnv.Groups[1].Value.ToLower();
                            codeEnvLine = src.Substring(0, mEnv.Index).Count(c => c == '\n') + 1;
                            codeFile    = cs;
                            envSource   = $"found in {System.IO.Path.GetFileName(cs)}:{codeEnvLine}";
                        }
                        var mLog = Regex.Match(src, @"[Ll]og[Ll]evel\s*=\s*AdjustLogLevel\.(\w+)");
                        if (mLog.Success && codeLogLevel == null)
                        {
                            codeLogLevel = mLog.Groups[1].Value;
                            codeLogLine  = src.Substring(0, mLog.Index).Count(c => c == '\n') + 1;
                            logSource    = $"found in {System.IO.Path.GetFileName(cs)}:{codeLogLine}";
                        }
                        if (codeEnv != null && codeLogLevel != null) break;
                    }
                }
            }

            string usedFile    = file ?? codeFile;
            string codeFileName = codeFile != null ? System.IO.Path.GetFileName(codeFile) : null;

            // Token
            bool tokenViaSO = file == null && codeToken == null && (codeEnv != null || codeLogLevel != null);
            if (tokenViaSO)
            {
                string soAsset   = assets.FirstOrDefault(a => a.ToLower().Contains("adjust") && a.EndsWith(".asset"));
                string soAndroid = soAsset != null ? GetYaml(soAsset, "Android") ?? GetYaml(soAsset, "AppToken") ?? GetYaml(soAsset, "Token") : null;
                string soIos     = soAsset != null ? GetYaml(soAsset, "iOS")     ?? GetYaml(soAsset, "AppToken") ?? GetYaml(soAsset, "Token") : null;
                string soName    = soAsset != null ? System.IO.Path.GetFileName(soAsset) : null;
                FieldStatus soStatus = soAndroid != null ? FieldStatus.Match : FieldStatus.Missing;

                result.AllFields.Add(new FieldResult
                {
                    Tab = TAB_ADJUST, Section = "Tokens", FieldName = "Android Token", Platform = "Android",
                    ProjectValue  = soAndroid ?? $"via ScriptableObject — locate {soName ?? "AdjustToken.asset"}",
                    ExpectedValue = "", AssetPath = soAsset ?? codeFile, YamlKey = "Android", Status = soStatus
                });
                result.AllFields.Add(new FieldResult
                {
                    Tab = TAB_ADJUST, Section = "Tokens", FieldName = "iOS Token", Platform = "iOS",
                    ProjectValue  = soIos ?? $"via ScriptableObject — locate {soName ?? "AdjustToken.asset"}",
                    ExpectedValue = "", AssetPath = soAsset ?? codeFile, YamlKey = "iOS", Status = soStatus
                });
            }
            else
            {
                string tNote = tokenSource != null ? $" ({tokenSource})" : (file == null && codeFile != null ? $" (from code)" : "");
                AddField(result, TAB_ADJUST, "Tokens", "Android Token" + tNote, "Android",
                    "Android", usedFile, codeToken ?? GetYaml(file, "Android"),
                    overrides.GetValueOrDefault("adjust_android_token"), isExternalDev);
                AddField(result, TAB_ADJUST, "Tokens", "iOS Token" + tNote, "iOS",
                    "iOS", usedFile, codeToken ?? GetYaml(file, "iOS"),
                    overrides.GetValueOrDefault("adjust_ios_token"), isExternalDev);
            }

            // Environment
            string env = GetYaml(file, "Environment");
            string envDisplay; FieldStatus envStatus;
            if (file != null)
            {
                envDisplay = env switch { "0" => "Sandbox", "1" => "Production", null => "(not found)", _ => $"Unknown ({env})" };
                envStatus  = env == "1" ? FieldStatus.Match : env == "0" ? FieldStatus.Mismatch : FieldStatus.Missing;
            }
            else if (codeEnv != null)
            {
                string src = codeEnv == "production" ? "Production" : "Sandbox";
                string loc = envSource != null ? $"  [{envSource}]" : (codeFileName != null ? $"  [{codeFileName}:{codeEnvLine}]" : "");
                envDisplay = $"{src} (from code{loc})";
                envStatus  = codeEnv == "production" ? FieldStatus.Match : FieldStatus.Mismatch;
            }
            else { envDisplay = "(not found)"; envStatus = FieldStatus.Missing; }

            result.AllFields.Add(new FieldResult
            {
                Tab = TAB_ADJUST, Section = "Settings", FieldName = "Environment", Platform = "",
                ProjectValue  = envDisplay + (env != null ? $"  (raw value: {env})" : ""),
                ExpectedValue = "Production  (raw value: 1)",
                AssetPath = usedFile, YamlKey = "Environment", Status = envStatus
            });

            // LogLevel
            string logLevel = GetYaml(file, "LogLevel");
            string logDisplay;
            if (logLevel != null)
            {
                logDisplay = logLevel switch
                {
                    "0" => "Verbose", "1" => "Debug", "2" => "Info", "3" => "Warn",
                    "4" => "Error",   "5" => "Assert", "6" => "Suppress", _ => $"Level {logLevel}"
                };
            }
            else if (codeLogLevel != null)
            {
                string loc = logSource != null ? $"  [{logSource}]" : (codeFileName != null ? $"  [{codeFileName}:{codeLogLine}]" : "");
                logDisplay = $"{codeLogLevel} (from code{loc})";
            }
            else logDisplay = "(not found)";

            bool logOkForProd = logLevel == "3" || logLevel == "4" || logLevel == "5" || logLevel == "6"
                             || (codeLogLevel != null && new[]{"suppress","error","assert","warn"}.Contains(codeLogLevel.ToLower()));

            result.AllFields.Add(new FieldResult
            {
                Tab = TAB_ADJUST, Section = "Settings", FieldName = "Log Level", Platform = "",
                ProjectValue  = logDisplay + (logLevel != null ? $"  (raw: {logLevel})" : ""),
                ExpectedValue = "Warn / Error / Assert / Suppress for production",
                AssetPath = usedFile, YamlKey = "LogLevel",
                Status = (file == null && codeLogLevel == null) ? FieldStatus.Missing
                       : logOkForProd ? FieldStatus.Match : FieldStatus.Mismatch
            });
        }

        // ── AppMetrica ────────────────────────────────────────────────────────────

        private static void ScanAppMetrica(List<string> assets, ScanResult result,
                                            Dictionary<string, string> overrides, bool isExternalDev = false)
        {
            string gdPath = Path.Combine(Application.dataPath,
                "GDMonetization", "Runtime", "Resources", "Configurations", "AppMetricaSettings.asset");
            var file = File.Exists(gdPath) ? gdPath.Replace('\\', '/')
                     : FindAsset(assets, "AppMetricaSettings")
                    ?? FindAsset(assets, "YandexMetricaSettings");

            // Non-GD: scan .cs files for AppMetrica.Activate(new AppMetricaConfig("KEY"))
            string codeKey = null, codeFile = null;
            if (isExternalDev && file == null)
            {
                foreach (var cs in GetAllCsFiles())
                {
                    string src = SafeReadFile(cs);
                    if (src == null || (!src.Contains("AppMetrica") && !src.Contains("Metrica") && !src.Contains("YMMYandex"))) continue;
                    var m1 = Regex.Match(src, @"AppMetrica\s*\.\s*Activate\s*\(\s*new\s+AppMetricaConfig\s*\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (m1.Success && codeKey == null) { codeKey = m1.Groups[1].Value; codeFile = cs; }
                    var m2 = Regex.Match(src, @"new\s+(?:AppMetricaConfig|YMMYandexMetricaConfig)\s*\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (m2.Success && codeKey == null) { codeKey = m2.Groups[1].Value; codeFile = cs; }
                    if (codeKey != null) break;
                }
            }

            string usedFile   = file ?? codeFile;
            string sourceNote = codeFile != null && file == null ? " (from code)" : "";

            AddField(result, TAB_APPMETRICA, "API Keys", "Android API Key" + sourceNote, "Android",
                "Android", usedFile, codeKey ?? GetYaml(file, "Android"),
                overrides.GetValueOrDefault("appmetrica_android_key"), isExternalDev);
            AddField(result, TAB_APPMETRICA, "API Keys", "iOS API Key" + sourceNote, "iOS",
                "iOS", usedFile, codeKey ?? GetYaml(file, "iOS"),
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
                                         Dictionary<string, string> overrides,
                                         SDKScanConfig config = null)
        {
            string gdPath = Path.Combine(Application.dataPath,
                "GDMonetization", "Runtime", "Resources", "Configurations", "AdUnitsSettings.asset");
            var file = File.Exists(gdPath) ? gdPath.Replace('\\', '/') : FindAsset(assets, "AdUnitsSettings");

            if (file == null)
            {
                // No asset — scan code for actual SDK ad unit calls (class name + line number)
                ScanAdUnitsFromCode(result, config);
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

            // Parse each network block — only scan networks that were selected
            // AppLovin/AdMob always go together (same asset)
            if (config == null || config.AppLovin)
                ScanNetworkBlock(result, file, content, "Applovin", TAB_ADUNITS, overrides);
            if (config == null || config.AppLovin)
                ScanNetworkBlock(result, file, content, "Admob",    TAB_ADUNITS, overrides);
            if (config == null || config.Metica)
                ScanNetworkBlock(result, file, content, "Metica",   TAB_ADUNITS, overrides);
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

        // ── Ad Units — code scan ──────────────────────────────────────────────────
        // Scans all .cs files for actual SDK ad unit load/create calls.
        // Reports: class name, line number, ad type, network — with no asset file needed.
        // Patterns verified from GD SDK source files (Archive.zip).

        private class AdUnitHit
        {
            public string Network;
            public string AdType;
            public string ClassName;
            public string FilePath;
            public int    LineNumber;
            public string AdUnitIdValue;  // actual string value if found
            public string ValueSource;    // where the value came from
        }

        private static void ScanAdUnitsFromCode(ScanResult result, SDKScanConfig config)
        {
            var hits = new List<AdUnitHit>();

            // Exact patterns from GD SDK source (Archive.zip)
            var patterns = new (string net, string type, string regex)[]
            {
                ("AppLovin", "Banner",        @"MaxSdk\.CreateBanner\s*\("),
                ("AppLovin", "MRec",          @"MaxSdk\.CreateMRec\s*\("),
                ("AppLovin", "Interstitial",  @"MaxSdk\.LoadInterstitial\s*\("),
                ("AppLovin", "Rewarded",      @"MaxSdk\.LoadRewardedAd\s*\("),
                ("AppLovin", "AppOpen",       @"MaxSdk\.LoadAppOpenAd\s*\("),
                ("AppLovin", "AppOpen",       @"MaxSdk\.ShowAppOpenAd\s*\("),
                ("AdMob",    "Banner",        @"new\s+BannerView\s*\("),
                ("AdMob",    "MRec",          @"new\s+BannerView\s*\([^,]+,\s*AdSize\.MediumRectangle"),
                ("AdMob",    "Interstitial",  @"InterstitialAd\.Load\s*\("),
                ("AdMob",    "Rewarded",      @"RewardedAd\.Load\s*\("),
                ("AdMob",    "AppOpen",       @"AppOpenAd\.Load\s*\("),
                ("Metica",   "Banner",        @"MeticaSdk\.Ads\.CreateBanner\s*\("),
                ("Metica",   "MRec",          @"MeticaSdk\.Ads\.CreateMrec\s*\("),
                ("Metica",   "MRec",          @"MeticaSdk\.Ads\.LoadMrec\s*\("),
                ("Metica",   "Interstitial",  @"MeticaSdk\.Ads\.LoadInterstitial\s*\("),
                ("Metica",   "Rewarded",      @"MeticaSdk\.Ads\.LoadRewarded\s*\("),
            };

            foreach (var cs in GetAllCsFiles())
            {
                string src = SafeReadFile(cs);
                if (src == null) continue;

                bool mayHaveHits = src.Contains("MaxSdk") || src.Contains("BannerView") ||
                                   src.Contains("InterstitialAd") || src.Contains("RewardedAd") ||
                                   src.Contains("AppOpenAd") || src.Contains("MeticaSdk");
                if (!mayHaveHits) continue;

                string className = System.IO.Path.GetFileNameWithoutExtension(cs);
                var classMatch = Regex.Match(src, @"(?:sealed\s+)?class\s+(\w+)");
                if (classMatch.Success) className = classMatch.Groups[1].Value;

                string[] lines = src.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    foreach (var (net, type, regex) in patterns)
                    {
                        if (!Regex.IsMatch(line, regex, RegexOptions.IgnoreCase)) continue;

                        bool duplicate = hits.Any(h =>
                            h.Network == net && h.AdType == type && h.ClassName == className);
                        if (duplicate) continue;

                        // Extract first argument from the call (adUnitId)
                        string callText = string.Join(" ", lines.Skip(i).Take(3));
                        callText = Regex.Replace(callText, @"\s+", " ");
                        var argMatch = Regex.Match(callText, @"\(\s*([\s\S]+?)(?:\s*,|\s*\))");
                        string firstArg = argMatch.Success ? argMatch.Groups[1].Value.Trim() : null;

                        string adUnitValue = null;
                        string valueSource = null;

                        if (firstArg != null)
                        {
                            if (firstArg.StartsWith("\""))
                            {
                                // Direct string literal
                                adUnitValue = firstArg.Trim('\"');
                                valueSource = $"literal in {className}.cs:{i + 1}";
                            }
                            else
                            {
                                // Trace the variable/field/property back to its value
                                var traced = TraceVariable(src, cs, new List<string>(), firstArg, i + 1);
                                adUnitValue = traced.value;
                                valueSource = traced.source ?? $"\'{firstArg}\' in {className}.cs:{i + 1}";

                                // If still not found — search other files for Initialize("value") pattern
                                if (adUnitValue == null)
                                {
                                    foreach (var otherCs in GetAllCsFiles())
                                    {
                                        string otherSrc = SafeReadFile(otherCs);
                                        if (otherSrc == null) continue;

                                        // .Initialize("hardcoded_id")
                                        var initLit = Regex.Match(otherSrc,
                                            @"\.Initialize\s*\(\s*""([^""]+)""",
                                            RegexOptions.IgnoreCase);
                                        if (initLit.Success)
                                        {
                                            int ln = otherSrc.Substring(0, initLit.Index).Count(c => c == '\n') + 1;
                                            adUnitValue = initLit.Groups[1].Value;
                                            valueSource = $"Initialize() in {System.IO.Path.GetFileName(otherCs)}:{ln}";
                                            break;
                                        }

                                        // .Initialize(varName) → trace varName
                                        var initVar = Regex.Match(otherSrc,
                                            @"\.Initialize\s*\(\s*(\w+)\s*[,)]",
                                            RegexOptions.IgnoreCase);
                                        if (initVar.Success)
                                        {
                                            string vn = initVar.Groups[1].Value;
                                            int ln = otherSrc.Substring(0, initVar.Index).Count(c => c == '\n') + 1;
                                            var t2 = TraceVariable(otherSrc, otherCs, new List<string>(), vn, ln);
                                            if (t2.value != null) { adUnitValue = t2.value; valueSource = t2.source; break; }
                                        }
                                    }
                                }

                                if (adUnitValue == null)
                                    valueSource = $"\'{firstArg}\' — not traceable to a literal (set via asset or at runtime)";
                            }
                        }

                        hits.Add(new AdUnitHit
                        {
                            Network       = net,
                            AdType        = type,
                            ClassName     = className,
                            FilePath      = cs,
                            LineNumber    = i + 1,
                            AdUnitIdValue = adUnitValue,
                            ValueSource   = valueSource
                        });
                        break;
                    }
                }
            }

            if (hits.Count == 0)
            {
                result.AllFields.Add(new FieldResult
                {
                    Tab = TAB_ADUNITS, Section = "Code Scan",
                    FieldName    = "No ad unit calls found",
                    ProjectValue = "No AppLovin / AdMob / Metica ad unit calls detected",
                    Status       = FieldStatus.Missing
                });
                return;
            }

            foreach (var grp in hits.GroupBy(h => h.Network))
            {
                foreach (var hit in grp.OrderBy(h => h.AdType))
                {
                    string display; FieldStatus status;
                    if (hit.AdUnitIdValue != null)
                    {
                        display = hit.ValueSource != null
                            ? $"{hit.AdUnitIdValue}  [{hit.ValueSource}]"
                            : hit.AdUnitIdValue;
                        status = FieldStatus.Match;
                    }
                    else
                    {
                        display = hit.ValueSource ?? $"{hit.ClassName}.cs:{hit.LineNumber}";
                        status  = FieldStatus.Empty;
                    }

                    result.AllFields.Add(new FieldResult
                    {
                        Tab           = TAB_ADUNITS,
                        Section       = $"{hit.Network} — Ad Units (from code)",
                        FieldName     = hit.AdType,
                        Platform      = "",
                        ProjectValue  = display,
                        ExpectedValue = "",
                        AssetPath     = hit.FilePath,
                        YamlKey       = "",
                        Status        = status
                    });
                }
            }
        }

        // ── Reverse engineering helpers ───────────────────────────────────────────

        // Extract content between matching parentheses starting at parenIndex (the '(' char)
        private static string ExtractParenContent(string src, int parenIndex)
        {
            int depth = 0;
            for (int i = parenIndex; i < src.Length; i++)
            {
                if (src[i] == '(') depth++;
                else if (src[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                        return src.Substring(parenIndex + 1, i - parenIndex - 1);
                }
            }
            return null;
        }

        // Split comma-separated arguments respecting nested parentheses
        private static List<string> SplitArgs(string argsRaw)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < argsRaw.Length; i++)
            {
                if (argsRaw[i] == '(') depth++;
                else if (argsRaw[i] == ')') depth--;
                else if (argsRaw[i] == ',' && depth == 0)
                {
                    result.Add(argsRaw.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            if (start < argsRaw.Length)
                result.Add(argsRaw.Substring(start).Trim());
            return result;
        }

        // Trace a variable/property/field expression back to its actual value or source
        private static (string value, string source) TraceVariable(
            string src, string csPath, List<string> assets, string expr, int fromLine)
        {
            string fileName = System.IO.Path.GetFileName(csPath);
            expr = expr.Trim();

            // 1. Already a string literal
            if (expr.StartsWith("\"") && expr.EndsWith("\""))
                return (expr.Trim('"'), $"literal in {fileName}:{fromLine}");

            // 2. AdjustEnvironment.X or AdjustLogLevel.X enum
            var enumMatch = Regex.Match(expr, @"Adjust(?:Environment|LogLevel)\.(\w+)");
            if (enumMatch.Success)
                return (enumMatch.Value, $"enum in {fileName}:{fromLine}");

            // 3. Strip "this." prefix
            string bare = Regex.Replace(expr, @"^this\.", "");

            // 4. Look for field/property assignment in same file: bareVar = "value" or = AdjustX.Y
            var assignLit = Regex.Match(src, $@"\b{Regex.Escape(bare)}\b\s*=\s*""([^""]+)""");
            if (assignLit.Success)
            {
                int line = src.Substring(0, assignLit.Index).Count(c => c == '\n') + 1;
                return (assignLit.Groups[1].Value, $"assigned in {fileName}:{line}");
            }

            var assignEnum = Regex.Match(src, $@"\b{Regex.Escape(bare)}\b\s*=\s*(Adjust(?:Environment|LogLevel)\.\w+)");
            if (assignEnum.Success)
            {
                int line = src.Substring(0, assignEnum.Index).Count(c => c == '\n') + 1;
                return (assignEnum.Groups[1].Value, $"assigned in {fileName}:{line}");
            }

            // 5. Look for SO loading pattern: var soVar = Resources.Load<T>("path"); ... soVar.bare
            //    Try to find: Resources.Load assigned to a variable, then .bare accessed
            var loadMatch = Regex.Match(src,
                @"(\w+)\s*=\s*Resources\.Load[^(]*\(\s*""([^""]+)""",
                RegexOptions.IgnoreCase);
            if (loadMatch.Success)
            {
                string soVar      = loadMatch.Groups[1].Value;
                string resPath    = loadMatch.Groups[2].Value;
                string assetName  = System.IO.Path.GetFileName(resPath);

                // Find asset file matching the Resources path
                var matchedAsset = assets.FirstOrDefault(a =>
                    System.IO.Path.GetFileNameWithoutExtension(a)
                        .Equals(assetName, System.StringComparison.OrdinalIgnoreCase));

                if (matchedAsset != null)
                {
                    // Try to read the YAML key that matches the property name
                    string val = GetYaml(matchedAsset, bare)
                              ?? GetYaml(matchedAsset, "AppToken")
                              ?? GetYaml(matchedAsset, "Android")
                              ?? GetYaml(matchedAsset, "iOS");
                    if (val != null)
                        return (val, $"from {System.IO.Path.GetFileName(matchedAsset)} via {soVar}.{bare}");
                    return (null, $"from SO {System.IO.Path.GetFileName(matchedAsset)} (key '{bare}' not found)");
                }

                // Asset not found but SO pattern confirmed
                return (null, $"via Resources.Load(\"{resPath}\") — asset not found in project");
            }

            // 6. PlayerPrefs
            var ppMatch = Regex.Match(src, $@"PlayerPrefs\.GetString\s*\(\s*""([^""]+)""[^)]*\)[^;]*{Regex.Escape(bare)}",
                RegexOptions.IgnoreCase);
            if (ppMatch.Success)
                return (null, $"PlayerPrefs key \"{ppMatch.Groups[1].Value}\"");

            // 7. Look in OTHER .cs files for this variable name assigned
            foreach (var otherCs in GetAllCsFiles())
            {
                if (otherCs == csPath) continue;
                string otherSrc = SafeReadFile(otherCs);
                if (otherSrc == null || !otherSrc.Contains(bare)) continue;
                var otherAssign = Regex.Match(otherSrc, $@"\b{Regex.Escape(bare)}\b\s*=\s*""([^""]+)""");
                if (otherAssign.Success)
                {
                    int line = otherSrc.Substring(0, otherAssign.Index).Count(c => c == '\n') + 1;
                    return (otherAssign.Groups[1].Value,
                        $"assigned in {System.IO.Path.GetFileName(otherCs)}:{line}");
                }
            }

            // 8. Unknown — return the expression itself so at least we show something
            return (null, $"expression '{expr}' — value determined at runtime");
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

        // Returns cached file list — populated once per scan, O(1) after first call
        private static string[] GetAllCsFiles()
            => _cachedCsFiles ?? Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);

        // Returns cached file content — O(1) dictionary lookup after InitScanCache
        private static string SafeReadFile(string path)
        {
            if (_cachedContents != null)
                return _cachedContents.TryGetValue(path, out string cached) ? cached : null;
            try { return File.ReadAllText(path); }
            catch { return null; }
        }

        // Read a value from a .cs file — tries named assignment then long string literals
        public static string ReadCsValue(string filePath, string yamlKey)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                string content = File.ReadAllText(filePath);
                var m = Regex.Match(content,
                    $@"{Regex.Escape(yamlKey)}\s*[=:]\s*""([^""]+)""",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (m.Success) return m.Groups[1].Value.Trim();
                var keys = Regex.Matches(content, @"""([A-Za-z0-9_\-]{20,})""");
                if (keys.Count > 0) return keys[0].Groups[1].Value.Trim();
                return null;
            }
            catch { return null; }
        }
    }
}
