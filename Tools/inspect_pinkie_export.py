import bpy
import os

fbx_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat_Pinkie.fbx'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)
print('OBJECTS', [(o.name, o.type) for o in bpy.data.objects])
for obj in bpy.data.objects:
    if obj.type == 'ARMATURE':
        print('ARM_ACTION', obj.animation_data.action.name if obj.animation_data and obj.animation_data.action else None)
        if obj.animation_data and obj.animation_data.action:
            print('ARM_RANGE', tuple(obj.animation_data.action.frame_range))
    if obj.type == 'MESH':
        print('MESH_MATERIALS', [m.name if m else None for m in obj.data.materials])
for action in bpy.data.actions:
    print('ACTION', action.name, tuple(action.frame_range))

