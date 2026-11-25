# JG TacRelay - Maintainer: Sarevoc
# Erzeugt den Dateiteil von CODEMAP.md mechanisch: jede .cs-Datei unter src\ und tests\ mit Zeilenzahl
# und der ersten <summary>-Zeile ihres ersten Typs. Der Rest der Karte (Weg der Daten, Wo man was
# aendert, Fallen) bleibt Handarbeit und steht zwischen den Markierungen unveraendert.
# -Pruefen: schreibt nichts, meldet nur Abweichungen (Exit 1), fuer Build und Commit.
param([switch] $Pruefen)
$ErrorActionPreference = 'Stop'
$r = Split-Path $PSScriptRoot -Parent
$karte = Join-Path $r 'CODEMAP.md'
$beginn = '<!-- DATEIEN-BEGINN (tools\codemap-erzeugen.ps1) -->'
$ende = '<!-- DATEIEN-ENDE -->'

$zeilen = foreach ($d in Get-ChildItem (Join-Path $r 'src'), (Join-Path $r 'tests') -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | Sort-Object FullName) {
    $rel = $d.FullName.Substring($r.Length + 1).Replace('\', '/')
    $inhalt = @(Get-Content $d.FullName -Encoding utf8)
    $zweck = ''
    for ($i = 0; $i -lt $inhalt.Count; $i++) {
        if ($inhalt[$i] -match '<summary>(.*)$') {
            $t = $Matches[1] -replace '</summary>.*$', ''
            if (-not $t.Trim() -and $i + 1 -lt $inhalt.Count) { $t = $inhalt[$i + 1] -replace '^\s*///\s*', '' }
            $zweck = ($t -replace '<[^>]+>', '' -replace '\|', '/').Trim()
            if ($zweck.Length -gt 140) { $zweck = $zweck.Substring(0, 137) + '...' }
            break
        }
    }
    "| ``$rel`` | $($inhalt.Count) | $zweck |"
}
$block = @($beginn, '', '| Datei | Zeilen | Aufgabe (erste Beschreibung im Code) |', '| --- | --- | --- |') + $zeilen + @('', $ende)

$alt = if (Test-Path $karte) { Get-Content $karte -Raw -Encoding utf8 } else { '' }
if ($alt -match [regex]::Escape($beginn) + '(?s).*?' + [regex]::Escape($ende)) {
    $neu = [regex]::Replace($alt, [regex]::Escape($beginn) + '(?s).*?' + [regex]::Escape($ende), ($block -join "`r`n").Replace('$', '$$'))
} else {
    $neu = $alt.TrimEnd() + "`r`n`r`n## Dateien`r`n`r`n" + ($block -join "`r`n") + "`r`n"
}
if ($Pruefen) {
    if ($neu -ne $alt) { Write-Host 'CODEMAP.md ist veraltet: tools\codemap-erzeugen.ps1 ausfuehren.'; exit 1 }
    Write-Host "CODEMAP.md aktuell ($(@($zeilen).Count) Dateien)."; exit 0
}
[IO.File]::WriteAllText($karte, $neu, [Text.UTF8Encoding]::new($false))
Write-Host "CODEMAP.md geschrieben: $(@($zeilen).Count) Dateien."
