// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Globalization;

namespace Funk.Protokoll.Identitaet;

/// <summary>
/// Konten und Geräte als reine Logik (3.3 bis 3.6): Einstufung nach Hello, Verknüpfung per Einmalcode,
/// Freigabe, Entfernen und Sperren, Kontoverwaltung, Abgleich und Fristen. Jede Aktion prüft Rechte und Status am
/// aktuellen <see cref="Stand"/>; bei einem Fehler bleibt der Stand unverändert (gleiche Referenz). Die Uhr kommt
/// über <see cref="TimeProvider"/>. Nicht threadsicher: genau ein Schreiber (6.2); <see cref="Stand"/> dürfen
/// andere Threads jederzeit lesen.
/// </summary>
public sealed partial class Kontenbuch
{
    /// <summary>Höchstens so viele Geräte in WARTET oder FREIGEGEBEN je Konto.</summary>
    public const int MaxAktiveGeraete = 3;

    /// <summary>Höchstens so viele Konten (1.2).</summary>
    public const int MaxKonten = 500;

    /// <summary>Höchstens so viele Sperren durch den Abgleich je Stunde (3.7, 3.8).</summary>
    public const int AbgleichSperrenJeStunde = 3;

    /// <summary>WARTET ohne Entscheidung verfällt danach (3.6).</summary>
    public static readonly TimeSpan Wartefrist = TimeSpan.FromHours(24);

    /// <summary>FREIGEGEBEN ohne Sitzung fällt danach auf WARTET zurück (3.6).</summary>
    public static readonly TimeSpan Ruhefrist = TimeSpan.FromDays(90);

    /// <summary>Konten ohne freigegebenes Gerät werden so lange nach dem letzten Geräteereignis gelöscht (3.3).</summary>
    public static readonly TimeSpan Loeschfrist = TimeSpan.FromDays(30);

    /// <summary>Frist der Rückfrage <c>VerknuepfungBestaetigen</c> (3.4, 4.2).</summary>
    public static readonly TimeSpan Bestaetigungsfrist = TimeSpan.FromSeconds(120);

    /// <summary>Grund, den ein Rückfall nach der 90-Tage-Regel trägt.</summary>
    public const string RuhefristGrund = "90 Tage ohne Sitzung";

    private readonly TimeProvider _zeit;
    private readonly Dictionary<long, OffeneVerknuepfung> _rueckfragen = [];
    private readonly Zeitfensterzaehler _abgleichSperren;
    private Kontenstand _stand;

    /// <param name="zeit">Wanduhr für Fristen und Zeitstempel, monotone Uhr für die Abgleich-Grenze (4.8).</param>
    public Kontenbuch(TimeProvider zeit, Kontenstand? stand = null)
    {
        ArgumentNullException.ThrowIfNull(zeit);
        _zeit = zeit;
        _abgleichSperren = new(zeit, TimeSpan.FromHours(1), AbgleichSperrenJeStunde);
        _stand = (stand ?? Kontenstand.Leer) with { GeraeteMitSitzung = [] };
    }

    /// <summary>Der aktuelle, unveränderliche Stand.</summary>
    public Kontenstand Stand => Volatile.Read(ref _stand);

    /// <summary>
    /// Ob ein SC-Name (als Namensschlüssel nach NFKC und Großschreibung) einer Vormerkung gehört. Vormerkungen liegen im
    /// Mitgliederbuch; Konten und Vormerkungen bilden einen gemeinsamen Namensraum (6.13). Ohne Angabe: keine.
    /// </summary>
    public Func<string, bool>? VormerkungsNamen { get; set; }

    /// <summary>
    /// Welche Vormerkung einen Discord-Namen (als Namensschlüssel) trägt, oder <c>null</c> (A53: Discord-Namen sind eindeutig
    /// über Konten und Vormerkungen wie die SC-Namen). Ohne Angabe: keine.
    /// </summary>
    public Func<string, uint?>? VormerkungsDiscordName { get; set; }

    private DateTimeOffset Jetzt => _zeit.GetUtcNow();

