# JG TacRelay - Maintainer: Sarevoc
#requires -Version 7.0
<#
    Baut das Setup des JG-TacRelay-Clients (Inno Setup 6.7, installer\TacRelay.iss) und prüft es mit innounp.
    Das ZIP-Paket für Server und Probefunk (tools\paket-bauen.ps1) bleibt daneben bestehen.

    Ablauf:
        1. Fassung aus Directory.Build.props (<Version>, die eine Quelle).
        2. Client (win-x64, framework-abhängig, mit den nativen DLLs aus -NativeOrdner) und funk-testserver
           veröffentlichen, in <Arbeitsordner>\<JJJJMMTT_HHMM>\quelle zusammenstellen: Client im Wurzelordner,
           probefunk\ mit Testserver, probefunk.json und probefunk-starten.cmd, lizenzen\, THIRD-PARTY-NOTICES.txt.
           Kein Linux-Server, keine .pdb.
        3. Soll prüfen (Pflichtdateien, nichts Verbotenes, jede DLL eigen oder in THIRD-PARTY-NOTICES), Texte mit
           tools\text-pruefen.ps1, Geheimnisse mit tools\geheimnisse-pruefen.ps1.
        4. Bilder des Assistenten zeichnen (tools\symbol-zeichnen.ps1) und ISCC.exe aufrufen.
        5. Mit innounp listen und entpacken (nach <Arbeitsordner>\<zeit>\entpackt): jede Datei unter {app} gleich der
           Quelle (SHA-256), keine Datei außerhalb des Solls, kein Ziel außer {app}, AppId, AppVersion,
           PrivilegesRequired, AppMutex und kein [Registry] im Skript, Dateiversion von TacRelay.exe und des Setups,
           Symbol in TacRelay.exe und im Setup gleich src\Funk.Client\TacRelay.ico.
        6. Setup nach <Ziel>\<JJJJMMTT_HHMM>_JG-TacRelay-Setup-<Fassung>.exe, daneben .sha256.

    Admin-Setup (-Verwaltung): Dazu kommt verwaltung\Funk.Verwaltung.dll, der Verwaltungsteil (A45), genau die Datei, deren
    SHA-256 beim Bau in TacRelay.dll geschrieben wurde (VerwaltungSha256, src\Funk.Client\Funk.Client.csproj); das Werkzeug
    prüft das vor dem Übersetzen. Ausgabe <zeit>_JG-TacRelay-Admin-Setup-<Fassung>.exe. Das normale Setup darf keinen
    Verwaltungscode enthalten: kein verwaltung\, keine Funk.Verwaltung.*, kein Verweis in deps.json und in den Programmdateien
    im Wurzelordner kein Typ aus Funk.Verwaltung oder Funk.Server und keiner der Admin-Typen (Liste unten). Der Probefunk in
    probefunk\ bringt den Server als Bibliothek mit; ihn betrifft die Typprüfung nicht.

    Serverprofil: Liegt konfig\profil.json im Repository, kommt es in den Programmordner (Dienste\Profilquelle.cs); die
    Profilschlüssel stehen beim Bau aus konfig\profilschluessel.txt in TacRelay.dll. Liegt das Profil dabei, prüft das Werkzeug
    seine Signatur mit funk-admin profil-pruefen gegen genau diese Schlüssel und bricht ab, wenn sie nicht passt (oder die DLL
    einen anderen Schlüssel trägt): Ein solches Setup verbände nie (Befund 01.10.2026). Seit dem Umbau zu JG TacRelay
    (19.10.2026) bricht es auch ab, wenn das Profil fehlt, und sagt, was zu tun ist: das Profil mit funk-admin profil-schreiben
    neu signieren oder mit -OhneProfil bewusst ein Setup ohne Serverprofil bauen. Ein solches Setup verbindet nur im Probefunk;
    am Ende steht das in der Zusammenfassung.

    .NET Desktop Runtime (seit 0.13.2): Das Setup bettet den Installer von Microsoft ein und startet ihn nur, wenn die
    Laufzeit auf dem Ziel-PC fehlt (installer\TacRelay.iss). Ohne -Laufzeit nimmt das Werkzeug die höchste Fassung
    windowsdesktop-runtime-10.x.y-win-x64.exe aus C:\Claude\data\dotnet-laufzeit (dort mit HERKUNFT.txt). Es prüft vor dem
    Übersetzen die Authenticode-Signatur (gültig, Microsoft Corporation) und danach mit innounp, dass der Installer genau
    einmal unter {tmp} liegt, bytegleich, und nirgends unter {app}. Eine neuere Laufzeit: von Microsoft laden (Quelle und
    SHA-512 in releases.json), in den Ordner legen, HERKUNFT.txt nachtragen.

    Das Werkzeug führt das Setup NIE aus, auch nicht still: Es wird nur gebaut, gelesen und von innounp entpackt.
    Aufgerufen werden nur dotnet (mit tools\dotnet-umgebung.ps1), ISCC.exe und innounp.exe mit absolutem Pfad
    (C:\Claude\Eigene_Programme\INSTALLIERT.md) und die eigenen Prüfwerkzeuge. Nichts wird aus dem Netz geladen.

    Aufruf (neben einem Spiel mit niedriger Priorität):
        pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\tools\setup-bauen.ps1
        cmd /c start "" /belownormal /wait /b pwsh -NoProfile -File ...\tools\setup-bauen.ps1 -ArbeitsordnerBehalten
        pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\tools\setup-bauen.ps1 -Verwaltung
        pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\tools\setup-bauen.ps1 -OhneProfil
