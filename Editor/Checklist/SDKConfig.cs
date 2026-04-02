// SDKConfig.cs
// Detects whether this project uses the GD SDK by checking for SDKConfiguration.asset.
// If GD SDK present  → scan everything automatically, no setup needed.
// If GD SDK absent   → show setup screen so external dev picks which SDKs they have.

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
namespace GDChecklist
{
    public static class SDKConfig
    {
        // ── GD SDK detection ──────────────────────────────────────────────────────
        // SDKConfiguration.asset is unique to your SDK — always at Assets/Configurations/
        private const string GD_SDK_ASSET = "SDKConfiguration.asset";
        private const string GD_CONFIG_PATH = "Configurations"; // folder name under Assets

        public static bool IsGDSDKPresent()
        {
            string path = Path.Combine(Application.dataPath, GD_CONFIG_PATH, GD_SDK_ASSET);
            return File.Exists(path);
        }

        // ── Read which analytics networks are enabled FROM the SOs themselves ─────
        // Instead of asking the developer, we read the NetworkSO files directly.
        // EnableInitialization: 1 = this network is active in the project.

        public static bool IsNetworkEnabled(string networkSOName)
        {
            string networkDir = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "Analytics Networks");
            string file = Path.Combine(networkDir, networkSOName + ".asset");
            if (!File.Exists(file)) return false;

            string content = File.ReadAllText(file);
            var match = Regex.Match(content, @"EnableInitialization:\s*(\d)");
            return match.Success && match.Groups[1].Value == "1";
        }

        // ── Per-SDK presence checks based on actual asset files ───────────────────

        // AppLovin — always at Assets/MaxSdk/Resources/AppLovinSettings.asset
        public static bool HasAppLovin()
        {
            string path = Path.Combine(Application.dataPath, "MaxSdk", "Resources", "AppLovinSettings.asset");
            return File.Exists(path);
        }

