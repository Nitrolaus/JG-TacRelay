// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>Verbindungszustand für die Anzeige, verdichtet aus 4.6.</summary>
public enum Verbindungsstatus
{
    Getrennt,
    Verbinden,
    Registrierung,
    Warteraum,
    Abgleich,
    Aktiv,
    AktivTunnel,
}

/// <summary>
/// Das eigene Konto (Welcome.konto, danach <c>Mitgliedschaften.konto</c>, F3, F13): Rechte, Namen, Admin-Zustand, Rollen vom
/// Admin, eigene Lieblingsrollen und Zeichen.
/// </summary>
public sealed record KontoInfo(uint Id, string Name, string Benutzername, Stufe? Stufe, IReadOnlyList<Recht> Rechte)
{
    /// <summary>
    /// Wirksam: dauerhaft (<see cref="Rechte"/>) oder aus einem laufenden Auftrag (6.12). So prüft auch der Server; ein Auftrag
    /// gilt hier ab der Meldung des Servers (<c>aktiv</c>) und bis <see cref="BeauftragungInfo.Bis"/>. Solange "Heute als Funker"
    /// läuft (<see cref="AlsFunkerBis"/>, 6.15), ruhen die Rechte der Einsatzleitung, wie beim Server (<c>Lagebild.HatRecht</c>).
    /// </summary>
    public bool Hat(Recht recht)
    {
        if (AlsFunker && IstEinsatzleitungsrecht(recht)) return false;
        return Rechte.Contains(recht) || (Beauftragung is { Aktiv: true } b && b.Rechte.Contains(recht));
    }

    /// <summary>Führung und ZUWEISEN (6.1): was im Funker-Modus ruht.</summary>
    public static bool IstEinsatzleitungsrecht(Recht recht) =>
        recht is Recht.GlobalSenden or Recht.Durchsage or Recht.DurchsageAlle or Recht.MehrfachSenden or Recht.Zuweisen or Recht.SprechanzeigeAlle;

    /// <summary>
    /// "Heute als Funker" läuft bis dahin (Zeit des Servers), <c>null</c> ohne. Der Server schickt das Feld nur, solange der Modus
    /// läuft, und meldet das Ende mit neuen Mitgliedschaften.
    /// </summary>
    public DateTimeOffset? AlsFunkerBis { get; init; }

    /// <summary>"Heute als Funker" läuft.</summary>
    public bool AlsFunker => AlsFunkerBis is not null;

    /// <summary>Stufe ADMIN: darf "Heute als Funker" einschalten.</summary>
    public bool IstAdmin => Stufe == Protokoll.Steuerung.Stufe.Admin;

    /// <summary>Nur die dauerhaften Rechte (Stufe und Einzelrechte), ohne Auftrag.</summary>
    public bool HatDauerhaft(Recht recht) => Rechte.Contains(recht);

    /// <summary>Laufender oder geplanter Auftrag für die Einsatzleitung (F2, F3, 6.12), sonst <c>null</c>.</summary>
    public BeauftragungInfo? Beauftragung { get; init; }

    /// <summary>Einsatzleitung: darf einteilen (ZUWEISEN, aus Stufe, Einzelrecht oder Auftrag). Dann gibt es das Brett.</summary>
    public bool Einsatzleitung => Hat(Recht.Zuweisen);

    /// <summary>SC-Namen, Hauptname zuerst (1 bis 3).</summary>
    public IReadOnlyList<string> ScNamen { get; init; } = [];

    public string? DiscordName { get; init; }

    /// <summary>Admin-Zustand (3.9); <c>null</c> ohne Admin-Nachweis.</summary>
    public AdminStatus? Admin { get; init; }

    /// <summary>Vom Eigentümer vorgesehen: der Verwaltungsteil darf einen Admin-Schlüssel beantragen.</summary>
    public bool AdminAntrag { get; init; }

    public bool Eigentuemer { get; init; }

    /// <summary>Mitgliederrollen, vom Admin eingetragen (A46).</summary>
    public IReadOnlyList<ushort> Rollen { get; init; } = [];

    public IReadOnlyList<ushort> Lieblingsrollen { get; init; } = [];

    public ushort? ErsteWahl { get; init; }

    /// <summary>Kennungen der eigenen Zeichen (A49), etwa ADMIN.</summary>
    public IReadOnlyList<string> Zeichen { get; init; } = [];

    /// <summary>"Mein Zeichen zeigen".</summary>
    public bool ZeichenZeigen { get; init; } = true;

