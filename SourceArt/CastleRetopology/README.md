# Clean mobile castle kit

Ten active architectural models have been structurally rebuilt in Blender from
regular, connected quads. This is not another voxel-remesh/decimate pass. Small
carvings, wood grain and shoji lattice detail use a shared trim atlas; posts,
beams, panels, floors, railings, lanterns, treads and landings remain actual 3D
geometry. Original FBXs under `Assets/Models` have not been changed. Characters
are outside this architectural pass.

The kit is authored in `SourceArt/CastleRetopology.blend`. Models overlap at their
local origin by design for export; only the square room is initially visible.
Unhide the named mesh to inspect another module. The source meshes are all
quads with zero boundary edges; game FBXs are triangulated by Unity as usual.
Intersecting separate structural solids are intentional, not a single Boolean
manifold building. Do not use automatic voxel remeshing on this library.

## Geometry budgets

Triangle counts are compared with the runtime assets used immediately before
this pass, not the much larger original source FBXs.

| Model | Before | New near | New medium |
| --- | ---: | ---: | ---: |
| CorrectedRoom | 6,000 | 2,560 | 1,012 |
| HallWayWithTurnFixed | 5,999 | 3,664 | 1,168 |
| HallWay3Way | 6,000 | 4,768 | 1,600 |
| DoubleStaresBase | 6,000 | 2,172 | 1,068 |
| SingleStareBase | 6,000 | 1,388 | 704 |
| Path | 6,000 | 584 | 296 |
| Stares | 6,000 | 744 | 312 |
| PackFlatStair | 852 | 500 | — |
| PackSwitchbackStair / stair_2 | 1,992 | 1,708 | — |
| PackStairFlight | 792 | 744 | — |

The ten-model near set falls from 45,635 to 18,832 triangles, a 58.7% reduction.
The seven medium meshes total 6,160 instead of about 17,500 triangles. The far
room is 712 triangles instead of 900. The full U-shaped stair_2 is retained as a
single two-flight route, not assembled from unrelated stair sections.

## Textures and runtime integration

`Assets/InfinityCastle/CastleModules/Retopology/CastleTrimAlbedo.png` is the shared
atlas generated from the supplied references using built-in image generation.
The prompt/layout is in `texture-prompt.md`. UVs use inset atlas cells rather
than stretching an old texture over a new topology. Android import is 1024px,
ASTC 6x6 with mipmaps. Lantern-only emission adds no realtime lights or extra
texture lookup. The existing timber-construction shader and instancing remain.

`CastleModules.json` is generated with the geometry. It stores bounds, floor
and roof heights and actual open doorway positions. Unity's importer applies
these to the prefabs instead of reusing the old scanned model dimensions. Exact
mesh floors replace rectangular repair colliders, especially in L/T corners.
Gravity fields for the rebuilt kit also ray-test those actual surfaces, rather
than treating the empty corner of a concave room as an attracting floor.
The pack-stair bake prefers the new UV-mapped stair FBXs; it cannot silently
restore the old brown procedural material. Closed doors use the same shoji atlas.

Pooling, castle density, learned-model inference, streaming and gravity settings
are not reduced to achieve the geometry savings. This pass is not a measured
FPS guarantee: Vivo Y400 performance still requires a device build and profiling.

## Rebuild and checks

Run Blender with `--background --python Tools/rebuild_castle_topology.py`.
Add `--skip-preview` to skip the ten review renders on subsequent exports.
Then run `Tools/RunArchitectureValidation.ps1 -Mode Retopology` to import the kit,
rebuild construction metadata and validate real Unity openings/collisions/UVs.
The existing `Volume`, `Assembly`, `Streaming` and `Player` checks exercise the
integrated runtime. Test results are written under `Temp/`.

Pre-pass derivatives and authoring library were copied into
`SourceArt/BeforeRetopology-20261023/`. They can be restored without touching the
original source models.
