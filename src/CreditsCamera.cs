using UnityEngine;
using UnityEngine.SceneManagement;

namespace WalkNWash.VRCompanion
{
    internal static class CreditsCamera
    {
        // EndScene's serialized camera is enabled but Untagged. UnityVRMod's
        // normal selector only considers configured overrides and Camera.main.
        internal static Camera Resolve(Camera selected)
        {
            if (selected || !VrScreen.CreditsScene) return selected;
            var scene = SceneManager.GetActiveScene();
            foreach (var camera in Camera.allCameras)
            {
                // Only native on-screen cameras in this scene. Disabled eye/capture
                // cameras and cameras rendering to textures cannot bootstrap the rig.
                if (camera && camera.isActiveAndEnabled && camera.gameObject.scene == scene && !camera.targetTexture)
                    return camera;
            }
            return null;
        }
    }
}
