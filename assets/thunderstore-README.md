# PeakXR

Play PEAK in VR.

Early release. It has only been tested with SteamVR, mostly on Linux, so expect bugs and report them on GitHub.

## What it does

- Play with a headset. Your head drives the camera.
- Your tracked hands are your character's own arms.
- Grab items with your hand, eat by bringing food to your mouth, throw with your arm, climb by grabbing walls. Each has a button fallback.
- Menus and the HUD are panels in the 3D world. The title menu puts you on the beach, and loading screens keep head tracking running.
- A **VR** tab in the game's settings menu lists every PeakXR setting.
- Comfort options: snap or smooth turn, vignette, fades instead of camera shake, seated mode.

## Requirements

- [BepInExPack_PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/)
- SteamVR, or another OpenXR runtime
- Linux (Proton) only: add these Steam launch options

```
WINEDLLOVERRIDES="winhttp=n,b" %command% -force-vulkan
```

The override lets BepInEx load under Proton. `-force-vulkan` picks Vulkan, the only graphics API tested in VR under Proton.

On Windows, PEAK starts on Direct3D12, which usually runs VR slower. Add `-force-d3d11` to the launch options, or pick "PEAK using DX11" when Steam asks.

## Install

1. Install BepInExPack_PEAK.
2. Install PeakXR with r2modman, Gale or the Thunderstore app. Manual install: copy the `BepInEx` folder from the zip into the game folder.
3. Start SteamVR (or your OpenXR runtime) before the game.

## First launch

1. Put the headset on and start PEAK from Steam.
2. Open `BepInEx/LogOutput.log`. A line starting with `[xr.bootstrap] VR active` means it worked. `[xr.bootstrap] No XR runtime started` means the game is running flat; see Troubleshooting.
3. Hold the menu button for one second to recenter and recalibrate your eye height.

## Controls

Default layout for a right-handed player. Set `Input.DominantHand = Left` to swap the controllers.

- Left stick: move. Click: sprint.
- Right stick: left/right turn, up/down change item. Click: crouch.
- Grip: grab or interact. Trigger: use the item in that hand.
- Right A: jump. Right B: drop (hold to charge a throw).
- Left A: put the item away. Hold it for the backpack. Left B: ping. Hold it for the emote wheel.
- Menu: tap to pause, hold one second to recenter.
- Talk: hold a free hand at your mouth like a walkie-talkie (when the game's mic mode is Push To Talk). No button talks by default.
- Valve Index: squeeze a trackpad firmly to get the menu button.

Full table, wheels, passport and guidebook, Vive and WMR notes:
[docs/controls.md](https://github.com/ThePhaseless/PeakXR/blob/master/docs/controls.md)

## Comfort

Settings are in the VR tab, or in `BepInEx/config/com.thephaseless.peakxr.cfg` under `Comfort`. The `Preset` setting picks a group of defaults:

| Preset | Turning | Vignette |
|---|---|---|
| Comfortable | Snap 30 degrees | Strong |
| Moderate (default) | Snap 30 degrees | Medium |
| Intense | Smooth, 150 degrees per second | Off |

Other options include head- or hand-relative movement, sprint and crouch as toggle or hold, `SeatedMode`, room-scale walking, and a `ComfortEvents` section that sets what happens when the game would move your view (knockback, launch, ragdoll, camera shake, wind). Defaults favor comfort, with no head bob and no shake.

## Multiplayer

- You can play with players who do not have the mod. They do not need to install anything.
- Unmodded players see your head direction and basic reaching.
- Players with PeakXR, in VR or flat, see your full head, hands and fingers, and you see theirs.

## Known limitations

- Only SteamVR has been tested. Other OpenXR runtimes and controllers may not work.
- OpenXR under Proton is new and may not work on your setup.
- The on-screen keyboard has no symbol layer.
- A game update can break a feature. PeakXR switches off only the broken feature and keeps going.

## Troubleshooting

- Game starts flat: Check that the runtime was running first, then read `BepInEx/LogOutput.log` for the `[xr.bootstrap]` lines.
- OpenXR does not start: By default PeakXR waits `XR.StartTimeoutSeconds`, then tries OpenVR, then runs flat. To force a runtime, set `Mode = OpenXR`, `OpenVR` or `Disabled` under `[Runtime]` in `BepInEx/config/PeakXR.Preloader.cfg`. The launch options `-peakxr-openxr`, `-peakxr-openvr` and `-peakxr-novr` override it for one run.
- Play flat without uninstalling: Launch with `-peakxr-novr`.
- Check compatibility after a game update: launch with `-peakxr-novr -peakxr-compat`. Every feature installs without starting VR, and each `[compat]` line in `BepInEx/LogOutput.log` says Active or Failed.
- Something in the mod does not work: After a game update, a feature that cannot find its target is switched off. The compat list in the VR tab and the `[compat]` report in the log show which ones.

## License

MIT. PeakXR is written from scratch and contains no code from other PEAK or Unity VR mods. The XR runtime files come from Unity's packages (Unity Companion License) and Valve's unity-xr-plugin (BSD-3-Clause).

Source and issues: https://github.com/ThePhaseless/PeakXR
