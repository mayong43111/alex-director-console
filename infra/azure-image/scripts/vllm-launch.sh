#!/usr/bin/env bash
set -euo pipefail

: "${VLLM_MODEL:?VLLM_MODEL must point to an installed model directory}"
exec /opt/alex/venvs/vllm/bin/vllm serve "$VLLM_MODEL" \
  --host 0.0.0.0 \
  --port "${VLLM_PORT:-8000}" \
  ${VLLM_EXTRA_ARGS:-}
