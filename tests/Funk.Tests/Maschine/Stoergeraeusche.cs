// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Selbst erzeugte Störgeräusche für den Vergleich der Rauschfilter (Klangstufe B): Lüfter als rosa und braunes Rauschen,
/// Tastaturanschläge, Musik und Stimmen im Hintergrund. Alles mit fester Saat und ohne fremde Aufnahmen; die Stimmen im
/// Hintergrund sind der Klangkorpus selbst (Windows-Sprachausgabe), um ein Drittel versetzt, so dass meist eine andere Stimme
/// unter dem Satz liegt. Pegel sind Effektivwerte in dBFS über die ganze Länge.
/// </summary>
internal static class Stoergeraeusche
{
    private const int Fs = AnbindungSignale.Fs;

    /// <summary>Rosa Rauschen (Filter nach Paul Kellet, -3 dB je Oktave), wie ein Lüfter.</summary>
    public static float[] Rosa(int n, float dbfs, ulong saat)
    {
        var z = new AnbindungSignale.Zufall(saat);
        var x = new float[n];
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        for (var i = 0; i < n; i++)
        {
            var w = z.Gauss();
            b0 = 0.99886 * b0 + w * 0.0555179;
            b1 = 0.99332 * b1 + w * 0.0750759;
            b2 = 0.96900 * b2 + w * 0.1538520;
            b3 = 0.86650 * b3 + w * 0.3104856;
            b4 = 0.55000 * b4 + w * 0.5329522;
            b5 = -0.7616 * b5 - w * 0.0168980;
            x[i] = (float)(b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362);
            b6 = w * 0.115926;
        }
        return AufPegel(x, dbfs);
    }

    /// <summary>Braunes Rauschen (-6 dB je Oktave, Integrator mit Leck bei etwa 16 Hz), wie ein großer, langsamer Lüfter.</summary>
    public static float[] Braun(int n, float dbfs, ulong saat)
    {
        var z = new AnbindungSignale.Zufall(saat);
        var x = new float[n];
        double y = 0;
        for (var i = 0; i < n; i++)
        {
            y = 0.998 * y + z.Gauss();
            x[i] = (float)y;
        }
        return AufPegel(x, dbfs);
    }

    /// <summary>
    /// Tastatur: Folgen von 3 bis 8 Anschlägen im Abstand von 110 bis 260 ms, dazwischen 0,4 bis 2 s Ruhe. Jeder Anschlag ist ein
    /// Drücken und 60 bis 110 ms später ein leiseres Loslassen: ein Rauschstoß (Abklingzeit 2 bis 4 ms) durch einen Resonator bei
    /// 2 bis 4,5 kHz und ein dumpfer Schlag (180 bis 320 Hz, Abklingzeit 8 ms). Pegel als Effektivwert über die ganze Länge.
    /// </summary>
    public static float[] Tastatur(int n, float dbfs, ulong saat)
    {
        var z = new AnbindungSignale.Zufall(saat);
        var x = new double[n];
        var pos = (int)(z.Gleich(0.1, 0.5) * Fs);
        while (pos < n)
        {
            var anschlaege = 3 + (int)(z.Gleich() * 6);
            for (var a = 0; a < anschlaege && pos < n; a++)
            {
                var staerke = z.Gleich(0.5, 1.0);
                Anschlag(x, pos, staerke, z);
                Anschlag(x, pos + (int)(z.Gleich(0.06, 0.11) * Fs), 0.4 * staerke, z);
                pos += (int)(z.Gleich(0.11, 0.26) * Fs);
            }
            pos += (int)(z.Gleich(0.4, 2.0) * Fs);
        }
        return AufPegel(Array.ConvertAll(x, v => (float)v), dbfs);
    }

    private static void Anschlag(double[] x, int start, double staerke, AnbindungSignale.Zufall z)
    {
        var tauKlick = z.Gleich(0.002, 0.004) * Fs;
        var tauSchlag = 0.008 * Fs;
        var f = z.Gleich(2000, 4500);
        var fs = z.Gleich(180, 320);
        var r = Math.Exp(-Math.PI * 600 / Fs);
        var c = 2 * r * Math.Cos(2 * Math.PI * f / Fs);
        double s1 = 0, s2 = 0;
        var laenge = (int)(0.04 * Fs);
        for (var i = 0; i < laenge && start + i < x.Length; i++)
        {
            var e = z.Gauss() * Math.Exp(-i / tauKlick);
            var y = e * (1 - r) + c * s1 - r * r * s2;
            s2 = s1;
            s1 = y;
            var schlag = 0.6 * Math.Sin(2 * Math.PI * fs * i / Fs) * Math.Exp(-i / tauSchlag);
            x[start + i] += staerke * (y + schlag);
        }
    }

    /// <summary>
    /// Musik: Akkordfolge (C, a, F, G zu je 2 s) aus je drei Tönen mit fünf Obertönen und weichem Einsatz, Basston, Fußtrommel alle
    /// 500 ms (Sinus von 120 auf 50 Hz, abklingend) und Becken alle 250 ms (hochgefiltertes Rauschen, 30 ms).
    /// </summary>
    public static float[] Musik(int n, float dbfs, ulong saat)
    {
        var z = new AnbindungSignale.Zufall(saat);
        var akkorde = new[] { new[] { 261.63, 329.63, 392.00 }, [220.00, 261.63, 329.63], [174.61, 220.00, 261.63], [196.00, 246.94, 293.66] };
        var x = new float[n];
        double hp = 0, alt = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Fs;
            var akkord = akkorde[(int)(t / 2.0) % akkorde.Length];
            var imAkkord = t % 2.0;
            var huelle = Math.Min(1.0, imAkkord / 0.08) * (0.7 + 0.3 * Math.Exp(-imAkkord * 1.5));
            double s = 0;
            foreach (var f in akkord)
            {
                for (var k = 1; k <= 5; k++) s += Math.Sin(2 * Math.PI * f * k * t) / (k * k);
            }
            s *= 0.25 * huelle;
            s += 0.35 * Math.Sin(2 * Math.PI * akkord[0] / 2 * t) * huelle;
            var imSchlag = t % 0.5;
            s += 0.8 * Math.Sin(2 * Math.PI * (50 + 70 * Math.Exp(-imSchlag * 30)) * imSchlag) * Math.Exp(-imSchlag * 12);
            var imBecken = t % 0.25;
            var w = z.Gauss();
            hp = 0.85 * (hp + w - alt);
            alt = w;
            if (imBecken < 0.03) s += 0.15 * hp * Math.Exp(-imBecken * 120);
            x[i] = (float)s;
        }
        return AufPegel(x, dbfs);
    }

    /// <summary>Stimmen im Hintergrund: der Klangkorpus um ein Drittel versetzt (im Kreis), Pegel relativ zum Korpus in dB.</summary>
    public static float[] Stimmen(int n, float relativDb)
    {
        var k = Klangkorpus.Signal;
        var versatz = k.Length / 3;
        var f = Pegel.DbZuLinear(relativDb);
        var x = new float[n];
        for (var i = 0; i < n; i++) x[i] = f * k[(i + versatz) % k.Length];
        return x;
    }

    private static float[] AufPegel(float[] x, float dbfs)
    {
        double e = 0;
        foreach (var v in x) e += (double)v * v;
        var faktor = (float)(Math.Pow(10, dbfs / 20.0) / Math.Sqrt(Math.Max(e / x.Length, 1e-30)));
        for (var i = 0; i < x.Length; i++) x[i] *= faktor;
        return x;
    }
}
