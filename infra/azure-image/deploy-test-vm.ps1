[CmdletBinding()]
param(
    [string]$SubscriptionId = "25741150-3c69-49ef-b192-934816cf1782",
    [string]$ResourceGroup = "rg-alex-image-validation",
    [string]$VmName = "vm-alex-image-validation",
    [string]$AdminUsername = "msuser",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$bundlePath = Join-Path $PSScriptRoot "dist"
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build-bundle.ps1") -BundlePath $bundlePath
}
if (-not (Test-Path $bundlePath)) { throw "Bundle does not exist: $bundlePath" }

$publicIp = az vm show --subscription $SubscriptionId --resource-group $ResourceGroup --name $VmName --show-details --query publicIps --output tsv
if ($LASTEXITCODE -ne 0 -or -not $publicIp) { throw "Unable to resolve VM public IP" }

$archive = Join-Path ([IO.Path]::GetTempPath()) "alex-image-bundle.tar.gz"
$archiveRoot = Join-Path ([IO.Path]::GetTempPath()) "alex-image-bundle"
if (Test-Path $archiveRoot) { Remove-Item $archiveRoot -Recurse -Force }
New-Item $archiveRoot -ItemType Directory | Out-Null
Copy-Item (Join-Path $bundlePath "*") $archiveRoot -Recurse -Force
tar -czf $archive -C $archiveRoot .
if ($LASTEXITCODE -ne 0) { throw "Bundle archive creation failed" }

for ($attempt = 1; $attempt -le 60; $attempt++) {
    ssh -o BatchMode=yes -o ConnectTimeout=5 -o StrictHostKeyChecking=accept-new "$AdminUsername@$publicIp" "cloud-init status --wait >/dev/null"
    if ($LASTEXITCODE -eq 0) { break }
    if ($attempt -eq 60) { throw "VM SSH/cloud-init did not become ready" }
}

scp $archive "${AdminUsername}@${publicIp}:/tmp/alex-image-bundle.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "Bundle upload failed" }

$remoteCommand = "sudo rm -rf /tmp/alex-image && sudo install -d /tmp/alex-image && sudo tar -xzf /tmp/alex-image-bundle.tar.gz -C /tmp/alex-image && sudo bash /tmp/alex-image/scripts/install-runtime.sh && sudo bash /tmp/alex-image/scripts/configure-host.sh /tmp/alex-image && sudo bash /opt/alex/bin/bootstrap-jobs.sh"
ssh -o BatchMode=yes "$AdminUsername@$publicIp" $remoteCommand
if ($LASTEXITCODE -ne 0) { throw "Remote installation failed" }

Write-Output "Deployment completed on $publicIp. Run sudo /opt/alex/bin/verify-host.sh over SSH for detailed verification."
