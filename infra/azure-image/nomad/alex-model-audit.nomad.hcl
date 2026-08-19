job "alex-model-audit" {
  datacenters = ["dc1"]
  type        = "system"

  group "audit" {
    task "model-audit" {
      driver = "raw_exec"
      user   = "alex"

      config {
        command = "/opt/alex/bin/model-audit-loop.sh"
      }

      env {
        ALEX_MODEL_MANIFEST         = "/opt/alex/config/models.json"
        ALEX_MODEL_AUDIT_OUTPUT     = "/opt/alex-data/audit/model-audit.json"
        MODEL_AUDIT_INTERVAL_SECONDS = "300"
      }

      resources {
        cpu    = 100
        memory = 128
      }
    }
  }
}
