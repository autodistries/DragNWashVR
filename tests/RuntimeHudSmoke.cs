using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    internal static class RuntimeHudSmoke
    {
        internal static void Run(Action<bool, string> check)
        {
            var rig = new GameObject("HUD test rig");
            var group = new GameObject("HUD source test", typeof(RectTransform));
            group.SetActive(false);
            var barObject = new GameObject("Test cleanliness", typeof(RectTransform));
            barObject.SetActive(false);
            try
            {
                var bars = group.AddComponent<UiProgressBars>();
                var bar = barObject.AddComponent<UiProgressBar>();
                var surface = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                surface.transform.SetParent(barObject.transform, false);
                AccessTools.Field(typeof(UiProgressBar), "panel").SetValue(bar, surface);
                AccessTools.Field(typeof(UiProgressBars), "cleanBar").SetValue(bars, bar);
                barObject.SetActive(true);
                AccessTools.Field(typeof(UiProgressBar), "_progress").SetValue(bar, .42f);
                var readings = new List<HudReading>();
                HudSource.AppendBars(bars, readings);
                check(readings.Count == 1 && readings[0].Label == "Cleanliness" && Mathf.Abs(readings[0].Value - .42f) < .001f,
                    "HUD reads actual desktop bar value");
                surface.SetActive(false);
                readings.Clear();
                HudSource.AppendBars(bars, readings);
                check(readings.Count == 0, "HUD omits desktop-hidden bars");
                surface.SetActive(true);
                bar.enabled = false;
                HudSource.AppendBars(bars, readings);
                check(readings.Count == 0, "HUD omits disabled bar components");

                using (var hud = new VrHud())
                {
                    readings.Add(new HudReading { Label = "Cleanliness", Value = .72f, Color = new Color(.3f, .85f, .65f) });
                    readings.Add(new HudReading { Label = "Soap coverage", Value = .36f, Color = Color.cyan });
                    readings.Add(new HudReading { Label = "Sponge supply", Value = .81f, Color = new Color(.3f, .8f, 1) });
                    hud.Present(readings);
                    hud.Place(rig.transform, Vector3.zero, Quaternion.identity, 1, -.5f, .4f);
                    var root = (GameObject)Backend.Field(hud, "root");
                    Vector3 relative = root.transform.localPosition;
                    Quaternion rotation = Quaternion.Euler(20, 65, -10);
                    Vector3 head = new Vector3(.2f, 1.6f, -.3f);
                    rig.transform.SetPositionAndRotation(new Vector3(4, 2, -5), Quaternion.Euler(0, 30, 0));
                    rig.transform.localScale = Vector3.one * 2;
                    hud.Place(rig.transform, head, rotation, 1, -.5f, .4f);
                    check(Vector3.Distance(Quaternion.Inverse(rotation) * (root.transform.localPosition - head), relative) < .0001f,
                        "HUD retains position relative to head after movement/rotation");
                    check(Quaternion.Angle(root.transform.localRotation, rotation) < .001f, "HUD faces head through pitch/yaw/roll");
                    var fills = (List<Image>)Backend.Field(hud, "fills");
                    check(Mathf.Abs(fills[0].rectTransform.sizeDelta.x - 330 * .72f) < .001f, "HUD fill reflects percentage");
                    rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    rig.transform.localScale = Vector3.one;
                    hud.Place(rig.transform, Vector3.zero, Quaternion.identity, 1, -.5f, .4f);
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "--vr-companion-ui-capture") >= 0)
                        RuntimeDialogueSmoke.Capture(() => hud.Show(31), hud.EndEye, "progress-hud.png", check);
                    hud.EndEye();
                    check(!root.GetComponent<Canvas>().enabled, "HUD hidden outside eye render passes");
                    readings.RemoveAt(2);
                    hud.Present(readings);
                    var rows = (List<GameObject>)Backend.Field(hud, "rows");
                    check(!rows[2].activeSelf, "removed sponge does not leave stale HUD row");
                    readings.Clear();
                    hud.Present(readings);
                    hud.Show(31);
                    check(!root.GetComponent<Canvas>().enabled, "empty HUD stays hidden");
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(rig);
                UnityEngine.Object.Destroy(group);
                UnityEngine.Object.Destroy(barObject);
            }
        }
    }
}
