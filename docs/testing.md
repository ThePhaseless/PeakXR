# In-game testing with the sandbox

The `sandbox` feature spawns every item, mob and entity and removes the risk of dying, so interactions can be checked one at a time in VR and in flat play. Research and decompile references are in `docs/research/creative-test-mode.md`.

## Setup

1. Set `Sandbox.Enabled = true` in `BepInEx/config/com.thephaseless.peakxr.cfg`. It is off by default.
2. Play **offline** (solo, no lobby) or as the **host**. The sandbox refuses to act in someone else's lobby.
3. Keys: `F1` opens the game console (unlocked by the sandbox), `F7` toggles the flat sandbox window (`Sandbox.WindowKey`). `F5` and `Insert` belong to other mods.
4. The VR UI will call the same `SandboxActions` methods. Until it exists, drive VR sessions with console commands, or put the headset on after spawning from the flat window.

## Console commands

Type in the F1 console, for example `Sandbox.Spawn Rope`. Names are the prefab names (spaces optional).

| Command | Does |
|---|---|
| `Sandbox.Spawn <Item>` | Drop the item ahead of you |
| `Sandbox.Give <Item>` | Put the item in your hand or inventory |
| `Sandbox.SpawnAll` | Spawn every item on a grid ahead of you (the test line) |
| `Sandbox.ListItems`, `Sandbox.ListMobs` | Print names |
| `Sandbox.Mob <name>` | Spawn a mob or entity ahead of you |
| `Sandbox.ScanMobs` | Deep scan Resources for more spawnable prefabs |
| `Sandbox.God` | Toggle god mode |
| `Sandbox.Revive`, `Sandbox.Warp` | Revive where you died; warp to spawn |
| `Sandbox.Jump <Segment>` | Beach, Tropics, Alpine, Caldera, TheKiln, Peak |
| `Sandbox.SetTime <hours>` | 0 to 24 (sunrise 5, sunset 21) |
| `Sandbox.FreeCam` | Game fly camera, flat play only |

The game's own commands also work, for example `Character.InfiniteStamina`, `Afflictions.AddPoison`, `Afflictions.ClearAll`, `Item.Add <Item>`.

## Procedure

Start a run, turn on god mode, then work through the sections. Record the result per item or mob in your report: pass, or what broke (log line, visual, or input).

### 1. Test line

`Sandbox.SpawnAll`, wait for "Test line done", then walk the grid. For every item:

- It spawned, has a model, and is not stuck in the ground or another item.
- Looking at it shows the right prompt; the pick-up gesture or button grabs it with the correct hand.
- Held pose is sane: grip position, orientation, nothing clipping the camera.
- Primary use and secondary use do something (see categories below), then drop and throw.
- Re-grab a thrown item. Put it in a backpack slot and take it out.

### 2. Per item category

| Category | Examples | Check in VR |
|---|---|---|
| Food and berries | Apple Berry, Berrynana, Granola Bar, Egg, TrailMix | Eat by bringing it to the mouth; button fallback; stamina or hunger change; peel or leftover spawns |
| Mushrooms | Mushroom Normie, Cluster, Glow, Poison variants | Eat effects; poison variants raise Poison; ShelfShroom and BounceShroom place and work |
| Medicine | Bandages, FirstAidKit, Antidote, Cure-All, AloeVera, Sunscreen, Heat Pack, drinks | Use on self and on a friend (`canUseOnFriend`); the matching status drops |
| Amulets and relics | Amulet_*, AncientIdol, Cursed Skull, ScoutsHonor, Strange Gem | Passive or use effect applies; Curse status behaves |
| Light and fire | Torch, Lantern, Candle, Flare, FireWood, Marshmallow, PortableStovetopItem | Lights when used; cooking changes the held item; light follows the hand |
| Climbing and rope | RopeSpool, Anti-Rope Spool, RopeShooter, ClimbingSpike, ChainShooter, RescueHook, ClimbingChalk | Aim with the hand; the rope attaches; climbing it works with arm locomotion; spike hammering |
| Movement | Balloon, Parasol, Glider, Parachute, Jetpack, Rocketpack, ScoutCannonItem, Warp Compass, WarpFungus, Warpsketball | Held item steers in VR without sickness spikes; camera fades on warps; no forced camera motion |
| Tools and misc | Compass, Binoculars, Megaphone, Bugle, Guidebook, Passport, Frisbee, Basketball, Dynamite, Snowball | Binoculars overlay is readable in VR; guidebook pages open in world space; throwables use hand velocity |
| Containers | Backpack, Fannypack, Luggage* | Open, put items in, take them out, wear on the back; luggage opens and spawns loot |
| Chess pieces (`C_*`) and props | C_King, Bishop, Knight | Grab and carry; nothing special should happen |
| Not spawnable alone | `*Preview`, `*_Prop`, `BaseConstructable*`, `TestConstructable` | Spawning is allowed; expect inert or invisible objects, do not report as bugs |

