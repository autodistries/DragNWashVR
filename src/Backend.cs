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
        private readonly Action<string> log;
        private int pollFrame = -1;
        internal Vector2 Move, Turn;
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
            if (inputAttempted) return;
            inputAttempted = true;
            try
            {
                input = IsOpenXr
                    ? (IControllerInput)new OpenXrInput(Convert.ToUInt64(Field(Setup, "_xrInstance")),
                        Convert.ToUInt64(Field(Setup, "_xrSession")), log)
                    : new OpenVrInput(Field(Setup, "_hmd"));
                log((IsOpenXr ? "OpenXR" : "OpenVR") + " thumbstick input attached.");
            }
            catch (Exception e) { log("Controller input unavailable; camera follow remains active: " + e.Message); }
        }

        internal void Poll()
        {
            if (pollFrame == Time.frameCount) return;
            pollFrame = Time.frameCount;
            Move = Turn = Vector2.zero;
            if (input == null || !Focused) return;
            try { input.Poll(out Move, out Turn); }
            catch (Exception e)
            {
                log("Controller polling stopped: " + e.Message);
                input.Dispose();
                input = null;
            }
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
        public void Dispose() { input?.Dispose(); input = null; }
    }

    internal interface IControllerInput : IDisposable
    {
        void Poll(out Vector2 move, out Vector2 turn);
    }
}
