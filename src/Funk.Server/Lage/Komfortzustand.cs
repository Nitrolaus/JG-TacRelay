// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Diagnostics;
using Funk.Protokoll.Schutz;

namespace Funk.Server.Lage;

/// <summary>
/// Komfortzustand (6.2): die zuletzt gemeldete Sendeauswahl je Konto, ohne Austausch des Schnappschusses.
/// Setzt der Zustandsschreiber bei einer Umstellung eine neue Sendeauswahl, verwirft er den Eintrag hier;
/// dann gilt wieder die aus dem Schnappschuss. Gehört dem Thread des Zustandsschreibers.
/// </summary>
public sealed class Komfortzustand
{
    private readonly Dictionary<uint, Sendeauswahl> _auswahl = [];

    public Sendeauswahl AuswahlVon(Schnappschuss z, uint kontoId)
    {
        ArgumentNullException.ThrowIfNull(z);
        if (_auswahl.TryGetValue(kontoId, out var a)) return a;
        return z.Konten.TryGetValue(kontoId, out var k) ? k.Sendeauswahl : Sendeauswahl.Leer;
    }

    /// <summary><c>Praesenz.sendetIn</c> (4.5): Netz-IDs der Sendeauswahl, Global zuerst, dann nach Feld.</summary>
    public ImmutableArray<ushort> SendetIn(Schnappschuss z, uint kontoId)
    {
        ArgumentNullException.ThrowIfNull(z);
        if (!z.Konten.TryGetValue(kontoId, out var k)) return [];
        var auswahl = AuswahlVon(z, kontoId);
        var netze = ImmutableArray.CreateBuilder<ushort>();
        if (auswahl.Global) netze.Add(Netz.GlobalId);
        foreach (var feld in auswahl.Felder)
            if (k.FeldNummer(feld) is { } m) netze.Add(m.NetzId);
        return netze.ToImmutable();
    }

    internal void Setzen(uint kontoId, Sendeauswahl auswahl) => _auswahl[kontoId] = auswahl;

    /// <summary>Ob das Konto eine eigene Auswahl gemeldet hat, die noch gilt.</summary>
    internal bool Hat(uint kontoId, out Sendeauswahl auswahl) => _auswahl.TryGetValue(kontoId, out auswahl!);

    internal void Verwerfen(uint kontoId) => _auswahl.Remove(kontoId);
}

/// <summary>Ergebnis von <see cref="Zusammenfasser{T}.Einreichen"/>.</summary>
public enum Einreichung : byte
{
    /// <summary>Sofort anwenden.</summary>
    Sofort = 0,

    /// <summary>Wartet auf den Topf; wird über <see cref="Zusammenfasser{T}.Faellig"/> angewendet.</summary>
    Wartet = 1,
}

/// <summary>
/// Zusammenfassen von Zustandsmeldungen je Sitzung und Art (4.8): Topf 4/s, Burst 8, ein wartender Platz.
/// Eine neuere Anfrage ersetzt die wartende, die ersetzte bekommt UEBERHOLT; die wartende gilt, sobald der
/// Topf es erlaubt. Es gilt also immer der letzte Stand, und Überschuss ist kein Verstoß. Je Sitzung und Art eine
/// Instanz; nicht threadsicher, gehört dem Thread, der die Nachrichten dieser Sitzung verarbeitet.
/// </summary>
public sealed class Zusammenfasser<T>
{
    private readonly TokenEimer _eimer;
    private readonly long _basis;
    private T? _wartend;

    public Zusammenfasser(long jetzt, int proSekunde = 4, int burst = 8)
    {
        _basis = jetzt;
        _eimer = new TokenEimer(burst, proSekunde, 0);
    }

    public bool Wartet { get; private set; }

    /// <summary>Reicht eine Anfrage ein. <paramref name="ueberholt"/> ist die ersetzte wartende Anfrage, falls es eine gab.</summary>
    public Einreichung Einreichen(T anfrage, long jetzt, out T? ueberholt, out bool hatUeberholt)
    {
        hatUeberholt = Wartet;
        ueberholt = Wartet ? _wartend : default;
        if (_eimer.Nehmen(Takte(jetzt)))
        {
            Wartet = false;
            _wartend = default;
            return Einreichung.Sofort;
        }
        Wartet = true;
        _wartend = anfrage;
        return Einreichung.Wartet;
    }

    /// <summary>Die wartende Anfrage, sobald der Topf es erlaubt.</summary>
    public bool Faellig(long jetzt, out T? anfrage)
    {
        anfrage = default;
        if (!Wartet || !_eimer.Nehmen(Takte(jetzt))) return false;
        anfrage = _wartend;
        Wartet = false;
        _wartend = default;
        return true;
    }

    private long Takte(long jetzt)
    {
        var ms = Math.Max(0, jetzt - _basis);
        return ms / 1000 * Stopwatch.Frequency + ms % 1000 * Stopwatch.Frequency / 1000;
    }
}
