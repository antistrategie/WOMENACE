#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["openai>=1.50", "python-dotenv"]
# ///
"""
Transcribe + translate a directory of Japanese voice clips via OpenAI.

Two API calls per clip:
  1. Audio transcription via gpt-4o-transcribe (or override). language=ja.
  2. Chat completion via gpt-5 (or override) to translate JP → EN.

OpenAI's gpt-4o-transcribe hears 指揮官 ("commander") correctly where Qwen2-Audio
phoneticised it to シキカん / Shyian. Translation through a frontier text model
also reads better than the audio-LLM two-pass approach we were using.

Reads OPENAI_API_KEY from the environment. Writes <source>/.trans.csv with
columns: filename, transcript, english, note.

Usage:
    uv run --script transcribe.py assets/additions/audio/cheyanne
    uv run --script transcribe.py path/to/clips --asr-model gpt-4o-transcribe --translate-model gpt-5
"""
from __future__ import annotations

import argparse
import csv
import os
import sys
from pathlib import Path

import openai
from dotenv import load_dotenv

# Load .env from repo root (parent of scripts/voice/) so OPENAI_API_KEY is picked up
# without needing to export it in every shell.
load_dotenv(Path(__file__).resolve().parents[2] / ".env")

DEFAULT_OUTPUT_NAME = ".trans.csv"
DEFAULT_ASR_MODEL = "gpt-4o-transcribe"
DEFAULT_TRANSLATE_MODEL = "gpt-5"

TRANSLATE_SYSTEM = (
    "You are a translator. Translate the user's Japanese text into natural "
    "conversational English. The input is a single voice line from a tactical "
    "squad-based game (Marines, snipers, combat barks). Match the tone — short "
    "exclamations stay short, formal lines stay formal. Output only the English "
    "translation, no commentary, no quotes."
)


def transcribe_jp(client: openai.OpenAI, model: str, wav_path: Path) -> str:
    with wav_path.open("rb") as f:
        result = client.audio.transcriptions.create(
            model=model,
            file=f,
            language="ja",
        )
    return (result.text or "").strip()


def translate_en(client: openai.OpenAI, model: str, jp_text: str) -> str:
    if not jp_text:
        return ""
    resp = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "system", "content": TRANSLATE_SYSTEM},
            {"role": "user", "content": jp_text},
        ],
    )
    return (resp.choices[0].message.content or "").strip()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    ap.add_argument("source", type=Path, help="directory of .wav files to transcribe")
    ap.add_argument(
        "-o",
        "--output",
        type=Path,
        default=None,
        help="output CSV path (default: <source>/.trans.csv)",
    )
    ap.add_argument(
        "--asr-model",
        default=DEFAULT_ASR_MODEL,
        help=f"OpenAI ASR model (default: {DEFAULT_ASR_MODEL})",
    )
    ap.add_argument(
        "--translate-model",
        default=DEFAULT_TRANSLATE_MODEL,
        help=f"OpenAI translation model (default: {DEFAULT_TRANSLATE_MODEL})",
    )
    args = ap.parse_args()

    if not args.source.is_dir():
        print(f"error: {args.source} is not a directory", file=sys.stderr)
        return 1
    if not os.environ.get("OPENAI_API_KEY"):
        print("error: OPENAI_API_KEY environment variable not set", file=sys.stderr)
        return 1

    output_path = args.output if args.output is not None else args.source / DEFAULT_OUTPUT_NAME
    wavs = sorted(args.source.glob("*.wav"))
    if not wavs:
        print(f"no .wav files in {args.source}", file=sys.stderr)
        return 1

    output_path.parent.mkdir(parents=True, exist_ok=True)
    client = openai.OpenAI()

    print(
        f"transcribing {len(wavs)} clip(s) from {args.source.name} "
        f"(ASR: {args.asr_model}, MT: {args.translate_model})",
        file=sys.stderr,
    )
    with output_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["filename", "transcript", "english", "note"])
        for i, wav in enumerate(wavs, 1):
            try:
                jp = transcribe_jp(client, args.asr_model, wav)
                en = translate_en(client, args.translate_model, jp)
            except Exception as ex:
                print(f"[{i}/{len(wavs)}] {wav.name}: ERROR {type(ex).__name__}: {ex}", file=sys.stderr)
                jp, en = "", ""
            writer.writerow([wav.name, jp, en, ""])
            f.flush()
            print(f"[{i}/{len(wavs)}] {wav.name}: {jp[:60]} | {en[:60]}", file=sys.stderr)

    print(f"wrote {output_path} ({len(wavs)} rows)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
