// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Größenklassen des Hauptfensters (Sarevoc, 02.10.2026, Q19 und Vorgabe j; Fassung 0.6.0, Stufe 1, 2 und 2b; seit 0.10.0 ohne die Klassen mit
/// aufgeklappter Sprachaktivierung, Klein mindestens 900 x 900, Q89): Klein für die Startseite, Seite für die übrigen Seiten, Brett und
/// Verwaltung, die Seite Sprachaktivierung, jede mit eigener gemerkter Größe. Beim Wechsel bleibt die linke obere Ecke; passt das Fenster dann
/// nicht mehr in den Arbeitsbereich, rückt es so weit nach oben oder links wie nötig, bei 100 und 175 %. Maximiert ändert sich nichts an der
/// Größe, und beim Wiederherstellen gilt die Klasse von jetzt. Reine Rechnung in <see cref="Fenstergroessen"/>, das Fenster nur
/// im Speicher (<see cref="Bildlauf"/>). Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheFenstergroessenTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    [Test]
    public static void Fenstergroessen_KlasseJeBereich()
    {
        Pruefe.Gleich(Groessenklasse.Klein, Fenstergroessen.KlasseFuer(HauptViewModel.StartKennung), "Start");
        Pruefe.Gleich(Groessenklasse.Seite, Fenstergroessen.KlasseFuer("einstellungen"), "Einstellungen: eigene Größe, die Startseite bleibt klein (Q19)");
        Pruefe.Gleich(Groessenklasse.Seite, Fenstergroessen.KlasseFuer("audio"), "Audio");
        Pruefe.Gleich(Groessenklasse.Seite, Fenstergroessen.KlasseFuer("netze"), "Meine Kanäle");
        Pruefe.Gleich("seite", Fenstergroessen.Schluessel(Groessenklasse.Seite), "Schlüssel seite");
        Pruefe.Gleich(Groessenklasse.Brett, Fenstergroessen.KlasseFuer(HauptViewModel.RaidKennung), "Brett");
        Pruefe.Gleich(Groessenklasse.Verwaltung, Fenstergroessen.KlasseFuer(HauptViewModel.VerwaltungKennung), "Verwaltung");
        Pruefe.Gleich(Groessenklasse.Sprachaktivierung, Fenstergroessen.KlasseFuer(HauptViewModel.SprachaktivierungKennung), "Seite Sprachaktivierung");
        Pruefe.Gleich(Groessenklasse.Klein, Fenstergroessen.KlasseFuer(null), "ohne Bereich klein");
        Pruefe.Gleich("start", Fenstergroessen.Schluessel(Groessenklasse.Klein), "Schlüssel der Startseite seit Stufe 2b");
        Pruefe.Gleich("sprachaktivierung-seite", Fenstergroessen.Schluessel(Groessenklasse.Sprachaktivierung), "Schlüssel der Seite Sprachaktivierung");
        Pruefe.Wahr(Fenstergroessen.Veraltet.SequenceEqual(["klein", "sprachaktivierung"]), "die Schlüssel aus Stufe 1 und 2 gelten nicht mehr");
        Pruefe.Gleich("brett", Fenstergroessen.Schluessel(Groessenklasse.Brett), "Schlüssel brett");
        Pruefe.Gleich("verwaltung", Fenstergroessen.Schluessel(Groessenklasse.Verwaltung), "Schlüssel verwaltung");
        // 0.10.0 (E5): Die Klassen mit aufgeklappter Sprachaktivierung gibt es nicht mehr.
        Pruefe.Gleich("Klein Brett Verwaltung Seite Sprachaktivierung", string.Join(" ", Enum.GetNames<Groessenklasse>()), "fünf Klassen, keine aufgeklappte");
    }

    [Test]
    public static void Fenstergroessen_VoreinstellungenMindestUndMerken()
    {
        var gemerkt = new Dictionary<string, Fenstergroesse>(StringComparer.Ordinal);
        var g = new Fenstergroessen(gemerkt);
        Pruefe.Gleich(new Size(900, 900), g.Groesse(Groessenklasse.Klein), "Klein: so klein wie möglich, die Mindestgröße (seit 0.10.0 900 x 900, Q89)");
        Pruefe.Gleich(new Size(1120, 780), g.Groesse(Groessenklasse.Brett), "Brett wie heute");
        Pruefe.Gleich(new Size(1120, 780), g.Groesse(Groessenklasse.Verwaltung), "Verwaltung wie heute");

        // Ältere Datei (0.5.x): nur der Platz des Hauptfensters mit gezogener Größe. Sie wird einmal die Größe der Seiten (Audio,
        // Netze, Einstellungen); die Startseite bleibt klein. Danach schreibt HauptfensterOrt die Größe der jeweils aktiven Klasse.
        var altDict = new Dictionary<string, Fenstergroesse>(StringComparer.Ordinal);
        var alt = new Fenstergroessen(altDict, new Size(1000, 600));
        Pruefe.Gleich(new Size(1000, 600), alt.Groesse(Groessenklasse.Seite), "Seite aus einer älteren Datei: die zuletzt gemerkte Größe des Hauptfensters");
        Pruefe.Gleich(new Fenstergroesse(1000, 600), altDict["seite"], "einmal übernommen");
        Pruefe.Gleich(new Size(900, 900), alt.Groesse(Groessenklasse.Klein), "die Startseite bleibt klein");
        Pruefe.Gleich(new Size(1120, 780), alt.Groesse(Groessenklasse.Brett), "Brett bleibt bei der Voreinstellung");
        var spaeter = new Fenstergroessen(altDict, new Size(1400, 900));
        Pruefe.Gleich(new Size(1000, 600), spaeter.Groesse(Groessenklasse.Seite), "mit Einträgen zählt der alte Platz nicht mehr");
        Pruefe.Gleich(new Size(960, 700), g.Groesse(Groessenklasse.Seite), "Seite ohne gemerkte Größe: wie bis Stufe 1");
        Pruefe.Gleich(new Size(640, 480), Fenstergroessen.Mindest(Groessenklasse.Seite), "Mindestgröße Seite wie bis Stufe 1");

        g.Merken(Groessenklasse.Brett, 1400.004, 900);
        Pruefe.Gleich(new Fenstergroesse(1400, 900), gemerkt["brett"], "gezogen: unter dem Schlüssel, auf zwei Stellen gerundet");
        Pruefe.Gleich(new Size(1400, 900), g.Groesse(Groessenklasse.Brett), "Brett: die gezogene Größe");
        g.Merken(Groessenklasse.Klein, 1300, 960);
        Pruefe.Gleich(new Size(1300, 960), g.Groesse(Groessenklasse.Klein), "Klein gezogen");
        // Q89 (E5): Eine kleinere gemerkte Größe hebt die Mindestgröße an, etwa 760 x 660 aus 0.9.3.
        g.Merken(Groessenklasse.Klein, 760, 660);
        Pruefe.Gleich(new Fenstergroesse(760, 660), gemerkt["start"], "gemerkt bleibt, was gezogen wurde");
        Pruefe.Gleich(new Size(900, 900), g.Groesse(Groessenklasse.Klein), "Klein aus 0.9.3 (760 x 660): jetzt 900 x 900");
        g.Merken(Groessenklasse.Klein, 1200, 700);
        Pruefe.Gleich(new Size(1200, 900), g.Groesse(Groessenklasse.Klein), "breit genug, aber zu niedrig: nur die Höhe hebt sich");
        g.Merken(Groessenklasse.Verwaltung, double.NaN, 500);
        Pruefe.Falsch(gemerkt.ContainsKey("verwaltung"), "Unbrauchbares wird nicht gemerkt");
        g.Merken(Groessenklasse.Verwaltung, 0.5, 500);
        Pruefe.Falsch(gemerkt.ContainsKey("verwaltung"), "zu klein wird nicht gemerkt");

        Pruefe.Gleich(new Size(900, 900), Fenstergroessen.Mindest(Groessenklasse.Klein), "Mindestgröße Klein: Start und erster Start ohne Rollbalken (Q89, Weg a)");
        Pruefe.Gleich(Fenstergroessen.KleinMindest, Fenstergroessen.KleinVoreinstellung, "Klein öffnet so klein wie möglich");
        Pruefe.Gleich(new Size(560, 440), Fenstergroessen.Mindest(Groessenklasse.Brett), "Mindestgröße Brett wie heute");
        Pruefe.Gleich(new Size(560, 440), Fenstergroessen.Mindest(Groessenklasse.Verwaltung), "Mindestgröße Verwaltung wie heute");

        // Stufe 1 legte unter "klein" die alte, große Größe von 0.5.x ab (Befund N5), unter "sprachaktivierung" stand die Breite für
        // drei Spalten: Beides gilt nicht mehr und fällt beim Aufbau weg. Die Schlüssel der aufgeklappten Klassen (Stufe 2b bis 0.9.3)
        // bleiben stehen, für ältere Clients, und werden nicht mehr gelesen.
        var stufe1 = new Dictionary<string, Fenstergroesse>(StringComparer.Ordinal)
        {
            ["klein"] = new(1120, 780), ["sprachaktivierung"] = new(1100, 1046), ["brett"] = new(1400, 900),
            ["start-aufgeklappt"] = new(760, 1006), ["seite-aufgeklappt"] = new(960, 1046),
        };
        var neu = new Fenstergroessen(stufe1);
        Pruefe.Gleich(new Size(900, 900), neu.Groesse(Groessenklasse.Klein), "Datei aus Stufe 1: die Startseite klein");
        Pruefe.Gleich(new Size(960, 700), neu.Groesse(Groessenklasse.Seite), "die aufgeklappte Größe gilt nicht für die Seiten");
        Pruefe.Wahr(stufe1.Keys.Order(StringComparer.Ordinal).SequenceEqual(["brett", "seite-aufgeklappt", "start-aufgeklappt"]),
            "veraltete Schlüssel entfernt, brett und die aufgeklappten bleiben: " + string.Join(",", stufe1.Keys));
        foreach (var k in Enum.GetValues<Groessenklasse>())
        {
            var m = Fenstergroessen.Mindest(k);
            var v = new Fenstergroessen(new Dictionary<string, Fenstergroesse>(StringComparer.Ordinal)).Groesse(k);
            Pruefe.Wahr(v.Width >= m.Width && v.Height >= m.Height, k + ": Voreinstellung nicht unter der Mindestgröße");
        }
    }

    [Test]
    public static void Fenstergroessen_Wechsel_LageBleibt_AmRandGeschoben_Bei100Und175Prozent()
    {
        var mindest = Fenstergroessen.GrossMindest;
        var gross = Fenstergroessen.GrossVoreinstellung;
        // 100 %: Full HD, Arbeitsbereich 1920 x 1040. Mitten drin bleibt die Ecke.
        var p = Fenstergroessen.Wechsel(new FensterOrt(100, 80, 960, 700), gross, [FullHd], mindest);
        Pruefe.Gleich(new Platz(100, 80, 1120, 780, FullHd), p, "100 %: Ecke bleibt, neue Größe");
        // Rechts unten passt es nicht mehr: so weit nach links und oben wie nötig.
        p = Fenstergroessen.Wechsel(new FensterOrt(900, 300, 960, 700), gross, [FullHd], mindest);
        Pruefe.Gleich(new Platz(1920 - 1120, 1040 - 780, 1120, 780, FullHd), p, "100 %: nach links und oben geschoben");
        // Nur unten: nur nach oben.
        p = Fenstergroessen.Wechsel(new FensterOrt(100, 500, 960, 700), gross, [FullHd], mindest);
        Pruefe.Gleich(new Platz(100, 260, 1120, 780, FullHd), p, "100 %: nur nach oben");
        // 175 %: 1120 x 780 Einheiten sind 1960 x 1365 Bildpunkte.
        p = Fenstergroessen.Wechsel(new FensterOrt(2000, 800, 960, 700), gross, ZweiMonitore.Alle(), mindest);
        Pruefe.Gleich(new Platz(3840 - 1960, 2076 - 1365, 1120, 780, TestHaupt), p, "175 %: in Bildpunkten gerechnet, Taskleiste beachtet");
        p = Fenstergroessen.Wechsel(new FensterOrt(4000, 100, 960, 700), gross, ZweiMonitore.Alle(), mindest);
        Pruefe.Gleich(new Platz(4000, 100, 1120, 780, TestZweiter), p, "175 %: auf dem zweiten Monitor, dort passt es");
        // Kleiner Laptop: Klein (seit 0.10.0 900 x 900, Q89) passt nicht in die Höhe, das Fenster wird so hoch wie der Arbeitsbereich; dann rollt
        // Start dort (bis 0.9.3 stand hier die aufgeklappte Sprachaktivierung).
        p = Fenstergroessen.Wechsel(new FensterOrt(200, 100, 960, 700), Fenstergroessen.KleinVoreinstellung, [Laptop], Fenstergroessen.Mindest(Groessenklasse.Klein));
        Pruefe.Gleich(new Platz(200, 0, 900, 728, Laptop), p, "Laptop: begrenzt auf den Arbeitsbereich und nach oben geschoben");
        // Unter der Mindestgröße: die Mindestgröße.
        p = Fenstergroessen.Wechsel(new FensterOrt(0, 0, 960, 700), new Size(10, 10), [FullHd], mindest);
        Pruefe.Gleich(new Platz(0, 0, 560, 440, FullHd), p, "Mindestgröße gewinnt");
    }

    private static HauptViewModel MitBrettUndVerwaltung(Aufbau a)
    {
        var dienst = new VerwaltungsdienstAttrappe();
        var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
        a.Verwaltung = teil;
        var h = a.Haupt(out _, out _, out _, out _);
        a.Dienst.LageSetzen(l => l with { Konto = new KontoInfo(9, "Nordlicht", "nordlicht", Stufe.Admin, [Recht.Zuweisen]) { Admin = AdminStatus.Aktiv } });
        Pruefe.Wahr(h.Bereiche.Any(b => b.Kennung == HauptViewModel.RaidKennung) && h.Bereiche.Any(b => b.Kennung == HauptViewModel.VerwaltungKennung), "Brett und Verwaltung da");
        return h;
    }

    [Test]
    public static void Hauptfenster_WechseltDieGroesseJeKlasse_MerktGezogene_MaximiertBleibt()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer with { Status = Verbindungsstatus.Aktiv });
            var h = MitBrettUndVerwaltung(a);
            Bereich Bereich(string kennung) => h.Bereiche.First(b => b.Kennung == kennung);
            h.AktiverBereich = Bereich(HauptViewModel.StartKennung);
            var haupt = new Hauptfenster(h, a.Ablage, ZweiMonitore);
            fenster.Add(haupt);
            Bildlauf.Leeren();
            var fuehrung = haupt.Platzfuehrung;
            var (links, oben) = (haupt.Left, haupt.Top);
            Pruefe.Gleich(new Size(900, 900), new Size(haupt.Width, haupt.Height), "Start: klein (seit 0.10.0 900 x 900, Q89)");
            Pruefe.Gleich(new Size(900, 900), new Size(haupt.MinWidth, haupt.MinHeight), "Mindestgröße klein: Start und erster Start ohne Rollbalken (Q89)");
            Pruefe.Gleich(Groessenklasse.Klein, haupt.Klasse, "Klasse klein");

            h.AktiverBereich = Bereich(HauptViewModel.RaidKennung);
            Pruefe.Gleich(new Size(1120, 780), new Size(haupt.Width, haupt.Height), "Brett: die große Voreinstellung");
            Pruefe.Gleich(new Size(560, 440), new Size(haupt.MinWidth, haupt.MinHeight), "Mindestgröße Brett wie heute");
            Pruefe.Wahr(haupt.Left == links && haupt.Top == oben, "die Ecke bleibt");
            // Am Rand ziehen: gemerkt für das Brett, nicht für Klein.
            fuehrung.BewegungBeginnt();
            haupt.Width = 1400;
            haupt.Height = 900;
            fuehrung.BewegungEndet();
            Pruefe.Gleich(new Fenstergroesse(1400, 900), a.Ablage.Aktuell.HauptfensterGroessen!["brett"], "gezogen: unter brett gemerkt");
            Pruefe.Falsch(a.Ablage.Aktuell.HauptfensterGroessen!.ContainsKey("klein"), "Klein nicht angefasst");
            Pruefe.Wahr(a.Ablage.Aktuell.HauptfensterOrt is { Breite: 1400, Hoehe: 900 }, "HauptfensterOrt weiter für ältere Clients mitgeschrieben");

            h.AktiverBereich = Bereich("einstellungen");
            Pruefe.Gleich(new Size(960, 700), new Size(haupt.Width, haupt.Height), "Einstellungen: die Größe der Seiten");
            h.AktiverBereich = Bereich(HauptViewModel.RaidKennung);
            Pruefe.Gleich(new Size(1400, 900), new Size(haupt.Width, haupt.Height), "Brett: die zuletzt dort gezogene Größe");
            h.AktiverBereich = Bereich(HauptViewModel.VerwaltungKennung);
            Pruefe.Gleich(new Size(1120, 780), new Size(haupt.Width, haupt.Height), "Verwaltung: eigene Klasse, noch die Voreinstellung");
            h.AktiverBereich = Bereich(HauptViewModel.EinstellungenKennung);
            Pruefe.Gleich(new Size(960, 700), new Size(haupt.Width, haupt.Height), "Einstellungen nach der Verwaltung: die Größe der Seiten");

            // Ein größerer Bereich, der an der Ecke nicht passt, rückt nur so weit nach links und oben wie nötig und kommt beim Wechsel zurück
            // wieder an die alte Stelle. Bis 0.9.3 stand hier die aufgeklappte Sprachaktivierung; seit 0.10.0 (E5) gibt es sie nicht mehr, das
            // Brett in seiner gezogenen Größe zeigt dasselbe.
            fuehrung.BewegungBeginnt();
            (haupt.Left, haupt.Top) = (1100, 400);
            fuehrung.BewegungEndet();
            var (links2, oben2) = (haupt.Left, haupt.Top);
            h.AktiverBereich = Bereich(HauptViewModel.RaidKennung);
            Pruefe.Gleich(new Size(1400, 900), new Size(haupt.Width, haupt.Height), "Brett: die gezogene Größe");
            // 2450 x 1575 Bildpunkte passen ab (1925, 700) nicht in 3840 x 2076: nur so weit nach links und oben wie nötig. Die Seite (1680 x 1225)
            // passt dort, das Fenster kommt also zurück.
            Pruefe.Wahr(Math.Abs((haupt.Left * 1.75) - (3840 - 2450)) < 1.01 && Math.Abs((haupt.Top * 1.75) - (2076 - 1575)) < 1.01,
                T($"nach links und oben geschoben, so wenig wie nötig ({haupt.Left * 1.75:0} / {haupt.Top * 1.75:0})"));
            h.AktiverBereich = Bereich(HauptViewModel.EinstellungenKennung);
            Pruefe.Gleich(new Size(960, 700), new Size(haupt.Width, haupt.Height), "zurück: wieder die Größe der Seiten");
            Pruefe.Wahr(haupt.Left == links2 && haupt.Top == oben2, "zurück: wieder an der alten Stelle");
            // Wer das Fenster selbst verschiebt, bestimmt die Stelle: Danach geht es nicht mehr an die alte zurück.
            h.AktiverBereich = Bereich(HauptViewModel.RaidKennung);
            fuehrung.BewegungBeginnt();
            haupt.Left -= 100;
            fuehrung.BewegungEndet();
            var verschoben = (haupt.Left, haupt.Top);
            h.AktiverBereich = Bereich(HauptViewModel.EinstellungenKennung);
            Pruefe.Wahr((haupt.Left, haupt.Top) == verschoben, "selbst verschoben: die Ecke bleibt, wo du sie hingestellt hast");
            fuehrung.BewegungBeginnt();
            (haupt.Left, haupt.Top) = (links, oben);
            fuehrung.BewegungEndet();
            // Die alte Einstellung "Sprachaktivierung aufgeklappt" aus 0.9.3 ändert keine Größe mehr.
            a.Ablage.Aktuell.Schwellenbereich.Offen = true;
            h.AktiverBereich = Bereich(HauptViewModel.RaidKennung);
            Pruefe.Gleich(new Size(1400, 900), new Size(haupt.Width, haupt.Height), "Brett: unverändert");
            h.AktiverBereich = Bereich(HauptViewModel.EinstellungenKennung);
            Pruefe.Gleich(new Size(960, 700), new Size(haupt.Width, haupt.Height), "Einstellungen: die Seite, nicht länger");
            h.AktiverBereich = Bereich(HauptViewModel.RaidKennung);
            Pruefe.Wahr(haupt.Left == links && haupt.Top == oben, "Brett an der alten Ecke");

            // Maximiert: Der Wechsel merkt sich die Klasse, lässt die Größe aber in Ruhe; beim Wiederherstellen gilt die Klasse von jetzt.
            // Ohne Fensterhandle kommt kein StateChanged, deshalb die Stelle direkt.
            haupt.WindowState = WindowState.Maximized;
            haupt.ZustandAnwenden();
            h.AktiverBereich = Bereich(HauptViewModel.EinstellungenKennung);
            Pruefe.Gleich(Groessenklasse.Seite, haupt.Klasse, "maximiert: Klasse gewechselt");
            Pruefe.Gleich(new Size(1400, 900), new Size(haupt.Width, haupt.Height), "maximiert: Width und Height (RestoreBounds) unverändert");
            haupt.WindowState = WindowState.Normal;
            haupt.ZustandAnwenden();
            Pruefe.Gleich(new Size(960, 700), new Size(haupt.Width, haupt.Height), "wiederhergestellt: die Größe der Klasse von jetzt");
            Pruefe.Wahr(haupt.Left == links && haupt.Top == oben, "die Ecke bleibt auch danach");
            Pruefe.Falsch(a.Ablage.Aktuell.HauptfensterGroessen!.ContainsKey("klein"), "Maximieren und Wiederherstellen merken keine Größe");

            // In der Datei und beim nächsten Start.
            a.Ablage.Sichern();
            var datei = a.Speicher.Laden();
            Pruefe.Gleich(new Fenstergroesse(1400, 900), datei.HauptfensterGroessen!["brett"], "in der Datei");
            Pruefe.Wahr(File.ReadAllText(a.Speicher.Pfad).Contains("\"hauptfensterGroessen\"", StringComparison.Ordinal), "unter hauptfensterGroessen");
            h.AktiverBereich = Bereich(HauptViewModel.RaidKennung);
            var zweit = new Hauptfenster(h, a.Ablage, ZweiMonitore);
            fenster.Add(zweit);
            Pruefe.Gleich(new Size(1400, 900), new Size(zweit.Width, zweit.Height), "Neustart im Brett: die gemerkte Größe");
            Pruefe.Gleich(Groessenklasse.Brett, zweit.Klasse, "Neustart: Klasse Brett");
        });
    }

    /// <summary>
    /// Umstieg aus einer Datei von 0.5.x (nur der Platz des Hauptfensters mit seiner Größe, eine Seite wie damals Audio): Seit Stufe 2b
    /// öffnet das Fenster auf der Startseite in der Größe von Klein und übernimmt nur die linke obere Ecke, ganz im Arbeitsbereich;
    /// die alte Größe wird die der Seiten. Bis Stufe 2 öffnete es im alten Bereich in der alten, großen Größe.
    /// </summary>
    [Test]
    public static void Hauptfenster_AeltereDatei_StartKleinAnDerAltenLage_SeiteAusDemAltenPlatz()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer with { Status = Verbindungsstatus.Aktiv });
            // Wie eine Datei aus 0.5.x: nur der Platz, mit gezogener Größe, maximiert, zuletzt auf einer Seite (damals Audio, seit 0.10.0 Einstellungen).
            a.Ablage.Aktuell.HauptfensterOrt = new FensterOrt(1100, 700, 1000, 640, Maximiert: true);
            var h = MitBrettUndVerwaltung(a);
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            Pruefe.Gleich(HauptViewModel.StartKennung, h.AktiverBereich.Kennung, "Umstieg: die Startseite");
            Pruefe.Gleich(Groessenklasse.Klein, haupt.Klasse, "Klasse klein");
            Pruefe.Gleich(new Size(900, 900), new Size(haupt.Width, haupt.Height), "die Größe von Klein, nicht die alte");
            Pruefe.Gleich(WindowState.Normal, haupt.WindowState, "nur die Lage, nicht maximiert");
            // Seit 0.10.0 ist Klein 900 x 900 (Q89): 1100 + 900 passen nicht in 1920 und 700 + 900 nicht in 1040; nach links und oben in den
            // Arbeitsbereich (bis 0.9.3 mit 760 x 660 nur nach oben, die linke Kante blieb).
            Pruefe.Wahr(haupt.Left == 1020 && haupt.Top == 140, T($"die alte Ecke, in den Arbeitsbereich gerückt ({haupt.Left} / {haupt.Top})"));
            Pruefe.Gleich(new Fenstergroesse(1000, 640), a.Ablage.Aktuell.HauptfensterGroessen!["seite"], "die alte Größe gilt den Seiten");
            Pruefe.Falsch(a.Ablage.Aktuell.HauptfensterGroessen!.ContainsKey("start"), "Klein bleibt bei der Voreinstellung");
            // In die Einstellungen: die alte Größe, so nah an der Ecke wie möglich.
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            Pruefe.Gleich(new Size(1000, 640), new Size(haupt.Width, haupt.Height), "Seite");
            Pruefe.Wahr(haupt.Left == 920 && haupt.Top == 140, T($"Einstellungen an den Rand des Arbeitsbereichs geschoben ({haupt.Left} / {haupt.Top})"));
            // Ins Brett: 1120 x 780 passt ab (1020, 140) nicht, das Fenster rückt nach links.
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.RaidKennung);
            Pruefe.Gleich(new Size(1120, 780), new Size(haupt.Width, haupt.Height), "Brett");
            Pruefe.Wahr(haupt.Left == 800 && haupt.Top == 140, T($"an den Rand des Arbeitsbereichs geschoben ({haupt.Left} / {haupt.Top})"));
            // Zurück zur Startseite: wieder klein an der Ecke von vorher.
            h.AktiverBereich = h.Bereiche[0];
            Pruefe.Wahr(new Size(haupt.Width, haupt.Height) == new Size(900, 900) && haupt.Left == 1020 && haupt.Top == 140,
                T($"Start wieder klein an der Ecke ({haupt.Left} / {haupt.Top})"));
            // Ein zweiter Aufbau (die Datei hat jetzt Größen je Klasse) ist kein Umstieg mehr: Der Bereich bleibt.
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            var zweit = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(zweit);
            Pruefe.Gleich(HauptViewModel.EinstellungenKennung, h.AktiverBereich.Kennung, "kein zweiter Umstieg");
            Pruefe.Gleich(new Size(1000, 640), new Size(zweit.Width, zweit.Height), "Einstellungen in der Größe der Seiten");
        });
    }

    /// <summary>Laptop mit Full HD bei 150 % (Windows-Voreinstellung vieler 15-Zoll-Geräte): Arbeitsbereich 1280 x 688 Einheiten.</summary>
    private static readonly Bildschirm Laptop150 = new(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1032), 1.5, true);

    /// <summary>
    /// Gegenprüfung Stufe 1: Die Mindestgröße einer Klasse (bis 0.9.3 mit Sprachaktivierung 716 Einheiten hoch, seit 0.10.0 Klein 900 x 900)
    /// darf das Fenster nicht über den Arbeitsbereich hinaus treiben. WPF hält MinHeight auch gegen eine kleinere Height; sonst ragte das
    /// Fenster auf einem Laptop mit 150 % unter die Taskleiste, und am unteren Rand lägen Statuszeile und Fußzeile. Dort rollt Start dann
    /// (Q89, im Modulbericht mit Zahlen).
    /// </summary>
    [Test]
    public static void Hauptfenster_Mindestgroesse_NieUeberDenArbeitsbereich()
    {
        foreach (var k in Enum.GetValues<Groessenklasse>())
        {
            var m = Fenstergroessen.Mindest(k, Laptop150);
            Pruefe.Wahr(m.Width <= 1280 && m.Height <= 688, T($"{k}: auf dem Laptop höchstens der Arbeitsbereich ({m.Width:0} x {m.Height:0})"));
            Pruefe.Gleich(Fenstergroessen.Mindest(k), Fenstergroessen.Mindest(k, TestHaupt), k + ": bei Sarevoc (4K, 175 %) ungekürzt");
            Pruefe.Gleich(Fenstergroessen.Mindest(k), Fenstergroessen.Mindest(k, null), k + ": ohne Monitor ungekürzt");
        }
        Bildlauf.Ausfuehren(fenster =>
        {
            const double unten = 1032;
            using var a = new Aufbau(FunkLage.Leer with { Status = Verbindungsstatus.Aktiv });
            var h = a.Haupt(out _, out _, out _, out _);
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([Laptop150]));
            fenster.Add(haupt);
            void Pruefen(Hauptfenster f, string wann)
            {
                Pruefe.Wahr(f.MinHeight * 1.5 <= unten + 0.5 && f.MinWidth * 1.5 <= 1920 + 0.5,
                    T($"{wann}: Mindestgröße {f.MinWidth:0} x {f.MinHeight:0} passt in 1280 x 688"));
                var boden = (f.Top + Math.Max(f.Height, f.MinHeight)) * 1.5;
                Pruefe.Wahr(boden <= unten + 0.5, T($"{wann}: Unterkante {boden:0} nicht unter dem Arbeitsbereich ({unten:0})"));
            }
            Pruefen(haupt, "Seite");
            h.AktiverBereich = h.Bereiche[0];
            Pruefe.Gleich(Groessenklasse.Klein, haupt.Klasse, "Start");
            Pruefe.Gleich(new Size(900, 688), new Size(haupt.MinWidth, haupt.MinHeight), "Klein auf dem Laptop: so hoch wie der Arbeitsbereich, dort rollt Start");
            Pruefen(haupt, "Start");
            // Neustart auf der Startseite: dieselbe Grenze schon beim Aufbau.
            var zweit = new Hauptfenster(h, a.Ablage, new FesteBildschirme([Laptop150]));
            fenster.Add(zweit);
            Pruefen(zweit, "Neustart auf Start");
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            Pruefe.Gleich(new Size(640, 480), new Size(haupt.MinWidth, haupt.MinHeight), "wieder die Mindestgröße der Seiten");
            Pruefen(haupt, "zurück auf der Seite");
            // Wo der Arbeitsbereich reicht, bleibt die Mindestgröße der Klasse ungekürzt (Full HD bei 100 %).
            h.AktiverBereich = h.Bereiche[0];
            var voll = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(voll);
            Pruefe.Gleich(Fenstergroessen.Mindest(Groessenklasse.Klein), new Size(voll.MinWidth, voll.MinHeight), "Full HD: ungekürzt");
        });
    }
}
