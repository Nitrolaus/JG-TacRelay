// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Funk.Client.Design;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Farbstellung Anthrazit (Sarevoc, 18./19.10.2026, Q5, Q12, Q17): Anthrazit und Grau, neutral und ruhig, mit gedämpftem Akzent Petrol,
/// gemessen nach WCAG 2.1 gegen #252525, #2F2F2F und #1E1E1E. Regeln: Text mindestens 4,5:1, Rahmen, Fokus und Zustandsfarben mindestens
/// 3:1, die Leitfarbe nur als Fläche. Bis 19.10.2026 gab es drei Stellungen in Weinrot (P1 bis P3); sie sind entfallen. Ohne
/// WPF-Fenster; die Bilder macht OberflaecheFunkfeldBilderTests.
/// </summary>
internal static class OberflaecheFarbvorschlagTests
{
    private static void Mindestens(double soll, Color vorn, Color hinten, string was)
    {
        var k = Kontrast.Verhaeltnis(vorn, hinten);
        Pruefe.Wahr(k >= soll, $"{was}: {k:0.00}:1, verlangt {soll}:1");
    }

    /// <summary>Farbton nach HSV in Grad, 0 bis 360.</summary>
    private static double Farbton(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max - min < 1e-9) return 0;
        var h = max == r ? (g - b) / (max - min) : max == g ? 2 + ((b - r) / (max - min)) : 4 + ((r - g) / (max - min));
        return ((h * 60) + 360) % 360;
    }

    /// <summary>Sättigung nach HSV, 0..1.</summary>
    private static double Saettigung(Color c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max == 0 ? 0 : (max - min) / (double)max;
    }

    [Test]
    public static void Farbvorschlag_Kontraste_Anthrazit()
    {
        Pruefe.Gleich(1, Farbvorschlaege.Alle.Count, "genau eine Farbstellung");
        Pruefe.Gleich(1, Enum.GetValues<Farbstellung>().Length, "die Aufzählung kennt nur Anthrazit");
        foreach (var s in Farbvorschlaege.Alle)
        {
            var n = s.Name;
            foreach (var (name, flaeche) in new[] { ("#252525", Palette.Anthrazit), ("#2F2F2F", Palette.Flaeche), ("#1E1E1E", Palette.FlaecheTief) })
                Mindestens(3, s.Akzent, flaeche, $"{n}: Akzent (Rahmen, Fokus, Reglerspur) auf {name}");
            Mindestens(4.5, Palette.Text, s.Leitfarbe, n + ": weiße Schrift auf der Leitfarbe");
            Mindestens(4.5, Palette.Text, s.LeitfarbeGedrueckt, n + ": weiße Schrift auf der Leitfarbe gedrückt");
            Mindestens(4.5, Palette.Text2, s.Leitfarbe, n + ": kleine Schrift auf Global");
            Mindestens(3, Palette.Text, s.AkzentFlaeche, n + ": Knauf auf der Akzentfläche");
            Mindestens(3, Palette.Text, s.SchliessenFlaeche, n + ": Symbol Schließen unter der Maus");
            Mindestens(3, s.SchliessenFlaeche, Palette.FlaecheTief, n + ": Schließen unter der Maus auf der Titelzeile des Funkfelds");
            Mindestens(2, s.SchliessenFlaeche, s.Leitfarbe, n + ": Schließen unter der Maus hebt sich von der Titelleiste ab");
            Mindestens(3, Palette.Senden, s.Leitfarbe, n + ": grüner Rahmen auf Global");
            Mindestens(3, Palette.Hoeren, s.Leitfarbe, n + ": orange Marke auf Global");
            Mindestens(4.5, Palette.Senden, s.Leitfarbe, n + ": grüne Schrift 'Du sprichst' auf Global");
            Mindestens(4.5, s.RotSchrift, Palette.Anthrazit, n + ": rote Schrift auf #252525");
            Mindestens(4.5, s.RotSchrift, Palette.Flaeche, n + ": rote Schrift auf der Kachelfläche #2F2F2F");
            Mindestens(4.5, s.RotSchrift, Palette.FlaecheTief, n + ": rote Schrift auf der Unterlage #1E1E1E");
            Mindestens(3, s.Akzent, Palette.FlaecheTief, n + ": Rand des Griffschilds auf der Grafik");
            Mindestens(3, s.Glut, Palette.FlaecheTief, n + ": Glühen um den Strich des offenen Reiters");
            Pruefe.Wahr(Kontrast.Verhaeltnis(s.Leitfarbe, Palette.Anthrazit) < 3, n + ": die Leitfarbe bleibt Fläche, nie Linie oder Schrift");
            Pruefe.Wahr(Kontrast.Leuchtdichte(s.LeitfarbeGedrueckt) < Kontrast.Leuchtdichte(s.Leitfarbe), n + ": gedrückt dunkler als die Fläche");
            Console.WriteLine(string.Create(CultureInfo.GetCultureInfo("de-DE"),
                $"MASS {n}: Akzent {Kontrast.Verhaeltnis(s.Akzent, Palette.Anthrazit):0.00}/{Kontrast.Verhaeltnis(s.Akzent, Palette.Flaeche):0.00}/{Kontrast.Verhaeltnis(s.Akzent, Palette.FlaecheTief):0.00}, " +
                $"Weiß auf der Leitfarbe {Kontrast.Verhaeltnis(Palette.Text, s.Leitfarbe):0.00} (gedrückt {Kontrast.Verhaeltnis(Palette.Text, s.LeitfarbeGedrueckt):0.00}), " +
                $"Grün auf Global {Kontrast.Verhaeltnis(Palette.Senden, s.Leitfarbe):0.00}, Weiß auf der Akzentfläche {Kontrast.Verhaeltnis(Palette.Text, s.AkzentFlaeche):0.00}, " +
                $"Schrift {Kontrast.Verhaeltnis(s.RotSchrift, Palette.Anthrazit):0.00}/{Kontrast.Verhaeltnis(s.RotSchrift, Palette.Flaeche):0.00}/{Kontrast.Verhaeltnis(s.RotSchrift, Palette.FlaecheTief):0.00}, " +
                $"Schließen {Kontrast.Verhaeltnis(Palette.Text, s.SchliessenFlaeche):0.00}/{Kontrast.Verhaeltnis(s.SchliessenFlaeche, s.Leitfarbe):0.00}"));
        }

        // Die Farben von Sarevoc (19.10.2026): Akzent Petrol #2F8A8C, hell #4FB3B4; Leitfarbe dunkles Petrol im selben Farbton; die
        // Grautöne neutral; Rot nur für Warnungen und Schließen.
        var a = Farbvorschlaege.Anthrazit;
        Pruefe.Gleich(Color.FromRgb(0x2F, 0x8A, 0x8C), a.Akzent, "Akzent Petrol #2F8A8C");
        Pruefe.Gleich(Color.FromRgb(0x4F, 0xB3, 0xB4), a.Glut, "Glühen in Petrol hell #4FB3B4");
        Pruefe.Wahr(Math.Abs(Farbton(a.Leitfarbe) - Farbton(a.Akzent)) < 4 && Math.Abs(Farbton(a.LeitfarbeGedrueckt) - Farbton(a.Akzent)) < 6,
            string.Create(CultureInfo.InvariantCulture, $"Leitfarbe im Farbton des Petrols ({Farbton(a.Leitfarbe):0}, {Farbton(a.LeitfarbeGedrueckt):0} gegen {Farbton(a.Akzent):0} Grad)"));
        foreach (var grau in new[] { Palette.Anthrazit, Palette.Flaeche, Palette.FlaecheTief, Palette.Linie, Palette.Text2, Palette.TextAus })
            Pruefe.Wahr(Saettigung(grau) < 0.01, "neutrales Grau " + grau);
        foreach (var rot in new[] { a.RotSchrift, a.SchliessenFlaeche })
            Pruefe.Wahr(Farbton(rot) < 15 || Farbton(rot) > 345, string.Create(CultureInfo.InvariantCulture, $"{rot} ist rot ({Farbton(rot):0} Grad)"));
    }

    [Test]
    public static void Farbvorschlag_Anthrazit_IstDiePalette()
    {
        var a = Farbvorschlaege.Anthrazit;
        Pruefe.Gleich(Palette.Leitfarbe, a.Leitfarbe, "Leitfarbe");
        Pruefe.Gleich(Palette.LeitfarbeGedrueckt, a.LeitfarbeGedrueckt, "gedrückt");
        Pruefe.Gleich(Palette.Akzent, a.Akzent, "Akzent");
        Pruefe.Gleich(Palette.AkzentFlaeche, a.AkzentFlaeche, "Akzentfläche");
        Pruefe.Gleich(Palette.RotSchrift, a.RotSchrift, "rote Schrift");
        Pruefe.Gleich(Palette.SchliessenFlaeche, a.SchliessenFlaeche, "Schließen unter der Maus");
        Pruefe.Gleich(Palette.Glut, a.Glut, "Glühen");
        Pruefe.Gleich(Color.FromArgb(0x26, a.Akzent.R, a.Akzent.G, a.Akzent.B), a.KorridorFlaeche, "Korridor wie in Farben.xaml");
        Pruefe.Gleich(Farbstellung.Anthrazit, Farbvorschlaege.Voreinstellung, "Voreinstellung Anthrazit");
        Pruefe.Gleich(Farbvorschlaege.Anthrazit, Farbvorschlaege.Von(Farbstellung.Anthrazit), "Von");
        Pruefe.Gleich(Farbvorschlaege.Anthrazit, Farbvorschlaege.Von((Farbstellung)9), "Unbekanntes wird die Voreinstellung");
    }

    [Test]
    public static void Farbvorschlag_Woerterbuch_ErsetztNurDieRollen()
    {
        var w = Farbvorschlaege.Woerterbuch(Farbvorschlaege.Anthrazit);
        var rollen = new[] { "Leitfarbe", "LeitfarbeGedrueckt", "AkzentFlaeche", "Akzent", "RotSchrift", "SchliessenFlaeche", "KorridorFlaeche", "Glut" };
        Pruefe.Gleich((rollen.Length * 2) + 1, w.Count, "je Rolle Farbe und Pinsel, dazu die Kennung");
        foreach (var r in rollen)
        {
            Pruefe.Wahr(w["Farbe." + r] is Color, "Farbe." + r);
            Pruefe.Wahr(w["Pinsel." + r] is SolidColorBrush { IsFrozen: true } p && p.Color == (Color)w["Farbe." + r], "Pinsel." + r + " eingefroren und gleich");
        }
        Pruefe.Gleich(Farbvorschlaege.Anthrazit.Akzent, (Color)w["Farbe.Akzent"], "Akzent");
        Pruefe.Gleich(Farbstellung.Anthrazit, (Farbstellung)w[Farbvorschlaege.Kennung], "Kennung");

        var ziel = new ResourceDictionary();
        ziel.MergedDictionaries.Add(new ResourceDictionary());
        ziel.MergedDictionaries.Add(new ResourceDictionary());
        Farbvorschlaege.Anwenden(ziel, Farbstellung.Anthrazit);
        Pruefe.Gleich(3, ziel.MergedDictionaries.Count, "eingesetzt");
        Pruefe.Wahr(ziel.MergedDictionaries[2].Contains(Farbvorschlaege.Kennung), "ohne Farben.xaml am Ende, wo es gewinnt");
        // Ein Satz, der keine Stellung ist (so zeichnen Tests Gegenproben), ersetzt den vorigen, statt sich daneben zu legen.
        var probe = Farbvorschlaege.Anthrazit with { Leitfarbe = Color.FromRgb(0x30, 0x30, 0x60) };
        Farbvorschlaege.Anwenden(ziel, probe);
        Pruefe.Gleich(3, ziel.MergedDictionaries.Count, "ersetzt statt gestapelt");
        Pruefe.Gleich(probe.Leitfarbe, (Color)ziel["Farbe.Leitfarbe"], "der neue Satz gilt");
        Farbvorschlaege.Anwenden(ziel, Farbstellung.Anthrazit);
        Pruefe.Gleich(Farbvorschlaege.Anthrazit.Leitfarbe, (Color)ziel["Farbe.Leitfarbe"], "zurück auf Anthrazit");
        Pruefe.Gleich(3, ziel.MergedDictionaries.Count, "wieder nur eins");
    }

    /// <summary>
    /// Damit die Farbstellung überall wirkt, nimmt die Oberfläche Farben nur über die Rollen und Pinsel aus Farben.xaml: Kein Farbwert der
    /// Farbstellung steht fest in einer Ansicht oder in Code außerhalb von Design\ (als #RRGGBB, #AARRGGBB oder Color.FromRgb).
    /// </summary>
    [Test]
    public static void Farbvorschlag_KeinFesterFarbwertDerFarbstellungInDerOberflaeche()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        Pruefe.Wahr(d is not null, "Repository gefunden");
        var a = Farbvorschlaege.Anthrazit;
        var farben = new[] { a.Leitfarbe, a.LeitfarbeGedrueckt, a.Akzent, a.RotSchrift, a.SchliessenFlaeche, a.Glut };
        var muster = farben.SelectMany(c => new[]
        {
            string.Create(CultureInfo.InvariantCulture, $"{c.R:X2}{c.G:X2}{c.B:X2}"),
            string.Create(CultureInfo.InvariantCulture, $"0x{c.R:X2}, 0x{c.G:X2}, 0x{c.B:X2}"),
        }).ToList();
        var funde = new List<string>();
        var gezaehlt = 0;
        foreach (var projekt in new[] { "Funk.Client", "Funk.Verwaltung" })
        {
            var wurzel = Path.Combine(d!.FullName, "src", projekt);
            foreach (var datei in Directory.EnumerateFiles(wurzel, "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(wurzel, datei);
                if (rel.StartsWith("bin", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("obj", StringComparison.OrdinalIgnoreCase)) continue;
                if (!datei.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) && !datei.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel is @"Design\Farben.xaml" or @"Design\Palette.cs" or @"Design\Farbvorschlaege.cs") continue;
                gezaehlt++;
                var text = File.ReadAllText(datei);
                foreach (var m in muster)
                {
                    if (text.Contains(m, StringComparison.OrdinalIgnoreCase)) funde.Add(projekt + "\\" + rel + ": " + m);
                }
            }
        }
        Pruefe.Wahr(gezaehlt > 100, "die Suche findet die Quellen (" + gezaehlt + ")");
        Pruefe.Gleich(0, funde.Count, "fester Farbwert statt Rolle: " + string.Join(" | ", funde));
    }
}
