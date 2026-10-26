# Red Nichirin katana

Use `RedNichirinKatana.prefab`, which has the supplied curved blade and
decorative guard, a dark steel/red hamon finish, and a fitted white cloth grip
with ten gold diamond openings on each side. The original GLB is unchanged.

The weapon is 1.05 metres long. Its root/GripPivot is inside the handle; the
blade runs along local +Y. Parent the prefab to a character hand/weapon socket
and adjust its local grip rotation for that character. BladeTip marks the tip.
It is not automatically equipped, and does not modify the player's controller,
body mesh, existing sheathed sword, or animations. Draw/sheath animations and
combat hit detection are not included in this visual asset.

One mesh/submesh, one standard URP Lit material, a 2048 albedo atlas and 1024
normal, metallic/smoothness, and emission maps. Android uses mipmapped ASTC 6x6.
The red edge has mild self-emission; the prefab contains no realtime lights,
post-processing, runtime scripts, Rigidbody, or movement-blocking colliders.
Per-device performance and combat effects still need testing in gameplay.

Editable source and portable textured GLB:
`SourceArt/Weapons/RedNichirinKatana/RedNichirinKatana.blend` and `.glb` at the
project root. An untouched original is stored in that folder's `Original/`.
Rebuild with Blender using `Tools/build_red_katana.py`; regenerate and validate
the Unity prefab with `Tools/RunArchitectureValidation.ps1 -Mode Katana`.
The build report records triangle count and texture sizes; Unity import,
material, pivot, UV, scale and front/back render checks are recorded in
`Temp/red-katana-tests.txt`. Preview images are `Temp/red-katana-*.png`.
