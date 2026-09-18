using System;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace WalkNWash.VRCompanion
{
    internal static class EscapeMenu
    {
        // Follow the current menu's actual Escape intent, including pause/back.
        // Its serialized actions are separate from MenuManager.actions.
        internal static void Press()
        {
            var manager = AccessTools.Field(typeof(MenuManager), "_instance").GetValue(null);
            if (manager == null) return;
            var menu = AccessTools.Field(typeof(MenuManager), "_currentMenu").GetValue(manager) as Menu;
            if (menu == null || !menu.isShown) return;
            var intents = AccessTools.Field(typeof(Menu), "intents").GetValue(menu);
            if (intents == null) return;
            var entries = AccessTools.Field(typeof(MenuIntents), "intents").GetValue(intents) as Array;
            if (entries == null) return;
            foreach (var entry in entries)
            {
                var type = entry.GetType();
                var reference = AccessTools.Field(type, "inputAction").GetValue(entry) as InputActionReference;
                if (reference?.action == null || !reference.action.enabled) continue;
                foreach (var binding in reference.action.bindings)
                {
                    if (!string.Equals(binding.effectivePath, "<Keyboard>/escape", StringComparison.OrdinalIgnoreCase)) continue;
                    MenuManager.TriggerEvent(new MenuEventUserIntent((string)AccessTools.Field(type, "intentName").GetValue(entry)));
                    return;
                }
            }
        }
    }
}
