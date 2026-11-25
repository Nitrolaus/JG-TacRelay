// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Q93 a (Sarevoc 12.10.2026): die Spalte "Wer ist da" in Klein. Öffnet man sie, wird das Hauptfenster um ihre Breite breiter, nach rechts, sonst nach
/// links ausweichend, ohne den Monitor zu verlassen; Start bleibt ganz (zwei Spalten, kein Rollen). Ohne Platz liegt sie als Schublade über Start.
/// Schließen bringt Breite und Lage zurück, nur wenn das Fenster unverändert steht; gemerkt wird die Größe ohne die Spalte. Maximiert und breiter
/// gezogen wie bisher. Reine Rechnung in <see cref="Fenstergroessen"/> bei 100, 125, 150 und 175 %, das Fenster nur im Speicher
/// (<see cref="Bildlauf"/>); mit <c>FUNK_BILDER</c> entsteht umbau-start-900-wer-ist-da-breit (die Schublade zeichnet
/// <see cref="OberflaecheUmbauStartTests"/>). Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheWerIstDaPlatzTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Full HD hochkant bei 100 %: 1080 breit, schmaler als Klein mit Spalte (1240).</summary>
    private static readonly Bildschirm Hochkant = new(new Pixelrechteck(0, 0, 1080, 1920), new Pixelrechteck(0, 0, 1080, 1880), 1.0, true);

    /// <summary>4K hochkant bei 175 %: 2160 Bildpunkte sind 1234 Einheiten, knapp zu schmal für 1240.</summary>
    private static readonly Bildschirm Hochkant175 = new(new Pixelrechteck(0, 0, 2160, 3840), new Pixelrechteck(0, 0, 2160, 3780), 1.75, true);

    /// <summary>Full HD bei 125 %: Arbeitsbereich 1536 x 825,6 Einheiten.</summary>
    private static readonly Bildschirm FullHd125 = new(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1032), 1.25, true);

    /// <summary>Laptop mit Full HD bei 150 %: Arbeitsbereich 1280 x 688 Einheiten.</summary>
    private static readonly Bildschirm Laptop150 = new(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1032), 1.5, true);

    /// <summary>Full HD bei 175 %: 1097 Einheiten breit.</summary>
    private static readonly Bildschirm FullHd175 = new(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1040), 1.75, true);

    /// <summary>Links neben Full HD ein zweites Full HD bei 100 % (-1920 bis 0).</summary>
    private static readonly Bildschirm Links100 = new(new Pixelrechteck(-1920, 0, 1920, 1080), new Pixelrechteck(-1920, 0, 1920, 1040), 1.0, false);

    /// <summary>Links neben Full HD ein WQHD bei 150 % (-2560 bis 0): gemischte Skala, die Ecke kann dort liegen, während das Fenster auf Full HD steht.</summary>
    private static readonly Bildschirm LinksWqhd = new(new Pixelrechteck(-2560, 0, 2560, 1440), new Pixelrechteck(-2560, 0, 2560, 1400), 1.5, false);

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Die Rechnung für ein Fenster mit der Skala des Monitors unter seiner Ecke (dort steht es in diesen Fällen auch überwiegend).</summary>
    private static (Spaltenplatz Art, Platz? Breiter) Oeffnen(int links, int oben, double breite, double hoehe, params Bildschirm[] bildschirme) =>
        Fenstergroessen.SpalteOeffnen(new FensterOrt(links, oben, breite, hoehe), bildschirme, bildschirme.Length == 0 ? 1 : Platzierung.Am(links, oben, bildschirme).Skala);

    /// <summary>Die Rechnung für ein Fenster, das gerade die Skala <paramref name="skala"/> hat.</summary>
    private static (Spaltenplatz Art, Platz? Breiter) OeffnenMit(double skala, int links, int oben, double breite, double hoehe, params Bildschirm[] bildschirme) =>
        Fenstergroessen.SpalteOeffnen(new FensterOrt(links, oben, breite, hoehe), bildschirme, skala);

    private static (Spaltenplatz Art, Platz? Breiter) Breiter(Platz p) => (Spaltenplatz.Breiter, p);

    /// <summary>
    /// Die Rechnung: Klein wird um 340 breiter, nach rechts, solange der Arbeitsbereich seines Monitors reicht, sonst so weit nach links wie nötig,
    /// nie auf einen anderen Monitor; ohne Platz eine Schublade, ab 1240 Breite bleibt alles. Sein Monitor ist der, auf dem es überwiegend steht
    /// (seit dem Befund der Prüfung; vorher der unter der Ecke). In Bildpunkten mit der Skala des Monitors, bei 100, 125, 150 und 175 %, auch
    /// neben einem Monitor mit anderer Skala und mit der Ecke auf dem Nachbarmonitor. Dazu, wann das Fenster als unverändert gilt und was gemerkt
    /// wird.
    /// </summary>
    [Test]
    public static void WerIstDaPlatz_Rechnung_RechtsLinksSchublade_Bei100Bis175Prozent()
    {
        Pruefe.Gleich(340.0, Fenstergroessen.WerIstDaBreite, "Breite der Spalte (A48)");
        Pruefe.Gleich(1240.0, Fenstergroessen.WerIstDaDanebenAb, "Klein ab Werk plus die Spalte");

        // 100 %, Full HD (Arbeitsbereich 1920 x 1040): Klein mittig (510, 70) wird nach rechts breiter, die Ecke bleibt.
        Pruefe.Gleich(Breiter(new Platz(510, 70, 1240, 900, FullHd)), Oeffnen(510, 70, 900, 900, FullHd), "100 %: nach rechts");
        Pruefe.Gleich(Breiter(new Platz(680, 70, 1240, 900, FullHd)), Oeffnen(680, 70, 900, 900, FullHd), "100 %: passt genau bis an den Rand");
        Pruefe.Gleich(Breiter(new Platz(680, 140, 1240, 900, FullHd)), Oeffnen(1020, 140, 900, 900, FullHd), "100 %: am rechten Rand nach links ausgewichen");
        Pruefe.Gleich(Breiter(new Platz(680, 70, 1240, 900, FullHd)), Oeffnen(1500, 70, 900, 900, FullHd), "100 %: ragte rechts hinaus, jetzt ganz drin");
        Pruefe.Gleich(Breiter(new Platz(-200, 70, 1240, 900, FullHd)), Oeffnen(-200, 70, 900, 900, FullHd), "links hinaus: rechts ist Platz, die Ecke bleibt");
        // Schon breit genug: wie bisher. Ein Punkt Spiel für die Rundung des Layouts.
        Pruefe.Gleich((Spaltenplatz.Daneben, (Platz?)null), Oeffnen(100, 70, 1240, 900, FullHd), "1240: daneben, nichts ändert sich");
        Pruefe.Gleich((Spaltenplatz.Daneben, (Platz?)null), Oeffnen(100, 70, 1239.5, 900, FullHd), "1239,5: gilt als 1240");
        Pruefe.Gleich(Breiter(new Platz(100, 70, 1238.9 + 340, 900, FullHd)), Oeffnen(100, 70, 1238.9, 900, FullHd), "gezogenes Klein unter 1240: um 340 breiter");
        // Kein Platz: Schublade, das Fenster bleibt.
        Pruefe.Gleich((Spaltenplatz.Schublade, (Platz?)null), Oeffnen(90, 490, 900, 900, Hochkant), "hochkant 1080 breit: Schublade");
        Pruefe.Gleich((Spaltenplatz.Schublade, (Platz?)null), Oeffnen(0, 0, 900, 900), "ohne Monitor: nichts verschieben");
        Pruefe.Gleich((Spaltenplatz.Schublade, (Platz?)null), Fenstergroessen.SpalteOeffnen(new FensterOrt(0, 0), [FullHd], 1), "ohne Größe: nichts verschieben");

        // 125 %: 1240 Einheiten sind 1550 Bildpunkte.
        Pruefe.Gleich(Breiter(new Platz(300, 0, 1240, 825.6, FullHd125)), Oeffnen(300, 0, 900, 825.6, FullHd125), "125 %: nach rechts");
        Pruefe.Gleich(Breiter(new Platz(1920 - 1550, 0, 1240, 825.6, FullHd125)), Oeffnen(397, 0, 900, 825.6, FullHd125), "125 %: Klein mittig rückt nach links");
        // 150 %: 1860 Bildpunkte, der Laptop hat 1920.
        Pruefe.Gleich(Breiter(new Platz(60, 0, 1240, 688, Laptop150)), Oeffnen(285, 0, 900, 688, Laptop150), "150 %: Laptop, fast bis an den linken Rand");
        // 175 % (Sarevocs Rechner, zwei 4K): 2170 Bildpunkte; nie auf den anderen Monitor.
        Pruefe.Gleich(Breiter(new Platz(1132, 250, 1240, 900, TestHaupt)), Oeffnen(1132, 250, 900, 900, TestHaupt, TestZweiter), "175 %: Klein mittig nach rechts");
        Pruefe.Gleich(Breiter(new Platz(3840 - 2170, 250, 1240, 900, TestHaupt)), Oeffnen(3000, 250, 900, 900, TestHaupt, TestZweiter),
            "175 %: am rechten Rand des Hauptmonitors nach links, nicht hinüber auf den zweiten");
        Pruefe.Gleich(Breiter(new Platz(7680 - 2170, 100, 1240, 900, TestZweiter)), Oeffnen(6000, 100, 900, 900, TestHaupt, TestZweiter), "175 %: auf dem zweiten Monitor");
        Pruefe.Gleich((Spaltenplatz.Schublade, (Platz?)null), Oeffnen(0, 0, 900, 594.29, FullHd175), "175 % auf Full HD (1097 breit): Schublade");
        Pruefe.Gleich((Spaltenplatz.Schublade, (Platz?)null), Oeffnen(0, 0, 900, 900, Hochkant175), "175 % auf 4K hochkant (1234 breit): Schublade");
        // Gemischt: Full HD bei 100 %, rechts daneben WQHD bei 150 %.
        Pruefe.Gleich(Breiter(new Platz(680, 70, 1240, 900, FullHd)), Oeffnen(1000, 70, 900, 900, FullHd, Wqhd), "gemischt: auf Full HD nach links, nicht auf den WQHD");
        Pruefe.Gleich(Breiter(new Platz(2200, 100, 1240, 900, Wqhd)), Oeffnen(2200, 100, 900, 900, FullHd, Wqhd), "gemischt: auf dem WQHD mit dessen 150 % nach rechts");
        // Befund der Prüfung: Es zählt der Monitor, auf dem das Fenster überwiegend steht (wie Windows ihn für die Skala nimmt), nicht der unter
        // der Ecke. Bis dahin sprang ein Fenster, dessen Ecke auf dem Nachbarmonitor lag, ganz dort hinüber.
        Pruefe.Gleich(Breiter(new Platz(-200, 70, 1240, 900, FullHd)), OeffnenMit(1, -200, 70, 900, 900, LinksWqhd, FullHd),
            "Ecke auf dem WQHD links, 700 von 900 auf Full HD: dort nach rechts, kein Sprung auf -1240");
        Pruefe.Gleich(Breiter(new Platz(-200, 70, 1240, 900, FullHd)), OeffnenMit(1, -200, 70, 900, 900, Links100, FullHd), "dasselbe neben einem Monitor mit 100 %");
        Pruefe.Gleich(Breiter(new Platz(-1860, 20, 1240, 900, LinksWqhd)), OeffnenMit(1.5, -1000, 20, 900, 900, LinksWqhd, FullHd),
            "überwiegend auf dem WQHD (1000 von 1350 Bildpunkten): mit 150 % dort nach links, nicht auf Full HD");
        Pruefe.Gleich(Breiter(new Platz(3300, 250, 1240, 900, TestZweiter)), OeffnenMit(1.75, 3300, 250, 900, 900, TestHaupt, TestZweiter),
            "175 %: Ecke auf dem Hauptmonitor, 1035 von 1575 auf dem zweiten: dort nach rechts, kein Sprung auf 1670");
        Pruefe.Gleich(TestZweiter, Platzierung.Ueberwiegend(new Pixelrechteck(3300, 250, 1575, 1575), [TestHaupt, TestZweiter]), "Ueberwiegend: der mit dem größten Stück");
        Pruefe.Gleich(FullHd, Platzierung.Ueberwiegend(new Pixelrechteck(-5000, 70, 900, 900), [FullHd]), "Ueberwiegend auf keinem: der nächste");

        // Unverändert: auf einen Bildpunkt und eine Einheit genau; maximiert zählt nicht.
        var vorher = new Platz(1020, 140, 900, 900, FullHd);
        var nachher = new Platz(680, 140, 1240, 900, FullHd);
        Pruefe.Wahr(Fenstergroessen.Unveraendert(new FensterOrt(680, 140, 1240, 900), nachher), "genau so");
        Pruefe.Wahr(Fenstergroessen.Unveraendert(new FensterOrt(681, 139, 1240.6, 899.5), nachher), "Rundung");
        Pruefe.Wahr(Fenstergroessen.Unveraendert(new FensterOrt(680, 140, 1240, 900, Maximiert: true), nachher), "maximiert mit demselben Normalzustand");
        Pruefe.Falsch(Fenstergroessen.Unveraendert(new FensterOrt(690, 140, 1240, 900), nachher), "verschoben");
        Pruefe.Falsch(Fenstergroessen.Unveraendert(new FensterOrt(680, 140, 1300, 900), nachher), "breiter gezogen");
        Pruefe.Falsch(Fenstergroessen.Unveraendert(new FensterOrt(680, 140), nachher), "ohne Größe");
        // Gemerkt wird ohne die Spalte.
        Pruefe.Gleich(new FensterOrt(1020, 140, 900, 900), Fenstergroessen.OhneSpalte(new FensterOrt(680, 140, 1240, 900), vorher, nachher), "unverändert: der Platz davor");
        Pruefe.Gleich(new FensterOrt(1020, 140, 900, 900, Maximiert: true), Fenstergroessen.OhneSpalte(new FensterOrt(680, 140, 1240, 900, Maximiert: true), vorher, nachher),
            "maximiert: der Platz davor, maximiert");
        Pruefe.Gleich(new FensterOrt(600, 90, 900, 900), Fenstergroessen.OhneSpalte(new FensterOrt(600, 90, 1240, 900), vorher, nachher), "verschoben: deine Lage, ohne die Spalte");
        Pruefe.Gleich(new FensterOrt(680, 140, 960, 900), Fenstergroessen.OhneSpalte(new FensterOrt(680, 140, 1300, 900), vorher, nachher), "breiter gezogen: 1300 ohne die Spalte");
        Pruefe.Gleich(new FensterOrt(680, 140, 1100, 900), Fenstergroessen.OhneSpalte(new FensterOrt(680, 140, 1100, 900), vorher, nachher),
            "schmaler gezogen: die Spalte liegt als Schublade darüber und nimmt keine Breite");
    }

    /// <summary>Breite der Grundansicht von Start (Sprechen und Hören).</summary>
    private static double Grundbreite(Hauptfenster haupt) =>
        OberflaecheUmbauStartTests.Teil<Spaltenwechsel>(OberflaecheUmbauStartTests.Seite(haupt), "Grundansicht").ActualWidth;

    /// <summary>
    /// Start ganz: zwei Spalten, bis Sprechen und Hören kein Rollen (seit 0.11.2 darf "Senden" darunter hereinrollen,
    /// <see cref="OberflaecheStartseiteTests.BisSenden"/>; bis 0.11.1 kein Rollbalken), nichts abgeschnitten (nur ein langer Name darf kürzen), Glasschatten frei.
    /// </summary>
    private static void StartGanz(Hauptfenster haupt, string was)
    {
        var seite = OberflaecheUmbauStartTests.Seite(haupt);
        var grund = OberflaecheUmbauStartTests.Teil<Spaltenwechsel>(seite, "Grundansicht");
        Pruefe.Wahr(grund.Nebeneinander, T($"{was}: Sprechen und Hören nebeneinander ({grund.ActualWidth:0.0})"));
        var (unten, platz) = OberflaecheStartseiteTests.BisSenden(Bildlauf.Wurzel(haupt));
        Pruefe.Wahr(unten <= platz + 0.5, T($"{was}: bis Sprechen und Hören kein Rollen ({unten:0.0} von {platz:0.0})"));
        Pruefe.Wahr(NichtsAbgeschnitten(seite, was, "NameText") >= 30, was + ": nichts abgeschnitten, genug Elemente geprüft");
        _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
    }

    private static Border Spalte(Hauptfenster haupt) => (Border)haupt.FindName("WerIstDaSpalte");

    /// <summary>Die Spalte Hören von Start, die unter der Schublade liegt.</summary>
    private static StackPanel Hoeren(Hauptfenster haupt) => OberflaecheUmbauStartTests.Teil<StackPanel>(OberflaecheUmbauStartTests.Seite(haupt), "HoerenSpalte");

    private static Rectangle Schatten(Hauptfenster haupt) => (Rectangle)haupt.FindName("WerIstDaSchatten");

    private static string Ort(Hauptfenster haupt, double skala = 1) =>
        T($"{haupt.Left * skala:0} / {haupt.Top * skala:0}, {haupt.Width:0.#} x {haupt.Height:0.#}");

    /// <summary>
    /// Full HD bei 100 %, Klein ab Werk mittig: Öffnen macht das Fenster in einem Schritt 1240 breit, die Ecke bleibt, Start ist so breit wie
    /// ohne Spalte und ganz, der Fokus bleibt, nichts wird gemerkt; Schließen bringt es in einem Schritt zurück. Bild
    /// umbau-start-900-wer-ist-da-breit. Dazu 175 % auf Sarevocs Rechner: dort ebenso, am rechten Rand nach links ausgewichen.
    /// </summary>
    [Test]
    public static void WerIstDaPlatz_Klein_BreiterNachRechts_StartGanz_ZurueckBeimSchliessen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkLage.Leer);
            var (haupt, h, funkfeld) = OberflaecheUmbauStartTests.Aufbauen(a, fenster, erststart: false, verbunden: true, out var schluessel);
            using var k = schluessel;
            var fuehrung = haupt.Platzfuehrung;
            // Klein mittig auf Full HD: (1920 - 900) / 2 = 510, (1040 - 900) / 2 = 70.
            Pruefe.Wahr(haupt.Klasse == Groessenklasse.Klein && (haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (510, 70, 900, 900), "Klein mittig: " + Ort(haupt));
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));
            var ohneSpalte = Grundbreite(haupt);
            var fokus = haupt.FokusSetzen();
            var gesetzt = fuehrung.Gesetzte;
            var einSchritt = fuehrung.InEinemSchritt;

            h.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(h.WerIstDa.Offen, "offen");
            Pruefe.Wahr((haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (510, 70, 1240, 900), "um die Spalte breiter nach rechts, die Ecke bleibt: " + Ort(haupt));
            // Gezählt werden die Aufrufe von Setzen; dass eines mit Fensterhandle ein einziges SetWindowPos ist, prüft der Quelltext-Wächter
            // (WerIstDaPlatz_KeinAktivieren_EinSetWindowPos_Quelltext), denn der Bildlauf hat keinen Handle.
            Pruefe.Gleich((gesetzt + 1, einSchritt + 1), (fuehrung.Gesetzte, fuehrung.InEinemSchritt), "ein Setzen, in einem Schritt verlangt");
            Pruefe.Wahr(haupt.Verbreitert && !haupt.Schublade, "verbreitert, keine Schublade");
            Pruefe.Wahr(fokus is not null && ReferenceEquals(fokus, FocusManager.GetFocusedElement(haupt)), "der Fokus bleibt in der Navigation");
            var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(haupt.Width, haupt.Height));
            StartGanz(haupt, "verbreitert");
            Pruefe.Gleich(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(Hoeren(haupt)), "verbreitert: Hören bleibt im Tab-Kreis");
            Pruefe.Wahr(Math.Abs(Grundbreite(haupt) - ohneSpalte) < 0.5, T($"Start so breit wie ohne die Spalte ({Grundbreite(haupt):0.0} und {ohneSpalte:0.0})"));
            Pruefe.Wahr(ZuSehen(Spalte(haupt)) && Grid.GetColumn(Spalte(haupt)) == 2 && !ZuSehen(Schatten(haupt)), "die Spalte daneben, ohne Schatten");
            Pruefe.Falsch(haupt.TitelKurz, "Titelleiste mit Namen");
            KontrastPruefen(Spalte(haupt), Farbvorschlaege.Anthrazit, "Spalte daneben", mindestens: 3);
            Bildablage.Ablegen(bild, "umbau-start-900-wer-ist-da-breit");

            h.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr((haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (510, 70, 900, 900), "zu: Breite und Lage wie vorher: " + Ort(haupt));
            Pruefe.Gleich((gesetzt + 2, einSchritt + 2), (fuehrung.Gesetzte, fuehrung.InEinemSchritt), "zurück: ein Setzen, in einem Schritt verlangt");
            Pruefe.Falsch(haupt.Verbreitert || haupt.Schublade, "nicht mehr verbreitert");
            Pruefe.Gleich(0, fuehrung.Gemerkt, "Öffnen und Schließen sind keine Handgriffe: nichts gemerkt");
            Pruefe.Falsch(a.Ablage.Aktuell.HauptfensterGroessen!.ContainsKey("start"), "keine Größe für Klein gemerkt");
            Pruefe.Gleich(null, a.Ablage.Aktuell.HauptfensterOrt, "kein Platz gemerkt");
            funkfeld.Dispose();

            // 175 % auf Sarevocs Rechner: Klein mittig auf dem Hauptmonitor (1132 / 250 Bildpunkte), 2170 Bildpunkte breit passt nach rechts.
            using var b = new Aufbau(FunkLage.Leer);
            var (test, hj, fj) = OberflaecheUmbauStartTests.Aufbauen(b, fenster, erststart: false, verbunden: true, out var schluessel2, ZweiMonitore);
            using var k2 = schluessel2;
            Pruefe.Wahr(Math.Abs((test.Left * 1.75) - 1132) < 1 && Math.Abs((test.Top * 1.75) - 250) < 1, "175 %: Klein mittig: " + Ort(test, 1.75));
            hj.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(test.Width == 1240 && Math.Abs((test.Left * 1.75) - 1132) < 1, "175 %: nach rechts breiter: " + Ort(test, 1.75));
            _ = Bildlauf.Zeichnen(test, 1.75, new Size(test.Width, test.Height));
            StartGanz(test, "175 %, verbreitert");
            hj.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(test.Width == 900 && Math.Abs((test.Left * 1.75) - 1132) < 1, "175 %: zurück: " + Ort(test, 1.75));
            // Selbst an den rechten Rand geschoben (3000 Bildpunkte): nach links ausgewichen, auf dem Hauptmonitor.
            test.Platzfuehrung.BewegungBeginnt();
            test.Left = 3000 / 1.75;
            test.Platzfuehrung.BewegungEndet();
            var gesetztJu = test.Platzfuehrung.Gesetzte;
            hj.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(test.Width == 1240 && Math.Abs((test.Left * 1.75) - (3840 - 2170)) < 1, "175 %: am Rand nach links, nicht auf den zweiten Monitor: " + Ort(test, 1.75));
            Pruefe.Gleich(gesetztJu + 1, test.Platzfuehrung.Gesetzte, "175 %: ein Setzen");
            hj.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(test.Width == 900 && Math.Abs((test.Left * 1.75) - 3000) < 1, "175 %: zurück an deine Stelle: " + Ort(test, 1.75));
            // Befund der Prüfung: Steht Klein überwiegend auf dem zweiten Monitor (Ecke bei 3300, 1035 von 1575 Bildpunkten drüben), wird es dort
            // nach rechts breiter; bis dahin sprang es 1630 Bildpunkte nach links ganz auf den Hauptmonitor.
            test.Platzfuehrung.BewegungBeginnt();
            test.Left = 3300 / 1.75;
            test.Platzfuehrung.BewegungEndet();
            hj.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(test.Width == 1240 && Math.Abs((test.Left * 1.75) - 3300) < 1 && test.Platzfuehrung.Gesetzt?.Bildschirm == TestZweiter,
                "175 %: überwiegend auf dem zweiten Monitor, dort nach rechts: " + Ort(test, 1.75));
            hj.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(test.Width == 900 && Math.Abs((test.Left * 1.75) - 3300) < 1, "175 %: zurück: " + Ort(test, 1.75));
            fj.Dispose();

            // Gemischte Skala im Ablauf: links ein WQHD bei 150 %, rechts Full HD bei 100 % (Hauptmonitor). Klein steht mit der Ecke auf dem WQHD,
            // 700 von 900 Einheiten auf Full HD, hat also dessen 100 %: rechts breiter, auf Full HD, und zurück.
            using var c = new Aufbau(FunkLage.Leer);
            var (gm, hg, fg) = OberflaecheUmbauStartTests.Aufbauen(c, fenster, erststart: false, verbunden: true, out var schluessel3, new FesteBildschirme([FullHd, LinksWqhd]));
            using var k3 = schluessel3;
            gm.Platzfuehrung.BewegungBeginnt();
            gm.Left = -200;
            gm.Platzfuehrung.BewegungEndet();
            hg.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr((gm.Left, gm.Width) == (-200, 1240) && gm.Platzfuehrung.Gesetzt?.Bildschirm == FullHd && gm.Verbreitert,
                "gemischt: Ecke auf dem WQHD, Fenster auf Full HD: dort nach rechts, kein Sprung: " + Ort(gm));
            hg.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr((gm.Left, gm.Width) == (-200, 900) && !gm.Verbreitert, "gemischt: zurück an deine Stelle: " + Ort(gm));
            fg.Dispose();
        });
    }

    /// <summary>
    /// Am rechten Rand (Full HD, 125 %): Das Fenster weicht nach links aus, ohne den Monitor zu verlassen, und kommt beim Schließen an seine Stelle
    /// zurück. Wechselt der Bereich mit offener Spalte, gilt wie beim Schieben eines Klassenwechsels die Ecke davor; zurück auf Start kommt Klein
    /// gleich verbreitert, jeweils in einem Schritt.
    /// </summary>
    [Test]
    public static void WerIstDaPlatz_Klein_WeichtNachLinksAus_BereichswechselUndZurueck()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer);
            var (haupt, h, funkfeld) = OberflaecheUmbauStartTests.Aufbauen(a, fenster, erststart: false, verbunden: true, out var schluessel);
            using var k = schluessel;
            var fuehrung = haupt.Platzfuehrung;
            fuehrung.BewegungBeginnt();
            (haupt.Left, haupt.Top) = (1020, 140);
            fuehrung.BewegungEndet();
            var gesetzt = fuehrung.Gesetzte;
            h.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr((haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (680, 140, 1240, 900), "nach links ausgewichen, bis an den rechten Rand: " + Ort(haupt));
            Pruefe.Gleich(gesetzt + 1, fuehrung.Gesetzte, "eine Größenänderung");
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(haupt.Width, haupt.Height));
            StartGanz(haupt, "ausgewichen");

            // Einstellungen mit offener Spalte: die Größe der Seiten von der Ecke vor dem Ausweichen aus (960 passt dort nicht ganz, also 960 / 140).
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            Pruefe.Wahr((haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (960, 140, 960, 700), "Einstellungen: " + Ort(haupt));
            Pruefe.Wahr(!haupt.Verbreitert && !haupt.Schublade && Grid.GetColumn(Spalte(haupt)) == 2, "Einstellungen: wie bisher daneben");
            Pruefe.Gleich(gesetzt + 2, fuehrung.Gesetzte, "eine Größenänderung");
            // Zurück auf Start: Klein an der alten Ecke und gleich verbreitert, in einem Schritt.
            h.AktiverBereich = h.Bereiche[0];
            Pruefe.Wahr((haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (680, 140, 1240, 900) && haupt.Verbreitert, "Start: gleich verbreitert: " + Ort(haupt));
            Pruefe.Gleich(gesetzt + 3, fuehrung.Gesetzte, "eine Größenänderung, nicht zwei");
            h.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr((haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (1020, 140, 900, 900), "zu: wieder an deiner Stelle: " + Ort(haupt));
            Pruefe.Gleich(gesetzt + 4, fuehrung.Gesetzte, "eine Größenänderung");
            funkfeld.Dispose();

            // 125 %: Klein ist 900 x 825,6 (Arbeitsbereich), mittig bei 397 Bildpunkten; 1550 Bildpunkte passen erst ab 370.
            using var b = new Aufbau(FunkLage.Leer);
            var (f125, h125, ff125) = OberflaecheUmbauStartTests.Aufbauen(b, fenster, erststart: false, verbunden: true, out var schluessel2, new FesteBildschirme([FullHd125]));
            using var k2 = schluessel2;
            Pruefe.Wahr(Math.Abs((f125.Left * 1.25) - 397) < 1 && Math.Abs(f125.Height - 825.6) < 0.01, "125 %: Klein mittig: " + Ort(f125, 1.25));
            h125.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(f125.Width == 1240 && Math.Abs((f125.Left * 1.25) - 370) < 1 && Math.Abs(f125.Height - 825.6) < 0.01, "125 %: nach links ausgewichen: " + Ort(f125, 1.25));
            h125.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(f125.Width == 900 && Math.Abs((f125.Left * 1.25) - 397) < 1, "125 %: zurück: " + Ort(f125, 1.25));
            ff125.Dispose();
        });
    }

    /// <summary>
    /// Kein Platz (Full HD hochkant, 1080 breit, und 4K hochkant bei 175 %): Das Fenster bleibt, und die Spalte liegt als Schublade rechts über
    /// Start, mit Schatten an ihrer linken Kante, oben in der Zeichnung; Start behält seine Breite, ohne Rollbalken und ohne Abgeschnittenes.
    /// Wird das Fenster breiter als 1240 gezogen, steht sie wieder daneben. Das Bild der Schublade zeichnet
    /// <see cref="OberflaecheUmbauStartTests.Umbau_Start_ZweiSpaltenUndGestapelt"/> (umbau-start-900-wer-ist-da-schublade).
    /// </summary>
    [Test]
    public static void WerIstDaPlatz_KeinPlatz_SchubladeUeberStart()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer);
            var (haupt, h, funkfeld) = OberflaecheUmbauStartTests.Aufbauen(a, fenster, erststart: false, verbunden: true, out var schluessel, new FesteBildschirme([Hochkant]));
            using var k = schluessel;
            var fuehrung = haupt.Platzfuehrung;
            Pruefe.Wahr((haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (90, 490, 900, 900), "Klein mittig hochkant: " + Ort(haupt));
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));
            var ohneSpalte = Grundbreite(haupt);
            var gesetzt = fuehrung.Gesetzte;
            // Befund der Prüfung: Der Fokus liegt vor Strg+6 auf der Funk-Lautstärke, die gleich unter der Schublade liegt (im Bildlauf der logische
            // Fokus; der Tastaturfokus braucht ein Fensterhandle).
            var regler = OberflaecheUmbauStartTests.Teil<Slider>(OberflaecheUmbauStartTests.Seite(haupt), "FunkLautstaerke");
            FocusManager.SetFocusedElement(haupt, regler);
            Pruefe.Gleich(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(Hoeren(haupt)), "zu: Hören im Tab-Kreis");

            h.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr((haupt.Left, haupt.Top, haupt.Width, haupt.Height) == (90, 490, 900, 900), "kein Platz: das Fenster bleibt: " + Ort(haupt));
            Pruefe.Gleich(gesetzt, fuehrung.Gesetzte, "keine Größenänderung");
            Pruefe.Wahr(haupt.Schublade && !haupt.Verbreitert, "Schublade");
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));
            StartGanz(haupt, "Schublade");
            // Unter der Schublade ist Hören aus Tab-Kreis und Pfeilnavigation genommen, Sprechen daneben nicht; der Fokus ist in die Schublade gewandert.
            var hoeren = Hoeren(haupt);
            Pruefe.Wahr(KeyboardNavigation.GetTabNavigation(hoeren) == KeyboardNavigationMode.None && KeyboardNavigation.GetDirectionalNavigation(hoeren) == KeyboardNavigationMode.None
                && KeyboardNavigation.GetControlTabNavigation(hoeren) == KeyboardNavigationMode.None, "Schublade: Hören nicht mehr mit der Tastatur erreichbar");
            var sprechen = OberflaecheUmbauStartTests.Teil<StackPanel>(OberflaecheUmbauStartTests.Seite(haupt), "SprechenSpalte");
            Pruefe.Gleich(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(sprechen), "Schublade: Sprechen bleibt im Tab-Kreis");
            var fokusJetzt = FocusManager.GetFocusedElement(haupt);
            Pruefe.Wahr(fokusJetzt is TextBox { Name: "WerIstDaSuche" } suche && In(suche, Spalte(haupt)),
                "der Fokus ist von der verdeckten Funk-Lautstärke ins Suchfeld der Schublade gewandert: " + (fokusJetzt?.GetType().Name ?? "nichts"));
            Pruefe.Wahr(Math.Abs(Grundbreite(haupt) - ohneSpalte) < 0.5, T($"Start behält seine Breite ({Grundbreite(haupt):0.0} und {ohneSpalte:0.0})"));
            var spalte = Spalte(haupt);
            var schatten = Schatten(haupt);
            var wurzel = Bildlauf.Wurzel(haupt);
            var raster = (Grid)VisualTreeHelper.GetParent(spalte);
            Pruefe.Wahr(ZuSehen(spalte) && ZuSehen(schatten) && Grid.GetColumn(spalte) == 1 && raster.ColumnDefinitions[2].ActualWidth == 0,
                "die Spalte liegt über dem Inhalt, Spalte 2 ist leer, der Schatten ist zu sehen");
            var links = spalte.TranslatePoint(new Point(0, 0), wurzel).X;
            var rechts = spalte.TranslatePoint(new Point(spalte.ActualWidth, 0), wurzel).X;
            var inhaltRechts = raster.TranslatePoint(new Point(raster.ActualWidth, 0), wurzel).X;
            Pruefe.Wahr(Math.Abs(spalte.ActualWidth - Fenstergroessen.WerIstDaBreite) < 0.5 && Math.Abs(rechts - inhaltRechts) < 0.5,
                T($"rechts bündig, 340 breit ({links:0.0} bis {rechts:0.0}, Rand {inhaltRechts:0.0})"));
            // Sprechen liegt ganz links der Schublade und bleibt deshalb mit der Tastatur bedienbar; verdeckt ist nur Hören.
            var sprechenRechts = sprechen.TranslatePoint(new Point(sprechen.ActualWidth, 0), wurzel).X;
            var hoerenLinks = hoeren.TranslatePoint(new Point(0, 0), wurzel).X;
            Console.WriteLine(T($"MESSUNG Schublade Klein 900: Sprechen endet bei {sprechenRechts:0.0}, Hören beginnt bei {hoerenLinks:0.0}, die Schublade bei {links:0.0}"));
            Pruefe.Wahr(sprechenRechts < links, T($"Sprechen endet links der Schublade ({sprechenRechts:0.0}, Schublade ab {links:0.0})"));
            Pruefe.Wahr(Math.Abs(schatten.TranslatePoint(new Point(schatten.ActualWidth, 0), wurzel).X - links) < 0.5 && !schatten.IsHitTestVisible,
                "der Schatten an der linken Kante, Klicks gehen hindurch");
            // Oben in der Zeichnung: Ein Treffer mitten in der Schublade landet in ihr, nicht in Start darunter.
            var punkt = spalte.TranslatePoint(new Point(spalte.ActualWidth / 2, spalte.ActualHeight / 2), wurzel);
            var treffer = Treffer(wurzel, punkt);
            Pruefe.Wahr(In(treffer, spalte), T($"die Schublade liegt oben ({punkt.X:0} / {punkt.Y:0}, getroffen {Kette(treffer)})"));
            KontrastPruefen(spalte, Farbvorschlaege.Anthrazit, "Schublade", mindestens: 3);

            // Breiter gezogen: ab 1240 wieder daneben, darunter wieder Schublade (Start nie schmaler als in Klein).
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1300, 900));
            Pruefe.Wahr(!haupt.Schublade && Grid.GetColumn(spalte) == 2 && !ZuSehen(schatten), "1300: daneben");
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 900));
            Pruefe.Wahr(haupt.Schublade && Grid.GetColumn(spalte) == 1, "1100: wieder Schublade");
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));

            h.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(!haupt.Schublade && Grid.GetColumn(spalte) == 2 && schatten.Visibility == Visibility.Collapsed, "zu: keine Schublade, kein Schatten");
            Pruefe.Wahr((haupt.Width, haupt.Height) == (900, 900) && fuehrung.Gesetzte == gesetzt, "zu: das Fenster blieb");
            // Gegenprobe: Zu trifft dieselbe Stelle Start.
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));
            treffer = Treffer(wurzel, punkt);
            Pruefe.Wahr(!In(treffer, spalte) && In(treffer, OberflaecheUmbauStartTests.Seite(haupt)), "zu: dieselbe Stelle trifft Start, getroffen " + Kette(treffer));
            Pruefe.Gleich(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(Hoeren(haupt)), "zu: Hören wieder im Tab-Kreis");

            // Maximiert geöffnet, ohne Platz für Klein mit Spalte: nichts verbreitert; wiederhergestellt liegt sie als Schublade über Start (Q93 a).
            haupt.WindowState = WindowState.Maximized;
            haupt.ZustandAnwenden();
            h.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(!haupt.Verbreitert && !haupt.Schublade && haupt.Width == 900 && fuehrung.Gesetzte == gesetzt, "maximiert hochkant: nichts verbreitert, daneben");
            haupt.WindowState = WindowState.Normal;
            haupt.ZustandAnwenden();
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));
            Pruefe.Wahr(haupt.Schublade && Grid.GetColumn(spalte) == 1, "wiederhergestellt hochkant: Schublade");
            h.WerIstDa.UmschaltenCommand.Execute(null);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));
            funkfeld.Dispose();

            // 175 % auf 4K hochkant: 1234 Einheiten breit, also Schublade; Start ganz auch bei 175 %.
            using var b = new Aufbau(FunkLage.Leer);
            var (f175, h175, ff175) = OberflaecheUmbauStartTests.Aufbauen(b, fenster, erststart: false, verbunden: true, out var schluessel2, new FesteBildschirme([Hochkant175]));
            using var k2 = schluessel2;
            h175.WerIstDa.UmschaltenCommand.Execute(null);
            Pruefe.Wahr(f175.Schublade && f175.Width == 900 && !f175.Verbreitert, "175 % hochkant: Schublade: " + Ort(f175, 1.75));
            _ = Bildlauf.Zeichnen(f175, 1.75, new Size(900, 900));
            StartGanz(f175, "175 %, Schublade");
            ff175.Dispose();
        });
    }

    /// <summary>
    /// Was die Maus an <paramref name="punkt"/> träfe: das oberste Sichtbare, das Klicks annimmt. Wie die Eingabe übergeht der Treffer Verborgenes
    /// (die Popups über allem) und was Klicks durchlässt; ohne Fensterhandle tut <see cref="VisualTreeHelper.HitTest(Visual, Point)"/> das nicht
    /// von selbst.
    /// </summary>
    private static DependencyObject? Treffer(Visual wurzel, Point punkt)
    {
        DependencyObject? treffer = null;
        VisualTreeHelper.HitTest(wurzel,
            d => d is UIElement { Visibility: not Visibility.Visible } or UIElement { IsHitTestVisible: false }
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren
                : HitTestFilterBehavior.Continue,
            r =>
            {
                treffer = r.VisualHit;
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(punkt));
        return treffer;
    }

    private static bool In(DependencyObject? d, DependencyObject ziel)
    {
        for (; d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ziel)) return true;
        return false;
    }

    /// <summary>Typ und Name des Treffers und seiner Vorfahren, für die Meldung.</summary>
    private static string Kette(DependencyObject? d)
    {
        var teile = new List<string>();
        for (; d is not null && teile.Count < 8; d = VisualTreeHelper.GetParent(d))
            teile.Add(d.GetType().Name + (d is FrameworkElement { Name.Length: > 0 } f ? " " + f.Name : ""));
        return teile.Count == 0 ? "nichts" : string.Join(" < ", teile);
    }

    /// <summary>
    /// Was du selbst tust, gilt (Full HD): Verschoben oder breiter gezogen geht das Fenster beim Schließen nicht zurück; gemerkt wird dabei die
    /// Größe ohne die Spalte, der nächste Start beginnt also nicht breit. Maximieren und Wiederherstellen ändern nichts am Platz, dann geht es
    /// zurück. Maximiert steht die Spalte wie bisher daneben; geöffnet oder geschlossen wird dabei der Platz des Normalzustands verbreitert oder
    /// zurückgesetzt, das Wiederherstellen bringt ihn in einem Schritt (Befunde der Prüfung). Breiter gezogen und auf den übrigen Seiten wie
    /// bisher: daneben, nichts verbreitert.
    /// </summary>
    [Test]
    public static void WerIstDaPlatz_Nutzeraenderung_KeinZurueck_GemerktOhneSpalte_MaximiertWieBisher()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer);
            var (haupt, h, funkfeld) = OberflaecheUmbauStartTests.Aufbauen(a, fenster, erststart: false, verbunden: true, out var schluessel);
            using var k = schluessel;
            var fuehrung = haupt.Platzfuehrung;
            var groessen = a.Ablage.Aktuell.HauptfensterGroessen!;
            void Umschalten() => h.WerIstDa.UmschaltenCommand.Execute(null);
            void KleinNeu()
            {
                // Über Einstellungen zurück auf Start: Klein in seiner gemerkten Größe.
                h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
                h.AktiverBereich = h.Bereiche[0];
            }

            // Verschoben: kein Zurück; gemerkt deine Lage mit der Breite ohne die Spalte, keine Größe für Klein.
            Umschalten();
            Pruefe.Wahr(haupt.Width == 1240, "verbreitert");
            fuehrung.BewegungBeginnt();
            haupt.Left = 400;
            fuehrung.BewegungEndet();
            Pruefe.Gleich(new FensterOrt(400, 70, 900, 900), a.Ablage.Aktuell.HauptfensterOrt, "verschoben: gemerkt ohne die Spalte");
            Pruefe.Falsch(groessen.ContainsKey("start"), "verschoben: keine Größe für Klein");
            var gesetzt = fuehrung.Gesetzte;
            Umschalten();
            Pruefe.Wahr((haupt.Left, haupt.Width) == (400, 1240) && fuehrung.Gesetzte == gesetzt, "zu nach dem Verschieben: bleibt, wie du es hast: " + Ort(haupt));

            // Maximiert und wiederhergestellt: der Platz ist derselbe, also zurück.
            KleinNeu();
            Pruefe.Wahr((haupt.Left, haupt.Width) == (400, 900), "Klein wieder 900: " + Ort(haupt));
            Umschalten();
            Pruefe.Wahr(haupt.Width == 1240, "verbreitert");
            haupt.WindowState = WindowState.Maximized;
            haupt.ZustandAnwenden();
            Pruefe.Falsch(haupt.Schublade, "maximiert: keine Schublade");
            haupt.WindowState = WindowState.Normal;
            haupt.ZustandAnwenden();
            Umschalten();
            Pruefe.Wahr((haupt.Left, haupt.Width) == (400, 900), "nach Maximieren und Wiederherstellen zurück: " + Ort(haupt));
            // Maximiert geschlossen (Befund der Prüfung): Der Platz des Normalzustands geht zurück, ohne dass man etwas sieht; das Fenster bleibt
            // maximiert. Wiederherstellen bringt Klein 900 an der alten Stelle in einem Schritt. Bis dahin blieb das Fenster danach 1240 breit, und
            // weil das als "daneben" galt, kam es nie mehr auf 900 zurück. Im Bildlauf stehen Left und Width auch für den Normalzustand; im echten
            // Fenster schreibt WPF sie maximiert in die Wiederherstellungsgrenzen (SetWindowPlacement).
            Umschalten();
            Pruefe.Wahr(haupt.Width == 1240 && haupt.Verbreitert, "verbreitert");
            haupt.WindowState = WindowState.Maximized;
            haupt.ZustandAnwenden();
            gesetzt = fuehrung.Gesetzte;
            var einSchritt = fuehrung.InEinemSchritt;
            Umschalten();
            Pruefe.Wahr(haupt.WindowState == WindowState.Maximized && (haupt.Left, haupt.Width) == (400, 900) && !haupt.Verbreitert,
                "maximiert geschlossen: bleibt maximiert, der Normalzustand wieder 900 an der alten Stelle: " + Ort(haupt));
            Pruefe.Gleich((gesetzt + 1, einSchritt + 1), (fuehrung.Gesetzte, fuehrung.InEinemSchritt), "maximiert geschlossen: ein Setzen in den Normalzustand");
            haupt.WindowState = WindowState.Normal;
            haupt.ZustandAnwenden();
            Pruefe.Wahr((haupt.Left, haupt.Width) == (400, 900) && fuehrung.Gesetzte == gesetzt + 1, "wiederhergestellt: Klein 900 an der alten Stelle: " + Ort(haupt));

            // Maximiert geöffnet (Befund der Prüfung): Die Spalte steht daneben wie bisher, und der Platz des Normalzustands wird verbreitert, ohne
            // dass man etwas sieht. Wiederherstellen bringt Klein 1240 mit der Spalte daneben in einem Schritt, Start ganz; bis dahin kam Klein 900
            // mit der Schublade über Hören, obwohl rechts Platz war.
            haupt.WindowState = WindowState.Maximized;
            haupt.ZustandAnwenden();
            gesetzt = fuehrung.Gesetzte;
            Umschalten();
            Pruefe.Wahr(haupt.WindowState == WindowState.Maximized && haupt.Verbreitert && !haupt.Schublade && (haupt.Left, haupt.Width) == (400, 1240),
                "maximiert geöffnet: daneben, der Normalzustand 1240: " + Ort(haupt));
            Pruefe.Gleich(gesetzt + 1, fuehrung.Gesetzte, "maximiert geöffnet: ein Setzen in den Normalzustand");
            // Maximiert gezeichnet (Full HD, 1920 x 1040); danach Width wieder als Normalzustand, wie WPF ihn in RestoreBounds hält.
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1040));
            Pruefe.Wahr(!haupt.Schublade && Grid.GetColumn(Spalte(haupt)) == 2 && ZuSehen(Spalte(haupt)), "maximiert: die Spalte daneben wie bisher");
            (haupt.Width, haupt.Height) = (1240, 900);
            haupt.WindowState = WindowState.Normal;
            haupt.ZustandAnwenden();
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(haupt.Width, haupt.Height));
            Pruefe.Wahr(!haupt.Schublade && haupt.Verbreitert && (haupt.Left, haupt.Width) == (400, 1240) && fuehrung.Gesetzte == gesetzt + 1,
                "wiederhergestellt: Klein 1240, die Spalte daneben, keine Schublade, kein weiteres Setzen: " + Ort(haupt));
            StartGanz(haupt, "maximiert geöffnet, wiederhergestellt");
            Umschalten();
            Pruefe.Wahr((haupt.Left, haupt.Width) == (400, 900) && !haupt.Verbreitert, "zu: zurück auf Klein 900: " + Ort(haupt));
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(haupt.Width, haupt.Height));

            // Breiter gezogen (Spalte zu): Ab 1240 steht die Spalte daneben, Start bleibt ganz, nichts wird verbreitert. Im echten Fenster ordnet
            // WPF nach dem Ziehen neu an; hier übernimmt das der Bildlauf.
            fuehrung.BewegungBeginnt();
            haupt.Width = 1300;
            fuehrung.BewegungEndet();
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1300, 900));
            gesetzt = fuehrung.Gesetzte;
            Umschalten();
            Pruefe.Wahr(haupt.Width == 1300 && fuehrung.Gesetzte == gesetzt && !haupt.Verbreitert && !haupt.Schublade, "1300: daneben, nichts verbreitert");
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1300, 900));
            StartGanz(haupt, "1300 mit Spalte daneben");
            Umschalten();
            // Auf den übrigen Seiten wie bisher.
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            gesetzt = fuehrung.Gesetzte;
            Umschalten();
            Pruefe.Wahr(fuehrung.Gesetzte == gesetzt && !haupt.Verbreitert && !haupt.Schublade, "Einstellungen: nichts verbreitert");
            Umschalten();
            h.AktiverBereich = h.Bereiche[0];

            // Breiter gezogen, während die Spalte offen ist: gemerkt ohne die Spalte; zu bleibt das Fenster, wie du es gezogen hast.
            fuehrung.BewegungBeginnt();
            haupt.Width = 900;
            fuehrung.BewegungEndet();
            Umschalten();
            Pruefe.Wahr(haupt.Width == 1240 && haupt.Verbreitert, "verbreitert: " + Ort(haupt));
            fuehrung.BewegungBeginnt();
            haupt.Width = 1300;
            fuehrung.BewegungEndet();
            Pruefe.Gleich(new Fenstergroesse(960, 900), groessen["start"], "gezogen mit Spalte: Klein ohne die Spalte gemerkt");
            Pruefe.Wahr(a.Ablage.Aktuell.HauptfensterOrt is { Breite: 960, Hoehe: 900 }, "HauptfensterOrt ohne die Spalte");
            gesetzt = fuehrung.Gesetzte;
            Umschalten();
            Pruefe.Wahr(haupt.Width == 1300 && fuehrung.Gesetzte == gesetzt, "zu nach dem Ziehen: bleibt, wie du es hast");
            a.Ablage.Sichern();
            Pruefe.Gleich(new Fenstergroesse(960, 900), a.Speicher.Laden().HauptfensterGroessen!["start"], "in der Datei ohne die Spalte");
            funkfeld.Dispose();

            // Der nächste Start beginnt nicht breit.
            var zweit = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(zweit);
            Pruefe.Wahr(zweit.Klasse == Groessenklasse.Klein && (zweit.Width, zweit.Height) == (960, 900), "nächster Start: 960 x 900, nicht 1300: " + Ort(zweit));
        });
    }

    /// <summary>Die Methoden von Q93 a in Hauptfenster.xaml.cs, in denen weder aktiviert noch fokussiert noch der Zustand gewechselt wird.</summary>
    private static readonly string[] Q93Methoden = ["WerIstDaGeaendert", "SpalteOeffnen", "Verbreitern", "SpalteSchliessen", "SpalteAnordnen", "GroesseAnwenden", "ZustandAnwenden"];

    /// <summary>
    /// Kein Aktivieren, kein Fokusraub, ein Schritt (Q93 a, Befunde der Prüfung). Der Bildlauf hat kein Fensterhandle: Ein Activate() bliebe dort
    /// wirkungslos, und ob ein Setzen ein SetWindowPos ist oder vier, sieht er nicht; die Tests oben zählen nur die Aufrufe von Setzen. Darum liest
    /// dieser Wächter den Quelltext. In den Methoden von Q93 a (<see cref="Q93Methoden"/>) steht kein Activate, kein Focus() und kein Wechsel von
    /// WindowState; Hauptfenster.xaml.cs ruft nirgends Activate(). Focus() steht in FokusAusVerdecktem einmal und nur hinter "if (tastatur)":
    /// Dort lag der Tastaturfokus schon im Fenster. Verbreitern und Zurück setzen in einem Schritt; Platzfuehrung setzt diesen vor jeder
    /// Zuweisung an Left oder Width mit genau einem SetWindowPos ohne Aktivieren und ohne Z-Reihenfolge. Am echten Fenster ist das nicht gemessen
    /// (Bildschirm tabu). Gegenprobe: Ein eingefügtes Activate(), ein Focus() ohne Bedingung und ein Setzen ohne einSchritt fielen auf.
    /// </summary>
    [Test]
    public static void WerIstDaPlatz_KeinAktivieren_EinSetWindowPos_Quelltext()
    {
        var haupt = File.ReadAllText(Quelle(@"src\Funk.Client\Fenster\Hauptfenster.xaml.cs")).ReplaceLineEndings("\n");
        var fuehrung = File.ReadAllText(Quelle(@"src\Funk.Client\Fenster\Platzfuehrung.cs")).ReplaceLineEndings("\n");
        var verstoesse = Verstoesse(haupt);
        Pruefe.Gleich(0, verstoesse.Count, "Hauptfenster: " + string.Join("; ", verstoesse));

        // Gegenproben am veränderten Text.
        static string Einfuegen(string code, string methode, string zeile) =>
            Regex.Replace(code, @"(?m)^(    private void " + methode + @"\(\)\n    \{\n)", "$1        " + zeile + "\n");
        Pruefe.Wahr(Verstoesse(Einfuegen(haupt, "SpalteOeffnen", "Activate();")).Any(v => v.Contains("SpalteOeffnen: Activate(", StringComparison.Ordinal)),
            "Gegenprobe: Activate() in SpalteOeffnen fiele auf");
        Pruefe.Wahr(Verstoesse(Einfuegen(haupt, "FokusAusVerdecktem", "WerIstDaKnopf.Focus();")).Any(v => v.StartsWith("FokusAusVerdecktem", StringComparison.Ordinal)),
            "Gegenprobe: Focus() ohne Bedingung fiele auf");
        Pruefe.Wahr(Verstoesse(haupt.Replace("Platzfuehrung.Setzen(breiter, einSchritt: true)", "Platzfuehrung.Setzen(breiter)", StringComparison.Ordinal))
            .Any(v => v.StartsWith("Verbreitern", StringComparison.Ordinal)), "Gegenprobe: Verbreitern ohne einen Schritt fiele auf");

        // Platzfuehrung: ein SetWindowPos ohne Aktivieren und ohne Z-Reihenfolge, vor den Zuweisungen der Eigenschaften.
        Pruefe.Falsch(fuehrung.Contains("Activate(", StringComparison.Ordinal), "Platzfuehrung ruft kein Activate()");
        var umsetzen = Methode(fuehrung, "Umsetzen") ?? throw new PruefungException("Platzfuehrung.Umsetzen fehlt");
        Pruefe.Gleich(1, Regex.Count(umsetzen, @"\bSetWindowPos\("), "Umsetzen: genau ein SetWindowPos");
        Pruefe.Wahr(umsetzen.Contains("SwpNoactivate", StringComparison.Ordinal) && umsetzen.Contains("SwpNozorder", StringComparison.Ordinal)
            && umsetzen.Contains("SwpNoownerzorder", StringComparison.Ordinal), "Umsetzen: ohne Aktivieren und ohne Z-Reihenfolge");
        Pruefe.Wahr(umsetzen.IndexOf("SetWindowPos(", StringComparison.Ordinal) < umsetzen.IndexOf("_fenster.Left =", StringComparison.Ordinal),
            "Umsetzen: erst das SetWindowPos, dann (nur bei Abweichung) die Eigenschaften");
        Pruefe.Wahr(fuehrung.Contains("private const uint SwpNoactivate = 0x0010;", StringComparison.Ordinal)
            && fuehrung.Contains("private const uint SwpNozorder = 0x0004;", StringComparison.Ordinal), "die Werte der Schalter");
        var setzen = Methode(fuehrung, "Setzen") ?? throw new PruefungException("Platzfuehrung.Setzen fehlt");
        var umsetzenAufruf = setzen.IndexOf("Umsetzen(griff, platz, s)", StringComparison.Ordinal);
        Pruefe.Wahr(umsetzenAufruf >= 0 && umsetzenAufruf < setzen.IndexOf("_fenster.Left =", StringComparison.Ordinal),
            "Setzen: in einem Schritt geht Umsetzen vor den einzelnen Zuweisungen, die nur ohne ihn kommen");
    }

    /// <summary>Was der Wächter in Hauptfenster.xaml.cs findet (leer: nichts).</summary>
    private static List<string> Verstoesse(string code)
    {
        var v = new List<string>();
        if (code.Contains("Activate(", StringComparison.Ordinal)) v.Add("Activate( im Hauptfenster");
        foreach (var name in Q93Methoden)
        {
            if (Methode(code, name) is not { } rumpf)
            {
                v.Add(name + " fehlt");
                continue;
            }
            if (rumpf.Contains("Activate(", StringComparison.Ordinal)) v.Add(name + ": Activate(");
            if (rumpf.Contains(".Focus(", StringComparison.Ordinal)) v.Add(name + ": .Focus(");
            if (Regex.IsMatch(rumpf, @"\bWindowState\s*=(?!=)")) v.Add(name + ": WindowState =");
        }
        foreach (var name in new[] { "Verbreitern", "SpalteSchliessen" })
        {
            if (Methode(code, name) is { } rumpf && !Regex.IsMatch(rumpf, @"Platzfuehrung\.Setzen\([^)]*einSchritt: true\)")) v.Add(name + ": Setzen ohne einSchritt");
        }
        if (Methode(code, "FokusAusVerdecktem") is not { } fokus) v.Add("FokusAusVerdecktem fehlt");
        else
        {
            var zeilen = fokus.Split('\n').Where(z => z.Contains(".Focus(", StringComparison.Ordinal)).ToList();
            if (zeilen.Count != 1 || !zeilen[0].TrimStart().StartsWith("if (tastatur) ", StringComparison.Ordinal))
                v.Add("FokusAusVerdecktem: Focus() nur einmal und nur mit dem Tastaturfokus im Fenster (" + zeilen.Count + ")");
            if (fokus.Contains("Activate(", StringComparison.Ordinal)) v.Add("FokusAusVerdecktem: Activate(");
        }
        return v;
    }

    /// <summary>Der Rumpf einer Methode (vier Leerzeichen eingerückt, Zeilenenden "\n"), oder <c>null</c>.</summary>
    private static string? Methode(string code, string name)
    {
        var m = Regex.Match(code, @"(?ms)^    (?:private|internal|public)[^\n]*\b" + name + @"\([^\n]*\)\n    \{\n(.*?)\n    \}\n");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Quelle(string relativ)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(System.IO.Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return System.IO.Path.Combine(d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden."), relativ);
    }
}
