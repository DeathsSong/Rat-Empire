import bpy
import os

fbx_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat.fbx'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)
arm = bpy.data.objects['Armature']
act = arm.animation_data.action
print('BEFORE', act.name, act.frame_range)
arm.pose.bones['Root'].rotation_mode = 'XYZ'
arm.pose.bones['Root'].rotation_euler = (0.0, 0.0, 0.0)
ok = arm.pose.bones['Root'].keyframe_insert(data_path='rotation_euler', frame=257, group='Pinkie Kick')
print('INSERT', ok, 'AFTER', act.name, act.frame_range)
for layer in act.layers:
    for strip in layer.strips:
        for cb in strip.channelbags:
            print('CHANNELBAG', cb.slot_handle, 'FCURVES', len(cb.fcurves))
            for fc in cb.fcurves:
                if 'Root' in fc.data_path:
                    print('ROOTFC', fc.data_path, fc.array_index, len(fc.keyframe_points), [p.co[:] for p in fc.keyframe_points[-3:]])

