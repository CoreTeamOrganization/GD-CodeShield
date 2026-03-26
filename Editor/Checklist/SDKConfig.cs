// SDKConfig.cs
// Detects whether this project uses the GD SDK by checking for SDKConfiguration.asset.
// If GD SDK present  → scan everything automatically, no setup needed.
// If GD SDK absent   → show setup screen so external dev picks which SDKs they have.

using System.IO;
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

        // Metica — MeticaSettings.asset in Configurations + MeticaNetworkSO enabled
        public static bool HasMetica()
        {
            string path = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "MeticaSettings.asset");
            return File.Exists(path) && IsNetworkEnabled("MeticaNetworkSO");
        }

        // Adjust — AdjustToken.asset in Configurations + AdjustNetworkSO enabled
        public static bool HasAdjust()
        {
            string path = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AdjustToken.asset");
            return File.Exists(path) && IsNetworkEnabled("AdjustNetworkSO");
        }

        // AppMetrica — AppMetricaSettings.asset + AppMetricaNetworkSO enabled
        public static bool HasAppMetrica()
        {
            string path = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AppMetricaSettings.asset");
            return File.Exists(path) && IsNetworkEnabled("AppMetricaNetworkSO");
        }

        // Firebase — FirebaseNetworkSO enabled + google-services.json present
        public static bool HasFirebase()
        {
            return IsNetworkEnabled("FirebaseNetworkSO");
        }

        // AdUnits — AdUnitsSettings.asset in Configurations
        public static bool HasAdUnits()
        {
            string path = Path.Combine(Application.dataPath, GD_CONFIG_PATH, "AdUnitsSettings.asset");
            return File.Exists(path);
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

        public static SDKScanConfig BuildScanConfig()
        {
            if (IsGDSDK)
            {
                // Developer confirmed GD SDK — auto-detect from actual SO files
                return new SDKScanConfig
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
                // External developer — use what they selected on setup screen
                return new SDKScanConfig
                {
                    AppLovin   = ManualAppLovin,
                    Metica     = ManualMetica,
                    Adjust     = ManualAdjust,
                    AppMetrica = ManualAppMetrica,
                    Firebase   = ManualFirebase,
                    AdUnits    = ManualAdUnits,
                    IsGDSDK    = false
                };
            }
        }
    }
}
