"""PMX-to-MENACE Blender conversion for Jiangyu.

Produces a single glTF carrying the PMX character's mesh + skeleton, renamed
to MENACE's humanoid bone convention, T-pose-calibrated against the reference
soldier's avatar, with attachment bones grafted on for weapon / equipment
sockets. The downstream Unity-side `Jiangyu.Mod.BakeHumanoid` Editor utility
consumes this glTF and bakes the avatar / material / LODGroup / animator into
an addition soldier prefab.

Pipeline:
  1. Parse the reference soldier glTF for armature shape + bone landmarks.
  2. Import PMX (mmd_tools), strip shape keys, scale to target height.
  3. Rename PMX bones to MENACE humanoid names via the config bone_map.
  4. Rebuild PMX materials as glTF-compatible Principled BSDFs.
  5. Remap vertex groups, drop ignored / unmapped groups, rebind meshes.
  6. Pose arm + foot chains to the reference avatar's T-pose calibration
     (rotations from the avatar's m_SkeletonPose) and bake the mesh.
  7. Graft reference attachment bones (sockets) onto the PMX armature so
     weapons / equipment attach correctly at runtime.
  8. Conform mesh names to `{basename}_LOD0..LODN`.
  9. Decimate each LOD per its ratio.
 10. Export glTF with standard settings. Source PMX textures pass through
     unchanged, one Principled BSDF material per source texture. The Unity
     side (Jiangyu's BakeHumanoid) is responsible for swapping in the
     Menace/character shader and wiring its Mask/Normal/Effect slots.

Run as:
    blender --background --python pmx_to_menace.py -- --config <config.json>
"""

import argparse
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    import bpy  # type: ignore
    import mathutils  # type: ignore
except ImportError as exc:  # pragma: no cover - Blender-only entry point
    raise SystemExit("This script must be run inside Blender.") from exc

try:
    import numpy as np  # type: ignore
except ImportError as exc:  # pragma: no cover - Blender ships numpy
    raise SystemExit("numpy is required (Blender bundles numpy by default).") from exc


# -----------------------------------------------------------------------------
# Config
# -----------------------------------------------------------------------------


@dataclass
class TransferConfig:
    pmx_path: Path
    reference_prefab_path: Path
    reference_avatar_path: Path
    output_path: Path
    source_mesh_names: list[str]
    bone_map: dict[str, str]
    ignore_bones: list[str]
    hip_leg_weight_blend: float
    height_scale_override: float | None
    target_height_metres: float | None
    lod_decimate_ratios: list[float]
    lod_mesh_basename: str

    @staticmethod
    def load(path: Path) -> "TransferConfig":
        data = json.loads(path.read_text(encoding="utf-8"))
        return TransferConfig(
            pmx_path=Path(data["pmx_path"]),
            reference_prefab_path=Path(data["reference_prefab_path"]),
            reference_avatar_path=Path(data["reference_avatar_path"]),
            output_path=Path(data["output_path"]),
            source_mesh_names=list(data.get("source_mesh_names", [])),
            bone_map=dict(data.get("bone_map", {})),
            ignore_bones=list(data.get("ignore_bones", [])),
            hip_leg_weight_blend=float(data.get("hip_leg_weight_blend", 0.0)),
            height_scale_override=data.get("height_scale_override"),
            target_height_metres=data.get("target_height_metres"),
            lod_decimate_ratios=[
                float(r) for r in data.get("lod_decimate_ratios", [1.0, 0.5, 0.25, 0.1])
            ],
            lod_mesh_basename=str(data.get("lod_mesh_basename", "character")),
        )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert a PMX model into a MENACE-compatible authored glTF."
    )
    parser.add_argument(
        "--config", required=True, help="Path to the transfer configuration JSON file."
    )
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = []
    return parser.parse_args(argv)


# -----------------------------------------------------------------------------
# Target contract (parsed directly from JSON, no Blender import)
# -----------------------------------------------------------------------------


# Mecanim humanoid joint hierarchy used by MENACE's soldier rig. The order
# of HUMANOID_BONE_HIERARCHY matches the layout of m_SkeletonPose in Unity's
# Avatar .asset, so it serves both as the index sequence for the avatar
# parser and as the source of truth for parent relationships. Any reference
# bone outside this set is treated as an attachment (socket / collider /
# equipment anchor) and grafted onto the PMX armature for retargeting.
HUMANOID_BONE_HIERARCHY: tuple[tuple[str, str | None], ...] = (
    ("Root", None),
    ("Hips", "Root"),
    ("Spine", "Hips"),
    ("Spine2", "Spine"),
    ("Neck", "Spine2"),
    ("Head", "Neck"),
    ("Shoulder_L", "Spine2"),
    ("UpperArm_L", "Shoulder_L"),
    ("LowerArm_L", "UpperArm_L"),
    ("Hand_L", "LowerArm_L"),
    ("Shoulder_R", "Spine2"),
    ("UpperArm_R", "Shoulder_R"),
    ("LowerArm_R", "UpperArm_R"),
    ("Hand_R", "LowerArm_R"),
    ("UpperLeg_L", "Hips"),
    ("LowerLeg_L", "UpperLeg_L"),
    ("Foot_L", "LowerLeg_L"),
    ("UpperLeg_R", "Hips"),
    ("LowerLeg_R", "UpperLeg_R"),
    ("Foot_R", "LowerLeg_R"),
)
HUMANOID_BONE_NAMES: frozenset[str] = frozenset(name for name, _ in HUMANOID_BONE_HIERARCHY)


# Length used for grafted attachment bones (Hand_*_Socket, Foot_*_ColliderRotator,
# backpack mount). Purely a visual length in the armature, never affects runtime
# transforms.
ATTACHMENT_BONE_LENGTH_METRES: float = 0.05

# Fallback foot-bone length used when the PMX rig's foot bone has zero or
# near-zero length after import. Roughly matches the reference soldier's
# Foot_L bone length so the retargeted REST direction reads correctly.
FOOT_BONE_FALLBACK_LENGTH_METRES: float = 0.226

# Hips↔UpperLeg weight blend radius, expressed as a multiplier on the
# Hips→UpperLeg world distance. Verts within this radius whose Hips weight
# exceeds min_hips_weight get some of their weight redistributed.
HIPS_UPPERLEG_BLEND_RADIUS_FACTOR: float = 2.0


@dataclass
class ReferenceBone:
    name: str
    parent_name: str | None
    # World matrix in the reference glTF's coordinate system. Translations are
    # in the source unit (Unity meters when extracted via AssetRipper).
    world_matrix: "mathutils.Matrix"


@dataclass
class ReferenceArmature:
    bones: list[ReferenceBone]
    bone_by_name: dict[str, ReferenceBone]
    # Reference soldier height in metres (Hips-to-Head world delta). Used to
    # compute a uniform pre-scale for the PMX character so it lands at a sensible
    # world size while keeping its own proportions.
    height_metres: float
    # Full armature Y-span (lowest bone Y to highest bone Y) in metres. This is
    # what the auto pre-scale matches against, using just Hips→Head leaves
    # characters with longer legs visibly taller than the reference even though
    # their torsos line up. Y-span captures the whole body height.
    yspan_metres: float


# Per-bone (head→tail direction, twist reference) pair in Blender Z-up world
# frame, keyed by humanoid bone name. Returned by parse_avatar_humanoid_tpose
# and consumed by the T-pose calibration step.
ReferenceTPose = dict[str, tuple["mathutils.Vector", "mathutils.Vector"]]


@dataclass
class MirrorFrame:
    """Axis-flip relationship between the PMX rig and the reference Avatar's
    body frame.

    PMX/MMD characters and Unity humanoid characters may be authored in
    different starting orientations. ``mirror_x`` is true when the rigs put
    their L sides on opposite world-X signs. ``mirror_y`` is true when they
    face opposite world-Y directions. Both true is equivalent to a 180°
    rotation around Z, the typical case for a PMX character facing the
    opposite way from the reference.
    """
    mirror_x: bool
    mirror_y: bool

    def apply(
        self, v: "mathutils.Vector | None", apply_y: bool = True
    ) -> "mathutils.Vector | None":
        if v is None:
            return None
        return mathutils.Vector((
            -v.x if self.mirror_x else v.x,
            -v.y if (self.mirror_y and apply_y) else v.y,
            v.z,
        ))


