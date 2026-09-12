using System;
using System.Collections.Generic;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    // Only rendering and bones are copied. No collider, animator, audio or gameplay script.
    internal sealed class HandVisual : IDisposable
    {
        private readonly List<Transform> bones = new List<Transform>();
        private readonly List<Quaternion> rest = new List<Quaternion>();
        private readonly Transform root;
        private GameObject owned;
        internal HandVisual(Transform root)
        {
            this.root = root;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("index_0") || t.name.StartsWith("middle_0") || t.name.StartsWith("pinky_0") || t.name.StartsWith("thumb_0"))
                { bones.Add(t); rest.Add(t.localRotation); }
        }
        internal void Pose(float grip, float trigger, bool active, float degrees)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                if (!bones[i]) continue;
                float curl = bones[i].name.StartsWith("index") ? trigger : grip;
                // Native contact animation owns the active pose. Reset only our idle additive pose.
                if (!active) bones[i].localRotation = rest[i] * Quaternion.Euler(Mathf.Clamp01(curl) * degrees, 0, 0);
            }
        }
        internal void Restore()
        { for (int i = 0; i < bones.Count; i++) if (bones[i]) bones[i].localRotation = rest[i]; }
        internal static HandVisual Mirror(Transform source, HandVisual idleTemplate = null)
        {
            var map = new Dictionary<Transform, Transform>();
            var container = new GameObject("VR right hand (visual only)");
            Transform Copy(Transform original, Transform parent)
            {
                var t = new GameObject(original.name).transform;
                t.SetParent(parent, false); t.localPosition = original.localPosition;
                t.localRotation = original.localRotation; t.localScale = original.localScale;
                map.Add(original, t);
                foreach (Transform child in original) Copy(child, t);
                return t;
            }
            var clone = Copy(source, container.transform);
            clone.localPosition = Vector3.zero; clone.localRotation = Quaternion.identity;
            clone.localScale = new Vector3(-source.localScale.x, source.localScale.y, source.localScale.z);
            if (idleTemplate != null)
                for (int i = 0; i < idleTemplate.bones.Count; i++)
                    if (map.TryGetValue(idleTemplate.bones[i], out var idleBone)) idleBone.localRotation = idleTemplate.rest[i];
            foreach (var renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var copy = map[renderer.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = renderer.sharedMesh; copy.sharedMaterials = renderer.sharedMaterials;
                var mappedBones = new Transform[renderer.bones.Length];
                for (int i = 0; i < mappedBones.Length; i++)
                    if (renderer.bones[i]) map.TryGetValue(renderer.bones[i], out mappedBones[i]);
                copy.bones = mappedBones;
                if (renderer.rootBone && map.TryGetValue(renderer.rootBone, out var bone)) copy.rootBone = bone;
                copy.localBounds = renderer.localBounds; copy.updateWhenOffscreen = true;
                copy.shadowCastingMode = renderer.shadowCastingMode;
            }
            return new HandVisual(container.transform) { owned = container };
        }
        internal void Place(HandFrame frame, bool visible)
        {
            if (owned) owned.SetActive(visible);
            if (visible) root.SetPositionAndRotation(frame.Position, frame.Rotation);
        }
        public void Dispose() { Restore(); if (owned) UnityEngine.Object.Destroy(owned); }
    }
}
