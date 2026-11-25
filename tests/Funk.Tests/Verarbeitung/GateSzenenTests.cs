// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>
/// Gate-Kennzahlen an synthetischen Szenen (audio-dsp.md, Empfehlung 8). Ziele aus ENTWURF.md: unter 0,5
/// Fehlauslösungen pro Minute je Geräuschszene, unter 2 % abgeschnittene Anlaute. Die Sprachwahrscheinlichkeit
/// kommt hier vom eigenen Stimmhaftigkeitsschätzer; mit RNNoise/Silero sind die Zahlen neu zu messen.
/// </summary>
internal static class GateSzenenTests
{
    private const double Minuten = 3;

    private static GateMessung Messen(string szene, float[] signal, IReadOnlyList<Aeusserung>? marken = null)
    {
        var m = GateKennzahlen.Messen(signal, marken ?? [], new Stimmhaftigkeit());
        Console.WriteLine($"  Gate {szene}: {m}");
        return m;
    }

    [Test]
    public static void Szene_Stille_KeineFehlausloesung()
    {
        var m = Messen("Stille", Testszenen.Stille(Minuten * 60, 1));
        Pruefe.Gleich(0, m.Fehlausloesungen, "Fehlauslösungen bei Stille");
    }

    [Test]
    public static void Szene_Tastatur_UnterHalbProMinute()
    {
        var m = Messen("Tastatur", Testszenen.Tastatur(Minuten * 60, 2));
        PruefeWert.Hoechstens(0.5, m.FehlausloesungenJeMinute, "Fehlauslösungen je Minute (Tastatur)");
    }

    [Test]
    public static void Szene_Tuete_UnterHalbProMinute()
    {
        var m = Messen("Tüte", Testszenen.Tuete(Minuten * 60, 3));
        PruefeWert.Hoechstens(0.5, m.FehlausloesungenJeMinute, "Fehlauslösungen je Minute (Tüte)");
    }

    [Test]
    public static void Szene_Sprache_WenigerAlsZweiProzentAbgeschnitten()
    {
        var (signal, marken) = Testszenen.Sprache(Minuten * 60, 4);
        var m = Messen("Sprache", signal, marken);
        PruefeWert.Mindestens(50, m.Anlaute, "genug Äußerungen für die Quote");
        PruefeWert.Hoechstens(0.02, m.AnteilAbgeschnitten, "Anteil abgeschnittener Anlaute");
        PruefeWert.Hoechstens(0.5, m.FehlausloesungenJeMinute, "Fehlauslösungen je Minute (Sprache)");
    }

    [Test]
    public static void Szene_SpracheMitTastatur_WenigerAlsZweiProzentAbgeschnitten()
    {
        var (sprache, marken) = Testszenen.Sprache(Minuten * 60, 5);
        var signal = Testszenen.Summe(sprache, Testszenen.Tastatur(Minuten * 60, 6));
        var m = Messen("Sprache mit Tastatur", signal, marken);
        PruefeWert.Hoechstens(0.02, m.AnteilAbgeschnitten, "Anteil abgeschnittener Anlaute beim Tippen");
        PruefeWert.Hoechstens(0.5, m.FehlausloesungenJeMinute, "Fehlauslösungen je Minute beim Tippen");
    }

    [Test]
    public static void Empfindlichkeit_Unempfindlich_SchneidetMehrAb()
    {
        // Gegenprobe, dass die Messung etwas misst: mit Regler ganz unten und 25 dB mehr Abstand fehlen Anlaute.
        var (signal, marken) = Testszenen.Sprache(60, 8);
        var normal = GateKennzahlen.Messen(signal, marken, new Stimmhaftigkeit());
        var stumpf = GateKennzahlen.Messen(signal, marken, new Stimmhaftigkeit(),
            new GateEinstellungen { AbstandStartDb = 30, AbstandMinDb = 30, AbstandMaxDb = 40 }, empfindlichkeit: -1f);
        Console.WriteLine($"  Gate Gegenprobe normal: {normal}");
        Console.WriteLine($"  Gate Gegenprobe unempfindlich: {stumpf}");
        PruefeWert.Mindestens(normal.AbgeschnitteneAnlaute + 1, stumpf.AbgeschnitteneAnlaute, "unempfindlich schneidet mehr ab");
    }
}
