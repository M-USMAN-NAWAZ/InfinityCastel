import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
out = {}
for name in ('Tanjiro', 'Yoriichi'):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / f'SourceArt/Characters/Inspection/{name}-inspection.blend'))
    obj = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH')
    vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    h = max(p.z for p in vertices)
    neighbours = [[] for _ in vertices]
    for edge in obj.data.edges:
        a,b = edge.vertices
        neighbours[a].append(b); neighbours[b].append(a)
    unseen = set(range(len(vertices)))
    components = []
    while unseen:
        stack = [unseen.pop()]; part = []
        while stack:
            current = stack.pop(); part.append(current)
            for other in neighbours[current]:
                if other in unseen: unseen.remove(other); stack.append(other)
        components.append(part)
    out[name] = {'components': sorted([len(part) for part in components], reverse=True), 'slices': {}}
    for x in (.12,.18,.24,.3,.36,.4,.44,.48):
        near = [p for p in vertices if abs(p.x-x)<.008 and p.z>h*.7]
        if near: out[name]['slices'][x] = [[min(p[i] for p in near),max(p[i] for p in near)] for i in range(3)]
    camera = bpy.context.scene.camera
    centre = Vector((max(p.x for p in vertices)*.84,0,h*.78))
    camera.location = centre + Vector((0,0,2))
    camera.rotation_euler = (centre-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale = h*.28
    bpy.context.scene.render.filepath = str(ROOT / f'SourceArt/Characters/Inspection/{name}-hand-top.png')
    bpy.ops.render.render(write_still=True)
ROOT.joinpath('SourceArt/Characters/Inspection/topology.json').write_text(json.dumps(out,indent=2))
print(json.dumps(out,indent=2))
