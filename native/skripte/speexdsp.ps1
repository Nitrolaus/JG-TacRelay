# JG TacRelay - Maintainer: Sarevoc

<#
.SYNOPSIS
  Baut speexdsp_resampler.dll (nur der Resampler aus speexdsp 1.2.1) reproduzierbar von vorn.

.DESCRIPTION
  Ablauf:
    1. Bauordner native\bau\speexdsp und alte Ausgaben loeschen
    2. In der vcvars64-Umgebung mit CMake + Ninja konfigurieren und bauen
       (Release: /O2, statische CRT /MT, Float-Variante, SSE/SSE2, /Brepro)
    3. DLL nach native\out\win-x64 kopieren, SHA-256 daneben ablegen
    4. Lizenz nach native\out\lizenzen\speexdsp kopieren, HERKUNFT.txt schreiben
    5. dumpbin: Exporte, Abhaengigkeiten (keine VC-Redist) und Maschinentyp pruefen
    6. Rauchtest (native\eigen\speexdsp\rauchtest.c) gegen die kopierte DLL ausfuehren
    7. SHA-256 der DLL ausgeben

  Die entpackten Quellen werden nur gelesen. Ausfuehrliche Protokolle landen in
  native\bau\speexdsp\logs, auf der Konsole steht nur die Zusammenfassung.

.PARAMETER OhneSse
  Baut die reine C-Fassung ohne SSE-Innenschleifen (zum Vergleich).

.PARAMETER OhneRauchtest
  Ueberspringt Bau und Lauf des Rauchtests.

.EXAMPLE
  pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\native\skripte\speexdsp.ps1
#>
[CmdletBinding()]
param(
    [switch]$OhneSse,
    [switch]$OhneRauchtest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ------------------------------------------------------------------ Pfade
$native = Split-Path -Parent $PSScriptRoot
$quelle = Join-Path $native 'quellen\speexdsp-1.2.1'
$eigen  = Join-Path $native 'eigen\speexdsp'
$bau    = Join-Path $native 'bau\speexdsp'
$cmBau  = Join-Path $bau 'cmake'
$logs   = Join-Path $bau 'logs'
$ausDir = Join-Path $native 'out\win-x64'
$lizDir = Join-Path $native 'out\lizenzen\speexdsp'
$archiv = 'C:\Claude\data\tacrelay-quellen\speexdsp-1.2.1.tar.gz'

$vs     = 'C:\Program Files\Microsoft Visual Studio\18\Community'
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$cmake  = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$ninja  = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'

$dllName = 'speexdsp_resampler.dll'
$dllAus  = Join-Path $ausDir $dllName
$pflichtExporte = @(
    'speex_resampler_init',
    'speex_resampler_destroy',
    'speex_resampler_process_float',
    'speex_resampler_process_interleaved_float',
    'speex_resampler_set_rate_frac',
    'speex_resampler_get_input_latency',
    'speex_resampler_get_output_latency',
    'speex_resampler_reset_mem',
    'speex_resampler_skip_zeros'
)

function Schritt([string]$Text) { Write-Host "== $Text" -ForegroundColor Cyan }

foreach ($p in @($vcvars, $cmake, $ninja, (Join-Path $quelle 'libspeexdsp\resample.c'),
                 (Join-Path $eigen 'CMakeLists.txt'), (Join-Path $eigen 'config.h'),
                 (Join-Path $eigen 'speexdsp_resampler.def'), (Join-Path $eigen 'rauchtest.c'))) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Fehlt: $p" }
}

# Sicherung gegen Tippfehler: geloescht wird nur native\bau\speexdsp.
if ($bau -ne (Join-Path (Join-Path $native 'bau') 'speexdsp')) { throw "Unerwarteter Bauordner: $bau" }

