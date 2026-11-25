# JG TacRelay - Maintainer: Sarevoc
#requires -Version 7.0
<#
    Schnürt das Serverpaket für den Heimserver aus einem geprüften Gesamtpaket (tools\paket-bauen.ps1). Es wird nichts
    übersetzt, nur umverpackt:

        linux\     funk-server, funk-admin, funk-echo, funk-testclient (bytegleich aus dem Gesamtpaket), dazu
                   HEIMSERVER-PROXMOX.md, BETRIEB.md, HEIMSERVER-HAERTUNG.md, EINRICHTEN.md und UMZUG.md aus main (CRLF),
                   konfig\ samt fail2ban\ aus dem Git-Blob von main (LF, sonst liest systemd die Units nicht sauber; mit
                   profilschluessel.txt für einrichten und pruefen), funk-einrichten.sh aus main (LF, sonst liest bash ein
                   CR mit) und "Profil erneuern.cmd" für Sarevocs Stick (CRLF, für cmd)
        lizenzen\, THIRD-PARTY-NOTICES.txt   bytegleich aus dem Gesamtpaket
        LIESMICH.txt                         aus tools\paket\LIESMICH-Server.txt (Platzhalter unten)
        MANIFEST.txt                         SHA-256 jeder Datei

    Ziel: <Ziel>\<JJJJMMTT_HHMM>_JG-TacRelay-Server-<Fassung>\ und daneben ZIP und .sha256.

    Prüfungen, jede bricht ab:
        1. Der Serverteil des Gesamtpakets (linux\, lizenzen\, THIRD-PARTY-NOTICES.txt) stimmt Datei für Datei mit dessen
           MANIFEST überein, und das Gesamtpaket wurde aus einem sauberen main gebaut (Kopfzeile des MANIFEST).
        2. main ist ausgecheckt und sauber; der Stand des Gesamtpakets liegt in main, und seither hat sich nichts geändert,
           was in die Serverprogramme eingeht (Projekte und Wurzeldateien unten). Die Programme tragen die Fassung aus
           Directory.Build.props und genau diesen Stand (ProductVersion "<Fassung>+<Commit>").
        3. Nach dem Zusammenstellen ist jedes Programm und jede Lizenzdatei bytegleich mit dem MANIFEST des Gesamtpakets.
        4. LIESMICH ohne typografische oder unsichtbare Zeichen (C:\Claude\tools\text-pruefen.ps1), das Paket ohne Geheimnisse
           (C:\Claude\tools\geheimnisse-pruefen.ps1).
        5. Die vier Programme starten mit ihrer Hilfe; server.beispiel.json besteht "pruefen" bis auf das fehlende Profil,
           und eine server.json ohne "totpSchluessel" (Stand 0.1.0) meldet "pruefen" als Fehler. Das Paket ändert sich dabei
           nicht.
        6. Das ZIP enthält genau das Paket (entpackt und verglichen), und MANIFEST.txt passt zum Inhalt.

    Platzhalter der LIESMICH-Vorlage: {{PAKET}}, {{FASSUNG}}, {{GESAMTPAKET}}, {{PROGRAMMSTAND}} (Commit der Programme),
    {{STAND}} (Commit von main für Anleitungen und konfig).

    Gebaut, gestartet oder installiert wird hier nichts außer "dotnet <programm>.dll" mit Hilfe bzw. "pruefen"; kein Fenster,
    kein Netz, keine Geräte. Ausgaben nur unter C:\Claude.

    Vorab-Pakete aus einem Modulzweig (seit 0.6.0, Schritt 3.6): -Zweig modul/s3 verlangt statt main diesen Zweig, für Gesamtpaket
    und Arbeitskopie gleichermaßen (ein Worktree dieses Zweigs, sauber); MANIFEST und Ausgabe nennen den Zweig. Ohne den Schalter gilt
    main wie bisher.

    Aufruf:
        pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\tools\serverpaket-bauen.ps1 -Gesamtpaket C:\Claude\output\test\<zeit>_JG-TacRelay
        pwsh -NoProfile -File ...\tools\serverpaket-bauen.ps1 -Gesamtpaket <ordner> -ArbeitsordnerBehalten
        pwsh -NoProfile -File C:\Claude\work\JG-TacRelay-wt\s3\tools\serverpaket-bauen.ps1 -Gesamtpaket <ordner> -Zweig modul/s3
