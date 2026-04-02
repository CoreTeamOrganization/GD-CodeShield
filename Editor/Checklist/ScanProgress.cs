// Editor/Checklist/ScanProgress.cs
// Unified progress reporting for GD Checklist scans.
// Shows BOTH:
//   - EditorUtility.DisplayProgressBar  — blocking modal bar across top of Unity (like compile)
//   - Progress.Start / Progress.Report  — spinning indicator bottom-right (like domain reload)

using UnityEditor;

namespace GDChecklist
{
    internal static class ScanProgress
    {
        private static int _progressId = -1;

        // Call once before any heavy work starts
        public static void Begin(string title)
        {
            // Spinning bottom-right indicator — same style as domain reload
            _progressId = Progress.Start(title, null, Progress.Options.Indefinite);

            // Blocking top bar — prevents accidental clicks during scan
            EditorUtility.DisplayProgressBar(title, "Starting…", 0f);
        }

        // Call at each step with a 0-1 progress value
        public static void Report(string step, float progress)
        {
            if (_progressId >= 0)
                Progress.Report(_progressId, progress, step);

            EditorUtility.DisplayProgressBar("GD Checklist", step, progress);
        }

        // Call when done — clears both indicators
        public static void End()
        {
            EditorUtility.ClearProgressBar();

            if (_progressId >= 0)
            {
                Progress.Finish(_progressId);
                _progressId = -1;
            }
        }

        // Convenience — use in a try/finally block
        // try { ScanProgress.Begin("…"); … ScanProgress.Report(…); }
        // finally { ScanProgress.End(); }
    }
}
