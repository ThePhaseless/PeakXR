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

PeakXR runs in VR. Setup: SteamVR with an ALVR virtual headset (static head at 1.6 m, 1088x1216 per eye at 72 Hz) on the Radeon 780M, PEAK launched directly through Proton-CachyOS, OpenXR, MultiPass, `Render.RenderScale` 0.6.

The full self-test ran inside the headset session, so the creative-mode coverage above was repeated in VR: 194 items spawned, 10 held and used, 31 entities spawned, all segments and times of day. Results: 254 pass, 2 fail, 13 skip (run VR1); 254 pass, 3 fail, 12 skip (run VR2). The VR-specific checks:

| Step | Result |
|---|---|
| `vr.00-session` | OpenXR session on SteamVR/OpenXR 2.17.10, eye texture 922x1032, MultiPass |
| `vr.01-rig` | head tracked at (0, 1.60, 0), stereo camera on both eyes, audio listener at the head |
| `vr.02-title-stage` | title menu is a world-space panel on the beach |
| `vr.03-canvases` | every overlay canvas converted or deliberately left on the desktop |
| `vr.04-settings-panel` | failed: the pause menu sat 51 m behind the player (fixed in `d75f6d4`, not yet re-run) |
| `vr.05-loading-space` | the 3D loading room showed for 328 frames while the game camera was culled |
| `vr.06-features` | 25 of 25 VR features active, avatar pose fresh, HUD in world space, no stuck fade |
| `vr.07-comfort` | a knockback pulsed the comfort vignette to 0.28 and it reopened after 0.71 s; a warp faded to black and back |

Headset screenshots from the stream show correct stereo, the character's own arms and posed fingers holding items, the world-space stamina bar, and the 3D loading room.

Three problems came up and were fixed:

- The first launch crashed inside SteamVR's client library one second after the session started. DXVK had picked the RTX 4070 while SteamVR ran on the Radeon. The game and SteamVR have to use the same GPU; for this setup that means `VK_DRIVER_FILES` limited to RADV and `MESA_VK_DEVICE_SELECT=1002:15bf!`.
- The game image filled only the top-left part of each eye, with stale pixels around it. Under OpenXR MultiPass, `XRSettings.renderViewportScale` shrinks the rendered area but the runtime still receives the full image, so dynamic resolution now stays off under OpenXR (`07c8bcf`). A run with it off showed the full frame.
- The pause menu was never positioned, because a closed quit-confirm popup inside its canvas made the canvas count as closed (`d75f6d4`).

Launching PEAK from the Steam client while SteamVR runs opens a "VR Theater Screen" prompt that blocks Steam's launch queue. Launch it directly through Proton for VR tests instead.

## Not tested yet

The virtual headset has no moving controllers, so everything driven by hand motion is still untested: physical grab, eat, throw and climb, the laser pointer, wheels and the keyboard, finger tracking from real input, and VR multiplayer pose sync with a second client. Performance numbers from the 780M runs say nothing about a real setup on the RTX 4070.
