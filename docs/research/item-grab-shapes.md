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
