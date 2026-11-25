// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using Funk.Client.Dienste;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Das Brett sperrt über den Dienst (Fassung 0.6.0, Schritt 3.6, FUNKPROTOKOLL 6.17) gegen den echten Server im Prozess, ohne TLS und
/// ohne Fenster: <see cref="FunkClientDienst.KanalSperreAsync"/> eines Admins setzt eine Kanalsperre bis 04:00, die Lage der Einsatzleitung
/// zeigt sie in den Details der Person (Praesenz), Aufheben räumt sie wieder weg; ein Funker bekommt den Satz "Dafür fehlt das Recht."
/// </summary>
internal static class Stufe36DrahtTests
{
    private const string DiscordTest = "990000000000000001";
    private const string DiscordOtto = "990000000000000002";

    internal static async Task<FunkClientDienst> DienstAsync(Testserver t, ServerHalter halter, Geraeteidentitaet identitaet, string discord, string name, string stufe, int adresse)
    {
        var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = halter.Aufbau(identitaet, Lokal(adresse)),
            Identitaet = identitaet,
            Geraetename = "PC " + name,
            ClientName = "Kerntest",
            ClientVersion = "0.6.0",
            Rueckfrage = (_, _) => Task.FromResult(true),
            Empfaenger = new Sammler(),
            Zufall = () => 0.0,
        });
        var d = new FunkClientDienst(() => new FunkAnbindung(client, null, null, null, null));
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null), name + ": Registrierungscode");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = discord, ["benutzername"] = name, ["stufe"] = stufe });
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum), name + ": Warteraum");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = identitaet.Fingerabdruck.Anzeige });
        Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Abgeglichen && d.Lage.ServerKanaele), name + ": aktiv, Server kennt Kanäle");
        return d;
    }

    [Test]
    public static async Task Draht_Dienst_KanalSperre_SetzenSehenAufheben()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(Stufe3VersionstorTests.KanalBrueckeJson) });
        var halter = new ServerHalter { Server = t };
        using var idJu = Geraeteidentitaet.Fluechtig();
        using var idOtto = Geraeteidentitaet.Fluechtig();
        await using var test = await DienstAsync(t, halter, idJu, DiscordTest, "test", "ADMIN", 241);
        await using var otto = await DienstAsync(t, halter, idOtto, DiscordOtto, "otto", "FUNKER", 242);
        var schiff3 = Kanalrechnung.Id(100, 3);
        Pruefe.Gleich<string?>(null, await otto.FeldSetzenAsync(0, schiff3), "Otto in Schiff 3");
        var kontoOtto = otto.Lage.Konto!.Id;

        Pruefe.Gleich("Dafür fehlt das Recht.", await otto.KanalSperreAsync(test.Lage.Konto!.Id, schiff3, sperren: true), "ein Funker sperrt nicht");
        Pruefe.Gleich<string?>(null, await test.KanalSperreAsync(kontoOtto, schiff3, sperren: true), "der Admin sperrt Otto in Schiff 3");
        Pruefe.Wahr(await Warten(() => test.Lage.Funker.FirstOrDefault(f => f.KontoId == kontoOtto)?.Details?.SperreVon(schiff3) is { } s
            && s.Durch == test.Lage.Konto.Id && Auftragszeit.Anzeigen(s.Bis.ToUnixTimeMilliseconds()).EndsWith("04:00", StringComparison.Ordinal)),
            "die Einsatzleitung sieht die Sperre bis 04:00 in den Details");
        Pruefe.Wahr(await Warten(() => otto.Lage.SperreVon(schiff3) is not null), "Otto sieht sie in seiner Lage");

        Pruefe.Gleich<string?>(null, await test.KanalSperreAsync(kontoOtto, schiff3, sperren: false), "aufheben");
        Pruefe.Wahr(await Warten(() => test.Lage.Funker.FirstOrDefault(f => f.KontoId == kontoOtto)?.Details is { Gesperrt.Count: 0 }), "Details ohne Sperre");
        Pruefe.Wahr(await Warten(() => otto.Lage.SperreVon(schiff3) is null), "Otto wieder frei");
    }
}
