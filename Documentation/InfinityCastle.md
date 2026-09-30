# Infinity Castle

Open the Starter Assets `Playground` scene, stop an existing Play session, and start Play again. The new pools need a fresh session.

The runtime uses the assigned Danish buildings at their original scale. Unit-scale module parents position the art; they do not resize it. `stair_1` is the level walkway, and `stair_3` supplies the stepped flights. Timber balconies, fitted stair collision wedges, and connector landings provide continuous walking surfaces.

The runtime prefab's `Architecture Finishes` restore the unassigned imported albedo textures on shared runtime materials. Timber, paper panels, and illuminated window finishes are editable there; the original model and material assets are not modified.

## Controls

- WASD: camera-relative movement.
- Space: jump, including sideways and inverted gravity.
- Shift: sprint.
- Mouse: look.

The native CharacterController supplies capsule dimensions but is disabled during play. InfinityCharacterMotor uses PhysX capsule sweeps in the current gravity frame. ThirdPersonController owns movement and jump velocity; InfinityGravityBody only selects gravity. There is no second world-down gravity pass.

## Castle Rules

- Building, connector, and landing pools are populated once during initialization. Streaming does not instantiate or destroy modules.
- Four route layers surround the player horizontally, with two additional connected floors below. Background architecture tracks movement in all three axes.
- Retained cells and connectors do not move when the streaming window advances. Only outgoing and incoming edge rows are recycled.
- The nearest two route layers remain stable. Outer, unoccupied wings may disconnect, slide along one checked direction, dissolve, and reassemble using a different pooled building.
- Complete swept bounds are reserved before movement. Moving buildings pause when the player enters their safety volume.
- Tilted background districts have gravity matching their orientation. Main routes and their stairs share world-down gravity.
- A tiled landing hall remains fixed during a fall and catches gaps within three architectural layers. It cannot chase the falling player downward.
- Scene lighting and skybox are overridden only while `Castle Lighting` is enabled; authored materials and scene assets are not rewritten.

The layout is anime-inspired rather than a frame-for-frame recreation of the film. The imported buildings have large, differing bounds, so clearance determines spacing rather than scaling their geometry.

## Verification

`Assets/Editor/InfinityCastleValidation.cs` checks six gravity directions, movement, jumping, landing, overlapping zones, downhill/uphill connectors, moving-building clearance, pool reuse, and the saved Playground configuration.

Run it in an isolated Unity project copy with `-batchmode -nographics -quit -executeMethod InfinityCastleValidation.Run`. Do not run validation against an open working scene. The validation entry point intentionally refuses non-batch execution.

`InfinityCastleValidation.RenderPlayground` runs an isolated play-mode preview and captures the actual scene before and during shifts.
