name       = "alex-node"
data_dir   = "/opt/nomad/data"
bind_addr  = "0.0.0.0"
datacenter = "dc1"

server {
  enabled          = true
  bootstrap_expect = 1
}

client {
  enabled = true

  options = {
    "driver.raw_exec.enable" = "1"
    "docker.volumes.enabled" = "true"
  }

  host_volume "alex-models" {
    path      = "/opt/alex-data/models"
    read_only = false
  }

  host_volume "alex-data" {
    path      = "/opt/alex-data/app"
    read_only = false
  }
}

plugin "docker" {
  config {
    allow_privileged = false
    volumes {
      enabled = true
    }
  }
}

consul {
  address = "127.0.0.1:8500"
}