    /// <summary>"Meine Angaben in Wer ist da zeigen" (Sarevoc, 30.09.2026, Antwort 3).</summary>
    public bool KarteFreigeben { get; init; } = true;

    /// <summary>
    /// Seit 0.11.7: alle eigenen Abzeichen (Ränge in Laufbahnfolge, dann Tätigkeiten), auch ausgeblendete; vergeben nur ein Admin. Die
    /// automatischen Zeichen stehen weiter in <see cref="Zeichen"/>. Leer ohne Abzeichen oder bei einem älteren Server.
    /// </summary>
    public IReadOnlyList<string> Abzeichen { get; init; } = [];

    /// <summary>Seit 0.11.7: das selbst gewählte Abzeichen "vorn"; <c>null</c> ohne Wahl (dann steht das erste sichtbare vorn).</summary>
    public string? AbzeichenVorn { get; init; }

    /// <summary>Seit 0.11.7: die Abzeichen und Zeichen, die andere nicht sehen sollen (nur die Anzeige, nie Rechte).</summary>
    public IReadOnlyList<string> AbzeichenAus { get; init; } = [];

    /// <summary>Führung (Stufe RAIDLEITER oder Global senden) oder Einsatzleitung (ZUWEISEN): darf den Rollenkatalog ergänzen (Antwort 4).</summary>
    public bool Fuehrung => Hat(Recht.GlobalSenden) || Hat(Recht.Zuweisen);

    /// <summary>
    /// Seit 0.12.0: der Haken "darf Meldungen senden", vom Admin gesetzt (<c>KontoStand.meldungenSenden</c>). Allein heißt er nichts:
    /// Ob gesendet werden darf, sagt <see cref="DarfMelden"/>.
    /// </summary>
    public bool MeldungenSenden { get; init; }

    /// <summary>
    /// Darf gerade Meldungen senden (Meldesystem 0.12.0, M1, M15-M18), nach derselben Regel wie der Server (<see cref="Melderecht"/>):
    /// Haken und Stufe Führung oder ADMIN oder ein laufender Auftrag, nicht im Funker-Modus. Nur dann gibt es das Feld "Meldungen" auf
    /// der Funkfeld-Zuteilung; entscheidend bleibt der Server.
    /// </summary>
    public bool DarfMelden => Melderecht.Darf(MeldungenSenden, Stufe, Beauftragung is { Aktiv: true }, AlsFunker);

    /// <summary>Der Verwaltungsteil hat etwas zu zeigen: vorgesehen, in Einrichtung, eingerichtet oder gesperrt.</summary>
    public bool Verwaltung => Admin is not null || AdminAntrag;
}

/// <summary>
/// Der eigene Auftrag für die Einsatzleitung (6.12), Zeiten in der Uhr des Servers. <see cref="Aktiv"/>: er gilt gerade (laut
/// Server); sonst ist er geplant. <see cref="Durch"/> ist das erteilende Konto, 0 die Serverkonsole.
/// </summary>
public sealed record BeauftragungInfo(DateTimeOffset Von, DateTimeOffset Bis, IReadOnlyList<Recht> Rechte, uint Durch, string? Grund, bool Aktiv);

/// <summary>Ein Netz aus der NetzListe, in der Reihenfolge des Servers (A16: Name ohne Sortiernummer).</summary>
public sealed record NetzInfo(ushort Id, string Name, bool IstGlobal, bool NurTaste, bool SprachaktivierungStandard, int Reihenfolge)
{
    /// <summary>Einheit, zu der das Netz gehört (Einheitsnetz wie "Crew 2"), sonst <c>null</c> (Sondernetz wie Kommando).</summary>
    public ushort? SchiffId { get; init; }

    /// <summary>
    /// Die Ch.-Liste eines Kanalnetzes (6.16, seit 0.6.0), Index 0 ist die Ruhe; <c>null</c> bei einem gewöhnlichen Netz. Die Kanäle
    /// tragen die Kanal-ID <c>0x8000 + Id x 32 + Ch.</c> (<see cref="Kanalrechnung"/>).
    /// </summary>
    public IReadOnlyList<string>? Kanaele { get; init; }

    /// <summary>Wer das Netz selbst in ein Feld legen darf (<c>offen</c>, 4.5); gilt bei einem Kanalnetz für alle Ch.</summary>
    public NetzOffen Offen { get; init; }

    /// <summary>Ein Kanalnetz mit Ch.-Liste (6.16).</summary>
    public bool IstKanalnetz => Kanaele is { Count: >= Kanalrechnung.EintraegeMin };