### 3. Per mob and entity

Use the Entities tab (F7) or `Sandbox.Mob <name>`. Mobs that are item prefabs (Beetle, Scorpion, Frog) appear there too.

| Name | Style | Check |
|---|---|---|
| Beetle, Scorpion, Frog | item prefab with `Mob` | Idles, notices you, attacks, ragdoll hit does not whip the VR camera; can be picked up and cooked |
| Character_Scoutmaster | room object | Chases, grabs; assumed to need its spawner link, watch the log for NREs |
| MushroomZombie | plain | Wakes near you, chases, spores status |
| BeeSwarm | plain | Chases, poisons; assumed to run without a hive |
| Tornado | needs a `TornadoSpawner` in the scene | Only spawns on a map that has one; pushes you; camera does not roll. The self-test triggers the spawner itself |
| BugfixOnYou | attaches to you | Poison ticks on you |
| RavenVoiceThief, tumbleweed | plain | Behaviour unchecked; note what it does |
| Anything else from "Deep scan" | plain | Spawns, replicates, no log errors |

For each mob: it spawns facing you, its attack applies the right status or ragdoll, god mode cancels the damage, VR hands can still grab items while it attacks, and it does not break the VR camera.

### 4. Per interaction

With god mode on, repeat each in VR and then flat:

- Grab, hold, drop, throw, and pass an item to a second hand.
- Eat, drink, use on self, use on friend, secondary use.
- Backpack in and out, hotbar slot swap, drop with a full inventory.
- Climb: wall, rope, ladder-like spike; let go and fall (god mode removes fall damage).
- Ragdoll, pass out, `Sandbox.Revive`: the camera fades or detaches; no motion sickness trigger; control returns.
- `Sandbox.Jump` to each segment and `Sandbox.Warp`: fade, rig follows the character, UI re-anchors.
- `Sandbox.SetTime` at 0, 12, 21: lighting and fog render correctly in both eyes.
- Multiplayer smoke test: host with the sandbox, a second client without it; spawned items and mobs show up for the client, nothing desyncs.

## Unattended self-test

The `sandbox.selftest` feature plays the procedure above by itself, for runs where nobody watches the screen. It runs only when the game starts with `-peakxr-selftest`. For that run it turns the sandbox and god mode on without touching `Sandbox.Enabled` in the config, and it quits the game when it is done.

Flat: add `-peakxr-novr -peakxr-selftest` to the launch options (after the usual `WINEDLLOVERRIDES="winhttp=n,b" %command%`).

VR: start SteamVR with a headset first (a virtual one such as ALVR works, nobody has to wear it), then launch with `-force-vulkan -peakxr-selftest` and without `-peakxr-novr`. The run adds the `vr.*` steps below. If VR does not start, `vr.00-session` fails with the last `[xr.bootstrap]` line, the other `vr.*` steps are `SKIP` ("VR not active") and the rest of the script runs flat.

### Live input

