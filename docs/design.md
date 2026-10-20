# PeakXR design

PeakXR is a VR mod for PEAK (Unity 6000.3, Mono, URP, Photon PUN 2). It is a BepInEx 5 plugin plus a preloader patcher, MIT licensed and written clean-room: no code from PeakVR (Andrey04o), PEAK_VR, LCVR, CWVR or RepoXR. Research notes live in `docs/research/`.

## Goals

- Full VR play: head-tracked camera, tracked hands that are the character's own arms, controller locomotion.
- VR-native UI: menus and HUD are world-space panels in the 3D scene, never a flat screen floating in a void. The title menu stands you on the beach; loading keeps head tracking alive in a 3D space.
- Comfort first: snap or smooth turn, comfort vignette, no camera shake, no forced camera motion, fades instead of cuts, seated mode and height calibration.
- Physical interaction: grab items with your hand, eat by bringing food to your mouth, throw with your arm, climb by grabbing walls (each with a button-based fallback).
- Multiplayer with vanilla clients. Vanilla peers see head direction and basic reaching. Modded peers, VR or flat, see full head, hands and fingers.
- Survive game updates: see the rules below.

## Forward-compatibility rules

1. Harmony prefix/postfix only, no transpilers.
2. Every patch belongs to a `Feature` (`src/PeakXR/Framework`). A feature that can't find a target is rolled back alone, logged in the `[compat]` report at startup, and shown in the VR settings tab. The game keeps running.
3. Resolve non-public members with `GameMember` inside `Feature.Install`, never in static initializers.
4. Find objects by component type or by a reference the game already holds (`MainCamera.instance`, `Character.localCharacter`, `GUIManager.instance`). Never use hierarchy paths or scene indices. Scene names are allowed only as soft hints.
5. Never change `Application.version`, `CharacterSyncData` layout or RPC signatures, and never add RPC names that vanilla doesn't have.
6. No game-version lock. Log the game and Unity versions at startup.
7. After a game update, run `mise run decompile` and `mise run compat-check`.

## Runtime architecture

```
Preloader (BepInEx patcher)        Plugin (BepInEx plugin)
  picks runtime (cfg/args)            FeatureManager: discovers Feature subclasses,
  copies native XR plugin  ---env-->  installs each with its own Harmony instance
  writes UnitySubsystems manifest     PeakXR.Core sources compiled in (pure logic, unit tested)
```

Shared contracts (written first, other features code against them):

| Type | Purpose |
|---|---|
| `VRSession` | requested runtime and whether XR is rendering; `Started`/`Stopped` events |
| `VRRig` | tracking-space root, head camera, grip and aim transforms per hand. Its own root object, never under `MainCamera` |
| `VRInput` | raw per-frame controller and HMD state (`HandState`) |
| `AvatarPose` / `AvatarPoseDriver` | tracked pose for any character, applied through the game's arm IK. Local VR and remote peers use the same driver |
| `Plugin.Settings` | BepInEx `ConfigFile`. Features bind their own entries; the VR settings tab renders every entry automatically |

## Features

| Feature id | Area | Notes |
|---|---|---|
| `xr.bootstrap` | load managed XR assemblies, create the OpenXR (or OpenVR) loader, start subsystems, MultiPass | sets `VRSession.Active` |
| `xr.input` | sample `UnityEngine.XR.InputDevices` into `VRInput` | provider-agnostic |
| `rig.camera` | `VRRig`; drive `MainCamera.instance` from the HMD; skip `MainCameraMovement.LateUpdate` pose writes; neutralize FOV and shake; spectate, passed-out and cutscene states via fade or detached camera | |
| `render.fixes` | disable motion blur, lens distortion, chromatic aberration, DoF and head bob; STP to linear upscaling; fix `CameraQuad` overlays; no-op FPS cap and VSync under XR | |
| `player.look` | HMD to `lookValues` (vanilla-visible head), snap or smooth turn rotates the rig; scripted looks (frog tongue, rescue hook, scoutmaster) hold, then blend back | |
| `player.locomotion` | controller to `CharacterInput` (layout in `docs/controls.md`); head- or hand-relative direction; room-scale walking; comfort vignette and presets; seated mode | grip, trigger and drop stay with `interact.physical` |
| `ui.world` | convert overlay canvases to world space; laser pointer input; HUD on wrist or body; VR fade | |
| `ui.menu` | 3D title menu, pause menu panel, 3D loading space with live tracking | |
| `ui.settings` | VR tab in the game's settings menu, generated from `Plugin.Settings`, plus the compat report | |
| `avatar.local` | own arms IK to controllers, show arms (`HideTheBody`), finger curls | |
| `interact.physical` | grab-to-pickup, hand-aimed use, eat-to-mouth, throw velocity, VR climbing with arm locomotion; climb assist: wall-over-item grab priority, angle-checked grabs with surface haptics, regrip grace, stamina and slide haptics, arm lunge, ledge mantle | button fallbacks |
| `interact.aim` | aimed tools and launchers (rope tie-off, rope and vine shooters, rescue hook, pitons, darts, antizooka, clone staff, constructables) aim from the holding hand, or along the two-hand axis; the camera pose is swapped only for the duration of each game call; aim line while aiming; shot haptics | `new:item-aim` rows |
| `interact.tools` | wrist-twist and pull gestures for scroll input (rope payout, cannon angle), hammer strikes for pitons, hand-steered glider | button fallbacks |
| `interact.binoculars` | zoomed view on a lens render texture; the HMD view and FOV are never overridden | panel fallback |
| `interact.lights` | hand-held candle, lantern, flare, torch: light follows the hand, culling fix, strike-to-light gestures | button fallbacks |
| `interact.ping` | ping aimed by a hand ray (finger-gun gesture or button), marker sized for stereo | |
| `net.pose` | handshake via player property `PeakXR`; `RaiseEvent` pose stream (codec in Core); remote `AvatarPoseDriver`; `RPCA_StartReaching` for vanilla | runs in flat too |
| `sandbox` | built-in console unlock, item, mob and segment spawner, god mode | opt-in, offline or host only |

## Hands and items

- Hands are independent. Each arm follows only its own controller, and any one-handed item works in either hand while the other hand stays free to climb, interact or point.
- One held item at a time, like vanilla. The free hand can grip the held item's second grip but cannot pick up a second item.
- Every item is classified `OneHanded`, `TwoHanded` or `OptionalTwoHand` in a data table, with a default inferred from the item's components so new items still work.
- Custom procedural grip and hold poses are authored where vanilla has none that fits (finger presets per grip type, one-hand and two-hand holds, left-hand mirroring). Vanilla clients see the default hand.

## Comfort defaults

Snap turn 30°, vignette on (medium), head-relative movement, no head bob, no shake, sprint without FOV change, fade when the game takes the camera (ragdoll, pass out, cutscene, spectate), seated mode off, height auto-calibrated on first frame with recenter on a long press of the menu button.

## Testing

- `mise run test`: unit tests for `PeakXR.Core` (math, codec, comfort, gestures).
- In game: `sandbox` feature plus PEAK_MX for flat checks; the SteamVR MCP for headset tests.
- Linux launch options: `WINEDLLOVERRIDES="winhttp=n,b" %command% -force-d3d11`.
