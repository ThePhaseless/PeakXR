# Item grab shapes

`item-grab-shapes.csv` compares each item's colliders with its drawn meshes, for all 194 entries of the game's `ItemDatabase` (`resources.assets`, read with UnityPy, read-only). Everything is in the item root's frame with the root's scale, in metres, for an item lying on the ground:

- Preview hands under `Hand_R`/`Hand_L` are left out, because `HandVisual.Awake` deactivates them (HandVisual.cs:9).
- A backpack's worn mesh and its collider are left out, because `Backpack.Update` hides them on the ground (Backpack.cs:41-42).
- `Item.colliders` holds only colliders on active objects (Item.cs:530), and `Item.SetState` makes every one of them solid and enabled (Item.cs:717-761). A prefab trigger therefore counts as solid, and a collider on an inactive child is ignored.
- Skinned meshes are posed through their bones. Their stored bounds run up to twice the posed mesh (the frog's are 2 m tall).

Columns:

| Column | Meaning |
| --- | --- |
| `vis_lo`, `vis_hi`, `col_lo`, `col_hi` | Bounds of the drawn meshes and of the active colliders |
| `size_ratio` | Collider size over drawn size, per axis |
| `coverage` | Share of the drawn box inside the collider box |
| `centre_offset` | Distance between the two box centres |
| `gap_p95`, `gap_max` | How far drawn vertices lie outside every collider: a hand on them may not reach a collider |
| `overhang` | How far collider extremes lie outside the drawn box: a grab from empty air |
| `aabb_gap`, `aabb_over` | The same two measures from boxes only, as the runtime sees them |
| `mode` | What `ItemGrabShape` does: `collider` (nothing), `extend` (the parts in `extras` add reach, by up to `extra_reach`), `+clip` (the reach is limited to what is drawn) |
| `flags` | `SMALL` (gap over 0.10), `LARGE` (overhang over 0.06), `OFFSET` (centres over 0.08 apart), `INACTIVE_CHILD_COLLIDER` |

No item has a concave mesh collider, a trigger-only collider set or no collider at all. Items with a skinned mesh posed by many bones only extend reach to the bone span plus 5 cm, so the Mandrake's leaves stay out of reach. Snowballs (`ItemScaleSyncer`) always use their colliders.

## Left-hand holds

`item-left-holds.csv` covers the 193 items with a `Hand_R` grip. It measures how the left hand holds each item when the hold starts in the left hand. The game only authors a right-hand hold: `Hand_R` for the holding hand and `Hand_L` for the second. The left hold was the right hold mirrored across the item's own x = 0 (`GripMath.MirrorX`), with the second grip at `Hand_L` with x negated. A rigid item can't be mirrored, so on an item that isn't symmetric about its x = 0 plane, the left hand lands where the item has nothing.

The data comes from the drawn meshes, triangles included, read with UnityPy (read-only) like the table above. The hands are the game's own preview hands under each grip, carried into the left hold. All distances are in metres.

| Column | Meaning |
| --- | --- |
| `mirror_asym_p95` | 95th percentile distance from the item's mirrored vertices back to its surface: 0 for an item symmetric about x = 0 |
| `x_centre` | The drawn box's centre in x |
| `plane` | The plane the new left hold mirrors across (see below) |
| `gap_right`, `gap_left_plain`, `gap_left_fixed` | Nearest preview-hand vertex to the drawn surface: right hold, old plain mirror, new left hold |
| `second_*` | The same for the second hand at `Hand_L` |
| `shift`, `second_shift` | How far the new left hold moves each grip back toward the item |
| `off_plane_features` | Named functional transforms (particles, muzzles) off the x = 0 plane. Each appears `2·|x|` from where a true mirror image would put it |

Findings:

- 76 items aren't symmetric about x = 0 (`mirror_asym_p95` over 3 cm), but most of them still meet the hand. With the plain mirror, 11 items left the holding or second hand more than 2 cm farther off the model than the right hold does:

  | Item | Extra gap |
  | --- | --- |
  | Flag_Plantable_Checkpoint | 0.14 |
  | Shell Big | 0.13 |
  | RitualDagger, second hand | 0.12 |
  | CactusBall, second hand | 0.09 |
  | FireWood | 0.08 |
  | Glizzy_CattailVariant | 0.07 |
  | Glizzy | 0.06 |
  | Amulet_Healing, second hand | 0.04 |
  | Marshmallow | 0.03 |
  | Parasol and Parasol_Roots Variant | 0.02 |

- The flag's pole lies diagonally in the item's xy plane, so the plain mirror also turned the pole about 75 degrees across the left fist.
- The functional transforms off the mirror plane are small: the Bugle's bell particles 0.10, the Pepper Berry's 0.08, the AntiZooka's 0.06 and the Rocketpack's worn exhausts. None is a barrel. Every launcher's muzzle is on its x = 0 plane, as `HandAim.TryBarrel` already noted.
- EarlyWorm is left out: its ground mesh sits 0.3 m from both of the game's own grips.

The rule (Core `LeftHold`, cached per item id by `ItemHoldInfo`):

1. A plane is ruled out if it puts the item's far end on the other side of the grip, unless the item is even across that plane. The far end is the end of the longest side away from the grip. Ruling it out keeps a flag's pole pointing past the fist, not back along the arm.
2. Keep the plain mirror while the left palm (the preview hand's centre) stays within 1 cm of the right palm's distance to the item, and the plain mirror isn't ruled out.
3. Otherwise mirror across the plane the item is most nearly symmetric about: x = 0, x through the drawn centre (an item drawn off its pivot sideways), or z through the drawn centre. The z plane is the item turned half round its up axis, for the flag, skewers, firewood, Shell Big and the Berrynana. Planes are tried in that order, within 2% of the longest side of the best. A launcher never takes the z plane, because that would turn its barrel back. If every plane is ruled out, the plain mirror stays.
4. Then shift each grip toward the item until its palm is as far from it as in the right hold.

Meshes aren't readable at runtime: only 6 of the 346 meshes in `resources.assets` have Read/Write. So the runtime measures against the item's colliders with `Collider.ClosestPoint`, once they are solid in the hand (Item.cs:779), and the symmetry from 26 probes around the drawn box. Checked against the real meshes, this leaves one item off by more than 2 cm: RitualDagger's second grip, 2.9 cm, and the game's own `Hand_L` there is already 35 cm off the dagger. Plane counts: 166 Origin, 7 CentreX, 19 CentreZ. 62 items get a shift of up to 0.18 m.

The probe symmetry and the mesh symmetry can disagree near the 2% margin. In the headset the colliders read the checkpoint flag as nearly as even across x as across z, so the runtime took the x plane, and the pole pointed back past the fist in the left hand. The far-end rule closes that case, whichever plane the probes prefer.

Self-test `vr.244-left-hold` checks this in the game: it holds the flagged items and three symmetric controls in each hand, judges every item and fails at the end with all those that failed.
