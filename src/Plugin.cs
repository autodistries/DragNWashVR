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
    [BepInPlugin(Id, "Walk N Wash VR Companion", "0.8.3")]
    [BepInDependency("com.newunitymodder.unityvrmod", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(30000)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.walknwash.vrcompanion";
        private static Plugin current;
        internal static Plugin Current => current;
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
        private UserSettings userSettings;
        private UserSettingsPanel settingsPanel;
        internal UserSettings Preferences => userSettings;
        private HandRoles handRoles;
        internal HandRoles Roles => handRoles;
        internal int FreeHand => handRoles.FreeHand;
        internal int ToolHand => handRoles.ToolHand;
        private bool buttonInputFailed;
        private VrCrouch crouch;
        private bool crouchFailed;
        private ConfigEntry<bool> bodyFollowsHead;
        private ConfigEntry<float> bodyYawDeadZone;
        private bool bodyFacingFailed;
        private ConfigEntry<bool> roomWalking;
        private ConfigEntry<float> roomLeanRadius, roomRecenterSpeed, bodyWidth;
        private readonly VrBodyCollider bodyCollider = new VrBodyCollider();
        private RoomWalkState roomWalk;
        private bool roomWalkFailed, roomLeaning;
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
        private readonly QuickTests quickTests = new QuickTests();
        internal bool Calibrating => (settingsPanel?.Active ?? false) || QuickTests.Active || (gripEditor != null && gripEditor.Active);
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
            userSettings = new UserSettings();
            handDiagnostics = new HandDiagnostics(HandLog);
            rightHandDiagnostics = new HandDiagnostics(HandLog, 2);
            Application.wantsToQuit += WantsToQuit;
            enabledSetting = userSettings.Bind("General", "Enabled", true, "Enable player follow and input integration. Restart after changing this setting.");
            startVrAutomatically = userSettings.Bind("General", "Start VR Automatically", true, "Enable VR once UnityVRMod is ready at game startup. F11 still toggles VR afterward; use F11 to retry if startup fails.");
            headAim = userSettings.Bind("Camera", "Headset Aims Character", true, "Drive game look/aim from headset orientation. Mouse/right stick turn the tracking origin.");
            eyeOffset = userSettings.Bind("Camera", "Eye Height Offset", 0f, "Additional eye height in game world units after recentering. Use this instead of UnityVRMod's eye offset.");
            leftWristOffset = userSettings.Bind("Hands", "Left Wrist Position Offset", new Vector3(0, 0, -.14f), "Offset from tracked controller grip to hand mesh wrist, in controller-local meters: X right, Y up, Z forward. Rotates with the controller. Negative Z moves the wrist toward the grip/body. Only affects left hand and its contact origin, not calibrated tools. Tune for your controller; default is the user-tested 14 cm correction.");
            rightWristOffset = userSettings.Bind("Hands", "Right Wrist Position Offset", new Vector3(0, 0, -.14f), "Controller-local wrist offset in meters, applied before per-tool grip position/rotation. Rotates with the right controller. Negative Z brings held objects toward the grip/body; adjust separately from the left wrist.");
            handPullback = userSettings.Bind("Hands", "Hand Pullback", 0f, new ConfigDescription("Move both hand/tool origins backward in the tracking frame, in meters before VR scaling. Does not change feet or rotate with head movement.", new AcceptableValueRange<float>(0f, .3f)));
            leftHandShift = userSettings.Bind("Hands", "Left Hand Left Shift", 0f, new ConfigDescription("Move only the left hand origin left in the tracking frame, in meters before VR scaling.", new AcceptableValueRange<float>(0f, .3f)));
            recenterKey = userSettings.Bind("Camera", "Recenter Key", Key.F10, "Recalibrate current physical head position to character eyes. Stand or sit comfortably, then press this key.");
            handRoles = new HandRoles(userSettings.Bind("Hands", "Tool Hand", ToolController.Right,
                "Controller holding equipped tools. The other controller is the free/slap hand. Restart the game after changing. Menus always use the right controller.").Value == ToolController.Left);
            controllers = userSettings.Bind("Input", "Enable Controllers", true, "Left stick moves; right stick turns. Keyboard and mouse remain available.");
            mouseTurn = userSettings.Bind("Input", "Mouse Turns Body", true, "With headset aim enabled, horizontal mouse/gamepad look turns the VR origin. Vertical look is ignored.");
            deadzone = userSettings.Bind("Input", "Stick Deadzone", .2f, new ConfigDescription("Radial movement deadzone.", new AcceptableValueRange<float>(0f, .9f)));
            snapDegrees = userSettings.Bind("Input", "Snap Turn Degrees", 30f, new ConfigDescription("One turn per right-stick deflection; release stick to turn again.", new AcceptableValueRange<float>(0f, 90f)));
            smoothTurning = userSettings.Bind("Input", "Smooth Turning", true, "Use continuous right-stick turning. Disable to use Snap Turn Degrees instead.");
            turnSpeed = userSettings.Bind("Input", "Smooth Turn Speed", 135f, new ConfigDescription("Degrees per second at full right-stick deflection.", new AcceptableValueRange<float>(0f, 360f)));
            bodyYawDeadZone = userSettings.Bind("Camera", "Body Head Yaw Dead Zone", 15f, new ConfigDescription("Degrees the head can turn to either side of body facing before the feet follow. Body turns smoothly only far enough to restore this margin.", new AcceptableValueRange<float>(0, 90)));
            bodyFollowsHead = userSettings.Bind("Camera", "Body Follows Head", true, "Turn the player body toward headset yaw, including when standing still. Keeps native slope alignment and turning smoothing; does not rotate the VR tracking origin.");
            cutsceneFreecam = userSettings.Bind("Camera", "Cutscene Freecam", false, "Detach the VR camera during cutscenes; left Y toggles this. Internal state in the versioned config, not a user preference.");
            cutsceneFreecam.SettingChanged += (_, __) => poseFrame = -1;
            roomWalking = userSettings.Bind("Input", "Room Scale Walking", true, "Move the character capsule with horizontal physical headset motion. Obstacles limit the body; natural head leaning remains free. F10 resets the movement baseline.");
            roomLeanRadius = userSettings.Bind("Input", "Room Scale Lean Radius", RoomWalkState.DefaultLeanRadius,
                new ConfigDescription("Horizontal lean allowance before immediate body following, in meters before VR scaling. Within this area the body catches up smoothly at Room Scale Body Recenter Speed. Obstacles stop the capsule while head and hand tracking remain free. Zero follows immediately; F10 resets the center.", new AcceptableValueRange<float>(0f, .5f)));
            roomRecenterSpeed = userSettings.Bind("Input", "Room Scale Body Recenter Speed", RoomWalkState.DefaultRecenterSpeed,
                new ConfigDescription("How quickly the body settles underneath the tracked head, in inverse seconds. The entire route and final capsule position must be clear before any automatic movement. Head and hand world poses stay fixed. Zero retains the full lean dead zone.", new AcceptableValueRange<float>(0f, 12f)));
            bodyWidth = userSettings.Bind("Input", "VR Body Width", VrBodyCollider.DefaultWidth,
                new ConfigDescription("Player collision capsule width relative to the native game while VR is active. Default 0.55 is 45% narrower; 1 restores native width. Height, crouching and eye level are preserved.", new AcceptableValueRange<float>(.5f, 1f)));
            physicalCrouch = userSettings.Bind("Input", "Height Crouch", true, "Continuously lower the player capsule with calibrated headset height; tracked head motion controls the view without an extra crouch drop. Left X overrides crouch/stand; F10 recalibrates and restores automatic mode.");
            crouchThreshold = userSettings.Bind("Input", "Crouch Height Threshold", .75f, new ConfigDescription("Fraction of calibrated height below which crouch begins. Stand again 0.10 above this threshold. Calibrate comfortably upright with F10, standing or seated.", new AcceptableValueRange<float>(.4f, .9f)));
            showPrompt = userSettings.Bind("UI", "Show Interaction Prompt", true, "Display a world-space interaction hint in VR. Other desktop UI is unchanged.");
            showDialogue = userSettings.Bind("UI", "Show Dialogue", true, "Show dialogue text and answers in VR. Aim right controller and press its index trigger to continue or choose.");
            dialogueWidth = userSettings.Bind("UI", "Dialogue Width", 1.2f, new ConfigDescription("Dialogue panel width in tracking-space meters.", new AcceptableValueRange<float>(.5f, 2f)));
            dialogueDistance = userSettings.Bind("UI", "Dialogue Distance", 1.6f, new ConfigDescription("Distance from headset when dialogue opens. F10 places the panel in front again.", new AcceptableValueRange<float>(.7f, 3f)));
            pointerPitch = userSettings.Bind("UI", "Pointer Pitch Offset", 0f, new ConfigDescription("Controller ray pitch adjustment in degrees; useful for OpenVR controller pose conventions.", new AcceptableValueRange<float>(-60f, 60f)));
            showSpongeSupply = userSettings.Bind("UI", "Show Sponge Supply", false, "Show the extra equipped-sponge supply bar in the VR HUD.");
            showHud = userSettings.Bind("UI", "Show Progress HUD", true, "Show game progress bars and equipped sponge supply at the upper-left of the headset view.");
            hudScale = userSettings.Bind("UI", "HUD Scale", 1f, new ConfigDescription("Progress HUD size multiplier.", new AcceptableValueRange<float>(.5f, 1.5f)));
            hudHorizontal = userSettings.Bind("UI", "HUD Horizontal Offset", -.5f, new ConfigDescription("Upper-left HUD edge horizontally in head space, at 1.2 meters depth.", new AcceptableValueRange<float>(-1f, 0f)));
            hudVertical = userSettings.Bind("UI", "HUD Vertical Offset", .4f, new ConfigDescription("Upper-left HUD edge vertically in head space, at 1.2 meters depth.", new AcceptableValueRange<float>(0f, .8f)));
            try
            {
                harmony = new Harmony(Id);
                renderStage = new VrRenderStage(RenderFrame);
                Type managerType = AccessTools.TypeByName("UnityVRMod.Features.VrVisualization.VrVisualizationManager");
                Patch(managerType, "Update", nameof(ManagerPrefix), null);
                Patch(AccessTools.TypeByName("UnityVRMod.Features.Util.CameraFinder"), "FindGameCamera", null, nameof(CreditsCameraSelected));
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
                Patch(typeof(MassSpringController), "SetCapsuleToPosture", null, nameof(BodyPostureChanged));
                Patch(typeof(MassSpringController), "SetPosture", nameof(PhysicalPosture), null);
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
            && !locomotion.HasCutscene && !FreecamRequested
            && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused);
        private ConfigEntry<bool> cutsceneFreecam;
        private readonly FreecamState freecam = new FreecamState();
        private GameObject freecamRig;
        private bool FreecamRequested => cutsceneFreecam != null && cutsceneFreecam.Value && IsSceneView;
        private bool sceneSnapLatched;
        private static readonly FieldInfo sceneInstance = AccessTools.Field(typeof(WalkNWashSceneState), "instance");
        private static readonly FieldInfo sceneEnding = AccessTools.Field(typeof(WalkNWashSceneState), "isSexScene");
        private bool originWasSceneView;
        private GameObject sceneViewCamera;
        private WalkNWashSceneState originScene;
        private bool IsSceneView
        {
            get
            {
                var state = sceneInstance.GetValue(null) as WalkNWashSceneState;
                return (state && (bool)sceneEnding.GetValue(state)) || (locomotion != null && locomotion.HasCutscene);
            }
        }
        private bool CanSceneTurn
        {
            get
            {
                if (!VrActive || !controllers.Value || !backend.Focused || Calibrating || ScreenActive
                    || quit.Pending || quit.Ready || (GameStateManager.Instance != null && GameStateManager.Instance.IsPaused)) return false;
                return IsSceneView;
            }
        }
        private float SceneTurn()
        {
            if (FreecamRequested || !CanSceneTurn || CanControl) { sceneSnapLatched = false; return 0; }
            backend.Poll();
            if (!smoothTurning.Value) return ControlMath.Snap(backend.Turn.x, snapDegrees.Value, ref sceneSnapLatched);
            sceneSnapLatched = false;
            return ControlMath.SmoothTurn(backend.Turn.x, deadzone.Value, turnSpeed.Value, Time.unscaledDeltaTime);
        }
        private bool CanAim => ControllerContext && MenuManager.actions.Player.Look.enabled;
        private bool CanControl => !Calibrating && ControllerContext && MenuManager.actions.Player.Move.enabled;
        internal bool HandsMode => VrActive && controllers.Value && calibrated && calibratedRig == backend.Rig;
        internal Transform HandRig => backend?.Rig ? backend.Rig.transform : null;
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
            Vector3 wrist = hand == 1 ? leftWristOffset.Value : rightWristOffset.Value;
            if (Backend.Finite(wrist)) offset += rotation * wrist;
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
            try
            {
                if (settingsPanel == null) settingsPanel = new UserSettingsPanel(userSettings, ConfigVersionReset.GetPluginVersion(), Recenter);
                bool wasOpen = settingsPanel.Active;
                settingsPanel.Tick(backend, VrActive && backend.Focused && !QuickTests.Active && !(gripEditor?.Active ?? false));
                if (settingsPanel.Active || wasOpen) { ReleaseButtons(); menuHold = default; }
            }
            catch (Exception e) { settingsPanel?.Close(); Logger.LogError("Preferences panel failed: " + e); }
            try { quickTests.Tick(backend, VrActive && backend.Focused && !(settingsPanel?.Active ?? false)); }
            catch (Exception e) { quickTests.Close(); Logger.LogError("Quick tests failed: " + e); }
            try
            {
                if (gripEditor == null) gripEditor = new GripEditor(this);
                bool wasEditing = Calibrating;
                gripEditor.Tick(backend, hands?.Grips, !(settingsPanel?.Active ?? false) && ControllerContext && controllers.Value && hands != null && hands.Active);
                if (Calibrating || wasEditing) { ReleaseButtons(); menuHold = default; }
            }
            catch (Exception e) { gripEditor?.Finish(false); Logger.LogError("VR calibration failed: " + e); }
            try
            {
                bool usable = !Calibrating && Enabled && controllers.Value && VrActive && backend.Focused && !quit.Pending && !quit.Ready;
                if (usable) backend.Poll();
                menuHold.Update(usable && backend.Menu, usable, Time.unscaledTime);
                if (menuHold.ShortPress) EscapeMenu.Press();
            }
            catch (Exception e) { menuHold = default; Logger.LogError("VR Escape failed: " + e); }
            if (!screenFailed)
            {
                try { if (VrActive) screen.Dispatch(backend, controllers.Value && !QuickTests.Active && !(settingsPanel?.Active ?? false)); else screen.Reset(); }
                catch (Exception e) { ScreenError(e); }
            }
            if (!hudToggleFailed)
            {
                try { UpdateViewToggle(); }
                catch (Exception e)
                {
                    hudToggleFailed = true;
                    Logger.LogError("Left Y view toggle unavailable; other controls remain active. " + e);
                }
            }
            if (dialogueFailed) return;
            try
            {
                bool visible = !(settingsPanel?.Active ?? false) && !QuickTests.Active && Enabled && showDialogue.Value && VrActive && !ScreenActive
                    && (GameStateManager.Instance == null || !GameStateManager.Instance.IsPaused);
                if (!visible) dialogue.Reset();
                else dialogue.Dispatch(controllers.Value && backend.Focused);
            }
            catch (Exception e) { DialogueError(e); }
        }

        private void UpdateViewToggle()
        {
            bool sceneView = IsSceneView;
            bool usable = !Calibrating && Enabled && controllers.Value && VrActive && backend.Focused
                && (!sceneView || CanSceneTurn);
            if (usable) backend.Poll();
            if (!hudToggle.Update(usable && backend.ToggleHud, usable)) return;
            if (sceneView)
            {
                cutsceneFreecam.Value = !cutsceneFreecam.Value;
                Logger.LogInfo("Cutscene freecam " + (cutsceneFreecam.Value ? "enabled" : "disabled") + " (left Y).");
            }
            else
            {
                showHud.Value = !showHud.Value;
                hud.Reset();
                Logger.LogInfo("Progress HUD " + (showHud.Value ? "shown" : "hidden") + " (left Y).");
            }
        }

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
                    usable ? backend.RightTrigger : 0, usable && backend.Jump, usable, leftTracked, rightTracked, handRoles.ToolOnLeft);
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
        private static void CreditsCameraSelected(ref Camera __result)
        {
            if (current != null && current.Enabled) __result = CreditsCamera.Resolve(__result);
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
            if (!Enabled) { bodyCollider.Dispose(); freecam.Reset(); freecamRig = null; return; }
            Guard(() =>
            {
                bodyCollider.Update(VrActive && player && player.isActiveAndEnabled ? player.GetComponent<CapsuleCollider>() : null, bodyWidth.Value);
                if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame) { handDiagnostics?.Toggle(); rightHandDiagnostics?.Toggle(); }
                if (Keyboard.current != null && Keyboard.current[recenterKey.Value].wasPressedThisFrame)
                {
                    Recenter();
                }
                if (VrActive) UpdateOrigin();
                else { freecam.Reset(); freecamRig = null; }
                hands?.RefreshVisuals();
            });
        }
        internal void Recenter()
        {
            freecam.Reset();
            roomWalk.Reset(); calibrated = false; poseFrame = -1; roomLeaning = false;
            handDiagnostics?.Recenter(); rightHandDiagnostics?.Recenter();
            menuRig = null; dialogue.Recenter(); screen.Recenter();
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
                finally { rendering = false; curtain.EndEye(); quickTests.EndEye(); settingsPanel?.EndEye(); prompt.EndEye(); dialogue.EndEye(); hud.EndEye(); screen.EndEye(); gripEditor?.EndEye(); handDiagnostics?.EndEye(); rightHandDiagnostics?.EndEye(); }
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
            current.Guard(() => current.settingsPanel?.BeginEye(current.backend));
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
                    current.dialogue.BeginEye(current.backend, !(current.settingsPanel?.Active ?? false) && !QuickTests.Active && current.showDialogue.Value && current.VrActive && !current.ScreenActive
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
                    current.controllers.Value && !current.buttonInputFailed, current.FreeHand);
            }
            catch (Exception e)
            {
                current.promptFailed = true;
                current.prompt.EndEye();
                current.Logger.LogError("VR interaction prompt disabled; other controls remain active. " + e);
            }
        }
        private static void AfterEye() { current?.curtain.EndEye(); current?.quickTests.EndEye(); current?.settingsPanel?.EndEye(); current?.prompt.EndEye(); current?.dialogue.EndEye(); current?.hud.EndEye(); current?.screen.EndEye(); current?.gripEditor?.EndEye(); current?.handDiagnostics?.EndEye(); current?.rightHandDiagnostics?.EndEye(); }
        private static void PromptShown(Vector3 __0) { current?.prompt.Show(__0); }
        private static void PromptHidden() { current?.prompt.Hide(); }
        private void UpdateOrigin()
        {
            if (poseFrame == Time.frameCount) return;
            poseFrame = Time.frameCount;
            UpdateNativeOrigin();
            UpdateFreecam();
        }
        private void UpdateFreecam()
        {
            var rig = backend.Rig;
            if (!FreecamRequested || !rig) { freecam.Reset(); freecamRig = null; return; }
            bool tracked = backend.HeadPose(out var head, out var rotation);
            if (freecamRig != rig) { freecam.Reset(); freecamRig = rig; }
            if (!freecam.Active)
            {
                if (!tracked) return;
                var position = rig.transform.position;
                freecam.Enter(position.x, position.y, position.z, rig.transform.eulerAngles.y);
            }
            // Dialogue disables native gameplay, but does not own the freecam sticks.
            // Ending scenes can keep Yarn running for their entire duration.
            bool usable = tracked && CanSceneTurn;
            if (usable) backend.Poll();
            freecam.Step(usable, backend.Move.x, backend.Move.y, backend.Turn.x, backend.Turn.y,
                backend.Jump, backend.ToggleCrouch, head.x, head.z, rotation.eulerAngles.y, rig.transform.localScale.x,
                Time.unscaledDeltaTime, deadzone.Value, smoothTurning.Value, turnSpeed.Value, snapDegrees.Value);
            rig.transform.SetPositionAndRotation(new Vector3(freecam.X, freecam.Y, freecam.Z), Quaternion.Euler(0, freecam.Yaw, 0));
        }
        private void UpdateNativeOrigin()
        {
            bool sceneView = IsSceneView;
            var authoredCamera = Backend.Field(backend.Setup, "_currentlyTrackedOriginalCameraGO") as GameObject;
            var state = sceneInstance.GetValue(null) as WalkNWashSceneState;
            if (sceneView != originWasSceneView || originScene != state) freecam.Reset();
            if (sceneView != originWasSceneView || (sceneView && (sceneViewCamera != authoredCamera || originScene != state)))
            {
                calibrated = false; menuRig = null; sceneSnapLatched = false;
                roomWalk.Reset(); screen.Recenter(); dialogue.Recenter();
            }
            originWasSceneView = sceneView; sceneViewCamera = authoredCamera; originScene = state;
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
                roomLeaning = false;
                float startingYaw = sceneView && authoredCamera ? authoredCamera.transform.eulerAngles.y : look.LookYaw;
                rigYaw = startingYaw - rotation.eulerAngles.y;
                calibrated = true;
                calibratedRig = rig;
                if (!crouchFailed)
                {
                    try
                    {
                        if (crouch == null) crouch = new VrCrouch(player, bodyCollider.NativeRadius(player.GetComponent<CapsuleCollider>()));
                        crouch.State.Calibrate(head.y);
                    }
                    catch (Exception e) { CrouchError(e); }
                }
                Logger.LogInfo("Camera calibrated to player eyes. Backend: " + (backend.IsOpenXr ? "OpenXR" : "OpenVR") + "; physical head: " + head);
            }
            rigYaw += SceneTurn();
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
                bool leaning = RoomWalkState.Leaning(head.y, baseline.y,
                    (headRotation * Vector3.up).y, ref roomLeaning);
                bool usable = !roomWalkFailed && roomWalking.Value && !Calibrating && ControllerContext
                    && MenuManager.actions.Player.Move.enabled && locomotion.isActiveAndEnabled;
                var rig = backend.Rig.transform;
                if (rig.localScale.x <= 0 || !Backend.Finite(rig.lossyScale)) { roomWalk.Reset(); return; }
                var moved = RoomWalk.Follow(player.GetComponent<Rigidbody>(), player.GetComponent<CapsuleCollider>(),
                    ref roomWalk, ref baseline, head, Time.unscaledTime, usable, roomLeanRadius.Value, rigYaw, rig.localScale.x, roomRecenterSpeed.Value, !leaning);
                if (moved == Vector3.zero) return;
                var field = AccessTools.Field(typeof(LookController), "lastViewMatrix");
                // Use the published body pose, not a translated, potentially stale
                // interpolated camera matrix. Native LateUpdate can safely read it again.
                field.SetValue(look, look.transform.localToWorldMatrix);
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
            menuYaw += SceneTurn();
            var anchor = camera.transform.position;
            ControlMath.Origin(anchor.x, anchor.y, anchor.z,
                menuBaseline.x, menuBaseline.y, menuBaseline.z,
                menuYaw, rig.transform.localScale.x, out float x, out float y, out float z);
            rig.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, menuYaw, 0));
        }
        private static void PhysicalPosture(MassSpringController __instance, ref float __0)
        {
            var self = current;
            if (self == null || self.crouchFailed || self.crouch == null || !self.physicalCrouch.Value
                || !self.crouch.State.HeightDriven || self.Calibrating || !self.ControllerContext
                || !MenuManager.actions.Player.Crouch.enabled || __0 > 1
                || __instance.gameObject != self.player.gameObject) return;
            try
            {
                if (self.backend.HeadPose(out var head, out _))
                    __0 = self.crouch.CapsulePosture(head.y, self.HandScale);
            }
            catch (Exception e) { self.CrouchError(e); }
        }

        private static void BodyPostureChanged(MassSpringController __instance)
        {
            current?.bodyCollider.AfterPosture(__instance.GetComponent<CapsuleCollider>());
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
                current.freecam.Reset(); current.freecamRig = null;
                current.bodyCollider.Dispose();
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
                current.freecam.Reset(); current.freecamRig = null;
                current.bodyCollider.Dispose();
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
            yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            quit.Complete();
            Application.Quit();
        }
        private void OnDestroy()
        {
            bodyCollider.Dispose();
            Application.wantsToQuit -= WantsToQuit;
            renderStage?.Dispose();
            InputSystem.onBeforeUpdate -= BeforeInputUpdate;
            settingsPanel?.Dispose(); quickTests.Dispose();
            prompt.Dispose();
            dialogue.Dispose();
            hud.Dispose(); screen.Dispose(); curtain.Dispose();
            handDiagnostics?.Dispose(); rightHandDiagnostics?.Dispose();
            gripEditor?.Dispose();
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
