# Item fronts

`item-fronts.csv` lists, for the 193 items in the game's `ItemDatabase` with a `Hand_R` grip, the side a person turns toward their eyes to look at the item: a face, a dial, a cover or a label. Each row has a `kind`, the front as a unit vector in the item's own frame (for `front` rows) and the evidence. Core `ItemFront` holds the `front` rows as a table, generated from this file.

| Kind | Items | Meaning |
| --- | --- | --- |
| `front` | 50 | The item has a side worth showing. The VR hold turns it toward the eyes. |
| `fixed` | 30 | The item's use fixes how it is held: launchers and the blowgun (barrel along the aim), bugles and the megaphone (mouthpiece to the mouth), binoculars, torch, flare and candle (flame up), lanterns (hang from the bail, glass all round), parasols and the glider (canopy up), the checkpoint flag, the piton, the rope spools, the stove, the dagger and the dynamite. |
| `none` | 113 | No side differs enough to matter: fruit, berries, mushrooms, eggs, balls, stones and gems, chess figures, sticks, the creatures (skinned, posed at runtime). |

Items in neither the table nor the game today, such as those a future version adds, keep the authored hold.

## How the fronts were found

All data was read from `resources.assets` with UnityPy, read-only.

1. **The side the game shows.** The game poses a held item before the camera: it sits at `Item.defaultPos` in the look frame and turns so its +Z runs along `defaultForward` (CharacterRagdoll.cs:230-245). The direction from the item to the camera, in the item's frame, is the side the game presents. It is `-Z` for most items, tilted for those whose `defaultPos` is above or below the eye line (the compasses, the cookies, `Amulet_Clone`).
2. **Textured renders.** Every item's drawn meshes were rendered with their base textures (`_BaseTexture` in the game's `W/Peak_*` shaders), once from the presented side and once from the opposite side. The front is the side with the face, dial, cover art or label: BingBong's eyes and lips, the idol's and the cursed skull's gem eyes, each medallion's inlay, each compass's dial, the passport's cover art, the milk carton's picture, the antidote's label, the bandage roll's clasps. On every item with a front, that side is the one the game presents.
3. **Named parts.** Child transforms named like faces and dials, measured from the drawn middle along the presented side: `Lip Top` and `Lip Bottom` on BingBong, `Needle` on each compass (+0.36 to +0.76), `Eyes` on the cursed skull (+0.92).
4. **Flatness.** Books, the passport, the guidebook pages and the packaged food are thin along the presented axis, so one of their two broad faces is the cover. For these flat fronts (the compasses, the amulets, the books and pages, the passport and the packets) the table holds the face's own normal: the least-spread axis of the drawn vertices, signed toward the presented side. The game shows such faces tilted: the compass dial 19 degrees up from the camera line, the warp compass and the amulets 45 to 52. The compass's normal matches the axis its needle turns about (`CompassPointer` projects the heading onto the plane across its `forward`, CompassPointer.cs:53-55). Other fronts keep the presented side.
5. **Function.** Items the mod already orients by their use (`HandAim.TryBarrel` launchers, the bugle at the mouth, binoculars at the eyes, the parasol and glider canopy) are `fixed` whatever their look.

Lanterns were rendered too: the glass runs all round both gas lamps, and they hang from their bail, so they are `fixed`.

## The VR hold

`AvatarPoseDriver.ItemInController` places the item as the game's right hold does, mirrored for the left hand (Core `LeftHold`). For an item with a front, Core `ItemFront.Turn` then turns it so the front faces the eyes as seen from the item's middle (where it is drawn, `HeldItemScale`). It first spins the item about the line from the grip through its middle, which keeps both the grip and the item's body where the hand has them. A dial held by its rim faces along that line or across the arm, which no spin can fix, so a tilt about the grip follows, at most 60 degrees in all (`ItemFront.MaxTilt`). Spin and tilt repeat three times, since each tilt moves the middle. The hand stays on the grip throughout. An item whose middle is within 2 cm of the grip turns straight to the eyes.

The eyes are one fixed point in the controller's grip frame (`ItemFront.EyePoint`), 0.45 m along `ItemFront.EyeInGrip`: back along the forearm, a little above the fist and toward the body's middle, as when a hand holds something up before the face to look at it. The turn depends only on the item and the hand, so:

- it is the same every frame, and the item does not swim as the wrist turns;
- peers running the mod draw the same hold from the same table; vanilla peers see the game's own hold.

Gesture points (`HeldItemPoints`), the drawn scale's pivot (`HeldItemScale`), the second grip (read from the item as it lies) and the two-hand holds all start from `ItemInController`, so they follow the turn.

Self-test `vr.245-item-front` checks the front against the eyes in the game, in the natural hold (`ItemFront.NaturalGrip`).

### Run 15: the compass at 0.69

The first version spun the item only, toward the eye direction from the fist, and took the presented side as every item's front. Run 15 measured the compass at 0.69 in both hands, the passport at 0.88/0.82 and the amulet at 0.88/0.87. Three causes, all reproduced offline:

1. **The test's hand was not the natural hold.** `vr.245` raised the hand with `MoveTrack`, whose `SetHand` aims the scripted fingers away from the head through the hand. That puts the eyes along the grip's +Y, about 40 degrees from `EyeInGrip`. With that pose the offline model gives BingBong 0.97/0.91, the passport 0.86 and the granola bar 0.97, against 0.97/0.94, 0.88/0.82 and 0.98 in the game. The step now holds the hand naturally: the fist 0.45 m before the eyes with `EyeInGrip` pointing at them.
2. **The front was the camera line, not the dial.** The game shows the compass dial tilted 19 degrees from the camera line and the amulets about 50, so aiming the presented side left the faces turned away by that much.
3. **A spin alone cannot show a dial held by its rim.** In the natural hold, with the true normal, the spin alone reaches only 0.60 for the compass and 0.56 to 0.73 for the amulet. The bounded tilt brings them to 0.99 and 1.00.

Expected `vr.245` values (offline, natural hold, drawn middle): BingBong 1.00/1.00, Compass 0.99/0.99, Passport 1.00/1.00, Granola Bar 1.00/1.00, Amulet_Healing 1.00/1.00 (left/right). Every `front` item reaches at least 0.97, except the Book of Bones (0.89/0.85), whose bulk the 60-degree budget cannot swing far enough. The tilt turns the grip-to-middle line by 60 degrees at most (compasses, amulets, books). It moves no item into the forearm: at most 4% of an item's drawn points fall inside a 6 cm forearm cylinder. The one exception is BingBong's prop variant, at 36% in the authored hold already.

## BingBong

BingBong's face (the eyes and the separate lip meshes) is on its `-Z` side, the side the game presents. The last `vr.244` run measured its `+Z` pointing away from the eyes in both hands (`+Z to the eyes -0.99`), so the face was already toward the player at the scripted hand pose. That run's screenshots stood the camera 0.45 m from the middle of an item drawn over a metre wide, so they showed only its cloth. The `vr.244` and `vr.245` shots now stand back far enough to frame the whole item.

## Left holds and the front

The left hold's rule 2 (no half turn for an item with a front, `item-grab-shapes.md`) also takes the fronts from this table, so a front that is only in the texture, such as the passport's or the guidebook's, is kept too.
