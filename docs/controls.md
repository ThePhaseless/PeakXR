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

- Use follows the hand that holds the item: its trigger is the primary use and the other trigger is the secondary use. With empty hands the right trigger climbs and the left trigger reaches out to help a friend, like the game's mouse buttons.
- Grip, trigger and the hand throw come from the physical interaction settings (`Interaction` section). There, `DropButton` picks the drop button. The default, `DominantSecondary`, is B on your dominant hand, so it moves to the left controller with `Input.DominantHand = Left`. Names starting with `Left` or `Right` stay on that controller whichever hand is dominant.
- The emote wheel opens at the hand that holds Y. Move that hand away from where the wheel opened, or push its stick, toward an emote and release Y to play it. Stick up and down on the right controller flips the page. The backpack wheel opens at the hand that grips the pack; move the hand toward a pocket and let go.
- Menu: `Camera.RecenterHoldSeconds` (1 s by default) sets both uses. Let go before it to pause; keep holding to recenter. A small ring below the centre of your view fills while you hold, so you can see which one you will get.
- While you are dead or passed out you watch a teammate from a detached view behind them (`Camera.TakeoverView`). Flick the turn stick up or down to switch to the next or previous teammate; the view fades between them.
- When you hold something with a scroll function (rope spool, binoculars, guidebook, scout cannon), stick up and down scroll it. Put the item away to change items.
- Vive wands and WMR controllers have no A/B buttons: click the top of the trackpad for B/Y and the bottom for A/X. On a Vive wand the centre of the pad is the stick click.
- Push to talk: hold the move-stick click for half a second and keep holding while you speak. The click itself still toggles sprint, which does nothing while you stand still. It only matters when the game's microphone mode is Push To Talk or Push To Mute. You can also hold a free hand at your mouth like a walkie-talkie; `Voice.HandToMouthTalk` turns that off.
- An open passport or guidebook sits above the hand that holds it. Turn pages by swiping your other hand across it. The passport's tabs and page buttons also take the laser, and the guidebook also turns with stick up and down.
- Menu panels at a kiosk (boarding pass, custom run options) and the end-of-run results take the laser like any other menu. At the end of a won run, any controller button skips the credits wait, the same as any key on a keyboard.

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
- Walking around your play space moves your character with you. Your view stays where your head is and the character steps after it once you lean further than `LeanDistance`. If the character cannot follow, for example against a wall or while climbing, and you get more than 1 m away from it, the view fades out and back in centred on the character. Turn `RoomScaleWalking` off to skip the stepping and keep only the fade.

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
