// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Server.Lage;
using static Funk.Tests.RaidleitungServerTests;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;
using Kanalsperre = Funk.Server.Lage.Kanalsperre;
using Mitgliedschaft = Funk.Server.Lage.Mitgliedschaft;
using Netz = Funk.Server.Lage.Netz;
using Praesenz = Funk.Protokoll.Steuerung.Praesenz;

namespace Funk.Tests;

/// <summary>
/// Unabhängige Gegenprüfung der Stufe 3 (Schritt 3.7, Fassung 0.6.0): Befunde je erst rot, dann grün, dazu Messungen.
/// Praesenz-Deltas (4.5): Ein Ch.-Wechsel, eine Rollenwahl und eine Kanalsperre ändern an einem Praesenz-Eintrag nur die Details, die
/// allein die Einsatzleitung bekommt. Bis 3.6 ging trotzdem jedem Funker ein Delta zu, bei einer Sperre für alle sogar eines mit allen
/// Betroffenen an alle (bei 100 Sitzungen einige MB je Sperre).
/// </summary>
internal static class Stufe37GegenpruefungTests
{
    private const string DiscordTest = "970000000000000001";
    private const string DiscordOtto = "970000000000000002";
    private const string DiscordGina = "970000000000000003";
    private const string DiscordFritz = "970000000000000004";

    private static ushort K(ushort netz, byte ch) => Kanalrechnung.Id(netz, ch);

    private static List<Praesenz> PraesenzVon(KernFunker f) => [.. f.Pushes.Where(p => p.Body is Praesenz).Select(p => (Praesenz)p.Body)];

    private static int Bytes(IEnumerable<Praesenz> liste) => liste.Sum(p => SteuerJson.Serialisieren(p).Length);

    private static int Eintraege(IEnumerable<Praesenz> liste) => liste.Sum(p => p.Eintraege.Count);