    /// <summary>
    /// Einstufung nach Hello (4.6 WARTE_HELLO). Wendet die Fristen dieses Geräts an und startet bei einem nach
    /// der 90-Tage-Regel zurückgefallenen Gerät die 24-h-Frist.
    /// </summary>
    public Einstufung Einstufen(Fingerabdruck fingerabdruck)
    {
        var s = _stand;
        var w = new List<Wirkung>();
        if (s.GesperrteFingerabdruecke.Contains(fingerabdruck)) return new(EinstufungsArt.GeraetGesperrt, 0, 0, w);
        if (s.GeraetZuFingerabdruck(fingerabdruck) is not { } vorher) return new(EinstufungsArt.Unbekannt, 0, 0, w);

        var jetzt = Jetzt;
        FristenFuerGeraet(ref s, vorher, jetzt, w);
        Einstufung ergebnis;
        if (!s.Geraete.TryGetValue(vorher.GeraetId, out var g))
        {
            ergebnis = new(EinstufungsArt.Unbekannt, 0, 0, w);
        }
        else if (g.Status is GeraetStatus.Abgelehnt or GeraetStatus.Entzogen)
        {
            ergebnis = new(EinstufungsArt.GeraetGesperrt, g.KontoId, g.GeraetId, w);
        }
        else if (g.Status == GeraetStatus.Entfernt)
        {
            ergebnis = new(EinstufungsArt.Unbekannt, 0, 0, w);
        }
        else if (s.Konten[g.KontoId].Status == KontoStatus.Gesperrt)
        {
            ergebnis = new(EinstufungsArt.KontoGesperrt, g.KontoId, g.GeraetId, w);
        }
        else if (g.Status == GeraetStatus.Wartet)
        {
            if (g.WartefristAb is null) s = MitGeraet(s, g with { WartefristAb = jetzt });
            ergebnis = new(EinstufungsArt.Wartet, g.KontoId, g.GeraetId, w);
        }
        else
        {
            ergebnis = new(EinstufungsArt.Freigegeben, g.KontoId, g.GeraetId, w);
        }
        if (!ReferenceEquals(s, _stand)) Uebernehmen(s);
        return ergebnis;
    }

    /// <summary>
    /// Nach einem Treffer des Einmalcodes (3.4 Schritt 4 und 5): prüft Konto und Gerät und legt die Rückfrage an
    /// die Verbindung an. Nichts wird angelegt, bevor der Client bestätigt. Über den Bot entstehen nur Konten der
    /// Stufe FUNKER; privilegierte Konten nur über das Admin-Werkzeug.
    /// </summary>
    public Verknuepfungsanfrage VerknuepfungAnfragen(
        CodeBindung bindung, string discordId, string benutzername, string? name, Herkunft herkunft, Stufe stufeNeuesKonto = Stufe.Funker)
    {
        ArgumentNullException.ThrowIfNull(bindung);
        if (!Enum.IsDefined(herkunft) || !Enum.IsDefined(stufeNeuesKonto)
            || !Zeichenregeln.IstSnowflake(discordId) || !Zeichenregeln.IstBenutzername(benutzername)
            || (herkunft == Herkunft.Bot && stufeNeuesKonto != Stufe.Funker)
            || _rueckfragen.ContainsKey(bindung.Verbindung))
            return new(Fehlercode.Ungueltig, null, null, false, default);

        var s = _stand;
        var jetzt = Jetzt;
        var fehler = PruefeVerknuepfbar(s, bindung.Geraet, discordId, herkunft, jetzt);
        if (fehler != Fehlercode.Keiner) return new(fehler, null, null, false, default);

        var konto = s.KontoZuDiscordId(discordId);
        var frist = jetzt + Bestaetigungsfrist;
        _rueckfragen[bindung.Verbindung] = new OffeneVerknuepfung(bindung, discordId, benutzername, name, herkunft, stufeNeuesKonto, frist);
        return new(Fehlercode.Keiner, konto?.Name ?? Namensvorschlag(name, benutzername), benutzername, konto is null, frist);
    }

