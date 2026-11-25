// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json.Nodes;
using Funk.Admin;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.RaidleitungServerTests;

namespace Funk.Tests;

/// <summary>
/// Kanalsperren an der Konsole (Fassung 0.6.0, Schritt 3.5, FUNKPROTOKOLL 6.17): <c>kanalsperre-setzen</c>, <c>kanalsperre-aufheben</c> und
/// <c>kanalsperren</c> in funk-admin. Die Übersetzung (genau eins von --konto und --alle, --netz, --ch, --bis), die Tabelle der Optionen
/// und am laufenden Server: Sperre für eine Person und für alle, Sichtbarkeit am Funker (Urheber 0 heißt Konsole), Liste, Journal, kein
/// Ende nach einer Sekunde (die Konsole hat keine Einsatzleitung, die enden könnte), Aufheben auch einer fremden Sperre, Fehler mit Grund.
/// </summary>
internal static class Stufe35KonsoleTests
{
    private const string DiscordOtto = "960000000000000012";

    private static (string Befehl, JsonObject Argumente) U(params string[] a) => Uebersetzung.Uebersetzen(new Befehlszeile(a, Befehlsoptionen.Schalter));

    private static string? Pruefen(params string[] a) => Befehlsoptionen.Pruefen(new Befehlszeile(a, Befehlsoptionen.Schalter));

    private static ushort K(ushort netz, byte ch) => Kanalrechnung.Id(netz, ch);

    [Test]
    public static void Konsole_Kanalsperren_UebersetzenUndPruefen()
    {
        var (befehl, a) = U("kanalsperre-setzen", "--konto", "5", "--netz", "Crew", "--ch", "3", "--bis", "23:30");
        Pruefe.Gleich("kanalsperre-setzen", befehl, "Befehl");
        Pruefe.Gleich("""{"konto":5,"netz":"Crew","ch":3,"bis":"23:30"}""", a.ToJsonString(), "für eine Person mit Ch. und Ende");
        (_, a) = U("kanalsperre-setzen", "--alle", "--netz", "101");
        Pruefe.Gleich("""{"netz":"101","alle":true}""", a.ToJsonString(), "für alle, ganzes Netz, ohne Ende");
        (befehl, a) = U("kanalsperre-aufheben", "--konto", "5", "--netz", "100", "--ch", "3");
        Pruefe.Gleich("""{"konto":5,"netz":"100","ch":3}""", a.ToJsonString(), "aufheben");
        (befehl, a) = U("kanalsperren");
        Pruefe.Gleich("kanalsperren", befehl, "die Liste braucht nichts");

        Pruefe.Gleich("Für wen? --konto <id> oder --alle. Nichts geändert.", Pruefe.Wirft<ArgumentException>(() => U("kanalsperre-setzen", "--netz", "100"), "ohne Ziel").Message, "ohne --konto und ohne --alle");
        Pruefe.Gleich("--konto und --alle zusammen geht nicht. Nichts geändert.", Pruefe.Wirft<ArgumentException>(() => U("kanalsperre-setzen", "--konto", "5", "--alle", "--netz", "100"), "beides").Message, "beides");
        Pruefe.Gleich("--netz fehlt (Nummer oder Name, siehe netzliste). Nichts geändert.", Pruefe.Wirft<ArgumentException>(() => U("kanalsperre-aufheben", "--alle"), "ohne Netz").Message, "ohne --netz");

        Pruefe.Gleich(null, Pruefen("kanalsperre-setzen", "--alle", "--netz", "100", "--ch", "3", "--bis", "23:30"), "die Tabelle kennt alle Optionen");
        Pruefe.Gleich(null, Pruefen("kanalsperre-aufheben", "--konto", "5", "--netz", "100", "--json-aus"), "aufheben mit JSON");
        Pruefe.Gleich("kanalsperre-aufheben kennt --bis nicht. Erlaubt: --konto, --alle, --netz, --ch (dazu --konfig oder --socket, --json-aus). Nichts geändert.",
            Pruefen("kanalsperre-aufheben", "--konto", "5", "--netz", "100", "--bis", "23:30"), "--bis gehört nur zum Setzen");
        Pruefe.Wahr(Pruefen("kanalsperren", "--konto", "5") is { } m && m.Contains("kanalsperren braucht keine Optionen", StringComparison.Ordinal), "die Liste nimmt keine Option");
    }

