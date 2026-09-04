[CmdletBinding()]
param(
    [string]$BundlePath = (Join-Path $PSScriptRoot "dist")
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$apiProject = Join-Path $repositoryRoot "src/backend/AlexDirectorConsole.V2.Api/AlexDirectorConsole.V2.Api.csproj"
$frontendRoot = Join-Path $repositoryRoot "src/frontend"
$workflowPath = Join-Path $repositoryRoot "src/backend/AlexDirectorConsole.V2.Api/Skills/video-generation/workflows/minimax-h3-fl2va-api.json"

if (Test-Path $BundlePath) {
    Remove-Item $BundlePath -Recurse -Force
}
New-Item $BundlePath -ItemType Directory -Force | Out-Null

$apiOutput = Join-Path $BundlePath "app/api"
$frontendOutput = Join-Path $BundlePath "app/frontend"
$workflowOutput = Join-Path $BundlePath "app/workflows"
New-Item $apiOutput, $frontendOutput, $workflowOutput -ItemType Directory -Force | Out-Null

dotnet publish $apiProject -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false -o $apiOutput
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Push-Location $frontendRoot
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "frontend build failed" }
    Copy-Item "dist/*" $frontendOutput -Recurse -Force
}
finally {
    Pop-Location
}

Copy-Item $workflowPath $workflowOutput -Force
Copy-Item (Join-Path $PSScriptRoot "config") $BundlePath -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot "nomad") $BundlePath -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot "scripts") $BundlePath -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot "comfyui-custom-nodes") $BundlePath -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot "cloud-init.yaml.tmpl") $BundlePath -Force

Write-Output "Bundle created at $BundlePath"
