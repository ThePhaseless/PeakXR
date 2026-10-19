# PEAK internals for a VR mod: player, camera, input, items, animation, networking

Game: PEAK 2.6.b, Unity 6000.3.15f1, Mono, URP, Photon PUN 2. Source: decompiled code under `decomp/` (read-only).
All paths below are relative to `decomp/Assembly-CSharp/` unless they start with `Zorro.`. Format: `File.cs:line`.
Not in the decomp (scenes/prefabs): the camera hierarchy, PhotonView observed-component lists, Animator controllers, Canvas render modes. Items marked UNVERIFIED depend on those.

---------------------------------------------------------------------
## 0. Architecture in one paragraph

The player is a **physics ragdoll that chases an animation pose**, not a CharacterController. Each `Character` has ~20 `Bodypart` rigidbodies on joints. Every `FixedUpdate` the Animator (manual PlayableGraph update) and the Animation-Rigging IK are evaluated, the animated pose is saved per part (`Bodypart.SaveAnimationData`), the bones are reset to their rigidbody positions, and `Bodypart.Animate` applies forces/torques toward the saved pose. Walking, jumping and climbing are forces on all parts (`CharacterMovement`, `CharacterClimbing`). The camera is a separate scene object (`MainCamera` + `MainCameraMovement`) that follows the head bodypart and takes its rotation from `data.lookValues`. Look direction is a 2-float (yaw, pitch), synced to the network; there is no roll anywhere.

Consequence for VR: head orientation maps to `data.lookValues` (yaw, pitch only); hands can be driven by overriding the two IK hand targets; everything physical (position, held items) follows the ragdoll and goes through normal game paths.

---------------------------------------------------------------------
## 1. Local player

### 1.1 Finding it
- `Character.localCharacter` static, `Character.cs:118`. Set in `Character.Awake` (`Character.cs:329`) when `view.IsMine && !isBot`. Null before spawn and in menus.
- `Character.localCharacterExists` (`Character.cs:206`), `Character.IsLocal` (`:241`), `Character.observedCharacter` (`:210`, spectate target or local).
- `Character.AllCharacters` (`:128`), `Character.AllBotCharacters`, `PlayerHandler.GetAllPlayerCharacters()` (`PlayerHandler.cs:68`).
- `Player.localPlayer` (`Player.cs:27`). `Player` is a separate networked object ("Player" prefab, `CharacterSpawner.cs:269`) that owns the inventory. `Character.player` => `PlayerHandler.GetPlayer(view.Owner)`.
- Character prefab is spawned with `PhotonNetwork.Instantiate("Character", ...)` (`CharacterSpawner.cs:243`). Hook `Character.Awake`/`Start` postfix or poll `Character.localCharacter` to attach VR once.
- Bots/zombies/scoutmaster reuse `Character` (`isBot`, `isZombie`, `isScoutmaster`, `Character.cs:112-116`); `IsPlayerControlled` (`:187`) filters them.

### 1.2 Component layout (all on the Character root GameObject unless noted)
`Character` (`Character.cs`) holds `input` (CharacterInput), `data` (CharacterData), `refs` (CharacterRefs, `Character.cs:19-110`, filled by `InitializeRefs` `:366`):

| ref | class | role |
|---|---|---|
| `refs.movement` | CharacterMovement | input sampling, look accumulation, walking forces, jump, crouch/sprint state |
| `refs.climbing` | CharacterClimbing | wall climbing (stamina "stick" model) and piton handles |
| `refs.ropeHandling` / `refs.vineClimbing` | CharacterRopeHandling / CharacterVineClimbing | rope, vine |
| `refs.ragdoll` | CharacterRagdoll | owns `partList` (Bodypart), drives the per-FixedUpdate animation+IK evaluation |
| `refs.animations`, `refs.animator`, `refs.rigCreator` | CharacterAnimations, Animator, RigCreator | animator params, IK weights/targets. Animator and RigCreator are on a child (`GetComponentInChildren`). |
| `refs.ikRigBuilder`, `refs.ikRig`, `refs.ikLeft`, `refs.ikRight`, `refs.IKHandTargetLeft`, `refs.IKHandTargetRight` | Animation Rigging (RigBuilder, Rig, 2x TwoBoneIKConstraint, 2x Transform) | arm IK. Targets found at `ikRig/IK_Arm_Left/Target` and `IK_Arm_Right/Target` (`Character.cs:395-404`). |
| `refs.items` | CharacterItems | equip, hold, use, drop |
| `refs.interactible` | CharacterInteractible | other players can interact with this character (carry/eat/feed) |
| `refs.grabbing` | CharacterGrabbing | reach / help-up / kick |
| `refs.carriying` | CharacterCarrying | carrying a passed-out player |
| `refs.afflictions`, `refs.stats` | CharacterAfflictions, CharacterStats | status effects (injury, hunger, cold, poison...), stamina weight |
| `refs.backpackHandler` | CharacterBackpackHandler | backpack/jetpack/rocketpack visuals on the back |
| `refs.customization`, `refs.hideTheBody` | CharacterCustomization, HideTheBody | cosmetics; first-person body hiding |
| `refs.balloons` | CharacterBalloons | parachute/balloons |
| `refs.voice` | CharacterVoiceHandler | proximity voice |
| `refs.view` | PhotonView | owner of all RPCs below |
| `refs.head`, `refs.hip` (set in `Start` `:497`) | Bodypart | `GetBodypart(BodypartType)` `:1301`, `GetBodypartRig` `:1306` |
| `refs.carryPosRef` | Transform | where a carried player is held |

Helper transforms created at runtime in `Character.CreateHelperObjects` (`Character.cs:479`), children of `refs.helperObjects`: `animationHeadTransform`, `animationHipTransform`, `animationItemTransform`, `animationLookTransform`, `animationPositionTransform`. They hold the **animated** (not physical) head/hip pose and the item pose; updated in `CharacterRagdoll.SaveAdditionalTransformPositions` (`CharacterRagdoll.cs:207`).

Other components on the same prefab: `CharacterSyncer` (network pose, section 7), `CharacterData` (MonoBehaviourPunCallbacks, all state), `PlayerEyeLook`, `CharacterVoiceHandler`, `BadgeUnlocker`, `PointPinger` (ping), `CharacterHeatEmission`.

Useful positions: `Character.Center` = torso position (`:273`), `Character.Head` = head position (`:275`), `HipPos()` `:1389`, `TorsoPos()` `:1394`.

### 1.3 CharacterData (`CharacterData.cs`) - fields a VR mod will touch
- Look: `Vector2 lookValues` (`:77`), derived `lookDirection`, `lookDirection_Flat`, `lookDirection_Right`, `lookDirection_Up` (`:79-85`).
- Movement: `isGrounded`, `sinceGrounded`, `isSprinting`, `isCrouching` (`:157`), `worldMovementInput*`, `avarageVelocity`, `groundNormal`.
- Climb: `isClimbing`, `isRopeClimbing`, `isVineClimbing`, `climbPos`, `climbNormal`, `currentClimbHandle`; `isClimbingAnything` (`:405`).
- Consciousness: `passedOut`, `fullyPassedOut`, `dead` (`:335`), `fullyConscious` (`:393`), `fallSeconds`, `currentRagdollControll` (0 = floppy ragdoll, 1 = fully animated; `GetTargetRagdollControll` `:485` returns 0 when fallSeconds>0, passedOut, fullyPassedOut, dead; 1 when carried).
- Items: `currentItem` (`:417`), `currentStickyItem`.
- Reach/grab: `isReaching`, `grabJoint`, `grabbedPlayer`, `sincePressReach`, `sincePressClimb`.
- Carry: `carrier`, `carriedPlayer`, `isCarried`.
- IK override: `overrideIKForSeconds` (`:~210`, while >0 `HandleIK` does not apply item IK).
- Stamina: `currentStamina`, `extraStamina`, `TotalStamina`.

---------------------------------------------------------------------
## 2. Camera and look direction

### 2.1 Camera classes
- `MainCamera` (`MainCamera.cs`): holds `static MainCamera instance` (`:8`), `Camera cam` (internal), `CameraOverride camOverride`. `SetCameraOverride(CameraOverride)` `:27`. It is a scene object with the `Camera`, `AudioListener` (`CharacterVoiceHandler.cs:143` fetches it) and `MainCameraMovement` on the same GameObject. Hierarchy/parenting of that GameObject: UNVERIFIED (scene), but `MainCameraMovement` writes its **world** `transform.position/rotation` every LateUpdate, so treat it as a root-level free camera. Create a VR rig parent only if you re-parent it yourself.
- `MainCameraMovement : Singleton<MainCameraMovement>` (`MainCameraMovement.cs`), `[DefaultExecutionOrder(500)]`. Everything is in `LateUpdate` (`:61`):
  1. god cam (debug `isGodCam`) -> `godcam.Update`.
  2. `cam.camOverride != null` -> `OverrideCam()` (`:309`): `fieldOfView = override.fov`, `SetPositionAndRotation(override.transform)`. Used by binoculars (`Action_ShowBinocularOverlay.cs:29`), `Action_OverrideCamera.cs:7`, `CameraOverride.DoOverride`.
  3. local `data.fullyPassedOut` -> `Spectate(useGhost: true)` (`:135`): camera at `specCharacter.GetSpectatePosition()` behind by `spectateZoom`, rotation from **local** `lookDirection`.
  4. `isRocketing` -> spectate (rocket launch, `StartRocketing` from `CharacterMovement.cs:537`).
  5. else `CharacterCam()` (`:272`).
