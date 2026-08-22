[CmdletBinding()]
param(
    [string]$CommitMessage = "chore: deploy production",
    [string]$Subscription = "yongma-1",
    [string]$ResourceGroup = "RG-QWEN-LORA-JPE",
    [string]$Registry = "alexdirector66595",
    [string]$ContainerApp = "ca-alex-director-66595",
    [string]$ImageName = "alex-director-console",
    [string]$Remote = "origin",
    [switch]$SkipValidation
)

$ErrorActionPreference = "Stop"

function Assert-NativeSuccess([string]$Operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found."
    }
}

Push-Location $PSScriptRoot
try {
    Require-Command "git"
    Require-Command "dotnet"
    Require-Command "npm.cmd"
    Require-Command "az.cmd"

    $branch = (& git branch --show-current).Trim()
    Assert-NativeSuccess "Reading the current Git branch"
    if ([string]::IsNullOrWhiteSpace($branch)) {
        throw "Deployments require a checked-out Git branch."
    }

    if (-not $SkipValidation) {
        Write-Host "[1/6] Running API tests..." -ForegroundColor Cyan
        & dotnet test "src/backend/AlexDirectorConsole.V2.Api.Tests/AlexDirectorConsole.V2.Api.Tests.csproj" -c Release --no-restore
        Assert-NativeSuccess "API tests"

        Write-Host "[2/6] Building frontend..." -ForegroundColor Cyan
        Push-Location "src/frontend"
        try {
            & npm.cmd run build
            Assert-NativeSuccess "Frontend build"
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Host "[1/6] Validation skipped." -ForegroundColor Yellow
        Write-Host "[2/6] Frontend build skipped." -ForegroundColor Yellow
    }

    Write-Host "[3/6] Creating deployment commit..." -ForegroundColor Cyan
    & git diff --check
    Assert-NativeSuccess "Git whitespace validation"
    & git add --all
    Assert-NativeSuccess "Staging changes"
    & git diff --cached --quiet
    $stagedDiffExitCode = $LASTEXITCODE
    if ($stagedDiffExitCode -eq 1) {
        & git commit -m $CommitMessage
        Assert-NativeSuccess "Creating deployment commit"
    }
    elseif ($stagedDiffExitCode -ne 0) {
        throw "Checking staged changes failed with exit code $stagedDiffExitCode."
    }
    else {
        Write-Host "No source changes to commit; deploying the current commit."
    }

    if ((& git status --porcelain).Count -ne 0) {
        throw "The working tree is not clean after commit."
    }

    Write-Host "[4/6] Pushing $branch to $Remote..." -ForegroundColor Cyan
    & git push $Remote $branch
    Assert-NativeSuccess "Git push"

    & az.cmd account set --subscription $Subscription
    Assert-NativeSuccess "Selecting Azure subscription"
    $commitSha = (& git rev-parse --short=12 HEAD).Trim().ToLowerInvariant()
    Assert-NativeSuccess "Reading commit SHA"
    $imageTag = "git-$commitSha"
    $loginServer = "$Registry.azurecr.io"
    $fullImage = "$loginServer/${ImageName}:$imageTag"

    Write-Host "[5/6] Building $fullImage in Azure Container Registry..." -ForegroundColor Cyan
    & az.cmd acr build `
        --subscription $Subscription `
        --registry $Registry `
        --image "${ImageName}:$imageTag" `
        --platform linux/amd64 `
        .
    Assert-NativeSuccess "ACR image build"

    $revisionSuffix = "$commitSha-$(Get-Date -AsUTC -Format 'yyyyMMddHHmmss')"
    Write-Host "[6/6] Deploying Container App revision $revisionSuffix..." -ForegroundColor Cyan
    & az.cmd containerapp update `
        --subscription $Subscription `
        --resource-group $ResourceGroup `
        --name $ContainerApp `
        --image $fullImage `
        --revision-suffix $revisionSuffix `
        --output none
    Assert-NativeSuccess "Container App update"

    $application = & az.cmd containerapp show `
        --subscription $Subscription `
        --resource-group $ResourceGroup `
        --name $ContainerApp `
        --output json | ConvertFrom-Json
    Assert-NativeSuccess "Reading deployed Container App"
    $deployedImage = $application.properties.template.containers[0].image
    if ($application.properties.provisioningState -ne "Succeeded" -or $deployedImage -ne $fullImage) {
        throw "Deployment verification failed. State='$($application.properties.provisioningState)', image='$deployedImage'."
    }

    $url = "https://$($application.properties.configuration.ingress.fqdn)/"
    $response = Invoke-WebRequest $url -MaximumRedirection 0 -SkipHttpErrorCheck -TimeoutSec 60
    if ([int]$response.StatusCode -notin 200, 302, 401) {
        throw "Production endpoint returned HTTP $([int]$response.StatusCode)."
    }

    Write-Host "Deployment succeeded." -ForegroundColor Green
    Write-Host "Commit:  $commitSha"
    Write-Host "Image:   $fullImage"
    Write-Host "Revision: $($application.properties.latestRevisionName)"
    Write-Host "URL:      $url"
    Write-Host "HTTP:     $([int]$response.StatusCode)"
}
finally {
    Pop-Location
}