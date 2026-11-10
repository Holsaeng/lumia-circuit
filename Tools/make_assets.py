# Blender 5.x: 루미아 서킷 에셋 생성 → FBX 내보내기 + 미리보기 렌더
# 실행: blender -b -P make_assets.py -- <FBX 출력 폴더(절대 경로)> <미리보기 PNG(절대 경로)>
# 모든 모델은 직접 만든 저폴리 도형 조합이다. 앞 = -Y, 바닥 = z 0.
import bpy, sys, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if len(argv) > 0 else os.path.join(HERE, "..", "Assets", "Resources", "Models")
PREVIEW = os.path.abspath(argv[1]) if len(argv) > 1 else os.path.join(HERE, "preview.png")
os.makedirs(OUT, exist_ok=True)
PI = math.pi

bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------- 재질 (Unity에서는 이름으로 URP 재질에 다시 연결한다) ----------
MATS = {}
def mat(name, color, emit=0.0, metallic=0.0, rough=0.5):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    if emit > 0:
        b.inputs["Emission Color"].default_value = (*color, 1)
        b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = (*color, 1)
    MATS[name] = m
    return m

P = {
    "Body": lambda: mat("Body", (0.92, 0.94, 0.97), rough=0.25),
    "Visor": lambda: mat("Visor", (0.02, 0.03, 0.06), rough=0.08, metallic=0.6),
    "Glow": lambda: mat("Glow", (0.1, 0.9, 1.0), emit=2),
    "Accent": lambda: mat("Accent", (1.0, 0.55, 0.1), rough=0.35),
    "Metal": lambda: mat("Metal", (0.1, 0.11, 0.15), rough=0.3, metallic=0.8),
    "Aura": lambda: mat("Aura", (1.0, 0.15, 0.3), emit=2),          # 동물의 눈·발밑 고리 (상태 색)
    "FurWhite": lambda: mat("FurWhite", (0.93, 0.9, 0.85), rough=0.8),
    "FurBrown": lambda: mat("FurBrown", (0.36, 0.22, 0.14), rough=0.85),
    "FurGrey": lambda: mat("FurGrey", (0.42, 0.45, 0.5), rough=0.85),
    "FurDark": lambda: mat("FurDark", (0.12, 0.1, 0.16), rough=0.8),
    "FurPurple": lambda: mat("FurPurple", (0.26, 0.16, 0.36), rough=0.8),
    "Comb": lambda: mat("Comb", (0.85, 0.1, 0.12), rough=0.5),
    "Snout": lambda: mat("Snout", (0.75, 0.5, 0.45), rough=0.6),
    "Bone": lambda: mat("Bone", (0.95, 0.92, 0.82), rough=0.4),
    "CellGlow": lambda: mat("CellGlow", (1.0, 0.8, 0.25), emit=1.5),  # 크레딧 칩
    "CoreGlow": lambda: mat("CoreGlow", (1.0, 0.1, 0.2), emit=2.5),   # VF 혈액 샘플
    "BonusGlow": lambda: mat("BonusGlow", (0.3, 1.0, 0.5), emit=2),   # 보급 상자 띠
    "Crate": lambda: mat("Crate", (0.16, 0.2, 0.27), rough=0.45, metallic=0.5),
    # 맵 소품
    "Leaf": lambda: mat("Leaf", (0.2, 0.45, 0.18), rough=0.9),
    "LeafLight": lambda: mat("LeafLight", (0.35, 0.58, 0.22), rough=0.9),
    "Trunk": lambda: mat("Trunk", (0.35, 0.24, 0.15), rough=0.9),
    "Stone": lambda: mat("Stone", (0.5, 0.5, 0.52), rough=0.95),
    "Wood": lambda: mat("Wood", (0.6, 0.44, 0.26), rough=0.9),
    "WoodDark": lambda: mat("WoodDark", (0.38, 0.26, 0.15), rough=0.9),
    "LampPole": lambda: mat("LampPole", (0.2, 0.21, 0.24), rough=0.6),
    "LampGlow": lambda: mat("LampGlow", (1.0, 0.86, 0.6), rough=0.5),
}

