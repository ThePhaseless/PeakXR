# VR immersion audit: flat UI and button-driven gameplay

Audit of what PEAK still presents as a flat 2D panel, or drives through a button, key, mouse-look or canned animation, compared with what PeakXR already does on master `2b72965`. It lists only real gaps and clear upgrades. Read-only: nothing was built or run.

The "Game now" and "PeakXR now" notes describe that commit. Most candidates have shipped since; the Status column in the ranked summary names the file that implements each one.

Out of scope, because other work covers them: hotbar slots 1-4 on the hips or wrist picked by grabbing, and menu panel backdrops.

Paths: `A/` is `decomp/Assembly-CSharp/`. PeakXR paths are relative to the repo root. Row ids such as IC-30 point into `docs/mechanics.md`, which has the per-mechanic inventory and test steps.

## Rules every proposal follows

- Vanilla compatibility: each proposal ends in the input the game already reads (`CharacterInput` booleans, `movementInput`, interact hold) or in an RPC argument vanilla already sends. None adds an RPC or changes `CharacterSyncData`. Vanilla peers see the normal game result. Modded peers also see the tracked hands through `net.pose`.
- Hands: each hand acts on its own, and a player holds one item at a time. "Free hand" means the hand that is not holding the item.
- Every gesture keeps the button path as a fallback and has an on/off setting under its area's section. Gestures run only while `VRSession.Active`.
- Effort: S is under a day with one patch point, M is a few days or several patch points, L is a week or more, or needs new art or a new model.

## Ranked summary

Ranked by user impact per unit of effort. Every row is detailed further down.

| Rank | Id | Candidate | Effort | Status |
|---|---|---|---|---|
| 1 | G1 | Rope and vine climbing hand over hand | M | Done: `src/PeakXR/Interact/Climb/ClimbHands.cs` (`Interaction.RopeHandOverHand`) |
| 2 | U1 | Interact prompt and hold ring at the target, not on the HUD panel | S | Done: `src/PeakXR/UI/Spatial/TargetPrompt.cs` |
| 3 | G2 | Physical crouch from real head height | S | Done: `src/PeakXR/Locomotion/BodyGestures.cs` (`Locomotion.PhysicalCrouch`) |
| 4 | G3 | Cook by holding the item over the fire | S | Done: `src/PeakXR/Interact/Physical/FireGestures.cs` (`Interaction.CookOverFire`) |
| 5 | G4 | Open luggage and respawn chests by lifting the lid | M | Done: `src/PeakXR/Interact/Physical/LidGesture.cs` (`Interaction.LiftLidToOpen`) |
| 6 | U2 | Item prompts as labels on the controller buttons | M | Done: `src/PeakXR/UI/Spatial/ButtonLabels.cs` |
| 7 | G5 | Bugle raised to the mouth, pitch from the bell angle | S | Done: `src/PeakXR/Interact/Physical/HeldGestures.cs` (`Interaction.BugleAtMouth`) |
| 8 | G6 | Light fuses and fires by touching them with a flame | M | Done: `src/PeakXR/Interact/Physical/FireGestures.cs` (`Interaction.LightWithFlame`) |
| 9 | G7 | Grab a piton handle to hang from it | S | Done: `src/PeakXR/Interact/Climb/ClimbHands.cs` (`Interaction.PitonGripHang`) |
| 10 | U3 | Biome title and event banners as world signage | S | Done: `src/PeakXR/UI/Spatial/WorldBanners.cs` |
| 11 | U4 | Rope length and item fuel shown on the held item | S | Done: `src/PeakXR/UI/Spatial/HeldGauges.cs` |
| 12 | G8 | Bandages and status items pressed to the body | S | Done: `src/PeakXR/Interact/Physical/HeldGestures.cs` (`Interaction.PressToBody`) |
| 13 | U5 | Poke (direct touch) on every menu panel | M | Partial: pokes work on the title menu (`src/PeakXR/UI/Menu/TitleMenuDepth.cs`), the passport (`src/PeakXR/UI/Passport/PassportPoke.cs`) and the keyboard; other panels use the laser |
| 14 | U6 | Spectate name and controls hint on the spectated player | S | Done: `src/PeakXR/UI/Spatial/SpectateView.cs` |
| 15 | G9 | Arm-swing sprint | M | Done: `src/PeakXR/Locomotion/BodyGestures.cs` (`Locomotion.ArmSwingSprint`, on by default) |
| 16 | G10 | Warm your hands at the campfire | S | Done: `src/PeakXR/Interact/Physical/FireGestures.cs` (`Interaction.WarmHandsAtFire`) |
| 17 | U7 | Wrist menu: pause and quick toggles on the off-hand wrist | M | Open |
| 18 | G11 | High five and fist bump between VR players | M | Open |
| 19 | U8 | Controls page replaced by a labelled controller model | M | Open |
| 20 | G12 | Parasol opened by pushing the runner | S | Done: `src/PeakXR/Interact/Physical/ParasolRunner.cs` (`Interaction.ParasolRunner`) |
| 21 | U9 | End-of-run timeline as a mountain diorama | L | Open |
| 22 | U10 | Boarding pass as a ticket held in the hand | M | Open: the pass is still a docked panel (`src/PeakXR/UI/Lobby/KioskDock.cs`) |

