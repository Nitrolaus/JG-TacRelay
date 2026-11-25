// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using Funk.Admin;
using Funk.Protokoll.Steuerung;
using Funk.Tests.Oberflaeche;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Konsole mit Kanälen (Fassung 0.6.0, Schritt 3.3): <c>netz-setzen --kanaele</c> und <c>--kanaele-datei</c> (die erste Bezeichnung ist die
/// Ruhe), <c>--gruppe</c>, und <c>flotte-vorlage --name starcitizen</c> am laufenden Server: 24 Schritte neben einer vorhandenen Flotte,
/// die Ausgabe kündigt das Versionstor an und nennt die Annahmen, ein zweiter Lauf ändert nichts.
/// </summary>
internal static class Stufe3KonsoleTests
{
    private static (string Befehl, JsonObject Argumente) U(params string[] a) => Uebersetzung.Uebersetzen(new Befehlszeile(a, Befehlsoptionen.Schalter));

    [Test]
    public static void Konsole_NetzSetzen_KanaeleAusZeileUndDatei_RuheZuerst()
    {
        var (_, a) = U("netz-setzen", "--id", "120", "--name", "Brücke", "--offen", "HOEREN_SENDEN", "--gruppe", "Schiffe", "--kanaele", " Ruhe, Schiff 1 ,Schiff 2");
        var netz = (JsonObject)a["objekt"]!;
        Pruefe.Gleich("[\"Ruhe\",\"Schiff 1\",\"Schiff 2\"]", netz["kanaele"]!.ToJsonString(), "Ch.-Liste getrimmt");
        Pruefe.Wahr((string)netz["gruppe"]! == "Schiffe" && (string)netz["ausloeserStandard"]! == "VA", "Gruppe, ohne Schalter Sprachaktivierung");
        Pruefe.Gleich(null, Befehlsoptionen.Pruefen(new Befehlszeile(["netz-setzen", "--id", "120", "--name", "X", "--kanaele", "Ruhe,A", "--gruppe", "G"], Befehlsoptionen.Schalter)),
            "--kanaele und --gruppe stehen in der Tabelle");

        using var ordner = new Testordner();
        var datei = Path.Combine(ordner.Pfad, "jaeger.txt");
        File.WriteAllLines(datei, ["# Jäger mit Staffeln", "Ruhe", "", "Staffel 1-Alpha", "  Staffel 1-Bravo  "]);
        (_, a) = U("netz-setzen", "--id", "121", "--name", "Jäger", "--kanaele-datei", datei);
        Pruefe.Gleich("[\"Ruhe\",\"Staffel 1-Alpha\",\"Staffel 1-Bravo\"]", ((JsonObject)a["objekt"]!)["kanaele"]!.ToJsonString(), "aus der Datei, ohne leere Zeilen und Kommentare");

        foreach (var (was, args, text) in new[]
        {
            ("ohne Ruhe", new[] { "netz-setzen", "--id", "120", "--name", "X", "--kanaele", "Schiff 1,Schiff 2" },
                "Die Ch.-Liste beginnt mit Ruhe (Ch. 0) und hat mindestens einen Ch., etwa --kanaele \"Ruhe,Schiff 1,Schiff 2\". Nichts geändert."),
            ("nur Ruhe", ["netz-setzen", "--id", "120", "--name", "X", "--kanaele", "Ruhe"],
                "Die Ch.-Liste beginnt mit Ruhe (Ch. 0) und hat mindestens einen Ch., etwa --kanaele \"Ruhe,Schiff 1,Schiff 2\". Nichts geändert."),
            ("leere Bezeichnung", ["netz-setzen", "--id", "120", "--name", "X", "--kanaele", "Ruhe,,A"],
                "--kanaele hat eine leere Bezeichnung (zwei Kommas hintereinander?). Nichts geändert."),
            ("beides", ["netz-setzen", "--id", "120", "--name", "X", "--kanaele", "Ruhe,A", "--kanaele-datei", datei],
                "--kanaele und --kanaele-datei zusammen geht nicht. Nichts geändert."),
            ("zu viele", ["netz-setzen", "--id", "120", "--name", "X", "--kanaele", "Ruhe," + string.Join(",", Enumerable.Range(1, 31).Select(i => "C" + i))],
                "Höchstens 30 Ch. nach der Ruhe, hier sind es 31. Nichts geändert."),
        })
        {
            var e = Pruefe.Wirft<ArgumentException>(() => U(args), was);
            Pruefe.Gleich(text, e.Message, was);
        }
    }

    [Test]
    public static async Task Konsole_NetzSetzenMitKanaelen_UndFlotteVorlageStarCitizen_AmServer()
    {
        await using var t = await Testserver.StartenAsync();
        await Testflotte.AnDieKonsoleAsync(t);
        var (befehl, a) = U("netz-setzen", "--id", "90", "--name", "Echo", "--offen", "HOEREN_SENDEN", "--kanaele", "Ruhe,Echo 1,Echo 2");
        await t.AdminOk(befehl, a);
        var stand = await t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Aktuell);
        Pruefe.Wahr(stand.Netze[90].Kanaele is ["Ruhe", "Echo 1", "Echo 2"], "Kanalnetz über die Konsole");
        var zweitesKanalnetz = await t.Admin("flotte-vorlage", new JsonObject { ["name"] = "starcitizen" });
        Pruefe.Wahr(!zweitesKanalnetz.Ok && zweitesKanalnetz.Text.StartsWith("Es gibt schon Kanalnetze", StringComparison.Ordinal), "nicht neben vorhandene Kanalnetze: " + zweitesKanalnetz.Text);
        await t.AdminOk("netz-loeschen", new JsonObject { ["id"] = 90 });

        var r = await t.AdminOk("flotte-vorlage", new JsonObject { ["name"] = "starcitizen" });
        Pruefe.Wahr(r.Text.StartsWith("Star Citizen übernommen: 24 Schritte.", StringComparison.Ordinal) && r.Text.Contains("Clients ab 0.6.0", StringComparison.Ordinal)
            && r.Text.Contains("Ch. Alle am Ende", StringComparison.Ordinal), "Ausgabe mit Versionstor und Annahmen: " + r.Text);
        stand = await t.Dienst.Zentrale.Ausfuehren(() => t.Dienst.Zentrale.Schreiber.Aktuell);
        Pruefe.Gleich(10, L.Kanaele.Anzahl(stand.Netze), "zehn Kanalnetze, Global ohne Ch.");
        Pruefe.Wahr(stand.Rollen.Values.Count(x => x.Name == "Kapitän") == 2 && stand.Schiffe.Count == Testflotte.Schiffe,
            "neben der Schiffsflotte: beide Kapitän-Rollen, die 15 Schiffe bleiben");
        var zweiter = await t.Admin("flotte-vorlage");
        Pruefe.Wahr(!zweiter.Ok && zweiter.Text.EndsWith("Nichts geändert.", StringComparison.Ordinal), "kein zweiter Lauf, auch ohne --name: " + zweiter.Text);
        var journal = (await t.AdminOk("journal", new JsonObject { ["anzahl"] = 5 })).Text;
        Pruefe.Wahr(journal.Contains("Flottenvorlage Star Citizen übernommen", StringComparison.Ordinal), "im Journal: " + journal);
        var unbekannt = await t.Admin("flotte-vorlage", new JsonObject { ["name"] = "beispiel" });
        Pruefe.Gleich("Vorlagen: starcitizen. Nichts geändert.", unbekannt.Text, "unbekannte Vorlage");
    }
}
