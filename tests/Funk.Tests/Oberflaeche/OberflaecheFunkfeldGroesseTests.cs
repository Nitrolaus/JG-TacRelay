// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Funk.Client.Ablage;
using Funk.Client.Fenster;
using Funk.Client.Steuerelemente;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Größe und Platz der Fenster (A32; Sarevoc, 30.09.2026 nach dem ersten Lauf am echten Fenster, 175 %, zwei Monitore: "Das
/// Funkfeld öffnet sich beim Starten absurd groß über 2 Bildschirme", "beim Funkfeld-Start bitte in optimaler Größe,
/// gerade so, dass alles angezeigt wird", "so wie sich jemand ein Fenster einstellt, soll es sein, auch später").
/// Geprüft: die Ursache selbst (das Kachelraster meldete jedes Angebot als Wunsch, und ein Fenster mit SizeToContent bietet
/// seinem Inhalt den ganzen virtuellen Bildschirm an), die optimale Größe je Ansicht, Feldzahl und DPI, der erste Start
/// oben mittig, das Wiederherstellen (vorhandener und abgesteckter Monitor, zu groß, andere DPI) und das gebündelte Merken
/// beim Verschieben und Ziehen, für Funkfeld und Hauptfenster. Die Monitore sind feste Listen (<see cref="FesteBildschirme"/>);
/// Fenster nur im Speicher (<see cref="Bildlauf"/>, ohne Fensterhandle, auch neben einem Spiel).
/// </summary>
internal static class OberflaecheFunkfeldGroesseTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly double[] Skalen = [1.0, 1.5, 1.75];
    private static readonly (FunkfeldDarstellung D, int N)[] Ansichten =
        [(FunkfeldDarstellung.Raster, 10), (FunkfeldDarstellung.Raster, 5), (FunkfeldDarstellung.Liste, 10), (FunkfeldDarstellung.Liste, 5)];

    /// <summary>Sarevocs Rechner, gemessen am 30.09.2026: zwei 4K-Monitore bei 175 %, die Taskleiste unten auf dem Hauptmonitor (84 Bildpunkte).</summary>
    internal static readonly Bildschirm TestHaupt = new(new Pixelrechteck(0, 0, 3840, 2160), new Pixelrechteck(0, 0, 3840, 2076), 1.75, true);

    internal static readonly Bildschirm TestZweiter = new(new Pixelrechteck(3840, 0, 3840, 2160), new Pixelrechteck(3840, 0, 3840, 2160), 1.75, false);

    internal static FesteBildschirme ZweiMonitore { get; } = new([TestHaupt, TestZweiter]);

    /// <summary>Gemischt: Hauptmonitor Full HD bei 100 %, rechts daneben WQHD bei 150 %.</summary>
    internal static readonly Bildschirm FullHd = new(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1040), 1.0, true);

    internal static readonly Bildschirm Wqhd = new(new Pixelrechteck(1920, 0, 2560, 1440), new Pixelrechteck(1920, 0, 2560, 1400), 1.5, false);

    internal static FesteBildschirme Gemischt { get; } = new([FullHd, Wqhd]);

    /// <summary>Kleiner Laptop: 1366 x 768 bei 100 %, Taskleiste 40.</summary>
    internal static readonly Bildschirm Laptop = new(new Pixelrechteck(0, 0, 1366, 768), new Pixelrechteck(0, 0, 1366, 728), 1.0, true);

    /// <summary>Höchstgrößen der Ansichten in Einheiten (OberflaecheFunkfeldBilderTests), plus einen Bildpunkt Rundung.</summary>
    private static readonly Dictionary<(FunkfeldDarstellung, int), Size> Hoechstens = new()
    {
        [(FunkfeldDarstellung.Raster, 10)] = new Size(800, 220),
        [(FunkfeldDarstellung.Raster, 5)] = new Size(800, 115),
        [(FunkfeldDarstellung.Liste, 10)] = new Size(294, 345),
        [(FunkfeldDarstellung.Liste, 5)] = new Size(294, 200),
    };

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>
    /// Die Ursache, nachgestellt wie in der Ursachensuche (Test A, ohne Handle): Das Fenster mit SizeToContent gibt seinem
    /// Inhalt nicht Unendlich, sondern die größte Nachführgröße von Windows, bei Sarevoc 7708 x 2188 Bildpunkte, also 4404,6 x
    /// 1250,3 Einheiten. Auf dem alten Stand meldete das Kachelraster genau dieses Angebot zurück (4394,3 x 1216, 10 Spalten
    /// statt 5), und das Funkfeld ging über beide Monitore; auch jedes andere Angebot (1000 x 600) spiegelte es. Jetzt ist
    /// der Wunsch mit jeder Grenze derselbe wie frei gemessen, auch über mehrere Durchgänge (messen, anordnen, neu messen).
    /// </summary>
    [Test]
    public static void Ursache_KachelrasterMeldetInhaltNieDasAngebot()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage, ZweiMonitore);
            fenster.Add(ff);
            var grenzen = new[]
            {
                new Size(7708 / 1.75, 2188 / 1.75),
                new Size(SystemParameters.MaximumWindowTrackWidth, SystemParameters.MaximumWindowTrackHeight),
                new Size(1000, 600),
                new Size(5000, 3000),
            };
            foreach (var (d, n) in Ansichten)
            {
                f.Darstellung = d;
                f.Ansicht = n;
                var (_, frei) = Bildlauf.Zeichnen(ff);
                var wurzel = Bildlauf.Wurzel(ff);
                var raster = Bildlauf.Nachfahren<Kachelraster>(wurzel).Single();
                var rasterFrei = raster.DesiredSize;
                foreach (var grenze in grenzen)
                {
                    var was = T($"{d}-{n}, Grenze {grenze.Width:0.#} x {grenze.Height:0.#}");
                    for (var durchgang = 1; durchgang <= 4; durchgang++)
                    {
                        wurzel.InvalidateMeasure();
                        wurzel.Measure(grenze);
                        Bildlauf.Leeren();
                        wurzel.Measure(grenze);
                        var wunsch = wurzel.DesiredSize;
                        wurzel.Arrange(new Rect(wunsch));
                        Pruefe.Wahr(Math.Abs(wunsch.Width - frei.Width) < 0.5 && Math.Abs(wunsch.Height - frei.Height) < 0.5,
                            T($"{was}, Durchgang {durchgang}: Wunsch {wunsch.Width:0.#} x {wunsch.Height:0.#}, frei {frei.Width:0.#} x {frei.Height:0.#}"));
                    }
                    Pruefe.Wahr(raster.LetztesAngebot.Width <= grenze.Width + 0.01, was + ": Angebot an das Raster höchstens die Grenze");
                    if (grenze.Width > 4000)
                        Console.WriteLine(T($"MASS Ursache {d}-{n}: Angebot an das Kachelraster {raster.LetztesAngebot.Width:0.#} x {raster.LetztesAngebot.Height:0.#}, Wunsch {raster.DesiredSize.Width:0.#} x {raster.DesiredSize.Height:0.#}"));
                    Pruefe.Wahr(Math.Abs(raster.DesiredSize.Width - rasterFrei.Width) < 0.5 && Math.Abs(raster.DesiredSize.Height - rasterFrei.Height) < 0.5,
                        T($"{was}: das Kachelraster wünscht {raster.DesiredSize.Width:0.#} x {raster.DesiredSize.Height:0.#}, nicht das Angebot {raster.LetztesAngebot.Width:0.#} x {raster.LetztesAngebot.Height:0.#}"));
                }
                // Mit der Grenze angeordnet: dieselben Spalten wie frei, 2 Reihen zu 5 statt 10 in einer.
                var reihen = raster.Children.Cast<FrameworkElement>().Select(k => Math.Round(k.TranslatePoint(default, raster).Y)).Distinct().Count();
                Pruefe.Gleich(d == FunkfeldDarstellung.Liste ? n : n == 10 ? 2 : 1, reihen, T($"{d}-{n}: Reihen mit der Grenze des Fensters"));
                Console.WriteLine(T($"MASS Ursache {d}-{n}: frei {frei.Width:0.#} x {frei.Height:0.#}, mit der Grenze von Sarevocs Fenster (4404,6 x 1250,3) ebenso; alter Stand 4404,6 x 1250,3"));
            }

            // Das Kachelraster allein: ein großes Angebot ändert nichts, ein kleineres macht es schmaler und höher.
            var r = Bildlauf.Nachfahren<Kachelraster>(Bildlauf.Wurzel(ff)).Single();
            f.Darstellung = FunkfeldDarstellung.Raster;
            f.Ansicht = 10;
            Bildlauf.Zeichnen(ff);
            r = Bildlauf.Nachfahren<Kachelraster>(Bildlauf.Wurzel(ff)).Single();
            r.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var natur = r.DesiredSize;
            r.Measure(new Size(5000, 3000));
            Pruefe.Gleich(natur, r.DesiredSize, "Kachelraster: Angebot 5000 x 3000 wie Unendlich");
            // Auf Bildpunkte gerundet (Layoutrundung bei der DPI des Testprozesses): 766 wird etwa 765,7.
            Pruefe.Wahr(Math.Abs(Kachelraster.Breite(5, 150, 4) - natur.Width) < 0.6, T($"natürliche Breite: 5 Kacheln zu 150 mit 4 Abstand ({natur.Width:0.##})"));
            r.Measure(new Size(500, 3000));
            Pruefe.Wahr(r.DesiredSize.Width <= 500 && r.DesiredSize.Height > natur.Height, T($"schmaler angeboten: {r.DesiredSize.Width:0} x {r.DesiredSize.Height:0}"));
            f.Dispose();
        });
    }

    [Test]
    public static void Groesse_OptimalGerundetUndBegrenzt()
    {
        foreach (var s in Skalen)
        {
            var b = new Bildschirm(new Pixelrechteck(0, 0, 3840, 2160), new Pixelrechteck(0, 0, 3840, 2076), s, true);
            foreach (var wunsch in new[] { new Size(776, 192.6), new Size(776, 111.4), new Size(240, 312.6), new Size(240, 170.8), new Size(1120, 780) })
            {
                var o = Platzierung.Optimal(wunsch, b);
                var was = T($"{wunsch.Width} x {wunsch.Height} bei {s}");
                Pruefe.Wahr(o.Width >= wunsch.Width - 1e-9 && o.Height >= wunsch.Height - 1e-9, was + ": nichts abgeschnitten");
                Pruefe.Wahr(o.Width - wunsch.Width < (1 / s) + 1e-9 && o.Height - wunsch.Height < (1 / s) + 1e-9, was + ": höchstens ein Bildpunkt mehr");
                Pruefe.Wahr(Math.Abs((o.Width * s) - Math.Round(o.Width * s)) < 1e-6 && Math.Abs((o.Height * s) - Math.Round(o.Height * s)) < 1e-6,
                    T($"{was}: ganze Bildpunkte ({o.Width * s} x {o.Height * s})"));
                Pruefe.Wahr(o.Width <= wunsch.Width * Platzierung.HoechstensFaktor && o.Height <= wunsch.Height * Platzierung.HoechstensFaktor, was + ": höchstens 1,2 mal");
            }
        }
        Pruefe.Gleich(new Size(1358 / 1.75, 338 / 1.75), Platzierung.Optimal(new Size(776, 192.6), TestHaupt), "Raster 10 bei 175 %: 1358 x 338 Bildpunkte");
        Pruefe.Gleich(1358, Platzierung.Pixel(Platzierung.Optimal(new Size(776, 192.6), TestHaupt).Width, 1.75), "776 * 1,75 wird nicht zu 1359");
        // Begrenzt auf den Arbeitsbereich, auch auf einem kleinen Monitor.
        var winzig = new Bildschirm(new Pixelrechteck(0, 0, 800, 600), new Pixelrechteck(0, 0, 640, 200), 1.25, true);
        var eng = Platzierung.Optimal(new Size(776, 312.6), winzig);
        Pruefe.Wahr(eng.Width <= 640 / 1.25 + 1e-9 && eng.Height <= 200 / 1.25 + 1e-9, T($"nie größer als der Arbeitsbereich: {eng.Width:0.#} x {eng.Height:0.#}"));
        Pruefe.Gleich(new Size(1366, 728), Platzierung.Begrenzen(new Size(1120 * 2, 780 * 2), Laptop, new Size(560, 440)), "Hauptfenster auf dem Laptop begrenzt");
        Pruefe.Gleich(new Size(560, 440), Platzierung.Begrenzen(new Size(10, 10), Laptop, new Size(560, 440)), "zu klein: Mindestgröße");
        var unsinn = Platzierung.Optimal(new Size(double.NaN, double.PositiveInfinity), Laptop);
        Pruefe.Wahr(double.IsFinite(unsinn.Width) && double.IsFinite(unsinn.Height), "Unsinn ergibt eine endliche Größe");
    }

    /// <summary>
    /// Das Funkfeld im Speicher: kein SizeToContent mehr, sondern die optimale Größe fest gesetzt, je Ansicht und Feldzahl
    /// bei 100, 150 und 175 %, und beim ersten Start oben mittig auf dem Monitor des Hauptfensters (sonst dem Hauptmonitor).
    /// </summary>
    [Test]
    public static void Groesse_Funkfeld_OptimalJeAnsichtFeldzahlUndDpi()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var s in Skalen)
            {
                var haupt = new Bildschirm(new Pixelrechteck(0, 0, 3840, 2160), new Pixelrechteck(0, 0, 3840, 2076), s, true);
                var monitore = new FesteBildschirme([haupt, new Bildschirm(new Pixelrechteck(3840, 0, 1920, 1080), new Pixelrechteck(3840, 0, 1920, 1040), 1, false)]);
                foreach (var (d, n) in Ansichten)
                {
                    using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
                    a.Ablage.Aktuell.Funkfeld.Darstellung = d;
                    a.Ablage.Aktuell.Funkfeld.Ansicht = n;
                    var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
                    var ff = new FunkfeldFenster(f, a.Ablage, monitore);
                    fenster.Add(ff);
                    var was = T($"{d}-{n} bei {s}");
                    Pruefe.Gleich(SizeToContent.Manual, ff.SizeToContent, was + ": kein SizeToContent");
                    var (_, frei) = Bildlauf.Zeichnen(ff);
                    Pruefe.Wahr(Math.Abs(ff.LetzteWunschgroesse.Width - frei.Width) < 0.5 && Math.Abs(ff.LetzteWunschgroesse.Height - frei.Height) < 0.5,
                        T($"{was}: gemessen wie im Bild ({ff.LetzteWunschgroesse.Width:0.#} x {ff.LetzteWunschgroesse.Height:0.#}, frei {frei.Width:0.#} x {frei.Height:0.#})"));
                    var soll = Platzierung.Optimal(ff.LetzteWunschgroesse, haupt);
                    Pruefe.Wahr(Math.Abs(ff.Width - soll.Width) < 0.01 && Math.Abs(ff.Height - soll.Height) < 0.01, T($"{was}: {ff.Width:0.##} x {ff.Height:0.##}, optimal {soll.Width:0.##} x {soll.Height:0.##}"));
                    var max = Hoechstens[(d, n)];
                    Pruefe.Wahr(ff.Width <= max.Width + 1 && ff.Height <= max.Height + 1, T($"{was}: kompakt ({ff.Width:0} x {ff.Height:0})"));
                    // Oben mittig auf dem Hauptmonitor, 24 Einheiten unter dem Rand.
                    var p = ff.Platzfuehrung.Gesetzt!;
                    Pruefe.Gleich(haupt, p.Bildschirm, was + ": Hauptmonitor");
                    Pruefe.Wahr(Math.Abs(p.Links + (p.Rahmen.Breite / 2.0) - 1920) <= 1, T($"{was}: mittig ({p.Links} + {p.Rahmen.Breite} / 2)"));
                    Pruefe.Gleich(Platzierung.Pixel(FunkfeldFenster.AbstandOben, s), p.Oben, was + ": oben mit Abstand");
                    Pruefe.Wahr(Math.Abs((ff.Left * s) - p.Links) < 0.5 && Math.Abs((ff.Top * s) - p.Oben) < 0.5, was + ": Lage in Einheiten mit der Skala des Monitors");
                    Pruefe.Gleich(0, a.Ablage.Aktuell.Funkfeld.Fensterorte.Count, was + ": nichts gemerkt, solange du nichts bewegst");
                    if (Math.Abs(s - 1.75) < 1e-9 && d == FunkfeldDarstellung.Raster && n == 10)
                        Console.WriteLine(T($"MASS Funkfeld Raster 10 bei 175 %: {p.Rahmen.Breite} x {p.Rahmen.Hoehe} Bildpunkte (Befund vorher 7708 x 2188)"));
                    f.Dispose();
                }
            }

            // Erster Start mit Hauptfenster auf dem zweiten Monitor: das Funkfeld kommt dorthin.
            using (var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene()))
            {
                var f = a.Funkfeld();
                var ff = new FunkfeldFenster(f, a.Ablage, ZweiMonitore, () => (5760, 1080));
                fenster.Add(ff);
                var p = ff.Platzfuehrung.Gesetzt!;
                Pruefe.Gleich(TestZweiter, p.Bildschirm, "auf dem Monitor des Hauptfensters");
                Pruefe.Wahr(Math.Abs(p.Links + (p.Rahmen.Breite / 2.0) - 5760) <= 1 && p.Oben == 42, T($"oben mittig dort: {p.Links},{p.Oben}"));
                f.Dispose();
            }
        });
    }

    [Test]
    public static void Platzierung_Wiederherstellen_MonitorFehltZuGrossAndereDpi()
    {
        var min = new Size(150, 60);
        var raster = new Size(776, 192.6);

        // Vorhandener Monitor: genau so, auch auf dem zweiten.
        var p = Platzierung.Wiederherstellen(new FensterOrt(4000, 300, 700, 210), raster, ZweiMonitore.Alle(), min);
        Pruefe.Gleich(new Platz(4000, 300, 700, 210, TestZweiter), p, "zweiter Monitor, eigene Größe: genau so");
        p = Platzierung.Wiederherstellen(new FensterOrt(100, 50), raster, ZweiMonitore.Alle(), min);
        Pruefe.Gleich((100, 50), (p.Links, p.Oben), "ohne Größe: die Lage");
        Pruefe.Gleich(Platzierung.Optimal(raster, TestHaupt), new Size(p.Breite, p.Hoehe), "ohne Größe: die optimale");
        // Am Rand teils außerhalb, aber greifbar: bleibt so (du hast es dorthin geschoben).
        p = Platzierung.Wiederherstellen(new FensterOrt(3500, 1900, 776, 192.6), raster, [TestHaupt], min);
        Pruefe.Gleich((3500, 1900), (p.Links, p.Oben), "teils über den Rand, greifbar: genau so");

        // Monitor abgesteckt: auf den nächsten, ganz in dessen Arbeitsbereich.
        p = Platzierung.Wiederherstellen(new FensterOrt(5000, 300, 776, 192.6), raster, [TestHaupt], min);
        Pruefe.Gleich(TestHaupt, p.Bildschirm, "abgesteckt: der nächste Monitor");
        Pruefe.Gleich(3840 - Platzierung.Pixel(776, 1.75), p.Links, "an seinen rechten Rand gerückt");
        Pruefe.Gleich(300, p.Oben, "die Höhe bleibt");
        Pruefe.Gleich(new Size(776, 192.6), new Size(p.Breite, p.Hoehe), "die Größe bleibt");
        p = Platzierung.Wiederherstellen(new FensterOrt(-3000, -900), raster, [TestHaupt], min);
        Pruefe.Wahr(p.Links == 0 && p.Oben == 0, T($"links oben weit draußen: in die Ecke ({p.Links},{p.Oben})"));

        // Größer als der Arbeitsbereich: begrenzt und hineingeschoben (etwa eine Übergröße aus dem alten Stand).
        p = Platzierung.Wiederherstellen(new FensterOrt(140, 105, 4404.6, 1250.3), raster, ZweiMonitore.Alle(), min);
        Pruefe.Gleich(TestHaupt, p.Bildschirm, "Übergröße: der Monitor mit dem größten Stück");
        Pruefe.Wahr(p.Rahmen.Breite <= 3840 && p.Rahmen.Hoehe <= 2076 && p.Rahmen.Rechts <= 3840 && p.Rahmen.Unten <= 2076,
            T($"begrenzt auf den Arbeitsbereich: {p.Rahmen.Links},{p.Rahmen.Oben} {p.Rahmen.Breite} x {p.Rahmen.Hoehe}"));

        // Nicht greifbar: Titelzeile über dem Monitor, nur ein Streifen sichtbar.
        p = Platzierung.Wiederherstellen(new FensterOrt(100, -150, 776, 192.6), raster, [TestHaupt], min);
        Pruefe.Gleich(0, p.Oben, "Titelzeile über dem Rand: hereingeholt");
        p = Platzierung.Wiederherstellen(new FensterOrt(3820, 300, 776, 192.6), raster, [TestHaupt], min);
        Pruefe.Wahr(p.Rahmen.Rechts <= 3840, "nur 20 Bildpunkte sichtbar: ganz hereingeholt");

        // Andere DPI: Die Lage zählt in Bildpunkten, die Größe in Einheiten; auf dem 150-%-Monitor rechnet die Größe mit 1,5.
        p = Platzierung.Wiederherstellen(new FensterOrt(2200, 100, 776, 192.6), raster, Gemischt.Alle(), min);
        Pruefe.Gleich(Wqhd, p.Bildschirm, "auf dem WQHD mit 150 %");
        Pruefe.Gleich((2200, 100), (p.Links, p.Oben), "Lage in Bildpunkten unverändert");
        Pruefe.Gleich(1164, p.Rahmen.Breite, "776 Einheiten sind dort 1164 Bildpunkte");
        p = Platzierung.Wiederherstellen(new FensterOrt(2200, 100), new Size(240, 312.6), Gemischt.Alle(), min);
        Pruefe.Gleich(Platzierung.Optimal(new Size(240, 312.6), Wqhd), new Size(p.Breite, p.Hoehe), "optimale Größe mit der Skala des Zielmonitors");
        // Über die Grenze der Monitore: Die linke obere Ecke (Titelzeile) entscheidet, der Platz bleibt genau so.
        p = Platzierung.Wiederherstellen(new FensterOrt(1700, 100, 776, 192.6), raster, Gemischt.Alle(), min);
        Pruefe.Gleich(new Platz(1700, 100, 776, 192.6, FullHd), p, "über die Grenze: der Monitor der Titelzeile, genau so");
        // Die Ecke auf keinem Monitor (links hinausgeschoben): der mit dem größten Stück.
        p = Platzierung.Wiederherstellen(new FensterOrt(-200, 100, 776, 192.6), raster, [FullHd, new Bildschirm(new Pixelrechteck(-1000, 500, 1000, 800),
            new Pixelrechteck(-1000, 500, 1000, 800), 1, false)], min);
        Pruefe.Gleich(FullHd, p.Bildschirm, "Ecke draußen: der Monitor mit dem größten Stück");
        // Gemischt, abgesteckt: der WQHD fehlt, das Fenster kommt auf den Full-HD-Monitor, begrenzt auf dessen Höhe.
        p = Platzierung.Wiederherstellen(new FensterOrt(3000, 1200, 776, 1300), raster, [FullHd], min);
        Pruefe.Wahr(p.Bildschirm == FullHd && p.Rahmen.Rechts <= 1920 && p.Rahmen.Unten <= 1040 && p.Hoehe <= 1040,
            T($"auf den Full-HD-Monitor, begrenzt: {p.Rahmen.Links},{p.Rahmen.Oben} {p.Rahmen.Breite} x {p.Rahmen.Hoehe}"));

        // Hilfen.
        Pruefe.Gleich(TestHaupt, Platzierung.Haupt([TestZweiter, TestHaupt]), "der Hauptmonitor, auch an zweiter Stelle");
        Pruefe.Gleich(TestZweiter, Platzierung.Am(5000, 500, ZweiMonitore.Alle()), "Monitor am Punkt");
        Pruefe.Gleich(TestZweiter, Platzierung.Am(9000, 500, ZweiMonitore.Alle()), "neben allen: der nächste");
        Pruefe.Gleich(0L, new Pixelrechteck(0, 0, 10, 10).Schnitt(new Pixelrechteck(10, 0, 5, 5)), "Kanten berühren sich: kein Schnitt");
        Pruefe.Wahr(Math.Abs(new Pixelrechteck(0, 0, 10, 10).Abstand(new Pixelrechteck(13, 14, 1, 1)) - 5) < 1e-9, "Abstand 3, 4, 5");
    }

    [Test]
    public static void Platzierung_FensterOrt_PruefenUndAlteStaende()
    {
        Pruefe.Gleich(null, FensterOrt.OderNull(new FensterOrt(2_000_000, 0)), "weit draußen: verworfen");
        Pruefe.Gleich(new FensterOrt(5, 5), FensterOrt.OderNull(new FensterOrt(5, 5, double.NaN, 100)), "unbrauchbare Größe fällt weg, die Lage bleibt");
        Pruefe.Gleich(new FensterOrt(5, 5), FensterOrt.OderNull(new FensterOrt(5, 5, 100, null)), "halbe Größe fällt weg");
        Pruefe.Gleich(new FensterOrt(5, 5, 100, 50), FensterOrt.OderNull(new FensterOrt(5, 5, 100, 50)), "gültig bleibt");
        // Alter Stand in Einheiten: WPF setzte ihn mit der Skala des Hauptmonitors.
        Pruefe.Gleich(new FensterOrt(175, 105, 1120, 780, true), FensterOrt.AusAltemStand(new FensterStand(100, 60, 1120, 780, true), 1.75, mitGroesse: true), "alter Stand bei 175 %");
        Pruefe.Gleich(new FensterOrt(175, 105), FensterOrt.AusAltemStand(new FensterStand(100, 60, 4404.6, 1250.3), 1.75, mitGroesse: false), "nur die Lage");
        Pruefe.Gleich(null, FensterOrt.AusAltemStand(null, 1.75, true), "ohne alten Stand nichts");
    }

    /// <summary>
    /// Merken (A32): Lage und Größe je Ansicht und Feldzahl, sobald du das Funkfeld verschiebst oder am Rand ziehst, einmal je
    /// Handgriff (Windows klammert ihn in WM_ENTERSIZEMOVE und WM_EXITSIZEMOVE; ohne Fensterhandle ruft der Test die beiden
    /// Stellen direkt), nicht bei jedem Bildpunkt; beim nächsten Start genau so. Was das Programm selbst setzt, wird nicht
    /// gemerkt.
    /// </summary>
    [Test]
    public static void Merken_Funkfeld_BeimVerschiebenUndZiehenGebuendelt()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var e = a.Ablage.Aktuell.Funkfeld;
            var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage, ZweiMonitore);
            fenster.Add(ff);
            // Wie nach dem Zeigen: Der Dispatcher läuft, bevor jemand Ansicht oder Feldzahl wechseln kann.
            Bildlauf.Leeren();
            var fuehrung = ff.Platzfuehrung;
            var optimal = new Size(ff.Width, ff.Height);
            Pruefe.Gleich(0, fuehrung.Gemerkt, "gesetzt, nicht gemerkt");

            // Verschieben: viele Bewegungen, ein Eintrag, ohne Größe.
            fuehrung.BewegungBeginnt();
            for (var i = 0; i < 40; i++)
            {
                ff.Left += 7;
                ff.Top += 3;
            }
            fuehrung.BewegungEndet();
            Pruefe.Gleich(1, fuehrung.Gemerkt, "40 Bewegungen, einmal gemerkt");
            var ort = e.Fensterorte["Raster-10"];
            Pruefe.Gleich(new FensterOrt(Platzierung.Pixel(ff.Left, 1.75), Platzierung.Pixel(ff.Top, 1.75)), ort, "verschoben: nur die Lage, in Bildpunkten");
            a.Ablage.Sichern();
            Pruefe.Gleich(1, a.Ablage.Schreibvorgaenge, "einmal geschrieben");

            // Ein Klick auf die Titelzeile ohne Bewegung merkt nichts.
            fuehrung.BewegungBeginnt();
            fuehrung.BewegungEndet();
            Pruefe.Gleich(1, fuehrung.Gemerkt, "ohne Änderung nichts gemerkt");

            // Am Rand ziehen: Lage und Größe.
            fuehrung.BewegungBeginnt();
            for (var i = 0; i < 25; i++)
            {
                ff.Width += 4;
                ff.Height += 2;
            }
            fuehrung.BewegungEndet();
            Pruefe.Gleich(2, fuehrung.Gemerkt, "gezogen: einmal gemerkt");
            ort = e.Fensterorte["Raster-10"];
            Pruefe.Wahr(ort.MitGroesse && Math.Abs(ort.Breite!.Value - (optimal.Width + 100)) < 0.01 && Math.Abs(ort.Hoehe!.Value - (optimal.Height + 50)) < 0.01,
                T($"gezogen: mit Größe ({ort.Breite:0.##} x {ort.Hoehe:0.##})"));
            // Danach nur verschoben: Die gezogene Größe bleibt gemerkt.
            fuehrung.BewegungBeginnt();
            ff.Left -= 20;
            fuehrung.BewegungEndet();
            Pruefe.Wahr(e.Fensterorte["Raster-10"].Breite == ort.Breite, "nur verschoben: die gezogene Größe bleibt");
            var gemerkt = e.Fensterorte["Raster-10"];

            // Neustart: genau so, auch nach dem Rundlauf durch die Datei.
            a.Ablage.Sichern();
            var datei = a.Speicher.Laden();
            Pruefe.Gleich(gemerkt, datei.Funkfeld.Fensterorte["Raster-10"], "in der Datei");
            Pruefe.Wahr(System.IO.File.ReadAllText(a.Speicher.Pfad).Contains("\"fensterorte\"", StringComparison.Ordinal), "unter fensterorte");
            var zweit = new FunkfeldFenster(f, a.Ablage, ZweiMonitore);
            fenster.Add(zweit);
            Bildlauf.Leeren();
            // Die Lage auf ganze Bildpunkte gerundet: höchstens ein halber Bildpunkt, 0,29 Einheiten bei 175 %.
            Pruefe.Wahr(Math.Abs(zweit.Left - ff.Left) < 0.3 && Math.Abs(zweit.Top - ff.Top) < 0.3 && Math.Abs(zweit.Width - ff.Width) < 0.01
                && Math.Abs(zweit.Height - ff.Height) < 0.01, T($"Neustart genau so: {zweit.Left:0.##},{zweit.Top:0.##} {zweit.Width:0.##} x {zweit.Height:0.##}"));

            // 5 Felder: an derselben Stelle, in der optimalen Größe des 5ers; verschoben nimmt der 10er die Lage mit. (Beide
            // Fenster hängen am selben Modell und schalten beide um.)
            var (links10, oben10, hoehe10) = (zweit.Left, zweit.Top, zweit.Height);
            f.Ansicht = 5;
            // Den neuen Platz wendet das Fenster vor dem nächsten Zeichnen an, nicht mitten in der Änderung (PlatzVormerken).
            Bildlauf.Leeren();
            Pruefe.Wahr(Math.Abs(ff.Left - links10) < 0.01 && Math.Abs(ff.Top - oben10) < 0.01, "5er an der Stelle des 10ers");
            Pruefe.Wahr(ff.Height < hoehe10 - 50, T($"5er in seiner optimalen Größe ({ff.Height:0} statt {hoehe10:0})"));
            fuehrung.BewegungBeginnt();
            ff.Top += 40;
            fuehrung.BewegungEndet();
            Pruefe.Gleich(e.Fensterorte["Raster-5"].Oben, e.Fensterorte["Raster-10"].Oben, "die Lage gilt für beide Feldzahlen");
            Pruefe.Gleich(gemerkt.Breite, e.Fensterorte["Raster-10"].Breite, "die Größe nur je Feldzahl");
            Pruefe.Falsch(e.Fensterorte["Raster-5"].MitGroesse, "der 5er hat keine gezogene Größe");

            // Liste: eigener Platz; ohne Anhalt bleibt die linke obere Ecke, die Größe wird die optimale der Liste.
            var vorher = (ff.Left, ff.Top);
            f.Darstellung = FunkfeldDarstellung.Liste;
            Bildlauf.Leeren();
            Pruefe.Wahr(Math.Abs(ff.Left - vorher.Left) < 0.3 && Math.Abs(ff.Top - vorher.Top) < 0.3, "Liste ohne eigenen Platz: bleibt stehen");
            Pruefe.Wahr(ff.Width < 300, T($"in der schmalen Größe der Liste ({ff.Width:0})"));

            // "Größe nach Inhalt": gezogene Größen vergessen, das offene Fenster folgt, die Lage bleibt.
            f.Darstellung = FunkfeldDarstellung.Raster;
            f.Ansicht = 10;
            Bildlauf.Leeren();
            var lage = (ff.Left, ff.Top);
            f.GroesseNachInhalt();
            Bildlauf.Leeren();
            Pruefe.Falsch(e.Fensterorte["Raster-10"].MitGroesse, "Größe vergessen");
            Pruefe.Wahr(Math.Abs(ff.Width - optimal.Width) < 0.01 && Math.Abs(ff.Height - optimal.Height) < 0.01, "wieder optimal");
            Pruefe.Wahr(Math.Abs(ff.Left - lage.Left) < 0.3 && Math.Abs(ff.Top - lage.Top) < 0.3, "an derselben Stelle");
            f.Dispose();
        });
    }

    /// <summary>
    /// Wiederherstellen im Fenster: ein gemerkter Platz auf einem abgesteckten Monitor kommt auf den nächsten; eine gemerkte
    /// Übergröße aus dem alten Stand wird auf den Arbeitsbereich begrenzt; ein Monitor mit anderer DPI rechnet die Lage mit
    /// seiner Skala in Einheiten um. Alte Plätze (Einheiten, Größe womöglich vom übergroßen Fenster) geben nur die Lage.
    /// </summary>
    [Test]
    public static void Merken_Funkfeld_WiederherstellenAufVorhandenemMonitor()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var e = a.Ablage.Aktuell.Funkfeld;
            e.Fensterorte["Raster-10"] = new FensterOrt(6000, 400, 800, 230);
            var f = a.Funkfeld();
            var zwei = new FunkfeldFenster(f, a.Ablage, ZweiMonitore);
            fenster.Add(zwei);
            Pruefe.Gleich(new Platz(6000, 400, 800, 230, TestZweiter), zwei.Platzfuehrung.Gesetzt, "zweiter Monitor da: genau so");
            var eins = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([TestHaupt]));
            fenster.Add(eins);
            var p = eins.Platzfuehrung.Gesetzt!;
            Pruefe.Wahr(p.Bildschirm == TestHaupt && p.Rahmen.Rechts <= 3840 && p.Oben == 400, T($"zweiter Monitor abgesteckt: auf den Hauptmonitor ({p.Links},{p.Oben})"));
            Pruefe.Gleich(new FensterOrt(6000, 400, 800, 230), e.Fensterorte["Raster-10"], "das Rücken wird nicht gemerkt: kommt der Monitor wieder, steht es dort");

            // Übergröße (wie aus dem alten Stand, am Rand kleiner gezogen): begrenzt auf den Arbeitsbereich eines Monitors.
            e.Fensterorte["Raster-10"] = new FensterOrt(140, 105, 4404.6, 1250.3);
            var gross = new FunkfeldFenster(f, a.Ablage, ZweiMonitore);
            fenster.Add(gross);
            p = gross.Platzfuehrung.Gesetzt!;
            Pruefe.Wahr(p.Rahmen.Breite <= 3840 && p.Rahmen.Rechts <= 3840 && p.Rahmen.Unten <= 2076, T($"Übergröße begrenzt: {p.Rahmen.Breite} x {p.Rahmen.Hoehe}"));

            // Andere DPI: auf dem WQHD mit 150 % die Lage durch 1,5.
            e.Fensterorte["Raster-10"] = new FensterOrt(2400, 300);
            var gemischt = new FunkfeldFenster(f, a.Ablage, Gemischt);
            fenster.Add(gemischt);
            Pruefe.Wahr(Math.Abs(gemischt.Left - 1600) < 0.01 && Math.Abs(gemischt.Top - 200) < 0.01, T($"Lage mit 1,5 umgerechnet: {gemischt.Left:0.##},{gemischt.Top:0.##}"));
            Pruefe.Wahr(Math.Abs((gemischt.Width * 1.5) - Math.Round(gemischt.Width * 1.5)) < 1e-6, "optimale Größe in ganzen Bildpunkten des WQHD");

            // Alte Datei: Platz in Einheiten mit einer Übergröße; nur die Lage gilt, mit 1,75 in Bildpunkte umgesetzt.
            e.Fensterorte.Clear();
            e.Orte = new Dictionary<string, FunkfeldPlatz>(StringComparer.Ordinal) { ["Raster-10"] = new FunkfeldPlatz(80, 60, 4404.6, 1250.3) };
            var alt = new FunkfeldFenster(f, a.Ablage, ZweiMonitore);
            fenster.Add(alt);
            p = alt.Platzfuehrung.Gesetzt!;
            Pruefe.Wahr(p.Links == 140 && p.Oben == 105 && Math.Abs(p.Breite - alt.LetzteWunschgroesse.Width) < 1, T($"alter Platz: Lage 140,105, optimale Größe ({p.Links},{p.Oben} {p.Breite:0.#})"));
            alt.Platzfuehrung.BewegungBeginnt();
            alt.Left += 10;
            alt.Platzfuehrung.BewegungEndet();
            Pruefe.Gleich(null, e.Orte, "einmal bewegt: der alte Platz ist abgelöst");
            f.Dispose();
        });
    }

    /// <summary>
    /// Neuaufbau für eine Farbstellung (A31) und Wiederöffnen in derselben Sitzung (Gegenprüfung 30.09.2026): Das neue
    /// Fenster kommt an den Platz des alten, auch wenn der nicht gemerkt ist (Funkfeld nie bewegt, nur Ansicht gewechselt;
    /// Hauptfenster per Win+Pfeil eingerastet). Vorher merkte das Programm den Platz beim Laden und Schließen; seit A32 nur
    /// nach einem Handgriff, und das neue Funkfeld sprang oben mittig auf den Monitor des Hauptfensters, womöglich auf den
    /// anderen Bildschirm. Gemerkt wird dabei nichts, und ein gemerkter Platz geht vor.
    /// </summary>
    [Test]
    public static void Neuaufbau_AmBisherigenPlatzOhneMerken()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var e = a.Ablage.Aktuell.Funkfeld;
            var f = a.Funkfeld();
            // Das alte Funkfeld stand links oben auf dem Hauptmonitor, nie bewegt; das Hauptfenster ist inzwischen auf dem zweiten.
            var bisher = new FensterOrt(140, 105, 700, 210);
            var neu = new FunkfeldFenster(f, a.Ablage, ZweiMonitore, () => (5760, 1080), bisher);
            fenster.Add(neu);
            var p = neu.Platzfuehrung.Gesetzt!;
            Pruefe.Wahr(p.Bildschirm == TestHaupt && p.Links == 140 && p.Oben == 105, T($"Funkfeld am bisherigen Platz, nicht beim Hauptfenster ({p.Links},{p.Oben})"));
            Pruefe.Gleich(Platzierung.Optimal(neu.LetzteWunschgroesse, TestHaupt), new Size(p.Breite, p.Hoehe), "in der optimalen Größe: eine Größe kommt nur aus dem Merken");
            Pruefe.Gleich(0, e.Fensterorte.Count, "nichts gemerkt");
            Pruefe.Gleich(0, neu.Platzfuehrung.Gemerkt, "auch nicht über die Platzführung");

            // Ein gemerkter Platz geht vor.
            e.Fensterorte["Raster-10"] = new FensterOrt(4000, 300, 800, 230);
            var gemerkt = new FunkfeldFenster(f, a.Ablage, ZweiMonitore, null, bisher);
            fenster.Add(gemerkt);
            Pruefe.Gleich(new Platz(4000, 300, 800, 230, TestZweiter), gemerkt.Platzfuehrung.Gesetzt, "gemerkt: genau so");

            // Ein abgesteckter Monitor gilt auch für den bisherigen Platz.
            e.Fensterorte.Clear();
            var weg = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([TestHaupt]), null, new FensterOrt(6000, 400));
            fenster.Add(weg);
            Pruefe.Wahr(weg.Platzfuehrung.Gesetzt is { } q && q.Bildschirm == TestHaupt && q.Rahmen.Rechts <= 3840, "bisher auf einem fehlenden Monitor: auf den nächsten");
            f.Dispose();

            // Hauptfenster: der bisherige Platz samt Größe und Maximiert, auch gegen einen älteren gemerkten.
            using var b = new Aufbau();
            var h = b.Haupt(out var funkfeld, out _, out _, out _);
            b.Ablage.Aktuell.HauptfensterOrt = new FensterOrt(100, 100, 1120, 780);
            // Klein ist seit 0.10.0 mindestens 900 hoch (Q89): Der bisherige Platz hat diese Höhe und passt so in den Arbeitsbereich des WQHD
            // (40 + 900 x 1,5 = 1390 von 1400); bis 0.9.3 stand hier 1300 x 700 an (2000, 100).
            var haupt = new Hauptfenster(h, b.Ablage, Gemischt, new FensterOrt(2000, 40, 1300, 900, true));
            fenster.Add(haupt);
            Pruefe.Gleich(new Platz(2000, 40, 1300, 900, Wqhd, true), haupt.Platzfuehrung.Gesetzt, "Hauptfenster am bisherigen Platz, eingerastet oder nicht");
            Pruefe.Gleich(WindowState.Maximized, haupt.WindowState, "maximiert wie vorher");
            Pruefe.Gleich(new FensterOrt(100, 100, 1120, 780), b.Ablage.Aktuell.HauptfensterOrt, "nichts gemerkt");
            funkfeld.Dispose();
        });

        // Die App gibt den Platz weiter: das Funkfeld beim Schließen (Neuaufbau und Wiederöffnen), das Hauptfenster beim Neuaufbau.
        var app = System.IO.File.ReadAllText(Quelle(@"src\Funk.Client\App.xaml.cs"));
        Pruefe.Wahr(app.Contains("_funkfeldZuletzt = z.Platzfuehrung.Aktuell()", StringComparison.Ordinal), "Funkfeld: Platz beim Schließen festgehalten");
        Pruefe.Wahr(app.Contains("bisher: _funkfeldZuletzt", StringComparison.Ordinal), "Funkfeld: neues Fenster am bisherigen Platz");
        Pruefe.Wahr(app.Contains("HauptfensterBauen(alt.Platzfuehrung.Aktuell())", StringComparison.Ordinal), "Hauptfenster: neues am Platz des alten");
    }

    private static string Quelle(string relativ)
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return System.IO.Path.Combine(d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden."), relativ);
    }

    /// <summary>Hauptfenster (A32): Lage, Größe und Maximiert gemerkt und wiederhergestellt, alter Stand übernommen.</summary>
    [Test]
    public static void Merken_Hauptfenster_LageGroesseUndMaximiert()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau();
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            var haupt = new Hauptfenster(h, a.Ablage, Gemischt);
            fenster.Add(haupt);
            var p = haupt.Platzfuehrung.Gesetzt!;
            Pruefe.Gleich(FullHd, p.Bildschirm, "erster Start: Hauptmonitor");
            Pruefe.Gleich(new Size(900, 900), new Size(haupt.Width, haupt.Height), "Voreinstellung der Größenklasse Klein (Startseite, seit 0.10.0 900 x 900, Q89)");
            Pruefe.Wahr(p.Links == 510 && p.Oben == 70, T($"mittig im Arbeitsbereich ({p.Links},{p.Oben})"));
            var klein = new Hauptfenster(h, a.Ablage, new FesteBildschirme([Laptop]));
            fenster.Add(klein);
            Pruefe.Wahr(klein.Height <= 728 && klein.Width <= 1366, T($"auf dem Laptop begrenzt ({klein.Width:0} x {klein.Height:0})"));

            haupt.Platzfuehrung.BewegungBeginnt();
            haupt.Left = 2000;
            haupt.Top = 100;
            haupt.Width = 1300;
            haupt.Platzfuehrung.BewegungEndet();
            var ort = a.Ablage.Aktuell.HauptfensterOrt!;
            Pruefe.Gleich(new FensterOrt(2000, 100, 1300, 900), ort, "gemerkt mit Größe (Skala des Monitors aus dem Platz: 1)");
            // Maximieren: Ohne Fensterhandle meldet WPF StateChanged nicht selbst; der Test ruft OnStateChanged wie WPF nach WM_SIZE.
            haupt.WindowState = WindowState.Maximized;
            typeof(Window).GetMethod("OnStateChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(haupt, [EventArgs.Empty]);
            Pruefe.Wahr(a.Ablage.Aktuell.HauptfensterOrt!.Maximiert, "maximiert gemerkt");
            var neu = new Hauptfenster(h, a.Ablage, Gemischt);
            fenster.Add(neu);
            Pruefe.Gleich(WindowState.Maximized, neu.WindowState, "Neustart: maximiert");
            Pruefe.Wahr(Math.Abs(neu.Left - (2000 / 1.5)) < 0.01 && Math.Abs(neu.Width - 1300) < 0.01, T($"mit der Lage des Normalzustands auf dem WQHD ({neu.Left:0.##}, {neu.Width:0})"));

            // Alter Stand in Einheiten: mit der Skala des Hauptmonitors umgesetzt; bewegt, gilt der neue. Eine so alte Datei kennt
            // auch die Größen je Klasse nicht.
            a.Ablage.Aktuell.HauptfensterOrt = null;
            a.Ablage.Aktuell.HauptfensterGroessen = null;
            a.Ablage.Aktuell.Hauptfenster = new FensterStand(50, 40, 1000, 700);
            var alt = new Hauptfenster(h, a.Ablage, ZweiMonitore);
            fenster.Add(alt);
            Pruefe.Wahr(alt.Platzfuehrung.Gesetzt is { Links: 88, Oben: 70 } && Math.Abs(alt.Width - 900) < 0.01, "alter Stand bei 175 %: 88,70; die Startseite bleibt klein, die alte Größe gilt den Seiten");
            alt.Platzfuehrung.BewegungBeginnt();
            alt.Top += 10;
            alt.Platzfuehrung.BewegungEndet();
            Pruefe.Gleich(null, a.Ablage.Aktuell.Hauptfenster, "abgelöst");
            Pruefe.Wahr(a.Ablage.Aktuell.HauptfensterOrt is { Oben: 88 }, "neuer Platz in Bildpunkten: " + a.Ablage.Aktuell.HauptfensterOrt);
            // Abgesteckt: auf den nächsten Monitor.
            a.Ablage.Aktuell.HauptfensterOrt = new FensterOrt(5000, 200, 1120, 780);
            var weg = new Hauptfenster(h, a.Ablage, new FesteBildschirme([TestHaupt]));
            fenster.Add(weg);
            Pruefe.Wahr(weg.Platzfuehrung.Gesetzt is { } q && q.Rahmen.Rechts <= 3840, "Monitor abgesteckt: auf den Hauptmonitor");
            funkfeld.Dispose();
        });
    }
}
