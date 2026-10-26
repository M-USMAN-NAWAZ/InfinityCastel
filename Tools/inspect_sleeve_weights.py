"""Inspect the supplied garment's bind-space regions and arm weights."""
import bpy
import json
from pathlib import Path
from collections import Counter
from mathutils import Vector

root=Path(__file__).resolve().parents[1]
source=root/'SourceArt/Characters'
bpy.ops.wm.open_mainfile(filepath=str(source/'Inspection/Tanjiro-inspection.blend'))
original=next(obj for obj in bpy.context.scene.objects if obj.type=='MESH')
points=[original.matrix_world@v.co for v in original.data.vertices]
bpy.ops.wm.open_mainfile(filepath=str(source/'Tanjiro-SharedRig.blend'))
obj=next(obj for obj in bpy.context.scene.objects if obj.type=='MESH')
rig=next(obj for obj in bpy.context.scene.objects if obj.type=='ARMATURE')
report={}
for start,end in [(0,.10),(.10,.14),(.14,.18),(.18,.24),(.24,.30),(.30,.36),(.36,.40)]:
    part=[v for v in obj.data.vertices if start<points[v.index].x<end and .50<points[v.index].z<.88]
    report[str((start,end))]={'count':len(part),'z_range': [min(points[v.index].z for v in part),max(points[v.index].z for v in part)],
        'weights':Counter(obj.vertex_groups[g.group].name for v in part for g in v.groups if g.weight>.15)}
    report[str((start,end))]['slices']={}
    for low,high in [(.5,.6),(.6,.7),(.7,.77),(.77,.83),(.83,.88)]:
        group=[v for v in part if low<points[v.index].z<high]
        report[str((start,end))]['slices'][str((low,high))]={'count':len(group),'weights':Counter(obj.vertex_groups[g.group].name for v in group for g in v.groups if g.weight>.15)}
print(json.dumps(report,indent=2))
source.joinpath('Inspection/Tanjiro-sleeve-weights-before.json').write_text(json.dumps(report,indent=2))
scene=bpy.context.scene
scene.frame_set(21)
scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.color_type='TEXTURE'
scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
camera_data=bpy.data.cameras.new('Sleeve review');camera=bpy.data.objects.new('Sleeve review',camera_data)
scene.collection.objects.link(camera);scene.camera=camera;camera_data.type='ORTHO';camera_data.ortho_scale=1.9
for side,direction in [('back',(0,3,0)),('side',(3,0,0))]:
    centre=Vector((0,0,.825));camera.location=centre+Vector(direction)
    camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(source/f'Inspection/Tanjiro-sleeve-{side}-before.png')
    bpy.ops.render.render(write_still=True)
