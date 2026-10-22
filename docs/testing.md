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
| `Sandbox.Revive`, `Sandbox.Warp` | Revive where you died (passed out: where you lie); warp to spawn |
| `Sandbox.Jump <Segment>` | Beach, Tropics, Alpine, Caldera, TheKiln, Peak |
| `Sandbox.SetTime <hours>` | 0 to 24 (sunrise 5, sunset 21) |
| `Sandbox.FreeCam` | Game fly camera, flat play only |
| `Sandbox.Stamina <current> <extra>`, `Sandbox.MaxStamina <max>` | Set the stamina bar and the bonus bar; max stamina is set through Hunger |
| `Sandbox.AddStatus <Type> <amount>`, `Sandbox.ClearStatus <Type>` | Add a status the game's way (Cold, Poison, Injury...), or set one to 0 |
| `Sandbox.PassOut` | Pass out (the game's own command); `Sandbox.Revive` wakes you |
| `Sandbox.RunSetting <Setting> <value>` | Set a run setting for this run, not saved; `HelpingHand 1` is kick mode. Marks the run custom |
| `Sandbox.GiveLeft <Item>`, `Sandbox.GiveRight <Item>` | Give an item into that VR hand |
| `Sandbox.Wall` | Place a climbable test wall 3 m ahead |

The stamina, status and pass-out commands work offline only and turn god mode off first, because god mode would undo them.

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
| `use.<name>` | One item per category (food, tools, rope, lights, launchers) reaches the hand and is used for its own use time (`Item.usingTimePrimary`, plus the margin below) or until its cast finishes: the game must start its primary use (`Item.isUsingPrimary`) or take it from the hand. Items are picked by exact name first (`Compass`, not `Cheat Compass`, whose use warps to Caldera). The rope spool ties off instead: in VR it is first aimed at open sky, where the trigger press must get the NoWall refusal cue, then at ground within `RopeTier.maxAnchorDistance` of the body (the hand's aim in VR, the camera in flat play), where holding use for `castTime` must anchor the rope (`Rope.attachmenState` anchored). Then dropped |
| `04-beach` | The airport kiosk loads the first level with a local character |
| `05-mob-scan`, `mob.<name>` | Each entity from the deep scan spawns, is watched for 3 s and filmed, then removed. Before each step the player's statuses, afflictions and screen statuses (BLIND) are cleared, and the step fails if the player is still blinded. Afterwards new AOEs, ghost ball fields and items are removed. A plain spawn must show a sign of life: it moved or came 0.3 m closer, targeted the player, blinded them, grew, or removed itself. A `MagicBeanVine` gets the `RPC_GrowVine` a planted bean sends, so it grows; its growth is read from the vine's own length (the origin's y scale) for as long as 0.2 m takes at its `growingSpeed`. A `MushroomZombie` sleeps until looked at, gets up for its `initialWakeUpTime` and idles 2 s, and gets that long to target the player. A `RavenVoiceThief` is assigned the player and set stealing, as the gloom does, and must hold the theft. Names with `Flag_`, `Rope` or `_Placed` only have to spawn. Entities that cannot be spawned alone are made in their game context instead (below) |
| `06-segment-<Segment>` | `Sandbox.Jump` to every segment; the segment the player is already in passes when it is the current one. After a jump `MapHandler` must report the target segment (`TheKiln` for `Peak`: the game keeps the summit in the Kiln's slot). The game's Peak target (`MapHandler.respawnThePeak`) has had no ground under it, so when nothing lies within 20 m below it the step stands the player on the ground under the Peak progress point instead. The landing must have terrain within 2.5 m under the torso and be grounded within the time a fall to the ground found under the player takes (at least 4 s). The VR view must sit within 0.8 m of the avatar's head and outside terrain; while the view is detached from a ragdolled body (`Camera.DetachOnRagdoll`) only the terrain check applies |
| `07-time-<hh>` | Time of day 06, 12 and 21, run outdoors in Caldera with the shot aimed 20 degrees up at the sky. The clock must sit within 0.5 h of the hour and the sun pivot (`DayNightManager.earth`) must turn 10 degrees or more from the previous time |

Waits that stand in for a game duration read it from the item or object (`GameTiming` in `src/PeakXR/Sandbox`): use time, use on a friend, interact hold (`GetInteractTime`), zombie wake-up, vine growth and fall time. Each wait gets the duration plus a quarter, 1 s, and the scripted setup before it (`SelfTestChecks.WaitBudget`), so a slow item gets more time than a fast one.

Entities made in their game context:

| Step | How |
|---|---|
| `mob.Bingbong_*` | No game code spawns them (`BingBongPhysics.DoEffect` is empty). Spawned with their `bingbong` transform set 5 m behind the player, looking at them; the effect must follow it, push or pull the body, and remove itself after 2 s |
| `mob.ChainShootable` | The chain shooter turns until its own `WillAttach` accepts the terrain ahead, fires, and the chain must have colliders; `JungleVine.Interact` must start a vine climb |
| `mob.ScoutmasterGhostOrbiter` | Spawned 4 m away with `SetOrbitCharacter(local)`, the soul pillar's call; it must reach the player within 2 s |
| `mob.TiedBalloon` | A balloon item used from the hand ties a balloon (`CharacterBalloons.TieNewBalloon`); popping it unties it |
| `mob.Tornado` | After the first segment jump that reaches a running `TornadoSpawner`: its own `SpawnTornado`; the tornado must move and catch the player warped beside it (VR: the comfort vignette pulses once caught; pulses before the capture do not count). When today's map has none, the very last steps go back to the airport, pick a Mesa level with the game's `MapDebugUI.SetMapNumber` (`08-mesa-level`), jump its segments (`08-mesa-<Segment>`) and try there; the run ends on that level. `SKIP` when no level has one |
| `mob.MushroomZombie_Player`, `mob.PetrifiedScout`, `mob.PlayerGhost` | Last in the run. `Character.Zombify`, petrify 100 plus `Character.Die`, and `Character.Die`; the zombie, statue or ghost must appear, then `RPCA_ReviveAtPosition` revives the player where they stood, which must remove the zombie and the ghost (VR: the fade clears, no spectate nameplate for your own body). The solo game over (`Character.CheckEndGame`) is held back meanwhile |

VR runs only:

| Step id | Runs | Checks |
|---|---|---|
| `vr.00-session` | after `00-title` | `VRSession.Active` within 60 s; the note has the running runtime, eye texture size, stereo mode and the XR device names |
| `vr.01-rig` | title | `VRRig.Instance` and its tracked head (note says moving or static), `MainCamera` camera renders stereo to both eyes, an enabled `AudioListener` within 0.5 m of the head |
| `vr.02-title-stage` | title | The title menu canvas is world space and the title stage holds `VRRig.OriginOverride` (when `UI.TitleOnBeach` is on), and at least one button group sits within `UI.TitleReach` (+0.25 m) of the stage eye; screenshot `vr-02-title` |
| `vr.03-canvases` | airport | No active root canvas that ui.world converts is still screen space (its own Hidden and Fade rules are exempt); the note counts world and overlay canvases; screenshot `vr-03-airport` |
| `vr.04-settings-panel` | airport | The pause menu with the VR tab is world space, 0.5 to 4 m from the head and under 60° off the view; screenshot `vr-04-settings` |
| `vr.70-passport` | airport | `PassportManager.ToggleOpen` opens the passport (ui.passport places it at the hand); screenshot `vr-70-passport`, then it closes |
| `vr.71-kiosk` | airport | The check-in kiosk opens its boarding pass (`Interact_CastFinished`), docked at the kiosk by ui.lobby; screenshot `vr-71-kiosk` from 1.2 m in front of it, then every window closes |
| `vr.72-keyboard` | airport | The VR keyboard opens as a platform text dialog (`VRKeyboard.TryOpenDialog`); screenshot `vr-72-keyboard`, then the dialog is cancelled |
| `vr.05-loading-space` | after `04-beach` | During that load the loading space was shown and the game camera's `cullingMask` was 0; screenshot `vr-05-loading` mid-load. SKIP when `UI.Loading.LoadingSpace` is off (the load has already run, so the step cannot turn it on) |
| `vr.06-features` | beach | Every VR feature is `Active`, the local avatar driver has a fresh pose, the HUD canvas is world space, `VRFade.Target` is 0; records whether the vignette ring exists yet |
| `vr.46-lunge` | beach, after `vr.44` | At a wall like `vr.15`'s with 2.8 m of face above the grip (the jump rises about 1.4 m; on a lower wall the body tops out and there is nothing to catch), one gripping hand climbs and pulls up, then is thrown straight up at twice `Interaction.LungeSpeed` (at least 4 m/s) and lets go: the game's climb jump must fire (`RPCA_ClimbJump` resets `sinceClimbJump`), the same hand must catch the wall again inside the 0.6 s lunge grace, and the climb must not have ended in between. Then, god mode off and stamina at 0 for 0.6 s, the same throw must get the NoStamina refusal cue on that hand and no climb jump. The lunge also plays the whoosh feel on the throwing hand, and the climb must stay unbroken between the lunge and the catch; screenshot `vr-46-lunge` |
| `vr.33-ragdoll-arms` | beach, after `vr.09` | A 2 s fall (`RPCA_Fall` on the local character) releases the tracked arm IK (`AvatarPoseDriver.ArmWeight` 0 within 1 s). While the body falls, each ragdoll hand stays within 0.9 m of the chest and each tracked hand target within 1.5 m of the hip, even with the view detached. The arms are tracked again within 6 s of the fall ending; screenshot `vr.33-ragdoll-arms` |
| `vr.07-comfort` | beach, after the mobs | A knockback (`RPCA_AddForceAtPosition` on the local character) pulses the comfort vignette, which opens again within 2 s; a warp to the current segment leaves `VRFade.Target` at 0; screenshot `vr-07-knockback` |
| `vr.34-blind` | beach, after `vr.07` | BLIND for 5 s (`Character.illegalStatusAction`): the game's white-out quad is never drawn and the haze that replaces it peaks above 0.1 and at most at `ComfortPolicy.MaxWashAlpha`. Every `mob.*` step that leaves the player blinded checks the same; screenshot `vr-34-blind` |
| `vr.10-grab-right`, `vr.11-grab-left` | beach, after `vr.07` | An Apple spawned 0.45 m ahead at chest height; that hand reaches it in 0.5 s and grips. The character holds it within 3 s, in that hand (`LocalHold` and the avatar pose). A slow release keeps it, or drops it with `Interaction.ReleaseToDrop`; screenshot `vr-10-grab-right` / `vr-11-grab-left` |
| `vr.12-eat` | beach | The hand holding a given Apple, steered to the mouth point, eats it: the game's `Consume` RPC stamps `CharacterData.lastConsumedItem` within the use time plus 6 s. God mode is lifted and the status the item changes is set first (Hunger from `Action_RestoreHunger` or `Action_ModifyStatus`, else the item's largest `Action_ModifyStatus`): 0.5 for a restore, 0 for an added status. Eating must move it the right way by at least half the change. The note has the grip's and the item's closest approach (the item stays outside the holder's own head collider), the use progress and the status before and after; screenshot `vr-12-eat` |
| `vr.13-throw` | beach | A held Apple swung forward at 6 m/s and let go mid-swing leaves the hand and lands 2 m or more away. Its launch speed is within the throw clamp (hip speed plus half `maxThrowForce`) and its range within what that speed allows; screenshot `vr-13-throw` in flight |
| `vr.14-two-hand` | beach | A two-hand item (`RopeSpool`, else `Binoculars` or `RopeShooter`) in one hand; the free hand grips it and `AvatarPose.TwoHandHold` turns on, and off again on release; screenshot `vr-14-two-hand` |
| `vr.15-climb` | beach | Warps to the nearest steep terrain face and faces it. The search casts from the player and rings 15 m and 35 m around them; with nothing there, from every suitcase and campfire of the active segment, so it does not depend on the day's map. A gripping hand on the surface starts the climb (`CharacterData.isClimbing`), pulling the hand down 0.4 m lifts the body at least 0.1 m, and letting go ends the climb. Then a warp back to spawn; screenshot `vr-15-climb` |
| `vr.16-snap-turn` | beach | Turn stick right, then centred: the rig turns by `Comfort.SnapTurnAngle` (with smooth turning, by more than 5°); screenshot `vr-16-snap-turn` |
| `vr.17-move` | beach | Move stick forward for 1 s walks the character at least 0.5 m; screenshot `vr-17-move` |
| `vr.18-emote-wheel` | beach | Holding the emote button (off hand, upper button) opens the game's emote wheel without its dimmer. Moving the hand up onto the ring hovers a slice; a pointer ray at the empty centre or past a corner hits nothing of the wheel, one at a slice's drawn wedge hits that slice; releasing closes the wheel and plays the emote; screenshot `vr-18-emote-wheel` |
| `vr.19-laser` | beach | Pause menu open, both aim rays on its Resume button, a trigger pull: the menu closes; screenshot `vr-19-laser` |
| `vr.20-holster` | beach | A held Apple taken to its body slot and let go goes into that hotbar slot; a grip there draws it back into the same hand; screenshot `vr-20-holster` |
| `vr.29-ground-backpack` | beach | A grip on a spawned ground backpack opens its wheel, which stays open every frame while the grip is closed; opening the grip closes it and the move stick walks again; screenshot `vr-29-ground-backpack` |
| `vr.32-shoulder-stash` | beach | With a pack worn and an Apple grabbed in one hand (grip kept closed), the other hand over the shoulder opens the pack wheel, hovers a pocket, and opening that grip stashes the Apple and closes the wheel; screenshot `vr-32-shoulder-stash` |
| `vr.64-cue-in-menu` | beach, pause menu | With the pause menu open, a NotNow refusal at the right hand gets the full cue twice: once through the game's SFX player and once through PeakXR's own fallback sound source, forced for the second pass. Each pass needs the ring, the refused note played and the denied pulse |
| `vr.65-hints-off` | beach | With `UI.ActionHints` and `UI.ActionHintText` off, the trigger on a rescue hook held in one hand still gets the whole NeedsTwoHands cue, with the hook red. No teal ring, rising note, reach ray or text label may show. Both settings go back to their earlier values; screenshot `vr-65-hints-off` |

More VR steps, grouped by area rather than by run order. The end-of-run slot is last. They all run on the scripted hands described below.

#### Climbing

| Step id | Runs | Checks |
|---|---|---|
| `vr.42b-rope-pump` | beach, after vr.56 (before vr.42c) | Hangs a rope, grips it, measures sway with a still head, then sways the head 0.25 m at 0.35 Hz. Asserts ClimbHands.LastPump above 0, pumped swing at least 0.1 m beyond still swing, no refusal cue. SKIP if RopeHandOverHand or RopeSwingPump off. |
| `vr.42d-pump-not-mine` | beach, after vr.42b | Hangs and grips a rope and treats it as someone else's (offline, every rope belongs to the player), then sways the head as in vr.42b. Asserts ClimbHands.LastPump stays 0 and the gripping hand gets exactly one NotHere cue. SKIP if ui.affordance is not installed, or RopeHandOverHand or RopeSwingPump is off. |
| `vr.42c-rope-end-stop` | beach, after vr.42b | Grips the test rope, pokes ropePercent to 0.02, pulls the hand down 0.4 m. Expects the EndStop feel on the hand. SKIP if Interaction.RopeHandOverHand off. |
| `vr.43b-vine-look-back` | tropics, after vr.43 | Attaches the vine via the game's Interact, turns the rig 180 degrees, grips and pulls the hand back along the vine 0.4 m. Asserts the body moves at least 0.1 m along the pull (ClimbHands re-signs GetSign) and no cue. SKIP if RopeHandOverHand off. |
| `vr.43c-vine-slide` | tropics, after vr.43b | Attaches the vine, pokes CharacterVineClimbing.attachVel to 10 so Sliding() is true. Asserts the ClimbCue.Whirr cue reaches the main hand within 1 s. SKIP if RopeHandOverHand or ClimbSurfaceFeel off. |
| `vr.44b-handle-rest` | beach, after vr.54 | Hangs on a placed piton, sets stamina to 0.3. Expects the Progress feel on the gripping hand within 1.5 s. SKIP if PitonGripHang off, or if the piton cracked first (vr.56 covers it). |
| `vr.46b-lunge-too-soon` | beach, first of ClimbSteps, after vr.46 | Grips a tall wall, pokes sinceClimbJump to 0.2, throws the hand upward. Expects the TooSoon refusal cue at that hand (no tint) and no climb jump (clock not reset). SKIP if ArmLunge off. |
| `vr.47-climb-deny-reach` | beach, after vr.46b | Stands 1.6 m from the face and grips a hold beyond 1.25 m of the eye. Expects the OutOfReach cue and no climbing. SKIP if PhysicalClimb off, or if the head ended within 1.35 m of the face. |
| `vr.47b-free-hand-pocket` | beach, after vr.47 | Gives an Apple in the right hand, spawns a second on the ground, grips it with the left. Asserts it leaves the world (pocketed), the Stowed cue fires, and the first Apple stays in the right hand. SKIP if the give fails. |
| `vr.48-climb-deny-steep` | beach, after vr.47b | Places a 40 degree slab, then a ceiling tilted 5 degrees. Asserts no chalk ring on the slope, TooFlat then Overhang cues on the grips, and no climbing on either. SKIP if PhysicalClimb off. |
| `vr.48b-surface-texture` | beach, after vr.48 | Adds a ClimbModifierSurface (stamina use 0.3) to the wall, grips it. Asserts the ClimbCue.Texture tick reaches the hand within 2 s, and no deny cue. SKIP if Interaction.ClimbSurfaceFeel off. |
| `vr.49-condor` | beach, after vr.48b | Pokes a stand-in Condor into CharacterData.carrierCondor, grips the wall. Expects the Carried cue and asserts no climbing. No SKIP besides the wall being missing. |
| `vr.50-climb-hover` | beach, after vr.49 | Open hand at the wall. Asserts the ClimbHover chalk ring is drawn and a Contact feel arrives with no deny cue; then a grip must climb and the ring must go. SKIP if Interaction.ClimbHoverRing off. |
| `vr.51-air-grip` | beach, after vr.50 | Warps the player 1.5 m above the ground at the wall, grips mid-air. Asserts the grip climb starts (isClimbing) and no deny cue. SKIP if the player landed before the grip. |
| `vr.51b-falling-catch` | beach, after vr.51 | Drops the player 2.5 m, waits for fall speed over 3 m/s, grips the wall. Asserts climbing starts and the ClimbCue.Catch feel is at least the ClimbFeel.CleanCatch amplitude. SKIP if ClimbSurfaceFeel off. |
| `vr.52-climb-empty` | beach, after vr.51b | Grips the wall, empties both stamina bars, pulls the hand down 0.4 m. Asserts the body rises under 0.1 m and the NoStamina cue (with the stamina bar red) fires. SKIP if stamina cannot be set. |
| `vr.53-forced-detach` | beach, after vr.52 | Grips the wall, then calls StopAnyClimbing while the hand still grips. Expects the Cancel feel on that hand; reports a note only, no failing assertion beyond the feel. |
| `vr.54-mantle` | beach, after vr.53 | Builds a 1.7 m Map-layer ledge, climbs hand over hand. Asserts the climb starts, PhysicalInteraction.LastMantleAt marks a mantle, feet end within 0.3 m of the top, and a Contact settle tick follows. SKIP if PhysicalClimb off. |
| `vr.55-handle-occupied` | beach, after vr.44b | Sets ClimbHandle.hanger on a placed piton to a stand-in Character, grips it. Expects the Occupied cue (piton red) and asserts neither hanging (currentClimbHandle) nor wall climbing happens. |
| `vr.56-fragile-piton` | beach, after vr.55 | Hangs on a hammered ShittyPiton. Expects the Danger feel once it cracks (within 8 s), then waits for the break and expects the Cancel feel. SKIP if PitonGripHang off or the piton has no ShittyPiton. |
| `vr.57-rope-deny-stamina` | beach, after vr.42c | Hangs a rope, empties stamina, grips the rope. Asserts the NoStamina cue (tinted) fires, and neither isRopeClimbing nor wall climbing starts. SKIP if stamina cannot be set. |
| `vr.58-rope-deny-unanchored` | beach, after vr.57 | Pokes Rope.attachmenState to unattached, grips a rope segment. Asserts the Unanchored cue (rope tinted), no rope climb and no wall climb. Fails if the rope has no segments. |
| `vr.58b-breakable-anchor` | beach, after vr.58 | Adds BreakableRopeAnchor to the test rope's anchor if missing, grips the rope. Expects the Danger feel within 9 s of the climb start. SKIP if RopeHandOverHand off. |
| `vr.59-vine-deny-stamina` | tropics, after vr.43c | Empties stamina, grips a point on the vine. Asserts the NoStamina cue (tinted) fires, and neither isVineClimbing nor wall climbing starts. SKIP if stamina cannot be set. |

#### Body and movement

| Step id | Runs | Checks |
|---|---|---|
| `vr.140-roomscale-blocked` | VR run, MovementSteps | With Locomotion RoomScaleWalking off, the head scripted 0.9 m backward gives NotNow cue on both hands, with a ring at the feet (LocomotionInput.WarnFar), before the 1 m fade. SKIP: player.locomotion not installed. |
| `vr.141-forced-look-side` | VR run, MovementSteps | Writing lookValues.x +40 degrees each frame (as a frog tongue does) gives ForcedLook comfort rumble with right hand amplitude above left. Records LookPatches.ScriptedLookWeight. SKIP: comfort haptics off. |
| `vr.142-sprint-out` | VR run, MovementSteps | Stick sprint sets isSprinting with no wrist cue. Emptying stamina mid-sprint stops it and fires NoStamina with tint on the move-stick hand. SKIP: sandbox stamina control unavailable. |
| `vr.154-jump-out` | VR run, MovementSteps | A jump press with stamina near 0.006 takes off (BodyFeedback.TakeOffs rises), then the move-stick hand gets the NoStamina cue with tint. SKIP: sandbox stamina control unavailable. |
| `vr.143-crouch-walk` | VR run, MovementSteps | Head scripted to 40% of standing height sets isCrouching and gives a Contact feel on both hands. Crouched 1 s walk covers 0.3 to 0.75 of the standing distance. Standing up gives Contact again. SKIP: PhysicalCrouch off or seated. |
| `vr.144-jump` | VR run, MovementSteps | Jump press takes off within 0.3 s with a Contact feel on the dominant hand. A second press before the game's cooldown does not jump and fires TooSoon on the dominant hand. |
| `vr.155-hop-jump` | VR run, MovementSteps | With Locomotion.HopToJump on: standing up from a crouch, and look down then up with arms lifting, do not take off. Arms up with head 0.1 m above standing takes off, with Contact on the dominant hand. SKIP: not installed or seated. |
| `vr.158-arm-swing-still` | VR run, MovementSteps | With the arm swing setting on, swinging with the stick idle or sideways with stick forward does not sprint (isSprinting with BodyGestures.Swinging). A forward running swing with stick forward does. SKIP: player.locomotion not installed. |
| `vr.145-landing` | VR run, MovementSteps | A 12 m warp drop, god mode off: BodyFeedback.Landings rises, Danger feel on both hands before landing, Contact feel after, and LastLanding amplitude at least 0.6. SKIP: sky blocked within 13 m above. |
| `vr.146-slope` | VR run, MovementSteps | Warping onto terrain of 58 to 75 degrees (no CollisionModifier) gives the TooSteep cue on both hands. SKIP: no such slope within 30 m. |
| `vr.148-ragdoll-getup` | VR run, MovementSteps | RPCA_Fall for 2 s: when currentRagdollControll passes 0.9, both hands get a Contact feel, and the move stick then walks at least 0.3 m in 0.8 s. |
| `vr.149-knockback-side` | VR run, MovementSteps | RPCA_AddForceAtPosition from the right gives a comfort rumble (ComfortEventsRuntime) with right amplitude above left. SKIP: comfort haptics off. |
| `vr.150-pre-pass-out` | VR run, MovementSteps | Adding Injury 1 raises passOutValue and gives a Danger feel on both hands within 3 s, before the pass-out. Status then cleared and revived if passed out. SKIP: game refused the status. |
| `vr.147-morale-glow` | VR run, MovementSteps | StaminaBar.PlayMoraleBoost(2) is handled by MoraleBoostTextPatch, the MORALEBOOST text stays disabled, and the WouldWork chord sound plays after the mark. SKIP: no stamina bar. |
| `vr.153-spectate-single` | VR run, MovementSteps | Passed out solo, a spectate stick flick gives CantSwitch on the hand bound to VRAction.SpectateNext. Then revives. SKIP: not solo, or sandbox pass-out unavailable. |
| `vr.157-spectate-point` | VR run, MovementSteps | Passed out solo, dominant hand pointing straight up with trigger pulled gives CantSwitch on that hand (PointToWatch). Then revives. SKIP: not solo, or sandbox pass-out unavailable. |
| `vr.156-head-near-wall` | VR run, MovementSteps | With RoomScaleWalking off and a sandbox wall ahead, leaning the head to within 0.12 m of it gives a Contact feel on both hands. Records RigDriver.EyeGuardBlack in the note only. SKIP: not installed, or no wall. |
| `vr.159-amulet-pips` | VR run, MovementSteps | With totalExtraJumps 2: AmuletPips shows 2 beads, 2 lit. An air jump raises BodyFeedback.AirJumps, drops lit beads to 1, and gives a Contact feel on both hands. |
| `vr.210-heartbeat` | VR run, MovementSteps | Spending stamina from 0.25 gives BodyFeedback beats with a measured period. Under the Weight 0.5 status the beat period is longer than without it. SKIP: sandbox stamina control unavailable. |
| `vr.211-shiver` | VR run, MovementSteps | Cold raised in four 0.05 steps gives at least 5 BodyFeedback.Shivers pulses. Held steady for 1 s gives none. SKIP: game refused Cold. |
| `vr.151-wind-pressure` | VR run, MovementSteps | Inside an active WindChillZone, ComfortEventsRuntime reports a pressure push within 3 s, with no hand above BodyCues.PressureMax. SKIP: no active wind zone, or no push (sheltered or calm). |
| `vr.212-tornado-brace` | VR run, MovementSteps | Within a Tornado's range, pressure on the hand is positive standing and lower after the head is scripted to crouch. Also fails when RoomScaleSetting is null. SKIP: no tornado reaching the player. |
| `vr.152-bounce` | VR run, MovementSteps | Warp 2.5 m above the nearest CollisionModifier with knockback within 60 m: EnvironmentCues.Bounces rises and both hands get a Contact feel. SKIP: no bounce shroom within 60 m. |
| `vr.213-lava-sting` | VR run, MovementSteps | Calling Lava.HitPlayer on the local body (god mode off) raises EnvironmentCues.Stings and gives a Danger feel on both hands. SKIP: no Lava in the level. |
| `vr.214-balloon` | VR run, MovementSteps | TieNewBalloon gives a Contact feel on the item hand. A TiedBalloon appears; Pop() raises EnvironmentCues.Pops and gives a Contact feel on the other hand. |
| `vr.215-parachute` | VR run, MovementSteps | CharacterBalloons.SetParachute(true) raises EnvironmentCues.Tugs and gives a Contact feel on both hands. Parachute closed afterwards. |
| `vr.216-sugar-crash` | VR run, MovementSteps | GUIManager.StartSugarRush then EndSugarRush gives a Cancel feel on both hands. SKIP: no GUIManager. |
| `vr.217-float-end` | VR run, MovementSteps | Adding Affliction_LowGravity (2.5 s) gives a Progress feel on both hands within 4 s, the warning before gravity returns. |
| `vr.218-bridge` | VR run, MovementSteps | Standing on an owned BreakableBridge with its timer and isBreaking forced gives a Danger feel on both hands. SKIP: no owned bridge within 80 m, or warp missed the planks. |

#### Item gestures

| Step id | Runs | Checks |
|---|---|---|
| `vr.160-use-routes` | VR run, ItemGestureSteps | For each listed route item, given and held, ItemGestures.UseOf(held) equals the expected UseGesture. Items absent from the game version are listed only. SKIP: none of the route items exist. |
| `vr.161-drink` | VR run, ItemStep Antidote | Hand raised to the mouth casts the drink to completion (lastFinishedCast), the status it lowers (Action_ModifyStatus) falls by at least half its change, and no refusal cue fires. SKIP: EatAtMouth off. |
| `vr.162-build-not-at-mouth` | VR run, ItemStep Checkpoint Flag | Held at the mouth for use time plus 1 s, the flag starts no use (isUsingPrimary false), castProgress stays 0, and the item stays in hand. |
| `vr.163-build` | VR run, ItemStep Checkpoint Flag | Aimed at open sky the trigger gives OutOfReach. Aimed at ground in reach, holding the trigger spawns a CheckpointFlag and the item leaves the hand. SKIP: ui.affordance not installed. |
| `vr.164-commune-guard` | VR run, ItemStep Cursed Skull | Bare trigger gives DangerSoon and starts no cast. Held at the face the cast reaches 0.4 with AtFace and a Progress feel. Pulled away gives a Cancel feel, player alive, skull held. SKIP: CommuneAtFace off, or cast under 0.5 s. |
| `vr.165-read-cancel` | VR run, ItemStep Book of Bones | Held at the eyes the cast reaches 0.3 with HeldGestures.AtFace. Lowered, it gives a Cancel feel, isSkeleton stays false, book still held. SKIP: ReadAtFace off, already skeleton, or cast under 0.5 s. |
| `vr.166-eat-cancel` | VR run, ItemStep apple fragment | Food at the mouth casts to 0.5, pulled away gives a Cancel feel. lastConsumedItem unchanged, item still held, Hunger not lowered more than 0.02. SKIP: ui.affordance not installed, or cast under 0.5 s. |
| `vr.167-secondary-deny` | VR run, Items steps | With a must-use-on-friend food held and nobody in front, off-hand trigger gives NeedsFriend with tint on the off hand and starts no use or cast. SKIP: ui.affordance not installed, or no such item. |
| `vr.168-shoot-hand` | VR run, ItemStep RopeShooter | Aimed at open sky the trigger gives NoWall. Aimed at terrain or the test wall within maxLength, holding the trigger spawns a RopeAnchorProjectile with no refusal cue. SKIP: ui.affordance not installed. |
| `vr.169-zoom-endstop` | VR run, ItemStep Binoculars | In forced Hold mode with the trigger held, binocularsActive turns on. Twisting the hand 270 degrees per second drives lerpedFOV to maxFov and gives one EndStop feel. SKIP: ui.affordance not installed, or TwistZoom off. |
| `vr.25b-cook-danger` | VR run, ItemStep apple fragment, after vr.25 | Food set to cook level 2 (one more cook burns it) is held over the lit fire. The cook hold gives a Danger feel on the food hand, then the hand pulls back and the cook level stays 2. SKIP: no lit campfire, ui.affordance not installed, or CookOverFire off. |
| `vr.170-throw-mid-cast` | VR run, ItemStep apple fragment | The trigger starts the eat cast, then the hand swings and lets go as in vr.13. The release gives TooSoon with the item red, the item stays in the hand and PhysicalInteraction.ThrowCount does not change. SKIP: ui.affordance not installed, or cast under 0.5 s. |
| `vr.171-vine-no-path` | VR run, items steps | A vine or chain shooter turned until WillAttach passes fires with every path try forced to fail. The cast gives NoPath with the shooter red and no new JungleVine. SKIP: ui.affordance not installed, no shooter item, or no terrain in its window. |
| `vr.172-warp-refund` | VR run, items steps | The warp compass held on the trigger with nobody alive beyond its minimumDistance gives NeedsFriend with the compass red when the game refunds the use, and the player moves less than 2 m. SKIP: ui.affordance not installed, no compass item, or someone to warp to. |

#### Friends

| Step id | Runs | Checks |
|---|---|---|
| `vr.100-thorn-pluck` | SocialSteps, solo sandbox, VR hand step, god mode off | Thorn over 25 degrees off camera aim: scripted hand pinches it, contact tick felt, pull 14 cm, thorn leaves stuckIn within game hold time, progress tick felt. SKIP if AddThorn yields no off-view thorn. |
| `vr.101-thorn-stuck` | SocialSteps, solo sandbox, VR hand step, god mode off | Foot stuck via Character.TryStickBodypart; pinch on a thorn gives DenyReason.Stuck cue on that hand, no tint, and the thorn stays after 1.5 s. Fails if foot cannot stick. SKIP if no thorn can be added. |
| `vr.102-tick-pluck` | SocialSteps, solo sandbox, VR hand step, god mode off | Spawned BugfixOnYou tick attaches to local player; left-hand pinch gives contact tick and the tick is removed within 3 s, hand holds the bug item. SKIP if the tick cannot spawn. |
| `vr.104-struggle` | SocialSteps, solo sandbox, VR hand step, god mode off | Web status 0.5 sets isStruggling; up/down shaking of both hands (up to 12 strokes) lowers the Web status, and a Progress feel is felt on the left hand. SKIP if AddStatus refuses. |
| `vr.105-give-up` | SocialSteps, solo sandbox, VR hand step, god mode off | After PassOut and fullyPassedOut, one grip gives DenyReason.NeedsTwoHands and interactIsPressed stays false for 20 frames; both grips plus both triggers hold interact and give a Progress feel. SKIP if PassOut refuses. |

#### Comms and menus

| Step id | Runs | Checks |
|---|---|---|
| `vr.110-ping` | CommsSteps, VR hand step, scripted hands | Right finger gun aimed at ground: PingPreview dot shown, not red, within 1.5 m of aim; squeeze updates PointPinger _timeLastPinged, Contact feel, no denial cue. SKIP if no PointPinger or interact.ping missing. |
| `vr.111-ping-refused` | CommsSteps, VR hand step, scripted hands | Sky aim gives red dot and NoWall cue with tint, no ping sent; second ping inside PointPinger.coolDown gives TooSoon; finger gun on a hand with unusable held item gives UseOtherHand on the free hand. SKIP as vr.110. |
| `vr.112-ptt` | CommsSteps, VR hand step, scripted hands | In PushToTalk with HandToMouthTalk on, hand at mouth sets pushToTalkPressed, plays Transmit click, shows MicGlyph.Open, Contact feel; hand away releases with closing click. In VoiceActivation it gives MicAlwaysOn. SKIP if no PushToTalkSetting or voice. |
| `vr.113-mute` | CommsSteps, VR hand step, scripted hands | With PalmMute on, palm over mouth in VoiceActivation sets VoiceInput.MuteHeld, stops voice.transmitting, shows MicGlyph.Muted, Cancel click and Cancel feel; reopens on leaving. In PushToMute it presses pushToTalkPressed. SKIP if no setting, voice handler or feature. |
| `vr.114-emote-refused` | CommsSteps, VR hand step, scripted hands | With the pause menu open, holding the emote binding gives NotNow and the wheel stays shut. After unpausing, wheel opens and a centre release gives a Cancel feel with no new denial. |
| `vr.115-gesture-emote` | CommsSteps, VR hand step, scripted hands | Open hand raised beside the head and waved at 1.5 Hz for up to 2.5 s makes CharacterAnimations emoting true and GestureEmotes.LastPlayed equal WaveAnim, with no refusal cue. SKIP if Emotes gestures setting off. Fails if no wave emote data. |
| `vr.116-disabled-control` | AirportScreenSteps, ScreenHandStep, scripted hands, boarding pass | Lower-ascent arrow clicked down until disabled; laser press on it gives DenyReason.Locked with tint on the pressing hand, and pass.ascentIndex is unchanged. SKIP if no kiosk, boarding pass or character. |
| `vr.117-pause-refused` | CommsSteps, VR hand step, scripted hands | With the emote wheel open, a left menu tap gives NotNow on the left hand, and GUIManager.InPauseMenu stays false. |
| `vr.118-passport-locked` | AirportScreenSteps, ScreenHandStep, scripted hands, passport | Laser press on a locked passport cosmetic gives Locked with tint (noted if none in save). With SwipeToFlip on, right-hand swipe on the last page gives an EndStop feel. SKIP if no PassportManager or character. |
| `vr.119-keyboard-type` | AirportScreenSteps, ScreenHandStep, scripted hands, VRKeyboard | Four-character dialog: poking keys with the right fingertip types "ab12"; a fifth key gives PackFull with tint and text unchanged; Done submits "ab12" with ok true. Fails if the keyboard will not open. |
| `vr.120-invite-offline` | AirportScreenSteps, ScreenHandStep, airport invite kiosk | Calling AirportInviteFriendsKiosk.Interact while not in a lobby gives DenyReason.Blocked on the nearer hand, tinted only if the kiosk is active. SKIP if no kiosk, or already InLobby. |
| `vr.121-prompt-glyphs` | CommsSteps via PromptGlyphSteps, VR hand step | With TargetPrompt on and ActionHintText off, hovered Apple hides the PICKUP text, draws the Grab glyph, keeps its name; ActionHintText on restores text, no glyph. With ActionHintText off again, a web draws the Struggle glyph, Contact feel both hands. Runs with TargetPrompt turned on; every setting it changes goes back after the step, also after a timeout. |
| `vr.123-ping-dotted` | CommsSteps, VR hand step, scripted hands | Right finger gun aimed at ground: PingPreview.RayDots at least 3, landing dot shown and not red, FromLeft equals hand; aiming gives no refusal cue. |
| `vr.124-button-ping-preview` | CommsSteps, VR hand step, scripted hands | Thumb resting on the ping button shows aim preview from that hand (ButtonAiming, DotShown); tap pings with Contact feel and no cue; long press hides preview while held and after release. SKIP if no PointPinger, or ping not on Secondary. |
| `vr.125-hold-glyph` | CommsSteps via PromptGlyphSteps, VR hand step | With ActionHintText off, gripping a closed suitcase holds it, draws the Hold ring and hides text; lifting lid halfway makes HoldFillShown track constantInteractableProgress within 0.1, Progress feel. Runs with TargetPrompt turned on; SKIP if no suitcase. |
| `vr.126-wheel-climbing-refused` | CommsSteps via WheelGapSteps, VR hand step | Warped to a wall and climbing, holding the emote binding gives DenyReason.NotWhileClimbing and the wheel stays shut. Fails if climbing does not start. SKIP if no wall found. |
| `vr.127-thumbs-up` | CommsSteps via WheelGapSteps, VR hand step | Fist forward, trigger touched, thumb off all buttons and up: emoting true, GestureEmotes.LastPlayed equals ThumbsUpAnim within 2 s, no refusal cue. SKIP if Emotes gestures setting off or no thumbs-up emote. |
| `vr.128-backpack-wheel-cancel` | CommsSteps via WheelGapSteps, VR hand step | Grip on a spawned ground Backpack opens the wheel (usingBackpackWheel); release with no pocket hovered gives Cancel feel, no denial added, and the pack is not worn. Fails if no Backpack item or open ground. |
| `vr.129-passport-try-on` | AirportScreenSteps, ScreenHandStep, scripted hands, passport | Right fingertip poke on an unlocked, unworn skin cell increments PassportPoke.Equips, changes the worn skin, and gives a Contact feel; skin restored. SKIP if no passport or no such cell. |
| `vr.130-connecting-wait` | AirportScreenSteps, ScreenHandStep, scripted hands, boarding pass | With the load flag faked on, laser press on Start gives DenyReason.NotNow with tint on the pressing hand. SKIP if no kiosk or pass, or Start not interactable. |
| `vr.131-modal-bell` | AirportScreenSteps, ScreenHandStep | Modal.TestModal opens a modal and, once placed, gives a NotNow cue within 1.5 s on the laser hand, without tint. |
| `vr.132-guide-comms-rows` | AirportScreenSteps, plain VRStep, no hands | GhostDemo on a stage plays guide topics Ping, Mic, Mute, Spectate for one loop each; each stays alive on its topic and plays at least one cue sound. Fails if no rig. |
| `vr.133-target-names` | CommsSteps via PromptGlyphSteps, VR hand step | Empty right hand reaches an Apple low beside you: it glows and, with TargetNames off, shows no name; the glow goes when the hand leaves. With TargetNames on, the Apple on the head's ray shows its name. Runs with TargetPrompt turned on. |

#### Spatial UI

Each step turns its `UI.*` setting on for the step and puts it back afterwards, also after a timeout, so a turned-off feature can no longer pass without being tested. A text check (`Readable`, rule in `PeakXR.Core.Legibility`) reads the text where the game draws it. The text has to be shown, not faded (alpha 0.5 or more) and not empty. Seen from the eye it has to be at least 0.25 m away, at most 45 degrees off the gaze, at most 60 degrees edge-on, and at least 0.4 degrees tall, the smallest legible size in Microsoft's mixed-reality typography guidance. Labels at a hand are judged with the gaze on that hand, and everything else with the gaze along the head's view. The note gives the measured size, distance and angles. The screenshots re-aim the camera (`ShotAt`), so a frame proves a label was drawn, not that the player could see it. That is decided from poses.

| Step id | Runs | Checks |
|---|---|---|
| `ui.01-target-prompt` | SpatialSteps, VR hand step | An Apple held on the head's ray 0.8 m out (the game reaches 2 m, Interaction.cs:7) becomes the hovered target. Its name group moves to `PeakXR_TargetPrompt`, within 0.6 m of the Apple, no farther from the eye than the Apple and at least 3 degrees above it. The name is drawn and readable. Runs with TargetPrompt and TargetNames on: by default a plain pickup's label draws nothing |
| `ui.02-button-labels` | SpatialSteps, VR hand step | A held Apple's use prompt moves to a `PeakXR_ButtonLabel` within 0.2 m of the hand, drawn on top and readable with the gaze on the hand, named TRIGGER, with its leader ending within 0.1 m of the hand. With no finger on a button the labels are even (use under 0.95, throw over 0.6). A finger on the trigger lights the use label (0.95 or more) and dims throw (0.6 or less). Runs with ButtonLabels on |
| `ui.03-world-banner` | SpatialSteps, VR hand step | `SetHeroTitle` with this biome's title puts the hero title on `PeakXR_HeroSign` within 0.75 m of where its own ray places it (4 to 30 m). The title image is readable along the head's view, and it is gone within 12 s. Runs with WorldBanners on; SKIP without a `DayNightManager` |
| `ui.04-rope-gauge` | SpatialSteps, VR hand step | A held rope spool shows `UI_Rope` under `PeakXR_RopeGauge`, its length text within 0.4 m of the spool and readable with the gaze on the spool. Runs with HeldItemGauges on |
| `ui.05-wrist-hud` | SpatialSteps, VR hand step | The off hand raised like a watch: the wrist HUD is posed, within 45 degrees of the view and at least 0.25 m from the eye, faces the eye within 25 degrees, its watch face within 50 degrees, its bar across the forearm (90 ± 15 degrees), at least 12 degrees wide, drawn on top, and shows a Cold slot after `AddStatus(Cold)`. SKIP when `UI.HUD.HudMode` is not Wrist |

#### World and items in the world

| Step id | Runs | Checks |
|---|---|---|
| `vr.80-campfire-far` | beach, world steps | A grip on the nearest unlit campfire with `moraleBoostRadius` shrunk to 0.05 m gets the FriendsFar cue with the logs red within 2 s, and the fire stays off. The radius is restored. The note has the eye-to-hand distance at the grip. `SKIP` with no unlit campfire |
| `vr.81-chest-two-hands` | beach, world steps | One hand on a closed respawn chest lid gets the NeedsTwoHands cue (red) within 2 s and the hold progress stays at 0.05 or below. Both hands lifting must raise progress or open the chest. `SKIP` with no closed chest or `Interaction.LiftLidToOpen` off |
| `vr.82-lid-progress` | beach, world steps | Lifting a closed suitcase lid logs a Progress feel, then a Contact feel at the open, on the dominant hand. `SKIP` with no closed suitcase or `LiftLidToOpen` off |
| `vr.82b-lid-low-grip` | beach, world steps | A grip 8 cm below the case centre gets the AimHigher cue (no tint) within 2 s, and the case stays the held interactable, so the vanilla hold is kept. `SKIP` with `UI.ActionHints` or `LiftLidToOpen` off |
| `vr.83-cook-cant-use` | beach, world steps | The first item whose cooking is disabled (not a light or a placeable) held in the heat column of a lit fire gets the CantUse cue with the item red within 0.5 s. `SKIP` with no campfire or `Interaction.CookOverFire` off |
| `vr.83b-cook-not-lit` | beach, world steps | An Apple held over an unlit campfire gets the NotLit cue (red) and the fire stays off |
| `vr.83c-cook-warmth` | beach, world steps | Food carried to 0.85 m out and 0.55 m up, short of the cook column, gives a Progress feel. Fails if the fire became the held interactable or the food cooked |
| `vr.84-book-of-bones` | beach, world steps | The Book of Bones hand held 0.4 m above a spawned skeleton's hips starts the revive hold: progress above 0 on the skeleton plus a Progress feel. `SKIP` with `Interaction.BookOverBones` off |
| `vr.84b-book-used-up` | beach, world steps | The same setup with the book's uses at 0 gets the UsedUp cue (red) within 0.5 s and no hold progress |
| `vr.87-fuse-lit` | beach, world steps | A lit torch carried to a ghost fire or a cannon or rocket fuse that cannot be lit again gets UsedUp (or Occupied), red, within 0.5 s. `SKIP` with none loaded |
| `vr.88-scout-cannon` | beach, world steps | Standing 2 m from an unlit scout cannon, `Light()` gives a Progress ramp within 1.5 s. After the shot, a notice, a Contact tick and the notice note follow |
| `vr.92-checkpoint` | beach, world steps | Planting a checkpoint flag with the hand aimed at the ground, then `Character.TryCheckpoint()` succeeds, a notice shows, a Contact feel fires and the notice sound sits within 0.5 m of the flag |
| `vr.94-effigy-nobody-down` | beach, world steps | A scout effigy with nobody down cannot be used: the trigger gets the NotInTrouble cue (red) and the effigy stays in hand. `SKIP` with `Interaction.GuardWastedItems` off or someone down |

#### End-of-run slot

These steps run last in the VR run, after `TornadoTrip` (which may load another level) and the `06-segment-Peak` jump. The slot is skipped unless the run is VR, the game is ready and a `MapHandler` exists. It ends with `vr.86-end-screen`, which returns to the airport, where the summary and quit run as after any step.

| Step id | Checks |
|---|---|
| `vr.85-campfire-segment` | A bare-hand grip on the nearest unlit campfire that advances the segment lights it for real, with no refusal cue on that hand. Run flow must arm its segment fade within 3 s; while the swap is pending (up to 60 s) and 1 s after, the drawn fade (`FadeView.Alpha`, not just the requested `VRFade.Target`) must reach 0.5 when `UI.FadeOnSegmentChange` is on, and the segment must advance. `SKIP` with no unlit campfire ahead |
| `vr.89-to-peak` | Jumps the run to the Peak segment (screenshot `vr-89-peak`) |
| `vr.89-altar-full` | With every altar slot filled locally, the held amulet carried up to a socket gets the Occupied cue (red) within 2 s; a finished vanilla hold (`Interact_CastFinished`) is refused the same way, and the amulet stays in hand after 1 s. `SKIP` with `GuardWastedItems` off |
| `vr.89b-amulet-socket` | With the altar emptied locally, an amulet carried to the socket starts the interact hold (progress above 0) plus a Progress feel. `SKIP` with `Interaction.AmuletInSocket` off |
| `vr.90-soul-pillar` | Both open palms on the soul pillar start its hold (progress 0.3 or more), then are withdrawn; a Progress feel must be logged. `SKIP` with no pillar or `PalmsOnPillar` off; the note says whether an inactive pillar is in the scene and whether the level has the Nadir (`VoidBiome`) |
| `vr.91-portal` | A hand pushed into the summit portal starts the hold (progress 0.3), then is pulled out; the character is not dead and no loading screen shows. `SKIP` with no active portal or `HandIntoPortal` off; the note says the same for the portal |
| `vr.93-summit-flare` | A lit flare held when the helicopter is summoned (the step sends the RPC itself if it must): a notice shows, a Contact feel fires and the notice sound plays |
| `vr.86-end-screen` | Forces a win, laser-presses Next on the end screen. A forced win must roll the credits. During credits an early press gets TooSoon, both hands feel Contact once the skip delay has passed, then the press must start the airport load within the game's 0.5 s hold plus the margin (the credits end by themselves 80 s in, so reaching the airport alone proves nothing) |

The existing `use.<name>` steps now check the real effect: food must change `lastConsumedItem` and lower Hunger by half the item's change, lanterns and torches must be lit with fuel falling, flares lit and tracked, rope and chain launchers must spawn their projectile or vine, and binoculars must raise and lower in `Hold` mode.

Screenshots in VR show the desktop mirror, which rig.camera copies from one eye.

Nobody wears the headset during a run, so the HMD keeps looking wherever it started. Steps whose subject sits outside that view shoot it with `ShotAt` (`SelfTestRunner.Look.cs`): for the frames of that one capture the head and camera turn toward the subject (and with a distance, move up to it) between `beginContextRendering` and `endContextRendering`, then go back, so tracking, hands and the step's checks never see it. Aimed shots: `02-settings-vr` and `vr-04-settings` (the pause menu), `vr.08-arm-reach` (between the hands), `vr.33-ragdoll-arms` (the chest from 1.5 m, mid-fall) and `vr.33-ragdoll-arms-after` (between the hands, tracked again), `vr-09-belt-view` (60 degrees down, the belt and hotbar ghosts), `vr-10`/`vr-11` (the held Apple), `vr-12-eat` (the Apple at the mouth, from 0.45 m), `vr-13-throw` (the Apple in flight), `vr-14-two-hand`, `vr-18-emote-wheel`, `vr-20-holster` (the body slot), `vr-22-bandage` (the hold ring at the chest hand), `vr-25-cook` (the food and its hold ring), `vr-26-light` (between the torch flame and the fire, from 1.2 m) and `vr-26-lit` (the lit fire), `07-time-<hh>` (the sky), `vr-29-ground-backpack` and `vr-32-shoulder-stash` (the wheel), `vr-30-guide-<topic>` (row and demo), `vr-31-comfort-first` (the settings panel), `vr-31-station` (the station demo), `vr-6x-deny-<reason>` (the denial, shot while the trigger is still held so the red preview and reason label are up), `ui-02-button-labels`, `ui-04-rope-gauge` (after ui.03's hero title has gone), `ui-05-wrist-hud` (the wrist HUD raised like a watch, with a Cold slot; the step fails unless it faces the eye within 25 degrees and spans at least 12 degrees) and `vr-70` to `vr-72`. `vr-07-knockback` is taken at the vignette pulse's peak with the ring drawn.

Steps `vr.10` to `vr.20` need no one at the controllers. While each one runs, `VRInput.TestOverride` replaces both controllers right after the sampler with scripted hands, which rest tracked in front of the chest with a finger on the trigger. The step moves a hand, closes a grip or pushes a stick, and the rest of the mod treats that as real controller input. Each step starts with empty hands and an empty hotbar, and from the same stance (`Settle`, `SelfTestRunner.Settle.cs`): grips, triggers and sticks released, any climb ended, the body back on this segment's home spot (flat, open ground found by the first hand step there, else the game's base camp spawn) facing the home heading, grounded, under control, the rig recentred and its eye guard waited out, then the hip's net horizontal travel within 3 cm over 2 s, each end averaged over 1 s so the game's ~1 s idle sway cancels while a walk, push or creep does not (`StillWindow`, shared with the MP settle), with the view's eye guard done (`RigDriver.ViewSettling`). A body that does not settle fails the step with "not settled". When it ends, the grips open, the override is cleared and anything the step dropped or threw is removed, so a failed step leaves nothing behind for the next one.

Refusal steps check the whole cue, not just the refusal. ui.affordance writes each refusal or notice into a small capture as it shows it: the reason, the ring drawn (glyph, angular size, red tint), the sound and the haptic pulses per hand (`CueLog`, `CueCapture`). `ExpectCue` (`SelfTestRunner.Cues.cs`) fails a step unless the reason, a ring of at least `CueLayout.MinDegrees`, the refused (or notice) sound and a delivered haptic pulse all reach that hand in time. With `tint` it also needs the red preview on the refused thing. A pulse withheld because `Interaction.Haptics` is off counts as delivered. `ExpectFeel` checks the cues that are not refusals (progress, contact, cancel, end stop, danger). `ExpectNoCue` fails a step that should stay quiet when that hand gets a reason, ring or pulse. `vr.60` to `vr.65`, the spool's NoWall press and `vr.46`'s stamina refusal use `ExpectCue`. A tie that works uses `ExpectNoCue`. When no wall turns up anywhere, `vr.15`, `vr.44` and `vr.46` use the sandbox test wall.

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

MpObserver puts B under the same progress and save guard as the self-test: B earns no achievements or stats, completes no ascent, and never writes or deletes its quicksave. The log names each call it blocks. If a guarded game method is missing, the observer refuses to start. It records a fingerprint of B's quicksave at the start and at the end, and `tools/mp-check.sh` fails when the two differ. On A, the progress guard is `SelfTestProgressGuard`: the self-test ends with `99-save-untouched` and the MP script with `save-untouched`.

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
| `feed-refuse` (VR) | Holds a non-food item at B's head; expects the CantUse cue | No `GetFedItemRPC` or `SendFeedDataRPC` |
| `reach-deny` (VR) | Grips B's hand while B needs no help; expects NotInTrouble and no reaching | No local `RPCA_Fall` |
| `reach` (VR) | Asks B to web itself, then grips B's hand; `sinceGrabFriend` must start within 3 s | B's web status |
| `kick-mode` (VR) | Grips B's hand (no kick for 1 s), then shoves with both open palms; `isKicking` must turn on | A local `RPCA_Fall` |
| `carry` (VR) | Asks B to pass out, then lifts B with both hands and puts B down | Remote `RPCA_StartCarry` and `RPCA_Drop` |
| `pack` (VR) | Walks behind B and grips B's worn pack | B still wears its pack, no `PutInBackpackRPC` or `RPCAddItemToBackpack` |
| `pal-jump` (VR) | Crouches by lowering the head to 0.6 of its height, asks B to stand on A, then jumps | A local `JumpRpc` with `isPalJump` |
| `campfire-far` (VR) | Grips the logs of an unlit campfire out of B's morale radius; expects FriendsFar and the fire stays off | No `Light_Rpc` |
| `spectate` (VR) | Passes out, waits until the view follows B, flicks the stick up; expects CantSwitch. Then wakes itself | B sees A passed out |

`scene-button` (VR) runs after `ping` and before `board`: A grips a scene button only the host can press (a host-owned trigger) and expects the Blocked cue, and B must see no `RPCA_Trigger`. The social checkpoints from `feed-refuse` on run after `feed`, and `spectate` is last. Between them A asks B to wake when B is passed out, dead or struggling.

A step that finds nothing to do (no wall or no food within 40 m, no host-owned scene button, no pack on B) is `SKIP`. `kick-mode` is also `SKIP` unless the host run has kick mode on (`Sandbox.RunSetting HelpingHand 1`). Run 4 (one VR client, one unmodded client, before the social and world checkpoints) gave 16 pass, 0 fail, 2 skip (`eat` and `feed`, no food in range), and `tools/mp-check.sh` printed no `FAIL`.

### Observer requests

B obeys requests from A. A appends `{id, do, t}` lines to `a-requests.jsonl`; B ignores lines older than the lobby, runs each one and appends `{id, do, ok, note, t}` to `b-acks.jsonl` (and a `request` line to `b-observer.jsonl`). A waits 5 s for the ack by default, and a missing or failed ack skips the step. The requests are `passout` (the game's `Character.PassOut` console command), `web` (adds the Web status to B's own character, refused in the airport), `wake` (releases crouch, clears statuses and revives B on its own view if dead or passed out), `crouch` and `stand` (B's own crouch input) and `stand-on` (a `WarpPlayerRPC` on B's own view onto A's head). Apart from `web`, B uses only the game's console command, RPCs on its own view, or its own input. No current step sends `crouch` or `stand`.

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
tools/mp-check.sh [mp-dir]       # POS_TOL=1.0, LAND_TOL=1.5 metres, LAG_TOL=1.0 s by default
```

It prints one `PASS`, `FAIL`, `SKIP` or `INFO` line per check and exits with 1 when any check fails:

- B saw a remote player join.
- A's version check passed: the lobby's version equals A's, A is in the lobby, and the master is the host.
- For each checkpoint A passed: B logged it with A present, B's view of A's position is within `POS_TOL`, and B sees the same held item id as A. B can write its checkpoint before A's equip RPC arrives, so the first matching sample within `LAG_TOL` seconds after the checkpoint counts. For `drop` and `throw`, an item of that kind lies within `LAND_TOL` of where A saw it land. A checkpoint A failed is a `FAIL`, and one A skipped is a `SKIP`.
- B logged no exceptions and no unknown-event errors, and received no event 151.
- A sent no event 151 (with a vanilla host, net.pose has no peers).
- `feed-refuse`: B got no `GetFedItemRPC` or `SendFeedDataRPC`. `reach-deny`: B got no local `RPCA_Fall`. `scene-button`: no `RPCA_Trigger` reached B. `campfire-far`: B saw no `Light_Rpc`.
- `kick-mode`: B logged a local `RPCA_Fall` from the shove. `carry`: B logged a remote `RPCA_StartCarry` and `RPCA_Drop`. `pal-jump`: B logged a local `JumpRpc` with `isPalJump`. `spectate`: B's checkpoint shows A passed out. `reach`: B's own Web status is 0.05 or less, so the reach freed B. `pack`: B still wears its pack and got no `PutInBackpackRPC` or `RPCAddItemToBackpack`.
- B's quicksave fingerprint is the same at the end as at the start.

The remaining checks are by eye: the screenshots for how A's held items and arms look on B's screen, and B's `rpc` lines in `b-observer.jsonl` for emote, ping, eat and feed.

## What the sandbox does not cover

- A vanilla client or someone else's lobby: the sandbox is inactive there on purpose.
- Mobs and hazards that exist only inside map prefabs (spiders, jellyfish, lava, geysers): reach them by `Sandbox.Jump` to the right segment.
- Free camera in VR: refused.

## Known gaps

- Spawn paths marked "assumed" above have not been run in game.
- `Sandbox.Mob` for prefabs found by the deep scan is a plain network instantiate; some need extra setup the game does after spawning.
