// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Eingabe;
using Funk.Klang.Maschine;
using Funk.Tests.Maschine;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// 0.10.0, AP4: die Bilder der neuen Oberfläche aus der echten Ansicht (kein Entwurfs-XAML), zum Vergleich mit der abgenommenen Mischung
/// (<c>C:\Claude\tmp\tr-entwurf-m\m-1</c> bis <c>m-8</c>). Gezeichnet nur im Speicher (<see cref="Bildlauf"/>), in der Farbstellung ab Werk; mit
/// <c>FUNK_BILDER</c> entstehen die PNG 0100-1 bis 0100-7: Start verbunden bei 1100 mit SENDET und in Klein 900 x 900, der erste Start in Klein,
/// die Einstellungen zugeklappt bei 1100 und in der Größe ab Werk 960 x 700 (mit einer Linie, wie weit das Fenster reicht), Senden offen mit
/// "1 selbst gestellt: Wärme +2 dB", Empfangen offen mit den Kanälen und Personen des Beispielabends und die Statuszeile in fünf Lagen. Ohne
/// <c>FUNK_BILDER</c> prüft der Test an jedem Bild: nichts abgeschnitten, Kontrast, Glasschatten frei, Start und erster Start bei 900 ohne
/// Rollen (Q89), die Einstellungen bei 640 zu und offen ohne Überlauf. Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheUmbauBilderTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Breite der großen Bilder wie in der Vorlage m-1.</summary>
    private const double Breit = 1100;

    /// <summary>Höhe, in der die Bilder zuerst gezeichnet werden (Klein ab Werk ist 900 hoch).</summary>
    private const double Hoch = 900;

    /// <summary>Die kleinste Fensterbreite der Seiten (Fenstergroessen.SeiteMindest).</summary>
    private const double Schmal = 640;

    /// <summary>Was die Statuszeile zeigt: SENDET oder einer der vier Hinweissätze, die gelb oder grau neben dem Balken stehen (m-8).</summary>
    private enum Status
    {
        Sendet,
        Ruhe,
        NichtVerbunden,
        OhneSprechtaste,
        UnterMindest,
    }

    /// <summary>
    /// Die Lagen des Bogens 0100-7 mit Satz, Marke und Beschriftung; Reihenfolge wie auf dem Bogen m-8. Seit 0.11.2 ohne die Sätze zu Ruhe und
    /// Trennung (Sarevoc 13.10.2026: "raus heißt raus"; bis 0.11.1 "... es wird nichts gesendet"): Dort zeigt nur der Balken ohne Grün, dass nichts hinausgeht.
    /// </summary>
    private static readonly (Status Status, string? Satz, string Marke, string Beschriftung)[] Lagen =
    [
        (Status.Sendet, null, "SENDET", "Zum Vergleich: Du sprichst, und es geht hinaus. SENDET, kein Satz"),
        (Status.Ruhe, null, "Ruhe", "1  Du sprichst, der Kanal steht auf Ruhe: kein Satz, der Balken gelb"),
        (Status.NichtVerbunden, null, "Getrennt", "2  Du sprichst, bist aber nicht verbunden: Marke Getrennt, kein Satz"),
        (Status.OhneSprechtaste, "Kanal braucht die Sprechtaste - keine belegt", "Ruhe", "3  Du sprichst in einen Kanal auf Taste, eine Sprechtaste ist nicht belegt"),
        (Status.UnterMindest, "Stimme unter der Mindest-Funklautstärke", "Ruhe", "4  Du sprichst leiser als die Mindest-Funklautstärke"),
    ];

    /// <summary>
    /// Alle Bilder der neuen Oberfläche nacheinander, je mit den Prüfungen; mit <c>FUNK_BILDER</c> als PNG abgelegt. Gemessene Höhen in der
    /// Messdatei <c>umbau-bilder-mass.txt</c>.
    /// </summary>
    [Test]
    public static void Umbau_Bilder_StartEinstellungenUndStatuszeile()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            var satz = Farbvorschlaege.Von(Farbvorschlaege.Voreinstellung);
            var mass = new List<string>();
            Pruefe.Gleich(new Size(Hoch, Hoch), Fenstergroessen.KleinVoreinstellung, "Klein ab Werk 900 x 900 (Q89)");
            Pruefe.Gleich(new Size(960, 700), Fenstergroessen.SeiteVoreinstellung, "Einstellungen ab Werk 960 x 700");

            StartBilder(fenster, satz, mass);
            ErststartBild(fenster, satz, mass);
            EinstellungenBilder(fenster, satz, mass);
            StatuszeilenBogen(fenster, satz, mass);
            Messdateien.Schreiben("umbau-bilder-mass.txt", mass);
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Start
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Das Hauptfenster wie nach dem Einrichten (Profil, bestätigte Namen, freigegeben, Funk-Lautstärke 80 %), Audio läuft, globale Tasten
    /// (Attrappe), auf Full HD. <paramref name="erststart"/>: ohne Hörweise; <paramref name="sprechtaste"/>: Maustaste 4 als Sprechtaste.
    /// </summary>
    private static (Hauptfenster Haupt, HauptViewModel Modell, FunkfeldViewModel Funkfeld) Aufbauen(Aufbau a, List<Window> fenster, bool erststart,
        bool sprechtaste, out IDisposable schluessel)
    {
        schluessel = OberflaecheStartseiteTests.Einrichten(a, hoerweise: !erststart);
        a.Ablage.Aktuell.Verbindung.Freigegeben = true;
        if (sprechtaste) OberflaecheStartseiteTests.SprechtasteBelegen(a);
        a.Ablage.Aktuell.Audio.FunkLautstaerkeProzent = 80;
        a.Tastenquelle = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
        audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        h.MeinProfil.LadenAsync().GetAwaiter().GetResult();
        h.Takt();
        h.AktiverBereich = Seiten.Start(h);
        h.FensterAktiviert();
        var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
        fenster.Add(haupt);
        return (haupt, h, funkfeld);
    }

    /// <summary>Eine Meldung der Klangmaschine wie beim Sprechen; danach ein Takt, damit Statuszeile und Funkfeld sie haben.</summary>
    private static void Sprechen(Aufbau a, HauptViewModel h, float db, bool sendet, bool zurueckgehalten = false)
    {
        a.Uhr += 200;
        a.Maschine.Melden(new PegelMeldung(a.Uhr, db, -62, -45, 0.95f, sendet, Zurueckgehalten: zurueckgehalten));
        h.Takt();
    }

    /// <summary>
    /// 0100-1 und 0100-2 (m-1, m-2): Start verbunden mit dem Beispielabend (ohne Einheiten, also ohne Einsteigen), Sprechtaste belegt. Bei 1100
    /// SENDET, in Klein 900 x 900 Ruhe; beide Male "Sprechen" und "Hören" nebeneinander, kein Pegelbalken auf Start, kein Rollbalken.
    /// </summary>
    private static void StartBilder(List<Window> fenster, Farbsatz satz, List<string> mass)
    {
        using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
        var (haupt, h, funkfeld) = Aufbauen(a, fenster, erststart: false, sprechtaste: true, out var schluessel);
        using var k = schluessel;
        foreach (var (name, breite, sendet) in new[] { ("0100-1-start-1100", Breit, true), ("0100-2-start-900", Hoch, false) })
        {
            Sprechen(a, h, sendet ? -21 : -80, sendet);
            Pruefe.Gleich(sendet ? StatusMarke.Sendet : StatusMarke.Ruhe, h.Statuszeile.Marke, name + ": Marke");
            var groesse = new Size(breite, Hoch);
            var (bild, _) = Bildlauf.Zeichnen(haupt, 1, groesse);
            var seite = Seite<StartViewModel>(haupt);
            Pruefe.Wahr(Teil<Spaltenwechsel>(seite, "Grundansicht").Nebeneinander, name + ": Sprechen und Hören nebeneinander");
            // E23: erst unter 900 nur Zeichen und Zahl; in Klein ab Werk (900) mit Namen wie in der Vorlage m-7c (bis AP4 kurz: Rundung).
            Pruefe.Falsch(haupt.TitelKurz, name + ": Titelleiste mit Namen");
            TitelleisteGanz(haupt, name);
            // Seit 0.11.2 steht der Pegelbalken mit den Griffen unten auf Start unter "Senden" (Sarevoc 13.10.2026; bis 0.11.1 keiner, Q87, mit Verweis).
            Pruefe.Gleich("StartPegel", string.Join("|", Bildlauf.Nachfahren<Pegelbalken>(seite).Select(p => p.Name)), name + ": der Pegelbalken unter Senden");
            Pruefe.Wahr(Sichtbar(seite, "Maustaste 4 (zurück)") && Sichtbar(seite, "80 %") && !Sichtbar(seite, "Pegel und SENDET stehen unten in der Zeile Mikrofon."),
                name + ": Sprechtaste mit Namen, Funk-Lautstärke, kein Verweis mehr");
            KeinRollbalken(haupt, groesse, name, mass);
            Pruefen(haupt, seite, satz, name);
            Bildablage.Ablegen(bild, name);
        }
        funkfeld.Dispose();
    }

    /// <summary>
    /// 0100-3 (m-7c): der erste Start in Klein 900 x 900, beide Angaben offen, Start wartet auf die Taste; "Später" steht bei zwei schmalen
    /// Spalten unter dem Wartefeld. Kein Rollbalken.
    /// </summary>
    private static void ErststartBild(List<Window> fenster, Farbsatz satz, List<string> mass)
    {
        using var a = new Aufbau(FunkLage.Leer);
        var (haupt, h, funkfeld) = Aufbauen(a, fenster, erststart: true, sprechtaste: false, out var schluessel);
        using var k = schluessel;
        const string name = "0100-3-erststart-900";
        Pruefe.Wahr(h.Start.ErststartOffen && h.Start.SprechtasteSchrittOffen && h.Start.Sprechtaste.Wartet, name + ": beide Angaben offen, Start wartet");
        Pruefe.Gleich("Willkommen. Zwei Angaben fehlen noch, dann kommt hier Verbinden.", h.Start.ErststartSatz, name + ": Willkommenssatz");
        var groesse = new Size(Hoch, Hoch);
        var (bild, _) = Bildlauf.Zeichnen(haupt, 1, groesse);
        var seite = Seite<StartViewModel>(haupt);
        Pruefe.Wahr(Sichtbar(seite, "Drück jetzt deine Sprechtaste") && Sichtbar(seite, "Wartet auf deine Taste ..."), name + ": Schritt und Wartefeld");
        Pruefe.Falsch(Teil<Spaltenwechsel>(seite, "SchrittZeile").Nebeneinander, name + ": Später unter dem Wartefeld");
        Pruefe.Gleich(StatusMarke.Getrennt, h.Statuszeile.Marke, name + ": Statuszeile Getrennt");
        Pruefe.Falsch(haupt.TitelKurz, name + ": Titelleiste mit Namen wie in m-7c");
        TitelleisteGanz(haupt, name);
        KeinRollbalken(haupt, groesse, name, mass);
        Pruefen(haupt, seite, satz, name);
        Bildablage.Ablegen(bild, name);
        funkfeld.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Einstellungen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Seit 0.11.1 (Sarevoc 12.10.2026, Bilder 0111-1 bis 0111-3 statt 0100-4 und 0100-5): die Einstellungen zugeklappt bei 1100 und in der Größe ab
    /// Werk 960 x 700, dort mit der Linie, bis wohin das Fenster reicht (darunter rollt die Seite, Q89 erlaubt das auf Seiten); unter Senden
    /// stehen die Schalter und der Pegelbalken offen. 0111-3: Senden mit offenem Erweitert und der Wärme auf +2 dB, Kopfzeile und Satz zählen
    /// sie. 0100-6: Empfangen offen mit den Kanälen und Personen des Beispielabends. Dazu ohne Bild alles bei 640 zu und offen.
    /// </summary>
    private static void EinstellungenBilder(List<Window> fenster, Farbsatz satz, List<string> mass)
    {
        using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel());
        var (haupt, h, funkfeld) = Aufbauen(a, fenster, erststart: false, sprechtaste: true, out var schluessel);
        using var k = schluessel;
        h.AktiverBereich = Seiten.Einstellungen(h);
        h.Takt();

        var zu = Ganz(haupt, Breit);
        var seite = Seite<EinstellungenViewModel>(haupt);
        Pruefe.Falsch(Bildlauf.Nachfahren<Expander>(seite).Any(x => x.IsExpanded), "0111-1: alle Klappbereiche zu");
        Pruefe.Wahr(Sichtbar(seite, EinstellungenViewModel.AutomatikSenden) && Sichtbar(seite, EinstellungenViewModel.SatzFehlersuche), "0111-1: Kästen und Satz");
        foreach (var name in new[] { "Echounterdrückung automatisch", "Echoschutz", "Rauschunterdrückung", "Automatische Lautstärke", "Sprachaktivierung",
                     "Mindest-Funklautstärke", "Eingangspegel", "Mindestlautstärke einmessen", "Sprachaktivierung genau einstellen" })
            Pruefe.Wahr(Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => System.Windows.Automation.AutomationProperties.GetName(e) == name && ZuSehen(e)),
                "0111-1: unter Senden ohne Aufklappen zu sehen: " + name);
        mass.Add(T($"MASS 0111-1 zu bei 1100: Bild {zu.PixelHeight} hoch"));
        Pruefen(haupt, seite, satz, "0111-1 zu, 1100");
        Bildablage.Ablegen(zu, "0111-1-einstellungen-zu-1100");
        var ab960 = MitLinie(haupt, 960, 700, satz, mass, "0111-2 zu bei 960 x 700");
        Pruefen(haupt, Seite<EinstellungenViewModel>(haupt), satz, "0111-2 zu, 960");
        Bildablage.Ablegen(ab960, "0111-2-einstellungen-zu-960");

        // Senden mit offenem Erweitert, eine Sache selbst gestellt (m-5).
        var senden = h.Einstellungen.AbschnittSenden!.Feinheiten;
        h.Audio.Klangfarbe.Waerme = 2;
        Pruefe.Gleich("1 selbst gestellt: Wärme +2 dB. Alles andere steht ab Werk.", senden.StandSatz, "0111-3: Satz neben Zurück");
        Pruefe.Wahr(senden.Kopfzeile.StartsWith("Erweitert: (1 selbst gestellt) ", StringComparison.Ordinal), "0111-3: Kopfzeile " + senden.Kopfzeile);
        Seiten.Feinheiten(h, Abschnittskennung.Senden);
        var ganz = Ganz(haupt, Breit);
        seite = Seite<EinstellungenViewModel>(haupt);
        Pruefe.Wahr(Sichtbar(seite, senden.Kopfrest) && Sichtbar(seite, senden.StandSatz) && Sichtbar(seite, "Zurück auf Werkseinstellung"), "0111-3: Kopfzeile, Satz und Knopf zu sehen");
        Pruefe.Wahr(Teil<Spaltenwechsel>(seite, "SendenSpalten").Nebeneinander && Teil<Spaltenwechsel>(seite, "SendenErweitertSpalten").Nebeneinander,
            "0111-3: Senden und Erweitert je in zwei Spalten");
        // Das Bild zeigt den Abschnitt Senden über die Breite der Karte, von seiner Linie bis unter den Fuß von Erweitert.
        var wurzel = Bildlauf.Wurzel(haupt);
        var karte = Teil<Border>(seite, "AbschnitteKarte").TransformToAncestor(wurzel).TransformBounds(new Rect(Teil<Border>(seite, "AbschnitteKarte").RenderSize));
        var abschnitt = Teil<StackPanel>(seite, "AbschnittSenden");
        var r = abschnitt.TransformToAncestor(wurzel).TransformBounds(new Rect(abschnitt.RenderSize));
        var links = (int)Math.Max(0, Math.Floor(karte.Left - 8));
        var oben = (int)Math.Max(0, Math.Floor(r.Top - 14));
        var offen = new CroppedBitmap(ganz, new Int32Rect(links, oben, (int)Math.Min(ganz.PixelWidth - links, Math.Ceiling(karte.Width + 16)),
            (int)Math.Min(ganz.PixelHeight - oben, Math.Ceiling(r.Height + 28))));
        mass.Add(T($"MASS 0111-3 Senden mit offenem Erweitert bei 1100: Ausschnitt {offen.PixelWidth} x {offen.PixelHeight}, Seite {ganz.PixelHeight} hoch"));
        Pruefen(haupt, seite, satz, "0111-3 Senden mit offenem Erweitert");
        Bildablage.Ablegen(offen, "0111-3-senden-erweitert-offen");
        senden.Offen = false;

        // Empfangen offen mit dem Beispielabend: Kanäle mit Ch., Global, Personen.
        Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
        var empfangen = Ganz(haupt, Breit);
        seite = Seite<EinstellungenViewModel>(haupt);
        Pruefe.Wahr(Sichtbar(seite, "Je Kanal") && Sichtbar(seite, "Je Person") && Sichtbar(seite, "Global") && Sichtbar(seite, "Falke"),
            "0100-6: Kanäle, Global und Personen");
        mass.Add(T($"MASS 0100-6 Empfangen offen bei 1100: Bild {empfangen.PixelHeight} hoch"));
        Pruefen(haupt, seite, satz, "0100-6 Empfangen offen");
        Bildablage.Ablegen(empfangen, "0100-6-empfangen-offen");

        // Ohne Bild: die kleinste Breite, zu und mit allen Feinheiten offen.
        foreach (var a2 in h.Einstellungen.Abschnitte) a2.Feinheiten.Offen = false;
        _ = Ganz(haupt, Schmal);
        Pruefen(haupt, Seite<EinstellungenViewModel>(haupt), satz, "640 zu");
        Seiten.Feinheiten(h);
        var schmal = Ganz(haupt, Schmal);
        mass.Add(T($"MASS Einstellungen offen bei 640: Bild {schmal.PixelHeight} hoch"));
        _ = NichtsAbgeschnitten(Seite<EinstellungenViewModel>(haupt), "640 offen");
        _ = OberflaecheStartseiteTests.SchattenFrei(Seite<EinstellungenViewModel>(haupt), "640 offen");
        funkfeld.Dispose();
    }

    /// <summary>
    /// Zeichnet so hoch, dass nichts rollt, und zieht eine gestrichelte Linie, bis wohin ein Fenster mit <paramref name="hoehe"/> reicht (darunter
    /// stehen Statuszeile und Fußzeile, wie in der Vorlage m-4b).
    /// </summary>
    private static RenderTargetBitmap MitLinie(Hauptfenster haupt, double breite, double hoehe, Farbsatz satz, List<string> mass, string was)
    {
        _ = Bildlauf.Zeichnen(haupt, 1, new Size(breite, hoehe));
        var wurzel = Bildlauf.Wurzel(haupt);
        var lauf = Lauf(haupt);
        var mehr = Math.Max(0, lauf.ExtentHeight - lauf.ViewportHeight);
        var unten = hoehe - (lauf.TransformToAncestor(wurzel).Transform(new Point(0, 0)).Y + lauf.ActualHeight);
        var falz = hoehe - unten;
        mass.Add(T($"MASS {was}: Inhalt {lauf.ExtentHeight:0.0} hoch, Platz {lauf.ViewportHeight:0.0}, es fehlen {mehr:0}, ohne Rollen {hoehe + mehr:0} hoch"));
        var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(breite, Math.Ceiling(hoehe + mehr + 2)));
        var bogen = new Bogen(bild.PixelWidth, bild.PixelHeight, 1, Palette.Anthrazit);
        bogen.Bild(bild, 0, 0, bild.PixelWidth, bild.PixelHeight);
        bogen.Zeichnung(dc =>
        {
            var stift = new Pen(Bogen.Pinsel(satz.Akzent), 1.5) { DashStyle = new DashStyle([6, 4], 0) };
            stift.Freeze();
            // Nur in der Navigation links und im rechten Rand, damit die Linie keine Schrift durchstreicht.
            dc.DrawLine(stift, new Point(1, falz + 0.5), new Point(170, falz + 0.5));
            dc.DrawLine(stift, new Point(bild.PixelWidth - 22, falz + 0.5), new Point(bild.PixelWidth - 1, falz + 0.5));
        });
        bogen.Text(T($"Fenster {hoehe:0} hoch: ab hier rollen"), 12, falz - 20, 11, Palette.Text2);
        return bogen.Fertig();
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Statuszeile
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 0100-7 (m-8): die Statuszeile in fünf Lagen, je aus dem ganzen Hauptfenster in 1100 Breite ausgeschnitten und untereinander auf einem
    /// Bogen: SENDET zum Vergleich, dann Ruhe und Nicht verbunden (Marke Getrennt; seit 0.11.2 ohne Satz), die Sätze Kanal braucht die Sprechtaste
    /// und Stimme unter der Mindest-Funklautstärke. Jede Lage entsteht im Modell so, wie sie im Betrieb entsteht.
    /// </summary>
    private static void StatuszeilenBogen(List<Window> fenster, Farbsatz satz, List<string> mass)
    {
        var teile = new List<(string Text, BitmapSource Bild)>();
        foreach (var (status, satzText, marke, beschriftung) in Lagen)
        {
            var lage = status == Status.Ruhe ? FunkDienstAttrappe.KanalBeispiel() with { SendeFelder = [4], GlobalSenden = false } : FunkDienstAttrappe.KanalBeispiel();
            using var a = new Aufbau(lage);
            var (haupt, h, funkfeld) = Aufbauen(a, fenster, erststart: false, sprechtaste: status != Status.OhneSprechtaste, out var schluessel);
            using var k = schluessel;
            switch (status)
            {
                case Status.Sendet:
                    Sprechen(a, h, -21, sendet: true);
                    break;
                case Status.Ruhe:
                    Sprechen(a, h, -24, sendet: false);
                    break;
                case Status.NichtVerbunden:
                    a.Dienst.LageSetzen(_ => FunkLage.Leer);
                    Sprechen(a, h, -26, sendet: false);
                    break;
                case Status.OhneSprechtaste:
                    // Senden mit Stimme aus: Jeder Kanal sendet nur mit Taste, und es ist keine belegt.
                    h.Audio.SprachaktivierungAn = false;
                    Sprechen(a, h, -23, sendet: false);
                    break;
                case Status.UnterMindest:
                    Sprechen(a, h, -53, sendet: false, zurueckgehalten: true);
                    break;
            }
            var was = "0100-7 " + status;
            Pruefe.Gleich(satzText, h.Statuszeile.Hinweis, was + ": Satz");
            Pruefe.Gleich(marke, h.Statuszeile.MarkenText, was + ": Marke");
            var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(Breit, Hoch));
            var wurzel = Bildlauf.Wurzel(haupt);
            var zeile = (Border)haupt.FindName("Statuszeile");
            if (satzText is not null) Pruefe.Wahr(Sichtbar(zeile, satzText), was + ": Satz zu sehen");
            Pruefe.Wahr(Sichtbar(zeile, marke) && Sichtbar(zeile, "Mikrofon"), was + ": Marke und Mikrofon zu sehen");
            _ = NichtsAbgeschnitten(zeile, was);
            _ = KontrastPruefen(zeile, satz, was, mindestens: 2);
            var r = zeile.TransformToAncestor(wurzel).TransformBounds(new Rect(zeile.RenderSize));
            mass.Add(T($"MASS {was}: Statuszeile {r.Width:0} x {r.Height:0} bei y {r.Top:0}"));
            teile.Add((beschriftung, new CroppedBitmap(bild, new Int32Rect((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Width),
                (int)Math.Ceiling(r.Height)))));
            funkfeld.Dispose();
        }
        const double rand = 24, kopf = 70, schrift = 22, luecke = 18;
        var breite = teile.Max(t => t.Bild.PixelWidth);
        var hoehe = kopf + teile.Sum(t => schrift + t.Bild.PixelHeight + luecke) + rand - luecke;
        var bogen = new Bogen(breite + (2 * rand), hoehe, 1, Palette.Anthrazit);
        bogen.Text("Statuszeile \"Mikrofon\" unten, die eine Stelle für Pegel und SENDET (0.10.0, echte Ansicht)", rand, 14, 15, fett: true);
        bogen.Text("Der Satz steht nur, wenn du sprichst und nichts hinausgeht. Gelb: es hätte ausgelöst. Grau: zu leise, unter der Linie.", rand, 38, 13,
            Palette.Text2);
        var y = kopf;
        foreach (var (text, bild) in teile)
        {
            bogen.Text(text, rand, y, 13, Palette.Text2, fett: true);
            bogen.Bild(bild, rand, y + schrift, bild.PixelWidth, bild.PixelHeight);
            y += schrift + bild.PixelHeight + luecke;
        }
        Bildablage.Ablegen(bogen.Fertig(), "0100-7-statuszeile");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Prüfungen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Nichts abgeschnitten, Glasschatten frei, Kontrast auf der Seite und in der Statuszeile.</summary>
    private static void Pruefen(Hauptfenster haupt, FrameworkElement seite, Farbsatz satz, string was)
    {
        Pruefe.Wahr(NichtsAbgeschnitten(seite, was) >= 20, was + ": genug Elemente geprüft");
        _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
        _ = KontrastPruefen(seite, satz, was, mindestens: 15);
        _ = KontrastPruefen((Border)haupt.FindName("Statuszeile"), satz, was + ", Statuszeile", mindestens: 2);
    }

    /// <summary>Kein Text der Titelleiste ist beschnitten; nur "spricht: ..." darf sich kürzen (mit Auslassungspunkten).</summary>
    private static void TitelleisteGanz(Hauptfenster haupt, string was)
    {
        var titel = (Grid)haupt.FindName("TitelLeiste");
        foreach (var t in Bildlauf.Nachfahren<TextBlock>(titel).Where(ZuSehen))
            Pruefe.Wahr(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(t) is null || t.TextTrimming != TextTrimming.None,
                T($"{was}: \"{Kurz(Inhalt(t))}\" in der Titelleiste nicht abgeschnitten"));
        foreach (var k in new[] { "WerIstDaKnopf", "FunkfeldKnopf" })
        {
            var knopf = (FrameworkElement)haupt.FindName(k);
            Pruefe.Wahr(ZuSehen(knopf) && System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(knopf) is null, was + ": " + k + " ganz");
        }
    }

    /// <summary>
    /// Start passt bis Sprechen und Hören ohne Rollen in <paramref name="groesse"/> (seit 0.11.2 darf "Senden" darunter hereinrollen,
    /// <see cref="OberflaecheStartseiteTests.BisSenden"/>; bis 0.11.1 die ganze Seite ohne Rollbalken); die gebrauchte Fensterhöhe in die Messdatei.
    /// </summary>
    private static void KeinRollbalken(Hauptfenster haupt, Size groesse, string was, List<string> mass)
    {
        var lauf = Lauf(haupt);
        var (unten, platz) = OberflaecheStartseiteTests.BisSenden(Bildlauf.Wurzel(haupt));
        mass.Add(T($"MASS {was}: Inhalt {lauf.ExtentHeight:0.0} von {lauf.ViewportHeight:0.0}, Fenster ohne Rollen {groesse.Height - lauf.ViewportHeight + lauf.ExtentHeight:0}, Sprechen und Hören bis {unten:0.0}"));
        Pruefe.Wahr(unten <= platz + 0.5, T($"{was}: bis Sprechen und Hören kein Rollen ({unten:0.0} von {platz:0.0})"));
    }

    private static ScrollViewer Lauf(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt)).First(sv => sv.Content is StackPanel p && p.Children.OfType<ContentControl>().Any());

    private static ContentPresenter Seite<TModell>(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is TModell);

    private static T Teil<T>(FrameworkElement seite, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(seite).FirstOrDefault(e => e.Name == name) ?? throw new PruefungException(name + " fehlt");
}
