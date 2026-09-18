using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WalkNWash.VRCompanion
{
    // Native Interacter subscribes in Awake but has no matching teardown.
    // Keep the exact action subscribed to, even if MenuManager replaces its actions.
    internal sealed class InteracterLifetime : MonoBehaviour
    {
        private Interacter owner;
        private InputAction action;
        private Action<InputAction.CallbackContext> started, canceled;
        private bool subscribed;

        internal static void Attach(Interacter owner)
        {
            var action = MenuManager.actions.Player.Plap;
            var started = (Action<InputAction.CallbackContext>)Delegate.CreateDelegate(
                typeof(Action<InputAction.CallbackContext>), owner, AccessTools.Method(typeof(Interacter), "OnAttackStarted"));
            var canceled = (Action<InputAction.CallbackContext>)Delegate.CreateDelegate(
                typeof(Action<InputAction.CallbackContext>), owner, AccessTools.Method(typeof(Interacter), "OnAttackCanceled"));
            action.performed -= started;
            action.canceled -= canceled;
            var lifetime = owner.GetComponent<InteracterLifetime>() ?? owner.gameObject.AddComponent<InteracterLifetime>();
            lifetime.Unsubscribe();
            lifetime.owner = owner; lifetime.action = action;
            lifetime.started = started; lifetime.canceled = canceled;
            if (lifetime.isActiveAndEnabled) lifetime.Subscribe();
        }

        private void Subscribe()
        {
            if (subscribed || action == null || !owner) return;
            action.performed += Started; action.canceled += Canceled; subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            action.performed -= Started; action.canceled -= Canceled; subscribed = false;
        }
        private void Started(InputAction.CallbackContext context)
        {
            if (owner && owner.isActiveAndEnabled) started(context);
        }
        private void Canceled(InputAction.CallbackContext context)
        {
            if (owner && owner.isActiveAndEnabled) canceled(context);
        }
        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();
        private void Update()
        {
            // Also support destroying only the Interacter component.
            if (!owner) { Unsubscribe(); Destroy(this); }
        }
    }
}
