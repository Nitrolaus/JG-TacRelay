// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>Eine offene eigene Anfrage. <see cref="Kontext"/> trägt etwa die angefragte Epoche bei VoiceKeys.</summary>
public readonly record struct OffeneAnfrage(uint Nr, Nachrichtentyp Typ, long FristMs, byte Kontext);

/// <summary>
/// Eigene Anfragen einer Seite (4.2): vergibt Anfrage-Nummern streng aufsteigend (Client 1..0x7FFFFFFF, Server
/// ab 0x80000001), hält höchstens 32 offen, ordnet Antworten zu und meldet abgelaufene Fristen.
/// Nicht threadsicher.
/// </summary>
public sealed class EigeneAnfragen
{
    private readonly OffeneAnfrage[] _offen = new OffeneAnfrage[Steuerkonstanten.MaxOffeneAnfragen];
    private readonly uint _hoechste;
    private uint _letzte;
    private int _anzahl;

    public EigeneAnfragen(Seite eigeneSeite)
    {
        _letzte = eigeneSeite == Seite.Server ? Rahmen.ServerAnfrageBit : 0;
        _hoechste = eigeneSeite == Seite.Server ? uint.MaxValue : Rahmen.ClientAnfrageMax;
    }

    /// <summary>Zahl offener Anfragen.</summary>
    public int Offen => _anzahl;

    /// <summary>
    /// Vergibt eine Nummer und trägt die Anfrage ein. <c>false</c>, wenn schon 32 offen sind oder der
    /// Nummernraum erschöpft ist (dann neu verbinden). <paramref name="fristMs"/> 0 heißt ohne Frist.
    /// </summary>
    public bool VersucheStellen(Nachrichtentyp typ, long jetztMs, long fristMs, byte kontext, out uint nr)
    {
        nr = 0;
        if (_anzahl == _offen.Length || _letzte == _hoechste) return false;
        nr = ++_letzte;
        _offen[_anzahl++] = new OffeneAnfrage(nr, typ, fristMs == 0 ? long.MaxValue : jetztMs + fristMs, kontext);
        return true;
    }

    /// <summary>
    /// Ordnet eine Antwort zu: Die Nummer muss offen sein, der Typ gleich dem der Anfrage oder <c>Fehler</c>.
    /// Sonst <c>false</c> (<c>PROTOKOLL</c>). Eine zugeordnete Anfrage ist danach erledigt.
    /// </summary>
    public bool VersucheAufloesen(uint nr, ushort antwortTyp, out OffeneAnfrage anfrage)
    {
        for (var i = 0; i < _anzahl; i++)
        {
            if (_offen[i].Nr != nr) continue;
            anfrage = _offen[i];
            if (antwortTyp != (ushort)anfrage.Typ && antwortTyp != (ushort)Nachrichtentyp.Fehler) return false;
            Entfernen(i);
            return true;
        }
        anfrage = default;
        return false;
    }

    /// <summary>Nimmt die erste abgelaufene Anfrage heraus, falls es eine gibt.</summary>
    public bool NaechsteAbgelaufene(long jetztMs, out OffeneAnfrage anfrage)
    {
        for (var i = 0; i < _anzahl; i++)
        {
            if (_offen[i].FristMs > jetztMs) continue;
            anfrage = _offen[i];
            Entfernen(i);
            return true;
        }
        anfrage = default;
        return false;
    }

    private void Entfernen(int i)
    {
        _anzahl--;
        Array.Copy(_offen, i + 1, _offen, i, _anzahl - i);
        _offen[_anzahl] = default;
    }
}

/// <summary>
/// Anfragen der Gegenseite (4.2): prüft Nummernbereich und strenge Aufsteigung und begrenzt die Zahl offener
/// Anfragen auf 32. Ein Verstoß ergibt <c>PROTOKOLL</c>. Nicht threadsicher.
/// </summary>
public sealed class FremdeAnfragen
{
    private readonly uint[] _offen = new uint[Steuerkonstanten.MaxOffeneAnfragen];
    private readonly uint _kleinste;
    private readonly uint _hoechste;
    private uint _letzte;
    private int _anzahl;

    public FremdeAnfragen(Seite gegenseite)
    {
        _kleinste = gegenseite == Seite.Server ? Rahmen.ServerAnfrageBit + 1 : 1;
        _hoechste = gegenseite == Seite.Server ? uint.MaxValue : Rahmen.ClientAnfrageMax;
        _letzte = _kleinste - 1;
    }

    /// <summary>Zahl offener Anfragen der Gegenseite.</summary>
    public int Offen => _anzahl;

    /// <summary>Prüft und verbucht eine neue Anfrage. <c>null</c> heißt angenommen.</summary>
    public Fehlercode? Annehmen(uint nr)
    {
        if (nr < _kleinste || nr > _hoechste || nr <= _letzte) return Fehlercode.Protokoll;
        if (_anzahl == _offen.Length) return Fehlercode.Protokoll;
        _letzte = nr;
        _offen[_anzahl++] = nr;
        return null;
    }

    /// <summary>Trägt eine gesendete Antwort aus. <c>false</c>, wenn die Nummer nicht offen war.</summary>
    public bool Beantwortet(uint nr)
    {
        for (var i = 0; i < _anzahl; i++)
        {
            if (_offen[i] != nr) continue;
            _anzahl--;
            Array.Copy(_offen, i + 1, _offen, i, _anzahl - i);
            return true;
        }
        return false;
    }
}

/// <summary>Sonstige Konstanten des Steuerkanals.</summary>
public static class Steuerkonstanten
{
    /// <summary>Höchstens so viele offene Anfragen je Richtung (4.2).</summary>
    public const int MaxOffeneAnfragen = 32;
}
