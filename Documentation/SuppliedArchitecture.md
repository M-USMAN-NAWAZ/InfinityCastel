# Runtime Castle Architecture

Playground uses mobile derivatives of the supplied models in `Assets/Models/CastleModules`: CorrectedRoom,
HallWayWithTurnFixed, HallWay3Way, DoubleStaresBase, SingleStareBase, Path and Stares.
The building-traversal mode does not use Danish towers or an enclosing facade box.

## Layout

The central castle is a connected network of differently proportioned building
wings, assembled from native rooms, halls, roofs and stairs. Native Path geometry
and fitted collision connect verified doorways. The existing trained ML director
chooses layout and movement policy; geometric clearance remains deterministic.

Additional room-and-path clusters occupy interior cells throughout a 3D volume,
not six boundary planes. Clusters vary in story count, room dimensions, depth,
horizontal offset, facing and gravity orientation. Outer rooms have native collision
and gravity when approached. All three entrances have fitted walkways/staircase
routes, including connections between floors; they are not facade panels or a
skybox replacement.

The camera's clear mode, skybox material and scene fog are left unchanged. Warm
ambient lighting and two shadowless lights are configured independently.
The saved Playground fog is now a light warm brown (density 0.004), instead of
dense blue fog that hid distant buildings as blue silhouettes against the orange skybox.
Startup also repairs that exact old preset in an already-open scene; other custom
fog settings and the user's skybox are preserved.

## Ultra Jump and Gravity Transfers

Aim at a reachable rooftop and press **Q**, **Shift+Space**, or the gamepad's
north face button. Android has a separate **Ultra Jump** touch button. Regular
Space remains the ordinary jump. Targeted jumps reach roofs up to 85 metres away
when their flight path is clear. If no aimed landing is clear, an untargeted
forward jump uses an 18-metre arc and 26 m/s forward launch.

The starting building includes five separated native rooms: an inverted room
overhead and rooms facing inward from four sides. Their roof collision and
gravity fields are real. Approach corridors are kept clear during generation.
During a targeted jump, gravity changes halfway through flight; momentum remains
in world space, and the camera eases into the new orientation. Landing releases
the temporary flight override back to the building's ordinary gravity field.
There is no flight teleport, flight-only room instantiation or enclosing shell.

Gravity seams use the capsule's real centre, not an orientation-dependent guessed
height. After turning, the chosen direction remains committed until the capsule
moves 0.45 metres relative to that surface or leaves/disables its field. A small
same-direction distance margin also resolves overlapping floors. Rotating in place
cannot immediately activate the reverse corner, while walking back still can.

## Pooling and Frame Budget

The default Android-budget configuration prewarms 96 building-wing slots (including
four full-building layer reserves), 288 shared path connections, 96 outer-room physics bodies,
and 1,536 lightweight reusable cluster-data slots. No castle GameObjects or meshes are created
or destroyed by traversal, shifts or spatial streaming after warm-up. Far room
volumes use shared instanced LOD meshes; the complete room distance mesh has
900 triangles. Nearby collision is leased from the physics pool.

All seven playable meshes now use Blender-derived `RuntimeMobile` assets: 6,000
triangles near the player, 2,500 at medium distance, and the existing 900-triangle
complete-room mesh far away. The original supplied models have about 50,000
triangles each and are preserved, but are no longer cloned for the playable pool.
Blender voxel cleanup repairs topology that otherwise prevents collapse below
roughly 10,000 triangles. Fresh UVs and selected-to-active colour baking transfer
the original appearance to the reduced meshes (1024-pixel near atlases, 512-pixel
medium atlases, one material per mesh, mipmaps and Android ASTC 6x6 compression).
Native MeshColliders use the 6,000-triangle geometry, not the 50,000-triangle sources.
Reusing the original atlas after
collapsing mesh UV seams causes the large stretched roof/window triangles; the
new atlas/material must always travel with its matching LOD mesh. Original source
models and textures are not modified.

The seven playable near FBXs retain Read/Write data and import pre-cooked MeshColliders.
Their rotated/scaled runtime parents can require re-cooking in a built player;
the Editor's mesh access previously concealed failures when Read/Write was off.
A targeted model importer and build preflight preserve these flags on reimport.
Render-only distance meshes still have Read/Write disabled.

Startup and streaming run through the same incremental work queue, using
`layoutMillisecondsPerFrame` (1 ms in the saved prefab) as the streaming soft CPU
budget. Initial loading uses at least 8 ms per frame while controls are held;
using the same 1 ms limit for preload stretched it over thousands of frames.
Atomic Unity
operations can exceed that budget. The camera is never disabled. A loading overlay
holds controls until the initial surrounding volume and three full lower building
layers have been prepared. Completing warm-up does not teleport the player back
to spawn. Placement checks use cached
module bounds and free doorway anchors; rejected candidates do not move PhysX
assemblies. Room, path and physics pools are reused as the spatial window moves.

