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

        internal static Vector3 VisualOffset(Transform parent, Quaternion rotation, float amount, bool touching)
        {
            // Native effects test a <0.1 distance in parent-local units. Preserve
            // those units and leave a margin once touching, even with a scaled rig.
            if (touching) amount = Mathf.Clamp(amount, 0, .08f);
            return parent.TransformVector(parent.InverseTransformDirection(rotation * Vector3.back) * amount);
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

        internal static HandHit Physical(HandFrame frame, Vector3 eye, Vector3 previous, bool continuous,
            float reach, float radius, HandHit retained, float maxDepth)
        {
            // Recover an established contact from outside the actual collider, even
            // when the tracked controller is now inside it. Never acquire through a wall.
            if (continuous && retained.Collider && retained.Collider.enabled && retained.Collider.gameObject.activeInHierarchy)
            {
                Vector3 normal = retained.Normal.normalized;
                float depth = Vector3.Dot(frame.Position - retained.Point, normal);
                if (depth <= reach + radius && depth >= -maxDepth)
                {
                    Vector3 projected = frame.Position - normal * depth;
                    float margin = radius + .05f;
                    if (retained.Collider.Raycast(new Ray(projected + normal * margin, -normal), out var surface,
                        margin + maxDepth) && Vector3.Distance(surface.point, frame.Position) <= maxDepth
                        && Vector3.Dot(surface.normal, normal) > .1f && Visible(eye, surface.point))
                        return new HandHit { Point = surface.point, Normal = surface.normal, Collider = surface.collider };
                }
            }
            var direct = Find(frame, eye, previous, continuous, reach, radius);
            if (direct.Collider) return direct;
            if (continuous && Clear(eye, previous))
            {
                var delta = frame.Position - previous;
                if (delta.sqrMagnitude > .000001f && Physics.SphereCast(previous, radius, delta.normalized,
                    out var swept, delta.magnitude, Mask, QueryTriggerInteraction.Ignore)
                    && Vector3.Dot(delta, swept.normal) < -.000001f && Visible(eye, swept.point))
                    return new HandHit { Point = swept.point, Normal = swept.normal, Collider = swept.collider };
            }
            if (!Visible(eye, frame.Position)) return direct;
            float nearest = radius * radius;
            // Probe a volume from outside in six controller-local directions.
            // Unlike ClosestPoint this also supports non-convex skin MeshColliders.
            foreach (var axis in VolumeAxes)
            {
                Vector3 direction = frame.Rotation * axis;
                if (!Physics.SphereCast(frame.Position - direction * (2 * radius), radius, direction,
                    out var volume, 2 * radius, Mask, QueryTriggerInteraction.Ignore)) continue;
                float distance = (frame.Position - volume.point).sqrMagnitude;
                if (volume.normal.sqrMagnitude < .5f || distance > nearest || !Visible(eye, volume.point)) continue;
                nearest = distance;
                direct = new HandHit { Point = volume.point, Normal = volume.normal, Collider = volume.collider };
            }
            return direct;
        }

        private static readonly Vector3[] VolumeAxes = { Vector3.right, Vector3.left, Vector3.up,
            Vector3.down, Vector3.forward, Vector3.back };

        private static bool Visible(Vector3 eye, Vector3 point)
            => Vector3.Distance(eye, point) <= 2f && Clear(eye, point);

        internal static bool Clear(Vector3 origin, Vector3 target)
        {
            Vector3 delta = target - origin;
            return delta.magnitude < .03f || !Physics.Raycast(origin, delta.normalized, delta.magnitude - .025f,
                Mask, QueryTriggerInteraction.Ignore);
        }
    }
}