def ico(loc, r, sub=1):
    bpy.ops.mesh.primitive_ico_sphere_add(radius=r, location=loc, subdivisions=sub)

# ---------- 맵 소품 (원점 = 바닥 중심) ----------
def make_tree():
    R = empty("Tree")
    cyl((0, 0, 0.12), 0.06, 0.24, 8);                  finish("Trunk", "Trunk", smooth=False, parent=R)
    cone((0, 0, 0.42), 0.3, 0.1, 0.42, 8);             finish("Leaf0", "Leaf", smooth=False, parent=R)
    cone((0, 0, 0.72), 0.22, 0.0, 0.42, 8);            finish("Leaf1", "LeafLight", smooth=False, parent=R)
    return R

def make_bush():
    R = empty("Bush")
    for i, (x, y, r) in enumerate(((0, 0, 0.2), (0.17, 0.05, 0.14), (-0.15, -0.06, 0.15))):
        ico((x, y, r * 0.8), r, 1);                    finish("Leaf%d" % i, "Leaf" if i else "LeafLight", smooth=False, parent=R, scale=(1, 1, 0.8))
    return R

def make_rock():
    R = empty("Rock")
    ico((0, 0, 0.13), 0.22, 1);                        finish("Rock0", "Stone", smooth=False, parent=R, scale=(1, 0.8, 0.65))
    ico((0.18, 0.1, 0.07), 0.11, 1);                   finish("Rock1", "Stone", smooth=False, parent=R, scale=(1, 1, 0.7))
    return R

def make_crate():
    R = empty("WoodBox")
    cube((0, 0, 0.17), 0.34);                          finish("Box", "Wood", smooth=False, bevel=0.012, parent=R)
    for i, z in enumerate((0.04, 0.3)):
        cube((0, 0, z), 0.36);                         finish("Frame%d" % i, "WoodDark", smooth=False, parent=R, scale=(1, 1, 0.12))
    return R

# ---------- 도우미 ----------
def finish(name, material, smooth=True, bevel=0.0, parent=None, scale=None, rot=None):
    o = bpy.context.active_object
    o.name = name
    if scale: o.scale = scale
    if rot: o.rotation_euler = rot
    if bevel > 0:
        md = o.modifiers.new("Bevel", "BEVEL"); md.width = bevel; md.segments = 3
    o.data.materials.append(P[material]())
    for p in o.data.polygons: p.use_smooth = smooth
    if parent:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()
    return o

def empty(name, loc=(0, 0, 0), parent=None):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    if parent: e.parent = parent
    bpy.context.view_layer.update()
    return e

def sphere(loc, r, seg=32, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=seg, ring_count=rings)
def torus(loc, R, r):
    bpy.ops.mesh.primitive_torus_add(location=loc, major_radius=R, minor_radius=r, major_segments=64, minor_segments=12)
def cone(loc, r1, r2, d, verts=24):
    bpy.ops.mesh.primitive_cone_add(location=loc, radius1=r1, radius2=r2, depth=d, vertices=verts)
def cyl(loc, r, d, verts=24):
    bpy.ops.mesh.primitive_cylinder_add(location=loc, radius=r, depth=d, vertices=verts)
def cube(loc, size=1.0):
    bpy.ops.mesh.primitive_cube_add(location=loc, size=size)

