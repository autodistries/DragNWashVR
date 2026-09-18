using FluidRenderingForGames;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    // Fill spatial gaps without rerunning native timers, rub events or supply drain.
    internal sealed class SpongeStroke
    {
        private Collider previousCollider;
        private Vector3 previousPoint, previousNormal;
        private int sample, lastEmission = -2;

        internal void Begin(bool active)
        {
            sample++;
            if (!active) previousCollider = null;
        }

        internal int Emit(FluidParticleSystemSettings fluid, FluidParticleSystem.ParticleCollision contact, float extraAmount = .5f)
        {
            int added = 0;
            var collider = contact.collider;
            if (extraAmount > 0 && collider && collider == previousCollider && lastEmission == sample - 1)
            {
                var transform = collider.transform;
                Vector3 start = transform.TransformPoint(previousPoint);
                Vector3 normal = transform.TransformDirection(previousNormal).normalized;
                float distance = Vector3.Distance(start, contact.position);
                // Use the brush's narrow footprint, bounded to avoid excessive decal work.
                float spacing = Mathf.Clamp(Mathf.Abs(contact.size * fluid.splatSize) * .5f, .01f, .04f);
                if (distance <= .5f && Vector3.Dot(normal, contact.normal.normalized) > .5f)
                {
                    int steps = Mathf.Min(16, Mathf.CeilToInt(distance / spacing));
                    // All gap stamps share a fixed allowance. Fast movement must
                    // not multiply soap strength by the number of subdivisions.
                    float weight = extraAmount / Mathf.Max(1, steps - 1);
                    for (int i = 1; i < steps; i++)
                    {
                        float t = (float)i / steps;
                        Vector3 point = Vector3.Lerp(start, contact.position, t);
                        Vector3 inward = Vector3.Lerp(normal, contact.normal, t).normalized;
                        // Reproject each stamp onto this surface. Never bridge open air,
                        // another collider, tracking loss, release, or a large jump.
                        if (!collider.Raycast(new Ray(point - inward * .08f, inward), out var hit, .16f)
                            || Vector3.Dot(hit.normal, -inward) < .5f) continue;
                        var stamp = contact;
                        stamp.position = hit.point; stamp.normal = -hit.normal;
                        stamp.stretch = stamp.normal * contact.stretch.magnitude;
                        stamp.heightStrength *= weight;
                        stamp.color.a *= Mathf.Clamp01(weight);
                        fluid.OnFluidCollision(stamp); added++;
                    }
                }
            }
            fluid.OnFluidCollision(contact);
            previousCollider = collider; lastEmission = sample;
            if (collider)
            {
                previousPoint = collider.transform.InverseTransformPoint(contact.position);
                previousNormal = collider.transform.InverseTransformDirection(contact.normal);
            }
            return added;
        }
    }
}
