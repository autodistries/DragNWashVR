namespace WalkNWash.VRCompanion
{
    internal static class JumpBinding
    {
        internal const int OpenVrButtonA = 7;

        internal static string OpenXrPath(string profile)
        {
            if (profile == "htc/vive_controller") return "/user/hand/right/input/squeeze/click";
            return profile == "oculus/touch_controller" || profile == "valve/index_controller"
                ? "/user/hand/right/input/a/click" : null;
        }

        internal static bool OpenVrPressed(int hand, ulong buttons, bool trackpadOnly = false)
            => hand == 2 && (buttons & (1UL << (trackpadOnly ? 2 : OpenVrButtonA))) != 0;
    }
}
