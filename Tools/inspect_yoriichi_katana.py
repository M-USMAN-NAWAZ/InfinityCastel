"""Inspect the replacement FBX without changing its mesh or materials."""
import bpy
import json
from pathlib import Path
from mathutils import Vector, Quaternion
import math

root = Path(__file__).resolve().parents[1]
folder = root / 'SourceArt/Weapons/YoriichiKatana'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.import_scene.fbx(filepath=str(folder / 'Original/YoriichiKatana.fbx'))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
report = {'meshes': [], 'materials': [], 'images': []}
points = []
for obj in meshes:
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    points.extend(verts)
    report['meshes'].append(dict(name=obj.name, vertices=len(verts),
        triangles=sum(len(p.vertices)-2 for p in obj.data.polygons),
        minimum=[min(v[i] for v in verts) for i in range(3)],
        maximum=[max(v[i] for v in verts) for i in range(3)],
        uv=[u.name for u in obj.data.uv_layers],
        materials=[s.material.name if s.material else None for s in obj.material_slots]))
for mat in bpy.data.materials:
    report['materials'].append(dict(name=mat.name, nodes=[dict(name=n.name, type=n.type,
        image=n.image.name if n.type == 'TEX_IMAGE' and n.image else None,
        inputs={s.name:list(s.default_value) if hasattr(s.default_value, '__len__') else s.default_value
            for s in n.inputs if hasattr(s, 'default_value') and s.type in {'VALUE','RGBA'}},
        links=[(l.from_node.name,l.from_socket.name,l.to_socket.name) for s in n.inputs for l in s.links])
        for n in mat.node_tree.nodes] if mat.use_nodes else []))
for img in bpy.data.images:
    report['images'].append(dict(name=img.name,path=img.filepath,size=list(img.size),packed=bool(img.packed_file)))
folder.joinpath('inspection.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(folder / 'Inspection.blend'))
low=Vector([min(p[i] for p in points) for i in range(3)])
high=Vector([max(p[i] for p in points) for i in range(3)])
centre=(low+high)*.5
span=max(high-low)
scene=bpy.context.scene
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.color_type='TEXTURE'
scene.display.shading.light='STUDIO'
scene.render.resolution_x=1600
scene.render.resolution_y=900
scene.render.resolution_percentage=100
data=bpy.data.cameras.new('Inspection camera')
camera=bpy.data.objects.new('Inspection camera',data)
scene.collection.objects.link(camera)
scene.camera=camera
data.type='ORTHO'
data.ortho_scale=span*1.12
for axis in range(3):
    direction=Vector((0,0,0)); direction[axis]=1
    camera.location=centre+direction*span*2
    camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(folder/f'original-axis{axis}.png')
    bpy.ops.render.render(write_still=True)
