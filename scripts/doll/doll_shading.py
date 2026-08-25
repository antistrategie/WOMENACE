#!/usr/bin/env python3
"""Resolve a doll's glTF material names to shader and ramp bake arguments.

One shared rule table serves every doll. GFL2 names character parts
conventionally, so the part a material belongs to is readable from its name:
Face, EyeWhite, Eyes, Eyes+, EyeShadow and Tongue appear in 27 of the 28
doll/outfit glTFs in this project, BodySkin in 20. Outfit-specific pieces
(Cloth1-Hat, Glove, P1-Cloth-Skirt) are cloth surfaces, which is what the
fallback covers, so no doll needs its own entry.

The twenty-eighth is a port taken from the game's own model rather than from a
PMX, which names its materials after their textures. The rules match those too,
which is why the part words are matched anywhere in a name and not only at its
front.

Emits the -overrideShaderFor and -setTextureFor arguments BakeHumanoid takes.
Materials whose part has no ramp yet resolve to nothing and stay on the game's
own shader, which is how the face and eyes wait for their own shading paths
without holding up the rest of the model.

    python3 scripts/doll/doll_shading.py unity/Assets/Authored/makiatto/default
"""

import fnmatch
import json
import re
import sys
from pathlib import Path
from urllib.parse import unquote

SHADER = "Womenace/DollToon"

# GFL2 names a texture after the material that binds it, and its material names
# carry the shader: uber, ubertrans, faceuber, eyelashuber. So a base map whose
# name carries ubertrans is the game saying that surface is drawn blended, and a
# base map that does not is the game saying it is not.
#
# That name is the whole test, and deliberately so. A sub-255 alpha on a _da map
# is not evidence: silk stockings, zips and sunglass lenses all measure partial
# across most of their footprint and the game draws every one of them opaque.
# Routing from an alpha histogram is what put the car's wheels on a blended
# shader.
TRANSPARENT_SHADER = "Womenace/DollToonTrans"
TRANSPARENT_MARKER = "ubertrans"

# The marker test reads the game's own texture naming, and a PMX repack can
# lose it. These outfits' repacks did: the cn client draws the listed
# materials' meshes under _trans_ mesh names (c_ClukaySSR0104_slg_cloth1_dt_
# trans_lod0 and friends, checked against the client asset map), but the
# repacked sheets dropped the ubertrans marker from the texture names. Keyed
# by the Authored/<doll>/<outfit> directory pair, unioned with the marker
# test. The names are the game's verdict carried over by hand, not an alpha
# measurement.
TRANSLUCENT_OVERRIDES = {
    "klukai/indigo_oath": {"Veil", "SuitLace", "SuitBra", "SkirtC", "SkirtD"},
}

# First match wins, so the specific parts come before the general ones. Matching
# is case-insensitive and ignores a trailing duplicate suffix, because Hair.001
# and Teeth.001 are exporter artefacts for a repeated name rather than distinct
# parts.
PART_RULES = [
    # The inverted-hull outline's own submesh: the outlined parts drawn again with
    # front faces culled. Matched before everything else so its name does not fall
    # through to the part it was copied from.
    ("outline", "outline"),
    ("*hair*", "hair"),
    # Mouth and eye-white are welded into the face: they share vertices with it,
    # so leaving them on the game's shader draws a line around each. Measured
    # solid (mouth, teeth and tongue export opaque; the eye-white's alpha is 255
    # across its whole footprint), so they need no cutoff and take the face's own
    # path, which is the only way the seam disappears rather than moves.
    ("*teeth*", "face"),
    ("*tongue*", "face"),
    ("*mouth*", "face"),
    ("*eyewhite*", "face"),
    # Measured solid, not alpha-cut: sampling triangle interiors across all three
    # returns alpha 255 everywhere, so the shapes are in the geometry and no cutoff is
    # needed. The exporter's MASK alpha mode was being cautious, not descriptive. They
    # take the face's own path, which the game does too: its eyelash materials carry
    # _UseBlendTex alongside its face materials.
    ("*brow*", "face"),
    ("*lash*", "face"),
    ("*eyelid*", "face"),
    # The face skin itself, which is continuous with the neck: the two meet at a
    # shared boundary, so leaving one on the game's shader and the other on ours
    # draws a seam along the jaw. Takes the skin ramp, which is what the game
    # does too, its capture binding the same ramp for face and skin.
    #
    # Matched anywhere in the name, not only at the front: a port taken from the
    # game's own model rather than from a PMX names its materials after their
    # textures, so the face arrives as c_SextansSSR01_slg_face_d.
    ("*face*", "face"),
    # The game's three-material eye stack. Each layer takes its own shader because
    # a blend mode is fixed per pass: the eyeball is opaque, the darkening layer
    # multiplies, the highlight adds.
    ("*eyeshadow*", "eyeshadow"),
    ("eyes+", "eyehighlight"),
    ("eyes*", "eye"),
    # The emotion overlays are inert and stay on the game's shader: measured, all 476
    # Emotions1 vertices sit behind the face surface and Emotions2 is a degenerate
    # plane inside the head sampling fully transparent texels. They are MMD-style
    # expression quads parked out of sight until a morph brings them forward, and
    # MENACE has nothing to drive that.
    ("*iris*", "eye"),
    ("*pupil*", "eye"),
    ("*emotion*", "emotion"),
    ("*socks*", "silkstock"),
    ("*stocking*", "silkstock"),
    ("*weapon*", "weapon"),
    ("*bodyskin*", "skin"),
    ("*skin*", "skin"),
    ("*fingernail*", "skin"),
]
FALLBACK_PART = "cloth"

