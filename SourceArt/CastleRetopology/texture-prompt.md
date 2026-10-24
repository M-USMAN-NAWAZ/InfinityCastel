# Shared castle trim atlas

Generated using the built-in image generation tool, not the API/CLI.

Reference inputs: the supplied square room, stair-platform and L-hallway images.
Style: polished dark reddish walnut, ivory rice-paper shoji, antique gold floral
fittings and amber lanterns. Texture detail replaces expensive small carvings.

Prompt specification: produce one opaque orthographic 4 by 4 grid with exactly
equal cells, no labels, no perspective, no cast shadows, no gaps. Boundaries at
25%, 50% and 75%. Row-major, from top left:

1. Dark reddish walnut, vertical grain for posts.
2. Reddish/honey horizontal timber grain.
3. Charcoal wooden roof boards and horizontal seams.
4. Near-black lacquered timber.
5. Ivory rice paper, no lattice.
6. Amber lantern paper, bright cream centre, no frame.
7. Centred antique-gold floral rosette with a thin border and dark recess.
8. Repeating horizontal carved gold floral trim.
9. Polished reddish wooden floor boards, horizontal grain/seams.
10. Deep dark timber lattice.
11. Smooth aged brass.
12. Bronze/wood end cap with subtle gold edging.
13. Ivory shoji with a four-column, five-row reddish lattice.
14. Amber shoji with the same lattice.
15. Dark vertical lower-wall timber boards.
16. Cream/yellow lantern paper with brass-coloured vertical edge details.

Runtime: one 1024px albedo atlas, sRGB, opaque, mipmapped, ASTC 6x6 on Android.
UVs have an eight-pixel-equivalent inset at 1024px. Lantern cells have a cheap
UV-cell emission mask in the existing shader: no extra texture or point lights.
