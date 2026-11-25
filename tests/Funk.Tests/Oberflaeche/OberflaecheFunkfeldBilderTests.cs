// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Maschine;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Funkfeld kompakt im Speicher gezeichnet (<see cref="Bildlauf"/>, ohne Fensterhandle, auch neben einem Spiel): gemessene
/// Größen der Ansichten Raster und Liste (A29) gegen die Ziele (Raster höchstens 800 x 222), Global als letztes Feld, Kürzen langer
/// Namen, Schrift ab 11, Rahmen (weder minimierbar noch maximierbar), Platz je Ansicht und die drei Farbstellungen bis in
/// Hauptfenster und Schwellengrafik. Mit <c>FUNK_BILDER</c> entstehen die Vergleichsbilder für Sarevoc (funkfeld-ansichten.png,
/// auch bei 175 %, farben.png) und die Funkfeld-Bilder für docs\bilder. Durchsichtigkeit: OberflaecheFunkfeldDurchsichtTests.
/// </summary>
internal static class OberflaecheFunkfeldBilderTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    internal static readonly Color Grund = Color.FromRgb(0x3C, 0x42, 0x4C);
    internal static readonly Color Grau = Color.FromRgb(0xB8, 0xBE, 0xC8);

    /// <summary>
    /// Ein Name in der Höchstlänge str(32) zum Kürzen. Bis 0.8.0 "Logistik Nachschub Hangar Ost" (29 Zeichen); seit 0.8.1 fehlen am Namen
    /// Pfeil und Rand der Kanalwahl (Q44), er hat etwa 11 Einheiten mehr Platz, und der Test soll nicht vom Zufall der Schriftbreite abhängen.
    /// </summary>
    private const string LangerName = "Logistik Nachschub Hangar Ost 12";
    private static readonly int[] Feldzahlen = [10, 5];
    private static readonly double[] Skalen = [1.0, 1.75];
    private static readonly WindowState[] Gesperrt = [WindowState.Maximized, WindowState.Minimized];

    /// <summary>Höchstgrößen in Einheiten: Raster höchstens 800 x 222, Liste etwa 288 breit (264 seit Stufe 3 mit Rolle in einer zweiten Kopfzeile, 288 seit Schritt 3.5 mit VA/Taste je Feld).</summary>
    private static readonly Dictionary<(FunkfeldDarstellung, int), Size> Hoechstens = new()
    {
        [(FunkfeldDarstellung.Raster, 10)] = new Size(800, 220),
        [(FunkfeldDarstellung.Raster, 5)] = new Size(800, 115),
        [(FunkfeldDarstellung.Liste, 10)] = new Size(294, 345),
        [(FunkfeldDarstellung.Liste, 5)] = new Size(294, 200),
    };

    /// <summary>
    /// Lebendszene: Crew SENDET (Kobold spricht), Kommando und Jäger HÖREN mit Sprecher, weitere hören, Durchsagen ist AUS,
    /// Global hört; ein langer Name zum Kürzen.
    /// </summary>
    internal static FunkLage Szene()
    {
        var b = FunkDienstAttrappe.Beispiel();
        return b with
        {
            Netze = [.. b.Netze.Select(n => n.Id == 9 ? n with { Name = LangerName } : n)],
            Felder = [.. b.Felder.Select(f => f.Feld switch { 1 => f with { Hoeren = false }, 4 or 6 => f with { Hoeren = true }, _ => f })],
            Sprecher = [new SprecherInfo(2, "Falke", [2], false), new SprecherInfo(3, "Kobold", [4], false), new SprecherInfo(7, "Sturmvogel", [6], false)],
        };
    }

    /// <summary>
    /// Szene der Vorführung (--vorfuehrung, Namen erfunden) wie auf den alten Bildern in docs\bilder: Crew SENDET und
    /// Kobold spricht dort, Jäger ist AUS, Sturmvogel spricht darin, Falke in Kommando.
    /// </summary>
    internal static FunkLage Vorfuehrungsszene()
    {
        var b = FunkDienstAttrappe.Beispiel();
        return b with { Sprecher = [new SprecherInfo(2, "Falke", [2], false), new SprecherInfo(3, "Kobold", [4], false), new SprecherInfo(7, "Sturmvogel", [6], false)] };
    }

    internal static FunkfeldViewModel Lebendig(Aufbau a)
    {
        var f = a.Funkfeld();
        Pegel(a, f);
        return f;
    }

    internal static void Pegel(Aufbau a, FunkfeldViewModel f)
    {
        a.Maschine.EmpfangsPegelMelden(2, -24);
        a.Maschine.EmpfangsPegelMelden(4, -12);
        a.Maschine.EmpfangsPegelMelden(6, -30);
        // Der Takt meldet höchstens alle 66 ms; die Uhr des Aufbaus springt, damit jeder Aufruf hier zählt.
        a.Uhr += FunkfeldViewModel.PegelAbstandMs;
        f.Takt();
    }

    private static string Name(FunkfeldDarstellung d) => d == FunkfeldDarstellung.Raster ? "A Raster" : "C Liste";

    /// <summary>Die Farben, die ein Bildlauf zu Beginn trägt: Anthrazit wie im Programm.</summary>
    internal static Farbsatz Voreinstellung => Farbvorschlaege.Von(Farbvorschlaege.Voreinstellung);

    [Test]
    public static void Funkfeld_Ansichten_GroessenGlobalKuerzenUndBild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(Szene());
            var f = Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            Pruefe.Wahr(ff.AllowsTransparency && ff.WindowStyle == WindowStyle.None, "immer geschichtet (A33), ohne Rahmen");
            Pruefe.Wahr(ff.Topmost && !ff.ShowInTaskbar && !ff.ShowActivated, "immer vorn, ohne Fokus beim Öffnen (A24)");
            Pruefe.Gleich(SizeToContent.Manual, ff.SizeToContent, "kein SizeToContent: die optimale Größe fest gesetzt (A32)");

            var bilder = new Dictionary<(FunkfeldDarstellung, int), (RenderTargetBitmap Klein, RenderTargetBitmap Gross, Size Groesse)>();
            foreach (var d in Enum.GetValues<FunkfeldDarstellung>())
            {
                foreach (var n in Feldzahlen)
                {
                    f.Darstellung = d;
                    f.Ansicht = n;
                    Pegel(a, f);
                    var (klein, groesse) = Bildlauf.Zeichnen(ff);
                    var (gross, _) = Bildlauf.Zeichnen(ff, 1.75);
                    var was = $"{Name(d)}, {n} Felder";
                    Console.WriteLine(string.Create(De, $"MASS Funkfeld {was}: {groesse.Width:0} x {groesse.Height:0} Einheiten, bei 175 % {gross.PixelWidth} x {gross.PixelHeight} Bildpunkte"));
                    var hoechstens = Hoechstens[(d, n)];
                    Pruefe.Wahr(groesse.Width <= hoechstens.Width && groesse.Height <= hoechstens.Height,
                        string.Create(De, $"{was}: {groesse.Width:0} x {groesse.Height:0}, höchstens {hoechstens.Width} x {hoechstens.Height}"));
                    GlobalAmEnde(ff, d, n, was);
                    SchriftMindestens11(ff, was);
                    if (n == 10) Gekuerzt(ff, was);
                    bilder[(d, n)] = (klein, gross, groesse);
                }
            }
            var raster = bilder[(FunkfeldDarstellung.Raster, 10)].Groesse;
            var liste = bilder[(FunkfeldDarstellung.Liste, 10)].Groesse;
            Pruefe.Wahr(liste.Width < 0.40 * raster.Width && liste.Height > liste.Width, "die Liste steht senkrecht und schmal");
            Pruefe.Wahr(raster.Width * raster.Height < 0.45 * 1640 * 226, "Raster kleiner als die Hälfte des alten Funkfelds (1640 x 226)");

            foreach (var skala in Skalen)
                Bildablage.Ablegen(AnsichtenBogen(bilder, skala), skala == 1 ? "funkfeld-ansichten" : "funkfeld-ansichten-175");
            f.Dispose();
        });
    }

    /// <summary>Global (A23) ist das letzte Feld: im Raster rechts unten, in der Liste unten.</summary>
    private static void GlobalAmEnde(FunkfeldFenster ff, FunkfeldDarstellung d, int n, string was)
    {
        var raster = Bildlauf.Nachfahren<Kachelraster>(Bildlauf.Wurzel(ff)).Single();
        var kinder = raster.Children.Cast<FrameworkElement>().ToList();
        Pruefe.Gleich(n, kinder.Count, was + ": Felder");
        Pruefe.Wahr(kinder[^1].DataContext is KachelViewModel { IstGlobal: true }, was + ": Global ist das letzte Feld");
        var orte = kinder.Select(k => VisualTreeHelper.GetOffset(k)).ToList();
        var g = orte[^1];
        Pruefe.Wahr(orte.All(o => o.Y <= g.Y + 0.5), was + ": Global in der untersten Reihe");
        var reihen = orte.Select(o => Math.Round(o.Y)).Distinct().Count();
        if (d == FunkfeldDarstellung.Liste)
        {
            Pruefe.Gleich(n, reihen, was + ": eine Zeile je Feld");
            Pruefe.Wahr(orte.All(o => Math.Abs(o.X - g.X) < 0.5), was + ": alle in einer Spalte");
        }
        else
        {
            Pruefe.Gleich(n == 10 ? 2 : 1, reihen, was + (n == 10 ? ": 2 Reihen zu 5" : ": eine Reihe"));
            Pruefe.Wahr(orte.All(o => o.X <= g.X + 0.5), was + ": Global rechts außen");
        }
    }

    /// <summary>Keine Schrift unter 11 Einheiten (Symbolschrift der Titelknöpfe ausgenommen).</summary>
    private static void SchriftMindestens11(FunkfeldFenster ff, string was)
    {
        foreach (var t in Bildlauf.Nachfahren<TextBlock>(Bildlauf.Wurzel(ff)))
        {
            if (t.FontFamily.Source.Contains("Icons", StringComparison.Ordinal) || t.FontFamily.Source.Contains("MDL2", StringComparison.Ordinal)) continue;
            Pruefe.Wahr(t.FontSize >= 11, string.Create(De, $"{was}: Schrift {t.FontSize} für '{t.Text}'"));
        }
    }

    /// <summary>Lange Namen werden mit Auslassungspunkten gekürzt, nicht umbrochen.</summary>
    private static void Gekuerzt(FunkfeldFenster ff, string was)
    {
        var namen = Bildlauf.Nachfahren<TextBlock>(Bildlauf.Wurzel(ff)).Where(t => t.Name == "NameText").ToList();
        Pruefe.Wahr(namen.Count >= 9, was + ": Namen gefunden");
        var lang = namen.Single(t => t.Text == LangerName);
        var voll = new FormattedText(lang.Text, De, FlowDirection.LeftToRight, new Typeface(lang.FontFamily, lang.FontStyle, lang.FontWeight, lang.FontStretch),
            lang.FontSize, Brushes.White, 1).Width;
        Pruefe.Gleich(TextTrimming.CharacterEllipsis, lang.TextTrimming, was + ": Auslassungspunkte");
        Pruefe.Gleich(TextWrapping.NoWrap, lang.TextWrapping, was + ": kein Umbruch");
        Pruefe.Wahr(lang.ActualWidth + 1 < voll, string.Create(De, $"{was}: gekürzt ({lang.ActualWidth:0} von {voll:0})"));
        Pruefe.Wahr(lang.ActualHeight < lang.FontSize * 1.8, was + ": eine Zeile");
        foreach (var t in namen) Pruefe.Wahr(t.ActualHeight < t.FontSize * 1.8, was + ": eine Zeile für " + t.Text);
    }

    private static RenderTargetBitmap AnsichtenBogen(Dictionary<(FunkfeldDarstellung, int), (RenderTargetBitmap Klein, RenderTargetBitmap Gross, Size Groesse)> bilder, double skala)
    {
        const double rand = 24;
        const double zielB = 800;
        const double zielH = 222;
        const double block = 22 + zielH + 30;
        var rechts = rand + zielB + 48;
        var listeB = bilder[(FunkfeldDarstellung.Liste, 10)].Groesse.Width;
        var listeH = bilder[(FunkfeldDarstellung.Liste, 10)].Groesse.Height;
        var b = new Bogen(rechts + listeB + 24 + listeB + rand, Math.Max(70 + (2 * block), 112 + listeH + 30) + 110, skala, Grund);
        b.Text("Funkfeld - Ansicht A Raster (Voreinstellung) und C Liste, je 10 und 5 Felder", rand, 16, 20, fett: true);
        b.Text("Szene: Crew SENDET (Kobold spricht), Kommando und Jäger HÖREN mit Sprecher, Durchsagen AUS, Global hört. Gestrichelt: das Ziel für das 10er-Feld, 800 x 222 Einheiten (bei 175 % 1400 x 388 Bildpunkte).",
            rand, 44, 12, Grau);
        var y = 70.0;
        foreach (var n in Feldzahlen)
        {
            var e = bilder[(FunkfeldDarstellung.Raster, n)];
            b.Text(string.Create(De, $"A Raster - {n} Felder: {e.Groesse.Width:0} x {e.Groesse.Height:0} Einheiten"), rand, y, 14, fett: true);
            var ort = new Rect(rand, y + 22, zielB, zielH);
            b.Bild(skala == 1 ? e.Klein : e.Gross, ort.X, ort.Y, e.Groesse.Width, e.Groesse.Height);
            b.Rahmen(ort, Grau, 1.5, gestrichelt: true);
            b.Text("Ziel 800 x 222", ort.Right - 118, ort.Bottom - 20, 12, Grau);
            y += block;
        }
        var x = rechts;
        foreach (var n in Feldzahlen)
        {
            var e = bilder[(FunkfeldDarstellung.Liste, n)];
            b.Text(string.Create(De, $"C Liste - {n} Felder"), x, 70, 14, fett: true);
            b.Text(string.Create(De, $"{e.Groesse.Width:0} x {e.Groesse.Height:0} Einheiten"), x, 90, 12, Grau);
            b.Bild(skala == 1 ? e.Klein : e.Gross, x, 112, e.Groesse.Width, e.Groesse.Height);
            // Die Höhe des Ziels zum Vergleich neben der Liste
            b.Rahmen(new Rect(x, 112, e.Groesse.Width, zielH), Grau, 1, gestrichelt: true);
            x += listeB + 24;
        }
        var unten = Math.Max(70 + (2 * block), 112 + listeH + 30);
        b.Text("Bedienung: Klick wählt das Sendenetz, Strg+Klick nimmt eins dazu, Tasten 1-9 und 0 (Global), + und - schalten weiter.", rand, unten, 12, Grau);
        b.Text("Liste: Lautstärke und VA/Taste stehen unter Netze und Rollen. Titelzeile: Regler, Ansicht Raster oder Liste, Schiebeschalter 5/10, Schließen;", rand, unten + 18, 12, Grau);
        b.Text("ziehen an jeder freien Fläche. Es startet gerade so groß, dass alles zu sehen ist, und merkt sich je Ansicht Lage und Größe, so wie du es einstellst.", rand, unten + 36, 12, Grau);
        b.Text("Neben der Liste gestrichelt die Höhe des Ziels (222).", rand, unten + 54, 12, Grau);
        return b.Fertig();
    }

    /// <summary>
    /// Die Titelzeile (Sendenetze) bestimmt die Breite nicht mit, gemessen wie im Fenster, das sich dem Inhalt anpasst:
    /// mit freier Breite messen, in der Wunschgröße anordnen. Vorher wuchs die Liste mit "Senden: Logistik Nachschub
    /// Hangar" von 240 auf 348 und mit vier Sendenetzen auf 470 Einheiten, am rechten Bildschirmrand aus dem Bild.
    /// </summary>
    [Test]
    public static void Funkfeld_Kopfzeile_BestimmtDieBreiteNicht()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var breiten = new Dictionary<FunkfeldDarstellung, double>();
            foreach (var sende in new byte[][] { [2], [7], [0, 2, 7] })
            {
                using var a = new Aufbau(Szene() with { SendeFelder = sende, GlobalSenden = sende.Length > 1 });
                var f = Lebendig(a);
                var ff = new FunkfeldFenster(f, a.Ablage);
                fenster.Add(ff);
                foreach (var d in Enum.GetValues<FunkfeldDarstellung>())
                {
                    f.Darstellung = d;
                    var natur = WieImFenster(ff);
                    var was = string.Create(De, $"{Name(d)}, Kopfzeile '{f.Kopfzeile}'");
                    if (breiten.TryGetValue(d, out var erst))
                        Pruefe.Wahr(Math.Abs(natur.Width - erst) < 0.5, string.Create(De, $"{was}: {natur.Width:0} breit, mit einem Sendenetz {erst:0}"));
                    else
                        breiten[d] = natur.Width;
                    var kopf = ff.KopfText;
                    Pruefe.Gleich(f.Kopfzeile, kopf.Text, was + ": Text");
                    var voll = new FormattedText(kopf.Text, De, FlowDirection.LeftToRight, new Typeface(kopf.FontFamily, kopf.FontStyle, kopf.FontWeight, kopf.FontStretch),
                        kopf.FontSize, Brushes.White, 1).Width;
                    // Platz, der der Titelzeile bleibt: bei kurzem Text der ganze Text, bei langem so viel wie da ist, gekürzt.
                    Pruefe.Wahr(kopf.ActualWidth >= Math.Min(voll, 60) - 0.5, string.Create(De, $"{was}: {kopf.ActualWidth:0} Einheiten für den Text"));
                    Pruefe.Wahr(Bildlauf.Wurzel(ff).ActualWidth <= natur.Width + 0.5, was + ": angeordnet in der Wunschbreite");                }
                f.Dispose();
            }
            Pruefe.Wahr(breiten[FunkfeldDarstellung.Liste] <= 294, string.Create(De, $"die Liste bleibt schmal: {breiten[FunkfeldDarstellung.Liste]:0}"));

            // Live wie im Programm: Die Wahl ändert sich im stehenden Fenster, nur das Layout läuft nach.
            using (var a = new Aufbau(Szene()))
            {
                var f = Lebendig(a);
                var ff = new FunkfeldFenster(f, a.Ablage);
                fenster.Add(ff);
                Bildlauf.Leeren();
                f.Darstellung = FunkfeldDarstellung.Liste;
                var vorher = WieImFenster(ff);
                foreach (var k in f.Kacheln.Where(k => k.KannSenden && !k.IstSenden).ToList())
                    f.SendenWaehlenAsync(k, hinzufuegen: true).GetAwaiter().GetResult();
                var w = Bildlauf.Wurzel(ff);
                w.UpdateLayout();
                Bildlauf.Leeren();
                w.UpdateLayout();
                LayoutInRuhe(w);
                Pruefe.Wahr(f.Kopfzeile.Length > 30, "lange Kopfzeile: " + f.Kopfzeile);
                Pruefe.Gleich(f.Kopfzeile, ff.KopfText.Text, "live: Text");
                Pruefe.Wahr(Math.Abs(w.DesiredSize.Width - vorher.Width) < 0.5 && Math.Abs(w.ActualWidth - vorher.Width) < 0.5,
                    string.Create(De, $"live: die Liste bleibt {vorher.Width:0} breit, wünscht {w.DesiredSize.Width:0}"));
                f.Dispose();
            }
        });
    }

    /// <summary>Wie ein Fenster mit SizeToContent: mit freier Breite messen, dann in der Wunschgröße anordnen.</summary>
    private static Size WieImFenster(FunkfeldFenster ff)
    {
        ff.ApplyTemplate();
        var w = Bildlauf.Wurzel(ff);
        var frei = new Size(double.PositiveInfinity, double.PositiveInfinity);
        w.Measure(frei);
        Bildlauf.Leeren();
        w.Measure(frei);
        var g = w.DesiredSize;
        w.Arrange(new Rect(g));
        w.UpdateLayout();
        Bildlauf.Leeren();
        w.UpdateLayout();
        LayoutInRuhe(w);
        return g;
    }

    /// <summary>
    /// Das Layout kommt zur Ruhe: Kein sichtbares Element bleibt ungemessen oder ungeordnet (sonst liefe es im Kreis, Bild
    /// für Bild). Eingeklappte Elemente misst WPF nicht und zählen nicht.
    /// </summary>
    private static void LayoutInRuhe(FrameworkElement wurzel)
    {
        bool Sichtbar(DependencyObject? e)
        {
            for (; e is not null; e = VisualTreeHelper.GetParent(e))
            {
                if (e is UIElement { Visibility: not Visibility.Visible }) return false;
                if (ReferenceEquals(e, wurzel)) return true;
            }
            return true;
        }
        var unruhig = Bildlauf.Nachfahren<UIElement>(wurzel).Prepend(wurzel).Where(e => Sichtbar(e) && (!e.IsMeasureValid || !e.IsArrangeValid)).ToList();
        Pruefe.Gleich(0, unruhig.Count, "Layout in Ruhe, unruhig: " + string.Join(", ", unruhig.Take(4).Select(e => e.GetType().Name)));
    }

    /// <summary>
    /// Eine Ablehnung ("hier darfst du nicht senden") steht rot in der Titelzeile und nimmt den Kacheln keine Höhe. Vorher
    /// stand sie in einer eigenen Zeile unter den Kacheln: Bei gemerkter Größe schrumpfte eine Kachel im Raster von 77 auf 67
    /// Einheiten, und der Pegel (A18) verschwand, bis zur nächsten Wahl eines Sendenetzes.
    /// </summary>
    [Test]
    public static void Funkfeld_Hinweis_InDerTitelzeileOhneHoehe()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(Szene());
            var f = Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage);
            fenster.Add(ff);
            var crew = f.Kacheln.Single(k => k.Name == "Crew");
            var jaeger = f.Kacheln.Single(k => k.Name == "Jäger");
            foreach (var d in Enum.GetValues<FunkfeldDarstellung>())
            {
                f.Darstellung = d;
                f.SendenWaehlenAsync(crew, hinzufuegen: false).GetAwaiter().GetResult();
                Pruefe.Gleich(null, f.Hinweis, Name(d) + ": ohne Ablehnung");
                var (_, natur) = Bildlauf.Zeichnen(ff);
                var kachel = ErsteKachel(ff).ActualHeight;
                Pruefe.Gleich(f.Kopfzeile, ff.KopfText.Text, Name(d) + ": Titelzeile zeigt die Sendenetze");

                f.SendenWaehlenAsync(jaeger, hinzufuegen: false).GetAwaiter().GetResult();
                Pruefe.Wahr(f.Hinweis is { Length: > 0 }, Name(d) + ": Ablehnung");
                Pruefe.Gleich(natur, Bildlauf.Zeichnen(ff).Groesse, Name(d) + ": frei gemessen wird das Fenster nicht größer");
                var (bild, _) = Bildlauf.Zeichnen(ff, 1, natur);
                Pruefe.Wahr(Math.Abs(ErsteKachel(ff).ActualHeight - kachel) < 0.5,
                    string.Create(De, $"{Name(d)}: bei gemerkter Größe bleibt die Kachel {kachel:0.0} hoch, war {ErsteKachel(ff).ActualHeight:0.0}"));
                Pruefe.Gleich(f.Hinweis, ff.KopfText.Text, Name(d) + ": Ablehnung in der Titelzeile");
                Pruefe.Gleich(Voreinstellung.RotSchrift, ((SolidColorBrush)ff.KopfText.Foreground).Color, Name(d) + ": in roter Schrift");
                // Seit Stufe 3 stehen im Raster Rollenliste und "Mehrere senden" davor: gemessen wird, wo der Text steht.
                var text = ff.KopfText.TransformToAncestor(Bildlauf.Wurzel(ff)).TransformBounds(new Rect(ff.KopfText.RenderSize));
                Pruefe.Wahr(Deckend(bild, new Rect(text.X + 8, 5, Math.Min(120, text.Width - 16), 14), c => c.R > 190 && c.R > c.G + 50 && c.R > c.B + 30),
                    Name(d) + ": rote Schrift im Bild der Titelzeile");
                if (d == FunkfeldDarstellung.Raster) Bildablage.Ablegen(bild, "funkfeld-hinweis");
            }
            f.SendenWaehlenAsync(crew, hinzufuegen: false).GetAwaiter().GetResult();
            Pruefe.Gleich(f.Kopfzeile, ff.KopfText.Text, "nach der nächsten Wahl wieder die Sendenetze");
            f.Dispose();
        });
    }

    private static FrameworkElement ErsteKachel(FunkfeldFenster ff) =>
        Bildlauf.Nachfahren<Kachelraster>(Bildlauf.Wurzel(ff)).Single().Children.Cast<FrameworkElement>().First();

    [Test]
    public static void Funkfeld_Fenster_RahmenUndPlatzJeAnsicht()
    {
        // A24, Overlay: weder minimieren noch maximieren. Die Stilbits rechnet Fensterrahmen.Stil, ohne Fenster.
        const int stil = 0x16CF0000;
        var ohne = Fensterrahmen.Stil(stil, minimierbar: false, maximierbar: false);
        Pruefe.Gleich(0, ohne & Fensterrahmen.WsMinimizebox, "ohne WS_MINIMIZEBOX");
        Pruefe.Gleich(0, ohne & Fensterrahmen.WsMaximizebox, "ohne WS_MAXIMIZEBOX");
        Pruefe.Gleich(stil & ~(Fensterrahmen.WsMinimizebox | Fensterrahmen.WsMaximizebox), ohne, "sonst bleibt jedes Bit");
        Pruefe.Gleich(stil, Fensterrahmen.Stil(stil, minimierbar: true, maximierbar: true), "das Hauptfenster behält beide");
        Pruefe.Gleich(WindowState.Normal, Fensterrahmen.Erlaubt(WindowState.Maximized, false, false), "Maximiert wird zurückgeholt");
        Pruefe.Gleich(WindowState.Normal, Fensterrahmen.Erlaubt(WindowState.Minimized, false, false), "Minimiert wird zurückgeholt");
        Pruefe.Gleich(WindowState.Maximized, Fensterrahmen.Erlaubt(WindowState.Maximized, true, true), "das Hauptfenster darf maximieren");

        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(Szene());
            var e = a.Ablage.Aktuell.Funkfeld;
            // Bei 100 %, damit Einheiten und Bildpunkte dieselben Zahlen sind.
            var monitor = new Bildschirm(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1040), 1, true);
            e.Merken(FunkfeldDarstellung.Raster, 10, new FensterOrt(120, 80, 820, 230), groesseGezogen: true);
            e.Merken(FunkfeldDarstellung.Liste, 10, new FensterOrt(50_000, 80, 296, 360), groesseGezogen: true);
            var f = a.Funkfeld();
            var ff = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([monitor]));
            fenster.Add(ff);
            // Wie nach dem Zeigen: Der Dispatcher läuft, bevor jemand Ansicht oder Feldzahl wechseln kann. Sonst fielen die im
            // Konstruktor gemessenen Kacheln weg, bevor WPF ihre Bindungen aufgelöst hat (Bindungsfehler der Symbole).
            Bildlauf.Leeren();

            // A24 und Fensterrahmen.cs: schließbar, schmale Titelzeile zum Ziehen, Rand zum Größeziehen.
            Pruefe.Falsch(SystemCommands.MinimizeWindowCommand.CanExecute(null, ff), "nicht minimierbar");
            Pruefe.Falsch(SystemCommands.MaximizeWindowCommand.CanExecute(null, ff), "nicht maximierbar");
            Pruefe.Wahr(SystemCommands.CloseWindowCommand.CanExecute(null, ff), "schließbar");
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(ff);
            Pruefe.Gleich(24.0, chrome.CaptionHeight, "Titelzeile 24 Einheiten");
            Pruefe.Wahr(chrome.ResizeBorderThickness.Left >= 4 && chrome.ResizeBorderThickness.Bottom >= 4, "Rand zum Größeziehen");
            Pruefe.Gleich(new Thickness(1), ff.BorderThickness, "Rahmen im Normalzustand");
            // Ohne Handle meldet WPF StateChanged nicht von selbst (erst die Fensternachricht WM_SIZE löst es aus). Der Test
            // setzt den Zustand und ruft OnStateChanged wie WPF nach der Nachricht; der Rahmen holt das Fenster zurück.
            var zustandGeaendert = typeof(Window).GetMethod("OnStateChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            foreach (var zustand in Gesperrt)
            {
                ff.WindowState = zustand;
                zustandGeaendert.Invoke(ff, [EventArgs.Empty]);
                Pruefe.Gleich(WindowState.Normal, ff.WindowState, zustand + " wird sofort zurückgeholt");
            }
            Pruefe.Gleich(new Thickness(1), ff.BorderThickness, "Rahmen bleibt der des Normalzustands");

            // Die gemerkte Größe gilt genau so; ohne sie die optimale Größe (A32), nie SizeToContent.
            Pruefe.Gleich(SizeToContent.Manual, ff.SizeToContent, "nie SizeToContent");
            Pruefe.Gleich(new Rect(120, 80, 820, 230), new Rect(ff.Left, ff.Top, ff.Width, ff.Height), "Platz und Größe des Rasters");
            // Ein Wechsel wendet den Platz vor dem nächsten Zeichnen an (PlatzVormerken); hier läuft der Dispatcher dafür.
            f.Ansicht = 5;
            Bildlauf.Leeren();
            var optimal5 = Platzierung.Optimal(ff.LetzteWunschgroesse, monitor);
            Pruefe.Gleich(new Rect(120, 80, optimal5.Width, optimal5.Height), new Rect(ff.Left, ff.Top, ff.Width, ff.Height), "5er an der Stelle des 10ers, in seiner optimalen Größe");
            f.Ansicht = 10;
            f.Darstellung = FunkfeldDarstellung.Liste;
            Bildlauf.Leeren();
            Pruefe.Wahr(ff.Left + ff.Width <= 1920 && ff.Top == 80 && Math.Abs(ff.Width - 296) < 0.01, string.Create(De, $"Monitor der Liste fehlt: auf den vorhandenen gerückt ({ff.Left:0},{ff.Top:0})"));
            f.Darstellung = FunkfeldDarstellung.Raster;
            Bildlauf.Leeren();
            Pruefe.Gleich(new Rect(120, 80, 820, 230), new Rect(ff.Left, ff.Top, ff.Width, ff.Height), "zurück im Raster: sein Platz und seine Größe");

            // "Größe nach Inhalt" unter Einstellungen: die gezogene Größe ist weg, die Lage bleibt, das offene Fenster folgt.
            f.GroesseNachInhalt();
            Bildlauf.Leeren();
            var optimal10 = Platzierung.Optimal(ff.LetzteWunschgroesse, monitor);
            Pruefe.Gleich(new Rect(120, 80, optimal10.Width, optimal10.Height), new Rect(ff.Left, ff.Top, ff.Width, ff.Height), "optimale Größe an derselben Stelle");
            Pruefe.Falsch(e.Fensterorte["Raster-10"].MitGroesse, "Größe vergessen");
            Pruefe.Gleich(SizeToContent.Manual, ff.SizeToContent, "auch dann kein SizeToContent");
            f.Dispose();
        });
    }

    /// <summary>
    /// Die Funkfeld-Bilder für docs\bilder, im Speicher statt aus einem Programmlauf, in der Szene der Vorführung und bei
    /// 175 % wie Sarevocs Bildschirm: 01-verbindung-funkfeld (Raster 10), 08-fuenfer-funkfeld (Raster 5), 12-liste-funkfeld
    /// (Liste 10). Sie ersetzen die Bilder des alten, breiten Funkfelds.
    /// </summary>
    [Test]
    public static void Funkfeld_Dokubilder_Vorfuehrung()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(Vorfuehrungsszene());
            var f = Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage);
            fenster.Add(ff);
            foreach (var (d, n, name) in new[] { (FunkfeldDarstellung.Raster, 10, "01-verbindung-funkfeld"), (FunkfeldDarstellung.Raster, 5, "08-fuenfer-funkfeld"),
                         (FunkfeldDarstellung.Liste, 10, "12-liste-funkfeld") })
            {
                f.Darstellung = d;
                f.Ansicht = n;
                Pegel(a, f);
                var (bild, groesse) = Bildlauf.Zeichnen(ff, 1.75);
                Pruefe.Wahr(groesse.Width <= Hoechstens[(d, n)].Width && groesse.Height <= Hoechstens[(d, n)].Height, name + ": kompakt");
                PruefeFarbe(Bildlauf.Pixel(bild, 4, 4, 1.75), Voreinstellung.Leitfarbe, name + ": Marke oben links in der Leitfarbe");
                Pruefe.Gleich("Senden: Crew", f.Kopfzeile, name + ": Titelzeile");
                Bildablage.Ablegen(bild, name);
            }
            f.Dispose();
        });
    }

    internal static bool Deckend(BitmapSource bild, Rect bereich, Func<Color, bool> passt)
    {
        for (var y = bereich.Top; y < bereich.Bottom; y++)
        {
            for (var x = bereich.Left; x < bereich.Right; x++)
            {
                var c = Bildlauf.Pixel(bild, x, y);
                if (c.A == 255 && passt(c)) return true;
            }
        }
        return false;
    }

    [Test]
    public static void Farben_Anthrazit_BisInDieGrafik()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var spalten = new List<(Farbsatz Satz, RenderTargetBitmap Haupt, RenderTargetBitmap Feld, Size FeldGroesse)>();
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var app = Application.Current!.Resources;
                Pruefe.Gleich(satz.Akzent, (Color)app["Farbe.Akzent"], satz.Name + ": Akzent in den Ressourcen");
                Pruefe.Gleich(satz.Leitfarbe, (Color)app["Farbe.Leitfarbe"], satz.Name + ": Leitfarbe in den Ressourcen");
                Pruefe.Wahr(app.MergedDictionaries[1].Contains(Farbvorschlaege.Kennung), satz.Name + ": direkt hinter Farben.xaml");
                // Schließen unter der Maus: eigene Rolle, sichtbar gegen die Titelleiste in der Leitfarbe.
                var schliessen = (ControlTemplate)((Style)app["Knopf.Schliessen"]).Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
                var unterMaus = schliessen.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty).Setters.OfType<Setter>().Single();
                var hover = ((SolidColorBrush)unterMaus.Value).Color;
                Pruefe.Gleich(satz.SchliessenFlaeche, hover, satz.Name + ": Schließen unter der Maus");
                Pruefe.Wahr(Kontrast.Verhaeltnis(hover, satz.Leitfarbe) >= 2, satz.Name + ": Schließen unter der Maus hebt sich von der Titelleiste ab");

                using var a = new Aufbau(Szene());
                var h = a.Haupt(out var funkfeld, out var schwelle, out _, out _);
                for (var i = 0; i < 400; i++)
                    a.Maschine.Melden(new PegelMeldung(i * 20L, i % 50 < 25 ? -25 : -62, -61, -52, i % 50 < 25 ? 0.9f : 0.1f, i % 50 < 25));
                schwelle.Automatik = true;
                schwelle.Lernen = true;
                h.AktiverBereich = h.Bereiche[0];
                h.Takt();
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var (hb, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1000, 780));
                PruefeFarbe(Bildlauf.Pixel(hb, 700, 4), satz.Leitfarbe, satz.Name + ": Titelleiste");
                // Seit 0.6.0 ist die Frage ein Schritt auf der Startseite (Q10); seit 0.10.0 ist "Ich höre mit" dabei umrandet (Q76; der
                // Willkommenssatz steht nur ohne Verbindung, die Szene hier ist verbunden).
                var erststart = Bildlauf.Nachfahren<Border>(haupt).First(b => b.Name == "HoerweiseRahmen");
                Pruefe.Wahr(erststart.Visibility == Visibility.Visible && erststart.BorderThickness == new Thickness(2), satz.Name + ": Erststart umrandet");
                Pruefe.Gleich(satz.Akzent, ((SolidColorBrush)erststart.BorderBrush).Color, satz.Name + ": Rand des Erststarts im Akzent");
                Pruefe.Wahr(Enthaelt(hb, satz.Akzent), satz.Name + ": Akzent im Bild des Hauptfensters");
                Pruefe.Wahr(Enthaelt(hb, satz.Leitfarbe), satz.Name + ": Leitfarbe im Bild des Hauptfensters");
                // Seit 0.10.0 steht die Schwellengrafik nur auf der Seite Sprachaktivierung (bis 0.9.3 auch aufgeklappt unter der Startseite).
                h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
                var (sb, _) = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.SprachaktivierungVoreinstellung);
                var grafik = haupt.Schwellenbereich.Verlaufsgrafik;
                Pruefe.Wahr(grafik.ActualHeight >= 120, satz.Name + ": Seite Sprachaktivierung");
                Pruefe.Gleich(satz.Akzent, ((SolidColorBrush)Akzente.Fuer(grafik).Akzent).Color, satz.Name + ": die Schwellengrafik zeichnet im Akzent der Stellung");
                Pruefe.Wahr(Enthaelt(sb, satz.Akzent), satz.Name + ": Akzent auf der Seite Sprachaktivierung");

                if (satz.Stellung == Farbvorschlaege.Voreinstellung)
                {
                    // Die Regler im Hauptfenster: Lautstärke je Kanal unter Empfangen > Feinheiten (bis 0.9.3 unter Meine Kanäle),
                    // Durchsichtigkeit unter Funkfeld und Darstellung > Feinheiten. Gezeichnet auch neben einem Spiel, damit der Zuhörer ihre
                    // Bindungen sieht (der Fenstertest überspringt sich dann).
                    Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
                    Bildablage.Ablegen(Bildlauf.Zeichnen(haupt, 1, new Size(1100, 1400)).Bild, "hauptfenster-empfangen");
                    Seiten.Feinheiten(h, Abschnittskennung.Funkfeld);
                    Bildablage.Ablegen(Bildlauf.Zeichnen(haupt, 1, new Size(1100, 1800)).Bild, "hauptfenster-einstellungen");
                }

                funkfeld.Takt();
                var ff = new FunkfeldFenster(funkfeld, a.Ablage);
                fenster.Add(ff);
                var (fb, fg) = Bildlauf.Zeichnen(ff);
                PruefeFarbe(Bildlauf.Pixel(fb, 4, 4), satz.Leitfarbe, satz.Name + ": Marke des Funkfelds");
                PruefeFarbe(Bildlauf.Pixel(fb, fg.Width - 150, fg.Height - 12), satz.Leitfarbe, satz.Name + ": Global in der Leitfarbe");
                spalten.Add((satz, hb, fb, fg));
                funkfeld.Dispose();
            }
            Bildablage.Ablegen(FarbenBogen(spalten), "farben");
        });
    }

    internal static void PruefeFarbe(Color ist, Color soll, string was) =>
        Pruefe.Wahr(Math.Abs(ist.R - soll.R) <= 2 && Math.Abs(ist.G - soll.G) <= 2 && Math.Abs(ist.B - soll.B) <= 2, $"{was}: erwartet {soll}, war {ist}");

    /// <summary>Kommt die Farbe (auf 2 Stufen genau, deckend) irgendwo im Bild vor?</summary>
    internal static bool Enthaelt(BitmapSource bild, Color farbe)
    {
        var breite = bild.PixelWidth;
        var p = new byte[breite * bild.PixelHeight * 4];
        bild.CopyPixels(p, breite * 4, 0);
        for (var i = 0; i < p.Length; i += 4)
        {
            if (p[i + 3] == 255 && Math.Abs(p[i + 2] - farbe.R) <= 2 && Math.Abs(p[i + 1] - farbe.G) <= 2 && Math.Abs(p[i] - farbe.B) <= 2) return true;
        }
        return false;
    }

    internal static string Hex(Color c) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    private static string K(Color a, Color b) => Kontrast.Verhaeltnis(a, b).ToString("0.00", De) + ":1";

    private static RenderTargetBitmap FarbenBogen(List<(Farbsatz Satz, RenderTargetBitmap Haupt, RenderTargetBitmap Feld, Size FeldGroesse)> spalten)
    {
        const double rand = 24;
        const double breite = 1000;
        var b = new Bogen(rand + (spalten.Count * (breite + rand)), 70 + 30 + 780 + 18 + 200 + 18 + 190, 1, Grund);
        b.Text("Farben - Anthrazit mit Petrol (Hauptfenster Audio mit Erststart-Karte und Sprachaktivierung, darunter Funkfeld A)", rand, 16, 18, fett: true);
        b.Text("Kontraste nach WCAG 2.1: Text mindestens 4,5:1, Rahmen, Fokus und Bedienelemente mindestens 3:1. Gemessen gegen Anthrazit #252525 und Kachelfläche #2F2F2F.",
            rand, 44, 12, Grau);
        var x = rand;
        foreach (var (s, haupt, feld, fg) in spalten)
        {
            var y = 72.0;
            b.Text(s.Name, x, y, 17, fett: true);
            y += 30;
            b.Bild(haupt, x, y, breite, 780);
            y += 780 + 18;
            b.Bild(feld, x, y, fg.Width, fg.Height);
            y += 200 + 18;
            var ok = Kontrast.Verhaeltnis(s.Akzent, Palette.Flaeche) >= 3 && Kontrast.Verhaeltnis(Palette.Text, s.Leitfarbe) >= 4.5
                && Kontrast.Verhaeltnis(s.RotSchrift, Palette.Anthrazit) >= 4.5 && Kontrast.Verhaeltnis(s.RotSchrift, Palette.FlaecheTief) >= 4.5
                && Kontrast.Verhaeltnis(Palette.Text, s.SchliessenFlaeche) >= 3 && Kontrast.Verhaeltnis(s.SchliessenFlaeche, s.Leitfarbe) >= 2;
            var zeilen = new[]
            {
                $"Leitfarbe {Hex(s.Leitfarbe)}: Weiß darauf {K(Palette.Text, s.Leitfarbe)}, als Linie auf #252525 nur {K(s.Leitfarbe, Palette.Anthrazit)}",
                $"Akzent {Hex(s.Akzent)} (Rahmen, Fokus, Reglerspur): {K(s.Akzent, Palette.Anthrazit)} auf #252525, {K(s.Akzent, Palette.Flaeche)} auf #2F2F2F",
                $"Akzentfläche {Hex(s.AkzentFlaeche)} (Schalter an): Weiß darauf {K(Palette.Text, s.AkzentFlaeche)}",
                $"Schließen unter der Maus {Hex(s.SchliessenFlaeche)}: Weiß darauf {K(Palette.Text, s.SchliessenFlaeche)}, gegen die Titelleiste {K(s.SchliessenFlaeche, s.Leitfarbe)}",
                $"Rote Schrift {Hex(s.RotSchrift)}: {K(s.RotSchrift, Palette.Anthrazit)} auf #252525, {K(s.RotSchrift, Palette.Flaeche)} auf #2F2F2F, {K(s.RotSchrift, Palette.FlaecheTief)} auf #1E1E1E",
                $"Global: Weiß {K(Palette.Text, s.Leitfarbe)}, Grün {K(Palette.Senden, s.Leitfarbe)}, Orange {K(Palette.Hoeren, s.Leitfarbe)}",
                ok ? "Regeln erfüllt" + (Kontrast.Verhaeltnis(s.RotSchrift, Palette.Flaeche) < 4.5 ? " (rote Schrift nur auf der dunklen Unterlage)" : ", rote Schrift auch auf der Kachelfläche")
                   : "Regeln NICHT erfüllt",
            };
            foreach (var z in zeilen)
            {
                b.Text(z, x, y, 12.5, Colors.White);
                y += 22;
            }
            x += breite + rand;
        }
        return b.Fertig();
    }
}
