// JG TacRelay - Maintainer: Sarevoc
using System.Numerics;

namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Zeitkürzen und Zeitdehnen um eine Grundperiode (Accelerate / Preemptive Expand nach dem Prinzip von
/// NetEQ): Der Versatz K mit der höchsten normierten Kreuzkorrelation zwischen dem Rahmenanfang und dem
/// um K verschobenen Abschnitt wird gesucht. Nur bei klarer Periodizität (Korrelation mindestens
/// <see cref="Korrelationsschwelle"/>) oder in leisen Rahmen wird gekürzt bzw. gedehnt, mit
/// Kosinus-Überblendung über K Samples. Anfang und Ende des Ergebnisses schließen stetig an.
/// Allokiert nicht.
/// </summary>
internal static class Zeitdehnung
{
    public const int MinVersatz = 120; // 2,5 ms, Grundfrequenz 400 Hz
    public const int MaxVersatz = 480; // 10 ms, Grundfrequenz 100 Hz
    public const float Korrelationsschwelle = 0.85f;
    private const int Fenster = 240; // 5 ms Vergleichsfenster

    /// <summary>
    /// Kürzt <paramref name="ein"/> um eine Periode. Rückgabe: Länge in <paramref name="aus"/>;
    /// gleich der Eingangslänge, wenn nicht gekürzt wurde. <paramref name="aus"/> muss mindestens so lang sein wie die Eingabe.
    /// </summary>
    public static int Kuerzen(ReadOnlySpan<float> ein, Span<float> aus, bool leise)
    {
        var n = ein.Length;
        var k = Versatz(ein, leise, Math.Min(MaxVersatz, n / 2));
        if (k == 0)
        {
            ein.CopyTo(aus);
            return n;
        }
        // aus[0..k) blendet von ein[0..k) nach ein[k..2k), danach geht es mit ein[2k..] weiter.
        for (var i = 0; i < k; i++)
        {
            var w = Ausblendgewicht(i, k);
            aus[i] = ein[i] * w + ein[i + k] * (1f - w);
        }
        ein[(2 * k)..].CopyTo(aus[k..]);
        return n - k;
    }

    /// <summary>
    /// Dehnt <paramref name="ein"/> um eine Periode. Rückgabe: Länge in <paramref name="aus"/>;
    /// <paramref name="aus"/> muss mindestens Eingangslänge plus <see cref="MaxVersatz"/> fassen.
    /// </summary>
    public static int Dehnen(ReadOnlySpan<float> ein, Span<float> aus, bool leise)
    {
        var n = ein.Length;
        var k = Versatz(ein, leise, Math.Min(MaxVersatz, n / 2));
        if (k == 0)
        {
            ein.CopyTo(aus);
            return n;
        }
        // aus = ein[0..k), dann Überblendung von ein[k..2k) zurück auf ein[0..k), dann ein[k..] noch einmal.
        ein[..k].CopyTo(aus);
        for (var i = 0; i < k; i++)
        {
            var w = Ausblendgewicht(i, k);
            aus[k + i] = ein[k + i] * w + ein[i] * (1f - w);
        }
        ein[k..].CopyTo(aus[(2 * k)..]);
        return n + k;
    }

    /// <summary>Gewicht von 1 nach 0 (halbe Kosinusperiode), am Anfang und Ende mit Steigung null.</summary>
    private static float Ausblendgewicht(int i, int laenge) => 0.5f * (1f + MathF.Cos(MathF.PI * i / laenge));

    private static int Versatz(ReadOnlySpan<float> x, bool leise, int kMax)
    {
        if (kMax < MinVersatz) return 0;
        if (leise) return kMax;
        var k = BesterVersatz(x, kMax, out var korrelation);
        return korrelation >= Korrelationsschwelle ? k : 0;
    }

    private static int BesterVersatz(ReadOnlySpan<float> x, int kMax, out float besteKorrelation)
    {
        var fenster = Math.Min(Fenster, x.Length - kMax);
        besteKorrelation = 0f;
        if (fenster < 32) return 0;
        var a = x[..fenster];
        var energieA = Skalarprodukt(a, a);
        if (energieA <= 1e-12f) return 0;
        // Gleitende Energie in double: In float löscht die laufende Summe bei abklingenden Rahmen aus, die
        // Energie wird zu klein geschätzt und die Korrelation zu groß.
        double energieB = Skalarprodukt(x.Slice(MinVersatz, fenster), x.Slice(MinVersatz, fenster));
        var bester = 0;
        for (var k = MinVersatz; k <= kMax; k++)
        {
            if (k > MinVersatz)
            {
                double raus = x[k - 1];
                double rein = x[k + fenster - 1];
                energieB += rein * rein - raus * raus;
            }
            if (energieB <= 1e-12) continue;
            var c = (float)(Skalarprodukt(a, x.Slice(k, fenster)) / Math.Sqrt(energieA * energieB));
            if (c > besteKorrelation)
            {
                besteKorrelation = c;
                bester = k;
            }
        }
        return bester;
    }

    internal static float Skalarprodukt(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var n = Math.Min(a.Length, b.Length);
        var summe = 0f;
        var i = 0;
        if (Vector.IsHardwareAccelerated && n >= Vector<float>.Count)
        {
            var acc = Vector<float>.Zero;
            for (; i <= n - Vector<float>.Count; i += Vector<float>.Count)
                acc += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
            summe = Vector.Dot(acc, Vector<float>.One);
        }
        for (; i < n; i++) summe += a[i] * b[i];
        return summe;
    }
}