- `CharacterCam()` (`:272-307`) is the first-person camera:
  - `cam.fieldOfView = GetFov()` (`:328`; FovSetting, min 70 fallback, `ExtraFovSetting` added while climbing, lerped).
  - rotation = `LookRotation(localCharacter.data.lookDirection)`, blended toward the head rigidbody rotation by `ragdollCam = 1 - currentRagdollControll` (so the camera rolls with the head when ragdolled/fallen), then `Rotate(GamefeelHandler.instance.GetRotation(), Space.World)` (shake, springs, perlin).
  - position target = `Character.GetCameraPos(GetHeadOffset())` = `head.TransformPoint(up*1 + forward*offset)` with `offset` lerped to **-0.5** (`MainCameraMovement.cs:317`, `Character.cs:1358`), blended with torso position by `ragdollCam`; clamped to within `characterPovMaxDistance` (0.1) and lerped with `characterPovLerpRate` (5)/s.
- Near clip, culling mask: nothing in code sets `nearClipPlane`/`cullingMask` for the main camera; set in the scene (UNVERIFIED value). Mirrors copy `src.nearClipPlane/fieldOfView` (`MirrorReflection.cs:107-110`; `MirageManager.cs:17` copies FOV from `MainCamera.instance.cam`).
- Shake: `GamefeelHandler.instance` (`GamefeelHandler.cs`): `AddPerlinShake`, `AddRotationShake_Local_Stiff/Loose`, summed by `GetRotation()` (child local euler angles). Called from climbing (`CharacterClimbing.cs`), falls (`Character.RPCA_Fall` `:1752`), etc. In VR this must be removed from the camera (comfort): postfix the camera to ignore it, or no-op `AddPerlinShake`/`AddRotationShake_*`. `PhotosensitiveSetting` already halves it.
- First-person body hiding: `HideTheBody` (`HideTheBody.cs`). `Update` (`:39`): shows the body on non-local characters or when local is dead/fully passed out; on the local character `Toggle(false)` (`:57`) sets material float `_VertexGhost = 1` on `body`, `headRend`, `sash`, `medal`, `costumes`, face renderers, hats, and enables `shadowCaster`/`shadowCasterHat` (shadow-only copies). To show the VR player's own arms/hands: postfix `HideTheBody.Toggle` (or patch `Update`) and set `_VertexGhost` back to 0 on `body` (and optionally keep head hidden). The body mesh is `refs.mainRenderer` / `HideTheBody.body` (SkinnedMeshRenderer).

### 2.2 Look rotation storage and application
- Stored as `CharacterData.lookValues` (`Vector2`): `x` = yaw in degrees (unbounded, **not wrapped**), `y` = pitch in degrees, positive = looking **up**, clamped to **[-85, 85]** in `CharacterMovement.CameraLook` (`CharacterMovement.cs:283-290`).
- Conversion: `HelperFunctions.LookToEuler(look) = (-look.y, look.x, 0)` (`HelperFunctions.cs:183`); `LookToDirection(look, Vector3.forward) = Quaternion.Euler(LookToEuler(look)) * forward`; inverse `DirectionToLook(dir)` (`:203`), `EulerToLook(euler) = (euler.y, -euler.x)`.
  - From an HMD world rotation `q`: `e = q.eulerAngles` (normalize e.x to [-180,180]); `lookValues.x = e.y` (wrap to [-180,180]), `lookValues.y = clamp(-e.x, -85, 85)`.
- Applied in `Character.RecalculateLookDirections()` (`Character.cs:1347`): fills `data.lookDirection`, `lookDirection_Flat`, `lookDirection_Right`, `lookDirection_Up`.
- Written by: `CharacterMovement.CameraLook` (local mouse/stick, runs when `CanMoveCamera()` i.e. not `data.usingWheel`), `CharacterSyncer.Update` (remote players, lerped), and scripted forced looks (`FrogTongue.cs:430-453`, `RescueHook.cs:350-353`, `Scoutmaster.cs`, `MushroomZombie.cs:762`, `BotToCharacterTranslator.cs:19`). Those scripted writes still need to be respected (e.g. frog tongue drags your look); in VR, either skip HMD override while such effects run or accept the conflict (it will fight).
- **Who reads the look direction** (this is what the HMD must feed):
  - `CharacterMovement.Update` -> `Character.CalculateWorldMovementDir` (`Character.cs:1311`): stick input is relative to `lookDirection`/`lookDirection_Right`. For controller- or body-relative locomotion, postfix this method and recompute `data.worldMovementInput*`, or just feed different `lookValues`.
  - `Character.SetRotation` (`Character.cs:1529`, called from `CharacterRagdoll.RotateCharacter`): the animated rig root is rotated to `LookRotation(lookDirection_Flat)`, so **body yaw == head yaw**; while climbing it faces `-climbNormal`, on rope `-ropeClimbWorldNormal`, when carried the carrier's `carryPosRef`.
  - `CharacterAnimations.Update` (`:160`): animator `Look Y` from `lookDirection` vs head frame; `lookRef`. Used for head pitch animation.
  - `CharacterRagdoll.SaveAdditionalTransformPositions` (`:207`): `animationLookTransform` rotation = `Euler(-lookY * 0.5, lookX, 0)` (**pitch halved**), `animationItemTransform` = `animationLookTransform.TransformPoint(item.defaultPos)` with forward from `lookDirection * defaultForward`. This sets where held items and hands go (section 6).
  - `CharacterItems.GetItemHoldForward/Up` (`CharacterItems.cs:701-704`): `lookDirection`, `lookDirection_Up` for the item rigidbody hold torque. `DropItemRpc` (`:385`) throw direction from `lookValues` (`:414`).
  - `Interaction.DoInteractableRaycasts` (`Interaction.cs:196`): raycast from `MainCamera.instance.transform` (position, forward), length `Interaction.distance` = 2 m, sphere-cast radius `area` 0.5.
  - `CharacterClimbing.TryToStartWallClimb` (`:534`): origin `MainCamera.instance.transform.position`, direction `data.lookDirection`, 1.25 m.
  - Item use raycasts all use `MainCamera.instance.transform`: `RopeShooter.cs:87,133`, `VineShooter.cs:52-82`, `Action_RaycastDart.cs:43-49`, `Constructable.cs:79-118`, `Peak/Action_RaycastSpawnSomething.cs:28-37`, `CharacterItems.cs:1075` (climbing spike), `Action_LaunchPlayer.cs:7`, `Peak/LookWithCamera.cs:9`, `RescueHook.cs:268` (`cam.ScreenPointToRay(screen centre)`), `Guidebook.cs:180` (book floats in front of camera), `IsLookedAt.cs:43`, `BingBongPowers.cs:33`.
  - `PointPinger.DoPing` (`PointPinger.cs:61`): `Camera.main.ScreenPointToRay(mouse or screen centre)`.
  - Grabbing/helping: `CharacterGrabbing.Reach` (`:247`) and kick use `data.lookDirection`; `CharacterVineClimbing.cs:67` and `CharacterRopeHandling.cs:154` use `lookDirection_Flat`.
  - `Character.RPCA_...` jump variants use `lookDirection_Flat` (`CharacterMovement.cs:636`).
- Consequence: because almost every raycast uses `MainCamera.instance.transform`, giving that transform the HMD pose (or a controller pose, by patching individual call sites) makes aiming follow the HMD with zero extra work. For controller-aimed tools (rope shooter, dart, vine shooter) patch the specific call site or temporarily swap `MainCamera.instance.transform` pose around the call.

### 2.3 Recommended camera patch points
1. `MainCameraMovement.LateUpdate` **Postfix** (`MainCameraMovement.cs:61`, order 500): after `CharacterCam()`, set the camera/ rig pose. Keep position from the ragdoll head (it already follows `GetCameraPos`), add the room-scale HMD local offset, replace rotation with HMD. Also skip when `isGodCam`; for `camOverride` (binoculars/cutscenes) either hand control to a screen-quad or accept the override pose plus HMD delta.
2. Do **not** let the game set FOV on a stereo camera: prefix/neutralize `GetFov` (`:328`) or the `cam.cam.fieldOfView =` writes at `:278,:311`.
3. Feed back yaw/pitch: postfix `CharacterMovement.CameraLook` (`CharacterMovement.cs:283`): compute `lookValues` from the HMD (formula in 2.2), then call `character.RecalculateLookDirections()` (internal, use Harmony `AccessTools`). Optionally prefix to skip the stick accumulation.
4. Wrap yaw into [-180,180] each frame (half-float sync loses precision for large angles, see 7.1).

---------------------------------------------------------------------
## 3. Input

### 3.1 Mechanism
Unity **Input System** project-wide actions: `InputSystem.actions.FindAction("<name>")` in `CharacterInput.Init` (`CharacterInput.cs:141`, called from `Character.Awake`). `CharacterInput` (on the Character, `Character.input`) caches the `InputAction`s in statics and **samples them into public fields once per frame** in `CharacterInput.Sample(bool playerMovementActive)` (`CharacterInput.cs:212`), called from `CharacterMovement.Update` (`CharacterMovement.cs:198`) for the local character only: `character.input.Sample(character.CanDoInput())`. `CanDoInput` (`Character.cs:1714`) is false when `GUIManager.windowBlockingInput` or `wheelActive`. If `MainCameraMovement.isGodCam`, `ResetInput()` is called instead. `Rebinding.LoadRebindingsFromFile()` also runs in `Init`.
`Sample` first calls `ResetInput()` (`:297`), then fills fields. A few fields (interact, emote, pause, slot select) are sampled even when `playerMovementActive` is false.
Input scheme detection: `Zorro.ControllerSupport.InputHandler.GetCurrentUsedInputScheme()` (`InputHandler.cs`, `InputScheme { KeyboardMouse, Gamepad, Unknown }`) switches movement read (`GetMovementInput` `:267`: gamepad reads `Move` Vector2, keyboard reads 4 button actions) and look sensitivity (`CharacterMovement.CameraLook`). `InputHandler.Default` action map is enabled/disabled each frame (`InputHandler.cs:Update`).

