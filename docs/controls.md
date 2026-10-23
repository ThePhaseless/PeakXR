# VR controls

The default layout for a right-handed player. With `Input.DominantHand = Left` the two controllers swap roles.

| Control | Left controller | Right controller |
|---|---|---|
| Stick | Move | Left/right: turn. Up/down: next or previous item |
| Stick click | Tap: sprint | Crouch |
| A / X (lower button) | Tap: put the item away or take it out again. Hold: take out the backpack | Jump |
| B / Y (upper button) | Tap: ping. Hold: emote wheel | Drop. Hold to charge a throw |
| Grip | Grab or interact | Grab or interact |
| Trigger | Use (secondary) | Use (primary) |
| Menu | Release within 1 s: pause. Hold 1 s: recenter | Same as left |

Notes:

- Ropes and vines: grip one to grab it, then pull hand over hand to climb along it (`Interaction.RopeHandOverHand`). Open both hands for longer than `ClimbRegripGrace` to let go; jump and the stick still work.
- Pitons and pickaxe handles: grip the handle to hang from it, and open the hand to let go (`Interaction.PitonGripHang`). With the setting off, interact again or jump to let go.
- Reaching for rock: an open hand on a hold you can grab shows a faint chalk ring and gives a tick (`Interaction.ClimbHoverRing`). The grab checks the rock right at your hand instead of along your view (`Interaction.ClimbGrabFromHand`). Both are on by default.
- Climbing feel (`Interaction.ClimbSurfaceFeel`, on): a hard catch ratchets, easy holds tick, slides grit, hazards prickle, wind shudders and a sliding vine whirrs. A rope anchor or piton that is about to fail warns in the gripping hand.
- Rope swing: lean your head along the swing to pump it higher (`Interaction.RopeSwingPump`, on). It works on ropes you put out yourself. Only a rope's owner can swing it. On someone else's rope, the first clear pump in each hold (three quick leans back and forth, in time with the swing) gives the refusal cue. Looking around or leaning to grab gives nothing.
- Gripping a rope, vine or piton the game refuses gives the refusal cue at that hand, with a ring, sound and tick that say why: not anchored, too slack, no stamina, or taken.
- An item on the ground is picked up when your eyes could see it: its nearest point, its centre or its top. An item half sunk into uneven ground still counts, but one behind a rock fin does not.
- A grab with a free hand while you hold an item puts the item in your pocket, with a pin notice.
- Two-hand items: your free hand joins the held item when it closes on the item's second grip, or on the item's body when nothing else is at that hand. A rope, chain, ledge or item there takes the grab instead.
- Pointing at the scout statue with an amulet in hand, or at a skeleton with the Book of Bones, and closing the grip (index straight) runs that action from up to the game's interact reach. It wins over rock or a belt slot in reach, but something right at your hand still takes the grab.
- When your character's arm cannot reach your controller, for example in a deep stoop, the held item leaves the character's hand and follows your controller. It goes back into the hand when the arm catches up.
- Use follows the hand that holds the item: its trigger is the primary use and the other trigger is the secondary use. With empty hands the right trigger climbs and the left trigger reaches out to help a friend, like the game's mouse buttons.
- Grip, trigger and the hand throw come from the physical interaction settings (`Interaction` section). There, `DropButton` picks the drop button. The default, `DominantSecondary`, is B on your dominant hand, so it moves to the left controller with `Input.DominantHand = Left`. Names starting with `Left` or `Right` stay on that controller whichever hand is dominant.
- Letting go: a slow grip release drops the held item (`Interaction.ReleaseToDrop`, on; off keeps the item until you throw it or press drop) and a fast one throws it (`ThrowByHand`). Only an item your grip closed on can be dropped or thrown this way. An item that arrives in a hand that is already gripping, such as one you pick with the stick or a wheel, or one a friend hands you, stays put until you open and close that grip again.
- The game ignores a throw or drop while the held item is still in use, for example while you eat. If you throw or open your hand then, the item stays in your hand and you get the wait cue.
- The emote wheel opens centred on the hand that holds Y. Move that hand out onto an emote on the ring, point the laser at one, or push the stick, then release Y to play it. The hand in the middle of the wheel or past its edge picks nothing. Stick up and down on the right controller flips the page. The backpack wheel opens at the hand that grips the pack; move the hand toward a pocket and let go. Letting go on nothing gives a soft cancel tick. A wheel the game refuses (for example while climbing) gives the refusal cue.
- Menu: `Camera.RecenterHoldSeconds` (1 s by default) sets both uses. Let go before it to pause; keep holding to recenter. A small ring below the centre of your view fills while you hold, so you can see which one you will get.
- Valve Index: SteamVR keeps the system button for its dashboard. Squeeze either trackpad firmly instead; it works as the menu button.
- While you are dead or passed out you watch a teammate from a detached view behind them (`Camera.TakeoverView`). Flick the turn stick up or down to switch to the next or previous teammate; the view fades between them. You can also point at a teammate and pull the trigger to watch them. Arrows on that stick mark next and previous, and the teammate's name, status bar and items float on them (`UI.SpectateOnPlayer`).
- When you draw an item, its prompts (use, drop, scroll) appear as small labels beside the buttons that do them for 3 s, and again whenever you look at your hand (`UI.ButtonLabels`). Each label sits on a dark plate, with the button's name as large as the label text.
- When you hold something with a scroll function (rope spool, binoculars, guidebook, scout cannon), stick up and down scroll it. Put the item away to change items.
- Rope spool, paying out and setting the length: hold the spool in one hand. Twist that wrist like a knob to pay rope out or reel it in, about 30 degrees per step (`Interaction.Tools.TwistScroll`, `TwistStepDegrees`). Or close the grip of your free hand at the spool, within about 15 cm, and pull away to pay rope out, or push back to reel it in (`Interaction.Tools.PullRope`). The free hand ticks once per 0.25 m of rope. The stick scrolls too. The length you leave is the length of the rope once it is tied.
- Rope spool, tying off: point the hand that holds the spool at a wall or the ground within 10 m, with the spot no more than 5 m from your body, then hold the trigger. The game shows a ghost anchor and fills the use progress over about 2 s. Release early to cancel. When it completes, the rope is anchored there, and you release the trigger before trying again.
- Rope spool, refused: when your aim meets no wall within 10 m, or the wall is more than 5 m from your body, the trigger gives the refused cue instead of the success tick. A red ghost of the anchor and a slashed ring mark the spot, a falling note plays there and the controller ticks three times. With `UI.ActionHintText` on, the reason also shows as text ("No wall in reach", then "Aim at a closer wall").
- Target names: the thing your hand or gaze would pick up or use glows, and its name stays hidden unless you turn on `UI.TargetNames`. A plain pickup gets no picture either. Hold rings, eat and feed, struggle and refusals still show theirs. Some items, such as the berries, differ only by colour, so in colour-blind mode every item you point at shows its name. `UI.ColorBlindNames` follows the game's colour-blind setting by default (`FollowGame`); set it to `On` or `Off` to decide yourself.
- Refused actions: anything the game refuses or limits gets the same cue, whatever your settings. The thing you tried turns red (the held item, a placement ghost, the stamina bar), a ring at the spot shows a shape for the cause, a soft falling note plays there and the controller gives three firm ticks. The shape tells you why: a slash means no, a bar means full and a sweeping arc means wait. A bolt is stamina, two inward chevrons mean both hands, an arrow says aim higher or lower, and a chevron onto a line asks for solid ground. A person points at friends, a padlock means locked or not here, an empty cup means used up or not lit, a weight is too heavy, wavy lines are spoiled food and "!" warns of danger. When the refusal is about another player, you see the ring only and their body keeps its colour. A refused emote wheel, pause, greyed-out control, locked cosmetic, end of a book and keyboard key all give the same cue. A control that greys out under a laser you hold still, like an arrow at its last step, reads as locked from the next press. A ring whose spot is out of sight (a hand overhead on a rope, something beside or behind you) also shows a copy at the edge of your view, about 1 m away, with a pointer toward the real spot. The real ring stays where it is. Notices (an item stowed or dropped, friends revived, a cannon about to fire) use a pin in a teal ring and a soft bell. Hold-to-use targets show a ring that fills while you hold. Target verbs show as button pictures; `UI.ActionHintText` brings the words back. `UI.ActionHints`, on by default, also shows where the action would work: a teal double ring, a chevron moving toward it, a rising note and the reach ray. Turning it off hides those hints; refusals still cue. The first time each kind of refusal happens, its short reason also shows as text beside the ring, so you can learn what the shape means. After that you get the shape alone (`UI.FirstTimeRefusalLabels`, on). The Reset button under that setting in the VR tab shows every reason's text once more. With `UI.ActionHintText` on, the text shows every time. With `Interaction.Haptics` off, the controller stops vibrating for everything, menu ticks included, while the rings and notes still play.
- Vibration strength: `Interaction.HapticStrength` sets the strength of every vibration, gameplay and menus alike. 1 is normal and the default, 2 is twice as strong and 0 is off. A config saved by an older version moves over on the first start: the old default 0.5 becomes 1, and any other value is doubled (up to 2), so it feels the same as before. Refusals and danger warnings are felt stronger than a menu hover tick at any strength setting. Each hand plays one vibration at a time, by priority: refusals and danger warnings first, then feedback on what the hand did (a grab, contact, progress, a use), then background feel. A vibration never cuts off one of higher priority; a grab or contact tick that comes during a refusal is skipped. Background feel (heartbeat, wind, surface texture, shiver, warmth) pauses while any other cue plays and for 0.1 s after.
- Binoculars: raise them with the trigger of the hand that holds them. By default (`Interaction.Binoculars.Activation = Toggle`) they stay up until you pull the trigger again, as in the game. With `Hold` they stay up only while you hold the trigger and lower when you let go. Twist the wrist to zoom (`Interaction.Binoculars.TwistZoom`). Twisting past the end of the zoom gives one hard thunk, then nothing more.
- Vine and chain shooters: when the game finds no clear path for the vine to where you aimed, nothing is built and the refusal cue marks that spot. The warp compass gives the friends cue at your hand when nobody is far enough away to warp to, and the game gives the use back.
- WMR controllers have no A/B buttons: click the top of the touchpad for B/Y and the bottom for A/X.
- Vive wands: the trackpad only acts when clicked, so a resting thumb never walks or turns. The off-hand pad walks toward your thumb while clicked (click at the rim to sprint). On the dominant pad, click left or right to turn, the top for B/Y, the bottom for A/X and the centre for the stick click. Hotbar and scroll flicks have no pad: use the belt slots and the wrist-twist gestures. The off hand has no B/Y or A/X then: ping with the finger gun, open the backpack over your shoulder and put items away on the belt.
- Talk: hold a free hand at your mouth like a walkie-talkie. A click and a mic icon by the hand show the mic is open (`Voice.HandToMouthTalk`, on). It only matters when the game's microphone mode is Push To Talk or Push To Mute. Cover your mouth with a flat open palm to mute (`Voice.PalmMute`, on). No button talks by default: every button already has a job. To use one, set `Voice.PushToTalkButton`; that control is then reserved for talking.
- Ping: make a finger gun, or rest a thumb on B/Y. A dot and a dotted line show where the ping will land. A ping at nothing gets the refusal cue.
- Emotes by gesture (`Emotes.GestureEmotes`, on): wave a raised open hand to wave, hold both open hands overhead to cheer, or hold an empty fist still out in front of you, at chest height or above, with the thumb up for a thumbs-up. Gestures do nothing while you climb, hang on a rope or vine, carry a friend, or grip something that took the grip. They also do nothing while you are passed out or dead, in a menu, or with a wheel open. The thumbs-up also needs a controller that senses your thumb, so a plain fist never sends one.
- Friends: pinch a thorn or arrow, then pull your hand back to pluck it (`Social.PullToPluck`, on); without the setting the grip alone starts it. In a custom run with the helping hand set to kick, push both open palms at a friend to kick them (`Social.ShoveKick`, on). Gripping a friend never kicks. While a friend or condor carries you, or you stand on a crouched friend, your hands feel a light sway or pressure (`Social.CarriedSway`, `Social.PalJumpFeel`, both on).
- An open passport or guidebook sits above the hand that holds it. Turn pages by swiping your other hand across it. The passport's tabs and page buttons also take the laser, and the guidebook also turns with stick up and down.
- Menus (title, pause, settings, passport, end screen) sit on a dark rounded plate cut to the buttons and text they hold. It grows or shrinks when a page, tab or open list changes, and it never takes a click. `UI.MenuBackplate` (on) switches it off and `UI.MenuBackplateOpacity` (0.85) sets how dark it is. At 0.85, white text stays readable even against snow in sunlight. A menu whose buttons and text spread over more than 70° of your view, like the title, gets no plate, since one plate around all of it would be a full-view screen. In the VR tab, dropdown text is black or white, whichever is easier to read on its box.
- Menu panels at a kiosk (boarding pass, custom run options) and the end-of-run results take the laser like any other menu. At the end of a won run, any controller button skips the credits wait, the same as any key on a keyboard.

