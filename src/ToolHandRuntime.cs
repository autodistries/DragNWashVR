using System;
using HarmonyLib;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal static class ToolHandRuntime
    {
        private static readonly System.Reflection.FieldInfo handRoles = AccessTools.Field(typeof(Plugin), "handRoles");
        private static readonly System.Reflection.FieldInfo hands = AccessTools.Field(typeof(Plugin), "hands");
        private static readonly System.Reflection.FieldInfo actionButtons = AccessTools.Field(typeof(Plugin), "actionButtons");
        private static readonly System.Reflection.FieldInfo gripEditor = AccessTools.Field(typeof(Plugin), "gripEditor");
        private static readonly System.Reflection.FieldInfo menuHold = AccessTools.Field(typeof(Plugin), "menuHold");
        private static readonly System.Reflection.FieldInfo failed = AccessTools.Field(typeof(Plugin), "failed");
        private static readonly System.Reflection.FieldInfo handDiagnostics = AccessTools.Field(typeof(Plugin), "handDiagnostics");
        private static readonly System.Reflection.FieldInfo rightHandDiagnostics = AccessTools.Field(typeof(Plugin), "rightHandDiagnostics");

        internal static void Apply(UserSettings source, ToolController value)
        {
            var host = Plugin.Current;
            if (!host || !ReferenceEquals(host.Preferences, source)) return;
            bool toolOnLeft = value == ToolController.Left;
            if (host.Roles.ToolOnLeft == toolOnLeft) return;

            // Never carry input, an F8 preview, or visual state owned by the old
            // semantic hand across the role boundary.
            try { (gripEditor.GetValue(host) as GripEditor)?.Finish(false); }
            catch (Exception e) { host.HandLog("Grip calibration cleanup during tool hand change failed: " + e.Message); }
            try { (actionButtons.GetValue(host) as ActionButtons)?.Release(); }
            catch (Exception e) { host.HandLog("Button cleanup during tool hand change failed: " + e.Message); }
            menuHold.SetValue(host, default(MenuHold));

            var oldHands = hands.GetValue(host) as VrHands;
            hands.SetValue(host, null);
            try { oldHands?.Dispose(); }
            catch (Exception e)
            {
                host.HandLog("Controller-hand cleanup during tool hand change failed; forcing a clean rebuild: " + e.Message);
                try { oldHands?.RestoreAll(); }
                catch (Exception restore) { host.HandLog("Controller-hand restore retry failed: " + restore.Message); }
                try { new Harmony(Plugin.Id + ".hands").UnpatchSelf(); }
                catch (Exception unpatch) { host.HandLog("Controller-hand unpatch retry failed: " + unpatch.Message); }
            }

            handRoles.SetValue(host, new HandRoles(toolOnLeft));
            (handDiagnostics.GetValue(host) as HandDiagnostics)?.Recenter();
            (rightHandDiagnostics.GetValue(host) as HandDiagnostics)?.Recenter();

            if (!(bool)failed.GetValue(host))
            {
                try { hands.SetValue(host, new VrHands(host, host.Config)); }
                catch (Exception e)
                {
                    host.HandLog("Controller hands unavailable after changing tool hand; other controls remain active. " + e);
                }
            }
            host.HandLog("Tool hand changed to " + host.Roles.ToolName + ".");
        }
    }
}
