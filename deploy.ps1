#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes xOTA Companion alongside QSO Logger and creates shortcuts.
.DESCRIPTION
    - Builds a self-contained single-file win-x64 release
    - Deploys to the same parent folder as QSO Logger
    - Creates Desktop and Start Menu shortcuts
    - xOTA Companion has READ-ONLY access to the QSO Logger database
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Config ───────────────────────────────────────────────────────────────────
$QsoLoggerDir  = "C:\Users\lance\OneDrive\Radio\QSOLogger"
$DeployDir     = Join-Path (Split-Path $QsoLoggerDir -Parent) "xOTA Companion"
$ProjectDir    = $PSScriptRoot
$PublishDir    = "C:\BuildCache\xOTACompanion\publish"
$ExeName       = "xOTACompanion.exe"
$AppName       = "xOTA Companion"

# ── Step 1: Publish ───────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==> Building self-contained release..." -ForegroundColor Cyan

Push-Location $ProjectDir
try {
    dotnet publish "$ProjectDir\xOTACompanion.csproj" `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output "$PublishDir" `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

# ── Step 2: Deploy ────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==> Deploying to: $DeployDir" -ForegroundColor Cyan

if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
}

# Stop running instance if any
$running = Get-Process -Name "xOTACompanion" -EA SilentlyContinue
if ($running) {
    Write-Host "    Stopping running instance..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Copy-Item -Path "$PublishDir\*" -Destination $DeployDir -Recurse -Force
Write-Host "    Files copied." -ForegroundColor Green

# ── Step 3: Verify DB access is read-only ─────────────────────────────────────
# xOTA Companion already opens the QSO Logger DB with read-only mode in
# GreenLoggerDbService.cs (SqliteOpenMode.ReadOnly). No further action needed.
Write-Host ""
Write-Host "==> Database access: READ-ONLY (enforced in GreenLoggerDbService)" -ForegroundColor Green

# ── Step 4: Shortcuts ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==> Creating shortcuts..." -ForegroundColor Cyan

$ExePath  = Join-Path $DeployDir $ExeName
$IconPath = Join-Path $DeployDir "tree_icon.ico"
$Shell    = New-Object -ComObject WScript.Shell

# Desktop shortcut
$DesktopLink = Join-Path ([Environment]::GetFolderPath("Desktop")) "$AppName.lnk"
$sc = $Shell.CreateShortcut($DesktopLink)
$sc.TargetPath       = $ExePath
$sc.WorkingDirectory = $DeployDir
if (Test-Path $IconPath) { $sc.IconLocation = "$IconPath,0" }
$sc.Description      = "xOTA Companion - POTA/SOTA spot viewer"
$sc.Save()
Write-Host "    Desktop shortcut created." -ForegroundColor Green

# Start Menu shortcut
$StartMenuDir  = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs"
$StartMenuLink = Join-Path $StartMenuDir "$AppName.lnk"
$sc2 = $Shell.CreateShortcut($StartMenuLink)
$sc2.TargetPath       = $ExePath
$sc2.WorkingDirectory = $DeployDir
if (Test-Path $IconPath) { $sc2.IconLocation = "$IconPath,0" }
$sc2.Description      = "xOTA Companion - POTA/SOTA spot viewer"
$sc2.Save()
Write-Host "    Start Menu shortcut created." -ForegroundColor Green

# ── Done ──────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==> Deploy complete!" -ForegroundColor Green
Write-Host "    Installed to : $DeployDir"
Write-Host "    QSO Logger DB: $QsoLoggerDir (read-only)"
Write-Host ""
