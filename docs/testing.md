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
| Tornado | needs a `TornadoSpawner` in the scene | Only spawns on a map that has one; pushes you; camera does not roll |
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

## What the sandbox does not cover

- A vanilla client or someone else's lobby: the sandbox is inactive there on purpose.
- Mobs and hazards that exist only inside map prefabs (spiders, jellyfish, lava, geysers): reach them by `Sandbox.Jump` to the right segment.
- Free camera in VR: refused.

## Known gaps

- Spawn paths marked "assumed" above have not been run in game.
- `Sandbox.Mob` for prefabs found by the deep scan is a plain network instantiate; some need extra setup the game does after spawning.