### 3.2 All fields (CharacterInput) and their source action
| field | type | action(s) | consumers |
|---|---|---|---|
| `movementInput` | Vector2 | `Move` (gamepad) or `MoveForward/Backward/Left/Right` | `CalculateWorldMovementDir`, sprint/crouch gating, climbing, rope, animator `Input X/Y`; **also synced to remote as 4 flags** |
| `lookInput` | Vector2 | `Look` | `CharacterMovement.CameraLook`; animator `Look X` |
| `scrollInput` | float | `Scroll` | `Item.Scroll`, spectate zoom |
| `scrollForward/BackwardWasPressed/IsPressed` | bool | `ScrollForward/ScrollBackward` | `Item.ScrollButton*`, spectate zoom |
| `sprintIsPressed/WasPressed`, `sprintToggleIsPressed/WasPressed` | bool | `Sprint`, `SprintToggle` | `CharacterMovement.SetMovementState` (`:231`), climb jump |
| `crouchIsPressed/WasPressed`, `crouchToggleWasPressed` | bool | `Crouch`, `CrouchToggle` | `SetMovementState` |
| `jumpWasPressed/IsPressed` | bool | `Jump` | `TryToJump` (`:415`), jetpack, stop climb/rope/vine |
| `interactWasPressed/IsPressed/WasReleased` | bool | `Interact` | `Interaction.DoInteraction` (`:99`), unhang pitons, hold-to-revive timer (`Character.cs:~1217`) |
| `dropWasPressed/IsPressed/WasReleased` | bool | `Drop` | `CharacterItems.DoDropping` (`:252`): hold = throw charge, release = drop/throw |
| `usePrimaryWasPressed/IsPressed/WasReleased` | bool | `UsePrimary` | `CharacterItems.DoUsing` (`:197`) -> `Item.StartUsePrimary/Continue/Cancel`; **also wall climb** when empty-handed (`Character.cs:1201` sets `sincePressClimb`), stop climbing on release (`CharacterClimbing.cs:~150`) |
| `useSecondaryWasPressed/IsPressed/WasReleased` | bool | `UseSecondary` | `DoUsing` secondary; **reach/help** when empty-handed (`Character.cs:1203-1220` sets `sincePressReach`; `CharacterGrabbing.Update` `:83` -> `RPCA_StartReaching`), feed friend |
| `pingWasPressed` | bool | `Ping` | `PointPinger` |
| `selectSlotForward/BackwardWasPressed`, `unselectSlotWasPressed` | bool | `SelectSlotForward/Backward`, `UnselectSlot` | `CharacterItems.DoSwitching` (`:729`) |
| `HotbarKeyWasPressed(i)/IsPressed(i)` | method over `hotbarActions[0..8]` | `Hotbar1..9` | `DoSwitching`, spectate switching (`spectateLeft/RightWasPressed`) |
| `selectBackpackWasPressed` | bool | `SelectBackpack` | backpack equip |
| `emoteIsPressed` | bool | `Emote` | `GUIManager.cs:563` opens the radial emote wheel while held |
| `pauseWasPressed` | bool | `Pause` | `GUIManager.UpdatePaused` (`GUIManager.cs:~420`) |
| `pushToTalkPressed` | bool | `PushToTalk` | voice |
| `spectateLeft/RightWasPressed` | bool | derived | `MainCameraMovement.HandleSpecSelection` |
| `itemSwitchBlocked` | bool | set by code | blocks hotbar/slot actions |

### 3.3 Patch points for injecting VR controller values
- **Best single point**: Harmony **Postfix** on `CharacterInput.Sample(bool)` (`CharacterInput.cs:212`). All fields are public; overwrite them after the real sampling (OR them with the VR state; respect `playerMovementActive` and `character.CanDoInput()` the way `Sample` does, otherwise the VR trigger acts through open menus/wheels). Because `Sample` runs from `CharacterMovement.Update` before `CameraLook`/`TryToJump`/`SetMovementState` in the same frame, injected values take effect that frame. Note `ResetInput` zeroes everything every frame, so you must set `...WasPressed`/`...WasReleased` edges yourself exactly one frame.
- Alternative: a virtual Input System device (`InputSystem.AddDevice<Gamepad>()` + `InputSystem.QueueStateEvent`) feeds `Move`/`Look`/buttons through the real actions and keeps rebinding/UI navigation working, but scheme switches to `Gamepad` (`InputHandler.Update` reads `PlayerInput.currentControlScheme`) which changes sensitivity paths and glyphs. Use only if you want UI navigation by pad.
- Look: skip `lookInput` entirely (set to zero) and override `lookValues` in a `CameraLook` postfix (section 2.3). Snap/smooth turn: add a yaw offset to the tracking-space origin, not to `lookValues` directly.
- Hand-based actions need physical gestures instead of buttons (section 6.4): produce `interactWasPressed`, `usePrimary*`, `dropWasPressed/Released` from grab/trigger and hand-proximity logic in the same postfix.
- Wheels (emote, backpack) are screen-space UI driven by mouse/stick (`EmoteWheel.cs:34,120`, `BackpackWheel.cs:89,253` `TestSelectSliceGamepad(Vector2)`); `GUIManager.hudCanvas` (`GUIManager.cs:22`) Canvas render mode is UNVERIFIED (scene). VR UI needs a world-space/quad redirect (e.g. `CameraQuad`, or re-parenting canvases) and a way to feed the wheel stick vector.
- Cursor: `CursorHandler.cs:15-21` locks/unlocks the cursor. Menus/pause use `EventSystem`, so VR pointer needs an `InputSystemUIInputModule`-compatible pointer or a laser that sets the mouse position.

---------------------------------------------------------------------
## 4. Movement and climbing

### 4.1 Walking, sprint, crouch, jump (`CharacterMovement.cs`)
- `Update` (`:183`): sample input (local only) -> `CameraLook` if `CanMoveCamera` -> `TryToJump` on `jumpWasPressed` (or jetpack if held and `hasJetpack`) -> `SetMovementState` -> `Character.CalculateWorldMovementDir`.
- `SetMovementState` (`:231`, owner only): crouch via `RPC RPCA_SetCrouch(bool)` (`:226`, All); `data.isSprinting = movementInput.y > 0.01 && (sprintIsPressed || toggle) && CheckSprint() && !OutOfRegularStamina() && !isStruggling`, drains stamina `sprintStaminaUsage`.
- `FixedUpdate` (`:292`): ground checks (`RaycastGroundCheck` `:898`, `EvaluateGroundChecks` `:727`), `Stand()` (`:716`) applies spring-up forces, gravity, then per-part `Animate`, gravity, `AddMovementForce(movementForce * ragdollControl)` along `data.worldMovementInput_Grounded`, drag. Movement is **force-based on all bodyparts**; speed is capped via drag, not set directly. `GetMovementForce` (`:397`) halves when crouching, multiplies by `sprintMultiplier`.
- Jump: `TryToJump` (`:415`) -> `RPC JumpRpc(bool isPalJump)` (`:626`, All) -> `IDoJump` adds `jumpImpulse` to every part after 0.1 s; uses stamina; `jumpsRemaining`/`extraJumps`.
- Physical room-scale movement: the character does not follow the HMD. If the HMD moves, either (a) only translate the camera rig in the character's local frame and accept camera/body offset, or (b) push the ragdoll: `character.refs.ragdoll.MoveAllRigsInDirection(delta)` (`CharacterRagdoll.cs:~270`, teleports all rigs, used for warps) or `Character.AddForce`/`MoveBodypartTowardsPoint`. Teleporting rigs bypasses collision; do small deltas with a wall check. Hip position is what remote players see (7.1).
- Falling/ragdoll: `Character.Fall(seconds, shake)` (`Character.cs:1735`, `RPC RPCA_Fall` All `:1752`) sets `fallSeconds` -> `currentRagdollControll` goes to 0 (limp). Fall damage in `CharacterMovement.Land`/`CheckFallDamage` (`:817-858`).
- Passing out: `HandleLife` (`Character.cs:1039`) ramps `passOutValue` when `afflictions.shouldPassOut`, then `RPC RPCA_PassOut` (`:993`); `HandlePassedOut` (`:571`) runs the revive/death timers (`deathTimer`, `RPCA_UnPassOut`, `RPCA_Die` `:736`, zombify `RPCA_Zombify`). Dead/fully-passed-out: `MainCameraMovement` switches to spectate/ghost (`PlayerGhost` object via `PhotonNetwork.Instantiate("PlayerGhost")`, `MainCameraMovement.cs:143`). A VR camera must handle this branch: body hidden state flips (`HideTheBody.Update`), camera is third-person behind the spectated character.
- Carried: `CharacterCarrying` (`CharacterCarrying.cs`). Carrier interacts with a fully-passed-out player (`CharacterInteractible.Interact`) -> `StartCarry` (`:49`, `RPC RPCA_StartCarry`). Carried character's rig is pulled to `carrier.refs.carryPosRef` (`GetCarried` `:41`), `data.carrier` set, ragdoll control forced to 1, `SetRotation` follows the carrier. Drop with slot 3 key or interacting again (`RPCA_Drop`).
- Pal jump/stand on player: `CheckForPalJump`, `StandOnPlayer` (`CharacterMovement.cs:992-1034`).

