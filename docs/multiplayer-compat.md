# Multiplayer compatibility with vanilla clients

Static audit of master `ebb5b8f` (2026-10-21) against the requirement: a PeakXR player can share a lobby with unmodded players, and those players see correct, non-desynced results. PeakXR paths are relative to `src/PeakXR/` unless they start with `src/`. Decomp paths are relative to `decomp/Assembly-CSharp/` unless they name another folder. Photon behaviour (`Photon.Realtime/...`, `PunEvent.cs`) is cited from `PhotonRealtime.dll` and `PhotonUnityNetworking.dll` in `PEAK_Data/Managed`, decompiled with ilspycmd (not kept in `decomp/`); message-rate limits come from Photon docs and are marked as such. PeakXR line numbers were rechecked against the current source on 2026-10-28. Nothing here was run in game.

## Verdict

Statically, the requirement holds, and the two-client test (run 4, below) agrees for the steps it drives. Vanilla peers see some cosmetic differences, and a few gestures loosen vanilla preconditions.

- PeakXR adds no RPC names, changes no RPC signatures and leaves the game version, Photon AppVersion and the Steam lobby version untouched.
- Vanilla clients never receive the pose event. They see one extra int player property, which PUN stores and the game never reads.
- Every world outcome (pickups, throws, shots, climbs, feeding, carrying, pings, pitons) is decided by the owner or the master and reaches peers through vanilla RPCs or `CharacterSyncer`. No state that peers simulate independently is changed.
- What vanilla peers see differently is presentation: arms, the held item's hand and pose, the aim direction of tools.
- One gesture still loosens a vanilla precondition on purpose: hand feeding of normal items ignores the facing angle (risk 2). Its RPCs carry arguments vanilla can produce, and every client applies the same result, so nothing desyncs. The other loosened gates from the first audit (risks 1, 3, 4, 6, 8, 9) are fixed; see Fixes.

## Version and lobby gates

| Gate | Where the game sets it | PeakXR |
|---|---|---|
| `PhotonNetwork.GameVersion`, `AppSettings.AppVersion` from `Application.version` | `Peak.Network/NetworkingUtilities.cs:155-158`, `Peak.Network/PhotonShim.cs:91-94` | Not written. `Application.version` is only read for logging (`src/PeakXR/Plugin.cs:43`, `src/PeakXR/Sandbox/SelfTestRunner.cs:1297`) |
| Steam lobby data `PeakVersion`, mismatch modal on join | `SteamLobbyHandler.cs:409`, `:332-346` | Not written. Nothing in `src/` (PeakXR, Core, Preloader) writes `SetLobbyData`, room properties, matchmaking, NickName or AuthValues. The MP test script only reads the lobby's `PeakVersion` (`Sandbox/SelfTestRunner.MpTest.cs:564`) |
| Room custom properties | `PhotonShim.cs:114`, `:120` | Not written |
| Kick / ban | Manual host kick only: `PlayerHandler.cs:199-211`, `PhotonShim.cs:157` (event 18); crossplay kick `NetworkConnector.cs:388`; ban check on rejoin `ReconnectHandler.cs:324` | Nothing reacts to mods. The decomp has no anti-cheat, BepInEx, Harmony or mod detection and no rate checks |
| PeakXR to PeakXR | none | The `PeakXR` property's low 16 bits are the pose protocol: bits 0-7 the major version (the packet's version byte), bits 8-15 a minor version; bits 17-30 are reserved and ignored (`src/PeakXR.Core/PeerTable.cs:6-35`). Peers with the same major pair; a different major is logged once per peer with both versions (`Net/NetPoseSync.cs:151-160`). The current value (1, or 1 with the VR bit) is the first release's, so older builds still pair. Additive changes go after the packet's parts as trailing data, which receivers skip up to `PoseCodec.MaxPacketSize` (128 bytes; `src/PeakXR.Core/PoseCodec.cs:57-77`); all eight flag bits are taken |

## Network touchpoints

Sandbox and self-test rows are listed separately below. "Vanilla-producible" means a vanilla client could send the same call with the same argument ranges.

