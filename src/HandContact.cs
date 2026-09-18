using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal struct HandFrame
    {
        internal Vector3 Position, Direction;
        internal Quaternion Rotation;
        internal float Curl, Trigger;
    }

    internal struct HandHit
    {
        internal Vector3 Point, Normal;
        internal Collider Collider;
    }

    internal static class HandContact
    {
        internal const int Mask = 385;
        internal static Vector3 PalmDirection(Quaternion wrist, Quaternion meshCorrection, float tiltDegrees)
        {
            // plapper_L: palm outward is +Z; fingers extend along +Y.
            float angle = Mathf.Clamp(tiltDegrees, 0, 90) * Mathf.Deg2Rad;
            return (wrist * meshCorrection * new Vector3(0, Mathf.Sin(angle), Mathf.Cos(angle))).normalized;
        }
        internal static Quaternion SurfaceRotation(Vector3 normal, Quaternion wrist)
        {
            Vector3 forward = -normal.normalized;
            Vector3 up = Vector3.ProjectOnPlane(wrist * Vector3.up, forward);
            if (up.sqrMagnitude < .001f) up = Vector3.ProjectOnPlane(wrist * Vector3.right, forward);
            return Quaternion.LookRotation(forward, up.normalized);
        }

        // A forward ray gives familiar extension; a short swept probe catches close rubbing.
        internal static HandHit Find(HandFrame frame, Vector3 bodyEye, Vector3 previous, bool hadPrevious, float reach, float radius)
        {
            Vector3 origin = frame.Position, direction = frame.Direction.normalized;
            var result = new HandHit { Point = origin + direction * Mathf.Min(reach, .35f), Normal = -direction };
            if (Vector3.Distance(origin, bodyEye) > 2f || !Clear(bodyEye, origin)) return result;
            RaycastHit hit = default;
            bool found = radius > 0 && Physics.SphereCast(origin - direction * radius, radius, direction, out hit,
                Mathf.Min(reach, .18f) + radius, Mask, QueryTriggerInteraction.Ignore);
            Vector3 delta = origin - previous;
            if (!found && hadPrevious && radius > 0 && delta.sqrMagnitude > .00001f && delta.sqrMagnitude < .25f)
                found = Physics.SphereCast(previous, radius, delta.normalized, out hit, delta.magnitude,
                    Mask, QueryTriggerInteraction.Ignore);
            if (!found) found = Physics.Raycast(origin, direction, out hit, reach, Mask, QueryTriggerInteraction.Ignore);
            if (found && Vector3.Distance(bodyEye, hit.point) <= 2f && Clear(bodyEye, hit.point))
                return new HandHit { Point = hit.point, Normal = hit.normal, Collider = hit.collider };
            return result;
        }

        internal static bool Clear(Vector3 origin, Vector3 target)
        {
            Vector3 delta = target - origin;
            return delta.magnitude < .03f || !Physics.Raycast(origin, delta.normalized, delta.magnitude - .025f,
                Mask, QueryTriggerInteraction.Ignore);
        }
    }
}
