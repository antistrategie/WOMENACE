"""Cut a music track into a clip the game can open on and then loop without a break.

A soundtrack release of a looping piece plays its opening, goes round its loop twice and
fades out. The game repeats a whole clip end to start, which would play the opening again on
every round, so ModMusic goes round a part of the clip instead: once playback is into the
clip's tail it is moved back by one pass of the loop.

The clip is the track from its first sample to the end of the loop's first pass, and then a
tail: the ten seconds that follow the loop's start, a second time. The tail is the same
samples as the ten seconds one pass before it, so the move back lands on the music that was
playing and nothing is heard of it. The last 40 ms before the tail are faded into the 40 ms
before the loop's start, which are the same music a pass apart, so the little a lossy
source's two passes differ by does not click where the tail is joined on.

`--start` is where the loop has begun, in seconds: any moment after the opening from which
the track is the same one pass later. `--period` is a guess at how long one pass is, refined
to the sample by lining the two passes up. The report gives the pass in samples, which is the
number ModMusic.Loops wants, and says how alike the two passes are: about -20 dB is an MP3's
own noise, and anything above -12 dB is not a repeat.

    uv run --with numpy --with soundfile scripts/music/loop_track.py SOURCE OUT.wav --start 4 --period 93

SOURCE is anything ffmpeg reads.
"""
import argparse
import subprocess
import tempfile
from pathlib import Path

import numpy as np
import soundfile as sf

CROSSFADE = 0.04
# How much of the loop's start is repeated after its first pass. ModMusic.TailSeconds is the
# same number.
TAIL = 10.0


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--start", type=float, required=True, help="a moment after the opening, in seconds, from which the track repeats")
    parser.add_argument("--period", type=float, required=True, help="one pass of the loop, in seconds, roughly")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory() as work:
        decoded = Path(work) / "decoded.wav"
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", str(args.source), "-vn", "-c:a", "pcm_s16le", str(decoded)], check=True)
        audio, rate = sf.read(decoded, dtype="float64", always_2d=True)
    mid = audio.mean(axis=1)
    start, tail, fade = round(args.start * rate), round(TAIL * rate), round(CROSSFADE * rate)

    # The period to the sample: six seconds of the first pass against the second, a quarter
    # of a second either side of the guess.
    guess = round(args.period * rate)
    at, window, reach = start + 2 * rate, 6 * rate, rate // 4
    if at + guess + reach + window > len(mid):
        raise SystemExit("the track is too short to hold two passes of that period")
    found = np.correlate(mid[at + guess - reach:at + guess + reach + window], mid[at:at + window], mode="valid")
    period = guess - reach + int(np.argmax(found))
    if start + period + tail > len(mid):
        raise SystemExit("the track does not hold a pass and a tail after that start")

    def difference(begin: int, length: int) -> float:
        first, second = mid[begin:begin + length], mid[begin + period:begin + period + length]
        return 20 * np.log10(np.sqrt(np.mean((first - second) ** 2)) / (np.sqrt(np.mean(first ** 2)) + 1e-12) + 1e-12)

    half = rate // 2
    body = float(np.median([difference(t, half) for t in range(start, start + tail, half)]))
    seam = difference(start - half, half)

    ramp = np.linspace(0.0, 1.0, fade)[:, None]
    clip = audio[:start + period].copy()
    clip[-fade:] = clip[-fade:] * np.cos(ramp * np.pi / 2) + audio[start - fade:start] * np.sin(ramp * np.pi / 2)
    clip = np.concatenate([clip, audio[start:start + tail]])
    args.out.parent.mkdir(parents=True, exist_ok=True)
    sf.write(args.out, clip, rate, subtype="PCM_16")
    print(f"{args.out.name}: one pass is {period} samples at {rate} Hz, the clip {len(clip)} samples ({len(clip) / rate:.2f} s). "
          f"The two passes differ by {body:.1f} dB over the tail and {seam:.1f} dB in the half second before the loop's start.")


if __name__ == "__main__":
    main()
