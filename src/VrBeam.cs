using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // Draw the whole beam before UI, then redraw only the portion on the viewer
    // side of the UI plane after UI. This gives correct front/behind compositing
    // without letting scene depth hide the pointer.
    internal sealed class VrBeam : IDisposable
    {
        internal readonly LineRenderer Behind;
        internal readonly LineRenderer Front;
        private readonly Transform surface;
        private readonly Material behindMaterial, frontMaterial;
        private bool requested, frontRequested;

        internal VrBeam(Transform surface, int sortingLayerID, int sortingOrder)
        {
            this.surface = surface;
            Behind = Create("Controller ray", sortingLayerID, sortingOrder - 1,
                (int)RenderQueue.Transparent - 1, out behindMaterial);
            Front = Create("Controller ray front", sortingLayerID, sortingOrder + 1,
                (int)RenderQueue.Transparent + 1, out frontMaterial);
        }

        private LineRenderer Create(string name, int sortingLayerID, int sortingOrder, int queue, out Material material)
        {
            var go = new GameObject(name, typeof(LineRenderer));
            go.transform.SetParent(surface, false);
            var line = go.GetComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingLayerID = sortingLayerID;
            line.sortingOrder = sortingOrder;
            material = new Material(Graphic.defaultGraphicMaterial);
            material.mainTexture = Texture2D.whiteTexture;
            material.renderQueue = queue;
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            line.sharedMaterial = material;
            line.startColor = line.endColor = Color.cyan;
            line.enabled = false;
            return line;
        }

        internal void Set(Vector3 start, Vector3 end, float width)
        {
            if (!Behind || !Front || !surface)
            {
                requested = frontRequested = false;
                return;
            }

            requested = true;
            Behind.startWidth = Behind.endWidth = width;
            Behind.SetPosition(0, start);
            Behind.SetPosition(1, end);

            frontRequested = FrontSegment(surface, start, end, out var frontStart, out var frontEnd);
            Front.startWidth = Front.endWidth = width;
            if (frontRequested)
            {
                Front.SetPosition(0, frontStart);
                Front.SetPosition(1, frontEnd);
            }
        }

        internal void SetTracked(bool tracked)
        {
            requested = tracked;
            if (!tracked) frontRequested = false;
            if (!tracked) EndEye();
        }

        internal void Show(int layer)
        {
            if (!Behind || !Front)
            {
                requested = frontRequested = false;
                return;
            }

            Behind.gameObject.layer = Front.gameObject.layer = layer;
            Behind.enabled = requested;
            Front.enabled = requested && frontRequested;
        }

        internal void EndEye()
        {
            // The beam objects are children of their world-space UI surface, so a
            // scene change can destroy them before the owning panel gets its final
            // per-eye cleanup callback. Unity's overloaded bool handles that state.
            if (Behind) Behind.enabled = false;
            if (Front) Front.enabled = false;
        }

        internal void Clear()
        {
            requested = frontRequested = false;
            EndEye();
        }

        // World-space UI faces the user from local negative Z. Return only the
        // portion of the line that lies on that viewer side of the UI plane.
        internal static bool FrontSegment(Transform surface, Vector3 start, Vector3 end,
            out Vector3 frontStart, out Vector3 frontEnd)
        {
            const float epsilon = .00001f;
            float a = surface.InverseTransformPoint(start).z;
            float b = surface.InverseTransformPoint(end).z;
            bool startFront = a <= epsilon;
            bool endFront = b <= epsilon;

            if (startFront && endFront)
            {
                frontStart = start;
                frontEnd = end;
                return true;
            }
            if (!startFront && !endFront)
            {
                frontStart = frontEnd = default;
                return false;
            }

            float denominator = b - a;
            float t = Mathf.Abs(denominator) < epsilon ? 0 : Mathf.Clamp01(-a / denominator);
            Vector3 crossing = Vector3.Lerp(start, end, t);
            if (startFront)
            {
                frontStart = start;
                frontEnd = crossing;
            }
            else
            {
                frontStart = crossing;
                frontEnd = end;
            }
            return (frontEnd - frontStart).sqrMagnitude > epsilon * epsilon;
        }

        public void Dispose()
        {
            if (Behind) UnityEngine.Object.Destroy(Behind.gameObject);
            if (Front) UnityEngine.Object.Destroy(Front.gameObject);
            if (behindMaterial) UnityEngine.Object.Destroy(behindMaterial);
            if (frontMaterial) UnityEngine.Object.Destroy(frontMaterial);
        }
    }
}