    [Test]
    public static async Task Konsole_Kanalsperren_AmServer_SetzenListeAufheben()
    {
        await using var t = await Testserver.StartenAsync();
        await t.AdminOk("netz-setzen", new JsonObject { ["objekt"] = JsonNode.Parse(Stufe3VersionstorTests.KanalBrueckeJson) });
        var (b0, a0) = U("netz-setzen", "--id", "101", "--name", "Kommando", "--offen", "HOEREN_SENDEN", "--taste");
        await t.AdminOk(b0, a0);
        var halter = new ServerHalter { Server = t };
        await using var otto = await KernFunker.AktivAsync(halter, DiscordOtto, "otto", "FUNKER", Lokal(233));
        var schiff3 = K(100, 3);
        Pruefe.Wahr((await Gedrosselt(() => otto.Client.FeldSetzenAsync(0, schiff3))).Ok, "Otto in Brücke, Schiff 3");
        var konto = otto.KontoId.ToString(CultureInfo.InvariantCulture);

        // für eine Person, Kanal nach Name ohne Rücksicht auf Groß und Klein, mit Ch.
        var (b, a) = U("kanalsperre-setzen", "--konto", konto, "--netz", "brücke", "--ch", "3");
        var r = await t.AdminOk(b, a);
        Pruefe.Wahr(r.Text.StartsWith("Brücke Ch. 3 (Schiff 3) für ", StringComparison.Ordinal) && r.Text.Contains("gesperrt bis ", StringComparison.Ordinal)
            && r.Text.EndsWith("04:00.", StringComparison.Ordinal), "Ausgabe mit Kanal, Person und Ende 04:00: " + r.Text);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften?.Gesperrt is [{ NetzId: var n, Durch: 0, Alle: null }] && n == schiff3),
            "Otto sieht die Sperre, Urheber 0 ist die Konsole");
        var wechsel = await Gedrosselt(() => otto.Client.FeldSetzenAsync(0, K(100, 4)));
        Pruefe.Gleich(Fehlercode.KeinRecht, wechsel.Code, "kein Ch.-Wechsel im gesperrten Feld");
        Pruefe.Wahr(wechsel.Fehler!.Details!.Value.GetProperty("durch").GetUInt32() == 0, "die Ablehnung nennt die Konsole (0)");

        // eine Konsolensperre endet nicht nach einer Sekunde (die Konsole hat keine Einsatzleitung, die enden könnte)
        await Task.Delay(2200);
        Pruefe.Wahr(otto.Client.Lage.Mitgliedschaften?.Gesperrt is [{ NetzId: var n2 }] && n2 == schiff3, "nach zwei Sekunden noch gesperrt");
        var liste = (await t.AdminOk("kanalsperren", new JsonObject())).Text;
        Pruefe.Wahr(liste.Contains("Brücke Ch. 3 (Schiff 3) für ", StringComparison.Ordinal) && liste.Contains(", von Konsole", StringComparison.Ordinal) && liste.Contains("otto", StringComparison.OrdinalIgnoreCase),
            "Liste mit Kanal, Person, Ende und Urheber: " + liste);
        var journal = (await t.AdminOk("journal", new JsonObject { ["anzahl"] = 20 })).Text;
        Pruefe.Wahr(journal.Contains("KANAL_GESPERRT", StringComparison.Ordinal) && journal.Contains("durch Konsole", StringComparison.Ordinal), "im Journal durch Konsole: " + journal);

        // für alle (ganzes Netz, Nummer); die Sperre für die Person bleibt daneben
        (b, a) = U("kanalsperre-setzen", "--alle", "--netz", "101", "--bis", "23:55");
        r = await t.AdminOk(b, a);
        Pruefe.Wahr(r.Text.StartsWith("Kommando für alle ohne Einsatzleitung gesperrt bis ", StringComparison.Ordinal), "für alle: " + r.Text);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften?.Gesperrt is { Count: 2 } g && g.Any(s => s.NetzId == 101 && s.Alle == true)), "Otto sieht auch die Sperre für alle");

        // Fehler mit Grund, nichts geändert
        foreach (var (was, args, teil) in new[]
        {
            ("unbekanntes Netz", new[] { "kanalsperre-setzen", "--alle", "--netz", "gibtsnicht" }, "Den Kanal 'gibtsnicht' gibt es nicht"),
            ("Ch. an einem gewöhnlichen Netz", ["kanalsperre-setzen", "--alle", "--netz", "101", "--ch", "2"], "Kommando hat keine Ch."),
            ("Ch. zu hoch", ["kanalsperre-setzen", "--alle", "--netz", "Brücke", "--ch", "99"], "Brücke hat nur Ch. 0 bis "),
            ("Global", ["kanalsperre-setzen", "--alle", "--netz", "1"], "Global (auch dessen Ch. 1) und eine Ruhe lassen sich nicht sperren"),
            ("unbekanntes Konto", ["kanalsperre-setzen", "--konto", "99999", "--netz", "101"], "Das Konto 99999 gibt es nicht"),
            ("Ende in der Vergangenheit oder ohne Sinn", ["kanalsperre-setzen", "--alle", "--netz", "101", "--bis", "gestern"], "--bis: HH:MM oder JJJJ-MM-TT HH:MM"),
        })
        {
            (b, a) = U(args);
            var f = await t.Admin(b, a);
            Pruefe.Wahr(!f.Ok && f.Text.Contains(teil, StringComparison.Ordinal) && f.Text.EndsWith("Nichts geändert.", StringComparison.Ordinal), was + ": " + f.Text);
        }

        // aufheben: die Person, dann alle; was nicht liegt, ändert nichts und sagt es
        (b, a) = U("kanalsperre-aufheben", "--konto", konto, "--netz", "Brücke", "--ch", "3");
        r = await t.AdminOk(b, a);
        Pruefe.Wahr(r.Text.StartsWith("Brücke Ch. 3 (Schiff 3) für ", StringComparison.Ordinal) && r.Text.EndsWith("wieder frei.", StringComparison.Ordinal), "aufgehoben: " + r.Text);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften?.Gesperrt is [{ NetzId: 101 }]), "nur die Sperre für alle bleibt");
        (b, a) = U("kanalsperre-aufheben", "--konto", konto, "--netz", "Brücke", "--ch", "3");
        var nochmal = await t.Admin(b, a);
        Pruefe.Wahr(!nochmal.Ok && nochmal.Text.Contains("liegt für ", StringComparison.Ordinal) && nochmal.Text.EndsWith("keine Sperre. Nichts geändert.", StringComparison.Ordinal), "ein zweites Mal: " + nochmal.Text);
        (b, a) = U("kanalsperre-aufheben", "--alle", "--netz", "101");
        await t.AdminOk(b, a);
        Pruefe.Wahr(await Warten(() => otto.Client.Lage.Mitgliedschaften?.Gesperrt is null), "alles frei");
        Pruefe.Gleich("Keine Kanalsperren", (await t.AdminOk("kanalsperren", new JsonObject())).Text, "die Liste ist leer");
        Pruefe.Wahr((await t.AdminOk("journal", new JsonObject { ["anzahl"] = 30 })).Text.Contains("KANAL_FREI", StringComparison.Ordinal), "Journal KANAL_FREI");
    }
}
