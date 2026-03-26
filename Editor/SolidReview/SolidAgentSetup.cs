// SolidAgentSetup.cs
// No external DLLs needed — SOLID Agent uses built-in Unity APIs only.

namespace SolidAgent
{
    public static class SolidAgentSetup
    {
        // Always ready — no DLL setup required
        public static bool AreDLLsReady() => true;
        public static void TrySetupManual(string path) { }
    }
}