def plate(name, pts, material, parent, thick=0.02):
    """XZ 평면 다각형(부모 기준 좌표)에 두께를 준 판."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([(x, 0, z) for x, z in pts], [], [list(range(len(pts)))])
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = parent
    md = o.modifiers.new("Solid", "SOLIDIFY"); md.thickness = thick; md.offset = 0
    o.modifiers.new("Tri", "TRIANGULATE")
    me.materials.append(P[material]())
    return o

def eyes(R, x, y, z, r=0.032):
    for i, sx in enumerate((-1, 1)):
        sphere((sx * x, y, z), r, 16, 8); finish("Eye" + "LR"[i], "Aura", parent=R)

def ring(R, radius):
    torus((0, 0, 0.03), radius, 0.02); finish("Ring", "Aura", parent=R)

# ---------- 루미 (플레이어: 보급 로봇) ----------
def make_lumi():
    R = empty("Lumi")
    sphere((0, 0, 0.5), 0.38, 48, 24);      finish("Body", "Body", parent=R, scale=(1, 1, 0.9))
    sphere((0, -0.24, 0.54), 0.26, 48, 24); finish("Visor", "Visor", parent=R, scale=(1.15, 0.75, 0.6))
    for i, sx in enumerate((-1, 1)):
        sphere((sx * 0.1, -0.42, 0.55), 0.055, 24, 12)
        finish("Eye" + "LR"[i], "Glow", parent=R, scale=(1, 0.5, 1.3))
    torus((0, 0, 0.12), 0.3, 0.045);        finish("Ring", "Glow", parent=R)
    cone((0, 0, 0.2), 0.1, 0.22, 0.12);     finish("Thruster", "Accent", parent=R)
    cyl((0, 0, 0.92), 0.015, 0.18);         finish("Antenna", "Body", parent=R)
    sphere((0, 0, 1.03), 0.045, 24, 12);    finish("AntennaTip", "Glow", parent=R)
    for i, sx in enumerate((-1, 1)):
        cube((sx * 0.4, 0.02, 0.5))
        finish("Fin" + "LR"[i], "Accent", smooth=False, bevel=0.015, parent=R, scale=(0.035, 0.16, 0.1))
    return R

# ---------- 야생동물 4종 ----------
def make_chicken():
    R = empty("Chicken")
    sphere((0, 0.03, 0.36), 0.28);          finish("Body", "FurWhite", parent=R, scale=(0.9, 1.1, 0.95))
    sphere((0, -0.2, 0.66), 0.17);          finish("Head", "FurWhite", parent=R)
    cone((0, -0.4, 0.64), 0.06, 0, 0.14);   finish("Beak", "Accent", parent=R, rot=(PI / 2, 0, 0))
    for i in range(3):
        sphere((0, -0.26 + i * 0.07, 0.83 - abs(i - 1) * 0.03), 0.055, 16, 8); finish("Comb%d" % i, "Comb", parent=R)
    sphere((0, -0.34, 0.55), 0.04, 16, 8);  finish("Wattle", "Comb", parent=R)
    cone((0, 0.33, 0.52), 0.13, 0, 0.3);    finish("Tail", "FurWhite", parent=R, rot=(-PI / 3, 0, 0))
    for i, sx in enumerate((-1, 1)):
        sphere((sx * 0.24, 0.04, 0.38), 0.17); finish("Wing" + "LR"[i], "FurWhite", parent=R, scale=(0.35, 1, 0.7))
        cyl((sx * 0.09, 0, 0.07), 0.02, 0.14, 8); finish("Leg" + "LR"[i], "Accent", parent=R)
    eyes(R, 0.1, -0.33, 0.7)
    ring(R, 0.36)
    return R

def make_bat():
    R = empty("Bat")
    sphere((0, 0, 0.6), 0.2);               finish("Body", "FurPurple", parent=R, scale=(1, 0.9, 1.1))
    sphere((0, -0.1, 0.8), 0.15);           finish("Head", "FurPurple", parent=R)
    for i, sx in enumerate((-1, 1)):
        cone((sx * 0.09, -0.08, 0.97), 0.06, 0, 0.18, 8); finish("Ear" + "LR"[i], "FurDark", smooth=False, parent=R, rot=(0, sx * 0.25, 0))
        cone((sx * 0.035, -0.225, 0.725), 0.016, 0, 0.06, 8); finish("Fang" + "LR"[i], "Bone", parent=R, rot=(PI, 0, 0))
        W = empty("Wing" + "LR"[i], (sx * 0.14, 0.02, 0.66), R)   # 어깨 축 (실행 중 날갯짓)
        pts = [(0, 0.09), (sx * 0.42, 0.24), (sx * 0.56, 0.02), (sx * 0.45, -0.1), (sx * 0.38, 0.0),
               (sx * 0.28, -0.14), (sx * 0.2, -0.02), (sx * 0.1, -0.13), (0, -0.07)]
        plate("Membrane" + "LR"[i], pts, "FurDark", W)
    eyes(R, 0.06, -0.235, 0.82, 0.035)
    ring(R, 0.34)
    return R

def make_boar():
    R = empty("Boar")
    sphere((0, 0.06, 0.38), 0.3);           finish("Body", "FurBrown", parent=R, scale=(0.85, 1.3, 0.85))
    sphere((0, -0.3, 0.4), 0.2);            finish("Head", "FurBrown", parent=R, scale=(0.9, 1, 0.9))
    cyl((0, -0.5, 0.36), 0.09, 0.14);       finish("Snout", "Snout", parent=R, rot=(PI / 2, 0, 0))
    for i, sx in enumerate((-1, 1)):
        cone((sx * 0.11, -0.5, 0.4), 0.03, 0, 0.16, 8); finish("Tusk" + "LR"[i], "Bone", parent=R, rot=(0.6, 0, sx * -0.35))
        cone((sx * 0.14, -0.25, 0.6), 0.06, 0, 0.13, 8); finish("Ear" + "LR"[i], "FurBrown", smooth=False, parent=R, rot=(0, sx * 0.4, 0))
        for j, y in enumerate((-0.2, 0.3)):
            cyl((sx * 0.15, y, 0.1), 0.06, 0.2, 12); finish("Leg%s%d" % ("LR"[i], j), "FurDark", parent=R)
    for i in range(5):
        cube((0, -0.22 + i * 0.14, 0.655 - abs(i - 1.5) * 0.02))
        finish("Mane%d" % i, "FurDark", smooth=False, parent=R, scale=(0.04, 0.09, 0.09), rot=(PI / 4, 0, 0))
    cone((0, 0.47, 0.45), 0.03, 0, 0.14, 8); finish("Tail", "FurDark", parent=R, rot=(-PI / 2, 0, 0))
    eyes(R, 0.1, -0.455, 0.48, 0.03)
    ring(R, 0.42)
    return R

def make_wolf():
    R = empty("Wolf")
    sphere((0, 0.08, 0.5), 0.24);           finish("Body", "FurGrey", parent=R, scale=(0.8, 1.5, 0.85))
    sphere((0, -0.2, 0.6), 0.2);            finish("Chest", "FurWhite", parent=R)
    sphere((0, -0.34, 0.74), 0.17);         finish("Head", "FurGrey", parent=R)
    cone((0, -0.52, 0.7), 0.1, 0.04, 0.22); finish("Muzzle", "FurGrey", parent=R, rot=(PI / 2, 0, 0))
    sphere((0, -0.64, 0.7), 0.04, 16, 8);   finish("Nose", "FurDark", parent=R)
    for i, sx in enumerate((-1, 1)):
        cone((sx * 0.1, -0.3, 0.93), 0.06, 0, 0.17, 8); finish("Ear" + "LR"[i], "FurDark", smooth=False, parent=R)
        for j, y in enumerate((-0.2, 0.32)):
            cyl((sx * 0.12, y, 0.17), 0.045, 0.34, 12); finish("Leg%s%d" % ("LR"[i], j), "FurGrey", parent=R)
    cone((0, 0.54, 0.58), 0.08, 0, 0.38);   finish("Tail", "FurGrey", parent=R, rot=(-PI / 2.4, 0, 0))
    eyes(R, 0.075, -0.475, 0.79, 0.03)
    ring(R, 0.42)
    return R

# ---------- 아이템 ----------
def make_cell():      # 크레딧 칩
    R = empty("Cell")
    cyl((0, 0, 0), 0.1, 0.03, 6);           finish("Chip", "CellGlow", smooth=False, parent=R, rot=(PI / 2, 0, 0))
    cyl((0, 0, 0), 0.055, 0.05, 6);         finish("Stamp", "CellGlow", smooth=False, parent=R, rot=(PI / 2, 0, PI / 6))
    return R

def make_core():      # VF 혈액 샘플
    R = empty("Core")
    cyl((0, 0, 0), 0.075, 0.3);             finish("Vial", "CoreGlow", parent=R)
    for i, z in enumerate((-0.17, 0.17)):
        cyl((0, 0, z), 0.09, 0.06);         finish("Cap%d" % i, "Metal", smooth=False, parent=R)
    torus((0, 0, 0), 0.26, 0.016);          finish("Ring", "CoreGlow", parent=R, rot=(0.5, 0, 0))
    torus((0, 0, 0), 0.26, 0.016);          finish("Ring2", "CoreGlow", parent=R, rot=(-0.5, 0, PI / 2))
    return R

def make_bonus():     # 항공 보급 상자
    R = empty("Bonus")
    cube((0, 0, 0), 0.36);                  finish("Crate", "Crate", smooth=False, bevel=0.02, parent=R)
    cube((0, 0, 0), 0.36);                  finish("Band", "BonusGlow", smooth=False, parent=R, scale=(1.03, 1.03, 0.16))
    cube((0, 0, 0.19), 0.36);               finish("Lid", "Crate", smooth=False, bevel=0.015, parent=R, scale=(1.08, 1.08, 0.12))
    cube((0, 0, 0.215), 0.1);               finish("Beacon", "BonusGlow", smooth=False, bevel=0.01, parent=R, scale=(1, 1, 0.5))
    return R

def make_lamp():
    R = empty("Lamp")
    cyl((0, 0, 0.05), 0.13, 0.1, 12);          finish("Base", "LampPole", smooth=False, parent=R)
    cyl((0, 0, 0.8), 0.035, 1.5, 12);          finish("Pole", "LampPole", parent=R)
    cube((0, -0.16, 1.52));                     finish("Arm", "LampPole", smooth=False, parent=R, scale=(0.03, 0.2, 0.03))
    cone((0, -0.3, 1.47), 0.13, 0.05, 0.1, 12); finish("Shade", "LampPole", smooth=False, parent=R)
    sphere((0, -0.3, 1.4), 0.07, 16, 8);        finish("Bulb", "LampGlow", parent=R)
    return R

ASSETS = [make_lumi(), make_chicken(), make_bat(), make_boar(), make_wolf(), make_cell(), make_core(), make_bonus(),
          make_tree(), make_bush(), make_rock(), make_crate(), make_lamp()]

# ---------- FBX 내보내기 (에셋별 1파일) ----------
def export(r):
    bpy.ops.object.select_all(action="DESELECT")
    r.select_set(True)
    for c in r.children_recursive: c.select_set(True)
    bpy.context.view_layer.objects.active = r
    path = os.path.join(OUT, r.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"EMPTY", "MESH"},
        use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
        axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True)
    print("EXPORTED", path, os.path.getsize(path))

for r in ASSETS:
    export(r)

# ---------- 미리보기 렌더 ----------
for i, r in enumerate(ASSETS):
    r.location = ((i - 5.5) * 1.0, 0, 0.45 if 5 <= i < 8 else 0)
    if i < 5: r.rotation_euler = (0, 0, 0.5)

scn = bpy.context.scene
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try:
        scn.render.engine = eng; break
    except Exception:
        pass
w = bpy.data.worlds.new("W"); scn.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.06, 0.1, 1)
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 3
sun.rotation_euler = (math.radians(50), 0, math.radians(-30)); scn.collection.objects.link(sun)
bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, -0.02))
g = bpy.context.active_object; g.data.materials.append(mat("Ground", (0.03, 0.04, 0.08), rough=0.4))
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scn.collection.objects.link(cam)
cam.location = (0, -8.5, 3.2)
cam.rotation_euler = (Vector((0, 0, 0.4)) - cam.location).to_track_quat("-Z", "Y").to_euler()
cam.data.lens = 30
scn.camera = cam
scn.render.resolution_x, scn.render.resolution_y = 1800, 600
scn.render.filepath = PREVIEW
try:
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", PREVIEW)
except Exception as e:
    print("PREVIEW FAILED", e)
