# JG TacRelay - Maintainer: Sarevoc

<#
    deepfilternet.ps1 - baut deepfilternet.dll (DeepFilterNet 0.5.6 / libDF in Rust mit tract 0.19.16, Modell
    DeepFilterNet3 fest einkompiliert, eigene C-Schicht native\eigen\deepfilternet) reproduzierbar von vorn und offline.
    JG TacRelay, x64, MSVC-Linker, Release, LTO, statische CRT (+crt-static), /Brepro.

    Ablauf
      1. Voraussetzungen: SHA-256 des Quellarchivs, Rust 1.97.1, vcvars64, vendorte Crates
      2. native\bau\deepfilternet\ loeschen und neu anlegen
      3. Aus dem Quellarchiv nur libDF, das Modell DeepFilterNet3_onnx.tar.gz (SHA-256 geprueft), die Lizenzen und das
         README nach bau\quelle entpacken; die eigene Kiste (Cargo.toml, Cargo.lock, src) nach bau\kiste kopieren und dort
         tacrelay-bau.toml schreiben (cargo --config; Quellen nur aus dem Vendor-Ordner, offline, Pfade abgeschnitten, /Brepro)
      4. cargo build --release --offline --locked in der vcvars64-Umgebung (tract-linalg braucht ml64 und lib)
      5. DLL nach native\out\win-x64\ kopieren, SHA-256 ablegen
      6. dumpbin: genau die 9 Exporte dfn_*, keine VC-Laufzeit, x64; keine lokalen Pfade in der DLL
      7. Lizenzen nach native\out\lizenzen\deepfilternet\: DeepFilterNet, HERKUNFT.txt und je gelinkter Crate deren
         Lizenzdateien samt CRATES.txt (ohne Lizenzdatei: MIT-Text mit den Autoren aus Cargo.toml), dazu rust-std\ mit
         COPYRIGHT-library.html der Rust-Fassung; jede Lizenzangabe muss freizuegig sein, sonst Abbruch
      8. Rauchtest (eigen\deepfilternet\rauchtest.c) mit cl /O2 /MT bauen und gegen die kopierte DLL laufen lassen

    Aufruf
      pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\native\skripte\deepfilternet.ps1
      pwsh -NoProfile -File ...\deepfilternet.ps1 -OhneRauchtest

    Pfade: native\ ist der Ordner ueber diesem Skript, also die jeweilige Arbeitskopie. -Bau und -Ausgabe lenken
    Bauordner und DLL um. Der Bauordner wird geloescht und muss unter native\bau\ oder C:\Claude\tmp\ liegen. Die Pfade
    des Bauordners und des Vendor-Ordners schneidet rustc ab (--remap-path-prefix); bitgleich ist der Bau trotzdem nur
    bei gleichem Bauordner sicher (gemessen ist das in BAUBERICHT.md).

    Netz: keines. Die Crates kommen aus dem Vendor-Ordner (angelegt einmalig mit cargo vendor, Liste in
    C:\Claude\data\tacrelay-quellen\INVENTAR.md); cargo prueft jede Datei gegen .cargo-checksum.json.
    Hinweis PATH: wie bei den anderen Bauskripten setzt die .cmd vor vcvars einen kurzen PATH (sonst findet vcvars bei
    ueber 8191 Zeichen das Windows SDK nicht) und schaltet den Telemetrie-Sender von vcvars ab.
#>
[CmdletBinding()]
param(
    [switch]$OhneRauchtest,
    [string]$Bau = (Join-Path (Split-Path -Parent $PSScriptRoot) 'bau\deepfilternet'),
    [string]$Ausgabe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'out\win-x64'),
    [string]$Vendor = 'C:\Claude\data\tacrelay-quellen\deepfilternet-0.5.6-vendor'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Pfade und feste Werte ---------------------------------------------------------------------
