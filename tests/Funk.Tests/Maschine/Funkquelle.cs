// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Ein Funker im Mischer ohne Netz und Opus (Prüfstand der Klangstufe C2a): spielt ein fertiges Signal (48 kHz mono) im 10-ms-Takt und
/// meldet die Ereignisse wie ein Jitterpuffer: Begonnen im ersten Block einer Sprechphase, Aktiv solange sie läuft, Beendet in ihrem letzten
/// Block. Außerhalb der Phasen ist er still. Phasen in Samples, nicht überlappend, aufsteigend.
/// </summary>
internal sealed class Funkquelle(float[] signal, IReadOnlyList<(int Von, int Bis)> phasen) : IKlangquelle
{
    private const int R = 480;
    private int _pos;
    private int _phase;

    /// <summary>Ein Funker, der durchgehend sendet (eine Phase über das ganze Signal).</summary>
    public static Funkquelle Durchgehend(float[] signal) => new(signal, [(0, signal.Length)]);

    public int Bloecke => _pos / R;

    public QuellenEreignis Lesen(Span<float> ziel)
    {
        var e = QuellenEreignis.Keine;
        var von = _pos;
        var bis = _pos + R;
        _pos = bis;
        while (_phase < phasen.Count && phasen[_phase].Bis <= von) _phase++;
        if (_phase >= phasen.Count || phasen[_phase].Von >= bis)
        {
            ziel.Clear();
            return e;
        }
        var (pv, pb) = phasen[_phase];
        for (var i = 0; i < R; i++)
        {
            var s = von + i;
            ziel[i] = s >= pv && s < pb && s < signal.Length ? signal[s] : 0f;
        }
        e |= QuellenEreignis.Aktiv;
        if (pv >= von) e |= QuellenEreignis.Begonnen;
        if (pb <= bis) e |= QuellenEreignis.Beendet;
        return e;
    }
}
