# In-game test report

PEAK 2.6.b, Unity 6000.3.15f1, Proton-CachyOS 11.0, BepInEx 5.4.23.3 (BepInExPack_PEAK 5.4.75301), PEAK_MX 1.1.2 loaded alongside. Runs from 2026-10-21, master `eb8157c`.

## Install and boot

- BepInEx loads under Proton with `"winhttp"="native,builtin"` set in the game's Wine prefix (`user.reg`, backup next to it). That replaces the `WINEDLLOVERRIDES` launch option.
- PeakXR's preloader and plugin load. The title screen shows no exceptions in `Player.log`.
- The version check still reports `VersionOkay: true` and Photon still uses app version `2.6`, so lobbies with unmodded players work as before.

## Compatibility probe

`-peakxr-novr -peakxr-compat` installs every feature without starting XR. All 30 features came up `Active`: every Harmony patch target and private member they use resolves in this game build.

## Flat self-test (`-peakxr-selftest`)

The run starts a solo game, opens the VR settings tab, spawns and uses items, loads the first level, spawns every network-spawnable entity, jumps through every segment and changes the time of day. It writes `BepInEx/PeakXR-selftest.log` and screenshots, then quits.

| Run | Pass | Fail | Skip | Notes |
|---|---|---|---|---|
| 2 | 242 | 14 | 5 | first full run |
| 3 | 246 | 2 | 13 | with `-peakxr-compat`, so every VR feature's patches were live during play |
| 4 | 248 | 0 | 13 | after fixes |
| 5 | 248 | 0 | 13 | regression run after adding the VR steps |

Coverage in runs 4 and 5: 194 items spawned, 10 representative items held and used on the beach (food, compass, binoculars, rope spool, lantern, flare, torch, rope shooter, chain shooter), 43 entities scanned, 31 spawned, 5 segments jumped to, 3 times of day.

The runs found one PeakXR bug, fixed in `698772c`. The VR settings tab threw and showed no rows: the game ships no text-field cell, so the first text setting tried to instantiate `null`. Text settings now show their value read-only, and one bad row can no longer empty the tab.

The two remaining exceptions come from the game itself: a spawned Stone (`ItemOptimizer` deregisters before it registered) and held binoculars (`ItemRenderFeatureManager.setFeatureActive`). No exception in any run had a PeakXR frame outside the test harness.

Thirteen steps are skipped on purpose. The BingBong tool effects, the chain, the tornado, the zombie and petrified player bodies, the spectate ghost, the scoutmaster orbiter and the tied balloon need an owner, a spawner or a dying player that a standalone spawn can't provide. The last skip is the segment the player is already in.

## VR

Not working yet. Tested once with SteamVR and an ALVR virtual headset (Radeon 780M), launching PEAK directly through Proton:

- The preloader installed the OpenXR and OpenVR runtimes, `xr.bootstrap` started OpenXR with MultiPass, and SteamVR gave the game a session (`XR_SESSION_STATE_READY`).
- About one second later the game exited with a native crash, before Unity created its first eye texture. The cause is still open. The leading suspect is the D3D11-to-Vulkan swapchain handoff in Proton's OpenXR bridge; the desktop mirror blit and the eye-buffer settings are the other two candidates.
- Test runs now write `BepInEx/PeakXR-trace.log` with a flush after each line, so the next crash shows exactly where it stopped. The next session also compares OpenVR (`-peakxr-openvr`).

Launching PEAK from the Steam client while SteamVR runs opens a "VR Theater Screen" prompt that blocks Steam's launch queue. Launch it directly through Proton for VR tests instead.

## Not tested yet

Everything that needs a running headset: tracked hands and fingers, physical grab, eat, throw and climb, the world-space menus and HUD, comfort options, and VR multiplayer pose sync.
