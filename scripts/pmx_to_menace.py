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
    fist_pose: bool
    keep_right_index_extended: bool
    rigid_bind_meshes: dict[str, str]
    mesh_textures: dict[str, str]
    fist_rotations: dict[str, float]
    hang_down_chain_prefixes: list[str]
    dress_leg_prefixes: list[str]
    # Where the skirt stops being pelvis and starts being leg, as metres relative to the crotch.
    # The default ramp (2cm above the crotch down to 20cm below it) suits a long skirt whose hem hangs clear of the
    # thigh. A short or layered skirt sitting ON the thigh needs a much shallower ramp: the thigh
    # rotates fully while a half-weighted panel over it rotates half as far, and the difference is
    # the leg coming through the cloth.
    dress_leg_blend_top: float
    dress_leg_blend_depth: float  # measured DOWN FROM blend_top, not from the crotch
    # Half-width, in metres, of the centreline strip over which the skirt blends between the two
    # legs. Defaults to the hip half-width, which spreads the blend across the whole front: every
    # panel is then a mix of both legs and rotates by the average, so the leg that actually swings
    # outruns the cloth over it. Narrowing this welds each panel to the thigh it covers and leaves
    # only a thin strip at the centre stretching between them.
    dress_leg_split_width: float | None
    # Half-width, in metres, of the front-to-back band over which the skirt stops following the
    # legs. Only the FRONT of a skirt should follow them: the vanilla locomotion only ever swings a
    # leg forward, so the back panel has nothing to follow, and weighting it to the legs drags it
    # forward off the body and leaves the backside poking out through it. None keeps the whole
    # skirt leg-following, which suits a skirt with no back panel to speak of.
    dress_leg_front_band: float | None
    # Where that band is centred, as metres in front of (negative) or behind (positive) the hip
    # joint. The joint itself is the sensible default: cloth in front of it is what a forward
    # swing reaches.
    dress_leg_front_pivot: float
    # Materials whose name contains any of these are deleted before anything else
    # touches the mesh. MMD rigs ship alternate-state submeshes for parts the outfit
    # covers -- bare feet under shoes, a torso patch under a top, named "(Hide)" or
    # "Unused" -- which MMD viewers hide and a straight conversion does not, so they
    # render as a second layer poking through the clothing that replaced them.
    strip_material_patterns: list[str]
    # Skip the right-palm calibration for rigs whose animations are their own
    # captured clips rather than retargeted vanilla holds (Sextans): the clips
    # are self-consistent with the rig's existing mesh-vs-bone relationship,
    # so recalibrating the palm would break the grips they were captured with.
    skip_palm_calibration: bool

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
            fist_pose=bool(data.get("fist_pose", True)),
            keep_right_index_extended=bool(data.get("keep_right_index_extended", True)),
            rigid_bind_meshes=dict(data.get("rigid_bind_meshes", {})),
            mesh_textures=dict(data.get("mesh_textures", {})),
            fist_rotations={k: float(v) for k, v in data.get("fist_rotations", {}).items()},
            hang_down_chain_prefixes=list(data.get("hang_down_chain_prefixes", [])),
            dress_leg_prefixes=list(data.get("dress_leg_prefixes", [])),
            dress_leg_blend_top=float(data.get("dress_leg_blend_top", 0.02)),
            # 0.22 with the 0.02 default top puts blend_bottom back on crotch - 0.20, the ramp
            # every rig built before these knobs existed was prepped with.
            dress_leg_blend_depth=float(data.get("dress_leg_blend_depth", 0.22)),
            dress_leg_split_width=(
                float(data["dress_leg_split_width"])
                if data.get("dress_leg_split_width") is not None else None
            ),
            dress_leg_front_band=(
                float(data["dress_leg_front_band"])
                if data.get("dress_leg_front_band") is not None else None
            ),
            dress_leg_front_pivot=float(data.get("dress_leg_front_pivot", 0.0)),
            strip_material_patterns=list(data.get("strip_material_patterns", [])),
            skip_palm_calibration=bool(data.get("skip_palm_calibration", False)),
        )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert a PMX model into a MENACE-compatible authored glTF."
    )
    parser.add_argument(
        "--config", required=True, help="Path to the transfer configuration JSON file."
    )
    parser.add_argument(
        "--stage",
        choices=["full", "prep", "finish"],
        default="full",
        help=(
            "full (default): run the whole pipeline in one process. "
            "prep: run rig rename + T-pose + attachment grafting, then save a single "
            "full-res LOD0 .blend (--blend) for manual weight painting. "
            "finish: re-open the hand-edited --blend, decimate LOD1-N from the "
            "corrected LOD0, and export the glTF."
        ),
    )
    parser.add_argument(
        "--blend",
        help="Path to the handoff .blend file. Written by --stage prep, read by --stage finish.",
    )
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = []
    args = parser.parse_args(argv)
    if args.stage in ("prep", "finish") and not args.blend:
        parser.error(f"--stage {args.stage} requires --blend <path>")
    return args


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


def _detect_facing_from_foot_mesh(
    armature_obj: "bpy.types.Object", mesh_objects: list["bpy.types.Object"]
) -> "bool | None":
    """Whether the PMX character faces world +Y, read from the foot MESH.

    The toe box holds most of a foot's vertex mass and extends forward of
    the ankle joint, so the centroid of the Foot_L/Foot_R-weighted vertices
    sits on the facing side of the ankle. Bone tail direction is NOT a
    reliable facing signal: some riggers anchor 足首's tail at the heel,
    others at the toe, and a wrong guess flips the foot rest retarget.

    Returns None when no foot-weighted vertices exist.
    """
    offset = mathutils.Vector()
    count = 0
    for foot_name in ("Foot_L", "Foot_R"):
        bone = armature_obj.data.bones.get(foot_name)
        if bone is None:
            continue
        ankle = armature_obj.matrix_world @ bone.head_local
        for mesh in mesh_objects:
            vg = mesh.vertex_groups.get(foot_name)
            if vg is None:
                continue
            group_index = vg.index
            for v in mesh.data.vertices:
                for gw in v.groups:
                    if gw.group == group_index and gw.weight > 0.3:
                        offset += (mesh.matrix_world @ v.co) - ankle
                        count += 1
                        break
    if count == 0:
        return None
    return (offset / count).y > 0