Launch with `-force-vulkan -peakxr-selftest -peakxr-selftest-hold` (VR, SteamVR running). The script runs `00-title`, `vr.00` to `vr.03`, `01-airport`, `04-beach`, `vr.05` and `vr.06`, skips the settings, items, use, mob and segment steps, writes `PeakXR-selftest.log` and then leaves the game running on the beach. It quits 20 minutes later. Another session can now drive virtual Index controllers through SteamVR.

With `-peakxr-selftest` (hold or not) every VR sample is diffed and changes are logged to `BepInEx/LogOutput.log` (flushed per line), one line per edge:

- `[input] L|R tracked|primary|secondary|menu|stickclick|triggertouch|primarytouch on|off`
- `[input] L|R trigger|grip level N (0.62)`: N is 0 below 0.1, 1 below 0.5, 2 below 0.9, 3 from 0.9.
- `[input] L|R stick-right|stick-left|stick-up|stick-down on|off (x,y)`: crossing 0.5.
- `[input] L|R pos x y z`: at most once per second, after the hand moved more than 2 cm.
- `[input] char jumpWasPressed|sprintIsPressed|usePrimaryIsPressed|useSecondaryIsPressed|interactIsPressed|dropIsPressed|emoteIsPressed|pingWasPressed|move-right|move-left|move-forward|move-back on|off`: the local `CharacterInput` after all bindings ran.

Look for: each `L|R` line you drove has a matching `char` line in the same frame range (stick up gives `move-forward`, trigger level 3 gives `usePrimaryIsPressed` while holding an item). An `L|R` line with no `char` line means the binding dropped it; no `L|R` line means the sampler never saw it.

The script:

| Step id | Checks |
|---|---|
| `00-title` | The Title scene and `MainMenu` load |
| `01-airport` | Play Solo goes offline and loads the Airport with a local character |
| `02-settings-vr` | The pause menu opens, its settings page shows the VR tab, and the menu closes again |
| `item.<name>` | Every `ItemDatabase` item spawns and still exists two frames later, in rows of 8 |
| `use.<name>` | One item per category (food, tools, rope, lights, launchers) reaches the hand, is used for 1.5 s, then dropped |
| `04-beach` | The airport kiosk loads the first level with a local character |
| `05-mob-scan`, `mob.<name>` | Each entity from the deep scan spawns, is filmed after 3 s, then removed. Entities that cannot be spawned alone are made in their game context instead (below) |
| `06-segment-<Segment>` | `Sandbox.Jump` to every segment; the segment the player is already in passes when it is the current one |
| `07-time-<hh>` | Time of day 06, 12 and 21 |

Entities made in their game context:

| Step | How |
|---|---|
| `mob.Bingbong_*` | No game code spawns them (`BingBongPhysics.DoEffect` is empty). Spawned with their `bingbong` transform set 5 m behind the player, looking at them; the effect must follow it, push or pull the body, and remove itself after 2 s |
| `mob.ChainShootable` | The chain shooter turns until its own `WillAttach` accepts the terrain ahead, fires, and the chain must have colliders; `JungleVine.Interact` must start a vine climb |
| `mob.ScoutmasterGhostOrbiter` | Spawned 4 m away with `SetOrbitCharacter(local)`, the soul pillar's call; it must reach the player within 2 s |
| `mob.TiedBalloon` | A balloon item used from the hand ties a balloon (`CharacterBalloons.TieNewBalloon`); popping it unties it |
| `mob.Tornado` | After the first segment jump that reaches a running `TornadoSpawner`: its own `SpawnTornado`; the tornado must move and catch the player warped beside it (VR: the comfort vignette pulses). When today's map has none, the very last steps go back to the airport, pick a Mesa level with the game's `MapDebugUI.SetMapNumber` (`08-mesa-level`), jump its segments (`08-mesa-<Segment>`) and try there; the run ends on that level. `SKIP` when no level has one |
| `mob.MushroomZombie_Player`, `mob.PetrifiedScout`, `mob.PlayerGhost` | Last in the run. `Character.Zombify`, petrify 100 plus `Character.Die`, and `Character.Die`; the zombie, statue or ghost must appear, then `RPCA_ReviveAtPosition` revives the player where they stood, which must remove the zombie and the ghost (VR: the fade clears, no spectate nameplate for your own body). The solo game over (`Character.CheckEndGame`) is held back meanwhile |

