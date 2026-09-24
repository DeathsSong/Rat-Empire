import bpy
import os

fbx_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Art', 'HandPaintedRat', 'HandPaintedRat.fbx'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)
for mat in bpy.data.materials:
    print('MAT', mat.name, 'use_nodes=', mat.use_nodes)
    for node in mat.node_tree.nodes if mat.use_nodes else []:
        print(' NODE', node.type, node.name, 'label=', node.label)
        if node.type in {'BSDF_PRINCIPLED', 'RGB'}:
            for inp in node.inputs:
                if hasattr(inp, 'default_value'):
                    try:
                        print('  INPUT', inp.name, inp.default_value)
                    except Exception:
                        pass
        for inp in node.inputs:
            if inp.is_linked:
                print('  LINKED', inp.name, 'from=', inp.links[0].from_node.name, inp.links[0].from_socket.name)
        if node.type == 'TEX_IMAGE':
            print('  IMAGE', node.image.name if node.image else None, node.image.filepath if node.image else None)
