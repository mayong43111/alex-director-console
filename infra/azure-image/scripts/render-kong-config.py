#!/usr/bin/env python3
import json
import os
from pathlib import Path

username = os.environ.get("ALEX_INGRESS_USERNAME", "msuser")
password = os.environ.get("ALEX_INGRESS_PASSWORD")
if not password:
    raise SystemExit("ALEX_INGRESS_PASSWORD is required")

config = {
    "_format_version": "3.0",
    "services": [
        {
            "name": "alex-api",
            "url": "http://127.0.0.1:6275",
            "routes": [{"name": "alex-api", "paths": ["/api"], "strip_path": False}],
        },
        {
            "name": "alex-frontend",
            "url": "http://127.0.0.1:8080",
            "routes": [{"name": "alex-frontend", "paths": ["/"], "strip_path": False}],
        },
    ],
    "plugins": [{"name": "basic-auth", "config": {"hide_credentials": True}}],
    "consumers": [
        {
            "username": username,
            "basicauth_credentials": [{"username": username, "password": password}],
        }
    ],
}

output = Path("/opt/alex/config/kong.json")
output.write_text(json.dumps(config, ensure_ascii=True, indent=2), encoding="utf-8")
os.chmod(output, 0o600)
