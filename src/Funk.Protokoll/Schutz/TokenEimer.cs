// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;

namespace Funk.Protokoll.Schutz;

/// <summary>
/// Token-Eimer als Ratenbremse: füllt sich mit <c>proSekunde</c> bis <c>kapazitaet</c> auf,
/// jede Annahme kostet Marken. Die Zeit kommt als Stopwatch-Zeitstempel von außen, damit Tests
/// deterministisch sind und im Heißpfad nichts allokiert wird. Nicht threadsicher.
/// </summary>
public sealed class TokenEimer
{
    private readonly double _kapazitaet;
    private readonly double _proTick;
    private double _stand;
    private long _letzter;

    public TokenEimer(double kapazitaet, double proSekunde, long jetzt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kapazitaet);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(proSekunde);
        _kapazitaet = kapazitaet;
        _proTick = proSekunde / Stopwatch.Frequency;
        _stand = kapazitaet;
        _letzter = jetzt;
    }

    public double Stand => _stand;

    public bool Nehmen(long jetzt, double menge = 1)
    {
        if (jetzt > _letzter)
        {
            _stand = Math.Min(_kapazitaet, _stand + (jetzt - _letzter) * _proTick);
            _letzter = jetzt;
        }
        if (_stand < menge) return false;
        _stand -= menge;
        return true;
    }
}
