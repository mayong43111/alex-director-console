job "alex-kong" {
  datacenters = ["dc1"]
  type        = "service"

  group "ingress" {
    network {
      mode = "host"
      port "https" {
        static = 8443
      }
    }

    task "kong" {
      driver = "docker"
      user   = "root"

      config {
        image        = "kong:3.9"
        network_mode = "host"
        volumes = [
          "/opt/alex/config/kong.json:/etc/kong/kong.json:ro",
          "/opt/alex/tls:/etc/kong/tls:ro"
        ]
      }

      env {
        KONG_DATABASE           = "off"
        KONG_DECLARATIVE_CONFIG = "/etc/kong/kong.json"
        KONG_PROXY_LISTEN       = "0.0.0.0:8443 ssl"
        KONG_ADMIN_LISTEN       = "off"
        KONG_STATUS_LISTEN      = "127.0.0.1:8100"
        KONG_SSL_CERT           = "/etc/kong/tls/tls.crt"
        KONG_SSL_CERT_KEY       = "/etc/kong/tls/tls.key"
      }

      service {
        name     = "alex-ingress"
        port     = "https"
        provider = "consul"
        check {
          name           = "kong-https"
          type           = "tcp"
          interval       = "15s"
          timeout        = "5s"
        }
      }

      resources {
        cpu    = 500
        memory = 1024
      }

      shutdown_delay = "5s"
    }
  }
}
