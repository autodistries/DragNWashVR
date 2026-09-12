# Focused task queue

Keep one implementation task active. Commit completed changes separately. Finish
the pending headset checks before expanding into more UI or tracked-hand work.
An implemented feature is not hardware-verified until the user tests it.

## Current focus

Version 0.2.2 fixes the startup regression reported on 2026-09-12. Versions 0.2.0
and 0.2.1 created a Unity input device too early in BepInEx Awake; the exception
removed every companion hook. Device creation now waits until after Unity startup,
and button failures leave camera, sticks, and F10 active.

Validation: 148 offline checks and 10 headless checks inside the actual Unity player
passed, including the production input callback and trigger/jump action callbacks.
User confirmed v0.2.2 works, including jumping, object interaction, and visible
interaction text. Current task: controller-pointed dialogue panel, line advance,
and answer selection. Keep dialogue failures isolated from working gameplay.

- [x] Halve interaction prompt size.
- [x] Fix OpenVR trigger selection: Axis1 is the index trigger; selecting the last
  one-dimensional axis could read Axis2 squeeze/grip instead. OpenXR already uses
  `/input/trigger/value`.
- [ ] Show dialogue text and choices in VR.
- [ ] Aim right controller at text/answer and use index trigger to activate it.
- [ ] Verify dialogue clicks never trigger gameplay actions or skip two lines.

## Next: headset validation

- [x] Confirm v0.2.2 restores left-stick movement, camera follow, headset aim, and
  F10. Check logs for `Companion ready` and `VR action buttons ready` with no
  companion startup/input errors. User confirmed these controls work in v0.1.0
  backup `BepInEx/companion-backups/20260912-011800-158473227/` and fail in later
  builds; user now confirms v0.2.2 works.

- [ ] Right A jumps; holding behaves like Space; release then press can jump again.
  A held while resuming VR or leaving a menu must not cause an accidental jump.
- [ ] Smooth turning feels correct; tune speed if needed (default 90°/second).
- [ ] Right trigger interacts with a selected object and holds/releases hand use.
- [ ] Left trigger performs the desktop right-click action.
- [ ] Interaction prompt is visible/readable in both eyes and disappears when
  looking away; target matches the actual object being interacted with.
- [ ] No stuck input after pause, focus loss, controller disconnect, or F11 toggle.
- [ ] Camera follow, headset aim, movement, and F10 still work after these changes.

Fix failures from this list before starting the backlog. Record backend and relevant
`BepInEx/LogOutput.log` messages with each failure.

## Backlog: not started

- [ ] Inventory missing UI (dialogue, progress bars, menus) during gameplay; choose
  one concrete screen to support next.
- [ ] Add VR menu interaction after deciding how that menu should appear in VR.
- [ ] Investigate tracked controller hands/tool aiming as a separate feature.

## Implemented, awaiting headset validation

- [x] Right-controller **A → Player.Jump** (same game action as desktop Space),
  with hold/release behavior and focus/menu gating. OpenXR and OpenVR implemented.
- [x] Configurable continuous right-stick turning; snap mode remains optional.
- [x] Trigger bindings through the game's input actions.
- [x] World-space interaction prompt for VR.

## Confirmed working by user

Initial controls confirmed in v0.1.0; v0.2.2 recovery confirmed after the startup fix.

- [x] Native VR startup through Proton/WiVRn.
- [x] Left stick moves the character.
- [x] Right stick snap-turns by about 30° (original mode).
- [x] Camera follows the character.
- [x] Headset rotation drives character look.
- [x] F10 recenters over the character's feet.
- [x] Right A jumps.
- [x] Controller interaction works with objects (trigger/grip mapping correction pending retest).
- [x] Interaction text appears in headset (half-size adjustment pending retest).
