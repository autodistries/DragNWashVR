using System.Reflection;

namespace WalkNWash.VRCompanion
{
    // Veto the first quit only while reproducing F11 -> a few normal frames -> quit.
    internal sealed class VrQuit
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal bool Pending { get; private set; }
        internal bool Ready { get; private set; }

        internal bool Request(object manager)
        {
            if (Ready) return true;
            if (Pending) return false;
            if (manager == null) return true;
            var type = manager.GetType();
            if (!(bool)type.GetField("_managerInitialized", Fields).GetValue(manager)
                || (bool)type.GetField("_isUserSafeModeActive", Fields).GetValue(manager)) return true;
            // Set before invoking the native path to prevent reentrant double toggles.
            Pending = true;
            type.GetMethod("ToggleUserSafeMode", Fields).Invoke(manager, null);
            return false;
        }

        internal void Complete() { Ready = true; Pending = false; }
    }
}
