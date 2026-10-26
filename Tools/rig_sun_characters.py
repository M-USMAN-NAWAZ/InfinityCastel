"""Build matching humanoid skeletons with fitted shoulders and original UVs."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'Assets/Characters/SunBreathers'
SOURCE = ROOT / 'SourceArt/Characters'
SETTINGS = {
    'Tanjiro': dict(height=1.65, hips=.57, neck=.864, head=.90, shoulder=.143, elbow=.278,
        wrist=.405, arm_z=.846, arm_y=.006, palm=.455, tips=[.493,.513,.508,.496],
        finger_y=[-.016,-.001,.014,.025], thumb=[(.424,-.019,.840),(.444,-.040,.838),(.465,-.050,.837)],
        shoulder_inset=.022, shoulder_drop=.012, sleeve_drop=.057),
    'Yoriichi': dict(height=1.85, hips=.60, neck=.913, head=.945, shoulder=.135, elbow=.246,
        wrist=.352, arm_z=.895, arm_y=-.054, palm=.382, tips=[.428,.441,.433,.412],
        finger_y=[-.073,-.056,-.041,-.028], thumb=[(.365,-.071,.886),(.383,-.098,.882),(.401,-.117,.882)],
        shoulder_inset=.020, shoulder_drop=.012, sleeve_drop=.033)
}

def smoothstep(start, end, value):
    t = max(0.0, min(1.0, (value-start)/(end-start)))
    return t*t*(3-2*t)

def fit_shoulder(point, fit):
    # Shift the whole arm, not just the skin around the joint. Blend into the
    # chest below the collar; leave the face, lower torso, and UVs alone.
    value = Vector(point)
    armz = fit['arm_z']
    weight = (smoothstep(.06, fit['shoulder'], abs(value.x))
              * smoothstep(armz-.16, armz-.055, value.z)
              * (1-smoothstep(armz+.035, armz+.070, value.z)))
    value.x -= math.copysign(fit['shoulder_inset']*weight, value.x)
    value.z -= fit['shoulder_drop']*weight
    return value

shared_keys = None
report = {}
for name, fit in SETTINGS.items():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / f'Inspection/{name}-inspection.blend'))
    # Originals are preserved separately; a locked .blend1 must not prevent an
    # owned generated rig from rebuilding. This does not save user preferences.
    bpy.context.preferences.filepaths.save_version=0
    obj = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH')
    for other in list(bpy.context.scene.objects):
        if other != obj: bpy.data.objects.remove(other, do_unlink=True)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    original_height = max(vertex.co.z for vertex in obj.data.vertices)
    factor = fit['height'] / original_height
    original = [vertex.co.copy() for vertex in obj.data.vertices]
    original_uvs = [tuple(loop.uv) for layer in obj.data.uv_layers for loop in layer.data]
    for vertex in obj.data.vertices: vertex.co = fit_shoulder(vertex.co, fit)*factor
    obj.data.update()
    assert original_uvs == [tuple(loop.uv) for layer in obj.data.uv_layers for loop in layer.data]
    assert all((vertex.co-original[vertex.index]*factor).length < 1e-6
               for vertex in obj.data.vertices
               if original[vertex.index].z > fit['arm_z']+.071 or original[vertex.index].z < fit['arm_z']-.161)
    joint = fit_shoulder((fit['shoulder'],fit['arm_y']*.3,fit['arm_z']),fit)
    shoulder_ratio = joint.x/fit['shoulder']
    assert .82 < shoulder_ratio < .88, 'Shoulder fit must stay local and modest'
    obj.name = name + '_Body'
    obj.data.name = name + '_Skin'
    folder = DEST / name
    folder.mkdir(parents=True, exist_ok=True)
    for image in bpy.data.images:
        if image.packed_file:
            suffix = 'Albedo' if image.name.endswith('20250901.png') else image.name.rsplit('_',1)[-1].split('.')[0].title()
            image.filepath_raw = str(folder / f'{name}_{suffix}.png')
            image.file_format = 'PNG'
            image.save()
    for slot in obj.material_slots:
        if slot.material: slot.material.name = name + '_Material'
    armature = bpy.data.armatures.new('SunBreatherSkeleton')
    rig = bpy.data.objects.new('SunBreatherRig', armature)
    bpy.context.collection.objects.link(rig)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True); bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    bones = {}
    def bone(label, head, tail, parent=None, deform=True):
        value = armature.edit_bones.new(label)
        value.head = fit_shoulder(head, fit) * factor; value.tail = fit_shoulder(tail, fit) * factor
        if parent: value.parent = bones[parent]
        value.use_deform = deform
        value.align_roll(Vector((0,-1,0)))
        bones[label] = value
    hip = fit['hips']; neck = fit['neck']; armz = fit['arm_z']; army = fit['arm_y']
    bone('Root', (0,0,0), (0,0,.08), deform=False)
    bone('Hips', (0,0,hip), (0,0,hip+.06), 'Root')
    bone('Spine', (0,0,hip+.06), (0,0,hip+.14), 'Hips')
    bone('Chest', (0,0,hip+.14), (0,0,armz-.035), 'Spine')
    bone('UpperChest', (0,0,armz-.035), (0,0,neck), 'Chest')
    bone('Neck', (0,0,neck), (0,-.008,fit['head']), 'UpperChest')
    bone('Head', (0,-.008,fit['head']), (0,-.008,original_height-.03), 'Neck')
    for side, sign in [('Left',1),('Right',-1)]:
        def pos(x,y,z): return (x*sign,y,z)
        bone(side+'Shoulder', pos(.045,0,armz), pos(fit['shoulder'],army*.3,armz), 'UpperChest')
        bone(side+'UpperArm', pos(fit['shoulder'],army*.3,armz), pos(fit['elbow'],army*.7,armz), side+'Shoulder')
        bone(side+'LowerArm', pos(fit['elbow'],army*.7,armz), pos(fit['wrist'],army,armz), side+'UpperArm')
        bone(side+'Hand', pos(fit['wrist'],army,armz), pos(fit['palm'],army,armz), side+'LowerArm')
        for finger, end, y in zip(('Index','Middle','Ring','Little'),fit['tips'],fit['finger_y']):
            start = fit['palm']; step = (end-start)/3
            for part, label in enumerate(('Proximal','Intermediate','Distal')):
                bone(side+finger+label, pos(start+step*part,y,armz+.003), pos(start+step*(part+1),y,armz+.003),
                     side+'Hand' if part==0 else side+finger+('Proximal' if part==1 else 'Intermediate'))
        thumb = [Vector(p) for p in fit['thumb']]
        thumb.append(thumb[-1]+(thumb[-1]-thumb[-2])*.45)
        for part, label in enumerate(('Proximal','Intermediate','Distal')):
            bone(side+'Thumb'+label, pos(*thumb[part]), pos(*thumb[part+1]),
                 side+'Hand' if part==0 else side+'Thumb'+('Proximal' if part==1 else 'Intermediate'))
        x = .108 if name=='Tanjiro' else .10
        bone(side+'UpperLeg', pos(x,0,hip), pos(x*1.12,-.012,.31), 'Hips')
        bone(side+'LowerLeg', pos(x*1.12,-.012,.31), pos(x*1.18,.005,.069), side+'UpperLeg')
        bone(side+'Foot', pos(x*1.18,.005,.069), pos(x*1.18,-.067,.026), side+'LowerLeg')
        bone(side+'Toes', pos(x*1.18,-.067,.026), pos(x*1.18,-.112,.024), side+'Foot')
        bone(side+'SwordSocket', pos(fit['palm']-.01,army-.016,armz), pos(fit['palm']-.01,army-.08,armz), side+'Hand',False)
    hierarchy = {value.name: value.parent.name if value.parent else None for value in armature.edit_bones}
    bpy.ops.object.mode_set(mode='OBJECT')
    # Blender's native bone-heat solver respects the supplied connected surface.
    # Four normalized influences are exported per vertex for Unity skinning.
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    bpy.context.view_layer.objects.active = obj
    # Gather the long underarm panels into the actual sleeve surface in bind
    # space. World-space garment anchors pulled these panels into hollow hoops.
    # Keep the shoulder/cuff attached, the original UVs, and ordinary arm bones.
    sleeve_masks=[]
    compressed_depths=[]
    colour=obj.data.color_attributes.new(name='SleeveMask',type='FLOAT_COLOR',domain='POINT')
    for vertex in obj.data.vertices:
        point=original[vertex.index]; x=abs(point.x); side='Left' if point.x>0 else 'Right'
        weights={obj.vertex_groups[g.group].name:g.weight for g in vertex.groups}
        arm_share=sum(weights.get(side+part,0) for part in ('UpperArm','LowerArm'))
        loose=(1-smoothstep(armz-.09,armz-.025,point.z))*smoothstep(.10,.15,x)
        loose*=smoothstep(.30,.70,arm_share)
        loose=max(0,min(1,loose)); sleeve_masks.append(loose)
        colour.data[vertex.index].color=(loose,1 if side=='Left' else 0,0,1)
        if loose<.001: continue
        depth=armz-point.z
        # Smoothly compress the excess drop to roughly 9 cm in game units.
        # Small existing folds survive; no disconnected strips or extra cloth
        # simulation are introduced. The mapping stays monotonic (no inversion).
        start=min(.032,fit['sleeve_drop']*.6)
        gathered=start+(fit['sleeve_drop']-start)*(1-math.exp(-max(0,depth-start)/.045))
        target=fit_shoulder((point.x,point.y,armz),fit).z-min(depth,gathered)
        vertex.co.z=vertex.co.z*(1-loose)+target*factor*loose
        if loose>.95: compressed_depths.append((fit_shoulder((point.x,point.y,armz),fit)*factor).z-vertex.co.z)
        # Deep hem vertices must bend with the corresponding arm segment, not
        # inherit inconsistent heat weights from their old waist-height shape.
        blend=smoothstep(fit['elbow']-.035,fit['elbow']+.035,x)
        for group in list(vertex.groups):
            obj.vertex_groups[group.group].add([vertex.index],group.weight*(1-loose),'REPLACE')
        for label,weight in [('UpperArm',loose*(1-blend)),('LowerArm',loose*blend)]:
            if weight>.0001:
                group=obj.vertex_groups[side+label]
                group.add([vertex.index],weights.get(side+label,0)*(1-loose)+weight,'REPLACE')
    assert sum(mask>.5 for mask in sleeve_masks)>500, 'Loose garment mask is missing'
    assert max(compressed_depths)<.11, f'Sleeve underside still hangs away from the arm: {max(compressed_depths):.3f}m'
    assert original_uvs == [tuple(loop.uv) for layer in obj.data.uv_layers for loop in layer.data]
    obj.data.update()
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    unweighted = [v.index for v in obj.data.vertices if sum(g.weight for g in v.groups)<.99]
    if unweighted: raise RuntimeError(f'{name}: unweighted vertices: {len(unweighted)}')
    bind = [v.co.copy() for v in obj.data.vertices]
    # One shared diagnostic action, not a claimed breathing-technique animation.
    if shared_keys is None:
        def turn(label, axis, degrees):
            rest = rig.data.bones[label].matrix_local.to_quaternion()
            return list(rest.inverted() @ Quaternion(Vector(axis), math.radians(degrees)) @ rest)
        shared_keys = {1:{}, 21:{}, 41:{}, 61:{}}
        for frame, sign in [(21,1),(41,-1)]:
            pose = shared_keys[frame]
            for side, direction in [('Left',1),('Right',-1)]:
                pose[side+'UpperArm'] = turn(side+'UpperArm',(0,1,0),direction*55)
                pose[side+'LowerArm'] = turn(side+'LowerArm',(0,0,1),-direction*(65 if direction==sign else 35))
                if direction==sign:
                    pose[side+'UpperLeg'] = turn(side+'UpperLeg',(1,0,0),-35)
                    pose[side+'LowerLeg'] = turn(side+'LowerLeg',(1,0,0),60)
                for finger in ('Index','Middle','Ring','Little'):
                    for part in ('Proximal','Intermediate','Distal'):
                        label = side+finger+part
                        pose[label] = turn(label,(0,1,0),direction*35)
            pose['Chest'] = turn('Chest',(0,0,1),sign*18)
    for frame, values in shared_keys.items():
        for value in rig.pose.bones:
            value.rotation_mode='QUATERNION'
            value.rotation_quaternion=values.get(value.name,(1,0,0,0))
            value.keyframe_insert(data_path='rotation_quaternion',frame=frame,group=value.name)
    rig.animation_data.action.name='SharedRigCheck'
    scene = bpy.context.scene
    scene.render.fps=30; scene.frame_start=1; scene.frame_end=61; scene.frame_set(1)
    # Validate real deformation under the same action on both meshes.
    samples = []
    for frame in (1,21,41,61):
        scene.frame_set(frame); bpy.context.view_layer.update()
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        displacement = max((v.co-bind[v.index]).length for v in mesh.vertices)
        assert all(math.isfinite(c) for v in mesh.vertices for c in v.co)
        samples.append({'frame':frame,'maximum_displacement':displacement})
        evaluated.to_mesh_clear()
    assert samples[1]['maximum_displacement']>.1
    scene.frame_set(1)
    rig.show_in_front=True
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / f'{name}-SharedRig.blend'))
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(folder / f'{name}_Humanoid.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},
        add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,
        axis_forward='Z',axis_up='Y',path_mode='RELATIVE',embed_textures=False)
    # Render rest/bend checks with the original texture, not a generated likeness.
    scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.color_type='TEXTURE'
    camera_data=bpy.data.cameras.new('Rig review camera');camera=bpy.data.objects.new('Rig review camera',camera_data)
    scene.collection.objects.link(camera);scene.camera=camera;camera_data.type='ORTHO';camera_data.ortho_scale=fit['height']*1.25
    centre=Vector((0,0,fit['height']*.5));camera.location=centre+Vector((.3,-3,.1))*fit['height']
    camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    for frame,label in [(1,'rest'),(21,'bend'),(41,'opposite')]:
        scene.frame_set(frame);scene.render.filepath=str(SOURCE / f'Inspection/{name}-rig-{label}.png')
        bpy.ops.render.render(write_still=True)
    report[name]={'bones':hierarchy,'height_metres':fit['height'],'vertices':len(obj.data.vertices),
        'maximum_influences':max(len(v.groups) for v in obj.data.vertices),'unweighted_vertices':len(unweighted),
        'garment_anchors':0,'compressed_sleeve_vertices':sum(mask>.5 for mask in sleeve_masks),
        'maximum_compressed_drop_metres':max(compressed_depths),
        'deformation_samples':samples,
        'shoulder_fit':{'joint_width_before_metres':2*fit['shoulder']*factor,
                        'joint_width_after_metres':2*joint.x*factor,
                        'width_reduction_percent':100*(1-shoulder_ratio),
                        'drop_metres':fit['shoulder_drop']*factor,
                        'uvs_unchanged':True,'face_and_lower_body_unchanged':True}}
assert report['Tanjiro']['bones']==report['Yoriichi']['bones'], 'Skeleton hierarchy differs'
SOURCE.joinpath('shared-rig-report.json').write_text(json.dumps(report,indent=2))
print('MATCHING RIGS BUILT:', {name:len(data['bones']) for name,data in report.items()})
