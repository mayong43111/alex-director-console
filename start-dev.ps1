param(
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

$apiPath = Join-Path $PSScriptRoot "src/AlexDirectorConsole.Api"
$webPath = Join-Path $PSScriptRoot "src/web"

foreach ($command in @("dotnet", "npm")) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' was not found in PATH."
    }
}

if (-not $SkipInstall -and -not (Test-Path (Join-Path $webPath "node_modules"))) {
    Write-Host "Installing frontend dependencies..." -ForegroundColor Cyan
    & npm install --prefix $webPath
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Starting API with hot reload: http://localhost:5174" -ForegroundColor Cyan
$apiProcess = Start-Process dotnet `
    -ArgumentList @("watch", "run", "--launch-profile", "http") `
    -WorkingDirectory $apiPath `
    -NoNewWindow `
    -PassThru

try {
    Write-Host "Starting web app with HMR: http://localhost:6173" -ForegroundColor Cyan
    $webProcess = Start-Process npm.cmd `
        -ArgumentList @("run", "dev") `
        -WorkingDirectory $webPath `
        -NoNewWindow `
        -PassThru

    Write-Host "Press Ctrl+C to stop both servers." -ForegroundColor DarkGray
    Wait-Process -Id $apiProcess.Id, $webProcess.Id -Any
}
finally {
    foreach ($process in @($apiProcess, $webProcess)) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}