    /// <summary>Die Gruppe für Kanalwahl und Reiter der Raid-Seite ("Schiffe", "Boden"; seit 0.6.0 vom Server, im Client seit 0.11.2), sonst <c>null</c>.</summary>
    public string? Gruppe { get; init; }
}

/// <summary>Ein eigenes Feld (Mitgliedschaft): Feldnummer, Netz, hören, senden, fest.</summary>
public sealed record FeldInfo(byte Feld, ushort NetzId, bool Hoeren, bool Senden, bool Fest);

/// <summary>
/// Eine Kanalsperre, die das eigene Konto trifft (6.17, <c>Mitgliedschaften.gesperrt</c>): ein Kanal, ein ganzes Kanalnetz oder ein
/// gewöhnliches Netz, bis wann (Uhr des Servers), durch wen, und ob sie für alle ohne Einsatzleitung gilt.
/// </summary>
public sealed record KanalsperreInfo(ushort NetzId, DateTimeOffset Bis, uint Durch, bool Alle)
{
    /// <summary>
    /// Die Sperre aus <paramref name="sperren"/>, die einen Kanal oder ein Netz trifft: auf genau diese ID oder auf das ganze Kanalnetz
    /// (6.17); <c>null</c> ohne. Eine Ruhe ist nie gesperrt.
    /// </summary>
    public static KanalsperreInfo? Treffer(IReadOnlyList<KanalsperreInfo> sperren, ushort id)
    {
        ArgumentNullException.ThrowIfNull(sperren);
        if (Kanalrechnung.IstRuhe(id)) return null;
        var kanalnetz = Kanalrechnung.Zerlegen(id, out var n, out _) ? n : (ushort?)null;
        return sperren.FirstOrDefault(s => s.NetzId == id || (kanalnetz is { } k && s.NetzId == k));
    }
}

/// <summary>
/// Eine Stummschaltung des eigenen Kontos durch Einsatzleitung oder Admin (4.5 Stummschalten, 0x0034, <c>Mitgliedschaften.stumm</c>): in welchem
/// Netz (0 alle, 1 Global, sonst Netz oder Kanalnetz, dann für alle seine Ch.), bis wann (Uhr des Servers), durch wen. Sie endet von selbst; der
/// Server schickt dazu nichts, die Oberfläche rechnet mit <see cref="FunkLage.ZeitVersatz"/> nach (wie <c>Lagebild.IstStumm</c>).
/// </summary>
public sealed record StummschaltungInfo(ushort NetzId, DateTimeOffset Bis, uint Durch)
{
    /// <summary>In allen Netzen (netzId 0).</summary>
    public bool Alle => NetzId == 0;

    /// <summary>Trifft sie <paramref name="id"/>: alle, genau dieses Netz oder das Kanalnetz eines Kanals.</summary>
    public bool Trifft(ushort id) =>
        Alle || NetzId == id || (Kanalrechnung.Zerlegen(id, out var kanalnetz, out _) && NetzId == kanalnetz);
}

/// <summary>
/// Plätze einer Rolle auf einer Einheit (oder ohne Einheit) und wie viele davon besetzt sind (A15). <see cref="Belegt"/> zählt
/// wie die Kapazität des Servers BELEGT und RESERVIERT (K1); <see cref="Reserviert"/> ist der Anteil davon ohne Sitzung.
/// </summary>
public sealed record PlatzInfo(ushort RolleId, int Kapazitaet, int Belegt)
{
    public int Reserviert { get; init; }

    /// <summary>Mindestbesetzung für die Ampel (F5), 0 ohne.</summary>
    public int Mindest { get; init; }

    public bool Voll => Belegt >= Kapazitaet;

    public int Frei => Math.Max(0, Kapazitaet - Belegt);
}

/// <summary>Eine Einheit (Schiff, Trupp oder Orga, A39) mit Plätzen und Belegung.</summary>
public sealed record SchiffInfo(ushort Id, string Name, int Reihenfolge, IReadOnlyList<PlatzInfo> Plaetze)
{
    public int Belegt => Plaetze.Sum(p => p.Belegt);

    public int Kapazitaet => Plaetze.Sum(p => p.Kapazitaet);

    /// <summary>Schiff, Trupp (Infanterie) oder Orga (F5); ältere Server kennen nur Schiffe.</summary>
    public Einheitsart Art { get; init; } = Einheitsart.Schiff;

    /// <summary>Frei, etwa "Hammerhead".</summary>
    public string? Typ { get; init; }

    /// <summary>Einheitsnetze: Kennung (CREW, BRUECKE, ...) zum Netz.</summary>
    public IReadOnlyDictionary<string, ushort> Netze { get; init; } = new Dictionary<string, ushort>();

