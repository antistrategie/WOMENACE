"""Cut a music track down to the part that loops.

A soundtrack release of a looping piece plays its loop twice and fades out. The game's
music repeats a whole clip end to start (Stem's SoundInstance.Looped sets AudioSource.loop),
so a track loops without a break only when the clip is exactly one pass of the loop. This
writes the second pass: it starts at the top of the piece, carries what rings over from the
first pass, and ends where the piece comes round to its top again.

The period is found from the track itself. `--period` is a guess in seconds (how long one
pass is), refined to the sample by lining the two passes up. The last 40 ms of the pass are
faded into the 40 ms before it, which are the same music, so the little a lossy source's two
passes differ by does not click. The report says how alike the two passes are: about -20 dB
is an MP3's own noise, and anything above -12 dB is not a repeat.

    uv run --with numpy --with soundfile scripts/music/loop_track.py SOURCE OUT.wav --period 60

SOURCE is anything ffmpeg reads.
"""
import argparse
import subprocess
import tempfile
from pathlib import Path

import numpy as np
import soundfile as sf

CROSSFADE = 0.04


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--period", type=float, required=True, help="one pass of the loop, in seconds, roughly")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory() as work:
        decoded = Path(work) / "decoded.wav"
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", str(args.source), "-vn", "-c:a", "pcm_s16le", str(decoded)], check=True)
        audio, rate = sf.read(decoded, dtype="float64", always_2d=True)
    mid = audio.mean(axis=1)

    # The period to the sample: six seconds of the first pass against the second, a quarter
    # of a second either side of the guess.
    guess = round(args.period * rate)
    at, window, reach = 20 * rate, 6 * rate, rate // 4
    if at + guess + reach + window > len(mid):
        raise SystemExit("the track is too short to hold two passes of that period")
    found = np.correlate(mid[at + guess - reach:at + guess + reach + window], mid[at:at + window], mode="valid")
    period = guess - reach + int(np.argmax(found))
    if 2 * period > len(mid):
        raise SystemExit("the track does not hold two whole passes")

    def difference(start: int, length: int) -> float:
        first, second = mid[start:start + length], mid[start + period:start + period + length]
        return 20 * np.log10(np.sqrt(np.mean((first - second) ** 2)) / (np.sqrt(np.mean(first ** 2)) + 1e-12) + 1e-12)

    half = rate // 2
    body = float(np.median([difference(t, half) for t in range(period // 4, period - half, half)]))
    seam = difference(period - half, half)

    fade = round(CROSSFADE * rate)
    ramp = np.linspace(0.0, 1.0, fade)[:, None]
    loop = audio[period:2 * period].copy()
    loop[-fade:] = loop[-fade:] * np.cos(ramp * np.pi / 2) + audio[period - fade:period] * np.sin(ramp * np.pi / 2)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    sf.write(args.out, loop, rate, subtype="PCM_16")
    print(f"{args.out.name}: one pass is {period} samples ({period / rate:.3f} s at {rate} Hz). "
          f"The two passes differ by {body:.1f} dB in the body and {seam:.1f} dB in the half second before the seam.")


if __name__ == "__main__":
    main()
