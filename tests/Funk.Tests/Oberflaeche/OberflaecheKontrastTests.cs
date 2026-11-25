// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows.Media;
using System.Xml.Linq;
using Funk.Client.Design;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Farbstellung Anthrazit (Sarevoc, 18./19.10.2026) und Kontrastregeln nach WCAG 2.1: Text mindestens 4,5:1, Bedienelemente, Rahmen
/// und Zustandsfarben mindestens 3:1. Dazu: Palette.cs und Farben.xaml stimmen überein.
/// </summary>
internal static class OberflaecheKontrastTests
{
    private static void Mindestens(double soll, Color vorn, Color hinten, string was)
    {
        var k = Kontrast.Verhaeltnis(vorn, hinten);
        Pruefe.Wahr(k >= soll, $"{was}: {k:0.00}:1, verlangt {soll}:1");
    }

    [Test]
    public static void Kontrast_GemesseneWerteDerFarbstellung()
    {
        static void Etwa(double erwartet, Color a, Color b, string was)
        {
            var k = Kontrast.Verhaeltnis(a, b);
            Pruefe.Wahr(Math.Abs(k - erwartet) < 0.06, $"{was}: {k:0.00}:1, festgehalten {erwartet}:1");
        }
        // Gemessen am 19.10.2026 (Farbvorschlaege.cs nennt dieselben Werte).
        Etwa(13.29, Palette.Text, Palette.Leitfarbe, "Weiß auf der Leitfarbe");
        Etwa(3.75, Palette.Akzent, Palette.Anthrazit, "Akzent Petrol auf Anthrazit");
        Etwa(5.59, Palette.RotSchrift, Palette.Anthrazit, "Rote Schrift auf Anthrazit");
        Etwa(15.3, Palette.Text, Palette.Anthrazit, "Schrift");
        Etwa(9.5, Palette.Text2, Palette.Anthrazit, "Schrift zweitrangig");
        Etwa(4.6, Palette.TextAus, Palette.Anthrazit, "Schrift deaktiviert");
        Pruefe.Wahr(Kontrast.Verhaeltnis(Palette.Leitfarbe, Palette.Anthrazit) < 1.6, "Leitfarbe auf Anthrazit nur 1,15:1: nie als Schrift oder Linie");
    }

    [Test]
    public static void Kontrast_TexteUndZustaende()
    {
        foreach (var (name, flaeche) in new[] { ("Anthrazit", Palette.Anthrazit), ("Fläche", Palette.Flaeche), ("Fläche tief", Palette.FlaecheTief) })
        {
            Mindestens(4.5, Palette.Text, flaeche, "Schrift auf " + name);
            Mindestens(4.5, Palette.Text2, flaeche, "Schrift zweitrangig auf " + name);
            Mindestens(4.5, Palette.RotSchrift, flaeche, "rote Schrift auf " + name);
            Mindestens(3, Palette.Akzent, flaeche, "Fokus und Rahmen auf " + name);
            Mindestens(3, Palette.Senden, flaeche, "Senden (grün) auf " + name);
            Mindestens(3, Palette.Hoeren, flaeche, "Hören (orange) auf " + name);
            Mindestens(3, Palette.TextAus, flaeche, "Rahmen Aus auf " + name);
            Mindestens(4.5, Palette.Senden, flaeche, "grüne Schrift ('Du sprichst') auf " + name);
        }
        Mindestens(4.5, Palette.AufFarbe, Palette.Senden, "SENDET auf Grün");
        Mindestens(4.5, Palette.AufFarbe, Palette.Hoeren, "HÖRT auf Orange");
        Mindestens(3, Palette.Senden, Palette.Leitfarbe, "grüner Rahmen auf Global");
        Mindestens(3, Palette.Hoeren, Palette.Leitfarbe, "orange Marke auf Global");
        Mindestens(4.5, Palette.Text2, Palette.Leitfarbe, "kleine Schrift auf Global");
        Mindestens(3, Palette.Text, Palette.SchliessenFlaeche, "Symbol Schließen auf seiner Fläche unter der Maus");
        Mindestens(4.5, Palette.Text, Palette.LeitfarbeGedrueckt, "Weiß auf der Leitfarbe gedrückt");
        Pruefe.Wahr(Kontrast.Verhaeltnis(Palette.Senden, Palette.Hoeren) < 3, "Grün und Orange sind allein nicht sicher unterscheidbar: darum Symbol und Text");
    }

    /// <summary>Farbe <paramref name="vorn"/> mit Deckkraft <paramref name="deckkraft"/> über <paramref name="hinten"/>, wie WPF sie mischt.</summary>
    private static Color Mischen(Color vorn, Color hinten, double deckkraft)
    {
        byte K(byte v, byte h) => (byte)Math.Round((v * deckkraft) + (h * (1 - deckkraft)));
        return Color.FromRgb(K(vorn.R, hinten.R), K(vorn.G, hinten.G), K(vorn.B, hinten.B));
    }

