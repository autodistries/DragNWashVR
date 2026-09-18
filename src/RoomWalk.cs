using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal static class RoomWalk
    {
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
                if (!hit.collider || hit.collider.attachedRigidbody == body
                    || Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, hit.collider.gameObject.layer)
                    || Physics.GetIgnoreCollision(capsule, hit.collider)) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - .005f));
            }
            Vector3 moved = direction * allowed;
            body.position += moved;
            return moved;
        }
    }
}