#>
param(
    [Parameter(Mandatory)][string]$Gesamtpaket,
    [string]$Ziel = 'C:\Claude\output\test',
    [string]$Liesmich = '',
    [string]$Arbeitsordner = 'C:\Claude\tmp\tr-serverpaket',
    [string]$Zweig = 'main',
    [switch]$ArbeitsordnerBehalten
)

$ErrorActionPreference = 'Stop'
# Falle (CODEMAP): dotnet-umgebung.ps1 zuerst einbinden, dann Set-StrictMode. Es bindet C:\Claude\tools\dev-umgebung.ps1 ein,
# das Variablen abfragt, die nicht immer gesetzt sind; und dessen Variablen ($Programme, $Tools, $bin) landen hier. Eigene
# Variablen deshalb erst danach und nie unter diesen Namen.
. (Join-Path $PSScriptRoot 'dotnet-umgebung.ps1')
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$wurzel = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet).Source
$gitExe = (Get-Command git).Source
$utf8 = [Text.UTF8Encoding]::new($false)
if (-not $Liesmich) { $Liesmich = Join-Path $PSScriptRoot 'paket\LIESMICH-Server.txt' }

# Was in die vier Serverprogramme eingeht (ProjectReference der Projekte) und die Wurzeldateien des Baus.
$serverQuellen = @('src/Funk.Server', 'src/Funk.Admin', 'src/Funk.Echo', 'src/Funk.Protokoll', 'src/Funk.ClientKern',
    'tests/Funk.Testclient', 'Directory.Build.props', 'Directory.Packages.props', 'global.json', 'NuGet.config')
# Diese DLLs tragen die Fassung und den Stand in der ProductVersion.
$eigeneDlls = @('funk-server.dll', 'funk-admin.dll', 'funk-echo.dll', 'funk-testclient.dll', 'Funk.Protokoll.dll', 'Funk.ClientKern.dll')
# linux\konfig im Gesamtpaket, wie tools\paket-bauen.ps1 es füllt.
$konfigDateien = @('funk-echo.service', 'funk-server.service', 'server.beispiel.json', 'profilschluessel.txt',
    'fail2ban/filter.d/tacrelay.conf', 'fail2ban/jail.d/tacrelay.conf')
$anleitungen = @('HEIMSERVER-PROXMOX.md', 'BETRIEB.md', 'HEIMSERVER-HAERTUNG.md', 'EINRICHTEN.md', 'UMZUG.md')
# Einrichten und Umzug (A55): Quelle in main, Ziel in linux\, Zeilenende. Das Skript neben den Programmen
# (sudo bash funk-einrichten.sh), der Doppelklick ebenso, weil Sarevocs Stickordner funk-offline eine Kopie von linux ist.
$skripte = @(
    @{ Quelle = 'konfig/funk-einrichten.sh'; Ziel = 'funk-einrichten.sh'; Ende = "`n" },
    @{ Quelle = 'tools/paket/Profil erneuern.cmd'; Ziel = 'Profil erneuern.cmd'; Ende = "`r`n" })

function Schritt([string]$text) { Write-Host ("== " + $text) }

