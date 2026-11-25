// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Glasdesign;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Glasdesign in JG TacRelay (0.6.0, Stufe 2): die feste Kopie unter extern\Glasdesign ist die aus HERKUNFT.md. Von 0.6.0 bis 0.11.1 trugen
/// Knöpfe und Reiter-Pillen Glas (Farbstellung der Gilde, Q8, Q15); seit 0.11.2 sind alle Knöpfe flach (Sarevoc 13.10.2026), Glasdesign liefert noch
/// Farbstellung, Hilfetexte und Tooltips. Die Schrift auf den Knöpfen hat in Anthrazit mindestens 4,5:1, gesperrt 3:1, und der Leuchtschalter
/// aus Joystick Guardian kommt nirgends vor (Sarevoc: "kein JG im Knopf").
/// </summary>
internal static class OberflaecheGlasTests
{
    private static string Kopie() => Path.Combine(EinrichtungTests.Repo(), "extern", "Glasdesign");

    [Test]
    public static void Glas_KopieGleichHerkunft()
    {
        var herkunft = File.ReadAllText(Path.Combine(Kopie(), "HERKUNFT.md"));
        var soll = Regex.Matches(herkunft, @"^\| `([^`]+)` \| (\d+) \| `([0-9a-f]{64})` \|\r?$", RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .ToDictionary(m => m.Groups[1].Value, m => (Laenge: long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), Sha: m.Groups[3].Value),
                StringComparer.Ordinal);
        Pruefe.Wahr(soll.Count >= 20, "HERKUNFT.md nennt die kopierten Dateien (" + soll.Count + ")");
        Pruefe.Wahr(herkunft.Contains("0.5.0-beta.13", StringComparison.Ordinal) && herkunft.Contains("0fd0663", StringComparison.Ordinal), "Fassung und Stand genannt");
        // Dass Weinrot nicht kopiert ist (Sarevoc, Q5, Q24), prüft die letzte Zeile: eingebaut sind nur Standard und Anthrazit. Die eine bewusste Abweichung
        // vom Blob (Thema.cs ohne den Eintrag der Tabelle früherer Namen) steht in HERKUNFT.md, ihre SHA-256 in der Tabelle; den alten Namen hält der
        // Namenswächter fern (NamenswaechterTests).
        var ist = Directory.EnumerateFiles(Kopie(), "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(Kopie(), d).Replace('\\', '/'))
            .Where(r => !r.StartsWith("bin/", StringComparison.Ordinal) && !r.StartsWith("obj/", StringComparison.Ordinal) && r != "HERKUNFT.md")
            .Order(StringComparer.Ordinal).ToList();
        Pruefe.Gleich(string.Join("\n", soll.Keys.Order(StringComparer.Ordinal)), string.Join("\n", ist), "genau die Dateien aus HERKUNFT.md, nichts dazu, nichts weg");
        foreach (var r in ist)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Kopie(), r.Replace('/', Path.DirectorySeparatorChar)));
            Pruefe.Gleich(soll[r].Laenge, bytes.LongLength, r + ": Länge wie bei der Übernahme");
            Pruefe.Gleich(soll[r].Sha, Convert.ToHexStringLower(SHA256.HashData(bytes)), r + ": SHA-256 wie bei der Übernahme");
        }
        var attribute = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), ".gitattributes"));
        Pruefe.Wahr(attribute.Contains("extern/** -text", StringComparison.Ordinal), ".gitattributes hält die Kopie Byte für Byte");
        Pruefe.Gleich(new Version(0, 5, 0, 0), typeof(Thema).Assembly.GetName().Version, "Glasdesign.dll trägt ihre eigene Fassung 0.5.0");
        Pruefe.Gleich("Anthrazit,Standard", string.Join(",", Thema.Eingebaut.Order(StringComparer.Ordinal)), "eingebaut sind Standard und Anthrazit");
    }

    /// <summary>
    /// Schlüssel, die Glasdesign 0.3.0 gestrichen hat (Außenlinie, Fuge, Rand der Schalterspur): JG TacRelay nennt sie nirgends,
    /// sonst fiele ein DynamicResource still auf nichts und ein StaticResource beim Laden um.
    /// </summary>
    [Test]
    public static void Glas_GestricheneSchluesselNirgends()
    {
        var gestrichen = GestricheneSchluessel;
        var funde = new List<string>();
        var gezaehlt = 0;
        foreach (var d in Directory.EnumerateFiles(Path.Combine(EinrichtungTests.Repo(), "src"), "*.*", SearchOption.AllDirectories))
        {
            if (Regex.IsMatch(d, @"[\\/](bin|obj)[\\/]", RegexOptions.None, TimeSpan.FromSeconds(1)) || Path.GetExtension(d) is not (".cs" or ".xaml")) continue;
            gezaehlt++;
            var text = File.ReadAllText(d);
            funde.AddRange(gestrichen.Where(s => text.Contains(s, StringComparison.Ordinal)).Select(s => Path.GetFileName(d) + ": " + s));
        }
        Pruefe.Wahr(gezaehlt > 100, "die Suche findet die Quellen (" + gezaehlt + ")");
        Pruefe.Gleich("", string.Join("\n", funde), "gestrichene Schlüssel in Funk.*");
        var schluessel = typeof(Schluessel).GetFields().Select(f => (string?)f.GetRawConstantValue()).ToHashSet(StringComparer.Ordinal);
        Pruefe.Falsch(gestrichen.Any(schluessel.Contains), "in Glasdesign gibt es sie nicht mehr");
        Pruefe.Wahr(schluessel.IsSupersetOf(NeueSchluessel), "die neuen Schlüssel sind da");
    }

    private static readonly string[] GestricheneSchluessel = ["Glas.Aussenlinie", "Glas.Fuge", "Glas.Spur.An.Rand", "Glas.Spur.Aus.Rand", "Glas.Spur.Gesperrt.Rand"];

    private static readonly bool[] ZuUndOffen = [false, true];

    private static readonly string[] NeueSchluessel = ["Glas.Kante.Haupt", "Glas.Spiegel.Farbe", "Glas.Spiegel.Haupt.Farbe"];

    // 0.11.2: Gelöscht sind Glas_SchattenNirgendsAngeschnitten (kein Glasknopf durfte seinen Schatten an einem Rand verlieren) und
    // Glas_Renderzeit_SchattenJeSeite (Renderzeit der Seiten je Schattenstufe der Glasknöpfe). Seit 0.11.2 gibt es keine Glasknöpfe mehr (Sarevoc 13.10.2026:
    // "mach überall die Glasbuttons raus, mach neutrale Darstellung"); beide prüften nur sie. Dass nirgends Glas übrig ist, prüft
    // Glas_KnoepfeUndReiterFlach_NirgendsGlas in jedem Bereich.

    /// <summary>App.xaml und der Bildlauf der Tests laden dieselben Wörterbücher in derselben Reihenfolge.</summary>
    [Test]
    public static void Glas_AppUndBildlaufLadenDasselbe()
    {
        var app = System.Xml.Linq.XDocument.Load(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "App.xaml"));
        var quellen = app.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute("Source") is not null)
            .Select(e => (string)e.Attribute("Source")!)
            .Select(s => s.StartsWith('/') ? "pack://application:,,," + s : "pack://application:,,,/TacRelay;component/" + s)
            .ToList();
        Pruefe.Gleich(string.Join("\n", Bildlauf.Woerterbuecher), string.Join("\n", quellen), "App.xaml und Bildlauf");
        Pruefe.Wahr(quellen.Any(q => q.EndsWith("Farbstellungen/Anthrazit.xaml", StringComparison.Ordinal)), "Farbstellung Anthrazit");
        Pruefe.Falsch(quellen.Any(q => q.EndsWith("/Glas.xaml", StringComparison.Ordinal)), "nicht Glas.xaml: keine Vorgabestile für CheckBox (Schalter bleiben flach)");
    }

    /// <summary>
    /// Karten mit mehr als einem Hauptknopf (Leitfarbe), die das dürfen, weil ihre Knöpfe nie zugleich zu sehen sind: Im Zweitfaktor
    /// steht erst "Geheimnis abholen" (bis es abgeholt ist), danach "Einrichtung abschließen" in genau einem von zwei Zweigen
    /// (NurErsterCode oder HatGeheimnis).
    /// </summary>
    private static readonly string[] MehrereHauptknoepfeNacheinander = ["VerwaltungAnsicht.xaml: ZweitfaktorKarte"];

    /// <summary>Q21: ein Hauptknopf (Leitfarbe) je Karte. Ein Knopf, der nur eine Rückfrage öffnet, ist neutral; die Leitfarbe trägt ihr "Ja".</summary>
    [Test]
    public static void Glas_HoechstensEinHauptknopfJeKarte()
    {
        var funde = new List<string>();
        foreach (var datei in Directory.EnumerateFiles(Path.Combine(EinrichtungTests.Repo(), "src"), "*.xaml", SearchOption.AllDirectories))
        {
            if (Regex.IsMatch(datei, @"[\\/](bin|obj)[\\/]", RegexOptions.None, TimeSpan.FromSeconds(1))) continue;
            var doc = System.Xml.Linq.XDocument.Load(datei);
            foreach (var karte in doc.Descendants().Where(e => (string?)e.Attribute("Style") == "{StaticResource Karte}"))
            {
                var haupt = karte.Descendants().Count(e => (string?)e.Attribute("Style") == "{StaticResource Knopf.Haupt}");
                if (haupt <= 1) continue;
                var name = (string?)karte.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name") ?? "ohne Namen";
                funde.Add(Path.GetFileName(datei) + ": " + name);
            }
        }
        Pruefe.Gleich(string.Join("\n", MehrereHauptknoepfeNacheinander), string.Join("\n", funde.Order(StringComparer.Ordinal)),
            "Karten mit mehr als einem Hauptknopf");
    }

    /// <summary>Sarevoc, 02.10.2026: "die JG-Schalter sind nicht für JG TacRelay, kein JG im Knopf". Sie bleiben in der Kopie, benutzt werden sie nicht.</summary>
    [Test]
    public static void Glas_KeinLeuchtschalterUndKeinJgZeichenInTacRelay()
    {
        var src = Path.Combine(EinrichtungTests.Repo(), "src");
        var verboten = new[] { "Leuchtschalter", "Leuchtzeichen", "Leuchtgeometrie", "Leuchtfarbe", "Glas.Leucht", "Glas.Leuchtet", "Glas.Wartet" };
        var funde = new List<string>();
        var gezaehlt = 0;
        foreach (var d in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
        {
            if (Regex.IsMatch(d, @"[\\/](bin|obj)[\\/]", RegexOptions.None, TimeSpan.FromSeconds(1))) continue;
            if (Path.GetExtension(d) is not (".cs" or ".xaml")) continue;
            gezaehlt++;
            var text = File.ReadAllText(d);
            foreach (var v in verboten)
                if (text.Contains(v, StringComparison.Ordinal)) funde.Add(Path.GetRelativePath(src, d) + ": " + v);
            if (Path.GetExtension(d) == ".xaml" && Regex.IsMatch(text, @"Content=""JG""|>JG<", RegexOptions.None, TimeSpan.FromSeconds(1)))
                funde.Add(Path.GetRelativePath(src, d) + ": JG im Knopf");
        }
        Pruefe.Wahr(gezaehlt > 100, "die Suche findet die Quellen (" + gezaehlt + ")");
        Pruefe.Gleich("", string.Join("\n", funde), "Leuchtschalter oder JG-Zeichen in Funk.*");
    }

    /// <summary>
    /// Seit 0.11.2 (Sarevoc 13.10.2026: "mach überall die Glasbuttons raus, mach neutrale Darstellung") sind alle Knöpfe flach: Knopf, Knopf.Haupt,
    /// Umschalter und Umschalter.Radio bauen auf Knopf.Flach, die Hauptaktion flach in der Leitfarbe mit Akzentrand, ein gewählter Umschalter in der
    /// gedrückten Leitfarbe mit Akzentrand. Kein Glaskörper mehr, weder in einem einzelnen Knopf noch irgendwo im Funkfeld, auf dem Brett oder in einem Bereich der
    /// Navigation (einmal zu, einmal mit offenem Wer ist da). Bis 0.11.1 prüfte dieser Test das Gegenteil (Knöpfe und Reiter aus Glas, Q8, Q15).
    /// </summary>
    [Test]
    public static void Glas_KnoepfeUndReiterFlach_NirgendsGlas()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var r = Application.Current!.Resources;
            Pruefe.Gleich("Anthrazit", Thema.Aktuell(r), "Farbstellung Anthrazit von Glasdesign");
            Style Stil(string s) => (Style)r[s];
            T Gezeichnet<T>(T e) where T : FrameworkElement
            {
                e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                e.Arrange(new Rect(e.DesiredSize));
                e.UpdateLayout();
                return e;
            }
            bool Flach(ButtonBase k) => !Bildlauf.Nachfahren<Glaskoerper>(Gezeichnet(k)).Any() && k.Template?.FindName("Schleier", k) is Border;
            Color Farbe(string s) => ((SolidColorBrush)r[s]).Color;
            Color Grund(Control k) => ((SolidColorBrush)k.Background).Color;
            Color Rand(Control k) => ((SolidColorBrush)k.BorderBrush).Color;

            var ohneStil = new Button { Content = "Funkfeld 10" };
            Pruefe.Wahr(Flach(ohneStil), "Button ohne Stil: flach wie Knopf");
            var knopf = new Button { Style = Stil("Knopf"), Content = "Trennen" };
            Pruefe.Wahr(Flach(knopf) && Grund(knopf) == Farbe("Pinsel.Flaeche"), "Knopf: flach und neutral");
            var haupt1 = new Button { Style = Stil("Knopf.Haupt"), Content = "Verbinden" };
            Pruefe.Wahr(Flach(haupt1) && Grund(haupt1) == Farbe("Pinsel.Leitfarbe") && Rand(haupt1) == Farbe("Pinsel.Akzent"), "Knopf.Haupt: flach in der Leitfarbe mit Akzentrand");
            var umschalter = new ToggleButton { Style = Stil("Umschalter"), Content = "VA" };
            Pruefe.Wahr(Flach(umschalter) && Grund(umschalter) == Farbe("Pinsel.Flaeche"), "Umschalter: flach und neutral");
            var gewaehlt = new ToggleButton { Style = Stil("Umschalter"), Content = "VA", IsChecked = true };
            Pruefe.Wahr(Flach(gewaehlt) && Grund(gewaehlt) == Farbe("Pinsel.LeitfarbeGedrueckt") && Rand(gewaehlt) == Farbe("Pinsel.Akzent"),
                "Umschalter gewählt: gedrückte Leitfarbe mit Akzentrand");
            var radio = new RadioButton { Style = Stil("Umschalter.Radio"), Content = "Headset", IsChecked = true };
            Pruefe.Wahr(Flach(radio) && Grund(radio) == Farbe("Pinsel.LeitfarbeGedrueckt"), "Umschalter.Radio gewählt: gedrückte Leitfarbe");
            Pruefe.Wahr(Flach(new Funk.Client.Steuerelemente.Halteknopf { Style = Stil("Knopf"), Content = "Sprechtaste halten" }), "Halteknopf: flach");
            Pruefe.Falsch(Bildlauf.Nachfahren<Glaskoerper>(Gezeichnet(new Button { Style = Stil("Knopf.Titel"), Content = "x" })).Any(), "Titelleiste flach");
            Pruefe.Falsch(Bildlauf.Nachfahren<Glaskoerper>(Gezeichnet(new Button { Style = Stil("Knopf.Schliessen"), Content = "x" })).Any(), "Schließen flach");

            // Das Funkfeld (Knöpfe wie VA/Taste, Aus, Mehrere senden waren seit Stufe 3.4 Glas) und das Brett: nirgends Glas.
            using (var f = new Aufbau(OberflaecheFunkfeldBilderTests.Szene()))
            {
                using var ffModell = f.Funkfeld();
                var ff = new FunkfeldFenster(ffModell, f.Ablage);
                fenster.Add(ff);
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Wahr(Bildlauf.Nachfahren<ButtonBase>(Bildlauf.Wurzel(ff)).Count() > 5, "Knöpfe im Funkfeld gezeichnet");
                Pruefe.Falsch(Bildlauf.Nachfahren<Glaskoerper>(Bildlauf.Wurzel(ff)).Any(), "kein Glas im Funkfeld");
            }
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkDienstAttrappe.Raidabend());
            var dienst = new VerwaltungsdienstAttrappe();
            using var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
            a.Verwaltung = teil;
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Funk.Protokoll.Steuerung.Stufe.Admin, Admin = Funk.Protokoll.Steuerung.AdminStatus.Aktiv } });
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            var knoepfe = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var offen in ZuUndOffen)
            {
                h.WerIstDa.Offen = offen;
                foreach (var bereich in h.Navigation.ToList())
                {
                    h.AktiverBereich = bereich;
                    _ = Bildlauf.Zeichnen(haupt, 1, new Size(1400, 900));
                    var wurzel = Bildlauf.Wurzel(haupt);
                    var was = bereich.Titel + (offen ? ", Wer ist da offen" : "");
                    knoepfe[bereich.Kennung] = Bildlauf.Nachfahren<ButtonBase>(wurzel).Count(Ansichtspruefung.ZuSehen);
                    Pruefe.Falsch(Bildlauf.Nachfahren<Glaskoerper>(wurzel).Any(), was + ": kein Glas");
                }
            }
            // Seit 0.10.0 (AP3, Q87) fünf: Start, Einstellungen, Sprachaktivierung, Raid, Verwaltung.
            Pruefe.Wahr(knoepfe.Count >= 5 && knoepfe.Values.All(n => n > 0), "Knöpfe in jedem Bereich gezeichnet: " + string.Join(", ", knoepfe.Select(g => g.Key + " " + g.Value)));
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// Schrift auf jedem Knopf von JG TacRelay, in jedem Zustand und auf der Kartenfläche jeder Farbstellung: wie im
    /// Glasdesign-Projekt an gerenderten Pixeln (vierfach), dreimal gezeichnet (echte, weiße, unsichtbare Schrift); gezählt
    /// werden nur Pixel, die die Schrift zu mindestens 95 % deckt, samt ihren vier Nachbarn. Dazu der Rand des Knopfs gegen die
    /// Fläche (mindestens 3:1, damit er sich von ihr abhebt). Werte in der Messdatei glas-kontrast.txt (Messdateien).
    /// <para>
    /// Seit 0.11.2 sind die Knöpfe flach (Knopf.Flach) statt aus Glas. Überfahren und Drücken legen dort einen Schleier über die Fläche, der nur mit
    /// der Maus entsteht (die Zustandsvorschau von Glasdesign wirkt nicht mehr); gemessen werden deshalb normal und gesperrt. Gesperrt zeichnet
    /// Knopf.Flach die ganze Fläche zu 70 % (Opacity am Rand): Dort deckt die Schrift höchstens 70 %, gezählt wird ab 65 %. Verlangt werden 4,5:1
    /// und der Rand 3:1 für jeden bedienbaren Knopf; gesperrt wird nur gemessen und in die Messdatei geschrieben, wie überall sonst im Programm
    /// ausgenommen (WCAG 1.4.3, inaktive Komponenten, Ansichtspruefung.KontrastPruefen). Die Glasknöpfe bis 0.11.1 hielten gesperrt 3:1; die flachen
    /// liegen bei etwa 2,7:1 (offene Frage an Sarevoc).
    /// </para>
    /// </summary>
    [Test]
    public static void Glas_SchriftUndRand_InAnthrazit()
    {
        var zeilen = new List<string> { "Farbstellung; Knopf; Zustand; Schrift kleinster Kontrast; Pixel; Rand gegen Fläche" };
        var fehler = new List<string>();
        Bildlauf.Ausfuehren(_ =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var r = Application.Current!.Resources;
                Style Stil(string s) => (Style)r[s];
                foreach (var (name, erzeugen) in new (string, Func<Control>)[]
                         {
                             ("Knopf", () => new Button { Style = Stil("Knopf"), Content = "Wer ist da" }),
                             ("Knopf.Haupt", () => new Button { Style = Stil("Knopf.Haupt"), Content = "Verbinden" }),
                             ("Umschalter", () => new ToggleButton { Style = Stil("Umschalter"), Content = "Sprechtaste" }),
                             ("Umschalter gewählt", () => new ToggleButton { Style = Stil("Umschalter"), Content = "Sprachaktivierung", IsChecked = true }),
                             ("Umschalter.Radio gewählt", () => new RadioButton { Style = Stil("Umschalter.Radio"), Content = "Lautsprecher", IsChecked = true }),
                         })
                {
                    foreach (var (zustand, gesperrt) in new[] { ("normal", false), ("gesperrt", true) })
                    {
                        var knopf = erzeugen();
                        knopf.IsEnabled = !gesperrt;
                        var (k, n, rand) = Glaskontrast.Messen(knopf, ((SolidColorBrush)r["Pinsel.Flaeche"]).Color, gesperrt ? 0.65 : 0.95);
                        var zeile = string.Create(CultureInfo.InvariantCulture, $"{satz.Name}; {name}; {zustand}; {k:0.00}; {n}; {rand:0.00}");
                        zeilen.Add(zeile + (gesperrt ? " (nur gemessen)" : ""));
                        if (gesperrt) continue;
                        if (n < 20) fehler.Add(zeile + " (zu wenig voll gedeckte Pixel)");
                        else if (k < 4.5) fehler.Add(zeile + " Schrift unter 4.5");
                        if (rand < 3) fehler.Add(zeile + " Rand unter 3:1");
                    }
                }
            }
        });
        Messdateien.Schreiben("glas-kontrast.txt", zeilen);
        Pruefe.Gleich("", string.Join("\n", fehler), "Kontrast auf Glas");
    }

    /// <summary>
    /// Die Messung schlägt an: Schrift fast in der Farbe der Knopffläche ergibt einen Wert nahe 1. Seit 0.11.2 ist der Knopf flach, die Schrift kommt
    /// aus Foreground (bis 0.11.1 aus dem Schlüssel Glas.Schrift des Glaskörpers).
    /// </summary>
    [Test]
    public static void Glas_Gegenprobe_MessungErkenntSchlechtenKontrast()
    {
        Bildlauf.Ausfuehren(_ =>
        {
            var r = Application.Current!.Resources;
            var knopf = new Button { Style = (Style)r["Knopf"], Content = "Wer ist da" };
            var grund = ((SolidColorBrush)r["Pinsel.Flaeche"]).Color;
            knopf.Foreground = new LinearGradientBrush(grund, Color.FromRgb((byte)Math.Min(255, grund.R + 1), (byte)Math.Min(255, grund.G + 1), (byte)Math.Min(255, grund.B + 1)), 90);
            var (k, n, _) = Glaskontrast.Messen(knopf, grund);
            Pruefe.Wahr(n >= 20, "Pixel gemessen");
            Pruefe.Wahr(k < 2, "schlechter Kontrast wird erkannt: " + k.ToString("0.00", CultureInfo.InvariantCulture));
        });
    }
}