### 4.2 Wall climbing (`CharacterClimbing.cs`)
- It is a **stick-to-surface climb with no discrete hand holds and no hand IK**. Start: hold `usePrimary` with empty hands (`sincePressClimb < 0.1`), or `jump` in the air if JumpToClimbSetting. `TryToStartWallClimb` (`:534`): line cast (+ 0.05/0.1 thickened) from `MainCamera.instance.transform.position` along `data.lookDirection` for 1.25 m on layer TerrainMap; `AcceptableGrabAngle` (surface angle between ~50 and 170 degrees from up); then `EquipSlot(None)` and `RPC StartClimbRpc(Vector3 climbPos, Vector3 climbNormal)` (`:349`, All). Stored in `data.climbPos`, `data.climbNormal`.
- While climbing (`Climbing()` called from FixedUpdate): re-samples the wall from `RaycastPos()` (`:597`, `climbPos + normal*0.4`) toward `GetRequestedPostition()` (moves `climbPos` by stick input along the wall plane scaled by stamina, `climbSpeed`); applies force pulling torso to `climbPos + normal*0.4` (`GetClimbDirection`, `climbForce`). Stamina drains each frame (`maxStaminaUsage`..`minStaminaUsage`, by wall angle `GetAngleUsage`); out of stamina -> slide down (`playerSlide`). Release `usePrimary` (unless toggled on gamepad) or `jump` -> `RPC StopClimbingRpc(float fallSpeed)` (`:627`, All). Sprint while climbing = `RPCA_ClimbJump` (`:238`) lunge.
- Animation: animator bool `Climb Surface` (`CharacterAnimations.cs`, `AN_CLIMB_SURFACE`) plays a canned climbing animation; hands/feet are NOT placed on the wall by IK. `Character.SetRotation` makes the rig face `-climbNormal`. `isClimbing` and `climbPos` (half precision, only when climbing) are synced (`CharacterSyncData`).
- VR hand-on-wall: the game never computes per-hand targets, so there is nothing to feed. A faithful VR climb needs a mod-side layer: e.g. trigger `StartClimbRpc`-equivalent through input (`usePrimaryIsPressed`) when a controller hand is within reach of a TerrainMap collider, and move `climbPos` from controller delta (patch `GetRequestedPostition` postfix or inject `movementInput`), and override hand IK targets (section 6) to the wall contact point.
- Pitons/handles: `ClimbHandle` (interactable, `RPCA_Hang`/`RPCA_UnHang`), `HandleClimbHandle` (`:209`) moves `Hand_L/Hand_R` rigs toward the handle with `MoveBodypartTowardsPoint` (the only place hands are physically positioned for climbing). `interactWasPressed` or `jump` releases.
- Climbing spikes (item): `CharacterItems.UpdateClimbingSpikeUse` (`:981`) raycasts from `MainCamera` (`:1075`), hold primary (grounded) or secondary (climbing) for `usingTimePrimary`, `HammerClimbingSpike`.

### 4.3 Rope and vine
- `RopeSegment : IInteractible` (`RopeSegment.cs:6`); `Interact` (`:39`) -> `RPC GrabRopeRpc(PhotonView rope, int segmentIndex)` (`CharacterRopeHandling.cs:144`). Climb speed from `movementInput.y`; `ropePercent` synced (`CharacterSyncData`); jump leaves (`StopRopeClimbingRpc(bool jumped)` `:84`). Rig faces `-ropeClimbWorldNormal`.
- Vine: `JungleVine : IInteractible`; `RPC GrabVineRpc(PhotonView, int)` (`CharacterVineClimbing.cs:155`); climb direction uses `lookDirection_Flat` sign (`:67`); animator `Vine Hang/Slide/Type`; `RPCA_SyncVineClimb(float,float)` 4x/s. No hand IK here either.
- Ladders: no separate ladder class found; ladders are normal climb surfaces or ropes.

### 4.4 Reach / help friend / kick (`CharacterGrabbing.cs`)
`useSecondary` with empty hands -> `data.sincePressReach` -> `RPCA_StartReaching`/`RPCA_StopReaching` (`:152-170`, All) -> `data.isReaching`. `Reach()` (`:247`): within 4 m and 60 degrees of look dir, pulls helpable allies toward `Center + ZeroY(lookDirection * extendReachDistance(1))` (`DragTowards`). The right arm is IK-posed forward (`CharacterAnimations.ReachIK` `:325` / `ConfigureIK` `:~399`). `RPCA_GrabAttach` is scoutmaster-only (FixedJoint hand to body part). Kick mode (`isKickMode`): `RPCA_Kick`.

---------------------------------------------------------------------
## 5. Items

### 5.1 Item class (`Item.cs`, `class Item : MonoBehaviourPunCallbacks, IInteractible`)
- Statics: `Item.ALL_ITEMS`, `ALL_ACTIVE_ITEMS`; `Item.TryGetItemFromCollider(Collider, out Item)` (`:477`).
- Identity/data: `ushort itemID`, `ItemUIData UIData` (`canDrop`, `canPocket`, `hasMainInteract`, `hasSecondInteract`, `itemName`, prompts), `ItemInstanceData data` (per-instance data: uses `DataEntryKey.ItemUses`, `CookedAmount`, `Fuel`, `UseRemainingPercentage`...), `itemTags`.
- Physics: `Rigidbody rig`, `Collider[] colliders`, `Item.Move(pos, rot)` (`:707`, teleports rig).
- Hold pose: `defaultPos` (`:109`, offset in `animationLookTransform` space, animated in from z=0.7 over ~0.2 s at Awake `:~420`), `defaultForward` (`:111`), `rightHandOnly` (`:115`), `gliderHold`. Grip points are **child transforms named exactly `Hand_R` and `Hand_L`** of the item (`CharacterItems.GetItemPosRight/Left`, `CharacterItems.cs:879-915`, `item.transform.Find("Hand_R")`). These are the natural grab-attachment targets for VR hand alignment.
- State: `ItemState { Ground, Held, InBackpack }` (`ItemState.cs`); `SetState(ItemState, Character)` (`Item.cs:734`): Ground = gravity + colliders; Held = no gravity, `excludeLayer` Default, `holderCharacter` set, master transfers **PhotonView ownership to the holder** (`TransferOwnership`); InBackpack = kinematic, scale 0.5, colliders off. `holderCharacter`, `trueHolderCharacter`, `wearerCharacter` (backpack).
- Interaction: `Interact(Character)` (`:578`): if the player has a free slot (`Player.HasEmptySlot`), deactivates the object and `RPC RequestPickup(PhotonView)` to MasterClient (`:606`) -> master `Player.AddItem` (`Player.cs:87`) -> `RPC OnPickupAccepted(byte slotID)` (`CharacterItems.cs:441`) to the owner -> `EquipSlot(slot)`; the world object is destroyed. Denied -> `DenyPickupRPC`. `IsInteractible` (`:690`) false when `blockInteraction`, Held or InBackpack. So **picking up = Interact on an `IInteractible`**, i.e. the item flows into a slot and a *new* held item is instantiated from the prefab (`PhotonNetwork.Instantiate("0_Items/"+name)` in `EquipSlot` `CharacterItems.cs:~500`). There is no direct "attach world object to hand".
- Use: `CanUsePrimary` (`:797`), `CanUseSecondary` (`:819`), `StartUsePrimary` (`:849`), `ContinueUsePrimary` (progress by `usingTimePrimary`, `castProgress`), `FinishCastPrimary` (`:902`), `CancelUsePrimary`, `StartUseSecondary` (`:977`), `FinishCastSecondary` (`:1034`, includes feed-friend), `Scroll*`. Events (Actions): `OnPrimaryStarted/Held/FinishedCast/Released/Cancelled`, `OnSecondaryStarted/Held/FinishedCast/Cancelled`, `OnConsumed`, `OnScrolled`, `OnStateChange`.
- Consuming: `Item.ConsumeDelayed` (`:1079`) -> `RPC Consume(int consumerID)` (`:1096`, All): sets animator bool `Consumed Item`, empties the slot, re-equips, deactivates. Food has `usingTimePrimary` > 0 so eating is "hold primary until the cast bar fills"; `ItemUseFeedback` (`ItemUseFeedback.cs`) holds a `useAnimation` animator bool that is cleared at `FinishCastPrimary` (`Item.cs:906`). The eat animation brings the item to the face via animator only (UNVERIFIED which animator state; not driven by hand IK).
- Feeding others: `canUseOnFriend`, `mustUseOnFriend`; secondary use targets `Interaction.instance.bestCharacter` (raycast hit `CharacterInteractible`) -> `Character.FeedItem` (`Character.cs:1640`) -> `RPC GetFedItemRPC(int itemPhotonID)`; `SendFeedDataRPC`/`RemoveFeedDataRPC`.

### 5.2 ItemAction / ItemComponent
- `ItemActionBase : MonoBehaviourPun` (`ItemActionBase.cs`): `RunAction()`; `character => item.holderCharacter`.
- `ItemAction : ItemActionBase` (`ItemAction.cs`): serialized booleans choose which Item event triggers `RunAction`: `OnPressed`, `OnHeld`, `OnReleased`, `OnCastFinished`, `OnCancelled`, `OnSecondaryCastFinished`, `OnSecondaryPressed`, `OnSecondaryHeld`, `OnSecondaryCancelled`, `OnConsumed`.
- Concrete actions (52): `Action_Consume`, `Action_RestoreHunger`, `Action_ModifyStatus`, `Action_ApplyAffliction`, `Action_ReduceUses`, `Action_ConsumeAndSpawn`, `Action_Spawn`, `Action_RaycastDart`, `Action_Flare`, `Action_Parasol`, `Action_Balloon`, `Action_LightCandle/Lantern`, `Action_Torch`, `Action_OverrideCamera`, `Actions_Binoculars`/`Action_ShowBinocularOverlay`, `Action_Guidebook*`, `Action_TootMagicBugle`, `Action_Warp*`, `Action_LaunchPlayer`, `Action_PlayAnimation` (empty), `Action_PlayItemAnimation`, `Action_Die`, `Action_StrangeGem`, ..., plus `Peak/Action_*` (`Action_Antizooka`, `Action_CloneSelectedItem`, `Action_RaycastSpawnSomething`, `Action_SacrificeFriend`, `Action_SuperJumpAmulet`...).
- `ItemComponent : MonoBehaviourPunCallbacks` subclasses (item-specific logic with own networking): `Candle`, `Lantern`, `Flare`, `Beehive`, `Balloon`, `JetpackItem`, `Rocketpack`, `RopeSpool`, `RopeShooter`, `VineShooter`, `RescueHook`, `Mandrake`, `MagicBean`, `MagicBugle`, `Constructable`, `Dynamite`, `Snowball`, `StickyItemComponent`, `ClimbingSpikeComponent`, `ItemCooking`, `ItemScaleSyncer`, `AmuletBase`, `ParachuteItem`. Item subclasses: `Backpack`, `Guidebook`, `MobItem`, `Stone`.
- Items that need aiming use `MainCamera.instance.transform` (2.2), or the muzzle (`spawnTransform`, `rayOrigin`) for origin and camera forward for direction; for VR, replace the forward with the hand/muzzle forward at those call sites.

