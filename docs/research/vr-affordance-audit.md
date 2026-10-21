# VR affordance audit: actions the hands suggest and the game refuses

On a flat screen the crosshair, the prompt and the greyed-out item tell the player what will work. In VR the hands and body make many actions look possible that PEAK rejects or silently ignores: it measures from the camera, wants a specific wall angle, needs stamina, a free hand, a lit fire or everyone near it. This audit lists 34 such cases, ranked by frustration times frequency, with the rule in the decompile, why the VR body misleads, where PeakXR makes the attempt, and the cue that would make it clear. The last section, "What ui.affordance shows", describes the cues built for the top cases.

Related documents: `docs/mechanics.md` has the per-mechanic inventory (row ids such as IT-09 point into it), `docs/research/vr-immersion-audit.md` covers gesture upgrades, and `docs/multiplayer-compat.md` already lists three parity refusals (too far, wrong angle, keep holding) that go through `ParityFeedback`. This audit does not repeat their analysis.

Paths: `A/` is `decomp/Assembly-CSharp/`; `src/` is `src/PeakXR/`; `core/` is `src/PeakXR.Core/`. Decomp line numbers were read from the files. PeakXR line numbers are for commit `fb58e97` and drift, so look up PeakXR code by function name. Prefab-only values (for example `climbingSpikeStartDistance`) are not in the decompile; read them from the component at runtime.

## How to read the scores

F is frustration from 1 (shrug) to 5 (player thinks the mod is broken or loses an item). Q is how often a VR player meets it in a normal run, 1 (rare) to 5 (every few minutes). Score is F x Q. Cue types: pre-emptive means shown while the hand hovers or aims, before the attempt; on-failure means shown when the attempt just did nothing. A haptic alone is never enough for a rule the player has not learned, so each entry names a short label as well. Labels are 20 characters or fewer. Names in code font such as `NoStamina` are `DenyReason` values from `core/Affordance.cs`; "new" marks a reason that does not exist yet.

## Ranked summary

| Rank | Item | Score | Rule ref | Proposed cue |
|---|---|---|---|---|
| 1 | Wall grab refused (angle, reach, stamina) | 4x5 = 20 | `CharacterClimbing.cs:511-532`, `:534-562` | On-failure label at hand: `TooFlat`, `Overhang`, `OutOfReach`, `NoStamina` |
| 2 | Starting a climb, rope, vine or carry puts away or drops the held item | 4x4 = 16 | `CharacterClimbing.cs:569`, `CharacterItems.cs:498` | Pre-emptive "Item will stow" or "Item will drop" on the holding hand |
| 3 | Piton hammer: out of arm reach, slow strike, early release | 4x4 = 16 | `CharacterItems.cs:981-1027`, `:1072-1076` | Hover ring at the preview plus "Strike harder" or "Too far" |
| 4 | Stamina gates (rope and vine grab, climbing up, overhang) | 4x4 = 16 | `RopeSegment.cs:52`, `JungleVine.cs:103`, `CharacterClimbing.cs:301`, `:455` | `NoStamina` at the hand on a refused grab, hover on a rope |
| 5 | Interaction reach is 2 m from the head | 3x5 = 15 | `Interaction.cs:7`, `:204`, `:214` | Existing `OutOfReach` ring plus a reach-end marker |
| 6 | Lighting a campfire needs every living player in range | 4x3 = 12 | `Campfire.cs:322`, `:377-388` | Pre-emptive "Friends too far" on the log, before the 5 s hold |
| 7 | Pickup with a full inventory | 4x3 = 12 | `Item.cs:580-583`, `Player.cs:268-287` | Red tint on the item, "Inventory full" |
| 8 | Two-handed item used with one hand (PeakXR rule) | 4x3 = 12 | `src/Interact/PhysicalInteraction.cs` Inject, `core/ItemHold.cs:163` | `NeedsTwoHands` at the holding hand (wired in working tree) |
| 9 | Carrying a downed friend: both hands, pack dropped, item put away | 4x3 = 12 | `CharacterInteractible.cs:77-88`, `CharacterCarrying.cs:51-67` | "Needs both hands", "Pack will drop" |
| 10 | Rope shooter: grounded, ammo, 30 m | 4x3 = 12 | `RopeShooter.cs:122-138` | Aim line turns red with the reason |
| 11 | Vine shooter: grounded, pitch window, 40 m | 4x3 = 12 | `VineShooter.cs:71-92` | Aim line red, "Aim lower" or "Aim higher" |
| 12 | Item with no uses, fuel or valid state ignores the trigger | 3x4 = 12 | `Item.cs:797-817`, `Lantern.cs:152` | Deny instead of the success tick: `UsedUp` or `CantUse` |
| 13 | Rope spool anchor farther than 5 m from the body | 3x4 = 12 | `RopeTier.cs:11-13`, `:103-111` | Red ghost anchor plus "Too far" |
| 14 | Opening the hand: no drop by default, `canDrop` and `canThrow` false | 3x4 = 12 | `CharacterItems.cs:259`, `Item.cs:65-71` | Tick when the grip opens on a kept item, `CantThrow` |
| 15 | Holstering: lit flare, overflow slot, switch gates | 3x3 = 9 | `CharacterItems.cs:498`, `:735-738`, `Flare.cs:30-31` | Existing double pulse plus a reason label |
| 16 | Rope segments too slack or too horizontal to grab | 3x3 = 9 | `RopeSegment.cs:49-58` | Hover "Rope too slack" |
| 17 | Climb lunge refused (1 s cooldown, no stamina) | 3x3 = 9 | `CharacterClimbing.cs:122` | `TooSoon` or `NoStamina` at the throwing hand |
| 18 | Weak piton cracks and breaks after a hidden hang time | 3x3 = 9 | `ShittyPiton.cs:42`, `:55-93` | Crack haptic on the hanging hand |
| 19 | Use on a friend: target, range, mouth dwell | 3x3 = 9 | `Item.cs:831-840`, `CharacterInteractible.cs:92-181` | Ring on the friend plus `WrongAngle` or "Not downed" |
| 20 | Grab through a wall is filtered out | 3x3 = 9 | `Interaction.cs:257-262` | "Blocked" ring on the hidden target |
| 21 | Sticky (cactus) ball cannot be released by a hand throw | 4x2 = 8 | `CharacterItems.cs:277-289`, `CharacterData.cs:419-435` | "Hold drop to charge" cue |
| 22 | Rescue hook unusable without a target in range | 3x2 = 6 | `RescueHook.cs:114`, `:280-284` | Aim line red |
| 23 | Constructable preview invalid (distance, slope, overlap) | 3x2 = 6 | `Constructable.cs:62-68`, `:137-148` | Trigger squeeze gives deny haptic |
| 24 | Stash into a worn pack ignores `canBackpack` | 3x2 = 6 | `BackpackWheelSlice.cs:84` | Pre-check in `ReleaseAtShoulder` |
| 25 | Help-up reach needs an empty hand and a helpable target | 3x2 = 6 | `CharacterGrabbing.cs:247-253`, `:293-338` | "Hands full" |
| 26 | Hold interact cancels when the ray leaves the target | 3x2 = 6 | `Interaction.cs:128-134` | Progress ring turns red on drift |
| 27 | Cooking: item not cookable, fire unlit or spent | 2x3 = 6 | `Campfire.cs:341-356`, `ItemCooking.cs:48` | "Can't cook this" |
| 28 | Equip and switch cooldowns swallow a pickup or draw | 2x3 = 6 | `Item.cs:580`, `CharacterItems.cs:72-84` | `TooSoon` |
| 29 | Respawn chest and luggage rules | 2x3 = 6 | `RespawnChest.cs:53`, `Luggage.cs:168-170` | "Needs both hands", "Already open" |
| 30 | Sprint and crouch gestures gated by state | 2x3 = 6 | `CharacterMovement.cs:241-263`, `Character.cs:1500-1526` | Gesture-off tick |
| 31 | Ping: cooldown, conscious, terrain hit | 2x3 = 6 | `PointPinger.cs:24-47`, `:58` | `TooSoon`, "No surface" |
| 32 | Eating with no hunger wastes the food | 2x3 = 6 | `Action_RestoreHunger.cs:7` | Optional "Not hungry" warning |
| 33 | Passed out, struggling or dead: all hand actions stop | 2x2 = 4 | `Interaction.cs:59-69`, `CharacterItems.cs:183`, `:199` | Existing status UI |
| 34 | Emote wheel unavailable while climbing, dead or won | 1x2 = 2 | `GUIManager.cs:312-322`, `:563` | None needed |

