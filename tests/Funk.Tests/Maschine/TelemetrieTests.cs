// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Telemetrie der Sendekette (Klangstufe A, Recherche Schritt 1): Werte und Laufzeit je Stufe, verpasste Fristen, die neue
/// Reihenfolge (Pegelstufe und Begrenzer hinter dem APM, vor RNNoise und dem Gate) und das Klangprotokoll, das nur auf Wunsch und nur im
/// angegebenen Ordner entsteht. Geräte-Attrappen, simulierte Uhr, feste Seeds.
/// </summary>
internal static class TelemetrieTests
{
    private static readonly KlangStufe[] Alle =
        [KlangStufe.Referenz, KlangStufe.Echo, KlangStufe.Rauschfilter, KlangStufe.Pegelstufe, KlangStufe.Begrenzer, KlangStufe.Gate, KlangStufe.Opus, KlangStufe.Gesamt];

    [Test]
    public static async Task Telemetrie_JeStufe_WerteUndLaufzeiten()
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 6f);
        await s.StartenAsync();
        var (sprache, _) = Testszenen.Sprache(8, 3);
        s.Abspielen(sprache);
        var takte = sprache.Length / 480;

        var stand = s.Maschine.Telemetrie;
        Pruefe.Gleich((long)takte, stand.Rahmen, "Rahmen");
        Pruefe.Gleich(10_000.0, stand.BudgetUs, "Frist eines Rahmens");
        foreach (var stufe in Alle)
        {
            var l = stand.Stufe(stufe);
            if (stufe == KlangStufe.Opus) PruefeWert.Mindestens(50, l.Anzahl, "Opus: nur Rahmen mit Paket, gesendet wurde");
            else Pruefe.Gleich((long)takte, l.Anzahl, $"{stufe}: jeder Rahmen gemessen");
            Pruefe.Wahr(l.P50Us > 0 && l.P50Us <= l.P95Us && l.P95Us <= l.P99Us && l.P99Us <= l.MaxUs, $"{stufe}: {l}");
        }
        PruefeWert.Mindestens(stand.Stufe(KlangStufe.Echo).P99Us, stand.Stufe(KlangStufe.Gesamt).P99Us, "Gesamt mindestens so lang wie eine Stufe (p99)");
        Pruefe.Gleich(takte, s.Telemetrie.Count, "ein Eintrag je Rahmen");
        Pruefe.Gleich(stand.Letzter, s.Telemetrie[^1], "letzter Eintrag");

        var mitSprache = 0;
        for (var i = 0; i < s.Telemetrie.Count; i++)
        {
            var e = s.Telemetrie[i];
            Pruefe.Gleich((long)i, e.Rahmen, "laufende Nummer");
            Pruefe.Gleich(6f, e.PegelstufeDb, "feste Verstärkung");
            Pruefe.Wahr(e.Sprachwahrscheinlichkeit is >= 0f and <= 1f, "Wahrscheinlichkeit");
            Pruefe.Wahr(e.BegrenzerDb <= 0f, "Begrenzer senkt nur ab");
            Pruefe.Gleich(e.Gate is GateZustand.Senden or GateZustand.Nachlauf, e.Sendet, $"Rahmen {i}: Sendet folgt dem Gate");
            Pruefe.Gleich(e.RauschfilterDbfs, e.GatePegelDbfs, $"Rahmen {i}: das Gate sieht das Signal hinter RNNoise");
            if (e.EchoDbfs > -80f && e.BegrenzerDb == 0f)
            {
                PruefeWert.Nahe(e.EchoDbfs + 6f, e.VerstaerktDbfs, 0.01, $"Rahmen {i}: Pegelstufe hinter dem APM, vor RNNoise");
                mitSprache++;
            }
            if (i >= 2 && s.Telemetrie[i - 2].VerstaerktDbfs > -80f)
                PruefeWert.Nahe(e.RauschfilterDbfs - s.Telemetrie[i - 2].VerstaerktDbfs, e.RauschfilterDb, 0.01, $"Rahmen {i}: Wirkung RNNoise zeitrichtig");
        }
        PruefeWert.Mindestens(150, mitSprache, "Rahmen mit Signal");
        PruefeWert.Mindestens(1, s.Telemetrie.Count(e => e.Sendet), "es wurde gesendet");
        Pruefe.Wahr(s.Telemetrie.All(e => float.IsFinite(e.Zeiten.GesamtUs) && float.IsFinite(e.Zeiten.EchoUs)), "jede Stufe hat eine Zeit");
        Pruefe.Wahr(s.Telemetrie.Any(e => float.IsNaN(e.Zeiten.OpusUs)), "Opus nur, wenn kodiert wurde");
    }

    [Test]
    public static async Task Telemetrie_AlteKette_OhnePegelstufeUndBegrenzer()
    {
        await using var s = new Pruefstand(mitPegelstufe: false, apm: ApmEinstellungen.Sprechfunk) { TelemetrieMitschreiben = true };
        await s.StartenAsync();
        var (sprache, _) = Testszenen.Sprache(4, 4);
        s.Abspielen(sprache);
        var stand = s.Maschine.Telemetrie;
        Pruefe.Gleich(0L, stand.Stufe(KlangStufe.Pegelstufe).Anzahl, "keine Pegelstufe");
        Pruefe.Gleich(0L, stand.Stufe(KlangStufe.Begrenzer).Anzahl, "kein Begrenzer");
        foreach (var e in s.Telemetrie)
        {
            Pruefe.Gleich(0f, e.PegelstufeDb, "ohne Pegelstufe 0 dB");
            Pruefe.Gleich(0f, e.BegrenzerDb, "ohne Begrenzer 0 dB");
            Pruefe.Gleich(e.RauschfilterDbfs, e.GatePegelDbfs, "das Gate sieht das Signal hinter RNNoise");
            Pruefe.Gleich(e.EchoDbfs, e.VerstaerktDbfs, "nichts zwischen APM und RNNoise");
        }
    }

    [Test]
    public static async Task Telemetrie_VerpassteFrist_WirdGezaehlt()
    {
        // Ein Kodierer, der bei jedem 20. Paket 11 ms braucht: Diese Rahmen reißen die Frist von 10 ms.
        var langsam = 0;
        await using var s = new Pruefstand(kodierer: () => new LangsamerKodierer(() => ++langsam % 20 == 0)) { TelemetrieMitschreiben = true };
        await s.StartenAsync();
        s.Maschine.SprechtasteGedrueckt();
        s.Abspielen(Testszenen.Sprache(3, 5).Signal);
        s.Maschine.SprechtasteLosgelassen();
        var stand = s.Maschine.Telemetrie;
        var ueber = s.Telemetrie.Count(e => e.Zeiten.GesamtUs > 10_000f);
        PruefeWert.Mindestens(5, ueber, "langsame Rahmen");
        Pruefe.Gleich((long)ueber, stand.DeadlineVerpasst, "verpasste Fristen");
        PruefeWert.Mindestens(11_000, stand.Stufe(KlangStufe.Opus).MaxUs, "die langsame Stufe ist zu sehen");
        PruefeWert.Hoechstens(5_000, stand.Stufe(KlangStufe.Gate).MaxUs, "das Gate bekommt die Zeit des Kodierers nicht angerechnet");
    }

    [Test]
    public static async Task Klangprotokoll_NurAufWunsch_NurImOrdner()
    {
        var ordner = Path.Combine(Testserver.Wurzel(), "klangprotokoll-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var s = new Pruefstand();
            await s.StartenAsync();
            var (sprache, _) = Testszenen.Sprache(6, 6);
            s.Abspielen(sprache.AsSpan(0, 48000));
            Pruefe.Falsch(Directory.Exists(ordner), "ohne Wunsch kein Protokoll");

            s.Maschine.KlangprotokollOrdner = ordner;
            Pruefe.Gleich(ordner, s.Maschine.KlangprotokollOrdner, "Ordner");
            s.Abspielen(sprache.AsSpan(48000, 4 * 48000));
            s.Maschine.KlangprotokollOrdner = null;
            s.Abspielen(sprache.AsSpan(5 * 48000, 48000));

            var dateien = Directory.GetFiles(ordner);
            Pruefe.Gleich(1, dateien.Length, "genau eine Datei");
            Pruefe.Wahr(Path.GetFileName(dateien[0]).StartsWith("klangprotokoll-", StringComparison.Ordinal) && dateien[0].EndsWith(".csv", StringComparison.Ordinal), dateien[0]);
            var zeilen = File.ReadAllLines(dateien[0]);
            Pruefe.Wahr(zeilen[0].StartsWith('#'), "Kopf mit Erklärung");
            var kopf = zeilen.First(z => z.StartsWith("zeitMs;", StringComparison.Ordinal)).Split(';');
            var daten = zeilen.Where(z => z.Length > 0 && char.IsAsciiDigit(z[0])).ToList();
            // 4 s zu je 100 ms; die Zusammenfassung kommt als eigene Zeile alle 10 s und am Ende.
            PruefeWert.Nahe(40, daten.Count, 2, "eine Zeile je 100 ms");
            foreach (var z in daten)
            {
                var f = z.Split(';');
                Pruefe.Gleich(kopf.Length, f.Length, "Spalten");
                foreach (var w in f) Pruefe.Wahr(double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out _), $"Zahl: {w} in {z}");
            }
            Pruefe.Wahr(zeilen.Any(z => z.StartsWith("# Laufzeit", StringComparison.Ordinal)), "Zusammenfassung der Laufzeiten am Ende");
        }
        finally
        {
            if (Directory.Exists(ordner)) Directory.Delete(ordner, true);
        }
    }

    /// <summary>Opus-Kodierer, der auf Wunsch 11 ms wartet (aktiv, ohne Schlafen, damit die Zeit sicher vergeht).</summary>
    private sealed class LangsamerKodierer(Func<bool> langsam) : IKodierer
    {
        private readonly OpusKodierer _opus = new();

        public int Kodieren(ReadOnlySpan<float> pcm20ms, Span<byte> ziel)
        {
            if (langsam())
            {
                var start = Stopwatch.GetTimestamp();
                while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 11) Thread.SpinWait(50);
            }
            return _opus.Kodieren(pcm20ms, ziel);
        }

        public void SetzeVerlustHinweis(int prozent) => _opus.SetzeVerlustHinweis(prozent);

        public void Dispose() => _opus.Dispose();
    }
}
