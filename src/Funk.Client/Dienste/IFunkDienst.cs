// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Naht der Oberfläche zum Funkbetrieb. Im Betrieb steckt <see cref="FunkClientDienst"/> dahinter (Funk.ClientKern),
/// in Entwurf, Vorführung und Tests <see cref="FunkDienstAttrappe"/>. Die Anfragen liefern <c>null</c> bei Erfolg,
/// sonst einen Text für die Anzeige; sie werfen nicht.
/// </summary>
public interface IFunkDienst : IAsyncDisposable
{
    /// <summary>Aktueller Stand.</summary>
    FunkLage Lage { get; }

    /// <summary>Der Stand hat sich geändert; kommt aus einem beliebigen Thread.</summary>
    event Action? LageGeaendert;

    Task VerbindenAsync();

    Task TrennenAsync();

    Task<string?> HoerenSetzenAsync(ushort netzId, bool hoeren);

    Task<string?> SendeauswahlSetzenAsync(IReadOnlyList<byte> felder, bool mitGlobal);

    /// <summary>A15: Rolle und Schiff in einem Schritt.</summary>
    Task<string?> EinsteigenAsync(ushort rolleId, ushort? schiffId);

    /// <summary>A4: Pegel für einen Funker in allen Netzen, am Konto gespeichert.</summary>
    Task<string?> PegelSetzenAsync(uint kontoId, float db);

    /// <summary>Rollenkatalog lesen (N6, für alle): die Namen der Mitglieder- und Lieblingsrollen.</summary>
    Task<Dienstantwort<RollenkatalogAntwort>> RollenkatalogAsync();

    /// <summary>
    /// Rollenkatalog ergänzen (Führung und Einsatzleitung, Sarevoc am 30.09.2026, Antwort 4): der ganze Katalog mit den neuen
    /// Einträgen; vorhandene bleiben unverändert, sonst lehnt der Server ab.
    /// </summary>
    Task<string?> RollenkatalogErgaenzenAsync(IReadOnlyList<Rollenkatalogeintrag> eintraege, long version);

    /// <summary>Mitgliedskarte (N10, A48): was jemand angegeben hat, nach der Sichtbarkeitsregel des Servers.</summary>
    Task<Dienstantwort<MitgliedskarteAntwort>> MitgliedskarteAsync(uint kontoId);

    /// <summary>Eigene Lieblingsrollen (N8): bis zu 8 aus dem Katalog, eine als erste Wahl.</summary>
    Task<string?> LieblingsrollenSetzenAsync(IReadOnlyList<ushort> rollen, ushort? ersteWahl);

    /// <summary>Eigene Namen und Schalter (N2, F11): SC-Namen, Discord-Name, Zeichen zeigen, Angaben freigeben.</summary>
    Task<string?> ProfilSetzenAsync(ProfilSetzenAnfrage anfrage);

    // ---- Stufe 2: Einsteigen und Brett (RAIDLEITUNG-ENTWURF, M4 und M5) ----

    /// <summary>"Wie letztes Mal" (A21): der eigene frühere Platz samt Feldern und Sendeauswahl über <c>SatzAnwenden</c>.</summary>
    Task<string?> WieLetztesMalAsync();

    /// <summary>
    /// Verschieben (4.5, nur Einsatzleitung): eine oder mehrere Änderungen, alles oder nichts, die Kapazität am Endzustand
    /// (Tausch in einer Anfrage). Mit <paramref name="basisVersion"/> kommt VERALTET, wenn jemand die Betroffenen inzwischen
    /// umgestellt hat (<see cref="Dienstantwort{T}.Code"/>).
    /// </summary>
    Task<Dienstantwort<VerschiebenAntwort>> VerschiebenAsync(IReadOnlyList<Verschiebung> aenderungen, long? basisVersion = null);

    /// <summary>Stummschalten (4.5, Einsatzleitung): <paramref name="netzId"/> 0 heißt alle Netze, 0 Sekunden hebt auf.</summary>
    Task<string?> StummschaltenAsync(uint kontoId, ushort netzId, ushort sekunden);

    /// <summary>PlaetzeFreigeben (4.5, Einsatzleitung): die Plätze aller Konten ohne Sitzung frei; Antwort die Anzahl.</summary>
    Task<Dienstantwort<ushort>> PlaetzeFreigebenAsync();

