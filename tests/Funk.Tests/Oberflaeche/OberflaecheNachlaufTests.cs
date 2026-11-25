// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Nachlauf der Sprechtaste (Q66, 0.9.3) in Einstellungen und Oberfläche: das Feld in den Tasten (ab Werk 100 ms, nachsichtig gelesen,
/// begrenzt), der Klappbereich "Feinheiten" unter der Sprechtaste (ab Werk zu, Kopfzeile vorn mit dem Stand, dann der Inhalt, Zustand gemerkt;
/// seit 0.10.0 der Behälter des Abschnitts Tasten mit dem Nachlauf als Teil, E6), der Regler mit ausführlichem Hovertext und "Zurück auf
/// Werkseinstellung" mit dem Satz daneben. Jede Änderung geht sofort an die Maschine.
/// </summary>
internal static class OberflaecheNachlaufTests
{
    [Test]
    public static void Nachlauf_Einstellungen_AbWerk_Nachsichtig_Begrenzt_HinUndZurueck()
    {
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        var speicher = new EinstellungsSpeicher(pfad);
        var leer = speicher.Laden();
        Pruefe.Gleich(100, TastenEinstellungen.NachlaufAbWerkMs, "ab Werk 100 ms: gemessen bis 50 ms, aufgerundet plus Reserve");
        Pruefe.Gleich(TastenEinstellungen.NachlaufAbWerkMs, leer.Tasten.NachlaufMs, "ohne Datei ab Werk");
        Pruefe.Gleich(0, leer.Anzeige.OffeneFeinheiten.Count, "Feinheiten ab Werk zu");

        // Eine Datei von 0.9.2 kennt die Felder nicht.
        File.WriteAllText(pfad, "{ \"tasten\": { \"meldenetz\": 7 } }");
        var alt = speicher.Laden();
        Pruefe.Wahr(alt.Tasten is { NachlaufMs: 100, Meldenetz: 7 }, "alte Datei: Nachlauf ab Werk, der Rest gilt");

        foreach (var (json, soll) in new[]
                 {
                     ("250", 250), ("\"300\"", 300), ("104", 100), ("105", 110), ("12.6", 10), ("-20", 0), ("5000", 1000), ("null", 100),
                     ("\"abc\"", 100), ("true", 100), ("[1]", 100), ("{ \"a\": 1 }", 100),
                 })
        {
            File.WriteAllText(pfad, "{ \"tasten\": { \"nachlaufMs\": " + json + ", \"meldenetz\": 7 }, "
                + "\"anzeige\": { \"offeneFeinheiten\": [\"sprechtaste\", 5, { \"a\": 1 }, \"sprechtaste\", \"\", null, \"tasten\"] } }");
            var l = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, json + ": kein Fehler");
            Pruefe.Gleich(soll, l.Tasten.NachlaufMs, json);
            Pruefe.Gleich(7, (int?)l.Tasten.Meldenetz, json + ": der Rest der Tasten bleibt");
            // 0.10.0 (E6): Die Kennung aus 0.9.3 wird einmal umgedeutet; neben der neuen steht sie nicht doppelt.
            Pruefe.Gleich("tasten", string.Join(",", l.Anzeige.OffeneFeinheiten), json + ": nur Texte, einmal, nicht leer, umgedeutet");
        }
        File.WriteAllText(pfad, "{ \"anzeige\": { \"offeneFeinheiten\": \"sprechtaste\" } }");
        Pruefe.Gleich(0, speicher.Laden().Anzeige.OffeneFeinheiten.Count, "keine Liste: leer");
        Pruefe.Gleich(0, Directory.GetFiles(o.Pfad, "*.defekt-*").Length, "nichts beiseitegelegt");

