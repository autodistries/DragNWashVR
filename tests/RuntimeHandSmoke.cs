using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using com.gatordragongames.washnwalk.tools;

namespace WalkNWash.VRCompanion
{
    internal static class RuntimeHandSmoke
    {
        public sealed class EligibleObject : Interactable
        {
            internal bool Eligible = true;
            protected override bool CanInteract(Tool tool) => Eligible;
        }
        private sealed class Input : IControllerInput
        {
            internal Vector3 Left = new Vector3(-.25f, 1.8f, 0), Right = new Vector3(.25f, 1.8f, 0);
            internal Quaternion LeftRotation = Quaternion.identity;
            internal bool LeftValid = true, RightValid = true;
            public bool Hand(int hand, bool aim, ulong space, long time, Array poses, out Vector3 position, out Quaternion rotation)
            { position = hand == 1 ? Left : Right; rotation = hand == 1 && !aim ? LeftRotation : Quaternion.identity; return hand == 1 ? LeftValid : RightValid; }
            public float Squeeze(int hand) => .6f;
            public void Pulse(int hand, float strength) { }
            public void Poll(out Vector2 move, out Vector2 turn, out float left, out float right, out bool jump, out bool hud, out bool crouch)
            { move = turn = Vector2.zero; left = right = 0; jump = hud = crouch = false; }
            public void Dispose() { }
        }
        private static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
        private static void Near(Action<bool, string> check, Vector3 a, Vector3 b, string name) => check(Vector3.Distance(a, b) < .002f, name);
        private static int fluidEmissions;
        private static Vector3 fluidPoint;
        private static bool FluidBoundary(object[] __args)
        {
            fluidEmissions++; fluidPoint = (Vector3)Backend.Field(__args[0], "position");
            return false; // Record native emission without starting the GPU fluid simulation in this fixture.
        }

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
                var idleCollider = visual.gameObject.AddComponent<BoxCollider>();
                var disabledCollider = visual.gameObject.AddComponent<SphereCollider>(); disabledCollider.enabled = false;
                var jiggle = (MonoBehaviour)visual.gameObject.AddComponent(AccessTools.TypeByName("GatorDragonGames.JigglePhysics.JiggleColliderExample"));
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
                check(idleCollider.enabled, "idle hand preserves authored passive contact collider");
                check(jiggle.enabled, "idle hand preserves the actual game jiggle component");
                check(!disabledCollider.enabled, "idle tracking cannot enable an originally disabled collider");
                Near(check, visual.position, left.Position, "native idle hook places hand at controller");
                check(rubs == 0 && slaps == 0, "idle hand touching view ray cannot produce contact effects");
                // Reproduce the reported grip: old mesh fingers (+Y) point right,
                // old palm (+Z) points forward. The corrected mesh must point forward/down.
                input.LeftRotation = Quaternion.Euler(0, 0, -90);
                tick(false);
                Near(check, visual.up, Vector3.forward, "reported sideways fingers now point forward");
                Near(check, visual.forward, Vector3.down, "reported forward palm now faces down");
                plugin.TryHandFrame(1, out var correctedFrame);
                Near(check, correctedFrame.Direction, Vector3.forward, "left mesh alignment cannot rotate controller aim");
                input.LeftRotation = Quaternion.Euler(0, 40, 0) * input.LeftRotation;
                tick(false);
                Near(check, visual.up, Quaternion.Euler(0, 40, 0) * Vector3.forward, "corrected hand still follows wrist rotation");
                input.LeftRotation = Quaternion.identity;
                Set(state, "Blend", 1f); Set(state, "Using", true); Set(model, "splatted", true);
                tick(true);
                check(idleCollider.enabled, "active hand restores authored contact collider");
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
                check(!idleCollider.enabled, "lost tracking disables passive contact too");
                check(!jiggle.enabled, "lost tracking disables actual jiggle contact");
                input.LeftValid = true;
                using (var fingers = new HandVisual(visual))
                {
                    Quaternion rest = finger.localRotation;
                    fingers.Pose(0, .5f, false, -60);
                    check(Quaternion.Angle(rest, finger.localRotation) > 29, "idle index finger curls from trigger");
                    check((Quaternion.Inverse(rest) * finger.localRotation * Vector3.up).z < -.49f,
                        "idle index finger bends inward rather than backward");
                    check((float)((ConfigEntry<float>)Backend.Field(hands, "curlDegrees")).DefaultValue < 0,
                        "installed hand default uses inward finger curl");
                    Quaternion curled = finger.localRotation;
                    fingers.Pose(1, 1, true, -60);
                    check(Quaternion.Angle(curled, finger.localRotation) < .01f, "idle curl cannot overwrite active animation");
                    fingers.Restore(); Near(check, finger.localRotation * Vector3.up, rest * Vector3.up, "finger pose restores before native animation");
                }
                SpongeChecks(plugin, hands, root, material, right, check);
                SelectionChecks(plugin, root, left, check);
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

