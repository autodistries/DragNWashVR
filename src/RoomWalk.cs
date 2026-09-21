using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal static class RoomWalk
    {
        internal static Vector3 Follow(Rigidbody body, CapsuleCollider capsule, ref RoomWalkState state,
            ref Vector3 baseline, Vector3 head, float now, bool enabled, float leanRadius, float yaw, float scale, float recenterSpeed = 0, bool allowBodyMovement = true)
        {
            if (!Backend.Finite(head) || !Backend.Finite(new Vector3(scale, yaw, 0)) || scale <= 0)
            { state.Reset(); return Vector3.zero; }
            if (!state.Sample(head.x, head.z, now, enabled, leanRadius, out float dx, out float dz, recenterSpeed))
                return Vector3.zero;
            // Keep observing head poses while bent, but consume none of the lean.
            // Returning upright can resume from the existing body center without
            // treating the whole bend as a stale tracking sample or a recenter.
            if (!allowBodyMovement || dx * dx + dz * dz < .000000000001f) return Vector3.zero;
            var rotation = Quaternion.Euler(0, yaw, 0);
            if (recenterSpeed > 0)
            {
                state.Offset(out float fullX, out float fullZ);
                if (!CanRecenter(body, capsule, rotation * new Vector3(fullX, 0, fullZ) * scale))
                    return Vector3.zero;
            }
            var moved = Move(body, capsule, rotation * new Vector3(dx, 0, dz) * scale);
            var trackingMove = Quaternion.Inverse(rotation) * moved / scale;
            state.Commit(trackingMove.x, trackingMove.z);
            // Cancelling the requested delta here would pin the headset when the body hits a wall.
            baseline += trackingMove;
            return moved;
        }

        // Automatic catch-up is all-or-nothing with respect to its destination:
        // a clear first step does not mean the capsule can fit under the head.
        internal static bool CanRecenter(Rigidbody body, CapsuleCollider capsule, Vector3 offset)
        {
            if (!body || body.isKinematic || !body.detectCollisions || !capsule || (!capsule.enabled || !capsule.gameObject.activeInHierarchy)
                || capsule.attachedRigidbody != body || !Backend.Finite(offset)) return false;
            offset.y = 0;
            Physics.SyncTransforms(); // Include this frame's animated obstacle transforms.
            if (Overlapping(body, capsule, Vector3.zero) || Overlapping(body, capsule, offset)) return false;
            float length = offset.magnitude;
            if (length < .000001f) return true;
            foreach (var hit in body.SweepTestAll(offset / length, length + .005f, QueryTriggerInteraction.Ignore))
                if (Blocks(body, capsule, hit.collider) && hit.distance < length + .005f) return false;
            return true;
        }

        private static bool Overlapping(Rigidbody body, CapsuleCollider capsule, Vector3 offset)
        {
            // SweepTest does not reliably report colliders already overlapping the
            // starting shape. Check actual penetration at both ends separately.
            var poseOffset = body.position - body.transform.position + offset;
            var bounds = capsule.bounds;
            foreach (var other in Physics.OverlapBox(bounds.center + poseOffset, bounds.extents,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Blocks(body, capsule, other) && Physics.ComputePenetration(capsule,
                    capsule.transform.position + poseOffset, capsule.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out _, out float depth)
                    && depth > .0001f) return true;
            }
            return false;
        }

        private static bool Blocks(Rigidbody body, CapsuleCollider capsule, Collider other)
            => other && other.attachedRigidbody != body
                && !Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, other.gameObject.layer)
                && !Physics.GetIgnoreCollision(capsule, other);

        // Preserve native spring/jump/stick velocity. Teleport() would zero it.
        internal static Vector3 Move(Rigidbody body, CapsuleCollider capsule, Vector3 requested)
        {
            if (!body || body.isKinematic || !body.detectCollisions || !capsule || !capsule.enabled
                || !capsule.gameObject.activeInHierarchy || !Backend.Finite(requested)) return Vector3.zero;
            requested.y = 0;
            float length = requested.magnitude;
            if (length < .000001f) return Vector3.zero;
            Vector3 direction = requested / length;
            float allowed = length;
            foreach (var hit in body.SweepTestAll(direction, length + .005f, QueryTriggerInteraction.Ignore))
            {
                if (!Blocks(body, capsule, hit.collider)) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - .005f));
            }
            if (allowed < .000001f) return Vector3.zero;
            Vector3 before = body.position;
            Vector3 destination = before + direction * allowed;
            // The shipped player is interpolated. A physics-only position write can
            // leave the transform used by LookController behind the consumed motion.
            // Room-scale corrections are explicit teleports, not velocity commands:
            // publish the same pose to physics and rendering before consuming it.
            body.position = destination;
            body.transform.position = destination;
            Physics.SyncTransforms();
            return body.position - before;
        }
    }
}
