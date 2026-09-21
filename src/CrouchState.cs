using System;

namespace WalkNWash.VRCompanion
{
    internal static class CrouchBinding
    {
        internal static bool OpenVrPressed(int hand, ulong buttons, bool trackpadOnly = false)
            => hand == 1 && (buttons & (1UL << (trackpadOnly ? 2 : 7))) != 0;
        internal static string OpenXrPath(string profile)
            => profile == "oculus/touch_controller" ? "/user/hand/left/input/x/click"
                : profile == "valve/index_controller" ? "/user/hand/left/input/a/click"
                : profile == "htc/vive_controller" ? "/user/hand/left/input/squeeze/click" : null;
    }

    internal sealed class CrouchState
    {
        private HudToggle toggle;
        private bool physical, desktopOverride;
        private bool? forced;
        internal float Baseline { get; private set; }
        // Eye-level/local reference spaces can report zero. Keep seated calibration useful.
        internal float ReferenceHeight => Math.Max(.5f, Baseline);
        internal bool Automatic => !forced.HasValue;
        internal bool HeightDriven => Automatic && !desktopOverride;
        internal bool Crouched => forced ?? physical;

        internal void Calibrate(float height)
        {
            Baseline = Valid(height) ? height : 0;
            physical = desktopOverride = false;
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
        {
            desktopOverride = desktop != 0;
            return desktop > 0 ? desktop : (forced.HasValue ? (forced.Value ? -1 : 0) : (physical ? -1 : desktop));
        }

        internal float CapsulePosture(float height, float worldScale, float standingCylinderHeight)
        {
            if (!Valid(height) || !Valid(worldScale) || worldScale <= 0 || !Valid(standingCylinderHeight) || standingCylinderHeight <= .001f) return 1;
            // Keep the authored standing size and spherical end caps. Only the
            // cylindrical section shrinks continuously with physical head lowering.
            return Math.Max(0, Math.Min(1, 1 + (height - Baseline) * worldScale / standingCylinderHeight));
        }

        internal static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal float EyeAdjustment(float height, float avatarHeight, float gameDrop, float worldScale)
        {
            if (!Valid(height)) return 0;
            float raw = height - Baseline;
            float mapped = raw * worldScale;
            // Don't push the eye below the feet when kneeling deeply.
            mapped = Math.Max(.15f - avatarHeight, mapped);
            // In height mode the tracked pose owns vertical camera motion. Cancel
            // native crouch animation/smoothing instead of adding a second drop.
            // Explicit X/desktop overrides retain native crouch/stretch behavior.
            float desired = HeightDriven ? mapped :
                (mapped < 0 && gameDrop < 0 ? Math.Min(mapped, gameDrop) : mapped + gameDrop);
            return desired - gameDrop - raw * worldScale;
        }
    }
}
