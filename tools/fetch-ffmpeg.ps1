<#
    Holt ffmpeg.exe nach tools\ffmpeg\.

    Es gibt kein NuGet-Gegenstück zu ffmpeg-static, das die Binärdatei
    mitliefert — darum dieser Schritt. Die Datei liegt bewusst nicht im
    Repository: sie ist rund 80 MB groß und ändert sich unabhängig vom Code.

    Der Build kopiert sie neben die Anwendung, sobald sie da ist
    (siehe LocalPrep.App.csproj). Solange sie fehlt, greift FfmpegLocator im
    Entwicklungsbetrieb auf die Kopie der Electron-Fassung zurück.

    Aufruf:
        pwsh tools\fetch-ffmpeg.ps1
        pwsh tools\fetch-ffmpeg.ps1 -Force      # auch bei vorhandener Datei
#>

[CmdletBinding()]
param(
    [switch]$Force,
    # "essentials" reicht für LocalPrep: enthält libmp3lame, flac, vorbis und aac.
    [string]$Url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
)

$ErrorActionPreference = 'Stop'

$toolsDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetDir = Join-Path $toolsDir 'ffmpeg'
$targetExe = Join-Path $targetDir 'ffmpeg.exe'

if ((Test-Path $targetExe) -and -not $Force) {
    $size = [math]::Round((Get-Item $targetExe).Length / 1MB, 1)
    Write-Host "ffmpeg.exe ist bereits da ($size MB). -Force erzwingt einen Neubezug."
    exit 0
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$zip = Join-Path ([System.IO.Path]::GetTempPath()) "ffmpeg-$(Get-Random).zip"

try {
    Write-Host "Lade $Url …"
    $progress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'   # sonst bremst die Fortschrittsanzeige den Download
    Invoke-WebRequest -Uri $Url -OutFile $zip -UseBasicParsing
    $ProgressPreference = $progress

    Write-Host 'Entpacke …'
    $extract = Join-Path ([System.IO.Path]::GetTempPath()) "ffmpeg-x-$(Get-Random)"
    Expand-Archive -Path $zip -DestinationPath $extract -Force

    $found = Get-ChildItem -Path $extract -Filter 'ffmpeg.exe' -Recurse |
             Select-Object -First 1
    if (-not $found) { throw 'ffmpeg.exe im Archiv nicht gefunden.' }

    Copy-Item $found.FullName $targetExe -Force

    $size = [math]::Round((Get-Item $targetExe).Length / 1MB, 1)
    Write-Host "Fertig: $targetExe ($size MB)"

    # Kurzer Funktionsnachweis statt blindem Vertrauen
    $version = & $targetExe -hide_banner -version 2>&1 | Select-Object -First 1
    Write-Host $version
}
finally {
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    if ($extract) { Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue }
}
