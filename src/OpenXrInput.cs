using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using static WalkNWash.VRCompanion.OpenXrNative;

namespace WalkNWash.VRCompanion
{
    internal sealed class OpenXrInput : IControllerInput
    {
        private readonly ulong instance, session;
        private ulong set, moveAction, turnAction, leftTriggerAction, rightTriggerAction, jumpAction;
        private ulong aimAction, aimSpace, hudToggleAction, crouchToggleAction, menuAction;
        private readonly ulong[] gripActions = new ulong[2], gripSpaces = new ulong[2];
        private readonly ulong[] handAimActions = new ulong[2], handAimSpaces = new ulong[2];
        private readonly ulong[] squeezeActions = new ulong[2], hapticActions = new ulong[2];
        private ApplyHaptic haptic;
        private LocateSpace locateSpace;
        private ReadPose readPose;
        private DestroySet destroySpace;
        private readonly GetProc getProc;
        private readonly StringToPath toPath;
        private readonly CreateAction createAction;
        private readonly SetOperation sync;
        private readonly ReadVector read;
        private readonly ReadFloat readFloat;
        private readonly ReadBoolean readBoolean;
        private readonly DestroySet destroy;
        private NativeArray<ActiveSet> active;

        internal OpenXrInput(ulong instance, ulong session, Action<string> log)
        {
            this.instance = instance;
            this.session = session;
            if (instance == 0 || session == 0) throw new InvalidOperationException("OpenXR session is null");
            IntPtr module = GetModuleHandleW("openxr_loader.dll");
            IntPtr entry = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "xrGetInstanceProcAddr");
            if (entry == IntPtr.Zero) throw new InvalidOperationException("Cannot find existing OpenXR loader");
            getProc = Marshal.GetDelegateForFunctionPointer<GetProc>(entry);
            toPath = Load<StringToPath>("xrStringToPath");
            createAction = Load<CreateAction>("xrCreateAction");
            sync = Load<SetOperation>("xrSyncActions");
            read = Load<ReadVector>("xrGetActionStateVector2f");
            readFloat = Load<ReadFloat>("xrGetActionStateFloat");
            readBoolean = Load<ReadBoolean>("xrGetActionStateBoolean");
            destroy = Load<DestroySet>("xrDestroyActionSet");
            try
            {
                var info = new ActionSetInfo { type = 28, name = "walknwash_companion", localizedName = "Walk N Wash VR", priority = 0 };
                Check(Load<CreateSet>("xrCreateActionSet")(instance, ref info, out set), "xrCreateActionSet");
                moveAction = Create("move", "Move", 3);
                turnAction = Create("turn", "Turn", 3);
                leftTriggerAction = Create("secondary", "Secondary action", 2);
                rightTriggerAction = Create("primary", "Interact or use hand", 2);
                jumpAction = Create("jump", "Jump", 1);
                hudToggleAction = Create("toggle_hud", "Toggle progress HUD", 1);
                menuAction = Create("menu_hold", "Pause or preferences", 1);
                crouchToggleAction = Create("toggle_crouch", "Override crouch", 1);
                aimAction = Create("right_aim", "Point at dialogue", 4);
                handAimActions[1] = aimAction;
                handAimActions[0] = Create("left_aim", "Left hand aim", 4);
                for (int i = 0; i < 2; i++)
                {
                    string side = i == 0 ? "left" : "right";
                    gripActions[i] = Create(side + "_grip_pose", side + " hand pose", 4);
                    squeezeActions[i] = Create(side + "_squeeze", side + " finger curl", 2);
                    hapticActions[i] = Create(side + "_haptic", side + " contact feedback", 100);
                }
                var suggest = Load<Suggest>("xrSuggestInteractionProfileBindings");
                string[] profiles = { "oculus/touch_controller", "valve/index_controller", "microsoft/motion_controller", "htc/vive_controller" };
                int accepted = 0;
                foreach (string profile in profiles)
                {
                    string axis = profile.StartsWith("htc/") ? "trackpad" : "thumbstick";
                    var profileBindings = new List<Binding> {
                        new Binding { action = moveAction, path = Path("/user/hand/left/input/" + axis) },
                        new Binding { action = turnAction, path = Path("/user/hand/right/input/" + axis) },
                        new Binding { action = leftTriggerAction, path = Path("/user/hand/left/input/trigger/value") },
                        new Binding { action = rightTriggerAction, path = Path("/user/hand/right/input/trigger/value") },
                        new Binding { action = aimAction, path = Path("/user/hand/right/input/aim/pose") }
                    };
                    for (int i = 0; i < 2; i++)
                    {
                        string hand = "/user/hand/" + (i == 0 ? "left" : "right");
                        profileBindings.Add(new Binding { action = gripActions[i], path = Path(hand + "/input/grip/pose") });
                        if (i == 0) profileBindings.Add(new Binding { action = handAimActions[i], path = Path(hand + "/input/aim/pose") });
                        profileBindings.Add(new Binding { action = hapticActions[i], path = Path(hand + "/output/haptic") });
                        if (profile == "oculus/touch_controller" || profile == "valve/index_controller")
                            profileBindings.Add(new Binding { action = squeezeActions[i], path = Path(hand + "/input/squeeze/value") });
                        else if (profile == "htc/vive_controller")
                            profileBindings.Add(new Binding { action = squeezeActions[i], path = Path(hand + "/input/squeeze/click") });
                    }
                    // Bind only controls available on this profile. Unsupported paths
                    // reject the entire profile, including tracking and movement.
                    string jumpPath = JumpBinding.OpenXrPath(profile);
                    if (jumpPath != null) profileBindings.Add(new Binding { action = jumpAction, path = Path(jumpPath) });
                    string hudPath = HudToggleBinding.OpenXrPath(profile);
                    if (hudPath != null) profileBindings.Add(new Binding { action = hudToggleAction, path = Path(hudPath) });
                    string menuPath = MenuHold.OpenXrPath(profile);
                    if (menuPath != null) profileBindings.Add(new Binding { action = menuAction, path = Path(menuPath) });
                    string crouchPath = CrouchBinding.OpenXrPath(profile);
                    if (crouchPath != null) profileBindings.Add(new Binding { action = crouchToggleAction, path = Path(crouchPath) });
                    using (var bindings = new NativeArray<Binding>(profileBindings.ToArray()))
                    {
                        var suggested = new SuggestedBindings { type = 51, profile = Path("/interaction_profiles/" + profile), count = (uint)profileBindings.Count, bindings = bindings.Pointer };
                        int result = suggest(instance, ref suggested);
                        if (result >= 0) accepted++;
                        else log("OpenXR binding profile skipped: " + profile + " (" + result + ")");
                    }
                }
                if (accepted == 0) throw new InvalidOperationException("No supported OpenXR controller profile");
                // UnityVRMod does not attach action sets. OpenXR permits attachment after
                // xrBeginSession, but only once per session; teardown owns this set too.
                using (var sets = new NativeArray<ulong>(set))
                {
                    var attach = new SetList { type = 60, count = 1, sets = sets.Pointer };
                    Check(Load<SetOperation>("xrAttachSessionActionSets")(session, ref attach), "xrAttachSessionActionSets");
                }
                active = new NativeArray<ActiveSet>(new ActiveSet { set = set });
                // Pointer setup may fail independently of existing gameplay input.
                try
                {
                    destroySpace = Load<DestroySet>("xrDestroySpace");
                    locateSpace = Load<LocateSpace>("xrLocateSpace");
                    readPose = Load<ReadPose>("xrGetActionStatePose");
                    var spaceInfo = new ActionSpaceInfo { type = 38, action = aimAction, pose = new OpenXrNative.Pose { qw = 1 } };
                    var createSpace = Load<CreateSpace>("xrCreateActionSpace");
                    Check(createSpace(session, ref spaceInfo, out aimSpace), "xrCreateActionSpace");
                    handAimSpaces[1] = aimSpace;
                    for (int i = 0; i < 2; i++)
                    {
                        spaceInfo.action = gripActions[i];
                        Check(createSpace(session, ref spaceInfo, out gripSpaces[i]), "grip space");
                        if (i == 0)
                        {
                            spaceInfo.action = handAimActions[i];
                            Check(createSpace(session, ref spaceInfo, out handAimSpaces[i]), "left aim space");
                        }
                    }
                    try { haptic = Load<ApplyHaptic>("xrApplyHapticFeedback"); }
                    catch (Exception e) { log("Contact haptics unavailable: " + e.Message); }
                }
                catch (Exception e) { log("Dialogue pointer unavailable; gameplay input remains active: " + e.Message); }
            }
            catch { Dispose(); throw; }
        }

