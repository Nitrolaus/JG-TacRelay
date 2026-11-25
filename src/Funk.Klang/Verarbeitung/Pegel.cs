// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>Festes Arbeitsformat der Verarbeitung: float32, 48 kHz, mono je Strom.</summary>
public static class Klangformat
{
    public const int Abtastrate = 48000;
    public const int SamplesJeMs = 48;
    public const int Samples10ms = 480;
    public const int Samples20ms = 960;

    /// <summary>Größter Opus-Rahmen (120 ms); so groß sind die Dekodierpuffer, falls ein Paket mehr liefert als 20 ms.</summary>
    public const int MaxSamplesJePaket = 5760;
}

/// <summary>
/// Pegelrechnung. dBFS bezieht sich hier auf den mittleren quadratischen Wert: Ein Rechteck mit
/// Vollaussteuerung hat 0 dBFS, ein voll ausgesteuerter Sinus -3,01 dBFS.
/// </summary>
public static class Pegel
{
    /// <summary>Untergrenze für Pegelangaben, damit Stille nicht minus unendlich ergibt.</summary>
    public const float MinDbfs = -100f;

    public static float DbZuLinear(float db) => float.IsNegativeInfinity(db) ? 0f : MathF.Pow(10f, db / 20f);

    public static float LinearZuDb(float linear) => linear <= 1e-5f ? MinDbfs : 20f * MathF.Log10(linear);

    public static float LeistungZuDb(float leistung) => leistung <= 1e-10f ? MinDbfs : 10f * MathF.Log10(leistung);

    /// <summary>Mittlere Leistung (mittlerer quadratischer Wert).</summary>
    public static float Leistung(ReadOnlySpan<float> x)
    {
        if (x.IsEmpty) return 0f;
        double summe = 0;
        foreach (var v in x) summe += v * v;
        return (float)(summe / x.Length);
    }

    public static float Dbfs(ReadOnlySpan<float> x) => LeistungZuDb(Leistung(x));

    public static float Spitze(ReadOnlySpan<float> x)
    {
        var s = 0f;
        foreach (var v in x)
        {
            var b = MathF.Abs(v);
            if (b > s) s = b;
        }
        return s;
    }
}