## Cases

### 1. Wall grab refused (angle, reach, stamina)

Rule: `CharacterClimbing.AcceptableGrabAngle` (`A/CharacterClimbing.cs:511-532`) takes `d = Vector3.Angle(normal, up) - 90`. If `d > 0` it refuses when `d > 80` (angle above 170, a ceiling); otherwise it refuses when `-d > 40` (angle below 50, ground or a gentle slope). A `ClimbModifierSurface` can force 90 near it (`ClimbModifierSurface.cs:63-70`). `TryToStartWallClimb` (`:534-578`) casts from the camera (`:541`) for at most `raycastDistance = 1.25f` (`:534`, with 0.05 and 0.1 thickened retries at `:556-560`). It also refuses when `!(sinceLastClimbStarted > 1f) && OutOfStamina()` (`:562`) or while a condor carries you, and `CanClimb` (`:580-595`) fails for 0.2 s after any climb and while on a rope or vine.

VR: a hand pressed flat on the floor, a ceiling or a shallow slab looks like a hold. The wall may also be within arm's reach but over 1.25 m from the eye, so a lean-in grab fails.

PeakXR: `PhysicalInteraction.OnGripPress` runs `ProbeHold`, which mirrors the angle test through `ClimbRules.GrabAngleOk` (`core/ClimbAssist.cs:64`) and quietly drops the grip to other actions; `StartClimb` returns false when the game starts nothing. The working tree now calls `Affordance.Deny` with `TooFlat`, `Overhang`, `OutOfReach` (with the 1.25 m boundary) and `NoStamina` there. Gaps: `ProbeHold` returns true when its short rays find nothing, so "no wall within reach" shows only as the `OutOfReach` branch.

Cue: on-failure label at the hand plus a slip haptic (already designed). A pre-emptive version is possible because `ProbeHold` runs on grip press only; a per-frame probe while a hand hovers within `ClimbRadius` of terrain could tint the hand, but this is optional.

### 2. Starting a climb, rope, vine or carry puts the held item away

Rule: `TryToStartWallClimb` calls `EquipSlot(None)` (`A/CharacterClimbing.cs:569`). `RopeSegment.Interact` does the same (`RopeSegment.cs:43`), as do `JungleVine.Interact` (`JungleVine.cs:114`) and `CharacterCarrying.StartCarry` (`CharacterCarrying.cs:51`). `EquipSlot` stows only when `UIData.canPocket`; otherwise it sends `DropItemRpc` and the item falls (`CharacterItems.cs:498-505`). A lit flare has `canPocket = false` (`Flare.cs:30`).

VR: the other hand holds a torch or a lantern and the free hand grips the wall. The held item vanishes from the hand, or drops down the mountain, with no warning.

PeakXR: `PhysicalInteraction.StartClimb` (the `StartWallClimb` call), the rope and vine grabs through `FindCandidate` and `GrabInteractible`, and `SocialHands` carry.

Cue: pre-emptive, on the holding hand when the other hand's grip closes on a climbable surface: if `currentItem.UIData.canPocket` is false, a strong double pulse and the label "Item will drop"; otherwise a soft tick and "Item will stow". Since the grab still succeeds, show it as a one-time warning per item, not a refusal. Option: for a non-pocketable item, delay the grab by 0.3 s so the player can let go. New reason: `ItemWillDrop`.

### 3. Piton hammer: out of arm reach, slow strike, early release

Rule: `UpdateClimbingSpikeUse` (`A/CharacterItems.cs:981-1027`). The ray starts at the camera and runs `climbingSpikeStartDistance` while climbing or `climbingSpikeStartDistanceGrounded` otherwise, on the terrain-map layer only (`:1072-1076`; prefab values in `ClimbingSpikeComponent.cs:10-16`). If it misses, the tick resets and nothing shows (`:1006-1009`). Primary start needs `!isClimbingAnything && climbingSpikeSelected`; secondary needs `climbingSpikeSelected || isClimbingAnything` (`:1012`, `:1019`). Once started, the cast cancels when the preview is farther from the camera than `climbingSpikePreviewDisableDistance(Grounded)` (`:971-979`, `:989-992`) or when the start button is released (`:994-998`). There is no wall-angle limit. The piton lands after `usingTimePrimary` seconds.

VR: PeakXR swaps the strikes for the hold timer (`src/Interact/Tools/SpikeHammer.cs:33-79`): a strike is a hand within 0.2 m of the preview point moving at least 1.2 m/s into the wall (`:28-30`, `:81-95`). With no strike the tick stays at the minimum. A player who pulls the trigger at a wall beyond arm reach, or who strikes too softly, sees the preview and nothing else. Releasing the trigger between strikes cancels the cast silently.

