using System;
using com.gatordragongames.washnwalk.tools;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WalkNWash.VRCompanion
{
    internal sealed class GripEditor : IDisposable
    {
        private readonly Plugin host;
        private ToolGrips grips;
        private string tool;
        private Vector3 position, rotation, grabPosition;
        private Quaternion grabRotation;
        private TriggerButton grab, save, cancel, reset;
        private bool dragging, xStep, yStep, yawStep, pitchStep;
        private GameObject label;
        private TextMesh text;
        internal bool Active => tool != null;
        internal GripEditor(Plugin host) { this.host = host; }
        internal void Tick(Backend backend, ToolGrips values, bool usable)
        {
            if (!usable) { if (Active) Finish(false); return; }
            bool key = Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame;
            string held = ToolManager.GetCurrentTool()?.name;
            if (Active && (!usable || held != tool)) { Finish(false); return; }
            if (key && Active) { Finish(false); return; }
            if (key && usable && values != null && held != null)
            {
                grips = values; tool = held;
                grips.Read(tool, out position, out rotation);
                grab = save = cancel = reset = default;
                dragging = xStep = yStep = yawStep = pitchStep = false;
                grips.Preview(tool, position, rotation);
            }
            if (!Active) return;
            backend.Poll();
            if (!host.TryHandFrame(1, out var left) || !host.TryHandFrame(2, out var right))
            { Finish(false); return; }
            if (cancel.Update(backend.Menu ? 1 : 0, true)) { Finish(false); return; }
            if (save.Update(backend.Jump ? 1 : 0, true)) { Finish(true); return; }
            if (reset.Update(backend.ToggleCrouch ? 1 : 0, true))
                grips.Read(tool, out position, out rotation, true);
            bool down = grab.Update(backend.LeftTrigger, true);
            float scale = host.HandScale;
            if (down)
            {
                if (!dragging)
                {
                    var pose = grips.Apply(tool, right, scale);
                    grabPosition = Quaternion.Inverse(left.Rotation) * (pose.Position - left.Position);
                    grabRotation = Quaternion.Inverse(left.Rotation) * pose.Rotation;
                }
                Vector3 target = left.Position + left.Rotation * grabPosition;
                position = Quaternion.Inverse(right.Rotation) * (target - right.Position) / scale;
                rotation = (Quaternion.Inverse(right.Rotation) * left.Rotation * grabRotation).eulerAngles;
            }
            dragging = down;
            if (!down)
            {
                bool depth = backend.RightTrigger > .65f;
                position.x += GripStep.Read(backend.Move.x, ref xStep) * .001f;
                if (depth) position.z += GripStep.Read(backend.Move.y, ref yStep) * .001f;
                else position.y += GripStep.Read(backend.Move.y, ref yStep) * .001f;
                if (depth) rotation.z += GripStep.Read(backend.Turn.x, ref yawStep);
                else rotation.y += GripStep.Read(backend.Turn.x, ref yawStep);
                rotation.x += GripStep.Read(backend.Turn.y, ref pitchStep);
            }
            grips.Preview(tool, position, rotation);
        }
        internal void Finish(bool persist)
        {
            if (Active) { grips.Finish(persist); host.HandLog("Grip " + tool + (persist ? " saved." : " canceled.")); }
            tool = null; dragging = false; EndEye();
        }
        internal void BeginEye(Backend backend)
        {
            EndEye();
            if (!Active || !backend.HeadPose(out var head, out var facing)) return;
            if (!label)
            {
                label = new GameObject("WalkNWash_GripEditor");
                UnityEngine.Object.DontDestroyOnLoad(label);
                text = label.AddComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 48; text.characterSize = .009f;
                text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
                label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            }
            var camera = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            if (!camera || camera.cullingMask == 0) return;
            int layer = 0; while ((camera.cullingMask & (1 << layer)) == 0 && layer < 31) layer++;
            label.layer = layer;
            var rig = backend.Rig.transform;
            var worldRotation = rig.rotation * facing;
            label.transform.SetPositionAndRotation(rig.TransformPoint(head) + worldRotation * new Vector3(0, .30f, .8f), worldRotation);
            text.text = "Grip: " + tool + "\nLeft trigger: grab / release pose\nSticks: 1 mm XY / 1 degree yaw-pitch\nHold right trigger: depth / roll\nRight A: SAVE   Right B / F8: CANCEL   Left X: RESET\n"
                + $"Offset mm: {position.x * 1000:F0}, {position.y * 1000:F0}, {position.z * 1000:F0}\nRotation: {rotation.x:F1}, {rotation.y:F1}, {rotation.z:F1}";
            label.SetActive(true);
        }
        internal void EndEye() { if (label) label.SetActive(false); }
        public void Dispose() { Finish(false); if (label) UnityEngine.Object.Destroy(label); }
    }
}
