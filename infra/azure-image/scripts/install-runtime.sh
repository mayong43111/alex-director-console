#!/usr/bin/env bash
set -euo pipefail

export DEBIAN_FRONTEND=noninteractive
COMFYUI_COMMIT="${COMFYUI_COMMIT:-c67885b14556cf3e4e061862925282d403d09862}"
KREA2_COMMIT="${KREA2_COMMIT:-db3984fbc6e13b34c0064990fc2d95ac64d00058}"
VLLM_VERSION="${VLLM_VERSION:-0.10.2}"
PYTORCH_INDEX_URL="${PYTORCH_INDEX_URL:-https://download.pytorch.org/whl/cu128}"
NOMAD_VERSION="${NOMAD_VERSION:-2.0.5}"
CONSUL_VERSION="${CONSUL_VERSION:-2.0.3}"
export UV_PYTHON_INSTALL_DIR="${UV_PYTHON_INSTALL_DIR:-/opt/uv-python}"
APT_GET=(apt-get -o DPkg::Lock::Timeout=600)

install_hashicorp_binary() {
  local product="$1"
  local version="$2"
  local architecture
  case "$(dpkg --print-architecture)" in
    amd64) architecture=amd64 ;;
    arm64) architecture=arm64 ;;
    *) echo "Unsupported architecture: $(dpkg --print-architecture)" >&2; return 1 ;;
  esac
  local archive="${product}_${version}_linux_${architecture}.zip"
  local temporary_directory
  temporary_directory="$(mktemp -d)"
  curl -fsSL "https://releases.hashicorp.com/${product}/${version}/${archive}" -o "$temporary_directory/$archive"
  curl -fsSL "https://releases.hashicorp.com/${product}/${version}/${product}_${version}_SHA256SUMS" -o "$temporary_directory/SHA256SUMS"
  (
    cd "$temporary_directory"
    grep "  ${archive}$" SHA256SUMS | sha256sum --check --strict
    unzip -o "$archive" "$product"
    install -m 0755 "$product" "/usr/local/bin/$product"
  )
  rm -rf "$temporary_directory"
}

"${APT_GET[@]}" update
"${APT_GET[@]}" install -y --no-install-recommends ca-certificates curl gpg git jq openssl unzip
if ! command -v docker >/dev/null 2>&1; then
  "${APT_GET[@]}" install -y --no-install-recommends docker.io
fi
"${APT_GET[@]}" remove -y nomad consul || true
rm -f /etc/apt/sources.list.d/hashicorp.list
systemctl unmask nomad.service consul.service || true
rm -f /etc/systemd/system/nomad.service /etc/systemd/system/consul.service
install_hashicorp_binary nomad "$NOMAD_VERSION"
install_hashicorp_binary consul "$CONSUL_VERSION"

curl -LsSf https://astral.sh/uv/install.sh | env UV_INSTALL_DIR=/usr/local/bin sh
install -d -m 0755 "$UV_PYTHON_INSTALL_DIR"
uv python install 3.12

id -u alex >/dev/null 2>&1 || useradd --system --create-home --home-dir /opt/alex --shell /usr/sbin/nologin alex
id -u nomad >/dev/null 2>&1 || useradd --system --home-dir /opt/nomad --shell /usr/sbin/nologin nomad
id -u consul >/dev/null 2>&1 || useradd --system --home-dir /opt/consul --shell /usr/sbin/nologin consul
install -d -o alex -g alex /opt/alex/app/api /opt/alex/app/frontend /opt/alex/app/workflows /opt/alex/bin /opt/alex/config /opt/alex/tls
install -d -o alex -g alex /opt/alex-data/app /opt/alex-data/models/comfyui/{checkpoints,text_encoders,diffusion_models,unet,loras,vae,controlnet} /opt/alex-data/models/huggingface /opt/alex-data/models/krea-official /opt/alex-data/audit
install -d -o nomad -g nomad /opt/nomad/data
install -d -o consul -g consul /opt/consul

cat >/etc/systemd/system/nomad.service <<'EOF'
[Unit]
Description=Nomad
Documentation=https://developer.hashicorp.com/nomad/docs
After=network-online.target consul.service
Wants=network-online.target

[Service]
ExecStart=/usr/local/bin/nomad agent -config=/etc/nomad.d
ExecReload=/bin/kill -HUP $MAINPID
KillMode=process
KillSignal=SIGINT
LimitNOFILE=65536
Restart=on-failure
RestartSec=2
TasksMax=infinity
OOMScoreAdjust=-1000

[Install]
WantedBy=multi-user.target
EOF

cat >/etc/systemd/system/consul.service <<'EOF'
[Unit]
Description=Consul
Documentation=https://developer.hashicorp.com/consul/docs
After=network-online.target
Wants=network-online.target

[Service]
User=consul
Group=consul
ExecStart=/usr/local/bin/consul agent -config-dir=/etc/consul.d
ExecReload=/bin/kill -HUP $MAINPID
KillSignal=SIGINT
LimitNOFILE=65536
Restart=on-failure
RestartSec=2

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload

git config --global --get-all safe.directory | grep -Fxq /opt/comfyui || git config --global --add safe.directory /opt/comfyui
git config --global --get-all safe.directory | grep -Fxq /opt/krea-2 || git config --global --add safe.directory /opt/krea-2
if [[ ! -d /opt/comfyui/.git ]]; then
  git clone https://github.com/comfyanonymous/ComfyUI.git /opt/comfyui
fi
git -C /opt/comfyui fetch --depth 1 origin "$COMFYUI_COMMIT"
git -C /opt/comfyui checkout --detach "$COMFYUI_COMMIT"
if ! runuser -u alex -- test -x /opt/comfyui/.venv/bin/python; then
  rm -rf /opt/comfyui/.venv
  uv venv --python 3.12 /opt/comfyui/.venv
fi
uv pip install --python /opt/comfyui/.venv/bin/python torch torchvision torchaudio --index-url "$PYTORCH_INDEX_URL"
uv pip install --python /opt/comfyui/.venv/bin/python -r /opt/comfyui/requirements.txt
chown -R alex:alex /opt/comfyui

if [[ ! -d /opt/krea-2/.git ]]; then
  git clone https://github.com/krea-ai/krea-2.git /opt/krea-2
fi
git -C /opt/krea-2 fetch --depth 1 origin "$KREA2_COMMIT"
git -C /opt/krea-2 checkout --detach "$KREA2_COMMIT"
cd /opt/krea-2
if ! runuser -u alex -- test -x /opt/krea-2/.venv/bin/python; then
  rm -rf /opt/krea-2/.venv
fi
uv sync --frozen
chown -R alex:alex /opt/krea-2

if ! runuser -u alex -- test -x /opt/alex/venvs/vllm/bin/python; then
  rm -rf /opt/alex/venvs/vllm
  uv venv --python 3.12 /opt/alex/venvs/vllm
fi
uv pip install --python /opt/alex/venvs/vllm/bin/python "vllm==$VLLM_VERSION"
if ! runuser -u alex -- test -x /opt/alex/venvs/model-tools/bin/python; then
  rm -rf /opt/alex/venvs/model-tools
  uv venv --python 3.12 /opt/alex/venvs/model-tools
fi
uv pip install --python /opt/alex/venvs/model-tools/bin/python huggingface_hub
chown -R alex:alex /opt/alex/venvs /opt/uv-python

systemctl enable docker consul nomad
"${APT_GET[@]}" clean
rm -rf /var/lib/apt/lists/* /root/.cache
