# JG TacRelay - Maintainer: Sarevoc

<#
.SYNOPSIS
  Baut libopus 1.6.1 als opus.dll (Windows x64, Release /O2, statische CRT) fuer JG TacRelay.

.DESCRIPTION
  Reproduzierbar von vorn, ohne Netz:
    1. Bauordner bau\opus loeschen
    2. vcvars64-Umgebung in diesen Prozess holen (wird am Ende zurueckgesetzt)
    3. CMake + Ninja konfigurieren, bauen
    4. Einstellungen aus compile_commands.json nachpruefen (/O2, /MT, AVX2 nur in den AVX2-Dateien, ...)
    5. opus.dll nach out\win-x64 kopieren, Maschine, Abhaengigkeiten und Exporte pruefen (dumpbin)
    6. Rauchtest: eigen\opus\rauchtest.c bauen und gegen die DLL laufen lassen, dazu eine .NET-Gegenprobe
    7. Lizenz nach out\lizenzen\opus, SHA-256 der DLL ausgeben

  Lange Ausgaben (CMake, Ninja, dumpbin) landen in bau\opus\logs\, auf der Konsole steht nur das Ergebnis.

  Schalter der Bibliothek:
    OPUS_BUILD_SHARED_LIBRARY=ON, OPUS_OSCE=ON, OPUS_DEEP_PLC=ON, OPUS_DRED=OFF,
    OPUS_X86_MAY_HAVE_AVX2=ON (Laufzeiterkennung), OPUS_X86_PRESUME_AVX2=OFF,
    OPUS_STATIC_RUNTIME=ON (/MT), OPUS_BUILD_TESTING=OFF, OPUS_BUILD_PROGRAMS=OFF.
  Zusaetzlich /DENABLE_OSCE_BWE=1: CMake setzt es bei OPUS_OSCE nicht, autotools (--enable-osce) schon.
  Ohne das lehnt der Decoder OPUS_SET_OSCE_BWE mit OPUS_UNIMPLEMENTED ab.
  /Brepro fuer Compiler und Linker, damit gleiche Quellen eine bitgleiche DLL ergeben.

.PARAMETER OhneOsceBwe
  Laesst ENABLE_OSCE_BWE weg (Stand der CMake-Vorlage von opus).

.PARAMETER OhneRauchtest
  Ueberspringt Schritt 6.

.EXAMPLE
  pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\native\skripte\opus.ps1
