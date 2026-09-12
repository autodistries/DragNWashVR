# Focused task queue

Keep one implementation task active. Commit completed changes separately.
An implemented feature is not hardware-verified until the user tests it.

## Current focus: validate v0.4.0 crouch and hand-use movement

Implemented: height-based crouch (enter 75%, exit 85%), proportional vertical
mapping to kobold height, left X forced crouch/stand, F10 reset to automatic mode.
Native capsule/headroom handling remains active; physical and game camera drops
do not stack. Removed the companion's hand-use movement block; native slower
interaction movement remains. Height failures are isolated from other controls.

Validation: 240 offline checks and 55 checks inside Unity passed. New runtime
checks exercise the production movement/posture hook, active hands, disabled look
and move actions, cutscenes, focus loss, cached headset height, X and jump priority.
Headset comfort and actual crouch thresholds still need user validation.

## Next: headset checks, in order

- [ ] Stand or sit comfortably upright and press F10. Lower head: crouch below
  75%; rise above 85%: stand. Eye should not drop twice when crouch activates.
- [ ] Left X forces opposite posture; holding does not flicker; another press
  toggles back. F10 restores automatic height mode. Y/right A remain separate.
- [ ] Try standing under low headroom: native collider must remain blocked.
  Check jump, tracking/focus loss, F11, and seated recalibration.
- [ ] Move with left stick while holding left trigger, right trigger, and both.
  Game's slower interaction speed is expected; pause/dialogue must still block.

- [ ] Left Y hides HUD; release/press shows it again. Holding Y must not flicker.
  X, right B, triggers, and grips must not toggle it. Visibility survives restart.
- [ ] HUD sits further right and slightly lower; still stays fixed in the view.

- [ ] Dialogue boxes use less height; short lines have no large blank area.
  Long lines and wrapped answers remain readable; clicking/pagination still works.
- [ ] Left trigger uses left hand/left click (objects, petting, buttons).
  Right trigger uses right hand/right click (equipped tools, sponge).
  Interaction hint says Left trigger; dialogue laser selection stays right trigger.
- [ ] Progress bars appear upper-left while washing and stay there when looking
  around. Tune HUD Scale/Horizontal Offset/Vertical Offset if needed.
- [ ] Values match desktop bars. Soap coverage and Sponge supply are distinct;
  sponge supply changes as it is used/refilled and disappears when unequipped.
- [ ] Optional task bars match desktop visibility; no stale bars across scenes,
  pause, or F11 toggles. HUD appears in both eyes and stays out of desktop view.
- [ ] Gameplay movement, camera follow, headset aim, jump, and F10 still work.

Fix the first failing check before expanding scope. Record backend and relevant
`BepInEx/LogOutput.log` messages with each failure.

## Backlog: not started

- [ ] Inventory other missing UI (inventory, menus); choose one concrete screen next.
- [ ] General VR menu interaction.
- [ ] Controller-driven hands/tool aiming (deferred until current UI work passes).
  Left hand follows left controller; right hand/item follows right controller.
  Preserve idle hands near body with interaction disabled. Left trigger activates
  left-hand touch/pet/button interaction; right trigger activates held tools.
  Replace camera-directed contact rays as well as visible hand poses, so washing
  requires controller movement rather than head movement. Preserve reach limits,
  collisions, release behavior, and sponge refill/contact mechanics.
- [ ] Stick-based dialogue selection only if controller pointing proves unsuitable.

## Confirmed by user

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