    /// <summary>
    /// Antwort des Clients auf die Rückfrage (3.4 Schritt 5 und 6). Nein ergibt <c>ABGELEHNT</c>, nach der Frist
    /// <c>FRIST</c>, beides ohne Anlage. Bei Ja wird erneut geprüft, dann das Konto gefunden oder als FUNKER (bzw.
    /// mit der Stufe des Admin-Werkzeugs) angelegt und das Gerät in WARTET gesetzt. Nie direkt FREIGEGEBEN.
    /// </summary>
    public Verknuepfungsergebnis VerknuepfungBeantworten(long verbindung, bool ja)
    {
        if (!_rueckfragen.Remove(verbindung, out var o)) return Verknuepfungsfehler(Fehlercode.Unbekannt);
        var jetzt = Jetzt;
        if (jetzt >= o.Frist) return Verknuepfungsfehler(Fehlercode.Frist);
        if (!ja) return Verknuepfungsfehler(Fehlercode.Abgelehnt);

        var s = _stand;
        var fehler = PruefeVerknuepfbar(s, o.Bindung.Geraet, o.DiscordId, o.Herkunft, jetzt);
        if (fehler != Fehlercode.Keiner) return Verknuepfungsfehler(fehler);

        var konto = s.KontoZuDiscordId(o.DiscordId);
        if (konto is null)
        {
            s = KontoAnlegenIntern(s, o.DiscordId, o.Benutzername, o.Name, o.Stufe, jetzt, out konto);
        }
        else if (!string.Equals(konto.Benutzername, o.Benutzername, StringComparison.Ordinal))
        {
            konto = konto with { Benutzername = o.Benutzername };
            s = MitKonto(s, konto);
        }

        if (s.GeraetZuFingerabdruck(o.Bindung.Geraet) is { } alt) s = OhneGeraet(s, alt);
        var g = new Geraet(s.NaechsteGeraetId, konto.KontoId, o.Bindung.Geraet, o.Bindung.Geraetename, o.Bindung.SchluesselArt,
            GeraetStatus.Wartet, Verknuepft: jetzt, WartefristAb: jetzt, FreigegebenAm: null, Zuletzt: null);
        s = MitGeraet(s, g) with { NaechsteGeraetId = g.GeraetId + 1 };
        s = Ereignis(s, konto.KontoId, jetzt);
        Uebernehmen(s);

        List<Wirkung> w =
        [
            new GeraetStatusGeaendert(g.GeraetId, konto.KontoId, GeraetStatusMeldung.Wartet, null, null),
            new BotNachricht(BotZiel.AdminKanal, null, "GERAET_WARTET",
                $"Gerät {g.Name} ({g.Anzeige}) von {konto.Benutzername} ({konto.Name}) wartet auf Freigabe."),
            new BotNachricht(BotZiel.Direktnachricht, konto.DiscordId, "GERAET_VERKNUEPFT",
                $"Gerät {g.Name} ({g.Anzeige}) wurde mit deinem Funkkonto verknüpft. Warst du das nicht, melde dich sofort bei einem Admin."),
        ];
        return new(Fehlercode.Keiner, konto.KontoId, g.GeraetId, g.Name, g.Anzeige, w);
    }

    /// <summary>Die Verbindung ist zu. Liefert, ob dabei eine offene Rückfrage verfiel (Bot: <c>FRIST</c>).</summary>
    public bool VerbindungBeendet(long verbindung) => _rueckfragen.Remove(verbindung);

    /// <summary>Eine AKTIV-Sitzung dieses Geräts beginnt; zählt für die 90-Tage-Regel.</summary>
    public void SitzungBegonnen(uint geraetId)
    {
        var s = _stand;
        if (!s.Geraete.TryGetValue(geraetId, out var g)) return;
        s = MitGeraet(s, g with { Zuletzt = Jetzt });
        Uebernehmen(s with { GeraeteMitSitzung = s.GeraeteMitSitzung.Add(geraetId) });
    }

    /// <summary>Die AKTIV-Sitzung dieses Geräts endet.</summary>
    public void SitzungBeendet(uint geraetId)
    {
        var s = _stand;
        if (!s.Geraete.TryGetValue(geraetId, out var g)) return;
        s = MitGeraet(s, g with { Zuletzt = Jetzt });
        Uebernehmen(s with { GeraeteMitSitzung = s.GeraeteMitSitzung.Remove(geraetId) });
    }

