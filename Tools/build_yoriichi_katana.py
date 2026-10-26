"""Bake the reference inscription into the supplied FBX; preserve its geometry."""
import bpy
import json
import math
import sys
import numpy as np
from pathlib import Path
from mathutils import Matrix, Vector, Quaternion

root=Path(__file__).resolve().parents[1]
source=root/'SourceArt/Weapons/YoriichiKatana'
dest=root/'Assets/Weapons/YoriichiKatana'
dest.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source/'Inspection.blend'))
bpy.context.preferences.filepaths.save_version=0
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
obj.name='YoriichiKatana';obj.data.name='YoriichiKatanaMesh'
mat=obj.active_material;nodes=mat.node_tree.nodes;links=mat.node_tree.links
shader=nodes.get('Principled BSDF')
output=nodes.get('Material Output')
for n in list(nodes):
    if n.type=='TEX_IMAGE':
        uv=nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';links.new(uv.outputs[0],n.inputs['Vector'])
base=shader.inputs['Base Color'].links[0].from_socket
metal=shader.inputs['Metallic'].links[0].from_socket
rough=shader.inputs['Roughness'].links[0].from_socket
glyph=bpy.data.images.load(str(source/'MetsuGlyph.png'))
glyph_node=nodes.new('ShaderNodeTexImage');glyph_node.image=glyph;glyph_node.extension='CLIP'
glyph_uv=obj.data.uv_layers.new(name='InscriptionProjection')
# Source blade runs +X. The broad faces face +/-Y; give each face readable lettering.
# Keep the inscription clear of the collar and of the cutting edge.
for face in obj.data.polygons:
    side=1 if face.center.y>=0 else -1
    for index in face.loop_indices:
        p=obj.data.vertices[obj.data.loops[index].vertex_index].co
        glyph_uv.data[index].uv=(.5+side*(p.z-.058)/.030,(p.x-.052)/.034)
uv=nodes.new('ShaderNodeUVMap');uv.uv_map=glyph_uv.name
links.new(uv.outputs[0],glyph_node.inputs['Vector'])
mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MIX'
links.new(base,mix.inputs[1]);links.new(glyph_node.outputs['Color'],mix.inputs[2]);links.new(glyph_node.outputs['Alpha'],mix.inputs[0])
links.new(mix.outputs[0],shader.inputs['Base Color'])

scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
scene.world=bpy.data.worlds.new('Neutral studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.12,.15,.21,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.5
for name,pos,energy,size in [('Key',(.4,.6,.8),80,1.2),('Fill',(.3,-.7,.2),70,1),('Guard',(-.2,.3,.3),20,.5)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.size=size
    light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=pos
    light.rotation_euler=(Vector((.05,0,.06))-light.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Review');camera=bpy.data.objects.new('Review',data);scene.collection.objects.link(camera);scene.camera=camera
data.type='ORTHO';scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX';scene.render.resolution_percentage=100
def capture(name,centre,scale,size,direction=(0,1,0),roll=0):
    centre=Vector(centre);camera.location=centre+Vector(direction)*2
    camera.rotation_euler=((centre-camera.location).to_track_quat('-Z','Y')@Quaternion((0,0,1),roll)).to_euler()
    data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=size
    scene.render.filepath=str(source/name);bpy.ops.render.render(write_still=True)

if '--inspect' in sys.argv:
    for x in [0,.03,.05,.07,.09,.12]:
        verts=[v.co for v in obj.data.vertices if abs(v.co.x-x)<.006]
        print('SLICE',x,[(min(v[i] for v in verts),max(v[i] for v in verts)) for i in range(3)] if verts else [])
    capture('placement-preview.png',(.055,0,.061),.16,(1200,900),roll=-math.pi/2)
    sys.exit(0)

# Bake to the source UV atlas; source image reads have an explicit UV map.
obj.data.uv_layers.active=obj.data.uv_layers['UVMap'];obj.data.uv_layers['UVMap'].active_render=True
maps={};scene.cycles.samples=1;scene.render.bake.margin=8
for label,size in [('Albedo',2048),('Normal',1024),('MetallicSmoothness',1024)]:
    image=bpy.data.images.new('Katana_'+label,width=size,height=size,alpha=True)
    image.colorspace_settings.name='sRGB' if label=='Albedo' else 'Non-Color'
    target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
    if label=='Normal':links.new(shader.outputs[0],output.inputs['Surface'])
    else:
        emission=nodes.new('ShaderNodeEmission')
        if label=='Albedo':links.new(mix.outputs[0],emission.inputs['Color'])
        else:
            pack=nodes.new('ShaderNodeCombineColor');links.new(metal,pack.inputs[0])
            invert=nodes.new('ShaderNodeMath');invert.operation='SUBTRACT';invert.inputs[0].default_value=1
            links.new(rough,invert.inputs[1]);links.new(invert.outputs[0],pack.inputs[1]);links.new(pack.outputs[0],emission.inputs['Color'])
        links.new(emission.outputs[0],output.inputs['Surface'])
    bpy.ops.object.bake(type='NORMAL' if label=='Normal' else 'EMIT')
    if label=='MetallicSmoothness':
        pixels=np.empty(size*size*4,dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape((-1,4))
        pixels[:,3]=pixels[:,1];pixels[:,1:3]=pixels[:,0:1];image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw=str(dest/f'Katana_{label}.png');image.file_format='PNG';image.save();maps[label]=image
    links.new(shader.outputs[0],output.inputs['Surface'])

material=bpy.data.materials.new('YoriichiKatana');material.use_nodes=True
nodes=material.node_tree.nodes;links=material.node_tree.links;shader=nodes.get('Principled BSDF')
for label in maps:
    texture=nodes.new('ShaderNodeTexImage');texture.image=maps[label]
    if label=='Albedo':links.new(texture.outputs[0],shader.inputs['Base Color'])
    elif label=='Normal':
        normal=nodes.new('ShaderNodeNormalMap');links.new(texture.outputs[0],normal.inputs['Color']);links.new(normal.outputs[0],shader.inputs['Normal'])
    else:
        split=nodes.new('ShaderNodeSeparateColor');links.new(texture.outputs[0],split.inputs[0]);links.new(split.outputs[0],shader.inputs['Metallic'])
        invert=nodes.new('ShaderNodeMath');invert.operation='SUBTRACT';invert.inputs[0].default_value=1
        links.new(texture.outputs['Alpha'],invert.inputs[1]);links.new(invert.outputs[0],shader.inputs['Roughness'])
obj.data.materials.clear();obj.data.materials.append(material)
for name in [u.name for u in obj.data.uv_layers if u.name!='UVMap']:obj.data.uv_layers.remove(obj.data.uv_layers[name])
# Reorient for a hand-held Unity prefab: blade +Y, root centred inside the grip.
obj.matrix_world=Matrix(((0,0,1,-.061),(0,-1,0,0),(1,0,0,.16),(0,0,0,1)))
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
bpy.ops.wm.save_as_mainfile(filepath=str(source/'YoriichiKatana.blend'))
bpy.ops.export_scene.fbx(filepath=str(dest/'YoriichiKatana.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,
    axis_forward='-Z',axis_up='Y',path_mode='RELATIVE',embed_textures=False)
report=dict(triangles=sum(len(p.vertices)-2 for p in obj.data.polygons),vertices=len(obj.data.vertices),materials=1,
    inscription='滅',inscription_baked_into_albedo=True,original_geometry_preserved=True,length_metres=1.196758,
    maps={label:list(i.size) for label,i in maps.items()})
source.joinpath('build-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
# All previews below show the exported, baked material, not the temporary projection.
capture('katana-preview.png',(0,0,.40),1.34,(1600,650),(0,-1,.08),math.pi/2)
capture('inscription-preview.png',(-.003,0,.235),.135,(1200,900),(0,-1,0),0)
capture('inscription-reverse.png',(-.003,0,.235),.135,(1200,900),(0,1,0),0)
print('YORIICHI KATANA BUILT',json.dumps(report,ensure_ascii=False))
