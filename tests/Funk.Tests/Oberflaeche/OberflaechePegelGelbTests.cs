// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Maschine;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Q43 (0.8.0): Die Pegelanzeigen (Pegelbalken auf Startseite, in der Leiste und unter Audio, Pegelkurve der Schwellengrafik, Pegel auf den Feldern
/// des Funkfelds) haben drei Zustände: weiß (grau im Balken), solange nichts auslöst; gelb, wenn die Sprachaktivierung auslösen würde, aber nichts
/// hinausgeht (Ch. Ruhe, kein Kanal, nicht verbunden, stumm durch die Einsatzleitung), auch ohne Kanal; grün, sobald tatsächlich etwas hinausgeht.
/// Gelb und Grün kommen aus der Farbstellung, der Tooltip sagt es in Wörtern. Mikro stumm bleibt bei "stumm": Die Maschine verwirft dort jeden
/// Rahmen vor der Messung (Klangstufe C2a), es gibt nichts, woraus Gelb sich rechnen ließe.
/// </summary>
internal static class OberflaechePegelGelbTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Der Satz, den der Tooltip wörtlich tragen soll.</summary>
    private const string GelbSatz = "Gelb: würde senden, geht aber nirgends hin";

    /// <summary>Eine Pegelmeldung über der Linie mit Sprache; <paramref name="sendet"/> ist, was die Maschine meldet.</summary>
    private static void Sprechen(Aufbau a, bool sendet, float db = -20f)
    {
        a.Uhr += 200;
        a.Maschine.Melden(new PegelMeldung(a.Uhr, db, -62, -40, 0.95f, sendet));
    }

    private static bool Gruenlich(Color c) => c.G > c.R + 60 && c.G > c.B + 30;

    /// <summary>Nahe an der Probefarbe Lila der Farbstellung im Test (kein Gelb, kein Grün, kein Weiß).</summary>
    private static bool Lilalich(Color c) => c.R > c.G + 80 && c.B > c.G + 80;

    private static bool Nah(Color a, Color b, int toleranz = 3) =>
        Math.Abs(a.R - b.R) <= toleranz && Math.Abs(a.G - b.G) <= toleranz && Math.Abs(a.B - b.B) <= toleranz;

    /// <summary>Die Farbe <paramref name="flaeche"/> mit ihrer Deckkraft über <paramref name="grund"/>, wie WPF sie beim Zeichnen mischt.</summary>
    private static Color Ueber(Color grund, Color flaeche)
    {
        double a = flaeche.A / 255.0;
        byte K(byte g, byte f) => (byte)Math.Round((g * (1 - a)) + (f * a));
        return Color.FromRgb(K(grund.R, flaeche.R), K(grund.G, flaeche.G), K(grund.B, flaeche.B));
    }

    private static double V(Color a, Color b) => Kontrast.Verhaeltnis(a, b);

    // ------------------------------------------------------------------------------------------------------------------------------
    // Der Verlauf rechnet "würde senden"
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Der Pegelverlauf setzt <see cref="PegelMeldung.WuerdeSenden"/> nur, wenn der Weg nicht offen steht, und dann genau dort, wo der Satz "es wird
    /// nichts gesendet" anspringt: Die Maschine sendet (Taste, offene Sprachaktivierung) oder es wird über der Linie gesprochen. Offen gibt es nur
    /// Grün oder Weiß, und was die Maschine selbst als "würde senden" meldete, nimmt der Verlauf weg. Bei stummem Mikro nie.
    /// </summary>
    [Test]
    public static void PegelGelb_Verlauf_DreiZustaende()
    {
        var v = new PegelVerlauf();
        var zeit = 0L;
        PegelMeldung Aufnehmen(float db, float p, bool sendet, bool stumm = false)
        {
            zeit += 20;
            v.Hinzufuegen(new PegelMeldung(zeit, db, -62, -40, p, sendet, stumm));
            Pruefe.Wahr(v.Letzte(out var letzte), "abgelegt");
            return letzte;
        }

        Pruefe.Gleich(Sendeweg.Offen, v.Weg, "ohne Funkfeld offen");
        var m = Aufnehmen(-20, 0.95f, sendet: true);
        Pruefe.Wahr(m.Sendet && !m.WuerdeSenden, "offen und gesendet: grün");
        m = Aufnehmen(-20, 0.95f, sendet: false);
        Pruefe.Falsch(m.Sendet || m.WuerdeSenden, "offen, über der Linie, aber die Maschine sendet nicht (Kanal nur mit Taste, Anlauf): weiß");
        zeit += 20;
        v.Hinzufuegen(new PegelMeldung(zeit, -20, -62, -40, 0.95f, false, WuerdeSenden: true));
        Pruefe.Falsch(v.Letzte(out m) && m.WuerdeSenden, "nur der Verlauf setzt 'würde senden', nie die Maschine");

        foreach (var weg in new[] { Sendeweg.Ruhe, Sendeweg.Leitung, Sendeweg.Getrennt })
        {
            v.Weg = weg;
            m = Aufnehmen(-20, 0.95f, sendet: false);
            Pruefe.Wahr(!m.Sendet && m.WuerdeSenden, weg + ": über der Linie gesprochen, Sprachaktivierung der Maschine aus (A10): gelb");
            Pruefe.Gleich(PegelVerlauf.HinweisText(weg), v.Hinweis(m), weg + ": dazu der Satz in Wörtern");
            m = Aufnehmen(-20, 0.95f, sendet: true);
            Pruefe.Wahr(!m.Sendet && m.WuerdeSenden, weg + ": Sprechtaste oder offene Sprachaktivierung: gelb, nie grün");
            m = Aufnehmen(-50, 0.95f, sendet: false);
            Pruefe.Falsch(m.WuerdeSenden, weg + ": unter der Linie: weiß");
            m = Aufnehmen(-20, 0.1f, sendet: false);
            Pruefe.Falsch(m.WuerdeSenden, weg + ": Geräusch ohne Sprache: weiß");
            m = Aufnehmen(-120, 0f, sendet: false, stumm: true);
            Pruefe.Falsch(m.WuerdeSenden, weg + ": Mikro stumm: kein Gelb, die Maschine misst dann nichts");
        }

        v.Weg = Sendeweg.Offen;
        m = Aufnehmen(-20, 0.95f, sendet: true);
        Pruefe.Wahr(m.Sendet && !m.WuerdeSenden, "wieder offen: grün, kein Gelb");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Alle Anzeigen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Pegelbalken (Startseite, Audio), Leiste und Grafik und die Felder des Funkfelds: gelb auf Ch. Ruhe (auch wenn die Maschine wegen A10 gar nicht
    /// sendet), stumm durch die Einsatzleitung und ohne Verbindung; grün nur mit echtem Kanal; weiß unter der Linie. Auf dem Funkfeld steht das Gelb
    /// auf den Feldern der Sendeauswahl, auch auf dem Feld mit Ch. Ruhe. Mikro stumm zeigt weiter "stumm".
    /// </summary>
    [Test]
    public static void PegelGelb_Anzeigen_WuerdeSendenGehtNirgendsHin()
    {
        var l = FunkDienstAttrappe.KanalBeispiel();
        using var a = new Aufbau(l with { SendeFelder = [4], GlobalSenden = false });
        var f = a.Funkfeld();
        var audio = a.Audio();
        var schwelle = a.Schwelle();
        void Takt()
        {
            audio.Takt();
            schwelle.Takt();
            a.Uhr += 100;
            f.Takt();
        }
        var ruhe = f.Kacheln[4];
        Pruefe.Gleich(KachelZustand.Ruhe, ruhe.Zustand, "Feld 5 steht auf Ch. Ruhe und ist zum Senden gewählt");
        Pruefe.Falsch(a.Maschine.Sprachaktivierung, "ohne Kanal keine Sprachaktivierung in der Maschine (A10)");

        Sprechen(a, sendet: false);
        Takt();
        Pruefe.Wahr(audio.WuerdeSenden && schwelle.WuerdeSenden, "Ch. Ruhe, über der Linie gesprochen: Pegelbalken, Leiste und Grafik gelb");
        Pruefe.Falsch(audio.Sendet || schwelle.Sendet || f.SendetGerade, "Ch. Ruhe: nirgends grün");
        Pruefe.Wahr(ruhe.WuerdeSenden && !ruhe.EigeneSendung && ruhe.PegelAnteil > 0, T($"Ch. Ruhe: das gewählte Feld zeigt den eigenen Pegel gelb ({ruhe.PegelAnteil})"));
        Pruefe.Falsch(f.Kacheln.Where(k => !ReferenceEquals(k, ruhe)).Any(k => k.WuerdeSenden), "nur das gewählte Feld");

        Sprechen(a, sendet: false, db: -70);
        Takt();
        Pruefe.Falsch(audio.WuerdeSenden || schwelle.WuerdeSenden || ruhe.WuerdeSenden, "unter der Linie: weiß, auch auf dem Feld");

        a.Dienst.LageSetzen(x => x with { SendeFelder = [3] });
        var bruecke = f.Kacheln[3];
        Pruefe.Wahr(bruecke.IstSenden, "Feld 4 sendet");
        Sprechen(a, sendet: true);
        Takt();
        Pruefe.Wahr(audio.Sendet && schwelle.Sendet && bruecke.EigeneSendung, "echter Kanal: grün");
        Pruefe.Falsch(audio.WuerdeSenden || schwelle.WuerdeSenden || f.Kacheln.Any(k => k.WuerdeSenden), "echter Kanal: kein Gelb");
        Sprechen(a, sendet: false);
        Takt();
        Pruefe.Falsch(audio.Sendet || audio.WuerdeSenden || bruecke.WuerdeSenden, "Weg offen, über der Linie, die Maschine sendet nicht: weiß");

        a.Dienst.LageSetzen(x => x with { Stumm = [new StummschaltungInfo(0, a.Jetzt.AddMinutes(30), 4)] });
        Sprechen(a, sendet: true);
        Takt();
        Pruefe.Wahr(audio.WuerdeSenden && schwelle.WuerdeSenden && !audio.Sendet, "stumm durch die Einsatzleitung: gelb");
        Pruefe.Wahr(bruecke.WuerdeSenden && !bruecke.EigeneSendung, "stumm durch die Einsatzleitung: das Sendefeld zeigt den eigenen Pegel gelb");

        a.Dienst.LageSetzen(x => x with { Stumm = [], Status = Verbindungsstatus.Getrennt });
        Sprechen(a, sendet: false);
        Takt();
        Pruefe.Wahr(audio.WuerdeSenden && schwelle.WuerdeSenden && !audio.Sendet, "nicht verbunden: gelb");

        a.Maschine.MikroStumm = true;
        a.Uhr += 200;
        a.Maschine.Melden(new PegelMeldung(a.Uhr, -120, -62, -40, 0f, false, Stumm: true));
        Takt();
        Pruefe.Wahr(audio.Stumm && !audio.WuerdeSenden && !schwelle.WuerdeSenden, "Mikro stumm: 'stumm' statt Gelb");
        f.Dispose();
    }

    /// <summary>Der Pegelbalken nimmt das Gelb aus der Farbstellung (Ressource Farbe.WuerdeSenden), kein festes; Grün geht vor; der Tooltip in Wörtern.</summary>
    [Test]
    public static void PegelGelb_Pegelbalken_GelbDerFarbstellung()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var gelb = Color.FromRgb(0xC8, 0x50, 0xE0);
            var gruen = Color.FromRgb(0x20, 0xB0, 0x90);
            var balken = new[]
            {
                new Pegelbalken { Width = 200, Height = 10, PegelDb = -10, WuerdeSenden = true },
                new Pegelbalken { Width = 200, Height = 10, PegelDb = -10, WuerdeSenden = true, Sendet = true },
                new Pegelbalken { Width = 200, Height = 10, PegelDb = -10 },
            };
            var stapel = new StackPanel();
            foreach (var b in balken)
            {
                stapel.Children.Add(b);
                stapel.Children.Add(new Border { Height = 10 });
            }
            var w = new Window { Content = stapel, Background = Brushes.Black };
            w.Resources["Farbe.WuerdeSenden"] = gelb;
            w.Resources["Farbe.Senden"] = gruen;
            fenster.Add(w);
            var (bild, _) = Bildlauf.Zeichnen(w, 1, new Size(240, 80));
            Color Farbe(Pegelbalken b)
            {
                var o = b.TransformToAncestor(Bildlauf.Wurzel(w)).Transform(new Point(0, 0));
                var p = Bildlauf.Pixel(bild, o.X + 30, o.Y + 5);
                return Color.FromRgb(p.R, p.G, p.B);
            }
            Pruefe.Gleich(gelb, Farbe(balken[0]), "würde senden: das Gelb der Farbstellung");
            Pruefe.Gleich(gruen, Farbe(balken[1]), "sendet: Grün geht vor");
            Pruefe.Gleich(Palette.Text2, Farbe(balken[2]), "sonst grau wie bisher");
            var hilfe = balken[0].Hilfe(Pegelbalken.Teil.Balken) ?? "";
            Pruefe.Wahr(hilfe.Contains(GelbSatz, StringComparison.Ordinal), "Tooltip in Wörtern: " + hilfe);
            Pruefe.Wahr(KorridorViewModel.HilfeBalken.StartsWith("Grün heißt: wird gesendet", StringComparison.Ordinal), "der Tooltip beginnt wie bisher");
        });
    }

    /// <summary>
    /// Die Schwellengrafik: Pegelkurve und Hinterlegung gelb, wo es gesendet hätte, aber nichts hinausging (hier auf Ruhe), grün, wo gesendet wurde;
    /// beides getrennt. Das Gelb kommt aus der Farbstellung, die Hinterlegung hat <see cref="Akzente.WuerdeSendenDeckkraft"/>. Der Tooltip der Grafik
    /// sagt, was Grün und Gelb heißen.
    /// </summary>
    [Test]
    public static void PegelGelb_Schwellengrafik_KurveUndHinterlegungGelb()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var schwelle = a.Schwelle();
            // Fest: ohne Korridor und ohne rote Linie, damit oben in der Pegelfläche nur die Hinterlegung liegt.
            schwelle.Automatik = false;
            for (var ms = 0L; ms <= 10_000; ms += 20)
            {
                a.Verlauf.Weg = ms < 5_000 ? Sendeweg.Offen : Sendeweg.Ruhe;
                a.Maschine.Melden(new PegelMeldung(ms, -20, -62, -40, 0.95f, ms < 5_000));
            }
            a.Verlauf.Weg = Sendeweg.Offen;
            schwelle.Takt();
            var probe = Color.FromRgb(0xFF, 0x00, 0xFF);
            var grafik = new SchwellenGrafik { Modell = schwelle, Width = 684, Height = 434 };
            var w = new Window { Content = grafik };
            w.Resources["Farbe.WuerdeSenden"] = probe;
            fenster.Add(w);
            var (bild, _) = Bildlauf.Zeichnen(w, 1, new Size(700, 450));
            var o = grafik.TransformToAncestor(Bildlauf.Wurzel(w)).Transform(new Point(0, 0));
            var p = grafik.Pegelflaeche;
            var y = SchwellenGrafik.DbZuY(-20, p);
            Color Kurve(double anteil)
            {
                var x = p.Left + (p.Width * anteil);
                return Enumerable.Range(-2, 5).Select(d => Bildlauf.Pixel(bild, o.X + x, o.Y + y + d)).OrderByDescending(c => c.R + c.G + c.B).First();
            }
            Color Grund(double anteil) => Bildlauf.Pixel(bild, o.X + p.Left + (p.Width * anteil), o.Y + p.Top + 20);
            var gesendet = Kurve(0.25);
            var gelb = Kurve(0.75);
            Console.WriteLine(T($"  Kurve gesendet {gesendet}, würde senden {gelb}; Hinterlegung {Grund(0.25)} und {Grund(0.75)}"));
            Pruefe.Wahr(Gruenlich(gesendet), T($"gesendet: Kurve grün ({gesendet})"));
            Pruefe.Wahr(Lilalich(gelb), T($"würde senden: Kurve in der Farbe der Farbstellung ({gelb})"));
            var sollGelb = Ueber(SchwellenGrafik.HintergrundFarbe, Akzente.WuerdeSendenFlaecheFarbe(probe));
            var sollGruen = Ueber(SchwellenGrafik.HintergrundFarbe, SchwellenGrafik.SendenFlaecheFarbe);
            Pruefe.Wahr(Nah(Grund(0.75), sollGelb), T($"würde senden: hinterlegt mit dem Gelb der Farbstellung ({Grund(0.75)}, soll {sollGelb})"));
            Pruefe.Wahr(Nah(Grund(0.25), sollGruen), T($"gesendet: grün hinterlegt wie bisher ({Grund(0.25)}, soll {sollGruen})"));
            Pruefe.Wahr(schwelle.GrafikHilfe.Contains(GelbSatz, StringComparison.Ordinal), "Tooltip der Grafik in Wörtern");
        });
    }

    /// <summary>Funkfeld: der Pegel auf dem gewählten Feld mit Ch. Ruhe gelb (Pinsel.WuerdeSenden), mit echtem Kanal grün wie bisher.</summary>
    [Test]
    public static void PegelGelb_Funkfeld_FeldGelb()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel() with { SendeFelder = [4], GlobalSenden = false });
            var f = a.Funkfeld();
            var ff = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(ff);
            f.Darstellung = FunkfeldDarstellung.Raster;
            ff.DarstellungAnwenden();
            var gelb = ((SolidColorBrush)ff.FindResource("Pinsel.WuerdeSenden")).Color;
            var gruen = ((SolidColorBrush)ff.FindResource("Pinsel.Senden")).Color;
            Color? Fuellung(KachelViewModel k)
            {
                _ = Bildlauf.Zeichnen(ff);
                return Bildlauf.Nachfahren<Anteilbalken>(Bildlauf.Wurzel(ff)).Single(b => ReferenceEquals(b.DataContext, k)).Fuellung is SolidColorBrush s ? s.Color : null;
            }
            Sprechen(a, sendet: false);
            a.Uhr += 100;
            f.Takt();
            Pruefe.Gleich<Color?>(gelb, Fuellung(f.Kacheln[4]), "Ch. Ruhe gewählt, über der Linie gesprochen: der Pegel auf dem Feld gelb");
            a.Dienst.LageSetzen(x => x with { SendeFelder = [3] });
            Sprechen(a, sendet: true);
            a.Uhr += 100;
            f.Takt();
            Pruefe.Gleich<Color?>(gruen, Fuellung(f.Kacheln[3]), "echter Kanal: grün");
            Pruefe.Falsch(Fuellung(f.Kacheln[4]) == gelb, "Ch. Ruhe nicht mehr gewählt: dort kein Gelb");
            f.Dispose();
        });
    }

    /// <summary>
    /// Die gelbe Hinterlegung ist höchstens so hell wie die grüne, also hält jede Linie darauf, was sie im gesendeten Abschnitt hält; die rote Linie der
    /// Automatik mindestens 3:1, auch mit Zone darüber (WCAG 1.4.11), in jeder Farbstellung. Das Gelb selbst steht auf der Grafik und auf der Kachel
    /// deutlich. Gegenprobe: Mit der Deckkraft des Grüns (25 %) wäre der gelbe Abschnitt heller als der grüne.
    /// </summary>
    [Test]
    public static void PegelGelb_Hinterlegung_HaeltKontrastWieGruen()
    {
        var grund = SchwellenGrafik.HintergrundFarbe;
        var gruen = Ueber(grund, SchwellenGrafik.SendenFlaecheFarbe);
        var gelb = Ueber(grund, SchwellenGrafik.WuerdeSendenFlaecheFarbe);
        var gelbZone = Ueber(gelb, Akzente.ZoneFarbe);
        Console.WriteLine(string.Create(De, $"MASS Hinterlegung: grün {Kontrast.Leuchtdichte(gruen):0.0000}, gelb {Kontrast.Leuchtdichte(gelb):0.0000}"));
        Pruefe.Wahr(Kontrast.Leuchtdichte(gelb) <= Kontrast.Leuchtdichte(gruen), "die gelbe Hinterlegung ist höchstens so hell wie die grüne");
        foreach (var satz in Farbvorschlaege.Alle)
        {
            var linie = Akzente.AutomatikFarbe(satz.RotSchrift);
            Console.WriteLine(string.Create(De, $"MASS Linie der Automatik {satz.Name}: gelb {V(linie, gelb):0.00}, gelb mit Zone {V(linie, gelbZone):0.00}"));
            Pruefe.Wahr(V(linie, gelb) >= 3 && V(linie, gelbZone) >= 3, string.Create(De, $"{satz.Name}: mindestens 3:1 im gelben Abschnitt ({V(linie, gelb):0.00}, {V(linie, gelbZone):0.00})"));
        }
        Pruefe.Wahr(V(Palette.WuerdeSenden, grund) >= 4.5 && V(Palette.WuerdeSenden, Palette.Flaeche) >= 4.5, "das Gelb auf der Grafik und auf der Kachel");
        Pruefe.Gleich(Palette.Tunnel, Palette.WuerdeSenden, "dasselbe Gelb wie der Tunnel und das gelbe Zeichen");
        var zuHell = Ueber(grund, Color.FromArgb(0x40, Palette.WuerdeSenden.R, Palette.WuerdeSenden.G, Palette.WuerdeSenden.B));
        Pruefe.Wahr(Kontrast.Leuchtdichte(zuHell) > Kontrast.Leuchtdichte(gruen), "Gegenprobe: mit 25 % wäre der gelbe Abschnitt heller als der grüne");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Kleinkram nach 0.8.0: Legende mit Gelb, Hinweis der Leiste lesbar
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sichtbar ohne Fensterhandle (dort ist IsVisible immer falsch und das nie gezeigte Fenster selbst nicht Visible): das Element und alle
    /// Vorfahren unter dem Fenster auf Visible, mit Fläche.
    /// </summary>
    private static bool Sichtbar(FrameworkElement e)
    {
        if (e.ActualWidth <= 0 || e.ActualHeight <= 0) return false;
        for (DependencyObject? d = e; d is not null and not Window; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement u && u.Visibility != Visibility.Visible) return false;
        return true;
    }

    /// <summary>Der Eintrag der Legende, der <paramref name="text"/> trägt: Zeichen (Rechteck) und Wort.</summary>
    private static (Rectangle Zeichen, TextBlock Wort)? Legendeneintrag(DependencyObject wurzel, string text)
    {
        var wort = Bildlauf.Nachfahren<TextBlock>(wurzel).FirstOrDefault(t => t.Text == text);
        if (wort?.Parent is not StackPanel eintrag) return null;
        var zeichen = eintrag.Children.OfType<Rectangle>().FirstOrDefault();
        return zeichen is null ? null : (zeichen, wort);
    }

    /// <summary>
    /// Die Legende unter der Schwellengrafik nennt neben "gesendet" (grün) auch Gelb: "würde senden, geht nirgends hin", immer sichtbar wie der
    /// grüne Eintrag, direkt dahinter. Zeichen und Hinterlegung in genau den Farben, mit denen die Grafik die gelben Abschnitte zeichnet (aus der
    /// Farbstellung, <see cref="Akzente.WuerdeSenden"/> und <see cref="Akzente.WuerdeSendenFlaeche"/>), in jeder Stellung.
    /// </summary>
    [Test]
    public static void PegelGelb_Legende_NenntGelbNebenGruen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var stellung in Enum.GetValues<Farbstellung>())
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
                var schwelle = a.Schwelle();
                var bereich = new Schwellenbereich { DataContext = schwelle };
                var w = new Window { Content = bereich };
                fenster.Add(w);
                _ = Bildlauf.Zeichnen(w, 1, new Size(1400, 900));
                var grafik = Bildlauf.Nachfahren<SchwellenGrafik>(bereich).Single();
                var kurve = Akzente.Fuer(grafik);
                var gruen = Legendeneintrag(bereich, "gesendet");
                var gelb = Legendeneintrag(bereich, "würde senden, geht nirgends hin");
                Pruefe.Wahr(gruen is not null, stellung + ": der grüne Eintrag steht wie bisher");
                Pruefe.Wahr(gelb is not null, stellung + ": ein Eintrag für Gelb");
                var (zeichen, wort) = gelb!.Value;
                Pruefe.Wahr(Sichtbar(wort) && Sichtbar(zeichen), stellung + ": immer sichtbar");
                var reihe = (Panel)((StackPanel)wort.Parent).Parent;
                Pruefe.Gleich(reihe.Children.IndexOf((UIElement)gruen!.Value.Wort.Parent) + 1, reihe.Children.IndexOf((UIElement)wort.Parent), stellung + ": direkt hinter Grün");
                Pruefe.Gleich(((SolidColorBrush)kurve.WuerdeSenden).Color, (zeichen.Stroke as SolidColorBrush)?.Color ?? Colors.Transparent,
                    stellung + ": Rand im Gelb der Kurve");
                Pruefe.Gleich(((SolidColorBrush)kurve.WuerdeSendenFlaeche).Color, (zeichen.Fill as SolidColorBrush)?.Color ?? Colors.Transparent,
                    stellung + ": Fläche wie die gelbe Hinterlegung");
                Pruefe.Gleich(gruen.Value.Zeichen.Width, zeichen.Width, stellung + ": Zeichen so groß wie das grüne");
                Pruefe.Gleich(gruen.Value.Wort.FontSize, wort.FontSize, stellung + ": Schrift wie das grüne");
            }
        });
    }

    /// <summary>Hauptfenster auf der Startseite in der Mindestgröße, Feld 5 mit Ch. Ruhe zum Senden gewählt.</summary>
    private static (Hauptfenster Haupt, HauptViewModel Modell, FunkfeldViewModel Funkfeld) RuheStart(Aufbau a, List<Window> fenster)
    {
        a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
        a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
        a.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        h.AktiverBereich = h.Bereiche[0];
        var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
        fenster.Add(haupt);
        a.Maschine.Korridor = new KorridorStand(-23, float.NaN, float.NaN);
        return (haupt, h, funkfeld);
    }

    /// <summary>
    /// Der Satz "Ruhe: es wird nichts gesendet" stand bis 0.9.3 in der Leiste der Sprachaktivierung neben dem Balken, von 0.10.0 bis 0.11.1 in der
    /// Statuszeile "Mikrofon" (Q87, m-8); seit 0.11.2 gibt es ihn nicht mehr. Die übrigen Sätze (Sprechtaste fehlt, unter der Mindest-Funklautstärke)
    /// stehen neben dem Balken in der Schrift der übrigen Statustexte (mindestens <c>Groesse.Klein</c>), vollständig, auch in der Mindestgröße von
    /// Klein; die Zeile läuft dabei nicht über (die Marke bleibt ganz). Im Balken steht kein Satz. Ohne Satz ist der Platz leer.
    /// </summary>
    [Test]
    public static void PegelGelb_Statuszeile_HinweisLesbarNebenDemBalken()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel() with { SendeFelder = [4], GlobalSenden = false });
            var (haupt, h, funkfeld) = RuheStart(a, fenster);
            var mindest = Fenstergroessen.KleinMindest;
            var klein = (double)haupt.FindResource("Groesse.Klein");

            Sprechen(a, sendet: false);
            h.Takt();
            // Seit 0.11.2 gibt es zu Ruhe keinen Satz mehr (Sarevoc 13.10.2026: "raus heißt raus"; bis 0.11.1 "Ruhe: es wird nichts gesendet"): Der Platz
            // neben dem Balken bleibt leer, der Balken gelb. Die übrigen Sätze (Sprechtaste fehlt, unter der Mindest-Funklautstärke) stehen dort weiter.
            Pruefe.Gleich<string?>(null, h.Statuszeile.Hinweis, "Ruhe: kein Satz");
            var (bild, _) = Bildlauf.Zeichnen(haupt, 1, mindest);
            var wurzel = Bildlauf.Wurzel(haupt);
            T Benannt<T>(string name) where T : FrameworkElement => Bildlauf.Nachfahren<T>(wurzel).First(e => e.Name == name);
            Rect Lage(FrameworkElement e) => e.TransformToAncestor(wurzel).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
            var zeile = Benannt<FrameworkElement>("Statuszeile");
            var pegel = Benannt<Pegelbalken>("StatusPegel");
            var marke = Benannt<FrameworkElement>("StatusMarke");
            var gefunden = Bildlauf.Nachfahren<TextBlock>(zeile).FirstOrDefault(t => t.Name == "StatusHinweis");
            Pruefe.Wahr(gefunden is not null, "neben dem Balken ein eigener Text");
            var hinweis = gefunden!;
            Pruefe.Falsch(Sichtbar(hinweis) && hinweis.Text.Length > 0, T($"Ruhe: der Platz neben dem Balken bleibt leer ({hinweis.Text})"));
            Pruefe.Wahr(hinweis.FontSize >= klein, T($"Schrift mindestens wie die Statustexte ({hinweis.FontSize} gegen {klein})"));
            Pruefe.Falsch(pegel.HinweisImBalken, "im Balken steht der Satz nicht");
            Pruefe.Falsch(pegel.Hilfe(Pegelbalken.Teil.Balken)?.StartsWith("Ruhe:", StringComparison.Ordinal) == true, "der Tooltip des Balkens nennt keinen Satz zu Ruhe");

            // Rechts vom Pegel (-20 dBFS) gäbe es im Balken nur durch den Satz helle Bildpunkte.
            var o = Lage(pegel);
            var hell = 0;
            for (var x = o.Left + (o.Width * 0.85); x < o.Right - 2; x++)
                for (var y = o.Top + 1; y < o.Bottom - 1; y++)
                    if (Bildlauf.Pixel(bild, x, y) is { R: > 150 } c && c.G > 150) hell++;
            Pruefe.Gleich(0, hell, "kein Satz im Balken");

            var dip = VisualTreeHelper.GetDpi(hinweis).PixelsPerDip;
            // Seit 0.11.2 nur noch zwei Sätze (bis 0.11.1 dazu Ruhe, Stummschaltung und Trennung).
            foreach (var satz in new[] { PegelVerlauf.HinweisText(Sendeweg.OhneSprechtaste)!, PegelVerlauf.UnterMindestText })
            {
                // SetCurrentValue lässt die Bindung stehen: Der nächste Satz des Modells ersetzt die Probe wieder. Seit 0.11.2 hat Ruhe keinen Satz, der
                // Text stünde ohne Satz verborgen; darum auch die Sichtbarkeit (wie in OberflaecheUmbauRahmenTests).
                hinweis.SetCurrentValue(TextBlock.TextProperty, satz);
                hinweis.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
                _ = Bildlauf.Zeichnen(haupt, 1, mindest);
                var voll = new FormattedText(satz, De, FlowDirection.LeftToRight, new Typeface(hinweis.FontFamily, hinweis.FontStyle, hinweis.FontWeight, hinweis.FontStretch),
                    hinweis.FontSize, Brushes.White, dip).WidthIncludingTrailingWhitespace;
                var l = Lage(zeile);
                var hl = Lage(hinweis);
                Console.WriteLine(T($"  \"{satz}\" in {mindest.Width:0} x {mindest.Height:0}: Zeile {l.Left:0}-{l.Right:0}, Satz {hl.Left:0}-{hl.Right:0} ({voll:0.0} breit), Balken {o.Left:0}-{o.Right:0}"));
                Pruefe.Wahr(hinweis.ActualWidth >= voll - 0.5, T($"{satz}: der Satz vollständig ({hinweis.ActualWidth:0.0} von {voll:0.0})"));
                Pruefe.Wahr(hl.Left >= l.Left && hl.Right <= Lage(pegel).Left + 0.5, T($"{satz}: links neben dem Balken, in der Zeile"));
                Pruefe.Wahr(Lage(marke).Right <= l.Right + 0.5, T($"{satz}: die Zeile läuft nicht über (Marke bis {Lage(marke).Right:0})"));
                Pruefe.Gleich(160.0, pegel.ActualWidth, satz + ": der Balken behält seine Breite");
            }

            // Echter Kanal, eine Weile still: kein Satz mehr.
            a.Dienst.LageSetzen(x => x with { SendeFelder = [3] });
            a.Uhr += PegelVerlauf.HinweisHaltMs * 3;
            a.Maschine.Melden(new PegelMeldung(a.Uhr, -60, -62, -40, 0.05f, false));
            h.Takt();
            Pruefe.Gleich<string?>(null, h.Statuszeile.Hinweis, "kein Satz im Modell");
            // Das Modell hatte seit dem Anfang keinen Satz: Die Bindungen melden keine Änderung, die Proben stünden noch da; sie holen den Stand neu.
            hinweis.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
            hinweis.GetBindingExpression(UIElement.VisibilityProperty)?.UpdateTarget();
            _ = Bildlauf.Zeichnen(haupt, 1, mindest);
            Pruefe.Falsch(Sichtbar(hinweis) && hinweis.Text.Length > 0, "ohne Satz bleibt der Platz leer");
            funkfeld.Dispose();
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Sichtproben
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sichtproben zum Kleinkram (mit <c>FUNK_BILDER</c>), in Anthrazit: die Seite Sprachaktivierung mit Grün und Gelb und der Legende, die
    /// Startseite in der Mindestgröße bei Ruhe mit dem Satz neben dem Balken der Statuszeile (bis 0.9.3 der Leiste).
    /// </summary>
    [Test]
    public static void PegelGelb_Kleinkram_Sichtproben()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel() with { SendeFelder = [3], GlobalSenden = false });
                var (haupt, h, funkfeld) = RuheStart(a, fenster);
                h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
                var start = a.Uhr;
                for (var ms = 0L; ms <= 10_000; ms += 20)
                {
                    if (ms == 5_000) a.Dienst.LageSetzen(x => x with { SendeFelder = [4] });
                    var satz = ms is >= 1_000 and <= 3_600 || ms >= 6_000;
                    var welle = (float)((Math.Sin(ms / 90.0) * 4) + (Math.Sin(ms / 37.0) * 2));
                    a.Maschine.Melden(satz
                        ? new PegelMeldung(start + ms, -22 + welle, -62, -44, 0.95f, ms < 5_000)
                        : new PegelMeldung(start + ms, -60 + (welle / 2), -62, -44, 0.05f, false));
                }
                a.Uhr = start + 10_000;
                h.Takt();
                // Seit 0.11.2 ohne Satz zu Ruhe (Sarevoc 13.10.2026: "raus heißt raus"); bis 0.11.1 "mit Satz".
                Pruefe.Wahr(h.Audio.WuerdeSenden && h.Schwelle.NichtsGesendet is null, kurz + ": am Ende gelb, ohne Satz");
                Ablegen(haupt, kurz + "-kleinkram-seite-legende-gruen-gelb", new Size(1600, 1000));
                h.AktiverBereich = h.Bereiche[0];
                h.Takt();
                Ablegen(haupt, kurz + "-kleinkram-start-ruhe-mindestgroesse", Fenstergroessen.KleinMindest);
                Ablegen(haupt, kurz + "-kleinkram-start-ruhe-mindestgroesse-175", Fenstergroessen.KleinMindest, 1.75);
                funkfeld.Dispose();
            }
        });
    }

    /// <summary>
    /// Sichtproben (mit <c>FUNK_BILDER</c>), in Anthrazit: die Seite Sprachaktivierung mit einem gesendeten Satz (grün) und einem auf Ch. Ruhe
    /// (gelb), die Startseite mit gelbem Balken und dem Satz in Wörtern, das Funkfeld mit gelbem Pegel auf dem Feld mit Ch. Ruhe.
    /// </summary>
    [Test]
    public static void PegelGelb_Sichtproben()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel() with { SendeFelder = [3], GlobalSenden = false });
                a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
                a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
                a.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
                a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
                var h = a.Haupt(out var funkfeld, out _, out _, out _);
                var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
                fenster.Add(haupt);
                a.Maschine.Korridor = new KorridorStand(-23, float.NaN, float.NaN);
                h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
                // Erst ein Satz, der hinausgeht, dann auf Ch. Ruhe derselbe Satz (die Maschine sendet dort nicht, A10).
                var start = a.Uhr;
                for (var ms = 0L; ms <= 10_000; ms += 20)
                {
                    if (ms == 5_000) a.Dienst.LageSetzen(x => x with { SendeFelder = [4] });
                    var satz = ms is >= 1_000 and <= 3_600 || ms >= 6_000;
                    var welle = (float)((Math.Sin(ms / 90.0) * 4) + (Math.Sin(ms / 37.0) * 2));
                    a.Maschine.Melden(satz
                        ? new PegelMeldung(start + ms, -22 + welle, -62, -44, 0.95f, ms < 5_000)
                        : new PegelMeldung(start + ms, -60 + (welle / 2), -62, -44, 0.05f, false));
                }
                a.Uhr = start + 10_000;
                h.Takt();
                Pruefe.Wahr(h.Audio.WuerdeSenden && !h.Audio.Sendet, kurz + ": am Ende gelb");
                Ablegen(haupt, kurz + "-q43-seite-gruen-gelb", new Size(1600, 1000));
                h.AktiverBereich = h.Bereiche[0];
                h.Takt();
                Ablegen(haupt, kurz + "-q43-start-gelb", Fenstergroessen.KleinMindest);

                var ff = new FunkfeldFenster(funkfeld, a.Ablage, new FesteBildschirme([FullHd]));
                fenster.Add(ff);
                funkfeld.Darstellung = FunkfeldDarstellung.Raster;
                ff.DarstellungAnwenden();
                a.Uhr += 100;
                funkfeld.Takt();
                Pruefe.Wahr(funkfeld.Kacheln[4].WuerdeSenden, kurz + ": Feld mit Ch. Ruhe gelb");
                Ablegen(ff, kurz + "-q43-funkfeld-ruhe-gelb", null, 1.75);
                funkfeld.Dispose();
            }
        });
    }

    /// <summary>Wie in den Sichtproben 0.6.2: zeichnen, Pegelbalken und Grafik ausdrücklich neu (ohne Fensterhandle kein Loaded), dann ablegen.</summary>
    private static void Ablegen(Window w, string name, Size? groesse = null, double skala = 1)
    {
        _ = Bildlauf.Zeichnen(w, skala, groesse);
        foreach (var b in Bildlauf.Nachfahren<Pegelbalken>(Bildlauf.Wurzel(w))) b.InvalidateVisual();
        foreach (var g in Bildlauf.Nachfahren<SchwellenGrafik>(Bildlauf.Wurzel(w))) g.NeuZeichnen();
        Bildablage.Ablegen(Bildlauf.Zeichnen(w, skala, groesse).Bild, name);
    }
}
