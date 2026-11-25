// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Schutz;
using Funk.Server.Betrieb;

namespace Funk.Server.Sprachpfad;

/// <summary>
/// Vorfilter je Präfix für Datagramme von fremden Quellen (5.9 Schritt 4): 400 Datagramme/s, Stoß 200, höchstens
/// <see cref="Max"/> Präfixe. Ist die Tabelle voll (etwa durch gefälschte Quelladressen mit einer bekannten Sitzungs-ID),
/// gehen neue Präfixe ohne Vorfilter weiter: Ablehnen hieße, dass niemand mehr eine Erstbindung oder ein NAT-Rebinding
/// schafft. Die Budgets je Sitzung und Quelladresse (60/s, zusammen 200/s) begrenzen die Arbeit weiterhin. Gehört dem
/// Relay-Thread; allokiert nur für ein neues Präfix.
/// </summary>
internal sealed class Vorfilter(int max = Vorfilter.Max)
{
    public const int Max = 65_536;

    /// <summary>Ein Präfix ohne Datagramm seit so langer Zeit fällt bei der Pflege heraus.</summary>
    public static readonly long RuheTakte = Zeit.TakteAusMs(10_000);

    private readonly Dictionary<Praefix, (TokenEimer Eimer, long Zuletzt)> _toepfe = [];

    public int Anzahl => _toepfe.Count;

    /// <summary>So oft kam ein neues Präfix bei voller Tabelle ohne Vorfilter durch (Messwert).</summary>
    public long Uebergelaufen { get; private set; }

    /// <summary><c>true</c>: das Datagramm darf weiter zu den Budgets der Sitzung.</summary>
    public bool Nehmen(Praefix p, long takte)
    {
        if (!_toepfe.TryGetValue(p, out var v))
        {
            if (_toepfe.Count >= max)
            {
                Uebergelaufen++;
                return true;
            }
            v = (new TokenEimer(200, 400, takte), takte);
        }
        var ok = v.Eimer.Nehmen(takte);
        _toepfe[p] = (v.Eimer, takte);
        return ok;
    }

    /// <summary>Entfernt ruhende Präfixe, ohne eine Kopie der Tabelle anzulegen (etwa einmal je Sekunde aufrufen).</summary>
    public void Pflegen(long takte)
    {
        // Entfernen beim Aufzählen ist bei Dictionary seit .NET Core 3.0 erlaubt.
        foreach (var (p, v) in _toepfe)
            if (takte - v.Zuletzt > RuheTakte) _toepfe.Remove(p);
    }
}
