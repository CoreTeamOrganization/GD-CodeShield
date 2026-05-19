// SolidTelemetry.cs
// Fires a silent "scan completed" telemetry event to the GD CodeShield webhook
// after a SOLID scan finishes. Used to power a dashboard tracking tool usage and
// code-health trends across studios.
//
// Identifier:   PlayerSettings.productName + Application.identifier (no personal data)
// Endpoint:     Same Apps Script webhook as Contact Support, distinguished by action=telemetry
// Trigger:      Called from SolidAgentWindow right after RatingEngine.GenerateReport
// Behaviour:    Fire-and-forget. Never blocks UI. Silently swallows network failures.

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace SolidAgent
{
    internal static class SolidTelemetry
    {
        // Same webhook as Contact Support — Apps Script branches on 'action' parameter
        private const string WEBHOOK_URL =
            "https://script.google.com/macros/s/AKfycbzn7mlBIZByoS39ICiYvA7RLvnCvE2uc9LQd-VnM6Ytprn5jOqYq0niTg9IjsIHRYCM/exec";

        public static void ReportScanCompleted(SolidReport report)
        {
            if (report == null) return;

            try
            {
                // Extract per-principle scores. Order is fixed: SRP, OCP, LSP, ISP.
                int sScore = ScoreFor(report, "SRP");
                int oScore = ScoreFor(report, "OCP");
                int lScore = ScoreFor(report, "LSP");
                int iScore = ScoreFor(report, "ISP");

                string game     = SafeString(PlayerSettings.productName);
                string bundleId = SafeString(Application.identifier);
                string unityVer = SafeString(Application.unityVersion);
                string platform = SafeString(Application.platform.ToString());
                string ts       = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                string form =
                      "action=telemetry"
                    + "&event=solid_scan_completed"
                    + "&gameName="      + UnityWebRequest.EscapeURL(game)
                    + "&bundleId="      + UnityWebRequest.EscapeURL(bundleId)
                    + "&overallScore="  + report.OverallScore.ToString("F1")
                    + "&overallLabel="  + UnityWebRequest.EscapeURL(report.OverallLabel ?? "")
                    + "&srpScore="      + sScore
                    + "&ocpScore="      + oScore
                    + "&lspScore="      + lScore
                    + "&ispScore="      + iScore
                    + "&totalFiles="    + report.TotalFiles
                    + "&totalViolations=" + report.TotalViolations
                    + "&unityVersion="  + UnityWebRequest.EscapeURL(unityVer)
                    + "&platform="      + UnityWebRequest.EscapeURL(platform)
                    + "&timestamp="     + UnityWebRequest.EscapeURL(ts);

                byte[] raw = System.Text.Encoding.UTF8.GetBytes(form);
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

        private static int ScoreFor(SolidReport report, string principle)
        {
            if (report.Ratings == null) return 0;
            var r = report.Ratings.FirstOrDefault(x => x.Principle.ToString() == principle);
            return r != null ? r.Score : 0;
        }

        private static string SafeString(string s) => string.IsNullOrEmpty(s) ? "(unknown)" : s;
    }
}