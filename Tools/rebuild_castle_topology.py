"""Blender: rebuild the active architectural kit as UV-mapped structural quads.

Not a voxel remesh/decimate. Source FBXs are never edited. Dimensions and ports
are exported together so Unity cannot silently use the old scanned openings.
Run: blender --background --python Tools/rebuild_castle_topology.py
"""
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'Assets/InfinityCastle/CastleModules'
OUT = ART / 'Retopology'
MOBILE = ART / 'RuntimeMobile'
SOURCE = ROOT / 'SourceArt/CastleRetopology'
PREVIEW = ROOT / 'Temp/RetopologyPreview'
ATLAS = OUT / 'CastleTrimAlbedo.png'


def v3(v):
    return dict(zip(('x', 'y', 'z'), v))


def port(x, y, z, dx, dz, width=2.4):
    return {'position': v3((x, y, z)), 'outward': v3((dx, 0, dz)), 'width': width}


class Builder:
    """All input coordinates are Unity metres, Y up, positive Z forward."""
    def __init__(self, name, level):
        self.name, self.level = name, level
        self.vertices, self.faces, self.tiles = [], [], []
        self.vertex_indices = {}
        self.boxes = set()

    def face(self, points, tile):
        if (Vector(points[1])-Vector(points[0])).cross(Vector(points[2])-Vector(points[0])).length < 1e-8:
            return
        # Right-handed Blender conversion; FBX -Z forward, Y up returns Unity metres.
        indices=[]
        for x,y,z in points:
            key=tuple(round(n,6) for n in (x,-z,y))
            if key not in self.vertex_indices:
                self.vertex_indices[key]=len(self.vertices);self.vertices.append(key)
            indices.append(self.vertex_indices[key])
        self.faces.append(tuple(indices))
        self.tiles.append(tile)

    def box(self, centre, size, tile=0, top=None):
        key=tuple(round(n,6) for n in (*centre,*size))+(tile,top)
        if key in self.boxes:return
        self.boxes.add(key)
        x, y, z = centre; a, b, c = [s / 2 for s in size]
        points = [(x-a,y-b,z-c),(x+a,y-b,z-c),(x+a,y+b,z-c),(x-a,y+b,z-c),
                  (x-a,y-b,z+c),(x+a,y-b,z+c),(x+a,y+b,z+c),(x-a,y+b,z+c)]
        for i, face in enumerate(((0,3,2,1),(4,5,6,7),(0,4,7,3),(1,2,6,5),(3,7,6,2),(0,1,5,4))):
            self.face([points[n] for n in face], top if i == 4 and top is not None else tile)

    def beam(self, a, b, width=.12, depth=None, tile=1):
        a, b = Vector(a), Vector(b); along = (b-a).normalized()
        side = along.cross(Vector((0, 1, 0)))
        if side.length < .01: side = Vector((1, 0, 0))
        side.normalize(); other = along.cross(side).normalized()
        side *= width / 2; other *= (depth or width) / 2
        p = [point + side*s + other*t for point in (a, b) for s,t in ((-1,-1),(1,-1),(1,1),(-1,1))]
        for face in ((0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)):
            self.face([tuple(p[n]) for n in face], tile)

    def post(self, x, z, bottom, top, lantern=False):
        self.box((x,(bottom+top)/2,z),(.23,top-bottom,.23))
        if self.level == 0:
            for y in (bottom+.12, top-.16):
                self.box((x,y,z),(.31,.16,.31),6)
            self.box((x,bottom+.035,z),(.36,.07,.36),3)
            if lantern:
                self.lantern(x+.19, top-.8, z-.19)

    def lantern(self, x, y, z):
        self.box((x,y,z),(.17,.52,.17),15)
        if self.level == 0:
            for yy in (y-.3,y+.3): self.box((x,yy,z),(.23,.08,.23),10)

    def rail(self, a, b, openings=()):
        a,b = Vector(a),Vector(b); delta=b-a; length=delta.length
        if length < .1: return
        tangent=delta/length
        # Openings are distances along this edge, keeping roof doors traversable.
        pieces=[(0.,length)]
        for low,high in openings:
            pieces=[sub for start,end in pieces for sub in ((start,min(end,low)),(max(start,high),end)) if sub[1]-sub[0]>.15]
        for low,high in pieces:
            start,end=a+tangent*low,a+tangent*high
            for h in (.4,.88): self.beam(tuple(start+Vector((0,h,0))),tuple(end+Vector((0,h,0))),.075,tile=0)
            count=max(1,math.ceil((high-low)/(1.8 if self.level == 0 else 4.)))
            for i in range(count+1):
                p=start+(end-start)*i/count
                self.box((p.x,p.y+.49,p.z),(.13,.98,.13))
                if self.level==0 and i in (0,count):
                    self.box((p.x,p.y+.91,p.z),(.18,.12,.18),6)

    def slab(self, rects, y, tile):
        xs=sorted({n for r in rects for n in (r[0],r[1])})
        zs=sorted({n for r in rects for n in (r[2],r[3])})
        # Regular floor patches give construction animation actual planks/sections.
        def divide(values):
            return sorted({round(a+(b-a)*i/max(1,math.ceil((b-a)/1.6)),6) for a,b in zip(values,values[1:]) for i in range(max(1,math.ceil((b-a)/1.6))+1)})
        xs,zs=divide(xs),divide(zs)
        inside=lambda x,z:any(a-1e-5<x<b+1e-5 and c-1e-5<z<d+1e-5 for a,b,c,d in rects)
        for x1,x2 in zip(xs,xs[1:]):
            for z1,z2 in zip(zs,zs[1:]):
                if not inside((x1+x2)/2,(z1+z2)/2): continue
                self.face([(x1,y,z1),(x1,y,z2),(x2,y,z2),(x2,y,z1)],tile)
                self.face([(x1,y-.17,z1),(x2,y-.17,z1),(x2,y-.17,z2),(x1,y-.17,z2)],3)
                for a,b,probe in (((x1,z1),(x2,z1),((x1+x2)/2,z1-.01)),((x2,z1),(x2,z2),(x2+.01,(z1+z2)/2)),
                                  ((x2,z2),(x1,z2),((x1+x2)/2,z2+.01)),((x1,z2),(x1,z1),(x1-.01,(z1+z2)/2))):
                    if not inside(*probe): self.face([(a[0],y,a[1]),(b[0],y,b[1]),(b[0],y-.17,b[1]),(a[0],y-.17,a[1])],1)

    def wall(self, a, b, doors, height=3.6):
        a,b=Vector((a[0],0,a[1])),Vector((b[0],0,b[1])); d=(b-a).normalized(); length=(b-a).length
        gaps=[]
        for door in doors:
            p=Vector(tuple(door['position'][k] for k in ('x','y','z')))
            t=(p-a).dot(d)
            if -.01<=t<=length+.01 and (p-a-d*t).length<.05:
                gaps.append((t-door['width']/2,t+door['width']/2))
        limits=sorted({0.,length,*[max(0,min(length,x)) for gap in gaps for x in gap]})
        # Subdivide solid walls into genuine separate shoji bays; no baked room facades.
        points={0.,length}
        for low,high in zip(limits,limits[1:]):
            door=any(g0<(low+high)/2<g1 for g0,g1 in gaps)
            count=1 if door or self.level>0 else max(1,math.ceil((high-low)/1.8))
            for i in range(count+1): points.add(low+(high-low)*i/count)
            if door: continue
            for i in range(count):
                c=a+d*(low+(high-low)*(i+.5)/count); w=(high-low)/count-.12
                size=(w,.65,.105) if abs(d.x)>.5 else (.105,.65,w)
                self.box((c.x,.43,c.z),size,14)
                size=(w,2.,.07) if abs(d.x)>.5 else (.07,2.,w)
                self.box((c.x,1.79,c.z),size,12)
                if self.level == 0:
                    for h in (.79,2.79): self.beam(tuple(c-d*w/2+Vector((0,h,0))),tuple(c+d*w/2+Vector((0,h,0))),.08)
        for i,t in enumerate(sorted(points)):
            p=a+d*t
            if self.level==0: self.post(p.x,p.z,0,height-.12,lantern=i%2==0)
            else:self.box((p.x,1.74,p.z),(.23,3.48,.23))
        for h,thick in ((.08,.13),(3.04,.16),(3.48,.23)):
            self.beam(tuple(a+Vector((0,h,0))),tuple(b+Vector((0,h,0))),.22,thick,0)
        c=(a+b)/2
        self.box((c.x,3.25,c.z),(length,.25,.08) if abs(d.x)>.5 else (.08,.25,length),9)
        if self.level == 0:
                self.box((c.x,3.56,c.z),(length,.09,.26) if abs(d.x)>.5 else (.26,.09,length),1)
        return gaps

    def architecture(self, polygon, rects, doors):
        self.slab(rects,0,8); self.slab(rects,3.6,2)
        for a,b in zip(polygon,polygon[1:]+polygon[:1]):
            gaps=self.wall(a,b,doors)
            # The roof rail shares every doorway's opening, never an invisible wall.
            if self.level < 2: self.rail((a[0],3.6,a[1]),(b[0],3.6,b[1]),gaps)

    def stairs(self, x, z0, z1, y0, y1, width, count):
        for i in range(count):
            t=(i+.5)/count; y=y0+(y1-y0)*(i+1)/count
            depth=abs(z1-z0)/count+.005
            self.box((x,y-.11,z0+(z1-z0)*t),(width,.22,depth),0,8)
        for side in (-1,1):
            xx=x+side*(width/2-.08)
            self.beam((xx,y0-.25,z0),(xx,y1-.25,z1),.14,.3)
            self.beam((xx,y0+.9,z0),(xx,y1+.9,z1),.10)
            self.beam((xx,y0+.45,z0),(xx,y1+.45,z1),.065)
            for i in range(5):
                t=i/4; self.post(xx,z0+(z1-z0)*t,y0+(y1-y0)*t,y0+(y1-y0)*t+.99)

    def object(self, material):
        mesh=bpy.data.meshes.new('Retopo_'+self.name+('_LOD2' if self.level==1 else '_LOD3' if self.level==2 else ''))
        mesh.from_pydata(self.vertices,[],self.faces); mesh.update()
        uv=mesh.uv_layers.new(name='AtlasUV')
        # Eight source pixels of padding inside each 256px runtime tile prevents mip bleed.
        inset=.008
        for poly,tile in zip(mesh.polygons,self.tiles):
            col,row=tile%4,tile//4
            coords=((0,0),(1,0),(1,1),(0,1))
            p=[mesh.vertices[i].co for i in poly.vertices]
            e0,e1=p[1]-p[0],p[2]-p[1]
            # Keep grain aligned to a beam/post and use a narrow UV strip for treads,
            # rather than squeezing four floorboards onto every 30cm step.
            rotate=(tile==0 and abs(e0.normalized().z)>.8) or (tile in (1,2,8) and e0.length<e1.length)
            if tile in (2,8) and abs(poly.normal.z)>.8:
                rotate=abs(e0.x)<abs(e1.x)  # All floor/roof boards follow X, not alternating patch aspect ratios.
            scale_v=min(1.,max(.08,(e0.length/max(e1.length,1e-6) if rotate else e1.length/max(e0.length,1e-6)))) if tile in (1,2,8) else 1.
            for j,loop in enumerate(poly.loop_indices):
                u,v=coords[(j+1 if rotate else j)%4];v=.5+(v-.5)*scale_v
                uv.data[loop].uv=(col/4+inset+u*(.25-2*inset),1-(row+1)/4+inset+v*(.25-2*inset))
        # Each structural part has flat, intentional hard edges, not noisy remeshed normals.
        obj=bpy.data.objects.new(mesh.name,mesh); bpy.context.collection.objects.link(obj)
        obj.data.materials.append(material)
        return obj


