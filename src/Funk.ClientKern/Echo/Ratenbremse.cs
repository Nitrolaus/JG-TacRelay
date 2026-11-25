// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;

namespace Funk.ClientKern.Echo;

/// <summary>
/// Zwei Token-Eimer (Pakete und Opus-Bytes) wie die Grenzen des Servers je Sitzung (6.3), aber als Bremse: Statt zu
/// verwerfen nennt <see cref="Nehmen"/> die Wartezeit, bis das Paket gehen darf. Zeit in Stopwatch-Takten von außen.
/// Nicht threadsicher.
/// </summary>
public sealed class Ratenbremse
{
    private readonly EchoOptionen _o;
    private double _pakete;
    private double _bytes;
    private long _letzter;

    public Ratenbremse(EchoOptionen optionen, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(optionen);
        _o = optionen;
        _pakete = optionen.PaketeStoss;
        _bytes = optionen.OpusBytesStoss;
        _letzter = jetzt;
    }

    /// <summary>0: genommen, das Paket darf sofort gehen. Sonst die Takte bis zum nächsten Versuch; nichts wird genommen.</summary>
    public long Nehmen(long jetzt, int opusBytes, int pakete = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(opusBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pakete);
        if (opusBytes > _o.OpusBytesStoss || pakete > _o.PaketeStoss) throw new ArgumentOutOfRangeException(nameof(opusBytes), "Größer als der Stoß.");
        if (jetzt > _letzter)
        {
            var sekunden = (jetzt - _letzter) / (double)Stopwatch.Frequency;
            _pakete = Math.Min(_o.PaketeStoss, _pakete + sekunden * _o.PaketeJeSekunde);
            _bytes = Math.Min(_o.OpusBytesStoss, _bytes + sekunden * _o.OpusBytesJeSekunde);
            _letzter = jetzt;
        }
        if (_pakete >= pakete && _bytes >= opusBytes)
        {
            _pakete -= pakete;
            _bytes -= opusBytes;
            return 0;
        }
        var fehlt = Math.Max(Math.Max(0, pakete - _pakete) / _o.PaketeJeSekunde, Math.Max(0, opusBytes - _bytes) / _o.OpusBytesJeSekunde);
        return Math.Max(1, (long)Math.Ceiling(fehlt * Stopwatch.Frequency));
    }
}
