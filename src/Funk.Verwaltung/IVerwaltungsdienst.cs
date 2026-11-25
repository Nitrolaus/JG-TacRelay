// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung;

/// <summary>Ergebnis einer Anfrage der Verwaltung: ein Wert oder ein Satz für die Anzeige, nie beides.</summary>
public sealed record Verwaltungsantwort<T>(T? Wert, string? Fehler)
{
    public bool Ok => Fehler is null;
}

/// <summary>Bau von <see cref="Verwaltungsantwort{T}"/>.</summary>
public static class Verwaltungsantwort
{
    public static Verwaltungsantwort<T> Gut<T>(T wert) => new(wert, null);

    public static Verwaltungsantwort<T> Schlecht<T>(string fehler) => new(default, fehler);
}

/// <summary>Was die Oberfläche der Verwaltung vom eigenen Konto und der Admin-Sitzung wissen muss (3.9).</summary>
public sealed record Verwaltungslage
{
    public static Verwaltungslage Getrennt { get; } = new();

    /// <summary>Die Sitzung ist AKTIV; ohne sie geht nichts.</summary>
    public bool Verbunden { get; init; }

    /// <summary>Name des eigenen Kontos für die Anzeige.</summary>
    public string? Name { get; init; }

    /// <summary>Admin-Zustand des eigenen Kontos laut Server, <c>null</c> ohne Admin-Nachweis.</summary>
    public AdminStatus? Admin { get; init; }

    /// <summary>Der Eigentümer hat das Konto vorgesehen; ein Antrag auf den Admin-Schlüssel ist möglich.</summary>
    public bool AntragMoeglich { get; init; }

    public bool Eigentuemer { get; init; }

    /// <summary>Auf diesem PC liegt schon ein Admin-Schlüssel (im TPM).</summary>
    public bool SchluesselVorhanden { get; init; }

    /// <summary>Ende der offenen Admin-Sitzung (Leerlauf oder 12 h), <c>null</c> ohne Admin-Sitzung.</summary>
    public DateTimeOffset? SitzungBis { get; init; }

    /// <summary>Bis dahin gehen heikle Handlungen ohne neuen Code (5 min nach dem letzten Code).</summary>
    public DateTimeOffset? BestaetigtBis { get; init; }

    /// <summary>Übrige Notfallcodes, wenn der Server sie nannte (Anmeldung mit Notfallcode).</summary>
    public int? NotfallcodesUebrig { get; init; }

    /// <summary>Die Fassung, die der Server in Welcome meldet; <c>null</c>, solange keine bekannt ist.</summary>
    public string? ServerFassung { get; init; }

    public bool SitzungOffen => SitzungBis is not null;

    /// <summary>Der Server kann eine Gerätesperre aufheben (ab 0.8.0); ohne bekannte Fassung gilt ja, wie beim Senden.</summary>
    public bool SperreAufhebenBekannt => Versionen.ServerAb(ServerFassung, Versionen.EntsperrenAb);

    /// <summary>
    /// Der Server kennt den Haken "darf Meldungen senden" (Meldesystem, ab 0.12.0); ohne bekannte Fassung gilt ja. Ein älterer übergeht
    /// das Feld still, die Verwaltung setzt es dort deshalb nicht.
    /// </summary>
    public bool MeldungenBekannt => Versionen.ServerAb(ServerFassung, Versionen.MeldesystemAb);
}

/// <summary>Ergebnis von "Liste einfügen": die Zeilen, die nicht passen (leer bei Erfolg), und ein Hinweis oder <c>null</c>.</summary>
public sealed record Einfuegeergebnis(IReadOnlyList<Zeilenfehler> Zeilen, string? Hinweis = null);

/// <summary>
/// Die Verwaltung aus Sicht ihrer Oberfläche (A43-A47): Admin-Nachweis (Einrichtung, Anmeldung, Schritt-Bestätigung),
/// Anträge und Geräte, Mitglieder mit Rollen und Vormerkungen, Rollenkatalog, Abzeichen. Im Betrieb
/// <see cref="Verwaltungsdienst"/> über den laufenden Client, in Tests eine Attrappe mit vorgetäuschter Admin-Sitzung. Die
/// Anfragen werfen nicht; ein Fehler kommt als Satz für die Anzeige.
/// </summary>
public interface IVerwaltungsdienst
{
    Verwaltungslage Lage { get; }

    /// <summary>Die Lage hat sich geändert; kommt aus einem beliebigen Thread.</summary>
    event Action? LageGeaendert;

    /// <summary>Ein Push für die Verwaltung (neuer Antrag, Mitglieder geändert, Warnung); aus einem beliebigen Thread.</summary>
    event Action<object>? Nachricht;

