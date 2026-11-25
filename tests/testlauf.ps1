# JG TacRelay - Maintainer: Sarevoc
#requires -Version 7.0
<#
    Testlauf mit der Umgebung aus tools\dotnet-umgebung.ps1: Pakete aus dem Zonen-Cache, keine Telemetrie, Temp und
    CLI-Zustand in C:\Claude, keine stehen bleibenden Bauprozesse. Ohne sie nimmt das Restore von "dotnet run" den
    NuGet-Ordner im Benutzerprofil, und die CLI sendet Telemetrie.

    Baut zuerst (ohne Bauserver) und startet dann den Testlauf ohne erneutes Bauen. Alles nach den Schaltern geht an den
    Testlauf, etwa ein Namensteil als Filter:

        pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\tests\testlauf.ps1
        pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\tests\testlauf.ps1 Probefunk
        pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\tests\testlauf.ps1 -Konfiguration Release -OhneBauen Last_

    Ergebnis: Der Exitcode ist 0 nur, wenn alles bestanden hat; bei Fehlschlägen, einem Baufehler oder ohne Summenzeile ist er
    ungleich 0. Neben einem Spiel läuft der Lauf mit niedriger Priorität über

        cmd /c start "" /belownormal /wait /b pwsh -NoProfile -File ...\tests\testlauf.ps1

    und dort kommt der Exitcode nicht an: "start /wait /b" liefert immer 0 (gemessen am 29.09.2026). Deshalb schreibt jeder
    Lauf zusätzlich die Datei C:\Claude\tmp\testlauf-ergebnis.txt (mit -Ergebnisdatei ein anderer Ort unter C:\Claude):
    erste Zeile die Summenzeile ("x bestanden, y fehlgeschlagen, z gesamt, t ms"), zweite Zeile "Exitcode n", dazu
    Arbeitskopie, Argumente und Zeit. Ausgewertet wird die Summenzeile, nicht der Exitcode von cmd.

    Tests mit echten Geräten, Eingabegeräten oder Fenstern laufen nur mit FUNK_RAUCHTEST=1 und ohne Spiel
    (tests\Funk.Tests\Spielschutz.cs).
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Debug', 'Release')][string]$Konfiguration = 'Debug',
    [switch]$OhneBauen,
    [string]$Ergebnisdatei = 'C:\Claude\tmp\testlauf-ergebnis.txt',
    [Parameter(ValueFromRemainingArguments)][string[]]$Testargumente = @()
)
$ErrorActionPreference = 'Stop'
# Vor Set-StrictMode einbinden: dev-umgebung.ps1 fragt Variablen ab, die nicht immer gesetzt sind.
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\dotnet-umgebung.ps1')
Set-StrictMode -Version Latest

$ergebnisPfad = [System.IO.Path]::GetFullPath($Ergebnisdatei)
if (-not $ergebnisPfad.StartsWith('C:\Claude\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Die Ergebnisdatei muss unter C:\Claude liegen: $ergebnisPfad"
}

# Summenzeile und Code in die Ergebnisdatei, dann mit dem Code beenden. Auch bei einem Baufehler, sonst stünde dort der
# Stand eines früheren Laufs.
function Beenden([string]$Summe, [int]$Code) {
    $zeilen = @(
        $Summe
        "Exitcode $Code"
        "Arbeitskopie $(Split-Path -Parent $PSScriptRoot)"
        "Argumente $($Testargumente -join ' ')"
        "Zeit $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    )
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ergebnisPfad) | Out-Null
    Set-Content -LiteralPath $ergebnisPfad -Value $zeilen -Encoding utf8
    exit $Code
}

$projekt = Join-Path $PSScriptRoot 'Funk.Tests\Funk.Tests.csproj'
if (-not $OhneBauen) {
    & dotnet build $projekt -c $Konfiguration --nologo --disable-build-servers
    if ($LASTEXITCODE -ne 0) { Beenden "Bauen fehlgeschlagen" $LASTEXITCODE }
}

# Die Ausgabe geht unverändert weiter; nebenbei wird die Summenzeile des Testlaufs (Testlauf.Summenzeile) gemerkt.
$summe = $null
& dotnet run --project $projekt -c $Konfiguration --no-build -- @Testargumente | ForEach-Object {
    if ($_ -match '^(\d+) bestanden, (\d+) fehlgeschlagen, (\d+) gesamt') { $summe = $_ }
    $_
}
$code = $LASTEXITCODE
if ($null -eq $summe) {
    Beenden "keine Summenzeile (Testlauf abgebrochen)" ($code -ne 0 ? $code : 1)
}
# Die Summenzeile entscheidet: Fehlschläge ergeben nie 0, auch wenn dotnet run einmal 0 meldete.
$fehlgeschlagen = [int]([regex]::Match($summe, '(\d+) fehlgeschlagen').Groups[1].Value)
if ($fehlgeschlagen -gt 0 -and $code -eq 0) { $code = 1 }
Beenden $summe $code