#>
param(
    [string]$Ziel = 'C:\Claude\output\test',
    [string]$NativeOrdner = 'C:\Claude\work\JG-TacRelay\native\out',
    [string]$Arbeitsordner = 'C:\Claude\tmp\tr-setup',
    [switch]$ArbeitsordnerBehalten,
    # Admin-Setup mit dem Verwaltungsteil (A45); ohne den Schalter das normale Setup für alle.
    [switch]$Verwaltung,
    # Bewusst ohne Serverprofil bauen (nur Probefunk), etwa solange konfig\profil.json fehlt oder neu signiert werden muss.
    [switch]$OhneProfil,
    # Installer der .NET Desktop Runtime 10 von Microsoft; leer heißt die höchste Fassung in C:\Claude\data\dotnet-laufzeit.
    [string]$Laufzeit = ''
)

$ErrorActionPreference = 'Stop'
# Vor Set-StrictMode einbinden: dev-umgebung.ps1 fragt Variablen ab, die nicht immer gesetzt sind. Setzt auch TEMP
# auf C:\Claude\tmp; ISCC und innounp legen ihre Zwischenstände dort ab.
. (Join-Path $PSScriptRoot 'dotnet-umgebung.ps1')
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$iscc = 'C:\Claude\Eigene_Programme\innosetup\ISCC.exe'
$innounp = 'C:\Claude\Eigene_Programme\innounp\innounp.exe'
$wurzel = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet).Source
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { 'C:\Claude\tools\nuget-packages' }
$utf8 = [Text.UTF8Encoding]::new($false)
$symbolDatei = Join-Path $wurzel 'src\Funk.Client\TacRelay.ico'
# Die Kennung des Programms, wie sie in installer\TacRelay.iss steht (seit JG TacRelay neu, danach nie ändern).
$appId = '{E2908D2D-EA8B-4B2D-B4F1-CBDE75DE42AE}'

function Schritt([string]$text) { Write-Host ("== " + $text) }

function Textdatei([string]$pfad, [string]$inhalt) {
    # CRLF, UTF-8 ohne BOM: Editor und cmd unter Windows lesen das ohne Umwege.
    $normal = ($inhalt -replace "`r`n", "`n") -replace "`n", "`r`n"
    New-Item -ItemType Directory -Force (Split-Path -Parent $pfad) | Out-Null
    [IO.File]::WriteAllText($pfad, $normal, $utf8)
}

function Veroeffentlichen([string]$projekt, [string]$ausgabe, [string[]]$zusatz, [string]$log) {
    $argumente = @('publish', (Join-Path $wurzel $projekt), '-c', 'Release', '-o', $ausgabe, '--nologo', '--disable-build-servers') + $zusatz
    & $dotnet @argumente *> $log
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $projekt gescheitert:`n$((Get-Content $log -Tail 25) -join "`n")" }
}

