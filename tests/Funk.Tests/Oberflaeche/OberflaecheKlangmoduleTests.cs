// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Klangmodule (0.9.0, Sarevoc 06.10.2026, Q48 bis Q52): je Modul ein Schiebeschalter, ab Werk an, danach wie zuletzt geschaltet, zu jedem Modul ein
/// ausführlicher Hovertext. Bis 0.9.3 eine Karte mit zwei Spalten Senden und Empfangen auf der Seite Audio (Variante B); seit 0.10.0 (Q87) stehen die
/// Module zum Empfangen unter Empfangen im Klappbereich, "Senden mit Stimme" (das Modul Sprachaktivierung) auf Start, und die Echounterdrückung hat
/// drei Stufen (E11). Die Module zum Senden standen 0.10.0 und 0.11.0 im Klappbereich unter Senden; seit 0.11.1 (Sarevoc 12.10.2026) stehen sie dort offen,
/// ohne Aufklappen, wie bis 0.9.3, samt dem Schalter Sprachaktivierung.
/// </summary>
internal static class OberflaecheKlangmoduleTests
{
    // Seit 0.9.3 ohne den gesperrten Platzhalter "Stimmerkennung": Die Stimmerkennung ist eingestellt (Sarevoc 11.10.2026). Seit 0.11.1 wieder mit der
    // Sprachaktivierung unter Senden (bis 0.11.0 nur als "Senden mit Stimme" auf Start).
    private static readonly string[] Senden = ["Echoschutz", "Rauschunterdrückung", "Automatische Lautstärke", "Sprachaktivierung", "Mindest-Funklautstärke"];

    private static readonly string[] Empfangen = ["Auto Dauersend-Dämpfung", "Pegelausgleich"];

    [Test]
    public static void Klangmodule_Einstellungen_AbWerkAn_WieZuletztGeschaltet_Nachsichtig()
    {
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        var speicher = new EinstellungsSpeicher(pfad);
        Pruefe.Wahr(speicher.Laden().Audio is { Echoschutz: true, Sprachaktivierung: true, MindestFunklautstaerke: true, Pegelausgleich: true }, "ab Werk alle an");

        // Eine Datei von 0.8.x kennt die Felder nicht: alle an.
        File.WriteAllText(pfad, "{ \"audio\": { \"echounterdrueckung\": false } }");
        Pruefe.Wahr(speicher.Laden().Audio is { Echoschutz: true, Sprachaktivierung: true, MindestFunklautstaerke: true, Pegelausgleich: true, Echounterdrueckung: false },
            "alte Datei: Module an, der Rest gilt");

        // Falsche Werte legen die Datei nicht beiseite und gelten als an.
        File.WriteAllText(pfad, "{ \"audio\": { \"echoschutz\": \"nein\", \"sprachaktivierung\": { \"a\": 1 }, \"mindestFunklautstaerke\": [1], \"pegelausgleich\": null, \"echounterdrueckung\": false } }");
        var l = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "kein Fehler");
        Pruefe.Wahr(l.Audio is { Echoschutz: true, Sprachaktivierung: true, MindestFunklautstaerke: true, Pegelausgleich: true, Echounterdrueckung: false }, "falsche Werte: an");
        Pruefe.Gleich(0, Directory.GetFiles(o.Pfad, "*.defekt-*").Length, "nichts beiseitegelegt");

