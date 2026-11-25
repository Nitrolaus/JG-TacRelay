// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Klangstufe C1 in der Oberfläche (KLANGSTUFE-C-ENTWURF.md im Planungsordner): Umstieg der Einstellungen aus 0.6.0 (Grenzlinie wird
/// Mindest-Funklautstärke relativ zur Stimme, ein nie gelernter Startwert der Verstärkung fällt weg), der Korridor als ein Modell für
/// Pegelbalken und Schwellengrafik (Ziehen, Zone, Tastatur, Tooltips in Wörtern), "Mindestlautstärke einmessen", Start immer auf der
/// Startseite (Q25), feste Größe der Schwellengrafik auf der Seite (Punkt 9) und die Sichtproben (mit FUNK_BILDER). Gezeichnet ohne
/// Fensterhandle (<see cref="Bildlauf"/>), Klangmaschine als Attrappe.
/// </summary>
internal static class OberflaecheKlangC1Tests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Die Stimme gilt als gelernt, am Ziel der Pegelstufe (-23 dBFS).</summary>
    private static void StimmeGelernt(Aufbau a) => a.Maschine.Korridor = new KorridorStand(Pegelstufe.ZielDbfs, float.NaN, float.NaN);

    // ------------------------------------------------------------------------------------------------------------------------------
    // Umstieg der Einstellungen aus 0.6.0
    // ------------------------------------------------------------------------------------------------------------------------------

    private static ClientEinstellungen Laden(Testordner o, string json, out EinstellungsSpeicher speicher)
    {
        speicher = new EinstellungsSpeicher(o.Datei("client-einstellungen.json"));
        File.WriteAllText(speicher.Pfad, json);
        var e = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "Datei gelesen: " + json);
        return e;
    }

    [Test]
    public static void C1_Umstieg060_GrenzlinieWirdMindestabstand_NieGelernteVerstaerkungFaelltWeg()
    {
        using var o = new Testordner();
        var ab = Laden(o, "{ \"version\": 1, \"gate\": { \"modus\": \"Automatisch\", \"grenzlinieDb\": -90 } }", out _);
        Pruefe.Gleich(GateVorgaben.MindestAbstandAbWerkDb, ab.Gate.MindestAbstandDb, "nie verstellt: der neue Wert ab Werk");
        Pruefe.Gleich(2, ab.Version, "Dateiversion 2");

        var selbst = Laden(o, "{ \"version\": 1, \"gate\": { \"modus\": \"Automatisch\", \"grenzlinieDb\": -38 } }", out var speicher);
        Pruefe.Gleich(15f, selbst.Gate.MindestAbstandDb, "selbst gesetzt (-38 dBFS): bleibt, als 15 dB unter der Stimme am Ziel");
        Pruefe.Gleich(GateWerte.MinDb, selbst.Gate.GrenzlinieDb, "keine feste Grenze mehr (sie sperrte ein leises Mikrofon)");

        var lernend = Laden(o, "{ \"version\": 1, \"gate\": { \"modus\": \"Lernend\", \"korridorVonDb\": -45, \"grenzlinieDb\": -90 } }", out _);
        Pruefe.Gleich(22f, lernend.Gate.MindestAbstandDb, "Lernend mit selbst gesetztem Korridor von: dessen Untergrenze bleibt");

        var ungelernt = Laden(o, "{ \"version\": 1, \"audio\": { \"gelernteVerstaerkungDb\": 6.0, \"gelernteVerstaerkungEingang\": \"mic-1\" } }", out _);
        Pruefe.Gleich(null, ungelernt.Audio.GelernteVerstaerkungDb, "der unberührte Startwert (+6 dB) war nie gelernt: fällt weg");
        Pruefe.Gleich(null, ungelernt.Audio.GelernteVerstaerkungEingang, "samt Mikrofon");
        var gelernt = Laden(o, "{ \"version\": 1, \"audio\": { \"gelernteVerstaerkungDb\": 17.0 } }", out _);
        Pruefe.Gleich(17.0, gelernt.Audio.GelernteVerstaerkungDb, "wirklich gelernt: bleibt");

        // Einmal umgestellt, bleibt ein danach gesetzter Wert, auch ganz unten.
        selbst.Gate.MindestAbstandDb = GateVorgaben.MaxMindestAbstandDb;
        speicher.Speichern(selbst);
        using (var json = JsonDocument.Parse(File.ReadAllBytes(speicher.Pfad)))
        {
            Pruefe.Gleich(2, json.RootElement.GetProperty("version").GetInt32(), "geschrieben als Version 2");
            Pruefe.Gleich(60.0, json.RootElement.GetProperty("gate").GetProperty("mindestAbstandDb").GetDouble(), "Abstand in der Datei");
        }
        Pruefe.Gleich(GateVorgaben.MaxMindestAbstandDb, speicher.Laden().Gate.MindestAbstandDb, "kein zweites Umstellen");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Q25: Start immer auf der Startseite
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C1_Start_ImmerStartseite_BereichNurInDerSitzung()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Bereich = "netze";
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        Pruefe.Gleich(HauptViewModel.StartKennung, h.AktiverBereich.Kennung, "Start auf der Startseite, auch wenn die Datei Netze nennt");
        // Seit 0.10.0 (E2): Strg+2 öffnet Einstellungen (bis 0.9.3 Audio, Strg+3 Meine Kanäle).
        h.BereichWaehlenCommand.Execute("2");
        Pruefe.Gleich(HauptViewModel.EinstellungenKennung, h.AktiverBereich.Kennung, "in der Sitzung gewechselt");
        Pruefe.Gleich("netze", a.Ablage.Aktuell.Bereich, "nicht geschrieben (der alte Wert bleibt nur für ältere Clients)");
        a.Ablage.Aktuell.Bereich = null;
        h.BereichWaehlenCommand.Execute("1");
        h.BereichWaehlenCommand.Execute("2");
        Pruefe.Gleich(null, a.Ablage.Aktuell.Bereich, "ein Wechsel schreibt nichts");
        Pruefe.Gleich(HauptViewModel.EinstellungenKennung, h.AktiverBereich.Kennung, "die Sitzung merkt den Bereich");
        funkfeld.Dispose();
        var neu = a.Haupt(out var funkfeld2, out _, out _, out _);
        Pruefe.Gleich(HauptViewModel.StartKennung, neu.AktiverBereich.Kennung, "nächster Start: wieder die Startseite");
        funkfeld2.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Korridor als Modell
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C1_Korridor_Modell_ZiehenZoneTastenGrenzen()
    {
        using var a = new Aufbau();
        StimmeGelernt(a);
        var k = new KorridorViewModel(a.Maschine, a.Ablage);
        Pruefe.Wahr(k.Sichtbar && !k.ZeigeNichtEingemessen, "Automatik mit gelernter Stimme: Korridor sichtbar");
        Pruefe.Gleich(-23.0 - GateVorgaben.MindestAbstandAbWerkDb, k.UntergrenzeDb, "Untergrenze ab Werk unter der Stimme");
        Pruefe.Gleich(-29.0, k.ObergrenzeDb, "Obergrenze Stimme minus 6 dB");

        k.UntergrenzeZiehen(-40);
        Pruefe.Gleich(17f, a.Maschine.Gate.MindestAbstandDb, "gezogen: 17 dB unter der Stimme, an der Maschine");
        Pruefe.Gleich(17f, a.Ablage.Aktuell.Gate.MindestAbstandDb, "in der Ablage");
        k.UntergrenzeZiehen(-20);
        Pruefe.Gleich(-32.0, k.UntergrenzeDb, "nie näher als 3 dB an die Obergrenze");
        k.ObergrenzeZiehen(-10);
        Pruefe.Gleich(-29.0, k.ObergrenzeDb, "die Obergrenze nie näher als 6 dB an die Stimme");
        k.UntergrenzeZiehen(-60);
        k.ObergrenzeZiehen(-70);
        Pruefe.Gleich(k.UntergrenzeDb + 3, k.ObergrenzeDb, "die Obergrenze nie unter die Untergrenze");
        k.ObergrenzeZiehen(-35);
        var breite = k.ObergrenzeDb - k.UntergrenzeDb;
        k.KorridorVerschieben(4);
        Pruefe.Gleich(-31.0, k.ObergrenzeDb, "Zone gezogen: beide hoch");
        Pruefe.Gleich(breite, k.ObergrenzeDb - k.UntergrenzeDb, "die Breite bleibt");
        k.KorridorVerschieben(30);
        Pruefe.Gleich(-29.0, k.ObergrenzeDb, "verschoben nur bis an die Obergrenze");
        Pruefe.Gleich(breite, k.ObergrenzeDb - k.UntergrenzeDb, "auch dort bleibt die Breite");
        var vorher = k.UntergrenzeDb;
        k.UntergrenzeSchieben(-KorridorViewModel.Schritt * 5);
        Pruefe.Gleich(vorher - 5, k.UntergrenzeDb, "Pfeil mit Strg: 5 dB");
        k.ObergrenzeSchieben(-1);
        Pruefe.Gleich(-30.0, k.ObergrenzeDb, "Bild runter: 1 dB");
        a.Ablage.Sichern();
        Pruefe.Gleich(a.Ablage.Aktuell.Gate.MindestAbstandDb, a.Speicher.Laden().Gate.MindestAbstandDb, "gespeichert");

        // Ohne gelernte Stimme: keine Linien, kein Ziehen, der Hinweis in Wörtern.
        a.Maschine.Korridor = KorridorStand.Leer;
        k.Takt();
        var gemerkt = a.Ablage.Aktuell.Gate.MindestAbstandDb;
        k.UntergrenzeZiehen(-30);
        Pruefe.Wahr(!k.Sichtbar && k.ZeigeNichtEingemessen && k.ZeileText == KorridorViewModel.NichtEingemessen, "nicht eingemessen: " + k.ZeileText);
        Pruefe.Gleich(gemerkt, a.Ablage.Aktuell.Gate.MindestAbstandDb, "ohne Stimme ändert Ziehen nichts");
        Pruefe.Wahr(double.IsNaN(k.UntergrenzeDb), "keine Untergrenze");

        // Fest: kein Korridor, der Funker bestimmt die Linie selbst.
        StimmeGelernt(a);
        a.Ablage.Aktuell.Gate.Modus = GateModus.Fest;
        k.Takt();
        k.Auffrischen();
        Pruefe.Wahr(!k.Sichtbar && !k.ZeigeNichtEingemessen, "Fest: weder Korridor noch Hinweis");
        foreach (var text in new[] { KorridorViewModel.HilfeUntergrenze, KorridorViewModel.HilfeObergrenze, KorridorViewModel.HilfeZone, KorridorViewModel.HilfeRoteLinie,
                     KorridorViewModel.HilfeBalken, KorridorViewModel.HilfeNichtEingemessen, k.StandText })
        {
            Pruefe.Falsch(text.Any(char.IsDigit) || text.Contains("dB", StringComparison.Ordinal), "Wörter statt dB: " + text);
        }
        // Gegenprüfung C1, N2 (Klangstufe C2b): ehrlich statt "leiser wird nie ..."; OberflaecheKlangC2bTests.C2b_N2_TexteZurMindestFunklautstaerkeEhrlich.
        Pruefe.Wahr(KorridorViewModel.HilfeUntergrenze.StartsWith("Mindest-Funklautstärke. Untergrenze: Die Automatik stellt sich nie empfindlicher ein", StringComparison.Ordinal),
            "Text der linken Linie");
        Pruefe.Wahr(KorridorViewModel.HilfeBalken.StartsWith("Grün heißt: wird gesendet", StringComparison.Ordinal), "Text des Balkens");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Einmessen
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static async Task C1_Einmessen_SetztVerstaerkungUndUntergrenzeAufEinmal()
    {
        using var a = new Aufbau();
        var k = new KorridorViewModel(a.Maschine, a.Ablage);
        // Punkt 3: Bis einmal eingemessen wurde, steht beim Pegelbalken der Hinweis, daneben der Knopf. Seit 0.10.0 (Gegenlesung Punkt 2: Griffe
        // gehören nicht auf Start) nicht mehr auf der Startseite, sondern unter Einstellungen > Senden > Feinheiten (bis AP2 im Bereich Audio).
        Pruefe.Wahr(k.ZeigeEinmessHinweis && k.ZeileText == KorridorViewModel.NichtEingemessen, "vor dem Einmessen: " + k.ZeileText);
        var bereiche = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster", "Bereiche.xaml"));
        var knopf = bereiche[bereiche.IndexOf("x:Name=\"AudioEinmessenKnopf\"", StringComparison.Ordinal)..];
        Pruefe.Wahr(knopf[..knopf.IndexOf("</StackPanel>", StringComparison.Ordinal)].Contains("{Binding Korridor.ZeileText}", StringComparison.Ordinal),
            "Senden > Feinheiten: Knopf und Hinweis beim Pegelbalken");
        // Seit 0.11.2 stehen Pegelbalken mit Griffen und Einmessen wieder auf Start, unten unter "Senden" (Sarevoc 13.10.2026: "der Audio-Live-Balken mit
        // den verschiebbaren vertikalen Linien muss auf die Startseite"); von 0.10.0 bis 0.11.1 nicht (Q87).
        var start = bereiche[bereiche.IndexOf("x:Name=\"StartEinmessenKnopf\"", StringComparison.Ordinal)..];
        Pruefe.Wahr(start[..start.IndexOf("</StackPanel>", StringComparison.Ordinal)].Contains("x:Name=\"StartPegel\"", StringComparison.Ordinal)
            && start[..start.IndexOf("/>", StringComparison.Ordinal)].Contains("{Binding Korridor.EinmessenCommand}", StringComparison.Ordinal),
            "Start unter Senden: Einmessen und Pegelbalken (seit 0.11.2)");
        Pruefe.Falsch(k.EinmessenCommand.CanExecute(null), "ohne laufendes Mikrofon gesperrt");
        Pruefe.Wahr(k.EinmessenGesperrt?.Contains("Mikrofon", StringComparison.Ordinal) == true, "mit Grund");
        await a.Maschine.StartenAsync(null, null, CancellationToken.None);
        k.Takt();
        Pruefe.Wahr(k.EinmessenCommand.CanExecute(null), "Mikrofon läuft: frei");
        Pruefe.Gleich("Mindestlautstärke einmessen", k.EinmessKnopf, "Knopf");

        k.EinmessenCommand.Execute(null);
        Pruefe.Gleich(1, a.Maschine.MindestEinmessStarts, "an die Maschine");
        Pruefe.Wahr(k.EinmessText.StartsWith("1 von 2: Sprich einen Satz so, wie du funkst.", StringComparison.Ordinal), "Satz 1: " + k.EinmessText);
        Pruefe.Gleich("Einmessen abbrechen", k.EinmessKnopf, "während des Einmessens: abbrechen");
        a.Maschine.MindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Leise, 3000, 0f, 0f, -30f, float.NaN);
        k.Takt();
        Pruefe.Wahr(k.EinmessText.StartsWith("2 von 2: Jetzt derselbe Satz leise, wie zur Familie.", StringComparison.Ordinal), "Satz 2: " + k.EinmessText);

        // Zu wenig Unterschied: freundlich wiederholen lassen, nichts ändern.
        var vorher = a.Ablage.Aktuell.Gate.MindestAbstandDb;
        a.Maschine.MindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.ZuWenigUnterschied, 0, 0f, 0f, -30f, -35f);
        k.Takt();
        Pruefe.Wahr(k.EinmessText.Contains("kaum leiser", StringComparison.Ordinal) && k.EinmessText.Contains("noch einmal", StringComparison.Ordinal), "wiederholen: " + k.EinmessText);
        Pruefe.Gleich(vorher, a.Ablage.Aktuell.Gate.MindestAbstandDb, "nichts geändert");

        // Fertig: Grundverstärkung und Untergrenze auf einmal; die Stimme gilt als gelernt.
        k.EinmessenCommand.Execute(null);
        a.Maschine.MindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Fertig, 0, 9.5f, 12f, -32.5f, -50.5f);
        k.Takt();
        Pruefe.Gleich<double?>(9.5, a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb, "Grundverstärkung gemerkt (Automatik: als gelernt)");
        Pruefe.Gleich(12f, a.Ablage.Aktuell.Gate.MindestAbstandDb, "Untergrenze gemerkt");
        Pruefe.Wahr(a.Maschine.Lautstaerke is { Automatisch: true, FestDb: 9.5f, Gelernt: true, Eingemessen: true }, "an die Maschine, eingemessen: " + a.Maschine.Lautstaerke);
        Pruefe.Gleich(12f, a.Maschine.Gate.MindestAbstandDb, "Gate an der Maschine");
        Pruefe.Wahr(k.EinmessText.StartsWith("Eingemessen.", StringComparison.Ordinal), "fertig: " + k.EinmessText);
        Pruefe.Wahr(a.Ablage.Aktuell.Gate.MindestEingemessen && !k.ZeigeEinmessHinweis, "eingemessen: kein Hinweis mehr");
        a.Ablage.Sichern();
        Pruefe.Wahr(a.Speicher.Laden().Gate.MindestEingemessen, "gemerkt");
        var gesetzt = a.Maschine.LautstaerkeGesetzt;
        k.Takt();
        Pruefe.Gleich(gesetzt, a.Maschine.LautstaerkeGesetzt, "nur einmal übernommen");

        // Feste Verstärkung: als eingemessener fester Wert.
        a.Ablage.Aktuell.Audio.AutomatischeLautstaerke = false;
        k.EinmessenCommand.Execute(null);
        a.Maschine.MindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Fertig, 0, 4f, 10f, -27f, -42f);
        k.Takt();
        Pruefe.Wahr(a.Ablage.Aktuell.Audio is { FesteVerstaerkungDb: 4.0, FestEingemessen: true }, "fest: eingemessen gemerkt");
        Pruefe.Wahr(a.Maschine.Lautstaerke is { Automatisch: false, FestDb: 4f, Gelernt: true }, "fest: als gelernt an die Maschine");
        Pruefe.Wahr(a.Ablage.Aktuell.Audio.LautstaerkeFuerMaschine() is { Automatisch: false, Gelernt: true }, "auch beim nächsten Start");

        // Abbrechen während des Einmessens.
        k.EinmessenCommand.Execute(null);
        k.EinmessenCommand.Execute(null);
        Pruefe.Gleich(MindestEinmessSchritt.Bereit, a.Maschine.MindestEinmessen.Schritt, "abgebrochen");
        Pruefe.Gleich("Mindestlautstärke einmessen", k.EinmessKnopf, "Knopf wieder zum Einmessen");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Pegelbalken und Schwellengrafik
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C1_Pegelbalken_Korridor_TeileTooltipsZiehenTasten()
    {
        Bildlauf.Ausfuehren(ohneFenster =>
        {
            using var a = new Aufbau();
            StimmeGelernt(a);
            var k = new KorridorViewModel(a.Maschine, a.Ablage);
            var b = new Pegelbalken { Width = 450, Height = 36, PegelDb = -30, SchwelleDb = -40, Sendet = true, Korridor = k };
            _ = OberflaecheHilfeTests.Zeichnen(b);
            var mitte = b.ActualHeight / 2;
            double X(double db) => b.XFuer(db);
            Pruefe.Gleich(new Rect(0, 9, 450, 18), b.Band, "der Balken in der Mitte, oben und unten Platz für die Griffe");
            Pruefe.Gleich("Untergrenze", b.TeilAn(new Point(X(k.UntergrenzeDb), mitte)), "linke Linie");
            Pruefe.Gleich("Untergrenze", b.TeilAn(new Point(X(k.UntergrenzeDb) + 2, Pegelbalken.GriffRadius)), "oberer Griff der linken Linie");
            Pruefe.Gleich("Obergrenze", b.TeilAn(new Point(X(k.ObergrenzeDb), b.ActualHeight - Pegelbalken.GriffRadius)), "unterer Griff der rechten Linie");
            Pruefe.Gleich("Zone", b.TeilAn(new Point(X(-35), mitte)), "dazwischen die Zone");
            Pruefe.Gleich("RoteLinie", b.TeilAn(new Point(X(-40), mitte)), "die rote Linie");
            Pruefe.Gleich("Balken", b.TeilAn(new Point(X(-80), mitte)), "links davon der Balken");
            Pruefe.Gleich(KorridorViewModel.HilfeUntergrenze, b.Hilfe(Pegelbalken.Teil.Untergrenze), "Tooltip links");
            Pruefe.Gleich(KorridorViewModel.HilfeBalken, b.Hilfe(Pegelbalken.Teil.Balken), "Tooltip Balken");

            b.Ziehen(new Point(X(k.UntergrenzeDb), mitte), new Point(X(-42), mitte));
            PruefeWert.Nahe(-42, k.UntergrenzeDb, 0.5, "linke Linie gezogen");
            b.Ziehen(new Point(X(-35), mitte), new Point(X(-37), mitte));
            PruefeWert.Nahe(-31, k.ObergrenzeDb, 0.5, "Zone um 2 dB nach links: beide tiefer");
            PruefeWert.Nahe(-44, k.UntergrenzeDb, 0.5, "beide");
            Pruefe.Wahr(b.Taste(Key.Right, strg: false), "Pfeil rechts");
            PruefeWert.Nahe(-43, k.UntergrenzeDb, 0.5, "Pfeil rechts: 1 dB höher");
            Pruefe.Wahr(b.Taste(Key.PageDown, strg: true), "Strg+Bild runter");
            PruefeWert.Nahe(-36, k.ObergrenzeDb, 0.5, "Obergrenze 5 dB tiefer");
            Pruefe.Falsch(b.Taste(Key.A, strg: false), "andere Taste tut nichts");

            // Nicht eingemessen: keine Griffe, der Balken sagt es im Tooltip. Seit ui-fix 0.6.2 (Q39) stehen die Linien grau da, greifbar sind sie nicht.
            a.Maschine.Korridor = KorridorStand.Leer;
            k.Takt();
            _ = OberflaecheHilfeTests.Zeichnen(b);
            Pruefe.Wahr(b.TeilAn(new Point(X(-44), mitte)) is "Balken" or "GrauLinie" && !b.Druecken(new Point(X(-44), mitte)), "keine Linie mehr greifbar");
            Pruefe.Wahr(b.Hilfe(Pegelbalken.Teil.Balken)!.Contains(KorridorViewModel.NichtEingemessen, StringComparison.Ordinal), "Hinweis im Tooltip");
            Pruefe.Falsch(b.Taste(Key.Right, strg: false), "Tasten ohne Wirkung");

            // Ohne Korridor wie bisher: der ganze Balken, kein Fokus.
            var schlicht = new Pegelbalken { Width = 200, Height = 10 };
            _ = OberflaecheHilfeTests.Zeichnen(schlicht);
            Pruefe.Gleich(new Rect(0, 0, 200, 10), schlicht.Band, "ohne Korridor die volle Höhe");
            Pruefe.Falsch(schlicht.Focusable, "ohne Korridor nicht im Tabulator");
        });
    }

    [Test]
    public static void C1_Schwellengrafik_KorridorMitRundenGriffen_ZoneUndTooltips()
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
            double Y(double db) => SchwellenGrafik.DbZuY(db, p);
            var k = s.Korridor;
            Pruefe.Gleich("Untergrenze", g.GriffAn(new Point(p.Left, Y(k.UntergrenzeDb))), "runder Griff links an der Mindest-Funklautstärke");
            Pruefe.Gleich("Untergrenze", g.GriffAn(new Point(p.Right, Y(k.UntergrenzeDb))), "und rechts");
            Pruefe.Gleich("Obergrenze", g.GriffAn(new Point(p.Right, Y(k.ObergrenzeDb))), "Obergrenze");
            Pruefe.Gleich("Zone", g.GriffAn(new Point(p.Left + 100, Y((k.UntergrenzeDb + k.ObergrenzeDb) / 2))), "Zone dazwischen");
            Pruefe.Gleich(KorridorViewModel.HilfeObergrenze, g.HilfeAn(new Point(p.Right, Y(k.ObergrenzeDb))), "Tooltip in Wörtern");
            Pruefe.Gleich(KorridorViewModel.HilfeZone, g.HilfeAn(new Point(p.Left + 100, Y((k.UntergrenzeDb + k.ObergrenzeDb) / 2))), "Tooltip der Zone");
            Pruefe.Gleich(null, g.GriffAn(new Point(p.Left + 100, Y(-80))), "darunter nichts zu greifen");

            // Lernend: derselbe Korridor, solange die Stimme bekannt ist; sonst der von/bis aus 0.6.0.
            s.Modus = GateModus.Lernend;
            _ = OberflaecheHilfeTests.Zeichnen(g);
            Pruefe.Gleich("Untergrenze", g.GriffAn(new Point(p.Right, Y(k.UntergrenzeDb))), "Lernend mit Stimme: der Korridor relativ zur Stimme");
            a.Maschine.Korridor = KorridorStand.Leer;
            s.Takt();
            _ = OberflaecheHilfeTests.Zeichnen(g);
            Pruefe.Wahr(s.ZeigeKorridor, "Lernend ohne Stimme: Korridor von/bis");
            Pruefe.Gleich("KorridorOben", g.GriffAn(new Point(p.Right, Y(s.KorridorBisDb))), "Griff bis");
            s.Modus = GateModus.Automatisch;
            _ = OberflaecheHilfeTests.Zeichnen(g);
            Pruefe.Gleich(null, g.GriffAn(new Point(p.Right, Y(-48))), "Automatik ohne Stimme: nichts zu greifen (keine Grenzlinie mehr)");
            Pruefe.Wahr(s.GrafikHilfe.Contains(KorridorViewModel.NichtEingemessen, StringComparison.Ordinal), "die Hilfe sagt warum");
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 9: feste Größe der Schwellengrafik auf der Seite
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C1_Seite_SchwellengrafikFesteGroesse()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            foreach (var groesse in new[] { Fenstergroessen.SeiteMindest, Fenstergroessen.SeiteVoreinstellung, new Size(1600, 1000) })
            {
                _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                var g = haupt.Schwellenbereich.Verlaufsgrafik;
                Console.WriteLine(T($"  Seite {groesse.Width:0} x {groesse.Height:0}: Grafik {g.ActualWidth:0} x {g.ActualHeight:0}"));
                // Auf ganze Bildpunkte gerundet (Bildlauf zeichnet mit der Skala des Systems).
                Pruefe.Wahr(Math.Abs(g.ActualWidth - Schwellenbereich.SeitenGrafik.Width) < 1 && Math.Abs(g.ActualHeight - Schwellenbereich.SeitenGrafik.Height) < 1,
                    T($"{groesse.Width:0} x {groesse.Height:0}: feste Größe {Schwellenbereich.SeitenGrafik}, war {g.ActualWidth:0.0} x {g.ActualHeight:0.0}"));
            }
            // Bis 0.9.3 folgte die Grafik unten unter der Leiste der Breite des Fensters; seit 0.10.0 gibt es sie nur noch auf der Seite (Q87).
            funkfeld.Dispose();
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Sichtproben
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Verlauf aus der Vorführszene mit Korridor (so steht die rote Linie im Korridor).</summary>
    private static void Verlauf(Aufbau a, HauptViewModel h, ref long zeit)
    {
        var szene = new Funk.Client.Klang.Vorfuehrungsszene(7);
        var gate = a.Maschine.Gate;
        var korridor = a.Maschine.Korridor;
        for (var ms = 0L; ms <= 20_000; ms += 20) a.Maschine.Melden(szene.Naechste(ms, gate, true, false, korridor) with { ZeitMs = zeit + ms });
        zeit += 20_020;
        h.Takt();
    }

    /// <summary>
    /// Sichtproben für Sarevoc (mit FUNK_BILDER, etwa C:\Claude\output\test\20261003_klang-c1-sichtproben): Startseite mit Korridor am Balken
    /// und Knopf "Mindestlautstärke einmessen" (Anthrazit) und nicht eingemessen, Audio mit Korridor, Einmessen und Funk-Klick über
    /// Lautsprecher, die Seite Sprachaktivierung in Mindestgröße und groß (feste Grafik), der Balken und die Grafik vergrößert.
    /// </summary>
    [Test]
    public static void C1_Sichtproben()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Raidabend());
            a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
            a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
            a.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
            a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Lautsprecher;
            var h = a.Haupt(out var funkfeld, out var schwelle, out var audio, out _);
            _ = a.Maschine.StartenAsync(null, null, CancellationToken.None);
            var n = 0;
            var zeit = 0L;
            void Bild(Window w, string name, Size groesse, double skala = 1, bool schattenPruefen = true)
            {
                var (bild, _) = Bildlauf.Zeichnen(w, skala, groesse);
                if (schattenPruefen) OberflaecheStartseiteTests.SchattenFrei(Bildlauf.Wurzel(w), name);
                Bildablage.Ablegen(bild, T($"{++n:00}-{name}"));
            }

            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);
                var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
                fenster.Add(haupt);
                // Seit 0.10.0 (Gegenlesung Punkt 2): Der Balken mit Griffen und "Mindestlautstärke einmessen" stehen nicht mehr auf Start, sondern
                // unter Einstellungen > Senden > Feinheiten (bis AP2 im Bereich Audio); Start zeigt Pegel und SENDET nur unten in der Statuszeile.
                Seiten.Feinheiten(h, Abschnittskennung.Senden);
                StimmeGelernt(a);
                Verlauf(a, h, ref zeit);
                a.Maschine.Melden(new PegelMeldung(zeit, -27, -62, -40, 0.95f, true));
                h.Takt();
                Bild(haupt, "senden-korridor-" + kurz, new Size(960, 1900));
                var griffe = Bildlauf.Nachfahren<Pegelbalken>(Bildlauf.Wurzel(haupt)).Single(b => b.Name == "AudioPegel");
                Pruefe.Wahr(Ansichtspruefung.ZuSehen(griffe) && audio.Korridor.Sichtbar, kurz + ": der Balken mit Griffen steht unter Senden > Feinheiten");
                if (stellung != Farbstellung.Anthrazit) continue;
                a.Maschine.Korridor = KorridorStand.Leer;
                h.Takt();
                Bild(haupt, "senden-nicht-eingemessen-" + kurz, new Size(960, 1900));
                StimmeGelernt(a);
                h.Takt();
                Bild(haupt, "senden-korridor-lautsprecher-" + kurz, new Size(960, 1900));
                audio.Korridor.EinmessenCommand.Execute(null);
                a.Maschine.MindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Leise, 3000, 0f, 0f, -30f, float.NaN);
                h.Takt();
                Bild(haupt, "senden-einmessen-satz2-" + kurz, new Size(960, 700));
                audio.Korridor.EinmessenCommand.Execute(null);
                h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
                Verlauf(a, h, ref zeit);
                // In der Mindestgröße rollt die Seite (Q12, auf Seiten erlaubt); der Schatten unten am Rand des Bildlaufs ist dort kein Befund.
                Bild(haupt, "sprachaktivierung-seite-mindestgroesse-" + kurz, Fenstergroessen.SeiteMindest, schattenPruefen: false);
                Bild(haupt, "sprachaktivierung-seite-gross-" + kurz, new Size(1500, 950));
                schwelle.Modus = GateModus.Lernend;
                Verlauf(a, h, ref zeit);
                Bild(haupt, "sprachaktivierung-seite-lernend-" + kurz, Fenstergroessen.SeiteVoreinstellung);
                schwelle.Modus = GateModus.Automatisch;
            }

            // Von 0.10.0 bis 0.11.1 (Q87) zeigte Start weder Pegelbalken noch Einmessen. Seit 0.11.2 stehen beide unten unter "Senden" (Sarevoc 13.10.2026);
            // das Einmessen selbst läuft weiter im Popup über dem Fenster.
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            {
                var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
                fenster.Add(haupt);
                h.AktiverBereich = h.Bereiche[0];
                h.Takt();
                _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.KleinMindest);
                var seite = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is StartViewModel);
                Pruefe.Gleich("StartPegel", string.Join("|", Bildlauf.Nachfahren<Pegelbalken>(seite).Select(p => p.Name)), "Start: der Pegelbalken unter Senden");
                Pruefe.Gleich(1, Bildlauf.Nachfahren<Button>(seite).Count(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Mindestlautstärke einmessen"),
                    "Start: einmal Einmessen, unter Senden");
            }
            foreach (var (stellung, kurz) in new[] { (Farbstellung.Anthrazit, "anthrazit") })
            {
                Bildlauf.RessourcenSetzen(stellung);
                StimmeGelernt(a);
                h.Takt();
                var flaeche = (System.Windows.Media.Brush)Application.Current!.Resources["Pinsel.Flaeche"];
                var balken = new Pegelbalken { Width = 360, Height = 36, PegelDb = -27, SchwelleDb = -40, Sendet = true, Korridor = audio.Korridor };
                Bildablage.Ablegen(OberflaecheHilfeTests.Zeichnen(new Border { Background = flaeche, Padding = new Thickness(10), Child = balken }, 3),
                    T($"{++n:00}-balken-nah-300-{kurz}"));
                var grafik = new SchwellenGrafik { Modell = schwelle, Width = 640, Height = 360 };
                Bildablage.Ablegen(OberflaecheHilfeTests.Zeichnen(new Border { Background = flaeche, Padding = new Thickness(8), Child = grafik }, 2),
                    T($"{++n:00}-grafik-nah-200-{kurz}"));
            }
            funkfeld.Dispose();
        });
    }
}
