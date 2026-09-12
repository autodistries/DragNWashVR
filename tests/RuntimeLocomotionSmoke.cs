using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal static class RuntimeLocomotionSmoke
    {
        // Mimics the mod's cached pose/session fields, never opens a native VR session.
        private sealed class TestOpenXR
        {
            public GameObject _vrRig;
            public int _currentSessionState = 5;
            public ViewState _locatedViewState = new ViewState();
            public View[] _locatedViews = { new View(), new View() };
            public ulong _appSpace = 1;
            public FrameState _xrFrameState = new FrameState();
        }
        private sealed class FrameState { public long predictedDisplayTime = 1; }
        private sealed class ViewState { public ulong viewStateFlags = 3; }
        private sealed class View { public Pose pose = new Pose(); }
        private sealed class Pose { public Position position = new Position(); public Rotation orientation = new Rotation(); }
        private sealed class Position { public float x = 0, y = 2, z = 0; }
        private sealed class Rotation { public float x = 0, y = 0, z = 0, w = 1; }
        private sealed class Manager { public bool _isUserSafeModeActive = false; public float _autoSafeModeEndTime = 0; }
        private static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);

        internal static void Run(Action<bool, string> check)
        {
            var plugin = (Plugin)AccessTools.Field(typeof(Plugin), "current").GetValue(null);
            var saved = new Dictionary<string, object>();
            foreach (string field in new[] { "backend", "manager", "player", "look", "locomotion", "calibrated", "calibratedRig", "crouch", "rigYaw", "headRotation", "snapLatched" })
                saved[field] = Backend.Field(plugin, field);
            var headAim = (ConfigEntry<bool>)Backend.Field(plugin, "headAim");
            bool previousAim = headAim.Value;
            var heightCrouch = (ConfigEntry<bool>)Backend.Field(plugin, "physicalCrouch");
            bool previousHeight = heightCrouch.Value;
            var threshold = (ConfigEntry<float>)Backend.Field(plugin, "crouchThreshold");
            float previousThreshold = threshold.Value;
            var oldLook = AccessTools.Field(typeof(LookController), "instance").GetValue(null);
            var gameState = GameStateManager.Instance;
            bool paused = gameState != null && gameState.IsPaused;
            bool moveEnabled = MenuManager.actions.Player.Move.enabled;
            bool lookEnabled = MenuManager.actions.Player.Look.enabled;
            bool crouchEnabled = MenuManager.actions.Player.Crouch.enabled;
            var avatar = new GameObject("Locomotion smoke avatar");
            avatar.SetActive(false);
            var rig = new GameObject("Locomotion smoke rig");
            try
            {
                AccessTools.Field(typeof(LookController), "instance").SetValue(null, null);
                var player = avatar.AddComponent<PlayerController>();
                var loco = avatar.GetComponent<LocomotionController>();
                var look = avatar.GetComponent<LookController>();
                avatar.SetActive(true);
                loco.enabled = false;
                avatar.GetComponent<MassSpringController>().enabled = false;
                var crouch = new VrCrouch(player);
                crouch.State.Calibrate(2);
                check(Mathf.Abs(crouch.EyeAdjustment(2, 1)) < .001f, "real avatar standing dimensions preserve eye anchor");
                var setup = new TestOpenXR { _vrRig = rig };
                var backend = new Backend(setup, _ => { });
                Set(backend, "pollFrame", Time.frameCount);
                backend.Move = Vector2.up;
                Set(plugin, "backend", backend); Set(plugin, "manager", new Manager());
                Set(plugin, "player", player); Set(plugin, "look", look); Set(plugin, "locomotion", loco);
                Set(plugin, "calibrated", true); Set(plugin, "calibratedRig", rig); Set(plugin, "crouch", crouch);
                Set(plugin, "rigYaw", 0f); Set(plugin, "headRotation", Quaternion.identity);
                headAim.Value = false; heightCrouch.Value = true; threshold.Value = .75f;
                if (gameState != null) gameState.IsPaused = false;
                MenuManager.actions.Player.Move.Enable(); MenuManager.actions.Player.Look.Enable(); MenuManager.actions.Player.Crouch.Enable();
                var postfix = AccessTools.Method(typeof(Plugin), "PlayerPostfix");
                Action step = () => postfix.Invoke(null, new object[] { player });
                loco.IsInteracting = true;
                step();
                check(loco.MoveInput == Vector3.forward, "production player hook moves while hands are active");
                loco.MoveInput = Vector3.zero;
                MenuManager.actions.Player.Look.Disable();
                step();
                check(loco.MoveInput == Vector3.forward, "movement does not depend on look action being enabled");
                loco.MoveInput = Vector3.zero;
                MenuManager.actions.Player.Move.Disable();
                step();
                check(loco.MoveInput == Vector3.zero, "disabled movement action still blocks VR movement");
                MenuManager.actions.Player.Move.Enable();
                loco.HasCutscene = true; step();
                check(loco.MoveInput == Vector3.zero, "cutscene still blocks VR movement");
                loco.HasCutscene = false;
                setup._currentSessionState = 4; step();
                check(loco.MoveInput == Vector3.zero, "focus loss still blocks VR movement");
                setup._currentSessionState = 5;
                foreach (var view in setup._locatedViews) view.pose.position.y = 1.4f;
                loco.PostureInput = 0; step();
                check(loco.PostureInput == -1, "cached headset height reaches native posture through production hook");
                backend.ToggleCrouch = true; loco.PostureInput = 0; step();
                check(loco.PostureInput == 0 && !crouch.State.Automatic, "left X forces stand through production hook");
                backend.ToggleCrouch = false; step();
                backend.ToggleCrouch = true; step();
                check(loco.PostureInput == -1, "second left X forces crouch through production hook");
                loco.PostureInput = 1; step();
                check(loco.PostureInput == 1, "native jump posture retains priority through production hook");
                check(!(bool)Backend.Field(plugin, "failed") && !(bool)Backend.Field(plugin, "crouchFailed"), "locomotion hooks remain enabled");
                RuntimeHandSmoke.Run(plugin, backend, look, check);
            }
            finally
            {
                avatar.SetActive(false);
                foreach (var field in saved) Set(plugin, field.Key, field.Value);
                headAim.Value = previousAim; heightCrouch.Value = previousHeight; threshold.Value = previousThreshold;
                AccessTools.Field(typeof(LookController), "instance").SetValue(null, oldLook);
                if (gameState != null) gameState.IsPaused = paused;
                if (!moveEnabled) MenuManager.actions.Player.Move.Disable();
                if (lookEnabled) MenuManager.actions.Player.Look.Enable(); else MenuManager.actions.Player.Look.Disable();
                if (!crouchEnabled) MenuManager.actions.Player.Crouch.Disable();
                UnityEngine.Object.Destroy(avatar); UnityEngine.Object.Destroy(rig);
            }
        }
    }
}
