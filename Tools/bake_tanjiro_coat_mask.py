"""Bake a UV-addressed garment mask from the preserved Tanjiro source mesh.

Run with Blender --background --python Tools/bake_tanjiro_coat_mask.py.
Only the editor mask is written; source geometry, UVs and textures are unchanged.
"""
import bpy
import json
import math
from pathlib import Path

root = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root / 'SourceArt/Characters/Tanjiro-SharedRig.blend'))
obj = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
shader = next(n for n in obj.active_material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
small = shader.inputs['Base Color'].links[0].from_node.image.copy()
small.scale(512, 512)
pixels = list(small.pixels)
green = [False] * len(obj.data.vertices)
for loop in obj.data.loops:
    uv = obj.data.uv_layers.active.data[loop.index].uv
    x, y = int((uv.x % 1) * 511), int((uv.y % 1) * 511)
    r, g, b = pixels[(y * 512 + x) * 4:(y * 512 + x) * 4 + 3]
    green[loop.vertex_index] |= g > .09 and g > r * 1.65 and b > r * 1.25
def region(p):
    return .48 < p.z < 1.12 and abs(p.x) < .31
def ramp(a, b, value):
    return max(0, min(1, (value-a)/(b-a)))
# Fit the outer coat surface from its green checks. Geodesic growth alone can
# cross this fused source mesh into the trousers at the hem. The radial surface
# test rejects that inner clothing while filling the coat's black checks.
grid = {}; bins = 48
for vertex in obj.data.vertices:
    p = vertex.co; inner = .11 + max(0, .98-p.z)*.25
    if green[vertex.index] and region(p) and (p.y > .035 or abs(p.x) > inner) and p.y > -.13:
        angle = math.atan2(p.x,p.y)
        key = (round(p.z/.04),round(angle/(2*math.pi)*bins)%bins)
        grid.setdefault(key,[]).append((p.z,angle,math.hypot(p.x,p.y)))
mask = []
for vertex in obj.data.vertices:
    p = vertex.co; estimates = []
    if region(p) and p.y > -.145:
        angle = math.atan2(p.x,p.y)
        key = (round(p.z/.04),round(angle/(2*math.pi)*bins)%bins)
        for dz in range(-2,3):
            for da in range(-2,3):
                for z,a,r in grid.get((key[0]+dz,(key[1]+da)%bins),[]):
                    angular = (angle-a+math.pi)%(2*math.pi)-math.pi
                    score = ((p.z-z)/.08)**2+(angular/.26)**2
                    if score < 1.3: estimates.append((score,r))
    estimates.sort(); nearest = estimates[:5]
    if nearest:
        radius = sum(r/(.02+s) for s,r in nearest)/sum(1/(.02+s) for s,r in nearest)
        selected = ramp(radius-.032,radius-.015,math.hypot(p.x,p.y))
        mask.append(1.0 if selected > .08 else 0.0)
    else: mask.append(0.0)
# Close isolated unselected corners inside a garment triangle. The AI source
# joins coat and trousers into one surface; leaving a single hem corner with
# thigh weights stretches that triangle even when the surrounding coat is fixed.
for _ in range(3):
    grown = mask.copy()
    for face in obj.data.polygons:
        if sum(mask[i] > .5 for i in face.vertices) < 2: continue
        for i in face.vertices:
            p = obj.data.vertices[i].co
            if region(p) and p.y > -.145: grown[i] = 1.0
    mask = grown
samples = {}
for loop in obj.data.loops:
    uv = obj.data.uv_layers.active.data[loop.index].uv
    key = (round(uv.x * 100000), round(uv.y * 100000))
    samples[key] = max(samples.get(key, 0), mask[loop.vertex_index])
payload = {'samples': [{'x': u/100000, 'y': v/100000, 'z': round(w, 5)} for (u, v), w in samples.items()]}
path = root / 'Assets/Editor/TanjiroCoatMask.json'
path.write_text(json.dumps(payload, separators=(',', ':')))
assert sum(w > .5 for w in mask) > 2000, 'Coat segmentation failed.'
print(f'Baked {sum(w > .5 for w in mask)} coat vertices into {len(samples)} UV samples: {path}')
