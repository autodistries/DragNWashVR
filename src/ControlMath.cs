using System;

namespace WalkNWash.VRCompanion
{
    internal static class ControlMath
    {
        internal static void Deadzone(float x, float y, float zone, out float dx, out float dy)
        {
            dx = dy = 0;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return;
            float length = (float)Math.Sqrt(x * x + y * y);
            zone = Math.Max(0, Math.Min(.95f, zone));
            if (length <= zone) return;
            float scale = (Math.Min(length, 1) - zone) / ((1 - zone) * length);
            dx = x * scale;
            dy = y * scale;
        }

        internal static float Snap(float x, float degrees, ref bool latched)
        {
            if (Math.Abs(x) < .3f) latched = false;
            if (latched || Math.Abs(x) < .7f || float.IsNaN(x)) return 0;
            latched = true;
            return Math.Sign(x) * degrees;
        }

        internal static float SmoothTurn(float x, float deadzone, float degreesPerSecond, float deltaTime)
        {
            Deadzone(x, 0, deadzone, out float amount, out _);
            return amount * degreesPerSecond * Math.Max(0, Math.Min(.1f, deltaTime));
        }

        // Calibrated tracking-space position maps to the moving character's eye anchor.
        internal static void Origin(float ax, float ay, float az, float bx, float by, float bz,
            float yaw, float scale, out float x, out float y, out float z)
        {
            double angle = yaw * Math.PI / 180;
            float sin = (float)Math.Sin(angle), cos = (float)Math.Cos(angle);
            x = ax - scale * (cos * bx + sin * bz);
            y = ay - scale * by;
            z = az - scale * (-sin * bx + cos * bz);
        }
    }
}
