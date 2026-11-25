# JG TacRelay - Maintainer: Sarevoc
# Laedt die von Sarevoc am 29.09.2026 freigegebenen Quellarchive nach C:\Claude\data\tacrelay-quellen\
# und schreibt ein Inventar mit SHA-256. Vorhandene Dateien werden nicht erneut geladen.
# Freigabeliste: opus 1.6.1, webrtc-audio-processing 2.1, abseil-cpp 20240722.0 (+ Meson-Patch aus
# dem Wrap der APM), speexdsp 1.2.1, rnnoise (main), rnnoise-Modell. Nichts anderes.
# Jeder Lauf fragt das Netz und braucht deshalb -NetzFreigegeben; die Freigabe vom 29.09.2026 galt dem einmaligen
# Laden, nicht jedem weiteren Lauf. Liegt alles schon da, fragt das Skript das Netz gar nicht und laesst INVENTAR.md.
param(
    [switch]$NetzFreigegeben
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if (-not $NetzFreigegeben) {
    Write-Host 'Laedt aus dem Netz: nur mit -NetzFreigegeben, und den gibt Sarevoc im Gespraech frei.'
    exit 5
}
$ziel = 'C:\Claude\data\tacrelay-quellen'
New-Item -ItemType Directory -Force $ziel | Out-Null
$h = @{ 'User-Agent' = 'JG-TacRelay-Build' }

$posten = @(
    @{ Datei = 'opus-1.6.1.tar.gz'; Url = 'https://downloads.xiph.org/releases/opus/opus-1.6.1.tar.gz'; Sha = '6ffcb593207be92584df15b32466ed64bbec99109f007c82205f0194572411a1' },
    @{ Datei = 'webrtc-audio-processing-2.1.tar.xz'; Url = 'https://freedesktop.org/software/pulseaudio/webrtc-audio-processing/webrtc-audio-processing-2.1.tar.xz'; Sha = $null },
    @{ Datei = 'abseil-cpp-20240722.0.tar.gz'; Url = 'https://github.com/abseil/abseil-cpp/archive/refs/tags/20240722.0.tar.gz'; Sha = $null },
    @{ Datei = 'speexdsp-1.2.1.tar.gz'; Url = 'https://downloads.xiph.org/releases/speex/speexdsp-1.2.1.tar.gz'; Sha = $null },
    @{ Datei = 'rnnoise-main.tar.gz'; Url = 'https://gitlab.xiph.org/xiph/rnnoise/-/archive/main/rnnoise-main.tar.gz'; Sha = $null }
)
# Das Modell traegt seinen SHA-256 im Namen; liegt es schon da, wird model_version nicht erneut abgefragt.
$modell = Get-ChildItem -Path $ziel -Filter 'rnnoise_data-*.tar.gz' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if ($modell) { $mv = $modell.Name -replace '^rnnoise_data-(.+)\.tar\.gz$', '$1' }
else { $mv = (Invoke-WebRequest -Uri 'https://gitlab.xiph.org/xiph/rnnoise/-/raw/main/model_version' -Headers $h).Content.Trim() }
$posten += @{ Datei = "rnnoise_data-$mv.tar.gz"; Url = "https://media.xiph.org/rnnoise/models/rnnoise_data-$mv.tar.gz"; Sha = $mv }

$geladen = 0
$inventar = foreach ($p in $posten) {
    $pfad = Join-Path $ziel $p.Datei
    if (-not (Test-Path $pfad)) {
        Invoke-WebRequest -Uri $p.Url -OutFile $pfad -Headers $h
        $geladen++
    }
    $sha = (Get-FileHash $pfad -Algorithm SHA256).Hash.ToLowerInvariant()
    $status = if ($null -eq $p.Sha) { 'kein Soll-Hash, festgehalten' } elseif ($sha -eq $p.Sha) { 'PASST' } else { 'ABWEICHUNG' }
    [pscustomobject]@{ Datei = $p.Datei; Bytes = (Get-Item $pfad).Length; Sha256 = $sha; Pruefung = $status; Quelle = $p.Url }
}
$inventar | Format-Table Datei, Bytes, Pruefung -AutoSize | Out-String -Width 200

if ($geladen -gt 0) {
    $md = @('# Inventar tacrelay-quellen', '', "Geladen am $(Get-Date -Format 'yyyy-MM-dd HH:mm') nach Freigabe durch Sarevoc (29.09.2026).", '',
        '| Datei | Bytes | SHA-256 | Prüfung | Quelle |', '| --- | --- | --- | --- | --- |')
    $md += $inventar | ForEach-Object { "| $($_.Datei) | $($_.Bytes) | ``$($_.Sha256)`` | $($_.Pruefung) | $($_.Quelle) |" }
    $md | Set-Content (Join-Path $ziel 'INVENTAR.md') -Encoding utf8
}
else {
    Write-Host 'Nichts geladen, INVENTAR.md bleibt, wie es ist.'
}
if ($inventar.Pruefung -contains 'ABWEICHUNG') { Write-Error 'Mindestens eine Prüfsumme weicht ab - nicht verwenden.' }
