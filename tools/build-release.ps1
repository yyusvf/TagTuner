<#
.SYNOPSIS
  Baut Setup und portables ZIP fuer eine Veroeffentlichung.

.DESCRIPTION
  Eigenstaendiger Build, damit auf dem Zielrechner keine Runtime noetig ist.
  Danach das Setup mit Inno Setup und ein ZIP mit demselben Inhalt.

.EXAMPLE
  pwsh tools\build-release.ps1 -Version 0.4.0
#>
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'

Write-Host "TagTuner $Version" -ForegroundColor Cyan

# ── Aufraeumen ──────────────────────────────────────────────────
foreach ($dir in @($publish, $dist)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir | Out-Null
}

# ── Tests ───────────────────────────────────────────────────────
# Ein Release mit einem roten Test gibt es nicht. Sie laufen in einem
# Temp-Ordner und fassen weder Musik noch Verlauf des Nutzers an.
Write-Host 'Tests laufen...'
dotnet test (Join-Path $root 'tests\TagTuner.Tests') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests sind fehlgeschlagen, kein Release' }

# ── Bauen ───────────────────────────────────────────────────────
Write-Host 'Veroeffentlichung wird gebaut...'
dotnet publish (Join-Path $root 'src\TagTuner.App') `
    -c Release -r win-x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" `
    -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish ist fehlgeschlagen' }

$exe = Join-Path $publish 'TagTuner.exe'
if (-not (Test-Path $exe)) { throw 'TagTuner.exe fehlt in der Veroeffentlichung' }

# XAML wird ohne diesen Schritt nicht mitgeliefert und die App stirbt beim
# Start; die csproj holt das nach, hier wird es nur geprueft.
foreach ($needed in @('TagTuner.pri', 'MainWindow.xbf', 'App.xbf')) {
    if (-not (Test-Path (Join-Path $publish $needed))) {
        throw "$needed fehlt in der Veroeffentlichung"
    }
}

$size = [math]::Round((Get-ChildItem $publish -Recurse | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "  $size MB"

# ── Setup ───────────────────────────────────────────────────────
$iscc = Get-ChildItem "$env:LOCALAPPDATA\Programs","C:\Program Files*" -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $iscc) { throw 'Inno Setup (ISCC.exe) wurde nicht gefunden' }

Write-Host 'Setup wird gebaut...'
& $iscc "/DAppVersion=$Version" (Join-Path $root 'installer\TagTuner.iss') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup ist fehlgeschlagen' }

# ── Portables ZIP ───────────────────────────────────────────────
# Der Name entscheidet die Reihenfolge auf der Release-Seite: GitHub sortiert
# alphabetisch, und „Setup" soll vor dem ZIP stehen. Darum „Windows-Portable"
# statt nur „Portable".
Write-Host 'ZIP wird gepackt...'
Compress-Archive -Path (Join-Path $publish '*') `
    -DestinationPath (Join-Path $dist "TagTuner-$Version-Windows-Portable.zip") -Force

Get-ChildItem $dist | ForEach-Object {
    '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB)
}
