# Mobile performance pass

This pass preserves the castle artwork, shader, density, visible/fade distances,
pool capacities, construction animation, random layouts, controls, gravity,
movement, jumps, sword animations and collision distances. No quality preset,
mesh, material or texture was reduced or replaced.

## Changes

- Nearby room collision selection rejects entire distant district bounds before
  inspecting their rooms. Only the existing leased body list needs retirement
  checks. The original 48/68 metre room thresholds and 24 metre prediction radius
  remain unchanged.
- Distant path clusters with no collider leases no longer rescan all 72 crossing
  slots every refresh. A cluster leaving range still releases its leases once;
  recycling resets that state. Path prediction and promotion limits are unchanged.
- Room distances are computed once before sorting, rather than repeatedly
  crossing into `Transform.position` and recalculating bounds distances inside
  the comparator. The position is captured once per query/render pass.
- District shoji-door matrices are cached from the same source port expression.
  Native door rendering reads each part's world matrix once per part.

The original steady-state desktop microbenchmark found about 6.18 ms per forced
district-physics refresh at 925 clusters / 11,941 rooms / 24,028 paths. Post-change
runs during the Android build ranged from 1.09 to 3.34 ms, but background build
contention makes those timings unsuitable as an FPS claim. They are not phone
measurements.

## Verification

Run `Tools/RunArchitectureValidation.ps1 -Mode Volume` in an isolated project.
It now calls `CastlePerformanceValidation.Validate` before and after traversal:
90 six-direction/fast-travel queries must select exactly the same rooms as the
exhaustive scan, cluster lease state must be consistent, and every cached door
matrix must equal the previous expression. Existing checks cover deep falls,
all six gravity orientations, moving support, native doors, intact stair_2,
pool reuse, dense graph variety and startup control recovery. Both passes passed.

The editor profile was returned to stopped/outside Play mode. Runtime-only
background execution used for measurement is not a Player Settings change.

Use `Tools/RunArchitectureValidation.ps1 -Mode AndroidBuild` for the ARM64 release
APK. The build retains regular and instanced castle shader variants for Vulkan
and GLES3. Existing Android frame pacing and the 30 FPS target are unchanged.

The release build completed successfully. `Builds/InfinityCastle-Android-optimized.apk`
is the updated package; `Builds/InfinityCastle-Android-before-performance.apk`
preserves the previous APK. The build log confirmed four regular and four
instanced Castle Forward variants on each of Vulkan and GLES3.

There was no connected Android device. Smoothness, GPU cost, sustained frame
rate and thermal throttling on the Vivo Y400 still require an on-phone test;
passing desktop checks does not certify mobile frame rate.
