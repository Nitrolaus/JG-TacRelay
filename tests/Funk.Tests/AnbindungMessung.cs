// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using Funk.Klang.Kern;

namespace Funk.Tests;

/// <summary>
/// Echtzeitregeln der Anbindung: keine Allokation in den heißen Pfaden, und die Rechenzeit je
/// Rahmen (offener Punkt aus recherche\audio-dsp.md, Gegenprüfung 9). Die Zeiten werden als
/// MESSUNG-Zeilen ausgegeben; geprüft wird nur grob, damit ein belasteter Rechner nicht rot wird.
/// </summary>
internal static class AnbindungMessung
{
    private const int Aufwaermen = 200;

    private static string Ms(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    private static (double Mittel, double Median, double P99, double Max) Kennzahlen(List<double> zeiten)
    {
        var arr = zeiten.Skip(Aufwaermen).ToArray();
        return (arr.Average(), AnbindungSignale.Quantil(arr, 0.5), AnbindungSignale.Quantil(arr, 0.99), arr.Max());
    }

    private static void Melden(string was, List<double> zeiten, double pMax)
    {
        var (mittel, median, p99, max) = Kennzahlen(zeiten);
        Console.WriteLine($"MESSUNG {was}: Mittel {Ms(mittel)} ms, Median {Ms(median)} ms, p99 {Ms(p99)} ms, Max {Ms(max)} ms, {zeiten.Count - Aufwaermen} Rahmen");
        Pruefe.Wahr(mittel < 1.0, $"{was}: Mittel {Ms(mittel)} ms unter 1 ms");
        Pruefe.Wahr(p99 < pMax, $"{was}: p99 {Ms(p99)} ms unter {pMax} ms");
    }

    [Test]
    public static void Anbindung_HeissePfade_AllokierenNicht()
    {
        var sprache = AnbindungSignale.Sprache(48000, 71);
        using var k = new OpusKodierer();
        using var d = new OpusDekodierer();
        using var rn = new RnnoiseProzessor();
        using var rs = new Resampler(48000, 44100);
        using var apm = new EchoProzessor();
        apm.Konfigurieren(ApmEinstellungen.Sprechfunk);
        var clip = new OpusSoftClip();
        var paket = new byte[OpusKodierer.MaxPaketBytes];
        var pcm = new float[OpusDekodierer.MaxSamples];
        var rahmen = new float[480];
        var aus = new float[1024];
        k.SetzeVerlustHinweis(10);

        void Runde(int i)
        {
            int f = (i % 49) * 960;
            k.SetzeVerlustHinweis(10 + (i & 1));
            int n = k.Kodieren(sprache.AsSpan(f, 960), paket);
            d.Dekodieren(paket.AsSpan(0, n), pcm);
            d.DekodierenFec(paket.AsSpan(0, n), pcm, 960);
            d.Verdecken(pcm, 480);
            clip.Anwenden(pcm.AsSpan(0, 960));
            sprache.AsSpan(f, 480).CopyTo(rahmen);
            rn.Verarbeiten(rahmen);
            apm.VerarbeiteWiedergabe(sprache.AsSpan(f + 480, 480));
            apm.VerarbeiteAufnahme(rahmen);
            apm.SetzeVerzoegerung(40);
            _ = apm.Statistik();
            rs.Verarbeiten(sprache.AsSpan(f, 480), aus, out _, out _);
            _ = OpusPaket.Toc(paket);
            _ = OpusPaket.HatLbrr(paket.AsSpan(0, n));
            if (i % 50 == 0)
            {
                rn.Zuruecksetzen();
                d.Zuruecksetzen();
            }
        }

        for (int i = 0; i < 20; i++) Runde(i);
        long vorher = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 20; i < 220; i++) Runde(i);
        long allokiert = GC.GetAllocatedBytesForCurrentThread() - vorher;
        Pruefe.Gleich(0L, allokiert, "allokierte Bytes in 200 Runden aller heißen Pfade");
    }

