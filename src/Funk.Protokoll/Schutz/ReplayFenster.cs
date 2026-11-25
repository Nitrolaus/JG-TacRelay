// JG TacRelay - Maintainer: Sarevoc
using System.Numerics;

namespace Funk.Protokoll.Schutz;

/// <summary>
/// Gleitendes Replay-Fenster (5.9, Vektor 8.7) nach RFC 6479: Bitmaske als Ringpuffer aus 64-Bit-Blöcken.
/// Angenommen wird ein Zähler über dem Höchstwert, oder einer mit <c>Höchstwert - Zähler &lt; Breite</c>, der
/// noch nicht gesehen wurde. Die Maske hat mindestens Breite + 64 Bit, damit die volle Breite W nutzbar ist.
/// Ablauf je Paket: <see cref="IstNeu"/> als Vorfilter (Schritt 6), dann den AEAD-Tag prüfen, erst danach
/// <see cref="Annehmen(ulong)"/> (Schritt 8, maßgeblich). So verschiebt ein gefälschtes Paket das Fenster nie.
/// Threadsicher: <see cref="Annehmen(ulong)"/> prüft und setzt unter einer Sperre, von zwei gleichen Kopien
/// gewinnt genau eine (P-05).
/// </summary>
public sealed class ReplayFenster
{
    private const int BlockBits = 64;
    private readonly Lock _sperre = new();
    private readonly ulong[] _bloecke;
    private readonly ulong _blockMaske;
    private ulong _hoechster;
    private bool _leer = true;

    /// <param name="breite">Fensterbreite W; Voreinstellung 2048 nach 5.9.</param>
    public ReplayFenster(int breite = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(breite);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(breite, 1 << 20);
        Breite = (ulong)breite;
        var bits = Math.Max(2 * BlockBits, (int)BitOperations.RoundUpToPowerOf2((uint)(breite + BlockBits)));
        _bloecke = new ulong[bits / BlockBits];
        _blockMaske = (ulong)_bloecke.Length - 1;
    }

    /// <summary>So weit darf ein Paket unter dem höchsten angenommenen Zähler liegen (ausschließlich).</summary>
    public ulong Breite { get; }

    /// <summary>Höchster angenommener Zähler; 0, solange nichts angenommen wurde.</summary>
    public ulong Hoechster
    {
        get { lock (_sperre) return _hoechster; }
    }

    /// <summary>Vorprüfung, nur lesend: ob der Zähler neu und nicht zu alt ist.</summary>
    public bool IstNeu(ulong zaehler)
    {
        lock (_sperre) return IstNeuGesperrt(zaehler);
    }

    /// <summary>Prüft und setzt atomar. Nur für authentisierte Pakete aufrufen. false = doppelt oder zu alt.</summary>
    public bool Annehmen(ulong zaehler) => Annehmen(zaehler, out _);

    /// <inheritdoc cref="Annehmen(ulong)"/>
    /// <param name="zaehler">Zähler aus dem Kopf.</param>
    /// <param name="neuerHoechstwert">Ob das Paket einen neuen Höchstwert brachte (auslösend für 5.10).</param>
    public bool Annehmen(ulong zaehler, out bool neuerHoechstwert)
    {
        lock (_sperre)
        {
            neuerHoechstwert = false;
            if (!IstNeuGesperrt(zaehler)) return false;

            if (_leer)
            {
                Array.Clear(_bloecke);
                _hoechster = zaehler;
                _leer = false;
                neuerHoechstwert = true;
            }
            else if (zaehler > _hoechster)
            {
                var alterBlock = _hoechster / BlockBits;
                var abstand = zaehler / BlockBits - alterBlock;
                if (abstand >= (ulong)_bloecke.Length)
                {
                    Array.Clear(_bloecke);
                }
                else
                {
                    for (ulong i = 1; i <= abstand; i++)
                        _bloecke[(int)((alterBlock + i) & _blockMaske)] = 0;
                }
                _hoechster = zaehler;
                neuerHoechstwert = true;
            }

            _bloecke[BlockIndex(zaehler)] |= Maske(zaehler);
            return true;
        }
    }

    private bool IstNeuGesperrt(ulong zaehler)
    {
        if (_leer || zaehler > _hoechster) return true;
        if (_hoechster - zaehler >= Breite) return false;
        return (_bloecke[BlockIndex(zaehler)] & Maske(zaehler)) == 0;
    }

    private int BlockIndex(ulong zaehler) => (int)(zaehler / BlockBits & _blockMaske);

    private static ulong Maske(ulong zaehler) => 1UL << (int)(zaehler % BlockBits);
}
