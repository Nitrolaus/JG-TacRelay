# JG TacRelay - Maintainer: Sarevoc

<#
    Umgebung für jeden dotnet-Aufruf dieses Repositorys (Bauen, Testen, Paket). Bindet zuerst
    C:\Claude\tools\dev-umgebung.ps1 ein (NUGET_PACKAGES in der Zone, Telemetrie aus) und hält dann auch das in
    C:\Claude, was SDK, MSBuild, NuGet und der Compiler sonst im Benutzerprofil ablegen oder am Profil ändern:

    - TEMP und TMP: MSBuildTemp (Antwortdateien), NuGetScratch (Sperrdateien), Schattenkopien der Analyzer
    - NUGET_SCRATCH, NUGET_HTTP_CACHE_PATH, NUGET_PLUGINS_CACHE_PATH: sonst unter %TEMP% bzw. %LOCALAPPDATA%\NuGet
    - DOTNET_CLI_HOME: Erstlauf-Marken der CLI. Nur zusammen mit den beiden Schaltern darunter: Ein neuer CLI-Ordner hat
      noch keine Marken, und ohne die Schalter löste der erste Aufruf dort Zertifikat und PATH-Eintrag erst aus.
    - DOTNET_GENERATE_ASPNET_CERTIFICATE=false: kein Entwicklerzertifikat im Zertifikatsspeicher des Benutzers
    - DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false: kein dauerhafter PATH-Eintrag unter HKCU\Environment
    - DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1: keine Nachfrage nach Workload-Updates im Netz
    - MSBUILDDISABLENODEREUSE=1: keine MSBuild-Knoten, die nach dem Lauf stehen bleiben (dazu --disable-build-servers)

    Einbinden mit vorangestelltem Punkt, im selben Befehl wie dotnet und vor Set-StrictMode (dev-umgebung.ps1 fragt
    Variablen ab, die nicht immer gesetzt sind):

        . C:\Claude\work\JG-TacRelay\tools\dotnet-umgebung.ps1; dotnet build C:\Claude\work\JG-TacRelay\TacRelay.slnx --disable-build-servers

    Tests: tests\testlauf.ps1, Paket: tools\paket-bauen.ps1; beide binden es selbst ein.
#>
. C:\Claude\tools\dev-umgebung.ps1 *> $null

$trDotnetTemp = 'C:\Claude\tmp\dotnet-temp'
$trDotnetHome = 'C:\Claude\tools\dotnet-home'
foreach ($trOrdner in $trDotnetTemp, $trDotnetHome) {
    if (-not (Test-Path -LiteralPath $trOrdner)) { New-Item -ItemType Directory -Force -Path $trOrdner | Out-Null }
}

$env:TEMP = $trDotnetTemp
$env:TMP = $trDotnetTemp
$env:NUGET_SCRATCH = Join-Path $trDotnetTemp 'NuGetScratch'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $trDotnetTemp 'nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $trDotnetTemp 'nuget-plugins'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_HOME = $trDotnetHome
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