        private static void SelectionChecks(Plugin plugin, GameObject root, HandFrame left, Action<bool, string> check)
        {
            var listField = AccessTools.Field(typeof(Interactable), "_interactables");
            var cachedField = AccessTools.Field(typeof(Interactable), "bestInteractable");
            var originalList = listField.GetValue(null); var originalCached = cachedField.GetValue(null);
            var near = new GameObject("Controller-selected object"); near.SetActive(false);
            near.transform.SetParent(root.transform, true); near.transform.position = left.Position + Vector3.forward * .3f;
            var side = new GameObject("Side object"); side.SetActive(false);
            side.transform.SetParent(root.transform, true); side.transform.position = left.Position + Vector3.right;
            try
            {
                listField.SetValue(null, new List<Interactable>());
                var forwardObject = near.AddComponent<EligibleObject>();
                var sideObject = side.AddComponent<EligibleObject>();
                near.SetActive(true); side.SetActive(true);
                check(Interactable.GetBestInteractable(null) == forwardObject, "controller direction selects eligible forward object");
                check(Interactable.GetCachedInteractable() == forwardObject, "controller selection and game cached target agree");
                forwardObject.Eligible = false;
                check(Interactable.GetBestInteractable(null) == null, "game eligibility is preserved for controller selection");
                forwardObject.Eligible = true;
                near.transform.position = left.Position + Vector3.forward * 4;
                check(Interactable.GetBestInteractable(null) == null, "controller selection preserves maximum interaction distance");
            }
            finally
            {
                near.SetActive(false); side.SetActive(false);
                listField.SetValue(null, originalList); cachedField.SetValue(null, originalCached);
                UnityEngine.Object.Destroy(near); UnityEngine.Object.Destroy(side);
            }
        }

        private static void SpongeChecks(Plugin plugin, VrHands hands, GameObject root, PhysicsMaterialExtension material,
            HandFrame right, Action<bool, string> check)
        {
            var managerField = AccessTools.Field(typeof(ToolManager), "_instance");
            var oldManager = managerField.GetValue(null);
            var eventField = AccessTools.Field(typeof(ToolModelSponge), "spongeRubbed");
            var oldRub = eventField.GetValue(null);
            var testPatch = new Harmony(Plugin.Id + ".hand-smoke-fluid");
            var fluidType = AccessTools.TypeByName("FluidRenderingForGames.FluidParticleSystemSettings");
            var fluid = ScriptableObject.CreateInstance(fluidType);
            var tool = ScriptableObject.CreateInstance<Tool>();
            var otherTool = ScriptableObject.CreateInstance<Tool>();
            var gameObject = new GameObject("Smoke sponge"); gameObject.SetActive(false);
            gameObject.transform.SetParent(root.transform, false);
            try
            {
                testPatch.Patch(AccessTools.Method(fluidType, "OnFluidCollision"), prefix: new HarmonyMethod(typeof(RuntimeHandSmoke), nameof(FluidBoundary)));
                var sponge = gameObject.AddComponent<ToolModelSponge>();
                var visual = new GameObject("Sponge visual").transform; visual.SetParent(gameObject.transform, false);
                Set(sponge, "sponge", visual); Set(sponge, "fluid", fluid);
                Set(sponge, "spongeNormalOffset", AnimationCurve.Constant(0, 1, 0));
                Set(sponge, "spongeAudioSource", gameObject.AddComponent<AudioSource>());
                Set(sponge, "wetSpongeMat", material); Set(sponge, "drySpongeMat", material);
                Set(sponge, "hitColliders", new Collider[32]); Set(sponge, "fillAmount", .8f);
                Set(tool, "_model", sponge);
                var manager = new ToolManager(); Set(manager, "_currentTool", tool); Set(manager, "_emptyTool", otherTool);
                managerField.SetValue(null, manager);
                int rubs = 0; Vector3 rubPoint = Vector3.zero;
                ToolModelSponge.SpongeRubAction callback = (model, collider, point, normal) => { rubs++; rubPoint = point; };
                eventField.SetValue(null, callback);
                fluidEmissions = 0;
                object state = AccessTools.Method(typeof(VrHands), "State").Invoke(hands, new object[] { sponge });
                Action<bool> tick = active =>
                {
                    Set(state, "LastFrame", -1);
                    if (active) sponge.UseContinuous(); else sponge.UpdateNotInUse();
                };
                tick(false); Near(check, visual.position, right.Position, "equipped idle sponge follows right controller");
                Near(check, visual.forward, right.Rotation * Vector3.forward, "left hand correction cannot rotate the right tool");
                check(rubs == 0 && fluidEmissions == 0, "idle sponge emits neither rub nor fluid");
                Set(state, "Using", true); Set(state, "Blend", 1f); Set(sponge, "splatted", true);
                tick(true);
                check(rubs == 1 && fluidEmissions == 1, "native sponge contact emits one rub and one fluid collision");
                Near(check, rubPoint, visual.position, "native sponge event matches visible contact");
                Near(check, fluidPoint, visual.position, "native fluid emission matches visible sponge");
                check(Convert.ToSingle(Backend.Field(sponge, "fillAmount")) < .8f, "native washing consumes sponge supply");
                tick(false);
                check(rubs == 1 && fluidEmissions == 1 && Backend.Field(sponge, "hitCollider") == null,
                    "sponge release stops fluid and clears contact");
                // The same component on a placed tool must stay on the original idle path.
                hands.RestoreAll(); Set(manager, "_currentTool", otherTool);
                Set(state, "LastFrame", -1); sponge.UpdateNotInUse();
                Near(check, visual.localPosition, Vector3.zero, "unequipped sponge returns to authored local idle position");
            }
            finally
            {
                hands.RestoreAll(); eventField.SetValue(null, oldRub); managerField.SetValue(null, oldManager);
                testPatch.UnpatchSelf(); UnityEngine.Object.Destroy(gameObject);
                UnityEngine.Object.Destroy(tool); UnityEngine.Object.Destroy(otherTool); UnityEngine.Object.Destroy(fluid);
            }
        }
    }
}