function Kopieren([string]$von, [string]$nach) {
    # Ohne PDB: kleiner, und keine Pfade des Bau-PCs im Setup.
    New-Item -ItemType Directory -Force $nach | Out-Null
    foreach ($d in Get-ChildItem $von -Recurse -File | Where-Object Extension -ne '.pdb') {
        $rel = $d.FullName.Substring($von.TrimEnd('\').Length + 1)
        $z = Join-Path $nach $rel
        New-Item -ItemType Directory -Force (Split-Path -Parent $z) | Out-Null
        Copy-Item $d.FullName $z -Force
    }
}

function Hashliste([string]$ordner) {
    $basis = $ordner.TrimEnd('\')
    Get-ChildItem $basis -Recurse -File | ForEach-Object {
        [pscustomobject]@{
            Pfad  = $_.FullName.Substring($basis.Length + 1)
            Bytes = $_.Length
            Sha   = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object Pfad
}

function InDerZone([string]$pfad) {
    $voll = [IO.Path]::GetFullPath($pfad)
    if (-not $voll.StartsWith('C:\Claude\', [StringComparison]::OrdinalIgnoreCase)) { throw "Nur unter C:\Claude: $voll" }
    return $voll
}

# Bilder eines Symbols als Liste "BreitexHöhe SHA-256 der Bilddaten": aus einer .ico-Datei oder aus der ersten
# Symbolgruppe (RT_GROUP_ICON) einer Programmdatei. Die Programmdatei wird dafür nur als Datendatei geladen
# (LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE), nie ausgeführt.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

public static class TrSymbolLeser
{
    private delegate bool NamenRueckruf(IntPtr modul, IntPtr typ, IntPtr name, IntPtr param);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string pfad, IntPtr datei, uint flags);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr modul);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr modul, IntPtr name, IntPtr typ);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr modul, IntPtr info);
    [DllImport("kernel32")]
    private static extern IntPtr LockResource(IntPtr daten);
    [DllImport("kernel32", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr modul, IntPtr info);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool EnumResourceNamesW(IntPtr modul, IntPtr typ, NamenRueckruf rueckruf, IntPtr param);

    private static byte[] Ressource(IntPtr modul, IntPtr name, int typ)
    {
        var info = FindResourceW(modul, name, (IntPtr)typ);
        if (info == IntPtr.Zero) throw new InvalidDataException("Ressource fehlt (Typ " + typ + ")");
        var groesse = (int)SizeofResource(modul, info);
        var zeiger = LockResource(LoadResource(modul, info));
        var daten = new byte[groesse];
        Marshal.Copy(zeiger, daten, 0, groesse);
        return daten;
    }

    private static string Eintrag(int breite, int hoehe, byte[] bild)
    {
        return (breite == 0 ? 256 : breite) + "x" + (hoehe == 0 ? 256 : hoehe) + " " + Convert.ToHexString(SHA256.HashData(bild));
    }

    public static List<string> AusIco(string pfad)
    {
        var d = File.ReadAllBytes(pfad);
        var liste = new List<string>();
        int anzahl = BitConverter.ToUInt16(d, 4);
        for (var i = 0; i < anzahl; i++)
        {
            var e = 6 + 16 * i;
            var laenge = BitConverter.ToInt32(d, e + 8);
            var versatz = BitConverter.ToInt32(d, e + 12);
            liste.Add(Eintrag(d[e], d[e + 1], d.AsSpan(versatz, laenge).ToArray()));
        }
        liste.Sort(StringComparer.Ordinal);
        return liste;
    }

    public static List<string> AusProgramm(string pfad)
    {
        var modul = LoadLibraryExW(pfad, IntPtr.Zero, 0x2 | 0x20);
        if (modul == IntPtr.Zero) throw new IOException("Nicht als Datendatei ladbar: " + pfad + " (" + Marshal.GetLastWin32Error() + ")");
        var liste = new List<string>();
        Exception fehler = null;
        try
        {
            EnumResourceNamesW(modul, (IntPtr)14, (m, t, name, p) =>
            {
                try
                {
                    var gruppe = Ressource(m, name, 14);
                    int anzahl = BitConverter.ToUInt16(gruppe, 4);
                    for (var i = 0; i < anzahl; i++)
                    {
                        var e = 6 + 14 * i;
                        var id = BitConverter.ToUInt16(gruppe, e + 12);
                        liste.Add(Eintrag(gruppe[e], gruppe[e + 1], Ressource(m, (IntPtr)id, 3)));
                    }
                }
                catch (Exception ex)
                {
                    fehler = ex;
                }
                return false;
            }, IntPtr.Zero);
        }
        finally
        {
            FreeLibrary(modul);
        }
        if (fehler != null) throw fehler;
        liste.Sort(StringComparer.Ordinal);
        return liste;
    }
}
'@

# ---- Vorbedingungen ----
$Ziel = InDerZone $Ziel
$Arbeitsordner = InDerZone $Arbeitsordner
foreach ($p in $iscc, $innounp) { if (-not (Test-Path -LiteralPath $p)) { throw "Fehlt: $p (C:\Claude\Eigene_Programme\INSTALLIERT.md)" } }
if (-not (Test-Path (Join-Path $NativeOrdner 'win-x64\opus.dll'))) { throw "Native DLLs fehlen in $NativeOrdner\win-x64." }
if (-not (Test-Path (Join-Path $NativeOrdner 'lizenzen'))) { throw "Lizenzen der nativen DLLs fehlen in $NativeOrdner\lizenzen." }

$props = [xml](Get-Content (Join-Path $wurzel 'Directory.Build.props') -Raw)
$versionen = @($props.SelectNodes('/Project/PropertyGroup/Version'))
if ($versionen.Count -ne 1) { throw "Directory.Build.props: genau ein <Version> erwartet, gefunden $($versionen.Count)." }
$fassung = $versionen[0].InnerText.Trim()
if ($fassung -notmatch '^\d+\.\d+\.\d+$') { throw "Directory.Build.props: <Version> '$fassung' ist nicht Major.Minor.Patch." }

$laufzeiten = & $dotnet --list-runtimes
$desktop = @($laufzeiten | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 10\.' } | ForEach-Object { ($_ -split ' ')[0..1] -join ' ' })
$laufzeitText = if ($desktop.Count -gt 0) { $desktop[-1] } else { 'auf dem Bau-PC nicht gefunden' }

# Der eingebettete Installer der .NET Desktop Runtime: Fassung aus dem Namen, Signatur von Microsoft.
$laufzeitMuster = '^windowsdesktop-runtime-(10\.\d+\.\d+)-win-x64\.exe$'
if (-not $Laufzeit) {
    $kandidaten = @(Get-ChildItem 'C:\Claude\data\dotnet-laufzeit' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match $laufzeitMuster } |
            Sort-Object { [version]([regex]::Match($_.Name, $laufzeitMuster).Groups[1].Value) })
    if ($kandidaten.Count -eq 0) { throw 'Kein Installer der .NET Desktop Runtime 10 in C:\Claude\data\dotnet-laufzeit (windowsdesktop-runtime-10.x.y-win-x64.exe von Microsoft).' }
    $Laufzeit = $kandidaten[-1].FullName
}
$Laufzeit = [IO.Path]::GetFullPath($Laufzeit)
if (-not (Test-Path -LiteralPath $Laufzeit)) { throw "Laufzeit-Installer fehlt: $Laufzeit" }
$laufzeitName = Split-Path -Leaf $Laufzeit
if ($laufzeitName -notmatch $laufzeitMuster) { throw "Laufzeit-Installer heißt nicht windowsdesktop-runtime-10.x.y-win-x64.exe: $laufzeitName" }
$laufzeitFassung = $Matches[1]
$signatur = Get-AuthenticodeSignature -LiteralPath $Laufzeit
if ($signatur.Status -ne 'Valid' -or $signatur.SignerCertificate.Subject -notmatch '(^|, )O=Microsoft Corporation(,|$)') {
    throw "Laufzeit-Installer nicht gültig von Microsoft signiert: $($signatur.Status), $($signatur.SignerCertificate.Subject)"
}
$laufzeitSha = (Get-FileHash -LiteralPath $Laufzeit -Algorithm SHA256).Hash.ToLowerInvariant()

$stempel = Get-Date -Format 'yyyyMMdd_HHmm'
$art = if ($Verwaltung) { 'Admin-Setup' } else { 'Setup' }
$name = "${stempel}_JG-TacRelay-$art-$fassung"
$setupZiel = Join-Path $Ziel "$name.exe"
$hashZiel = "$setupZiel.sha256"
if ((Test-Path $setupZiel) -or (Test-Path $hashZiel)) { throw "Gibt es schon: $setupZiel" }
$arbeit = Join-Path $Arbeitsordner $stempel
if (Test-Path $arbeit) { throw "Arbeitsordner gibt es schon (zwei Läufe in derselben Minute?): $arbeit" }
$pub = Join-Path $arbeit 'publish'
$quelle = Join-Path $arbeit 'quelle'
$bilder = Join-Path $arbeit 'bilder'
$entpackt = Join-Path $arbeit 'entpackt'
New-Item -ItemType Directory -Force $pub, $quelle, $Ziel | Out-Null
$commit = (git -C $wurzel rev-parse --short HEAD 2>$null)
$zweig = (git -C $wurzel rev-parse --abbrev-ref HEAD 2>$null)
$sauber = -not (git -C $wurzel status --porcelain 2>$null)

# ---- Veröffentlichen ----
Schritt "Fassung $fassung (Directory.Build.props), Arbeitsordner $arbeit"
Schritt 'Client (win-x64, framework-abhängig)'
Veroeffentlichen 'src\Funk.Client\Funk.Client.csproj' "$pub\client" @('-r', 'win-x64', '--self-contained', 'false', "-p:FunkNativeOrdner=$NativeOrdner\win-x64") "$arbeit\client.log"
Schritt 'Testserver (Probefunk)'
Veroeffentlichen 'tests\Funk.Testserver\Funk.Testserver.csproj' "$pub\probefunk" @() "$arbeit\testserver.log"

# ---- Quelle zusammenstellen ----
Schritt 'Quelle zusammenstellen'
Kopieren "$pub\client" $quelle
Kopieren "$pub\probefunk" "$quelle\probefunk"
# Im Testserver ist funk-server nur Bibliothek; sein Programmstarter und seine Startdateien gehören nicht dazu.
Remove-Item "$quelle\probefunk\funk-server.exe", "$quelle\probefunk\funk-server.deps.json", "$quelle\probefunk\funk-server.runtimeconfig.json" -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$quelle\probefunk\konfig" | Out-Null
Copy-Item "$wurzel\konfig\probefunk.json" "$quelle\probefunk\konfig\"
Textdatei "$quelle\probefunk\probefunk-starten.cmd" ([IO.File]::ReadAllText("$wurzel\installer\probefunk-starten.cmd"))
Textdatei "$quelle\THIRD-PARTY-NOTICES.txt" ([IO.File]::ReadAllText("$wurzel\THIRD-PARTY-NOTICES.txt"))

# ---- Serverprofil (eingebaut) ----
$programmText = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes("$quelle\TacRelay.dll"))
$profilDatei = Join-Path $wurzel 'konfig\profil.json'
$profilHinweis = 'keines (nur Probefunk)'
# Was zu tun ist, wenn das Profil fehlt oder nicht passt; steht in jeder Abbruchmeldung zum Profil.
$profilAusweg = 'Abhilfe: konfig\profil.json mit funk-admin profil-schreiben neu signieren (Anleitung HEIMSERVER-PROXMOX.md) ' +
    'oder bewusst ohne Serverprofil bauen: setup-bauen.ps1 -OhneProfil (das Setup verbindet dann nur im Probefunk).'
if ($OhneProfil) {
    $profilHinweis = 'keines, bewusst ohne Serverprofil gebaut (-OhneProfil): verbinden nur im Probefunk'
    Write-Warning ('-OhneProfil: Das Setup bringt kein Serverprofil mit' + $(if (Test-Path -LiteralPath $profilDatei) { ', konfig\profil.json bleibt draußen' } else { '' }) +
        '; verbinden geht nur im Probefunk.')
}
elseif (Test-Path -LiteralPath $profilDatei) {
    Copy-Item -LiteralPath $profilDatei -Destination "$quelle\profil.json"
    $profilHinweis = 'konfig\profil.json'
    if ($programmText.IndexOf('FunkProfilschluessel', [StringComparison]::Ordinal) -lt 0) {
        Write-Warning 'konfig\profil.json ist dabei, aber TacRelay.dll trägt keinen Profilschlüssel (konfig\profilschluessel.txt fehlt?): Verbinden geht nur im Probefunk.'
    }
}
else {
    throw "Kein Serverprofil: konfig\profil.json fehlt, ein Setup ohne Profil verbindet nur im Probefunk. $profilAusweg"
}

# ---- Serverprofil gegen den eingebauten Profilschlüssel prüfen ----
# Ein Profil, dessen Signatur nicht zum Schlüssel in TacRelay.dll passt, nähme kein Client an; dann lieber kein Setup.
if (-not $OhneProfil -and (Test-Path -LiteralPath $profilDatei)) {
    $schluesselDatei = Join-Path $wurzel 'konfig\profilschluessel.txt'
    $spkis = @(if (Test-Path -LiteralPath $schluesselDatei) { Get-Content -LiteralPath $schluesselDatei | ForEach-Object { $_.Trim() } | Where-Object { $_ } })
    if ($spkis.Count -eq 0) { throw "Profil passt nicht: konfig\profil.json ist dabei, aber konfig\profilschluessel.txt fehlt oder ist leer. $profilAusweg" }
    foreach ($spki in $spkis) {
        if ($programmText.IndexOf($spki, [StringComparison]::Ordinal) -lt 0) {
            throw "Profil passt nicht: TacRelay.dll trägt den Profilschlüssel aus konfig\profilschluessel.txt nicht (Datei nach dem Bau geändert?). $profilAusweg"
        }
    }
    Schritt 'Serverprofil gegen den eingebauten Profilschlüssel prüfen (funk-admin profil-pruefen)'
    Veroeffentlichen 'src\Funk.Admin\Funk.Admin.csproj' "$arbeit\funk-admin" @() "$arbeit\funk-admin.log"
    $pruefArgumente = @("$arbeit\funk-admin\funk-admin.dll", 'profil-pruefen', '--profil', $profilDatei) + @($spkis | ForEach-Object { '--spki'; $_ })
    $pruefung = @(& $dotnet @pruefArgumente 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { throw "Profil passt nicht zum eingebauten Profilschlüssel (funk-admin profil-pruefen):`n$($pruefung -join "`n")`n$profilAusweg" }
    $profilHinweis = 'konfig\profil.json, Signatur gegen den eingebauten Profilschlüssel geprüft (' + $pruefung[0] + ')'
}

# ---- Verwaltungsteil (nur Admin-Setup) ----
if ($Verwaltung) {
    Schritt 'Verwaltungsteil (Admin-Setup)'
    # Gebaut beim Veröffentlichen des Clients (Ziel VerwaltungsteilPruefsumme in Funk.Client.csproj), in derselben Konfiguration.
    $verwaltungDatei = Join-Path $wurzel 'src\Funk.Verwaltung\bin\Release\net10.0-windows\Funk.Verwaltung.dll'
    if (-not (Test-Path -LiteralPath $verwaltungDatei)) { throw "Fehlt: $verwaltungDatei (entsteht beim Veröffentlichen des Clients)." }
    New-Item -ItemType Directory -Force "$quelle\verwaltung" | Out-Null
    Copy-Item -LiteralPath $verwaltungDatei -Destination "$quelle\verwaltung\Funk.Verwaltung.dll"
    $verwaltungSha = (Get-FileHash "$quelle\verwaltung\Funk.Verwaltung.dll" -Algorithm SHA256).Hash
    if ($programmText.IndexOf($verwaltungSha, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Die Prüfsumme des Verwaltungsteils ($verwaltungSha) steht nicht in TacRelay.dll (VerwaltungSha256): Der Client würde ihn nicht laden."
    }
}

# ---- Lizenzen (wie tools\paket-bauen.ps1, dazu Inno Setup für Setup und Deinstallationsprogramm) ----
Schritt 'Lizenzen'
$liz = "$quelle\lizenzen"
Kopieren (Join-Path $NativeOrdner 'lizenzen') "$liz\native"
$mit = [IO.File]::ReadAllText("$wurzel\tools\paket\MIT-Lizenz.txt")
Textdatei "$liz\dotnet\LICENSE.txt" ($mit.Replace('{{COPYRIGHT}}', 'Copyright (c) .NET Foundation and Contributors'))
Textdatei "$liz\innosetup\LICENSE.txt" ([IO.File]::ReadAllText((Join-Path (Split-Path -Parent $iscc) 'license.txt')))
$ungeklaert = [Collections.Generic.List[string]]::new()
foreach ($depsDatei in "$quelle\TacRelay.deps.json", "$quelle\probefunk\funk-testserver.deps.json") {
    $deps = Get-Content $depsDatei -Raw | ConvertFrom-Json
    foreach ($eintrag in $deps.libraries.PSObject.Properties) {
        if ($eintrag.Value.type -ne 'package') { continue }
        $paketname, $version = $eintrag.Name -split '/'
        $zielordner = Join-Path "$liz\nuget" $paketname.ToLowerInvariant()
        if (Test-Path $zielordner) { continue }
        $herkunft = Join-Path $nuget ($paketname.ToLowerInvariant() + '\' + $version)
        $texte = @(Get-ChildItem $herkunft -File | Where-Object { $_.Name -match '^(LICENSE|License|THIRD-PARTY-NOTICES|ThirdPartyNotices)' })
        if ($texte.Count -gt 0) {
            New-Item -ItemType Directory -Force $zielordner | Out-Null
            foreach ($d in $texte) { Copy-Item $d.FullName $zielordner }
            continue
        }
        $nuspec = [xml](Get-Content (Get-ChildItem $herkunft -Filter '*.nuspec' | Select-Object -First 1).FullName -Raw)
        $lizenz = $nuspec.package.metadata.license
        if ($lizenz -and $lizenz.type -eq 'expression' -and $lizenz.'#text' -eq 'MIT') {
            Textdatei "$zielordner\LICENSE.txt" ($mit.Replace('{{COPYRIGHT}}', 'Copyright ' + $nuspec.package.metadata.copyright + ' (' + $paketname + ' ' + $version + ', laut nuspec)'))
        }
        else {
            $ungeklaert.Add("$paketname $version`: keine Lizenzdatei und kein MIT-Ausdruck in der nuspec")
        }
    }
}

# ---- Soll ----
Schritt 'Soll prüfen'
$liste = @(Hashliste $quelle)
$pfade = @($liste | ForEach-Object Pfad)
$pflicht = @(
    'TacRelay.exe', 'TacRelay.dll', 'TacRelay.runtimeconfig.json', 'TacRelay.deps.json', 'Glasdesign.dll',
    'opus.dll', 'rnnoise.dll', 'speexdsp_resampler.dll', 'webrtc_apm.dll', 'deepfilternet.dll',
    'probefunk\funk-testserver.exe', 'probefunk\funk-testserver.dll', 'probefunk\funk-testserver.runtimeconfig.json',
    'probefunk\funk-testserver.deps.json', 'probefunk\konfig\probefunk.json', 'probefunk\probefunk-starten.cmd',
    'THIRD-PARTY-NOTICES.txt', 'lizenzen\dotnet\LICENSE.txt', 'lizenzen\innosetup\LICENSE.txt')
if ($Verwaltung) { $pflicht += 'verwaltung\Funk.Verwaltung.dll' }
$befunde = [Collections.Generic.List[string]]::new()
foreach ($p in $pflicht) { if ($p -notin $pfade) { $befunde.Add("fehlt: $p") } }
# Verboten: Debugsymbole, der Linux-Server und seine Werkzeuge, Dienstdateien und Beispielkonfiguration, Anleitungen
# für den Heimserver, und jeder Ort außer dem Wurzelordner, probefunk\ und lizenzen\.
$verboten = '\.pdb$|(^|\\)funk-(admin|echo|testclient)\.|(^|\\)funk-server\.(exe|deps\.json|runtimeconfig\.json)$|\.service$|server\.beispiel\.json$|HEIMSERVER|BETRIEB\.md$|(^|\\)linux\\'
# Das normale Setup trägt keinen Verwaltungscode (A45, ANMELDUNG-SICHERHEIT "Admin-Teile nur für Admins").
if (-not $Verwaltung) { $verboten += '|(^|\\)verwaltung\\|(^|\\)Funk\.Verwaltung\.' }
$ordnerErlaubt = if ($Verwaltung) { '^(probefunk|lizenzen|verwaltung)\\' } else { '^(probefunk|lizenzen)\\' }
foreach ($p in $pfade) {
    if ($p -match $verboten) { $befunde.Add("verboten: $p") }
    if ($p.Contains('\') -and $p -notmatch $ordnerErlaubt) { $befunde.Add("außerhalb der erlaubten Ordner: $p") }
}
if (-not $Verwaltung) {
    if ((Get-Content "$quelle\TacRelay.deps.json" -Raw).Contains('Funk.Verwaltung')) { $befunde.Add('TacRelay.deps.json nennt Funk.Verwaltung') }
    # Typen der Programmdateien im Wurzelordner, nur gelesen (System.Reflection.Metadata), nie geladen.
    # Dieselbe Liste wie VerboteneTypen in tests\Funk.Tests\VerwaltungsteilTests.cs (der Test gleicht sie ab).
    $adminTypen = @('Adminzugang', 'TpmAdminSchluessel', 'Adminschluesselwahl', 'Verwaltungsdienst', 'VerwaltungViewModel', 'AntraegeViewModel',
        'MitgliederViewModel', 'KatalogViewModel', 'QrCode', 'QrBild', 'VerwaltungAnsicht', 'Adminbuch', 'Mitgliederbuch',
        'TotpTresor', 'Totp', 'Adminvoraussetzungen', 'PcpTpmPruefung', 'Tpmdeutung')
    foreach ($d in Get-ChildItem -LiteralPath $quelle -File | Where-Object { $_.Extension -in '.dll', '.exe' }) {
        $strom = [IO.File]::OpenRead($d.FullName)
        $pe = $null
        try {
            $pe = [Reflection.PortableExecutable.PEReader]::new($strom)
            if (-not $pe.HasMetadata) { continue }
            $md = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            if (-not $md.IsAssembly) { continue }
            foreach ($h in $md.TypeDefinitions) {
                $t = $md.GetTypeDefinition($h)
                $typName = $md.GetString($t.Name)
                $raum = $md.GetString($t.Namespace)
                if ($typName -in $adminTypen -or $raum -match '^Funk\.(Verwaltung|Server)(\.|$)') { $befunde.Add("Verwaltungscode im normalen Setup: $($d.Name) $raum.$typName") }
            }
        }
        finally {
            if ($pe) { $pe.Dispose() }
            $strom.Dispose()
        }
    }
}
$notices = [IO.File]::ReadAllText("$quelle\THIRD-PARTY-NOTICES.txt")
$eigen = '^(Funk\.[A-Za-z]+|TacRelay|funk-(server|testserver))\.dll$'
foreach ($d in Get-ChildItem $quelle -Recurse -File -Include '*.dll', '*.exe') {
    if ($d.Name -match $eigen) { continue }
    if (-not $notices.Contains($d.Name)) { $ungeklaert.Add("$($d.FullName.Substring($quelle.Length + 1)): nicht in THIRD-PARTY-NOTICES") }
}
foreach ($u in $ungeklaert) { $befunde.Add("ungeklärt: $u") }
if ($befunde.Count -gt 0) { throw "Soll nicht erfüllt:`n  $($befunde -join "`n  ")" }
Textdatei "$arbeit\soll.txt" (($liste | ForEach-Object { '{0}  {1,10}  {2}' -f $_.Sha, $_.Bytes, $_.Pfad }) -join "`n")

# ---- Texte und Geheimnisse ----
Schritt 'Texte und Geheimnisse prüfen'
foreach ($t in "$quelle\probefunk\probefunk-starten.cmd", "$quelle\THIRD-PARTY-NOTICES.txt", "$wurzel\installer\TacRelay.iss") {
    & pwsh -NoProfile -File C:\Claude\tools\text-pruefen.ps1 -Pfad $t -Still
    if ($LASTEXITCODE -ne 0) { throw "Typografische oder unsichtbare Zeichen in $t (tools\text-pruefen.ps1)." }
}
& pwsh -NoProfile -File C:\Claude\tools\geheimnisse-pruefen.ps1 -Pfad $quelle -Still
if ($LASTEXITCODE -ne 0) { throw 'tools\geheimnisse-pruefen.ps1 meldet Fundstellen in der Quelle des Setups.' }

# ---- Bilder und Symbol ----
Schritt 'Bilder des Assistenten zeichnen (tools\symbol-zeichnen.ps1)'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'symbol-zeichnen.ps1') -Ziel "$arbeit\symbol-neu.ico" -AssistentOrdner $bilder *> "$arbeit\symbol.log"
if ($LASTEXITCODE -ne 0) { throw "symbol-zeichnen.ps1 gescheitert: $((Get-Content "$arbeit\symbol.log") -join ' ')" }
$symbolHinweis = if ((Get-FileHash "$arbeit\symbol-neu.ico").Hash -eq (Get-FileHash $symbolDatei).Hash) {
    'aus tools\symbol-zeichnen.ps1 neu gezeichnet und gleich src\Funk.Client\TacRelay.ico'
}
else {
    # Kein Abbruch: GDI+ kann auf einem anderen Windows um Nuancen anders glätten. Ins Setup kommt die Datei aus dem
    # Repository.
    Write-Warning 'src\Funk.Client\TacRelay.ico weicht vom neu gezeichneten Symbol ab (tools\symbol-zeichnen.ps1 ausführen?).'
    'weicht vom neu gezeichneten ab'
}

# ---- ISCC ----
Schritt 'Setup übersetzen (ISCC.exe)'
$isccArgumente = @("/DQuelle=$quelle", "/DBilder=$bilder", "/DFassung=$fassung", "/DAusgabe=$arbeit", "/DAusgabeName=$name",
    "/DLaufzeitInstaller=$Laufzeit", "/DLaufzeitFassung=$laufzeitFassung")
if ($Verwaltung) { $isccArgumente += '/DVerwaltung=1' }
& $iscc @isccArgumente (Join-Path $wurzel 'installer\TacRelay.iss') *> "$arbeit\iscc.log"
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe gescheitert (Exitcode $LASTEXITCODE):`n$((Get-Content "$arbeit\iscc.log" -Tail 25) -join "`n")" }
$setupDatei = Join-Path $arbeit "$name.exe"
if (-not (Test-Path -LiteralPath $setupDatei)) { throw "ISCC meldet Erfolg, aber $setupDatei fehlt." }

# ---- Prüfung mit innounp: nur lesen und entpacken, nie ausführen ----
Schritt 'Prüfung mit innounp (listen, entpacken, vergleichen)'
$pruefBefunde = [Collections.Generic.List[string]]::new()
# -a: auch gleiche Dateien unter mehreren Namen (Inno speichert sie nur einmal), -m: auch Skript und embedded\.
& $innounp -v -a -m -b -h -o -u $setupDatei *> "$arbeit\innounp-liste.txt"
if ($LASTEXITCODE -ne 0) { throw "innounp -v: Exitcode $LASTEXITCODE, siehe $arbeit\innounp-liste.txt" }
& $innounp -x -a -m -b -y -q -h -o -u "-d$entpackt" $setupDatei *> "$arbeit\innounp-entpacken.txt"
if ($LASTEXITCODE -ne 0) { throw "innounp -x: Exitcode $LASTEXITCODE, siehe $arbeit\innounp-entpacken.txt" }

# Die Liste: je Datei "Größe Datum Zeit Ziel". Erlaubt sind nur Ziele unter {app}\, das nachgebaute Skript und die
# eingebetteten Teile von Inno (embedded\: übersetzter Code, Sprachdatei, Bilder des Assistenten).
$gelistet = @(Get-Content "$arbeit\innounp-liste.txt" | ForEach-Object {
        if ($_ -match '^\s*\d+\s+\d{4}-\d{2}-\d{2} \d{2}:\d{2}\s+(.+?)\s*$') { $Matches[1] }
    })
# Einzige Ausnahme: der Installer der .NET-Laufzeit unter {tmp} (dontcopy, nur bei Bedarf entpackt), genau einmal.
$laufzeitZiel = '{tmp}\' + $laufzeitName
foreach ($z in $gelistet) {
    if ($z -ne $laufzeitZiel -and $z -notmatch '^(\{app\}\\.+|install_script\.iss|embedded\\[^\\]+)$') { $pruefBefunde.Add("Ziel außerhalb von {app}: $z") }
}
$laufzeitGelistet = @($gelistet | Where-Object { $_ -eq $laufzeitZiel }).Count
if ($laufzeitGelistet -ne 1) { $pruefBefunde.Add("Laufzeit-Installer $laufzeitZiel ist $laufzeitGelistet-mal gelistet, erwartet einmal") }
if (@($gelistet | Where-Object { $_ -like '{app}\*' -and $_ -like '*windowsdesktop-runtime*' }).Count -gt 0) { $pruefBefunde.Add('Laufzeit-Installer liegt unter {app}') }
$appGelistet = @($gelistet | Where-Object { $_ -like '{app}\*' } | ForEach-Object { $_.Substring(6) } | Sort-Object)
$sollPfade = @($liste | ForEach-Object Pfad | Sort-Object)
if (($appGelistet -join '|') -ne ($sollPfade -join '|')) {
    $pruefBefunde.Add("innounp listet unter {app} nicht genau das Soll ($($appGelistet.Count) gegen $($sollPfade.Count)): " +
        ((Compare-Object $sollPfade $appGelistet | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join '; '))
}

# Entpackt: unter {app} genau die Quelle, Datei für Datei gleich; daneben nur Skript und embedded\.
$app = Join-Path $entpackt '{app}'
if (-not (Test-Path -LiteralPath $app)) { throw "innounp hat nichts nach {app} entpackt: $entpackt" }
$ist = @(Hashliste $app)
$abweichend = @(Compare-Object $liste $ist -Property Pfad, Sha)
foreach ($a in $abweichend) { $pruefBefunde.Add("$(if ($a.SideIndicator -eq '<=') { 'fehlt im Setup' } else { 'nicht im Soll' }): $($a.Pfad)") }
foreach ($d in Get-ChildItem $entpackt -Recurse -File) {
    $rel = $d.FullName.Substring($entpackt.Length + 1)
    if ($rel -eq $laufzeitZiel) {
        if ((Get-FileHash -LiteralPath $d.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $laufzeitSha) { $pruefBefunde.Add("Laufzeit-Installer im Setup weicht vom Original ab: $rel") }
        continue
    }
    if ($rel -notmatch '^(\{app\}\\|install_script\.iss$|embedded\\)') { $pruefBefunde.Add("entpackt außerhalb von {app}: $rel") }
}
if (-not (Test-Path -LiteralPath (Join-Path $entpackt $laufzeitZiel))) { $pruefBefunde.Add("Laufzeit-Installer nicht entpackt: $laufzeitZiel") }

# Das von innounp nachgebaute Skript: Kennung, Fassung, Rechte, kein [Registry], und der AppMutex, den das Programm hält
# (Einzelinstanz.LaeuftMarke; dass er dort gleich heißt, prüft SetupTests).
$skript = Get-Content (Join-Path $entpackt 'install_script.iss') -Raw
if ($skript -notmatch ('(?m)^AppId=\{?' + [regex]::Escape($appId) + '\s*$')) { $pruefBefunde.Add("AppId nicht $appId") }
if ($skript -notmatch ('(?m)^AppVersion=' + [regex]::Escape($fassung) + '\s*$')) { $pruefBefunde.Add("AppVersion nicht $fassung") }
if ($skript -notmatch '(?m)^PrivilegesRequired=lowest\s*$') { $pruefBefunde.Add('PrivilegesRequired nicht lowest') }
if ($skript -match '(?m)^\[Registry\]') { $pruefBefunde.Add('[Registry]-Abschnitt im Setup') }
$mutexSoll = @(Select-String -LiteralPath (Join-Path $wurzel 'installer\TacRelay.iss') -Pattern '^AppMutex=(.+)$')
if ($mutexSoll.Count -ne 1) { $pruefBefunde.Add("installer\TacRelay.iss: genau ein AppMutex erwartet, gefunden $($mutexSoll.Count)") }
elseif ($skript -notmatch ('(?m)^AppMutex=' + [regex]::Escape($mutexSoll[0].Matches[0].Groups[1].Value.Trim()) + '\s*$')) {
    $pruefBefunde.Add("AppMutex im Setup nicht $($mutexSoll[0].Matches[0].Groups[1].Value.Trim())")
}
# Die Seite für eine vorhandene Installation: ihre drei Wahlen stehen unter [CustomMessages] (die baut innounp nach, die
# überschriebenen [Messages] nicht).
foreach ($wahl in 'WahlAktualisieren', 'WahlReparieren', 'WahlNeu') {
    if ($skript -notmatch "(?m)^de\.$wahl=") { $pruefBefunde.Add("[CustomMessages] de.$wahl fehlt im Setup") }
}

# Fassung der Programmdatei und des Setups, als Zahlen (der Text füllt Inno mit Leerzeichen auf).
function Fassung4([Diagnostics.FileVersionInfo]$v) { '{0}.{1}.{2}.{3}' -f $v.FileMajorPart, $v.FileMinorPart, $v.FileBuildPart, $v.FilePrivatePart }
$exeInfo = (Get-Item (Join-Path $app 'TacRelay.exe')).VersionInfo
$exeFassung = Fassung4 $exeInfo
if ($exeFassung -ne "$fassung.0" -or $exeInfo.ProductVersion.Trim() -notmatch ('^' + [regex]::Escape($fassung) + '(\+|$)')) {
    $pruefBefunde.Add("TacRelay.exe hat Fassung $exeFassung / $($exeInfo.ProductVersion), erwartet $fassung.0 / $fassung")
}
$setupInfo = (Get-Item $setupDatei).VersionInfo
if ((Fassung4 $setupInfo) -ne "$fassung.0" -or $setupInfo.ProductVersion.Trim() -ne $fassung) {
    $pruefBefunde.Add("Setup hat Fassung $(Fassung4 $setupInfo) / $($setupInfo.ProductVersion.Trim()), erwartet $fassung.0 / $fassung")
}

# Symbol: dieselben Bilder wie in src\Funk.Client\TacRelay.ico, in der Programmdatei und im Setup.
$sollBilder = [TrSymbolLeser]::AusIco($symbolDatei)
foreach ($pe in (Join-Path $app 'TacRelay.exe'), $setupDatei) {
    $bilderIst = [TrSymbolLeser]::AusProgramm($pe)
    if (($bilderIst -join '|') -ne ($sollBilder -join '|')) {
        $pruefBefunde.Add("Symbol in $(Split-Path -Leaf $pe) weicht ab: $($bilderIst.Count) Bilder, Soll $($sollBilder.Count) ($(($sollBilder | ForEach-Object { ($_ -split ' ')[0] }) -join ', '))")
    }
}
if ($pruefBefunde.Count -gt 0) { throw "Prüfung nicht bestanden:`n  $($pruefBefunde -join "`n  ")`nArbeitsordner: $arbeit" }

# ---- Ablegen ----
Schritt 'Ablegen'
$hash = (Get-FileHash $setupDatei -Algorithm SHA256).Hash.ToLowerInvariant()
Move-Item -LiteralPath $setupDatei -Destination $setupZiel
if ((Get-FileHash $setupZiel -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "SHA-256 nach dem Verschieben verändert: $setupZiel" }
[IO.File]::WriteAllText($hashZiel, "$hash  $name.exe`r`n", $utf8)
if (-not $ArbeitsordnerBehalten) { Remove-Item -Recurse -Force $arbeit }

Write-Host ''
Write-Host "Setup:     $setupZiel ($([math]::Round((Get-Item $setupZiel).Length / 1MB, 1)) MB)"
Write-Host "Art:       $(if ($Verwaltung) { 'Admin-Setup mit Verwaltungsteil (verwaltung\Funk.Verwaltung.dll)' } else { 'normales Setup, ohne Verwaltungscode (geprüft)' })"
Write-Host "Profil:    $profilHinweis"
Write-Host "SHA-256:   $hash (auch in $(Split-Path -Leaf $hashZiel))"
Write-Host "Fassung:   $fassung (Directory.Build.props), TacRelay.exe $exeFassung"
Write-Host "Quelle:    $zweig $commit$(if (-not $sauber) { ' (mit ungesicherten Änderungen)' })"
Write-Host "Inhalt:    $($liste.Count) Dateien, $([math]::Round(($liste | Measure-Object Bytes -Sum).Sum / 1MB, 1)) MB unter {app}"
Write-Host "Laufzeit:  auf dem Bau-PC $laufzeitText; im Setup $laufzeitName (signiert von Microsoft, SHA-256 $laufzeitSha), installiert nur, wenn sie fehlt"
Write-Host "Symbol:    $symbolHinweis; in TacRelay.exe und Setup je $($sollBilder.Count) Bilder"
Write-Host "Prüfung:   bestanden (innounp: Liste, Entpacken, SHA-256 je Datei, Skript, Fassung, Symbol)"
if ($ArbeitsordnerBehalten) { Write-Host "Arbeit:    $arbeit (soll.txt, innounp-liste.txt, entpackt\)" }
