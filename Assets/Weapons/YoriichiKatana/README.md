# Yoriichi katana with red inscription

Use `YoriichiKatana.prefab`. The supplied FBX's mesh, red blade, grip and gold fittings are retained. The red **滅** character from the reference is baked near the guard on both blade faces, readable rather than mirrored. There is no floating decal, extra renderer or runtime shader dependency.

The original is preserved at `SourceArt/Weapons/YoriichiKatana/Original/YoriichiKatana.fbx` (SHA256 `C11A5D442E8032FF787EBEF15E7AD17A231276D2AABAC7B0996FDF286DFED8C1`). The editable Blender file and renders are alongside it in the parent folder.

The source has 50,000 triangles; this texture-only change does not decimate or retopologize it. One mesh/material, 2K albedo and 1K normal/metallic-smoothness maps. Android import uses mipmaps and ASTC 6x6. These are import settings, not an on-device performance certification.

Blade axis is Unity +Y; prefab root and `GripPivot` sit inside the grip. `BladeTip` is available for later attack effects. Not automatically equipped on a character; no scene or movement changes.

## Rebuild and verify

Run `Tools/inspect_yoriichi_katana.py` in Blender background mode after placing the source in `Original`, then `Tools/build_yoriichi_katana.py`. Run `Tools/RunArchitectureValidation.ps1 -Mode YoriichiKatana` to create the URP material/prefab in the isolated Unity project and verify imports/renders.

## Generated texture provenance

Mode: built-in image generation with the user's screenshot as a shape/style reference, transparent background. The selected image is saved at `SourceArt/Weapons/YoriichiKatana/MetsuGlyph.png`. Blender's material bake composites it into the supplied atlas.

Final prompt:

> Asset type: transparent game texture decal.
> Input image 1 is only a shape/style reference: the RED engraved Japanese kanji circled on the sword. The hand-drawn red circle/line are annotations, NOT part of the design.
> Create one clean isolated upright Japanese kanji "滅" (U+6EC5), the exact character shown. Preserve its angular tapered calligraphic strokes: three separated water drops at left, the complex right component, balanced square silhouette. Crimson red with small pink-red beveled highlights and dark burgundy edges like the reference engraving. Front-on orthographic flat texture, no perspective or screen moire.
> Composition: single centered glyph occupying 80% of a square canvas, generous transparent margins.
> Must be the one correct glyph "滅", not any similar character. No blade, no metal background, no other text, no annotation circle or stroke, no border, no cast shadow, no glow, no watermark. Genuinely transparent background, preserve alpha for baking into an existing blade albedo.
