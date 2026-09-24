import bpy
import os
from mathutils import Vector, Quaternion

fbx_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat.fbx'))
out_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Tools', 'preview'))
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 640
scene.render.resolution_y = 480
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.world = bpy.data.worlds.new('PreviewWorld')
scene.world.color = (0.035, 0.035, 0.035)
scene.frame_set(256)
mesh = bpy.data.objects['rat_mesh']
arm = bpy.data.objects['Armature']
root = arm.pose.bones['Root']
root.rotation_mode = 'QUATERNION'

# Calculate framing from mesh world bounds.
corners = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
center = sum(corners, Vector()) / 8.0
radius = max((c - center).length for c in corners)
cam_data = bpy.data.cameras.new('PreviewCamera')
cam = bpy.data.objects.new('PreviewCamera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.location = center + Vector((radius * 3.0, -radius * 4.0, radius * 2.0))
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 58

for name, axis in [('x', Vector((1,0,0))), ('y', Vector((0,1,0))), ('z', Vector((0,0,1)))]:
    scene.frame_set(288)
    root.rotation_mode = 'QUATERNION'
    root.rotation_quaternion = Quaternion(axis, 3.14159265)
    scene.render.filepath = os.path.join(out_dir, 'root_flip_' + name + '.png')
    bpy.ops.render.render(write_still=True)
