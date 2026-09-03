from __future__ import annotations

import json
import os
import queue
import shutil
import subprocess
import threading
import uuid
import wave
from pathlib import Path
from typing import Any

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse


DATA_ROOT = Path(os.environ.get("VOICE_TRAINING_DATA_ROOT", "App_Data/voice-training")).resolve()
REFERENCE_ROOT = Path(os.environ.get("VOICE_REFERENCE_ROOT", "App_Data/reference-audio")).resolve()
REFERENCE_RUNTIME_ROOT = Path(os.environ.get("VOICE_REFERENCE_RUNTIME_ROOT", str(REFERENCE_ROOT)))
RUNNER = os.environ.get("VOICE_TRAINING_RUNNER", "")
MINIMUM_TRAINING_SECONDS = float(os.environ.get("VOICE_MINIMUM_TRAINING_SECONDS", "60"))
ALLOWED_STATUSES = {"queued", "running", "completed", "failed"}
work_queue: queue.Queue[str] = queue.Queue()
state_lock = threading.Lock()

app = FastAPI(title="Alex Voice Training Worker", version="1.0.0")


def job_directory(external_job_id: str) -> Path:
    return DATA_ROOT / external_job_id


def state_path(external_job_id: str) -> Path:
    return job_directory(external_job_id) / "state.json"


def read_state(external_job_id: str) -> dict[str, Any]:
    path = state_path(external_job_id)
    if not path.is_file():
        raise HTTPException(status_code=404, detail="Training job was not found.")
    return json.loads(path.read_text(encoding="utf-8"))


def write_state(external_job_id: str, **changes: Any) -> dict[str, Any]:
    with state_lock:
        path = state_path(external_job_id)
        state = json.loads(path.read_text(encoding="utf-8"))
        state.update(changes)
        temporary_path = path.with_suffix(".tmp")
        temporary_path.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
        temporary_path.replace(path)
        return state


def public_state(state: dict[str, Any]) -> dict[str, Any]:
    return {
        "externalJobId": state["externalJobId"],
        "status": state["status"],
        "progressPercent": state["progressPercent"],
        "gptWeightsPath": state.get("gptWeightsPath"),
        "soVitsWeightsPath": state.get("soVitsWeightsPath"),
        "error": state.get("error"),
    }


def validate_specification(value: Any) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise HTTPException(status_code=400, detail="specification must be a JSON object.")
    required = {"jobId", "name", "baseModelVersion", "language", "samples"}
    missing = sorted(required - value.keys())
    if missing:
        raise HTTPException(status_code=400, detail=f"Missing specification fields: {', '.join(missing)}")
    if value["baseModelVersion"] not in {"v1", "v2", "v3", "v4", "v2Pro", "v2ProPlus"}:
        raise HTTPException(status_code=400, detail="Unsupported baseModelVersion.")
    if not isinstance(value["samples"], list) or len(value["samples"]) < 3:
        raise HTTPException(status_code=400, detail="At least three samples are required.")
    return value


def validate_wav(path: Path) -> float:
    try:
        with wave.open(str(path), "rb") as audio:
            if audio.getnchannels() < 1 or audio.getsampwidth() != 2:
                raise ValueError("Only PCM16 WAV samples are supported.")
            return audio.getnframes() / audio.getframerate()
    except (wave.Error, EOFError, ValueError) as error:
        raise HTTPException(status_code=400, detail=f"Invalid WAV sample: {path.name}: {error}") from error


def execute_job(external_job_id: str) -> None:
    write_state(external_job_id, status="running", progressPercent=5, error=None)
    if not RUNNER:
        write_state(
            external_job_id,
            status="failed",
            progressPercent=5,
            error="VOICE_TRAINING_RUNNER is not configured.",
        )
        return
    directory = job_directory(external_job_id)
    result_path = directory / "result.json"
    try:
        subprocess.run(
            [RUNNER, str(directory / "specification.json"), str(directory), str(result_path)],
            cwd=directory,
            check=True,
        )
        result = json.loads(result_path.read_text(encoding="utf-8"))
        gpt_path = Path(result["gptWeightsPath"])
        sovits_path = Path(result["soVitsWeightsPath"])
        if gpt_path.suffix != ".ckpt" or not gpt_path.is_file():
            raise RuntimeError("Runner did not produce a GPT .ckpt file.")
        if sovits_path.suffix != ".pth" or not sovits_path.is_file():
            raise RuntimeError("Runner did not produce a SoVITS .pth file.")
        write_state(
            external_job_id,
            status="completed",
            progressPercent=100,
            gptWeightsPath=str(gpt_path.resolve()),
            soVitsWeightsPath=str(sovits_path.resolve()),
            error=None,
        )
    except Exception as error:
        write_state(external_job_id, status="failed", error=str(error))