        private T Load<T>(string name) where T : Delegate
        {
            Check(getProc(instance, name, out var pointer), name);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException(name + " unavailable");
            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }
        private static void Check(int result, string operation)
        {
            if (result < 0) throw new InvalidOperationException(operation + " failed: " + result);
        }
        private ulong Path(string path) { Check(toPath(instance, path, out var value), path); return value; }
        private ulong Create(string name, string label, int type)
        {
            var info = new ActionInfo { type = 29, name = name, localizedName = label, actionType = type };
            Check(createAction(set, ref info, out var action), "xrCreateAction " + name);
            return action;
        }
        private Vector2 Read(ulong action)
        {
            var info = new GetInfo { type = 58, action = action };
            var state = new VectorState { type = 25 };
            Check(read(session, ref info, ref state), "xrGetActionStateVector2f");
            return state.isActive != 0 ? new Vector2(state.x, state.y) : Vector2.zero;
        }
        private float ReadTrigger(ulong action)
        {
            var info = new GetInfo { type = 58, action = action };
            var state = new FloatState { type = 24 };
            Check(readFloat(session, ref info, ref state), "xrGetActionStateFloat");
            return state.isActive != 0 ? state.value : 0;
        }
        private bool ReadButton(ulong action)
        {
            var info = new GetInfo { type = 58, action = action };
            var state = new BooleanState { type = 23 };
            Check(readBoolean(session, ref info, ref state), "xrGetActionStateBoolean");
            return state.isActive != 0 && state.value != 0;
        }
        public void Poll(out Vector2 move, out Vector2 turn, out float leftTrigger, out float rightTrigger, out bool jump, out bool hudToggle, out bool crouchToggle, out bool menu)
        {
            move = turn = Vector2.zero;
            leftTrigger = rightTrigger = 0;
            jump = hudToggle = crouchToggle = menu = false;
            var info = new SetList { type = 61, count = 1, sets = active.Pointer };
            int result = sync(session, ref info);
            if (result == 8) return; // XR_SESSION_NOT_FOCUSED: release all input.
            Check(result, "xrSyncActions");
            move = Read(moveAction);
            turn = Read(turnAction);
            leftTrigger = ReadTrigger(leftTriggerAction);
            rightTrigger = ReadTrigger(rightTriggerAction);
            menu = ReadButton(menuAction);
            jump = ReadButton(jumpAction);
            hudToggle = ReadButton(hudToggleAction);
            crouchToggle = ReadButton(crouchToggleAction);
        }
        public void Dispose()
        {
            for (int i = 0; i < 2; i++)
            {
                if (gripSpaces[i] != 0) { destroySpace(gripSpaces[i]); gripSpaces[i] = 0; }
                if (handAimSpaces[i] != 0) { destroySpace(handAimSpaces[i]); handAimSpaces[i] = 0; }
            }
            aimSpace = 0;
            active?.Dispose();
            active = null;
            if (set != 0) { destroy(set); set = 0; }
        }

