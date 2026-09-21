using System;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal sealed class VrBodyCollider : IDisposable
    {
        internal const float DefaultWidth = .55f;
        private CapsuleCollider capsule;
        private float originalRadius;
        internal float NativeRadius(CapsuleCollider target)
            => capsule && capsule == target ? originalRadius : target.radius;

        internal void Update(CapsuleCollider target, float width)
        {
            if (capsule != target) Dispose();
            if (!target || target.direction != 1) return;
            if (!capsule) { capsule = target; originalRadius = target.radius; }
            width = CrouchState.Valid(width) ? Mathf.Clamp(width, .5f, 1f) : DefaultWidth;
            // Preserve height and center: narrowing must not lower the head or feet.
            capsule.radius = originalRadius * width;
        }

        internal void AfterPosture(CapsuleCollider target)
        {
            if (!capsule || capsule != target) return;
            // Native SetCapsuleToPosture uses the current radius for both end caps.
            // Its center is unchanged algebraically; restore only the lost height.
            capsule.height += 2 * (originalRadius - capsule.radius);
        }

        public void Dispose()
        {
            if (capsule) capsule.radius = originalRadius;
            capsule = null;
        }
    }
}