    /// <summary>
    /// Wendet alle fälligen Fristen an (regelmäßig vom Server aufzurufen): WARTET nach 24 h löschen, FREIGEGEBEN
    /// ohne Sitzung nach 90 Tagen auf WARTET, Konten ohne freigegebenes Gerät 30 Tage nach dem letzten
    /// Geräteereignis löschen (nie den letzten aktiven ADMIN), offene Rückfragen nach 120 s verwerfen.
    /// </summary>
    public IReadOnlyList<Wirkung> FristenAnwenden()
    {
        var jetzt = Jetzt;
        var start = _stand;
        var s = start;
        var w = new List<Wirkung>();
        foreach (var g in start.Geraete.Values)
            if (s.Geraete.TryGetValue(g.GeraetId, out var aktuell)) FristenFuerGeraet(ref s, aktuell, jetzt, w);

        foreach (var k in s.Konten.Values)
        {
            // Ein Antrag ohne wartendes Gerät (abgelehnt, verfallen, entfernt) ist erledigt; sein Konto geht sofort.
            if (k.IstAntrag && AktiveGeraete(s, k.KontoId, jetzt) == 0)
            {
                s = KontoEntfernenIntern(s, k, Trenngrund.KontoGeloescht, w);
                continue;
            }
            if (jetzt - k.LetztesGeraeteereignis < Loeschfrist || HatFreigegebenesGeraet(s, k.KontoId)
                || IstLetzterAktiverAdmin(s, k) || k.Eigentuemer) continue;
            s = KontoEntfernenIntern(s, k, Trenngrund.KontoGeloescht, w);
        }

        foreach (var (verbindung, o) in _rueckfragen.ToArray())
        {
            if (jetzt < o.Frist) continue;
            _rueckfragen.Remove(verbindung);
            w.Add(new VerknuepfungVerfallen(verbindung));
        }
        if (!ReferenceEquals(s, start)) Uebernehmen(s);
        return w;
    }

    private void Uebernehmen(Kontenstand neu) => Volatile.Write(ref _stand, neu with { Version = _stand.Version + 1 });

    private static Verknuepfungsergebnis Verknuepfungsfehler(Fehlercode fehler) => new(fehler, 0, 0, null, null, []);

    private static Fehlercode PruefeVerknuepfbar(Kontenstand s, Fingerabdruck fingerabdruck, string discordId, Herkunft herkunft, DateTimeOffset jetzt)
    {
        if (s.GesperrteFingerabdruecke.Contains(fingerabdruck)) return Fehlercode.GeraetGesperrt;
        if (s.NaechsteGeraetId == uint.MaxValue) return Fehlercode.Grenze; // sonst liefe die Geräte-ID über (u32, 4.5)
        if (s.GeraetZuFingerabdruck(fingerabdruck) is { } vorhanden && !IstVerfallen(vorhanden, jetzt))
        {
            if (vorhanden.Status is GeraetStatus.Abgelehnt or GeraetStatus.Entzogen) return Fehlercode.GeraetGesperrt;
            if (vorhanden.Status != GeraetStatus.Entfernt) return Fehlercode.Ungueltig;
        }
        if (s.KontoZuDiscordId(discordId) is not { } konto)
            return s.Konten.Count >= MaxKonten || s.NaechsteKontoId > int.MaxValue ? Fehlercode.Grenze : Fehlercode.Keiner;
        if (herkunft == Herkunft.Bot && konto.Privilegiert) return Fehlercode.NurAdminWerkzeug;
        if (konto.Status == KontoStatus.Gesperrt) return Fehlercode.KontoGesperrt;
        return AktiveGeraete(s, konto.KontoId, jetzt) >= MaxAktiveGeraete ? Fehlercode.GeraeteLimit : Fehlercode.Keiner;
    }

