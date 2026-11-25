// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Funk.Client.Ablage;
using Funk.Client.Fenster;
using Funk.Client.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Regler der Durchsichtigkeit in der Titelzeile des Funkfelds (A30, Sarevoc: "links neben der Windows-Leiste", bestätigt: in der
/// Titelzeile links neben den Fensterknöpfen). Geprüft: dieselbe Einstellung wie unter Einstellungen, in beide Richtungen,
/// gemerkt, 0 bis 85 %; jeder Wert wirkt sofort, auch beim Ziehen über 0 hinweg, auf dasselbe Fenster (A33, Sarevoc: "beim
/// Schieben der Transparenz blendet das Fenster aus, das soll so nicht sein"; vorher öffnete die App das Funkfeld mit
/// anderer Schichtung neu); der Regler passt in alle Ansichten, ohne dass das Fenster breiter wird (Liste 264, seit Stufe 3). Die Logik
/// ohne Fenster, Verdrahtung und Platz im Speicherbild (<see cref="Bildlauf"/>, ohne Fensterhandle). Mit <c>FUNK_BILDER</c>
/// entsteht funkfeld-titelzeile.png mit Regler und Schiebeschalter.
/// </summary>
internal static class OberflaecheFunkfeldReglerTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly int[] Bildstufen = [0, 40];
    private static readonly string[] AlteWege = ["FunkfeldNeuOeffnenVormerken", "FunkfeldNeuOeffnen"];

    private static List<string> Mitschreiben(INotifyPropertyChanged quelle)
    {
        var namen = new List<string>();
        quelle.PropertyChanged += (_, e) => namen.Add(e.PropertyName ?? "");
        return namen;
    }

    [Test]
    public static void Regler_Modell_WirktSofortAuchUeberNull()
    {
        using var a = new Aufbau();
        var h = a.Haupt(out var f, out _, out _, out _);
        var e = (EinstellungenViewModel)Seiten.Einstellungen(h).Inhalt;
        Pruefe.Gleich(0.0, f.ReglerWert, "Standard: deckend");
        Pruefe.Gleich("0 %", f.ReglerText, "Wertanzeige");

        // Beide Regler stellen dieselbe Einstellung und ziehen sich gegenseitig nach.
        var feld = Mitschreiben(f);
        var einstellungen = Mitschreiben(e);
        e.Durchsichtigkeit = 40;
        Pruefe.Gleich(40.0, f.ReglerWert, "Einstellungen stellen den Regler im Funkfeld");
        Pruefe.Wahr(feld.Contains(nameof(f.ReglerWert)) && feld.Contains(nameof(f.ReglerText)) && feld.Contains(nameof(f.ReglerHinweis)),
            "der Regler im Funkfeld wird benachrichtigt");
        f.ReglerWert = 25;
        Pruefe.Gleich(25.0, e.Durchsichtigkeit, "der Regler im Funkfeld stellt die Einstellungen");
        Pruefe.Wahr(einstellungen.Contains(nameof(e.Durchsichtigkeit)) && einstellungen.Contains(nameof(e.DurchsichtigkeitText)),
            "der Regler unter Einstellungen wird benachrichtigt");
        Pruefe.Gleich("25 %", e.DurchsichtigkeitText, "gleiche Anzeige wie im Funkfeld");
        Pruefe.Gleich("25 %", f.ReglerText, "Wertanzeige im Funkfeld");
        a.Ablage.Sichern();
        Pruefe.Gleich(25, a.Speicher.Laden().Funkfeld.Durchsichtigkeit, "gemerkt je Nutzer, in seiner Einstellungsdatei");
        f.ReglerWert = 95;
        Pruefe.Gleich(85, f.Durchsichtigkeit, "oben auf 85 % begrenzt");
        Pruefe.Wahr(Math.Abs(f.Durchsicht.Fenster - 0.15) < 1e-9, "bei 85 % bleiben 15 % Deckkraft, das Fenster bleibt greifbar");
        f.ReglerWert = double.NaN;
        Pruefe.Gleich(0, f.Durchsichtigkeit, "NaN wird 0");
        f.ReglerWert = 37.4;
        Pruefe.Gleich(37, f.Durchsichtigkeit, "ganze Prozent");

        // Jeder Wert wirkt sofort, auch über 0 hinweg: nichts wird vorgemerkt, kein Fenster wird neu geöffnet.
        foreach (var w in new[] { 25, 10, 0, 5, 0, 50, 85, 1, 0 })
        {
            f.ReglerWert = w;
            Pruefe.Gleich(w, f.Durchsichtigkeit, string.Create(De, $"{w} %: sofort"));
            Pruefe.Gleich((double)w, f.ReglerWert, string.Create(De, $"{w} %: der Regler zeigt, was gilt"));
            Pruefe.Wahr(Math.Abs(f.Durchsicht.Fenster - (1 - (w / 100.0))) < 1e-9 && f.Durchsicht.Hintergrund == 1,
                string.Create(De, $"{w} %: das ganze Fenster, die Flächen deckend"));
        }
        foreach (var weg in new[] { "ReglerZiehenBeginnt", "ReglerZiehenEndet", "NeuOeffnenNoetig", "ReglerFokusWeitergeben", "ReglerFokusAbholen", "GanzesFenster" })
            Pruefe.Gleich(0, typeof(FunkfeldViewModel).GetMember(weg).Length, weg + " gibt es nicht mehr");

        // Raster: immer da. Liste: über das Symbol aufgeklappt, dann breit.
        Pruefe.Wahr(f.ReglerSichtbar && !f.ReglerBreit, "Raster: der Regler steht immer da");
        feld.Clear();
        f.Darstellung = FunkfeldDarstellung.Liste;
        Pruefe.Falsch(f.ReglerSichtbar, "Liste: zugeklappt");
        f.ReglerOffen = true;
        Pruefe.Wahr(f.ReglerSichtbar && f.ReglerBreit, "Liste aufgeklappt: breit");
        Pruefe.Wahr(feld.Contains(nameof(f.ReglerSichtbar)) && feld.Contains(nameof(f.ReglerBreit)), "das Fenster wird benachrichtigt");
        f.Darstellung = FunkfeldDarstellung.Raster;
        Pruefe.Falsch(f.ReglerOffen, "ein Wechsel der Ansicht klappt zu");
        OberflaecheFunkfeldAnsichtenTests.NurEinfacheZeichen(f.ReglerHinweis, "Tooltip");
        f.Dispose();
    }

    /// <summary>
    /// Am Fenster: immer geschichtet, auch deckend; beim Ziehen am Knauf wirkt jeder Wert sofort auf dasselbe Fenster, über
    /// 0 hinweg und bis 85 %, die Tastatur ebenso. Es gibt kein Ereignis zum Neuöffnen mehr, und die App kennt keinen Weg
    /// dafür (vorher: NeuOeffnen, FunkfeldNeuOeffnenVormerken, Vormerken beim Ziehen).
    /// </summary>
    [Test]
    public static void Regler_Fenster_WirktSofortOhneNeuesFenster()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var f = a.Funkfeld();
            f.Durchsichtigkeit = 30;
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            Bildlauf.Zeichnen(ff);
            var regler = ff.TitelRegler;
            // Wie der Regler unter Einstellungen: 0 bis 85 in Schritten zu 5.
            Pruefe.Gleich((double)FunkfeldEinstellungen.DurchsichtigkeitMax, regler.Maximum, "bis 85 %, wie die Einstellung");
            Pruefe.Gleich(0.0, regler.Minimum, "ab 0");
            Pruefe.Wahr(regler.SmallChange == 5 && regler.LargeChange == 10 && regler.TickFrequency == 5 && regler.IsSnapToTickEnabled,
                "Pfeiltasten 5 %, Bild auf und ab 10 %, Stufen zu 5 % wie unter Einstellungen");
            Pruefe.Wahr(WindowChrome.GetIsHitTestVisibleInChrome(regler), "in der Titelzeile bedienbar, nicht nur zum Ziehen des Fensters");
            Pruefe.Gleich(f.ReglerHinweis, regler.ToolTip as string, "Tooltip");
            Pruefe.Wahr(regler.Focusable && regler.IsTabStop, "per Tab erreichbar");
            Pruefe.Gleich("Durchsichtigkeit des Funkfelds in Prozent", AutomationProperties.GetName(regler), "Name für Bildschirmleser");
            Pruefe.Wahr(regler.FocusVisualStyle is not null, "Fokus sichtbar (gemessen in Durchsicht_Fokusrahmen_AufTragenderFlaeche)");
            Pruefe.Gleich(30.0, regler.Value, "zeigt die Einstellung");
            Pruefe.Gleich("30 %", ff.ReglerWertText.Text, "Wertanzeige");
            Pruefe.Wahr(ff.AllowsTransparency, "immer geschichtet (A33)");
            Pruefe.Gleich(null, typeof(FunkfeldFenster).GetEvent("NeuOeffnen"), "kein Ereignis zum Neuöffnen mehr");
            var privat = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            foreach (var weg in AlteWege)
                Pruefe.Gleich(null, typeof(Funk.Client.App).GetMethod(weg, privat), "die App öffnet das Funkfeld nicht mehr neu: " + weg);

            // Ziehen: jeder Wert sofort, auch über 0 und bis 85, am selben Fenster.
            var knauf = Bildlauf.Nachfahren<Thumb>(regler).Single();
            knauf.RaiseEvent(new DragStartedEventArgs(0, 0));
            foreach (var w in new[] { 15.0, 0, 5, 40, 85, 0, 60 })
            {
                regler.Value = w;
                var was = string.Create(De, $"beim Ziehen auf {w} %");
                Pruefe.Gleich((int)w, f.Durchsichtigkeit, was + ": gilt sofort");
                Pruefe.Wahr(Math.Abs(ff.Opacity - (1 - (w / 100))) < 1e-9, string.Create(De, $"{was}: Deckkraft {ff.Opacity:0.00}"));
                Pruefe.Wahr(ff.AllowsTransparency, was + ": dasselbe geschichtete Fenster");
                Pruefe.Gleich(string.Create(De, $"{w} %"), ff.ReglerWertText.Text, was + ": Wertanzeige");
            }
            knauf.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
            Pruefe.Gleich(60, f.Durchsichtigkeit, "losgelassen: bleibt, was gezeigt war");

            // Tastatur (Befehle des Reglers, an Pfeile und Bild auf/ab gebunden).
            regler.Value = 0;
            Slider.IncreaseLarge.Execute(null, regler);
            Pruefe.Gleich(10, f.Durchsichtigkeit, "Bild auf: 10 %");
            Slider.IncreaseSmall.Execute(null, regler);
            Pruefe.Gleich(15, f.Durchsichtigkeit, "Pfeil: 15 %");
            Slider.DecreaseLarge.Execute(null, regler);
            Pruefe.Gleich(5, f.Durchsichtigkeit, "Bild ab: 5 %");
            Slider.DecreaseSmall.Execute(null, regler);
            Pruefe.Gleich(0, f.Durchsichtigkeit, "Pfeil: 0 %");
            Pruefe.Wahr(ff.Opacity == 1 && ff.AllowsTransparency, "deckend, weiter dasselbe Fenster");
            regler.Value = 100;
            Pruefe.Wahr(regler.Value == 85 && Math.Abs(ff.Opacity - 0.15) < 1e-9, "mehr als 85 geht nicht: 15 % Deckkraft bleiben");
            f.Dispose();
        });
    }

    [Test]
    public static void Regler_Titelzeile_PasstOhneBreiteresFenster()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            foreach (var (d, n, offen) in Ansichten)
            {
                f.Darstellung = d;
                f.Ansicht = n;
                f.ReglerOffen = offen;
                var (_, groesse) = Bildlauf.Zeichnen(ff);
                var wurzel = Bildlauf.Wurzel(ff);
                var was = $"{d} {n}{(d == FunkfeldDarstellung.Liste ? offen ? " aufgeklappt" : " zugeklappt" : "")}";
                if (d == FunkfeldDarstellung.Liste)
                    Pruefe.Wahr(Math.Abs(groesse.Width - 288) < 0.5, string.Create(De, $"{was}: die Liste bleibt 288 breit (264 plus VA/Taste je Feld), ist {groesse.Width:0.#}"));
                else
                    Pruefe.Wahr(Math.Abs(groesse.Width - 776) < 0.5, string.Create(De, $"{was}: das Raster bleibt 776 breit, ist {groesse.Width:0.#}"));
                var knopf = d == FunkfeldDarstellung.Liste ? ff.ReglerSymbol
                    : Bildlauf.Nachfahren<RadioButton>(wurzel).First(r => AutomationProperties.GetName(r) == "Ansicht Raster");
                Pruefe.Gleich(d == FunkfeldDarstellung.Liste ? Visibility.Visible : Visibility.Collapsed, ff.ReglerSymbol.Visibility, was + ": Symbol nur in der Liste");
                // Der Schiebeschalter 5/10 (A33) steht in jeder Ansicht in der Titelzeile, vor Schließen, in der Liste schmal.
                var schalter = Rechteck(ff.FeldSchalter, wurzel);
                var titel = Rechteck(ff.Kopf, wurzel);
                Pruefe.Wahr(schalter.Top >= titel.Top - 0.5 && schalter.Bottom <= titel.Bottom + 0.5 && schalter.Right <= titel.Right - 30 + 0.5,
                    string.Create(De, $"{was}: Schiebeschalter in der Titelzeile vor Schließen ({schalter} in {titel})"));
                Pruefe.Gleich(d == FunkfeldDarstellung.Liste, ff.FeldSchalter.Kompakt, was + ": schmal nur in der Liste");
                Pruefe.Wahr(d == FunkfeldDarstellung.Raster || schalter.Width + ff.FeldSchalter.Margin.Left + ff.FeldSchalter.Margin.Right <= 32.5,
                    string.Create(De, $"{was}: in der Liste höchstens 32 breit ({schalter.Width:0.#})"));
                Console.WriteLine(string.Create(De, $"MASS Schiebeschalter {was}: {schalter.Width:0.#} x {schalter.Height:0.#} bei {schalter.Left:0.#}"));
                if (d == FunkfeldDarstellung.Raster || offen)
                {
                    var regler = Rechteck(ff.TitelRegler, wurzel);
                    var wert = Rechteck(ff.ReglerWertText, wurzel);
                    var links = Rechteck(knopf, wurzel).Left;
                    Console.WriteLine(string.Create(De, $"MASS Regler in der Titelzeile, {was}: {regler.Width:0} breit, bei {regler.Left:0} bis {wert.Right:0}, Knöpfe ab {links:0}"));
                    Pruefe.Wahr(regler.Width >= 70, string.Create(De, $"{was}: Regler {regler.Width:0} breit"));
                    Pruefe.Wahr(wert.Right <= links + 0.5 && links - wert.Right <= 14, string.Create(De, $"{was}: direkt links neben den Fensterknöpfen ({wert.Right:0} und {links:0})"));
                    Pruefe.Wahr(regler.Top >= 0 && regler.Bottom <= 24, was + ": in der Titelzeile");
                    var wt = ff.ReglerWertText;
                    var breitester = new FormattedText("85 %", De, FlowDirection.LeftToRight, new Typeface(wt.FontFamily, wt.FontStyle, wt.FontWeight, wt.FontStretch),
                        wt.FontSize, Brushes.White, 1).Width;
                    Pruefe.Wahr(breitester <= wert.Width, string.Create(De, $"{was}: auch '85 %' ungekürzt ({breitester:0.0} in {wert.Width:0})"));
                    Pruefe.Gleich(ff.ReglerWertText.Text, f.ReglerText, was + ": Wertanzeige");
                }
                else
                {
                    Pruefe.Gleich(Visibility.Collapsed, ff.ReglerUnterlage.Visibility, was + ": Regler zugeklappt");
                }
                // Der Text der Sendenetze bleibt lesbar, solange er zu sehen ist: ganz oder wenigstens 60 Einheiten.
                if (!(d == FunkfeldDarstellung.Liste && offen))
                {
                    var kopf = ff.KopfText;
                    var voll = new FormattedText(kopf.Text, De, FlowDirection.LeftToRight, new Typeface(kopf.FontFamily, kopf.FontStyle, kopf.FontWeight, kopf.FontStretch),
                        kopf.FontSize, Brushes.White, 1).Width + kopf.Padding.Left + kopf.Padding.Right;
                    Console.WriteLine(string.Create(De, $"MASS Titelzeile {was}: '{kopf.Text}' braucht {voll:0}, hat {kopf.ActualWidth:0}"));
                    Pruefe.Wahr(kopf.ActualWidth >= Math.Min(voll, 60) - 0.5, string.Create(De, $"{was}: {kopf.ActualWidth:0} Einheiten für '{kopf.Text}'"));
                    // Ein kurzes Sendenetz steht auch in der Liste neben dem Symbol ganz da, wie vor dem Regler.
                    Pruefe.Gleich("Senden: Crew", kopf.Text, was + ": Szene");
                    Pruefe.Wahr(kopf.ActualWidth >= voll - 0.5, string.Create(De, $"{was}: '{kopf.Text}' ungekürzt ({kopf.ActualWidth:0.0} von {voll:0.0})"));
                }
                else
                {
                    Pruefe.Gleich(Visibility.Collapsed, ff.KopfText.Visibility, was + ": der Text ruht, solange der Regler aufgeklappt ist");
                }
            }
            f.Dispose();
        });
    }

    private static readonly (FunkfeldDarstellung D, int N, bool Offen)[] Ansichten =
    [
        (FunkfeldDarstellung.Raster, 10, false), (FunkfeldDarstellung.Raster, 5, false),
        (FunkfeldDarstellung.Liste, 10, false), (FunkfeldDarstellung.Liste, 10, true),
        (FunkfeldDarstellung.Liste, 5, false), (FunkfeldDarstellung.Liste, 5, true),
    ];

    /// <summary>
    /// funkfeld-titelzeile.png für Sarevoc: die Titelzeile mit Regler und Schiebeschalter 5/10 in Raster 10, Raster 5 und Liste 10
    /// (Regler zu- und aufgeklappt), je bei 0 und 40 % (ganzes Fenster), in der Voreinstellung Anthrazit, über dem selbst
    /// gezeichneten Spielbild; dazu die Titelzeile vergrößert.
    /// </summary>
    [Test]
    public static void Regler_Bild_TitelzeileInAllenAnsichten()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
            var zellen = new List<(string Was, int Prozent, RenderTargetBitmap Bild, Size Groesse, double Fenster)>();
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            foreach (var (d, n, offen, name) in new[]
                     {
                         (FunkfeldDarstellung.Raster, 10, false, "Raster 10"), (FunkfeldDarstellung.Raster, 5, false, "Raster 5"),
                         (FunkfeldDarstellung.Liste, 10, false, "Liste 10, Regler zugeklappt"), (FunkfeldDarstellung.Liste, 10, true, "Liste 10, Regler aufgeklappt"),
                     })
            {
                foreach (var p in Bildstufen)
                {
                    // Immer dasselbe Fenster: Die Durchsichtigkeit öffnet keines mehr neu (A33).
                    f.Durchsichtigkeit = p;
                    f.Darstellung = d;
                    f.Ansicht = n;
                    f.ReglerOffen = offen;
                    OberflaecheFunkfeldBilderTests.Pegel(a, f);
                    var (bild, groesse) = Bildlauf.Zeichnen(ff, 1.75);
                    Pruefe.Gleich(f.ReglerText, ff.ReglerWertText.Text, name + ": Wertanzeige im Bild");
                    zellen.Add((name, p, bild, groesse, ff.Opacity));
                }
            }
            Bildablage.Ablegen(Vergleichsbogen(zellen), "funkfeld-titelzeile");
            f.Dispose();
        });
    }

    private static RenderTargetBitmap Vergleichsbogen(List<(string Was, int Prozent, RenderTargetBitmap Bild, Size Groesse, double Fenster)> zellen)
    {
        const double rand = 24;
        const double spalte = 830;
        const double lupe = 2;
        var hoehen = zellen.Where(z => z.Prozent == 0).Select(z => z.Groesse.Height).ToList();
        var zeilen = hoehen.Select(hh => 30 + hh + 24 + 12 + (24 * lupe) + 30).ToList();
        var b = new Bogen(rand + (2 * spalte) + rand, 100 + zeilen.Sum() + 60, 1.75, OberflaecheFunkfeldBilderTests.Grund);
        b.Text("Funkfeld - Titelzeile mit Regler der Durchsichtigkeit (A30) und Schiebeschalter 5 oder 10 Felder (A33), Farbstellung Anthrazit", rand, 16, 20, fett: true);
        b.Text("Links 0 % (deckend), rechts 40 %, das ganze Fenster. Der Regler stellt dieselbe Einstellung wie unter Einstellungen und wirkt beim Ziehen sofort. Darunter die Titelzeile vergrößert.",
            rand, 46, 12, OberflaecheFunkfeldBilderTests.Grau);
        b.Text("Schiebeschalter: Raster mit 5 und 10 an den Enden, der weiße Knauf gleitet dazwischen. Liste (264 breit): Der Knauf trägt die Zahl. Klick, Ziehen, Leertaste, Pfeiltasten.",
            rand, 64, 12, OberflaecheFunkfeldBilderTests.Grau);
        var y = 100.0;
        var zeile = 0;
        foreach (var gruppe in zellen.GroupBy(z => z.Was))
        {
            foreach (var z in gruppe)
            {
                var x = rand + (z.Prozent == 0 ? 0 : spalte);
                b.Text($"{z.Was} - {z.Prozent} %", x, y, 14, fett: true);
                var flaeche = new Rect(x, y + 26, spalte - 30, z.Groesse.Height + 24);
                b.Zeichnung(dc => Spielbild.Zeichnen(dc, flaeche));
                b.Bild(z.Bild, x + 12, y + 38, z.Groesse.Width, z.Groesse.Height, z.Fenster);
                // Die Titelzeile vergrößert: rechter Teil, wo Regler und Knöpfe sitzen.
                var breite = Math.Min(z.Groesse.Width, 330);
                var ausschnitt = new CroppedBitmap(z.Bild, new Int32Rect((int)((z.Groesse.Width - breite) * 1.75), 0, (int)(breite * 1.75), (int)(24 * 1.75)));
                var lupeY = y + 38 + z.Groesse.Height + 18;
                b.Zeichnung(dc => Spielbild.Zeichnen(dc, new Rect(x, lupeY - 4, (breite * lupe) + 8, (24 * lupe) + 8)));
                b.Bild(ausschnitt, x + 4, lupeY, breite * lupe, 24 * lupe, z.Fenster);
            }
            y += zeilen[zeile++];
        }
        b.Text("Das Funkfeld ist immer geschichtet: Beim Ziehen am Regler verschwindet es nicht mehr, jeder Wert wirkt sofort, bis 85 % (15 % Deckkraft bleiben, damit es greifbar bleibt).",
            rand, y + 10, 12, OberflaecheFunkfeldBilderTests.Grau);
        return b.Fertig();
    }

    /// <summary>Rechteck eines Elements in Einheiten, bezogen auf die Wurzel.</summary>
    private static Rect Rechteck(FrameworkElement e, FrameworkElement wurzel) =>
        e.TransformToAncestor(wurzel).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
}
