# JG TacRelay - Maintainer: Sarevoc
#requires -Version 7.0
<#
    Rechnet das Programmsymbol aus den SVG-Vorlagen in grafik\symbol\ und schreibt es als .ico mit 16, 20, 24, 32, 40, 48,
    64, 128 und 256 Pixeln. Eigenbau (Entwurf 3 vom 19.10.2026), keine fremde Vorlage, kein Gildenbezug: ein Kopfhörer mit
    Mikrofon in einem Sechseck, Petrol auf Anthrazit.

    Vorlagen (je viewBox 0 0 64 64, Breite und Höhe setzt dieses Skript je Größe):
        grafik\symbol\symbol.svg        ab 32 Pixeln: Sechseck #1F2326 mit Rand in Petrol #2F8A8C, Kopfbügel weiß #E9EDF0,
                                        Hörmuscheln in Petrol hell #4FB3B4, Mikrofonarm mit Kapsel
        grafik\symbol\symbol-klein.svg  20 und 24 Pixel: ohne Mikrofonarm, Rand, Bügel und Hörmuscheln kräftiger
        grafik\symbol\symbol-16.svg     16 Pixel: ohne Mikrofonarm, Seiten, Bügel und Hörmuscheln auf ganzen Bildpunkten

    Die Vorlagen und dieses Skript sind die Quelle des Symbols; src\Funk.Client\TacRelay.ico ist sein Ergebnis und liegt
    daneben im Repository, damit der Bau nichts zeichnen muss. Wer das Symbol ändert, ändert die Vorlagen und schreibt die
    Datei neu (aus dem Ordner des Repositorys):

        pwsh -NoProfile -File tools\symbol-zeichnen.ps1
        pwsh -NoProfile -File tools\symbol-zeichnen.ps1 -Vorschau C:\Claude\tmp\symbol-bogen.png

    Gerechnet wird mit ImageMagick und librsvg (portable unter C:\Claude\Eigene_Programme\imagemagick, sonst -Magick), jede
    Größe eigens aus ihrer Vorlage, nicht aus 256 verkleinert. Die Bilder bis 64 Pixel stehen als 32-Bit-Bitmap in der
    Datei, 128 und 256 als PNG, wie es Windows seit Vista erwartet; beide schreibt GDI+ (System.Drawing aus .NET) aus den
    gerechneten Bildpunkten, damit dieselbe Vorlage immer dieselbe Datei ergibt. Das Skript liest nichts aus dem Netz und
    legt keine Zwischendateien an; ImageMagick bekommt die Vorlage über die Standardeingabe.
