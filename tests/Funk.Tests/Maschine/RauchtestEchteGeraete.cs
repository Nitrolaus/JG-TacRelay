// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Klang.Maschine;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Rauchtest mit echten Geräten, nur von Hand: <c>FUNK_RAUCHTEST=1</c>. Öffnet die Standardgeräte für Kommunikation
/// über WASAPI (Mikrofon, Wiedergabe stumm, Loopback als Echoreferenz) für drei Sekunden. Es wird nichts hörbar
/// ausgegeben und nichts gespeichert. Läuft ein Spiel oder eine Vollbildanwendung, wird nichts geöffnet, und startet eins
/// während des Tests, gibt er sofort frei (<see cref="Spielschutz"/>).
/// </summary>
internal static class RauchtestEchteGeraete
{
    private const string Was = "Rauchtest mit echten Geräten";

    [Test]
    public static async Task Rauchtest_Wasapi_DreiSekundenStumm()
    {
        if (!Spielschutz.RauchtestFreigegeben(Was)) return;

        var meldungen = 0;
        var fehler = new List<Exception>();
        await using var maschine = new KlangMaschine(new KlangMaschinenOptionen { AusgabeStumm = true });
        maschine.Pegel += _ => Interlocked.Increment(ref meldungen);
        maschine.Fehler += (_, e) =>
        {
            lock (fehler) fehler.Add(e);
        };
        var ein = maschine.Eingaenge();
        var aus = maschine.Ausgaenge();
        Console.WriteLine($"  Rauchtest: {ein.Count} Eingänge, {aus.Count} Ausgänge; Standard: " +
            $"{ein.FirstOrDefault(g => g.IstStandard)?.Name ?? "-"} / {aus.FirstOrDefault(g => g.IstStandard)?.Name ?? "-"}");
        var uhr = Stopwatch.StartNew();
        await maschine.StartenAsync(null, null, CancellationToken.None);
        var startMs = uhr.ElapsedMilliseconds;
        if (!await Spielschutz.WartenAsync(() => false, TimeSpan.FromSeconds(3), Was)) return;
        var st = maschine.Statistik;
        await maschine.StoppenAsync();
        Console.WriteLine($"  Rauchtest: Aufnahme {st.Aufnahmeformat}, Wiedergabe {st.Wiedergabeformat}");
        Console.WriteLine($"  Rauchtest: Start {startMs} ms, {st.Rahmen} Rahmen, {meldungen} Pegelmeldungen, Aufnahme {st.AufnahmeLaeuft}, " +
            $"Wiedergabe {st.WiedergabeLaeuft}, Loopback {st.EchoreferenzLaeuft}, Referenz fehlte {st.ReferenzFehlt}, verworfen {st.ReferenzVerworfen}");
        var fuellstaende = Enumerable.Range(0, Sendekette.ReferenzHoechstRahmen + 2).Select(maschine.Kette.ReferenzFuellstand).ToArray();
        Console.WriteLine($"  Rauchtest: Referenz beim Lesen (Rahmen bereit: Anzahl) {string.Join(", ", fuellstaende.Select((n, i) => $"{i}: {n}"))}");
        foreach (var e in fehler) Console.WriteLine($"  Rauchtest Fehler: {e.GetType().Name}: {e.Message} {e.InnerException?.Message}");
        Pruefe.Wahr(st.AufnahmeLaeuft && st.WiedergabeLaeuft, "Aufnahme und Wiedergabe liefen");
        PruefeWert.Mindestens(250, st.Rahmen, "Rahmen in 3 s (Soll 300)");
        PruefeWert.Mindestens(120, meldungen, "Pegelmeldungen in 3 s (Soll 150)");
    }
}
