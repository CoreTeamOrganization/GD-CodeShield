// ChecklistTelemetry.cs
// Fires a silent telemetry event after a Checklist scan completes, capturing
// the installed SDK versions detected by SDKVersionDetector. Powers the
// per-studio SDK version dashboard.
//
// Endpoint: same Apps Script webhook as SolidTelemetry + Contact Support,
//           distinguished by action=sdk_versions.
// Trigger:  called from ChecklistWindow.RunScan after BuildScanConfig completes.
// Behavior: fire-and-forget. Never blocks UI. Swallows network errors.

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GDChecklist
{
    internal static class ChecklistTelemetry
    {
        // Same webhook as SolidTelemetry + Contact Support
        private const string WEBHOOK_URL =
            "https://script.google.com/macros/s/AKfycbzn7mlBIZByoS39ICiYvA7RLvnCvE2uc9LQd-VnM6Ytprn5jOqYq0niTg9IjsIHRYCM/exec";

        public static void ReportChecklistScanCompleted()
        {
            try
            {
                // Pull all SDK versions (cached by SDKVersionDetector, so this is fast)
                var sdks = new Dictionary<string, SDKVersion>
                {
                    { "applovin",   SDKVersionDetector.GetVersionForTab(0) },
                    { "metica",     SDKVersionDetector.GetVersionForTab(1) },
                    { "adjust",     SDKVersionDetector.GetVersionForTab(2) },
                    { "appmetrica", SDKVersionDetector.GetVersionForTab(3) },
                    { "firebase",   SDKVersionDetector.GetVersionForTab(4) },
                };

                // GD Monetization SDK — only included when actually installed
                var gdMon = SDKVersionDetector.GetVersionForTab(-1);

                string game     = SafeString(PlayerSettings.productName);
                string bundleId = SafeString(Application.identifier);
                string unityVer = SafeString(Application.unityVersion);
                string platform = SafeString(Application.platform.ToString());
                string ts       = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                var sb = new StringBuilder();
                sb.Append("action=sdk_versions");
                sb.Append("&event=checklist_scan_completed");
                sb.Append("&gameName=");      sb.Append(UnityWebRequest.EscapeURL(game));
                sb.Append("&bundleId=");      sb.Append(UnityWebRequest.EscapeURL(bundleId));
                sb.Append("&unityVersion=");  sb.Append(UnityWebRequest.EscapeURL(unityVer));
                sb.Append("&platform=");      sb.Append(UnityWebRequest.EscapeURL(platform));
                sb.Append("&timestamp=");     sb.Append(UnityWebRequest.EscapeURL(ts));

                // GD Monetization SDK — empty when not installed (non-GD studios)
                sb.Append("&gdMonetizationVersion=");
                sb.Append(UnityWebRequest.EscapeURL(gdMon?.Version ?? ""));

                // Per-SDK: headline version + full modules JSON
                foreach (var pair in sdks)
                {
                    string key = pair.Key;
                    var sdk = pair.Value;
                    string headlineVer = sdk?.Version ?? "";
                    string modulesJson = BuildModulesJson(sdk);

                    sb.Append("&");
                    sb.Append(key);
                    sb.Append("Version=");
                    sb.Append(UnityWebRequest.EscapeURL(headlineVer));

                    sb.Append("&");
                    sb.Append(key);
                    sb.Append("Modules=");
                    sb.Append(UnityWebRequest.EscapeURL(modulesJson));
                }

                byte[] raw = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                var req = new UnityWebRequest(WEBHOOK_URL, "POST");
                req.uploadHandler   = new UploadHandlerRaw(raw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                req.timeout = 10;
                var op = req.SendWebRequest();

                op.completed += _ =>
                {
                    try { req.Dispose(); } catch { }
                };
            }
            catch
            {
                // Silently swallow — telemetry must never disrupt user workflow or surface in Console
            }
        }

        // Builds a JSON array of [{label, version}, ...] for the modules list
        // Returns "[]" if no modules. Minimal JSON encoder — no quotes/backslashes in labels expected.
        private static string BuildModulesJson(SDKVersion sdk)
        {
            if (sdk?.Modules == null || sdk.Modules.Count == 0) return "[]";
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < sdk.Modules.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var m = sdk.Modules[i];
                sb.Append("{\"label\":\"");
                sb.Append(EscapeJson(m.label));
                sb.Append("\",\"version\":\"");
                sb.Append(EscapeJson(m.version));
                sb.Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string SafeString(string s) => string.IsNullOrEmpty(s) ? "(unknown)" : s;
    }
}