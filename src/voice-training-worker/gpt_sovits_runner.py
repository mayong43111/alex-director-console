#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

import yaml


PRETRAINED = {
    "v1": ("s2G488k.pth", "s2D488k.pth", "s1bert25hz-2kh-longer-epoch=68e-step=50232.ckpt"),
    "v2": (
        "gsv-v2final-pretrained/s2G2333k.pth",
        "gsv-v2final-pretrained/s2D2333k.pth",
        "gsv-v2final-pretrained/s1bert25hz-5kh-longer-epoch=12-step=369668.ckpt",
    ),
    "v2Pro": ("v2Pro/s2Gv2Pro.pth", "v2Pro/s2Dv2Pro.pth", "s1v3.ckpt"),
    "v2ProPlus": ("v2Pro/s2Gv2ProPlus.pth", "v2Pro/s2Dv2ProPlus.pth", "s1v3.ckpt"),
}


def write_json(path: Path, value: Any) -> None:
    temporary_path = path.with_suffix(path.suffix + ".tmp")
    temporary_path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary_path.replace(path)


def set_progress(job_directory: Path, progress: int) -> None:
    path = job_directory / "state.json"
    state = json.loads(path.read_text(encoding="utf-8"))
    state["progressPercent"] = progress
    write_json(path, state)


def run(root: Path, script: str, environment: dict[str, str], *arguments: str) -> None:
    child_environment = os.environ.copy()
    python_paths = (str(root), str(root / "GPT_SoVITS"))
    if child_environment.get("PYTHONPATH"):
        python_paths += (child_environment["PYTHONPATH"],)
    child_environment["PYTHONPATH"] = os.pathsep.join(python_paths)
    child_environment.update(environment)
    subprocess.run(
        [sys.executable, "-s", str(root / script), *arguments],
        cwd=root,
        env=child_environment,
        check=True,
    )


def combine_parts(directory: Path, target_name: str, part_name: str, header: str | None = None) -> None:
    part_path = directory / part_name
    lines = part_path.read_text(encoding="utf-8").strip().splitlines()
    part_path.unlink()
    output = ([header] if header else []) + lines
    (directory / target_name).write_text("\n".join(output) + "\n", encoding="utf-8")


def latest_file(directory: Path, pattern: str) -> Path:
    matches = sorted(directory.glob(pattern), key=lambda path: path.stat().st_mtime, reverse=True)
    if not matches:
        raise RuntimeError(f"Training did not create {pattern} in {directory}.")
    return matches[0]