    [Test]
    public static void Kontrast_AusKachel_GedimmtAberLesbar()
    {
        // A17: Eine Kachel im Zustand AUS wird gedimmt. "AUS", "Ruhe", Taste und Lautstärke stehen in zweitrangiger
        // Schrift (12 und 11 Punkt) und müssen auch gedimmt 4,5:1 halten. Mit 0,5 waren es nur 3,3:1.
        var f = Palette.Flaeche;
        Mindestens(4.5, Mischen(Palette.Text2, f, Palette.AusDeckkraft), f, "zweitrangige Schrift auf der AUS-Kachel");
        Mindestens(4.5, Mischen(Palette.Text, f, Palette.AusDeckkraft), f, "Name auf der AUS-Kachel");
        Pruefe.Wahr(Palette.AusDeckkraft < 0.85, "die Kachel ist sichtbar gedimmt");
        Pruefe.Wahr(Kontrast.Verhaeltnis(Mischen(Palette.Text2, f, 0.5), f) < 4.5, "Gegenprobe: 0,5 wäre zu wenig");

        // Die Vorlage benutzt wirklich diesen Wert und keinen eigenen: deckend Palette.AusDeckkraft, durchsichtig die
        // Dimmung aus FunkfeldDeckung (OberflaecheFunkfeldDurchsichtTests).
        var xaml = File.ReadAllText(Path.Combine(Wurzel(), "src", "Funk.Client", "Fenster", "FunkfeldFenster.xaml"));
        Pruefe.Wahr(xaml.Contains("x:Key=\"Funkfeld.AusDeckkraft\" Member=\"d:Palette.AusDeckkraft\"", StringComparison.Ordinal), "FunkfeldFenster beginnt mit Palette.AusDeckkraft");
        Pruefe.Wahr(xaml.Contains("Value=\"{DynamicResource Funkfeld.AusDeckkraft}\"", StringComparison.Ordinal), "die Kachel nimmt die Dimmung aus der Ressource");
        Pruefe.Gleich(Palette.AusDeckkraft, FunkfeldDeckung.Deckend.AusDeckkraft, "deckend dimmt AUS wie bisher");
    }

    [Test]
    public static void Kontrast_GrenzeKleinerBedienelemente()
    {
        // Knopfrand, Reglerspur und Rand der Marke AUS im Funkfeld: 3:1 auf Kachel, Global (jede Farbstellung) und der
        // Titelzeile. TextAus #8D8D8D hatte auf dem früheren Global nur 2,98:1 und hält auf der Leitfarbe nur knapp darüber.
        Mindestens(3, Palette.Grenze, Palette.Flaeche, "Grenze auf der Kachel");
        Mindestens(3, Palette.Grenze, Palette.FlaecheTief, "Grenze in der Titelzeile");
        foreach (var s in Farbvorschlaege.Alle)
        {
            Mindestens(3, Palette.Grenze, s.Leitfarbe, "Grenze auf Global, " + s.Name);
            Pruefe.Wahr(Kontrast.Verhaeltnis(Palette.Grenze, s.Leitfarbe) > Kontrast.Verhaeltnis(Palette.TextAus, s.Leitfarbe) + 0.5,
                "die Grenze hebt sich auf Global deutlich stärker ab als TextAus, " + s.Name);
        }
        Mindestens(3, Mischen(Palette.Grenze, Palette.Flaeche, Palette.AusDeckkraft), Palette.Flaeche, "Rand der Marke AUS gedimmt");
    }

    [Test]
    public static void Kontrast_FokusUndGriffe()
    {
        // Fokusrahmen: im Akzent auf Anthrazit ja, auf der Titelleiste in der Leitfarbe weiß (der Akzent hielte dort nur knapp 3:1).
        Pruefe.Wahr(Kontrast.Verhaeltnis(Palette.Akzent, Palette.Leitfarbe) < 3.5, "Gegenprobe: Akzent auf der Leitfarbe nur knapp über 3:1 (3,25), darum dort weiß");
        Mindestens(3, Palette.Text, Palette.Leitfarbe, "weißer Fokus in der Titelleiste");
        var stile = File.ReadAllText(Path.Combine(Wurzel(), "src", "Funk.Client", "Design", "Stile.xaml"));
        var titel = stile[stile.IndexOf("x:Key=\"Knopf.Titel\"", StringComparison.Ordinal)..];
        titel = titel[..titel.IndexOf("</Style>", StringComparison.Ordinal)];
        Pruefe.Wahr(titel.Contains("Fokus.RahmenHell", StringComparison.Ordinal), "Titelknöpfe mit weißem Fokus");

        // Beschriftete Griffe der Schwellengrafik: weiße 10-Punkt-Schrift braucht 4,5:1.
        Pruefe.Wahr(Kontrast.Verhaeltnis(Palette.Text, Palette.Akzent) < 4.5, "Gegenprobe: weiß auf dem Akzent zu wenig für kleine Schrift");
        Mindestens(4.5, Palette.Text, Palette.Leitfarbe, "Griffschild weiß auf der Leitfarbe");
        Mindestens(3, Palette.Akzent, Palette.FlaecheTief, "Rand des Griffschilds auf der Grafik");
    }

    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new PruefungException("Repository nicht gefunden");
    }

    [Test]
    public static void Kontrast_PaletteGleichFarbenXaml()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        Pruefe.Wahr(d is not null, "Repository gefunden");
        var xaml = XDocument.Load(Path.Combine(d!.FullName, "src", "Funk.Client", "Design", "Farben.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var farben = xaml.Root!.Elements().Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => e.Value.Trim(), StringComparer.Ordinal);
        var palette = typeof(Palette).GetProperties().Where(p => p.PropertyType == typeof(Color)).ToList();
        Pruefe.Wahr(palette.Count >= 14, "Palette vollständig");
        foreach (var p in palette)
        {
            Pruefe.Wahr(farben.TryGetValue("Farbe." + p.Name, out var text), "Farbe." + p.Name + " in Farben.xaml");
            var c = (Color)p.GetValue(null)!;
            var soll = string.Create(CultureInfo.InvariantCulture, $"#FF{c.R:X2}{c.G:X2}{c.B:X2}");
            Pruefe.Gleich(soll, text!.ToUpperInvariant(), "Farbe." + p.Name);
        }
    }
}
