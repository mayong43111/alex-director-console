import io
import json
import wave

from fastapi.testclient import TestClient

import app


def wav_bytes(seconds: int) -> bytes:
    output = io.BytesIO()
    with wave.open(output, "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(16000)
        audio.writeframes(b"\0\0" * 16000 * seconds)
    return output.getvalue()


def test_create_and_get_job(tmp_path, monkeypatch):
    monkeypatch.setattr(app, "DATA_ROOT", tmp_path)
    monkeypatch.setattr(app, "RUNNER", "")
    samples = [
        {"id": "11111111-1111-1111-1111-111111111111", "fileName": "one.wav", "transcript": "第一条。"},
        {"id": "22222222-2222-2222-2222-222222222222", "fileName": "two.wav", "transcript": "第二条。"},
        {"id": "33333333-3333-3333-3333-333333333333", "fileName": "three.wav", "transcript": "第三条。"},
    ]
    specification = {
        "jobId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "name": "contract-test",
        "baseModelVersion": "v2",
        "language": "zh",
        "dialect": "普通话",
        "speakingStyle": "自然",
        "defaultSpeed": 1.0,
        "usagePolicy": "practice-only",
        "samples": samples,
    }
    files = {"specification": (None, json.dumps(specification), "application/json")}
    for sample in samples:
        files[f"sample-{sample['id'].replace('-', '')}"] = (
            sample["fileName"],
            wav_bytes(20),
            "audio/wav",
        )

    with TestClient(app.app) as client:
        response = client.post("/v1/training/jobs", files=files)
        assert response.status_code == 202
        created = response.json()
        assert created["status"] == "queued"
        queried = client.get(f"/v1/training/jobs/{created['externalJobId']}")
        assert queried.status_code == 200
        assert queried.json()["externalJobId"] == created["externalJobId"]
        persisted = json.loads(
            (tmp_path / created["externalJobId"] / "specification.json").read_text(encoding="utf-8")
        )
        assert persisted["totalDurationSeconds"] == 60
        assert all(item["localPath"].endswith(".wav") for item in persisted["samples"])


def test_rejects_short_dataset(tmp_path, monkeypatch):
    monkeypatch.setattr(app, "DATA_ROOT", tmp_path)
    samples = [
        {"id": f"00000000-0000-0000-0000-00000000000{index}", "fileName": f"{index}.wav", "transcript": "文本"}
        for index in range(1, 4)
    ]
    specification = {
        "jobId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "name": "short",
        "baseModelVersion": "v2",
        "language": "zh",
        "samples": samples,
    }
    files = {"specification": (None, json.dumps(specification), "application/json")}
    for sample in samples:
        files[f"sample-{sample['id'].replace('-', '')}"] = (sample["fileName"], wav_bytes(1), "audio/wav")

    with TestClient(app.app) as client:
        response = client.post("/v1/training/jobs", files=files)
    assert response.status_code == 400
    assert "60 seconds" in response.json()["detail"]


def test_upload_reference_audio(tmp_path, monkeypatch):
    monkeypatch.setattr(app, "REFERENCE_ROOT", tmp_path)
    monkeypatch.setattr(app, "REFERENCE_RUNTIME_ROOT", tmp_path / "runtime")

    with TestClient(app.app) as client:
        response = client.put(
            "/v1/reference-audio/11111111111111111111111111111111.wav",
            content=wav_bytes(1),
            headers={"Content-Type": "audio/wav"},
        )

    assert response.status_code == 200
    assert response.json()["runtimePath"].endswith("runtime\\11111111111111111111111111111111.wav")
    assert (tmp_path / "11111111111111111111111111111111.wav").read_bytes().startswith(b"RIFF")