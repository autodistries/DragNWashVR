using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal static class RuntimeHandSmoke
    {
        private sealed class Input : IControllerInput
        {
            internal Vector3 Left = new Vector3(-.25f, 1.8f, 0), Right = new Vector3(.25f, 1.8f, 0);
            internal bool LeftValid = true, RightValid = true;
            public bool Hand(int hand, bool aim, ulong space, long time, Array poses, out Vector3 position, out Quaternion rotation)
            { position = hand == 1 ? Left : Right; rotation = Quaternion.identity; return hand == 1 ? LeftValid : RightValid; }
            public float Squeeze(int hand) => .6f;
            public void Pulse(int hand, float strength) { }
            public void Poll(out Vector2 move, out Vector2 turn, out float left, out float right, out bool jump, out bool hud, out bool crouch)
            { move = turn = Vector2.zero; left = right = 0; jump = hud = crouch = false; }
            public void Dispose() { }
        }
        private static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
        private static void Near(Action<bool, string> check, Vector3 a, Vector3 b, string name) => check(Vector3.Distance(a, b) < .002f, name);

        // Called inside the production-player fixture, after ordinary locomotion checks.
        internal static void Run(Plugin plugin, Backend backend, LookController look, Action<bool, string> check)
        {
            var hands = (VrHands)Backend.Field(plugin, "hands");
            check(hands != null && !(bool)Backend.Field(hands, "failed"), "hand Harmony hooks installed successfully");
            object oldInput = Backend.Field(backend, "input");
            Vector3 baseline = (Vector3)Backend.Field(plugin, "baseline");
            Matrix4x4 oldMatrix = (Matrix4x4)Backend.Field(look, "lastViewMatrix");
            var physical = (ConfigEntry<bool>)Backend.Field(plugin, "physicalCrouch");
            bool oldPhysical = physical.Value;
            var input = new Input();
            var root = new GameObject("Hand smoke objects"); root.transform.position = new Vector3(1000, 1000, 1000);
            var handObject = new GameObject("Hand smoke model"); handObject.SetActive(false);
            handObject.transform.SetParent(root.transform, false);
            GameObject databaseObject = null;
            var databaseField = AccessTools.Field(typeof(PhysicsMaterialExtensionDatabase), "instance");
            var oldDatabase = databaseField.GetValue(null);
            var material = ScriptableObject.CreateInstance<PhysicsMaterialExtension>();
            var oldRub = PlapperHand.RubEvent;
            var oldPlap = PlapperHand.PlapEvent;
            try
            {
                Set(backend, "input", input); Set(plugin, "baseline", new Vector3(0, 2, 0)); physical.Value = false;
                Set(look, "lastViewMatrix", Matrix4x4.TRS(root.transform.position, Quaternion.identity, Vector3.one));
                check(plugin.TryHandFrame(1, out var left) && plugin.TryHandFrame(2, out _), "both controller frames resolve independently");
                plugin.TryHandFrame(2, out var right);
                Near(check, right.Position - left.Position, new Vector3(.5f, 0, 0), "controller separation survives body mapping");
                Vector3 rightBefore = right.Position;
                input.Left += Vector3.right * .1f;
                plugin.TryHandFrame(1, out var translated); plugin.TryHandFrame(2, out right);
                Near(check, translated.Position - left.Position, Vector3.right * .1f, "left controller translation moves left hand");
                Near(check, right.Position, rightBefore, "left controller cannot move right hand");
                Set(plugin, "headRotation", Quaternion.Euler(0, 65, 0));
                plugin.TryHandFrame(1, out var afterHead);
                Near(check, afterHead.Position, translated.Position, "head rotation alone cannot steer hand position");
                Near(check, afterHead.Direction, translated.Direction, "head rotation alone cannot steer hand ray");
                input.LeftValid = false;
                check(!plugin.TryHandFrame(1, out _) && plugin.TryHandFrame(2, out _), "one lost controller does not invalidate the other");
                input.LeftValid = true; plugin.TryHandFrame(1, out left);

                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform, true);
                wall.transform.position = left.Position + Vector3.forward * .65f;
                wall.transform.localScale = new Vector3(2, 2, .1f);
                Physics.SyncTransforms();
                var hit = HandContact.Find(left, plugin.HandBodyEye, left.Position, false, 1.5f, .045f);
                check(hit.Collider == wall.GetComponent<Collider>(), "controller ray selects actual surface");
                Near(check, hit.Point, left.Position + Vector3.forward * .6f, "contact point matches controller ray");
                Quaternion wrist = Quaternion.Euler(0, 0, 37);
                Quaternion surface = HandContact.SurfaceRotation(Vector3.back, wrist);
                Near(check, surface * Vector3.forward, Vector3.forward, "surface alignment preserves contact normal");
                Near(check, surface * Vector3.up, wrist * Vector3.up, "wrist twist survives surface alignment");
                check(Backend.Finite(HandContact.SurfaceRotation(Vector3.down, Quaternion.identity)), "vertical contact has stable wrist fallback");
                var blocked = left; blocked.Position += Vector3.forward;
                check(HandContact.Find(blocked, plugin.HandBodyEye, blocked.Position, false, 1.5f, .045f).Collider == null,
                    "controller beyond wall cannot interact through it");

                var model = handObject.AddComponent<PlapperHand>();
                var parent = new GameObject("Authored hand parent").transform; parent.SetParent(handObject.transform, false);
                parent.localPosition = new Vector3(.6f, -.2f, .1f); parent.localRotation = Quaternion.Euler(0, 25, 0);
                var visual = new GameObject("plapper_L").transform; visual.SetParent(parent, false);
                var finger = new GameObject("index_01").transform; finger.SetParent(visual, false);
                Set(model, "hand", visual); Set(model, "plapNormalOffset", AnimationCurve.Constant(0, 1, 0));
                Set(model, "spongeAudioSource", root.AddComponent<AudioSource>()); Set(model, "hitColliders", new Collider[32]);
                Set(model, "wetSpongeMat", material); Set(model, "drySpongeMat", material);
                Set(material, "impactInfos", new List<PhysicsMaterialExtension.ImpactInfo>());
                databaseObject = new GameObject("Hand smoke material database"); databaseObject.SetActive(false);
                var database = databaseObject.AddComponent<PhysicsMaterialExtensionDatabase>();
                Set(database, "extensions", new List<PhysicsMaterialExtension>()); Set(database, "defaultExtension", material);
                databaseField.SetValue(null, database);
                int rubs = 0, slaps = 0; Vector3 rubPoint = Vector3.zero, rubNormal = Vector3.zero;
                PlapperHand.RubEvent = (collider, point, normal) => { rubs++; rubPoint = point; rubNormal = normal; };
                PlapperHand.PlapEvent = (collider, point, normal) => slaps++;
                object state = AccessTools.Method(typeof(VrHands), "State").Invoke(hands, new object[] { model });
                Action<bool> tick = active =>
                {
                    Set(state, "LastFrame", -1);
                    if (active) model.UseContinuous(); else model.UpdateNotInUse();
                };
                tick(false);
                Near(check, visual.position, left.Position, "native idle hook places hand at controller");
                check(rubs == 0 && slaps == 0, "idle hand touching view ray cannot produce contact effects");
                Set(state, "Blend", 1f); Set(state, "Using", true); Set(model, "splatted", true);
                tick(true);
                check(rubs == 1 && slaps == 0, "preserved native contact tail emits one rub callback");
                Near(check, visual.position, hit.Point, "visible hand reaches controller-selected contact");
                Near(check, rubPoint, visual.position, "native event position agrees with moved hand despite rotated parent");
                Near(check, rubNormal, hit.Normal, "native event normal agrees with contact surface");
                model.UseContinuous();
                check(rubs == 1, "second update in same frame cannot duplicate effects");
                tick(false);
                check(rubs == 1 && Backend.Field(model, "hitCollider") == null, "release clears stale target before idle effect code");
                input.LeftValid = false; tick(true);
                check(rubs == 1 && Backend.Field(model, "hitCollider") == null, "tracking loss cannot keep rubbing");
                input.LeftValid = true;
                using (var mirror = HandVisual.Mirror(visual))
                {
                    var clone = (Transform)Backend.Field(mirror, "root");
                    check(clone.GetComponentsInChildren<Collider>(true).Length == 0 && clone.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
                        "mirrored hand cannot clone gameplay scripts or colliders");
                    mirror.Place(right, true); Near(check, clone.position, right.Position, "right idle hand follows right controller");
                }
                using (var fingers = new HandVisual(visual))
                {
                    Quaternion rest = finger.localRotation;
                    fingers.Pose(0, .5f, false, 60);
                    check(Quaternion.Angle(rest, finger.localRotation) > 29, "idle index finger curls from trigger");
                    fingers.Restore(); Near(check, finger.localRotation * Vector3.up, rest * Vector3.up, "finger pose restores before native animation");
                }
                check(!(bool)Backend.Field(hands, "failed"), "hand integration survives real contact callbacks");
            }
            finally
            {
                PlapperHand.RubEvent = oldRub; PlapperHand.PlapEvent = oldPlap;
                hands.RestoreAll(); Set(backend, "input", oldInput); Set(plugin, "baseline", baseline);
                Set(look, "lastViewMatrix", oldMatrix); physical.Value = oldPhysical;
                databaseField.SetValue(null, oldDatabase);
                UnityEngine.Object.Destroy(databaseObject); UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(root);
            }
        }
    }
}
