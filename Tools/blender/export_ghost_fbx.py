"""Export the ghost companion from ghost.blend to an FBX for Unity.

Run headless:
  blender.exe -b "D:\\blender-5.2.3\\projects\\ghost\\ghost.blend" --python Tools/blender/export_ghost_fbx.py -- <out.fbx>

The body's height gradient is a Blender shader node setup that FBX can't carry, so it is
baked into vertex colours here (along with the face and eye colours). Unity renders them
with the AHC/GhostVertexColor shader. Animation is not exported: the float/blink loop is
reproduced procedurally by GhostCompanion.cs so it can react to the guidance.
"""
import sys
import bpy

OUT = sys.argv[sys.argv.index("--") + 1]

# Same palette as build_ghost.py (sRGB hex).
C_BOTTOM, C_MID, C_TOP = "#E1EADB", "#9FB899", "#56795A"
C_FACE, C_EYE = "#1F3826", "#F6FBF2"
RAMP = [(0.0, C_BOTTOM), (0.42, C_MID), (1.0, C_TOP)]  # positions over body-space Z 0.15 → 2.2


def to_lin(h):
    h = h.lstrip("#")
    f = lambda v: v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    return [f(int(h[i:i + 2], 16) / 255) for i in (0, 2, 4)]


def to_srgb(c):
    f = lambda v: v * 12.92 if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055
    return [min(1.0, max(0.0, f(v))) for v in c]


def ramp(t):
    t = min(1.0, max(0.0, t))
    for (p0, c0), (p1, c1) in zip(RAMP, RAMP[1:]):
        if t <= p1:
            k = (t - p0) / (p1 - p0)
            a, b = to_lin(c0), to_lin(c1)
            return [a[i] + (b[i] - a[i]) * k for i in range(3)]  # Blender ramps blend in linear
    return to_lin(RAMP[-1][1])


# Drop the stage (ground, lights, camera); keep the ghost.
for o in list(bpy.data.objects):
    if not o.name.startswith("Ghost"):
        bpy.data.objects.remove(o, do_unlink=True)

# Rest pose: clear the float animation so the exported transforms are neutral.
for o in bpy.data.objects:
    o.animation_data_clear()
rig = bpy.data.objects["Ghost_Rig"]
rig.location, rig.rotation_euler, rig.scale = (0, 0, 0), (0, 0, 0), (1, 1, 1)
bpy.data.objects["Ghost_EyesRig"].scale = (1, 1, 1)
bpy.context.view_layer.update()

# Flatten: the FBX exporter mis-converts the axes of meshes parented under empties, so
# un-parent everything (keeping world transforms) and drop the empties. Each eye keeps its
# own origin at its centre, which is what GhostCompanion scales to blink.
for o in bpy.data.objects:
    if o.type == "MESH" and o.parent is not None:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
for name in ("Ghost_Rig", "Ghost_EyesRig"):
    bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
bpy.context.view_layer.update()

# Lighter subdivision for a standalone headset.
for o in bpy.data.objects:
    for m in o.modifiers:
        if m.type == "SUBSURF":
            m.levels = m.render_levels = 1


def bake(obj, color_of):
    me = obj.data
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "POINT")
    mw = obj.matrix_world
    for v in me.vertices:
        r, g, b = to_srgb(color_of(mw @ v.co))
        attr.data[v.index].color_srgb = (r, g, b, 1.0)
    me.color_attributes.active_color = attr


for name in ("Ghost_Body", "Ghost_Ear_L", "Ghost_Ear_R"):
    bake(bpy.data.objects[name], lambda p: ramp((p.z - 0.15) / (2.2 - 0.15)))
bake(bpy.data.objects["Ghost_Face"], lambda p: to_lin(C_FACE))
for name in ("Ghost_Eye_L", "Ghost_Eye_R", "Ghost_Nose"):
    bake(bpy.data.objects[name], lambda p: to_lin(C_EYE))

bpy.ops.export_scene.fbx(
    filepath=OUT,
    object_types={"MESH"},
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",
    colors_type="SRGB",
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    add_leaf_bones=False,
    bake_anim=False,
)
print("EXPORTED", OUT)
