#!/usr/bin/env python3
"""Extract Sinbreaker's native particle data and resources from the GFL2 CN client.

Requires UnityPy. The Unity editor builds the meshes, materials and prefabs from
source.json. Source bundles concatenate independently encrypted UnityFS archives,
so these offsets address an archive, not an object inside the first archive.
"""

import argparse
import json
import math
import struct
from pathlib import Path

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler


MODEL_BUNDLE = "07a19a46f978facf2788df78d91d0746.bundle"
THRUSTER_GEOMETRY = (
    "c_VoymastinaSSR01_Mech_slg_G1_lod0",
    "c_VoymastinaSSR01_Mech_slg_G2_lod0",
)
SHADER_VARIANTS = {
    -2495521360321488460: 0,  # UV_Effect_UVAll_Fast_2
    5733032150767803583: 1,   # UV_Effect_UVAll_Fast_2_New
    -2163029941690609794: 2,  # Particle_Base_Bland_Fast
}
ARCHIVES = [
    (MODEL_BUNDLE, 0),
    ("dc81c33230041cbc001647d2992c9a46.bundle", 5077287),
    ("eb159176c1d42f59ba14d6b8a30eec76.bundle", 5311297),
    ("885eea9fcca73bdadde4ecd6a3af1312.bundle", 4884827),
    ("8df9efa9eeea5454306585548c0b3832.bundle", 278357),
    ("ee975ea8adb7cca96782f4696df48744.bundle", 387633),
    ("955fa5119f09de4dd465708e0b8ed405.bundle", 6825760),
    ("ffb59c752357dcf96d6aa48a526f19e6.bundle", 7133800),
    ("8ed54b13e8c2de3a704b3657bd0fd0e1.bundle", 12523398),
    ("cbdc9398c34d5d74f6e592578482c5bc.bundle", 3643671),
    ("d97fbdc2ac15971a5f3e0e04513294da.bundle", 6772467),
    ("cba64e8ed7d08564a8b26090790b7c4d.bundle", 4346331),
    ("4ffbf368837de6fb5a5a6d70895a4ab5.bundle", 6953601),
    ("7782b1d72f11b63531d4af5a058082c5.bundle", 0),
    ("f2d3c5ad3abe1654708a128abe2e5478.bundle", 4424718),
    ("1690704fa5be58ed865b5f14967be2de.bundle", 5257837),
    ("66d49b5e8bd38b9dd17eba69a7658bdc.bundle", 233812),
    ("bbd466f229aff1838b0ae797885eb3ad.bundle", 5881188),
    ("8f3f17ade08e995eec8cadc1f1116f80.bundle", 3960811),
    ("667313243849ba45c7acbcac74ee54b9.bundle", 4009169),
    ("ed7d3d8d40ba9d482a3a3c772f770f6d.bundle", 5533472),
    ("bbd466f229aff1838b0ae797885eb3ad.bundle", 4874621),
]


def decrypt(data):
    if data.startswith(b"UnityFS\0"):
        return data
    signature = b"UnityFS\0\0\0\0\x075.x."
    key = bytes(a ^ b for a, b in zip(data[:16], signature))
    count = min(len(data), 32768)
    return bytes(data[i] ^ key[i % 16] for i in range(count)) + data[count:]


def read_archive(path, offset):
    with path.open("rb") as stream:
        stream.seek(offset)
        header = decrypt(stream.read(256))
        if not header.startswith(b"UnityFS\0"):
            raise ValueError(f"Not a UnityFS archive: {path}:{offset}")
        position = 12
        for _ in range(2):
            position = header.index(b"\0", position) + 1
        length = struct.unpack_from(">q", header, position)[0]
        if not 0 < length <= path.stat().st_size - offset:
            raise ValueError(f"Invalid archive length: {path}:{offset}")
        stream.seek(offset)
        return decrypt(stream.read(length))


def vectors(values, axes):
    return [dict(zip(axes, value)) for value in values]


def with_versions(value, node):
    """Keep native serialisation versions so Unity does not run obsolete upgrades.

    Missing versions reinterpret float colours as bytes and discard modern bursts.
    UnityPy exposes the versions on type-tree nodes, not in read_typetree's values.
    """
    if isinstance(value, dict):
        children = {child.m_Name: child for child in node.m_Children}
        result = {key: with_versions(child, children[key]) for key, child in value.items()}
        if node.m_Version > 1:
            result = {"serializedVersion": node.m_Version, **result}
        return result
    if isinstance(value, list):
        array = next(child for child in node.m_Children if child.m_Type == "Array")
        element = next(child for child in array.m_Children if child.m_Name == "data")
        return [with_versions(child, element) for child in value]
    return value