PeakXR: `SpikeHammer.BeforeUpdate`, `AimPatches.ClimbingSpike` (`src/Interact/Aim/AimPatches.cs:253-273`). `ParityFeedback.KeepHolding` already covers "all strikes in, hold time not run".

Cue: while casting, if the hand-to-preview distance is above the strike radius for over 1 s, hover an `OutOfReach` ring at the preview with the walking distance. If a hand enters the radius slower than `MinSpeed`, label "Strike harder" (new `TooSlow`). When the trigger is released with `climbingSpikeCastProgress` above 0, a short deny haptic and `MovedAway` if the preview left range. With a spike selected and `RaycastClimbingSpikeStart()` false while the trigger is held, `NoWall` "No wall in reach".

### 4. Stamina gates

Rule: rope grab needs `!interactor.OutOfStamina()` (`A/RopeSegment.cs:52`), vine grab the same (`JungleVine.cs:103`). `OutOfStamina` is `currentStamina < 0.005f && extraStamina < 0.001f` (`Character.cs:1683-1692`). A wall grab is refused for 1 s after the last grab when exhausted (`CharacterClimbing.cs:562`). Once on the wall, upward input is ignored while exhausted (`:301`), the climber slides (`:263`), and the overhang limit tightens from 80 to 60 degrees past horizontal (`:455`), which ends the climb silently (`:265-271`). On a rope, failing `UseStamina` releases the grip (`CharacterRopeHandling.cs:71`).

VR: the hand closes on the rope and nothing happens, or the arms pull and the body will not rise.

PeakXR: `FindCandidate` filters ropes through `IsInteractible` (`src/Interact/PhysicalInteraction.cs`, line check at the `it.IsInteractible(c)` test), so the grip falls through silently. `StaminaBeat` and `ClimbHands` give haptics only once climbing. `UpdateClimb` zeroes the up stick when exhausted (`slideOnly || outOfStamina`).

Cue: on a grip within reach of a rope or vine segment while `c.OutOfStamina()`, `NoStamina` at the hand ("No stamina"). While climbing with `outOfStaminaFor > 0` and the arms pulling up, hover the same label at the wrist rather than another pulse.

### 5. Interaction reach is 2 m from the head

Rule: `Interaction.distance = 2f` (`A/Interaction.cs:7`), ray origin `MainCamera.instance.transform.position` (`:204`), line check to `origin + dir * distance` (`:214`), then a sphere cast of radius `area = 0.5f` (`:9`, `:241`). Targets must pass `IsInteractible` (`:233`, `:255`).

VR: a pointing hand reaches farther than the head would, and the game would accept a pick the head cannot make.

PeakXR: `InteractPatches.Raycasts` shortens the hand ray by the hand's offset from the head (`src/Interact/InteractPatches.cs:96-121`) and `ReportTooFar` (`:72-93`, called at `:144`) calls `ParityFeedback.Denied(TooFar)` when the full ray would have found something. The working tree forwards that to `Affordance.Deny(OutOfReach)` with the reach end.

Cue: on-failure ring is wired. The pre-emptive faint ray the affordance runtime draws while pointing is the right second half. Grab by hand overlap (`FindCandidate`) uses `GrabRadius` at the hand and is not subject to the 2 m rule.

### 6. Lighting a campfire needs every living player in range

Rule: the hold finishes in `Campfire.Interact_CastFinished` and lights only `else if (EveryoneInRange(moraleBoostRadius))` (`A/Campfire.cs:309-326`). `EveryoneInRange` is true when every non-dead player is within the radius (`:377-388`). The flat prompt lists who is missing (`:261-269`, `:390-410`, text key CANTLIGHT). The campfire prefab radius is not in the decompile.

VR: touching a lit torch to the logs starts a 5 s hold (`cookTime`, `:40`, `:429-432`) with crackle haptics, then nothing happens. The prompt text with names is on the HUD, easily missed.

PeakXR: `FireGestures.UpdateLight` (`src/Interact/Physical/FireGestures.cs:127-153`) finds the unlit campfire in `FindLightable` (`:199`, campfire block near `:240`) and calls `GestureRequests.Interact(target)` at `:149`. A grip hold on an unlit fire reaches the same `Interact_CastFinished` through `PhysicalInteraction`.

Cue: pre-emptive. Before `GestureRequests.Interact(target)`, if `target is Campfire fire && !fire.EveryoneInRange(out var names, fire.moraleBoostRadius)`, deny with the missing names under the log: "Friends too far" (new `FriendsFar`), no crackle, no hold. The method is public.

### 7. Pickup with a full inventory

Rule: `Item.Interact` returns silently when `!interactor.player.HasEmptySlot(itemID)` or within 0.25 s of the last equip (`A/Item.cs:578-583`). `HasEmptySlot` is true while any hotbar slot or the temporary slot 250 is free (`Player.cs:268-287`); a backpack item needs an empty backpack slot (`:271-274`). The master then re-checks in `AddItem`: the temporary slot is refused while climbing anything (`:132`) and a second backpack fails (`:98-105`), which sends `DenyPickupRPC` (`Item.cs:598-603`). While climbing, a non-pocketable item is released instead (`:584-588`).

VR: the grab pulses success, the hand closes, the item stays put.

PeakXR: `PhysicalInteraction.OnGripPress` `GrabItem` case plays `Haptics.Pulse(0.6, 0.05)` before the game decides (`:256-261`); `FindCandidate` checks only `IsInteractible`.

Cue: pre-emptive in `FindCandidate`: for an `Item` candidate, if `!c.player.HasEmptySlot(item.itemID)` or `c.data.isClimbingAnything && !item.UIData.canPocket` or the 0.25 s lockout is running, skip the success pulse and deny. Label "Inventory full" (new `InventoryFull`), "Not while climbing" for the climbing case (`NotWhileClimbing`), "Pack slot taken" for the second backpack. The slot ghosts (`src/Inventory/SlotGhosts.cs`) already show a full belt.

### 8. Two-handed item used with one hand

Rule: not a game rule. `ItemHoldTable.CanUse` (`core/ItemHold.cs:163`) blocks primary and secondary use of a `TwoHanded` profile (antizooka, rescue hook and claw at `:89-93`, and large items by size, `:145`) unless the free hand grips it. Vanilla would fire.

VR: the item fires in vanilla, so a player used to the flat game, or to a one-hand-capable item, squeezes the trigger and nothing happens.

PeakXR: `PhysicalInteraction.Inject` zeroes the use inputs and sets `useBlocked` (block starting at `if (holding && !ItemHoldTable.CanUse(...))`). The working tree adds `Affordance.DenyAtHand(NeedsTwoHands)` with the second grip as the hint.