        var e = new ClientEinstellungen();
        e.Tasten.NachlaufMs = 370;
        e.Anzeige.OffeneFeinheiten.Add(Abschnittskennung.Tasten);
        speicher.Speichern(e);
        var z = speicher.Laden();
        Pruefe.Gleich(370, z.Tasten.NachlaufMs, "hin und zurück: Nachlauf");
        Pruefe.Wahr(z.Anzeige.OffeneFeinheiten.SequenceEqual([Abschnittskennung.Tasten]), "hin und zurück: aufgeklappt");
    }

    [Test]
    public static void Nachlauf_Feinheiten_SchaltetMaschine_ZurueckUndOffenGemerkt()
    {
        using var a = new Aufbau();
        Pruefe.Gleich(0, a.Maschine.TastenNachlaufMs, "vorher nichts gesetzt");
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var vm = h.Einstellungen.Tasten;
        var f = h.Einstellungen.AbschnittTasten!.Feinheiten;
        var n = vm.Nachlauf;
        Pruefe.Gleich(100, a.Maschine.TastenNachlaufMs, "beim Anlegen bekommt die Maschine den gemerkten Wert");
        // Seit 0.10.0 (AP3): ein Klappbereich für den ganzen Abschnitt Tasten (bis AP2 unter der Zeile Sprechtaste, nur mit dem Nachlauf).
        Pruefe.Wahr(ReferenceEquals(f.Teile[0], n), "0.10.0 (E6): der Behälter Tasten trägt den Nachlauf als ersten Teil");
        Pruefe.Gleich(Abschnittskennung.Tasten, f.Kennung, "Kennung des Abschnitts");
        Pruefe.Falsch(f.Offen, "ab Werk zugeklappt");
        Pruefe.Falsch(f.EtwasSelbstGestellt, "ab Werk nichts selbst gestellt");
        // 0.11.1 (Sarevoc 12.10.2026): "Erweitert:" vorn, dann der Stand in Klammern und der Inhalt.
        Pruefe.Gleich("Erweitert: (ab Werk) Nachlauf der Sprechtaste, Kanal vor und zurück, Melde-Taste, weitere Tasten, Funk-Klick", f.Kopfzeile,
            "die Kopfzeile nennt vorn das Wort, dann Stand und Inhalt (E6, m-4)");
        Pruefe.Gleich("Alles unter Erweitert steht auf der Werkseinstellung.", f.StandSatz, "der Satz neben dem Knopf");
        Pruefe.Falsch(f.ZurueckCommand.CanExecute(null), "Zurück gesperrt, solange nichts selbst gestellt ist");

        var geaendert = new List<string?>();
        f.PropertyChanged += (_, e) => geaendert.Add(e.PropertyName);
        var teilGeaendert = new List<string?>();
        n.PropertyChanged += (_, e) => teilGeaendert.Add(e.PropertyName);
        n.Nachlauf = 237.4;
        Pruefe.Gleich(240, n.NachlaufMs, "der Regler rastet auf 10 ms");
        Pruefe.Gleich(240, a.Maschine.TastenNachlaufMs, "sofort an die Maschine");
        Pruefe.Gleich(240, a.Ablage.Aktuell.Tasten.NachlaufMs, "gemerkt");
        Pruefe.Gleich("240 ms", n.NachlaufText, "Wert neben dem Regler");
        Pruefe.Wahr(f.EtwasSelbstGestellt && f.Kopfzeile == "Erweitert: (1 selbst gestellt) " + f.Inhalt && f.ZurueckCommand.CanExecute(null),
            "selbst gestellt: Kopfzeile und Knopf (" + f.Kopfzeile + ")");
        Pruefe.Gleich("1 selbst gestellt: Nachlauf 240 ms. Alles andere steht ab Werk.", f.StandSatz, "der Satz nennt, was selbst gestellt ist");
        Pruefe.Wahr(geaendert.Contains(nameof(Feinheiten.Kopfzeile)) && geaendert.Contains(nameof(Feinheiten.StandSatz))
            && teilGeaendert.Contains(nameof(NachlaufTeil.NachlaufText)), "die Oberfläche erfährt es");
        n.NachlaufMs = 5000;
        Pruefe.Gleich(1000, n.NachlaufMs, "höchstens 1000 ms");
        n.Nachlauf = double.NaN;
        Pruefe.Gleich(1000, n.NachlaufMs, "keine Zahl ändert nichts");

        f.ZurueckCommand.Execute(null);
        Pruefe.Wahr(n.NachlaufMs == 100 && a.Maschine.TastenNachlaufMs == 100 && !f.EtwasSelbstGestellt && !f.ZurueckCommand.CanExecute(null), "Zurück auf Werkseinstellung");

        f.Offen = true;
        Pruefe.Wahr(a.Ablage.Aktuell.Anzeige.OffeneFeinheiten.SequenceEqual([Abschnittskennung.Tasten]), "aufgeklappt gemerkt");
        n.NachlaufMs = 300;
        a.Ablage.Sichern();
        a.Maschine.TastenNachlaufMs = 0;
        funkfeld.Dispose();
        var neu = a.Haupt(out var funkfeld2, out _, out _, out _);
        Pruefe.Wahr(neu.Einstellungen.AbschnittTasten!.Feinheiten.Offen && neu.Einstellungen.Tasten.Nachlauf.NachlaufMs == 300 && a.Maschine.TastenNachlaufMs == 300,
            "eine neue Sitzung: wie zuletzt");
        Pruefe.Wahr(a.Speicher.Laden() is { Tasten.NachlaufMs: 300 } g && g.Anzeige.OffeneFeinheiten.Contains(Abschnittskennung.Tasten), "auch in der Datei");
        neu.Einstellungen.AbschnittTasten.Feinheiten.Offen = false;
        Pruefe.Gleich(0, a.Ablage.Aktuell.Anzeige.OffeneFeinheiten.Count, "zugeklappt gemerkt");
        funkfeld2.Dispose();
    }

    [Test]
    public static void Nachlauf_Hovertext_AusfuehrlichOhneZahlen_EinfacheZeichen()
    {
        var hilfe = NachlaufTeil.NachlaufHilfe;
        Pruefe.Wahr(hilfe.Length >= 400, "ausführlich wie bei den Klangmodulen (" + hilfe.Length + " Zeichen)");
        Pruefe.Falsch(hilfe.Any(char.IsDigit), "ohne Zahlen, wie die Hovertexte der Klangmodule");
        foreach (var teil in new[] { "Mehr, wenn", "Weniger, wenn", "nahtlos", "Mikro stumm", "Klick", "Melde-Taste" })
            Pruefe.Wahr(hilfe.Contains(teil, StringComparison.Ordinal), "sagt wofür, wann mehr, wann weniger: " + teil);
        using var a = new Aufbau();
        var h = a.Haupt(out _, out _, out _, out _);
        var f = h.Einstellungen.AbschnittTasten!.Feinheiten;
        // 0.10.0 (E6): Der Hovertext am Knopf sagt, dass danach alle Automatiken wieder laufen und Gelerntes bleibt, ohne Zahlen.
        Pruefe.Falsch(f.ZurueckHilfe.Any(char.IsDigit), "der Hovertext am Knopf ohne Zahlen");
        foreach (var teil in new[] { "Automatiken", "gelernt", "bleibt" })
            Pruefe.Wahr(f.ZurueckHilfe.Contains(teil, StringComparison.Ordinal), "Hovertext am Knopf: " + teil);
        foreach (var text in new[] { hilfe, NachlaufTeil.NachlaufErklaerung, f.KopfHilfe, f.ZurueckHilfe, f.Kopfzeile, f.StandSatz })
            Pruefe.Falsch(text.Any(z => z is '–' or '—' or '−' or '„' or '“' or '”' or '…' or ' '), "nur einfache Zeichen: " + text);
    }

    [Test]
    public static void Nachlauf_Feinheiten_UnterTasten_Zugeklappt_ReglerUndZurueck()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var a = new Aufbau();
                var h = a.Haupt(out var funkfeld, out _, out _, out _);
                h.AktiverBereich = h.Bereiche.Single(b => b.Inhalt is EinstellungenViewModel);
                var f = h.Einstellungen.AbschnittTasten!.Feinheiten;
                var n = ((EinstellungenViewModel)h.AktiverBereich.Inhalt).Tasten.Nachlauf;
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var (zu, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1120, 2600));
                var wurzel = Bildlauf.Wurzel(haupt);
                var klapp = Bildlauf.Nachfahren<Expander>(wurzel).Single(x => x.Name == "TastenFeinheiten");
                Pruefe.Falsch(klapp.IsExpanded, kurz + ": ab Werk zugeklappt");
                var kopf = Bildlauf.Nachfahren<ToggleButton>(klapp).Single(x => x.Name == "Kopf");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(kopf).Any(t => t.Text == "(ab Werk) " + f.Inhalt && Ansichtspruefung.ZuSehen(t)),
                    kurz + ": die Kopfzeile nennt vorn das Wort, dann Stand und Inhalt (E6, 0.11.1)");
                Pruefe.Gleich(f.KopfHilfe, Glasdesign.Hilfe.GetText(kopf), kurz + ": Hovertext an der Kopfzeile");
                Pruefe.Gleich(null, Glasdesign.Hilfe.GetText(klapp), kurz + ": nicht über dem ganzen Bereich");

                // Seit 0.11.4 eine Karte mit Titel und Beschreibung (zwei Zeilen), bis 0.11.3 eine Zeile unter 40.
                Pruefe.Wahr(klapp.ActualHeight < 56, kurz + ": zugeklappt nur die Karte mit Titel und Beschreibung (" + klapp.ActualHeight + ")");

                f.Offen = true;
                var (auf, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1120, 2600));
                Pruefe.Wahr(klapp.IsExpanded, kurz + ": aufgeklappt über das Modell");
                var regler = Bildlauf.Nachfahren<Slider>(klapp).Single(x => x.Name == "NachlaufRegler");
                Pruefe.Wahr(Ansichtspruefung.ZuSehen(regler), kurz + ": der Regler ist zu sehen");
                // In der Gruppe Sprechtaste unter der Zeile Sprechtaste, vor "Sendekanal vor" (E17).
                var titel = Bildlauf.Nachfahren<TextBlock>(klapp).Where(Ansichtspruefung.ZuSehen).ToList();
                double Oben(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), wurzel).Y;
                var sprechtaste = titel.Last(t => t.Text == "Sprechtaste");
                var vor = titel.First(t => t.Text == "Sendekanal vor");
                Pruefe.Wahr(Oben(sprechtaste) < Oben(regler) && Oben(regler) < Oben(vor), kurz + ": unter der Sprechtaste, vor der nächsten Taste");
                Pruefe.Wahr(regler is { Minimum: 0, Maximum: 1000, TickFrequency: 10, IsSnapToTickEnabled: true, SmallChange: 10 } && regler.Value == 100,
                    kurz + ": 0 bis 1000 ms in 10-ms-Schritten, ab Werk 100");
                Pruefe.Gleich(NachlaufTeil.NachlaufHilfe, Glasdesign.Hilfe.GetText(regler), kurz + ": ausführlicher Hovertext am Regler");
                var zurueck = Bildlauf.Nachfahren<Button>(klapp).Single(x => x.Name == "FeinheitenZurueck");
                Pruefe.Falsch(zurueck.IsEnabled, kurz + ": Zurück gesperrt ab Werk");
                Pruefe.Gleich(f.ZurueckHilfe, Glasdesign.Hilfe.GetText(zurueck), kurz + ": Hovertext am Knopf");
                Pruefe.Gleich("Zurück auf Werkseinstellung", zurueck.Content, kurz + ": ohne Zugriffstaste (0.10.0)");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(klapp).Any(t => t.Text == f.StandSatz && Ansichtspruefung.ZuSehen(t)), kurz + ": der Satz neben dem Knopf");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(klapp).Any(t => t.Text == "100 ms" && Ansichtspruefung.ZuSehen(t)), kurz + ": Wert neben dem Regler");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(klapp).Any(t => t.Text == NachlaufTeil.NachlaufErklaerung && Ansichtspruefung.ZuSehen(t)),
                    kurz + ": Erklärung unter dem Regler");

                regler.Value = 350;
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1120, 2600));
                Pruefe.Wahr(n.NachlaufMs == 350 && a.Maschine.TastenNachlaufMs == 350, kurz + ": der Regler stellt die Maschine");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(kopf).Any(t => t.Text == "(1 selbst gestellt) " + f.Inhalt), kurz + ": die Kopfzeile sagt selbst gestellt");
                Pruefe.Wahr(zurueck.IsEnabled, kurz + ": Zurück geht jetzt");
                Bildablage.Ablegen(Ausschnitt(auf, klapp, wurzel), "nachlauf-feinheiten-auf-" + kurz);
                zurueck.Command.Execute(null);
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1120, 2600));
                Pruefe.Wahr(regler.Value == 100 && a.Maschine.TastenNachlaufMs == 100 && !zurueck.IsEnabled, kurz + ": zurück auf 100 ms");
                Bildablage.Ablegen(Ausschnitt(zu, klapp, wurzel), "nachlauf-feinheiten-zu-" + kurz);

                // Schmal (Prüfung 0.9.3): Auch in der kleinsten Breite der Seite (640) und in Klein (bis 0.9.3 760, seit 0.10.0 900) endet der Regler vor seinem Wert, nichts
                // ist abgeschnitten, und der Knauf ist bei 1000 ms zu sehen. Mit der Beschriftung links daneben wurde er unter etwa 708 abgeschnitten.
                regler.Value = 1000;
                double Links(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), wurzel).X;
                double Rechts(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth, 0), wurzel).X;
                var karte = Vorfahren(klapp).OfType<Border>().First(b => ReferenceEquals(b.Style, b.TryFindResource("Karte")));
                foreach (var breite in new[] { Fenstergroessen.SeiteMindest.Width, Fenstergroessen.KleinMindest.Width })
                {
                    var schmal = Bildlauf.Zeichnen(haupt, 1, new Size(breite, 2600)).Bild;
                    var wo = kurz + ", " + breite + ": ";
                    var wert = Bildlauf.Nachfahren<TextBlock>(klapp).Single(x => x.Name == "NachlaufWert");
                    Pruefe.Gleich("1000 ms", wert.Text, wo + "Wert");
                    Pruefe.Wahr(Rechts(regler) <= Links(wert) + 0.5 && Rechts(wert) <= Rechts(karte) + 0.5, wo + "der Regler endet vor seinem Wert, der Wert in der Karte");
                    Pruefe.Wahr(LayoutInformation.GetLayoutClip(regler) is null && regler.ActualWidth >= 100, wo + "der Regler ist nicht abgeschnitten (" + regler.ActualWidth + ")");
                    var knauf = Bildlauf.Nachfahren<Thumb>(regler).Single();
                    Pruefe.Wahr(Ansichtspruefung.ZuSehen(knauf) && Rechts(knauf) <= Rechts(regler) + 0.5, wo + "der Knauf ist bei 1000 ms zu sehen");
                    if (breite == Fenstergroessen.SeiteMindest.Width) Bildablage.Ablegen(Ausschnitt(schmal, klapp, wurzel), "nachlauf-feinheiten-klein-" + kurz);
                }
                funkfeld.Dispose();
            }
        });
    }

    /// <summary>Die Vorfahren im Baum der Darstellung, vom Elternteil an aufwärts.</summary>
    private static IEnumerable<DependencyObject> Vorfahren(DependencyObject d)
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(d); p is not null; p = System.Windows.Media.VisualTreeHelper.GetParent(p)) yield return p;
    }

    /// <summary>Die Karte der globalen Tasten um den Klappbereich herum.</summary>
    private static System.Windows.Media.Imaging.CroppedBitmap Ausschnitt(System.Windows.Media.Imaging.BitmapSource bild, FrameworkElement klapp, FrameworkElement wurzel)
    {
        var r = klapp.TransformToAncestor(wurzel).TransformBounds(new Rect(klapp.RenderSize));
        var oben = (int)Math.Max(0, r.Top - 80);
        var hoehe = (int)Math.Min(bild.PixelHeight - oben, r.Height + 160);
        return new System.Windows.Media.Imaging.CroppedBitmap(bild, new Int32Rect(0, oben, bild.PixelWidth, hoehe));
    }
}
