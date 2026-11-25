// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Nodes;
using Funk.Admin;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Die Konsole nach der Übernahme der Vorlage Star Citizen (Fassung 0.6.0, Schritt 3.6, HEIMSERVER-PROXMOX Abschnitt 18): <c>netzliste</c>
/// nennt jeden Kanal und bei einem Kanalnetz seine Ch. ("Ch. 0 bis 13"), <c>status</c> zählt Kanäle. Der Rückweg aus der Anleitung, mit
/// denselben Befehlen wie dort: die Kanalnetze aus <c>netzliste</c> herauslesen (wie das awk dort), jedes mit <c>netz-loeschen</c> löschen;
/// Global hat die Vorlage nicht geändert, es bleibt ohne Ch. Danach fällt das Versionstor, und ein Client 0.5.x kommt wieder hinein. Echo
/// und die alte Flotte bleiben.
/// </summary>
internal static class Stufe36KonsoleTests
{
    private static (string Befehl, JsonObject Argumente) U(params string[] a) => Uebersetzung.Uebersetzen(new Befehlszeile(a, Befehlsoptionen.Schalter));

    /// <summary>Wie in der Anleitung: <c>awk '$1 == "Kanal" &amp;&amp; $2 != 1 &amp;&amp; / Ch\. 0 bis / {print $2}'</c>.</summary>
    private static List<string> Kanalnetze(string netzliste) =>
        [.. netzliste.Split('\n').Where(z => z.Contains(" Ch. 0 bis ", StringComparison.Ordinal))
            .Select(z => z.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(f => f.Length > 1 && f[0] == "Kanal" && f[1] != "1").Select(f => f[1])];

    [Test]
    public static async Task Konsole_NetzlisteMitCh_UndRueckwegNachStarCitizen()
    {
        await using var t = await Testserver.StartenAsync();
        var (be, ae) = U("netz-setzen", "--id", "90", "--name", "Echo", "--offen", "HOEREN_SENDEN", "--va");
        await t.AdminOk(be, ae);
        var (bc, ac) = U("netz-setzen", "--id", "2", "--name", "Kommando", "--offen", "HOEREN_SENDEN", "--standard", "--taste");
        await t.AdminOk(bc, ac);
        var (bv, av) = U("flotte-vorlage", "--name", "starcitizen");
        await t.AdminOk(bv, av);
        Pruefe.Gleich(Fehlercode.VersionZuAlt, (await Stufe3VersionstorTests.HelloAsync(t, "0.5.1"))?.Code, "nach der Vorlage gilt das Versionstor");

        var liste = (await t.AdminOk("netzliste")).Text;
        var zeilen = liste.Split('\n');
        Pruefe.Wahr(zeilen.Where(z => z.Length > 0 && !z.StartsWith("Rolle", StringComparison.Ordinal) && !z.StartsWith("Schiff", StringComparison.Ordinal)
            && !z.StartsWith("Version", StringComparison.Ordinal)).All(z => z.StartsWith("Kanal ", StringComparison.Ordinal)), "jede Netzzeile heißt Kanal:\n" + liste);
        Pruefe.Wahr(zeilen.Any(z => z.Contains("Echo", StringComparison.Ordinal) && !z.Contains(" Ch. ", StringComparison.Ordinal)), "Echo ohne Ch.");
        Pruefe.Wahr(zeilen.Any(z => z.StartsWith("Kanal     1  Global", StringComparison.Ordinal) && !z.Contains(" Ch. ", StringComparison.Ordinal)), "Global ohne Ch.");
        Pruefe.Wahr(zeilen.Any(z => z.Contains("Brücke", StringComparison.Ordinal) && z.EndsWith("Ch. 0 bis 13", StringComparison.Ordinal)), "Brücke mit Ruhe, Schiff 1 bis 12 und Alle");
        Pruefe.Wahr((await t.AdminOk("status")).Text.Contains(" Kanäle, ", StringComparison.Ordinal), "status zählt Kanäle");

        var ids = Kanalnetze(liste);
        Pruefe.Gleich(10, ids.Count, "die zehn Kanalnetze der Vorlage, ohne Global und ohne Echo: " + string.Join(",", ids));
        foreach (var id in ids)
        {
            var (bl, al) = U("netz-loeschen", "--id", id);
            await t.AdminOk(bl, al);
        }
        Pruefe.Gleich<Fehler?>(null, await Stufe3VersionstorTests.HelloAsync(t, "0.5.1"), "ohne Kanalnetz (Global blieb ohne Ch.) fällt das Tor");
        var (bg, ag) = U("netz-setzen", "--id", "1", "--name", "Global");
        await t.AdminOk(bg, ag);
        Pruefe.Gleich<Fehler?>(null, await Stufe3VersionstorTests.HelloAsync(t, "0.5.1"), "Global ohne Ch. setzen schadet nicht");

        var danach = (await t.AdminOk("netzliste")).Text;
        Pruefe.Wahr(!danach.Contains(" Ch. ", StringComparison.Ordinal) && danach.Contains("Echo", StringComparison.Ordinal) && danach.Contains("Kommando", StringComparison.Ordinal),
            "keine Ch. mehr, Echo und Kommando bleiben:\n" + danach);
    }
}