def detect_pmx_mirror_frame(
    armature_obj: "bpy.types.Object",
    mesh_objects: list["bpy.types.Object"],
    reference_tpose: ReferenceTPose,
) -> MirrorFrame:
    """Detect L/R-side and facing-direction flips between the PMX rig and
    the reference Avatar.

    L/R side: compare the UpperArm_L tail-direction X sign on both rigs.

    Facing: the PMX side reads the foot MESH (toe mass forward of the
    ankle), falling back to the MMD heel-anchor bone convention (足首.L
    tail BEHIND the ankle) when no foot weights exist. The reference Avatar
    uses Unity's toe-anchor convention, where foot tail direction matches
    facing.
    """
    pmx_upper_l = armature_obj.pose.bones.get("UpperArm_L")
    pmx_l_on_plus_x = pmx_upper_l is not None and (pmx_upper_l.tail - pmx_upper_l.head).x > 0
    ref_upper_l_y, _ = _avatar_bone_yz("UpperArm_L", reference_tpose)
    ref_l_on_plus_x = ref_upper_l_y is not None and ref_upper_l_y.x > 0
    mirror_x = pmx_l_on_plus_x != ref_l_on_plus_x

    pmx_faces_plus_y = _detect_facing_from_foot_mesh(armature_obj, mesh_objects)
    facing_source = "foot mesh"
    if pmx_faces_plus_y is None:
        facing_source = "foot bone heel-anchor fallback"
        pmx_foot_l = armature_obj.pose.bones.get("Foot_L")
        pmx_foot_dir_y = (pmx_foot_l.tail - pmx_foot_l.head).y if pmx_foot_l else 0.0
        pmx_faces_plus_y = pmx_foot_dir_y < 0  # PMX heel-anchor: facing is opposite.
    ref_foot_l_y, _ = _avatar_bone_yz("Foot_L", reference_tpose)
    ref_faces_plus_y = ref_foot_l_y is not None and ref_foot_l_y.y > 0  # Unity toe-anchor: facing matches.
    mirror_y = pmx_faces_plus_y != ref_faces_plus_y

    print(
        f"[info] character mirror_x = {mirror_x} (pmx L on +X: {pmx_l_on_plus_x}, ref L on +X: {ref_l_on_plus_x}). "
        f"mirror_y = {mirror_y} (pmx faces +Y: {pmx_faces_plus_y} via {facing_source}, "
        f"ref faces +Y: {ref_faces_plus_y})"
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


def measure_palm_normals(armature_obj: "bpy.types.Object") -> dict[str, list[float]]:
    """Measure each hand's palm normal in the wrist bone's local frame.

    MENACE's humanoid retarget drives the doll's hand BONE to the same
    global orientation as the vanilla soldier's, so the palm the player
    sees is decided by where the palm GEOMETRY sits relative to that bone.
    MMD wrist bones carry their roll ~70-80 degrees away from the vanilla
    rig's palm-along-bone-X convention, which is what makes a doll hold a
    rifle palm-up. Aligning bone axes alone cannot see this: the palm has
    to be measured anatomically, from the finger-base bones, BEFORE
    rename_pmx_bones_to_menace collapses them.

    Palm normal = the normal of the knuckle plane (wrist, index base,
    pinky base), signed so it points toward the curled middle fingertip.
    This runs AFTER apply_fist_pose, so the middle finger's distal joint
    sits well off the knuckle plane on the palm side, a much stronger
    sign signal than the thumb (whose base is nearly in-plane) and immune
    to the mirrored bone-frame handedness of the .L side. Expressed in
    the wrist pose-bone's local axes, which survives every later rigid
    re-pose of the hand (the mesh rides the bone, so the palm's
    bone-local coordinates are invariant).

    Returns {"R": [x, y, z], "L": [x, y, z]} for the sides that have the
    MMD wrist + finger bones, empty entries omitted.
    """
    result: dict[str, list[float]] = {}
    for side, sfx in (("R", ".R"), ("L", ".L")):
        wrist = armature_obj.pose.bones.get("手首" + sfx)
        index = armature_obj.pose.bones.get("人指１" + sfx)
        pinky = armature_obj.pose.bones.get("小指１" + sfx)
        middle_tip = (armature_obj.pose.bones.get("中指３" + sfx)
                      or armature_obj.pose.bones.get("中指２" + sfx))
        if wrist is None or index is None or pinky is None or middle_tip is None:
            print(f"[warn] palm probe: MMD wrist/finger bones missing for side {side}. "
                  "Hand roll will not be palm-calibrated.")
            continue
        to_local = wrist.matrix.to_3x3().inverted()
        v_index = to_local @ (index.head - wrist.head)
        v_pinky = to_local @ (pinky.head - wrist.head)
        normal = v_index.cross(v_pinky)
        if normal.length < 1e-9:
            print(f"[warn] palm probe: degenerate finger layout for side {side}. Skipping.")
            continue
        normal.normalize()
        # Curl side: the fisted middle fingertip, minus its in-plane part.
        v_tip = to_local @ (middle_tip.tail - wrist.head)
        knuckle_mid = (v_index + v_pinky) / 2
        curl = v_tip - knuckle_mid
        if abs(normal.dot(curl)) < 1e-4:
            print(f"[warn] palm probe: middle fingertip is in the knuckle plane for side {side} "
                  "(fist pose missing?). Sign may be unreliable.")
        if normal.dot(curl) < 0:
            normal.negate()
        result[side] = list(normal)
        print(f"[info] palm probe {side}: normal in wrist-local = "
              f"({normal.x:.3f}, {normal.y:.3f}, {normal.z:.3f})")
    return result


def pose_arm_chains_and_bake(
    armature_obj: "bpy.types.Object",
    mesh_objects: list["bpy.types.Object"],
    reference_tpose: ReferenceTPose,
    frame: MirrorFrame,
    palm_locals: dict[str, list[float]] | None = None,
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

    # Stub-bone rigs (AssetStudio FBX rips export bones as null nodes, so
    # Blender imports every bone as a short world-+Y stub) break the pose
    # math below: it derives each bone's rotation from its REST basis, which
    # is only meaningful when bone Y runs along the limb. Detect stubs by
    # comparing bone Y against the direction to the anatomical child and
    # re-orient in EDIT mode first: tail toward the child, roll chosen as
    # the minimal-twist frame against the avatar target basis so the bake
    # swings the arm without spiralling the mesh around the limb axis.
    # MMD imports already point along the limb, the gate leaves them alone.
    clear_selection()
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    reoriented = 0
    for chain in chain_bones:
        edit_bones = armature_obj.data.edit_bones
        prev_dir = None
        for i, bone_name in enumerate(chain):
            eb = edit_bones.get(bone_name)
            if eb is None:
                continue
            child = edit_bones.get(chain[i + 1]) if i + 1 < len(chain) else None
            direction = (child.head - eb.head) if child is not None else prev_dir
            if direction is None or direction.length < 1e-9:
                continue
            prev_dir = direction.copy()
            current_y = (eb.tail - eb.head).normalized()
            if current_y.angle(direction.normalized()) < math.radians(30.0):
                continue  # already limb-aligned (PMX/MMD rigs)
            basis = _resolve_bone_basis(bone_name, reference_tpose, frame, apply_y=False)
            eb.tail = eb.head + direction
            if basis is not None:
                target_x, target_y, target_z = basis
                swing = target_y.rotation_difference(direction.normalized())
                eb.align_roll(swing @ target_z)
            reoriented += 1
    bpy.ops.object.mode_set(mode="OBJECT")
    if reoriented:
        print(f"[info] re-oriented {reoriented} stub arm bone(s) along the limb before T-pose calibration")

    clear_selection()
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")

    rotated = 0
    hand_reference_frames: dict[str, tuple["mathutils.Vector", "mathutils.Vector"]] = {}
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
            # The RIGHT hand gets a palm-down roll on top of the axis alignment
            # for the MESH BAKE ONLY. MMD wrist axes sit rolled relative to
            # the palm compared to the vanilla rig, so without this the palm
            # GEOMETRY bakes in rolled and every retargeted animation shows it
            # (rifle held palm-up). The bone's REST frame is restored to the
            # reference basis in edit mode after the bake (mesh stays put):
            # the humanoid retarget, the grafted Hand_*_Socket the weapon
            # rides, and the left-hand IK contract all read that frame.
            # RIGHT hand only. The left hand is deliberately NOT palm-calibrated:
            # it is IK-slaved to each weapon's weapon_hand_l empty, and the raw
            # MMD wrist roll under the reference-basis alignment is what those
            # empties were authored and verified against. Calibrating the left to
            # world-down showed thumb-under grips in game, world-up showed the
            # palm facing away, and the uncalibrated original was correct.
            side = "L" if bone_name.endswith("_L") else "R"
            if bone_name == "Hand_R" and palm_locals and side in palm_locals:
                palm_world = m @ mathutils.Vector(palm_locals[side])
                axis = target_y.normalized()
                down = mathutils.Vector((0.0, 0.0, -1.0))
                pw = palm_world - palm_world.dot(axis) * axis
                dw = down - down.dot(axis) * axis
                if pw.length > 1e-6 and dw.length > 1e-6:
                    angle = math.atan2(axis.dot(pw.cross(dw)), pw.dot(dw))
                    hand_reference_frames[bone_name] = (target_y.copy(), target_z.copy())
                    m = mathutils.Matrix.Rotation(angle, 3, axis) @ m
                    print(f"[info] palm-down roll on {bone_name} (mesh bake): {math.degrees(angle):+.1f} deg")
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

    # Restore each palm-rolled hand bone's REST frame to the reference
    # basis. The palm roll above exists only so the bake carries the palm
    # geometry to world-down. The bone itself must keep the reference frame:
    # the avatar the humanoid retarget uses, the Hand_*_Socket the weapon
    # parents under, and the weapon_hand_l IK orientations are all defined
    # against it. Edit-mode roll changes move no geometry.
    if hand_reference_frames:
        bpy.ops.object.mode_set(mode="EDIT")
        for bone_name, (ref_y, ref_z) in hand_reference_frames.items():
            eb = armature_obj.data.edit_bones.get(bone_name)
            if eb is None:
                continue
            bone_y = (eb.tail - eb.head).normalized()
            if bone_y.angle(ref_y) > math.radians(2.0):
                print(f"[warn] {bone_name} rest direction drifted from the reference "
                      f"({math.degrees(bone_y.angle(ref_y)):.1f} deg), roll restore may be off")
            eb.align_roll(ref_z)
        bpy.ops.object.mode_set(mode="OBJECT")
        print(f"[info] restored {len(hand_reference_frames)} hand bone REST frame(s) "
              "to the reference basis (mesh unchanged)")

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
    palm_locals: dict[str, list[float]] | None = None,
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

    frame = detect_pmx_mirror_frame(armature_obj, mesh_objects, reference_tpose)
    rotated = pose_arm_chains_and_bake(armature_obj, mesh_objects, reference_tpose, frame, palm_locals)
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
    """Import the character source model. PMX goes through mmd_tools. FBX
    (AssetStudio/VoyExport rips, e.g. GFL2 characters) goes through the
    native FBX importer with animation takes skipped: the model glTF only
    needs mesh + skeleton, clips ship separately as a Unity-side humanoid
    animation donor."""
    if not path.exists():
        raise FileNotFoundError(f"source model not found: {path}")
    before = {obj.name for obj in bpy.data.objects}
    if path.suffix.lower() == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    else:
        ensure_mmd_tools_enabled()
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


def apply_fist_pose(
    armature_obj: "bpy.types.Object",
    mesh_objects: list["bpy.types.Object"],
    *,
    keep_right_index_extended: bool = True,
    custom_rotations: dict[str, float] | None = None,
) -> int:
    """Curl PMX finger bones into a fist pose, bake the deformation, apply
    the new rest. Returns the number of finger bones rotated.

    Why this exists: vanilla MENACE soldier rigs have no finger bones — the
    in-game hand pose is whatever the mesh was baked at. After
    rename_pmx_bones_to_menace collapses PMX finger bones onto Hand_L /
    Hand_R, finger geometry rides the hand rigidly. So the pose at THIS
    stage is the pose that ships. We bake a fist now while individual
    finger bones still exist.

    Right index finger stays extended by default — soldiers with weapons
    benefit from a trigger-finger pose. Override via
    keep_right_index_extended=False for a full fist on both hands.

    PMX/MMD finger bone names (post-mmd_tools rename) use the .L/.R suffix
    and full-width numerals: 中指１.L, 親指０.R, etc. The thumb is numbered
    0/1/2; the other four fingers are 1/2/3.

    Curl angles per joint approximate a moderate closed fist. They rotate
    around the bone-local X axis, which is the standard MMD finger-curl
    axis. Sign is positive for both sides because mmd_tools imports finger
    bones with consistent local-X orientation across left and right.
    """
    NON_THUMB_FINGERS = ["中指", "人指", "小指", "薬指"]

    # Joint curl angles (degrees). Knuckle/PIP/DIP for non-thumb;
    # CMC/MCP/IP for the thumb (gentler — a closed fist tucks the thumb
    # over the fingers without fully curling it).
    NON_THUMB_ANGLES = [55, 75, 30]
    THUMB_ANGLES = [20, 30, 40]

    rotations: list[tuple[str, float]] = []
    if custom_rotations:
        # Non-PMX rigs (e.g. GFL2 AdvancedSkeleton) name their fingers
        # differently, so the config supplies bone-name -> curl-degrees
        # directly instead of the PMX name templates below. The explicit
        # list is authoritative: keep_right_index_extended does not apply
        # (leave the index-finger bones out of the list instead).
        if keep_right_index_extended:
            print("[info] fist-pose: custom fist_rotations supplied, keep_right_index_extended is ignored")
        rotations = [(name, math.radians(deg)) for name, deg in custom_rotations.items()]
    else:
        for side in (".L", ".R"):
            for finger in NON_THUMB_FINGERS:
                if keep_right_index_extended and finger == "人指" and side == ".R":
                    continue
                for joint, deg in zip("１２３", NON_THUMB_ANGLES):
                    rotations.append((f"{finger}{joint}{side}", math.radians(deg)))
            for joint, deg in zip("０１２", THUMB_ANGLES):
                rotations.append((f"親指{joint}{side}", math.radians(deg)))

    clear_selection()
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")

    rotated = 0
    missing: list[str] = []
    for bone_name, angle in rotations:
        pb = armature_obj.pose.bones.get(bone_name)
        if pb is None:
            missing.append(bone_name)
            continue
        pb.rotation_mode = "XYZ"
        pb.rotation_euler.x += angle
        rotated += 1

    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")

    if missing:
        print(f"[info] fist-pose: {len(missing)} finger bone(s) absent in PMX (e.g. {missing[0]}). Skipped them.")

    if rotated == 0:
        print("[info] fist-pose: no finger bones found. Skipping bake.")
        return 0

    print(f"[info] fist-pose: rotated {rotated} finger bone(s). Baking mesh.")
    for mesh in mesh_objects:
        ensure_armature_modifier(mesh, armature_obj)
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


def apply_mesh_textures(mesh_objects: list["bpy.types.Object"], mesh_textures: dict[str, str]) -> None:
    """Give texture-less meshes a material carrying the configured diffuse.

    AssetStudio FBX rips export no materials or textures at all, so each mesh
    arrives as a single bare slot. For each configured mesh this creates one
    material named after the texture stem, loads the image, and wires the
    minimal Principled graph rebuild_material_for_gltf expects to find. The
    mesh's first UV layer is renamed to "UVMap" to match the graph's UV node.
    """
    if not mesh_textures:
        return
    for mesh in mesh_objects:
        tex_path = mesh_textures.get(mesh.name)
        if tex_path is None:
            continue
        tex = Path(tex_path)
        if not tex.exists():
            raise FileNotFoundError(f"mesh_textures: texture not found for {mesh.name}: {tex}")
        if mesh.data.uv_layers and mesh.data.uv_layers[0].name != "UVMap":
            mesh.data.uv_layers[0].name = "UVMap"
        image = bpy.data.images.load(str(tex), check_existing=True)
        mat = bpy.data.materials.new(name=tex.stem)
        mat.use_nodes = True
        tree = mat.node_tree
        while tree.nodes:
            tree.nodes.remove(tree.nodes[0])
        out = tree.nodes.new(type="ShaderNodeOutputMaterial")
        bsdf = tree.nodes.new(type="ShaderNodeBsdfPrincipled")
        tex_node = tree.nodes.new(type="ShaderNodeTexImage")
        tex_node.image = image
        uv = tree.nodes.new(type="ShaderNodeUVMap")
        uv.uv_map = "UVMap"
        tree.links.new(uv.outputs["UV"], tex_node.inputs["Vector"])
        tree.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
        tree.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
        mesh.data.materials.clear()
        mesh.data.materials.append(mat)
        print(f"[info] mesh_textures: {mesh.name} <- {tex.name}")


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


def orient_stub_core_bones(armature_obj: "bpy.types.Object") -> None:
    """Point core skeleton bones at their anatomical child. Stub-bone rigs
    (AssetStudio FBX rips) import every bone as a short world-+Y stub, which
    reads as "bones pointing the wrong way" during inspection and makes hand
    posing awkward. Orientation of a REST bone does not move the bound mesh,
    the exported skin stays consistent.

    The arm chain (UpperArm/LowerArm/Hand) is deliberately left alone: the
    T-pose calibration re-orients it with a minimal-twist roll against the
    reference basis, and pre-orienting it here with an arbitrary roll would
    make that bake spiral the arm mesh. Feet likewise belong to
    retarget_foot_bones_rest. The >30 degree gate leaves PMX/MMD rigs (bones
    already limb-aligned) untouched."""
    chains = {
        "Hips": "Spine",
        "Spine": "Spine2",
        "Spine2": "Neck",
        "Neck": "Head",
        "Shoulder_L": "UpperArm_L",
        "Shoulder_R": "UpperArm_R",
        "UpperLeg_L": "LowerLeg_L",
        "UpperLeg_R": "LowerLeg_R",
        "LowerLeg_L": "Foot_L",
        "LowerLeg_R": "Foot_R",
    }
    # The reference rig carries local Z = character FRONT on the spine chain,
    # and socket grafting composes reference parent-LOCAL offsets with these
    # frames (the backpack hangs off Spine2's local -Z, "behind"). A stub
    # bone's roll is arbitrary after a tail edit, which swings such sockets
    # sideways, so the spine chain's roll is pinned to the same convention.
    # The pipeline normalises characters to face -Y, so front = -Y.
    spine_chain = {"Hips", "Spine", "Spine2", "Neck", "Head"}
    front = mathutils.Vector((0.0, -1.0, 0.0))

    clear_selection()
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones = armature_obj.data.edit_bones
    oriented = 0
    for bone_name, child_name in chains.items():
        eb = edit_bones.get(bone_name)
        child = edit_bones.get(child_name)
        if eb is None or child is None:
            continue
        direction = child.head - eb.head
        if direction.length < 1e-9:
            continue
        current_y = (eb.tail - eb.head).normalized()
        if current_y.angle(direction.normalized()) < math.radians(30.0):
            continue
        eb.tail = eb.head + direction
        if bone_name in spine_chain:
            eb.align_roll(front)
        oriented += 1
    # Head has no mapped child: give it an up-pointing tail matching its
    # neck-to-head direction so it reads naturally in pose mode.
    head = edit_bones.get("Head")
    neck = edit_bones.get("Neck")
    if head is not None and neck is not None:
        direction = head.head - neck.head
        if direction.length > 1e-9:
            current_y = (head.tail - head.head).normalized()
            if current_y.angle(direction.normalized()) >= math.radians(30.0):
                head.tail = head.head + direction
                head.align_roll(front)
                oriented += 1
    bpy.ops.object.mode_set(mode="OBJECT")
    if oriented:
        print(f"[info] oriented {oriented} stub core bone(s) toward their anatomical child")


def apply_rigid_binds(mesh_objects: list["bpy.types.Object"], rigid_bind_meshes: dict[str, str]) -> None:
    """Weight every vertex of the configured meshes 1.0 to a single SOURCE
    bone. For rips whose exporter lost a mesh's skin (AssetStudio collapses
    single-influence skins, e.g. GFL2 faces bound rigidly to the head), the
    correct binding is a rigid attach to that one bone. Runs before the
    vertex-group remap, so the source bone name folds into its MENACE target
    with everything else."""
    if not rigid_bind_meshes:
        return
    for mesh in mesh_objects:
        bone_name = rigid_bind_meshes.get(mesh.name)
        if bone_name is None:
            continue
        for vg in list(mesh.vertex_groups):
            mesh.vertex_groups.remove(vg)
        group = mesh.vertex_groups.new(name=bone_name)
        group.add(list(range(len(mesh.data.vertices))), 1.0, "REPLACE")
        print(f"[info] rigid-bind: {mesh.name} -> {bone_name} ({len(mesh.data.vertices)} verts)")


def remap_vertex_groups(
    mesh_obj: "bpy.types.Object",
    config: TransferConfig,
    extra_map: dict[str, str] | None = None,
) -> None:
    merges = dict(config.bone_map)
    if extra_map:
        merges.update(extra_map)
    for source, target in merges.items():
        if source == target:
            continue
        merge_vertex_group(mesh_obj, source, target)
    removable = set(merges.keys()) | set(config.ignore_bones)
    for name in list(removable):
        vg = mesh_obj.vertex_groups.get(name)
        if vg is not None:
            mesh_obj.vertex_groups.remove(vg)


def remove_unmapped_vertex_groups(mesh_obj: "bpy.types.Object", target_armature: "bpy.types.Object") -> None:
    allowed = {bone.name for bone in target_armature.data.bones}
    for vg in list(mesh_obj.vertex_groups):
        if vg.name not in allowed:
            mesh_obj.vertex_groups.remove(vg)


def bind_unweighted_to_nearest_bone(mesh_obj: "bpy.types.Object", armature_obj: "bpy.types.Object") -> int:
    """Rigid-bind any vertex with no deform weight to the nearest bone.

    AssetStudio rips zero out single-influence skins, and when the affected
    part is a submesh inside a larger mesh (accessory pearls inside a cloth
    mesh) the rigid_bind_meshes whole-mesh fix cannot target it. Leaving the
    verts unweighted pins them to the model origin at runtime. Nearest-bone
    distance is measured against the head-to-tail segment of each deform
    bone, in world space. Returns the number of verts bound."""
    bones = [
        (b.name, armature_obj.matrix_world @ b.head_local, armature_obj.matrix_world @ b.tail_local)
        for b in armature_obj.data.bones
        if b.use_deform
    ]
    if not bones:
        return 0

    def segment_distance(p, a, b):
        ab = b - a
        denom = ab.length_squared
        if denom < 1e-12:
            return (p - a).length
        t = max(0.0, min(1.0, (p - a).dot(ab) / denom))
        return (p - (a + ab * t)).length

    world = mesh_obj.matrix_world
    bound = 0
    per_group: dict[str, list[int]] = {}
    for vert in mesh_obj.data.vertices:
        if sum(g.weight for g in vert.groups) >= 0.001:
            continue
        p = world @ vert.co
        name = min(bones, key=lambda entry: segment_distance(p, entry[1], entry[2]))[0]
        per_group.setdefault(name, []).append(vert.index)
        bound += 1
    for name, indices in per_group.items():
        group = mesh_obj.vertex_groups.get(name) or mesh_obj.vertex_groups.new(name=name)
        group.add(indices, 1.0, "REPLACE")
    if bound:
        summary = ", ".join(f"{n}:{len(ix)}" for n, ix in sorted(per_group.items()))
        print(f"[info] bound {bound} unweighted vert(s) on {mesh_obj.name} to nearest bone ({summary})")
    return bound


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
) -> dict[str, str]:
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

    # Source names can equal OTHER groups' target names (GFL2 names its upper
    # arm "Shoulder_L", which is MENACE's clavicle name), and any rename onto
    # a still-occupied name is silently uniquified to "Name.001", losing the
    # bone. Name lookups made while names mutate can likewise grab the wrong
    # bone. So: park EVERY mapped bone on a reserved unique name first, then
    # do all renames and deletions through the park names, where no collision
    # or aliasing is possible.
    parked: dict[str, list[str]] = {}
    park_counter = 0
    for menace_name, pmx_names in by_target.items():
        park_names: list[str] = []
        for pmx_name in pmx_names:
            eb = edit_bones.get(pmx_name)
            if eb is None:
                continue
            park = f"__jy_park_{park_counter}"
            park_counter += 1
            eb.name = park
            park_names.append(park)
        if park_names:
            parked[menace_name] = park_names

    # Blender renames vertex groups in sync with bone renames on bound
    # meshes, so parking moves every mesh's vertex groups onto the park
    # names too. Primaries fold back automatically (their park -> target
    # rename syncs the group to the target). Duplicates are DELETED while
    # holding a park name, stranding their groups there, so the park -> target
    # mapping is returned for remap_vertex_groups to fold explicitly.
    park_to_target: dict[str, str] = {}
    # An UNMAPPED bone can still hold a MENACE target name (GFL2 rigs use
    # names like "Shoulder_L" natively). Renaming the primary onto an
    # occupied name is silently uniquified to "Name.001" and the avatar
    # builder later misses the bone, so occupying bones are parked aside
    # first. Their vertex groups follow the rename and get dropped by
    # remove_unmapped_vertex_groups, same as any unmapped bone's.
    for menace_name in list(parked):
        shadow = edit_bones.get(menace_name)
        if shadow is not None:
            print(
                f"[warn] unmapped bone '{menace_name}' occupies a MENACE target name. "
                "Parking it aside. Map or ignore it in the config to silence this."
            )
            shadow.name = f"__jy_shadowed_{menace_name}"

    for menace_name, park_names in parked.items():
        # First parked bone per target is the primary: renamed to the MENACE
        # name. The rest are duplicates: children reparent onto the primary
        # (common for twist bones whose children are hand finger bones), the
        # bone is deleted, and its weights fold into the primary later via
        # remap_vertex_groups.
        edit_bones.get(park_names[0]).name = menace_name
        renamed += 1
        for park in park_names[1:]:
            park_to_target[park] = menace_name
            eb = edit_bones.get(park)
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
    return park_to_target


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


def strip_hidden_submeshes(pmx_meshes: list, patterns: list[str]) -> None:
    """Delete faces belonging to alternate-state materials the outfit covers.

    MMD rigs carry the geometry an outfit replaces rather than deleting it, marked
    in the material name (``BodySkin(Feet/Hide)``, ``Set1-Socks-NoShoes(Hide)``).
    An MMD viewer hides those materials; a straight conversion keeps them, so the
    bare foot sits inside the shoe. The two are weighted differently -- the shoe
    leans on LowerLeg, the foot under it on Foot -- so they deform apart and the
    toes come through. Deleting the faces is the fix: nothing should ever draw them.

    Runs before scaling and weight work so the removed geometry cannot influence
    either. Vertices and edges left unused by the deletion go with the faces.
    """
    if not patterns:
        return

    import bmesh  # type: ignore

    for mesh in pmx_meshes:
        matched = [
            slot.name for slot in mesh.material_slots
            if slot.name and any(p in slot.name for p in patterns)
        ]
        if not matched:
            continue

        bm = bmesh.new()
        bm.from_mesh(mesh.data)
        bm.faces.ensure_lookup_table()
        doomed_names = set(matched)
        doomed = [
            f for f in bm.faces
            if mesh.material_slots[f.material_index].name in doomed_names
        ]
        faces_before = len(bm.faces)
        verts_before = len(bm.verts)
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        bm.to_mesh(mesh.data)
        bm.free()
        mesh.data.update()

        # Drop the now-empty slots so they do not reach the exported material list.
        # temp_override rather than a context dict: Blender 4.0 removed the dict form.
        for name in matched:
            index = mesh.material_slots.find(name)
            if index < 0:
                continue
            mesh.active_material_index = index
            with bpy.context.temp_override(object=mesh, active_object=mesh, selected_objects=[mesh]):
                bpy.ops.object.material_slot_remove()

        print(
            f"[info] stripped hidden submesh(es) from {mesh.name}: {', '.join(matched)} "
            f"({faces_before - len(mesh.data.polygons)} face(s), "
            f"{verts_before - len(mesh.data.vertices)} vert(s))"
        )


def redistribute_dress_to_legs(
    pmx_meshes: list,
    pmx_armature,
    config: "TransferConfig",
) -> None:
    """Convert physics-skirt weights into a cheap leg-following skirt rig.

    Vertex groups matching ``dress_leg_prefixes`` (a physics bone grid like
    Skirt_0_0..Skirt_9_17) would otherwise fold into a single torso bone and
    the dress would hang rigidly while the legs animate through it. Instead
    each vertex's skirt weight is rewritten onto the humanoid rig:

      - at waistband height and above it stays on the pelvis,
      - below the crotch it follows the legs, split left/right by the
        vertex's X with a smooth 50/50 band at the centreline so the front
        and back panels stretch between the legs instead of tearing.

    Weights are written into the PRIMARY source groups for Hips and
    UpperLeg_L/R from the bone map (they fold into those bones), so this
    runs BEFORE bone rename, after scaling (thresholds are in metres).
    """
    prefixes = config.dress_leg_prefixes
    if not prefixes:
        return

    primary: dict[str, str] = {}
    for source, target in config.bone_map.items():
        primary.setdefault(target, source)
    try:
        hips_group = primary["Hips"]
        leg_l_group = primary["UpperLeg_L"]
        leg_r_group = primary["UpperLeg_R"]
    except KeyError as missing:
        print(f"[warn] dress-to-legs: bone map has no primary for {missing}, skipping")
        return

    leg_l_bone = pmx_armature.data.bones.get(leg_l_group)
    leg_r_bone = pmx_armature.data.bones.get(leg_r_group)
    if leg_l_bone is None or leg_r_bone is None:
        print("[warn] dress-to-legs: leg bones missing, skipping")
        return
    arm_mw = pmx_armature.matrix_world
    crotch_z = ((arm_mw @ leg_l_bone.head_local).z + (arm_mw @ leg_r_bone.head_local).z) / 2
    half_width = (
        config.dress_leg_split_width
        if config.dress_leg_split_width is not None
        else max(abs((arm_mw @ leg_l_bone.head_local).x), 0.06)
    )
    half_width = max(half_width, 1e-3)

    # The character faces -Y, so cloth in FRONT of the hip joint is at a lower y than it.
    leg_y = ((arm_mw @ leg_l_bone.head_local).y + (arm_mw @ leg_r_bone.head_local).y) / 2
    front_pivot = leg_y + config.dress_leg_front_pivot
    front_band = (
        max(config.dress_leg_front_band, 1e-3)
        if config.dress_leg_front_band is not None else None
    )

    # Full pelvis above the crotch, full leg a configured depth below it, linear between.
    blend_top = crotch_z + config.dress_leg_blend_top
    blend_bottom = blend_top - max(config.dress_leg_blend_depth, 1e-3)

    for mesh in pmx_meshes:
        groups = [
            g for g in mesh.vertex_groups
            if any(g.name.startswith(p) for p in prefixes)
        ]
        if not groups:
            continue
        group_indices = {g.index for g in groups}
        for name in (hips_group, leg_l_group, leg_r_group):
            if mesh.vertex_groups.get(name) is None:
                mesh.vertex_groups.new(name=name)
        hips_vg = mesh.vertex_groups[hips_group]
        leg_l_vg = mesh.vertex_groups[leg_l_group]
        leg_r_vg = mesh.vertex_groups[leg_r_group]

        mw = mesh.matrix_world
        rewritten = 0
        for v in mesh.data.vertices:
            skirt_weight = sum(
                gw.weight for gw in v.groups if gw.group in group_indices
            )
            if skirt_weight <= 1e-4:
                continue
            world = mw @ v.co
            leg_frac = min(1.0, max(0.0, (blend_top - world.z) / (blend_top - blend_bottom)))
            if front_band is not None:
                # 1 in front of the band, 0 behind it. Whatever the back gives up here returns to
                # the pelvis below, because the leg and hip shares are taken from one weight.
                front = (front_pivot + front_band - world.y) / (2.0 * front_band)
                leg_frac *= min(1.0, max(0.0, front))
            side = min(1.0, max(0.0, 0.5 + world.x / (2.0 * half_width)))
            index = [v.index]
            hips_vg.add(index, skirt_weight * (1.0 - leg_frac), "ADD")
            leg_l_vg.add(index, skirt_weight * leg_frac * side, "ADD")
            leg_r_vg.add(index, skirt_weight * leg_frac * (1.0 - side), "ADD")
            for g in groups:
                g.remove(index)
            rewritten += 1
        if rewritten:
            print(
                f"[info] dress-to-legs ({mesh.name}): rewrote {rewritten} vert(s) "
                f"from {len(groups)} skirt group(s) (crotch z={crotch_z:.3f}m, "
                f"pelvis->leg ramp {blend_top:.3f}m..{blend_bottom:.3f}m"
                + (f", front-only band y {front_pivot - front_band:+.3f}..{front_pivot + front_band:+.3f}"
                   if front_band is not None else "")
                + ")"
            )


def straighten_hang_chains(pmx_meshes: list, prefixes: list[str]) -> None:
    """Re-drape dangling cloth chains so they hang straight down (world -Z).

    A chain is every vertex group named ``<prefix><number>`` (e.g.
    Cloth_RF01..Cloth_RF015), ordered by the number: lowest link is the
    anchored top, highest the free tip. Some rigs author these strips
    extending along the limb rather than hanging with gravity. Each link's
    vertex ring is moved onto a vertical stack below the top anchor
    (segment lengths preserved) and rotated so its cross-section faces the
    new axis, weight-blended per vertex so the strip stays smooth and its
    attachment seam to the garment doesn't tear.

    Runs BEFORE bone rename (it needs the original group names) and expects
    the chains' bone_map targets to be torso bones, so the T-pose arm bake
    never moves the re-draped strip afterwards.
    """
    down = mathutils.Vector((0.0, 0.0, -1.0))
    for mesh in pmx_meshes:
        for prefix in prefixes:
            groups = [
                g for g in mesh.vertex_groups
                if g.name.startswith(prefix) and g.name[len(prefix):].isdigit()
            ]
            if len(groups) < 2:
                continue
            groups.sort(key=lambda g: int(g.name[len(prefix):]))
            order = [g.index for g in groups]
            chain_set = set(order)

            vert_chain_weights: dict[int, dict[int, float]] = {}
            for v in mesh.data.vertices:
                for gw in v.groups:
                    if gw.group in chain_set and gw.weight > 1e-4:
                        vert_chain_weights.setdefault(v.index, {})[gw.group] = gw.weight
            if not vert_chain_weights:
                print(f"[warn] hang chain '{prefix}' on {mesh.name}: no weighted verts, skipping")
                continue

            mw = mesh.matrix_world
            verts = mesh.data.vertices
            weighted_sum = {gi: mathutils.Vector() for gi in order}
            weight_total = {gi: 0.0 for gi in order}
            for vi, weights in vert_chain_weights.items():
                world_co = mw @ verts[vi].co
                for gi, w in weights.items():
                    weighted_sum[gi] += world_co * w
                    weight_total[gi] += w
            links = [gi for gi in order if weight_total[gi] > 1e-6]
            if len(links) < 2:
                continue
            centroid = {gi: weighted_sum[gi] / weight_total[gi] for gi in links}

            # Stack link centroids vertically below the top anchor, keeping
            # each segment's length so the strip neither stretches nor bunches.
            new_centroid = {links[0]: centroid[links[0]].copy()}
            for prev, cur in zip(links, links[1:]):
                segment = (centroid[cur] - centroid[prev]).length
                new_centroid[cur] = new_centroid[prev] + down * segment

            # Per-link minimal-arc rotation from the local chain direction to
            # -Z, so each ring's tilt follows the new vertical axis without
            # accumulating twist.
            link_rotation = {}
            for j, gi in enumerate(links):
                a = centroid[links[max(j - 1, 0)]]
                b = centroid[links[min(j + 1, len(links) - 1)]]
                direction = b - a
                link_rotation[gi] = (
                    direction.normalized().rotation_difference(down)
                    if direction.length > 1e-6
                    else mathutils.Quaternion()
                )

            inv = mw.inverted()
            for vi, weights in vert_chain_weights.items():
                world_co = mw @ verts[vi].co
                target = mathutils.Vector()
                chain_weight = 0.0
                for gi, w in weights.items():
                    if gi not in centroid:
                        continue
                    target += (new_centroid[gi] + link_rotation[gi] @ (world_co - centroid[gi])) * w
                    chain_weight += w
                if chain_weight <= 0.0:
                    continue
                target /= chain_weight
                # Verts shared with the garment keep their garment share:
                # blend by the chain's weight fraction (rigs normalise to 1).
                fraction = min(1.0, chain_weight)
                verts[vi].co = inv @ world_co.lerp(target, fraction)
            mesh.data.update()
            print(
                f"[info] hang chain '{prefix}' ({mesh.name}): re-draped "
                f"{len(vert_chain_weights)} vert(s) across {len(links)} link(s)"
            )


def prep_pmx(config: "TransferConfig") -> tuple:
    """Stages 1-8: import, scale, rig rename, T-pose calibration, attachment
    grafting and the optional hip-leg weight blend. Returns (armature, meshes)
    with the source mesh(es) still un-conformed and un-decimated, ready either
    for LOD generation (full) or for a manual weight-painting handoff (prep)."""
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

    strip_hidden_submeshes(pmx_meshes, config.strip_material_patterns)

    # the PMX character keeps her own proportions. The uniform scale is only to bring
    # her into a sensible world size. Without proportional fit, we just compare
    # the PMX character's Hips→Head delta against the reference's and scale to match.
    scale = _compute_height_scale_against_reference(
        reference, pmx_armature, config.bone_map,
        config.height_scale_override, config.target_height_metres,
    )
    print(f"[info] uniform pre-scale factor: {scale:.4f}")
    apply_uniform_scale(pmx_armature, pmx_meshes, scale)

    if config.fist_pose:
        print("[info] applying fist-pose to fingers (bakes pose into mesh before bone collapse)")
        apply_fist_pose(
            pmx_armature, pmx_meshes,
            keep_right_index_extended=config.keep_right_index_extended,
            custom_rotations=config.fist_rotations,
        )

    straighten_hang_chains(pmx_meshes, config.hang_down_chain_prefixes)
    redistribute_dress_to_legs(pmx_meshes, pmx_armature, config)

    if config.skip_palm_calibration:
        print("[info] palm calibration disabled by config")
        palm_locals = {}
    else:
        palm_locals = measure_palm_normals(pmx_armature)

    print("[info] renaming PMX bones to MENACE humanoid names")
    park_map = rename_pmx_bones_to_menace(pmx_armature, config.bone_map, config.ignore_bones)

    orient_stub_core_bones(pmx_armature)

    apply_mesh_textures(pmx_meshes, config.mesh_textures)

    print("[info] rebuilding PMX materials for glTF")
    rebuild_materials_for_gltf(pmx_meshes, config.pmx_path)

    apply_rigid_binds(pmx_meshes, config.rigid_bind_meshes)

    print("[info] folding renamed vertex groups (merge duplicates after bone rename)")
    for mesh in pmx_meshes:
        remove_all_modifiers(mesh)
        remap_vertex_groups(mesh, config, park_map)
        remove_unmapped_vertex_groups(mesh, pmx_armature)
        bind_unweighted_to_nearest_bone(mesh, pmx_armature)
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
    apply_reference_tpose_calibration(pmx_armature, pmx_meshes, reference_tpose, palm_locals)

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

    return pmx_armature, pmx_meshes


def join_source_meshes(pmx_meshes: list) -> list:
    """Join multi-mesh sources (FBX rips split body/face/hair/cloth) into
    one mesh so the LOD chain has a single root. Material slots and vertex
    groups survive the join. Single-mesh sources pass through unchanged."""
    if len(pmx_meshes) <= 1:
        return pmx_meshes
    print(f"[info] joining {len(pmx_meshes)} source meshes into one")
    clear_selection()
    for mesh in pmx_meshes:
        mesh.select_set(True)
    bpy.context.view_layer.objects.active = pmx_meshes[0]
    bpy.ops.object.join()
    clear_selection()
    return [pmx_meshes[0]]


def finish_pmx(config: "TransferConfig", pmx_armature, pmx_meshes) -> None:
    """Stages 9-11: conform LOD names, decimate LOD1-N, purge, export glTF.
    Decimation runs on copies of LOD0, so any hand-painted weights on LOD0
    (the full-res mesh) propagate down to the lower LODs automatically."""
    pmx_meshes = join_source_meshes(pmx_meshes)

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


def recover_handoff_scene(config: "TransferConfig", blend_path: Path) -> tuple:
    """Re-open a --stage prep .blend and recover (armature, [LOD0 mesh]) for the
    finish stage. Prefers the mesh named '<basename>_LOD0'; falls back to the
    deform mesh bound to the armature with the most vertices."""
    if not blend_path.exists():
        raise FileNotFoundError(f"handoff .blend not found: {blend_path}")
    bpy.ops.wm.open_mainfile(filepath=str(blend_path))
    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError(f"{blend_path} contains no armature.")
    armature = armatures[0]
    lod0_name = f"{config.lod_mesh_basename}_LOD0"
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    chosen = [o for o in meshes if o.name == lod0_name]
    if not chosen:
        bound = [
            o for o in meshes
            if any(m.type == "ARMATURE" and m.object == armature for m in o.modifiers)
        ] or meshes
        bound.sort(key=lambda o: len(o.data.vertices), reverse=True)
        chosen = bound[:1]
        if chosen:
            print(f"[warn] no mesh named '{lod0_name}', using '{chosen[0].name}'")
    if not chosen:
        raise RuntimeError(f"{blend_path} contains no usable mesh.")
    return armature, chosen


def main() -> None:
    args = parse_args()
    config = TransferConfig.load(Path(args.config).resolve())

    if args.stage == "finish":
        armature, meshes = recover_handoff_scene(config, Path(args.blend).resolve())
        finish_pmx(config, armature, meshes)
        return

    armature, meshes = prep_pmx(config)

    if args.stage == "prep":
        # Drop mmd_tools' rigid-body / joint helper objects so the handoff
        # scene is just the armature + the character mesh, clean to paint.
        # The data API removes regardless of selectability — mmd_tools parks
        # its rigid bodies in a hidden collection that bpy.ops.object.delete
        # (used by purge_non_authored_scene) silently skips.
        keep = {armature.name, *(m.name for m in meshes)}
        for obj in [o for o in bpy.data.objects if o.name not in keep]:
            bpy.data.objects.remove(obj, do_unlink=True)
        for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.armatures):
            for datum in list(block):
                if datum.users == 0:
                    block.remove(datum)
        print(f"[info] handoff scene reduced to {len(bpy.data.objects)} object(s)")
        # Multi-mesh sources join BEFORE the handoff: the finish stage
        # recovers exactly one mesh from the .blend, so unjoined siblings
        # would be silently purged after the weight-painting round-trip.
        meshes = join_source_meshes(meshes)
        # Name the single full-res mesh LOD0 so the finish stage can find it,
        # then hand the bound, T-posed scene off for manual weight painting.
        lod0_name = f"{config.lod_mesh_basename}_LOD0"
        meshes[0].name = lod0_name
        meshes[0].data.name = lod0_name
        blend_path = Path(args.blend).resolve()
        blend_path.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
        print(f"[done] handoff scene written to: {blend_path}")
        print("[next] weight-paint LOD0, save, then re-run with --stage finish")
        return

    finish_pmx(config, armature, meshes)


if __name__ == "__main__":
    main()