## One-handed play

Set `Accessibility.OneHanded` to `Left` or `Right` to play with that controller alone. The other controller is ignored, even if it is still switched on, and the layout below replaces the two-controller one. `Input.DominantHand` stops mattering while it is on.

| Control | The one controller |
|---|---|
| Stick | Up/down: walk forward or back (toward your head or your hand, per `Locomotion.MoveRelativeTo`). Left/right: turn. A push counts as whichever is larger, so it never walks and turns at once |
| Stick click | Sprint. Keep it pressed for half a second to crouch as well |
| A / X | Jump |
| B / Y | Tap: pause. Hold: emote wheel |
| Grip | Grab or interact. Open it to drop, swing and open it to throw |
| Trigger | Use. With the stick clicked in as you pull it: the secondary use (use on a friend, reach out with an empty hand) |
| Menu | Release within 1 s: pause. Hold 1 s: recenter. Controllers without one (the Touch right controller) pause on B |

What moves off the buttons:

- Hotbar: draw from and holster on the belt slots. Stick up and down walks now, so it no longer cycles the hotbar or scrolls a held item. Twist your wrist instead: that pays out rope and tilts the scout cannon (`Interaction.Tools.TwistScroll`) and zooms the binoculars (`Interaction.Binoculars.TwistZoom`).
- Put away: holster the item on the belt. Backpack: reach over a shoulder and grip to open its wheel, or hold that grip still in the wheel's centre for a second to take the pack off.
- Ping: make a finger gun with the empty hand.
- Crouch: crouch for real (`Locomotion.PhysicalCrouch`), or keep the stick clicked in.
- Drop: open the grip (`Interaction.ReleaseToDrop`). With `ReleaseToDrop` off, `Interaction.DropButton` drops as usual and takes B over from pause and the emote wheel.

