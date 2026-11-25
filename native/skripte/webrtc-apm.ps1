# JG TacRelay - Maintainer: Sarevoc

<#
    webrtc-apm.ps1 - baut webrtc_apm.dll (WebRTC Audio Processing Module 2.1 + eigene C-Schicht)
    reproduzierbar von vorn. JG TacRelay, x64, MSVC, Release /O2, statische CRT (/MT).

    Ablauf
      1. native\bau\webrtc-apm\ loeschen und neu anlegen
      2. Meson-Quellbaum zusammenstellen (nur Kopien, die Upstream-Quellen bleiben unberuehrt):
           bau\webrtc-apm\quelle\                     <- eigen\webrtc-apm\ (meson.build, apm_c.*, rtc_export_statisch.h)
           bau\webrtc-apm\quelle\subprojects\webrtc-audio-processing\  <- quellen\webrtc-audio-processing-2.1\
           bau\webrtc-apm\quelle\subprojects\abseil-cpp.wrap + packagecache\  (abseil 20240722.0 + wrapdb-Patch)
      3. MSVC-Umgebung aus vcvars64.bat uebernehmen (mit kurzem PATH, siehe unten)
      4. meson setup (ninja, --wrap-mode=nodownload, release, static, b_vscrt=mt) und meson compile
      5. DLL nach native\out\win-x64\ kopieren, Exporte und Abhaengigkeiten mit dumpbin pruefen
      6. Rauchtest (eigen\webrtc-apm\rauchtest.c) bauen und gegen die kopierte DLL laufen lassen
      7. Lizenzdateien nach native\out\lizenzen\webrtc-apm\, SHA-256 der DLL ausgeben

    Aufruf
      pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\native\skripte\webrtc-apm.ps1
      pwsh -NoProfile -File ...\webrtc-apm.ps1 -Sekunden 5      # laengerer Echotest
      pwsh -NoProfile -File ...\webrtc-apm.ps1 -OhneRauchtest

    Pfade (seit 29.09.2026, Modul aec3): native\ ist der Ordner ueber diesem Skript, also die jeweilige
    Arbeitskopie. -Quellen, -Bau und -Ausgabe lenken Upstream-Quellen, Bauordner und DLL um, etwa fuer
    einen Bau in einer Arbeitskopie, der den Ausgabeordner von main nicht anfassen darf:
      pwsh -NoProfile -File ...\webrtc-apm.ps1 -Quellen C:\Claude\tmp\tr-aec3\quellen\webrtc-audio-processing-2.1 `
           -Bau C:\Claude\tmp\tr-aec3\bau\webrtc-apm
    Der Bauordner wird geloescht und muss deshalb unter native\bau\ oder unter C:\Claude\tmp\ liegen.

    Hinweis PATH: Der geerbte PATH dieser Maschine ist ueber 8191 Zeichen lang. cmd.exe wertet ihn
    dann nicht mehr aus, vcvars64.bat findet weder reg.exe noch vswhere.exe und laesst das Windows
    SDK weg (kein stdio.h). Das Skript setzt deshalb fuer seinen eigenen Prozess einen kurzen PATH.
    Am System wird nichts veraendert.

    Netz: keines. Meson laeuft mit --wrap-mode=nodownload und --force-fallback-for=absl_*,
    abseil kommt ausschliesslich aus subprojects\packagecache (Hash prueft Meson gegen das .wrap).
#>
[CmdletBinding()]
param(
    [double]$Sekunden = 1.0,
    [switch]$OhneRauchtest,
    [string]$Quellen = (Join-Path (Split-Path -Parent $PSScriptRoot) 'quellen\webrtc-audio-processing-2.1'),
    [string]$Bau = (Join-Path (Split-Path -Parent $PSScriptRoot) 'bau\webrtc-apm'),
    [string]$Ausgabe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'out\win-x64')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Pfade -------------------------------------------------------------------------------------
$Native    = Split-Path -Parent $PSScriptRoot
$Upstream  = [IO.Path]::GetFullPath($Quellen)
$Eigen     = Join-Path $Native 'eigen\webrtc-apm'
$Bau       = [IO.Path]::GetFullPath($Bau)
$Quelle    = Join-Path $Bau 'quelle'
$BauDir    = Join-Path $Bau 'build'
$Logs      = Join-Path $Bau 'logs'
$Tmp       = Join-Path $Bau 'tmp'
$TestDir   = Join-Path $Bau 'rauchtest'
$Out       = [IO.Path]::GetFullPath($Ausgabe)
$Lizenzen  = Join-Path (Split-Path -Parent $Out) 'lizenzen\webrtc-apm'
$DllName   = 'webrtc_apm.dll'

$Meson     = 'C:\Claude\tools\meson\.venv\Scripts\meson.exe'
$VsRoot    = 'C:\Program Files\Microsoft Visual Studio\18\Community'
$VcVars    = Join-Path $VsRoot 'VC\Auxiliary\Build\vcvars64.bat'
$NinjaDir  = Join-Path $VsRoot 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja'
$AbseilVer = 'abseil-cpp-20240722.0'

$ErwarteteExporte = @(
    'apm_aec3_configure', 'apm_aec3_describe',
    'apm_configure', 'apm_create', 'apm_destroy', 'apm_get_stats',
    'apm_process_capture', 'apm_process_render', 'apm_set_stream_delay_ms', 'apm_version'
) | Sort-Object

function Schritt([string]$Text) { Write-Host ""; Write-Host "== $Text" -ForegroundColor Cyan }

function Pruefe-Pfad([string]$Pfad, [string]$Was) {
    if (-not (Test-Path -LiteralPath $Pfad)) { throw "$Was fehlt: $Pfad" }
}

# Fuehrt ein Programm aus, schreibt stdout+stderr in eine Logdatei und bricht bei Fehler ab.
function Ausfuehren([string]$Log, [string]$Programm, [string[]]$Argumente) {
    $logPfad = Join-Path $Logs $Log
    "> $Programm $($Argumente -join ' ')" | Out-File -LiteralPath $logPfad -Encoding utf8
    & $Programm @Argumente *>&1 | Out-File -LiteralPath $logPfad -Encoding utf8 -Append
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Host "Fehler (Exitcode $code). Letzte Zeilen aus $logPfad :" -ForegroundColor Red
        Get-Content -LiteralPath $logPfad -Tail 40 | ForEach-Object { Write-Host "  $_" }
        throw "$Programm scheiterte mit Exitcode $code"
    }
    return $logPfad
}

# --- 0. Voraussetzungen ------------------------------------------------------------------------
Schritt 'Voraussetzungen'
Pruefe-Pfad $Upstream 'Upstream-Quellen'
Pruefe-Pfad (Join-Path $Upstream 'meson.build') 'Upstream meson.build'
Pruefe-Pfad (Join-Path $Upstream "subprojects\abseil-cpp.wrap") 'abseil-cpp.wrap'
Pruefe-Pfad (Join-Path $Upstream "subprojects\packagecache\$AbseilVer.tar.gz") 'abseil-Archiv im packagecache'
Pruefe-Pfad (Join-Path $Upstream 'subprojects\packagecache\abseil-cpp_20240722.0-3_patch.zip') 'abseil-Patch im packagecache'
foreach ($f in 'meson.build', 'apm_c.cpp', 'apm_c.h', 'rtc_export_statisch.h', 'rauchtest.c') {
    Pruefe-Pfad (Join-Path $Eigen $f) "eigene Datei $f"
}
Pruefe-Pfad $Meson 'Meson'
Pruefe-Pfad $VcVars 'vcvars64.bat'
Pruefe-Pfad (Join-Path $NinjaDir 'ninja.exe') 'ninja.exe'
Write-Host "  Meson $(& $Meson --version)"

# --- 1. Bauordner frisch -----------------------------------------------------------------------
Schritt 'Bauordner neu anlegen'
$bauVoll = [IO.Path]::GetFullPath($Bau)
if (-not ($bauVoll.StartsWith((Join-Path $Native 'bau\'), [StringComparison]::OrdinalIgnoreCase) -or
          $bauVoll.StartsWith('C:\Claude\tmp\', [StringComparison]::OrdinalIgnoreCase))) {
    throw "Sicherheitsabbruch: Bauordner liegt weder unter native\bau\ noch unter C:\Claude\tmp\: $bauVoll"
}
if (Test-Path -LiteralPath $Bau) { Remove-Item -LiteralPath $Bau -Recurse -Force }
foreach ($d in $Bau, $Quelle, $Logs, $Tmp, $TestDir, $Out, $Lizenzen, (Join-Path $Quelle 'subprojects')) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}
Write-Host "  $Bau"

# --- 2. Quellbaum zusammenstellen --------------------------------------------------------------
Schritt 'Meson-Quellbaum zusammenstellen (Kopien)'
foreach ($f in 'meson.build', 'apm_c.cpp', 'apm_c.h', 'rtc_export_statisch.h') {
    Copy-Item -LiteralPath (Join-Path $Eigen $f) -Destination $Quelle
}
$ziel = Join-Path $Quelle 'subprojects\webrtc-audio-processing'
Copy-Item -LiteralPath $Upstream -Destination $ziel -Recurse
# Wrap und packagecache auch auf oberster Ebene, damit abseil eindeutig aus dem Cache kommt
Copy-Item -LiteralPath (Join-Path $Upstream 'subprojects\abseil-cpp.wrap') -Destination (Join-Path $Quelle 'subprojects')
Copy-Item -LiteralPath (Join-Path $Upstream 'subprojects\packagecache') -Destination (Join-Path $Quelle 'subprojects\packagecache') -Recurse
$anzahl = (Get-ChildItem -LiteralPath $ziel -Recurse -File).Count
Write-Host "  Upstream kopiert: $anzahl Dateien"

# --- 3. MSVC-Umgebung --------------------------------------------------------------------------
Schritt 'MSVC-Umgebung (vcvars64.bat)'
$env:PATH = @(
    "$env:SystemRoot\system32", $env:SystemRoot, "$env:SystemRoot\System32\Wbem",
    "$env:SystemRoot\System32\WindowsPowerShell\v1.0",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
) -join ';'
# sonst startet vcvars einen Telemetrie-Sender (Netz); TMP/TEMP schon fuer vcvars in den Bauordner
$env:VSCMD_SKIP_SENDTELEMETRY = '1'
$env:TMP = $Tmp; $env:TEMP = $Tmp
$vcZeilen = cmd /c "`"$VcVars`" >nul 2>&1 && set"
if ($LASTEXITCODE -ne 0) { throw "vcvars64.bat scheiterte ($LASTEXITCODE)" }
foreach ($z in $vcZeilen) {
    $i = $z.IndexOf('=')
    if ($i -gt 0) { [Environment]::SetEnvironmentVariable($z.Substring(0, $i), $z.Substring($i + 1)) }
}
if (-not $env:WindowsSDKVersion -or $env:WindowsSDKVersion -eq '\') { throw 'vcvars64.bat hat kein Windows SDK gefunden' }
$env:PATH = "$NinjaDir;$env:PATH"
$env:TMP = $Tmp; $env:TEMP = $Tmp
$env:CC = 'cl'; $env:CXX = 'cl'
$env:PYTHONDONTWRITEBYTECODE = '1'
$clZeile = (cmd /c 'cl 2>&1' | Select-Object -First 1)
Write-Host "  $clZeile"
Write-Host "  Windows SDK $($env:WindowsSDKVersion.TrimEnd('\')), VC-Tools $($env:VCToolsVersion)"

# --- 4. Meson ----------------------------------------------------------------------------------
Schritt 'meson setup'
$fallback = 'absl_base,absl_flags,absl_strings,absl_numeric,absl_synchronization,absl_bad_optional_access'
$setupArgs = @(
    'setup', $BauDir, $Quelle,
    '--backend=ninja',
    '--wrap-mode=nodownload',
    "--force-fallback-for=$fallback",
    '--buildtype=release',
    # C++20 ueberall: webrtc braucht es unter MSVC (C7555, siehe eigen\webrtc-apm\meson.build),
    # abseil verlangt denselben Standard wie sein Nutzer; die Subprojekte bringen sonst c++17 mit.
    '-Dcpp_std=c++20',
    '-Dwebrtc-audio-processing:cpp_std=c++20',
    '-Dabseil-cpp:cpp_std=c++20',
    '-Ddefault_library=static',
    '-Db_vscrt=mt',
    '-Db_ndebug=true'
)
$log = Ausfuehren 'meson-setup.log' $Meson $setupArgs
Select-String -LiteralPath $log -Pattern 'C\+\+ compiler for the host|Subproject .* finished|Build targets in project|Dependency absl_base' |
    ForEach-Object { Write-Host "  $($_.Line.Trim())" }

Schritt 'meson compile (nur Ziel webrtc_apm und seine Abhaengigkeiten)'
$uhr = [Diagnostics.Stopwatch]::StartNew()
$log = Ausfuehren 'meson-compile.log' $Meson @('compile', '-C', $BauDir, 'webrtc_apm')
$uhr.Stop()
$warnungen = @(Select-String -LiteralPath $log -Pattern 'warning C\d+' ).Count
Write-Host ("  fertig in {0:N0} s, {1} Compiler-Warnungen (Details in {2})" -f $uhr.Elapsed.TotalSeconds, $warnungen, $log)

# Stichprobe: wurden /O2 und /MT wirklich benutzt?
$ninjaDatei = Join-Path $BauDir 'build.ninja'
$flags = Select-String -LiteralPath $ninjaDatei -Pattern '"/MT"|/MT\b' -List
if (-not $flags) { throw '/MT nicht in build.ninja gefunden' }
if (-not (Select-String -LiteralPath $ninjaDatei -Pattern '/O2' -List)) { throw '/O2 nicht in build.ninja gefunden' }
if (Select-String -LiteralPath $ninjaDatei -Pattern '/std:c\+\+1[147]' -List) { throw 'build.ninja enthaelt noch einen C++-Standard unter c++20' }
if (Select-String -LiteralPath $ninjaDatei -Pattern '"/MD"|/MDd' -List) { throw 'build.ninja enthaelt /MD (dynamische CRT)' }
Write-Host '  build.ninja: /O2, /MT und /std:c++20 durchgehend'

$gebaut = Join-Path $BauDir $DllName
Pruefe-Pfad $gebaut 'gebaute DLL'

# --- 5. Kopieren und pruefen -------------------------------------------------------------------
Schritt 'DLL kopieren und mit dumpbin pruefen'
$dll = Join-Path $Out $DllName
Copy-Item -LiteralPath $gebaut -Destination $dll -Force

$exportLog = Join-Path $Logs 'dumpbin-exports.txt'
dumpbin /nologo /exports $dll | Out-File -LiteralPath $exportLog -Encoding utf8
$exporte = @(Get-Content -LiteralPath $exportLog |
    Where-Object { $_ -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]{8}\s+(\S+)' } |
    ForEach-Object { $Matches[1] }) | Sort-Object
Write-Host "  Exporte ($($exporte.Count)): $($exporte -join ', ')"
$diff = Compare-Object -ReferenceObject $ErwarteteExporte -DifferenceObject $exporte
if ($diff) {
    $diff | ForEach-Object { Write-Host "  Abweichung: $($_.InputObject) $($_.SideIndicator)" -ForegroundColor Red }
    throw "Exporttabelle weicht von den $($ErwarteteExporte.Count) apm_*-Funktionen ab"
}
Write-Host "  Exporte stimmen genau ($($ErwarteteExporte.Count) von $($ErwarteteExporte.Count), keine weiteren)"

$depLog = Join-Path $Logs 'dumpbin-dependents.txt'
dumpbin /nologo /dependents $dll | Out-File -LiteralPath $depLog -Encoding utf8
$abh = @(Get-Content -LiteralPath $depLog | Where-Object { $_ -match '^\s+(\S+\.dll)\s*$' } | ForEach-Object { $Matches[1] })
Write-Host "  Abhaengigkeiten: $($abh -join ', ')"
$crt = $abh | Where-Object { $_ -match '^(vcruntime|msvcp|ucrtbase|api-ms-win-crt)' }
if ($crt) { throw "DLL haengt an der dynamischen CRT: $($crt -join ', ')" }
Write-Host '  keine VC-Redist-Abhaengigkeit (statische CRT)'

$hdrLog = Join-Path $Logs 'dumpbin-headers.txt'
dumpbin /nologo /headers $dll | Out-File -LiteralPath $hdrLog -Encoding utf8
$maschine = (Select-String -LiteralPath $hdrLog -Pattern 'machine \((x64|x86|ARM64)\)' -List).Matches[0].Groups[1].Value
if ($maschine -ne 'x64') { throw "DLL ist nicht x64, sondern $maschine" }
Write-Host "  Maschine: $maschine, Groesse: $((Get-Item -LiteralPath $dll).Length) Bytes"

# --- 6. Rauchtest ------------------------------------------------------------------------------
if (-not $OhneRauchtest) {
    Schritt 'Rauchtest bauen und ausfuehren'
    $exe = Join-Path $TestDir 'rauchtest.exe'
    $null = Ausfuehren 'rauchtest-build.log' 'cl' @(
        '/nologo', '/O2', '/MT', '/W3', '/std:c11', "/I$Eigen",
        (Join-Path $Eigen 'rauchtest.c'),
        "/Fe$exe", "/Fo$(Join-Path $TestDir 'rauchtest.obj')"
    )
    $testLog = Join-Path $Logs 'rauchtest.txt'
    & $exe $dll ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $Sekunden)) *>&1 |
        Tee-Object -FilePath $testLog | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "Rauchtest gescheitert (Exitcode $LASTEXITCODE), siehe $testLog" }
}

