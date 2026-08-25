"""Measure a PMX model's canonical height and print the config-ready target.

The dev-released PMX files are the client meshes at true scale (1 MMD unit =
8cm), so the Foot->Head bone span at import IS the character's canonical GFL2
height. Doll `target_height_metres` policy is canon span x1.2, the factor that
converts GFL2 life-size into MENACE's slightly-oversized world.

Run inside Blender, one or more PMX paths after --:
    blender --background --python scripts/doll/measure_pmx_height.py -- "<model.pmx>" [...]
"""
import sys

import bpy  # type: ignore
import mathutils  # type: ignore

WORLD_FACTOR = 1.2


def measure(pmx: str) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        bpy.ops.preferences.addon_enable(module="mmd_tools")
    except Exception:
        bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
    bpy.ops.mmd_tools.import_model(
        filepath=pmx, scale=0.08, types={"MESH", "ARMATURE"}, clean_model=False)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")

    def z(name):
        b = arm.pose.bones.get(name)
        return None if b is None else (arm.matrix_world @ b.head.to_4d()).to_3d().z

    head, ankle = z("頭"), z("足首.L")
    crown = 0.0
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        for c in o.bound_box:
            crown = max(crown, (o.matrix_world @ mathutils.Vector(c)).z)
    if head is None or ankle is None:
        print(f"{pmx}: no MMD head/ankle bones, cannot measure")
        return
    span = head - ankle
    print(f"{pmx}:")
    print(f"  canon span (Foot->Head): {span:.3f} m, crown {crown:.3f} m")
    print(f"  target_height_metres at x{WORLD_FACTOR}: {span * WORLD_FACTOR:.3f}")


for path in (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []):
    measure(path)
