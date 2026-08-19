#!/usr/bin/env bash
set -uo pipefail
interval="${MODEL_AUDIT_INTERVAL_SECONDS:-300}"
while true; do
  /opt/alex/bin/model-audit.py || true
  sleep "$interval"
done
