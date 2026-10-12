"""Inspect supplied FBX artwork with Blender without modifying its geometry."""
import argparse
import json
from pathlib import Path

import bpy
from mathutils import Vector


def inspect(source, output):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(source))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    if not meshes:
        raise RuntimeError(f'No meshes in {source.name}')
    corners = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    minimum = Vector(tuple(min(point[i] for point in corners) for i in range(3)))
    maximum = Vector(tuple(max(point[i] for point in corners) for i in range(3)))
    centre, size = (minimum + maximum) * .5, maximum - minimum
    data = {'source': source.name, 'bounds_min': list(minimum), 'bounds_max': list(maximum),
            'dimensions': list(size), 'objects': [], 'images': []}
    for obj in meshes:
        obj.data.calc_loop_triangles()
        data['objects'].append({'name': obj.name, 'vertices': len(obj.data.vertices),
                                'triangles': len(obj.data.loop_triangles),
                                'materials': [slot.name for slot in obj.material_slots],
                                'dimensions': list(obj.dimensions)})
    for image in bpy.data.images:
        data['images'].append({'name': image.name, 'size': list(image.size),
                               'packed': image.packed_file is not None, 'path': image.filepath})
    output.mkdir(parents=True, exist_ok=True)
    (output / f'{source.stem}.json').write_text(json.dumps(data, indent=2))

    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = 'BOTH'
    scene.display.shading.show_specular_highlight = False
    scene.display.shading.background_type = 'WORLD'
    scene.world = bpy.data.worlds.new('Inspection world')
    scene.world.color = (.13, .15, .17)
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    camera_data = bpy.data.cameras.new('Inspection camera')
    camera = bpy.data.objects.new('Inspection camera', camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera_data.type = 'ORTHO'
    span = max(size)
    camera_data.clip_start = max(.001, span * .0001)
    camera_data.clip_end = span * 20
    for label, direction in [('overview', Vector((1.2, -1.8, 1.1))),
                              ('front', Vector((0, -2, .15))),
                              ('top', Vector((.01, -.01, 2)))]:
        camera.location = centre + direction.normalized() * span * 3
        camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Y').to_euler()
        bpy.context.view_layer.update()
        inverse = camera.matrix_world.inverted()
        projected = [inverse @ point for point in corners]
        height = max(p.y for p in projected) - min(p.y for p in projected)
        width = max(p.x for p in projected) - min(p.x for p in projected)
        camera_data.ortho_scale = max(height, width / 1.25) * 1.15
        scene.render.filepath = str(output / f'{source.stem}-{label}.png')
        bpy.ops.render.render(write_still=True)
    print(f'INSPECTED {source.name}: {len(meshes)} meshes, '
          f'{sum(obj["triangles"] for obj in data["objects"])} triangles, size={tuple(size)}', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--pattern', default='*.fbx')
    args = parser.parse_args(__import__('sys').argv[__import__('sys').argv.index('--') + 1:])
    for source in sorted(Path(args.input).resolve().rglob(args.pattern)):
        inspect(source, Path(args.output).resolve())
