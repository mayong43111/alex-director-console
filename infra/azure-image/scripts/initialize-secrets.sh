#!/usr/bin/env bash
set -euo pipefail

secrets_file=/etc/alex/secrets.env
initial_file=/var/lib/alex/initial-credentials
username="${ALEX_INITIAL_USERNAME:-msuser}"
password="${ALEX_INITIAL_PASSWORD:-}"
ingress_username="${ALEX_INGRESS_USERNAME:-$username}"
ingress_password="${ALEX_INGRESS_PASSWORD:-}"

random_password() {
  openssl rand -hex 18
}

credentials_exist=false
if [[ -s "$secrets_file" && -s "$initial_file" && -z "$password" && -z "$ingress_password" ]]; then
  credentials_exist=true
  username="$(sed -n 's/^username=//p' "$initial_file" | head -n 1)"
else
  [[ -n "$password" ]] || password="$(random_password)"
  [[ -n "$ingress_password" ]] || ingress_password="$password"
fi

if ! id "$username" >/dev/null 2>&1; then
  useradd --create-home --shell /bin/bash --groups sudo "$username"
fi
install -d -m 0700 /etc/alex /var/lib/alex
if [[ "$credentials_exist" == false ]]; then
  echo "$username:$password" | chpasswd
  printf 'ALEX_INGRESS_USERNAME=%q\nALEX_INGRESS_PASSWORD=%q\n' "$ingress_username" "$ingress_password" >"$secrets_file"
  printf 'username=%s\npassword=%s\ningress_username=%s\ningress_password=%s\n' "$username" "$password" "$ingress_username" "$ingress_password" >"$initial_file"
fi
chmod 0600 "$secrets_file" "$initial_file"

install -d -m 0755 /etc/ssh/sshd_config.d
printf 'PasswordAuthentication yes\n' >/etc/ssh/sshd_config.d/60-alex-password.conf
systemctl restart ssh || systemctl restart sshd

if [[ ! -s /opt/alex/tls/tls.crt || ! -s /opt/alex/tls/tls.key ]]; then
  certificate_hostname="$(hostname | cut -c1-63)"
  openssl req -x509 -newkey rsa:3072 -sha256 -days 825 -nodes \
    -subj "/CN=alex-director-console" \
    -addext "subjectAltName=DNS:alex-director-console,DNS:${certificate_hostname},DNS:localhost,IP:127.0.0.1" \
    -keyout /opt/alex/tls/tls.key -out /opt/alex/tls/tls.crt
  chmod 0600 /opt/alex/tls/tls.key
fi

set -a
# shellcheck disable=SC1090
source "$secrets_file"
set +a
/opt/alex/bin/render-kong-config.py