def worker_loop() -> None:
    while True:
        external_job_id = work_queue.get()
        try:
            execute_job(external_job_id)
        finally:
            work_queue.task_done()


@app.on_event("startup")
def start_worker() -> None:
    DATA_ROOT.mkdir(parents=True, exist_ok=True)
    REFERENCE_ROOT.mkdir(parents=True, exist_ok=True)
    for path in DATA_ROOT.glob("*/state.json"):
        state = json.loads(path.read_text(encoding="utf-8"))
        if state.get("status") == "queued":
            work_queue.put(state["externalJobId"])
        elif state.get("status") == "running":
            write_state(
                state["externalJobId"],
                status="failed",
                error="Worker restarted while the training process was running.",
            )
    if not any(thread.name == "voice-training-queue" for thread in threading.enumerate()):
        threading.Thread(target=worker_loop, name="voice-training-queue", daemon=True).start()


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.put("/v1/reference-audio/{file_name}")
async def upload_reference_audio(file_name: str, request: Request) -> dict[str, str]:
    if not file_name.endswith(".wav") or not file_name.removesuffix(".wav").isalnum():
        raise HTTPException(status_code=400, detail="Reference file name must be alphanumeric and end in .wav.")
    content = await request.body()
    if not content or len(content) > 50 * 1024 * 1024:
        raise HTTPException(status_code=400, detail="Reference WAV must contain at most 50 MB.")
    temporary_path = REFERENCE_ROOT / f"{file_name}.tmp"
    destination = REFERENCE_ROOT / file_name
    temporary_path.write_bytes(content)
    try:
        validate_wav(temporary_path)
        temporary_path.replace(destination)
    except Exception:
        temporary_path.unlink(missing_ok=True)
        raise
    return {"runtimePath": str(REFERENCE_RUNTIME_ROOT / file_name)}


@app.post("/v1/training/jobs", status_code=202)
async def create_job(request: Request) -> JSONResponse:
    form = await request.form()
    specification_part = form.get("specification")
    if specification_part is None:
        raise HTTPException(status_code=400, detail="Missing specification part.")
    specification_text = (
        (await specification_part.read()).decode("utf-8")
        if hasattr(specification_part, "read")
        else str(specification_part)
    )
    try:
        specification = validate_specification(json.loads(specification_text))
    except json.JSONDecodeError as error:
        raise HTTPException(status_code=400, detail="specification is not valid JSON.") from error

    external_job_id = uuid.uuid4().hex
    directory = job_directory(external_job_id)
    samples_directory = directory / "samples"
    samples_directory.mkdir(parents=True)
    total_duration = 0.0
    try:
        for sample in specification["samples"]:
            field_name = f"sample-{uuid.UUID(sample['id']).hex}"
            upload = form.get(field_name)
            if upload is None or not hasattr(upload, "read"):
                raise HTTPException(status_code=400, detail=f"Missing sample part: {field_name}")
            destination = samples_directory / f"{uuid.UUID(sample['id']).hex}.wav"
            with destination.open("wb") as output:
                shutil.copyfileobj(upload.file, output)
            total_duration += validate_wav(destination)
            sample["localPath"] = str(destination.resolve())
        if total_duration < MINIMUM_TRAINING_SECONDS:
            raise HTTPException(
                status_code=400,
                detail=f"Samples must contain at least {MINIMUM_TRAINING_SECONDS:g} seconds of audio.",
            )
        specification["totalDurationSeconds"] = total_duration
        (directory / "specification.json").write_text(
            json.dumps(specification, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        state = {
            "externalJobId": external_job_id,
            "status": "queued",
            "progressPercent": 0,
            "gptWeightsPath": None,
            "soVitsWeightsPath": None,
            "error": None,
        }
        state_path(external_job_id).write_text(
            json.dumps(state, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        work_queue.put(external_job_id)
        return JSONResponse(public_state(state), status_code=202)
    except Exception:
        shutil.rmtree(directory, ignore_errors=True)
        raise


@app.get("/v1/training/jobs/{external_job_id}")
def get_job(external_job_id: str) -> dict[str, Any]:
    state = read_state(external_job_id)
    if state["status"] not in ALLOWED_STATUSES:
        raise HTTPException(status_code=500, detail="Persisted training status is invalid.")
    return public_state(state)