using System;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WalkNWash.VRCompanion
{
    // Included ONLY with -p:SmokeTest=true. Requires --vr-companion-smoke-test.
    // Executes inside the actual Unity player, then quits. No headset needed.
    internal static class RuntimeSmoke
    {
        internal static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "--vr-companion-smoke-test") >= 0;

        internal static void Run(ActionButtons startupButtons, bool coreEnabled, ManualLogSource log)
        {
            int count = 0;
            Action<bool, string> check = (condition, name) =>
            {
                if (!condition) throw new Exception(name);
                count++;
            };
            try
            {
                check(coreEnabled, "core hooks and production button callback survive startup");
                check(startupButtons != null, "deferred device creation succeeds");
                var buttons = startupButtons;
                using (var actions = new InputSystem_Actions())
                {
                    int press = 0, release = 0, jumps = 0, secondary = 0;
                    actions.Player.Plap.performed += _ => press++;
                    actions.Player.Plap.canceled += _ => release++;
                    actions.Player.Jump.performed += _ => jumps++;
                    actions.Player.Attack.performed += _ => secondary++;
                    actions.Player.Enable();
                    // Explicit updates are confined to this standalone runtime test.
                    Action<float, float, bool, bool> step = (left, right, jump, enabled) =>
                    {
                        buttons.Update(actions, left, right, jump, enabled);
                        InputSystem.Update();
                    };
                    step(0, 0, false, true);
                    step(1, 0, false, true);
                    check(press == 1 && actions.Player.Plap.IsPressed() && !actions.Player.Attack.IsPressed(), "left trigger performs left-hand action");
                    step(1, 0, false, true);
                    check(press == 1, "hold does not repeat performed");
                    step(0, 0, false, true);
                    check(release == 1 && !actions.Player.Plap.IsPressed(), "trigger canceled on release");
                    step(0, 1, true, true);
                    check(jumps == 1 && secondary == 1, "right A and right trigger reach game actions");
                    step(0, 1, true, false);
                    check(!actions.Player.Jump.IsPressed() && !actions.Player.Attack.IsPressed(), "focus loss releases buttons");
                    step(0, 1, true, true);
                    check(jumps == 1 && secondary == 1, "held buttons do not activate after focus return");
                    step(0, 0, false, true);
                    step(0, 0, true, true);
                    check(jumps == 2, "A can jump again after release");
                    buttons.Release();
                    check(!actions.Player.Jump.IsPressed(), "synchronous release clears jump");
                    actions.Player.Disable();
                }
                RuntimeDialogueSmoke.Run(check);
                RuntimeHudSmoke.Run(check);
                RuntimeLocomotionSmoke.Run(check);
                log.LogInfo("RUNTIME SMOKE PASS: " + count + " Unity input/dialogue/HUD/locomotion checks.");
                Application.Quit(0);
            }
            catch (Exception e)
            {
                log.LogError("RUNTIME SMOKE FAIL: " + e);
                Application.Quit(1);
            }
        }
    }
}
