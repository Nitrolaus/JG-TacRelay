// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Funk.Server.Kern;
using Funk.Server.Lage;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Tests;

/// <summary>
/// Last der Kanäle, im Speicher gemessen (Fassung 0.6.0, 6.16 "Last", 4.8): Jeder Ch.-Wechsel ist ein Commit mit einem Schreiben von
/// <c>lage.json</c>. Gemessen werden Größe und Schreibzeit von <c>lage.json</c> und die Zeit eines ganzen Commits für einen Ch.-Wechsel
/// mit 40 Funkern in einer großen Flotte (31 Kanalnetze, 21 Rollen) und mit 500 Konten zu 16 Kanälen, dazu Bau des Schnappschusses und Weiterleitung mit 100 Sitzungen und
/// 900 verschiedenen Kanälen. Die Zahlen stehen als MESSUNG in der Ausgabe; geprüft werden nur weite Grenzen, damit ein voller PC
/// den Lauf nicht kippt. Das ersetzt nicht den Lasttest am Server (Schritt 3.6, Linux).
/// </summary>
internal static class Stufe3LastTests
{
    private static string Ms(double ms) => ms.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Eine große Flotte: 31 Kanalnetze (Global mit Ch., dazu 30 Kanalnetze 100 bis 129 zu Ruhe und 20 Ch.), 21 Rollen zu 9 Feldern.</summary>
    private static SchnappschussBauer Grossflotte(int konten, int felderJeKonto, int sitzungen)
    {
        var b = new SchnappschussBauer().MitNetz(Netz.Global() with { Kanaele = ["Ruhe", "Gesamt", .. Enumerable.Range(1, 20).Select(i => "Global " + i)] });
        for (ushort n = 100; n < 130; n++)
            b.MitNetz(new Netz(n, "Kanal " + n) { Kanaele = ["Ruhe", .. Enumerable.Range(1, 20).Select(i => "Schiff " + i)], Offen = Offenheit.HoerenSenden, Gruppe = "Schiffe" });
        for (ushort r = 1; r <= 21; r++)
            b.MitRolle(new Rolle(r, "Rolle " + r) { SelbstWaehlbar = true, Felder = [.. Enumerable.Range(0, 9).Select(f => new RollenFeld((byte)f, (ushort)(100 + (r + f) % 30), null, true, true, f == 0, false))] });
        for (uint k = 1; k <= konten; k++)
        {
            var felder = Enumerable.Range(0, felderJeKonto).Select(f => new Mitgliedschaft((byte)f, Kanalrechnung.Id((ushort)(100 + (k + (uint)f) % 30), (byte)(1 + (k * 7 + (uint)f) % 20)), true, true, false));
            b.MitKonto(new Konto { Id = k, Name = "Funker " + k, Felder = [.. felder], GewaehlteRolle = (ushort)(1 + k % 21) });
            if (k <= sitzungen) b.MitSitzung(k + 50_000, k, k);
        }
        return b;
    }

    private static (double Median, double Max) Zeiten(int runden, Action aktion)
    {
        var zeiten = new List<double>(runden);
        for (var i = 0; i < runden; i++)
        {
            var uhr = Stopwatch.StartNew();
            aktion();
            zeiten.Add(uhr.Elapsed.TotalMilliseconds);
        }
        zeiten.Sort();
        return (zeiten[zeiten.Count / 2], zeiten[^1]);
    }