### 5.3 How an item is held (`CharacterItems.cs`)
- Slots: `Player.itemSlots` = 3 `ItemSlot`s (ids 0..2), `Player.backpackSlot` (`BackpackSlot`, id 3 = `Player.BACKPACKSLOTINDEX`), `Player.tempFullSlot` (id 250, overflow when picked up with full inventory). `ItemSlot` (`ItemSlot.cs`): `prefab`, `data`, `itemSlotID`, `IsEmpty()`. `Player.GetItemSlot(byte)` `:246`. Current: `CharacterItems.currentSelectedSlot` (`Optionable<byte>`), `lastSelectedSlot`. `MAX_SLOT = 3`.
- Equip flow: `EquipSlot(Optionable<byte>)` (`:482`, local) instantiates the networked item prefab in front of the torso then `RPC EquipSlotRpc(int slotID, int viewID)` (`:546`, All) -> `Equip(Item)` (`:582`) -> `data.currentItem = item; item.SetState(Held)`, coroutine moves the item to the hold pose and, after a few physics frames, `AttachItem` (`:641`): puts `Hand_R`/`Hand_L` **bodypart rigidbodies** at the item's `Hand_R`/`Hand_L` and adds **FixedJoints from the hands to `item.rig`**. So the held item is physically welded to the ragdoll's hands; it is positioned by the hand rigs, which follow the animated IK pose. Late joiners: `RPC_InitHoldingItem` (`:921`).
- Each FixedUpdate `HoldItem` (`:170`) also adds a force/torque pulling the item toward `GetItemHoldPos` (`:706` = hip + (`animationItemTransform` - `animationHipTransform`), terrain pushed-off) and orientation `LookRotation(lookDirection, lookDirection_Up)`. 
- The IK hand targets for the held item are set in `CharacterAnimations.ConfigureIK` (`CharacterAnimations.cs:390`): left target = `GetItemPosLeft/RotLeft`, right = `GetItemPosRight/RotRight`, both in the **animated space** relative to `animationItemTransform` (head position + look frame, offset `item.defaultPos`). That is how hands end up in front of the face along the look direction.
- Unequip/drop: `DoDropping` (`:252`): hold `drop` > `delayBeforeThrowCharge` charges `throwChargeLevel` (0..1 over `throwChargeTime`); release -> `RPC DropItemRpc(throwCharge, slot, pos, vel, rot, data, cache)` (`:385`, All): holder destroys the held item; **master** instantiates a world item, sets velocity = `item velocity + lookDirection(from lookValues) * force*0.5` where force = lerp(minThrowForce, maxThrowForce, charge) (`:~414`), calls `RPC_SetThrownData`. `DropItemFromSlotRPC`, `DropAllItems` (`:~320`), `DestroyHeldItemRpc`. `isChargingThrow` is synced in `CharacterSyncData`. Sticky items (`StickyItemComponent`) require charge to throw.
- Throwing: only the **master client** spawns the thrown world item (`DropItemRpc` runs `if (PhotonNetwork.IsMasterClient)`), with velocity = sent `velocity` argument + `lookDirection(from the thrower's replicated lookValues) * force * 0.5` (`CharacterItems.cs:~414-425`). So direction comes from the thrower's synced look values, not from the hand. A modded thrower can feed hand velocity through the `velocity` argument: the sender passes `currentItem.rig.linearVelocity` (`CharacterItems.cs:~278`), so patch `DoDropping` to send the controller velocity instead. Replacing the look-based component only works if the VR client is master.
- Backpack: item `Backpack` (`Backpack.cs`) is a normal Item; held in slot 3; worn on back via `CharacterBackpackHandler`/`BackpackOnBackVisuals` (visuals only). Opening: interacting with a backpack on the ground/another player calls `GUIManager.OpenBackpackWheel` (`Backpack.cs:32`) (radial wheel to stash/retrieve; `Backpack.Stash`, `RPCAddItemToBackpack`). `Backpack.Wear` -> pickup. `CharacterBackpackHandler.StashInBackpack` (`:~150`) for stashing in another character's backpack. Items in a backpack are real `Item` objects in `InBackpack` state at `backpackSlotTransform`, interactable via `RequestPickup`/`ClearDataFromBackpack` (`Item.cs:644`).
- Items on other players' backs (not the player's own view) via `BackpackOnBackVisuals`.

### 5.4 Interaction raycast (`Interaction.cs`)
- `Interaction : MonoBehaviour` (`[DefaultExecutionOrder(600)]`), `Interaction.instance`. `LateUpdate` (`:78`): if `canInteract` (not passed out, `CanDoInput`, no `currentStickyItem`) -> `DoInteractableRaycasts` (`:196`) -> `bestInteractable`, `bestCharacter` -> `DoInteraction` (`:99`).
- `DoInteractableRaycasts`: (1) special: carrying someone with slot 3 selected targets them; (2) stuck items / thorns / arrows on self via camera angle; (3) `HelperFunctions.LineCheckAll(camPos, camPos + camFwd*distance, AllPhysical, ..., QueryTriggerInteraction.Collide)`, nearest non-self, non-held-item collider's `GetComponentInParent<IInteractible>()` filtered by `IsInteractible`; (4) fallback `Physics.SphereCastNonAlloc(pos + fwd*area/2, area, fwd, ...)` choosing smallest angle to forward with line-of-sight to TerrainMap. `distance = 2`, `area = 0.5`, `maxCharacterInteractAngle = 90` (`:7-11`).
- `IInteractible` (`IInteractible.cs`): `IsInteractible(Character)`, `Interact(Character)`, `HoverEnter/Exit`, `Center()`, `GetTransform()`, `GetInteractionText()`, `GetName()`. `IInteractibleConstant` adds hold-to-interact: `IsConstantlyInteractable`, `GetInteractTime`, `Interact_CastFinished`, `CancelCast`, `ReleaseInteract`, `holdOnFinish`. Implementers: `Item`, `Backpack`, `RopeSegment`, `JungleVine`, `ClimbHandle`, `CharacterInteractible` (carry/eat/revive), `Luggage`, `Campfire`, `RespawnChest`, `Bugfix`, `Skelleton`, `ScoutStatue`, `ScoutmasterSoulPillar`, `PeakGatePortal`, airport kiosks, `ThornOnMe`, `FakeItem`, `GhostFire`, `TriggerOnInteract`, `RocketFuse`, `ScoutCannonFuse`.
- `Interact` is also hold-driven: `interactIsPressed` held on a constant interactable accumulates `currentInteractableHeldTime`; `Interact_CastFinished` fires at `GetInteractTime` (e.g. cannibalism 3 s). Input flow: `readyToInteract` resets when `interactIsPressed` is false.