    public PlatzInfo? PlatzVon(ushort rolleId) => Plaetze.FirstOrDefault(p => p.RolleId == rolleId);
}

/// <summary>
/// Ein Feld einer Rolle: ein festes Netz oder ein Einheitsnetz über seine Kennung (genau eins von beiden). Nennt
/// <see cref="NetzId"/> ein Kanalnetz, legt <see cref="Ch"/> den Ch. fest; fehlt er, löst der Server ihn auf (6.16).
/// </summary>
public sealed record RollenfeldInfo(byte Feld, ushort? NetzId, string? Kennung, bool Hoeren, bool Senden, bool Sendevorschlag, bool Fest)
{
    public byte? Ch { get; init; }
}

/// <summary>Eine Rolle mit der Vorschau ihrer Netze (A22).</summary>
public sealed record RolleInfo(ushort Id, string Name, int Reihenfolge, bool BrauchtSchiff, bool SelbstWaehlbar, IReadOnlyList<string> Netze)
{
    /// <summary>Plätze ohne Einheit; <c>null</c> heißt unbegrenzt.</summary>
    public int? Kapazitaet { get; init; }

    /// <summary>Wie viele die Rolle ohne Einheit gerade besetzen (BELEGT und RESERVIERT, K1).</summary>
    public int Belegt { get; init; }

    public IReadOnlyList<RollenfeldInfo> Felder { get; init; } = [];

    /// <summary>Die Netze, die diese Rolle auf <paramref name="einheit"/> bekommt (Einheitsnetze über die Kennungen aufgelöst).</summary>
    public IReadOnlyList<ushort> NetzeAuf(SchiffInfo? einheit) =>
        [.. Felder.Select(f => f.NetzId ?? (einheit is not null && f.Kennung is not null && einheit.Netze.TryGetValue(f.Kennung, out var n) ? n : (ushort)0))
            .Where(n => n != 0).Distinct()];

    /// <summary>
    /// Wie <see cref="NetzeAuf"/>, aber ein Rollenfeld mit festem Ch. nennt den Kanal selbst (Kanal-ID, 6.16): so zeigen Brett und
    /// Vorschau "Brücke (Schiff 3)" statt nur den Namen des Kanalnetzes. Ohne Ch. bleibt es die Netz-ID.
    /// </summary>
    public IReadOnlyList<ushort> KanaeleAuf(SchiffInfo? einheit) =>
        [.. Felder.Select(f => f.NetzId is { } n && f.Ch is { } ch && n is > 0 and <= Kanalrechnung.KanalnetzIdMax && ch <= Kanalrechnung.ChMax ? Kanalrechnung.Id(n, ch)
                : f.NetzId ?? (einheit is not null && f.Kennung is not null && einheit.Netze.TryGetValue(f.Kennung, out var e) ? e : (ushort)0))
            .Where(n => n != 0).Distinct()];
}

/// <summary>
/// Was die Einsatzleitung (ZUWEISEN) je Person zusätzlich sieht (4.5 Praesenz, F4): Felder, wohin gesendet wird, Stummschaltungen,
/// Verbindung, wie der Platz zustande kam und der letzte Platz.
/// </summary>
public sealed record FunkerDetails(IReadOnlyList<FeldInfo> Felder, IReadOnlyList<ushort> SendetIn, IReadOnlyList<Stummschaltung> Stumm)
{
    public Verbindungsweg? Verbindung { get; init; }

    public Guete? Guete { get; init; }

    public MitgliedschaftsQuelle? Quelle { get; init; }

    /// <summary>Wer eingeteilt hat (bei VERSCHIEBEN oder AUFSTELLUNG).</summary>
    public uint? Durch { get; init; }

    /// <summary>Seit wann der Platz gilt (Unix-ms).</summary>
    public long? Seit { get; init; }

    /// <summary>Der letzte Platz (Einheit, Rolle), für "wie zuletzt".</summary>
    public (ushort? SchiffId, ushort? RolleId)? LetzterPlatz { get; init; }

    /// <summary>Ein ADMIN spielt heute als Funker (6.15) und ist für die Einsatzleitung einteilbar.</summary>
    public bool AlsFunker { get; init; }

    /// <summary>Die Kanalsperren dieser Person (6.17, seit 0.6.0), ohne die für alle; Uhr des Servers.</summary>
    public IReadOnlyList<KanalsperreInfo> Gesperrt { get; init; } = [];

    /// <summary>Die Sperre, die einen Kanal oder ein Netz für diese Person trifft (auch über das ganze Kanalnetz), sonst <c>null</c>.</summary>
    public KanalsperreInfo? SperreVon(ushort id) => KanalsperreInfo.Treffer(Gesperrt, id);
}

