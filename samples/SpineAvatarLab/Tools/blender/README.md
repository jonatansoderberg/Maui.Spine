# Kairo hair in Blender: curves, cards, atlas

The hair of the Kairo avatar (`KAIRO_HAIR=cards` in `../kairo_scan.py`) is made here, in Blender 5.2 LTS driven
through the `blender` MCP (or `~/.cache/spine-avatar-lab/kairo/tools/bmcp.py`), from the work file
`~/.cache/spine-avatar-lab/kairo/blender/kairo-hair-cards-wip.blend` (Head, HairEnvelope = the owner's approved hair
volume, LabCamera = the lab's camera, Root's Z rotation = the lab's yaw).

Inside Blender, `exec(open(path).read())` these in order; every step is deterministic (seeded, fixed order):

| File | What |
|---|---|
| `kairo_hair_lib.py` | `world_mesh`, `bvh_of`, `lab_render(path, yaw, show=[...])` through LabCamera, `PIV` (the head pivot) |
| `kairo_hair_trace.py` | `RadialSurface` (the envelope's outermost hit per direction on a 1 deg sphere grid, blurred = the wig surface), `HairField` (the comb field, `trace`, roots on `ScalpHair`), `build_hair_object` |
| `kairo_hair_style.py` | `StyledField..StyledField10`: S-waves, clumps to guides, ear pockets, gap cells, hem stall, undercoat. `StyledField10(params=T16_PARAMS).grow_styled(16000, seed=7)` is the approved curves layer t16 |
| `kairo_hair_cards.py` | `build_cards` (ribbons along the guides, UV strips), `add_filler_guides`, the flat layers `add_fringe_guides`/`add_parting_guides`/`add_sweep_guides` with `grow_fringe_children`/`grow_parting_children`/`grow_layer_children` (pull `FLAT_PULL`, root spread `FLAT_SPREAD`), `build_cap_zones` (the scalp cap), `dump_layers`, `card_material_lab`/`cap_material_lab` (the package look), `export_cards_glb` (GLB, alphaMode MASK; the generator applies each mesh node's transform), the measures `camera_cap_exposure` (geometry) and `exposure_with_alpha` (like the lab: rays + atlas alpha) |
| `rasterize_cards.py DATA.npz OUT.png ...` | outside Blender (numpy + Pillow): the strand atlas, one strip per card from the strands in its slab |
| `rasterize_cap.py DATA.npz OUT.png` | the cap texture from the undercoat strands in a spherical UV |

The emitter `ScalpHair` is the skull under the hair by the owner's painted roots in the head texture (see `ScalpZones`).
Candidate 14 (2026-10-16): 320 cards (t16 guides, filler, fringe, parting and sweep layers), atlas 2048 x 5120 (r16, slab 25 mm), cap texture r13, GLB `kairo/blender/kairo-hair-cards.glb`;
the package is built with `KAIRO_HAIR=cards KAIRO_CARDS_GLB=blender/kairo-hair-cards.glb python Tools/kairo_scan.py
~/.cache/spine-avatar-lab/kairo <out>` and checked in the lab with `Tools/lab/lab-cards.sh`. The session notes are in
`issues/542-kairo-hair-root-cap.md` (2026-10-16 entries).
