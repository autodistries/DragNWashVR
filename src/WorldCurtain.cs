using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // Covers the entire binocular view, behind VR UI but above scene geometry.
    internal sealed class WorldCurtain : IDisposable
    {
        private GameObject root;
        private Canvas canvas;
        private Image image;
        private Material material;
        private static Animator transitionAnimator;
        private static string transitionIdle;
        internal static void TrackTransition(IntermissionFade source)
        {
            transitionAnimator = Backend.Field(source, "_animator") as Animator;
            transitionIdle = (string)Backend.Field(source, "_idleState");
        }
        internal static bool TransitionRunning
        {
            get
            {
                if (!transitionAnimator || !transitionAnimator.isActiveAndEnabled) return false;
                if (transitionAnimator.IsInTransition(0) || !transitionAnimator.GetCurrentAnimatorStateInfo(0).IsName(transitionIdle)) return true;
                transitionAnimator = null;
                return false;
            }
        }

        internal static float Opacity
        {
            get
            {
                if (QuickTests.Mask) return 1;
                // MenuManager can briefly retain a managed reference to a Unity-destroyed
                // loading menu while scenes swap. Check Unity object validity before
                // touching its serialized state so scene transitions cannot disable VR.
                var menu = VrScreen.CurrentMenu;
                if ((menu && menu is MenuLoading loading && loading.isShown) || IntermissionFade.isRunning || TransitionRunning) return 1;
                var fader = AccessTools.Field(typeof(BlackoutFader), "_instance").GetValue(null) as BlackoutFader;
                var group = fader ? Backend.Field(fader, "canvasGroup") as CanvasGroup : null;
                return group && group.isActiveAndEnabled ? Mathf.Clamp01(group.alpha) : 0;
            }
        }
        internal void BeginEye(Backend backend, bool allowed)
        {
            EndEye();
            float alpha = allowed ? Opacity : 0;
            if (alpha <= 0 || !backend.Rig || !backend.HeadPose(out var head, out var rotation)) return;
            if (!root)
            {
                root = new GameObject("WalkNWash_WorldCurtain", typeof(RectTransform), typeof(Canvas), typeof(CanvasRenderer), typeof(Image));
                UnityEngine.Object.DontDestroyOnLoad(root);
                canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true; canvas.sortingOrder = 31800;
                material = new Material(Graphic.defaultGraphicMaterial);
                material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                image = root.GetComponent<Image>(); image.material = material; image.raycastTarget = false;
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 100);
            }
            var left = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            var right = Backend.Field(backend.Setup, "_rightVrCamera") as Camera;
            int mask = left && right ? left.cullingMask & right.cullingMask : 0;
            if (mask == 0) return;
            int layer = 0; while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
            root.layer = layer;
            var rig = backend.Rig.transform;
            root.transform.SetPositionAndRotation(rig.TransformPoint(head) + rig.rotation * rotation * Vector3.forward, rig.rotation * rotation);
            image.color = new Color(0, 0, 0, alpha); canvas.enabled = true;
        }
        internal void EndEye() { if (canvas) canvas.enabled = false; }
        public void Dispose() { UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(material); }
    }
}