VR runs only:

| Step id | Runs | Checks |
|---|---|---|
| `vr.00-session` | after `00-title` | `VRSession.Active` within 60 s; the note has the running runtime, eye texture size, stereo mode and the XR device names |
| `vr.01-rig` | title | `VRRig.Instance` and its tracked head (note says moving or static), `MainCamera` camera renders stereo to both eyes, an enabled `AudioListener` within 0.5 m of the head |
| `vr.02-title-stage` | title | The title menu canvas is world space and the title stage holds `VRRig.OriginOverride` (when `UI.TitleOnBeach` is on); screenshot `vr-02-title` |
| `vr.03-canvases` | airport | No active root canvas that ui.world converts is still screen space (its own Hidden and Fade rules are exempt); the note counts world and overlay canvases; screenshot `vr-03-airport` |
| `vr.04-settings-panel` | airport | The pause menu with the VR tab is world space, 0.5 to 4 m from the head and under 60° off the view; screenshot `vr-04-settings` |
| `vr.05-loading-space` | after `04-beach` | During that load the loading space was shown and the game camera's `cullingMask` was 0; screenshot `vr-05-loading` mid-load |
| `vr.06-features` | beach | Every VR feature is `Active`, the local avatar driver has a fresh pose, the HUD canvas is world space, `VRFade.Target` is 0; records whether the vignette ring exists yet |
| `vr.07-comfort` | beach, after the mobs | A knockback (`RPCA_AddForceAtPosition` on the local character) pulses the comfort vignette, which opens again within 2 s; a warp to the current segment leaves `VRFade.Target` at 0; screenshot `vr-07-knockback` |
| `vr.10-grab-right`, `vr.11-grab-left` | beach, after `vr.07` | An Apple spawned 0.45 m ahead at chest height; that hand reaches it in 0.5 s and grips. The character holds it within 3 s, in that hand (`LocalHold` and the avatar pose). A slow release keeps it, or drops it with `Interaction.ReleaseToDrop`; screenshot `vr-10-grab-right` / `vr-11-grab-left` |
| `vr.12-eat` | beach | The hand holding a given Apple, steered to the mouth point, eats it: the game's `Consume` RPC stamps `CharacterData.lastConsumedItem` within the use time plus 6 s. The note has the grip's and the item's closest approach (the item stays outside the holder's own head collider), the use progress and hunger before and after; screenshot `vr-12-eat` |
| `vr.13-throw` | beach | A held Apple swung forward at 6 m/s and let go mid-swing leaves the hand and lands 2 m or more away. Its launch speed is within the throw clamp (hip speed plus half `maxThrowForce`) and its range within what that speed allows; screenshot `vr-13-throw` in flight |
| `vr.14-two-hand` | beach | A two-hand item (`RopeSpool`, else `Binoculars` or `RopeShooter`) in one hand; the free hand grips it and `AvatarPose.TwoHandHold` turns on, and off again on release; screenshot `vr-14-two-hand` |
| `vr.15-climb` | beach | Warps to the nearest steep terrain face within 25 m and faces it. A gripping hand on the surface starts the climb (`CharacterData.isClimbing`), pulling the hand down 0.4 m lifts the body at least 0.1 m, and letting go ends the climb. Then a warp back to spawn; screenshot `vr-15-climb` |
| `vr.16-snap-turn` | beach | Turn stick right, then centred: the rig turns by `Comfort.SnapTurnAngle` (with smooth turning, by more than 5°); screenshot `vr-16-snap-turn` |
| `vr.17-move` | beach | Move stick forward for 1 s walks the character at least 0.5 m; screenshot `vr-17-move` |
| `vr.18-emote-wheel` | beach | Holding the emote button (off hand, upper button) opens the game's emote wheel. Moving the hand up hovers a slice, and releasing closes the wheel and plays the emote; screenshot `vr-18-emote-wheel` |
| `vr.19-laser` | beach | Pause menu open, both aim rays on its Resume button, a trigger pull: the menu closes; screenshot `vr-19-laser` |
| `vr.20-holster` | beach | A held Apple taken to its body slot and let go goes into that hotbar slot; a grip there draws it back into the same hand; screenshot `vr-20-holster` |
| `vr.29-ground-backpack` | beach | A grip on a spawned ground backpack opens its wheel, which stays open every frame while the grip is closed; opening the grip closes it and the move stick walks again; screenshot `vr-29-ground-backpack` |
| `vr.32-shoulder-stash` | beach | With a pack worn and an Apple grabbed in one hand (grip kept closed), the other hand over the shoulder opens the pack wheel, hovers a pocket, and opening that grip stashes the Apple and closes the wheel; screenshot `vr-32-shoulder-stash` |