function Voll([string]$pfad) { [IO.Path]::GetFullPath($pfad).TrimEnd('\') }

function Hashliste([string]$ordner) {
    Get-ChildItem -LiteralPath $ordner -Recurse -File | Where-Object { $_.Name -ne 'MANIFEST.txt' -or $_.DirectoryName -ne $ordner } | ForEach-Object {
        [pscustomobject]@{
            Pfad  = $_.FullName.Substring($ordner.Length + 1).Replace('\', '/')
            Bytes = $_.Length
            Sha   = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object Pfad
}

function Manifest([string]$datei) {
    @(Get-Content -LiteralPath $datei -Encoding utf8 | ForEach-Object {
            if ($_ -match '^([0-9a-f]{64})\s+(\d+)\s+(.+)$') { [pscustomobject]@{ Pfad = $Matches[3].Trim(); Bytes = [long]$Matches[2]; Sha = $Matches[1] } }
        } | Sort-Object Pfad)
}

function Git([string[]]$argumente) {
    $aus = & $gitExe -C $wurzel @argumente 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($argumente -join ' '): $($aus -join ' ')" }
    return $aus
}

function GitBlob([string]$rev, [string]$pfad) {
    # Bytes direkt aus der Standardausgabe, ohne Umweg über die Textkodierung der Konsole (Umlaute in den Kommentaren).
    $si = [Diagnostics.ProcessStartInfo]::new($gitExe)
    foreach ($a in '-C', $wurzel, 'cat-file', 'blob', "${rev}:$pfad") { $si.ArgumentList.Add($a) }
    $si.RedirectStandardOutput = $true
    $si.RedirectStandardError = $true
    $si.UseShellExecute = $false
    $proc = [Diagnostics.Process]::Start($si)
    $ms = [IO.MemoryStream]::new()
    $proc.StandardOutput.BaseStream.CopyTo($ms)
    $null = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0 -or $ms.Length -eq 0) { throw "$pfad gibt es in $rev nicht" }
    $b = $ms.ToArray()
    if ($b -contains 13) { throw "CR im Git-Blob von $pfad ($rev); erwartet LF" }
    return , $b
}

function ImTeil([string]$pfad) { $pfad -like 'linux/*' -or $pfad -like 'lizenzen/*' -or $pfad -eq 'THIRD-PARTY-NOTICES.txt' }
function Ersetzt([string]$pfad) {
    $pfad -like 'linux/konfig/*' -or ($anleitungen | Where-Object { $pfad -eq "linux/$_" }) -or ($skripte | Where-Object { $pfad -eq "linux/$($_.Ziel)" })
}

# ---- Vorbedingungen ----
$Gesamtpaket = Voll $Gesamtpaket
$Ziel = Voll $Ziel
$Arbeitsordner = Voll $Arbeitsordner
if (-not $Ziel.StartsWith('C:\Claude\', [StringComparison]::OrdinalIgnoreCase)) { throw "Das Ziel muss unter C:\Claude liegen: $Ziel" }
if (-not $Arbeitsordner.StartsWith('C:\Claude\tmp\', [StringComparison]::OrdinalIgnoreCase)) { throw "Der Arbeitsordner muss unter C:\Claude\tmp liegen: $Arbeitsordner" }
if (-not (Test-Path -LiteralPath (Join-Path $Gesamtpaket 'MANIFEST.txt'))) { throw "Kein Gesamtpaket (MANIFEST.txt fehlt): $Gesamtpaket" }
if (-not (Test-Path -LiteralPath $Liesmich)) { throw "LIESMICH-Vorlage fehlt: $Liesmich" }

# ---- 1. Gesamtpaket gegen sein MANIFEST ----
Schritt 'Gesamtpaket gegen sein MANIFEST'
$gesamtName = Split-Path -Leaf $Gesamtpaket
$kopfzeilen = @(Get-Content -LiteralPath (Join-Path $Gesamtpaket 'MANIFEST.txt') -Encoding utf8 -TotalCount 2)
if ($kopfzeilen.Count -lt 2 -or $kopfzeilen[0] -ne "MANIFEST $gesamtName") { throw "MANIFEST gehört nicht zu $gesamtName (erste Zeile: $($kopfzeilen[0]))" }
if ($kopfzeilen[1] -notmatch '^Erzeugt .* aus (\S+) ([0-9a-f]{7,40})(.*)$') { throw "Kopfzeile des MANIFEST nennt keinen Stand: $($kopfzeilen[1])" }
$gesamtZweig = $Matches[1]
$gesamtCommit = $Matches[2]
if ($gesamtZweig -ne $Zweig) { throw "Das Gesamtpaket kommt nicht aus $Zweig, sondern aus $gesamtZweig" }
if ($Matches[3].Trim()) { throw "Das Gesamtpaket wurde nicht aus einem sauberen Stand gebaut: $($Matches[3].Trim())" }
$soll = @(Manifest (Join-Path $Gesamtpaket 'MANIFEST.txt') | Where-Object { ImTeil $_.Pfad })
if ($soll.Count -eq 0) { throw 'MANIFEST des Gesamtpakets ohne Einträge für linux, lizenzen und THIRD-PARTY-NOTICES.txt' }
$ist = @(Hashliste $Gesamtpaket | Where-Object { ImTeil $_.Pfad })
$abweichung = Compare-Object $soll $ist -Property Pfad, Bytes, Sha
if ($abweichung) { throw "Das Gesamtpaket weicht von seinem MANIFEST ab:`n$($abweichung | Out-String)" }
foreach ($d in $eigeneDlls) { if (-not ($soll | Where-Object Pfad -eq "linux/$d")) { throw "linux/$d fehlt im Gesamtpaket" } }
foreach ($k in $konfigDateien) { if (-not ($soll | Where-Object Pfad -eq "linux/konfig/$k")) { throw "linux/konfig/$k fehlt im Gesamtpaket" } }
$konfigIst = @($soll | Where-Object { $_.Pfad -like 'linux/konfig/*' } | ForEach-Object { $_.Pfad.Substring('linux/konfig/'.Length) } | Sort-Object)
if (($konfigIst -join ',') -ne (($konfigDateien | Sort-Object) -join ',')) { throw "linux/konfig im Gesamtpaket hat andere Dateien: $($konfigIst -join ', ')" }
Write-Host "   $($soll.Count) Dateien des Serverteils stimmen mit dem MANIFEST von $gesamtName überein (gebaut aus $Zweig $gesamtCommit)"

# ---- 2. Stand des Zweigs (main, für Vorab-Pakete ein Modulzweig) ----
Schritt "Stand von $Zweig"
$zweigJetzt = (Git @('branch', '--show-current')) -join ''
if ($zweigJetzt -ne $Zweig) { throw "Die Arbeitskopie steht nicht auf $Zweig, sondern auf $zweigJetzt" }
if (Git @('status', '--porcelain')) { throw 'Die Arbeitskopie hat ungesicherte Änderungen' }
$stand = ((Git @('rev-parse', 'HEAD')) -join '').Trim()
$standKurz = ((Git @('rev-parse', '--short', 'HEAD')) -join '').Trim()
$programmstand = ((Git @('rev-parse', '--verify', "$gesamtCommit^{commit}")) -join '').Trim()
$programmstandKurz = ((Git @('rev-parse', '--short', $programmstand)) -join '').Trim()
& $gitExe -C $wurzel merge-base --is-ancestor $programmstand $stand
if ($LASTEXITCODE -ne 0) { throw "Der Stand des Gesamtpakets ($programmstandKurz) liegt nicht in $Zweig ($standKurz)" }
$codeAenderungen = @(Git (@('log', '--oneline', "$programmstand..$stand", '--') + $serverQuellen))
if ($codeAenderungen.Count -gt 0) { throw "Seit $programmstandKurz hat sich geändert, was in die Serverprogramme eingeht; erst ein neues Gesamtpaket bauen:`n$($codeAenderungen -join "`n")" }
$props = [Text.Encoding]::UTF8.GetString((GitBlob $stand 'Directory.Build.props'))
if ($props -notmatch '<Version>\s*(\d+\.\d+\.\d+)\s*</Version>') { throw 'Keine <Version> in Directory.Build.props' }
$fassung = $Matches[1]
foreach ($d in $eigeneDlls) {
    $v = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Gesamtpaket "linux\$d")).ProductVersion
    if ($v -ne "$fassung+$programmstand") { throw "linux/$d trägt $v, erwartet $fassung+$programmstand" }
}
$doku = @(Git (@('log', '--oneline', "$programmstand..$stand", '--', 'konfig', 'docs/HEIMSERVER-PROXMOX.md', 'docs/BETRIEB.md', 'docs/HEIMSERVER-HAERTUNG.md',
            'docs/EINRICHTEN.md', 'docs/UMZUG.md', 'tools/paket/Profil erneuern.cmd')))
Write-Host "   $Zweig $standKurz, sauber; Programme Fassung $fassung aus $programmstandKurz, seither kein Code am Server, $($doku.Count) Commit(s) an Anleitung und konfig"

$stempel = Get-Date -Format 'yyyyMMdd_HHmm'
$name = "${stempel}_JG-TacRelay-Server-$fassung"
$paket = Join-Path $Ziel $name
$zip = "$paket.zip"
if ((Test-Path -LiteralPath $paket) -or (Test-Path -LiteralPath $zip)) { throw "Gibt es schon: $paket" }
$arbeit = Join-Path $Arbeitsordner $stempel
if (Test-Path -LiteralPath $arbeit) { throw "Arbeitsordner gibt es schon: $arbeit" }
New-Item -ItemType Directory -Force $arbeit | Out-Null

# ---- 3. Zusammenstellen ----
Schritt 'Zusammenstellen'
New-Item -ItemType Directory -Force $paket | Out-Null
Copy-Item -LiteralPath (Join-Path $Gesamtpaket 'linux') -Destination (Join-Path $paket 'linux') -Recurse
Copy-Item -LiteralPath (Join-Path $Gesamtpaket 'lizenzen') -Destination (Join-Path $paket 'lizenzen') -Recurse
Copy-Item -LiteralPath (Join-Path $Gesamtpaket 'THIRD-PARTY-NOTICES.txt') -Destination $paket
# Anleitungen aus main mit CRLF, wie .gitattributes (*.md eol=crlf) sie auscheckt; konfig aus main mit LF wie im Git.
foreach ($a in $anleitungen) {
    $text = $utf8.GetString((GitBlob $stand "docs/$a"))
    [IO.File]::WriteAllText((Join-Path $paket "linux\$a"), $text.Replace("`n", "`r`n"), $utf8)
}
foreach ($k in $konfigDateien) {
    $ziel = Join-Path $paket "linux\konfig\$($k.Replace('/', '\'))"
    New-Item -ItemType Directory -Force (Split-Path -Parent $ziel) | Out-Null
    [IO.File]::WriteAllBytes($ziel, (GitBlob $stand "konfig/$k"))
}
foreach ($s in $skripte) {
    # GitBlob bricht bei einem CR ab: Im Git liegt jede Textdatei mit LF, CRLF entsteht erst hier.
    $text = $utf8.GetString((GitBlob $stand $s.Quelle))
    if ($s.Ende -ne "`n") { $text = $text.Replace("`n", $s.Ende) }
    [IO.File]::WriteAllText((Join-Path $paket "linux\$($s.Ziel)"), $text, $utf8)
}

# Programme und Lizenzen bytegleich, und nichts dazugekommen außer den ersetzten Anleitungen und konfig.
$paketListe = @(Hashliste $paket)
$programme = @($paketListe | Where-Object { -not (Ersetzt $_.Pfad) })
$programmeSoll = @($soll | Where-Object { -not (Ersetzt $_.Pfad) })
$abweichung = Compare-Object $programmeSoll $programme -Property Pfad, Bytes, Sha
if ($abweichung) { throw "Programme oder Lizenzen weichen vom MANIFEST des Gesamtpakets ab:`n$($abweichung | Out-String)" }
$ersetztIst = @($paketListe | Where-Object { Ersetzt $_.Pfad } | ForEach-Object Pfad | Sort-Object)
$ersetztSoll = @(($konfigDateien | ForEach-Object { "linux/konfig/$_" }) + ($anleitungen | ForEach-Object { "linux/$_" }) + ($skripte | ForEach-Object { "linux/$($_.Ziel)" }) | Sort-Object)
if (($ersetztIst -join ',') -ne ($ersetztSoll -join ',')) { throw "Anleitungen und konfig passen nicht: $($ersetztIst -join ', ')" }
Write-Host "   $($programme.Count) Programm- und Lizenzdateien bytegleich, $($ersetztIst.Count) Dateien aus $Zweig $standKurz"

# ---- 4. LIESMICH, Texte und Geheimnisse ----
Schritt 'LIESMICH, Texte und Geheimnisse'
$vorlage = [IO.File]::ReadAllText($Liesmich, $utf8)
$liesmichText = $vorlage.Replace('{{PAKET}}', $name).Replace('{{FASSUNG}}', $fassung).Replace('{{GESAMTPAKET}}', $gesamtName)
$liesmichText = $liesmichText.Replace('{{PROGRAMMSTAND}}', $programmstandKurz).Replace('{{STAND}}', $standKurz)
if ($liesmichText -match '\{\{[A-Z]+\}\}') { throw "Unbekannter Platzhalter in der LIESMICH-Vorlage: $($Matches[0])" }
$liesmichText = ($liesmichText -replace "`r`n", "`n") -replace "`n", "`r`n"
[IO.File]::WriteAllText((Join-Path $paket 'LIESMICH.txt'), $liesmichText, $utf8)
foreach ($t in 'LIESMICH.txt', 'THIRD-PARTY-NOTICES.txt', 'linux\HEIMSERVER-PROXMOX.md', 'linux\BETRIEB.md', 'linux\HEIMSERVER-HAERTUNG.md',
    'linux\EINRICHTEN.md', 'linux\UMZUG.md', 'linux\funk-einrichten.sh', 'linux\Profil erneuern.cmd') {
    & pwsh -NoProfile -File C:\Claude\tools\text-pruefen.ps1 -Pfad (Join-Path $paket $t) -Still
    if ($LASTEXITCODE -ne 0) { throw "Typografische oder unsichtbare Zeichen in $t (C:\Claude\tools\text-pruefen.ps1)" }
}
& pwsh -NoProfile -File C:\Claude\tools\geheimnisse-pruefen.ps1 -Pfad $paket -Still
if ($LASTEXITCODE -ne 0) { throw 'C:\Claude\tools\geheimnisse-pruefen.ps1 meldet Fundstellen im Paket' }
Write-Host '   LIESMICH, THIRD-PARTY-NOTICES und Anleitungen ohne Befund, keine Geheimnisse'

# ---- 5. Programme starten (Hilfe) und Konfiguration prüfen ----
Schritt 'Programme starten, Beispielkonfiguration und Konfiguration von 0.1.0 prüfen'
$vorher = @(Hashliste $paket)
& $dotnet (Join-Path $paket 'linux\funk-server.dll') --hilfe *> "$arbeit\server.log"; if ($LASTEXITCODE -ne 0) { throw 'funk-server --hilfe' }
& $dotnet (Join-Path $paket 'linux\funk-admin.dll') hilfe *> "$arbeit\admin.log"; if ($LASTEXITCODE -ne 0) { throw 'funk-admin hilfe' }
& $dotnet (Join-Path $paket 'linux\funk-echo.dll') --hilfe *> "$arbeit\echo.log"; if ($LASTEXITCODE -ne 0) { throw 'funk-echo --hilfe' }
& $dotnet (Join-Path $paket 'linux\funk-testclient.dll') hilfe *> "$arbeit\testclient.log"; if ($LASTEXITCODE -ne 0) { throw 'funk-testclient hilfe' }
$beispiel = Join-Path $paket 'linux\konfig\server.beispiel.json'
& $dotnet (Join-Path $paket 'linux\funk-server.dll') --konfig $beispiel pruefen *> "$arbeit\pruefen.log"
$code = $LASTEXITCODE
$pruefen = Get-Content -LiteralPath "$arbeit\pruefen.log" -Raw
# Das Beispiel zeigt auf /etc/funk/profil.json, das erst auf dem Server entsteht: genau dieser eine Befund ist erwartet.
$nurProfilFehlt = $pruefen -match '(?s)^\s*Konfiguration ungültig:\s+Serverprofil: Profildatei fehlt: [^\r\n]*\s*$'
if ($code -ne 0 -and -not $nurProfilFehlt) { throw "server.beispiel.json besteht pruefen nicht:`n$pruefen" }
# Eine server.json wie aus 0.1.0, ohne totpSchluessel: pruefen muss das melden, statt still einen Standardpfad zu nehmen.
$ohneTotp = Join-Path $arbeit 'server-ohne-totp.json'
$zeilen = @([IO.File]::ReadAllLines($beispiel, $utf8) | Where-Object { $_ -notmatch '^\s*"totpSchluessel"\s*:' })
[IO.File]::WriteAllLines($ohneTotp, [string[]]$zeilen, $utf8)
& $dotnet (Join-Path $paket 'linux\funk-server.dll') --konfig $ohneTotp pruefen *> "$arbeit\pruefen-ohne-totp.log"
$code = $LASTEXITCODE
$pruefen = Get-Content -LiteralPath "$arbeit\pruefen-ohne-totp.log" -Raw
if ($code -ne 2 -or $pruefen -notmatch 'totpSchluessel fehlt') { throw "server.json ohne totpSchluessel: erwartet Exitcode 2 mit 'totpSchluessel fehlt', war $code`n$pruefen" }
$nachher = @(Hashliste $paket)
if (Compare-Object $vorher $nachher -Property Pfad, Bytes, Sha) { throw 'Das Paket hat sich beim Starten verändert' }
Write-Host '   alle vier starten; Beispiel nur mit "Profildatei fehlt", ohne totpSchluessel Exitcode 2 mit "totpSchluessel fehlt"'

# ---- 6. MANIFEST, ZIP und Gegenprobe ----
Schritt 'MANIFEST, ZIP und Gegenprobe'
$liste = @(Hashliste $paket)
$kopf = @(
    "MANIFEST $name",
    "Erzeugt $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Programme und Lizenzen bytegleich aus $gesamtName (gebaut aus $Zweig $programmstandKurz), Fassung $fassung",
    "Anleitungen (linux/*.md, CRLF), linux/konfig und linux/funk-einrichten.sh (LF wie im Git), linux/Profil erneuern.cmd (CRLF) aus $Zweig $standKurz",
    'Laufzeit im Container: Microsoft.NETCore.App 10 (Paket dotnet-runtime-10.0), linux-x64',
    "$($liste.Count) Dateien. Prüfen: Get-FileHash <datei> -Algorithm SHA256 (Windows) oder sha256sum <datei> (Linux)",
    '',
    'SHA-256                                                           Bytes  Datei')
$manifestText = (($kopf + ($liste | ForEach-Object { '{0}  {1,10}  {2}' -f $_.Sha, $_.Bytes, $_.Pfad })) -join "`r`n") + "`r`n"
[IO.File]::WriteAllText((Join-Path $paket 'MANIFEST.txt'), $manifestText, $utf8)
$gelesen = @(Manifest (Join-Path $paket 'MANIFEST.txt'))
if (Compare-Object $gelesen $liste -Property Pfad, Bytes, Sha) { throw 'MANIFEST.txt passt nicht zum Paket' }

Compress-Archive -LiteralPath $paket -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$zipHash  $(Split-Path $zip -Leaf)`r`n", $utf8)
$entpackt = Join-Path $arbeit 'entpackt'
Expand-Archive -LiteralPath $zip -DestinationPath $entpackt
$imZip = Join-Path $entpackt $name
if (-not (Test-Path -LiteralPath $imZip)) { throw "Im ZIP fehlt der Ordner $name" }
if (Compare-Object $liste @(Hashliste $imZip) -Property Pfad, Bytes, Sha) { throw 'Der ZIP-Inhalt weicht vom Paket ab' }
if ((Get-FileHash -LiteralPath (Join-Path $imZip 'MANIFEST.txt')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $paket 'MANIFEST.txt')).Hash) { throw 'MANIFEST.txt im ZIP weicht ab' }
$weitere = @(Get-ChildItem -LiteralPath $entpackt -Force | Where-Object Name -ne $name)
if ($weitere.Count -gt 0) { throw "Im ZIP liegt mehr als der Paketordner: $($weitere.Name -join ', ')" }
if (-not $ArbeitsordnerBehalten) { Remove-Item -LiteralPath $arbeit -Recurse -Force }

Write-Host ''
Write-Host "Paket:     $paket ($($liste.Count) Dateien + MANIFEST)"
Write-Host "ZIP:       $zip ($([math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 2)) MB), SHA-256 $zipHash"
Write-Host "Programme: Fassung $fassung aus $gesamtName ($Zweig $programmstandKurz); Anleitungen und konfig aus $Zweig $standKurz"
if ($ArbeitsordnerBehalten) { Write-Host "Arbeit:    $arbeit" }
