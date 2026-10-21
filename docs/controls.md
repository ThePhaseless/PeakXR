# VR controls

The default layout for a right-handed player. With `Input.DominantHand = Left` the two controllers swap roles.

| Control | Left controller | Right controller |
|---|---|---|
| Stick | Move | Left/right: turn. Up/down: next or previous item |
| Stick click | Tap: sprint. Hold: push to talk | Crouch |
| A / X (lower button) | Tap: put the item away or take it out again. Hold: take out the backpack | Jump |
| B / Y (upper button) | Tap: ping. Hold: emote wheel | Drop. Hold to charge a throw |
| Grip | Grab or interact | Grab or interact |
| Trigger | Use (secondary) | Use (primary) |
| Menu | Release within 1 s: pause. Hold 1 s: recenter | Same as left |

Notes:

- Ropes and vines: grip one to grab it, then pull hand over hand to climb along it (`Interaction.RopeHandOverHand`). Open both hands for longer than `ClimbRegripGrace` to let go; jump and the stick still work.
- Pitons and pickaxe handles: grip the handle to hang from it, and open the hand to let go (`Interaction.PitonGripHang`). With the setting off, interact again or jump to let go.
- Use follows the hand that holds the item: its trigger is the primary use and the other trigger is the secondary use. With empty hands the right trigger climbs and the left trigger reaches out to help a friend, like the game's mouse buttons.
- Grip, trigger and the hand throw come from the physical interaction settings (`Interaction` section). There, `DropButton` picks the drop button. The default, `DominantSecondary`, is B on your dominant hand, so it moves to the left controller with `Input.DominantHand = Left`. Names starting with `Left` or `Right` stay on that controller whichever hand is dominant.
- The emote wheel opens centred on the hand that holds Y. Move that hand out onto an emote on the ring, point the laser at one, or push the stick, then release Y to play it. The hand in the middle of the wheel or past its edge picks nothing. Stick up and down on the right controller flips the page. The backpack wheel opens at the hand that grips the pack; move the hand toward a pocket and let go.
- Menu: `Camera.RecenterHoldSeconds` (1 s by default) sets both uses. Let go before it to pause; keep holding to recenter. A small ring below the centre of your view fills while you hold, so you can see which one you will get.
- Valve Index: SteamVR keeps the system button for its dashboard. Squeeze either trackpad firmly instead; it works as the menu button.
- While you are dead or passed out you watch a teammate from a detached view behind them (`Camera.TakeoverView`). Flick the turn stick up or down to switch to the next or previous teammate; the view fades between them. Arrows on that stick mark next and previous, and the teammate's name, status bar and items float on them (`UI.SpectateOnPlayer`).
- When you draw an item, its prompts (use, drop, scroll) appear as small labels beside the buttons that do them for 3 s, and again whenever you look at your hand (`UI.ButtonLabels`).
- When you hold something with a scroll function (rope spool, binoculars, guidebook, scout cannon), stick up and down scroll it. Put the item away to change items.
- Vive wands and WMR controllers have no A/B buttons: click the top of the trackpad for B/Y and the bottom for A/X. On a Vive wand the centre of the pad is the stick click.
- Push to talk: hold the move-stick click for half a second and keep holding while you speak. The click itself still toggles sprint, which does nothing while you stand still. It only matters when the game's microphone mode is Push To Talk or Push To Mute. You can also hold a free hand at your mouth like a walkie-talkie; `Voice.HandToMouthTalk` turns that off.
- An open passport or guidebook sits above the hand that holds it. Turn pages by swiping your other hand across it. The passport's tabs and page buttons also take the laser, and the guidebook also turns with stick up and down.
- Menu panels at a kiosk (boarding pass, custom run options) and the end-of-run results take the laser like any other menu. At the end of a won run, any controller button skips the credits wait, the same as any key on a keyboard.

## VR Guide and practice

In VR the VR tab of the settings (title or pause menu) starts with a VR Guide section. It covers only what PeakXR adds or changes in VR; PEAK's own rules and items are left to the game.

- Demos: press Show to list one row per VR mechanic: grab and drop, two-hand hold, throw, eat, belt holster, backpack, wall climb, rope and vine, piton, cook, light a fire, open a suitcase, bugle, bandage, parasol, turning, walk and sprint, crouch, emote wheel, pause and recenter, menus and laser, wrist status and refused actions. Rest the laser on a row or click it and a small ghost of you acts it out beside the panel for two loops. Its controllers light the button it presses, the rings and notes are the same cues the game uses in play (a double ring and a rising note where an action works, a slashed ring and a falling note where it is refused), and the matching real controller ticks. With `UI.ActionHintText` on, a one-line caption sits under the ghost.
- Practice: press Start. The tab first shows only the comfort choices (turn mode, vignette strength, seated mode) with their usual previews. Press Start again to go: from the title it plays solo offline and walks you through stations in the airport. In the solo airport it starts where you are. Each station shows its demo ahead of you and marks where to act with a double ring: turn, grab an apple, holster it on your belt, eat, throw, climb the nearest wall, open the emote wheel. When you do it, a rising note plays, both controllers pulse and a ring widens. Two orbs float at chest height on your off-hand side: rest a hand in the chevron orb to skip a station, or in the cross orb to leave. While it runs, the Practice row in the VR tab reads Stop. Finishing, leaving and stopping all end back at the title. Boarding the plane ends the practice and keeps the run you started.
- The practice never runs online. In an online lobby, or in a solo run in progress, Start plays the refused cue at the row (the "Not here, start it from the title" label with `UI.ActionHintText` on) and does nothing.
- The first time you reach the title in VR, a small offer appears once beside the title panel: a ghost reaching for an apple and two orbs within reach. Rest a hand in the chevron orb to open the comfort-first practice view, or in the cross orb to dismiss it. It also goes away when you pick anything in the menu, and the Practice row stays in the guide for later.