    [Test]
    public static void Messung_LageJson_UndCommitJeChWechsel()
    {
        foreach (var (konten, felder, sitzungen, was) in new[] { (40, 9, 40, "große Flotte, 40 Funker zu 9 Kanälen"), (500, 16, 100, "500 Konten zu 16 Kanälen, 100 Sitzungen") })
        {
            var verzeichnis = Path.Combine(Testserver.Wurzel(), "s3-last-" + Guid.NewGuid().ToString("N")[..12]);
            try
            {
                var ablage = new Zustandsablage(verzeichnis);
                var start = Grossflotte(konten, felder, sitzungen).Bauen();
                var schreiber = new Zustandsschreiber(start, null, ablage);
                var (schreibenMedian, schreibenMax) = Zeiten(20, () => ablage.Speichern(schreiber.Aktuell, schreiber.Neben));
                var groesse = new FileInfo(Path.Combine(verzeichnis, Zustandsablage.LageDatei)).Length;
                byte ch = 1;
                var (commitMedian, commitMax) = Zeiten(40, () =>
                {
                    ch = (byte)(ch % 20 + 1);
                    var r = schreiber.Ausfuehren(Auftrag.FeldSetzen(1, 0, Kanalrechnung.Id(101, ch)), 1_790_000_000_000);
                    Pruefe.Wahr(r.Erfolg, "Ch.-Wechsel: " + r);
                });
                Console.WriteLine($"MESSUNG Stufe 3 {was}: lage.json {groesse / 1024.0:F1} KB, Schreiben Median {Ms(schreibenMedian)} ms (höchstens {Ms(schreibenMax)}), "
                    + $"Commit eines Ch.-Wechsels Median {Ms(commitMedian)} ms (höchstens {Ms(commitMax)})");
                Pruefe.Wahr(groesse < 2 * 1024 * 1024, $"{was}: lage.json {groesse} Byte");
                Pruefe.Wahr(commitMedian < 200, $"{was}: Commit {commitMedian} ms im Median");
            }
            finally
            {
                if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, recursive: true);
            }
        }
    }

    [Test]
    public static void Messung_100Sitzungen_900Kanaele_SchnappschussUndWeiterleitung()
    {
        // Jede Sitzung hält 9 eigene Kanäle: 900 verschiedene Kanal-IDs über 30 Kanalnetze zu 30 Ch. Dazu hört jede Sitzung Ch. 1 von 101.
        var b = Grossflotte(0, 0, 0);
        for (ushort n = 100; n < 130; n++) b.MitNetz(new Netz(n, "Kanal " + n) { Kanaele = [.. Enumerable.Range(0, 31).Select(i => i == 0 ? "Ruhe" : "Ch " + i)], Offen = Offenheit.HoerenSenden });
        for (uint k = 0; k < 100; k++)
        {
            var felder = Enumerable.Range(0, 9).Select(f => new Mitgliedschaft((byte)f, Kanalrechnung.Id((ushort)(100 + (k * 9 + (uint)f) / 30), (byte)(1 + (k * 9 + (uint)f) % 30)), true, true, false))
                .Append(new Mitgliedschaft(9, Kanalrechnung.Id(129, 30), true, true, false));
            b.MitKonto(new Konto { Id = k + 1, Name = "S" + k, Felder = [.. felder.DistinctBy(m => m.NetzId)] }).MitSitzung(k + 60_000, k + 1, k + 1);
        }
        var z = b.Bauen();
        var (bauMedian, bauMax) = Zeiten(20, () => b.Bauen());
        var relay = new Weiterleiter();
        var ziel = new Mitschrift();
        ushort[] gemeinsam = [Kanalrechnung.Id(129, 30)];
        uint frame = 100;
        // Im 20-ms-Takt wie ein Encoder, sonst griffe die Sprachgrenze der Sitzung (60 Pakete/s, 4.8).
        var (paketMedian, paketMax) = Zeiten(500, () =>
        {
            ziel.Leeren();
            var paket = new Sprachpaket(60_000, frame, 1, Paketkennzeichen.Keine, Sendeart.Netz, gemeinsam, Funkprobe.Opus);
            relay.Verarbeiten(z, paket, 1_790_000_000_000 + frame++ * 20L, ziel);
        });
        Console.WriteLine($"MESSUNG Stufe 3 Schnappschuss mit 100 Sitzungen und {z.Konten.Values.SelectMany(k => k.Felder).Select(m => m.NetzId).Distinct().Count()} Kanälen: "
            + $"Bau Median {Ms(bauMedian)} ms (höchstens {Ms(bauMax)}), Weiterleitung an 99 Hörer Median {Ms(paketMedian)} ms (höchstens {Ms(paketMax)})");
        Pruefe.Gleich(99, ziel.Sprache.Count, "99 Hörer im gemeinsamen Kanal");
        Pruefe.Wahr(bauMedian < 100, $"Bau {bauMedian} ms im Median");
        Pruefe.Wahr(paketMedian < 2, $"Weiterleitung {paketMedian} ms im Median");
    }
}
