// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;
using Funk.Protokoll.Steuerung;
using Funk.Tests.Verarbeitung;
using Glasdesign;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Stufe 2b von 0.6.0 nach Sarevocs erstem Test der Vorab-Fassung (02.10.2026): Die Sprachaktivierung klappte unter ihrer Leiste auf (bis 0.9.3;
/// seit 0.10.0 ist sie nur eine Seite und unten steht die Statuszeile, Q87), Klein ist seit 0.10.0 900 x 900 (Q89); der Funk-Klick steht
/// als Karte im Bereich Audio; wer von 0.5.x umsteigt, bekommt die Startseite klein an der alten Lage; die Hörprobe ist ohne
/// laufendes Audio gesperrt und sagt warum. Gezeichnet ohne Fensterhandle (<see cref="Bildlauf"/>), mit <c>FUNK_BILDER</c> als
/// PNG. Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheStufe2bTests
{
    /// <summary>
    /// Der Grund an der Hörprobe bei gestopptem Audio: seit 0.11.2 "Geht erst, wenn das Audio läuft." (bis 0.11.1 "Audio ist gestoppt. Starte es mit
    /// "Audio starten" ..."; den Knopf "Audio starten" gibt es auf Start nicht mehr).
    /// </summary>
    private const string GestopptGrund = "Geht erst, wenn das Audio läuft.";

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Sarevocs Skala 175 % und 100 %.</summary>
    private static readonly double[] Skalen = [1.0, 1.75];

    /// <summary>Bereich Audio in der Voreinstellung der Seiten und in deren Mindestbreite.</summary>
    private static readonly double[] AudioBreiten = [960.0, 640.0];

    /// <summary>Laptop mit Full HD bei 150 % (Windows-Voreinstellung vieler 15-Zoll-Geräte): Arbeitsbereich 1280 x 688 Einheiten.</summary>
    private static readonly Bildschirm Laptop150 = new(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1032), 1.5, true);

    private static string T(FormattableString s) => s.ToString(De);

    private static T Benannt<T>(Window haupt, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(Bildlauf.Wurzel(haupt)).First(e => e.Name == name);

    /// <summary>Wo ein Element im Fenster liegt (Einheiten, bezogen auf die Wurzel).</summary>
    private static Rect Lage(FrameworkElement e, Visual wurzel) => e.TransformToAncestor(wurzel).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    /// <summary>Hauptfenster auf der Startseite, getrennt, Namen eingetragen, Headset gewählt (kein Erststart), auf <paramref name="bildschirm"/>.</summary>
    private static (Hauptfenster Haupt, HauptViewModel Modell, SchwellenViewModel Schwelle) Startseite(Aufbau a, List<Window> fenster, Bildschirm bildschirm)
    {
        a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
        a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
        a.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        var h = a.Haupt(out _, out var schwelle, out _, out _);
        h.AktiverBereich = h.Bereiche[0];
        h.Takt();
        var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([bildschirm]));
        fenster.Add(haupt);
        return (haupt, h, schwelle);
    }

    /// <summary>
    /// Die Seite Sprachaktivierung, gemessen (seit 0.10.0 die einzige Stelle des Bereichs; bis 0.9.3 klappte er auch unter seiner Leiste auf,
    /// mit zwei Spalten in 760, und das Fenster wuchs nach unten): in ihrer Größe ab Werk stehen die Einstellungen rechts neben der festen Grafik,
    /// in ihrer Mindestbreite darunter; kein Text ohne Umbruch wird abgeschnitten, kein Funk-Klick, in Anthrazit bei 100 und 175 %. Sichtproben für Sarevoc.
    /// </summary>
    [Test]
    public static void Sprachaktivierung_Seite_PlatzbedarfUndSichtproben()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var (haupt, h, _) = Startseite(a, fenster, FullHd);
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            h.Takt();
            Pruefe.Falsch(Bildlauf.Nachfahren<FrameworkElement>(haupt.Schwellenbereich).Any(e => e.DataContext is KlickViewModel), "kein Funk-Klick in der Sprachaktivierung");

            foreach (var (groesse, rechts) in new[] { (Fenstergroessen.SprachaktivierungVoreinstellung, true), (Fenstergroessen.SprachaktivierungMindest, false) })
                foreach (var skala in Skalen)
                {
                    var was = T($"{groesse.Width:0} x {groesse.Height:0}, {skala * 100:0} %");
                    var (bild, _) = Bildlauf.Zeichnen(haupt, skala, groesse);
                    Bildablage.Ablegen(bild, T($"sprachaktivierung-seite-{groesse.Width:0}-anthrazit") + (skala == 1 ? "" : "-175"));
                    var grafik = haupt.Schwellenbereich.Verlaufsgrafik;
                    Console.WriteLine(T($"  {was}: Bereich {haupt.Schwellenbereich.ActualWidth:0} breit, Grafik {grafik.ActualWidth:0} x {grafik.ActualHeight:0}, Einstellungen {(haupt.Schwellenbereich.EinstellungenDarunter ? "darunter" : "rechts")}"));
                    Pruefe.Wahr(Math.Abs(grafik.ActualWidth - Schwellenbereich.SeitenGrafik.Width) < 1 && Math.Abs(grafik.ActualHeight - Schwellenbereich.SeitenGrafik.Height) < 1,
                        T($"{was}: die Grafik in fester Größe ({grafik.ActualWidth:0} x {grafik.ActualHeight:0})"));
                    Pruefe.Gleich(!rechts, haupt.Schwellenbereich.EinstellungenDarunter, was + ": Einstellungen " + (rechts ? "rechts" : "darunter"));
                    foreach (var text in Bildlauf.Nachfahren<TextBlock>(haupt.Schwellenbereich).Where(x => ZuSehen(x) && x.TextWrapping == TextWrapping.NoWrap).ToList())
                    {
                        // DesiredSize enthält den Rand (Margin), ActualWidth nicht.
                        var hat = text.ActualWidth + text.Margin.Left + text.Margin.Right;
                        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        PruefeWert.Hoechstens(hat + 0.5, text.DesiredSize.Width, T($"{was}: \"{Kurz(Inhalt(text))}\" ganz zu sehen"));
                    }
                    Pruefe.Wahr(Sichtbar(haupt.Schwellenbereich, "Schwelle bestimmt sich selbst") && Sichtbar(haupt.Schwellenbereich, "Rauschboden"), was + ": beide Teile zu sehen");
                }
        });
    }

    /// <summary>
    /// Mindestgröße und Arbeitsbereich der Startseite, seit 0.10.0 Klein mindestens 900 x 900 (Q89, Weg a; bis 0.9.3 mit aufgeklappter
    /// Sprachaktivierung gemessen): Full HD bei 100 % (1920 x 1040) nimmt sie ganz auf, ein Laptop mit Full HD bei 150 % (1280 x 688
    /// Einheiten) nicht; dort hält die Mindestgröße den Arbeitsbereich ein, und Start rollt. Die übrigen Seiten bleiben bei 640 x 480.
    /// </summary>
    [Test]
    public static void Klein_Mindestgroesse_1080pUndLaptop150()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (bildschirm, name, unten) in new[] { (FullHd, "Full HD 100 %", 1040.0), (Laptop150, "Laptop 150 %", 1032.0) })
            {
                using var a = new Aufbau(FunkLage.Leer);
                var (haupt, h, _) = Startseite(a, fenster, bildschirm);
                // Seit 0.10.0 (AP3) steht Einstellungen für die Seiten (bis 0.9.3 Audio).
                foreach (var seite in new[] { HauptViewModel.StartKennung, HauptViewModel.EinstellungenKennung })
                {
                    h.AktiverBereich = h.Bereiche.First(b => b.Kennung == seite);
                    var klasse = Fenstergroessen.Mindest(haupt.Klasse);
                    _ = Bildlauf.Zeichnen(haupt, 1, new Size(haupt.Width, haupt.Height));
                    var boden = (haupt.Top + haupt.Height) * bildschirm.Skala;
                    Console.WriteLine(T($"  {name}, {seite}: Mindest der Klasse {klasse.Width:0} x {klasse.Height:0}, gesetzt {haupt.MinWidth:0} x {haupt.MinHeight:0}, Fenster {haupt.Width:0} x {haupt.Height:0}, Unterkante {boden:0} von {unten:0}"));
                    Pruefe.Wahr(haupt.MinWidth * bildschirm.Skala <= 1920 + 0.5 && haupt.MinHeight * bildschirm.Skala <= unten + 0.5,
                        T($"{name}, {seite}: Mindestgröße im Arbeitsbereich ({haupt.MinWidth:0} x {haupt.MinHeight:0})"));
                    Pruefe.Wahr(boden <= unten + 0.5, T($"{name}, {seite}: Unterkante {boden:0} im Arbeitsbereich"));
                    Pruefe.Wahr(haupt.Width <= (seite == HauptViewModel.StartKennung ? 900 : 960), T($"{name}, {seite}: nicht breiter ({haupt.Width:0})"));
                    if (bildschirm == FullHd)
                        Pruefe.Gleich(klasse, new Size(haupt.MinWidth, haupt.MinHeight), name + ", " + seite + ": Mindestgröße ungekürzt");
                    else if (seite == HauptViewModel.StartKennung)
                        Pruefe.Gleich(new Size(900, 688), new Size(haupt.MinWidth, haupt.MinHeight), name + ": Klein so hoch wie der Arbeitsbereich");
                }
            }
        });
    }

    /// <summary>
    /// Umstieg mit einer echten Einstellungsdatei von 0.5.1 (tests\Funk.Tests\Daten\einstellungen-0.5.1, von 0.5.1 selbst
    /// geschrieben: Fenster 1280 x 860 bei 300 / 200, zuletzt Netze, Klang Piep). Bis Stufe 2 öffnete das Fenster in Netze mit
    /// 1280 x 860 (Sarevocs erster Test der Vorab-Fassung); jetzt die Startseite in Klein (seit 0.10.0 900 x 900, bis 0.9.3 760 x 660) an der
    /// alten Ecke, so weit nach oben gerückt wie nötig, die alte Größe gilt den Seiten, der Klang wird übergangen. Sichtprobe für Sarevoc.
    /// </summary>
    [Test]
    public static void Umstieg_EchteDatei051_StartseiteKleinAnDerAltenLage()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var o = new Testordner();
            var pfad = o.Datei("client-einstellungen.json");
            File.Copy(Path.Combine(EinrichtungTests.Repo(), "tests", "Funk.Tests", "Daten", "einstellungen-0.5.1", "client-einstellungen.json"), pfad);
            Pruefe.Wahr(File.ReadAllText(pfad).Contains("\"klang\": \"Piep\"", StringComparison.Ordinal), "die Datei trägt noch den Klang");
            var speicher = new EinstellungsSpeicher(pfad);
            var e = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, "0.5.1-Datei ohne Fehler gelesen");
            Pruefe.Wahr(e.HauptfensterGroessen is null && e.Bereich == "netze" && e.HauptfensterOrt is { Links: 300, Oben: 200, Breite: 1280, Hoehe: 860 },
                "so wie 0.5.1 sie schrieb");
            Pruefe.Gleich(-10.0, e.Klick.EigeneDb, "der Klick behält seine Lautstärke");

            using var a = new Aufbau(FunkLage.Leer, e);
            var h = a.Haupt(out _, out _, out _, out _);
            // Seit Klangstufe C1 (Q25) beginnt jeder Start auf der Startseite; bis dahin stand hier "vor dem Fenster noch Netze".
            Pruefe.Gleich(HauptViewModel.StartKennung, h.AktiverBereich.Kennung, "vor dem Fenster schon die Startseite (Q25)");
            Pruefe.Gleich(-10f, a.Maschine.Klick.EigeneDb, "die Maschine bekommt den Klick ohne Klang");
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            Pruefe.Gleich(HauptViewModel.StartKennung, h.AktiverBereich.Kennung, "Umstieg: die Startseite");
            Pruefe.Gleich(new Size(900, 900), new Size(haupt.Width, haupt.Height), "klein, nicht 1280 x 860 (Q89: 900 x 900)");
            // 200 + 900 passen nicht in 1040: nach oben, die linke Kante bleibt.
            Pruefe.Wahr(haupt.Left == 300 && haupt.Top == 140, T($"an der alten Ecke, in den Arbeitsbereich gerückt ({haupt.Left} / {haupt.Top})"));
            Pruefe.Gleich(new Fenstergroesse(1280, 860), a.Ablage.Aktuell.HauptfensterGroessen!["seite"], "die alte Größe gilt den Seiten");
            // Seit Klangstufe C1 (Q25) schreibt der Client den Bereich nicht mehr (bis dahin "gemerkt: Start"); der alte Eintrag bleibt
            // ungelesen stehen, die Startseite kommt trotzdem.
            Pruefe.Gleich("netze", a.Ablage.Aktuell.Bereich, "Q25: nichts gemerkt, der alte Eintrag bleibt ungelesen");
            var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(haupt.Width, haupt.Height));
            Bildablage.Ablegen(bild, "start-nach-umstieg-051");
            // Seit 0.11.2 darf "Senden" unten auf Start hereinrollen (OberflaecheStartseiteTests.BisSenden); bis Sprechen und Hören kein Rollen.
            var (unten, platz) = OberflaecheStartseiteTests.BisSenden(Bildlauf.Wurzel(haupt));
            Pruefe.Wahr(unten <= platz + 0.5, T($"die Startseite bis Sprechen und Hören ohne Rollen ({unten:0} von {platz:0})"));
            // Seit 0.10.0 (AP3) gibt es Meine Kanäle nicht mehr; Einstellungen ist eine Seite derselben Größenklasse.
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            Pruefe.Gleich(new Size(1280, 860), new Size(haupt.Width, haupt.Height), "Einstellungen in der alten Größe der Seiten");
        });
    }

    /// <summary>
    /// Der Funk-Klick (seit Stufe 2b; bis 0.9.3 als Karte im Bereich Audio, seit 0.10.0 unter Einstellungen > Tasten > Feinheiten), in Anthrazit:
    /// Texte, drei Schalter (ab Werk an), zwei Lautstärken, Anhören, keine Wahl des Klangs, keine Zugriffstaste, Kontrast. Bei 960 stehen die zwei
    /// Spalten nebeneinander, bei 640 untereinander, ohne dass Text über den Teil ragt. Sichtprobe für Sarevoc in Anthrazit.
    /// </summary>
    [Test]
    public static void Audio_KarteFunkKlick_TexteSchalterKontrast()
    {
        string[] texte = ["Rauschen", "Bei deiner Taste", "Bei anderen Sprechern", "Bei Sprachaktivierung dezent", "-12 dB", "-15 dB"];
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                foreach (var breite in AudioBreiten)
                {
                    using var a = new Aufbau(FunkLage.Leer);
                    a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
                    var h = a.Haupt(out _, out _, out var audio, out _);
                    Seiten.Feinheiten(h, Abschnittskennung.Tasten);
                    var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
                    fenster.Add(haupt);
                    var bild = Ganz(haupt, breite);
                    var was = T($"{satz.Name}, {breite:0}");
                    var karte = Benannt<Border>(haupt, "KlickKarte");
                    Pruefe.Wahr(ReferenceEquals(karte.DataContext, audio.Klick) && ZuSehen(karte), was + ": der Funk-Klick unter Tasten > Feinheiten");
                    Pruefe.Wahr(Sichtbar(Bildlauf.Wurzel(haupt), "Funk-Klick"), was + ": Überschrift Funk-Klick");
                    foreach (var text in texte) Pruefe.Wahr(Sichtbar(karte, text), was + ": " + text);
                    Pruefe.Gleich(3, Bildlauf.Nachfahren<CheckBox>(karte).Count(c => c.IsChecked == true), was + ": drei Schalter, ab Werk an");
                    Pruefe.Gleich(2, Bildlauf.Nachfahren<Slider>(karte).Count(), was + ": zwei Lautstärken");
                    Pruefe.Wahr(Bildlauf.Nachfahren<Button>(karte).Single() is { } knopf && ZuSehen(knopf) && Sichtbar(knopf, "Anhören"), was + ": Knopf Anhören");
                    Pruefe.Falsch(Bildlauf.Nachfahren<ComboBox>(karte).Any(), was + ": keine Wahl des Klangs");
                    Pruefe.Gleich(0, Bildlauf.Nachfahren<AccessText>(karte).Count(x => x.AccessKey != '\0'), was + ": keine Zugriffstaste");
                    KontrastPruefen(karte, satz, was + ", Funk-Klick", mindestens: 8);
                    foreach (var text in Bildlauf.Nachfahren<TextBlock>(karte).Where(ZuSehen))
                    {
                        var r = Lage(text, karte);
                        PruefeWert.Hoechstens(karte.ActualWidth - karte.Padding.Right + 0.5, r.Right, T($"{was}: \"{Kurz(Inhalt(text))}\" in der Karte"));
                    }
                    var spalten = Bildlauf.Nachfahren<StackPanel>(karte).Where(p => p.Width == 300).Select(p => Lage(p, karte)).ToList();
                    Pruefe.Gleich(2, spalten.Count, was + ": zwei Spalten");
                    Pruefe.Gleich(breite >= 960, Math.Abs(spalten[0].Top - spalten[1].Top) < 0.5, was + ": breit nebeneinander, schmal untereinander");
                    if (satz.Stellung == Farbvorschlaege.Voreinstellung && breite >= 960) Bildablage.Ablegen(bild, "audio-funkklick-anthrazit");
                }
            }
        });
    }

    /// <summary>
    /// Befund N3 der Gegenprüfung Stufe 2: Die Hörprobe war bei gestopptem Audio klickbar und tat nichts. Jetzt ist der Knopf auf
    /// der Startseite gesperrt (bis 0.9.3 auch im Bereich Audio und unten in der Sprachaktivierung; seit 0.10.0 gibt es nur die eine, Q75) und sagt im
    /// Hilfetext warum; mit laufendem Audio geht er wieder, abbrechen geht immer. Seit 0.10.0 geht sie auch verbunden.
    /// </summary>
    [Test]
    public static void Hoerprobe_OhneAudioGesperrt_MitGrund()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer);
            var (haupt, h, _) = Startseite(a, fenster, FullHd);
            var hp = h.Audio.Hoerprobe;
            Pruefe.Falsch(hp.ProbeCommand.CanExecute(null), "Audio gestoppt: Hörprobe gesperrt");
            Pruefe.Wahr(hp.ProbeGesperrt is { } grund && grund.Contains(GestopptGrund, StringComparison.Ordinal), "mit Grund: " + hp.ProbeGesperrt);
            Pruefe.Falsch(h.Start.HoerprobeCommand.CanExecute(null), "Startseite: gesperrt");
            Pruefe.Gleich(hp.ProbeGesperrt, h.Start.HoerprobeGesperrt, "Startseite: derselbe Grund");

            // Gezeichnet: auf der Startseite gesperrt, der Hilfetext nennt den Grund. Bis 0.9.3 stand die Sprachaktivierung dabei unten offen und
            // zeigte eine zweite Hörprobe, und der Bereich Audio eine dritte; seit 0.10.0 gibt es nur die eine auf Start (Q75, E19), unter Einstellungen
            // keine.
            List<Button> Knoepfe()
            {
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(haupt.Width, haupt.Height));
                return [.. Bildlauf.Nachfahren<Button>(Bildlauf.Wurzel(haupt)).Where(k => System.Windows.Automation.AutomationProperties.GetName(k) == "Hörprobe" && ZuSehen(k))];
            }
            void Pruefen(string wo, bool frei, int anzahl = 1)
            {
                var knoepfe = Knoepfe();
                Pruefe.Gleich(anzahl, knoepfe.Count, wo + ": " + anzahl + " Hörprobe zu sehen");
                foreach (var k in knoepfe)
                {
                    if (frei) Pruefe.Wahr(k.IsEnabled, wo + ": frei");
                    else Pruefe.Wahr(!k.IsEnabled && Hilfe.GetGesperrt(k) is { } g && g.Contains(GestopptGrund, StringComparison.Ordinal), wo + ": gesperrt mit Grund: " + Hilfe.GetGesperrt(k));
                }
            }
            Pruefen("Startseite", frei: false);
            Seiten.Feinheiten(h);
            Pruefen("Einstellungen, alle Feinheiten offen", frei: false, anzahl: 0);

            var gemeldet = 0;
            h.Start.HoerprobeCommand.CanExecuteChanged += (_, _) => gemeldet++;
            h.Audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.Takt();
            Pruefe.Wahr(hp.ProbeCommand.CanExecute(null) && hp.ProbeGesperrt is null, "Audio läuft: Hörprobe frei");
            Pruefe.Wahr(h.Start.HoerprobeCommand.CanExecute(null) && gemeldet > 0, "Startseite: frei und gemeldet");
            h.AktiverBereich = h.Bereiche[0];
            Pruefen("Startseite", frei: true);

            // Läuft eine Probe, geht Abbrechen auch ohne Audio.
            a.Maschine.Probe = new ProbeStand(ProbeZustand.Aufnahme, 3000);
            h.Audio.StoppenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.Takt();
            Pruefe.Wahr(hp.ProbeCommand.CanExecute(null) && hp.ProbeKnopfZusatz == "abbrechen", "abbrechen geht immer");
            a.Maschine.Probe = new ProbeStand(ProbeZustand.Bereit, 0);
            h.Takt();
            Pruefe.Falsch(hp.ProbeCommand.CanExecute(null), "danach wieder gesperrt");

            // Seit 0.10.0 (Q75): Die eine Hörprobe geht auch verbunden; gesperrt ist sie nur, wenn die Hörprobe selbst nicht kann (bis 0.9.3 auf
            // der Startseite "nur getrennt").
            h.Audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.Takt();
            a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Verbinden });
            Pruefe.Wahr(h.Start.HoerprobeCommand.CanExecute(null), "verbunden: frei");
            h.Audio.StoppenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.Takt();
            Pruefe.Falsch(h.Start.HoerprobeCommand.CanExecute(null), "verbunden ohne Audio: gesperrt");
            Pruefe.Gleich(hp.ProbeGesperrt, h.Start.HoerprobeGesperrt, "der Grund kommt aus der Hörprobe");
            Pruefe.Wahr(h.Start.HoerprobeGesperrt.Contains(GestopptGrund, StringComparison.Ordinal), "Grund: " + h.Start.HoerprobeGesperrt);
        });
    }
}
