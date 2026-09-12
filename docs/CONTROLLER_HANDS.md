# Controller-driven hands: feasibility and design

Study date: 2026-09-12. Companion baseline: v0.4.1 (`38d49c9`).
Status: research completed; no hand implementation installed.

## Recommendation

Use each controller's position **and** orientation. Keep the original distinction
between inactive presentation and active interaction. Preserve the game's contact
and washing pipeline; change how its target is chosen and represented.

| State | Position | Orientation | Gameplay effects |
| --- | --- | --- | --- |
| Inactive | Left hand / equipped right tool follows its controller | Calibrated grip pose, with per-model offset | None |
| Trigger held, no contact | Animate forward from controller toward a bounded target | Controller-directed palm/tool aim | None until contact qualifies |
| Trigger held, touching | Contact target follows controller motion along surface | Surface normal controls contact tilt; preserve wrist twist where compatible | Original slap/rub/wash logic |
| Release / tracking loss / dialogue / pause | Release contact, return to tracked idle or hide unavailable hand | No stale tracking used | Stop effects; require trigger release before reactivation |

Direction-only control is a useful first active-hand prototype, but the ray should
start at the controller, not the head. Otherwise translating a hand sideways has
no effect, and the two hands still feel like pointers attached to the face.

The table describes left-hand and sponge contact. Sprayers keep their normal
trigger-controlled stream, with the nozzle following the controller.

Begin with the original assisted extension distance for compatibility. Then test
a hybrid: close contact uses a short swept palm/sponge probe, with bounded forward
extension for surfaces outside comfortable physical reach. This gives a tangible
washing motion while retaining access to the dragon's higher parts. Distances,
transition blending, and contact hysteresis need headset tuning, not guesses baked
into the first implementation.

## Findings in the installed game

Reviewed local `DragNWash_Data/Managed/Assembly-CSharp.dll`, SHA-256:
`8b1b439e62bff222fbce00869a3343422c77d08e384db7ed323b5e66f44195e2`.
The local decompilation used for this study is `/tmp/walknwash-game-study.cs`;
method names below remain useful if that temporary file is removed. No decompiled
game source or assets are included in this repository.

| Component/method | Verified behavior | Integration consequence |
| --- | --- | --- |
| `PlapperHand.UseContinuous` | Builds a ray from `LookController` position/rotation plus accumulated mouse look; casts 3 game units with mask 385, accepts contact below 2 | Replace origin/direction and remove mouse accumulation for VR hand aiming |
| `PlapperHand.UpdatePlapper` | Moves visible hand toward cached target, aligns to hit normal, adds slap offset and roll, drives animator/audio, emits slap/rub events and hitbox effects | Preserve contact pipeline; keep target, visible position, and effect coordinates consistent |
| `ToolModelSponge.UseContinuous/UpdateSponge` | Similar ray and reach; surface alignment, sponge supply consumption, fluid collision, audio and hitboxes | The same target abstraction can serve both hands, while retaining separate state |
| `ToolTest.LateUpdate` | Updates tool and left hand independently using their held-action state | Update each interaction once per game frame, never once per rendered eye |
| `ToolManager` | Instantiates equipped models under `_toolModelAnchor`, destroys/replaces them on equip | Rebind right-tool attachment on every equip, and scope overrides to held models |
| `Interactable.GetBestInteractable` | Chooses eligible nearby objects from `Camera.main` direction, angle and distance | Hand aiming must also replace object selection, not just washing rays |
| `Interacter` | Caches selected object in Update; left-click callback activates non-hand objects | Revalidate controller selection at click time; synchronize prompt and cached target |
| `HitboxPlapInteractable` | Activates hand-interactable objects on a slap hit | Preserve this path to avoid duplicate activation through generic selection |
| `InteractableWetSponge` / `ToolTest.OnPlapStarted` | Refill is an interactable action; a cached refill target suppresses ordinary left-hand use | Preserve existing refill first; direct right-hand dunking is an explicit later UX change |
| `ToolModelSprayer` / `WalkNWashSplineSprayer` | Spray uses a nozzle transform, with its own start/stop behavior | Move tool/nozzle with right controller; sponge ray replacement alone does not cover sprayer |

The inspected code demonstrably aligns the whole hand/sponge to surface normals.
It does not demonstrate independent finger collision/wrapping. Asset strings show
`plapper_idle`, `plapper_smack`, `plapper_smack_idle`, and finger-related skeleton
paths elsewhere in the assets, but do not establish which bones belong to the
visible first-person hand. Preserve the existing animator/material setup until
a live hierarchy inspection identifies how the observed wrapping is produced.

`RalivIK.dll` contains humanoid arm/hand targets. That alone does not establish
that the visible first-person hand uses this solver, or that a reusable right-hand
mesh exists. A visible right hand needs an asset/rig audit; right-controller tool
control can ship independently.

## Tracking and coordinate mapping

Current [Backend](../src/Backend.cs) exposes only the right-hand dialogue aim.
[OpenVR input](../src/OpenVrInput.cs) resolves controller role 2 from the mod's
cached tracked poses; extend this to roles 1 and 2. The mod already collects an
array of device poses. Existing startup logs confirm OpenVR is the user's active
backend, but do not establish finger-tracking capability.