# Ramp atlas per part, resolved against the doll's sibling ramps directory
# first and Authored/shared/ramps second. The split follows the game's own:
# hair, cloth and silkstock ship as per-character gradient assets and genuinely
# differ per character, where no character ships a skin, face or weapon
# gradient at all, those atlases being global dumps. A part absent here takes
# no ramp and keeps the game's shader.
# Each part names its atlas, and where a part lists more than one the first
# found wins. Leva, Sextans and Voymastina ship no silkstock gradient of their
# own, so their stockings fall back to the character's cloth atlas rather than
# to a global one: the game's silkstock gradients sit close to their character's
# cloth and nowhere near each other.
PART_RAMPS = {
    "hair": ("ramp_hair.png",),
    "cloth": ("ramp_cloth_main.png",),
    "skin": ("ramp_skin.png",),
    "silkstock": ("ramp_silkstock.png", "ramp_cloth_main.png"),
    # The game's own weapon ramp: the capture's binding map puts ResourceId 6550
    # on the weapon draws, and this file is that atlas reoriented.
    "weapon": ("ramp_weapon.png",),
    # The game binds one ramp for face and skin alike, so the face takes the
    # skin's. It reaches that ramp by the FaceSDF sweep rather than by N dot L,
    # off the lookup coordinates bake_face_sdf_uv.py reconstructs.
    "face": ("ramp_skin.png",),
    # emotion stays on the game's shader: the overlays are parked out of sight.
}

# Parts whose shader is not the main doll shader. These take no ramp: they are
# blend layers composited over what is already drawn, so they carry no lighting
# model of their own beyond what their own shader does.
PART_SHADERS = {
    "eye": "Womenace/DollEye",
    "eyeshadow": "Womenace/DollEyeShadow",
    "eyehighlight": "Womenace/DollEyeHighlight",
    "outline": "Womenace/DollOutline",
}


def part_for(material_name):
    """The GFL2 part a source material belongs to."""
    name = material_name.lower()
    base = re.sub(r"\.\d+$", "", name)
    for pattern, part in PART_RULES:
        if fnmatch.fnmatch(name, pattern) or fnmatch.fnmatch(base, pattern):
            return part
    return FALLBACK_PART


def material_names(gltf_path):
    with open(gltf_path) as handle:
        doc = json.load(handle)
    return sorted({m.get("name", "") for m in doc.get("materials", []) if m.get("name")})


def base_texture_by_material(gltf_path):
    """Each material's base-colour texture filename, from the glTF itself.

    Sibling maps are found by swapping the suffix on this name rather than by
    guessing from the material name: the weapon's material is called Weapon while
    its textures are named qiang, so only the texture name relates them.
    """
    with open(gltf_path) as handle:
        doc = json.load(handle)
    images = [i.get("uri", "") for i in doc.get("images", [])]
    textures = [t.get("source") for t in doc.get("textures", [])]
    out = {}
    for material in doc.get("materials", []):
        name = material.get("name")
        index = material.get("pbrMetallicRoughness", {}).get("baseColorTexture", {}).get("index")
        if not name or index is None or index >= len(textures):
            continue
        source = textures[index]
        if source is None or source >= len(images):
            continue
        out[name] = unquote(images[source])
    return out


def translucent_materials(gltf_path):
    """Material names the game draws blended, by their base map's name."""
    gltf_path = Path(gltf_path)
    outfit_key = f"{gltf_path.parent.parent.name}/{gltf_path.parent.name}"
    marked = {name for name, texture in base_texture_by_material(gltf_path).items()
              if TRANSPARENT_MARKER in Path(texture).stem.lower()}
    return marked | TRANSLUCENT_OVERRIDES.get(outfit_key, set())


# Suffix on the base map, and the sibling suffixes to look for. A material
# shipping _smo rather than _rmo carries inverted roughness, so it is matched
# separately and left for the shader to interpret.
SIBLING_MAPS = [
    ("_NormalMap", ["_n"]),
    ("_MaskMap", ["_rmo", "_smo"]),
    # Hair specular. Only hair ships one, and the shader's default is black, so a
    # material without it contributes no highlight.
    ("_SpecularMap", ["_spc"]),
]


