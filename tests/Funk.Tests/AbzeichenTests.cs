// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using Funk.Protokoll.Identitaet;
using Funk.Server.Kern;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Abzeichen (0.11.7, Sarevoc 14./15.10.2026): der feste Katalog, die Prüfregeln und Rechte des Mitgliederbuchs, die eigene Ansicht
/// (vorn, ausblenden), Laden einer alten <c>mitglieder.json</c>, Löschen mit dem Konto und die Bodies auf dem Draht. Ohne Server;
/// das Zusammenspiel steht in <see cref="AdminSitzungTests"/> (Zusammenspiel_Abzeichen).
/// </summary>
internal static class AbzeichenTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static Fingerabdruck Fp(int n) => new(Enumerable.Repeat((byte)n, 32).ToArray());

    /// <summary>Kontenbuch und Mitgliederbuch wie in der Zentrale (wie in <see cref="MitgliederTests"/>).</summary>
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
    }

    // ---------------------------------------------------------------- Katalog

    [Test]
    public static void Abzeichen_KatalogLaedt()
    {
        var laufbahnen = S.Abzeichenkatalog.Laufbahnen;
        Pruefe.Gleich("MARINE,NAVY,ORGA", string.Join(',', laufbahnen.Select(l => l.Kennung)), "drei Laufbahnen");
        Pruefe.Gleich("10,10,8", string.Join(',', laufbahnen.Select(l => l.Raenge.Length)), "Ränge je Laufbahn");
        Pruefe.Gleich(22, S.Abzeichenkatalog.Taetigkeiten.Length, "22 Tätigkeiten");
        var alle = laufbahnen.SelectMany(l => l.Raenge.Select(r => r.Kennung)).Concat(S.Abzeichenkatalog.Taetigkeiten.Select(t => t.Kennung)).ToList();
        Pruefe.Gleich(50, alle.Count, "50 Einträge");
        Pruefe.Gleich(50, alle.Distinct(StringComparer.Ordinal).Count(), "Kennungen eindeutig");
        Pruefe.Wahr(alle.All(k => k.Length is > 0 and <= S.Grenzen.AbzeichenKennungMax), "Kennungen höchstens 16 Zeichen");
        Pruefe.Wahr(alle.All(k => k.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')), "Kennungen aus A-Z, 0-9 und _");
        Pruefe.Gleich("Piloten,Waffen,Technik und Versorgung,Erkundung und Gewerbe", string.Join(',', S.Abzeichenkatalog.Gruppen), "Gruppen in Katalogfolge");

        Pruefe.Wahr(S.Abzeichenkatalog.Rang("MARINE_04") is { Laufbahn: "MARINE", Stufe: 4, Name: "Sergeant" }, "Rang mit Laufbahn und Stufe");
        Pruefe.Wahr(S.Abzeichenkatalog.Rang("ORGA_08") is { Stufe: 8 }, "höchste Orga-Stufe");
        Pruefe.Gleich("Marine: Sergeant", S.Abzeichenkatalog.Titel("MARINE_04"), "Titel eines Rangs");
        Pruefe.Gleich("Gunner M", S.Abzeichenkatalog.Titel("T_GUNNER_M"), "Titel einer Tätigkeit");
        Pruefe.Gleich("Kapitän", S.Abzeichenkatalog.Name("NAVY_10"), "Name");
        Pruefe.Gleich(10, S.Abzeichenkatalog.RaengeVon("NAVY").Length, "Ränge einer Laufbahn");
        Pruefe.Wahr(S.Abzeichenkatalog.RaengeVon("GIBTSNICHT").IsEmpty, "unbekannte Laufbahn: leer");
        Pruefe.Wahr(S.Abzeichenkatalog.IstTaetigkeit("T_SANI") && !S.Abzeichenkatalog.IstRang("T_SANI"), "Tätigkeit ist kein Rang");
        Pruefe.Wahr(S.Abzeichenkatalog.IstRang("NAVY_01") && !S.Abzeichenkatalog.IstTaetigkeit("NAVY_01"), "Rang ist keine Tätigkeit");
        Pruefe.Falsch(S.Abzeichenkatalog.Kennt("ADMIN") || S.Abzeichenkatalog.Kennt("FUEHRUNG") || S.Abzeichenkatalog.Kennt(null), "Zeichen stehen nicht im Katalog");
        Pruefe.Wahr(S.Abzeichenkatalog.IstWaehlbar("ADMIN") && S.Abzeichenkatalog.IstWaehlbar("EINSATZLEITUNG") && S.Abzeichenkatalog.IstWaehlbar("T_EVA"),
            "wählbar: Katalog und die automatischen Zeichen");
        Pruefe.Falsch(S.Abzeichenkatalog.IstWaehlbar("FUEHRUNG") || S.Abzeichenkatalog.IstWaehlbar("XYZ"), "FUEHRUNG und Unbekanntes nicht wählbar");
        Pruefe.Gleich("ADMIN,EINSATZLEITUNG,MARINE_10,NAVY_01,ORGA_02,T_JAEGER,T_AUSBILDER",
            string.Join(',', S.Abzeichenkatalog.Ordnen(["T_AUSBILDER", "ORGA_02", "T_JAEGER", "NAVY_01", "EINSATZLEITUNG", "MARINE_10", "ADMIN", "T_JAEGER"])),
            "Ordnen: Zeichen, Ränge in Laufbahnfolge, Tätigkeiten in Katalogfolge, ohne Doppelte");
        Pruefe.Gleich(Funk.Server.Kern.Zentrale.Zeichen.Admin, S.Abzeichenkatalog.Admin, "ADMIN wie im Server");
        Pruefe.Gleich(Funk.Server.Kern.Zentrale.Zeichen.Einsatzleitung, S.Abzeichenkatalog.Einsatzleitung, "EINSATZLEITUNG wie im Server");
        Pruefe.Gleich(Funk.Server.Kern.Zentrale.Zeichen.Fuehrung, S.Abzeichenkatalog.Fuehrung, "FUEHRUNG wie im Server");
    }

    // ---------------------------------------------------------------- Mitgliederbuch: Rechte und Prüfregeln

    [Test]
    public static void Abzeichen_NurInAdminSitzungMitKontenVerwalten()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var fritz = w.Freigegeben(Fp(2), "1001");
        var otto = w.Freigegeben(Fp(3), "1002", Stufe.Raidleiter);
        var vorher = w.Mitglieder.Stand;

        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, w.Mitglieder.AbzeichenSetzen(w.K, Akteur.Sitzung(admin.KontoId, admin.GeraetId), fritz.KontoId, ["MARINE_01"], []).Fehler,
            "ADMIN ohne Admin-Sitzung");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Mitglieder.AbzeichenSetzen(w.K, Akteur.Sitzung(otto.KontoId, otto.GeraetId), fritz.KontoId, ["MARINE_01"], []).Fehler,
            "Raidleiter vergibt keine Abzeichen");
        Pruefe.Gleich(Fehlercode.KeinRecht, w.Mitglieder.AbzeichenSetzen(w.K, Akteur.Sitzung(fritz.KontoId, fritz.GeraetId), fritz.KontoId, ["MARINE_10"], []).Fehler,
            "Funker nicht, auch nicht sich selbst");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Mitglieder.Stand), "ohne Seiteneffekt");

        var e = w.Mitglieder.AbzeichenSetzen(w.K, admin, fritz.KontoId, ["NAVY_02", "MARINE_04"], ["T_GUNNER_M", "T_JAEGER"]);
        Pruefe.Wahr(e.Ok && e.BetroffeneKonten.SequenceEqual([fritz.KontoId]), "in der Admin-Sitzung: " + e.Fehler);
        var d = w.Mitglieder.Stand.DatenVon(fritz.KontoId);
        Pruefe.Gleich("MARINE_04,NAVY_02", string.Join(',', d.Raenge), "Ränge in Laufbahnfolge gespeichert");
        Pruefe.Gleich("T_JAEGER,T_GUNNER_M", string.Join(',', d.Taetigkeiten), "Tätigkeiten in Katalogfolge gespeichert");
        Pruefe.Gleich("Marine: Sergeant, Navy: Maat, Jägerpilot, Gunner M", Mitgliederbuch.AbzeichenText(d), "Text fürs Journal");

        // Befördern ersetzt den Rang der Laufbahn; leere Listen nehmen alles weg.
        Pruefe.Wahr(w.Mitglieder.AbzeichenSetzen(w.K, admin, fritz.KontoId, ["MARINE_05", "NAVY_02"], ["T_JAEGER"]).Ok, "befördert");
        Pruefe.Gleich("MARINE_05,NAVY_02", string.Join(',', w.Mitglieder.Stand.DatenVon(fritz.KontoId).Raenge), "ersetzt");
        Pruefe.Wahr(w.Mitglieder.AbzeichenSetzen(w.K, admin, fritz.KontoId, [], []).Ok, "alles weg");
        Pruefe.Gleich("keine", Mitgliederbuch.AbzeichenText(w.Mitglieder.Stand.DatenVon(fritz.KontoId)), "nichts mehr");
        Pruefe.Gleich(Fehlercode.Unbekannt, w.Mitglieder.AbzeichenSetzen(w.K, admin, 4711, ["MARINE_01"], []).Fehler, "unbekanntes Konto");
        Pruefe.Wahr(w.Mitglieder.AbzeichenSetzen(w.K, Akteur.AdminWerkzeug, fritz.KontoId, ["ORGA_01"], []).Ok, "Konsole darf");
    }

    [Test]
    public static void Abzeichen_Pruefregeln()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var fritz = w.Freigegeben(Fp(2), "1001");
        var vorher = w.Mitglieder.Stand;
        var faelle = new (string Was, string[] Raenge, string[] Taetigkeiten, string Grund)[]
        {
            ("unbekannte Kennung", ["MARINE_99"], [], "unbekanntes Abzeichen MARINE_99"),
            ("unbekannte Tätigkeit", [], ["T_KOCH"], "unbekanntes Abzeichen T_KOCH"),
            ("zwei Ränge einer Laufbahn", ["MARINE_01", "MARINE_02"], [], "zwei Ränge in Marine"),
            ("derselbe Rang doppelt", ["NAVY_03", "NAVY_03"], [], "zwei Ränge in Navy"),
            ("vier Ränge", ["MARINE_01", "NAVY_01", "ORGA_01", "ORGA_02"], [], "höchstens ein Rang je Laufbahn"),
            ("7 Tätigkeiten", [], ["T_JAEGER", "T_BOMBER", "T_MEDIUM", "T_CAPITAL", "T_LANDUNG", "T_SCOUT", "T_SANI"], "höchstens 6 Tätigkeiten"),
            ("Rang als Tätigkeit", [], ["MARINE_01"], "MARINE_01 ist keine Tätigkeit"),
            ("Tätigkeit als Rang", ["T_SANI"], [], "T_SANI ist kein Rang"),
            ("Tätigkeit doppelt", [], ["T_SANI", "T_SANI"], "T_SANI doppelt"),
            ("Zeichen als Rang", ["ADMIN"], [], "unbekanntes Abzeichen ADMIN"),
        };
        foreach (var (was, raenge, taetigkeiten, grund) in faelle)
        {
            var e = w.Mitglieder.AbzeichenSetzen(w.K, admin, fritz.KontoId, raenge, taetigkeiten);
            Pruefe.Gleich(Fehlercode.Ungueltig, e.Fehler, was);
            Pruefe.Gleich(grund, e.Grund, was + ": Grund");
            Pruefe.Wahr(ReferenceEquals(vorher, w.Mitglieder.Stand), was + ": nichts geändert");
        }
        Pruefe.Wahr(w.Mitglieder.AbzeichenSetzen(w.K, admin, fritz.KontoId, ["MARINE_10", "NAVY_10", "ORGA_08"],
            ["T_JAEGER", "T_BOMBER", "T_MEDIUM", "T_CAPITAL", "T_LANDUNG", "T_SCOUT"]).Ok, "drei Ränge und sechs Tätigkeiten gehen");
    }

    [Test]
    public static void Abzeichen_EigeneAnsichtVornUndAus()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var fritz = w.Freigegeben(Fp(2), "1001");
        Pruefe.Wahr(w.Mitglieder.AbzeichenSetzen(w.K, admin, fritz.KontoId, ["MARINE_04"], ["T_SANI", "T_JAEGER"]).Ok, "vergeben");

        Pruefe.Gleich(Fehlercode.Ungueltig, w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, "XYZ", null).Fehler, "vorn unbekannt");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, "FUEHRUNG", null).Fehler, "vorn FUEHRUNG");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, null, ["T_SANI", "XYZ"]).Fehler, "aus mit Unbekanntem");
        Pruefe.Wahr(w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, null, null) is { Ok: true, BetroffeneKonten.Count: 0 }, "nichts angegeben: nichts geändert");

        Pruefe.Wahr(w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, "T_SANI", ["T_JAEGER", "ADMIN", "T_JAEGER"]).Ok, "vorn und aus");
        var d = w.Mitglieder.Stand.DatenVon(fritz.KontoId);
        Pruefe.Gleich("T_SANI", d.AbzeichenVorn, "vorn gespeichert");
        Pruefe.Gleich("ADMIN,T_JAEGER", string.Join(',', d.AbzeichenAus), "aus geordnet, ohne Doppelte");
        Pruefe.Gleich("MARINE_04,T_SANI", string.Join(',', Mitgliederbuch.SichtbareAbzeichen(d)), "sichtbar ohne Ausgeblendetes");
        Pruefe.Gleich("MARINE_04,T_JAEGER,T_SANI", string.Join(',', d.AlleAbzeichen()), "besessen bleibt alles");

        Pruefe.Wahr(w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, null, ["T_SANI"]).Ok, "nur aus geändert");
        d = w.Mitglieder.Stand.DatenVon(fritz.KontoId);
        Pruefe.Wahr(d.AbzeichenVorn == "T_SANI" && d.AbzeichenAus.SequenceEqual(["T_SANI"]), "vorn bleibt, aus ersetzt");
        Pruefe.Wahr(w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, "", []).Ok, "keine Wahl, nichts aus");
        d = w.Mitglieder.Stand.DatenVon(fritz.KontoId);
        Pruefe.Wahr(d.AbzeichenVorn is null && d.AbzeichenAus.IsEmpty, "geleert");
        Pruefe.Gleich("MARINE_04,T_JAEGER,T_SANI", string.Join(',', Mitgliederbuch.SichtbareAbzeichen(d)), "alles wieder sichtbar");
        // Ausblenden ändert nur die Anzeige, nie Rechte oder Besitz.
        Pruefe.Wahr(w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, null, ["MARINE_04", "T_JAEGER", "T_SANI"]).Ok, "alles aus");
        Pruefe.Gleich(3, w.Mitglieder.Stand.DatenVon(fritz.KontoId).AlleAbzeichen().Count(), "besessen bleibt");
        Pruefe.Wahr(Mitgliederbuch.SichtbareAbzeichen(w.Mitglieder.Stand.DatenVon(fritz.KontoId)).IsEmpty, "nichts sichtbar");
    }

    // ---------------------------------------------------------------- Ablage und Löschen

    [Test]
    public static void Abzeichen_AlteMitgliederJsonLaedtUndNeueUeberstehtNeustart()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "abzeichen-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var w = new Welt();
            var konto = w.Freigegeben(Fp(1), "5001").KontoId;
            var id = konto.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Eine mitglieder.json aus 0.11.6: ohne die vier neuen Felder.
            Directory.CreateDirectory(verzeichnis);
            var alt = "{\"katalog\":[],\"katalogVersion\":0,\"vormerkungen\":[],\"naechsteVormerkung\":1,"
                + "\"mitglieder\":[{\"kontoId\":" + id + ",\"rollen\":[],\"lieblingsrollen\":[],\"ersteWahl\":null,\"zeichenZeigen\":false,\"karteFreigeben\":true,\"notiz\":\"alt\"}],"
                + "\"untergilden\":[],\"version\":3}";
            File.WriteAllText(Path.Combine(verzeichnis, Zustandsablage.MitgliederDatei), alt, new UTF8Encoding(false));
            var geladen = new Zustandsablage(verzeichnis).MitgliederLaden();
            var d = geladen.DatenVon(konto);
            Pruefe.Wahr(geladen.Mitglieder.ContainsKey(konto) && d is { ZeichenZeigen: false, Notiz: "alt", AbzeichenVorn: null }
                && d.Raenge.IsEmpty && d.Taetigkeiten.IsEmpty && d.AbzeichenAus.IsEmpty, "alte Datei lädt, neue Felder leer");

            var buch = new Mitgliederbuch(TimeProvider.System, geladen);
            Pruefe.Wahr(buch.AbzeichenSetzen(w.K, Akteur.AdminWerkzeug, konto, ["ORGA_03"], ["T_FUNKER"]).Ok, "vergeben");
            Pruefe.Wahr(buch.AbzeichenAnsichtSetzen(konto, "ORGA_03", ["T_FUNKER"]).Ok, "Ansicht");
            new Zustandsablage(verzeichnis).MitgliederSpeichern(buch.Stand);
            var text = File.ReadAllText(Path.Combine(verzeichnis, Zustandsablage.MitgliederDatei));
            Pruefe.Falsch(text.Contains("alleAbzeichen", StringComparison.OrdinalIgnoreCase), "keine abgeleiteten Felder in der Datei");
            var wieder = new Zustandsablage(verzeichnis).MitgliederLaden().DatenVon(konto);
            Pruefe.Wahr(wieder.Raenge.SequenceEqual(["ORGA_03"]) && wieder.Taetigkeiten.SequenceEqual(["T_FUNKER"]) && wieder.AbzeichenVorn == "ORGA_03"
                && wieder.AbzeichenAus.SequenceEqual(["T_FUNKER"]) && wieder.Notiz == "alt", "übersteht den Neustart");
        }
        finally
        {
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, true);
        }
    }

    [Test]
    public static void Abzeichen_KontoGeloeschtRaeumtAuf()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var fritz = w.Freigegeben(Fp(2), "1001");
        Pruefe.Wahr(w.Mitglieder.AbzeichenSetzen(w.K, admin, fritz.KontoId, ["NAVY_05"], ["T_EVA"]).Ok, "vergeben");
        Pruefe.Wahr(w.Mitglieder.AbzeichenAnsichtSetzen(fritz.KontoId, "NAVY_05", ["T_EVA"]).Ok, "Ansicht");
        w.Mitglieder.KontoEntfernt(fritz.KontoId);
        Pruefe.Falsch(w.Mitglieder.Stand.Mitglieder.ContainsKey(fritz.KontoId), "Mitgliedsangaben weg");
        var d = w.Mitglieder.Stand.DatenVon(fritz.KontoId);
        Pruefe.Wahr(d.Raenge.IsEmpty && d.Taetigkeiten.IsEmpty && d.AbzeichenVorn is null && d.AbzeichenAus.IsEmpty, "Abzeichen weg");
    }

    // ---------------------------------------------------------------- Draht

    [Test]
    public static void Abzeichen_DrahtUndGrenzen()
    {
        var anfrage = new S.AbzeichenAnfrage { KontoId = 7, Raenge = ["MARINE_04"], Taetigkeiten = ["T_SANI"] };
        var json = Encoding.UTF8.GetString(S.SteuerJson.Serialisieren(anfrage));
        Pruefe.Gleich("{\"kontoId\":7,\"raenge\":[\"MARINE_04\"],\"taetigkeiten\":[\"T_SANI\"]}", json, "Abzeichen auf dem Draht");
        Pruefe.Wahr(S.SteuerJson.Lesen(Encoding.UTF8.GetBytes(json), S.SteuerJson.Info<S.AbzeichenAnfrage>()).Ok, "gelesen");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, S.SteuerJson.Lesen("{\"kontoId\":7,\"raenge\":[]}"u8, S.SteuerJson.Info<S.AbzeichenAnfrage>()).Fehler, "taetigkeiten ist Pflicht");
        var sieben = "{\"kontoId\":7,\"raenge\":[],\"taetigkeiten\":[\"A\",\"B\",\"C\",\"D\",\"E\",\"F\",\"G\"]}";
        Pruefe.Gleich(S.Fehlercode.ZuGross, S.SteuerJson.Lesen(Encoding.UTF8.GetBytes(sieben), S.SteuerJson.Info<S.AbzeichenAnfrage>()).Fehler, "7 Tätigkeiten: ZU_GROSS");
        var vier = "{\"kontoId\":7,\"raenge\":[\"A\",\"B\",\"C\",\"D\"],\"taetigkeiten\":[]}";
        Pruefe.Gleich(S.Fehlercode.ZuGross, S.SteuerJson.Lesen(Encoding.UTF8.GetBytes(vier), S.SteuerJson.Info<S.AbzeichenAnfrage>()).Fehler, "4 Ränge: ZU_GROSS");
        var lang = "{\"kontoId\":7,\"raenge\":[\"ABCDEFGHIJKLMNOPQ\"],\"taetigkeiten\":[]}";
        Pruefe.Gleich(S.Fehlercode.ZuGross, S.SteuerJson.Lesen(Encoding.UTF8.GetBytes(lang), S.SteuerJson.Info<S.AbzeichenAnfrage>()).Fehler, "Kennung über 16 Zeichen");

        // Alte Gegenstellen: ProfilSetzen, KontoStand und Praesenz ohne die neuen Felder bleiben gültig, neue Felder sind optional.
        Pruefe.Wahr(S.SteuerJson.Lesen("{\"zeichenZeigen\":true}"u8, S.SteuerJson.Info<S.ProfilSetzenAnfrage>()).Body is S.ProfilSetzenAnfrage { AbzeichenVorn: null, AbzeichenAus: null },
            "ProfilSetzen eines Clients bis 0.11.6");
        var profil = S.SteuerJson.Lesen("{\"abzeichenVorn\":\"\",\"abzeichenAus\":[\"ADMIN\",\"T_SANI\"]}"u8, S.SteuerJson.Info<S.ProfilSetzenAnfrage>());
        Pruefe.Wahr(profil.Body is S.ProfilSetzenAnfrage { AbzeichenVorn: "", AbzeichenAus.Count: 2 }, "ProfilSetzen mit Abzeichen");
        var ohne = new S.PraesenzEintrag { KontoId = 1, Name = "A", Benutzername = "a", Online = true };
        Pruefe.Falsch(Encoding.UTF8.GetString(S.SteuerJson.Serialisieren(new S.Praesenz { Voll = true, ZustandsVersion = 1, Eintraege = [ohne], Entfernt = [] }))
            .Contains("abzeichen", StringComparison.Ordinal), "Praesenz ohne Abzeichen schreibt keine Felder");
        var mit = ohne with { Abzeichen = ["MARINE_04", "T_SANI"], AbzeichenVorn = "T_SANI" };
        var gelesen = S.SteuerJson.LesenOderWerfen<S.Praesenz>(S.SteuerJson.Serialisieren(new S.Praesenz { Voll = true, ZustandsVersion = 1, Eintraege = [mit], Entfernt = [] }));
        Pruefe.Wahr(gelesen.Eintraege[0] is { AbzeichenVorn: "T_SANI", Abzeichen: ["MARINE_04", "T_SANI"] }, "Praesenz mit Abzeichen hin und zurück");

        Pruefe.Gleich("0.11.7", S.Versionen.ServerFassungFuer(S.Nachrichtentyp.Abzeichen), "0x0025 erst an Server ab 0.11.7");
        Pruefe.Falsch(S.Versionen.ServerKennt("0.11.6", S.Nachrichtentyp.Abzeichen), "Server 0.11.6 kennt 0x0025 nicht");
        Pruefe.Wahr(S.Versionen.ServerKennt("0.11.7", S.Nachrichtentyp.Abzeichen) && S.Versionen.ServerKennt("0.12.0", S.Nachrichtentyp.Abzeichen), "ab 0.11.7");
        Pruefe.Gleich(S.Ratengruppe.Verwaltung, S.Ratengrenzen.Gruppe(S.Nachrichtentyp.Abzeichen), "Ratengruppe wie Mitglied");
        Pruefe.Wahr(S.Katalog.Von(S.Nachrichtentyp.Abzeichen) is { HochAnfrage.Erlaubt: true, RunterAntwort.Erlaubt: true, RunterPush.Erlaubt: false }, "Anfrage hoch, Antwort runter");
        Pruefe.Falsch(S.Typbereiche.IstOptional((ushort)S.Nachrichtentyp.Abzeichen), "Pflichtanfrage");
    }
}
