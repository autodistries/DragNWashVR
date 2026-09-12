using System;
using System.Collections.Generic;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    // Additive idle finger pose on the original left hand; active animation stays native.
    internal sealed class HandVisual : IDisposable
    {
        private readonly List<Transform> bones = new List<Transform>();
        private readonly List<Quaternion> rest = new List<Quaternion>();
        internal HandVisual(Transform root)
        {
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
        public void Dispose() { Restore(); }
    }
}
