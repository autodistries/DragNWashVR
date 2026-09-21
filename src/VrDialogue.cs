using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Yarn.Unity;

namespace WalkNWash.VRCompanion
{
    internal sealed class VrDialogue : IDisposable
    {
        private static readonly FieldInfo instance = AccessTools.Field(typeof(DialogCommands), "_instance");
        private static readonly MethodInfo advance = AccessTools.Method(typeof(LineAdvancer), "RequestLineHurryUpInternal");
        private readonly VrDialoguePanel panel = new VrDialoguePanel();
        private readonly List<DialogueChoice> choices = new List<DialogueChoice>();
        private readonly List<OptionItem> items = new List<OptionItem>();
        private DialogCommands source;
        private DialogueRunner runner;
        private LinePresenter line;
        private LineAdvancer advancer;
        private OptionsPresenter options;
        private CanvasGroup optionsGroup;
        private TriggerButton trigger;
        private bool held, wasRunning, presented;
        private int frame = -1;
        private OptionItem pendingOption;
        private object pendingToken;
        private LineAdvancer pendingLine;
        private int pendingLineFrame;

        private void RefreshSource()
        {
            var found = instance.GetValue(null) as DialogCommands;
            if (source == found) return;
            Reset();
            source = found;
            runner = null; line = null; advancer = null; options = null; optionsGroup = null;
            if (source == null) return;
            runner = Backend.Field(source, "dialogueRunner") as DialogueRunner;
            line = Backend.Field(source, "linePresenter") as LinePresenter;
            advancer = Backend.Field(source, "lineAdvancer") as LineAdvancer;
            options = runner?.DialoguePresenters.OfType<OptionsPresenter>().FirstOrDefault();
            if (options != null) optionsGroup = Backend.Field(options, "canvasGroup") as CanvasGroup;
        }

        internal bool IsRunning
        {
            get { RefreshSource(); return runner != null && runner.IsDialogueRunning; }
        }
        internal bool BlocksGameplay => IsRunning && advancer != null && advancer.isActiveAndEnabled;
        private bool OptionsVisible => options != null && options.isActiveAndEnabled && Visible(optionsGroup);
        private static bool Visible(CanvasGroup group)
            => group != null && group.gameObject.activeInHierarchy && group.alpha > .01f;

        // Called in normal Update, never inside either eye's rendering pass.
        internal void Dispatch(bool allowed)
        {
            var option = pendingOption;
            object token = pendingToken;
            var lineTarget = pendingLine;
            int received = pendingLineFrame;
            pendingOption = null; pendingToken = null; pendingLine = null;
            if (!allowed) { trigger = new TriggerButton(); held = false; return; }
            if (!IsRunning) { Reset(); return; }
            if (option != null)
            {
                if (OptionsVisible && optionsGroup.interactable && option.isActiveAndEnabled
                    && option.IsInteractable() && ReferenceEquals(option.Option, token))
                    option.InvokeOptionSelected();
            }
            else if (lineTarget != null && lineTarget == advancer && advancer.isActiveAndEnabled
                && !OptionsVisible && line != null && Visible(line.canvasGroup)
                && received == Convert.ToInt32(Backend.Field(advancer, "frameContentReceived")))
            {
                // Same entry point as the desktop hurry-up/advance input handler.
                // It respects typewriter state and refuses advancing a new line
                // in the frame in which that line first arrived.
                advance.Invoke(advancer, null);
            }
        }

        internal void BeginEye(Backend backend, bool visible, bool controls, float width, float distance, float pitch)
        {
            if (!visible || !IsRunning || backend.Rig == null)
            { Reset(); return; }
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                presented = false;
                if (!wasRunning) { trigger = new TriggerButton(); held = false; panel.Recenter(); }
                wasRunning = true;
                bool showOptions = OptionsVisible;
                bool showLine = line != null && line.isActiveAndEnabled && Visible(line.canvasGroup);
                if (!showOptions && !showLine)
                { trigger = new TriggerButton(); held = false; panel.EndEye(); return; }
                choices.Clear(); items.Clear();
                TMP_Text body = line?.lineText;
                string speaker = line?.characterNameText != null && line.characterNameText.gameObject.activeInHierarchy
                    ? line.characterNameText.text : "";
                int visibleCharacters = body != null ? body.maxVisibleCharacters : int.MaxValue;
                if (showOptions)
                {
                    // Options without a visible native prompt must not borrow a
                    // hidden line presenter's previous line or prefab placeholder.
                    body = null;
                    speaker = "";
                    var views = Backend.Field(options, "optionViews") as List<OptionItem>;
                    if (views != null)
                        foreach (var item in views)
                        {
                            if (item == null || !item.isActiveAndEnabled) continue;
                            var text = Backend.Field(item, "text") as TMP_Text;
                            items.Add(item);
                            choices.Add(new DialogueChoice { Token = item.Option, Source = text,
                                Text = text != null ? text.text : item.Option.Line.TextWithoutCharacterName.Text,
                                Enabled = optionsGroup.interactable && item.IsInteractable() });
                        }
                    var lastLine = Backend.Field(options, "lastLineText") as TMP_Text;
                    if (lastLine != null && lastLine.gameObject.activeInHierarchy) body = lastLine;
                    visibleCharacters = int.MaxValue;
                }
                if (!backend.HeadPose(out var head, out var rotation)) { Reset(); return; }
                panel.Present(body, speaker, body != null ? body.text : "", visibleCharacters, choices,
                    advancer != null && advancer.isActiveAndEnabled && !showOptions, line?.characterNameText);
                panel.Place(backend.Rig.transform, head, rotation, width, distance,
                    UserSettings.DialogueFollowsView != null && UserSettings.DialogueFollowsView.Value);
                presented = true;
                backend.Poll();
                Vector3 hand = Vector3.zero;
                Quaternion aim = Quaternion.identity;
                bool tracked = controls && advancer != null && advancer.isActiveAndEnabled
                    && backend.Focused && backend.Aim(out hand, out aim);
                var rig = backend.Rig.transform;
                var ray = new Ray(rig.TransformPoint(hand), rig.rotation * aim * Quaternion.Euler(pitch, 0, 0) * Vector3.forward);
                int target = panel.Point(ray, tracked);
                bool pressed = trigger.Update(backend.RightTrigger, tracked);
                if (pressed && !held)
                {
                    if (!panel.ChangePage(target))
                    {
                        if (target >= 0 && target < items.Count)
                        { pendingOption = items[target]; pendingToken = choices[target].Token; }
                        else if (target == VrDialoguePanel.Continue)
                        { pendingLine = advancer; pendingLineFrame = Convert.ToInt32(Backend.Field(advancer, "frameContentReceived")); }
                    }
                }
                held = pressed;
            }
            // Both eyes must see the same panel state and pointer, with one click
            // decision per frame. No UI is drawn into the normal desktop camera.
            if (!presented) return;
            var left = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            var right = Backend.Field(backend.Setup, "_rightVrCamera") as Camera;
            int mask = left != null && right != null ? left.cullingMask & right.cullingMask : 0;
            if (mask == 0) { panel.EndEye(); return; }
            int layer = 0;
            while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
            panel.BeginEye(layer);
        }

        internal void Recenter() { panel.Recenter(); }
        internal void EndEye() { panel.EndEye(); }
        internal void Reset()
        {
            panel.Hide();
            trigger = new TriggerButton();
            held = wasRunning = presented = false;
            pendingOption = null; pendingToken = null; pendingLine = null;
            frame = -1;
        }
        public void Dispose() { Reset(); panel.Dispose(); }
    }
}