def sibling_assignments(doll_dir, material, base_texture, inverted):
    """Shader property to texture path, for maps sitting beside the base map.

    Appends to `inverted` any material wired from an `_smo` map, whose R is
    smoothness rather than roughness. The shader cannot tell the two apart from
    the texture alone, so it reads _MaskRoughnessInverted, and nothing here can
    set a float. Reported rather than wired silently: an unflagged _smo renders
    every rough surface glossy and every gloss surface matte.
    """
    stem = Path(base_texture).stem
    suffix = None
    for candidate in ("_d", "_da"):
        if stem.endswith(candidate):
            suffix = candidate
            break
    if suffix is None:
        return []
    root = stem[: -len(suffix)]

    found = []
    for prop, wanted in SIBLING_MAPS:
        for want in wanted:
            for folder in (doll_dir, doll_dir / "normalmap"):
                for ext in (".png", ".tga"):
                    candidate = folder / f"{root}{want}{ext}"
                    if candidate.is_file():
                        rel = str(candidate).split("unity/", 1)[-1]
                        found.append(f"{material}:{prop}={rel}")
                        if want == "_smo":
                            inverted.append(material)
                        break
                else:
                    continue
                break
            else:
                continue
            break
    return found


def resolve(doll_dir):
    doll_dir = Path(doll_dir)
    gltf = doll_dir / "model.gltf"
    if not gltf.is_file():
        raise SystemExit(f"no model.gltf in {doll_dir}")

    # Per-character ramps sit beside the outfit folders; the global set sits in
    # Authored/shared/ramps. A doll overrides a shared ramp by shipping its own.
    ramps_dirs = [doll_dir.parent / "ramps",
                  doll_dir.parent.parent / "shared" / "ramps"]

    bases = base_texture_by_material(gltf)
    translucent = translucent_materials(gltf)
    shader_pairs, texture_triples, float_triples, skipped, inverted = [], [], [], {}, []
    for name in material_names(gltf):
        part = part_for(name)

        # A part with its own shader and no ramp: the bake wires its base texture
        # from the glTF, and the shader needs nothing else.
        own_shader = PART_SHADERS.get(part)
        if own_shader is not None:
            shader_pairs.append(f"{name}={own_shader}")
            continue

        ramps = PART_RAMPS.get(part)
        if ramps is None:
            skipped.setdefault(part, []).append(name)
            continue
        ramp_asset = next((directory / ramp
                           for ramp in ramps for directory in ramps_dirs
                           if (directory / ramp).is_file()), None)
        if ramp_asset is None:
            raise SystemExit(f"ramp missing for part '{part}': "
                             + " or ".join(ramps) + " not in "
                             + " or ".join(str(d) for d in ramps_dirs))
        # Ramps live under Assets/, and the bake takes project-relative paths.
        rel = str(ramp_asset).split("unity/", 1)[-1]
        shader_pairs.append(
            f"{name}={TRANSPARENT_SHADER if name in translucent else SHADER}")
        texture_triples.append(f"{name}:_RampMap={rel}")
        # Normal and RMO maps, when the source ships them beside the base map.
        base = bases.get(name)
        if base:
            texture_triples.extend(
                sibling_assignments(doll_dir, name, base, inverted))

        # The face shades from its SDF sweep rather than N dot L. The map is
        # shared: the game binds one across some thirty face materials, which
        # works because every GFL2 face unwraps the same way.
        if part == "face":
            sdf = Path("Assets/Authored/shared/face_sdf.png")
            if not (doll_dir.parents[1] / "shared" / "face_sdf.png").is_file():
                raise SystemExit(f"face SDF map missing: {sdf}")
            texture_triples.append(f"{name}:_SdfMap={sdf}")
            float_triples.append(f"{name}:_UseBlendTex=1")

        # The hair specular path, gated on the character's game-hair reference:
        # transfer_hair_uv writes the strip UV into TEXCOORD_1 when that dump
        # exists, and only then does driving the streak from it mean anything.
        # A non-zero intensity is also what routes hair off GGX.
        if part == "hair" and (doll_dir.parent / "hair_uv1_ref.npz").is_file():
            float_triples.append(f"{name}:_MatCapIntensity=1")


    return shader_pairs, texture_triples, float_triples, skipped, inverted


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    shader_pairs, texture_triples, float_triples, skipped, inverted = resolve(sys.argv[1])

    for part, names in sorted(skipped.items()):
        print(f"# no ramp for part '{part}' yet, keeping the game shader: "
              f"{', '.join(names)}", file=sys.stderr)
    if inverted:
        print(f"# WIRED FROM _smo, SET _MaskRoughnessInverted=1 ON THESE OR THEIR "
              f"ROUGHNESS READS BACKWARDS: {', '.join(sorted(set(inverted)))}",
              file=sys.stderr)
    print(f"# {len(shader_pairs)} material(s) shaded", file=sys.stderr)

    # Printed as three lines so a caller can read them into shell variables:
    # -overrideShaderFor, -setTextureFor, -setFloatFor.
    print(",".join(shader_pairs))
    print(",".join(texture_triples))
    print(",".join(float_triples))


if __name__ == "__main__":
    main()
