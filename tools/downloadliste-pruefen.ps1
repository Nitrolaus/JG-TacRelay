# JG TacRelay - Maintainer: Sarevoc
# Ermittelt fuer die geplanten Downloads Groesse und Erreichbarkeit per HEAD-Anfrage.
# Laedt nichts herunter. NuGet-Abhaengigkeiten werden ueber die Registrierungs-API (JSON) aufgeloest.
# Besteht nur aus Netzanfragen (xiph, freedesktop, github, gitlab, nuget.org, pypi.org) und braucht deshalb
# -NetzFreigegeben, den Sarevoc fuer jeden Lauf im Gespraech freigibt.
param(
    [switch]$NetzFreigegeben
)
$ErrorActionPreference = 'Stop'
if (-not $NetzFreigegeben) {
    Write-Host 'Fragt das Netz: nur mit -NetzFreigegeben, und den gibt Sarevoc im Gespraech frei.'
    exit 5
}
$h = @{ 'User-Agent' = 'JG-TacRelay-Planung' }

function Groesse([string] $url) {
    try {
        $r = Invoke-WebRequest -Uri $url -Method Head -Headers $h -MaximumRedirection 10
        $l = $r.Headers['Content-Length']
        if ($l) { [math]::Round([double]($l | Select-Object -First 1) / 1MB, 2) } else { '?' }
    } catch { "FEHLER: $($_.Exception.Message)" }
}

$quellen = @(
    @{ Name = 'opus-1.6.1.tar.gz'; Zweck = 'Codec (libopus), SHA-256 bekannt'; Url = 'https://downloads.xiph.org/releases/opus/opus-1.6.1.tar.gz' },
    @{ Name = 'webrtc-audio-processing-2.1.tar.xz'; Zweck = 'Echounterdrueckung AEC3, AGC2, Hochpass'; Url = 'https://freedesktop.org/software/pulseaudio/webrtc-audio-processing/webrtc-audio-processing-2.1.tar.xz' },
    @{ Name = 'abseil-cpp 20240722.0'; Zweck = 'Abhaengigkeit der WebRTC APM'; Url = 'https://github.com/abseil/abseil-cpp/archive/refs/tags/20240722.0.tar.gz' },
    @{ Name = 'speexdsp-1.2.1.tar.gz'; Zweck = 'Resampler (Drift, 44,1 kHz, 16 kHz fuer Silero)'; Url = 'https://downloads.xiph.org/releases/speex/speexdsp-1.2.1.tar.gz' },
    @{ Name = 'rnnoise main (Quellarchiv)'; Zweck = 'Rauschunterdrueckung und Sprachwahrscheinlichkeit'; Url = 'https://gitlab.xiph.org/xiph/rnnoise/-/archive/main/rnnoise-main.tar.gz' },
    @{ Name = 'silero_vad.onnx'; Zweck = 'Sprach-Erkennung (VAD-Modell)'; Url = 'https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx' }
)
foreach ($q in $quellen) { [pscustomobject]@{ Datei = $q.Name; MB = (Groesse $q.Url); Zweck = $q.Zweck; Quelle = $q.Url } }

# RNNoise-Modell: Name und Hash stehen in model_version bzw. download_model.sh
try {
    $mv = (Invoke-WebRequest -Uri 'https://gitlab.xiph.org/xiph/rnnoise/-/raw/main/model_version' -Headers $h).Content.Trim()
    $url = "https://media.xiph.org/rnnoise/models/rnnoise_data-$mv.tar.gz"
    [pscustomobject]@{ Datei = "rnnoise_data-$mv.tar.gz"; MB = (Groesse $url); Zweck = 'RNNoise-Modellgewichte (werden einkompiliert)'; Quelle = $url }
} catch { [pscustomobject]@{ Datei = 'rnnoise-Modell'; MB = "FEHLER: $($_.Exception.Message)"; Zweck = ''; Quelle = '' } }

# NuGet: Paket samt Abhaengigkeiten fuer net10.0 bzw. net8.0/netstandard
$gesehen = @{}
function NuGet([string] $id, [string] $version) {
    $key = "$id/$version".ToLowerInvariant()
    if ($gesehen.ContainsKey($key)) { return }
    $gesehen[$key] = $true
    $lid = $id.ToLowerInvariant()
    $url = "https://api.nuget.org/v3-flatcontainer/$lid/$version/$lid.$version.nupkg"
    [pscustomobject]@{ Datei = "$id $version (NuGet)"; MB = (Groesse $url); Zweck = ''; Quelle = $url }
    try {
        $reg = Invoke-RestMethod -Uri "https://api.nuget.org/v3/registration5-gz-semver2/$lid/$version.json" -Headers $h
        $leaf = Invoke-RestMethod -Uri $reg.catalogEntry -Headers $h
        $gruppe = $leaf.dependencyGroups | Sort-Object { switch -Regex ($_.targetFramework) { '^net10' { 0 } '^net9' { 1 } '^net8' { 2 } '^\.?netstandard2\.1' { 3 } '^\.?netstandard2' { 4 } default { 9 } } } | Select-Object -First 1
        foreach ($d in $gruppe.dependencies) {
            $v = ($d.range -replace '[\[\(\]\) ]', '' -split ',')[0]
            NuGet $d.id $v
        }
    } catch { }
}
NuGet 'NAudio' '3.1.0'
NuGet 'Microsoft.ML.OnnxRuntime' '1.30.0'
NuGet 'CommunityToolkit.Mvvm' '8.4.2'
NuGet 'gong-wpf-dragdrop' '4.0.0'

# Meson fuer den Build der WebRTC APM (in ein venv unter tools\)
try {
    $pj = Invoke-RestMethod -Uri 'https://pypi.org/pypi/meson/json' -Headers $h
    $whl = $pj.urls | Where-Object packagetype -eq 'bdist_wheel' | Select-Object -First 1
    [pscustomobject]@{ Datei = "meson $($pj.info.version) (PyPI)"; MB = [math]::Round($whl.size / 1MB, 2); Zweck = 'Build-System der WebRTC APM, nur in venv'; Quelle = $whl.url }
} catch { }
