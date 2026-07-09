// UpdateChecker.cs
// Checks GitHub releases for a newer tag than the installed package version and
// exposes it so the Hub can show an "update available" banner.
//
// Rules (same spirit as telemetry):
//   - Fire-and-forget, silent on every failure — a dead network/API must never
//     surface an error or slow a window down.
//   - At most one request per 24h per machine (EditorPrefs-throttled).
//   - Banner is dismissible per version: dismissed 1.5.0 stays dismissed until 1.5.1.

using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GDCodeShield
{
    internal static class UpdateChecker
    {
        private const string RELEASES_LATEST_URL =
            "https://api.github.com/repos/CoreTeamOrganization/GD-CodeShield/releases/latest";

        private const string PREF_LAST_CHECK = "GDCodeShield_Update_LastCheckTicks";
        private const string PREF_LATEST     = "GDCodeShield_Update_LatestVersion";
        private const string PREF_DISMISSED  = "GDCodeShield_Update_Dismissed_"; // + version

        private const double CHECK_INTERVAL_HOURS = 24;

        /// <summary>Installed package version, or null when not running as a package.</summary>
        public static string CurrentVersion()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo
                    .FindForAssembly(typeof(UpdateChecker).Assembly);
                return info?.version;
            }
            catch { return null; }
        }

        /// <summary>True when a newer, not-yet-dismissed version exists on GitHub.</summary>
        public static bool UpdateAvailable(out string latestVersion)
        {
            latestVersion = EditorPrefs.GetString(PREF_LATEST, "");
            string current = CurrentVersion();
            if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(current)) return false;
            if (EditorPrefs.GetBool(PREF_DISMISSED + latestVersion, false)) return false;

            return Version.TryParse(latestVersion, out var latest)
                && Version.TryParse(current, out var cur)
                && latest > cur;
        }

        public static void Dismiss(string version)
        {
            if (!string.IsNullOrEmpty(version))
                EditorPrefs.SetBool(PREF_DISMISSED + version, true);
        }

        /// <summary>Kick off the daily check. Safe to call from every window OnEnable.</summary>
        public static void CheckAsync()
        {
            try
            {
                long lastTicks = long.TryParse(EditorPrefs.GetString(PREF_LAST_CHECK, "0"), out var t) ? t : 0;
                if ((DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc)).TotalHours < CHECK_INTERVAL_HOURS)
                    return;
                EditorPrefs.SetString(PREF_LAST_CHECK, DateTime.UtcNow.Ticks.ToString());

                var req = UnityWebRequest.Get(RELEASES_LATEST_URL);
                req.SetRequestHeader("User-Agent", "GD-CodeShield-Updater"); // GitHub API requires one
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 10;
                var op = req.SendWebRequest();

                op.completed += _ =>
                {
                    try
                    {
                        if (req.result == UnityWebRequest.Result.Success)
                        {
                            // {"tag_name":"v1.5.0", ...} — tag format per release workflow
                            var m = Regex.Match(req.downloadHandler.text, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                            if (m.Success)
                                EditorPrefs.SetString(PREF_LATEST, m.Groups[1].Value);
                        }
                    }
                    catch { }
                    finally { try { req.Dispose(); } catch { } }
                };
            }
            catch { /* silent — update check must never disrupt the editor */ }
        }
    }
}
