# Multiplayer compatibility with vanilla clients

Static audit of master `ebb5b8f` (2026-10-21) against the requirement: a PeakXR player can share a lobby with unmodded players, and those players see correct, non-desynced results. PeakXR paths are relative to `src/PeakXR/` unless they start with `src/`. Decomp paths are relative to `decomp/Assembly-CSharp/` unless they name another folder. Photon behaviour (`Photon.Realtime/...`, `PunEvent.cs`) is cited from `PhotonRealtime.dll` and `PhotonUnityNetworking.dll` in `PEAK_Data/Managed`, decompiled with ilspycmd (not kept in `decomp/`); message-rate limits come from Photon docs and are marked as such. Nothing here was run in game.

## Verdict

Statically, the requirement holds. Vanilla peers see some cosmetic differences, and a few gestures loosen vanilla preconditions.

- PeakXR adds no RPC names, changes no RPC signatures and leaves the game version, Photon AppVersion and the Steam lobby version untouched.
- Vanilla clients never receive the pose event. They see one extra int player property, which PUN stores and the game never reads.
- Every world outcome (pickups, throws, shots, climbs, feeding, carrying, pings, pitons) is decided by the owner or the master and reaches peers through vanilla RPCs or `CharacterSyncer`. No state that peers simulate independently is changed.
- What vanilla peers see differently is presentation: arms, the held item's hand and pose, the aim direction of tools.
- One gesture still loosens a vanilla precondition on purpose: hand feeding of normal items ignores the facing angle (risk 2). Its RPCs carry arguments vanilla can produce, and every client applies the same result, so nothing desyncs. The other loosened gates from the first audit (risks 1, 3, 4, 6, 8, 9) are fixed; see Fixes.

## Version and lobby gates

| Gate | Where the game sets it | PeakXR |
|---|---|---|
| `PhotonNetwork.GameVersion`, `AppSettings.AppVersion` from `Application.version` | `Peak.Network/NetworkingUtilities.cs:123-126`, `Peak.Network/PhotonShim.cs:91-94` | Not written. `Application.version` is only read for logging (`src/PeakXR/Plugin.cs:35`, `src/PeakXR/Sandbox/SelfTestRunner.cs:675`) |
| Steam lobby data `PeakVersion`, mismatch modal on join | `SteamLobbyHandler.cs:409`, `:332-345` | Not written. No `SetLobbyData`, room properties, matchmaking, NickName or AuthValues anywhere in `src/` (PeakXR, Core, Preloader) |
| Room custom properties | `PhotonShim.cs:114`, `:120` | Not written |
| Kick / ban | Manual host kick only: `PlayerHandler.cs:198-211`, `PhotonShim.cs:157` (event 18); crossplay kick `NetworkConnector.cs:375`; ban check on rejoin `ReconnectHandler.cs:290` | Nothing reacts to mods. The decomp has no anti-cheat, BepInEx, Harmony or mod detection and no rate checks |

## Network touchpoints

Sandbox and self-test rows are listed separately below. "Vanilla-producible" means a vanilla client could send the same call with the same argument ranges.