Cue: on-failure at the trigger press, wired. Pre-emptive: tint the item's second grip (`Hand_L` or `Hand_R` child) while the free hand is away.

### 9. Carrying a downed friend

Rule: `CanBeCarried` needs `fullyPassedOut && !dead && !carrier` and not a bot (`A/CharacterInteractible.cs:77-88`); the 3 s hold is not used, a single `Interact` starts it (`:114-124`). `StartCarry` first puts away the held item (`CharacterCarrying.cs:51`) and `RPCA_StartCarry` drops a worn backpack at the friend's torso (`:59-67`). Carrying ends when the friend wakes or dies (`:29-32`). Hotbar slot 4 while carrying drops the friend (`CharacterItems.cs:821-824`).

VR: PeakXR requires both hands on the friend (`CarryBothHands`, `src/Social/SocialHands.cs:335-340`). A single grip on a downed friend does nothing and gives no hint. A player who grips with a pack on loses the pack without a warning.

PeakXR: `SocialHands.Find` (`:197-207`) picks `Kind.Carry`; `ActiveInteract` withholds the interact until both hands agree.

Cue: when one hand has `Kind.Carry` and the other does not, hover `NeedsTwoHands` at that hand. When carrying would start with a worn pack, label "Pack will drop" (new `PackDrops`) on the shoulder anchor; with an item in hand, the case-2 warning.

### 10. Rope shooter: grounded, ammo, 30 m

Rule: `RopeShooter.WillAttach` (`A/RopeShooter.cs:122-138`) is false when `!isGrounded` (`:125`), `!HasAmmo` (`:129`), or the ray from the camera finds no terrain within `maxLength = 30f` (`:29`, `:133`). `Update` writes it into `overrideUsability` every frame (`:67-70`), so the trigger just does nothing. The cast re-checks and, with no ammo, plays the empty sound (`:72-86`).

VR: the aim line is white and always looks valid. Airborne or at a wall 35 m away, the trigger is dead.

PeakXR: `AimPatches.RopeShooterWillAttach` (`src/Interact/Aim/AimPatches.cs:57-71`) runs the check with the camera moved to the hand; `AimReticle.TryAim` draws the line (`src/Interact/Aim/AimReticle.cs:55-87`) and `Classify` already reads `maxLength` (`:100`).

Cue: pre-emptive in `AimReticle.LateUpdate`: while the trigger is touched, if `!item.CanUsePrimary()`, tint the dot and line `Affordance.DenyColor` and set the label by order: not grounded "Stand on ground" (new `NeedsGround`), `!HasAmmo` "Used up", no hit within `maxLength` "No wall in reach". `CanUsePrimary` is public and, for these tools, equals the game's own validity check.

### 11. Vine shooter: grounded, pitch window, 40 m

Rule: `VineShooter.WillAttach` (`A/VineShooter.cs:71-92`): grounded (`:74`), terrain hit within `maxLength = 40f` (`:16`, `:78`), and for `dot = Vector3.Dot(Vector3.up, (hit.point - camera.position).normalized)`: refused when `dot > 0.6f || dot < -0.7f` (`:82-86`) and again when `dot > 0.5f` (`:87-90`), so the window is about 44 degrees below to 30 degrees above horizontal. After the cast, `CheckVinePath` is tried ten times with growing slack (`:48-66`); if none fits, nothing spawns and no use is spent.

VR: aiming at a high ledge or straight down looks fine; the tool is dead.

PeakXR: `AimPatches.VineShooterWillAttach` (`:103-117`), `VineShooterFire` (`:122-132`); `AimReticle.Classify` reads `maxLength`.

Cue: as case 10 with "Aim lower" when `dot > 0.5`, "Aim higher" when `dot < -0.7` (new `AimLower`, `AimHigher`). A failed path after a valid cast: on-failure "No room for vine".

### 12. Item with no uses, fuel or valid state ignores the trigger

Rule: `Item.CanUsePrimary` (`A/Item.cs:797-817`) is false when `overrideUsability` is false, `!UIData.hasMainInteract`, or `ItemUses` data is 0. `DoUsing` simply skips it (`CharacterItems.cs:201-212`). Cases: lantern with no fuel (`Lantern.cs:152` writes uses 0 when fuel is gone), wrecked cooked items (`ItemCooking.cs:195-211`), scout effigy with nobody downed (`ScoutEffigy.cs:12-18`), amulet when `petrifyAmount >= 100` (`Peak/AmuletBase.cs:31-32`), warp item after use (`Action_WarpToBiome.cs:10`), magic bugle below `initialTootCost` (`MagicBugle.cs:90`; silent at `BugleSFX.cs:50-53`), rope spool with `ropeFuel <= 2` (`RopeSpool.cs:37`). There is no normal-bugle cooldown.

VR: the trigger squeeze gives the success tick and nothing else.

PeakXR: `PhysicalInteraction.Inject` plays `Haptics.Pulse(0.3, 0.03)` on `PrimaryEdge.Pressed && holding`, before the game's `CanUsePrimary` is consulted. `HeldGauges` shows the fuel arc.

Cue: replace the success tick with a deny when `!item.CanUsePrimary()`: `UsedUp` if the `ItemUses` entry is 0 or the fuel gauge is empty, else `CantUse`; for the effigy "No one is down" (new `NoOneDown`).

### 13. Rope spool anchor farther than 5 m from the body

Rule: `RopeTier` (`A/RopeTier.cs`): the ghost ray goes up to `maxAnchorGhostDistance = 10f` from the camera (`:11`, `:99`), but the anchor counts as good only when the hit is within `maxAnchorDistance = 5f` of `holderCharacter.Center` (`:13`, `:103-111`); otherwise `ropeAnchor.Ghost` stays true and the cast timer never starts. A found anchor is fixed (`goodAnchorPlace`, `:95-116`) until the item moves 10 m from it (`:81-87`). After placing, the trigger must be released before the next try (`:63-70`). The rope itself is limited to `min(ropeFuel, 40)` segments (`RopeSpool.cs:140`, `Rope.cs:77`).

VR: the hand reaches a wall 8 m away, the game draws its ghost anchor red, but the distance is measured from the torso, not the hand.

PeakXR: `AimPatches.RopeTierUpdate` (`:43-52`) moves the ray to the hand.

Cue: pre-emptive, from `tier.ropeAnchor != null && tier.ropeAnchor.Ghost`: label "Too far" at the ghost with the distance to close (new hint `Move 3 m closer`); the player sees the number, not a colour alone. A segment cap in `RopeSpool.Update` is silent: label "Rope fully out" when `Segments` equals the clamp and scroll is still pushed (low value).

