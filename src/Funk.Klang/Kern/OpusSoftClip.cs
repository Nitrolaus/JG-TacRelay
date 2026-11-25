// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>
/// <c>opus_pcm_soft_clip</c> als letzte Stufe hinter dem Mischer (ENTWURF: nie hart clippen). Hält
/// je Kanal einen Zustandswert, damit die Kennlinie über Rahmengrenzen stetig bleibt. Allokiert nur
/// im Konstruktor. Nur aus einem Thread benutzen.
/// </summary>
/// <remarks>
/// libopus begrenzt ±Unendlich auf ±2 und formt dann weich, lässt NaN aber unverändert durch
/// (Gegenprüfung). Weil danach nur noch das Ausgabegerät kommt, ersetzt die Hülle NaN und ±Unendlich
/// vorher durch 0 (gezählt in <see cref="ErsetzteWerte"/>). Die Ausgabe ist damit immer endlich und
/// liegt in [-1, 1].
/// </remarks>
public sealed unsafe class OpusSoftClip
{
    private readonly float[] _speicher;
    private long _ersetzt;

    public OpusSoftClip(int kanaele = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kanaele);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kanaele, 8);
        Kanaele = kanaele;
        _speicher = new float[kanaele];
    }

    public int Kanaele { get; }

    /// <summary>NaN- und Unendlich-Werte, die vor dem Begrenzen durch 0 ersetzt wurden.</summary>
    public long ErsetzteWerte => Volatile.Read(ref _ersetzt);

    /// <summary>Begrenzt verschränkte Werte in-place weich auf [-1, 1]. Länge: Vielfaches der Kanalzahl.</summary>
    public void Anwenden(Span<float> werte)
    {
        LaengenPruefung.Vielfaches(werte.Length, Kanaele, nameof(werte));
        if (werte.IsEmpty) return;
        int ersetzt = Endlichkeit.Ersetzen(werte);
        if (ersetzt != 0) _ersetzt += ersetzt;
        fixed (float* p = werte)
        fixed (float* m = _speicher)
        {
            OpusNativ.SoftClip(p, werte.Length / Kanaele, Kanaele, m);
        }
    }

    public void Zuruecksetzen() => Array.Clear(_speicher);
}
