"""Prepare the current supplied modules, preserving authored surfaces and UVs."""
import argparse
import json
from collections import defaultdict
from pathlib import Path
import sys

import bmesh
import bpy
from mathutils import Vector

NAMES = ['CorrectedRoom', 'HallWayWithTurn1', 'HallWay3Way', 'DoubleStaresBase', 'SingleStareBase']


def height_band(mesh, low, high):
    bins = defaultdict(float)
    for face in mesh.polygons:
        if face.normal.z > .8 and low <= face.center.z <= high:
            bins[round(face.center.z / .003)] += face.area
    if not bins:
        raise RuntimeError('No architectural floor surface')
    return max(bins, key=bins.get) * .003


def export(obj, path):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH'},
        axis_forward='-Z', axis_up='Y', global_scale=1, apply_scale_options='FBX_SCALE_ALL',
        bake_anim=False, path_mode='RELATIVE')


def prepare(source, output, fallback):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(source))
    obj = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    mesh = obj.data
    mesh.update()
    minimum = Vector([min(v.co[i] for v in mesh.vertices) for i in range(3)])
    maximum = Vector([max(v.co[i] for v in mesh.vertices) for i in range(3)])
    span = maximum - minimum
    stair = 'Stare' in source.stem
    floor = height_band(mesh, minimum.z, maximum.z if stair else minimum.z + span.z * .4)
    roof = floor if stair else height_band(mesh, minimum.z + span.z * .55, maximum.z)
    factor = 9.0 if stair else 3.5 / max(.1, roof - floor)
    centre = (minimum + maximum) * .5
    for vertex in mesh.vertices:
        vertex.co = (vertex.co - Vector((centre.x, centre.y, floor))) * factor
    mesh.update()
    roof = (roof - floor) * factor
    folder = output / source.stem
    textures = folder / 'Textures'
    textures.mkdir(parents=True, exist_ok=True)
    for image in bpy.data.images:
        target = textures / Path(image.name).name
        if image.packed_file:
            target.write_bytes(image.packed_file.data)
        elif (fallback / Path(image.name).name).exists():
            target.write_bytes((fallback / Path(image.name).name).read_bytes())
        else:
            continue
        image.filepath = str(target)
        if image.packed_file:
            image.unpack(method='REMOVE')
        image.reload()

    if not stair:
        # Cut only roof-rail gates; native floors and corner posts remain intact.
        bm = bmesh.new()
        bm.from_mesh(mesh)
        for axis in (0, 1):
            for edge in (-1.1, 1.1):
                normal = [0, 0, 0]
                normal[axis] = 1
                plane = [0, 0, 0]
                plane[axis] = edge
                bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                    plane_co=plane, plane_no=normal, dist=.00001)
        gates = [f for f in bm.faces if min(v.co.z for v in f.verts) > roof + .04 and
            any(min(v.co[a] for v in f.verts) >= -1.101 and max(v.co[a] for v in f.verts) <= 1.101 for a in (0, 1))]
        bmesh.ops.delete(bm, geom=gates, context='FACES')
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()

    if source.stem == 'HallWayWithTurn1':
        # The authored elbow retains a wall between its two arms. Open that
        # junction in the derivative, preserving floors, ceiling and outside posts.
        bm = bmesh.new()
        bm.from_mesh(mesh)
        for axis, edges in ((0, (-1.65, -.75, .75, 1.65)), (1, (-3, -1, 1, 3)), (2, (.04, 2.7))):
            for edge in edges:
                normal = [0, 0, 0]
                normal[axis] = 1
                plane = [0, 0, 0]
                plane[axis] = edge
                bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                    plane_co=plane, plane_no=normal, dist=.00001)
        panels = [f for f in bm.faces if all(.039 <= v.co.z <= 2.701 and
            .749 <= abs(v.co.x) <= 1.651 and .999 <= abs(v.co.y) <= 3.001 for v in f.verts)]
        bmesh.ops.delete(bm, geom=panels, context='FACES')
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()

    mesh.calc_loop_triangles()
    report = {'source': source.name, 'scale': factor, 'roof_height': roof,
        'kind': 'Stair' if stair else 'Room' if source.stem == 'CorrectedRoom' else 'Corridor',
        'source_triangles': len(mesh.loop_triangles), 'lods': []}
    original = mesh.copy()
    for level, ratio in enumerate((.38, .15, .05)):
        obj.data = original.copy()
        modifier = obj.modifiers.new('Shared mobile LOD', 'DECIMATE')
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.name = source.stem + '_LOD' + str(level)
        obj.data.calc_loop_triangles()
        export(obj, folder / (obj.name + '.fbx'))
        report['lods'].append(len(obj.data.loop_triangles))
    obj.data = original.copy()
    obj.name = source.stem + '_Collision'
    export(obj, folder / (obj.name + '.fbx'))
    obj.data = original.copy()
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    if stair:
        remove = [f for f in bm.faces if abs(f.normal.z) < .65 or f.calc_center_median().z > .12]
    else:
        remove = [f for f in bm.faces if f.normal.z < .65 or abs(f.calc_center_median().z) > .12]
    bmesh.ops.delete(bm, geom=remove, context='FACES')
    if stair:
        # Imported open stair faces have inconsistent winding; keep their exact footprint.
        bmesh.ops.reverse_faces(bm, faces=[f for f in bm.faces if f.normal.z < 0])
    else:
        for vertex in bm.verts:
            vertex.co.z = 0
    bm.to_mesh(obj.data)
    bm.free()
    obj.name = source.stem + '_Floor'
    export(obj, folder / (obj.name + '.fbx'))
    (folder / 'Inspection.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report), flush=True)


parser = argparse.ArgumentParser()
parser.add_argument('--input', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--only', choices=NAMES)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
output = Path(args.output).resolve()
for name in ([args.only] if args.only else NAMES):
    prepare(Path(args.input).resolve() / (name + '.fbx'), output,
        output / 'CorrectedRoom' / 'Textures')
