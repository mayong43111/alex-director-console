job "alex-comfyui" {
  datacenters = ["dc1"]
  type        = "service"

  group "comfyui" {
    network {
      mode = "host"
      port "http" {
        static = 8188
      }
    }

    task "comfyui" {
      driver = "raw_exec"
      user   = "alex"

      config {
        command = "/opt/alex/bin/comfyui-launch.sh"
      }

      env {
        COMFYUI_PORT = "8188"
        HF_HOME      = "/opt/alex-data/models/huggingface-cache"
      }

      service {
        name     = "comfyui"
        port     = "http"
        provider = "consul"
        check {
          name     = "comfyui-object-info"
          type     = "http"
          path     = "/object_info"
          interval = "30s"
          timeout  = "10s"
        }
      }

      resources {
        cpu    = 2000
        memory = 4096
      }

      kill_timeout = "30s"
      shutdown_delay = "5s"
    }
  }
}
