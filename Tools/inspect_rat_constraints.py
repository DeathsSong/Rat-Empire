import bpy
import os

fbx_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat.fbx'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)
for obj in bpy.context.scene.objects:
    if obj.type == 'EMPTY' or obj.type == 'ARMATURE':
        print('OBJ', obj.name, 'constraints=', [(c.type, c.name, getattr(c, 'target', None).name if getattr(c, 'target', None) else None) for c in obj.constraints])
        if obj.animation_data and obj.animation_data.action:
            print(' ACTION', obj.animation_data.action.name, 'slots', len(obj.animation_data.action.slots))
            for layer in obj.animation_data.action.layers:
                for strip in layer.strips:
                    for channelbag in strip.channelbags:
                        for fc in channelbag.fcurves:
                            print('  FC', fc.data_path, fc.array_index, 'keys', len(fc.keyframe_points))
                            break
                        break
                    break
