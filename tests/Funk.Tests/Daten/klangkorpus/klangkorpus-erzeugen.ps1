# JG TacRelay - Maintainer: Sarevoc

<#
    Erzeugt den Klangkorpus der Klangstufe A (Pumpen messen): klangkorpus.wav und klangkorpus-marken.csv in diesem Ordner.

    Herkunft: Alle Sätze sind eigener Text. Gesprochen von der Windows-Sprachausgabe auf diesem PC (OneCore-Stimmen
    Microsoft Stefan, Katja und Hedda über Windows.Media.SpeechSynthesis), nur in den Speicher synthetisiert, nie auf ein
    Gerät ausgegeben. Keine fremden Aufnahmen. Das Ergebnis ist Testmaterial und gehört nicht ins Paket. Der Plan mit den
    Texten steht in klangkorpus-plan.csv, im Code stehen nur ASCII-Zeichen: Windows PowerShell liest ein Skript ohne BOM als
    ANSI, und aus einem UTF-8-Umlaut würde dort unter anderem ein Anführungszeichen.

    Aufbau: drei Blöcke (Stefan, Katja, Hedda) mit je sieben bis acht Äußerungen. Jede Äußerung bekommt einen Pegel und eine
    Hülle, damit leise und laute Stellen, Lautstärkeänderungen innerhalb eines Satzes, ein hervorgehobenes Wort und verklingende
    Satzenden vorkommen; dazwischen Pausen von 150 ms bis 3 s (mehrere um die 400 bis 600 ms der Halteregel). Pegel sind
    Effektivwerte (dBFS) der Äußerung ohne Hülle, gemessen ohne die Stille am Rand (Rahmen von 10 ms unter -60 dBFS).
      normal        konstant
      leise         konstant, leiser Zielpegel
      laut          konstant, lauter Zielpegel
      anschwellend  in dB linear von -9 auf +9 dB über die Äußerung
      abfallend     bis 55 % konstant, dann in dB linear auf -16 dB am Ende (verklingendes Satzende)
      wort          konstant, ein Wort (Spalte betont) 10 dB lauter, mit 20 ms Rampen

    Ausgabe: WAV 16 kHz, 16 Bit, mono, so wie die Stimmen rechnen (der Test wandelt mit dem speexdsp-Resampler auf 48 kHz).
    Die CSV hat zwei Zeilenarten (Positionen in Samples bei 16 kHz, ab Dateianfang):
      aeusserung;nr;stimme;huelle;zielDb;start;ende;text
      wort;nr;start;text
    Die Wortanfänge sind die Wortmarken der Sprachausgabe.

    Klangstufe C2a: Mit -Name klangkorpus-kurz entsteht derselbe Aufbau aus klangkorpus-kurz-plan.csv: kurze Funksprüche ("Hit", "Check",
    "Kontakt", "Go", ...) unter 0,5 s, normal und gerufen, als eigene Äußerungen (Wortanfang = Äußerungsanfang, Ende = Wortende).

    Aufruf nur in Windows PowerShell 5.1 (die WinRT-Projektion gibt es in PowerShell 7 nicht):
      C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File <dieses Skript>
    Bricht ab, wenn eine Stimme fehlt oder eine Spitze über -1 dBFS läge. Eine neue Fassung der Stimmen kann andere Samples
    ergeben; maßgeblich ist die eingecheckte WAV-Datei.
