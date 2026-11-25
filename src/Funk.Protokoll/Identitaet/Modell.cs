// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Protokoll.Identitaet;

/// <summary>Stufe eines Kontos, zugleich der Rang: FUNKER &lt; RAIDLEITER &lt; ADMIN (6.10).</summary>
public enum Stufe
{
    Funker,
    Raidleiter,
    Admin,
}

/// <summary>Rechte nach 6.1. Standard aus der Stufe, Abweichungen als <c>plus</c> und <c>minus</c>.</summary>
[Flags]
public enum Rechte
{
    Keine = 0,
    GlobalSenden = 1 << 0,
    Durchsage = 1 << 1,
    DurchsageAlle = 1 << 2,
    MehrfachSenden = 1 << 3,
    Zuweisen = 1 << 4,
    SprechanzeigeAlle = 1 << 5,
    GeraeteFreigeben = 1 << 6,
    KontenVerwalten = 1 << 7,
    NetzeVerwalten = 1 << 8,
}

/// <summary>Standardrechte, Privilegierung und Drahtnamen (6.1, 6.10).</summary>
public static class Rechteregeln
{
    /// <summary>Alle Rechte aus 6.1.</summary>
    public const Rechte Alle = (Rechte)((1 << 9) - 1);

    /// <summary>Rechte, die ein Konto privilegiert machen (Begriffe, 6.10).</summary>
    public const Rechte Privilegierend = Rechte.GeraeteFreigeben | Rechte.KontenVerwalten | Rechte.NetzeVerwalten;

    /// <summary>
    /// Stufe RAIDLEITER ("Führung" in der Oberfläche): seit 0.3.0 ohne <c>ZUWEISEN</c> (A37). Gleich zu halten mit
    /// <c>Funk.Server.Lage.Rechteregeln.Fuehrung</c>; ein Test vergleicht beide Tabellen.
    /// </summary>
    private const Rechte Fuehrung = Rechte.GlobalSenden | Rechte.Durchsage | Rechte.DurchsageAlle
        | Rechte.MehrfachSenden | Rechte.SprechanzeigeAlle;

    private static readonly (Rechte Recht, string Name)[] Namen =
    [
        (Rechte.GlobalSenden, "GLOBAL_SENDEN"),
        (Rechte.Durchsage, "DURCHSAGE"),
        (Rechte.DurchsageAlle, "DURCHSAGE_ALLE"),
        (Rechte.MehrfachSenden, "MEHRFACH_SENDEN"),
        (Rechte.Zuweisen, "ZUWEISEN"),
        (Rechte.SprechanzeigeAlle, "SPRECHANZEIGE_ALLE"),
        (Rechte.GeraeteFreigeben, "GERAETE_FREIGEBEN"),
        (Rechte.KontenVerwalten, "KONTEN_VERWALTEN"),
        (Rechte.NetzeVerwalten, "NETZE_VERWALTEN"),
    ];

    /// <summary>Standardrechte der Stufe nach der Tabelle in 6.1.</summary>
    public static Rechte Standard(Stufe stufe) => stufe switch
    {
        Stufe.Funker => Rechte.Keine,
        Stufe.Raidleiter => Fuehrung,
        Stufe.Admin => Alle,
        _ => throw new ArgumentOutOfRangeException(nameof(stufe)),
    };

    /// <summary>Privilegiert: Stufe ADMIN oder eines der Rechte aus <see cref="Privilegierend"/>.</summary>
    public static bool IstPrivilegiert(Stufe stufe, Rechte rechte) => stufe == Stufe.Admin || (rechte & Privilegierend) != 0;

    /// <summary>Drahtnamen in der Reihenfolge der Tabelle 6.1.</summary>
    public static IReadOnlyList<string> Drahtnamen(Rechte rechte)
    {
        var liste = new List<string>(9);
        foreach (var (recht, name) in Namen)
            if ((rechte & recht) != 0) liste.Add(name);
        return liste;
    }