    /// <summary>
    /// Fragt die Oberfläche nach einem frischen Code, wenn der Server <c>SCHRITT_BESTAETIGEN</c> sagt (3.9); der Text sagt
    /// wofür. <c>null</c> heißt abgebrochen. Ohne sie scheitert jede heikle Handlung nach 5 Minuten.
    /// </summary>
    Func<string, CancellationToken, Task<string?>>? CodeFragen { get; set; }

    /// <summary>
    /// Vorprüfung vor dem Einrichten (seit 0.4.1, Sarevocs Entscheidung vom 01.10.2026), ohne einen Schlüssel anzulegen: TPM 2.0 über
    /// den Platform Crypto Provider (technisch Pflicht). Wirft nicht; was sich nicht feststellen lässt, steht als
    /// <see cref="TpmBefund.NichtFeststellbar"/> da.
    /// </summary>
    Task<TpmStand> VoraussetzungenAsync();

    Task<Verwaltungsantwort<string>> AntragStellenAsync();

    Task<Verwaltungsantwort<AdminEinrichtungAntwort>> GeheimnisAbholenAsync();

    Task<Verwaltungsantwort<IReadOnlyList<string>>> EinrichtungBestaetigenAsync(string code);

    Task<Verwaltungsantwort<AdminSitzungAntwort>> AnmeldenAsync(string code, bool notfallcode);

    Task AbmeldenAsync();

    Task<Verwaltungsantwort<IReadOnlyList<GeraetEintrag>>> AntraegeAsync();

    Task<string?> EntscheidenAsync(uint geraetId, GeraetEntscheidung entscheidung, uint? vormerkungId = null, uint? kontoId = null);

    Task<Verwaltungsantwort<int>> UnpassendeAblehnenAsync();

    /// <summary>
    /// Die Geräte eines Kontos für seine Mitgliederkarte (seit 0.8.0, <c>GeraeteListe</c> mit <c>kontoId</c>, 4.5): Name, Kennung
    /// und Zustand, auch entfernte und gesperrte.
    /// </summary>
    Task<Verwaltungsantwort<IReadOnlyList<GeraetEintrag>>> GeraeteAsync(uint kontoId);

    /// <summary>
    /// "Sperre aufheben" (seit 0.8.0, <c>GeraetEntscheiden ENTSPERREN</c>, 3.6): Das gesperrte Gerät gilt danach als entfernt, der
    /// PC kann neu beantragen. Heikel, also mit frischem Code; Rechte wie beim Sperren. Einen Server vor 0.8.0 fragt der Dienst
    /// gar nicht erst (<see cref="Verwaltungslage.SperreAufhebenBekannt"/>).
    /// </summary>
    Task<string?> SperreAufhebenAsync(uint geraetId);

    Task<Verwaltungsantwort<IReadOnlyList<MitgliedEintrag>>> MitgliederAsync();

    Task<string?> VormerkenAsync(string scName, string? discordName, IReadOnlyList<ushort> rollen, string? notiz);

    Task<string?> KontoAendernAsync(uint kontoId, IReadOnlyList<ushort> rollen, string notiz);

    /// <summary>
    /// Abzeichen eines Kontos (seit 0.11.7, <c>Abzeichen</c> 0x0025): Ränge (höchstens einer je Laufbahn) und Tätigkeiten
    /// (höchstens 6) ersetzen die gespeicherten; nur in der Admin-Sitzung. Zurück kommt der gespeicherte Stand. Einen Server vor
    /// 0.11.7 fragt der Client gar nicht erst (<see cref="Versionen.AbzeichenAb"/>, der Kern antwortet dann mit einem Fehler).
    /// </summary>
    Task<Verwaltungsantwort<AbzeichenAntwort>> AbzeichenSetzenAsync(uint kontoId, IReadOnlyList<string> raenge, IReadOnlyList<string> taetigkeiten);

    /// <summary>
    /// Haken "darf Meldungen senden" eines Kontos (seit 0.12.0, Meldesystem M15-M18; <c>Mitglied AENDERN</c> nur mit
    /// <c>meldungenSenden</c>): nur in der Admin-Sitzung, mit Journal; bleibt, bis ein Admin ihn wegnimmt. Er wirkt nur bei Führung,
    /// Admin oder Einsatzleitung mit laufendem Auftrag; der Stand steht danach in <see cref="MitgliedEintrag.MeldungenSenden"/>. Einen
    /// Server vor 0.12.0 fragt der Dienst nicht (<see cref="Verwaltungslage.MeldungenBekannt"/>), er übergänge das Feld still.
    /// </summary>
    Task<string?> MeldungenSendenSetzenAsync(uint kontoId, bool an);

    /// <summary>
    /// Discord-Name eines Kontos (A53, <c>KontoVerwaltung DISCORDNAME</c>): nur ein Admin in der Admin-Sitzung, eindeutig über
    /// alle Konten und Vormerkungen; <c>null</c> leert ihn.
    /// </summary>
    Task<string?> DiscordNameSetzenAsync(uint kontoId, string? discordName);

