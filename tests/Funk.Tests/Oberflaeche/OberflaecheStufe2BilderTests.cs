// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Glasdesign;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Sichtproben für Sarevoc nach Stufe 2 von 0.6.0, nur im Speicher gezeichnet (mit <c>FUNK_BILDER</c> als PNG): Einstellungen zu und mit offenen
/// Feinheiten (bis 0.9.3 die Bereiche Audio, Netze und Einstellungen) sowie das Brett in Anthrazit (bis 0.11.1 mit Glasknöpfen, seit 0.11.2 flach), je ohne angeschnittenen Schatten; dazu ein Bogen mit
/// den Glasknöpfen von JG TacRelay in allen Zuständen auf der Kartenfläche. Die Startseite zeigt
/// OberflaecheStartseiteTests. Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheStufe2BilderTests
{
    /// <summary>
    /// Seit 0.10.0 (AP3, Q87) nur noch Einstellungen, einmal zugeklappt und einmal mit allen Feinheiten offen (bis 0.9.3 dazu die Seiten Audio
    /// und Meine Kanäle, deren Inhalt jetzt in den Feinheiten steht).
    /// </summary>
    private static readonly (string Kennung, bool Offen)[] Stellungen = [("einstellungen", false), ("einstellungen", true)];

    [Test]
    public static void Stufe2_BereicheUndBrett_KnoepfeFlach()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            var satz = Farbvorschlaege.Von(Farbstellung.Anthrazit);
            using (var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene()))
            {
                a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
                var h = a.Haupt(out var funkfeld, out _, out _, out _);
                foreach (var (stelle, offen) in Stellungen)
                {
                    var kennung = stelle + (offen ? "-feinheiten" : "");
                    h.AktiverBereich = h.Bereiche.First(b => b.Kennung == stelle);
                    foreach (var x in h.Einstellungen.Abschnitte) x.Feinheiten.Offen = offen;
                    var haupt = new Hauptfenster(h, a.Ablage);
                    fenster.Add(haupt);
                    Pruefe.Gleich(Groessenklasse.Seite, haupt.Klasse, kennung + ": Größe der Seiten");
                    var bild = Ansichtspruefung.Ganz(haupt, Fenstergroessen.SeiteVoreinstellung.Width);
                    Bildablage.Ablegen(bild, "bereich-" + kennung + "-glas-anthrazit");
                    var seite = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => ReferenceEquals(c.Content, h.AktiverBereich.Inhalt));
                    // Seit 0.11.2 sind alle Knöpfe flach (Sarevoc 13.10.2026: "mach überall die Glasbuttons raus"); bis 0.11.1 Glasknöpfe.
                    Pruefe.Wahr(Bildlauf.Nachfahren<ButtonBase>(seite).Count(Ansichtspruefung.ZuSehen) >= 3 && !Bildlauf.Nachfahren<Glaskoerper>(seite).Any(), kennung + ": Knöpfe flach, ohne Glas");
                    OberflaecheStartseiteTests.SchattenFrei(seite, kennung);
                    Ansichtspruefung.KontrastPruefen(seite, satz, kennung, mindestens: 10);
                }
                funkfeld.Dispose();
            }
            using (var b = new Aufbau(FunkDienstAttrappe.Raidabend()))
            {
                var h = b.Haupt(out var funkfeld, out _, out _, out _);
                h.AktiverBereich = h.Bereiche.First(x => x.Kennung == HauptViewModel.RaidKennung);
                var haupt = new Hauptfenster(h, b.Ablage);
                fenster.Add(haupt);
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1400, 900));
                Bildablage.Ablegen(bild, "brett-glas-anthrazit");
                var brett = Bildlauf.Nachfahren<BrettAnsicht>(Bildlauf.Wurzel(haupt)).Single();
                Pruefe.Wahr(Bildlauf.Nachfahren<ButtonBase>(brett).Any(Ansichtspruefung.ZuSehen) && !Bildlauf.Nachfahren<Glaskoerper>(brett).Any(), "Brett: Knöpfe flach, ohne Glas");
                OberflaecheStartseiteTests.SchattenFrei(brett, "Brett");
                funkfeld.Dispose();
            }
        });
    }

    /// <summary>Die Glasknöpfe von JG TacRelay in jedem Zustand, auf der Kartenfläche, als ein Bogen.</summary>
    [Test]
    public static void Stufe2_Glasknoepfe_InP1P2P3()
    {
        Bildlauf.Ausfuehren(_ =>
        {
            const double skala = 1.5;
            var zustaende = new (string Name, Zustandvorschau Vorschau, bool Gesperrt)[]
            {
                ("normal", Zustandvorschau.Keine, false), ("überfahren", Zustandvorschau.Ueberfahren, false),
                ("gedrückt", Zustandvorschau.Gedrueckt, false), ("Fokus", Zustandvorschau.Fokus, false), ("gesperrt", Zustandvorschau.Keine, true),
            };
            var bogen = new Bogen(150 + (zustaende.Length * 150), 60 + (3 * 205), skala, Palette.Anthrazit);
            bogen.Text("Knöpfe in der Farbstellung Anthrazit (Glasdesign) auf der Kartenfläche", 16, 10, 14, fett: true);
            for (var z = 0; z < zustaende.Length; z++) bogen.Text(zustaende[z].Name, 150 + (z * 150), 32, 12, Palette.Text2);
            var y = 52.0;
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var r = Application.Current!.Resources;
                bogen.Text(satz.Name, 16, y + 10, 12.5, Palette.Text2);
                for (var z = 0; z < zustaende.Length; z++)
                {
                    var spalte = new StackPanel { Background = (System.Windows.Media.Brush)r["Pinsel.Flaeche"] };
                    foreach (var knopf in new ButtonBase[]
                             {
                                 new Button { Style = (Style)r["Knopf.Haupt"], Content = "Verbinden" },
                                 new Button { Style = (Style)r["Knopf"], Content = "Funkfeld 10" },
                                 new ToggleButton { Style = (Style)r["Umschalter"], Content = "VA", IsChecked = true },
                                 new RadioButton { Style = (Style)r["Umschalter.Radio"], Content = "Links" },
                             })
                    {
                        Glas.SetZustandVorschau(knopf, zustaende[z].Vorschau);
                        knopf.IsEnabled = !zustaende[z].Gesperrt;
                        knopf.Margin = new Thickness(10, 6, 10, 8);
                        knopf.HorizontalAlignment = HorizontalAlignment.Left;
                        spalte.Children.Add(knopf);
                    }
                    var b = OberflaecheHilfeTests.Zeichnen(spalte, skala);
                    bogen.Bild(b, 150 + (z * 150), y, b.PixelWidth / skala, b.PixelHeight / skala);
                }
                y += 205;
            }
            Bildablage.Ablegen(bogen.Fertig(), "glasknoepfe-anthrazit");
        });
    }
}
