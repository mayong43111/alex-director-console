#!/usr/bin/env bash
set -euo pipefail

if [[ -f /etc/alex/model.env ]]; then
  set -a
  # shellcheck disable=SC1091
  source /etc/alex/model.env
  set +a
fi

exec /opt/alex/venvs/model-tools/bin/python /opt/alex/bin/model-download.py "$@"