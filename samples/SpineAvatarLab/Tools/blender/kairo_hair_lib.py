# kairo_hair_lib.py: helpers exec'd inside Blender (exec(open(LIB).read())) for the Kairo hair-curves work.
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
S = "/private/tmp/claude-501/-Users-jonatansoderberg/7d78f3cc-57f5-422d-aab2-3696c98f80ad/scratchpad"
PIV = Vector((0.005, 0.046, 0.053))   # HeadPivot bone head = the Scalp/HairEnvelope origin

def world_mesh(o, evaluated=True):
    dg = bpy.context.evaluated_depsgraph_get()
    src = o.evaluated_get(dg) if evaluated else o
    me = src.to_mesh() if evaluated else o.data
    mw = o.matrix_world
    verts = [mw @ v.co for v in me.vertices]
    me.calc_loop_triangles()
    tris = [tuple(t.vertices) for t in me.loop_triangles]
    polys = [(tuple(p.vertices), p.material_index) for p in me.polygons]
    if evaluated: src.to_mesh_clear()
    return verts, tris, polys

def bvh_of(o):
    v, t, _ = world_mesh(o)
    return BVHTree.FromPolygons(v, t), v, t

def lab_yaw(deg):
    root = bpy.data.objects['Root']; root.rotation_mode = 'XYZ'; root.rotation_euler[2] = math.radians(deg)

def lab_render(path, yaw=0, hide=(), show=None, engine='BLENDER_WORKBENCH', res=600, color_type='TEXTURE', samples=None):
    """Render through LabCamera at the lab's yaw (Root Z). hide: object names hidden for this render;
    show: if given, every other mesh/curves object is hidden. Settings restored afterwards."""
    sc = bpy.context.scene
    root = bpy.data.objects['Root']; root.rotation_mode = 'XYZ'; old_yaw = root.rotation_euler[2]
    root.rotation_euler[2] = math.radians(yaw)
    old_hide = {o.name: o.hide_render for o in bpy.data.objects}
    if show is not None:
        for o in bpy.data.objects:
            if o.type in ('MESH', 'CURVES', 'CURVE') and o.name not in show: o.hide_render = True
    for n in hide: bpy.data.objects[n].hide_render = True
    old = (sc.render.engine, sc.render.resolution_x, sc.render.resolution_y, sc.render.filepath, sc.render.resolution_percentage,
           sc.display.shading.color_type, sc.render.image_settings.file_format, sc.render.film_transparent)
    try: sc.render.engine = engine
    except TypeError as e: print("engine error:", e)
    sc.render.resolution_x = sc.render.resolution_y = res; sc.render.resolution_percentage = 100
    sc.render.filepath = path; sc.render.image_settings.file_format = 'PNG'; sc.render.film_transparent = False
    sc.display.shading.color_type = color_type
    if samples is not None and engine != 'BLENDER_WORKBENCH':
        try: sc.eevee.taa_render_samples = samples
        except Exception: pass
    bpy.ops.render.render(write_still=True)
    (sc.render.engine, sc.render.resolution_x, sc.render.resolution_y, sc.render.filepath, sc.render.resolution_percentage,
     sc.display.shading.color_type, sc.render.image_settings.file_format, sc.render.film_transparent) = old
    for n, h in old_hide.items():
        o = bpy.data.objects.get(n)
        if o: o.hide_render = h
    root.rotation_euler[2] = old_yaw
    return os.path.exists(path)

def ensure_object(name, data, parent=None, collection=None):
    o = bpy.data.objects.get(name)
    if o:
        oldd = o.data; o.data = data
        if oldd and oldd.users == 0:
            {'MESH': bpy.data.meshes, 'CURVES': bpy.data.hair_curves}.get(oldd.__class__.__name__.upper().replace('CURVES','CURVES'), None)
    else:
        o = bpy.data.objects.new(name, data)
        (collection or bpy.data.collections.get('Collection') or bpy.context.scene.collection).objects.link(o)
    if parent is not None:
        o.parent = parent; o.matrix_parent_inverse = Matrix.Identity(4); o.matrix_world = Matrix.Identity(4) @ parent.matrix_world
    return o