#>
[CmdletBinding()]
param(
    [switch]$OhneOsceBwe,
    [switch]$OhneRauchtest
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Pfade (alle absolut) ----------------------------------------------------
$Native    = 'C:\Claude\work\JG-TacRelay\native'
$Quelle    = Join-Path $Native 'quellen\opus-1.6.1'
$Bau       = Join-Path $Native 'bau\opus'
$CmakeBau  = Join-Path $Bau 'cmake'
$Logs      = Join-Path $Bau 'logs'
$RtBau     = Join-Path $Bau 'rauchtest'
$Ausgabe   = Join-Path $Native 'out\win-x64'
$Lizenzen  = Join-Path $Native 'out\lizenzen\opus'
$Eigen     = Join-Path $Native 'eigen\opus'
$DllZiel   = Join-Path $Ausgabe 'opus.dll'
$Archiv    = 'C:\Claude\data\tacrelay-quellen\opus-1.6.1.tar.gz'
$ArchivSoll = '6ffcb593207be92584df15b32466ed64bbec99109f007c82205f0194572411a1'

$VS        = 'C:\Program Files\Microsoft Visual Studio\18\Community'
$VcVars    = Join-Path $VS 'VC\Auxiliary\Build\vcvars64.bat'
$CMake     = Join-Path $VS 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$Ninja     = Join-Path $VS 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'

$PflichtExporte = @(
    'opus_encoder_create', 'opus_encode_float', 'opus_decoder_create', 'opus_decode_float',
    'opus_encoder_ctl', 'opus_decoder_ctl', 'opus_pcm_soft_clip', 'opus_get_version_string',
    # fuer den C#-Wrapper ebenfalls noetig
    'opus_encoder_destroy', 'opus_decoder_destroy', 'opus_strerror', 'opus_encode', 'opus_decode',
    'opus_encoder_get_size', 'opus_decoder_get_size', 'opus_packet_get_nb_samples', 'opus_packet_get_bandwidth'
)

# --- Hilfen -------------------------------------------------------------------
$script:Fehler = 0

function Schritt([string]$Text) { Write-Host ''; Write-Host "== $Text" }

function Pruefe([bool]$Ok, [string]$Name, [string]$Detail = '') {
    $zeile = if ($Ok) { 'PASS' } else { 'FAIL' }
    $zeile += "  $Name"
    if ($Detail) { $zeile += " - $Detail" }
    Write-Host $zeile
    if (-not $Ok) { $script:Fehler++ }
}

function Ausfuehren([string]$Name, [string]$Programm, [string[]]$Argumente) {
    $log = Join-Path $Logs "$Name.log"
    $alt = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Programm @Argumente *> $log
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $alt }
    if ($code -ne 0) {
        Write-Host "FEHLER: $Name endet mit Exitcode $code. Letzte Zeilen aus $($log):"
        Get-Content -LiteralPath $log -Tail 40 | ForEach-Object { Write-Host "  $_" }
        throw "$Name fehlgeschlagen"
    }
    return $log
}

function Hat-Define([string]$Befehl, [string]$Name) {
    return $Befehl -match "(^|\s)[-/]D$([regex]::Escape($Name))(=\S*)?(\s|$)"
}

# --- Umgebung sichern, damit ein Aufruf per Punkt die Sitzung nicht veraendert --
$UmgebungVorher = @{}
Get-ChildItem env: | ForEach-Object { $UmgebungVorher[$_.Name] = $_.Value }