def parse_reference_armature(gltf_path: Path) -> ReferenceArmature:
    """Load every joint reachable from any skin in the reference glTF.

    Bone names are taken straight from the glTF nodes. The caller relies on
    MENACE's bone naming being stable in the vanilla soldier export.
    """
    if not gltf_path.exists():
        raise FileNotFoundError(f"reference prefab glTF not found: {gltf_path}")
    data = json.loads(gltf_path.read_text(encoding="utf-8"))
    nodes = data.get("nodes", [])

    parent_index: list[int | None] = [None] * len(nodes)
    for i, n in enumerate(nodes):
        for c in n.get("children") or []:
            parent_index[c] = i

    world_matrices: list[mathutils.Matrix | None] = [None] * len(nodes)

    def local_matrix(i: int) -> mathutils.Matrix:
        n = nodes[i]
        if "matrix" in n:
            m = n["matrix"]
            mat = mathutils.Matrix(
                (
                    (m[0], m[4], m[8], m[12]),
                    (m[1], m[5], m[9], m[13]),
                    (m[2], m[6], m[10], m[14]),
                    (m[3], m[7], m[11], m[15]),
                )
            )
            return mat
        t = mathutils.Vector(n.get("translation", (0.0, 0.0, 0.0)))
        r = n.get("rotation", (0.0, 0.0, 0.0, 1.0))
        rq = mathutils.Quaternion((r[3], r[0], r[1], r[2]))
        s = mathutils.Vector(n.get("scale", (1.0, 1.0, 1.0)))
        return mathutils.Matrix.LocRotScale(t, rq, s)

    def world_of(i: int) -> mathutils.Matrix:
        cached = world_matrices[i]
        if cached is not None:
            return cached
        local = local_matrix(i)
        parent = parent_index[i]
        world = local if parent is None else world_of(parent) @ local
        world_matrices[i] = world
        return world

    for i in range(len(nodes)):
        world_of(i)

    joint_indices: set[int] = set()
    for skin in data.get("skins", []):
        for j in skin.get("joints", []) or []:
            joint_indices.add(int(j))
    if not joint_indices:
        raise RuntimeError(
            "reference prefab glTF has no skin joints. Cannot extract a bone hierarchy."
        )

    # Expand the bone set to include every descendant of a joint. MENACE's
    # vanilla soldier export carries attachment sockets ("Hand_L_Socket",
    # "backpack", "Foot_*_ColliderRotator") as children of humanoid joints but
    # NOT in the skin's joints array (they're sockets, never skinned to). They
    # still need to participate in the grafted-bone set so PrefabAttachment
    # lookups by name resolve on the the PMX character rig.
    bone_indices: set[int] = set(joint_indices)
    changed = True
    while changed:
        changed = False
        for i, n in enumerate(nodes):
            if i not in bone_indices:
                continue
            for c in n.get("children") or []:
                if c not in bone_indices:
                    bone_indices.add(int(c))
                    changed = True

    # Drop nodes whose entire descendant subtree is unnamed, those are mesh
    # primitives or other non-bone artifacts the glTF exporter leaves behind.
    def has_name(i: int) -> bool:
        return bool(nodes[i].get("name"))

    bone_indices = {i for i in bone_indices if has_name(i)}

    def closest_bone_ancestor(i: int) -> int | None:
        p = parent_index[i]
        while p is not None and p not in bone_indices:
            p = parent_index[p]
        return p

    bones: list[ReferenceBone] = []
    bone_by_name: dict[str, ReferenceBone] = {}
    name_collisions: dict[str, int] = {}
    for i in sorted(bone_indices):
        raw_name = nodes[i].get("name") or f"node_{i}"
        if raw_name in bone_by_name:
            name_collisions[raw_name] = name_collisions.get(raw_name, 1) + 1
            continue
        parent_i = closest_bone_ancestor(i)
        parent_name = nodes[parent_i].get("name") if parent_i is not None else None
        bone = ReferenceBone(
            name=raw_name,
            parent_name=parent_name,
            world_matrix=world_matrices[i],
        )
        bones.append(bone)
        bone_by_name[raw_name] = bone

    if name_collisions:
        details = ", ".join(f"{n}×{c}" for n, c in sorted(name_collisions.items()))
        print(
            f"[warn] reference armature has duplicate joint names. Keeping first only: {details}"
        )

    hips = bone_by_name.get("Hips")
    head = bone_by_name.get("Head")
    if hips is None or head is None:
        raise RuntimeError(
            "reference armature is missing Hips or Head. Cannot determine reference height."
        )
    height = abs((head.world_matrix.translation - hips.world_matrix.translation).length)
    if height <= 0.001:
        raise RuntimeError(
            f"reference armature Hips→Head distance is too small ({height:.4f}). Is the reference scaled correctly?"
        )

    # Body height = floor-to-head landmark. Using ONLY the named humanoid
    # landmarks (Foot_L and Head world positions) avoids the trap of picking
    # up hair / accessory / IK-helper extremities in min/max across the whole
    # armature. Computed as 3D Euclidean distance because the reference uses
    # Y-up (glTF convention) but Blender uses Z-up natively, and we want the
    # same measure on both rigs without per-axis-convention adapters. For a
    # standing character, Foot→Head 3D distance ≈ Y-axis floor-to-head.
    foot_l = bone_by_name.get("Foot_L")
    if foot_l is None:
        raise RuntimeError("reference armature is missing Foot_L. Cannot determine body height.")
    body_height = (head.world_matrix.translation - foot_l.world_matrix.translation).length
    if body_height <= 0.001:
        raise RuntimeError(
            f"reference Foot_L→Head delta is too small ({body_height:.4f}). Is the reference scaled correctly?"
        )

    return ReferenceArmature(
        bones=bones,
        bone_by_name=bone_by_name,
        height_metres=height,
        yspan_metres=body_height,
    )


def attachment_bones_for_graft(reference: ReferenceArmature) -> list[ReferenceBone]:
    """All reference bones that aren't part of the Mecanim humanoid set.

    Returned in topological order (each bone after all of its ancestors) so
    grafting can resolve parents that were themselves grafted earlier.
    """
    candidates = [b for b in reference.bones if b.name not in HUMANOID_BONE_NAMES]
    by_name = {b.name: b for b in candidates}

    ordered: list[ReferenceBone] = []
    placed: set[str] = set(HUMANOID_BONE_NAMES)

    def depth(b: ReferenceBone) -> int:
        d = 0
        cur: ReferenceBone | None = b
        while cur is not None and cur.parent_name is not None:
            parent = reference.bone_by_name.get(cur.parent_name)
            cur = parent
            d += 1
        return d

    for b in sorted(candidates, key=depth):
        # Skip bones whose ancestor chain doesn't lead to either a humanoid
        # bone or another to-be-grafted bone we'll reach. In practice MENACE
        # attachments always parent under the humanoid skeleton, but a stray
        # disconnected node would just be dropped.
        cur = b
        chain_ok = True
        while cur.parent_name is not None and cur.parent_name not in placed:
            if cur.parent_name not in by_name:
                chain_ok = False
                break
            cur = by_name[cur.parent_name]
        if not chain_ok:
            print(f"[warn] reference attachment bone '{b.name}' has no humanoid ancestor. Skipping.")
            continue
        ordered.append(b)
        placed.add(b.name)

    return ordered


def ensure_armature_modifier(mesh_obj: "bpy.types.Object", armature_obj: "bpy.types.Object") -> None:
    for mod in mesh_obj.modifiers:
        if mod.type == "ARMATURE" and mod.object == armature_obj:
            return
    mod = mesh_obj.modifiers.new(name="Armature", type="ARMATURE")
    mod.object = armature_obj


def apply_armature_modifier(mesh_obj: "bpy.types.Object") -> None:
    clear_selection()
    bpy.context.view_layer.objects.active = mesh_obj
    mesh_obj.select_set(True)
    for mod in list(mesh_obj.modifiers):
        if mod.type == "ARMATURE":
            try:
                bpy.ops.object.modifier_apply(modifier=mod.name)
            except RuntimeError as e:
                print(f"[warn] modifier_apply on {mesh_obj.name}/{mod.name} failed: {e}")


