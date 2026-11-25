# JG TacRelay - Maintainer: Sarevoc

<#
.SYNOPSIS
  Laedt opus.dll in einen .NET-Prozess (pwsh) und ruft zwei Funktionen auf.
.DESCRIPTION
  Gegenprobe zum C-Rauchtest: Laeuft die DLL in einem .NET-Prozess ohne
  VC-Redist, und stimmt die Aufrufkonvention? Wird vom Build-Skript in einem
  eigenen pwsh-Prozess gestartet, damit die DLL danach wieder frei ist.
.EXAMPLE
  pwsh -NoProfile -File C:\Claude\work\JG-TacRelay\native\eigen\opus\dotnet-rauchtest.ps1 C:\Claude\work\JG-TacRelay\native\out\win-x64\opus.dll
#>
param(
    [Parameter(Mandatory)][string]$Dll
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [IO.Path]::IsPathRooted($Dll) -or -not (Test-Path -LiteralPath $Dll)) {
    Write-Output "FAIL  DLL nicht gefunden oder Pfad nicht absolut: $Dll"
    exit 1
}

$quelle = @"
using System;
using System.Runtime.InteropServices;
public static class OpusDotnetRauchtest
{
    [DllImport(@"$Dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr opus_get_version_string();

    [DllImport(@"$Dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int opus_decoder_get_size(int channels);

    [DllImport(@"$Dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr opus_decoder_create(int fs, int channels, out int error);

    [DllImport(@"$Dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void opus_decoder_destroy(IntPtr st);
}
"@
Add-Type -TypeDefinition $quelle

$fehler = 0
$version = [Runtime.InteropServices.Marshal]::PtrToStringAnsi([OpusDotnetRauchtest]::opus_get_version_string())
if ($version -like '*1.6.1*') { Write-Output "PASS  .NET $([Environment]::Version): opus_get_version_string = $version" }
else { Write-Output "FAIL  .NET: opus_get_version_string = $version"; $fehler++ }

$groesse = [OpusDotnetRauchtest]::opus_decoder_get_size(1)
if ($groesse -gt 0) { Write-Output "PASS  .NET: opus_decoder_get_size(1) = $groesse Bytes" }
else { Write-Output "FAIL  .NET: opus_decoder_get_size(1) = $groesse"; $fehler++ }

$err = -99
$dec = [OpusDotnetRauchtest]::opus_decoder_create(48000, 1, [ref]$err)
if ($dec -ne [IntPtr]::Zero -and $err -eq 0) {
    Write-Output "PASS  .NET: opus_decoder_create(48000, 1) liefert Zeiger, Fehlercode 0"
    [OpusDotnetRauchtest]::opus_decoder_destroy($dec)
}
else { Write-Output "FAIL  .NET: opus_decoder_create Fehlercode $err"; $fehler++ }

# Welche opus.dll wirklich im Prozess steckt (die CRT-Abhaengigkeiten prueft das Build-Skript per dumpbin)
$geladen = [Diagnostics.Process]::GetCurrentProcess().Modules |
    Where-Object { $_.ModuleName -eq 'opus.dll' } |
    ForEach-Object { $_.FileName }
Write-Output ("INFO  .NET: geladen aus " + ($geladen -join ', '))

if ($fehler) { Write-Output "ERGEBNIS  FAIL ($fehler)"; exit 1 }
Write-Output 'ERGEBNIS  PASS'
exit 0
