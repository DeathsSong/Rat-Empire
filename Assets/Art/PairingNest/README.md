# Pairing Habitat nest asset

The runtime nest is the imported `rat_nest_box.fbx` model in
`Assets/Resources/PairingNest`. It contains the authored wooden walls/rims,
base, and the single `Nest_Bedding_Layer` surface from the supplied Blender
scene. The companion `rat_nest_box.blend.bytes` preserves the source scene
without asking Unity 2022 to invoke an external Blender importer.

`HabitatBuilder` loads the model once from `Resources/PairingNest/rat_nest_box`,
aligns its evaluated renderer bounds to the Pairing Habitat floor, and uses
the bedding renderer as the authoritative pinkie placement surface.