def parse_avatar_humanoid_tpose(avatar_asset_path: Path) -> ReferenceTPose:
    """Read the reference Avatar .asset and return each humanoid bone's
    head→tail direction (bone Y) and twist reference (bone Z) at the
    avatar's T-pose calibration, expressed as unit vectors in Blender's
    Z-up world frame.

    The avatar's m_Human.m_Skeleton block stores per-bone local TRS for the
    20-node humanoid skeleton (Root, Hips, Spine, Chest/Spine2, Neck, Head,
    Shoulder_L, UpperArm_L, LowerArm_L, Hand_L, Shoulder_R, UpperArm_R,
    LowerArm_R, Hand_R, UpperLeg_L, LowerLeg_L, Foot_L, UpperLeg_R,
    LowerLeg_R, Foot_R) in glTF Y-up. We cascade through the canonical
    parent chain to get each bone's world rotation in Y-up, apply that to
    the bone-local Y and Z axes to get the bone's frame in Y-up world,
    then convert the direction vectors to Blender Z-up via the standard
    axis swap. Returning both Y and Z lets the caller fully orient
    the PMX character's bones (head→tail direction AND roll) to match the
    reference's calibration, matching only Y leaves the bone's twist
    asymmetric between L and R sides and produces gun-orientation bugs.
    """
    bone_names = [name for name, _ in HUMANOID_BONE_HIERARCHY]
    name_to_index = {name: i for i, name in enumerate(bone_names)}
    parent_indices = [
        name_to_index[parent] if parent is not None else -1
        for _, parent in HUMANOID_BONE_HIERARCHY
    ]

    import re
    text = avatar_asset_path.read_text(encoding="utf-8")
    human_start = text.find("    m_Human:")
    if human_start == -1:
        raise RuntimeError(f"m_Human not found in {avatar_asset_path}")
    sk_start = text.find("m_Skeleton:", human_start)
    sk_end = text.find("m_LeftHand:", sk_start)
    pose_start = text.find("m_SkeletonPose:", sk_start)
    section = text[pose_start:sk_end if sk_end > 0 else len(text)]
    entries = re.findall(
        r"- t: \{x: (?P<tx>[^,]+), y: (?P<ty>[^,]+), z: (?P<tz>[^}]+)\}\s+"
        r"q: \{x: (?P<qx>[^,]+), y: (?P<qy>[^,]+), z: (?P<qz>[^,]+), w: (?P<qw>[^}]+)\}\s+"
        r"s: \{x: (?P<sx>[^,]+), y: (?P<sy>[^,]+), z: (?P<sz>[^}]+)\}",
        section,
    )
    if len(entries) < len(bone_names):
        raise RuntimeError(
            f"avatar humanoid skeleton has {len(entries)} entries, expected at least {len(bone_names)}"
        )

    # Cascade local rotations through the parent chain (in Y-up).
    world_q_yup: list[mathutils.Quaternion] = [None] * len(bone_names)
    for i in range(len(bone_names)):
        e = entries[i]
        local_q = mathutils.Quaternion((float(e[6]), float(e[3]), float(e[4]), float(e[5])))
        p = parent_indices[i]
        world_q_yup[i] = local_q if p == -1 else world_q_yup[p] @ local_q

    # Y-up → Z-up direction conversion: (x, y, z)_yup = (x, -z, y)_zup,
    # which is the same as rotating directions by +90° around X.
    yup_to_zup_3x3 = mathutils.Matrix.Rotation(math.pi / 2, 3, "X")

    result: ReferenceTPose = {}
    for i, name in enumerate(bone_names):
        bone_y_yup = world_q_yup[i] @ mathutils.Vector((0.0, 1.0, 0.0))
        bone_z_yup = world_q_yup[i] @ mathutils.Vector((0.0, 0.0, 1.0))
        bone_y_zup = (yup_to_zup_3x3 @ bone_y_yup).normalized()
        bone_z_zup = (yup_to_zup_3x3 @ bone_z_yup).normalized()
        result[name] = (bone_y_zup, bone_z_zup)
    return result


def _avatar_bone_yz(
    name: str, reference_tpose: ReferenceTPose
) -> tuple["mathutils.Vector | None", "mathutils.Vector | None"]:
    pair = reference_tpose.get(name)
    if pair is None:
        return None, None
    y, z = pair
    return y.copy(), z.copy()


def detect_pmx_mirror_frame(
    armature_obj: "bpy.types.Object", reference_tpose: ReferenceTPose
) -> MirrorFrame:
    """Detect L/R-side and facing-direction flips between the PMX rig and
    the reference Avatar.

    L/R side: compare the UpperArm_L tail-direction X sign on both rigs.

    Facing: compare Foot_L tail-direction Y on both rigs. PMX/MMD's 足首.L
    has its TAIL at the heel anchor (BEHIND the ankle, opposite facing).
    Unity humanoid foot bones have their TAIL at the TOE (forward of the
    ankle, same as facing). The foot's Y direction therefore has opposite
    meaning between the two rigs.
    """
    pmx_upper_l = armature_obj.pose.bones.get("UpperArm_L")
    pmx_l_on_plus_x = pmx_upper_l is not None and (pmx_upper_l.tail - pmx_upper_l.head).x > 0
    ref_upper_l_y, _ = _avatar_bone_yz("UpperArm_L", reference_tpose)
    ref_l_on_plus_x = ref_upper_l_y is not None and ref_upper_l_y.x > 0
    mirror_x = pmx_l_on_plus_x != ref_l_on_plus_x

    pmx_foot_l = armature_obj.pose.bones.get("Foot_L")
    pmx_foot_dir_y = (pmx_foot_l.tail - pmx_foot_l.head).y if pmx_foot_l else 0.0
    pmx_faces_plus_y = pmx_foot_dir_y < 0  # PMX heel-anchor: facing is opposite.
    ref_foot_l_y, _ = _avatar_bone_yz("Foot_L", reference_tpose)
    ref_faces_plus_y = ref_foot_l_y is not None and ref_foot_l_y.y > 0  # Unity toe-anchor: facing matches.
    mirror_y = pmx_faces_plus_y != ref_faces_plus_y

    print(
        f"[info] character mirror_x = {mirror_x} (pmx L on +X: {pmx_l_on_plus_x}, ref L on +X: {ref_l_on_plus_x}). "
        f"mirror_y = {mirror_y} (pmx faces +Y: {pmx_faces_plus_y}, ref faces +Y: {ref_faces_plus_y})"
    )
    return MirrorFrame(mirror_x=mirror_x, mirror_y=mirror_y)


def _resolve_bone_basis(
    bone_name: str,
    reference_tpose: ReferenceTPose,
    frame: MirrorFrame,
    apply_y: bool,
) -> tuple["mathutils.Vector", "mathutils.Vector", "mathutils.Vector"] | None:
    """Look up the target (X, Y, Z) world basis for ``bone_name`` from the
    reference Avatar's T-pose, mirrored into the PMX rig's body frame.

    Y is the bone's head→tail direction. Z is the twist reference. X is the
    right-handed completion (Y × Z) after Z is re-orthogonalised against Y.

    Returns None when the avatar T-pose has no data for that bone.
    """
    target_y, target_z = _avatar_bone_yz(bone_name, reference_tpose)
    if target_y is None or target_z is None:
        return None
    target_y = frame.apply(target_y, apply_y=apply_y).normalized()
    target_z = frame.apply(target_z, apply_y=apply_y).normalized()
    # Re-orthogonalise Z against Y (rounding might have made them not
    # perpendicular) and rebuild as a right-handed basis.
    target_z = (target_z - target_z.dot(target_y) * target_y).normalized()
    target_x = target_y.cross(target_z).normalized()
    return target_x, target_y, target_z


