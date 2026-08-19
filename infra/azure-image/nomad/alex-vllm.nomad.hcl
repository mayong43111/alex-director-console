job "alex-vllm" {
  datacenters = ["dc1"]
  type        = "service"

  group "vllm" {
    network {
      mode = "host"
      port "http" {
        static = 8000
      }
    }

    task "vllm" {
      driver = "raw_exec"
      user   = "alex"

      config {
        command = "/opt/alex/bin/vllm-launch.sh"
      }

      env {
        VLLM_MODEL = "/opt/alex-data/models/huggingface/qwen-3.8-27b"
        VLLM_PORT  = "8000"
        HF_HOME    = "/opt/alex-data/models/huggingface-cache"
      }

      service {
        name     = "vllm"
        port     = "http"
        provider = "consul"
        check {
          name     = "vllm-models"
          type     = "http"
          path     = "/v1/models"
          interval = "30s"
          timeout  = "10s"
        }
      }

      resources {
        cpu    = 4000
        memory = 32768
      }

      kill_timeout = "2m"
      shutdown_delay = "5s"
    }
  }
}
