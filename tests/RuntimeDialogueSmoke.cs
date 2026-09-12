using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Yarn.Unity;

namespace WalkNWash.VRCompanion
{
    internal static class RuntimeDialogueSmoke
    {
        internal static void Run(Action<bool, string> check)
        {
            var rig = new GameObject("Dialogue test rig");
            try
            {
                using (var panel = new VrDialoguePanel())
                {
                    var source = UnityEngine.Object.FindFirstObjectByType<TMP_Text>();
                    var choices = new List<DialogueChoice>();
                    for (int i = 0; i < 6; i++) choices.Add(new DialogueChoice { Token = new object(), Text = "Answer " + (i + 1), Enabled = i != 1 });
                    panel.Present(source, "Guide", "Which path would you like to take?", int.MaxValue, choices, false);
                    rig.transform.SetPositionAndRotation(new Vector3(3, 2, -4), Quaternion.Euler(0, 55, 0));
                    rig.transform.localScale = Vector3.one * 2;
                    panel.Place(rig.transform, Vector3.zero, Quaternion.identity, 1.2f, 1.6f);
                    var root = (GameObject)Backend.Field(panel, "root");
                    Func<float, float, Ray> ray = (x, y) => new Ray(rig.transform.position,
                        root.transform.TransformPoint(new Vector3(x, y, 0)) - rig.transform.position);
                    check(panel.Point(ray(0, 30), true) == 0, "pointing selects first answer under rig transform/scale");
                    check(panel.Point(ray(0, -60), true) == VrDialoguePanel.None, "unavailable answer cannot be selected");
                    check(panel.Point(ray(700, 30), true) == VrDialoguePanel.None, "missed panel cannot click");
                    check(panel.Point(ray(0, 30), false) == VrDialoguePanel.None, "untracked controller cannot click");
                    check(panel.Point(new Ray(root.transform.TransformPoint(new Vector3(0, 30, 100)), -root.transform.forward), true)
                        == VrDialoguePanel.None, "back-facing ray cannot click");
                    check(panel.Point(ray(380, -345), true) == VrDialoguePanel.Next, "page-next target reachable");
                    check(panel.ChangePage(VrDialoguePanel.Next), "next page opens");
                    check(panel.Point(ray(0, 30), true) == 4, "second page retains original answer index");
                    check(panel.Point(ray(-380, -345), true) == VrDialoguePanel.Previous, "page-previous target reachable");
                    panel.ChangePage(VrDialoguePanel.Previous);
                    Vector3 anchored = root.transform.position;
                    panel.Place(rig.transform, Vector3.right, Quaternion.Euler(0, 40, 0), 1.2f, 1.6f);
                    check(root.transform.position == anchored, "panel stays anchored while head moves");
                    panel.Recenter();
                    panel.Place(rig.transform, Vector3.right, Quaternion.identity, 1.2f, 1.6f);
                    check(root.transform.position != anchored, "F10 reanchors panel");

                    rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    rig.transform.localScale = Vector3.one;
                    panel.Recenter();
                    panel.Place(rig.transform, Vector3.zero, Quaternion.identity, 1.2f, 1.6f);
                    choices[0].Text = "Let's take the forest path.";
                    choices[1].Text = "Enter the locked tower. (Unavailable)";
                    choices[2].Text = "Tell me more about the village before I decide.";
                    choices[3].Text = "I would like to stay here for a while.";
                    panel.Present(source, "Guide", "There are several ways forward. Which path would you like to take?", int.MaxValue, choices, false);
                    panel.Point(ray(0, 30), true);
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "--vr-companion-ui-capture") >= 0)
                        Capture(panel, "dialogue-options.png", check);
                    panel.EndEye();
                    check(!root.GetComponent<Canvas>().enabled, "dialogue hidden outside VR eye passes");
                    panel.BeginEye(31);
                    check(root.GetComponentInChildren<LineRenderer>().enabled, "pointer restored for second eye");
                    panel.EndEye();
                    panel.Present(source, "Guide", "Point at this panel and press the right index trigger to reveal the text, then press again to continue.", int.MaxValue, new List<DialogueChoice>(), true);
                    check(panel.Point(ray(0, 0), true) == VrDialoguePanel.Continue, "line panel can advance");
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "--vr-companion-ui-capture") >= 0)
                        Capture(panel, "dialogue-line.png", check);
                }
            }
            finally { UnityEngine.Object.Destroy(rig); }

            var test = new GameObject("Yarn callback tests");
            test.SetActive(false); // No narrative runner lifecycle or project loading.
            try
            {
                var runner = test.AddComponent<DialogueRunner>();
                var advancer = test.AddComponent<LineAdvancer>();
                AccessTools.Field(typeof(LineAdvancer), "runner").SetValue(advancer, runner);
                var status = AccessTools.Field(typeof(LineAdvancer), "status");
                var contentFrame = AccessTools.Field(typeof(LineAdvancer), "frameContentReceived");
                var advance = AccessTools.Method(typeof(LineAdvancer), "RequestLineHurryUpInternal");
                using (var next = new CancellationTokenSource())
                using (var hurry = new CancellationTokenSource())
                {
                    AccessTools.Field(typeof(DialogueRunner), "currentLineCancellationSource").SetValue(runner, next);
                    AccessTools.Field(typeof(DialogueRunner), "currentLineHurryUpSource").SetValue(runner, hurry);
                    status.SetValue(advancer, Enum.ToObject(status.FieldType, 1));
                    contentFrame.SetValue(advancer, Time.frameCount - 1);
                    advance.Invoke(advancer, null);
                    check(hurry.IsCancellationRequested && !next.IsCancellationRequested, "first click reveals typewriter without skipping line");
                    status.SetValue(advancer, Enum.ToObject(status.FieldType, 2));
                    contentFrame.SetValue(advancer, Time.frameCount);
                    advance.Invoke(advancer, null);
                    check(!next.IsCancellationRequested, "newly arrived line cannot be skipped in same frame");
                    contentFrame.SetValue(advancer, Time.frameCount - 1);
                    advance.Invoke(advancer, null);
                    check(next.IsCancellationRequested, "waiting line advances through Yarn handler");
                    AccessTools.Field(typeof(DialogueRunner), "currentLineCancellationSource").SetValue(runner, null);
                    AccessTools.Field(typeof(DialogueRunner), "currentLineHurryUpSource").SetValue(runner, null);
                }
                var option = test.AddComponent<OptionItem>();
                var selected = new DialogueOption { DialogueOptionID = 17, IsAvailable = true };
                AccessTools.Field(typeof(OptionItem), "_option").SetValue(option, selected);
                option.OnOptionSelected = new YarnTaskCompletionSource<DialogueOption>();
                option.InvokeOptionSelected();
                check(option.OnOptionSelected.Task.IsCompletedSuccessfully()
                    && option.OnOptionSelected.Task.GetAwaiter().GetResult() == selected, "answer callback resolves exact Yarn option");
                option.OnOptionSelected = new YarnTaskCompletionSource<DialogueOption>();
                option.InvokeOptionSelected();
                check(!option.OnOptionSelected.Task.IsCompleted(), "same option cannot be submitted twice");
            }
            finally { UnityEngine.Object.Destroy(test); }
        }

        private static void Capture(VrDialoguePanel panel, string name, Action<bool, string> check)
        {
            var go = new GameObject("Dialogue capture camera", typeof(Camera));
            var camera = go.GetComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.16f, .18f, .22f);
            camera.cullingMask = 1 << 31;
            camera.fieldOfView = 60;
            camera.nearClipPlane = .01f;
            var target = new RenderTexture(1200, 1000, 24);
            var texture = new Texture2D(1200, 1000, TextureFormat.RGB24, false);
            RenderTexture original = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                panel.BeginEye(31);
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1200, 1000), 0, 0);
                texture.Apply();
                int bright = 0;
                foreach (var pixel in texture.GetPixels32()) if (pixel.r > 180 && pixel.g > 180 && pixel.b > 180) bright++;
                check(bright > 100, "rendered dialogue contains visible text: " + name);
                string directory = Path.Combine(BepInEx.Paths.GameRootPath, "walknwash-vr-companion", "dist");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name), ImageConversion.EncodeToPNG(texture));
            }
            finally
            {
                panel.EndEye();
                RenderTexture.active = original;
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(texture);
                UnityEngine.Object.Destroy(go);
            }
        }
    }
}
