[CmdletBinding()]
param(
    [string]$SubscriptionId = "25741150-3c69-49ef-b192-934816cf1782",
    [string]$ResourceGroup = "rg-alex-image-validation",
    [string]$Location = "japaneast",
    [string]$VmName = "vm-alex-image-validation",
    [string]$VmSize = "Standard_D4s_v5",
    [string]$AdminUsername = "msuser"
)

$ErrorActionPreference = "Stop"
$template = Get-Content (Join-Path $PSScriptRoot "cloud-init.yaml.tmpl") -Raw
function ConvertTo-Base64([string]$Value) {
    return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Value))
}

$values = @{
    "__ALEX_INITIAL_USERNAME_B64__" = ConvertTo-Base64 $AdminUsername
    "__ALEX_INITIAL_PASSWORD_B64__" = ConvertTo-Base64 $(if ($env:ALEX_INITIAL_PASSWORD) { $env:ALEX_INITIAL_PASSWORD } else { "" })
    "__ALEX_INGRESS_USERNAME_B64__" = ConvertTo-Base64 $(if ($env:ALEX_INGRESS_USERNAME) { $env:ALEX_INGRESS_USERNAME } else { $AdminUsername })
    "__ALEX_INGRESS_PASSWORD_B64__" = ConvertTo-Base64 $(if ($env:ALEX_INGRESS_PASSWORD) { $env:ALEX_INGRESS_PASSWORD } else { "" })
}
foreach ($entry in $values.GetEnumerator()) {
    $template = $template.Replace($entry.Key, $entry.Value)
}
$cloudInit = Join-Path ([IO.Path]::GetTempPath()) "$VmName-cloud-init.yaml"
[IO.File]::WriteAllText($cloudInit, $template, [Text.UTF8Encoding]::new($false))

az group create --subscription $SubscriptionId --name $ResourceGroup --location $Location --output none
if ($LASTEXITCODE -ne 0) { throw "Azure resource group creation failed" }

az vm create `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $VmName `
    --location $Location `
    --size $VmSize `
    --image "microsoft-dsvm:ubuntu-2004:2004-gen2:latest" `
    --admin-username $AdminUsername `
    --generate-ssh-keys `
    --public-ip-sku Standard `
    --custom-data $cloudInit `
    --tags workload=alex-image-validation lifecycle=temporary `
    --output none
if ($LASTEXITCODE -ne 0) { throw "Azure VM creation failed" }

$publicIp = az vm show --subscription $SubscriptionId --resource-group $ResourceGroup --name $VmName --show-details --query publicIps --output tsv
$nicId = az vm show --subscription $SubscriptionId --resource-group $ResourceGroup --name $VmName --query "networkProfile.networkInterfaces[0].id" --output tsv
$nsgId = az network nic show --subscription $SubscriptionId --ids $nicId --query "networkSecurityGroup.id" --output tsv
$nsgName = Split-Path $nsgId -Leaf
$currentIp = $null
foreach ($service in @("https://checkip.amazonaws.com", "https://api.ip.sb/ip", "https://ifconfig.me/ip")) {
    try {
        $candidate = (Invoke-WebRequest -Uri $service -UseBasicParsing -TimeoutSec 15).Content.Trim()
        if ($candidate -match '^\d{1,3}(\.\d{1,3}){3}$') {
            $currentIp = $candidate
            break
        }
    }
    catch {
        Write-Verbose "Public IP lookup failed at ${service}: $_"
    }
}
if (-not $currentIp) { throw "Unable to determine the current public IPv4 address" }

az network nsg rule update --subscription $SubscriptionId --resource-group $ResourceGroup --nsg-name $nsgName --name default-allow-ssh --source-address-prefixes "$currentIp/32" --output none
if ($LASTEXITCODE -ne 0) { throw "SSH NSG restriction failed" }
az network nsg rule create --subscription $SubscriptionId --resource-group $ResourceGroup --nsg-name $nsgName --name allow-alex-https --priority 1010 --access Allow --protocol Tcp --direction Inbound --source-address-prefixes "$currentIp/32" --destination-port-ranges 8443 --output none
if ($LASTEXITCODE -ne 0) { throw "HTTPS NSG rule creation failed" }

[PSCustomObject]@{
    ResourceGroup = $ResourceGroup
    VmName = $VmName
    PublicIp = $publicIp
    AdminUsername = $AdminUsername
    CredentialsPath = "/var/lib/alex/initial-credentials"
}
