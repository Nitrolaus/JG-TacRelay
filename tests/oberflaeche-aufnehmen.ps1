# JG TacRelay - Maintainer: Sarevoc
#requires -Version 7.0
<#
    Startet den Client, nimmt jedes sichtbare Fenster des Prozesses einzeln per PrintWindow (PW_RENDERFULLCONTENT) auf
    und schließt das Programm wieder über WM_CLOSE. Keine Bildschirmkopie, kein SetForegroundWindow: PrintWindow fragt
    das Fenster selbst nach seinem Inhalt, auch wenn es verdeckt ist. Liefert es nichts, gibt es kein Bild.

    Das Programm zeigt dabei echte Fenster: Das Hauptfenster wird beim Start aktiviert (Fokuswechsel), das Funkfeld
    steht über allen anderen. Deshalb startet nichts ohne -SichtbarFreigegeben (Exit 5), und diesen Schalter setzt nur,
    wem Sarevoc das im Gespräch freigegeben hat. Bilder ohne Programmstart: FUNK_BILDER=<Ordner unter C:\Claude> und der Test
    OberflaecheFensterTests (zeichnet ohne Fensterhandle).

    Nur mit --vorfuehrung (Attrappen: kein Netz, kein Audiogerät, keine globalen Tasten, keine Joysticks), sonst Exit 2.
    Der echte Client nur mit FUNK_RAUCHTEST=1 und --stumm, --testzugang nur auf 127.0.0.1; die Einstellungsdatei
    bekommt dann beimStartVerbinden=false und keine Serveradresse.

    Läuft Star Citizen oder eine andere Vollbildanwendung, startet das Skript nichts (Exit 4). Startet eine während der
    Wartezeit, schließt es das Programm sofort (Exit 4). Was auch passiert (Fehler, Strg+C), das Programm wird am Ende
    geschlossen.

        pwsh -File tests\oberflaeche-aufnehmen.ps1 -Exe <TacRelay.exe> -Ordner <docs\bilder> -Praefix haupt `
             -Einstellungen <pfad.json> -Argumente --vorfuehrung,--bereich,audio [-Warten 5] -SichtbarFreigegeben
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Ordner,
    [Parameter(Mandatory)][string]$Praefix,
    [Parameter(Mandatory)][string]$Einstellungen,
    [string[]]$Argumente = @(),
    [double]$Warten = 5,
    # Nur diese Fenster aufnehmen (Titel klein und ohne Sonderzeichen: tacrelay, funkfeld, schwelle); leer = alle.
    [string[]]$Nur = @(),
    # Sichtbare Fenster, Fokuswechsel, Funkfeld immer vorn: nur nach Sarevocs Freigabe im Gespräch.
    [switch]$SichtbarFreigegeben
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $SichtbarFreigegeben) {
    Write-Host 'Das Programm zeigt echte Fenster (Hauptfenster aktiviert, Funkfeld immer vorn). Nur mit -SichtbarFreigegeben,'
    Write-Host 'und den gibt Sarevoc im Gespräch frei. Bilder ohne Programmstart: FUNK_BILDER und OberflaecheFensterTests.'
    exit 5
}

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class TrFenster {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr h, int index);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static List<IntPtr> Fenster(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static string Titel(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
}
'@

# Nur lesend, wie tests\Funk.Tests\Spielschutz.cs: Prozessliste und Benachrichtigungszustand von Windows
# (2 Vollbild, 3 Direct3D-Vollbild, 4 Präsentationsmodus). Randlose Fenstermodi erkennt nur die Prozessliste.
function Spielgrund {
    $spiel = Get-Process -Name 'StarCitizen*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($spiel) { return "$($spiel.ProcessName) läuft" }
    $zustand = 0
    if ([TrFenster]::SHQueryUserNotificationState([ref]$zustand) -eq 0 -and $zustand -in 2, 3, 4) {
        return "eine Vollbildanwendung läuft (Benachrichtigungszustand $zustand)"
    }
    return $null
}

if ($grund = Spielgrund) {
    Write-Host "$grund - es wird nichts gestartet."
    exit 4
}

# Mit "pwsh -File" kommt eine Kommaliste als ein einziger Text an; deshalb hier selbst aufteilen.
$Argumente = @($Argumente | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
$Nur = @($Nur | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })

if ('--vorfuehrung' -notin $Argumente) {
    # Der echte Client öffnet Mikrofon, Loopback und Wiedergabe und meldet globale Tasten und Joysticks an.
    if ($env:FUNK_RAUCHTEST -ne '1' -or '--stumm' -notin $Argumente) {
        Write-Host 'Nur mit --vorfuehrung (Attrappen: kein Audiogerät, keine globalen Tasten, keine Joysticks).'
        Write-Host 'Den echten Client nur mit FUNK_RAUCHTEST=1 und --stumm.'
        exit 2
    }
    $i = [Array]::IndexOf($Argumente, '--testzugang')
    if ($i -ge 0 -and ($i + 1 -ge $Argumente.Count -or $Argumente[$i + 1] -notmatch '^127\.0\.0\.1:\d+$')) {
        Write-Host '--testzugang nur auf 127.0.0.1:Port.'
        exit 2
    }
    # Kein Verbinden mit einem gespeicherten Server: höchstens über den Testzugang auf Loopback.
    $daten = if (Test-Path -LiteralPath $Einstellungen) { Get-Content -LiteralPath $Einstellungen -Raw | ConvertFrom-Json -AsHashtable } else { $null }
    if ($daten -isnot [Collections.IDictionary]) { $daten = [ordered]@{} }
    if (-not $daten.Contains('verbindung') -or $daten['verbindung'] -isnot [Collections.IDictionary]) { $daten['verbindung'] = [ordered]@{} }
    $daten['verbindung']['beimStartVerbinden'] = $false
    $daten['verbindung'].Remove('adresse')
    Set-Content -LiteralPath $Einstellungen -Encoding utf8 -Value ($daten | ConvertTo-Json -Depth 16)
}

# PerMonitorV2, damit Fensterrechteck und Aufnahme in physischen Pixeln übereinstimmen.
[TrFenster]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null

New-Item -ItemType Directory -Force -Path $Ordner | Out-Null
$env:TACRELAY_EINSTELLUNGEN = $Einstellungen
$p = $null
$haupt = [IntPtr]::Zero
$code = 0
try {
    $p = if ($Argumente.Count -gt 0) {
        Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -ArgumentList $Argumente -PassThru
    } else {
        Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
    }
    # Warten und dabei alle 250 ms nachsehen, ob inzwischen ein Spiel läuft; dann sofort schließen.
    $bis = [DateTime]::UtcNow.AddSeconds($Warten)
    while ([DateTime]::UtcNow -lt $bis) {
        if ($grund = Spielgrund) { Write-Host "$grund - das Programm wird sofort geschlossen."; $code = 4; break }
        Start-Sleep -Milliseconds 250
    }
    if ($code -eq 0 -and $p.HasExited) { Write-Host "Programm schon beendet, Exit-Code $($p.ExitCode)"; $code = 3 }

    if ($code -eq 0) {
        foreach ($h in [TrFenster]::Fenster([uint32]$p.Id)) {
            $titel = [TrFenster]::Titel($h)
            if ([string]::IsNullOrWhiteSpace($titel)) { continue }
            $r = New-Object TrFenster+RECT
            [TrFenster]::GetWindowRect($h, [ref]$r) | Out-Null
            $w = $r.R - $r.L; $hh = $r.B - $r.T
            if ($w -lt 50 -or $hh -lt 50) { continue }
            if ($titel -eq 'JG TacRelay') { $haupt = $h }
            $kennung = ($titel -replace '[^A-Za-z0-9]', '').ToLowerInvariant()
            if ($Nur.Count -gt 0 -and $kennung -notin $Nur) { continue }
            # Fensterstile nur lesen: WS_MINIMIZEBOX (0x20000) und WS_EX_TOPMOST (0x8).
            $stil = [TrFenster]::GetWindowLongW($h, -16); $exStil = [TrFenster]::GetWindowLongW($h, -20)
            Write-Host ("  {0}: minimierbar={1}, immer vorn={2}, Platz {3},{4} Größe {5}x{6} (physische Pixel)" -f $titel, (($stil -band 0x20000) -ne 0), (($exStil -band 0x8) -ne 0), $r.L, $r.T, $w, $hh)
            $bmp = New-Object System.Drawing.Bitmap $w, $hh
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $hdc = $g.GetHdc()
                $ok = [TrFenster]::PrintWindow($h, $hdc, 2)   # PW_RENDERFULLCONTENT
                $g.ReleaseHdc($hdc)
                if ($ok) {
                    $ziel = Join-Path $Ordner "$Praefix-$kennung.png"
                    $bmp.Save($ziel, [System.Drawing.Imaging.ImageFormat]::Png)
                    Write-Host "Aufgenommen: $ziel (${w}x${hh})"
                } else {
                    Write-Host "PrintWindow lieferte nichts für '$titel' - kein Bild."
                }
            }
            finally {
                $g.Dispose(); $bmp.Dispose()
            }
        }
    }
}
finally {
    # Auch nach einem Fehler oder Strg+C: Das Programm bleibt nicht mit seinen Fenstern und Geräten stehen.
    if ($p -and -not $p.HasExited) {
        if ($haupt -eq [IntPtr]::Zero) {
            $gefunden = @([TrFenster]::Fenster([uint32]$p.Id) | Where-Object { [TrFenster]::Titel($_) -eq 'JG TacRelay' })
            if ($gefunden.Count -gt 0) { $haupt = $gefunden[0] }
        }
        if ($haupt -ne [IntPtr]::Zero) { [TrFenster]::SendMessageW($haupt, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }   # WM_CLOSE
        if (-not $p.WaitForExit(10000)) {
            Write-Host 'Programm schloss nicht in 10 s, wird beendet.'
            $p.Kill()
            if ($code -eq 0) { $code = 3 }
        } elseif ($code -eq 0) {
            Write-Host "Sauber beendet, Exit-Code $($p.ExitCode)"
        }
    }
}
exit $code