## Part 1. UI

### U1. Interact prompt and hold ring at the target (S)

- Game now: one hovered target, `Interaction.currentHovered`, picked by a 2 m camera ray (`A/Interaction.cs:81`, hold logic in `DoInteraction` at :106). `GUIManager.RefreshInteractablePrompt` (`A/GUIManager.cs:1045`) writes `interactName` and `interactPromptPrimary/Secondary/Hold/Lunge` (:84-96) as HUD text. The hold ring is `UI_UseItemProgress` (`A/UI_UseItemProgress.cs:10-54`), fed by `Interaction.constantInteractableProgress`. Campfire rewrites the prompt every frame, including its "who is missing" list (`A/Campfire.cs:261-265`).
- PeakXR now: the prompt texts stay on the body-locked HUD panel; `CanvasRules` has no rule for them and `HudSplit` does not move them (`src/PeakXR/UI/World/CanvasRules.cs`, `src/PeakXR/UI/World/HudLayout.cs`). The hold ring already moves above the acting hand (`src/PeakXR/UI/HandWidgets/HandWidgetsRuntime.cs`). The reticle is head-locked at 2 m, while the interact ray comes from the pointing hand (`src/PeakXR/Interact/InteractPatches.cs:31`), so the two can disagree.
- VR treatment: re-parent the `interactName` group into a `HudBox` (the pattern `HudSplit` already uses) and pose it every frame at `currentHovered.Center()`. Offset it 15 cm up and toward the head, billboard it on yaw only, and clamp it to 0.4 to 2 m from the eye. While a hand is pointing, add a short line from the fingertip to the label. Keep the ring at the hand, and pulse the hand once when the target changes. The `HudBox` is posed every frame, so the label follows moving targets such as carried players. Hide the HUD copy while the world label shows.
- Comfort and readability: world-locked text at arm's length is easy to read; scale it to a fixed angular size. Hysteresis on target changes stops flicker between two nearby items.
- Multiplayer: local only.

### U2. Item prompts as labels on the controller buttons (M)

- Game now: `GUIManager.UpdateItemPrompts` (`A/GUIManager.cs:1170`) shows `itemPromptMain/Scroll/Secondary/Drop/Throw` (:102-110) from `Item.UIData` (`A/Item.cs:39`), with glyphs from `InputIcon`. Lantern swaps its text at runtime (`A/Lantern.cs:150`).
- PeakXR now: the text stays on the HUD panel, with gamepad glyphs that do not match the VR layout (`docs/controls.md`).
- VR treatment: draw small labels on the holding hand's controller or glove: "Eat" by the trigger, "Scroll" by the stick, "Drop" by B. Read the game's own strings so they stay localised and follow runtime swaps such as Lantern. Show them for 3 s after an item is drawn and whenever the hand turns palm-up toward the face, so they are not always on. Map each prompt to the binding PeakXR actually uses: the drop button from `InteractConfig.DropButton`, scroll from the stick or twist.
- Comfort: none. The labels must not block the item.
- Multiplayer: local only.
- Note: this needs a controller or glove anchor per hand. `VRRig` grip transforms are enough; no controller models are needed.

### U3. Biome title and event banners as world signage (S)