        // Wie zuletzt geschaltet: aus bleibt aus, über das Speichern hinweg.
        using var a = new Aufbau();
        var audio = a.Audio();
        audio.Echoschutz = false;
        audio.SprachaktivierungAn = false;
        audio.MindestFunklautstaerke = false;
        audio.Pegelausgleich = false;
        a.Ablage.Sichern();
        Pruefe.Wahr(a.Speicher.Laden().Audio is { Echoschutz: false, Sprachaktivierung: false, MindestFunklautstaerke: false, Pegelausgleich: false }, "aus gemerkt");
    }

    [Test]
    public static void Klangmodule_Audio_SchaltetDieMaschine_RauschunterdrueckungMerktStark()
    {
        using var a = new Aufbau();
        var audio = a.Audio();
        Pruefe.Wahr(a.Maschine is { Echoschutz: true, MindestFunklautstaerke: true, Pegelausgleich: true }, "beim Anlegen an die Maschine: an");
        audio.Echoschutz = false;
        audio.MindestFunklautstaerke = false;
        audio.Pegelausgleich = false;
        Pruefe.Wahr(a.Maschine is { Echoschutz: false, MindestFunklautstaerke: false, Pegelausgleich: false }, "aus an die Maschine");

        // Eine neue Sitzung übernimmt den gemerkten Stand.
        var neu = new AudioViewModel(a.Maschine, a.Verlauf, a.Ablage, a.Takt);
        a.Maschine.Echoschutz = true;
        a.Maschine.Pegelausgleich = true;
        a.Maschine.MindestFunklautstaerke = true;
        _ = new AudioViewModel(a.Maschine, a.Verlauf, a.Ablage, a.Takt);
        Pruefe.Wahr(a.Maschine is { Echoschutz: false, MindestFunklautstaerke: false, Pegelausgleich: false }, "beim Anlegen wie zuletzt geschaltet");
        Pruefe.Falsch(neu.Echoschutz, "das Modell liest den gemerkten Stand");

        // Rauschunterdrückung: aus und wieder an kehrt zu Stark zurück, wenn Stark gewählt war.
        audio.RauschfilterStark = true;
        audio.Rauschunterdrueckung = false;
        Pruefe.Gleich(Rauschfilter.Aus, a.Maschine.Rauschfilter, "aus");
        Pruefe.Wahr(audio.RauschfilterStark && !audio.RauschfilterStandard, "aus zeigt die Wahl Stark (gesperrt)");
        audio.Rauschunterdrueckung = true;
        Pruefe.Gleich(Rauschfilter.Stark, a.Maschine.Rauschfilter, "wieder an: Stark");
        audio.RauschfilterStandard = true;
        audio.Rauschunterdrueckung = false;
        audio.Rauschunterdrueckung = true;
        Pruefe.Gleich(Rauschfilter.Standard, a.Maschine.Rauschfilter, "wieder an: Standard");
    }

    [Test]
    public static void Klangmodule_SprachaktivierungAus_JedesFeldNurTaste_WahlBleibtGemerkt()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        _ = a.Haupt(out var funkfeld, out _, out var audio, out var netze);
        var crew = funkfeld.Kacheln.Single(k => k.Name == "Crew");
        Pruefe.Wahr(crew.IstSenden && crew.Sprachaktivierung && crew.KannSprachaktivierung, "Ausgangslage: Crew sendet mit VA");
        Pruefe.Wahr(a.Maschine.Sprachaktivierung, "die Maschine löst per Sprache aus");

        audio.SprachaktivierungAn = false;
        Pruefe.Falsch(a.Maschine.Sprachaktivierung, "Modul aus: nur Sprechtaste");
        Pruefe.Falsch(crew.KannSprachaktivierung, "der VA-Schalter im Feld ist gesperrt");
        Pruefe.Falsch(crew.Sprachaktivierung, "das Feld zeigt Taste");
        Pruefe.Falsch(funkfeld.SprachaktivierungBenutzt, "keine Sprachaktivierung in Gebrauch");
        var regel = netze.Felder.Select(f => f.Regelung).OfType<NetzRegelung>().First(r => r.NetzId == crew.NetzId);
        Pruefe.Falsch(regel.KannSprachaktivierung || regel.Sprachaktivierung, "auch im Modell der Kanäle: Taste und gesperrt");

        audio.SprachaktivierungAn = true;
        Pruefe.Wahr(a.Maschine.Sprachaktivierung && crew.Sprachaktivierung && crew.KannSprachaktivierung, "wieder an: die gemerkte Wahl VA gilt");
        Pruefe.Wahr(regel.KannSprachaktivierung && regel.Sprachaktivierung, "Modell der Kanäle: wieder VA");
        funkfeld.Dispose();
    }

    /// <summary>
    /// Seit 0.11.1 (Sarevoc 12.10.2026): die Module zum Senden offen unter Senden, ohne Aufklappen, zwei Spalten ab der Breite der Größe ab Werk (bis 0.11.0
    /// im Klappbereich, Bild m-5); die zum Empfangen unter Empfangen im Klappbereich; jeder Schalter mit ausführlichem Hovertext, die drei Knöpfe der
    /// Echounterdrückung ebenso, und die Echounterdrückung mit ihrem Satz. Echoschutz aus: die Warnung unter dem Schalter. In Klein (900) und schmal
    /// (640) stehen die Spalten untereinander und passen in die Karte.
    /// </summary>
    [Test]
    public static void Klangmodule_Feinheiten_SendenUndEmpfangen_AusfuehrlicheHovertexte()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var b = new Aufbau(FunkDienstAttrappe.Beispiel());
                var h = b.Haupt(out var funkfeld, out _, out var audio, out _);
                // Nur Empfangen aufklappen: Senden zeigt seine Module seit 0.11.1 ohne Aufklappen.
                Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
                var haupt = new Hauptfenster(h, b.Ablage);
                fenster.Add(haupt);
                var gross = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 2600)).Bild;
                var wurzel = Bildlauf.Wurzel(haupt);
                var karte = Bildlauf.Nachfahren<Border>(wurzel).Single(x => x.Name == "AbschnitteKarte");
                var senden = Bildlauf.Nachfahren<StackPanel>(karte).Single(x => x.Name == "AbschnittSenden");
                Pruefe.Falsch(Bildlauf.Nachfahren<Expander>(karte).Single(x => x.Name == "SendenFeinheiten").IsExpanded, kurz + ": Senden > Erweitert bleibt zu");
                var empfangen = Bildlauf.Nachfahren<Expander>(karte).Single(x => x.Name == "EmpfangenFeinheiten");
                var spalten = Bildlauf.Nachfahren<Funk.Client.Steuerelemente.Spaltenwechsel>(senden).Single(x => x.Name == "SendenSpalten");
                Pruefe.Wahr(spalten.Nebeneinander, kurz + ": bei 1100 zwei Spalten (m-5)");

                foreach (var (klapp, module) in new (FrameworkElement Bereich, string[] Module)[] { (senden, Senden), (empfangen, Empfangen) })
                {
                    var schalter = Bildlauf.Nachfahren<CheckBox>(klapp).Where(Ansichtspruefung.ZuSehen).ToList();
                    var namen = schalter.Select(s => System.Windows.Automation.AutomationProperties.GetName(s)).ToList();
                    foreach (var modul in module)
                    {
                        Pruefe.Wahr(namen.Contains(modul), kurz + ": " + modul + " in " + klapp.Name + " zu sehen");
                        var s = schalter[namen.IndexOf(modul)];
                        var hilfe = Glasdesign.Hilfe.GetText(s) ?? "";
                        Pruefe.Wahr(hilfe.Length >= 150, kurz + ": ausführlicher Hovertext für " + modul + " (" + hilfe.Length + " Zeichen)");
                        Pruefe.Falsch(hilfe.Any(z => z is '–' or '—' or '−' or '„' or '“' or '”' or '…'), kurz + ": nur einfache Zeichen, " + modul);
                    }
                    Pruefe.Falsch(namen.Contains("Stimmerkennung"), kurz + ": kein Platzhalter Stimmerkennung");
                }
                // Die Echounterdrückung mit ihrem Satz wie der Schalter bis 0.9.3, der ausführliche Hovertext an Name und Satz.
                var echoKopf = Bildlauf.Nachfahren<StackPanel>(senden).Single(x => x.Name == "EchounterdrueckungKopf");
                Pruefe.Wahr(Ansichtspruefung.ZuSehen(echoKopf) && Ansichtspruefung.Sichtbar(echoKopf, AudioViewModel.EchounterdrueckungErklaerung)
                    && Glasdesign.Hilfe.GetText(echoKopf) == AudioViewModel.EchounterdrueckungHilfe, kurz + ": Echounterdrückung mit Satz und Hovertext");
                // E11: die Echounterdrückung in drei Stufen, jede mit ausführlichem Hovertext.
                var echo = Bildlauf.Nachfahren<RadioButton>(senden).Where(r => r.GroupName == "Echounterdrueckung").ToList();
                Pruefe.Gleich("Automatisch|An|Aus", string.Join("|", echo.Select(r => r.Content as string)), kurz + ": drei Stufen");
                Pruefe.Wahr(echo.All(r => (Glasdesign.Hilfe.GetText(r) ?? "").Length >= 150), kurz + ": ausführliche Hovertexte der Echounterdrückung");
                Pruefe.Wahr(echo[0].IsChecked == true, kurz + ": ab Werk Automatisch");

                // Echoschutz aus: die Warnung erscheint unter dem Schalter.
                var warnung = Bildlauf.Nachfahren<TextBlock>(senden).Single(x => x.Name == "EchoschutzWarnungText");
                Pruefe.Gleich(Visibility.Collapsed, warnung.Visibility, kurz + ": an: keine Warnung");
                audio.Echoschutz = false;
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 2600));
                Pruefe.Gleich(Visibility.Visible, warnung.Visibility, kurz + ": aus: Warnung");
                audio.Echoschutz = true;

                var rk = karte.TransformToAncestor(wurzel).TransformBounds(new Rect(karte.RenderSize));
                Bildablage.Ablegen(new System.Windows.Media.Imaging.CroppedBitmap(gross, new Int32Rect(0, (int)Math.Max(0, rk.Top - 10), gross.PixelWidth,
                    (int)Math.Min(gross.PixelHeight - Math.Max(0, rk.Top - 10), rk.Height + 20))), "klangmodule-" + kurz);
                // In Klein und schmal: untereinander, alles in der Karte.
                foreach (var breite in new[] { Fenstergroessen.Mindest(Groessenklasse.Klein).Width, Fenstergroessen.SeiteMindest.Width })
                {
                    var schmal = Bildlauf.Zeichnen(haupt, 1, new Size(breite, 3400)).Bild;
                    var rs = karte.TransformToAncestor(wurzel).TransformBounds(new Rect(karte.RenderSize));
                    Pruefe.Wahr(Bildlauf.Nachfahren<FrameworkElement>(senden).Where(Ansichtspruefung.ZuSehen).All(e => e.TranslatePoint(new Point(e.ActualWidth, 0), wurzel).X <= rs.Right + 0.5),
                        kurz + ", " + breite + ": Senden passt in die Karte");
                    if (breite == Fenstergroessen.SeiteMindest.Width)
                    {
                        Pruefe.Falsch(spalten.Nebeneinander, kurz + ": schmal untereinander");
                        Bildablage.Ablegen(new System.Windows.Media.Imaging.CroppedBitmap(schmal, new Int32Rect(0, (int)Math.Max(0, rs.Top - 10), schmal.PixelWidth,
                            (int)Math.Min(schmal.PixelHeight - Math.Max(0, rs.Top - 10), rs.Height + 20))), "klangmodule-schmal-" + kurz);
                    }
                }
                funkfeld.Dispose();
            }
        });
    }
}