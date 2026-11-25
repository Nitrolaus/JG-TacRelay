// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Ausbaustufe S2, Clientseite gegen den echten Server im Prozess (ohne TLS, ohne Geräte, ohne Fenster): Was der Client nach einem
/// Phasenwechsel übernimmt (die Sendeauswahl des Servers, auch wenn der Platz selbst gewählt war), die laufende Phase aus 0x4004 im
/// Lagebild, und die Bedienung über den FunkClientDienst wie im WPF-Client.
/// </summary>
internal static class S2ClientDrahtTests
{
    private const string DiscordTest = "970000000000000001";
    private const string DiscordOtto = "970000000000000002";
    private const string DiscordFritz = "970000000000000003";

    private static JsonNode Objekt(string json) => JsonNode.Parse(json)!;

    /// <summary>Flotte an der Konsole: Kommando (2), Crew 3 (7), Brücke 3 (8), Trupp Alpha (50), Orga (2000, offen), Rollen CO (10) und Marine (13).</summary>
    internal static async Task FlotteAsync(Testserver t)
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

    internal static NetzPhase Anflug => new() { Nr = 1, Name = "Anflug", Kurz = "ANFLUG" };

    /// <summary>"2 Entern": Marine hört die Crew, Kommando, und sendet in Trupp Alpha (Sendevorschlag).</summary>
    internal static NetzPhase Entern => new()
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

    /// <summary>Legt Anflug und Entern als Paket an: Admin-Sitzung, frischer Code.</summary>
    internal static async Task PhasenAnlegenAsync(Testserver t, Testuhr uhr, KernFunker admin)
    {
        using var sitzung = await AdminSitzungAsync(t, uhr, admin);
        uhr.Vor(TimeSpan.FromMinutes(6));
        Pruefe.Wahr((await Gedrosselt(() => admin.Client.AdminBestaetigenAsync(sitzung.Code()))).Ok, "Code bestätigt");
        var paket = SteuerJson.Element(new VerwaltungsPaket
        {
            Ops = [new VerwaltungsSchritt { Op = VerwaltungsOperation.PhaseSetzen, Objekt = SteuerJson.Element(Anflug) },
                new VerwaltungsSchritt { Op = VerwaltungsOperation.PhaseSetzen, Objekt = SteuerJson.Element(Entern) }],
        });
        var a = await Gedrosselt(() => admin.Client.AnfrageAsync(Nachrichtentyp.Verwaltung,
            new VerwaltungAnfrage { Op = VerwaltungsOperation.Paket, Version = admin.Client.Lage.NetzListe!.Version, Objekt = paket }));
        Pruefe.Wahr(a.Ok, "Phasen angelegt: " + a.Fehler);
    }

    [Test]
    public static async Task Phase_ClientUebernimmtSendeauswahl_AuchWennDerPlatzSelbstGewaehltWar()
    {
        // Gefunden beim Bau der Clientseite: Sitzung.Annehmen übernahm die Sendeauswahl des Servers nur bei einer Quelle außer
        // SELBST. Wer ein offenes Netz selbst dazugenommen hatte (Quelle SELBST), sendete nach "Entern" weiter auf sein altes Feld,
        // dort ohne Senderecht: Die Sprechtaste ging ins Leere.
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await FlotteAsync(t);
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(181));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(182));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(183));
        await PhasenAnlegenAsync(t, uhr, test);
        Pruefe.Wahr((await Gedrosselt(() => fritz.Client.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = 13, SchiffId = 3 }))).Ok,
            "Fritz Marine auf Schiff 3");
        Pruefe.Wahr((await Gedrosselt(() => fritz.Client.FeldSetzenAsync(5, 2000))).Ok, "Fritz nimmt Orga selbst dazu");
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften is { Quelle: MitgliedschaftsQuelle.Selbst } m && m.Felder.Any(f => f.NetzId == 2000)), "Quelle SELBST");
        Pruefe.Gleich(Sendeziel.Netz(7), fritz.Client.Sprechen.ZielAusAuswahl(), "vorher: Crew 3");

        await t.AdminOk("auftrag", new JsonObject { ["konto"] = otto.KontoId });
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.HatRecht(Recht.Zuweisen)), "Otto im Auftrag");
        var schalten = await Gedrosselt(() => otto.Client.AnfrageAsync(Nachrichtentyp.PhaseSchalten, new PhaseSchaltenAnfrage { Nr = 2, Erwartet = 1 }));
        Pruefe.Wahr(schalten.Ok, "Entern: " + schalten.Fehler);
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Mitgliedschaften?.Phase == 2), "Mitgliedschaften mit phase 2");
        Pruefe.Gleich(MitgliedschaftsQuelle.Selbst, fritz.Client.Lage.Mitgliedschaften!.Quelle, "die Quelle bleibt (Regel 9)");
        Pruefe.Wahr(await Warten(() => Equals(fritz.Client.Sprechen.ZielAusAuswahl(), Sendeziel.Netz(50))),
            "der Client übernimmt die Sendeauswahl des Servers: Trupp Alpha, war " + fritz.Client.Sprechen.ZielAusAuswahl());
        Pruefe.Wahr(await Warten(() => fritz.Client.Lage.Phase is { Nr: 2, Name: "Entern", Kurz: "ENTERN" } p && p.Durch == otto.KontoId),
            "die laufende Phase steht im Lagebild");
    }
}
