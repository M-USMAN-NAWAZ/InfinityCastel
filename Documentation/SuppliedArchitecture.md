# Playable Building Architecture

The default Playground uses the five current supplied room, corner-hall,
three-way-hall and stair models. The nine original Danish towers remain as dense
distant architecture at their authored scale. All runtime castle architecture
shares a dark-timber, warm-shoji and aged-brass finish. The player and skybox are
unchanged.

## Traversal

- Rooms meet at real, capsule-clear doorways. Most connections need no separate
  path mesh. Enter one room and leave another building.
- Walk on native room floors and building roofs. No perimeter walkway or broad
  connector landing is created in this mode.
- Selected gaps use the original side-railed stair_1 bridge. Roof-access flights
  use SingleStareBase. DoubleStaresBase is an optional upper-landing branch: its
  authored lower edge is not a usable entrance. Other gaps remain optional jumps.
- Bridges have pooled solid side-rail colliders fitted to their slope. Native
  stair rail collision retains the authored mesh independently of visual LODs.
- Corner and three-way halls have native floors, ceilings and shoji side panels. They physically move
  with their connected buildings, not as detached decorations.
- Sideways rooms are reachable beside native rooftops. Jumping into their entry
  fields changes gravity; returning to the roof restores its gravity. Each module
  uses its own orientation, including while its connected wing is moving.
  Stairs only join matching gravity.
- Falling prepares a pooled building under the predicted trajectory. Two wing
  slots are reserved for emergency landings, with two more kept for route growth.

## Motion And Pooling

DynamicInfinityCastle.Architecture.cs owns a fixed pool of 20 building wings,
with all room, roof, corridor and stair parts created once, and eight bridge slots.
The background retains its 192-object Android pool and four-layer window.

Connected wings share a motion pivot, easing curve and duration. Rooms, corridors
and stair joints retain their relative poses. An occupied group can carry the
player, but cannot rotate underneath them. Unoccupied groups translate, yaw or
change gravity frame when their complete swept bounds are clear. Approach stops
are decided for the entire group before any member advances.

The existing director controls quiet intervals and castle-wide waves. Its learned
policy receives the new room graph's observations and chooses motion and tempo;
placement and collision safety remain deterministic. The existing trained model
was not retrained for this new topology in this change.

Growth is bounded and incremental. Nearby buildings retain their poses; far,
unseen connected groups return to the pool together. No mesh, material, prefab or
GameObject is created during traversal, shifts, rebuilding or fall streaming.
The 30 FPS Android profile is a target, not a measured device-performance result.
Physical-phone profiling is still required before release.

## Source And Derived Assets

The five original FBX files remain unchanged in Assets/Models/CastleModules.
Playable derivatives live in Assets/InfinityCastle/CastleModules. The three
previously supplied hash-named models and their derivatives have been retired.

| Supplied File | Role |
| --- | --- |
| CorrectedRoom.fbx | Three-door room with its own walkable rooftop |
| HallWayWithTurn1.fbx | Connected L-shaped interior |
| HallWay3Way.fbx | Three-way interior junction |
| DoubleStaresBase.fbx | Optional platform attached through its verified upper landing |
| SingleStareBase.fbx | Native single-flight platform |

Derivatives use uniform metre conversion, three shared LOD meshes and compressed
1024-pixel texture atlases. The corner hall's missing atlas is repaired using the
current room atlas. Full authored collision is separate from visual LODs, so
decimation cannot seal passages. Supplemental floor collision is extracted from
the actual source footprint; no rectangular floor fills the halls' empty corners.
The corner hall's retained interior wall is opened at the elbow in its derivative
so a player can actually walk between its arms; the original FBX is unchanged.
Roof exits cut rail gates without adding another roof or a broad walkway.

Port checks use a human capsule and actual penetration, including panels and
ceilings. Connections align measured, often offset door and landing anchors,
rather than the centre of a bounding-box edge. Stair rail tops are not landings.
Concave collider broad-phase overlaps alone are insufficient.

Timber.png and Shoji.png in Assets/InfinityCastle/CastleTheme are shared finish
textures. CastleArchitecture.shader preserves source panel and trim detail while
unifying timber grain, paper fibres, dark roofs and metal accents. Runtime material
copies are cached once; the original character and skybox materials are untouched.

## Verification

Tools/ValidateSuppliedModels.ps1 bakes and checks the derived prefabs in a minimal
isolated Unity project. Tools/prepare_current_castle_modules.py regenerates meshes through
Blender; originals are never edited.

Tools/RunArchitectureValidation.ps1 -Mode Test runs full Unity checks: room
entry/exit, corner and junction interiors, stairs, rooftop traversal, stable gravity
entry and return, side rails in six gravity directions, indoor/outdoor jumps,
carried shifts, exact connected doorway offsets, whole-castle motion, overlap
clearance, six-direction fall recovery and fixed scene-object counts.

-Mode Preview loads the saved Playground, including its player, camera and
skybox, and renders player/overview screenshots under Temp.

Restart Play mode after these changes. The runtime prefab enables Use Building
Traversal and assigns all five architecture references. The older perimeter-deck
graph is retained only as an optional mode when building traversal is disabled.
