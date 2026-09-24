import bpy
import os
from mathutils import Vector

fbx_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat.fbx'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)
mesh = bpy.data.objects.get('rat_mesh')
arm = bpy.data.objects.get('Armature')
print('MESH loc', tuple(round(v,4) for v in mesh.location), 'rot', tuple(round(v,4) for v in mesh.rotation_euler), 'scale', tuple(round(v,4) for v in mesh.scale), 'dims', tuple(round(v,4) for v in mesh.dimensions))
print('ARM loc', tuple(round(v,4) for v in arm.location), 'rot', tuple(round(v,4) for v in arm.rotation_euler), 'scale', tuple(round(v,4) for v in arm.scale), 'dims', tuple(round(v,4) for v in arm.dimensions))
for name in ['Root','Spine','Chest','Head','Left_Front_UpperLeg','Left_Front_LowerLeg','Left_Front_Paw','Right_Front_UpperLeg','Left_Rear_UpperLeg','Left_Rear_LowerLeg','Left_Rear_Paw','Right_Rear_UpperLeg','Tail_Start']:
    b=arm.data.bones.get(name)
    if b: print('BONEPOS', name, 'head', tuple(round(v,4) for v in b.head_local), 'tail', tuple(round(v,4) for v in b.tail_local))
print('OBJ mats', [(m.name if m else None, list(mesh.material_slots).index(s) if s else None) for s in mesh.material_slots])
print('POLY MAT INDICES', sorted(set(p.material_index for p in mesh.data.polygons)))

