# In-game test report

## Latest results (2026-10-24, PEAK 3.0.1)

These replace the older numbers below, which are kept as history. The game is PEAK 3.0.1 (build 26155610), and `mise run compat-check` resolves all 355 patch targets.

| Check | Build | Result |
|---|---|---|
| VR self-test, runs 67 to 69 (476 steps) | `0bc85f8` | 472, 470 and 468 pass, with 0, 2 and 4 failures; 4 skips |
| VR self-test, runs 70 and 71 (477 steps) | `c7076bc`, `5dd53dc` | 470 pass and 3 failures in each, all in the tests rather than the mod (see below) |
| Two clients, B runs PeakXR flat (run 27) | `d61d73b` | A 25 pass, 1 fail; `mp-check.sh --modded-b` 91 pass, 2 fail |
| Two clients, B unmodded (run 28) | `d61d73b` | Every compatibility check passes: B logged no unknown events and received no event 151 |

Runs 68 and 69 used the same build, and each failed different steps. Four steps failed off and on: vr.230 (stoop pickup), vr.89b (altar progress buzz), vr.88 (scout cannon) and vr.58b (rope anchor). All four pass in runs 70 and 71.

- **vr.230:** held items now stay on the avatar's hand when the arm can't reach the controller. A ghost hand (`Avatar.ReachGhostHand`) shows at the controller once the real hand reaches more than 5 cm past the arm. vr.173 checks that the ghost is hidden at rest, fully visible at a floor reach, and gone 0.03 s after the hand comes back.
- **vr.89b, vr.88 and vr.58b:** fixed in the test steps. The mod's behaviour did not change.

The failures in runs 70 and 71 were all in the tests:

- **vr.00 and vr.02 (start-up):** the SteamVR dashboard covered the game at launch, or PEAK's own "Failed to connect to Photon Network" popup covered the title. vr.02 now skips with that reason.
- **mob.Character_Scoutmaster:** the check called a Scoutmaster that moved 52 m and targeted the player "no behaviour". It now counts targeting plus movement.
- **vr.82b:** the scripted hand was placed 2 cm past arm's reach. The step now steps in first.