    Task<string?> VormerkungAendernAsync(uint vormerkungId, string scName, string? discordName, IReadOnlyList<ushort> rollen, string notiz);

    Task<string?> VormerkungLoeschenAsync(uint vormerkungId);

    /// <summary>
    /// Konto ganz löschen (seit 0.6.3, <c>KontoVerwaltung LOESCHEN</c>, 3.6): Konto, Geräte, Pegel, Ablage, Plätze und
    /// Mitgliedsangaben; laufende Verbindungen enden. Heikel, also mit frischem Code. Nur Konten der Stufen FUNKER und
    /// RAIDLEITER; ADMIN-Konten und den Eigentümer löscht nur die Konsole (der Server sagt sonst <c>NUR_ADMIN_WERKZEUG</c>).
    /// </summary>
    Task<string?> KontoLoeschenAsync(uint kontoId);

    /// <summary>
    /// "Liste einfügen": bei Erfolg ohne Zeilen, sonst jede Zeile, die nicht passt (dann wurde nichts angelegt). Seit 0.11.8 kann
    /// ein Hinweis mitkommen, der nichts verhindert hat (Spalte 3, die Untergilde, wird übergangen).
    /// </summary>
    Task<Verwaltungsantwort<Einfuegeergebnis>> EinfuegenAsync(IReadOnlyList<string> zeilen);

    Task<Verwaltungsantwort<RollenkatalogAntwort>> KatalogAsync();

    Task<string?> KatalogSetzenAsync(IReadOnlyList<Rollenkatalogeintrag> eintraege, long version);

    // ---- Raidleitung (0.3.0): Einsatzleitung und Flotte ----

    /// <summary>Die Flotte, wie der Client sie gerade kennt (NetzListe, Eingeteilte, Mitglieder je Netz); ändert sie sich, kommt <see cref="LageGeaendert"/>.</summary>
    Flottenlage Flotte { get; }

    /// <summary>Ordner für Sicherungen und Entwürfe der Flotte (beim Client neben den Einstellungen), <c>null</c> ohne.</summary>
    string? Ablageordner { get; }

    /// <summary>Laufende und geplante Aufträge (Beauftragung LISTE, Admin-Sitzung).</summary>
    Task<Verwaltungsantwort<IReadOnlyList<BeauftragungEintrag>>> AuftraegeAsync();

    /// <summary>
    /// "Einsatzleitung vergeben" (Beauftragung ERTEILEN, frischer Code): <paramref name="von"/> <c>null</c> heißt jetzt,
    /// <paramref name="bis"/> <c>null</c> der nächste Morgen 04:00. Antwort: alle Aufträge danach.
    /// </summary>
    Task<Verwaltungsantwort<IReadOnlyList<BeauftragungEintrag>>> AuftragErteilenAsync(uint kontoId, long? von, long? bis, IReadOnlyList<Recht> rechte, string? grund);

    /// <summary>"Entziehen" (Beauftragung ENTZIEHEN, frischer Code).</summary>
    Task<string?> AuftragEntziehenAsync(uint kontoId);

    /// <summary>"Übernehmen" der Flotte: ein Verwaltungspaket gegen die Version der NetzListe (N5, frischer Code).</summary>
    Task<string?> FlotteUebernehmenAsync(VerwaltungsPaket paket, long version);
}

/// <summary>Was die Flottenseite vom laufenden Client braucht: die NetzListe, wie viele eingeteilt sind und wer in welchem Netz ist.</summary>
public sealed record Flottenlage
{
    public static Flottenlage Leer { get; } = new();

    public NetzListe? NetzListe { get; init; }

    /// <summary>Konten mit Platz, belegt oder reserviert, wie der Server zählt (K1).</summary>
    public int Eingeteilte { get; init; }

    /// <summary>Mitglieder je Netz aus den Details, die ein Admin sieht; für "Löschen trifft 4 Mitglieder".</summary>
    public IReadOnlyDictionary<ushort, int> Mitglieder { get; init; } = new Dictionary<ushort, int>();

    /// <summary>Der Server kennt Phasen (ab 0.5.0); sonst fehlt der Reiter, und der Entwurf schickt keine PHASE_SETZEN.</summary>
    public bool PhasenBekannt { get; init; }

    /// <summary>Der Server kennt Kanäle mit Ch. (ab 0.6.0, 6.16); sonst schickt der Entwurf keine Ch.-Liste.</summary>
    public bool KanaeleBekannt { get; init; }

    /// <summary>Die laufende Phase (0x4004), ohne Push 1: Ändert man sie, stellt der Server die Betroffenen sofort um.</summary>
    public byte LaufendePhase { get; init; } = 1;
}
