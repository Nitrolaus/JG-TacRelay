// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Funk.Admin;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;

namespace Funk.Tests;

/// <summary>
/// <c>funk-admin erster-admin</c> (EINRICHTEN-UMZUG-ENTWURF): offene Anträge zeigen, Kennung aus dem Client erfragen, freigeben,
/// Stufe ADMIN, Eigentümer und Admin vorsehen in einem Zug, dann die Kennung des Admin-Schlüssels für admin-ernennen. Dazu
/// <c>geraete --nicht-seit</c> für die Nachzügler vor dem Umzug.
/// </summary>
internal static class ErsterAdminTests
{
    /// <summary>Tastatur, die erst tippt, wenn der Test es sagt.</summary>
    private sealed class Tastatur : TextReader
    {
        private readonly Channel<string?> _zeilen = Channel.CreateUnbounded<string?>();

        public void Tippen(string? zeile) => _zeilen.Writer.TryWrite(zeile);

        public override string? ReadLine() => _zeilen.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
    }

    private static async Task<bool> Warten(Func<bool> bedingung, int ms = 10_000)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(20);
        }
        return true;
    }

    /// <summary>Ein Client stellt den Antrag mit seinen Namen (A42) und wartet im Warteraum.</summary>
    private static async Task<(FunkClient Client, Geraeteidentitaet Id)> AntragAsync(ServerHalter halter, string sc, string discord, string lokal)
    {
        var id = Geraeteidentitaet.Fluechtig();
        var c = new FunkClient(new FunkClientOptionen
        {
            Aufbau = halter.Aufbau(id, IPAddress.Parse(lokal)),
            Identitaet = id,
            ClientVersion = "1.0.0",
            Anmeldung = new Anmeldedaten(sc, discord),
            Zufall = () => 0.0,
        });
        c.Starten();
        Pruefe.Wahr(await c.WartenAufAsync(z => z == ClientZustand.Warteraum, TimeSpan.FromSeconds(10)), sc + ": Antrag im Warteraum, war " + c.Zustand);
        return (c, id);
    }

    [Test]
    public static async Task ErsterAdmin_InEinemZug()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        var (fremd, fremdId) = await AntragAsync(halter, "Fremder", "Fremder/X", "127.0.0.71");
        var (test, testId) = await AntragAsync(halter, "Sarevoc", "Sarevoc/Otto", "127.0.0.72");
        using var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var tastatur = new Tastatur();
        var sb = new StringBuilder();
        using var aus = TextWriter.Synchronized(new StringWriter(sb));
        string Ausgabe() { lock (sb) return sb.ToString(); }
        try
        {
            var lauf = new ErsterAdmin(tastatur, aus, (b, a) => t.Admin(b, a)).AusfuehrenAsync();
            Pruefe.Wahr(await Warten(() => Ausgabe().Contains("Kennung", StringComparison.Ordinal)), "fragt nach der Kennung:\n" + Ausgabe());
            Pruefe.Wahr(Ausgabe().Contains("Sarevoc", StringComparison.Ordinal) && Ausgabe().Contains("Fremder", StringComparison.Ordinal), "zeigt die offenen Anträge:\n" + Ausgabe());
            // 0.10.0 (Befund der Prüfung): Die Kennung steht auf der Startseite; den Bereich Verbindung gibt es seit 0.6.0 nicht mehr.
            Pruefe.Wahr(Ausgabe().Contains("Kennung von der Startseite deines Clients", StringComparison.Ordinal)
                && !Ausgabe().Contains("Bereich Verbindung", StringComparison.Ordinal), "fragt mit dem Weg von heute:\n" + Ausgabe());

            // Vertippt: nichts geschieht, er fragt noch einmal.
            tastatur.Tippen("0000-0000-0000-0000");
            Pruefe.Wahr(await Warten(() => Ausgabe().Contains("keinen offenen Antrag", StringComparison.Ordinal)), "falsche Kennung:\n" + Ausgabe());
            Pruefe.Wahr(Ausgabe().Contains("Sie steht im Client auf der Startseite", StringComparison.Ordinal), "sagt, wo die Kennung steht:\n" + Ausgabe());
            tastatur.Tippen(test.Geraet.Anzeige.ToLowerInvariant());

            Pruefe.Wahr(await Warten(() => Ausgabe().Contains("admin-ernennen", StringComparison.Ordinal)), "bis zur Frage nach dem Admin-Schlüssel:\n" + Ausgabe());
            Pruefe.Wahr(await test.WartenAufAsync(z => z is ClientZustand.AktivUdp or ClientZustand.AktivUdpBinden or ClientZustand.AktivTunnel, TimeSpan.FromSeconds(10)), "Test ist freigegeben");
            var konten = (await t.AdminOk("konten")).Daten!.AsArray();
            var konto = konten.Single(k => (string?)k!["discordName"] == "Sarevoc/Otto")!;
            Pruefe.Gleich("ADMIN", (string?)konto["stufe"], "Stufe ADMIN");
            Pruefe.Gleich(true, (bool?)konto["eigentuemer"], "Eigentümer");
            Pruefe.Gleich("VORGESEHEN", (string?)konto["admin"], "als Admin vorgesehen");
            Pruefe.Wahr(konten.Any(k => (string?)k!["discordName"] == "Fremder/X" && (string?)k["status"] == "ANTRAG"), "der fremde Antrag bleibt offen");

            // Noch kein Antrag des Admin-Schlüssels: er sagt es und wartet weiter.
            tastatur.Tippen("1111-2222-3333-4444");
            Pruefe.Wahr(await Warten(() => Ausgabe().Contains("Verwaltung einrichten", StringComparison.Ordinal) && Ausgabe().Split("Kennung des Admin-Schlüssels").Length >= 3),
                "ohne Antrag: noch einmal fragen:\n" + Ausgabe());

            // Der Client stellt den Antrag (Verwaltung einrichten) und zeigt die Kennung; Sarevoc tippt sie ab.
            AdminEinrichtungAntwort? antrag = null;
            for (var i = 0; i < 10 && antrag is null; i++)
            {
                var a = await test.AdminAntragAsync(schluessel);
                if (a.Ok) antrag = (AdminEinrichtungAntwort)a.Body!;
                else await Task.Delay(1500);
            }
            Pruefe.Wahr(antrag?.Kennung is not null, "Antrag des Admin-Schlüssels");
            tastatur.Tippen(antrag!.Kennung);
            Pruefe.Gleich(0, await lauf.WaitAsync(TimeSpan.FromSeconds(30)), "fertig:\n" + Ausgabe());
            Pruefe.Wahr(Ausgabe().Contains("ist Admin", StringComparison.Ordinal), "ernannt:\n" + Ausgabe());
            Pruefe.Wahr((await t.AdminOk("admins")).Text.Contains(antrag.Kennung!, StringComparison.Ordinal), "Schlüssel eingetragen");

            // Ein zweites Mal: Es gibt einen Eigentümer, erster-admin rührt nichts an.
            var zweiter = new StringWriter();
            var nochmal = new ErsterAdmin(new StringReader(fremd.Geraet.Anzeige + "\n"), zweiter, (b, a) => t.Admin(b, a));
            Pruefe.Gleich(1, await nochmal.AusfuehrenAsync(), "mit Eigentümer: " + zweiter);
            Pruefe.Wahr(zweiter.ToString().Contains("schon einen Eigentümer", StringComparison.Ordinal), zweiter.ToString());
            Pruefe.Wahr((await t.AdminOk("antraege")).Text.Contains("Fremder", StringComparison.Ordinal), "der fremde Antrag ist nicht freigegeben");
        }
        finally
        {
            tastatur.Tippen(null);
            await test.DisposeAsync();
            await fremd.DisposeAsync();
            testId.Dispose();
            fremdId.Dispose();
        }
    }

    [Test]
    public static async Task ErsterAdmin_OhneAntragOderKennung()
    {
        await using var t = await Testserver.StartenAsync();
        // Über die Kommandozeile, ohne offene Anträge.
        var (code, text) = await AdminEinrichtungTests.MitEingabe("\n", "erster-admin", "--socket", t.Dienst.Verwaltungspfad);
        Pruefe.Gleich(1, code, "ohne Antrag: " + text);
        Pruefe.Wahr(text.Contains("Keine offenen Anträge", StringComparison.Ordinal), text);

        var halter = new ServerHalter { Server = t };
        var (c, id) = await AntragAsync(halter, "Sarevoc", "Sarevoc/Otto", "127.0.0.73");
        try
        {
            (code, text) = await AdminEinrichtungTests.MitEingabe("AAAA-BBBB-CCCC-DDDD\nAAAA-BBBB-CCCC-DDDD\nAAAA-BBBB-CCCC-DDDD\n", "erster-admin", "--socket", t.Dienst.Verwaltungspfad);
            Pruefe.Gleich(1, code, "dreimal vertippt: " + text);
            Pruefe.Wahr(text.Contains("Nichts geändert", StringComparison.Ordinal), text);
            Pruefe.Wahr((await t.AdminOk("antraege")).Text.Contains("Sarevoc", StringComparison.Ordinal), "Antrag weiter offen");
        }
        finally
        {
            await c.DisposeAsync();
            id.Dispose();
        }
    }

    [Test]
    public static async Task Geraete_NichtSeit_NenntDieNachzuegler()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await using var anna = await KernFunker.AktivAsync(halter, "950000000000000001", "anna", "FUNKER", IPAddress.Parse("127.0.0.74"));
        var (c, id) = await AntragAsync(halter, "Spaet", "Spaet/X", "127.0.0.75");
        try
        {
            var heute = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var morgen = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var (code, text) = await AdminEinrichtungTests.Werkzeug("geraete", "--nicht-seit", "2000-01-01", "--socket", t.Dienst.Verwaltungspfad);
            Pruefe.Gleich(0, code, text);
            Pruefe.Wahr(text.Contains(anna.Identitaet.Fingerabdruck.Anzeige, StringComparison.Ordinal) == false && text.Contains("nie verbunden", StringComparison.Ordinal),
                "seit 2000 war anna verbunden; der Antrag nie:\n" + text);

            (code, text) = await AdminEinrichtungTests.Werkzeug("geraete", "--nicht-seit", morgen, "--socket", t.Dienst.Verwaltungspfad, "--json-aus");
            Pruefe.Gleich(0, code, text);
            var daten = JsonNode.Parse(text)!["daten"]!.AsArray();
            var a = daten.Single(g => (string?)g!["anzeige"] == anna.Identitaet.Fingerabdruck.Anzeige)!;
            Pruefe.Wahr(a["zuletzt"] is JsonValue, "mit zuletzt: " + a.ToJsonString());
            (code, text) = await AdminEinrichtungTests.Werkzeug("geraete", "--nicht-seit", morgen, "--socket", t.Dienst.Verwaltungspfad);
            Pruefe.Wahr(text.Contains("zuletzt " + heute, StringComparison.Ordinal) && text.Contains(morgen, StringComparison.Ordinal), "bis morgen nicht verbunden, zuletzt heute:\n" + text);

            (code, text) = await AdminEinrichtungTests.Werkzeug("geraete", "--nicht-seit", "01.10.2026", "--socket", t.Dienst.Verwaltungspfad);
            Pruefe.Gleich(2, code, "Datum falsch geschrieben: " + text);
            Pruefe.Wahr(text.Contains("2026-10-01", StringComparison.Ordinal), text);

            (code, text) = await AdminEinrichtungTests.Werkzeug("geraete", "--socket", t.Dienst.Verwaltungspfad);
            Pruefe.Wahr(code == 0 && text.Contains(anna.Identitaet.Fingerabdruck.Anzeige, StringComparison.Ordinal), "ohne Schalter wie bisher:\n" + text);
        }
        finally
        {
            await c.DisposeAsync();
            id.Dispose();
        }
    }
}