Screenshots in VR show the desktop mirror, which rig.camera copies from one eye.

Steps `vr.10` to `vr.20` need no one at the controllers. While each one runs, `VRInput.TestOverride` replaces both controllers right after the sampler with scripted hands, which rest tracked in front of the chest with a finger on the trigger. The step moves a hand, closes a grip or pushes a stick, and the rest of the mod treats that as real controller input. Each step starts with empty hands and an empty hotbar. When it ends, the grips open, the override is cleared and anything the step dropped or threw is removed, so a failed step leaves nothing behind for the next one.

Each step has a timeout. A step fails when its action fails, when it times out, or when the game logs an exception while it runs. Errors the game logs are written next to the step but do not fail it.

Results:

- `BepInEx/PeakXR-selftest.log`: one line per step (`id PASS|FAIL|SKIP 1.23s note | first logged error`), then the totals and the `[compat]` feature states. Lines are appended as steps finish, so a crash still leaves the steps run so far.
- `BepInEx/PeakXR-selftest/*.png`: screenshots named after the steps (`03-items-000.png`, `05-mob-BeeSwarm.png`) at the game window's resolution. Old screenshots are deleted when a new run starts.
- `BepInEx/LogOutput.log` has the same step lines with a `[selftest]` prefix.

## Two-client multiplayer test

This run covers part of the live test plan in `docs/multiplayer-compat.md` without anyone at the keyboard. Client A is PeakXR in VR on the main Steam account. Its hands are scripted through `VRInput.TestOverride`, the same way as the self-test's `vr.*` steps. Client B is a second Steam account in Flatpak Steam, running vanilla PEAK plus one read-only plugin, `MpObserver`. The two accounts must be Steam friends.

B hosts a lobby through the title menu's Host button and writes `lobby.json`. A joins that lobby through the game's own `SteamLobbyHandler.TryJoinLobby`, then plays a scripted sequence. After each step A appends a line to `a-checkpoints.jsonl`. B answers each line with its own snapshot of A's character under the same name. A sends nothing beyond what PeakXR always sends, and B sends no RPC or event and sets no property.

### Shared directory

Both clients use `~/.var/app/com.valvesoftware.Steam/peakxr-mp`. Flatpak Steam's sandbox has a private `/tmp` and hides the real home, but it mounts its own `~/.var/app/com.valvesoftware.Steam` at the same host path. Native Steam and a direct Proton launch see that path as well, so both sides read and write the same files. Both plugins use this directory when `-mpdir` is not given. They find the home from `$HOME`, or from Wine's `WINEHOMEDIR` when Wine hides `HOME`. The launch commands below still pass `-mpdir` so that nothing depends on that lookup. Each side deletes its own old files when it starts: A deletes `a-*`, and B deletes `b-*` and `lobby.json`.

### Client B setup (once)

The Flatpak copy is `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/PEAK`. It must be on the same build as A (`version.txt`). Give it the BepInEx loader and MpObserver only, never PeakXR or its preloader:

