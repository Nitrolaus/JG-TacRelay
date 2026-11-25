// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Klangfarbe (Q70 bis Q72, 0.9.3) in Einstellungen und Oberfläche: drei Felder in den Audio-Einstellungen (ab Werk 0, nachsichtig gelesen,
/// begrenzt), seit 0.10.0 ein Teil der Feinheiten unter Einstellungen > Senden (rechte Spalte, Bild m-5; bis AP2 ein eigener Klappbereich in der
/// Spalte Senden der Klangmodule auf der Seite Audio), drei Regler mit ausführlichem Hovertext ohne Zahlen und "Zurück auf neutral". Jede Änderung
/// geht sofort an die Maschine.
/// </summary>
internal static class OberflaecheKlangfarbeTests
{
    /// <summary>Die Stufen der Sendekette in ihrer Reihenfolge, wie der Hilfetext Senden sie nennen muss.</summary>
    private static readonly string[] Kette = ["Echounterdrückung", "automatische Lautstärke", "Rauschunterdrückung", "Klangfarbe", "Sprachaktivierung"];

    [Test]
    public static void Klangfarbe_Einstellungen_AbWerkNeutral_Nachsichtig_Begrenzt_HinUndZurueck()
    {
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        var speicher = new EinstellungsSpeicher(pfad);
        Pruefe.Gleich(KlangfarbeEinstellung.Neutral, speicher.Laden().Audio.Klangfarbe, "ohne Datei neutral");

        // Eine Datei von 0.9.2 kennt die Felder nicht.
        File.WriteAllText(pfad, "{ \"audio\": { \"echounterdrueckung\": false } }");
        Pruefe.Wahr(speicher.Laden().Audio is { Klangfarbe.IstNeutral: true, Echounterdrueckung: false }, "alte Datei: neutral, der Rest gilt");

        foreach (var (json, soll) in new[]
                 {
                     ("3", 3), ("-6", -6), ("\"4\"", 4), ("2.6", 3), ("-2.4", -2), ("9", 6), ("-20", -6), ("null", 0), ("\"abc\"", 0), ("true", 0),
                     ("[1]", 0), ("{ \"a\": 1 }", 0),
                 })
        {
            File.WriteAllText(pfad, "{ \"audio\": { \"klangfarbeWaermeDb\": " + json + ", \"klangfarbeKlarheitDb\": " + json + ", \"klangfarbeSchaerfeDb\": "
                + json + ", \"echounterdrueckung\": false } }");
            var l = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, json + ": kein Fehler");
            Pruefe.Gleich(new KlangfarbeEinstellung(soll, soll, soll), l.Audio.Klangfarbe, json);
            Pruefe.Gleich(soll, l.Audio.KlangfarbeWaermeDb, json + ": das Feld selbst ist begrenzt");
            Pruefe.Falsch(l.Audio.Echounterdrueckung, json + ": der Rest bleibt");
        }
        Pruefe.Gleich(0, Directory.GetFiles(o.Pfad, "*.defekt-*").Length, "nichts beiseitegelegt");