Bridges warm alongside their wings, retaining the same final GameObject pool size.
Residency covers a five-cell-radius sphere in every direction, plus a three-cell
lead volume up to 140 m along velocity/gravity prediction. Blocks are prepared in
lead-first order. Real architecture uses an opaque, single-pass distance fade from
160 to 192 m; unseen geometry is prepared beyond 200 m even if the camera looks
directly that way. This removes the old frustum veto that starved forward/downward
generation. Hidden old layers can be recycled above, below or sideways, with new
dimensions/orientations. There are no enclosing panels or fake castle surfaces.

The old one-room emergency catch was replaced by multi-room/corridor/stair wing
assemblies. Three are primed below the start; ongoing preparation keeps multiple
layers ahead of a descending player, not a room spawned just before impact.
Collider promotion runs independently every 0.08 s, including a velocity lead;
four room bodies and two crossings can be promoted per update. Slow doorway
matching is skipped at high travel speed so it cannot stall vertical residency.
Rooms and paths outside the visible band are CPU-culled as well as shader-hidden.
Leasing a district crossing's collider does not replace its instanced visual mesh;
walking closer cannot reveal a newly activated, differently detailed path object.

Inference-only play skips the Python-trainer connection attempt. Explicit training
agents retain their normal trainer workflow. The existing model has not been
retrained by these changes.

## Verification

`Tools/RunArchitectureValidation.ps1 -Mode Volume` runs isolated Unity physics
checks for native floors, jumps, a real deep fall, connections in all six gravity
frames, supported building motion, overlap clearance, outer doorway traversal,
3D distribution and fixed scene-object counts.

`-Mode Preview` runs the saved Playground in Play mode, verifies skybox/fog
preservation, measures generation work slices, and checks identical pooled object
identities after travel along all three axes. It writes screenshots and measurements
under `Temp`. The open-volume diagnostic disables fog only while taking that image.
Visible skybox gaps are intentional; zero-background coverage is no longer a goal.

`-Mode Traversal` checks actual jumps from the starting roof to all five gravity
rooms and back at 30/60 Hz and fluctuating frame times. It checks supported
landings, continuous flight, smooth camera rotation, real keyboard/sprint/touch
input paths, gamepad bindings, overlap clearance and unchanged pool size.
It also crosses an actual native roof/side-room seam in both directions and holds
the player still at the corner to detect idle gravity oscillation.
Super jump now applies `UltraJumpPower = 0.75` to range, forward speed and height
(63.75 m, 19.5 m/s and 13.5 m with the original base settings). Targeted gravity
transfers still solve their complete collision-checked trajectory; launch velocity
is not scaled after solving. The rounded SUPER JUMP control sits above normal
jump, and a camera swipe retains ownership when crossing a button.

The ML director still chooses the high-level plan. Movement shuffles eligible
building groups instead of always choosing the rider first; travel, duration,
wave order and fallback axes vary, with a rest period before repeating a group.
Both endpoints of an occupied path are protected from stretching/slanting, and
approaching a path stops endpoint motion. These safety constraints take precedence
over randomness. Runtime pooled building/connection and district reuse is deferred
inside the entire 200 m protected sphere, including behind the camera during fast turns;
the fully hidden outer preparation band can be filled while looking directly at it.
Deferred districts retry after a camera turn even without changing cells.
Traversal validation includes occupied-path, motion-diversity, visibility and
touch-layout checks. Initial surrounding layers are prepared before gameplay starts.
`-Mode Controls` renders the actual touch canvas at 16:9 and 20:9 into
`Temp/super-jump-controls-16x9.png` and `Temp/super-jump-controls-20x9.png`.

`-Mode Corners` reproduces overlapping floor, side-room and entry-only gravity
fields. It verifies stationary stability, walking back and pooled-zone release.
`-Mode Quality` renders all seven near/medium models from above, below and the
side, checks their materials, and compares silhouettes/colour against the originals.

`-Mode Player` builds and runs an isolated Windows development player to enforce
actual build-time mesh access restrictions. It checks native collider cooking
under rotated/nonuniform parents, fully prepared startup, normal jumps,
no distance-based respawn, a real deep fall, sustained frame-budgeted 45 m/s travel,
fixed pool size, full generation and ML inclusion. This is not a phone
performance test. `-Mode AndroidBuild` produces `Builds/InfinityCastle-Android-dense-textured.apk`;
physical-device testing is still required.
Shaders are stripped only from the opt-in headless test player, not the Android game.
`-Mode Recovery` separately checks supported deep drops, horizontal travel,
ascending jumps, genuine void recovery, and an incomplete emergency pool.

