// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Raidleitung über den Draht (Fassung 0.3.0, 6.12, N1, N4, N5, F2-F10): Auftrag erteilen nur in einer Admin-Sitzung mit
/// frischer Bestätigung, Regeln vor dem Code, Meldung, Mitgliedschaften und Brett ohne Neustart, Ablauf und Auftrag im Voraus
/// über den Takt, Journal; die Angriffe eines Beauftragten und eines Funkers; Ratengrenze; Paket mit der Testflotte,
/// Belegung als Push, Kontenverzeichnis, Wechselsperre mit Nachzüglern, letzter Platz in den Details; Konsole, Neustart und
/// Sicherung ohne Aufträge. Ohne TLS, prozessintern.
/// </summary>
internal static class RaidleitungServerTests
{
    private const string DiscordTest = "930000000000000001";
    private const string DiscordOtto = "930000000000000002";
    private const string DiscordFritz = "930000000000000003";
    private const string DiscordGina = "930000000000000004";

    internal static IPAddress Lokal(int n) => IPAddress.Parse("127.0.1." + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Wiederholt eine Anfrage, solange die Ratengrenze greift (die Grenzen selbst prüft ein eigener Test).</summary>
    internal static async Task<Antwort> Gedrosselt(Func<Task<Antwort>> anfrage)
    {
        for (var i = 0; ; i++)
        {
            var a = await anfrage();
            if (a.Code != Fehlercode.Grenze || i == 12) return a;
            await Task.Delay(3000);
        }
    }

    internal static async Task<bool> Warten(Func<bool> bedingung, int ms = 10_000)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(20);
        }
        return true;
    }

    private static string? Op(Meldung m) =>
        m.Details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("op", out var op) ? op.GetString() : null;

    private static Task<Meldung?> AuftragsmeldungAsync(KernFunker f, string op, int ms = 10_000) =>
        f.PushAsync<Meldung>(m => m.Art == MeldungsArten.Beauftragung && Op(m) == op, TimeSpan.FromMilliseconds(ms));

    internal sealed record Testadmin(ECDsa Schluessel, byte[] Geheimnis, Testuhr Uhr) : IDisposable
    {
        public string Code() => I.Totp.CodeText(Geheimnis, I.Totp.Zeitschritt(Uhr.GetUtcNow()));

        public void Dispose() => Schluessel.Dispose();
    }

    /// <summary>Einrichtung und Anmeldung wie in 3.9 (vorsehen, Antrag, ernennen, Geheimnis, erster Code, Anmeldung).</summary>
    internal static async Task<Testadmin> AdminSitzungAsync(Testserver t, Testuhr uhr, KernFunker f)
    {
        var c = f.Client;
        var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await t.AdminOk("admin-vorsehen", new JsonObject { ["konto"] = f.KontoId });
        var antrag = await Gedrosselt(() => c.AdminAntragAsync(schluessel));
        Pruefe.Wahr(antrag.Ok, "Antrag: " + antrag.Fehler);
        await t.AdminOk("admin-ernennen", new JsonObject { ["konto"] = f.KontoId, ["kennung"] = antrag.Als<AdminEinrichtungAntwort>().Kennung });
        var abholen = await Gedrosselt(() => c.GeheimnisAbholenAsync(schluessel));
        Pruefe.Wahr(abholen.Ok && I.Base32.TryDekodieren(abholen.Als<AdminEinrichtungAntwort>().Geheimnis!, out _), "Geheimnis: " + abholen.Fehler);
        I.Base32.TryDekodieren(abholen.Als<AdminEinrichtungAntwort>().Geheimnis!, out var g);
        var z = new Testadmin(schluessel, g, uhr);
        Pruefe.Wahr((await Gedrosselt(() => c.EinrichtungBestaetigenAsync(schluessel, z.Code()))).Ok, "erster Code");
        uhr.Vor(TimeSpan.FromSeconds(30));
        var anmeldung = await Gedrosselt(() => c.AdminAnmeldenAsync(schluessel, z.Code()));
        Pruefe.Wahr(anmeldung.Ok, "Admin-Sitzung: " + anmeldung.Fehler);
        return z;
    }

    private static Task<Antwort> Beauftragung(KernFunker f, BeauftragungAnfrage a) => Gedrosselt(() => f.Client.AnfrageAsync(Nachrichtentyp.Beauftragung, a));

    internal static Task<Antwort> Umsetzen(KernFunker wer, uint wen) => Gedrosselt(() => wer.Client.AnfrageAsync(Nachrichtentyp.Verschieben,
        new VerschiebenAnfrage { Aenderungen = [new Verschiebung { KontoId = wen, FelderPatch = [new FeldPatch { Feld = 9, NetzId = 2000 }] }] }));

    internal static async Task NetzAnlegenAsync(Testserver t, ushort id, string name) =>
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse($$"""{"id":{{id}},"name":"{{name}}","typ":"NORMAL","reihenfolge":1,"nurTaste":false,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false}""") });

    // ---------------------------------------------------------------- Auftrag über den Draht

    [Test]
    public static async Task Draht_Auftrag_AdminSitzungBestaetigung_BrettErscheintUndVerschwindet_AblaufUndVoraus()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await NetzAnlegenAsync(t, 2000, "Orga");
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(11));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(12));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(13));
        var jetzt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var erteilen = new BeauftragungAnfrage { Op = BeauftragungOperation.Erteilen, KontoId = otto.KontoId, Grund = "Op Nachtfalke", Bis = jetzt + 3 * 3600_000 };

        Pruefe.Gleich(Fehlercode.KeinRecht, (await Beauftragung(fritz, erteilen)).Code, "FUNKER erteilt nichts");
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Beauftragung(test, erteilen)).Code, "ADMIN ohne Admin-Sitzung");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Umsetzen(otto, fritz.KontoId)).Code, "ohne Auftrag teilt Otto nicht ein");

        using var admin = await AdminSitzungAsync(t, uhr, test);
        uhr.Vor(TimeSpan.FromMinutes(6));
        // Regeln vor dem Code: ein ohnehin verbotener Auftrag fragt nicht nach einer Bestätigung.
        var verboten = await Beauftragung(test, erteilen with { Rechte = [Recht.KontenVerwalten, Recht.Zuweisen, Recht.SprechanzeigeAlle] });
        Pruefe.Gleich(Fehlercode.Ungueltig, verboten.Code, "Verwaltungsrecht im Auftrag (Rechteausweitung)");
        Pruefe.Gleich(Fehlercode.SchrittBestaetigen, (await Beauftragung(test, erteilen)).Code, "ohne frischen Code: SCHRITT_BESTAETIGEN");
        Pruefe.Wahr((await Gedrosselt(() => test.Client.AdminBestaetigenAsync(admin.Code()))).Ok, "Code bestätigt");
        var ok = await Beauftragung(test, erteilen);
        Pruefe.Wahr(ok.Ok && ok.Body is BeauftragungAntwort { Beauftragungen: [{ Aktiv: true, Grund: "Op Nachtfalke" } b] } && b.KontoId == otto.KontoId, "erteilt: " + ok.Fehler);

        // Der Beauftragte erfährt es ohne Neustart: Meldung, eigener Stand, Brett (Praesenz mit Details), Zeichen.
        Pruefe.Wahr(await AuftragsmeldungAsync(otto, "ERTEILT") is not null, "Meldung BEAUFTRAGUNG{ERTEILT} an Otto");
        Pruefe.Wahr(await AuftragsmeldungAsync(test, "ERTEILT") is not null, "und an die Admin-Sitzung");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto?.Beauftragung is { Aktiv: true, Durch: var d } && d == test.KontoId), "Mitgliedschaften.konto.beauftragung");
        Pruefe.Wahr(otto.Client.Lage.HatRecht(Recht.Zuweisen) && !otto.Client.Lage.Konto!.Rechte.Contains(Recht.Zuweisen), "wirksam ZUWEISEN, dauerhaft nicht");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Praesenz.TryGetValue(fritz.KontoId, out var e) && e.Felder is not null), "Praesenz mit Details: das Brett erscheint");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Praesenz.TryGetValue(otto.KontoId, out var e) && e.Zeichen is ["EINSATZLEITUNG"]), "Zeichen EINSATZLEITUNG für alle");
        Pruefe.Wahr((await Umsetzen(otto, fritz.KontoId)).Ok, "Otto teilt ein");
        var liste = await Beauftragung(test, new BeauftragungAnfrage { Op = BeauftragungOperation.Liste });
        Pruefe.Wahr(liste.Ok && liste.Body is BeauftragungAntwort { Beauftragungen.Count: 1 }, "LISTE in der Admin-Sitzung");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Beauftragung(otto, new BeauftragungAnfrage { Op = BeauftragungOperation.Liste })).Code, "LISTE nicht für den Beauftragten");

        var entzug = await Beauftragung(test, new BeauftragungAnfrage { Op = BeauftragungOperation.Entziehen, KontoId = otto.KontoId });
        Pruefe.Wahr(entzug.Ok, "entzogen: " + entzug.Fehler);
        Pruefe.Wahr(await AuftragsmeldungAsync(otto, "ENTZOGEN") is not null, "Meldung ENTZOGEN");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto is { Beauftragung: null } && !otto.Client.Lage.HatRecht(Recht.Zuweisen)), "Auftrag fort");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Praesenz.TryGetValue(fritz.KontoId, out var e) && e.Felder is null), "Praesenz ohne Details: das Brett verschwindet");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Umsetzen(otto, fritz.KontoId)).Code, "sofort ohne ZUWEISEN");

        // Ablauf über den Takt und ein Auftrag im Voraus (von in 3 s, bis in 6 s); die Zeiten je Versuch neu, falls die
        // Ratengrenze einen Versuch aufhält.
        otto.PushesLeeren();
        var voraus = await Gedrosselt(() =>
        {
            var j = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return test.Client.AnfrageAsync(Nachrichtentyp.Beauftragung, erteilen with { Von = j + 3000, Bis = j + 6000 });
        });
        Pruefe.Wahr(voraus.Ok && voraus.Body is BeauftragungAntwort { Beauftragungen: [{ Aktiv: false }] }, "geplant: " + voraus.Fehler);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto?.Beauftragung is { Aktiv: false }), "geplant, noch nicht wirksam");
        Pruefe.Wahr(await AuftragsmeldungAsync(otto, "BEGONNEN", 15_000) is not null, "Takt: BEGONNEN");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.HatRecht(Recht.Zuweisen)), "ab von wirksam");
        Pruefe.Wahr(await AuftragsmeldungAsync(otto, "ABGELAUFEN", 15_000) is not null, "Takt: ABGELAUFEN");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Konto is { Beauftragung: null }), "abgeräumt");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Umsetzen(otto, fritz.KontoId)).Code, "nach dem Ablauf KEIN_RECHT");

        var journal = await t.AdminOk("journal", new JsonObject { ["anzahl"] = 100 });
        foreach (var art in new[] { "AUFTRAG_ERTEILT", "AUFTRAG_ENTZOGEN", "AUFTRAG_BEGONNEN", "AUFTRAG_ABGELAUFEN" })
            Pruefe.Wahr(journal.Text.Contains(art, StringComparison.Ordinal), "Journal: " + art);
        Pruefe.Wahr(journal.Text.Contains("Op Nachtfalke", StringComparison.Ordinal), "Journal nennt den Anlass");
        Pruefe.Wahr(t.Logbuch.Any(z => z.Contains("Bot-Meldung AUFTRAG_ERTEILT an Admin-Kanal", StringComparison.Ordinal)), "Zeile für den Admin-Kanal");
    }

    // ---------------------------------------------------------------- Angriffe

    [Test]
    public static async Task Draht_Angriffe_BeauftragterOhneVerwaltung_FunkerOhneAuftrag_Ratengrenze()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await NetzAnlegenAsync(t, 2000, "Orga");
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(21));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(22));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(23));
        await t.AdminOk("auftrag", new JsonObject { ["konto"] = otto.KontoId, ["grund"] = "Probe" });
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.HatRecht(Recht.Zuweisen)), "Otto im Auftrag");
        var v = otto.Client.Lage.NetzListe!.Version;

        (string Was, Nachrichtentyp Typ, object Body, Fehlercode Erwartet)[] faelle =
        [
            ("Auftrag weitergeben", Nachrichtentyp.Beauftragung, new BeauftragungAnfrage { Op = BeauftragungOperation.Erteilen, KontoId = fritz.KontoId }, Fehlercode.KeinRecht),
            ("eigenen Auftrag entziehen", Nachrichtentyp.Beauftragung, new BeauftragungAnfrage { Op = BeauftragungOperation.Entziehen, KontoId = otto.KontoId }, Fehlercode.KeinRecht),
            ("Konto sperren", Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = KontoOperation.Sperren, KontoId = fritz.KontoId }, Fehlercode.KeinRecht),
            ("Stufe setzen", Nachrichtentyp.KontoVerwaltung, new KontoVerwaltungAnfrage { Op = KontoOperation.Stufe, KontoId = fritz.KontoId, Wert = JsonSerializer.SerializeToElement("RAIDLEITER") }, Fehlercode.KeinRecht),
            ("Netz anlegen", Nachrichtentyp.Verwaltung, new VerwaltungAnfrage { Op = VerwaltungsOperation.NetzSetzen, Version = v, Objekt = JsonSerializer.SerializeToElement(new { id = 2001, name = "X", typ = "NORMAL", reihenfolge = 0, nurTaste = false, ausloeserStandard = "TASTE", offen = "NEIN", standard = false }) }, Fehlercode.KeinRecht),
            ("Paket", Nachrichtentyp.Verwaltung, new VerwaltungAnfrage { Op = VerwaltungsOperation.Paket, Version = v, Objekt = JsonSerializer.SerializeToElement(new { ops = new[] { new { op = "EINSTELLUNG", objekt = new { durchsageNurTaste = false } } } }) }, Fehlercode.KeinRecht),
            ("Journal", Nachrichtentyp.Journal, new JournalAnfrage(), Fehlercode.KeinRecht),
            ("Mitglied vormerken", Nachrichtentyp.Mitglied, new MitgliedAnfrage { Op = MitgliedOperation.Vormerken, ScName = "Neu" }, Fehlercode.KeinRecht),
        ];
        foreach (var (was, typ, body, erwartet) in faelle)
        {
            var a = await Gedrosselt(() => otto.Client.AnfrageAsync(typ, body));
            Pruefe.Gleich(erwartet, a.Code, "Beauftragter: " + was);
        }
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Umsetzen(otto, test.KontoId)).Code, "Beauftragter verschiebt keinen ADMIN");
        Pruefe.Wahr((await Umsetzen(otto, fritz.KontoId)).Ok, "aber einen Funker");
        Pruefe.Gleich(Fehlercode.Unbekannt, (await Beauftragung(fritz, new BeauftragungAnfrage { Op = BeauftragungOperation.Abgeben })).Code, "wer keinen Auftrag hat, gibt nichts ab");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.KontenVerzeichnis, new LeererBody()))).Code, "Kontenverzeichnis nur mit ZUWEISEN");
        Pruefe.Wahr(t.Dienst.Zentrale.Konten.Stand.Konten[fritz.KontoId] is { Status: I.KontoStatus.Aktiv, Stufe: I.Stufe.Funker }, "nichts geändert");

        // Ratengrenze: Beauftragung zählt zur Verwaltung (1/s, Stoß 5); die sechste Anfrage im Stoß ist GRENZE.
        await Task.Delay(6000);
        var codes = new List<Fehlercode?>();
        for (var i = 0; i < 7; i++) codes.Add((await fritz.Client.AnfrageAsync(Nachrichtentyp.Beauftragung, new BeauftragungAnfrage { Op = BeauftragungOperation.Abgeben })).Code);
        Pruefe.Wahr(codes.Any(c => c == Fehlercode.Grenze) && codes.Take(5).All(c => c == Fehlercode.Unbekannt), "Stoß 5, dann GRENZE: " + string.Join(",", codes));
        Pruefe.Falsch(fritz.Client.Zustand == ClientZustand.Getrennt, "GRENZE trennt nicht");

        // Abgeben: Otto beendet seinen Abend selbst.
        var ab = await Beauftragung(otto, new BeauftragungAnfrage { Op = BeauftragungOperation.Abgeben });
        Pruefe.Wahr(ab.Ok && await AuftragsmeldungAsync(otto, "ABGEGEBEN") is not null, "abgegeben: " + ab.Fehler);
        Pruefe.Wahr(await Warten(() => !otto.Client.Lage.HatRecht(Recht.Zuweisen)), "danach ohne ZUWEISEN");
    }

    // ---------------------------------------------------------------- Flotte, Belegung, Verzeichnis, Wechselsperre

    [Test]
    public static async Task Draht_FlottePaket_Belegung_Verzeichnis_Wechselsperre_LetzterPlatz()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(31));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(32));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(33));
        await using var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(34));
        using var admin = await AdminSitzungAsync(t, uhr, test);

        // Die Testflotte als ein Paket: ohne frischen Code SCHRITT_BESTAETIGEN, dann eine NetzListe.
        var vorlage = Testflotte.Schiffsflotte(test.Client.Lage.NetzListe);
        var paket = new VerwaltungAnfrage { Op = VerwaltungsOperation.Paket, Version = test.Client.Lage.NetzListe!.Version, Objekt = SteuerJson.Element(vorlage.Paket) };
        uhr.Vor(TimeSpan.FromMinutes(6));
        Pruefe.Gleich(Fehlercode.SchrittBestaetigen, (await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Verwaltung, paket))).Code, "Paket nur mit frischem Code");
        Pruefe.Wahr((await Gedrosselt(() => test.Client.AdminBestaetigenAsync(admin.Code()))).Ok, "Code bestätigt");
        var listen = test.Pushes.Count(p => p.Body is NetzListe);
        var uebernommen = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Verwaltung, paket));
        Pruefe.Wahr(uebernommen.Ok, "Paket übernommen: " + uebernommen.Fehler);
        Pruefe.Wahr(await Warten(() => test.Client.Lage.NetzListe is { Schiffe.Count: 15 } n && n.Netze.Count == 52), "15 Schiffe, 52 Netze mit Global");
        await Task.Delay(500);
        Pruefe.Gleich(listen + 1, test.Pushes.Count(p => p.Body is NetzListe), "genau eine NetzListe für das ganze Paket");
        var nl = test.Client.Lage.NetzListe!;
        Pruefe.Wahr(nl.Schiffe.All(s => s.Art == Einheitsart.Schiff), "art SCHIFF");
        var co = nl.Rollen.Single(r => r.Name == "Kapitän");
        var schiff1 = nl.Schiffe.Single(s => s.Name == "Schiff 1");
        var schiff2 = nl.Schiffe.Single(s => s.Name == "Schiff 2");

        // Belegung als Push: Fritz steigt auf Schiff 1 ein.
        Pruefe.Wahr((await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = co.Id, SchiffId = schiff1.Id }))).Ok, "Fritz CO auf Schiff 1");
        Pruefe.Wahr(await gina.PushAsync<Belegung>(b => b.Plaetze.Any(p => p.SchiffId == schiff1.Id && p.RolleId == co.Id && p.Belegt == 1 && p.Kapazitaet == 1) && b.MitPlatz == 1, TimeSpan.FromSeconds(10)) is { Online: 4 },
            "Belegung an alle: Schiff 1 CO 1/1, 4 online, 1 mit Platz");

        // Einsatzleitung im Auftrag: Kontenverzeichnis, Wechsel sperren, Nachzügler.
        await t.AdminOk("auftrag", new JsonObject { ["konto"] = otto.KontoId });
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.HatRecht(Recht.Zuweisen)), "Otto im Auftrag");
        var verzeichnis = await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.KontenVerzeichnis, new LeererBody()));
        Pruefe.Wahr(verzeichnis.Ok && verzeichnis.Body is KontenVerzeichnisAntwort kv && kv.Konten.Count == 4
            && kv.Konten.Single(k => k.KontoId == fritz.KontoId) is { Online: true, Platz: PlatzStatus.Belegt } f1 && f1.SchiffId == schiff1.Id, "Kontenverzeichnis: " + verzeichnis.Fehler);
        var sperren = await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.Verwaltung,
            new VerwaltungAnfrage { Op = VerwaltungsOperation.Selbstwahl, Version = otto.Client.Lage.NetzListe!.Version, Objekt = SteuerJson.Element(new SelbstwahlObjekt { An = false }) }));
        Pruefe.Wahr(sperren.Ok, "Wechsel gesperrt: " + sperren.Fehler);
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.NetzListe?.Einstellungen is { Selbstwahl: false, Nachzuegler: true, SelbstwahlBis: not null } e && e.SelbstwahlDurch == otto.KontoId),
            "NetzListe nennt Urheber, Frist und Nachzügler");
        var fritzWechsel = await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = co.Id, SchiffId = schiff2.Id }));
        Pruefe.Gleich(Fehlercode.SelbstwahlGesperrt, fritzWechsel.Code, "mit Platz: Wechsel gesperrt");
        Pruefe.Wahr((await Gedrosselt(() => gina.Client.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = co.Id, SchiffId = schiff2.Id }))).Ok,
            "Nachzüglerin ohne Platz steigt ein");

        // Zurück auf Standard: der letzte Platz steht für die Einsatzleitung in den Details (wie zuletzt).
        var zurueck = await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.Verschieben,
            new VerschiebenAnfrage { Aenderungen = [new Verschiebung { KontoId = fritz.KontoId, RolleId = 0 }] }));
        Pruefe.Wahr(zurueck.Ok, "Fritz auf Standard: " + zurueck.Fehler);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Praesenz.TryGetValue(fritz.KontoId, out var e) && e.LetzterPlatz is { } lp && lp.SchiffId == schiff1.Id && lp.RolleId == co.Id),
            "Praesenz-Details: letzter Platz Schiff 1 CO");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften?.LetzterStand is { } ls && ls.SchiffId == schiff1.Id), "Fritz: wie letztes Mal");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Praesenz.TryGetValue(fritz.KontoId, out var e) && e.LetzterPlatz is null), "ohne ZUWEISEN keine Details");

        // Abgeben beendet Ottos Sperre (Regel 7).
        Pruefe.Wahr((await Beauftragung(otto, new BeauftragungAnfrage { Op = BeauftragungOperation.Abgeben })).Ok, "Otto gibt ab");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.NetzListe?.Einstellungen is { Selbstwahl: true }), "mit dem Auftrag endet die Sperre");
    }

    // ---------------------------------------------------------------- Konsole, Neustart, Sicherung

    [Test]
    public static async Task Konsole_AuftragUeberstehtNeustart_SicherungOhneAuftrag_Vorlage()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "raid-" + Guid.NewGuid().ToString("N")[..12]);
        var t1 = await Testserver.StartenAsync(verzeichnis: verzeichnis);
        Testserver? t2 = null;
        try
        {
            var konto = (await t1.AdminOk("konto-anlegen", new JsonObject { ["discord"] = DiscordOtto, ["benutzername"] = "otto", ["name"] = "Otto" })).Daten!["kontoId"]!.GetValue<uint>();
            var admin = (await t1.AdminOk("konto-anlegen", new JsonObject { ["discord"] = DiscordTest, ["benutzername"] = "test", ["name"] = "Test", ["stufe"] = "ADMIN" })).Daten!["kontoId"]!.GetValue<uint>();
            Pruefe.Falsch((await t1.Admin("auftrag", new JsonObject { ["konto"] = admin })).Ok, "kein Auftrag für einen ADMIN");
            Pruefe.Falsch((await t1.Admin("auftrag", new JsonObject { ["konto"] = konto, ["bis"] = "morgen" })).Ok, "--bis unlesbar");
            var r = await t1.Admin("auftrag", new JsonObject { ["konto"] = konto, ["rechte"] = new JsonArray("ZUWEISEN", "SPRECHANZEIGE_ALLE", "KONTEN_VERWALTEN") });
            Pruefe.Wahr(!r.Ok && r.Text.StartsWith("UNGUELTIG", StringComparison.Ordinal), "Verwaltungsrecht im Auftrag: " + r.Text);
            var erteilt = await t1.AdminOk("auftrag", new JsonObject { ["konto"] = konto, ["grund"] = "Op Nachtfalke" });
            Pruefe.Wahr(erteilt.Text.StartsWith("Einsatzleitung für Otto", StringComparison.Ordinal) && erteilt.Text.Contains(" bis ", StringComparison.Ordinal), erteilt.Text);
            var bisMs = t1.Dienst.Zentrale.Schreiber.Aktuell.Konten[konto].Beauftragung!.Bis;
            var lokal = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(bisMs), Auftragszeit.Zone);
            Pruefe.Wahr(lokal is { Hour: 4, Minute: 0 } && bisMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <= 24 * 3600_000, "ohne --bis bis 04:00 Ortszeit: " + lokal);
            var auftraege = await t1.AdminOk("auftraege");
            Pruefe.Wahr(auftraege.Text.Contains("Otto", StringComparison.Ordinal) && auftraege.Text.Contains("läuft", StringComparison.Ordinal) && auftraege.Text.Contains("Op Nachtfalke", StringComparison.Ordinal),
                "auftraege: " + auftraege.Text);

            // Die Vorlage Star Citizen: neben die Flotte, mit den Annahmen; ein zweites Mal nicht.
            var vorlage = await t1.AdminOk("flotte-vorlage", new JsonObject { ["name"] = "starcitizen" });
            Pruefe.Wahr(vorlage.Text.Contains("Annahmen", StringComparison.Ordinal) && t1.Dienst.Zentrale.Schreiber.Aktuell.Netze.Values.Count(n => !n.Kanaele.IsEmpty) == 10,
                "Vorlage: " + vorlage.Text);
            Pruefe.Falsch((await t1.Admin("flotte-vorlage")).Ok, "nicht ein zweites Mal neben dieselbe Flotte");

            var sicherung = await t1.AdminOk("sicherung");
            var ziel = sicherung.Daten!["ziel"]!.GetValue<string>();
            var gesichert = await File.ReadAllTextAsync(Path.Combine(ziel, Funk.Server.Kern.Zustandsablage.LageDatei));
            Pruefe.Falsch(gesichert.Contains("beauftragung", StringComparison.Ordinal), "die Sicherung trägt keinen Auftrag");
            Pruefe.Wahr(gesichert.Contains("Bergung und Bergbau", StringComparison.Ordinal), "aber die Flotte");

            // Neustart: der Auftrag bleibt (6.12), die Konsole entzieht ihn danach.
            await t1.DienstBeendenAsync();
            t2 = await Testserver.StartenAsync(verzeichnis: verzeichnis);
            Pruefe.Wahr(t2.Dienst.Zentrale.Schreiber.Aktuell.Konten[konto].Beauftragung is { Grund: "Op Nachtfalke" }, "Auftrag übersteht den Neustart");
            Pruefe.Wahr((await t2.AdminOk("auftrag-entziehen", new JsonObject { ["konto"] = konto })).Text.Contains("entzogen", StringComparison.Ordinal), "entzogen");
            Pruefe.Falsch((await t2.Admin("auftrag-entziehen", new JsonObject { ["konto"] = konto })).Ok, "nichts mehr zu entziehen");
            Pruefe.Gleich("Keine Aufträge", (await t2.AdminOk("auftraege")).Text, "leer");
            await t2.DienstBeendenAsync();

            // Zurückgespielte Sicherung: kein Auftrag kommt zurück.
            var zurueck = Path.Combine(Testserver.Wurzel(), "raid-zurueck-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(Path.Combine(zurueck, "zustand"));
            foreach (var datei in Directory.GetFiles(ziel)) File.Copy(datei, Path.Combine(zurueck, "zustand", Path.GetFileName(datei)));
            var (lage, _) = new Funk.Server.Kern.Zustandsablage(Path.Combine(zurueck, "zustand")).LageLaden();
            Pruefe.Wahr(lage.Konten[konto].Beauftragung is null && lage.Netze.Values.Count(n => !n.Kanaele.IsEmpty) == 10, "aus der Sicherung ohne Auftrag, mit Flotte");
            Directory.Delete(zurueck, recursive: true);
        }
        finally
        {
            if (t2 is not null) await t2.DisposeAsync();
            await t1.DisposeAsync();
        }
    }
}
