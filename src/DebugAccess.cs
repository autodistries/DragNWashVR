using System.IO;

namespace WalkNWash.VRCompanion
{
    internal static class DebugAccess
    {
        private static bool initialized;
        internal static bool Enabled { get; private set; }

        internal static void Initialize(string assemblyLocation)
        {
            if (initialized) return;
            Enabled = MarkerPresent(assemblyLocation);
            initialized = true;
        }

        internal static bool MarkerPresent(string assemblyLocation)
        {
            if (string.IsNullOrEmpty(assemblyLocation)) return false;
            string directory = Path.GetDirectoryName(assemblyLocation);
            return !string.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory, ".debug"));
        }
    }
}
