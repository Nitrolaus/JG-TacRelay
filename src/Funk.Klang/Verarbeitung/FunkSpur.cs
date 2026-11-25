// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Was der eigene Mischer an Funk spielt, je 10-ms-Block (Klangstufe C2a): Pegel der Funk-Wiedergabe (ohne Klick und Hörprobe, mit allen
/// Lautstärken), ob ein Funker hörbar zu spielen begann, und der lauteste Funker. Schreiber ist der Wiedergabe-Thread (<see cref="Mischer"/>),
/// Leser der Aufnahme-Thread (der Wächter der eigenen Seite); ohne Sperre und ohne Allokation. Voll: Der älteste Block geht verloren (der Leser
/// holt nur auf, die Echoreferenz des Wächters ist ein Pegel, keine Wellenform).
/// </summary>
internal sealed class FunkSpur
{
    private const int Plaetze = 64;
    private readonly float[] _db = new float[Plaetze];
    private readonly bool[] _anfang = new bool[Plaetze];
    private readonly int[] _platz = new int[Plaetze];
    private long _geschrieben;
    private long _gelesen;

    /// <summary>Nur der Wiedergabe-Thread.</summary>
    public void Schreiben(float db, bool anfang, int platz)
    {
        var s = _geschrieben;
        var i = (int)(s & (Plaetze - 1));
        _db[i] = db;
        _anfang[i] = anfang;
        _platz[i] = platz;
        Volatile.Write(ref _geschrieben, s + 1);
    }

    /// <summary>
    /// Nur der Aufnahme-Thread: alle neuen Blöcke zusammengefasst (lautester Pegel, irgendein Anfang, Platz des lautesten). Falsch, wenn keiner
    /// kam; dann gilt für diesen Aufnahmerahmen der vorige Stand.
    /// </summary>
    public bool Lesen(out float db, out bool anfang, out int platz)
    {
        db = float.NegativeInfinity;
        anfang = false;
        platz = -1;
        var bis = Volatile.Read(ref _geschrieben);
        var von = Math.Max(_gelesen, bis - (Plaetze / 2));
        if (von >= bis) return false;
        for (var s = von; s < bis; s++)
        {
            var i = (int)(s & (Plaetze - 1));
            if (_db[i] > db)
            {
                db = _db[i];
                platz = _platz[i];
            }
            anfang |= _anfang[i];
        }
        _gelesen = bis;
        return true;
    }
}