    /// <summary>Liest Drahtnamen; ein unbekannter Name ergibt <c>false</c>.</summary>
    public static bool TryAusDrahtnamen(IEnumerable<string> namen, out Rechte rechte)
    {
        ArgumentNullException.ThrowIfNull(namen);
        rechte = Rechte.Keine;
        foreach (var n in namen)
        {
            var gefunden = false;
            foreach (var (recht, name) in Namen)
            {
                if (!string.Equals(name, n, StringComparison.Ordinal)) continue;
                rechte |= recht;
                gefunden = true;
                break;
            }
            if (!gefunden) return false;
        }
        return true;
    }

    public static string Drahtname(this Stufe stufe) => stufe switch
    {
        Stufe.Funker => "FUNKER",
        Stufe.Raidleiter => "RAIDLEITER",
        Stufe.Admin => "ADMIN",
        _ => throw new ArgumentOutOfRangeException(nameof(stufe)),
    };
}

/// <summary>Status eines Kontos (3.3).</summary>
public enum KontoStatus
{
    Aktiv,
    Gesperrt,
}

/// <summary>Grund einer Kontosperre. Eine Sperre durch den Abgleich hebt der Abgleich selbst wieder auf (3.7).</summary>
public enum Sperrgrund
{
    Keiner,
    Admin,
    Abgleich,
}

/// <summary>Gespeicherter Status eines Geräts (3.3).</summary>
public enum GeraetStatus
{
    Wartet,
    Freigegeben,
    Abgelehnt,
    Entfernt,
    Entzogen,
}

/// <summary>Status in <c>GeraetStatus</c>-Meldungen (4.5); zusätzlich BEANTRAGT für einen Entfernungsantrag.</summary>
public enum GeraetStatusMeldung
{
    Wartet,
    Freigegeben,
    Abgelehnt,
    Entfernt,
    Entzogen,
    Beantragt,
}

/// <summary>Hinweis eines Geschwistergeräts zu einer wartenden Anfrage (3.5).</summary>
public enum Markierung
{
    Keine,
    Bestaetigt,
    Einspruch,
}

/// <summary>Entscheidung nach <c>GeraetEntscheiden</c> (4.5).</summary>
public enum Entscheidung
{
    Freigeben,
    Ablehnen,
    Bestaetigen,
    Einspruch,
}

/// <summary>Antwort des Discord-Abgleichs (3.7).</summary>
public enum Mitgliedschaftsstatus
{
    Ok,
    NichtMitglied,
    RolleFehlt,
}

/// <summary>Woher eine Verknüpfung kommt. Privilegierte Konten nur über das Admin-Werkzeug (3.5).</summary>
public enum Herkunft
{
    Bot,
    AdminWerkzeug,
}

/// <summary>Fehlercodes dieses Moduls; Drahtnamen nach 4.9 und 3.8 über <see cref="Drahtnamen"/>.</summary>
public enum Fehlercode
{
    Keiner,
    Ungueltig,
    Unbekannt,
    KeinRecht,
    Grenze,
    CodeUngueltig,
    GeraeteLimit,
    NurAdminWerkzeug,
    GeraetGesperrt,
    KontoGesperrt,
    Abgelehnt,
    Frist,

    /// <summary>Ein SC-Name gehört schon einem Konto oder einer Vormerkung (3.3, 6.13).</summary>
    NameVergeben,

    /// <summary>Das Recht wirkt nur in einer Admin-Sitzung (3.9, 6.10).</summary>
    AdminSitzungNoetig,

    /// <summary>Heikle Handlung ohne frische Bestätigung (höchstens 5 min alter Code, 3.9).</summary>
    SchrittBestaetigen,

    /// <summary>Code aus der Authenticator-App falsch oder schon benutzt (3.9).</summary>
    ZweitfaktorFalsch,

