using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WalkNWash.VRCompanion
{
    [BepInPlugin(Id, "Walk N Wash VR Companion", "0.4.0")]
    [BepInDependency("com.newunitymodder.unityvrmod", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(30000)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.walknwash.vrcompanion";
        private static Plugin current;
        private Harmony harmony;
        private Backend backend;
        private object manager;
        private PlayerController player;
        private LookController look;
        private LocomotionController locomotion;
        private ActionButtons actionButtons;
        private bool buttonInputFailed;
        private VrCrouch crouch;
        private bool crouchFailed;
        private ConfigEntry<bool> physicalCrouch;
        private ConfigEntry<float> crouchThreshold;
        private readonly VrPrompt prompt = new VrPrompt();
        private readonly VrDialogue dialogue = new VrDialogue();
        private readonly VrHud hud = new VrHud();
        private bool hudFailed;
        private HudToggle hudToggle;
        private bool hudToggleFailed;
        private ConfigEntry<bool> showHud;
        private ConfigEntry<float> hudScale, hudHorizontal, hudVertical;
        private bool dialogueFailed;
        private ConfigEntry<bool> showDialogue;
        private ConfigEntry<float> dialogueWidth, dialogueDistance, pointerPitch;
        private bool promptFailed;
        private ConfigEntry<bool> showPrompt;
        private ConfigEntry<bool> enabledSetting, headAim, controllers, mouseTurn, smoothTurning;
        private ConfigEntry<float> eyeOffset, deadzone, snapDegrees, turnSpeed;
        private ConfigEntry<Key> recenterKey;
        private int scheduledFrame = -1, poseFrame = -1;
        private bool rendering, calibrated, snapLatched, failed;
        private Vector3 baseline;
        private Quaternion headRotation = Quaternion.identity;
        private float rigYaw;
        private GameObject calibratedRig;
        private static readonly MethodInfo viewUpdate = AccessTools.Method(typeof(LookController), "ViewUpdate");
        private static readonly FieldInfo smoothedLook = AccessTools.Field(typeof(LookController), "smoothedLook");
        private static readonly FieldInfo smoothingVelocity = AccessTools.Field(typeof(LookController), "smoothingVelocity");

        private void Awake()
        {
            current = this;
            enabledSetting = Config.Bind("General", "Enabled", true, "Enable player follow and input integration. Restart after changing this setting.");
            headAim = Config.Bind("Camera", "Headset Aims Character", true, "Drive game look/aim from headset orientation. Mouse/right stick turn the tracking origin.");
            eyeOffset = Config.Bind("Camera", "Eye Height Offset", 0f, "Additional eye height in game world units after recentering. Use this instead of UnityVRMod's eye offset.");
            recenterKey = Config.Bind("Camera", "Recenter Key", Key.F10, "Recalibrate current physical head position to character eyes. Stand or sit comfortably, then press this key.");
            controllers = Config.Bind("Input", "Enable Controllers", true, "Left stick moves; right stick turns. Keyboard and mouse remain available.");
            mouseTurn = Config.Bind("Input", "Mouse Turns Body", true, "With headset aim enabled, horizontal mouse/gamepad look turns the VR origin. Vertical look is ignored.");
            deadzone = Config.Bind("Input", "Stick Deadzone", .2f, new ConfigDescription("Radial movement deadzone.", new AcceptableValueRange<float>(0f, .9f)));
            snapDegrees = Config.Bind("Input", "Snap Turn Degrees", 30f, new ConfigDescription("One turn per right-stick deflection; release stick to turn again.", new AcceptableValueRange<float>(0f, 90f)));
            smoothTurning = Config.Bind("Input", "Smooth Turning", true, "Use continuous right-stick turning. Disable to use Snap Turn Degrees instead.");
            turnSpeed = Config.Bind("Input", "Smooth Turn Speed", 90f, new ConfigDescription("Degrees per second at full right-stick deflection.", new AcceptableValueRange<float>(0f, 360f)));
            physicalCrouch = Config.Bind("Input", "Height Crouch", true, "Map calibrated physical height to kobold height and crouch when lowered. Left X overrides crouch/stand; F10 recalibrates and restores automatic mode.");
            crouchThreshold = Config.Bind("Input", "Crouch Height Threshold", .75f, new ConfigDescription("Fraction of calibrated height below which crouch begins. Stand again 0.10 above this threshold. Calibrate comfortably upright with F10, standing or seated.", new AcceptableValueRange<float>(.4f, .9f)));
            showPrompt = Config.Bind("UI", "Show Interaction Prompt", true, "Display a world-space interaction hint in VR. Other desktop UI is unchanged.");
            showDialogue = Config.Bind("UI", "Show Dialogue", true, "Show dialogue text and answers in VR. Aim right controller and press its index trigger to continue or choose.");
            dialogueWidth = Config.Bind("UI", "Dialogue Width", 1.2f, new ConfigDescription("Dialogue panel width in tracking-space meters.", new AcceptableValueRange<float>(.5f, 2f)));
            dialogueDistance = Config.Bind("UI", "Dialogue Distance", 1.6f, new ConfigDescription("Distance from headset when dialogue opens. F10 places the panel in front again.", new AcceptableValueRange<float>(.7f, 3f)));
            pointerPitch = Config.Bind("UI", "Pointer Pitch Offset", 0f, new ConfigDescription("Controller ray pitch adjustment in degrees; useful for OpenVR controller pose conventions.", new AcceptableValueRange<float>(-60f, 60f)));
            showHud = Config.Bind("UI", "Show Progress HUD", true, "Show game progress bars and equipped sponge supply at the upper-left of the headset view.");
            hudScale = Config.Bind("UI", "HUD Scale", 1f, new ConfigDescription("Progress HUD size multiplier.", new AcceptableValueRange<float>(.5f, 1.5f)));
            hudHorizontal = Config.Bind("UI", "HUD Horizontal Offset", -.5f, new ConfigDescription("Upper-left HUD edge horizontally in head space, at 1.2 meters depth.", new AcceptableValueRange<float>(-1f, 0f)));
            hudVertical = Config.Bind("UI", "HUD Vertical Offset", .4f, new ConfigDescription("Upper-left HUD edge vertically in head space, at 1.2 meters depth.", new AcceptableValueRange<float>(0f, .8f)));
            try
            {
                harmony = new Harmony(Id);
                Type managerType = AccessTools.TypeByName("UnityVRMod.Features.VrVisualization.VrVisualizationManager");
                Patch(managerType, "Update", nameof(ManagerPrefix), null);
                int count = 0;
                foreach (string name in new[] { "OpenXR", "OpenVR" })
                {
                    Type type = AccessTools.TypeByName("UnityVRMod.Features.VrVisualization.VrCameraSetup_Core" + name);
                    if (type == null) continue;
                    Patch(type, "InitializeVr", null, nameof(Initialized));
                    Patch(type, "UpdatePoses", nameof(Schedule), null);
                    Patch(type, "RenderEye", nameof(BeforeEye), nameof(AfterEye));
                    Patch(type, "TeardownVr", nameof(BeforeTeardown), null);
                    count++;
                }
                if (count == 0) throw new InvalidOperationException("No supported UnityVRMod backend found");
                Patch(typeof(PlayerController), "Update", nameof(PlayerPrefix), nameof(PlayerPostfix));
                Patch(typeof(LookController), "LateUpdate", null, nameof(LookPostfix));
                Patch(typeof(AutoInputSwitcher), "OnDeviceChanged", nameof(DeviceChanged), null);
                Patch(typeof(UiPrompt), "ShowPrompt", null, nameof(PromptShown));
                Patch(typeof(UiPrompt), "HidePrompt", null, nameof(PromptHidden));
                Logger.LogInfo("Companion ready. F10 recenters; left stick moves; right stick turns.");
            }
            catch (Exception e)
            {
                failed = true;
                harmony?.UnpatchSelf();
                Logger.LogError("Companion disabled: incompatible game/mod hooks. " + e);
            }
        }

        private IEnumerator Start()
        {
            // BepInEx Awake runs before Unity's input layout globals are ready.
            // Let Unity finish initialization and one frame before adding a device.
            yield return null;
            if (!Enabled || !controllers.Value) yield break;
            try
            {
                actionButtons = new ActionButtons();
                InputSystem.onBeforeUpdate += BeforeInputUpdate;
                Logger.LogInfo("VR action buttons ready: triggers and right A.");
            }
            catch (Exception e)
            {
                buttonInputFailed = true;
                Logger.LogError("VR action buttons unavailable; camera, F10 and sticks remain active. " + e);
            }
#if VR_COMPANION_SMOKE_TEST
            if (RuntimeSmoke.Requested)
            {
                // Exercise the production input callback before injecting test input.
                yield return null;
                InputSystem.onBeforeUpdate -= BeforeInputUpdate;
                RuntimeSmoke.Run(actionButtons, !failed && !buttonInputFailed, Logger);
            }
#endif
        }

        private void ReleaseButtons()
        {
            try { actionButtons?.Release(); }
            catch (Exception e) { Logger.LogWarning("Button release failed: " + e.Message); }
        }

        private void Patch(Type type, string method, string prefix, string postfix)
        {
            MethodInfo original = type == null ? null : AccessTools.Method(type, method);
            if (original == null) throw new MissingMethodException(type?.FullName, method);
            harmony.Patch(original,
                prefix == null ? null : new HarmonyMethod(typeof(Plugin), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
        }

        private bool Enabled => !failed && enabledSetting.Value;
        private bool VrActive => Enabled && backend != null && backend.Rig != null && manager != null
            && !(bool)Backend.Field(manager, "_isUserSafeModeActive")
            && Time.time >= Convert.ToSingle(Backend.Field(manager, "_autoSafeModeEndTime"));
        private bool ControllerContext => VrActive && calibrated && calibratedRig == backend.Rig && player != null && player.isActiveAndEnabled
            && look != null && locomotion != null && backend.Focused
            && !DialogueActive
            && !locomotion.HasCutscene
            && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused);
        private bool CanAim => ControllerContext && MenuManager.actions.Player.Look.enabled;
        private bool CanControl => ControllerContext && MenuManager.actions.Player.Move.enabled;

        private bool DialogueActive
        {
            get
            {
                if (dialogueFailed) return false;
                try { return dialogue.BlocksGameplay; }
                catch (Exception e) { DialogueError(e); return false; }
            }
        }
        private void DialogueError(Exception e)
        {
            dialogueFailed = true;
            dialogue.Reset();
            Logger.LogError("VR dialogue disabled; gameplay controls remain active. " + e);
        }
        private void Update()
        {
            if (!hudToggleFailed)
            {
                try
                {
                    bool usable = Enabled && controllers.Value && VrActive && backend.Focused;
                    if (usable) backend.Poll();
                    if (hudToggle.Update(usable && backend.ToggleHud, usable))
                    {
                        showHud.Value = !showHud.Value;
                        hud.Reset();
                        Logger.LogInfo("Progress HUD " + (showHud.Value ? "shown" : "hidden") + " (left Y).");
                    }
                }
                catch (Exception e)
                {
                    hudToggleFailed = true;
                    Logger.LogError("HUD toggle unavailable; other controls remain active. " + e);
                }
            }
            if (dialogueFailed) return;
            try
            {
                bool visible = Enabled && showDialogue.Value && VrActive
                    && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused);
                if (!visible) dialogue.Reset();
                else dialogue.Dispatch(controllers.Value && backend.Focused);
            }
            catch (Exception e) { DialogueError(e); }
        }

        // A virtual device must not trigger the game's hardware hotplug pause handler.
        private static bool DeviceChanged(InputDevice device) => !(device is CompanionButtons);

        private void BeforeInputUpdate()
        {
            if (InputState.currentUpdateType == InputUpdateType.BeforeRender || actionButtons == null || buttonInputFailed) return;
            try
            {
                bool usable = Enabled && controllers.Value && ControllerContext;
                if (usable) backend.Poll();
                actionButtons.Update(MenuManager.actions, usable ? backend.LeftTrigger : 0,
                    usable ? backend.RightTrigger : 0, usable && backend.Jump, usable);
            }
            catch (Exception e)
            {
                buttonInputFailed = true;
                ReleaseButtons();
                Logger.LogError("Button input stopped; camera and sticks remain active. " + e);
            }
        }

        private void BindPlayer(PlayerController value)
        {
            if (player == value) return;
            player = value;
            look = value.GetComponent<LookController>();
            locomotion = value.GetComponent<LocomotionController>();
            crouch = null;
            calibrated = false;
        }

        private static void ManagerPrefix(object __instance)
        {
            if (current != null) current.manager = __instance;
        }
        private static void Initialized(object __instance, bool __result)
        {
            if (current == null || !current.Enabled || !__result) return;
            current.Guard(() =>
            {
                current.backend?.Dispose();
                current.backend = new Backend(__instance, message => current.Logger.LogInfo(message));
                current.calibrated = false;
                if (current.controllers.Value) current.backend.InitializeInput();
            });
        }
        private static bool Schedule(object __instance)
        {
            if (current == null || !current.Enabled || current.backend == null || current.backend.Setup != __instance) return true;
            if (current.rendering) return true;
            current.scheduledFrame = Time.frameCount;
            return false;
        }
        private void LateUpdate()
        {
            if (!Enabled) return;
            Guard(() =>
            {
                if (Keyboard.current != null && Keyboard.current[recenterKey.Value].wasPressedThisFrame)
                {
                    calibrated = false;
                    dialogue.Recenter();
                }
                if (scheduledFrame != Time.frameCount || !VrActive) return;
                rendering = true;
                try { backend.Render(); }
                finally { rendering = false; prompt.EndEye(); dialogue.EndEye(); hud.EndEye(); }
            });
        }
        private static void BeforeEye(object __instance)
        {
            if (current == null || !current.Enabled || current.backend?.Setup != __instance) return;
            current.Guard(current.UpdateOrigin);
            if (!current.hudFailed)
            {
                try
                {
                    current.hud.BeginEye(current.backend, current.showHud.Value && current.VrActive
                        && current.player != null && current.player.isActiveAndEnabled
                        && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused),
                        current.hudScale.Value, current.hudHorizontal.Value, current.hudVertical.Value);
                }
                catch (Exception e)
                {
                    current.hudFailed = true;
                    current.hud.Reset();
                    current.Logger.LogError("VR progress HUD disabled; controls and dialogue remain active. " + e);
                }
            }
            if (!current.dialogueFailed)
            {
                try
                {
                    current.dialogue.BeginEye(current.backend, current.showDialogue.Value && current.VrActive
                        && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused),
                        current.controllers.Value, current.dialogueWidth.Value, current.dialogueDistance.Value, current.pointerPitch.Value);
                }
                catch (Exception e) { current.DialogueError(e); }
            }
            if (current.promptFailed) return;
            try
            {
                current.prompt.BeginEye(current.backend,
                    current.showPrompt.Value && current.ControllerContext && MenuManager.actions.Player.Plap.enabled,
                    current.controllers.Value && !current.buttonInputFailed);
            }
            catch (Exception e)
            {
                current.promptFailed = true;
                current.prompt.EndEye();
                current.Logger.LogError("VR interaction prompt disabled; other controls remain active. " + e);
            }
        }
        private static void AfterEye() { current?.prompt.EndEye(); current?.dialogue.EndEye(); current?.hud.EndEye(); }
        private static void PromptShown(Vector3 __0) { current?.prompt.Show(__0); }
        private static void PromptHidden() { current?.prompt.Hide(); }
        private void UpdateOrigin()
        {
            if (poseFrame == Time.frameCount) return;
            poseFrame = Time.frameCount;
            if (player == null || !player.isActiveAndEnabled || look == null) return;
            if (!backend.HeadPose(out var head, out var rotation)) return;
            var rig = backend.Rig;
            if (rig == null) return;
            headRotation = rotation;
            if (!calibrated || calibratedRig != rig)
            {
                baseline = head;
                rigYaw = look.LookYaw - rotation.eulerAngles.y;
                calibrated = true;
                calibratedRig = rig;
                if (!crouchFailed)
                {
                    try
                    {
                        if (crouch == null) crouch = new VrCrouch(player);
                        crouch.State.Calibrate(head.y);
                    }
                    catch (Exception e) { CrouchError(e); }
                }
                Logger.LogInfo("Camera calibrated to player eyes. Backend: " + (backend.IsOpenXr ? "OpenXR" : "OpenVR") + "; physical head: " + head);
            }
            Vector3 anchor = LookController.GetLookPosition() + Vector3.up * eyeOffset.Value;
            if (!crouchFailed && crouch != null && physicalCrouch.Value)
            {
                try { anchor.y += crouch.EyeAdjustment(head.y, rig.transform.localScale.x); }
                catch (Exception e) { CrouchError(e); }
            }
            ControlMath.Origin(anchor.x, anchor.y, anchor.z, baseline.x, baseline.y, baseline.z,
                rigYaw, rig.transform.localScale.x, out float x, out float y, out float z);
            rig.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, rigYaw, 0));
        }
        private static void PlayerPrefix(PlayerController __instance)
        {
            if (current == null || !current.Enabled) return;
            current.Guard(() =>
            {
                current.BindPlayer(__instance);
                if (current.CanAim && current.headAim.Value) current.ApplyAim();
            });
        }
        private static void PlayerPostfix(PlayerController __instance)
        {
            if (current == null || !current.Enabled) return;
            current.Guard(() =>
            {
                current.UpdateCrouch();
                if (current.CanAim && current.headAim.Value)
                {
                    if (current.mouseTurn.Value) current.rigYaw += current.look.LookInput.x;
                    current.look.LookInput = Vector2.zero;
                    smoothedLook.SetValue(current.look, Vector2.zero);
                    smoothingVelocity.SetValue(current.look, Vector2.zero);
                }
                if (!current.controllers.Value || !current.CanControl) { current.snapLatched = false; return; }
                current.backend.Poll();
                if (!current.CanAim) current.snapLatched = false;
                else if (current.smoothTurning.Value)
                {
                    current.snapLatched = false;
                    current.rigYaw += ControlMath.SmoothTurn(current.backend.Turn.x, current.deadzone.Value,
                        current.turnSpeed.Value, Time.unscaledDeltaTime);
                }
                else current.rigYaw += ControlMath.Snap(current.backend.Turn.x, current.snapDegrees.Value, ref current.snapLatched);
                ControlMath.Deadzone(current.backend.Move.x, current.backend.Move.y, current.deadzone.Value, out var x, out var y);
                if (x != 0 || y != 0)
                {
                    float heading = current.rigYaw + current.headRotation.eulerAngles.y;
                    current.locomotion.MoveInput = Quaternion.Euler(0, heading, 0) * new Vector3(x, 0, y);
                }
            });
        }
        private void UpdateCrouch()
        {
            if (crouchFailed || crouch == null) return;
            try
            {
                bool usable = ControllerContext && MenuManager.actions.Player.Crouch.enabled;
                if (usable && controllers.Value) backend.Poll();
                float height = float.NaN;
                if (usable && backend.HeadPose(out var head, out _)) height = head.y;
                bool automatic = crouch.State.Automatic, previous = crouch.State.Crouched;
                crouch.State.Update(height, usable && controllers.Value && backend.ToggleCrouch,
                    usable, physicalCrouch.Value, crouchThreshold.Value);
                if (usable) locomotion.PostureInput = crouch.State.Posture(locomotion.PostureInput);
                if (automatic != crouch.State.Automatic || previous != crouch.State.Crouched)
                    Logger.LogInfo("VR crouch: " + (crouch.State.Crouched ? "crouched" : "standing")
                        + (crouch.State.Automatic ? " (height)." : " (left X override; F10 restores height mode)."));
            }
            catch (Exception e) { CrouchError(e); }
        }
        private void CrouchError(Exception e)
        {
            crouchFailed = true;
            crouch = null;
            Logger.LogError("VR crouch disabled; other controls remain active. " + e);
        }
        private void ApplyAim()
        {
            Quaternion world = Quaternion.Euler(0, rigYaw, 0) * headRotation;
            Vector3 euler = world.eulerAngles;
            LookController.SetLookRotation(Quaternion.Euler(euler.x, euler.y, 0));
        }
        private static void LookPostfix(LookController __instance)
        {
            if (current == null || !current.Enabled || current.look != __instance) return;
            current.Guard(() =>
            {
                if (!current.CanAim || !current.headAim.Value) return;
                current.ApplyAim();
                viewUpdate.Invoke(__instance, null);
            });
        }
        private static void BeforeTeardown(object __instance)
        {
            if (current == null || current.backend?.Setup != __instance) return;
            current.Guard(() =>
            {
                current.backend.Dispose();
                current.prompt.Hide();
                current.dialogue.Reset();
                current.hud.Reset();
                current.hudToggle = new HudToggle();
                current.ReleaseButtons();
                current.backend = null;
                current.calibrated = false;
                current.scheduledFrame = -1;
            });
        }
        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                failed = true;
                ReleaseButtons();
                Logger.LogError("Companion stopped after error; original VR rendering resumes. " + e);
            }
        }
        private void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= BeforeInputUpdate;
            prompt.Dispose();
            dialogue.Dispose();
            hud.Dispose();
            try { actionButtons?.Dispose(); }
            catch (Exception e) { Logger.LogWarning("Button cleanup failed: " + e.Message); }
            finally
            {
                try { backend?.Dispose(); }
                finally
                {
                    harmony?.UnpatchSelf();
                    if (current == this) current = null;
                }
            }
        }
    }
}
