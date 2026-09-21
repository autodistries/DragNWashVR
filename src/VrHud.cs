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
        internal bool ShowPercentage;
        internal float Value;
        internal Color Color;
        internal Color? Background;
    }

    internal sealed class HudSource
    {
        private UiProgressBars source;
        private float nextSearch;
        private static readonly FieldInfo toolManager = AccessTools.Field(typeof(ToolManager), "_instance");
        private static readonly string[] fields = { "cleanBar", "soapBar", "talkBar", "debrisBar", "bandagesBar", "HandjobBar" };
        internal readonly List<HudReading> Readings = new List<HudReading>();

        internal void Read(bool showSpongeSupply = false)
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
            if (showSpongeSupply && manager != null)
            {
                var tool = Backend.Field(manager, "_currentTool") as Tool;
                if (tool != null && tool.GetModel() is ToolModelSponge sponge && sponge.isActiveAndEnabled)
                    Readings.Add(new HudReading { Label = "Sponge supply", ShowPercentage = true, Value = Mathf.Clamp01(Convert.ToSingle(Backend.Field(sponge, "fillAmount"))), Color = new Color(.3f, .8f, 1) });
            }
        }

        private static Color BarColor(GameObject panel, string property, Color fallback)
        {
            var image = panel.GetComponent<Image>();
            var material = image ? image.material : null;
            if (!material || !material.HasProperty(property)) return fallback;
            return VisibleBarColor(material.GetColor(property));
        }

        // Native progress shaders ignore color alpha (the shipped colors use 0).
        // UI vertex alpha instead hides the fill and exposes the white border.
        internal static Color VisibleBarColor(Color color)
        {
            color.a = 1f;
            return color;
        }

        internal static void AppendBars(UiProgressBars source, List<HudReading> output)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                var bar = Backend.Field(source, fields[i]) as UiProgressBar;
                if (bar == null || !bar.isActiveAndEnabled) continue;
                var panel = Backend.Field(bar, "panel") as GameObject;
                if (panel == null || !panel.activeInHierarchy) continue;
                var text = bar.GetComponentInChildren<TMP_Text>(true);
                var legacyText = text == null ? bar.GetComponentInChildren<Text>(true) : null;
                output.Add(new HudReading { Label = text != null ? text.text : legacyText != null ? legacyText.text : string.Empty,
                    Value = Mathf.Clamp01(Convert.ToSingle(Backend.Field(bar, "_progress"))),
                    Color = BarColor(panel, "_FillColor", Color.white),
                    Background = BarColor(panel, "_BackgroundColor", new Color(.28f, .23f, .19f, .9f)) });
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
        private readonly List<Image> tracks = new List<Image>();
        private readonly List<Image> fills = new List<Image>();
        private Material material, fontMaterial;
        private TMP_FontAsset font;
        private int frame = -1, poseFrame = -1;
        private bool displayed, placed;
        private const float FollowResponse = 18f;

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
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudBarShape));
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
            fontMaterial.SetColor("_OutlineColor", Color.black);
            fontMaterial.SetFloat("_OutlineWidth", .2f);
            fontMaterial.EnableKeyword("OUTLINE_ON");
            root = new GameObject("WalkNWash_VR_Progress", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(root);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 31900;
            background = Box("Background", root.transform, new Vector2(360, 50), Vector2.zero, Color.clear);
            EndEye();
        }
        internal void Present(IList<HudReading> readings)
        {
            if (readings.Count == 0) { displayed = placed = false; EndEye(); return; }
            Create();
            displayed = true;
            float height = readings.Count * 38 + 12;
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
                    Rect(label, row.transform, new Vector2(320, 28), Vector2.zero);
                    var text = label.GetComponent<TextMeshProUGUI>();
                    text.font = font;
                    text.fontSharedMaterial = fontMaterial;
                    text.fontSize = 21;
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 18;
                    text.fontSizeMax = 21;
                    text.alignment = TextAlignmentOptions.Center;
                    text.raycastTarget = false;
                    labels.Add(text);
                    Box("Border", row.transform, new Vector2(330, 30), Vector2.zero, Color.white);
                    tracks.Add(Box("Track", row.transform, new Vector2(326, 26), Vector2.zero, new Color(.28f, .23f, .19f, .9f)));
                    var fill = Box("Fill", row.transform, new Vector2(326, 26), new Vector2(-163, 0), Color.white);
                    fill.rectTransform.pivot = new Vector2(0, .5f);
                    label.transform.SetAsLastSibling();
                    fills.Add(fill);
                }
                rows[i].SetActive(true);
                rows[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(0, height / 2 - 21 - i * 38);
                float value = readings[i].Value;
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);
                labels[i].text = readings[i].Label + (readings[i].ShowPercentage ? "  " + Mathf.RoundToInt(value * 100) + "%" : string.Empty);
                fills[i].rectTransform.sizeDelta = new Vector2(326 * value, 26);
                fills[i].color = readings[i].Color;
                tracks[i].color = readings[i].Background ?? new Color(.28f, .23f, .19f, .9f);
            }
            for (int i = readings.Count; i < rows.Count; i++) rows[i].SetActive(false);
        }
        internal static float FollowBlend(float deltaTime)
        {
            float dt = Mathf.Clamp(deltaTime, 1f / 240f, .05f);
            return 1f - Mathf.Exp(-FollowResponse * dt);
        }

        internal void Place(Transform rig, Vector3 head, Quaternion rotation, float scale, float horizontal, float vertical)
        {
            if (root.transform.parent != rig)
            {
                root.transform.SetParent(rig, false);
                placed = false;
            }
            float unit = .001f * scale;
            float height = root.GetComponent<RectTransform>().rect.height;
            // Settings locate the upper-left edge; rows grow downwards.
            Vector3 targetPosition = head + rotation * new Vector3(horizontal + 180 * unit, vertical - height * unit / 2, 1.2f);
            if (!placed)
            {
                root.transform.localPosition = targetPosition;
                root.transform.localRotation = rotation;
            }
            else
            {
                float blend = FollowBlend(Time.unscaledDeltaTime);
                root.transform.localPosition = Vector3.Lerp(root.transform.localPosition, targetPosition, blend);
                root.transform.localRotation = Quaternion.Slerp(root.transform.localRotation, rotation, blend);
            }
            root.transform.localScale = Vector3.one * unit;
            placed = true;
        }
        internal void BeginEye(Backend backend, bool visible, float scale, float horizontal, float vertical, bool showSpongeSupply = false)
        {
            if (!visible || backend.Rig == null) { placed = false; poseFrame = -1; EndEye(); return; }
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                source.Read(showSpongeSupply);
                Present(source.Readings);
            }
            if (!displayed || !backend.HeadPose(out var head, out var rotation)) { placed = false; poseFrame = -1; EndEye(); return; }
            // Stereo eye callbacks can both run in one game frame. Advance smoothing
            // once per frame so the response does not depend on the number of eyes.
            if (poseFrame != Time.frameCount)
            {
                poseFrame = Time.frameCount;
                Place(backend.Rig.transform, head, rotation, scale, horizontal, vertical);
            }
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
        internal void Reset() { EndEye(); frame = poseFrame = -1; displayed = placed = false; }
        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (fontMaterial != null) UnityEngine.Object.Destroy(fontMaterial);
            root = null; frame = poseFrame = -1; displayed = placed = false; rows.Clear(); labels.Clear(); fills.Clear(); tracks.Clear();
        }
    }
}
