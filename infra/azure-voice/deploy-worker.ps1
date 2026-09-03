param(
    [string]$ResourceGroup = 'RG-QWEN-LORA-JPE',
    [string]$VmName = 'vm-comfyui-a100-spot-jpe',
    [string]$SshUser = 'azureuser',
    [string]$SshKey = "$HOME/.ssh/id_rsa"
)

$ErrorActionPreference = 'Stop'
$vm = az vm show --resource-group $ResourceGroup --name $VmName -d -o json | ConvertFrom-Json
$hostName = $vm.publicIps
if (-not $hostName) { throw 'The voice VM does not have a public IP.' }
$repoRoot = Resolve-Path "$PSScriptRoot/../.."
$workerRoot = Join-Path $repoRoot 'src/voice-training-worker'

ssh -i $SshKey "$SshUser@$hostName" 'mkdir -p /opt/alex-data/alex-voice-worker'
scp -i $SshKey `
    (Join-Path $workerRoot 'app.py') `
    (Join-Path $workerRoot 'gpt_sovits_runner.py') `
    (Join-Path $workerRoot 'requirements.txt') `
    "$SshUser@${hostName}:/opt/alex-data/alex-voice-worker/"
scp -i $SshKey `
    (Join-Path $PSScriptRoot 'systemd/alex-voice-training.service') `
    (Join-Path $PSScriptRoot 'systemd/alex-gpt-sovits.service') `
    "$SshUser@${hostName}:/tmp/"
$remoteScript = @'
set -e
cat >/opt/alex-data/alex-voice-worker/run-training <<'EOF'
#!/bin/bash
exec /opt/alex-data/envs/gpt-sovits/bin/python /opt/alex-data/alex-voice-worker/gpt_sovits_runner.py "$@"
EOF
chmod +x /opt/alex-data/alex-voice-worker/run-training
/opt/alex-data/envs/gpt-sovits/bin/pip install -r /opt/alex-data/alex-voice-worker/requirements.txt
/opt/alex-data/envs/gpt-sovits/bin/pip install --force-reinstall --no-deps 'torchaudio==2.11.0+cu126' --index-url https://download.pytorch.org/whl/cu126
sudo install -m 0644 /tmp/alex-voice-training.service /etc/systemd/system/
sudo install -m 0644 /tmp/alex-gpt-sovits.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now alex-voice-training alex-gpt-sovits
curl --fail --silent --retry 30 --retry-connrefused --retry-delay 2 http://127.0.0.1:50010/health
curl --fail --silent --retry 60 --retry-connrefused --retry-delay 2 http://127.0.0.1:9880/docs >/dev/null
systemctl --no-pager --full status alex-voice-training alex-gpt-sovits | head -40
'@
$remoteScript = $remoteScript.Replace("`r", '')
$remoteScriptBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($remoteScript))
ssh -i $SshKey "$SshUser@$hostName" "echo '$remoteScriptBase64' | base64 --decode | bash"