/// <summary>Schriftkontrast auf Glas an gerenderten Pixeln, nach dem Verfahren der Glasdesign-Tests (KontrastTests).</summary>
internal static class Glaskontrast
{
    private sealed record Bild(int Breite, int Hoehe, byte[] Bytes)
    {
        public Color this[int x, int y]
        {
            get
            {
                var i = ((y * Breite) + x) * 4;
                return Color.FromRgb(Bytes[i + 2], Bytes[i + 1], Bytes[i]);
            }
        }
    }

    private static Bild Lesen(RenderTargetBitmap b)
    {
        var bytes = new byte[b.PixelWidth * b.PixelHeight * 4];
        b.CopyPixels(bytes, b.PixelWidth * 4, 0);
        return new Bild(b.PixelWidth, b.PixelHeight, bytes);
    }

    /// <summary>
    /// Kleinster Schriftkontrast, Zahl der gemessenen Pixel und der stärkste Kontrast des Knopfrands gegen <paramref name="flaeche"/>. Gezählt werden
    /// Pixel, die die Schrift samt ihren vier Nachbarn mindestens zu <paramref name="deckungAb"/> deckt (ab Werk 95 %).
    /// </summary>
    public static (double Schrift, int Pixel, double Rand) Messen(Control knopf, Color flaeche, double deckungAb = 0.95)
    {
        const double skala = 4;
        const double luft = 6;
        var rahmen = new Border { Background = new SolidColorBrush(flaeche), Padding = new Thickness(luft), Child = knopf };
        Glas.SetSchatten(rahmen, Schattenstufe.Aus);
        var echt = Lesen(OberflaecheHilfeTests.Zeichnen(rahmen, skala));
        knopf.Foreground = new LinearGradientBrush(Colors.White, Color.FromRgb(254, 254, 254), 90);
        var weiss = Lesen(OberflaecheHilfeTests.Zeichnen(rahmen, skala));
        knopf.Foreground = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(1, 0, 0, 0), 90);
        var grund = Lesen(OberflaecheHilfeTests.Zeichnen(rahmen, skala));
        knopf.ClearValue(Control.ForegroundProperty);

