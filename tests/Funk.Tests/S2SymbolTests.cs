// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Fenster;

namespace Funk.Tests;

/// <summary>
/// Ausbaustufe S2, Teil A2: das Zeichen für das Symbol im Infobereich. Kein Test legt ein echtes Symbol an (RAID-S2-ENTWURF
/// Abschnitt 5); geprüft wird, dass das eingebettete Programmzeichen dasselbe ist wie die Datei aus tools\symbol-zeichnen.ps1 und dass
/// für jede Symbolgröße des Infobereichs (16 bei 100 %, 20 bei 125 %, 24 bei 150 %, 32 bei 200 %) das passende Bild gewählt wird.
/// </summary>
internal static class S2SymbolTests
{
    [Test]
    public static void Programmzeichen_Eingebettet_UndBildJeGroesse()
    {
        var zeichen = Symboldatei.Programmzeichen();
        var datei = Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "TacRelay.ico");
        Pruefe.Wahr(zeichen.AsSpan().SequenceEqual(File.ReadAllBytes(datei)), "eingebettet ist genau TacRelay.ico");
        var bilder = Symboldatei.Bilder(zeichen);
        Pruefe.Gleich("16,20,24,32,40,48,64,128,256", string.Join(",", bilder.Select(b => b.Groesse)), "neun Größen");
        foreach (var (soll, erwartet) in new[] { (16, 16), (20, 20), (24, 24), (30, 32), (32, 32), (40, 40), (300, 256) })
            Pruefe.Gleich(erwartet, Symboldatei.Waehlen(bilder, soll)!.Value.Groesse, "für " + soll + " Bildpunkte");
    }

    [Test]
    public static void Symboldatei_Kaputt_KeineBilder()
    {
        Pruefe.Gleich(0, Symboldatei.Bilder([]).Count, "leer");
        Pruefe.Gleich(0, Symboldatei.Bilder([0, 0, 2, 0, 1, 0]).Count, "Typ 2 ist ein Mauszeiger, kein Symbol");
        // Ein Eintrag, der über das Ende zeigt, fällt weg; die Datei behauptet zwei Einträge, hat aber nur Platz für einen.
        var eins = new byte[6 + 16 + 4];
        eins[2] = 1;
        eins[4] = 1;
        eins[6] = 16;
        eins[14] = 4;
        eins[18] = 22;
        Pruefe.Gleich(1, Symboldatei.Bilder(eins).Count, "ein gültiger Eintrag");
        eins[14] = 5;
        Pruefe.Gleich(0, Symboldatei.Bilder(eins).Count, "zu lang: fällt weg");
        var zwei = (byte[])eins.Clone();
        zwei[4] = 2;
        Pruefe.Gleich(0, Symboldatei.Bilder(zwei).Count, "zwei Einträge ohne Platz: keine ICO-Datei");
        Pruefe.Gleich(null, Symboldatei.Waehlen([], 16), "nichts zu wählen");
    }
}
