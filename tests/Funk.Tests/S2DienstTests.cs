// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Tests.Oberflaeche;
using Funk.Verwaltung;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Ausbaustufe S2, Client wie im WPF-Programm gegen den echten Server im Prozess (FunkClientDienst über den Testzugang, ohne TLS,
/// ohne Geräte, ohne Fenster). Habicht (Admin) schaltet "Heute als Funker" mit Rückfrage ein und sitzt danach als Marine auf Schiff 3
/// wie Fritz; Otto leitet im Auftrag und schaltet mit zwei Drucken der Phasentaste auf "2 Entern". Die Meldezeile der Leitung sagt
/// es, der Verlauf bekommt den Eintrag, und Fritz liest in seiner Meldezeile, wohin er jetzt sendet. Der Verwaltungsteil kennt am
/// Server 0.5.0 die Phasen und die laufende. Zuletzt "Wieder Admin".
/// </summary>
internal static class S2DienstTests
{
    private const string DiscordTest = "980000000000000001";
    private const string DiscordOtto = "980000000000000002";
    private const string DiscordFritz = "980000000000000003";
    private const string DiscordHabicht = "980000000000000004";

    /// <summary>Ein Funker wie der WPF-Client: verbinden, Gerät verknüpfen und freigeben, bis aktiv.</summary>
    private static async Task<FunkClientDienst> DienstAsync(Testserver t, TestzugangLauscher zugang, string geraet, string discord, string name, string stufe)
    {
        var d = new FunkClientDienst(() => Verbindungsfabrik.Bauen(new VerbindungsEinstellungen { Geraetename = geraet }, (_, _) => Task.FromResult(true), null,
            zugang.Endpunkt.ToString()));
        await d.VerbindenAsync();
        Pruefe.Wahr(await Warten(() => d.Lage.Registrierung is not null), name + ": Registrierungscode");
        await t.AdminOk("verknuepfen", new JsonObject { ["code"] = d.Lage.Registrierung!.Code, ["discord"] = discord, ["benutzername"] = name, ["stufe"] = stufe });
        Pruefe.Wahr(await Warten(() => d.Lage.Status == Verbindungsstatus.Warteraum), name + ": Warteraum");
        await t.AdminOk("freigeben", new JsonObject { ["geraet"] = d.Lage.Geraet });
        Pruefe.Wahr(await Warten(() => d.Lage.IstAktiv && d.Lage.Konto is not null), name + ": aktiv");
        return d;
    }

    [Test]
    public static async Task Dienst_FunkerModusPhasentasteMeldezeileUndVerwaltung()
    {
        var uhr = new Testuhr();
        await using var t = await Testserver.StartenAsync(zeit: uhr);
        var halter = new ServerHalter { Server = t };
        await S2ClientDrahtTests.FlotteAsync(t);
        await using var zugang = new TestzugangLauscher(t.Dienst);
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(191));
        await S2ClientDrahtTests.PhasenAnlegenAsync(t, uhr, test);
        await using var otto = await DienstAsync(t, zugang, "PC Otto", DiscordOtto, "otto", "FUNKER");
        await using var fritz = await DienstAsync(t, zugang, "PC Fritz", DiscordFritz, "fritz", "FUNKER");
        await using var habicht = await DienstAsync(t, zugang, "PC Habicht", DiscordHabicht, "habicht", "ADMIN");
        Pruefe.Wahr(await Warten(() => otto.Lage is { ServerS2: true, Phasen.Count: 2 }), "der Server kennt Phasen, die Lage hat Anflug und Entern");

        // Die Oberflächenmodelle leben auf einem eigenen Thread mit Kontext, wie auf dem Oberflächen-Thread.
        using var faden = new EinFaden();
        var takt = new FadenTakt(faden);
        long ms = 0;
        using var ordner = new Testordner();
        var speicher = new EinstellungsSpeicher(ordner.Datei("einstellungen.json"));
        using var einstellungen = new Einstellungsablage(speicher, speicher.Laden(), takt, TimeSpan.FromHours(1));

        // Habicht: "Heute als Funker" mit Rückfrage, danach Marine auf Schiff 3 wie jeder.
        var funker = await faden.Ausfuehren(() => new FunkermodusViewModel(habicht, einstellungen, takt));
        Pruefe.Wahr(await faden.Warten(() => funker.Sichtbar), "Habicht ist Admin am Server 0.5.0: Schalter da");
        await faden.Ausfuehren(() => { funker.EinschaltenCommand.Execute(null); return true; });
        Pruefe.Wahr(await faden.Ausfuehren(() => funker.HatRueckfrage), "erst die Rückfrage");
        await faden.AusfuehrenAsync(async () => { await funker.JaCommand.ExecuteAsync(null); return true; });
        Pruefe.Wahr(await faden.Warten(() => funker.An && funker.StandText?.StartsWith("als Funker bis ", StringComparison.Ordinal) == true),
            "eingeschaltet: " + await faden.Ausfuehren(() => funker.Fehler ?? funker.StandText));
        Pruefe.Wahr(await Warten(() => habicht.Lage.Konto is { AlsFunker: true, Einsatzleitung: false }), "die Rechte der Einsatzleitung ruhen");
        Pruefe.Gleich(null, await habicht.EinsteigenAsync(13, 3), "Habicht als Marine auf Schiff 3");
        Pruefe.Gleich(null, await fritz.EinsteigenAsync(13, 3), "Fritz als Marine auf Schiff 3");

        // Otto leitet im Auftrag; Fritz sieht seine Meldezeile.
        await t.AdminOk("auftrag", new JsonObject { ["konto"] = otto.Lage.Konto!.Id });
        Pruefe.Wahr(await Warten(() => otto.Lage.Konto?.Einsatzleitung == true), "Otto im Auftrag");
        var zeileOtto = await faden.Ausfuehren(() => new Meldezeile(() => ms));
        var phasen = await faden.Ausfuehren(() => new Phasensteuerung(otto, zeileOtto, () => ms));
        var verlauf = new List<string>();
        phasen.Verlauf += verlauf.Add;
        var zeileFritz = await faden.Ausfuehren(() => new Meldezeile(() => ms));
        using var teilnehmer = await faden.Ausfuehren(() => new Teilnehmermeldungen(fritz, zeileFritz, takt));
        Pruefe.Wahr(await Warten(() => fritz.Lage.RolleId == 13 && otto.Lage.Funker.Count(f => f.RolleId == 13) == 2), "beide Marines in Ottos Lage");

        await faden.AusfuehrenAsync(async () => { await phasen.TasteAsync(1); return true; });
        var scharf = await faden.Ausfuehren(() => zeileOtto.Text);
        Pruefe.Wahr(scharf.StartsWith("Phase 2/2 ENTERN? Nochmal drücken - 2 Funker", StringComparison.Ordinal), "erster Druck: scharf, mit Habicht als Funker: " + scharf);
        Pruefe.Falsch(scharf.Contains("ausgelassen", StringComparison.Ordinal), "ein Admin im Funker-Modus wird nicht ausgelassen");
        ms += 600;
        await faden.AusfuehrenAsync(async () => { await phasen.TasteAsync(1); return true; });
        Pruefe.Wahr(await Warten(() => otto.Lage.PhaseNr == 2), "zweiter Druck: Entern läuft");
        var geschaltet = await faden.Ausfuehren(() => zeileOtto.Text);
        Pruefe.Gleich("Phase 2 Entern läuft - 2 umgestellt", geschaltet, "Meldezeile der Leitung");
        Pruefe.Wahr(verlauf is ["du: Phase Entern, 2 umgestellt"], "Verlauf: " + string.Join(" | ", verlauf));
        Pruefe.Wahr(await faden.Warten(() => zeileFritz.Text.EndsWith(": sendest in Trupp Alpha", StringComparison.Ordinal)
                && zeileFritz.Text.StartsWith("Phase Entern (", StringComparison.Ordinal)),
            "Fritz liest, wohin er jetzt sendet: " + await faden.Ausfuehren(() => zeileFritz.Text));

        // Der Verwaltungsteil kennt am Server 0.5.0 die Phasen und weiß, welche läuft.
        var wirt = new VerwaltungsdienstTests.Wirt(test.Client);
        using var verwaltung = new Verwaltungsdienst(wirt, new VerwaltungsdienstTests.FluechtigerSpeicher(), uhr, TimeSpan.FromMilliseconds(1500));
        Pruefe.Wahr(await Warten(() =>
        {
            wirt.Ausloesen();
            return verwaltung.Flotte is { PhasenBekannt: true, LaufendePhase: 2 };
        }), "Flottenlage: Phasen bekannt, Entern läuft");

        // Wieder Admin: ein Klick, die Rechte kommen zurück.
        await faden.AusfuehrenAsync(async () => { await funker.WiederAdminCommand.ExecuteAsync(null); return true; });
        Pruefe.Wahr(await Warten(() => habicht.Lage.Konto is { AlsFunker: false, Einsatzleitung: true }), "wieder Admin");
    }
}
