# Focused task queue

Keep one implementation task active. Commit completed changes separately.
An implemented feature is not hardware-verified until the user tests it.

## Current focus: validate v0.3.2 HUD toggle and placement

Implemented: left Y toggles HUD visibility once per press and saves it. HUD moved
right/down (horizontal -0.5, vertical 0.4). Previous compact dialogue, hand mapping,
and progress values remain implemented. Height-based crouch/X override and
controller-driven hand poses are recorded below for later.

Validation: 203 offline checks and 44 checks inside the actual Unity player
passed. Rendered previews inspected. Tests cover compact panel hit targets,
pagination, Yarn callbacks, left/right action mapping, live desktop bar values,
hidden bars, HUD positioning after head/rig transforms, and stale row removal.
Headset readability and live washing/tool changes still need user validation.

## Next: headset checks, in order

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

- [ ] Height-driven crouch: compare tracked head height to a calibrated standing
  baseline; crouch when lowered and stand when raised. Use separate enter/exit
  thresholds to avoid flicker. Support seated calibration and F10 recentering;
  do not count the game's own crouch camera offset as physical head movement.
  Left-controller X must override automatic crouch. Define a clear way to resume
  automatic mode, and preserve safe posture on tracking loss or blocked headroom.

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
