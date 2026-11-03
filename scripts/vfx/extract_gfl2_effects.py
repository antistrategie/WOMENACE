#!/usr/bin/env python3
"""Extract GFL2 particle effects from the CN client for the Unity effect builder.

Reads a spec JSON naming the source archives and the effects to take from them:

    {
      "archives": [["<pack>.bundle", <offset>], ...],
      "effects": {"<output name>": "<source root GameObject>", ...}
    }

and writes `source.json` plus `textures/*.png` beside the spec. The Unity side
(`Womenace.EditorTools.BuildGfl2Effects`) turns that into materials, meshes and
prefabs. Source bundles concatenate independently encrypted UnityFS archives, so
each archive is addressed by its pack file and byte offset, not by bundle name.

Particle systems keep their native serialisation (with type-tree versions) so
Unity reads curves, gradients and bursts unchanged. Object references inside a
particle system are stripped, except sub-emitters, which are recorded as paths
relative to the effect root, and shape meshes, which are exported with the rest
of the geometry.

    uv run --with UnityPy --with lz4 python -I scripts/vfx/extract_gfl2_effects.py \\
      --bundles "<GF2_Exilium_Data>/LocalCache/Data/AssetBundles_Windows" \\
      --spec unity/Assets/Authored/springfield/taryz/effects/spec.json
"""

import argparse
import json
import struct
from pathlib import Path

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

UnityPy.config.FALLBACK_UNITY_VERSION = "2019.4.29f1"

SIGNATURE = b"UnityFS\0\0\0\0\x075.x."


def decrypt(data):
    if data.startswith(b"UnityFS\0"):
        return data
    key = bytes(a ^ b for a, b in zip(data[:16], SIGNATURE))
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


def with_versions(value, node):
    """Keep native serialisation versions so Unity does not run obsolete upgrades."""
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


class Builtin:
    """A reference into Unity's built-in resources, which no bundle carries."""

    # Path ids of the built-in extra resources a GFL2 effect uses.
    NAMES = {10202: "Cube", 10206: "Cylinder", 10207: "Sphere", 10208: "Capsule", 10209: "Plane",
             10210: "Quad", 10300: "Default-Particle", 10301: "Default-ParticleSystem"}

    def __init__(self, cab, path_id):
        self.cab = cab
        self.path_id = path_id
        self.name = self.NAMES.get(path_id, f"builtin_{path_id}")


class Source:
    def __init__(self, bundles, archives):
        self.objects = {}
        self.environments = []
        for filename, offset in archives:
            environment = UnityPy.Environment()
            environment.load_file(read_archive(bundles / filename, offset), name=f"{filename}:{offset}")
            self.environments.append(environment)
            for obj in environment.objects:
                self.objects[(obj.assets_file.name.lower(), obj.path_id)] = obj

    def resolve(self, owner, reference):
        if not reference or not reference.get("m_PathID"):
            return None
        file_id = reference["m_FileID"]
        cab = (owner.assets_file.externals[file_id - 1].path.rsplit("/", 1)[-1]
               if file_id else owner.assets_file.name).lower()
        if not cab.startswith("cab-"):
            # Unity's built-in resources: the Unity side substitutes its own copy by name.
            return Builtin(cab, reference["m_PathID"])
        obj = self.objects.get((cab, reference["m_PathID"]))
        if obj is None:
            raise KeyError(f"{cab} is not in the spec's archives (path id {reference['m_PathID']})")
        return obj

    def find_root(self, name):
        for obj in self.objects.values():
            if obj.type.name != "GameObject":
                continue
            tree = obj.read_typetree()
            if tree["m_Name"] != name:
                continue
            for component in tree["m_Component"]:
                transform = self.resolve(obj, component["component"])
                if transform.type.name == "Transform" and not transform.read_typetree()["m_Father"]["m_PathID"]:
                    return obj
        raise KeyError(f"No root GameObject named {name}")