def create(name,level,material):
    b=Builder(name,level); doors=[]; kind=0; floor=(0,0,0); roof=(0,3.6,0)
    if name=='CorrectedRoom':
        doors=[port(0,0,3,0,1),port(3.6,0,0,1,0),port(-3.6,0,0,-1,0)]
        b.architecture([(-3.6,-3),(3.6,-3),(3.6,3),(-3.6,3)],[(-3.6,3.6,-3,3)],doors)
    elif name=='HallWayWithTurnFixed':
        kind=1; doors=[port(-2.4,0,4,0,1),port(4,0,-2.4,1,0)]
        b.architecture([(-4,-4),(4,-4),(4,-.8),(-.8,-.8),(-.8,4),(-4,4)],
                       [(-4,4,-4,-.8),(-4,-.8,-.8,4)],doors)
        floor=(-2.4,0,0)
    elif name=='HallWay3Way':
        kind=1; doors=[port(0,0,4,0,1),port(6,0,-2.4,1,0),port(-6,0,-2.4,-1,0)]
        b.architecture([(-6,-4),(6,-4),(6,-.8),(1.6,-.8),(1.6,4),(-1.6,4),(-1.6,-.8),(-6,-.8)],
                       [(-6,6,-4,-.8),(-1.6,1.6,-.8,4)],doors)
    elif name in ('SingleStareBase','DoubleStaresBase'):
        kind=3; double=name.startswith('Double'); w=4.5 if double else 3.8
        b.slab([(-w,w,-4,1)],0,8)
        for x in (-w+.2,w-.2):
            for z in (-3.8,.8): b.post(x,z,-2.2,0)
        # Side roof exit and stair entrance are intentionally unrailed.
        b.rail((-w,0,-4),(w,0,-4),[(w-1.2,w+1.2)])
        b.rail((-w,0,-4),(-w,0,1),[(1.8,4.8)])
        b.rail((w,0,-4),(w,0,1),[(1.8,4.8)])
        if double:
            for x in (-2.5,2.5): b.stairs(x,4,1,-1.8,0,2.25,12 if level==0 else 8)
            b.slab([(-3.7,3.7,3.8,4.2)],-1.8,8)
            doors=[port(-w,0,-.7,-1,0,2.4)];floor=(-w,0,-.7);roof=floor
        else:
            b.stairs(-.6,4,1,-1.4,0,2.4,10 if level==0 else 7)
            doors=[port(-.6,-1.4,4,0,1),port(0,0,-4,0,-1),port(w,0,-1,1,0),port(-w,0,-1,-1,0)]
            floor=(-.6,-1.4,4);roof=(0,0,-4)
    elif name=='Path':
        b.slab([(-1.2,1.2,-5.8,5.8)],0,8)
        for side in (-1,1): b.rail((side*1.1,0,-5.8),(side*1.1,0,5.8))
        b.post(-1.06,0,0,3.7,True);b.post(1.06,0,0,3.7,True)
        b.box((0,3.5,0),(2.4,.25,.35),7)
        b.box((0,3.23,0),(2.1,.28,.12),9)
    elif name in ('Stares','PackStairFlight'):
        b.stairs(0,-2.6,2.6,0,2.1,2.4,16 if level==0 else 10)
    elif name=='PackFlatStair':
        b.slab([(-1.2,1.2,-8.5,8.5)],0,8)
        for x in (-1.1,1.1): b.rail((x,0,-8.5),(x,0,8.5))
    elif name=='PackSwitchbackStair':
        b.stairs(1.,-4.,3.,0,3,1.8,20)
        b.slab([(-1.9,1.9,3.,4.)],3,8)
        b.stairs(-1.,3.,-4.,3,6,1.8,20)
        b.rail((-1.9,3,4),(1.9,3,4))
    else: raise ValueError(name)
    obj=b.object(material)
    # Bounds/metadata in Unity space, generated from exactly the same coordinates.
    mins=[min(p[i] for p in b.vertices) for i in range(3)];maxs=[max(p[i] for p in b.vertices) for i in range(3)]
    lo=(mins[0],mins[2],-maxs[1]); hi=(maxs[0],maxs[2],-mins[1])
    mesh=obj.data;mesh.calc_loop_triangles()
    bm=bmesh.new();bm.from_mesh(mesh)
    boundary=sum(edge.is_boundary for edge in bm.edges);bm.free()
    stats={'name':name,'lod':level,'vertices':len(mesh.vertices),'quads':sum(len(p.vertices)==4 for p in mesh.polygons),
           'faces':len(mesh.polygons),'triangles':len(mesh.loop_triangles),'boundary_edges':boundary}
    metadata={'name':name,'kind':kind,'minimum':v3(lo),'maximum':v3(hi),'floorPoint':v3(floor),'roofPoint':v3(roof),
              'floorSize':{'x':hi[0]-lo[0],'y':hi[2]-lo[2]},'roofSize':{'x':hi[0]-lo[0],'y':hi[2]-lo[2]},'ports':doors}
    assert stats['triangles']<=6000,stats
    assert stats['quads']==stats['faces'] and not boundary,stats
    return obj,metadata,stats


