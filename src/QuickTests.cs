using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // Manual visual fixtures. Story jumps use native loading, never scene-name guesses.
    internal sealed class QuickTests : IDisposable
    {
        private readonly VrDialoguePanel panel = new VrDialoguePanel();
        private readonly List<DialogueChoice> choices = new List<DialogueChoice>();
        private GameObject sample;
        private Image paper;
        private bool pressed, armed, sampleDialogue;
        private int alphaStep;
        private string message = "Visual checks or native scene shortcuts. F5 closes.\nStory jumps disable saving until game restart.";
        internal static bool Active { get; private set; }
        internal static bool Mask { get; private set; }
        internal static bool Sample { get; private set; }
        internal static bool NoSaving { get; private set; }
        internal static bool AllowSave() => !NoSaving;
        internal QuickTests() => DebugAccess.Initialize(typeof(QuickTests).Assembly.Location);
        internal void Tick(Backend backend, bool usable)
        {
            if (!DebugAccess.Enabled) { Close(); return; }
            if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
            {
                if (Active) Close();
                else if (usable) { Active = true; armed = false; panel.Recenter(); }
            }
            if (!Active) return;
            if (!usable || !backend.Rig || !backend.HeadPose(out var head, out var rotation)) { Close(); return; }
            if (choices.Count == 0)
            {
                foreach (string text in new[] { "Menu alpha: cycle 0 / 50 / 100%", "World blackout: toggle", "Dialogue / choices sample", "Ryan hotel scene (saving OFF until restart)", "Ryan + Conrad scene (saving OFF)", "Alexander scene (saving OFF)", "Conrad scene (saving OFF)", "Finale (saving OFF)", "Native unstick transition", "Close tests" })
                    choices.Add(new DialogueChoice { Token = text, Text = text, Enabled = true });
            }
            panel.Present(null, NoSaving ? "TEST SESSION — SAVING DISABLED" : "Quick VR tests",
                message, int.MaxValue, choices, false);
            panel.Place(backend.Rig.transform, head, rotation, 1.1f, 1.2f);
            backend.Poll();
            bool tracked = backend.Aim(out var position, out var aim);
            int hit = panel.Point(new Ray(backend.Rig.transform.TransformPoint(position), backend.Rig.transform.rotation * aim * Vector3.forward), tracked);
            bool down = tracked && backend.RightTrigger > .65f;
            if (!down) armed = true;
            if (armed && pressed && !down && tracked)
            {
                if (!panel.ChangePage(hit) && hit >= 0) Select(hit);
            }
            pressed = down;
        }
        private void Select(int index)
        {
            switch (index)
            {
                case 0:
                    CreateSample(); Sample = true; sample.SetActive(true);
                    float alpha = (alphaStep++ % 3) * .5f;
                    paper.color = new Color(.08f, .14f, .22f, alpha);
                    message = "Native capture background alpha: " + alpha + ".\nText behind this panel must read LEFT → RIGHT; no white squares.\nFlat menu retains its normal minimum opacity.";
                    break;
                case 1: Mask = !Mask; message = "World blackout: " + Mask + ". UI and laser remain visible."; break;
                case 2:
                    sampleDialogue = !sampleDialogue;
                    message = sampleDialogue ? "Fresh conversation A: readable dialogue, with selectable answers below.\nF5 close/reopen must not leave this panel in the scene." : "Fresh conversation B: previous sample replaced.\nThese are visual fixtures; use actual scenes to test Yarn lifecycle.";
                    break;
                case 8:
                    if (!UnityEngine.Object.FindFirstObjectByType<WalkNWashSceneState>()) { message = "Load a save first for unstick."; break; }
                    Close(); WalkNWashSceneState.UnstickPlayer(); break;
                case 9: Close(); break;
                default:
                    if (index < 3 || index > 7) break;
                    if (!(VrScreen.CurrentMenu is MenuUnpaused)) { message = "Load a save and resume gameplay before a story jump."; break; }
                    NoSaving = true;
                    string intent = new[] { "RyanSexScene", "ConradRyanSexScene", "AlexanderSexScene", "ConradSexScene", "FinishGame" }[index - 3];
                    Close();
                    MenuManager.TriggerEvent(new MenuEventUserIntent(intent));
                    Debug.Log("VR quick test: " + intent + "; save writes disabled until restart.");
                    break;
            }
        }
        private void CreateSample()
        {
            if (sample) return;
            sample = new GameObject("VR test native canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(sample);
            sample.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            sample.GetComponent<Canvas>().sortingOrder = 30000;
            var scaler = sample.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var bg = new GameObject("Alpha sample", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(sample.transform, false);
            var rect = bg.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            paper = bg.GetComponent<Image>(); paper.raycastTarget = false;
            var label = new GameObject("Native glyph sample", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.transform.SetParent(sample.transform, false);
            rect = label.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0, .75f); rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            var text = label.GetComponent<TextMeshProUGUI>(); text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = 36; text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.raycastTarget = false;
            text.text = "LEFT → Native UI text 0123456789 → RIGHT\nAa Bb Cc — readable letters, not squares";
        }
        internal void BeginEye(Backend backend)
        {
            if (!Active) return;
            var left = Backend.Field(backend.Setup, "_leftVrCamera") as Camera;
            var right = Backend.Field(backend.Setup, "_rightVrCamera") as Camera;
            int mask = left && right ? left.cullingMask & right.cullingMask : 0;
            if (mask == 0) return;
            int layer = 0; while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
            panel.BeginEye(layer);
        }
        internal void EndEye() => panel.EndEye();
        internal void Close()
        {
            Active = Mask = Sample = false; pressed = armed = false;
            if (sample) sample.SetActive(false);
            panel.Hide();
        }
        public void Dispose() { Close(); panel.Dispose(); UnityEngine.Object.Destroy(sample); }
    }
}
