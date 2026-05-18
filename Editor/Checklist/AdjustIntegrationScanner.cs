// AdjustIntegrationScanner.cs
// Validates Adjust SDK *integration* (init path, manifest permissions, EDM4U deps,
// iOS frameworks, ad revenue calls). Complements existing ScanAdjust which validates
// *config* values (tokens, environment, log level).
//
// Output: FieldResult rows with Tab = TAB_ADJUST (2) and SubTab = "Init Path" /
// "Manifest" / "Dependencies" / "iOS" / "Ad Revenue". ChecklistWindow routes by SubTab.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GDChecklist
{
    public static class AdjustIntegrationScanner
    {
        private const int TAB_ADJUST = 2;

        // Sub-tab names — must match ChecklistWindow's sub-tab labels exactly
        public const string SUB_INIT       = "Init Path";
        public const string SUB_MANIFEST   = "Manifest";
        public const string SUB_DEPS       = "Dependencies";
        public const string SUB_IOS        = "iOS";
        public const string SUB_ADREVENUE  = "Ad Revenue";

        public static void Append(ScanResult result, string dataPath)
        {
            try
            {
                ScanProgress.Report("Adjust: init path…", 0.78f);
                ScanInitPath(result, dataPath);

                ScanProgress.Report("Adjust: AndroidManifest…", 0.82f);
                ScanManifest(result, dataPath);

                ScanProgress.Report("Adjust: dependencies…", 0.86f);
                ScanDependencies(result, dataPath);

                ScanProgress.Report("Adjust: iOS settings…", 0.90f);
                ScanIOS(result, dataPath);

                ScanProgress.Report("Adjust: ad revenue calls…", 0.93f);
                ScanAdRevenue(result, dataPath);
            }
            catch (System.Exception ex)
            {
                AddRow(result, SUB_INIT, "Integration scan", FieldStatus.Mismatch,
                    "Scan error", ex.Message, "");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  1. INIT PATH
        // ══════════════════════════════════════════════════════════════════════════

        private static void ScanInitPath(ScanResult result, string dataPath)
        {
            // 1a. Locate Adjust.cs anywhere in project
            string adjustCsPath = LocateAdjustCs();
            if (string.IsNullOrEmpty(adjustCsPath))
            {
                AddRow(result, SUB_INIT, "SDK Installation", FieldStatus.Missing,
                    "Adjust SDK not installed",
                    "Adjust.cs not found anywhere in the project. Install via Package Manager (https://github.com/adjust/unity_sdk.git?path=Assets/Adjust) or import the .unitypackage.",
                    "");
                return;
            }
            AddRow(result, SUB_INIT, "SDK Installation", FieldStatus.Match,
                "Adjust SDK detected",
                "Adjust.cs found at " + adjustCsPath,
                adjustCsPath);

            // 1b. Resolve the Adjust MonoBehaviour Type via the MonoScript
            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(adjustCsPath);
            System.Type adjustType = monoScript != null ? monoScript.GetClass() : null;
            if (adjustType == null) adjustType = FindAdjustType();
            if (adjustType == null)
            {
                AddRow(result, SUB_INIT, "Adjust Type", FieldStatus.Mismatch,
                    "Could not resolve compiled Adjust type",
                    "The validator needs the compiled Adjust class to read instance fields. Make sure the Adjust SDK compiles without errors.",
                    "");
                return;
            }

            // 1c. Open scenes from build settings + active scene, find Adjust components, read via reflection
            var scenesToScan = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && File.Exists(s.path)) scenesToScan.Add(s.path);
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.path) && !scenesToScan.Contains(activeScene.path))
                scenesToScan.Add(activeScene.path);

            if (scenesToScan.Count == 0)
            {
                AddRow(result, SUB_INIT, "Scenes to scan", FieldStatus.Missing,
                    "No scenes in build settings",
                    "Add your bootstrap scene to File → Build Settings, then re-scan.",
                    "");
                return;
            }

            // Save preopen state so we restore cleanly
            var preOpen = new HashSet<string>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var sc = EditorSceneManager.GetSceneAt(i);
                if (sc.isLoaded && !string.IsNullOrEmpty(sc.path)) preOpen.Add(sc.path);
            }

            var instances = new List<(string scenePath, string goName, string appToken, int environment, int logLevel, bool startManually)>();

            foreach (var scenePath in scenesToScan)
            {
                Scene loadedScene;
                bool weOpenedIt = false;
                if (preOpen.Contains(scenePath))
                {
                    loadedScene = EditorSceneManager.GetSceneByPath(scenePath);
                }
                else
                {
                    try { loadedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive); weOpenedIt = true; }
                    catch { continue; }
                }

                foreach (var root in loadedScene.GetRootGameObjects())
                {
                    var comps = root.GetComponentsInChildren(adjustType, includeInactive: true);
                    foreach (var comp in comps)
                    {
                        string tok = GetFieldValueMulti<string>(comp, adjustType, "appToken");
                        int env = GetEnumOrInt(comp, adjustType, "environment");
                        int log = GetEnumOrInt(comp, adjustType, "logLevel");
                        bool manual = GetFieldValueMulti<bool>(comp, adjustType, "startManually", "startSdkManually");
                        instances.Add((scenePath, ((Component)comp).gameObject.name, tok, env, log, manual));
                    }
                }

                if (weOpenedIt)
                {
                    try { EditorSceneManager.CloseScene(loadedScene, removeScene: true); }
                    catch { }
                }
            }

            if (instances.Count == 0)
            {
                AddRow(result, SUB_INIT, "Adjust GameObject", FieldStatus.Missing,
                    "Not found in any scene",
                    "Either Adjust.prefab is not in a scene (code-only init), or the SDK is unused. If you initialize Adjust from code only, this is fine — see InitSdk call site check below.",
                    "");
            }
            else
            {
                foreach (var inst in instances)
                {
                    string label = $"[{Path.GetFileNameWithoutExtension(inst.scenePath)}] Init Mode";
                    if (inst.startManually)
                    {
                        AddRow(result, SUB_INIT, label, FieldStatus.Match,
                            "Start SDK Manually = ENABLED",
                            "Prefab fields are ignored at runtime — init must happen via Adjust.InitSdk(...) in code. Validated below.",
                            inst.scenePath);
                    }
                    else
                    {
                        // Auto-init — prefab fields are the source of truth
                        if (string.IsNullOrWhiteSpace(inst.appToken))
                        {
                            AddRow(result, SUB_INIT, label, FieldStatus.Missing,
                                "Start SDK Manually = DISABLED — App Token EMPTY in prefab",
                                "Adjust will fail to initialize. Either set App Token in prefab inspector, or check Start SDK Manually and call Adjust.InitSdk(config) from code.",
                                inst.scenePath);
                        }
                        else if (!Regex.IsMatch(inst.appToken, @"^[a-z0-9]{12}$"))
                        {
                            AddRow(result, SUB_INIT, label, FieldStatus.Mismatch,
                                $"Start SDK Manually = DISABLED — token '{inst.appToken}' invalid format",
                                "Adjust app tokens are exactly 12 lowercase alphanumeric characters.",
                                inst.scenePath);
                        }
                        else if (inst.environment == 0)
                        {
                            AddRow(result, SUB_INIT, label, FieldStatus.Mismatch,
                                "Start SDK Manually = DISABLED — Environment = Sandbox in prefab",
                                "Switch to Production before release builds — Sandbox data does not appear in production dashboards.",
                                inst.scenePath);
                        }
                        else
                        {
                            AddRow(result, SUB_INIT, label, FieldStatus.Match,
                                $"Auto-init from prefab (token {inst.appToken}, env=Production)",
                                "Adjust will initialize automatically from the prefab Awake().",
                                inst.scenePath);
                        }
                    }
                }
            }

            // 1d. Code scan: find Adjust.InitSdk(...) call sites
            var initCallSites = FindInitSdkCallSites(dataPath);

            bool anyManual = instances.Any(x => x.startManually);
            bool sceneAutoInit = instances.Count > 0 && !anyManual;

            if (initCallSites.Count == 0)
            {
                if (sceneAutoInit)
                {
                    AddRow(result, SUB_INIT, "InitSdk Call Site", FieldStatus.Match,
                        "No Adjust.InitSdk(...) call in code (auto-init handles it)",
                        "Adjust will initialize from the prefab. Code-side InitSdk is not required.",
                        "");
                }
                else if (anyManual)
                {
                    AddRow(result, SUB_INIT, "InitSdk Call Site", FieldStatus.Missing,
                        "Start SDK Manually is ON but no Adjust.InitSdk(...) call found in code",
                        "Adjust will never initialize. Either disable Start SDK Manually on the prefab, or add Adjust.InitSdk(new AdjustConfig(token, env)) somewhere in your bootstrap code.",
                        "");
                }
                else
                {
                    AddRow(result, SUB_INIT, "InitSdk Call Site", FieldStatus.Missing,
                        "No Adjust prefab in any scene AND no InitSdk call in code",
                        "Adjust SDK is installed but never initialized.",
                        "");
                }
            }
            else if (initCallSites.Count == 1)
            {
                var site = initCallSites[0];
                if (sceneAutoInit)
                {
                    AddRow(result, SUB_INIT, "InitSdk Call Site", FieldStatus.Mismatch,
                        $"Double init risk — Adjust.InitSdk(...) at {RelPath(site.file)}:{site.line} AND prefab auto-init enabled",
                        "Double initialization can corrupt session tracking. Enable Start SDK Manually on the prefab, or remove the code call.",
                        site.file, site.line);
                }
                else
                {
                    AddRow(result, SUB_INIT, "InitSdk Call Site", FieldStatus.Match,
                        $"Single Adjust.InitSdk(...) call at {RelPath(site.file)}:{site.line}",
                        "",
                        site.file, site.line);
                }
            }
            else
            {
                var locs = string.Join("\n", initCallSites.Select(s => $"  • {RelPath(s.file)}:{s.line}"));
                AddRow(result, SUB_INIT, "InitSdk Call Site", FieldStatus.Mismatch,
                    $"{initCallSites.Count} Adjust.InitSdk(...) calls found — only one is allowed",
                    "Multiple init calls corrupt session tracking. Locations:\n" + locs,
                    "");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  2. ANDROID MANIFEST
        // ══════════════════════════════════════════════════════════════════════════

        private static void ScanManifest(ScanResult result, string dataPath)
        {
            string manifestPath = Path.Combine(dataPath, "Plugins", "Android", "AndroidManifest.xml").Replace('\\', '/');
            if (!File.Exists(manifestPath))
            {
                AddRow(result, SUB_MANIFEST, "AndroidManifest.xml", FieldStatus.Missing,
                    "No custom AndroidManifest.xml at Assets/Plugins/Android/",
                    "Adjust's post-build process will copy a default manifest at build time. If you need custom permissions or activities, create your own at this path.",
                    "");
                return;
            }
            AddRow(result, SUB_MANIFEST, "AndroidManifest.xml", FieldStatus.Match,
                "Custom manifest present",
                manifestPath,
                manifestPath);

            XmlDocument doc;
            try { doc = new XmlDocument(); doc.Load(manifestPath); }
            catch (System.Exception ex)
            {
                AddRow(result, SUB_MANIFEST, "Manifest parse", FieldStatus.Mismatch,
                    "AndroidManifest.xml is malformed XML",
                    ex.Message,
                    manifestPath);
                return;
            }

            var permissions = new Dictionary<string, XmlNode>();
            foreach (XmlNode n in doc.GetElementsByTagName("uses-permission"))
            {
                string name = n.Attributes?["android:name"]?.Value;
                if (!string.IsNullOrEmpty(name)) permissions[name] = n;
            }

            var checks = new (string perm, FieldStatus sev, string label, string why)[]
            {
                ("android.permission.INTERNET", FieldStatus.Mismatch, "INTERNET",
                    "Required for Adjust to communicate with its servers. Without this, NO data reaches Adjust."),
                ("android.permission.ACCESS_NETWORK_STATE", FieldStatus.Missing, "ACCESS_NETWORK_STATE",
                    "Required to read network type. Without it, retry logic and reporting are degraded."),
                ("com.google.android.gms.permission.AD_ID", FieldStatus.Missing, "AD_ID (Android 12+)",
                    "Required to read Google Advertising ID on Android 12+ (API 31+). Without it, gps_adid is empty and attribution is degraded.")
            };

            foreach (var c in checks)
            {
                if (permissions.TryGetValue(c.perm, out XmlNode node))
                {
                    string toolsNode = node.Attributes?["tools:node"]?.Value;
                    if (toolsNode == "remove")
                    {
                        AddRow(result, SUB_MANIFEST, c.label, FieldStatus.Mismatch,
                            $"Permission is REMOVED via tools:node=\"remove\"",
                            $"This silently disables attribution. {c.why}",
                            manifestPath);
                    }
                    else
                    {
                        AddRow(result, SUB_MANIFEST, c.label, FieldStatus.Match,
                            "Permission declared",
                            c.perm,
                            manifestPath);
                    }
                }
                else
                {
                    string snippet = $"<uses-permission android:name=\"{c.perm}\" />";
                    AddRowWithFix(result, SUB_MANIFEST, c.label, c.sev,
                        "Permission MISSING",
                        c.why + "\n\nAdd this line inside <manifest> in AndroidManifest.xml.",
                        manifestPath, snippet);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  3. EDM4U DEPENDENCIES
        // ══════════════════════════════════════════════════════════════════════════

        private static readonly (string spec, FieldStatus sev, string label, string why, string version)[] DependencyChecks =
        {
            ("com.google.android.gms:play-services-ads-identifier", FieldStatus.Missing,
                "play-services-ads-identifier",
                "Reads gps_adid (Google Advertising ID) from Google Play Services on Android. NOT related to push/messaging — 'gms' here = Google MOBILE Services. Required if shipping on Google Play with paid attribution. Optional for non-Google stores or COPPA/children apps.",
                "18.2.0"),
            ("com.google.android.gms:play-services-appset", FieldStatus.Missing,
                "play-services-appset",
                "Optional. Reads the App Set ID — shared identifier across apps from the same developer. Useful for cross-app analytics.",
                "16.1.0"),
            ("com.android.installreferrer:installreferrer", FieldStatus.Missing,
                "Install Referrer",
                "Required for the Google Play Referrer API — Adjust uses this to attribute install source. Required if shipping on Google Play with paid acquisition.",
                "2.2")
        };

        private static void ScanDependencies(ScanResult result, string dataPath)
        {
            var depFiles = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(dataPath, "*Dependencies.xml", SearchOption.AllDirectories))
                    depFiles.Add(f.Replace('\\', '/'));
            }
            catch { }

            if (depFiles.Count == 0)
            {
                string snippet =
                    "<dependencies>\n" +
                    "  <androidPackages>\n" +
                    "    <androidPackage spec=\"com.google.android.gms:play-services-ads-identifier:18.2.0\" />\n" +
                    "    <androidPackage spec=\"com.google.android.gms:play-services-appset:16.1.0\" />\n" +
                    "    <androidPackage spec=\"com.android.installreferrer:installreferrer:2.2\" />\n" +
                    "  </androidPackages>\n" +
                    "</dependencies>";

                AddRowWithFix(result, SUB_DEPS, "Dependencies.xml", FieldStatus.Missing,
                    "No *Dependencies.xml file found anywhere in Assets/",
                    "EDM4U (External Dependency Manager for Unity) is required for Adjust v5+ to resolve native Android SDKs.\n\n" +
                    "1. Install EDM4U: https://github.com/googlesamples/unity-jar-resolver/releases\n" +
                    "2. Create Assets/Adjust/Editor/AdjustDependencies.xml\n" +
                    "3. Paste the snippet on the right\n" +
                    "4. Menu: Assets → External Dependency Manager → Android Resolver → Force Resolve",
                    "", snippet);
                return;
            }

            AddRow(result, SUB_DEPS, "Dependencies.xml", FieldStatus.Match,
                $"Found {depFiles.Count} dependency file(s)",
                string.Join("\n", depFiles.Take(3)),
                depFiles[0]);

            var foundSpecs = new Dictionary<string, (string version, string file)>();
            foreach (var f in depFiles)
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(f);
                    foreach (XmlNode n in doc.GetElementsByTagName("androidPackage"))
                    {
                        string spec = n.Attributes?["spec"]?.Value;
                        if (string.IsNullOrEmpty(spec)) continue;
                        int lastColon = spec.LastIndexOf(':');
                        if (lastColon < 0) continue;
                        string ga = spec.Substring(0, lastColon);
                        string ver = spec.Substring(lastColon + 1);
                        foundSpecs[ga] = (ver, f);
                    }
                }
                catch { /* ignore individual dep file parse failures */ }
            }

            string targetDepFile = depFiles.FirstOrDefault(f => f.Contains("Adjust")) ?? depFiles[0];

            foreach (var exp in DependencyChecks)
            {
                if (foundSpecs.TryGetValue(exp.spec, out var info))
                {
                    AddRow(result, SUB_DEPS, exp.label, FieldStatus.Match,
                        $"{exp.spec}:{info.version}",
                        $"Declared in {info.file}",
                        info.file);
                }
                else
                {
                    string snippet = $"<androidPackage spec=\"{exp.spec}:{exp.version}\" />";
                    AddRowWithFix(result, SUB_DEPS, exp.label, exp.sev,
                        $"{exp.spec} NOT declared",
                        exp.why + "\n\nFIX:\n" +
                        $"1. Open {targetDepFile}\n" +
                        "2. Inside <androidPackages>, add the snippet on the right\n" +
                        "3. Menu: Assets → External Dependency Manager → Android Resolver → Force Resolve",
                        targetDepFile, snippet);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  4. iOS BUILD SETTINGS
        // ══════════════════════════════════════════════════════════════════════════

        private static void ScanIOS(ScanResult result, string dataPath)
        {
            string settingsPath = null;
            try
            {
                foreach (var f in Directory.GetFiles(dataPath, "AdjustSettings.asset", SearchOption.AllDirectories))
                { settingsPath = f.Replace('\\', '/'); break; }
            }
            catch { }

            if (settingsPath == null)
            {
                AddRow(result, SUB_IOS, "AdjustSettings.asset", FieldStatus.Missing,
                    "Not found in project",
                    "Post-build iOS framework wiring is configured in this ScriptableObject. Generated when the Adjust SDK is first imported. If missing, iOS builds may not link AdServices / AppTrackingTransparency frameworks.",
                    "");
                return;
            }

            AddRow(result, SUB_IOS, "AdjustSettings.asset", FieldStatus.Match,
                "Found", settingsPath, settingsPath);

            string content;
            try { content = File.ReadAllText(settingsPath); }
            catch { return; }

            CheckBoolField(result, settingsPath, content, "iOSFrameworkAdSupport", "AdSupport.framework",
                "Required to read IDFA. Disable only if you have a strict reason — attribution accuracy will drop.");
            CheckBoolField(result, settingsPath, content, "iOSFrameworkAdServices", "AdServices.framework",
                "Required for Apple Search Ads measurement.");
            CheckBoolField(result, settingsPath, content, "iOSFrameworkAppTrackingTransparency", "AppTrackingTransparency.framework",
                "Required to show ATT consent dialog and obtain IDFA on iOS 14.5+. Without this, IDFA is nil on modern iOS.");
            CheckBoolField(result, settingsPath, content, "iOSFrameworkStoreKit", "StoreKit.framework",
                "Required for SKAdNetwork postback delivery.");

            var utdRx = new Regex(@"^\s*iOSUserTrackingUsageDescription:\s*(.*)$", RegexOptions.Multiline);
            var utdMatch = utdRx.Match(content);
            bool attEnabled = ReadBoolField(content, "iOSFrameworkAppTrackingTransparency");
            if (utdMatch.Success)
            {
                string desc = utdMatch.Groups[1].Value.Trim().Trim('"');
                if (string.IsNullOrEmpty(desc))
                {
                    if (attEnabled)
                    {
                        AddRow(result, SUB_IOS, "User Tracking Description", FieldStatus.Mismatch,
                            "EMPTY — App Store will REJECT the build",
                            "ATT framework is enabled but iOSUserTrackingUsageDescription is empty. Fill in a clear, user-facing reason for tracking in the Adjust prefab Post-Build inspector (or AdjustSettings.asset).",
                            settingsPath);
                    }
                    else
                    {
                        AddRow(result, SUB_IOS, "User Tracking Description", FieldStatus.Match,
                            "Empty (acceptable — ATT not enabled)",
                            "", settingsPath);
                    }
                }
                else
                {
                    AddRow(result, SUB_IOS, "User Tracking Description", FieldStatus.Match,
                        $"\"{desc}\"", "", settingsPath);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  5. AD REVENUE
        // ══════════════════════════════════════════════════════════════════════════

        private static void ScanAdRevenue(ScanResult result, string dataPath)
        {
            var allCs = new List<string>();
            try
            {
                foreach (var p in Directory.GetFiles(dataPath, "*.cs", SearchOption.AllDirectories))
                {
                    string norm = p.Replace('\\', '/');
                    if (norm.Contains("/Adjust/")) continue;
                    allCs.Add(norm);
                }
            }
            catch { return; }

            var sites = new List<(string file, int line, string source)>();
            var adRevenueRx = new Regex(
                @"new\s+AdjustAdRevenue\s*\(\s*""?([A-Za-z_][\w\-]*)""?\s*\)|Adjust\.TrackAdRevenue\s*\(");

            foreach (var file in allCs)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; }
                string content = string.Join("\n", lines);
                foreach (Match m in adRevenueRx.Matches(content))
                {
                    int ln = GetLineNumber(content, m.Index);
                    if (IsCodeNoise(lines, ln, m.Value)) continue;
                    string src = m.Groups[1].Success ? m.Groups[1].Value : "(unknown)";
                    sites.Add((file, ln, src));
                }
            }

            if (sites.Count == 0)
            {
                AddRow(result, SUB_ADREVENUE, "TrackAdRevenue calls", FieldStatus.Match,
                    "No Adjust ad revenue calls detected in code",
                    "If S2S ad revenue is enabled on the Adjust dashboard for your mediation network, that handles tracking server-side. Otherwise no ad revenue is being tracked.",
                    "");
                return;
            }

            string locations = string.Join("\n", sites.Select(s => $"  • {RelPath(s.file)}:{s.line} (source: {s.source})"));
            AddRow(result, SUB_ADREVENUE, "TrackAdRevenue calls", FieldStatus.Mismatch,
                $"{sites.Count} Adjust ad revenue call site(s) detected",
                "RISK: If your Adjust dashboard ALSO has S2S ad revenue enabled for the same mediation source, this WILL cause double-counting (one event tracked twice). Disable one side.\n\n" +
                "Locations:\n" + locations,
                sites[0].file, sites[0].line);
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════════

        private static void AddRow(ScanResult result, string subTab, string fieldName, FieldStatus status,
                                    string projectValue, string detail, string assetPath, int line = 0)
        {
            result.AllFields.Add(new FieldResult
            {
                Tab = TAB_ADJUST,
                SubTab = subTab,
                Section = subTab,
                FieldName = fieldName,
                Platform = "",
                ExpectedValue = detail,
                ProjectValue = projectValue,
                Status = status,
                AssetPath = assetPath,
                YamlKey = "integration",
                LineNumber = line
            });
        }

        private static void AddRowWithFix(ScanResult result, string subTab, string fieldName, FieldStatus status,
                                           string projectValue, string detail, string assetPath, string fixSnippet)
        {
            result.AllFields.Add(new FieldResult
            {
                Tab = TAB_ADJUST,
                SubTab = subTab,
                Section = subTab,
                FieldName = fieldName,
                Platform = "",
                ExpectedValue = detail,
                ProjectValue = projectValue,
                Status = status,
                AssetPath = assetPath,
                YamlKey = "integration",
                FixSnippet = fixSnippet
            });
        }

        private static string LocateAdjustCs()
        {
            var guids = AssetDatabase.FindAssets("Adjust t:Script");
            string fallback = null;
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(p)) continue;
                if (!p.EndsWith("/Adjust.cs", System.StringComparison.Ordinal)) continue;
                if (p.Contains("/AdjustValidator/")) continue;
                string folder = p.Substring(0, p.LastIndexOf('/'));
                if (File.Exists(folder + "/AdjustConfig.cs")) return p;
                if (fallback == null) fallback = p;
            }
            return fallback;
        }

        private static System.Type FindAdjustType()
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.Name == "Adjust" && typeof(MonoBehaviour).IsAssignableFrom(t))
                        return t;
                }
            }
            return null;
        }

        private static T GetFieldValueMulti<T>(object instance, System.Type type, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                var f = FindField(type, name);
                if (f == null) continue;
                try
                {
                    object v = f.GetValue(instance);
                    if (v == null) continue;
                    if (v is T tv) return tv;
                    return (T)System.Convert.ChangeType(v, typeof(T));
                }
                catch { }
            }
            return default(T);
        }

        private static int GetEnumOrInt(object instance, System.Type type, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                var f = FindField(type, name);
                if (f == null) continue;
                try
                {
                    object v = f.GetValue(instance);
                    if (v == null) continue;
                    if (v is int iv) return iv;
                    if (v.GetType().IsEnum) return System.Convert.ToInt32(v);
                    if (int.TryParse(v.ToString(), out int p)) return p;
                }
                catch { }
            }
            return -1;
        }

        private static FieldInfo FindField(System.Type type, string fieldName)
        {
            var t = type;
            while (t != null && t != typeof(object))
            {
                var f = t.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static List<(string file, int line)> FindInitSdkCallSites(string dataPath)
        {
            var sites = new List<(string, int)>();
            string[] files;
            try { files = Directory.GetFiles(dataPath, "*.cs", SearchOption.AllDirectories); }
            catch { return sites; }

            var rx = new Regex(@"Adjust\.InitSdk\s*\(");
            foreach (var f in files)
            {
                string norm = f.Replace('\\', '/');
                if (norm.Contains("/Adjust/")) continue; // skip SDK folder itself
                string[] lines;
                try { lines = File.ReadAllLines(f); }
                catch { continue; }
                string content = string.Join("\n", lines);
                foreach (Match m in rx.Matches(content))
                {
                    int ln = GetLineNumber(content, m.Index);
                    if (IsCodeNoise(lines, ln, m.Value)) continue;
                    sites.Add((norm, ln));
                }
            }
            return sites;
        }

        private static int GetLineNumber(string content, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < content.Length; i++)
                if (content[i] == '\n') line++;
            return line;
        }

        private static bool IsCodeNoise(string[] lines, int lineNumber1Based, string matchText)
        {
            if (lineNumber1Based < 1 || lineNumber1Based > lines.Length) return false;
            string line = lines[lineNumber1Based - 1];
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//")) return true;
            if (trimmed.StartsWith("///")) return true;

            int blockDepth = 0;
            for (int i = 0; i < lineNumber1Based - 1 && i < lines.Length; i++)
            {
                int open = CountOccurrences(lines[i], "/*");
                int close = CountOccurrences(lines[i], "*/");
                blockDepth += open - close;
            }
            if (blockDepth > 0) return true;

            int pos = line.IndexOf(matchText, System.StringComparison.Ordinal);
            if (pos >= 0)
            {
                int quoteCount = 0;
                for (int i = 0; i < pos; i++)
                {
                    if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) quoteCount++;
                }
                if (quoteCount % 2 == 1) return true;
            }
            return false;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.Ordinal)) != -1)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        private static string RelPath(string p) => p.Replace('\\', '/');

        private static bool ReadBoolField(string content, string field)
        {
            var rx = new Regex(@"^\s*" + Regex.Escape(field) + @":\s*(\d+)\s*$", RegexOptions.Multiline);
            var m = rx.Match(content);
            return m.Success && m.Groups[1].Value == "1";
        }

        private static void CheckBoolField(ScanResult result, string path, string content,
                                            string field, string label, string why)
        {
            var rx = new Regex(@"^\s*" + Regex.Escape(field) + @":\s*(\d+)\s*$", RegexOptions.Multiline);
            var m = rx.Match(content);
            if (!m.Success)
            {
                AddRow(result, SUB_IOS, label, FieldStatus.Missing,
                    "Setting not present in AdjustSettings.asset", "", path);
                return;
            }
            if (m.Groups[1].Value == "1")
                AddRow(result, SUB_IOS, label, FieldStatus.Match, "Enabled", "", path);
            else
                AddRow(result, SUB_IOS, label, FieldStatus.Mismatch, "Disabled", why, path);
        }
    }
}