### 14. Opening the hand: no drop by default, `canDrop` and `canThrow` false

Rule: drop needs `UIData.canDrop` (`A/CharacterItems.cs:259`) and no use in progress (`:254-258`); `canThrow` only hides the throw prompt (`Item.cs:71`, `GUIManager.cs:1183`). PeakXR throws when the release speed is at least `ThrowSpeed` (default 2 m/s) and otherwise keeps the item unless `ReleaseToDrop` is on (default false, `core/GripIntent.cs`, `OnRelease`).

VR: an open hand should let go. Instead the item stays held. A non-throwable item swung hard stays too.

PeakXR: `OnGripRelease` (`src/Interact/PhysicalInteraction.cs`, the `GripIntent.OnRelease` call). The working tree already calls `Affordance.DenyAtHand(CantThrow)` for a fast release of a `canThrow = false` item.

Cue: a slow release that keeps the item gets a single soft tick on that hand and, the first three times per session, the hint "Swing to throw, or use drop". Items with `canDrop = false`: `CantUse`-style "Can't drop this".

### 15. Holstering: lit flare, overflow slot, switch gates

Rule: `EquipSlot` stows only a `canPocket` item that is not in the overflow slot 250 (`A/CharacterItems.cs:498-505`); `DoSwitching` returns early while the held item is mid-use or finished a cast within 0.1 s, when not conscious, `lockedFromSwitching` (0.1 s, 0.25 s after three quick switches, `:72-84`), climbing a wall or rope (not a vine), or with a sticky item held (`:735-741`). A lit flare cannot be pocketed or put in a pack (`Flare.cs:30-31`). A rocket pack in slot 3 refuses (`:484-487`). While struggling none of this runs (`:183`).

VR: the hand reaches the belt slot and the slot accepts it visually.

PeakXR: `BodyInventory.Holster` (`src/Inventory/BodyInventory.cs:328-348`), `SwitchBlocked` (`:502`), `DoublePulse` (`:536`). The cue exists but says nothing about why. `SwitchBlocked` lacks `afflictions.isStruggling`.

Cue: keep the double pulse and add a label chosen by the failing test: "Lit, can't stow" (new `ItemLit`) for `canPocket` false, "Won't fit here" (`CantHolster`) for slot 250, `NotWhileClimbing` for the climbing gates, `TooSoon` for the cooldown.

### 16. Rope segments too slack or too horizontal to grab

Rule: `RopeSegment.IsInteractible` requires the segment's angle from up to be under `maxRopeAngle * 0.6` (54 degrees) or within 54 degrees of down, plus `rope.isClimbable`, plus stamina, plus not the rope you hold (`A/RopeSegment.cs:49-58`, `CharacterRopeHandling.cs:29`). A grab within 0.2 s of the last is ignored (`RopeSegment.cs:41`).

VR: a rope lying across a ledge or curling slack looks grabbable. The hand closes on it and the grip falls through to something else.

PeakXR: `FindCandidate` skips it through `IsInteractible`.

Cue: pre-emptive, when a hand is within the grab radius of a `RopeSegment` that fails only on angle: hover "Rope too slack" (new `RopeSlack`). Low priority because the rope usually hangs.

### 17. Climb lunge refused

Rule: the climb jump needs `sinceClimbJump > 1f && outOfStaminaFor < 0.5f` and a stick magnitude above 0.1 (`A/CharacterClimbing.cs:118-125`), and costs 0.2 stamina (`:241`).

VR: a fast upward hand release is read as a lunge. During the 1 s cooldown or when exhausted it does nothing but let go.

PeakXR: `PhysicalInteraction.TryLunge`. The working tree now denies `NoStamina` when exhausted; the cooldown case still returns silently.

Cue: `TooSoon` at the throwing hand for the cooldown (the lunge velocity was valid, so the player meant it).

### 18. Weak piton breaks after a hidden hang time

Rule: `ShittyPiton` picks `totalSecondsOfHang = Random.Range(1f, 5f)` at start (`A/ShittyPiton.cs:42`), counts it down only while someone hangs (`:70-77`), then cracks every 1.5 s and breaks on the fourth crack (`:20`, `:55-93`). Players cannot tell the weak ones from good ones before hanging. The handle also refuses a second hanger (`ClimbHandle.cs:99-102`).

VR: a player trusts a hand-held piton with full body weight.

PeakXR: `ClimbHands` handle mode (`src/Interact/Climb/ClimbHands.cs`), no crack feedback beyond the game's camera shake.

Cue: on-event only. Hook `ShittyPiton.Crack` and pulse the hanging hand with a rising pattern per crack (1 of 4), and fire a "Piton failing" label on the first crack so the player knows to move.

### 19. Use on a friend: target, range, mouth dwell

Rule: a `canUseOnFriend` item needs `Interaction.hasValidTargetCharacter` when `mustUseOnFriend` (`A/Item.cs:831-840`, `:984`, `:1039`). The target is valid only if alive, not you, and either passed out or within `maxCharacterInteractAngle = 90f` of facing you (flat look vectors, `A/CharacterInteractible.cs:92-99`, `:156-181`); items with `overrideFeedAngle` use their own window (`:167-172`), such as `RitualDagger` (100 to 180) per `docs/multiplayer-compat.md`.

VR: PeakXR feeds when the item is held within 0.22 m of the friend's head (0.3 m exit, 0.25 s dwell; 0.08 m larger at the torso for healing items) and relaxes the facing test for normal items (`src/Social/SocialHands.cs:35-37`, `UpdateFeed`, `src/Social/SocialPatches.cs:112-140`). It does not relax it for `overrideFeedAngle` items.

PeakXR: `SocialPatches.Raycasts` postfix forwards `WrongAngle` through `ParityFeedback` (`:100-103`) only for `overrideFeedAngle` items after the dwell.

Cue: add a ring on the friend while a `mustUseOnFriend` item hovers inside the zone but `IsInteractible` is false because the friend is dead ("Beyond help", new) or not downed for items that need it. Already-wired `WrongAngle` stays.

### 20. Grab through a wall is filtered out

Rule: the game's pick drops candidates whose line from the camera is blocked by terrain that is not the candidate (`A/Interaction.cs:257-262`).

VR: a hand pushed through a crate, rock or thin wall overlaps an item that cannot be picked up.

PeakXR: `FindCandidate` repeats the check from the eye (`src/Interact/PhysicalInteraction.cs`, line-of-sight block with the 5 cm margin) and skips silently.