| # | PeakXR (file:line) | Network effect | Decomp | Verdict |
|---|---|---|---|---|
| 1 | `Net/NetPoseSync.cs:144` | `LocalPlayer.SetCustomProperties({"PeakXR": int})` on room join and on VR start/stop; a refused call is retried once a second (`:117-121`) | PUN merges keys (`Photon.Realtime/Player.cs:123-140`) and fires `OnPlayerPropertiesUpdate`; the game's handlers are empty or MarkDirty (`ReconnectHandler.cs:421`, `OrbFogHandler.cs:313`, `CachedPlayerList.cs:148`, `PhotonRoomEvents.cs:31`); game code reads only named keys (`UserID`, `PlatformFamily`) | OK. Re-sent harmlessly when the game re-publishes the local hashtable (`NetworkingUtilities.cs:166-182`) |
| 2 | `Net/NetPoseSync.cs:282` | `RaiseEvent(151, byte[<=44], TargetActors = handshake peers, DoNotCache, Unreliable)`; skipped when no peer (`:239`) | Game event codes 1-4, 18 (`CustomCommandType.cs:3-9`); `Zorro.PhotonUtility/CustomCommandListener.cs:54-62` ignores unknown codes; PUN uses 200-212, 254-255 (`PunEvent.cs:5-21`) | OK. The server delivers it only to PeakXR peers; vanilla never receives it |
| 3 | `Net/NetPoseSync.cs:326-400` | Receives 151 from VR peers only, rate-limited 40/s per sender, validated (version byte, `VrActive` flag, length up to 128 bytes, head and hands within 4 m of the hip; `src/PeakXR.Core/PeerTable.cs:123-172`). The first invalid packet per peer is logged, since another mod may use 151 too | none | OK. Drives remote avatars locally on modded clients only |
| 4 | `Look/LookPatches.cs:129-132` | Writes `data.lookValues` from the HMD; synced by `CharacterSyncer` | `CharacterSyncer.cs:37`, `CharacterSyncData.cs:62`; remote lerp `CharacterSyncer.cs:139-141` | OK. Pitch clamped to ±85 like vanilla (`CharacterMovement.cs:289`). Yaw accumulates like vanilla mouse look and wraps only while no peer can see it (risk 3, fixed) |
| 5 | `Interact/InteractPatches.cs:176-185` | Hand grab target replaces the head ray's pick, after `IsInteractible`; feeds `DoInteraction` → `Item.Interact` → `RequestPickup` | `Interaction.cs:91`, `Item.cs:591` (master validates) | OK. Overrides the carrying-slot-3 short-circuit (`Interaction.cs:198-202`); a grab is within arm's length of the head |
| 6 | `Interact/InteractPatches.cs:139-164` | Interaction ray starts at the pointing hand, not the head | same | OK. The ray is shortened by the hand's offset from the head (`Interact/InteractPatches.cs:160-162`), so picks stay within vanilla's 2 m of the head (risk 4, fixed) |
| 7 | `Interact/InteractPatches.cs:224-308`, `Interact/PhysicalInteraction.cs:1136-1147` | Throw: item velocity clamped to the vanilla full-charge maximum, the charge a vanilla throw would need for the hand's speed, then vanilla `DoDropping` sends `DropItemRpc` | `CharacterItems.cs:266-295`, master spawn `:415-428` | OK. Master applies the velocity for everyone. Item spawns at the tracked hand and no throw animation plays (risk 5) |
| 8 | `Interact/PhysicalInteraction.cs:1461-1467`, `:1503-1509` | `CharacterClimbing.TryToStartWallClimb(forceAttempt, hand direction)` → `StartClimbRpc` | `CharacterClimbing.cs:534` | OK. Same 1.25 m limit and angle check; skips the `sincePressClimb` input gate only |
| 9 | `Interact/PhysicalInteraction.cs:2502-2543`, `Interact/Climb/ClimbHands.cs:231-243` | Sprint/movement flags while climbing → `RPCA_ClimbJump`, rope/vine climb input | `CharacterClimbing.cs:122`; `ropePercent`/`climbPos` sync `CharacterSyncer.cs:41-46` | OK. Nothing writes `ropePercent` or `climbPos` directly |
| 10 | `Interact/Physical/HeldGestures.cs:377-406` | Swaps `lookDirection` to the hand aim for `BugleSFX.UpdateTooting` only, restored after | `BugleSFX.cs:59-61` `RPC_StartToot(clip, pitch)` | OK. Pitch in [0,1] vs vanilla [0.002,0.998]; receiver clamps to [0.01,0.99] |
| 11 | `Social/SocialPatches.cs:104-134` | Feed target (item held at a friend's mouth or torso) replaces the head ray's pick → `Item.StartUseSecondary` → `SendFeedDataRPC`, then `FeedItem` | `Interaction.cs:91`, `Item.cs:984-986`, `:1039-1053` | OK. `IsInteractible` guard (`Social/SocialPatches.cs:119`), so items with `overrideFeedAngle` keep their vanilla window (risk 1, fixed) |
| 12 | `Social/SocialPatches.cs:317-346` | `IsSecondaryInteractible` returns true when the hand feeds, ignoring facing angle (normal items only) | `CharacterInteractible.cs:156-180` | Loosened by design (risk 2). Peers do not re-check the angle |
| 13 | `Social/SocialHands.cs:516-519`, `:526-529`, `:1159` | interact / useSecondary / jump flags: carry, drop, reach, open a friend's pack, struggle | `CharacterCarrying.cs:52`, `:88`; `CharacterGrabbing.cs:122`, `:253`; `BackpackOnBackVisuals.cs:154` | OK. Vanilla re-gates each (`CanBeCarried`, target selection, `IsInteractible`) |
| 14 | `Interact/Physical/FireGestures.cs:188`, `:287` → `PhysicalInteraction.cs:2334-2339` | Campfire / fuse interact via the grab target → `Light_Rpc`, cook cast | `Campfire.cs:336` | OK. `IsInteractible` checked; no line-of-sight test |
| 15 | `Interact/Physical/ItemGesturesPatches.cs:64`, `Interact/Physical/LidGesture.cs:301-306` | Caps `currentInteractableHeldTime` (luggage lid) | luggage cast finish | OK. Can only slow the cast |
| 16 | `Interact/Tools/SpikeHammer.cs:94-97` | Hammer strikes advance `climbingSpikeTick` → piton `InstantiateItemRoom` | `CharacterItems.cs:1080` | OK. Strikes speed the timer up to at most `HammerCast.MaxSpeedup` (1.5x) the vanilla hold's pace (`src/PeakXR.Core/HammerCast.cs:33-35`, `Interact/Tools/SpikeHammer.cs:80-97`), a third sooner than holding (risk 6, fixed) |
| 17 | `Interact/Aim/AimPatches.cs` (RescueHook ray replaced at `:217`, prefix at `:222`) | Camera swapped to the hand aim for rope/vine shooters, dart, rescue hook, constructables, antizooka, cloner | `GetShot`, `ForceBuildVine_RPC`, `RPC_DartImpact`, `RPCA_Rescue*`, `Constructable.cs:171`, `Action_CloneSelectedItem.cs:51` | OK. Owner computes the hit and sends it; vanilla sees the muzzle along the look, the shot along the hand |
| 18 | `Interact/Ping/PingPatches.cs:50-100` | Replaces `PointPinger.TryGetPingHit` with a hand ray, same mask and range | `PointPinger.cs:71` `ReceivePoint_Rpc` | OK |
| 19 | `Inventory/InventoryPatches.cs:53-57`, `Locomotion/LocomotionPatches.cs:51`, `Inventory/BodyInventory.cs:596-610` | Slot keys 0-3 → `EquipSlot` via `DoSwitching` | vanilla switching | OK |
| 20 | `Locomotion/LocomotionInput.cs:193-276`, `Locomotion/BodyGestures.cs:204-212`, `Interact/Physical/ParasolRunner.cs:93`, `Interact/Lights/HandLights.cs:162-164`, `Interact/Tools/ToolInput.cs:141-215`, `Interact/Ping/PingInput.cs:191-293` | Boolean and clamped analog input | `movementInput` synced as sign flags (`CharacterSyncData.cs:68-83`) | OK. Same quantisation as a gamepad |
| 21 | `UI/RunFlow/RunFlowPatches.cs:229-279` | VR Skip mode drops the beach `Fall(7)`, so `RPCA_Fall` is not sent | `Character.cs:2032-2036` | OK. Same as the game's own `SkipWakeup` tag |
| 22 | `Avatar/AvatarPatches.cs:51-220`, `Avatar/AvatarPoseDriver.Apply.cs` | Hand IK, held-item frame, one-hand joint, fingers, remote head bone | Not synced: arms are local; held items are recomputed per client (`ItemPhysicsSyncer.cs:55`, `:89-105`; `CharacterItems.cs:170-179`, `:641-651`, `:706`) | Cosmetic divergence (risk 7) |
| 23 | `Emotes/EmotesFeature.cs:82`, `Comfort/ComfortEventsPatches.cs:59-587` | Postfixes on RPC receivers (`RPCA_PlayRemove`, `RPCA_Fall`, `RPCA_AddForce*`, `RPCA_LaunchTarget`, `RPCA_CaptureCharacter`). Cutscene shakes are zeroed around the game's own `Update`, and mirage particles are cleared after the game's `fadeMirage` (`:460-587`). The only skipping prefix left is on the mirror camera | none | OK. Observe only; no RPC suppressed or altered |
| 24 | `UI/Lobby/InviteNote.cs:130-141`, `UI/Menu/LoadingSpace.cs:419` | Reads room name and level-load progress | none | OK. Read only |
| 25 | `Inventory/BodyInventory.cs:487-488` | A hand reaching over the shoulder opens your own worn backpack's wheel with the call the worn pack's interaction makes (`Interact_CastFinished`); moving items in or out then goes through the vanilla wheel | `BackpackOnBackVisuals.cs:162-172` | Loosened by design: vanilla opens a worn pack only from another player's interaction, or after putting it down. The wheel's item moves are the ones a friend opening the pack sends, so peers see the same results. A worn rocket pack is refused, so the reach never lights it |
| 26 | `Interact/Climb/ClimbHands.cs:665-734` | A rhythm of leans along a rope's swing pushes the whole rope (`AddForce` on its segment bodies), only on a rope this client owns | `Rope.cs:122`, `:155` (the owner simulates; vanilla syncs the segments) | OK. Peers, vanilla included, see the rope swing through vanilla sync, and anyone climbing it swings with it, as with any owner-side physics. On a rope someone else owns, the pump is refused at the hand (`NotHere`) and nothing is sent |

### Sandbox and self-test

| PeakXR (file:line) | Network effect | Gate | Verdict |
|---|---|---|---|
| `Sandbox/SandboxActions.cs:231`, `:353`, `:421-467` | `InstantiateItemRoom`, `InstantiateRoomObject`, `Instantiate`, `RPCA_InitTornado` | `Active` = `Enabled` and offline (`:92`); `Enabled` = config `Sandbox.Enabled` (default false, `SandboxFeature.cs:31-39`) or `Forced`. Every action checks it first (`Guard`, `:115-134`) | Off in normal play, and never in a lobby, hosted ones included: offline (Play Solo) only |
| `Sandbox/SandboxActions.cs:508-655` | God mode (local stamina/status flags), `Character.Revive`, `WarpToSpawn`, `MapHandler.JumpToSegment`, `DayNightManager.setTimeOfDay` | same | Offline cheats through the game's own console paths |
| `Sandbox/SelfTestFeature.cs:101-126`, `Sandbox/SandboxActions.cs:138-142` | A Play Solo run that used any sandbox action, or opened the console the sandbox unlocks, keeps the player's quicksave and grants no achievements, stats or ascents until the next title or airport (`SelfTestProgressGuard`, `SandboxRunMark`). The player is told once, at the off hand in VR | offline only | No network effect. It keeps a cheated solo run out of the real profile |
| `Sandbox/SandboxPatches.cs:24-30` | Skips `CheckFallDamage` for the local character | `GodMode` and `Active` | Local only |
| `Sandbox/SandboxActions.cs:488-500` | Local `MobManager` stand-in | refused when the room has more than one player | OK. Late joiners still throw once (existing `shortcut:` note) |
| `Sandbox/SelfTestRunner*.cs` (`RPCA_AddForceAtPosition` `VR.cs:991-997`, `WarpPlayerRPC` `Hands.cs:673`, `Climb.cs:179`, `Items.cs:1114`, and more, buffered `GetShot` `Climb.cs:581-583`, `PhotonNetwork.Destroy` `Hands.cs:2018`, `SelfTestRunner.cs:1408`) | Vanilla RPCs | Feature installs only with launch flag `-peakxr-selftest` (`SelfTestFeature.cs:51-52`), which sets `Forced` (`:77`) and starts through Play Solo (offline, `SelfTestRunner.cs:277-291`) | Off in normal play. `Forced` counts only offline and is dropped once this client is in an online room (`SandboxActions.cs:77-85`); `Ready` requires offline mode (`SelfTestRunner.cs:72-76`) (risk 8, fixed). The two-client test script (`-peakxr-mptest`, `Sandbox/MpTestFeature.cs`) is the one online runner: it never turns the sandbox on and sends only vanilla RPCs on its own character |
| `Sandbox/SelfTestRunner.MpTest.cs:1990`, `:2057-2063` | `mp.self-wake` revives A where it lies, in a real online room: statuses cleared, then `RPCA_ReviveAtPosition` to all | Only with the launch flag `-peakxr-mptest` (`Sandbox/MpTestFeature.cs:24`) | Test-only, never in normal play. The RPC is vanilla and targets its own character, but no vanilla player can revive themselves |

PEAK_MX is a separate third-party mod (`docs/research/creative-test-mode.md:21`) used alongside for flat tests. PeakXR has no code that references or depends on it, and its own network effects are out of scope here. Do not load it in lobbies with vanilla players.

## Master client

When the PeakXR player hosts, the master-only paths it reaches are the vanilla ones: `RequestPickup` validation, `DropItemRpc` spawning with the client-sent velocity (`CharacterItems.cs:415-428`), piton `InstantiateItemRoom` (`CharacterItems.cs:1080`) and `info.Sender.IsMasterClient` checks on host-authority RPCs (`Player.cs:199`, `CharacterSpawner.cs:434`, `:527`, `:553`, `OrbFogHandler.cs:136`). PeakXR patches none of these, buffers no RPC (only the self-test's `GetShot` and `TriggerHelicopter` use `AllBuffered`) and does not touch segment advance, spawning, fog or `NetworkConnector` beyond void comfort-fade prefixes on `LeaveRoom`/`OnLeftRoom` (`Comfort/ComfortEventsPatches.cs:596-631`). Hosting adds nothing: the sandbox acts only offline.

## Bandwidth and rate

- The game sets `SendRate` and `SerializationRate` to 30 (`NetworkingUtilities.cs:153-154`).
- A pose packet is at most 44 bytes of payload plus about 30 bytes of Photon framing, sent at up to 20-30 Hz (default 25, `Net/NetPoseFeature.cs:49-57`), and only when the pose changed or every 0.25 s as a keepalive (`src/PeakXR.Core/PoseSendGate.cs:23-41`). That is about 1.9 KB/s upload per VR player at most.
- The send rate drops as PeakXR peers join (`Net/NetPoseSync.cs:270`, `src/PeakXR.Core/PoseSendGate.cs:53-75`): 250/N² Hz for N PeakXR players, kept between 5 Hz and the configured rate. Receivers stretch the interpolation delay to 1.5 send intervals (`Net/NetPoseSync.cs:406`), so remote hands still interpolate at 5 Hz, 300 ms behind. The remote's look yaw, which the pose's frame turns with, plays back with the same delay (`YawHistory`, `:428-432`), so head and snap turns do not swing the hands.
- With no PeakXR peers, nothing is sent: a VR player among vanilla players adds zero event traffic.
- Photon counts a message as one send plus one per receiver. Docs-based, not code-cited: the room guideline is about 500 msg/s. Vanilla `CharacterSyncer` traffic is about 30·N² msg/s (480 for 4 players). With the scaled rate, an all-VR lobby adds about 250 pose msg/s for 3 to 7 players (15.6 Hz each at 4 players) and 320 msg/s for 8 (5 Hz floor). Exceeding the guideline triggers server warnings and possible throttling, not a per-client kick.

## Risks

| # | Severity | Where | Problem | Suggested fix |
|---|---|---|---|---|
| 1 | Medium, fixed | `Social/SocialPatches.cs:82-113` | The feed target replaces the ray pick without `IsInteractible`. For items with `overrideFeedAngle`, `FeedAngle` keeps vanilla's `false`, but the Raycasts postfix still makes the friend `bestCharacter`, so `SendFeedDataRPC` and `FeedItem` go out at an angle vanilla refuses. Consistent on all clients, but outside vanilla preconditions | Guard with `target.IsInteractible(Character.localCharacter)` as `Interact/InteractPatches.cs:176-185` does |
| 2 | Low | `Social/SocialPatches.cs:291-321` | Feeding normal items ignores facing angle. Intended for hand feeding; arguments are vanilla-valid | Keep, or require the giver's hand within the vanilla angle cone of the receiver's look |
| 3 | Low, fixed | `src/PeakXR.Core/LookMath.cs:78-82` via `Look/LookPatches.cs:129-132` | Past ±2048° the yaw is pulled back by 1800°. Remotes plain-`Lerp` `lookValues` (`CharacterSyncer.cs:139-141`), so every peer sees the avatar spin five turns within one packet interval, and the held item whips with it | Pull back only while dead, passed out, warping or loading; or drop the pull-back and accept 2° steps like vanilla mouse look |
| 4 | Low, fixed for interaction and constructables | `Interact/InteractPatches.cs:139-213`, `Interact/Aim/AimPatches.cs` (`ConstructableUpdate` shortens `maxConstructDistance` by the hand's offset for the call) | Interact and aim start at the hand: effective reach about 2.5-2.8 m vs vanilla 2 m; the grab overrides the carrying-slot-3 pick. Master still validates pickups | Measure the interaction distance from the head, or subtract the hand offset from the max distance |
| 5 | Low, partly fixed | `Interact/InteractPatches.cs:224-308` (the `CharacterItems.DoDropping` prefix) | Hand throws spawn the item at the tracked hand, a visible pop on vanilla screens. They used to send charge 0, so vanilla saw no throw animation | The charge is fixed: a throw now sends the charge a vanilla throw would need for the hand's speed and takes the master's look impulse off the item's velocity (row 10 covers the wind-up it skips). The pop remains; skip the hand throw when the hand is far from the hold point |
| 6 | Low, fixed | `Interact/Tools/SpikeHammer.cs:94-97` | Three quick strikes finish the piton faster than `usingTimePrimary` | Floor the tick at the elapsed real cast time |
| 7 | Low (cosmetic, visible) | `Avatar/AvatarPatches.cs:49-160`, `Avatar/AvatarPoseDriver.Apply.cs` | Vanilla peers see default look-driven arms and a right-hand hold; a left-hand hold or an aimed tool looks different from what the VR player sees | Not fixable without a vanilla-side change. Document it; optionally bias `lookValues` toward the tool aim on the shot frame |
| 8 | Low, fixed | `Sandbox/SelfTestRunner.cs:72`, `Sandbox/SelfTestFeature.cs:77` | With `-peakxr-selftest-hold`, `Forced` stays on after the script stops; a host session started later in the same process has the sandbox active | Gate `Ready` and `Forced` on `PhotonNetwork.OfflineMode`, or clear `Forced` when the runner stops |
| 9 | Low, fixed | `Net/NetPoseSync.cs:237-284` | All-modded lobbies of 4+ VR players can exceed Photon's ~500 msg/s room guideline (docs) | Scale the send rate down with peer count, or batch when more than 3 peers |
| 10 | Low, accepted | `Interact/InteractPatches.cs:224-308` (the `CharacterItems.DoDropping` prefix) | A VR hand throw sends the charge that matches the measured hand speed, so it skips the wind-up. A vanilla throw holds the drop button past `delayBeforeThrowCharge` and then charges at `1 / throwChargeTime` per second (`CharacterItems.cs:299-301`; fields at :17, :23). The force is capped at vanilla's full-charge maximum (`minThrowForce + (maxThrowForce - minThrowForce) * charge`, :394, :428), so a VR player throws no harder than anyone else, only sooner: a small edge over vanilla players | Leave it. A charge-time gate would make hand throws feel laggy; adding one is a design decision |
| 11 | Low, fixed | `Interact/PhysicalInteraction.cs` (`OnGripPress`, GrabItem), `Interact/InteractPatches.cs` (`KeepHeldOnPocket`) | A free-hand pocketing pickup with every pocket full went to the temporary slot 250 (`Player.cs:137-141`) while the equip that vanilla always follows it with (`CharacterItems.cs:457`) was skipped: an un-held item in slot 250, synced to every peer, one more item than vanilla can carry | The grip now refuses it up front (pockets-full cue), and an answer that still lands in slot 250 keeps vanilla's equip, which swaps the held item: that hand gets a notice, not the success tick |
| 12 | Low, accepted | `Interact/InteractPatches.cs` (the `CharacterItems.DoDropping` prefix) | The throw takes the master's look impulse off the item's velocity using the local `lookValues`, while the master (when that is another player) adds it from its own lagged, lerped copy (`CharacterItems.cs:415-427`, `CharacterSyncer.cs:139-141`). Turning the head while throwing bends the throw by the look change over one sync interval, at most the charge's look force (`minThrowForce`..`maxThrowForce` x 0.5 x `throwForceMultiplier`) times the angle | Leave it. The master's copy is not visible to the thrower, and sending charge 0 would lose vanilla's throw animation, bonks and sticky-item release. The host's own throws are exact |

No high-severity risk found.

## Fixes

Fixed after the audit.

| Risk | Fix |
|---|---|
| 1 | The social raycast postfix replaces the pick only when `target.IsInteractible(localCharacter)` holds, the same check the ray's own pick makes (`Interaction.cs:233`): `Social/SocialPatches.cs:98`. Items with `overrideFeedAngle` keep their vanilla window (`CharacterInteractible.cs:167-172`). Two prefabs set it (read from `resources.assets` with UnityPy; 3 of 373 `Item` behaviours did not parse): `Amulet_Healing` (0 to 360°, so always allowed) and `RitualDagger` (100 to 180°, so only from behind). The dagger was the real loophole |
| 3 | No pull-back while anyone can see the avatar. `LookMath.ContinueYaw` (`src/PeakXR.Core/LookMath.cs:78`) wraps the yaw to one turn only when `Unobserved` (`Look/LookPatches.cs:184-188`) holds: offline, alone in the room, or dead (the body is parked at `Character.DeathPos`, `Character.cs:558`, `:758`). Past 8192° (8° half-float steps, `LookMath.cs:68`) it wraps anyway as a safety net, which takes 23 net turns without dying or being alone |
| 4 | The hand interaction ray is shortened by the hand's distance from the head (`Interact/InteractPatches.cs:160-162`, restored by the finalizer at `:209-213`), so a pick is never more than `Interaction.distance` (2 m, `Interaction.cs:7`) from the head. Aim tools still fire from the hand: their ranges are tens of metres and the owner computes the hit |
| 6 | `HammerCast.TickBeforeUpdate` caps the timer at `HammerCast.MaxSpeedup` (1.5x) the vanilla hold's pace (`src/PeakXR.Core/HammerCast.cs:33-35`), which `SpikeHammer` tracks from the cast start (`Interact/Tools/SpikeHammer.cs:84`, `:96-97`). A full set of strikes places the spike a third sooner than holding |
| 8 | `SandboxActions.Forced` counts only in offline mode and is cleared for good once the client is in an online room (`Sandbox/SandboxActions.cs:77-85`). `Ready` requires offline mode (`Sandbox/SelfTestRunner.cs:72-76`). The runner clears `Forced` on a finish without hold (`:1235-1236`) and when destroyed (`:85-88`). With `-peakxr-selftest-hold` the offline sandbox stays on for live-input tests until the game quits or goes online |
| 9 | `PoseRate.ForPeers` (`src/PeakXR.Core/PoseSendGate.cs:65`), called at `Net/NetPoseSync.cs:270`, sends at 250/N² Hz for N PeakXR players, clamped to 5 Hz at the low end and the configured rate at the top. `PoseRate.Delay` (`Net/NetPoseSync.cs:406`) raises the receive delay to 1.5 send intervals. Tests: `PoseRateTests` in `tests/PeakXR.Core.Tests/NetPoseTests.cs` |

A refused gesture never fails silently. Each refusal calls `ParityFeedback.Denied(left, worldPos, reason)` (`Interact/ParityFeedback.cs`), which plays a low, long "denied" pulse, unlike the short success ticks, and logs at Debug. It fires at most once per 0.75 s per reason. Call sites:

- "Too far": interact pressed, nothing picked, and the full-length hand ray hits an interactible past the shortened range (`Interact/InteractPatches.cs:115`, called at `:187`).
- "Wrong angle": an `overrideFeedAngle` item held at a friend's mouth for the dwell time outside its feed window (`Social/SocialPatches.cs:128`).
- "Keep holding": every hammer strike landed before the capped cast time ran out (`Interact/Tools/SpikeHammer.cs:99-100`).

The shared affordance service (`src/PeakXR/UI/Affordance/`) is meant to take over this hook and add the red outline and the reason label.

## Live single-client check (2026-10-21)

PeakXR in VR (master `ad2932f`, Vulkan, SteamVR with the ALVR virtual headset) hosted a real online lobby, through the game's own host flow driven by the read-only `MpObserver` plugin. It advertised `peakVersion 2.6` and game `2.6.b`, the same values unmodded PEAK writes. It stayed online in the airport for over two minutes with no exceptions in the log. The only RPC seen was the game's own `EquipSlotRpc` for the passport, and nothing else went out, since no PeakXR peer was in the room. This shows what an unmodded player would see when browsing for or joining the lobby. It does not replace the two-client test below.

### Two clients on one machine

Client B is the unmodded PEAK on a second Steam account, in a second Steam install on the same machine, with only `MpObserver`. Client A is PeakXR on the first account. Early attempts failed: A died about 28 s after launch with a Steam client assert (`stalled cross-thread pipe`) whenever B's game ran. Run 4 (2026-10-22) got through, with A in VR and B unmodded: 16 pass, 0 fail, 2 skip, and `tools/mp-check.sh` is clean. The two skips are `eat` and `feed`, because no food lay within 40 m. The logs do not record what changed since the early attempts.

What run 4 confirmed: B saw A join, A's version check passed (lobby 2.6 equals local 2.6, master is the host), and B logged no exceptions, no unknown-event errors and no event 151. A sent none. B's view of A matched A on position and held item for grab, drop, pickup, throw (landed 7.8 m away), holster, climb and the two-handed rope spool. Emote, ping and suitcase opening reached B as `RPCA_PlayRemove`, `ReceivePoint_Rpc` and `OpenLuggageRPC`. Rerun it with `tools/mp-check.sh [mp-dir]` (see "Two-client multiplayer test" in `docs/testing.md`). The held-item check accepts the first matching sample within `LAG_TOL` (1.0 s by default), because B can write its checkpoint before A's equip RPC arrives.

## What the two-client test has not fully confirmed

Run 4 covered item 1 for its own steps (no event 151 on B, no kick) and the thrown item's landing in item 3. The rest is by hand.

1. Vanilla clients log nothing for the `PeakXR` property or event 151, and are never kicked.
2. Remote lerp of `lookValues` looks smooth for head-driven yaw, including snap turns and past 2048° (risk 3 fix).
3. Thrown items, rope and vine shots, pitons, darts and the rescue hook land in the same place on both screens.
4. Feeding, carrying, reaching and pack-opening by hand succeed on the vanilla side with the expected effects.
5. Held items show sanely on the vanilla screen when the VR player holds left-handed and two-handed.
6. Measured Photon traffic per VR player matches the estimate.

### Test plan

Steps 1, 3, 5, 6 (feeding) and 9 (ping) also run unattended with A in VR and B hosting: see "Two-client multiplayer test" in `docs/testing.md`.

Setup: client A is PeakXR in VR (SteamVR first, launch options from the README) with `BepInEx/LogOutput.log` at Debug. Client B is a second Steam account on vanilla PEAK (no BepInEx), on the same build. Run each step twice: A hosts, then B hosts. Keep `Sandbox.Enabled=false`, no PEAK_MX, no `-peakxr-*` flags.

1. Lobby. A hosts a private lobby; B joins by invite. Expect no version-mismatch modal and no kick. On A, the log shows `[net.pose]` with zero peers and no events sent. On B, `Player.log` has no new errors.
2. Look. A turns their head, snap-turns 20 times one way (past 2048°), and looks up and down. B watches A's avatar: head and body follow without jitter. Expect no multi-turn spin (risk 3 fix).
3. Items. A picks up an item by hand, swaps slots, drops it, and throws it at a marked target. B confirms the same slot, the item in A's hand, and the landing spot. Repeat left-handed.
4. Tools. A fires the rope shooter, vine shooter, a dart and the rescue hook at a marked wall or at B, and places a piton with the hammer. Both screens show the same anchor, hit and piton position.
5. Climbing. A starts a wall climb by grabbing, lunges, climbs a rope and a vine. B sees the climb start, the jump and rope progress at the same height.
6. Social. A feeds B by holding food at B's mouth, from the front and then from the side. A heals B by pressing a bandage to B's torso. B passes out; A reaches and helps B up, carries B, drops B, and opens B's pack. B's stats and inventory match on both screens.
7. Fire. A lights a campfire by hand and cooks food over it. B sees the fire lit and the item cooked.
8. Beach wake-up. Start a run with VR Skip mode on. B sees A standing up without the fall, as with the game's SkipWakeup tag.
9. Ping and bugle. A pings by pointing and plays the bugle at different pitches. B sees the ping in the same place and hears the pitch change.
10. Traffic. Repeat step 2 with a second PeakXR client C in VR and record Photon traffic stats (`PhotonNetwork.NetworkingClient.LoadBalancingPeer.TrafficStatsGameLevel`) on A for 60 s. Expect about 25 pose events/s while moving and about 4/s while still. With 4 PeakXR clients, expect about 15/s while moving.

Pass: B shows no errors or desync in any step, and nothing differs between screens except arm pose and held-item hand (risk 7).