- Game now: `GUIManager.SetHeroTitle` (`A/GUIManager.cs:484`) renders the biome title to an RT shown as a RawImage on the HUD. Callers are `MountainProgressHandler:105, :187`, `TombTrigger:26` and `Peak/ForceHeroTextTrigger:45`. The 4 s banners `TheFogRises`, `TheLavaRises`, `TheGloomRises` and `TheGhostsRise` start at :1195.
- PeakXR now: they ride the HUD panel; the dim background `heroBG` is disabled (`src/PeakXR/UI/World/HudLayout.cs`).
- VR treatment: on `SetHeroTitle`, move the hero RawImage to a large quad 25 to 40 m ahead, world-locked, facing the player along the path up (head yaw at trigger time), slightly above the horizon. Fade it in and out with the vanilla timing. The fog or lava banners go to the same spot, tinted.
- Comfort: a distant, world-locked banner beats one fixed to the body. Keep it out of the lower field of view while climbing.
- Multiplayer: local.

### U4. Rope length and item fuel on the held item (S)

- Game now: `UI_Rope` (`A/UI_Rope.cs`, driven by `GUIManager.UpdateRope` :635) draws a spiral and "x.xxm" on the HUD. Fuel and uses bars are on the hotbar slot (`A/InventoryItemUI.cs` TrySetFuel :159, from `UseRemainingPercentage`): lantern, bugle, flare, jetpack and others.
- PeakXR now: rope payout is physical (`src/PeakXR/Interact/Tools/ToolInput.cs`, `PullRope`, `TwistScroll`), but the length readout stays on the HUD. Fuel lives on the hotbar, which is being reworked.
- VR treatment: put the rope UI on a 6 cm quad tangent to the spool in the hand. Show item fuel as a thin arc on the held item, near the grip, using the same value. Coordinate with the hotbar work so slots show it too.
- Comfort: none.
- Multiplayer: local.

### U5. Poke on every menu panel (M)

- Game now: every menu is a mouse or gamepad canvas.
- PeakXR now: menus are world panels used with the laser (`src/PeakXR/UI/World/LaserPointer.cs`). Only the VR keyboard takes pokes (`src/PeakXR/UI/Keyboard/VRKeyboard.cs`).
- VR treatment: when a menu panel is within 0.6 m, switch that hand from laser to fingertip. Hover is the point on the panel nearest the fingertip, a press happens 1 cm past the surface, and a tick fires. Reuse the keyboard's poke code. Pull close panels to within arm's reach on open: the pause menu, kiosk panels, passport tabs, settings. Sliders drag along the finger.
- Comfort: arm fatigue if a panel sits high. Place close panels at chest height, tilted 20 to 30 degrees up.
- Multiplayer: local.

### U6. Spectate name and controls hint on the spectated player (S)

- Game now: `GUIManager.UpdateSpectate` (`A/GUIManager.cs:599`) shows `spectatingNameText` and, if dead, `spectatingInputs` (:134) on the HUD. The switch inputs are hotbar keys (`A/MainCameraMovement.cs:193-200`).
- PeakXR now: a detached follow view; a stick flick switches with a fade (`src/PeakXR/Rig/RigDriver.cs`, `docs/controls.md`). The name and hint stay on the HUD panel, and the hint shows keyboard or gamepad glyphs.
- VR treatment: show the spectated name as a nametag above that player (reuse the `UIPlayerNames` placement in `src/PeakXR/UI/World/WorldPatches.cs:71`). Replace the hint with left and right chevrons by the turn-stick hand. Put the dying or give-up bar on the off-hand wrist while spectating; it already moves there in wrist mode.
- Comfort: none.
- Multiplayer: local.

### U7. Wrist menu (M)

- Game now: pause opens on `pauseWasPressed` (`A/GUIManager.cs:420`). The room code, invite button and player list (mute, volume, kick) all sit in the pause main page (`A/PauseMenuMainPage.cs:33-35, :133`; `A/AudioLevelSlider.cs:10`).
- PeakXR now: the menu button opens the pause panel in front of the head; a long press recenters (`docs/controls.md`).
- VR treatment: turn the off-hand palm toward the face and hold 0.4 s to raise a small wrist panel. It holds poke buttons for pause, recenter, mute (mirroring `GUIManager.muteIcon`, :78), the room code and the HUD mode. It only calls existing pause-page methods. The menu button keeps working.
- Comfort: a dwell time stops accidental opens while climbing. Disable it while a hand is on a wall.
- Multiplayer: kick and mute go through the vanilla `PlayerHandler.Kick` and `AudioLevels` calls.

### U8. Controls page replaced by a labelled controller model (M)

