// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>
/// Übergabe von 10-ms-Rahmen (480 Werte, 48 kHz mono) von genau einem Schreiber an genau einen Leser, ohne Sperre und
/// ohne Allokation. Dient der Echoreferenz: Der Loopback-Thread schreibt, was der Lautsprecher spielt, der
/// Aufnahme-Thread liest es vor jedem Mikrofonrahmen. Voll: Der Schreiber verwirft und zählt.
/// </summary>
internal sealed class RahmenRing
{
    private const int Rahmen = Klangformat.Samples10ms;

    private readonly float[] _werte;
    private readonly int _maske;
    private int _schreib;
    private int _lese;
    private long _verworfen;

    public RahmenRing(int kapazitaet)
    {
        if (kapazitaet < 2 || (kapazitaet & (kapazitaet - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(kapazitaet), "Muss eine Zweierpotenz und mindestens 2 sein.");
        _werte = new float[kapazitaet * Rahmen];
        _maske = kapazitaet - 1;
    }

    public int Kapazitaet => _maske + 1;

    /// <summary>Rahmen, die gerade bereitliegen (aus jedem Thread, Momentaufnahme).</summary>
    public int Fuellstand => Volatile.Read(ref _schreib) - Volatile.Read(ref _lese);

    /// <summary>
    /// Laufende Nummer des nächsten Rahmens, den der Schreiber ablegt (aus jedem Thread, Momentaufnahme). Ein abgelegter Rahmen
    /// behält seine Nummer, auch wenn der Leser ihn wegkürzt; was der Schreiber bei vollem Ring verwirft, bekommt keine. Die Nummer
    /// läuft nach 2^31 Rahmen (248 Tagen) über; Abstände rechnen ungeprüft und bleiben richtig.
    /// </summary>
    public int Geschrieben => Volatile.Read(ref _schreib);

    /// <summary>Nur der Leser: Nummer des Rahmens, den das nächste <see cref="Lesen"/> liefert.</summary>
    public int NaechsterLesen => _lese;

    /// <summary>Rahmen, die der Schreiber wegen eines vollen Rings verworfen hat.</summary>
    public long Verworfen => Interlocked.Read(ref _verworfen);

    /// <summary>Nur der Schreiber. Genau 480 Werte.</summary>
    public bool Schreiben(ReadOnlySpan<float> rahmen)
    {
        if (rahmen.Length != Rahmen) throw new ArgumentException("Rahmen muss 480 Werte haben.", nameof(rahmen));
        var s = _schreib;
        if (s - Volatile.Read(ref _lese) >= Kapazitaet)
        {
            Interlocked.Increment(ref _verworfen);
            return false;
        }
        rahmen.CopyTo(_werte.AsSpan((s & _maske) * Rahmen, Rahmen));
        Volatile.Write(ref _schreib, s + 1);
        return true;
    }

    /// <summary>Nur der Leser: ältesten Rahmen nach <paramref name="ziel"/> kopieren. Falsch, wenn keiner da ist.</summary>
    public bool Lesen(Span<float> ziel)
    {
        var l = _lese;
        if (l == Volatile.Read(ref _schreib)) return false;
        _werte.AsSpan((l & _maske) * Rahmen, Rahmen).CopyTo(ziel);
        Volatile.Write(ref _lese, l + 1);
        return true;
    }

    /// <summary>Nur der Leser: bis auf <paramref name="behalten"/> Rahmen alles Ältere verwerfen.</summary>
    public int Kuerzen(int behalten)
    {
        var l = _lese;
        var zuviel = Volatile.Read(ref _schreib) - l - behalten;
        if (zuviel <= 0) return 0;
        Volatile.Write(ref _lese, l + zuviel);
        return zuviel;
    }
}
