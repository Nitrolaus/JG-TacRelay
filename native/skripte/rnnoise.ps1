# JG TacRelay - Maintainer: Sarevoc

<#
.SYNOPSIS
  Baut rnnoise.dll (x64, MSVC, /O2, statische CRT /MT) reproduzierbar von vorn.

.DESCRIPTION
  Ablauf:
    1. Voraussetzungen und SHA-256 des Modellarchivs pruefen
    2. Bauordner native\bau\rnnoise\ loeschen und neu anlegen
    3. rnnoise_data.c/.h aus dem Modellarchiv in den Bauordner entpacken
       (nicht in die Quellen)
    4. CMake (Ninja) mit native\eigen\rnnoise\CMakeLists.txt konfigurieren und bauen
    5. DLL nach native\out\win-x64\ kopieren, SHA-256 ausgeben und ablegen
    6. dumpbin /exports und /dependents: Pflichtexporte vorhanden, keine VC-Redist
    7. Lizenz- und Herkunftsdateien nach native\out\lizenzen\rnnoise\
    8. Rauchtest (native\eigen\rnnoise\rauchtest.c) gegen die kopierte DLL

  Hinweis PATH: Der PATH des Nutzers ist laenger als 8191 Zeichen. vcvars64.bat
  verliert ihn dann und findet weder findstr noch das Windows SDK. Deshalb setzt
  die erzeugte .cmd-Datei vor vcvars einen kurzen PATH - nur fuer diesen Prozess.

  Keine Netzzugriffe, schreibt nur unter C:\Claude\work\JG-TacRelay\native\.

.EXAMPLE
  pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\native\skripte\rnnoise.ps1
.EXAMPLE
  pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\native\skripte\rnnoise.ps1 -OhneRauchtest
