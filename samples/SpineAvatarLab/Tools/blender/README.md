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
Candidate 29 (2026-10-16): 705 cards (`build_candidate(T22_PARAMS, npz, filler_min_dist=0.016, w_scale=3.0, max_w=0.11)`: t16 guides, filler every 16 mm, fringe, parting, sweep and temple layers, conformed sides), atlas 1792 x 10304 (`rasterize_cards.py --strip 56,448 --c0 0.08,0.09,0.26 --c1 0.50,0.54,0.82 --gamma 2.0 --shadedepth 0.025 --shademin 0.3`; 56 x 448 px strips for the archive's 32 MiB per-entry limit, the ramp darker than the reference because the lab lifts it: lab lum = 0.89 x atlas + 19), cap texture r13, the cap behind the cards only, GLB `kairo/blender/kairo-hair-cards.glb`;
the package is built with `KAIRO_HAIR=cards KAIRO_CARDS_GLB=blender/kairo-hair-cards.glb python Tools/kairo_scan.py
~/.cache/spine-avatar-lab/kairo <out>` and checked in the lab with `Tools/lab/lab-cards.sh` (`YAWS="-40 -60"` for other views); the owner's marked screenshots are measured in their own view with `Tools/lab/viewmatch`-style IoU (kairo/tools/viewmatch.py) and `Tools/lab/markcrop.py` (the marked boxes mapped into the capture by the hair's bbox, dark-navy pixel counts before and after); `Tools/lab/edgecap.py` counts cap outside the hair in a flat-green-cap capture (`solo.py flat=cap:0,1,0`), `Tools/lab/skinpeek.py` the skin enclosed by hair in a box, `Tools/lab/spikes.py` the silhouette's spikes (hair an 11 mm opening removes outside the head), and `Tools/lab/haircolor.py` the hair's luminance percentiles, pale/dark shares, saturation and hue (run on the reference views in `kairo/tools/ref/` and on the lab captures alike). The session notes are in
`issues/542-kairo-hair-root-cap.md` (2026-10-16 entries).

## Tripo hair (2026-10-17, the approved tripo45)

The hair in `Resources/Raw/Avatars/kairo.spineavatar` is generated, not carded. In Blender, after the files above:

| File | What |
|---|---|
| `tripo_fit.py` | `fit_tripo` (a Tripo model from `generate_3d` fitted to `Head`), `register_to` + `apply_fit` (another view's model registered to the front one), `merge_hair3` (whole locks per azimuth sector into one mesh, 2x2 atlas), `export_tripo` (GLB in the cards' slot: `KairoCards` + `KairoCap`, the cap behind the hair, an under-shell joined in) |
| `ear_fix.py` | `fix_ears`: locks through the ears moved outside or behind the ear, small or over-bent ones dropped |
| `hairline_fix.py` | `fix_hairline`: the hair's straight lower edge at the forehead broken into tips on the scalp |
| `hair_chain.py` | the steps in order (`run_chain`), the atlas retone (`../lab/hair_retone.py`), Tripo's collar dropped, loose shards dropped |

tripo45: `merge_hair3([('TripoFull', (-85, 85)), ('TripoRightPngFit', (40, 140)), ('TripoBackFit', (95, -95)), ('TripoLeftPngFit', (-140, -40))], 'TripoMerged4')`,
then `fix_ears`, `fix_hairline`, the retone (`hair_retone.py` k 2.2/1.3), the collar dropped (`TripoMerged8`), five loose pieces at the right eye's corner removed (`TripoMerged8b`),
`export_tripo('TripoMerged8b', path, cap_mode='behind', under='TripoUnder', cap_image='HairCapTexDark')`; the package:
`KAIRO_HAIR=cards KAIRO_CARDS_GLB=blender/kairo-hair-tripo45.glb python Tools/kairo_scan.py ~/.cache/spine-avatar-lab/kairo Resources/Raw/Avatars`.
