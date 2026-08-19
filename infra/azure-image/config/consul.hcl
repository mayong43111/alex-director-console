datacenter       = "dc1"
data_dir         = "/opt/consul"
server           = true
bootstrap_expect = 1
bind_addr        = "{{ GetPrivateIP }}"
client_addr      = "0.0.0.0"
ui_config {
  enabled = true
}
connect {
  enabled = true
}
