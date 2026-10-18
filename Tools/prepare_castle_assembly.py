"""Blender: cut visual-only timber pieces; preserve the mobile atlas and collision sources."""
from pathlib import Path
import bisect
import json
import sys
import bpy
import bmesh
from mathutils import Vector

root = Path(__file__).resolve().parents[1]
output = root / 'Assets/InfinityCastle/CastleModules/RuntimeAssembly'
output.mkdir(parents=True, exist_ok=True)
names = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [
    'CorrectedRoom', 'HallWay3Way', 'HallWayWithTurnFixed', 'DoubleStaresBase', 'SingleStareBase', 'Path', 'Stares']
for name in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(root / 'Assets/InfinityCastle/CastleModules/RuntimeMobile' / (name + '.fbx')))
    obj = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    source = obj.data
    low = Vector(tuple(min(v.co[i] for v in source.vertices) for i in range(3)))
    high = Vector(tuple(max(v.co[i] for v in source.vertices) for i in range(3)))
    up = max(range(3), key=lambda i: abs((obj.matrix_world.to_3x3() @ Vector(tuple(int(a == i) for a in range(3)))).z))
    lateral = [a for a in range(3) if a != up]
    # Long floor/roof strips, tall post sections and larger wall panels, rather than tiny cubic voxels.
    divisions = {lateral[0]: 10, lateral[1]: 2, up: 3}
    planes = {a: [low[a] + (high[a] - low[a]) * f for f in
        ((.16, .78) if a == up else tuple(i / divisions[a] for i in range(1, divisions[a])))] for a in range(3)}
    mesh = bmesh.new(); mesh.from_mesh(source)
    for axis, cuts in planes.items():
        normal = Vector(tuple(int(a == axis) for a in range(3)))
        for cut in cuts:
            point = normal * cut
            bmesh.ops.bisect_plane(mesh, geom=list(mesh.verts) + list(mesh.edges) + list(mesh.faces),
                plane_co=point, plane_no=normal, dist=.0000001)
    face_layer = mesh.faces.layers.int.new('piece')
    groups = {}
    for face in mesh.faces:
        centre = face.calc_center_median()
        key = tuple(bisect.bisect_right(planes[a], centre[a]) for a in range(3))
        groups.setdefault(key, []).append(face)
    for index, faces in enumerate(groups.values()):
        for face in faces:
            face[face_layer] = index
    positions, polygons, uv_values, ids = [], [], [], []
    for index, key in enumerate(groups):
        piece = mesh.copy()
        tag = piece.faces.layers.int.get('piece')
        bmesh.ops.delete(piece, geom=[f for f in piece.faces if f[tag] != index], context='FACES')
        piece.verts.ensure_lookup_table()
        boundaries = [e for e in piece.edges if e.is_boundary]
        if boundaries:
            bmesh.ops.holes_fill(piece, edges=boundaries, sides=0)
        piece.normal_update()
        uv = piece.loops.layers.uv.active
        start = len(positions)
        for vertex in piece.verts:
            vertex.index = len(positions) - start
            positions.append(tuple(vertex.co))
        # Encode phase with the cell ID; Unity bakes actual imported-space centres into UV1.
        phase = key[up] * .24 + ((index * .61803398875) % 1) * .2
        for face in piece.faces:
            polygons.append(tuple(start + v.index for v in face.verts))
            uv_values.extend(tuple(loop[uv].uv) for loop in face.loops)
            ids.extend([(float(index), phase)] * len(face.loops))
        piece.free()
    result = bpy.data.meshes.new(name + ' Rigid Timber Pieces')
    result.from_pydata(positions, [], polygons); result.update()
    albedo = result.uv_layers.new(name='Albedo')
    pieces = result.uv_layers.new(name='AssemblyId')
    for loop, base, tag in zip(range(len(result.loops)), uv_values, ids):
        albedo.data[loop].uv = base; pieces.data[loop].uv = tag
    obj.data = result
    result.calc_loop_triangles()
    assert len(result.loop_triangles) <= 24000, (name, len(result.loop_triangles))
    assert len(groups) > 20 and len(result.uv_layers) == 2
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    staging = root / 'Temp' / (name + '_Assembly.fbx')
    bpy.ops.export_scene.fbx(filepath=str(staging), use_selection=True, object_types={'MESH'},
        axis_forward='-Z', axis_up='Y', bake_anim=False, path_mode='STRIP', embed_textures=False)
    staging.replace(output / staging.name)
    report = {'model': name, 'pieces': len(groups), 'visual_triangles': len(result.loop_triangles),
        'collision_triangles': 6000, 'textures': 'Existing mobile atlas; UV0 preserved'}
    (output / (name + '.json')).write_text(json.dumps(report, indent=2))
    print('ASSEMBLY', json.dumps(report), flush=True)
    mesh.free()