### 5.5 Patch points for physical interaction (VR)
| goal | where |
|---|---|
| Grab/pick up by reaching (hand overlap) | Postfix/prefix `Interaction.DoInteractableRaycasts` (`Interaction.cs:196`) to select an `IInteractible` from a hand overlap sphere instead of camera ray (`out interactableResult`), and drive `Character.input.interactWasPressed/IsPressed` from grip so `DoInteraction` calls `Interact`. Or call `interactible.Interact(Character.localCharacter)` yourself. Keep `canInteract` semantics (`CanDoInput`). Also hands could use `Item.ALL_ACTIVE_ITEMS` proximity. |
| Equip hotbar by reaching to shoulder/belt | Call `character.refs.items.EquipSlot(Optionable<byte>.Some(slot))` (`CharacterItems.cs:482`) or set `input.selectSlot*/HotbarKey*` via `CharacterInput.Sample` postfix (`itemSwitchBlocked` honoured by `HotbarKey*`). Patch `CharacterItems.DoSwitching` (`:729`) gate conditions if needed. |
| Hold-to-eat (food to mouth) | Postfix `CharacterInput.Sample` to set `usePrimaryIsPressed` only while hand/item position is within ~0.15 m of head `Mouth` area (`Character.Head`/`animationHeadTransform` + forward); `Item.usingTimePrimary` then fills `castProgress` normally and `Action_Consume` runs on `OnPrimaryFinishedCast`. Item events: `OnPrimaryStarted/Cancelled` for haptics. `Item.CanUsePrimary` (`:797`) is the gate. |
| Throw with hand velocity | `DoDropping` (`:252`): drive `dropWasPressed/IsPressed/WasReleased` from a throw gesture; `throwChargeLevel` accrues with time (could be set from hand speed in a postfix); `DropItemRpc` args `velocity` (sender's velocity, `CharacterItems.cs:~278`) can carry hand velocity (see 5.3). |
| Drop / stash in backpack | `RPC DropItemRpc`; backpack via `CharacterBackpackHandler.StashInBackpack` (`CharacterBackpackHandler.cs:~150`) or the wheel `Backpack.Stash`. |
| Aimed items (dart, rope shooter, vine shooter, constructables, climbing spike, pings) | Replace `MainCamera.instance.transform.position/forward` at call sites listed in 2.2 with the held-item/controller pose (transpiler or temporarily set camera transform inside prefix/postfix pairs). |
| Show own hands/arms | `HideTheBody.Toggle` postfix (2.1); IK targets (section 6). |

---------------------------------------------------------------------
## 6. Animation and IK

### 6.1 Structure
- `Animator` on `refs.rigCreator` child (`refs.animator`). Controllers: `CharacterAnimations.defaultController` / `noHeadBobController` (assigned by HeadBobSetting, `CharacterAnimations.cs:152`). The PlayableGraph is stopped and set to **manual** (`CharacterRagdoll.Start` `CharacterRagdoll.cs:~45`) and evaluated explicitly in `CharacterRagdoll.FixedUpdate` (`:151-170`) in this order: `RotateCharacter` (`SetRotation`) -> `ikRigBuilder.SyncLayers()` -> `ikRigBuilder.Evaluate(dt)` -> `animator.playableGraph.Evaluate(dt)` -> **`animations.ConfigureIK()`** -> `Bodypart.SaveAnimationData()` for all parts -> `SaveAdditionalTransformPositions()` -> `ResetRotation()` -> `Bodypart.ResetTransform()` (snap bones back to rigidbody positions). Runs for local and remote characters.
  - Note the order: `ConfigureIK` writes IK targets **after** this tick's evaluate, so targets take effect on the next FixedUpdate (one fixed-step latency; negligible at 50 Hz but matters for jitter at 90 Hz displays). Setting the targets later in the frame (LateUpdate) also works because the next FixedUpdate reads them; just write them every frame and keep them current in FixedUpdate.
- Animator parameters set each Update in `CharacterAnimations.Update` (`:160-300`), all derived from replicated data so remote clients animate identically: `Input X/Y` (from `movementInput`, synced as 4 direction flags), `Sprint`, `Crouch`, `Is Grounded`, `Velocity Y/Z` (`avarageVelocity`, synced), `Look Y` (head pitch relative), `Look X` (from `lookInput`, **not synced**: remote is 0), `Climb Surface`, `Climb Rope`, `Climb Jump`, `Vine Hang/Slide/Type`, `Reach`, `Reach Type`, `Grab`, `Hang`, `Help`, `Webbed`, `Is Sliding`, `Charge Jump`, `Jump`, `Since Grounded`, `Myers Distance`, `Floor Lean Forward/Right`, `Idle` (item idle index `holdingItemIdleIndex` = 2 when holding an item), `Throw Charge` (from `items.throwChargeLevel`), `Throw` (`throwTime`), `Point`/`Point X/Y` (when `CharacterAnimations.point` GameObject set, ping pointing), `Emote`, `InWater`.
- Emote: `PlayEmote(string)` (`:410`) -> `RPC RPCA_PlayRemove(string emoteName, bool succeeded)` (`:417`, All) -> `animator.Play(emoteName, 0, 0)`; special names `A_Scout_Emote_PlayDead` (Fall 3 s), `A_Scout_Emote_BackFlip`. Emote ends after 2 s or on movement. The wheel is `EmoteWheel.Choose` (`EmoteWheel.cs:87`).
- `CharacterAnimations.PlaySpecificAnimation(string)` (`:~390`) plays a state by name (used for "Movement" reset after cancelling item use).

### 6.2 Bones / body parts
`BodypartType` enum (`BodypartType.cs`) is generic across all rigs (humans, animals, bots): `Hip, Mid, Torso, Neck, Head, Arm_L, Elbow_L, Hand_L, Arm_R, Elbow_R, Hand_R, Leg_L, Knee_L, Foot_L, Leg_R, Knee_R, Foot_R, Item, Mouth, Jaw_*, Hip_L/R, Shoulder_L/R, Toe_L/R, Finger_L/R, Unnasigned_*, Tail_*, Extra_*, many Leg_n/Knee_n/Foot_n, Spine_n, Jiggle_n, Finger_n_m_L/R (15 per hand), Final`. The player scout uses roughly: `Hip, Mid, Torso, Head, Arm_L/R, Elbow_L/R, Hand_L/R, Leg_L/R, Knee_L/R, Foot_L/R` (the physics-ragdoll set; `Neck`/`Mouth` may exist, UNVERIFIED which of the others are populated on the scout prefab). Finger bones: the enum has finger slots, but whether the scout prefab has finger rigs is UNVERIFIED; `Bodypart` rigidbodies are only on `RigCreator.parts`. Use `character.refs.ragdoll.partDict` (`CharacterRagdoll.cs:partDict`) at runtime to list what exists. Accessors: `character.GetBodypart(BodypartType)`, `.Rig` (Rigidbody), `Bodypart.transform` (cached).
- `Bodypart` (`Bodypart.cs`, `[DefaultExecutionOrder(-1)]`): `Animate(force, torque)` (`:143`): sets the `ConfigurableJoint` target rotation from the animated local rotation, applies torque to match animated rotation, applies a force toward `hip + targetOffsetRelativeToHip` (animated offset from animated hip). Forces are scaled by `currentRagdollControll`.

### 6.3 Existing arm IK: yes, we can drive it
- Animation Rigging `TwoBoneIKConstraint` x2 (`refs.ikLeft`, `refs.ikRight`), targets `refs.IKHandTargetLeft/Right`, rig `refs.ikRig` (weight), builder `refs.ikRigBuilder`. Only present if the prefab has a `RigBuilder` (`Character.cs:393`); guard with null checks (`refs.ikRight` null otherwise).
- Weights each Update/LateUpdate in `HandleIK` (`CharacterAnimations.cs:339`): reaching (`ReachIK`: not crouching, `isReaching`, `sinceGrabFriend > 0.5`) -> rig 1, right 1, left 0; holding an item with `overrideIKForSeconds <= 0` -> rig 1, right 1, left 1 unless `item.rightHandOnly`; else rig weight 0 (arms follow the plain animation).
- Target writes in `ConfigureIK` (`:390`): with an item: left/right target = item `Hand_L/Hand_R` mapped via `animationItemTransform`; reaching: right target = `animationHeadTransform.position + animationLookTransform.TransformDirection((0.15,-0.1,1.5))`, local euler `(-30,-90,-70 + lookValues.y)`.
- **Coordinate frame**: IK targets are in the *animated* skeleton's world space (which is offset from the physical ragdoll by `animationHipTransform.position - hip.Rig.position`). Convert a physical world pose to IK space with `ikPos = physPos - hip.Rig.position + animationHipTransform.position` (inverse of `Character.GetAnimationRelativePosition` `Character.cs:1363`). Rotations need no conversion except the rig root yaw already applied by `SetRotation`. The target rotation is hand-bone orientation (matches the item `Hand_R` child orientation convention: forward = fingers? see item prefab, UNVERIFIED; calibrate with a test item).
- **Patch**: Harmony **Postfix on `CharacterAnimations.ConfigureIK`** (`CharacterAnimations.cs:390`) to overwrite `refs.IKHandTargetLeft/Right` position+rotation from controller poses; Harmony **Postfix on `HandleIK`** (`:339`) to force `ikRig.weight = ikLeft.weight = ikRight.weight = 1` (also when no item is held), and keep `data.overrideIKForSeconds` irrelevant. Both are called from `Update` and `LateUpdate` (HandleIK) and the physics tick (ConfigureIK).
  - When holding an item, the item is welded to the hand rigs (FixedJoint) and also force-pulled toward the *look-frame* hold pose (`HoldItem` `CharacterItems.cs:170`) – these will fight your VR hand pose. Patch `CharacterItems.GetItemHoldPos` (`:706`), `GetItemHoldForward/Up` (`:701/704`) (or `HoldItem`) to use the controller pose instead, and shift `animationItemTransform` (set in `SaveAdditionalTransformPositions` `CharacterRagdoll.cs:225-230`) so the IK item targets (`GetItemPosRight/Left`) follow the controller. Simplest: postfix `CharacterRagdoll.SaveAdditionalTransformPositions` and overwrite `refs.animationItemTransform.SetPositionAndRotation(...)` with the (animated-space) controller pose of the holding hand; both hold force and IK targets derive from it.
- Head: no head IK; the head bone follows the animation (`Look Y` parameter blends head pitch) and the physical head joint; no roll. `refs.animationHeadTransform` holds the animated head. The camera does not read the head bone rotation except when ragdolled.
- Fingers: no finger IK or curl parameters found; hands are bone/animation poses (open hand vs. grip is baked into the animation or item pose).
- Arm positions without an item and without reach: pure animation (walk cycle swing, climbing animation). With IK rig weight 1 and targets from controllers, arms can follow the controllers in all states except where other code sets weights (`Climbing`, carry) - override weights every frame.
- Elbow hint: `TwoBoneIKConstraint` hint transform not exposed; elbows come from the constraint's hint target in the prefab (UNVERIFIED).

---------------------------------------------------------------------
## 7. Networking (Photon PUN 2)

### 7.1 What the Character syncs continuously
Only **one** continuous stream per character in code: `CharacterSyncer : PhotonBinaryStreamSerializer<CharacterSyncData>` (`CharacterSyncer.cs`; base `PhotonBinaryStreamSerializer.cs` is an `IPunObservable` that writes `bool` + `byte[]` blob). Network rate: `PhotonNetwork.SerializationRate = 30`, `SendRate = 30` (`Peak.Network/NetworkingUtilities.cs:121-122`). Item sync: `ItemPhysicsSyncer` (`ItemPhysicsSyncData`: pos, rot, linear/angular velocity) – applied by remotes **only when `itemState == Ground`** (`ItemPhysicsSyncer.cs:FixedUpdate`), so held items are positioned locally from the holder's ragdoll on each client.
No `PhotonTransformView`/`PhotonRigidbodyView`/`PhotonAnimatorView` is referenced in code; whether the prefab has any is UNVERIFIED (they are PUN components, not in this decomp). The code design (hip target + local physics + local animator, below) indicates none for bodyparts.

`CharacterSyncData` (`CharacterSyncData.cs`, serialize `:59`, deserialize `:116`) written by `CharacterSyncer.GetDataToWrite` (`:32`):
| field | wire format | notes |
|---|---|---|
| `hipLocation` | float3 (full) | `GetBodypart(Hip).Rig.position` (physical hip). Remote lerps rigs toward it (`InterpolateRigPositions` `:166`: `tPos = Lerp(last, remote, 0.3)`, `MoveAllRigsInDirection(delta*0.1)`; snaps if > 10 m) |
| `lookValues` | 2x half (`WriteHalf2`) | yaw, pitch degrees. Remote: lerped `lastLook` -> `RemoteValue.lookValues` over the 1/30 s window in `Update` (`CharacterSyncer.cs:132`) and written into `data.lookValues` |
| `movementInput` | 4 flags (WALK_RIGHT/LEFT/FORWARD/BACKWARD) | digital: remote `input.movementInput` is -1/0/+1 per axis |
| flags | byte | SPRINT, ROPE_CLIMBING, CLIMBING, IS_GROUNDED + walk flags |
| `sinceGrounded` | half | only applied if differs > 2 s |
| `ropePercent` | half, only if rope climbing | |
| `averageVelocity` | half3 | remote adjusts rig velocities by ratio/delta (`OnDataReceived`) |
| `groundNormal`, `groundPos` | half3 | |
| `climbPos` | half3, only if climbing | |
| `stammina`, `extraStammina`, `spectateZoom`, `isChargingThrow` | half | `isChargingThrow` >0.5 -> `items.isChargingThrow` |
| `petrify` | int | |

**Never change this format**: vanilla peers use the same `Deserialize`; a different byte layout desyncs/throws. Anything extra must travel outside this stream (7.4).
Half precision: a yaw of 1000 deg has ~0.5 deg resolution; keep yaw wrapped to [-180,180] (the vanilla game accumulates unbounded; mod should wrap).
Position: remote players see your **hip** location only; they re-simulate the rest with physics + animation.

### 7.2 Discrete state and RPCs relevant to pose/items (all via `refs.view` / `photonView.RPC`)
Character-level (class: method (target), file:line):
- Crouch: `CharacterMovement.RPCA_SetCrouch(bool)` (All) `CharacterMovement.cs:226`.
- Jump: `CharacterMovement.JumpRpc(bool isPalJump)` (All) `:626`; `JetpackRPC(float)` `:591`; `RocketRPC(int)` `:531`; `RocketExplodeRPC` `:984`.
- Climb: `CharacterClimbing.StartClimbRpc(Vector3,Vector3)` `:349`, `StopClimbingRpc(float)` `:627`, `RPCA_ClimbJump()` `:238`; `ClimbHandle.RPCA_Hang/UnHang(PhotonView)`; `CharacterRopeHandling.GrabRopeRpc(PhotonView,int)` `:144`, `StopRopeClimbingRpc(bool)` `:84`; `CharacterVineClimbing.GrabVineRpc(PhotonView,int)` `:155`, `StopVineClimbingRpc(bool)` `:136`, `RPCA_SyncVineClimb(float,float)` `:84`.
- Reach/grab/kick: `CharacterGrabbing.RPCA_StartReaching()` `:168`, `RPCA_StopReaching()` `:152`, `RPCA_StartGrabbing()` `:162`, `RPCA_GrabAttach(PhotonView,int,Vector3)` `:55`, `RPCA_GrabUnattach()` `:72`, `RPCA_Kick()` `:174`, `RPCA_KickImpact(Vector3)` `:185`.
- Carry: `CharacterCarrying.RPCA_StartCarry(PhotonView)` `:56`, `RPCA_Drop(PhotonView)` `:92`.
- Items: `CharacterItems.EquipSlotRpc(int slotID, int viewID)` `:546`, `DropItemRpc(...)` `:385`, `DropItemFromSlotRPC(byte,Vector3)` `:349`, `DestroyHeldItemRpc()` `:371`, `OnPickupAccepted(byte)` `:441`, `RPC_InitHoldingItem(PhotonView)` `:921`, `RefreshAllCharacterCarryWeightRPC`. Item: `Item.RequestPickup(PhotonView)` `:606`, `DenyPickupRPC`, `Consume(int)` `:1096`, `SetKinematicRPC(bool,Vector3,Quaternion)` `:1199`, `SetItemInstanceDataRPC(ItemInstanceData)` `:1255`, `SendFeedDataRPC`/`RemoveFeedDataRPC`, `RPC_SetThrownData(int,float)` `:1451`, `ApplyForceRPC`. Player: `SyncInventoryRPC(byte[],bool)` `Player.cs:170`, `RPCRemoveItemFromSlot(byte)`, `RPC_SetInventory(byte[])`, `RPC_GetKicked(PhotonMessageInfo)` `:193`.
- Emotes / idle: `CharacterAnimations.RPCA_PlayRemove(string,bool)` `:417`; `CharacterCustomization.SetCharacterIdle_RPC(int)` `CharacterCustomization.cs:709`; ping `PointPinger.ReceivePoint_Rpc(Vector3,Vector3)` `PointPinger.cs:84`.
- State: `Character.RPCA_Fall(float,float)` `:1752`, `RPCA_UnFall`, `RPCA_PassOut`/`RPCA_UnPassOut`/`RPCA_Die`/`RPCA_Zombify`/`RPCA_ReviveAtPosition`, `WarpPlayerRPC`, `RPCA_AddForceAtPosition`, `RPCA_AddForceToBodyPart(BodypartType,Vector3,Vector3)`, `CharacterData.RPC_SyncOnJoin(...)` (sent to joining players: contains `isReaching`, `isCrouching`, `currentItem`, climbing flags).
- Non-RPC: item use state is **not** replicated as an event; remote players see held-item use results through each item's own RPCs/IPunObservable (`Candle`, `Lantern`, `Flare`, `JetpackItem`, `AmuletBase`, ...), and animator bools set locally from replicated data. Eating progress (`castProgress`) is local; the consume result is `Consume` RPC (All).

### 7.3 Which values must keep flowing through normal paths so vanilla peers see sensible motion
Must remain real and consistent (the vanilla game derives remote animation and physics from them):
1. `data.lookValues` (yaw, pitch) -> synced look; drives remote body yaw (`SetRotation`), head pitch, hand/held-item placement, throw direction on the master.
2. `input.movementInput` (even though VR locomotion might be analog/teleport) -> remote walk animation direction and speed; use the real stick values.
3. Hip physical position (`Bodypart Hip` rigidbody) -> `hipLocation`. All locomotion, including room-scale offset, must actually move the ragdoll (`MoveAllRigsInDirection` / forces), or remote players see no room-scale motion.
4. `isSprinting`, `isGrounded`, `avarageVelocity`, `groundNormal/Pos`, `isClimbing`, `climbPos`, rope/vine state, `isCrouching` (RPC), `isReaching` (RPC), `isChargingThrow`: use the game's own paths (inject input, not data) so state machines and RPCs stay coherent.
5. Items: always equip/pickup/drop/use via the game's slot RPCs (`EquipSlot`, `Interact`, `DropItemRpc`), never by moving item rigidbodies or toggling `currentItem`.
6. Do not call `RPCA_PlayRemove` with unknown names (would `Play` a missing state; harmless but logs a warning).

### 7.4 Can unmodded peers see the VR pose? Value by value

**Per-value answer.** What a vanilla client renders for a VR player is entirely derived from the replicated state above plus its own animator; a modded client has no network channel in vanilla to describe hand or head-roll pose.

| quantity | vanilla-synced channel | what vanilla renders | precision / rate | limits |
|---|---|---|---|---|
| Head yaw | `lookValues.x` (half) in `CharacterSyncData` | Whole body turns to `lookDirection_Flat` via `SetRotation` (so body yaw == head yaw), head/eyes turn | half float, 30 Hz, lerped per packet; resolution ~0.1 deg near 0, worse as angle grows (wrap!) | Body follows head instantly; can't look over shoulder independently of body |
| Head pitch | `lookValues.y` (half), clamp ±85 | Animator `Look Y` + `animationLookTransform` (pitch × 0.5) tilt head/arms/held item; remote sees ~full look direction for `lookDirection` | same | pitch-only; clamp at ±85 |
| Head roll | none | not shown | | **No**: no roll in `lookValues`, none anywhere in the pose path. The local camera adds roll only from ragdoll head rotation (`CharacterCam`) |
| Head position | `hipLocation` (full float3) | Body is placed from the hip; head = animated offset above hip | ~30 Hz, lerped 0.3/frame | Crouch-lean/room-scale head offset beyond what the hip moved is not shown. Physical hip must move |
| Left hand position/rotation | none direct | Shown only (a) holding a two-handed item: hand at the item's `Hand_L` grip relative to `animationItemTransform` (look frame + that client's local `item.defaultPos`); (b) never as a free hand (reach is right hand only) | follows look values | **Partial**: VR left hand pose is not transmittable; a vanilla client puts it at the fixed in-front-of-face hold pose. Any local `defaultPos` tweaks apply only to the modifier's own rendering of everyone |
| Right hand position/rotation | `isReaching` flag (RPC) + holding state | (a) holding any item: item at fixed hold offset in front of face along `lookDirection`; (b) `isReaching`: right arm forward at fixed offset (0.15,-0.1,1.5) in look frame; (c) otherwise animation (walk swing) | | **Partial** at best: only discrete "arm forward" / "item in front", never the real controller pose. Pitch of the item pose uses lookValues, so aiming the head aims the arm |
| Held item pose | local on each client from holder's look values; item `ItemPhysicsSyncer` ignored while Held | in front of face (hold pose), rotated by look direction × `defaultForward` | | Cannot move item independently of head on vanilla screens (workaround: nothing in vanilla) |
| Climbing hands | `isClimbing`, `climbPos` (half3), `Climb Surface` anim | canned climb animation facing the wall, torso pulled to `climbPos + normal*0.4`; hands NOT placed on holds | | **No** hand fidelity; `climbPos` is where the *body* is, so you can only influence it via real `climbPos` |
| Rope/vine | rope/vine flags + percent | canned animation | | No |
| Fingers | none | none (no finger params in the animator, no finger network data) | | **No** |
| Emotes | `RPCA_PlayRemove(name, bool)` | existing animator emote states by name | reliable RPC | existing emotes only; can map VR gestures to emote names (e.g. wave) and send them |
| Ragdoll bodypart transforms | **not synced** (only hip) | each client simulates the remote ragdoll locally from hip target + animation | | cannot write arbitrary bodypart rigidbody poses to remote clients |
| Animator params | not synced as such; derived from replicated data on each client | local animator on each client | | Synced inputs only |

