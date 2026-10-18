# Runtime Castle Architecture

Playground uses the supplied models in `Assets/Models/CastleModules`: CorrectedRoom,
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
and gravity when approached. Their paired front entrances have physical native
paths; they are not facade panels or a skybox replacement.

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
and 768 lightweight reusable cluster-data slots. No castle GameObjects or meshes are created
or destroyed by traversal, shifts or spatial streaming after warm-up. Far room
volumes use shared instanced LOD meshes; the complete room distance mesh has
900 triangles. Nearby collision is leased from the physics pool.

All seven medium-distance meshes are regenerated from the current supplied FBXs
at 12,500 triangles (about one quarter of the originals). Their colours are baked
onto new 1024-pixel mipmapped/compressed atlases. Reusing the original atlas after
collapsing mesh UV seams causes the large stretched roof/window triangles; the
new atlas/material must always travel with its matching LOD mesh. Source models
and their near textures are not modified.

The seven native FBXs retain Read/Write data and import pre-cooked MeshColliders.
Their rotated/scaled runtime parents can require re-cooking in a built player;
the Editor's mesh access previously concealed failures when Read/Write was off.
A targeted model importer and build preflight preserve these flags on reimport.
Render-only distance meshes still have Read/Write disabled.

Startup and streaming run through the same incremental work queue, using
`layoutMillisecondsPerFrame` (default 2 ms) as a soft CPU budget. Atomic Unity
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
performance test. `-Mode AndroidBuild` produces `Builds/InfinityCastle-Android-streaming.apk`;
physical-device testing is still required.
Shaders are stripped only from the opt-in headless test player, not the Android game.
`-Mode Recovery` separately checks supported deep drops, horizontal travel,
ascending jumps, genuine void recovery, and an incomplete emergency pool.

`Tools/prepare_castle_distance_meshes.py` regenerates shared distance models through
Blender without editing the original supplied models. Android frame rate and thermal
performance still require physical-device profiling.

Restart Play mode after changing generator code or pool settings.

`-Mode Streaming` travels 240 m in each of six directions with the camera facing
travel. It checks inner residency, ahead-of-view preparation, six-sided coverage,
multi-room lower layers and unchanged GameObject/body pools. It also renders the
distance transition to verify that geometry beyond the hidden band contributes
zero pixels. Reports and views are saved under `Temp/castle-streaming-tests.txt`
and `Temp/streaming-*.png`.
