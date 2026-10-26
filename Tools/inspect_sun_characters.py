import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'SourceArt/Characters/Inspection'
report = {}
for name in ('Tanjiro', 'Yoriichi'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT / f'SourceArt/Characters/Originals/{name}.fbx'))
    objects = list(bpy.context.scene.objects)
    meshes = [obj for obj in objects if obj.type == 'MESH']
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    data = {'bounds': [list(low), list(high)], 'objects': [], 'images': []}
    for obj in objects:
        entry = {'name': obj.name, 'type': obj.type, 'parent': obj.parent.name if obj.parent else None,
                 'location': list(obj.location), 'rotation': list(obj.rotation_euler), 'scale': list(obj.scale)}
        if obj.type == 'ARMATURE':
            entry['bones'] = [{'name': bone.name, 'parent': bone.parent.name if bone.parent else None,
                'head': list(obj.matrix_world @ bone.head_local), 'tail': list(obj.matrix_world @ bone.tail_local)} for bone in obj.data.bones]
        if obj.type == 'MESH':
            obj.data.calc_loop_triangles()
            entry.update(vertices=len(obj.data.vertices), triangles=len(obj.data.loop_triangles),
                groups=[group.name for group in obj.vertex_groups],
                modifiers=[{'type': modifier.type, 'target': modifier.object.name if modifier.type == 'ARMATURE' and modifier.object else None} for modifier in obj.modifiers],
                materials=[slot.material.name if slot.material else None for slot in obj.material_slots])
        data['objects'].append(entry)
    for image in bpy.data.images:
        data['images'].append({'name': image.name, 'size': list(image.size), 'path': image.filepath, 'packed': bool(image.packed_file)})
    report[name] = data
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('Inspection World')
    scene.world.color = (.14, .14, .14)
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.background_type = 'WORLD'
    scene.display.shading.show_specular_highlight = False
    camera_data = bpy.data.cameras.new('Inspection camera')
    camera = bpy.data.objects.new('Inspection camera', camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    centre = (low + high) * .5
    extent = max(high - low)
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = extent * 1.2
    camera_data.clip_end = extent * 20
    for label, direction in [('front', Vector((0, -1, 0))), ('side', Vector((1, 0, 0))), ('back', Vector((0, 1, 0)))]:
        camera.location = centre + direction * extent * 3
        camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(OUT / f'{name}-{label}.png')
        bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / f'{name}-inspection.blend'))
OUT.joinpath('inspection.json').write_text(json.dumps(report, indent=2))
print(json.dumps({name: {'bounds': value['bounds'], 'meshes': len([o for o in value['objects'] if o['type']=='MESH']),
    'armatures': len([o for o in value['objects'] if o['type']=='ARMATURE']), 'images': value['images']} for name, value in report.items()}, indent=2))
