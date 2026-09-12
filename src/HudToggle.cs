namespace WalkNWash.VRCompanion
{
    internal static class HudToggleBinding
    {
        // xrizer's legacy Touch mapping: left Y -> ApplicationMenu, X -> A.
        internal const int OpenVrButton = 1;
        internal static bool OpenVrPressed(int hand, ulong buttons)
            => hand == 1 && (buttons & (1UL << OpenVrButton)) != 0;
        internal static string OpenXrPath(string profile)
            => profile == "oculus/touch_controller" ? "/user/hand/left/input/y/click"
                : profile == "valve/index_controller" ? "/user/hand/left/input/b/click" : null;
    }

    internal struct HudToggle
    {
        private TriggerButton button;
        private bool wasDown;
        internal bool Update(bool pressed, bool enabled)
        {
            bool down = button.Update(pressed ? 1 : 0, enabled);
            bool toggle = down && !wasDown;
            wasDown = down;
            return toggle;
        }
    }
}