    /// <summary>Zu viele Fehlversuche: Pause oder Sperre bis zur Konsole (3.9).</summary>
    ZweitfaktorGesperrt,

    /// <summary>Höchstens 30 offene Anträge (3.4).</summary>
    AntraegeVoll,

    /// <summary>Die mitgeschickte Version ist nicht mehr die aktuelle (Rollenkatalog).</summary>
    Veraltet,
}

/// <summary>Drahtnamen der Aufzählungen dieses Moduls.</summary>
public static class Drahtnamen
{
    public static string Drahtname(this Fehlercode code) => code switch
    {
        Fehlercode.Keiner => "OK",
        Fehlercode.Ungueltig => "UNGUELTIG",
        Fehlercode.Unbekannt => "UNBEKANNT",
        Fehlercode.KeinRecht => "KEIN_RECHT",
        Fehlercode.Grenze => "GRENZE",
        Fehlercode.CodeUngueltig => "CODE_UNGUELTIG",
        Fehlercode.GeraeteLimit => "GERAETE_LIMIT",
        Fehlercode.NurAdminWerkzeug => "NUR_ADMIN_WERKZEUG",
        Fehlercode.GeraetGesperrt => "GERAET_GESPERRT",
        Fehlercode.KontoGesperrt => "KONTO_GESPERRT",
        Fehlercode.Abgelehnt => "ABGELEHNT",
        Fehlercode.Frist => "FRIST",
        Fehlercode.NameVergeben => "NAME_VERGEBEN",
        Fehlercode.AdminSitzungNoetig => "ADMIN_SITZUNG_NOETIG",
        Fehlercode.SchrittBestaetigen => "SCHRITT_BESTAETIGEN",
        Fehlercode.ZweitfaktorFalsch => "ZWEITFAKTOR_FALSCH",
        Fehlercode.ZweitfaktorGesperrt => "ZWEITFAKTOR_GESPERRT",
        Fehlercode.AntraegeVoll => "ANTRAEGE_VOLL",
        Fehlercode.Veraltet => "VERALTET",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    public static string Drahtname(this GeraetStatusMeldung status) => status switch
    {
        GeraetStatusMeldung.Wartet => "WARTET",
        GeraetStatusMeldung.Freigegeben => "FREIGEGEBEN",
        GeraetStatusMeldung.Abgelehnt => "ABGELEHNT",
        GeraetStatusMeldung.Entfernt => "ENTFERNT",
        GeraetStatusMeldung.Entzogen => "ENTZOGEN",
        GeraetStatusMeldung.Beantragt => "BEANTRAGT",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string Drahtname(this SchluesselArt art) => art switch
    {
        SchluesselArt.Software => "SOFTWARE",
        SchluesselArt.Tpm => "TPM",
        _ => throw new ArgumentOutOfRangeException(nameof(art)),
    };

    public static bool TryAusDrahtname(string? name, out SchluesselArt art)
    {
        art = SchluesselArt.Software;
        switch (name)
        {
            case "SOFTWARE": return true;
            case "TPM": art = SchluesselArt.Tpm; return true;
            default: return false;
        }
    }
}

/// <summary>
/// Ein Konto (3.3). Die wirksamen Rechte sind der Standard der Stufe mit den Abweichungen
/// <see cref="Plus"/> und <see cref="Minus"/>. <see cref="DiscordId"/> fehlt bei Konten aus einem Antrag ohne Bot (3.4);
/// sie kommt mit der späteren Verknüpfung.
/// </summary>
public sealed record Konto(
    uint KontoId,
    string? DiscordId,
    string Benutzername,
    string Name,
    Stufe Stufe,
    Rechte Plus,
    Rechte Minus,
    KontoStatus Status,
    Sperrgrund Sperrgrund,
    DateTimeOffset LetztesGeraeteereignis,
    string? NamensvorschlagAbgleich = null)
{
    /// <summary>SC-Namen, höchstens 3, Hauptname zuerst; serverweit eindeutig (3.3, 6.13).</summary>
    public ImmutableArray<string> ScNamen { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Discord-Name, wie im Setup eingetippt ("Mustermann/Max"); nur eine Angabe, kein Schlüssel.</summary>
    public string? DiscordName { get; init; }

    /// <summary>Eigentümer des Servers (3.10); nur an der Konsole zu setzen, im Client unantastbar.</summary>
    public bool Eigentuemer { get; init; }

    /// <summary>Offener Antrag ohne Bot (3.4): Konto WARTET, besetzt keinen Namen, bis ein Admin freigibt.</summary>
    public Kontoantrag? Antrag { get; init; }

    public bool IstAntrag => Antrag is not null;

    /// <summary>Hauptname (erster SC-Name) oder <c>null</c>.</summary>
    public string? Hauptname => ScNamen.IsDefaultOrEmpty ? null : ScNamen[0];

    public Rechte Rechte => (Rechteregeln.Standard(Stufe) | Plus) & ~Minus;

    public bool Privilegiert => Rechteregeln.IstPrivilegiert(Stufe, Rechte);

    public bool Hat(Rechte recht) => (Rechte & recht) == recht;
}

/// <summary>
/// Angaben eines Antrags ohne Code (3.4): SC-Name und Discord-Name aus dem Setup. Sie kommen erst mit der Freigabe in den
/// Namensraum; bis dahin sind sie Angaben, keine Beweise.
/// </summary>
public sealed record Kontoantrag(string ScName, string DiscordName, DateTimeOffset Gestellt);

/// <summary>
/// Ein Gerät (3.2, 3.3). <see cref="WartefristAb"/> startet die 24-h-Frist in WARTET; nach dem Rückfall durch die
/// 90-Tage-Regel läuft sie erst ab dem nächsten Verbindungsversuch. <see cref="AntragVon"/> ist das
/// Geschwistergerät, das die Entfernung beantragt hat.
/// </summary>
public sealed record Geraet(
    uint GeraetId,
    uint KontoId,
    Fingerabdruck Fingerabdruck,
    string Name,
    SchluesselArt SchluesselArt,
    GeraetStatus Status,
    DateTimeOffset Verknuepft,
    DateTimeOffset? WartefristAb,
    DateTimeOffset? FreigegebenAm,
    DateTimeOffset? Zuletzt,
    Markierung Markierung = Markierung.Keine,
    uint? AntragVon = null)
{
    public string Anzeige => Fingerabdruck.Anzeige;

    /// <summary>Zählt zum Limit von 3 (WARTET oder FREIGEGEBEN).</summary>
    public bool IstAktiv => Status is GeraetStatus.Wartet or GeraetStatus.Freigegeben;

    /// <summary>Später verknüpft als <paramref name="anderes"/>; bei Gleichstand entscheidet die höhere ID.</summary>
    public bool IstJuengerAls(Geraet anderes)
    {
        ArgumentNullException.ThrowIfNull(anderes);
        return Verknuepft > anderes.Verknuepft || (Verknuepft == anderes.Verknuepft && GeraetId > anderes.GeraetId);
    }
}

/// <summary>
/// Unveränderlicher Stand aller Konten und Geräte (6.2: Schnappschuss mit strukturellem Teilen). Gesperrte
/// Fingerabdrücke bleiben auch nach dem Löschen eines Kontos (3.6). <see cref="GeraeteMitSitzung"/> ist flüchtig
/// und wird beim Laden nach einem Neustart geleert.
/// </summary>
public sealed record Kontenstand
{
    public static Kontenstand Leer { get; } = new();

    public ImmutableDictionary<uint, Konto> Konten { get; init; } = ImmutableDictionary<uint, Konto>.Empty;

    public ImmutableDictionary<uint, Geraet> Geraete { get; init; } = ImmutableDictionary<uint, Geraet>.Empty;

    public ImmutableDictionary<string, uint> NachDiscordId { get; init; } = ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal);

    public ImmutableDictionary<string, uint> NachNamensschluessel { get; init; } = ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal);

    /// <summary>Namensraum der SC-Namen (NFKC, Großschreibung) freigegebener Konten; Anträge stehen nie darin.</summary>
    public ImmutableDictionary<string, uint> NachScName { get; init; } = ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal);

