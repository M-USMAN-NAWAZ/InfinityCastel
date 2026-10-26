"""Restyle the supplied katana, preserving its blade/guard, and bake one Unity atlas."""
import bpy
import bmesh
import json
import math
import sys
import numpy as np
from pathlib import Path
from mathutils import Vector, Quaternion

root=Path(__file__).resolve().parents[1]
source=root/'SourceArt/Weapons/RedNichirinKatana'
dest=root/'Assets/Weapons/RedNichirinKatana'
dest.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source/'Inspection.blend'))
bpy.context.preferences.filepaths.save_version=0
parts=[o for o in bpy.context.scene.objects if o.type=='MESH']
for o in parts:
    matrix=o.matrix_world.copy();o.parent=None;o.matrix_world=matrix
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)

def value_node(mat,kind,**values):
    node=mat.node_tree.nodes.new(kind)
    for key,value in values.items(): setattr(node,key,value)
    return node

styles={}
for o in parts:
    mat=o.active_material;name=mat.name
    # Retain the source's blade lettering and irregular hamon and its normal maps.
    for node in mat.node_tree.nodes:
        if node.type=='TEX_IMAGE' and node.inputs['Vector'].is_linked is False:
            uv=value_node(mat,'ShaderNodeUVMap',uv_map='UVMap')
            mat.node_tree.links.new(uv.outputs['UV'],node.inputs['Vector'])
    shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    if name=='steel':
        old=shader.inputs['Base Color'].links[0].from_socket
        separate=value_node(mat,'ShaderNodeSeparateColor');mat.node_tree.links.new(old,separate.inputs['Color'])
        difference=value_node(mat,'ShaderNodeMath',operation='SUBTRACT')
        mat.node_tree.links.new(separate.outputs['Red'],difference.inputs[0]);mat.node_tree.links.new(separate.outputs['Green'],difference.inputs[1])
        mask=value_node(mat,'ShaderNodeMath',operation='MULTIPLY',use_clamp=True);mask.inputs[1].default_value=10
        mat.node_tree.links.new(difference.outputs[0],mask.inputs[0])
        mix=value_node(mat,'ShaderNodeMixRGB',blend_type='MIX')
        mix.inputs[1].default_value=(.022,.027,.038,1);mix.inputs[2].default_value=(.65,.012,.024,1)
        mat.node_tree.links.new(mask.outputs[0],mix.inputs[0]);mat.node_tree.links.new(mix.outputs[0],shader.inputs['Base Color'])
        emission=value_node(mat,'ShaderNodeMixRGB',blend_type='MULTIPLY');emission.inputs[0].default_value=1
        emission.inputs[2].default_value=(.32,.002,.004,1);mat.node_tree.links.new(mask.outputs[0],emission.inputs[1])
        mat.node_tree.links.new(emission.outputs[0],shader.inputs['Emission Color']);shader.inputs['Emission Strength'].default_value=1
        metallic=.78;roughness=.27;emit=emission.outputs[0]
    else:
        if name=='plate_in': colour=(.027,.034,.035,1);metallic=.72;roughness=.38
        else: colour=(.58,.32,.065,1);metallic=.82;roughness=.3
        for link in list(shader.inputs['Base Color'].links):mat.node_tree.links.remove(link)
        shader.inputs['Base Color'].default_value=colour;emit=None
    for key,value in [('Metallic',metallic),('Roughness',roughness)]:
        for link in list(shader.inputs[key].links):mat.node_tree.links.remove(link)
        shader.inputs[key].default_value=value
    styles[mat.name]=(shader,metallic,1-roughness,emit)
    if name in ('mental_grib','long_grib','bottom_grib'):
        for v in o.data.vertices:v.co.y=(v.co.y+.081)*.73-.081

