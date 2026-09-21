namespace WalkNWash.VRCompanion
{
    internal struct MenuHold
    {
        internal const float Duration = .45f;
        private TriggerButton button;
        private bool holding, fired;
        private float started;
        internal bool ShortPress { get; private set; }
        internal static bool OpenVrPressed(int hand, ulong buttons) => hand == 2 && (buttons & (1UL << 1)) != 0;
        internal static string OpenXrPath(string profile) =>
            profile == "oculus/touch_controller" || profile == "valve/index_controller"
                ? "/user/hand/right/input/b/click"
                : profile == "htc/vive_controller" ? "/user/hand/right/input/menu/click" : null;
        internal bool Update(bool pressed, bool enabled, float now)
        {
            ShortPress = false;
            bool down = button.Update(pressed ? 1 : 0, enabled);
            if (!enabled) { holding = fired = false; return false; }
            if (!down)
            {
                ShortPress = holding && !fired && now - started < Duration;
                bool longRelease = holding && !fired && !ShortPress;
                holding = fired = false;
                return longRelease;
            }
            if (!holding) { holding = true; started = now; }
            if (fired || now - started < Duration) return false;
            fired = true;
            return true;
        }
    }
}
