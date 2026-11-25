// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Übergabe von Sprachpaketen vom Netz-Thread (genau ein Schreiber) an den Audio-Thread (genau ein
/// Leser), ohne Sperre und ohne Allokation. Alle Plätze werden im Konstruktor angelegt.
/// Der Schreiber veröffentlicht einen Platz erst nach dem Kopieren (Volatile.Write auf den Schreibzeiger),
/// der Leser gibt ihn erst nach der Verarbeitung frei.
/// </summary>
internal sealed class PaketRing
{
    private readonly byte[][] _daten;
    private readonly int[] _laenge;
    private readonly long[] _ankunftUs;
    private readonly ushort[] _phase;
    private readonly uint[] _frame;
    private readonly Sprachkennzeichen[] _kennzeichen;
    private readonly int _maske;
    private int _schreib;
    private int _lese;

    public PaketRing(int kapazitaet, int maxBytes)
    {
        if (kapazitaet < 2 || (kapazitaet & (kapazitaet - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(kapazitaet), "Muss eine Zweierpotenz und mindestens 2 sein.");
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        _daten = new byte[kapazitaet][];
        for (var i = 0; i < kapazitaet; i++) _daten[i] = new byte[maxBytes];
        _laenge = new int[kapazitaet];
        _ankunftUs = new long[kapazitaet];
        _phase = new ushort[kapazitaet];
        _frame = new uint[kapazitaet];
        _kennzeichen = new Sprachkennzeichen[kapazitaet];
        _maske = kapazitaet - 1;
        MaxBytes = maxBytes;
    }

    public int MaxBytes { get; }

    public int Kapazitaet => _maske + 1;

    /// <summary>Nur vom Schreiber. Falsch, wenn der Ring voll oder das Paket zu groß ist.</summary>
    public bool Schreiben(long ankunftUs, ushort phase, uint frame, Sprachkennzeichen kennzeichen, ReadOnlySpan<byte> daten)
    {
        if (daten.Length > MaxBytes) return false;
        var s = _schreib;
        if (s - Volatile.Read(ref _lese) >= Kapazitaet) return false;
        var i = s & _maske;
        daten.CopyTo(_daten[i]);
        _laenge[i] = daten.Length;
        _ankunftUs[i] = ankunftUs;
        _phase[i] = phase;
        _frame[i] = frame;
        _kennzeichen[i] = kennzeichen;
        Volatile.Write(ref _schreib, s + 1);
        return true;
    }

    /// <summary>Nur vom Leser: ältesten Platz ansehen, ohne ihn freizugeben.</summary>
    public bool Spaehen(out int index)
    {
        var l = _lese;
        if (l == Volatile.Read(ref _schreib))
        {
            index = -1;
            return false;
        }
        index = l & _maske;
        return true;
    }

    /// <summary>Nur vom Leser: den zuletzt angesehenen Platz freigeben.</summary>
    public void Freigeben() => Volatile.Write(ref _lese, _lese + 1);

    /// <summary>Nur vom Leser: alles verwerfen, was bisher veröffentlicht wurde.</summary>
    public void Leeren() => Volatile.Write(ref _lese, Volatile.Read(ref _schreib));

    public ReadOnlySpan<byte> Daten(int index) => _daten[index].AsSpan(0, _laenge[index]);

    public long AnkunftUs(int index) => _ankunftUs[index];

    public ushort Phase(int index) => _phase[index];

    public uint Frame(int index) => _frame[index];

    public Sprachkennzeichen Kennzeichen(int index) => _kennzeichen[index];
}