/// <summary>
/// Eine Einsatzphase der Flotte (6.14): Nummer 1 ist die Grundstellung, 2 bis 5 tragen je Rolle, die sich ändert, eine Variante
/// (vollständige Feldliste wie die Rollenfelder). Rollen ohne Variante bleiben wie in der Grundstellung.
/// </summary>
public sealed record PhaseInfo(byte Nr, string Name, string Kurz, IReadOnlyDictionary<ushort, IReadOnlyList<RollenfeldInfo>> Varianten)
{
    /// <summary>Die Felder einer Rolle in dieser Phase: ihre Variante, sonst die Grundstellung der Rolle.</summary>
    public IReadOnlyList<RollenfeldInfo> FelderVon(RolleInfo rolle)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        return Varianten.TryGetValue(rolle.Id, out var v) ? v : rolle.Felder;
    }
}

/// <summary>Die laufende Phase (0x4004), Zeiten in der Uhr des Servers; <see cref="Durch"/> 0 heißt Konsole oder Rückfall nach 12 h.</summary>
public sealed record LaufendePhaseInfo(byte Nr, string Name, string Kurz, uint Durch, DateTimeOffset Seit);

/// <summary>Ein Funker aus der Praesenz.</summary>
public sealed record FunkerInfo(uint KontoId, string Name, bool Online, ushort? SchiffId, ushort? RolleId)
{
    /// <summary>Hauptname (erster SC-Name), eindeutig.</summary>
    public string? ScName { get; init; }

    /// <summary>Discord-Name wie beim Antrag eingetippt ("Mustermann/Max"); "Wer ist da" zeigt ihn als Namen (Antwort 3).</summary>
    public string? DiscordName { get; init; }

    /// <summary>Kennungen der Zeichen (A49); nur, was der Server schickt.</summary>
    public IReadOnlyList<string> Zeichen { get; init; } = [];

    /// <summary>Seit 0.11.7: die sichtbaren Abzeichen (Ränge, dann Tätigkeiten); nur, was der Server schickt, also nichts Ausgeblendetes.</summary>
    public IReadOnlyList<string> Abzeichen { get; init; } = [];

    /// <summary>Seit 0.11.7: das Abzeichen, das die Person vorn zeigt (nur wenn sichtbar), sonst <c>null</c>.</summary>
    public string? AbzeichenVorn { get; init; }

    /// <summary>Der Name in "Wer ist da": Discord-Name, sonst Hauptname, sonst Kontoname.</summary>
    public string Anzeigename => DiscordName ?? ScName ?? Name;

    /// <summary>BELEGT oder RESERVIERT, <c>null</c> ohne Platz.</summary>
    public PlatzStatus? Platz { get; init; }

    /// <summary>Nur für die Einsatzleitung (ZUWEISEN), sonst <c>null</c>.</summary>
    public FunkerDetails? Details { get; init; }
}

/// <summary>Der eigene frühere Platz (<c>Mitgliedschaften.letzterStand</c>), für "wie letztes Mal" (A21).</summary>
public sealed record LetzterStandInfo(ushort? SchiffId, ushort? RolleId, IReadOnlyList<FeldInfo> Felder, IReadOnlyList<byte> SendeFelder, bool GlobalSenden);

/// <summary>Wer gerade spricht und in welche Netze (Sprechanzeige).</summary>
public sealed record SprecherInfo(uint KontoId, string Name, IReadOnlyList<ushort> Netze, bool Durchsage);

/// <summary>Einmalcode der Registrierung zum Anzeigen (3.4). Der Code erscheint nie in <see cref="ToString"/>.</summary>
public sealed record Registrierungsangabe(string Code, string Befehl, DateTimeOffset GueltigBis)
{
    public override string ToString() => "Registrierungsangabe(***)";
}

/// <summary>
/// Was die Oberfläche vom Funkbetrieb wissen muss, als unveränderlicher Schnappschuss. Die Anbindung baut ihn bei jeder
/// Änderung neu; die Oberfläche liest nur.
/// </summary>
public sealed record FunkLage
{
    public static FunkLage Leer { get; } = new();

    public Verbindungsstatus Status { get; init; }

    /// <summary>
    /// Letzter Hinweis (Trennungsgrund, Fehler beim Aufbau) für Funker: ohne Adresse, Port und Pfad, bei Fehlern mit Fehlernummer
    /// (<see cref="Verbindungstexte"/>).
    /// </summary>
    public string? Hinweis { get; init; }

