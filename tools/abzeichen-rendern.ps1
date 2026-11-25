# JG TacRelay - Maintainer: Sarevoc

<#
    Rechnet die Abzeichen-Vorlagen (grafik\abzeichen\*.svg) in PNG mit 128 x 128 Bildpunkten um und legt sie mit dem Katalog
    (abzeichen.json) dorthin, wo Client und Protokoll sie einbinden. Die PNG werden eingecheckt; der Bau braucht dieses Skript nicht.

    Aufruf:
        pwsh -File tools\abzeichen-rendern.ps1
        pwsh -File tools\abzeichen-rendern.ps1 -Bogen C:\Claude\tmp\abzeichen-bogen.png   # zusätzlich ein Übersichtsbogen

    Braucht ImageMagick mit librsvg (portable unter C:\Claude\Eigene_Programme\imagemagick, sonst -Magick angeben).
    Die Vorlage für neue Abzeichen steht in grafik\abzeichen\LIESMICH.md.
#>
param(
    [string]$Magick = 'C:\Claude\Eigene_Programme\imagemagick\magick.exe',
    [string]$Bogen = ''
)
$ErrorActionPreference = 'Stop'
$wurzel = Split-Path $PSScriptRoot -Parent
$quelle = Join-Path $wurzel 'grafik\abzeichen'
$ziel = Join-Path $wurzel 'src\Funk.Client\Abzeichen'
$katalogZiel = Join-Path $wurzel 'src\Funk.Protokoll\Abzeichen\abzeichen.json'
if (-not (Test-Path $Magick)) { throw "ImageMagick nicht gefunden: $Magick" }
New-Item -ItemType Directory -Force $ziel, (Split-Path $katalogZiel) | Out-Null

$katalog = Get-Content (Join-Path $quelle 'abzeichen.json') -Raw | ConvertFrom-Json
$kennungen = @($katalog.laufbahnen.raenge.kennung) + @($katalog.taetigkeiten.kennung)
foreach ($k in $kennungen) {
    $svg = Join-Path $quelle "$k.svg"
    if (-not (Test-Path $svg)) { throw "Vorlage fehlt: $svg" }
    & $Magick -background none -density 384 $svg -resize 128x128 -define png:color-type=6 (Join-Path $ziel "$k.png")
    if ($LASTEXITCODE -ne 0) { throw "Umrechnen gescheitert: $k" }
}
# Übrig gebliebene PNG ohne Katalogzeile wären tote Ressourcen.
$fremd = Get-ChildItem $ziel -Filter *.png | Where-Object { $kennungen -notcontains $_.BaseName }
if ($fremd) { throw "PNG ohne Katalogzeile: $($fremd.Name -join ', ')" }
Copy-Item (Join-Path $quelle 'abzeichen.json') $katalogZiel -Force
"{0} Abzeichen umgerechnet nach {1}" -f $kennungen.Count, $ziel

if ($Bogen) {
    # Übersicht: je Laufbahn eine Zeile, die Tätigkeiten darunter, jedes Bild in 128 und in 16 Bildpunkten.
    $zeilen = @()
    foreach ($l in $katalog.laufbahnen) { $zeilen += , @($l.raenge.kennung) }
    for ($i = 0; $i -lt $katalog.taetigkeiten.Count; $i += 11) { $zeilen += , @($katalog.taetigkeiten.kennung[$i..([Math]::Min($i + 10, $katalog.taetigkeiten.Count - 1))]) }
    $teile = foreach ($z in $zeilen) {
        $bilder = foreach ($k in $z) {
            $p = Join-Path $ziel "$k.png"
            "( $p -resize 96x96 ( $p -resize 16x16 -background none -gravity center -extent 32x32 ) -background none -gravity center -append -extent 110x140 )"
        }
        "( $($bilder -join ' ') +append )"
    }
    $befehl = "& '$Magick' -background '#1b1d22' $($teile -join ' ') -append -bordercolor '#1b1d22' -border 12 '$Bogen'"
    Invoke-Expression $befehl.Replace('( ', '`( ').Replace(' )', ' `)')
    "Bogen: $Bogen"
}