    private static void FristenFuerGeraet(ref Kontenstand s, Geraet g, DateTimeOffset jetzt, List<Wirkung> w)
    {
        if (IstVerfallen(g, jetzt))
        {
            s = Ereignis(OhneGeraet(s, g), g.KontoId, jetzt);
            w.Add(new GeraetGeloescht(g.GeraetId, g.KontoId));
        }
        else if (IstRuhend(s, g, jetzt))
        {
            s = MitGeraet(s, g with { Status = GeraetStatus.Wartet, WartefristAb = null, FreigegebenAm = null, Markierung = Markierung.Keine });
            s = Ereignis(s, g.KontoId, jetzt);
            w.Add(new GeraetStatusGeaendert(g.GeraetId, g.KontoId, GeraetStatusMeldung.Wartet, null, RuhefristGrund));
        }
    }

    /// <summary>WARTET und die 24-h-Frist ist abgelaufen; zählt dann nicht mehr zum Limit.</summary>
    internal static bool IstVerfallen(Geraet g, DateTimeOffset jetzt) =>
        g.Status == GeraetStatus.Wartet && g.WartefristAb is { } ab && jetzt - ab >= Wartefrist;

    private static bool IstRuhend(Kontenstand s, Geraet g, DateTimeOffset jetzt)
    {
        if (g.Status != GeraetStatus.Freigegeben || s.GeraeteMitSitzung.Contains(g.GeraetId)) return false;
        var bezug = g.Verknuepft;
        if (g.FreigegebenAm is { } f && f > bezug) bezug = f;
        if (g.Zuletzt is { } z && z > bezug) bezug = z;
        return jetzt - bezug >= Ruhefrist;
    }

    private static int AktiveGeraete(Kontenstand s, uint kontoId, DateTimeOffset jetzt)
    {
        var n = 0;
        foreach (var g in s.Geraete.Values)
            if (g.KontoId == kontoId && g.IstAktiv && !IstVerfallen(g, jetzt)) n++;
        return n;
    }

    private static bool HatFreigegebenesGeraet(Kontenstand s, uint kontoId)
    {
        foreach (var g in s.Geraete.Values)
            if (g.KontoId == kontoId && g.Status == GeraetStatus.Freigegeben) return true;
        return false;
    }

    private static bool IstLetzterAktiverAdmin(Kontenstand s, Konto k)
    {
        if (k.Stufe != Stufe.Admin || k.Status != KontoStatus.Aktiv) return false;
        foreach (var anderes in s.Konten.Values)
            if (anderes.KontoId != k.KontoId && anderes.Stufe == Stufe.Admin && anderes.Status == KontoStatus.Aktiv) return false;
        return true;
    }

    /// <summary>
    /// Ein Verwaltungsrecht (3.9, 6.10): ohne das Recht <c>KEIN_RECHT</c> (kein Hinweis darauf, was es gäbe), mit dem Recht
    /// aber ohne Admin-Sitzung <c>ADMIN_SITZUNG_NOETIG</c>. Das Admin-Werkzeug hat alle Rechte.
    /// </summary>
    internal static Fehlercode VerwaltungsrechtPruefen(Akteur akteur, Konto? ak, Rechte recht)
    {
        if (akteur.IstAdminWerkzeug) return Fehlercode.Keiner;
        if (ak is null || !ak.Hat(recht)) return Fehlercode.KeinRecht;
        if ((recht & Rechteregeln.Privilegierend) != 0 && !akteur.AdminSitzung) return Fehlercode.AdminSitzungNoetig;
        return Fehlercode.Keiner;
    }

