# Walk N Wash VR Companion

A BepInEx 6 Mono companion for Drag N Wash / Walk N Wash and
[UnityVRMod](https://github.com/NewUnityModder/UnityVRMod). This is an experimental
game-specific integration, targeting the local Unity 6000.3.14f1 build and
UnityVRMod 0.1.0-beta. It supports that mod's OpenXR and OpenVR variants.

UnityVRMod creates a VR rig at the original camera's position but does not move
that rig with the player. This plugin anchors the rig to the game's
`LookController.GetLookPosition()` each frame, preserving physical headset
translation and rotation. Rendering is deferred to late update, after the game
updates its player and eye anchor.

Current work and pending headset checks: [TODO.md](TODO.md). Keep one task active.

## Features and controls

- First-person camera follows the player, with a calibrated physical head origin.
- Headset direction drives character look/aim. Horizontal mouse or gamepad look
  turns the tracking origin; vertical mouse look is ignored in this mode.
- Left VR thumbstick moves relative to headset heading.
- Right VR thumbstick turns continuously, up to 90 degrees per second. Set
  `Smooth Turning = false` to use 30-degree snap turns instead.
- **F10** recenters: your current physical head position becomes the character's
  eye position. Use it while sitting or standing comfortably.
- **F11** remains UnityVRMod's VR/safe-mode toggle.
- **Right controller A** jumps using `Player.Jump`, the same action as desktop
  Space. Press, hold, and release follow the game's normal jump behavior.
- **Left trigger** performs the game's left-click action: interact with an object,
  pet, or hold to reach with the left hand. **Right trigger** performs its right-click
  action, using the right hand and equipped tool. Dialogue selection still uses
  the right controller and right trigger.
- A world-space **Left trigger / Interact** hint appears above the game's selected
  interactable in VR. Hand-based targets say **Use hand**. The desktop hint remains.
- Dialogue text and answer choices appear on a panel in front of you. **Aim the
  right controller and press its index trigger** to reveal/advance a line or select
  the highlighted answer. Release between presses. Longer answer lists have
  clickable **Previous / Next** page controls. Panel height fits its text and visible
  answers; short lines no longer reserve a large empty box. **F10** places the panel in front again.
- A compact upper-left HUD follows headset orientation and shows the visible game
  progress bars plus **Sponge supply** when the sponge is equipped.
- Keyboard movement, gamepad movement, and existing interaction keys remain
  available. These trigger bindings operate the game's existing hand/tool animations;
  free-moving hands and general VR menu clicking are not implemented.

Controller movement respects the game's interaction lock. Headset aiming and
trigger release remain active while using a hand/tool. Inputs respect disabled
game actions, pause state, cutscenes, and VR focus. The headset view still follows
the player during cutscenes; this build does not reproduce cinematic camera paths.
Interactive dialogue captures VR gameplay input; automatic background dialogue
does not block movement. Physical leaning moves the viewpoint, not the game's collision capsule, so leaning
through walls remains possible. Player meshes are not hidden automatically.

## Build and install

Place this repository directly inside the game directory. Prerequisites are a
.NET SDK supporting .NET Standard 2.1, the installed game, BepInEx 6 Mono, and one
UnityVRMod backend. No NuGet packages or downloads are used.

```bash
bash build.sh
bash install.sh
```

For another game installation, set `GAME_DIR`:

```bash
GAME_DIR=/path/to/walknwash-windows-64 bash install.sh
```

The installed file is:

```text
BepInEx/plugins/WalkNWashVRCompanion/WalkNWash.VRCompanion.dll
```

Installation backs up any previous companion DLL outside the plugin scan
directory. Game assemblies, UnityVRMod binaries, build outputs, and local logs
are excluded from this Git repository. This plugin does not replace UnityVRMod.

## Launch and first test

Connect the headset to WiVRn before launching. In the existing Fish shell with
the `protonize` alias, launch from the game directory:

```fish
SteamGameId=0 \
VR_OVERRIDE=/opt/xrizer \
XR_RUNTIME_JSON=/usr/share/openxr/1/openxr_wivrn.json \
WINEDLLOVERRIDES=winhttp=n,b \
protonize --prefix yiff DragNWash.exe -force-d3d11
```

1. Restart the game after installation; reach gameplay before pressing F11.
2. Press F10 while looking forward in a comfortable posture.
3. Walk using the keyboard. The viewpoint should follow the character's eyes.
4. Turn and lean physically. The viewpoint should rotate and translate; character
   aim should follow headset direction during normal gameplay.
5. Test left-stick movement and continuous right-stick turning, then release both sticks.
6. Look at an interactable: a **Left trigger** label should appear in the headset.
   Squeeze the left trigger. Also hold/release it away
   from an interactable to test the existing hand action. Test the right trigger's
   tool action. Release triggers before re-enabling VR or returning from a menu.
7. Press/hold/release right A to test jumping, then release and press again.
8. Open a menu and switch VR off/on. Input should stop appropriately, and a rebuilt
   rig should recalibrate when rendering resumes.

The initial camera follow, headset aim, movement, snap turn, and recentering were
confirmed working by the user in v0.1.0. The user reported that v0.2.0 and v0.2.1
disabled all companion controls at startup. Version 0.2.2 fixes that input-device
initialization failure and passes headless Unity device/action checks.
The user subsequently confirmed recovery, jumping,
object interaction, and visible interaction text. Version 0.3.0 halves that text's
size, corrects OpenVR trigger/grip confusion, and adds controller-pointed dialogue;
the user subsequently confirmed dialogue rendering, continuing, and selecting answers.
Version 0.3.1 fits panel height to its content, matches gameplay triggers to hands,
and adds the progress HUD. These newest changes still need headset validation.
Automated checks do not prove that a given game renderer or controller
profile works correctly at runtime. In particular, if the world stays attached
to the headset despite physical head rotation, that is a separate pose/rendering
issue; camera follow alone will not fix it.

## Configuration

First launch generates `BepInEx/config/local.walknwash.vrcompanion.cfg`.
Edit it while the game is closed.

| Setting | Default | Purpose |
| --- | --- | --- |
| Enabled | true | Enable the companion |
| Headset Aims Character | true | Connect headset orientation to game aim |
| Eye Height Offset | 0 | Additional game-world vertical offset |
| Recenter Key | F10 | Recalibrate current physical head position |
| Enable Controllers | true | Enable VR thumbstick bindings |
| Mouse Turns Body | true | Allow horizontal desktop look to turn the rig |
| Stick Deadzone | 0.2 | Filter thumbstick drift |
| Smooth Turning | true | Continuous turning; disable for snap turns |
| Smooth Turn Speed | 90 | Degrees per second at full stick deflection |
| Snap Turn Degrees | 30 | Turn angle per right-stick deflection |
| Show Interaction Prompt | true | World-space interaction label in VR |
| Show Dialogue | true | World-space dialogue and answer panel |
| Dialogue Width | 1.2 | Panel width in tracking-space meters |
| Dialogue Distance | 1.6 | Initial distance from headset in tracking-space meters |
| Pointer Pitch Offset | 0 | Adjust controller ray pitch in degrees if needed |
| Show Progress HUD | true | Head-relative progress bars and equipped sponge supply |
| HUD Scale | 1 | HUD size multiplier |
| HUD Horizontal Offset | -0.65 | Upper-left edge horizontal position in head space |
| HUD Vertical Offset | 0.48 | Upper-left edge vertical position in head space |

Use this plugin's eye-height offset instead of UnityVRMod's eye-height/scene-pose
offsets during gameplay: the companion controls the rig's position. UnityVRMod's
world scale remains effective. Choosing a camera override does not select the
player anchor; the game's own look controller supplies that anchor.

## Input implementation

OpenXR attaches movement, turn, and trigger actions to UnityVRMod's existing session,
then syncs them while focused. Suggested profiles are Oculus Touch, Valve
Index, Microsoft motion controllers, and Vive trackpads. Runtime profile emulation
may support other controllers. Attachment failure is logged and leaves camera
follow enabled. No second OpenXR instance or session is created.

OpenVR uses UnityVRMod's existing `CVRSystem` and legacy controller state, selecting
the joystick axis by its device property and falling back to axis zero for
trackpads. The index trigger reads **Axis1**, with button 33 as the digital fallback.
It does not choose an arbitrary axis by its `Trigger` type: xrizer also exposes
the grip/squeeze value as a one-dimensional Axis2 (see
[xrizer's legacy mapping](https://github.com/Supreeeme/xrizer/blob/main/src/input/legacy.rs)). OpenXR explicitly binds
`/input/trigger/value`, not squeeze. This depends on the runtime's legacy input
emulation, including xrizer's.

The game has Unity Input System movement bindings, but the mod's native VR session
does not provide Unity XR devices. The companion supplies movement directly to the
existing `LocomotionController.MoveInput` while retaining the normal keyboard path.
Triggers feed a custom Unity Input System device bound to `Player.Plap` and
`Player.Attack`. This preserves the game's normal performed/canceled callbacks and
held-button behavior without generating desktop mouse events. Triggers use separate
press/release thresholds and must return to neutral after focus or VR is lost.
Device creation is deferred until one frame after Unity calls `Start`, because
BepInEx `Awake` runs before the input layout globals are ready in this game.
Device creation and button-update failures are isolated from camera/stick hooks.

Right A uses a third button on that device bound to `Player.Jump`. OpenXR binds
`/user/hand/right/input/a/click` for Oculus Touch and Valve Index profiles; existing
Vive/Microsoft motion-controller profiles do not get an unsupported A binding.
OpenVR reads `k_EButton_A` from the right controller's pressed-button mask. Runtime
legacy input emulation must expose that button. No alternative jump button is
assigned to controllers without A. Like triggers, A must be released after resuming
VR/focus or re-enabling the jump action before another press is accepted.

## Interaction UI

The original `UiPrompt` uses `Camera.main.WorldToScreenPoint` and desktop pixel
coordinates. Those screen overlays do not appear in the mod's eye render textures.
The companion mirrors its show/hide signals into a separate world-space label,
displayed only during the VR eye render passes. The label tracks the same target
the game selects; it does not make out-of-range objects interactable.

The dialogue panel separately mirrors the active Yarn line presenter and its
actual option items, including speaker, revealed characters, and unavailable
answers. It stays anchored to the tracking origin while you look around. A right
controller ray highlights an answer; the trigger queues one activation for normal
Update, outside the eye render passes. The handler rechecks that the dialogue and
option are still current before using Yarn's original selection method. Line
advance uses the same handler as desktop input: reveal the typewriter first,
then advance. Opening dialogue or losing tracking/focus requires trigger release
before a new click. Dialogue clicks do not synthesize desktop mouse events.

OpenXR uses the right-hand `aim/pose` action in the mod's reference space and
predicted frame time. OpenVR uses the right controller's tracked pose from the
mod's existing compositor frame; adjust `Pointer Pitch Offset` if its forward
direction differs from the comfortable pointing angle for your controller.

The progress HUD reads the game's visible `UiProgressBar` values and respects
hidden or disabled bars. Washing bars hide outside the washing phase, like the
desktop group. **Soap coverage** is the dragon's soap coverage; **Sponge supply**
is the equipped sponge's remaining fill, not that same percentage. Optional
objective and dialogue-readiness bars appear when their desktop bars appear.
The HUD occupies a single stereo surface at 1.2 tracking-space meters and follows
head position and rotation to stay in the upper-left of the view. Adjust its size
or offsets under `[UI]` in the config if needed.

This does not convert the remaining desktop menus or inventory into VR. Existing desktop mouse and keyboard controls remain available.

## Validation and troubleshooting

```bash
dotnet run --project tests/Checks.csproj -c Release -- ..
cc -std=c11 -Wall -Werror tests/openxr-layout.c -o /tmp/walknwash-openxr-layout
/tmp/walknwash-openxr-layout
```

The C# checks require .NET 10 and use the local BepInEx copy of Mono.Cecil. They
test calibration, movement deadzones, snap-turn release behavior, x64 OpenXR
structure layouts, and hook signatures in the actual game and available mod DLLs
(including the local OpenVR/OpenXR ZIP archives). The optional C check validates
the same structure layouts against installed Khronos OpenXR headers.

The opt-in runtime tests exercise the actual startup input device, trigger/jump
callbacks, transformed controller rays, answer pagination, disabled answers,
panel recentering, and Yarn's reveal/advance/selection handlers. They can also
render sample panels to `dist/dialogue-options.png` and `dist/dialogue-line.png`, plus `dist/progress-hud.png`.
Close the game first, then use the same Wine/Proton environment as your normal
launcher, passing its executable and subcommand to the wrapper. For this install:

```bash
env STEAM_COMPAT_DATA_PATH=/home/cat/.prefixes/yiff \
    STEAM_COMPAT_CLIENT_INSTALL_PATH=/home/cat/.local/share/Steam \
    SteamGameId=0 WINEDLLOVERRIDES=winhttp=n,b \
    bash tests/run-runtime-smoke.sh \
    '/home/cat/.local/share/Steam/steamapps/common/Proton - Experimental/proton' run
```

The wrapper saves/restores the installed DLL and original BepInEx log, retains test
logs under `dist/runtime-*`, and refuses to run alongside the game. It launches a
bounded batch-mode check, which logs `RUNTIME SMOKE PASS` or `RUNTIME SMOKE FAIL`
and exits. Set `SMOKE_NOGRAPHICS=1` to skip image capture. Normal builds exclude
the test code entirely. These checks do not prove native headset/controller
tracking or in-headset readability.

Read `BepInEx/LogOutput.log` for `Walk N Wash VR Companion`:

- `Companion ready`: hooks installed.
- `VR action buttons ready`: deferred Unity device creation succeeded.
- `VR action buttons unavailable` or `Button input stopped`: trigger/jump input
  failed; camera, F10, and native stick controls remain active. Save the exception.
- `thumbstick input attached`: native input initialization succeeded.
- `Camera calibrated to player eyes`: player anchor and valid head pose found.
- `Controller input unavailable`: camera follow remains enabled; see the following
  native error and test with keyboard movement.
- `Companion stopped after error`: integration disabled itself; original mod
  rendering resumes. Save this error before relaunching.

To uninstall, move the `WalkNWashVRCompanion` directory outside `BepInEx/plugins`
while the game is closed. No game files or original mod DLLs are patched on disk.

References: [OpenXR action lifecycle](https://registry.khronos.org/OpenXR/specs/1.1/html/xrspec.html#input-action-sets),
[UnityVRMod OpenXR renderer](https://github.com/NewUnityModder/UnityVRMod/blob/main/src/Features/VRVisualization/VrCameraSetup_CoreOpenXR.cs),
[UnityVRMod OpenVR renderer](https://github.com/NewUnityModder/UnityVRMod/blob/main/src/Features/VRVisualization/VrCameraSetup_CoreOpenVR.cs).
