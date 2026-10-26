"""Inspect and render the supplied GLB before editing its artwork."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

root=Path(__file__).resolve().parents[1]
folder=root/'SourceArt/Weapons/RedNichirinKatana'
folder.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(folder/'Original/katana_sword_red_blade.glb'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
report={}
for o in meshes:
    points=[o.matrix_world@v.co for v in o.data.vertices]
    report[o.name]={'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),
        'min':[min(p[i] for p in points) for i in range(3)],'max':[max(p[i] for p in points) for i in range(3)],
        'materials':[s.material.name if s.material else None for s in o.material_slots],
        'material_faces':{str(i):sum(p.material_index==i for p in o.data.polygons) for i in range(len(o.material_slots))},
        'uv_layers':[v.name for v in o.data.uv_layers],
        'shader_images':{s.material.name:[{'node':n.name,'image':n.image.name,'outputs':[link.to_socket.name for link in n.outputs[0].links]} for n in s.material.node_tree.nodes if n.type=='TEX_IMAGE'] for s in o.material_slots}}
report['textures']=[{'name':i.name,'size':list(i.size),'packed':bool(i.packed_file)} for i in bpy.data.images]
folder.joinpath('inspection.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(folder/'Inspection.blend'))
points=[o.matrix_world@Vector(v) for o in meshes for v in o.bound_box]
low=Vector([min(p[i] for p in points) for i in range(3)])
high=Vector([max(p[i] for p in points) for i in range(3)])
centre=(low+high)*.5; span=max(high-low)
scene=bpy.context.scene; scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.color_type='TEXTURE'; scene.display.shading.light='STUDIO'
scene.render.resolution_x=1400;scene.render.resolution_y=900;scene.render.resolution_percentage=100
data=bpy.data.cameras.new('Inspection camera');camera=bpy.data.objects.new('Inspection camera',data)
scene.collection.objects.link(camera);scene.camera=camera;data.type='ORTHO';data.ortho_scale=span*1.15
for name,direction in [('broadside',(0,.1,1)),('reverse',(0,-.1,-1))]:
    camera.location=centre+Vector(direction).normalized()*span*2
    camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(folder/f'original-{name}.png');bpy.ops.render.render(write_still=True)
data.ortho_scale=5.5;centre=Vector((-1.4,-.05,0));camera.location=centre+Vector((-1,2,9))
camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(folder/'original-handle.png');bpy.ops.render.render(write_still=True)
scene.display.shading.color_type='MATERIAL'
for target in ('Object_12','Object_14'):
    for o in meshes: o.hide_render=o.name!=target
    bpy.data.objects[target].active_material.diffuse_color=(.8,.8,.8,1)
    scene.render.filepath=str(folder/f'isolated-{target}.png');bpy.ops.render.render(write_still=True)
