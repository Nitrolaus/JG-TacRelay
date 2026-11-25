// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>
/// Rechenzeit je 10-ms-Block (p50, p99, Maximum), ausgegeben für den Bericht. Die Grenze ist großzügig und fängt
/// nur grobe Rückschritte; die Werte hängen von Rechner, Last und Build-Art ab.
/// </summary>
internal static class RechenzeitTests
{
    private static (double P50, double P99, double Max) Messen(int anzahl, Action schritt)
    {
        for (var i = 0; i < 200; i++) schritt();
        var zeiten = new double[anzahl];
        for (var i = 0; i < anzahl; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            schritt();
            zeiten[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        Array.Sort(zeiten);
        return (zeiten[anzahl / 2], zeiten[(int)(anzahl * 0.99)], zeiten[^1]);
    }

    private static void Ausgeben(string was, (double P50, double P99, double Max) z) =>
        Console.WriteLine(FormattableString.Invariant($"  Rechenzeit {was}: p50 {z.P50 * 1000:F0} us, p99 {z.P99 * 1000:F0} us, max {z.Max * 1000:F0} us je 10 ms"));

    [Test]
    public static void Rechenzeit_Mischer_Gate_Jitterpuffer()
    {
        var m = new Mischer();
        // Gleichwert-Quellen, damit die Sinusberechnung der Testquelle nicht mitgemessen wird; Summe weit über 1.
        for (var i = 0; i < 16; i++) m.QuelleHinzufuegen(new TestQuelle(0.3f) { Gleichwert = true }, i % 4, funkerPegelDb: 6f);
        var aus = new float[960];
        var mischer = Messen(3000, () => m.Mischen(aus, 2));
        Ausgeben("Mischer 16 Quellen + Begrenzer + Clipping", mischer);

        var (signal, _) = Testszenen.Sprache(40, 12);
        var g = new SprachGate();
        var s = new Stimmhaftigkeit();
        var puffer = new float[480];
        var pos = 0;
        var gate = Messen(3000, () =>
        {
            var rahmen = signal.AsSpan(pos, 480);
            g.Verarbeiten(rahmen, s.Schaetzen(rahmen));
            while (g.Holen(puffer, out _, out _))
            {
            }
            pos = (pos + 480) % (signal.Length - 480);
        });
        Ausgeben("Stimmhaftigkeit + Gate", gate);

        // Jitterpuffer ohne Dekodierkosten: die Attrappe rechnet Sinus, das ist hier nicht gemeint.
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(new NullDekodierer(), uhrUs: uhr.Lesen);
        var pakete = Netzsimulation.Phase(1, 0, 3000, Netzsimulation.Jitter(30_000, 40_000, 2)).OrderBy(p => p.AnkunftUs).ToArray();
        var index = 0;
        var t = 3_000L;
        var block = new float[480];
        var puff = Messen(2500, () =>
        {
            while (index < pakete.Length && pakete[index].AnkunftUs <= t)
            {
                var p = pakete[index++];
                jp.Einreihen(p.AnkunftUs, p.Phase, p.Frame, p.Kennzeichen, p.Daten);
            }
            uhr.Us = t;
            jp.Lesen(block);
            t += 10_000;
        });
        Ausgeben("Jitterpuffer (ohne Dekodierer)", puff);

        PruefeWert.Hoechstens(2.0, mischer.P99, "Mischer p99 in ms");
        PruefeWert.Hoechstens(2.0, gate.P99, "Gate p99 in ms");
        PruefeWert.Hoechstens(2.0, puff.P99, "Jitterpuffer p99 in ms");
    }

    private sealed class NullDekodierer : Funk.Klang.Kern.IDekodierer
    {
        public int Abtastrate => 48000;

        public int Dekodieren(ReadOnlySpan<byte> paket, Span<float> ziel)
        {
            for (var i = 0; i < 960; i++) ziel[i] = 0.1f * ((i & 63) - 32) / 32f;
            return 960;
        }

        public int DekodierenFec(ReadOnlySpan<byte> folgepaket, Span<float> ziel, int fehlendeSamples)
        {
            ziel[..fehlendeSamples].Clear();
            return fehlendeSamples;
        }

        public int Verdecken(Span<float> ziel, int fehlendeSamples)
        {
            ziel[..fehlendeSamples].Clear();
            return fehlendeSamples;
        }

        public void Dispose()
        {
        }
    }
}
