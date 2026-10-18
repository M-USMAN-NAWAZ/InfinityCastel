"""Bake supplied FBXs into atlas-correct playable mobile meshes and distance LODs."""
from pathlib import Path
import argparse
import json
import time
import bpy
import sys

root = Path(__file__).resolve().parents[1]
target = root / 'Assets/InfinityCastle/CastleModules/RuntimeLod'
target.mkdir(parents=True, exist_ok=True)
staging = root / 'Temp/DistanceMeshBake'
staging.mkdir(parents=True, exist_ok=True)


def bake_lod(source, name, triangles, resolution, voxel=False, destination=target):
    obj = source.copy()
    obj.data = source.data.copy()
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    span = max(max(v.co[i] for v in obj.data.vertices) - min(v.co[i] for v in obj.data.vertices) for i in range(3))
    if voxel:
        obj.data.remesh_voxel_size = span / (80 if voxel is True else voxel)
        bpy.ops.object.voxel_remesh()
    obj.data.calc_loop_triangles()
    modifier = obj.modifiers.new('Complete distance geometry', 'DECIMATE')
    modifier.ratio = min(1, triangles / len(obj.data.loop_triangles))
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.calc_loop_triangles()
    count = len(obj.data.loop_triangles)
    assert 0 < count <= triangles + 8, (name, count, triangles)

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
    for polygon in obj.data.polygons:
        polygon.material_index = 0
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
    (staging / (name + 'Albedo.png')).replace(destination / (name + 'Albedo.png'))
    source.select_set(False)
    obj.data.calc_loop_triangles()
    print(name, len(obj.data.loop_triangles), 'triangles; rebaked', resolution, 'atlas', flush=True)
    assert len(obj.data.uv_layers) > 0 and len(obj.data.materials) == 1
    assert all(0 <= uv.uv.x <= 1.001 and 0 <= uv.uv.y <= 1.001 for uv in obj.data.uv_layers.active.data)
    # Publish complete files so an open Unity editor cannot import a partial FBX.
    bpy.ops.export_scene.fbx(filepath=str(staging / (name + '.fbx')), use_selection=True, object_types={'MESH'},
        axis_forward='-Z', axis_up='Y', bake_anim=False, path_mode='STRIP', embed_textures=False)
    (staging / (name + '.fbx')).replace(destination / (name + '.fbx'))
    bpy.data.objects.remove(obj, do_unlink=True)
    return {'triangles': count, 'atlas': resolution, 'materials': 1,
            'fbx_bytes': (destination / (name + '.fbx')).stat().st_size}


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--mobile', action='store_true', help='Bake both playable and distant mobile meshes; keep original FBXs untouched.')
parser.add_argument('--library-only', action='store_true', help='Package already-baked mobile meshes and textures in an editable .blend file.')
parser.add_argument('names', nargs='*', default=[])
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
names = args.names or ['CorrectedRoom', 'HallWay3Way', 'DoubleStaresBase', 'SingleStareBase', 'Path', 'Stares', 'HallWayWithTurnFixed']
destination = root / 'Assets/InfinityCastle/CastleModules/RuntimeMobile' if args.mobile else target
destination.mkdir(parents=True, exist_ok=True)
if args.library_only:
    if not args.mobile:
        parser.error('--library-only requires --mobile')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for index, name in enumerate(names):
        for level, suffix in enumerate(('', '_LOD2')):
            before = set(bpy.data.objects)
            bpy.ops.import_scene.fbx(filepath=str(destination / (name + suffix + '.fbx')))
            for obj in set(bpy.data.objects) - before:
                obj.name = name + (' Near' if level == 0 else ' Medium')
                obj.location.x += index % 3 * 1.3
                obj.location.y += index // 3 * 1.3
                obj.hide_set(level != 0)
                obj.hide_render = level != 0
    for image in bpy.data.images:
        if image.source == 'FILE':
            image.pack()
    bpy.context.scene.display.shading.color_type = 'TEXTURE'
    library = root / 'SourceArt/CastleMobile.blend'
    library.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(library))
    names = []
for name in names:
    started = time.monotonic()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(root / 'Assets/Models/CastleModules' / (name + '.fbx')))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    if len(meshes) != 1:
        raise RuntimeError(f'{name}: expected one supplied mesh, found {len(meshes)}')
    obj = meshes[0]
    obj.data.calc_loop_triangles()
    source_count = len(obj.data.loop_triangles)
    print(name, source_count, 'source triangles', flush=True)
    if args.mobile:
        report = {'source': name, 'source_triangles': source_count}
        report['near'] = bake_lod(obj, name, 6000, 1024, voxel=256, destination=destination)
        report['medium'] = bake_lod(obj, name + '_LOD2', 2500, 512, voxel=192, destination=destination)
        report['seconds'] = round(time.monotonic() - started, 2)
        (destination / (name + '.json')).write_text(json.dumps(report, indent=2))
        print('MOBILE_BAKE', json.dumps(report), flush=True)
    else:
        bake_lod(obj, name + '_LOD2', 12500, 1024)
        if name == 'CorrectedRoom':
            bake_lod(obj, name + '_LOD3', 900, 512, voxel=True)
            # Preserve existing far-room atlas identity used by saved prefabs.
            (target / (name + '_LOD3Albedo.png')).replace(target / 'CorrectedRoom_DistrictAlbedo.png')
