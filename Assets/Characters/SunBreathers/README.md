# Tanjiro and Yoriichi — shared Humanoid rigs

Tanjiro now also has sword **draw / sheathe** actions: press **R**, or use the
castle's mobile **DRAW / SHEATHE** button. The existing Playground player and
`TanjiroThirdPerson.prefab` inherit the setup. See `SwordActions/README.md` for
the Humanoid clips, attachment events, preserved source artwork and tests.

Open `SharedRigDemo.unity` and enter Play mode to see both characters use the
same `SharedRigCheck` controller and Tanjiro's Humanoid diagnostic clip.
`Tanjiro.prefab` and `Yoriichi.prefab` each retain their own valid Avatar.
The Playground player now uses Tanjiro's visual on its existing controller root.
Its prior geometry and root Animator are kept disabled for easy reversal.
The prefab root faces Unity +Z; its `Model` child contains the Animator.
The Avatar reference pose uses Unity's Enforce T-Pose alignment, so there is
no extra 180-degree visual rotation to conflict with standard movement clips.

## Third-person movement

`TanjiroThirdPerson.prefab` and `YoriichiThirdPerson.prefab` reuse this project's
Starter Assets `ThirdPersonController`, inputs, locomotion Animator Controller,
and camera target. Open `ThirdPersonDemo.unity` to try Tanjiro with the existing
follow camera on a test floor. To try Yoriichi, replace the player with
`YoriichiThirdPerson.prefab` and set the follow camera's Follow/Look At to its
`PlayerCameraRoot`. These are separate from the rig-diagnostic prefabs above.

Movement/collision/input/gravity components stay on the player root. The
Humanoid Animator stays on `Model`, with root motion disabled. The movement
controller now supports both child and legacy root Animators. The small
`ThirdPersonAnimationEvents` component forwards footstep/landing events from
the child to that root. Keep the existing mobile input UI pointed at the new
root's `StarterAssetsInputs` if replacing a player later. The Playground swap
reuses the original player root, input, camera target, speed tuning, and castle
references. The standalone Tanjiro preview is reused as the attached visual;
the separate Yoriichi preview remains untouched.

To repeat the visual-only setup, use **Infinity Castle > Player > Use Tanjiro
on Current Player** with Playground open outside Play mode. It is undoable and
saves a pre-swap scene copy to `Temp/Playground-before-Tanjiro.unity`.

## Rig contract

- Both skeletons have the same 55 bone names and parent relationships.
- Body, toes, and all five fingers are mapped to Unity Humanoid.
- `LeftSwordSocket` and `RightSwordSocket` are children of the respective hands.
  Adjust the weapon's local grip offset when a separate katana is supplied.
- Each supplied mesh has its own bone-heat skinning, normalized to at most four
  influences per vertex. Character proportions are not forced to match.
- Shoulder joints are approximately 15% narrower, with a roughly 2 cm drop and
  a smooth local coat/upper-body fit. Arm and finger bones follow that fit;
  original UVs, texture images, height, and lower-body proportions are preserved.
- Root motion is disabled on the prefabs. Gameplay movement and gravity remain
  the responsibility of the existing character controller.

The long sleeve undersides are compressed into the sleeve mesh in Blender,
with smoothly blended shoulder/cuff attachments and arm-segment skin weights.
The failed outward-pulling garment anchors and runtime drape component have
been removed. No cloth simulation, per-frame mesh rebuilding, or additional
runtime sleeve logic is needed. Original UVs and texture files are retained;
up to four bone influences and the shared Humanoid locomotion remain in use.

`SharedRigCheck` is a looping deformation/retargeting test, **not a finished
Hinokami Kagura technique** or a gameplay locomotion controller. It exercises
elbows, shoulders, fingers, knees, and torso rotation on both characters.
Further combat poses need visual review as each technique is authored.

## Preserved artwork and limitations

The supplied FBXs are each one connected mesh, including clothing, hair, and
the sheathed sword. Original UVs and embedded texture images are preserved.
These are skeletal-deformation rigs, not physics-simulated cloth/hair or
facial rigs. The original FBX weapon is fused; Tanjiro's separate `SwordActions`
mesh and katana setup provide the drawable version without overwriting it.
The original geometry is about
50,000 triangles per character; it has not been decimated during rigging.
Albedo and normal textures use a 2048 import limit and Android ASTC 6x6.
Actual Android frame rate still needs device testing.

## Source and verification

Editable Blender files: `SourceArt/Characters/Tanjiro-SharedRig.blend` and
`SourceArt/Characters/Yoriichi-SharedRig.blend` at the project root.
Untouched input copies are under `SourceArt/Characters/Originals/`.
Bone hierarchy and weight checks: `SourceArt/Characters/shared-rig-report.json`.

Rebuild with Blender using `Tools/rig_sun_characters.py`.
Verify and regenerate Unity prefabs in an isolated project with:

```powershell
& './Tools/RunArchitectureValidation.ps1' -Mode Characters -GraphicsAPI Vulkan
```

Unity results: `Temp/sun-breather-rig-tests.txt` and
`Temp/sun-breathers-shared-rig.png`. Tests also simulate grounding, walk/sprint,
jump/landing, camera look, sideways gravity, and legacy root-Animator lookup.
Idle/walk/jump snapshots are `Temp/sun-breather-<character>-<pose>.png`.
Reference T-pose alignment, relaxed idle arms, forward direction, and changing
walk poses are checked in addition to movement and Animator parameters.
Garment checks sample 12 phases each of walking, running and jumping, measuring
torso crossings and the fabric's distance from the arm segments, and capturing
front, side and back views. Images are
`Temp/sun-breather-Tanjiro-garment-<motion>-<phase>-<view>.png`.