[OpenXR input](../src/OpenXrInput.cs) creates only a right aim space. Add left/right
grip pose actions for attachment and left/right aim poses for assisted reach.
Keep the right dialogue ray using its existing aim semantics. OpenXR provides
separate grip and aim paths; grip is used for hand-held object placement in the
[Khronos interaction guide](https://www.khronos.org/developers/linkto/interaction-in-openxr).
For legacy OpenVR, inspect the controller model/runtime pose convention and
calibrate palm/nozzle offsets rather than assuming its pose is identical to
OpenXR aim or grip.

Create all additional OpenXR actions before the companion attaches its action
set. Add no second session. Missing optional finger inputs must not invalidate
otherwise working controller profiles.

Use one shared hand-frame calculation for visuals, raycasts, prompts, contact
events, and optional haptics. Include body turn, F10 recenter, world scale, and
the existing crouch origin correction. Start with the same rig transform as the
headset; any later arm-length scaling must apply consistently to both contact
and visuals. Avoid introducing separate height correction twice.

The current companion schedules native poses and rig anchoring late, while game
hand logic runs from `ToolTest.LateUpdate`. A prototype must establish explicit
pose/update ordering. Cached simulation poses are acceptable initially; render
poses may improve visual latency later, but must not duplicate gameplay callbacks
or leave the displayed contact point detached from its simulated surface.

## Preserve contact while replacing aim

Introduce a per-hand state: tracked idle frame, active aim, target collider/point/
normal, contact validity, and activation/release state. Use the actual visual
parent transform when converting world targets to local coordinates; verify the
prefab hierarchy instead of assuming that parent equals the camera.

Patch the hand/sponge target calculation narrowly, then retain their native
animation and effect work. `UpdatePlapper` also reconstructs world effect points
using `LookController.GetLookMatrix`; it needs the same hand frame as the visible
mesh. A target-only patch that misses this reconstruction can still slap one
location while showing another. Scoped call replacements or a helper before
native contact update are preferable to changing the global camera/look state.

On contact, keep surface-normal alignment. Preserve controller twist by projecting
a calibrated wrist axis onto the contact plane, with a stable fallback near
degenerate angles. The game's extra automatic hand roll may need blending to
avoid fighting that twist. Do not apply unconstrained controller rotation after
surface alignment, which would push the palm or sponge into the surface.

Inactive updates currently still call `UpdatePlapper`/`UpdateSponge`; their effect
gate checks distance to a cached target and `hitCollider`. Explicitly clear/gate
contact on release before showing a freely tracked idle hand. Otherwise an idle
hand passing near a stale target could trigger unwanted contact work.

Keep original eligibility checks, contact masks, and body-relative reach limits.
Moving the ray origin to the controller must not silently add physical arm length
on top of unlimited virtual reach. Near-contact probes need swept motion to avoid
skipping thin surfaces and an occlusion check to avoid touching through walls.

Both hand implementations write the same `LocomotionController.IsInteracting`
boolean on start/stop. Releasing one can clear it while the other remains held.
Track aggregate active state during hand integration so interaction movement speed
and other game behavior remain consistent when using both hands together.

## Fingers and extra immersion

First, inspect the visible hand's bones, blendshapes, animator and deformation
materials in a live scene. If suitable controls exist, animate an idle open-hand
pose toward a grip pose using squeeze and index-trigger values; button touch can
drive thumb/index gestures where available. These are inferred poses, not measured
finger motion. While active, blend back to the original contact pose so finger
animation does not defeat the existing surface behavior.

Full finger tracking is a later optional input provider. OpenXR exposes hand joints
through [XR_EXT_hand_tracking](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XR_EXT_hand_tracking.html),
and the application must check
[system support](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrSystemHandTrackingPropertiesEXT.html).
The locally inspected UnityVRMod OpenXR initialization enables only
`XR_KHR_D3D11_enable`. Hand tracking therefore requires extending instance
initialization, not merely querying joints after the companion attaches input.

Current upstream [xrizer skeletal input](https://github.com/Supreeeme/xrizer/blob/main/src/input/skeletal.rs)
also has estimated skeletons from controller inputs. This is a potential OpenVR
route, but the installed xrizer version and controller/runtime capabilities must
be checked before promising real joint tracking. The companion currently uses
legacy controller state, not skeletal actions. WiVRn working with controller poses
does not by itself prove fingers are available through the whole stack.

After contact works, add short haptic pulses on initial touch/slap and subtle
movement-dependent feedback while rubbing. Trigger pressure could blend reach or
visual compression while keeping the existing activation threshold. Keep feedback
rate-limited and optional. Direct sponge dunking and controller-operated object
pickup are later interaction improvements, each preserving the game's resources
and eligibility rules.

## Implementation order and acceptance

1. Audit live hand/tool hierarchy; add both controller poses and debug contact
   targets. No effects in idle state. Validate OpenVR first, then OpenXR.
2. Replace active left-hand and sponge aiming; preserve original extension,
   surface alignment, animations, callbacks, release and tool supply behavior.
   Synchronize controller-selected objects, prompts and click-time validation.
3. Add freely tracked idle presentation and controlled wrist twist. Verify model
   replacements, crouch/recenter transforms, and independent two-hand state.
4. Tune near-contact versus assisted extension, then add nozzle-based tools and
   contact haptics. Finger posing follows the asset audit; real tracking is optional.

Acceptance: moving a controller sideways changes contact with head still; turning
the head alone does not steer a held hand; each hand remains independent; no idle
effects; released or lost tracking cannot leave rubbing/spraying active; visible
contact and game effects coincide; buttons fire once; tool equip/refill still
works; walking/turning/crouching/F10 preserve alignment; both eyes share one
gameplay contact update; dialogue keeps its right-controller pointer.

The next deliverable should be a small left-hand-plus-sponge prototype, with
fingers and a new visible right-hand model deferred until its contact behavior
passes headset testing.
