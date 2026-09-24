import bpy, os
from mathutils import Quaternion, Vector
fbx=os.path.abspath(os.path.join(os.path.dirname(__file__),'..','Assets','Art','HandPaintedRat','HandPaintedRat.fbx'))
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=fbx,use_anim=True)
s=bpy.context.scene; arm=bpy.data.objects['Armature']; root=arm.pose.bones['Root']
for frame, q in [(256, Quaternion((1,0,0),0)), (288, Quaternion((1,0,0),3.14159))]:
 s.frame_set(frame); root.rotation_mode='QUATERNION'; root.rotation_quaternion=q; root.keyframe_insert(data_path='rotation_quaternion', frame=frame, group='Pinkie'); bpy.context.view_layer.update(); print('FRAME',frame,'ROT',tuple(round(x,4) for x in root.rotation_quaternion),'MAT',[[round(v,3) for v in row] for row in root.matrix.to_4x4()])
s.frame_set(256); print('AFTER256',tuple(round(x,4) for x in root.rotation_quaternion)); s.frame_set(288); print('AFTER288',tuple(round(x,4) for x in root.rotation_quaternion));

