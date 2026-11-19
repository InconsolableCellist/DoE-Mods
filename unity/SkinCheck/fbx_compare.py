# Skin check 1 and 2: is the optimised FBX the same model, and does a merged material glow
# anywhere it shouldn't?
#
#   blender -b --factory-startup --python fbx_compare.py -- OLD.fbx NEW.fbx [EMISSION.png EYE_MATERIAL]
#
# 1. Geometry. Every vertex position, corner UV, corner normal, bone weight, shape-key offset and
#    bone must match between OLD and NEW; only the material each face uses may differ.
# 2. Emission overlap (when EMISSION.png is given). Rasterises the UV triangles of every face of
#    OLD that is NOT on EYE_MATERIAL onto the emission map, and counts lit texels under them.
#    Zero means one material can carry the emission map without lighting up anything else.
#
# Exit code 0 = pass, 1 = fail. Prints a short report; nothing is written.
import bpy, sys, numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
old_fbx, new_fbx = args[0], args[1]
emis_png = args[2] if len(args) > 2 else None
eye_mat = args[3] if len(args) > 3 else "Eyes"

def load(path):
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)
    for a in list(bpy.data.armatures): bpy.data.armatures.remove(a)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    d = {"meshes": {}}
    for o in meshes:
        me = o.data
        n = len(me.vertices)
        co = np.empty(n * 3, np.float64); me.vertices.foreach_get("co", co)
        uv = np.empty(len(me.loops) * 2, np.float64); me.uv_layers.active.data.foreach_get("uv", uv)
        lv = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", lv)
        try:
            cn = np.array([c.vector[:] for c in me.corner_normals], np.float64)
        except AttributeError:
            cn = None
        groups = {g.index: g.name for g in o.vertex_groups}
        w = [tuple(sorted((groups[g.group], round(g.weight, 5)) for g in v.groups if g.weight > 1e-5)) for v in me.vertices]
        keys = {}
        if me.shape_keys:
            for k in me.shape_keys.key_blocks:
                a = np.empty(n * 3, np.float64); k.data.foreach_get("co", a); keys[k.name] = a
        mats = [s.material.name if s.material else "" for s in o.material_slots]
        pm = [mats[p.material_index] if p.material_index < len(mats) else "" for p in me.polygons]
        d["meshes"][o.name] = dict(co=co, uv=uv, lv=lv, cn=cn, w=w, keys=keys, pm=pm, obj=o)
    d["bones"] = {}
    if arm:
        for b in arm.data.bones:
            d["bones"][b.name] = (tuple(round(x, 5) for x in b.head_local), tuple(round(x, 5) for x in b.tail_local), b.parent.name if b.parent else None)
    return d

def maxdiff(a, b):
    if a is None or b is None: return None
    if a.shape != b.shape: return float("inf")
    return float(np.max(np.abs(a - b))) if a.size else 0.0

ok = True
def check(label, good, detail=""):
    global ok
    ok &= good
    print(("PASS " if good else "FAIL ") + label + (": " + detail if detail else ""))

A = load(old_fbx)
# Emission overlap needs OLD's mesh and material assignment while it is loaded.
if emis_png:
    img = bpy.data.images.load(emis_png)
    W, H = img.size
    px = np.array(img.pixels[:], np.float32).reshape(H, W, 4)   # bottom-up, like UV v
    lum = px[..., 0] * 0.3 + px[..., 1] * 0.59 + px[..., 2] * 0.11
    lit = lum > 0.02
    hits = {}
    for name, m in A["meshes"].items():
        me = m["obj"].data
        me.calc_loop_triangles()
        uvl = me.uv_layers.active.data
        for t in me.loop_triangles:
            mat = m["pm"][t.polygon_index]
            if mat == eye_mat: continue
            p = np.array([uvl[l].uv[:] for l in t.loops]) * [W, H]
            x0, y0 = np.floor(p.min(0)).astype(int); x1, y1 = np.ceil(p.max(0)).astype(int)
            x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W - 1), min(y1, H - 1)
            if x1 < x0 or y1 < y0: continue
            sub = lit[y0:y1 + 1, x0:x1 + 1]
            if not sub.any(): continue
            yy, xx = np.mgrid[y0:y1 + 1, x0:x1 + 1]; cx, cy = xx + 0.5, yy + 0.5
            (ax, ay), (bx, by), (qx, qy) = p
            den = (by - qy) * (ax - qx) + (qx - bx) * (ay - qy)
            if abs(den) < 1e-12: continue
            l1 = ((by - qy) * (cx - qx) + (qx - bx) * (cy - qy)) / den
            l2 = ((qy - ay) * (cx - qx) + (ax - qx) * (cy - qy)) / den
            inside = (l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)
            n = int((inside & sub).sum())
            if n: hits[mat] = hits.get(mat, 0) + n
    check("emission overlap", not hits,
          "no lit emission texel under any face outside `%s`" % eye_mat if not hits else
          "lit texels under " + ", ".join("%s: %d" % kv for kv in sorted(hits.items())))

B = load(new_fbx)
check("mesh objects", sorted(A["meshes"]) == sorted(B["meshes"]), "%s vs %s" % (sorted(A["meshes"]), sorted(B["meshes"])))
for name in sorted(set(A["meshes"]) & set(B["meshes"])):
    a, b = A["meshes"][name], B["meshes"][name]
    check(name + " vertex count", len(a["w"]) == len(b["w"]), "%d vs %d" % (len(a["w"]), len(b["w"])))
    check(name + " face corners", np.array_equal(a["lv"], b["lv"]), "%d corners" % len(a["lv"]))
    d = maxdiff(a["co"], b["co"]); check(name + " positions", d is not None and d < 1e-5, "max diff %.2e" % d)
    d = maxdiff(a["uv"], b["uv"]); check(name + " UVs", d is not None and d < 1e-6, "max diff %.2e" % d)
    if a["cn"] is not None and b["cn"] is not None:
        d = maxdiff(a["cn"], b["cn"]); check(name + " corner normals", d < 1e-4, "max diff %.2e" % d)
    bad = sum(1 for x, y in zip(a["w"], b["w"]) if x != y)
    check(name + " bone weights", bad == 0, "%d vertex(es) differ" % bad)
    check(name + " shape keys", sorted(a["keys"]) == sorted(b["keys"]), ", ".join(sorted(a["keys"])))
    for k in sorted(set(a["keys"]) & set(b["keys"])):
        d = maxdiff(a["keys"][k], b["keys"][k]); check(name + " shape `%s`" % k, d < 1e-5, "max diff %.2e" % d)
    ma, mb = sorted(set(a["pm"])), sorted(set(b["pm"]))
    print("INFO %s materials: %d (%s) -> %d (%s)" % (name, len(ma), ", ".join(ma), len(mb), ", ".join(mb)))
check("bones", A["bones"] == B["bones"], "%d bones" % len(A["bones"]))
print("RESULT", "PASS" if ok else "FAIL")
sys.stdout.flush()
import os; os._exit(0 if ok else 1)