    [Test]
    public static void Anbindung_Rechenzeit_Je10msRahmen()
    {
        int n = AnbindungSignale.Fs * 30;
        var fern = AnbindungSignale.Sprache(n, 81, -18);
        var nah = AnbindungSignale.Sprache(n, 82, -24);
        var rauschen = AnbindungSignale.Rauschen(n, -50, 83);
        var aufnahme = new float[n];
        int verzug = 48 * 40;
        for (int i = 0; i < n; i++)
            aufnahme[i] = (i >= verzug ? 0.5f * fern[i - verzug] : 0f) + ((i / 96000) % 3 == 2 ? nah[i] : 0f) + rauschen[i];

        // RNNoise: je 10 ms
        using (var rn = new RnnoiseProzessor())
        {
            var rahmen = new float[480];
            var t = new List<double>(n / 480);
            for (int f = 0; f + 480 <= n; f += 480)
            {
                aufnahme.AsSpan(f, 480).CopyTo(rahmen);
                long a = Stopwatch.GetTimestamp();
                rn.Verarbeiten(rahmen);
                t.Add(Stopwatch.GetElapsedTime(a).TotalMilliseconds);
            }
            Melden("RNNoise 480 Samples", t, 5.0);
        }

        // APM: Wiedergabe + Aufnahme je 10 ms, Vorschlag Sprechfunk (AEC3, HPF, AGC2, NS aus) und mit NS
        foreach (var (name, einstellungen) in new[]
        {
            ("APM AEC3+HPF+AGC2 (Render+Capture)", ApmEinstellungen.Sprechfunk),
            ("APM AEC3+HPF+AGC2+NS mittel (Render+Capture)", ApmEinstellungen.Sprechfunk with { Rauschen = ApmRauschStufe.Mittel }),
        })
        {
            using var apm = new EchoProzessor();
            apm.Konfigurieren(einstellungen);
            apm.SetzeVerzoegerung(40);
            var rahmen = new float[480];
            var t = new List<double>(n / 480);
            for (int f = 0; f + 480 <= n; f += 480)
            {
                aufnahme.AsSpan(f, 480).CopyTo(rahmen);
                long a = Stopwatch.GetTimestamp();
                apm.VerarbeiteWiedergabe(fern.AsSpan(f, 480));
                apm.VerarbeiteAufnahme(rahmen);
                t.Add(Stopwatch.GetElapsedTime(a).TotalMilliseconds);
            }
            Melden(name, t, 5.0);
        }

        // Opus: Kodieren + Dekodieren je 20 ms (zum Vergleich)
        using (var k = new OpusKodierer())
        using (var d = new OpusDekodierer())
        {
            k.SetzeVerlustHinweis(10);
            var paket = new byte[OpusKodierer.MaxPaketBytes];
            var pcm = new float[960];
            var tk = new List<double>(n / 960);
            var td = new List<double>(n / 960);
            for (int f = 0; f + 960 <= n; f += 960)
            {
                long a = Stopwatch.GetTimestamp();
                int len = k.Kodieren(aufnahme.AsSpan(f, 960), paket);
                long b = Stopwatch.GetTimestamp();
                d.Dekodieren(paket.AsSpan(0, len), pcm);
                long c = Stopwatch.GetTimestamp();
                tk.Add(Stopwatch.GetElapsedTime(a, b).TotalMilliseconds);
                td.Add(Stopwatch.GetElapsedTime(b, c).TotalMilliseconds);
            }
            Melden("Opus Kodieren 960 Samples (K10)", tk, 10.0);
            Melden("Opus Dekodieren 960 Samples (K7+BWE)", td, 10.0);
        }
    }

    [Test]
    public static void Anbindung_Latenz_ApmUndRnnoise()
    {
        // Verzögerung, die die Verarbeitung selbst dem Nahsprecher hinzufügt (ohne Fernsignal).
        int n = AnbindungSignale.Fs * 2;
        var nah = AnbindungSignale.Sprache(n, 61, -24);
        var auf = (float[])nah.Clone();
        var still = new float[480];
        using (var apm = new EchoProzessor())
        {
            apm.Konfigurieren(ApmEinstellungen.Sprechfunk with { Agc2Adaptiv = false });
            for (int f = 0; f < n / 480; f++)
            {
                apm.VerarbeiteWiedergabe(still);
                apm.VerarbeiteAufnahme(auf.AsSpan(f * 480, 480));
            }
        }
        var (apmVerzug, apmKorr) = AnbindungSignale.BesterVerzug(nah, auf, 24000, 24000, 700);
        Console.WriteLine($"MESSUNG Latenz APM mit AEC3+HPF: {apmVerzug} Samples ({apmVerzug / 48.0:0.0} ms), Korrelation {apmKorr:0.000}");
        Pruefe.Wahr(apmKorr > 0.9, $"APM-Verzug eindeutig: Korrelation {apmKorr:0.000}");
        Pruefe.Wahr(apmVerzug is > 300 and < 600, $"APM-Verzug {apmVerzug} Samples");
        Console.WriteLine($"MESSUNG Latenz RNNoise: {RnnoiseProzessor.VerzoegerungSamples} Samples ({RnnoiseProzessor.VerzoegerungSamples / 48.0:0.0} ms), geprüft in Rnnoise_Verzoegerung_960Samples");
    }
}