    /// <summary>
    /// Namensraum der Discord-Namen (NFKC, Großschreibung wie die SC-Namen, A53) freigegebener Konten; Anträge stehen nie darin.
    /// Gebaut, nicht gespeichert.
    /// </summary>
    public ImmutableDictionary<string, uint> NachDiscordName { get; init; } = ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal);

    public ImmutableDictionary<Fingerabdruck, uint> NachFingerabdruck { get; init; } = ImmutableDictionary<Fingerabdruck, uint>.Empty;

    /// <summary>
    /// Bewusst gesperrte Geräte: Nur ein Admin sperrt (ENTZOGEN, Konsole <c>sperren</c>), nie Ablehnen, Löschen oder eine Frist
    /// (seit 0.7.0, Sarevoc 04.10.2026). Die Sperre gilt auch nach dem Löschen des Kontos.
    /// </summary>
    public ImmutableHashSet<Fingerabdruck> GesperrteFingerabdruecke { get; init; } = ImmutableHashSet<Fingerabdruck>.Empty;

    public ImmutableHashSet<uint> GeraeteMitSitzung { get; init; } = ImmutableHashSet<uint>.Empty;

    public uint NaechsteKontoId { get; init; } = 1;

    public uint NaechsteGeraetId { get; init; } = 1;

    /// <summary>Steigt mit jeder Änderung.</summary>
    public long Version { get; init; }

    /// <summary>Alle Geräte eines Kontos, älteste zuerst.</summary>
    public IReadOnlyList<Geraet> GeraeteVon(uint kontoId)
    {
        var liste = new List<Geraet>();
        foreach (var g in Geraete.Values)
            if (g.KontoId == kontoId) liste.Add(g);
        liste.Sort(static (a, b) => a.IstJuengerAls(b) ? 1 : b.IstJuengerAls(a) ? -1 : 0);
        return liste;
    }

    /// <summary>Konto zur Discord-ID oder <c>null</c>.</summary>
    public Konto? KontoZuDiscordId(string discordId) =>
        NachDiscordId.TryGetValue(discordId, out var id) ? Konten[id] : null;

    /// <summary>Konto, dem dieser SC-Name gehört (Vergleich nach NFKC und Großschreibung), oder <c>null</c>.</summary>
    public Konto? KontoZuScName(string scName) =>
        NachScName.TryGetValue(Zeichenregeln.Namensschluessel(scName), out var id) && Konten.TryGetValue(id, out var k) ? k : null;

    /// <summary>
    /// Baut die Namensräume der SC-Namen und der Discord-Namen (A53) aus den Konten, nach dem Laden; sie werden nicht
    /// gespeichert. Offene Anträge stehen in keinem von beiden. Tragen zwei Konten denselben Discord-Namen (etwa aus einem
    /// Stand vor A53), gehört er dem mit der kleineren Konto-ID, und <see cref="DoppelteDiscordNamen"/> nennt beide.
    /// </summary>
    public Kontenstand MitScNamenIndex()
    {
        var index = ImmutableDictionary.CreateBuilder<string, uint>(StringComparer.Ordinal);
        var discord = ImmutableDictionary.CreateBuilder<string, uint>(StringComparer.Ordinal);
        foreach (var k in Konten.Values.OrderBy(k => k.KontoId))
        {
            foreach (var n in k.ScNamen)
                index[Zeichenregeln.Namensschluessel(n)] = k.KontoId;
            if (!k.IstAntrag && k.DiscordName is { } d) discord.TryAdd(Zeichenregeln.Namensschluessel(d), k.KontoId);
        }
        return this with { NachScName = index.ToImmutable(), NachDiscordName = discord.ToImmutable(), NachNamensschluessel = AnzeigenamenNachgezogen() };
    }

    /// <summary>
    /// Der Namensraum der Anzeigenamen steht in konten.json, mit Schlüsseln, wie sie beim Schreiben galten. Seit das große ẞ
    /// wie das kleine ß zählt (Gegenprüfung des Nachtbaus, 01.10.2026), zieht das Laden jeden Schlüssel auf die heutige Rechnung
    /// nach; sonst fände weder der Vergleich noch das Umbenennen den alten Eintrag. Fallen zwei zusammen, bleibt der erste.
    /// </summary>
    private ImmutableDictionary<string, uint> AnzeigenamenNachgezogen()
    {
        if (NachNamensschluessel.Keys.All(k => string.Equals(k, Zeichenregeln.Namensschluessel(k), StringComparison.Ordinal))) return NachNamensschluessel;
        var neu = ImmutableDictionary.CreateBuilder<string, uint>(StringComparer.Ordinal);
        foreach (var (schluessel, id) in NachNamensschluessel.OrderBy(e => e.Key, StringComparer.Ordinal))
            neu.TryAdd(Zeichenregeln.Namensschluessel(schluessel), id);
        return neu.ToImmutable();
    }

    /// <summary>Konten, deren Discord-Name schon ein anderes Konto trägt (nur aus alten Ständen möglich); für eine Warnung beim Start.</summary>
    public IReadOnlyList<Konto> DoppelteDiscordNamen() =>
        [.. Konten.Values.Where(k => !k.IstAntrag && k.DiscordName is { } d
            && NachDiscordName.TryGetValue(Zeichenregeln.Namensschluessel(d), out var besitzer) && besitzer != k.KontoId).OrderBy(k => k.KontoId)];

    /// <summary>Der Eigentümer (3.10) oder <c>null</c>, solange keiner gesetzt ist.</summary>
    public Konto? Eigentuemer
    {
        get
        {
            foreach (var k in Konten.Values)
                if (k.Eigentuemer) return k;
            return null;
        }
    }

    /// <summary>Gerät zum Fingerabdruck oder <c>null</c>.</summary>
    public Geraet? GeraetZuFingerabdruck(Fingerabdruck fingerabdruck) =>
        NachFingerabdruck.TryGetValue(fingerabdruck, out var id) ? Geraete[id] : null;
}

