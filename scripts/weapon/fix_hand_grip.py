#!/usr/bin/env python3
"""Fix the left-hand grip on a baked weapon GLB.

Blender's glTF exporter has a round-trip bug that negates the x and w
components of the `weapon_hand_l` empty's rotation quaternion, flipping the
off-hand 180 degrees about the barrel. The fix is to negate x and w again,
restoring the intended grip. Position and every other node are left untouched.

    scripts/weapon/fix_hand_grip.py unity/Assets/Authored/weapon/m4_ssr/raw.glb

Pass --node to target a differently named IK empty, and --dry-run to preview
the change without writing. After running, re-bake the prefab from the GLB
(Jiangyu.Mod.BakeWeapon) so the corrected empty lands in main.prefab.
"""

import argparse
import json
import struct
import sys

JSON_CHUNK = b"JSON"
BIN_CHUNK = b"BIN\x00"


def read_glb(path):
    """Return (json_dict, bin_chunk_bytes) from a binary glTF file."""
    with open(path, "rb") as handle:
        data = handle.read()
    if data[:4] != b"glTF":
        raise SystemExit(f"{path} is not a binary glTF (.glb)")
    gltf = None
    binary = None
    offset = 12  # skip magic + version + total length
    while offset < len(data):
        length = struct.unpack("<I", data[offset : offset + 4])[0]
        chunk_type = data[offset + 4 : offset + 8]
        chunk = data[offset + 8 : offset + 8 + length]
        if chunk_type == JSON_CHUNK:
            gltf = json.loads(chunk.decode("utf-8"))
        elif chunk_type == BIN_CHUNK:
            binary = chunk
        offset += 8 + length
    if gltf is None:
        raise SystemExit(f"{path} has no JSON chunk")
    return gltf, binary


def write_glb(path, gltf, binary):
    """Write a binary glTF from a JSON dict and the untouched BIN chunk."""
    json_bytes = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    json_bytes += b" " * ((4 - len(json_bytes) % 4) % 4)  # pad to 4 bytes with spaces

    chunks = struct.pack("<I", len(json_bytes)) + JSON_CHUNK + json_bytes
    if binary is not None:
        bin_bytes = binary + b"\x00" * ((4 - len(binary) % 4) % 4)
        chunks += struct.pack("<I", len(bin_bytes)) + BIN_CHUNK + bin_bytes

    total = 12 + len(chunks)
    header = b"glTF" + struct.pack("<II", 2, total)
    with open(path, "wb") as handle:
        handle.write(header + chunks)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("glb", help="the weapon raw.glb to fix in place")
    parser.add_argument("--node", default="weapon_hand_l", help="name of the IK empty (default weapon_hand_l)")
    parser.add_argument("--dry-run", action="store_true", help="show the change without writing")
    options = parser.parse_args()

    gltf, binary = read_glb(options.glb)
    target = next((n for n in gltf.get("nodes", []) if n.get("name") == options.node), None)
    if target is None:
        raise SystemExit(f"no node named '{options.node}' in {options.glb}")
    rotation = target.get("rotation")
    if not rotation or len(rotation) != 4:
        raise SystemExit(f"'{options.node}' has no rotation quaternion to flip")

    fixed = [-rotation[0], rotation[1], rotation[2], -rotation[3]]
    print(f"{options.node} rotation")
    print(f"  before: {rotation}")
    print(f"  after:  {fixed}")
    if options.dry_run:
        print("(dry run, not written)")
        return

    target["rotation"] = fixed
    write_glb(options.glb, gltf, binary)
    print(f"wrote {options.glb}. Re-bake the prefab so the fix reaches main.prefab.")


if __name__ == "__main__":
    sys.exit(main())
