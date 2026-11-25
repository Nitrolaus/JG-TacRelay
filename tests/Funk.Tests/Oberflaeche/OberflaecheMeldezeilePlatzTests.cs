// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Meldezeile an einem festen Platz, ohne Dauerfeuer (0.11.1, Sarevoc 12.10.2026: "Das Overlay ... muss an einem festen Platz am Bildschirm
/// aufpoppen, nicht da, wo das Funkfeld ist, und auch nicht immerzu spammen"). Geprüft: die Rechnung der neun Stellen je Monitor und DPI und
/// die Wahl des Monitors (Automatisch mit dem Spiel, Hauptmonitor, Monitor n, abgesteckt), das Modell unter Einstellungen (Raster setzt die
/// Stelle, Probe 3 s, Monitorliste, Ablage nachsichtig), die Entprellung (dieselbe Meldung höchstens einmal je 5 min, Tasten immer), keine
/// Meldung bei eigenen Aktionen und beim Zeigen des Funkfelds, und das Raster im Bildlauf (Bild 0111-5-meldezeile-raster mit FUNK_BILDER).
/// Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheMeldezeilePlatzTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Ein dritter Monitor links neben dem Hauptmonitor, 4K bei 175 %.</summary>
    private static readonly Bildschirm Links4k = new(new Pixelrechteck(-3840, 0, 3840, 2160), new Pixelrechteck(-3840, 0, 3840, 2100), 1.75, false);

    private static readonly IReadOnlyList<Bildschirm> Drei = [FullHd, Wqhd, Links4k];

    /// <summary>Die zwei Breiten des Bildlaufs: breit und die kleinste Karte.</summary>
    private static readonly double[] Breiten = [1100, 640];

    private static ContentPresenter Seite(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);

    private static T Teil<T>(FrameworkElement wurzel, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(wurzel).FirstOrDefault(e => e.Name == name) ?? throw new PruefungException(name + " fehlt");

    // ------------------------------------------------------------------------------------------------------------------------------
    // Rechnung
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Die neun Stellen auf dem Hauptmonitor (Full HD, 100 %), auf dem WQHD bei 150 % rechts daneben und auf einem 4K bei 175 % links davon:
    /// 8 Einheiten vom Rand mit der Skala des Monitors, in der Mitte zentriert, im ganzen Monitorrechteck (die Taskleiste zählt nicht); eine zu
    /// breite Meldezeile bleibt am linken und oberen Rand. Die Wahl des Monitors: Automatisch nimmt das Spiel, sonst den Hauptmonitor; 0 den
    /// Hauptmonitor, n den n-ten, ein fehlender den Hauptmonitor. Das größte sichtbare Fenster des Spiels aus einer Liste.
    /// </summary>
    [Test]
    public static void Platz_Rechnung_NeunStellenJeMonitorUndDpi_MonitorwahlUndSpielfenster()
    {
        (int, int) F(Meldezeilenstelle s, Bildschirm b, int breite = 500, int hoehe = 40) => Meldezeilenlage.Fest(s, b, breite, hoehe);
        Pruefe.Gleich(9, MeldezeilePlatzViewModel.Stellen.Count, "neun Stellen");
        var erwartet = new Dictionary<Meldezeilenstelle, (int, int)>
        {
            [Meldezeilenstelle.ObenLinks] = (8, 8), [Meldezeilenstelle.ObenMitte] = (710, 8), [Meldezeilenstelle.ObenRechts] = (1412, 8),
            [Meldezeilenstelle.MitteLinks] = (8, 520), [Meldezeilenstelle.Mitte] = (710, 520), [Meldezeilenstelle.MitteRechts] = (1412, 520),
            [Meldezeilenstelle.UntenLinks] = (8, 1032), [Meldezeilenstelle.UntenMitte] = (710, 1032), [Meldezeilenstelle.UntenRechts] = (1412, 1032),
        };
        foreach (var (stelle, ecke) in erwartet) Pruefe.Gleich(ecke, F(stelle, FullHd), "Full HD, " + MeldezeilePlatzViewModel.Name(stelle));
        Pruefe.Gleich(FunkfeldEinstellungen.MeldezeileStelleAbWerk, Meldezeilenstelle.ObenMitte, "ab Werk oben Mitte (Sarevoc)");
        // WQHD bei 150 % rechts neben Full HD: der Rand ist 12 Bildpunkte, die Mitte liegt bei 1920 + (2560 - 500) / 2.
        Pruefe.Gleich((2950, 12), F(Meldezeilenstelle.ObenMitte, Wqhd), "WQHD oben Mitte");
        Pruefe.Gleich((1920 + 12, 1440 - 12 - 40), F(Meldezeilenstelle.UntenLinks, Wqhd), "WQHD unten links, bis an den Rand des Monitors");
        // 4K bei 175 % links: der Rand ist 14 Bildpunkte, Koordinaten negativ.
        Pruefe.Gleich((-3840 + 14, 14), F(Meldezeilenstelle.ObenLinks, Links4k), "4K links oben links");
        Pruefe.Gleich((-14 - 500, 2160 - 14 - 40), F(Meldezeilenstelle.UntenRechts, Links4k), "4K links unten rechts");
        Pruefe.Gleich((-3840 + ((3840 - 500) / 2), (2160 - 40) / 2), F(Meldezeilenstelle.Mitte, Links4k), "4K links Mitte");
        // Zu breit: links und oben im Monitor, nie darüber hinaus.
        Pruefe.Gleich((0, 8), F(Meldezeilenstelle.ObenMitte, FullHd, 3000), "zu breit: links im Monitor");
        Pruefe.Gleich((0, 0), F(Meldezeilenstelle.UntenRechts, FullHd, 3000, 2000), "zu groß: oben links im Monitor");
        // Skala Unsinn: Rand wie bei 100 %.
        var kaputt = new Bildschirm(new Pixelrechteck(0, 0, 1000, 500), new Pixelrechteck(0, 0, 1000, 500), double.NaN, true);
        Pruefe.Gleich((8, 8), F(Meldezeilenstelle.ObenLinks, kaputt), "Skala NaN zählt als 1");

        // Die Wahl des Monitors.
        Pruefe.Gleich(Wqhd, Meldezeilenlage.Monitor(Drei, null, Wqhd), "Automatisch mit Spiel: der Monitor des Spiels");
        Pruefe.Gleich(FullHd, Meldezeilenlage.Monitor(Drei, null, null), "Automatisch ohne Spiel: der Hauptmonitor");
        Pruefe.Gleich(FullHd, Meldezeilenlage.Monitor(Drei, 0, Wqhd), "0: der Hauptmonitor, auch mit Spiel");
        Pruefe.Gleich(FullHd, Meldezeilenlage.Monitor(Drei, 1, Wqhd), "1: der erste");
        Pruefe.Gleich(Wqhd, Meldezeilenlage.Monitor(Drei, 2, null), "2: der zweite");
        Pruefe.Gleich(Links4k, Meldezeilenlage.Monitor(Drei, 3, null), "3: der dritte");
        Pruefe.Gleich(FullHd, Meldezeilenlage.Monitor(Drei, 4, Wqhd), "4 gibt es nicht (abgesteckt): der Hauptmonitor");
        Pruefe.Gleich(FullHd, Meldezeilenlage.Monitor(Drei, -1, Wqhd), "Unsinn: der Hauptmonitor");
        Pruefe.Gleich(Wqhd, Meldezeilenlage.Monitor([Wqhd, Links4k], 0, null), "ohne Hauptmonitor in der Liste: der erste");
        _ = Pruefe.Wirft<ArgumentException>(() => Meldezeilenlage.Monitor([], null, null), "ohne Monitor");

        // Das Fenster des Spiels: das größte sichtbare Fenster eines Prozesses mit dem Namen StarCitizen (nicht der Launcher, nicht fremde).
        Pruefe.Gleich("StarCitizen", Spielfenster.Prozess, "der Prozessname genau");
        (nint, uint, Pixelrechteck)[] fensterliste =
        [
            (11, 7, new Pixelrechteck(0, 0, 100, 100)), (12, 7, new Pixelrechteck(1920, 0, 2560, 1440)), (13, 9, new Pixelrechteck(0, 0, 4000, 4000)),
            (14, 7, new Pixelrechteck(0, 0, 0, 0)),
        ];
        Pruefe.Gleich((nint)12, Spielfenster.Groesstes(fensterliste, new HashSet<uint> { 7 }), "das größte Fenster des Spiels");
        Pruefe.Gleich((nint)0, Spielfenster.Groesstes(fensterliste, new HashSet<uint> { 8 }), "kein Fenster dieses Prozesses");
        Pruefe.Gleich((nint)0, Spielfenster.Groesstes(fensterliste, new HashSet<uint>()), "kein Spiel");
        Pruefe.Gleich((nint)0, Spielfenster.Groesstes([(14, 7, new Pixelrechteck(0, 0, 0, 0))], new HashSet<uint> { 7 }), "ohne Fläche zählt nicht");
        // Der echte Weg, nur lesend (Prozessliste und EnumWindows, kein Handle, nichts aktiviert): wirft nie bis in die Meldezeile (Befund der Prüfung:
        // der Fang saß um Id statt um den Aufruf). Ohne Spiel null; läuft neben dem Testlauf eines, sein Monitor.
        _ = Spielfenster.Monitor();
        Pruefe.Gleich(640.0, Meldezeilenlage.Hoechstbreite(1), "die Breite hängt nicht mehr am Funkfeld: 640 bei 0 %");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Modell unter Einstellungen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Das Raster: neun Felder, ab Werk oben Mitte; ein Feld wählen setzt die Stelle, merkt sie vor und zeigt die Probe 3 s (nicht bei 2999 ms
    /// fort, bei 3000 fort); abwählen tut nichts; der Klick (Command) auf das schon gewählte Feld zeigt die Probe noch einmal (Befund der
    /// Prüfung). Die Monitorliste: Automatisch, Hauptmonitor, Monitor 1 und 2 mit Maßen; die Wahl über die Liste
    /// setzt den Monitor und zeigt die Probe; ein gewählter Monitor, der fehlt, steht als "nicht angeschlossen" da; Unsinn wird Automatisch.
    /// Gespeichert und nachsichtig gelesen (unbekannte Stelle: oben Mitte; Text statt Zahl: Automatisch; Kommazahl gerundet; außerhalb: Automatisch).
    /// Erweitert zählt beides als selbst gestellt und stellt beides zurück.
    /// </summary>
    [Test]
    public static void Platz_Modell_RasterSetztStelleZeigtProbe3s_MonitorwahlGemerktUndNachsichtig()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Bildschirme = [FullHd, Wqhd] };
        long ms = 10_000;
        var zeile = new Meldezeile(() => ms);
        var platz = new MeldezeilePlatzViewModel(a.Ablage, () => a.Bildschirme);
        var proben = 0;
        platz.ProbeVerlangt += () =>
        {
            proben++;
            zeile.Zeigen(Meldezeile.ProbeSchluessel, Meldezeile.ProbeText, Meldeart.Hinweis, Meldezeiten.ProbeMs, immer: true);
        };
        Pruefe.Gleich("Meldezeile oben links|Meldezeile oben Mitte|Meldezeile oben rechts|Meldezeile Mitte links|Meldezeile Mitte|Meldezeile Mitte rechts|Meldezeile unten links|Meldezeile unten Mitte|Meldezeile unten rechts",
            string.Join("|", platz.Felder.Select(f => f.Name)), "neun Felder in der Reihenfolge des Rasters");
        Pruefe.Gleich(Meldezeilenstelle.ObenMitte, platz.Stelle, "ab Werk oben Mitte");
        Pruefe.Gleich(1, platz.Felder.Count(f => f.Gewaehlt), "genau ein Feld gewählt");
        Pruefe.Wahr(platz.Felder[1].Gewaehlt && platz.Felder[1].Hilfe.StartsWith("Meldezeile oben Mitte: ", StringComparison.Ordinal), "oben Mitte gewählt, mit Hovertext");
        Pruefe.Falsch(zeile.Sichtbar, "ohne Klick keine Probe");

        // Klick auf unten rechts.
        var vorher = a.Ablage.Schreibvorgaenge;
        platz.Felder[8].Gewaehlt = true;
        Pruefe.Gleich(Meldezeilenstelle.UntenRechts, platz.Stelle, "die Stelle");
        Pruefe.Gleich(Meldezeilenstelle.UntenRechts, a.Ablage.Aktuell.Funkfeld.MeldezeileStelle, "gemerkt");
        Pruefe.Wahr(platz.Felder[8].Gewaehlt && platz.Felder.Count(f => f.Gewaehlt) == 1, "nur unten rechts gewählt");
        Pruefe.Gleich(1, proben, "eine Probe");
        Pruefe.Gleich(Meldezeile.ProbeText, zeile.Text, "die Probe steht");
        ms += Meldezeiten.ProbeMs - 1;
        zeile.Takt();
        Pruefe.Wahr(zeile.Sichtbar, "nach 2999 ms noch da");
        ms += 1;
        zeile.Takt();
        Pruefe.Falsch(zeile.Sichtbar, "nach 3 s fort");
        a.Ablage.Sichern();
        Pruefe.Gleich(vorher + 1, a.Ablage.Schreibvorgaenge, "vorgemerkt und geschrieben");
        platz.Felder[8].Gewaehlt = false;
        platz.Felder[8].Gewaehlt = true;
        Pruefe.Gleich(Meldezeilenstelle.UntenRechts, platz.Stelle, "abwählen tut nichts, dasselbe Feld noch einmal ändert nichts");
        Pruefe.Gleich(1, proben, "keine zweite Probe ohne Änderung über die Eigenschaft");
        // Der Klick (Command des RadioButtons): auf das gewählte Feld die Probe noch einmal, auf ein anderes die Wahl mit einer Probe.
        platz.Felder[8].WaehlenCommand.Execute(null);
        Pruefe.Wahr(platz.Stelle == Meldezeilenstelle.UntenRechts && proben == 2, "der Klick auf das gewählte Feld zeigt die Probe noch einmal (Befund der Prüfung)");
        platz.Felder[7].WaehlenCommand.Execute(null);
        Pruefe.Wahr(platz.Stelle == Meldezeilenstelle.UntenMitte && proben == 3, "der Klick auf ein anderes Feld wählt es, genau eine Probe");
        platz.Felder[8].WaehlenCommand.Execute(null);
        Pruefe.Wahr(platz.Stelle == Meldezeilenstelle.UntenRechts && proben == 4, "und zurück auf unten rechts");

        // Die Monitorliste und die Wahl darüber.
        Pruefe.Gleich(MeldezeilePlatzViewModel.AutomatischText + "|" + MeldezeilePlatzViewModel.HauptmonitorText + "|Monitor 1: 1920 x 1080 (Hauptmonitor)|Monitor 2: 2560 x 1440",
            string.Join("|", platz.Monitore.Select(m => m.Name)), "die Liste");
        Pruefe.Wahr(platz.GewaehlterMonitor is { Wert: null } && platz.Monitor is null, "ab Werk Automatisch");
        platz.GewaehlterMonitor = platz.Monitore.Single(m => m.Wert == 2);
        Pruefe.Gleich(2, platz.Monitor, "Monitor 2 gewählt");
        Pruefe.Gleich(new Meldezeilenwahl(Meldezeilenstelle.UntenRechts, 2), platz.Wahl, "die Wahl für das Fenster");
        Pruefe.Gleich(5, proben, "die Wahl des Monitors zeigt die Probe");
        Pruefe.Wahr(zeile.Sichtbar, "die Probe steht wieder");
        platz.GewaehlterMonitor = null;
        Pruefe.Gleich(2, platz.Monitor, "null aus der Liste (WPF beim Neufüllen) ändert nichts");
        platz.Monitor = 5;
        Pruefe.Gleich(5, platz.Monitor, "ein fehlender Monitor bleibt gemerkt");
        Pruefe.Wahr(platz.Monitore.Last().Name == "Monitor 5 (nicht angeschlossen)" && platz.GewaehlterMonitor?.Wert == 5, "steht als nicht angeschlossen in der Liste");
        platz.Monitor = 99;
        Pruefe.Wahr(platz.Monitor is null && platz.Monitore.Count == 4, "Unsinn wird Automatisch, die Liste ohne den fehlenden");
        a.Bildschirme = [FullHd, Wqhd, Links4k];
        platz.MonitoreLesen();
        Pruefe.Gleich("Monitor 3: 3840 x 2160", platz.Monitore.Last().Name, "ein dritter Monitor kommt beim Lesen dazu");
        platz.Monitor = 0;
        Pruefe.Gleich(MeldezeilePlatzViewModel.HauptmonitorText, platz.GewaehlterMonitor?.Name, "Hauptmonitor gewählt");
        platz.ProbeCommand.Execute(null);
        Pruefe.Gleich(9, proben, "Monitor 5, Unsinn (wird Automatisch), Monitor 0 und der Knopf: je eine Probe");

        // Gespeichert und wieder gelesen; null wird nicht geschrieben.
        platz.Monitor = null;
        a.Ablage.Sichern();
        var text = File.ReadAllText(a.Ablage.Pfad);
        Pruefe.Wahr(System.Text.RegularExpressions.Regex.IsMatch(text, "\"meldezeileStelle\":\\s*\"UntenRechts\"") && !text.Contains("meldezeileMonitor", StringComparison.Ordinal),
            "die Stelle als Name, Automatisch ohne Feld");
        var gelesen = new EinstellungsSpeicher(a.Ablage.Pfad).Laden();
        Pruefe.Wahr(gelesen.Funkfeld.MeldezeileStelle == Meldezeilenstelle.UntenRechts && gelesen.Funkfeld.MeldezeileMonitor is null, "wieder gelesen");
        foreach (var (json, stelle, monitor, was) in new (string, Meldezeilenstelle, int?, string)[]
                 {
                     ("{\"funkfeld\":{\"meldezeileStelle\":\"quatsch\",\"meldezeileMonitor\":\"x\"}}", Meldezeilenstelle.ObenMitte, null, "Unsinn: ab Werk"),
                     ("{\"funkfeld\":{\"meldezeileStelle\":\"untenLinks\",\"meldezeileMonitor\":3.7}}", Meldezeilenstelle.UntenLinks, 4, "Name ohne Großschreibung, Kommazahl gerundet"),
                     ("{\"funkfeld\":{\"meldezeileStelle\":7,\"meldezeileMonitor\":-5}}", Meldezeilenstelle.UntenMitte, null, "Zahl der Stelle, Monitor unter 0"),
                     ("{\"funkfeld\":{\"meldezeileMonitor\":99}}", Meldezeilenstelle.ObenMitte, null, "ohne Stelle, Monitor über der Grenze"),
                     ("{\"funkfeld\":{\"meldezeileMonitor\":0}}", Meldezeilenstelle.ObenMitte, 0, "Hauptmonitor"),
                 })
        {
            var datei = a.Ordner.Datei("probe-" + Guid.NewGuid().ToString("N")[..6] + ".json");
            File.WriteAllText(datei, json);
            var e = new EinstellungsSpeicher(datei).Laden();
            Pruefe.Gleich((stelle, monitor), (e.Funkfeld.MeldezeileStelle, e.Funkfeld.MeldezeileMonitor), was);
        }

        // Erweitert unter Funkfeld und Darstellung zählt beides und stellt beides zurück.
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var teil = h.Einstellungen.AbschnittFunkfeld!.Feinheiten;
        Pruefe.Wahr(teil.Inhalt.Contains("Meldezeile", StringComparison.Ordinal), "der Inhalt nennt die Meldezeile: " + teil.Inhalt);
        Pruefe.Gleich("Meldezeile unten rechts", string.Join("|", teil.SelbstGestellt), "die Stelle zählt");
        h.Einstellungen.Meldezeile.Monitor = 2;
        Pruefe.Gleich("Meldezeile unten rechts|Meldezeile auf Monitor 2", string.Join("|", teil.SelbstGestellt), "der Monitor zählt");
        Pruefe.Wahr(teil.Kopfzeile.StartsWith("Erweitert: (2 selbst gestellt) ", StringComparison.Ordinal), "Kopfzeile: " + teil.Kopfzeile);
        teil.ZurueckCommand.Execute(null);
        Pruefe.Gleich("", string.Join("|", teil.SelbstGestellt), "zurück auf Werkseinstellung");
        Pruefe.Wahr(h.Einstellungen.Meldezeile.Stelle == Meldezeilenstelle.ObenMitte && h.Einstellungen.Meldezeile.Monitor is null, "oben Mitte, Automatisch");
        funkfeld.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Kein Dauerfeuer
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Derselbe Hinweis der Automatik (Schlüssel und Text) erscheint höchstens einmal je 5 Minuten, gezählt ab dem Erscheinen (nicht ab dem Versuch:
    /// Was wartend verfällt, sperrt nichts); ein unterdrückter Versuch verlängert nichts; ein anderer Text oder ein anderer Schlüssel erscheint; mit
    /// <c>immer</c> jedes Mal, ein Fehler ebenso; eine gehaltene zählt nicht. Was eine Taste beantwortet (Melden geht nicht, Phasentasten), kommt bei
    /// jedem Druck; Sendekanal, Mikro stumm und Dauersender stumm erscheinen seit 0.11.2 gar nicht mehr (<see cref="Meldezeile.Erlaubt"/>). Was der
    /// Server meldet (Phase, Einteilung, Kanäle, Entzug), erscheint auch, wenn derselbe Stand binnen 5 min zurückkehrt (Befund der Prüfung).
    /// </summary>
    [Test]
    public static async Task Platz_Entprellung_GleicheMeldungHoechstensEinmalJe5Min_TastenImmer()
    {
        // Seit 0.11.2 nimmt die Meldezeile nur Schlüssel aus Meldezeile.Erlaubt an; die Entprellung gilt für jeden davon, darum laufen die Schritte mit
        // ihnen (bis 0.11.1 mit frei gewählten Schlüsseln und dem Satz zu Ruhe, den es nicht mehr gibt).
        const string umstellung = Teilnehmermeldungen.UmstellungSchluessel;
        const string ansage = Teilnehmermeldungen.AnsageSchluessel;
        const string entzug = Teilnehmermeldungen.EntzugSchluessel;
        const string melden = Teilnehmermeldungen.MeldenSchluessel;
        const string phase = Phasensteuerung.Schluessel;
        long ms = 0;
        var z = new Meldezeile(() => ms);
        z.Zeigen(umstellung, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 2000);
        Pruefe.Wahr(z.Sichtbar, "das erste Mal");
        ms = 2500;
        z.Takt();
        Pruefe.Falsch(z.Sichtbar, "abgelaufen");
        z.Zeigen(umstellung, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 2000);
        Pruefe.Falsch(z.Sichtbar, "dieselbe Meldung binnen 5 min: nicht noch einmal");
        ms = 299_999;
        z.Zeigen(umstellung, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 2000);
        Pruefe.Falsch(z.Sichtbar, "kurz vor 5 min: nicht");
        ms = 300_000;
        z.Zeigen(umstellung, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 2000);
        Pruefe.Wahr(z.Sichtbar, "nach 5 min ab dem Erscheinen wieder (die unterdrückten Versuche zählen nicht)");
        ms = 300_100;
        z.Zeigen(umstellung, "Eingeteilt von Ferox: Schiff 2, Pilot", Meldeart.Hinweis, 2000);
        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 2, Pilot", z.Text, "ein anderer Text erscheint");
        z.Zeigen(ansage, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 2000);
        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Gunner", z.Text, "ein anderer Schlüssel erscheint");
        ms = 303_000;
        z.Takt();
        z.Zeigen(entzug, "Nicht gesendet: kein Senderecht mehr in Orga", Meldeart.Fehler, 1000);
        ms = 305_000;
        z.Takt();
        z.Zeigen(entzug, "Nicht gesendet: kein Senderecht mehr in Orga", Meldeart.Fehler, 1000);
        Pruefe.Wahr(z.Sichtbar, "ein Fehler erscheint jedes Mal, auch ohne immer (Befund der Prüfung: ein zweiter Entzug binnen 5 min fiel still weg)");
        ms = 306_500;
        z.Takt();
        Pruefe.Falsch(z.Sichtbar, "abgelaufen");
        z.Zeigen(entzug, "Nicht gesendet: kein Senderecht mehr in Orga", Meldeart.Fehler, 1000, immer: true);
        Pruefe.Wahr(z.Sichtbar, "mit immer jedes Mal");
        ms = 307_600;
        z.Takt();
        z.Halten(melden, "Melden: an Kommando", Meldeart.Hinweis, senden: true);
        z.Loesen(melden);
        z.Halten(melden, "Melden: an Kommando", Meldeart.Hinweis, senden: true);
        Pruefe.Gleich("Melden: an Kommando", z.Text, "eine gehaltene Meldung ist ein Zustand und zählt nicht");
        z.Loesen(melden);
        // Ein Umbruch im Text zählt wie das Leerzeichen, das daraus wird.
        z.Zeigen(umstellung, "Eingeteilt von Ferox:\nSchiff 3, Marine", Meldeart.Hinweis, 1000);
        ms = 309_000;
        z.Takt();
        z.Zeigen(umstellung, "Eingeteilt von Ferox: Schiff 3, Marine", Meldeart.Hinweis, 1000);
        Pruefe.Falsch(z.Sichtbar, "derselbe Satz, einmal mit Umbruch");

        // Gemerkt wird das Erscheinen, nicht der Versuch (Befund der Prüfung): Ein Hinweis, der hinter einem Fehler wartet und ungesehen
        // verfällt, sperrt nichts; einer, der noch nachrückt, zählt ab dem Nachrücken.
        ms = 310_000;
        z.Takt();
        z.Zeigen(entzug, "Nicht gesendet: kein Senderecht mehr in Crew 1", Meldeart.Fehler, 5000);
        z.Zeigen(ansage, "Kanäle eingeteilt von Kobold", Meldeart.Hinweis, 1000);
        Pruefe.Gleich("Nicht gesendet: kein Senderecht mehr in Crew 1", z.Text, "der Hinweis wartet hinter dem Fehler");
        ms = 316_000;
        z.Takt();
        Pruefe.Falsch(z.Sichtbar, "beides fort, der Hinweis ungesehen verfallen");
        z.Zeigen(ansage, "Kanäle eingeteilt von Kobold", Meldeart.Hinweis, 1000);
        Pruefe.Gleich("Kanäle eingeteilt von Kobold", z.Text, "ungesehen verfallen sperrt nicht: der Hinweis erscheint");
        ms = 318_000;
        z.Takt();
        z.Zeigen(entzug, "Nicht gesendet: kein Senderecht mehr in Crew 1", Meldeart.Fehler, 2000);
        z.Zeigen(phase, "Phasen schaltet die Einsatzleitung", Meldeart.Hinweis, 10_000);
        ms = 320_000;
        z.Takt();
        Pruefe.Gleich("Phasen schaltet die Einsatzleitung", z.Text, "nach dem Fehler rückt der Hinweis nach");
        ms = 619_000;
        z.Takt();
        z.Zeigen(phase, "Phasen schaltet die Einsatzleitung", Meldeart.Hinweis, 1000);
        Pruefe.Falsch(z.Sichtbar, "5 min zählen ab dem Nachrücken (320 s), nicht ab dem Versuch (318 s)");
        ms = 620_000;
        z.Zeigen(phase, "Phasen schaltet die Einsatzleitung", Meldeart.Hinweis, 1000);
        Pruefe.Wahr(z.Sichtbar, "5 min nach dem Nachrücken wieder");

        // Die Tasten: bei jedem Druck. Seit 0.11.2 nur noch die Tasten, deren Antwort über dem Spiel stehen darf (Melden geht nicht, Phasentasten);
        // Sendekanal, Mikro stumm und Dauersender stumm fallen still weg (Meldezeile.Erlaubt).
        using var a = new Aufbau(dienst: MitRaidabend());
        var zeile = new Meldezeile(() => a.Uhr);
        using var t = new Teilnehmermeldungen(a.Dienst, zeile, SofortTakt.Instanz) { Abgelegt = true };
        void Weg()
        {
            a.Uhr += 10_000;
            zeile.Takt();
            Pruefe.Falsch(zeile.Sichtbar, "zwischendurch fort");
        }
        t.MeldenGehtNicht("Melden geht nicht: kein Senderecht in Kommando");
        Weg();
        t.MeldenGehtNicht("Melden geht nicht: kein Senderecht in Kommando");
        Pruefe.Gleich("Melden geht nicht: kein Senderecht in Kommando", zeile.Text, "Melden geht nicht: bei jedem Druck");
        Weg();
        t.SendenetzGewechselt();
        Pruefe.Falsch(zeile.Sichtbar, "Sendekanal nach der Taste in der Ablage: seit 0.11.2 nicht mehr über dem Spiel");
        var f = a.Funkfeld();
        f.Meldezeile = zeile;
        f.Mikro.TasteGedrueckt();
        Pruefe.Falsch(zeile.Sichtbar, "Mikro stumm: seit 0.11.2 nicht mehr über dem Spiel");
        f.Mikro.TasteGedrueckt();
        Pruefe.Falsch(zeile.Sichtbar, "Mikro wieder an: ebenso");
        f.Dauersend.StummTaste();
        Pruefe.Falsch(zeile.Sichtbar, "Dauersender stumm: seit 0.11.2 nicht mehr über dem Spiel");
        f.Dispose();
        var phasen = new Phasensteuerung(a.Dienst, zeile, () => a.Uhr, () => a.Jetzt);
        await phasen.TasteAsync(+1);
        var scharf = zeile.Text;
        Pruefe.Wahr(scharf.StartsWith("Phase 2/3 ENTERN?", StringComparison.Ordinal), "scharf: " + scharf);
        a.Uhr += Meldezeiten.ScharfMs + 1;
        phasen.Takt();
        zeile.Takt();
        Pruefe.Falsch(zeile.Sichtbar, "verfallen");
        await phasen.TasteAsync(+1);
        Pruefe.Gleich(scharf, zeile.Text, "die Phasentaste sagt es bei jedem Druck wieder");

        // Was der Server meldet, erscheint jedes Mal (Befund der Prüfung: entprellt fiel derselbe Stand, der binnen 5 min zurückkehrt, still weg):
        // Phase vor, zurück, wieder vor; Gunner, Pilot, wieder Gunner; ein zweiter Entzug im selben Kanal.
        var d = MitRaidabend();
        long uhr = 0;
        var server = new Meldezeile(() => uhr);
        using var tm = new Teilnehmermeldungen(d, server, SofortTakt.Instanz);
        void Weiter(int sekunden)
        {
            uhr += sekunden * 1000L;
            server.Takt();
            Pruefe.Falsch(server.Sichtbar, "zwischendurch fort");
        }
        FeldInfo[] trupp = [new FeldInfo(0, 12, true, false, false), new FeldInfo(2, 41, true, true, false)];
        FeldInfo[] crew = [new FeldInfo(0, 12, true, true, false)];
        void Phase(byte nr, string name, FeldInfo[] felder, byte[] sendeFelder) => d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1, UmstellungPhase = nr, LaufendePhase = new LaufendePhaseInfo(nr, name, name.ToUpperInvariant(), Ferox, Abend),
            Felder = felder, SendeFelder = sendeFelder,
        });
        Phase(2, "Entern", trupp, [2]);
        Pruefe.Gleich("Phase Entern (Ferox): sendest in Trupp Alpha", server.Text, "Phase vor");
        Weiter(60);
        Phase(1, "Anflug", crew, [0]);
        Pruefe.Gleich("Phase Anflug (Ferox): sendest in Crew 1", server.Text, "Phase zurück");
        Weiter(60);
        Phase(2, "Entern", trupp, [2]);
        Pruefe.Gleich("Phase Entern (Ferox): sendest in Trupp Alpha", server.Text, "Phase wieder vor binnen 5 min: erscheint wieder");
        Weiter(30);
        void Platz(ushort rolle) => d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1, UmstellungPhase = null, SchiffId = Schiff1, RolleId = rolle, Platz = PlatzStatus.Belegt,
            Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = Ferox,
        });
        Platz(Gunner);
        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Gunner", server.Text, "Gunner");
        Weiter(30);
        Platz(Pilot);
        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Pilot", server.Text, "Pilot");
        Weiter(30);
        Platz(Gunner);
        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Gunner", server.Text, "wieder Gunner binnen 5 min: erscheint wieder");
        Weiter(30);
        d.EreignisMelden(new SendenetzEntzogen([NetzOrga], Ferox));
        Pruefe.Gleich("Nicht gesendet: kein Senderecht mehr in Orga (Ferox)", server.Text, "Entzug");
        Weiter(30);
        d.EreignisMelden(new SendenetzEntzogen([NetzOrga], Ferox));
        Pruefe.Gleich("Nicht gesendet: kein Senderecht mehr in Orga (Ferox)", server.Text, "ein zweiter Entzug im selben Kanal binnen 5 min: erscheint wieder");
    }

    /// <summary>
    /// Eigene Aktionen erzeugen keine Meldung (Sarevoc): die eigene Rollenwahl und der eigene Ch. (Quelle SELBST, wie seit S2), sich selbst am Brett
    /// einteilen (durch = man selbst), eine selbst geschaltete Phase (neu in 0.11.1; auch, wenn der Name erst mit dem Push kommt). Beim Start und
    /// Zeigen des Funkfelds erscheint nichts (nur der Hinweis beim allerersten Ablegen, einmal); "Ruhe: es wird nichts gesendet" kommt je 5 min
    /// einmal, nicht bei jedem Satz im Raum.
    /// </summary>
    [Test]
    public static void Platz_EigeneAktionen_OhneMeldung_BeimZeigenDesFunkfeldsNichts()
    {
        var d = MitRaidabend();
        long ms = 0;
        var zeile = new Meldezeile(() => ms);
        using var t = new Teilnehmermeldungen(d, zeile, SofortTakt.Instanz);
        var ich = d.Lage.Konto!.Id;
        Pruefe.Gleich(Mustermann, ich, "der Beispielabend: Mustermann");
        FeldInfo[] trupp = [new FeldInfo(0, 12, true, false, false), new FeldInfo(2, 41, true, true, false)];
        // Die eigene Phase, mit Namen im selben Push.
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1, UmstellungPhase = 2, LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", ich, Abend), Felder = trupp, SendeFelder = [2],
        });
        Pruefe.Falsch(zeile.Sichtbar, "selbst geschaltete Phase: keine Meldung");
        // Die eigene Phase, der Name kommt erst mit dem Phasenpush: die Meldung ohne Namen geht dann fort.
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1, UmstellungPhase = 3, LaufendePhase = null,
            Felder = [new FeldInfo(0, 12, true, true, false)], SendeFelder = [0],
        });
        Pruefe.Wahr(zeile.Text.StartsWith("Phase Rückzug: ", StringComparison.Ordinal), "noch ohne Namen: " + zeile.Text);
        d.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(3, "Rückzug", "RÜCKZUG", ich, Abend) });
        Pruefe.Falsch(zeile.Sichtbar, "der Push nennt einen selbst: die Meldung geht fort");
        // Eine Phase durch jemand anders meldet weiter.
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1, UmstellungPhase = 2, LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", Ferox, Abend), Felder = trupp, SendeFelder = [2],
        });
        Pruefe.Gleich("Phase Entern (Ferox): sendest in Trupp Alpha", zeile.Text, "Phase durch Ferox: die Meldung");
        zeile.Loesen(Teilnehmermeldungen.UmstellungSchluessel);
        // Sich selbst am Brett einteilen, die eigene Rolle, der eigene Ch.
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1, UmstellungPhase = null, SchiffId = Schiff1, RolleId = Gunner, Platz = PlatzStatus.Belegt,
            Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = ich,
        });
        Pruefe.Falsch(zeile.Sichtbar, "sich selbst eingeteilt: keine Meldung");
        d.LageSetzen(l => l with { KontoVersion = l.KontoVersion + 1, RolleId = Pilot, Quelle = MitgliedschaftsQuelle.Rolle, QuelleDurch = null });
        Pruefe.Falsch(zeile.Sichtbar, "eigene Rollenwahl: keine Meldung");
        d.LageSetzen(l => l with { KontoVersion = l.KontoVersion + 1, Quelle = MitgliedschaftsQuelle.Selbst, Felder = [new FeldInfo(0, 12, true, true, false), new FeldInfo(1, 13, true, false, false)] });
        Pruefe.Falsch(zeile.Sichtbar, "eigener Ch. oder eigenes Feld: keine Meldung");

        // Das Funkfeld zeigen und ablegen: nichts. Seit 0.11.2 auch beim allerersten Ablegen nicht mehr (der Hinweis zur Ablage fällt unter
        // Meldezeile.Erlaubt still weg; bis 0.11.1 stand er dort einmal).
        using var a = new Aufbau(dienst: d);
        var funker = new FunkermodusViewModel(a.Dienst, a.Ablage, a.Takt);
        var ablage = new FunkfeldAblage(new FunkfeldFensterAttrappe(), new AblagesymbolAttrappe(), a.Ablage, zeile, a.Dienst, () => ms, funker);
        ablage.Starten();
        Pruefe.Falsch(zeile.Sichtbar, "beim Start des Funkfelds nichts");
        ablage.Umschalten();
        Pruefe.Falsch(zeile.Sichtbar, "das allererste Ablegen: seit 0.11.2 kein Hinweis über dem Spiel");
        ablage.Umschalten();
        Pruefe.Falsch(zeile.Sichtbar, "zeigen: nichts");
        ablage.Umschalten();
        ablage.Zeigen(mitFokus: true);
        Pruefe.Falsch(zeile.Sichtbar, "ein zweites Ablegen und Zeigen: nichts");

        // Sprechen in Ruhe: Den Satz "es wird nichts gesendet" gibt es seit 0.11.2 nirgends mehr (Sarevoc 13.10.2026: "raus heißt raus"); die Meldezeile
        // bleibt leer, beim ersten Satz im Raum wie nach 5 min.
        using var b = new Aufbau(FunkDienstAttrappe.KanalBeispiel() with { SendeFelder = [4], GlobalSenden = false });
        OberflaecheStartseiteTests.SprechtasteBelegen(b);
        var h = b.Haupt(out var f, out _, out _, out _);
        var ruhe = new Meldezeile(() => b.Uhr);
        f.Meldezeile = ruhe;
        void Reden()
        {
            for (var i = 0; i < 3; i++)
            {
                b.Uhr += 20;
                b.Maschine.Melden(new PegelMeldung(b.Uhr, -20, -62, -40, 0.95f, false));
            }
            b.Uhr += 100;
            h.Takt();
        }
        void Pause()
        {
            b.Uhr += PegelVerlauf.HinweisHaltMs * 3;
            b.Maschine.Melden(new PegelMeldung(b.Uhr, -70, -62, -40, 0.05f, false));
            h.Takt();
            ruhe.Takt();
        }
        Reden();
        Pruefe.Gleich(Sendeweg.Ruhe, b.Verlauf.Weg, "das Sendeziel steht auf Ruhe");
        Pruefe.Falsch(ruhe.Sichtbar, "der erste Satz im Raum: nichts über dem Spiel");
        Pause();
        Pruefe.Falsch(ruhe.Sichtbar, "nach der Pause nichts");
        b.Uhr += Meldezeiten.WiederholungMs;
        Reden();
        Pruefe.Falsch(ruhe.Sichtbar, "nach 5 min ebenso nichts");
        f.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Bildlauf
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Das Raster unter Einstellungen > Funkfeld und Darstellung > Erweitert im Bildlauf (kein Fenster auf dem Bildschirm): neun Felder mit Namen und
    /// Hovertext in einem kleinen Monitor, eines gewählt; die Monitorliste mit Automatisch, Hauptmonitor, Monitor 1 und 2; der Knopf Probe. Ein Klick
    /// auf ein Feld wählt es und löst die Probe aus, die Liste setzt den Monitor, der Knopf die Probe. Bei 640 und 1100 und bei 175 % nichts
    /// abgeschnitten, Schatten frei, Kontrast, Zugriffstasten eindeutig (O, P). Dazu das Fenster: wohin es käme (ZielFuer), ohne Handle. Mit
    /// FUNK_BILDER das Bild 0111-5-meldezeile-raster.
    /// </summary>
    [Test]
    public static void Platz_Bildlauf_RasterInDenEinstellungen_KlickListeKnopf_Bild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            var satz = Farbvorschlaege.Von(Farbvorschlaege.Voreinstellung);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Bildschirme = [FullHd, Wqhd] };
            using var k = OberflaecheStartseiteTests.Einrichten(a);
            OberflaecheStartseiteTests.SprechtasteBelegen(a);
            a.Tastenquelle = new Maschine.TastenQuelleAttrappe(Funk.Klang.Eingabe.EingabeArt.Tastatur, Funk.Klang.Eingabe.EingabeArt.Maus);
            var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
            audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.MeinProfil.LadenAsync().GetAwaiter().GetResult();
            Seiten.Feinheiten(h, Abschnittskennung.Funkfeld);
            h.Takt();
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            var platz = h.Einstellungen.Meldezeile;
            var proben = 0;
            platz.ProbeVerlangt += () => proben++;

            foreach (var breite in Breiten)
            {
                var was = T($"{breite:0}");
                var bild = Ganz(haupt, breite);
                var seite = Seite(haupt);
                var raster = Teil<Border>(seite, "MeldezeileRaster");
                var felder = Bildlauf.Nachfahren<RadioButton>(raster).ToList();
                Pruefe.Gleich(9, felder.Count, was + ": neun Felder");
                Pruefe.Gleich(string.Join("|", platz.Felder.Select(x => x.Name)), string.Join("|", felder.Select(AutomationProperties.GetName)), was + ": die Namen in der Reihenfolge des Rasters");
                Pruefe.Gleich(1, felder.Count(r => r.IsChecked == true), was + ": genau eines gewählt");
                Pruefe.Wahr(felder[(int)platz.Stelle].IsChecked == true, was + ": das gewählte ist die Stelle");
                foreach (var r in felder)
                {
                    Pruefe.Wahr(ZuSehen(r) && r.ActualWidth > 50 && r.ActualHeight > 30, was + ": Feld zu sehen und groß genug zum Klicken: " + AutomationProperties.GetName(r));
                    Pruefe.Wahr(Glasdesign.Hilfe.GetText(r)?.StartsWith(AutomationProperties.GetName(r) + ": ", StringComparison.Ordinal) == true, was + ": Hovertext nennt die Stelle");
                }
                Pruefe.Wahr(felder.Max(r => r.ActualWidth) - felder.Min(r => r.ActualWidth) < 0.5 && felder.Max(r => r.ActualHeight) - felder.Min(r => r.ActualHeight) < 0.5,
                    was + ": alle Felder gleich groß");
                Pruefe.Wahr(raster.ActualWidth > raster.ActualHeight * 1.5, was + ": der kleine Monitor ist breiter als hoch");
                var liste = Teil<ComboBox>(seite, "MeldezeileMonitorWahl");
                Pruefe.Gleich(4, liste.Items.Count, was + ": Automatisch, Hauptmonitor, Monitor 1, Monitor 2");
                Pruefe.Wahr(liste.SelectedItem is Monitorwahl { Wert: null }, was + ": Automatisch gewählt");
                Pruefe.Wahr(ZuSehen(Teil<Button>(seite, "MeldezeileProbeKnopf")), was + ": der Knopf Probe");
                Pruefe.Wahr(Sichtbar(seite, MeldezeilePlatzViewModel.Erklaerung[..40]), was + ": der Satz");
                Pruefe.Wahr(NichtsAbgeschnitten(seite, was) >= 60, was + ": genug Elemente geprüft");
                _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
                if (breite == 1100)
                {
                    _ = KontrastPruefen(Teil<StackPanel>(seite, "AbschnittFunkfeld"), satz, was + ", Funkfeld und Darstellung", mindestens: 10);
                    Bildablage.Ablegen(Ausschnitt(haupt, bild, "AbschnittFunkfeld"), "0111-5-meldezeile-raster");
                }
                _ = Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, bild.PixelHeight));
                _ = NichtsAbgeschnitten(seite, was + ", 175 %");
                _ = OberflaecheStartseiteTests.SchattenFrei(seite, was + ", 175 %");
            }

            // Klick auf unten links wie ein Mausklick: Der RadioButton setzt seinen Wert selbst (SetCurrentValue, die Bindung bleibt), dann läuft
            // sein Command; dann die Liste, der Knopf.
            _ = Ganz(haupt, 1100);
            var seite2 = Seite(haupt);
            var felder2 = Bildlauf.Nachfahren<RadioButton>(Teil<Border>(seite2, "MeldezeileRaster")).ToList();
            void Klicken(RadioButton feld)
            {
                feld.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
                feld.Command!.Execute(feld.CommandParameter);
            }
            Klicken(felder2[6]);
            Bildlauf.Leeren();
            Pruefe.Gleich(Meldezeilenstelle.UntenLinks, platz.Stelle, "Klick auf unten links: die Stelle");
            Pruefe.Gleich(Meldezeilenstelle.UntenLinks, a.Ablage.Aktuell.Funkfeld.MeldezeileStelle, "gemerkt");
            Pruefe.Gleich(1, proben, "eine Probe");
            _ = Ganz(haupt, 1100);
            felder2 = Bildlauf.Nachfahren<RadioButton>(Teil<Border>(Seite(haupt), "MeldezeileRaster")).ToList();
            Pruefe.Wahr(felder2[6].IsChecked == true && felder2.Count(r => r.IsChecked == true) == 1, "nur unten links gewählt");
            Klicken(felder2[6]);
            Bildlauf.Leeren();
            Pruefe.Wahr(platz.Stelle == Meldezeilenstelle.UntenLinks && proben == 2, "ein zweiter Klick auf das gewählte Feld: die Probe noch einmal (Befund der Prüfung)");
            var liste2 = Teil<ComboBox>(Seite(haupt), "MeldezeileMonitorWahl");
            liste2.SelectedItem = liste2.Items.Cast<Monitorwahl>().Single(m => m.Wert == 2);
            Bildlauf.Leeren();
            Pruefe.Gleich(2, platz.Monitor, "die Liste setzt Monitor 2");
            Pruefe.Gleich(3, proben, "die Wahl zeigt die Probe");
            Teil<Button>(Seite(haupt), "MeldezeileProbeKnopf").Command!.Execute(null);
            Pruefe.Gleich(4, proben, "der Knopf zeigt die Probe");
            var tasten = OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt));
            Pruefe.Gleich("", string.Join(" | ", OberflaecheHauptfensterTests.Doppelte(tasten, "Funkfeld und Darstellung offen")), "Zugriffstasten eindeutig");
            Pruefe.Wahr(tasten.Any(x => x.Taste == 'O') && tasten.Any(x => x.Taste == 'P'), "Alt+O Monitor (M hat die Automatik unter Senden), Alt+P Probe");
            var teil = h.Einstellungen.AbschnittFunkfeld!.Feinheiten;
            Pruefe.Gleich("Meldezeile unten links|Meldezeile auf Monitor 2", string.Join("|", teil.SelbstGestellt), "Erweitert zählt beides");
            Pruefe.Wahr(Sichtbar(Seite(haupt), teil.StandSatz), "der Satz unten nennt beides");
            // Zurück auf Werkseinstellung: Raster und Auswahlfeld ziehen nach (das Auswahlfeld hing sonst auf Monitor 2, Befund beim Bau).
            teil.ZurueckCommand.Execute(null);
            Bildlauf.Leeren();
            _ = Ganz(haupt, 1100);
            felder2 = Bildlauf.Nachfahren<RadioButton>(Teil<Border>(Seite(haupt), "MeldezeileRaster")).ToList();
            Pruefe.Wahr(felder2[(int)Meldezeilenstelle.ObenMitte].IsChecked == true && felder2.Count(r => r.IsChecked == true) == 1,
                "zurück: oben Mitte gewählt, unten links nicht mehr (die Bindung hat den Klick überlebt)");
            Pruefe.Wahr(Teil<ComboBox>(Seite(haupt), "MeldezeileMonitorWahl").SelectedItem is Monitorwahl { Wert: null }, "zurück: das Auswahlfeld zeigt Automatisch");
            Pruefe.Gleich(6, proben, "zurück: je eine Probe für Stelle und Monitor");

            // Das Fenster: wohin es käme, aus Wahl und Spiel, ohne Handle.
            var zeile = new Meldezeile(() => 0);
            var wahl = new Meldezeilenwahl(Meldezeilenstelle.UntenLinks, 2);
            Bildschirm? spiel = null;
            var mf = new MeldezeileFenster(zeile, new FesteBildschirme([FullHd, Wqhd]), () => 1, () => wahl, () => spiel);
            fenster.Add(mf);
            Pruefe.Gleich((1932, 1388, Wqhd), mf.ZielFuer(500, 40).GetValueOrDefault(), "Monitor 2, unten links");
            wahl = new Meldezeilenwahl(Meldezeilenstelle.ObenMitte, null);
            spiel = Wqhd;
            Pruefe.Gleich((2950, 12, Wqhd), mf.ZielFuer(500, 40).GetValueOrDefault(), "Automatisch mit Spiel auf dem WQHD");
            // 0.11.2: Verliert das Programm das Spiel kurz aus den Augen, bleibt die Meldezeile beim zuletzt gesehenen Spielmonitor (sonst sprang
            // sie auf den Hauptmonitor und zurück).
            spiel = null;
            Pruefe.Gleich((2950, 12, Wqhd), mf.ZielFuer(500, 40).GetValueOrDefault(), "Automatisch, Spiel kurz nicht gesehen: beim zuletzt gesehenen Spielmonitor");
            var neu = new MeldezeileFenster(zeile, new FesteBildschirme([FullHd, Wqhd]), () => 1, () => wahl, () => null);
            fenster.Add(neu);
            Pruefe.Gleich((710, 8, FullHd), neu.ZielFuer(500, 40).GetValueOrDefault(), "Automatisch ohne je ein Spiel: Hauptmonitor, oben Mitte");
            wahl = new Meldezeilenwahl(Meldezeilenstelle.ObenMitte, 0);
            spiel = Wqhd;
            Pruefe.Gleich((710, 8, FullHd), mf.ZielFuer(500, 40).GetValueOrDefault(), "Hauptmonitor gewählt: das Spiel zählt nicht");
            var leer = new MeldezeileFenster(zeile, new FesteBildschirme([]));
            fenster.Add(leer);
            Pruefe.Wahr(leer.ZielFuer(500, 40) is null, "ohne Monitor kein Ziel");
            funkfeld.Dispose();
        });
    }

    /// <summary>Ein Ausschnitt um einen Abschnitt der Einstellungen über die Breite der Karte, aus einem Bild der ganzen Seite in Skala 1.</summary>
    private static CroppedBitmap Ausschnitt(Hauptfenster haupt, BitmapSource ganz, string abschnittName)
    {
        var wurzel = Bildlauf.Wurzel(haupt);
        var seite = Seite(haupt);
        var karte = Teil<Border>(seite, "AbschnitteKarte");
        var rk = karte.TransformToAncestor(wurzel).TransformBounds(new Rect(karte.RenderSize));
        var abschnitt = Teil<StackPanel>(seite, abschnittName);
        var r = abschnitt.TransformToAncestor(wurzel).TransformBounds(new Rect(abschnitt.RenderSize));
        var links = (int)Math.Max(0, Math.Floor(rk.Left - 8));
        var oben = (int)Math.Max(0, Math.Floor(r.Top - 14));
        return new CroppedBitmap(ganz, new Int32Rect(links, oben, (int)Math.Min(ganz.PixelWidth - links, Math.Ceiling(rk.Width + 16)),
            (int)Math.Min(ganz.PixelHeight - oben, Math.Ceiling(r.Height + 28))));
    }
}
