#!/opt/alex/venvs/model-tools/bin/python
import json
import os
import shutil
import subprocess
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

MANIFEST = Path(os.environ.get("ALEX_MODEL_MANIFEST", "/opt/alex/config/models.json"))
OUTPUT = Path(os.environ.get("ALEX_MODEL_AUDIT_OUTPUT", "/opt/alex-data/audit/model-audit.json"))
MODEL_EXTENSIONS = (".safetensors", ".ckpt", ".pt", ".pth", ".bin", ".gguf")


def command_status(command: list[str]) -> dict:
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=20, check=False)
        return {"available": True, "exitCode": result.returncode, "output": (result.stdout or result.stderr).strip()[-1000:]}
    except (FileNotFoundError, subprocess.TimeoutExpired) as error:
        return {"available": False, "exitCode": None, "output": str(error)}


def http_json(url: str) -> tuple[bool, object]:
    try:
        with urllib.request.urlopen(url, timeout=10) as response:
            return True, json.load(response)
    except Exception as error:
        return False, str(error)


def workflow_references(directories: list[str]) -> list[dict]:
    references: list[dict] = []
    seen: set[tuple[str, str]] = set()
    for directory in directories:
        root = Path(directory)
        if not root.exists():
            continue
        for workflow in root.rglob("*.json"):
            try:
                content = json.loads(workflow.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                continue
            stack = [content]
            while stack:
                value = stack.pop()
                if isinstance(value, dict):
                    stack.extend(value.values())
                elif isinstance(value, list):
                    stack.extend(value)
                elif isinstance(value, str) and value.lower().endswith(MODEL_EXTENSIONS):
                    key = (str(workflow), value)
                    if key not in seen:
                        seen.add(key)
                        references.append({"workflow": str(workflow), "model": value})
    return references


def main() -> int:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    root = Path(manifest["modelRoot"])
    model_results = []
    all_present = True
    for model in manifest["models"]:
        files = list(model.get("files", [])) + list(model.get("officialFiles", []))
        missing = [relative for relative in files if not (root / relative).is_file()]
        repository_environment = model.get("repositoryEnvironment")
        repository = model.get("repository") or (os.environ.get(repository_environment, "") if repository_environment else "")
        configured = bool(repository or model.get("officialRepository"))
        ready = configured and not missing
        all_present = all_present and (ready or not model.get("required", False))
        model_results.append({
            "id": model["id"],
            "displayName": model["displayName"],
            "runtime": model["runtime"],
            "repositoryConfigured": configured,
            "missingFiles": missing,
            "ready": ready,
        })

    references = workflow_references(manifest.get("workflowDirectories", []))
    installed_names = {path.name for path in root.rglob("*") if path.is_file()}
    workflow_missing = [item for item in references if Path(item["model"]).name not in installed_names]

    comfy_ok, object_info = http_json("http://127.0.0.1:8188/object_info")
    available_nodes = set(object_info) if comfy_ok and isinstance(object_info, dict) else set()
    missing_nodes = [node for node in manifest.get("requiredComfyUiNodes", []) if node not in available_nodes]

    report = {
        "checkedAtUtc": datetime.now(timezone.utc).isoformat(),
        "ready": all_present and not workflow_missing and comfy_ok and not missing_nodes,
        "gpu": command_status(["nvidia-smi", "-L"]),
        "services": {
            "docker": command_status(["systemctl", "is-active", "docker"]),
            "consul": command_status(["systemctl", "is-active", "consul"]),
            "nomad": command_status(["systemctl", "is-active", "nomad"]),
            "comfyUiObjectInfo": {"available": comfy_ok, "detail": None if comfy_ok else object_info},
        },
        "software": {
            "comfyUi": Path("/opt/comfyui/main.py").is_file(),
            "kreaOfficial": Path("/opt/krea-2/inference.py").is_file(),
            "vllm": Path("/opt/alex/venvs/vllm/bin/vllm").is_file(),
            "appApi": Path("/opt/alex/app/api/AlexDirectorConsole.V2.Api").is_file(),
            "appFrontend": Path("/opt/alex/app/frontend/index.html").is_file(),
        },
        "models": model_results,
        "workflowReferences": references,
        "missingWorkflowModels": workflow_missing,
        "missingComfyUiNodes": missing_nodes,
        "disk": shutil.disk_usage(root)._asdict() if root.exists() else None,
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    temporary = OUTPUT.with_suffix(".tmp")
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(OUTPUT)
    print(json.dumps(report, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