#>
[CmdletBinding()]
param(
    [switch]$OhneRauchtest,
    # Nur Generikpfad (ohne SSE4.1/AVX2-Laufzeitauswahl) - fuer Vergleiche.
    [switch]$OhneX86Rtcd
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

# ---------------------------------------------------------------- Pfade
$Native     = 'C:\Claude\work\JG-TacRelay\native'
$Quelle     = "$Native\quellen\rnnoise-main"
$Eigen      = "$Native\eigen\rnnoise"
$Bau        = "$Native\bau\rnnoise"
$BauCmake   = "$Bau\cmake"
$BauModell  = "$Bau\modell"
$BauTmp     = "$Bau\tmp"
$OutDir     = "$Native\out\win-x64"
$LizenzDir  = "$Native\out\lizenzen\rnnoise"
$DllOut     = "$OutDir\rnnoise.dll"

$Daten      = 'C:\Claude\data\tacrelay-quellen'
$ModellHash = '0a8755f8e2d834eff6a54714ecc7d75f9932e845df35f8b59bc52a7cfe6e8b37'
$ModellTgz  = "$Daten\rnnoise_data-$ModellHash.tar.gz"
$QuellTgz   = "$Daten\rnnoise-main.tar.gz"

$VsRoot     = 'C:\Program Files\Microsoft Visual Studio\18\Community'
$Vcvars     = "$VsRoot\VC\Auxiliary\Build\vcvars64.bat"
$CMake      = "$VsRoot\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$Ninja      = "$VsRoot\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
$Tar        = 'C:\Windows\System32\tar.exe'
$Cmd        = 'C:\Windows\System32\cmd.exe'

$Pflichtexporte = @('rnnoise_create', 'rnnoise_destroy', 'rnnoise_process_frame', 'rnnoise_get_frame_size')

function Schritt([string]$Text) { Write-Host "`n== $Text" }

function Abbruch([string]$Text) {
    Write-Host "FEHLER: $Text" -ForegroundColor Red
    exit 1
}

# Fuehrt Befehlszeilen in einer vcvars64-Umgebung aus (eigene .cmd im Bauordner),
# schreibt alles nach $Log und gibt den Exitcode zurueck.
function Invoke-VcBefehl([string]$Name, [string[]]$Zeilen, [string]$Log) {
    $datei = "$Bau\$Name.cmd"
    $kopf = @(
        '@echo off'
        'set "PATH=C:\Windows\System32;C:\Windows;C:\Windows\System32\Wbem;C:\Program Files (x86)\Microsoft Visual Studio\Installer"'
        "set `"TMP=$BauTmp`""
        "set `"TEMP=$BauTmp`""
        # sonst startet vcvars einen Telemetrie-Sender (Netz)
        'set "VSCMD_SKIP_SENDTELEMETRY=1"'
        "call `"$Vcvars`" >nul"
        'if errorlevel 1 exit /b 90'
    )
    ($kopf + $Zeilen + @('exit /b 0')) | Set-Content -Path $datei -Encoding ascii
    & $Cmd /d /c $datei *> $Log
    return $LASTEXITCODE
}

$uhr = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------- 1. Voraussetzungen
Schritt 'Voraussetzungen pruefen'
foreach ($p in @($Quelle, "$Quelle\src\denoise.c", "$Quelle\COPYING", $Eigen, "$Eigen\CMakeLists.txt",
                 "$Eigen\rnnoise.def", "$Eigen\rauchtest.c", $ModellTgz, $Vcvars, $CMake, $Ninja, $Tar, $Cmd)) {
    if (-not (Test-Path -LiteralPath $p)) { Abbruch "fehlt: $p" }
}
$ist = (Get-FileHash -LiteralPath $ModellTgz -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ist -ne $ModellHash) { Abbruch "Modellarchiv SHA-256 $ist statt $ModellHash" }
Write-Host "Modellarchiv SHA-256 passt: $ist"
$modellVersion = (Get-Content -LiteralPath "$Quelle\model_version" -Raw).Trim()
if ($modellVersion -ne $ModellHash) {
    Write-Host "WARNUNG: model_version in rnnoise-main ($modellVersion) passt nicht zum Archiv" -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 2. Bauordner neu
Schritt 'Bauordner neu anlegen'
$bauVoll = [System.IO.Path]::GetFullPath($Bau)
if ($bauVoll -ne 'C:\Claude\work\JG-TacRelay\native\bau\rnnoise') { Abbruch "unerwarteter Bauordner $bauVoll" }
if (Test-Path -LiteralPath $Bau) { Remove-Item -LiteralPath $Bau -Recurse -Force }
New-Item -ItemType Directory -Force -Path $BauCmake, $BauModell, $BauTmp | Out-Null
Write-Host $Bau

# ---------------------------------------------------------------- 3. Modell entpacken
Schritt 'Modellgewichte entpacken (Standardmodell, nicht "little")'
& $Tar -xzf $ModellTgz -C $BauModell --strip-components 1 src/rnnoise_data.c src/rnnoise_data.h
if ($LASTEXITCODE -ne 0) { Abbruch "tar Exitcode $LASTEXITCODE" }
Get-ChildItem -LiteralPath $BauModell | ForEach-Object { Write-Host ("{0,-16} {1,12:N0} Bytes" -f $_.Name, $_.Length) }

# ---------------------------------------------------------------- 4. Konfigurieren und bauen
Schritt 'CMake konfigurieren und bauen (Ninja, Release, /O2, /MT)'
$rtcd  = if ($OhneX86Rtcd) { 'OFF' } else { 'ON' }
$rauch = if ($OhneRauchtest) { 'OFF' } else { 'ON' }
$bauLog = "$Bau\bau.log"
$code = Invoke-VcBefehl 'bauen' @(
    "`"$CMake`" -S `"$Eigen`" -B `"$BauCmake`" -G Ninja -DCMAKE_MAKE_PROGRAM=`"$Ninja`" -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=cl -DRNNOISE_QUELLE=`"$Quelle`" -DRNNOISE_MODELL=`"$BauModell`" -DRNNOISE_X86_RTCD=$rtcd -DRNNOISE_RAUCHTEST=$rauch"
    'if errorlevel 1 exit /b 91'
    "`"$CMake`" --build `"$BauCmake`" --config Release"
    'if errorlevel 1 exit /b 92'
) $bauLog
if ($code -ne 0) {
    Get-Content -LiteralPath $bauLog -Tail 40 | ForEach-Object { Write-Host $_ }
    Abbruch "Bau fehlgeschlagen (Exitcode $code), Protokoll: $bauLog"
}
$warnungen = @(Select-String -LiteralPath $bauLog -Pattern 'warning [A-Z]+\d+' -AllMatches)
Write-Host "Bau erfolgreich, $($warnungen.Count) Warnungszeilen, Protokoll: $bauLog"

$dllBau = "$BauCmake\rnnoise.dll"
if (-not (Test-Path -LiteralPath $dllBau)) { Abbruch "DLL nicht erzeugt: $dllBau" }

# ---------------------------------------------------------------- 5. DLL kopieren, Hash
Schritt 'DLL nach out\win-x64 kopieren'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Copy-Item -LiteralPath $dllBau -Destination $DllOut -Force
$hash = (Get-FileHash -LiteralPath $DllOut -Algorithm SHA256).Hash.ToLowerInvariant()
$hashBau = (Get-FileHash -LiteralPath $dllBau -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $hashBau) { Abbruch 'Kopie weicht vom Bauergebnis ab' }
"$hash *rnnoise.dll" | Set-Content -LiteralPath "$DllOut.sha256" -Encoding ascii
$groesse = (Get-Item -LiteralPath $DllOut).Length
Write-Host ("{0} ({1:N0} Bytes)" -f $DllOut, $groesse)

# ---------------------------------------------------------------- 6. dumpbin
Schritt 'dumpbin /exports und /dependents'
$expTxt = "$Bau\exports.txt"
$depTxt = "$Bau\dependents.txt"
$code = Invoke-VcBefehl 'dumpbin' @(
    "dumpbin /nologo /exports `"$DllOut`" > `"$expTxt`""
    'if errorlevel 1 exit /b 93'
    "dumpbin /nologo /dependents /headers `"$DllOut`" > `"$depTxt`""
    'if errorlevel 1 exit /b 94'
) "$Bau\dumpbin.log"
if ($code -ne 0) { Abbruch "dumpbin fehlgeschlagen (Exitcode $code)" }

$exporte = @(Get-Content -LiteralPath $expTxt |
    ForEach-Object { if ($_ -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]{8}\s+(\S+)') { $Matches[1] } })
Write-Host "Exporte ($($exporte.Count)): $($exporte -join ', ')"
$fehlend = @($Pflichtexporte | Where-Object { $exporte -notcontains $_ })
if ($fehlend.Count -gt 0) { Abbruch "Pflichtexporte fehlen: $($fehlend -join ', ')" }
Write-Host "Pflichtexporte vorhanden: $($Pflichtexporte -join ', ')"

$depZeilen = Get-Content -LiteralPath $depTxt
$abhaengig = @($depZeilen | Where-Object { $_ -match '^\s+(\S+\.dll)\s*$' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
Write-Host "Abhaengigkeiten: $($abhaengig -join ', ')"
$crt = @($abhaengig | Where-Object { $_ -match '(?i)^(vcruntime|msvcp|ucrtbase|api-ms-win-crt)' })
if ($crt.Count -gt 0) { Abbruch "DLL haengt an der VC-Laufzeit: $($crt -join ', ')" }
$maschine = ($depZeilen | Where-Object { $_ -match 'machine \(' } | Select-Object -First 1)
if ($maschine) { Write-Host "Zielmaschine: $($maschine.Trim())" }

# ---------------------------------------------------------------- 7. Lizenzen
Schritt 'Lizenz- und Herkunftsdateien'
if (Test-Path -LiteralPath $LizenzDir) { Remove-Item -LiteralPath $LizenzDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $LizenzDir | Out-Null
Copy-Item -LiteralPath "$Quelle\COPYING", "$Quelle\AUTHORS", "$Quelle\datasets.txt" -Destination $LizenzDir -Force
$quellHash = if (Test-Path -LiteralPath $QuellTgz) { (Get-FileHash -LiteralPath $QuellTgz -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'nicht vorhanden' }
@(
    'rnnoise.dll - Herkunft der Bestandteile'
    ''
    'Quellcode'
    '  Projekt:  RNNoise (Xiph.Org), Snapshot des Zweigs main'
    '  Quelle:   https://gitlab.xiph.org/xiph/rnnoise/-/archive/main/rnnoise-main.tar.gz'
    "  Archiv:   rnnoise-main.tar.gz, SHA-256 $quellHash"
    '  Lizenz:   BSD-3-Clause, siehe COPYING (enthaelt auch kiss_fft, Mark Borgerding,'
    '            und die x86-Dateien, Cisco/Mozilla/Amazon, alle unter denselben Bedingungen)'
    ''
    'Modellgewichte (in die DLL einkompiliert)'
    '  Datei:    src/rnnoise_data.c und src/rnnoise_data.h (Standardmodell, nicht "little")'
    '  Quelle:   https://media.xiph.org/rnnoise/models/rnnoise_data-' + $ModellHash + '.tar.gz'
    "  Archiv:   SHA-256 $ModellHash (beim Bau geprueft)"
    "  model_version in rnnoise-main: $modellVersion"
    '  Lizenz:   Das Modellarchiv enthaelt KEINE eigene Lizenzdatei. Die Gewichte werden'
    '            vom RNNoise-Projekt zusammen mit dem Quellcode verteilt; eine ausdrueckliche'
    '            Lizenzangabe fuer die Gewichte selbst liegt nicht vor -> ungeklaert, gemeldet.'
    '  Training: laut README mit oeffentlich verfuegbaren Datensaetzen, Liste in datasets.txt.'
    ''
    'Eigenbau JG TacRelay'
    '  native\eigen\rnnoise\CMakeLists.txt, rnnoise.def, rauchtest.c'
    '  Bau:      native\skripte\rnnoise.ps1 (MSVC x64, /O2, /MT, /Brepro)'
    "  DLL:      rnnoise.dll, SHA-256 $hash"
) | Set-Content -LiteralPath "$LizenzDir\HERKUNFT.txt" -Encoding utf8
Get-ChildItem -LiteralPath $LizenzDir | ForEach-Object { Write-Host "  $($_.Name)" }

# ---------------------------------------------------------------- 8. Rauchtest
$rauchErgebnis = 'nicht ausgefuehrt'
if (-not $OhneRauchtest) {
    Schritt 'Rauchtest'
    $exe = "$BauCmake\rauchtest.exe"
    if (-not (Test-Path -LiteralPath $exe)) { Abbruch "Rauchtest nicht gebaut: $exe" }
    $rauchTxt = "$Bau\rauchtest.txt"
    & $exe $DllOut *> $rauchTxt
    $rauchCode = $LASTEXITCODE
    Get-Content -LiteralPath $rauchTxt | ForEach-Object { Write-Host $_ }
    if ($rauchCode -ne 0) { Abbruch "Rauchtest fehlgeschlagen (Exitcode $rauchCode)" }
    $rauchErgebnis = 'bestanden'
}

# ---------------------------------------------------------------- Zusammenfassung
Schritt 'Ergebnis'
Write-Host "DLL:        $DllOut"
Write-Host "Groesse:    $('{0:N0}' -f $groesse) Bytes"
Write-Host "SHA-256:    $hash"
Write-Host "Rauchtest:  $rauchErgebnis"
Write-Host "Dauer:      $([math]::Round($uhr.Elapsed.TotalSeconds, 1)) s"
exit 0
