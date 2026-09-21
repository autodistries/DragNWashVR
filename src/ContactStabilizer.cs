using UnityEngine;

namespace WalkNWash.VRCompanion
{
    // Presentation only: acquisition, slap velocity and soap stamps use the raw hit.
    internal sealed class ContactStabilizer
    {
        private Collider surface;
        private Vector3 localPoint;
        private Quaternion localRotation;
        internal void Reset() => surface = null;

        internal bool TryPose(out Vector3 point, out Quaternion rotation)
        {
            point = default; rotation = Quaternion.identity;
            if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy) return false;
            point = surface.transform.TransformPoint(localPoint);
            rotation = surface.transform.rotation * localRotation;
            return true;
        }

        internal void Apply(HandHit hit, Transform parent, float dt, ref Vector3 point, ref Quaternion rotation)
        {
            if (!hit.Collider) { Reset(); return; }
            var t = hit.Collider.transform;
            if (surface == hit.Collider && dt > 0 && dt < .1f)
            {
                float blend = 1 - Mathf.Exp(-dt / .035f);
                Vector3 old = t.TransformPoint(localPoint);
                // Cap lag in the same local units as the native effect-distance check.
                Vector3 lag = parent.InverseTransformVector(Vector3.Lerp(old, point, blend) - point);
                if (Vector3.Distance(old, point) < .15f)
                {
                    Vector3 candidate = point + parent.TransformVector(Vector3.ClampMagnitude(lag, .01f));
                    float cast = Mathf.Max(.005f, parent.TransformVector(Vector3.one * .03f).magnitude);
                    // Project back onto skin: an interpolated chord can cut inside curved surfaces.
                    if (hit.Collider.Raycast(new Ray(candidate + hit.Normal * cast, -hit.Normal), out var projected, cast * 2)
                        && parent.InverseTransformVector(projected.point - hit.Point).magnitude <= .011f
                        && Vector3.Dot(projected.normal, hit.Normal) > .8f)
                    {
                        point = projected.point;
                        // Smooth wrist twist while keeping the palm flush with the current skin.
                        var wrist = Quaternion.Slerp(t.rotation * localRotation, rotation, blend);
                        rotation = HandContact.SurfaceRotation(projected.normal, wrist);
                    }
                }
            }
            surface = hit.Collider;
            localPoint = t.InverseTransformPoint(point);
            localRotation = Quaternion.Inverse(t.rotation) * rotation;
        }
    }
}
