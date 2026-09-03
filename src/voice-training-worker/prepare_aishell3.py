#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import urllib.error
import urllib.request
import wave
from pathlib import Path


DATASET = "AISHELL/AISHELL-3"
REVISION = "f20d5db4a31fe779ef07bb1af4ea92da5c786622"
CONTENT_URLS = {
    split: f"https://huggingface.co/datasets/{DATASET}/resolve/main/{split}/content.txt"
    for split in ("train", "test")
}
PINYIN = re.compile(r"^[a-z]+[0-5]$", re.IGNORECASE)
SOURCES = (
    ("shen-zhaoli", "沈照璃", "SSB0780", "train"),
    ("qing-xing", "青杏", "SSB0693", "test"),
    ("pei-xuance", "裴玄策", "SSB0590", "train"),
)


def download(url: str, destination: Path) -> None:
    request = urllib.request.Request(url, headers={"User-Agent": "alex-director-console/voice-training"})
    with urllib.request.urlopen(request, timeout=120) as response, destination.open("wb") as output:
        output.write(response.read())


def transcripts(content: str, speaker: str) -> list[tuple[str, str]]:
    result = []
    for line in content.splitlines():
        if not line.startswith(speaker):
            continue
        parts = line.split(maxsplit=1)
        if len(parts) != 2:
            continue
        file_name, annotated_text = parts
        text = "".join(token for token in annotated_text.split() if not PINYIN.fullmatch(token))
        if text:
            result.append((file_name, text))
    return result


def duration_seconds(path: Path) -> float:
    with wave.open(str(path), "rb") as audio:
        if audio.getnchannels() != 1 or audio.getsampwidth() != 2:
            raise ValueError("AISHELL sample is not mono PCM16 WAV.")
        return audio.getnframes() / audio.getframerate()


def prepare_role(
    root: Path,
    slug: str,
    role_name: str,
    speaker: str,
    split: str,
    content: str,
    target_seconds: float,
) -> dict[str, object]:
    destination = root / slug
    destination.mkdir(parents=True, exist_ok=True)
    selected = []
    total_duration = 0.0
    for file_name, text in transcripts(content, speaker):
        url = (
            f"https://huggingface.co/datasets/{DATASET}/resolve/"
            f"{REVISION if split == 'train' else 'main'}/{split}/wav/{speaker}/{file_name}"
        )
        output_path = destination / file_name
        try:
            if not output_path.is_file():
                temporary_path = output_path.with_suffix(".tmp")
                download(url, temporary_path)
                temporary_path.replace(output_path)
            duration = duration_seconds(output_path)
        except (OSError, ValueError, urllib.error.HTTPError, urllib.error.URLError):
            output_path.unlink(missing_ok=True)
            output_path.with_suffix(".tmp").unlink(missing_ok=True)
            continue
        if duration < 2 or duration > 12:
            output_path.unlink(missing_ok=True)
            continue
        selected.append(
            {
                "fileName": file_name,
                "transcript": text,
                "durationSeconds": round(duration, 3),
                "sourceUrl": url,
            }
        )
        total_duration += duration
        if total_duration >= target_seconds:
            break
    if total_duration < target_seconds:
        raise RuntimeError(f"{speaker} only yielded {total_duration:.1f} seconds of valid audio.")
    manifest = {
        "roleName": role_name,
        "speaker": speaker,
        "dataset": "AISHELL-3 (SLR93)",
        "license": "Apache License 2.0",
        "usagePolicy": "practice-only",
        "canExport": False,
        "sampleCount": len(selected),
        "totalDurationSeconds": round(total_duration, 3),
        "samples": selected,
    }
    (destination / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--target-seconds", type=float, default=75)
    arguments = parser.parse_args()
    arguments.output.mkdir(parents=True, exist_ok=True)
    contents = {}
    for split, url in CONTENT_URLS.items():
        content_path = arguments.output / f"aishell3-{split}-content.txt"
        if not content_path.is_file():
            download(url, content_path)
        contents[split] = content_path.read_text(encoding="utf-8")
    manifests = [
        prepare_role(arguments.output, *source, contents[source[3]], arguments.target_seconds)
        for source in SOURCES
    ]
    print(json.dumps(manifests, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()