$Native     = Split-Path -Parent $PSScriptRoot
$Eigen      = Join-Path $Native 'eigen\deepfilternet'
$Bau        = [IO.Path]::GetFullPath($Bau)
$Quelle     = Join-Path $Bau 'quelle'
$Kiste      = Join-Path $Bau 'kiste'
$Ziel       = Join-Path $Bau 'ziel'
$Logs       = Join-Path $Bau 'logs'
$Tmp        = Join-Path $Bau 'tmp'
$TestDir    = Join-Path $Bau 'rauchtest'
$Out        = [IO.Path]::GetFullPath($Ausgabe)
$Lizenzen   = Join-Path (Split-Path -Parent $Out) 'lizenzen\deepfilternet'
$Vendor     = [IO.Path]::GetFullPath($Vendor)
$DllName    = 'deepfilternet.dll'
$BauKonfig  = Join-Path $Kiste 'tacrelay-bau.toml'

$Archiv      = 'C:\Claude\data\tacrelay-quellen\DeepFilterNet-v0.5.6.tar.gz'
$ArchivHash  = '0f9a219d06c404bc4200f228e7e224f108cbbca04d7227a6de7d2ce974c2f579'
$ModellHash  = 'c94d91f70911001c946e0fabb4aa9adc37045f45a03b56008cb0c8244cb63616'
$Wurzel      = 'DeepFilterNet-0.5.6'
$RustSoll    = '1.97.1'
$CargoHome   = 'C:\Claude\Eigene_Programme\rust\cargo'
$RustupHome  = 'C:\Claude\Eigene_Programme\rust\rustup'
$Cargo       = Join-Path $CargoHome 'bin\cargo.exe'
$Rustc       = Join-Path $CargoHome 'bin\rustc.exe'
$VsRoot      = 'C:\Program Files\Microsoft Visual Studio\18\Community'
$VcVars      = Join-Path $VsRoot 'VC\Auxiliary\Build\vcvars64.bat'
$Tar         = 'C:\Windows\System32\tar.exe'
$Cmd         = 'C:\Windows\System32\cmd.exe'

$ErwarteteExporte = @('dfn_create', 'dfn_delay_samples', 'dfn_destroy', 'dfn_frame_length', 'dfn_process',
    'dfn_set_atten_lim', 'dfn_set_post_filter_beta', 'dfn_set_thresholds', 'dfn_version') | Sort-Object
# Freizuegige Lizenzen (SPDX); jede Angabe einer gelinkten Crate darf nur aus diesen bestehen.
$Erlaubt = @('MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC', 'Zlib', '0BSD', 'Unlicense', 'CC0-1.0',
    'BSL-1.0', 'Unicode-DFS-2016', 'Unicode-3.0')

function Schritt([string]$Text) { Write-Host ""; Write-Host "== $Text" -ForegroundColor Cyan }

function Pruefe-Pfad([string]$Pfad, [string]$Was) {
    if (-not (Test-Path -LiteralPath $Pfad)) { throw "$Was fehlt: $Pfad" }
}

