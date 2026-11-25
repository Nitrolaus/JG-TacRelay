// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Text;
using Funk.Client.Dienste;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Server.Kern;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Meldesystem 0.12.0 (Sarevoc 11.10. und 16.10.2026, M1-M25 in SPRECHERANZEIGE-ENTWURF.md im Planungsordner) ohne Server: Katalog und Bilder, Bodies und Grenzen auf
/// dem Draht, Versionen und Ratengruppe, der laufende Stand mit gestellter Uhr (Befehl, Zustand, Ampel, Countdown, Bereit-Abfrage und
/// ihr Ende nach 2 min, Textmeldung), die Regel fürs Senden, der Haken im Mitgliederbuch samt alter <c>mitglieder.json</c>, der Uhrabgleich und
/// die Abbildung in die Lage des Clients. Das Zusammenspiel mit Server und Client-Kern steht in <see cref="MeldesystemServerTests"/>.
/// </summary>
internal static class MeldesystemTests
{
    private const long T0 = 1_800_000_000_000;

    private static S.MeldungSendenAnfrage A(string art, string? motiv = null, byte? groesse = null, string? zeile = null, long? ziel = null, string? form = null) =>
        new() { Art = art, Motiv = motiv, Groesse = groesse, Zeile = zeile, Ziel = ziel, Form = form };

    // ---------------------------------------------------------------- Katalog und Bilder

    [Test]
    public static void Katalog_MotiveArtenAusfuehrungen()
    {
        var motive = S.Meldungskatalog.Motive;
        Pruefe.Gleich(19, motive.Length, "19 Motive (17 aus dem Grafiksatz v3, Stopp mit zwei Bildern, dazu los, stopfen, pause)");
        Pruefe.Gleich("sammeln,sammelpunkt_x,stopp,rueckzug,start_freigegeben,medizinische_hilfe,los,stopfen",
            string.Join(',', S.Meldungskatalog.VonArt(S.Meldungsart.Befehl).Select(m => m.Kennung)), "8 Befehle");
        Pruefe.Gleich("einsatz_noch_nicht_gestartet,event_noch_nicht_gestartet,position_halten,funkdisziplin,pause",
            string.Join(',', S.Meldungskatalog.VonArt(S.Meldungsart.Zustand).Select(m => m.Kennung)), "5 Zustände");
        Pruefe.Gleich("ampel_rot_stopp,ampel_gelb_bereithalten,ampel_gruen_los", string.Join(',', S.Meldungskatalog.VonArt(S.Meldungsart.Ampel).Select(m => m.Kennung)), "Ampel rot, gelb, grün");
        Pruefe.Gleich("countdown_startzeit,countdown_restzeit", string.Join(',', S.Meldungskatalog.VonArt(S.Meldungsart.Countdown).Select(m => m.Kennung)), "zwei Countdowns");
        Pruefe.Gleich("bereit_melden", string.Join(',', S.Meldungskatalog.VonArt(S.Meldungsart.Bereit).Select(m => m.Kennung)), "Bereit-Abfrage");
        Pruefe.Wahr(motive.All(m => m.Kennung.Length <= S.Grenzen.MotivKennungMax && m.HatAusfuehrung(S.Meldungskatalog.Schlicht)), "alle in Schlicht, Kennung höchstens 32");
        Pruefe.Gleich("los,stopfen,pause", string.Join(',', motive.Where(m => !m.HatAusfuehrung(S.Meldungskatalog.Metall)).Select(m => m.Kennung)), "ohne Metall");
        Pruefe.Gleich("los,stopfen,pause", string.Join(',', motive.Where(m => m.HatAusfuehrung(S.Meldungskatalog.Schrift)).Select(m => m.Kennung)), "Nur Schrift");
        Pruefe.Gleich("Rückzug", S.Meldungskatalog.Motiv("rueckzug")?.Name, "Anzeigename");
        Pruefe.Wahr(S.Meldungskatalog.IstArt("ampel_gelb_bereithalten", S.Meldungsart.Ampel) && !S.Meldungskatalog.IstArt("sammeln", S.Meldungsart.Zustand), "Art");
        Pruefe.Falsch(S.Meldungskatalog.Kennt("feuer_frei") || S.Meldungskatalog.Kennt(null), "Unbekanntes");

        // Ersatz Schlicht, wenn die Ausführung fehlt; Stopp-Bild nach Wahl des Funkers.
        Pruefe.Gleich("metall", S.Meldungskatalog.AusfuehrungFuer("sammeln", "metall"), "Metall, wo es das gibt");
        Pruefe.Gleich("schlicht", S.Meldungskatalog.AusfuehrungFuer("los", "metall"), "los ohne Metall: Schlicht");
        Pruefe.Gleich("schrift", S.Meldungskatalog.AusfuehrungFuer("pause", "schrift"), "pause in Nur Schrift");
        Pruefe.Gleich("schlicht", S.Meldungskatalog.AusfuehrungFuer("sammeln", "schrift"), "sammeln ohne Nur Schrift: Schlicht");
        Pruefe.Gleich("stopp_handschuh", S.Meldungskatalog.Bild("stopp"), "Stopp ab Werk Handschuh");
        Pruefe.Gleich("stopp_xeno", S.Meldungskatalog.Bild("stopp", "stopp_xeno"), "Stopp Xeno gewählt");
        Pruefe.Gleich("sammeln", S.Meldungskatalog.Bild("sammeln", "stopp_xeno"), "die Stopp-Wahl gilt nur für Stopp");
        Pruefe.Gleich("Meldungen.metall.stopp_xeno.png", S.Meldungskatalog.Ressource("stopp", "metall", "stopp_xeno"), "Ressource");
        Pruefe.Gleich("Meldungen.schlicht.los.png", S.Meldungskatalog.Ressource("los", "metall"), "Ressource mit Ersatz");
        Pruefe.Gleich(null, S.Meldungskatalog.Ressource("gibtsnicht"), "unbekannt: keine Ressource");
        Pruefe.Gleich("countdown_restzeit", S.Meldungskatalog.MotivFuerForm("Restzeit"), "Form zu Motiv");
        Pruefe.Gleich("Startzeit", S.Meldungskatalog.FormFuerMotiv("countdown_startzeit"), "Motiv zu Form");
    }

    /// <summary>
    /// Jedes Bild des Katalogs liegt in jeder seiner Ausführungen im Client (RGBA-PNG, längste Seite 600, Seitenverhältnis wie die
    /// Vorlage), keine Ressource ohne Katalogzeile, zusammen höchstens 15 MB.
    /// </summary>
    [Test]
    public static void Katalog_BilderImClient()
    {
        var assembly = typeof(FunkLage).Assembly;
        var vorhanden = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Meldungen.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var soll = S.Meldungskatalog.Motive.SelectMany(m => m.Ausfuehrungen.SelectMany(a => m.Bilder.Select(b => S.Meldungskatalog.RessourceVon(a, b)))).ToList();
        Pruefe.Gleich(40, soll.Count, "40 Bilder: 20 Schlicht, 17 Metall, 3 Nur Schrift");
        Pruefe.Gleich("", string.Join(", ", soll.Where(s => !vorhanden.Contains(s))), "fehlende Bilder");
        Pruefe.Gleich("", string.Join(", ", vorhanden.Except(soll)), "Bilder ohne Katalogzeile");
        long summe = 0;
        foreach (var name in soll)
        {
            using var strom = assembly.GetManifestResourceStream(name)!;
            var kopf = new byte[26];
            strom.ReadExactly(kopf);
            summe += strom.Length;
            var breite = BinaryPrimitives.ReadInt32BigEndian(kopf.AsSpan(16));
            var hoehe = BinaryPrimitives.ReadInt32BigEndian(kopf.AsSpan(20));
            Pruefe.Wahr(kopf[1] == (byte)'P' && kopf[2] == (byte)'N' && kopf[3] == (byte)'G', name + ": PNG");
            Pruefe.Gleich((byte)6, kopf[25], name + ": RGBA");
            Pruefe.Gleich(600, Math.Max(breite, hoehe), name + ": längste Seite 600");
            var form = name.Contains("countdown", StringComparison.Ordinal) ? "600x400" : name.Contains("ampel", StringComparison.Ordinal) ? "400x600" : "600x600";
            Pruefe.Gleich(form, breite + "x" + hoehe, name + ": Seitenverhältnis");
        }
        Console.WriteLine($"Meldungsbilder: {soll.Count} Dateien, {summe / 1048576.0:F2} MB");
        Pruefe.Wahr(summe <= 15 * 1024 * 1024, "zusammen höchstens 15 MB, sind " + summe);
    }

