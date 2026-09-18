using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // Render original screen-space UI, then restore it before desktop rendering/input.
    internal sealed class VrScreen : IDisposable
    {
        private GameObject root, cameraObject;
        private Canvas panel;
        private Camera capture;
        private RenderTexture texture;
        private RawImage picture;
        private Image cursor;
        private LineRenderer laser;
        private Material material, laserMaterial;
        private bool laserRequested;
        private bool anchored, pressed;
        private Vector3 anchorPosition;
        private Quaternion anchorRotation;
        private TriggerButton trigger;
        private int frame = -1;
        private Vector2 pointer;
        private bool pointed;
        private GameObject hover, down;
        private PointerEventData data;
        private EventSystem eventSystem;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private Menu sourceMenu;
        internal static bool Capturing { get; private set; }
        internal static Menu CurrentMenu => Backend.Field(AccessTools.Field(typeof(MenuManager), "_instance").GetValue(null), "_currentMenu") as Menu;
        internal static bool Wanted
        {
            get
            {
                if (QuickTests.Sample) return true;
                var menu = CurrentMenu;
                if (menu && !(menu is MenuUnpaused) && menu.isShown) return true;
                if (IntermissionFade.isRunning || WorldCurtain.TransitionRunning) return true;
                return false; // Blackout fades cover the world, not a flat UI screen.
            }
        }
        private void Create()
        {
            if (root) return;
            root = new GameObject("WalkNWash_VR_Screen", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(root);
            panel = root.GetComponent<Canvas>(); panel.renderMode = RenderMode.WorldSpace;
            panel.sortingOrder = 32000;
            material = new Material(Graphic.defaultGraphicMaterial);
            material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            picture = Child<RawImage>("Original UI", root.transform);
            picture.uvRect = new Rect(0, 0, 1, 1);
            picture.material = material; picture.raycastTarget = false;
            cursor = Child<Image>("Pointer", root.transform);
            cursor.material = material; cursor.color = Color.cyan; cursor.raycastTarget = false;
            cursor.rectTransform.sizeDelta = new Vector2(16, 16);
            var beam = new GameObject("Controller ray", typeof(LineRenderer));
            beam.transform.SetParent(root.transform, false);
            laser = beam.GetComponent<LineRenderer>();
            laser.positionCount = 2; laser.useWorldSpace = true;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            laser.receiveShadows = false;
            laser.sortingLayerID = panel.sortingLayerID;
            laser.sortingOrder = panel.sortingOrder - 1;
            // Use the UI shader so the depth override works, but render one queue
            // before the panel so UI backgrounds/text composite over the beam.
            laserMaterial = new Material(Graphic.defaultGraphicMaterial);
            laserMaterial.mainTexture = Texture2D.whiteTexture;
            laserMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 1;
            laserMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            laser.sharedMaterial = laserMaterial;
            laser.startColor = laser.endColor = Color.cyan;
            cameraObject = new GameObject("WalkNWash_UI_Capture", typeof(Camera));
            UnityEngine.Object.DontDestroyOnLoad(cameraObject);
            capture = cameraObject.GetComponent<Camera>(); capture.enabled = false;
            // UI capture needs an RGBA path, never HDR/RGB-only intermediate buffers.
            capture.allowHDR = false; capture.allowMSAA = false;
            var extraType = AccessTools.TypeByName("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (extraType != null)
            {
                var extra = cameraObject.AddComponent(extraType);
                AccessTools.Property(extraType, "renderPostProcessing")?.SetValue(extra, false);
                AccessTools.Property(extraType, "allowXRRendering")?.SetValue(extra, false);
            }
            capture.orthographic = true; capture.orthographicSize = 5;
            capture.transform.position = new Vector3(0, -10000, 0);
            // Native UI composites over this backing: transparent regions retain
            // 20% opacity while opaque images/text remain opaque.
            capture.clearFlags = CameraClearFlags.SolidColor; capture.backgroundColor = new Color(0, 0, 0, .2f);
            capture.cullingMask = 1 << 31; capture.nearClipPlane = .01f; capture.farClipPlane = 10;
            EndEye();
        }
        private static T Child<T>(string name, Transform parent) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            go.transform.SetParent(parent, false);
            return go.GetComponent<T>();
        }
        internal void BeginEye(Backend backend, bool allowed)
        {
            if (!allowed || !Wanted || !backend.Rig) { Reset(); return; }
            Create();
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                if (sourceMenu != CurrentMenu) { CancelInput(); sourceMenu = CurrentMenu; }
                Capture();
                if (!backend.HeadPose(out var head, out var rotation)) { Reset(); return; }
                if (!anchored)
                {
                    // Keep the screen upright at eye height, including after F10.
                    anchorRotation = Quaternion.Euler(0, rotation.eulerAngles.y, 0);
                    anchorPosition = head + anchorRotation * Vector3.forward * 1.8f;
                    anchored = true; sourceMenu = CurrentMenu;
                }
                root.transform.SetPositionAndRotation(backend.Rig.transform.TransformPoint(anchorPosition), backend.Rig.transform.rotation * anchorRotation);
                root.transform.localScale = Vector3.one * (4.4f * backend.Rig.transform.localScale.x / Screen.width);
                bool tracked = backend.Aim(out var hand, out var aim); // Aim also checks runtime focus.
                UpdatePointer(new Ray(backend.Rig.transform.TransformPoint(hand), backend.Rig.transform.rotation * aim * Vector3.forward),
                    tracked, backend.Rig.transform.lossyScale.x);
            }
            var left = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            var right = Backend.Field(backend.Setup, "_rightVrCamera") as Camera;
            int mask = left && right ? left.cullingMask & right.cullingMask : 0;
            if (mask == 0) { EndEye(); return; }
            int layer = 0; while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
            Show(layer);
        }
        internal void Show(int layer)
        {
            root.layer = picture.gameObject.layer = cursor.gameObject.layer = laser.gameObject.layer = layer;
            panel.enabled = true;
            laser.enabled = laserRequested;
        }
        internal void UpdatePointer(Ray ray, bool tracked, float scale)
        {
            laserRequested = tracked;
            pointed = tracked && Point(ray, out pointer);
            cursor.enabled = pointed;
            if (!tracked) { laser.enabled = false; return; }
            Vector3 end = ray.GetPoint(2 * scale);
            if (pointed)
            {
                Vector2 local = pointer - new Vector2(Screen.width, Screen.height) / 2;
                cursor.rectTransform.anchoredPosition = local;
                end = root.transform.TransformPoint(new Vector3(local.x, local.y, 0));
            }
            laser.startWidth = laser.endWidth = .003f * scale;
            laser.SetPosition(0, ray.origin); laser.SetPosition(1, end);
        }
        internal bool Point(Ray ray, out Vector2 pixels)
        {
            pixels = default;
            if (!root || !new Plane(root.transform.forward, root.transform.position).Raycast(ray, out float distance) || distance < 0) return false;
            Vector3 local = root.transform.InverseTransformPoint(ray.GetPoint(distance));
            pixels = new Vector2(local.x + Screen.width / 2f, local.y + Screen.height / 2f);
            return pixels.x >= 0 && pixels.y >= 0 && pixels.x <= Screen.width && pixels.y <= Screen.height;
        }
        private sealed class CanvasState
        {
            internal Canvas Canvas; internal RenderMode Mode; internal Camera Camera; internal float Distance;
            internal Vector3 Position, Scale; internal Quaternion Rotation;
            internal Vector2 Min, Max, Pivot, Size, Anchored;
            internal CanvasState(Canvas c)
            {
                Canvas = c; Mode = c.renderMode; Camera = c.worldCamera; Distance = c.planeDistance;
                var t = (RectTransform)c.transform;
                Position = t.localPosition; Rotation = t.localRotation; Scale = t.localScale;
                Min = t.anchorMin; Max = t.anchorMax; Pivot = t.pivot; Size = t.sizeDelta; Anchored = t.anchoredPosition;
            }
            internal void Restore()
            {
                if (!Canvas) return;
                Canvas.renderMode = Mode; Canvas.worldCamera = Camera; Canvas.planeDistance = Distance;
                var t = (RectTransform)Canvas.transform;
                t.anchorMin = Min; t.anchorMax = Max; t.pivot = Pivot; t.sizeDelta = Size;
                t.anchoredPosition = Anchored; t.localPosition = Position; t.localRotation = Rotation; t.localScale = Scale;
            }
        }
        internal void Capture()
        {
            Create(); EndEye();
            int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            if (!texture || texture.width != width || texture.height != height)
            {
                if (texture) { capture.targetTexture = null; texture.Release(); UnityEngine.Object.Destroy(texture); }
                texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); texture.Create();
                capture.targetTexture = texture; picture.texture = texture;
            }
            root.GetComponent<RectTransform>().sizeDelta = picture.rectTransform.sizeDelta = new Vector2(width, height);
            var saved = new List<CanvasState>(); var layers = new Dictionary<GameObject, int>();
            try
            {
                foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!c.isRootCanvas || !c.isActiveAndEnabled || c.renderMode == RenderMode.WorldSpace) continue;
                    saved.Add(new CanvasState(c));
                    foreach (var t in c.GetComponentsInChildren<Transform>(true))
                    { if (!layers.ContainsKey(t.gameObject)) layers.Add(t.gameObject, t.gameObject.layer); t.gameObject.layer = 31; }
                    c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = capture; c.planeDistance = 1;
                }
                Canvas.ForceUpdateCanvases();
                Capturing = true;
                try { capture.Render(); }
                finally { Capturing = false; }
            }
            finally
            {
                foreach (var s in saved) s.Restore();
                foreach (var pair in layers) if (pair.Key) pair.Key.layer = pair.Value;
                Canvas.ForceUpdateCanvases();
            }
        }
        // Native UI events run once in Update, never once per eye or during rendering.
        internal void Dispatch(Backend backend, bool allowed)
        {
            if (!allowed || !Wanted || !backend.Focused || !anchored || sourceMenu != CurrentMenu || !sourceMenu || sourceMenu is MenuUnpaused || IntermissionFade.isRunning || !pointed || frame < Time.frameCount - 1)
            { CancelInput(); return; }
            backend.Poll();
            bool held = trigger.Update(backend.RightTrigger, true);
            Send(pointer, pointed && frame >= Time.frameCount - 1, held);
        }
        internal void Send(Vector2 position, bool valid, bool held)
        {
            var system = EventSystem.current;
            if (!system) { CancelInput(); return; }
            if (data == null || eventSystem != system)
            { CancelInput(); eventSystem = system; data = new PointerEventData(system) { pointerId = -100, button = PointerEventData.InputButton.Left }; }
            data.delta = position - data.position; data.position = position;
            data.pointerCurrentRaycast = default;
            hits.Clear(); if (valid) system.RaycastAll(data, hits);
            GameObject target = null;
            foreach (var hit in hits) if (hit.module is GraphicRaycaster) { target = hit.gameObject; data.pointerCurrentRaycast = hit; break; }
            if (hover != target)
            {
                if (hover) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerExitHandler);
                hover = target;
                if (hover) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerEnterHandler);
            }
            if (held && !pressed && target)
            {
                data.pressPosition = position; data.pointerPressRaycast = data.pointerCurrentRaycast;
                down = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler)
                    ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
                data.pointerPress = down; data.rawPointerPress = target;
                system.SetSelectedGameObject(ExecuteEvents.GetEventHandler<ISelectHandler>(target), data);
                data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
                if (data.pointerDrag) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
            }
            if (held && down && data.pointerDrag && (data.dragging || !data.useDragThreshold
                || (position - data.pressPosition).sqrMagnitude > system.pixelDragThreshold * system.pixelDragThreshold))
            {
                if (!data.dragging) { ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler); data.dragging = true; }
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
            }
            if (!held && pressed) Release(target && down == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target));
            pressed = held;
        }
        private void Release(bool click)
        {
            if (data != null)
            {
                if (down) { ExecuteEvents.Execute(down, data, ExecuteEvents.pointerUpHandler); if (click && !data.dragging) ExecuteEvents.Execute(down, data, ExecuteEvents.pointerClickHandler); }
                if (data.dragging && data.pointerDrag) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
                data.dragging = false; data.pointerPress = data.pointerDrag = null;
            }
            down = null;
        }
        internal void EndEye() { if (panel) panel.enabled = false; if (laser) laser.enabled = false; }
        internal void Recenter() { anchored = false; }
        private void CancelInput()
        {
            Release(false);
            if (hover && data != null) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerExitHandler);
            hover = null; pressed = false; trigger = new TriggerButton();
        }
        internal void Reset()
        {
            CancelInput(); pointed = anchored = laserRequested = false; frame = -1; EndEye();
        }
        public void Dispose()
        {
            Reset(); if (capture) capture.targetTexture = null;
            if (texture) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(cameraObject); UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(laserMaterial);
        }
    }
}
