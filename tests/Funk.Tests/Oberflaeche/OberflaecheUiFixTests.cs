// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Oberflächen-Korrekturen 0.6.2 (KLANGSTUFE-C-ENTWURF.md im Planungsordner, "Mit in die nächste Fassung (nicht Klang)"): Grün heißt überall, dass
/// tatsächlich etwas hinausgeht (Fehler von Sarevoc und Q41), Korridor-Linien immer sichtbar (Q39), Einmessen als Popup (Q40), große feste
/// Schwellengrafik, rotes Mikro bei stumm (Q42) und "Senden: ..." ungekürzt. Gezeichnet ohne Fensterhandle (<see cref="Bildlauf"/>).
/// </summary>
internal static class OberflaecheUiFixTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    private static readonly string[] EinmessKnoepfe = ["EinmessNochEinmal", "EinmessSchliessen"];

    private static IEnumerable<FrameworkElement> Benannt(DependencyObject wurzel, string name) =>
        Bildlauf.Nachfahren<FrameworkElement>(wurzel).Where(e => e.Name == name);

    /// <summary>Eine Pegelmeldung über der Linie mit Sprache, wie sie die Maschine bei gedrückter Sprechtaste oder offener Sprachaktivierung meldet.</summary>
    private static void Sprechen(Aufbau a, bool sendet, float db = -20f)
    {
        a.Uhr += 200;
        a.Maschine.Melden(new PegelMeldung(a.Uhr, db, -62, -40, 0.95f, sendet));
    }

    private static bool Gruenlich(Color c) => c.G > c.R + 60 && c.G > c.B + 30;

    private static bool Weisslich(Color c) => c.R > 170 && c.G > 170 && c.B > 170 && Math.Abs(c.R - c.G) < 40;

    // ------------------------------------------------------------------------------------------------------------------------------
    // 1. Fehler: Grün nur, wenn etwas hinausgeht
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Ursache des Grün-Fehlers: Die Oberfläche nahm "sendet" ungeprüft aus der Maschine. Auf Ruhe schaltet das Funkfeld die Sprachaktivierung ab
    /// (A10), die Maschine meldet dann nie "sendet" (kein Grün beim Sprechen), mit der Sprechtaste aber schon, obwohl nichts hinausgeht. Grün heißt
    /// jetzt überall: mindestens ein gewählter Kanal nicht Ruhe, nicht von der Einsatzleitung stummgeschaltet, und die Maschine sendet.
    /// </summary>
    [Test]
    public static void UiFix_Gruen_NurWennEtwasHinausgeht()
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
        Pruefe.Gleich(KachelZustand.Ruhe, f.Kacheln[4].Zustand, "Feld 5 steht auf Ruhe");
        Pruefe.Falsch(f.Kacheln.Any(k => k.IstSenden), "kein Kanal sendet");
        Pruefe.Falsch(a.Maschine.Sprachaktivierung, "auf Ruhe keine Sprachaktivierung (A10)");

        // Sprechtaste auf Ruhe: Die Maschine meldet "sendet", hinaus geht nichts.
        Sprechen(a, sendet: true);
        Takt();
        Pruefe.Falsch(audio.Sendet, "Ruhe mit Sprechtaste: Pegelbalken nicht grün");
        Pruefe.Falsch(schwelle.Sendet, "Ruhe mit Sprechtaste: Leiste und Grafik nicht grün");
        Pruefe.Falsch(f.Kacheln.Any(k => k.EigeneSendung), "Ruhe: keine Kachel zeigt 'Du sprichst'");

        // Ein echter Kanal: grün.
        a.Dienst.LageSetzen(x => x with { SendeFelder = [3] });
        Pruefe.Wahr(f.Kacheln[3].IstSenden, "Feld 4 sendet");
        Sprechen(a, sendet: true);
        Takt();
        Pruefe.Wahr(audio.Sendet && schwelle.Sendet, "echter Kanal: grün");
        Pruefe.Wahr(f.Kacheln[3].EigeneSendung, "echter Kanal: die Kachel zeigt 'Du sprichst'");
        Sprechen(a, sendet: false);
        Takt();
        Pruefe.Falsch(audio.Sendet || schwelle.Sendet, "die Maschine sendet nicht: nicht grün");

        // Von der Einsatzleitung in allen Kanälen stummgeschaltet: Der Server verwirft alles.
        a.Dienst.LageSetzen(x => x with { Stumm = [new StummschaltungInfo(0, a.Jetzt.AddMinutes(30), 4)] });
        Sprechen(a, sendet: true);
        Takt();
        Pruefe.Falsch(audio.Sendet || schwelle.Sendet, "stumm durch die Einsatzleitung: nicht grün");
        f.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // 2. Q41: überall dasselbe Grün beim Senden
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Der Pegelbalken nimmt das Grün aus der Farbstellung (Ressource Farbe.Senden), kein festes Grün.</summary>
    [Test]
    public static void UiFix_Pegelbalken_GruenDerFarbstellung()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var probe = Color.FromRgb(0x20, 0xB0, 0x90);
            var balken = new Pegelbalken { Width = 200, Height = 10, PegelDb = -10, Sendet = true };
            var w = new Window { Content = balken, Background = Brushes.Black };
            w.Resources["Farbe.Senden"] = probe;
            fenster.Add(w);
            var (bild, _) = Bildlauf.Zeichnen(w, 1, new Size(240, 40));
            var o = balken.TransformToAncestor(Bildlauf.Wurzel(w)).Transform(new Point(0, 0));
            var p = Bildlauf.Pixel(bild, o.X + 30, o.Y + 5);
            Pruefe.Gleich(probe, Color.FromRgb(p.R, p.G, p.B), "Balken beim Senden im Grün der Farbstellung");
        });
    }

    /// <summary>Q41: Die Pegelkurve der Schwellengrafik ist grün, solange gesendet wird, sonst weiß; die grünen Sendeabschnitte bleiben hinterlegt.</summary>
    [Test]
    public static void UiFix_Schwellengrafik_KurveGruenBeimSenden()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var schwelle = a.Schwelle();
            for (var ms = 0L; ms <= 10_000; ms += 20) a.Maschine.Melden(new PegelMeldung(ms, -20, -62, -40, 0.95f, ms >= 5_000));
            schwelle.Takt();
            var grafik = new SchwellenGrafik { Modell = schwelle, Width = 684, Height = 434 };
            var w = new Window { Content = grafik };
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
            var ruhe = Kurve(0.25);
            var senden = Kurve(0.75);
            Console.WriteLine(T($"  Kurve ohne Senden {ruhe}, beim Senden {senden}"));
            Pruefe.Wahr(Weisslich(ruhe), T($"ohne Senden weiß ({ruhe})"));
            Pruefe.Wahr(Gruenlich(senden), T($"beim Senden grün ({senden})"));
        });
    }

    /// <summary>
    /// Funkfeld: "Senden: ..." grün, solange gesendet wird (Q41); das Mikro selbst rot, wenn es stumm ist (Q42), frei wie bisher; in der Liste mit
    /// Stummschaltung durch die Einsatzleitung bleibt "Senden: ..." ungekürzt.
    /// </summary>
    [Test]
    public static void UiFix_Funkfeld_SendenGruen_MikroRot_SendenUngekuerzt()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Jetzt = new DateTimeOffset(2026, 10, 4, 20, 52, 0, TimeSpan.FromHours(2)) };
            var f = a.Funkfeld();
            var ff = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(ff);
            var gruen = ((SolidColorBrush)ff.FindResource("Pinsel.Senden")).Color;
            var rot = ((SolidColorBrush)ff.FindResource("Pinsel.RotSchrift")).Color;
            foreach (var d in new[] { FunkfeldDarstellung.Raster, FunkfeldDarstellung.Liste })
            {
                f.Darstellung = d;
                ff.DarstellungAnwenden();
                var kopf = (TextBlock)ff.FindName("KopfText");
                Sprechen(a, sendet: false);
                a.Uhr += 100;
                f.Takt();
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Falsch(((SolidColorBrush)kopf.Foreground).Color == gruen, d + ": ohne Senden nicht grün");
                Sprechen(a, sendet: true);
                a.Uhr += 100;
                f.Takt();
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Gleich(gruen, ((SolidColorBrush)kopf.Foreground).Color, d + ": 'Senden: Crew' grün beim Senden");
                Sprechen(a, sendet: false);
                a.Uhr += 100;
                f.Takt();

                var mikro = (Button)Benannt(Bildlauf.Wurzel(ff), "MikroKnopf").Single();
                var symbol = (System.Windows.Shapes.Path)Benannt(mikro, "MikroSymbol").Single();
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Falsch(symbol.Stroke is SolidColorBrush { Color: var frei } && frei == rot, d + ": frei nicht rot");
                mikro.Command.Execute(null);
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Wahr(symbol.Stroke is SolidColorBrush { Color: var stumm } && stumm == rot, T($"{d}: stumm: das Zeichen selbst rot ({symbol.Stroke})"));
                mikro.Command.Execute(null);

                // Einsatzleitung schaltet einen anderen Kanal stumm: Schloss am Knopf, "Senden: Crew" bleibt ganz.
                a.Dienst.LageSetzen(l => l with { Stumm = [new StummschaltungInfo(2, a.Jetzt.AddMinutes(38), 4)] });
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Wahr(symbol.Stroke is SolidColorBrush { Color: var leitung } && leitung == rot, T($"{d}: stumm durch die Einsatzleitung: rot ({symbol.Stroke})"));
                var ft = new FormattedText(kopf.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(kopf.FontFamily, kopf.FontStyle, kopf.FontWeight, kopf.FontStretch), kopf.FontSize, Brushes.White, 1);
                var platz = kopf.ActualWidth - kopf.Padding.Left - kopf.Padding.Right;
                Console.WriteLine(T($"  {d}: '{kopf.Text}' braucht {ft.WidthIncludingTrailingWhitespace:0.0}, hat {platz:0.0}"));
                Pruefe.Wahr(platz + 0.5 >= ft.WidthIncludingTrailingWhitespace, T($"{d}: '{kopf.Text}' ungekürzt ({platz:0.0} für {ft.WidthIncludingTrailingWhitespace:0.0})"));
                a.Dienst.LageSetzen(l => l with { Stumm = [] });
            }
            f.Dispose();
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // 3. Q39: Korridor-Linien immer sichtbar, ohne Stimme grau
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Ohne bekannte Stimme stehen die Linien des Korridors trotzdem da (grau), am Pegelbalken über und unter dem Balken und in der Grafik.</summary>
    [Test]
    public static void UiFix_Korridor_OhneStimme_LinienGrau()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var schwelle = a.Schwelle();
            Pruefe.Wahr(schwelle.Korridor.Automatik && !schwelle.Korridor.StimmeBekannt, "Automatik, Stimme unbekannt");
            var balken = new Pegelbalken { Width = 300, Height = 28, PegelDb = -60, Korridor = schwelle.Korridor };
            var grafik = new SchwellenGrafik { Modell = schwelle, Width = 684, Height = 434 };
            var stapel = new StackPanel();
            stapel.Children.Add(balken);
            stapel.Children.Add(grafik);
            var w = new Window { Content = stapel, Background = Brushes.Black };
            fenster.Add(w);
            var (bild, _) = Bildlauf.Zeichnen(w, 1, new Size(720, 500));
            var wurzel = Bildlauf.Wurzel(w);
            var ob = balken.TransformToAncestor(wurzel).Transform(new Point(0, 0));
            var oben = 0;
            for (var x = 0; x < 300; x++)
                for (var y = 0; y < (int)Pegelbalken.Ueberstand - 1; y++)
                    if (Bildlauf.Pixel(bild, ob.X + x, ob.Y + y) is { R: > 90 } c && Math.Abs(c.R - c.G) < 20) oben++;
            Pruefe.Wahr(oben >= 8, T($"Pegelbalken: graue Linien über dem Balken ({oben} Bildpunkte)"));

            var og = grafik.TransformToAncestor(wurzel).Transform(new Point(0, 0));
            var p = grafik.Pegelflaeche;
            var zeilen = 0;
            for (var y = (int)p.Top + 3; y < (int)p.Bottom - 3; y++)
            {
                var grau = 0;
                var n = 0;
                for (var x = p.Left + 40; x < p.Right - 10; x += 4, n++)
                    if (Bildlauf.Pixel(bild, og.X + x, og.Y + y) is var c && c.R > 110 && c.R < 170 && Math.Abs(c.R - c.G) < 12 && Math.Abs(c.R - c.B) < 12) grau++;
                if (grau > n * 0.6) zeilen++;
            }
            Pruefe.Wahr(zeilen >= 2, T($"Grafik: zwei graue waagerechte Linien ({zeilen} Zeilen)"));
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // 5. Große, feste Schwellengrafik auf der Seite
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Die Grafik auf der Seite Sprachaktivierung: Pegelfläche mindestens 600 x 260, fest (wächst und schrumpft nicht mit dem Fenster); in der
    /// kleinsten Breite der Seite rollt sie nur senkrecht, nichts ist rechts abgeschnitten.
    /// </summary>
    [Test]
    public static void UiFix_Seite_SchwellengrafikGrossUndFest()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            foreach (var groesse in new[] { new Size(haupt.MinWidth, haupt.MinHeight), new Size(1600, 1000) })
            {
                _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                var g = haupt.Schwellenbereich.Verlaufsgrafik;
                var p = g.Pegelflaeche;
                var aussen = (ScrollViewer)haupt.FindName("SchwellenSeitenPlatz");
                var rechts = g.TransformToAncestor(aussen).TransformBounds(new Rect(g.RenderSize)).Right;
                Console.WriteLine(T($"  Seite {groesse.Width:0} x {groesse.Height:0}: Grafik {g.ActualWidth:0} x {g.ActualHeight:0}, Pegelfläche {p.Width:0} x {p.Height:0}, rechts {rechts:0} von {aussen.ViewportWidth:0}"));
                Pruefe.Wahr(p.Width >= 599.5 && p.Height >= 259.5, T($"{groesse.Width:0} x {groesse.Height:0}: Pegelfläche mindestens 600 x 260 ({p.Width:0} x {p.Height:0})"));
                Pruefe.Wahr(rechts <= aussen.ViewportWidth + 0.5, T($"{groesse.Width:0} x {groesse.Height:0}: rechts nichts abgeschnitten ({rechts:0} von {aussen.ViewportWidth:0})"));
                var s = g.Sprachflaeche;
                Pruefe.Wahr(s.Width >= 599.5 && s.Height >= 109.5 && s.Top >= p.Bottom + 10, T($"{groesse.Width:0} x {groesse.Height:0}: Sprachfläche mindestens 600 x 110 unter der Pegelfläche ({s})"));
                Pruefe.Gleich(Schwellenbereich.SeitenGrafik, new Size(Math.Round(g.ActualWidth), Math.Round(g.ActualHeight)), "feste Größe");
                var gross = groesse.Width >= 1500;
                Pruefe.Gleich(!gross, haupt.Schwellenbereich.EinstellungenDarunter, T($"{groesse.Width:0}: Einstellungen {(gross ? "rechts" : "darunter")}"));
                Pruefe.Gleich(Visibility.Collapsed, aussen.ComputedHorizontalScrollBarVisibility, "nie waagerecht rollen");
                if (!gross) Pruefe.Gleich(Visibility.Visible, aussen.ComputedVerticalScrollBarVisibility, "Mindestgröße: die Seite rollt senkrecht");
                else Pruefe.Gleich(Visibility.Collapsed, aussen.ComputedVerticalScrollBarVisibility, "großes Fenster: kein Rollbalken");
            }
            Pruefe.Gleich(new Size(960, 480), Fenstergroessen.Mindest(Groessenklasse.Sprachaktivierung), "eigene Mindestgröße der Seite");
            Pruefe.Gleich(Groessenklasse.Sprachaktivierung, haupt.Klasse, "die Seite hat ihre eigene Größenklasse");
            funkfeld.Dispose();
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Neue Schnittstellen (rot per Mutationsprobe belegt)
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Auf Ruhe, ohne Verbindung oder stumm durch die Einsatzleitung und über der Linie gesprochen: Es geht nichts hinaus (kein Grün, die Meldezeile
    /// sendet nicht), aber seit 0.11.2 steht dazu kein Satz mehr, weder am Pegelbalken noch in Leiste, Grafik oder Meldezeile (Sarevoc 13.10.2026: "die
    /// Meldung 'es wird nichts gesendet' will ich nicht ... raus heißt raus"; bis 0.11.1 "Ruhe: es wird nichts gesendet"). Mit echtem Kanal grün.
    /// </summary>
    [Test]
    public static void UiFix_NichtsGesendet_OhneSatz()
    {
        var l = FunkDienstAttrappe.KanalBeispiel();
        using var a = new Aufbau(l with { SendeFelder = [4], GlobalSenden = false });
        var f = a.Funkfeld();
        var zeile = new Meldezeile(() => a.Uhr);
        f.Meldezeile = zeile;
        var audio = a.Audio();
        var schwelle = a.Schwelle();
        void Takt()
        {
            audio.Takt();
            schwelle.Takt();
            a.Uhr += 100;
            f.Takt();
        }
        Sprechen(a, sendet: false, db: -70);
        Takt();
        Pruefe.Gleich<string?>(null, audio.NichtsGesendet, "still: kein Satz");
        Sprechen(a, sendet: false);
        Takt();
        Pruefe.Gleich(Sendeweg.Ruhe, a.Verlauf.Weg, "das Sendeziel steht auf Ruhe");
        Pruefe.Wahr(audio.WuerdeSenden && !audio.Sendet, "Ruhe, über der Linie gesprochen: gelb statt grün, es geht nichts hinaus");
        Pruefe.Gleich<string?>(null, audio.NichtsGesendet, "Ruhe: kein Satz am Pegelbalken");
        Pruefe.Gleich<string?>(null, schwelle.NichtsGesendet, "Ruhe: kein Satz in Leiste und Grafik");
        Pruefe.Falsch(zeile.Sichtbar, "Ruhe: nichts in der Meldezeile");
        Pruefe.Falsch(zeile.Sendet, "Meldezeile: nichts hinaus");

        a.Dienst.LageSetzen(x => x with { SendeFelder = [3] });
        Sprechen(a, sendet: true);
        Takt();
        Pruefe.Gleich<string?>(null, audio.NichtsGesendet, "echter Kanal: kein Satz");
        Pruefe.Wahr(zeile.Sendet && f.SendetGerade, "echter Kanal: Meldezeile und 'Senden: ...' grün");

        a.Dienst.LageSetzen(x => x with { Stumm = [new StummschaltungInfo(0, a.Jetzt.AddMinutes(30), 4)] });
        Sprechen(a, sendet: true);
        Takt();
        Pruefe.Gleich(Sendeweg.Leitung, a.Verlauf.Weg, "stumm durch die Einsatzleitung");
        Pruefe.Wahr(!audio.Sendet && audio.NichtsGesendet is null && !zeile.Sichtbar, "stumm durch die Einsatzleitung: nichts hinaus, kein Satz");

        a.Dienst.LageSetzen(x => x with { Stumm = [], Status = Verbindungsstatus.Getrennt });
        Sprechen(a, sendet: false);
        Takt();
        Pruefe.Gleich(Sendeweg.Getrennt, a.Verlauf.Weg, "nicht verbunden");
        Pruefe.Wahr(!audio.Sendet && audio.NichtsGesendet is null && !zeile.Sichtbar, "nicht verbunden: nichts hinaus, kein Satz");
        f.Dispose();
        Pruefe.Gleich(Sendeweg.Offen, a.Verlauf.Weg, "ohne Funkfeld gilt wieder, was die Maschine meldet");
    }

    /// <summary>Der Satz steht im Pegelbalken (auch in der schmalen Leiste) und sein Tooltip erklärt ihn.</summary>
    [Test]
    public static void UiFix_NichtsGesendet_ImBalken()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var balken = new Pegelbalken { Width = 160, Height = 10, PegelDb = -20, Hinweis = "Ruhe: es wird nichts gesendet" };
            var leer = new Pegelbalken { Width = 160, Height = 10, PegelDb = -20 };
            var stapel = new StackPanel();
            stapel.Children.Add(balken);
            stapel.Children.Add(new Border { Height = 10 });
            stapel.Children.Add(leer);
            var w = new Window { Content = stapel, Background = Brushes.Black };
            fenster.Add(w);
            var (bild, _) = Bildlauf.Zeichnen(w, 1, new Size(200, 60));
            var wurzel = Bildlauf.Wurzel(w);
            int Hell(Pegelbalken b)
            {
                var o = b.TransformToAncestor(wurzel).Transform(new Point(0, 0));
                var n = 0;
                for (var x = 125; x < 158; x++)
                    for (var y = 1; y < 9; y++)
                        if (Bildlauf.Pixel(bild, o.X + x, o.Y + y) is { R: > 150 } c && c.G > 150) n++;
                return n;
            }
            // Rechts vom Pegel (der Balken endet bei -20 dBFS etwa bei 124) gibt es nur durch den Satz helle Bildpunkte.
            Pruefe.Wahr(Hell(balken) > 10 && Hell(leer) == 0, T($"der Satz steht im Balken ({Hell(balken)} helle Bildpunkte, ohne {Hell(leer)})"));
            Pruefe.Wahr(balken.Hilfe(Pegelbalken.Teil.Balken)?.StartsWith("Ruhe: es wird nichts gesendet. ", StringComparison.Ordinal) == true, "Tooltip");
        });
    }

    /// <summary>Q41: Die Zeile "Melden: an ..." in der Meldezeile ist grün, solange tatsächlich etwas hinausgeht.</summary>
    [Test]
    public static void UiFix_Meldezeile_GruenBeimSenden()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            // Seit 0.11.2 mit Schlüsseln aus Meldezeile.Erlaubt (bis 0.11.1 "anderes" mit "Kobold stumm.", das jetzt still wegfiele).
            var zeile = new Meldezeile(() => 1000);
            zeile.Halten(Teilnehmermeldungen.MeldenSchluessel, "Melden: an Kommando", Meldeart.Hinweis, senden: true);
            zeile.Zeigen(Teilnehmermeldungen.UmstellungSchluessel, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 3000);
            var w = new MeldezeileFenster(zeile);
            fenster.Add(w);
            var gruen = ((SolidColorBrush)w.FindResource("Pinsel.Senden")).Color;
            Color Farbe(string text)
            {
                _ = Bildlauf.Zeichnen(w);
                return ((SolidColorBrush)Bildlauf.Nachfahren<TextBlock>(Bildlauf.Wurzel(w)).Single(t => t.Text == text).Foreground).Color;
            }
            // Seit Sarevoc Q112 steht eine Meldung da: die Umstellung (gleicher Rang, neuer) verdrängt die gehaltene, bis sie gelöst ist.
            zeile.Sendet = true;
            Pruefe.Falsch(Farbe("Eingeteilt von Ferox: Schiff 1, Gunner") == gruen, "andere Zeilen bleiben, wie sie sind, auch beim Senden");
            zeile.Sendet = false;
            zeile.Loesen(Teilnehmermeldungen.UmstellungSchluessel);
            Pruefe.Falsch(Farbe("Melden: an Kommando") == gruen, "ohne Senden nicht grün");
            zeile.Sendet = true;
            Pruefe.Gleich(gruen, Farbe("Melden: an Kommando"), "beim Senden grün");
        });
    }

    /// <summary>Q39: Die grauen Linien lassen sich nicht ziehen und sagen im Tooltip, warum; mit bekannter Stimme wieder im Akzent und ziehbar.</summary>
    [Test]
    public static void UiFix_Korridor_GrauNichtZiehbar_Tooltip()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var schwelle = a.Schwelle();
            var k = schwelle.Korridor;
            var balken = new Pegelbalken { Width = 300, Height = 28, Korridor = k };
            var grafik = new SchwellenGrafik { Modell = schwelle, Width = 684, Height = 434 };
            var stapel = new StackPanel();
            stapel.Children.Add(balken);
            stapel.Children.Add(grafik);
            var w = new Window { Content = stapel };
            fenster.Add(w);
            _ = Bildlauf.Zeichnen(w, 1, new Size(720, 500));
            Pruefe.Wahr(k.ZeigeLinien && k.LinienGrau && !k.Sichtbar, "Automatik ohne Stimme: Linien grau, nicht ziehbar");
            Pruefe.Wahr(Math.Abs(Funk.Klang.Verarbeitung.Pegelstufe.ZielDbfs - a.Ablage.Aktuell.Gate.MindestAbstandDb - k.LinieUntenDb) < 0.01, "grau dort, wo sie bei einer Stimme am Ziel läge");
            var x = balken.XFuer(k.LinieUntenDb);
            Pruefe.Gleich("GrauLinie", balken.TeilAn(new Point(x, 3)), "Pegelbalken: graue Linie");
            Pruefe.Gleich(KorridorViewModel.HilfeGrau, balken.Hilfe(Pegelbalken.Teil.GrauLinie), "Tooltip");
            Pruefe.Falsch(balken.Druecken(new Point(x, 3)), "nicht ziehbar");
            var p = grafik.Pegelflaeche;
            var y = SchwellenGrafik.DbZuY(k.LinieObenDb, p);
            Pruefe.Gleich(KorridorViewModel.HilfeGrau, grafik.HilfeAn(new Point(p.Left + 200, y)), "Grafik: Tooltip an der grauen Linie");
            Pruefe.Gleich<string?>(null, grafik.GriffAn(new Point(p.Left + 200, y)), "Grafik: nichts zu greifen");

            a.Maschine.Korridor = new KorridorStand(-23, float.NaN, float.NaN);
            k.Takt();
            Pruefe.Wahr(k.Sichtbar && !k.LinienGrau, "mit Stimme: Linien im Akzent, ziehbar");
            Pruefe.Gleich("Untergrenze", balken.TeilAn(new Point(balken.XFuer(k.LinieUntenDb), 3)), "mit Stimme: die Untergrenze");
            schwelle.Automatik = false;
            Pruefe.Falsch(k.ZeigeLinien, "ohne Automatik kein Korridor (Fest)");
        });
    }

    /// <summary>Namen eingetragen und Headset gewählt: die Startseite ohne die Frage des ersten Starts.</summary>
    private static void Vorbereiten(Aufbau a)
    {
        a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
        a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
        a.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
    }

    /// <summary>10 s Verlauf: zwei Sätze, die hinausgehen (über der Linie, Sprache), dazwischen Rauschen; am Ende wird gerade gesendet.</summary>
    private static void Verlauf(Aufbau a, float stimme = -22f)
    {
        var start = a.Uhr;
        for (var ms = 0L; ms <= 10_000; ms += 20)
        {
            var satz = ms is >= 1_000 and <= 3_600 || ms >= 6_000;
            var welle = (float)(Math.Sin(ms / 90.0) * 4 + Math.Sin(ms / 37.0) * 2);
            a.Maschine.Melden(satz
                ? new PegelMeldung(start + ms, stimme + welle, -62, -44, 0.95f, true)
                : new PegelMeldung(start + ms, -60 + (welle / 2), -62, -44, 0.05f, false));
        }
        a.Uhr = start + 10_000;
    }

    /// <summary>
    /// Zeichnet das Fenster in <paramref name="groesse"/> und legt das Bild ab. Ohne Fensterhandle kommt kein Loaded; Pegelbalken und Grafik hören dann
    /// nicht auf ihr Modell und zeichnen hier ausdrücklich neu.
    /// </summary>
    private static void Ablegen(Window w, string name, Size? groesse = null, double skala = 1)
    {
        _ = Bildlauf.Zeichnen(w, skala, groesse);
        foreach (var b in Bildlauf.Nachfahren<Pegelbalken>(Bildlauf.Wurzel(w))) b.InvalidateVisual();
        foreach (var g in Bildlauf.Nachfahren<SchwellenGrafik>(Bildlauf.Wurzel(w))) g.NeuZeichnen();
        Bildablage.Ablegen(Bildlauf.Zeichnen(w, skala, groesse).Bild, name);
    }

    /// <summary>
    /// Sichtproben für Sarevoc (mit <c>FUNK_BILDER</c>), in Anthrazit, nur offscreen gezeichnet: das Popup des Einmessens in beiden Schritten mit
    /// weichgezeichnetem Fenster, die Linien grau vor dem Einmessen, grüne Kurve und grüner Balken beim Senden, der Satz auf Ruhe, die Seite
    /// Sprachaktivierung in ihrer Mindestgröße und groß, das Funkfeld (Raster und Liste) mit rotem Mikro und ungekürztem "Senden: ...".
    /// </summary>
    [Test]
    public static void UiFix_Sichtproben()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);

                // Einmessen: Popup über der Startseite, beide Schritte.
                using (var a = new Aufbau(FunkDienstAttrappe.Beispiel()))
                {
                    Vorbereiten(a);
                    var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
                    var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
                    fenster.Add(haupt);
                    _ = a.Maschine.StartenAsync(null, null, CancellationToken.None);
                    audio.Korridor.Takt();
                    audio.Korridor.EinmessenCommand.Execute(null);
                    foreach (var (schritt, rest, db, name) in new[] { (MindestEinmessSchritt.Normal, 4000, -24f, "einmessen-schritt1"), (MindestEinmessSchritt.Leise, 3000, -37f, "einmessen-schritt2") })
                    {
                        a.Maschine.MindestEinmessen = new MindestEinmessStand(schritt, rest, 0f, 0f, float.NaN, float.NaN);
                        Sprechen(a, sendet: false, db: db);
                        h.Takt();
                        Ablegen(haupt, kurz + "-" + name, Fenstergroessen.KleinMindest);
                    }
                    audio.Korridor.PopupSchliessenCommand.Execute(null);

                    // Vor dem Einmessen: Linien grau, am Pegelbalken der Startseite und in der Grafik der Seite.
                    Sprechen(a, sendet: false, db: -45);
                    h.Takt();
                    Ablegen(haupt, kurz + "-start-ungemessen-grau", Fenstergroessen.KleinMindest);
                    h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
                    Verlauf(a);
                    h.Takt();
                    Ablegen(haupt, kurz + "-seite-ungemessen-grau", new Size(1600, 1000));

                    // Stimme bekannt, es wird gesendet: grüne Kurve, grüner Balken in der Leiste; Seite groß und in der Mindestgröße.
                    a.Maschine.Korridor = new KorridorStand(-23, float.NaN, float.NaN);
                    Verlauf(a);
                    h.Takt();
                    Ablegen(haupt, kurz + "-seite-senden-gross", new Size(1600, 1000));
                    Ablegen(haupt, kurz + "-seite-senden-mindestgroesse", Fenstergroessen.SprachaktivierungMindest);
                    h.AktiverBereich = h.Bereiche[0];
                    h.Takt();
                    Ablegen(haupt, kurz + "-start-senden-gruen", Fenstergroessen.KleinMindest);
                    funkfeld.Dispose();
                }

                // Ruhe: über der Linie gesprochen, nichts geht hinaus.
                using (var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel() with { SendeFelder = [4], GlobalSenden = false }))
                {
                    Vorbereiten(a);
                    var h = a.Haupt(out var funkfeld, out _, out _, out _);
                    var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
                    fenster.Add(haupt);
                    a.Maschine.Korridor = new KorridorStand(-23, float.NaN, float.NaN);
                    Verlauf(a);
                    h.Takt();
                    // Seit 0.11.2 ohne Satz (bis 0.11.1 "Ruhe: es wird nichts gesendet"); der Balken zeigt nur, dass nichts hinausgeht.
                    Pruefe.Wahr(h.Audio.NichtsGesendet is null && !h.Audio.Sendet, "Ruhe: kein Satz, nichts hinaus");
                    Ablegen(haupt, kurz + "-start-ruhe-hinweis", Fenstergroessen.KleinMindest);
                    h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
                    Verlauf(a);
                    h.Takt();
                    Ablegen(haupt, kurz + "-seite-ruhe-hinweis", new Size(1600, 1000));
                    funkfeld.Dispose();
                }

                // Funkfeld: Raster und Liste, Einsatzleitung hat einen anderen Kanal stummgeschaltet (rotes Mikro mit Schloss), es wird gesendet.
                using (var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Jetzt = new DateTimeOffset(2026, 10, 4, 20, 52, 0, TimeSpan.FromHours(2)) })
                {
                    var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
                    var ff = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([FullHd]));
                    fenster.Add(ff);
                    a.Dienst.LageSetzen(l => l with { Stumm = [new StummschaltungInfo(2, a.Jetzt.AddMinutes(38), 4)] });
                    foreach (var d in new[] { FunkfeldDarstellung.Raster, FunkfeldDarstellung.Liste })
                    {
                        f.Darstellung = d;
                        ff.DarstellungAnwenden();
                        Sprechen(a, sendet: true);
                        a.Uhr += 100;
                        f.Takt();
                        Ablegen(ff, kurz + "-funkfeld-" + (d == FunkfeldDarstellung.Raster ? "raster" : "liste") + "-mikro-rot-senden", null, 1.75);
                    }
                    f.Mikro.UmschaltenCommand.Execute(null);
                    a.Dienst.LageSetzen(l => l with { Stumm = [] });
                    _ = Bildlauf.Zeichnen(ff);
                    Ablegen(ff, kurz + "-funkfeld-liste-selbst-stumm", null, 1.75);
                    f.Dispose();
                }
            }
        });
    }

    /// <summary>
    /// Q40: Einmessen als Popup über dem Hauptfenster, der Inhalt weichgezeichnet; zwei Schritte mit großem Countdown, dann "Fertig" oder "noch
    /// einmal"; Knöpfe "Noch einmal" und "Abbrechen" seit 0.11.2 flach wie alle (bis 0.11.1 aus Glas).
    /// </summary>
    [Test]
    public static void UiFix_Einmessen_PopupMitWeichzeichnung()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            _ = a.Maschine.StartenAsync(null, null, CancellationToken.None);
            var k = audio.Korridor;
            k.Takt();
            var inhalt = (FrameworkElement)haupt.FindName("Inhalt");
            var popup = (FrameworkElement)haupt.FindName("EinmessPopup");
            string Text(string name) => ((TextBlock)haupt.FindName(name)).Text;
            void Stand(MindestEinmessSchritt schritt, int restMs)
            {
                a.Maschine.MindestEinmessen = new MindestEinmessStand(schritt, restMs, 0f, 0f, float.NaN, float.NaN);
                k.Takt();
                _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.KleinMindest);
            }
            _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.KleinMindest);
            Pruefe.Gleich(Visibility.Collapsed, popup.Visibility, "zu Beginn kein Popup");
            Pruefe.Gleich<object?>(null, inhalt.Effect, "zu Beginn nicht weichgezeichnet");

            k.EinmessenCommand.Execute(null);
            Pruefe.Gleich(1, a.Maschine.MindestEinmessStarts, "der Einmess-Kern beginnt");
            Stand(MindestEinmessSchritt.Normal, 5000);
            Pruefe.Gleich(Visibility.Visible, popup.Visibility, "Popup offen");
            Pruefe.Wahr(inhalt.Effect is BlurEffect && !inhalt.IsHitTestVisible, "Inhalt weichgezeichnet und nicht bedienbar");
            Pruefe.Gleich("Jetzt sprechen wie am Funk", Text("EinmessTitel"), "Schritt 1");
            Pruefe.Gleich("5", Text("EinmessZahl"), "großer Countdown");
            Pruefe.Wahr(((TextBlock)haupt.FindName("EinmessZahl")).FontSize >= 48, "groß");
            Pruefe.Gleich("Abbrechen", ((Button)haupt.FindName("EinmessSchliessen")).Content, "läuft: Abbrechen");
            Stand(MindestEinmessSchritt.Normal, 1200);
            Pruefe.Gleich("2", Text("EinmessZahl"), "zählt herunter");
            Stand(MindestEinmessSchritt.Umschalten, 1500);
            Pruefe.Gleich("Kurze Pause", Text("EinmessTitel"), "Pause");
            Stand(MindestEinmessSchritt.Leise, 5000);
            Pruefe.Gleich("Jetzt leise, wie zu jemandem im Raum", Text("EinmessTitel"), "Schritt 2");
            Pruefe.Gleich("5", Text("EinmessZahl"), "Countdown Schritt 2");
            Stand(MindestEinmessSchritt.ZuWenigUnterschied, 0);
            Pruefe.Gleich("Unterschied zu klein, noch einmal", Text("EinmessTitel"), "zu klein");
            Pruefe.Gleich(Visibility.Collapsed, ((FrameworkElement)haupt.FindName("EinmessZahl")).Visibility, "ohne Countdown");
            Pruefe.Gleich("Schließen", ((Button)haupt.FindName("EinmessSchliessen")).Content, "danach: Schließen");
            // Seit 0.11.2 flach wie alle Knöpfe (Knopf.Flach; bis 0.11.1 Glas).
            foreach (var name in EinmessKnoepfe)
            {
                var knopf = (Button)haupt.FindName(name);
                Pruefe.Falsch(Bildlauf.Nachfahren<Glasdesign.Glaskoerper>(knopf).Any(), name + ": kein Glas");
                Pruefe.Wahr(knopf.Template?.FindName("Schleier", knopf) is Border, name + ": flach wie die anderen Knöpfe (Knopf.Flach)");
            }

            k.NochEinmalCommand.Execute(null);
            Pruefe.Gleich(2, a.Maschine.MindestEinmessStarts, "noch einmal: neu begonnen");
            Stand(MindestEinmessSchritt.Normal, 5000);
            Pruefe.Gleich("Jetzt sprechen wie am Funk", Text("EinmessTitel"), "wieder Schritt 1");
            k.PopupSchliessenCommand.Execute(null);
            Pruefe.Gleich(MindestEinmessSchritt.Bereit, a.Maschine.MindestEinmessen.Schritt, "Abbrechen bricht das Einmessen ab");
            _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.KleinMindest);
            Pruefe.Gleich(Visibility.Collapsed, popup.Visibility, "Popup zu");
            Pruefe.Gleich<object?>(null, inhalt.Effect, "nicht mehr weichgezeichnet");

            k.EinmessenCommand.Execute(null);
            Stand(MindestEinmessSchritt.Fertig, 0);
            Pruefe.Gleich("Fertig", Text("EinmessTitel"), "Fertig");
            k.PopupSchliessenCommand.Execute(null);
            Pruefe.Falsch(k.PopupOffen, "Schließen");
            Pruefe.Wahr(Bildlauf.Nachfahren<Pegelbalken>(popup).Any(), "Pegel im Popup");
            funkfeld.Dispose();
        });
    }
}
