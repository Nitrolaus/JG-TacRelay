// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>Konto, Registrierung, Freigabe und Lebenszyklus (3.3 bis 3.6) mit simulierter Uhr: P-11, P-12, P-19, P-45, P-48.</summary>
internal static class IdentitaetLebenszyklusTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static Fingerabdruck Fp(int n) => new(Enumerable.Repeat((byte)n, 32).ToArray());

    private static T Eine<T>(Ergebnis e) where T : Wirkung => e.Wirkungen.OfType<T>().Single();

    private sealed class Welt
    {
        private long _verbindung = 100;

        public Welt() => Buch = new Kontenbuch(Uhr);

        public IdentitaetsUhr Uhr { get; } = new(Start);

        public Kontenbuch Buch { get; }

        public long NeueVerbindung() => ++_verbindung;

        public Verknuepfungsanfrage Anfragen(long verbindung, Fingerabdruck fp, string discordId, Herkunft herkunft = Herkunft.Bot, Stufe stufe = Stufe.Funker) =>
            Buch.VerknuepfungAnfragen(new CodeBindung(verbindung, fp, "Rechner " + discordId, SchluesselArt.Software),
                discordId, "nutzer" + discordId, "Name " + discordId, herkunft, stufe);

        public Verknuepfungsergebnis Verknuepfen(Fingerabdruck fp, string discordId, Herkunft herkunft = Herkunft.Bot, Stufe stufe = Stufe.Funker)
        {
            var v = NeueVerbindung();
            var a = Anfragen(v, fp, discordId, herkunft, stufe);
            return a.Ok ? Buch.VerknuepfungBeantworten(v, true) : new Verknuepfungsergebnis(a.Fehler, 0, 0, null, null, []);
        }

        /// <summary>Verknüpft und gibt über das Admin-Werkzeug frei; liefert die Sitzung des Geräts.</summary>
        public Akteur Freigegeben(Fingerabdruck fp, string discordId, Stufe stufe = Stufe.Funker)
        {
            var e = Verknuepfen(fp, discordId, stufe == Stufe.Funker ? Herkunft.Bot : Herkunft.AdminWerkzeug, stufe);
            Pruefe.Wahr(e.Ok, $"Verknüpfung {discordId}: {e.Fehler}");
            Pruefe.Wahr(Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, e.GeraetId, Entscheidung.Freigeben).Ok, $"Freigabe {discordId}");
            // Die Verwaltungsrechte wirken nur in einer Admin-Sitzung mit frischer Bestätigung (3.9); diese Tests prüfen die
            // Regeln aus 3.3 bis 3.6 dahinter. Ohne Admin-Sitzung: AdminSitzungTests.
            return Akteur.Verwaltend(e.KontoId, e.GeraetId, bestaetigt: true);
        }

        public Geraet Geraet(uint id) => Buch.Stand.Geraete[id];

        public Konto Konto(uint id) => Buch.Stand.Konten[id];
    }

    // ---------------------------------------------------------------- 3.4 Registrierung

    [Test]
    public static void Registrierung_EndeZuEndeKeinWegZuFreigegeben()
    {
        var w = new Welt();
        var register = new EinmalcodeRegister(w.Uhr);
        var fp = Fp(1);
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(fp).Art, "unbekannt: REGISTRIERUNG");

        var code = register.Ausstellen(7, fp, "Sarevocs Rechner", SchluesselArt.Tpm, "198.51.100.7").Code!;
        var treffer = register.Einloesen(code.Replace('-', ' '), "1001");
        Pruefe.Wahr(treffer.Ok, "Code trifft");
        var anfrage = w.Buch.VerknuepfungAnfragen(treffer.Bindung!, "1001", "test", "Test", Herkunft.Bot);
        Pruefe.Wahr(anfrage.Ok, "Rückfrage angelegt");
        Pruefe.Gleich("Test", anfrage.KontoName, "Name in der Rückfrage");
        Pruefe.Gleich("test", anfrage.Benutzername, "Benutzername in der Rückfrage");
        Pruefe.Wahr(anfrage.Neu, "neues Konto");
        Pruefe.Gleich(Start + TimeSpan.FromSeconds(120), anfrage.Frist, "Frist 120 s");
        Pruefe.Gleich(0, w.Buch.Stand.Konten.Count, "vor dem Ja wird nichts angelegt");

        var e = w.Buch.VerknuepfungBeantworten(7, true);
        Pruefe.Wahr(e.Ok, "Ja");
        var konto = w.Konto(e.KontoId);
        var geraet = w.Geraet(e.GeraetId);
        Pruefe.Gleich(Stufe.Funker, konto.Stufe, "neues Konto ist FUNKER");
        Pruefe.Gleich("1001", konto.DiscordId, "Discord-ID");
        Pruefe.Gleich(GeraetStatus.Wartet, geraet.Status, "Gerät WARTET, nie direkt FREIGEGEBEN");
        Pruefe.Gleich("Sarevocs Rechner", geraet.Name, "Gerätename");
        Pruefe.Gleich(SchluesselArt.Tpm, geraet.SchluesselArt, "Schlüsselart laut Client");
        Pruefe.Gleich(fp.Anzeige, e.Anzeige, "Anzeige für den Bot");
        Pruefe.Gleich(GeraetStatusMeldung.Wartet, Eine<GeraetStatusGeaendert>(e).Status, "GeraetStatus{WARTET}");
        var dm = e.Wirkungen.OfType<BotNachricht>().Single(b => b.Ziel == BotZiel.Direktnachricht);
        Pruefe.Gleich("1001", dm.DiscordId, "DM an den Inhaber");
        Pruefe.Gleich($"Gerät Sarevocs Rechner ({fp.Anzeige}) wurde mit deinem Funkkonto verknüpft. Warst du das nicht, melde dich sofort bei einem Admin.",
            dm.Text, "Text der DM nach 3.4 Schritt 7");
        Pruefe.Wahr(e.Wirkungen.OfType<BotNachricht>().Any(b => b.Ziel == BotZiel.AdminKanal), "Meldung im Admin-Kanal");
        Pruefe.Gleich(EinstufungsArt.Wartet, w.Buch.Einstufen(fp).Art, "danach WARTERAUM");
        Pruefe.Falsch(w.Buch.Stand.Geraete.Values.Any(g => g.Status == GeraetStatus.Freigegeben), "kein Weg vom Code zu FREIGEGEBEN");
        Pruefe.Wahr(w.Buch.IstErstesGeraet(e.GeraetId), "erstes Gerät des Kontos");
    }

    [Test]
    public static void Registrierung_RueckfrageNeinFristVerbindungsende()
    {
        var w = new Welt();
        var vorher = w.Buch.Stand;
        w.Anfragen(1, Fp(1), "1001");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Anfragen(1, Fp(2), "1002").Fehler, "zweite Rückfrage auf derselben Verbindung");
        Pruefe.Gleich(Fehlercode.Abgelehnt, w.Buch.VerknuepfungBeantworten(1, false).Fehler, "nein: ABGELEHNT");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "nichts angelegt");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.VerknuepfungBeantworten(1, true).Fehler, "Rückfrage ist verbraucht");

        w.Anfragen(2, Fp(1), "1001");
        w.Uhr.Vor(TimeSpan.FromSeconds(119));
        w.Anfragen(3, Fp(3), "1003");
        w.Uhr.Vor(TimeSpan.FromSeconds(1));
        Pruefe.Gleich(Fehlercode.Frist, w.Buch.VerknuepfungBeantworten(2, true).Fehler, "Ja nach 120 s: FRIST");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "auch nach Frist nichts angelegt");

        w.Uhr.Vor(TimeSpan.FromSeconds(119));
        var verfallen = w.Buch.FristenAnwenden().OfType<VerknuepfungVerfallen>().Single();
        Pruefe.Gleich(3L, verfallen.Verbindung, "Frist ohne Antwort meldet FRIST an den Bot");

        w.Anfragen(4, Fp(1), "1001");
        Pruefe.Wahr(w.Buch.VerbindungBeendet(4), "Verbindung zu: Rückfrage fort");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.VerknuepfungBeantworten(4, true).Fehler, "danach unbekannt");
    }

    [Test]
    public static void Registrierung_PrivilegiertNurAdminWerkzeug()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Verknuepfen(Fp(2), "9001").Fehler, "ADMIN über den Bot");
        var zweites = w.Verknuepfen(Fp(2), "9001", Herkunft.AdminWerkzeug);
        Pruefe.Wahr(zweites.Ok, "ADMIN über das Admin-Werkzeug");
        Pruefe.Gleich(GeraetStatus.Wartet, w.Geraet(zweites.GeraetId).Status, "auch dort nur WARTET");

        var funker = w.Freigegeben(Fp(3), "1001");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(admin, funker.KontoId, Rechte.GeraeteFreigeben).Fehler, "Verwaltungsrecht als Einzelrecht nie im Client (3.9)");
        Pruefe.Wahr(w.Buch.RechteSetzen(Akteur.AdminWerkzeug, funker.KontoId, Rechte.GeraeteFreigeben).Ok, "Funker bekommt GERAETE_FREIGEBEN an der Konsole");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Verknuepfen(Fp(4), "1001").Fehler, "privilegiert durch Einzelrecht");

        var rl = w.Verknuepfen(Fp(5), "2001", Herkunft.AdminWerkzeug, Stufe.Raidleiter);
        Pruefe.Wahr(w.Verknuepfen(Fp(6), "2001").Ok, "RAIDLEITER über den Bot erlaubt");
        Pruefe.Wahr(w.Buch.BrauchtRueckfrageBeimInhaber(rl.GeraetId), "Freigabeansicht verlangt Rückfrage beim Inhaber");
        Pruefe.Falsch(w.Buch.BrauchtRueckfrageBeimInhaber(funker.GeraetId), "bei FUNKER nicht");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Anfragen(50, Fp(7), "3001", Herkunft.Bot, Stufe.Admin).Fehler, "Bot legt nie höhere Stufe an");
    }

    [Test]
    public static void Registrierung_GesperrtLimitUndVerfall()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var f = w.Freigegeben(Fp(2), "1001");
        Pruefe.Wahr(w.Verknuepfen(Fp(3), "1001").Ok, "Gerät 2");
        var drittes = w.Verknuepfen(Fp(4), "1001");
        Pruefe.Wahr(drittes.Ok, "Gerät 3");
        var vorher = w.Buch.Stand;
        Pruefe.Gleich(Fehlercode.GeraeteLimit, w.Verknuepfen(Fp(5), "1001").Fehler, "Gerät 4: GERAETE_LIMIT");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "Stand unverändert");

        Pruefe.Wahr(w.Buch.GeraetEntfernen(f, drittes.GeraetId, false, "").Ok, "jüngeres Gerät entfernt");
        Pruefe.Wahr(w.Verknuepfen(Fp(5), "1001").Ok, "ENTFERNT zählt nicht zum Limit");
        Pruefe.Gleich(Fehlercode.GeraeteLimit, w.Verknuepfen(Fp(6), "1001").Fehler, "wieder voll");
        w.Uhr.Vor(TimeSpan.FromHours(24));
        Pruefe.Wahr(w.Verknuepfen(Fp(6), "1001").Ok, "verfallene WARTET zählen nicht mehr, auch ohne Aufräumlauf");

        Pruefe.Wahr(w.Buch.KontoSperren(admin, f.KontoId).Ok, "gesperrt");
        Pruefe.Gleich(Fehlercode.KontoGesperrt, w.Verknuepfen(Fp(7), "1001").Fehler, "gesperrtes Konto: KONTO_GESPERRT");
    }

    [Test]
    public static void Registrierung_BekannteFingerabdruecke()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var a = w.Freigegeben(Fp(2), "1001");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Verknuepfen(Fp(2), "1002").Fehler, "freigegebener Fingerabdruck");
        Pruefe.Wahr(w.Buch.GeraetEntfernen(a, a.GeraetId, false, "").Ok, "selbst entfernt");
        var neu = w.Verknuepfen(Fp(2), "1002");
        Pruefe.Wahr(neu.Ok, "ENTFERNT darf sich neu registrieren, auch an ein anderes Konto");
        Pruefe.Falsch(w.Buch.Stand.Geraete.ContainsKey(a.GeraetId), "alter Eintrag ersetzt");
        Pruefe.Gleich(neu.GeraetId, w.Buch.Stand.NachFingerabdruck[Fp(2)], "Index zeigt auf den neuen Eintrag");

        Pruefe.Wahr(w.Buch.GeraetEntfernen(admin, neu.GeraetId, true, "gestohlen").Ok, "entzogen");
        Pruefe.Gleich(Fehlercode.GeraetGesperrt, w.Verknuepfen(Fp(2), "1003").Fehler, "ENTZOGEN: GERAET_GESPERRT");
    }

    // ---------------------------------------------------------------- 4.6 Einstufung

    [Test]
    public static void Einstufen_AlleFaelle()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(9)).Art, "unbekannt");
        var wartet = w.Verknuepfen(Fp(2), "1001");
        var e = w.Buch.Einstufen(Fp(2));
        Pruefe.Gleich(EinstufungsArt.Wartet, e.Art, "WARTET");
        Pruefe.Gleich(wartet.KontoId, e.KontoId, "Konto für Welcome");
        Pruefe.Gleich(EinstufungsArt.Freigegeben, w.Buch.Einstufen(Fp(1)).Art, "FREIGEGEBEN, Konto AKTIV");

        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, wartet.GeraetId, Entscheidung.Ablehnen).Ok, "abgelehnt");
        // Seit 0.7.0 löscht Ablehnen das Gerät, gesperrt wird nichts (Sarevoc 04.10.2026): Es ist wieder unbekannt und darf neu beantragen.
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(2)).Art, "ABGELEHNT: gelöscht, wie unbekannt");

        var f = w.Freigegeben(Fp(3), "1002");
        Pruefe.Wahr(w.Buch.KontoSperren(admin, f.KontoId).Ok, "Konto gesperrt");
        Pruefe.Gleich(EinstufungsArt.KontoGesperrt, w.Buch.Einstufen(Fp(3)).Art, "Konto GESPERRT");
        Pruefe.Wahr(w.Buch.KontoEntsperren(admin, f.KontoId).Ok, "entsperrt");
        Pruefe.Gleich(EinstufungsArt.Freigegeben, w.Buch.Einstufen(Fp(3)).Art, "wieder bekannt");

        Pruefe.Wahr(w.Buch.GeraetEntfernen(f, f.GeraetId, false, "").Ok, "selbst entfernt");
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(3)).Art, "ENTFERNT: wie unbekannt");
        var g = w.Freigegeben(Fp(4), "1003");
        Pruefe.Wahr(w.Buch.GeraetEntfernen(admin, g.GeraetId, true, "").Ok, "entzogen");
        Pruefe.Gleich(EinstufungsArt.GeraetGesperrt, w.Buch.Einstufen(Fp(4)).Art, "ENTZOGEN");
    }

    // ---------------------------------------------------------------- 3.6 Fristen

    [Test]
    public static void Frist_WartetVerfaelltNach24h()
    {
        var w = new Welt();
        var e = w.Verknuepfen(Fp(1), "1001");
        w.Uhr.Vor(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        Pruefe.Gleich(0, w.Buch.FristenAnwenden().Count, "nach 23:59:59 noch da");
        Pruefe.Wahr(w.Buch.Stand.Geraete.ContainsKey(e.GeraetId), "Gerät vorhanden");
        w.Uhr.Vor(TimeSpan.FromSeconds(1));
        var wirkungen = w.Buch.FristenAnwenden();
        Pruefe.Gleich(e.GeraetId, wirkungen.OfType<GeraetGeloescht>().Single().GeraetId, "nach 24 h gelöscht");
        Pruefe.Falsch(w.Buch.Stand.Geraete.ContainsKey(e.GeraetId), "fort");
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(1)).Art, "danach unbekannt");
        Pruefe.Gleich(w.Uhr.GetUtcNow(), w.Konto(e.KontoId).LetztesGeraeteereignis, "Verfall ist ein Geräteereignis");
    }

    [Test]
    public static void Frist_Ruhefrist90Tage()
    {
        var w = new Welt();
        var a = w.Freigegeben(Fp(1), "1001");
        w.Buch.SitzungBegonnen(a.GeraetId);
        w.Uhr.Vor(TimeSpan.FromHours(2));
        w.Buch.SitzungBeendet(a.GeraetId);
        w.Uhr.Vor(TimeSpan.FromDays(90) - TimeSpan.FromSeconds(1));
        w.Buch.FristenAnwenden();
        Pruefe.Gleich(GeraetStatus.Freigegeben, w.Geraet(a.GeraetId).Status, "89 Tage 23:59:59 nach der letzten Sitzung");
        w.Uhr.Vor(TimeSpan.FromSeconds(1));
        var wirkung = w.Buch.FristenAnwenden().OfType<GeraetStatusGeaendert>().Single();
        Pruefe.Gleich(GeraetStatusMeldung.Wartet, wirkung.Status, "nach 90 Tagen zurück auf WARTET");
        Pruefe.Gleich(Kontenbuch.RuhefristGrund, wirkung.Grund, "mit Grund");
        Pruefe.Wahr(w.Geraet(a.GeraetId).WartefristAb is null, "24-h-Frist läuft noch nicht");

        w.Uhr.Vor(TimeSpan.FromDays(10));
        w.Buch.FristenAnwenden();
        Pruefe.Gleich(GeraetStatus.Wartet, w.Geraet(a.GeraetId).Status, "ohne Verbindungsversuch bleibt es in WARTET");
        Pruefe.Gleich(EinstufungsArt.Wartet, w.Buch.Einstufen(Fp(1)).Art, "Verbindung landet im WARTERAUM");
        Pruefe.Gleich(w.Uhr.GetUtcNow(), w.Geraet(a.GeraetId).WartefristAb, "jetzt läuft die 24-h-Frist");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, a.GeraetId, Entscheidung.Freigeben).Ok, "neue Freigabe möglich");

        var b = w.Freigegeben(Fp(2), "1002");
        w.Buch.SitzungBegonnen(b.GeraetId);
        w.Uhr.Vor(TimeSpan.FromDays(120));
        w.Buch.FristenAnwenden();
        Pruefe.Gleich(GeraetStatus.Freigegeben, w.Geraet(b.GeraetId).Status, "laufende Sitzung schützt vor dem Rückfall");
    }

    [Test]
    public static void Frist_RueckfallDannVerfall()
    {
        var w = new Welt();
        var a = w.Freigegeben(Fp(1), "1001");
        w.Uhr.Vor(TimeSpan.FromDays(90));
        w.Buch.FristenAnwenden();
        w.Buch.Einstufen(Fp(1));
        w.Uhr.Vor(TimeSpan.FromHours(24));
        Pruefe.Gleich(a.GeraetId, w.Buch.FristenAnwenden().OfType<GeraetGeloescht>().Single().GeraetId, "24 h nach dem Verbindungsversuch verfallen");
    }

    [Test]
    public static void Frist_KontoLoeschenNach30Tagen()
    {
        var w = new Welt();
        var e = w.Verknuepfen(Fp(1), "1001");
        w.Uhr.Vor(TimeSpan.FromHours(24));
        w.Buch.FristenAnwenden();
        w.Uhr.Vor(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        w.Buch.FristenAnwenden();
        Pruefe.Wahr(w.Buch.Stand.Konten.ContainsKey(e.KontoId), "29 Tage 23:59:59 nach dem Verfall noch da");
        w.Uhr.Vor(TimeSpan.FromSeconds(1));
        var wirkungen = w.Buch.FristenAnwenden();
        Pruefe.Gleich(e.KontoId, wirkungen.OfType<KontoGeloescht>().Single().KontoId, "Konto samt Pegeln, Ablage und Plätzen löschen");
        Pruefe.Falsch(w.Buch.Stand.NachDiscordId.ContainsKey("1001"), "Discord-ID frei");

        var f = w.Freigegeben(Fp(2), "1002");
        w.Buch.SitzungBegonnen(f.GeraetId);
        Pruefe.Wahr(w.Buch.KontoAnlegen(Akteur.AdminWerkzeug, "9001", "test", "Test", Stufe.Admin).Ok, "Admin ohne Gerät");
        w.Uhr.Vor(TimeSpan.FromDays(400));
        w.Buch.FristenAnwenden();
        Pruefe.Wahr(w.Buch.Stand.Konten.ContainsKey(f.KontoId), "Konto mit freigegebenem Gerät bleibt");
        Pruefe.Wahr(w.Buch.Stand.NachDiscordId.ContainsKey("9001"), "der letzte aktive ADMIN wird nie automatisch gelöscht");
    }

    // ---------------------------------------------------------------- 3.5 Freigabe

    [Test]
    public static void Freigabe_NurMitRechtUndNieFuerPrivilegierte()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var funker = w.Freigegeben(Fp(2), "1001");
        var rl = w.Freigegeben(Fp(3), "2001", Stufe.Raidleiter);
        var wartet = w.Verknuepfen(Fp(4), "1002");

        var vorher = w.Buch.Stand;
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(funker, wartet.GeraetId, Entscheidung.Freigeben).Fehler, "FUNKER");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(rl, wartet.GeraetId, Entscheidung.Freigeben).Fehler, "RAIDLEITER ohne GERAETE_FREIGEBEN");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(funker, 999, Entscheidung.Ablehnen).Fehler, "ohne Recht kein Hinweis auf Existenz");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "P-45: Zustand vorher gleich nachher");

        var ok = w.Buch.GeraetEntscheiden(admin, wartet.GeraetId, Entscheidung.Freigeben);
        Pruefe.Wahr(ok.Ok, "ADMIN gibt frei");
        var push = Eine<GeraetStatusGeaendert>(ok);
        Pruefe.Gleich(GeraetStatusMeldung.Freigegeben, push.Status, "GeraetStatus{FREIGEGEBEN}");
        Pruefe.Gleich(admin.KontoId, push.Durch, "durch");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.GeraetEntscheiden(admin, wartet.GeraetId, Entscheidung.Freigeben).Fehler, "nur aus WARTET");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.GeraetEntscheiden(admin, 999, Entscheidung.Freigeben).Fehler, "unbekannte ID");

        var adminZwei = w.Verknuepfen(Fp(5), "9002", Herkunft.AdminWerkzeug, Stufe.Admin);
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.GeraetEntscheiden(admin, adminZwei.GeraetId, Entscheidung.Freigeben).Fehler, "privilegiertes Konto im Client");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, adminZwei.GeraetId, Entscheidung.Freigeben).Ok, "über das Admin-Werkzeug");

        var nochEins = w.Verknuepfen(Fp(6), "1003");
        var ab = w.Buch.GeraetEntscheiden(admin, nochEins.GeraetId, Entscheidung.Ablehnen);
        Pruefe.Gleich(Trenngrund.GeraetAbgelehnt, Eine<SitzungenBeenden>(ab).Grund, "Ablehnung trennt");
        // Seit 0.7.0 ist das abgelehnte Gerät fort und nicht gesperrt; den Push baut der Server aus dem Stand davor.
        Pruefe.Falsch(w.Buch.Stand.Geraete.ContainsKey(nochEins.GeraetId), "ABGELEHNT: Gerät gelöscht");
        Pruefe.Falsch(w.Buch.Stand.GesperrteFingerabdruecke.Contains(Fp(6)), "ABGELEHNT sperrt nichts");
        var abPush = Eine<GeraetStatusGeaendert>(ab);
        Pruefe.Gleich(GeraetStatusMeldung.Abgelehnt, abPush.Status, "GeraetStatus{ABGELEHNT}");
        Pruefe.Wahr(abPush.Vorher is { } davor && davor.Geraete.ContainsKey(nochEins.GeraetId), "Push aus dem Stand davor");
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(6)).Art, "darf neu beantragen");

        Pruefe.Wahr(w.Buch.KontoSperren(Akteur.AdminWerkzeug, admin.KontoId).Ok, "Admin gesperrt");
        var dritter = w.Verknuepfen(Fp(7), "1004");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(admin, dritter.GeraetId, Entscheidung.Freigeben).Fehler, "gesperrter Admin handelt nicht mehr");
    }

    [Test]
    public static void Freigabe_GeschwisterNurMarkieren()
    {
        var w = new Welt();
        var a = w.Freigegeben(Fp(1), "1001");
        var b = w.Verknuepfen(Fp(2), "1001");
        var fremd = w.Freigegeben(Fp(3), "1002");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(a, b.GeraetId, Entscheidung.Freigeben).Fehler, "Geschwister gibt nicht frei");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(a, b.GeraetId, Entscheidung.Ablehnen).Fehler, "Geschwister lehnt nicht ab");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(fremd, b.GeraetId, Entscheidung.Bestaetigen).Fehler, "fremdes Konto markiert nicht");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(Akteur.Sitzung(b.KontoId, b.GeraetId), b.GeraetId, Entscheidung.Bestaetigen).Fehler,
            "wartendes Gerät handelt nicht");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, b.GeraetId, Entscheidung.Einspruch).Fehler, "Admin-Werkzeug markiert nicht");

        Pruefe.Wahr(w.Buch.GeraetEntscheiden(a, b.GeraetId, Entscheidung.Bestaetigen).Ok, "BESTAETIGEN");
        Pruefe.Gleich(Markierung.Bestaetigt, w.Geraet(b.GeraetId).Markierung, "markiert");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(a, b.GeraetId, Entscheidung.Einspruch).Ok, "EINSPRUCH");
        Pruefe.Gleich(Markierung.Einspruch, w.Geraet(b.GeraetId).Markierung, "Einspruch vermerkt");
        Pruefe.Gleich(GeraetStatus.Wartet, w.Geraet(b.GeraetId).Status, "Status bleibt WARTET");
        Pruefe.Falsch(w.Buch.IstErstesGeraet(b.GeraetId), "nicht das erste Gerät");
    }

    // ---------------------------------------------------------------- 3.6 Entfernen und Sperren

    [Test]
    public static void Entfernen_SelbstJuengerAelterAntrag()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(9), "9001", Stufe.Admin);
        var alt = w.Freigegeben(Fp(1), "1001");
        w.Uhr.Vor(TimeSpan.FromMinutes(1));
        var jung = w.Freigegeben(Fp(2), "1001");

        var antrag = w.Buch.GeraetEntfernen(jung, alt.GeraetId, false, "altes Notebook");
        Pruefe.Gleich(GeraetStatusMeldung.Beantragt, antrag.Status, "jüngeres gegen älteres: nur BEANTRAGT");
        Pruefe.Gleich("GERAET_ANTRAG", Eine<Meldung>(antrag).Art, "Meldung an Admins und Inhaber");
        Pruefe.Gleich(GeraetStatus.Freigegeben, w.Geraet(alt.GeraetId).Status, "älteres bleibt freigegeben");
        Pruefe.Gleich(jung.GeraetId, w.Geraet(alt.GeraetId).AntragVon, "Antrag vermerkt");

        Pruefe.Wahr(w.Buch.AntragZurueckweisen(admin, alt.GeraetId).Ok, "Admin weist zurück");
        Pruefe.Wahr(w.Geraet(alt.GeraetId).AntragVon is null, "Antrag fort");
        w.Buch.GeraetEntfernen(jung, alt.GeraetId, false, "");
        var streit = w.Buch.GeraetEntfernen(alt, jung.GeraetId, false, "Streit");
        Pruefe.Gleich(GeraetStatusMeldung.Entfernt, streit.Status, "älteres gegen jüngeres: sofort ENTFERNT");
        Pruefe.Gleich(Trenngrund.GeraetEntfernt, Eine<SitzungenBeenden>(streit).Grund, "laufende Sitzung endet");
        Pruefe.Gleich(GeraetStatus.Entfernt, w.Geraet(jung.GeraetId).Status, "nicht dauerhaft");

        var selbst = w.Buch.GeraetEntfernen(alt, alt.GeraetId, false, "");
        Pruefe.Gleich(GeraetStatusMeldung.Entfernt, selbst.Status, "selbst entfernen");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.GeraetEntfernen(admin, alt.GeraetId, false, "").Fehler, "schon entfernt");

        var x = w.Freigegeben(Fp(3), "1002");
        var y = w.Freigegeben(Fp(4), "1003");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntfernen(x, y.GeraetId, false, "").Fehler, "fremdes Gerät ohne Recht");
        Pruefe.Gleich(GeraetStatusMeldung.Entfernt, w.Buch.GeraetEntfernen(admin, y.GeraetId, false, "Verlust").Status, "Admin entfernt ein fremdes Gerät");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.GeraetEntfernen(x, x.GeraetId, false, new string('g', 41)).Fehler, "Grund über 40");
    }

    [Test]
    public static void Entzug_DauerhaftNurMitGeraeteFreigeben()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var adminZweitgeraet = w.Verknuepfen(Fp(2), "9001", Herkunft.AdminWerkzeug);
        var f = w.Freigegeben(Fp(3), "1001");
        var rl = w.Freigegeben(Fp(4), "2001", Stufe.Raidleiter);

        var vorher = w.Buch.Stand;
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntfernen(f, f.GeraetId, true, "").Fehler, "FUNKER sperrt nicht dauerhaft, auch nicht sich selbst");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntfernen(rl, f.GeraetId, true, "").Fehler, "RAIDLEITER ohne GERAETE_FREIGEBEN");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntfernen(admin, adminZweitgeraet.GeraetId, true, "").Fehler, "Geschwister nie dauerhaft über den Client (T6)");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "ohne Seiteneffekt");

        Pruefe.Wahr(w.Buch.RechteSetzen(Akteur.AdminWerkzeug, rl.KontoId, Rechteregeln.Standard(Stufe.Raidleiter) | Rechte.GeraeteFreigeben).Ok, "RAIDLEITER + GERAETE_FREIGEBEN");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntfernen(rl, admin.GeraetId, true, "").Fehler, "Gerät eines ADMIN nur durch einen ADMIN");

        var entzug = w.Buch.GeraetEntfernen(admin, f.GeraetId, true, "Schlüssel kopiert");
        Pruefe.Gleich(GeraetStatusMeldung.Entzogen, entzug.Status, "ENTZOGEN");
        Pruefe.Gleich(Trenngrund.GeraetEntzogen, Eine<SitzungenBeenden>(entzug).Grund, "P-12: laufende Sitzung endet sofort");
        Pruefe.Gleich("Schlüssel kopiert", Eine<GeraetStatusGeaendert>(entzug).Grund, "Grund im Push");
        Pruefe.Wahr(w.Buch.Stand.GesperrteFingerabdruecke.Contains(Fp(3)), "Fingerabdruck dauerhaft gesperrt");

        Pruefe.Wahr(w.Buch.KontoLoeschen(admin, f.KontoId).Ok, "Konto gelöscht (DSGVO)");
        Pruefe.Gleich(EinstufungsArt.GeraetGesperrt, w.Buch.Einstufen(Fp(3)).Art, "gesperrter Fingerabdruck bleibt als Hashwert");
        Pruefe.Gleich(Fehlercode.GeraetGesperrt, w.Verknuepfen(Fp(3), "1005").Fehler, "und kann sich nicht neu verknüpfen");
        Pruefe.Gleich(GeraetStatusMeldung.Entzogen, w.Buch.GeraetEntfernen(Akteur.AdminWerkzeug, adminZweitgeraet.GeraetId, true, "").Status, "Admin-Werkzeug darf alles");
    }

    /// <summary>
    /// Sperre aufheben (seit 0.8.0, Nachtauftrag 05.10.2026): dieselben Rechte wie beim Sperren, frischer Code. Danach ist das Gerät
    /// ENTFERNT, der Fingerabdruck frei, und der PC kann neu beantragen; von selbst ist er nicht freigegeben.
    /// </summary>
    [Test]
    public static void Entsperren_RechteWieSperren_DanachNeuerAntrag()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var adminZweitgeraet = w.Verknuepfen(Fp(2), "9001", Herkunft.AdminWerkzeug);
        var f = w.Freigegeben(Fp(3), "1001");
        var rl = w.Freigegeben(Fp(4), "2001", Stufe.Raidleiter);
        var opfer = w.Freigegeben(Fp(5), "1002");
        var admin2 = w.Freigegeben(Fp(6), "9002", Stufe.Admin);
        Pruefe.Wahr(w.Buch.GeraetEntfernen(admin, opfer.GeraetId, true, "gestohlen").Ok, "gesperrt");
        Pruefe.Wahr(w.Buch.GeraetEntfernen(Akteur.AdminWerkzeug, adminZweitgeraet.GeraetId, true, "").Ok, "Geschwister an der Konsole gesperrt");
        Pruefe.Wahr(w.Buch.GeraetEntfernen(Akteur.AdminWerkzeug, admin2.GeraetId, true, "").Ok, "Gerät eines anderen Admins an der Konsole gesperrt");

        var vorher = w.Buch.Stand;
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntsperren(f, opfer.GeraetId).Fehler, "FUNKER hebt keine Sperre auf");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntsperren(rl, opfer.GeraetId).Fehler, "RAIDLEITER ohne GERAETE_FREIGEBEN");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, w.Buch.GeraetEntsperren(Akteur.Sitzung(admin.KontoId, admin.GeraetId), opfer.GeraetId).Fehler,
            "ohne Admin-Sitzung");
        Pruefe.Gleich(Fehlercode.SchrittBestaetigen, w.Buch.GeraetEntsperren(Akteur.Verwaltend(admin.KontoId, admin.GeraetId, bestaetigt: false), opfer.GeraetId).Fehler,
            "ohne frischen Code");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntsperren(admin, adminZweitgeraet.GeraetId).Fehler, "Geschwister nie über den Client (T6)");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.GeraetEntsperren(admin, admin2.GeraetId).Fehler, "fremdes Admin-Konto nur an der Konsole");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.GeraetEntsperren(admin, 999).Fehler, "unbekanntes Gerät");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.GeraetEntsperren(admin, f.GeraetId).Fehler, "nicht gesperrt");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "abgewiesen: ohne Seiteneffekt");
        Pruefe.Wahr(w.Buch.RechteSetzen(Akteur.AdminWerkzeug, rl.KontoId, Rechteregeln.Standard(Stufe.Raidleiter) | Rechte.GeraeteFreigeben).Ok, "RAIDLEITER + GERAETE_FREIGEBEN");
        vorher = w.Buch.Stand;
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraetEntsperren(rl, admin2.GeraetId).Fehler, "Gerät eines ADMIN nur durch einen ADMIN");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "auch hier ohne Seiteneffekt");

        var auf = w.Buch.GeraetEntsperren(admin, opfer.GeraetId);
        Pruefe.Wahr(auf.Ok, "aufgehoben: " + auf.Fehler);
        Pruefe.Gleich(GeraetStatusMeldung.Entfernt, Eine<GeraetStatusGeaendert>(auf).Status, "Push: ENTFERNT");
        Pruefe.Gleich(GeraetStatus.Entfernt, w.Buch.Stand.Geraete[opfer.GeraetId].Status, "das Gerät gilt als entfernt");
        Pruefe.Falsch(w.Buch.Stand.GesperrteFingerabdruecke.Contains(Fp(5)), "Fingerabdruck nicht mehr gesperrt");
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(5)).Art, "der PC ist wie unbekannt, nicht freigegeben");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.GeraetEntsperren(admin, opfer.GeraetId).Fehler, "ein zweites Mal: nicht mehr gesperrt");
        var neu = w.Verknuepfen(Fp(5), "1006");
        Pruefe.Wahr(neu.Ok, "neuer Antrag möglich: " + neu.Fehler);
        Pruefe.Falsch(w.Buch.Stand.Geraete.ContainsKey(opfer.GeraetId), "der alte Eintrag ist ersetzt");

        Pruefe.Wahr(w.Buch.GeraetEntsperren(Akteur.AdminWerkzeug, admin2.GeraetId).Ok, "die Konsole darf auch Admin-Geräte");
        Pruefe.Gleich(GeraetStatus.Entfernt, w.Buch.Stand.Geraete[admin2.GeraetId].Status, "Admin-Gerät entfernt, nicht wieder freigegeben");
    }

    /// <summary>
    /// Eine Sperre ohne Gerät (seit 0.7.0 bleibt sie nach dem Löschen des Kontos stehen) hebt nur die Konsole auf (seit 0.8.0): Sie
    /// findet die Kennung über die Anzeigeform wie bei <c>sperren</c> und gibt den Fingerabdruck frei.
    /// </summary>
    [Test]
    public static void Entsperren_OhneGeraet_NurKonsole()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var f = w.Freigegeben(Fp(3), "1001");
        Pruefe.Wahr(w.Buch.GeraetEntfernen(admin, f.GeraetId, true, "").Ok, "gesperrt");
        Pruefe.Wahr(w.Buch.KontoLoeschen(admin, f.KontoId).Ok, "Konto gelöscht, die Sperre bleibt");
        Pruefe.Falsch(w.Buch.Stand.Geraete.ContainsKey(f.GeraetId), "kein Gerät mehr");

        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.FingerabdruckEntsperren(admin, Fp(3)).Fehler, "nicht über den Client");
        Pruefe.Gleich(Fehlercode.Keiner, w.Buch.SperreSuchen(Fp(3).Anzeige, out var gefunden), "über die Anzeigeform gefunden");
        Pruefe.Gleich(Fp(3), gefunden, "der richtige Fingerabdruck");
        Pruefe.Gleich(Fehlercode.Keiner, w.Buch.SperreSuchen(Fp(3).AlsBase64(), out _), "über den vollen Fingerabdruck");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.SperreSuchen(Fp(9).Anzeige, out _), "nicht gesperrte Kennung");

        Pruefe.Wahr(w.Buch.FingerabdruckEntsperren(Akteur.AdminWerkzeug, gefunden).Ok, "an der Konsole aufgehoben");
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(3)).Art, "wieder unbekannt");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.FingerabdruckEntsperren(Akteur.AdminWerkzeug, Fp(3)).Fehler, "ein zweites Mal: nicht gesperrt");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.SperreSuchen(Fp(3).Anzeige, out _), "nicht mehr in der Sperrliste");
        Pruefe.Wahr(w.Verknuepfen(Fp(3), "1005").Ok, "der PC kann neu beantragen");
    }

    // ---------------------------------------------------------------- 6.10 Kontoverwaltung

    [Test]
    public static void Verwaltung_RechteUndRang()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var f = w.Freigegeben(Fp(2), "1001");
        var f2 = w.Freigegeben(Fp(3), "1002");
        var rl = w.Freigegeben(Fp(4), "2001", Stufe.Raidleiter);
        var rlPlus = w.Freigegeben(Fp(5), "2002", Stufe.Raidleiter);
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(admin, rlPlus.KontoId, Rechteregeln.Standard(Stufe.Raidleiter) | Rechte.KontenVerwalten).Fehler, "Verwaltungsrecht nur an der Konsole");
        Pruefe.Wahr(w.Buch.RechteSetzen(Akteur.AdminWerkzeug, rlPlus.KontoId, Rechteregeln.Standard(Stufe.Raidleiter) | Rechte.KontenVerwalten).Ok, "RAIDLEITER + KONTEN_VERWALTEN");

        var vorher = w.Buch.Stand;
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.KontoSperren(f, f2.KontoId).Fehler, "FUNKER ohne KONTEN_VERWALTEN");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.KontoSperren(rl, f.KontoId).Fehler, "RAIDLEITER ohne KONTEN_VERWALTEN");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.KontoSperren(f, 999).Fehler, "ohne Recht kein Hinweis auf Existenz");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.KontoSperren(rlPlus, admin.KontoId).Fehler, "Regel 3: ADMIN nur durch ADMIN");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.NameSetzen(rlPlus, admin.KontoId, "Chef").Fehler, "Regel 3 auch für den Namen");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.StufeSetzen(rlPlus, rlPlus.KontoId, Stufe.Funker).Fehler, "Regel 2: nicht die eigene Stufe");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.RechteSetzen(rlPlus, rlPlus.KontoId, Rechteregeln.Standard(Stufe.Raidleiter)).Fehler, "Regel 2: nicht die eigenen Rechte");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(admin, admin.KontoId, Rechte.Keine).Fehler, "ADMIN-Konten nur an der Konsole, auch das eigene");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.RechteSetzen(rlPlus, f.KontoId, Rechte.NetzeVerwalten).Fehler, "Regel 1: kein Recht, das man nicht hat");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.StufeSetzen(rlPlus, f.KontoId, Stufe.Admin).Fehler, "nicht über die eigene Stufe hinaus");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoSperren(admin, admin.KontoId).Fehler, "Regel 4: letzter aktiver ADMIN");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoLoeschen(admin, admin.KontoId).Fehler, "Regel 4: letzter aktiver ADMIN");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.RechteSetzen(admin, f.KontoId, (Rechte)(1 << 12)).Fehler, "unbekanntes Recht");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "P-45: keine Seiteneffekte");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.KontoSperren(admin, 999).Fehler, "mit Recht: UNBEKANNT");

        var stufe = w.Buch.StufeSetzen(rlPlus, f.KontoId, Stufe.Raidleiter);
        Pruefe.Wahr(stufe.Ok, "RAIDLEITER + KONTEN_VERWALTEN stuft FUNKER zu RAIDLEITER");
        Pruefe.Gleich("RECHTE_GEAENDERT", Eine<Meldung>(stufe).Art, "Regel 6: Meldung");
        Pruefe.Wahr(stufe.Wirkungen.OfType<BotNachricht>().Any(b => b.Art == "RECHTE_GEAENDERT"), "Regel 6: Admin-Kanal");

        Pruefe.Wahr(w.Buch.KontoSperren(rlPlus, f2.KontoId).Ok, "RAIDLEITER + KONTEN_VERWALTEN sperrt FUNKER");
        var adminZwei = w.Freigegeben(Fp(6), "9002", Stufe.Admin);
        Pruefe.Wahr(w.Buch.KontoSperren(adminZwei, admin.KontoId).Ok, "nicht mehr der letzte: ADMIN sperrt ADMIN");
        Pruefe.Wahr(w.Buch.KontoSperren(Akteur.AdminWerkzeug, adminZwei.KontoId).Ok, "Admin-Werkzeug sperrt auch den letzten");
    }

    [Test]
    public static void Verwaltung_AbweichungenUndPrivilegierung()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var rl = w.Freigegeben(Fp(2), "2001", Stufe.Raidleiter);
        // Seit 0.3.0 hat RAIDLEITER kein ZUWEISEN; als Einzelrecht bleibt es möglich (fester Stellvertreter, 6.12).
        Pruefe.Falsch(Rechteregeln.Standard(Stufe.Raidleiter).HasFlag(Rechte.Zuweisen), "RAIDLEITER ohne ZUWEISEN");
        var neu = (Rechteregeln.Standard(Stufe.Raidleiter) & ~Rechte.MehrfachSenden) | Rechte.Zuweisen;
        Pruefe.Wahr(w.Buch.RechteSetzen(admin, rl.KontoId, neu).Ok, "plus ZUWEISEN, minus MEHRFACH_SENDEN");
        Pruefe.Gleich(Rechte.Zuweisen, w.Konto(rl.KontoId).Plus, "plus als sichtbare Abweichung");
        Pruefe.Gleich(Rechte.MehrfachSenden, w.Konto(rl.KontoId).Minus, "minus als sichtbare Abweichung");
        Pruefe.Wahr(w.Konto(rl.KontoId).Hat(Rechte.Zuweisen) && !w.Konto(rl.KontoId).Hat(Rechte.MehrfachSenden), "ZUWEISEN da, MEHRFACH_SENDEN fehlt");

        Pruefe.Wahr(w.Buch.StufeSetzen(admin, rl.KontoId, Stufe.Funker).Ok, "herabgestuft");
        Pruefe.Gleich(Rechte.Keine, w.Konto(rl.KontoId).Plus | w.Konto(rl.KontoId).Minus, "Regel 5: STUFE setzt Abweichungen zurück");
        Pruefe.Gleich(Rechte.Keine, w.Konto(rl.KontoId).Rechte, "Standard FUNKER");

        var wartend = w.Verknuepfen(Fp(3), "2001");
        Pruefe.Wahr(w.Buch.Stand.Geraete.ContainsKey(wartend.GeraetId), "wartendes Gerät da");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(admin, rl.KontoId, Rechte.GeraeteFreigeben).Fehler, "privilegieren nur an der Konsole");
        var priv = w.Buch.RechteSetzen(Akteur.AdminWerkzeug, rl.KontoId, Rechte.GeraeteFreigeben);
        Pruefe.Wahr(priv.Ok, "wird privilegiert");
        Pruefe.Gleich(Rechte.GeraeteFreigeben, w.Konto(rl.KontoId).Plus, "plus als sichtbare Abweichung");
        Pruefe.Gleich("GERAETE_FREIGEBEN", Rechteregeln.Drahtnamen(w.Konto(rl.KontoId).Rechte).Single(), "Drahtname");
        Pruefe.Falsch(w.Buch.Stand.Geraete.ContainsKey(wartend.GeraetId), "WARTET verfällt beim Privilegieren");
        Pruefe.Gleich(wartend.GeraetId, Eine<GeraetGeloescht>(priv).GeraetId, "als Wirkung");
        Pruefe.Wahr(w.Buch.Stand.Geraete.ContainsKey(rl.GeraetId), "freigegebene Geräte bleiben");
    }

    [Test]
    public static void Verwaltung_SperrenEntsperrenLoeschen()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var f = w.Freigegeben(Fp(2), "1001");
        var sperre = w.Buch.KontoSperren(admin, f.KontoId);
        var beenden = Eine<SitzungenBeenden>(sperre);
        Pruefe.Wahr(beenden.KontoId == f.KontoId && beenden.GeraetId is null, "P-12: alle Sitzungen des Kontos enden sofort");
        Pruefe.Gleich(Trenngrund.KontoGesperrt, beenden.Grund, "Fehler{KONTO_GESPERRT}");
        Pruefe.Gleich(Sperrgrund.Admin, w.Konto(f.KontoId).Sperrgrund, "Grund ADMIN");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraeteListe(f, null, false).Fehler, "gesperrtes Konto handelt nicht");
        Pruefe.Wahr(w.Buch.KontoEntsperren(admin, f.KontoId).Ok, "entsperrt");
        Pruefe.Gleich(KontoStatus.Aktiv, w.Konto(f.KontoId).Status, "AKTIV");

        var loeschen = w.Buch.KontoLoeschen(admin, f.KontoId);
        Pruefe.Gleich(f.KontoId, Eine<KontoGeloescht>(loeschen).KontoId, "Pegel, Ablage, Plätze folgen");
        Pruefe.Gleich(Trenngrund.KontoGeloescht, Eine<SitzungenBeenden>(loeschen).Grund, "Sitzungen enden");
        Pruefe.Falsch(w.Buch.Stand.Geraete.ContainsKey(f.GeraetId), "Geräte fort");
        var wieder = w.Verknuepfen(Fp(3), "1001");
        Pruefe.Wahr(wieder.Ok && wieder.KontoId != f.KontoId, "neues Konto mit neuer ID");

        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.KontoAnlegen(admin, "3001", "x", "X", Stufe.Funker).Fehler, "Konto anlegen nur im Admin-Werkzeug");
        Pruefe.Wahr(w.Buch.KontoAnlegen(Akteur.AdminWerkzeug, "3001", "x", "X", Stufe.Funker).Ok, "Admin-Werkzeug");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.KontoAnlegen(Akteur.AdminWerkzeug, "3001", "x", "X", Stufe.Funker).Fehler, "Discord-ID schon vergeben");
    }

    [Test]
    public static void Namen_EindeutigNachNormalisierungUndGross()
    {
        var w = new Welt();
        uint Anlegen(string id, string name) => w.Buch.KontoAnlegen(Akteur.AdminWerkzeug, id, "nutzer" + id, name, Stufe.Funker).KontoId;

        Pruefe.Gleich("Test", w.Konto(Anlegen("1001", "Test")).Name, "erster");
        Pruefe.Gleich("TEST (2)", w.Konto(Anlegen("1002", "TEST")).Name, "Großschreibung kollidiert");
        Pruefe.Gleich("test (3)", w.Konto(Anlegen("1003", "test")).Name, "nächste Nummer");
        var lang = new string('x', 32);
        Anlegen("1004", lang);
        Pruefe.Gleich(new string('x', 28) + " (2)", w.Konto(Anlegen("1005", lang)).Name, "gekürzt auf 32 Zeichen");
        Pruefe.Gleich("nutzer1006", w.Konto(Anlegen("1006", "Böse‮eviw")).Name, "Bidi-Zeichen: Benutzername als Name");
        Pruefe.Gleich("Jürgen", w.Konto(Anlegen("1007", "Jürgen")).Name, "Umlaute bleiben");

        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        Pruefe.Wahr(w.Buch.NameSetzen(admin, w.Buch.Stand.NachDiscordId["1003"], "Test").Ok, "Umbenennen");
        Pruefe.Gleich("Test (3)", w.Konto(w.Buch.Stand.NachDiscordId["1003"]).Name, "Kollision beim Umbenennen; (2) ist belegt, (3) ist der eigene Schlüssel");

        var vollbreit = w.Konto(Anlegen("1008", "Ｔｅｓｔ")).Name;
        if (Zeichenregeln.NormalisierungWirksam)
            Pruefe.Gleich("Ｔｅｓｔ (4)", vollbreit, "NFKC: Vollbreite kollidiert mit Test; (2) und (3) sind belegt");
        else
            Pruefe.Gleich("Ｔｅｓｔ", vollbreit, "InvariantGlobalization: NFKC wirkungslos (Bericht, offener Punkt)");
    }

    // ---------------------------------------------------------------- 3.7 Abgleich (Server-Seite)

    [Test]
    public static void Abgleich_SperrenAufhebenGrenzen()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var f = w.Freigegeben(Fp(2), "1001");

        var sperre = w.Buch.MitgliedschaftMelden("1001", Mitgliedschaftsstatus.NichtMitglied, "neuername", "Neuer Nick");
        Pruefe.Gleich(Trenngrund.KontoGesperrt, Eine<SitzungenBeenden>(sperre).Grund, "Sitzungen enden");
        Pruefe.Gleich("ABGLEICH", Eine<Meldung>(sperre).Art, "Meldung an KONTEN_VERWALTEN");
        var k = w.Konto(f.KontoId);
        Pruefe.Wahr(k.Status == KontoStatus.Gesperrt && k.Sperrgrund == Sperrgrund.Abgleich, "GESPERRT mit Grund ABGLEICH");
        Pruefe.Gleich("neuername", k.Benutzername, "Benutzername aktualisiert");
        Pruefe.Gleich("Name 1001", k.Name, "Name bleibt");
        Pruefe.Gleich("Neuer Nick", k.NamensvorschlagAbgleich, "name nur als Vorschlag");

        w.Buch.MitgliedschaftMelden("1001", Mitgliedschaftsstatus.Ok, null, null);
        Pruefe.Gleich(KontoStatus.Aktiv, w.Konto(f.KontoId).Status, "OK hebt die Abgleich-Sperre auf");

        w.Buch.KontoSperren(admin, f.KontoId);
        w.Buch.MitgliedschaftMelden("1001", Mitgliedschaftsstatus.Ok, null, null);
        Pruefe.Gleich(KontoStatus.Gesperrt, w.Konto(f.KontoId).Status, "Admin-Sperre bleibt");

        var adminMeldung = w.Buch.MitgliedschaftMelden("9001", Mitgliedschaftsstatus.RolleFehlt, null, null);
        Pruefe.Gleich(KontoStatus.Aktiv, w.Konto(admin.KontoId).Status, "ADMIN nie gesperrt");
        Pruefe.Gleich(0, adminMeldung.Wirkungen.OfType<SitzungenBeenden>().Count(), "nur Meldung");
        Pruefe.Wahr(w.Buch.MitgliedschaftMelden("7777", Mitgliedschaftsstatus.NichtMitglied, null, null).Wirkungen.Count == 0, "unbekannte ID: nichts");
    }

    [Test]
    public static void KompromittierterBot_P48()
    {
        var w = new Welt();
        var register = new EinmalcodeRegister(w.Uhr);
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var konten = Enumerable.Range(0, 6).Select(i => w.Freigegeben(Fp(10 + i), (1001 + i).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var echt = register.Ausstellen(1, Fp(30), "Rechner", SchluesselArt.Software, "p").Code!;

        var zufall = new Random(48);
        var treffer = 0;
        var grenzen = 0;
        for (var i = 0; i < 2000; i++)
        {
            var id = (7000 + zufall.Next(20)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var code = new string(Enumerable.Range(0, 12).Select(_ => Einmalcode.Alphabet[zufall.Next(32)]).ToArray());
            var e = register.Einloesen(code, id);
            if (e.Ok) treffer++;
            if (e.Fehler == Fehlercode.Grenze) grenzen++;
        }
        Pruefe.Gleich(0, treffer, "geratene Codes treffen nicht");
        Pruefe.Gleich(2000 - 20 * 5, grenzen, "je Discord-ID höchstens 5 Fehlversuche, dann GRENZE");
        Pruefe.Wahr(register.Einloesen(echt, "8001").Ok, "echter Code weiter nutzbar");

        var gesperrt = 0;
        foreach (var id in w.Buch.Stand.NachDiscordId.Keys.ToList())
        {
            var e = w.Buch.MitgliedschaftMelden(id, Mitgliedschaftsstatus.NichtMitglied, null, null);
            gesperrt += e.Wirkungen.OfType<SitzungenBeenden>().Count();
        }
        Pruefe.Gleich(Kontenbuch.AbgleichSperrenJeStunde, gesperrt, "höchstens 3 Sperren je Stunde");
        Pruefe.Gleich(KontoStatus.Aktiv, w.Konto(admin.KontoId).Status, "kein ADMIN gesperrt");
        Pruefe.Gleich(3, konten.Count(k => w.Konto(k.KontoId).Status == KontoStatus.Aktiv), "die übrigen bleiben aktiv");
        w.Uhr.Vor(TimeSpan.FromHours(1));
        var nochAktiv = w.Konto(konten.First(k => w.Konto(k.KontoId).Status == KontoStatus.Aktiv).KontoId).DiscordId!;
        var nachher = w.Buch.MitgliedschaftMelden(nochAktiv, Mitgliedschaftsstatus.NichtMitglied, null, null);
        Pruefe.Wahr(nachher.Wirkungen.OfType<SitzungenBeenden>().Any(), "nach einer Stunde wieder möglich");
        Pruefe.Falsch(w.Buch.Stand.Geraete.Values.Any(g => g.Status == GeraetStatus.Freigegeben && g.GeraetId > konten[^1].GeraetId), "kein Gerät vom Bot freigegeben");
    }

    // ---------------------------------------------------------------- Admin-Werkzeug und Auskunft

    [Test]
    public static void AdminWerkzeug_BootstrapUndSuche()
    {
        var w = new Welt();
        var register = new EinmalcodeRegister(w.Uhr);
        var fp = Fp(1);
        var code = register.Ausstellen(1, fp, "Admin-PC", SchluesselArt.Tpm, "192.168.1.5").Code!;
        var treffer = register.EinloesenUeberAdminWerkzeug(code);
        Pruefe.Wahr(w.Buch.VerknuepfungAnfragen(treffer.Bindung!, "9001", "test", "Test", Herkunft.AdminWerkzeug, Stufe.Admin).Ok, "funk-admin verknuepfen --stufe ADMIN");
        var e = w.Buch.VerknuepfungBeantworten(1, true);
        Pruefe.Wahr(e.Ok, "im Client bestätigt");
        Pruefe.Gleich(Stufe.Admin, w.Konto(e.KontoId).Stufe, "ADMIN-Konto");
        Pruefe.Gleich(GeraetStatus.Wartet, w.Geraet(e.GeraetId).Status, "auch hier erst WARTET");

        Pruefe.Gleich(Fehlercode.Keiner, w.Buch.GeraetSuchen(fp.Anzeige.ToUpperInvariant(), out var gefunden), "per Anzeigeform");
        Pruefe.Gleich(e.GeraetId, gefunden!.GeraetId, "richtiges Gerät");
        Pruefe.Gleich(Fehlercode.Keiner, w.Buch.GeraetSuchen(fp.AlsBase64(), out gefunden), "per Fingerabdruck");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, gefunden!.GeraetId, Entscheidung.Freigeben).Ok, "funk-admin freigeben");
        Pruefe.Gleich(EinstufungsArt.Freigegeben, w.Buch.Einstufen(fp).Art, "Admin ist AKTIV");

        var gleicherKopf = new byte[32];
        fp.Schreiben(gleicherKopf);
        gleicherKopf[31] ^= 0xFF;
        w.Verknuepfen(new Fingerabdruck(gleicherKopf), "1001");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.GeraetSuchen(fp.Anzeige, out _), "Anzeigeform mehrdeutig");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.GeraetSuchen("0000-0000-0000-0000", out _), "unbekannt");
    }

    [Test]
    public static void GeraeteListe_RechteUndFilter()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var f = w.Freigegeben(Fp(2), "1001");
        w.Verknuepfen(Fp(3), "1001");
        var fremd = w.Verknuepfen(Fp(4), "1002");

        var eigene = w.Buch.GeraeteListe(f, null, false);
        Pruefe.Gleich(2, eigene.Geraete.Count, "eigene Geräte");
        Pruefe.Wahr(eigene.Geraete.All(g => g.KontoId == f.KontoId), "nur eigene");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.GeraeteListe(f, fremd.KontoId, false).Fehler, "fremd nur mit GERAETE_FREIGEBEN");
        Pruefe.Gleich(4, w.Buch.GeraeteListe(admin, null, false).Geraete.Count, "Freigeber sieht alle");
        Pruefe.Gleich(2, w.Buch.GeraeteListe(admin, null, true).Geraete.Count, "nurWartend");
        Pruefe.Gleich(1, w.Buch.GeraeteListe(admin, fremd.KontoId, false).Geraete.Count, "je Konto");
        w.Uhr.Vor(TimeSpan.FromHours(24));
        Pruefe.Gleich(0, w.Buch.GeraeteListe(admin, null, true).Geraete.Count, "verfallene WARTET erscheinen nicht");
    }
}
