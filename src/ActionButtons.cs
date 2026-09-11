using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace WalkNWash.VRCompanion
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CompanionButtonState : IInputStateTypeInfo
    {
        public FourCC format => new FourCC('W', 'V', 'R', 'B');
        [InputControl(name = "primary", layout = "Button", bit = 0)]
        [InputControl(name = "secondary", layout = "Button", bit = 1)]
        [InputControl(name = "jump", layout = "Button", bit = 2)]
        public uint buttons;
    }

    [InputControlLayout(stateType = typeof(CompanionButtonState), displayName = "Walk N Wash VR Actions")]
    public sealed class CompanionButtons : InputDevice
    {
        public CompanionButtons() { }
        public ButtonControl primary { get; private set; }
        public ButtonControl secondary { get; private set; }
        public ButtonControl jump { get; private set; }
        protected override void FinishSetup()
        {
            base.FinishSetup();
            primary = GetChildControl<ButtonControl>("primary");
            secondary = GetChildControl<ButtonControl>("secondary");
            jump = GetChildControl<ButtonControl>("jump");
        }
    }

    // Feed real InputActions so their performed/canceled callbacks and held state
    // remain consistent. This does not synthesize mouse clicks into the desktop.
    internal sealed class ActionButtons : IDisposable
    {
        private readonly CompanionButtons device;
        private InputSystem_Actions bound;
        private TriggerButton primary, secondary, jump;
        private const string PrimaryPath = "<CompanionButtons>/primary";
        private const string SecondaryPath = "<CompanionButtons>/secondary";
        private const string JumpPath = "<CompanionButtons>/jump";

        internal ActionButtons()
        {
            InputSystem.RegisterLayout<CompanionButtons>();
            device = InputSystem.AddDevice<CompanionButtons>();
        }

        private static void Binding(InputAction action, string path, string groups, bool add)
        {
            bool enabled = action.enabled;
            if (enabled) action.Disable();
            try
            {
                if (add)
                {
                    if (!action.bindings.Any(b => b.path == path))
                        action.AddBinding(new InputBinding { path = path, groups = groups, name = "VR button" });
                }
                else
                {
                    for (int i = action.bindings.Count - 1; i >= 0; i--)
                        if (action.bindings[i].path == path) action.ChangeBinding(i).Erase();
                }
            }
            finally { if (enabled) action.Enable(); }
        }

        internal void Update(InputSystem_Actions actions, float left, float right, bool jumpPressed, bool enabled)
        {
            if (!ReferenceEquals(bound, actions))
            {
                Unbind();
                bound = actions;
                string groups = string.Join(";", actions.controlSchemes.Select(s => s.bindingGroup));
                Binding(actions.Player.Plap, PrimaryPath, groups, true);
                Binding(actions.Player.Attack, SecondaryPath, groups, true);
                Binding(actions.Player.Jump, JumpPath, groups, true);
            }
            // Preserve explicit device filters, adding only this companion's device.
            var allowed = actions.devices;
            if (allowed.HasValue && !allowed.Value.Contains(device))
                actions.devices = allowed.Value.Concat(new InputDevice[] { device }).ToArray();
            uint state = primary.Update(right, enabled && actions.Player.Plap.enabled) ? 1u : 0u;
            if (secondary.Update(left, enabled && actions.Player.Attack.enabled)) state |= 2;
            if (jump.Update(jumpPressed ? 1 : 0, enabled && actions.Player.Jump.enabled)) state |= 4;
            InputSystem.QueueStateEvent(device, new CompanionButtonState { buttons = state });
        }

        internal void Release()
        {
            primary.Update(0, false);
            secondary.Update(0, false);
            jump.Update(0, false);
            if (device.added) InputState.Change(device, new CompanionButtonState());
        }

        private void Unbind()
        {
            if (bound == null) return;
            Release();
            Binding(bound.Player.Plap, PrimaryPath, null, false);
            Binding(bound.Player.Attack, SecondaryPath, null, false);
            Binding(bound.Player.Jump, JumpPath, null, false);
            var allowed = bound.devices;
            if (allowed.HasValue) bound.devices = allowed.Value.Where(d => d != device).ToArray();
            bound = null;
        }
        public void Dispose()
        {
            try { Unbind(); }
            finally { if (device.added) InputSystem.RemoveDevice(device); }
        }
    }
}