```sh
mise run build-observer
SRC=~/.local/share/Steam/steamapps/common/PEAK            # A's copy, which has BepInExPack_PEAK
B=~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/PEAK
cp "$SRC"/{winhttp.dll,doorstop_config.ini,.doorstop_version} "$B"/
mkdir -p "$B/BepInEx/plugins/MpObserver"
cp -r "$SRC/BepInEx/core" "$B/BepInEx/"
cp dist/MpObserver/MpObserver.dll "$B/BepInEx/plugins/MpObserver/"
```

BepInEx only loads under Proton when Wine prefers the native `winhttp.dll`. Pick one of these:

- Launch options. In Flatpak Steam, open PEAK's Properties and set the launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command% -mpobs -mpdir /home/<user>/.var/app/com.valvesoftware.Steam/peakxr-mp`. With the flags in the launch options, a plain `-applaunch` needs no arguments. MpObserver stays idle on any launch without `-mpobs`.
- Prefix registry, as in the native setup. Launch PEAK once from Flatpak Steam so that the prefix exists, quit it, and then add `"winhttp"="native,builtin"` under `[Software\\Wine\\DllOverrides]` in `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/3527290/pfx/user.reg`. Edit the file only while the game is closed.

Check the setup by launching B once. `BepInEx/LogOutput.log` in B's copy must list `MpObserver` and nothing from PeakXR.

### Launch

Start B first, before SteamVR, because Steam can block game launches while SteamVR runs (the VR Theater prompt):

```sh
flatpak run com.valvesoftware.Steam -applaunch 3527290 \
  -mpobs -mpdir /home/<user>/.var/app/com.valvesoftware.Steam/peakxr-mp
```

Leave out the flags when they are already in the launch options. `steam://run/3527290` also works, but Steam asks for confirmation when that URL carries arguments, and nobody is there to click. B waits for the title, switches the lobby type to Friends (A joins by id), presses Host, and writes `lobby.json` once the Airport room is ready.

Then start SteamVR with a headset (a virtual one works) and launch A directly through Proton, as in the VR self-test:

```sh
PEAK=~/.local/share/Steam/steamapps/common/PEAK
cd "$PEAK" && \
STEAM_COMPAT_DATA_PATH=~/.local/share/Steam/steamapps/compatdata/3527290 \
STEAM_COMPAT_CLIENT_INSTALL_PATH=~/.local/share/Steam \
SteamAppId=3527290 SteamGameId=3527290 \
WINEDLLOVERRIDES="winhttp=n,b" \
"$HOME/.local/share/Steam/compatibilitytools.d/Proton-CachyOS Latest-x86_64_v3/proton" run "$PEAK/PEAK.exe" \
  -force-vulkan -peakxr-mptest -mpdir "$HOME/.var/app/com.valvesoftware.Steam/peakxr-mp"
```

Under the vr-test-mcp's SteamVR (ALVR on the Radeon), also set `VK_DRIVER_FILES` and `MESA_VK_DEVICE_SELECT` as in the self-test notes. `-peakxr-mptest` cannot be combined with `-peakxr-selftest`. A waits up to 4 minutes for a `lobby.json` written in the last 30 minutes. Without VR, the steps marked VR below are `SKIP`.

A quits after writing `a-done.json`. B quits when it sees that file, 10 minutes after hosting if nobody joined, or 15 minutes after A joined.

### Steps

