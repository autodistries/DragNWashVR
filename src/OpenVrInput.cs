using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    // Use UnityVRMod's own Valve.VR bindings and session, avoiding a second VR runtime.
    internal sealed class OpenVrInput : IControllerInput
    {
        private readonly object system;
        private readonly MethodInfo role, state, property;
        private readonly Type roleType, stateType, propertyType, errorType;
        private readonly float[] squeeze = new float[2];
        private readonly Action<string> log;
        private readonly string[] status = new string[2];

        internal OpenVrInput(object system, Action<string> log = null)
        {
            this.system = system ?? throw new InvalidOperationException("OpenVR system is null");
            this.log = log;
            Type type = system.GetType();
            role = AccessTools.Method(type, "GetTrackedDeviceIndexForControllerRole");
            state = AccessTools.Method(type, "GetControllerState");
            property = AccessTools.Method(type, "GetInt32TrackedDeviceProperty");
            roleType = role.GetParameters()[0].ParameterType;
            stateType = state.GetParameters()[1].ParameterType.GetElementType();
            propertyType = property.GetParameters()[1].ParameterType;
            errorType = property.GetParameters()[2].ParameterType.GetElementType();
        }

        internal static bool IsFocused(object system)
        {
            if (system == null) return false;
            var method = AccessTools.Method(system.GetType(), "IsInputAvailable");
            return method != null && (bool)method.Invoke(system, null);
        }

        private Vector2 Read(int hand, out float trigger, out bool jump, out bool hudToggle, out bool crouchToggle, out bool menu)
        {
            trigger = 0;
            jump = false;
            hudToggle = crouchToggle = menu = false;
            uint index = (uint)role.Invoke(system, new[] { Enum.ToObject(roleType, hand) });
            if (index == uint.MaxValue)
            { Report(hand, "unavailable: no left/right controller role assigned"); return Vector2.zero; }
            object[] args = { index, Activator.CreateInstance(stateType), (uint)Marshal.SizeOf(stateType) };
            if (!(bool)state.Invoke(system, args))
            { Report(hand, "unavailable: GetControllerState failed for device " + index); return Vector2.zero; }
            ulong pressed = Convert.ToUInt64(Backend.Field(args[1], "ulButtonPressed"));
            var axes = new ControllerAxes();
            int triggerType = 0;
            for (int i = 0; i < 5; i++)
            {
                object[] propArgs = { index, Enum.ToObject(propertyType, 3002 + i), Enum.ToObject(errorType, 0) };
                int axisType = (int)property.Invoke(system, propArgs);
                if (Convert.ToInt32(propArgs[2]) != 0) continue;
                axes.Observe(i, axisType);
                if (i == 1) triggerType = axisType;
            }
            jump = JumpBinding.OpenVrPressed(hand, pressed, axes.TrackpadOnly);
            hudToggle = HudToggleBinding.OpenVrPressed(hand, pressed);
            crouchToggle = CrouchBinding.OpenVrPressed(hand, pressed, axes.TrackpadOnly);
            menu = MenuHold.OpenVrPressed(hand, pressed);
            trigger = TriggerBinding.OpenVrValue(triggerType,
                Convert.ToSingle(Backend.Field(Backend.Field(args[1], "rAxis1"), "x")),
                pressed);
            squeeze[hand - 1] = axes.TrackpadOnly ? ((pressed & (1UL << 2)) != 0 ? 1 : 0)
                : Mathf.Clamp01(Convert.ToSingle(Backend.Field(Backend.Field(args[1], "rAxis2"), "x")));
            Report(hand, "device " + index + (axes.Index < 0 ? ": no joystick/trackpad axis reported"
                : (axes.TrackpadOnly ? ": trackpad axis " : ": joystick axis ") + axes.Index));
            if (axes.Index < 0) return Vector2.zero;
            object value = Backend.Field(args[1], "rAxis" + axes.Index);
            return new Vector2(Convert.ToSingle(Backend.Field(value, "x")), Convert.ToSingle(Backend.Field(value, "y")));
        }

        private void Report(int hand, string message)
        {
            if (status[hand - 1] == message) return;
            status[hand - 1] = message;
            log?.Invoke("OpenVR " + (hand == 1 ? "left" : "right") + " controller " + message);
        }

        public void Poll(out Vector2 move, out Vector2 turn, out float leftTrigger, out float rightTrigger, out bool jump, out bool hudToggle, out bool crouchToggle, out bool menu)
        { squeeze[0] = squeeze[1] = 0; move = Read(1, out leftTrigger, out _, out hudToggle, out crouchToggle, out _); turn = Read(2, out rightTrigger, out jump, out _, out _, out menu); }
        public void Dispose() { }

        internal bool Aim(Array poses, out Vector3 position, out Quaternion rotation)
            => Hand(poses, 2, out position, out rotation);

        internal bool Hand(Array poses, int hand, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            uint index = (uint)role.Invoke(system, new[] { Enum.ToObject(roleType, hand) });
            if (poses == null || index >= poses.Length) return false;
            return Backend.TrackedPose(poses.GetValue((int)index), out position, out rotation);
        }
        public bool Hand(int hand, bool aim, ulong space, long time, Array poses, out Vector3 position, out Quaternion rotation)
            => Hand(poses, hand, out position, out rotation);
        public float Squeeze(int hand) => squeeze[hand - 1];
        public void Pulse(int hand, float strength)
        {
            uint index = (uint)role.Invoke(system, new[] { Enum.ToObject(roleType, hand) });
            if (index == uint.MaxValue) return;
            AccessTools.Method(system.GetType(), "TriggerHapticPulse")?.Invoke(system,
                new object[] { index, 0u, (ushort)Mathf.Clamp(strength * 3999, 1, 3999) });
        }
    }
}