    /// <summary>
    /// Selbstwahl an oder sperren (Verwaltung SELBSTWAHL, F9, Einsatzleitung ohne Admin-Sitzung): <paramref name="nachzuegler"/>
    /// true ist "Wechsel sperren", false "Ganz sperren".
    /// </summary>
    Task<string?> SelbstwahlSetzenAsync(bool an, bool nachzuegler = true);

    /// <summary>KontenVerzeichnis (N4, Einsatzleitung, höchstens 1 je 5 s): alle Konten, auch offline, für "Alle Mitglieder".</summary>
    Task<Dienstantwort<IReadOnlyList<VerzeichnisEintrag>>> KontenVerzeichnisAsync();

    /// <summary>Den eigenen Auftrag abgeben (Beauftragung ABGEBEN, 6.12 Regel 6), etwa mit "Abend beenden".</summary>
    Task<string?> AuftragAbgebenAsync();

    // ---- Ausbaustufe S2 (0.5.0, RAID-S2-ENTWURF): nur an Server ab 0.5.0 (FunkLage.ServerS2) ----

    /// <summary>"Heute als Funker" (0x003C, 6.15): nur Stufe ADMIN, nur für sich selbst, ohne Code. Antwort der Stand danach.</summary>
    Task<Dienstantwort<FunkermodusAntwort>> FunkermodusAsync(bool an);

    /// <summary>
    /// Phase schalten (0x003D, 6.14, ZUWEISEN). <paramref name="erwartet"/> ist die Phase, die der Client für die laufende hält;
    /// weicht sie ab, kommt VERALTET mit <see cref="Dienstantwort{T}.Details"/> <c>{nr, durch}</c>, und nichts ändert sich.
    /// </summary>
    Task<Dienstantwort<PhaseSchaltenAntwort>> PhaseSchaltenAsync(byte nr, byte erwartet);

    // ---- Stufe 3 (0.6.0, 6.16): Kanäle mit Ch.; RolleWaehlen nur an Server ab 0.6.0 (FunkLage.ServerKanaele) ----

    /// <summary>
    /// Den Ch. eines eigenen Felds wechseln (Kanal-ID im selben Kanalnetz, auch die Ruhe; 0x0035 FeldSetzen); <paramref name="netzId"/>
    /// <c>null</c> hieße leeren. Seit 0.8.1 (Q44/Q45) geht der Ch.-Wechsel auch im festen Feld, nur nicht im gesperrten; einen anderen
    /// Kanal ins Feld legen oder es leeren lehnt der Server ab.
    /// </summary>
    Task<string?> FeldSetzenAsync(byte feld, ushort? netzId);

    /// <summary>
    /// Eine Rolle wählen (0x0039 RolleWaehlen): legt ihre Felder an, der Platz bleibt. <paramref name="ch"/> sind Wünsche je
    /// Rollenfeld, etwa der zuletzt benutzte Ch. je Kanalnetz.
    /// </summary>
    Task<string?> RolleWaehlenAsync(ushort rolleId, IReadOnlyList<ChWunsch> ch);

    /// <summary>
    /// Eine Kanalsperre setzen oder aufheben (0x003A KanalSperre, 6.17, Einsatzleitung, nur an Server ab 0.6.0): <paramref name="netzId"/>
    /// ist ein Kanal, ein ganzes Kanalnetz oder ein gewöhnliches Netz. Gesetzt wird bis zum nächsten 04:00, das rechnet der Server.
    /// </summary>
    Task<string?> KanalSperreAsync(uint kontoId, ushort netzId, bool sperren);

    /// <summary>
    /// Die laufenden Kanalsperren für alle ohne Einsatzleitung (0x003A LISTE, Konto 0 der Antwort, nur Einsatzleitung, Server ab 0.6.0),
    /// für den Abschnitt "Für alle gesperrt" am Brett. Setzen und aufheben geht über <see cref="KanalSperreAsync"/> mit Konto 0.
    /// </summary>
    Task<Dienstantwort<IReadOnlyList<KanalsperreInfo>>> SperrenFuerAlleAsync();

    // ---- Meldesystem (0.12.0, M1-M23): nur an Server ab 0.12.0 (FunkLage.ServerMeldungen) ----

