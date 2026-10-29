# PeakXR

Play PEAK in a VR headset. You climb by grabbing the rock with your own hands, pick items up, eat by lifting food to your mouth and pull yourself along ropes hand over hand. Friends who don't have the mod can still play with you.

This is an early release. Almost all testing so far has been on SteamVR, mostly on Linux, so expect bugs and please report them.

## Features

- Head tracking drives the camera, and your tracked hands are your character's arms.
- Climb walls, ropes, vines and pitons by gripping them. Grab, eat, throw and use items with your hands. Each action also has a button fallback.
- Menus and the HUD are panels in the world. The title menu puts you on the beach, and head tracking keeps running on loading screens.
- A VR tab in the game's settings menu holds every PeakXR setting.
- Comfort options: snap or smooth turning, a vignette, fades in place of camera shake, seated mode and room-scale walking. The defaults lean toward comfort.
- Multiplayer works with unmodded players. They see your head direction and basic reaching. Players who also run PeakXR, in VR or flat, see your full head, hands and fingers.

## Install

1. Install PeakXR with r2modman or the Thunderstore Mod Manager. It pulls in BepInExPack_PEAK for you.
2. Set SteamVR as your OpenXR runtime (SteamVR settings, OpenXR, "Set SteamVR as OpenXR runtime"). Other OpenXR runtimes have their own switch.
3. Set the launch options below.
4. Start SteamVR, put the headset on, then press "Start modded" in the mod manager.

### Launch options

Windows: PEAK starts on Direct3D12, which usually runs VR slower. Add `-force-d3d11` to the launch arguments in your mod manager's settings, or to PEAK's launch options in Steam (Properties, General, Launch options).

Linux (Proton), in PEAK's launch options in Steam:

AMD or Intel GPU:

```
WINEDLLOVERRIDES="winhttp=n,b" %command% -force-vulkan
```

NVIDIA GPU:

```
PROTON_ENABLE_WAYLAND=0 WINEDLLOVERRIDES="winhttp=n,b" %command% -force-d3d11
```

The DLL override lets BepInEx load under Proton. On NVIDIA, Vulkan VR crashes in Proton's OpenXR bridge, and Proton's Wayland driver stops Direct3D from starting VR, so these options use Direct3D11 on X11. On a laptop with two GPUs, SteamVR and PEAK have to run on the GPU that drives the headset.

### Check that it works

You should stand on the beach with the title menu in front of you. If the game starts flat, look in `BepInEx/LogOutput.log`: `[xr.bootstrap] VR active` means VR started, and `[xr.bootstrap] No XR runtime started` means the runtime was not running or is not set as the OpenXR runtime. To play flat without uninstalling, launch with `-peakxr-novr`.

## Controls

Default layout for a right-handed player. Set `Input.DominantHand = Left` to swap the controllers.

- Left stick moves, click to sprint. Right stick turns left and right and changes items up and down, click to crouch.
- Grip grabs or interacts. Trigger uses the item in that hand.
- Right lower button (A) jumps. Right upper button (B) drops the item, hold it to charge a throw.
- Left lower button (X or A) puts the item away, hold it for the backpack. Left upper button (Y or B) pings, hold it for the emote wheel.
- Hold both stick clicks for one second to recenter and recalibrate your eye height. Where the menu button reaches the game, a tap pauses and a one-second hold recenters.
- To talk with Push To Talk, hold a free hand at your mouth like a walkie-talkie.

The full table, the item and emote wheels, the passport and notes for Index, Vive and WMR controllers are in [docs/controls.md](https://github.com/ThePhaseless/PeakXR/blob/master/docs/controls.md).

## Known limitations

- Only SteamVR has been tested. Other OpenXR runtimes and controllers may not work.
- OpenXR under Proton is new and may not work on every setup.
- The on-screen keyboard has no symbol layer.
- A game update can break a feature. PeakXR turns off only the broken feature and keeps running. The compat list in the VR tab shows which ones are off.

## Reporting bugs

Open an issue at https://github.com/ThePhaseless/PeakXR/issues.

Save a bug report in the game right after the problem: hold both stick clicks for about 2.5 seconds (you recenter at 1 second, keep holding), or press "Save bug report" under Debug in the VR tab. Each report is a folder in `BepInEx/PeakXR-reports/`. Zip that folder and attach it to the issue with a sentence or two on what you did, what you expected and what happened. If the game crashed, attach `BepInEx/LogOutput.log` as well. Reports stay on your computer until you send them, and player names and Steam IDs are blanked, but check `screenshot.png` for names above other players before you share it.

More in [docs/debugging.md](https://github.com/ThePhaseless/PeakXR/blob/master/docs/debugging.md).

## License

MIT. PeakXR is written from scratch and contains no code from other PEAK or Unity VR mods. The XR runtime files come from Unity's packages (Unity Companion License) and Valve's unity-xr-plugin (BSD-3-Clause).

Source: https://github.com/ThePhaseless/PeakXR
