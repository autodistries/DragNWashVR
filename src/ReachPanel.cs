using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    internal sealed class ReachPanel : IDisposable
    {
        private readonly VrDialoguePanel panel = new VrDialoguePanel();
        private readonly Action save;
        private ConfigEntry<float> reach;
        private RectTransform knob;
        private TMP_Text value;
        private bool dragging, pressed, armed;
        internal bool Active { get; private set; }
        internal ReachPanel(Action save) { this.save = save; }
        internal void Tick(Backend backend, ConfigEntry<float> setting, bool usable)
        {
            if (!usable) { Close(); return; }
            if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
            {
                if (Active) { Close(); return; }
                reach = setting; Active = true; armed = false; panel.Recenter();
            }
            if (!Active) return;
            backend.Poll();
            if (!backend.HeadPose(out var head, out var rotation)) { Close(); return; }
            panel.Present(null, "Hand / tool reach", "\n\n\n\n", int.MaxValue,
                Array.Empty<DialogueChoice>(), false);
            panel.Place(backend.Rig.transform, head, rotation, 1.1f, 1.2f);
            if (!knob) Create();
            bool tracked = backend.Aim(out var hand, out var aim);
            var ray = new Ray(backend.Rig.transform.TransformPoint(hand), backend.Rig.transform.rotation * aim * Vector3.forward);
            panel.Point(ray, tracked);
            bool down = tracked && backend.RightTrigger > .65f;
            if (!down) armed = true;
            var root = panel.Surface;
            var plane = new Plane(root.forward, root.position);
            if (tracked && plane.Raycast(ray, out float distance))
            {
                Vector3 local = root.InverseTransformPoint(ray.GetPoint(distance));
                if (armed && down && !pressed && Mathf.Abs(local.y) < 35 && Mathf.Abs(local.x) <= 380) dragging = true;
                if (down && dragging)
                    reach.Value = Mathf.Lerp(.1f, 2f, Mathf.InverseLerp(-350, 350, local.x));
            }
            if (!down && dragging) { dragging = false; save(); }
            pressed = down;
            RefreshValue();
        }
        private void RefreshValue()
        {
            knob.anchoredPosition = new Vector2(Mathf.Lerp(-350, 350, Mathf.InverseLerp(.1f, 2f, reach.Value)), 0);
            value.text = "Reach: " + Mathf.RoundToInt(reach.Value / (float)reach.DefaultValue * 100) + "%";
        }
        private void Create()
        {
            var material = panel.Surface.GetComponentInChildren<Image>().material;
            RectTransform Box(string name, Vector2 size, Vector2 position, Color color)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = go.GetComponent<RectTransform>(); rect.SetParent(panel.Surface, false);
                rect.sizeDelta = size; rect.anchoredPosition = position;
                var image = go.GetComponent<Image>(); image.color = color; image.material = material; image.raycastTarget = false;
                return rect;
            }
            TMP_Text Label(string name, string text, Vector2 position)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                var rect = go.GetComponent<RectTransform>(); rect.SetParent(panel.Surface, false);
                rect.sizeDelta = new Vector2(880, 40); rect.anchoredPosition = position;
                var label = go.GetComponent<TextMeshProUGUI>(); label.font = TMP_Settings.defaultFontAsset;
                label.fontSharedMaterial = panel.Surface.Find("Text").GetComponent<TMP_Text>().fontSharedMaterial;
                label.fontSize = 26; label.color = DialogueStyle.Ink; label.alignment = TextAlignmentOptions.Center;
                label.text = text; label.raycastTarget = false; return label;
            }
            Box("Reach track", new Vector2(700, 10), Vector2.zero, Color.gray);
            float x = Mathf.Lerp(-350, 350, Mathf.InverseLerp(.1f, 2f, (float)reach.DefaultValue));
            Box("Default marker", new Vector2(4, 36), new Vector2(x, 0), Color.black);
            Label("Default", "Default 100%", new Vector2(x, -37)).rectTransform.sizeDelta = new Vector2(200, 35);
            knob = Box("Reach handle", new Vector2(22, 32), Vector2.zero, new Color(.1f, .4f, .7f));
            value = Label("Reach value", "", new Vector2(0, 48));
            RefreshValue();
            Label("Instructions", "Right trigger: drag    |    F6: close (saved)", new Vector2(0, -88));
        }
        internal void BeginEye(Backend backend)
        {
            if (!Active || !knob) return;
            var left = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            var right = Backend.Field(backend.Setup, "_rightVrCamera") as Camera;
            int mask = left && right ? left.cullingMask & right.cullingMask : 0;
            if (mask == 0) return;
            int layer = 0; while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
            panel.BeginEye(layer);
        }
        internal void EndEye() => panel.EndEye();
        internal void Close()
        {
            if (Active) save();
            Active = dragging = pressed = false; panel.Hide();
        }
        public void Dispose() { Close(); panel.Dispose(); }
    }
}
