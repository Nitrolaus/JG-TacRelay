// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Verarbeitung;
using Funk.Protokoll.Steuerung;
using Gong = GongSolutions.Wpf.DragDrop.DragDrop;
using static Funk.Client.Dienste.FunkDienstAttrappe;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Personenkachel aus 0.11.0 (Q98, Variante A des Entwurfs, Stile.xaml) links auf der Raid-Seite, im echten Hauptfenster über dem
/// Beispielabend der Attrappe (Namen erfunden), im Speicher gezeichnet (<see cref="Bildlauf"/>) bei 175 % (Sarevocs Bildschirm), in Anthrazit: die
/// Namen links (seit 0.11.2 ein Name je Reihe über die ganze Breite; bis 0.11.1 Kacheln in Neu im Funk, Ohne Platz und Alle Mitglieder), und ein
/// Name im Ziehen: gedimmt in der Liste, das Ziehbild mit der Maus über einem Kanal, das Ziel grün mit dem Satz aus Bewerten. Das Ziehbild legt
/// im Programm gong-wpf-dragdrop an die Maus; hier wird es selbst darübergelegt, Lage nach dem ersten Anordnen. Geprüft: im Brett ein Name je
/// Reihe, nichts abgeschnitten (nur Namen kürzen), Kontrast, nur einfache Zeichen, zwei Ziehquellen links mit eigenem Ziehbild, Zugriffstasten
/// eindeutig, auch mit "Wer ist da" daneben. "Wer ist da" zeigt seit 0.11.5 keine Kacheln mehr (eine Zeile je Person, OberflaecheWerIstDaTests);
/// bis 0.11.4 prüfte diese Klasse auch seine Kacheln drei je Reihe. Mit <c>FUNK_BILDER</c> entstehen tr0110-brett-links und tr0110-brett-ziehen,
/// je mit -anthrazit. Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheKachelBilderTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Sarevocs Bildschirm: 175 %.</summary>
    private const double Skala = 1.75;

    private const double KachelHoehe = 30;

    private static string T(FormattableString s) => s.ToString(De);

    private static string Kurzname(Farbsatz satz) => satz.Stellung switch
    {
        _ => "anthrazit",
    };

    /// <summary>
    /// Der Beispielabend der Attrappe; Nordwind mit erfundenem Discord-Namen, denn Wer ist da zeigt den Discord-Namen groß. Mit
    /// <paramref name="offline"/> sind Hagel, Pollux und Zinnober offline (Kranich steht ohnehin nur im Verzeichnis).
    /// </summary>
    private static FunkDienstAttrappe Szene(bool offline)
    {
        var d = MitRaidabend();
        d.LageSetzen(l => Belegungsstand(l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId == Nordwind ? f with { DiscordName = "Nordwind" }
                : offline && f.KontoId is Hagel or Pollux or Zinnober ? f with { Online = false } : f)],
        }));
        return d;
    }

    private static Aufbau Aufbauen(FunkDienstAttrappe dienst)
    {
        var a = new Aufbau(dienst: dienst);
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        // Ein erfundener Gerätename, sonst stünde der Name dieses Rechners im Bild.
        a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
        return a;
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Brett
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Wer neu in den Funk kommt (Nachzügler): online, ohne Platz.</summary>
    private static FunkerInfo Neu(uint konto, string name, params string[] zeichen) =>
        new(konto, name, true, null, null) { ScName = name, DiscordName = name, Zeichen = zeichen, Details = new FunkerDetails([], [], []) };

    private static VerzeichnisEintrag Offline(uint konto, string name) =>
        new() { KontoId = konto, Name = name, Benutzername = name.ToLowerInvariant(), ScName = name, Stufe = Stufe.Funker, Online = false };

    /// <summary>
    /// Die Namen links auf der Raid-Seite seit 0.11.2 (Sarevoc 13.10.2026): je Person ein Name-Button über die ganze Breite (Personenkachel, 30 hoch, einer je
    /// Reihe), live oben nach Rang, offline darunter; rechts im Button die Zahl der Kanäle. Bis 0.11.1 standen hier drei Listen (Neu im Funk, Ohne
    /// Platz, Alle Mitglieder) mit Kacheln 97 x 30, zwei je Reihe, und die Karten auf den Plätzen. Ein Name im Ziehen: gedimmt, das Ziehbild
    /// (Brett.Name.Geist, 200 breit), der Kanal darunter grün mit dem Satz aus Bewerten.
    /// </summary>
    [Test]
    public static void Kacheln_Brett_ListenLinksUndZiehen_Bilder()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var was = satz.Name + ", Brett";
                var d = Szene(offline: false);
                d.OhnePraesenz.AddRange([Offline(30, "Mistral"), Offline(31, "Basalt"), Offline(32, "Orion")]);
                using var a = Aufbauen(d);
                var h = a.Haupt(out var funkfeld, out _, out _, out _);
                var b = h.Brett;
                b.KartenAbwartenAsync().GetAwaiter().GetResult();
                h.BereichWaehlenCommand.Execute("5");
                // Zwei kommen neu in den Funk (einer mit Führung, seit 0.11.7 ohne Bild dafür, aber mit einem Rang), das Verzeichnis bringt die Offline-Konten.
                a.Dienst.LageSetzen(l => Belegungsstand(l with { Funker = [.. l.Funker, Neu(15, "Wolke"), Neu(16, "Lotse", Zeichensatz.Fuehrung) with { Abzeichen = ["NAVY_05"] }] }));
                b.VerzeichnisLadenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Pruefe.Wahr(b.NamenLive.Count == 15 && b.NamenOffline.Count == 4, T($"{was}: {b.NamenLive.Count} live, {b.NamenOffline.Count} offline"));
                Pruefe.Gleich("Nordwind|Ferox|Lotse|Mustermann", string.Join("|", b.NamenLive.Take(4).Select(k => k.Name)), was + ": Admin, Führung, Einsatzleitung zuerst");

                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var (bild, _) = Bildlauf.Zeichnen(haupt, Skala, new Size(1920, 1080));
                var ansicht = Bildlauf.Nachfahren<BrettAnsicht>(Bildlauf.Wurzel(haupt)).Single();
                var links = (Grid)ansicht.FindName("Links");
                var kacheln = Kacheln(links);
                Pruefe.Gleich(19, kacheln.Count, was + ": ein Name je Person links (15 live, 4 offline)");
                Pruefe.Wahr(kacheln.All(k => k.DataContext is BrettKarte), was + ": die Namen sind Karten des Bretts");
                var spiel = 1 / Skala;
                Pruefe.Wahr(kacheln.All(k => Math.Abs(k.ActualHeight - KachelHoehe) <= spiel && Math.Abs(k.ActualWidth - kacheln[0].ActualWidth) <= spiel && k.ActualWidth > 200),
                    T($"{was}: über die ganze Breite ({kacheln[0].ActualWidth:0.#}), 30 hoch"));
                var oben = kacheln.Select(k => Math.Round(k.TransformToAncestor(links).Transform(new Point(0, 0)).Y, 1)).ToList();
                Pruefe.Gleich(oben.Count, oben.Distinct().Count(), was + ": ein Name je Reihe");
                Pruefe.Wahr(Sichtbar(links, "Teilnehmer") && Sichtbar(links, "Wolke") && Sichtbar(links, "Mistral"), was + ": Überschrift und Namen");
                var lotse = kacheln.Single(k => k.DataContext is BrettKarte { KontoId: 16 });
                Pruefe.Wahr(Bildlauf.Nachfahren<Image>(lotse).Any(ZuSehen), was + ": Lotse mit Abzeichen");
                var mistral = kacheln.Single(k => k.DataContext is BrettKarte { KontoId: 30 });
                Pruefe.Wahr(Glasdesign.Hilfe.GetText(mistral)?.Contains("offline", StringComparison.Ordinal) == true
                    && Bildlauf.Nachfahren<System.Windows.Shapes.Ellipse>(mistral).Single(e => e.Name == "Kreis").Fill is SolidColorBrush { Color.A: 0 }, was + ": Mistral offline");
                Pruefe.Wahr(oben[kacheln.IndexOf(mistral)] > oben.Where((_, i) => kacheln[i].DataContext is BrettKarte { Online: true }).Max(), was + ": offline unter den Angemeldeten");
                // Die Zahl der Kanäle rechts im Namen: Brandung in Kommando, Kobold nur in Einheitsnetzen.
                TextBlock Zahl(uint konto) => Bildlauf.Nachfahren<TextBlock>(links).Single(t => t.Name == "Zahl" && t.DataContext is BrettKarte k && k.KontoId == konto);
                Pruefe.Wahr(ZuSehen(Zahl(Brandung)) && Zahl(Brandung).Text == "1" && Zahl(Kobold).Text.Length == 0, was + ": Zahl der Kanäle");

                // Ziehen: zwei Quellen links, jede mit dem Ziehen des Bretts und dem eigenen Ziehbild.
                var geistVorlage = (DataTemplate)ansicht.FindResource("Brett.Name.Geist");
                var quellen = Bildlauf.Nachfahren<ItemsControl>(links).Where(Gong.GetIsDragSource).ToList();
                Pruefe.Wahr(quellen.Count == 2 && quellen.All(q => ReferenceEquals(Gong.GetDragHandler(q), b.Ziehen) && ReferenceEquals(Gong.GetDragAdornerTemplate(q), geistVorlage)),
                    was + ": zwei Ziehquellen links (" + quellen.Count + ")");

                NichtsAbgeschnitten(links, was, "NameText");
                KontrastPruefen(links, satz, was, mindestens: 10);
                NurEinfacheZeichen(links, was);
                Bildablage.Ablegen(bild, "tr0110-brett-links-" + Kurzname(satz));

                // Zinnober wird gezogen: gedimmt in der Liste, über Kommando grün mit Satz.
                var zinnober = b.Karte(Zinnober)!;
                var command = b.Baum.Single(k => k.Name == "Kommando");
                b.Ziehen.StartDrag(new Ziehinfo(zinnober));
                var ueber = new Ablageinfo(zinnober, null);
                command.DragOver(ueber);
                Pruefe.Wahr(command.Ablagezustand == Ablagewirkung.Passt && ueber.EffectText == "Zinnober nach Kommando", was + ": " + ueber.EffectText);
                (bild, _) = Bildlauf.Zeichnen(haupt, Skala, new Size(1920, 1080));
                var gedimmt = Kacheln(links).Single(k => ReferenceEquals(k.DataContext, zinnober));
                Pruefe.Gleich(0.35, gedimmt.Opacity, was + ": der gezogene Name gedimmt");
                var ziel = Bildlauf.Nachfahren<Border>(ansicht).Single(x => ReferenceEquals(x.DataContext, command) && ReferenceEquals(x.Style, ansicht.FindResource("Brett.Ziel")));
                Pruefe.Gleich(((SolidColorBrush)Pinsel("Pinsel.Senden")).Color, ((SolidColorBrush)ziel.BorderBrush).Color, was + ": das Ziel grün");
                var ort = ziel.TransformToAncestor(Bildlauf.Wurzel(haupt)).Transform(new Point(0, 0));

                // Das Ziehbild, wie gong es an die Maus legt (Vorlage Brett.Name.Geist).
                var geist = new Window { Content = new ContentControl { Content = zinnober, ContentTemplate = geistVorlage, Focusable = false }, Background = Brushes.Transparent };
                fenster.Add(geist);
                var (geistBild, geistGroesse) = Bildlauf.Zeichnen(geist, Skala);
                Pruefe.Wahr(Math.Abs(geistGroesse.Width - 200) <= 1 && Math.Abs(geistGroesse.Height - KachelHoehe) <= 1, T($"{was}: Ziehbild {geistGroesse.Width:0.#} x {geistGroesse.Height:0.#}"));
                var bogen = new Bogen(1920, 1080, Skala, Palette.Anthrazit);
                bogen.Bild(bild, 0, 0, 1920, 1080);
                var x = ort.X + 40;
                var y = ort.Y + 4;
                bogen.Bild(geistBild, x, y, geistGroesse.Width, geistGroesse.Height);
                bogen.Zeichnung(dc => Mauszeiger(dc, new Point(x + 120, y + KachelHoehe - 9)));
                Bildablage.Ablegen(bogen.Fertig(), "tr0110-brett-ziehen-" + Kurzname(satz));

                // Abgebrochen: nichts mehr gezogen, das Ziel neutral.
                b.Ziehen.DragCancelled();
                Pruefe.Wahr(!zinnober.Gezogen && command.Ablagezustand == Ablagewirkung.Keine && command.AblageText.Length == 0, was + ": nach dem Ziehen zurück");

                // Zugriffstasten: eindeutig, auch mit "Wer ist da" daneben.
                if (satz.Stellung == Farbvorschlaege.Voreinstellung)
                {
                    h.WerIstDa.Offen = true;
                    _ = Bildlauf.Zeichnen(haupt, 1, new Size(1600, 1000));
                    var doppelt = OberflaecheHauptfensterTests.Doppelte(OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt)), "Brett").ToList();
                    Pruefe.Gleich(0, doppelt.Count, "doppelte Zugriffstasten: " + string.Join(" | ", doppelt));
                }
                funkfeld.Dispose();
            }
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Prüfen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Die sichtbaren Personenkacheln unter <paramref name="wurzel"/> (Knöpfe im Stil Personenkachel.Knopf).</summary>
    private static List<Button> Kacheln(DependencyObject wurzel)
    {
        var stil = (Style)Application.Current!.Resources["Personenkachel.Knopf"];
        return [.. Bildlauf.Nachfahren<Button>(wurzel).Where(k => ReferenceEquals(k.Style, stil) && ZuSehen(k))];
    }

    private static void NurEinfacheZeichen(DependencyObject wurzel, string was)
    {
        foreach (var t in Bildlauf.Nachfahren<TextBlock>(wurzel).Where(ZuSehen))
        {
            var inhalt = Inhalt(t);
            Pruefe.Falsch(inhalt.Any(z => z is '\u2013' or '\u2014' or '\u2212' or '\u201E' or '\u201C' or '\u201D' or '\u2026' or '\u2022' or '\u00A0'),
                was + ": nur einfache Zeichen in \"" + inhalt + "\"");
        }
    }

    /// <summary>Ein Mauszeiger, selbst gezeichnet: weißer Pfeil mit dunkler Kante, Spitze an <paramref name="spitze"/>.</summary>
    private static void Mauszeiger(DrawingContext dc, Point spitze)
    {
        var pfeil = Geometry.Parse("M 0,0 L 0,17 L 4.5,13 L 7.5,20 L 10,19 L 7,12 L 12.5,12 Z");
        var kante = new Pen(Bogen.Pinsel(Color.FromRgb(0x10, 0x10, 0x10)), 1) { LineJoin = PenLineJoin.Round };
        kante.Freeze();
        dc.PushTransform(new TranslateTransform(spitze.X, spitze.Y));
        dc.DrawGeometry(Brushes.White, kante, pfeil);
        dc.Pop();
    }

    private static Brush Pinsel(string schluessel) => (Brush)Application.Current!.Resources[schluessel];
}
