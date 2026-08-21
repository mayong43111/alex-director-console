param(
    [switch]$SkipInstall,
    [int]$ApiPort = 6275,
    [int]$WebPort = 5273
)

$ErrorActionPreference = "Stop"

$apiPath = Join-Path $PSScriptRoot "src/backend/AlexDirectorConsole.V2.Api"
$webPath = Join-Path $PSScriptRoot "src/frontend"
$apiProcess = $null
$webProcess = $null

function Assert-CommandExists([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Get-AvailablePort([int]$PreferredPort, [string]$Service) {
    foreach ($candidate in $PreferredPort..($PreferredPort + 100)) {
        $listener = [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback,
            $candidate)
        try {
            $listener.Start()
            if ($candidate -ne $PreferredPort) {
                Write-Host "$Service port $PreferredPort is unavailable; using $candidate instead." -ForegroundColor Yellow
            }
            return $candidate
        }
        catch {
            continue
        }
        finally {
            $listener.Stop()
        }
    }

    throw "No available $Service port found between $PreferredPort and $($PreferredPort + 100)."
}

function Stop-ProcessTree([System.Diagnostics.Process]$Process) {
    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
}

function Wait-HttpReady(
    [string]$Url,
    [System.Diagnostics.Process]$Process,
    [int]$TimeoutSeconds = 600) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "API exited with code $($Process.ExitCode) before it became ready."
        }

        try {
            $response = Invoke-WebRequest `
                -Uri $Url `
                -UseBasicParsing `
                -TimeoutSec 1
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }

    throw "API did not become ready within $TimeoutSeconds seconds."
}

Assert-CommandExists "dotnet"
Assert-CommandExists "npm.cmd"
$apiPort = Get-AvailablePort $ApiPort "API"
$webPort = Get-AvailablePort $WebPort "Frontend"

if (-not $SkipInstall -and -not (Test-Path (Join-Path $webPath "node_modules"))) {
    Write-Host "Installing frontend dependencies..." -ForegroundColor Cyan
    & npm.cmd install --prefix $webPath
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed with exit code $LASTEXITCODE."
    }
}

$previousWatchRestart = $env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT
$previousApiTarget = $env:VITE_API_TARGET
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = "1"
$env:VITE_API_TARGET = "http://127.0.0.1:$apiPort"

try {
    Write-Host "Starting API hot reload: http://127.0.0.1:$apiPort" -ForegroundColor Cyan
    $apiProcess = Start-Process dotnet `
        -ArgumentList @("watch", "run", "--launch-profile", "http", "--urls", "http://127.0.0.1:$apiPort") `
        -WorkingDirectory $apiPath `
        -NoNewWindow `
        -PassThru

    Write-Host "Waiting for API to become ready..." -ForegroundColor DarkGray
    Wait-HttpReady "http://127.0.0.1:$apiPort/api/v2/projects" $apiProcess

    Write-Host "Starting frontend HMR: http://127.0.0.1:$webPort" -ForegroundColor Cyan
    $webProcess = Start-Process npm.cmd `
        -ArgumentList @("run", "dev", "--", "--host", "127.0.0.1", "--port", $webPort, "--strictPort") `
        -WorkingDirectory $webPath `
        -NoNewWindow `
        -PassThru

    Write-Host "Development services are running. Press Ctrl+C to stop both." -ForegroundColor Green

    while (-not $apiProcess.HasExited -and -not $webProcess.HasExited) {
        $apiProcess.WaitForExit(500) | Out-Null
        $webProcess.Refresh()
    }

    if ($apiProcess.HasExited) {
        throw "API exited with code $($apiProcess.ExitCode)."
    }
    throw "Frontend exited with code $($webProcess.ExitCode)."
}
finally {
    Write-Host "Stopping development services..." -ForegroundColor DarkGray
    Stop-ProcessTree $webProcess
    Stop-ProcessTree $apiProcess
    $env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = $previousWatchRestart
    $env:VITE_API_TARGET = $previousApiTarget
}