def export(obj,path):
    # Unity's FBX importer reflects X when converting handedness. Apply the
    # inverse only to the export copy, with corrected winding and UV loops.
    # The Blender library stays in the reference orientation.
    clone=obj.copy();clone.data=obj.data.copy();bpy.context.collection.objects.link(clone)
    bm=bmesh.new();bm.from_mesh(clone.data)
    for v in bm.verts:v.co.x=-v.co.x
    bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(clone.data);bm.free()
    clone.data.name=obj.data.name
    bpy.ops.object.select_all(action='DESELECT');clone.hide_set(False);clone.select_set(True);bpy.context.view_layer.objects.active=clone
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
                             apply_unit_scale=True,bake_space_transform=True,use_mesh_modifiers=False,add_leaf_bones=False,
                             bake_anim=False,path_mode='STRIP')
    mesh=clone.data;bpy.data.objects.remove(clone,do_unlink=True);bpy.data.meshes.remove(mesh)


def preview(objects):
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE'
    scene.world=bpy.data.worlds.new('Neutral preview');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.22,.24,.28,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.55
    scene.render.resolution_x=1200;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    light_data=bpy.data.lights.new('Softbox','AREA');light_data.energy=1800;light_data.shape='DISK';light_data.size=8
    light=bpy.data.objects.new('Softbox',light_data);scene.collection.objects.link(light);light.location=(2,-4,9)
    light.rotation_euler=(Vector((0,0,1))-light.location).to_track_quat('-Z','Y').to_euler()
    camera=bpy.data.objects.new('Review camera',bpy.data.cameras.new('Review camera'));scene.collection.objects.link(camera);scene.camera=camera
    camera.data.type='ORTHO';camera.data.clip_end=200
    for obj in objects:
        for other in objects:other.hide_render=other!=obj
        corners=[obj.matrix_world@Vector(c) for c in obj.bound_box]
        centre=sum(corners,Vector())/8;span=max(obj.dimensions)
        camera.location=centre+Vector((1.4,-1.8,1.3)).normalized()*span*3
        camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler();bpy.context.view_layer.update()
        projected=[camera.matrix_world.inverted()@p for p in corners]
        camera.data.ortho_scale=max(max(p.x for p in projected)-min(p.x for p in projected),
                                    (max(p.y for p in projected)-min(p.y for p in projected))*4/3)*1.12
        scene.render.filepath=str(PREVIEW/(obj.name+'.png'));bpy.ops.render.render(write_still=True)
    for obj in objects:obj.hide_render=False


