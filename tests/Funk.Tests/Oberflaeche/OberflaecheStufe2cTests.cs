// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Stufe 2c von 0.6.0 nach Sarevocs Rückmeldungen vom 02.10.2026 (19:30): in der Leiste links ein Unterpunkt "Sprachaktivierung" mit der ganzen
/// Schwellenseite, nur wenn die Sprachaktivierung benutzt wird; seit 0.10.0 (E3) unter Einstellungen statt unter Audio, und unten klappt
/// nichts mehr auf (dort steht die Statuszeile). Dazu die Sichtproben der Stufe (Glasdesign 0.3.1, die drei Modi der Schwelle, der Unterpunkt
/// und seine Seite), mit
/// <c>FUNK_BILDER</c> als PNG. Gezeichnet ohne Fensterhandle (<see cref="Bildlauf"/>). Der Klassenname steht nach
/// OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheStufe2cTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static readonly double[] Skalen = [1.0, 1.75];

    private static readonly GateModus[] Modi = [GateModus.Fest, GateModus.Automatisch, GateModus.Lernend];

    /// <summary>Was es seit 0.10.0 unten im Hauptfenster nicht mehr gibt: Platz, Trenner, Leiste und Zeile der aufgeklappten Sprachaktivierung.</summary>
    private static readonly string[] Entfallen = ["SchwellenUntenPlatz", "SchwellenTrenner", "SchwellenLeiste", "SchwellenZeile", "LeistenPegel", "LeistenHinweis"];


    private static string T(FormattableString s) => s.ToString(De);

    private static string[] Kennungen(HauptViewModel h) => [.. h.Navigation.Select(b => b.Kennung)];

    /// <summary>
    /// Der Unterpunkt steht genau dann unter Einstellungen (seit 0.10.0, E3; bis 0.9.3 unter Audio), wenn ein Feld einen Kanal trägt, auf dem
    /// du senden darfst und der auf Sprachaktivierung steht (nur dann kann sie je senden). Die Bereiche mit Strg+1 und Strg+2 bleiben, wie
    /// sie sind (seit 0.10.0 nur noch Start und Einstellungen, E1). Wird die Sprachaktivierung nicht mehr benutzt, während seine Seite vorn ist,
    /// bleibt sie mit ihm, bis man sie verlässt (Befund der Prüfung); geht "Senden mit Stimme" aus, geht es zu Einstellungen. Ohne Verbindung gibt es
    /// ihn nicht.
    /// </summary>
    [Test]
    public static void Unterpunkt_NurWennDieSprachaktivierungBenutztWird()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        Pruefe.Wahr(funkfeld.SprachaktivierungBenutzt, "Beispiel: Crew und Brücke stehen auf Sprachaktivierung");
        Pruefe.Gleich("start einstellungen sprachaktivierung", string.Join(" ", Kennungen(h).Where(k => k is not (HauptViewModel.RaidKennung or HauptViewModel.VerwaltungKennung))),
            "Unterpunkt direkt unter Einstellungen (E3); Audio und Meine Kanäle gibt es nicht mehr (E1)");
        var unterpunkt = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
        Pruefe.Wahr(unterpunkt.Unterpunkt && unterpunkt.Titel == "Sprachaktivierung" && ReferenceEquals(unterpunkt.Inhalt, h.Schwelle), "eingerückt, gleicher Name, gleiches Modell wie unten");
        Pruefe.Falsch(h.Bereiche.Contains(unterpunkt), "kein Bereich mit eigenem Tastenkürzel");
        h.BereichWaehlenCommand.Execute("3");
        Pruefe.Wahr(h.AktiverBereich.Inhalt is StartViewModel, "Strg+3 bewirkt nichts (E2)");
        h.BereichWaehlenCommand.Execute("2");
        Pruefe.Wahr(h.AktiverBereich.Inhalt is EinstellungenViewModel, "Strg+2 Einstellungen, nicht der Unterpunkt");

        h.AktiverBereich = unterpunkt;
        Pruefe.Wahr(h.IstSprachaktivierungsseite && !h.MitBildlauf && h.SeitenInhalt is null, "die Seite steht ohne den Bildlauf der Bereiche");
        foreach (var k in funkfeld.Kacheln.Where(k => k.KannSprachaktivierung)) k.Sprachaktivierung = false;
        Pruefe.Falsch(funkfeld.SprachaktivierungBenutzt, "alle Kanäle auf Sprechtaste");
        // Seit dem Befund der Prüfung lässt sich die Seite mit "Senden mit Stimme" an auch ohne Benutzung öffnen (Strg+T, "Genau einstellen ...");
        // wer gerade auf ihr ist, bleibt darum, und ihr Unterpunkt steht da, bis man sie verlässt (bis dahin ging es sofort zu Einstellungen, E3).
        Pruefe.Wahr(h.IstSprachaktivierungsseite && Kennungen(h).Contains(HauptViewModel.SprachaktivierungKennung), "auf der Seite: sie und ihr Unterpunkt bleiben");
        h.BereichWaehlenCommand.Execute("2");
        Pruefe.Falsch(Kennungen(h).Contains(HauptViewModel.SprachaktivierungKennung), "Seite verlassen: der Unterpunkt verschwindet");
        Pruefe.Gleich(HauptViewModel.EinstellungenKennung, h.AktiverBereich.Kennung, "auf Einstellungen");
        h.SprachaktivierungEinstellenCommand.Execute(null);
        h.Audio.SprachaktivierungAn = false;
        Pruefe.Gleich(HauptViewModel.EinstellungenKennung, h.AktiverBereich.Kennung, "Senden mit Stimme aus: von seiner Seite geht es zu Einstellungen (E3)");
        h.Audio.SprachaktivierungAn = true;

        funkfeld.Kacheln.First(k => k.KannSprachaktivierung && k.KannSenden).Sprachaktivierung = true;
        Pruefe.Wahr(Kennungen(h).Contains(HauptViewModel.SprachaktivierungKennung), "ein Kanal auf Sprachaktivierung: wieder da");

        // Ein Kanal auf Sprachaktivierung, auf dem du nicht senden darfst, zählt nicht.
        using var b = new Aufbau(FunkDienstAttrappe.Beispiel() with
        {
            Felder = [.. FunkDienstAttrappe.Beispiel().Felder.Select(f => f with { Senden = f.NetzId is not (4 or 5) && f.Senden })],
        });
        var hb = b.Haupt(out var fb, out _, out _, out _);
        Pruefe.Falsch(fb.SprachaktivierungBenutzt || Kennungen(hb).Contains(HauptViewModel.SprachaktivierungKennung), "Sprachaktivierung nur auf Kanälen ohne Senderecht: kein Unterpunkt");

        using var c = new Aufbau(FunkLage.Leer);
        var hc = c.Haupt(out _, out _, out _, out _);
        Pruefe.Falsch(Kennungen(hc).Contains(HauptViewModel.SprachaktivierungKennung), "nicht verbunden: kein Unterpunkt");
        funkfeld.Dispose();
        fb.Dispose();
    }

    /// <summary>
    /// Seit 0.10.0 gibt es den Bereich nur noch auf seiner Seite (bis 0.9.3 derselbe, umgesetzt zwischen unten und der Seite): mit Überschrift,
    /// ohne Hörprobe und Mithören (E19: die eine Hörprobe steht auf Start, Mithören unter Senden > Feinheiten; bis AP2 links unter der Grafik).
    /// Unten steht die Statuszeile; ein Bereich, eine Leiste oder ein Trenner unten gibt es nicht, auch
    /// nicht mit einer alten Datei, in der die Sprachaktivierung aufgeklappt gemerkt ist. Strg+T auf der Seite legt nur den Fokus auf die Grafik;
    /// in einem anderen Bereich steht der Bereich verborgen auf seinem Platz.
    /// </summary>
    [Test]
    public static void Seite_DerBereichNurAufDerSeite_UntenNichts()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            // Wie eine Datei aus 0.9.3 mit aufgeklappter Sprachaktivierung: Das Feld bleibt lesbar und ohne Wirkung.
            a.Ablage.Aktuell.Schwellenbereich.Offen = true;
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt);
            var bereich = haupt.Schwellenbereich;
            var platz = (FrameworkElement)bereich.Parent;
            Pruefe.Gleich(Groessenklasse.Seite, haupt.Klasse, "Einstellungen in der Größe der Seiten, nichts aufgeklappt");
            Pruefe.Gleich("SchwellenSeitenPlatz", platz.Name, "der Bereich steht auf dem Platz der Seite");
            Pruefe.Wahr(platz.Visibility != Visibility.Visible, "in Einstellungen verborgen");
            foreach (var weg in Entfallen)
                Pruefe.Gleich(null, haupt.FindName(weg), weg + ": gibt es nicht mehr (Q87)");

            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            _ = Bildlauf.Zeichnen(haupt);
            // ui-fix 0.6.2: eigene Größenklasse, weil die Grafik dort fest und groß ist (bis 0.6.1 die Größe der Seiten).
            Pruefe.Gleich(Groessenklasse.Sprachaktivierung, haupt.Klasse, "die Seite hat ihre eigene Größe");
            Pruefe.Wahr(ReferenceEquals(haupt.Schwellenbereich, bereich) && ReferenceEquals(bereich.Parent, platz), "derselbe Bereich, auf seinem Platz");
            Pruefe.Wahr(platz.Visibility == Visibility.Visible, "die Seite ist zu sehen");
            Pruefe.Gleich(1, Bildlauf.Nachfahren<Schwellenbereich>(Bildlauf.Wurzel(haupt)).Count(), "nur einmal im Fenster");
            Pruefe.Wahr(Sichtbar(bereich, "Sprachaktivierung"), "Überschrift");
            // E19: keine Hörprobe und kein Mithören mehr auf der Seite (bis AP2 links unter der Grafik).
            Pruefe.Falsch(Bildlauf.Nachfahren<Button>(bereich).Any(k => k.Command == h.Schwelle.Hoerprobe.ProbeCommand), "keine Hörprobe auf der Seite (E19)");
            Pruefe.Falsch(Bildlauf.Nachfahren<CheckBox>(bereich).Any(c => c.Name == "MithoerenSchalter"), "kein Mithören auf der Seite (E19)");
            Pruefe.Wahr(Bildlauf.Nachfahren<Button>(bereich).Any(k => k.Name == "SchwelleEinmessenKnopf"), "Mindestlautstärke einmessen bleibt auf der Seite (E19)");
            var tasten = OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt));
            Pruefe.Wahr(tasten.Select(t => t.Taste).ToHashSet().IsSupersetOf(['M', 'L']), "Automatik und Lernen mit ihren Zugriffstasten");
            Pruefe.Gleich("", string.Join(" | ", OberflaecheHauptfensterTests.Doppelte(tasten, "Seite Sprachaktivierung")), "Zugriffstasten eindeutig");
            h.SprachaktivierungEinstellenCommand.Execute(null);
            Pruefe.Wahr(h.IstSprachaktivierungsseite, "Strg+T auf der Seite: bleibt auf ihr");

            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            _ = Bildlauf.Zeichnen(haupt);
            Pruefe.Wahr(ReferenceEquals(bereich.Parent, platz) && platz.Visibility != Visibility.Visible, "zurück in Einstellungen: der Bereich bleibt verborgen auf seinem Platz");
            Pruefe.Wahr(a.Ablage.Aktuell.Schwellenbereich.Offen, "das alte Feld bleibt stehen, für ältere Clients");
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// Platz der Seite (Sarevocs Vorgabe: mit Platz, ohne Rollbalken in der Größenklasse Seite, wenn möglich): in 960 x 700 in allen drei
    /// Modi weder außen noch in der rechten Spalte ein Rollbalken und die Grafik höher als unten aufgeklappt in der Standardhöhe
    /// (407 x 206, gemessen in OberflaecheStufe2bTests bis 0.9.3; als Seite 394 x 236 bis 273); in der Mindestgröße der Seiten
    /// (640 x 480) darf sie rollen, bleibt aber bedienbar. Anthrazit bei 100 und 175 %. Seit ui-fix 0.6.2 hat die Seite eine eigene Größenklasse:
    /// ohne Rollbalken in ihrer Voreinstellung 1280 x 900, in ihrer Mindestgröße 960 x 480 rollt sie, die Grafik bleibt fest und groß.
    /// </summary>
    [Test]
    public static void Seite_PlatzOhneRollbalken()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var h = a.Haupt(out var funkfeld, out var schwelle, out _, out _);
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            // ui-fix 0.6.2: eigene Größenklasse; ab Werk so groß, dass die Einstellungen rechts neben der festen, großen Grafik stehen.
            Pruefe.Gleich(Fenstergroessen.SprachaktivierungVoreinstellung, new Size(haupt.Width, haupt.Height), "ab Werk 1280 x 900");
            foreach (var modus in Modi)
                foreach (var skala in Skalen)
                {
                    schwelle.Modus = modus;
                    _ = Bildlauf.Zeichnen(haupt, skala, Fenstergroessen.SprachaktivierungVoreinstellung);
                    var was = T($"{modus}, {skala * 100:0} %");
                    var aussen = (ScrollViewer)haupt.FindName("SchwellenSeitenPlatz");
                    var rechts = Bildlauf.Nachfahren<ScrollViewer>(haupt.Schwellenbereich).Single();
                    var grafik = haupt.Schwellenbereich.Verlaufsgrafik;
                    Console.WriteLine(T($"  Seite {was}: Grafik {grafik.ActualWidth:0} x {grafik.ActualHeight:0}, rechts {rechts.ExtentHeight:0} von {rechts.ViewportHeight:0}, außen {aussen.ExtentHeight:0} von {aussen.ViewportHeight:0}, Bereich {haupt.Schwellenbereich.ActualHeight:0}"));
                    Pruefe.Gleich(Visibility.Collapsed, aussen.ComputedVerticalScrollBarVisibility, was + ": außen kein Rollbalken");
                    Pruefe.Gleich(Visibility.Collapsed, rechts.ComputedVerticalScrollBarVisibility, was + ": rechts kein Rollbalken");
                    // Seit Klangstufe C1 (Punkt 9) hat die Grafik als Seite eine feste Größe (bis dahin "mit Platz": mindestens 380 breit und höher als unten).
                    Pruefe.Wahr(Math.Abs(grafik.ActualWidth - Schwellenbereich.SeitenGrafik.Width) < 1 && Math.Abs(grafik.ActualHeight - Schwellenbereich.SeitenGrafik.Height) < 1, T($"{was}: Grafik in fester Größe ({grafik.ActualWidth:0} x {grafik.ActualHeight:0})"));
                }

            schwelle.Modus = GateModus.Lernend;
            _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.SprachaktivierungMindest);
            var aussenKlein = (ScrollViewer)haupt.FindName("SchwellenSeitenPlatz");
            Pruefe.Gleich(Visibility.Visible, aussenKlein.ComputedVerticalScrollBarVisibility, "Mindestgröße: die Seite rollt (Q12: auf Seiten erlaubt)");
            var klein = haupt.Schwellenbereich.Verlaufsgrafik;
            Console.WriteLine(T($"  Seite Lernend in 960 x 480: Grafik {klein.ActualWidth:0} x {klein.ActualHeight:0}"));
            // ui-fix 0.6.2: Die Grafik wird nie kleiner (bis 0.6.1 hier mindestens 140 x 120).
            Pruefe.Wahr(Math.Abs(klein.ActualWidth - Schwellenbereich.SeitenGrafik.Width) < 1 && Math.Abs(klein.ActualHeight - Schwellenbereich.SeitenGrafik.Height) < 1,
                T($"Mindestgröße: Grafik in fester Größe ({klein.ActualWidth:0} x {klein.ActualHeight:0})"));
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// Füllt den Verlauf mit 10 s der Vorführszene (Sprache, Klappern, Lärm im Hintergrund), gerechnet mit der Einstellung, die
    /// gerade gilt; so steht die rote Linie der Automatik dort, wo die Grenzlinie sie hält.
    /// </summary>
    private static void Verlauf(Aufbau a, SchwellenViewModel schwelle, ref long zeit)
    {
        var szene = new Funk.Client.Klang.Vorfuehrungsszene(7);
        var gate = a.Maschine.Gate;
        for (var ms = 0L; ms <= 20_000; ms += 20) a.Maschine.Melden(szene.Naechste(ms, gate, true, false) with { ZeitMs = zeit + ms });
        zeit += 20_020;
        schwelle.Takt();
    }

    /// <summary>
    /// Sichtproben der Stufe 2c für Sarevoc (mit <c>FUNK_BILDER</c>): Startseite und Bereiche mit Glasdesign in Anthrazit; die Seite Sprachaktivierung in Fest (Linie mit Griff), Automatik (rote Linie, Grenzlinie mit Griff, Zone)
    /// und Lernend (Korridor); Einstellungen mit dem Unterpunkt (bis 0.9.3 Audio, unten aufgeklappt); die Grafik in Automatik vergrößert. Jede Seite ohne
    /// angeschnittenen Schatten.
    /// </summary>
    [Test]
    public static void Stufe2c_Sichtproben()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkDienstAttrappe.Raidabend());
            a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
            a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
            a.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
            a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
            var dienst = new VerwaltungsdienstAttrappe();
            using var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
            a.Verwaltung = teil;
            var h = a.Haupt(out var funkfeld, out var schwelle, out _, out _);
            a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Funk.Protokoll.Steuerung.Stufe.Admin, Admin = Funk.Protokoll.Steuerung.AdminStatus.Aktiv } });
            Pruefe.Wahr(h.Navigation.Any(b => b.Kennung == HauptViewModel.SprachaktivierungKennung), "Raidabend: Unterpunkt da");
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            var n = 0;
            var zeit = 0L;
            void Bild(string name, Size groesse, double skala = 1)
            {
                var (bild, _) = Bildlauf.Zeichnen(haupt, skala, groesse);
                OberflaecheStartseiteTests.SchattenFrei(Bildlauf.Wurzel(haupt), name);
                Bildablage.Ablegen(bild, T($"{++n:00}-{name}"));
            }

            foreach (var (kennung, groesse) in new[]
                     {
                         (HauptViewModel.StartKennung, Fenstergroessen.KleinMindest), (HauptViewModel.EinstellungenKennung, Fenstergroessen.SeiteVoreinstellung),
                         (HauptViewModel.RaidKennung, Fenstergroessen.GrossVoreinstellung), (HauptViewModel.VerwaltungKennung, Fenstergroessen.GrossVoreinstellung),
                     })
            {
                h.AktiverBereich = h.Navigation.First(b => b.Kennung == kennung);
                Bild(kennung + "-glas031-anthrazit", groesse);
            }
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);

            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            schwelle.Modus = GateModus.Fest;
            schwelle.SchwelleDb = -44;
            Verlauf(a, schwelle, ref zeit);
            Bild("sprachaktivierung-fest-linie-mit-griff", Fenstergroessen.SeiteVoreinstellung);
            schwelle.Modus = GateModus.Automatisch;
            // Seit Klangstufe C1 der Korridor relativ zur gelernten Stimme statt der Grenzlinie.
            a.Maschine.Korridor = new KorridorStand(-23f, float.NaN, float.NaN);
            schwelle.Takt();
            Verlauf(a, schwelle, ref zeit);
            Bild("sprachaktivierung-automatik-rote-linie-grenzlinie-zone", Fenstergroessen.SeiteVoreinstellung);
            var grafik = haupt.Schwellenbereich.Verlaufsgrafik;
            var nah = OberflaecheHilfeTests.Zeichnen(new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current!.Resources["Pinsel.Flaeche"],
                Padding = new Thickness(8),
                Child = new Funk.Client.Steuerelemente.SchwellenGrafik { Modell = schwelle, Width = 640, Height = 360 },
            }, 2);
            Bildablage.Ablegen(nah, T($"{++n:00}-grafik-automatik-200"));
            schwelle.Modus = GateModus.Lernend;
            schwelle.KorridorVonDb = -58;
            schwelle.KorridorBisDb = -36;
            Verlauf(a, schwelle, ref zeit);
            Bild("sprachaktivierung-lernend-korridor", Fenstergroessen.SeiteVoreinstellung);
            Pruefe.Wahr(grafik.ActualHeight >= Schwellenbereich.SeitenGrafik.Height - 1, "Grafik gezeichnet (seit Klangstufe C1 in fester Größe)");

            schwelle.Modus = GateModus.Automatisch;
            Verlauf(a, schwelle, ref zeit);
            // Seit 0.10.0 klappt unten nichts mehr auf; der Unterpunkt steht unter Einstellungen (E3).
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            Bild("einstellungen-mit-unterpunkt-automatik", Fenstergroessen.SeiteVoreinstellung);
            funkfeld.Dispose();
        });
    }
}