Run Blender in background mode with `--python Tools/prepare_castle_distance_meshes.py
-- --mobile` to regenerate the seven near/medium models, atlases and triangle reports.
Then run `Tools/RunArchitectureValidation.ps1 -Mode Install` to import and wire the
mobile meshes/materials into the runtime prefab. Add `--library-only` to the Blender
command to package existing results in `SourceArt/CastleMobile.blend` with packed
textures and hidden medium meshes. `-Mode Quality` compares both mobile
levels against the original artwork from three directions, enforcing triangle/material
budgets and silhouette/colour checks. The compiled player check records full startup
time, including prewarming; this is not the shorter network-only generation log.
Original supplied models are not edited. Android frame rate and thermal
performance still require physical-device profiling.

The October 18 compiled headless Windows check measured full preload at 10.06 s
and 1,045 work frames after the loading-only budget change, versus 36.23 s and
13,331 work frames with the reduced meshes but the old 1 ms preload budget.
Both runs completed the native-collider and fixed-pool streaming checks. These
uncapped headless measurements are not a 30 fps phone loading-time prediction.

Restart Play mode after changing generator code or pool settings.

Timber construction uses GPU-driven rigid sections of the actual textured models,
with a 1.8-second arrival and a longer visible flight. `CastleTimberAssemblyBake`
now prepares all seven near/medium models plus the far room LOD without adding
triangles or changing their albedo UVs, normals, rest positions or bounds. Whole
triangles share a rigid pivot/phase in UV1; UV2 stores the local-space size, so
FBX import scales cannot launch pieces hundreds of metres away. Collider meshes
remain the original optimized sources. The older Blender-cut hero meshes are
retained as source/prototype assets, not used by the batched runtime effect.

Every newly visible native module, distributed room and path gets a construction
clock. There is no two-slot limit or skipped-animation fallback. Native renderer
property blocks and the existing 128-instance district batches drive the same
shader; no fragment GameObjects, rigidbodies, additional draw passes or higher-detail
substitute meshes are created. Matrix and clock arrays are rebased together for
each batch. Seen rooms never restart until genuinely recycled. Only the immediate
4m contact area completes early, keeping the player's floor stable.

The render hook is installed before the first building, not after full startup,
so construction is visible while generation runs. The loading UI is a small panel,
not a full-screen dimmer. Animated/finished construction remains opaque within
the 192m render boundary: the old distance dither cannot mask its timber movement.
Unseen rooms keep their clock until viewed, including when preparing far ahead.
`animateTimberConstruction` and `timberConstructionSeconds` control presentation;
castle generation, ML decisions and the full-volume startup gate are unchanged.

`Tools/RunArchitectureValidation.ps1 -Mode Assembly` verifies all 17 animated meshes (15 room/path LODs and two extracted pack stairs),
rigid triangles, unchanged triangle budgets, 201 simultaneous startup clocks,
one-shot/recycling behaviour, contact safety and actual GPU-rendered movement.
It exercises different clocks across the 128-instance batch boundary and checks
that distance dithering does not mask construction at 176m. Images and animation
frames go to `Temp/TimberAssemblyPreview`. These checks are not physical Android
frame-rate or thermal profiling.
The rendered Playground regression recorded 182 startup construction starts and
106 concurrent animated modules, then passed movement/look/jump and fixed-pool
streaming. The separate volume check passed native collisions, a 44m deep drop,
all six gravity orientations and moving-building support. Full startup in this
rendered PC test was 39.00 seconds; this change does not claim faster generation
or a tested phone frame rate.

`-Mode Streaming` travels 480 m in each of six directions with the camera facing
travel. It checks inner residency, ahead-of-view preparation, six-sided coverage,
multi-room lower layers and unchanged GameObject/body pools. It also renders the
distance transition to verify that geometry beyond the hidden band contributes
zero pixels. Reports and views are saved under `Temp/castle-streaming-tests.txt`
and `Temp/streaming-*.png`.

### Uniform streamed districts and guarded doors

The distant layout no longer substitutes a few isolated pairs for the dense spawn
network. Every 48 m cell uses the same seeded, three/four-storey network of 12/16
complete rooms, with variable room dimensions and one of six gravity frames.
Facing doors have real walkways; exterior switchback flights connect floors and
return through the remaining side doors. All three doors of every distributed
room have a physical route, including the top/bottom wrap. These are actual room
and stair meshes, not enclosing walls or a backdrop.

