// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Glasdesign;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Stufe 3, Schritt 3.4 im Speicher gezeichnet (<see cref="Bildlauf"/>, kein Fensterhandle): das Funkfeld mit Kanälen. Raster bleibt
/// etwa 776 x 193 mit Rollenliste und "Mehrere senden" in der Titelzeile, die Liste etwa 288 breit (Schritt 3.5: mit VA/Taste je Feld) mit der Rolle in einer zweiten
/// Kopfzeile; feste Mindestgröße je Ansicht und Skalierung -30 bis +30 % (Sarevoc Q12); Knöpfe seit 0.11.2 flach (bis 0.11.1 Glas nur in den Knöpfen), VA leuchtet grün (Q15);
/// Statuspunkte mit Kontrast in Anthrazit. Mit <c>FUNK_BILDER</c> die Sichtproben für Sarevoc (Q22).
/// </summary>
internal static class OberflaecheStufe34Tests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static readonly Bildschirm Monitor100 = new(new Pixelrechteck(0, 0, 3840, 2160), new Pixelrechteck(0, 0, 3840, 2076), 1.0, true);

    private static FesteBildschirme Monitore { get; } = new([Monitor100]);

    private static IEnumerable<FrameworkElement> Benannt(DependencyObject wurzel, string name) =>
        Bildlauf.Nachfahren<FrameworkElement>(wurzel).Where(e => e.Name == name);

    /// <summary>Lage eines Elements im Fenster (Einheiten).</summary>
    private static Rect Lage(FrameworkElement e, Visual wurzel) => e.TransformToAncestor(wurzel).TransformBounds(new Rect(e.RenderSize));

    /// <summary>Das Element des Felds <paramref name="platz"/> mit diesem Namen (aus der Vorlage der Kachel).</summary>
    private static FrameworkElement ImFeld(FunkfeldFenster ff, int platz, string name) =>
        Benannt(Bildlauf.Wurzel(ff), name).First(e => e.DataContext is KachelViewModel k && k.Platz == platz && !k.IstGlobal);

    private static (FunkfeldFenster Fenster, FunkfeldViewModel Modell) Bauen(Aufbau a, List<Window> fenster, FunkfeldDarstellung d = FunkfeldDarstellung.Raster, int n = 10)
    {
        a.Ablage.Aktuell.Funkfeld.Darstellung = d;
        a.Ablage.Aktuell.Funkfeld.Ansicht = n;
        var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
        var ff = new FunkfeldFenster(f, a.Ablage, Monitore);
        fenster.Add(ff);
        return (ff, f);
    }

    [Test]
    public static void Groessen_RasterUndListe_RolleUndMehrfachInDerTitelzeile()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
            var (ff, f) = Bauen(a, fenster);
            var (_, raster) = Bildlauf.Zeichnen(ff);
            Console.WriteLine(string.Create(De, $"MASS Stufe 3.4 Raster 10: {raster.Width:0.#} x {raster.Height:0.#} Einheiten"));
            Pruefe.Wahr(raster.Width <= 780 && raster.Width >= 770 && raster.Height <= 200, string.Create(De, $"Raster bleibt etwa 776 x 193: {raster.Width:0.#} x {raster.Height:0.#}"));
            var wurzel = Bildlauf.Wurzel(ff);
            var kopf = (FrameworkElement)ff.FindName("Kopf");
            var rolle = (FrameworkElement)ff.FindName("RolleWahl");
            var mehrfach = (FrameworkElement)ff.FindName("MehrfachSchalter");
            Pruefe.Wahr(rolle.IsVisible || rolle.Visibility == Visibility.Visible, "Rollenliste sichtbar");
            Pruefe.Wahr(Math.Abs(rolle.ActualWidth - 196) < 1, string.Create(De, $"Rollenliste 196 breit (168 plus Platz für (geändert), Schritt 3.5): {rolle.ActualWidth:0.#}"));
            var lr = Lage(rolle, wurzel);
            Pruefe.Wahr(lr.Left < 40 && lr.Top < 4, string.Create(De, $"oben links: {lr}"));
            Pruefe.Gleich(Visibility.Visible, mehrfach.Visibility, "Mehrere senden: Max ist Führung");
            foreach (var kind in Bildlauf.Nachfahren<FrameworkElement>(kopf).Where(e => e.Visibility == Visibility.Visible && e.ActualWidth > 0))
            {
                var r = Lage(kind, kopf);
                Pruefe.Wahr(r.Right <= kopf.ActualWidth + 0.5, string.Create(De, $"Titelzeile ohne Überlauf: {kind.GetType().Name} {kind.Name} endet bei {r.Right:0.#} von {kopf.ActualWidth:0.#}"));
            }
            var kopfText = (FrameworkElement)ff.FindName("KopfText");
            // Seit Klangstufe C2b (Q34) steht der Mikro-Knopf zwischen Text und Regler; er nimmt seine Breite aus diesem Platz, das Raster bleibt 776 breit.
            var mikro = (FrameworkElement)ff.FindName("MikroUnterlage");
            Pruefe.Wahr(kopfText.ActualWidth + mikro.ActualWidth + mikro.Margin.Left >= 150 && kopfText.ActualWidth >= 120,
                string.Create(De, $"Platz für 'Senden: ...': {kopfText.ActualWidth:0.#} (Mikro-Knopf {mikro.ActualWidth:0.#})"));
            Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(rolle).Any(t => t.Text == "3 Kapitän"), "zeigt 3 Kapitän");
            Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(ImFeld(ff, 1, "ChWaehler")).Any(t => t.Text == "Schiff 3"), "der Ch.-Wähler zeigt Schiff 3");

            // Q44 (0.8.1): Der Name ist eine feste Beschriftung ohne Pfeil und ohne eigene Klickfläche (der Klick wählt den Sendekanal).
            // Gekürzte Namen ("Bergung und Bergbau") stehen voll im Tooltip der Kachel, mit der Herkunft des Kanals.
            Pruefe.Falsch(Benannt(wurzel, "NameWahl").Any(), "keine Kanalwahl am Namen mehr");
            var namen = Benannt(wurzel, "NameText").ToList();
            Pruefe.Wahr(namen.Count == f.Kacheln.Count && namen.All(n => n is TextBlock { IsHitTestVisible: false }), "je Kachel ein Name als Text, ohne Klickfläche: " + namen.Count);
            var kopfStil = (Style)ff.FindResource("Kachelkopf");
            var koepfe = Bildlauf.Nachfahren<Button>(wurzel).Where(b => ReferenceEquals(b.Style, kopfStil)).ToList();
            Pruefe.Gleich(f.Kacheln.Count, koepfe.Count, "je Kachel ein Kachelkopf");
            foreach (var knopf in koepfe)
            {
                var k = (KachelViewModel)knopf.DataContext;
                var hilfe = Hilfe.GetText(knopf) ?? "";
                var soll = k.Herkunft is { } h ? k.KanalText + ": " + h : k.PunktHilfe;
                Pruefe.Wahr(hilfe.StartsWith(soll, StringComparison.Ordinal), "Tooltip mit dem vollen Namen und woher der Kanal kommt: " + hilfe);
            }

            f.Darstellung = FunkfeldDarstellung.Liste;
            ff.DarstellungAnwenden();
            var (_, liste) = Bildlauf.Zeichnen(ff);
            Console.WriteLine(string.Create(De, $"MASS Stufe 3.4 Liste 10: {liste.Width:0.#} x {liste.Height:0.#} Einheiten"));
            Pruefe.Wahr(liste.Width >= 282 && liste.Width <= 294, string.Create(De, $"Liste etwa 288 breit (264 plus VA/Taste je Feld, Schritt 3.5): {liste.Width:0.#}"));
            Pruefe.Wahr(liste.Height <= 350, string.Create(De, $"Liste 10 kompakt: {liste.Height:0.#}"));
            var rolleListe = (FrameworkElement)ff.FindName("RolleWahlListe");
            var lr2 = Lage(rolleListe, Bildlauf.Wurzel(ff));
            Pruefe.Wahr(lr2.Top >= 23 && lr2.Left < 10 && rolleListe.ActualWidth > 200, string.Create(De, $"Liste: Rolle in der zweiten Kopfzeile über die Breite: {lr2}"));
            Pruefe.Gleich(Visibility.Visible, ((FrameworkElement)ff.FindName("MehrfachSymbol")).Visibility, "Liste: Mehrere senden als Symbol");
            f.Dispose();
        });
    }

    [Test]
    public static void Mindestgroesse_FestJeAnsicht_UndSkalierungMinus30Bis30()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var groessen = new Dictionary<(FunkfeldDarstellung, int), Size>();
            foreach (var d in new[] { FunkfeldDarstellung.Raster, FunkfeldDarstellung.Liste })
            {
                foreach (var s in new[] { -30, 0, 30 })
                {
                    using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
                    a.Ablage.Aktuell.Funkfeld.Skalierung = s;
                    var (ff, f) = Bauen(a, fenster, d);
                    var w = ff.LetzteWunschgroesse;
                    groessen[(d, s)] = w;
                    Console.WriteLine(string.Create(De, $"MASS Stufe 3.4 {d} 10 bei {s:+0;-0;0} %: {w.Width:0.#} x {w.Height:0.#} Einheiten, Mindestgröße {ff.MinWidth:0.#} x {ff.MinHeight:0.#}"));
                    Pruefe.Wahr(Math.Abs(ff.MinWidth - w.Width) < 1 && Math.Abs(ff.MinHeight - w.Height) < 1,
                        string.Create(De, $"{d} {s} %: Mindestgröße = Inhalt ({ff.MinWidth:0.#} x {ff.MinHeight:0.#} gegen {w.Width:0.#} x {w.Height:0.#})"));
                    Pruefe.Wahr(ff.Width >= ff.MinWidth - 0.01 && ff.Height >= ff.MinHeight - 0.01, d + " " + s + " %: nie kleiner");
                    f.Dispose();
                }
                var basis = groessen[(d, 0)];
                foreach (var s in new[] { -30, 30 })
                {
                    var g = groessen[(d, s)];
                    var faktor = 1 + (s / 100.0);
                    Pruefe.Wahr(Math.Abs((g.Width / basis.Width) - faktor) < 0.03 && Math.Abs((g.Height / basis.Height) - faktor) < 0.03,
                        string.Create(De, $"{d} {s} %: {g.Width / basis.Width:0.###} x {g.Height / basis.Height:0.###} statt {faktor}"));
                }
            }

            // Eine früher kleiner gezogene Größe wird auf die Mindestgröße angehoben.
            using (var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel()))
            {
                a.Ablage.Aktuell.Funkfeld.Fensterorte["Raster-10"] = new FensterOrt(100, 100, 500, 120);
                var (ff, f) = Bauen(a, fenster);
                Pruefe.Wahr(ff.Width >= ff.MinWidth - 0.01 && ff.Height >= ff.MinHeight - 0.01 && ff.MinWidth > 700,
                    string.Create(De, $"gezogene 500 x 120 angehoben auf {ff.Width:0.#} x {ff.Height:0.#}"));
                f.Dispose();
            }
        });
    }

    /// <summary>
    /// Die Knöpfe des Funkfelds: Von Stufe 3.4 bis 0.11.1 aus Glas (VA/Taste, Aus, Mehrere senden, Mikro), seit 0.11.2 flach (Sarevoc 13.10.2026: "überall
    /// die Glasbuttons raus"); kein Glaskörper mehr im Funkfeld. VA leuchtet weiter grün, Taste nicht.
    /// </summary>
    [Test]
    public static void Flach_KeinGlas_VaLeuchtetGruen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
            var (ff, f) = Bauen(a, fenster);
            var (bild, _) = Bildlauf.Zeichnen(ff);
            var wurzel = Bildlauf.Wurzel(ff);
            Pruefe.Falsch(Bildlauf.Nachfahren<Glaskoerper>(wurzel).Any(), "kein Glas im Funkfeld");
            // Gezeichnet wird nur, was sichtbar ist: VA/Taste in jedem Feld und Global, Aus in jedem Feld mit Kanal (nicht auf Ruhe), Mehrere senden.
            bool Sichtbar(DependencyObject d)
            {
                // Bis zur Wurzel: Ein nie gezeigtes Fenster steht selbst auf Collapsed (CODEMAP, Fallen).
                for (var p = d; p is not null && !ReferenceEquals(p, wurzel); p = VisualTreeHelper.GetParent(p))
                    if (p is UIElement { Visibility: not Visibility.Visible }) return false;
                return true;
            }
            var schalter = Benannt(wurzel, "VaSchalter").Where(Sichtbar).ToList();
            Pruefe.Gleich(f.Kacheln.Count, schalter.Count, "VA/Taste in jedem Feld und Global, flach");
            var va = ImFeld(ff, 3, "VaSchalter");
            Pruefe.Wahr(va is ToggleButton { IsChecked: true }, "Brücke auf VA");
            var r = Lage(va, wurzel);
            var mitte = Bildlauf.Pixel(bild, r.Left + 6, r.Top + (r.Height / 2));
            Pruefe.Wahr(mitte.G > mitte.R + 50 && mitte.G > mitte.B + 50, "VA leuchtet grün: " + mitte);
            Pruefe.Wahr(ImFeld(ff, 0, "VaSchalter") is ToggleButton { IsChecked: true, IsEnabled: true }, "Kommando mit Sprachaktivierung (seit 0.9.1, Q53; bis 0.9.0 nur Taste)");
            // Der Knopf Taste, der nicht leuchten darf: seit 0.9.1 am festen Global-Feld (Gesamt, nur Taste) statt an Kommando.
            var taste = Benannt(Bildlauf.Wurzel(ff), "VaSchalter").First(e => e.DataContext is KachelViewModel { IstGlobal: true });
            Pruefe.Wahr(taste is ToggleButton { IsChecked: false }, "Global (Gesamt) nur Taste");
            var rt = Lage(taste, wurzel);
            var grau = Bildlauf.Pixel(bild, rt.Left + 6, rt.Top + (rt.Height / 2));
            Pruefe.Falsch(grau.G > grau.R + 50 && grau.G > grau.B + 50, "Taste leuchtet nicht: " + grau);
            f.Dispose();
        });
    }

    // 0.11.2: Gelöscht ist Renderzeit_GlasknoepfeGegenFlach_Gemessen (was das Glas in den Knöpfen des Funkfelds an Renderzeit kostete, gegen flache
    // Vorlagen an denselben Knöpfen). Seit 0.11.2 sind die Knöpfe flach (Sarevoc 13.10.2026), es gibt kein Glas mehr zu messen.

    [Test]
    public static void Statuspunkte_KontrastInP1BisP3_UndOhnePunktAusgegraut()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var stellung in Enum.GetValues<Farbstellung>())
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
                a.Ablage.Aktuell.Farbstellung = stellung;
                var (ff, f) = Bauen(a, fenster);
                const double skala = 1.75;
                var (bild, _) = Bildlauf.Zeichnen(ff, skala);
                var wurzel = Bildlauf.Wurzel(ff);
                foreach (var (platz, was) in new[] { (3, "grün"), (0, "gelb"), (6, "rot"), (5, "grauer Ring") })
                {
                    var punkt = ImFeld(ff, platz, "Punkt");
                    Pruefe.Gleich(Visibility.Visible, punkt.Visibility, was + ": Punkt da");
                    var r = Lage(punkt, wurzel);
                    // Der Ring ist nur am Rand gefärbt: dort messen, sonst in der Mitte.
                    var p = Bildlauf.Pixel(bild, platz == 5 ? r.Left + 0.6 : r.Left + (r.Width / 2), r.Top + (r.Height / 2), skala);
                    // Die Kachel darüber, zwischen Punkt und dem Rand von SENDET (3 breit).
                    var grund = Bildlauf.Pixel(bild, r.Left + (r.Width / 2), r.Top - 3, skala);
                    var k = Kontrast.Verhaeltnis(Color.FromRgb(p.R, p.G, p.B), Color.FromRgb(grund.R, grund.G, grund.B));
                    Pruefe.Wahr(k >= 3, string.Create(De, $"{stellung} {was}: Punkt gegen Kachel {k:0.00}:1"));
                }
                Pruefe.Gleich(Visibility.Collapsed, ImFeld(ff, 4, "Punkt").Visibility, stellung + ": Ruhe ohne Punkt");
                Pruefe.Gleich(Visibility.Collapsed, ImFeld(ff, 7, "Punkt").Visibility, stellung + ": leer ohne Punkt");
                f.Dispose();
            }
        });
    }

    [Test]
    public static void Sichtproben_FuerJu()
    {
        if (Bildablage.Ordner() is null) return;
        Bildlauf.Ausfuehren(fenster =>
        {
            Sichtprobe(fenster, Farbstellung.Anthrazit, "anthrazit", mitListe: true);
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            Skalierungsbogen(fenster);
        });
    }

    /// <summary>
    /// Die Vorführdaten mit einer Sperre mehr, die nur in der Liste des Ch.-Wählers auffällt: Brücke Schiff 7 für Max (von Nordlicht
    /// bis 04:00), ein grauer Eintrag mit Grund. Bis 0.8.0 lag hier auch Reserve 7 ganz gesperrt für die Kanalwahl am Namen (seit Q44 weg).
    /// </summary>
    private static FunkLage Sichtszene()
    {
        var l = FunkDienstAttrappe.KanalBeispiel();
        var bis = l.Gesperrt[0].Bis;
        ushort Netz(string name) => l.Netze.Single(n => n.Name == name).Id;
        return l with
        {
            Gesperrt = [.. l.Gesperrt, new KanalsperreInfo(Funk.Protokoll.Steuerung.Kanalrechnung.Id(Netz("Brücke"), 7), bis, 4, false)],
        };
    }

    private static void Sichtprobe(List<Window> fenster, Farbstellung stellung, string kurz, bool mitListe)
    {
        Bildlauf.RessourcenSetzen(stellung);
        using var a = new Aufbau(Sichtszene());
        a.Ablage.Aktuell.Farbstellung = stellung;
        var (ff, f) = Bauen(a, fenster);
        var (raster, rg) = Bildlauf.Zeichnen(ff, 1.75);
        Bildablage.Ablegen(raster, "34-01-raster-" + kurz);
        if (!mitListe)
        {
            f.Dispose();
            return;
        }

        // Ch.-Wähler und Rollenliste offen: die Liste neben dem Fenster gezeichnet, wo das Klappfeld erscheint. Das Bild 34-03 (Kanalwahl am
        // Namen) gibt es seit 0.8.1 nicht mehr (Q44); die Nummern der übrigen bleiben, damit sie sich mit älteren Läufen vergleichen lassen.
        Bildablage.Ablegen(MitKlappliste(ff, ImFeld(ff, 0, "ChWaehler"), rg), "34-02-ch-waehler-offen-" + kurz);
        Bildablage.Ablegen(MitKlappliste(ff, (FrameworkElement)ff.FindName("RolleWahl"), rg), "34-04-rollenliste-offen-" + kurz);

        f.MehrfachAn = true;
        a.Dienst.LageSetzen(l => l with { SendeFelder = [1, 3] });
        Bildlauf.Leeren();
        var (mehrfach, _) = Bildlauf.Zeichnen(ff, 1.75);
        Bildablage.Ablegen(mehrfach, "34-05-mehrfachsenden-an-" + kurz);
        f.MehrfachAn = false;
        a.Dienst.LageSetzen(l => l with { SendeFelder = [3] });

        f.Darstellung = FunkfeldDarstellung.Liste;
        ff.DarstellungAnwenden();
        var (liste, _) = Bildlauf.Zeichnen(ff, 1.75);
        Bildablage.Ablegen(liste, "34-06-liste-" + kurz);
        f.Dispose();
    }

    /// <summary>Das Fenster und darunter die Liste des Klappfelds <paramref name="klapp"/> an ihrer Stelle, bei 175 %.</summary>
    private static RenderTargetBitmap MitKlappliste(FunkfeldFenster ff, FrameworkElement klapp, Size groesse)
    {
        const double skala = 1.75;
        var wahl = (Klappwahl)klapp;
        var liste = wahl.Liste ?? throw new PruefungException("Klappfeld ohne Liste");
        liste.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Bildlauf.Leeren();
        liste.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        liste.Arrange(new Rect(liste.DesiredSize));
        liste.UpdateLayout();
        var r = Lage(klapp, Bildlauf.Wurzel(ff));
        var (bild, _) = Bildlauf.Zeichnen(ff, skala);
        var lb = new RenderTargetBitmap((int)Math.Ceiling(liste.ActualWidth * skala), (int)Math.Ceiling(liste.ActualHeight * skala), 96 * skala, 96 * skala, PixelFormats.Pbgra32);
        lb.Render(liste);
        var b = new Bogen(Math.Max(groesse.Width, r.Left + liste.ActualWidth) + 16, r.Bottom + liste.ActualHeight + 16, skala, OberflaecheFunkfeldBilderTests.Grund);
        b.Bild(bild, 0, 0, groesse.Width, groesse.Height);
        b.Bild(lb, r.Left, r.Bottom + 2, liste.ActualWidth, liste.ActualHeight);
        return b.Fertig();
    }

    private static void Skalierungsbogen(List<Window> fenster)
    {
        var bilder = new List<(int S, RenderTargetBitmap Bild, Size G)>();
        foreach (var s in new[] { -30, 0, 30 })
        {
            using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
            a.Ablage.Aktuell.Funkfeld.Skalierung = s;
            var (ff, f) = Bauen(a, fenster);
            var (bild, g) = Bildlauf.Zeichnen(ff);
            bilder.Add((s, bild, g));
            f.Dispose();
        }
        var b = new Bogen(bilder.Max(x => x.G.Width) + 48, bilder.Sum(x => x.G.Height + 40) + 40, 1, OberflaecheFunkfeldBilderTests.Grund);
        var y = 16.0;
        foreach (var (s, bild, g) in bilder)
        {
            b.Text(string.Create(De, $"Skalierung {s:+0;-0;0} %: {g.Width:0} x {g.Height:0} Einheiten (Mindestgröße)"), 24, y, 13, fett: true);
            b.Bild(bild, 24, y + 22, g.Width, g.Height);
            y += g.Height + 40;
        }
        Bildablage.Ablegen(b.Fertig(), "34-07-skalierung-anthrazit");
    }
}
