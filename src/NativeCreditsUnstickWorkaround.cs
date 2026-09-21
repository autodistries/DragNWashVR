using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WalkNWash.VRCompanion
{
    // TEMPORARY NATIVE-GAME BUG WORKAROUND — deliberately isolated in this file.
    //
    // Current game behavior:
    // MenuUnpaused.OnShow(Unstick) calls WalkNWashSceneState.UnstickPlayer().
    // UnstickPlayer dereferences its static `instance` without checking whether a
    // gameplay WalkNWashSceneState exists. EndScene is the credits scene and can
    // have no such gameplay state, so the pause-menu Unstick action can throw and
    // leave menu input half-transitioned.
    //
    // Removal rule: when the game adds its own null/destroyed-instance guard (or
    // otherwise makes Unstick safe outside gameplay), delete this entire file.
    // The permanent VR credits rendering support in VrScreen does not depend on it.
    internal static class NativeCreditsUnstickWorkaround
    {
        private static readonly Harmony patches = new Harmony(Plugin.Id + ".native-credits-unstick-workaround");
        private static readonly FieldInfo sceneInstance = AccessTools.Field(typeof(WalkNWashSceneState), "instance");

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Initialize()
        {
            try
            {
                var unstick = AccessTools.Method(typeof(WalkNWashSceneState), nameof(WalkNWashSceneState.UnstickPlayer));
                if (unstick == null || sceneInstance == null) return;
                patches.Patch(unstick, prefix: new HarmonyMethod(typeof(NativeCreditsUnstickWorkaround), nameof(BeforeUnstickPlayer)));
            }
            catch (Exception e)
            {
                // This workaround must never make the companion fail to load if a
                // future game update changes or removes the affected native method.
                Console.Error.WriteLine("Walk N Wash credits Unstick workaround unavailable: " + e.Message);
            }
        }

        internal static bool ShouldRunNative(string sceneName, bool hasSceneState)
            => !VrScreen.IsCreditsScene(sceneName) || hasSceneState;

        private static bool BeforeUnstickPlayer()
        {
            var state = sceneInstance.GetValue(null) as WalkNWashSceneState;
            bool hasSceneState = state;
            return ShouldRunNative(SceneManager.GetActiveScene().name, hasSceneState);
        }
    }
}
