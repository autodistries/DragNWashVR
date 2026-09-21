using System;

namespace WalkNWash.VRCompanion
{
    // Detached tracking-origin pose. Native cameras and player transforms are never changed.
    internal sealed class FreecamState
    {
        internal bool Active { get; private set; }
        internal float X, Y, Z, Yaw;
        private bool armed, snapLatched;
        internal void Reset() { Active = armed = snapLatched = false; }
        internal void Enter(float x, float y, float z, float yaw)
        { X = x; Y = y; Z = z; Yaw = yaw; Active = true; armed = snapLatched = false; }

        internal void Step(bool usable, float moveX, float moveY, float turnX, float turnY,
            bool up, bool down, float headX, float headZ, float headYaw, float scale,
            float dt, float zone, bool smoothTurn, float turnSpeed, float snapDegrees)
        {
            if (!Active) return;
            if (!usable || !CrouchState.Valid(dt) || !CrouchState.Valid(scale) || scale <= 0
                || !CrouchState.Valid(headX) || !CrouchState.Valid(headZ) || !CrouchState.Valid(headYaw)
                || !CrouchState.Valid(moveX) || !CrouchState.Valid(moveY)
                || !CrouchState.Valid(turnX) || !CrouchState.Valid(turnY))
            { armed = snapLatched = false; return; }
            ControlMath.Deadzone(moveX, moveY, zone, out float x, out float z);
            ControlMath.Deadzone(0, turnY, zone, out _, out float y);
            if (!armed)
            {
                armed = x == 0 && z == 0 && y == 0 && Math.Abs(turnX) <= zone && !up && !down;
                return; // Release controls after enabling, menus or focus loss.
            }
            dt = Math.Max(0, Math.Min(.1f, dt));
            float turn = smoothTurn ? ControlMath.SmoothTurn(turnX, zone, turnSpeed, dt)
                : ControlMath.Snap(turnX, snapDegrees, ref snapLatched);
            if (smoothTurn) snapLatched = false;
            // Turn about the headset rather than orbiting around the tracking origin.
            Rotate(headX * scale, headZ * scale, Yaw, out float beforeX, out float beforeZ);
            Yaw += turn;
            Rotate(headX * scale, headZ * scale, Yaw, out float afterX, out float afterZ);
            X += beforeX - afterX; Z += beforeZ - afterZ;
            y = Math.Max(-1, Math.Min(1, y + (up ? 1 : 0) - (down ? 1 : 0)));
            float length = (float)Math.Sqrt(x * x + y * y + z * z);
            if (length > 1) { x /= length; y /= length; z /= length; }
            Rotate(x, z, Yaw + headYaw, out x, out z);
            float distance = 1.5f * scale * dt; // 1.5 tracking metres/second, level flight.
            X += x * distance; Y += y * distance; Z += z * distance;
        }

        private static void Rotate(float x, float z, float yaw, out float rx, out float rz)
        {
            double radians = yaw * Math.PI / 180;
            float sin = (float)Math.Sin(radians), cos = (float)Math.Cos(radians);
            rx = cos * x + sin * z; rz = -sin * x + cos * z;
        }
    }
}