# Replace the straight bands with a fitted white wrap and real diamond openings.
old_wrap=bpy.data.objects['Object_12'];parts.remove(old_wrap);bpy.data.objects.remove(old_wrap,do_unlink=True)
white=bpy.data.materials.new('White woven silk');white.use_nodes=True
bsdf=white.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(.88,.87,.83,1)
bsdf.inputs['Roughness'].default_value=.64
noise=value_node(white,'ShaderNodeTexNoise');noise.inputs['Scale'].default_value=1650;noise.inputs['Detail'].default_value=2
coord=value_node(white,'ShaderNodeTexCoord');white.node_tree.links.new(coord.outputs['Position'] if 'Position' in coord.outputs else coord.outputs['Object'],noise.inputs['Vector'])
bump=value_node(white,'ShaderNodeBump');bump.inputs['Strength'].default_value=.2;bump.inputs['Distance'].default_value=.00015
white.node_tree.links.new(noise.outputs['Fac'],bump.inputs['Height']);white.node_tree.links.new(bump.outputs[0],bsdf.inputs['Normal'])
styles[white.name]=(bsdf,0,.36,None)
vertices=[];faces=[];count=10;pitch=3.62/count
for i in range(count):
    block_start=len(vertices)
    centre=-3.45+(i+.5)*pitch
    outer=[(centre-pitch*.5,-.365),(centre+pitch*.5,-.365),(centre+pitch*.5,.365),(centre-pitch*.5,.365)]
    inner=[(centre-.10,0),(centre,-.085),(centre+.10,0),(centre,.085)]
    for sign in (1,-1):
        start=len(vertices)
        for x,y in outer+inner:
            vertices.append((x,y-.081,sign*.18))
        for j in range(4):
            face=(start+j,start+(j+1)%4,start+4+(j+1)%4,start+4+j)
            faces.append(face if sign>0 else tuple(reversed(face)))
    faces.extend([(block_start,block_start+8,block_start+9,block_start+1),
                  (block_start+2,block_start+10,block_start+11,block_start+3)])
mesh=bpy.data.meshes.new('Fitted crossed silk wrap');mesh.from_pydata(vertices,[],faces);mesh.update()
wrap=bpy.data.objects.new('White diamond wrap',mesh);bpy.context.collection.objects.link(wrap);mesh.materials.append(white)
bpy.ops.object.select_all(action='DESELECT');wrap.select_set(True);bpy.context.view_layer.objects.active=wrap
# Weld adjoining tiles before adding the thin cloth rim around each opening.
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=bm.verts,dist=.00001)
bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
solid=wrap.modifiers.new('Cloth thickness','SOLIDIFY');solid.thickness=.014;solid.offset=-1
bpy.ops.object.modifier_apply(modifier=solid.name)
bevel=wrap.modifiers.new('Soft cloth rims','BEVEL');bevel.width=.004;bevel.segments=2
bpy.ops.object.modifier_apply(modifier=bevel.name)
for p in mesh.polygons:p.use_smooth=True
parts.append(wrap)

scale=1.05/(17.73050499+3.84150243)
for o in parts:
    for v in o.data.vertices:
        x,y,z=v.co;v.co=Vector((y+.081,z,x+1.55))*scale
    # Unity needs only the final atlas; source UV sets remain explicit for baking.
    o.data.update()
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();katana=bpy.context.object
katana.name='RedNichirinKatana';katana.data.name='RedNichirinKatanaMesh'
for o in list(bpy.context.scene.objects):
    if o!=katana:bpy.data.objects.remove(o,do_unlink=True)
triangles=sum(len(p.vertices)-2 for p in katana.data.polygons)
assert triangles<10000 and all(math.isfinite(c) for v in katana.data.vertices for c in v.co)
assert 1.049<max(v.co.z for v in katana.data.vertices)-min(v.co.z for v in katana.data.vertices)<1.051