    /// <summary>
    /// Für die anderen Bücher (Mitglieder, Journal): Akteur gültig (Konto AKTIV, Gerät FREIGEGEBEN) und das Recht, bei einem
    /// Verwaltungsrecht nur in einer Admin-Sitzung.
    /// </summary>
    public static Fehlercode RechtPruefen(Kontenstand s, Akteur akteur, Rechte recht)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!AkteurPruefen(s, akteur, out var ak)) return Fehlercode.KeinRecht;
        return VerwaltungsrechtPruefen(akteur, ak, recht);
    }

    /// <summary>Heikle Handlung (3.9): nur mit frischer Bestätigung; sonst <c>SCHRITT_BESTAETIGEN</c>.</summary>
    internal static Fehlercode BestaetigungPruefen(Akteur akteur) =>
        akteur.IstAdminWerkzeug || (akteur.AdminSitzung && akteur.Bestaetigt) ? Fehlercode.Keiner : Fehlercode.SchrittBestaetigen;

    /// <summary>Tauscht die SC-Namen eines Kontos im Namensraum aus (alte Einträge des Kontos fort, neue hinein).</summary>
    private static Kontenstand MitScNamen(Kontenstand s, uint kontoId, IEnumerable<string> alt, IEnumerable<string> neu)
    {
        var index = s.NachScName;
        foreach (var n in alt)
        {
            var schluessel = Zeichenregeln.Namensschluessel(n);
            if (index.TryGetValue(schluessel, out var id) && id == kontoId) index = index.Remove(schluessel);
        }
        foreach (var n in neu) index = index.SetItem(Zeichenregeln.Namensschluessel(n), kontoId);
        return s with { NachScName = index };
    }

    /// <summary>Setzt den Discord-Namen eines Kontos und tauscht ihn im Namensraum aus (A53); <c>null</c> leert ihn.</summary>
    private static Kontenstand MitDiscordName(Kontenstand s, Konto k, string? neu)
    {
        var index = OhneDiscordName(s.NachDiscordName, k);
        if (neu is not null) index = index.SetItem(Zeichenregeln.Namensschluessel(neu), k.KontoId);
        return MitKonto(s with { NachDiscordName = index }, k with { DiscordName = neu });
    }

    /// <summary>Nimmt den Discord-Namen eines Kontos aus dem Namensraum, falls er dort diesem Konto gehört.</summary>
    private static ImmutableDictionary<string, uint> OhneDiscordName(ImmutableDictionary<string, uint> index, Konto k)
    {
        if (k.DiscordName is not { } alt) return index;
        var schluessel = Zeichenregeln.Namensschluessel(alt);
        return index.TryGetValue(schluessel, out var id) && id == k.KontoId ? index.Remove(schluessel) : index;
    }

    /// <summary>Sitzung: Konto AKTIV, Gerät gehört dazu und ist FREIGEGEBEN. Admin-Werkzeug: immer, ohne Konto.</summary>
    private static bool AkteurPruefen(Kontenstand s, Akteur akteur, out Konto? konto)
    {
        konto = null;
        if (akteur.IstAdminWerkzeug) return true;
        if (!s.Konten.TryGetValue(akteur.KontoId, out var k) || k.Status != KontoStatus.Aktiv) return false;
        if (!s.Geraete.TryGetValue(akteur.GeraetId, out var g) || g.KontoId != k.KontoId || g.Status != GeraetStatus.Freigegeben) return false;
        konto = k;
        return true;
    }

    private static Kontenstand MitKonto(Kontenstand s, Konto k) => s with { Konten = s.Konten.SetItem(k.KontoId, k) };

    private static Kontenstand MitGeraet(Kontenstand s, Geraet g) => s with
    {
        Geraete = s.Geraete.SetItem(g.GeraetId, g),
        NachFingerabdruck = s.NachFingerabdruck.SetItem(g.Fingerabdruck, g.GeraetId),
    };

    private static Kontenstand OhneGeraet(Kontenstand s, Geraet g)
    {
        var index = s.NachFingerabdruck.TryGetValue(g.Fingerabdruck, out var id) && id == g.GeraetId
            ? s.NachFingerabdruck.Remove(g.Fingerabdruck)
            : s.NachFingerabdruck;
        return s with
        {
            Geraete = s.Geraete.Remove(g.GeraetId),
            NachFingerabdruck = index,
            GeraeteMitSitzung = s.GeraeteMitSitzung.Remove(g.GeraetId),
        };
    }

    private static Kontenstand Ereignis(Kontenstand s, uint kontoId, DateTimeOffset jetzt) =>
        s.Konten.TryGetValue(kontoId, out var k) ? MitKonto(s, k with { LetztesGeraeteereignis = jetzt }) : s;

    private static string Namensvorschlag(string? name, string benutzername) =>
        Zeichenregeln.IstText(name, 1, 32) ? name! : benutzername;

    /// <summary>Eindeutig nach NFKC und <c>ToUpperInvariant</c>; bei Kollision " (2)", " (3)" usw., gekürzt auf 32.</summary>
    private static string EindeutigerName(Kontenstand s, string wunsch, uint kontoId)
    {
        if (IstFrei(s, wunsch, kontoId)) return wunsch;
        for (var i = 2; ; i++)
        {
            var zusatz = string.Create(CultureInfo.InvariantCulture, $" ({i})");
            var kandidat = Zeichenregeln.Kuerzen(wunsch, 32 - zusatz.Length) + zusatz;
            if (IstFrei(s, kandidat, kontoId)) return kandidat;
        }
    }

    private static bool IstFrei(Kontenstand s, string name, uint kontoId) =>
        !s.NachNamensschluessel.TryGetValue(Zeichenregeln.Namensschluessel(name), out var id) || id == kontoId;

    private static Kontenstand NameSetzenIntern(Kontenstand s, Konto k, string name)
    {
        var alterSchluessel = Zeichenregeln.Namensschluessel(k.Name);
        var index = s.NachNamensschluessel.TryGetValue(alterSchluessel, out var id) && id == k.KontoId
            ? s.NachNamensschluessel.Remove(alterSchluessel)
            : s.NachNamensschluessel;
        return MitKonto(s with { NachNamensschluessel = index.SetItem(Zeichenregeln.Namensschluessel(name), k.KontoId) }, k with { Name = name });
    }

    private static Kontenstand KontoAnlegenIntern(
        Kontenstand s, string discordId, string benutzername, string? name, Stufe stufe, DateTimeOffset jetzt, out Konto konto)
    {
        var id = s.NaechsteKontoId;
        var eindeutig = EindeutigerName(s, Namensvorschlag(name, benutzername), id);
        konto = new Konto(id, discordId, benutzername, eindeutig, stufe, Rechte.Keine, Rechte.Keine, KontoStatus.Aktiv,
            Sperrgrund.Keiner, jetzt);
        return s with
        {
            Konten = s.Konten.Add(id, konto),
            NachDiscordId = s.NachDiscordId.SetItem(discordId, id),
            NachNamensschluessel = s.NachNamensschluessel.SetItem(Zeichenregeln.Namensschluessel(eindeutig), id),
            NaechsteKontoId = id + 1,
        };
    }

    /// <summary>
    /// Löscht Konto und Geräte vollständig: Fingerabdrücke, Namen, Discord-ID (3.6). Seit 0.7.0 (Sarevoc, 04.10.2026: Löschen heißt
    /// vollständig entfernen) kommt dabei nichts auf die Sperrliste; bis 0.6.x blieben dort die Fingerabdrücke abgelehnter und
    /// entzogener Geräte. Eine bewusste Sperre (ENTZOGEN, <see cref="GeraetEntfernen"/>) steht schon darauf und bleibt.
    /// </summary>
    private static Kontenstand KontoEntfernenIntern(Kontenstand s, Konto k, Trenngrund grund, List<Wirkung> w)
    {
        foreach (var g in s.GeraeteVon(k.KontoId)) s = OhneGeraet(s, g);
        var schluessel = Zeichenregeln.Namensschluessel(k.Name);
        var namen = s.NachNamensschluessel.TryGetValue(schluessel, out var id) && id == k.KontoId
            ? s.NachNamensschluessel.Remove(schluessel)
            : s.NachNamensschluessel;
        w.Add(new SitzungenBeenden(k.KontoId, null, grund));
        w.Add(new KontoGeloescht(k.KontoId));
        s = MitScNamen(s, k.KontoId, k.ScNamen, []);
        return s with
        {
            Konten = s.Konten.Remove(k.KontoId),
            NachDiscordId = k.DiscordId is { } d ? s.NachDiscordId.Remove(d) : s.NachDiscordId,
            NachNamensschluessel = namen,
            NachDiscordName = OhneDiscordName(s.NachDiscordName, k),
        };
    }

    private sealed record OffeneVerknuepfung(
        CodeBindung Bindung, string DiscordId, string Benutzername, string? Name, Herkunft Herkunft, Stufe Stufe, DateTimeOffset Frist);
}