def pose_arm_chains_and_bake(
    armature_obj: "bpy.types.Object",
    mesh_objects: list["bpy.types.Object"],
    reference_tpose: ReferenceTPose,
    frame: MirrorFrame,
) -> int:
    """Pose both arm chains to the reference Avatar's T-pose, bake mesh
    deformation, and apply the pose as the new rest.

    For the gun-grip animation to land the hand where the reference's would,
    the PMX character's bind pose must also be at T-pose for the arm chain.
    Shoulder bones are NOT touched. Shoulder rest direction varies across
    rigs and forcing it to a target tears the chest/shoulder mesh seam
    during the bake. Only UpperArm → LowerArm → Hand needs T-pose alignment.

    mirror_y is NOT applied to arm bones. The body-frame Y-flip is only
    needed for bones with a PMX-vs-Unity convention mismatch (the feet).
    Arm bones use the same Y-axis convention across both rigs, and the
    runtime gun mesh is authored against the reference's world-frame hand
    orientation. Flipping Y would render the gun upside-down.

    Returns the number of bones rotated.
    """
    chain_bones = (
        ("UpperArm_L", "LowerArm_L", "Hand_L"),
        ("UpperArm_R", "LowerArm_R", "Hand_R"),
    )

    clear_selection()
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")

    rotated = 0
    for chain in chain_bones:
        for bone_name in chain:
            basis = _resolve_bone_basis(bone_name, reference_tpose, frame, apply_y=False)
            if basis is None:
                print(f"[warn] no avatar T-pose data for '{bone_name}'. Skipping.")
                continue
            target_x, target_y, target_z = basis
            pmx_pb = armature_obj.pose.bones.get(bone_name)
            if pmx_pb is None:
                continue
            head = pmx_pb.head.copy()
            # 3x3 with columns = (X, Y, Z) axes in world. Blender's Matrix
            # constructor takes rows, so build the row-major form directly.
            m = mathutils.Matrix((
                (target_x.x, target_y.x, target_z.x),
                (target_x.y, target_y.y, target_z.y),
                (target_x.z, target_y.z, target_z.z),
            ))
            pmx_pb.matrix = mathutils.Matrix.Translation(head) @ m.to_4x4()
            bpy.context.view_layer.update()
            rotated += 1

    bpy.ops.object.mode_set(mode="OBJECT")
    if rotated == 0:
        print("[info] arm chains already at T-pose. Skipping pose bake.")
        return 0

    print(f"[info] posed {rotated} arm bone(s) to T-pose direction. Baking mesh.")
    for mesh in mesh_objects:
        ensure_armature_modifier(mesh, armature_obj)
        # Dual Quaternion Skinning preserves volume at the elbow where the
        # pose rotation is largest. LBS would collapse joint volume on bends
        # and mangle the elbow mesh.
        for mod in mesh.modifiers:
            if mod.type == "ARMATURE":
                mod.use_deform_preserve_volume = True
        apply_armature_modifier(mesh)

    clear_selection()
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply()
    bpy.ops.object.mode_set(mode="OBJECT")

    for mesh in mesh_objects:
        ensure_armature_modifier(mesh, armature_obj)
    return rotated


def retarget_foot_bones_rest(
    armature_obj: "bpy.types.Object",
    reference_tpose: ReferenceTPose,
    frame: MirrorFrame,
) -> int:
    """Change foot bone REST direction without rotating the mesh.

    The reference Avatar's T-pose foot Y direction is 35° below horizontal
    (forward and down), the natural foot-bone direction for a flat-foot
    stance. A pose+bake here would rotate the PMX foot mesh into a
    tippy-toes look because the PMX mesh wasn't authored against Unity's
    foot-bone-toward-toe convention. Edit-mode head/tail change gives the
    bone the right REST direction while the mesh stays at its PMX rest
    visual.

    Returns the number of foot bones retargeted.
    """
    foot_bones = ("Foot_L", "Foot_R")

    clear_selection()
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones = armature_obj.data.edit_bones

    retargeted = 0
    for bone_name in foot_bones:
        basis = _resolve_bone_basis(bone_name, reference_tpose, frame, apply_y=True)
        if basis is None:
            print(f"[warn] no avatar T-pose data for foot '{bone_name}'. Skipping.")
            continue
        _, target_y, target_z = basis
        eb = edit_bones.get(bone_name)
        if eb is None:
            print(f"[warn] foot bone '{bone_name}' missing in edit mode. Skipping.")
            continue
        length = (eb.tail - eb.head).length
        if length < 1e-6:
            length = FOOT_BONE_FALLBACK_LENGTH_METRES
        head = eb.head.copy()
        eb.tail = head + target_y * length
        eb.align_roll(target_z)
        retargeted += 1

    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[info] retargeted {retargeted} foot bone REST direction(s) via edit mode (mesh unchanged).")
    return retargeted


def apply_reference_tpose_calibration(
    armature_obj: "bpy.types.Object",
    mesh_objects: list["bpy.types.Object"],
    reference_tpose: ReferenceTPose,
) -> int:
    """Calibrate the PMX rig to the reference Avatar's T-pose.

    Three-step orchestration:
      1. Detect L/R-side and facing-direction flips between the two rigs.
      2. Pose both arm chains to T-pose, bake the deformation into the mesh,
         and apply the pose as the new rest.
      3. Retarget foot bone REST directions in edit mode without rotating
         the mesh.

    MENACE animations are authored against the vanilla soldier's humanoid
    Avatar, which is T-pose calibrated. For the gun-grip animation to land
    the hand where the reference's would, the PMX character's bind pose
    must match. Posing in pose mode, applying the Armature modifier on the
    mesh, then applying pose-as-rest gives T-pose. The Unity-side Avatar
    build then captures T-pose as muscle-zero.

    Only the arm chain and feet are touched. Hips, Spine, and Legs stay at
    the PMX character's PMX rest. Their calibration mismatch with the
    reference's avatar is small and doesn't show up as obviously as the
    gun-grip arm mispositioning.

    Returns the number of arm bones rotated in step 2.
    """
    if armature_obj.type != "ARMATURE":
        raise RuntimeError("apply_reference_tpose_calibration target must be an armature.")

    frame = detect_pmx_mirror_frame(armature_obj, reference_tpose)
    rotated = pose_arm_chains_and_bake(armature_obj, mesh_objects, reference_tpose, frame)
    retarget_foot_bones_rest(armature_obj, reference_tpose, frame)
    return rotated


def graft_attachment_bones(
    pmx_armature_obj,
    reference: ReferenceArmature,
) -> int:
    """Add reference attachment bones to the PMX character's armature.

    Both POSITION and ORIENTATION are taken from the reference relative to
    the parent bone. We compute the reference child's full transform in the
    reference parent's local frame, convert from glTF Y-up to Blender Z-up
    (the reference glTF is Y-up. Blender uses Z-up natively, so a raw delta
    would be applied to the wrong axes), and apply that local transform to
    the PMX character's parent bone in Blender. The grafted bone ends up with the
    same parent-relative position AND rotation as the reference's socket,
    so MENACE PrefabAttachment slots (gun grips, backpack mount, foot
    collider rotators) end up oriented correctly when the runtime parents an
    attached GameObject to them.

    Returns the number of bones grafted.
    """
    if pmx_armature_obj.type != "ARMATURE":
        raise RuntimeError("graft target must be an armature object.")

    to_graft = attachment_bones_for_graft(reference)
    if not to_graft:
        return 0

    clear_selection()
    bpy.context.view_layer.objects.active = pmx_armature_obj
    pmx_armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    edit_bones = pmx_armature_obj.data.edit_bones

    grafted = 0
    skipped = 0
    for ref_bone in to_graft:
        parent_name = ref_bone.parent_name
        if parent_name is None:
            print(f"[warn] reference attachment bone '{ref_bone.name}' has no parent. Skipping.")
            skipped += 1
            continue
        parent_eb = edit_bones.get(parent_name)
        if parent_eb is None:
            print(
                f"[warn] reference attachment bone '{ref_bone.name}' expects parent "
                f"'{parent_name}' which is missing on the PMX character. Skipping."
            )
            skipped += 1
            continue
        ref_parent = reference.bone_by_name.get(parent_name)
        if ref_parent is None:
            skipped += 1
            continue

        if ref_bone.name in edit_bones:
            print(f"[info] attachment bone '{ref_bone.name}' already on the PMX character. Skipping graft.")
            skipped += 1
            continue

        # Parent-LOCAL graft: compute the socket's transform relative to
        # its parent in the reference's data, then apply that same local
        # transform to the PMX character's parent's armature-local matrix. The
        # local relationship is intrinsic to the bone hierarchy (e.g.
        # Hand_R_Socket is identity-local to Hand_R, Foot_*_ColliderRotator
        # is a 33° X rotation off the foot bone) and composes correctly
        # regardless of whether the parent's world rotation differs between
        # the reference and the PMX character. A world-delta graft would place the
        # socket at the reference's world orientation, which is wrong once
        # her parent bone's world rotation diverges (visible as the gun
        # being rotated wrong on the PMX character but correct on the reference).
        ref_socket_local = ref_parent.world_matrix.inverted() @ ref_bone.world_matrix
        socket_matrix = parent_eb.matrix @ ref_socket_local
        socket_head = socket_matrix.to_translation()
        socket_y = mathutils.Vector(socket_matrix.col[1]).to_3d().normalized()
        socket_z = mathutils.Vector(socket_matrix.col[2]).to_3d().normalized()
        new_bone = edit_bones.new(ref_bone.name)
        new_bone.head = socket_head
        new_bone.tail = socket_head + socket_y * ATTACHMENT_BONE_LENGTH_METRES
        new_bone.align_roll(socket_z)
        new_bone.parent = parent_eb
        new_bone.use_connect = False
        new_bone.use_deform = False
        grafted += 1

    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[info] grafted {grafted} attachment bone(s). Skipped {skipped}.")
    return grafted