# --- 7. Lizenzen und Hash ----------------------------------------------------------------------
Schritt 'Lizenzdateien'
$abseilLizenz = Join-Path $Quelle "subprojects\$AbseilVer\LICENSE"
$lizenzListe = [ordered]@{
    'webrtc-audio-processing-COPYING.txt' = Join-Path $Upstream 'COPYING'
    'webrtc-LICENSE.txt'                  = Join-Path $Upstream 'webrtc\LICENSE'
    'webrtc-PATENTS.txt'                  = Join-Path $Upstream 'webrtc\PATENTS'
    'abseil-cpp-LICENSE.txt'              = $abseilLizenz
    'pffft-LICENSE.txt'                   = Join-Path $Upstream 'webrtc\third_party\pffft\LICENSE'
    'ooura-fft-LICENSE.txt'               = Join-Path $Upstream 'webrtc\common_audio\third_party\ooura\LICENSE'
    'spl_sqrt_floor-LICENSE.txt'          = Join-Path $Upstream 'webrtc\common_audio\third_party\spl_sqrt_floor\LICENSE'
    'fft-LICENSE.txt'                     = Join-Path $Upstream 'webrtc\modules\third_party\fft\LICENSE'
    'rnnoise-COPYING.txt'                 = Join-Path $Upstream 'webrtc\third_party\rnnoise\COPYING'
}
$uebersicht = @(
    'Lizenzdateien zu webrtc_apm.dll (JG TacRelay), erzeugt von native\skripte\webrtc-apm.ps1',
    "Stand: $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
    '',
    'Datei | Herkunft | SHA-256'
)
foreach ($name in $lizenzListe.Keys) {
    $quelleDatei = $lizenzListe[$name]
    Pruefe-Pfad $quelleDatei "Lizenzdatei $name"
    Copy-Item -LiteralPath $quelleDatei -Destination (Join-Path $Lizenzen $name) -Force
    $h = (Get-FileHash -LiteralPath $quelleDatei -Algorithm SHA256).Hash.ToLowerInvariant()
    $herkunft = $quelleDatei.Replace($Quelle + '\subprojects\', "packagecache\$AbseilVer.tar.gz: ").Replace($Native + '\', 'native\')
    $uebersicht += "$name | $herkunft | $h"
    Write-Host "  $name"
}
$uebersicht | Out-File -LiteralPath (Join-Path $Lizenzen 'UEBERSICHT.txt') -Encoding utf8

Schritt 'Ergebnis'
$sha = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
# gleiche Form wie bei den anderen DLLs in out\win-x64 (sha256sum, Binaermodus)
Set-Content -LiteralPath "$dll.sha256" -Value "$sha *$DllName" -Encoding ascii
Write-Host "  DLL:     $dll"
Write-Host "  SHA-256: $sha"
Write-Host "  Logs:    $Logs"