**Two-client runs on the latest build are still to do.** Runs 29 and 30 on `40a2bb3` never got past the join, with "Failed to fetch lobby data". At the time, client A's Steam was logged off, and Steam's connection log shows 103 "Disconnected By Remote Host" entries. Changes since the last good two-client run touch feeding (the feed zone counts from the item's nearest point), body presses (a slow hand is needed), held items and the ghost hand. The ghost hand is local only.

The four skips are things the game can't provide in this run: a pickaxe handle, a cheer emote, and two objects from the hardest late-game route.

PEAK 2.6.b, Unity 6000.3.15f1, Proton-CachyOS 11.0, BepInEx 5.4.23.3 (BepInExPack_PEAK 5.4.75301), PEAK_MX 1.1.2 loaded alongside. Runs from 2026-10-21, master `eb8157c` for the first runs. Later runs name their own commit.

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
| `ebb5b8f` | 248 | 0 | 13 | `-peakxr-novr -peakxr-selftest` on master after the immersion features |
| `8fd90a5` | 248 | 0 | 13 | same run, repeated |

Coverage in runs 4 and 5: 194 items spawned, 10 representative items held and used on the beach (food, compass, binoculars, rope spool, lantern, flare, torch, rope shooter, chain shooter), 43 entities scanned, 31 spawned, 5 segments jumped to, 3 times of day.

The runs found one PeakXR bug, fixed in `698772c`. The VR settings tab threw and showed no rows: the game ships no text-field cell, so the first text setting tried to instantiate `null`. Text settings now show their value read-only, and one bad row can no longer empty the tab.

The two remaining exceptions, unchanged in the later runs, come from the game itself: a spawned Stone (`ItemOptimizer` deregisters before it registered) and held binoculars (`ItemRenderFeatureManager.setFeatureActive`). No exception in any run had a PeakXR frame outside the test harness.

Thirteen steps are skipped on purpose. The BingBong tool effects, the chain, the tornado, the zombie and petrified player bodies, the spectate ghost, the scoutmaster orbiter and the tied balloon need an owner, a spawner or a dying player that a standalone spawn can't provide. The last skip is the segment the player is already in. The VR runs from run 16 on test all of these in their game context (see "Entities that used to skip").

## VR

PeakXR runs in VR. Setup: SteamVR with an ALVR virtual headset (static head at 1.6 m, 1088x1216 per eye at 72 Hz) on the Radeon 780M, PEAK launched directly through Proton-CachyOS, OpenXR, MultiPass, `Render.RenderScale` 0.6.

The full self-test ran inside the headset session, so the creative-mode coverage above was repeated in VR: 194 items spawned, 10 held and used, 31 entities spawned, all segments and times of day. Results: 254 pass, 2 fail, 13 skip (run VR1); 254 pass, 3 fail, 12 skip (run VR2); after the fixes below, 256 pass, 0 fail, 13 skip (run VR3). The VR-specific checks, as of VR3:

| Step | Result |
|---|---|
| `vr.00-session` | OpenXR session on SteamVR/OpenXR 2.17.10, eye texture 922x1032, MultiPass |
| `vr.01-rig` | head tracked at (0, 1.60, 0), stereo camera on both eyes, audio listener at the head |
| `vr.02-title-stage` | title menu is a world-space panel on the beach |
| `vr.03-canvases` | every overlay canvas converted or deliberately left on the desktop |
| `vr.04-settings-panel` | pause menu 1.80 m in front of the head, 8 degrees off the view (it sat 51 m behind the player before `d75f6d4`) |
| `vr.05-loading-space` | the 3D loading room showed for 328 frames while the game camera was culled |
| `vr.06-features` | 25 of 25 VR features active, avatar pose fresh, HUD in world space, no stuck fade |
| `vr.07-comfort` | a knockback pulsed the comfort vignette to 0.28 and it reopened after 0.71 s; a warp faded to black and back |

Headset screenshots from the stream show correct stereo, the character's own arms and posed fingers holding items, the world-space stamina bar, and the 3D loading room.

Three problems came up and were fixed:

- The first launch crashed inside SteamVR's client library one second after the session started. DXVK had picked the RTX 4070 while SteamVR ran on the Radeon. The game and SteamVR have to use the same GPU; for this setup that means `VK_DRIVER_FILES` limited to RADV and `MESA_VK_DEVICE_SELECT=1002:15bf!`.
- The game image filled only the top-left part of each eye, with stale pixels around it. Under OpenXR MultiPass, `XRSettings.renderViewportScale` shrinks the rendered area but the runtime still receives the full image, so dynamic resolution now stays off under OpenXR (`07c8bcf`). Run VR3 kept the setting on in the config and the headset showed the full frame.
- The pause menu was never positioned, because a closed quit-confirm popup inside its canvas made the canvas count as closed (`d75f6d4`).

Launching PEAK from the Steam client while SteamVR runs opens a "VR Theater Screen" prompt that blocks Steam's launch queue. Launch it directly through Proton for VR tests instead.

## Graphics API

On this Proton setup (direct `proton run`, no Wine VR registry keys) D3D12 and D3D11 both fail to create a device (`VK_WINE_openxr_device_extensions` not supported) and Unity falls back to Vulkan. Every VR run so far ran on Vulkan, including the runs launched with `-force-d3d11`. The launch option is now `-force-vulkan` (see the README). Without a flag the game tries D3D12, then D3D11, then Vulkan.

## VR runs after the immersion features

Setup: SteamVR with an ALVR virtual Index, Radeon 780M, Vulkan, OpenXR MultiPass, full self-test.

| Run | Commit | Pass | Fail | Skip |
|---|---|---|---|---|
| 9 | `9c4cb42` | 283 | 4 | 14 |
| 10 | `0d3f4e3` | 286 | 3 | 13 |
| 11 | `0693c34` | 287 | 2 | 13 |
| 12 | `e33b231` | 288 | 1 | 13 |
| 13 | `8f17d94` | 286 | 3 | 13 |
| 14 | `20558b3` | 288 | 1 | 13 |
| 15 | `17f510d` | 290 | 0 | 13 |
| 16 | `ee9d73d` | 301 | 3 | 1 |
| 17 | `e8ce873` | 303 | 1 | 1 |
| 18 | `ad2932f` | 307 | 0 | 0 |

Run 18 is the first run with no failure and no skip. The log holds only the two known vanilla exceptions.

Run 29 on `2124aaa` (master): 308 pass, 7 fail, 0 skip, 676 s. The `vr.12-eat` and `vr.72-keyboard` steps pass. Known open items, the seven failures:

- `vr.04-settings-panel`: a flat-screen backdrop is still left on the pause menu (29 hidden, 6 row strips drawn).
- `vr.29-ground-backpack`: the backpack wheel did not open on a grip with nothing under the hand.
- `vr.42-rope-hand-over-hand`: the grip did not grab the rope (nearest segment 0.83 m from the reach point).
- `vr.45-lean`: the body and view moved 0.5 m while standing, and the hands sat 0.19 m off the controllers.
- `vr.46-lunge`: the hand did not catch the wall after the lunge (0.74 s, grace 0.6 s).
- `use.Compass`: the primary use never started.
- `use.RopeSpool`: no red preview while the tie-off attempt was held, though the NoWall denial fired.

The one failure left in run 12 was `vr.26` (light with flame): the game's hover landed on a nearby item instead of the campfire. Runs 13 to 18 found the cause and four other bugs, listed below.

New VR steps that pass in run 12:

| Step | What it showed |
|---|---|
| `vr.08` arm reach | eye 0.02 m from the avatar's eyes, hands about 2.5 cm from the controllers, arm stretch x1.00, elbows pointing down |
| `vr.09` belt view | 3 slot ghosts visible when looking down, 0 body colliders in the way |
| `vr.16` snap turn | the eye slid 0.5 cm on a 30 degree snap |
| `vr.40` | physical crouch |
| `vr.41` | arm-swing sprint |
| `vr.42` rope hand over hand | a 0.40 m pull raised the climb point 0.37 m |
| `vr.43` vine | 0.32 m along the vine for a 0.40 m pull |
| `vr.44` | piton hang |
| `vr.21` bugle | pitch follows the bell |
| `vr.22` bandage | pressed to the body |
| `vr.23` parasol | runner |
| `vr.24` luggage lid | a still hand barely moves it, a 0.35 m lift opens it |
| `vr.25` cook over fire | cooked 0 to 1 |
| `vr.27` warm hands | 8 pulses in 2 s |
| `vr.60` to `vr.62` | refused-action cues (NoWall, NeedsTwoHands, CantUse) |
| `vr.63` | sticky item hand throw |
| `ui.01` | interact prompt at the target |
| `ui.02` | button labels by the hand |
| `ui.03` world banner | sign clamped to 28.6 m by a raycast |
| `ui.04` | rope gauge beside the spool |

### Bugs found by runs 13 to 18

All five are fixed.

- Holding an item and pulling the trigger while the hand pointed at something sent a ping instead of using the item. `PingInput` armed the finger-gun ping on a hand that held an item. Now only an empty hand pings. The new step `vr.28-use-while-pointing` passes with "used True, pings 0".
- The torch is lit like a lantern (`FlareActive`), and the self-test handed over an unlit torch. `vr.26` now lights it with the trigger, and the light hold starts on the campfire. A cue now shows when an unlit flame is held at an unlit fire.
- The campfire light zone sat at the edge of physical reach. It is now 0.6 m wide, with a "too far" cue.
- Snap turn slid the view 4.7 cm, because the torso catches up over several frames. The eye is now held level through that. `vr.16` measures 0.5 cm on a 30 degree snap.
- The VR eye sat on top of the avatar's head. It now sits at the avatar's eyes: `vr.08` measures 0.02 m from them. Scouts have big heads, so the real avatar eyes are about 0.48 m above the shoulders.

### Entities that used to skip

The 13 skipped entities are now tested in their game context:

- BingBong effects (5): the game never spawns these, so the test uses a stand-in target.
- ChainShootable: fired at terrain, then climbed.
- MushroomZombie_Player, PetrifiedScout and PlayerGhost: the local player dies through the game's own path with a test-only end-game hold, then is revived.
- ScoutmasterGhostOrbiter: set up with `SetOrbitCharacter`.
- TiedBalloon: spawned through the balloon item.
- Tornado: the map of the day had no `TornadoSpawner`, so the run loads a Mesa level through the game's own level index offset and kiosk. The tornado moved 15.6 m in 4 s, caught the player and lifted them 8.7 m. The comfort vignette peaked at 0.63.
- Beach segment: the player is already there, which now counts as a pass.

### New features checked in VR

- VR Guide in the VR settings tab (`vr.30`): the Grab and Climb demos play, with cue sounds beside their rows.
- Offline practice in the solo airport (`vr.31`): the turn station passes and the exit leads back to the title.

Headset visual checks, from MCP screenshots:

- Arms now bend with the elbows down. Before, the elbows pointed sideways and the forearms looked like flat bands, because the game's elbow IK hint had weight 0.
- The pause menu sits on a soft translucent rounded backdrop, with the laser.
- Looking down first showed the top of the player's own torso, because the eye sat on top of the head, 0.46 to 0.48 m above the shoulders. Scouts have big heads and the real avatar eyes are about 0.48 m above the shoulders. The eye now sits at the avatar's eyes and the belt slots draw on top.

Desktop mirror: it was upside down and cropped, because the provider's mirror `srcRect` is a window-aspect crop (x 0.20, y 0.33, w 0.60, h 0.33). The whole eye is now drawn upright and letterboxed. A desktop screenshot (spectacle) confirmed it.

## Hand-driven checks

The self-test also drives PeakXR's own input layer with scripted controller poses and buttons (steps vr.10 to vr.20), and another session drove the virtual Index controllers live through SteamVR while PEAK idled on the beach.

Scripted hands, run 18 (266 pass, 1 fail at the time of that report): grabbing with either hand, throwing (landed 2.8 to 3.0 m away at about 6 m/s, inside the vanilla clamp), two-hand holds, snap turn, stick walking, the emote wheel, clicking the pause menu with the laser, holstering to a body slot and drawing back, and climbing all pass. Climbing needed a fix: the game only moves a climber 2/7 of the requested step on a flat wall, so arm pulls were scaled against the wrong speed; a 0.4 m pull now lifts the body 0.39 m.

Live controller input reached PeakXR within one frame for every action: thumbstick turn and walk, A jump, trigger use, grip interact, the emote wheel on B, the pause menu on a firm trackpad squeeze (SteamVR keeps the Index system button), arm tracking and finger touch. Two problems came up and are fixed but not yet re-run in the headset:

- Pressing B also jumped, because PEAK ships its own "XR" input scheme that binds Jump, Use and Move to XR controllers. PeakXR now stops the game's input system from reading XR controllers while VR runs.
- The pause menu was readable only with effort, because the whole 1920x1080 canvas was fitted to the panel width while the menu content filled a small part of it. Menus are now sized by what they draw.

The eat step once failed because the test moved the wrong hand to the mouth (it read which hand held the item one frame too early). The test is fixed, and `vr.12-eat` passes in run 29.

## Multiplayer

`docs/multiplayer-compat.md` holds a static audit: no new RPCs, version gates untouched, pose event 151 sent only to PeakXR peers, adaptive pose rate. Parity fixes are merged: feed angle check, no remote yaw spin, reach clamp to the vanilla 2 m from the head, piton time never faster than vanilla, sandbox only offline. 

The two-client test has run to the end. Run 4 had one VR client (PeakXR) and one unmodded client: 16 pass, 0 fail, 2 skip, 188 s. `tools/mp-check.sh` on its logs is clean. B logged no exceptions and no unknown-event errors, received no event 151, and A sent none. Join, walk to the host, emote, ping, board, regroup, luggage, grab, drop, pickup, throw, holster, climb and two-hand pass, with positions and held items matching on both sides. The `eat` and `feed` steps skip because no food lay within 40 m of the players.

Earlier runs failed (run 3: 7 pass, 9 fail, 2 skip). The fix that made run 4 clean was a lag tolerance in `tools/mp-check.sh`: B can write its checkpoint before A's equip RPC arrives, so the held-item check takes the first sample within `LAG_TOL` (1.0 s by default). To rerun, follow "Two-client multiplayer test" in `docs/testing.md`, then run `tools/mp-check.sh [mp-dir]`.

`tools/MpObserver` is the read-only observer plugin on the unmodded client, and `-peakxr-mptest` is PeakXR's scripted side. The static audit in `docs/multiplayer-compat.md` still covers what the test does not drive.

## REA

rea-agents 6.7.0 is registered for Claude Code (`~/.claude.json`). `rea mcp doctor` reports healthy: 207/207 tools, 6/6 prompts, request flow OK. A live `inspect_managed_members` call read `CharacterAnimations` from the game's `Assembly-CSharp.dll`.

## Not tested yet

Still untested: finger curls from real hand tracking, and two VR players in one lobby. Feeding another player over the network and a second PeakXR client (in flat mode) have both been tested since this list was written. No person has yet tried these in a headset: comfort tuning, haptic and audio cue levels, wrist HUD readability, and real hand tracking. Performance numbers from the 780M runs say nothing about a real setup on the RTX 4070.
