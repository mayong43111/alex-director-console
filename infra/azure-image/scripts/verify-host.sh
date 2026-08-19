#!/usr/bin/env bash
set -euo pipefail

printf '%-24s %s\n' "docker" "$(docker --version)"
printf '%-24s %s\n' "consul" "$(consul version | head -n 1)"
printf '%-24s %s\n' "nomad" "$(nomad version | head -n 1)"
printf '%-24s %s\n' "comfyui" "$(test -f /opt/comfyui/main.py && echo installed || echo missing)"
printf '%-24s %s\n' "krea-2" "$(test -f /opt/krea-2/inference.py && echo installed || echo missing)"
printf '%-24s %s\n' "vllm" "$(/opt/alex/venvs/vllm/bin/python -c 'import vllm; print(vllm.__version__)')"
printf '%-24s %s\n' "gpu" "$(nvidia-smi -L 2>&1 || true)"
printf '%-24s %s\n' "api" "$(curl -ksS -o /dev/null -w '%{http_code}' http://127.0.0.1:6275/api/v2/projects || true)"
printf '%-24s %s\n' "frontend" "$(curl -ksS -o /dev/null -w '%{http_code}' http://127.0.0.1:8080/ || true)"
printf '%-24s %s\n' "comfyui-http" "$(curl -ksS -o /dev/null -w '%{http_code}' http://127.0.0.1:8188/object_info || true)"
printf '%-24s %s\n' "kong-https" "$(curl -ksS -o /dev/null -w '%{http_code}' https://127.0.0.1:8443/ || true)"
nomad job status
/opt/alex/bin/model-audit.py >/dev/null
jq '{ready,gpu,software,models,missingComfyUiNodes,missingWorkflowModels}' /opt/alex-data/audit/model-audit.json
