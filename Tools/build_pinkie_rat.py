import bpy
import math
import os
from mathutils import Vector, Quaternion

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
FBX_IN = os.path.join(ROOT, 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat.fbx')
FBX_OUT = os.path.join(ROOT, 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat_Pinkie.fbx')
BLEND_OUT = os.path.join(ROOT, 'Tools', 'HandPaintedRat_Pinkie.blend')
PREVIEW_DIR = os.path.join(ROOT, 'Tools', 'preview')
SKIN_TEXTURE = os.path.join(ROOT, 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat_PinkieSkin.png')
os.makedirs(PREVIEW_DIR, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX_IN, use_anim=True)
scene = bpy.context.scene
arm = bpy.data.objects['Armature']
mesh = bpy.data.objects['rat_mesh']

scene.frame_set(1)
bpy.context.view_layer.update()
base_bones = {}
for pb in arm.pose.bones:
    base_bones[pb.name] = {
        'location': pb.location.copy(),
        'rotation': pb.rotation_quaternion.copy(),
        'scale': pb.scale.copy(),
    }
base_arm = {
    'location': arm.location.copy(),
    'rotation_mode': arm.rotation_mode,
    'rotation_euler': arm.rotation_euler.copy(),
    'rotation_quaternion': arm.rotation_quaternion.copy(),
    'scale': arm.scale.copy(),
}

def set_bone(name, location=None, rotation=None, scale=None, frame=None):
    pb = arm.pose.bones[name]
    pb.rotation_mode = 'QUATERNION'
    if location is not None:
        pb.location = location
        pb.keyframe_insert(data_path='location', frame=frame, group='Pinkie Kick')
    if rotation is not None:
        pb.rotation_quaternion = rotation
        pb.keyframe_insert(data_path='rotation_quaternion', frame=frame, group='Pinkie Kick')
    if scale is not None:
        pb.scale = scale
        pb.keyframe_insert(data_path='scale', frame=frame, group='Pinkie Kick')

def key_armature_transform(frame):
    arm.location = base_arm['location']
    arm.rotation_mode = base_arm['rotation_mode']
    if base_arm['rotation_mode'] == 'QUATERNION':
        arm.rotation_quaternion = base_arm['rotation_quaternion']
        arm.keyframe_insert(data_path='rotation_quaternion', frame=frame, group='Pinkie Kick')
    else:
        arm.rotation_euler = base_arm['rotation_euler']
        arm.keyframe_insert(data_path='rotation_euler', frame=frame, group='Pinkie Kick')
    arm.scale = base_arm['scale']
    arm.keyframe_insert(data_path='location', frame=frame, group='Pinkie Kick')
    arm.keyframe_insert(data_path='scale', frame=frame, group='Pinkie Kick')

def base_pose(frame):
    key_armature_transform(frame)
    for name, pose in base_bones.items():
        set_bone(name, pose['location'], pose['rotation'], pose['scale'], frame)

def delta(base, axis, degrees):
    return base @ Quaternion(Vector(axis), math.radians(degrees))

# Establish a neutral pose at the start and end instead of inheriting the old
# Attack clip's final pose.
for frame in (257, 288):
    scene.frame_set(1)
    base_pose(frame)

root_base = base_bones['Root']['rotation']
spine_base = base_bones['Spine']['rotation']
chest_base = base_bones['Chest']['rotation']
head_base = base_bones['Head']['rotation']
tail_base = {name: base_bones[name]['rotation'] for name in ('Tail_Start', 'Tail_1', 'Tail_2', 'Tail_3')}

# Roll the whole rat onto its back around the body axis, with a small relaxed
# lift through the spine, chest, and head.
roll_back = Quaternion((0, 1, 0), math.pi)
for frame in (257, 260, 264, 272, 280, 288):
    scene.frame_set(frame)
    set_bone('Root', base_bones['Root']['location'], root_base @ roll_back, base_bones['Root']['scale'], frame)
    set_bone('Spine', base_bones['Spine']['location'], delta(spine_base, (1, 0, 0), -7), base_bones['Spine']['scale'], frame)
    set_bone('Chest', base_bones['Chest']['location'], delta(chest_base, (1, 0, 0), 5), base_bones['Chest']['scale'], frame)
    set_bone('Head', base_bones['Head']['location'], delta(head_base, (1, 0, 0), 6), base_bones['Head']['scale'], frame)

# Light alternating hind-leg kicks. The offset keeps the motion soft instead
# of making both legs flap together.
kick_frames = {
    257: (-4, 7),
    264: (18, -12),
    272: (-16, 20),
    280: (20, -16),
    288: (0, 0),
}
for frame, (left, right) in kick_frames.items():
    scene.frame_set(frame)
    for side, amount in (('Left', left), ('Right', right)):
        upper = side + '_Rear_UpperLeg'
        lower = side + '_Rear_LowerLeg'
        paw = side + '_Rear_Paw'
        set_bone(upper, base_bones[upper]['location'], delta(base_bones[upper]['rotation'], (1, 0, 0), amount), base_bones[upper]['scale'], frame)
        set_bone(lower, base_bones[lower]['location'], delta(base_bones[lower]['rotation'], (1, 0, 0), -amount * 1.55), base_bones[lower]['scale'], frame)
        set_bone(paw, base_bones[paw]['location'], delta(base_bones[paw]['rotation'], (0, 0, 1), amount * 0.35), base_bones[paw]['scale'], frame)

for frame, amount in ((257, 0), (264, -7), (272, 8), (280, -6), (288, 0)):
    scene.frame_set(frame)
    for index, name in enumerate(('Tail_Start', 'Tail_1', 'Tail_2', 'Tail_3')):
        set_bone(name, base_bones[name]['location'], delta(tail_base[name], (0, 1, 0), amount * (1.0 - index * 0.16)), base_bones[name]['scale'], frame)

# Dedicated pinkie skin material. The source `rat` material is not modified.
pink = bpy.data.materials.get('PinkieSkin') or bpy.data.materials.new('PinkieSkin')
pink.use_nodes = True
nodes = pink.node_tree.nodes
links = pink.node_tree.links
nodes.clear()
out = nodes.new('ShaderNodeOutputMaterial')
out.location = (280, 0)
bsdf = nodes.new('ShaderNodeBsdfPrincipled')
bsdf.location = (0, 0)
bsdf.inputs['Roughness'].default_value = 0.58
if 'Subsurface Weight' in bsdf.inputs:
    bsdf.inputs['Subsurface Weight'].default_value = 0.045
if 'Subsurface Radius' in bsdf.inputs:
    bsdf.inputs['Subsurface Radius'].default_value = (1.0, 0.35, 0.4)
if 'Sheen Weight' in bsdf.inputs:
    bsdf.inputs['Sheen Weight'].default_value = 0.08
if os.path.exists(SKIN_TEXTURE):
    image = bpy.data.images.load(SKIN_TEXTURE, check_existing=True)
    image.colorspace_settings.name = 'sRGB'
    image_node = nodes.new('ShaderNodeTexImage')
    image_node.location = (-280, 0)
    image_node.image = image
    image_node.extension = 'REPEAT'
    links.new(image_node.outputs['Color'], bsdf.inputs['Base Color'])
else:
    bsdf.inputs['Base Color'].default_value = (0.95, 0.43, 0.53, 1.0)
links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
mesh.data.materials.clear()
mesh.data.materials.append(pink)

# Soft preview render for checking the pose.
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 800
scene.render.resolution_y = 600
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.world = bpy.data.worlds.new('PinkiePreviewWorld')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.018, 0.012, 0.018, 1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.28

def add_area(name, location, energy, size, color):
    data = bpy.data.lights.new(name, type='AREA')
    data.energy = energy
    data.shape = 'DISK'
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector((0, 0, 0)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    return obj

scene.frame_set(257)
bpy.context.view_layer.update()
corners = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
center = sum(corners, Vector()) / 8.0
radius = max((c - center).length for c in corners)
cam_data = bpy.data.cameras.new('PinkiePreviewCamera')
cam_data.type = 'ORTHO'
cam_data.ortho_scale = radius * 3.25
cam = bpy.data.objects.new('PinkiePreviewCamera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.location = center + Vector((radius * 3.2, -radius * 6.5, radius * 1.8))
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.clip_start = 0.0001
cam_data.clip_end = 10.0
add_area('Key', center + Vector((radius * 3, -radius * 3, radius * 4)), 18, radius * 3, (1.0, 0.72, 0.76))
add_area('Fill', center + Vector((-radius * 3, -radius * 2, radius)), 8, radius * 2, (0.72, 0.82, 1.0))
add_area('Rim', center + Vector((0, radius * 3, radius * 2)), 14, radius * 2, (1.0, 0.45, 0.5))

for frame in (257, 264, 272, 280, 288):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(PREVIEW_DIR, 'pinkie_kick_' + str(frame) + '.png')
    bpy.ops.render.render(write_still=True)

scene.frame_start = 257
scene.frame_end = 288
scene.frame_set(257)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)

# Export only the deformed mesh and armature; the original FBX is untouched.
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=FBX_OUT,
    use_selection=True,
    object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True,
    add_leaf_bones=False,
    use_armature_deform_only=False,
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    axis_forward='-Z',
    axis_up='Y',
)
print('PINKIE_BUILD_DONE', FBX_OUT, BLEND_OUT)
