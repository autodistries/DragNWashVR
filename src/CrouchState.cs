using System;

namespace WalkNWash.VRCompanion
{
    internal static class CrouchBinding
    {
        internal static bool OpenVrPressed(int hand, ulong buttons)
            => hand == 1 && (buttons & (1UL << 7)) != 0;
        internal static string OpenXrPath(string profile)
            => profile == "oculus/touch_controller" ? "/user/hand/left/input/x/click"
                : profile == "valve/index_controller" ? "/user/hand/left/input/a/click" : null;
    }

    internal sealed class CrouchState
    {
        private HudToggle toggle;
        private bool physical;
        private bool? forced;
        internal float Baseline { get; private set; }
        // Eye-level/local reference spaces can report zero. Keep seated calibration useful.
        internal float ReferenceHeight => Math.Max(.5f, Baseline);
        internal bool Automatic => !forced.HasValue;
        internal bool Crouched => forced ?? physical;

        internal void Calibrate(float height)
        {
            Baseline = Valid(height) ? height : 0;
            physical = false;
            forced = null;
            toggle = new HudToggle();
        }

        internal void Update(float height, bool pressed, bool usable, bool automaticHeight, float threshold)
        {
            if (usable)
            {
                if (!automaticHeight) physical = false;
                else if (Valid(height))
                {
                    float ratio = 1 + (height - Baseline) / ReferenceHeight;
                    float enter = Math.Max(.4f, Math.Min(.9f, threshold));
                    if (ratio <= enter) physical = true;
                    else if (ratio >= Math.Min(.98f, enter + .1f)) physical = false;
                }
            }
            // Tracking loss freezes physical posture; context loss disarms X until release.
            if (toggle.Update(pressed, usable)) forced = !Crouched;
        }

        internal float Posture(float desktop)
            => desktop > 0 ? desktop : (forced.HasValue ? (forced.Value ? -1 : 0) : (physical ? -1 : desktop));

        internal static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal float EyeAdjustment(float height, float avatarHeight, float gameDrop, float worldScale)
        {
            if (!Valid(height)) return 0;
            float raw = height - Baseline;
            float mapped = raw / ReferenceHeight * avatarHeight;
            // Don't push the eye below the feet when kneeling deeply.
            mapped = Math.Max(.15f - avatarHeight, mapped);
            // Physical lowering and the capsule's crouch use the deeper offset, not their sum.
            float desired = mapped < 0 && gameDrop < 0 ? Math.Min(mapped, gameDrop) : mapped + gameDrop;
            return desired - gameDrop - raw * worldScale;
        }
    }
}
