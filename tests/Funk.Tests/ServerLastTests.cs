// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>
/// Kleiner Lasttest auf Loopback (Vorstufe zu P-30): viele Sitzungen im selben Netz, mehrere Sprecher mit 50 Paketen/s,
/// gemessen werden Zustellquote und Verarbeitungszeit des Relais. Läuft nur mit <c>FUNK_LASTTEST=1</c>, weil er
/// einige Sekunden dauert; Größe über <c>FUNK_LAST_SITZUNGEN</c>, <c>FUNK_LAST_SPRECHER</c>, <c>FUNK_LAST_SEKUNDEN</c>.
/// </summary>
internal static class ServerLastTests
{
    private static int Zahl(string name, int standard) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out var z) && z > 0 ? z : standard;

    [Test]
    public static async Task Last_VieleHoererMehrereSprecher()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FUNK_LASTTEST"), "1", StringComparison.Ordinal))
        {
            Console.WriteLine("HINWEIS ServerLastTests übersprungen: FUNK_LASTTEST=1 setzen.");
            return;
        }
        var anzahl = Zahl("FUNK_LAST_SITZUNGEN", 20);
        var sprecherZahl = Math.Min(anzahl, Zahl("FUNK_LAST_SPRECHER", 4));
        var sekunden = Zahl("FUNK_LAST_SEKUNDEN", 5);

        // Loopback hat kein DSL-Budget; gemessen wird die Leistung des Relais, nicht der Heimanschluss (7.4: 35 Mbit/s).
        var mbit = Zahl("FUNK_LAST_MBIT", 500);
        await using var t = await Testserver.StartenAsync(grenzen: new Funk.Server.Betrieb.GrenzenKonfiguration { AusgangMbit = mbit });
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = new JsonObject
            {
                ["id"] = 2, ["name"] = "Last", ["typ"] = "NORMAL", ["reihenfolge"] = 0, ["nurTaste"] = false,
                ["ausloeserStandard"] = "TASTE", ["offen"] = "HOEREN_SENDEN", ["standard"] = true,
            },
        });
        var funker = new List<Testfunker>();
        try
        {
            for (var i = 0; i < anzahl; i++)
            {
                var f = await t.AktivAsync((100_000_000_000_000_000UL + (ulong)i).ToString(CultureInfo.InvariantCulture), "last" + i.ToString(CultureInfo.InvariantCulture),
                    i < sprecherZahl ? "RAIDLEITER" : "FUNKER", new System.Net.IPAddress([127, 0, (byte)(1 + i / 250), (byte)(2 + i % 250)]));
                f.UdpVerbinden();
                await f.UdpBindenAsync();
                funker.Add(f);
            }

            using var ende = new CancellationTokenSource();
            var empfangen = new long[anzahl];
            var hoerer = funker.Select((f, i) => Task.Run(async () =>
            {
                while (!ende.IsCancellationRequested)
                {
                    var r = await f.UdpEmpfangenAsync(TimeSpan.FromMilliseconds(200));
                    if (r is { } p && p.Kopf.Typ == PaketTyp.SpracheRunter) Interlocked.Increment(ref empfangen[i]);
                }
            })).ToList();

            var opus = new byte[140];
            opus[0] = 0xF8;
            var gesendet = 0L;
            var uhr = Stopwatch.StartNew();
            var takt = 0;
            while (uhr.Elapsed < TimeSpan.FromSeconds(sekunden))
            {
                for (var s = 0; s < sprecherZahl; s++)
                {
                    funker[s].SpracheSenden([2], opus, (uint)(1000 + takt), (ushort)(s + 1), takt == 0 ? Kennzeichen.Beginn : Kennzeichen.None);
                    gesendet++;
                }
                takt++;
                var soll = TimeSpan.FromMilliseconds(20 * takt);
                if (soll > uhr.Elapsed) await Task.Delay(soll - uhr.Elapsed);
            }
            await Task.Delay(500);
            var messwerte = await t.AdminOk("messwerte");
            await ende.CancelAsync();
            await Task.WhenAll(hoerer);

            var erwartet = gesendet * (anzahl - 1);
            var summe = empfangen.Sum();
            var verlust = erwartet == 0 ? 0 : 100.0 * (erwartet - summe) / erwartet;
            var rate = gesendet / uhr.Elapsed.TotalSeconds;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"LAST {anzahl} Sitzungen, {sprecherZahl} Sprecher, {sekunden} s: hoch {gesendet} ({rate:F0}/s), runter erwartet {erwartet}, empfangen {summe}, Verlust {verlust:F2} %"));
            Console.WriteLine("LAST Relais: " + messwerte.Text);
            Pruefe.Wahr(verlust < 5, "Zustellquote über 95 % auf Loopback");
        }
        finally
        {
            foreach (var f in funker) await f.DisposeAsync();
        }
    }
}
