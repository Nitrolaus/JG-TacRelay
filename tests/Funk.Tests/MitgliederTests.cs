// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Anmeldung mit SC-Name und Discord-Name (3.4, A42), Namensraum ohne Besetzen durch offene Anträge, Freigabe mit
/// Vormerkung oder an ein vorhandenes Konto, Eigentümer und ADMIN-Konten im Client (3.9, 3.10), Admin-Sitzung und frische
/// Bestätigung als Regel der Bücher, Rollenkatalog, Vormerkungen und Lieblingsrollen (6.13); seit 0.11.8 ohne Untergilden, deren
/// gespeicherte Daten nur unverändert durchgereicht werden. Ohne Server.
/// </summary>
internal static class MitgliederTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static Fingerabdruck Fp(int n) => new(Enumerable.Repeat((byte)n, 32).ToArray());

    /// <summary>Kontenbuch und Mitgliederbuch wie in der Zentrale: Vormerkungen belegen Namen im Kontenbuch.</summary>
    private sealed class Welt
    {
        private long _verbindung = 900;

        public Welt()
        {
            Buch = new Kontenbuch(Uhr);
            Mitglieder = new Mitgliederbuch(Uhr);
            Buch.VormerkungsNamen = Mitglieder.NameBelegt;
            Buch.VormerkungsDiscordName = Mitglieder.DiscordNameVon;
        }

        public IdentitaetsUhr Uhr { get; } = new(Start);
        public Kontenbuch Buch { get; }
        public Mitgliederbuch Mitglieder { get; }
        public Kontenstand K => Buch.Stand;

        /// <summary>Konto über Code und Admin-Werkzeug, Gerät freigegeben; zurück kommt eine Admin-Sitzung mit frischem Code.</summary>
        public Akteur Freigegeben(Fingerabdruck fp, string discordId, Stufe stufe = Stufe.Funker)
        {
            var v = ++_verbindung;
            var herkunft = stufe == Stufe.Funker ? Herkunft.Bot : Herkunft.AdminWerkzeug;
            var a = Buch.VerknuepfungAnfragen(new CodeBindung(v, fp, "Rechner", SchluesselArt.Software), discordId, "n" + discordId, "Name " + discordId, herkunft, stufe);
            Pruefe.Wahr(a.Ok, "Anfrage " + discordId + ": " + a.Fehler);
            var e = Buch.VerknuepfungBeantworten(v, true);
            Pruefe.Wahr(e.Ok, "Verknüpfung " + discordId);
            Pruefe.Wahr(Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, e.GeraetId, Entscheidung.Freigeben).Ok, "Freigabe " + discordId);
            return Akteur.Verwaltend(e.KontoId, e.GeraetId, bestaetigt: true);
        }

        public Antragsergebnis Antrag(int fp, string sc, string discord)
        {
            var a = Buch.AntragStellen(Fp(fp), "PC " + fp, SchluesselArt.Software, sc, discord);
            return a;
        }

        public Konto Konto(uint id) => Buch.Stand.Konten[id];

        public void Katalog(params string[] namen) => Pruefe.Wahr(Mitglieder.KatalogSetzen(K, Akteur.AdminWerkzeug,
            [.. namen.Select((n, i) => new Katalogrolle((ushort)(i + 1), n))], null).Ok, "Katalog");
    }

    // ---------------------------------------------------------------- Antrag ohne Code und Namensraum (3.4, A42)

    [Test]
    public static void Antrag_BesetztKeinenNamen()
    {
        var w = new Welt();
        var erster = w.Antrag(1, "Sarevoc", "sarevoc");
        Pruefe.Wahr(erster.Ok, "Antrag");
        Pruefe.Gleich(GeraetStatus.Wartet, w.K.Geraete[erster.GeraetId].Status, "Gerät WARTET");
        Pruefe.Wahr(w.Konto(erster.KontoId).IstAntrag && w.Konto(erster.KontoId).DiscordId is null, "Antragskonto ohne Discord-ID");
        Pruefe.Wahr(erster.Wirkungen.OfType<Meldung>().Any(m => m.Art == "ANTRAG_NEU"), "Meldung an die Freigebenden");
        Pruefe.Falsch(w.K.NachScName.ContainsKey("SAREVOC"), "Name erst mit der Freigabe im Namensraum");
        // Angriffsfall "Namen besetzen": ein zweiter Antrag mit demselben Namen wird angenommen, nicht abgewiesen.
        var zweiter = w.Antrag(2, "ＳAREVOC ", "anderer");
        Pruefe.Wahr(zweiter.Ok, "zweiter Antrag mit demselben Namen (Breitschrift, andere Schreibung)");

        Pruefe.Wahr(w.Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, erster.GeraetId, Entscheidung.Freigeben).Ok, "ersten freigeben");
        var konto = w.Konto(erster.KontoId);
        Pruefe.Falsch(konto.IstAntrag, "kein Antrag mehr");
        Pruefe.Gleich("Sarevoc", konto.ScNamen.Single(), "SC-Name übernommen");
        Pruefe.Gleich("sarevoc", konto.DiscordName, "Discord-Name übernommen");
        Pruefe.Gleich(erster.KontoId, w.K.NachScName["SAREVOC"], "jetzt im Namensraum");
        Pruefe.Gleich("ＳAREVOC", w.Buch.VergebeneNamen(w.Konto(zweiter.KontoId)).Single(), "der zweite sieht: Name vergeben");

        Pruefe.Wahr(w.Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, zweiter.GeraetId, Entscheidung.Freigeben).Ok, "zweiten trotzdem freigeben");
        var zweites = w.Konto(zweiter.KontoId);
        Pruefe.Wahr(zweites.ScNamen.IsEmpty, "ohne den vergebenen Namen");
        Pruefe.Falsch(string.Equals(zweites.Name, konto.Name, StringComparison.Ordinal), "Anzeigename eindeutig");
        Pruefe.Gleich(erster.KontoId, w.K.NachScName["SAREVOC"], "der Name bleibt beim ersten");
    }

    [Test]
    public static void Antrag_UngueltigeAngabenUndGrenze()
    {
        var w = new Welt();
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Antrag(1, "S", "sarevoc").Fehler, "SC-Name zu kurz");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Antrag(1, "<script>", "sarevoc").Fehler, "Sonderzeichen im SC-Name");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Antrag(1, "Sarevoc", "x").Fehler, "Discord-Name zu kurz");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Antrag(1, "Sare‮voc", "sarevoc").Fehler, "Richtungszeichen");
        Pruefe.Wahr(w.K.Konten.IsEmpty, "nichts angelegt");
        for (var i = 0; i < Kontenbuch.MaxOffeneAntraege; i++)
            Pruefe.Wahr(w.Antrag(10 + i, "Pilot " + i, "pilot" + i).Ok, "Antrag " + i);
        var voll = w.Antrag(99, "Pilot X", "pilotx");
        Pruefe.Gleich(Fehlercode.AntraegeVoll, voll.Fehler, "31. Antrag");
        Pruefe.Wahr(voll.Wirkungen.OfType<Alarm>().Any(a => a.Art == "ANTRAEGE_VOLL"), "Alarm");
        Pruefe.Gleich(30, w.Buch.OffeneAntraege(), "30 offen");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Antrag(10, "Pilot Y", "piloty").Fehler, "dasselbe Gerät nicht zweimal");
    }

    [Test]
    public static void Antrag_FreigabeMitVormerkungOderAnKonto()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        w.Katalog("Pilot", "Sanitäter");
        var vm = w.Mitglieder.Vormerken(w.K, admin, "Hexa", "hexa_dc", [2], "kommt Freitag");
        Pruefe.Wahr(vm.Ok, "vorgemerkt");
        var vmId = vm.Ids[0];
        Pruefe.Gleich(Fehlercode.NameVergeben, w.Buch.ProfilSetzen(w.Freigegeben(Fp(2), "1001"), ["hexa"], null).Fehler, "Vormerkung belegt den Namen");

        // Ohne ausdrückliche Übernahme bekommt ein Antrag "Hexa" den Namen nicht.
        var fremd = w.Antrag(3, "Hexa", "jemand");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, fremd.GeraetId, Entscheidung.Freigeben).Ok, "freigegeben ohne Vormerkung");
        Pruefe.Wahr(w.Konto(fremd.KontoId).ScNamen.IsEmpty, "Name der Vormerkung nicht übernommen");

        var echt = w.Antrag(4, "Hexa", "hexa_dc");
        var vorschlaege = w.Mitglieder.Vorschlaege("hexa", "HEXA_DC");
        Pruefe.Wahr(vorschlaege.Count == 1 && vorschlaege[0].UeberScName, "Vorschlag über SC-Name");
        var zusatz = new Freigabezusatz(Vormerkung: new Vormerkungsuebernahme(vmId, "Hexa", "hexa_dc"));
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, echt.GeraetId, Entscheidung.Freigeben, zusatz).Ok, "mit Vormerkung freigegeben");
        Pruefe.Wahr(w.Mitglieder.VormerkungUebernehmen(vmId, echt.KontoId).Ok, "Vormerkung übernommen");
        Pruefe.Gleich("Hexa", w.Konto(echt.KontoId).ScNamen.Single(), "Hauptname aus der Vormerkung");
        Pruefe.Gleich((ushort)2, w.Mitglieder.Stand.DatenVon(echt.KontoId).Rollen.Single(), "Rollen aus der Vormerkung");
        Pruefe.Gleich("kommt Freitag", w.Mitglieder.Stand.DatenVon(echt.KontoId).Notiz, "Notiz aus der Vormerkung");
        Pruefe.Falsch(w.Mitglieder.Stand.Vormerkungen.ContainsKey(vmId), "Vormerkung fort");
        Pruefe.Falsch(w.Mitglieder.NameBelegt("HEXA"), "Name gehört jetzt dem Konto, nicht mehr der Vormerkung");

        // Zweites Gerät eines Mitglieds: ausdrücklich an das Konto hängen.
        var mitglied = w.Freigegeben(Fp(5), "1002");
        var laptop = w.Antrag(6, "Laptop Name", "egal");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.GeraetEntscheiden(admin, laptop.GeraetId, Entscheidung.Freigeben,
            new Freigabezusatz(mitglied.KontoId, new Vormerkungsuebernahme(1, "x", null))).Fehler, "nicht beides zugleich");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.GeraetEntscheiden(admin, laptop.GeraetId, Entscheidung.Freigeben,
            new Freigabezusatz(admin.KontoId)).Fehler, "an ein ADMIN-Konto nur an der Konsole");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.GeraetEntscheiden(admin, laptop.GeraetId, Entscheidung.Freigeben,
            new Freigabezusatz(9999)).Fehler, "unbekanntes Konto");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, laptop.GeraetId, Entscheidung.Freigeben, new Freigabezusatz(mitglied.KontoId)).Ok, "an das Konto gehängt");
        Pruefe.Gleich(mitglied.KontoId, w.K.Geraete[laptop.GeraetId].KontoId, "Gerät gehört dem Mitglied");
        Pruefe.Falsch(w.K.Konten.ContainsKey(laptop.KontoId), "Antragskonto fort");
        Pruefe.Falsch(w.K.NachScName.ContainsKey("LAPTOP NAME"), "Name des Antrags nicht übernommen");
    }

    [Test]
    public static void Antrag_AblehnenAlleOhneVormerkung()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var a = w.Antrag(2, "Flut Eins", "flut1");
        var b = w.Antrag(3, "Echter", "echter");
        bool Passt(Konto k) => k.Antrag?.ScName == "Echter";
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, w.Buch.AntraegeAblehnen(Akteur.Sitzung(admin.KontoId, admin.GeraetId), Passt).Fehler, "ohne Admin-Sitzung");
        Pruefe.Gleich(Fehlercode.SchrittBestaetigen, w.Buch.AntraegeAblehnen(Akteur.Verwaltend(admin.KontoId, admin.GeraetId, false), Passt).Fehler, "ohne frischen Code");
        var r = w.Buch.AntraegeAblehnen(admin, Passt);
        Pruefe.Wahr(r.Ok && r.Geraete.Single() == a.GeraetId, "nur der unpassende");
        // Seit 0.7.0 löscht Ablehnen den Antrag ganz, Gerät und Antragskonto; gesperrt wird nichts (Sarevoc 04.10.2026).
        Pruefe.Falsch(w.K.Geraete.ContainsKey(a.GeraetId) || w.K.Konten.ContainsKey(a.KontoId), "abgelehnt und gelöscht");
        Pruefe.Gleich(a.KontoId, r.Wirkungen.OfType<KontoGeloescht>().Single().KontoId, "Antragskonto fort");
        Pruefe.Wahr(w.K.GesperrteFingerabdruecke.IsEmpty, "nichts gesperrt");
        Pruefe.Gleich(GeraetStatus.Wartet, w.K.Geraete[b.GeraetId].Status, "der passende wartet weiter");
        Pruefe.Wahr(w.Antrag(2, "Flut Eins", "flut1").Ok, "derselbe PC darf neu beantragen");
    }

    [Test]
    public static void Profil_EigeneNamenOhneFremdeZuNehmen()
    {
        var w = new Welt();
        var a = w.Freigegeben(Fp(1), "1001");
        var b = w.Freigegeben(Fp(2), "1002");
        var r = w.Buch.ProfilSetzen(a, ["Sarevoc", "Sare Alt"], null);
        Pruefe.Wahr(r.Ok && r.ScNamen.Count == 2, "zwei Namen");
        Pruefe.Gleich("Sarevoc", w.Konto(a.KontoId).Hauptname, "Hauptname zuerst");
        var raub = w.Buch.ProfilSetzen(b, ["sarevoc"], null);
        Pruefe.Wahr(raub.Fehler == Fehlercode.NameVergeben && raub.Vergeben == "sarevoc", "Name eines anderen");
        Pruefe.Gleich(Fehlercode.NameVergeben, w.Buch.ProfilSetzen(b, ["Ｓarevoc"], null).Fehler, "auch in Breitschrift");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.ProfilSetzen(b, ["Eins", "Zwei", "Drei", "Vier"], null).Fehler, "höchstens drei");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.ProfilSetzen(b, ["Doppel", "doppel"], null).Fehler, "doppelt");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.ProfilSetzen(b, [], null).Fehler, "leer nicht für sich selbst");
        Pruefe.Wahr(w.Buch.ProfilSetzen(a, ["Sarevoc"], null).Ok, "zweiten Namen abgeben");
        Pruefe.Wahr(w.Buch.ProfilSetzen(b, ["Sare Alt"], null).Ok, "freigewordener Name geht");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.ProfilSetzen(Akteur.AdminWerkzeug, ["Konsole"], null).Fehler, "Konsole hat kein Profil");
    }

    // ---------------------------------------------------------------- Discord-Name: eindeutig, nur vom Admin (A53)

    [Test]
    public static void DiscordName_EindeutigUndNurVomAdmin()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var zweiterAdmin = w.Freigegeben(Fp(2), "9002", Stufe.Admin);
        var a = w.Freigegeben(Fp(3), "1001");
        var b = w.Freigegeben(Fp(4), "1002");
        Pruefe.Wahr(w.Buch.EigentuemerSetzen(Akteur.AdminWerkzeug, admin.KontoId).Ok, "Eigentümer");

        var gesetzt = w.Buch.DiscordNameSetzen(admin, a.KontoId, " Sarevoc/Otto ");
        Pruefe.Wahr(gesetzt.Ok && gesetzt.DiscordName == "Sarevoc/Otto", "Admin setzt den Discord-Namen, bereinigt: " + gesetzt.Fehler);
        Pruefe.Gleich("Sarevoc/Otto", w.Konto(a.KontoId).DiscordName, "am Konto");
        Pruefe.Gleich(a.KontoId, w.K.NachDiscordName["SAREVOC/OTTO"], "im Namensraum der Discord-Namen");

        // Angriffsfälle "gleicher Name in anderer Schreibung": groß und klein, Breitschrift, Leerzeichen am Rand.
        var vorher = w.K;
        foreach (var fremd in new[] { "sarevoc/otto", "SAREVOC/OTTO", "Ｓａｒｅｖｏｃ/Ｏｔｔｏ", "  sarevoc/Otto" })
        {
            var r = w.Buch.DiscordNameSetzen(admin, b.KontoId, fremd);
            Pruefe.Wahr(r.Fehler == Fehlercode.NameVergeben && r.Vergeben is not null, "vergeben: " + fremd);
        }
        Pruefe.Wahr(ReferenceEquals(vorher, w.K), "nichts geändert");

        // Nur der Admin in der Admin-Sitzung und die Konsole.
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.DiscordNameSetzen(Akteur.Sitzung(b.KontoId, b.GeraetId), b.KontoId, "Bert/DC").Fehler, "Funker: nicht einmal den eigenen");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.DiscordNameSetzen(Akteur.Sitzung(b.KontoId, b.GeraetId), a.KontoId, "Dieb").Fehler, "Funker: keinen fremden");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, w.Buch.DiscordNameSetzen(Akteur.Sitzung(admin.KontoId, admin.GeraetId), b.KontoId, "Bert/DC").Fehler,
            "Admin ohne Admin-Sitzung");
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(Akteur.Verwaltend(admin.KontoId, admin.GeraetId, bestaetigt: false), b.KontoId, "Bert/DC").Ok,
            "in der Admin-Sitzung ohne frischen Code (Namen verleihen keine Rechte)");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.DiscordNameSetzen(zweiterAdmin, admin.KontoId, "Test").Fehler, "den Eigentümer fasst im Client niemand an");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.DiscordNameSetzen(admin, zweiterAdmin.KontoId, "Zwei").Fehler, "einen anderen Admin nur an der Konsole");
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(admin, admin.KontoId, "Test/Sarevoc").Ok, "der eigene in der Admin-Sitzung");
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(Akteur.AdminWerkzeug, zweiterAdmin.KontoId, "Zwei/DC").Ok, "Konsole darf jeden");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.DiscordNameSetzen(admin, b.KontoId, "Sare\u200Bvoc").Fehler, "unsichtbares Zeichen");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Buch.DiscordNameSetzen(admin, 4242, "Niemand").Fehler, "unbekanntes Konto");

        // ProfilSetzen ändert den Discord-Namen nicht mehr; der Server setzt das durch, nicht nur die Oberfläche.
        vorher = w.K;
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.ProfilSetzen(b, ["Bert"], "Sarevoc/Otto").Fehler, "ProfilSetzen mit fremdem Discord-Namen");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.ProfilSetzen(b, null, "Bert Neu").Fehler, "ProfilSetzen mit neuem eigenem Discord-Namen");
        Pruefe.Wahr(ReferenceEquals(vorher, w.K), "abgewiesen ohne Seiteneffekt, auch die SC-Namen nicht");
        var gleich = w.Buch.ProfilSetzen(b, ["Bert"], "Bert/DC");
        Pruefe.Wahr(gleich.Ok && gleich.DiscordName == "Bert/DC", "derselbe Discord-Name stört nicht (ältere Clients schicken ihn mit)");
        Pruefe.Gleich("Bert", w.Konto(b.KontoId).Hauptname, "SC-Namen gesetzt");

        // Leeren gibt den Namen frei.
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(Akteur.AdminWerkzeug, a.KontoId, null).Ok, "Konsole leert");
        Pruefe.Gleich(null, w.Konto(a.KontoId).DiscordName, "leer");
        Pruefe.Falsch(w.K.NachDiscordName.ContainsKey("SAREVOC/OTTO"), "frei");
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(admin, b.KontoId, "SAREVOC/otto").Ok, "jetzt geht er an einen anderen");
        Pruefe.Falsch(w.K.NachDiscordName.ContainsKey("BERT/DC"), "der alte Name von b ist frei");

        // Eine Vormerkung belegt ihren Discord-Namen ebenso.
        Pruefe.Wahr(w.Mitglieder.Vormerken(w.K, admin, "Hexa", "Hexa/DC", null, null).Ok, "Vormerkung");
        var vm = w.Buch.DiscordNameSetzen(admin, a.KontoId, "hexa/dc");
        Pruefe.Wahr(vm.Fehler == Fehlercode.NameVergeben && vm.Vergeben == "hexa/dc", "Name einer Vormerkung");

        // Konto gelöscht: Name frei.
        Pruefe.Wahr(w.Buch.KontoLoeschen(Akteur.AdminWerkzeug, b.KontoId).Ok, "löschen");
        Pruefe.Falsch(w.K.NachDiscordName.ContainsKey("SAREVOC/OTTO"), "nach dem Löschen frei");
    }

    /// <summary>
    /// Gegenprüfung des Nachtbaus (A53): Ein unsichtbares Zeichen, das kein Formatzeichen ist (Hangul-Füller U+3164, Verbinder
    /// U+034F, Variantenwähler U+FE0F), gab einem Namen, der aussieht wie "Fritz/DC", einen eigenen Schlüssel. So kam ein
    /// zweiter "Fritz/DC" über den Antrag durch die Freigabe, ohne Warnung und ohne Ablehnung; ebenso über den Admin, die
    /// Vormerkung und den SC-Namen in ProfilSetzen. Und "GROẞ/DC" galt nicht als dieselbe Schreibung wie "Groß/DC".
    /// </summary>
    [Test]
    public static void DiscordName_UnsichtbareZeichenUndEszettUmgehenNichts()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var fritz = w.Freigegeben(Fp(2), "1001");
        var b = w.Freigegeben(Fp(3), "1002");
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(Akteur.AdminWerkzeug, fritz.KontoId, "Fritz/DC").Ok, "Fritz trägt Fritz/DC");
        Pruefe.Wahr(w.Buch.ScNamenSetzen(Akteur.AdminWerkzeug, fritz.KontoId, ["Fritz"]).Ok, "und den SC-Namen Fritz");
        var fueller = char.ConvertFromUtf32(0x3164);
        var verbinder = char.ConvertFromUtf32(0x034F);
        var wahl = char.ConvertFromUtf32(0xFE0F);

        // Über den Antrag: Der Server nimmt ihn gar nicht erst an, also kommt er auch nicht zur Freigabe.
        var antrag = w.Antrag(4, "Falscher", "Fritz/DC" + fueller);
        Pruefe.Gleich(Fehlercode.Ungueltig, antrag.Fehler, "Antrag mit Hangul-Füller im Discord-Namen");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Antrag(5, "Fritz" + fueller, "Anders/DC").Fehler, "Antrag mit Hangul-Füller im SC-Namen");

        // Über den Admin, ProfilSetzen und die Vormerkung.
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.DiscordNameSetzen(admin, b.KontoId, "Fritz/DC" + verbinder).Fehler, "Admin: Verbinder U+034F");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.DiscordNameSetzen(admin, b.KontoId, wahl + "Fritz/DC").Fehler, "Admin: Variantenwähler vorn");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.ProfilSetzen(b, ["Fritz" + fueller], null).Fehler, "ProfilSetzen: SC-Name mit Hangul-Füller");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Mitglieder.Vormerken(w.K, admin, "Neu", "Fritz/DC" + wahl, null, null).Fehler, "Vormerkung: Variantenwähler");
        Pruefe.Wahr(!w.K.Konten.Values.Any(k => k.KontoId != fritz.KontoId && k.DiscordName is { } d && d.StartsWith("Fritz/DC", StringComparison.Ordinal)),
            "kein zweiter Fritz/DC");

        // Großes ẞ: dieselbe Schreibung wie ß, also vergeben.
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(admin, b.KontoId, "Gro" + char.ConvertFromUtf32(0x00DF) + "/DC").Ok, "b trägt Groß/DC");
        var eszett = w.Buch.DiscordNameSetzen(Akteur.AdminWerkzeug, fritz.KontoId, "GRO" + char.ConvertFromUtf32(0x1E9E) + "/DC");
        Pruefe.Gleich(Fehlercode.NameVergeben, eszett.Fehler, "GROẞ/DC ist Groß/DC");
    }

    /// <summary>
    /// Folge davon, dass ẞ jetzt wie ß zählt: Gespeicherte Stände tragen Schlüssel nach der alten Rechnung. Der Namensraum der
    /// Anzeigenamen steht als "namen" in konten.json und wird beim Laden nachgezogen; Vormerkungen und Rollenkatalog, deren Namen
    /// jetzt zusammenfallen, lassen das Laden und "Liste einfügen" nicht scheitern.
    /// </summary>
    [Test]
    public static void Namensraum_AlteSchluesselMitEszettNachLaden()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var gross = char.ConvertFromUtf32(0x1E9E);
        var klein = char.ConvertFromUtf32(0x00DF);

        // konten.json von vorher: Anzeigename "MAẞ" unter dem alten Schlüssel.
        var alt = w.K with { NachNamensschluessel = w.K.NachNamensschluessel.Add("MA" + gross, 4242) };
        var geladen = alt.MitScNamenIndex();
        Pruefe.Wahr(geladen.NachNamensschluessel.TryGetValue("MA" + klein, out var id) && id == 4242, "unter dem heutigen Schlüssel");
        Pruefe.Falsch(geladen.NachNamensschluessel.ContainsKey("MA" + gross), "der alte Schlüssel ist fort");
        Pruefe.Wahr(ReferenceEquals(w.K.NachNamensschluessel, w.K.MitScNamenIndex().NachNamensschluessel), "ohne alte Schlüssel bleibt der Index, wie er ist");

        // mitglieder.json von vorher: zwei Vormerkungen, deren SC-Namen heute denselben Schlüssel haben, und zwei solche Rollen.
        var stand = w.Mitglieder.Stand with
        {
            Vormerkungen = w.Mitglieder.Stand.Vormerkungen.Add(7, new Vormerkung(7, "Ma" + klein)).Add(8, new Vormerkung(8, "MA" + gross)),
            Katalog = w.Mitglieder.Stand.Katalog.Add(1, new Katalogrolle(1, "Gro" + klein)).Add(2, new Katalogrolle(2, "GRO" + gross)),
        };
        var buch = new Mitgliederbuch(w.Uhr, stand);
        Pruefe.Wahr(buch.NameBelegt(Zeichenregeln.Namensschluessel("MA" + gross)), "geladen, der Name ist belegt");
        var r = buch.Einfuegen(w.K, admin, ["Neu; ; ; gro" + klein]);
        Pruefe.Wahr(r.Ok, "Liste einfügen findet die Rolle: " + r.Fehler + " " + string.Join(" | ", r.Zeilen.Select(z => z.Grund)));
    }

    [Test]
    public static void DiscordName_FreigabeOhneBelegtenNamen()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var otto = w.Freigegeben(Fp(2), "1001");
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(Akteur.AdminWerkzeug, otto.KontoId, "Sarevoc/Otto").Ok, "Otto trägt Sarevoc/Otto");

        // Ein offener Antrag besetzt nichts, auch nicht mit dem Discord-Namen eines Mitglieds.
        var taeuscher = w.Antrag(3, "Fremder", "ＳAREVOC/otto");
        Pruefe.Wahr(taeuscher.Ok, "Antrag mit dem Namen eines Mitglieds wird angenommen");
        Pruefe.Falsch(w.K.NachDiscordName.ContainsValue(taeuscher.KontoId), "besetzt nichts");

        // Seit 0.7.0 (ein PC, ein Konto, Sarevoc 04.10.2026) scheitert die Freigabe nicht mehr am belegten Namen: Das neue Konto bekommt
        // ihn nur nicht, also gibt sich niemand in "Wer ist da" als Otto aus (A53).
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, taeuscher.GeraetId, Entscheidung.Freigeben).Ok, "Freigabe geht");
        Pruefe.Gleich(null, w.Konto(taeuscher.KontoId).DiscordName, "ohne den belegten Discord-Namen");
        Pruefe.Gleich(otto.KontoId, w.K.NachDiscordName["SAREVOC/OTTO"], "Otto behält ihn");

        // Zwei Anträge mit demselben Namen, etwa zwei PCs desselben Menschen: Der erste bekommt ihn, der zweite wird ohne ihn frei.
        var eins = w.Antrag(4, "Neu Eins", "Neu/DC");
        var zwei = w.Antrag(5, "Neu Zwei", "neu/dc");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, eins.GeraetId, Entscheidung.Freigeben).Ok, "erster frei");
        Pruefe.Gleich(eins.KontoId, w.K.NachDiscordName["NEU/DC"], "jetzt belegt");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, zwei.GeraetId, Entscheidung.Freigeben).Ok, "zweiter auch frei");
        Pruefe.Gleich(null, w.Konto(zwei.KontoId).DiscordName, "aber ohne den Namen");
        Pruefe.Gleich(eins.KontoId, w.K.NachDiscordName["NEU/DC"], "der Name bleibt beim ersten");

        // Vormerkung mit demselben Discord-Namen: Ohne ihre Übernahme bleibt der Name bei ihr, mit Übernahme geht er ans Konto.
        var vm = w.Mitglieder.Vormerken(w.K, admin, "Hexa", "Hexa/DC", null, null);
        Pruefe.Wahr(vm.Ok, "vorgemerkt");
        var falsch = w.Antrag(6, "Hexa Falsch", "hexa/dc");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, falsch.GeraetId, Entscheidung.Freigeben).Ok, "ohne Übernahme frei");
        Pruefe.Gleich(null, w.Konto(falsch.KontoId).DiscordName, "ohne Übernahme: der Name gehört der Vormerkung");
        Pruefe.Gleich(vm.Ids[0], w.Mitglieder.DiscordNameVon("HEXA/DC"), "die Vormerkung hält ihn weiter");
        var hexa = w.Antrag(8, "Hexa Echt", "hexa/dc");
        var zusatz = new Freigabezusatz(Vormerkung: new Vormerkungsuebernahme(vm.Ids[0], "Hexa", "Hexa/DC"));
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, hexa.GeraetId, Entscheidung.Freigeben, zusatz).Ok, "mit Übernahme");
        Pruefe.Wahr(w.Mitglieder.VormerkungUebernehmen(vm.Ids[0], hexa.KontoId).Ok, "übernommen");
        Pruefe.Gleich("hexa/dc", w.Konto(hexa.KontoId).DiscordName, "Discord-Name des Antrags");
        Pruefe.Gleich(hexa.KontoId, w.K.NachDiscordName["HEXA/DC"], "im Namensraum");
        Pruefe.Gleich(null, w.Mitglieder.DiscordNameVon("HEXA/DC"), "die Vormerkung hält ihn nicht mehr");

        // Ein Gerät an ein vorhandenes Konto hängen kann der Server weiter (Konsole, --an-konto); der Verwaltungsteil bietet es seit
        // 0.7.0 nicht mehr an (ein PC, ein Konto). Der Discord-Name des Antrags spielt dann keine Rolle, das Konto behält seinen.
        var laptop = w.Antrag(7, "Laptop", "Sarevoc/Otto");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, laptop.GeraetId, Entscheidung.Freigeben, new Freigabezusatz(otto.KontoId)).Ok, "an Ottos Konto gehängt");
        Pruefe.Gleich("Sarevoc/Otto", w.Konto(otto.KontoId).DiscordName, "unverändert");
    }

    [Test]
    public static void DiscordName_VormerkungenUndListe()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var mitglied = w.Freigegeben(Fp(2), "1001");
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(Akteur.AdminWerkzeug, mitglied.KontoId, "Belegt/DC").Ok, "Konto trägt Belegt/DC");
        var m = w.Mitglieder;

        var vonKonto = m.Vormerken(w.K, admin, "Neuling", "belegt/dc", null, null);
        Pruefe.Wahr(vonKonto.Fehler == Fehlercode.NameVergeben && vonKonto.Vergeben == "belegt/dc" && vonKonto.DiscordVergeben, "Discord-Name eines Kontos");
        var frei = m.Vormerken(w.K, admin, "Neuling", "Frei/DC", null, null);
        Pruefe.Wahr(frei.Ok, "vorgemerkt");
        Pruefe.Gleich(frei.Ids[0], m.DiscordNameVon("FREI/DC"), "belegt");
        Pruefe.Wahr(m.Vormerken(w.K, admin, "Andere", "ＦＲＥＩ/dc", null, null) is { Fehler: Fehlercode.NameVergeben, DiscordVergeben: true }, "Discord-Name einer Vormerkung, Breitschrift");
        Pruefe.Wahr(m.VormerkungAendern(w.K, admin, frei.Ids[0], null, "frei/DC", null, null).Ok, "der eigene bleibt frei für sich selbst");
        Pruefe.Wahr(m.VormerkungAendern(w.K, admin, frei.Ids[0], null, "Umbenannt/DC", null, null).Ok, "umbenannt");
        Pruefe.Gleich(null, m.DiscordNameVon("FREI/DC"), "alter Name frei");
        Pruefe.Gleich(Fehlercode.NameVergeben, m.VormerkungAendern(w.K, admin, frei.Ids[0], null, "BELEGT/DC", null, null).Fehler, "ändern auf den eines Kontos");

        // Liste einfügen: alles oder nichts, mit Klartext je Zeile.
        string[] liste =
        [
            "Alpha; belegt/dc",
            "Beta; umbenannt/dc",
            "Gamma; Gamma/DC",
            "Delta; ＧＡＭＭＡ/dc",
            "Epsilon; Epsilon/DC",
        ];
        var vorher = m.Stand;
        var r = m.Einfuegen(w.K, admin, liste);
        Pruefe.Wahr(r.Fehler == Fehlercode.Ungueltig && ReferenceEquals(vorher, m.Stand), "nichts angelegt");
        Pruefe.Gleich("1,2,4", string.Join(",", r.Zeilen.Select(z => z.Zeile)), "Konto, Vormerkung und doppelt in der Liste");
        Pruefe.Wahr(r.Zeilen.All(z => z.Grund.Contains("Discord-Namen", StringComparison.Ordinal)), "Klartext nennt den Discord-Namen: " + string.Join(" | ", r.Zeilen.Select(z => z.Grund)));
        var gut = m.Einfuegen(w.K, admin, ["Gamma; Gamma/DC", "Epsilon; Epsilon/DC", "Zeta"]);
        Pruefe.Wahr(gut.Ok && gut.Ids.Count == 3, "eingefügt");
        Pruefe.Gleich(gut.Ids[0], m.DiscordNameVon("GAMMA/DC"), "belegt");
        Pruefe.Gleich(Fehlercode.NameVergeben, w.Buch.DiscordNameSetzen(admin, mitglied.KontoId, "gamma/dc").Fehler, "Konto kann ihn nicht mehr nehmen");
        Pruefe.Wahr(m.VormerkungLoeschen(w.K, admin, gut.Ids[0]).Ok && m.DiscordNameVon("GAMMA/DC") is null, "gelöscht, frei");
    }

    // ---------------------------------------------------------------- Eigentümer und ADMIN-Konten (3.9, 3.10)

    [Test]
    public static void Eigentuemer_ImClientUnantastbar()
    {
        var w = new Welt();
        var test = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var zweiter = w.Freigegeben(Fp(2), "9002", Stufe.Admin);
        var funker = w.Freigegeben(Fp(3), "1001");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.EigentuemerSetzen(zweiter, zweiter.KontoId).Fehler, "Eigentümer nie aus dem Client");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.EigentuemerSetzen(Akteur.AdminWerkzeug, funker.KontoId).Fehler, "nur ein ADMIN-Konto");
        Pruefe.Wahr(w.Buch.EigentuemerSetzen(Akteur.AdminWerkzeug, test.KontoId).Ok, "Konsole setzt Test");
        Pruefe.Wahr(w.K.Eigentuemer?.KontoId == test.KontoId, "Eigentümer");

        // Angriffsfall "Eigentümer herabstufen": ein zweiter Admin mit übernommenem PC, mit Admin-Sitzung und frischem Code.
        var vorher = w.K;
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.StufeSetzen(zweiter, test.KontoId, Stufe.Funker).Fehler, "herabstufen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoSperren(zweiter, test.KontoId).Fehler, "sperren (auch als Notbremse nicht)");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoLoeschen(zweiter, test.KontoId).Fehler, "löschen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(zweiter, test.KontoId, Rechte.Keine).Fehler, "Rechte nehmen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.NameSetzen(zweiter, test.KontoId, "Niemand").Fehler, "umbenennen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.ScNamenSetzen(zweiter, test.KontoId, []).Fehler, "SC-Namen nehmen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.GeraetEntfernen(zweiter, test.GeraetId, true, "weg").Fehler, "Gerät des Eigentümers entziehen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.GeraetEntfernen(zweiter, test.GeraetId, false, "").Fehler, "Gerät des Eigentümers entfernen");
        Pruefe.Wahr(ReferenceEquals(vorher, w.K), "nichts geändert");

        // Sarevoc selbst: eigene Namen ja, die eigene Stufe nicht.
        Pruefe.Wahr(w.Buch.ProfilSetzen(test, ["Test"], null).Ok, "eigene SC-Namen");
        Pruefe.Wahr(w.Buch.NameSetzen(test, test.KontoId, "Test").Ok, "eigener Anzeigename");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoSperren(test, test.KontoId).Fehler, "sich selbst sperren");
        Pruefe.Wahr(w.Buch.StufeSetzen(Akteur.AdminWerkzeug, test.KontoId, Stufe.Admin).Ok, "Konsole darf alles");
        // Nur herabstufen nicht, auch an der Konsole: Sonst hinge das Merkmal an einem Konto ohne ADMIN (3.10).
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.StufeSetzen(Akteur.AdminWerkzeug, test.KontoId, Stufe.Raidleiter).Fehler, "Konsole stuft den Eigentümer nicht herab");
        Pruefe.Gleich(Stufe.Admin, w.Konto(test.KontoId).Stufe, "Test bleibt ADMIN");

        // Ein zweiter Eigentümer ersetzt den ersten.
        Pruefe.Wahr(w.Buch.EigentuemerSetzen(Akteur.AdminWerkzeug, zweiter.KontoId).Ok, "Eigentümer wechseln");
        Pruefe.Falsch(w.Konto(test.KontoId).Eigentuemer, "Test nicht mehr");
        Pruefe.Gleich(1, w.K.Konten.Values.Count(k => k.Eigentuemer), "genau einer");
        Pruefe.Wahr(w.Buch.StufeSetzen(Akteur.AdminWerkzeug, test.KontoId, Stufe.Raidleiter).Ok, "Gegenprobe: nach dem Wechsel geht das Herabstufen");
    }

    [Test]
    public static void AdminKonten_ImClientNurSperren()
    {
        var w = new Welt();
        var a = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var b = w.Freigegeben(Fp(2), "9002", Stufe.Admin);
        var c = w.Freigegeben(Fp(3), "9003", Stufe.Admin);
        var f = w.Freigegeben(Fp(4), "1001");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.StufeSetzen(a, f.KontoId, Stufe.Admin).Fehler, "Stufe ADMIN nur an der Konsole");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(a, f.KontoId, Rechte.GeraeteFreigeben).Fehler, "GERAETE_FREIGEBEN als Einzelrecht");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(a, f.KontoId, Rechte.KontenVerwalten).Fehler, "KONTEN_VERWALTEN als Einzelrecht");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(a, f.KontoId, Rechte.NetzeVerwalten).Fehler, "NETZE_VERWALTEN als Einzelrecht");
        Pruefe.Wahr(w.Buch.RechteSetzen(a, f.KontoId, Rechte.Durchsage).Ok, "Einzelrecht ohne Verwaltungsrecht geht");
        Pruefe.Wahr(w.Buch.StufeSetzen(a, f.KontoId, Stufe.Raidleiter).Ok, "RAIDLEITER geht");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.StufeSetzen(a, b.KontoId, Stufe.Raidleiter).Fehler, "anderen Admin herabstufen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoLoeschen(a, b.KontoId).Fehler, "anderen Admin löschen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.NameSetzen(a, b.KontoId, "B").Fehler, "anderen Admin umbenennen");
        Pruefe.Wahr(w.Buch.KontoSperren(a, b.KontoId).Ok, "Notbremse: anderen Admin sperren");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoEntsperren(c, b.KontoId).Fehler, "entsperren nur an der Konsole");
        Pruefe.Wahr(w.Buch.KontoEntsperren(Akteur.AdminWerkzeug, b.KontoId).Ok, "Konsole entsperrt");

        // Angriffsfall "Rechteausweitung": RAIDLEITER mit KONTEN_VERWALTEN (von der Konsole) kommt nicht höher.
        var rl = w.Freigegeben(Fp(5), "2001", Stufe.Raidleiter);
        Pruefe.Wahr(w.Buch.RechteSetzen(Akteur.AdminWerkzeug, rl.KontoId, Rechteregeln.Standard(Stufe.Raidleiter) | Rechte.KontenVerwalten).Ok, "Konsole");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.StufeSetzen(rl, f.KontoId, Stufe.Admin).Fehler, "nicht über die eigene Stufe");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.KontoSperren(rl, a.KontoId).Fehler, "kein ADMIN-Konto anfassen");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.RechteSetzen(rl, rl.KontoId, Rechteregeln.Alle).Fehler, "nicht die eigenen Rechte");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(rl, f.KontoId, Rechte.KontenVerwalten).Fehler, "sein Verwaltungsrecht nicht weitergeben");

        // Angriffsfall "anderen Admin über seine Geräte aussperren": A hat eine Admin-Sitzung mit frischem Code. Gegen C gibt
        // es im Client nur die Notbremse "Konto sperren"; entfernen oder dauerhaft sperren (Fingerabdruck für immer) nur an der
        // Konsole. Dasselbe gilt für ein privilegiertes Konto ohne Stufe ADMIN (RL mit KONTEN_VERWALTEN von der Konsole).
        var vorher = w.K;
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.GeraetEntfernen(a, c.GeraetId, true, "weg").Fehler, "Gerät eines anderen Admins dauerhaft sperren");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.GeraetEntfernen(a, c.GeraetId, false, "").Fehler, "Gerät eines anderen Admins entfernen");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.GeraetEntfernen(a, rl.GeraetId, false, "").Fehler, "Gerät eines privilegierten Kontos entfernen");
        Pruefe.Wahr(ReferenceEquals(vorher, w.K), "Geräte unverändert");
        // Gegenprobe: das eigene Gerät, das Gerät eines Funkers und an der Konsole auch das Gerät eines Admins gehen.
        var g = w.Freigegeben(Fp(6), "1006");
        Pruefe.Gleich(GeraetStatusMeldung.Entfernt, w.Buch.GeraetEntfernen(a, g.GeraetId, false, "Verlust").Status, "Gerät eines Funkers entfernt");
        Pruefe.Gleich(GeraetStatusMeldung.Entfernt, w.Buch.GeraetEntfernen(Akteur.AdminWerkzeug, c.GeraetId, false, "").Status, "Konsole entfernt das Admin-Gerät");
        Pruefe.Gleich(GeraetStatusMeldung.Entfernt, w.Buch.GeraetEntfernen(a, a.GeraetId, false, "").Status, "eigenes Gerät");
    }

    [Test]
    public static void AdminSitzung_VerwaltungsrechteNurDarin()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var f = w.Freigegeben(Fp(2), "1001");
        var antrag = w.Antrag(3, "Neuer", "neuer");
        var normal = Akteur.Sitzung(admin.KontoId, admin.GeraetId);
        var ohneCode = Akteur.Verwaltend(admin.KontoId, admin.GeraetId, bestaetigt: false);
        var vorher = w.K;
        var mitglieder = w.Mitglieder.Stand;

        // Gemeinsamer Test der Bücher: jede Verwaltungshandlung eines ADMIN ohne Admin-Sitzung, ohne Seiteneffekt.
        var faelle = new (string Was, Func<Akteur, Fehlercode> Aufruf, bool Heikel)[]
        {
            ("KontoSperren", a => w.Buch.KontoSperren(a, f.KontoId).Fehler, true),
            ("KontoEntsperren", a => w.Buch.KontoEntsperren(a, f.KontoId).Fehler, true),
            ("StufeSetzen", a => w.Buch.StufeSetzen(a, f.KontoId, Stufe.Raidleiter).Fehler, true),
            ("RechteSetzen", a => w.Buch.RechteSetzen(a, f.KontoId, Rechte.Durchsage).Fehler, true),
            ("KontoLoeschen", a => w.Buch.KontoLoeschen(a, f.KontoId).Fehler, true),
            ("NameSetzen", a => w.Buch.NameSetzen(a, f.KontoId, "Neu").Fehler, false),
            ("ScNamenSetzen", a => w.Buch.ScNamenSetzen(a, f.KontoId, ["Neu"]).Fehler, false),
            ("GeraetEntscheiden", a => w.Buch.GeraetEntscheiden(a, antrag.GeraetId, Entscheidung.Freigeben).Fehler, true),
            ("GeraetAblehnen", a => w.Buch.GeraetEntscheiden(a, antrag.GeraetId, Entscheidung.Ablehnen).Fehler, true),
            ("AntragZurueckweisen", a => w.Buch.AntragZurueckweisen(a, antrag.GeraetId).Fehler, false),
            ("GeraetEntfernen", a => w.Buch.GeraetEntfernen(a, f.GeraetId, true, "").Fehler, true),
            ("GeraeteListe fremd", a => w.Buch.GeraeteListe(a, f.KontoId, false).Fehler, false),
            ("AntraegeAblehnen", a => w.Buch.AntraegeAblehnen(a, _ => false).Fehler, true),
            ("KatalogSetzen", a => w.Mitglieder.KatalogSetzen(w.K, a, [new Katalogrolle(1, "Pilot")], null).Fehler, false),
            ("Vormerken", a => w.Mitglieder.Vormerken(w.K, a, "Vorgemerkt", null, null, null).Fehler, false),
            ("Einfuegen", a => w.Mitglieder.Einfuegen(w.K, a, ["Liste Eins"]).Fehler, false),
            ("KontoAendern", a => w.Mitglieder.KontoAendern(w.K, a, f.KontoId, [], "Notiz").Fehler, false),
            ("AbzeichenSetzen", a => w.Mitglieder.AbzeichenSetzen(w.K, a, f.KontoId, ["MARINE_01"], ["T_SANI"]).Fehler, false),
            ("RechtPruefen KONTEN_VERWALTEN", a => Kontenbuch.RechtPruefen(w.K, a, Rechte.KontenVerwalten), false),
        };
        foreach (var (was, aufruf, heikel) in faelle)
        {
            Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, aufruf(normal), was + " ohne Admin-Sitzung");
            if (heikel) Pruefe.Gleich(Fehlercode.SchrittBestaetigen, aufruf(ohneCode), was + " ohne frischen Code");
            Pruefe.Wahr(ReferenceEquals(vorher, w.K) && ReferenceEquals(mitglieder, w.Mitglieder.Stand), was + ": ohne Seiteneffekt");
        }
        var eigene = w.Buch.GeraeteListe(normal, null, false);
        Pruefe.Wahr(eigene.Ok && eigene.Geraete.Count == 1 && eigene.Geraete.All(g => g.KontoId == admin.KontoId), "ohne Admin-Sitzung nur die eigenen Geräte");
        // Ein Funker ohne Recht bekommt KEIN_RECHT, nicht den Hinweis auf eine Admin-Sitzung.
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Buch.KontoSperren(Akteur.Sitzung(f.KontoId, f.GeraetId), admin.KontoId).Fehler, "Funker");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Mitglieder.Vormerken(w.K, f, "Xy", null, null, null).Fehler, "Funker merkt nicht vor");

        // In der Admin-Sitzung ohne frischen Code gehen die nicht heiklen Handlungen.
        Pruefe.Wahr(w.Buch.NameSetzen(ohneCode, f.KontoId, "Neu").Ok, "Name ohne frischen Code");
        Pruefe.Wahr(w.Buch.GeraeteListe(ohneCode, null, false).Ok, "Liste ohne frischen Code");
        Pruefe.Wahr(w.Buch.KontoSperren(admin, f.KontoId).Ok, "mit frischem Code: sperren");
    }

    // ---------------------------------------------------------------- Rollenkatalog, Vormerkungen, Liste einfügen (N6, N7)

    [Test]
    public static void Katalog_GrenzenUndVersion()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var m = w.Mitglieder;
        Pruefe.Wahr(m.KatalogSetzen(w.K, admin, [new Katalogrolle(1, "Pilot") { Gruppe = "Flug" }, new Katalogrolle(2, "Sanitäter")], 0).Ok, "Katalog");
        Pruefe.Gleich(1L, m.Stand.KatalogVersion, "Version 1");
        Pruefe.Gleich(Fehlercode.Veraltet, m.KatalogSetzen(w.K, admin, [new Katalogrolle(1, "Pilot")], 0).Fehler, "alte Version");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.KatalogSetzen(w.K, admin, [new Katalogrolle(1, "A"), new Katalogrolle(1, "B")], null).Fehler, "Id doppelt");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.KatalogSetzen(w.K, admin, [new Katalogrolle(1, "Pilot"), new Katalogrolle(2, "PILOT")], null).Fehler, "Name doppelt");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.KatalogSetzen(w.K, admin, [new Katalogrolle(0, "Null")], null).Fehler, "Id 0");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.KatalogSetzen(w.K, admin,
            [.. Enumerable.Range(1, 65).Select(i => new Katalogrolle((ushort)i, "Rolle " + i))], null).Fehler, "65 Einträge");
        Pruefe.Wahr(m.KatalogSetzen(w.K, admin, [.. Enumerable.Range(1, 64).Select(i => new Katalogrolle((ushort)i, "Rolle " + i))], null).Ok, "64 Einträge");

        // Entfernte Rollen verschwinden überall.
        var f = w.Freigegeben(Fp(2), "1001");
        Pruefe.Wahr(m.KontoAendern(w.K, admin, f.KontoId, [1, 2], null).Ok, "Rollen 1 und 2");
        Pruefe.Wahr(m.LieblingsrollenSetzen(w.K, f, [3], 3).Ok, "Lieblingsrolle 3");
        var r = m.KatalogSetzen(w.K, admin, [new Katalogrolle(1, "Rolle 1")], null);
        Pruefe.Wahr(r.Ok && r.BetroffeneKonten.Contains(f.KontoId), "betroffen");
        var d = m.Stand.DatenVon(f.KontoId);
        Pruefe.Wahr(d.Rollen.SequenceEqual([(ushort)1]) && d.Lieblingsrollen.IsEmpty && d.ErsteWahl is null, "bereinigt");
    }

    [Test]
    public static void Vormerkung_NamenUndGrenzen()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var mitglied = w.Freigegeben(Fp(2), "1001");
        w.Buch.ProfilSetzen(mitglied, ["Belegt"], null);
        w.Katalog("Pilot");
        var m = w.Mitglieder;
        Pruefe.Gleich(Fehlercode.NameVergeben, m.Vormerken(w.K, admin, "belegt", null, null, null).Fehler, "Name eines Kontos");
        var a = m.Vormerken(w.K, admin, "Neuling", "neu_dc", [1], null);
        Pruefe.Wahr(a.Ok, "vorgemerkt");
        Pruefe.Gleich(Fehlercode.NameVergeben, m.Vormerken(w.K, admin, "NEULING", null, null, null).Fehler, "Name einer Vormerkung");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.Vormerken(w.K, admin, "Rollig", null, [9], null).Fehler, "Rolle nicht im Katalog");
        Pruefe.Wahr(m.VormerkungAendern(w.K, admin, a.Ids[0], "Neuling", "neu2", null, null).Ok, "eigener Name bleibt frei für sich selbst");
        Pruefe.Gleich("neu2", m.Stand.Vormerkungen[a.Ids[0]].DiscordName, "geändert");
        Pruefe.Wahr(m.VormerkungAendern(w.K, admin, a.Ids[0], "Umbenannt", null, null, null).Ok, "umbenannt");
        Pruefe.Falsch(m.NameBelegt("NEULING"), "alter Name frei");
        Pruefe.Wahr(m.NameBelegt("UMBENANNT"), "neuer Name belegt");
        Pruefe.Wahr(m.VormerkungLoeschen(w.K, admin, a.Ids[0]).Ok && !m.NameBelegt("UMBENANNT"), "gelöscht, Name frei");
        Pruefe.Gleich(Fehlercode.Unbekannt, m.VormerkungLoeschen(w.K, admin, a.Ids[0]).Fehler, "nicht zweimal");
    }

    [Test]
    public static void Einfuegen_AllesOderNichts()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var mitglied = w.Freigegeben(Fp(2), "1001");
        w.Buch.ProfilSetzen(mitglied, ["Vorhanden"], null);
        w.Katalog("Pilot", "Sanitäter");
        var m = w.Mitglieder;
        string[] liste =
        [
            "Alpha; alpha_dc; BS; Pilot, Sanitäter",
            "Beta; ; ; pilot",
            "Vorhanden; x; ; ",
            "Gamma; gamma; XX; ",
            "Delta; delta; ; Koch",
            "Alpha; doppelt",
            "E; e",
            "Eins; zwei; drei; vier; fünf",
        ];
        var vorher = m.Stand;
        var r = m.Einfuegen(w.K, admin, liste);
        Pruefe.Gleich(Fehlercode.Ungueltig, r.Fehler, "fehlerhafte Zeilen");
        Pruefe.Wahr(ReferenceEquals(vorher, m.Stand), "nichts angelegt");
        // Zeile 4 ("XX" in Spalte 3) ist seit 0.11.8 kein Fehler mehr: Die Spalte wird übergangen.
        Pruefe.Gleich("3,5,6,7,8", string.Join(",", r.Zeilen.Select(z => z.Zeile)), "jede fehlerhafte Zeile genannt");
        Pruefe.Gleich(null, r.Hinweis, "kein Hinweis, wenn nichts angelegt ist");
        Pruefe.Wahr(r.Zeilen.Single(z => z.Zeile == 5).Grund.Contains("Koch", StringComparison.Ordinal), "Klartext mit der Rolle");

        var gut = m.Einfuegen(w.K, admin, ["Alpha; alpha_dc; BS; Pilot, Sanitäter", "Beta; ; ; pilot"]);
        Pruefe.Wahr(gut.Ok && gut.Ids.Count == 2, "zwei Vormerkungen");
        var alpha = m.Stand.Vormerkungen[gut.Ids[0]];
        Pruefe.Wahr(alpha.DiscordName == "alpha_dc" && alpha.Rollen.SequenceEqual([(ushort)1, (ushort)2]), "Felder übernommen");
        Pruefe.Gleich(Mitgliederbuch.Spalte3Hinweis, gut.Hinweis, "Spalte 3 übergangen, ein Hinweis je Liste");
        Pruefe.Wahr(m.Stand.Untergilden.IsEmpty, "keine Untergilde angelegt oder zugewiesen");
        var ohne = m.Einfuegen(w.K, admin, ["Gamma; gamma_dc; ; Pilot", "Delta"]);
        Pruefe.Wahr(ohne.Ok && ohne.Ids.Count == 2 && ohne.Hinweis is null, "Spalte 3 leer: kein Hinweis");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.Einfuegen(w.K, admin, []).Fehler, "leere Liste");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.Einfuegen(w.K, admin, [.. Enumerable.Range(0, 201).Select(i => "Name " + i)]).Fehler, "über 200 Zeilen");
    }

    // ---------------------------------------------------------------- Lieblingsrollen (N8); Untergilden seit 0.11.8 entfallen

    [Test]
    public static void Lieblingsrollen_NurAusDemKatalogUndOhneAdminRollen()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var f = w.Freigegeben(Fp(2), "1001");
        w.Katalog("Pilot", "Sanitäter", "Bordschütze", "Ingenieur", "Späher", "Händler", "Bergbau", "Frachter", "Koch");
        var m = w.Mitglieder;
        Pruefe.Wahr(m.KontoAendern(w.K, admin, f.KontoId, [1], null).Ok, "Admin trägt Pilot ein");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.LieblingsrollenSetzen(w.K, f, [42], null).Fehler, "Rolle nicht im Katalog");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.LieblingsrollenSetzen(w.K, f, [2, 3, 4, 5, 6, 7, 8, 9, 1], null).Fehler, "mehr als 8");
        Pruefe.Gleich(Fehlercode.Ungueltig, m.LieblingsrollenSetzen(w.K, f, [2], 3).Fehler, "erste Wahl nicht in der Liste");
        var r = m.LieblingsrollenSetzen(w.K, f, [1, 2, 3], 1);
        Pruefe.Wahr(r.Ok, "Wunsch gesetzt, erste Wahl darf eine eingetragene Rolle sein");
        var d = m.Stand.DatenVon(f.KontoId);
        Pruefe.Wahr(d.Lieblingsrollen.SequenceEqual([(ushort)2, (ushort)3]), "eingetragene Rolle steht nicht doppelt als Wunsch");
        Pruefe.Wahr(m.LieblingsrollenSetzen(w.K, f, [], null).Ok, "alle Wünsche fort");
        Pruefe.Wahr(m.Stand.DatenVon(f.KontoId).Rollen.SequenceEqual([(ushort)1]), "die Rolle des Admins bleibt");
        Pruefe.Gleich(Fehlercode.KeinRecht, m.LieblingsrollenSetzen(w.K, Akteur.AdminWerkzeug, [1], null).Fehler, "Konsole hat keine Wünsche");
        w.Buch.KontoSperren(Akteur.AdminWerkzeug, f.KontoId);
        Pruefe.Gleich(Fehlercode.KeinRecht, m.LieblingsrollenSetzen(w.K, f, [2], null).Fehler, "gesperrtes Konto");
    }

    /// <summary>
    /// Seit 0.11.8 (Sarevoc: "Untergilden entfallen ganz, überall"): Untergilden aus einem älteren Stand bleiben stehen, damit eine ältere
    /// Serverfassung sie wieder laden kann, und niemand wird mehr zugewiesen. Ändern rührt sie nicht an. Fällt ein Konto oder eine
    /// Vormerkung weg, fällt deren Nummer heraus (Datenschutz, 15.10.2026), die eines Kontos auch als Ansprechpartner; sonst bleibt
    /// jedes Feld, und eine Untergilde ohne die Nummer bleibt derselbe Datensatz.
    /// </summary>
    [Test]
    public static void Untergilden_AlterStandBleibt_GeloeschteNummernFallenHeraus()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var a = w.Freigegeben(Fp(2), "1001");
        var b = w.Freigegeben(Fp(3), "1002");
        var c = w.Freigegeben(Fp(5), "1004");
        var ohne = w.Freigegeben(Fp(6), "1005");
        var alt = new Untergilde(1, "Beispielstaffel", "BS") { Konten = [a.KontoId, b.KontoId], Vormerkungen = [1, 2], StammEinheit = 3, Ansprechpartner = a.KontoId };
        var zweite = new Untergilde(2, "Zweite Staffel", "ZS") { Konten = [c.KontoId], StammEinheit = 4, Ansprechpartner = b.KontoId };
        var m = new Mitgliederbuch(w.Uhr, new Mitgliederstand { Untergilden = Mitgliederstand.Leer.Untergilden.Add(1, alt).Add(2, zweite) });
        var gespeichert = m.Stand.Untergilden;
        var vm1 = m.Vormerken(w.K, admin, "Kommt Noch", null, null, "Notiz");
        var vm2 = m.Vormerken(w.K, admin, "Kommt Auch", null, null, null);
        Pruefe.Wahr(vm1.Ok && vm2.Ok && vm1.Ids[0] == 1 && vm2.Ids[0] == 2, "zwei Vormerkungen mit den Nummern aus der alten Untergilde");
        Pruefe.Wahr(m.KontoAendern(w.K, admin, a.KontoId, null, "Notiz").Ok, "Konto geändert");
        Pruefe.Wahr(m.VormerkungAendern(w.K, admin, 2, "Kommt Doch", null, null, null).Ok, "Vormerkung geändert");
        Pruefe.Wahr(ReferenceEquals(gespeichert, m.Stand.Untergilden), "Ändern lässt die Untergilden unverändert, nicht einmal neu gebaut");

        var neu = w.Freigegeben(Fp(4), "1003");
        Pruefe.Wahr(m.VormerkungUebernehmen(1, neu.KontoId) is { Ok: true } u && u.BetroffeneKonten.SequenceEqual([neu.KontoId]), "übernommen");
        Pruefe.Wahr(m.Stand.DatenVon(neu.KontoId).Notiz == "Notiz", "Notiz geht weiter an das Konto");
        Pruefe.Wahr(m.Stand.Untergilden[1] is { } u1 && u1.Vormerkungen.SequenceEqual([2u]) && u1.Konten.SequenceEqual([a.KontoId, b.KontoId]),
            "übernommene Vormerkung fällt heraus, das neue Konto kommt nicht hinein");
        Pruefe.Wahr(m.VormerkungLoeschen(w.K, admin, 2).Ok, "Vormerkung gelöscht");
        Pruefe.Wahr(m.Stand.Untergilden[1].Vormerkungen.IsEmpty, "gelöschte Vormerkung fällt heraus");

        m.KontoEntfernt(a.KontoId);
        Pruefe.Falsch(m.Stand.Mitglieder.ContainsKey(a.KontoId), "Mitgliedsdaten fort (Datenschutz)");
        Pruefe.Wahr(m.Stand.Untergilden[1] is { Name: "Beispielstaffel", Kuerzel: "BS", StammEinheit: 3, Ansprechpartner: null } g
            && g.Konten.SequenceEqual([b.KontoId]), "Kontonummer fort, auch als Ansprechpartner; sonst jedes Feld wie gespeichert");
        Pruefe.Wahr(ReferenceEquals(zweite, m.Stand.Untergilden[2]), "die andere Untergilde bleibt derselbe Datensatz");

        // Ohne Mitgliedsangaben, aber in einer Untergilde und als Ansprechpartner: Die Nummer fällt trotzdem heraus.
        Pruefe.Falsch(m.Stand.Mitglieder.ContainsKey(b.KontoId) || m.Stand.Mitglieder.ContainsKey(c.KontoId), "b und c ohne Mitgliedsangaben");
        m.KontoEntfernt(b.KontoId);
        Pruefe.Wahr(m.Stand.Untergilden[1].Konten.IsEmpty && m.Stand.Untergilden[2] is { Ansprechpartner: null, StammEinheit: 4 } z
            && z.Konten.SequenceEqual([c.KontoId]), "b aus der Liste und als Ansprechpartner der zweiten fort");
        m.KontoEntfernt(c.KontoId);
        Pruefe.Wahr(m.Stand.Untergilden[2] is { Name: "Zweite Staffel", Kuerzel: "ZS" } z2 && z2.Konten.IsEmpty, "c fort, Name und Kürzel bleiben");
        Pruefe.Gleich(2, m.Stand.Untergilden.Count, "keine Untergilde verschwindet, auch leer nicht");

        var version = m.Stand.Version;
        var vorher = m.Stand.Untergilden;
        m.KontoEntfernt(ohne.KontoId);
        Pruefe.Wahr(m.Stand.Version == version && ReferenceEquals(vorher, m.Stand.Untergilden), "Konto ohne Angaben und ohne Untergilde: nichts geändert");
    }

    [Test]
    public static void Stand_IndexNachDemLaden()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        w.Mitglieder.Vormerken(w.K, admin, "Geladen", null, null, null);
        // Nach dem Laden fehlt der Namensindex (er steht nicht in der Datei) und wird neu gebaut.
        var ohneIndex = w.Mitglieder.Stand with { NachScName = System.Collections.Immutable.ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal) };
        var geladen = new Mitgliederbuch(w.Uhr, ohneIndex);
        Pruefe.Wahr(geladen.NameBelegt("GELADEN"), "Index neu gebaut");
        w.Buch.ProfilSetzen(w.Freigegeben(Fp(2), "1001"), ["Kontoname"], null);
        var konten = w.K with { NachScName = System.Collections.Immutable.ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal) };
        Pruefe.Wahr(konten.MitScNamenIndex().NachScName.ContainsKey("KONTONAME"), "Kontenstand ebenso");

        // Discord-Namen: Konten und Vormerkungen, ohne offene Anträge.
        var mitDiscord = w.Freigegeben(Fp(3), "1002");
        Pruefe.Wahr(w.Buch.DiscordNameSetzen(Akteur.AdminWerkzeug, mitDiscord.KontoId, "Geladen/DC").Ok, "Discord-Name gesetzt");
        w.Antrag(4, "Antrag", "Antrag/DC");
        var leer = System.Collections.Immutable.ImmutableDictionary.Create<string, uint>(StringComparer.Ordinal);
        var neu = (w.K with { NachDiscordName = leer }).MitScNamenIndex();
        Pruefe.Wahr(neu.NachDiscordName.TryGetValue("GELADEN/DC", out var id) && id == mitDiscord.KontoId, "Discord-Name im Index");
        Pruefe.Falsch(neu.NachDiscordName.ContainsKey("ANTRAG/DC"), "ein Antrag steht nicht darin");
        // Ein Stand von vor A53 kann zwei Konten mit demselben Discord-Namen tragen: Er gehört dem mit der kleineren Konto-ID,
        // die anderen nennt die Warnung beim Start (Dienst), bis ein Admin sie ändert.
        var zweiter = w.Freigegeben(Fp(5), "1003");
        var alt = w.K with { Konten = w.K.Konten.SetItem(zweiter.KontoId, w.K.Konten[zweiter.KontoId] with { DiscordName = "ＧＥＬＡＤＥＮ/dc" }) };
        var doppelt = alt.MitScNamenIndex();
        Pruefe.Gleich(mitDiscord.KontoId, doppelt.NachDiscordName["GELADEN/DC"], "der kleineren Konto-ID");
        Pruefe.Wahr(doppelt.DoppelteDiscordNamen().Select(k => k.KontoId).SequenceEqual([zweiter.KontoId]), "die Warnung nennt das andere Konto");
        Pruefe.Wahr(neu.DoppelteDiscordNamen().Count == 0, "sonst keine Warnung");
        w.Mitglieder.Vormerken(w.K, admin, "Vorgemerkt Zwei", "Vorgemerkt/DC", null, null);
        var geladen2 = new Mitgliederbuch(w.Uhr, w.Mitglieder.Stand with { NachDiscordName = leer });
        Pruefe.Wahr(geladen2.DiscordNameVon("VORGEMERKT/DC") is not null, "Vormerkungen ebenso");
    }
}
