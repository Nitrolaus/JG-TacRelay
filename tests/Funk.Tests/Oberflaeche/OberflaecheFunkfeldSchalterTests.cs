// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Funk.Client.Ablage;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Schiebeschalter 5 oder 10 Felder in der Titelzeile des Funkfelds (A33, Sarevoc: "den 5 oder 10 Knopf weg machen, sondern eine
/// Schiebebutton anfertigen"): Maus (Klick, Ziehen), Tastatur (Leertaste, Pfeile, Pos1 und Ende), dieselbe Einstellung wie
/// unter Einstellungen, gemerkt, Fokus sichtbar, Kontrast gemessen, im Raster mit beiden Zahlen, in der Liste schmal mit der
/// Zahl im Knauf. Dazu die Karte Funkfeld unter Einstellungen mit den neuen Optionen. Im Speicher ohne Fensterhandle
/// (<see cref="Bildlauf"/>): Ohne echte Maus ruft der Test die Schritte des Ziehens direkt und den Klick über OnClick;
/// Tastenereignisse brauchen eine Quelle, die hier nur im Speicher steht (keine HwndSource). Mit <c>FUNK_BILDER</c> entsteht
/// einstellungen-funkfeld.png.
/// </summary>
internal static class OberflaecheFunkfeldSchalterTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Quelle für Tastaturereignisse im Speicher (KeyEventArgs verlangt eine); kein Fenster, nicht angemeldet.</summary>
    private sealed class Tastenquelle : PresentationSource
    {
        public override Visual? RootVisual { get; set; }

        public override bool IsDisposed => false;

        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }

    private static void Taste(UIElement ziel, Key taste)
    {
        var quelle = new Tastenquelle();
        ziel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, quelle, 0, taste) { RoutedEvent = Keyboard.KeyDownEvent });
        ziel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, quelle, 0, taste) { RoutedEvent = Keyboard.KeyUpEvent });
    }

    /// <summary>Ein Klick, wie ButtonBase ihn nach dem Loslassen über dem Schalter auslöst.</summary>
    private static void Klick(ButtonBase knopf) =>
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(knopf, null);

    [Test]
    public static void Schalter_MausTastaturUndEinstellung()
    {
        Pruefe.Gleich(false, Schiebeschalter.StellungNachTaste(Key.Left), "Pfeil links: 5");
        Pruefe.Gleich(true, Schiebeschalter.StellungNachTaste(Key.Right), "Pfeil rechts: 10");
        Pruefe.Gleich(false, Schiebeschalter.StellungNachTaste(Key.Home), "Pos1: 5");
        Pruefe.Gleich(true, Schiebeschalter.StellungNachTaste(Key.End), "Ende: 10");
        Pruefe.Gleich(null, Schiebeschalter.StellungNachTaste(Key.D1), "Ziffern bleiben dem Funkfeld (Sendenetz)");
        Pruefe.Gleich(null, Schiebeschalter.StellungNachTaste(Key.Add), "+ bleibt dem Funkfeld (weiterschalten)");
        Pruefe.Gleich(0.25, Schiebeschalter.AnteilBeimZiehen(0, 5, 20), "ein Viertel des Wegs");
        Pruefe.Gleich(1.0, Schiebeschalter.AnteilBeimZiehen(0.5, 100, 20), "nicht über das Ende");
        Pruefe.Gleich(0.0, Schiebeschalter.AnteilBeimZiehen(1, double.NaN, 0) * 0, "Unsinn bleibt im Weg");
        Pruefe.Wahr(Schiebeschalter.StellungNachZiehen(0.5) && !Schiebeschalter.StellungNachZiehen(0.49), "ab der Mitte rechts");

        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            _ = a.Haupt(out var f, out _, out _, out _);
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            Bildlauf.Zeichnen(ff);
            var s = ff.FeldSchalter;
            Pruefe.Wahr(s.IsChecked == true && f.Ansicht == 10, "10 Felder: Knauf rechts");
            Pruefe.Wahr(s.Weg >= 10 && Math.Abs(s.KnaufVersatz - s.Weg) < 0.01, string.Create(De, $"Knauf am rechten Ende ({s.KnaufVersatz:0.#} von {s.Weg:0.#})"));
            Pruefe.Gleich("5 oder 10 Felder", AutomationProperties.GetName(s), "Name für Bildschirmleser");
            Pruefe.Wahr(UIElementAutomationPeer.CreatePeerForElement(s) is IToggleProvider, "für Bildschirmleser ein Umschalter");
            Pruefe.Wahr(s.Focusable && s.IsTabStop && s.FocusVisualStyle is not null, "per Tab erreichbar, Fokus sichtbar (gemessen in Durchsicht_Fokusrahmen_AufTragenderFlaeche)");
            Pruefe.Wahr(WindowChrome.GetIsHitTestVisibleInChrome(s), "in der Titelzeile bedienbar, nicht nur zum Ziehen des Fensters");
            Pruefe.Wahr(s.ToolTip is string tipp && tipp.Contains("Pfeiltasten", StringComparison.Ordinal), "Tooltip nennt die Bedienung");
            OberflaecheFunkfeldAnsichtenTests.NurEinfacheZeichen((string)s.ToolTip, "Tooltip");
            Pruefe.Gleich(0, Bildlauf.Nachfahren<ButtonBase>(Bildlauf.Wurzel(ff)).Count(b => b is not Schiebeschalter && AutomationProperties.GetName(b) == "5 oder 10 Felder"),
                "der alte Knopf 5/10 ist weg");

            // Klick schaltet um, das Funkfeld folgt samt Größe.
            var hoehe10 = ff.Height;
            Klick(s);
            Bildlauf.Leeren();
            Pruefe.Wahr(f.Ansicht == 5 && f.Kacheln.Count == 5 && a.Ablage.Aktuell.Funkfeld.Ansicht == 5, "Klick: 5 Felder, gemerkt");
            Pruefe.Wahr(ff.Height < hoehe10 - 50, string.Create(De, $"das Funkfeld nimmt die optimale Größe des 5ers ({ff.Height:0} statt {hoehe10:0})"));
            Bildlauf.Zeichnen(ff);
            Pruefe.Gleich(0.0, s.KnaufVersatz, "Knauf links");
            Klick(s);
            Pruefe.Gleich(10, f.Ansicht, "Klick: zurück auf 10");

            // Ziehen: unter der Schwelle ein Klick, darüber folgt der Knauf; losgelassen zählt die Seite.
            Bildlauf.Zeichnen(ff);
            var weg = s.Weg;
            s.ZiehenBeginnt(40);
            s.ZiehenBis(41);
            Pruefe.Falsch(s.Zieht, "ein Bildpunkt: noch kein Ziehen");
            s.ZiehenBis(40 - (weg * 0.4));
            Pruefe.Wahr(s.Zieht && Math.Abs(s.KnaufVersatz - (weg * 0.6)) < 0.01, string.Create(De, $"der Knauf folgt ({s.KnaufVersatz:0.#})"));
            s.ZiehenEndet();
            Pruefe.Gleich(10, f.Ansicht, "rechts der Mitte losgelassen: bleibt 10");
            Pruefe.Wahr(Math.Abs(s.KnaufVersatz - weg) < 0.01, "der Knauf springt ans Ende");
            s.ZiehenBeginnt(40);
            s.ZiehenBis(40 - (weg * 0.7));
            s.ZiehenEndet();
            Pruefe.Gleich(5, f.Ansicht, "über die Mitte gezogen: 5");
            s.ZiehenBeginnt(10);
            s.ZiehenBis(10 + (weg * 5));
            Pruefe.Wahr(Math.Abs(s.KnaufVersatz - weg) < 0.01, "nicht über das Ende hinaus");
            s.ZiehenEndet();
            Pruefe.Gleich(10, f.Ansicht, "ganz nach rechts: 10");

            // Tastatur.
            Taste(s, Key.Left);
            Pruefe.Gleich(5, f.Ansicht, "Pfeil links: 5");
            Taste(s, Key.Left);
            Pruefe.Gleich(5, f.Ansicht, "noch einmal links: bleibt 5");
            Taste(s, Key.Right);
            Pruefe.Gleich(10, f.Ansicht, "Pfeil rechts: 10");
            Taste(s, Key.Home);
            Pruefe.Gleich(5, f.Ansicht, "Pos1: 5");
            Taste(s, Key.End);
            Pruefe.Gleich(10, f.Ansicht, "Ende: 10");
            Taste(s, Key.Space);
            Pruefe.Gleich(5, f.Ansicht, "Leertaste schaltet um");
            Taste(s, Key.Space);
            Pruefe.Gleich(10, f.Ansicht, "und wieder zurück");

            // Von außen (bis 0.9.3 auch unter Einstellungen; seit 0.10.0 nur hier im Funkfeld, #74) und Datei.
            f.Ansicht = 5;
            Pruefe.Gleich(false, s.IsChecked, "von außen 5 gewählt: der Schalter folgt");
            f.Ansicht = 10;
            Pruefe.Gleich(true, s.IsChecked, "und zurück");
            f.Ansicht = 5;
            a.Ablage.Sichern();
            Pruefe.Gleich(5, a.Speicher.Laden().Funkfeld.Ansicht, "gemerkt");

            // Liste: schmal, der Knauf trägt die Zahl; die Enden sind leer.
            f.Darstellung = FunkfeldDarstellung.Liste;
            Bildlauf.Zeichnen(ff);
            Pruefe.Wahr(s.Kompakt, "Liste: schmal");
            var zahl = (TextBlock)s.Template.FindName("Zahl", s);
            Pruefe.Wahr(zahl.Visibility == Visibility.Visible && zahl.Text == "5", "die Zahl im Knauf: 5");
            Pruefe.Wahr(((TextBlock)s.Template.FindName("Links", s)).Visibility == Visibility.Collapsed
                && ((TextBlock)s.Template.FindName("Rechts", s)).Visibility == Visibility.Collapsed, "keine Beschriftung an den Enden");
            f.Ansicht = 10;
            Bildlauf.Zeichnen(ff);
            Pruefe.Gleich("10", zahl.Text, "die Zahl im Knauf: 10");
            Pruefe.Wahr(zahl.ActualWidth <= ((FrameworkElement)s.Template.FindName("PART_Knauf", s)).ActualWidth, "10 passt in den Knauf");
            f.Darstellung = FunkfeldDarstellung.Raster;
            Bildlauf.Zeichnen(ff);
            Pruefe.Wahr(!s.Kompakt && ((TextBlock)s.Template.FindName("Links", s)).Visibility == Visibility.Visible && zahl.Visibility == Visibility.Collapsed,
                "Raster: 5 und 10 an den Enden, der Knauf ohne Zahl");
            f.Dispose();
        });
    }

    /// <summary>
    /// Kontrast im Speicherbild bei 175 %, über reinem Weiß, deckend: Spurrand gegen die Unterlage und weißer Knauf gegen die
    /// Spur mindestens 3:1, die Zahlen mindestens 4,5:1 (die gewählte weiß, die andere zweitrangig, in der Liste dunkel im
    /// weißen Knauf).
    /// </summary>
    [Test]
    public static void Schalter_KontrastInRasterUndListe()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            foreach (var (d, n) in new[] { (FunkfeldDarstellung.Raster, 10), (FunkfeldDarstellung.Raster, 5), (FunkfeldDarstellung.Liste, 10), (FunkfeldDarstellung.Liste, 5) })
            {
                f.Darstellung = d;
                f.Ansicht = n;
                var messungen = OberflaecheFunkfeldDurchsichtTests.MessenUeberWeiss(ff, out _);
                var texte = Bildlauf.Nachfahren<TextBlock>(ff.FeldSchalter).Where(t => t.Visibility == Visibility.Visible && t.ActualWidth > 0)
                    .Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
                var zumSchalter = messungen.Where(m => m.Was.StartsWith("Schiebeschalter", StringComparison.Ordinal)
                    || texte.Any(t => m.Was == "Schrift '" + t + "'")).ToList();
                Pruefe.Wahr(zumSchalter.Count(m => m.Was.StartsWith("Schiebeschalter", StringComparison.Ordinal)) == 2, $"{d}-{n}: Spur und Knauf gemessen");
                Pruefe.Wahr(zumSchalter.Count >= (d == FunkfeldDarstellung.Liste ? 3 : 4), $"{d}-{n}: auch die Zahlen gemessen ({zumSchalter.Count})");
                foreach (var m in zumSchalter)
                    Pruefe.Wahr(m.Verhaeltnis >= m.Verlangt, string.Create(De, $"{d}-{n}, {m.Was}: {m.Verhaeltnis:0.00}:1, verlangt {m.Verlangt}:1"));
                Console.WriteLine(string.Create(De, $"MASS Schiebeschalter {d}-{n}: {string.Join(", ", zumSchalter.Select(m => string.Create(De, $"{m.Was} {m.Verhaeltnis:0.0}:1")))}"));
            }
            f.Dispose();
        });
    }

    /// <summary>
    /// Funkfeld unter Einstellungen (A32, A33; seit 0.10.0 in den Feinheiten von Funkfeld und Darstellung, E6): Regler bis 85 %, die Option
    /// "Schrift und Symbole deckend halten" ab Werk aus (statt "Ganzes Fenster"), der Knopf "Größe nach Inhalt" mit dem neuen Hinweis. Bild
    /// einstellungen-funkfeld.png.
    /// </summary>
    [Test]
    public static void Einstellungen_Funkfeld_NeueOptionenUndBild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var h = a.Haupt(out var f, out _, out _, out _);
            Seiten.Feinheiten(h, Abschnittskennung.Funkfeld);
            var haupt = new Hauptfenster(h, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 1060));
            var wurzel = Bildlauf.Wurzel(haupt);
            // Seit 0.10.0 keine eigene Karte mehr: die Feinheiten von Funkfeld und Darstellung um den Regler der Durchsichtigkeit.
            var regler = Bildlauf.Nachfahren<Slider>(wurzel).Single(r => AutomationProperties.GetName(r) == "Durchsichtigkeit des Funkfelds in Prozent");
            var karte = (FrameworkElement)Bildlauf.Nachfahren<Expander>(wurzel).Single(x => x.Name == "FunkfeldFeinheiten");
            Pruefe.Wahr(Bildlauf.Nachfahren<Slider>(karte).Contains(regler), "der Regler steht in den Feinheiten von Funkfeld und Darstellung");
            Pruefe.Gleich(85.0, regler.Maximum, "Regler unter Einstellungen bis 85 %");
            var option = Bildlauf.Nachfahren<CheckBox>(karte).Single(c => AutomationProperties.GetName(c) == "Schrift und Symbole deckend halten");
            Pruefe.Gleich(false, option.IsChecked, "ab Werk aus: das ganze Fenster wird durchsichtig");
            Pruefe.Falsch(Bildlauf.Nachfahren<CheckBox>(karte).Any(c => AutomationProperties.GetName(c) == "Ganzes Fenster durchsichtig"), "der alte Schalter ist weg");
            option.IsChecked = true;
            Pruefe.Wahr(f.SchriftDeckend, "die Option stellt das Funkfeld");
            option.IsChecked = false;
            var knopf = Bildlauf.Nachfahren<Button>(karte).Single(b => AutomationProperties.GetName(b) == "Größe des Funkfelds nach dem Inhalt");
            Pruefe.Wahr(knopf.Command is not null, "Größe nach Inhalt bleibt");
            var hinweise = Bildlauf.Nachfahren<TextBlock>(karte).Select(t => t.Text).ToList();
            Pruefe.Wahr(hinweise.Any(t => t.Contains("optimale", StringComparison.Ordinal)), "der Hinweis erklärt die optimale Größe und das Merken");
            foreach (var t in hinweise) OberflaecheFunkfeldAnsichtenTests.NurEinfacheZeichen(t, "Karte Funkfeld");
            ReglerStellt(f, regler);

            // Bild der Karte: in einem Bogen, über einen VisualBrush, damit die Karte vollständig drauf ist.
            var b = new Bogen(karte.ActualWidth + 48, karte.ActualHeight + 96, 1.75, OberflaecheFunkfeldBilderTests.Grund);
            b.Text("Einstellungen - Funkfeld und Darstellung, Feinheiten", 24, 16, 16, fett: true);
            b.Text("Durchsichtigkeit bis 85 %, das ganze Fenster; die Option \"Schrift und Symbole deckend halten\" ab Werk aus; Größe nach Inhalt.",
                24, 42, 12, OberflaecheFunkfeldBilderTests.Grau);
            var pinsel = new VisualBrush(karte) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            b.Zeichnung(dc => dc.DrawRectangle(pinsel, null, new Rect(24, 72, karte.ActualWidth, karte.ActualHeight)));
            Bildablage.Ablegen(b.Fertig(), "einstellungen-funkfeld");
            f.Dispose();
        });

        // Regler unter Einstellungen und Funkfeld sind eins.
        static void ReglerStellt(FunkfeldViewModel f, Slider regler)
        {
            regler.Value = 85;
            Pruefe.Gleich(85, f.Durchsichtigkeit, "Regler unter Einstellungen stellt das Funkfeld auf 85 %");
            regler.Value = 0;
        }
    }
}
