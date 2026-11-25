// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Kanalsperren über den Draht (Fassung 0.6.0, 0x003A KanalSperre, FUNKPROTOKOLL 6.17, P-63): mit der Client-Bibliothek, ohne TLS,
/// prozessintern. Ein Funker sperrt nicht und liest keine Liste; die Einsatzleitung sperrt bis 04:00, der Gesperrte sieht die Sperre in
/// <c>Mitgliedschaften.gesperrt</c>, die Einsatzleitung in den Praesenz-Details und als Meldung KANALSPERRE; FeldSetzen nennt Grund,
/// Setzenden und Ende; das Journal nennt Kanal und Klarnamen. Dazu die Sperre für alle, die Ratengrenze und ein Server 0.5.0.
/// </summary>
internal static class Stufe3KanalsperreDrahtTests
{
    private const string DiscordTest = "960000000000000001";
    private const string DiscordOtto = "960000000000000002";
    private const string DiscordGina = "960000000000000003";

    private static ushort K(ushort netz, byte ch) => Kanalrechnung.Id(netz, ch);

    private static async Task KanaeleAsync(Testserver t)
    {
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(Stufe3VersionstorTests.KanalBrueckeJson) });
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":101,"name":"Kommando","typ":"NORMAL","reihenfolge":11,"nurTaste":true,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false,"kanaele":["Ruhe","Kommando 1","Kommando 2"]}"""),
        });
    }

    private static bool HatMeldung(KernFunker f, string op) =>
        f.Pushes.Any(p => p.Body is Meldung { Art: MeldungsArten.Kanalsperre, Details: { ValueKind: JsonValueKind.Object } d }
            && d.TryGetProperty("op", out var o) && o.GetString() == op);

    [Test]
    public static async Task Draht_KanalSperre_SetzenSehenAblehnenAufheben()
    {
        await using var t = await Testserver.StartenAsync();
        await KanaeleAsync(t);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(241));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(242));
        await using var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(243));
        var schiff3 = K(100, 3);
        Pruefe.Wahr((await Gedrosselt(() => otto.Client.FeldSetzenAsync(0, schiff3))).Ok, "Otto in Schiff 3");
        Pruefe.Wahr((await Gedrosselt(() => gina.Client.FeldSetzenAsync(0, schiff3))).Ok, "Gina in Schiff 3");

        Pruefe.Gleich(Fehlercode.KeinRecht, (await gina.Client.KanalSperreAsync(KanalSperreOperation.Setzen, otto.KontoId, schiff3)).Code, "ein Funker sperrt nicht");
        Pruefe.Gleich(Fehlercode.KeinRecht, (await gina.Client.KanalSperreAsync(KanalSperreOperation.Liste)).Code, "und liest keine Liste");
        Pruefe.Gleich(Fehlercode.Ungueltig, (await test.Client.KanalSperreAsync(KanalSperreOperation.Setzen, otto.KontoId, 1)).Code, "Global sperrt niemand");

        var vorher = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var a = await test.Client.KanalSperreAsync(KanalSperreOperation.Setzen, otto.KontoId, schiff3);
        Pruefe.Wahr(a.Ok, "Test sperrt Otto in Schiff 3: " + a.Fehler?.Code);
        var sperre = a.Als<KanalSperreAntwort>().Sperren.Single();
        Pruefe.Wahr(sperre.KontoId == otto.KontoId && sperre.NetzId == schiff3 && sperre.Durch == test.KontoId, "die Antwort nennt die Sperre");
        Pruefe.Wahr(sperre.Bis > vorher && sperre.Bis - vorher <= Kanalrechnung.SperreMaxMs && Auftragszeit.Anzeigen(sperre.Bis).EndsWith("04:00", StringComparison.Ordinal),
            "ohne bis: bis zum nächsten 04:00 (" + Auftragszeit.Anzeigen(sperre.Bis) + ")");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften?.Gesperrt is [{ NetzId: var n, Alle: null }] && n == schiff3),
            "Otto sieht seine Sperre in Mitgliedschaften.gesperrt");
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Praesenz.TryGetValue(otto.KontoId, out var e) && e.Gesperrt is { Count: 1 }), "die Einsatzleitung sieht sie in den Details");
        Pruefe.Wahr(await Warten(() => gina.Client.Lage.Praesenz.TryGetValue(otto.KontoId, out var e) && e.Gesperrt is null), "ein Funker nicht");
        Pruefe.Wahr(await Warten(() => HatMeldung(test, "GESETZT")), "Meldung KANALSPERRE an die Einsatzleitung");
        Pruefe.Falsch(HatMeldung(gina, "GESETZT"), "nicht an Funker");

        var wechsel = await Gedrosselt(() => otto.Client.FeldSetzenAsync(0, K(100, 4)));
        Pruefe.Gleich(Fehlercode.KeinRecht, wechsel.Code, "kein Ch.-Wechsel im gesperrten Feld");
        var details = wechsel.Fehler!.Details!.Value;
        Pruefe.Wahr(details.GetProperty("grund").GetString() == "GESPERRT" && details.GetProperty("durch").GetUInt32() == test.KontoId
            && details.GetProperty("bis").GetInt64() == sperre.Bis, "details nennen Grund, Setzenden und Ende: " + details);

        Pruefe.Wahr(await gina.SpruchAsync(Sendeziel.Netz(schiff3)), "Gina spricht in Schiff 3");
        await Task.Delay(300);
        Pruefe.Falsch(otto.Empfang.Pakete.Any(p => p.Absender == gina.KontoId && p.Netze.Contains(schiff3) && !p.Ende), "Otto hört Gina nicht");

        var liste = await test.Client.KanalSperreAsync(KanalSperreOperation.Liste);
        Pruefe.Wahr(liste.Ok && liste.Als<KanalSperreAntwort>().Sperren.Count == 1, "LISTE für die Einsatzleitung");
        var journal = (await t.AdminOk("journal", new JsonObject { ["anzahl"] = 20 })).Text;
        Pruefe.Wahr(journal.Contains("KANAL_GESPERRT", StringComparison.Ordinal) && journal.Contains("Brücke Ch. 3 (Schiff 3)", StringComparison.Ordinal)
            && journal.Contains("otto", StringComparison.OrdinalIgnoreCase), "Journal mit Kanal und Klarnamen: " + journal);

        var weg = await test.Client.KanalSperreAsync(KanalSperreOperation.Aufheben, otto.KontoId, schiff3);
        Pruefe.Wahr(weg.Ok && weg.Als<KanalSperreAntwort>().Sperren.Count == 0, "aufgehoben");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften?.Gesperrt is null), "Mitgliedschaften ohne Sperre");
        Pruefe.Wahr((await t.AdminOk("journal", new JsonObject { ["anzahl"] = 20 })).Text.Contains("KANAL_FREI", StringComparison.Ordinal), "Journal KANAL_FREI");

        var alle = await test.Client.KanalSperreAsync(KanalSperreOperation.Setzen, 0, 101);
        Pruefe.Wahr(alle.Ok, "Kommando für alle gesperrt");
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften?.Gesperrt is [{ NetzId: 101, Alle: true }]), "Otto sieht die Sperre für alle");
        Pruefe.Wahr(test.Client.Lage.Mitgliedschaften?.Gesperrt is null, "die Einsatzleitung bleibt ausgenommen");
        Pruefe.Wahr(otto.Enden.IsEmpty && gina.Enden.IsEmpty, "niemand wurde getrennt");
    }

    [Test]
    public static async Task Draht_KanalSperre_Flut_GrenzeOhneTrennung_UndServer050()
    {
        await using var t = await Testserver.StartenAsync();
        await KanaeleAsync(t);
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(251));
        await Task.Delay(2500);
        var codes = new List<Fehlercode?>();
        for (var i = 0; i < 30; i++) codes.Add((await test.Client.KanalSperreAsync(KanalSperreOperation.Liste)).Code);
        Pruefe.Wahr(codes.Take(20).All(c => c is null), "Stoß 20 geht durch: " + string.Join(",", codes));
        Pruefe.Wahr(codes.Skip(20).Count(c => c == Fehlercode.Grenze) >= 8, "danach GRENZE: " + string.Join(",", codes));
        await Task.Delay(300);
        Pruefe.Wahr(test.Enden.IsEmpty && test.Client.Zustand is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, "die Flut trennt nicht");

        await using var alt = await Testserver.StartenAsync();
        alt.Dienst.Zentrale.Version = "0.5.0";
        await using var otto = await KernFunker.AktivAsync(new ServerHalter { Server = alt }, DiscordOtto, "otto", "ADMIN", Lokal(252));
        Pruefe.Gleich(Fehlercode.Version, (await otto.Client.KanalSperreAsync(KanalSperreOperation.Liste)).Code, "an einen Server 0.5.0 schickt der Client die Anfrage nicht");
    }
}