    /// <summary>
    /// Die technische Einzelheit zu <see cref="Hinweis"/> (Adresse:Port, Meldung von Windows, Pfad, Ausnahmetext), <c>null</c>
    /// wenn nichts. Nur für "Details" im Admin-Setup, nie Funkern zeigen.
    /// </summary>
    public string? HinweisEinzelheit { get; init; }

    /// <summary>
    /// Die letzte Sitzung endete mit <c>Bye{ENTFERNT}</c>: Gerät entfernt, Konto gelöscht oder wartendes Gerät verfallen (seit 0.6.3).
    /// Das Gerät ist dann nicht mehr freigegeben, und der Client verbindet nicht von selbst neu. Gilt, bis der Server wieder antwortet.
    /// </summary>
    public bool Entfernt { get; init; }

    /// <summary>Server für die Anzeige (Profilname oder Host).</summary>
    public string? Server { get; init; }

    public KontoInfo? Konto { get; init; }

    /// <summary>Fingerabdruck des Geräts in Anzeigeform.</summary>
    public string? Geraet { get; init; }

    /// <summary>TPM oder Software.</summary>
    public string? Schluesselart { get; init; }

    public IReadOnlyList<NetzInfo> Netze { get; init; } = [];

    public IReadOnlyList<FeldInfo> Felder { get; init; } = [];

    /// <summary>Sendeauswahl als Feldnummern (C1).</summary>
    public IReadOnlyList<byte> SendeFelder { get; init; } = [];

    /// <summary>Global in der Sendeauswahl.</summary>
    public bool GlobalSenden { get; init; }

    /// <summary>Global senden erlaubt (Mitgliedschaften.global.senden).</summary>
    public bool GlobalDarfSenden { get; init; }

    public IReadOnlyList<SchiffInfo> Schiffe { get; init; } = [];

    public IReadOnlyList<RolleInfo> Rollen { get; init; } = [];

    public IReadOnlyList<FunkerInfo> Funker { get; init; } = [];

    public IReadOnlyList<SprecherInfo> Sprecher { get; init; } = [];

    /// <summary>A4: am Konto gespeicherter Pegel je Funker in dB (Netz 0, alle Netze).</summary>
    public IReadOnlyDictionary<uint, float> FunkerPegel { get; init; } = new Dictionary<uint, float>();

    public ushort? SchiffId { get; init; }

    public ushort? RolleId { get; init; }

    /// <summary>Selbstwahl von Rolle und Schiff erlaubt (NetzListe.einstellungen.selbstwahl, A15).</summary>
    public bool Selbstwahl { get; init; }

    /// <summary>Wer die Selbstwahl gesperrt hat (0: Konsole oder ein Stand vor 0.3.0), <c>null</c> ohne Sperre (F9).</summary>
    public uint? SelbstwahlDurch { get; init; }

    /// <summary>Wann die Sperre der Selbstwahl von selbst endet (12 h), <c>null</c> ohne Frist.</summary>
    public DateTimeOffset? SelbstwahlBis { get; init; }

    /// <summary>"Wechsel sperren": Wer noch keinen Platz hat, nimmt trotz Sperre einen freien (F9); false ist "Ganz sperren".</summary>
    public bool Nachzuegler { get; init; } = true;

    /// <summary>Version der NetzListe; Verwaltung (auch SELBSTWAHL) schickt sie mit.</summary>
    public long NetzListeVersion { get; init; }

    /// <summary>Zuletzt bekannte <c>zustandsVersion</c> des Servers (für <c>basisVersion</c>).</summary>
    public long ZustandsVersion { get; init; }

    /// <summary>Wie viele gerade online sind und wie viele davon einen Platz haben (Belegung, sonst aus der Praesenz).</summary>
    public int Online { get; init; }

    public int MitPlatz { get; init; }

    /// <summary>Der eigene Platz: BELEGT oder RESERVIERT, <c>null</c> ohne.</summary>
    public PlatzStatus? Platz { get; init; }

    /// <summary>Wie der eigene Platz zustande kam und durch wen (Teilnehmersicht: "eingeteilt von Mustermann").</summary>
    public MitgliedschaftsQuelle? Quelle { get; init; }

    public uint? QuelleDurch { get; init; }

    /// <summary>Der eigene frühere Platz für "wie letztes Mal" (A21), sobald der Server einen kennt.</summary>
    public LetzterStandInfo? LetzterStand { get; init; }