| Checkpoint | A does | B records |
| --- | --- | --- |
| `join` | Joins by lobby id; checks the lobby's `PeakVersion` against its own, that it is in that Steam lobby, and that the Photon master is the host | A's arrival (`join` line) |
| `walk-to-host` (VR) | Walks on the move stick to 1.5 m from B | A's position |
| `emote` (VR) | Opens the emote wheel, flicks, plays an emote | `RPCA_PlayRemove` |
| `ping` (VR) | Finger-gun ping at the ground in front of B | `ReceivePoint_Rpc` with the point |
| `board` | Walks to the kiosk and starts the run (the master loads the island) | scene change |
| `regroup` (VR) | Walks back to B on the beach | position |
| `luggage` (VR) | Lifts suitcase lids by hand until 2 foods and 1 other item lie within 40 m, trying up to six cases; a case it cannot walk to is passed over | `OpenLuggageRPC` |
| `grab` (VR) | Picks up the nearest non-food item by hand (never a backpack, which opens its wheel) | A's current item |
| `drop` (VR) | Taps the drop button; logs where the item came to rest | ground items of that kind near A |
| `pickup` (VR) | Picks the same item up again | current item |
| `throw` (VR) | Faces B and throws it; logs the landing point | ground items of that kind |
| `eat` (VR) | Grabs food and holds it to the mouth until eaten | `Consume` |
| `holster` (VR) | Holsters an item at the body slot and draws it back | `EquipSlotRpc` |
| `climb` (VR) | Walks to the nearest climbable wall and starts climbing by grabbing it | `StartClimbRpc`, climbing flag |
| `two-hand` (VR) | Holds a two-handed item with both hands | current item, hand IK targets |
| `feed` (VR) | Holds food at B's head until B is fed | `SendFeedDataRPC`, `GetFedItemRPC`, B's own `lastConsumedItem` |

A step that finds nothing to do (no wall or no food within 40 m) is `SKIP`.

### Files

All files are in the shared directory.

- `lobby.json`: `{lobbyId, hostSteamId, time, room, peakVersion, game}` from B.
- `a-checkpoints.jsonl`: one line per checkpoint with `status` (pass, fail or skip), `note`, A's `pos`, `head`, `look`, held item (`heldId`), `itemId`, `sent151`, and per step `landing`, `ping`, `landingToHost` or the join fields (`versionOk`, `inLobby`, `masterIsHost`, `lobbyVersion`, `localVersion`).
- `a-mptest.log`: the self-test style step log. `a-done.json`: A's reason for ending, totals, and counts of the events it sent by code.
- `b-observer.jsonl`: `start`, `lobby`, `join`, a `sample` of every remote character every 0.5 s (position, head, look, current item and its state, use flags, climbing, carrying, carried, passed out, fall time, hand IK targets, events received), `checkpoint` lines (a sample plus ground items and the RPCs since the previous checkpoint), every observed `rpc`, and `log` lines for exceptions, errors and unknown-event errors.
- `b-summary.json`: end reason, received event counts by code (`event151` must be 0), RPC counts, exceptions with the first 20 messages, errors, and unknown-event errors.
- `a-<checkpoint>.png` and `b-<checkpoint>.png`: each side's screen at the checkpoint. B turns its view toward A first.

### Checking the result

```sh
tools/mp-check.sh [mp-dir]       # POS_TOL=1.0 and LAND_TOL=1.5 metres by default
```

It prints one `PASS`, `FAIL`, `SKIP` or `INFO` line per check and exits with 1 when any check fails:

- B saw a remote player join.
- A's version check passed: the lobby's version equals A's, A is in the lobby, and the master is the host.
- For each checkpoint A passed: B logged it with A present, B's view of A's position is within `POS_TOL`, and B sees the same held item id as A. For `drop` and `throw`, an item of that kind lies within `LAND_TOL` of where A saw it land. A checkpoint A failed is a `FAIL`, and one A skipped is a `SKIP`.
- B logged no exceptions and no unknown-event errors, and received no event 151.
- A sent no event 151 (with a vanilla host, net.pose has no peers).

The remaining checks are by eye: the screenshots for how A's held items and arms look on B's screen, and B's `rpc` lines in `b-observer.jsonl` for emote, ping, eat and feed.

## What the sandbox does not cover

- A vanilla client or someone else's lobby: the sandbox is inactive there on purpose.
- Mobs and hazards that exist only inside map prefabs (spiders, jellyfish, lava, geysers): reach them by `Sandbox.Jump` to the right segment.
- Free camera in VR: refused.

## Known gaps

- Spawn paths marked "assumed" above have not been run in game.
- `Sandbox.Mob` for prefabs found by the deep scan is a plain network instantiate; some need extra setup the game does after spawning.