scene=bpy.context.scene
scene.world=bpy.data.worlds.new('Dark studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.11,.13,.17,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
def area(name,position,energy,size):
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=position
    obj.rotation_euler=(Vector((0,0,.4))-obj.location).to_track_quat('-Z','Y').to_euler()
area('Soft key',(-.4,-.6,.9),65,1.2);area('White edge reflection',(.1,.7,.7),100,1.0);area('Handle fill',(.5,-.2,-.2),24,.6)
data=bpy.data.cameras.new('Katana review');camera=bpy.data.objects.new('Katana review',data);scene.collection.objects.link(camera);scene.camera=camera
data.type='ORTHO';data.ortho_scale=1.2
centre=Vector((-.035,0,.34));camera.location=centre+Vector((.05,-2,.4))
camera.rotation_euler=((centre-camera.location).to_track_quat('-Z','Y')@Quaternion(Vector((0,0,1)),math.pi/2)).to_euler()
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1600;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'

if '--preview-only' not in sys.argv:
    atlas=katana.data.uv_layers.new(name='AtlasUV');katana.data.uv_layers.active=atlas;atlas.active_render=True
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.006,area_weight=.3,correct_aspect=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    scene.cycles.samples=1;scene.render.bake.margin=8
    saved_outputs={}
    materials=[slot.material for slot in katana.material_slots]
    for mat in materials:
        output=next(n for n in mat.node_tree.nodes if n.type=='OUTPUT_MATERIAL');saved_outputs[mat.name]=output.inputs['Surface'].links[0].from_socket
    maps={}
    for label,size in [('Albedo',2048),('Normal',1024),('MetallicSmoothness',1024),('Emission',1024)]:
        image=bpy.data.images.new('Katana_'+label,width=size,height=size,alpha=True)
        image.colorspace_settings.name='sRGB' if label in ('Albedo','Emission') else 'Non-Color'
        for mat in materials:
            shader,metal,smooth,emit=styles[mat.name];output=next(n for n in mat.node_tree.nodes if n.type=='OUTPUT_MATERIAL')
            target=value_node(mat,'ShaderNodeTexImage',image=image);mat.node_tree.nodes.active=target
            if label=='Normal':mat.node_tree.links.new(saved_outputs[mat.name],output.inputs['Surface'])
            else:
                emission=value_node(mat,'ShaderNodeEmission')
                if label=='Albedo':
                    colour=shader.inputs['Base Color']
                    if colour.is_linked:mat.node_tree.links.new(colour.links[0].from_socket,emission.inputs['Color'])
                    else:emission.inputs['Color'].default_value=colour.default_value
                elif label=='MetallicSmoothness':emission.inputs['Color'].default_value=(metal,smooth,0,1)
                elif emit:mat.node_tree.links.new(emit,emission.inputs['Color'])
                else:emission.inputs['Color'].default_value=(0,0,0,1)
                mat.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
        bpy.ops.object.bake(type='NORMAL' if label=='Normal' else 'EMIT')
        if label=='MetallicSmoothness':
            pixels=np.empty(size*size*4,dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape((-1,4))
            pixels[:,3]=pixels[:,1];pixels[:,1:3]=pixels[:,0:1];image.pixels.foreach_set(pixels.ravel())
        image.filepath_raw=str(dest/f'Katana_{label}.png');image.file_format='PNG';image.save();maps[label]=image
        for mat in materials:
            output=next(n for n in mat.node_tree.nodes if n.type=='OUTPUT_MATERIAL');mat.node_tree.links.new(saved_outputs[mat.name],output.inputs['Surface'])
    material=bpy.data.materials.new('RedNichirinKatana');material.use_nodes=True
    shader=material.node_tree.nodes.get('Principled BSDF')
    for label,socket in [('Albedo','Base Color'),('Emission','Emission Color')]:
        node=value_node(material,'ShaderNodeTexImage',image=maps[label]);material.node_tree.links.new(node.outputs['Color'],shader.inputs[socket])
    shader.inputs['Emission Strength'].default_value=1
    packed=value_node(material,'ShaderNodeTexImage',image=maps['MetallicSmoothness']);separate=value_node(material,'ShaderNodeSeparateColor')
    material.node_tree.links.new(packed.outputs['Color'],separate.inputs['Color']);material.node_tree.links.new(separate.outputs[0],shader.inputs['Metallic'])
    rough=value_node(material,'ShaderNodeMath',operation='SUBTRACT');rough.inputs[0].default_value=1
    material.node_tree.links.new(packed.outputs['Alpha'],rough.inputs[1]);material.node_tree.links.new(rough.outputs[0],shader.inputs['Roughness'])
    normal=value_node(material,'ShaderNodeTexImage',image=maps['Normal']);convert=value_node(material,'ShaderNodeNormalMap')
    material.node_tree.links.new(normal.outputs['Color'],convert.inputs['Color']);material.node_tree.links.new(convert.outputs[0],shader.inputs['Normal'])
    for name in [layer.name for layer in katana.data.uv_layers if layer.name!='AtlasUV']:
        katana.data.uv_layers.remove(katana.data.uv_layers[name])
    katana.data.materials.clear();katana.data.materials.append(material)
    for face in katana.data.polygons:face.material_index=0
    bpy.ops.wm.save_as_mainfile(filepath=str(source/'RedNichirinKatana.blend'))
    bpy.ops.export_scene.fbx(filepath=str(dest/'RedNichirinKatana.fbx'),use_selection=True,object_types={'MESH'},
        add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='RELATIVE',embed_textures=False)
    bpy.ops.export_scene.gltf(filepath=str(source/'RedNichirinKatana.glb'),export_format='GLB',use_selection=True)
    source.joinpath('build-report.json').write_text(json.dumps({'length_metres':1.05,'triangles':triangles,'material_slots':1,
        'grip_pivot':True,'original_preserved':True,'white_crossed_wrap':True,'maps':{label:list(image.size) for label,image in maps.items()}},indent=2))
scene.cycles.samples=24
scene.render.filepath=str(source/'katana-preview.png');bpy.ops.render.render(write_still=True)
data.ortho_scale=.30;centre=Vector((0,0,0));camera.location=centre+Vector((.05,-2,.4))
camera.rotation_euler=((centre-camera.location).to_track_quat('-Z','Y')@Quaternion(Vector((0,0,1)),math.pi/2)).to_euler()
scene.render.resolution_x=1400;scene.render.resolution_y=850
scene.render.filepath=str(source/'katana-handle-preview.png');bpy.ops.render.render(write_still=True)
print('KATANA BUILT:',triangles,'triangles')
