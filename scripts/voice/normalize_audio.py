#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "soundfile", "pyloudnorm", "librosa"]
# ///
"""
Loudness-normalize voymastina audio additions to match vanilla MENACE's
sy bark levels.

Pass 1 (measure): walks a directory of reference vanilla clips (any format
soundfile/librosa can decode — .wav, .ogg), computes the integrated LUFS
loudness of each, and reports the median as the target.

Pass 2 (normalize): walks the voymastina additions directory, measures
each clip's LUFS, applies gain to hit the target, writes the result back
in place. Preserves sample rate and channel count.

Usage:
    uv run --script normalize_audio.py \\
        --reference ~/.local/share/jiangyu/cache/exports/ \\
        --target /home/justin/dev/github.com/antistrategie/WOMENACE/assets/additions/audio/voymastina/

Add --dry-run to see the report without writing files.
"""

from __future__ import annotations

import argparse
import statistics
import sys
from pathlib import Path

import librosa
import numpy as np
import pyloudnorm as pyln
import soundfile as sf


def measure_lufs(path: Path) -> float | None:
    try:
        data, sr = librosa.load(str(path), sr=None, mono=False)
    except Exception as ex:
        print(f"  skip {path.name}: read failed ({ex})", file=sys.stderr)
        return None
    # librosa returns shape (channels, samples) for multichannel or (samples,) for mono.
    if data.ndim == 1:
        audio_for_meter = data
    else:
        # pyloudnorm wants shape (samples, channels)
        audio_for_meter = data.T
    if len(audio_for_meter) < int(sr * 0.4):
        # Clip shorter than 400 ms — pyloudnorm requires that minimum.
        return None
    meter = pyln.Meter(sr)
    try:
        return meter.integrated_loudness(audio_for_meter)
    except Exception as ex:
        print(f"  skip {path.name}: meter failed ({ex})", file=sys.stderr)
        return None


def normalize_in_place(path: Path, target_lufs: float, peak_ceiling_db: float = -1.0) -> bool:
    """Apply gain to hit target LUFS; clip-protect to peak_ceiling_db. Returns True on write."""
    try:
        data, sr = sf.read(str(path), always_2d=False)
    except Exception as ex:
        print(f"  skip {path.name}: read failed ({ex})", file=sys.stderr)
        return False

    if data.ndim == 1:
        audio_for_meter = data
    else:
        audio_for_meter = data  # pyloudnorm wants (samples, channels) — soundfile gives that

    if len(audio_for_meter) < int(sr * 0.4):
        print(f"  skip {path.name}: too short for LUFS measurement", file=sys.stderr)
        return False

    meter = pyln.Meter(sr)
    try:
        current = meter.integrated_loudness(audio_for_meter)
    except Exception as ex:
        print(f"  skip {path.name}: meter failed ({ex})", file=sys.stderr)
        return False

    if np.isinf(current):
        print(f"  skip {path.name}: silent track (-inf LUFS)", file=sys.stderr)
        return False

    gain_db = target_lufs - current
    gain_lin = 10 ** (gain_db / 20)
    out = data.astype(np.float32) * gain_lin

    # Peak protection: don't let the loudest sample exceed peak_ceiling_db.
    peak = float(np.abs(out).max())
    ceiling = 10 ** (peak_ceiling_db / 20)
    if peak > ceiling:
        out = out * (ceiling / peak)
        applied_db = gain_db - 20 * np.log10(peak / ceiling)
    else:
        applied_db = gain_db

    sf.write(str(path), out, sr)
    print(
        f"  {path.name}: {current:+.1f} LUFS -> {target_lufs:+.1f} (applied {applied_db:+.1f} dB)",
        file=sys.stderr,
    )
    return True


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    ap.add_argument("--reference", type=Path, required=True,
                    help="directory of vanilla audio to measure (any format)")
    ap.add_argument("--target", type=Path, required=True,
                    help="directory of voymastina .wav files to normalize")
    ap.add_argument("--peak-ceiling-db", type=float, default=-1.0,
                    help="don't allow peaks above this dBFS after gain (default -1)")
    ap.add_argument("--dry-run", action="store_true",
                    help="measure only, don't write")
    args = ap.parse_args()

    if not args.reference.is_dir():
        print(f"error: reference {args.reference} is not a directory", file=sys.stderr)
        return 1
    if not args.target.is_dir():
        print(f"error: target {args.target} is not a directory", file=sys.stderr)
        return 1

    # Measure reference clips.
    print(f"measuring reference clips in {args.reference}...", file=sys.stderr)
    ref_files = sorted(
        list(args.reference.glob("*.wav"))
        + list(args.reference.glob("*.ogg"))
        + list(args.reference.glob("*.flac"))
    )
    if not ref_files:
        print("error: no audio in reference dir", file=sys.stderr)
        return 1
    lufs_values = []
    for p in ref_files:
        v = measure_lufs(p)
        if v is not None and not np.isinf(v):
            lufs_values.append(v)
            print(f"  {p.name}: {v:+.1f} LUFS", file=sys.stderr)
    if not lufs_values:
        print("error: no valid LUFS measurements", file=sys.stderr)
        return 1
    target = statistics.median(lufs_values)
    print(
        f"\nreference median LUFS: {target:+.1f} "
        f"(min {min(lufs_values):+.1f}, max {max(lufs_values):+.1f}, n={len(lufs_values)})",
        file=sys.stderr,
    )

    if args.dry_run:
        # Just measure target dir without writing.
        print(f"\ndry-run: measuring target clips in {args.target}...", file=sys.stderr)
        for p in sorted(args.target.glob("*.wav")):
            v = measure_lufs(p)
            if v is not None and not np.isinf(v):
                delta = target - v
                print(f"  {p.name}: {v:+.1f} LUFS (would apply {delta:+.1f} dB)", file=sys.stderr)
        return 0

    print(f"\nnormalizing target clips in {args.target}...", file=sys.stderr)
    written = 0
    for p in sorted(args.target.glob("*.wav")):
        if normalize_in_place(p, target, args.peak_ceiling_db):
            written += 1
    print(f"\ndone: {written} files updated", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
