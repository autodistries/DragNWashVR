namespace WalkNWash.VRCompanion
{
    internal static class JumpBinding
    {
        internal const int OpenVrButtonA = 7;

        internal static string OpenXrPath(string profile)
        {
            return profile == "oculus/touch_controller" || profile == "valve/index_controller"
                ? "/user/hand/right/input/a/click" : null;
        }

        internal static bool OpenVrPressed(int hand, ulong buttons)
            => hand == 2 && (buttons & (1UL << OpenVrButtonA)) != 0;
    }
}
