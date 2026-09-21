using System;

namespace WalkNWash.VRCompanion
{
    internal struct RoomWalkState
    {
        internal const float DefaultLeanRadius = .25f;
        internal const float DefaultRecenterSpeed = 4f;
        private bool ready;
        private float previousX, previousZ, previousTime, centerX, centerZ;
        internal void Reset() { ready = false; }

        // Head translation alone cannot distinguish a step from bending at the waist.
        // Keep physical leaning entirely out of body following, with hysteresis.
        internal static bool Leaning(float height, float standingHeight, float headUp, ref bool leaning)
        {
            if (!Finite(height) || !Finite(standingHeight) || !Finite(headUp)) return true;
            float lowered = standingHeight - height;
            if (lowered > .10f || headUp < .9396926f) leaning = true; // 20 degrees
            else if (lowered < .06f && headUp > .9781476f) leaning = false; // 12 degrees
            return leaning;
        }
        internal bool Sample(float x, float z, float now, bool enabled, float leanRadius, out float dx, out float dz, float recenterSpeed = 0)
        {
            dx = dz = 0;
            if (!enabled || !Finite(x) || !Finite(z) || !Finite(now)) { Reset(); return false; }
            float elapsed = now - previousTime;
            float stepX = x - previousX, stepZ = z - previousZ;
            bool accept = ready && elapsed > 0 && elapsed <= .25f
                && stepX * stepX + stepZ * stepZ <= .35f * .35f;
            previousX = x; previousZ = z; previousTime = now; ready = true;
            if (!accept)
            {
                // First pose, pause, tracking loss or recenter: never catch up old motion.
                centerX = x; centerZ = z;
                return false;
            }
            float offsetX = x - centerX, offsetZ = z - centerZ;
            float distance = (float)Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
            float radius = Finite(leanRadius) ? Math.Max(0, Math.Min(.5f, leanRadius)) : DefaultLeanRadius;
            if (distance <= .000001f) return true;
            float speed = Finite(recenterSpeed) ? Math.Max(0, Math.Min(12, recenterSpeed)) : DefaultRecenterSpeed;
            // Follow excess movement immediately, then relax the remaining lean.
            // Only Commit consumes actual movement, so an obstacle can retain the lean.
            float retained = Math.Min(distance, radius) * (float)Math.Exp(-speed * elapsed);
            float fraction = (distance - retained) / distance;
            dx = offsetX * fraction; dz = offsetZ * fraction;
            return true;
        }

        internal void Offset(out float x, out float z)
        { x = previousX - centerX; z = previousZ - centerZ; }

        // Consume only motion the capsule actually completed. Blocked motion remains a lean.
        internal void Commit(float dx, float dz) { centerX += dx; centerZ += dz; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
