"""Read archives out of the GFL2 client's pack files.

`GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows` holds the client's `.bundle` files. Many of
them are packs: several UnityFS archives stored end to end, each one encrypted on its own. An
archive is addressed here by its pack file and its byte offset there. Cut out and decrypted, it is
a plain UnityFS bundle that AssetStudio and UnityPy read normally.

The client also keeps a `GFF` index (one `.bundle` file whose header reads `GFF\\0`) that maps each
archive's virtual bundle name to its pack, offset and size. Nothing here reads it. A name search
needs the objects each archive holds, which the GFF index does not record, so `scan_packs` opens
every archive anyway, and walking a pack's archive headers finds the same pieces the index lists.
A caller that already knows an archive's pack and offset reads it directly with `read_archive`.

Standard library only, so any script can import it. A script under `scripts/` reaches it by
putting `scripts/` on `sys.path` and importing `gfl2.client`.
"""
import os
import struct
import sys
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

# The client is built on 2019.4. Its archives strip the engine version, so UnityPy needs it
# supplied (`UnityPy.config.FALLBACK_UNITY_VERSION`) to read the type trees.
UNITY_VERSION = "2019.4.29f1"

# Every archive starts with this header. The client XORs the first 32 KiB of each archive with a
# 16 byte key, so the key is the archive's first 16 bytes read against it.
SIGNATURE = b"UnityFS\0\0\0\0\x075.x."
SPAN = 32768


def decrypt(data: bytes) -> bytes:
    if data.startswith(b"UnityFS\0"):
        return data
    key = bytes(a ^ b for a, b in zip(data[:16], SIGNATURE))
    count = min(len(data), SPAN)
    return bytes(data[i] ^ key[i % 16] for i in range(count)) + data[count:]


def _length(header: bytes) -> int:
    """An archive's total length, which sits past the signature and two version strings."""
    position = 12
    for _ in range(2):
        position = header.index(b"\0", position) + 1
    return struct.unpack_from(">q", header, position)[0]


def archives(path: Path):
    """Every archive in one pack file, as (offset, decrypted bytes)."""
    path = Path(path)
    size = path.stat().st_size
    offset = 0
    with path.open("rb") as stream:
        while offset + 64 < size:
            stream.seek(offset)
            header = decrypt(stream.read(256))
            if not header.startswith(b"UnityFS\0"):
                return
            length = _length(header)
            if not 0 < length <= size - offset:
                return
            stream.seek(offset)
            yield offset, decrypt(stream.read(length))
            offset += length


def read_archive(path: Path, offset: int) -> bytes:
    """The archive at one offset of a pack file, decrypted."""
    path = Path(path)
    with path.open("rb") as stream:
        stream.seek(offset)
        header = decrypt(stream.read(256))
        if not header.startswith(b"UnityFS\0"):
            raise ValueError(f"Not a UnityFS archive: {path}:{offset}")
        length = _length(header)
        if not 0 < length <= path.stat().st_size - offset:
            raise ValueError(f"Invalid archive length: {path}:{offset}")
        stream.seek(offset)
        return decrypt(stream.read(length))


def packs(bundles: Path) -> list[Path]:
    return sorted(p for p in Path(bundles).iterdir() if p.suffix == ".bundle")


def scan_packs(bundles: Path, per_pack):
    """Run `per_pack` (a module-level function of one pack path) over every pack in parallel and
    yield each result as it finishes. The client has about 61,000 archives, so this takes minutes."""
    files = packs(bundles)
    with ProcessPoolExecutor(max(2, (os.cpu_count() or 4) - 2)) as pool:
        futures = [pool.submit(per_pack, p) for p in files]
        for done, future in enumerate(as_completed(futures), 1):
            yield future.result()
            if done % 1000 == 0 or done == len(files):
                print(f"walked {done}/{len(files)} packs", file=sys.stderr, flush=True)
