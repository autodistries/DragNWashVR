using System;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    // The game's UiPrompt projects into desktop pixels. Render a separate label
    // in world space, only during the two manually rendered VR eye passes.
    internal sealed class VrPrompt : IDisposable
    {
        private GameObject root;
        private TextMesh text;
        private MeshRenderer mesh;
        private bool requested;
        private Vector3 target;

        internal void Show(Vector3 position) { target = position + Vector3.up * .3f; requested = true; }
        internal void Hide() { requested = false; EndEye(); }

        private void Create()
        {
            if (root != null) return;
            root = new GameObject("WalkNWash_VR_InteractionPrompt");
            UnityEngine.Object.DontDestroyOnLoad(root);
            text = root.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) throw new InvalidOperationException("Built-in prompt font unavailable");
            text.fontSize = 64;
            text.characterSize = .0175f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            mesh = root.GetComponent<MeshRenderer>();
            mesh.sharedMaterial = text.font.material;
            mesh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mesh.receiveShadows = false;
            mesh.enabled = false;
        }

        internal void BeginEye(Backend backend, bool visible, bool triggers)
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
            root.layer = layer;
            root.transform.SetPositionAndRotation(target, facing);
            root.transform.localScale = Vector3.one * Mathf.Clamp(delta.magnitude, .6f, 3f);
            text.text = (triggers ? "Right trigger" : "Left mouse") +
                (interactable.interactsWithHand ? "\nUse hand" : "\nInteract");
            mesh.enabled = true;
        }

        internal void EndEye() { if (mesh != null) mesh.enabled = false; }
        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            mesh = null;
            text = null;
        }
    }
}