# -----------------------------------------------------------------------------
# Scene + PMX import helpers
# -----------------------------------------------------------------------------


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def clear_selection() -> None:
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")


def _mmd_import_available() -> bool:
    if not hasattr(bpy.ops, "mmd_tools"):
        return False
    return "import_model" in dir(bpy.ops.mmd_tools)


def ensure_mmd_tools_enabled() -> None:
    if _mmd_import_available():
        return
    candidates = [
        "bl_ext.blender_org.mmd_tools",
        "bl_ext.user_default.mmd_tools",
        "mmd_tools",
    ]
    last_error: Exception | None = None
    for module in candidates:
        try:
            bpy.ops.preferences.addon_enable(module=module)
        except Exception as e:
            last_error = e
            continue
        if _mmd_import_available():
            print(f"[info] enabled mmd_tools via '{module}'")
            return
    raise RuntimeError(
        f"mmd_tools is not available after addon_enable attempts. Last error: {last_error}"
    )


def import_pmx(path: Path) -> list:
    if not path.exists():
        raise FileNotFoundError(f"PMX file not found: {path}")
    ensure_mmd_tools_enabled()
    before = {obj.name for obj in bpy.data.objects}
    bpy.ops.mmd_tools.import_model(filepath=str(path))
    return [obj for obj in bpy.data.objects if obj.name not in before]