Everything that needs both hands takes one:

- A respawn chest opens with one hand lifting its lid.
- A two-handed item (the antizooka, rescue hook) works in one hand.
- Cheer: hold one open hand still over your head for about a second. A hand that swings is a wave.
- The kick shove: push one open palm at a friend.
- Soul pillar: lay one palm on it.
- Lifting a passed-out friend takes one grip (as with `Social.CarryWithBothHands` off).
- The wrist display moves to the one controller's wrist.

## VR Guide and practice

In VR the VR tab of the settings (title or pause menu) is laid out for the headset. The settings of the chosen category stand in one column about 30° wide; push the stick up or down while pointing at it, or drag it with the laser, to scroll. The categories are a small panel of their own to the left of the column, turned toward you, and each panel has its own plate. Every vibration setting is in the Haptics category, under a heading for the part of the game it belongs to, and the item and fire gestures have their own Items category. The config file keeps every setting where it was. Text is at least 1.3° tall wherever the menu stands. The game's own tabs keep the game's layout.

In VR the VR tab of the settings (title or pause menu) starts with a VR Guide section. It covers only what PeakXR adds or changes in VR; PEAK's own rules and items are left to the game.

- Demos: press Show to list one row per VR mechanic: grab and drop, two-hand hold, throw, eat, belt holster, backpack, wall climb, rope and vine, piton, cook, light a fire, open a suitcase, bugle, bandage, parasol, turning, walk and sprint, crouch, emote wheel, ping, talk, mute, spectate, pause and recenter, menus and laser, wrist status and refused actions. Rest the laser on a row or click it and a small ghost of you acts it out beside the panel for two loops. Its controllers light the button it presses, the rings and notes are the same cues the game uses in play (a double ring and a rising note where an action works, a slashed ring and a falling note where it is refused), and the matching real controller ticks. With `UI.ActionHintText` on, a one-line caption sits under the ghost. The list also has rows for the altar socket, release to drop, refusals out of view and their first label, rope pumping, the respawn chest, ground pickups, helping up, carrying, feeding, shaking off a web, hammering a piton, glider steering and giving up.
- Practice: press Start. The tab first shows only the comfort choices (turn mode, vignette strength, seated mode) with their usual previews. Press Start again to go: from the title it plays solo offline and walks you through stations in the airport. In the solo airport it starts where you are. Each station shows its demo ahead of you and marks where to act with a double ring: turn, grab an apple, holster it on your belt, eat, throw, climb the nearest wall, open the emote wheel. When you do it, a rising note plays, both controllers pulse and a ring widens. Practice ends with two drills: point at an apple out of reach to feel a refusal, then hold Menu to recenter. Two orbs float at chest height on your off-hand side: rest a hand in the chevron orb to skip a station, or in the cross orb to leave. While it runs, the Practice row in the VR tab reads Stop. Finishing, leaving and stopping all end back at the title. Boarding the plane ends the practice and keeps the run you started.
- The practice never runs online. In an online lobby, or in a solo run in progress, Start plays the refused cue at the row (the "Not here, start it from the title" label with `UI.ActionHintText` on) and does nothing.
- The first time you reach the title in VR, a small offer appears once beside the title panel: a ghost reaching for an apple and two orbs within reach. Rest a hand in the chevron orb to open the comfort-first practice view, or in the cross orb to dismiss it. It also goes away when you pick anything in the menu, and the Practice row stays in the guide for later.

