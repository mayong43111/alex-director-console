#!/usr/bin/env bash
set -euo pipefail

/opt/alex/bin/initialize-secrets.sh
systemctl restart consul nomad

for attempt in $(seq 1 60); do
  if nomad node status >/dev/null 2>&1; then
    break
  fi
  if [[ "$attempt" -eq 60 ]]; then
    echo "Nomad node did not become ready" >&2
    exit 1
  fi
  sleep 2
done

nomad job run /opt/alex/jobs/alex-app.nomad.hcl
nomad job run /opt/alex/jobs/alex-comfyui.nomad.hcl
nomad job run /opt/alex/jobs/alex-model-audit.nomad.hcl
nomad job run /opt/alex/jobs/alex-model-download.nomad.hcl
nomad job run /opt/alex/jobs/alex-kong.nomad.hcl

# vLLM is installed and its job is validated but intentionally not submitted.
nomad job validate /opt/alex/jobs/alex-vllm.nomad.hcl
