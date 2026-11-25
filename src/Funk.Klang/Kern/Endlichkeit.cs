// JG TacRelay - Maintainer: Sarevoc
using System.Numerics;
using System.Runtime.InteropServices;

namespace Funk.Klang.Kern;

/// <summary>
/// Schutz der nativen Bibliotheken vor NaN und Unendlich. Gemessen (Gegenprüfung): Ein einziges NaN
/// vergiftet den Zustand von RNNoise (Hochpass und GRU) und des APM (AEC3, Aufnahme wie Wiedergabe)
/// dauerhaft; danach kommt nur noch NaN heraus, bis die Instanz neu angelegt wird. Bei RNNoise
/// genügt dafür auch Unendlich oder ein riesiger endlicher Wert. Opus prüft selbst (der Rahmen wird
/// still), der Resampler erholt sich nach der Filterlänge.
/// </summary>
/// <remarks>Keine Methode allokiert oder sperrt; der Normalfall ist ein Vektorvergleich je Block.</remarks>
internal static class Endlichkeit
{
    /// <summary>Wahr, wenn kein Wert NaN oder ±Unendlich ist.</summary>
    internal static bool AlleEndlich(ReadOnlySpan<float> werte)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && werte.Length >= Vector<float>.Count)
        {
            // x - x ist 0 für jeden endlichen Wert und NaN für NaN und ±Unendlich.
            var bloecke = MemoryMarshal.Cast<float, Vector<float>>(werte);
            foreach (var v in bloecke)
                if (!Vector.EqualsAll(v - v, Vector<float>.Zero)) return false;
            i = bloecke.Length * Vector<float>.Count;
        }
        for (; i < werte.Length; i++)
            if (!float.IsFinite(werte[i])) return false;
        return true;
    }

    /// <summary>Kleinster normaler float-Wert (2^-126), etwa -758 dBFS.</summary>
    internal const float KleinsteNormale = 1.17549435e-38f;

    /// <summary>
    /// Setzt subnormale Werte in-place auf 0. RNNoise und APM lassen ihre rekursiven Filter in
    /// digitaler Stille bis in den subnormalen Bereich abklingen (gemessen: tausende Werte je
    /// Sekunde); in verwalteter Rechnung dahinter kostet jeder davon ein Vielfaches. Hörbar ist dabei
    /// nichts verändert.
    /// </summary>
    internal static void SubnormaleNullen(Span<float> werte)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && werte.Length >= Vector<float>.Count)
        {
            var grenze = new Vector<float>(KleinsteNormale);
            var bloecke = MemoryMarshal.Cast<float, Vector<float>>(werte);
            for (int k = 0; k < bloecke.Length; k++)
                bloecke[k] = Vector.ConditionalSelect(Vector.LessThan(Vector.Abs(bloecke[k]), grenze), Vector<float>.Zero, bloecke[k]);
            i = bloecke.Length * Vector<float>.Count;
        }
        for (; i < werte.Length; i++)
            if (float.IsSubnormal(werte[i])) werte[i] = 0f;
    }

    /// <summary>Ersetzt NaN und ±Unendlich in-place durch 0. Rückgabe: Anzahl ersetzter Werte.</summary>
    internal static int Ersetzen(Span<float> werte)
    {
        if (AlleEndlich(werte)) return 0;
        int ersetzt = 0;
        for (int i = 0; i < werte.Length; i++)
        {
            if (!float.IsFinite(werte[i]))
            {
                werte[i] = 0f;
                ersetzt++;
            }
        }
        return ersetzt;
    }
}
