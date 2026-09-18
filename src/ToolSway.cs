using System;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    // Audited tool clips animate these presentation pivots beneath the tracked root.
    // Freeze their held pose only; keep Animator state, effects and child bones live.
    internal sealed class ToolSway
    {
        private readonly Transform target;
        private readonly Animator animator;
        private readonly string clipName;
        private readonly Vector3 originalPosition;
        private readonly Quaternion originalRotation;
        private Vector3 heldPosition;
        private Quaternion heldRotation;
        private bool captured;

        internal ToolSway(Transform model, string path, Animator animator = null, string clipName = null)
        {
            target = model.Find(path);
            if (!target) throw new InvalidOperationException("Held tool pivot missing: " + path);
            this.animator = animator; this.clipName = clipName;
            originalPosition = target.localPosition; originalRotation = target.localRotation;
        }

        internal static ToolSway Create(Transform model)
        {
            var animator = model.GetComponent<Animator>();
            if (!animator || !animator.runtimeAnimatorController) return null;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                string path;
                switch (clip.name)
                {
                    case "crowbar_in_hand": path = "crowbar"; break;
                    case "sponge_in_hand_idle": path = "Sponge/sponge"; break;
                    case "in_hand_idle": path = "Pivot/power_washer"; break;
                    case "spinner_idle": path = "fidget_holder"; break;
                    case "bandage_in_hand": path = "bandaid"; break;
                    case "mount_in_hand": path = "BreedingStand"; break;
                    default: continue;
                }
                if (model.Find(path)) return new ToolSway(model, path, animator, clip.name);
            }
            return null;
        }

        internal void Apply()
        {
            if (!target) return;
            if (!captured)
            {
                if (animator)
                {
                    if (animator.IsInTransition(0)) return;
                    bool idle = false;
                    foreach (var clip in animator.GetCurrentAnimatorClipInfo(0))
                        if (clip.clip && clip.clip.name == clipName && clip.weight > .99f) idle = true;
                    if (!idle) return;
                }
                // Preserve the real, fully blended held pose. Time-zero sampling
                // skipped equip transitions and displaced existing grip calibrations.
                heldPosition = target.localPosition; heldRotation = target.localRotation;
                captured = true;
            }
            target.localPosition = heldPosition; target.localRotation = heldRotation;
        }
        internal void Restore()
        {
            if (!target) return;
            target.localPosition = originalPosition; target.localRotation = originalRotation;
        }
    }
}
