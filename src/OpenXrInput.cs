using System;
using System.Runtime.InteropServices;
using UnityEngine;
using static WalkNWash.VRCompanion.OpenXrNative;

namespace WalkNWash.VRCompanion
{
    internal sealed class OpenXrInput : IControllerInput
    {
        private readonly ulong instance, session;
        private ulong set, moveAction, turnAction, leftTriggerAction, rightTriggerAction;
        private readonly GetProc getProc;
        private readonly StringToPath toPath;
        private readonly CreateAction createAction;
        private readonly SetOperation sync;
        private readonly ReadVector read;
        private readonly ReadFloat readFloat;
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
            destroy = Load<DestroySet>("xrDestroyActionSet");
            try
            {
                var info = new ActionSetInfo { type = 28, name = "walknwash_companion", localizedName = "Walk N Wash VR", priority = 0 };
                Check(Load<CreateSet>("xrCreateActionSet")(instance, ref info, out set), "xrCreateActionSet");
                moveAction = Create("move", "Move", 3);
                turnAction = Create("turn", "Turn", 3);
                leftTriggerAction = Create("secondary", "Secondary action", 2);
                rightTriggerAction = Create("primary", "Interact or use hand", 2);
                var suggest = Load<Suggest>("xrSuggestInteractionProfileBindings");
                string[] profiles = { "oculus/touch_controller", "valve/index_controller", "microsoft/motion_controller", "htc/vive_controller" };
                int accepted = 0;
                foreach (string profile in profiles)
                {
                    string axis = profile.StartsWith("htc/") ? "trackpad" : "thumbstick";
                    using (var bindings = new NativeArray<Binding>(
                        new Binding { action = moveAction, path = Path("/user/hand/left/input/" + axis) },
                        new Binding { action = turnAction, path = Path("/user/hand/right/input/" + axis) },
                        new Binding { action = leftTriggerAction, path = Path("/user/hand/left/input/trigger/value") },
                        new Binding { action = rightTriggerAction, path = Path("/user/hand/right/input/trigger/value") }))
                    {
                        var suggested = new SuggestedBindings { type = 51, profile = Path("/interaction_profiles/" + profile), count = 4, bindings = bindings.Pointer };
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
        public void Poll(out Vector2 move, out Vector2 turn, out float leftTrigger, out float rightTrigger)
        {
            move = turn = Vector2.zero;
            leftTrigger = rightTrigger = 0;
            var info = new SetList { type = 61, count = 1, sets = active.Pointer };
            int result = sync(session, ref info);
            if (result == 8) return; // XR_SESSION_NOT_FOCUSED: release all input.
            Check(result, "xrSyncActions");
            move = Read(moveAction);
            turn = Read(turnAction);
            leftTrigger = ReadTrigger(leftTriggerAction);
            rightTrigger = ReadTrigger(rightTriggerAction);
        }
        public void Dispose()
        {
            active?.Dispose();
            active = null;
            if (set != 0) { destroy(set); set = 0; }
        }
    }
}
