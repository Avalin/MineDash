#Requires -Version 5.1
param(
    [string]$Image = "maoraw/minedash:latest",
    [switch]$Push
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

Test-DockerRunning

Write-Host "Building $Image ..." -ForegroundColor Cyan
docker build -t $Image .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Push) {
    Write-Host "Pushing $Image ..." -ForegroundColor Cyan
    docker push $Image
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Done." -ForegroundColor Green
