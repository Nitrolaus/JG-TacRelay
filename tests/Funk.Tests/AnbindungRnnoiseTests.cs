// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;

namespace Funk.Tests;

/// <summary>RNNoise-Anbindung gegen die echte rnnoise.dll.</summary>
internal static class AnbindungRnnoiseTests
{
    private const int R = RnnoiseProzessor.RahmenSamples;

    /// <summary>Verarbeitet ein ganzes Signal; liefert Ausgabe und Wahrscheinlichkeiten ab 100 ms.</summary>
    private static (float[] Aus, double[] P) Durchlauf(float[] signal)
    {
        using var r = new RnnoiseProzessor();
        var aus = new float[signal.Length];
        var p = new List<double>();
        for (int f = 0; f + R <= signal.Length; f += R)
        {
            float w = r.Verarbeiten(signal.AsSpan(f, R), aus.AsSpan(f, R));
            Pruefe.Wahr(w is >= 0f and <= 1f, $"Wahrscheinlichkeit {w} in [0, 1]");
            if (f >= 4800) p.Add(w);
        }
        Pruefe.Wahr(aus.All(float.IsFinite), "Ausgabe endlich");
        return (aus, p.ToArray());
    }

    [Test]
    public static void Rnnoise_Wahrscheinlichkeit_RauschenNiedrigSpracheHoch()
    {
        var (_, rauschen) = Durchlauf(AnbindungSignale.Rauschen(AnbindungSignale.Fs * 5, -45, 2));
        var (ausSprache, sprache) = Durchlauf(AnbindungSignale.Sprache(AnbindungSignale.Fs * 5, 3));

        double mR = rauschen.Average(), mS = sprache.Average();
        double medR = AnbindungSignale.Quantil(rauschen, 0.5), medS = AnbindungSignale.Quantil(sprache, 0.5);
        Pruefe.Wahr(mS > mR + 0.25, $"Mittel Sprache {mS:0.000} deutlich über Rauschen {mR:0.000}");
        Pruefe.Wahr(medS > 0.9, $"Median Sprache {medS:0.000}");
        Pruefe.Wahr(medR < 0.5, $"Median Rauschen {medR:0.000}");

        // Nur mit richtiger Skalierung (int16-Bereich hin und zurück) liegt der Ausgang im Bereich des Eingangs.
        double pegel = AnbindungSignale.PegelDb(ausSprache);
        Pruefe.Wahr(pegel is > -36 and < -20, $"Ausgangspegel Sprache {pegel:0.0} dBFS bei -24 dBFS Eingang");
    }

    [Test]
    public static void Rnnoise_Verzoegerung_960Samples()
    {
        foreach (var (name, signal) in new[]
        {
            ("Sprache", AnbindungSignale.Sprache(AnbindungSignale.Fs * 2, 6)),
            ("Rauschen", AnbindungSignale.Rauschen(AnbindungSignale.Fs * 2, -30, 5)),
        })
        {
            var (aus, _) = Durchlauf(signal);
            double beiSoll = AnbindungSignale.Korrelation(signal.AsSpan(9600, 48000), aus.AsSpan(9600 + RnnoiseProzessor.VerzoegerungSamples, 48000));
            foreach (int anders in new[] { 0, 480, 1440 })
            {
                double k = AnbindungSignale.Korrelation(signal.AsSpan(9600, 48000), aus.AsSpan(9600 + anders, 48000));
                Pruefe.Wahr(beiSoll > k + 0.3, $"{name}: Korrelation bei 960 ({beiSoll:0.000}) klar über {anders} ({k:0.000})");
            }
        }
    }

    [Test]
    public static void Rnnoise_Stille_LiefertNull()
    {
        using var r = new RnnoiseProzessor();
        var aus = new float[R];
        aus.AsSpan().Fill(1f);
        for (int i = 0; i < 5; i++) Pruefe.Gleich(0f, r.Verarbeiten(new float[R], aus), "Wahrscheinlichkeit bei Stille");
        Pruefe.Wahr(aus.All(v => v == 0f), "Stille bleibt Stille");
    }

    [Test]
    public static void Rnnoise_InPlace_GleichWieGetrennt()
    {
        var signal = AnbindungSignale.Sprache(R * 50, 8);
        using var a = new RnnoiseProzessor();
        using var b = new RnnoiseProzessor();
        var getrennt = new float[R];
        var inPlace = (float[])signal.Clone();
        for (int f = 0; f < 50; f++)
        {
            float pa = a.Verarbeiten(signal.AsSpan(f * R, R), getrennt);
            float pb = b.Verarbeiten(inPlace.AsSpan(f * R, R));
            Pruefe.Gleich(pa, pb, "Wahrscheinlichkeit");
            Pruefe.Wahr(getrennt.AsSpan().SequenceEqual(inPlace.AsSpan(f * R, R)), $"Rahmen {f} bitgleich");
        }
        Pruefe.Gleich(50L, a.VerarbeiteteRahmen, "Rahmen gezählt");
    }

    [Test]
    public static void Rnnoise_Zuruecksetzen_WieFrischAngelegt()
    {
        var vorlauf = AnbindungSignale.Rauschen(R * 30, -30, 13);
        var signal = AnbindungSignale.Sprache(R * 20, 14);
        using var benutzt = new RnnoiseProzessor();
        using var frisch = new RnnoiseProzessor();
        var weg = new float[R];
        for (int f = 0; f < 30; f++) benutzt.Verarbeiten(vorlauf.AsSpan(f * R, R), weg);
        benutzt.Zuruecksetzen();
        var a = new float[R];
        var b = new float[R];
        for (int f = 0; f < 20; f++)
        {
            float pa = benutzt.Verarbeiten(signal.AsSpan(f * R, R), a);
            float pb = frisch.Verarbeiten(signal.AsSpan(f * R, R), b);
            Pruefe.Gleich(pb, pa, $"Rahmen {f}: Wahrscheinlichkeit");
            Pruefe.Wahr(a.AsSpan().SequenceEqual(b), $"Rahmen {f}: bitgleich");
        }
    }

    [Test]
    public static void Rnnoise_Laengen_WerfenVorDemNativenAufruf()
    {
        using var r = new RnnoiseProzessor();
        Pruefe.Wirft<ArgumentException>(() => r.Verarbeiten(new float[479], new float[R]), "Eingang 479");
        Pruefe.Wirft<ArgumentException>(() => r.Verarbeiten(new float[481], new float[R]), "Eingang 481");
        Pruefe.Wirft<ArgumentException>(() => r.Verarbeiten(new float[R], new float[479]), "Ausgang 479");
        Pruefe.Wirft<ArgumentException>(() => r.Verarbeiten(new float[960]), "In-place 960");
        Pruefe.Gleich(0L, r.VerarbeiteteRahmen, "nie nativ aufgerufen");
        r.Dispose();
        Pruefe.Wirft<ObjectDisposedException>(() => r.Verarbeiten(new float[R]), "nach Dispose");
    }
}