/// <summary>
/// Wer eine Aktion auslöst: eine AKTIV-Sitzung (Konto und Gerät) oder das Admin-Werkzeug. <see cref="AdminSitzung"/>:
/// die Sitzung hat sich mit Admin-Schlüssel und Code ausgewiesen; nur dann wirken die Verwaltungsrechte
/// (<see cref="Rechteregeln.Privilegierend"/>, 3.9). <see cref="Bestaetigt"/>: der letzte Code ist höchstens 5 min alt;
/// nur dann gehen heikle Handlungen durch.
/// </summary>
public readonly record struct Akteur(bool IstAdminWerkzeug, uint KontoId, uint GeraetId, bool AdminSitzung = false, bool Bestaetigt = false)
{
    public static Akteur AdminWerkzeug => new(true, 0, 0, true, true);

    /// <summary>Normale Sitzung: Verwaltungsrechte wirken nicht.</summary>
    public static Akteur Sitzung(uint kontoId, uint geraetId) => new(false, kontoId, geraetId);

    /// <summary>Sitzung mit offener Admin-Sitzung; <paramref name="bestaetigt"/> für heikle Handlungen.</summary>
    public static Akteur Verwaltend(uint kontoId, uint geraetId, bool bestaetigt) => new(false, kontoId, geraetId, true, bestaetigt);
}
