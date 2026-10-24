# Retopology verification

Validated in Unity 6000.0.51f1 with the production runtime prefab.

| Check | Result |
| --- | --- |
| Blender source | All 18 exported meshes/LODs have exclusively quad faces and zero open boundary edges |
| Active geometry | Ten near models total 18,832 triangles (previously 45,635) |
| Imported kit | Bounds/axes match the opening manifest; real doorway floors and clearance pass |
| Texture integration | Shared 1024px atlas, no procedural timber override; Android ASTC 6x6 import |
| Collision | CPU-readable near meshes, non-degenerate triangles; intact U-stair centreline |
| Six-sided gameplay | Startup, deep falling, six gravity frames, moving buildings, doorway closures and real stair ascent pass |
| Vulkan rendering | Textures render without pink error pixels; completed construction matches the original textured mesh |
| Construction animation | Rigid timber flight, 201 startup clocks and 129 independently timed instances pass |
| Long-distance streaming | Surrounding rooms/halls remain present after 480m travel; fixed object/physics pools retained |
| Standalone player | Compiled headless player initializes, evaluates 96 learned plans, lands after a deep departure and streams at 45m/s |

Vulkan rendering was tested on Intel UHD Graphics 620. The compiled headless
player checks physics and streaming, not visual quality. An Android device was
not attached: these checks do **not** establish phone frame rate or thermal
performance, and no new APK is claimed as device-tested.

Detailed logs (local, ignored by Git):

- `Temp/castle-retopology-tests.txt`
- `Temp/building-volume-tests.txt`
- `Temp/castle-timber-assembly-tests.txt`
- `Temp/castle-streaming-tests.txt`
- `Temp/PlayerCheck/checks.txt`

The source originals were preserved. Only generated runtime derivatives and
their import/material/collision metadata were replaced. The topology source,
atlas prompt and budget table are alongside this report.