## Items and fires by hand

Each of these has an on/off setting in the `Interaction` section. With a setting off, the grip and trigger work as before.

- Cook (`CookOverFire`): hold food over a lit campfire. It cooks while it stays above the flames and stops when you pull it out. A soft buzz grows as it cooks. Held near a lit fire, food gives a faint pulse that gets stronger toward the heat. Food that cannot be used on the fire, or a fire that is not lit, gets the refusal cue.
- Open a suitcase (`LiftLidToOpen`): grip the top of the case and lift. The game's open timer only runs as far as you have raised the lid, so lift slowly. A respawn chest needs both hands on the lid; one hand alone gets the two-hands cue. Gripping the case low down holds it open the old way, and shows an upward ring at the lid edge to say where to lift.
- Bugle (`BugleAtMouth`): raise it to your mouth to play. Tilt the bell up for a higher note and down for a lower one. The trigger plays it too.
- Light things (`LightWithFlame`): touch a lit torch, flare, candle or lantern to a cannon or rocket fuse, a ghost fire or an unlit campfire and keep it there until it catches.
- Food and drinks: bring them to your mouth. Pulling them away before the use finishes cancels it. The trigger alone doesn't eat or drink: it gets the refusal cue and a ring at your mouth. For seated or one-handed play, turn on `TriggerEats` and the trigger eats and drinks too.
- Bandages, heat packs, chalk and other healing items (`PressToBody`): press the item to your chest or your other forearm to use it. Press it to a friend's body to use it on them.
- Book of Bones (`ReadAtFace`, on): hold it open in front of your eyes to read it. Lowering it stops.
- Cursed items (`CommuneAtFace`, on): they only work held eye to eye for the whole use. The trigger alone is refused with a warning.
- Revive with the Book of Bones (`BookOverBones`, on): hold the open book just above the skeleton's hips and keep it there. Gripping the skeleton still works.
- Summit altar (`AmuletInSocket`, on): bring an amulet to its socket. The Peak altar's sockets sit out of reach, so hold the amulet up over your head under the socket, or point at the statue and close the grip. Pointing at it with an amulet shows the socket it will fill, and a pointed grip without an amulet is refused. `GuardWastedItems` (on) refuses two actions the game would waste: an amulet into a full altar, and a scout effigy when nobody is down.
- Soul pillar (`PalmsOnPillar`, on): lay both open palms on it.
- Summit portal (`HandIntoPortal`, on): push a hand into it to enter, and pull it out to stop.
- A cannon about to fire, a checkpoint that saved you, a summoned helicopter and the last ten seconds of the summit countdown are felt as ticks.
- Warm your hands (`WarmHandsAtFire`): hold open palms toward a lit campfire to feel a slow warm buzz. When you are cold and near a lit campfire, both controllers shiver faintly. This changes nothing in the game.
- Parasol (`ParasolRunner`): grip the shaft with your free hand and push up to open it, or pull down to close it.

