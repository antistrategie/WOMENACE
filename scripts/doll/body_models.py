#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = []
# ///
"""Print each GFL2 character's body model, the code her profile gives (SSD-62D, SST-05 and so on).

A Doll's dummy links are named from her body model, so a new Doll needs her line in BodyModels
(code/Systems/Dolls/BodyModels.cs). The codes come from the client's own tables:
GunCharacterData holds a row per character, with field 2 her name and field 16 her body model,
each as a text id that LangPackageTableEnusData resolves to English.

Every table is a little-endian u32 index length, a protobuf index of (row id, offset, length),
then the rows, each a protobuf. A row of these two tables is a bucket of entries.

Run with the Table folder of the Steam client
(<GFL2>/GF2_Exilium_Data/LocalCache/Data/Table), and optionally names to filter by:
    uv run scripts/doll/body_models.py <table dir> [name ...]
"""

import struct
import sys
from pathlib import Path


def varint(b, i):
    result = shift = 0
    while True:
        c = b[i]
        i += 1
        result |= (c & 0x7F) << shift
        shift += 7
        if c < 0x80:
            return result, i


def fields(b):
    """(field, value) pairs of a protobuf message: an int for a varint, bytes for a length."""
    i, out = 0, []
    while i < len(b):
        key, i = varint(b, i)
        field, wire = key >> 3, key & 7
        if wire == 0:
            value, i = varint(b, i)
        elif wire == 2:
            n, i = varint(b, i)
            value, i = b[i : i + n], i + n
        elif wire == 5:
            value, i = b[i : i + 4], i + 4
        elif wire == 1:
            value, i = b[i : i + 8], i + 8
        else:
            raise ValueError(f"wire type {wire}")
        out.append((field, value))
    return out


def entries(path):
    """Every entry of a table, each as its list of fields."""
    b = path.read_bytes()
    n = struct.unpack_from("<I", b, 0)[0]
    base = 4 + n
    for field, row in fields(b[4:base]):
        if field != 3:
            continue
        loc = dict(fields(dict(fields(row))[2]))
        start = base + loc.get(1, 0)
        for _, entry in fields(b[start : start + loc[2]]):
            yield fields(entry)


def text_id(entry, field):
    """The text id a field holds as {1: id}, or None."""
    for f, value in entry:
        if f == field and isinstance(value, bytes):
            return dict(fields(value)).get(1)
    return None


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    tables = Path(sys.argv[1])
    wanted = {name.lower() for name in sys.argv[2:]}
    english = {}
    for entry in entries(tables / "LangPackageTableEnusData.bytes"):
        e = dict(entry)
        if isinstance(e.get(2), bytes):
            english[e[1]] = e[2].decode("utf-8", "replace")
    for entry in entries(tables / "GunCharacterData.bytes"):
        name = english.get(text_id(entry, 2))
        model = english.get(text_id(entry, 16))
        if name is None or name.startswith("Test "):
            continue
        if wanted and name.lower() not in wanted:
            continue
        print(f"{name}\t{model or '(none)'}")


if __name__ == "__main__":
    main()
