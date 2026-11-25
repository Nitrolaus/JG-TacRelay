// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>
/// Selbst synthetisierte Geräuschszenen für die Gate-Messung (keine fremden Aufnahmen), 48 kHz, feste Seeds.
/// Alle Szenen haben ein weißes Grundrauschen; die Pegel sind Effektivwerte in dBFS.
/// </summary>
internal static class Testszenen
{
    public const int Rate = 48000;

    private static double Gauss(Random r)
    {
        var u1 = 1.0 - r.NextDouble();
        var u2 = r.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private static float[] Grundrauschen(double sekunden, float dbfs, Random r)
    {
        var x = new float[(int)(sekunden * Rate) / 480 * 480];
        var sigma = Math.Pow(10, dbfs / 20.0);
        for (var i = 0; i < x.Length; i++) x[i] = (float)(sigma * Gauss(r));
        return x;
    }

    /// <summary>Fast Stille: Grundrauschen bei -80 dBFS.</summary>
    public static float[] Stille(double sekunden, int seed) => Grundrauschen(sekunden, -80f, new Random(seed));

    /// <summary>
    /// Tastatur: Grundrauschen -65 dBFS, Anschläge im Mittel 7 je Sekunde, jeder mit Loslassen 60-120 ms später.
    /// Ein Klick ist ein breitbandiger Rauschstoß (Abfall 1 ms) plus eine Resonanz bei 2,5-4,5 kHz (Abfall 2 ms),
    /// Spitze -26 bis -12 dBFS.
    /// </summary>
    public static float[] Tastatur(double sekunden, int seed)
    {
        var r = new Random(seed);
        var x = Grundrauschen(sekunden, -65f, r);
        var t = 0.2;
        while (t < sekunden - 0.3)
        {
            var spitze = Math.Pow(10, (-26 + 14 * r.NextDouble()) / 20);
            Klick(x, (int)(t * Rate), spitze, r);
            Klick(x, (int)((t + 0.06 + 0.06 * r.NextDouble()) * Rate), spitze * 0.6, r);
            t += Math.Max(0.04, -Math.Log(1 - r.NextDouble()) / 7.0);
        }
        return x;
    }

    private static void Klick(float[] x, int start, double spitze, Random r)
    {
        var f = 2500 + 2000 * r.NextDouble();
        var laenge = (int)(0.008 * Rate);
        for (var i = 0; i < laenge && start + i < x.Length; i++)
        {
            var tt = i / (double)Rate;
            var rauschen = Gauss(r) * Math.Exp(-tt / 0.001);
            var resonanz = Math.Sin(2 * Math.PI * f * tt) * Math.Exp(-tt / 0.002);
            x[start + i] += (float)(spitze * 0.5 * (rauschen + resonanz));
        }
    }

    /// <summary>
    /// Tüte: Knisterepisoden von 0,3-1,5 s mit 0,5-3 s Pause, darin im Mittel 400 kurze Rauschimpulse je Sekunde
    /// (0,2-1,5 ms, Abfall 0,3 ms), Spitzen log-gleichverteilt von -45 bis -12 dBFS.
    /// </summary>
    public static float[] Tuete(double sekunden, int seed)
    {
        var r = new Random(seed);
        var x = Grundrauschen(sekunden, -65f, r);
        var t = 0.3;
        while (t < sekunden - 0.5)
        {
            var dauer = 0.3 + 1.2 * r.NextDouble();
            var ende = Math.Min(sekunden - 0.1, t + dauer);
            var ti = t;
            while (ti < ende)
            {
                var spitze = Math.Pow(10, (-45 + 33 * r.NextDouble()) / 20);
                var laenge = (int)((0.0002 + 0.0013 * r.NextDouble()) * Rate);
                var start = (int)(ti * Rate);
                for (var i = 0; i < laenge && start + i < x.Length; i++)
                    x[start + i] += (float)(spitze * Gauss(r) * Math.Exp(-i / (0.0003 * Rate)));
                ti += -Math.Log(1 - r.NextDouble()) / 400.0;
            }
            t = ende + 0.5 + 2.5 * r.NextDouble();
        }
        return x;
    }

    private static readonly (double F1, double F2, double F3)[] Vokale =
    [
        (730, 1090, 2440), // a
        (270, 2290, 3010), // i
        (300, 870, 2240), // u
        (530, 1840, 2480), // e
        (570, 840, 2410), // o
    ];

    /// <summary>
    /// Sprachähnliche Szene: Äußerungen von 0,4-1,6 s aus Vokalen (harmonische Quelle mit 1/k-Obertönen bis 4 kHz,
    /// Grundton 100-220 Hz mit Vibrato und fallender Tonhöhe, drei Formantresonatoren), weicher Anlaut
    /// (Kosinusanstieg 30-100 ms, jede fünfte Äußerung 120 ms und -36 dBFS), Pausen 0,5-2,5 s, Grundrauschen -65 dBFS.
    /// Markiert wird der Anlaut dort, wo die Hüllkurve -20 dB unter dem Plateau erreicht.
    /// </summary>
    public static (float[] Signal, List<Aeusserung> Marken) Sprache(double sekunden, int seed)
    {
        var r = new Random(seed);
        var x = Grundrauschen(sekunden, -65f, r);
        var marken = new List<Aeusserung>();
        var t = 0.8;
        var nr = 0;
        while (true)
        {
            var dauer = 0.4 + 1.2 * r.NextDouble();
            if (t + dauer > sekunden - 0.5) break;
            var schwach = nr % 5 == 4;
            var anstieg = schwach ? 0.12 : 0.03 + 0.07 * r.NextDouble();
            var pegel = schwach ? -36.0 : -32 + 14 * r.NextDouble();
            var start = (int)(t * Rate);
            var ende = Vokal(x, start, (int)(dauer * Rate), anstieg, pegel, r);
            var anlaut = start + (int)(anstieg * Math.Acos(0.8) / Math.PI * Rate);
            marken.Add(new Aeusserung(anlaut, ende));
            t += dauer + 0.5 + 2.0 * r.NextDouble();
            nr++;
        }
        return (x, marken);
    }

    private static int Vokal(float[] x, int start, int laenge, double anstieg, double pegelDbfs, Random r)
    {
        var f0 = 100 + 120 * r.NextDouble();
        var vokal = Vokale[r.Next(Vokale.Length)];
        var y = new double[laenge];
        var phase = 0.0;
        for (var i = 0; i < laenge; i++)
        {
            var tt = i / (double)Rate;
            var f = f0 * (1 - 0.1 * i / laenge) * (1 + 0.02 * Math.Sin(2 * Math.PI * 5 * tt));
            phase += 2 * Math.PI * f / Rate;
            double s = 0;
            for (var k = 1; k * f < 4000; k++) s += Math.Sin(k * phase) / k;
            y[i] = s;
        }
        var gefiltert = new double[laenge];
        foreach (var (fr, band) in new[] { (vokal.F1, 80.0), (vokal.F2, 120.0), (vokal.F3, 160.0) })
            Resonator(y, gefiltert, fr, band);

        var anstiegSamples = (int)(anstieg * Rate);
        var abfall = (int)(0.06 * Rate);
        double plateau = 0;
        var plateauAnzahl = 0;
        for (var i = anstiegSamples; i < laenge - abfall; i++)
        {
            plateau += gefiltert[i] * gefiltert[i];
            plateauAnzahl++;
        }
        var skala = Math.Pow(10, pegelDbfs / 20) / Math.Sqrt(plateau / Math.Max(1, plateauAnzahl));
        for (var i = 0; i < laenge && start + i < x.Length; i++)
        {
            var huelle = i < anstiegSamples ? 0.5 * (1 - Math.Cos(Math.PI * i / anstiegSamples))
                : i >= laenge - abfall ? 0.5 * (1 + Math.Cos(Math.PI * (i - (laenge - abfall)) / abfall))
                : 1.0;
            x[start + i] += (float)(skala * huelle * gefiltert[i]);
        }
        return start + laenge;
    }

    /// <summary>Zweipoliger Resonator (Bandpass) nach Klatt, addiert auf das Ziel.</summary>
    private static void Resonator(double[] ein, double[] ziel, double frequenz, double bandbreite)
    {
        var c = -Math.Exp(-2 * Math.PI * bandbreite / Rate);
        var b = 2 * Math.Exp(-Math.PI * bandbreite / Rate) * Math.Cos(2 * Math.PI * frequenz / Rate);
        var a = 1 - b - c;
        double y1 = 0, y2 = 0;
        for (var i = 0; i < ein.Length; i++)
        {
            var y = a * ein[i] + b * y1 + c * y2;
            y2 = y1;
            y1 = y;
            ziel[i] += y;
        }
    }

    /// <summary>Zwei Szenen addieren (gleiche Länge vorausgesetzt).</summary>
    public static float[] Summe(float[] a, float[] b)
    {
        var x = new float[Math.Min(a.Length, b.Length)];
        for (var i = 0; i < x.Length; i++) x[i] = a[i] + b[i];
        return x;
    }
}
