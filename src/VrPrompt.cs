using System;
using UnityEngine;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // The game's UiPrompt projects into desktop pixels. Render a separate label
    // in world space, only during the two manually rendered VR eye passes.
    internal sealed class VrPrompt : IDisposable
    {
        private GameObject root;
        private Canvas canvas;
        private TriggerBadge badge;
        private Material material;
        private bool requested;
        private Vector3 target;

        internal void Show(Vector3 position) { target = position + Vector3.up * .3f; requested = true; }
        internal void Hide() { requested = false; EndEye(); }

        private void Create()
        {
            if (root != null) return;
            root = new GameObject("WalkNWash_VR_InteractionPrompt", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(root);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true; canvas.sortingOrder = 31999;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 68);
            material = new Material(Graphic.defaultGraphicMaterial);
            material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            badge = TriggerBadge.Create(root.transform, material, "LT", 120, 68);
            canvas.enabled = false;
        }

        internal void BeginEye(Backend backend, bool visible, bool triggers, int freeHand = 1)
        {
            EndEye();
            if (!visible || !requested || backend.Rig == null) return;
            // Reject stale prompts across scene changes or disabled interactables.
            var interactable = Interactable.GetCachedInteractable();
            if (interactable == null || !interactable.isActiveAndEnabled) return;
            if (!backend.HeadPose(out var position, out var rotation)) return;
            Create();
            Transform rig = backend.Rig.transform;
            Vector3 head = rig.TransformPoint(position);
            Quaternion facing = rig.rotation * rotation;
            Vector3 delta = target - head;
            if (Vector3.Dot(facing * Vector3.forward, delta) <= 0 || delta.sqrMagnitude < .01f) return;
            var camera = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            if (camera == null) return;
            int mask = camera.cullingMask;
            if (mask == 0) return;
            int layer = 0;
            while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            root.transform.SetPositionAndRotation(target, facing);
            root.transform.localScale = Vector3.one * Mathf.Clamp(delta.magnitude, .6f, 3f) * .001f;
            badge.SetCaption(triggers ? (freeHand == 1 ? "LT" : "RT") : "LMB");
            canvas.enabled = true;
            Canvas.ForceUpdateCanvases();
        }

        internal void EndEye() { if (canvas != null) canvas.enabled = false; }
        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null; canvas = null; badge = null;
        }
    }
}