def assert_same_effect(expected, actual, path):
    if isinstance(expected, dict):
        if expected.keys() != actual.keys():
            raise ValueError(f"Different effect fields at {path}")
        for key in expected:
            if key == "randomSeed" and expected.get("autoRandomSeed"):
                continue
            assert_same_effect(expected[key], actual[key], f"{path}.{key}")
    elif isinstance(expected, list):
        if len(expected) != len(actual):
            raise ValueError(f"Different effect array length at {path}")
        for index, (left, right) in enumerate(zip(expected, actual)):
            assert_same_effect(left, right, f"{path}[{index}]")
    elif isinstance(expected, str) and path.endswith(("particleJson", "rendererJson")):
        assert_same_effect(json.loads(expected), json.loads(actual), path)
    elif isinstance(expected, (int, float)) and not isinstance(expected, bool):
        if not math.isclose(expected, actual, rel_tol=1e-5, abs_tol=1e-5):
            raise ValueError(f"Different effect value at {path}: {expected} != {actual}")
    elif expected != actual:
        raise ValueError(f"Different effect value at {path}: {expected} != {actual}")


def extract(bundles, output):
    environments = []
    objects = {}
    for filename, offset in ARCHIVES:
        environment = UnityPy.Environment()
        environment.load_file(read_archive(bundles / filename, offset), name=filename)
        environments.append(environment)
        for obj in environment.objects:
            objects[(obj.assets_file.name, obj.path_id)] = obj

    def resolve(owner, reference):
        if not reference["m_PathID"]:
            return None
        file_id = reference["m_FileID"]
        cab = (owner.assets_file.externals[file_id - 1].path.rsplit("/", 1)[-1]
               if file_id else owner.assets_file.name)
        return objects[(cab, reference["m_PathID"])]

    model = environments[0]
    gos = {o.path_id: o.read_typetree() for o in model.objects if o.type.name == "GameObject"}
    transforms = {o.path_id: o.read_typetree() for o in model.objects if o.type.name == "Transform"}
    model_objects = {o.path_id: o for o in model.objects}

    def transform_path(tid):
        transform = transforms[tid]
        name = gos[transform["m_GameObject"]["m_PathID"]]["m_Name"]
        parent = transform["m_Father"]["m_PathID"]
        return f"{transform_path(parent)}/{name}" if parent in transforms else name

    materials = {}
    meshes = {}
    textures = {}

    def resource(owner, reference):
        obj = resolve(owner, reference)
        if obj is None:
            return ""
        tree = obj.read_typetree()
        name = tree["m_Name"]
        if obj.type.name == "Material":
            materials[name] = obj
        elif obj.type.name == "Mesh":
            meshes[name] = obj
        elif obj.type.name == "Texture2D":
            textures[name] = obj
        else:
            raise ValueError(f"Unexpected particle resource: {obj.type.name} {name}")
        return name

    def strip_references(value, path=""):
        if isinstance(value, dict):
            if "m_PathID" in value:
                if value["m_PathID"]:
                    raise ValueError(f"Unhandled source reference at {path}: {value}")
                return {"instanceID": 0}
            return {key: strip_references(child, f"{path}.{key}") for key, child in value.items()}
        if isinstance(value, list):
            return [strip_references(child, path) for child in value]
        return value

    def node(tid):
        transform = transforms[tid]
        go = gos[transform["m_GameObject"]["m_PathID"]]
        result = {
            "name": go["m_Name"],
            "active": go["m_IsActive"],
            "position": transform["m_LocalPosition"],
            "rotation": transform["m_LocalRotation"],
            "scale": transform["m_LocalScale"],
            "children": [node(c["m_PathID"]) for c in transform["m_Children"]],
        }
        for component in go["m_Component"]:
            obj = model_objects[component["component"]["m_PathID"]]
            if obj.type.name == "ParticleSystem":
                tree = with_versions(obj.read_typetree(), obj.serialized_type.node)
                del tree["m_GameObject"]
                result["particleJson"] = json.dumps(strip_references(tree), separators=(",", ":"), allow_nan=False)
            elif obj.type.name == "ParticleSystemRenderer":
                tree = with_versions(obj.read_typetree(), obj.serialized_type.node)
                del tree["m_GameObject"]
                result["materials"] = [resource(obj, ref) for ref in tree.pop("m_Materials")]
                result["meshes"] = [resource(obj, tree.pop(key)) for key in ("m_Mesh", "m_Mesh1", "m_Mesh2", "m_Mesh3")]
                result["rendererJson"] = json.dumps(strip_references(tree), separators=(",", ":"), allow_nan=False)
            elif obj.type.name not in ("Transform", "MonoBehaviour"):
                raise ValueError(f"Unexpected effect component: {obj.type.name}")
        return result

    effects = {}
    mounts = []
    geometry = []
    for tid, transform in transforms.items():
        path = transform_path(tid)
        go = gos[transform["m_GameObject"]["m_PathID"]]
        name = go["m_Name"]
        if not path.startswith("BattleVoymastinaSSR01/"):
            continue
        if name in THRUSTER_GEOMETRY:
            geometry.append({"name": name, "active": go["m_IsActive"]})
        if "Jet" not in name or "Child" in name:
            continue
        if not name.endswith("_Node"):
            continue
        for child in transform["m_Children"]:
            child_transform = transforms[child["m_PathID"]]
            child_name = gos[child_transform["m_GameObject"]["m_PathID"]]["m_Name"]
            assert len(child_transform["m_Children"]) == 1, child_name
            effect = node(child_transform["m_Children"][0]["m_PathID"])
            # The root carries only an almost-identity attachment transform.
            # Keep it per mount rather than baking one nozzle's transform into all copies.
            mounts.append({
                "bone": child_name,
                "effect": effect["name"],
                "position": effect["position"],
                "rotation": effect["rotation"],
                "scale": effect["scale"],
            })
            effect.update(position={"x": 0, "y": 0, "z": 0},
                          rotation={"x": 0, "y": 0, "z": 0, "w": 1},
                          scale={"x": 1, "y": 1, "z": 1})
            if effect["name"] in effects:
                assert_same_effect(effects[effect["name"]], effect, child_name)
            effects.setdefault(effect["name"], effect)
    assert len(mounts) == 24 and len(effects) == 8, (len(mounts), len(effects))
    assert len(geometry) == len(THRUSTER_GEOMETRY), geometry

    material_data = []
    for name, obj in sorted(materials.items()):
        tree = obj.read_typetree()
        properties = tree["m_SavedProperties"]
        material_data.append({
            "name": name,
            "shaderVariant": SHADER_VARIANTS[tree["m_Shader"]["m_PathID"]],
            "floats": [{"name": key, "value": value} for key, value in properties["m_Floats"]],
            # The disabled GFL2 camera fade stores infinity and is not part of the ported shader.
            "colours": [{"name": key, "value": value} for key, value in properties["m_Colors"]
                        if key != "_CameraFadeParams"],
            "textures": [{"name": key, "texture": resource(obj, value["m_Texture"]),
                          "scale": value["m_Scale"], "offset": value["m_Offset"]}
                         for key, value in properties["m_TexEnvs"]],
        })

    mesh_data = []
    for name, obj in sorted(meshes.items()):
        mesh = MeshHandler(obj.read())
        mesh.process()
        triangles = mesh.get_triangles()
        assert len(triangles) == 1, name
        mesh_data.append({
            "name": name,
            "vertices": vectors(mesh.m_Vertices, "xyz"),
            "normals": vectors(mesh.m_Normals, "xyz"),
            "uv": vectors(mesh.m_UV0, "xy"),
            "triangles": [index for triangle in triangles[0] for index in triangle],
        })

    output.mkdir(parents=True, exist_ok=True)
    (output / "textures").mkdir(exist_ok=True)
    texture_data = []
    for name, obj in sorted(textures.items()):
        obj.read().image.save(output / "textures" / f"{name}.png")
        tree = obj.read_typetree()
        settings = tree["m_TextureSettings"]
        texture_data.append({
            "name": name, "sRGB": tree["m_ColorSpace"] == 1, "mipmaps": tree["m_MipCount"] > 1,
            "filterMode": settings["m_FilterMode"], "aniso": settings["m_Aniso"],
            "wrapU": settings["m_WrapU"], "wrapV": settings["m_WrapV"], "wrapW": settings["m_WrapW"],
        })
    assert len(materials) == 10 and len(meshes) == 2 and len(textures) == 10
    source = {"effects": [effects[name] for name in sorted(effects)], "mounts": mounts,
              "materials": material_data, "meshes": mesh_data, "textures": texture_data,
              "geometry": sorted(geometry, key=lambda entry: entry["name"])}
    (output / "source.json").write_text(json.dumps(source, indent=2, allow_nan=False) + "\n")
    print(f"Extracted {len(effects)} effect phases, {len(mounts)} mounts, "
          f"{len(materials)} materials, {len(meshes)} meshes and {len(textures)} textures to {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundles", type=Path, required=True, help="GFL2 CN AssetBundles_Windows directory")
    parser.add_argument("--output", type=Path,
                        default=Path(__file__).resolve().parents[2] / "unity/Assets/Authored/voymastina_mech/thrusters")
    args = parser.parse_args()
    extract(args.bundles, args.output)
