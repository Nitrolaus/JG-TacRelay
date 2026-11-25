// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Identitaet;

/// <summary>
/// Folge einer Entscheidung, die der Server ausführt (Nachricht, Trennung, Bot-Meldung). Das Modul selbst hat
/// kein Netzwerk; es beschreibt nur, was geschehen muss.
/// </summary>
public abstract record Wirkung;

/// <summary>Warum Sitzungen enden.</summary>
public enum Trenngrund
{
    /// <summary><c>Fehler{KONTO_GESPERRT}</c>, Trennung, UDP-Schlüssel löschen (3.6).</summary>
    KontoGesperrt,

    /// <summary>Konto gelöscht (DSGVO oder Frist).</summary>
    KontoGeloescht,

    /// <summary>
    /// <c>GeraetStatus{ABGELEHNT}</c>, dann <c>Bye{ENTFERNT}</c> (3.5). Seit 0.7.0 ist der Antrag danach gelöscht; ohne Bye
    /// verbände sich der Client gleich wieder und beantragte von selbst neu.
    /// </summary>
    GeraetAbgelehnt,

    /// <summary><c>Bye{ENTFERNT}</c>.</summary>
    GeraetEntfernt,

    /// <summary><c>Bye{ENTZOGEN}</c>.</summary>
    GeraetEntzogen,

    /// <summary>An ein anderes Konto gehängt (Konsole): Trennung ohne Abschied, der Client verbindet sich gleich neu.</summary>
    Umgehaengt,
}

/// <summary>Beendet sofort alle Verbindungen eines Geräts oder, bei <c>GeraetId = null</c>, eines ganzen Kontos.</summary>
public sealed record SitzungenBeenden(uint KontoId, uint? GeraetId, Trenngrund Grund) : Wirkung;

/// <summary>
/// <c>GeraetStatus</c>-Push (4.5) an Sitzungen mit <c>GERAETE_FREIGEBEN</c>, an Sitzungen desselben Kontos und an die
/// wartende Verbindung dieses Geräts. Die übrigen Felder liest der Server aus dem Stand oder aus <see cref="Vorher"/>.
/// </summary>
public sealed record GeraetStatusGeaendert(uint GeraetId, uint KontoId, GeraetStatusMeldung Status, uint? Durch, string? Grund) : Wirkung
{
    /// <summary>
    /// Der Stand, in dem das Gerät noch steht, wenn es danach fort ist (seit 0.7.0: ABGELEHNT löscht den Antrag); sonst
    /// <c>null</c>, und es gilt der aktuelle Stand.
    /// </summary>
    public Kontenstand? Vorher { get; init; }
}

/// <summary>Ein Gerät ist aus dem Stand verschwunden (Frist abgelaufen oder Konto privilegiert geworden).</summary>
public sealed record GeraetGeloescht(uint GeraetId, uint KontoId) : Wirkung;

/// <summary>Konto gelöscht: Pegel, Ablage und Plätze des Kontos gehören mit gelöscht (3.3, 3.6).</summary>
public sealed record KontoGeloescht(uint KontoId) : Wirkung;

/// <summary><c>Meldung</c>-Push (0x4002) mit Art nach 4.5, etwa <c>GERAET_ANTRAG</c>, <c>RECHTE_GEAENDERT</c>, <c>ABGLEICH</c>.</summary>
public sealed record Meldung(string Art, uint? KontoId, uint? GeraetId) : Wirkung;

/// <summary>Ziel einer Bot-Meldung (3.8).</summary>
public enum BotZiel
{
    AdminKanal,
    Direktnachricht,
}

/// <summary><c>Meldung{ziel, discordId?, art, text}</c> an den Bot-Dienst (3.8).</summary>
public sealed record BotNachricht(BotZiel Ziel, string? DiscordId, string Art, string Text) : Wirkung;

/// <summary>Alarm für den Betrieb (Grenzen aus 3.4 und 3.8).</summary>
public sealed record Alarm(string Art) : Wirkung;

/// <summary>Eine offene Rückfrage <c>VerknuepfungBestaetigen</c> ist ohne Antwort abgelaufen (Bot: <c>FRIST</c>).</summary>
public sealed record VerknuepfungVerfallen(long Verbindung) : Wirkung;

/// <summary>Ergebnis einer Aktion. Bei einem Fehler bleibt der Stand unverändert.</summary>
public record Ergebnis(Fehlercode Fehler, IReadOnlyList<Wirkung> Wirkungen)
{
    public bool Ok => Fehler == Fehlercode.Keiner;

    internal static Ergebnis Mit(Fehlercode fehler) => new(fehler, []);
}

/// <summary>Einstufung nach Hello (4.6 WARTE_HELLO).</summary>
public enum EinstufungsArt
{
    /// <summary>Unbekannt oder ENTFERNT: weiter nach REGISTRIERUNG.</summary>
    Unbekannt,

    /// <summary>WARTET: weiter nach WARTERAUM.</summary>
    Wartet,

    /// <summary>FREIGEGEBEN und Konto AKTIV: weiter nach AKTIV_SYNC.</summary>
    Freigegeben,

    /// <summary>ABGELEHNT oder ENTZOGEN: <c>GERAET_GESPERRT</c>, Ende.</summary>
    GeraetGesperrt,

    /// <summary>Konto GESPERRT: <c>KONTO_GESPERRT</c>, Ende.</summary>
    KontoGesperrt,
}

/// <summary>Einstufung samt Kennungen für <c>Welcome</c>.</summary>
public sealed record Einstufung(EinstufungsArt Art, uint KontoId, uint GeraetId, IReadOnlyList<Wirkung> Wirkungen)
    : Ergebnis(Fehlercode.Keiner, Wirkungen);

/// <summary>Inhalt der Rückfrage <c>VerknuepfungBestaetigen{konto: {name, benutzername, neu}}</c> (3.4 Schritt 5).</summary>
public sealed record Verknuepfungsanfrage(
    Fehlercode Fehler, string? KontoName, string? Benutzername, bool Neu, DateTimeOffset Frist)
    : Ergebnis(Fehler, []);

/// <summary>Ergebnis der bestätigten Verknüpfung; für den Bot <c>geraet: {name, anzeige}</c>.</summary>
public sealed record Verknuepfungsergebnis(
    Fehlercode Fehler, uint KontoId, uint GeraetId, string? GeraetName, string? Anzeige, IReadOnlyList<Wirkung> Wirkungen)
    : Ergebnis(Fehler, Wirkungen);

/// <summary>Antwort auf <c>GeraetEntfernen</c>: ENTFERNT, ENTZOGEN oder BEANTRAGT (4.5).</summary>
public sealed record Entfernungsergebnis(Fehlercode Fehler, GeraetStatusMeldung? Status, IReadOnlyList<Wirkung> Wirkungen)
    : Ergebnis(Fehler, Wirkungen);

/// <summary>Ergebnis mit neuer Konto-ID (Admin-Werkzeug <c>Konto anlegen</c>).</summary>
public sealed record Anlageergebnis(Fehlercode Fehler, uint KontoId, IReadOnlyList<Wirkung> Wirkungen)
    : Ergebnis(Fehler, Wirkungen);
