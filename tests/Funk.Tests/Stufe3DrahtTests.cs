// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Kanäle über den Draht (Fassung 0.6.0, 0x0035 mit Kanal-IDs, 0x0039 RolleWaehlen, NetzListe mit <c>kanaele</c>, <c>gruppe</c> und
/// <c>ch</c>, <c>Mitgliedschaften.rolle</c>, <c>rolle</c> in den Praesenz-Details, P-61, P-62): mit der Client-Bibliothek, ohne TLS,
/// prozessintern. Dazu Sprache in einem Kanal mit dem Stempel der Kanal-ID, die Flut von FeldSetzen und RolleWaehlen an der
/// gemeinsamen Ratengrenze und ein Client 0.6.0 an einem Server 0.5.0.
/// </summary>
internal static class Stufe3DrahtTests
{
    private const string DiscordTest = "980000000000000001";
    private const string DiscordOtto = "980000000000000002";
    private const string DiscordGina = "980000000000000003";

    private static ushort K(ushort netz, byte ch) => Kanalrechnung.Id(netz, ch);

    private static async Task KanaeleAsync(Testserver t)
    {
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(Stufe3VersionstorTests.KanalBrueckeJson) });
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":101,"name":"Kommando","typ":"NORMAL","reihenfolge":11,"nurTaste":true,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false,"kanaele":["Ruhe","Kommando 1","Kommando 2"]}"""),
        });
        await t.AdminOk("rolle-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":12,"name":"Kapitän","reihenfolge":12,"selbstWaehlbar":true,"brauchtSchiff":false,"felder":[{"feld":0,"netzId":101,"senden":true,"hoeren":true,"sendenStandard":true,"fest":false},{"feld":1,"netzId":100,"senden":true,"hoeren":true,"sendenStandard":false,"fest":false}]}"""),
        });
        await t.AdminOk("rolle-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":15,"name":"Feste Brücke","reihenfolge":15,"selbstWaehlbar":true,"brauchtSchiff":false,"felder":[{"feld":0,"netzId":100,"senden":true,"hoeren":true,"sendenStandard":true,"fest":false,"ch":2}]}"""),
        });
    }

    [Test]
    public static async Task Draht_NetzListeRolleWaehlenFeldSetzenUndSpracheImKanal()
    {
        await using var t = await Testserver.StartenAsync();
        await KanaeleAsync(t);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(211));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(212));
        await using var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(213));
        var liste = otto.Client.Lage.NetzListe!;
        Pruefe.Wahr(liste.Netze.Single(n => n.Id == 100) is { Gruppe: "Schiffe", Kanaele: [ "Ruhe", "Schiff 1", .., "Schiff 5" ] }, "NetzListe trägt kanaele und gruppe");
        Pruefe.Wahr(liste.Rollen.Single(r => r.Id == 15).Felder.Single().Ch == 2, "Rollenfeld mit ch");

        var a = await Gedrosselt(() => otto.Client.RolleWaehlenAsync(12, [new ChWunsch { Feld = 1, Ch = 3 }]));
        Pruefe.Wahr(a.Ok && a.Body is SatzAnwendenAntwort { Uebersprungen.Count: 0 }, "Otto wählt Kapitän: " + a.Fehler);
        // Seit 0.8.1 (Q46) wendet der Server den Wunsch eines alten Clients nicht mehr an: alles auf Ruhe, Otto wechselt selbst.
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften is { Rolle: 12 } m && m.Felder.Any(f => f.Feld == 0 && f.NetzId == K(101, 0))
            && m.Felder.Any(f => f.Feld == 1 && f.NetzId == K(100, 0))), "Mitgliedschaften: rolle 12, Kommando und Brücke auf Ruhe");
        var wechsel = await Gedrosselt(() => otto.Client.FeldSetzenAsync(1, K(100, 3)));
        Pruefe.Wahr(wechsel.Ok, "Otto wechselt auf Schiff 3: " + wechsel.Fehler);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften is { } m2 && m2.Felder.Any(f => f.Feld == 1 && f.NetzId == K(100, 3))), "Brücke Schiff 3");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await Gedrosselt(() => otto.Client.FeldSetzenAsync(0, K(100, 4)))).Code, "ein anderes Kanalnetz ins Kommando-Feld (Q44)");
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Praesenz.TryGetValue(otto.KontoId, out var e) && e.Rolle == 12), "die Einsatzleitung sieht die gewählte Rolle");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Praesenz.TryGetValue(otto.KontoId, out var e) && e.Rolle is null), "ein Funker nicht");
        Pruefe.Gleich(Fehlercode.Unbekannt, (await Gedrosselt(() => otto.Client.RolleWaehlenAsync(99))).Code, "unbekannte Rolle");

        var feld = await Gedrosselt(() => gina.Client.FeldSetzenAsync(0, K(100, 3)));
        Pruefe.Wahr(feld.Ok, "Gina legt Schiff 3 in Feld 0: " + feld.Fehler);
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.FeldMitNetz(K(100, 3)) is { Hoeren: true, Senden: true }), "Gina hört und sendet Schiff 3");
        Pruefe.Gleich(Fehlercode.Unbekannt, (await Gedrosselt(() => gina.Client.FeldSetzenAsync(1, K(100, 6)))).Code, "Ch. 6 gibt es nicht");
        Pruefe.Gleich(Fehlercode.Ungueltig, (await Gedrosselt(() => gina.Client.FeldSetzenAsync(1, 100))).Code, "das Kanalnetz selbst");
        Pruefe.Wahr(gina.Client.Lage.NetzVon(K(100, 3))?.Id == 100, "der Client findet zur Kanal-ID das Kanalnetz (kein unbekanntes Netz, C8)");

        Pruefe.Wahr(await otto.SpruchAsync(Sendeziel.Netz(K(100, 3))), "Otto spricht in Schiff 3");
        Pruefe.Wahr(await Warten(() => gina.Empfang.Pakete.Any(p => p.Absender == otto.KontoId && p.Netze.SequenceEqual([K(100, 3)]))), "Gina empfängt mit der Kanal-ID im Stempel");
        Pruefe.Falsch(test.Empfang.Pakete.Any(p => p.Absender == otto.KontoId), "Test hat den Kanal nicht");
    }

    [Test]
    public static async Task Draht_Flut_FeldSetzenUndRolleWaehlen_GrenzeOhneTrennung()
    {
        await using var t = await Testserver.StartenAsync();
        await KanaeleAsync(t);
        var halter = new ServerHalter { Server = t };
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(221));
        await Task.Delay(2500);
        var codes = new List<Fehlercode?>();
        byte letzter = 0;
        for (var i = 0; i < 30; i++)
        {
            var ch = (byte)(1 + i % 5);
            var a = await otto.Client.FeldSetzenAsync(0, K(100, ch));
            codes.Add(a.Code);
            if (a.Ok) letzter = ch;
        }
        var rolle = await otto.Client.RolleWaehlenAsync(12);
        Pruefe.Wahr(codes.Take(10).All(c => c is null), "Stoß 10 geht durch: " + string.Join(",", codes));
        Pruefe.Wahr(codes.Skip(10).Count(c => c == Fehlercode.Grenze) >= 15, "danach GRENZE: " + string.Join(",", codes));
        Pruefe.Gleich(Fehlercode.Grenze, rolle.Code, "RolleWaehlen teilt sich den Eimer");
        await Task.Delay(300);
        Pruefe.Wahr(otto.Enden.IsEmpty && otto.Client.Zustand is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, "die Flut trennt nicht");
        var stand = await t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Aktuell.Konten[otto.KontoId].Felder.Single());
        Pruefe.Gleich(K(100, letzter), stand.NetzId, "der Server steht auf dem zuletzt angenommenen Ch.");
    }

    [Test]
    public static async Task Draht_Client060AmServer050_SchicktRolleWaehlenNicht()
    {
        await using var t = await Testserver.StartenAsync();
        t.Dienst.Zentrale.Version = "0.5.0";
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "FUNKER", Lokal(231));
        var a = await test.Client.RolleWaehlenAsync(12);
        Pruefe.Gleich(Fehlercode.Version, a.Code, "VERSION statt Senden");
        await Task.Delay(500);
        Pruefe.Wahr(test.Enden.IsEmpty && test.Client.Zustand is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, "verbunden, keine Trennung");
    }
}
