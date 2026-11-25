// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Filter 2. Ordnung nach dem Audio-EQ-Kochbuch von Robert Bristow-Johnson (als W3C Note "Audio EQ Cookbook" veröffentlicht), bei
/// <see cref="Klangformat.Abtastrate"/>: Hochpass, Tiefpass und Glocke (Peaking EQ). Direktform I mit eigenem Zustand in double. Die
/// Koeffizienten bleiben wie im Kochbuch (a0 nicht herausgekürzt), gerechnet wird je Schritt durch a0: So rechnet der Funk-Klick seit 0.6.0
/// Stufe 2b, und seine Vorlage in den Tests muss Wert für Wert gleich bleiben. Eine Kopie trägt ihren Zustand mit; die Fabriken liefern den
/// Zustand null. Gemeinsam für <see cref="FunkKlick"/> und <see cref="Klangfarbe"/> (seit 0.9.3). Allokiert nichts.
/// </summary>
internal struct Biquad
{
    /// <summary>Zustandswerte darunter (etwa -500 dBFS) setzt <see cref="KleineNullen"/> auf 0, damit nach langer Stille nichts subnormal wird.</summary>
    private const double Klein = 1e-25;

    private double _b0, _b1, _b2, _a0, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    /// <summary>Hochpass mit Grenzfrequenz <paramref name="hz"/> und Güte <paramref name="q"/>.</summary>
    public static Biquad Hochpass(double hz, double q)
    {
        var (c, a) = Winkel(hz, q);
        return new Biquad { _b0 = (1 + c) / 2, _b1 = -(1 + c), _b2 = (1 + c) / 2, _a0 = 1 + a, _a1 = -2 * c, _a2 = 1 - a };
    }

    /// <summary>Tiefpass mit Grenzfrequenz <paramref name="hz"/> und Güte <paramref name="q"/>.</summary>
    public static Biquad Tiefpass(double hz, double q)
    {
        var (c, a) = Winkel(hz, q);
        return new Biquad { _b0 = (1 - c) / 2, _b1 = 1 - c, _b2 = (1 - c) / 2, _a0 = 1 + a, _a1 = -2 * c, _a2 = 1 - a };
    }

    /// <summary>
    /// Glocke (Peaking EQ) um <paramref name="hz"/> mit Güte <paramref name="q"/> und <paramref name="db"/> Anhebung (negativ: Absenkung). Im
    /// Kochbuch: A = 10^(dB/40), b0 = 1 + alpha A, b1 = -2 cos w0, b2 = 1 - alpha A, a0 = 1 + alpha / A, a1 = -2 cos w0, a2 = 1 - alpha / A.
    /// </summary>
    public static Biquad Glocke(double hz, double q, double db)
    {
        var (c, a) = Winkel(hz, q);
        var g = Math.Pow(10, db / 40);
        return new Biquad { _b0 = 1 + (a * g), _b1 = -2 * c, _b2 = 1 - (a * g), _a0 = 1 + (a / g), _a1 = -2 * c, _a2 = 1 - (a / g) };
    }

    /// <summary>Ein Wert durch das Filter.</summary>
    public double Schritt(double x)
    {
        var y = ((_b0 * x) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2)) / _a0;
        (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
        return y;
    }

    /// <summary>Betragsquadrat des Frequenzgangs bei <paramref name="hz"/>, |H(e^jw)|^2.</summary>
    public readonly double Betrag2(double hz)
    {
        var w = 2 * Math.PI * hz / Klangformat.Abtastrate;
        var (s1, c1) = Math.SinCos(w);
        var (s2, c2) = Math.SinCos(2 * w);
        var zr = _b0 + (_b1 * c1) + (_b2 * c2);
        var zi = (_b1 * s1) + (_b2 * s2);
        var nr = _a0 + (_a1 * c1) + (_a2 * c2);
        var ni = (_a1 * s1) + (_a2 * s2);
        return ((zr * zr) + (zi * zi)) / ((nr * nr) + (ni * ni));
    }

    /// <summary>Den Zustand (die letzten zwei Ein- und Ausgangswerte) eines anderen Filters übernehmen, die Koeffizienten bleiben.</summary>
    public void ZustandVon(in Biquad anderes) => (_x1, _x2, _y1, _y2) = (anderes._x1, anderes._x2, anderes._y1, anderes._y2);

    /// <summary>Den Zustand setzen, als wären zuletzt <paramref name="x1"/>, <paramref name="x2"/> hinein- und <paramref name="y1"/>, <paramref name="y2"/> herausgegangen.</summary>
    public void ZustandSetzen(double x1, double x2, double y1, double y2) => (_x1, _x2, _y1, _y2) = (x1, x2, y1, y2);

    /// <summary>Der Zustand ist endlich (kein NaN, kein Unendlich).</summary>
    public readonly bool Endlich => double.IsFinite(_x1) && double.IsFinite(_x2) && double.IsFinite(_y1) && double.IsFinite(_y2);

    /// <summary>Sehr kleine Zustandswerte auf 0, damit das Filter nach langer Stille nicht in subnormalen Werten rechnet.</summary>
    public void KleineNullen()
    {
        if (Math.Abs(_x1) < Klein) _x1 = 0;
        if (Math.Abs(_x2) < Klein) _x2 = 0;
        if (Math.Abs(_y1) < Klein) _y1 = 0;
        if (Math.Abs(_y2) < Klein) _y2 = 0;
    }

    private static (double Cos, double Alpha) Winkel(double hz, double q)
    {
        var w = 2 * Math.PI * hz / Klangformat.Abtastrate;
        return (Math.Cos(w), Math.Sin(w) / (2 * q));
    }
}