## Turning and comfort

Settings live in the `Comfort` section. `Preset` sets several of them at once:

| Preset | Turning | Vignette | Move smoothing |
|---|---|---|---|
| Comfortable | Snap 30° | Strong | Off (instant) |
| Moderate (default) | Snap 30°; smooth turn, if chosen, at 60°/s | Medium | Off (instant) |
| Intense | Smooth 150°/s | Off | Off |

Changing `TurnMode`, `SnapTurnAngle`, `SmoothTurnSpeed`, `Vignette`, `VignetteStrength` or `MoveSmoothing` yourself switches the preset to Custom. The vignette darkens the edge of your view while your character moves, falls or smooth-turns without you moving, and while the view follows a body you don't control (spectating, passed out, carried). It starts closing at `VignetteStartSpeed` (0.2 m/s, so any artificial motion shows it) and reaches `VignetteStrength` at `VignetteFullSpeed` (2.5 m/s, about walking speed). `MoveSmoothing` sets how many seconds the move stick takes to reach full speed. At 0 it is instant, which is the most comfortable: a ramp stretches out the acceleration that causes motion sickness. The detached view (spectating, passed out, a tumble or launch) never glides fast: it speeds up and slows down gently and stops where the body stops. A body that gets away is caught up behind a fade. Choosing Comfortable also sets `Camera.ThirdPersonFollow` to 0, so the detached view stays still and jumps to a new spot behind a fade. Moderate and Intense set it back to 0.8. A preset changes it only while it still holds the previous preset's value, so a value you set yourself stays, and changing it never switches the preset to Custom. A config that still holds a preset's old values moves to the new ones, and values you changed yourself stay. `SeatedMode` treats your seated head height as standing height.

