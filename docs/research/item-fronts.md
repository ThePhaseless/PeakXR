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
4. **Flatness.** Books, the passport, the guidebook pages and the packaged food are thin along the presented axis, so one of their two broad faces is the cover.
5. **Function.** Items the mod already orients by their use (`HandAim.TryBarrel` launchers, the bugle at the mouth, binoculars at the eyes, the parasol and glider canopy) are `fixed` whatever their look.

Lanterns were rendered too: the glass runs all round both gas lamps, and they hang from their bail, so they are `fixed`.

## The VR hold

`AvatarPoseDriver.ItemInController` places the item as the game's right hold does, mirrored for the left hand (Core `LeftHold`). For an item with a front, Core `ItemFront.Turn` then turns it about the line from the grip through the item's drawn middle, by the angle that brings the front nearest the eyes. That turn keeps both the grip and the item's body where the hand has them, so the palm stays on the grip and the item stays in the palm. An item whose middle is within 2 cm of the grip turns straight to the eyes.

The eyes are one fixed direction in the controller's grip frame (`ItemFront.EyeInGrip`): back along the forearm, a little above the fist and toward the body's middle, as when a hand holds something up before the face to look at it. The turn depends only on the item and the hand, so:

- it is the same every frame, and the item does not swim as the wrist turns;
- peers running the mod draw the same hold from the same table; vanilla peers see the game's own hold.

Gesture points (`HeldItemPoints`), the drawn scale's pivot (`HeldItemScale`), the second grip (read from the item as it lies) and the two-hand holds all start from `ItemInController`, so they follow the turn.

Offline, with the scout's hand bones and the game's grips, the presented side already faces the eyes in the authored hold for every `front` item (a dot of 0.65 to 0.99 with the eye direction). The turn raises the compasses from 0.65-0.69 to 0.94-0.99, BingBong from 0.83 to 0.93 and the guidebooks from 0.86 to 0.92. Self-test `vr.245-item-front` checks the front against the eyes in the game.

## BingBong

BingBong's face (the eyes and the separate lip meshes) is on its `-Z` side, the side the game presents. The last `vr.244` run measured its `+Z` pointing away from the eyes in both hands (`+Z to the eyes -0.99`), so the face was already toward the player at the scripted hand pose. That run's screenshots stood the camera 0.45 m from the middle of an item drawn over a metre wide, so they showed only its cloth. The `vr.244` and `vr.245` shots now stand back far enough to frame the whole item.

## Left holds and the front

The left hold's rule 2 (no half turn for an item with a front, `item-grab-shapes.md`) also takes the fronts from this table, so a front that is only in the texture, such as the passport's or the guidebook's, is kept too.
