job "alex-model-download" {
  datacenters = ["dc1"]
  type        = "batch"

  periodic {
    crons            = ["0 3 * * *"]
    prohibit_overlap = true
    time_zone        = "UTC"
  }

  group "download" {
    task "download-models" {
      driver = "raw_exec"
      user   = "root"

      config {
        command = "/opt/alex/bin/model-download.sh"
      }

      env {
        ALEX_MODEL_MANIFEST = "/opt/alex/config/models.json"
        HF_HOME             = "/opt/alex-data/models/huggingface-cache"
      }

      resources {
        cpu    = 1000
        memory = 2048
      }
    }
  }
}