        var e = new ClientEinstellungen();
        e.Audio.Klangfarbe = new KlangfarbeEinstellung(5, -2, 7);
        Pruefe.Gleich(new KlangfarbeEinstellung(5, -2, 6), e.Audio.Klangfarbe, "beim Setzen begrenzt");
        speicher.Speichern(e);
        Pruefe.Gleich(new KlangfarbeEinstellung(5, -2, 6), speicher.Laden().Audio.Klangfarbe, "hin und zurück");
        var text = File.ReadAllText(pfad);
        Pruefe.Wahr(text.Contains("\"klangfarbeWaermeDb\": 5", StringComparison.Ordinal) && text.Contains("\"klangfarbeSchaerfeDb\": 6", StringComparison.Ordinal),
            "in der Datei unter audio, als ganze Zahlen");
    }

    [Test]
    public static void Klangfarbe_Feinheiten_SchaltetMaschine_KopfzeileZurueckUndOffenGemerkt()
    {
        using var a = new Aufbau();
        Pruefe.Gleich(0, a.Maschine.KlangfarbeGesetzt, "vorher nichts gesetzt");
        var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
        var f = audio.Klangfarbe;
        var s = h.Einstellungen.AbschnittSenden!.Feinheiten;
        Pruefe.Gleich(1, a.Maschine.KlangfarbeGesetzt, "beim Anlegen bekommt die Maschine den gemerkten Stand");
        Pruefe.Gleich(KlangfarbeEinstellung.Neutral, a.Maschine.Klangfarbe, "ab Werk neutral");
        Pruefe.Wahr(s.Kennung == Abschnittskennung.Senden && s.Teile.Contains(f), "0.10.0 (E6): die Klangfarbe ist ein Teil der Feinheiten unter Senden");
        Pruefe.Falsch(s.Offen, "ab Werk zugeklappt");
        Pruefe.Falsch(s.EtwasSelbstGestellt || f.SelbstGestellt.Count > 0, "ab Werk nichts selbst gestellt");
        // 0.11.1 (Sarevoc 12.10.2026): "Erweitert:" vorn, dann der Stand in Klammern; unter Erweitert stehen nur noch feste Verstärkung, Mithören und
        // Klangfarbe, die Schalter offen darüber (bis 0.11.0 "Rauschfilter, Echo, Lautstärke, Mindest-Funklautstärke, Klangfarbe, Mithören, Sprachaktivierung").
        Pruefe.Gleich("Erweitert: (ab Werk) Feste Verstärkung, Mithören, Klangfarbe", s.Kopfzeile,
            "die Kopfzeile nennt vorn das Wort, dann Stand und Inhalt (E6, m-5)");
        Pruefe.Gleich("Alles unter Erweitert steht auf der Werkseinstellung.", s.StandSatz, "der Satz neben dem Knopf (m-5)");
        Pruefe.Falsch(f.ZurueckCommand.CanExecute(null) || s.ZurueckCommand.CanExecute(null), "Zurück gesperrt, solange neutral");
        Pruefe.Gleich("0 dB", f.WaermeText, "Wert neben dem Regler");

        var geaendert = new List<string?>();
        s.PropertyChanged += (_, e) => geaendert.Add(e.PropertyName);
        var teilGeaendert = new List<string?>();
        f.PropertyChanged += (_, e) => teilGeaendert.Add(e.PropertyName);
        f.Waerme = 5.6;
        Pruefe.Gleich(new KlangfarbeEinstellung(6, 0, 0), a.Maschine.Klangfarbe, "der Regler rastet auf ganze dB und geht sofort an die Maschine");
        Pruefe.Gleich(6, a.Ablage.Aktuell.Audio.KlangfarbeWaermeDb, "gemerkt");
        Pruefe.Gleich("+6 dB", f.WaermeText, "mit Vorzeichen");
        Pruefe.Gleich("Erweitert: (1 selbst gestellt) " + s.Inhalt, s.Kopfzeile, "die Kopfzeile sagt, wie viel selbst gestellt ist");
        Pruefe.Gleich("1 selbst gestellt: Wärme +6 dB. Alles andere steht ab Werk.", s.StandSatz, "der Satz sagt, was selbst gestellt ist (m-5)");
        Pruefe.Wahr(f.ZurueckCommand.CanExecute(null) && s.ZurueckCommand.CanExecute(null), "selbst gestellt: beide Zurück gehen");
        Pruefe.Wahr(geaendert.Contains(nameof(Feinheiten.Kopfzeile)) && teilGeaendert.Contains(nameof(KlangfarbeTeil.WaermeText)), "die Oberfläche erfährt es");
        f.Klarheit = -2;
        f.Schaerfe = 99;
        Pruefe.Gleich("Erweitert: (3 selbst gestellt) " + s.Inhalt, s.Kopfzeile, "alle selbst gestellten gezählt");
        Pruefe.Gleich("3 selbst gestellt: Wärme +6 dB, Klarheit -2 dB, Schärfe +6 dB. Alles andere steht ab Werk.", s.StandSatz, "alle im Satz");
        Pruefe.Gleich("-2 dB", f.KlarheitText, "nur das einfache Minus");
        Pruefe.Gleich(new KlangfarbeEinstellung(6, -2, 6), a.Maschine.Klangfarbe, "begrenzt an die Maschine");
        f.Waerme = double.NaN;
        Pruefe.Gleich(6, f.Einstellung.WaermeDb, "keine Zahl ändert nichts");
        f.Waerme = 0;
        Pruefe.Gleich("2 selbst gestellt: Klarheit -2 dB, Schärfe +6 dB. Alles andere steht ab Werk.", s.StandSatz, "was neutral ist, fehlt im Satz");

        f.ZurueckCommand.Execute(null);
        Pruefe.Wahr(f.Einstellung.IstNeutral && a.Maschine.Klangfarbe.IstNeutral && !s.EtwasSelbstGestellt && !f.ZurueckCommand.CanExecute(null), "Zurück auf neutral");
        Pruefe.Gleich("Erweitert: (ab Werk) " + s.Inhalt, s.Kopfzeile, "wieder ab Werk");
        f.Schaerfe = -1;
        s.ZurueckCommand.Execute(null);
        Pruefe.Wahr(f.Einstellung.IstNeutral && !s.ZurueckCommand.CanExecute(null), "Zurück auf Werkseinstellung stellt die Klangfarbe auch zurück");

        s.Offen = true;
        Pruefe.Wahr(a.Ablage.Aktuell.Anzeige.OffeneFeinheiten.SequenceEqual([Abschnittskennung.Senden]), "aufgeklappt gemerkt");
        f.Klarheit = 3;
        a.Ablage.Sichern();
        a.Maschine.Klangfarbe = KlangfarbeEinstellung.Neutral;
        funkfeld.Dispose();
        var neu = a.Haupt(out var funkfeld2, out _, out var audio2, out _);
        Pruefe.Wahr(neu.Einstellungen.AbschnittSenden!.Feinheiten.Offen && audio2.Klangfarbe.Einstellung.KlarheitDb == 3 && a.Maschine.Klangfarbe == new KlangfarbeEinstellung(0, 3, 0),
            "eine neue Sitzung: wie zuletzt");
        funkfeld2.Dispose();
        Pruefe.Wahr(a.Speicher.Laden() is { Audio.KlangfarbeKlarheitDb: 3 } g && g.Anzeige.OffeneFeinheiten.Contains(Abschnittskennung.Senden), "auch in der Datei");

        // Eine Datei aus 0.9.3 mit offener Klangfarbe: Die alte Kennung wird einmal "senden" (E6), der Klappbereich bleibt offen.
        File.WriteAllText(a.Speicher.Pfad, "{ \"anzeige\": { \"offeneFeinheiten\": [\"klangfarbe\", \"sprechtaste\"] } }");
        var alt = a.Speicher.Laden();
        Pruefe.Gleich("senden,tasten", string.Join(",", alt.Anzeige.OffeneFeinheiten), "alte Kennungen umgedeutet");
    }

    [Test]
    public static void Klangfarbe_Hovertexte_AusfuehrlichOhneZahlen_EinfacheZeichen()
    {
        foreach (var (hilfe, teile) in new (string, string[])[]
                 {
                     (KlangfarbeTeil.WaermeHilfe, ["voller", "nicht tiefer", "Weniger, wenn"]),
                     (KlangfarbeTeil.KlarheitHilfe, ["Mitlaute", "verständlich", "Gefecht", "Weniger, wenn"]),
                     (KlangfarbeTeil.SchaerfeHilfe, ["Zischlaute", "dämpft", "Mehr, wenn"]),
                 })
        {
            var name = hilfe[..hilfe.IndexOf(':', StringComparison.Ordinal)];
            Pruefe.Wahr(hilfe.Length >= 400, name + ": ausführlich wie bei den Klangmodulen (" + hilfe.Length + " Zeichen)");
            Pruefe.Falsch(hilfe.Any(char.IsDigit), name + ": ohne Zahlen, wie die Hovertexte der Klangmodule");
            foreach (var teil in teile.Concat(["nur die Klangfarbe, nicht die Lautstärke", "Hörprobe hört die Klangfarbe mit"]))
                Pruefe.Wahr(hilfe.Contains(teil, StringComparison.Ordinal), name + ": sagt " + teil);
        }
        using var a = new Aufbau();
        var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
        var f = audio.Klangfarbe;
        var s = h.Einstellungen.AbschnittSenden!.Feinheiten;
        f.Waerme = -4;
        foreach (var text in new[] { KlangfarbeTeil.WaermeHilfe, KlangfarbeTeil.KlarheitHilfe, KlangfarbeTeil.SchaerfeHilfe,
                     KlangfarbeTeil.Erklaerung, s.KopfHilfe, KlangfarbeTeil.ZurueckHilfe, s.ZurueckHilfe, s.Kopfzeile, s.StandSatz, f.WaermeText, AudioViewModel.SendenHilfe,
                     StimmprobenViewModel.Erklaerung })
            Pruefe.Falsch(text.Any(z => z is '–' or '—' or '−' or '„' or '“' or '”' or '…' or ' '), "nur einfache Zeichen: " + text);
        Pruefe.Falsch(StimmprobenViewModel.Erklaerung.Contains("Stimmerkennung", StringComparison.Ordinal), "Stimmproben: nicht mehr für die Stimmerkennung");
        // Der Hilfetext Senden nennt die Reihenfolge der Kette: Echo, Lautstärke, Rauschen, Klangfarbe, Sprachaktivierung (Sendekette).
        var senden = AudioViewModel.SendenHilfe;
        var stellen = Kette.Select(w => senden.IndexOf(w, StringComparison.Ordinal)).ToList();
        Pruefe.Wahr(stellen.All(i => i >= 0) && stellen.SequenceEqual(stellen.Order()), "Senden: Reihenfolge wie in der Kette");
        funkfeld.Dispose();
    }

    [Test]
    public static void Klangfarbe_Feinheiten_UnterSenden_Zugeklappt_ReglerUndZurueck()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
                var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
                h.AktiverBereich = Seiten.Einstellungen(h);
                var f = audio.Klangfarbe;
                var s = h.Einstellungen.AbschnittSenden!.Feinheiten;
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var (zu, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 2400));
                var wurzel = Bildlauf.Wurzel(haupt);
                var karte = Bildlauf.Nachfahren<Border>(wurzel).Single(x => x.Name == "AbschnitteKarte");
                var klapp = Bildlauf.Nachfahren<Expander>(karte).Single(x => x.Name == "SendenFeinheiten");
                Pruefe.Falsch(klapp.IsExpanded, kurz + ": ab Werk zugeklappt");
                var kopf = Bildlauf.Nachfahren<ToggleButton>(klapp).Single(x => x.Name == "Kopf");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(kopf).Any(t => t.Text == "(ab Werk) " + s.Inhalt && Ansichtspruefung.ZuSehen(t)),
                    kurz + ": die Kopfzeile nennt vorn das Wort, dann Stand und Inhalt (E6, 0.11.1)");
                // "Erweitert:" fett (Sarevoc 12.10.2026: "wir nennen das nun Erweitert: (fett)"), der Rest nicht.
                // Seit 0.11.4 eine Karte: der Titel oben, darunter Stand und Inhalt in normaler Schrift.
                var titel = Bildlauf.Nachfahren<TextBlock>(kopf).Single(x => x.Name == "KopfText");
                var rest = Bildlauf.Nachfahren<TextBlock>(kopf).Single(x => x.Name == "KopfRest");
                Pruefe.Wahr(titel.Text == Feinheiten.Wort && titel.FontWeight == FontWeights.Bold && rest.FontWeight != FontWeights.Bold,
                    kurz + ": nur das Wort Erweitert: fett");
                Pruefe.Gleich(s.Kopfzeile, System.Windows.Automation.AutomationProperties.GetName(kopf), kurz + ": Bildschirmleser lesen die ganze Kopfzeile");
                Pruefe.Gleich(s.KopfHilfe, Glasdesign.Hilfe.GetText(kopf), kurz + ": Hovertext an der Kopfzeile");
                Pruefe.Gleich(null, Glasdesign.Hilfe.GetText(klapp), kurz + ": nicht über dem ganzen Bereich");
                Pruefe.Wahr(klapp.ActualHeight < 56, kurz + ": zugeklappt nur die Karte mit Titel und Beschreibung (" + klapp.ActualHeight + ")");
                Pruefe.Falsch(Bildlauf.Nachfahren<CheckBox>(karte).Any(x => x.Name == "StimmerkennungSchalter"), kurz + ": kein Platzhalter Stimmerkennung");
                s.Offen = true;
                var (auf, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 2400));
                Pruefe.Wahr(klapp.IsExpanded, kurz + ": aufgeklappt über das Modell");
                // Seit 0.11.1 unter Erweitert rechts, neben der festen Verstärkung (bis 0.11.0 rechts unter der Lautstärke, Bild m-5); die
                // automatische Lautstärke steht offen über dem Klappbereich.
                var senden = Bildlauf.Nachfahren<StackPanel>(klapp).Single(x => x.Name == "SendenErweitertRechts");
                double Oben(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), wurzel).Y;
                double Links0(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), wurzel).X;
                var fest = Bildlauf.Nachfahren<StackPanel>(klapp).Single(x => x.Name == "FesteVerstaerkungTeil");
                var klangfarbe = Bildlauf.Nachfahren<StackPanel>(senden).Single(x => x.Name == "KlangfarbeTeil");
                Pruefe.Wahr(Math.Abs(Oben(fest) - Oben(klangfarbe)) < 1 && Links0(fest) < Links0(klangfarbe), kurz + ": rechts neben der festen Verstärkung");
                Pruefe.Falsch(Bildlauf.Nachfahren<StackPanel>(klapp).Any(x => x.Name == "LautstaerkeTeil"), kurz + ": die automatische Lautstärke steht offen, nicht unter Erweitert");
                foreach (var (name, hilfe) in new[] { ("WaermeRegler", KlangfarbeTeil.WaermeHilfe), ("KlarheitRegler", KlangfarbeTeil.KlarheitHilfe),
                             ("SchaerfeRegler", KlangfarbeTeil.SchaerfeHilfe) })
                {
                    var regler = Bildlauf.Nachfahren<Slider>(klapp).Single(x => x.Name == name);
                    Pruefe.Wahr(Ansichtspruefung.ZuSehen(regler) && regler.ActualWidth >= 100, kurz + ": " + name + " ist zu sehen");
                    Pruefe.Wahr(regler is { Minimum: -6, Maximum: 6, TickFrequency: 1, IsSnapToTickEnabled: true, SmallChange: 1, Value: 0 },
                        kurz + ": " + name + " -6 bis +6 in ganzen dB, ab Werk 0");
                    Pruefe.Gleich(hilfe, Glasdesign.Hilfe.GetText(regler), kurz + ": ausführlicher Hovertext an " + name);
                    // Zweiseitig (Prüfung 0.9.3): neutral keine Akzentfläche (sie läse sich als "halb an"), der Knauf an der Marke in der Mitte.
                    var akzent = ((SolidColorBrush)regler.FindResource("Pinsel.Akzent")).Color;
                    Pruefe.Falsch(Bildlauf.Nachfahren<Border>(regler).Any(b => b.Background is SolidColorBrush p && p.Color == akzent && b.ActualWidth > 0 && Ansichtspruefung.ZuSehen(b)),
                        kurz + ": " + name + " neutral ohne Akzentfläche");
                    var mitte = Bildlauf.Nachfahren<Rectangle>(regler).Single(x => x.Name == "Mitte");
                    var knauf = Bildlauf.Nachfahren<Thumb>(regler).Single();
                    double MitteX(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth / 2, 0), wurzel).X;
                    Pruefe.Wahr(Ansichtspruefung.ZuSehen(mitte) && Math.Abs(MitteX(mitte) - MitteX(knauf)) <= 1, kurz + ": " + name + " neutral steht der Knauf an der Marke in der Mitte");
                }
                var zurueck = Bildlauf.Nachfahren<Button>(klapp).Single(x => x.Name == "KlangfarbeZurueck");
                Pruefe.Falsch(zurueck.IsEnabled, kurz + ": Zurück gesperrt ab Werk");
                Pruefe.Gleich(KlangfarbeTeil.ZurueckHilfe, Glasdesign.Hilfe.GetText(zurueck), kurz + ": Hovertext am Knopf");
                // 0.10.0 (E6): darunter der Fuß des Behälters mit "Zurück auf Werkseinstellung" und dem Satz.
                var werk = Bildlauf.Nachfahren<Button>(klapp).Single(x => x.Name == "FeinheitenZurueck");
                Pruefe.Wahr(!werk.IsEnabled && Equals(werk.Content, "Zurück auf Werkseinstellung") && Glasdesign.Hilfe.GetText(werk) == s.ZurueckHilfe, kurz + ": Zurück auf Werkseinstellung");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(klapp).Any(t => t.Text == s.StandSatz && Ansichtspruefung.ZuSehen(t)), kurz + ": der Satz neben dem Knopf");
                Pruefe.Gleich("Zurück auf neutral", zurueck.Content, kurz + ": Knopf heißt Zurück auf neutral");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(klapp).Any(t => t.Text == KlangfarbeTeil.Erklaerung && Ansichtspruefung.ZuSehen(t)),
                    kurz + ": Erklärung unter den Reglern");
                var rechts = karte.TranslatePoint(new Point(karte.ActualWidth, 0), wurzel).X;
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(klapp).Where(Ansichtspruefung.ZuSehen).All(t => t.TranslatePoint(new Point(t.ActualWidth, 0), wurzel).X <= rechts),
                    kurz + ": alles in der Karte");

                Bildlauf.Nachfahren<Slider>(klapp).Single(x => x.Name == "WaermeRegler").Value = 4;
                Bildlauf.Nachfahren<Slider>(klapp).Single(x => x.Name == "SchaerfeRegler").Value = -3;
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 2400));
                Pruefe.Gleich(new KlangfarbeEinstellung(4, 0, -3), a.Maschine.Klangfarbe, kurz + ": die Regler stellen die Maschine");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(kopf).Any(t => t.Text == "(2 selbst gestellt) " + s.Inhalt), kurz + ": die Kopfzeile zählt");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(klapp).Any(t => t.Text == "+4 dB" && Ansichtspruefung.ZuSehen(t)), kurz + ": Wert neben dem Regler");
                Pruefe.Wahr(zurueck.IsEnabled, kurz + ": Zurück geht jetzt");
                Bildablage.Ablegen(Ausschnitt(auf, karte, wurzel), "klangfarbe-feinheiten-auf-" + kurz);
                zurueck.Command.Execute(null);
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 2400));
                Pruefe.Wahr(a.Maschine.Klangfarbe.IstNeutral && !zurueck.IsEnabled, kurz + ": zurück auf neutral");
                Bildablage.Ablegen(Ausschnitt(zu, karte, wurzel), "klangfarbe-feinheiten-zu-" + kurz);

                // Schmal (Prüfung 0.9.3): in der kleinsten Breite der Seite (640), in Klein (seit 0.10.0 900) und in der Größe ab Werk (960), mit allen drei
                // Reglern verstellt. Jeder Regler endet vor seinem Wert, nichts ist abgeschnitten, der Knauf ist auch ganz außen zu sehen, und die
                // Kopfzeile bricht um, statt hart abgeschnitten zu werden. Bis dahin maß der Test nur in 760 und gegen den rechten Kartenrand.
                f.Einstellung = new KlangfarbeEinstellung(6, -6, 6);
                double Links(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), wurzel).X;
                double Rechts(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth, 0), wurzel).X;
                foreach (var breite in new[] { Fenstergroessen.SeiteMindest.Width, Fenstergroessen.KleinMindest.Width, Fenstergroessen.SeiteVoreinstellung.Width })
                {
                    var schmal = Bildlauf.Zeichnen(haupt, 1, new Size(breite, 2400)).Bild;
                    var wo = kurz + ", " + breite + ": ";
                    var spalte = Rechts(senden);
                    foreach (var (name, wertName, wert) in new[] { ("WaermeRegler", "WaermeWert", "+6 dB"), ("KlarheitRegler", "KlarheitWert", "-6 dB"), ("SchaerfeRegler", "SchaerfeWert", "+6 dB") })
                    {
                        var regler = Bildlauf.Nachfahren<Slider>(klapp).Single(x => x.Name == name);
                        var w = Bildlauf.Nachfahren<TextBlock>(klapp).Single(x => x.Name == wertName);
                        Pruefe.Gleich(wert, w.Text, wo + name + ": Wert");
                        Pruefe.Wahr(Rechts(regler) <= Links(w) + 0.5 && Rechts(w) <= spalte + 0.5, wo + name + " endet vor seinem Wert, der Wert in der Spalte");
                        // In 640 bleiben gut 95 Einheiten, der Knauf läuft also gut 6 Einheiten je dB; mindestens 80 verlangt.
                        Pruefe.Wahr(LayoutInformation.GetLayoutClip(regler) is null && regler.ActualWidth >= 80, wo + name + " nicht abgeschnitten (" + regler.ActualWidth + ")");
                        var knauf = Bildlauf.Nachfahren<Thumb>(regler).Single();
                        Pruefe.Wahr(Ansichtspruefung.ZuSehen(knauf) && Links(knauf) >= Links(regler) - 0.5 && Rechts(knauf) <= Rechts(regler) + 0.5,
                            wo + name + ": der Knauf ist ganz außen zu sehen");
                    }
                    // Seit 0.11.4 Titel und Beschreibung getrennt; die Beschreibung bricht um.
                    var kopfText = Bildlauf.Nachfahren<TextBlock>(kopf).Single(x => x.Name == "KopfRest");
                    Pruefe.Gleich("(3 selbst gestellt) " + s.Inhalt, kopfText.Text, wo + "Kopfzeile mit allen drei");
                    Pruefe.Wahr(Rechts(kopf) <= Rechts(klapp) + 0.5 && Rechts(kopfText) <= spalte + 8.5 && LayoutInformation.GetLayoutClip(kopf) is null && LayoutInformation.GetLayoutClip(kopfText) is null,
                        wo + "die Kopfzeile ist ganz zu sehen (bricht um)");
                    if (breite == Fenstergroessen.SeiteMindest.Width) Bildablage.Ablegen(Ausschnitt(schmal, karte, wurzel), "klangfarbe-feinheiten-klein-" + kurz);
                }

                // Seit 0.10.0 unter Fehlersuche > Feinheiten (Q65); zugeklappt steht ihr Inhalt nicht im Baum der Darstellung, darum aufgeklappt.
                h.Einstellungen.AbschnittFehlersuche!.Feinheiten.Offen = true;
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 2400));
                var fs = Bildlauf.Nachfahren<Expander>(wurzel).Single(x => x.Name == "FehlersucheFeinheiten");
                Pruefe.Wahr(Bildlauf.Nachfahren<Border>(fs).Any(x => x.Name == "StimmprobenKarte" && Ansichtspruefung.ZuSehen(x)), kurz + ": Stimmproben unter Fehlersuche");
                funkfeld.Dispose();
            }
        });
    }

    /// <summary>Ein Ausschnitt um die Karte (seit 0.10.0 die Karte mit den Abschnitten).</summary>
    private static System.Windows.Media.Imaging.CroppedBitmap Ausschnitt(System.Windows.Media.Imaging.BitmapSource bild, FrameworkElement karte, FrameworkElement wurzel)
    {
        var r = karte.TransformToAncestor(wurzel).TransformBounds(new Rect(karte.RenderSize));
        var oben = (int)Math.Max(0, r.Top - 10);
        var hoehe = (int)Math.Min(bild.PixelHeight - oben, r.Height + 20);
        return new System.Windows.Media.Imaging.CroppedBitmap(bild, new Int32Rect(0, oben, bild.PixelWidth, hoehe));
    }
}