try {
    Schritt 'Vorbedingungen'
    foreach ($p in $VcVars, $CMake, $Ninja, (Join-Path $Quelle 'CMakeLists.txt'), (Join-Path $Eigen 'rauchtest.c')) {
        if (-not (Test-Path -LiteralPath $p)) { throw "Fehlt: $p" }
    }
    $version = (Get-Content -LiteralPath (Join-Path $Quelle 'package_version') -Raw).Trim()
    Pruefe ($version -eq 'PACKAGE_VERSION="1.6.1"') 'Quellstand opus 1.6.1' $version

    # Modelldaten muessen im Quellbaum liegen - CMake laedt nichts nach, ohne sie kein Deep PLC / OSCE
    $modelle = 'plc_data.c', 'fargan_data.c', 'pitchdnn_data.c', 'lace_data.c', 'nolace_data.c', 'bbwenet_data.c'
    $fehlend = @($modelle | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Quelle "dnn\$_")) })
    $mb = ($modelle | ForEach-Object { (Get-Item -LiteralPath (Join-Path $Quelle "dnn\$_") -ErrorAction SilentlyContinue).Length } |
           Measure-Object -Sum).Sum / 1MB
    Pruefe ($fehlend.Count -eq 0) 'DNN-Modelldaten im Tarball (PLC, FARGAN, PitchDNN, LACE, NoLACE, BBWENet)' (
        '{0:N1} MB{1}' -f $mb, $(if ($fehlend) { ', fehlt: ' + ($fehlend -join ', ') } else { '' }))
    if ($fehlend) { throw 'Modelldaten fehlen' }

    if (Test-Path -LiteralPath $Archiv) {
        $ist = (Get-FileHash -LiteralPath $Archiv -Algorithm SHA256).Hash.ToLowerInvariant()
        Pruefe ($ist -eq $ArchivSoll) 'Originalarchiv opus-1.6.1.tar.gz unveraendert' $ist
    }

    Schritt 'Bauordner von vorn'
    $bauVoll = [IO.Path]::GetFullPath($Bau)
    if (-not $bauVoll.StartsWith("$Native\bau\", [StringComparison]::OrdinalIgnoreCase)) { throw "Bauordner ausserhalb von native\bau: $bauVoll" }
    if (Test-Path -LiteralPath $Bau) { Remove-Item -LiteralPath $Bau -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $CmakeBau, $Logs, $RtBau, $Ausgabe, $Lizenzen | Out-Null
    Write-Host "Bauordner: $Bau"

    Schritt 'MSVC-Umgebung (vcvars64)'
    # Schlanker PATH vor vcvars: Ist PATH laenger als 8191 Zeichen, durchsucht cmd.exe ihn nicht mehr,
    # winsdk.bat findet dann reg.exe nicht und das Windows SDK (UCRT, rc.exe, mt.exe) fehlt.
    # Nebenbei haelt das fremde Werkzeuge aus dem Nutzer-PATH aus dem Bau heraus.
    # VSCMD_SKIP_SENDTELEMETRY: sonst startet vcvars einen Telemetrie-Sender (Netz). TMP/TEMP in den
    # Bauordner, damit cl und link ihre Zwischendateien nicht im Benutzerprofil ablegen.
    $BauTmp = Join-Path $Bau 'tmp'
    New-Item -ItemType Directory -Force -Path $BauTmp | Out-Null
    $umgebungCmd = Join-Path $Bau 'umgebung.cmd'
    [IO.File]::WriteAllText($umgebungCmd, (@(
        '@echo off',
        'set "PATH=%SystemRoot%\system32;%SystemRoot%;%SystemRoot%\System32\Wbem;%SystemRoot%\System32\WindowsPowerShell\v1.0"',
        'set "VSCMD_SKIP_SENDTELEMETRY=1"',
        "set `"TMP=$BauTmp`"",
        "set `"TEMP=$BauTmp`"",
        "call `"$VcVars`" >nul 2>&1 || exit /b 1",
        'set'
    ) -join "`r`n") + "`r`n", [Text.Encoding]::ASCII)
    $zeilen = & cmd.exe /d /c $umgebungCmd
    if ($LASTEXITCODE -ne 0) { throw 'vcvars64.bat fehlgeschlagen' }
    foreach ($z in $zeilen) {
        if ($z -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process') }
    }
    $cl = (Get-Command cl.exe -ErrorAction SilentlyContinue)
    Pruefe ($null -ne $cl -and $env:VSCMD_ARG_TGT_ARCH -eq 'x64') 'cl.exe fuer x64 gefunden' $(if ($cl) { $cl.Source } else { '' })
    if (-not $cl) { throw 'cl.exe fehlt nach vcvars64' }
    $sdkOk = ($env:UCRTVersion -and $env:INCLUDE -match '\\ucrt' -and $env:LIB -match '\\um\\x64' -and (Get-Command rc.exe -ErrorAction SilentlyContinue))
    Pruefe ([bool]$sdkOk) 'Windows SDK erkannt (UCRT, um, rc.exe)' "SDK $($env:UCRTVersion), MSVC $($env:VCToolsVersion)"
    if (-not $sdkOk) { throw 'Windows SDK fehlt in der vcvars-Umgebung' }
    $dumpbin = (Get-Command dumpbin.exe).Source

    Schritt 'CMake konfigurieren'
    $cflags = '/DWIN32 /D_WINDOWS /Brepro'
    if (-not $OhneOsceBwe) { $cflags += ' /DENABLE_OSCE_BWE=1' }
    $cmakeArgs = @(
        '-G', 'Ninja',
        '-S', $Quelle,
        '-B', $CmakeBau,
        "-DCMAKE_MAKE_PROGRAM=$Ninja",
        '-DCMAKE_C_COMPILER=cl',
        '-DCMAKE_BUILD_TYPE=Release',
        "-DCMAKE_C_FLAGS=$cflags",
        '-DCMAKE_SHARED_LINKER_FLAGS=/Brepro',
        '-DCMAKE_EXPORT_COMPILE_COMMANDS=ON',
        '-DFETCHCONTENT_FULLY_DISCONNECTED=ON',
        '-DOPUS_BUILD_SHARED_LIBRARY=ON',
        '-DOPUS_BUILD_TESTING=OFF',
        '-DOPUS_BUILD_PROGRAMS=OFF',
        '-DOPUS_OSCE=ON',
        '-DOPUS_DEEP_PLC=ON',
        '-DOPUS_DRED=OFF',
        '-DOPUS_X86_MAY_HAVE_AVX2=ON',
        '-DOPUS_X86_PRESUME_AVX2=OFF',
        '-DOPUS_STATIC_RUNTIME=ON',
        '-DOPUS_INSTALL_PKG_CONFIG_MODULE=OFF',
        '-DOPUS_INSTALL_CMAKE_CONFIG_MODULE=OFF'
    )
    $log = Ausfuehren 'cmake-konfigurieren' $CMake $cmakeArgs
    Write-Host "Log: $log"

    $cache = @{}
    Get-Content -LiteralPath (Join-Path $CmakeBau 'CMakeCache.txt') | ForEach-Object {
        if ($_ -match '^([A-Za-z0-9_]+):[A-Z]+=(.*)$') { $cache[$Matches[1]] = $Matches[2] }
    }
    $zeige = 'CMAKE_BUILD_TYPE', 'OPUS_BUILD_SHARED_LIBRARY', 'OPUS_OSCE', 'OPUS_DRED', 'OPUS_STATIC_RUNTIME',
             'OPUS_X86_MAY_HAVE_SSE4_1', 'OPUS_X86_MAY_HAVE_AVX2', 'OPUS_X86_PRESUME_AVX2',
             'OPUS_USE_ALLOCA', 'OPUS_NONTHREADSAFE_PSEUDOSTACK', 'OPUS_HARDENING', 'OPUS_FAST_MATH'
    Write-Host ('Cache: ' + (($zeige | ForEach-Object { "$_=$($cache[$_])" }) -join ', '))
    Pruefe ($cache['OPUS_X86_MAY_HAVE_AVX2'] -eq 'ON') 'OPUS_X86_MAY_HAVE_AVX2 an (Laufzeiterkennung)'
    Pruefe ($cache['OPUS_USE_ALLOCA'] -eq 'ON' -and $cache['OPUS_NONTHREADSAFE_PSEUDOSTACK'] -ne 'ON') 'Stapelspeicher ueber alloca (threadsicher)'

    Schritt 'Bauen (Ninja)'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $log = Ausfuehren 'cmake-bauen' $CMake @('--build', $CmakeBau, '--config', 'Release')
    $warnungen = @(Select-String -LiteralPath $log -Pattern ': warning ' -SimpleMatch).Count
    Write-Host ("Gebaut in {0:N0} s, {1} Compilerwarnungen, Log: {2}" -f $sw.Elapsed.TotalSeconds, $warnungen, $log)

    Schritt 'Einstellungen aus compile_commands.json'
    $cc = Get-Content -LiteralPath (Join-Path $CmakeBau 'compile_commands.json') -Raw | ConvertFrom-Json
    $befehle = @($cc | Where-Object { $_.file -like "$Quelle\*" -or $_.file -like ($Quelle.Replace('\', '/') + '/*') })
    $alle = { param($bed) @($befehle | Where-Object { -not (& $bed $_.command) }).Count -eq 0 }
    Pruefe ($befehle.Count -gt 0) 'Uebersetzungseinheiten' "$($befehle.Count)"
    Pruefe (& $alle { param($c) $c -match '(^|\s)[-/]O2(\s|$)' }) 'alle mit /O2'
    Pruefe (& $alle { param($c) $c -match '(^|\s)[-/]MT(\s|$)' }) 'alle mit /MT (statische CRT)'
    Pruefe (& $alle { param($c) $c -notmatch '(^|\s)[-/]MD(\s|$)' }) 'keine mit /MD'
    Pruefe (& $alle { param($c) $c -match '(^|\s)[-/]Brepro(\s|$)' }) 'alle mit /Brepro'
    foreach ($d in 'DLL_EXPORT', 'ENABLE_DEEP_PLC', 'ENABLE_OSCE', 'OPUS_HAVE_RTCD', 'OPUS_X86_MAY_HAVE_AVX2', 'OPUS_X86_MAY_HAVE_SSE4_1', 'USE_ALLOCA', 'ENABLE_HARDENING') {
        Pruefe (& $alle { param($c) Hat-Define $c $d }) "Define $d gesetzt"
    }
    foreach ($d in 'ENABLE_DRED', 'OPUS_X86_PRESUME_AVX2', 'NONTHREADSAFE_PSEUDOSTACK', 'FIXED_POINT') {
        Pruefe (& $alle { param($c) -not (Hat-Define $c $d) }) "Define $d nicht gesetzt"
    }
    $bweGesetzt = & $alle { param($c) Hat-Define $c 'ENABLE_OSCE_BWE' }
    if ($OhneOsceBwe) { Pruefe (-not $bweGesetzt) 'ENABLE_OSCE_BWE bewusst aus' }
    else { Pruefe $bweGesetzt 'Define ENABLE_OSCE_BWE gesetzt (wie autotools --enable-osce)' }

    # /arch:AVX2 darf nur auf den Dateien der *_AVX2-Gruppen aus den .mk-Listen von opus liegen, sonst
    # erzeugt der Compiler AVX2-Befehle ausserhalb der Laufzeitweiche und die DLL stuerzt auf CPUs ohne AVX2 ab.
    $avx2Soll = @(foreach ($mk in 'celt_sources.mk', 'silk_sources.mk', 'lpcnet_sources.mk') {
        $aktiv = $false
        foreach ($zeile in Get-Content -LiteralPath (Join-Path $Quelle $mk)) {
            if ($zeile -match '^([A-Z0-9_]+)\s*=\s*(.*)$') { $aktiv = $Matches[1] -like '*_AVX2'; $rest = $Matches[2] }
            elseif ($zeile.Trim() -eq '') { $aktiv = $false; continue }
            else { $rest = $zeile }
            if ($aktiv) { $rest -split '\s+' | Where-Object { $_ -and $_ -ne '\' } | ForEach-Object { [IO.Path]::GetFileName($_) } }
        }
    }) | Sort-Object -Unique
    $avx2Ist = @($befehle | Where-Object { $_.command -match '/arch:AVX2' } | ForEach-Object { [IO.Path]::GetFileName($_.file) }) | Sort-Object -Unique
    $avx2Diff = @(Compare-Object -ReferenceObject @($avx2Soll) -DifferenceObject @($avx2Ist))
    Pruefe ($avx2Soll.Count -gt 0 -and $avx2Diff.Count -eq 0) '/arch:AVX2 genau auf den AVX2-Gruppen der .mk-Listen' (
        ($avx2Ist -join ', ') + $(if ($avx2Diff) { ' | Abweichung: ' + (($avx2Diff | ForEach-Object { "$($_.SideIndicator)$($_.InputObject)" }) -join ', ') } else { '' }))
    $dateien = $befehle | ForEach-Object { [IO.Path]::GetFileName($_.file) }
    $dnnErwartet = @($modelle) + @('osce.c', 'osce_features.c', 'nndsp.c', 'lpcnet_plc.c', 'x86_dnn_map.c', 'nnet_avx2.c')
    $dnnFehlt = @($dnnErwartet | Where-Object { $_ -notin $dateien })
    Pruefe ($dnnFehlt.Count -eq 0) 'Deep PLC, OSCE und DNN-Laufzeitauswahl sind mitgebaut' $(if ($dnnFehlt) { 'fehlt: ' + ($dnnFehlt -join ', ') } else { '' })
    Pruefe (@($dateien | Where-Object { $_ -like 'dred_*' }).Count -eq 0) 'keine DRED-Quellen im Bau'

    Schritt 'DLL ablegen und pruefen'
    $dllBau = Join-Path $CmakeBau 'opus.dll'
    if (-not (Test-Path -LiteralPath $dllBau)) { throw "opus.dll nicht gebaut: $dllBau" }
    Copy-Item -LiteralPath $dllBau -Destination $DllZiel -Force
    Write-Host "DLL: $DllZiel ($((Get-Item -LiteralPath $DllZiel).Length) Bytes)"

    $bytes = [IO.File]::ReadAllBytes($DllZiel)
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    $maschine = [BitConverter]::ToUInt16($bytes, $pe + 4)
    Pruefe ($maschine -eq 0x8664) 'PE-Maschine x64' ('0x{0:X4}' -f $maschine)

    $log = Ausfuehren 'dumpbin-abhaengigkeiten' $dumpbin @('/nologo', '/dependents', $DllZiel)
    $abh = @(Get-Content -LiteralPath $log | Where-Object { $_ -match '^\s+\S+\.dll\s*$' } | ForEach-Object { $_.Trim() })
    $crt = @($abh | Where-Object { $_ -match '^(vcruntime|msvcp|msvcr|ucrtbase|api-ms-win-crt-)' })
    Pruefe ($crt.Count -eq 0) 'keine VC-Redist-/UCRT-Abhaengigkeit' ('haengt ab von: ' + ($abh -join ', '))

    $log = Ausfuehren 'dumpbin-exporte' $dumpbin @('/nologo', '/exports', $DllZiel)
    $exporte = @(Get-Content -LiteralPath $log | ForEach-Object {
        if ($_ -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]{8}\s+(\S+)') { $Matches[1] } })
    $fehlen = @($PflichtExporte | Where-Object { $_ -notin $exporte })
    Pruefe ($fehlen.Count -eq 0) "Exporte vollstaendig ($($PflichtExporte.Count) Pflicht, $($exporte.Count) insgesamt)" $(if ($fehlen) { 'fehlt: ' + ($fehlen -join ', ') } else { "Liste: $log" })
    $unerwartet = @($exporte | Where-Object { $_ -notlike 'opus_*' })
    Pruefe ($unerwartet.Count -eq 0) 'nur opus_*-Symbole exportiert' ($unerwartet -join ', ')

    if (-not $OhneRauchtest) {
        Schritt 'Rauchtest (C, dynamisch geladen)'
        $exe = Join-Path $RtBau 'rauchtest.exe'
        $clArgs = @('/nologo', '/O2', '/MT', '/W4', '/Brepro', "/I$Quelle\include",
                    (Join-Path $Eigen 'rauchtest.c'), "/Fo$RtBau\rauchtest.obj", "/Fe$exe", '/link', '/Brepro')
        $log = Ausfuehren 'rauchtest-uebersetzen' 'cl.exe' $clArgs
        $w = @(Select-String -LiteralPath $log -Pattern 'warning' -SimpleMatch).Count
        Write-Host "rauchtest.exe gebaut ($w Warnungen)"

        $rtLog = Join-Path $Logs 'rauchtest.log'
        $rtArgs = @($DllZiel)
        if ($OhneOsceBwe) { $rtArgs += '--ohne-bwe' }
        & $exe @rtArgs *> $rtLog
        $rtCode = $LASTEXITCODE
        Get-Content -LiteralPath $rtLog | ForEach-Object { Write-Host "  $_" }
        Pruefe ($rtCode -eq 0) 'Rauchtest C' "Exitcode $rtCode"

        Schritt 'Gegenprobe .NET (eigener pwsh-Prozess)'
        $pwsh = Join-Path $PSHOME 'pwsh.exe'
        $netLog = Join-Path $Logs 'dotnet-rauchtest.log'
        & $pwsh -NoProfile -NonInteractive -File (Join-Path $Eigen 'dotnet-rauchtest.ps1') $DllZiel *> $netLog
        $netCode = $LASTEXITCODE
        Get-Content -LiteralPath $netLog | ForEach-Object { Write-Host "  $_" }
        Pruefe ($netCode -eq 0) 'Rauchtest .NET' "Exitcode $netCode"
    }

    Schritt 'Lizenz'
    Copy-Item -LiteralPath (Join-Path $Quelle 'COPYING') -Destination (Join-Path $Lizenzen 'COPYING') -Force
    Copy-Item -LiteralPath (Join-Path $Quelle 'AUTHORS') -Destination (Join-Path $Lizenzen 'AUTHORS') -Force
    $herkunft = @(
        'libopus 1.6.1 - Herkunft und Lizenz',
        '',
        'Lizenz:  BSD-3-Clause (Text in COPYING, Autoren in AUTHORS).',
        '         Die einkompilierten DNN-Modelldaten dnn\*_data.c (Deep PLC, FARGAN, PitchDNN, LACE, NoLACE,',
        '         BBWENet) tragen keinen eigenen Lizenzkopf; sie liegen unveraendert im Release-Tarball,',
        '         fuer den COPYING gilt.',
        'Patente: lizenzfreie Patentlizenzen laut COPYING: Xiph.Org (IETF IPR 1524), Microsoft (IPR 1914),',
        '         Broadcom (IPR 1526).',
        'Quelle:  https://downloads.xiph.org/releases/opus/opus-1.6.1.tar.gz',
        "SHA-256: $ArchivSoll (Tarball)",
        'Gebaut:  skripte\opus.ps1 - CMake/Ninja, MSVC x64, Release /O2, /MT, OSCE + Deep PLC an, DRED aus,',
        "         AVX2 per Laufzeiterkennung$(if (-not $OhneOsceBwe) { ', ENABLE_OSCE_BWE' })",
        '         Quellen unveraendert, keine Patches.'
    )
    [IO.File]::WriteAllLines((Join-Path $Lizenzen 'HERKUNFT.txt'), $herkunft, [Text.UTF8Encoding]::new($false))
    Write-Host "Lizenzdateien: $Lizenzen (COPYING, AUTHORS, HERKUNFT.txt)"

    Schritt 'Ergebnis'
    $hash = (Get-FileHash -LiteralPath $DllZiel -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "SHA-256  $hash  $DllZiel"
    if ($script:Fehler) {
        Write-Host "ERGEBNIS  FAIL ($script:Fehler Pruefungen fehlgeschlagen)"
        exit 1
    }
    Write-Host 'ERGEBNIS  PASS'
    exit 0
}
catch {
    Write-Host "ABBRUCH: $($_.Exception.Message)"
    exit 1
}
finally {
    # Umgebung zuruecksetzen (nur relevant, wenn per Punkt in eine Sitzung geladen)
    Get-ChildItem env: | ForEach-Object {
        if (-not $UmgebungVorher.ContainsKey($_.Name)) { [Environment]::SetEnvironmentVariable($_.Name, $null, 'Process') }
    }
    foreach ($k in $UmgebungVorher.Keys) {
        if ([Environment]::GetEnvironmentVariable($k, 'Process') -ne $UmgebungVorher[$k]) {
            [Environment]::SetEnvironmentVariable($k, $UmgebungVorher[$k], 'Process')
        }
    }
}
