// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die eine Farbstellung Anthrazit (Sarevoc, 18./19.10.2026, Q5, Q12, Q17), seit die Wahl zwischen P1, P2 und P3 (A31) entfallen ist: Unter
/// Einstellungen steht keine Farbwahl mehr, ein alter gespeicherter Wert wird Anthrazit, und Hauptfenster und Funkfeld tragen per Pixel die
/// Leitfarbe in Titelleiste und Global und den Akzent Petrol. Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach). Mit
/// <c>FUNK_BILDER</c> entsteht einstellungen.png. Der Test Einstellungen_Schmaler_KeinTextUeberDenKartenrand kam aus den früheren
/// OberflaecheLiveFarbwahlTests mit.
/// </summary>
internal static class OberflaecheStellungAnthrazitTests
{
    [Test]
    public static void Farben_NurAnthrazit_KeineWahlUnterEinstellungen()
    {
        using var a = new Aufbau();
        var h = a.Haupt(out var f, out _, out _, out _);
        var e = (EinstellungenViewModel)Seiten.Einstellungen(h).Inhalt;
        Pruefe.Gleich(Farbstellung.Anthrazit, a.Ablage.Aktuell.Farbstellung, "ohne gespeicherte Wahl Anthrazit");
        var eigenschaften = typeof(EinstellungenViewModel).GetProperties().Select(p => p.Name).ToList();
        Pruefe.Falsch(eigenschaften.Any(n => n.StartsWith("Farben", StringComparison.Ordinal)), "keine Farbwahl im Modell: " + string.Join(",", eigenschaften.Where(n => n.StartsWith("Farben", StringComparison.Ordinal))));
        Pruefe.Falsch(EinstellungenViewModel.InhaltFunkfeldErweitert.Contains("Farbstellung", StringComparison.Ordinal), "Erweitert nennt keine Farbstellung mehr");
        Pruefe.Falsch(typeof(HauptViewModel).GetMethods().Any(m => m.Name == "FokusBeimAufbau"), "kein Fokusziel Farbwahl mehr");

        // Mit "Schrift und Symbole deckend halten" hängt die Deckung von Global an der Leitfarbe.
        f.SchriftDeckend = true;
        f.Durchsichtigkeit = 80;
        Pruefe.Gleich(FunkfeldDeckung.Mindest(Farbvorschlaege.Anthrazit).Global, f.Deckung.Global, "Global rechnet mit der Leitfarbe von Anthrazit");
        f.Dispose();
    }

    [Test]
    public static void Farben_Anthrazit_InAllenFensternUndEinstellungen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var h = a.Haupt(out var funkfeld, out var schwelle, out var audio, out _);
            // Viel Akzent und Rot auf einmal: Erststart-Karte, gesperrtes Mikrofon (rote Schrift), Seite Sprachaktivierung,
            // Lernen an; im Funkfeld eine Ablehnung in roter Schrift.
            a.Maschine.AufnahmeFehlerBeimStart = "Windows verweigert den Zugriff auf das Mikrofon.";
            audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            for (var i = 0; i < 400; i++)
                a.Maschine.Melden(new PegelMeldung(i * 20L, i % 50 < 25 ? -25 : -62, -61, -52, i % 50 < 25 ? 0.9f : 0.1f, i % 50 < 25));
            schwelle.Automatik = true;
            schwelle.Lernen = true;
            funkfeld.SendenWaehlenAsync(funkfeld.Kacheln.Single(k => k.Name == "Jäger"), hinzufuegen: false).GetAwaiter().GetResult();
            h.Takt();