Cue: if the only candidate was dropped for blocking, show a red ring on its closest point and label "Blocked" (new `Blocked`).

### 21. Sticky (cactus) ball cannot be released by a hand throw

Rule: a held item with `StickyItemComponent` sets `currentStickyItem` (`A/CharacterData.cs:419-435`). Interaction is off while it is held (`Interaction.cs:59-69`), item switching is refused (`CharacterItems.cs:741-745`), and `DoDropping` returns without dropping if `throwChargeLevel < throwChargeRequirement` (`:276-289`; value in the prefab).

VR: `InteractPatches.Dropping` sets `throwChargeLevel = 0f` for a hand throw (`src/Interact/InteractPatches.cs:171-198`), so a swing can never meet the requirement. The ball stays glued; only the drop button held down charges.

PeakXR: `SocialHands.UpdateStickyCue` pulses once when the ball sticks.

Cue: in the `Dropping` prefix, if `data.currentStickyItem != null`, either set `throwChargeLevel` to the requirement (accepting the extra forward impulse) or deny with "Hold drop to charge". Pick one after in-game testing.

### 22. Rescue hook unusable without a target in range

Rule: `item.overrideUsability = hit.transform` every frame (`A/RescueHook.cs:108-115`). Range is `range = 30f`, `rangeDownward = 40f` (`:81-83`, `:279-284`), measured along the screen-centre ray.

VR: aim line white, trigger dead beyond range or at the sky. `AimPatches.RescueHookRay` (`:138-153`) already replaces the ray.

Cue: case 10 mechanism, label "No target in range" (19 characters).

### 23. Constructable preview invalid

Rule: `Constructable.Update` sets `overrideUsability` false when the preview is invalid (`A/Constructable.cs:62-68`): hit farther than `maxConstructDistance` (`:139`), slope above `maxConstructVerticalAngle` (`:143`), or overlap with an avoidance sphere (`ConstructablePreview.cs:38-48`). The preview prefab switches its own valid and invalid children (`:23-27`).

VR: the game's red preview is the only cue, and it moves with the hand aim. The trigger is dead.

PeakXR: `AimPatches.ConstructableUpdate` (`:170-180`).

Cue: deny haptic on trigger press while the preview is invalid, label by cause: "Too far", "Too steep", "Blocked".

### 24. Stash into a worn pack ignores `canBackpack`

Rule: the vanilla wheel greys out an empty pocket when `currentItem.UIData.canBackpack` is false (`A/BackpackWheelSlice.cs:80-86`); a lit flare sets it false (`Flare.cs:31`). `CharacterBackpackHandler.StashInBackpack` does not check it (`:127-136`), and `EquipSlot` then drops a non-pocketable item (`CharacterItems.cs:498`) after the slot has been emptied.

PeakXR: `BodyInventory.ReleaseAtShoulder` (`src/Inventory/BodyInventory.cs:350-380`) tests only free pocket, held slot, switch gates and rocket pack. This may lose or duplicate a lit flare; verify in game.

Cue: add `item.UIData.canBackpack && item.UIData.canPocket` to the checks and deny "Won't fit here" otherwise.

### 25. Help-up reach needs an empty hand and a helpable target

Rule: `CanGrab` requires no held item and no wall, rope or vine climb (`A/CharacterGrabbing.cs:319-338`). `Reach` pulls only characters within 4 m and 60 degrees of your look who pass `TargetCanBeHelped`: stuck, recently unstuck, struggling, or climbing below your pull point (`:247-253`, `:293-317`).

VR: gripping a standing or hanging friend does nothing visible.

PeakXR: `SocialHands.Find` returns `Kind.Reach` only when all of that holds (`:213-226`), otherwise the grip falls through silently.

Cue: with an item in hand and a helpable friend at the grip, `HandsFull` "Hands full". Otherwise silence is fine.

### 26. Hold interact cancels when the ray leaves the target

Rule: while a constant interaction runs, if the best interactible is not the held one, the hold is dropped at once (`A/Interaction.cs:128-134`). Cannibal bite (3 s, `CharacterInteractible.cs:199-202`), luggage (`timeToOpen`), cooking and fuse lighting all use it.

VR: grips pin the target (`GrabTarget`) and mitigate it. A pointing hand drifting off a chest does not.

PeakXR: `PhysicalInteraction.UpdateAim` and the `GripPressAction.Interact` path.

Cue: tint the progress ring red and play the deny tick when a hold with progress above 0 is lost to drift, label "Aim drifted".

### 27. Cooking: item not cookable, fire unlit or spent

Rule: a lit campfire is interactible only to someone holding an item with `cooking.canBeCooked` (`A/Campfire.cs:341-356`; `ItemCooking.cs:48`); a spent one never (`:347`). The cook takes `cookTime = 5f` (`:40`). The cooked count is not capped by a refusal: `FinishCookingImpl` increments to 12 (`ItemCooking.cs:175-188`), and benefits drop above 2 cooks and poison starts at 4 (`:213-257`).

VR: `FireGestures.UpdateCook` (`src/Interact/Physical/FireGestures.cs:81-125`) only starts for cookable items over a lit fire, so a non-cookable item over fire gives no feedback.

