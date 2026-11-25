// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// 0.11.1 (Sarevoc 12.10.2026, nach dem ersten Test von 0.11.0 am echten PC): Unter Einstellungen > Senden stehen die Schiebeschalter der Klangmodule mit
/// ihren kurzen Sätzen und die Funkschwelle (Pegelbalken mit Griffen, "Einmessen", "Genau einstellen ...") wieder offen, ohne Aufklappen; die
/// Klappbereiche heißen "Erweitert:" mit dem Wort fett vorn. Geprüft: was offen steht und was unter Erweitert, bei 640, 900 und 1100 und bei 175 %
/// nichts abgeschnitten und die Glasschatten frei, Kontrast in Anthrazit, die Kopfzeilen aller Klappbereiche (seit 0.11.9 sieben), die Zugriffstasten zu und offen,
/// und ein Wächter, der das alte Wort in Oberfläche, Texten, Quelltext, Handbuch und LIESMICH sucht. Der Klassenname steht nach
/// OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheUmbauErweitertTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Das Wort, das seit 0.11.1 nirgends mehr stehen darf, wo man es liest (Sarevoc: "das Wort ... kommt weg").</summary>
    private const string AltesWort = "Feinheiten";

    /// <summary>
    /// Linke Spalte unter Senden, ohne Aufklappen und in dieser Reihenfolge; mit der rechten zusammen die Reihenfolge von 0.9.3 (Spalte Senden der
    /// Karte Klangmodule).
    /// </summary>
    private static readonly string[] Links = ["Echounterdrückung automatisch", "Echoschutz", "Rauschunterdrückung"];

    /// <summary>Rechte Spalte: automatische Lautstärke, Sprachaktivierung, Mindest-Funklautstärke.</summary>
    private static readonly string[] Rechts = ["Automatische Lautstärke", "Sprachaktivierung", "Mindest-Funklautstärke"];

    /// <summary>Darunter über beide Spalten die Funkschwelle: Pegelbalken mit Griffen, dann "Genau einstellen ...".</summary>
    private static readonly string[] Funkschwelle = ["Eingangspegel", "Sprachaktivierung genau einstellen"];

    /// <summary>Die kurzen Sätze unter den Schaltern und am Balken, offen zu sehen.</summary>
    private static readonly string[] Saetze =
    [
        AudioViewModel.EchounterdrueckungErklaerung, AudioViewModel.EchoschutzErklaerung, AudioViewModel.RauschunterdrueckungErklaerung,
        AudioViewModel.AutomatikErklaerung, AudioViewModel.SprachaktivierungErklaerung, AudioViewModel.MindestErklaerung,
        EinstellungenViewModel.GriffeSatz, EinstellungenViewModel.GenauEinstellenSatz,
    ];

    /// <summary>Die übrigen Knöpfe unter Senden, ebenso ohne Aufklappen zu sehen.</summary>
    private static readonly string[] Knoepfe = ["Echounterdrückung an", "Echounterdrückung aus", "Rauschfilter Standard", "Rauschfilter Stark", "Mindestlautstärke einmessen"];

    private static readonly bool[] ZuUndOffen = [false, true];

    /// <summary>Was nur unter Senden > Erweitert steht (Namen der Elemente).</summary>
    private static readonly string[] NurErweitert = ["FestEinmessenKnopf", "MithoerenSchalter", "WaermeRegler", "KlarheitRegler", "SchaerfeRegler", "KlangfarbeZurueck"];

    private static ContentPresenter Seite(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);

    private static T Teil<T>(FrameworkElement wurzel, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(wurzel).FirstOrDefault(e => e.Name == name) ?? throw new PruefungException(name + " fehlt");

    /// <summary>Die Einstellungen wie nach dem Einrichten, verbunden mit dem Beispielabend, Audio läuft, Katalog geladen.</summary>
    private static (Hauptfenster Haupt, HauptViewModel Modell, FunkfeldViewModel Funkfeld, AudioViewModel Audio) Aufbauen(Aufbau a, List<Window> fenster,
        out IDisposable schluessel)
    {
        schluessel = OberflaecheStartseiteTests.Einrichten(a);
        OberflaecheStartseiteTests.SprechtasteBelegen(a);
        a.Tastenquelle = new Maschine.TastenQuelleAttrappe(Funk.Klang.Eingabe.EingabeArt.Tastatur, Funk.Klang.Eingabe.EingabeArt.Maus);
        var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
        audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        h.MeinProfil.LadenAsync().GetAwaiter().GetResult();
        h.AktiverBereich = Seiten.Einstellungen(h);
        h.Takt();
        var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
        fenster.Add(haupt);
        return (haupt, h, funkfeld, audio);
    }

    /// <summary>Ein Ausschnitt um den Abschnitt Senden über die Breite der Karte, aus einem Bild der ganzen Seite in Skala 1.</summary>
    private static CroppedBitmap SendenAusschnitt(Hauptfenster haupt, BitmapSource ganz)
    {
        var wurzel = Bildlauf.Wurzel(haupt);
        var seite = Seite(haupt);
        var karte = Teil<Border>(seite, "AbschnitteKarte");
        var rk = karte.TransformToAncestor(wurzel).TransformBounds(new Rect(karte.RenderSize));
        var abschnitt = Teil<StackPanel>(seite, "AbschnittSenden");
        var r = abschnitt.TransformToAncestor(wurzel).TransformBounds(new Rect(abschnitt.RenderSize));
        var links = (int)Math.Max(0, Math.Floor(rk.Left - 8));
        var oben = (int)Math.Max(0, Math.Floor(r.Top - 14));
        return new CroppedBitmap(ganz, new Int32Rect(links, oben, (int)Math.Min(ganz.PixelWidth - links, Math.Ceiling(rk.Width + 16)),
            (int)Math.Min(ganz.PixelHeight - oben, Math.Ceiling(r.Height + 28))));
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Senden offen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sarevoc 12.10.2026: Senden zeigt ohne Aufklappen links Echounterdrückung (Automatisch, An, Aus), Echoschutz und Rauschunterdrückung mit Standard
    /// und Stark, rechts automatische Lautstärke, Sprachaktivierung und Mindest-Funklautstärke, darunter über beide die Funkschwelle (Pegelbalken mit
    /// seinen Griffen, "Einmessen", "Genau einstellen ..."), je mit dem kurzen Satz; nichts davon steht im Klappbereich. Unter Erweitert (zu) bleiben feste Verstärkung, Mithören und
    /// Klangfarbe. Bei 640, 900 und 1100 und bei 175 % nichts abgeschnitten, Glasschatten frei; Kontrast in Anthrazit; die Zugriffstasten der
    /// zugeklappten Seite eindeutig, mit R für die Rauschunterdrückung. Ist die automatische Lautstärke aus, sagt ein Satz, wo die feste Verstärkung steht.
    /// </summary>
    [Test]
    public static void Erweitert_Senden_SchalterUndFunkschwelleOhneAufklappen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var mass = new List<string>();
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
                var (haupt, h, funkfeld, audio) = Aufbauen(a, fenster, out var schluessel);
                using var k = schluessel;
                var voreinstellung = satz.Stellung == Farbvorschlaege.Voreinstellung;
                double[] breiten = voreinstellung ? [640, 900, 1100] : [1100];
                foreach (var breite in breiten)
                {
                    var bild = Ganz(haupt, breite);
                    var seite = Seite(haupt);
                    var was = T($"{satz.Name}, {breite:0}");
                    var senden = Teil<StackPanel>(seite, "AbschnittSenden");
                    var klapp = Teil<Expander>(senden, "SendenFeinheiten");
                    Pruefe.Falsch(Bildlauf.Nachfahren<Expander>(seite).Any(x => x.IsExpanded), was + ": alle Klappbereiche zu");
                    var drinnen = Bildlauf.Nachfahren<FrameworkElement>(klapp).ToHashSet();
                    FrameworkElement Element(string name) =>
                        Bildlauf.Nachfahren<FrameworkElement>(senden).FirstOrDefault(e => AutomationProperties.GetName(e) == name && ZuSehen(e))
                        ?? throw new PruefungException(was + ": unter Senden ohne Aufklappen nicht zu sehen: " + name);
                    double Oben(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), senden).Y;
                    foreach (var spalte in new[] { Links, Rechts, Funkschwelle })
                    {
                        var elemente = spalte.Select(Element).ToList();
                        Pruefe.Falsch(elemente.Any(drinnen.Contains), was + ": nichts davon im Klappbereich");
                        for (var i = 1; i < elemente.Count; i++)
                            Pruefe.Wahr(Oben(elemente[i - 1]) < Oben(elemente[i]), was + ": " + spalte[i - 1] + " über " + spalte[i]);
                    }
                    // Die Funkschwelle unter beiden Spalten; nebeneinander steht rechts neben dem Echo die Lautstärke.
                    var unten = Links.Concat(Rechts).Select(Element).Max(e => Oben(e) + e.ActualHeight);
                    Pruefe.Wahr(Oben(Element(Funkschwelle[0])) > unten, was + ": die Funkschwelle unter beiden Spalten");
                    foreach (var name in Knoepfe) _ = Element(name);
                    foreach (var text in Saetze) Pruefe.Wahr(Sichtbar(senden, text), was + ": Satz zu sehen: " + Kurz(text));
                    Pruefe.Wahr(Sichtbar(senden, EinstellungenViewModel.AutomatikSenden), was + ": der Kasten Läuft von selbst bleibt");

                    // Die Funkschwelle: der Balken mit dem Korridor (Griffe für Mindest-Funklautstärke und Obergrenze), daneben Einmessen.
                    var pegel = Teil<Pegelbalken>(senden, "AudioPegel");
                    Pruefe.Wahr(ZuSehen(pegel) && ReferenceEquals(pegel.Korridor, audio.Korridor) && pegel.ActualWidth >= 200,
                        T($"{was}: Pegelbalken mit Korridor, breit genug für die Griffe ({pegel.ActualWidth:0.0})"));
                    Pruefe.Wahr(Teil<Button>(senden, "AudioEinmessenKnopf").TranslatePoint(new Point(0, 0), senden).X > pegel.TranslatePoint(new Point(0, 0), senden).X,
                        was + ": Einmessen rechts neben dem Balken");

                    // Unter Erweitert, zugeklappt nicht zu sehen.
                    Pruefe.Falsch(Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => NurErweitert.Contains(e.Name) && ZuSehen(e)), was + ": Erweitert ist zu");

                    var spalten = Teil<Spaltenwechsel>(senden, "SendenSpalten");
                    mass.Add(T($"MASS {was}: Senden zu {senden.ActualHeight:0.0} hoch, Spalten {spalten.ActualWidth:0.0} breit, nebeneinander {spalten.Nebeneinander}, Balken {pegel.ActualWidth:0.0}"));
                    Pruefe.Gleich(spalten.ActualWidth >= EinstellungenViewModel.SendenZweiSpaltenAb, spalten.Nebeneinander, was + ": Spaltenwechsel an der Grenze");
                    if (breite == 1100) Pruefe.Wahr(spalten.Nebeneinander, was + ": zwei Spalten");
                    if (spalten.Nebeneinander)
                        Pruefe.Wahr(Element(Rechts[0]).TranslatePoint(new Point(0, 0), senden).X > Element(Links[0]).TranslatePoint(new Point(0, 0), senden).X + 100,
                            was + ": rechts die Lautstärke neben dem Echo");
                    if (breite == 640) Pruefe.Falsch(spalten.Nebeneinander, was + ": untereinander");

                    Pruefe.Wahr(NichtsAbgeschnitten(seite, was) >= 60, was + ": genug Elemente geprüft");
                    _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
                    if (breite == 1100) _ = KontrastPruefen(senden, satz, was + ", Senden", mindestens: 20);
                    if (voreinstellung)
                    {
                        Bildablage.Ablegen(SendenAusschnitt(haupt, bild), T($"0111-senden-zu-{breite:0}"));
                        // 175 %: dieselbe Fläche in Bildpunkten mal 1,75, nichts abgeschnitten, Schatten frei.
                        _ = Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, bild.PixelHeight));
                        _ = NichtsAbgeschnitten(seite, was + ", 175 %");
                        _ = OberflaecheStartseiteTests.SchattenFrei(seite, was + ", 175 %");
                    }
                }

                // Senden mit offenem Erweitert, eine Sache selbst gestellt: ebenso bei 640, 900 und 1100 und bei 175 % nichts abgeschnitten, Schatten frei
                // (Befund beim Bau: ohne Luft links schnitt der gerundete Rand des Abschnitts den Schatten von "Automatisch" an).
                if (voreinstellung)
                {
                    audio.Klangfarbe.Waerme = 2;
                    h.Einstellungen.AbschnittSenden!.Feinheiten.Offen = true;
                    foreach (var breite in breiten)
                    {
                        var bild = Ganz(haupt, breite);
                        var seite = Seite(haupt);
                        var was = T($"{satz.Name}, Erweitert offen, {breite:0}");
                        var spalten = Teil<Spaltenwechsel>(seite, "SendenErweitertSpalten");
                        mass.Add(T($"MASS {was}: Senden {Teil<StackPanel>(seite, "AbschnittSenden").ActualHeight:0.0} hoch, Erweitert {spalten.ActualWidth:0.0} breit, nebeneinander {spalten.Nebeneinander}"));
                        Pruefe.Wahr(Bildlauf.Nachfahren<FrameworkElement>(seite).Where(e => NurErweitert.Contains(e.Name)).All(ZuSehen), was + ": Erweitert zu sehen");
                        _ = NichtsAbgeschnitten(seite, was);
                        _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
                        Bildablage.Ablegen(SendenAusschnitt(haupt, bild), T($"0111-senden-erweitert-offen-{breite:0}"));
                        _ = Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, bild.PixelHeight));
                        _ = NichtsAbgeschnitten(seite, was + ", 175 %");
                        _ = OberflaecheStartseiteTests.SchattenFrei(seite, was + ", 175 %");
                    }
                    h.Einstellungen.AbschnittSenden.Feinheiten.Offen = false;
                    audio.Klangfarbe.Waerme = 0;
                }

                // Die zugeklappte Seite: Zugriffstasten eindeutig, R (Rauschunterdrückung) jetzt ohne Aufklappen.
                _ = Ganz(haupt, 1100);
                var tasten = OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt));
                Pruefe.Gleich("", string.Join(" | ", OberflaecheHauptfensterTests.Doppelte(tasten, "Einstellungen zu")), satz.Name + ": Zugriffstasten eindeutig, zugeklappt");
                Pruefe.Wahr(tasten.Any(t => t.Taste == 'R'), satz.Name + ": Alt+R schaltet die Rauschunterdrückung ohne Aufklappen");

                // Automatische Lautstärke aus: der Satz sagt, wo die feste Verstärkung steht; an: kein Satz.
                audio.Lautstaerke.Automatisch = false;
                _ = Ganz(haupt, 1100);
                Pruefe.Wahr(Sichtbar(Teil<StackPanel>(Seite(haupt), "AbschnittSenden"), EinstellungenViewModel.AutomatikAusSatz), satz.Name + ": Satz bei Automatik aus");
                audio.Lautstaerke.Automatisch = true;
                _ = Ganz(haupt, 1100);
                Pruefe.Falsch(Sichtbar(Teil<StackPanel>(Seite(haupt), "AbschnittSenden"), EinstellungenViewModel.AutomatikAusSatz), satz.Name + ": kein Satz bei Automatik an");
                funkfeld.Dispose();
            }
            Messdateien.Schreiben("erweitert-mass.txt", mass);
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Kopfzeilen, Zugriffstasten, Texte im Fenster
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Alle acht Klappbereiche (Ich, Tasten, Empfangen, Senden, Funkfeld und Darstellung, seit 0.11.9 Sprecheranzeige, seit 0.12.0 Meldungen, Fehlersuche): Die Kopfzeile beginnt mit "Erweitert:" in fett,
    /// dann in normaler Schrift der Stand in Klammern und der Inhalt, etwa "Erweitert: (1 selbst gestellt) Feste Verstärkung, Mithören, Klangfarbe";
    /// Bildschirmleser bekommen die ganze Zeile. Zu und offen eindeutige Zugriffstasten; offen die des Plans. Kein sichtbarer Text, kein Hovertext und
    /// kein Name für Bildschirmleser auf Start, Einstellungen (alles offen) und der Seite Sprachaktivierung enthält das alte Wort.
    /// </summary>
    [Test]
    public static void Erweitert_Kopfzeilen_FettZugriffstastenUndTexteImFenster()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var (haupt, h, funkfeld, audio) = Aufbauen(a, fenster, out var schluessel);
            using var k = schluessel;
            audio.Klangfarbe.Waerme = 1;
            foreach (var offen in ZuUndOffen)
            {
                foreach (var x in h.Einstellungen.Abschnitte) x.Feinheiten.Offen = offen;
                _ = Ganz(haupt, 1100);
                var seite = Seite(haupt);
                var wo = offen ? "offen" : "zu";
                var klappen = Bildlauf.Nachfahren<Expander>(seite).Where(e => e.Header is Feinheiten).ToList();
                Pruefe.Gleich("ich tasten empfangen senden funkfeld sprecheranzeige meldungen fehlersuche", string.Join(" ", klappen.Select(e => ((Feinheiten)e.Header).Kennung)),
                    wo + ": acht Klappbereiche (seit 0.11.9 mit der Sprecheranzeige, seit 0.12.0 mit den Meldungen)");
                foreach (var e in klappen)
                {
                    var f = (Feinheiten)e.Header;
                    var kopf = Bildlauf.Nachfahren<ToggleButton>(e).First(t => t.Name == "Kopf");
                    // Seit 0.11.4 eine Karte wie in Windows 11 (Sarevoc: "B"): oben der Titel "Erweitert:" fett, darunter Stand und Inhalt in normaler Schrift.
                    var text = Bildlauf.Nachfahren<TextBlock>(kopf).Single(t => t.Name == "KopfText");
                    var rest = Bildlauf.Nachfahren<TextBlock>(kopf).Single(t => t.Name == "KopfRest");
                    Pruefe.Gleich(f.Kopfzeile, text.Text + " " + rest.Text, wo + ", " + f.Abschnitt + ": Titel und Beschreibung");
                    Pruefe.Wahr(f.Kopfzeile.StartsWith("Erweitert: (", StringComparison.Ordinal), wo + ", " + f.Abschnitt + ": beginnt mit Erweitert: und dem Stand");
                    Pruefe.Wahr(text.Text == "Erweitert:" && text.FontWeight == FontWeights.Bold && rest.FontWeight != FontWeights.Bold,
                        wo + ", " + f.Abschnitt + ": Erweitert: fett, der Rest nicht");
                    Pruefe.Gleich(f.Kopfzeile, AutomationProperties.GetName(kopf), wo + ", " + f.Abschnitt + ": Name für Bildschirmleser");
                    // Befund der Prüfung: Der Klappbereich selbst ist für Bildschirmleser eine Gruppe, und WPF nennt sie nach dem Text ihres Headers.
                    // Seit der Header der Behälter ist, hieß sie sonst nach dem Typnamen, samt dem alten Wort.
                    Pruefe.Gleich(f.Kopfzeile, UIElementAutomationPeer.CreatePeerForElement(e)?.GetName(), wo + ", " + f.Abschnitt + ": Name der Gruppe für Bildschirmleser");
                    Pruefe.Wahr(ZuSehen(text) && ZuSehen(rest) && LayoutInformation.GetLayoutClip(rest) is null, wo + ", " + f.Abschnitt + ": ganz zu sehen");
                }
                Pruefe.Wahr(Sichtbar(seite, "(1 selbst gestellt) " + EinstellungenViewModel.InhaltSendenErweitert), wo + ": Senden zählt die Wärme");
                var tasten = OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt));
                Pruefe.Gleich("", string.Join(" | ", OberflaecheHauptfensterTests.Doppelte(tasten, "Einstellungen " + wo)), wo + ": Zugriffstasten eindeutig");
                if (offen)
                    Pruefe.Wahr(tasten.Select(t => t.Taste).ToHashSet().IsSupersetOf(['S', 'E', 'F', 'R', 'D', 'K', 'G', 'Z', 'N', 'A', 'V']),
                        "offen: die Zugriffstasten des Plans (" + string.Join("", tasten.Select(t => t.Taste).Distinct().Order()) + ")");
            }

            // Kein Text im Fenster mit dem alten Wort: Einstellungen mit allem offen, Start, Seite Sprachaktivierung (je mit Statuszeile und Fußzeile).
            var funde = new List<string>();
            foreach (var (bereich, name) in new[] { (Seiten.Einstellungen(h), "Einstellungen"), (Seiten.Start(h), "Start"),
                         (h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung), "Sprachaktivierung") })
            {
                h.AktiverBereich = bereich;
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 3600));
                var gezaehlt = 0;
                foreach (var (stelle, text) in Texte(Bildlauf.Wurzel(haupt)))
                {
                    gezaehlt++;
                    if (text.Contains(AltesWort, StringComparison.OrdinalIgnoreCase)) funde.Add(name + ", " + stelle + ": " + Kurz(text));
                }
                Pruefe.Wahr(gezaehlt >= 50, name + ": genug Texte gelesen (" + gezaehlt + ")");
            }
            Pruefe.Gleich("", string.Join("\n", funde), "kein Text im Fenster mit dem alten Wort");
            funkfeld.Dispose();
        });
    }

    /// <summary>Alles, was im Fenster gelesen werden kann: Texte, Inhalte, Hovertexte, Sperrgründe und Namen für Bildschirmleser.</summary>
    private static IEnumerable<(string Stelle, string Text)> Texte(DependencyObject wurzel)
    {
        foreach (var e in Bildlauf.Nachfahren<FrameworkElement>(wurzel))
        {
            var name = e.GetType().Name + " " + e.Name;
            if (e is TextBlock t) yield return (name, Inhalt(t));
            if (e is AccessText z) yield return (name, z.Text);
            if (e is ContentControl { Content: string inhalt }) yield return (name, inhalt);
            if (e is HeaderedContentControl { Header: string kopf }) yield return (name, kopf);
            // Ein Header, der kein Text ist: Bildschirmleser bekommen, was die Gruppe daraus macht (Befund der Prüfung).
            if (e is HeaderedContentControl { Header: not null and not string and not UIElement } && UIElementAutomationPeer.CreatePeerForElement(e)?.GetName() is { Length: > 0 } gruppe)
                yield return (name + " (Gruppe)", gruppe);
            if (e.ToolTip is string tipp) yield return (name + " (ToolTip)", tipp);
            foreach (var (art, text) in new[] { ("Name", AutomationProperties.GetName(e)), ("Hilfe", Glasdesign.Hilfe.GetText(e)), ("Gesperrt", Glasdesign.Hilfe.GetGesperrt(e)) })
                if (!string.IsNullOrEmpty(text)) yield return (name + " (" + art + ")", text);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Wächter
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Wächter (Sarevoc 12.10.2026: "Das Wort ... kommt weg, wir nennen das nun Erweitert"): Das alte Wort steht in keiner Konstanten des Programms, in keiner
    /// Zeichenkette im C#-Quelltext, in keinem Text oder Attributwert der XAML-Dateien (auch nicht im StringFormat einer Bindung), nicht im Handbuch mit
    /// Glossar und nicht in den LIESMICH beider Pakete. Was die Modelle zusammensetzen (Kopfzeile, Hovertexte, Sätze, Meldezeile), prüft der Test
    /// ebenso. Erlaubt bleiben Bezeichner im Code (Klasse Feinheiten, x:Name, Bindungspfade) und der Schlüssel offeneFeinheiten in der Datei der
    /// Einstellungen, damit gemerkte Klappzustände gelten; Kommentare zählen nicht.
    /// </summary>
    [Test]
    public static void Erweitert_Waechter_KeinAltesWortInTextenQuelltextUndDoku()
    {
        var funde = new List<string>();
        var repo = EinrichtungTests.Repo();
        string Relativ(string pfad) => Path.GetRelativePath(repo, pfad);

        // 1. Konstanten des Programms (Hovertexte, Sätze, Meldungen).
        foreach (var typ in typeof(Feinheiten).Assembly.GetTypes())
            foreach (var feld in typ.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (feld is { IsLiteral: true } && feld.FieldType == typeof(string) && feld.GetRawConstantValue() is string wert && wert.Contains(AltesWort, StringComparison.Ordinal))
                    funde.Add("Konstante " + typ.Name + "." + feld.Name);

        // 2. Zeichenketten im C#-Quelltext, ohne Kommentarzeilen.
        var zeichenkette = new Regex(@"""(?:[^""\\]|\\.)*""", RegexOptions.CultureInvariant);
        var gelesen = 0;
        foreach (var datei in Directory.EnumerateFiles(Path.Combine(repo, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (datei.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || datei.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            gelesen++;
            var nr = 0;
            foreach (var zeile in File.ReadLines(datei))
            {
                nr++;
                if (zeile.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (Match m in zeichenkette.Matches(zeile))
                    if (m.Value.Contains(AltesWort, StringComparison.Ordinal)) funde.Add(Relativ(datei) + ":" + nr + ": " + Kurz(m.Value));
            }
        }
        Pruefe.Wahr(gelesen >= 100, "genug C#-Dateien gelesen (" + gelesen + ")");

        // 3. XAML: Texte zwischen den Marken und Attributwerte, ohne Kommentare. In Markuperweiterungen ({Binding ...}) nur, was in Hochkommas steht.
        var kommentar = new Regex("<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var attribut = new Regex(@"([\w:.]+)\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);
        var zwischen = new Regex(@">([^<>]+)<", RegexOptions.CultureInvariant);
        var hochkomma = new Regex("'([^']*)'", RegexOptions.CultureInvariant);
        var xaml = 0;
        foreach (var datei in Directory.EnumerateFiles(Path.Combine(repo, "src"), "*.xaml", SearchOption.AllDirectories))
        {
            xaml++;
            var text = kommentar.Replace(File.ReadAllText(datei), "");
            foreach (Match m in attribut.Matches(text))
            {
                var (name, wert) = (m.Groups[1].Value, m.Groups[2].Value);
                if (!wert.Contains(AltesWort, StringComparison.Ordinal) || name is "x:Name" or "x:Key") continue;
                if (!wert.StartsWith('{') || hochkomma.Matches(wert).Any(h => h.Groups[1].Value.Contains(AltesWort, StringComparison.Ordinal)))
                    funde.Add(Relativ(datei) + ": " + name + "=\"" + Kurz(wert) + "\"");
            }
            foreach (Match m in zwischen.Matches(text))
                if (m.Groups[1].Value.Contains(AltesWort, StringComparison.Ordinal)) funde.Add(Relativ(datei) + ": Text " + Kurz(m.Groups[1].Value.Trim()));
        }
        Pruefe.Wahr(xaml >= 10, "genug XAML-Dateien gelesen (" + xaml + ")");

        // 4. Handbuch mit Glossar und die LIESMICH beider Pakete: gar nicht, auch nicht klein geschrieben.
        var doku = Directory.EnumerateFiles(Path.Combine(repo, "docs", "handbuch"), "*.md")
            .Concat(Directory.EnumerateFiles(Path.Combine(repo, "tools", "paket"), "LIESMICH*.txt")).ToList();
        Pruefe.Wahr(doku.Count >= 5 && doku.Any(d => d.EndsWith("90-glossar.md", StringComparison.Ordinal)) && doku.Count(d => Path.GetFileName(d).StartsWith("LIESMICH", StringComparison.Ordinal)) == 2,
            "Handbuch, Glossar und zwei LIESMICH gefunden: " + string.Join(", ", doku.Select(Path.GetFileName)));
        foreach (var datei in doku)
        {
            var nr = 0;
            foreach (var zeile in File.ReadLines(datei))
            {
                nr++;
                if (zeile.Contains(AltesWort, StringComparison.OrdinalIgnoreCase)) funde.Add(Relativ(datei) + ":" + nr);
            }
        }

        // 5. Was die Modelle zusammensetzen: Kopfzeile, Hovertexte und Sätze je Abschnitt, die Meldezeile ohne Meldekanal.
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
        audio.Klangfarbe.Waerme = 2;
        foreach (var x in h.Einstellungen.Abschnitte)
        {
            var f = x.Feinheiten;
            foreach (var text in new[] { f.Kopfzeile, f.Kopfrest, f.KopfHilfe, f.ZurueckHilfe, f.StandSatz, f.Inhalt, x.Automatik ?? "", x.Satz ?? "" })
                if (text.Contains(AltesWort, StringComparison.OrdinalIgnoreCase)) funde.Add(x.Name + ": " + Kurz(text));
        }
        var melden = Meldenetzwahl.Hindernis(null, FunkDienstAttrappe.Beispiel()) ?? "";
        Pruefe.Gleich("Melden: kein Meldekanal (Tasten > Erweitert)", melden, "die Meldezeile nennt den neuen Weg");
        funkfeld.Dispose();

        Pruefe.Gleich("", string.Join("\n", funde), "das alte Wort steht nirgends mehr, wo man es liest");
    }
}