#>
param(
    [string]$Ziel = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Funk.Client\TacRelay.ico'),
    [string]$Vorschau,
    # Bilder für den Setup-Assistenten (tools\setup-bauen.ps1): klein-<n>.png oben rechts, gross-<n>.png links auf der
    # Abschlussseite, je in den Größen, die Inno Setup 6.7 bei 100 bis 250 % Skalierung anfordert. Ohne Text: Eine
    # Schrift in ein Bild zu rastern hieße, sie einzubetten.
    [string]$AssistentOrdner,
    [string]$Magick = 'C:\Claude\Eigene_Programme\imagemagick\magick.exe'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
if (-not (Test-Path -LiteralPath $Magick)) { throw "ImageMagick nicht gefunden: $Magick" }

$vorlagen = Join-Path (Split-Path -Parent $PSScriptRoot) 'grafik\symbol'
$groessen = 16, 20, 24, 32, 40, 48, 64, 128, 256
$anthrazit = [Drawing.Color]::FromArgb(255, 0x25, 0x25, 0x25)

# Die Vorlage je Größe: 16 auf ganzen Bildpunkten, 20 und 24 vereinfacht, ab 32 vollständig.
function Vorlage([int]$s) {
    $name = if ($s -le 16) { 'symbol-16.svg' } elseif ($s -le 24) { 'symbol-klein.svg' } else { 'symbol.svg' }
    $pfad = Join-Path $vorlagen $name
    if (-not (Test-Path -LiteralPath $pfad)) { throw "Vorlage fehlt: $pfad" }
    return [IO.File]::ReadAllText($pfad, [Text.UTF8Encoding]::new($false))
}

# Rechnet eine Vorlage in $s x $s Bildpunkten: Breite und Höhe der Vorlage auf $s, librsvg über die Standardeingabe, PNG mit
# Alpha über die Standardausgabe, ohne Zeitstempel. Ergebnis ist eine Bitmap mit festen 96 dpi: Sonst übernähme sie die
# Skalierung des Bildschirms (gemessen: 168 dpi bei 175 %), die im pHYs-Block der PNG-Bilder landete.
function Zeichnen([int]$s) {
    $svg = Vorlage $s
    $muster = [regex]'<svg ([^>]*?)width="64" height="64"'
    if ($muster.Matches($svg).Count -ne 1) { throw 'Vorlage ohne width="64" height="64" im svg-Element' }
    $svg = $muster.Replace($svg, ('<svg $1width="{0}" height="{0}"' -f $s), 1)
    $start = [Diagnostics.ProcessStartInfo]::new($Magick)
    foreach ($a in '-background', 'none', 'RSVG:-', '-define', 'png:exclude-chunks=date,time', 'PNG32:-') { $start.ArgumentList.Add($a) }
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.UseShellExecute = $false
    $p = [Diagnostics.Process]::Start($start)
    $fehler = $p.StandardError.ReadToEndAsync()
    $eingabe = [Text.UTF8Encoding]::new($false).GetBytes($svg)
    $p.StandardInput.BaseStream.Write($eingabe, 0, $eingabe.Length)
    $p.StandardInput.Close()
    $ms = [IO.MemoryStream]::new()
    $p.StandardOutput.BaseStream.CopyTo($ms)
    $p.WaitForExit()
    if ($p.ExitCode -ne 0 -or $ms.Length -eq 0) { throw "ImageMagick gescheitert ($s px): $($fehler.Result)" }
    $ms.Position = 0
    $gelesen = [Drawing.Bitmap]::new($ms)
    try {
        if ($gelesen.Width -ne $s -or $gelesen.Height -ne $s) { throw "ImageMagick lieferte $($gelesen.Width) x $($gelesen.Height) statt $s x $s" }
        # Bildpunkte unverändert in eine eigene Bitmap (32 Bit, nicht vormultipliziert); die gelesene hängt am Strom.
        $bild = [Drawing.Bitmap]::new($s, $s, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bild.SetResolution(96, 96)
        $rechteck = [Drawing.Rectangle]::new(0, 0, $s, $s)
        $quelle = $gelesen.LockBits($rechteck, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $ziel = $bild.LockBits($rechteck, [Drawing.Imaging.ImageLockMode]::WriteOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $zeile = [byte[]]::new($s * 4)
            for ($y = 0; $y -lt $s; $y++) {
                [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($quelle.Scan0, $y * $quelle.Stride), $zeile, 0, $zeile.Length)
                [Runtime.InteropServices.Marshal]::Copy($zeile, 0, [IntPtr]::Add($ziel.Scan0, $y * $ziel.Stride), $zeile.Length)
            }
        }
        finally {
            $gelesen.UnlockBits($quelle)
            $bild.UnlockBits($ziel)
        }
    }
    finally { $gelesen.Dispose(); $ms.Dispose() }
    return $bild
}

# Ein Bild als 32-Bit-DIB, wie es in einer .ico steht: BITMAPINFOHEADER mit doppelter Höhe, Zeilen von unten nach oben
# in BGRA (nicht vormultipliziert), danach die UND-Maske (1 Bit je Pixel, Zeilen auf 4 Byte aufgefüllt, 1 = durchsichtig).
function AlsDib([Drawing.Bitmap]$bild) {
    $s = $bild.Width
    $daten = $bild.LockBits([Drawing.Rectangle]::new(0, 0, $s, $s), [Drawing.Imaging.ImageLockMode]::ReadOnly,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $zeile = $s * 4
        $roh = [byte[]]::new($zeile * $s)
        for ($y = 0; $y -lt $s; $y++) {
            [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($daten.Scan0, $y * $daten.Stride), $roh, $y * $zeile, $zeile)
        }
    }
    finally { $bild.UnlockBits($daten) }
    $maskeZeile = [int]([Math]::Ceiling($s / 32.0) * 4)
    $ms = [IO.MemoryStream]::new()
    $bw = [IO.BinaryWriter]::new($ms)
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int](2 * $s)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]($zeile * $s + $maskeZeile * $s)); $bw.Write([int]0); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($roh, $y * $zeile, $zeile) }
    for ($y = $s - 1; $y -ge 0; $y--) {
        $maske = [byte[]]::new($maskeZeile)
        for ($x = 0; $x -lt $s; $x++) {
            if ($roh[$y * $zeile + $x * 4 + 3] -eq 0) { $maske[[int][Math]::Floor($x / 8)] = $maske[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $bw.Write($maske)
    }
    $bw.Flush()
    return , $ms.ToArray()
}

function AlsPng([Drawing.Bitmap]$bild) {
    $ms = [IO.MemoryStream]::new()
    $bild.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$bilder = [ordered]@{}
foreach ($s in $groessen) { $bilder[[string]$s] = Zeichnen $s }

# ICONDIR, je Bild ein ICONDIRENTRY (16 Byte), dann die Bilddaten.
$teile = [Collections.Generic.List[byte[]]]::new()
foreach ($s in $groessen) {
    $b = $bilder[[string]$s]
    [byte[]]$daten = if ($s -ge 128) { AlsPng $b } else { AlsDib $b }
    $teile.Add($daten)
}
$ms = [IO.MemoryStream]::new()
$bw = [IO.BinaryWriter]::new($ms)
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$groessen.Count)
$versatz = 6 + 16 * $groessen.Count
for ($i = 0; $i -lt $groessen.Count; $i++) {
    $s = $groessen[$i]
    $kante = [byte]($(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write($kante); $bw.Write($kante); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$teile[$i].Length); $bw.Write([int]$versatz)
    $versatz += $teile[$i].Length
}
foreach ($t in $teile) { $bw.Write($t) }
$bw.Flush()
New-Item -ItemType Directory -Force (Split-Path -Parent $Ziel) | Out-Null
[IO.File]::WriteAllBytes($Ziel, $ms.ToArray())
Write-Host "Symbol geschrieben: $Ziel ($($ms.Length) Byte, $($groessen -join ', ') px)"

if ($AssistentOrdner) {
    New-Item -ItemType Directory -Force $AssistentOrdner | Out-Null
    foreach ($s in 58, 77, 97, 116, 124, 143, 159) {
        $b = Zeichnen $s
        $b.Save((Join-Path $AssistentOrdner "klein-$s.png"), [Drawing.Imaging.ImageFormat]::Png)
        $b.Dispose()
    }
    # Seitenverhältnis 164:314; Anthrazit, das Symbol oben im Drittel.
    foreach ($paar in @(@(202, 386), @(269, 515), @(336, 643), @(403, 772), @(430, 824), @(498, 953), @(534, 1022))) {
        $w, $h = $paar
        $bild = [Drawing.Bitmap]::new($w, $h, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bild.SetResolution(96, 96)
        $g = [Drawing.Graphics]::FromImage($bild)
        try {
            $g.Clear($anthrazit)
            $s = [int][Math]::Round(0.56 * $w)
            $symbol = Zeichnen $s
            $g.DrawImage($symbol, [int](($w - $s) / 2), [int][Math]::Round(0.30 * $h - $s / 2), $s, $s)
            $symbol.Dispose()
        }
        finally { $g.Dispose() }
        $bild.Save((Join-Path $AssistentOrdner "gross-$w.png"), [Drawing.Imaging.ImageFormat]::Png)
        $bild.Dispose()
    }
    Write-Host "Assistentenbilder: $AssistentOrdner"
}

if ($Vorschau) {
    # Alle Größen in echter Größe und vergrößert (bis 64 px achtfach, Pixel sichtbar), auf hellem und auf dunklem Grund.
    $faktor = { param($s) [Math]::Max(1, [Math]::Min(8, [int][Math]::Floor(512 / $s))) }
    $breite = 20 + ($groessen | ForEach-Object { $_ * (& $faktor $_) + 20 } | Measure-Object -Sum).Sum
    $hoehe = 2 * (256 + 512 + 60)
    $blatt = [Drawing.Bitmap]::new([int]$breite, [int]$hoehe)
    $g = [Drawing.Graphics]::FromImage($blatt)
    try {
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
        $haelfte = $hoehe / 2
        $g.FillRectangle([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 0xF3, 0xF3, 0xF3)), 0, 0, $breite, $haelfte)
        $g.FillRectangle([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 0x1C, 0x1C, 0x1C)), 0, $haelfte, $breite, $haelfte)
        foreach ($oben in 0, $haelfte) {
            $x = 20
            foreach ($s in $groessen) {
                $b = $bilder[[string]$s]
                $g.DrawImage($b, [int]$x, [int]($oben + 20), $s, $s)
                $f = & $faktor $s
                $g.DrawImage($b, [Drawing.Rectangle]::new([int]$x, [int]($oben + 296), $s * $f, $s * $f))
                $x += $s * $f + 20
            }
        }
    }
    finally { $g.Dispose() }
    New-Item -ItemType Directory -Force (Split-Path -Parent $Vorschau) | Out-Null
    $blatt.Save($Vorschau, [Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Vorschau: $Vorschau"
}
