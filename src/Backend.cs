using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal sealed class Backend : IDisposable
    {
        internal readonly object Setup;
        internal readonly bool IsOpenXr;
        private IControllerInput input;
        private bool inputAttempted;
        private bool disposed, initializationFailed, pollingFailed, requireNeutral;
        private float nextInputAttempt, nextPollAttempt;
        private readonly Action<string> log;
        private int pollFrame = -1;
        private bool hapticsFailed;
        internal Vector2 Move, Turn;
        internal float LeftTrigger, RightTrigger;
        internal bool Jump, ToggleHud, ToggleCrouch, Menu;
        private static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();

        internal Backend(object setup, Action<string> log)
        {
            Setup = setup;
            IsOpenXr = setup.GetType().Name.EndsWith("OpenXR");
            this.log = log;
        }

        internal static object Field(object value, string name)
        {
            if (value == null) return null;
            string key = value.GetType().AssemblyQualifiedName + ":" + name;
            if (!fields.TryGetValue(key, out var field))
            {
                field = AccessTools.Field(value.GetType(), name);
                if (field == null) throw new MissingFieldException(value.GetType().FullName, name);
                fields[key] = field;
            }
            return field.GetValue(value);
        }

        private static float Number(object value, string name) => Convert.ToSingle(Field(value, name));
        internal GameObject Rig => Field(Setup, "_vrRig") as GameObject;
        internal bool Focused => IsOpenXr
            ? Convert.ToInt32(Field(Setup, "_currentSessionState")) == 5
            : OpenVrInput.IsFocused(Field(Setup, "_hmd"));

        internal void InitializeInput()
        {
            if (disposed || input != null || Time.unscaledTime < nextInputAttempt) return;
            inputAttempted = true;
            nextInputAttempt = Time.unscaledTime + 2f;
            try
            {
                input = IsOpenXr
                    ? (IControllerInput)new OpenXrInput(Convert.ToUInt64(Field(Setup, "_xrInstance")),
                        Convert.ToUInt64(Field(Setup, "_xrSession")), log)
                    : new OpenVrInput(Field(Setup, "_hmd"), log);
                requireNeutral = initializationFailed;
                initializationFailed = false;
                log((IsOpenXr ? "OpenXR" : "OpenVR") + " controller input attached.");
            }
            catch (Exception e)
            {
                if (!initializationFailed)
                    log("Controller input unavailable; will retry while VR is active: " + (e.InnerException ?? e).Message);
                initializationFailed = true;
            }
        }

        internal void Poll()
        {
            if (disposed || pollFrame == Time.frameCount) return;
            pollFrame = Time.frameCount;
            ClearInput();
            if (input == null && inputAttempted) InitializeInput();
            if (input == null || !Focused || Time.unscaledTime < nextPollAttempt) return;
            try
            {
                input.Poll(out Move, out Turn, out LeftTrigger, out RightTrigger, out Jump, out ToggleHud, out ToggleCrouch, out Menu);
                if (pollingFailed) log("Controller input recovered; release controls to resume.");
                pollingFailed = false;
                if (requireNeutral)
                {
                    requireNeutral = !CrouchState.Valid(Move.x) || !CrouchState.Valid(Move.y)
                        || !CrouchState.Valid(Turn.x) || !CrouchState.Valid(Turn.y)
                        || !CrouchState.Valid(LeftTrigger) || !CrouchState.Valid(RightTrigger)
                        || Move.sqrMagnitude > .04f || Turn.sqrMagnitude > .04f
                        || LeftTrigger > .35f || RightTrigger > .35f || Jump || ToggleHud || ToggleCrouch || Menu;
                    ClearInput();
                }
            }
            catch (Exception e) { InputError(e); }
        }

        private void ClearInput()
        {
            Move = Turn = Vector2.zero;
            LeftTrigger = RightTrigger = 0;
            Jump = ToggleHud = ToggleCrouch = Menu = false;
        }

        private void InputError(Exception e)
        {
            if (!pollingFailed) log("Controller polling interrupted; will retry: " + (e.InnerException ?? e).Message);
            pollingFailed = requireNeutral = true;
            nextPollAttempt = Time.unscaledTime + 2f;
            ClearInput();
            // Keep the action set alive: OpenXR only allows attaching once per session.
        }

        internal bool HeadPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (IsOpenXr)
            {
                object state = Field(Setup, "_locatedViewState");
                if ((Convert.ToUInt64(Field(state, "viewStateFlags")) & 3) != 3) return false;
                var views = Field(Setup, "_locatedViews") as Array;
                if (views == null || views.Length < 2) return false;
                object pose = Field(views.GetValue(0), "pose");
                object left = Field(pose, "position");
                object right = Field(Field(views.GetValue(1), "pose"), "position");
                position = new Vector3((Number(left, "x") + Number(right, "x")) / 2,
                    (Number(left, "y") + Number(right, "y")) / 2,
                    -(Number(left, "z") + Number(right, "z")) / 2);
                object q = Field(pose, "orientation");
                rotation = new Quaternion(Number(q, "x"), Number(q, "y"), -Number(q, "z"), -Number(q, "w"));
            }
            else
            {
                var poses = Field(Setup, "_trackedPoses") as Array;
                if (poses == null || poses.Length == 0) return false;
                object pose = poses.GetValue(0);
                if (!(bool)Field(pose, "bPoseIsValid") || !(bool)Field(pose, "bDeviceIsConnected")) return false;
                object m = Field(pose, "mDeviceToAbsoluteTracking");
                position = new Vector3(Number(m, "m3"), Number(m, "m7"), -Number(m, "m11"));
                rotation = Quaternion.LookRotation(new Vector3(-Number(m, "m2"), -Number(m, "m6"), Number(m, "m10")),
                    new Vector3(Number(m, "m1"), Number(m, "m5"), -Number(m, "m9")));
            }
            return !(float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z));
        }

        internal void Render() => AccessTools.Method(Setup.GetType(), "UpdatePoses").Invoke(Setup, null);
        internal bool Aim(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (disposed || pollingFailed || !Focused) return false;
            try
            {
                if (input is OpenXrInput xr)
                    return xr.Aim(Convert.ToUInt64(Field(Setup, "_appSpace")),
                        Convert.ToInt64(Field(Field(Setup, "_xrFrameState"), "predictedDisplayTime")), out position, out rotation);
                return input is OpenVrInput vr && vr.Aim(Field(Setup, "_trackedPoses") as Array, out position, out rotation);
            }
            catch (Exception e) { InputError(e); return false; }
        }

        internal bool Hand(int hand, bool aim, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero; rotation = Quaternion.identity;
            if (disposed || pollingFailed || hand < 1 || hand > 2 || !Focused) return false;
            if (input == null) return false;
            ulong space = IsOpenXr ? Convert.ToUInt64(Field(Setup, "_appSpace")) : 0;
            long time = IsOpenXr ? Convert.ToInt64(Field(Field(Setup, "_xrFrameState"), "predictedDisplayTime")) : 0;
            var poses = IsOpenXr ? null : Field(Setup, "_trackedPoses") as Array;
            try
            {
                bool valid = input.Hand(hand, aim, space, time, poses, out position, out rotation);
                return valid && Finite(position) && Finite(rotation);
            }
            catch (Exception e) { InputError(e); return false; }
        }
        internal float Squeeze(int hand)
        {
            if (disposed || pollingFailed || requireNeutral || !Focused) return 0;
            try { return input?.Squeeze(hand) ?? 0; }
            catch (Exception e) { InputError(e); return 0; }
        }
        internal void Pulse(int hand, float strength)
        {
            if (hapticsFailed || !Focused) return;
            try
            {
                input?.Pulse(hand, strength);
            }
            catch (Exception e) { hapticsFailed = true; log("Contact haptics stopped: " + e.Message); }
        }
        internal static bool Finite(Vector3 p) => CrouchState.Valid(p.x) && CrouchState.Valid(p.y) && CrouchState.Valid(p.z);
        internal static bool Finite(Quaternion q) => CrouchState.Valid(q.x) && CrouchState.Valid(q.y) && CrouchState.Valid(q.z)
            && CrouchState.Valid(q.w) && q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > .5f;

        internal static bool TrackedPose(object pose, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (pose == null || !(bool)Field(pose, "bPoseIsValid") || !(bool)Field(pose, "bDeviceIsConnected")) return false;
            object m = Field(pose, "mDeviceToAbsoluteTracking");
            position = new Vector3(Number(m, "m3"), Number(m, "m7"), -Number(m, "m11"));
            rotation = Quaternion.LookRotation(new Vector3(-Number(m, "m2"), -Number(m, "m6"), Number(m, "m10")),
                new Vector3(Number(m, "m1"), Number(m, "m5"), -Number(m, "m9")));
            return !(float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z));
        }
        public void Dispose()
        {
            disposed = true;
            try { input?.Dispose(); }
            finally { input = null; ClearInput(); }
        }
    }

    internal interface IControllerInput : IDisposable
    {
        bool Hand(int hand, bool aim, ulong space, long time, Array poses, out Vector3 position, out Quaternion rotation);
        float Squeeze(int hand);
        void Pulse(int hand, float strength);
        void Poll(out Vector2 move, out Vector2 turn, out float leftTrigger, out float rightTrigger, out bool jump, out bool hudToggle, out bool crouchToggle, out bool menu);
    }
}
