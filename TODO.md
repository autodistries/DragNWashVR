# Focused task queue

Keep one implementation task active. Commit completed changes separately. Finish
the pending headset checks before expanding into more UI or tracked-hand work.
An implemented feature is not hardware-verified until the user tests it.

## Current focus

No implementation task active. Version 0.2.1 is built and installed; 143 offline
checks and the independent OpenXR ABI check passed. Next task: run the headset
validation below, then fix the first reported failure before adding features.

## Next: headset validation

Blocked for now: headset battery depleted. No hardware testing available.

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

- [x] Native VR startup through Proton/WiVRn.
- [x] Left stick moves the character.
- [x] Right stick snap-turns by about 30° (original mode).
- [x] Camera follows the character.
- [x] Headset rotation drives character look.
- [x] F10 recenters over the character's feet.
