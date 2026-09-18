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
    [BepInPlugin(Id, "Walk N Wash VR Companion", "0.7.0")]
    [BepInDependency("com.newunitymodder.unityvrmod", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(30000)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.walknwash.vrcompanion";
        private static Plugin current;
        private Harmony harmony;
        private Backend backend;
        private VrRenderStage renderStage;
        private int renderedFrame = -1;
        private object manager;
        private PlayerController player;
        private LookController look;
        private LocomotionController locomotion;
        private ActionButtons actionButtons;
        private VrHands hands;
        private bool buttonInputFailed;
        private VrCrouch crouch;
        private bool crouchFailed;
        private ConfigEntry<bool> bodyFollowsHead;
        private ConfigEntry<float> bodyYawDeadZone;
        private bool bodyFacingFailed;
        private ConfigEntry<bool> roomWalking;
        private RoomWalkState roomWalk;
        private bool roomWalkFailed;
        private ConfigEntry<bool> physicalCrouch;
        private ConfigEntry<float> crouchThreshold;
        private readonly VrPrompt prompt = new VrPrompt();
        private readonly VrDialogue dialogue = new VrDialogue();
        private readonly AutoVrStart autoStart = new AutoVrStart();
        private readonly VrQuit quit = new VrQuit();
        private ConfigEntry<bool> startVrAutomatically;
        private readonly VrScreen screen = new VrScreen();
        private bool screenFailed;
        private readonly WorldCurtain curtain = new WorldCurtain();
        private readonly VrHud hud = new VrHud();
        private bool hudFailed;
        private GameObject menuRig, menuCamera;
        private Vector3 menuBaseline;
        private float menuYaw;
        private HandDiagnostics handDiagnostics, rightHandDiagnostics;
        private GripEditor gripEditor;
        private ReachPanel reachPanel;
        private readonly QuickTests quickTests = new QuickTests();
        internal bool Calibrating => QuickTests.Active || (gripEditor != null && gripEditor.Active) || (reachPanel != null && reachPanel.Active);
        private MenuHold menuHold;
        private HudToggle hudToggle;
        private bool hudToggleFailed;
        private ConfigEntry<bool> showHud, showSpongeSupply;
        private ConfigEntry<float> hudScale, hudHorizontal, hudVertical;
        private bool dialogueFailed;
        private ConfigEntry<bool> showDialogue;
        private ConfigEntry<float> dialogueWidth, dialogueDistance, pointerPitch;
        private bool promptFailed;
        private ConfigEntry<bool> showPrompt;
        private ConfigEntry<bool> enabledSetting, headAim, controllers, mouseTurn, smoothTurning;
        private ConfigEntry<float> eyeOffset, deadzone, snapDegrees, turnSpeed;
        private ConfigEntry<float> handPullback, leftHandShift;
        private ConfigEntry<Vector3> leftWristOffset, rightWristOffset;
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
            handDiagnostics = new HandDiagnostics(HandLog);
            rightHandDiagnostics = new HandDiagnostics(HandLog, 2);
            Application.wantsToQuit += WantsToQuit;
            enabledSetting = Config.Bind("General", "Enabled", true, "Enable player follow and input integration. Restart after changing this setting.");
            startVrAutomatically = Config.Bind("General", "Start VR Automatically", true, "Enable VR once UnityVRMod is ready at game startup. F11 still toggles VR afterward; use F11 to retry if startup fails.");
            headAim = Config.Bind("Camera", "Headset Aims Character", true, "Drive game look/aim from headset orientation. Mouse/right stick turn the tracking origin.");
            eyeOffset = Config.Bind("Camera", "Eye Height Offset", 0f, "Additional eye height in game world units after recentering. Use this instead of UnityVRMod's eye offset.");
            leftWristOffset = Config.Bind("Hands", "Left Wrist Position Offset", new Vector3(0, 0, -.14f), "Offset from tracked controller grip to hand mesh wrist, in controller-local meters: X right, Y up, Z forward. Rotates with the controller. Negative Z moves the wrist toward the grip/body. Only affects left hand and its contact origin, not calibrated tools. Tune for your controller; default is the user-tested 14 cm correction.");
            rightWristOffset = Config.Bind("Hands", "Right Wrist Position Offset", new Vector3(0, 0, -.14f), "Controller-local wrist offset in meters, applied before per-tool grip position/rotation. Rotates with the right controller. Negative Z brings held objects toward the grip/body; adjust separately from the left wrist.");
            handPullback = Config.Bind("Hands", "Hand Pullback", 0f, new ConfigDescription("Move both hand/tool origins backward in the tracking frame, in meters before VR scaling. Does not change feet or rotate with head movement.", new AcceptableValueRange<float>(0f, .3f)));
            leftHandShift = Config.Bind("Hands", "Left Hand Left Shift", 0f, new ConfigDescription("Move only the left hand origin left in the tracking frame, in meters before VR scaling.", new AcceptableValueRange<float>(0f, .3f)));
            recenterKey = Config.Bind("Camera", "Recenter Key", Key.F10, "Recalibrate current physical head position to character eyes. Stand or sit comfortably, then press this key.");
            controllers = Config.Bind("Input", "Enable Controllers", true, "Left stick moves; right stick turns. Keyboard and mouse remain available.");
            mouseTurn = Config.Bind("Input", "Mouse Turns Body", true, "With headset aim enabled, horizontal mouse/gamepad look turns the VR origin. Vertical look is ignored.");
            deadzone = Config.Bind("Input", "Stick Deadzone", .2f, new ConfigDescription("Radial movement deadzone.", new AcceptableValueRange<float>(0f, .9f)));
            snapDegrees = Config.Bind("Input", "Snap Turn Degrees", 30f, new ConfigDescription("One turn per right-stick deflection; release stick to turn again.", new AcceptableValueRange<float>(0f, 90f)));
            smoothTurning = Config.Bind("Input", "Smooth Turning", true, "Use continuous right-stick turning. Disable to use Snap Turn Degrees instead.");
            turnSpeed = Config.Bind("Input", "Smooth Turn Speed", 120f, new ConfigDescription("Degrees per second at full right-stick deflection.", new AcceptableValueRange<float>(0f, 360f)));
            bodyYawDeadZone = Config.Bind("Camera", "Body Head Yaw Dead Zone", 15f, new ConfigDescription("Degrees the head can turn to either side of body facing before the feet follow. Body turns smoothly only far enough to restore this margin.", new AcceptableValueRange<float>(0, 90)));
            bodyFollowsHead = Config.Bind("Camera", "Body Follows Head", true, "Turn the player body toward headset yaw, including when standing still. Keeps native slope alignment and turning smoothing; does not rotate the VR tracking origin.");
            roomWalking = Config.Bind("Input", "Room Scale Walking", true, "Move the character capsule with horizontal physical headset motion. Obstacles limit the body; natural head leaning remains free. F10 resets the movement baseline.");
            physicalCrouch = Config.Bind("Input", "Height Crouch", true, "Map calibrated physical height to kobold height and crouch when lowered. Left X overrides crouch/stand; F10 recalibrates and restores automatic mode.");
            crouchThreshold = Config.Bind("Input", "Crouch Height Threshold", .75f, new ConfigDescription("Fraction of calibrated height below which crouch begins. Stand again 0.10 above this threshold. Calibrate comfortably upright with F10, standing or seated.", new AcceptableValueRange<float>(.4f, .9f)));
            showPrompt = Config.Bind("UI", "Show Interaction Prompt", true, "Display a world-space interaction hint in VR. Other desktop UI is unchanged.");
            showDialogue = Config.Bind("UI", "Show Dialogue", true, "Show dialogue text and answers in VR. Aim right controller and press its index trigger to continue or choose.");
            dialogueWidth = Config.Bind("UI", "Dialogue Width", 1.2f, new ConfigDescription("Dialogue panel width in tracking-space meters.", new AcceptableValueRange<float>(.5f, 2f)));
            dialogueDistance = Config.Bind("UI", "Dialogue Distance", 1.6f, new ConfigDescription("Distance from headset when dialogue opens. F10 places the panel in front again.", new AcceptableValueRange<float>(.7f, 3f)));
            pointerPitch = Config.Bind("UI", "Pointer Pitch Offset", 0f, new ConfigDescription("Controller ray pitch adjustment in degrees; useful for OpenVR controller pose conventions.", new AcceptableValueRange<float>(-60f, 60f)));
            showSpongeSupply = Config.Bind("UI", "Show Sponge Supply", false, "Show the extra equipped-sponge supply bar in the VR HUD.");
            showHud = Config.Bind("UI", "Show Progress HUD", true, "Show game progress bars and equipped sponge supply at the upper-left of the headset view.");
            hudScale = Config.Bind("UI", "HUD Scale", 1f, new ConfigDescription("Progress HUD size multiplier.", new AcceptableValueRange<float>(.5f, 1.5f)));
            hudHorizontal = Config.Bind("UI", "HUD Horizontal Offset", -.5f, new ConfigDescription("Upper-left HUD edge horizontally in head space, at 1.2 meters depth.", new AcceptableValueRange<float>(-1f, 0f)));
            hudVertical = Config.Bind("UI", "HUD Vertical Offset", .4f, new ConfigDescription("Upper-left HUD edge vertically in head space, at 1.2 meters depth.", new AcceptableValueRange<float>(0f, .8f)));
            try
            {
                harmony = new Harmony(Id);
                renderStage = new VrRenderStage(RenderFrame);
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
                    Patch(type, "TeardownCameraRig", nameof(BeforeRigTeardown), null);
                    if (name == "OpenVR") Patch(type, "SetupCameraRig", null, nameof(OpenVrRigReady));
                    count++;
                }
                if (count == 0) throw new InvalidOperationException("No supported UnityVRMod backend found");
                Patch(AccessTools.TypeByName("FluidRenderingForGames.FluidRenderingRendererFeature"), "AddRenderPasses", nameof(BeforeFluidPasses), null);
                Patch(typeof(SaveManagerV1), "Save", nameof(AllowTestSave), null);
                Patch(typeof(SaveManagerV1), "DeleteSave", nameof(AllowTestSave), null);
                Patch(typeof(SaveManager), "Save", nameof(AllowTestSave), null);
                Patch(typeof(SaveManager), "DeleteSave", nameof(AllowTestSave), null);
                Patch(typeof(IntermissionFade), "OnTransition", nameof(TransitionStarted), null);
                Patch(typeof(Yarn.Unity.OptionsPresenter), "OnDialogueStartedAsync", nameof(OptionsStarting), null);
                Patch(typeof(PlayerController), "Update", nameof(PlayerPrefix), nameof(PlayerPostfix));
                Patch(typeof(LocomotionAnimationBridge), "Update", nameof(BodyPrefix), nameof(BodyPostfix));
                Patch(typeof(LookController), "LateUpdate", null, nameof(LookPostfix));
                Patch(typeof(AutoInputSwitcher), "OnDeviceChanged", nameof(DeviceChanged), null);
                Patch(typeof(Interacter), "Awake", null, nameof(InteracterAwake));
                Patch(typeof(UiPrompt), "ShowPrompt", null, nameof(PromptShown));
                Patch(typeof(UiPrompt), "HidePrompt", null, nameof(PromptHidden));
                Logger.LogInfo("Companion ready. F10 recenters; left stick moves; right stick turns.");
            }
            catch (Exception e)
            {
                failed = true;
                renderStage?.Dispose(); renderStage = null;
                harmony?.UnpatchSelf();
                Logger.LogError("Companion disabled: incompatible game/mod hooks. " + e);
            }
            if (!failed)
            {
                try { hands = new VrHands(this, Config); }
                catch (Exception e) { Logger.LogError("Controller hands unavailable; existing controls remain active. " + e); }
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

        private static void TransitionStarted(IntermissionFade __instance) => WorldCurtain.TrackTransition(__instance);
        private static bool AllowTestSave() => QuickTests.AllowSave();
        private static bool BeforeFluidPasses() => !VrScreen.Capturing;

        private static void OptionsStarting(Yarn.Unity.OptionsPresenter __instance)
        {
            // Yarn retains the preceding conversation's line unless explicitly reset.
            AccessTools.Field(typeof(Yarn.Unity.OptionsPresenter), "lastSeenLine")?.SetValue(__instance, null);
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
            && !ScreenActive && !DialogueActive
            && !locomotion.HasCutscene
            && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused);
        private bool CanAim => ControllerContext && MenuManager.actions.Player.Look.enabled;
        private bool CanControl => !Calibrating && ControllerContext && MenuManager.actions.Player.Move.enabled;
        internal bool HandsMode => VrActive && controllers.Value && calibrated && calibratedRig == backend.Rig;
        internal float HandScale => backend?.Rig ? backend.Rig.transform.localScale.x : 1f;
        internal Vector3 HandBodyEye => LookController.GetLookPosition();
        internal void HandLog(string message) => Logger.LogInfo(message);
        internal void HandPulse(int hand, float strength) => backend?.Pulse(hand, strength);
        internal void HandInteraction(bool active) { if (locomotion != null) locomotion.IsInteracting = active; }
        internal bool TryHandFrame(int hand, out HandFrame frame)
        {
            frame = default;
            if (!ControllerContext || !controllers.Value || !backend.HeadPose(out var head, out _)
                || !backend.Hand(hand, false, out var position, out var rotation)) return false;
            backend.Poll();
            var rig = backend.Rig.transform;
            Vector3 offset = Vector3.back * handPullback.Value;
            if (hand == 1) offset += Vector3.left * leftHandShift.Value;
            // Correct each tracking pivot to its wrist in controller space, before
            // rig scaling. Per-tool grip adjustments are applied afterward.
            Vector3 wrist = hand == 1 ? leftWristOffset.Value : rightWristOffset.Value;
            if (Backend.Finite(wrist)) offset += rotation * wrist;
            // Use exactly the eye cameras' tracking transform, not a second
            // independently reconstructed character/crouch origin.
            frame.Position = rig.TransformPoint(position + offset);
            frame.Rotation = rig.rotation * rotation;
            frame.Direction = backend.Hand(hand, true, out _, out var aim) ? rig.rotation * aim * Vector3.forward : frame.Rotation * Vector3.forward;
            frame.Curl = backend.Squeeze(hand);
            frame.Trigger = hand == 1 ? backend.LeftTrigger : backend.RightTrigger;
            return Backend.Finite(frame.Position) && Backend.Finite(frame.Rotation);
        }

        private bool ScreenActive => !screenFailed && VrScreen.Wanted;
        private void ScreenError(Exception e) { screenFailed = true; screen.Reset(); Logger.LogError("VR screen disabled: " + e); }

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
            hands?.Maintain();
            try { quickTests.Tick(backend, VrActive && backend.Focused); }
            catch (Exception e) { quickTests.Close(); Logger.LogError("Quick tests failed: " + e); }
            try
            {
                if (reachPanel == null) reachPanel = new ReachPanel(() => Config.Save());
                bool reachWasOpen = reachPanel.Active;
                reachPanel.Tick(backend, hands?.AssistedReach, ControllerContext && controllers.Value && hands != null && !(gripEditor?.Active ?? false));
                if (reachPanel.Active || reachWasOpen) { ReleaseButtons(); menuHold = default; }
                if (gripEditor == null) gripEditor = new GripEditor(this);
                bool wasEditing = Calibrating;
                gripEditor.Tick(backend, hands?.Grips, !reachPanel.Active && ControllerContext && controllers.Value && hands != null && hands.Active);
                if (Calibrating || wasEditing) { ReleaseButtons(); menuHold = default; }
            }
            catch (Exception e) { reachPanel?.Close(); gripEditor?.Finish(false); Logger.LogError("VR settings/calibration failed: " + e); }
            try
            {
                bool usable = !Calibrating && Enabled && controllers.Value && VrActive && backend.Focused && !quit.Pending && !quit.Ready;
                if (usable) backend.Poll();
                if (menuHold.Update(usable && backend.Menu, usable, Time.unscaledTime)) EscapeMenu.Press();
            }
            catch (Exception e) { menuHold = default; Logger.LogError("VR Escape failed: " + e); }
            if (!screenFailed)
            {
                try { if (VrActive) screen.Dispatch(backend, controllers.Value && !QuickTests.Active); else screen.Reset(); }
                catch (Exception e) { ScreenError(e); }
            }
            if (!hudToggleFailed)
            {
                try
                {
                    bool usable = !Calibrating && Enabled && controllers.Value && VrActive && backend.Focused;
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
                bool visible = !QuickTests.Active && Enabled && showDialogue.Value && VrActive && !ScreenActive
                    && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused);
                if (!visible) dialogue.Reset();
                else dialogue.Dispatch(controllers.Value && backend.Focused);
            }
            catch (Exception e) { DialogueError(e); }
        }

        // A virtual device must not trigger the game's hardware hotplug pause handler.
        private static bool DeviceChanged(InputDevice device) => !(device is CompanionButtons);
        private static void InteracterAwake(Interacter __instance) => InteracterLifetime.Attach(__instance);

        private void BeforeInputUpdate()
        {
            if (InputState.currentUpdateType == InputUpdateType.BeforeRender || actionButtons == null || buttonInputFailed) return;
            try
            {
                bool usable = !Calibrating && Enabled && controllers.Value && ControllerContext;
                if (usable) backend.Poll();
                bool leftTracked = hands == null || !hands.Active || TryHandFrame(1, out _);
                bool rightTracked = hands == null || !hands.Active || TryHandFrame(2, out _);
                actionButtons.Update(MenuManager.actions, usable ? backend.LeftTrigger : 0,
                    usable ? backend.RightTrigger : 0, usable && backend.Jump, usable, leftTracked, rightTracked);
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
            if (current == null) return;
            current.manager = __instance;
#if VR_COMPANION_SMOKE_TEST
            if (RuntimeSmoke.Requested) return;
#endif
            if (!current.Enabled || current.quit.Pending || current.quit.Ready) return;
            try
            {
                if (current.autoStart.Tick(__instance, current.startVrAutomatically.Value))
                    current.Logger.LogInfo("Automatic VR startup requested. F11 remains available.");
            }
            catch (Exception e) { current.Logger.LogWarning("Automatic VR startup failed; press F11 to retry: " + e); }
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
                if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame) { handDiagnostics?.Toggle(); rightHandDiagnostics?.Toggle(); }
                if (Keyboard.current != null && Keyboard.current[recenterKey.Value].wasPressedThisFrame)
                {
                    roomWalk.Reset();
                    calibrated = false;
                    handDiagnostics?.Recenter(); rightHandDiagnostics?.Recenter();
                    menuRig = null;
                    dialogue.Recenter(); screen.Recenter();
                }
                // Finish native parent/camera motion before placing tracked visuals,
                // and before Unity skins them. RenderEye reuses this frame's origin.
                if (VrActive) UpdateOrigin();
                hands?.RefreshVisuals();
            });
        }
        private void RenderFrame()
        {
            if (!Enabled || renderedFrame == Time.frameCount) return;
            Guard(() =>
            {
                if (scheduledFrame != Time.frameCount || !VrActive) return;
                renderedFrame = Time.frameCount;
                rendering = true;
                try { backend.Render(); }
                finally { rendering = false; curtain.EndEye(); quickTests.EndEye(); prompt.EndEye(); dialogue.EndEye(); hud.EndEye(); screen.EndEye(); gripEditor?.EndEye(); reachPanel?.EndEye(); handDiagnostics?.EndEye(); rightHandDiagnostics?.EndEye(); }
            });
        }
        private static void BeforeEye(object __instance)
        {
            if (current == null || !current.Enabled || current.backend?.Setup != __instance) return;
            current.Guard(current.UpdateOrigin);
            try { current.handDiagnostics?.BeginEye(current.backend, current.hands); current.rightHandDiagnostics?.BeginEye(current.backend, current.hands); }
            catch (Exception e) { current.handDiagnostics?.Dispose(); current.handDiagnostics = null; current.rightHandDiagnostics?.Dispose(); current.rightHandDiagnostics = null; current.Logger.LogWarning("Hand diagnostics disabled: " + e); }
            current.Guard(() => current.curtain.BeginEye(current.backend, current.VrActive));
            current.Guard(() => current.gripEditor?.BeginEye(current.backend));
            current.Guard(() => current.reachPanel?.BeginEye(current.backend));
            current.Guard(() => current.quickTests.BeginEye(current.backend));
            if (!current.screenFailed)
            {
                try { current.screen.BeginEye(current.backend, current.VrActive); }
                catch (Exception e) { current.ScreenError(e); }
            }
            if (!current.hudFailed)
            {
                try
                {
                    current.hud.BeginEye(current.backend, current.showHud.Value && current.VrActive && !current.ScreenActive
                        && current.player != null && current.player.isActiveAndEnabled
                        && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused),
                        current.hudScale.Value, current.hudHorizontal.Value, current.hudVertical.Value, current.showSpongeSupply.Value);
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
                    current.dialogue.BeginEye(current.backend, !QuickTests.Active && current.showDialogue.Value && current.VrActive && !current.ScreenActive
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
        private static void AfterEye() { current?.curtain.EndEye(); current?.quickTests.EndEye(); current?.reachPanel?.EndEye(); current?.prompt.EndEye(); current?.dialogue.EndEye(); current?.hud.EndEye(); current?.screen.EndEye(); current?.gripEditor?.EndEye(); current?.handDiagnostics?.EndEye(); current?.rightHandDiagnostics?.EndEye(); }
        private static void PromptShown(Vector3 __0) { current?.prompt.Show(__0); }
        private static void PromptHidden() { current?.prompt.Hide(); }
        private void UpdateOrigin()
        {
            if (poseFrame == Time.frameCount) return;
            poseFrame = Time.frameCount;
            if (player == null || !player.isActiveAndEnabled || look == null)
            {
                roomWalk.Reset();
                UpdateMenuOrigin();
                return;
            }
            menuRig = null;
            if (!backend.HeadPose(out var head, out var rotation)) { roomWalk.Reset(); return; }
            var rig = backend.Rig;
            if (rig == null) return;
            headRotation = rotation;
            if (!calibrated || calibratedRig != rig)
            {
                roomWalk.Reset();
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
            UpdateRoomWalk(head);
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
        private void UpdateRoomWalk(Vector3 head)
        {
            try
            {
                bool usable = !roomWalkFailed && roomWalking.Value && !Calibrating && ControllerContext
                    && MenuManager.actions.Player.Move.enabled && locomotion.isActiveAndEnabled;
                if (!roomWalk.Sample(head.x, head.z, Time.unscaledTime, usable, out float dx, out float dz)) return;
                var rig = backend.Rig.transform;
                if (rig.localScale.x <= 0 || !Backend.Finite(rig.lossyScale)) { roomWalk.Reset(); return; }
                var yaw = Quaternion.Euler(0, rigYaw, 0);
                Vector3 request = yaw * new Vector3(dx, 0, dz) * rig.localScale.x;
                var moved = RoomWalk.Move(player.GetComponent<Rigidbody>(), player.GetComponent<CapsuleCollider>(), request);
                // Consume the entire physical step, including the blocked portion.
                // Otherwise rejected movement accumulates between head and body.
                baseline += new Vector3(dx, 0, dz);
                // LookController cached its eye matrix earlier this LateUpdate.
                // Translate that cache without running smoothing/animation twice.
                var field = AccessTools.Field(typeof(LookController), "lastViewMatrix");
                field.SetValue(look, Matrix4x4.Translate(moved) * (Matrix4x4)field.GetValue(look));
            }
            catch (Exception e)
            {
                roomWalkFailed = true; roomWalk.Reset();
                Logger.LogError("Room-scale walking disabled; stick movement remains available: " + e);
            }
        }
        private void UpdateMenuOrigin()
        {
            var rig = backend.Rig;
            var camera = Backend.Field(backend.Setup, "_currentlyTrackedOriginalCameraGO") as GameObject;
            if (!rig || !camera || !backend.HeadPose(out var head, out var rotation)) return;
            if (menuRig != rig || menuCamera != camera)
            {
                menuRig = rig;
                menuCamera = camera;
                menuBaseline = head;
                menuYaw = camera.transform.eulerAngles.y - rotation.eulerAngles.y;
                screen.Recenter();
            }
            // Calibrate once, then preserve physical head motion relative to the
            // authored menu camera. Never continuously turn the scene with the HMD.
            var anchor = camera.transform.position;
            ControlMath.Origin(anchor.x, anchor.y, anchor.z,
                menuBaseline.x, menuBaseline.y, menuBaseline.z,
                menuYaw, rig.transform.localScale.x, out float x, out float y, out float z);
            rig.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, menuYaw, 0));
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
                bool usable = !Calibrating && ControllerContext && MenuManager.actions.Player.Crouch.enabled;
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
        private static void BodyPrefix(LocomotionAnimationBridge __instance, out Quaternion __state)
            => __state = __instance.transform.rotation;
        private static void BodyPostfix(LocomotionAnimationBridge __instance, Quaternion __state)
        {
            var host = current;
            if (host == null || host.bodyFacingFailed || !host.bodyFollowsHead.Value || !host.ControllerContext || host.Calibrating) return;
            try
            {
                // This component also serves other actors. Only modify our player's bridge.
                if (Backend.Field(__instance, "locomotionController") as LocomotionController != host.locomotion
                    || !host.backend.HeadPose(out _, out var head)) return;
                float yaw = host.rigYaw + head.eulerAngles.y;
                var correction = (Quaternion)Backend.Field(__instance, "initialRotation");
                var previousForward = __state * Quaternion.Inverse(correction) * Vector3.forward;
                __instance.transform.rotation = BodyFacing.Rotation(__state, yaw, host.locomotion.SurfaceNormal,
                    correction, __instance.turnSpeed, Time.deltaTime, out var forward, host.bodyYawDeadZone.Value);
                AccessTools.Field(typeof(LocomotionAnimationBridge), "lastForward").SetValue(__instance, forward);
                var animator = Backend.Field(__instance, "animator") as Animator;
                if (animator && animator.runtimeAnimatorController)
                {
                    float turn = Vector3.SignedAngle(previousForward,
                        __instance.transform.rotation * Quaternion.Inverse(correction) * Vector3.forward, Vector3.up)
                        / Mathf.Max(Time.deltaTime, .0001f) / 180f;
                    animator.SetFloat((string)Backend.Field(__instance, "turnParam"), turn);
                }
            }
            catch (Exception e)
            {
                host.bodyFacingFailed = true;
                host.Logger.LogError("Headset body turning disabled; native body animation remains: " + e);
            }
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
        private static void OpenVrRigReady(object __instance)
        {
            if (current == null || !current.Enabled) return;
            current.Guard(() =>
            {
                if (Backend.Field(__instance, "_vrRig") is GameObject rig && rig
                    && OpenVrTargets.Ensure(__instance))
                    current.Logger.LogInfo("Recreated OpenVR eye textures after rig restart.");
            });
        }
        private static void BeforeRigTeardown(object __instance)
        {
            if (current == null || current.backend?.Setup != __instance) return;
            current.Guard(() =>
            {
                current.hands?.RestoreAll();
                current.prompt.Hide(); current.dialogue.Reset(); current.hud.Reset(); current.screen.Reset();
                current.ReleaseButtons();
                current.calibrated = false;
                current.calibratedRig = null;
                current.roomWalk.Reset();
                current.menuRig = current.menuCamera = null;
                current.poseFrame = current.scheduledFrame = -1;
            });
        }
        private static void BeforeTeardown(object __instance)
        {
            if (current == null || current.backend?.Setup != __instance) return;
            current.Guard(() =>
            {
                current.backend.Dispose();
                current.hands?.RestoreAll();
                current.prompt.Hide();
                current.dialogue.Reset();
                current.hud.Reset(); current.screen.Reset();
                current.handDiagnostics?.Recenter(); current.rightHandDiagnostics?.Recenter(); current.handDiagnostics?.EndEye(); current.rightHandDiagnostics?.EndEye();
                current.gripEditor?.Finish(false);
                current.menuHold = default;
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
        private bool WantsToQuit()
        {
            if (quit.Pending) return false;
            try
            {
                if (quit.Request(manager)) return true;
                scheduledFrame = -1;
                ReleaseButtons();
                Logger.LogInfo("Preparing exit: VR safe mode enabled; allowing cleanup frames before quitting.");
                StartCoroutine(FinishQuit());
                return false;
            }
            catch (Exception e)
            {
                quit.Complete();
                Logger.LogWarning("VR exit preparation failed; allowing normal exit: " + e.Message);
                return true;
            }
        }
        private IEnumerator FinishQuit()
        {
            // A synchronous OnApplicationQuit teardown is too late. Keep Unity
            // running briefly so F11's rendering/rig changes finish normally.
            yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            quit.Complete();
            Application.Quit();
        }
        private void OnDestroy()
        {
            Application.wantsToQuit -= WantsToQuit;
            renderStage?.Dispose();
            InputSystem.onBeforeUpdate -= BeforeInputUpdate;
            quickTests.Dispose();
            prompt.Dispose();
            dialogue.Dispose();
            hud.Dispose(); screen.Dispose(); curtain.Dispose();
            handDiagnostics?.Dispose(); rightHandDiagnostics?.Dispose();
            gripEditor?.Dispose(); reachPanel?.Dispose();
            hands?.Dispose();
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
