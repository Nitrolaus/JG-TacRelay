# JG TacRelay - Maintainer: Sarevoc

<#
    Baut das JG-TacRelay-Paket für den Probefunk auf dem eigenen PC und für den Heimserver.

    Inhalt:
        client\   JG TacRelay (Funk.Client), win-x64, framework-abhängig, mit den nativen DLLs
        server\   funk-testserver und funk-testclient (Probefunk, nur Loopback), dazu konfig\probefunk.json
        linux\    funk-server, funk-admin, funk-echo und funk-testclient (echo-probe), framework-abhängig als portable
                  DLLs (dotnet <name>.dll),
                  dazu konfig\ (Beispiele, Units, profilschluessel.txt, fail2ban), die Anleitungen HEIMSERVER-PROXMOX.md,
                  BETRIEB.md, HEIMSERVER-HAERTUNG.md, EINRICHTEN.md und UMZUG.md, funk-einrichten.sh (LF) und
                  "Profil erneuern.cmd" für Sarevocs Stick (CRLF)
        lizenzen\ Lizenztexte aller Fremdbestandteile (native, NuGet, .NET-Programmstarter)
        LIESMICH.txt, Probefunk starten.cmd, Probefunk pruefen.cmd, THIRD-PARTY-NOTICES.txt, MANIFEST.txt (SHA-256)

    Ziel: <Ziel>\<JJJJMMTT_HHMM>_JG-TacRelay\ und daneben die ZIP-Datei mit .sha256. Die nativen DLLs und ihre Lizenzen kommen aus
    dem Ordner von main (-NativeOrdner) und werden dort nur gelesen. Nichts wird aus dem Netz geladen.

    Danach die Prüfung ohne Fenster und ohne Audio (auslassen mit -OhnePruefung): der verpackte funk-testserver mit
    Echo-Funker und der verpackte funk-testclient (echo-probe) über den Testzugang, die Linux-DLLs mit --hilfe bzw.
    pruefen, und zum Schluss, dass das Paket noch zum MANIFEST passt.

    Aufruf:
        pwsh -File C:\Claude\work\JG-TacRelay-wt\probefunk\tools\paket-bauen.ps1
        pwsh -File ...\tools\paket-bauen.ps1 -OhnePruefung
#>
param(
    [string]$Ziel = 'C:\Claude\output\test',
    [string]$NativeOrdner = 'C:\Claude\work\JG-TacRelay\native\out',
    [string]$Arbeitsordner = 'C:\Claude\tmp\tr-paket',
    [switch]$OhnePruefung,
    [switch]$ArbeitsordnerBehalten
)

$ErrorActionPreference = 'Stop'
# Vor Set-StrictMode einbinden: dev-umgebung.ps1 fragt Variablen ab, die nicht immer gesetzt sind. dotnet-umgebung.ps1
# bindet es ein und hält dazu Temp, CLI-Zustand, Zertifikat und PATH des Profils aus dem Bau heraus.
. (Join-Path $PSScriptRoot 'dotnet-umgebung.ps1')
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$wurzel = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet).Source
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { 'C:\Claude\tools\nuget-packages' }
$utf8 = [Text.UTF8Encoding]::new($false)

function Schritt([string]$text) { Write-Host ("== " + $text) }

function Textdatei([string]$pfad, [string]$inhalt) {
    # CRLF, UTF-8 ohne BOM: Editor und cmd unter Windows lesen das ohne Umwege.
    $normal = ($inhalt -replace "`r`n", "`n") -replace "`n", "`r`n"
    New-Item -ItemType Directory -Force (Split-Path -Parent $pfad) | Out-Null
    [IO.File]::WriteAllText($pfad, $normal, $utf8)
}