        // Metica — check GD Configurations path first, then search project-wide
        public static bool HasMetica()
        {
            string gdPath = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "MeticaSettings.asset");
            if (File.Exists(gdPath)) return true;
            return FindAssetAnywhere("MeticaSettings.asset") != null;
        }

        // Adjust — check GD Configurations path first, then search project-wide
        public static bool HasAdjust()
        {
            string gdPath = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AdjustToken.asset");
            if (File.Exists(gdPath)) return true;
            return FindAssetAnywhere("AdjustToken.asset") != null;
        }

        // AppMetrica — check GD Configurations path first, then search project-wide
        public static bool HasAppMetrica()
        {
            string gdPath = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AppMetricaSettings.asset");
            if (File.Exists(gdPath)) return true;
            return FindAssetAnywhere("AppMetricaSettings.asset") != null
                || FindAssetAnywhere("YandexMetricaSettings.asset") != null;
        }

        // Firebase — check NetworkSO OR presence of google-services.json
        public static bool HasFirebase()
        {
            if (IsNetworkEnabled("FirebaseNetworkSO")) return true;
            return FindFileAnywhere("google-services.json") != null
                || FindFileAnywhere("GoogleService-Info.plist") != null;
        }

        // AdUnits — check GD Configurations path first, then search project-wide
        public static bool HasAdUnits()
        {
            string gdPath = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AdUnitsSettings.asset");
            if (File.Exists(gdPath)) return true;
            return FindAssetAnywhere("AdUnitsSettings.asset") != null;
        }

        // ── Fast variants — File.Exists only on known GD paths, no directory scan ──
        // Used on button tap for instant response. Falls back to true if path unknown.

        public static bool HasAppLovinFast()
            => File.Exists(Path.Combine(Application.dataPath, "MaxSdk", "Resources", "AppLovinSettings.asset"));

        public static bool HasMeticaFast()
            => File.Exists(Path.Combine(Application.dataPath, GD_CONFIG_PATH, "MeticaSettings.asset"));

        public static bool HasAdjustFast()
            => File.Exists(Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AdjustToken.asset"));

        public static bool HasAppMetricaFast()
            => File.Exists(Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AppMetricaSettings.asset"))
            || File.Exists(Path.Combine(Application.dataPath, GD_CONFIG_PATH, "YandexMetricaSettings.asset"));

        public static bool HasFirebaseFast()
        {
            if (IsNetworkEnabled("FirebaseNetworkSO")) return true;
            // Check only the two most common locations
            return File.Exists(Path.Combine(Application.dataPath, "google-services.json"))
                || File.Exists(Path.Combine(Application.dataPath, "GoogleService-Info.plist"))
                || File.Exists(Path.Combine(Application.dataPath, "StreamingAssets", "google-services.json"));
        }

        public static bool HasAdUnitsFast()
            => File.Exists(Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AdUnitsSettings.asset"));

        // ── Search helpers ────────────────────────────────────────────────────────
        private static string FindAssetAnywhere(string fileName)
        {
            try
            {
                return Directory.GetFiles(Application.dataPath, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault()?.Replace('\\', '/');
            }
            catch { return null; }
        }

        private static string FindFileAnywhere(string fileName)
        {
            try
            {
                return Directory.GetFiles(Application.dataPath, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault()?.Replace('\\', '/');
            }
            catch { return null; }
        }

        // ── Manual override — for external devs without GD SDK ────────────────────
        // Stored in EditorPrefs when external dev completes setup screen.

        private const string PREFIX         = "GDChecklist_SDK_";
        private const string SETUP_DONE_KEY = "GDChecklist_SetupDone";

        public static bool ManualAppLovin
        {
            get => EditorPrefs.GetBool(PREFIX + "AppLovin",   false);
            set => EditorPrefs.SetBool(PREFIX + "AppLovin",   value);
        }
        public static bool ManualMetica
        {
            get => EditorPrefs.GetBool(PREFIX + "Metica",     false);
            set => EditorPrefs.SetBool(PREFIX + "Metica",     value);
        }
        public static bool ManualAdjust
        {
            get => EditorPrefs.GetBool(PREFIX + "Adjust",     false);
            set => EditorPrefs.SetBool(PREFIX + "Adjust",     value);
        }
        public static bool ManualAppMetrica
        {
            get => EditorPrefs.GetBool(PREFIX + "AppMetrica", false);
            set => EditorPrefs.SetBool(PREFIX + "AppMetrica", value);
        }
        public static bool ManualFirebase
        {
            get => EditorPrefs.GetBool(PREFIX + "Firebase",   false);
            set => EditorPrefs.SetBool(PREFIX + "Firebase",   value);
        }
        public static bool ManualAdUnits
        {
            get => EditorPrefs.GetBool(PREFIX + "AdUnits",    false);
            set => EditorPrefs.SetBool(PREFIX + "AdUnits",    value);
        }

        public static bool IsSetupDone
        {
            get => EditorPrefs.GetBool(SETUP_DONE_KEY, false);
            set => EditorPrefs.SetBool(SETUP_DONE_KEY, value);
        }

        // Whether developer confirmed they use GD SDK
        private const string GD_SDK_KEY = "GDChecklist_IsGDSDK";
        public static bool IsGDSDK
        {
            get => EditorPrefs.GetBool(GD_SDK_KEY, false);
            set => EditorPrefs.SetBool(GD_SDK_KEY, value);
        }

        public static void ResetSetup()
        {
            _cachedConfig = null; // invalidate cache
            EditorPrefs.DeleteKey(SETUP_DONE_KEY);
            EditorPrefs.DeleteKey(GD_SDK_KEY);
            EditorPrefs.DeleteKey("GDChecklist_Version");
            EditorPrefs.DeleteKey(PREFIX + "AppLovin");
            EditorPrefs.DeleteKey(PREFIX + "Metica");
            EditorPrefs.DeleteKey(PREFIX + "Adjust");
            EditorPrefs.DeleteKey(PREFIX + "AppMetrica");
            EditorPrefs.DeleteKey(PREFIX + "Firebase");
            EditorPrefs.DeleteKey(PREFIX + "AdUnits");
        }

        // ── Build the final scan config ────────────────────────────────────────────
        // GD SDK present  → read directly from asset files + NetworkSOs
        // GD SDK absent   → use manual selections from setup screen

        // Cached config — rebuilt only when setup changes, not every frame
        private static SDKScanConfig _cachedConfig    = null;
        private static volatile bool _cacheBuilding   = false; // prevent double-build on background thread

        public static void InvalidateConfigCache()
        {
            _cachedConfig  = null;
            _cacheBuilding = false;
        }

        // Called from background thread on window open — pre-populates _cachedConfig
        // Uses only System.IO (thread-safe). Never calls UnityEngine API.
        public static void PrebuildConfigCache()
        {
            if (_cachedConfig != null || _cacheBuilding) return;
            _cacheBuilding = true;
            try
            {
                if (IsGDSDK)
                {
                    _cachedConfig = new SDKScanConfig
                    {
                        AppLovin   = HasAppLovin(),   Metica     = HasMetica(),
                        Adjust     = HasAdjust(),     AppMetrica = HasAppMetrica(),
                        Firebase   = HasFirebase(),   AdUnits    = HasAdUnits(),
                        IsGDSDK    = true
                    };
                }
                else
                {
                    // EditorPrefs is not safe off main thread — just mark as non-GD
                    // BuildScanConfig() on main thread will fill the real values instantly
                    _cachedConfig = new SDKScanConfig
                    {
                        AppLovin   = ManualAppLovin,   Metica     = ManualMetica,
                        Adjust     = ManualAdjust,     AppMetrica = ManualAppMetrica,
                        Firebase   = ManualFirebase,   AdUnits    = ManualAdUnits,
                        IsGDSDK    = false
                    };
                }
            }
            catch { _cachedConfig = null; }
            finally { _cacheBuilding = false; }
        }

        public static SDKScanConfig BuildScanConfig()
        {
            if (_cachedConfig != null) return _cachedConfig;

            if (IsGDSDK)
            {
                _cachedConfig = new SDKScanConfig
                {
                    AppLovin   = HasAppLovin(),
                    Metica     = HasMetica(),
                    Adjust     = HasAdjust(),
                    AppMetrica = HasAppMetrica(),
                    Firebase   = HasFirebase(),
                    AdUnits    = HasAdUnits(),
                    IsGDSDK    = true
                };
            }
            else
            {
                _cachedConfig = new SDKScanConfig
                {
                    AppLovin   = ManualAppLovin,   Metica     = ManualMetica,
                    Adjust     = ManualAdjust,     AppMetrica = ManualAppMetrica,
                    Firebase   = ManualFirebase,   AdUnits    = ManualAdUnits,
                    IsGDSDK    = false
                };
            }
            return _cachedConfig;
        }

        // Same as BuildScanConfig but reports progress — call ONLY from RunScan, never from OnGUI
        public static SDKScanConfig BuildScanConfigWithProgress()
        {
            InvalidateConfigCache(); // always rebuild fresh during an explicit scan

            if (IsGDSDK)
            {
                ScanProgress.Report("Detecting AppLovin…",   0.08f); bool al  = HasAppLovin();
                ScanProgress.Report("Detecting Metica…",     0.14f); bool mt  = HasMetica();
                ScanProgress.Report("Detecting Adjust…",     0.20f); bool adj = HasAdjust();
                ScanProgress.Report("Detecting AppMetrica…", 0.26f); bool am  = HasAppMetrica();
                ScanProgress.Report("Detecting Firebase…",   0.32f); bool fb  = HasFirebase();
                ScanProgress.Report("Detecting Ad Units…",   0.38f); bool au  = HasAdUnits();

                _cachedConfig = new SDKScanConfig
                {
                    AppLovin   = al,  Metica     = mt,  Adjust     = adj,
                    AppMetrica = am,  Firebase   = fb,  AdUnits    = au,
                    IsGDSDK    = true
                };
            }
            else
            {
                _cachedConfig = new SDKScanConfig
                {
                    AppLovin   = ManualAppLovin,   Metica     = ManualMetica,
                    Adjust     = ManualAdjust,     AppMetrica = ManualAppMetrica,
                    Firebase   = ManualFirebase,   AdUnits    = ManualAdUnits,
                    IsGDSDK    = false
                };
            }
            return _cachedConfig;
        }
    }
}
