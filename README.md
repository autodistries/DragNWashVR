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
- **Right trigger** performs the game's left-click action: interact with an object,
  or hold to reach/use the hand. **Left trigger** performs its right-click action.
- Keyboard movement, gamepad movement, and existing interaction keys remain
  available. These trigger bindings operate the game's existing hand/tool animations;
  tracked controller poses, free-moving hands, and VR menu clicking are not implemented.

Controller movement respects the game's interaction lock. Headset aiming and
trigger release remain active while using a hand/tool. Inputs respect disabled
game actions, pause state, cutscenes, and VR focus. The headset view still follows
the player during cutscenes; this build does not reproduce cinematic camera paths.
Physical leaning moves the viewpoint, not the game's collision capsule, so leaning
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
6. Look at an interactable and squeeze the right trigger. Also hold/release it away
   from an interactable to test the existing hand action. Test the left trigger's
   secondary action. Release triggers before re-enabling VR or returning from a menu.
7. Open a menu and switch VR off/on. Input should stop appropriately, and a rebuilt
   rig should recalibrate when rendering resumes.

Camera follow, visual comfort, and hardware input still require an in-headset
test. Automated checks do not prove that a given game renderer or controller
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
trackpads. This depends on the runtime's legacy input emulation, including xrizer's.

The game has Unity Input System movement bindings, but the mod's native VR session
does not provide Unity XR devices. The companion supplies movement directly to the
existing `LocomotionController.MoveInput` while retaining the normal keyboard path.
Triggers feed a custom Unity Input System device bound to `Player.Plap` and
`Player.Attack`. This preserves the game's normal performed/canceled callbacks and
held-button behavior without generating desktop mouse events. Triggers use separate
press/release thresholds and must return to neutral after focus or VR is lost.

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

Read `BepInEx/LogOutput.log` for `Walk N Wash VR Companion`:

- `Companion ready`: hooks installed.
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
