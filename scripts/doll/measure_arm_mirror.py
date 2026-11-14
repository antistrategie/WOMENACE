#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "scipy"]
# ///
"""Measure how far a doll's left arm is from being her right arm's mirror image.

A doll's two arms are modelled as mirror images, and pmx_to_menace.py swings each to the T-pose
without turning it about its own length, so they stay that way. An arm baked with a turn about
the limb shows here as the turn that lays the mirrored left arm back over the right, a segment at
a time (upper arm, forearm, hand). A sleeve's stripe then sits on the wrong side of the arm, which
is hard to see on a plain sleeve. Hands are posed differently on each side and never fit closely.
One-sided clothing on an arm gives a turn that means nothing.

    uv run scripts/doll/measure_arm_mirror.py unity/Assets/Authored/makiatto/default
"""
import argparse
import json
import math
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree

COMPONENTS = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
SEGMENTS = ("UpperArm", "LowerArm", "Hand")


class Model:
    def __init__(self, folder: Path):
        self.path = folder / "model.gltf"
        self.gltf = json.loads(self.path.read_text())
        self.bin_path = folder / self.gltf["buffers"][0]["uri"]
        self.data = bytearray(self.bin_path.read_bytes())
        self.nodes = self.gltf["nodes"]
        self.index = {n.get("name"): i for i, n in enumerate(self.nodes)}
        self.parent = {c: i for i, n in enumerate(self.nodes) for c in n.get("children", [])}
        self.world = {}

    def accessor(self, i):
        a = self.gltf["accessors"][i]
        view = self.gltf["bufferViews"][a["bufferView"]]
        kind = np.dtype(COMPONENTS[a["componentType"]])
        width = WIDTHS[a["type"]]
        start = view.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = view.get("byteStride", kind.itemsize * width)
        if stride != kind.itemsize * width:
            raise SystemExit("interleaved buffer views are not handled")
        return np.frombuffer(self.data, dtype=kind, count=a["count"] * width, offset=start).reshape(a["count"], width)

    def local(self, i):
        n = self.nodes[i]
        if "matrix" in n:
            return np.array(n["matrix"], dtype=np.float64).reshape(4, 4).T
        m = np.eye(4)
        x, y, z, w = n.get("rotation", [0, 0, 0, 1])
        m[:3, :3] = np.array([
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
        m[:3, :3] *= np.array(n.get("scale", [1, 1, 1]))
        m[:3, 3] = n.get("translation", [0, 0, 0])
        return m

    def world_of(self, i):
        if i not in self.world:
            m = self.local(i)
            self.world[i] = self.world_of(self.parent[i]) @ m if i in self.parent else m
        return self.world[i]

    def head(self, name):
        return self.world_of(self.index[name])[:3, 3]

    def lod0(self):
        """Every vertex of the first mesh: positions, joints (as node indices) and weights."""
        joints = self.gltf["skins"][0]["joints"]
        seen, positions, bones, weights = set(), [], [], []
        for primitive in self.gltf["meshes"][0]["primitives"]:
            at = primitive["attributes"]
            if at["POSITION"] in seen:
                continue
            seen.add(at["POSITION"])
            positions.append(self.accessor(at["POSITION"]).astype(np.float64))
            bones.append(np.array(joints)[self.accessor(at["JOINTS_0"]).astype(np.int64)])
            w = self.accessor(at["WEIGHTS_0"]).astype(np.float64)
            if w.max() > 1.5:
                w = w / w.sum(axis=1, keepdims=True)
            weights.append(w)
        return np.concatenate(positions), np.concatenate(bones), np.concatenate(weights)


def turn_about(axis, angle):
    axis = axis / np.linalg.norm(axis)
    x, y, z = axis
    c, s = math.cos(angle), math.sin(angle)
    return np.array([
        [c + x * x * (1 - c), x * y * (1 - c) - z * s, x * z * (1 - c) + y * s],
        [y * x * (1 - c) + z * s, c + y * y * (1 - c), y * z * (1 - c) - x * s],
        [z * x * (1 - c) - y * s, z * y * (1 - c) + x * s, c + z * z * (1 - c)]])


def measure(model: Model):
    positions, bones, weights = model.lod0()
    ends = {"UpperArm": "LowerArm", "LowerArm": "Hand"}
    for segment in SEGMENTS:
        sides = {}
        for side in ("L", "R"):
            bone = model.index.get(f"{segment}_{side}")
            if bone is None:
                break
            share = (weights * (bones == bone)).sum(axis=1)
            sides[side] = positions[share > 0.85]
        if len(sides) < 2 or min(len(v) for v in sides.values()) < 40:
            print(f"  {segment}: too few vertices to measure")
            continue
        start = model.head(f"{segment}_R")
        if segment in ends:
            axis = model.head(f"{ends[segment]}_R") - start
        else:
            axis = start - model.head("LowerArm_R")
        mirrored = sides["L"] * np.array([-1.0, 1.0, 1.0])
        tree = cKDTree(sides["R"])
        best = None
        for degrees in np.arange(-180.0, 180.0, 1.0):
            turned = (mirrored - start) @ turn_about(axis, math.radians(degrees)).T + start
            miss = tree.query(turned)[0].mean()
            if best is None or miss < best[1]:
                best = (degrees, miss)
        untouched = tree.query(mirrored)[0].mean()
        print(f"  {segment}: the mirrored left fits the right best turned {best[0]:+.0f} degrees "
              f"(mean miss {best[1] * 1000:.1f} mm, {untouched * 1000:.1f} mm as it is), "
              f"{len(sides['L'])} and {len(sides['R'])} vertices")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("dolls", nargs="+", type=Path, help="e.g. unity/Assets/Authored/makiatto/default")
    for folder in parser.parse_args().dolls:
        print(folder)
        measure(Model(folder))


if __name__ == "__main__":
    main()
