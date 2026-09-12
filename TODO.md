# Focused task queue

Keep one implementation task active. Commit completed changes separately.
An implemented feature is not hardware-verified until the user tests it.

## Current focus: validate v0.5.3 hand distance and roll

Implemented: both controller poses; left hand/sponge contact and matching object
selection; idle tracking; surface-compatible wrist twist; close-contact probes;
contact haptics; idle finger curl; equipped tool/nozzle tracking; direct sponge dunking. Original contact animations/effects
remain. Original passive ordinary/jiggle contact stays enabled while tracked,
including idle. Lost tracking clears that hand's action/contact; both hands retain independent state.

Asset audit: the first-person `plapper_L` has four finger chains and a separate
jiggle collider, enabled in all original serialized hand instances. See
[design and audit](docs/CONTROLLER_HANDS.md). Optical finger tracking is optional
future work; current curl is inferred from controller inputs.

Validation: 243 offline checks, C haptic ABI checks, and 110 checks inside Unity
passed. Tests exercise actual hand/sponge contact callbacks, target coordinates,
supply consumption, native fluid-emission calls, inactive/release behavior, object
selection eligibility, independent tracking loss and the existing controls.
User confirmed v0.5.0 hand position tracking and trigger-driven finger movement.
Reported faults: reversed curl, unwanted empty right hand, and palm/finger axes
misaligned. v0.5.1 reverses curl, removes the empty right mesh, and adds a left-only
mesh rotation correction. Passive contact is restored; idle gameplay targets still
clear to prevent stale rub/wash effects. Remaining headset checks follow.

v0.5.3 pulls both hand/tool origins back 15 cm in the tracking frame (scaled with
the VR rig), including their gameplay targets. Feet/F10 calibration is unchanged.
Left hand rolls 15 degrees right around its finger axis. Adjust `[Hands] Hand
Pullback` and `Left Hand Rotation Offset` if needed. Build verified; headset check pending.

## Hand headset checks, in order

- [x] User confirmed hand positions follow controllers and trigger moves fingers.
- [x] User confirmed palm direction, inward finger curl, and no empty right hand.
- [x] User confirmed F10 feet position.
- [ ] v0.5.3: hands/tools close enough, left hand roll comfortable, feet remain correct.
- [ ] Active surface animation still works; equipped right tool appears correctly.
- [ ] Idle left hand retains original passive jiggle contact. Tracking loss disables it.
- [ ] Hold left trigger: aim and rub with controller while looking elsewhere.
  Surface wrapping/slap animation remains; no stale rub/wash effects on release.
- [ ] Hold right trigger with sponge: scrub using controller motion. Visible
  contact matches washing; supply drops. Wrist twist stays aligned with surface.
- [ ] Left controller selects prompts/objects; buttons activate once. Right
  controller still selects dialogue answers.
- [ ] Hold both triggers, release one: the other remains active and movement works.
  Losing one controller stops only its contact; release before reactivation.
- [ ] Equip/unequip tools: no floating old model. Sprayer nozzle follows right
  controller. Tune per-tool grip offsets if needed after reporting a mismatch.
- [ ] Direct sponge dunk refills at a valid target, consumes bucket water, and
  does not repeat when sponge is full. Existing left-trigger refill still works.
- [ ] Contact haptics feel useful; near-contact and assisted reach feel natural.
- [ ] Walk, turn, crouch, F10, F11, dialogue, and pause: hands stay aligned and
  no stale contact/rendering survives. Repeat on OpenXR if using that backend.

## Previous build: v0.4.1 validation

Implemented: height-based crouch (enter 75%, exit 85%), proportional vertical
mapping to kobold height, left X forced crouch/stand, F10 reset to automatic mode.
Native capsule/headroom handling remains active; physical and game camera drops
do not stack. Removed the companion's hand-use movement block; native slower
interaction movement remains. Height failures are isolated from other controls.

Validation: 240 offline checks and 55 checks inside Unity passed. New runtime
checks exercise the production movement/posture hook, active hands, disabled look
and move actions, cutscenes, focus loss, cached headset height, X and jump priority.
User confirmed X override, F10 reset, movement while using hands, Y HUD toggle,
HUD placement, compact dialogue, and progress values. Automatic crouch thresholds
and headroom behavior still need user validation.

v0.4.1 raises full-stick smooth turn speed from 90 to 120 degrees/second.

## Next: headset checks, in order

- [ ] Faster full-stick turn speed (120 degrees/second) feels comfortable.

- [ ] Stand or sit comfortably upright and press F10. Lower head: crouch below
  75%; rise above 85%: stand. Eye should not drop twice when crouch activates.
- [ ] Edge cases: X hold must not flicker; Y/right A must remain separate.
- [ ] Try standing under low headroom: native collider must remain blocked.
  Check jump, tracking/focus loss, F11, and seated recalibration.
- [ ] Movement edge cases: both triggers together; pause/dialogue must still block.

- [ ] HUD toggle edge cases: held Y must not flicker; other buttons must not
  toggle it. Visibility survives restart.

- [ ] Dialogue edge cases: long lines and wrapped answers remain readable;
  pagination still works.
- [ ] Left trigger uses left hand/left click (objects, petting, buttons).
  Right trigger uses right hand/right click (equipped tools, sponge).
  Interaction hint says Left trigger; dialogue laser selection stays right trigger.
- [ ] Progress bars appear upper-left while washing and stay there when looking
  around. Tune HUD Scale/Horizontal Offset/Vertical Offset if needed.
- [ ] Sponge supply edge cases: refill changes supply and unequipping hides it.
- [ ] Optional task bars match desktop visibility; no stale bars across scenes,
  pause, or F11 toggles. HUD appears in both eyes and stays out of desktop view.
- [ ] Gameplay movement, camera follow, headset aim, jump, and F10 still work.

Fix the first failing check before expanding scope. Record backend and relevant
`BepInEx/LogOutput.log` messages with each failure.

## Backlog: not started

- [ ] Inventory other missing UI (inventory, menus); choose one concrete screen next.
- [ ] General VR menu interaction.
- [ ] Optional optical/skeletal finger tracking: capability probe, runtime instance
  extension support, per-bone retargeting, and contact-pose blending.
- [ ] Hand comfort tuning after headset feedback: model offsets, curl direction,
  haptic strength, tracking latency, and optional purpose-made right-hand mesh.
- [ ] Stick-based dialogue selection only if controller pointing proves unsuitable.

## Confirmed by user

- [x] v0.4.0 left X crouch override and F10 reset.
- [x] Movement while using hands.
- [x] Left Y HUD toggle and adjusted HUD placement.
- [x] Compact dialogue boxes.
- [x] Progress values.

- [x] v0.3.0 dialogue text, continuing, and answer selection work.

- [x] Native VR startup through Proton/WiVRn.
- [x] Left stick moves the character; original right stick snap-turns.
- [x] Camera follows character; headset rotation drives look; F10 recenters.
- [x] v0.2.2 restores controls after v0.2.0/v0.2.1 startup regression.
- [x] Right A jumps.
- [x] Controller interaction works with objects.
- [x] Interaction text appears in headset (previous size was too large).

Known working pre-regression backup:
`BepInEx/companion-backups/20260912-011800-158473227/` (v0.1.0).
Later installs preserve their previous DLL in timestamped backup directories.