    /// <summary>
    /// Der Server kennt die Ausbaustufe S2 (ab 0.5.0): Phasen, "Heute als Funker". Sonst blendet der Client beides aus und schickt
    /// 0x003C und 0x003D nicht (ein Server 0.4.x trennte mit PROTOKOLL).
    /// </summary>
    public bool ServerS2 { get; init; }

    /// <summary>Phasen der Flotte nach Nummer (6.14); leer ohne Phasen.</summary>
    public IReadOnlyList<PhaseInfo> Phasen { get; init; } = [];

    /// <summary>Die laufende Phase; ohne Push vom Server <c>null</c> (dann gilt die Grundstellung).</summary>
    public LaufendePhaseInfo? LaufendePhase { get; init; }

    /// <summary>Nummer der laufenden Phase: aus dem Push, sonst 1 (Grundstellung).</summary>
    public byte PhaseNr => LaufendePhase?.Nr ?? 1;

    /// <summary><c>kontoVersion</c> der letzten eigenen Mitgliedschaften: Steigt sie, kam eine Umstellung (Teilnehmersicht).</summary>
    public long KontoVersion { get; init; }

    /// <summary>Kam die letzte eigene Mitgliedschaften aus einem Phasenwechsel, dessen Nummer (6.14 Regel 11).</summary>
    public byte? UmstellungPhase { get; init; }

    public PhaseInfo? PhaseVon(byte nr) => Phasen.FirstOrDefault(p => p.Nr == nr);

    /// <summary>Uhr des Servers minus eigene Uhr (aus Welcome); Fristen in der Anzeige rechnen damit.</summary>
    public TimeSpan ZeitVersatz { get; init; }

    public Registrierungsangabe? Registrierung { get; init; }

    /// <summary>Im Warteraum nach einem Antrag: Namen, die schon ein freigegebenes Konto trägt (GeraetStatus, F1).</summary>
    public IReadOnlyList<string> ScNamenVergeben { get; init; } = [];

    public bool IstAktiv => Status is Verbindungsstatus.Aktiv or Verbindungsstatus.AktivTunnel;

    /// <summary>
    /// Die gewählte Rolle (<c>Mitgliedschaften.rolle</c>, 6.16, seit 0.6.0): die Rolle, deren Felder man über die Rollenliste hat;
    /// <c>null</c>, wenn die Felder aus dem Platz oder einer anderen Vorgabe kommen.
    /// </summary>
    public ushort? Rolle { get; init; }

    /// <summary>Kanalsperren, die das eigene Konto treffen (6.17): eigene und die für alle.</summary>
    public IReadOnlyList<KanalsperreInfo> Gesperrt { get; init; } = [];

    /// <summary>
    /// Stummschaltungen des eigenen Kontos durch Einsatzleitung oder Admin (0x0034, <c>Mitgliedschaften.stumm</c>), auch abgelaufene: Ob eine gilt,
    /// entscheidet die Uhr des Servers (<see cref="StummschaltungenAn"/>). Klangstufe C2b zeigt sie am Mikro-Knopf des Funkfelds.
    /// </summary>
    public IReadOnlyList<StummschaltungInfo> Stumm { get; init; } = [];

    /// <summary>Die Stummschaltungen, die zur eigenen Zeit <paramref name="jetzt"/> (mit dem Versatz der Serveruhr) noch gelten.</summary>
    public IReadOnlyList<StummschaltungInfo> StummschaltungenAn(DateTimeOffset jetzt)
    {
        if (Stumm.Count == 0) return [];
        var server = jetzt + ZeitVersatz;
        return [.. Stumm.Where(s => s.Bis > server)];
    }

    /// <summary>
    /// Der Stand der laufenden Sitzung ist vollständig (der Server hat den Abgleich mit <c>Zustand</c> abgeschlossen, 4.5). Bis dahin
    /// können Felder und Quelle noch aus der vorigen Sitzung stammen.
    /// </summary>
    public bool Abgeglichen { get; init; }

    /// <summary>Der Server kennt Kanäle mit Ch. (ab 0.6.0): Rollenliste und <c>RolleWaehlen</c> gehen nur dann.</summary>
    public bool ServerKanaele { get; init; }

    /// <summary>
    /// Der Server kennt das Meldesystem (ab 0.12.0): Nur dann schickt der Client <c>MeldungSenden</c> und <c>BereitMelden</c> (ein älterer
    /// Server trennte mit PROTOKOLL); ohne lesbare Fassung gilt ja, wie beim Senden.
    /// </summary>
    public bool ServerMeldungen { get; init; }

