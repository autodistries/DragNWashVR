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

        private Vector2 Read(int hand)
        {
            uint index = (uint)role.Invoke(system, new[] { Enum.ToObject(roleType, hand) });
            if (index == uint.MaxValue) return Vector2.zero;
            object[] args = { index, Activator.CreateInstance(stateType), (uint)Marshal.SizeOf(stateType) };
            if (!(bool)state.Invoke(system, args)) return Vector2.zero;
            int axis = 0;
            for (int i = 0; i < 5; i++)
            {
                object[] propArgs = { index, Enum.ToObject(propertyType, 3002 + i), Enum.ToObject(errorType, 0) };
                int axisType = (int)property.Invoke(system, propArgs);
                if (Convert.ToInt32(propArgs[2]) == 0 && axisType == 2) { axis = i; break; }
            }
            object value = Backend.Field(args[1], "rAxis" + axis);
            return new Vector2(Convert.ToSingle(Backend.Field(value, "x")), Convert.ToSingle(Backend.Field(value, "y")));
        }

        public void Poll(out Vector2 move, out Vector2 turn) { move = Read(1); turn = Read(2); }
        public void Dispose() { }
    }
}
