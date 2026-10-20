# VR controls

The default layout for a right-handed player. With `Input.DominantHand = Left` the two controllers swap roles.

| Control | Left controller | Right controller |
|---|---|---|
| Stick | Move | Left/right: turn. Up/down: next or previous item |
| Stick click | Sprint | Crouch |
| A / X (lower button) | Tap: put the item away or take it out again. Hold: take out the backpack | Jump |
| B / Y (upper button) | Tap: ping. Hold: emote wheel | Drop. Hold to charge a throw |
| Grip | Grab or interact | Grab or interact |
| Trigger | Use (secondary) | Use (primary) |
| Menu | Release within 1 s: pause. Hold 1 s: recenter | Same as left |

Notes:

- Use follows the hand that holds the item: its trigger is the primary use and the other trigger is the secondary use. With empty hands the right trigger climbs and the left trigger reaches out to help a friend, like the game's mouse buttons.
- Grip, trigger and the hand throw come from the physical interaction settings (`Interaction` section). There, `DropButton` picks the drop button. The default, `DominantSecondary`, is B on your dominant hand, so it moves to the left controller with `Input.DominantHand = Left`. Names starting with `Left` or `Right` stay on that controller whichever hand is dominant.
- Menu: one threshold, `Camera.RecenterHoldSeconds` (1 s by default), splits the two uses. Let go before it to pause; keep holding to recenter. A small ring below the centre of your view fills while you hold, so you can see which one you will get.
- When you hold something with a scroll function (rope spool, binoculars, guidebook, scout cannon), stick up and down scroll it. Put the item away to change items.
- Vive wands and WMR controllers have no A/B buttons: click the top of the trackpad for B/Y and the bottom for A/X. On a Vive wand the centre of the pad is the stick click.

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
