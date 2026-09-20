<#
.SYNOPSIS
    Erzeugt aus translations\*.json die Sprachdateien in TagTuner.Core.

.DESCRIPTION
    Englisch ist die Ausgangssprache und steht im Code selbst, deshalb gibt es
    dafür keine Tabelle. Jede andere Sprache ist eine JSON-Datei, die den
    englischen Text auf die Übersetzung abbildet.

    Das Skript prüft dabei mit: Ein Platzhalter wie {0}, der im Englischen
    steht, muss auch in der Übersetzung vorkommen, sonst fehlt zur Laufzeit
    ein Wert. Stimmt etwas nicht, bricht es ab, statt kaputten Code zu
    schreiben.
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'

# Kürzel und Name des Feldes in der Klasse. Die Reihenfolge ist die der
# Auswahlliste in den Einstellungen; die Eigenbezeichnungen stehen in
# Strings.cs, weil sie dort auch zur Laufzeit gebraucht werden.
$Languages = @(
    @{ Code = 'de'; Field = 'German' }
    @{ Code = 'fr'; Field = 'French' }
    @{ Code = 'es'; Field = 'Spanish' }
    @{ Code = 'it'; Field = 'Italian' }
    @{ Code = 'pt'; Field = 'Portuguese' }
    @{ Code = 'nl'; Field = 'Dutch' }
    @{ Code = 'pl'; Field = 'Polish' }
    @{ Code = 'ru'; Field = 'Russian' }
    @{ Code = 'uk'; Field = 'Ukrainian' }
    @{ Code = 'tr'; Field = 'Turkish' }
    @{ Code = 'cs'; Field = 'Czech' }
    @{ Code = 'sv'; Field = 'Swedish' }
)

# ConvertFrom-Json unterscheidet in Windows PowerShell keine Gross- und
# Kleinschreibung und wirft deshalb "ALBUM" und "Album" in einen Topf. Der
# Serializer aus System.Web tut das nicht.
Add-Type -AssemblyName System.Web.Extensions
$parser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$parser.MaxJsonLength = [int]::MaxValue

$source = Join-Path $Root 'translations'
$target = Join-Path $Root 'src\TagTuner.Core\Settings\Languages'
if (-not (Test-Path $target)) { New-Item -ItemType Directory -Force $target | Out-Null }

function Quote([string]$text) {
    # Reihenfolge zaehlt: der Backslash zuerst, sonst verdoppelt sich das,
    # was die anderen Ersetzungen einfuegen.
    $out = $text.Replace('\', '\\').Replace('"', '\"')
    $out = $out.Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    return '"' + $out + '"'
}

function Slots([string]$text) {
    ([regex]::Matches($text, '\{(\d+)') | ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique) -join ','
}

$problems = @()
$reference = $null

foreach ($lang in $Languages) {
    $file = Join-Path $source ($lang.Code + '.json')
    if (-not (Test-Path $file)) { throw "Die Datei $file fehlt." }

    $table = $parser.DeserializeObject((Get-Content $file -Raw -Encoding UTF8))
    # Nach Schluessel sortiert, damit zwei Laeufe dieselbe Datei ergeben.
    $keys = @($table.Keys | Sort-Object -CaseSensitive)

    if ($null -eq $reference) {
        $reference = $keys
    }
    else {
        foreach ($k in ($reference | Where-Object { $keys -notcontains $_ })) {
            $problems += "$($lang.Code): fehlt $k"
        }
        foreach ($k in ($keys | Where-Object { $reference -notcontains $_ })) {
            $problems += "$($lang.Code): unbekannt $k"
        }
    }

    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($key in $keys) {
        $value = [string]$table[$key]
        if ([string]::IsNullOrWhiteSpace($value)) {
            $problems += "$($lang.Code): leer $key"
            continue
        }
        if ((Slots $key) -ne (Slots $value)) {
            $problems += "$($lang.Code): Platzhalter stimmen nicht bei $key"
        }
        $lines.Add("        [$(Quote $key)] = $(Quote $value),")
    }

    $body = @(
        "// Erzeugt aus translations\$($lang.Code).json von tools\build-strings.ps1."
        "// Nicht von Hand bearbeiten: die JSON-Datei aendern und das Skript laufen lassen."
        ""
        "namespace TagTuner.Core.Settings;"
        ""
        "public static partial class Strings"
        "{"
        "    internal static readonly Dictionary<string, string> $($lang.Field) ="
        "        new(StringComparer.Ordinal)"
        "    {"
        $lines
        "    };"
        "}"
    ) -join "`n"

    $out = Join-Path $target "Strings.$($lang.Code).cs"
    [System.IO.File]::WriteAllText($out, $body + "`n", (New-Object System.Text.UTF8Encoding $true))
    Write-Host ("{0,-4} {1,4} Texte  ->  {2}" -f $lang.Code, $keys.Count, (Split-Path $out -Leaf))
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    throw "$($problems.Count) Problem(e) in den Uebersetzungen."
}

Write-Host "`nFertig: $($Languages.Count) Sprachen, Englisch kommt aus dem Code." -ForegroundColor Green
