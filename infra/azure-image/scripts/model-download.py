#!/usr/bin/env python3
import argparse
import json
import os
import shutil
from pathlib import Path

from huggingface_hub import hf_hub_download, snapshot_download

MANIFEST = Path(os.environ.get("ALEX_MODEL_MANIFEST", "/opt/alex/config/models.json"))


def download_file(repository: str, relative: str, destination: Path, token: str | None) -> None:
    candidates = [relative, f"split_files/{relative}"]
    last_error: Exception | None = None
    for candidate in candidates:
        try:
            cached = hf_hub_download(repo_id=repository, filename=candidate, token=token)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(cached, destination)
            return
        except Exception as error:
            last_error = error
    raise RuntimeError(f"{repository} does not provide {relative}: {last_error}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--model", action="append", dest="models")
    arguments = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    root = Path(manifest["modelRoot"])
    token = os.environ.get("HF_TOKEN") or None
    failures: list[str] = []

    for model in manifest["models"]:
        if arguments.models and model["id"] not in arguments.models:
            continue
        repository_environment = model.get("repositoryEnvironment")
        repository = model.get("repository") or (os.environ.get(repository_environment, "") if repository_environment else "")
        additional = model.get("additionalRepositories", {})
        for relative in model.get("files", []):
            destination = root / relative
            if destination.is_file():
                continue
            source_repository = additional.get(relative, repository)
            if not source_repository:
                failures.append(f"{model['id']}: set {repository_environment} before downloading {relative}")
                continue
            if arguments.dry_run:
                print(f"would download {model['id']}: {source_repository}/{relative} -> {destination}")
                continue
            try:
                download_file(source_repository, relative, destination, token)
                print(f"downloaded {model['id']}: {relative}")
            except Exception as error:
                failures.append(str(error))

        official_repository = model.get("officialRepository")
        for relative in model.get("officialFiles", []):
            destination = root / relative
            if destination.is_file():
                continue
            if not official_repository:
                failures.append(f"{model['id']}: official repository is not configured")
                continue
            if arguments.dry_run:
                print(f"would download {model['id']} official: {official_repository}/{Path(relative).name} -> {destination}")
                continue
            try:
                remote_name = Path(relative).name
                download_file(official_repository, remote_name, destination, token)
                print(f"downloaded {model['id']} official: {remote_name}")
            except Exception as error:
                failures.append(str(error))

    if failures:
        print("download completed with unresolved items:")
        for failure in failures:
            print(f"- {failure}")
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
