// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Befund der Gegenprüfung: Die Sendeaufbereitung gibt dem Kodierer Ziele mit 400 Byte (FUNKPROTOKOLL-v1 5.6), der
/// OpusKodierer verlangte aber 1276 und warf; ein Umweg (PaketKodierer) kopierte um. Jetzt geht die Länge des Ziels als
/// max_data_bytes an libopus. Geprüft mit echtem Opus.
/// </summary>
internal static class KodiererZielTests
{
    [Test]
    public static void Opus_Ziel400_SpracheKodiertUndDekodiert()
    {
        using var k = new OpusKodierer();
        using var d = new OpusDekodierer();
        var sprache = AnbindungSignale.Sprache(48000 * 2, 5);
        var ziel = new byte[Sendeaufbereitung.MaxPaketBytes];
        var pcm = new float[OpusDekodierer.MaxSamples];
        var laengen = new List<int>();
        for (var f = 0; f + OpusKodierer.RahmenSamples <= sprache.Length; f += OpusKodierer.RahmenSamples)
        {
            var n = k.Kodieren(sprache.AsSpan(f, OpusKodierer.RahmenSamples), ziel);
            Pruefe.Wahr(n is > 0 and <= Sendeaufbereitung.MaxPaketBytes, $"Rahmen {f / 960}: {n} Byte");
            Pruefe.Gleich(960, OpusPaket.AnzahlSamples(ziel.AsSpan(0, n)), "20 ms (5.13)");
            Pruefe.Gleich(960, d.Dekodieren(ziel.AsSpan(0, n), pcm), "dekodiert");
            laengen.Add(n);
        }
        laengen.Sort();
        Console.WriteLine($"  Opus in 400 Byte: {laengen.Count} Rahmen, {laengen[0]} bis {laengen[^1]} Byte, Median {laengen[laengen.Count / 2]}");
        Pruefe.Gleich(0L, d.UngueltigePakete, "kein ungültiges Paket");
    }

    [Test]
    public static void Opus_KleinesZiel_HaeltDieLaengeEin()
    {
        // 256 kbit/s auf Rauschen braucht um 640 Byte je Rahmen: Mit 1276 Byte Ziel wird das so lang, mit 400 Byte hält
        // libopus die Grenze, indem es die Bitrate dieses Rahmens senkt. Das Paket bleibt gültig.
        var rauschen = AnbindungSignale.Rauschen(960 * 50, -10, 31);
        foreach (var (grenze, name) in new[] { (OpusKodierer.MaxPaketBytes, "1276"), (400, "400"), (40, "40") })
        {
            using var k = new OpusKodierer(bitrate: 256_000);
            using var d = new OpusDekodierer();
            var ziel = new byte[grenze];
            var pcm = new float[OpusDekodierer.MaxSamples];
            var groesste = 0;
            for (var f = 0; f < 50; f++)
            {
                var n = k.Kodieren(rauschen.AsSpan(f * 960, 960), ziel);
                groesste = Math.Max(groesste, n);
                Pruefe.Wahr(n <= grenze, $"Ziel {name}: {n} Byte");
                Pruefe.Gleich(960, d.Dekodieren(ziel.AsSpan(0, n), pcm), $"Ziel {name}: dekodierbar");
            }
            Console.WriteLine($"  Opus 256 kbit/s Rauschen, Ziel {name} Byte: größtes Paket {groesste} Byte");
            if (grenze == OpusKodierer.MaxPaketBytes) Pruefe.Wahr(groesste > 400, "ohne Grenze wäre es länger (der Test misst etwas)");
        }
    }

    [Test]
    public static void Sendeaufbereitung_DirektMitOpus_KeinKodierfehler()
    {
        // So hängt es jetzt in der Sendekette: Sendeaufbereitung (400-Byte-Ziele) direkt vor dem OpusKodierer.
        using var k = new OpusKodierer();
        var s = new Sendeaufbereitung(new SprachGate(), k);
        var sprache = AnbindungSignale.Sprache(48000, 9);
        var pakete = 0;
        for (var r = 0; r + 480 <= sprache.Length; r += 480)
        {
            var n = s.Verarbeiten(sprache.AsSpan(r, 480), 0.9f, 0f, sprechtaste: r < 48000 - 4800);
            for (var i = 0; i < n; i++)
            {
                Pruefe.Wahr(s.Paket(i).Length <= Sendeaufbereitung.MaxPaketBytes, "höchstens 400 Byte");
                if (s.Paket(i).Length > 0) pakete++;
            }
        }
        Pruefe.Gleich(0L, s.Kodierfehler, "kein Kodierfehler (früher warf der Kodierer im Audio-Thread)");
        PruefeWert.Mindestens(40, pakete, "Pakete für knapp 1 s Sprechtaste");
        Pruefe.Falsch(typeof(Sendeaufbereitung).Assembly.GetTypes().Any(t => t.Name == "PaketKodierer"), "der Umweg ist entfernt");
    }
}
