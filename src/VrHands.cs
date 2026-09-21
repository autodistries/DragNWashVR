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
using FluidRenderingForGames;

namespace WalkNWash.VRCompanion
{
    internal sealed class VrHands : IDisposable
    {
        private static VrHands current;
        private readonly Plugin host;
        private readonly ToolGrips grips;
        internal ToolGrips Grips => grips;
        private readonly HashSet<int> warnedHitboxes = new HashSet<int>();
        private readonly Harmony patches = new Harmony(Plugin.Id + ".hands");
        private readonly Dictionary<MonoBehaviour, ContactState> states = new Dictionary<MonoBehaviour, ContactState>();
        private readonly Dictionary<Transform, SavedTransform> toolRoots = new Dictionary<Transform, SavedTransform>();
        private readonly ConfigEntry<bool> enabled, haptics, dunk, smoothSoap, diagnostics, triggerExtend;
        private readonly ConfigEntry<float> reach, probeRadius, spongeRadius, extraSoapAmount, palmReachTilt, directReach, plapSpeed, plapRearmSpeed;
        private readonly ConfigEntry<Vector3> leftRotationOffset;
        private bool failed;
        private static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();

        internal VrHands(Plugin host, ConfigFile config)
        {
            this.host = host; current = this;
            grips = new ToolGrips(config, host.Roles.ToolOnLeft, host.Preferences);
            enabled = config.Bind("Hands", "Controller Hands", true, "Track idle hands/tools and aim active reach from each controller.");
            palmReachTilt = host.Preferences.Bind("Hands", "Left Palm Reach Tilt", 52.5f, new ConfigDescription("Free-hand active reach: degrees from the palm outward normal toward the fingers. 0 points straight out of palm; 90 follows fingers. Surface contact still wraps normally.", new AcceptableValueRange<float>(0, 90)));
            reach = host.Preferences.Bind("Hands", "Hand Reach", 1.5f, new ConfigDescription("Maximum forward reach from the controller, also limited to 2 game units from the character eyes. Set to 0 for no forward extension.", new AcceptableValueRange<float>(0f, 2f)));
            triggerExtend = host.Preferences.Bind("Hands", "Trigger Extend", true,
                "Let the matching use trigger add assisted reach after physical contact misses.");
            directReach = config.Bind("Hands", "Contact Use Reach", .06f,
                new ConfigDescription("Physical contact reach in tracking-space metres.", new AcceptableValueRange<float>(0f, .15f)));
            plapSpeed = config.Bind("Hands", "Contact Plap Speed", .75f,
                new ConfigDescription("Inward palm speed required for a physical slap, in metres per second.", new AcceptableValueRange<float>(.2f, 3f)));
            plapRearmSpeed = config.Bind("Hands", "Contact Plap Rearm Speed", .2f,
                new ConfigDescription("Slow down below this inward speed to rearm a slap during contact.", new AcceptableValueRange<float>(0f, 1f)));
            probeRadius = config.Bind("Hands", "Contact Probe Radius", .045f, new ConfigDescription("Close-contact probe radius for hand/sponge rubbing.", new AcceptableValueRange<float>(0f, .1f)));
            spongeRadius = config.Bind("Hands", "Sponge Contact Radius", .085f,
                new ConfigDescription("Sponge contact volume in tracking meters when Trigger Extend is disabled, including side-on rubbing.", new AcceptableValueRange<float>(.02f, .15f)));
            haptics = host.Preferences.Bind("Hands", "Contact Haptics", true, "Brief feedback on contact and while rubbing.");
            leftRotationOffset = host.Preferences.Bind("Hands", "Left Hand Rotation Offset", new Vector3(15, 90, 90), "Reference correction for the free-hand mesh (mirrored when the free hand is right). Does not change held tools; contact still aligns the palm to the surface.");
            dunk = config.Bind("Hands", "Direct Sponge Dunk", true, "Hold the tool-hand trigger and place the sponge near a refill target to use its original refill action.");
            smoothSoap = config.Bind("Hands", "Smooth Soap Strokes", true, "Fill gaps between consecutive sponge contacts on the same surface. Original supply drain and interaction events remain once per update.");
            extraSoapAmount = config.Bind("Hands", "Extra Soap Amount", .5f, new ConfigDescription("Total extra soap shared across all gap-filling stamps, relative to one native stamp. 0 disables extra stamps; 0.5 adds at most half a stamp per update.", new AcceptableValueRange<float>(0f, 2f)));
            diagnostics = config.Bind("Hands", "Sponge Diagnostics", false, "Log sponge update rate, missed contacts, unchanged controller positions and added soap stamps every three seconds while used.");
            try
            {
                foreach (Type type in new[] { typeof(PlapperHand), typeof(ToolModelSponge) })
                {
                    Hook(type, "UseContinuous", nameof(Use));
                    Hook(type, "UpdateNotInUse", nameof(Idle));
                    string update = type == typeof(PlapperHand) ? "UpdatePlapper" : "UpdateSponge";
                    patches.Patch(AccessTools.Method(type, update), transpiler: new HarmonyMethod(typeof(VrHands), nameof(ContactUpdate)));
                }
                Hook(typeof(ToolModelScreenMovable), "UpdateMovablePosition", nameof(BeforeScreenMove));
                Hook(typeof(Interactable), "GetBestInteractable", nameof(Select));
                Hook(typeof(Interacter), "OnAttackStarted", nameof(BeforeInteract));
                Hook(typeof(ToolTest), "OnPlapStarted", nameof(BeforePlap));
                Hook(typeof(ToolTest), "LateUpdate", nameof(BeforeTools), nameof(AfterTools));
                Hook(typeof(ToolManager), "StartUseTool", nameof(BeforeToolStart));
                host.HandLog("Controller hand hooks ready; physical contact active, Trigger Extend " + (triggerExtend.Value ? "enabled." : "disabled."));
            }
            catch (Exception e) { Fail(e); }
        }
        internal bool Active => !failed && enabled.Value && host.HandsMode;
        internal void Maintain()
        {
            try
            {
                if (!Active) { RestoreAll(); return; }
                foreach (var model in states.Keys.Where(m => !m).ToArray()) Restore(model);
                foreach (var root in toolRoots.Keys.Where(t => !t).ToArray()) toolRoots.Remove(root);
                foreach (var state in states.Values)
                {
                    if (!host.Calibrating && host.TryHandFrame(state.Hand, out _)) continue;
                    state.Using = state.PreviousValid = state.Valid = false;
                    state.Stroke.Begin(false); state.Contact.Reset(); state.Stabilizer.Reset(); state.Contacting = false;
                    state.ContactPhysics(false); state.Show(false);
                    Set(state.Model, "hitCollider", null); Set(state.Model, "splatted", false);
                }
                if (!host.TryHandFrame(host.ToolHand, out _))
                {
                    foreach (var root in toolRoots.Values) root.Show(false);
                }
            }
            catch (Exception e) { Fail(e); }
        }
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
            internal readonly Vector3 Position, Scale;
            internal readonly Quaternion Rotation;
            private readonly Renderer[] renderers;
            private readonly bool[] visible;
            internal SavedTransform(Transform t)
            {
                Position = t.localPosition; Rotation = t.localRotation; Scale = t.localScale;
                renderers = t.GetComponentsInChildren<Renderer>(true); visible = renderers.Select(r => r.enabled).ToArray();
            }
            internal void Show(bool show) { for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].enabled = show && visible[i]; }
            internal void Restore(Transform t) { if (t) { t.localPosition = Position; t.localRotation = Rotation; t.localScale = Scale; Show(true); } }
        }
        private sealed class ContactState
        {
            internal MonoBehaviour Model;
            internal Transform Visual;
            internal SavedTransform Original;
            internal Renderer[] Renderers;
            internal bool[] RendererEnabled;
            internal Collider[] Colliders;
            internal bool[] ColliderEnabled;
            internal MonoBehaviour[] Jiggle;
            internal bool[] JiggleEnabled;
            internal HandFrame Frame;
            internal HandHit Hit;
            internal Vector3 Previous, LastFeedback;
            internal bool Valid, Using, PreviousValid, Contacting;
            internal float Blend, NextFeedback, NextDunk;
            internal int LastFrame = -1, Hand;
            internal readonly ContactDrivenUse Contact = new ContactDrivenUse();
            internal readonly SpongeStroke Stroke = new SpongeStroke();
            internal readonly ContactStabilizer Stabilizer = new ContactStabilizer();
            internal int Samples, Contacts, Unchanged, ExtraStamps;
            internal float DiagnosticStart;
            internal void Show(bool visible)
            { for (int i = 0; i < Renderers.Length; i++) if (Renderers[i]) Renderers[i].enabled = visible && RendererEnabled[i]; }
            internal void QueryColliders(bool active)
            {
                for (int i = 0; i < Colliders.Length; i++) if (Colliders[i]) Colliders[i].enabled = active && ColliderEnabled[i];
            }
            internal void ContactPhysics(bool active)
            {
                QueryColliders(active);
                for (int i = 0; i < Jiggle.Length; i++) if (Jiggle[i]) Jiggle[i].enabled = active && JiggleEnabled[i];
            }
            internal void Restore() { Original.Restore(Visual); Show(true); ContactPhysics(true); }
        }
        private ContactState State(MonoBehaviour model)
        {
            if (states.TryGetValue(model, out var value)) return value;
            var visual = (Transform)Backend.Field(model, model is PlapperHand ? "hand" : "sponge");
            value = new ContactState { Model = model, Visual = visual, Original = new SavedTransform(visual),
                Hand = model is PlapperHand ? host.FreeHand : host.ToolHand, Renderers = visual.GetComponentsInChildren<Renderer>(true) };
            value.RendererEnabled = value.Renderers.Select(r => r.enabled).ToArray();
            value.Colliders = visual.GetComponentsInChildren<Collider>(true);
            value.ColliderEnabled = value.Colliders.Select(c => c.enabled).ToArray();
            value.Jiggle = visual.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c && c.GetType().Name == "JiggleColliderExample").ToArray();
            value.JiggleEnabled = value.Jiggle.Select(c => c.enabled).ToArray();
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
            try { self.Update(model, active, Time.unscaledTime, Time.deltaTime); return false; }
            catch (Exception e) { self.Fail(e); return true; }
        }
        internal void Update(MonoBehaviour model, bool requested, float now, float deltaTime)
        {
            var state = State(model);
            if (state.LastFrame == Time.frameCount) return;
            state.LastFrame = Time.frameCount;
            state.Valid = host.TryHandFrame(state.Hand, out state.Frame);
            if (state.Valid && model is ToolModel)
                state.Frame = grips.Apply(ToolManager.GetCurrentTool()?.name, state.Frame, host.HandScale);
            if (state.Valid && model is PlapperHand) state.Frame.Direction = FreeHandReachDirection(state.Frame.Rotation);
            bool usable = state.Valid && !host.Calibrating;
            bool physical = false, plap = false;
            state.Hit = new HandHit { Point = state.Frame.Position, Normal = -(state.Frame.Rotation * Vector3.forward) };
            state.QueryColliders(false);
            try
            {
                if (usable)
                {
                    var direct = state.Frame;
                    direct.Direction = direct.Rotation * (model is PlapperHand ? FreeRotation : Quaternion.identity) * Vector3.forward;
                    float scale = Mathf.Max(.01f, Mathf.Abs(host.HandScale));
                    state.Hit = state.Contact.Probe(direct, host.HandRig, host.HandBodyEye,
                        directReach.Value * scale, (model is ToolModelSponge && !triggerExtend.Value ? spongeRadius.Value : probeRadius.Value) * scale, now,
                        plapSpeed.Value, plapRearmSpeed.Value, out plap);
                    physical = state.Hit.Collider != null;
                    if (!physical && requested && triggerExtend.Value)
                        state.Hit = HandContact.Find(state.Frame, host.HandBodyEye, state.Previous, state.PreviousValid,
                            reach.Value, probeRadius.Value);
                }
                else state.Contact.Reset();
            }
            finally { state.ContactPhysics(state.Valid); }

            bool active = usable && (physical || (requested && triggerExtend.Value));
            state.Stroke.Begin(active && smoothSoap.Value);
            if (physical && model is PlapperHand) Set(model, "splatted", !plap);
            else if (model is ToolModelSponge) Set(model, "splatted", state.Contacting && state.Hit.Collider != null);
            else if (!active || !state.Using) Set(model, "splatted", false);
            state.Using = active;
            state.Contacting = active && state.Hit.Collider != null;
            // Physical contact is immediate. Assisted reach retains its progress across frames.
            state.Blend = physical ? 1f : active ? Mathf.MoveTowards(state.Blend, 1f, deltaTime * 20f) : 0f;
            if (!active) state.Hit = new HandHit { Point = state.Frame.Position, Normal = -(state.Frame.Rotation * Vector3.forward) };
            bool unchanged = state.PreviousValid && Vector3.SqrMagnitude(state.Previous - state.Frame.Position) < .00000001f;
            state.Previous = state.Frame.Position; state.PreviousValid = usable && active;
            Set(model, "lookOffset", Vector2.zero);
            Set(model, "usingT", state.Blend);
            Set(model, "hitCollider", active ? state.Hit.Collider : null);
            Set(model, "raycastTarget", state.Visual.parent.InverseTransformPoint(state.Hit.Point));
            Quaternion rotation = VisualRotation(state);
            Vector3 presentationPoint = state.Hit.Point;
            Quaternion presentationRotation = rotation;
            if (physical)
                state.Stabilizer.Apply(state.Hit, state.Visual.parent, deltaTime, ref presentationPoint, ref presentationRotation);
            else state.Stabilizer.Reset();
            Set(model, "raycastTargetRotation", Quaternion.Inverse(state.Visual.parent.rotation) * rotation);
            state.Show(state.Valid);
            string method = model is PlapperHand ? "UpdatePlapper" : "UpdateSponge";
            AccessTools.Method(model.GetType(), method).Invoke(model, null);
            if (model is ToolModelSponge)
            {
                if (usable && requested && dunk.Value) TryDunk(state);
                AccessTools.Method(model.GetType(), "UpdateAnimatorFillAmount").Invoke(model, null);
                if (diagnostics.Value && active)
                {
                    if (state.Samples++ == 0) state.DiagnosticStart = Time.unscaledTime;
                    if (unchanged) state.Unchanged++;
                    float elapsed = Time.unscaledTime - state.DiagnosticStart;
                    if (elapsed >= 3)
                    {
                        host.HandLog($"Sponge: {state.Samples / elapsed:F1} updates/s; {state.Contacts}/{state.Samples} contact updates; {state.Unchanged} unchanged controller samples; {state.ExtraStamps} gap stamps.");
                        state.Samples = state.Contacts = state.Unchanged = state.ExtraStamps = 0;
                    }
                }
                else state.Samples = state.Contacts = state.Unchanged = state.ExtraStamps = 0;
            }
            if (haptics.Value && state.Hit.Collider && Time.unscaledTime >= state.NextFeedback
                && Vector3.Distance(state.Visual.position, state.Hit.Point) < .1f)
            {
                float distance = Vector3.Distance(state.Hit.Point, state.LastFeedback);
                if (distance > .008f) host.HandPulse(state.Hand, Mathf.Clamp(.12f + distance * 2, .12f, .6f));
                state.LastFeedback = state.Hit.Point; state.NextFeedback = Time.unscaledTime + .08f;
            }
        }
        private Quaternion VisualRotation(ContactState state)
        {
            // plapper_L fingers run along local +Y; its contact palm faces +Z.
            // Correct mesh basis; active left reach uses this same palm/finger basis.
            Quaternion wrist = state.Frame.Rotation;
            if (state.Model is PlapperHand) wrist *= FreeRotation;
            return state.Hit.Collider ? HandContact.SurfaceRotation(state.Hit.Normal, wrist) : wrist;
        }
        // Injected before the original visual transform block. The native contact/effect tail remains.
        private static bool PlaceVisual(MonoBehaviour model)
        {
            if (current == null || !current.Active || !current.states.TryGetValue(model, out var s)) return false;
            if (!s.Valid) return true;
            var rotation = current.VisualRotation(s);
            Vector3 position = Vector3.Lerp(s.Frame.Position, s.Hit.Point, s.Blend);
            if (s.Contacting && s.Stabilizer.TryPose(out var surfacePoint, out var surfaceRotation))
            { position = surfacePoint; rotation = surfaceRotation; }
            var curve = (AnimationCurve)Backend.Field(model, model is PlapperHand ? "plapNormalOffset" : "spongeNormalOffset");
            if (s.Using && curve != null)
                position += HandContact.VisualOffset(s.Visual.parent, rotation, curve.Evaluate(s.Blend) * .2f,
                    s.Contacting && s.Blend >= .999f);
            if (model is PlapperHand)
                s.Visual.localScale = Vector3.Scale(s.Original.Scale, current.host.Roles.ToolOnLeft ? new Vector3(-1, 1, 1) : Vector3.one);
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
                if (instruction.operand is MethodInfo emission && emission.DeclaringType == typeof(FluidParticleSystemSettings)
                    && emission.Name == nameof(FluidParticleSystemSettings.OnFluidCollision))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    foreach (object label in Labels(instruction)) Labels(load).Add(label);
                    Labels(instruction).Clear();
                    yield return load;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(VrHands), nameof(EmitSoap));
                }
                if (instruction.operand is MethodInfo hitMethod && hitMethod.DeclaringType == typeof(HitboxTrigger)
                    && hitMethod.Name == "Hit")
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(VrHands), nameof(DispatchHit));
                }
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
        private static void EmitSoap(FluidParticleSystemSettings fluid, FluidParticleSystem.ParticleCollision contact, MonoBehaviour model)
        {
            if (current != null && current.Active && model is ToolModelSponge && current.states.TryGetValue(model, out var state))
            {
                // Stamp on the detected skin, not the model's decorative normal offset.
                if (state.Hit.Collider) contact.position = state.Hit.Point;
                state.Contacts++;
                if (current.smoothSoap.Value) { state.ExtraStamps += state.Stroke.Emit(fluid, contact, current.extraSoapAmount.Value); return; }
            }
            fluid.OnFluidCollision(contact);
        }
        // A malformed scene hitbox must not disable tracking for both controllers.
        private static void DispatchHit(HitboxTrigger target, FluidRenderingForGames.FluidParticleSystemSettings fluid, HitboxTrigger.HitType kind)
        {
            if (current == null || !current.Active) { target.Hit(fluid, kind); return; }
            if (!target) return;
            try
            {
                if (target is HitboxPlapInteractable && !(Backend.Field(target, "interactable") as Interactable))
                {
                    if (current.warnedHitboxes.Add(target.GetInstanceID()))
                        current.host.HandLog("Ignoring hand hitbox with missing interaction target: " + target.name);
                    return;
                }
                target.Hit(fluid, kind);
            }
            catch (Exception e)
            {
                if (current.warnedHitboxes.Add(target.GetInstanceID()))
                    current.host.HandLog("Hand hitbox callback failed; controller tracking remains active: " + target.name + ": " + e);
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
        private Quaternion FreeRotation => host.Roles.ToolOnLeft
            ? MirrorRotation(Quaternion.Euler(leftRotationOffset.Value)) : Quaternion.Euler(leftRotationOffset.Value);
        internal static Quaternion MirrorRotation(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);
        internal Vector3 FreeHandReachDirection(Quaternion controllerRotation)
            => HandContact.PalmDirection(controllerRotation, FreeRotation, palmReachTilt.Value);
        private Interactable FindInteractable(Tool tool)
        {
            if (!host.TryHandFrame(host.FreeHand, out var frame)) return null;
            frame.Direction = FreeHandReachDirection(frame.Rotation);
            var list = AccessTools.Field(typeof(Interactable), "_interactables").GetValue(null) as IEnumerable;
            Interactable best = null; float score = 0;
            if (list == null) return null;
            foreach (Interactable item in list)
            {
                if (!item || !item.gameObject.activeInHierarchy || !(bool)AccessTools.Method(item.GetType(), "CanInteract").Invoke(item, new object[] { tool })) continue;
                Vector3 delta = item.transform.position - frame.Position;
                if (delta.magnitude > 3 || Vector3.Distance(host.HandBodyEye, item.transform.position) > 3) continue;
                float angle = Vector3.Angle(frame.Direction, delta);
                // Match vanilla selection: no collider occlusion gate, 40 degree cone.
                float value = (1 - angle / 40f) * 10f / Mathf.Max(delta.magnitude, 1f);
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
        private static bool BeforeInteract(Interacter __instance)
        {
            // Unity destroyed objects still have managed fields. Never refresh a
            // stale subscriber's target, even during desktop/fallback operation.
            if (!__instance || !__instance.isActiveAndEnabled) return false;
            if (current == null || !current.Active) return true;
            try { Set(__instance, "_bestInteractable", Interactable.GetBestInteractable(ToolManager.GetCurrentTool())); }
            catch (Exception e) { current.Fail(e); }
            return true;
        }
        private static void BeforePlap()
        {
            if (current == null || !current.Active) return;
            try { Interactable.GetBestInteractable(ToolManager.GetCurrentTool()); }
            catch (Exception e) { current.Fail(e); }
        }
        private void TryDunk(ContactState state)
        {
            if (Time.unscaledTime < state.NextDunk || Number(state.Model, "fillAmount") >= .98f) return;
            var list = AccessTools.Field(typeof(Interactable), "_interactables").GetValue(null) as IEnumerable;
            if (list == null) return;
            foreach (Interactable item in list)
            {
                if (!(item is InteractableWetSponge) || !item.gameObject.activeInHierarchy
                    || Vector3.Distance(state.Visual.position, item.transform.position) > .2f
                    || Vector3.Distance(host.HandBodyEye, item.transform.position) > 2f
                    || !HandContact.Clear(host.HandBodyEye, state.Frame.Position)
                    || !VisibleTarget(state.Frame.Position, item)) continue;
                if (((InteractableWetSponge)item).bucket == null || ((InteractableWetSponge)item).bucket.fillAmount <= 0) continue;
                var tool = ToolManager.GetCurrentTool();
                if (!(bool)AccessTools.Method(item.GetType(), "CanInteract").Invoke(item, new object[] { tool })) continue;
                item.Interact(tool); state.NextDunk = Time.unscaledTime + .5f;
                if (haptics.Value) host.HandPulse(host.ToolHand, .35f);
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
                bool freeUsing = current.states.Values.Any(s => s.Model is PlapperHand && s.Model && s.Model.gameObject.activeInHierarchy && s.Valid && s.Using);
                bool toolUsing = (bool)Backend.Field(manager, "_useButtonDown") && current.host.TryHandFrame(current.host.ToolHand, out _);
                var model = CurrentToolModel();
                bool spongeUsing = model is ToolModelSponge && current.states.TryGetValue(model, out var state) && state.Valid && state.Using;
                current.host.HandInteraction(freeUsing || toolUsing || spongeUsing);
            }
            catch (Exception e) { current.Fail(e); }
        }
        private static bool BeforeScreenMove(ToolModelScreenMovable __instance)
        {
            if (current == null || !current.Active || !current.host.TryHandFrame(current.host.ToolHand, out _)) return true;
            Set(__instance, "lookOffset", Vector2.zero);
            return false; // Tracked sprayer must not rotate the camera through desktop motion assistance.
        }
        private void UpdateTool()
        {
            if (!Active) { RestoreAll(); return; }
            try
            {
                var model = CurrentToolModel();
                if (!model || model is ToolModelSponge) return;
                if (!toolRoots.ContainsKey(model.transform)) toolRoots.Add(model.transform, new SavedTransform(model.transform));
                bool valid = host.TryHandFrame(host.ToolHand, out var frame);
                toolRoots[model.transform].Show(valid);
                if (valid)
                {
                    frame = grips.Apply(ToolManager.GetCurrentTool()?.name, frame, host.HandScale);
                    model.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
                }
                // Native canceled callbacks stop spraying when tracking/focus is lost.
            }
            catch (Exception e) { Fail(e); }
        }
        internal bool DiagnosticPose(int hand, out Vector3 target, out Transform visual, out bool usingHand, out Bounds bounds)
        {
            target = default; visual = null; usingHand = false; bounds = default;
            var equipped = hand == host.ToolHand ? CurrentToolModel() : null;
            if (hand == host.ToolHand && equipped && !(equipped is ToolModelSponge))
            {
                if (!host.TryHandFrame(host.ToolHand, out var frame)) return false;
                frame = grips.Apply(ToolManager.GetCurrentTool()?.name, frame, host.HandScale);
                target = frame.Position; visual = equipped.transform; usingHand = frame.Trigger > .35f;
                bounds = new Bounds(visual.position, Vector3.zero);
                bool found = false;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
                }
                return true;
            }
            foreach (var state in states.Values)
            {
                if (state.Hand != hand || (hand == host.ToolHand && state.Model != equipped)) continue;
                if (!(state.Model is PlapperHand) && !(state.Model is ToolModelSponge)) continue;
                if (!state.Model || !state.Model.isActiveAndEnabled || !state.Valid || !state.Visual || state.LastFrame < Time.frameCount - 1) continue;
                target = state.Frame.Position; visual = state.Visual; usingHand = state.Using || state.Blend > .001f;
                bool found = false;
                foreach (var renderer in state.Renderers)
                {
                    if (!renderer || !renderer.enabled) continue;
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                if (!found) bounds = new Bounds(visual.position, Vector3.zero);
                return true;
            }
            return false;
        }
        internal void RefreshVisuals()
        {
            if (!Active) return;
            try
            {
                // ToolTest can place hands before their animated camera parent moves.
                // Reapply visual poses only: never repeat contact events or soap stamps.
                foreach (var state in states.Values)
                {
                    if (!state.Model || !host.TryHandFrame(state.Hand, out var frame)) continue;
                    if (state.Model is ToolModel)
                        frame = grips.Apply(ToolManager.GetCurrentTool()?.name, frame, host.HandScale);
                    if (state.Model is PlapperHand) frame.Direction = FreeHandReachDirection(frame.Rotation);
                    state.Frame = frame;
                    if (!state.Using) state.Hit = new HandHit { Point = frame.Position, Normal = -(frame.Rotation * Vector3.forward) };
                    PlaceVisual(state.Model);
                }
                // Roots follow the controller after native parent motion.
                var model = CurrentToolModel();
                if (model && !(model is ToolModelSponge) && host.TryHandFrame(host.ToolHand, out var right))
                {
                    right = grips.Apply(ToolManager.GetCurrentTool()?.name, right, host.HandScale);
                    model.transform.SetPositionAndRotation(right.Position, right.Rotation);
                }
            }
            catch (Exception e) { Fail(e); }
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
            toolRoots.Clear();
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