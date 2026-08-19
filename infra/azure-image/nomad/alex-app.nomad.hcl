job "alex-app" {
  datacenters = ["dc1"]
  type        = "service"

  group "app" {
    network {
      mode = "host"
      port "api" {
        static = 6275
      }
      port "frontend" {
        static = 8080
      }
    }

    task "api" {
      driver = "raw_exec"
      user   = "alex"

      config {
        command = "/opt/alex/app/api/AlexDirectorConsole.V2.Api"
      }

      env {
        ASPNETCORE_ENVIRONMENT                 = "Production"
        ASPNETCORE_URLS                        = "http://0.0.0.0:6275"
        ConnectionStrings__V2Database          = "Data Source=/opt/alex-data/app/alex-director-v2.db"
        LocalTts__BaseUrl                       = "http://127.0.0.1:8010"
        Logging__LogLevel__Microsoft            = "Warning"
      }

      service {
        name     = "alex-api"
        port     = "api"
        provider = "consul"
        check {
          name     = "projects-api"
          type     = "http"
          path     = "/api/v2/projects"
          interval = "15s"
          timeout  = "5s"
        }
      }

      resources {
        cpu    = 1000
        memory = 2048
      }

      shutdown_delay = "5s"
    }

    task "frontend" {
      driver = "docker"

      config {
        image        = "nginx:1.27-alpine"
        network_mode = "host"
        volumes = [
          "/opt/alex/app/frontend:/usr/share/nginx/html:ro",
          "/opt/alex/config/nginx.conf:/etc/nginx/nginx.conf:ro"
        ]
      }

      service {
        name     = "alex-frontend"
        port     = "frontend"
        provider = "consul"
        check {
          name     = "frontend-http"
          type     = "http"
          path     = "/"
          interval = "15s"
          timeout  = "5s"
        }
      }

      resources {
        cpu    = 200
        memory = 256
      }

      shutdown_delay = "5s"
    }
  }
}
