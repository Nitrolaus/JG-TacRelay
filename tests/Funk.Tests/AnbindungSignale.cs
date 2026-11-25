// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Tests;

/// <summary>
/// Selbst synthetisierte Testsignale mit festen Seeds (keine fremden Klänge) und kleine
/// Messhilfen für die Tests der Klang-Anbindung. Alles deterministisch.
/// </summary>
internal static class AnbindungSignale
{
    internal const int Fs = 48000;

    /// <summary>SplitMix64: kleiner, fest definierter Zufallsgenerator, unabhängig von der .NET-Version.</summary>
    internal sealed class Zufall(ulong seed)
    {
        private ulong _s = seed;

        internal ulong Naechste()
        {
            ulong z = _s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Gleichverteilt in [0, 1).</summary>
        internal double Gleich() => (Naechste() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Gleichverteilt in [a, b).</summary>
        internal double Gleich(double a, double b) => a + (b - a) * Gleich();

        /// <summary>Normalverteilt (Box-Muller), Mittel 0, Streuung 1.</summary>
        internal double Gauss()
        {
            double u1 = 1.0 - Gleich();
            double u2 = Gleich();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }

    internal static float[] Sinus(int n, double frequenz, double amplitude, double phase = 0)
    {
        var x = new float[n];
        for (int i = 0; i < n; i++) x[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequenz * i / Fs + phase));
        return x;
    }

    /// <summary>Weißes gaußsches Rauschen mit gegebenem RMS-Pegel in dBFS.</summary>
    internal static float[] Rauschen(int n, double rmsDbfs, ulong seed)
    {
        var z = new Zufall(seed);
        double s = Math.Pow(10, rmsDbfs / 20);
        var x = new float[n];
        for (int i = 0; i < n; i++) x[i] = (float)(s * z.Gauss());
        return x;
    }

    private static readonly (double F1, double F2, double F3)[] Vokale =
    [
        (730, 1090, 2440), // a
        (270, 2290, 3010), // i
        (300, 870, 2240),  // u
        (530, 1840, 2480), // e
        (570, 840, 2410),  // o
    ];

    /// <summary>
    /// Sprachähnliches Signal durch Formant-Synthese: Rosenberg-Glottispuls mit schwankender
    /// Grundfrequenz, fünf Resonatoren mit gleitenden Vokalformanten, Silben mit Hüllkurve,
    /// Reibelaute als Rauschstöße und Pausen. Gesamtpegel <paramref name="rmsDbfs"/>.
    /// </summary>
    internal static float[] Sprache(int n, ulong seed, double rmsDbfs = -24)
    {
        var z = new Zufall(seed);
        var quelle = new double[n];   // Anregung (stimmhaft + Hauch + Reibung)
        var f1 = new double[n];
        var f2 = new double[n];
        var f3 = new double[n];

        int pos = 0;
        double phase = 0, g0 = 0;
        var aktuell = Vokale[0];
        int silbe = 0;
        while (pos < n)
        {
            // Reibelaut oder Plosiv vor dem Vokal
            if (z.Gleich() < 0.6)
            {
                int laenge = (int)(z.Gleich(0.03, 0.08) * Fs);
                double pegel = z.Gleich(0.05, 0.15);
                double hp = 0, alt = 0;
                for (int i = 0; i < laenge && pos < n; i++, pos++)
                {
                    double w = z.Gauss();
                    hp = 0.9 * (hp + w - alt); // Hochpass erster Ordnung: Zischen
                    alt = w;
                    double huelle = Math.Sin(Math.PI * i / laenge);
                    quelle[pos] = pegel * hp * huelle;
                    f1[pos] = aktuell.F1; f2[pos] = aktuell.F2; f3[pos] = aktuell.F3;
                }
            }

            // Stimmhafter Vokal
            var ziel = Vokale[(int)(z.Gleich() * Vokale.Length) % Vokale.Length];
            int dauer = (int)(z.Gleich(0.12, 0.26) * Fs);
            double grund = z.Gleich(105, 150);
            double gleit = z.Gleich(-25, 25);
            int an = (int)(0.015 * Fs), ab = (int)(0.03 * Fs), uebergang = (int)(0.04 * Fs);
            for (int i = 0; i < dauer && pos < n; i++, pos++)
            {
                double t = (double)i / dauer;
                double f0 = (grund + gleit * t) * (1 + 0.02 * Math.Sin(2 * Math.PI * 5.0 * pos / Fs));
                phase += f0 / Fs;
                if (phase >= 1) phase -= 1;
                double g = phase < 0.4 ? 0.5 * (1 - Math.Cos(Math.PI * phase / 0.4))
                         : phase < 0.56 ? Math.Cos(Math.PI * (phase - 0.4) / 0.32)
                         : 0;
                double puls = g - g0; // Lippenabstrahlung: erste Differenz
                g0 = g;
                double huelle = i < an ? 0.5 * (1 - Math.Cos(Math.PI * i / an))
                              : i > dauer - ab ? 0.5 * (1 + Math.Cos(Math.PI * (i - (dauer - ab)) / ab))
                              : 1;
                double hauch = 0.02 * z.Gauss() * (phase < 0.56 ? 1 : 0.3);
                quelle[pos] = huelle * (puls + hauch);
                double k = Math.Min(1.0, (double)i / uebergang);
                f1[pos] = aktuell.F1 + k * (ziel.F1 - aktuell.F1);
                f2[pos] = aktuell.F2 + k * (ziel.F2 - aktuell.F2);
                f3[pos] = aktuell.F3 + k * (ziel.F3 - aktuell.F3);
            }
            aktuell = ziel;

            // Pause, jede sechste Silbe länger
            silbe++;
            int pause = (int)((silbe % 6 == 0 ? z.Gleich(0.2, 0.3) : z.Gleich(0.04, 0.12)) * Fs);
            for (int i = 0; i < pause && pos < n; i++, pos++)
            {
                f1[pos] = aktuell.F1; f2[pos] = aktuell.F2; f3[pos] = aktuell.F3;
            }
        }

        // Fünf Resonatoren in Reihe: F1-F3 gleitend, F4 und F5 fest
        var y = new double[n];
        var zustand = new double[10];
        for (int i = 0; i < n; i++)
        {
            double v = quelle[i];
            v = Resonator(v, f1[i], 90, zustand, 0);
            v = Resonator(v, f2[i], 110, zustand, 2);
            v = Resonator(v, f3[i], 150, zustand, 4);
            v = Resonator(v, 3300, 250, zustand, 6);
            v = Resonator(v, 3750, 300, zustand, 8);
            y[i] = v;
        }

        double energie = 0;
        foreach (double v in y) energie += v * v;
        double rms = Math.Sqrt(energie / n);
        double faktor = Math.Pow(10, rmsDbfs / 20) / Math.Max(rms, 1e-30);
        var x = new float[n];
        for (int i = 0; i < n; i++) x[i] = (float)Math.Clamp(y[i] * faktor, -0.99, 0.99);
        return x;
    }

    /// <summary>Zweipoliger Resonator mit Verstärkung 1 bei Gleichspannung.</summary>
    private static double Resonator(double x, double f, double b, double[] s, int i)
    {
        double r = Math.Exp(-Math.PI * b / Fs);
        double c = 2 * r * Math.Cos(2 * Math.PI * f / Fs);
        double y = x * (1 - c + r * r) + c * s[i] - r * r * s[i + 1];
        s[i + 1] = s[i];
        s[i] = y;
        return y;
    }

    internal static double Energie(ReadOnlySpan<float> x)
    {
        double e = 0;
        foreach (float v in x) e += (double)v * v;
        return e;
    }

    /// <summary>RMS-Pegel in dBFS (-300 bei Stille).</summary>
    internal static double PegelDb(ReadOnlySpan<float> x) =>
        x.IsEmpty ? -300 : 10 * Math.Log10(Math.Max(Energie(x) / x.Length, 1e-30));

    /// <summary>Normierte Kreuzkorrelation zweier gleich langer Abschnitte.</summary>
    internal static double Korrelation(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int n = Math.Min(a.Length, b.Length);
        double ab = 0, aa = 0, bb = 0;
        for (int i = 0; i < n; i++)
        {
            ab += (double)a[i] * b[i];
            aa += (double)a[i] * a[i];
            bb += (double)b[i] * b[i];
        }
        return aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
    }

    /// <summary>
    /// Sucht die Verschiebung 0..<paramref name="maxVerzug"/>, um die <paramref name="spaet"/> hinter
    /// <paramref name="frueh"/> liegt, über das Fenster [start, start+laenge) von <paramref name="frueh"/>.
    /// </summary>
    internal static (int Verzug, double Korrelation) BesterVerzug(float[] frueh, float[] spaet, int start, int laenge, int maxVerzug)
    {
        int best = 0;
        double bestK = double.NegativeInfinity;
        for (int d = 0; d <= maxVerzug; d++)
        {
            if (start + d + laenge > spaet.Length) break;
            double k = Korrelation(frueh.AsSpan(start, laenge), spaet.AsSpan(start + d, laenge));
            if (k > bestK) { bestK = k; best = d; }
        }
        return (best, bestK);
    }

    /// <summary>Signal-Rausch-Abstand von <paramref name="test"/> gegen <paramref name="referenz"/> in dB.</summary>
    internal static double AbstandDb(ReadOnlySpan<float> referenz, ReadOnlySpan<float> test)
    {
        double s = 0, d = 0;
        for (int i = 0; i < referenz.Length; i++)
        {
            s += (double)referenz[i] * referenz[i];
            double f = (double)referenz[i] - test[i];
            d += f * f;
        }
        return 10 * Math.Log10(Math.Max(s, 1e-30) / Math.Max(d, 1e-30));
    }

    /// <summary>Quantil (0..1) einer Stichprobe; sortiert eine Kopie.</summary>
    internal static double Quantil(ReadOnlySpan<double> werte, double q)
    {
        var kopie = werte.ToArray();
        Array.Sort(kopie);
        int i = (int)Math.Ceiling(q * kopie.Length) - 1;
        return kopie[Math.Clamp(i, 0, kopie.Length - 1)];
    }
}