# Fuehrt Zeilen in einer vcvars64-Umgebung aus (eigene .cmd im Bauordner), alles nach $Log; gibt den Exitcode zurueck.
function Invoke-Vc([string]$Name, [string[]]$Zeilen, [string]$Log) {
    $datei = Join-Path $Bau "$Name.cmd"
    $kopf = @(
        '@echo off'
        'set "PATH=C:\Windows\System32;C:\Windows;C:\Windows\System32\Wbem;C:\Program Files (x86)\Microsoft Visual Studio\Installer"'
        "set `"TMP=$Tmp`""
        "set `"TEMP=$Tmp`""
        'set "VSCMD_SKIP_SENDTELEMETRY=1"'
        "call `"$VcVars`" >nul"
        'if errorlevel 1 exit /b 90'
        "set `"CARGO_HOME=$CargoHome`""
        "set `"RUSTUP_HOME=$RustupHome`""
        'set "CARGO_TERM_COLOR=never"'
        'set "CARGO_TERM_PROGRESS_WHEN=never"'
    )
    ($kopf + $Zeilen + @('exit /b 0')) | Set-Content -LiteralPath $datei -Encoding ascii
    & $Cmd /d /c $datei *> $Log
    return $LASTEXITCODE
}

function Sha256([string]$Pfad) { (Get-FileHash -LiteralPath $Pfad -Algorithm SHA256).Hash.ToLowerInvariant() }

$uhr = [Diagnostics.Stopwatch]::StartNew()

# --- 1. Voraussetzungen ------------------------------------------------------------------------
Schritt 'Voraussetzungen'
foreach ($f in 'Cargo.toml', 'Cargo.lock', 'src\lib.rs', 'deepfilternet.h', 'rauchtest.c') {
    Pruefe-Pfad (Join-Path $Eigen $f) "eigene Datei $f"
}
Pruefe-Pfad $Archiv 'Quellarchiv'
Pruefe-Pfad $Vendor 'Vendor-Ordner'
Pruefe-Pfad $VcVars 'vcvars64.bat'
Pruefe-Pfad $Cargo 'cargo.exe'
$ist = Sha256 $Archiv
if ($ist -ne $ArchivHash) { throw "Quellarchiv SHA-256 $ist statt $ArchivHash" }
Write-Host "  Quellarchiv SHA-256 passt: $ist"
$env:CARGO_HOME = $CargoHome
$env:RUSTUP_HOME = $RustupHome
$rustV = (& $Rustc -V).Trim()
if ($rustV -notmatch "^rustc $([regex]::Escape($RustSoll)) ") { throw "erwartet rustc $RustSoll, gefunden: $rustV" }
Write-Host "  $rustV"
$kisten = @(Get-ChildItem -LiteralPath $Vendor -Directory)
$ohnePruefsumme = @($kisten | Where-Object { -not (Test-Path -LiteralPath (Join-Path $_.FullName '.cargo-checksum.json')) })
if ($ohnePruefsumme.Count -gt 0) { throw "Vendor-Crates ohne .cargo-checksum.json: $($ohnePruefsumme.Name -join ', ')" }
Write-Host "  Vendor: $($kisten.Count) Crates"

# --- 2. Bauordner frisch -----------------------------------------------------------------------
Schritt 'Bauordner neu anlegen'
if (-not ($Bau.StartsWith((Join-Path $Native 'bau\'), [StringComparison]::OrdinalIgnoreCase) -or
          $Bau.StartsWith('C:\Claude\tmp\', [StringComparison]::OrdinalIgnoreCase))) {
    throw "Sicherheitsabbruch: Bauordner liegt weder unter native\bau\ noch unter C:\Claude\tmp\: $Bau"
}
if (Test-Path -LiteralPath $Bau) { Remove-Item -LiteralPath $Bau -Recurse -Force }
foreach ($d in $Bau, $Quelle, $Kiste, (Join-Path $Kiste 'src'), $Logs, $Tmp, $TestDir, $Out) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}
Write-Host "  $Bau"

# --- 3. Quellen und Kiste ----------------------------------------------------------------------
Schritt 'Quellen entpacken (libDF, Modell DeepFilterNet3, Lizenzen)'
$teile = @('libDF', 'models/DeepFilterNet3_onnx.tar.gz', 'LICENSE', 'LICENSE-MIT', 'LICENSE-APACHE', 'README.md') |
    ForEach-Object { "$Wurzel/$_" }
& $Tar -xzf $Archiv -C $Quelle --strip-components 1 @teile
if ($LASTEXITCODE -ne 0) { throw "tar Exitcode $LASTEXITCODE" }
$modell = Join-Path $Quelle 'models\DeepFilterNet3_onnx.tar.gz'
$ist = Sha256 $modell
if ($ist -ne $ModellHash) { throw "Modell SHA-256 $ist statt $ModellHash" }
Write-Host "  Modell DeepFilterNet3_onnx.tar.gz SHA-256 passt: $ist"

Copy-Item -LiteralPath (Join-Path $Eigen 'Cargo.toml'), (Join-Path $Eigen 'Cargo.lock') -Destination $Kiste
Copy-Item -LiteralPath (Join-Path $Eigen 'src\lib.rs') -Destination (Join-Path $Kiste 'src')
@(
    '# Erzeugt von native\skripte\deepfilternet.ps1 - nur Vendor-Quellen, offline, Pfade abgeschnitten'
    '[source.crates-io]'
    'replace-with = "vendored-sources"'
    ''
    '[source.vendored-sources]'
    "directory = '$Vendor'"
    ''
    '[net]'
    'offline = true'
    ''
    '[build]'
    "target-dir = '$Ziel'"
    'rustflags = ['
    "    '-C', 'target-feature=+crt-static',"
    "    '-C', 'link-arg=/Brepro',"
    "    '--remap-path-prefix=$Bau=bau',"
    "    '--remap-path-prefix=$Vendor=vendor',"
    "    '--remap-path-prefix=$CargoHome=cargo',"
    ']'
) | Set-Content -LiteralPath $BauKonfig -Encoding utf8
Write-Host "  Kiste: $Kiste"

# --- 4. Bauen ----------------------------------------------------------------------------------
Schritt 'cargo build --release --offline --locked'
$cargoLog = Join-Path $Logs 'cargo.log'
$code = Invoke-Vc 'bauen' @(
    "`"$Cargo`" --config `"$BauKonfig`" build --release --offline --locked --manifest-path `"$Kiste\Cargo.toml`""
    'if errorlevel 1 exit /b 91'
) $cargoLog
if ($code -ne 0) {
    Get-Content -LiteralPath $cargoLog -Tail 40 | ForEach-Object { Write-Host "  $_" }
    throw "Bau fehlgeschlagen (Exitcode $code), Protokoll: $cargoLog"
}
$warnungen = @(Select-String -LiteralPath $cargoLog -Pattern '^warning' )
Write-Host "  gebaut, $($warnungen.Count) Warnungszeilen (alle aus libDF oder Linker-Hinweisen), Protokoll: $cargoLog"
$dllBau = Join-Path $Ziel "release\$DllName"
Pruefe-Pfad $dllBau 'gebaute DLL'

# --- 5. Kopieren und Hash ----------------------------------------------------------------------
Schritt 'DLL nach out\win-x64'
$dllOut = Join-Path $Out $DllName
Copy-Item -LiteralPath $dllBau -Destination $dllOut -Force
$hash = Sha256 $dllOut
if ($hash -ne (Sha256 $dllBau)) { throw 'Kopie weicht vom Bauergebnis ab' }
"$hash *$DllName" | Set-Content -LiteralPath "$dllOut.sha256" -Encoding ascii
$groesse = (Get-Item -LiteralPath $dllOut).Length
Write-Host ("  {0} ({1:N0} Bytes)" -f $dllOut, $groesse)

# --- 6. dumpbin und Pfade ----------------------------------------------------------------------
Schritt 'dumpbin /exports /dependents /headers'
$expTxt = Join-Path $Logs 'dumpbin-exports.txt'
$depTxt = Join-Path $Logs 'dumpbin-dependents.txt'
$code = Invoke-Vc 'dumpbin' @(
    "dumpbin /nologo /exports `"$dllOut`" > `"$expTxt`""
    'if errorlevel 1 exit /b 93'
    "dumpbin /nologo /dependents /headers `"$dllOut`" > `"$depTxt`""
    'if errorlevel 1 exit /b 94'
) (Join-Path $Logs 'dumpbin.log')
if ($code -ne 0) { throw "dumpbin fehlgeschlagen (Exitcode $code)" }
$exporte = @(Get-Content -LiteralPath $expTxt |
    ForEach-Object { if ($_ -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]{8}\s+(\S+)') { $Matches[1] } }) | Sort-Object
if (($exporte -join ',') -ne ($ErwarteteExporte -join ',')) { throw "Exporte weichen ab: $($exporte -join ', ')" }
Write-Host "  Exporte ($($exporte.Count)): $($exporte -join ', ')"
$depZeilen = Get-Content -LiteralPath $depTxt
$abhaengig = @($depZeilen | Where-Object { $_ -match '^\s+(\S+\.dll)\s*$' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
Write-Host "  Abhaengigkeiten: $($abhaengig -join ', ')"
$crt = @($abhaengig | Where-Object { $_ -match '(?i)^(vcruntime|msvcp|ucrtbase|api-ms-win-crt)' })
if ($crt.Count -gt 0) { throw "DLL haengt an der VC-Laufzeit: $($crt -join ', ')" }
if (-not ($depZeilen | Where-Object { $_ -match 'machine \(x64\)' })) { throw 'DLL ist nicht x64' }
$bytes = [IO.File]::ReadAllBytes($dllOut)
$text = [Text.Encoding]::ASCII.GetString($bytes)
# Der Linker legt nur den Namen deepfilternet.pdb ab (/PDBALTPATH von rustc), ohne Ordner; gesucht wird ein PDB mit Pfad.
$funde = foreach ($m in @([regex]::Escape('C:\Claude'), [regex]::Escape($env:USERNAME), 'Eigene_Programme', '\\[^\\\x00]+\.pdb')) {
    $n = ([regex]::Matches($text, $m, 'IgnoreCase')).Count
    Write-Host "  '$m' in der DLL: $n"
    if ($n -gt 0) { $m }
}
if (@($funde).Count -gt 0) { throw "lokale Pfade in der DLL: $($funde -join ', ')" }

# --- 7. Lizenzen -------------------------------------------------------------------------------
Schritt 'Lizenzen'
if (Test-Path -LiteralPath $Lizenzen) { Remove-Item -LiteralPath $Lizenzen -Recurse -Force }
$crateLiz = Join-Path $Lizenzen 'crates'
New-Item -ItemType Directory -Force -Path $crateLiz | Out-Null
foreach ($f in 'LICENSE', 'LICENSE-MIT', 'LICENSE-APACHE') {
    Copy-Item -LiteralPath (Join-Path $Quelle $f) -Destination (Join-Path $Lizenzen "DeepFilterNet-$f.txt")
}
# Gelinkt = normale Abhaengigkeiten fuer x86_64-pc-windows-msvc ohne Prozedurmakros und deren Baum.
$baumLog = Join-Path $Logs 'cargo-tree.log'
$baumTxt = Join-Path $Logs 'gelinkt.txt'
$code = Invoke-Vc 'baum' @(
    "`"$Cargo`" --config `"$BauKonfig`" tree --offline --locked --manifest-path `"$Kiste\Cargo.toml`" --target x86_64-pc-windows-msvc -e normal,no-proc-macro --prefix none -f `"{p}|{l}`" > `"$baumTxt`""
    'if errorlevel 1 exit /b 95'
) $baumLog
if ($code -ne 0) { throw "cargo tree fehlgeschlagen (Exitcode $code)" }
$gelinkt = @(Get-Content -LiteralPath $baumTxt | ForEach-Object { $_ -replace ' \(\*\)$', '' } |
    Where-Object { $_ -match '^(\S+) v(\S+)( \(registry[^)]*\))?\|(.*)$' } | Sort-Object -Unique)
$zeilen = @()
$verboten = @()
foreach ($g in $gelinkt) {
    $null = $g -match '^(\S+) v(\S+)( \([^)]*\))?\|(.*)$'
    $name = $Matches[1]; $version = $Matches[2]; $lizenz = $Matches[4].Trim()
    if ($name -in 'deepfilternet', 'deep_filter') { continue }
    $ordner = Join-Path $Vendor "$name-$version"
    Pruefe-Pfad $ordner "Vendor-Ordner von $name $version"
    $tokens = @($lizenz -split '[\s/()]+' | Where-Object { $_ -and $_ -notin 'OR', 'AND', 'WITH' })
    if ($tokens.Count -eq 0 -or @($tokens | Where-Object { $_ -notin $Erlaubt }).Count -gt 0) { $verboten += "$name $version ($lizenz)" }
    $dateien = @(Get-ChildItem -LiteralPath $ordner -File | Where-Object { $_.Name -match '^(LICEN[CS]E|COPYING|NOTICE|UNLICENSE|COPYRIGHT)' })
    $ziel = Join-Path $crateLiz "$name-$version"
    New-Item -ItemType Directory -Force -Path $ziel | Out-Null
    foreach ($d in $dateien) { Copy-Item -LiteralPath $d.FullName -Destination $ziel }
    $angabe = if ($dateien.Count -gt 0) { $dateien.Name -join ', ' } else { '' }
    if ($dateien.Count -eq 0) {
        # Gegenpruefung Klangstufe B: Ohne Lizenzdatei in der Crate (realfft 3.3.0) fehlte der Vermerk, den MIT in jeder Kopie
        # verlangt. Dann der MIT-Text mit den Autoren aus Cargo.toml (ohne Mailadressen); jede andere Lizenz bricht ab.
        if ($tokens -notcontains 'MIT') { throw "$name $version bringt keine Lizenzdatei mit und ist nicht MIT: $lizenz" }
        $toml = Get-Content -LiteralPath (Join-Path $ordner 'Cargo.toml') -Raw
        $autoren = [regex]::Match($toml, '(?m)^authors\s*=\s*\[([^\]]*)\]').Groups[1].Value
        $autoren = (@([regex]::Matches($autoren, '"([^"<]+)') | ForEach-Object { $_.Groups[1].Value.Trim() }) -join ', ')
        if (-not $autoren) { $autoren = "the $name authors" }
        @(
            "$name $version - die Crate bringt keine Lizenzdatei mit; Cargo.toml: license = ""$lizenz"", authors = $autoren."
            'Es gilt die MIT-Lizenz (Standardtext, https://opensource.org/licenses/MIT):'
            ''
            'MIT License'
            ''
            "Copyright (c) $autoren"
            ''
            'Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated'
            'documentation files (the "Software"), to deal in the Software without restriction, including without limitation the'
            'rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit'
            'persons to whom the Software is furnished to do so, subject to the following conditions:'
            ''
            'The above copyright notice and this permission notice shall be included in all copies or substantial portions of the'
            'Software.'
            ''
            'THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE'
            'WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR'
            'COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR'
            'OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.'
        ) | Set-Content -LiteralPath (Join-Path $ziel 'LICENSE-MIT-nach-Cargo.toml.txt') -Encoding utf8
        $angabe = 'keine Lizenzdatei in der Crate; LICENSE-MIT-nach-Cargo.toml.txt (MIT-Standardtext mit den Autoren aus Cargo.toml)'
    }
    $zeilen += "{0,-28} {1,-10} {2,-50} {3}" -f $name, $version, $lizenz, $angabe
}
# Die Rust-Standardbibliothek (core, alloc, std, compiler_builtins ...) ist statisch gelinkt; ihre Lizenzen samt der Texte stehen
# in COPYRIGHT-library.html der benutzten Rust-Fassung (Gegenpruefung Klangstufe B).
$stdQuelle = Join-Path $RustupHome 'toolchains\stable-x86_64-pc-windows-msvc\share\doc\rust\COPYRIGHT-library.html'
Pruefe-Pfad $stdQuelle 'Lizenzen der Rust-Standardbibliothek'
$stdZiel = Join-Path $Lizenzen 'rust-std'
New-Item -ItemType Directory -Force -Path $stdZiel | Out-Null
Copy-Item -LiteralPath $stdQuelle -Destination $stdZiel
if (-not (Select-String -LiteralPath (Join-Path $stdZiel 'COPYRIGHT-library.html') -Pattern 'Rust Standard Library' -SimpleMatch -Quiet)) { throw 'COPYRIGHT-library.html ohne die Standardbibliothek' }
if ($verboten.Count -gt 0) { throw "nicht freizuegige oder unklare Lizenz: $($verboten -join '; ')" }
@(
    'Rust-Crates in deepfilternet.dll (gelinkt fuer x86_64-pc-windows-msvc, ohne Prozedurmakros), aus Cargo.lock'
    "Stand: native\eigen\deepfilternet\Cargo.lock, Vendor-Ordner $Vendor"
    ''
    ("{0,-28} {1,-10} {2,-50} {3}" -f 'Crate', 'Version', 'Lizenz (Cargo.toml)', 'Lizenzdateien in crates\<name>-<version>\')
) + $zeilen | Set-Content -LiteralPath (Join-Path $Lizenzen 'CRATES.txt') -Encoding utf8
Write-Host "  $($zeilen.Count) gelinkte Crates, alle freizuegig"
@(
    'deepfilternet.dll - Herkunft der Bestandteile'
    ''
    'Quellcode DeepFilterNet (libDF, Rust)'
    '  Projekt:  DeepFilterNet, Hendrik Schroeter, https://github.com/Rikorose/DeepFilterNet'
    '  Fassung:  Tag v0.5.6 (Commit 978576aa8400552a4ce9730838c635aa30db5e61, Release 31.08.2023)'
    '  Archiv:   https://github.com/Rikorose/DeepFilterNet/archive/refs/tags/v0.5.6.tar.gz'
    "            SHA-256 $ArchivHash (beim Bau geprueft)"
    '  Lizenz:   MIT OR Apache-2.0 nach Wahl (DeepFilterNet-LICENSE.txt, -LICENSE-MIT.txt, -LICENSE-APACHE.txt;'
    '            libDF/Cargo.toml: license = "MIT/Apache-2.0"; README.md Abschnitt License)'
    ''
    'Modellgewichte (in die DLL einkompiliert)'
    '  Datei:    models/DeepFilterNet3_onnx.tar.gz aus demselben Archiv (enc.onnx, erb_dec.onnx, df_dec.onnx, config.ini)'
    "            SHA-256 $ModellHash (beim Bau geprueft)"
    '  Lizenz:   Die Gewichte liegen im Repository selbst (Ordner models, README: "models contains pretrained for usage'
    '            in DeepFilterNet (Python) or libDF/deep-filter (Rust)") und stehen damit unter dessen Lizenzdateien'
    '            (MIT OR Apache-2.0). Eine eigene Lizenz nur fuer die Gewichte gibt es nicht; der README-Satz zur Lizenz'
    '            spricht von "All code in this repository". Keine Einschraenkung (etwa nicht kommerziell) gefunden.'
    '            libDF selbst (license = "MIT/Apache-2.0") kompiliert genau diese Datei ab Werk ein (Feature default-model,'
    '            libDF/Cargo.toml; include_bytes in libDF/src/tract.rs): Der Autor gibt die Gewichte mit seiner Crate weiter.'
    ''
    'Rust-Abhaengigkeiten'
    '  Liste mit Version und Lizenz: CRATES.txt, Lizenzdateien je Crate unter crates\'
    '  Versionen aus dem Cargo.lock von v0.5.6, auf die gebrauchten Crates gekuerzt; einzige Abweichung: time 0.3.28 ->'
    '  0.3.36 (nur Bauzeit, Vorlagen von tract-linalg; 0.3.28 baut mit Rust ab 1.80 nicht mehr, Fehler E0282)'
    ''
    'Rust-Standardbibliothek (statisch gelinkt)'
    "  $rustV; MIT OR Apache-2.0, einzelne Teile weitere freizuegige Lizenzen; Texte in rust-std\COPYRIGHT-library.html"
    ''
    'Eigenbau JG TacRelay'
    '  native\eigen\deepfilternet\Cargo.toml, Cargo.lock, src\lib.rs (C-Schicht), deepfilternet.h, rauchtest.c'
    "  Bau:      native\skripte\deepfilternet.ps1 ($rustV, MSVC-Linker, +crt-static, LTO, /Brepro, offline)"
    "  DLL:      $DllName, SHA-256 $hash"
) | Set-Content -LiteralPath (Join-Path $Lizenzen 'HERKUNFT.txt') -Encoding utf8

# --- 8. Rauchtest ------------------------------------------------------------------------------
$rauchErgebnis = 'nicht ausgefuehrt'
if (-not $OhneRauchtest) {
    Schritt 'Rauchtest'
    $exe = Join-Path $TestDir 'rauchtest.exe'
    $code = Invoke-Vc 'rauchtest-bauen' @(
        "cl /nologo /O2 /MT /W3 /Brepro /I`"$Eigen`" /Fo`"$TestDir\\`" /Fe`"$exe`" `"$Eigen\rauchtest.c`""
        'if errorlevel 1 exit /b 96'
    ) (Join-Path $Logs 'rauchtest-bau.log')
    if ($code -ne 0) {
        Get-Content -LiteralPath (Join-Path $Logs 'rauchtest-bau.log') -Tail 20 | ForEach-Object { Write-Host "  $_" }
        throw "Rauchtest nicht gebaut (Exitcode $code)"
    }
    $rauchTxt = Join-Path $Logs 'rauchtest.txt'
    & $exe $dllOut *> $rauchTxt
    $rauchCode = $LASTEXITCODE
    Get-Content -LiteralPath $rauchTxt | ForEach-Object { Write-Host "  $_" }
    if ($rauchCode -ne 0) { throw "Rauchtest fehlgeschlagen (Exitcode $rauchCode)" }
    $rauchErgebnis = 'bestanden'
}

# --- Ergebnis ----------------------------------------------------------------------------------
Schritt 'Ergebnis'
Write-Host "  DLL:        $dllOut"
Write-Host ("  Groesse:    {0:N0} Bytes" -f $groesse)
Write-Host "  SHA-256:    $hash"
Write-Host "  Rauchtest:  $rauchErgebnis"
Write-Host "  Dauer:      $([math]::Round($uhr.Elapsed.TotalSeconds, 1)) s"
exit 0
