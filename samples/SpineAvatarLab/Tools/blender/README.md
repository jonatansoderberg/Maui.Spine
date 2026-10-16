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
| `kairo_hair_cards.py` | `build_cards` (ribbons along the guides, UV strips), `add_filler_guides`, `add_fringe_guides` + `grow_fringe_children` (the fringe layer), `build_cap_zones` (the scalp cap), `dump_cards`, `card_material_lab`/`cap_material_lab` (the package look), `export_cards_glb` (GLB, alphaMode MASK) |
| `rasterize_cards.py DATA.npz OUT.png ...` | outside Blender (numpy + Pillow): the strand atlas, one strip per card from the strands in its slab |
| `rasterize_cap.py DATA.npz OUT.png` | the cap texture from the undercoat strands in a spherical UV |

The emitter `ScalpHair` is the skull under the hair by the owner's painted roots in the head texture (see `ScalpZones`).
Candidate 8 (2026-10-16): 256 cards, atlas 2048 x 4096 (r13), cap texture r12, GLB `kairo/blender/kairo-hair-cards.glb`;
the package is built with `KAIRO_HAIR=cards KAIRO_CARDS_GLB=blender/kairo-hair-cards.glb python Tools/kairo_scan.py
~/.cache/spine-avatar-lab/kairo <out>` and checked in the lab with `Tools/lab/lab-cards.sh`. The session notes are in
`issues/542-kairo-hair-root-cap.md` (2026-10-16 entries).
