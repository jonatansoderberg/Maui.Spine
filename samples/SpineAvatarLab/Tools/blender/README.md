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
| `kairo_hair_cards.py` | `build_cards` (ribbons along the guides, UV strips), `add_filler_guides`, the flat layers `add_fringe_guides`/`add_parting_guides`/`add_sweep_guides`/`add_temple_guides` with their children (`grow_all_layers` grows the whole candidate in one call; pull `FLAT_PULL`, root spread `FLAT_SPREAD`), `build_cap_zones` + `build_cap_for_cards` (the scalp cap, kept behind the cards and dropped where a lab view sees it past the cards' geometry or through a transparent card texel against skin: `cap_outside_cards`, `cap_exposed_by_render`, `drop_faces`), `dump_layers`, `card_material_lab`/`cap_material_lab` (the package look), `export_cards_glb` (GLB, alphaMode MASK; the generator applies each mesh node's transform), the measures `camera_cap_exposure` (geometry) and `exposure_with_alpha` (like the lab: rays + atlas alpha) |
| `rasterize_cards.py DATA.npz OUT.png ...` | outside Blender (numpy + Pillow): the strand atlas, one strip per card from the strands in its slab |
| `rasterize_cap.py DATA.npz OUT.png` | the cap texture from the undercoat strands in a spherical UV |

The emitter `ScalpHair` is the skull under the hair by the owner's painted roots in the head texture (see `ScalpZones`).
Candidate 26 (2026-10-16): 705 cards (`build_candidate(T22_PARAMS, npz, filler_min_dist=0.016, w_scale=3.0, max_w=0.11)`: t16 guides, filler every 16 mm, fringe, parting, sweep and temple layers, conformed sides), atlas 1792 x 10304 (56 x 448 px strips: `rasterize_cards.py --strip 56,448`, the archive's 32 MiB per-entry limit), cap texture r13, the cap behind the cards only, GLB `kairo/blender/kairo-hair-cards.glb`;
the package is built with `KAIRO_HAIR=cards KAIRO_CARDS_GLB=blender/kairo-hair-cards.glb python Tools/kairo_scan.py
~/.cache/spine-avatar-lab/kairo <out>` and checked in the lab with `Tools/lab/lab-cards.sh` (`YAWS="-40 -60"` for other views); the owner's marked screenshots are measured in their own view with `Tools/lab/viewmatch`-style IoU (kairo/tools/viewmatch.py) and `Tools/lab/markcrop.py` (the marked boxes mapped into the capture by the hair's bbox, dark-navy pixel counts before and after); `Tools/lab/edgecap.py` counts cap outside the hair in a flat-green-cap capture (`solo.py flat=cap:0,1,0`), `Tools/lab/skinpeek.py` the skin enclosed by hair in a box, and `Tools/lab/spikes.py` the silhouette's spikes (hair an 11 mm opening removes outside the head). The session notes are in
`issues/542-kairo-hair-root-cap.md` (2026-10-16 entries).