**Summary table: VR pose visible to vanilla peers**

| | visible to vanilla? | mechanism |
|---|---|---|
| head yaw | yes | `lookValues.x` (body yaw follows it) |
| head pitch | yes | `lookValues.y` (±85) |
| head roll | no | no roll in any synced value |
| head translation | partial | only via hip position `hipLocation` (needs ragdoll actually moved) |
| left hand | no | no channel; appears only at the item's left grip in the fixed hold pose if a two-handed item is held |
| right hand | partial | held item at fixed pose in front of face; `RPCA_StartReaching` shows right arm forward; not the real controller pose |
| fingers | no | none |

**Modded-to-modded extras (not seen by vanilla)** – the least invasive custom channels, in order of preference:
1. `PhotonNetwork.RaiseEvent(byte code, object content, RaiseEventOptions{Receivers = ReceiverGroup.Others}, SendOptions.SendUnreliable)` with a code in **100-199** (200+ reserved by Photon; used by the game: 1-4 `CustomCommandType` and 18 = kick, `CustomCommandType.cs`, `Peak.Network/PhotonShim.cs:149-158`). Vanilla receivers: `CustomCommandListener.OnEvent` (`Zorro.PhotonUtility/Zorro.PhotonUtility/CustomCommandListener.cs:52`) ignores unregistered codes >= 6 silently (it logs an error only for `code < 6`); `PhotonShim.RaiseGenericEvent` forwards all events to `PlayerHandler.OnNetworkEvent` (`PlayerHandler.cs:161`) which does nothing (`_ = eventData.EventCode`). Payload: `byte[]` with `actorNumber` implied (`EventData.Sender`), head quat (roll), two hand pose (pos relative to hip, quat as 3 halves/ smallest-three), finger curls 10 x byte. ~60 bytes at 30 Hz is fine. Only send when at least one other peer announced modded (see 2).
2. Handshake / capability flag: `PhotonNetwork.LocalPlayer.SetCustomProperties({"PeakXR": version})` (same mechanism as `UserID`/`PlatformFamily` in `Player.cs:336-363`, `NetworkingUtilities.cs:138-151`). Vanilla ignores unknown properties (`ReconnectHandler.OnPlayerPropertiesUpdate` is empty, `ReconnectHandler.cs:357`). Read `player.CustomProperties` of other actors to enable the stream.
3. Not recommended: extra PhotonView/RPC names on existing views (vanilla logs "method not found" errors per call), changing `CharacterSyncData`, adding observed components to the vanilla PhotonView (changes the stream layout for all).
Render received data on the modded side by overriding IK targets of the *remote* character's `refs.IKHandTargetLeft/Right` in the same `ConfigureIK` postfix (works for any `Character`, not only local) and the remote head via a postfix on `Character`/camera-independent head bone (head roll needs a bone override since no IK exists).

