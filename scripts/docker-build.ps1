#Requires -Version 5.1
param(
    [string]$Image = "maoraw/minedash:latest",
    [switch]$Push,
    [switch]$NoCache
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Test-DockerRunning {
    $output = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        $details = ($output | Out-String).Trim()
        if ($details -match 'dockerDesktopLinuxEngine|cannot find the file specified|Is the docker daemon running') {
            Write-Host "Docker Desktop isn't currently running. Please ensure you're running Docker." -ForegroundColor Red
        }
        else {
            Write-Host "Docker isn't available. Please ensure Docker is installed and running." -ForegroundColor Red
            if ($details) {
                Write-Host $details -ForegroundColor DarkGray
            }
        }
        exit 1
    }
}

function Invoke-DockerBuild {
    param([string[]]$BuildArgs)

    $output = & docker @BuildArgs 2>&1
    $exitCode = $LASTEXITCODE
    if ($output) {
        Write-Host ($output | Out-String)
    }
    return @{
        ExitCode = $exitCode
        Output = ($output | Out-String)
    }
}

Test-DockerRunning

$buildArgs = @("build", "-t", $Image, ".")
if ($NoCache) {
    $buildArgs = @("build", "--no-cache", "-t", $Image, ".")
}

Write-Host "Building $Image ..." -ForegroundColor Cyan
$result = Invoke-DockerBuild -BuildArgs $buildArgs

if ($result.ExitCode -ne 0 -and $result.Output -match 'parent snapshot.*does not exist') {
    Write-Host "Docker build cache looks corrupted. Pruning builder cache and retrying..." -ForegroundColor Yellow
    docker builder prune -f | Out-Null
    $buildArgs = @("build", "--no-cache", "-t", $Image, ".")
    $result = Invoke-DockerBuild -BuildArgs $buildArgs
}

if ($result.ExitCode -ne 0) { exit $result.ExitCode }

if ($Push) {
    Write-Host "Pushing $Image ..." -ForegroundColor Cyan
    docker push $Image
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Done." -ForegroundColor Green
