using System;
namespace WalkNWash.VRCompanion
{
    internal static class GripStep
    {
        internal static int Read(float value, ref bool latched)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) { latched = false; return 0; }
            if (Math.Abs(value) < .3f) latched = false;
            if (latched || Math.Abs(value) < .7f) return 0;
            latched = true;
            return value > 0 ? 1 : -1;
        }
    }
}
