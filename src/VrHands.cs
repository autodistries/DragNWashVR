using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using com.gatordragongames.washnwalk.tools;

namespace WalkNWash.VRCompanion
{
    internal sealed class VrHands : IDisposable
    {
        private static VrHands current;
        private readonly Plugin host;
        private readonly Harmony patches = new Harmony(Plugin.Id + ".hands");
        private readonly Dictionary<MonoBehaviour, ContactState> states = new Dictionary<MonoBehaviour, ContactState>();
        private readonly Dictionary<Transform, SavedTransform> toolRoots = new Dictionary<Transform, SavedTransform>();
        private readonly ConfigEntry<bool> enabled, haptics, fingers, rightHand, dunk;
        private readonly ConfigEntry<float> reach, probeRadius, curlDegrees;
        private bool failed;
        private HandVisual rightVisual;
        private Transform rightSource;
        private static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();

        internal VrHands(Plugin host, ConfigFile config)
        {
            this.host = host; current = this;
            enabled = config.Bind("Hands", "Controller Hands", true, "Track idle hands/tools and aim active reach from each controller.");
            reach = config.Bind("Hands", "Assisted Reach", 1.5f, new ConfigDescription("Maximum forward reach from the controller, also limited to 2 game units from the character eyes.", new AcceptableValueRange<float>(.1f, 2f)));
            probeRadius = config.Bind("Hands", "Contact Probe Radius", .045f, new ConfigDescription("Close-contact probe radius for hand/sponge rubbing.", new AcceptableValueRange<float>(0f, .1f)));
            haptics = config.Bind("Hands", "Contact Haptics", true, "Brief feedback on contact and while rubbing.");
            fingers = config.Bind("Hands", "Idle Finger Curl", true, "Infer idle finger curl from grip and index trigger. Native animation owns active contact.");
            curlDegrees = config.Bind("Hands", "Finger Curl Degrees", 65f, new ConfigDescription("Idle curl angle per finger joint; negative reverses bending direction.", new AcceptableValueRange<float>(-90f, 90f)));
            rightHand = config.Bind("Hands", "Show Empty Right Hand", true, "Show a visual-only mirrored hand when no tool is equipped.");
            dunk = config.Bind("Hands", "Direct Sponge Dunk", true, "Hold right trigger and place the sponge near a refill target to use its original refill action.");
            try
            {
                foreach (Type type in new[] { typeof(PlapperHand), typeof(ToolModelSponge) })
                {
                    Hook(type, "UseContinuous", nameof(Use));
                    Hook(type, "UpdateNotInUse", nameof(Idle));
                    string update = type == typeof(PlapperHand) ? "UpdatePlapper" : "UpdateSponge";
                    patches.Patch(AccessTools.Method(type, update), transpiler: new HarmonyMethod(typeof(VrHands), nameof(ContactUpdate)));
                }
                Hook(typeof(Interactable), "GetBestInteractable", nameof(Select));
                Hook(typeof(Interacter), "OnAttackStarted", nameof(BeforeInteract));
                Hook(typeof(ToolTest), "OnPlapStarted", nameof(BeforePlap));
                Hook(typeof(ToolTest), "LateUpdate", nameof(BeforeTools), nameof(AfterTools));
                Hook(typeof(ToolManager), "StartUseTool", nameof(BeforeToolStart));
                host.HandLog("Controller hand hooks ready.");
            }
            catch (Exception e) { Fail(e); }
        }
        internal bool Active => !failed && enabled.Value && host.HandsMode;
        private void Hook(Type type, string name, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.Name, name);
            patches.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(VrHands), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(VrHands), postfix));
        }
        private static void Set(object obj, string name, object value)
        {
            string key = obj.GetType().FullName + ":" + name;
            if (!fields.TryGetValue(key, out var f)) fields[key] = f = AccessTools.Field(obj.GetType(), name)
                ?? throw new MissingFieldException(obj.GetType().Name, name);
            f.SetValue(obj, value);
        }
        private static float Number(object obj, string name) => Convert.ToSingle(Backend.Field(obj, name));
        private sealed class SavedTransform
        {
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            internal SavedTransform(Transform t) { Position = t.localPosition; Rotation = t.localRotation; }
            internal void Restore(Transform t) { if (t) { t.localPosition = Position; t.localRotation = Rotation; } }
        }
        private sealed class ContactState
        {
            internal MonoBehaviour Model;
            internal Transform Visual;
            internal SavedTransform Original;
            internal Renderer[] Renderers;
            internal bool[] RendererEnabled;
            internal HandVisual Fingers;
            internal HandFrame Frame;
            internal HandHit Hit;
            internal Vector3 Previous, LastFeedback;
            internal bool Valid, Using, PreviousValid;
            internal float Blend, NextFeedback, NextDunk;
            internal int LastFrame = -1, Hand;
            internal void Show(bool visible)
            { for (int i = 0; i < Renderers.Length; i++) if (Renderers[i]) Renderers[i].enabled = visible && RendererEnabled[i]; }
            internal void Restore() { Original.Restore(Visual); Show(true); Fingers?.Restore(); }
        }
        private ContactState State(MonoBehaviour model)
        {
            if (states.TryGetValue(model, out var value)) return value;
            var visual = (Transform)Backend.Field(model, model is PlapperHand ? "hand" : "sponge");
            value = new ContactState { Model = model, Visual = visual, Original = new SavedTransform(visual),
                Hand = model is PlapperHand ? 1 : 2, Renderers = visual.GetComponentsInChildren<Renderer>(true) };
            value.RendererEnabled = value.Renderers.Select(r => r.enabled).ToArray();
            if (model is PlapperHand) value.Fingers = new HandVisual(visual);
            states.Add(model, value);
            host.HandLog("Tracking " + model.GetType().Name + "; visual " + visual.name + "; parent " + visual.parent.name);
            return value;
        }
        private static bool Use(MonoBehaviour __instance) => Handle(__instance, true);
        private static bool Idle(MonoBehaviour __instance) => Handle(__instance, false);
        private static bool Handle(MonoBehaviour model, bool active)
        {
            var self = current;
            if (self == null || !self.Active) { self?.Restore(model); return true; }
            // Only the equipped sponge is controlled, never the placed/refill model.
            if (model is ToolModel && CurrentToolModel() != model) return true;
            try { self.Update(model, active); return false; }
            catch (Exception e) { self.Fail(e); return true; }
        }
        private void Update(MonoBehaviour model, bool active)
        {
            var state = State(model);
            if (state.LastFrame == Time.frameCount) return;
            state.LastFrame = Time.frameCount;
            state.Valid = host.TryHandFrame(state.Hand, out state.Frame);
            active &= state.Valid;
            if (active && !state.Using) state.Fingers?.Restore();
            if (!active || !state.Using) Set(model, "splatted", false);
            state.Using = active;
            state.Blend = Mathf.MoveTowards(state.Blend, active ? 1 : 0, Time.deltaTime * (active ? 20 : 8));
            state.Hit = active ? HandContact.Find(state.Frame, host.HandBodyEye, state.Previous, state.PreviousValid,
                reach.Value, probeRadius.Value) : new HandHit { Point = state.Frame.Position, Normal = -(state.Frame.Rotation * Vector3.forward) };
            state.Previous = state.Frame.Position; state.PreviousValid = state.Valid && active;
            Set(model, "lookOffset", Vector2.zero);
            Set(model, "usingT", state.Blend);
            Set(model, "hitCollider", active ? state.Hit.Collider : null);
            Set(model, "raycastTarget", state.Visual.parent.InverseTransformPoint(state.Hit.Point));
            Quaternion rotation = state.Hit.Collider ? HandContact.SurfaceRotation(state.Hit.Normal, state.Frame.Rotation) : state.Frame.Rotation;
            Set(model, "raycastTargetRotation", Quaternion.Inverse(state.Visual.parent.rotation) * rotation);
            state.Show(state.Valid);
            string method = model is PlapperHand ? "UpdatePlapper" : "UpdateSponge";
            AccessTools.Method(model.GetType(), method).Invoke(model, null);
            if (model is ToolModelSponge)
            {
                if (active && dunk.Value) TryDunk(state);
                AccessTools.Method(model.GetType(), "UpdateAnimatorFillAmount").Invoke(model, null);
            }
            if (fingers.Value) state.Fingers?.Pose(state.Frame.Curl, state.Frame.Trigger, active, curlDegrees.Value);
            if (haptics.Value && state.Hit.Collider && Time.unscaledTime >= state.NextFeedback
                && Vector3.Distance(state.Visual.position, state.Hit.Point) < .1f)
            {
                float distance = Vector3.Distance(state.Hit.Point, state.LastFeedback);
                if (distance > .008f) host.HandPulse(state.Hand, Mathf.Clamp(.12f + distance * 2, .12f, .6f));
                state.LastFeedback = state.Hit.Point; state.NextFeedback = Time.unscaledTime + .08f;
            }
        }
        // Injected before the original visual transform block. The native contact/effect tail remains.
        private static bool PlaceVisual(MonoBehaviour model)
        {
            if (current == null || !current.Active || !current.states.TryGetValue(model, out var s)) return false;
            if (!s.Valid) return true;
            var rotation = s.Hit.Collider ? HandContact.SurfaceRotation(s.Hit.Normal, s.Frame.Rotation) : s.Frame.Rotation;
            Vector3 position = Vector3.Lerp(s.Frame.Position, s.Hit.Point, s.Blend);
            var curve = (AnimationCurve)Backend.Field(model, model is PlapperHand ? "plapNormalOffset" : "spongeNormalOffset");
            if (s.Using && curve != null) position += rotation * Vector3.back * curve.Evaluate(s.Blend) * .2f;
            s.Visual.SetPositionAndRotation(position, rotation);
            return true;
        }
        private static Matrix4x4 ContactMatrix(PlapperHand model)
        {
            if (current == null || !current.Active || !current.states.TryGetValue(model, out var s)) return LookController.GetLookMatrix();
            // Native tail multiplies by its authored offset immediately after this call.
            return s.Visual.parent.localToWorldMatrix * Matrix4x4.Translate(new Vector3(.415f, .429f, -.1f));
        }
        private static Quaternion ContactRotation(PlapperHand model)
            => current != null && current.Active && current.states.TryGetValue(model, out var s) ? s.Visual.parent.rotation : LookController.GetLookRotation();
        // Harmony's net-framework Label generic is not forwarded by netstandard reference assemblies.
        private static IList Labels(CodeInstruction code) => (IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(code);
        private static IEnumerable<CodeInstruction> ContactUpdate(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            int startTail = code.FindIndex(i => i.opcode == OpCodes.Ldfld && (i.operand as FieldInfo)?.Name == "spongeAudioSource") - 1;
            if (startTail < 0 || code[startTail].opcode != OpCodes.Ldarg_0) throw new InvalidOperationException("Hand contact tail changed");
            object tail = generator.DefineLabel(); Labels(code[startTail]).Add(tail);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return CodeInstruction.Call(typeof(VrHands), nameof(PlaceVisual));
            yield return new CodeInstruction(OpCodes.Brtrue, tail);
            foreach (var instruction in code)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(LookController)
                    && (method.Name == "GetLookMatrix" || method.Name == "GetLookRotation"))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    foreach (object label in Labels(instruction)) Labels(load).Add(label);
                    Labels(instruction).Clear();
                    yield return load;
                    instruction.operand = AccessTools.Method(typeof(VrHands), method.Name == "GetLookMatrix" ? nameof(ContactMatrix) : nameof(ContactRotation));
                }
                yield return instruction;
            }
        }
        private static ToolModel CurrentToolModel()
        {
            var manager = AccessTools.Field(typeof(ToolManager), "_instance").GetValue(null);
            return manager == null ? null : (Backend.Field(manager, "_currentTool") as Tool)?.GetModel();
        }
        private static bool Select(Tool tool, ref Interactable __result)
        {
            if (current == null || !current.Active) return true;
            try
            {
                __result = current.FindInteractable(tool);
                AccessTools.Field(typeof(Interactable), "bestInteractable").SetValue(null, __result);
                return false;
            }
            catch (Exception e) { current.Fail(e); return true; }
        }
        private Interactable FindInteractable(Tool tool)
        {
            if (!host.TryHandFrame(1, out var frame)) return null;
            var list = AccessTools.Field(typeof(Interactable), "_interactables").GetValue(null) as IEnumerable;
            Interactable best = null; float score = 0;
            if (list == null) return null;
            foreach (Interactable item in list)
            {
                if (!item || !item.gameObject.activeInHierarchy || !(bool)AccessTools.Method(item.GetType(), "CanInteract").Invoke(item, new object[] { tool })) continue;
                Vector3 delta = item.transform.position - frame.Position;
                if (delta.magnitude > 3 || Vector3.Distance(host.HandBodyEye, item.transform.position) > 3
                    || !VisibleTarget(frame.Position, item)) continue;
                float angle = Vector3.Angle(frame.Direction, delta);
                float value = (1 - angle / 35f) / Mathf.Max(delta.magnitude, .3f);
                if (value > score) { score = value; best = item; }
            }
            return best;
        }
        private static bool VisibleTarget(Vector3 origin, Interactable item)
        {
            Vector3 delta = item.transform.position - origin;
            if (!Physics.Raycast(origin, delta.normalized, out var hit, Mathf.Max(0, delta.magnitude - .025f), HandContact.Mask, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.transform.IsChildOf(item.transform) || item.transform.IsChildOf(hit.collider.transform);
        }
        private static void BeforeInteract(Interacter __instance)
        {
            if (current == null || !current.Active) return;
            try { Set(__instance, "_bestInteractable", Interactable.GetBestInteractable(ToolManager.GetCurrentTool())); }
            catch (Exception e) { current.Fail(e); }
        }
        private static void BeforePlap()
        {
            if (current == null || !current.Active) return;
            try { Interactable.GetBestInteractable(ToolManager.GetCurrentTool()); }
            catch (Exception e) { current.Fail(e); }
        }
        private void TryDunk(ContactState state)
        {
            if (Time.unscaledTime < state.NextDunk) return;
            var list = AccessTools.Field(typeof(Interactable), "_interactables").GetValue(null) as IEnumerable;
            if (list == null) return;
            foreach (Interactable item in list)
            {
                if (!(item is InteractableWetSponge) || !item.gameObject.activeInHierarchy
                    || Vector3.Distance(state.Frame.Position, item.transform.position) > .2f || !VisibleTarget(state.Frame.Position, item)) continue;
                var tool = ToolManager.GetCurrentTool();
                if (!(bool)AccessTools.Method(item.GetType(), "CanInteract").Invoke(item, new object[] { tool })) continue;
                item.Interact(tool); state.NextDunk = Time.unscaledTime + .5f;
                if (haptics.Value) host.HandPulse(2, .35f);
                break;
            }
        }
        private static void BeforeTools() { current?.UpdateTool(); }
        private static void BeforeToolStart() { current?.UpdateTool(); }
        private static void AfterTools(ToolTest __instance)
        {
            if (current == null || !current.Active) return;
            try
            {
                var manager = Backend.Field(__instance, "_toolManager");
                bool left = (bool)Backend.Field(__instance, "_plapperDown") && current.host.TryHandFrame(1, out _);
                bool right = (bool)Backend.Field(manager, "_useButtonDown") && current.host.TryHandFrame(2, out _);
                current.host.HandInteraction(left || right);
                current.UpdateRightHand();
            }
            catch (Exception e) { current.Fail(e); }
        }
        private void UpdateTool()
        {
            if (!Active) { RestoreAll(); return; }
            try
            {
                var model = CurrentToolModel();
                if (!model || model is ToolModelSponge) return;
                if (!toolRoots.ContainsKey(model.transform)) toolRoots.Add(model.transform, new SavedTransform(model.transform));
                if (host.TryHandFrame(2, out var frame)) model.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
                // Native canceled callbacks stop spraying when tracking/focus is lost.
            }
            catch (Exception e) { Fail(e); }
        }
        private void UpdateRightHand()
        {
            if (!rightHand.Value) { rightVisual?.Place(default, false); return; }
            var left = states.Values.FirstOrDefault(s => s.Hand == 1 && s.Visual);
            if (left == null) return;
            if (rightSource != left.Visual)
            {
                rightVisual?.Dispose(); rightVisual = HandVisual.Mirror(left.Visual); rightSource = left.Visual;
            }
            bool empty = ToolManager.GetCurrentTool() == ToolManager.GetEmptyTool();
            bool valid = host.TryHandFrame(2, out var frame);
            rightVisual.Place(frame, empty && valid);
            if (fingers.Value) rightVisual.Pose(frame.Curl, frame.Trigger, false, curlDegrees.Value);
        }
        private void Restore(MonoBehaviour model)
        {
            if (!states.TryGetValue(model, out var s)) return;
            if (model) { Set(model, "hitCollider", null); Set(model, "usingT", 0f); Set(model, "splatted", false); }
            s.Restore(); states.Remove(model);
        }
        internal void RestoreAll()
        {
            foreach (var model in states.Keys.ToArray()) Restore(model);
            foreach (var root in toolRoots) root.Value.Restore(root.Key);
            toolRoots.Clear(); rightVisual?.Dispose(); rightVisual = null; rightSource = null;
        }
        private void Fail(Exception e)
        {
            failed = true; patches.UnpatchSelf();
            try { RestoreAll(); } catch { }
            host.HandLog("Controller hands disabled; other controls remain active: " + e);
        }
        public void Dispose() { RestoreAll(); patches.UnpatchSelf(); if (current == this) current = null; }
    }
}