def main() -> None:
    if len(sys.argv) != 4:
        raise SystemExit("Usage: gpt_sovits_runner.py SPECIFICATION JOB_DIRECTORY RESULT_JSON")
    specification_path = Path(sys.argv[1]).resolve()
    job_directory = Path(sys.argv[2]).resolve()
    result_path = Path(sys.argv[3]).resolve()
    root = Path(os.environ.get("GPT_SOVITS_ROOT", "")).resolve()
    if not (root / "GPT_SoVITS" / "s1_train.py").is_file():
        raise RuntimeError(f"GPT_SOVITS_ROOT is invalid: {root}")

    specification = json.loads(specification_path.read_text(encoding="utf-8"))
    version = specification["baseModelVersion"]
    if version not in PRETRAINED:
        raise RuntimeError(f"Training runner does not support {version}.")
    experiment_name = f"alex-{specification['jobId'].replace('-', '')}"
    experiment_directory = job_directory / "experiment"
    experiment_directory.mkdir(exist_ok=True)
    dataset_path = job_directory / "dataset.list"
    dataset_path.write_text(
        "\n".join(
            f"{sample['localPath']}|{experiment_name}|zh|{sample['transcript']}"
            for sample in specification["samples"]
        )
        + "\n",
        encoding="utf-8",
    )

    pretrained_root = root / "GPT_SoVITS" / "pretrained_models"
    sovits_generator, sovits_discriminator, gpt_model = (
        pretrained_root / relative_path for relative_path in PRETRAINED[version]
    )
    required_paths = [
        sovits_generator,
        sovits_discriminator,
        gpt_model,
        pretrained_root / "chinese-roberta-wwm-ext-large",
        pretrained_root / "chinese-hubert-base",
    ]
    missing = [str(path) for path in required_paths if not path.exists()]
    if missing:
        raise RuntimeError(f"Missing pretrained assets: {', '.join(missing)}")

    common_environment = {
        "inp_text": str(dataset_path),
        "inp_wav_dir": str(job_directory / "samples"),
        "exp_name": experiment_name,
        "opt_dir": str(experiment_directory),
        "i_part": "0",
        "all_parts": "1",
        "_CUDA_VISIBLE_DEVICES": "0",
        "is_half": "True",
        "version": version,
        "bert_pretrained_dir": str(pretrained_root / "chinese-roberta-wwm-ext-large"),
        "cnhubert_base_dir": str(pretrained_root / "chinese-hubert-base"),
        "pretrained_s2G": str(sovits_generator),
        "s2config_path": str(root / "GPT_SoVITS" / "configs" / ("s2.json" if version in {"v1", "v2"} else f"s2{version}.json")),
    }

    run(root, "GPT_SoVITS/prepare_datasets/1-get-text.py", common_environment)
    combine_parts(experiment_directory, "2-name2text.txt", "2-name2text-0.txt")
    set_progress(job_directory, 20)
    run(root, "GPT_SoVITS/prepare_datasets/2-get-hubert-wav32k.py", common_environment)
    if version in {"v2Pro", "v2ProPlus"}:
        common_environment["sv_path"] = str(pretrained_root / "sv" / "pretrained_eres2netv2w24s4ep4.ckpt")
        run(root, "GPT_SoVITS/prepare_datasets/2-get-sv.py", common_environment)
    set_progress(job_directory, 35)
    run(root, "GPT_SoVITS/prepare_datasets/3-get-semantic.py", common_environment)
    combine_parts(
        experiment_directory,
        "6-name2semantic.tsv",
        "6-name2semantic-0.tsv",
        "item_name\tsemantic_audio",
    )
    set_progress(job_directory, 50)

    sovits_output = job_directory / "SoVITS_weights"
    sovits_output.mkdir(exist_ok=True)
    sovits_config_name = "s2.json" if version in {"v1", "v2"} else f"s2{version}.json"
    sovits_config = json.loads((root / "GPT_SoVITS" / "configs" / sovits_config_name).read_text(encoding="utf-8"))
    sovits_config["train"].update(
        {
            "batch_size": 8,
            "epochs": 8,
            "pretrained_s2G": str(sovits_generator),
            "pretrained_s2D": str(sovits_discriminator),
            "if_save_latest": True,
            "if_save_every_weights": True,
            "save_every_epoch": 8,
            "gpu_numbers": "0",
        }
    )
    sovits_config["model"]["version"] = version
    sovits_config["data"]["exp_dir"] = str(experiment_directory)
    sovits_config["s2_ckpt_dir"] = str(experiment_directory / f"logs_s2_{version}")
    sovits_config["save_weight_dir"] = str(sovits_output)
    sovits_config["name"] = experiment_name
    sovits_config["version"] = version
    sovits_config_path = job_directory / "s2-config.json"
    write_json(sovits_config_path, sovits_config)
    run(root, "GPT_SoVITS/s2_train.py", {}, "--config", str(sovits_config_path))
    sovits_path = latest_file(sovits_output, "*.pth")
    set_progress(job_directory, 75)

    gpt_output = job_directory / "GPT_weights"
    gpt_output.mkdir(exist_ok=True)
    gpt_config = yaml.safe_load((root / "GPT_SoVITS" / ("configs/s1longer.yaml" if version == "v1" else "configs/s1longer-v2.yaml")).read_text(encoding="utf-8"))
    gpt_config["train"].update(
        {
            "batch_size": 8,
            "epochs": 15,
            "save_every_n_epoch": 15,
            "if_save_every_weights": True,
            "if_save_latest": True,
            "half_weights_save_dir": str(gpt_output),
            "exp_name": experiment_name,
        }
    )
    gpt_config["pretrained_s1"] = str(gpt_model)
    gpt_config["train_semantic_path"] = str(experiment_directory / "6-name2semantic.tsv")
    gpt_config["train_phoneme_path"] = str(experiment_directory / "2-name2text.txt")
    gpt_config["output_dir"] = str(experiment_directory / f"logs_s1_{version}")
    gpt_config_path = job_directory / "s1-config.yaml"
    gpt_config_path.write_text(yaml.safe_dump(gpt_config, allow_unicode=True), encoding="utf-8")
    run(root, "GPT_SoVITS/s1_train.py", {"_CUDA_VISIBLE_DEVICES": "0", "hz": "25hz"}, "--config_file", str(gpt_config_path))
    gpt_path = latest_file(gpt_output, "*.ckpt")
    set_progress(job_directory, 95)
    write_json(
        result_path,
        {"gptWeightsPath": str(gpt_path.resolve()), "soVitsWeightsPath": str(sovits_path.resolve())},
    )


if __name__ == "__main__":
    main()