using System;
using HarmonyLib;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal static class OpenVrTargets
    {
        // The mod releases eye textures in TeardownCameraRig but only allocates
        // them during InitializeVr. Rig-only F11/scene restarts skip InitializeVr.
        internal static bool Ensure(object setup)
        {
            var left = Backend.Field(setup, "_leftEyeTexture") as RenderTexture;
            var right = Backend.Field(setup, "_rightEyeTexture") as RenderTexture;
            if (left && right && left.IsCreated() && right.IsCreated()) return false;
            if (!(bool)AccessTools.Method(setup.GetType(), "SetupRenderTargets").Invoke(setup, null))
                throw new InvalidOperationException("Could not recreate OpenVR eye render textures");
            return true;
        }
    }
}
