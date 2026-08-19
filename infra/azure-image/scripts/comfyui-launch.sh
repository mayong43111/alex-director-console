#!/usr/bin/env bash
set -euo pipefail

args=(--listen 0.0.0.0 --port "${COMFYUI_PORT:-8188}" --disable-auto-launch)
if ! command -v nvidia-smi >/dev/null 2>&1 || ! nvidia-smi -L >/dev/null 2>&1; then
  args+=(--cpu)
fi
cd /opt/comfyui
exec /opt/comfyui/.venv/bin/python main.py "${args[@]}" ${COMFYUI_EXTRA_ARGS:-}
