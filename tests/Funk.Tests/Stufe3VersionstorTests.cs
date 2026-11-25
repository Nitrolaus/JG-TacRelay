// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using System.Text.Json.Nodes;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Versionstor (Fassung 0.6.0, FUNKPROTOKOLL 4.7, P-62) über den Draht, ohne TLS, prozessintern: Ohne Kanalnetz bleibt ein Client
/// 0.5.x verbunden. Der Commit, der das erste Kanalnetz anlegt, trennt jede Verbindung unter 0.6.0 mit <c>VERSION_ZU_ALT</c> und
/// <c>mindest</c> 0.6.0, auch in der Registrierung; ein neues Hello unter 0.6.0 ebenso, ab 0.6.0 nicht. Nach dem Löschen des letzten
/// Kanalnetzes fällt das Tor.
/// </summary>
internal static class Stufe3VersionstorTests
{
    private const string DiscordAlt = "970000000000000001";
    private const string DiscordNeu = "970000000000000002";

    internal const string KanalBrueckeJson =
        """{"id":100,"name":"Brücke","typ":"NORMAL","reihenfolge":10,"nurTaste":false,"ausloeserStandard":"VA","offen":"HOEREN_SENDEN","standard":false,"gruppe":"Schiffe","kanaele":["Ruhe","Schiff 1","Schiff 2","Schiff 3","Schiff 4","Schiff 5"]}""";

    /// <summary>Ein Rohclient mit der Fassung <paramref name="version"/> bis AKTIV (wie <see cref="Testserver.AktivAsync"/>).</summary>
    private static async Task<Testfunker> AktivAlsAsync(Testserver t, string version, string discord, string benutzer)
    {
        var f = await t.VerbindenAsync(null, Lokal(201));
        var w = await f.HelloAsync(version);
        Pruefe.Gleich(WelcomeStatus.Unbekannt, w.Status, "Welcome für ein neues Gerät");
        var code = await f.RegistrierenAsync("PC " + benutzer);
        var verknuepfen = t.Admin("verknuepfen", new JsonObject { ["code"] = code, ["discord"] = discord, ["benutzername"] = benutzer });
        var rueckfrage = await f.ErwarteAsync(Nachrichtentyp.VerknuepfungBestaetigen);
        await f.AntwortAsync(Nachrichtentyp.VerknuepfungBestaetigen, rueckfrage.Kopf.AnfrageNr, new VerknuepfungBestaetigenAntwort { Ja = true });
        Pruefe.Wahr((await verknuepfen).Ok, "verknuepft");
        await f.ErwarteAsync(Nachrichtentyp.GeraetStatus);
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = f.Fingerabdruck.AlsBase64() });
        await f.SyncErwartenAsync();
        return f;
    }

    private static async Task VersionZuAltAsync(Testfunker f, string was)
    {
        var fehler = (await f.ErwarteAsync(Nachrichtentyp.Fehler)).Json<Fehler>();
        Pruefe.Gleich(Fehlercode.VersionZuAlt, fehler.Code, was + ": VERSION_ZU_ALT");
        Pruefe.Gleich("0.6.0", fehler.Details is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("mindest", out var m) ? m.GetString() : null, was + ": mindest 0.6.0");
        await f.WartenAufTrennungAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task<Fehler?> HelloAsync(Testserver t, string version)
    {
        await using var f = await t.VerbindenAsync(null, Lokal(203));
        await f.SendenAsync(Nachrichtentyp.Hello, new Hello
        {
            Minor = 0,
            Client = new ClientAngabe { Name = "Probe", Version = version },
            SchluesselArt = SchluesselArt.Software,
            Opus = "1.6.1",
            Faehigkeiten = [],
        });
        var r = await f.ErwarteAsync(x => x.Typ is Nachrichtentyp.Fehler or Nachrichtentyp.Welcome);
        return r.Typ == Nachrichtentyp.Fehler ? r.Json<Fehler>() : null;
    }

    [Test]
    public static async Task Versionstor_CommitTrenntAlteClients_HelloAbgewiesen_LoeschenOeffnet()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await using var neu = await KernFunker.AktivAsync(halter, DiscordNeu, "neu", "FUNKER", Lokal(202));
        await using var alt = await AktivAlsAsync(t, "0.5.1", DiscordAlt, "alt");
        await using var registrierung = await t.VerbindenAsync(null, Lokal(204));
        Pruefe.Gleich(WelcomeStatus.Unbekannt, (await registrierung.HelloAsync("0.5.0")).Status, "ein Gerät 0.5.0 in der Registrierung");
        Pruefe.Gleich<Fehler?>(null, await HelloAsync(t, "0.5.1"), "ohne Kanalnetz: Hello 0.5.1 bekommt Welcome");

        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(KanalBrueckeJson) });
        await VersionZuAltAsync(alt, "Sitzung 0.5.1 beim Commit des ersten Kanalnetzes");
        await VersionZuAltAsync(registrierung, "Registrierung 0.5.0");
        Pruefe.Wahr(await Warten(() => neu.Client.Lage.NetzListe?.Netze.Any(n => n.Id == 100 && n.Kanaele is { Count: 6 } && n.Gruppe == "Schiffe") == true),
            "der Client ab 0.6.0 bekommt die NetzListe mit dem Kanalnetz");
        await Task.Delay(300);
        Pruefe.Wahr(neu.Enden.IsEmpty && neu.Client.Zustand is ClientZustand.AktivUdp or ClientZustand.AktivTunnel, "und bleibt verbunden");

        Pruefe.Gleich(Fehlercode.VersionZuAlt, (await HelloAsync(t, "0.5.1"))?.Code, "neues Hello 0.5.1");
        Pruefe.Gleich(Fehlercode.VersionZuAlt, (await HelloAsync(t, "0.6.0-vorab"))?.Code, "eine Vorabfassung vor 0.6.0");
        Pruefe.Gleich<Fehler?>(null, await HelloAsync(t, "0.6.0"), "Hello 0.6.0");
        Pruefe.Gleich<Fehler?>(null, await HelloAsync(t, "1.0.0"), "Hello 1.0.0");

        await t.AdminOk("netz-loeschen", new JsonObject { ["id"] = 100 });
        Pruefe.Gleich<Fehler?>(null, await HelloAsync(t, "0.5.1"), "ohne Kanalnetz fällt das Tor");
    }

    [Test]
    public static async Task Versionstor_GlobalMitKanaelen_ZaehltAlsKanalnetz()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":1,"name":"Global","typ":"GLOBAL","reihenfolge":0,"nurTaste":true,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false,"kanaele":["Ruhe","Gesamt","Global 1","Global 2"]}"""),
        });
        Pruefe.Gleich(Fehlercode.VersionZuAlt, (await HelloAsync(t, "0.5.1"))?.Code, "Global mit Ch. öffnet das Tor nicht für 0.5.x");
        Pruefe.Gleich<Fehler?>(null, await HelloAsync(t, "0.6.0"), "0.6.0 kommt durch");
    }
}