# ------------------------------------------------- Hilfe: cmd in vcvars64
# Jeder Schritt wird als .cmd-Datei im Bauordner abgelegt und ueber cmd ausgefuehrt.
# Die Ausgabe geht in logs\<Name>.log, nicht auf die Konsole.
# Mit -EigeneUmleitung leiten die Befehle ihre Ausgabe selbst um.
function Invoke-VcSchritt([string]$Name, [string[]]$Befehle, [switch]$EigeneUmleitung) {
    $cmdDatei = Join-Path $bau "$Name.cmd"
    $log      = Join-Path $logs "$Name.log"
    # PATH: Der geerbte PATH ist hier laenger als die 8191 Zeichen, die eine cmd-Zeile
    # fassen kann; vcvars haengt noch mehr davor und zerlegt ihn dabei (dann fehlen reg.exe,
    # das Windows SDK, rc.exe und mt.exe). Deshalb startet jeder Schritt mit einem schlanken
    # System-PATH - das macht den Bau zugleich unabhaengig vom Benutzerprofil. Gilt nur in
    # diesem cmd-Kindprozess.
    # VSCMD_SKIP_SENDTELEMETRY: sonst startet vcvars einen Hintergrundprozess, der den
    # umgeleiteten Handle erbt und die Protokolldatei fuer die folgenden Befehle sperrt.
    $zeilen = @(
        '@echo off',
        'setlocal',
        'set "PATH=%SystemRoot%\system32;%SystemRoot%;%SystemRoot%\System32\Wbem;%SystemRoot%\System32\WindowsPowerShell\v1.0\"',
        'set "VSCMD_SKIP_SENDTELEMETRY=1"',
        # Zwischendateien von cl und link in den Bauordner, nicht ins Benutzerprofil
        "set `"TMP=$(Join-Path $bau 'tmp')`"",
        "set `"TEMP=$(Join-Path $bau 'tmp')`"",
        "set `"LOG=$log`"",
        "call `"$vcvars`" >`"$(Join-Path $logs "$Name.vcvars.log")`" 2>&1 || exit /b 90",
        'if not defined VCToolsVersion exit /b 91',
        'echo VCToolsVersion=%VCToolsVersion% >"%LOG%"'
    )
    foreach ($b in $Befehle) {
        if ($EigeneUmleitung) { $zeilen += "$b || exit /b 1" }
        else { $zeilen += "$b >>`"%LOG%`" 2>&1 || exit /b 1" }
    }
    $zeilen += 'exit /b 0'
    Set-Content -LiteralPath $cmdDatei -Value $zeilen -Encoding ascii
    & $cmdDatei | Out-Host
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Host "--- letzte Zeilen aus $log ---" -ForegroundColor Yellow
        Get-Content -LiteralPath $log -Tail 40 | ForEach-Object { Write-Host $_ }
        throw "Schritt '$Name' fehlgeschlagen (Exitcode $code). Protokoll: $log"
    }
    return $log
}

# ------------------------------------------------------- 1. Aufraeumen
Schritt 'Bauordner und alte Ausgaben entfernen'
if (Test-Path -LiteralPath $bau) { Remove-Item -LiteralPath $bau -Recurse -Force }
foreach ($alt in @($dllAus, "$dllAus.sha256")) {
    if (Test-Path -LiteralPath $alt) { Remove-Item -LiteralPath $alt -Force }
}
if (Test-Path -LiteralPath $lizDir) { Remove-Item -LiteralPath $lizDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $bau, $cmBau, $logs, (Join-Path $bau 'tmp'), $ausDir, $lizDir | Out-Null

# ------------------------------------------- 2. Konfigurieren und bauen
$vorw = { param($p) $p.Replace('\', '/') }   # CMake-Pfade mit Schraegstrich
$sse  = if ($OhneSse) { 'OFF' } else { 'ON' }
$rt   = if ($OhneRauchtest) { 'OFF' } else { 'ON' }

Schritt "Konfigurieren (Ninja, Release, SSE=$sse)"
$logKonf = Invoke-VcSchritt 'konfigurieren' @(
    "`"$cmake`" -S `"$(& $vorw $eigen)`" -B `"$(& $vorw $cmBau)`" -G Ninja" +
    " -DCMAKE_BUILD_TYPE=Release" +
    " `"-DCMAKE_MAKE_PROGRAM=$(& $vorw $ninja)`"" +
    " `"-DSPEEXDSP_QUELLE=$(& $vorw $quelle)`"" +
    " -DKF_SPEEX_SSE=$sse -DKF_RAUCHTEST=$rt"
)
$treffer = Select-String -LiteralPath $logKonf -Pattern 'The C compiler identification is (.+)$' | Select-Object -First 1
$compiler = if ($treffer) { $treffer.Matches[0].Groups[1].Value.Trim() } else { 'unbekannt' }
Write-Host "   Compiler: $compiler"

Schritt 'Bauen'
$logBau = Invoke-VcSchritt 'bauen' @("`"$cmake`" --build `"$(& $vorw $cmBau)`" --verbose")

# Schalter im tatsaechlichen Compileraufruf fuer resample.c nachweisen
$zeileCl = Select-String -LiteralPath $logBau -Pattern 'cl\.exe.*resample\.c' | Select-Object -First 1
if (-not $zeileCl) { throw 'Compileraufruf fuer resample.c nicht im Protokoll gefunden.' }
# CMake schreibt Schalter teils mit '-' (z. B. -MT, -DHAVE_CONFIG_H); cl nimmt beide Formen.
foreach ($s in @('O2', 'MT', 'DHAVE_CONFIG_H', 'Brepro')) {
    if ($zeileCl.Line -notmatch ('\s[/-]' + [regex]::Escape($s) + '(\s|$)')) {
        throw "Schalter /$s fehlt im Compileraufruf fuer resample.c"
    }
}
if ($zeileCl.Line -match '\s[/-]MDd?(\s|$)') { throw 'resample.c wurde mit /MD statt /MT uebersetzt.' }
if ($OhneSse -and $zeileCl.Line -notmatch '\s[/-]DKF_SPEEX_OHNE_SSE(\s|$)') { throw 'SSE-Abschaltung fehlt im Compileraufruf.' }
Write-Host '   resample.c uebersetzt mit /O2 /MT /DHAVE_CONFIG_H /Brepro (im Compileraufruf nachgewiesen)'
$warnungen = @(Select-String -LiteralPath $logBau -Pattern 'warning C\d+' | ForEach-Object {
    ($_.Line -replace '^.*[\\/]', '') })
Write-Host ("   Compilerwarnungen: {0}" -f $warnungen.Count)
$warnungen | Select-Object -Unique | ForEach-Object { Write-Host "     $_" }

$dllBau = Join-Path $cmBau $dllName
if (-not (Test-Path -LiteralPath $dllBau)) { throw "DLL nicht erzeugt: $dllBau" }

# ------------------------------------------------------- 3. DLL kopieren
Schritt 'DLL kopieren'
Copy-Item -LiteralPath $dllBau -Destination $dllAus -Force
$hash = (Get-FileHash -LiteralPath $dllAus -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$dllAus.sha256" -Value "$hash *$dllName" -Encoding ascii
Write-Host "   $dllAus ($((Get-Item -LiteralPath $dllAus).Length) Bytes)"

# ---------------------------------------------------- 4. Lizenzdateien
Schritt 'Lizenzdateien ablegen'
Copy-Item -LiteralPath (Join-Path $quelle 'COPYING') -Destination (Join-Path $lizDir 'COPYING') -Force
Copy-Item -LiteralPath (Join-Path $quelle 'AUTHORS') -Destination (Join-Path $lizDir 'AUTHORS') -Force
# resample.c traegt einen eigenen Lizenzkopf (BSD, "name of the author"), der von COPYING abweicht.
$quelltext = Get-Content -LiteralPath (Join-Path $quelle 'libspeexdsp\resample.c') -Raw
$kopf = [regex]::Match($quelltext, '(?s)^/\*.*?\*/').Value
if (-not $kopf) { throw 'Lizenzkopf in resample.c nicht gefunden.' }
Set-Content -LiteralPath (Join-Path $lizDir 'resample.c-Lizenzkopf.txt') -Value $kopf -Encoding ascii
$archivHash = if (Test-Path -LiteralPath $archiv) {
    (Get-FileHash -LiteralPath $archiv -Algorithm SHA256).Hash.ToLowerInvariant()
} else { 'Archiv nicht gefunden' }
$herkunft = @(
    'speexdsp_resampler.dll - Herkunft',
    '',
    'Bibliothek:   SpeexDSP 1.2.1, nur der Resampler',
    'Quelle:       https://downloads.xiph.org/releases/speex/speexdsp-1.2.1.tar.gz',
    "Archiv:       speexdsp-1.2.1.tar.gz, SHA-256 $archivHash",
    'Lizenz:       BSD-3-Clause (Xiph.Org), siehe COPYING; resample.c zusaetzlich',
    '              mit eigenem BSD-Kopf, siehe resample.c-Lizenzkopf.txt',
    'Uebersetzt:   libspeexdsp/resample.c (unveraendert)',
    'Eingebunden:  libspeexdsp/arch.h, libspeexdsp/os_support.h, libspeexdsp/resample_sse.h,',
    '              include/speex/speex_resampler.h, include/speex/speexdsp_types.h',
    'Eigen:        native/eigen/speexdsp/config.h, speexdsp_resampler.def, CMakeLists.txt',
    "Bau:          $compiler, x64, Release /O2, statische CRT /MT, FLOATING_POINT, SSE=$sse, /Brepro",
    "DLL SHA-256:  $hash"
)
Set-Content -LiteralPath (Join-Path $lizDir 'HERKUNFT.txt') -Value $herkunft -Encoding ascii
Get-ChildItem -LiteralPath $lizDir | ForEach-Object { Write-Host "   $($_.Name)" }

# -------------------------------------------------- 5. dumpbin-Pruefung
Schritt 'dumpbin: Exporte, Abhaengigkeiten, Maschinentyp'
$logExp = Join-Path $logs 'exports.txt'
$logDep = Join-Path $logs 'dependents.txt'
$logHdr = Join-Path $logs 'headers.txt'
Invoke-VcSchritt 'dumpbin' -EigeneUmleitung @(
    "dumpbin /nologo /exports `"$dllAus`" >`"$logExp`" 2>&1",
    "dumpbin /nologo /dependents `"$dllAus`" >`"$logDep`" 2>&1",
    "dumpbin /nologo /headers `"$dllAus`" >`"$logHdr`" 2>&1"
) | Out-Null

$exporte = @(Select-String -LiteralPath $logExp -Pattern '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]{8}\s+(\S+)' |
             ForEach-Object { $_.Matches.Groups[1].Value })
$fehlend = @($pflichtExporte | Where-Object { $exporte -notcontains $_ })
Write-Host ("   Exporte gesamt: {0}, davon Pflicht: {1}/{2}" -f $exporte.Count,
            ($pflichtExporte.Count - $fehlend.Count), $pflichtExporte.Count)
foreach ($e in $pflichtExporte) {
    Write-Host ("     {0} {1}" -f $(if ($exporte -contains $e) { 'ok  ' } else { 'FEHLT' }), $e)
}
if ($fehlend.Count) { throw "Pflichtexporte fehlen: $($fehlend -join ', ')" }

$abh = @(Select-String -LiteralPath $logDep -Pattern '^\s+(\S+\.dll)\s*$' |
         ForEach-Object { $_.Matches.Groups[1].Value })
Write-Host "   Abhaengigkeiten: $($abh -join ', ')"
$redist = @($abh | Where-Object { $_ -match '^(vcruntime|msvcp|ucrtbase|api-ms-win-crt)' })
if ($redist.Count) { throw "DLL haengt an der VC-Laufzeit: $($redist -join ', ')" }

if (-not (Select-String -LiteralPath $logHdr -Pattern 'machine \(x64\)' -Quiet)) {
    throw 'DLL ist kein x64-Abbild.'
}
Write-Host '   Maschinentyp: x64'

# ------------------------------------------------------- 6. Rauchtest
if (-not $OhneRauchtest) {
    Schritt 'Rauchtest gegen die kopierte DLL'
    $exe = Join-Path $cmBau 'rauchtest.exe'
    $logRt = Join-Path $logs 'rauchtest.txt'
    $ausgabe = & $exe $dllAus
    $rtCode = $LASTEXITCODE
    $ausgabe | Set-Content -LiteralPath $logRt -Encoding utf8
    $ausgabe | ForEach-Object { Write-Host "   $_" }
    if ($rtCode -ne 0) { throw "Rauchtest fehlgeschlagen (Exitcode $rtCode)." }
}

# ---------------------------------------------------------- 7. Ergebnis
Schritt 'Fertig'
Write-Host "   DLL:     $dllAus"
Write-Host "   SHA-256: $hash"