    /// <summary>
    /// Eine Meldung an alle, die online sind (0x0026): Befehl, Zustand, Ampel, Countdown, Bereit-Abfrage oder ihr Aufheben
    /// (<see cref="Meldungsvorgang"/>), nur wer darf (<see cref="KontoInfo.DarfMelden"/>). Höchstens eine in 2 s; die Wartezeit zählt
    /// ab der Antwort auf die vorige. Der Satz bei einem Fehler kommt meist vom Server (etwa "Die Zielzeit ist schon vorbei"). Die
    /// Meldung selbst kommt wie bei allen über den Push (<see cref="MeldungEingegangen"/>, <see cref="FunkLage.Meldestand"/>).
    /// </summary>
    Task<string?> MeldungSendenAsync(MeldungSendenAnfrage anfrage);

    /// <summary>
    /// "Bereit" auf die offene Bereit-Abfrage (0x0027), jeder Funker per Taste oder Knopf; <paramref name="nummer"/> aus
    /// <see cref="FunkLage.Meldestand"/> (<c>abfrage.nummer</c>). Zweimal ist wie einmal.
    /// </summary>
    Task<string?> BereitMeldenAsync(uint nummer);

    /// <summary>
    /// Die Uhr des Servers jetzt, in Unix-ms (<see cref="Funk.ClientKern.Uhrabgleich"/>: über Ping laufend abgeglichen, Ziel etwa 0,1 s;
    /// vor der ersten Ping-Antwort grob aus Welcome, ohne Verbindung die eigene Uhr). Countdown und Bereit-Abfrage rechnen damit.
    /// </summary>
    long ServerJetztMs();

    /// <summary>
    /// Ein Ereignis für die Teilnehmersicht, das nicht als Stand in der Lage aufgeht (etwa: die laufende Sendung verlor ein Ziel);
    /// aus einem beliebigen Thread.
    /// </summary>
    event Action<FunkEreignis>? Ereignis;
}

/// <summary>Ein Ereignis des Funkbetriebs für die Meldezeile (RAID-S2-ENTWURF Abschnitt 3, Tabelle).</summary>
public abstract record FunkEreignis;

/// <summary>
/// Die laufende Sendung verlor Ziele (C4, ZIEL_ENTZOGEN): Netze, in denen kein Senderecht mehr besteht, und wer umgestellt hat
/// (<c>null</c>, wenn der Server es nicht sagt).
/// </summary>
public sealed record SendenetzEntzogen(IReadOnlyList<ushort> Netze, uint? Durch) : FunkEreignis;

/// <summary>
/// Meldesystem (0.12.0): ein Push 0x4005 kam an, so wie der Server ihn schickt, also mit <c>befehl</c>, wenn es ein Befehl war
/// (8 s groß, Ton), und mit <c>anlass</c> (<see cref="Meldungsvorgang"/>). Die Lage hat den laufenden Stand schon, wenn das Ereignis
/// kommt (<see cref="FunkLage.Meldestand"/>, dort ohne Befehl).
/// </summary>
public sealed record MeldungEingegangen(Meldestand Stand) : FunkEreignis;

/// <summary>
/// Antwort einer lesenden Anfrage: ein Wert oder ein Satz für die Anzeige. <see cref="Code"/> nennt den Fehlercode des Servers,
/// wo die Oberfläche je nach Code anders reagiert (etwa VERALTET beim Zurücknehmen auf dem Brett).
/// </summary>
public sealed record Dienstantwort<T>(T? Wert, string? Fehler)
{
    public bool Ok => Fehler is null;

    public Fehlercode? Code { get; init; }

    /// <summary><c>details</c> des Fehlers (4.2), etwa <c>{nr, durch}</c> bei VERALTET auf PhaseSchalten; sonst <c>null</c>.</summary>
    public System.Text.Json.JsonElement? Details { get; init; }
}

/// <summary>Bau von <see cref="Dienstantwort{T}"/>.</summary>
public static class Dienstantwort
{
    public static Dienstantwort<T> Gut<T>(T wert) => new(wert, null);

    public static Dienstantwort<T> Schlecht<T>(string fehler, Fehlercode? code = null) => new(default, fehler) { Code = code };
}
