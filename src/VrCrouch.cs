using System;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal sealed class VrCrouch
    {
        internal readonly CrouchState State = new CrouchState();
        private readonly LookController look;
        private readonly float standingEyeLocal, avatarHeight;

        internal VrCrouch(PlayerController player)
        {
            look = player.GetComponent<LookController>();
            var body = player.GetComponent<MassSpringController>();
            var capsule = player.GetComponent<CapsuleCollider>();
            // Read standing dimensions saved by the game's Awake, even when currently crouched.
            standingEyeLocal = Convert.ToSingle(Backend.Field(body, "defaultBottomOffset"))
                + Convert.ToSingle(Backend.Field(body, "defaultCapsuleHeight")) + capsule.radius
                - Convert.ToSingle(Backend.Field(look, "viewTopDistance"));
            float height = standingEyeLocal + Convert.ToSingle(Backend.Field(body, "defaultSpringLength"));
            avatarHeight = Mathf.Max(.2f, player.transform.TransformVector(Vector3.up * height).y);
        }

        internal float EyeAdjustment(float headY, float worldScale)
        {
            float currentEye = Convert.ToSingle(Backend.Field(look, "capsuleSmoothdamp"));
            float gameDrop = look.transform.TransformVector(Vector3.up * (currentEye - standingEyeLocal)).y;
            return State.EyeAdjustment(headY, avatarHeight, gameDrop, worldScale);
        }
    }
}
