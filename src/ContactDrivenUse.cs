using UnityEngine;

namespace WalkNWash.VRCompanion
{
    // Owned by one VrHands.ContactState. No hooks, config gates, or global history.
    internal sealed class ContactDrivenUse
    {
        private Transform rig;
        private Vector3 previousTracking, previousPalm;
        private float previousTime;
        private bool hasPrevious;
        private ContactImpact impact;
        private Collider surface;
        private Vector3 localPoint, localNormal;

        internal void Reset()
        {
            rig = null; hasPrevious = false; impact = default; surface = null;
        }

        internal HandHit Probe(HandFrame frame, Transform currentRig, Vector3 bodyEye,
            float reach, float radius, float now, float plapSpeed, float rearmSpeed, out bool plap)
        {
            Vector3 tracking = currentRig.InverseTransformPoint(frame.Position);
            Vector3 palm = tracking + currentRig.InverseTransformDirection(frame.Direction) * .08f;
            bool continuous = hasPrevious && rig == currentRig
                && ValidSample(now - previousTime, (tracking - previousTracking).sqrMagnitude);
            if (!continuous) impact = default;
            Vector3 previous = continuous ? currentRig.TransformPoint(previousTracking) : frame.Position;
            var retained = continuous && surface ? new HandHit { Collider = surface,
                Point = surface.transform.TransformPoint(localPoint), Normal = surface.transform.TransformDirection(localNormal) } : default;
            var hit = HandContact.Physical(frame, bodyEye, previous, continuous, reach, radius, retained,
                .5f * Mathf.Abs(currentRig.lossyScale.x));
            surface = hit.Collider;
            if (surface)
            {
                localPoint = surface.transform.InverseTransformPoint(hit.Point);
                localNormal = surface.transform.InverseTransformDirection(hit.Normal);
            }
            float speed = continuous ? InwardSpeed(previousPalm, palm, previousTime, now, currentRig, hit.Normal) : 0f;
            plap = impact.Update(hit.Collider != null, speed, now, plapSpeed, rearmSpeed);
            rig = currentRig; previousTracking = tracking; previousPalm = palm; previousTime = now; hasPrevious = true;
            return hit;
        }

        private static bool ValidSample(float dt, float distanceSquared)
            => dt > .001f && dt <= .12f && distanceSquared <= .1225f;

        internal static float InwardSpeed(Vector3 previousTracking, Vector3 tracking, float previousTime, float now,
            Transform rig, Vector3 normal)
        {
            Vector3 delta = tracking - previousTracking;
            if (!rig || normal.sqrMagnitude <= .5f || !ValidSample(now - previousTime, delta.sqrMagnitude)) return 0f;
            // TransformDirection deliberately excludes world scale: thresholds use physical metres.
            Vector3 velocity = rig.TransformDirection(delta / (now - previousTime));
            return Mathf.Max(0f, -Vector3.Dot(velocity, normal.normalized));
        }
    }
}