    [Test]
    public static async Task Gp_ChWechselUndRolle_PraesenzDeltaNurAnDieEinsatzleitung()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(Stufe3VersionstorTests.KanalBrueckeJson) });
        await t.AdminOk("rolle-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":7,"name":"Brücke","reihenfolge":1,"selbstWaehlbar":true,"brauchtSchiff":false,"felder":[{"feld":0,"netzId":100,"senden":true,"hoeren":true,"sendenStandard":true,"fest":false}]}"""),
        });
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(171));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(172));
        await using var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(173));
        Pruefe.Wahr((await Gedrosselt(() => otto.Client.FeldSetzenAsync(0, K(100, 1)))).Ok, "Otto in Schiff 1");
        await Task.Delay(500);
        test.PushesLeeren();
        gina.PushesLeeren();

        for (byte ch = 2; ch <= 5; ch++)
            Pruefe.Wahr((await Gedrosselt(() => otto.Client.FeldSetzenAsync(0, K(100, ch)))).Ok, "Ch.-Wechsel auf " + ch.ToString(CultureInfo.InvariantCulture));
        Pruefe.Wahr((await Gedrosselt(() => otto.Client.RolleWaehlenAsync(7))).Ok, "Otto wählt die Rolle Brücke");
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Praesenz.TryGetValue(otto.KontoId, out var e) && e.Rolle == 7),
            "die Einsatzleitung sieht Ottos Rolle in den Details");
        await Task.Delay(500);

        var anJu = PraesenzVon(test);
        var anGina = PraesenzVon(gina);
        Console.WriteLine($"MESSUNG Gegenprüfung 3.7, 4 Ch.-Wechsel und 1 Rollenwahl: an die Einsatzleitung {anJu.Count} Praesenz-Deltas zu {Bytes(anJu)} Byte, "
            + $"an einen Funker {anGina.Count} zu {Bytes(anGina)} Byte");
        Pruefe.Wahr(anJu.Count >= 1 && anJu.All(p => p.Eintraege.All(e => e.KontoId == otto.KontoId)), "die Einsatzleitung bekommt Ottos Details");
        Pruefe.Gleich(0, anGina.Count, "ein Funker sieht an Ch.-Wechsel und Rollenwahl nichts (ohne Details): kein Praesenz-Delta an ihn");
        Pruefe.Wahr(test.Enden.IsEmpty && otto.Enden.IsEmpty && gina.Enden.IsEmpty, "niemand getrennt");
    }

    [Test]
    public static async Task Gp_SperreFuerAlle_MitgliedschaftenStattPraesenzAnAlle()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(Stufe3VersionstorTests.KanalBrueckeJson) });
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, DiscordTest, "test", "ADMIN", Lokal(181));
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(182));
        await using var gina = await KernFunker.AktivAsync(halter, DiscordGina, "gina", "FUNKER", Lokal(183));
        await using var fritz = await KernFunker.AktivAsync(halter, DiscordFritz, "fritz", "FUNKER", Lokal(184));
        Pruefe.Wahr((await Gedrosselt(() => otto.Client.FeldSetzenAsync(0, K(100, 3)))).Ok, "Otto in Schiff 3");
        Pruefe.Wahr((await Gedrosselt(() => gina.Client.FeldSetzenAsync(0, K(100, 1)))).Ok, "Gina in Schiff 1");
        await Task.Delay(500);
        foreach (var f in new[] { test, otto, gina, fritz }) f.PushesLeeren();

        var a = await test.Client.KanalSperreAsync(KanalSperreOperation.Setzen, 0, K(100, 3));
        Pruefe.Wahr(a.Ok, "Schiff 3 für alle gesperrt: " + a.Fehler?.Code);
        foreach (var f in new[] { otto, gina, fritz })
            Pruefe.Wahr(await Warten(() => f.Client.Lage.Mitgliedschaften?.Gesperrt is [{ Alle: true }]), "Mitgliedschaften mit der Sperre für alle an jeden Betroffenen");
        await Task.Delay(500);
        var anJu = PraesenzVon(test);
        var anFunker = PraesenzVon(gina).Concat(PraesenzVon(fritz)).Concat(PraesenzVon(otto)).ToList();
        Console.WriteLine($"MESSUNG Gegenprüfung 3.7, Sperre für alle bei 4 Sitzungen: an die Einsatzleitung {anJu.Count} Praesenz mit {Eintraege(anJu)} Einträgen zu {Bytes(anJu)} Byte, "
            + $"an drei Funker zusammen {anFunker.Count} mit {Eintraege(anFunker)} Einträgen zu {Bytes(anFunker)} Byte");
        Pruefe.Gleich(0, anFunker.Count, "an Funker kein Praesenz-Delta: an ihren Einträgen ohne Details ändert eine Sperre nichts");
        Pruefe.Falsch(anJu.Any(p => p.Eintraege.Any(e => e.KontoId == gina.KontoId || e.KontoId == fritz.KontoId)),
            "an die Einsatzleitung keine Einträge, deren Details gleich bleiben (Gina und Fritz senden nicht in Schiff 3)");

        var weg = await test.Client.KanalSperreAsync(KanalSperreOperation.Aufheben, 0, K(100, 3));
        Pruefe.Wahr(weg.Ok, "aufgehoben");
        foreach (var f in new[] { otto, gina, fritz })
            Pruefe.Wahr(await Warten(() => f.Client.Lage.Mitgliedschaften?.Gesperrt is null), "Mitgliedschaften ohne Sperre an jeden");
        await Task.Delay(300);
        Pruefe.Gleich(0, PraesenzVon(gina).Count + PraesenzVon(fritz).Count, "auch das Aufheben schickt Funkern kein Praesenz-Delta");
    }

    /// <summary>
    /// "Für alle gesperrt" am Brett über den echten Dienst (Gegenprüfung 3.7): <see cref="Funk.Client.Dienste.FunkClientDienst.SperrenFuerAlleAsync"/>
    /// liest LISTE und nennt nur die Sperren für alle, nicht die einer Person; ein Funker bekommt "Dafür fehlt das Recht."
    /// </summary>
    [Test]
    public static async Task Gp_Dienst_SperrenFuerAlle_ListeVomServer()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(Stufe3VersionstorTests.KanalBrueckeJson) });
        var halter = new ServerHalter { Server = t };
        using var idJu = Geraeteidentitaet.Fluechtig();
        using var idOtto = Geraeteidentitaet.Fluechtig();
        await using var test = await Stufe36DrahtTests.DienstAsync(t, halter, idJu, DiscordTest, "test", "ADMIN", 191);
        await using var otto = await Stufe36DrahtTests.DienstAsync(t, halter, idOtto, DiscordOtto, "otto", "FUNKER", 192);
        var kontoOtto = otto.Lage.Konto!.Id;
        Pruefe.Gleich<string?>(null, await test.KanalSperreAsync(0, K(100, 3), sperren: true), "Schiff 3 für alle");
        Pruefe.Gleich<string?>(null, await test.KanalSperreAsync(kontoOtto, K(100, 4), sperren: true), "Schiff 4 für Otto");
        var liste = await test.SperrenFuerAlleAsync();
        Pruefe.Wahr(liste.Ok && liste.Wert is [{ Alle: true } s] && s.NetzId == K(100, 3) && s.Durch == test.Lage.Konto!.Id,
            "nur die Sperre für alle: " + string.Join(",", liste.Wert?.Select(x => x.NetzId) ?? []));
        var funker = await otto.SperrenFuerAlleAsync();
        Pruefe.Wahr(!funker.Ok && funker.Fehler == "Dafür fehlt das Recht.", "ein Funker liest keine Liste: " + funker.Fehler);
        Pruefe.Gleich<string?>(null, await test.KanalSperreAsync(0, K(100, 3), sperren: false), "für alle aufgehoben");
        Pruefe.Wahr((await test.SperrenFuerAlleAsync()).Wert is { Count: 0 }, "Liste leer");
    }

    /// <summary>
    /// Konsole (Gegenprüfung 3.7, Kreuzprobe): Neben der Kanalvorlage heißen drei Kanäle "Kommando" (eigenes Netz, alte Flotte, Vorlage).
    /// <c>kanalsperre-setzen --netz Kommando</c> sperrte bis 3.6 ohne Warnung den mit der kleinsten Nummer, und die Antwort nannte nur
    /// "Kommando": Wer den Kanal der Vorlage meinte, glaubte ihn gesperrt. Bei mehreren gleichen Namen nur mit der Nummer.
    /// </summary>
    [Test]
    public static async Task Gp_Konsole_Kanalsperre_GleicherNameMehrmals_NurMitNummer()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":2,"name":"Kommando","typ":"NORMAL","reihenfolge":1,"nurTaste":true,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false}"""),
        });
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = JsonNode.Parse("""{"id":151,"name":"Kommando","typ":"NORMAL","reihenfolge":11,"nurTaste":true,"ausloeserStandard":"TASTE","offen":"HOEREN_SENDEN","standard":false,"kanaele":["Ruhe","Kommando 1","Kommando 2"]}"""),
        });
        var mehrdeutig = await t.Admin("kanalsperre-setzen", new JsonObject { ["alle"] = true, ["netz"] = "Kommando" });
        Pruefe.Falsch(mehrdeutig.Ok, "zwei Kanäle 'Kommando': ohne Nummer keine Sperre: " + mehrdeutig.Text);
        Pruefe.Wahr(mehrdeutig.Text.Contains("2, 151", StringComparison.Ordinal) && mehrdeutig.Text.Contains("Nichts geändert", StringComparison.Ordinal),
            "die Antwort nennt beide Nummern: " + mehrdeutig.Text);
        Pruefe.Gleich("Keine Kanalsperren", (await t.AdminOk("kanalsperren")).Text, "nichts gesperrt");
        var mitCh = await t.Admin("kanalsperre-setzen", new JsonObject { ["alle"] = true, ["netz"] = "command", ["ch"] = 1 });
        Pruefe.Falsch(mitCh.Ok, "auch mit --ch nicht über den Namen: " + mitCh.Text);

        var mitNummer = await t.AdminOk("kanalsperre-setzen", new JsonObject { ["alle"] = true, ["netz"] = "151", ["ch"] = 1 });
        Pruefe.Wahr(mitNummer.Text.StartsWith("Kommando Ch. 1 (Kommando 1) für alle", StringComparison.Ordinal), "mit Nummer und Ch.: " + mitNummer.Text);
        Pruefe.Wahr((await t.AdminOk("kanalsperre-aufheben", new JsonObject { ["alle"] = true, ["netz"] = "151", ["ch"] = 1 })).Ok, "aufheben mit Nummer");
        await t.AdminOk("netz-loeschen", new JsonObject { ["id"] = 2 });
        Pruefe.Wahr((await t.AdminOk("kanalsperre-setzen", new JsonObject { ["alle"] = true, ["netz"] = "Kommando" })).Text.StartsWith("Kommando (alle Ch.)", StringComparison.Ordinal),
            "ein eindeutiger Name geht weiter");
    }

    /// <summary>
    /// Weiterleitung je Paket mit ausgeschöpften Kanalsperren (64 für alle, dazu 27 Konten mit je 16, zusammen 496 von höchstens 500,
    /// 6.17): Der Heißpfad fragt sie für jeden Hörer und jedes Ziel (<see cref="Schnappschuss.GesperrtAn"/>). Vergleich gegen denselben
    /// Stand ohne Sperren; Grenze weit (2 ms im Median).
    /// </summary>
    [Test]
    public static void Messung_Weiterleitung_MitAllenKanalsperren()
    {
        static Schnappschuss Bauen(bool mitSperren)
        {
            var b = new SchnappschussBauer().MitNetz(Netz.Global());
            for (ushort n = 100; n < 130; n++)
                b.MitNetz(new Netz(n, "Kanal " + n.ToString(CultureInfo.InvariantCulture)) { Kanaele = [.. Enumerable.Range(0, 31).Select(i => i == 0 ? "Ruhe" : "Ch " + i.ToString(CultureInfo.InvariantCulture))], Offen = Offenheit.HoerenSenden });
            for (uint k = 0; k < 100; k++)
            {
                var felder = Enumerable.Range(0, 9).Select(f => new Mitgliedschaft((byte)f, K((ushort)(100 + (k * 9 + (uint)f) / 30), (byte)(1 + (k * 9 + (uint)f) % 30)), true, true, false))
                    .Append(new Mitgliedschaft(9, K(129, 30), true, true, false));
                // 16 eigene Sperren auf Kanäle, die das Konto nicht hält (keine Wirkung, aber jede wird gefragt).
                var eigene = mitSperren && k < 27 ? Enumerable.Range(0, 16).Select(i => new Kanalsperre(K(128, (byte)(1 + i)), 1_900_000_000_000, 1)).ToArray() : [];
                b.MitKonto(new Konto { Id = k + 1, Name = "S" + k.ToString(CultureInfo.InvariantCulture), Felder = [.. felder.DistinctBy(m => m.NetzId)], Sperren = [.. eigene] })
                    .MitSitzung(k + 60_000, k + 1, k + 1);
            }
            if (mitSperren)
                for (var i = 0; i < 64; i++) b.MitSperreFuerAlle(new Kanalsperre(K((ushort)(100 + i / 30), (byte)(1 + i % 30)), 1_900_000_000_000, 1) { Seit = 1 });
            return b.Bauen();
        }

        double Median(Schnappschuss z)
        {
            var relay = new Weiterleiter();
            var ziel = new Mitschrift();
            ushort[] gemeinsam = [K(129, 30)];
            uint frame = 100;
            var zeiten = new List<double>(500);
            for (var i = 0; i < 500; i++)
            {
                ziel.Leeren();
                var paket = new Sprachpaket(60_000, frame, 1, Paketkennzeichen.Keine, Sendeart.Netz, gemeinsam, Funkprobe.Opus);
                var uhr = Stopwatch.StartNew();
                relay.Verarbeiten(z, paket, 1_790_000_000_000 + frame++ * 20L, ziel);
                zeiten.Add(uhr.Elapsed.TotalMilliseconds);
            }
            Pruefe.Gleich(99, ziel.Sprache.Count, "99 Hörer im gemeinsamen Kanal");
            zeiten.Sort();
            return zeiten[zeiten.Count / 2];
        }

        var ohne = Median(Bauen(false));
        var mit = Median(Bauen(true));
        Console.WriteLine($"MESSUNG Gegenprüfung 3.7, Weiterleitung an 99 Hörer: ohne Sperren Median {ohne.ToString("F3", CultureInfo.InvariantCulture)} ms, "
            + $"mit 64 Sperren für alle und 16 je Konto {mit.ToString("F3", CultureInfo.InvariantCulture)} ms");
        Pruefe.Wahr(mit < 2, $"Weiterleitung mit Sperren {mit} ms im Median");
    }
}