def main():
    for folder in (OUT,SOURCE,PREVIEW):folder.mkdir(parents=True,exist_ok=True)
    if not ATLAS.exists():raise FileNotFoundError(ATLAS)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    image=bpy.data.images.load(str(ATLAS));image.pack()
    mat=bpy.data.materials.new('Castle shared trim atlas');mat.use_nodes=True
    nodes=mat.node_tree.nodes;tex=nodes.new('ShaderNodeTexImage');tex.image=image
    bsdf=nodes.get('Principled BSDF');mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color']);bsdf.inputs['Roughness'].default_value=.43
    # Only lantern paper cells emit, with no realtime point lights.
    uv=nodes.new('ShaderNodeTexCoord');sep=nodes.new('ShaderNodeSeparateXYZ');mat.node_tree.links.new(uv.outputs['UV'],sep.inputs[0])
    def math_node(operation,a,b=None):
        node=nodes.new('ShaderNodeMath');node.operation=operation
        for i,value in enumerate((a,b)):
            if value is None:continue
            if isinstance(value,(float,int)):node.inputs[i].default_value=value
            else:mat.node_tree.links.new(value,node.inputs[i])
        return node.outputs[0]
    cx=math_node('FLOOR',math_node('MULTIPLY',sep.outputs['X'],4))
    cy=math_node('FLOOR',math_node('MULTIPLY',sep.outputs['Y'],4))
    def equal(a,b):
        n=nodes.new('ShaderNodeMath');n.operation='COMPARE';mat.node_tree.links.new(a,n.inputs[0]);n.inputs[1].default_value=b;n.inputs[2].default_value=.01
        return n.outputs[0]
    mask=math_node('ADD',math_node('MULTIPLY',equal(cx,1),equal(cy,2)),math_node('MULTIPLY',equal(cx,3),equal(cy,0)))
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Emission Color']);mat.node_tree.links.new(mask,bsdf.inputs['Emission Strength'])
    names=['CorrectedRoom','HallWayWithTurnFixed','HallWay3Way','DoubleStaresBase','SingleStareBase','Path','Stares','PackFlatStair','PackSwitchbackStair','PackStairFlight']
    modules=[];statistics=[];near=[]
    for name in names:
        obj,metadata,stats=create(name,0,mat);near.append(obj);modules.append(metadata);statistics.append(stats)
        folder=OUT if name.startswith('Pack') else MOBILE
        export(obj,folder/(name+'.fbx'));obj.hide_set(True)
        if not name.startswith('Pack'):
            low,_,stats=create(name,1,mat);statistics.append(stats);export(low,MOBILE/(name+'_LOD2.fbx'));low.hide_set(True);low.hide_render=True
    far,_,stats=create('CorrectedRoom',2,mat);statistics.append(stats)
    export(far,ART/'RuntimeLod/CorrectedRoom_LOD3.fbx');far.hide_set(True);far.hide_render=True
    (OUT/'CastleModules.json').write_text(json.dumps({'modules':modules,'statistics':statistics},indent=2))
    (SOURCE/'topology-report.json').write_text(json.dumps(statistics,indent=2))
    for obj in near:
        obj.hide_set(obj!=near[0]);obj.hide_render=obj!=near[0]
    # Store clean quad source before rendering; all meshes stay at their local origin
    # in named collections so exporting one never bakes review scene placement.
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'SourceArt/CastleRetopology.blend'))
    for obj in near:obj.hide_set(False)
    if '--skip-preview' not in sys.argv:preview(near)
    print('RETOPOLOGY COMPLETE '+json.dumps(statistics),flush=True)


if __name__=='__main__':main()
