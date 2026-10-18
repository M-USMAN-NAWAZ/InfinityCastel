"""Bake current supplied FBXs into complete, atlas-correct mobile distance LODs."""
from pathlib import Path
import bpy
import sys

root = Path(__file__).resolve().parents[1]
target = root / 'Assets/InfinityCastle/CastleModules/RuntimeLod'
target.mkdir(parents=True, exist_ok=True)
staging = root / 'Temp/DistanceMeshBake'
staging.mkdir(parents=True, exist_ok=True)


def bake_lod(source, name, triangles, resolution, voxel=False):
    obj = source.copy()
    obj.data = source.data.copy()
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    span = max(max(v.co[i] for v in obj.data.vertices) - min(v.co[i] for v in obj.data.vertices) for i in range(3))
    if voxel:
        obj.data.remesh_voxel_size = span / 80
        bpy.ops.object.voxel_remesh()
    obj.data.calc_loop_triangles()
    modifier = obj.modifiers.new('Complete distance geometry', 'DECIMATE')
    modifier.ratio = min(1, triangles / len(obj.data.loop_triangles))
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)

    # A collapsed mesh cannot reuse the source's atlas: merged vertices blend
    # unrelated UV islands into large stretched triangles. Bake fresh UVs instead.
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    image = bpy.data.images.new(name + ' colour', width=resolution, height=resolution)
    material = bpy.data.materials.new(name + ' atlas')
    material.use_nodes = True
    node = material.node_tree.nodes.new('ShaderNodeTexImage')
    node.image = image
    material.node_tree.nodes.active = node
    obj.data.materials.clear()
    obj.data.materials.append(material)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 1
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    scene.render.bake.use_selected_to_active = True
    scene.render.bake.cage_extrusion = span * .025
    scene.render.bake.margin = 4
    source.select_set(True)
    bpy.ops.object.bake(type='DIFFUSE')
    material.node_tree.links.new(node.outputs['Color'], material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    image.filepath_raw = str(staging / (name + 'Albedo.png'))
    image.file_format = 'PNG'
    image.save()
    (staging / (name + 'Albedo.png')).replace(target / (name + 'Albedo.png'))
    source.select_set(False)
    obj.data.calc_loop_triangles()
    print(name, len(obj.data.loop_triangles), 'triangles; rebaked', resolution, 'atlas', flush=True)
    # Publish complete files so an open Unity editor cannot import a partial FBX.
    bpy.ops.export_scene.fbx(filepath=str(staging / (name + '.fbx')), use_selection=True, object_types={'MESH'},
        axis_forward='-Z', axis_up='Y', bake_anim=False, path_mode='STRIP', embed_textures=False)
    (staging / (name + '.fbx')).replace(target / (name + '.fbx'))
    bpy.data.objects.remove(obj, do_unlink=True)


names = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else (
    'CorrectedRoom', 'HallWay3Way', 'DoubleStaresBase', 'SingleStareBase', 'Path', 'Stares', 'HallWayWithTurnFixed')
for name in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(root / 'Assets/Models/CastleModules' / (name + '.fbx')))
    for obj in list(bpy.context.scene.objects):
        if obj.type != 'MESH':
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        obj.data.calc_loop_triangles()
        print(name, len(obj.data.loop_triangles), 'source triangles', flush=True)
        bake_lod(obj, name + '_LOD2', 12500, 1024)
        if name == 'CorrectedRoom':
            bake_lod(obj, name + '_LOD3', 900, 512, voxel=True)
            # Preserve existing far-room atlas identity used by saved prefabs.
            (target / (name + '_LOD3Albedo.png')).replace(target / 'CorrectedRoom_DistrictAlbedo.png')