`CastlePackStairsBake` extracts only `stair_1` and `stair_3` from
`Assets/Models/Danish/inifinity_castle_pack.fbx`, normalizes their walking width,
and saves independent lightweight meshes/prefabs. No Danish building meshes are
introduced. The stair material uses the existing castle timber finish; UV1/UV2
construction metadata uses the same animated shader as the rooms. Stair rise/run
fits measured mesh endpoints, with smooth fitted floor colliders and shortened
rails so corners stay passable.

A 288 m residency halo plus a velocity-led preparation volume stays around the
player, regardless of distance from spawn. The 192 m visible sphere is protected
against recycling in every direction, including behind the camera. The bounded
1,536-cell pool is layout data and instanced transforms, not thousands of new
GameObjects. Physical rooms remain capped at 96, and nearby staircase colliders
lease the existing connection pool. Nothing is instantiated while streaming.

For the native building network, safe doorway matches still become connections.
As authorized, unmatched/blocked doors receive opaque, timber-lattice shoji panels
and solid colliders, rather than remaining false exits into empty space. Panels
are one shared 12-triangle instanced mesh/material, follow each room's construction
clock, and are not additional enclosing geometry. Door state is refreshed when
connections are created, removed or recycled; connected passages stay open.

The Volume and Streaming regressions verify doorway state, all distributed
door endpoints, fitted stair endpoints/slopes and containment within collision-safe
cells. Streaming also enforces at least 400 complete rooms within 100 m at the far
travel samples, and checks fixed object/physics pools and hidden preparation.
This is an automated PC check, not a measured Vivo Y400 frame-rate guarantee.

October 19 verification: Volume passed a real 44 m fall, all six gravity frames,
38,784 connected district door endpoints and an actual room-to-stair-to-next-floor
walk with both corners. Streaming passed 480 m legs in six directions, maintaining
589–658 complete nearby rooms at the far samples, hidden preparation and fixed
GameObject/body pools. Stair collider promotion changed no geometry/texture pixels
(one pixel differed by one 8-bit channel rounding step). The GPU Assembly check
passed all 17 animated meshes, retained stair textures/CPU collider data and a
visible textured shoji draw.

The compiled Windows development player passed transformed collider cooking for
both extracted stairs, normal/deep jumps and frame-budgeted 45 m/s streaming. Its
uncapped, headless full preload was 9.51 s. The rendered Playground check passed
startup input/camera/safety and fixed-pool streaming, observed 1,000 construction
starts during loading and preserved the skybox/fog; full preload was 49.66 s.
The rendered check recorded a 16.84 ms maximum streaming work slice on this PC.
These are different test environments, not Android loading/FPS claims. A new
Android build and physical Vivo Y400 profiling are still required.

### Android shader retention and unchanged density (October 22)

The earlier Android build did not prove that runtime-only instanced castle
variants were retained. Graphics Settings used Strip Unused (0), although the
district rooms/paths are created after scene loading. It now uses Keep All (2),
and AndroidBuild enforces that setting in the isolated build project. The final
shader preprocessor rejects a castle pass missing either regular or instanced
variants on GLES3 or Vulkan. The current build reports eight variants per API:
four regular and four instanced. Headless Windows test stripping cannot run on
Android. See Unity's [instancing stripping guidance](https://docs.unity3d.com/6000.0/Documentation/Manual/gpu-instancing-strip-variants.html).

The original textured/construction shader now explicitly targets shader model
3.5 and uses a fixed 64-instance buffer limit. The C# draw loops split at the
same limit and rebase transforms and construction clocks together. More batches
do not mean fewer rooms: no room/path counts, residency/render radii, LOD meshes,
textures, lighting finish or gravity rules were reduced. Nonuniform scale and
inverse matrices are retained. District room transforms are cached when their
layout changes instead of recomputing them for every render.

The GPU assembly check passed on Vulkan (Intel UHD 620), including the original
room's textured appearance, 129 rooms across three batches, independent clocks,
rigid timber flight and textured shoji/stairs. Captures disable asynchronous
shader compilation and warm the reference renderer first so an editor placeholder
is not mistaken for the real shader. The test rejects pink error pixels and an
untextured/black reference.

The OpenGL Core streaming check passed 480 m in all six directions, maintaining
589–658 complete rooms within 100 m at the far samples, hidden preparation,
connected stairs and unchanged object/96-body pools. These tests run on this PC,
not the Vivo Y400. They do not certify Android FPS, thermal behaviour or the
phone driver's runtime shader compilation; the new APK still needs phone testing.

The corrected ARM64 APK built successfully:
`Builds/InfinityCastle-Android-dense-textured.apk`, 161,565,124 bytes, package
`com.infinity.castle`, minimum SDK 23 / target SDK 36. APK signature verification
passed. SHA-256:
`24b7f08cbc921c9bf1004a6d70f338b6cf47d26762dfda1f94ee42a50304ef37`.
