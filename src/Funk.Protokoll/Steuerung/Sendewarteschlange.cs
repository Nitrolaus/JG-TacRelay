// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>
/// Die zwei Warteschlangen der Schreibschleife je Sitzung (4.2), ohne Netzwerk: Steuerrahmen gehen vor.
/// Tunnel-Sprache (0x8001) wird beim Einreihen mit der monotonen Uhr gestempelt; ist sie beim Entnehmen älter als
/// 60 ms, wird sie verworfen. Je Absender liegen höchstens 3 Tunnelrahmen an; ein vierter verdrängt den ältesten
/// dieses Absenders, nie den eines anderen. Verworfene Einträge gehen an <c>freigeben</c> (Pufferrückgabe).
/// Ohne Allokation im laufenden Betrieb, sobald die Felder ihre Größe erreicht haben. Nicht threadsicher.
/// </summary>
public sealed class Sendewarteschlange<T>
{
    /// <summary>Höchstalter eines Tunnelrahmens vor dem Schreiben.</summary>
    public const long TunnelHoechstalterMs = 60;

    /// <summary>Tunnelrahmen je Absender in der Warteschlange.</summary>
    public const int TunnelJeAbsenderMax = 3;

    private readonly Queue<T> _steuer = new();
    private readonly Dictionary<uint, int> _jeAbsender = [];
    private readonly Action<T>? _freigeben;
    private Eintrag[] _ring = new Eintrag[64];
    private int _kopf;
    private int _anzahl;

    public Sendewarteschlange(Action<T>? freigeben = null) => _freigeben = freigeben;

    /// <summary>Wartende Steuerrahmen.</summary>
    public int Steuerrahmen => _steuer.Count;

    /// <summary>Wartende Tunnelrahmen (ohne verdrängte).</summary>
    public int Tunnelrahmen { get; private set; }

    /// <summary>Seit Beginn verworfene Tunnelrahmen (zu alt oder verdrängt).</summary>
    public long Verworfen { get; private set; }

    /// <summary>Reiht einen Steuerrahmen ein.</summary>
    public void SteuerEinreihen(T rahmen) => _steuer.Enqueue(rahmen);

    /// <summary>Reiht einen Tunnel-Sprachrahmen von <paramref name="absender"/> ein, gestempelt mit <paramref name="jetztMs"/>.</summary>
    public void TunnelEinreihen(uint absender, T rahmen, long jetztMs)
    {
        _jeAbsender.TryGetValue(absender, out var n);
        if (n == TunnelJeAbsenderMax)
        {
            AeltestenVerdraengen(absender);
            n--;
        }
        _jeAbsender[absender] = n + 1;
        if (_anzahl == _ring.Length) Wachsen();
        _ring[(_kopf + _anzahl) % _ring.Length] = new Eintrag(absender, jetztMs, rahmen, true);
        _anzahl++;
        Tunnelrahmen++;
    }

    /// <summary>
    /// Nächster zu schreibender Rahmen: erst Steuerrahmen, dann Tunnelrahmen, die nicht älter als 60 ms sind.
    /// <c>false</c>, wenn nichts ansteht.
    /// </summary>
    public bool VersucheEntnehmen(long jetztMs, out T rahmen)
    {
        if (_steuer.TryDequeue(out var steuer))
        {
            rahmen = steuer;
            return true;
        }
        while (_anzahl > 0)
        {
            var e = _ring[_kopf];
            _ring[_kopf] = default;
            _kopf = (_kopf + 1) % _ring.Length;
            _anzahl--;
            if (!e.Aktiv) continue;
            Tunnelrahmen--;
            Abziehen(e.Absender);
            if (jetztMs - e.StempelMs > TunnelHoechstalterMs)
            {
                Verworfen++;
                _freigeben?.Invoke(e.Wert);
                continue;
            }
            rahmen = e.Wert;
            return true;
        }
        rahmen = default!;
        return false;
    }

    private void AeltestenVerdraengen(uint absender)
    {
        for (var i = 0; i < _anzahl; i++)
        {
            var j = (_kopf + i) % _ring.Length;
            if (!_ring[j].Aktiv || _ring[j].Absender != absender) continue;
            var wert = _ring[j].Wert;
            _ring[j] = default;
            Tunnelrahmen--;
            Verworfen++;
            _freigeben?.Invoke(wert);
            return;
        }
    }

    private void Abziehen(uint absender)
    {
        var n = _jeAbsender[absender] - 1;
        if (n == 0) _jeAbsender.Remove(absender);
        else _jeAbsender[absender] = n;
    }

    /// <summary>
    /// Ring voll: zuerst die Lücken verdrängter Einträge entfernen, erst dann vergrößern. Ohne das wüchse der Ring
    /// bei stehender Schreibschleife (Client liest nicht) mit jedem Rahmen, obwohl je Absender nur 3 aktiv sind.
    /// So bleibt er höchstens doppelt so groß wie die Zahl aktiver Einträge (3 je Absender).
    /// </summary>
    private void Wachsen()
    {
        var aktiv = 0;
        for (var i = 0; i < _anzahl; i++)
        {
            var e = _ring[(_kopf + i) % _ring.Length];
            if (e.Aktiv) _ring[(_kopf + aktiv++) % _ring.Length] = e;
        }
        for (var i = aktiv; i < _anzahl; i++) _ring[(_kopf + i) % _ring.Length] = default;
        _anzahl = aktiv;
        if (_anzahl * 2 <= _ring.Length) return;

        var neu = new Eintrag[_ring.Length * 2];
        for (var i = 0; i < _anzahl; i++) neu[i] = _ring[(_kopf + i) % _ring.Length];
        _ring = neu;
        _kopf = 0;
    }

    private readonly record struct Eintrag(uint Absender, long StempelMs, T Wert, bool Aktiv);
}
