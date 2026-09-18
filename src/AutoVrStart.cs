using System.Reflection;

namespace WalkNWash.VRCompanion
{
    internal sealed class AutoVrStart
    {
        private bool attempted;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal bool Tick(object manager, bool enabled)
        {
            if (attempted || !enabled || manager == null) return false;
            var type = manager.GetType();
            if (!(bool)type.GetField("_managerInitialized", Fields).GetValue(manager)) return false;
            // Consume before calling native initialization: failures remain retryable
            // by F11, and later user toggles must never be undone automatically.
            attempted = true;
            var priorAttempt = type.GetField("_hasVrBeenAttemptedByUser", Fields);
            if ((bool)priorAttempt.GetValue(manager)) return false;
            if ((bool)type.GetField("_isUserSafeModeActive", Fields).GetValue(manager))
                type.GetMethod("ToggleUserSafeMode", Fields).Invoke(manager, null);
            else
                priorAttempt.SetValue(manager, true); // Let the normal manager Update initialize VR.
            return true;
        }
    }
}
