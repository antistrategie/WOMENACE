#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = [
#     "torch>=2.4",
#     "transformers>=4.45",
#     "soundfile",
#     "librosa",
#     "accelerate",
#     "sentencepiece",
# ]
# ///
"""
General-purpose Japanese audio transcription for character voice dumps.

Walks a directory of .wav files, runs each through Qwen3-ASR-1.7B for JP
transcription, then through Helsinki-NLP/opus-mt-ja-en for English. Writes
a CSV with one row per clip.

Usage:
    uv run --script transcribe.py /path/to/VO_Character_JP -o character.csv

Output CSV columns:
    source_dir   the immediate parent directory name (e.g. VO_Voymastina_JP)
    filename     the .wav filename
    transcript   the JP transcription
    english      the EN translation
    note         left blank (hand-annotate post-run if needed)

Designed to be reusable across characters. The model weights are downloaded
into ~/.cache/huggingface on first run.
"""

from __future__ import annotations

import argparse
import csv
import os
import sys
from pathlib import Path

import librosa
import soundfile as sf
import torch
from transformers import (
    AutoModelForSeq2SeqLM,
    AutoModelForSpeechSeq2Seq,
    AutoProcessor,
    AutoTokenizer,
)


ASR_MODEL_ID = "Qwen/Qwen3-ASR-1.7B"
TRANSLATION_MODEL_ID = "Helsinki-NLP/opus-mt-ja-en"
TARGET_SAMPLE_RATE = 16000


def load_models(device: str):
    print(f"loading ASR model {ASR_MODEL_ID} on {device}...", file=sys.stderr)
    asr_processor = AutoProcessor.from_pretrained(ASR_MODEL_ID, trust_remote_code=True)
    asr_model = AutoModelForSpeechSeq2Seq.from_pretrained(
        ASR_MODEL_ID,
        torch_dtype=torch.float16 if device != "cpu" else torch.float32,
        low_cpu_mem_usage=True,
        trust_remote_code=True,
    ).to(device)
    asr_model.eval()

    print(f"loading translation model {TRANSLATION_MODEL_ID}...", file=sys.stderr)
    tx_tokenizer = AutoTokenizer.from_pretrained(TRANSLATION_MODEL_ID)
    tx_model = AutoModelForSeq2SeqLM.from_pretrained(
        TRANSLATION_MODEL_ID,
        torch_dtype=torch.float32,
    ).to(device)
    tx_model.eval()

    return asr_processor, asr_model, tx_tokenizer, tx_model


def transcribe_jp(audio_path: Path, processor, model, device: str) -> str:
    audio, sr = sf.read(str(audio_path))
    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if sr != TARGET_SAMPLE_RATE:
        audio = librosa.resample(audio, orig_sr=sr, target_sr=TARGET_SAMPLE_RATE)
    inputs = processor(
        audio, sampling_rate=TARGET_SAMPLE_RATE, return_tensors="pt"
    ).to(device)
    with torch.inference_mode():
        ids = model.generate(
            **inputs,
            language="ja",
            task="transcribe",
            max_new_tokens=256,
        )
    text = processor.batch_decode(ids, skip_special_tokens=True)[0].strip()
    return text


def translate_jp_to_en(jp_text: str, tokenizer, model, device: str) -> str:
    if not jp_text:
        return ""
    inputs = tokenizer(jp_text, return_tensors="pt", truncation=True).to(device)
    with torch.inference_mode():
        ids = model.generate(**inputs, max_new_tokens=256, num_beams=4)
    return tokenizer.batch_decode(ids, skip_special_tokens=True)[0].strip()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    ap.add_argument("source", type=Path, help="directory of .wav files to transcribe")
    ap.add_argument(
        "-o",
        "--output",
        type=Path,
        required=True,
        help="output CSV path",
    )
    ap.add_argument(
        "--device",
        default=None,
        help="torch device override (cuda, cpu); auto-detected by default",
    )
    args = ap.parse_args()

    if not args.source.is_dir():
        print(f"error: {args.source} is not a directory", file=sys.stderr)
        return 1

    device = args.device or ("cuda" if torch.cuda.is_available() else "cpu")
    asr_processor, asr_model, tx_tokenizer, tx_model = load_models(device)

    wavs = sorted(args.source.glob("*.wav"))
    if not wavs:
        print(f"no .wav files in {args.source}", file=sys.stderr)
        return 1

    source_dir_name = args.source.name
    args.output.parent.mkdir(parents=True, exist_ok=True)

    print(f"transcribing {len(wavs)} clips from {source_dir_name}...", file=sys.stderr)
    with args.output.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["source_dir", "filename", "transcript", "english", "note"])
        for i, wav in enumerate(wavs, 1):
            try:
                jp = transcribe_jp(wav, asr_processor, asr_model, device)
                en = translate_jp_to_en(jp, tx_tokenizer, tx_model, device)
            except Exception as ex:
                print(f"[{i}/{len(wavs)}] {wav.name}: ERROR {ex}", file=sys.stderr)
                jp, en = "", ""
            writer.writerow([source_dir_name, wav.name, jp, en, ""])
            f.flush()
            print(f"[{i}/{len(wavs)}] {wav.name}: {jp[:60]}", file=sys.stderr)

    print(f"wrote {args.output} ({len(wavs)} rows)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
