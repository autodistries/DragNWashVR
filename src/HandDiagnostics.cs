using System;
using UnityEngine;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // Read-only render-time diagnostics. Never changes tracking or hand poses.
    internal sealed class HandDiagnostics : IDisposable
    {
        private readonly Action<string> log;
        private readonly int hand;
        private Transform trackedVisual;
        private bool enabled, baselineValid;
        private Vector3 rawBaseline, targetBaseline, visualBaseline;
        private float nextLog;
        private int sampledFrame = -1;
        private GameObject root;
        private readonly LineRenderer[] markers = new LineRenderer[3];
        private Material material;
        internal HandDiagnostics(Action<string> log, int hand = 1) { this.log = log; this.hand = hand; }
        internal void Toggle()
        {
            if (!DebugAccess.Enabled) return;
            enabled = !enabled; Recenter(); EndEye();
            log("Hand diagnostics " + (hand == 1 ? "left " : "right ") + (enabled ? "ON: cyan controller, yellow target, magenta wrist/tool pivot. Release triggers; F10 resets baseline." : "OFF."));
        }
        internal void Recenter() { baselineValid = false; nextLog = 0; }
        internal void BeginEye(Backend backend, VrHands hands)
        {
            EndEye();
            if (!enabled) return;
            if (backend == null || !backend.Rig || !backend.Focused || hands == null || !hands.Active
                || !backend.HeadPose(out var head, out _)
                || !backend.Hand(hand, false, out var raw, out _)
                || !hands.DiagnosticPose(hand, out var target, out var visual, out var active, out var bounds))
            { baselineValid = false; return; }
            if (trackedVisual != visual) { trackedVisual = visual; Recenter(); }
            var rig = backend.Rig.transform;
            var camera = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            if (!camera || camera.cullingMask == 0) return;
            Create();
            int layer = 0; while ((camera.cullingMask & (1 << layer)) == 0 && layer < 31) layer++;
            root.layer = layer;
            Vector3[] points = { rig.TransformPoint(raw), target, visual.position };
            float scale = Mathf.Abs(rig.lossyScale.x);
            for (int i = 0; i < 3; i++)
            {
                var marker = markers[i]; marker.gameObject.layer = layer;
                float size = (.024f - i * .007f) * scale;
                marker.startWidth = marker.endWidth = .0015f * scale;
                Vector3 p = points[i];
                marker.SetPositions(new[] { p - rig.right * size, p + rig.right * size, p,
                    p - rig.up * size, p + rig.up * size, p,
                    p - rig.forward * size, p + rig.forward * size });
            }
            root.SetActive(true);
            if (sampledFrame == Time.frameCount) return;
            sampledFrame = Time.frameCount;
            if (active) { baselineValid = false; return; } // Active reach is intentionally not 1:1.
            Vector3 rawRelative = raw - head;
            Vector3 targetRelative = rig.InverseTransformPoint(target) - head;
            Vector3 visualRelative = rig.InverseTransformPoint(visual.position) - head;
            if (!baselineValid)
            {
                rawBaseline = rawRelative; targetBaseline = targetRelative; visualBaseline = visualRelative;
                baselineValid = true;
            }
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + .5f;
            Vector3 dr = rawRelative - rawBaseline, dt = targetRelative - targetBaseline, dv = visualRelative - visualBaseline;
            string gain = dr.magnitude > .02f ? $" target/raw={dt.magnitude / dr.magnitude:F3} wrist/raw={dv.magnitude / dr.magnitude:F3}" : " (move >20mm for gain)";
            log($"HandDiag side={(hand == 1 ? "left" : "right")} visual={visual.name} idle frame={Time.frameCount} raw/head(mm)={Mm(rawRelative)} target/head(mm)={Mm(targetRelative)} wrist/head(mm)={Mm(visualRelative)}"
                + $" deltaRaw(mm)={Mm(dr)} deltaTarget(mm)={Mm(dt)} deltaWrist(mm)={Mm(dv)}{gain}"
                + $" target-raw(mm)={Mm(targetRelative - rawRelative)} wrist-target(mm)={Mm(visualRelative - targetRelative)}"
                + $" rigScale={rig.lossyScale.ToString("F4")} parentScale={(visual.parent ? visual.parent.lossyScale : Vector3.one).ToString("F4")} wristScale={visual.lossyScale.ToString("F4")}"
                + $" meshCenter/head(mm)={Mm(rig.InverseTransformPoint(bounds.center) - head)} meshWorldSize={bounds.size.ToString("F4")}");
        }
        private static string Mm(Vector3 value) => (value * 1000).ToString("F1");
        private void Create()
        {
            if (root) return;
            root = new GameObject("WalkNWash_HandDiagnostics_" + hand);
            root.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(root);
            // Sprites/Default ignores unity_GUIZTestMode. UI's shader actually
            // uses it, allowing markers to remain visible through scene geometry.
            material = new Material(Graphic.defaultGraphicMaterial);
            material.mainTexture = Texture2D.whiteTexture;
            material.renderQueue = 5000;
            material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            Color[] colors = { Color.cyan, Color.yellow, Color.magenta };
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Marker" + i); go.transform.SetParent(root.transform, false);
                var line = go.AddComponent<LineRenderer>(); markers[i] = line;
                line.sharedMaterial = material; line.useWorldSpace = true; line.positionCount = 8;
                line.startColor = line.endColor = colors[i];
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            }
        }
        internal void EndEye() { if (root) root.SetActive(false); }
        public void Dispose()
        {
            if (root) UnityEngine.Object.Destroy(root);
            if (material) UnityEngine.Object.Destroy(material);
            root = null; material = null;
        }
    }
}