def extract(bundles, spec_path):
    spec = json.loads(spec_path.read_text())
    output = spec_path.parent
    source = Source(bundles, spec["archives"])
    materials, meshes, textures = {}, {}, {}
    warnings = []

    def resource(owner, reference):
        obj = source.resolve(owner, reference)
        if obj is None:
            return ""
        if isinstance(obj, Builtin):
            return "builtin:" + obj.name
        name = obj.read_typetree()["m_Name"]
        kind = obj.type.name
        target = {"Material": materials, "Mesh": meshes, "Texture2D": textures}.get(kind)
        if target is None:
            raise ValueError(f"Unexpected effect resource: {kind} {name}")
        existing = target.get(name)
        if existing is not None and (existing.assets_file.name, existing.path_id) != (obj.assets_file.name, obj.path_id):
            raise ValueError(f"Two different {kind} assets are named {name}")
        target[name] = obj
        return name

    def effect(root_go):
        def go_of(transform):
            return source.resolve(transform, transform.read_typetree()["m_GameObject"])

        paths = {}

        def index(transform, path):
            paths[(transform.assets_file.name, transform.path_id)] = path
            for child in transform.read_typetree()["m_Children"]:
                child_transform = source.resolve(transform, child)
                name = go_of(child_transform).read_typetree()["m_Name"]
                index(child_transform, f"{path}/{name}" if path else name)

        root_transform = next(
            t for t in (source.resolve(root_go, c["component"]) for c in root_go.read_typetree()["m_Component"])
            if t.type.name == "Transform")
        index(root_transform, "")

        def path_of_particles(owner, reference):
            particles = source.resolve(owner, reference)
            go = source.resolve(particles, particles.read_typetree()["m_GameObject"])
            for component in go.read_typetree()["m_Component"]:
                transform = source.resolve(go, component["component"])
                if transform.type.name == "Transform":
                    return paths[(transform.assets_file.name, transform.path_id)]
            raise KeyError("Sub-emitter has no transform")

        def particle_json(obj, node_path, sub_emitters, shape_meshes):
            tree = with_versions(obj.read_typetree(), obj.serialized_type.node)
            del tree["m_GameObject"]
            module = tree.get("SubModule")
            if module is not None:
                for entry in module.get("subEmitters", []):
                    if entry["emitter"]["m_PathID"]:
                        sub_emitters.append({
                            "path": path_of_particles(obj, entry["emitter"]),
                            "type": entry["type"],
                            "properties": entry["properties"],
                            "probability": entry.get("emitProbability", 1.0),
                        })
                module["subEmitters"] = []
            shape = tree.get("ShapeModule")
            if shape is not None and shape.get("m_Mesh", {}).get("m_PathID"):
                shape_meshes.append(resource(obj, shape["m_Mesh"]))

            def strip(value, path):
                if isinstance(value, dict):
                    if "m_PathID" in value and "m_FileID" in value:
                        if value["m_PathID"]:
                            warnings.append(f"{node_path}: dropped reference at {path}")
                        return {"instanceID": 0}
                    return {key: strip(child, f"{path}.{key}") for key, child in value.items()}
                if isinstance(value, list):
                    return [strip(child, path) for child in value]
                return value

            return json.dumps(strip(tree, "ParticleSystem"), separators=(",", ":"), allow_nan=False)

        def node(transform, node_path):
            tree = transform.read_typetree()
            go = go_of(transform).read_typetree()
            result = {
                "name": go["m_Name"],
                "path": node_path,
                "active": bool(go["m_IsActive"]),
                "position": tree["m_LocalPosition"],
                "rotation": tree["m_LocalRotation"],
                "scale": tree["m_LocalScale"],
                "particleJson": "",
                "rendererJson": "",
                "materials": [],
                "meshes": [],
                "shapeMeshes": [],
                "subEmitters": [],
                "children": [],
            }
            for component in go["m_Component"]:
                obj = source.resolve(transform, component["component"])
                kind = obj.type.name
                if kind == "ParticleSystem":
                    result["particleJson"] = particle_json(obj, node_path, result["subEmitters"], result["shapeMeshes"])
                elif kind == "ParticleSystemRenderer":
                    renderer = with_versions(obj.read_typetree(), obj.serialized_type.node)
                    del renderer["m_GameObject"]
                    result["materials"] = [resource(obj, ref) for ref in renderer.pop("m_Materials")]
                    result["meshes"] = [resource(obj, renderer.pop(key)) for key in ("m_Mesh", "m_Mesh1", "m_Mesh2", "m_Mesh3")]
                    for key in ("m_StaticBatchRoot", "m_ProbeAnchor", "m_LightProbeVolumeOverride"):
                        renderer.pop(key, None)
                    result["rendererJson"] = json.dumps(renderer, separators=(",", ":"), allow_nan=False)
                elif kind not in ("Transform", "MonoBehaviour", "Light"):
                    warnings.append(f"{node_path}: dropped {kind} component")
            for child in tree["m_Children"]:
                child_transform = source.resolve(transform, child)
                name = go_of(child_transform).read_typetree()["m_Name"]
                result["children"].append(node(child_transform, f"{node_path}/{name}" if node_path else name))
            return result

        return node(root_transform, "")

    effects = []
    for output_name, root_name in spec["effects"].items():
        root = effect(source.find_root(root_name))
        root["name"] = output_name
        root["source"] = root_name
        effects.append(root)

    material_data = []
    for name, obj in sorted(materials.items()):
        tree = obj.read_typetree()
        shader = source.resolve(obj, tree["m_Shader"])
        shader_name = shader.read_typetree()["m_ParsedForm"]["m_Name"] if shader else ""
        properties = tree["m_SavedProperties"]
        material_data.append({
            "name": name,
            "shader": shader_name,
            "keywords": tree.get("m_ShaderKeywords") or " ".join(tree.get("m_ValidKeywords") or []),
            "floats": [{"name": key, "value": value} for key, value in properties["m_Floats"]],
            # GFL2's disabled camera fade stores infinity, which JSON cannot carry.
            "colours": [{"name": key, "value": value} for key, value in properties["m_Colors"]
                        if all(abs(c) != float("inf") for c in value.values())],
            "textures": [{"name": key, "texture": resource(obj, value["m_Texture"]),
                          "scale": value["m_Scale"], "offset": value["m_Offset"]}
                         for key, value in properties["m_TexEnvs"] if value["m_Texture"]["m_PathID"]],
        })

    mesh_data = []
    for name, obj in sorted(meshes.items()):
        mesh = MeshHandler(obj.read())
        mesh.process()
        submeshes = mesh.get_triangles()
        mesh_data.append({
            "name": name,
            "vertices": [dict(zip("xyz", v)) for v in mesh.m_Vertices],
            "normals": [dict(zip("xyz", v)) for v in (mesh.m_Normals or [])],
            "colours": [dict(zip("rgba", c)) for c in (mesh.m_Colors or [])],
            "uv": [dict(zip("xy", v)) for v in (mesh.m_UV0 or [])],
            "uv2": [dict(zip("xy", v)) for v in (mesh.m_UV1 or [])],
            "submeshes": [{"triangles": [i for tri in sub for i in tri]} for sub in submeshes],
        })

    (output / "textures").mkdir(parents=True, exist_ok=True)
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

    result = {"effects": effects, "materials": material_data, "meshes": mesh_data, "textures": texture_data}
    (output / "source.json").write_text(json.dumps(result, indent=1, allow_nan=False) + "\n")
    for warning in warnings:
        print("warning:", warning)
    print(f"Extracted {len(effects)} effects, {len(material_data)} materials, "
          f"{len(mesh_data)} meshes and {len(texture_data)} textures to {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--bundles", type=Path, required=True, help="GFL2 CN AssetBundles_Windows directory")
    parser.add_argument("--spec", type=Path, required=True, help="Spec JSON; outputs are written beside it")
    args = parser.parse_args()
    extract(args.bundles, args.spec)
