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

        internal OpenVrInput(object system)
        {
            this.system = system ?? throw new InvalidOperationException("OpenVR system is null");
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

        private Vector2 Read(int hand, out float trigger, out bool jump, out bool hudToggle, out bool crouchToggle)
        {
            trigger = 0;
            jump = false;
            hudToggle = crouchToggle = false;
            uint index = (uint)role.Invoke(system, new[] { Enum.ToObject(roleType, hand) });
            if (index == uint.MaxValue) return Vector2.zero;
            object[] args = { index, Activator.CreateInstance(stateType), (uint)Marshal.SizeOf(stateType) };
            if (!(bool)state.Invoke(system, args)) return Vector2.zero;
            jump = JumpBinding.OpenVrPressed(hand, Convert.ToUInt64(Backend.Field(args[1], "ulButtonPressed")));
            hudToggle = HudToggleBinding.OpenVrPressed(hand, Convert.ToUInt64(Backend.Field(args[1], "ulButtonPressed")));
            crouchToggle = CrouchBinding.OpenVrPressed(hand, Convert.ToUInt64(Backend.Field(args[1], "ulButtonPressed")));
            int axis = 0;
            int triggerType = 0;
            for (int i = 0; i < 5; i++)
            {
                object[] propArgs = { index, Enum.ToObject(propertyType, 3002 + i), Enum.ToObject(errorType, 0) };
                int axisType = (int)property.Invoke(system, propArgs);
                if (Convert.ToInt32(propArgs[2]) != 0) continue;
                if (axisType == 2) axis = i;
                if (i == 1) triggerType = axisType;
            }
            trigger = TriggerBinding.OpenVrValue(triggerType,
                Convert.ToSingle(Backend.Field(Backend.Field(args[1], "rAxis1"), "x")),
                Convert.ToUInt64(Backend.Field(args[1], "ulButtonPressed")));
            object value = Backend.Field(args[1], "rAxis" + axis);
            squeeze[hand - 1] = Mathf.Clamp01(Convert.ToSingle(Backend.Field(Backend.Field(args[1], "rAxis2"), "x")));
            return new Vector2(Convert.ToSingle(Backend.Field(value, "x")), Convert.ToSingle(Backend.Field(value, "y")));
        }

        public void Poll(out Vector2 move, out Vector2 turn, out float leftTrigger, out float rightTrigger, out bool jump, out bool hudToggle, out bool crouchToggle)
        { squeeze[0] = squeeze[1] = 0; move = Read(1, out leftTrigger, out _, out hudToggle, out crouchToggle); turn = Read(2, out rightTrigger, out jump, out _, out _); }
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
