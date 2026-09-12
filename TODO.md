# Focused task queue

Keep one implementation task active. Commit completed changes separately.
An implemented feature is not hardware-verified until the user tests it.

## Current focus: compact dialogue, hand mapping, progress HUD

Implemented: half-size interaction hint, corrected OpenVR index-trigger mapping,
and a dialogue panel controlled by pointing the right controller and pressing its
index trigger. No stick-navigation fallback yet; test the preferred pointer first.

Validation: 174 offline checks, independent OpenXR header ABI check, and 31 checks
inside the actual Unity player passed. The runtime checks include rendered sample
panels, ray hits after rig rotation/scaling, disabled answers, pagination, recenter,
and Yarn's reveal/advance/answer callbacks. Native pointing and the actual game's
full dialogue flow still need headset testing.

## Next: headset checks, in order

- [ ] Interaction hint is half its previous size and still readable.
- [ ] Index triggers (back buttons under index fingers) perform clicks; side grips
  do not. Right trigger interacts, left trigger performs the secondary action.
- [ ] Dialogue text/speaker and all answer choices appear in both eyes.
- [ ] Right-controller laser points comfortably; aimed answer highlights.
  If needed, tune `Pointer Pitch Offset` before changing the input design.
- [ ] Trigger on the line panel reveals text, then a fresh press advances it.
  The trigger used to open dialogue must not skip its first line.
- [ ] Trigger on an answer chooses that answer; disabled answers cannot be chosen.
  Test Previous/Next page buttons if a dialogue has more than four choices.
- [ ] Dialogue trigger never activates the character's hands or another object.
  Holding trigger, leaving dialogue, losing tracking/focus, or toggling F11 must
  not produce extra clicks; release before pressing again.
- [ ] F10 puts the dialogue panel in front again. Looking around otherwise leaves
  it anchored rather than attached to head rotation.
- [ ] Gameplay movement, camera follow, headset aim, jump, and smooth turn still work.
  Background auto-advancing dialogue must not block movement.

Fix the first failing check before expanding scope. Record backend and relevant
`BepInEx/LogOutput.log` messages with each failure.

## Backlog: not started

- [ ] Inventory other missing UI (progress bars, inventory, menus); choose one
  concrete screen to support next.
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

- [x] User confirmed v0.3.0 dialogue text, continuing, and answer selection work.