## Items and fires by hand

Each of these has an on/off setting in the `Interaction` section. With a setting off, the grip and trigger work as before.

- Cook (`CookOverFire`): hold food over a lit campfire. It cooks while it stays above the flames and stops when you pull it out. A soft buzz grows as it cooks.
- Open a suitcase (`LiftLidToOpen`): grip the top of the case and lift. The game's open timer only runs as far as you have raised the lid, so lift slowly. A respawn chest needs both hands on the lid. Gripping the case low down holds it open the old way.
- Bugle (`BugleAtMouth`): raise it to your mouth to play. Tilt the bell up for a higher note and down for a lower one. The trigger plays it too.
- Light things (`LightWithFlame`): touch a lit torch, flare, candle or lantern to a cannon or rocket fuse, a ghost fire or an unlit campfire and keep it there until it catches.
- Bandages and other healing items (`PressToBody`): press the item to your chest or your other forearm to use it. Press it to a friend's body to use it on them. Food still goes to your mouth.
- Warm your hands (`WarmHandsAtFire`): hold open palms toward a lit campfire to feel a slow warm buzz. When you are cold and near a lit campfire, both controllers shiver faintly. This changes nothing in the game.
- Parasol (`ParasolRunner`): grip the shaft with your free hand and push up to open it, or pull down to close it.

## Turning and comfort

Settings live in the `Comfort` section. `Preset` sets several of them at once:

| Preset | Turning | Vignette | Move smoothing |
|---|---|---|---|
| Comfortable | Snap 30° | Strong | 0.3 s |
| Moderate (default) | Snap 30° | Medium | 0.15 s |
| Intense | Smooth 150°/s | Off | Off |

Changing `TurnMode`, `SnapTurnAngle`, `SmoothTurnSpeed`, `Vignette`, `VignetteStrength` or `MoveSmoothing` yourself switches the preset to Custom. The vignette darkens the edge of your view while your character moves, falls or smooth-turns without you moving. `SeatedMode` treats your seated head height as standing height.

## Moving

Settings live in the `Locomotion` section.

- `MoveRelativeTo`: the move stick walks where you look (`Head`) or where the move hand points (`Hand`).
- `SprintMode` and `CrouchMode`: `Toggle` (click once) or `Hold`.
- `PhysicalCrouch` (on): crouch for real to crouch in game. It triggers when your head is about a third lower than the standing height measured at your last recenter, and lets go a little above that. Your view stays at your real head. Seated mode never triggers it, and the crouch click keeps working.
- `ArmSwingSprint` (off): swing your arms like running while the move stick walks forward to sprint. The stick still decides where you go and how fast you walk; swinging alone never moves you. It stays off while you climb or hold an item with both hands.
- Walking around your play space moves your character with you. Your view stays where your head is and the character steps after it once you lean further than `LeanLimit` (0.35 m); a smaller lean bends the character's upper body instead. After standing back up from a step, the character shuffles slowly under your head once you hold still. `LeanLimit` replaced `LeanDistance` (0.2 m), which walked the character on every lean; an old `LeanDistance` line left in the config file is ignored and can be deleted. If the character cannot follow, for example against a wall or while climbing, and you get more than 1 m away from it, the view fades out and back in centred on the character. Turn `RoomScaleWalking` off to skip the stepping and keep only the fade.

## Game events

In VR the game never moves your view by itself. Each kind of event that would move or flash it gets a treatment instead, set in the `ComfortEvents` section:

| Setting | Event | Default |
|---|---|---|
| `Knockback` | Hits, bonks, blasts | Vignette pulse |
| `Launch` | Cannon, geyser, bad mushroom, super jump, tornado | Third-person view until you land |
| `Ragdoll` | Your body starts tumbling | Rumble |
| `ForcedLook` | Something turns your character (frog tongue, grabs) | Rumble |
| `Shake` | Camera shake | Rumble |
| `StatusFlash` | Damage, cold, poison and other status flashes | None |
| `PostEffect` | Sugar rush, energy drink, heat, petrify screen effects | None |
| `Teleport` | Your body jumps somewhere without a run transition | Fade |
| `WindPush` | Wind, push zones, lava bounce | Vignette pulse |

Each one accepts `None`, `VignettePulse`, `FadeHoldFade`, `HapticPulse` or `ThirdPerson`. `Intensity` scales every vignette pulse and rumble, and 0 turns them off. With `Haptics` on, hits, launches, falls and shakes also rumble, whatever their treatment.

`CutsceneView` picks how the end cutscene looks. `Viewpoint` (the default) puts you at the cutscene camera, facing where it looks, and you can look around. When the shot cuts or moves far, the view fades to the new spot. `FollowCamera` glides after the camera instead. That is motion you did not make, so you have to turn it on. `Screen` plays the cutscene on a cinema screen. `Mirrors` off hides mirror reflections, which in VR are drawn from a point between your eyes and look flat.
