import bpy
import os

fbx_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat.fbx'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)

print('=== DATA COUNTS ===', len(bpy.data.objects), len(bpy.data.meshes), len(bpy.data.materials))
print('ALL DATA OBJECTS', [o.name + ':' + o.type for o in bpy.data.objects])
print('=== OBJECTS ===')
for obj in bpy.context.scene.objects:
    print(obj.type, obj.name, 'parent=', obj.parent.name if obj.parent else None)
    if obj.type == 'ARMATURE':
        print('=== BONES ===')
        for bone in obj.data.bones:
            print('BONE', bone.name, 'parent=', bone.parent.name if bone.parent else None)
        print('=== ACTIONS ON OBJECT ===')
        print(obj.animation_data.action.name if obj.animation_data and obj.animation_data.action else '<none>')
        print('=== ALL ACTIONS ===')
        for action in bpy.data.actions:
            print('ACTION', action.name, 'frames=', tuple(round(v, 3) for v in action.frame_range), 'slots=', len(action.slots))
    if obj.type == 'MESH':
        print('MATERIALS', [m.name if m else None for m in obj.data.materials])
        print('MODIFIERS', [m.type for m in obj.modifiers])