Cue: hovering a non-cookable held item in the heat column: "Can't cook this" (new `CantCook`). Optionally a tick at each completed cook with the count shown by the item tint (already the game's).

### 28. Equip and switch cooldowns swallow a pickup or draw

Rule: `Item.Interact` ignores a pickup within 0.25 s of the last equip (`A/Item.cs:580`); `lockedFromSwitching` is 0.1 s, or 0.25 s after three quick switches (`CharacterItems.cs:72-84`).

PeakXR: `BodyInventory.SwitchBlocked`, `FindCandidate`.

Cue: `TooSoon` only if the same attempt repeats within 1 s, not on first try.

### 29. Respawn chest and luggage

Rule: closed luggage is the only interactible one (`A/Luggage.cs:168-170`, `:209-211`). A respawn chest revives only when `Ascents.canReviveDead && Character.PlayerIsDeadOrDown()` and otherwise spawns items (`RespawnChest.cs:53-61`). PeakXR also needs both hands on a respawn chest lid (`src/Interact/Physical/LidGesture.cs`, `fraction = both ? ... : 0f`).

VR: a one-handed lift on a respawn chest does not progress.

Cue: `NeedsTwoHands` on the second lid grip when only one hand holds it. An opened case needs no cue.

### 30. Sprint and crouch gestures gated by state

Rule: sprint needs forward input, `CheckSprint` (not climbing, rope, vine or hanging; conscious; held item not in use and `blocksSprint` false, `A/Character.cs:1500-1526`) and, on the ground, regular stamina (`CharacterMovement.cs:263`). Crouch only while grounded and cleared by sprint or 0.2 s airborne (`:241-256`).

PeakXR: `BodyGestures.Apply` (`src/Locomotion/BodyGestures.cs:57-110`).

Cue: when an arm swing meets the sprint threshold but the game would refuse it, nothing visual; a single tick is enough.

### 31. Ping: cooldown, conscious, terrain hit

Rule: `canPing` needs `fullyConscious` and outside `coolDown` (`A/PointPinger.cs:24-47`); the ray must hit the terrain-map layer (`:58`, `:68-72`).

PeakXR: `src/Interact/Ping/PingPatches.cs:49-63`, `PingInput`.

Cue: `TooSoon` on the cooldown; a ping into the sky gets no hit and no cue, which is a short "No surface".

### 32. Eating with no hunger wastes the food

Rule: no refusal. `Action_RestoreHunger` subtracts hunger with no check (`A/Action_RestoreHunger.cs:7`) and `Action_Consume` destroys the item (`Action_Consume.cs:3-9`).

PeakXR: `PhysicalInteraction.Inject` eats at the mouth for `Hold.Edible` items; not edible items do nothing at the mouth.

Cue: optional pre-emptive "Not hungry" tint on the item at the mouth when the Hunger status is 0 and the item has no other benefit; off by default because the game never warns.

### 33. Passed out, struggling or dead

Rule: `Interaction.canInteract` is false when passed out, fully passed out, input blocked or a sticky item is held (`A/Interaction.cs:59-69`). `CharacterItems.Update` skips switching, dropping and using while `isStruggling` (`:183`); `DoUsing` skips when passed out (`:199`). Ping needs `fullyConscious`. Emote wheel and others are off when dead.

PeakXR: `GuardGiveUp` (`src/Social/SocialHands.cs`) and the status UI already cover these states.

Cue: none beyond the existing struggle prompt and spectate UI.

### 34. Emote wheel unavailable

Rule: `GUIManager.canEmote` is false when dead or when the player has won; the wheel does not open while `isClimbing` (`A/GUIManager.cs:312-322`, `:563`).

PeakXR: `LocomotionInput.cs:118` maps the emote action.

Cue: none needed.

## Checked and found to have no refusal

- Eating when full: the game accepts it (case 32).
- Normal bugle cooldown: none; only the magic bugle uses fuel (case 12).
- Throw below minimum charge: `DropItemRpc` handles zero charge as a plain drop (`A/CharacterItems.cs:391-395`); only the sticky case refuses (case 21).
- Dart gun: `FireDart` always fires (`A/Action_RaycastDart.cs:37-60`); its ammo is the normal `ItemUses` gate (case 12).
- Luggage already opened: not interactible, so no prompt, no outline, no grab (case 29).

## What ui.affordance shows

The `ui.affordance` feature (`src/UI/Affordance/`, Core logic in `core/Affordance.cs`) is the one place a refusal is reported. A refusal says why, and it also shows what would work, at the place where the player will try again.

- `Affordance.Deny(left, pos, reason, hint)` is for an attempt that just did nothing: a grip, a trigger press, a release. `Affordance.Hover(left, pos, reason, hint)` is the pre-emptive state. Callers send it every frame while it holds, and it fades 0.15 s after they stop.
- Shape carries the meaning, so the cue reads without colour or words. The refused spot gets a small ring (1.8 degrees across) with a glyph inside: a slash for "no", a bar for "full", a sweeping quarter arc for "wait". A notice is a ring with a pin standing on it. Colour backs this up: red for refused, teal for what works and for notices. A `DenyHint` adds the valid spot as a double ring that breathes slowly (plus or minus 8 % at 1 Hz), joined to the refused spot by a thin line along which a small chevron travels toward the valid spot every 1.2 s, so direction and distance are visible at a glance. The hint is one of: the spot where the action works (`At`), a spot that moves with a transform such as a grip or the wrist (`On`, `Wrist`), or the end of a range plus the distance still to close (`Range`, `Closer`).
- Sound: every cue plays a short quiet tone at its spot, in 3D, through the game's own SFX player and mixer group, so the game's sound volume applies. Refused is two notes falling a fourth (520 to 390 Hz, 0.17 s). Would-work is two notes rising a fifth (523 to 784 Hz), played at the valid spot 0.15 s after the refused one. A notice is one soft bell (880 Hz, 0.22 s). Pure sine tones with soft edges don't resemble the game's foley. A hover sounds once when it starts, at 60 % volume, and at most once per reason and hand every 0.9 s.
- Text is optional: `UI.ActionHintText`, off by default. With it off no label is built. With it on, the label has two lines, the reason and then the next step, with "Move 0.4 m closer" when the hint knows the distance. It uses the game's prompt font, sits above the hand when the refusal is at the hand and above the spot otherwise (below it for an overhang), pulled toward the eye off the surface and never nearer than 0.45 m, and draws over the world. Rings and lines are depth-tested and lifted off the surface they mark, so a ring behind rock stays hidden. A hover earns the label after 0.4 s of steady aim, and one label shows at a time until it fades.
- Haptic: three soft ticks (0.32 amplitude, 20 ms, 70 ms apart). A grip that slides off rock gets three fading ticks instead. Success cues are single pulses and a hazard hold is two strong beats, so neither can be mistaken for this pattern. Strength follows `Interaction.HapticStrength` and `Interaction.Haptics`.
- Rate limit: once per reason and hand every 0.9 s; another reason or hand is never held back. The label fades in over 0.12 s, holds 1.1 s and fades out over 0.4 s, so nothing flashes.
- Settings: `UI.ActionHints` (on), `UI.ReachRay` (on, the pointing ray) and `UI.ActionHintText` (off). With hints off, and in flat play, every call is a no-op; the body slots, the pickup tick and `ParityFeedback` then keep their old haptics.
- `ParityFeedback.Denied` (reach clamp, feed angle, piton time) forwards here and keeps its bare haptic only for the case where ui.affordance did not install.

Every row below plays the refused note at the refused spot (the bell for notices) and, when it has a valid spot, the rising note there. The Label column applies only with `UI.ActionHintText` on. Unless the row says otherwise, the refused glyph is the slash.

| Case | When | Refused mark | What works (double ring and chevron) | Label | Where |
|---|---|---|---|---|---|
| 1 Wall grab, angle | grip on rock the game will not grab, nothing else took the grip | ring at the probed surface, slip ticks | none; the next step is in words | "Overhang / Grip the side instead", "Too flat to climb / Walk up it" | `PhysicalInteraction.OnGripPress`, `ProbeHold` |
| 1 Wall grab, reach | climb start refused, wall over 1.35 m from the eye (1.25 m plus the 0.1 m of the game's thickened retries) | ring at the wall | ring where the 1.25 m head reach ends on the eye-to-wall line | "Too far / Move 0.3 m closer" | `PhysicalInteraction.DenyClimb` |
| 1, 4 Stamina | climb start refused under the game's own rule (within 1 s of the last climb start and out of stamina), or an arm lunge refused while exhausted | ring at the wall or hand | ring on the wrist stamina bar, following the wrist (HUD on wrist only) | "No stamina / Rest to recover" | `DenyClimb`, `TryLunge` |
| 3 Piton range | holding a spike with a finger on its trigger (or a real pull for a pocketed spike while climbing) and the wall past `climbingSpikeStartDistance(Grounded)` by more than 0.1 m, never inside the range; or a press the game refused | aim line turns the deny colour, ring on the wall | ring at the end of the real range on the aim line | "No wall in reach / Move 0.6 m closer" | `src/Interact/Tools/SpikeCue.cs` from the `UpdateClimbingSpikeUse` postfix |
| 3 Piton cast lost | cast cancelled with the button held: aim moved past the preview disable distance | ring at the preview | ring where the disable distance ends | "Moved too far / Move 0.2 m closer" | `SpikeCue` |
| 3 Piton time | all strikes in, vanilla hold time still running | ring with a sweeping arc (wait) at the preview | none; the hold ring shows the time | "Keep holding / Hold the trigger" | `SpikeHammer` via `ParityFeedback` |
| 5 Interaction reach | while a hand points, the menu laser is not showing and the game's own pass (sphere fallback included) found nothing | interactible straight ahead past the reach gets a hover ring | the hand ray is drawn exactly as long as the clamped reach (`Interaction.distance` minus the hand's offset from the camera), with a teal ring at its end when an interactible lies past it | "Too far / Move 0.5 m closer" after 0.4 s; on a press, via `ParityFeedback` | `AffordanceRuntime.UpdateRay`, `InteractPatches.Raycasts` |
| 6 Campfire | flame held on an unlit campfire while a living scout is outside `moraleBoostRadius` | ring at the fire | ring on the farthest missing friend, joined to the fire, only with a line of sight | "Friends too far / Gather everyone here" | `FireGestures.WarnFriendsFar` |
| 7 Pickup | grip on an item with no free slot, or a non-pocketable item while climbing | ring with a bar (full) on the item, in place of the success tick | none | "Pockets full / Drop or stash an item", "Not while climbing" | `OnGripPress`, GrabItem |
| 8 Two-handed item | trigger with a two-handed item in one hand | ring at the holding hand | ring on the item's grip for the free hand (`Hand_L`/`Hand_R`), following the item | "Needs both hands / Grip with the other hand" | `PhysicalInteraction.Inject` |
| 9 Carry | one hand on a passed-out friend for 0.6 s with `CarryBothHands`, once per grip | ring at the gripping hand | ring on the friend's body at the free hand's side | "Needs both hands" | `SocialHands.NudgeCarry` |
| 10, 11 Shooters | finger on the trigger while the shooter's own `WillAttach` fails; or a press | aim line turns the deny colour, hover ring at its end | vine aimed too steep: ring where an aim inside the 0.5 / -0.7 window, same heading, would attach, only if that ray hits terrain in range | "Feet on the ground", "Used up", "No wall in reach", "Too steep up / Aim lower" | `AimReticle`, `src/Interact/UseDenial.cs` |
| 12 Unusable item | trigger on an item whose `CanUsePrimary` is false | ring at the hand, in place of the success tick | none | "Used up", "Can't use this now" | `Inject`, `UseDenial` |
| 14 Throw | a fast release of an item with `canThrow` false | ring at the hand | none | "Can't throw this / Open slowly to drop it" | `OnGripRelease` |
| 15, 24 Holster and stash | a release over a slot or shoulder the game refuses | ring with a bar (full) at the hand; a ring with an arc for "too soon" and "can't switch now" | none | "Can't holster this / Carry or drop it", "Backpack full / Free a pocket first", "Not while climbing", "Too soon", "Can't switch now" | `BodyInventory.DoublePulse` with `BlockReason`; the shoulder stash now also checks `canBackpack` |
| 19 Feed angle | item at a friend's mouth outside the item's feed window | ring at the friend | ring 0.7 m in front of the friend along the middle of the window: where to stand | "Wrong angle / Face them from the ring" | `SocialPatches.Raycasts` via `ParityFeedback` |

| 2 Climb takes the item | holding an item that would drop (not pocketable, or in the overflow slot) while the free hand is in grip range of rock the game would grab | ring on the held item | none | "Climbing drops <item>" after 0.4 s. The climb grip is never refused or delayed | `src/Interact/ClimbStow.cs` `WarnNearWall` |
| 2 Item dropped | a climb, rope, vine or carry start dropped the item | none (a notice: the bell at the item) | teal ring with a pin on the fallen item, for 5 s or until picked up | "<item> fell here" | `ClimbStow.FindDropped`, `MarkDropped` |
| 2 Item stowed | a climb, rope, vine or carry start put the item in its slot | none (a notice: one soft tick, the bell at the slot) | chevron from the hand to the belt slot that now holds it, double ring on the slot | "Stowed on your belt" | `ClimbStow.Tick`, `BodyInventory.SlotWorld` |
| 21 Sticky ball | a gentle release or drop-button tap below the item's `throwChargeRequirement` | ring at the hand | none | "Stuck to your hand / Swing hard to throw it" | `InteractPatches.Dropping` |

Keeping the item in the free hand while climbing is not possible: vanilla equips nothing for every climb start, and peers would see the difference. A hand throw now sends the charge a vanilla throw would need for the hand's speed (`core/HandThrow.cs`), at least the sticky requirement, and takes the look-direction force the master adds for that charge off the item's velocity, so the item still leaves along the hand. That fixes case 21; a gentle release of a sticky item stays refused, as in vanilla.

Not wired yet: case 13 (rope spool anchor), 16 (slack rope), 18 (weak piton), 20, 22, 23 and 25 to 34.

Needs headset tuning: ring size (1.8 degrees) and line width, the deny and hint colours against snow and desert, label offset above the hand, the 0.4 s hover delay, the three-tick amplitude on Index and Quest motors, and whether the pointing ray is welcome or clutter. A friend out of sight gets no ring yet; an edge arrow toward them is not built. Sounds: the 0.3 volume, the 25 m range, and whether the tones sit well under the game mix on both headset speakers and headphones.
