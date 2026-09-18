using System;

namespace WalkNWash.VRCompanion
{
    internal struct RoomWalkState
    {
        private bool ready;
        private float previousX, previousZ, previousTime;
        internal void Reset() { ready = false; }
        internal bool Sample(float x, float z, float now, bool enabled, out float dx, out float dz)
        {
            dx = dz = 0;
            if (!enabled || !Finite(x) || !Finite(z) || !Finite(now)) { Reset(); return false; }
            float elapsed = now - previousTime;
            float stepX = x - previousX, stepZ = z - previousZ;
            bool accept = ready && elapsed > 0 && elapsed <= .25f
                && stepX * stepX + stepZ * stepZ <= .35f * .35f;
            previousX = x; previousZ = z; previousTime = now; ready = true;
            if (!accept) return false; // First pose, stale frame, or runtime recenter: rebase.
            dx = stepX; dz = stepZ;
            return true;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