    /// <summary>
    /// Seit 0.12.0: der laufende Stand des Meldesystems (0x4005) ohne Befehl: Zustand, Ampel, Countdown (Ziel in Serverzeit,
    /// <see cref="IFunkDienst.ServerJetztMs"/>), offene Bereit-Abfrage, eine bleibende Textmeldung. Befehle und Textmeldungen mit 8 s
    /// kommen nur als Ereignis (<see cref="MeldungEingegangen"/>).
    /// <c>null</c>, solange nichts läuft oder der Server älter ist.
    /// </summary>
    public Meldestand? Meldestand { get; init; }

    /// <summary>Seit 0.12.0: "n von m bereit" der letzten Bereit-Abfrage (0x4006), nur für die, die senden dürfen; sonst <c>null</c>.</summary>
    public BereitStand? BereitStand { get; init; }

    /// <summary>
    /// Netz aus der NetzListe oder <c>null</c>; für eine Kanal-ID (6.16) ihr Kanalnetz, wenn der Ch. in dessen Liste steht (wie
    /// <c>Lagebild.NetzVon</c> im ClientKern). So zeigt jede Stelle zu einem Kanal wenigstens den Namen des Kanalnetzes.
    /// </summary>
    public NetzInfo? NetzVon(ushort id)
    {
        foreach (var n in Netze)
            if (n.Id == id) return n;
        return Kanalrechnung.Zerlegen(id, out var netz, out var ch) && Netze.FirstOrDefault(n => n.Id == netz) is { IstKanalnetz: true } k && ch < k.Kanaele!.Count
            ? k
            : null;
    }

    /// <summary>Der Ch. einer Kanal-ID mit bekanntem Kanalnetz; <c>null</c> bei einem gewöhnlichen Netz oder einer unbekannten ID.</summary>
    public byte? ChVon(ushort id) =>
        Kanalrechnung.Zerlegen(id, out _, out var ch) && NetzVon(id) is { IstKanalnetz: true } ? ch : null;

    /// <summary>
    /// Anzeigename eines Netzes oder Kanals: "Brücke (Schiff 3)", "Brücke (Ruhe)", "Crew"; unbekannt "Kanal 33891". Für
    /// Kopfzeile, Meldezeile und Tooltips.
    /// </summary>
    public string KanalName(ushort id)
    {
        if (NetzVon(id) is not { } n) return "Kanal " + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ChVon(id) is { } ch ? n.Name + " (" + n.Kanaele![ch] + ")" : n.Name;
    }

    /// <summary>
    /// Die Sperre, die einen Kanal oder ein Netz für das eigene Konto trifft: auf genau diese ID oder auf das ganze Kanalnetz (6.17);
    /// <c>null</c> ohne. Eine Ruhe ist nie gesperrt.
    /// </summary>
    public KanalsperreInfo? SperreVon(ushort id) => KanalsperreInfo.Treffer(Gesperrt, id);

    /// <summary>Eine Sperre als Satz für Tooltip und Meldung: "Gesperrt von Nordlicht bis 04:00" (Ortszeit), für alle mit Zusatz.</summary>
    public string SperrSatz(KanalsperreInfo sperre)
    {
        ArgumentNullException.ThrowIfNull(sperre);
        var bis = sperre.Bis.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return "Gesperrt von " + HauptnameVon(sperre.Durch) + " bis " + bis + (sperre.Alle ? " (für alle)" : "");
    }

    public FeldInfo? FeldVon(byte feld) => Felder.FirstOrDefault(f => f.Feld == feld);

    public NetzInfo? Global => Netze.FirstOrDefault(n => n.IstGlobal);

    public string NameVon(uint kontoId) => Funker.FirstOrDefault(f => f.KontoId == kontoId)?.Name ?? "Funker " + kontoId;

    public SchiffInfo? SchiffVon(ushort? id) => id is { } s ? Schiffe.FirstOrDefault(x => x.Id == s) : null;

    public RolleInfo? RolleVon(ushort? id) => id is { } r ? Rollen.FirstOrDefault(x => x.Id == r) : null;

    /// <summary>Der Hauptname eines Kontos für Texte wie "eingeteilt von Mustermann": SC-Name, sonst Kontoname; 0 ist die Konsole.</summary>
    public string HauptnameVon(uint kontoId)
    {
        if (kontoId == 0) return "Serverkonsole";
        if (Konto?.Id == kontoId) return Konto.ScNamen.Count > 0 ? Konto.ScNamen[0] : Konto.Name;
        var f = Funker.FirstOrDefault(x => x.KontoId == kontoId);
        return f is null ? "Konto " + kontoId.ToString(System.Globalization.CultureInfo.InvariantCulture) : f.ScName ?? f.Name;
    }
}