| # | PeakXR (file:line) | Network effect | Decomp | Verdict |
|---|---|---|---|---|
| 1 | `Net/NetPoseSync.cs:126` | `LocalPlayer.SetCustomProperties({"PeakXR": int})` on room join and on VR start/stop | PUN merges keys (`Photon.Realtime/Player.cs:123-140`) and fires `OnPlayerPropertiesUpdate`; the game's handlers are empty or MarkDirty (`ReconnectHandler.cs:357`, `OrbFogHandler.cs:313`, `CachedPlayerList.cs:148`, `PhotonRoomEvents.cs:31`); game code reads only named keys (`UserID`, `PlatformFamily`) | OK. Re-sent harmlessly when the game re-publishes the local hashtable (`NetworkingUtilities.cs:138-151`) |
| 2 | `Net/NetPoseSync.cs:244-245` | `RaiseEvent(151, byte[<=44], TargetActors = handshake peers, DoNotCache, Unreliable)`; skipped when no peer (`:205`) | Game event codes 1-4, 18 (`CustomCommandType.cs:3-8`); `Zorro.PhotonUtility/CustomCommandListener.cs:54-62` ignores unknown codes; PUN uses 200-212, 254-255 (`PunEvent.cs:5-21`) | OK. The server delivers it only to PeakXR peers; vanilla never receives it |
| 3 | `Net/NetPoseSync.cs:289-352` | Receives 151 from VR peers only, rate-limited 40/s per sender, plausibility-checked (`src/PeakXR.Core/PeerTable.cs:105-142`) | none | OK. Drives remote avatars locally on modded clients only |
| 4 | `Look/LookPatches.cs:104` | Writes `data.lookValues` from the HMD; synced by `CharacterSyncer` | `CharacterSyncer.cs:37`, `CharacterSyncData.cs:268`; remote lerp `CharacterSyncer.cs:139-141` | OK. Pitch clamped to ±85 like vanilla (`CharacterMovement.cs:288`). Yaw accumulates like vanilla mouse look and wraps only while no peer can see it (risk 3, fixed) |
| 5 | `Interact/InteractPatches.cs:58-67` | Hand grab target replaces the head ray's pick, after `IsInteractible`; feeds `DoInteraction` → `Item.Interact` → `RequestPickup` | `Interaction.cs:91`, `Item.cs:591` (master validates) | OK. Overrides the carrying-slot-3 short-circuit (`Interaction.cs:198-202`); a grab is within arm's length of the head |
| 6 | `Interact/InteractPatches.cs:44-51` | Interaction ray starts at the pointing hand, not the head | same | OK. The ray is shortened by the hand's offset from the head (`Interact/InteractPatches.cs:119`), so picks stay within vanilla's 2 m of the head (risk 4, fixed) |
| 7 | `Interact/InteractPatches.cs:98-113`, `Interact/PhysicalInteraction.cs:924-929` | Throw: item velocity clamped to the vanilla full-charge maximum, charge 0, then vanilla `DoDropping` sends `DropItemRpc` | `CharacterItems.cs:266-295`, master spawn `:420-428` | OK. Master applies the velocity for everyone. Item spawns at the tracked hand and no throw animation plays (risk 5) |
| 8 | `Interact/PhysicalInteraction.cs:492` | `CharacterClimbing.TryToStartWallClimb(forceAttempt, hand direction)` → `StartClimbRpc` | `CharacterClimbing.cs:535` | OK. Same 1.25 m limit and angle check; skips the `sincePressClimb` input gate only |
| 9 | `Interact/PhysicalInteraction.cs:966-970`, `Interact/Climb/ClimbHands.cs:264-324` | Sprint/movement flags while climbing → `RPCA_ClimbJump`, rope/vine climb input | `CharacterClimbing.cs:122`; `ropePercent`/`climbPos` sync `CharacterSyncer.cs:41-46` | OK. Nothing writes `ropePercent` or `climbPos` directly |
| 10 | `Interact/Physical/HeldGestures.cs:150-165` | Swaps `lookDirection` to the hand aim for `BugleSFX.UpdateTooting` only, restored after | `BugleSFX.cs:59-61` `RPC_StartToot(clip, pitch)` | OK. Pitch in [0,1] vs vanilla [0.002,0.998]; receiver clamps to [0.01,0.99] |
| 11 | `Social/SocialPatches.cs:73-84` | Feed target (item held at a friend's mouth or torso) replaces the head ray's pick → `Item.StartUseSecondary` → `SendFeedDataRPC`, then `FeedItem` | `Interaction.cs:91`, `Item.cs:984-986`, `:1039-1053` | OK. `IsInteractible` guard (`Social/SocialPatches.cs:91`), so items with `overrideFeedAngle` keep their vanilla window (risk 1, fixed) |
| 12 | `Social/SocialPatches.cs:104-122` | `IsSecondaryInteractible` returns true when the hand feeds, ignoring facing angle (normal items only) | `CharacterInteractible.cs:156-180` | Loosened by design (risk 2). Peers do not re-check the angle |
| 13 | `Social/SocialHands.cs:174-186`, `:295-321`, `:454` | interact / useSecondary / jump flags: carry, drop, reach, open a friend's pack, struggle | `CharacterCarrying.cs:52`, `:88`; `CharacterGrabbing.cs:122`, `:253`; `BackpackOnBackVisuals.cs:154` | OK. Vanilla re-gates each (`CanBeCarried`, target selection, `IsInteractible`) |
| 14 | `Interact/Physical/FireGestures.cs:113`, `:149` → `PhysicalInteraction.cs:846` | Campfire / fuse interact via the grab target → `Light_Rpc`, cook cast | `Campfire.cs:324` | OK. `IsInteractible` checked; no line-of-sight test |
| 15 | `Interact/Physical/ItemGesturesPatches.cs:57`, `Interact/Physical/LidGesture.cs:99` | Caps `currentInteractableHeldTime` (luggage lid) | luggage cast finish | OK. Can only slow the cast |
| 16 | `Interact/Tools/SpikeHammer.cs:60-68` | Hammer strikes advance `climbingSpikeTick` → piton `InstantiateItemRoom` | `CharacterItems.cs:1080` | OK. Strikes never push the timer past the vanilla hold timer (`src/PeakXR.Core/HammerCast.cs:38`, `Interact/Tools/SpikeHammer.cs:63-74`) (risk 6, fixed) |
| 17 | `Interact/Aim/AimPatches.cs` (RescueHook ray replaced at `:143`, prefix at `:152`) | Camera swapped to the hand aim for rope/vine shooters, dart, rescue hook, constructables, antizooka, cloner | `GetShot`, `ForceBuildVine_RPC`, `RPC_DartImpact`, `RPCA_Rescue*`, `Constructable.cs:171`, `Action_CloneSelectedItem.cs:51` | OK. Owner computes the hit and sends it; vanilla sees the muzzle along the look, the shot along the hand |
| 18 | `Interact/Ping/PingPatches.cs:49-63` | Replaces `PointPinger.TryGetPingHit` with a hand ray, same mask and range | `PointPinger.cs:71` `ReceivePoint_Rpc` | OK |
| 19 | `Inventory/InventoryPatches.cs:45`, `Locomotion/LocomotionPatches.cs:44`, `Inventory/BodyInventory.cs:131` | Slot keys 0-3 → `EquipSlot` via `DoSwitching` | vanilla switching | OK |
| 20 | `Locomotion/LocomotionInput.cs:117-193`, `Locomotion/BodyGestures.cs:100-108`, `Interact/Physical/ParasolRunner.cs:93`, `Interact/Lights/HandLights.cs:161`, `Interact/Tools/ToolInput.cs:120-160`, `Interact/Ping/PingInput.cs:65-108` | Boolean and clamped analog input | `movementInput` synced as sign flags (`CharacterSyncData.cs:274-289`) | OK. Same quantisation as a gamepad |
| 21 | `UI/RunFlow/RunFlowPatches.cs:203-234` | VR Skip mode drops the beach `Fall(7)`, so `RPCA_Fall` is not sent | `Character.cs:2034-2039` | OK. Same as the game's own `SkipWakeup` tag |
| 22 | `Avatar/AvatarPatches.cs:67-105`, `Avatar/AvatarPoseDriver.Apply.cs:157-312` | Hand IK, held-item frame, one-hand joint, fingers, remote head bone | Not synced: arms are local; held items are recomputed per client (`ItemPhysicsSyncer.cs:56`, `:97-121`; `CharacterItems.cs:170-179`, `:640-651`, `:706`) | Cosmetic divergence (risk 7) |
| 23 | `Emotes/EmotesFeature.cs:68`, `Comfort/ComfortEventsPatches.cs:65-521` | Postfixes on RPC receivers (`RPCA_PlayRemove`, `RPCA_Fall`, `RPCA_AddForce*`, `RPCA_LaunchTarget`, `RPCA_CaptureCharacter`); skipping prefixes only on shake, mirror, mirage visuals | none | OK. Observe only; no RPC suppressed or altered |
| 24 | `UI/Lobby/InviteNote.cs:116-124`, `UI/Menu/LoadingSpace.cs:143` | Reads room name and level-load progress | none | OK. Read only |

### Sandbox and self-test

| PeakXR (file:line) | Network effect | Gate | Verdict |
|---|---|---|---|
| `Sandbox/SandboxActions.cs:172`, `:274`, `:347-390` | `InstantiateItemRoom`, `InstantiateRoomObject`, `Instantiate`, `RPCA_InitTornado` | `Active` = `Enabled` and (offline or master) (`:76`); `Enabled` = config `Sandbox.Enabled` (default false, `SandboxFeature.cs:30-38`) or `Forced` | Off in normal play. When a host turns it on, spawns go through vanilla master paths and stay consistent |
| `Sandbox/SandboxActions.cs:426-520` | God mode (local stamina/status flags), `Character.Revive`, `WarpToSpawn`, `MapHandler.JumpToSegment`, `DayNightManager.setTimeOfDay` | same | Host-only cheats through the game's own console paths |
| `Sandbox/SandboxPatches.cs:107-113` | Skips `CheckFallDamage` for the local character | `GodMode` and `Active` | Local only |
| `Sandbox/SandboxActions.cs:404-418` | Local `MobManager` stand-in | refused when the room has more than one player | OK. Late joiners still throw once (existing `shortcut:` note) |
| `Sandbox/SelfTestRunner*.cs` (`RPCA_AddForceAtPosition` `VR.cs:442`, `WarpPlayerRPC` `Hands.cs:420`, `Climb.cs:365`, `Items.cs:255`, buffered `GetShot` `Climb.cs:185`, `PhotonNetwork.Destroy` `Hands.cs:928`, `SelfTestRunner.cs:739`) | Vanilla RPCs | Feature installs only with launch flag `-peakxr-selftest` (`SelfTestFeature.cs:33-34`), which sets `Forced` (`:45`) and starts through Play Solo (offline, `SelfTestRunner.cs:190-205`) | Off in normal play. `Forced` counts only offline and is dropped once this client is in an online room (`SandboxActions.cs:72-80`); `Ready` requires offline mode (`SelfTestRunner.cs:63-67`) (risk 8, fixed) |

PEAK_MX is a separate third-party mod (`docs/research/creative-test-mode.md:21`) used alongside for flat tests. PeakXR has no code that references or depends on it, and its own network effects are out of scope here. Do not load it in lobbies with vanilla players.

## Master client

When the PeakXR player hosts, the master-only paths it reaches are the vanilla ones: `RequestPickup` validation, `DropItemRpc` spawning with the client-sent velocity (`CharacterItems.cs:420-428`), piton `InstantiateItemRoom` (`CharacterItems.cs:1080`) and `info.Sender.IsMasterClient` checks on host-authority RPCs (`Player.cs:195`, `CharacterSpawner.cs:434`, `:527`, `:553`, `OrbFogHandler.cs:136`). PeakXR patches none of these, buffers no RPC (only the self-test's `GetShot` uses `AllBuffered`) and does not touch segment advance, spawning, fog or `NetworkConnector` beyond void comfort-fade prefixes on `LeaveRoom`/`OnLeftRoom` (`Comfort/ComfortEventsPatches.cs:495-515`). The sandbox is the only host-only addition, and it is off by default.

## Bandwidth and rate

- The game sets `SendRate` and `SerializationRate` to 30 (`NetworkingUtilities.cs:121-122`).
- A pose packet is at most 44 bytes of payload plus about 30 bytes of Photon framing, sent at up to 20-30 Hz (default 25, `Net/NetPoseFeature.cs:49-57`), and only when the pose changed or every 0.25 s as a keepalive (`src/PeakXR.Core/PoseSendGate.cs:23-41`). That is about 1.9 KB/s upload per VR player at most.
- The send rate drops as PeakXR peers join (`Net/NetPoseSync.cs:233`, `src/PeakXR.Core/PoseSendGate.cs:53-80`): 250/N² Hz for N PeakXR players, kept between 5 Hz and the configured rate. Receivers stretch the interpolation delay to 1.5 send intervals (`Net/NetPoseSync.cs:358`), so remote hands still interpolate at 5 Hz, 300 ms behind.
- With no PeakXR peers, nothing is sent: a VR player among vanilla players adds zero event traffic.
- Photon counts a message as one send plus one per receiver. Docs-based, not code-cited: the room guideline is about 500 msg/s. Vanilla `CharacterSyncer` traffic is about 30·N² msg/s (480 for 4 players). With the scaled rate, an all-VR lobby adds about 250 pose msg/s for 3 to 7 players (15.6 Hz each at 4 players) and 320 msg/s for 8 (5 Hz floor). Exceeding the guideline triggers server warnings and possible throttling, not a per-client kick.

## Risks

| # | Severity | Where | Problem | Suggested fix |
|---|---|---|---|---|
| 1 | Medium, fixed | `Social/SocialPatches.cs:73-84` | The feed target replaces the ray pick without `IsInteractible`. For items with `overrideFeedAngle`, `FeedAngle` keeps vanilla's `false`, but the Raycasts postfix still makes the friend `bestCharacter`, so `SendFeedDataRPC` and `FeedItem` go out at an angle vanilla refuses. Consistent on all clients, but outside vanilla preconditions | Guard with `target.IsInteractible(Character.localCharacter)` as `Interact/InteractPatches.cs:61-64` does |
| 2 | Low | `Social/SocialPatches.cs:104-122` | Feeding normal items ignores facing angle. Intended for hand feeding; arguments are vanilla-valid | Keep, or require the giver's hand within the vanilla angle cone of the receiver's look |
| 3 | Low, fixed | `src/PeakXR.Core/LookMath.cs:42-48` via `Look/LookPatches.cs:104` | Past ±2048° the yaw is pulled back by 1800°. Remotes plain-`Lerp` `lookValues` (`CharacterSyncer.cs:139-141`), so every peer sees the avatar spin five turns within one packet interval, and the held item whips with it | Pull back only while dead, passed out, warping or loading; or drop the pull-back and accept 2° steps like vanilla mouse look |
| 4 | Low, fixed for interaction | `Interact/InteractPatches.cs:44-67`, `Interact/Aim/AimPatches.cs` | Interact and aim start at the hand: effective reach about 2.5-2.8 m vs vanilla 2 m; the grab overrides the carrying-slot-3 pick. Master still validates pickups | Measure the interaction distance from the head, or subtract the hand offset from the max distance |
| 5 | Low | `Interact/InteractPatches.cs:98-113` | Hand throws spawn the item at the tracked hand (visible pop on vanilla screens) with charge 0, so vanilla sees no throw animation | Optionally send a small charge and subtract the master's look impulse; skip the hand throw when the hand is far from the hold point |
| 6 | Low, fixed | `Interact/Tools/SpikeHammer.cs:60-68` | Three quick strikes finish the piton faster than `usingTimePrimary` | Floor the tick at the elapsed real cast time |
| 7 | Low (cosmetic, visible) | `Avatar/AvatarPatches.cs:67-105`, `Avatar/AvatarPoseDriver.Apply.cs` | Vanilla peers see default look-driven arms and a right-hand hold; a left-hand hold or an aimed tool looks different from what the VR player sees | Not fixable without a vanilla-side change. Document it; optionally bias `lookValues` toward the tool aim on the shot frame |
| 8 | Low, fixed | `Sandbox/SelfTestRunner.cs:63`, `Sandbox/SelfTestFeature.cs:45` | With `-peakxr-selftest-hold`, `Forced` stays on after the script stops; a host session started later in the same process has the sandbox active | Gate `Ready` and `Forced` on `PhotonNetwork.OfflineMode`, or clear `Forced` when the runner stops |
| 9 | Low, fixed | `Net/NetPoseSync.cs:203-247` | All-modded lobbies of 4+ VR players can exceed Photon's ~500 msg/s room guideline (docs) | Scale the send rate down with peer count, or batch when more than 3 peers |

No high-severity risk found.

## Fixes

Fixed after the audit. Line numbers are for the fixing commit; the risk rows above keep the original locations.

| Risk | Fix |
|---|---|
| 1 | The social raycast postfix replaces the pick only when `target.IsInteractible(localCharacter)` holds, the same check the ray's own pick makes (`Interaction.cs:233`): `Social/SocialPatches.cs:91`. Items with `overrideFeedAngle` keep their vanilla window (`CharacterInteractible.cs:167-172`). Two prefabs set it (read from `resources.assets` with UnityPy; 3 of 373 `Item` behaviours did not parse): `Amulet_Healing` (0 to 360°, so always allowed) and `RitualDagger` (100 to 180°, so only from behind). The dagger was the real loophole |
| 3 | No pull-back while anyone can see the avatar. `LookMath.ContinueYaw` (`src/PeakXR.Core/LookMath.cs:45`) wraps the yaw to one turn only when `Unobserved` (`Look/LookPatches.cs:114`) holds: offline, alone in the room, or dead (the body is parked at `Character.DeathPos`, `Character.cs:561`, `:761`). Past 8192° (8° half-float steps, `LookMath.cs:35`) it wraps anyway as a safety net, which takes 23 net turns without dying or being alone |
| 4 | The hand interaction ray is shortened by the hand's distance from the head (`Interact/InteractPatches.cs:119`, restored by the finalizer at `:66`), so a pick is never more than `Interaction.distance` (2 m, `Interaction.cs:7`) from the head. Aim tools still fire from the hand: their ranges are tens of metres and the owner computes the hit |
| 6 | `HammerCast.TickBeforeUpdate` caps the timer at the vanilla hold timer (`src/PeakXR.Core/HammerCast.cs:38`), which `SpikeHammer` tracks from the cast start (`Interact/Tools/SpikeHammer.cs:63`, `:74`). Strikes can only slow the cast |
| 8 | `SandboxActions.Forced` counts only in offline mode and is cleared for good once the client is in an online room (`Sandbox/SandboxActions.cs:72-80`). `Ready` requires offline mode (`Sandbox/SelfTestRunner.cs:63-67`). The runner clears `Forced` on a finish without hold (`:624`) and when destroyed (`:78`). With `-peakxr-selftest-hold` the offline sandbox stays on for live-input tests until the game quits or goes online |
| 9 | `PoseRate.ForPeers` (`src/PeakXR.Core/PoseSendGate.cs:65`), called at `Net/NetPoseSync.cs:233`, sends at 250/N² Hz for N PeakXR players, clamped to 5 Hz at the low end and the configured rate at the top. `PoseRate.Delay` (`Net/NetPoseSync.cs:358`) raises the receive delay to 1.5 send intervals. Tests: `PoseRateTests` in `tests/PeakXR.Core.Tests/NetPoseTests.cs` |

A refused gesture never fails silently. Each refusal calls `ParityFeedback.Denied(left, worldPos, reason)` (`Interact/ParityFeedback.cs`), which plays a low, long "denied" pulse, unlike the short success ticks, and logs at Debug. It fires at most once per 0.75 s per reason. Call sites:

- "Too far": interact pressed, nothing picked, and the full-length hand ray hits an interactible past the shortened range (`Interact/InteractPatches.cs:92`, called at `:144`).
- "Wrong angle": an `overrideFeedAngle` item held at a friend's mouth for the dwell time outside its feed window (`Social/SocialPatches.cs:100`).
- "Keep holding": every hammer strike landed before the vanilla hold time ran out (`Interact/Tools/SpikeHammer.cs:77`).

The shared affordance service (`src/PeakXR/UI/Affordance/`) is meant to take over this hook and add the red outline and the reason label.

## What only a live two-client test can confirm

1. Vanilla clients log nothing for the `PeakXR` property or event 151, and are never kicked.
2. Remote lerp of `lookValues` looks smooth for head-driven yaw, including snap turns and past 2048° (risk 3 fix).
3. Thrown items, rope and vine shots, pitons, darts and the rescue hook land in the same place on both screens.
4. Feeding, carrying, reaching and pack-opening by hand succeed on the vanilla side with the expected effects.
5. Held items show sanely on the vanilla screen when the VR player holds left-handed and two-handed.
6. Measured Photon traffic per VR player matches the estimate.

### Test plan

Steps 1, 3, 5, 6 (feeding) and 9 (ping) also run unattended with A in VR and B hosting: see "Two-client multiplayer test" in `docs/testing.md`.

Setup: client A is PeakXR in VR (SteamVR first, Proton launch options from the test notes) with `BepInEx/LogOutput.log` at Debug. Client B is a second Steam account on vanilla PEAK (no BepInEx), on the same build. Run each step twice: A hosts, then B hosts. Keep `Sandbox.Enabled=false`, no PEAK_MX, no `-peakxr-*` flags.

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
