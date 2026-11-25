// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Funk.Tests;

/// <summary>
/// funk-admin als Kommandozeile gegen einen Testserver: Ein Tippfehler ändert nichts und endet mit Exit 2. Anlass ist die
/// Gegenprüfung vom 01.10.2026: <c>scnamen --konto 1 --name "Sarevoc Test"</c> (statt <c>--namen</c>) löschte alle SC-Namen
/// des Kontos und meldete <c>SC-Namen:</c> mit Exit 0, weil funk-admin unbekannte Optionen still annahm und eine fehlende
/// Liste als leere schickte. Wegnehmen geht seither nur ausdrücklich mit <c>--leeren</c>.
/// </summary>
internal static class AdminWerkzeugTests
{
    [Test]
    public static async Task AdminWerkzeug_TippfehlerBeiScNamen_AendertNichts()
    {
        await using var t = await Testserver.StartenAsync();
        var s = t.Dienst.Verwaltungspfad;
        var (code, text) = await ServerPfadTests.Werkzeug("konto-anlegen", "--discord", "9001", "--benutzername", "test", "--name", "Test", "--socket", s);
        Pruefe.Gleich(0, code, "konto-anlegen: " + text);
        var konto = Regex.Match(text, @"Konto (\d+) angelegt", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        Pruefe.Wahr(konto.Length > 0, "Kontonummer: " + text);
        (code, text) = await ServerPfadTests.Werkzeug("scnamen", "--konto", konto, "--namen", "Sarevoc Test", "--socket", s);
        Pruefe.Gleich(0, code, "scnamen richtig geschrieben: " + text);
        Pruefe.Gleich("SC-Namen: Sarevoc Test", text.Trim(), "Antwort");

        async Task Abgelehnt(string was, params string[] argumente)
        {
            var (c, aus) = await ServerPfadTests.Werkzeug([.. argumente, "--socket", s]);
            Pruefe.Gleich(2, c, was + ": " + aus);
            Pruefe.Wahr(aus.Contains("Nichts geändert.", StringComparison.Ordinal), was + " sagt, dass nichts geschah: " + aus);
            var (_, liste) = await ServerPfadTests.Werkzeug("konten", "--socket", s);
            Pruefe.Wahr(liste.Contains("Sarevoc Test", StringComparison.Ordinal), was + ": SC-Name steht noch: " + liste);
        }

        await Abgelehnt("--name statt --namen (der Befund)", "scnamen", "--konto", konto, "--name", "Sarevoc Test");
        await Abgelehnt("--namen fehlt", "scnamen", "--konto", konto);
        await Abgelehnt("--namen leer", "scnamen", "--konto", konto, "--namen", "");
        await Abgelehnt("--namen nur Kommas", "scnamen", "--konto", konto, "--namen", " , ");
        await Abgelehnt("ohne Anführungszeichen", "scnamen", "--konto", konto, "--namen", "Sarevoc", "Test");
        await Abgelehnt("mit = statt Leerzeichen", "scnamen", "--konto", konto, "--namen=Sarevoc");
        await Abgelehnt("zweimal --namen", "scnamen", "--konto", konto, "--namen", "Sarevoc", "--namen", "Otto");
        await Abgelehnt("--namen und --leeren", "scnamen", "--konto", konto, "--namen", "Otto", "--leeren");
        await Abgelehnt("--namen ohne Wert vor --leeren", "scnamen", "--konto", konto, "--namen", "--leeren");
        await Abgelehnt("rechte ohne --rechte", "rechte", "--konto", konto);
        await Abgelehnt("rechte mit --recht", "rechte", "--konto", konto, "--recht", "ZUWEISEN");
        await Abgelehnt("discordname --name ohne Wert vor --leeren", "discordname", "--konto", konto, "--name", "--leeren");
        await Abgelehnt("unbekannter Schalter am Ende", "geraete", "--konto", konto, "--wartnd");

        // Wegnehmen nur ausdrücklich; danach zeigt konten den Anzeigenamen.
        (code, text) = await ServerPfadTests.Werkzeug("scnamen", "--konto", konto, "--leeren", "--socket", s);
        Pruefe.Gleich(0, code, "scnamen --leeren: " + text);
        Pruefe.Gleich("SC-Namen:", text.Trim(), "Antwort nach dem Leeren");
        (_, text) = await ServerPfadTests.Werkzeug("konten", "--socket", s);
        Pruefe.Falsch(text.Contains("Sarevoc", StringComparison.Ordinal), "weg nach --leeren: " + text);
        (code, text) = await ServerPfadTests.Werkzeug("rechte", "--konto", konto, "--leeren", "--socket", s);
        Pruefe.Gleich(0, code, "rechte --leeren: " + text);
    }

    /// <summary>Auch der Server nimmt eine leere Liste über den Socket nur mit <c>leeren</c> an (ältere oder fremde Aufrufer).</summary>
    [Test]
    public static async Task AdminWerkzeug_ServerNimmtLeereListeNurMitLeeren()
    {
        await using var t = await Testserver.StartenAsync();
        var angelegt = await t.AdminOk("konto-anlegen", new JsonObject { ["discord"] = "9002", ["benutzername"] = "otto", ["name"] = "Otto" });
        var konto = angelegt.Daten!["kontoId"]!.GetValue<uint>();
        await t.AdminOk("scnamen", new JsonObject { ["konto"] = konto, ["namen"] = new JsonArray("Sarevoc") });
        await t.AdminOk("rechte", new JsonObject { ["konto"] = konto, ["rechte"] = new JsonArray("ZUWEISEN") });

        var ohne = await t.Admin("scnamen", new JsonObject { ["konto"] = konto, ["namen"] = new JsonArray() });
        Pruefe.Falsch(ohne.Ok, "leere SC-Namen ohne leeren: " + ohne.Text);
        var r = await t.Admin("rechte", new JsonObject { ["konto"] = konto, ["rechte"] = new JsonArray() });
        Pruefe.Falsch(r.Ok, "leere Rechte ohne leeren: " + r.Text);
        var konten = await t.AdminOk("konten");
        Pruefe.Wahr(konten.Text.Contains("Sarevoc", StringComparison.Ordinal), "SC-Name steht noch: " + konten.Text);

        var mit = await t.AdminOk("scnamen", new JsonObject { ["konto"] = konto, ["namen"] = new JsonArray(), ["leeren"] = true });
        Pruefe.Gleich("SC-Namen: ", mit.Text, "mit leeren");
        await t.AdminOk("rechte", new JsonObject { ["konto"] = konto, ["rechte"] = new JsonArray(), ["leeren"] = true });
    }
}
