# JG TacRelay - Maintainer: Sarevoc
#requires -Version 7.0
<#
    Messwerkzeug für das echte Funkfeld (A32; Befund 30.09.2026: das Funkfeld ging bei Sarevoc über beide Bildschirme auf).
    Startet TacRelay.exe mit --funkfeld-messen. Das Programm öffnet dann NUR das Funkfeld mit der Vorführungsszene
    (Attrappen: kein Netz, kein Audiogerät, keine globalen Tasten, keine Joysticks), ohne Fokus, oben links auf dem
    Hauptmonitor, hält einige Sekunden lang alle 100 ms die echte Größe fest (GetWindowRect, ActualWidth, DPI, Monitor,
    Angebot an das Kachelraster), nimmt das Fenster einmal per PrintWindow (PW_RENDERFULLCONTENT) auf und schließt es
    wieder. Einstellungen schreibt es nur in eine Wegwerf-Datei neben den Bericht; das Profil bleibt unberührt.

    Das Funkfeld ist ein sichtbares Fenster, immer vorn. Deshalb startet nichts ohne -SichtbarFreigegeben (Exit 5), und
    diesen Schalter setzt nur, wem Sarevoc das im Gespräch freigegeben hat. Läuft Star Citizen oder eine andere
    Vollbildanwendung, startet nichts (Exit 4); das Programm prüft beides vor dem Öffnen und während der Messung noch
    einmal selbst und schließt dann sofort.

        pwsh -NoProfile -File tools\funkfeld-messen.ps1 -SichtbarFreigegeben [-Ansicht raster|liste] [-Felder 5|10]
             [-Sekunden 3] [-Exe <TacRelay.exe>] [-Bericht <C:\Claude\...\bericht.json>]

    Ohne -Bericht landet alles unter C:\Claude\tmp\tr-funkfeld-messen\<Zeitstempel>_<Ansicht>-<Felder>\. Danach fasst das
    Skript den Bericht zusammen: erwartete Größe (optimal, vom Programm gerechnet), Größe am Ende, ob das Fenster während
    der Messung wuchs. Exitcodes: 0 passt, 1 wächst oder weicht ab, 2 Argumente, 3 Fehler, 4 Spiel, 5 ohne Freigabe,
    6 Bericht außerhalb von C:\Claude.
#>
[CmdletBinding()]
param(
    [ValidateSet('raster', 'liste')][string]$Ansicht = 'raster',
    [ValidateSet(5, 10)][int]$Felder = 10,
    [ValidateRange(1, 30)][int]$Sekunden = 3,
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Funk.Client\bin\Debug\net10.0-windows\TacRelay.exe'),
    [string]$Bericht = '',
    # Sichtbares Fenster, immer vorn: nur nach Sarevocs Freigabe im Gespräch.
    [switch]$SichtbarFreigegeben
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $SichtbarFreigegeben) {
    Write-Host 'Die Messung öffnet ein sichtbares Funkfeld (immer vorn). Nur mit -SichtbarFreigegeben, und den gibt Sarevoc im Gespräch frei.'
    exit 5
}

Add-Type @'
using System; using System.Runtime.InteropServices;
public static class TrMessen {
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
}
'@

# Nur lesend, wie tests\Funk.Tests\Spielschutz.cs: Prozessliste und Benachrichtigungszustand von Windows
# (2 Vollbild, 3 Direct3D-Vollbild, 4 Präsentationsmodus). Randlose Fenstermodi erkennt nur die Prozessliste.
function Spielgrund {
    $spiel = Get-Process -Name 'StarCitizen*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($spiel) { return "$($spiel.ProcessName) läuft" }
    $zustand = 0
    if ([TrMessen]::SHQueryUserNotificationState([ref]$zustand) -eq 0 -and $zustand -in 2, 3, 4) {
        return "eine Vollbildanwendung läuft (Benachrichtigungszustand $zustand)"
    }
    return $null
}

if ($grund = Spielgrund) {
    Write-Host "$grund - es wird nichts gestartet."
    exit 4
}

if (-not $Bericht) {
    $Bericht = Join-Path 'C:\Claude\tmp\tr-funkfeld-messen' ("{0}_{1}-{2}\bericht.json" -f (Get-Date -Format 'yyyyMMdd_HHmmss'), $Ansicht, $Felder)
}
$Bericht = [System.IO.Path]::GetFullPath($Bericht)
if (-not $Bericht.StartsWith('C:\Claude\', [System.StringComparison]::OrdinalIgnoreCase) -or [System.IO.Path]::GetExtension($Bericht) -ne '.json') {
    Write-Host "Der Bericht muss eine .json-Datei unter C:\Claude sein: $Bericht"
    exit 6
}
if ($Bericht.Contains(' ')) {
    Write-Host "Bitte einen Berichtspfad ohne Leerzeichen: $Bericht"
    exit 2
}
if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Host "TacRelay.exe nicht gefunden: $Exe (erst bauen, etwa mit tests\testlauf.ps1)"
    exit 3
}

