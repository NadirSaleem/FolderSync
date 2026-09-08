<#
    Rebuilds FolderSyncSetup.msi from the current FolderSync source.

    Run this after any code change to SyncEngine / SyncEngine.Gui, then re-run
    it whenever you want a fresh installer. Requires the `wix` global dotnet
    tool (installed once via: dotnet tool install --global wix --version 5.0.2)
    and its UI extension (wix extension add WixToolset.UI.wixext/5.0.2 --global).

    Bump $Version below for each release you hand out — Windows Installer uses
    it (together with the fixed UpgradeCode in Package.wxs) to decide whether a
    new MSI is an upgrade of a previous install or a downgrade to block.
#>
param(
    [string]$Version = "1.0.1.2"
)

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$repoDir = Split-Path $installerDir -Parent
$publishDir = Join-Path $installerDir "publish"
$guiProject = Join-Path $repoDir "SyncEngine.Gui\SyncEngine.Gui.csproj"

Write-Host "Publishing self-contained release build..." -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
dotnet publish $guiProject -c Release -r win-x64 --self-contained -o $publishDir -p:WindowsAppSDKSelfContained=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Building MSI (version $Version)..." -ForegroundColor Cyan
Push-Location $installerDir
try {
    wix build Package.wxs -arch x64 -ext WixToolset.UI.wixext `
        -d PublishDir="$publishDir" `
        -d ProductVersion="$Version" `
        -o FolderSyncSetup.msi
    if ($LASTEXITCODE -ne 0) { throw "wix build failed" }
}
finally {
    Pop-Location
}

Write-Host "Done: $installerDir\FolderSyncSetup.msi" -ForegroundColor Green
