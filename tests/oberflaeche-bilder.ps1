# JG TacRelay - Maintainer: Sarevoc
#requires -Version 7.0
<#
    Erzeugt die Bilder der Oberfläche für docs\bilder\ in der Vorführung (Attrappen: kein Netz, kein Audiogerät, keine
    globalen Tasten, keine Joysticks). Jede Variante bekommt eine eigene Einstellungsdatei im Arbeitsordner, damit nichts
    im Benutzerprofil landet.

    Jede Variante startet das Programm mit echten, sichtbaren Fenstern (Hauptfenster aktiviert, Funkfeld immer vorn).
    Deshalb nur mit -SichtbarFreigegeben, und den gibt Sarevoc im Gespräch frei; ohne ihn startet nichts (Exit 5). Bilder
    ohne Programmstart: FUNK_BILDER=<Ordner unter C:\Claude> und der Test OberflaecheFensterTests.
    Läuft Star Citizen oder eine Vollbildanwendung, bricht das Aufnahmeskript ab, bevor ein Fenster aufgeht.

        pwsh -File tests\oberflaeche-bilder.ps1 -SichtbarFreigegeben [-Arbeitsordner C:\Claude\tmp\tr-oberflaeche]
#>
[CmdletBinding()]
param(
    [string]$Arbeitsordner = 'C:\Claude\tmp\tr-oberflaeche',
    [switch]$SichtbarFreigegeben
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $SichtbarFreigegeben) {
    Write-Host 'Sieben Programmstarts mit sichtbaren Fenstern: nur mit -SichtbarFreigegeben, und den gibt Sarevoc im Gespräch frei.'
    exit 5
}

$wurzel = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $wurzel 'src\Funk.Client\bin\Debug\net10.0-windows\TacRelay.exe'
$bilder = Join-Path $wurzel 'docs\bilder'
$aufnahme = Join-Path $PSScriptRoot 'oberflaeche-aufnehmen.ps1'
New-Item -ItemType Directory -Force -Path $Arbeitsordner, $bilder | Out-Null

$ohneFunkfeld = '"funkfeld": { "beimStartZeigen": false }'
# Die Funkfeld-Bilder (01-verbindung-funkfeld, 08-fuenfer-funkfeld, 12-liste-funkfeld) entstehen im Speicher, ohne
# Programmstart: Test OberflaecheFunkfeldBilderTests.Funkfeld_Dokubilder_Vorfuehrung mit FUNK_BILDER.
# Seit 0.10.0 gibt es nur noch Start und Einstellungen, darunter die Seite Sprachaktivierung (--bereich sprachaktivierung, sie steht
# nur bei Benutzung da, in der Vorführung sendet Crew mit Sprachaktivierung); Audio und Meine Kanäle sind aufgelöst, das Aufklappen
# unten (--schwelle) gibt es nicht mehr. Die Klappbereiche (seit 0.11.1 "Erweitert:") öffnet die Einstellungsdatei; der Schlüssel
# heißt weiter anzeige.offeneFeinheiten. Unter Senden stehen die Schalter seit 0.11.1 auch zugeklappt offen. Seit dem Umbau zu
# JG TacRelay (19.10.2026) kommen die Bilder unter docs\bilder ohne Programmstart aus dem Bildlauf mit FUNK_BILDER: 01-verbindung
# aus OberflaecheStartseiteTests (start-verbunden-udp-anthrazit), 04-einstellungen aus OberflaecheStellungAnthrazitTests
# (einstellungen), 05 bis 07 aus OberflaecheFensterTests (schwelle-automatisch, -lernend, -fest), die Funkfeld-Bilder wie oben;
# 02-audio, 03-netze, 09-kompakt, 10-kompakt-schwelle und 11-echt sind gelöscht. Weitere Bilder: OberflaecheUmbauBilderTests.
$varianten = @(
    @{ Praefix = '01-verbindung';           Json = '{}';                                                                        Argumente = '--vorfuehrung';                          Nur = 'tacrelay';          Warten = 7 }
    @{ Praefix = '02-senden';               Json = "{ `"anzeige`": { `"offeneFeinheiten`": [`"senden`"] }, $ohneFunkfeld }";       Argumente = '--vorfuehrung,--bereich,einstellungen';  Nur = 'tacrelay';          Warten = 9 }
    @{ Praefix = '03-empfangen';            Json = "{ `"anzeige`": { `"offeneFeinheiten`": [`"empfangen`"] }, $ohneFunkfeld }";    Argumente = '--vorfuehrung,--bereich,einstellungen';  Nur = 'tacrelay';          Warten = 6 }
    @{ Praefix = '04-einstellungen';        Json = "{ $ohneFunkfeld }";                                                         Argumente = '--vorfuehrung,--bereich,einstellungen';  Nur = 'tacrelay';          Warten = 6 }
    # A26: Die Schwelle hat kein eigenes Fenster; seit 0.10.0 nur noch als Seite Sprachaktivierung. A28: Schalter Automatik und Lernen.
    @{ Praefix = '05-schwelle-automatik';   Json = "{ $ohneFunkfeld }";                                                         Argumente = '--vorfuehrung,--bereich,sprachaktivierung'; Nur = 'tacrelay';       Warten = 15 }
    @{ Praefix = '06-schwelle-lernen';      Json = "{ `"gate`": { `"modus`": `"Lernend`", `"korridorVonDb`": -56, `"korridorBisDb`": -36, `"anstiegSekunden`": 1.5, `"rueckkehrSekunden`": 4 }, $ohneFunkfeld }"; Argumente = '--vorfuehrung,--bereich,sprachaktivierung'; Nur = 'tacrelay'; Warten = 15 }
    @{ Praefix = '07-schwelle-fest';        Json = "{ `"gate`": { `"modus`": `"Fest`", `"schwelleDb`": -42 }, $ohneFunkfeld }"; Argumente = '--vorfuehrung,--bereich,sprachaktivierung'; Nur = 'tacrelay';       Warten = 11 }
)

foreach ($v in $varianten) {
    $datei = Join-Path $Arbeitsordner "$($v.Praefix).json"
    Set-Content -Encoding utf8 -Path $datei -Value $v.Json
    Write-Host "== $($v.Praefix)"
    & pwsh -NoProfile -File $aufnahme -Exe $exe -Ordner $bilder -Praefix $v.Praefix -Einstellungen $datei -Argumente $v.Argumente -Nur $v.Nur -Warten $v.Warten -SichtbarFreigegeben
    if ($LASTEXITCODE -ne 0) { throw "Aufnahme $($v.Praefix) endete mit $LASTEXITCODE" }
}