Quantities to keep in mind for the vanilla view: because everything is derived from the head's look direction, a VR player standing still but swinging a controller around looks like standing still holding the item in front of the face.

### 7.5 Ownership/authority notes
- Local character is owned by its client; `IsMine` branches run only locally (`CharacterMovement.SetMovementState`, `CharacterGrabbing.Update`, `CharacterClimbing.Update`). Remote characters get `input.movementInput`, `isSprinting`, etc. written by `CharacterSyncer.OnDataReceived`.
- Master client does world spawning (item drop spawn `PhotonNetwork.InstantiateItemRoom`), pickup validation (`RequestPickup`), scene sync (`AutomaticallySyncScene`), kick.
- Held item ownership transferred to holder by master in `Item.SetState(Held)`.

---------------------------------------------------------------------
## 8. Matchmaking, version, anti-cheat

- Photon: `PhotonNetwork.GameVersion = BuildVersion(Application.version).ToString()`; `PhotonServerSettings.AppSettings.AppVersion = BuildVersion.ToMatchmaking()` = `"{Major}.{Minor}"` (`Peak.Network/NetworkingUtilities.cs:123-126`, `Peak.Network/PhotonShim.cs:91-94`; `Zorro.Core.Runtime/Zorro.Core/BuildVersion.cs`). Only major.minor (2.6) must match; patch/suffix ignored. Do not change `Application.version`.
- Steam lobby: lobby data key `"PeakVersion"` = `ToMatchmaking()` set on lobby creation (`SteamLobbyHandler.cs:409`); joiners compare (`SteamLobbyHandler.cs:332-345`) and show a mismatch modal. Equal for a mod that keeps `Application.version`.
- No anti-cheat, mod list, assembly hash, or signature check found anywhere in `Assembly-CSharp`, `Zorro.*`: grep for `cheat`, `anticheat`, `Assembly.GetAssemblies`, `modded`, `BepInEx`, `Harmony` returned nothing relevant (only an achievement-related "Bookworm cheat" emote check `EmoteWheel.cs:96`, unrelated). Steam VAC is not engaged by this code. Third-party: Steamworks auth + Photon `AuthValues` (`UserID`) only.
- Moderation: host can kick via `RaiseEvent(18, userId)` (`PhotonShim.cs:149-158`) and `Player.RPC_GetKicked` (`Player.cs:193`); blocklist `Portningsbolaget/BlocklistTable.cs`. These are user-driven, not mod-driven. A mod that spams an unknown RPC/event could be noisy in logs only.
- Achievements: `AchievementManager` may be affected by modifying state (e.g. debug flags), not required for VR; cheats via console commands exist but nothing blocks mods. UNVERIFIED: Steam achievement unlock for modded sessions; not guarded in code seen.
- Risk: `Application.isEditor`/`Debug.isDebugBuild` gates are unrelated.

---------------------------------------------------------------------
## 9. Patch point cheat sheet (Harmony)

| purpose | target | type |
|---|---|---|
| attach VR once local char exists | `Character.Start` (`Character.cs:497`) or poll `Character.localCharacter` | postfix |
| inject VR buttons/sticks | `CharacterInput.Sample(bool)` (`CharacterInput.cs:212`) | postfix |
| HMD -> yaw/pitch | `CharacterMovement.CameraLook()` (`CharacterMovement.cs:283`) then `Character.RecalculateLookDirections` (`Character.cs:1347`) | postfix (or prefix returning false) |
| camera pose / FOV / shake | `MainCameraMovement.LateUpdate` (`:61`), `GetFov` (`:328`), `GamefeelHandler.AddPerlinShake/AddRotationShake_*` (`GamefeelHandler.cs:46,36,41`) | postfix / prefix-false |
| camera overrides | `MainCamera.SetCameraOverride` (`MainCamera.cs:27`), `MainCameraMovement.OverrideCam` (`:309`) | prefix/postfix |
| own body visibility | `HideTheBody.Toggle` (`HideTheBody.cs:57`) / `Update` (`:39`) | postfix |
| controller-relative locomotion | `Character.CalculateWorldMovementDir` (`Character.cs:1311`) | postfix |
| hand IK from controllers | `CharacterAnimations.ConfigureIK` (`:390`) + `HandleIK` (`:339`) | postfix |
| held item follows hand | `CharacterRagdoll.SaveAdditionalTransformPositions` (`CharacterRagdoll.cs:207`), `CharacterItems.GetItemHoldPos/ForwardUp` (`CharacterItems.cs:706,701,704`), `HoldItem` (`:170`) | postfix / prefix |
| interact by hand reach | `Interaction.DoInteractableRaycasts` (`Interaction.cs:196`) | prefix/postfix |
| aim items by hand | `MainCamera.instance.transform` call sites in 2.2 | transpiler or swap pose around call |
| throw by hand velocity | `CharacterItems.DoDropping` (`:252`) | postfix/transpiler |
| wall climb from hands | `CharacterClimbing.TryToStartWallClimb` (`:534`), `GetRequestedPostition` (`:~290`) | prefix/postfix |
| ping | `PointPinger.DoPing` (`PointPinger.cs:61`) uses `Camera.main.ScreenPointToRay` | prefix |
| modded-peer channel | `PhotonNetwork.RaiseEvent` code 100-199 + LocalPlayer custom property | n/a |

Risks:
- Yaw `lookValues.x` is unbounded in vanilla; wrap it for half-float sync.
- Held items are welded to hand rigidbodies by FixedJoint and also force-pulled to the look-frame hold pose; both must be re-targeted or they fight.
- Physical hip = what peers see; room-scale translation requires moving the ragdoll (teleport of rigs bypasses collision).
- Camera is `LateUpdate` order 500 and also written by spectate/override paths; handle fully-passed-out, dead, rocket, binocular, cutscene branches.
- HUD/menus/wheels are screen-space UI (UNVERIFIED canvas mode); need a VR UI solution.
- Local head body hidden by `_VertexGhost`; arms hidden unless patched.
- IK targets are in animated space, not physical space; convert with the hip offset.
- Vanilla peers cannot see hands or head roll; richer pose only for modded peers via `RaiseEvent`.
- Never alter `CharacterSyncData` layout; never add RPC names vanilla clients lack.