            var haupt = new Hauptfenster(h, a.Ablage);
            var feld = new FunkfeldFenster(funkfeld, a.Ablage);
            fenster.Add(haupt);
            fenster.Add(feld);
            var satz = Farbvorschlaege.Anthrazit;
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            var sprach = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.SprachaktivierungVoreinstellung).Bild;
            OberflaecheFunkfeldBilderTests.PruefeFarbe(Bildlauf.Pixel(sprach, 700, 4), satz.Leitfarbe, "Titelleiste in der Leitfarbe");
            Pruefe.Wahr(Anzahl(sprach, satz.Akzent) >= Flaeche, "Akzent " + OberflaecheFunkfeldBilderTests.Hex(satz.Akzent) + " im Hauptfenster");
            h.AktiverBereich = Seiten.Einstellungen(h);
            var einst = Bildlauf.Zeichnen(haupt, 1, new Size(1000, 1300)).Bild;
            Pruefe.Wahr(Anzahl(einst, satz.Leitfarbe) >= Flaeche, "Leitfarbe auf der Seite Einstellungen");
            var ff = Bildlauf.Zeichnen(feld).Bild;
            OberflaecheFunkfeldBilderTests.PruefeFarbe(Bildlauf.Pixel(ff, 4, 4), satz.Leitfarbe, "Marke des Funkfelds in der Leitfarbe");
            Console.WriteLine(string.Create(CultureInfo.GetCultureInfo("de-DE"),
                $"MASS Anthrazit: Leitfarbe {Anzahl(sprach, satz.Leitfarbe)}/{Anzahl(einst, satz.Leitfarbe)}/{Anzahl(ff, satz.Leitfarbe)}, Akzent {Anzahl(sprach, satz.Akzent)}/{Anzahl(einst, satz.Akzent)}/{Anzahl(ff, satz.Akzent)} Bildpunkte (Sprachaktivierung, Einstellungen, Funkfeld)"));
            funkfeld.Dispose();

            // einstellungen.png: der Bereich Einstellungen ganz, mit Durchsichtigkeit (40 %) und ohne Farbwahl, in einem frischen Stand
            // ohne Erststart-Karte und ohne Warnung zum Mikrofon.
            using var sauber = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            sauber.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
            sauber.Tastenquelle = new Maschine.TastenQuelleAttrappe(Funk.Klang.Eingabe.EingabeArt.Tastatur, Funk.Klang.Eingabe.EingabeArt.Maus);
            h = sauber.Haupt(out funkfeld, out _, out _, out _);
            funkfeld.Durchsichtigkeit = 40;
            // Seit 0.10.0 (AP3): Durchsichtigkeit und Größe nach Inhalt stehen unter Funkfeld und Darstellung > Feinheiten.
            Seiten.Feinheiten(h, Abschnittskennung.Funkfeld);
            var einstellungen = new Hauptfenster(h, sauber.Ablage);
            fenster.Add(einstellungen);
            Bildlauf.Zeichnen(einstellungen, 1, new Size(1100, 1000));
            var rollbar = Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(einstellungen))
                .First(sv => Bildlauf.Nachfahren<ContentControl>(sv).Any(c => c.Content is EinstellungenViewModel));
            var ganz = 1000 + Math.Max(0, rollbar.ExtentHeight - rollbar.ViewportHeight) + 8;
            var (bild, _) = Bildlauf.Zeichnen(einstellungen, 1, new Size(1100, Math.Ceiling(ganz)));
            var texte = Bildlauf.Nachfahren<TextBlock>(Bildlauf.Wurzel(einstellungen)).Select(t => t.Text)
                .Concat(Bildlauf.Nachfahren<AccessText>(Bildlauf.Wurzel(einstellungen)).Select(t => t.Text.Replace("_", "", StringComparison.Ordinal))).ToList();
            foreach (var soll in Erwartet)
                Pruefe.Wahr(texte.Contains(soll), "Einstellungen zeigen " + soll);
            foreach (var weg in Entfallen)
                Pruefe.Falsch(texte.Any(t => t.Contains(weg, StringComparison.Ordinal)), "Einstellungen zeigen kein " + weg);
            Pruefe.Falsch(Bildlauf.Nachfahren<RadioButton>(Bildlauf.Wurzel(einstellungen)).Any(r => r.GroupName == "Farben"), "keine Knöpfe der Farbwahl");
            Pruefe.Wahr(texte.Any(t => t.StartsWith("Das ganze Funkfeld ist 40 % durchsichtig", StringComparison.Ordinal)), "die Wirkung des Reglers steht da");
            Pruefe.Wahr(texte.Contains("Schrift und Symbole deckend halten"), "die Option steht da");
            Pruefe.Falsch(texte.Any(t => t.Contains("Leiste", StringComparison.Ordinal)), "keine Leiste mehr");
            // Seit 0.10.0 (#75): Raster und Liste wählt man nur im Funkfeld.
            Pruefe.Falsch(texte.Contains("Raster") || texte.Contains("Liste"), "unter Einstellungen keine Wahl Raster oder Liste");
            Bildablage.Ablegen(bild, "einstellungen");
            funkfeld.Dispose();
        });
    }

    /// <summary>Was unter Funkfeld und Darstellung > Feinheiten steht (seit 0.10.0 ohne "Ansicht", "Raster" und "Liste", #75).</summary>
    private static readonly string[] Erwartet = ["Durchsichtigkeit", "Größe nach Inhalt"];

    /// <summary>Die Farbwahl ist seit 19.10.2026 weg, mit ihren drei Stellungen.</summary>
    private static readonly string[] Entfallen = ["Farbstellung", "P1 heute", "P2 Weinrot", "P3 Dunkel"];

    /// <summary>
    /// Startbreite des Hauptfensters und schmaler; es ist frei skalierbar (A25). Nicht darunter: Bei der Mindestbreite 560
    /// ragen die Zeilen der globalen Tasten ("Belegen", "Num + (nicht verfügbar)") noch über die Karte (gemessen 30.09.2026, offen).
    /// </summary>
    private static readonly double[] Breiten = [1120, 1000, 900, 800, 700];

    /// <summary>
    /// Kein Text im Bereich Einstellungen ragt rechts über den Inhalt seiner Karte, bei der Startbreite 1120 und schmaler.
    /// Befund der Gegenprüfung (1000 breit): Der Hinweis neben "Ganzes Fenster" stand in einer waagerechten Reihe mit fester
    /// Höchstbreite 560 und wurde unter etwa 1030 Breite abgeschnitten, aus "nicht" wurde "ni". Bei 900 lief danach die Zeile
    /// "Sendenetz weiterschalten (A7) ..." der Karte Tastatur ohne Umbruch über den Rand.
    /// </summary>
    [Test]
    public static void Einstellungen_Schmaler_KeinTextUeberDenKartenrand()
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            // Seit 0.10.0 (AP3) mit allen Feinheiten offen: Was früher in eigenen Karten stand, steht dort.
            Seiten.Feinheiten(h);
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            var karte = (Style)haupt.FindResource("Karte");
            foreach (var breite in Breiten)
            {
                Bildlauf.Zeichnen(haupt, 1, new Size(breite, 780));
                var rollbar = Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt))
                    .First(sv => Bildlauf.Nachfahren<ContentControl>(sv).Any(c => c.Content is EinstellungenViewModel));
                var karten = Bildlauf.Nachfahren<Border>(rollbar).Where(b => ReferenceEquals(b.Style, karte) && Sichtbar(b, rollbar)).ToList();
                // Seit 0.10.0 zwei Karten: Ich und die Abschnitte (bis 0.9.3 je eine Karte für Tasten, Funkfeld, Farben, Start ...).
                Pruefe.Wahr(karten.Count >= 2, string.Create(de, $"{breite} breit: Karten des Bereichs gefunden ({karten.Count})"));
                var ueber = new List<string>();
                var geprueft = 0;
                foreach (var k in karten)
                {
                    var rechts = k.ActualWidth - k.Padding.Right - k.BorderThickness.Right;
                    foreach (var t in Bildlauf.Nachfahren<FrameworkElement>(k).Where(e => (e is TextBlock or AccessText) && Sichtbar(e, k)))
                    {
                        geprueft++;
                        var r = t.TransformToAncestor(k).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight));
                        if (r.Right > rechts + 0.5)
                            ueber.Add(string.Create(de, $"'{(t is TextBlock tb ? tb.Text : ((AccessText)t).Text)}' bis {r.Right:0}, Karte bis {rechts:0}"));
                    }
                }
                Console.WriteLine(string.Create(de, $"MASS Einstellungen {breite} breit: {geprueft} Texte in {karten.Count} Karten, über den Rand {ueber.Count}"));
                Pruefe.Wahr(ueber.Count == 0, string.Create(de, $"{breite} breit, Text über den Kartenrand: {string.Join("; ", ueber)}"));
            }
            funkfeld.Dispose();
        });
    }

    /// <summary>Im Baum bis <paramref name="bis"/> sichtbar und gemessen (ohne Fensterhandle ist IsVisible immer falsch).</summary>
    private static bool Sichtbar(FrameworkElement e, DependencyObject bis)
    {
        if (e.ActualWidth < 1 || e.ActualHeight < 1) return false;
        for (DependencyObject? d = e; d is not null && !ReferenceEquals(d, bis); d = VisualTreeHelper.GetParent(d))
        {
            if (d is UIElement { Visibility: not Visibility.Visible }) return false;
        }
        return true;
    }

    /// <summary>So viele Bildpunkte einer Farbe sind eine Fläche, ein Rand oder eine Schrift in dieser Farbe, nicht nur Kantenglättung.</summary>
    private const int Flaeche = 25;

    /// <summary>Bildpunkte, die auf eine Stufe genau <paramref name="farbe"/> haben (deckend). WPF zeichnet Pinsel genau.</summary>
    private static int Anzahl(BitmapSource bild, Color farbe)
    {
        var p = new byte[bild.PixelWidth * bild.PixelHeight * 4];
        bild.CopyPixels(p, bild.PixelWidth * 4, 0);
        var n = 0;
        for (var i = 0; i < p.Length; i += 4)
        {
            if (p[i + 3] == 255 && Math.Abs(p[i + 2] - farbe.R) <= 1 && Math.Abs(p[i + 1] - farbe.G) <= 1 && Math.Abs(p[i] - farbe.B) <= 1) n++;
        }
        return n;
    }
}