def find_pmx_armature(objects) -> "bpy.types.Object":
    armatures = [obj for obj in objects if obj.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError("PMX import produced no armature.")
    # mmd_tools creates a single armature per model.
    return armatures[0]


def find_pmx_meshes(objects, names_whitelist: list[str]) -> list:
    meshes = [obj for obj in objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("PMX import produced no mesh.")
    if names_whitelist:
        wanted = set(names_whitelist)
        chosen = [obj for obj in meshes if obj.name in wanted]
        missing = sorted(wanted.difference(obj.name for obj in chosen))
        if missing:
            raise RuntimeError(
                "Configured source mesh objects were not found: " + ", ".join(missing)
            )
        return chosen
    return meshes


# -----------------------------------------------------------------------------
# Pre-scale
# -----------------------------------------------------------------------------


def pmx_bone_world_position(armature_obj: "bpy.types.Object", bone_name: str) -> "mathutils.Vector | None":
    bone = armature_obj.data.bones.get(bone_name)
    if bone is None:
        return None
    return armature_obj.matrix_world @ bone.head_local


def apply_transform_safe(obj, *, location: bool, rotation: bool, scale: bool) -> None:
    clear_selection()
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    try:
        bpy.ops.object.transform_apply(
            location=location, rotation=rotation, scale=scale
        )
    except RuntimeError as e:
        print(f"[warn] transform_apply on {obj.name} failed: {e}")


def make_single_user(objects) -> None:
    clear_selection()
    for obj in objects:
        obj.select_set(True)
    if objects:
        bpy.context.view_layer.objects.active = objects[0]
    try:
        bpy.ops.object.make_single_user(
            object=True, obdata=True, material=False, animation=False
        )
    except RuntimeError as e:
        print(f"[warn] make_single_user failed: {e}")


def apply_uniform_scale(armature_obj: "bpy.types.Object", mesh_objects: list["bpy.types.Object"], factor: float) -> None:
    objects = [armature_obj] + list(mesh_objects)

    # Detach EVERY object from its parent while preserving world transforms.
    # This includes detaching meshes from the armature: if we left them
    # parented, Blender would auto-compensate the child's matrix_basis when
    # the parent's transform_apply ran, inflating the mesh's stored scale
    # to (parent_scale × child_scale). When the mesh's own apply then ran,
    # it would bake the compounded scale, leaving the mesh at scale² while
    # the armature got scale¹, a hard-to-spot misalignment that grew with
    # height up the body.
    for obj in objects:
        if obj.parent is not None:
            world = obj.matrix_world.copy()
            obj.parent = None
            obj.matrix_world = world

    # mmd_tools imports can leave mesh and armature data shared across multiple objects
    # (e.g., proxy rigs, LOD helpers). Force single-user so transform_apply succeeds.
    make_single_user(objects)

    # Bake the existing post-import basis (scale + rotation from mmd_tools) into data.
    for obj in objects:
        apply_transform_safe(obj, location=True, rotation=True, scale=True)

    if abs(factor - 1.0) >= 1e-6:
        for obj in objects:
            obj.scale = (factor, factor, factor)
        for obj in objects:
            apply_transform_safe(obj, location=False, rotation=False, scale=True)

    clear_selection()


# -----------------------------------------------------------------------------
# Authored armature
# -----------------------------------------------------------------------------


def resolve_case_insensitive(path: Path) -> Path | None:
    if path.exists():
        return path
    parts = path.parts
    if not parts:
        return None
    current = Path(parts[0]) if path.is_absolute() else Path(".")
    start = 1 if path.is_absolute() else 0
    for part in parts[start:]:
        try:
            entries = list(current.iterdir())
        except OSError:
            return None
        match = next((e for e in entries if e.name.lower() == part.lower()), None)
        if match is None:
            return None
        current = match
    return current


def resolve_texture_path(raw_path: str, pmx_path: Path) -> Path | None:
    if not raw_path:
        return None
    candidate = Path(raw_path)
    roots: list[Path] = []
    if candidate.is_absolute():
        roots.append(candidate)
    else:
        roots.append((pmx_path.parent / candidate).resolve())
        roots.append((pmx_path.parent / raw_path).resolve())
    for r in roots:
        resolved = resolve_case_insensitive(r)
        if resolved is not None and resolved.is_file():
            return resolved
    return None


def collect_materials(mesh_objects: list["bpy.types.Object"]) -> list["bpy.types.Material"]:
    seen: list = []
    seen_ids: set[int] = set()
    for mesh_obj in mesh_objects:
        for slot in mesh_obj.material_slots:
            mat = slot.material
            if mat is None:
                continue
            key = mat.as_pointer()
            if key in seen_ids:
                continue
            seen_ids.add(key)
            seen.append(mat)
    return seen


def choose_base_texture_node(material):
    if material is None or not material.use_nodes or material.node_tree is None:
        return None
    nodes = material.node_tree.nodes
    preferred = nodes.get("mmd_base_tex")
    if (
        preferred is not None
        and preferred.type == "TEX_IMAGE"
        and getattr(preferred, "image", None) is not None
    ):
        return preferred
    for node in nodes:
        if node.type == "TEX_IMAGE" and getattr(node, "image", None) is not None:
            return node
    return None


def material_uses_alpha(material) -> bool:
    if material is None:
        return False
    tokens = ("lash", "brow", "eye", "shadow", "emotion", "mask", "hair", "cloth")
    return any(tok in material.name.lower() for tok in tokens)


def repair_texture_paths(materials, pmx_path: Path) -> None:
    for mat in materials:
        if mat is None or not mat.use_nodes or mat.node_tree is None:
            continue
        for node in mat.node_tree.nodes:
            if node.type != "TEX_IMAGE" or getattr(node, "image", None) is None:
                continue
            image = node.image
            resolved = resolve_texture_path(image.filepath, pmx_path)
            if resolved is None:
                continue
            needs_reload = image.filepath != str(resolved)
            image.filepath = str(resolved)
            image.filepath_raw = str(resolved)
            image.source = "FILE"
            if needs_reload:
                try:
                    image.reload()
                except Exception as e:
                    print(f"[warn] failed to reload {image.name}: {e}")


def rebuild_material_for_gltf(material) -> None:
    if material is None:
        return
    tex_node = choose_base_texture_node(material)
    if tex_node is None or tex_node.image is None:
        return
    image = tex_node.image
    material.use_nodes = True
    tree = material.node_tree
    while tree.nodes:
        tree.nodes.remove(tree.nodes[0])

    out = tree.nodes.new(type="ShaderNodeOutputMaterial")
    out.location = (300, 0)
    bsdf = tree.nodes.new(type="ShaderNodeBsdfPrincipled")
    bsdf.location = (0, 0)
    tex = tree.nodes.new(type="ShaderNodeTexImage")
    tex.location = (-300, 0)
    tex.image = image
    uv = tree.nodes.new(type="ShaderNodeUVMap")
    uv.location = (-550, 0)
    uv.uv_map = "UVMap"

    tree.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    tree.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    if material_uses_alpha(material):
        tree.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        if hasattr(material, "blend_method"):
            material.blend_method = "CLIP"
        if hasattr(material, "alpha_threshold"):
            material.alpha_threshold = 0.5
        material.use_backface_culling = False
    else:
        if hasattr(material, "blend_method"):
            material.blend_method = "OPAQUE"


def rebuild_materials_for_gltf(mesh_objects: list["bpy.types.Object"], pmx_path: Path) -> None:
    materials = collect_materials(mesh_objects)
    repair_texture_paths(materials, pmx_path)
    for mat in materials:
        rebuild_material_for_gltf(mat)


# -----------------------------------------------------------------------------
# Weight transfer + binding
# -----------------------------------------------------------------------------


def remove_shape_keys(mesh_obj: "bpy.types.Object") -> None:
    if mesh_obj.type != "MESH" or mesh_obj.data.shape_keys is None:
        return
    clear_selection()
    bpy.context.view_layer.objects.active = mesh_obj
    mesh_obj.select_set(True)
    bpy.ops.object.shape_key_remove(all=True)
    clear_selection()


def remove_all_modifiers(mesh_obj: "bpy.types.Object") -> None:
    for mod in list(mesh_obj.modifiers):
        mesh_obj.modifiers.remove(mod)


def merge_vertex_group(mesh_obj: "bpy.types.Object", source_name: str, target_name: str) -> None:
    source = mesh_obj.vertex_groups.get(source_name)
    if source is None:
        return
    target = mesh_obj.vertex_groups.get(target_name)
    if target is None:
        target = mesh_obj.vertex_groups.new(name=target_name)
    transfers: dict[int, float] = {}
    for vert in mesh_obj.data.vertices:
        for assign in vert.groups:
            if assign.group == source.index:
                transfers[vert.index] = assign.weight
                break
    for v_idx, weight in transfers.items():
        target.add([v_idx], weight, "ADD")


def remap_vertex_groups(mesh_obj: "bpy.types.Object", config: TransferConfig) -> None:
    for source, target in config.bone_map.items():
        if source == target:
            continue
        merge_vertex_group(mesh_obj, source, target)
    removable = set(config.bone_map.keys()) | set(config.ignore_bones)
    for name in list(removable):
        vg = mesh_obj.vertex_groups.get(name)
        if vg is not None:
            mesh_obj.vertex_groups.remove(vg)


def remove_unmapped_vertex_groups(mesh_obj: "bpy.types.Object", target_armature: "bpy.types.Object") -> None:
    allowed = {bone.name for bone in target_armature.data.bones}
    for vg in list(mesh_obj.vertex_groups):
        if vg.name not in allowed:
            mesh_obj.vertex_groups.remove(vg)


def blend_hips_to_upperleg_weights(
    mesh_obj,
    authored_armature,
    blend_fraction: float,
    min_hips_weight: float = 0.7,
) -> int:
    """Redistribute a fraction of each crotch/inner-thigh vertex's Hips weight
    onto UpperLeg_L and UpperLeg_R (split by inverse distance).

    MENACE's target skeleton puts UpperLeg heads *above* the Hips head, the
    real hip joints live on UpperLeg, while Hips is a low sacrum anchor, so
    target's native mesh carries crotch verts with mixed Hips/UpperLeg
    weighting to stay attached to the thigh tops as idle animations sway the
    legs. PMX authors weight the whole pelvis pure-Hips, which leaves the
    crotch stranded at the low Hips Z while the thighs move, causing the
    visible drop/stretch that appears when animations play but not at rest.

    The blend only touches verts that are >=`min_hips_weight` on Hips and
    within a radius scaled by the Hips→UpperLeg distance, so normal hip/back
    geometry isn't affected. Weight is conserved per vertex.
    """
    if blend_fraction <= 0.0:
        return 0
    hips_bone = authored_armature.data.bones.get("Hips")
    ull_bone = authored_armature.data.bones.get("UpperLeg_L")
    ulr_bone = authored_armature.data.bones.get("UpperLeg_R")
    if hips_bone is None or ull_bone is None or ulr_bone is None:
        return 0

    arm_world = authored_armature.matrix_world
    hips_pos = arm_world @ hips_bone.head_local
    ull_pos = arm_world @ ull_bone.head_local
    ulr_pos = arm_world @ ulr_bone.head_local

    hip_leg_dist = (ull_pos - hips_pos).length
    if hip_leg_dist < 1e-4:
        return 0
    blend_radius = hip_leg_dist * HIPS_UPPERLEG_BLEND_RADIUS_FACTOR

    hips_g = mesh_obj.vertex_groups.get("Hips")
    if hips_g is None:
        return 0
    ull_g = mesh_obj.vertex_groups.get("UpperLeg_L") or mesh_obj.vertex_groups.new(
        name="UpperLeg_L"
    )
    ulr_g = mesh_obj.vertex_groups.get("UpperLeg_R") or mesh_obj.vertex_groups.new(
        name="UpperLeg_R"
    )

    mesh_world = mesh_obj.matrix_world
    changes: list[tuple[int, float, float, float]] = []
    for vert in mesh_obj.data.vertices:
        hips_w = 0.0
        for assign in vert.groups:
            if assign.group == hips_g.index:
                hips_w = assign.weight
                break
        if hips_w < min_hips_weight:
            continue

        vw = mesh_world @ vert.co
        dl = (vw - ull_pos).length
        dr = (vw - ulr_pos).length
        nearest = min(dl, dr)
        if nearest > blend_radius:
            continue

        falloff = 1.0 - min(1.0, nearest / blend_radius)
        transfer = blend_fraction * hips_w * falloff
        if transfer <= 0.0:
            continue

        eps = 0.01
        inv_l = 1.0 / (dl + eps)
        inv_r = 1.0 / (dr + eps)
        total = inv_l + inv_r
        frac_l = inv_l / total
        frac_r = inv_r / total
        changes.append(
            (vert.index, hips_w - transfer, transfer * frac_l, transfer * frac_r)
        )

    for v_idx, new_hips_w, add_ull, add_ulr in changes:
        hips_g.add([v_idx], new_hips_w, "REPLACE")
        if add_ull > 0.0:
            ull_g.add([v_idx], add_ull, "ADD")
        if add_ulr > 0.0:
            ulr_g.add([v_idx], add_ulr, "ADD")
    return len(changes)


def bind_mesh_to_authored_armature(mesh_obj: "bpy.types.Object", armature_obj: "bpy.types.Object") -> None:
    # Put the mesh at the armature's frame so the bind pose is computed cleanly.
    world = mesh_obj.matrix_world.copy()
    mesh_obj.parent = armature_obj
    mesh_obj.matrix_parent_inverse = armature_obj.matrix_world.inverted()
    mesh_obj.matrix_world = world
    mod = mesh_obj.modifiers.new(name="Armature", type="ARMATURE")
    mod.object = armature_obj


# -----------------------------------------------------------------------------
# Mesh contract conformance
# -----------------------------------------------------------------------------


def conform_mesh_names(mesh_objects: list["bpy.types.Object"], target_mesh_names: list[str]) -> list["bpy.types.Object"]:
    if not mesh_objects:
        raise RuntimeError("No meshes to conform.")
    if not target_mesh_names:
        raise RuntimeError("Target contract has no mesh names.")

    if len(mesh_objects) == 1 and len(target_mesh_names) > 1:
        base = mesh_objects[0]
        conformed: list = []
        for idx, name in enumerate(target_mesh_names):
            if idx == 0:
                base.name = name
                base.data.name = name
                conformed.append(base)
            else:
                dup = base.copy()
                dup.data = base.data.copy()
                dup.name = name
                dup.data.name = name
                for col in list(base.users_collection):
                    col.objects.link(dup)
                conformed.append(dup)
        return conformed

    if len(mesh_objects) == len(target_mesh_names):
        ordered = sorted(mesh_objects, key=lambda obj: obj.name)
        for mesh_obj, name in zip(ordered, target_mesh_names, strict=True):
            mesh_obj.name = name
            mesh_obj.data.name = name
        return ordered

    raise RuntimeError(
        f"Cannot conform {len(mesh_objects)} source mesh(es) to {len(target_mesh_names)} target LOD mesh(es)."
    )


def decimate_lods(conformed_meshes: list, ratios: list[float]) -> None:
    """Decimate each LOD per its ratio.

    Each conformed LOD mesh keeps its own materials and UV layers, Blender's
    Decimate(COLLAPSE) preserves vertex groups (collapsed verts' weights
    average onto survivors), UV layers (loop UVs interpolated across
    collapsed edges) and material assignments per polygon, so the decimated
    LODs keep their armature bindings and their per-material texture
    sampling intact."""
    for i, lod in enumerate(conformed_meshes):
        ratio = ratios[i] if i < len(ratios) else 1.0
        if ratio >= 0.999:
            print(f"[info] {lod.name}: {len(lod.data.polygons)} polys (no decimation)")
            continue

        clear_selection()
        bpy.context.view_layer.objects.active = lod
        lod.select_set(True)
        mod = lod.modifiers.new(name="LOD_Decimate", type="DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        before = len(lod.data.polygons)
        applied = False
        try:
            bpy.ops.object.modifier_apply(modifier=mod.name)
            applied = True
        except RuntimeError:
            pass
        if not applied:
            depsgraph = bpy.context.evaluated_depsgraph_get()
            evaluated = lod.evaluated_get(depsgraph)
            new_mesh = bpy.data.meshes.new_from_object(evaluated)
            target_name = lod.name
            old = lod.data
            lod.data = new_mesh
            if old.users == 0:
                bpy.data.meshes.remove(old)
            new_mesh.name = target_name
            if mod.name in [m.name for m in lod.modifiers]:
                lod.modifiers.remove(mod)
        after = len(lod.data.polygons)
        print(f"[info] {lod.name}: {before} → {after} polys (decimate ratio={ratio:.2f})")

    clear_selection()


# -----------------------------------------------------------------------------
# Armature rename + cleanup
# -----------------------------------------------------------------------------


def rename_pmx_bones_to_menace(
    pmx_armature_obj,
    bone_map: dict[str, str],
    ignore_bones: list[str],
) -> None:
    """Rename and prune the PMX character's armature bones in-place.

    PMX bones in ``bone_map`` are renamed to their MENACE target names so
    Unity's humanoid Avatar auto-config picks them up by name. Multiple PMX
    bones mapping to the same MENACE name (e.g. all hair bones onto "Head")
    are renamed to the target. The first survives unchanged, subsequent
    duplicates get a numeric suffix from Blender's name uniquifier and we then
    merge their vertex groups via remap_vertex_groups elsewhere in the
    pipeline.

    Bones listed in ``ignore_bones`` are deleted entirely (typically MMD IK
    helpers like 足ＩＫ that aren't used at runtime).
    """
    if pmx_armature_obj.type != "ARMATURE":
        raise RuntimeError("rename_pmx_bones_to_menace target must be an armature.")

    clear_selection()
    bpy.context.view_layer.objects.active = pmx_armature_obj
    pmx_armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    edit_bones = pmx_armature_obj.data.edit_bones

    ignored = 0
    for ignore_name in ignore_bones:
        eb = edit_bones.get(ignore_name)
        if eb is not None:
            edit_bones.remove(eb)
            ignored += 1

    # mmd_tools auto-creates _dummy_* and _shadow_* helper bones for any PMX
    # bone with a "duplicate" relationship (like 足D.L being a shadow of 足.L).
    # These never carry weights we care about and just clutter the rig.
    mmd_helper_prefixes = ("_dummy_", "_shadow_")
    mmd_helper_deleted = 0
    for eb in list(edit_bones):
        if eb.name.startswith(mmd_helper_prefixes):
            edit_bones.remove(eb)
            mmd_helper_deleted += 1

    renamed = 0
    dedup_deleted = 0
    # Group PMX names by target. First PMX bone for each target keeps its name
    # (renamed to the MENACE target). Subsequent PMX bones mapping to the same
    # target are DELETED, their weights get folded into the primary via
    # remap_vertex_groups later, so the bones themselves are orphan duplicates.
    by_target: dict[str, list[str]] = {}
    for pmx_name, menace_name in bone_map.items():
        by_target.setdefault(menace_name, []).append(pmx_name)

    for menace_name, pmx_names in by_target.items():
        primary_set = False
        for pmx_name in pmx_names:
            eb = edit_bones.get(pmx_name)
            if eb is None:
                continue
            if not primary_set:
                # Reparent children of this bone to point at... itself, since
                # we'll rename it. No-op structurally. The rename below is the
                # actual work.
                eb.name = menace_name
                primary_set = True
                renamed += 1
            else:
                # Reparent this bone's children onto the primary (already
                # renamed to menace_name) so we don't orphan a chain when we
                # delete the duplicate. Common for twist bones whose children
                # are hand finger bones.
                primary_eb = edit_bones.get(menace_name)
                for child in list(eb.children):
                    child.parent = primary_eb
                edit_bones.remove(eb)
                dedup_deleted += 1

    bpy.ops.object.mode_set(mode="OBJECT")
    print(
        f"[info] renamed {renamed} PMX bone(s) to MENACE names. Deleted "
        f"{ignored} ignored, {mmd_helper_deleted} mmd-helper, {dedup_deleted} duplicate."
    )

    # Strip mmd_tools-authored pose constraints (IK chains, damped-track
    # twist setups, etc.) that reference now-deleted bones. Left in place
    # they evaluate to a collapsed rest pose (legs together, feet under
    # body) which then gets exported as the bind pose. Unity humanoid
    # animations don't need MMD's IK rig at runtime, so wiping all pose
    # constraints from the surviving bones is safe.
    constraints_removed = 0
    for pb in pmx_armature_obj.pose.bones:
        for con in list(pb.constraints):
            pb.constraints.remove(con)
            constraints_removed += 1
    if constraints_removed:
        print(f"[info] stripped {constraints_removed} mmd_tools pose constraint(s).")
    # Force the depsgraph to re-evaluate pose state. Without this the
    # bones' pose-mode head/tail values still reflect the pre-strip
    # IK-evaluated state, which then misleads downstream detection
    # heuristics (e.g. character-facing direction read from foot direction).
    bpy.context.view_layer.update()


# -----------------------------------------------------------------------------
# Export
# -----------------------------------------------------------------------------


def remove_objects(objects) -> None:
    clear_selection()
    for obj in objects:
        if obj.name in bpy.data.objects:
            obj.select_set(True)
    if any(obj.select_get() for obj in objects if obj.name in bpy.data.objects):
        bpy.ops.object.delete(use_global=False)
    clear_selection()


def purge_non_authored_scene(keep: list) -> None:
    keep_names = {obj.name for obj in keep}
    removable = [obj for obj in bpy.data.objects if obj.name not in keep_names]
    remove_objects(removable)

    for image in list(bpy.data.images):
        if image.users == 0:
            bpy.data.images.remove(image)
    for mat in list(bpy.data.materials):
        if mat.users == 0:
            bpy.data.materials.remove(mat)
    for armature in list(bpy.data.armatures):
        if armature.users == 0:
            bpy.data.armatures.remove(armature)


def export_gltf(output_path: Path, armature_obj, mesh_objects) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)

    # Strip vertex-colour attributes before export. mmd_tools can import
    # PMX extras as colour attributes (AO shading, SDEF blend masks, etc.)
    # which the glTF exporter then emits as COLOR_0 vertex streams. The
    # runtime multiplies those against texture colour, producing darker
    # regions that aren't in the texture and weren't in the PMX viewport.
    for mesh_obj in mesh_objects:
        if mesh_obj.type != "MESH":
            continue
        for attr in list(mesh_obj.data.color_attributes):
            mesh_obj.data.color_attributes.remove(attr)
        if hasattr(mesh_obj.data, "vertex_colors"):
            for vc in list(mesh_obj.data.vertex_colors):
                mesh_obj.data.vertex_colors.remove(vc)

    clear_selection()
    armature_obj.select_set(True)
    for obj in mesh_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = armature_obj

    suffix = output_path.suffix.lower()
    common = dict(
        filepath=str(output_path),
        use_selection=True,
        export_yup=True,
        export_normals=True,
        export_tangents=True,
        export_skins=True,
        export_animations=False,
        export_apply=False,
        export_attributes=False,
    )
    if suffix == ".glb":
        bpy.ops.export_scene.gltf(export_format="GLB", **common)
    elif suffix == ".gltf":
        bpy.ops.export_scene.gltf(export_format="GLTF_SEPARATE", **common)
    else:
        raise RuntimeError(f"Unsupported output format: {output_path}")
    clear_selection()
    enforce_mmd_material_flags(output_path)


def enforce_mmd_material_flags(output_path: Path) -> None:
    """Force alphaMode=MASK / alphaCutoff=0.5 / doubleSided=true on alpha materials.

    Blender's glTF exporter has been shifting alpha / culling property names across
    4.x→5.x (blend_method → surface_render_method, use_backface_culling semantics,
    etc.), so we re-write the three fields in the emitted JSON as a belt-and-braces
    guarantee. MMD-derived geometry is authored with the assumption that hair,
    skirt, and cape planes render on both sides. Using BLEND or single-sided
    rendering produces angle-dependent see-through glitches in game. MASK +
    alphaCutoff=0.5 behaves like OPAQUE for fully-covered texels while preserving
    the cut-out silhouette of the source PMX texture.
    """
    if output_path.suffix.lower() != ".gltf" or not output_path.exists():
        return
    data = json.loads(output_path.read_text(encoding="utf-8"))
    changed = False
    for material in data.get("materials", []):
        alpha_mode = material.get("alphaMode", "OPAQUE")
        if alpha_mode == "OPAQUE":
            continue
        if material.get("alphaMode") != "MASK":
            material["alphaMode"] = "MASK"
            changed = True
        if material.get("alphaCutoff") != 0.5:
            material["alphaCutoff"] = 0.5
            changed = True
        if material.get("doubleSided") is not True:
            material["doubleSided"] = True
            changed = True
    if changed:
        tmp_path = output_path.with_suffix(output_path.suffix + ".tmp")
        tmp_path.write_text(json.dumps(data, indent=2) + "\n")
        tmp_path.replace(output_path)


# -----------------------------------------------------------------------------
# Entry point
# -----------------------------------------------------------------------------


def _compute_height_scale_against_reference(
    reference: ReferenceArmature,
    pmx_armature,
    bone_map: dict[str, str],
    override: float | None,
    target_height_metres: float | None = None,
) -> float:
    """Compute the uniform pre-scale factor for the PMX armature.

    Priority order:
      1. ``height_scale_override``. Explicit multiplicative scale (raw factor).
      2. ``target_height_metres``. Absolute feet-to-head height in metres,
         e.g. 1.8 for an 180cm character. Useful for making the character
         taller or shorter than the vanilla MENACE soldier.
      3. Default. Match the reference armature's feet-to-head span (so the
         model fits the vanilla soldier proportions).

    We match the full feet-to-head Y-span rather than just Hips→Head because
    PMX characters often have different torso/leg proportions. Matching
    Hips→Head alone leaves long-legged characters visibly taller than the
    reference even though their torsos line up. The PMX character's per-bone
    proportions stay their own. Only the overall scale is brought to match.
    """
    if override is not None:
        return float(override)

    # Look up PMX bones for Foot_L and Head via reverse bone_map. We want
    # specific landmarks rather than max/min over the whole PMX armature
    # because PMX rigs have hair / IK / accessory bones at extreme positions
    # that aren't part of the body height we want to match.
    head_pmx = next((p for p, m in bone_map.items() if m == "Head"), None)
    foot_pmx = next((p for p, m in bone_map.items() if m == "Foot_L"), None)
    if head_pmx is None or foot_pmx is None:
        print(
            "[warn] bone_map does not provide both Head and Foot_L PMX names. "
            "Defaulting height scale to 1.0."
        )
        return 1.0

    head_pos = pmx_bone_world_position(pmx_armature, head_pmx)
    foot_pos = pmx_bone_world_position(pmx_armature, foot_pmx)
    if head_pos is None or foot_pos is None:
        print(
            f"[warn] could not locate PMX bones '{head_pmx}' / '{foot_pmx}'. "
            "Defaulting height scale to 1.0."
        )
        return 1.0

    pmx_body_height = (head_pos - foot_pos).length
    if pmx_body_height <= 0.001:
        print(
            f"[warn] PMX Foot_L→Head delta is too small ({pmx_body_height:.4f}). "
            "Defaulting height scale to 1.0."
        )
        return 1.0

    if target_height_metres is not None:
        scale = float(target_height_metres) / pmx_body_height
        print(
            f"[info] PMX body height = {pmx_body_height:.3f}m, "
            f"target = {float(target_height_metres):.3f}m, "
            f"reference soldier height = {reference.yspan_metres:.3f}m."
        )
        return scale

    print(
        f"[info] PMX body height = {pmx_body_height:.3f}m, "
        f"scaling to reference soldier height = {reference.yspan_metres:.3f}m."
    )
    return reference.yspan_metres / pmx_body_height


def main() -> None:
    args = parse_args()
    config = TransferConfig.load(Path(args.config).resolve())

    print(f"[info] loading reference armature: {config.reference_prefab_path}")
    reference = parse_reference_armature(config.reference_prefab_path)
    print(
        f"[info] reference: {len(reference.bones)} bone(s), "
        f"Hips→Head = {reference.height_metres:.3f}m, "
        f"Foot→Head = {reference.yspan_metres:.3f}m"
    )

    reset_scene()

    print(f"[info] importing PMX: {config.pmx_path}")
    pmx_imports = import_pmx(config.pmx_path)
    pmx_armature = find_pmx_armature(pmx_imports)
    pmx_meshes = find_pmx_meshes(pmx_imports, config.source_mesh_names)

    # Shape keys have to go BEFORE we transform mesh vertex data. PMX meshes
    # ship with facial-expression shape keys. Bpy.ops.object.transform_apply
    # silently fails on shape-keyed meshes, which would leave the mesh at PMX
    # original scale while the armature gets scaled, a hard-to-spot
    # armature-smaller-than-mesh mismatch.
    for mesh in pmx_meshes:
        remove_shape_keys(mesh)

    # the PMX character keeps her own proportions. The uniform scale is only to bring
    # her into a sensible world size. Without proportional fit, we just compare
    # the PMX character's Hips→Head delta against the reference's and scale to match.
    scale = _compute_height_scale_against_reference(
        reference, pmx_armature, config.bone_map,
        config.height_scale_override, config.target_height_metres,
    )
    print(f"[info] uniform pre-scale factor: {scale:.4f}")
    apply_uniform_scale(pmx_armature, pmx_meshes, scale)

    print("[info] renaming PMX bones to MENACE humanoid names")
    rename_pmx_bones_to_menace(pmx_armature, config.bone_map, config.ignore_bones)

    print("[info] rebuilding PMX materials for glTF")
    rebuild_materials_for_gltf(pmx_meshes, config.pmx_path)

    print("[info] folding renamed vertex groups (merge duplicates after bone rename)")
    for mesh in pmx_meshes:
        remove_all_modifiers(mesh)
        remap_vertex_groups(mesh, config)
        remove_unmapped_vertex_groups(mesh, pmx_armature)
        # Re-bind the mesh to the armature so the Armature modifier ties
        # vertex groups back to bones, without this the exported glTF has
        # no skin and the mesh ships unweighted.
        bind_mesh_to_authored_armature(mesh, pmx_armature)

    # Pose arms/feet to the reference Avatar's T-pose calibration AFTER
    # vertex groups have been renamed. The bake includes the L-arm roll
    # flip that gives the rig Mecanim-symmetric L/R local frames, so we
    # have to graft sockets AFTER this step, otherwise socket children
    # don't follow the roll flip and end up at flipped relative
    # orientations on the L side.
    reference_tpose = parse_avatar_humanoid_tpose(config.reference_avatar_path)
    print("[info] posing humanoid chain to reference T-pose and baking mesh")
    apply_reference_tpose_calibration(pmx_armature, pmx_meshes, reference_tpose)

    print("[info] grafting reference attachment bones onto the PMX character's armature")
    graft_attachment_bones(pmx_armature, reference)

    if config.hip_leg_weight_blend > 0.0:
        print(
            f"[info] blending Hips↔UpperLeg weights for crotch verts (fraction={config.hip_leg_weight_blend:.2f})"
        )
        total_blended = 0
        for mesh in pmx_meshes:
            total_blended += blend_hips_to_upperleg_weights(
                mesh, pmx_armature, config.hip_leg_weight_blend
            )
        print(f"[info] blended {total_blended} vert(s)")

    print("[info] conforming mesh names to LOD naming convention")
    lod_names = [
        f"{config.lod_mesh_basename}_LOD{i}"
        for i in range(len(config.lod_decimate_ratios))
    ]
    conformed = conform_mesh_names(pmx_meshes, lod_names)

    config.output_path.parent.mkdir(parents=True, exist_ok=True)

    print("[info] decimating LODs")
    decimate_lods(conformed, config.lod_decimate_ratios)

    print("[info] purging non-authored scene data")
    purge_non_authored_scene([pmx_armature, *conformed])

    print(f"[info] exporting glTF: {config.output_path}")
    export_gltf(config.output_path, pmx_armature, conformed)

    print("[done] addition prefab source written to:", config.output_path)



if __name__ == "__main__":
    main()