        double Deckung(int x, int y)
        {
            var g = grund[x, y];
            var w = weiss[x, y];
            double D(byte wk, byte gk) => gk >= 250 ? 0 : (wk - gk) / (255.0 - gk);
            return Math.Min(D(w.R, g.R), Math.Min(D(w.G, g.G), D(w.B, g.B)));
        }

        var kleinster = double.MaxValue;
        var n = 0;
        for (var y = 1; y < echt.Hoehe - 1; y++)
            for (var x = 1; x < echt.Breite - 1; x++)
            {
                if (Deckung(x, y) < deckungAb || Deckung(x - 1, y) < deckungAb || Deckung(x + 1, y) < deckungAb || Deckung(x, y - 1) < deckungAb || Deckung(x, y + 1) < deckungAb) continue;
                kleinster = Math.Min(kleinster, Kontrast.Verhaeltnis(echt[x, y], grund[x, y]));
                n++;
            }

        // Der Rand: die Pixel im äußeren Ring des Knopfs (2 Einheiten breit), der hellste Kontrast gegen die Fläche.
        var rand = 1.0;
        var links = (int)(luft * skala);
        var oben = (int)(luft * skala);
        var rechts = grund.Breite - 1 - (int)(luft * skala);
        var unten = grund.Hoehe - 1 - (int)(luft * skala);
        var ring = (int)(2 * skala);
        for (var y = oben; y <= unten; y++)
            for (var x = links; x <= rechts; x++)
            {
                if (x - links >= ring && rechts - x >= ring && y - oben >= ring && unten - y >= ring) continue;
                rand = Math.Max(rand, Kontrast.Verhaeltnis(grund[x, y], flaeche));
            }
        return (n == 0 ? 0 : kleinster, n, rand);
    }
}
