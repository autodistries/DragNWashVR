# DragonWashVR

a mod for Drag'n Wash that lets you play it in VR.

Get it on itch.io ! https://randomcat4.itch.io/dragn-wash-vr-mod

The itch.io version contains more features and bugfixes that will eventually get released here. Packaged downloads, installation instructions, setup help, updates and support all live there. By going to itch, you're also supporting me, which is (imo) pretty based

This repository contains source for v0.8.3.
It includes:
- Direct hand contact (no need to use trigger for hand and sponge to run and scrub~)
- User settings menu (tweak smooth/snap turning, contact haptics, eye height override, left or right handness, dialoges follow view..)
- more comforable moving and rotating (optimized legs placement, smaller hitboxes overall, better crouching, ..)
- cutscenes freecam
- a few crash/softlock fixes


The itch.io build is currently v0.9.1. Compared with this public v0.8.3 source, it additionally includes:
- an owned OpenXR implementation; UnityVRMod is no longer a dependency
- D3D11 and D3D12 rendering paths, with safer VR startup and shutdown. (I still recommend d3d11)
- controller-profile handling for more headsets
- eye-resolution control (trading graphics for fps)

## What to do with this ?

Prerequisites: have a copy of the game (Steam or itch.io), with BepInEx 6 beta
(https://builds.bepinex.dev/projects/bepinex_be).

## Build

Install the .NET SDK. From repository root (because we rely on game code), run:

```sh
dotnet build -c Release -p:GameDir="/path/to/Drag'n Wash" 
```

Copy `bin/Release/netstandard2.1/WalkNWash.VRCompanion.dll` to a new
`BepInEx/plugins/` folder in the game directory.