# Nur die Attrappen des Messwerkzeugs, nie der echte Client mit Geräten: --vorfuehrung steht immer dabei, --stumm
# gehört nicht hierher, und FUNK_RAUCHTEST (die Freigabe für echte Geräte) wird für diesen Lauf geleert.
$Argumente = @('--vorfuehrung', '--funkfeld-messen', $Bericht, '--ansicht', $Ansicht, '--felder', "$Felder", '--sekunden', "$Sekunden", '--sichtbar-freigegeben')
if ('--vorfuehrung' -notin $Argumente -or '--stumm' -in $Argumente) {
    Write-Host 'Das Messwerkzeug läuft nur mit den Attrappen der Vorführung.'
    exit 2
}
$env:FUNK_RAUCHTEST = $null
# Eigene Einstellungsdatei neben dem Bericht: Die Messung berührt so auch dann nichts von einem laufenden JG TacRelay,
# wenn die App den Schalter einmal später prüfen sollte als die Einzelinstanz.
$env:TACRELAY_EINSTELLUNGEN = [IO.Path]::ChangeExtension($Bericht, '.umgebung-einstellungen.json')
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Bericht) | Out-Null
if (Test-Path -LiteralPath $Bericht) { Remove-Item -LiteralPath $Bericht }

$p = $null
$code = 0
try {
    $p = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -ArgumentList $Argumente -PassThru
    # Das Programm schließt nach der Messzeit selbst (und bei einem Spiel sofort); dies ist nur die Notbremse.
    if (-not $p.WaitForExit(($Sekunden + 30) * 1000)) {
        Write-Host 'Das Programm endete nicht rechtzeitig und wird beendet.'
        $p.Kill()
        $code = 3
    } else {
        $code = $p.ExitCode
    }
}
finally {
    # Auch nach einem Fehler oder Strg+C: Das Funkfeld bleibt nicht offen stehen.
    if ($p -and -not $p.HasExited) { $p.Kill() }
}

if (-not (Test-Path -LiteralPath $Bericht)) {
    Write-Host "Kein Bericht entstanden: $Bericht (Exitcode $code)"
    exit ($code -ne 0 ? $code : 3)
}
$b = Get-Content -LiteralPath $Bericht -Raw | ConvertFrom-Json
if ($b.PSObject.Properties.Name -contains 'verweigert') {
    Write-Host "Verweigert (Code $($b.code)): $($b.verweigert)"
    exit $b.code
}

Write-Host "Bericht: $Bericht"
foreach ($m in $b.bildschirme) {
    Write-Host ("  Monitor {0},{1} {2} x {3} Bildpunkte, Arbeitsbereich bis {4},{5}, Skala {6}{7}" -f $m.bereich.links, $m.bereich.oben, $m.bereich.breite,
        $m.bereich.hoehe, $m.arbeit.rechts, $m.arbeit.unten, $m.skala, ($m.haupt ? ', Hauptmonitor' : ''))
}
if ($b.erwartet) {
    $e = $b.erwartet
    Write-Host ("Erwartet (optimal gerechnet): {0} x {1} Bildpunkte = {2} x {3} Einheiten bei Skala {4}, Lage {5},{6}; Wunsch des Inhalts {7} x {8}" -f `
        $e.breitePx, $e.hoehePx, $e.breiteEinheiten, $e.hoeheEinheiten, $e.skala, $e.links, $e.oben, $e.wunschBreite, $e.wunschHoehe)
}
if ($b.beimZeigen) {
    Write-Host ("Beim ersten Zeichnen: {0},{1} {2} x {3} Bildpunkte{4}" -f $b.beimZeigen.links, $b.beimZeigen.oben, $b.beimZeigen.breite, $b.beimZeigen.hoehe,
        ($b.nachgefuehrt ? ', danach an den Platz nachgeführt' : ''))
}
$proben = @($b.proben)
if ($proben.Count -gt 0) {
    $erste = $proben[0]; $letzte = $proben[-1]
    Write-Host ("Proben: {0}, erste {1} x {2}, letzte {3} x {4} Bildpunkte, SizeToContent {5}, Angebot an das Kachelraster {6} x {7}" -f $proben.Count,
        $erste.breitePx, $erste.hoehePx, $letzte.breitePx, $letzte.hoehePx, $letzte.sizeToContent, $letzte.rasterAngebotBreite, $letzte.rasterAngebotHoehe)
}
if ($b.ergebnis) {
    $r = $b.ergebnis
    Write-Host ("Wachsen: {0}" -f ($r.waechst ? 'ja' : 'nein'))
    Write-Host ("Endgröße {0} x {1} gegen erwartet {2} x {3} Bildpunkte, Abweichung {4}: {5}" -f $r.endeBreitePx, $r.endeHoehePx, $r.erwartetBreitePx,
        $r.erwartetHoehePx, $r.abweichungPx, ($r.passt ? 'passt' : 'passt NICHT'))
    Write-Host '(Zum Vergleich der Befund vom 30.09.2026: 7708 x 2188 Bildpunkte über beide Monitore.)'
}
Write-Host ($b.bild ? "Bild: $($b.bild)" : 'Kein Bild: PrintWindow lieferte nichts.')
if ($b.abbruch) { Write-Host "Abbruch: $($b.abbruch)" }

if ($code -ne 0) { exit $code }
exit (($b.ergebnis -and $b.ergebnis.passt) ? 0 : 1)