function Zeilenenden([string]$von, [string]$nach, [string]$ende) {
    # Für den Linux-Server LF (bash liest ein CR als Teil des Befehls), für cmd CRLF; UTF-8 ohne BOM, sonst steht vor #! ein Zeichen.
    $text = [IO.File]::ReadAllText($von, $utf8) -replace "`r`n", "`n"
    if ($ende -ne "`n") { $text = $text -replace "`n", $ende }
    New-Item -ItemType Directory -Force (Split-Path -Parent $nach) | Out-Null
    [IO.File]::WriteAllText($nach, $text, $utf8)
}

function Veroeffentlichen([string]$projekt, [string]$ausgabe, [string[]]$zusatz, [string]$log) {
    $argumente = @('publish', (Join-Path $wurzel $projekt), '-c', 'Release', '-o', $ausgabe, '--nologo', '--disable-build-servers') + $zusatz
    & $dotnet @argumente *> $log
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $projekt gescheitert:`n$((Get-Content $log -Tail 25) -join "`n")" }
}

function Kopieren([string]$von, [string]$nach) {
    # Ohne PDB: kleiner, und keine Pfade des Bau-PCs im Paket.
    New-Item -ItemType Directory -Force $nach | Out-Null
    foreach ($d in Get-ChildItem $von -Recurse -File | Where-Object Extension -ne '.pdb') {
        $rel = $d.FullName.Substring($von.TrimEnd('\').Length + 1)
        $z = Join-Path $nach $rel
        New-Item -ItemType Directory -Force (Split-Path -Parent $z) | Out-Null
        Copy-Item $d.FullName $z -Force
    }
}

function Hashliste([string]$ordner) {
    Get-ChildItem $ordner -Recurse -File | Where-Object { $_.Name -ne 'MANIFEST.txt' } | ForEach-Object {
        [pscustomobject]@{
            Pfad  = $_.FullName.Substring($ordner.Length + 1).Replace('\', '/')
            Bytes = $_.Length
            Sha   = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object Pfad
}

# ---- Vorbedingungen ----
if (-not (Test-Path (Join-Path $NativeOrdner 'win-x64\opus.dll'))) { throw "Native DLLs fehlen in $NativeOrdner\win-x64." }
if (-not (Test-Path (Join-Path $NativeOrdner 'lizenzen'))) { throw "Lizenzen der nativen DLLs fehlen in $NativeOrdner\lizenzen." }
$laufzeiten = & $dotnet --list-runtimes
$desktop = @($laufzeiten | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 10\.' } | ForEach-Object { ($_ -split ' ')[0..1] -join ' ' })
if ($desktop.Count -eq 0) { Write-Warning 'Microsoft.WindowsDesktop.App 10 ist auf diesem PC nicht installiert; der Client braucht sie.' }
$laufzeitText = if ($desktop.Count -gt 0) { $desktop[-1] } else { 'auf dem Bau-PC nicht gefunden' }

$stempel = Get-Date -Format 'yyyyMMdd_HHmm'
$name = "${stempel}_JG-TacRelay"
$paket = Join-Path $Ziel $name
$zip = "$paket.zip"
if ((Test-Path $paket) -or (Test-Path $zip)) { throw "Gibt es schon: $paket" }
$arbeit = Join-Path $Arbeitsordner $stempel
$pub = Join-Path $arbeit 'publish'
New-Item -ItemType Directory -Force $pub, $paket | Out-Null
$commit = (git -C $wurzel rev-parse --short HEAD 2>$null)
$zweig = (git -C $wurzel rev-parse --abbrev-ref HEAD 2>$null)
$sauber = -not (git -C $wurzel status --porcelain 2>$null)

# ---- Veröffentlichen ----
Schritt 'Client (win-x64, framework-abhängig)'
Veroeffentlichen 'src\Funk.Client\Funk.Client.csproj' "$pub\client" @('-r', 'win-x64', '--self-contained', 'false', "-p:FunkNativeOrdner=$NativeOrdner\win-x64") "$arbeit\client.log"
Schritt 'Testserver und Testclient (Probefunk)'
Veroeffentlichen 'tests\Funk.Testserver\Funk.Testserver.csproj' "$pub\server" @() "$arbeit\testserver.log"
Veroeffentlichen 'tests\Funk.Testclient\Funk.Testclient.csproj' "$pub\server" @() "$arbeit\testclient.log"
Schritt 'Server, Admin-Werkzeug, Echo-Funker und Prüf-Client (linux-x64, framework-abhängig, ohne Programmstarter)'
foreach ($p in 'src\Funk.Server\Funk.Server.csproj', 'src\Funk.Admin\Funk.Admin.csproj', 'src\Funk.Echo\Funk.Echo.csproj', 'tests\Funk.Testclient\Funk.Testclient.csproj') {
    Veroeffentlichen $p "$pub\linux" @('-r', 'linux-x64', '--self-contained', 'false', '-p:UseAppHost=false') "$arbeit\$([IO.Path]::GetFileNameWithoutExtension($p)).log"
}

Kopieren "$pub\client" "$paket\client"
Kopieren "$pub\server" "$paket\server"
# Im Testserver ist funk-server nur Bibliothek; sein Programmstarter und seine Startdateien gehören nicht dazu.
Remove-Item "$paket\server\funk-server.exe", "$paket\server\funk-server.deps.json", "$paket\server\funk-server.runtimeconfig.json" -ErrorAction SilentlyContinue
Kopieren "$pub\linux" "$paket\linux"
foreach ($dll in 'opus.dll', 'rnnoise.dll', 'speexdsp_resampler.dll', 'webrtc_apm.dll', 'deepfilternet.dll') {
    if (-not (Test-Path "$paket\client\$dll")) { throw "Native DLL fehlt im Client: $dll" }
}

# ---- Konfiguration und Anleitungen ----
Schritt 'Konfiguration, Anleitungen, Startdateien'
New-Item -ItemType Directory -Force "$paket\server\konfig", "$paket\linux\konfig" | Out-Null
Copy-Item "$wurzel\konfig\probefunk.json" "$paket\server\konfig\"
foreach ($k in 'server.beispiel.json', 'funk-server.service', 'funk-echo.service', 'profilschluessel.txt') { Copy-Item "$wurzel\konfig\$k" "$paket\linux\konfig\" }
# fail2ban-Beispiele für den Proxmox-Host (A51, HEIMSERVER-HAERTUNG.md, Punkt 7); serverpaket-bauen.ps1 kennt dieselbe Liste.
Copy-Item "$wurzel\konfig\fail2ban" "$paket\linux\konfig\" -Recurse
Copy-Item "$wurzel\docs\HEIMSERVER-PROXMOX.md", "$wurzel\docs\BETRIEB.md", "$wurzel\docs\HEIMSERVER-HAERTUNG.md", "$wurzel\docs\EINRICHTEN.md", "$wurzel\docs\UMZUG.md" "$paket\linux\"
# Einrichten und Umzug (A55): Das Skript liegt neben den Programmen (sudo bash funk-einrichten.sh im Ordner linux). Der
# Doppelklick ebenso: Sarevocs Stickordner funk-offline ist eine Kopie von linux (HEIMSERVER-PROXMOX.md, Abschnitt 6), und
# "cd /d %~dp0" braucht funk-admin.dll im selben Ordner.
Zeilenenden "$wurzel\konfig\funk-einrichten.sh" "$paket\linux\funk-einrichten.sh" "`n"
Zeilenenden "$wurzel\tools\paket\Profil erneuern.cmd" "$paket\linux\Profil erneuern.cmd" "`r`n"
Copy-Item "$wurzel\tools\paket\Probefunk starten.cmd", "$wurzel\tools\paket\Probefunk pruefen.cmd" $paket
Textdatei "$paket\THIRD-PARTY-NOTICES.txt" ([IO.File]::ReadAllText("$wurzel\THIRD-PARTY-NOTICES.txt"))

# Kennt der Client den Testzugang schon (Modul "integration")? Dann entfällt der Hinweis in LIESMICH.
$clientDll = [IO.File]::ReadAllBytes("$paket\client\TacRelay.dll")
$kennung = [Text.Encoding]::Unicode.GetBytes('--testzugang')
$kenntTestzugang = ([Text.Encoding]::Unicode.GetString($clientDll)).Contains('--testzugang') -or ([Text.Encoding]::Latin1.GetString($clientDll)).Contains([Text.Encoding]::Latin1.GetString($kennung))
$hinweis = if ($kenntTestzugang) { '' } else {
    "`nAchtung: Der Client in diesem Paket kennt den Schalter --testzugang noch nicht (Modul `"integration`" ist noch nicht`n" +
    "zusammengeführt). Er startet, verbindet sich aber nicht mit dem Testserver. Den Funkweg prüfst du bis dahin mit`n" +
    "`"Probefunk pruefen.cmd`".`n"
}
$liesmich = [IO.File]::ReadAllText("$wurzel\tools\paket\LIESMICH.txt").Replace('{{PAKET}}', $name).Replace('{{LAUFZEIT}}', $laufzeitText)
$liesmich = $liesmich.Replace("{{HINWEIS_CLIENT}}`r`n", "{{HINWEIS_CLIENT}}`n").Replace("{{HINWEIS_CLIENT}}`n", $hinweis + "`n")
Textdatei "$paket\LIESMICH.txt" $liesmich

# ---- Lizenzen ----
Schritt 'Lizenzen'
$liz = "$paket\lizenzen"
Kopieren (Join-Path $NativeOrdner 'lizenzen') "$liz\native"
$mit = [IO.File]::ReadAllText("$wurzel\tools\paket\MIT-Lizenz.txt")
Textdatei "$liz\dotnet\LICENSE.txt" ($mit.Replace('{{COPYRIGHT}}', 'Copyright (c) .NET Foundation and Contributors'))
$ungeklaert = [Collections.Generic.List[string]]::new()
$deps = Get-Content "$paket\client\TacRelay.deps.json" -Raw | ConvertFrom-Json
foreach ($eintrag in $deps.libraries.PSObject.Properties) {
    if ($eintrag.Value.type -ne 'package') { continue }
    $paketname, $version = $eintrag.Name -split '/'
    $quelle = Join-Path $nuget ($paketname.ToLowerInvariant() + '\' + $version)
    $zielordner = Join-Path "$liz\nuget" $paketname.ToLowerInvariant()
    $texte = @(Get-ChildItem $quelle -File | Where-Object { $_.Name -match '^(LICENSE|License|THIRD-PARTY-NOTICES|ThirdPartyNotices)' })
    if ($texte.Count -gt 0) {
        New-Item -ItemType Directory -Force $zielordner | Out-Null
        foreach ($d in $texte) { Copy-Item $d.FullName $zielordner }
        continue
    }
    $nuspec = [xml](Get-Content (Get-ChildItem $quelle -Filter '*.nuspec' | Select-Object -First 1).FullName -Raw)
    $lizenz = $nuspec.package.metadata.license
    if ($lizenz -and $lizenz.type -eq 'expression' -and $lizenz.'#text' -eq 'MIT') {
        # Kein Lizenztext im Paket: Standardtext der MIT-Lizenz mit dem Copyright aus der nuspec.
        Textdatei "$zielordner\LICENSE.txt" ($mit.Replace('{{COPYRIGHT}}', 'Copyright ' + $nuspec.package.metadata.copyright + ' (' + $paketname + ' ' + $version + ', laut nuspec)'))
    }
    else {
        $ungeklaert.Add("$paketname $version`: keine Lizenzdatei und kein MIT-Ausdruck in der nuspec")
    }
}

# ---- Jede ausgelieferte DLL muss eigen oder in den NOTICES sein ----
$notices = [IO.File]::ReadAllText("$paket\THIRD-PARTY-NOTICES.txt")
$eigen = '^(Funk\.[A-Za-z]+|TacRelay|funk-(server|admin|echo|testserver|testclient))\.(dll|exe)$'
foreach ($d in Get-ChildItem $paket -Recurse -File -Include '*.dll', '*.exe') {
    if ($d.Name -match $eigen -and $d.Name -notmatch '\.exe$') { continue }
    if ($d.Name -match '\.exe$' -and $notices.Contains($d.Name)) { continue }
    if (-not $notices.Contains($d.Name)) { $ungeklaert.Add("$($d.FullName.Substring($paket.Length + 1)): nicht in THIRD-PARTY-NOTICES") }
}

# ---- Texte und Geheimnisse prüfen ----
Schritt 'Texte und Geheimnisse prüfen'
foreach ($t in 'LIESMICH.txt', 'THIRD-PARTY-NOTICES.txt', 'Probefunk starten.cmd', 'Probefunk pruefen.cmd', 'linux\funk-einrichten.sh', 'linux\Profil erneuern.cmd',
    'linux\EINRICHTEN.md', 'linux\UMZUG.md') {
    & pwsh -NoProfile -File C:\Claude\tools\text-pruefen.ps1 -Pfad "$paket\$t" -Still
    if ($LASTEXITCODE -ne 0) { throw "Typografische oder unsichtbare Zeichen in $t (tools\text-pruefen.ps1)." }
}
& pwsh -NoProfile -File C:\Claude\tools\geheimnisse-pruefen.ps1 -Pfad $paket -Still
if ($LASTEXITCODE -ne 0) { throw 'tools\geheimnisse-pruefen.ps1 meldet Fundstellen im Paket.' }

# ---- MANIFEST ----
Schritt 'MANIFEST'
$liste = @(Hashliste $paket)
$kopf = @(
    "MANIFEST $name",
    "Erzeugt $(Get-Date -Format 'yyyy-MM-dd HH:mm') aus $zweig $commit$(if (-not $sauber) { ' (mit ungesicherten Änderungen)' })",
    "Laufzeit für den Client: Microsoft.WindowsDesktop.App 10 (x64), auf dem Bau-PC: $laufzeitText",
    "$($liste.Count) Dateien. Prüfen: Get-FileHash <datei> -Algorithm SHA256",
    '',
    'SHA-256                                                           Bytes  Datei'
)
Textdatei "$paket\MANIFEST.txt" (($kopf + ($liste | ForEach-Object { '{0}  {1,10}  {2}' -f $_.Sha, $_.Bytes, $_.Pfad })) -join "`n")

# ---- Prüfung ohne Fenster und ohne Audio ----
$pruefung = 'ausgelassen'
if (-not $OhnePruefung) {
    Schritt 'Prüfung: Testserver mit Echo-Funker, echo-probe über den Testzugang'
    $log = "$arbeit\pruefung.log"
    # Der Verwaltungssocket kurz im Arbeitsordner: Bei einem langen -Arbeitsordner wiche er sonst aus.
    & "$paket\server\funk-testserver.exe" --probe "$paket\server\konfig\probefunk.json" --zustand "$arbeit\probe-zustand" --testzugang 0 --echo `
        --socket "$arbeit\v.sock" --client "$paket\server\funk-testclient.exe" --client-argumente 'echo-probe --testzugang {testzugang} --frames 100' *> $log
    $code = $LASTEXITCODE
    $bestanden = $code -eq 0 -and (Select-String -Path $log -Pattern '^BESTANDEN' -Quiet)
    Get-Content $log | Where-Object { $_ -match '^(Aktiv als|Gesprochen|Echo von|BESTANDEN|NICHT BESTANDEN|TESTZUGANG)' } | ForEach-Object { '   ' + $_ }
    if (-not $bestanden) { throw "Prüfung nicht bestanden (Exitcode $code), Protokoll: $log" }

    Schritt 'Prüfung: Linux-DLLs starten (dotnet <dll> --hilfe, Beispielkonfiguration prüfen)'
    & $dotnet "$paket\linux\funk-server.dll" --hilfe *> "$arbeit\linux-server.log"; if ($LASTEXITCODE -ne 0) { throw 'funk-server --hilfe' }
    & $dotnet "$paket\linux\funk-admin.dll" hilfe *> "$arbeit\linux-admin.log"; if ($LASTEXITCODE -ne 0) { throw 'funk-admin hilfe' }
    & $dotnet "$paket\linux\funk-echo.dll" --hilfe *> "$arbeit\linux-echo.log"; if ($LASTEXITCODE -ne 0) { throw 'funk-echo --hilfe' }
    & $dotnet "$paket\linux\funk-testclient.dll" hilfe *> "$arbeit\linux-testclient.log"; if ($LASTEXITCODE -ne 0) { throw 'funk-testclient hilfe' }
    & $dotnet "$paket\linux\funk-server.dll" --konfig "$paket\linux\konfig\server.beispiel.json" pruefen *> "$arbeit\linux-pruefen.log"
    # Die Beispielkonfiguration zeigt auf /etc/funk/profil.json, das erst auf dem Server entsteht; seit "pruefen" das
    # Profil liest (Profilabgleich), endet der Lauf hier mit genau diesem einen Befund. Jeder andere zählt als Fehler.
    $pruefen = Get-Content "$arbeit\linux-pruefen.log" -Raw
    $nurProfilFehlt = $pruefen -match '(?s)^\s*Konfiguration ungültig:\s+Serverprofil: Profildatei fehlt: [^\r\n]*\s*$'
    if ($LASTEXITCODE -ne 0 -and -not $nurProfilFehlt) { throw "server.beispiel.json: $pruefen" }

    Schritt 'Prüfung: Paket unverändert (MANIFEST)'
    $nachher = @(Hashliste $paket)
    $abweichend = Compare-Object $liste $nachher -Property Pfad, Sha
    if ($abweichend) { throw "Paket hat sich bei der Prüfung verändert: $($abweichend | Out-String)" }
    if (Test-Path "$arbeit\probe-zustand") {
        $rest = @(Get-ChildItem "$arbeit\probe-zustand" -Recurse -File)
        if ($rest.Count -gt 0) { throw "Der Probe-Zustand wurde nicht gelöscht: $arbeit\probe-zustand" }
    }
    $pruefung = 'bestanden'
}

# ---- ZIP ----
Schritt 'ZIP'
Compress-Archive -Path $paket -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
# Prüfsumme daneben, in derselben Form wie Setup und Serverpaket (bis 0.3.0 von Hand angelegt).
[IO.File]::WriteAllText("$zip.sha256", "$zipHash  $(Split-Path $zip -Leaf)`r`n", $utf8)
if (-not $ArbeitsordnerBehalten) { Remove-Item -Recurse -Force $arbeit }

Write-Host ''
Write-Host "Paket:     $paket ($($liste.Count) Dateien, $([math]::Round(($liste | Measure-Object Bytes -Sum).Sum / 1MB, 1)) MB)"
Write-Host "ZIP:       $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB), SHA-256 $zipHash"
Write-Host "Quelle:    $zweig $commit$(if (-not $sauber) { ' (mit ungesicherten Änderungen)' })"
Write-Host "Laufzeit:  $laufzeitText"
Write-Host "Client:    $(if ($kenntTestzugang) { 'kennt --testzugang' } else { 'kennt --testzugang noch nicht (Hinweis steht in LIESMICH)' })"
Write-Host "Prüfung:   $pruefung"
if ($ungeklaert.Count -gt 0) {
    Write-Host 'UNGEKLÄRT:'
    foreach ($u in $ungeklaert) { Write-Host "  $u" }
}
else {
    Write-Host 'Lizenzen:  jede ausgelieferte DLL steht in THIRD-PARTY-NOTICES; offen bleiben dort die Gewichte von Opus und RNNoise.'
}