        internal bool Aim(ulong baseSpace, long time, out Vector3 position, out Quaternion rotation)
            => Locate(aimAction, aimSpace, baseSpace, time, out position, out rotation);

        internal bool Hand(int hand, bool aim, ulong baseSpace, long time, out Vector3 position, out Quaternion rotation)
            => Locate(aim ? handAimActions[hand - 1] : gripActions[hand - 1],
                aim ? handAimSpaces[hand - 1] : gripSpaces[hand - 1], baseSpace, time, out position, out rotation);
        public bool Hand(int hand, bool aim, ulong space, long time, Array poses, out Vector3 position, out Quaternion rotation)
            => Hand(hand, aim, space, time, out position, out rotation);
        public float Squeeze(int hand) => ReadTrigger(squeezeActions[hand - 1]);
        public void Pulse(int hand, float strength)
        {
            if (haptic == null) return;
            var info = new GetInfo { type = 59, action = hapticActions[hand - 1] };
            var vibration = new HapticVibration { type = 13, duration = 20000000, amplitude = Mathf.Clamp01(strength) };
            haptic(session, ref info, ref vibration);
        }
        private bool Locate(ulong action, ulong space, ulong baseSpace, long time, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (space == 0 || baseSpace == 0 || time <= 0) return false;
            var info = new GetInfo { type = 58, action = action };
            var state = new PoseState { type = 27 };
            if (readPose(session, ref info, ref state) < 0 || state.isActive == 0) return false;
            var location = new SpaceLocation { type = 42 };
            if (locateSpace(space, baseSpace, time, ref location) < 0 || (location.flags & 3) != 3) return false;
            var p = location.pose;
            position = new Vector3(p.x, p.y, -p.z);
            rotation = new Quaternion(p.qx, p.qy, -p.qz, -p.qw);
            return true;
        }
    }
}
