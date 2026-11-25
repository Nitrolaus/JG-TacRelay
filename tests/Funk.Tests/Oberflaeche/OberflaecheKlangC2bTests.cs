// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Eingabe;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Maschine;
using Funk.Tests.Verarbeitung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;
using Path = System.IO.Path;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Klangstufe C2b in der Oberfläche (KLANGSTUFE-C-ENTWURF.md im Planungsordner, Q1, Q2, Q8, Q13, Q26, Q31 bis Q36, "Mikro stumm") und die Nacharbeit aus
/// der Gegenprüfung C1 an der Oberfläche (output\test\20261004_pruefung-c1.md, M2 und N2). Gezeichnet ohne Fensterhandle (<see cref="Bildlauf"/>),
/// Klangmaschine und Funkbetrieb als Attrappen.
/// </summary>
internal static class OberflaecheKlangC2bTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Die Stimme gilt als gelernt, am Ziel der Pegelstufe (-23 dBFS).</summary>
    private static void StimmeGelernt(Aufbau a) => a.Maschine.Korridor = new KorridorStand(Pegelstufe.ZielDbfs, float.NaN, float.NaN);

    // ------------------------------------------------------------------------------------------------------------------------------
    // Gegenprüfung C1, M2: Die Zone folgt auch langsamem Ziehen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// M2: Der Balken spannt -90 bis 0 dBFS; bei 450 Einheiten Breite ist ein Bildpunkt 0,2 dB. Vierzig Mausschritte zu je einem Bildpunkt
    /// verschieben die Zone um 8 dB, wie die Maus; vorher rundete jeder Teilschritt auf 0,5 dB und verfiel (Untergrenze blieb stehen). Gezogen wird
    /// nach links (leiser): Ab Werk steht die Obergrenze schon an ihrer höchsten Stelle (Stimme minus 6 dB), nach rechts ginge die Zone nicht.
    /// </summary>
    [Test]
    public static void C2b_M2_ZoneAmPegelbalkenFolgtLangsamemZiehen()
    {
        Bildlauf.Ausfuehren(ohneFenster =>
        {
            using var a = new Aufbau();
            StimmeGelernt(a);
            var k = new KorridorViewModel(a.Maschine, a.Ablage);
            var b = new Pegelbalken { Width = 450, Height = 36, PegelDb = -30, SchwelleDb = -40, Sendet = true, Korridor = k };
            _ = OberflaecheHilfeTests.Zeichnen(b);
            var mitte = b.ActualHeight / 2;
            var unten = k.UntergrenzeDb;
            var oben = k.ObergrenzeDb;
            var start = b.XFuer((unten + oben) / 2);
            Pruefe.Gleich("Zone", b.TeilAn(new Point(start, mitte)), "in der Zone gegriffen");
            Pruefe.Wahr(b.Druecken(new Point(start, mitte)), "gedrückt");
            for (var i = 1; i <= 40; i++) b.Bewegen(new Point(start - i, mitte));
            b.Loslassen();
            var maus = b.DbFuer(start - 40) - b.DbFuer(start);
            Console.WriteLine(T($"  Balken: Maus {maus:0.0} dB in 40 Schritten, Untergrenze {unten:0.0} -> {k.UntergrenzeDb:0.0}, Obergrenze {oben:0.0} -> {k.ObergrenzeDb:0.0}"));
            PruefeWert.Nahe(unten + maus, k.UntergrenzeDb, 0.5, "langsam gezogen: die Untergrenze folgt der Maus");
            PruefeWert.Nahe(oben + maus, k.ObergrenzeDb, 0.5, "die Obergrenze ebenso");

            // Schnell zurück in einem Schritt: wieder am Anfang, ohne Überholen.
            var x = b.XFuer((k.UntergrenzeDb + k.ObergrenzeDb) / 2);
            Pruefe.Wahr(b.Druecken(new Point(x, mitte)), "wieder gegriffen");
            b.Bewegen(new Point(x + 40, mitte));
            b.Loslassen();
            PruefeWert.Nahe(unten, k.UntergrenzeDb, 0.5, "in einem Schritt zurück");

            // Hin und her in kleinen Schritten: am Ende dort, wo die Maus steht.
            x = b.XFuer((k.UntergrenzeDb + k.ObergrenzeDb) / 2);
            Pruefe.Wahr(b.Druecken(new Point(x, mitte)), "dritter Griff");
            for (var i = 1; i <= 25; i++) b.Bewegen(new Point(x - i, mitte));
            for (var i = 24; i >= 10; i--) b.Bewegen(new Point(x - i, mitte));
            b.Loslassen();
            PruefeWert.Nahe(unten + b.DbFuer(x - 10) - b.DbFuer(x), k.UntergrenzeDb, 0.5, "hin und her: wo die Maus losließ");
        });
    }

    /// <summary>M2 auch in der Schwellengrafik: dieselbe Rechnung (SchwellenGrafik.cs, Zone), 40 Schritte zu je 0,2 dB nach unten.</summary>
    [Test]
    public static void C2b_M2_ZoneInDerSchwellengrafikFolgtLangsamemZiehen()
    {
        Bildlauf.Ausfuehren(ohneFenster =>
        {
            using var a = new Aufbau();
            StimmeGelernt(a);
            var s = a.Schwelle();
            s.Takt();
            var g = new SchwellenGrafik { Modell = s, Width = 640, Height = 360 };
            _ = OberflaecheHilfeTests.Zeichnen(g);
            var p = g.Pegelflaeche;
            var k = s.Korridor;
            var unten = k.UntergrenzeDb;
            var oben = k.ObergrenzeDb;
            var mitteDb = (unten + oben) / 2;
            var x = p.Left + 100;
            Pruefe.Gleich("Zone", g.GriffAn(new Point(x, SchwellenGrafik.DbZuY(mitteDb, p))), "in der Zone gegriffen");
            Pruefe.Wahr(g.Druecken(new Point(x, SchwellenGrafik.DbZuY(mitteDb, p))), "gedrückt");
            for (var i = 1; i <= 40; i++) g.Bewegen(new Point(x, SchwellenGrafik.DbZuY(mitteDb - (0.2 * i), p)));
            g.Loslassen();
            Console.WriteLine(T($"  Grafik: Untergrenze {unten:0.0} -> {k.UntergrenzeDb:0.0}"));
            PruefeWert.Nahe(unten - 8, k.UntergrenzeDb, 0.5, "langsam gezogen: die Untergrenze folgt der Maus");
            PruefeWert.Nahe(oben - 8, k.ObergrenzeDb, 0.5, "die Obergrenze ebenso");
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Gegenprüfung C1, N2: ehrliche Texte
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// N2: Seit klang-c1b liegt die Linie nach dem Einmessen knapp unter dem leisen Satz, nicht "etwas näher an der leisen Seite"; und "leiser wird
    /// nie gesendet" stimmt nicht (eine offene Phase bleibt bei leiserer Sprache offen, nach dem Einmessen gehen 83 % der Rahmen 12 dB leiserer
    /// Sprache hinaus). Ehrlich: Die Automatik stellt sich nie empfindlicher ein als diese Linie.
    /// </summary>
    [Test]
    public static void C2b_N2_TexteZurMindestFunklautstaerkeEhrlich()
    {
        const string Ehrlich = "Die Automatik stellt sich nie empfindlicher ein als";
        Pruefe.Wahr(KorridorViewModel.HilfeUntergrenze.StartsWith("Mindest-Funklautstärke. Untergrenze: Die Automatik stellt sich nie empfindlicher ein als diese Linie.",
            StringComparison.Ordinal), "Tooltip der linken Linie: " + KorridorViewModel.HilfeUntergrenze);

        using var a = new Aufbau();
        StimmeGelernt(a);
        var k = new KorridorViewModel(a.Maschine, a.Ablage);
        Pruefe.Wahr(k.StandText.StartsWith(Ehrlich, StringComparison.Ordinal), "Stand neben dem Knopf: " + k.StandText);
        a.Maschine.StartenAsync(null, null, CancellationToken.None).GetAwaiter().GetResult();
        k.Takt();
        k.EinmessenCommand.Execute(null);
        a.Maschine.MindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Fertig, 0, 3f, 15f, -26f, -38f);
        k.Takt();
        Pruefe.Wahr(k.EinmessText.StartsWith("Eingemessen. " + Ehrlich, StringComparison.Ordinal), "nach dem Einmessen: " + k.EinmessText);

        // Kein Text des Clients sagt mehr "nie gesendet" oder "etwas näher an die leise Seite"; der Knopf nennt die Linie knapp unter dem leisen Satz.
        var client = Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client");
        foreach (var datei in Directory.EnumerateFiles(client, "*.*", SearchOption.AllDirectories)
                     .Where(d => d.EndsWith(".cs", StringComparison.Ordinal) || d.EndsWith(".xaml", StringComparison.Ordinal))
                     .Where(d => !d.Contains(@"\bin\", StringComparison.Ordinal) && !d.Contains(@"\obj\", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(datei);
            Pruefe.Falsch(text.Contains("nie gesendet", StringComparison.Ordinal), "\"nie gesendet\" in " + Path.GetFileName(datei));
            Pruefe.Falsch(text.Contains("leise Seite", StringComparison.Ordinal), "\"leise Seite\" in " + Path.GetFileName(datei));
        }
        // Von 0.10.0 bis 0.11.1 (AP2, Q87) kein Einmessen auf Start: in Bereiche.xaml nur im Bereich Audio. Seit 0.11.2 wieder auf Start unter "Senden"
        // (StartEinmessenKnopf, Sarevoc 13.10.2026), mit demselben Hilfetext: zwei in Bereiche.xaml.
        foreach (var (datei, anzahl) in new[] { ("Bereiche.xaml", 2), ("Schwellenbereich.xaml", 1) })
        {
            var xaml = File.ReadAllText(Path.Combine(client, "Fenster", datei));
            var treffer = xaml.Split("gd:Hilfe.Text=\"{x:Static m:KorridorViewModel.HilfeEinmessen}\"").Length - 1;
            Pruefe.Gleich(anzahl, treffer, datei + ": Knopf zum Einmessen mit dem gemeinsamen Hilfetext");
        }
        var hilfe = typeof(KorridorViewModel).GetField("HilfeEinmessen")?.GetValue(null) as string ?? "";
        Pruefe.Wahr(hilfe.Contains("knapp unter den leisen Satz", StringComparison.Ordinal) && hilfe.Contains(Ehrlich, StringComparison.Ordinal),
            "Hilfetext des Knopfs: " + hilfe);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Hilfen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Für Funker Wörter statt dB (Grundsatz, Q13): keine Ziffern, kein "dB".</summary>
    private static void Woerter(string text, string was)
    {
        Pruefe.Falsch(text.Any(char.IsDigit) || text.Contains("dB", StringComparison.Ordinal), "Wörter statt Zahlen, " + was + ": " + text);
        Pruefe.Falsch(text.Any(z => z is '\u2013' or '\u2014' or '\u2212' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), "nur einfache Zeichen, " + was + ": " + text);
    }

    /// <summary>Der Satz der Meldezeile unter einem Schlüssel (mehrere Zeilen gleicher Zeit stehen in der Reihenfolge ihres Eintreffens).</summary>
    private static string Satz(Meldezeile zeile, string schluessel) => zeile.Zeilen.SingleOrDefault(z => z.Schluessel == schluessel)?.Text ?? "";

    private static IEnumerable<FrameworkElement> Benannt(DependencyObject wurzel, string name) =>
        Bildlauf.Nachfahren<FrameworkElement>(wurzel).Where(e => e.Name == name);

    /// <summary>Das Element des Felds <paramref name="platz"/> mit diesem Namen (aus der Vorlage der Kachel; sein eigener Datenkontext kann ein anderer sein).</summary>
    private static FrameworkElement ImFeld(FunkfeldFenster ff, int platz, string name) =>
        Benannt(Bildlauf.Wurzel(ff), name).First(e => Vorfahren(e).OfType<FrameworkElement>().Any(v => v.DataContext is KachelViewModel k && k.Platz == platz && !k.IstGlobal));

    private static IEnumerable<DependencyObject> Vorfahren(DependencyObject d)
    {
        for (var v = VisualTreeHelper.GetParent(d); v is not null; v = VisualTreeHelper.GetParent(v)) yield return v;
    }

    /// <summary>
    /// Sichtbar im Baum (ohne Fensterhandle ist <see cref="UIElement.IsVisible"/> immer falsch): es und alle Vorfahren bis unter das Fenster auf Visible
    /// (das nie gezeigte Fenster selbst steht auf Collapsed).
    /// </summary>
    private static bool Sichtbar(UIElement e) =>
        e.Visibility == Visibility.Visible && Vorfahren(e).OfType<UIElement>().Where(v => v is not Window).All(v => v.Visibility == Visibility.Visible);

    private static Rect Lage(FrameworkElement e, Visual wurzel) => e.TransformToAncestor(wurzel).TransformBounds(new Rect(e.RenderSize));

    /// <summary>Lage mit Kanal "Crew" (Netz 4, Feld 3) sendend, Kobold spricht dort, Falke in Kommando; wie <see cref="FunkDienstAttrappe.Beispiel"/>.</summary>
    private static Aufbau Beispiel(IOberflaechentakt? takt = null) => new(FunkDienstAttrappe.Beispiel(), takt: takt) { Jetzt = new DateTimeOffset(2026, 10, 4, 20, 52, 0, TimeSpan.FromHours(2)) };

    private static FunkerDaempfung Kobold(float db = -10f, DauersendUrteil urteil = DauersendUrteil.Echo) => new(3, urteil, db, Daempfungsworte.Wort(db), false, false);

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 1: gelbes "!" für das eigene Urteil
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Das eigene Urteil des Wächters kommt aus dem Melde-Thread über den Oberflächentakt an: gelbes "!" an der eigenen Sendeanzeige (Titelzeile des
    /// Funkfelds), Tooltip mit Ursache und Hinweis in Wörtern, in der Meldezeile mit "!" seit 0.11.0 ein kurzer Satz in einer Zeile
    /// (<see cref="DauersendViewModel.Meldetext"/>; bis dahin dieselben Wörter wie im Tooltip, die in einer Zeile nach etwa 30 Zeichen gekürzt
    /// wären). Ohne Urteil verschwinden beide. Ist die Auto Dauersend-Dämpfung aus, sagt der Tooltip, dass nur angezeigt wird. Seit 0.11.2 steht das
    /// Urteil nicht mehr in der Meldezeile (Meldezeile.Erlaubt); geprüft wird der kurze Satz selbst.
    /// </summary>
    [Test]
    public static void C2b_EigenesUrteil_ZeichenTooltipUndMeldezeile()
    {
        var takt = new WarteschlangenTakt();
        using var a = Beispiel(takt);
        using var f = a.Funkfeld();
        var zeile = new Meldezeile(() => a.Uhr);
        f.Meldezeile = zeile;
        takt.Abarbeiten();
        var d = f.Dauersend;
        Pruefe.Falsch(d.HatEigenesUrteil, "ohne Urteil kein Zeichen");

        a.Maschine.UrteilMelden(WaechterStand.Aus(DauersendUrteil.Echo));
        Pruefe.Falsch(d.HatEigenesUrteil, "aus dem Melde-Thread: erst über den Takt");
        takt.Abarbeiten();
        Pruefe.Wahr(d.HatEigenesUrteil, "Echo: gelbes Zeichen");
        Pruefe.Gleich(Daempfungsworte.Ursache(DauersendUrteil.Echo), d.EigeneHilfe, "Tooltip: Ursache und Hinweis aus dem Klangkern");
        Pruefe.Wahr(d.EigeneHilfe.Contains("Lautsprecher leiser", StringComparison.Ordinal) && d.EigeneHilfe.Contains("Mikrofon näher", StringComparison.Ordinal), "mit Hinweis");
        Woerter(d.EigeneHilfe, "Echo");
        // Seit 0.11.2 steht das Urteil nicht mehr über dem Spiel (Meldezeile.Erlaubt, Sarevoc 13.10.2026); es bleibt das gelbe Zeichen im Funkfeld mit
        // Tooltip. Der kurze Satz (bis 0.11.1 in der Meldezeile) bleibt kurz und in Wörtern.
        Pruefe.Falsch(zeile.Sichtbar || zeile.Zeigt(DauersendViewModel.UrteilSchluessel), "keine Meldezeile");
        Pruefe.Gleich("Echo: Ton kommt ins Mikrofon zurück", DauersendViewModel.Meldetext(DauersendUrteil.Echo), "der kurze Satz");

        foreach (var u in new[] { DauersendUrteil.Spielton, DauersendUrteil.Raumgeraeusch, DauersendUrteil.KlemmendeTaste })
        {
            a.Maschine.UrteilMelden(WaechterStand.Aus(u));
            takt.Abarbeiten();
            Pruefe.Wahr(d.HatEigenesUrteil && d.EigeneHilfe == Daempfungsworte.Ursache(u), u + ": " + d.EigeneHilfe);
            Woerter(d.EigeneHilfe, u.ToString());
            Pruefe.Falsch(zeile.Sichtbar, u + ": keine Meldezeile");
            var kurz = DauersendViewModel.Meldetext(u);
            Pruefe.Wahr(kurz.Length is > 0 and <= 40, u + ": kurz: " + kurz);
            Woerter(kurz, u + " im kurzen Satz");
        }
        // Echo bei ausgeschalteter Echounterdrückung: der eigene Satz des Klangkerns.
        a.Maschine.UrteilMelden(new WaechterStand(DauersendUrteil.Echo, Daempfungsworte.EchoOhneEchounterdrueckung));
        takt.Abarbeiten();
        Pruefe.Gleich(Daempfungsworte.EchoOhneEchounterdrueckung, d.EigeneHilfe, "ohne Echounterdrückung");

        // Schalter aus: Der Wächter urteilt weiter, stellt aber nichts; das sagt der Tooltip. Seit 0.11.1 erscheint dieselbe Meldung (Schlüssel
        // und Text) höchstens einmal je 5 min (Entprellung); "Echo: ..." stand oben schon, darum die Uhr 5 min weiter.
        var daempfungVorher = a.Maschine.Daempfung;
        a.Maschine.Daempfung = new DaempfungEinstellung(false, Daempfungsstufe.DeutlichLeiser);
        a.Uhr += Meldezeiten.WiederholungMs;
        a.Maschine.UrteilMelden(WaechterStand.Aus(DauersendUrteil.Echo));
        takt.Abarbeiten();
        Pruefe.Wahr(d.EigeneHilfe.EndsWith(DauersendViewModel.NurAngezeigt, StringComparison.Ordinal), "aus: nur angezeigt: " + d.EigeneHilfe);
        Pruefe.Gleich("Echo: Ton kommt ins Mikrofon zurück", DauersendViewModel.Meldetext(DauersendUrteil.Echo, an: false), "aus: Echo nennt nur die Ursache");
        // Befund der Prüfung (0.11.0): Aus heißt, die Sendekette sperrt die klemmende Taste nicht und hebt keine Schwelle; die kurze Zeile
        // behauptet das dann auch nicht (bis dahin "Senden aus" ohne das "nur angezeigt" des langen Satzes).
        foreach (var (u, erwartet) in new[] { (DauersendUrteil.KlemmendeTaste, "Sprechtaste klemmt: Senden bleibt an"), (DauersendUrteil.Raumgeraeusch, "Raumgeräusch: nur angezeigt") })
        {
            a.Maschine.UrteilMelden(WaechterStand.Aus(u));
            takt.Abarbeiten();
            var kurz = DauersendViewModel.Meldetext(u, an: false);
            Pruefe.Gleich(erwartet, kurz, u + " aus: keine Handlung behauptet");
            Pruefe.Falsch(kurz.Contains("Senden aus", StringComparison.Ordinal) || kurz.Contains("angehoben", StringComparison.Ordinal), u + " aus: " + kurz);
            Pruefe.Wahr(d.EigeneHilfe.EndsWith(DauersendViewModel.NurAngezeigt, StringComparison.Ordinal), u + " aus: Tooltip nur angezeigt");
        }
        a.Maschine.Daempfung = daempfungVorher;

        a.Maschine.UrteilMelden(WaechterStand.Keins);
        takt.Abarbeiten();
        Pruefe.Falsch(d.HatEigenesUrteil, "Entwarnung: kein Zeichen");
        Pruefe.Falsch(zeile.Zeigt(DauersendViewModel.UrteilSchluessel), "und keine Zeile");

        // Die Zeile steht ihre Zeit, das Zeichen im Funkfeld bleibt, solange das Urteil gilt.
        a.Maschine.UrteilMelden(WaechterStand.Aus(DauersendUrteil.Raumgeraeusch));
        takt.Abarbeiten();
        a.Uhr += Meldezeiten.UrteilMs + 1;
        zeile.Takt();
        Pruefe.Wahr(d.HatEigenesUrteil && !zeile.Zeigt(DauersendViewModel.UrteilSchluessel), "nach der Meldezeit nur noch das Zeichen");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 2: gelbes "!" bei anderen Funkern, Auswahl am "!"
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Wo der Name eines anderen Funkers erscheint (Sprecher auf der Kachel im Funkfeld, Zeile unter Empfangen > Funker), steht bei einem Dauersender
    /// das gelbe "!" mit Tooltip in Wörtern (Q2, Q13). Die Auswahl daran: stumm, wieder normal, für diesen Funker nicht mehr dämpfen; jede geht an den
    /// Klangkern. "Wer ist da" zeigt das "!" seit 0.11.5 nicht mehr (Sarevoc 14.10.2026: hinter dem Namen nur Mikrofon, Status und Wippe).
    /// </summary>
    [Test]
    public static void C2b_AndereFunker_ZeichenAmNamen_AuswahlHandelt()
    {
        using var a = Beispiel();
        using var f = a.Funkfeld();
        var netze = a.Netze(f);
        var crew = f.Kacheln.Single(k => k.NetzId == 4);
        var command = f.Kacheln.Single(k => k.NetzId == 2);
        Pruefe.Gleich("Kobold", crew.Sprecher, "Kobold spricht in Crew");
        Pruefe.Wahr(crew.DauersendZeichen is null && !crew.HatDauersendZeichen, "ohne Dauersender kein Zeichen");
        FunkerZeile Zeile(uint konto) => netze.Funker.Single(z => z.KontoId == konto);
        Pruefe.Gleich(null, Zeile(3).Dauersend, "Empfangen > Funker: ohne Zeichen");
        Pruefe.Falsch(typeof(WerIstDaZeile).GetProperty("Dauersend") is not null, "Wer ist da: kein \"!\" mehr (seit 0.11.5)");

        a.Maschine.DauersenderMelden(Kobold());
        var z = crew.DauersendZeichen ?? throw new PruefungException("kein Zeichen an Kobold");
        Pruefe.Wahr(crew.HatDauersendZeichen, "Kachel: Zeichen da");
        Pruefe.Wahr(z.Hilfe.StartsWith("Kobold wird bei dir deutlich leiser gestellt: sendet im Takt der anderen.", StringComparison.Ordinal), "Tooltip: " + z.Hilfe);
        Pruefe.Wahr(z.Hilfe.EndsWith(DauersendViewModel.AuswahlHinweis, StringComparison.Ordinal), "sagt, was ein Klick tut: " + z.Hilfe);
        Woerter(z.Hilfe, "Kobold");
        Pruefe.Gleich(null, command.DauersendZeichen, "Falke nicht");
        Pruefe.Gleich(z, Zeile(3).Dauersend, "Meine Kanäle, Pegel je Person: dasselbe Zeichen an Kobold");
        Pruefe.Gleich(null, netze.Funker.Single(x => x.KontoId == 2).Dauersend, "dort an Falke keins");
        Pruefe.Gleich("stumm|wieder normal|für diesen Funker nicht mehr dämpfen", string.Join("|", z.Wahlen.Select(x => x.Text)), "die Auswahl am Zeichen");
        foreach (var x in z.Wahlen) Woerter(x.Hilfe, x.Text);

        z.WaehlenCommand.Execute(z.Wahlen[0]);
        Pruefe.Gleich((3u, FunkerAktion.Stumm), a.Maschine.FunkerAktionen[^1], "stumm an den Klangkern");
        var stumm = crew.DauersendZeichen!;
        Pruefe.Wahr(stumm.Stumm && stumm.Hilfe.StartsWith("Kobold ist bei dir stumm (deine Wahl): sendet im Takt der anderen.", StringComparison.Ordinal), "stumm: " + stumm.Hilfe);
        Pruefe.Falsch(stumm.Wahlen[0].Waehlbar, "stumm ist schon gewählt");

        stumm.WaehlenCommand.Execute(stumm.Wahlen[1]);
        Pruefe.Gleich((3u, FunkerAktion.WiederNormal), a.Maschine.FunkerAktionen[^1], "wieder normal an den Klangkern");
        Pruefe.Wahr(crew.DauersendZeichen is null && Zeile(3).Dauersend is null, "wieder normal und kein Urteil: kein Zeichen mehr");

        // Er sendet weiter dauernd; diesmal am Zeichen unter Empfangen > Funker (bis 0.11.4 in "Wer ist da"): für diesen Funker nicht mehr dämpfen.
        a.Maschine.DauersenderMelden(Kobold());
        var wer = Zeile(3).Dauersend!;
        wer.WaehlenCommand.Execute(wer.Wahlen[2]);
        Pruefe.Gleich((3u, FunkerAktion.NichtMehrDaempfen), a.Maschine.FunkerAktionen[^1], "nicht mehr dämpfen an den Klangkern");
        var nicht = Zeile(3).Dauersend!;
        Pruefe.Wahr(nicht.Hilfe.StartsWith("Kobold wird bei dir nicht mehr gedämpft (deine Wahl): sendet im Takt der anderen.", StringComparison.Ordinal), "Empfangen: " + nicht.Hilfe);
        Pruefe.Falsch(nicht.Wahlen[2].Waehlbar, "nicht mehr dämpfen ist schon gewählt");
        Pruefe.Gleich(nicht, crew.DauersendZeichen, "die Kachel zeigt denselben Stand");
        nicht.WaehlenCommand.Execute(nicht.Wahlen[1]);
        Pruefe.Wahr(crew.DauersendZeichen is null && Zeile(3).Dauersend is null, "wieder normal: kein Zeichen");

        // Dauersenden ohne Echo (klemmende Taste, Musik): eigene Worte; während die Dämpfung noch steigt, "etwas leiser".
        a.Maschine.DauersenderMelden(new FunkerDaempfung(2, DauersendUrteil.Dauersenden, -4f, Daempfungsworte.Wort(-4f), false, false));
        Pruefe.Wahr(command.DauersendZeichen?.Hilfe.StartsWith("Falke wird bei dir etwas leiser gestellt: sendet seit langem ohne Pause, fast ohne Sprache.",
            StringComparison.Ordinal) == true, "Falke: " + command.DauersendZeichen?.Hilfe);
        a.Maschine.Daempfung = new DaempfungEinstellung(false, Daempfungsstufe.DeutlichLeiser);
        a.Maschine.DauersenderMelden(new FunkerDaempfung(2, DauersendUrteil.Dauersenden, 0f, Daempfungsworte.Wort(0f), false, false));
        Pruefe.Wahr(command.DauersendZeichen?.Hilfe.StartsWith("Falke bleibt bei dir normal laut, die Auto Dauersend-Dämpfung ist aus: sendet seit langem ohne Pause",
            StringComparison.Ordinal) == true, "Schalter aus: " + command.DauersendZeichen?.Hilfe);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 3: Schalter und Stufe (bis 0.9.3 auf der Audio-Seite, seit 0.10.0 unter Einstellungen > Empfangen > Feinheiten)
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Schalter "Auto Dauersend-Dämpfung" (ab Werk an, Q1) und die Stufe für andere in Wörtern (ab Werk "deutlich leiser", Q26), gemerkt und an der Maschine.</summary>
    [Test]
    public static void C2b_Audio_SchalterUndStufeInWoertern()
    {
        using var a = new Aufbau();
        var audio = a.Audio();
        Pruefe.Wahr(audio.DaempfungAn && audio.StufeDeutlichLeiser && !audio.StufeEtwasLeiser && !audio.StufeFastWeg, "ab Werk an, deutlich leiser");
        audio.StufeFastWeg = true;
        Pruefe.Gleich(new DaempfungEinstellung(true, Daempfungsstufe.FastWeg), a.Maschine.Daempfung, "fast weg an der Maschine");
        Pruefe.Wahr(audio.StufeFastWeg && !audio.StufeDeutlichLeiser, "Wahl wie Knöpfe");
        Pruefe.Gleich(Daempfungsstufe.FastWeg, a.Ablage.Aktuell.Daempfung.Stufe, "gemerkt");
        Pruefe.Wahr(audio.DaempfungText.Contains("fast weg", StringComparison.Ordinal), "Text nennt die Stufe: " + audio.DaempfungText);
        audio.DaempfungAn = false;
        Pruefe.Gleich(new DaempfungEinstellung(false, Daempfungsstufe.FastWeg), a.Maschine.Daempfung, "aus an der Maschine, die Stufe bleibt");
        Pruefe.Falsch(a.Ablage.Aktuell.Daempfung.Auto, "aus gemerkt");
        a.Ablage.Sichern();
        Pruefe.Gleich(new DaempfungEinstellung(false, Daempfungsstufe.FastWeg), a.Speicher.Laden().Daempfung.AlsEinstellung(), "in der Datei");
        foreach (var text in new[] { AudioViewModel.DaempfungHilfe, AudioViewModel.DaempfungErklaerung, AudioViewModel.EtwasLeiserHilfe, AudioViewModel.DeutlichLeiserHilfe,
                     AudioViewModel.FastWegHilfe, AudioViewModel.StufeGesperrt, audio.DaempfungText })
            Woerter(text, "Audio");
        audio.DaempfungAn = true;
        audio.StufeEtwasLeiser = true;
        Pruefe.Gleich(new DaempfungEinstellung(true, Daempfungsstufe.EtwasLeiser), a.Maschine.Daempfung, "etwas leiser");

        // Der Teil unter Empfangen > Feinheiten (bis 0.9.3 die Karte auf der Audio-Seite): Schalter, drei Wörter, keine Zahl; ohne "Leises Reden" (E15).
        Bildlauf.Ausfuehren(fenster =>
        {
            using var b = new Aufbau(FunkDienstAttrappe.Beispiel());
            var h = b.Haupt(out var funkfeld, out _, out var au, out _);
            Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
            var haupt = new Hauptfenster(h, b.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(960, 1900));
            var karte = Benannt(Bildlauf.Wurzel(haupt), "DaempfungKarte").Single();
            Pruefe.Falsch(Bildlauf.Nachfahren<CheckBox>(karte).Any(c => c.Name == "LeisesRedenSchalter"), "E15: kein Schalter Leises Reden");
            var schalter = (CheckBox)Benannt(karte, "DaempfungSchalter").Single();
            Pruefe.Wahr(schalter.IsChecked == true, "Schalter an");
            var knoepfe = Bildlauf.Nachfahren<RadioButton>(karte).ToList();
            Pruefe.Gleich("etwas leiser|deutlich leiser|fast weg", string.Join("|", knoepfe.Select(k => k.Content as string)), "drei Stufen in Wörtern");
            Pruefe.Wahr(knoepfe[1].IsChecked == true, "deutlich leiser gewählt");
            // Seit 0.11.2 flache Reiter (Umschalter.Radio auf Knopf.Flach; bis 0.11.1 Glas-Reiter).
            Pruefe.Wahr(knoepfe.All(k => !Bildlauf.Nachfahren<Glasdesign.Glaskoerper>(k).Any() && k.Template?.FindName("Schleier", k) is Border), "Stufen als flache Reiter");
            foreach (var t in Bildlauf.Nachfahren<TextBlock>(karte)) Woerter(t.Text, "Karte");
            au.DaempfungAn = false;
            Bildlauf.Leeren();
            Pruefe.Falsch(knoepfe.Any(k => k.IsEnabled), "aus: Stufen gesperrt");
            funkfeld.Dispose();
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 4: Tasten "Dauersender stumm" (Q8) und "Mikro stumm" (Q32)
    // ------------------------------------------------------------------------------------------------------------------------------

    private static readonly Tastenbelegung F10 = Tastenbelegung.Taste(0x0044);
    private static readonly Tastenbelegung Stick = new(EingabeArt.Joystick, 14, @"\\?\HID#VID_3344&PID_0194&MI_00#7&1a2b3c&0&0000#{4d1e55b2}");

    /// <summary>
    /// Beide Tasten stehen unter Einstellungen wie die Sprechtaste, belegbar mit Tastatur und Joystick, ab Werk unbelegt, gemerkt in der Liste ab 0.5.0
    /// (ältere Clients übergehen sie). Q8: erster Druck schaltet den zuletzt gedämpften Funker stumm, der zweite macht ihn wieder normal; die Meldezeile
    /// sagt, wen (im Vollbild klickt keiner). Mikro stumm schaltet um und sagt es ebenso.
    /// </summary>
    [Test]
    public static async Task C2b_Tasten_DauersenderStummUndMikroStumm()
    {
        using var a = Beispiel();
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        var stick = new TastenQuelleAttrappe(EingabeArt.Joystick);
        using var verbund = new SprechtastenVerbund(raw, stick);
        using var vm = new TastenViewModel(a.Ablage, verbund, Tastennamen.Kurz);
        var dauer = vm.Funktionen.Single(x => x.Funktion == Tastenfunktion.DauersenderStumm);
        var mikro = vm.Funktionen.Single(x => x.Funktion == Tastenfunktion.MikroStumm);
        Pruefe.Gleich("Dauersender stumm", dauer.Titel, "Zeile Dauersender");
        Pruefe.Gleich("Mikro stumm", mikro.Titel, "Zeile Mikro");
        Woerter(dauer.Erklaerung, "Erklärung");
        Woerter(mikro.Erklaerung, "Erklärung");
        Pruefe.Gleich(0, dauer.Belegungen.Count + mikro.Belegungen.Count, "ab Werk unbelegt");
        Pruefe.Falsch(Tastenfunktionen.IstGehalten(Tastenfunktion.DauersenderStumm) || Tastenfunktionen.IstGehalten(Tastenfunktion.MikroStumm), "Flanke, nicht gehalten");
        stick.NaechsteTaste = Stick;
        await dauer.BelegenCommand.ExecuteAsync(null);
        stick.NaechsteTaste = null;
        raw.NaechsteTaste = F10;
        await mikro.BelegenCommand.ExecuteAsync(null);
        Pruefe.Gleich(Tastenfunktion.DauersenderStumm, stick.Zuordnungen.Single().Funktion, "Joystick für Dauersender stumm");
        Pruefe.Gleich(Tastenfunktion.MikroStumm, raw.Zuordnungen.Single(x => x.Taste == F10).Funktion, "F10 für Mikro stumm");
        Pruefe.Wahr(a.Ablage.Aktuell.Tasten.Weitere.Count == 2 && a.Ablage.Aktuell.Tasten.Eintraege.All(e => Tastenfunktionen.IstAlt(e.Funktion)), "in der eigenen Liste");
        a.Ablage.Sichern();
        var gelesen = a.Speicher.Laden().Tasten.Zuordnungen();
        Pruefe.Wahr(gelesen.Contains(new Tastenzuordnung(Tastenfunktion.MikroStumm, F10)) && gelesen.Any(x => x.Funktion == Tastenfunktion.DauersenderStumm), "aus der Datei");

        // Was die Tasten auslösen: über den Oberflächentakt, wie App.OnStartup es baut.
        var takt = new WarteschlangenTakt();
        using var b = new Aufbau(FunkDienstAttrappe.Beispiel(), takt: takt);
        using var f = b.Funkfeld();
        var zeile = new Meldezeile(() => b.Uhr);
        f.Meldezeile = zeile;
        var quelle = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Joystick);
        using var teilnehmer = new Teilnehmermeldungen(b.Dienst, zeile, takt);
        var ablage = new FunkfeldAblage(new FunkfeldFensterAttrappe(), new AblagesymbolAttrappe(), b.Ablage, zeile, b.Dienst, () => b.Uhr);
        var phasen = new Phasensteuerung(b.Dienst, zeile, () => b.Uhr, () => b.Jetzt);
        var sprechen = new Sprechsteuerung(b.Maschine, b.Bruecke, () => new MeldeEntscheid(2, "Kommando", null));
        using var betrieb = new Tastenbetrieb(quelle, sprechen, takt, f, ablage, phasen, teilnehmer);
        takt.Abarbeiten();

        // Seit 0.11.2 sagt die Meldezeile bei keiner der beiden Tasten mehr etwas (Meldezeile.Erlaubt, Sarevoc 13.10.2026); die Tasten wirken wie bisher.
        quelle.Ausloesen(Tastenfunktion.DauersenderStumm);
        takt.Abarbeiten();
        Pruefe.Wahr(b.Maschine.FunkerAktionen.Count == 0 && !zeile.Sichtbar, "noch keiner gedämpft: nichts geschaltet, keine Meldezeile");
        b.Maschine.DauersenderMelden(Kobold());
        takt.Abarbeiten();
        quelle.Ausloesen(Tastenfunktion.DauersenderStumm);
        Pruefe.Gleich(0, b.Maschine.FunkerAktionen.Count, "aus dem Thread der Quelle: erst über den Takt");
        takt.Abarbeiten();
        Pruefe.Gleich((3u, FunkerAktion.Stumm), b.Maschine.FunkerAktionen[^1], "erster Druck: Kobold stumm");
        Pruefe.Falsch(zeile.Sichtbar, "keine Meldezeile: " + zeile.Text);
        quelle.Ausloesen(Tastenfunktion.DauersenderStumm);
        takt.Abarbeiten();
        Pruefe.Gleich((3u, FunkerAktion.WiederNormal), b.Maschine.FunkerAktionen[^1], "zweiter Druck: wieder normal");
        Pruefe.Falsch(zeile.Sichtbar, "keine Meldezeile: " + zeile.Text);

        quelle.Ausloesen(Tastenfunktion.MikroStumm);
        takt.Abarbeiten();
        Pruefe.Wahr(b.Maschine.MikroStumm && f.Mikro.Stumm, "Mikro stumm per Taste");
        Pruefe.Falsch(zeile.Sichtbar, "Mikro stumm: keine Meldezeile");
        quelle.Ausloesen(Tastenfunktion.MikroStumm);
        takt.Abarbeiten();
        Pruefe.Wahr(!b.Maschine.MikroStumm && !f.Mikro.Stumm, "zweiter Druck: wieder an");
        Pruefe.Falsch(zeile.Sichtbar, "Mikro wieder an: keine Meldezeile");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 5: Mikro stumm, auch durch die Einsatzleitung
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Das Modell des Knopfs: frei, selbst stumm (rote Leuchte), stumm durch Einsatzleitung oder Admin aus dem Lagezustand (0x0034, Schloss und
    /// "bis Uhrzeit", nicht selbst lösbar; Tooltip mit wer, bis wann, welche Kanäle). Gilt sie nur für einzelne Kanäle, tragen deren Felder den
    /// roten Punkt. Sie endet zur Zeit des Servers von selbst. Die Sprechtaste bei stummem Mikro sagt "Mikro ist stumm" in der Meldezeile; der
    /// Pegelbalken zeigt "stumm".
    /// </summary>
    [Test]
    public static void C2b_MikroStumm_FreiStummEinsatzleitung()
    {
        var takt = new WarteschlangenTakt();
        using var a = Beispiel(takt);
        using var f = a.Funkfeld();
        var zeile = new Meldezeile(() => a.Uhr);
        f.Meldezeile = zeile;
        takt.Abarbeiten();
        var m = f.Mikro;
        Pruefe.Wahr(m is { Zustand: MikroZustand.Frei, Stumm: false, Querstrich: false, Leuchte: false, LeitungStumm: false }, "frei");
        Pruefe.Gleich(MikroViewModel.HilfeFrei, m.KnopfHilfe, "Tooltip frei");

        m.UmschaltenCommand.Execute(null);
        Pruefe.Wahr(a.Maschine.MikroStumm && m is { Zustand: MikroZustand.Stumm, Stumm: true, Querstrich: true, Leuchte: true }, "Klick: stumm, rote Leuchte");
        Pruefe.Gleich(MikroViewModel.HilfeStumm, m.KnopfHilfe, "Tooltip stumm");
        Pruefe.Falsch(zeile.Zeigt(MikroViewModel.Schluessel), "der Klick am Knopf braucht keine Meldezeile");
        a.Maschine.SprechtasteGedrueckt();
        takt.Abarbeiten();
        // Bis 0.11.1 stand hier kurz "Mikro ist stumm" in der Meldezeile; seit 0.11.2 nicht mehr über dem Spiel (Meldezeile.Erlaubt), der Knopf zeigt es rot.
        Pruefe.Wahr(!zeile.Sichtbar && m.Leuchte, "Sprechtaste bei stumm: keine Meldezeile, der Knopf leuchtet rot: " + zeile.Text);
        a.Maschine.SprechtasteLosgelassen();
        a.Uhr += Meldezeiten.MikroIstStummMs + 1;
        zeile.Takt();
        Pruefe.Falsch(zeile.Zeigt(MikroViewModel.Schluessel), "nur kurz");

        // Der Pegelbalken zeigt "stumm".
        var audio = a.Audio();
        audio.Takt();
        Pruefe.Wahr(audio.Stumm, "Audio weiß, dass das Mikro stumm ist");
        m.UmschaltenCommand.Execute(null);
        audio.Takt();
        Pruefe.Wahr(!a.Maschine.MikroStumm && !audio.Stumm && m.Zustand == MikroZustand.Frei, "wieder frei");

        // Stummschaltung durch Nordlicht (Konto 4) in allen Kanälen bis 21:30 (Uhr des Servers, Ortszeit für die Anzeige).
        var bis = new DateTimeOffset(2026, 10, 4, 21, 30, 0, TimeSpan.FromHours(2));
        a.Dienst.LageSetzen(l => l with { Stumm = [new StummschaltungInfo(0, bis, 4)] });
        takt.Abarbeiten();
        var uhrzeit = bis.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        Pruefe.Wahr(m is { Zustand: MikroZustand.Leitung, LeitungStumm: true, LeitungAlle: true, Querstrich: true, Leuchte: true }, "durch die Einsatzleitung, alle Kanäle");
        Pruefe.Gleich("bis " + uhrzeit, m.LeitungBis, "bis wann am Knopf");
        Pruefe.Wahr(m.KnopfHilfe.Contains("Nordlicht", StringComparison.Ordinal) && m.KnopfHilfe.Contains("bis " + uhrzeit, StringComparison.Ordinal)
            && m.KnopfHilfe.Contains("in allen Kanälen", StringComparison.Ordinal) && m.KnopfHilfe.Contains("nicht selbst aufheben", StringComparison.Ordinal),
            "Tooltip mit wer, bis wann, welche Kanäle: " + m.KnopfHilfe);
        Pruefe.Falsch(f.Kacheln.Any(k => k.StummGeschaltet), "alle Kanäle: kein roter Punkt je Feld, das sagt der Knopf");
        m.UmschaltenCommand.Execute(null);
        Pruefe.Wahr(m.Stumm && m.LeitungStumm, "der Klick schaltet nur die eigene Stummschaltung dazu");
        m.UmschaltenCommand.Execute(null);
        Pruefe.Wahr(!m.Stumm && m.LeitungStumm, "und wieder weg; die der Einsatzleitung bleibt");

        // Nur Crew (Netz 4) und Kommando (Netz 2): rote Punkte dort, das Mikro ist nicht ganz stumm.
        a.Dienst.LageSetzen(l => l with { Stumm = [new StummschaltungInfo(4, bis, 4), new StummschaltungInfo(2, bis.AddMinutes(30), 3)] });
        takt.Abarbeiten();
        var crew = f.Kacheln.Single(k => k.NetzId == 4);
        Pruefe.Wahr(m is { Zustand: MikroZustand.Leitung, LeitungAlle: false, Querstrich: true, Leuchte: false }, "einzelne Kanäle: Schloss, ohne Leuchte");
        Pruefe.Gleich("bis " + bis.AddMinutes(30).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), m.LeitungBis, "bis zur letzten");
        Pruefe.Wahr(crew is { StummGeschaltet: true, RoterPunkt: true } && f.Kacheln.Single(k => k.NetzId == 2).StummGeschaltet, "rote Punkte in Crew und Kommando");
        Pruefe.Wahr(f.Kacheln.Where(k => k.NetzId is not (4 or 2)).All(k => !k.StummGeschaltet), "sonst keine");
        Pruefe.Wahr(crew.PunktHilfe.Contains("Stummgeschaltet von Nordlicht bis " + uhrzeit, StringComparison.Ordinal), "Tooltip der Kachel: " + crew.PunktHilfe);
        Pruefe.Wahr(m.KnopfHilfe.Contains("Crew", StringComparison.Ordinal) && m.KnopfHilfe.Contains("Kommando", StringComparison.Ordinal)
            && m.KnopfHilfe.Contains("Kobold", StringComparison.Ordinal), "Tooltip nennt Kanäle und wer: " + m.KnopfHilfe);

        // Zur Zeit des Servers abgelaufen: frei, ohne dass der Server etwas schickt (der Takt sieht nach).
        a.Dienst.LageSetzen(l => l with { ZeitVersatz = TimeSpan.FromMinutes(5) });
        takt.Abarbeiten();
        a.Jetzt = bis.AddMinutes(30).AddMinutes(-4);
        m.Takt();
        takt.Abarbeiten();
        Pruefe.Wahr(m.Zustand == MikroZustand.Frei && !crew.StummGeschaltet, "mit dem Versatz der Serveruhr abgelaufen: frei, kein Punkt");
    }

    /// <summary>Der Pegelbalken bei stummem Mikro: kein Pegel, das Wort "stumm" in der Mitte, der Tooltip sagt es.</summary>
    [Test]
    public static void C2b_Pegelbalken_ZeigtStumm()
    {
        Bildlauf.Ausfuehren(ohneFenster =>
        {
            var b = new Pegelbalken { Width = 300, Height = 28, PegelDb = -20, SchwelleDb = -40, Sendet = true };
            var bild = OberflaecheHilfeTests.Zeichnen(b);
            var gruen = Bildlauf.Pixel(bild, b.XFuer(-60), 14);
            Pruefe.Wahr(gruen.G > 150 && gruen.R < 120, "ohne stumm: grüner Pegel, " + gruen);
            b.Stumm = true;
            bild = OberflaecheHilfeTests.Zeichnen(b);
            var leer = Bildlauf.Pixel(bild, b.XFuer(-80), 14);
            Pruefe.Falsch(leer.G > 150 && leer.R < 120, "stumm: kein Pegel, " + leer);
            var hell = 0;
            for (var x = 120; x < 180; x++)
                for (var y = 6; y < 22; y++)
                    if (Bildlauf.Pixel(bild, x, y) is { R: > 160, G: > 160, B: > 160 }) hell++;
            Pruefe.Wahr(hell > 20, T($"das Wort in der Mitte ({hell} helle Bildpunkte)"));
            Pruefe.Gleich(Pegelbalken.HilfeStumm, b.Hilfe(Pegelbalken.Teil.Balken), "Tooltip");
            Pruefe.Wahr(Pegelbalken.HilfeStumm.StartsWith("stumm", StringComparison.OrdinalIgnoreCase) || Pegelbalken.HilfeStumm.Contains("stumm", StringComparison.Ordinal), "Wort");
        });
        // Seit 0.10.0 (Q87) steht der Pegel für Start in der Statuszeile des Hauptfensters (bis 0.9.3 auf der Startseite selbst).
        var fenster = Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster");
        var bereiche = File.ReadAllText(Path.Combine(fenster, "Bereiche.xaml"));
        var haupt = File.ReadAllText(Path.Combine(fenster, "Hauptfenster.xaml"));
        Pruefe.Wahr(bereiche.Contains("Stumm=\"{Binding Stumm}\"", StringComparison.Ordinal) && haupt.Contains("Stumm=\"{Binding Stumm}\"", StringComparison.Ordinal),
            "Audio und Statuszeile: der Balken weiß von stumm");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Im Fenster: Funkfeld, Wer ist da, Meldezeile
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Das Funkfeld gezeichnet: oben in der Steuerleiste das eigene "!" vor "Senden: Crew" und der Mikro-Knopf neben dem Transparenz-Schieber (seit
    /// 0.11.2 flach wie die anderen Knöpfe, Q34: nur dort), an Kobold auf der Kachel das klickbare "!" mit der Auswahl, rote Punkte bei Stummschaltung
    /// einzelner Kanäle; in Raster und Liste. Dazu "Wer ist da" und die Meldezeile (seit 0.11.2 ohne das Urteil mit "!").
    /// </summary>
    [Test]
    public static void C2b_ImFenster_FunkfeldWerIstDaMeldezeile()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = Beispiel();
            var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(ff);
            foreach (var d in new[] { FunkfeldDarstellung.Raster, FunkfeldDarstellung.Liste })
            {
                f.Darstellung = d;
                ff.DarstellungAnwenden();
                a.Maschine.UrteilMelden(WaechterStand.Keins);
                a.Maschine.FunkerHandeln(3, FunkerAktion.WiederNormal);
                a.Maschine.DauersenderMelden(new FunkerDaempfung(3, DauersendUrteil.Keins, 0f, Daempfungsworte.Wort(0f), false, false));
                _ = Bildlauf.Zeichnen(ff);
                var wurzel = Bildlauf.Wurzel(ff);
                var eigen = Benannt(wurzel, "EigenesUrteilZeichen").Single();
                Pruefe.Gleich(Visibility.Collapsed, eigen.Visibility, d + ": ohne Urteil kein eigenes Zeichen");
                var mikro = (Button)Benannt(wurzel, "MikroKnopf").Single();
                var regler = Benannt(wurzel, "ReglerUnterlage").Single();
                var kopf = (FrameworkElement)ff.FindName("Kopf");
                Pruefe.Wahr(Vorfahren(mikro).Contains(kopf), d + ": der Knopf steht in der oberen Steuerleiste");
                // Seit 0.11.2 flach wie alle Knöpfe (bis 0.11.1 Glas).
                Pruefe.Falsch(Bildlauf.Nachfahren<Glasdesign.Glaskoerper>(mikro).Any(), d + ": flach wie die anderen Knöpfe, ohne Glas");
                Pruefe.Gleich(MikroViewModel.HilfeFrei, Glasdesign.Hilfe.GetText(mikro), d + ": Tooltip in Wörtern");
                var rm = Lage(mikro, wurzel);
                if (d == FunkfeldDarstellung.Raster)
                {
                    var rr = Lage(regler, wurzel);
                    var rt = Lage((FrameworkElement)ff.FindName("KopfText"), wurzel);
                    Pruefe.Wahr(rt.Right <= rm.Left + 0.5 && rm.Right <= rr.Left + 0.5 && rr.Left - rm.Right < 12,
                        T($"{d}: Senden, Mikro, Regler nebeneinander; der Regler bleibt an den Fensterknöpfen ({rt}, {rm}, {rr})"));
                }
                Pruefe.Wahr(rm.Top >= 0 && rm.Bottom <= 24.5, T($"{d}: in der Titelzeile ({rm})"));
                if (d == FunkfeldDarstellung.Liste)
                {
                    // Liste mit aufgeklapptem Regler: der Knopf bleibt, vor dem Regler.
                    f.ReglerOffen = true;
                    _ = Bildlauf.Zeichnen(ff);
                    var offen = Lage(mikro, wurzel);
                    var regler2 = Lage(regler, wurzel);
                    Pruefe.Wahr(Sichtbar(mikro) && offen.Right <= regler2.Left + 0.5 && offen.Width > 10, T($"Liste aufgeklappt: Mikro vor dem Regler ({offen}, {regler2})"));
                    f.ReglerOffen = false;
                    _ = Bildlauf.Zeichnen(ff);
                }
                Pruefe.Gleich(Visibility.Collapsed, Benannt(mikro, "MikroLeuchte").Single().Visibility, d + ": frei ohne Leuchte");
                Pruefe.Gleich(1, Bildlauf.Nachfahren<Button>(Bildlauf.Wurzel(ff)).Count(b => b.Name == "MikroKnopf"), d + ": nur ein Mikro-Knopf");

                // Eigenes Urteil und Kobold als Dauersender.
                a.Maschine.UrteilMelden(WaechterStand.Aus(DauersendUrteil.Echo));
                a.Maschine.DauersenderMelden(Kobold());
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Wahr(Sichtbar(eigen), d + ": eigenes Zeichen");
                var re = Lage(eigen, wurzel);
                var rk = Lage((FrameworkElement)ff.FindName("KopfText"), wurzel);
                Pruefe.Wahr(re.Right <= rk.Left + 0.5 && rk.Left - re.Right < 8 && re.Bottom <= 24.5, T($"{d}: vor der eigenen Sendeanzeige ({re}, {rk})"));
                Pruefe.Gleich(f.Dauersend.EigeneHilfe, Glasdesign.Hilfe.GetText(eigen), d + ": Tooltip mit Ursache");
                var zeichen = (Klappwahl)ImFeld(ff, 2, "DauersendZeichen");
                Pruefe.Gleich(Visibility.Visible, zeichen.Visibility, d + ": gelbes Zeichen an Kobold");
                Pruefe.Wahr(zeichen.IsHitTestVisible && Vorfahren(zeichen).OfType<UIElement>().All(v => v.IsHitTestVisible) && zeichen.IsEnabled && zeichen.ActualWidth > 8,
                    d + ": klickbar");
                Pruefe.Gleich(f.Kacheln[2].DauersendZeichen!.Hilfe, Glasdesign.Hilfe.GetText(zeichen), d + ": Tooltip");
                Pruefe.Gleich(3, zeichen.Eintraege!.Cast<DauersendWahl>().Count(), d + ": Auswahl mit drei Einträgen");
                Pruefe.Wahr(zeichen.Liste is not null, d + ": Klappliste da");
                var rz = Lage(zeichen, wurzel);
                Pruefe.Wahr(f.Kacheln.Where(k => k.Platz != 2).All(k => k.DauersendZeichen is null), d + ": nur an Kobold");
                zeichen.Waehlen!.Execute(zeichen.Eintraege!.Cast<DauersendWahl>().First());
                Pruefe.Gleich((3u, FunkerAktion.Stumm), a.Maschine.FunkerAktionen[^1], d + ": Wahl aus dem Fenster an den Klangkern");
                Pruefe.Wahr(rz.Width > 0, "Lage " + rz);

                // Mikro stumm und durch die Einsatzleitung.
                mikro.Command.Execute(null);
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Gleich(Visibility.Visible, Benannt(mikro, "MikroLeuchte").Single().Visibility, d + ": stumm mit roter Leuchte");
                Pruefe.Gleich((Geometry)ff.FindResource("Symbol.MikrofonStumm"), ((System.Windows.Shapes.Path)Benannt(mikro, "MikroSymbol").Single()).Data, d + ": Mikro mit Querstrich");
                mikro.Command.Execute(null);
                var bis = a.Jetzt.AddMinutes(38);
                a.Dienst.LageSetzen(l => l with { Stumm = [new StummschaltungInfo(4, bis, 4)] });
                _ = Bildlauf.Zeichnen(ff);
                Pruefe.Gleich(Visibility.Visible, Benannt(mikro, "MikroSchloss").Single().Visibility, d + ": Schloss");
                // ui-fix 0.6.2: "bis 21:30" steht im Tooltip statt am Knopf, damit "Senden: ..." daneben ungekürzt bleibt.
                Pruefe.Wahr(Glasdesign.Hilfe.GetText(mikro)?.Contains("bis " + bis.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.Ordinal) == true, d + ": bis wann (Tooltip)");
                Pruefe.Falsch(Benannt(mikro, "MikroBis").Any(), d + ": am Knopf keine Uhrzeit mehr");
                var punkt = Benannt(wurzel, "Punkt").First(e => e.DataContext is KachelViewModel { Platz: 2 });
                var strich = Bildlauf.Nachfahren<Rectangle>(punkt).Single();
                Pruefe.Gleich(Visibility.Visible, strich.Visibility, d + ": roter Punkt mit Querstrich in Crew");
                Pruefe.Gleich(((SolidColorBrush)ff.FindResource("Pinsel.RotSchrift")).Color, ((SolidColorBrush)Bildlauf.Nachfahren<Ellipse>(punkt).Single().Fill).Color,
                    d + ": rot wie eine Sperre (rote Schrift der Farbstellung)");
                a.Dienst.LageSetzen(l => l with { Stumm = [] });
                _ = Bildlauf.Zeichnen(ff);
            }
            f.Dispose();

            // Wer ist da: seit 0.11.5 kein Zeichen mehr an Kobold (Sarevoc 14.10.2026: hinter dem Namen nur Mikrofon, Status und Wippe); das "!" steht im
            // Funkfeld und in der Kopfzeile. Das Mikrofon in Wer ist da ist das Stummschalten der Einsatzleitung, nicht der Mikro-Knopf.
            using var b = Beispiel();
            var h = b.Haupt(out var funkfeld, out _, out _, out _);
            h.WerIstDa.Offen = true;
            var haupt = new Hauptfenster(h, b.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            b.Maschine.DauersenderMelden(Kobold());
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1280, 800));
            var spalte = (FrameworkElement)haupt.FindName("WerIstDaSpalte");
            Pruefe.Wahr(Sichtbar(spalte) && Benannt(spalte, "NameKnopf").Any(Sichtbar), "Wer ist da offen, mit Namen");
            Pruefe.Gleich(0, Benannt(Bildlauf.Wurzel(haupt), "DauersendZeichen").OfType<Klappwahl>().Count(Sichtbar), "Wer ist da: kein gelbes \"!\" mehr");
            Pruefe.Falsch(Benannt(Bildlauf.Wurzel(haupt), "MikroKnopf").Any(), "Q34: der Mikro-Knopf nur im Funkfeld, nicht im Hauptfenster");
            // Kopfzeile "spricht: Falke, Kobold": davor das Zeichen des Dauersenders.
            Pruefe.Wahr(h.SprechendZeichen is { KontoId: 3 }, "Kopfzeile: Zeichen an Kobold");
            var kopfZeichen = Benannt(Bildlauf.Wurzel(haupt), "SprechendZeichen").Single();
            Pruefe.Wahr(Sichtbar(kopfZeichen) && WindowChrome.GetIsHitTestVisibleInChrome(kopfZeichen), "in der Titelleiste sichtbar und klickbar");
            Pruefe.Wahr(Glasdesign.Hilfe.GetText(kopfZeichen)?.StartsWith("Kobold wird bei dir", StringComparison.Ordinal) == true, "Tooltip nennt ihn");
            funkfeld.Dispose();

            // Meldezeile: eine Meldung ohne gelbes Zeichen. Bis 0.11.1 stand dort auch das eigene Urteil mit gelbem Zeichen; seit 0.11.2 erscheint es
            // nicht mehr über dem Spiel (Meldezeile.Erlaubt), es ersetzt nichts und zeigt kein Zeichen.
            var zeile = new Meldezeile(() => 0);
            var mf = new MeldezeileFenster(zeile);
            fenster.Add(mf);
            zeile.Zeigen(Teilnehmermeldungen.UmstellungSchluessel, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 5000);
            _ = Bildlauf.Zeichnen(mf);
            Pruefe.Gleich(0, Benannt(Bildlauf.Wurzel(mf), "Ausrufe").OfType<UIElement>().Count(Sichtbar), "Meldezeile: kein gelbes Zeichen vor der Meldung");
            zeile.Zeigen(DauersendViewModel.UrteilSchluessel, DauersendViewModel.Meldetext(DauersendUrteil.Echo), Meldeart.Hinweis, 5000, ausrufe: true);
            _ = Bildlauf.Zeichnen(mf);
            Pruefe.Gleich(0, Benannt(Bildlauf.Wurzel(mf), "Ausrufe").OfType<UIElement>().Count(Sichtbar), "Meldezeile: das Urteil erscheint nicht, kein gelbes Zeichen");
            Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Gunner", zeile.Text, "die Meldung steht weiter");
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Sichtproben
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Text in Zeilen zu höchstens <paramref name="breite"/> Zeichen, an Leerzeichen getrennt.</summary>
    private static List<string> Umbrechen(string text, int breite)
    {
        var zeilen = new List<string>();
        var aktuell = "";
        foreach (var wort in text.Split(' '))
        {
            if (aktuell.Length > 0 && aktuell.Length + 1 + wort.Length > breite)
            {
                zeilen.Add(aktuell);
                aktuell = wort;
            }
            else
            {
                aktuell = aktuell.Length == 0 ? wort : aktuell + " " + wort;
            }
        }
        if (aktuell.Length > 0) zeilen.Add(aktuell);
        return zeilen;
    }

    /// <summary>Ein Bild mit Tooltips darunter (Tooltips sind Popups und lassen sich ohne Fenster nicht zeichnen).</summary>
    private static RenderTargetBitmap MitTexten(BitmapSource bild, Size groesse, double skala, IReadOnlyList<(string Wo, string Text)> texte, FrameworkElement? liste = null, Rect? listeBei = null)
    {
        var zeilen = texte.SelectMany(t => Umbrechen(t.Wo + ": " + t.Text, 96)).ToList();
        var listeH = liste?.ActualHeight ?? 0;
        var listeB = liste is null ? 0 : listeBei!.Value.Left + liste.ActualWidth;
        var breite = Math.Max(Math.Max(groesse.Width, listeB) + 16, 660);
        var unten = Math.Max(groesse.Height, listeBei is { } r ? r.Bottom + 2 + listeH : 0);
        var b = new Bogen(breite, unten + 16 + (zeilen.Count * 18) + 12, skala, OberflaecheFunkfeldBilderTests.Grund);
        b.Bild(bild, 0, 0, groesse.Width, groesse.Height);
        if (liste is not null)
        {
            var lb = new RenderTargetBitmap((int)Math.Ceiling(liste.ActualWidth * skala), (int)Math.Ceiling(liste.ActualHeight * skala), 96 * skala, 96 * skala, PixelFormats.Pbgra32);
            lb.Render(liste);
            b.Bild(lb, listeBei!.Value.Left, listeBei.Value.Bottom + 2, liste.ActualWidth, liste.ActualHeight);
        }
        var y = unten + 12;
        foreach (var z in zeilen)
        {
            b.Text(z, 10, y, 12);
            y += 18;
        }
        return b.Fertig();
    }

    /// <summary>Die Klappliste eines Klappfelds messen und anordnen, damit sie ohne Popup gezeichnet werden kann.</summary>
    private static FrameworkElement Klappliste(Klappwahl wahl)
    {
        var liste = wahl.Liste ?? throw new PruefungException("Klappfeld ohne Liste");
        liste.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Bildlauf.Leeren();
        liste.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        liste.Arrange(new Rect(liste.DesiredSize));
        liste.UpdateLayout();
        return liste;
    }

    /// <summary>
    /// Sichtproben für Sarevoc (nur mit FUNK_BILDER, etwa C:\Claude\output\test\20261004_klang-c2b-sichtproben), in Anthrazit: das Funkfeld mit dem
    /// Mikro-Knopf frei, stumm und stumm durch die Einsatzleitung (alle Kanäle und nur Crew mit rotem Punkt), mit dem eigenen "!" und dem "!" an
    /// Kobold samt offener Auswahl, die Liste, die Audio-Seite mit Schalter und Stufe, die Startseite mit "stumm" am Pegelbalken und die Meldezeile
    /// (seit 0.11.2 ohne das Urteil; "Wer ist da" mit "!" bis 0.11.4). Unter jedem Bild stehen die Tooltips im Wortlaut.
    /// </summary>
    [Test]
    public static void C2b_Sichtproben()
    {
        if (Bildablage.Ordner() is null) return;
        Bildlauf.Ausfuehren(fenster =>
        {
            var n = 0;
            const double Skala = 1.75;
            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var a = Beispiel();
                a.Ablage.Aktuell.Farbstellung = stellung;
                var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
                var ff = new FunkfeldFenster(f, a.Ablage, new FesteBildschirme([FullHd]));
                fenster.Add(ff);
                var mikro = (Button)Benannt(Bildlauf.Wurzel(ff), "MikroKnopf").Single();
                void Funkfeld(string name, params (string Wo, string Text)[] texte)
                {
                    var (bild, g) = Bildlauf.Zeichnen(ff, Skala);
                    Bildablage.Ablegen(MitTexten(bild, g, Skala, texte), T($"{++n:00}-{name}-{kurz}"));
                }
                Funkfeld("funkfeld-mikro-frei", ("Tooltip Mikro", f.Mikro.KnopfHilfe));
                f.Mikro.UmschaltenCommand.Execute(null);
                Funkfeld("funkfeld-mikro-stumm", ("Tooltip Mikro", f.Mikro.KnopfHilfe));
                f.Mikro.UmschaltenCommand.Execute(null);
                var bis = a.Jetzt.AddMinutes(38);
                a.Dienst.LageSetzen(l => l with { Stumm = [new StummschaltungInfo(0, bis, 4)] });
                Funkfeld("funkfeld-mikro-einsatzleitung", ("Tooltip Mikro", f.Mikro.KnopfHilfe));
                a.Dienst.LageSetzen(l => l with { Stumm = [new StummschaltungInfo(4, bis, 4)] });
                Funkfeld("funkfeld-mikro-einsatzleitung-nur-crew", ("Tooltip Mikro", f.Mikro.KnopfHilfe), ("Tooltip Feld Crew", f.Kacheln[2].KopfHilfe));
                a.Dienst.LageSetzen(l => l with { Stumm = [] });

                a.Maschine.UrteilMelden(WaechterStand.Aus(DauersendUrteil.Echo));
                a.Maschine.DauersenderMelden(Kobold());
                var z = f.Kacheln[2].DauersendZeichen!;
                Funkfeld("funkfeld-ausrufe-eigen-und-kobold", ("Tooltip eigenes !", f.Dauersend.EigeneHilfe), ("Tooltip ! an Kobold", z.Hilfe));
                var zeichen = (Klappwahl)ImFeld(ff, 2, "DauersendZeichen");
                var liste = Klappliste(zeichen);
                var r = Lage(zeichen, Bildlauf.Wurzel(ff));
                var (offen, go) = Bildlauf.Zeichnen(ff, Skala);
                Bildablage.Ablegen(MitTexten(offen, go, Skala, [.. z.Wahlen.Select(w => ("Tooltip " + w.Text, w.Hilfe))], liste, r), T($"{++n:00}-funkfeld-ausrufe-auswahl-{kurz}"));

                f.Darstellung = FunkfeldDarstellung.Liste;
                ff.DarstellungAnwenden();
                f.Mikro.UmschaltenCommand.Execute(null);
                Funkfeld("funkfeld-liste-ausrufe-mikro-stumm", ("Tooltip Mikro", f.Mikro.KnopfHilfe));
                f.Mikro.UmschaltenCommand.Execute(null);
                a.Dienst.LageSetzen(l => l with { Stumm = [new StummschaltungInfo(0, bis, 4)] });
                Funkfeld("funkfeld-liste-mikro-einsatzleitung", ("Tooltip Mikro", f.Mikro.KnopfHilfe));
                f.Dispose();

                // Hauptfenster: Empfangen > Feinheiten mit der Dämpfung (bis 0.9.3 die Audio-Seite), Wer ist da mit "!", Start mit "stumm" unten in der Statuszeile.
                using var b = Beispiel();
                b.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Lautsprecher;
                b.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
                b.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
                b.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
                var h = b.Haupt(out var funkfeld, out _, out var audio, out _);
                _ = b.Maschine.StartenAsync(null, null, CancellationToken.None);
                var haupt = new Hauptfenster(h, b.Ablage, new FesteBildschirme([FullHd]));
                fenster.Add(haupt);
                Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
                h.Takt();
                var (seite, _) = Bildlauf.Zeichnen(haupt, 1, new Size(960, 1900));
                var karte = Benannt(Bildlauf.Wurzel(haupt), "DaempfungKarte").Single();
                var rk = Lage(karte, Bildlauf.Wurzel(haupt));
                var ausschnitt = new CroppedBitmap(seite, new Int32Rect(0, (int)Math.Max(0, rk.Top - 60), 960, (int)Math.Min(seite.PixelHeight - Math.Max(0, rk.Top - 60), rk.Height + 120)));
                Bildablage.Ablegen(MitTexten(ausschnitt, new Size(960, ausschnitt.PixelHeight), 1,
                    [("Tooltip Schalter", AudioViewModel.DaempfungHilfe), ("Tooltip etwas leiser", AudioViewModel.EtwasLeiserHilfe),
                        ("Tooltip deutlich leiser", AudioViewModel.DeutlichLeiserHilfe), ("Tooltip fast weg", AudioViewModel.FastWegHilfe)]),
                    T($"{++n:00}-empfangen-daempfung-{kurz}"));
                if (stellung == Farbstellung.Anthrazit)
                {
                    audio.DaempfungAn = false;
                    var (aus, _) = Bildlauf.Zeichnen(haupt, 1, new Size(960, 1900));
                    Bildablage.Ablegen(new CroppedBitmap(aus, new Int32Rect(0, (int)Math.Max(0, rk.Top - 60), 960, (int)Math.Min(aus.PixelHeight - Math.Max(0, rk.Top - 60), rk.Height + 120))),
                        T($"{++n:00}-empfangen-daempfung-aus-{kurz}"));
                    audio.DaempfungAn = true;
                    h.AktiverBereich = h.Bereiche[0];
                    b.Maschine.MikroStumm = true;
                    h.Takt();
                    var (start, _) = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.KleinMindest);
                    Bildablage.Ablegen(MitTexten(start, Fenstergroessen.KleinMindest, 1, [("Tooltip Balken", Pegelbalken.HilfeStumm)]), T($"{++n:00}-start-pegel-stumm-{kurz}"));
                    b.Maschine.MikroStumm = false;
                    // Bis 0.11.4 hier das Bild weristda-ausrufe (das "!" an Kobold in Wer ist da); seit 0.11.5 zeigt Wer ist da kein "!" mehr.

                    var zeile = new Meldezeile(() => 0);
                    // Seit 0.11.2 erscheint das Urteil nicht mehr über dem Spiel (Meldezeile.Erlaubt, Sarevoc 13.10.2026): Das Bild zeigt die Meldezeile mit
                    // einer Meldung der Leitung, und das Urteil danach verdrängt sie nicht. Bis zur Gegenprüfung 0.11.5 erwartete diese Probe noch das
                    // Urteil und scheiterte, sobald Bilder entstanden (FUNK_BILDER); ohne Bilder läuft sie nicht.
                    zeile.Zeigen(Teilnehmermeldungen.UmstellungSchluessel, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 5000);
                    zeile.Zeigen(DauersendViewModel.UrteilSchluessel, DauersendViewModel.Meldetext(DauersendUrteil.Echo), Meldeart.Hinweis, 5000, ausrufe: true);
                    var mf = new MeldezeileFenster(zeile);
                    fenster.Add(mf);
                    var (mz, _) = Bildlauf.Zeichnen(mf, Skala);
                    Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Gunner", zeile.Text, "Bild Meldezeile: die Meldung der Leitung, nicht das Urteil");
                    Bildablage.Ablegen(mz, T($"{++n:00}-meldezeile-{kurz}"));
                }
                funkfeld.Dispose();
            }
        });
    }
}
