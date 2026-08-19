#!/usr/bin/env bash
set -euo pipefail

BUNDLE_ROOT="${1:-/tmp/alex-image}"
systemctl stop nomad 2>/dev/null || true

replace_directory() {
  local source_directory="$1"
  local target_directory="$2"
  local staged_directory="${target_directory}.new"
  local previous_directory="${target_directory}.old"
  rm -rf "$staged_directory" "$previous_directory"
  install -d "$staged_directory"
  cp -a "$source_directory/." "$staged_directory/"
  if [[ -d "$target_directory" ]]; then
    mv "$target_directory" "$previous_directory"
  fi
  mv "$staged_directory" "$target_directory"
  rm -rf "$previous_directory"
}

install -d /etc/nomad.d /etc/consul.d /etc/alex /opt/alex/config /opt/alex/bin /opt/alex/jobs /opt/alex/tls
install -m 0644 "$BUNDLE_ROOT/config/nomad.hcl" /etc/nomad.d/alex.hcl
install -m 0644 "$BUNDLE_ROOT/config/consul.hcl" /etc/consul.d/alex.hcl
install -m 0644 "$BUNDLE_ROOT/config/models.json" /opt/alex/config/models.json
install -m 0644 "$BUNDLE_ROOT/config/nginx.conf" /opt/alex/config/nginx.conf
install -m 0644 "$BUNDLE_ROOT/config/extra_model_paths.yaml" /opt/comfyui/extra_model_paths.yaml
install -m 0755 "$BUNDLE_ROOT/scripts/"*.sh /opt/alex/bin/
install -m 0755 "$BUNDLE_ROOT/scripts/"*.py /opt/alex/bin/
sed -i 's/\r$//' /opt/alex/bin/*.sh /opt/alex/bin/*.py
install -m 0644 "$BUNDLE_ROOT/nomad/"*.nomad.hcl /opt/alex/jobs/

if [[ -d "$BUNDLE_ROOT/app/api" ]]; then
  replace_directory "$BUNDLE_ROOT/app/api" /opt/alex/app/api
fi
if [[ -d "$BUNDLE_ROOT/app/frontend" ]]; then
  replace_directory "$BUNDLE_ROOT/app/frontend" /opt/alex/app/frontend
fi
if [[ -d "$BUNDLE_ROOT/app/workflows" ]]; then
  replace_directory "$BUNDLE_ROOT/app/workflows" /opt/alex/app/workflows
fi

chmod 0755 /opt/alex/app/api/AlexDirectorConsole.V2.Api
chown -R alex:alex /opt/alex/app /opt/alex/config /opt/alex/tls /opt/alex-data /opt/comfyui/extra_model_paths.yaml
chmod 0750 /etc/alex
systemctl daemon-reload
systemctl restart docker consul nomad
