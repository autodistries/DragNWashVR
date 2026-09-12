using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using com.gatordragongames.washnwalk.tools;

namespace WalkNWash.VRCompanion
{
    internal struct HudReading
    {
        internal string Label;
        internal float Value;
        internal Color Color;
    }

    internal sealed class HudSource
    {
        private UiProgressBars source;
        private float nextSearch;
        private static readonly FieldInfo toolManager = AccessTools.Field(typeof(ToolManager), "_instance");
        private static readonly string[] fields = { "cleanBar", "soapBar", "talkBar", "debrisBar", "bandagesBar", "HandjobBar" };
        private static readonly string[] labels = { "Cleanliness", "Soap coverage", "Conversation ready", "Debris removed", "Bandaging", "Hand stimulation" };
        internal readonly List<HudReading> Readings = new List<HudReading>();

        internal void Read()
        {
            Readings.Clear();
            if (source == null && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + .5f;
                source = UnityEngine.Object.FindFirstObjectByType<UiProgressBars>();
            }
            // The desktop group slides offscreen outside washing. Match its
            // intended visibility instead of showing stale values in other scenes.
            if (source != null && source.isActiveAndEnabled
                && WalkNWashSceneState.GetDragonState() == WalkNWashSceneState.DragonState.WaitingOnBeingWashed)
                AppendBars(source, Readings);

            var manager = toolManager.GetValue(null);
            if (manager != null)
            {
                var tool = Backend.Field(manager, "_currentTool") as Tool;
                if (tool != null && tool.GetModel() is ToolModelSponge sponge && sponge.isActiveAndEnabled)
                    Readings.Add(new HudReading { Label = "Sponge supply", Value = Mathf.Clamp01(Convert.ToSingle(Backend.Field(sponge, "fillAmount"))), Color = new Color(.3f, .8f, 1) });
            }
        }

        internal static void AppendBars(UiProgressBars source, List<HudReading> output)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                var bar = Backend.Field(source, fields[i]) as UiProgressBar;
                if (bar == null || !bar.isActiveAndEnabled) continue;
                var panel = Backend.Field(bar, "panel") as GameObject;
                if (panel == null || !panel.activeInHierarchy) continue;
                output.Add(new HudReading { Label = labels[i],
                    Value = Mathf.Clamp01(Convert.ToSingle(Backend.Field(bar, "_progress"))),
                    Color = i == 1 ? new Color(.4f, .8f, 1) : i == 2 ? new Color(1, .8f, .35f) : new Color(.3f, .85f, .65f) });
            }
        }
    }

    // A small binocular HUD in head space. The same surface is rendered by both
    // eyes, so it stays upper-left without independent per-eye depth placement.
    internal sealed class VrHud : IDisposable
    {
        private readonly HudSource source = new HudSource();
        private GameObject root;
        private Canvas canvas;
        private Image background;
        private readonly List<GameObject> rows = new List<GameObject>();
        private readonly List<TMP_Text> labels = new List<TMP_Text>();
        private readonly List<Image> fills = new List<Image>();
        private Material material, fontMaterial;
        private TMP_FontAsset font;
        private int frame = -1;
        private bool displayed;

        private static RectTransform Rect(GameObject go, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }
        private Image Box(string name, Transform parent, Vector2 size, Vector2 position, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Rect(go, parent, size, position);
            var image = go.GetComponent<Image>();
            image.material = material;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
        private void Create()
        {
            if (root != null) return;
            Dispose();
            font = TMP_Settings.defaultFontAsset;
            if (font == null) throw new InvalidOperationException("HUD font unavailable");
            material = new Material(Graphic.defaultGraphicMaterial);
            material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            fontMaterial = new Material(font.material);
            fontMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            root = new GameObject("WalkNWash_VR_Progress", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(root);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 31900;
            background = Box("Background", root.transform, new Vector2(360, 50), Vector2.zero, new Color(.02f, .03f, .055f, .82f));
            EndEye();
        }
        internal void Present(IList<HudReading> readings)
        {
            if (readings.Count == 0) { displayed = false; EndEye(); return; }
            Create();
            displayed = true;
            float height = readings.Count * 54 + 20;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(360, height);
            background.rectTransform.sizeDelta = new Vector2(360, height);
            for (int i = 0; i < readings.Count; i++)
            {
                if (i == rows.Count)
                {
                    var row = new GameObject("Status " + i, typeof(RectTransform));
                    Rect(row, root.transform, new Vector2(330, 50), Vector2.zero);
                    rows.Add(row);
                    var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                    Rect(label, row.transform, new Vector2(330, 30), new Vector2(0, 9));
                    var text = label.GetComponent<TextMeshProUGUI>();
                    text.font = font;
                    text.fontSharedMaterial = fontMaterial;
                    text.fontSize = 24;
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 18;
                    text.fontSizeMax = 24;
                    text.alignment = TextAlignmentOptions.MidlineLeft;
                    text.raycastTarget = false;
                    labels.Add(text);
                    Box("Track", row.transform, new Vector2(330, 9), new Vector2(0, -13), new Color(.18f, .23f, .28f));
                    var fill = Box("Fill", row.transform, new Vector2(330, 9), new Vector2(-165, -13), Color.white);
                    fill.rectTransform.pivot = new Vector2(0, .5f);
                    fills.Add(fill);
                }
                rows[i].SetActive(true);
                rows[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(0, height / 2 - 35 - i * 54);
                float value = readings[i].Value;
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);
                labels[i].text = readings[i].Label + "  " + Mathf.RoundToInt(value * 100) + "%";
                fills[i].rectTransform.sizeDelta = new Vector2(330 * value, 9);
                fills[i].color = readings[i].Color;
            }
            for (int i = readings.Count; i < rows.Count; i++) rows[i].SetActive(false);
        }
        internal void Place(Transform rig, Vector3 head, Quaternion rotation, float scale, float horizontal, float vertical)
        {
            root.transform.SetParent(rig, false);
            float unit = .001f * scale;
            float height = root.GetComponent<RectTransform>().rect.height;
            // Settings locate the upper-left edge; rows grow downwards.
            root.transform.localPosition = head + rotation * new Vector3(horizontal + 180 * unit, vertical - height * unit / 2, 1.2f);
            root.transform.localRotation = rotation;
            root.transform.localScale = Vector3.one * unit;
        }
        internal void BeginEye(Backend backend, bool visible, float scale, float horizontal, float vertical)
        {
            if (!visible || backend.Rig == null) { EndEye(); return; }
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                source.Read();
                Present(source.Readings);
            }
            if (!displayed || !backend.HeadPose(out var head, out var rotation)) { EndEye(); return; }
            Place(backend.Rig.transform, head, rotation, scale, horizontal, vertical);
            var left = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            var right = Backend.Field(backend.Setup, "_rightVrCamera") as Camera;
            int mask = left != null && right != null ? left.cullingMask & right.cullingMask : 0;
            if (mask == 0) return;
            int layer = 0;
            while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
            Show(layer);
        }
        internal void Show(int layer)
        {
            if (!displayed || root == null) return;
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = layer;
            canvas.enabled = true;
            Canvas.ForceUpdateCanvases();
        }
        internal void EndEye() { if (canvas != null) canvas.enabled = false; }
        internal void Reset() { EndEye(); frame = -1; displayed = false; }
        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (fontMaterial != null) UnityEngine.Object.Destroy(fontMaterial);
            root = null; rows.Clear(); labels.Clear(); fills.Clear();
        }
    }
}
