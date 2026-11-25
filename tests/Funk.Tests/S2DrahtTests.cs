// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Funker-Modus und Einsatzphasen über den Draht (Fassung 0.5.0, 0x003C, 0x003D, 0x4004, Verwaltung PHASE_SETZEN und
/// PHASE_LOESCHEN, NetzListe.phasen, die optionalen Felder): ohne TLS, prozessintern, mit Testuhr für TOTP. Dazu die Angriffe
/// (Funker, Führung, Beauftragter gegen Admin), die Flut von Phasenwechseln an der Ratengrenze und ein Client 0.5.0 am Server 0.4.0.
/// </summary>
internal static class S2DrahtTests
{
    private const string DiscordTest = "960000000000000001";
    private const string DiscordOtto = "960000000000000002";
    private const string DiscordFritz = "960000000000000003";
    private const string DiscordGina = "960000000000000004";
    private const string DiscordHans = "960000000000000005";

    private static JsonNode Objekt(string json) => JsonNode.Parse(json)!;

    /// <summary>Flotte an der Konsole: Kommando, Crew 3, Brücke 3, Trupp Alpha, Schiff 3, Rollen CO (10) und Marine (13).</summary>
    private static async Task FlotteAsync(Testserver t)
    {
        await t.AdminOk("schiff-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":3,"name":"Schiff 3","reihenfolge":3,"netze":{},"plaetze":[]}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":2,"name":"Kommando","typ":"NORMAL","reihenfolge":1,"nurTaste":false,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":7,"name":"Crew 3","typ":"NORMAL","reihenfolge":2,"nurTaste":false,"ausloeserStandard":"VA","offen":"NEIN","standard":false,"schiffId":3,"schiffsnetz":"CREW"}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":8,"name":"Brücke 3","typ":"NORMAL","reihenfolge":3,"nurTaste":true,"ausloeserStandard":"TASTE","offen":"NEIN","standard":false,"schiffId":3,"schiffsnetz":"BRUECKE"}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":50,"name":"Trupp Alpha","typ":"NORMAL","reihenfolge":5,"nurTaste":false,"ausloeserStandard":"TASTE","offen":"NEIN","standard":false}""") });
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":2000,"name":"Orga","typ":"NORMAL","reihenfolge":9,"nurTaste":false,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false}""") });
        await t.AdminOk("rolle-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":10,"name":"Kapitän","reihenfolge":12,"selbstWaehlbar":true,"brauchtSchiff":true,"felder":[{"feld":0,"netzId":2,"senden":true,"hoeren":true,"sendenStandard":true,"fest":false},{"feld":1,"schiffsnetz":"CREW","senden":true,"hoeren":true,"sendenStandard":false,"fest":false}]}""") });
        await t.AdminOk("rolle-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":13,"name":"Marine","reihenfolge":15,"selbstWaehlbar":true,"brauchtSchiff":true,"felder":[{"feld":0,"schiffsnetz":"CREW","senden":true,"hoeren":true,"sendenStandard":true,"fest":false},{"feld":1,"netzId":2,"senden":false,"hoeren":true,"sendenStandard":false,"fest":false}]}""") });
        await t.AdminOk("schiff-setzen", new JsonObject { ["objekt"] = Objekt("""{"id":3,"name":"Schiff 3","reihenfolge":3,"netze":{"CREW":7,"BRUECKE":8},"plaetze":[{"rolleId":10,"kapazitaet":1},{"rolleId":13,"kapazitaet":6}]}""") });
    }

    private static NetzPhase Anflug => new() { Nr = 1, Name = "Anflug", Kurz = "ANFLUG" };

    private static NetzPhase Entern => new()
    {
        Nr = 2,
        Name = "Entern",
        Kurz = "ENTERN",
        Felder =
        [
            new PhasenFeld { RolleId = 13, Feld = 0, Schiffsnetz = "CREW", Hoeren = true, Senden = false, SendenStandard = false, Fest = false },
            new PhasenFeld { RolleId = 13, Feld = 1, NetzId = 2, Hoeren = true, Senden = false, SendenStandard = false, Fest = false },
            new PhasenFeld { RolleId = 13, Feld = 2, NetzId = 50, Hoeren = true, Senden = true, SendenStandard = true, Fest = false },
        ],
    };

    private static JsonElement Paket(params (VerwaltungsOperation Op, object Objekt)[] ops) =>
        SteuerJson.Element(new VerwaltungsPaket { Ops = [.. ops.Select(o => new VerwaltungsSchritt { Op = o.Op, Objekt = o.Objekt is NetzPhase p ? SteuerJson.Element(p) : SteuerJson.Element((IdObjekt)o.Objekt) })] });

    private static string? DetailText(Antwort a, string name) =>
        a.Fehler?.Details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var w) ? w.ToString() : null;

    // ---------------------------------------------------------------- Funker-Modus

    [Test]
    public static async Task Draht_Funkermodus_EinUndAus_EinsatzleitungTeiltEin_Angriffe()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await NetzAnlegenAsync(t, 2000, "Orga");
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(141));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(142));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(143));
        await t.AdminOk("auftrag", new JsonObject { ["konto"] = otto.KontoId, ["grund"] = "Op Nachtfalke" });
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.HatRecht(Recht.Zuweisen)), "Otto im Auftrag");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Umsetzen(otto, test.KontoId)).Code, "vorher: den Admin setzt Otto nicht um");

        // Angriffe: Funker, Beauftragter, und ein Funker, der ein fremdes Konto in den Body schreibt.
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.Funkermodus, new FunkermodusAnfrage { An = true }))).Code, "FUNKER: KEIN_RECHT");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.Funkermodus, new FunkermodusAnfrage { An = true }))).Code, "Beauftragter: KEIN_RECHT");
        await using (var roh = await t.AktivAsync("960000000000000009", "roh", "FUNKER", Lokal(149)))
        {
            var fremd = Encoding.UTF8.GetBytes($$"""{"an":true,"kontoId":{{test.KontoId}}}""");
            var nr = roh.NaechsteNr();
            await roh.RohAsync((ushort)Nachrichtentyp.Funkermodus, 0, nr, fremd);
            var a = await roh.ErwarteAsync(r => r.Kopf.IstAntwort && r.Kopf.AnfrageNr == nr);
            Pruefe.Wahr(a.Typ == Nachrichtentyp.Fehler && a.Json<Fehler>().Code == Fehlercode.KeinRecht, "fremdes Konto im Body: KEIN_RECHT, das Feld zählt nicht");
            Pruefe.Falsch(roh.Getrennt, "ohne Trennung");
        }
        Pruefe.Wahr(test.Client.Lage.Konto?.AlsFunkerBis is null, "Test unberührt");

        // Sarevoc schaltet ein: bis 04:00 Ortszeit, ohne Admin-Sitzung.
        var ein = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Funkermodus, new FunkermodusAnfrage { An = true }));
        Pruefe.Wahr(ein.Ok && ein.Body is FunkermodusAntwort { An: true, Bis: { } bis }, "eingeschaltet: " + ein.Fehler);
        var bisMs = ein.Als<FunkermodusAntwort>().Bis!.Value;
        var lokal = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(bisMs), Auftragszeit.Zone);
        Pruefe.Wahr(lokal is { Hour: 4, Minute: 0 } && bisMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <= 24 * 3600_000, "bis 04:00 Ortszeit: " + lokal);
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Konto?.AlsFunkerBis == bisMs), "Mitgliedschaften.konto.alsFunkerBis");
        Pruefe.Wahr(await Warten(() => !test.Client.Lage.HatRecht(Recht.Zuweisen) && !test.Client.Lage.HatRecht(Recht.GlobalSenden)), "der Client sieht die Rechte ruhen");
        Pruefe.Wahr(test.Client.Lage.Mitgliedschaften!.Global.Senden is false, "Global senden aus");
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Praesenz.TryGetValue(fritz.KontoId, out var e) && e.Felder is null), "Praesenz ohne Details: das Brett verschwindet");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Praesenz.TryGetValue(test.KontoId, out var e) && e.AlsFunker == true), "die Einsatzleitung sieht: als Funker");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Praesenz.TryGetValue(test.KontoId, out var e) && e.AlsFunker is null), "ohne ZUWEISEN keine Details");
        var verzeichnis = await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.KontenVerzeichnis, new LeererBody()));
        Pruefe.Wahr(verzeichnis.Ok && verzeichnis.Als<KontenVerzeichnisAntwort>().Konten.Single(k => k.KontoId == test.KontoId).AlsFunker == true, "Kontenverzeichnis: als Funker");

        Pruefe.Wahr((await Umsetzen(otto, test.KontoId)).Ok, "jetzt setzt Otto den Admin um");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.Stummschalten,
            new StummschaltenAnfrage { KontoId = test.KontoId, NetzId = 0, Sekunden = 3600 }))).Code, "stummschalten nicht");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Umsetzen(test, fritz.KontoId)).Code, "Test teilt als Funker nicht ein");

        var aus = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Funkermodus, new FunkermodusAnfrage { An = false }));
        Pruefe.Wahr(aus.Ok && aus.Body is FunkermodusAntwort { An: false, Bis: null }, "wieder Admin: " + aus.Fehler);
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Konto is { AlsFunkerBis: null } && test.Client.Lage.HatRecht(Recht.Zuweisen)), "Rechte zurück");
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Praesenz.TryGetValue(fritz.KontoId, out var e) && e.Felder is not null), "das Brett erscheint wieder");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Umsetzen(otto, test.KontoId)).Code, "Otto setzt den Admin wieder nicht um");

        var journal = await t.AdminOk("journal", new JsonObject { ["anzahl"] = 100 });
        Pruefe.Wahr(journal.Text.Contains("spielt als Funker bis", StringComparison.Ordinal) && journal.Text.Contains("wieder als Admin", StringComparison.Ordinal),
            "Journal: " + journal.Text);
    }

    // ---------------------------------------------------------------- Phasen

    [Test]
    public static async Task Draht_Phasen_AnlegenSchaltenVeraltetFlutNachWelcomeLoeschen()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        try
        {
            await PhasenAblaufAsync(t, uhr);
        }
        catch (VerbindungGetrenntException ex)
        {
            // Trennt der Server, steht der Grund nur in seinem Logbuch (so fand sich die Tiefe 8 aus 4.3 bei PAKET mit Phasen).
            throw new PruefungException("Verbindung getrennt: " + ex.Message + " | " + string.Join(" || ", t.Logbuch.TakeLast(25)));
        }
    }

    private static async Task PhasenAblaufAsync(Testserver t, Testuhr uhr)
    {
        var halter = new ServerHalter { Server = t };
        await FlotteAsync(t);
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(151));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(152));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(153));
        await using var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(154));
        Pruefe.Wahr(test.Client.Lage.NetzListe!.Phasen is null or { Count: 0 }, "ohne Phasen: kein Feld");
        Pruefe.Falsch(test.Pushes.Any(p => p.Body is PhaseGeschaltet), "ohne Phasen kein PhaseGeschaltet nach Welcome");

        // Anlegen nur in der Admin-Sitzung, als Paket mit frischem Code.
        var v = test.Client.Lage.NetzListe!.Version;
        var paket = new VerwaltungAnfrage { Op = VerwaltungsOperation.Paket, Version = v, Objekt = Paket((VerwaltungsOperation.PhaseSetzen, Anflug), (VerwaltungsOperation.PhaseSetzen, Entern)) };
        Pruefe.Gleich(Fehlercode.AdminSitzungNoetig, (await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Verwaltung, paket))).Code, "ohne Admin-Sitzung");
        var einzeln = new VerwaltungAnfrage { Op = VerwaltungsOperation.PhaseSetzen, Version = v, Objekt = SteuerJson.Element(Entern) };
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.Verwaltung, einzeln))).Code, "ein Funker legt keine Phase an");
        using var admin = await AdminSitzungAsync(t, uhr, test);
        uhr.Vor(TimeSpan.FromMinutes(6));
        Pruefe.Gleich(Fehlercode.SchrittBestaetigen, (await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Verwaltung, paket))).Code, "Paket nur mit frischem Code");
        Pruefe.Wahr((await Gedrosselt(() => test.Client.AdminBestaetigenAsync(admin.Code()))).Ok, "Code bestätigt");
        var angelegt = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Verwaltung, paket));
        Pruefe.Wahr(angelegt.Ok, "Phasen angelegt: " + angelegt.Fehler);
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.NetzListe?.Phasen is [{ Nr: 1, Name: "Anflug", Felder: null }, { Nr: 2, Name: "Entern", Felder: [{ RolleId: 13 }, { RolleId: 13 }, { RolleId: 13, NetzId: 50 }] }]),
            "NetzListe.phasen bei allen");
        var kaputt = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Verwaltung, new VerwaltungAnfrage
        {
            Op = VerwaltungsOperation.PhaseSetzen,
            Version = test.Client.Lage.NetzListe!.Version,
            Objekt = SteuerJson.Element(Entern with { Nr = 3, Felder = [Entern.Felder![0] with { RolleId = 77 }] }),
        }));
        Pruefe.Gleich(Fehlercode.Ungueltig, kaputt.Code, "unbekannte Rolle in der Variante");

        // Fritz und Gina steigen als Marines ein, Otto leitet im Auftrag.
        foreach (var m in new[] { fritz, gina })
            Pruefe.Wahr((await Gedrosselt(() => m.Client.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = 13, SchiffId = 3 }))).Ok, m.Name + " Marine");
        await t.AdminOk("auftrag", new JsonObject { ["konto"] = otto.KontoId });
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.HatRecht(Recht.Zuweisen)), "Otto im Auftrag");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = 2, Erwartet = 1 }))).Code,
            "ein Funker schaltet keine Phase");

        var schalten = await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = 2, Erwartet = 1 }));
        Pruefe.Wahr(schalten.Ok && schalten.Body is PhaseSchaltenAntwort { Nr: 2, Umgestellt: 2, Ausgelassen.Count: 0 }, "Otto schaltet auf Entern: " + schalten.Fehler);
        Pruefe.Wahr(await gina.PushAsync<PhaseGeschaltet>(p => p.Nr == 2 && p.Name == "Entern" && p.Kurz == "ENTERN" && p.Durch == otto.KontoId, TimeSpan.FromSeconds(10)) is not null,
            "PhaseGeschaltet an alle");
        Pruefe.Wahr(await test.PushAsync<PhaseGeschaltet>(p => p.Nr == 2, TimeSpan.FromSeconds(10)) is not null, "auch an den Admin ohne Platz");
        var mg = await fritz.PushAsync<Mitgliedschaften>(m => m.Phase == 2, TimeSpan.FromSeconds(10));
        Pruefe.Wahr(mg is not null && mg.Felder.Any(f => f.NetzId == 50 && f.Senden) && mg.Felder.Single(f => f.NetzId == 7).Senden is false,
            "Mitgliedschaften mit phase 2: Trupp Alpha senden, Crew nur hören");
        Pruefe.Wahr(mg!.Sendeauswahl.Felder.SequenceEqual([mg.Felder.Single(f => f.NetzId == 50).Feld]), "Sendenetz Trupp Alpha");

        // Zwei Leiter: der zweite bekommt VERALTET mit der laufenden Phase.
        var zweiter = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = 2, Erwartet = 1 }));
        Pruefe.Gleich(Fehlercode.Veraltet, zweiter.Code, "zweiter Leiter: VERALTET");
        Pruefe.Gleich("2", DetailText(zweiter, "nr"), "details.nr");
        Pruefe.Gleich(otto.KontoId.ToString(System.Globalization.CultureInfo.InvariantCulture), DetailText(zweiter, "durch"), "details.durch");
        Pruefe.Gleich(Fehlercode.Unbekannt, (await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = 4, Erwartet = 2 }))).Code,
            "Phase 4 gibt es nicht");

        // Nach Welcome kommt die laufende Phase.
        await using (var hans = await KernFunker.AktivAsync(halter, DiscordHans, "hans", "FUNKER", Lokal(155)))
            Pruefe.Wahr(await hans.PushAsync<PhaseGeschaltet>(p => p.Nr == 2 && p.Name == "Entern", TimeSpan.FromSeconds(10)) is not null, "PhaseGeschaltet nach Welcome");

        // Flut: Stoß 2, danach GRENZE ohne Trennung.
        await Task.Delay(4500);
        var codes = new List<Fehlercode?>();
        byte laufend = 2;
        for (var i = 0; i < 6; i++)
        {
            var nr = laufend == 2 ? (byte)1 : (byte)2;
            var a = await otto.Client.AnfrageAsync(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = nr, Erwartet = laufend });
            codes.Add(a.Code);
            if (a.Ok) laufend = nr;
        }
        Pruefe.Wahr(codes.Take(2).All(c => c is null) && codes.Skip(2).Any(c => c == Fehlercode.Grenze), "Stoß 2, dann GRENZE: " + string.Join(",", codes));
        Pruefe.Falsch(otto.Client.Zustand == ClientZustand.Getrennt, "die Flut trennt nicht");
        Pruefe.Gleich(laufend, await t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Neben.LaufendeNr()), "der Server steht auf der zuletzt bestätigten Phase");

        // Auf Entern schalten und die laufende Phase löschen: Rückfall auf die Grundstellung, mit Push.
        if (laufend != 2)
        {
            await Task.Delay(2200);
            Pruefe.Wahr((await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = 2, Erwartet = 1 }))).Ok, "wieder Entern");
        }
        gina.PushesLeeren();
        // Ein TOTP-Code gilt einmal je Zeitschritt; der nächste kommt 30 s später.
        uhr.Vor(TimeSpan.FromSeconds(30));
        Pruefe.Wahr((await Gedrosselt(() => test.Client.AdminBestaetigenAsync(admin.Code()))).Ok, "frischer Code fürs Löschen");
        var loeschen = await Gedrosselt(() => test.Client.AnfrageAsync(Nachrichtentyp.Verwaltung, new VerwaltungAnfrage
        {
            Op = VerwaltungsOperation.PhaseLoeschen,
            Version = test.Client.Lage.NetzListe!.Version,
            Objekt = SteuerJson.Element(new IdObjekt { Id = 2 }),
        }));
        Pruefe.Wahr(loeschen.Ok, "laufende Phase gelöscht: " + loeschen.Fehler);
        Pruefe.Wahr(await gina.PushAsync<PhaseGeschaltet>(p => p.Nr == 1 && p.Name == "Anflug", TimeSpan.FromSeconds(10)) is not null, "Rückfall auf Anflug gemeldet");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Mitgliedschaften?.Felder.Single(f => f.NetzId == 7).Senden == true), "Gina sendet wieder in der Crew");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.NetzListe?.Phasen is [{ Nr: 1 }]), "nur noch die Grundstellung");
    }

    // ---------------------------------------------------------------- Rückfall nach 12 h, Fassung

    [Test]
    public static async Task Rueckfall_NachDemNeustart_WennDasLetzteSchaltenUeber12StundenHer()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "s2-rueckfall-" + Guid.NewGuid().ToString("N")[..12]);
        var zustand = Path.Combine(verzeichnis, "zustand");
        var bauer = new Funk.Server.Lage.SchnappschussBauer()
            .MitNetz(new Funk.Server.Lage.Netz(2, "Kommando"))
            .MitPhase(new Funk.Server.Lage.Einsatzphase(1, "Anflug", "ANFLUG"))
            .MitPhase(new Funk.Server.Lage.Einsatzphase(2, "Entern", "ENTERN"));
        var neben = Funk.Server.Lage.Nebenstand.Leer with
        {
            Phase = new Funk.Server.Lage.LaufendePhase(2, Funk.Server.Betrieb.Zeit.LageMs() - 13 * 3600_000L, 0),
        };
        new Funk.Server.Kern.Zustandsablage(zustand).Speichern(bauer.Bauen(), neben);
        await using var t = await Testserver.StartenAsync(verzeichnis: verzeichnis);
        Pruefe.Wahr(await Warten(() => t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Neben.LaufendeNr()).Result == 1, 15_000),
            "der Takt fällt nach mehr als 12 h auf die Grundstellung zurück");
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(161));
        Pruefe.Wahr(await test.PushAsync<PhaseGeschaltet>(p => p.Nr == 1 && p.Name == "Anflug" && p.Durch == 0, TimeSpan.FromSeconds(10)) is not null, "nach Welcome: Anflug, durch 0");
    }

    [Test]
    public static async Task Draht_Client050AmServer040_SchicktWederFunkermodusNochPhaseSchalten()
    {
        await using var t = await Testserver.StartenAsync();
        t.Dienst.Zentrale.Version = "0.4.0";
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(171));
        Pruefe.Gleich("0.4.0", test.Client.Lage.Welcome?.Server?.Version, "Welcome meldet 0.4.0");
        var a = await test.Client.AnfrageAsync(Nachrichtentyp.Funkermodus, new FunkermodusAnfrage { An = true });
        var b = await test.Client.AnfrageAsync(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = 2, Erwartet = 1 });
        Pruefe.Wahr(a.Code == Fehlercode.Version && b.Code == Fehlercode.Version, "VERSION statt Senden");
        await Task.Delay(500);
        Pruefe.Wahr(test.Client.Zustand is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, "verbunden, war " + test.Client.Zustand);
        Pruefe.Wahr(test.Enden.IsEmpty, "keine Trennung");
    }
}
