using System;
using System.Linq;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    internal sealed class UserSettingsPanel : IDisposable
    {
        private readonly VrDialoguePanel panel = new VrDialoguePanel();
        private readonly UserSettings settings;
        private readonly string version;
        private readonly Action recenter;
        private readonly List<DialogueChoice> choices = new List<DialogueChoice>();
        private ConfigEntryBase[] entries;
        private ConfigEntryBase selected;
        private TMP_Text versionLabel, sliderValue;
        private RectTransform sliderTrack, sliderDefault, sliderKnob;
        private MenuHold menuButton;
        private bool armed, pressed, dragging;
        private int pressedTarget = VrDialoguePanel.None;
        internal bool Active { get; private set; }
        internal UserSettingsPanel(UserSettings settings, string version, Action recenter = null)
        { this.settings = settings; this.version = version; this.recenter = recenter; }

        internal void Open()
        {
            entries = settings.Entries.ToArray();
            selected = null; Active = true; armed = pressed = dragging = false; panel.Recenter(); Refresh();
        }
        internal void Toggle() { if (Active) Close(); else Open(); }

        private void Refresh()
        {
            choices.Clear();
            if (selected == null)
            {
                foreach (var entry in entries)
                    choices.Add(new DialogueChoice { Token = entry, Text = DisplayName(entry) + ": " + Format(entry.BoxedValue), Enabled = true });
                choices.Add(new DialogueChoice { Token = "recenter", Text = "Recenter view and standing height", Enabled = recenter != null });
            }
            else if (selected.SettingType == typeof(float))
            {
                choices.Add(new DialogueChoice { Token = "reset", Text = "Reset to default", Enabled = true });
                choices.Add(new DialogueChoice { Token = "back", Text = "Back", Enabled = true });
            }
            else
            {
                foreach (string name in new[] { "Previous", "Next", "Reset to default", "Back" })
                    choices.Add(new DialogueChoice { Token = name, Text = name, Enabled = true });
            }
        }

        private static string DisplayName(ConfigEntryBase entry) => entry.Definition.Key;

        private static string Format(object value) => value is float f ? f.ToString("0.###") : value.ToString();

        internal void Tick(Backend backend, bool usable)
        {
            if (!usable) { Close(); return; }
            backend.Poll();
            if (menuButton.Update(backend.Menu, true, Time.unscaledTime)) Toggle();
            else if (menuButton.ShortPress && Active) Close();
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) Toggle();
            if (!Active) return;
            if (!backend.HeadPose(out var head, out var rotation)) { Close(); return; }

            bool floatSelected = selected != null && selected.SettingType == typeof(float);
            string heading = selected == null ? "Preferences"
                : floatSelected ? DisplayName(selected)
                : DisplayName(selected) + ": " + Format(selected.BoxedValue);
            string body = selected == null ? "" : floatSelected ? "\n\n\n\n" : selected.Description.Description;
            panel.Present(null, heading, body, int.MaxValue, choices, false);
            panel.Place(backend.Rig.transform, head, rotation, 1.2f, 1.3f);
            var triggerBadge = panel.Surface.Find("Trigger badge");
            if (triggerBadge) triggerBadge.gameObject.SetActive(false);
            EnsureVersionLabel();
            if (floatSelected) { EnsureSlider(); LayoutSlider(); RefreshSlider(); }
            ShowSlider(floatSelected);

            bool tracked = backend.Aim(out var position, out var aim);
            var ray = new Ray(backend.Rig.transform.TransformPoint(position), backend.Rig.transform.rotation * aim * Vector3.forward);
            int hit = panel.Point(ray, tracked);
            bool down = tracked && backend.RightTrigger > .65f;
            if (!tracked) { armed = pressed = dragging = false; pressedTarget = VrDialoguePanel.None; return; }
            if (!down) armed = true;

            bool wasDragging = dragging;
            if (floatSelected && TryPanelLocal(ray, out var local))
            {
                float y = sliderTrack.anchoredPosition.y;
                if (armed && down && !pressed && Mathf.Abs(local.y - y) <= 38 && Mathf.Abs(local.x) <= 380)
                {
                    dragging = true;
                    pressedTarget = VrDialoguePanel.None;
                }
                if (down && dragging)
                {
                    SetSliderValue(selected, Mathf.InverseLerp(-350, 350, local.x));
                    RefreshSlider();
                }
                if (!down && dragging)
                {
                    dragging = false;
                    settings.Save();
                    wasDragging = true;
                }
            }

            if (armed && down && !pressed && !dragging) pressedTarget = hit;
            if (armed && pressed && !down && !wasDragging && hit == pressedTarget)
            {
                if (!panel.ChangePage(hit) && hit >= 0) Select(hit);
            }
            pressed = down;
        }

        private bool TryPanelLocal(Ray ray, out Vector3 local)
        {
            local = default;
            var root = panel.Surface;
            var plane = new Plane(root.forward, root.position);
            if (!plane.Raycast(ray, out float distance)) return false;
            local = root.InverseTransformPoint(ray.GetPoint(distance));
            return true;
        }

        private void EnsureVersionLabel()
        {
            if (versionLabel) return;
            var go = new GameObject("Mod version", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(panel.Surface, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-22, 8); rect.sizeDelta = new Vector2(620, 24);
            versionLabel = go.GetComponent<TextMeshProUGUI>();
            var source = panel.Surface.Find("Text").GetComponent<TMP_Text>();
            versionLabel.font = source.font; versionLabel.fontSharedMaterial = source.fontSharedMaterial;
            versionLabel.fontSize = 14; versionLabel.color = DialogueStyle.Ink; versionLabel.raycastTarget = false;
            versionLabel.alignment = TextAlignmentOptions.BottomRight;
            versionLabel.text = "Drag'N Wash VR " + version;
        }

        private void EnsureSlider()
        {
            if (sliderTrack) return;
            var material = panel.Surface.GetComponentInChildren<Image>().material;
            RectTransform Box(string name, Vector2 size, Color color)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = go.GetComponent<RectTransform>(); rect.SetParent(panel.Surface, false); rect.sizeDelta = size;
                var image = go.GetComponent<Image>(); image.color = color; image.material = material; image.raycastTarget = false;
                return rect;
            }
            TMP_Text Label(string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                var rect = go.GetComponent<RectTransform>(); rect.SetParent(panel.Surface, false); rect.sizeDelta = new Vector2(880, 38);
                var label = go.GetComponent<TextMeshProUGUI>();
                var source = panel.Surface.Find("Text").GetComponent<TMP_Text>();
                label.font = source.font; label.fontSharedMaterial = source.fontSharedMaterial;
                label.fontSize = 26; label.color = DialogueStyle.Ink; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
                return label;
            }
            sliderTrack = Box("Preference slider track", new Vector2(700, 10), Color.gray);
            sliderDefault = Box("Preference slider default", new Vector2(4, 36), Color.black);
            sliderKnob = Box("Preference slider handle", new Vector2(22, 32), new Color(.1f, .4f, .7f));
            sliderValue = Label("Preference slider value");
        }

        private void LayoutSlider()
        {
            var body = panel.Surface.Find("Text").GetComponent<RectTransform>();
            float y = body.anchoredPosition.y - 4;
            sliderTrack.anchoredPosition = new Vector2(0, y);
            sliderDefault.anchoredPosition = new Vector2(SliderX(selected, Convert.ToSingle(selected.DefaultValue)), y);
            sliderKnob.anchoredPosition = new Vector2(SliderX(selected, (float)selected.BoxedValue), y);
            sliderValue.rectTransform.anchoredPosition = new Vector2(0, y + 48);
        }

        private void RefreshSlider()
        {
            if (!sliderKnob || selected == null || selected.SettingType != typeof(float)) return;
            sliderKnob.anchoredPosition = new Vector2(SliderX(selected, (float)selected.BoxedValue), sliderTrack.anchoredPosition.y);
            sliderValue.text = DisplayName(selected) + ": " + Format(selected.BoxedValue);
        }

        private void ShowSlider(bool show)
        {
            if (!sliderTrack) return;
            sliderTrack.gameObject.SetActive(show); sliderDefault.gameObject.SetActive(show);
            sliderKnob.gameObject.SetActive(show); sliderValue.gameObject.SetActive(show);
        }

        private static float SliderX(ConfigEntryBase entry, float value)
        {
            SliderRange(entry, out float min, out float max);
            return Mathf.Lerp(-350, 350, Mathf.InverseLerp(min, max, value));
        }

        internal static void SliderRange(ConfigEntryBase entry, out float min, out float max)
        {
            if (entry.Description.AcceptableValues is AcceptableValueRange<float> range)
            {
                min = range.MinValue; max = range.MaxValue; return;
            }
            if (entry.Definition.Key == "Eye Height Offset") { min = -1f; max = 1f; return; }
            float center = Convert.ToSingle(entry.DefaultValue);
            float span = Mathf.Max(1f, Mathf.Abs(center));
            min = center - span; max = center + span;
        }

        internal static void SetSliderValue(ConfigEntryBase entry, float normalized)
        {
            SliderRange(entry, out float min, out float max);
            float value = Mathf.Lerp(min, max, Mathf.Clamp01(normalized));
            float step = entry.Definition.Key.Contains("Speed") || entry.Definition.Key.Contains("Degrees") ? 1f : .01f;
            entry.BoxedValue = (float)Math.Round(value / step) * step;
        }

        internal void Select(int index)
        {
            if (selected == null)
            {
                if (index == entries.Length && recenter != null) { Close(); recenter(); return; }
                if (index < 0 || index >= entries.Length) return;
                selected = entries[index];
                if (selected.SettingType == typeof(bool))
                {
                    selected.BoxedValue = !(bool)selected.BoxedValue;
                    selected = null;
                    settings.Save();
                }
            }
            else if (selected.SettingType == typeof(float))
            {
                if (index == 0) selected.BoxedValue = selected.DefaultValue;
                else if (index == 1) selected = null;
            }
            else if (index == 3) selected = null;
            else if (index == 2) selected.BoxedValue = selected.DefaultValue;
            else Adjust(selected, index == 0 ? -1 : 1);
            settings.Save(); Refresh();
        }

        internal static void Adjust(ConfigEntryBase entry, int direction)
        {
            var type = entry.SettingType;
            if (!type.IsEnum) return;
            Array values = Enum.GetValues(type); int index = Array.IndexOf(values.Cast<object>().ToArray(), entry.BoxedValue);
            entry.BoxedValue = values.GetValue((index + direction + values.Length) % values.Length);
        }

        internal void BeginEye(Backend backend)
        {
            if (!Active || !versionLabel) return;
            var left = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            var right = Backend.Field(backend.Setup, "_rightVrCamera") as Camera;
            int mask = left && right ? left.cullingMask & right.cullingMask : 0;
            if (mask == 0) return;
            int layer = 0; while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
            panel.BeginEye(layer);
        }
        internal void EndEye() => panel.EndEye();
        internal void Close() { Active = armed = pressed = dragging = false; menuButton = default; pressedTarget = VrDialoguePanel.None; ShowSlider(false); panel.Hide(); }
        public void Dispose() { Close(); panel.Dispose(); }
    }
}