#>
param(
    # Name der Ausgabe ohne Endung: klangkorpus (Klangstufe A) oder klangkorpus-kurz (Klangstufe C2a, kurze Funksprüche). Plan, WAV und Marken
    # heißen danach: <Name>-plan.csv, <Name>.wav, <Name>-marken.csv.
    [string]$Name = 'klangkorpus'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Nur in Windows PowerShell 5.1 (WinRT-Sprachausgabe).' }

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType = WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
    Select-Object -First 1
function Warten($op, [type]$typ) {
    $t = $asTask.MakeGenericMethod($typ).Invoke($null, @($op))
    $t.Wait(-1) | Out-Null
    $t.Result
}

$Rate = 16000
$ordner = $PSScriptRoot
$stimmen = @{}
foreach ($v in [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices) { $stimmen[$v.DisplayName] = $v }

# Plan aus klangkorpus-plan.csv (UTF-8): Stimme, Hülle, Zielpegel (dBFS), Pause danach (ms), Text; bei "wort" ist das betonte
# Wort mit * markiert. Eine eigene Datei, weil Windows PowerShell ein Skript ohne BOM als ANSI liest (Umlaute im Text).
$plan = @(Get-Content -LiteralPath (Join-Path $ordner ($Name + '-plan.csv')) -Encoding UTF8 | Select-Object -Skip 1 |
    Where-Object { $_.Trim() -ne '' } | ForEach-Object { $f = $_.Split(';'); , @($f[0], $f[1], [int]$f[2], [int]$f[3], $f[4]) })

function Synthese([string]$stimme, [string]$text) {
    if (-not $stimmen.ContainsKey("Microsoft $stimme")) { throw "Stimme Microsoft $stimme fehlt." }
    $s = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
    $s.Voice = $stimmen["Microsoft $stimme"]
    $s.Options.IncludeWordBoundaryMetadata = $true
    $s.Options.AppendedSilence = [Windows.Media.SpeechSynthesis.SpeechAppendedSilence]::Min
    $strom = Warten ($s.SynthesizeTextToStreamAsync($text)) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
    $groesse = [uint32]$strom.Size
    $leser = New-Object Windows.Storage.Streams.DataReader($strom.GetInputStreamAt(0))
    $null = Warten ($leser.LoadAsync($groesse)) ([uint32])
    $bytes = New-Object byte[] $groesse
    $leser.ReadBytes($bytes)
    if ([BitConverter]::ToInt32($bytes, 24) -ne $Rate -or [BitConverter]::ToInt16($bytes, 22) -ne 1 -or [BitConverter]::ToInt16($bytes, 34) -ne 16) {
        throw "Unerwartetes Format der Sprachausgabe."
    }
    # Datenblock suchen (RIFF kann weitere Blöcke enthalten).
    $pos = 12
    while ([Text.Encoding]::ASCII.GetString($bytes, $pos, 4) -ne 'data') { $pos += 8 + [BitConverter]::ToInt32($bytes, $pos + 4) }
    $n = [BitConverter]::ToInt32($bytes, $pos + 4) / 2
    $werte = New-Object double[] $n
    for ($i = 0; $i -lt $n; $i++) { $werte[$i] = [BitConverter]::ToInt16($bytes, $pos + 8 + 2 * $i) / 32768.0 }
    $woerter = @()
    foreach ($spur in $strom.TimedMetadataTracks) {
        foreach ($c in $spur.Cues) { $woerter += , @([int][Math]::Round($c.StartTime.TotalMilliseconds * $Rate / 1000), $c.Text) }
    }
    return @(, $werte), @(, $woerter)
}

# Rand ohne Stimme abschneiden: 10-ms-Rahmen unter -60 dBFS am Anfang und Ende.
function Rand([double[]]$x) {
    $r = $Rate / 100
    $grenze = [Math]::Pow(10, -60 / 10)
    $rahmen = [int][Math]::Floor($x.Length / $r)
    $a = 0
    $e = $rahmen - 1
    while ($a -lt $rahmen) { $s = 0.0; for ($i = 0; $i -lt $r; $i++) { $s += $x[$a * $r + $i] * $x[$a * $r + $i] }; if ($s / $r -gt $grenze) { break }; $a++ }
    while ($e -gt $a) { $s = 0.0; for ($i = 0; $i -lt $r; $i++) { $s += $x[$e * $r + $i] * $x[$e * $r + $i] }; if ($s / $r -gt $grenze) { break }; $e-- }
    return ($a * $r), (($e + 1) * $r)
}

$ausgabe = New-Object System.Collections.Generic.List[double]
$zeilen = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $Rate; $i++) { $ausgabe.Add(0.0) } # 1 s Stille vorweg
for ($k = 0; $k -lt $plan.Count; $k++) {
    $stimme, $huelle, $zielDb, $pauseMs, $text = $plan[$k]
    $betont = $null
    if ($text.Contains('*')) { $betont = ($text.Substring($text.IndexOf('*') + 1) -split '[ ,.!]')[0]; $text = $text.Replace('*', '') }
    $roh, $woerter = Synthese $stimme $text
    $roh = $roh[0]; $woerter = $woerter[0]
    $von, $bis = Rand $roh
    $laenge = $bis - $von
    $summe = 0.0
    for ($i = $von; $i -lt $bis; $i++) { $summe += $roh[$i] * $roh[$i] }
    $faktor = [Math]::Pow(10, $zielDb / 20) / [Math]::Sqrt($summe / $laenge)
    # Betontes Wort: von seiner Marke bis zur nächsten.
    $wa = -1; $we = -1
    if ($null -ne $betont) {
        for ($w = 0; $w -lt $woerter.Count; $w++) {
            if ($woerter[$w][1] -eq $betont) { $wa = $woerter[$w][0]; $we = $(if ($w + 1 -lt $woerter.Count) { $woerter[$w + 1][0] } else { $bis }) }
        }
        if ($wa -lt 0) { throw "Betontes Wort $betont ohne Marke." }
    }
    $start = $ausgabe.Count
    $spitze = 0.0
    for ($i = $von; $i -lt $bis; $i++) {
        $t = ($i - $von) / [double]$laenge
        $db = switch ($huelle) {
            'anschwellend' { -9 + 18 * $t }
            'abfallend' { if ($t -lt 0.55) { 0 } else { -16 * ($t - 0.55) / 0.45 } }
            'wort' {
                $rampe = 0.02 * $Rate
                if ($i -lt $wa - $rampe -or $i -ge $we + $rampe) { 0 }
                elseif ($i -lt $wa) { 10 * ($i - ($wa - $rampe)) / $rampe }
                elseif ($i -lt $we) { 10 }
                else { 10 * (1 - ($i - $we) / $rampe) }
            }
            default { 0 }
        }
        $y = $roh[$i] * $faktor * [Math]::Pow(10, $db / 20)
        $spitze = [Math]::Max($spitze, [Math]::Abs($y))
        $ausgabe.Add($y)
    }
    if ($spitze -gt [Math]::Pow(10, -1 / 20)) { throw "Zeile $k des Plans zu laut: Spitze $(20 * [Math]::Log10($spitze)) dBFS." }
    $ende = $ausgabe.Count
    $zeilen.Add(('aeusserung;{0};{1};{2};{3};{4};{5};{6}' -f $k, $stimme, $huelle, $zielDb, $start, $ende, $text))
    foreach ($w in $woerter) {
        # Die Marke des ersten Worts liegt oft vor dem ersten hörbaren Rahmen; sie zählt dann ab dem Anfang der Äußerung.
        $p = [Math]::Max($w[0], $von)
        if ($p -lt $bis) { $zeilen.Add(('wort;{0};{1};{2}' -f $k, ($start + $p - $von), $w[1])) }
    }
    for ($i = 0; $i -lt $pauseMs * $Rate / 1000; $i++) { $ausgabe.Add(0.0) }
}
for ($i = 0; $i -lt $Rate; $i++) { $ausgabe.Add(0.0) } # 1 s Stille danach

# WAV schreiben (PCM 16 Bit, gerundet).
$n = $ausgabe.Count
$datei = Join-Path $ordner ($Name + '.wav')
$fs = [IO.File]::Create($datei)
$bw = New-Object IO.BinaryWriter($fs)
$bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + 2 * $n)); $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$Rate); $bw.Write([int]($Rate * 2)); $bw.Write([int16]2); $bw.Write([int16]16)
$bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int](2 * $n))
foreach ($y in $ausgabe) { $bw.Write([int16][Math]::Max(-32768, [Math]::Min(32767, [Math]::Round($y * 32768)))) }
$bw.Dispose()
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllLines((Join-Path $ordner ($Name + '-marken.csv')), $zeilen, $utf8)
'{0} Saetze, {1:F1} s, {2}' -f $plan.Count, ($n / $Rate), (Get-FileHash $datei -Algorithm SHA256).Hash
