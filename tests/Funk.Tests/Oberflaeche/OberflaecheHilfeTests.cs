// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Funk.Client;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Hilfe = Glasdesign.Hilfe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Hilfetexte an Knöpfen (Sarevoc, 02.10.2026, Vorgabe i, Q9; seit Stufe 2 über Glasdesign): <see cref="Hilfe"/> setzt aus Text, Taste
/// und Sperrgrund den ToolTip, zeigt den Grund, solange der Knopf gesperrt ist, wartet eine halbe Sekunde, bleibt stehen, solange
/// die Maus darauf ruht (Voreinstellung von WPF, Befund N2), und der Schalter "Tooltips zeigen" unter Einstellungen schweigt alle
/// ToolTips auf einmal. Aussehen: Glas.Tooltip. Ein Wächter zählt Knöpfe, Halteknöpfe und CheckBox-Schalter ohne Hilfetext im Client
/// und im Verwaltungsteil (Befunde N3, N4) gegen eine Liste, die nur schrumpfen darf. Der Klassenname steht nach
/// OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheHilfeTests
{
    /// <summary>
    /// WPF erlaubt das Überschreiben der ToolTip-Metadaten nur einmal je Typ: Es gibt deshalb nur eine Hilfe-Klasse, die von
    /// Glasdesign. JG TacRelay legt vorher nur die Anzeigedauer auf "unbegrenzt" fest (App.HilfeEinrichten).
    /// </summary>
    [Test]
    public static void Hilfe_NurEineKlasse_DieVonGlasdesign()
    {
        Pruefe.Gleich(null, typeof(App).Assembly.GetType("Funk.Client.Design.Hilfe"), "keine eigene Hilfe-Klasse mehr im Client");
        var repo = EinrichtungTests.Repo();
        Pruefe.Falsch(File.Exists(Path.Combine(repo, "src", "Funk.Client", "Design", "Hilfe.cs")), "Design\\Hilfe.cs entfernt");
        foreach (var datei in Directory.EnumerateFiles(Path.Combine(repo, "src"), "*.xaml", SearchOption.AllDirectories))
        {
            if (datei.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            Pruefe.Falsch(System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(datei), @"(?<![A-Za-z])d:Hilfe\.", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)), Path.GetFileName(datei) + ": Hilfe nur über gd:Hilfe");
        }
        Bildlauf.Ausfuehren(_ =>
        {
            var k = new Button();
            Hilfe.SetText(k, "Probe.");
            Pruefe.Gleich(int.MaxValue, ToolTipService.GetShowDuration(k), "Anzeigedauer unbegrenzt (Befund N2)");
            Pruefe.Gleich(ToolTipService.ShowDurationProperty.DefaultMetadata.DefaultValue, ToolTipService.GetShowDuration(new TextBox()),
                "wie die Voreinstellung von WPF");
            Pruefe.Gleich(Hilfe.Verzoegerung, ToolTipService.GetInitialShowDelay(new TextBox()), "Verzögerung eine halbe Sekunde, für jedes Element");
        });
    }

    [Test]
    public static void Hilfe_Inhalt_TextTasteUndGrundDerSperre()
    {
        Pruefe.Gleich("Funkfeld zeigen. (Strg+F)", Hilfe.Inhalt("Funkfeld zeigen.", "Strg+F", null, true), "Text mit Taste");
        Pruefe.Gleich("Text (Strg+T)", Hilfe.Inhalt(" Text ", " Strg+T ", null, true), "Ränder weg");
        Pruefe.Gleich("Verbinden.", Hilfe.Inhalt("Verbinden.", null, "Erst das Serverprofil.", true), "aktiv: der Text, nicht der Grund");
        Pruefe.Gleich("Erst das Serverprofil.", Hilfe.Inhalt("Verbinden.", "Strg+V", "Erst das Serverprofil.", false), "gesperrt: der Grund, ohne Taste");
        Pruefe.Gleich("Verbinden.", Hilfe.Inhalt("Verbinden.", null, "  ", false), "gesperrt ohne Grund: der Text");
        Pruefe.Gleich(null, Hilfe.Inhalt(null, "Strg+F", null, true), "ohne Text nichts, auch nicht die Taste allein");
        Pruefe.Gleich(null, Hilfe.Inhalt("", null, null, true), "leer ist nichts");
    }

    [Test]
    public static void Hilfe_AmKnopf_ToolTipVerzoegerungSperreUndGlobalerSchalter()
    {
        Bildlauf.Ausfuehren(_ =>
        {
            Hilfe.Zeigen = true;
            var k = new Button { Content = "Funkfeld" };
            Hilfe.SetText(k, "Holt das Funkfeld nach vorn.");
            Hilfe.SetTaste(k, "Strg+F");
            Pruefe.Gleich("Holt das Funkfeld nach vorn. (Strg+F)", k.ToolTip as string, "ToolTip aus Text und Taste");
            Pruefe.Falsch(ToolTipService.GetShowOnDisabled(k), "ohne Grund nichts am gesperrten Knopf");
            Pruefe.Gleich(Hilfe.Verzoegerung, ToolTipService.GetInitialShowDelay(k), "etwa eine halbe Sekunde Verzögerung");
            Pruefe.Gleich(int.MaxValue, ToolTipService.GetShowDuration(k), "bleibt stehen, solange die Maus ruht");
            Pruefe.Gleich(Hilfe.Verzoegerung, ToolTipService.GetInitialShowDelay(new TextBox()), "die Verzögerung gilt auch für Elemente ohne Hilfe (Titelknöpfe, Textfelder)");

            Hilfe.SetGesperrt(k, "Erst verbinden, dann gibt es ein Funkfeld.");
            Pruefe.Wahr(ToolTipService.GetShowOnDisabled(k), "mit Grund auch am gesperrten Knopf");
            Pruefe.Gleich("Holt das Funkfeld nach vorn. (Strg+F)", k.ToolTip as string, "aktiv: weiter der Text");
            k.IsEnabled = false;
            Pruefe.Gleich("Erst verbinden, dann gibt es ein Funkfeld.", k.ToolTip as string, "gesperrt: der Grund");
            k.IsEnabled = true;
            Pruefe.Gleich("Holt das Funkfeld nach vorn. (Strg+F)", k.ToolTip as string, "wieder aktiv: der Text");
            Hilfe.SetGesperrt(k, null);
            k.IsEnabled = false;
            Pruefe.Falsch(ToolTipService.GetShowOnDisabled(k), "Grund weg: gesperrt wieder stumm");
            Pruefe.Gleich("Holt das Funkfeld nach vorn. (Strg+F)", k.ToolTip as string, "der Text bleibt gesetzt");

            // Der globale Schalter: Das Öffnen jedes ToolTips läuft über ToolTipOpening, und Hilfe erledigt das Ereignis, solange
            // "Hilfetexte zeigen" aus ist. Die Ereignisargumente haben keinen öffentlichen Konstruktor; hier wie WPF selbst.
            var alt = new Button { Content = "Minimieren", ToolTip = "Minimieren" };
            static ToolTipEventArgs Oeffnen()
            {
                var args = (ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null, [true], null)!;
                args.RoutedEvent = ToolTipService.ToolTipOpeningEvent;
                return args;
            }
            foreach (var knopf in new[] { k, alt })
            {
                Hilfe.Zeigen = true;
                var an = Oeffnen();
                knopf.RaiseEvent(an);
                Pruefe.Falsch(an.Handled, "Hilfetexte an: " + knopf.Content + " darf öffnen");
                Hilfe.Zeigen = false;
                var aus = Oeffnen();
                knopf.RaiseEvent(aus);
                Pruefe.Wahr(aus.Handled, "Hilfetexte aus: " + knopf.Content + " öffnet nicht, auch ein alter ToolTip nicht");
            }
            Hilfe.Zeigen = true;
        });
    }

    /// <summary>Jeder ToolTip ist die Hilfekarte von Glasdesign (Glas.Tooltip): dunkle Glaskarte, Umbruch bei 320, ohne Schatten.</summary>
    [Test]
    public static void Hilfe_ToolTipStil_GlasKarteMitUmbruch()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var r = Application.Current!.Resources;
                var lang = "Verbindet dich mit dem Funk der Gilde. Vorher prüft das Programm das Serverprofil und deine Namen; fehlt etwas, steht es in der Karte darüber.";
                var t = new ToolTip { Content = lang };
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Pruefe.Wahr(ReferenceEquals(t.Background, r["Glas.Karte"]) && ReferenceEquals(t.BorderBrush, r["Glas.Karte.Linie"]), satz.Name + ": Glas.Tooltip");
                Pruefe.Wahr(t.DesiredSize.Width <= Hilfe.Hoechstbreite + 1, $"{satz.Name}: Umbruch bei {Hilfe.Hoechstbreite} ({t.DesiredSize.Width:0})");
                Pruefe.Wahr(t.DesiredSize.Height >= 40, $"{satz.Name}: mehrere Zeilen ({t.DesiredSize.Height:0})");
                Pruefe.Wahr(t.FontSize is >= 12 and <= 13, $"{satz.Name}: 12 bis 13 Punkt ({t.FontSize})");
                Pruefe.Falsch(t.HasDropShadow, satz.Name + ": kein Schatten (Effekt)");
                var kurz = new ToolTip { Content = "Funkfeld zeigen. (Strg+F)" };
                kurz.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Pruefe.Wahr(kurz.DesiredSize.Width < 220 && kurz.DesiredSize.Height < 36, $"{satz.Name}: kurzer Text einzeilig ({kurz.DesiredSize.Width:0} x {kurz.DesiredSize.Height:0})");
                // Gezeichnet (ein ToolTip darf keinen Elternteil haben) und auf den Grund des Fensters gelegt: Die Glaskarte ist zu 96 %
                // deckend; die Karte bleibt dunkel, die Schrift darauf mindestens 4,5:1.
                var bild = Zeichnen(t);
                var p = Bildlauf.Pixel(bild, 4, t.DesiredSize.Height / 2);
                var g = ((SolidColorBrush)r["Pinsel.Anthrazit"]).Color;
                byte Mischen(byte v, byte h) => (byte)Math.Round(((p.A * v) + ((255 - p.A) * h)) / 255.0);
                var karte = Color.FromRgb(Mischen(p.R, g.R), Mischen(p.G, g.G), Mischen(p.B, g.B));
                Pruefe.Wahr(p.A >= 0xF0, $"{satz.Name}: Karte fast deckend ({p.A})");
                Pruefe.Wahr(Kontrast.Leuchtdichte(karte) < 0.05, $"{satz.Name}: dunkle Karte ({karte})");
                var k = Kontrast.Verhaeltnis(((SolidColorBrush)t.Foreground).Color, karte);
                Pruefe.Wahr(k >= 4.5, $"{satz.Name}: Schrift auf der Hilfekarte {k:0.00}:1");
            }
        });
    }

    /// <summary>Zeichnet ein Element in seiner Wunschgröße, ohne Fenster und ohne Handle.</summary>
    internal static RenderTargetBitmap Zeichnen(FrameworkElement element, double skala = 1)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var g = new Size(Math.Ceiling(element.DesiredSize.Width), Math.Ceiling(element.DesiredSize.Height));
        element.Arrange(new Rect(g));
        element.UpdateLayout();
        Bildlauf.Leeren();
        var bild = new RenderTargetBitmap((int)Math.Ceiling(g.Width * skala), (int)Math.Ceiling(g.Height * skala), 96 * skala, 96 * skala, PixelFormats.Pbgra32);
        bild.Render(element);
        bild.Freeze();
        return bild;
    }

    [Test]
    public static void Hilfe_Einstellung_AbWerkAn_AlteUndNeueDateiLesbar_SchalterWirktSofort()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        Pruefe.Wahr(s.Laden().Anzeige.Hilfetexte, "ohne Datei: an");
        // Alte Datei (0.5.x) ohne das Feld: an. Neue Datei mit einem Feld, das dieser Client nicht kennt: lesbar; genauso liest
        // ein Client 0.5.x die neue Datei mit "hilfetexte" (Kreuzprobe über das unbekannte Feld).
        File.WriteAllText(s.Pfad, "{ \"anzeige\": { \"zeichenNebenNamen\": false } }");
        var alt = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "alte Datei lesbar");
        Pruefe.Wahr(alt.Anzeige.Hilfetexte && !alt.Anzeige.ZeichenNebenNamen, "alte Datei: Hilfetexte an, der Rest bleibt");
        File.WriteAllText(s.Pfad, "{ \"anzeige\": { \"hilfetexte\": false, \"spaeter\": { \"x\": 1 } } }");
        var neu = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "ein unbekanntes Feld daneben stört nicht");
        Pruefe.Falsch(neu.Anzeige.Hilfetexte, "aus gelesen");
        s.Speichern(neu);
        Pruefe.Wahr(File.ReadAllText(s.Pfad).Contains("\"hilfetexte\": false", StringComparison.Ordinal), "geschrieben");

        using var a = new Aufbau();
        var h = a.Haupt(out _, out _, out _, out _);
        Hilfe.Zeigen = true;
        Pruefe.Wahr(h.Einstellungen.Hilfetexte, "Schalter ab Werk an");
        h.Einstellungen.Hilfetexte = false;
        Pruefe.Falsch(a.Ablage.Aktuell.Anzeige.Hilfetexte || Hilfe.Zeigen, "aus: in der Ablage und sofort wirksam");
        h.Einstellungen.Hilfetexte = true;
        Pruefe.Wahr(a.Ablage.Aktuell.Anzeige.Hilfetexte && Hilfe.Zeigen, "wieder an");

        // Vorerst ein Schalter unter Einstellungen (Stufe 3 bringt die Reiter), und die App setzt ihn vor dem ersten Fenster.
        var repo = EinrichtungTests.Repo();
        var bereiche = File.ReadAllText(Path.Combine(repo, "src", "Funk.Client", "Fenster", "Bereiche.xaml"));
        Pruefe.Wahr(bereiche.Contains("Content=\"Tooltips zeigen\" IsChecked=\"{Binding Hilfetexte}\"", StringComparison.Ordinal), "Schalter Tooltips zeigen unter Einstellungen");
        var app = File.ReadAllText(Path.Combine(repo, "src", "Funk.Client", "App.xaml.cs"));
        Pruefe.Wahr(app.Contains("Hilfe.Zeigen = einstellungen.Anzeige.Hilfetexte;", StringComparison.Ordinal), "App: Schalter beim Start angewandt");
    }

    /// <summary>
    /// Knöpfe (Button, ToggleButton, RadioButton, Halteknopf) und CheckBox-Schalter in den XAML-Dateien des Clients und des
    /// Verwaltungsteils, außer den Titelknöpfen: welche einen Hilfetext oder ToolTip haben und welche nicht, mit Datei und
    /// Bezeichner (x:Name, AutomationProperties.Name, Content oder Command). Dateien des Clients stehen ohne Vorsatz da (wie bis
    /// Stufe 1), die des Verwaltungsteils mit "Funk.Verwaltung/".
    /// </summary>
    internal static List<(string Kennung, bool Hilfe, bool AlterToolTip)> Knoepfe()
    {
        var src = Path.Combine(EinrichtungTests.Repo(), "src");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var liste = new List<(string, bool, bool)>();
        var dateien = new[] { ("Funk.Client", ""), ("Funk.Verwaltung", "Funk.Verwaltung/") }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(src, p.Item1), "*.xaml", SearchOption.AllDirectories)
                .Select(d => (Datei: d, Rel: p.Item2 + Path.GetRelativePath(Path.Combine(src, p.Item1), d).Replace('\\', '/'))))
            .OrderBy(d => d.Rel, StringComparer.Ordinal);
        foreach (var (datei, rel) in dateien)
        {
            if (rel.Contains("bin/", StringComparison.Ordinal) || rel.Contains("obj/", StringComparison.Ordinal)) continue;
            var doc = XDocument.Load(datei, LoadOptions.SetLineInfo);
            var gezaehlt = new Dictionary<string, int>(StringComparer.Ordinal);
            // Stile, die selbst einen Hilfetext setzen (der Kopf einer Kachel im Funkfeld): Ihre Knöpfe haben einen.
            var stileMitHilfe = doc.Descendants().Where(s => s.Name.LocalName == "Style"
                    && s.Elements().Any(st => st.Name.LocalName == "Setter" && ((string?)st.Attribute("Property"))?.EndsWith("Hilfe.Text", StringComparison.Ordinal) == true))
                .Select(s => "{StaticResource " + (string?)s.Attribute(x + "Key") + "}").ToHashSet(StringComparer.Ordinal);
            // Teile einer Steuerelementvorlage (der Aufklappknopf der Auswahlliste) sind keine eigenen Knöpfe.
            foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "Button" or "ToggleButton" or "RadioButton" or "Halteknopf" or "CheckBox"
                         && !e.Ancestors().Any(v => v.Name.LocalName == "ControlTemplate")))
            {
                var stil = (string?)e.Attribute("Style") ?? "";
                if (stil.Contains("Knopf.Titel", StringComparison.Ordinal) || stil.Contains("Knopf.Schliessen", StringComparison.Ordinal)) continue;
                var hilfe = e.Attributes().Any(a => a.Name.LocalName == "Hilfe.Text") || e.Elements().Any(c => c.Name.LocalName.EndsWith(".Hilfe.Text", StringComparison.Ordinal))
                    || stileMitHilfe.Contains(stil);
                var alt = e.Attributes().Any(a => a.Name.LocalName is "ToolTip" or "ToolTipService.ToolTip")
                    || e.Elements().Any(c => c.Name.LocalName.EndsWith(".ToolTip", StringComparison.Ordinal));
                var name = (string?)e.Attribute(x + "Name") ?? (string?)e.Attribute("AutomationProperties.Name") ?? (string?)e.Attribute("Content")
                    ?? (string?)e.Attribute("Command") ?? (string?)e.Attribute("Style") ?? "Zeile " + ((IXmlLineInfo)e).LineNumber;
                var kennung = rel + ": " + name;
                gezaehlt[kennung] = gezaehlt.GetValueOrDefault(kennung) + 1;
                if (gezaehlt[kennung] > 1) kennung += " #" + gezaehlt[kennung];
                liste.Add((kennung, hilfe, alt));
            }
        }
        return liste;
    }

    /// <summary>
    /// Knöpfe, die noch keinen Hilfetext haben. Die Liste darf nur schrumpfen: Ein neuer Knopf ohne Hilfe.Text fällt auf, und wer
    /// einem Knopf hier einen Text gibt, nimmt ihn aus der Liste. Seit Stufe 2 leer: Jeder Knopf, Halteknopf und CheckBox-Schalter
    /// in Client und Verwaltungsteil hat einen Hilfetext (44 Einträge aus Stufe 1, dazu die neu erfassten).
    /// </summary>
    private static readonly string[] OhneHilfe = [];
    [Test]
    public static void Hilfe_Waechter_KnoepfeOhneHilfetextNurAusDerListe()
    {
        var knoepfe = Knoepfe();
        Pruefe.Wahr(knoepfe.Count > 60, "die Suche findet die Knöpfe des Clients (" + knoepfe.Count + ")");
        var fehlend = knoepfe.Where(k => !k.Hilfe && !k.AlterToolTip).Select(k => k.Kennung).ToList();
        var neu = fehlend.Except(OhneHilfe, StringComparer.Ordinal).ToList();
        var erledigt = OhneHilfe.Except(fehlend, StringComparer.Ordinal).ToList();
        Pruefe.Gleich("", string.Join("\n", neu), "Knöpfe ohne Hilfe.Text, die nicht in der Liste stehen (neue Knöpfe brauchen Hilfe.Text)");
        Pruefe.Gleich("", string.Join("\n", erledigt), "Einträge der Liste, die inzwischen einen Hilfetext haben (aus der Liste nehmen)");
        Pruefe.Gleich(OhneHilfe.Length, OhneHilfe.Distinct(StringComparer.Ordinal).Count(), "keine doppelten Einträge");
    }

    [Test]
    public static void Hilfe_BestehendeToolTipsAnKnoepfen_SindUmgestellt()
    {
        var alt = Knoepfe().Where(k => k.AlterToolTip).Select(k => k.Kennung).ToList();
        Pruefe.Gleich("", string.Join("\n", alt), "Knöpfe mit altem ToolTip statt Hilfe.Text");
        // Stile mit ToolTip-Settern an Knöpfen (Funkfeld) gehen denselben Weg.
        var funkfeld = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster", "FunkfeldFenster.xaml"));
        var kopf = funkfeld[funkfeld.IndexOf("x:Key=\"Kachelkopf\"", StringComparison.Ordinal)..];
        kopf = kopf[..kopf.IndexOf("</Style>", StringComparison.Ordinal)];
        Pruefe.Falsch(kopf.Contains("<Setter Property=\"ToolTip\"", StringComparison.Ordinal), "Funkfeld: Setter ToolTip im Knopfstil Kachelkopf umgestellt");
        Pruefe.Wahr(kopf.Contains("<Setter Property=\"gd:Hilfe.Text\"", StringComparison.Ordinal), "Kachelkopf mit Hilfetext");
    }
}
