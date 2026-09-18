using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal static class BodyFacing
    {
        internal static Quaternion Rotation(Quaternion previous, float yaw, Vector3 normal,
            Quaternion modelCorrection, float speed, float elapsed, out Vector3 forward, float deadZone = 15f)
        {
            // Measure anatomical facing, excluding the model's authored correction.
            Vector3 bodyForward = previous * Quaternion.Inverse(modelCorrection) * Vector3.forward;
            float bodyYaw = Mathf.Atan2(bodyForward.x, bodyForward.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(bodyYaw, yaw);
            float freedom = Mathf.Clamp(deadZone, 0, 90);
            yaw = bodyYaw + Mathf.Sign(delta) * Mathf.Max(0, Mathf.Abs(delta) - freedom);
            if (normal.sqrMagnitude < .01f) normal = Vector3.up;
            normal.Normalize();
            forward = Vector3.ProjectOnPlane(Quaternion.Euler(0, yaw, 0) * Vector3.forward, normal);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.ProjectOnPlane(previous * Vector3.forward, normal);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.Cross(normal, Vector3.right);
            forward.Normalize();
            return Quaternion.Slerp(previous, Quaternion.LookRotation(forward, normal) * modelCorrection,
                Mathf.Clamp01(speed * elapsed));
        }
    }
}