## Moving

Settings live in the `Locomotion` section.

- `MoveRelativeTo`: the move stick walks where you look (`Head`) or where the move hand points (`Hand`).
- `SprintMode` and `CrouchMode`: `Toggle` (click once) or `Hold`.
- `PhysicalCrouch` (on): crouch for real to crouch in game. It triggers when your head drops by `CrouchDepth` (0.2, a fifth of the standing height measured at your last recenter; 0.1 to 0.45) and lets go once you rise back above 70 % of that drop. At the default, stooping to the ground crouches the character, so its hands reach where yours are, while a nod or a glance at the floor does not. Your view stays at your real head. Seated mode never triggers it, and the crouch click keeps working.
- `ArmSwingSprint` (on): swing your arms like running while the move stick walks forward to sprint. The stick still decides where you go and how fast you walk; swinging alone never moves you. Only a swing below the chest counts, and gestures made standing still never sprint. It stays off while you climb or hold an item with both hands.
- `HopToJump` (off): swing both arms up and hop to jump. Standing up from a crouch, looking down and running arms do not jump.
- Walking around your play space moves your character with you. Your view stays where your head is and the character steps after it once you lean further than `LeanLimit` (0.35 m); a smaller lean bends the character's upper body instead. After standing back up from a lean past the limit, the character stays where it stepped and only straightens up. `LeanLimit` replaced `LeanDistance` (0.2 m), which walked the character on every lean; an old `LeanDistance` line left in the config file is ignored and can be deleted. If the character cannot follow, for example against a wall or while climbing, the view fades out and back in centred on the character once you get more than 1 m away from it, or once its steps after you stall (less than 3 cm closer in 1.5 s). Before that fade, a "not now" ring at the character's feet shows where to come back to. Turn `RoomScaleWalking` off to skip the stepping and keep only the fade.

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

`WindPressure` (on) adds light taps on the hand facing wind or a tornado's pull; they stop behind shelter. Other body feel is on top of that: a heartbeat while you spend stamina low on the bar, a shiver as you get colder, and feels for a lava sting, a bounce, a balloon, a parachute, a sugar crash and a bridge. Amulet charge shows as beads on the wrist. A head within 30 cm of rock gives a soft tick in the hand on that side. The heartbeat, the shiver and the rock tick each have their own switch in the `Locomotion` section (`Heartbeat`, `Shiver`, `WallTouch`, all on).

`CutsceneView` picks how the end cutscene looks. `Viewpoint` (the default) puts you at the cutscene camera, facing where it looks, and you can look around. When the shot cuts or moves far, the view fades to the new spot. `FollowCamera` glides after the camera instead. That is motion you did not make, so you have to turn it on. `Screen` plays the cutscene on a cinema screen. `Mirrors` off hides mirror reflections, which in VR are drawn from a point between your eyes and look flat.