- Game now: `PauseMenuControlsPage` lists 26 `PauseMenuRebindButton`s for KBM or gamepad (`A/PauseMenuControlsPage.cs:9`).
- PeakXR now: the page shows game bindings that do not apply in VR. The VR bindings exist only in `docs/controls.md` and the VR settings tab.
- VR treatment: in VR, hide the rebind list and show two floating controller silhouettes labelled from the live PeakXR binding table: dominant hand, `DropButton`, ping mode. Reuse the label anchors from U2.
- Multiplayer: local.

### U9. End-of-run timeline as a mountain diorama (L)

- Game now: `EndScreen` (`A/EndScreen.cs:11`) draws a 2D timeline with a pip per event (`DrawPip` :657; died, revived, passed out, won), an altitude per scout (`UpdateAltitude` :466), then ascent and badge popups.
- PeakXR now: a wide laser panel (`src/PeakXR/UI/RunFlow/EndScreenPlacement.cs`).
- VR treatment: a 1.2 m tabletop diorama of the mountain profile (biome bands, from the timeline's biome and height data) with a coloured path per scout and pip markers at height. Keep the vanilla panel beside it for buttons and badges. Grabbing the diorama rotates it.
- Comfort: none.
- Multiplayer: local; reads the same `TimelineInfo`.
- Effort is high because it needs the biome heights mapped to a model. Do it last.

### U10. Boarding pass as a ticket held in the hand (M)

- Game now: hold-interact at the kiosk (`A/AirportCheckInKiosk.cs:111`) opens `BoardingPass` (`A/BoardingPass.cs:12`): ascent +/-, custom options, Start.
- PeakXR now: the panel is docked at the kiosk and used with the laser (`src/PeakXR/UI/Lobby/KioskDock.cs`).
- VR treatment: the pass appears in the hand that triggered the kiosk, at ticket size (about 25 cm), and is used by poking (U5). Handing it back to the kiosk slot (within 15 cm) calls `StartGame`. Custom options stay a docked panel.
- Multiplayer: Start goes through vanilla `LoadIslandMaster`.

### UI: no gap found

- Nametags: world-placed (`src/PeakXR/UI/World/WorldPatches.cs:71`), with speaking icons (`src/PeakXR/Voice/SpeakingIndicators.cs`).
- Emote and backpack wheels: at the hand (`src/PeakXR/UI/Wheels/RadialWheels.cs`).
- Passport and guidebook: books at the hand (`src/PeakXR/UI/Passport/HandBook.cs`).
- Binoculars: lens render texture (`src/PeakXR/Interact/Binoculars/BinocularLens.cs`).
- Keyboard: `src/PeakXR/UI/Keyboard/VRKeyboard.cs`.
- Status flashes: peripheral rim (`src/PeakXR/UI/Status/PeripheralRim.cs`).
- Friend item-use feed: on the hand (`src/PeakXR/UI/HandWidgets/HandWidgetsRuntime.cs`).
- Loading and title: 3D spaces (`src/PeakXR/UI/Menu/LoadingSpace.cs`, `src/PeakXR/UI/Menu/TitleStage.cs`).
- Credits and cutscene: cinema screen (`src/PeakXR/UI/RunFlow/CinemaScreen.cs`).
- Compass: already a 3D needle on the item (`A/CompassPointer.cs`). The game has no map item.
- Throw charge bar (`A/GUIManager.cs:656`): only matters for the button fallback, since hand throws skip the charge (`src/PeakXR/Interact/InteractPatches.cs:84`).
- Struggle prompt (web): covered by `StruggleByShaking` (`src/PeakXR/Social/SocialHands.cs`).
- Small toasts (quicksave `A/GUIManager.cs:1265`, `AscentUI`, `EndgameCounter`): low value. Leave them on the HUD panel. `UI_Notifications` has no caller.
- Airport mirror: renders from one point and looks flat (`ComfortEvents.Mirrors`). This is a stereo rendering fix, not a UI one, and is not ranked here.

## Part 2. Gameplay and interactions

### G1. Rope and vine climbing hand over hand (M)

- Game now: interact on a `RopeSegment` grabs the rope (`A/RopeSegment.cs:39`, RPC `GrabRopeRpc`). After that, `CharacterRopeHandling` moves along it with `movementInput.y` (`A/CharacterRopeHandling.cs:63-67`), and jump lets go. Vines are the same: `A/CharacterVineClimbing.cs:68` reads `movementInput.y`, with RPCs `GrabVineRpc` and `RPCA_SyncVineClimb`.
- PeakXR now: wall climbing is physical, arm-pull locomotion with regrip grace (`src/PeakXR/Interact/PhysicalInteraction.cs` `UpdateClimb` :612). A grip near a rope or vine reaches it through the vanilla interact, but then you climb it with the stick. Room-scale follow is turned off on ropes (`src/PeakXR/Locomotion/LocomotionInput.cs:276`).
- VR treatment: while on a rope or vine, a gripping hand near the rope line (within 12 cm of the segment axis) anchors. Pulling that hand down along the rope converts to `movementInput.y`, using the same gain approach as `UpdateClimb`, scaled against `climbSpeed * GetMove()`. Hand over hand works with the regrip grace. Releasing both hands for more than the grace period, or pressing jump, lets go. Vines: hanging from both hands with no pull lets the vanilla slide run. Each grab gives rope-texture haptics, plus the stamina beat already used on walls. The stick stays as a fallback.
- Comfort: the motion is vertical and self-driven, like wall climbing. The rope swing is the risk; keep the view on the rope position (vanilla) and add the vignette on swing speed.
- Multiplayer: only `movementInput` changes; position syncs as vanilla.
- Test: sandbox rope spool or rope shooter (IT-01, IT-02), plus a vine segment in the jungle biome.

### G2. Physical crouch (S)

- Game now: `crouchIsPressed` or the toggle (`A/CharacterMovement.cs:237-255`), grounded only.
- PeakXR now: a turn-stick click (`src/PeakXR/Locomotion/LocomotionConfig.cs:43`).
- VR treatment: when the tracked head drops more than 30 percent of calibrated standing height (hysteresis: 30 percent in, 22 percent out), set `crouchIsPressed`. Seated mode treats seated height as standing (`docs/controls.md`), so it keeps working there. The view stays at the real head: only the body and collider crouch.
- Comfort: none, the motion is your own. A floor calibration error is the risk; re-calibrate on recenter.
- Multiplayer: vanilla crouch sync.

### G3. Cook by holding the item over the fire (S)

- Game now: hold interact at a lit campfire with a cookable item held (`A/Campfire.cs:300-309`, `cookTime = 5` at :40). `ItemCooking.StartCookingVisuals` (`A/ItemCooking.cs:190`) starts on press and `FinishCooking` (:264, RPC `SyncCookingStateRPC`) on cast end. A hot dog or marshmallow uses the same path (IC-30..38).
- PeakXR now: no special handling. You point and hold the grip like any interactable.
- VR treatment: while the held item's bounds sit inside a cylinder above the fire (radius 0.6 m, 0.2 to 1.0 m above the base), hold `interactIsPressed` with the campfire as the forced hover target. Leaving the cylinder cancels, which is the vanilla `CancelCast`. Warm haptics grow with progress, using the existing quarter-step ring. Forcing the hover target reuses the `DoInteractableRaycasts` patch (`src/PeakXR/Interact/InteractPatches.cs:31`).
- Comfort: none.
- Multiplayer: vanilla interact path and RPCs.

### G4. Open luggage and chests by lifting the lid (M)

- Game now: luggage is an `IInteractibleConstant` held for `timeToOpen` (`A/Luggage.cs:27, :214`), then `OpenLuggageRPC` (:72) and the "Luggage_Unclasp" animation (:68). `RespawnChest` inherits it (`A/RespawnChest.cs`).
- PeakXR now: a grip on the luggage holds the vanilla interact, with the ring at the hand (`src/PeakXR/Interact/PhysicalInteraction.cs`, `FindCandidate` :397).
- VR treatment: grip on the top half of the luggage bounds grabs the lid edge. Lifting the hand drives the hold: `interactIsPressed` stays on while the hand rises, and pauses (not cancels) while it stays still for up to 0.5 s. Map hand height (0 to 25 cm) onto the vanilla progress, with clasp-click haptics at quarter steps. Respawn chests use the same gesture with both hands (TwoHanded).
- Comfort: none.
- Multiplayer: vanilla RPC. The lid's look stays vanilla because the animation plays on completion; no fake lid is drawn.
- Note: the cast runs on time, not distance, so a fast lift still waits for `timeToOpen`. Tune the 25 cm so the lift takes about as long as the cast.

### G5. Bugle to the mouth, pitch from the bell (S)

- Game now: `BugleSFX.UpdateTooting` (`A/BugleSFX.cs:43-66`) starts on `isUsingPrimary` and sends `RPC_StartToot(clip, pitch)`, with pitch taken from `lookDirection` against up (:59-60). The raise animation is `defaultPos.y` (:117). The magic bugle burns fuel while tooting (`A/MagicBugle.cs:64`) (IT-26).
- PeakXR now: trigger to toot; pitch comes from head pitch.
- VR treatment: the mouthpiece within the `MouthGesture` radius (`src/PeakXR.Core/MouthGesture.cs`) holds primary, so you play it by putting it to your lips. For the pitch, swap `lookDirection` to the bell direction for the duration of `UpdateTooting`, using the swap pattern in `src/PeakXR/Interact/CameraSwap.cs`. Hand at the mouth already means talking for voice (`HandToMouthTalk`), so suppress that while a bugle is held.
- Comfort: none.
- Multiplayer: pitch goes through the vanilla RPC argument.

### G6. Light fuses and fires with a flame (M)

- Game now, all hold-interact:
  - Campfire light (`A/Campfire.cs:309`, `Light_Rpc`).
  - Ghost fire (`A/GhostFire.cs:82`, `RPC_SetLit`).
  - Scout cannon fuse (`A/ScoutCannonFuse.cs:48`, 1 s).
  - Rocket fuse (`A/RocketFuse.cs:50`).
  - Dynamite fuse starts on primary (`A/Dynamite.cs:135-139`, `SetFlareLitRPC`).
- PeakXR now: strike-to-light for flares and candles (`src/PeakXR/Interact/Lights/HandLights.cs`, `StrikeToLight`). Fuses and fires use the generic grip-hold.
- VR treatment: a lit held flame (torch, lit flare, candle, lantern) touched to the target (15 cm) holds the interact on it, with the flame-crackle haptics HandLights uses. Dynamite: touch its fuse to any flame, including a campfire, to send primary. Bare-hand grip-hold stays, so nobody gets stuck without a flame.
- Comfort: none.
- Multiplayer: vanilla RPCs.

### G7. Grab a piton handle to hang (S)

- Game now: interact on a `ClimbHandle` hangs (`A/ClimbHandle.cs:54-64`, `RPCA_Hang`); interact or jump lets go. It is the only vanilla place that moves the hand rigs to a grip.
- PeakXR now: the hammer places pitons (`src/PeakXR/Interact/Tools/SpikeHammer.cs`). Hanging goes through the generic grip-interact, and releasing the grip does nothing.
- VR treatment: grip within 10 cm of the handle sends interact (hang), and releasing that grip sends interact again (let go). The wall-climb `ProbeHold` priority must prefer the handle over the rock behind it (`src/PeakXR/Interact/PhysicalInteraction.cs:504`).
- Comfort: none.
- Multiplayer: vanilla RPCs.

### G8. Bandages and status items pressed to the body (S)

- Game now: healing and status items run through the hold-primary cast (`Action_ModifyStatus`, `Action_ClearAllStatus`, `Action_GiveExtraStamina`, ...; `CharacterItems.DoUsing` :197-225). Use on a friend is a secondary hold on the camera-picked target (`Item.canUseOnFriend`, `A/Item.cs:831`, secondary start at :979-981).
- PeakXR now: only items with `Action_Consume` or `Action_RestoreHunger` count as edible for eat-at-mouth (`src/PeakXR/Avatar/ItemHoldInfo.cs:61`). Every other status item is trigger-to-use. Feeding a friend is item-at-their-mouth (`src/PeakXR/Social/SocialHands.cs`, `FeedAtMouth`).
- VR treatment: non-edible, self-use status items (has `Action_ModifyStatus` or `Action_ClearAllStatus` and no `Action_Consume`) cast while held against your own torso or opposite forearm (within 15 cm), with a short wrap haptic. For friends, extend the feed target from the mouth to the torso for these items. Edible ones stay at the mouth.
- Comfort: none.
- Multiplayer: vanilla cast and feed RPCs.

### G9. Arm-swing sprint (M)

- Game now: sprint is a held or toggled button (`isSprinting`, state synced).
- PeakXR now: a move-stick click (`docs/controls.md`).
- VR treatment: an optional setting, off by default. While the move stick is pushed forward, alternating arm swings over 1.2 m/s average hand speed hold sprint. It never moves you without the stick, so it adds no motion.
- Comfort: neutral, since speed is still stick-driven. Turn it off while climbing or holding a two-hand item.
- Multiplayer: vanilla.

### G10. Warm your hands at the campfire (S)

- Game now: warming is passive in range (`A/Campfire.cs:158, :225`; body heat in `CharacterHeatEmission`). The morale boost is already felt on the hands (`src/PeakXR/UI/Status/StatusPatches.cs:31`).
- VR treatment: cosmetic. Open palms held toward a lit fire (within 1 m) give a slow warm haptic, and cold status (ST-09) gives a faint shiver on both controllers. It does not change the game result.
- Multiplayer: none.

### G11. High five and fist bump between VR players (M)

- Game now: there is no two-player emote (`CharacterAnimations.PlayEmote` :410 plays single emotes).
- PeakXR now: `net.pose` streams hands between modded peers (`src/PeakXR/Net/NetPoseSync.cs`). `interact.social` handles proximity actions with vanilla meaning.
- VR treatment: when your open hand meets a modded peer's tracked hand above 1 m/s, both sides play a clap sound locally and pulse that hand. A closed hand gives a fist bump. Each client detects the contact from the stream, so nothing new goes on the wire.
- Comfort: none.
- Multiplayer: no effect for vanilla peers. They see the hands only if the player is modded. No RPC.

### G12. Parasol opened by pushing the runner (S)

- Game now: primary toggles it (`A/Parasol.cs:23`, RPC `ToggleOpenRPC` :56).
- PeakXR now: the trigger toggles it.
- VR treatment: the free hand grips the shaft and slides up past 15 cm to open, or down to close. This sends the same toggle.
- Comfort: none.
- Multiplayer: vanilla RPC.

### Gameplay: no gap found, or already covered

- Wall climb, lunge, mantle and climb-jump: `src/PeakXR/Interact/PhysicalInteraction.cs`.
- Eat at the mouth: covers drinks too, since they use `Action_Consume`.
- Throw by hand velocity: `src/PeakXR/Interact/InteractPatches.cs:84`.
- Social:
  - Carry and drop.
  - Help-up reach.
  - Feeding.
  - Thorns and arrows.
  - Web shake.
  - Cannibal hold.
  - Open a pack from behind.
  - Give-up guard.

  All in `src/PeakXR/Social/SocialHands.cs`.
- Backpack over the shoulder and the wheel by hand: `src/PeakXR/Inventory/BodyInventory.cs`.
- Ping by finger gun: `src/PeakXR/Interact/Ping/`.
- Aimed tools and launchers: `src/PeakXR/Interact/Aim/AimPatches.cs`.
- Rope payout and cannon angle by twist: `src/PeakXR/Interact/Tools/ToolInput.cs`.
- Piton hammer: `src/PeakXR/Interact/Tools/SpikeHammer.cs`.
- Glider: `src/PeakXR/Interact/Tools/GliderHands.cs`.
- Hand lights: `src/PeakXR/Interact/Lights/HandLights.cs`.
- Camera-takeover events (rocket, tornado, cannon, rescue hook, frog tongue): `comfort.events`.
- Mechanics that do not exist in this build: vehicles, mounts, ziplines, ladders as a class (they are `ClimbModifierSurface` walls, `A/GUIManager.cs:706`), chains as a mechanic, doors and levers (only `TriggerOnInteract`), sitting at a campfire, ghost interactions (`PlayerGhost` is camera only).

### Considered and not recommended

- Pal jump as a hand-cup boost (`StandOnPlayer`, `A/CharacterMovement.cs:992-1034`): the boost is decided by the standing player's own jump. A cupped-hand version needs both players modded and new data on the wire.
- Physical jump (arms or head): false triggers while climbing and when ducking; the button is fine.
- Spinning a snowball to grow it (IT-43): growth is driven by rolling on the ground in prefab code that is not in the decompile. Check in game first.
- Drinking by tilting: drinks are already eaten at the mouth. A tilt check adds failure modes and no gameplay.
- Mandrake, BingBong squeeze, basketball dribble: low value. The trigger works.

## Open questions for in-game checks

- G1: rope `climbSpeed` and `GetMove()` values come from prefabs; tune the gain in game, as was done for walls (test-report: a 0.4 m pull lifts 0.39 m).
- G3: the campfire's world bounds and fire height come from prefabs; measure them before fixing the cylinder.
- G4: `timeToOpen` per luggage type is prefab data.
- U1: check that `currentHovered.Center()` is a sensible label spot for characters, ropes and the kiosk.