    // ---------------------------------------------------------------- Draht, Grenzen, Versionen, Rate

    [Test]
    public static void Draht_BodiesUndGrenzen()
    {
        var a = A(S.Meldungsvorgang.Countdown, groesse: 2, zeile: "Rennstart", ziel: T0 + 600_000, form: "Startzeit");
        var json = Encoding.UTF8.GetString(S.SteuerJson.Serialisieren(a));
        Pruefe.Gleich("{\"art\":\"Countdown\",\"groesse\":2,\"zeile\":\"Rennstart\",\"ziel\":1800000600000,\"form\":\"Startzeit\"}", json, "MeldungSenden auf dem Draht");
        Pruefe.Wahr(S.SteuerJson.Lesen(Encoding.UTF8.GetBytes(json), S.SteuerJson.Info<S.MeldungSendenAnfrage>()).Body is S.MeldungSendenAnfrage { Groesse: 2, Form: "Startzeit" }, "gelesen");
        S.Fehlercode? Fehler(string body) => S.SteuerJson.Lesen(Encoding.UTF8.GetBytes(body), S.SteuerJson.Info<S.MeldungSendenAnfrage>()).Fehler;
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Fehler("{\"motiv\":\"sammeln\"}"), "art ist Pflicht");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Fehler("{\"art\":\"Befehl\",\"groesse\":4}"), "Größe 4");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, Fehler("{\"art\":\"Befehl\",\"groesse\":0}"), "Größe 0");
        Pruefe.Gleich(null, Fehler("{\"art\":\"Befehl\",\"zeile\":\"" + new string('x', 60) + "\"}"), "Zeile 60 Zeichen");
        Pruefe.Gleich(S.Fehlercode.ZuGross, Fehler("{\"art\":\"Befehl\",\"zeile\":\"" + new string('x', 61) + "\"}"), "Zeile 61 Zeichen");
        Pruefe.Gleich(S.Fehlercode.ZuGross, Fehler("{\"art\":\"Befehl\",\"motiv\":\"" + new string('a', 33) + "\"}"), "Motiv 33 Zeichen");
        Pruefe.Gleich(null, Fehler("{\"art\":\"Feuer frei\"}"), "eine unbekannte Art ist auf dem Draht gültig (offener Text), der Server lehnt sie ab");
        Pruefe.Wahr(S.SteuerJson.Lesen("{\"nummer\":3}"u8, S.SteuerJson.Info<S.BereitMeldenAnfrage>()).Body is S.BereitMeldenAnfrage { Nummer: 3 }, "BereitMelden");

        var eintrag = new S.Meldungseintrag { Motiv = "countdown_restzeit", Groesse = 3, Zeile = "Start", Von = 7, Seit = T0, Ziel = T0 + 90_000, Form = "Restzeit" };
        var stand = new S.Meldestand
        {
            Anlass = S.Meldungsvorgang.Befehl, Von = 7,
            Befehl = eintrag with { Motiv = "sammeln", Ziel = null, Form = null },
            Zustand = eintrag with { Motiv = "pause", Groesse = 1, Ziel = null, Form = null },
            Ampel = eintrag with { Motiv = "ampel_rot_stopp", Ziel = null, Form = null },
            Countdown = eintrag,
            Abfrage = eintrag with { Motiv = "bereit_melden", Nummer = 4, Form = null },
        };
        Pruefe.Gleich(stand, S.SteuerJson.LesenOderWerfen<S.Meldestand>(S.SteuerJson.Serialisieren(stand)), "Meldestand hin und zurück");
        var bereit = new S.BereitStand { Nummer = 4, Offen = true, Bereit = 18, Online = 22, Fehlen = [3, 5, 8, 13], Von = 7, Bis = T0 + 120_000 };
        Pruefe.Wahr(S.SteuerJson.LesenOderWerfen<S.BereitStand>(S.SteuerJson.Serialisieren(bereit)) is { Bereit: 18, Online: 22, Fehlen: [3, 5, 8, 13], Offen: true }, "BereitStand hin und zurück");
        var zuViele = bereit with { Fehlen = [.. Enumerable.Range(1, S.Grenzen.KontenMax + 1).Select(i => (uint)i)] };
        Pruefe.Gleich(S.Fehlercode.ZuGross, S.SteuerJson.Lesen(S.SteuerJson.Serialisieren(zuViele), S.SteuerJson.Info<S.BereitStand>()).Fehler, "fehlen höchstens 500");

        // Alte Gegenstellen: KontoStand und MitgliedEintrag ohne das neue Feld; ein Server übergeht es, wenn er es nicht kennt.
        var ks = S.SteuerJson.Lesen("{\"rechte\":[],\"scNamen\":[],\"rollen\":[],\"lieblingsrollen\":[],\"zeichen\":[],\"zeichenZeigen\":true}"u8, S.SteuerJson.Info<S.KontoStand>());
        Pruefe.Wahr(ks.Body is S.KontoStand { MeldungenSenden: null }, "KontoStand eines Servers bis 0.11.9");
        var mitglied = new S.MitgliedAnfrage { Op = S.MitgliedOperation.Aendern, KontoId = 7, MeldungenSenden = true };
        Pruefe.Gleich("{\"op\":\"AENDERN\",\"kontoId\":7,\"meldungenSenden\":true}", Encoding.UTF8.GetString(S.SteuerJson.Serialisieren(mitglied)), "Mitglied AENDERN nur mit dem Haken");
    }

    [Test]
    public static void Draht_VersionenUndRate()
    {
        Pruefe.Gleich("0.12.0", S.Versionen.MeldesystemAb, "Fassung");
        Pruefe.Gleich("0.12.0", S.Versionen.ServerFassungFuer(S.Nachrichtentyp.MeldungSenden), "0x0026 erst an Server ab 0.12.0");
        Pruefe.Gleich("0.12.0", S.Versionen.ServerFassungFuer(S.Nachrichtentyp.BereitMelden), "0x0027 erst an Server ab 0.12.0");
        Pruefe.Falsch(S.Versionen.ServerKennt("0.11.9", S.Nachrichtentyp.MeldungSenden) || S.Versionen.ServerKennt("0.11.9", S.Nachrichtentyp.BereitMelden), "Server 0.11.9 kennt sie nicht");
        Pruefe.Wahr(S.Versionen.ServerKennt("0.12.0", S.Nachrichtentyp.MeldungSenden) && S.Versionen.ServerKennt("0.12.1", S.Nachrichtentyp.BereitMelden), "ab 0.12.0");
        Pruefe.Gleich(S.Ratengruppe.Meldung, S.Ratengrenzen.Gruppe(S.Nachrichtentyp.MeldungSenden), "Ratengruppe Meldung");
        Pruefe.Gleich(new S.Rate(0.5, 1), S.Ratengrenzen.Von(S.Ratengruppe.Meldung), "1 je 2 s, Stoß 1");
        Pruefe.Gleich(new S.Rate(1, 2), S.Ratengrenzen.Von(S.Ratengrenzen.Gruppe(S.Nachrichtentyp.BereitMelden)), "Bereit 1/s, Stoß 2");
        Pruefe.Wahr(S.Katalog.Von(S.Nachrichtentyp.Meldestand).RunterPush.Erlaubt, "Meldestand: Push");
        Pruefe.Gleich(S.Zustaende.AktivSync | S.Zustaende.Aktiv, S.Katalog.Von(S.Nachrichtentyp.Meldestand).Zustaende, "in AKTIV_SYNC und AKTIV (Nachlieferung nach Welcome)");

        // Je Sender höchstens eine Meldung in 2 s; Bereit hat einen eigenen Eimer.
        static S.RahmenKopf Kopf(S.Nachrichtentyp typ) => new(28, 1, 0, (ushort)typ, 1);
        var b = new S.Steuerbremse(0);
        Pruefe.Gleich(S.Bremsurteil.Annehmen, b.Pruefen(Kopf(S.Nachrichtentyp.MeldungSenden), 0).Urteil, "erste Meldung");
        Pruefe.Gleich(S.Bremsurteil.Grenze, b.Pruefen(Kopf(S.Nachrichtentyp.MeldungSenden), 1_999).Urteil, "zweite vor 2 s: GRENZE");
        Pruefe.Gleich(S.Bremsurteil.Annehmen, b.Pruefen(Kopf(S.Nachrichtentyp.BereitMelden), 1_999).Urteil, "Bereit aus eigenem Eimer");
        // Genau auf der Grenze entscheidet die Rundung der Marken; der Client wartet ab der Antwort, also immer etwas länger.
        Pruefe.Gleich(S.Bremsurteil.Annehmen, b.Pruefen(Kopf(S.Nachrichtentyp.MeldungSenden), 2_001).Urteil, "nach 2 s wieder eine");
        Pruefe.Gleich(S.Bremsurteil.Grenze, b.Pruefen(Kopf(S.Nachrichtentyp.MeldungSenden), 2_001).Urteil, "aber nur eine");
    }

    // ---------------------------------------------------------------- Laufender Stand (Server, gestellte Uhr)

    [Test]
    public static void Stand_BefehlZustandAmpelCountdown()
    {
        var m = new Meldesystem();
        var e = m.Senden(7, A(S.Meldungsvorgang.Befehl, "sammeln", 3, "  Treffpunkt Hangar  "), T0);
        Pruefe.Wahr(e.Fehler is null && e.Push is { Anlass: "Befehl", Von: 7, Befehl: { Motiv: "sammeln", Groesse: 3, Zeile: "Treffpunkt Hangar", Von: 7, Seit: T0 } }, "Befehl im Push");
        Pruefe.Falsch(m.HatStand(T0), "ein Befehl bleibt nicht stehen (M20: keine vergangenen Befehle)");

        e = m.Senden(7, A(S.Meldungsvorgang.Zustand, "position_halten", 3), T0 + 1);
        Pruefe.Wahr(m.Zustand is { Motiv: "position_halten", Groesse: 1, Von: 7 } && e.Push is { Zustand.Motiv: "position_halten", Befehl: null }, "Zustand, immer klein");
        e = m.Senden(8, A(S.Meldungsvorgang.Zustand, "funkdisziplin"), T0 + 2);
        Pruefe.Wahr(m.Zustand is { Motiv: "funkdisziplin", Von: 8 }, "ein anderer Zustand ersetzt den laufenden, gleich von wem");
        e = m.Senden(7, A(S.Meldungsvorgang.Befehl, "stopp"), T0 + 3);
        Pruefe.Wahr(e.Push is { Befehl.Motiv: "stopp", Zustand.Motiv: "funkdisziplin" }, "der Zustand steht neben dem Befehl");

        e = m.Senden(7, A(S.Meldungsvorgang.Ampel, "ampel_rot_stopp", 2), T0 + 4);
        Pruefe.Wahr(m.Ampel is { Motiv: "ampel_rot_stopp", Groesse: 2 } && e.Push!.Ampel is not null, "Ampel rot, doppelt");
        m.Senden(7, A(S.Meldungsvorgang.Ampel, "ampel_gruen_los"), T0 + 5);
        Pruefe.Wahr(m.Ampel is { Motiv: "ampel_gruen_los", Groesse: 1 }, "grün ersetzt rot (von Hand, M22)");

        e = m.Senden(7, A(S.Meldungsvorgang.Countdown, groesse: 3, zeile: "Rennstart", ziel: T0 + 600_000, form: "Startzeit"), T0 + 6);
        Pruefe.Wahr(m.Countdown is { Motiv: "countdown_startzeit", Form: "Startzeit", Ziel: T0 + 600_000, Groesse: 3, Zeile: "Rennstart" }, "Countdown Start um");
        m.Senden(7, A(S.Meldungsvorgang.Countdown, "countdown_restzeit", ziel: T0 + 90_000, form: "Restzeit"), T0 + 7);
        Pruefe.Wahr(m.Countdown is { Motiv: "countdown_restzeit", Ziel: T0 + 90_000 }, "Restzeit ersetzt");
        Pruefe.Wahr(m.HatStand(T0 + 8) && m.StandBauen(S.Meldungsvorgang.Stand, null, null, T0 + 8) is { Zustand: not null, Ampel: not null, Countdown: not null, Befehl: null, Abfrage: null },
            "Stand nach Welcome: Zustand, Ampel, Countdown, kein Befehl");

        // Regeln: Verstöße ändern nichts und nennen den Grund.
        var vorher = (m.Zustand, m.Ampel, m.Countdown);
        foreach (var (anfrage, grund) in new (S.MeldungSendenAnfrage, string)[]
        {
            (A("Feuer"), "unbekannte Art Feuer"),
            (A(S.Meldungsvorgang.Befehl), "Befehl braucht ein Motiv"),
            (A(S.Meldungsvorgang.Befehl, "gibtsnicht"), "unbekanntes Motiv gibtsnicht"),
            (A(S.Meldungsvorgang.Befehl, "position_halten"), "position_halten ist kein Motiv für Befehl"),
            (A(S.Meldungsvorgang.Zustand, "sammeln"), "sammeln ist kein Motiv für Zustand"),
            (A(S.Meldungsvorgang.Ampel, "sammeln"), "sammeln ist kein Motiv für Ampel"),
            (A(S.Meldungsvorgang.Countdown, ziel: T0 + 600_000), "Countdown braucht die Form Startzeit oder Restzeit"),
            (A(S.Meldungsvorgang.Countdown, "countdown_startzeit", ziel: T0 + 600_000, form: "Restzeit"), "countdown_startzeit passt nicht zur Form Restzeit"),
            (A(S.Meldungsvorgang.Countdown, form: "Startzeit"), "Countdown braucht eine Zielzeit"),
            (A(S.Meldungsvorgang.Countdown, form: "Startzeit", ziel: T0 + 10), "Die Zielzeit ist schon vorbei"),
            (A(S.Meldungsvorgang.Countdown, form: "Restzeit", ziel: T0 + 10 + S.Grenzen.CountdownVorlaufMaxMs + 1), "Die Zielzeit liegt mehr als 24 Stunden voraus"),
            (A(S.Meldungsvorgang.BereitAbfrage, "sammeln"), "sammeln ist kein Motiv für Bereit-Abfrage"),
        })
        {
            var f = m.Senden(7, anfrage, T0 + 10);
            Pruefe.Gleich(S.Fehlercode.Ungueltig, f.Fehler, anfrage.Art + ": UNGUELTIG");
            Pruefe.Gleich(grund, f.Grund, anfrage.Art + ": Grund");
            Pruefe.Wahr(f.Push is null && (m.Zustand, m.Ampel, m.Countdown) == vorher, anfrage.Art + ": nichts geändert");
        }

        // Ein Countdown nach dem Ziel fällt still aus dem Stand; aufheben, aus und abbrechen leeren die Plätze.
        Pruefe.Wahr(m.StandBauen(S.Meldungsvorgang.Stand, null, null, T0 + 90_000).Countdown is null, "nach dem Ziel nicht mehr im Push");
        Pruefe.Wahr(m.Takt(T0 + 90_000) is null && m.Countdown is null, "Takt: still entfernt, nichts zu senden");
        m.Senden(7, A(S.Meldungsvorgang.Countdown, form: "Restzeit", ziel: T0 + 200_000), T0 + 100_000);
        Pruefe.Wahr(m.Senden(7, A(S.Meldungsvorgang.CountdownAbbrechen), T0 + 100_001).Push is { Countdown: null } && m.Countdown is null, "Countdown abgebrochen");
        Pruefe.Wahr(m.Senden(7, A(S.Meldungsvorgang.ZustandAufheben), T0 + 100_002).Push is { Anlass: "ZustandAufheben", Zustand: null }, "Zustand aufgehoben");
        Pruefe.Wahr(m.Senden(7, A(S.Meldungsvorgang.AmpelAus), T0 + 100_003).Push is { Ampel: null } && !m.HatStand(T0 + 100_004), "Ampel aus, nichts läuft mehr");
    }

    [Test]
    public static void Stand_BereitAbfrageZaehlenUndEndeNachZweiMinuten()
    {
        var m = new Meldesystem();
        var e = m.Senden(7, A(S.Meldungsvorgang.BereitAbfrage, groesse: 2, zeile: "Alle bereit?"), T0);
        Pruefe.Wahr(e.BereitOffen && e.Push is { Abfrage: { Motiv: "bereit_melden", Nummer: 1, Groesse: 2, Ziel: T0 + 120_000, Zeile: "Alle bereit?" } }, "Abfrage 1, ab Werk bereit_melden, Ende nach 2 min");
        Pruefe.Wahr(m.HatStand(T0), "eine offene Abfrage bekommt auch, wer später kommt (M20)");
        Pruefe.Gleich((bool?)true, m.Bereit(9, 1, T0 + 1000), "Funker 9 bereit");
        Pruefe.Gleich((bool?)false, m.Bereit(9, 1, T0 + 2000), "zweimal ist wie einmal");
        Pruefe.Gleich((bool?)null, m.Bereit(9, 2, T0 + 2000), "falsche Nummer");
        m.Bereit(11, 1, T0 + 3000);

        // n von m: online sind 7, 8, 9, 10; 11 meldete bereit und ging dann (zählt nicht mehr). 7 hat gefragt und zählt nicht mit (weder in m noch
        // unter "fehlt", Schritt 2 vom 16.10.2026), auch wenn er selbst bereit meldet.
        Pruefe.Gleich((bool?)true, m.Bereit(7, 1, T0 + 3500), "der Fragende kann melden, es ändert nichts an der Zahl");
        var stand = Meldesystem.BereitStandBauen(m.Abfrage!, m.Bereite, [10, 7, 9, 8], offen: true);
        Pruefe.Wahr(stand is { Nummer: 1, Offen: true, Bereit: 1, Online: 3, Fehlen: [8, 10], Von: 7, Bis: T0 + 120_000 }, "1 von 3 bereit, es fehlen 8 und 10 (7 fragt)");

        // Eine neue Abfrage ersetzt die laufende.
        e = m.Senden(8, A(S.Meldungsvorgang.BereitAbfrage), T0 + 10_000);
        Pruefe.Wahr(e.Push!.Abfrage is { Nummer: 2, Von: 8, Groesse: 1 } && m.Bereite.Count == 0, "neue Nummer, niemand bereit");
        Pruefe.Gleich((bool?)null, m.Bereit(9, 1, T0 + 10_001), "auf die alte Nummer nicht mehr");
        m.Bereit(9, 2, T0 + 10_002);

        // Nach 2 min von selbst zu: Push an alle (BereitAbgelaufen), der letzte Stand für die Leitung.
        Pruefe.Wahr(m.Takt(T0 + 129_999) is null, "vor 2 min nichts");
        var ende = m.Takt(T0 + 130_000);
        Pruefe.Wahr(ende is { Push: { Anlass: "BereitAbgelaufen", Von: null, Abfrage: null }, BereitEnde: { Abfrage.Nummer: 2 } } && ende.BereitEnde!.Bereite.SetEquals([9u]),
            "nach 2 min abgelaufen, mit dem letzten Stand");
        Pruefe.Wahr(Meldesystem.BereitStandBauen(ende!.BereitEnde!.Abfrage, ende.BereitEnde.Bereite, [7, 8, 9], offen: false) is { Offen: false, Bereit: 1, Online: 2, Fehlen: [7] },
            "letzter Bereit-Stand: zu (8 hat gefragt und zählt nicht)");
        Pruefe.Wahr(m.Abfrage is null && m.Bereit(9, 2, T0 + 130_001) is null && m.Takt(T0 + 130_002) is null, "danach zu");

        // Schließen durch die Leitung; schon zu ist kein Fehler.
        Pruefe.Wahr(m.Senden(7, A(S.Meldungsvorgang.BereitSchliessen), T0 + 140_000) is { Fehler: null, Push: null, BereitEnde: null }, "nichts offen: nichts zu tun");
        m.Senden(7, A(S.Meldungsvorgang.BereitAbfrage), T0 + 150_000);
        var zu = m.Senden(8, A(S.Meldungsvorgang.BereitSchliessen), T0 + 151_000);
        Pruefe.Wahr(zu is { Push: { Anlass: "BereitSchliessen", Von: 8, Abfrage: null }, BereitEnde.Abfrage.Nummer: 3 } && m.Abfrage is null, "geschlossen von 8");
    }

    // ---------------------------------------------------------------- Textmeldung (M24)

    private static S.MeldungSendenAnfrage T(string? text, string? farbe = null, byte? groesse = null, string? dauer = null) =>
        new() { Art = S.Meldungsvorgang.Text, Text = text, Farbe = farbe, Groesse = groesse, Dauer = dauer };

    /// <summary>
    /// Die Textmeldung auf dem Draht: Art Text und TextAufheben (offener Text, anfragbar), Felder text, farbe, dauer; 120 Zeichen gehen, 121 sind ZU_GROSS
    /// (und ZU_GROSS trennt, 4.9), Farbe und Dauer höchstens 16; im Meldestand das Feld text mit Pflichtangaben. Die festen Farben und Dauern.
    /// </summary>
    [Test]
    public static void Draht_Textmeldung()
    {
        Pruefe.Wahr(S.Meldungsvorgang.IstAnfragbar("Text") && S.Meldungsvorgang.IstAnfragbar("TextAufheben"), "Text und TextAufheben sind anfragbar");
        Pruefe.Gleich("weiss,gelb,orange,rot,gruen,blau", string.Join(',', S.Textmeldungen.Farben), "sechs feste Farben, Weiß zuerst");
        Pruefe.Gleich("8s,bleibt", string.Join(',', S.Textmeldungen.Dauern), "8 s (ab Werk) oder bleibt");
        Pruefe.Wahr(S.Textmeldungen.IstFarbe("orange") && !S.Textmeldungen.IstFarbe("Orange") && !S.Textmeldungen.IstFarbe(null), "Farbe als Kennung, genau");
        Pruefe.Wahr(S.Textmeldungen.IstDauer("bleibt") && !S.Textmeldungen.IstDauer("8 s"), "Dauer als Kennung, genau");

        var a = T("Alle zum Hangar 2", "gelb", 2, "bleibt");
        var json = Encoding.UTF8.GetString(S.SteuerJson.Serialisieren(a));
        Pruefe.Gleich("{\"art\":\"Text\",\"groesse\":2,\"text\":\"Alle zum Hangar 2\",\"farbe\":\"gelb\",\"dauer\":\"bleibt\"}", json, "MeldungSenden mit Text auf dem Draht");
        S.Fehlercode? Fehler(string body) => S.SteuerJson.Lesen(Encoding.UTF8.GetBytes(body), S.SteuerJson.Info<S.MeldungSendenAnfrage>()).Fehler;
        Pruefe.Gleich(null, Fehler("{\"art\":\"Text\",\"text\":\"" + new string('x', 120) + "\"}"), "Text 120 Zeichen");
        Pruefe.Gleich(S.Fehlercode.ZuGross, Fehler("{\"art\":\"Text\",\"text\":\"" + new string('x', 121) + "\"}"), "Text 121 Zeichen: ZU_GROSS");
        Pruefe.Gleich(null, Fehler("{\"art\":\"Text\",\"text\":\"" + string.Concat(Enumerable.Repeat("🚀", 120)) + "\"}"), "120 Raketen (Zeichenpaare) zählen 120");
        Pruefe.Wahr(S.Fehlercodes.Trennt(S.Fehlercode.ZuGross), "ZU_GROSS trennt die Verbindung (darum begrenzt der Client hart)");
        Pruefe.Gleich(S.Fehlercode.ZuGross, Fehler("{\"art\":\"Text\",\"text\":\"a\",\"farbe\":\"" + new string('f', 17) + "\"}"), "Farbe über 16");
        Pruefe.Gleich(S.Fehlercode.ZuGross, Fehler("{\"art\":\"Text\",\"text\":\"a\",\"dauer\":\"" + new string('d', 17) + "\"}"), "Dauer über 16");
        Pruefe.Gleich(null, Fehler("{\"art\":\"Text\",\"text\":\"a\",\"farbe\":\"lila\"}"), "eine unbekannte Farbe ist auf dem Draht gültig, der Server lehnt sie ab");

        var stand = new S.Meldestand
        {
            Anlass = S.Meldungsvorgang.Text, Von = 7,
            Text = new S.Textmeldung { Text = "Pause bis 21 Uhr", Farbe = "blau", Groesse = 3, Dauer = "bleibt", Von = 7, Seit = T0 },
        };
        Pruefe.Gleich(stand, S.SteuerJson.LesenOderWerfen<S.Meldestand>(S.SteuerJson.Serialisieren(stand)), "Meldestand mit Text hin und zurück");
        Pruefe.Gleich(S.Fehlercode.Ungueltig, S.SteuerJson.Lesen("{\"anlass\":\"Text\",\"text\":{\"farbe\":\"rot\",\"groesse\":1,\"dauer\":\"8s\",\"von\":7,\"seit\":1}}"u8,
            S.SteuerJson.Info<S.Meldestand>()).Fehler, "im Meldestand ist der Text Pflicht");
        Pruefe.Wahr(S.Textmeldungen.Bleibend(stand.Text) is not null && S.Textmeldungen.Bleibend(stand.Text! with { Dauer = "8s" }) is null && S.Textmeldungen.Bleibend(null) is null,
            "nur eine bleibende gehört zum Stand");
    }

    /// <summary>
    /// Textmeldung im laufenden Stand (Server, gestellte Uhr): "bleibt" steht im Stand, in jedem Push und nach Welcome; "8 s" (ab Werk, Weiß, normal) nur
    /// im Push ihres Anlasses und ersetzt eine bleibende; eine neue ersetzt die laufende, gleich von wem; aufheben leert. Text getrimmt; ohne Text, mit
    /// unbekannter Farbe oder Dauer UNGUELTIG mit Klartext, ohne Seiteneffekt; Motiv und Zeile zählen nicht.
    /// </summary>
    [Test]
    public static void Stand_Textmeldung_BleibtUndAchtSekunden()
    {
        var m = new Meldesystem();
        var e = m.Senden(7, T("  Alle zum Hangar 2  ", "gelb", 2, "bleibt") with { Motiv = "sammeln", Zeile = "egal" }, T0);
        Pruefe.Wahr(e.Fehler is null && e.Push is { Anlass: "Text", Von: 7, Befehl: null, Text: { Text: "Alle zum Hangar 2", Farbe: "gelb", Groesse: 2, Dauer: "bleibt", Von: 7, Seit: T0 } },
            "bleibende Textmeldung im Push, getrimmt, ohne Befehl");
        Pruefe.Wahr(m.Text is { Text: "Alle zum Hangar 2" } && m.HatStand(T0 + 1), "sie gehört zum Stand (M20)");
        Pruefe.Wahr(m.StandBauen(S.Meldungsvorgang.Stand, null, null, T0 + 1).Text?.Farbe == "gelb", "nach Welcome nachgeliefert");
        e = m.Senden(8, A(S.Meldungsvorgang.Zustand, "pause"), T0 + 2);
        Pruefe.Wahr(e.Push is { Anlass: "Zustand", Text.Text: "Alle zum Hangar 2", Zustand.Motiv: "pause" }, "steht auch in jedem anderen Push");

        // Fehler ändern nichts.
        foreach (var (anfrage, grund) in new (S.MeldungSendenAnfrage, string)[]
        {
            (T(null), "Textmeldung braucht einen Text"),
            (T("   "), "Textmeldung braucht einen Text"),
            (T("Achtung", "lila"), "unbekannte Farbe lila"),
            (T("Achtung", "Gelb"), "unbekannte Farbe Gelb"),
            (T("Achtung", dauer: "5min"), "unbekannte Dauer 5min"),
        })
        {
            var f = m.Senden(7, anfrage, T0 + 3);
            Pruefe.Wahr(f.Fehler == S.Fehlercode.Ungueltig && f.Grund == grund && f.Push is null, "UNGUELTIG: " + grund + " (war " + f.Grund + ")");
            Pruefe.Wahr(m.Text is { Text: "Alle zum Hangar 2", Farbe: "gelb" }, "nichts geändert: " + grund);
        }

        // 8 s (ab Werk Weiß, normal): nur im Push, ersetzt die bleibende, gleich von wem.
        e = m.Senden(9, T("Funkstille"), T0 + 4);
        Pruefe.Wahr(e.Push is { Anlass: "Text", Von: 9, Text: { Text: "Funkstille", Farbe: "weiss", Groesse: 1, Dauer: "8s", Von: 9 } }, "8 s im Push, Weiß, normal");
        Pruefe.Wahr(m.Text is null, "die bleibende ist ersetzt");
        Pruefe.Wahr(m.StandBauen(S.Meldungsvorgang.Stand, null, null, T0 + 5).Text is null, "ein Text mit 8 s wird nicht nachgeliefert");
        Pruefe.Wahr(m.Senden(7, A(S.Meldungsvorgang.ZustandAufheben), T0 + 6).Push is { Text: null } && !m.HatStand(T0 + 6), "kein Text mehr in späteren Pushes, nichts läuft");

        // Eine neue bleibende ersetzt, aufheben leert.
        m.Senden(7, T("Pause bis 21 Uhr", "blau", 3, "bleibt"), T0 + 7);
        m.Senden(8, T("Sammeln am Tor", "gruen", 1, "bleibt"), T0 + 8);
        Pruefe.Wahr(m.Text is { Text: "Sammeln am Tor", Farbe: "gruen", Von: 8 }, "eine neue ersetzt die laufende, gleich von wem");
        e = m.Senden(7, new S.MeldungSendenAnfrage { Art = S.Meldungsvorgang.TextAufheben }, T0 + 9);
        Pruefe.Wahr(e.Push is { Anlass: "TextAufheben", Von: 7, Text: null } && m.Text is null && !m.HatStand(T0 + 9), "aufgehoben");
    }

    /// <summary>
    /// Im Client: Das Lagebild behält nur eine bleibende Textmeldung (eine mit 8 s ist ein Ereignis wie der Befehl); die Attrappe prüft wie der Server und
    /// legt den Text mit 8 s nur ins Ereignis, den bleibenden auch in die Lage.
    /// </summary>
    [Test]
    public static async Task Client_Textmeldung_LagebildUndAttrappe()
    {
        var lage = new Lagebild();
        var acht = new S.Textmeldung { Text = "Funkstille", Farbe = "weiss", Groesse = 1, Dauer = "8s", Von = 7, Seit = T0 };
        lage.MeldestandSetzen(new S.Meldestand { Anlass = S.Meldungsvorgang.Text, Text = acht });
        Pruefe.Wahr(lage.Meldestand is { Text: null }, "Lagebild: Text mit 8 s nicht im Stand");
        lage.MeldestandSetzen(new S.Meldestand { Anlass = S.Meldungsvorgang.Text, Text = acht with { Dauer = "bleibt" } });
        Pruefe.Wahr(lage.Meldestand is { Text.Dauer: "bleibt" }, "Lagebild: bleibender Text im Stand");

        var d = new FunkDienstAttrappe(FunkDienstAttrappe.Beispiel());
        var jetzt = new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
        d.Uhr = () => jetzt;
        var ereignisse = new List<MeldungEingegangen>();
        d.Ereignis += e => { if (e is MeldungEingegangen m) ereignisse.Add(m); };
        d.LageSetzen(l => l with { Konto = l.Konto! with { MeldungenSenden = true } });
        Pruefe.Gleich(null, await d.MeldungSendenAsync(T("Alle zum Hangar 2", "gelb", 2, "bleibt")), "bleibender Text");
        Pruefe.Wahr(ereignisse[^1].Stand is { Anlass: "Text", Text: { Farbe: "gelb", Groesse: 2 } } && d.Lage.Meldestand?.Text?.Dauer == "bleibt", "im Ereignis und in der Lage");
        Pruefe.Gleich("Meldung Text x2 gelb bleibt \"Alle zum Hangar 2\"", d.Aufrufe[^1], "gemerkt");
        jetzt = jetzt.AddSeconds(2);
        Pruefe.Gleich("unbekannte Farbe lila.", await d.MeldungSendenAsync(T("x", "lila")), "Farbe ungültig: der Satz wie vom Server");
        jetzt = jetzt.AddSeconds(2);
        Pruefe.Gleich(null, await d.MeldungSendenAsync(T("Funkstille")), "Text mit 8 s");
        Pruefe.Wahr(ereignisse[^1].Stand.Text is { Dauer: "8s", Farbe: "weiss" } && d.Lage.Meldestand is { Text: null }, "nur im Ereignis, der bleibende ist ersetzt");
        jetzt = jetzt.AddSeconds(2);
        Pruefe.Gleich(null, await d.MeldungSendenAsync(new S.MeldungSendenAnfrage { Art = S.Meldungsvorgang.TextAufheben }), "aufheben");
        Pruefe.Wahr(ereignisse[^1].Stand is { Anlass: "TextAufheben", Text: null }, "aufgehoben");
    }

    // ---------------------------------------------------------------- Recht (M1, M15-M18)

    [Test]
    public static void Recht_HakenStufeAuftragFunkermodus()
    {
        foreach (var (was, haken, stufe, auftrag, funker, darf) in new (string, bool, S.Stufe?, bool, bool, bool)[]
        {
            ("Führung mit Haken", true, S.Stufe.Raidleiter, false, false, true),
            ("Admin mit Haken", true, S.Stufe.Admin, false, false, true),
            ("Einsatzleitung mit Haken und laufendem Auftrag", true, S.Stufe.Funker, true, false, true),
            ("Funker mit Haken (bleibt stehen, wirkt nicht)", true, S.Stufe.Funker, false, false, false),
            ("Einsatzleitung mit Haken, Auftrag noch nicht oder nicht mehr", true, S.Stufe.Funker, false, false, false),
            ("Führung ohne Haken", false, S.Stufe.Raidleiter, false, false, false),
            ("Admin ohne Haken", false, S.Stufe.Admin, true, false, false),
            ("Einsatzleitung mit Auftrag ohne Haken", false, S.Stufe.Funker, true, false, false),
            ("Admin mit Haken, heute als Funker", true, S.Stufe.Admin, false, true, false),
            ("Admin mit Haken und Auftrag, heute als Funker", true, S.Stufe.Admin, true, true, false),
        })
        {
            Pruefe.Gleich(darf, S.Melderecht.Darf(haken, stufe, auftrag, funker), was);
        }
    }

    // ---------------------------------------------------------------- Haken im Mitgliederbuch und in mitglieder.json

    private static Fingerabdruck Fp(int n) => new(Enumerable.Repeat((byte)n, 32).ToArray());

    private static Akteur Freigegeben(Kontenbuch buch, int fp, string discordId, Stufe stufe)
    {
        var v = 900L + fp;
        var herkunft = stufe == Stufe.Funker ? Herkunft.Bot : Herkunft.AdminWerkzeug;
        var a = buch.VerknuepfungAnfragen(new CodeBindung(v, Fp(fp), "Rechner", SchluesselArt.Software), discordId, "n" + discordId, "Name " + discordId, herkunft, stufe);
        Pruefe.Wahr(a.Ok, "Anfrage " + discordId);
        var e = buch.VerknuepfungBeantworten(v, true);
        Pruefe.Wahr(buch.GeraetEntscheiden(Akteur.AdminWerkzeug, e.GeraetId, Entscheidung.Freigeben).Ok, "Freigabe " + discordId);
        return Akteur.Verwaltend(e.KontoId, e.GeraetId, bestaetigt: true);
    }

    [Test]
    public static void Haken_NurInAdminSitzungUndAlteDateiLaedt()
    {
        var uhr = new IdentitaetsUhr(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var buch = new Kontenbuch(uhr);
        var mitglieder = new Mitgliederbuch(uhr);
        var admin = Freigegeben(buch, 1, "9001", Stufe.Admin);
        var otto = Freigegeben(buch, 2, "1002", Stufe.Raidleiter);
        var fritz = Freigegeben(buch, 3, "1003", Stufe.Funker);
        mitglieder.KontoAendern(buch.Stand, admin, otto.KontoId, [], "Notiz");
        var vorher = mitglieder.Stand;

        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, mitglieder.KontoAendern(buch.Stand, Akteur.Sitzung(admin.KontoId, admin.GeraetId), otto.KontoId, null, null, true).Fehler,
            "ADMIN ohne Admin-Sitzung");
        Pruefe.Gleich(Fehlercode.KeinRecht, mitglieder.KontoAendern(buch.Stand, Akteur.Sitzung(otto.KontoId, otto.GeraetId), otto.KontoId, null, null, true).Fehler,
            "die Führung ernennt sich nicht selbst");
        Pruefe.Wahr(ReferenceEquals(vorher, mitglieder.Stand), "ohne Seiteneffekt");

        Pruefe.Wahr(mitglieder.KontoAendern(buch.Stand, admin, otto.KontoId, null, null, true).Ok, "in der Admin-Sitzung");
        Pruefe.Wahr(mitglieder.Stand.DatenVon(otto.KontoId) is { MeldungenSenden: true, Notiz: "Notiz" }, "Haken gesetzt, Notiz bleibt");
        Pruefe.Wahr(mitglieder.KontoAendern(buch.Stand, admin, otto.KontoId, [], "andere", null).Ok && mitglieder.Stand.DatenVon(otto.KontoId).MeldungenSenden,
            "ohne Angabe bleibt der Haken");
        Pruefe.Wahr(mitglieder.KontoAendern(buch.Stand, admin, fritz.KontoId, null, null, true).Ok && mitglieder.Stand.DatenVon(fritz.KontoId).MeldungenSenden,
            "bei einem Funker bleibt er stehen (M16)");
        Pruefe.Wahr(mitglieder.KontoAendern(buch.Stand, admin, otto.KontoId, null, null, false).Ok && !mitglieder.Stand.DatenVon(otto.KontoId).MeldungenSenden,
            "weggenommen (M18)");

        var verzeichnis = Path.Combine(Testserver.Wurzel(), "melde-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(verzeichnis);
            var id = otto.KontoId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Eine mitglieder.json aus 0.11.9: mit Abzeichen, ohne den Haken.
            var alt = "{\"katalog\":[],\"katalogVersion\":0,\"vormerkungen\":[],\"naechsteVormerkung\":1,"
                + "\"mitglieder\":[{\"kontoId\":" + id + ",\"rollen\":[],\"lieblingsrollen\":[],\"ersteWahl\":null,\"zeichenZeigen\":true,\"karteFreigeben\":true,"
                + "\"notiz\":\"alt\",\"raenge\":[\"MARINE_04\"],\"taetigkeiten\":[],\"abzeichenVorn\":null,\"abzeichenAus\":[]}],"
                + "\"untergilden\":[],\"version\":5}";
            File.WriteAllText(Path.Combine(verzeichnis, Zustandsablage.MitgliederDatei), alt, new UTF8Encoding(false));
            var geladen = new Zustandsablage(verzeichnis).MitgliederLaden();
            Pruefe.Wahr(geladen.DatenVon(otto.KontoId) is { MeldungenSenden: false, Notiz: "alt", Raenge: ["MARINE_04"] }, "alte Datei lädt, ohne Haken");
            var neu = new Mitgliederbuch(TimeProvider.System, geladen);
            Pruefe.Wahr(neu.KontoAendern(buch.Stand, Akteur.AdminWerkzeug, otto.KontoId, null, null, true).Ok, "Haken über die Konsole");
            new Zustandsablage(verzeichnis).MitgliederSpeichern(neu.Stand);
            Pruefe.Wahr(new Zustandsablage(verzeichnis).MitgliederLaden().DatenVon(otto.KontoId) is { MeldungenSenden: true, Notiz: "alt", Raenge: ["MARINE_04"] },
                "übersteht den Neustart");
        }
        finally
        {
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, true);
        }
    }

    // ---------------------------------------------------------------- Uhrabgleich (Client-Kern)

    [Test]
    public static void Uhr_PingMitLaufzeitausgleich()
    {
        long mono = 1000;
        long wand = T0;
        var u = new Uhrabgleich(() => mono, () => wand);
        Pruefe.Gleich(Abgleichsstufe.Keine, u.Stufe, "ohne Angabe");
        Pruefe.Gleich(T0, u.ServerJetztMs(), "ohne Angabe die eigene Uhr");
        Pruefe.Gleich((long?)null, u.GenauigkeitMs, "ohne Probe keine Genauigkeit");

        const long Versatz = 1_799_000_000_000; // Serverzeit = monotone Zeit + Versatz
        // Grob aus Welcome (die Antwort war 40 ms unterwegs): 40 ms daneben.
        u.Grob(5000 - 40 + Versatz, 5000);
        Pruefe.Gleich(Abgleichsstufe.Welcome, u.Stufe, "grob aus Welcome");
        Pruefe.Gleich(5000 + Versatz - 40, u.ServerZuMono(5000), "grob ohne Laufzeitausgleich");

        // Gleich lange Wege: genau.
        u.Probe(6000, 6100, 6050 + Versatz);
        Pruefe.Gleich(Abgleichsstufe.Ping, u.Stufe, "nach der ersten Ping-Antwort");
        Pruefe.Gleich(7000 + Versatz, u.ServerZuMono(7000), "genau bei gleichen Wegen");
        Pruefe.Wahr(u.GenauigkeitMs == 50 && u.LaufzeitMs == 100, "Genauigkeit halbe Laufzeit");
        u.Grob(1, 7000);
        Pruefe.Gleich(7000 + Versatz, u.ServerZuMono(7000), "Welcome zählt nach einem Ping nicht mehr");

        // Ein Stau hin (150 ms hin, 10 zurück) verschiebt die Schätzung, aber die Probe mit der kürzeren Laufzeit gilt.
        u.Probe(8000, 8160, 8150 + Versatz);
        Pruefe.Gleich(9000 + Versatz, u.ServerZuMono(9000), "der Stau zählt nicht");

        // Die Serveruhr springt um 300 ms: nach einem Fenster (8 Proben) ist es übernommen.
        for (var i = 0; i < Uhrabgleich.Fenster; i++)
        {
            var t = 10_000 + i * 2000L;
            u.Probe(t, t + 200, t + 100 + Versatz + 300);
        }
        Pruefe.Gleich(30_000 + Versatz + 300, u.ServerZuMono(30_000), "nach 8 Proben gilt der neue Stand");
        Pruefe.Gleich((long?)100, u.GenauigkeitMs, "Genauigkeit der besten Probe im Fenster");

        // Negative Laufzeit zählt nicht; eine umgestellte PC-Uhr stört nicht (gerechnet wird auf der monotonen Uhr).
        u.Probe(40_000, 39_000, 1);
        wand += 3_600_000;
        mono = 30_000;
        Pruefe.Gleich(30_000 + Versatz + 300, u.ServerJetztMs(), "PC-Uhr umgestellt: die Serverzeit bleibt");
        u.Zuruecksetzen();
        Pruefe.Wahr(u.Stufe == Abgleichsstufe.Keine && u.ServerJetztMs() == wand, "nach dem Zurücksetzen die eigene Uhr");
    }

    /// <summary>Ziel etwa 0,1 s: Bei Wegen zwischen 5 und 180 ms je Richtung, zufällig und ungleich, liegt der Fehler nie darüber.</summary>
    [Test]
    public static void Uhr_ZielEinZehntelSekunde()
    {
        var zufall = new Random(16_10_2026);
        long mono = 0;
        var u = new Uhrabgleich(() => mono, () => 0);
        const long Versatz = 1_800_000_000_000;
        long groessterFehler = 0;
        for (var i = 0; i < 500; i++)
        {
            var gesendet = i * 2000L;
            var hin = zufall.Next(5, 181);
            var zurueck = zufall.Next(5, 181);
            u.Probe(gesendet, gesendet + hin + zurueck, gesendet + hin + Versatz);
            mono = gesendet + hin + zurueck;
            groessterFehler = Math.Max(groessterFehler, Math.Abs(u.ServerJetztMs() - (mono + Versatz)));
            Pruefe.Wahr(Math.Abs(u.ServerJetztMs() - (mono + Versatz)) <= u.GenauigkeitMs, "Fehler innerhalb der genannten Genauigkeit");
        }
        Console.WriteLine("Uhrabgleich: größter Fehler " + groessterFehler + " ms bei Wegen 5-180 ms");
        Pruefe.Wahr(groessterFehler <= 100, "höchstens 0,1 s daneben, war " + groessterFehler);
    }

    // ---------------------------------------------------------------- Client: Lage, Sätze, Attrappe

    [Test]
    public static void Client_LageRechtUndSaetze()
    {
        S.Mitgliedschaften Mit(bool? haken, long? alsFunkerBis = null, S.BeauftragungAngabe? auftrag = null) => new()
        {
            ZustandsVersion = 1, KontoVersion = 1, Felder = [], Global = new S.GlobalAngabe { Senden = false }, Sendeauswahl = new S.Sendeauswahl { Felder = [], Global = false },
            Stumm = [], Quelle = S.MitgliedschaftsQuelle.Rolle,
            Konto = new S.KontoStand
            {
                Rechte = [], ScNamen = ["Max"], Rollen = [], Lieblingsrollen = [], Zeichen = [], ZeichenZeigen = true, MeldungenSenden = haken,
                AlsFunkerBis = alsFunkerBis, Beauftragung = auftrag,
            },
        };
        S.Welcome Welcome(S.Stufe stufe, string fassung) => new()
        {
            Minor = 0, Status = S.WelcomeStatus.Freigegeben, Server = new S.ServerAngabe { Version = fassung },
            Konto = new S.KontoAngabe { Id = 7, Name = "Max", Benutzername = "max", Stufe = stufe, Rechte = [] },
        };
        FunkLage Lage(S.Welcome w, S.Mitgliedschaften m, S.Meldestand? stand = null) =>
            LageAbbildung.Abbilden(new LageEingabe(S.ClientZustand.AktivUdp, w, null, m, new Dictionary<uint, S.PraesenzEintrag>(), [], false, []) { Meldestand = stand });

        var stand = new S.Meldestand { Anlass = S.Meldungsvorgang.Stand, Zustand = new S.Meldungseintrag { Motiv = "pause", Groesse = 1, Von = 3, Seit = T0 } };
        var l = Lage(Welcome(S.Stufe.Raidleiter, "0.12.0"), Mit(true), stand);
        Pruefe.Wahr(l.ServerMeldungen && l.Konto is { MeldungenSenden: true, DarfMelden: true } && l.Meldestand?.Zustand?.Motiv == "pause", "Führung mit Haken an Server 0.12.0");
        Pruefe.Falsch(Lage(Welcome(S.Stufe.Raidleiter, "0.11.9"), Mit(true)).ServerMeldungen, "Server 0.11.9 kennt das Meldesystem nicht");
        Pruefe.Wahr(Lage(Welcome(S.Stufe.Raidleiter, "0.12.0"), Mit(null)).Konto is { MeldungenSenden: false, DarfMelden: false }, "ohne Haken (oder älterer Server)");
        Pruefe.Wahr(Lage(Welcome(S.Stufe.Funker, "0.12.0"), Mit(true)).Konto is { MeldungenSenden: true, DarfMelden: false }, "Funker mit Haken: darf nicht");
        var auftrag = new S.BeauftragungAngabe { Von = T0, Bis = T0 + 3_600_000, Rechte = [S.Recht.Zuweisen], Durch = 1, Aktiv = true };
        Pruefe.Wahr(Lage(Welcome(S.Stufe.Funker, "0.12.0"), Mit(true, auftrag: auftrag)).Konto!.DarfMelden, "Einsatzleitung mit laufendem Auftrag");
        Pruefe.Falsch(Lage(Welcome(S.Stufe.Funker, "0.12.0"), Mit(true, auftrag: auftrag with { Aktiv = false })).Konto!.DarfMelden, "Auftrag geplant, noch nicht laufend");
        Pruefe.Falsch(Lage(Welcome(S.Stufe.Admin, "0.12.0"), Mit(true, alsFunkerBis: T0)).Konto!.DarfMelden, "Admin heute als Funker");

        // Die Verwaltung setzt den Haken nur an einem Server, der ihn kennt (ein älterer übergänge das Feld still).
        Pruefe.Falsch(new Funk.Verwaltung.Verwaltungslage { ServerFassung = "0.11.9" }.MeldungenBekannt, "Verwaltung: Server 0.11.9");
        Pruefe.Wahr(new Funk.Verwaltung.Verwaltungslage { ServerFassung = "0.12.0" }.MeldungenBekannt && new Funk.Verwaltung.Verwaltungslage().MeldungenBekannt,
            "Verwaltung: ab 0.12.0 und ohne bekannte Fassung");

        Pruefe.Gleich("Höchstens eine Meldung in 2 Sekunden, kurz warten.", FunkClientDienst.Meldefehler(S.Fehlercode.Grenze, null), "GRENZE");
        Pruefe.Gleich("Die Zielzeit ist schon vorbei.", FunkClientDienst.Meldefehler(S.Fehlercode.Ungueltig, "Die Zielzeit ist schon vorbei"), "Satz des Servers");
        Pruefe.Gleich("Dafür fehlt das Recht.", FunkClientDienst.Meldefehler(S.Fehlercode.KeinRecht, null), "ohne Text der Satz des Clients");
    }

    [Test]
    public static async Task Client_AttrappeWieDerServer()
    {
        var d = new FunkDienstAttrappe(FunkDienstAttrappe.Beispiel());
        var jetzt = new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
        d.Uhr = () => jetzt;
        var ereignisse = new List<MeldungEingegangen>();
        d.Ereignis += e => { if (e is MeldungEingegangen m) ereignisse.Add(m); };

        Pruefe.Wahr(await d.MeldungSendenAsync(A(S.Meldungsvorgang.Befehl, "sammeln")) is { } ohne && ohne.Contains("Ernennung", StringComparison.Ordinal), "ohne Haken kein Recht");
        d.LageSetzen(l => l with { Konto = l.Konto! with { MeldungenSenden = true } });
        Pruefe.Gleich(null, await d.MeldungSendenAsync(A(S.Meldungsvorgang.Befehl, "sammeln", 3)), "Befehl");
        Pruefe.Wahr(ereignisse.Single().Stand is { Anlass: "Befehl", Befehl: { Motiv: "sammeln", Groesse: 3 } } && d.Lage.Meldestand is { Befehl: null }, "Ereignis mit Befehl, Lage ohne");
        Pruefe.Gleich("Höchstens eine Meldung in 2 Sekunden, kurz warten.", await d.MeldungSendenAsync(A(S.Meldungsvorgang.Zustand, "pause")), "2 s");
        jetzt = jetzt.AddSeconds(2);
        Pruefe.Gleich(null, await d.MeldungSendenAsync(A(S.Meldungsvorgang.BereitAbfrage)), "Bereit-Abfrage");
        var nummer = d.Lage.Meldestand!.Abfrage!.Nummer!.Value;
        Pruefe.Wahr(d.Lage.BereitStand is { Offen: true, Bereit: 0, Online: 5 }, "0 von 5 (die Funker online im Beispiel, ohne Max, der fragt)");
        Pruefe.Gleich(null, await d.BereitMeldenAsync(nummer), "selbst bereit: geht, zählt aber nicht");
        Pruefe.Gleich(null, d.BereitVon(2, nummer), "Falke bereit");
        Pruefe.Wahr(d.Lage.BereitStand is { Bereit: 1, Online: 5, Fehlen: [3, 4, 5, 7] }, "1 von 5, es fehlen 3, 4, 5, 7");
        Pruefe.Wahr(d.BereitVon(2, nummer + 1)?.Contains("Keine offene", StringComparison.Ordinal) == true, "falsche Nummer");
        jetzt = jetzt.AddSeconds(2);
        Pruefe.Wahr((await d.MeldungSendenAsync(A(S.Meldungsvorgang.Countdown, form: "Startzeit", ziel: jetzt.ToUnixTimeMilliseconds() - 1)))?.Contains("vorbei", StringComparison.Ordinal) == true,
            "Zielzeit vorbei");
        Pruefe.Gleich(jetzt.ToUnixTimeMilliseconds(), d.ServerJetztMs(), "Serverzeit der Attrappe");
    }
}
