// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Stimmprobe für den Prüfstand der Klangstufe D (Sarevoc 08.10.2026, Q60): die echte Kette nimmt hinter Echo- und Rauschfilter auf, vor der
/// Sprachaktivierung, so lange wie verlangt, unabhängig vom Senden; danach lässt sie sich einmal abholen.
/// </summary>
internal static class StimmprobeTests
{
    [Test]
    public static async Task Stimmprobe_NimmtHinterDenFilternAuf_UnabhaengigVomSenden_EinmalAbholbar()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Sprachaktivierung = false;
        s.Stille(50);
        Pruefe.Gleich(default(StimmprobeStand), s.Maschine.Stimmprobe, "ohne Probe nichts");

        // Die Testszene spricht ab 0,8 s; die Probe beginnt dort.
        var (sprache, _) = Testszenen.Sprache(4, 93);
        s.Abspielen(sprache.AsSpan(0, 38400));
        s.Maschine.StimmprobeStarten(1000);
        s.Abspielen(sprache.AsSpan(38400, 24000));
        var halb = s.Maschine.Stimmprobe;
        Pruefe.Wahr(halb.Laeuft && !halb.Fertig && halb.AufgenommenMs is >= 400 and <= 600 && halb.DauerMs == 1000, "nach einer halben Sekunde: " + halb);
        Pruefe.Gleich(null, s.Maschine.StimmprobeHolen(), "vor dem Ende gibt es nichts zu holen");

        s.Abspielen(sprache.AsSpan(62400, 24000));
        Pruefe.Wahr(s.Maschine.Stimmprobe is { Fertig: true, Laeuft: false }, "nach einer Sekunde fertig");
        var daten = s.Maschine.StimmprobeHolen();
        Pruefe.Wahr(daten is { Length: 48000 }, "eine Sekunde bei 48 kHz");
        PruefeWert.Mindestens(-45, Pegel.Dbfs(daten!), "die Stimme ist darin (die Szene spricht mit -32 bis -18 dBFS)");
        Pruefe.Gleich(0, s.Pakete.Count, "gesendet wurde nichts (Sprachaktivierung aus, keine Taste)");
        Pruefe.Gleich(default(StimmprobeStand), s.Maschine.Stimmprobe, "abgeholt: weg");
        Pruefe.Gleich(null, s.Maschine.StimmprobeHolen(), "nur einmal abholbar");
    }
}
