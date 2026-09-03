param(
    [string]$ResourceGroup = 'RG-QWEN-LORA-JPE',
    [string]$VmName = 'vm-comfyui-a100-spot-jpe',
    [string]$SshUser = 'azureuser',
    [string]$SshKey = "$HOME/.ssh/id_rsa",
    [int]$MaxReconnectAttempts = 3
)

$ErrorActionPreference = 'Stop'
$vm = az vm show --resource-group $ResourceGroup --name $VmName -d -o json | ConvertFrom-Json
if (-not $vm.publicIps) { throw 'The voice VM does not have a public IP.' }
for ($attempt = 1; $attempt -le $MaxReconnectAttempts; $attempt++) {
    ssh -i $SshKey -N `
        -o BatchMode=yes `
        -o ConnectTimeout=15 `
        -o ConnectionAttempts=1 `
        -o ExitOnForwardFailure=yes `
        -o ServerAliveInterval=15 `
        -o ServerAliveCountMax=2 `
        -L 50010:127.0.0.1:50010 `
        -L 9880:127.0.0.1:9880 `
        "$SshUser@$($vm.publicIps)"
    if ($LASTEXITCODE -eq 0 -or $attempt -eq $MaxReconnectAttempts) { exit $LASTEXITCODE }
    Write-Warning "Voice tunnel disconnected; reconnecting ($attempt/$MaxReconnectAttempts)."
}