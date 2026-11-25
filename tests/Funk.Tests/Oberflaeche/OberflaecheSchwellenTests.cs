// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Maschine;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Sprachaktivierung einstellen (A26-A28): Schalter Automatik und Lernen, Linie ziehen, Korridor, Hörprobe, Livewerte
/// aus einem fremden Thread. A28 (Sarevoc, 29.09.2026): keine Rückmeldeknöpfe, gelernt wird still.
/// </summary>
internal static class OberflaecheSchwellenTests
{
    private static PegelMeldung Meldung(long ms, float pegel = -50, float boden = -60, float schwelle = -51, float sprache = 0.2f, bool sendet = false) =>
        new(ms, pegel, boden, schwelle, sprache, sendet);

    [Test]
    public static void Schwelle_Start_GibtGespeicherteWerteAnDieMaschine()
    {
        var e = new ClientEinstellungen();
        e.Gate.Uebernehmen(new GateEinstellung(GateModus.Lernend, -40, 11, -57, -33, 1.5f, 7, 0.6f));
        using var a = new Aufbau(einstellungen: e);
        var vm = a.Schwelle();
        Pruefe.Gleich(new GateEinstellung(GateModus.Lernend, -40, 11, -57, -33, 1.5f, 7, 0.6f, GateWerte.MinDb), a.Maschine.Gate, "Gate der Maschine (Grenzlinie ganz unten in der Grafik)");
        Pruefe.Wahr(vm.Automatik && vm.Lernen && vm.ZeigeKorridor && vm.ZeigeAbstand && !vm.ZeigeSchwelle, "Lernend: beide Schalter an, Korridor und Abstand sichtbar");
    }

    [Test]
    public static void Schwelle_A28_SchalterAutomatikUndLernen()
    {
        using var a = new Aufbau();
        var vm = a.Schwelle();
        Pruefe.Wahr(vm.Automatik && !vm.Lernen && vm.KannLernen, "Standard: Automatik an, Lernen aus");
        Pruefe.Gleich(GateModus.Automatisch, a.Maschine.Gate.Modus, "Maschine: Automatisch");

        vm.Lernen = true;
        Pruefe.Gleich(GateModus.Lernend, a.Maschine.Gate.Modus, "Lernen an: Lernend");
        Pruefe.Wahr(vm.ZeigeKorridor, "Korridor sichtbar");

        var geaendert = new HashSet<string?>();
        vm.PropertyChanged += (_, e) => geaendert.Add(e.PropertyName);
        vm.Automatik = false;
        Pruefe.Gleich(GateModus.Fest, a.Maschine.Gate.Modus, "Automatik aus: feste Schwelle");
        Pruefe.Wahr(vm.ZeigeSchwelle && !vm.ZeigeAbstand && !vm.ZeigeKorridor && !vm.KannLernen, "ohne Automatik nur die feste Schwelle, Lernen gesperrt");
        Pruefe.Wahr(vm.Lernen, "die Wahl Lernen bleibt gemerkt");
        foreach (var n in new[] { nameof(vm.Automatik), nameof(vm.KannLernen), nameof(vm.ZeigeSchwelle), nameof(vm.ZeigeKorridor), nameof(vm.ModusKurz), nameof(vm.LinieDb) })
            Pruefe.Wahr(geaendert.Contains(n), "gemeldet: " + n);

        vm.Automatik = true;
        Pruefe.Gleich(GateModus.Lernend, a.Maschine.Gate.Modus, "Automatik wieder an: gilt die gemerkte Wahl Lernen");
        vm.Lernen = false;
        Pruefe.Gleich(GateModus.Automatisch, a.Maschine.Gate.Modus, "Lernen aus: Automatisch");

        vm.Automatik = false;
        vm.Lernen = true;
        Pruefe.Gleich(GateModus.Fest, a.Maschine.Gate.Modus, "Lernen ohne Automatik ändert den Modus nicht");
        a.Ablage.Sichern();
        var gelesen = a.Speicher.Laden().Gate;
        Pruefe.Wahr(gelesen.Modus == GateModus.Fest && gelesen.Lernen, "Modus und gemerkte Wahl gespeichert");
    }

    [Test]
    public static void Schwelle_A28_KeineRueckmeldeknoepfe()
    {
        // Sarevoc, 29.09.2026: keine Knöpfe "war kein Sprechen"/"abgeschnitten". Die Oberfläche bietet außer "Standardwerte"
        // keinen Befehl an, und der Vertrag der Maschine kennt keine Rückmeldung mehr (gelernt wird still).
        var befehle = typeof(SchwellenViewModel).GetProperties()
            .Where(p => typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType)).Select(p => p.Name).ToList();
        Pruefe.Gleich("ZuruecksetzenCommand", string.Join(", ", befehle), "einzige Befehle der Schwelle");
        var vertrag = typeof(IKlangMaschine).GetMethods().Select(m => m.Name).ToList();
        Pruefe.Falsch(vertrag.Any(n => n.StartsWith("Rueckmeldung", StringComparison.Ordinal)), "keine Rückmeldung im Vertrag: " + string.Join(", ", vertrag));
        Pruefe.Falsch(typeof(KlangMaschine).GetMethods().Any(m => m.Name.StartsWith("Rueckmeldung", StringComparison.Ordinal)), "und nicht in der Maschine");

        var xaml = File.ReadAllText(Path.Combine(Wurzel(), "src", "Funk.Client", "Fenster", "Schwellenbereich.xaml"));
        Pruefe.Falsch(xaml.Contains("Rueckmeldung", StringComparison.OrdinalIgnoreCase) || xaml.Contains("abgeschnitten", StringComparison.OrdinalIgnoreCase),
            "kein Rückmeldeknopf im Bereich");
    }

    [Test]
    public static void Schwelle_Zuruecksetzen_BehaeltDieSchalter()
    {
        using var a = new Aufbau();
        var vm = a.Schwelle();
        vm.Lernen = true;
        vm.KorridorBisDb = -20;
        vm.AbstandDb = 20;
        vm.ZuruecksetzenCommand.Execute(null);
        Pruefe.Wahr(vm.Automatik && vm.Lernen, "Schalter bleiben");
        Pruefe.Gleich(9.0, vm.AbstandDb, "Abstand auf Standard");
        Pruefe.Gleich(-30.0, vm.KorridorBisDb, "Korridor auf Standard");
        vm.Automatik = false;
        vm.ZuruecksetzenCommand.Execute(null);
        Pruefe.Wahr(!vm.Automatik && vm.Lernen, "auch ohne Automatik bleibt die gemerkte Wahl");
    }

    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new PruefungException("Repository nicht gefunden");
    }

    [Test]
    public static void Schwelle_Fest_LinieZiehenSetztSchwelle()
    {
        using var a = new Aufbau();
        var vm = a.Schwelle();
        var geaendert = new List<string?>();
        vm.PropertyChanged += (_, e) => geaendert.Add(e.PropertyName);
        vm.Automatik = false;
        Pruefe.Gleich(GateModus.Fest, a.Maschine.Gate.Modus, "Modus an der Maschine");
        Pruefe.Wahr(geaendert.Contains(nameof(SchwellenViewModel.Automatik)) && geaendert.Contains(nameof(SchwellenViewModel.ZeigeAbstand)), "Schalter werden benachrichtigt");
        vm.LinieZiehen(-38.5);
        Pruefe.Gleich(-38.5f, a.Maschine.Gate.SchwelleDb, "Schwelle an der Maschine");
        Pruefe.Gleich(-38.5, vm.LinieDb, "Linie steht dort");
        vm.LinieZiehen(20);
        Pruefe.Gleich(0f, a.Maschine.Gate.SchwelleDb, "oben bei 0 dBFS begrenzt");
        vm.LinieZiehen(double.NaN);
        Pruefe.Gleich(0f, a.Maschine.Gate.SchwelleDb, "NaN ändert nichts");
        vm.LinieSchieben(-5);
        Pruefe.Gleich(-5f, a.Maschine.Gate.SchwelleDb, "Tastatur schiebt die Linie");
        a.Ablage.Sichern();
        Pruefe.Gleich(-5f, a.Speicher.Laden().Gate.SchwelleDb, "gespeichert");
    }

    /// <summary>
    /// Sarevoc (Stufe 2c, Punkt 1): Die Linie der Automatik zeigt nur an, was die Maschine gerade nimmt; ziehen lässt sie sich nicht
    /// mehr (bis 2b setzte Ziehen den Abstand zum Boden, der Griff sah aber nur im Modus Fest so aus, wie er wirkte). Der Abstand
    /// bleibt am Regler.
    /// </summary>
    [Test]
    public static void Schwelle_Automatisch_LinieZeigtNurAn()
    {
        using var a = new Aufbau();
        var vm = a.Schwelle();
        vm.Automatik = true;
        a.Maschine.Melden(Meldung(100, boden: -60, schwelle: -51));
        Pruefe.Wahr(vm.Takt(), "neue Meldung");
        Pruefe.Gleich(-51.0, vm.LinieDb, "Linie zeigt die wirksame Schwelle der Maschine");
        Pruefe.Falsch(vm.ZeigeSchwelle, "Automatik: keine gezogene Schwelle");
        var vorher = a.Maschine.Gate;
        vm.LinieZiehen(-48);
        vm.LinieSchieben(5);
        Pruefe.Gleich(vorher, a.Maschine.Gate, "Ziehen ändert nichts; ohne gelernte Stimme auch der Pfeil nicht (Klangstufe C1)");
        // Mit gelernter Stimme schiebt der Pfeil die Mindest-Funklautstärke (bis Klangstufe C1 die Grenzlinie).
        a.Maschine.Korridor = new KorridorStand(-23f, float.NaN, float.NaN);
        vm.Takt();
        vm.LinieZiehen(-48);
        vm.LinieSchieben(5);
        Pruefe.Gleich(vorher with { MindestAbstandDb = vorher.MindestAbstandDb - 5 }, a.Maschine.Gate, "Ziehen ändert nichts; Pfeil hoch schiebt die Mindest-Funklautstärke");
        a.Maschine.Korridor = KorridorStand.Leer;
        vm.Takt();
        vm.Lernen = true;
        var lernend = a.Maschine.Gate;
        vm.LinieZiehen(-48);
        vm.LinieSchieben(5);
        Pruefe.Gleich(lernend, a.Maschine.Gate, "Lernend: weder Ziehen noch Pfeil ändern etwas (der Korridor hat Bild hoch/runter)");
    }

    /// <summary>
    /// Sarevoc (Stufe 2c, Punkt 2), seit Klangstufe C1 als Mindest-Funklautstärke relativ zur gelernten Stimme statt der Grenzlinie: ab Werk
    /// 25 dB unter der Stimme; gezogen wirkt sie an der Maschine und wird gemerkt; sie gilt in Automatisch und Lernend (dort mit gelernter
    /// Stimme statt des Korridors von/bis), im Modus Fest die gezogene Schwelle. Standardwerte setzen sie zurück.
    /// </summary>
    [Test]
    public static void Schwelle_Automatik_Mindest_WirktUndWirdGemerkt()
    {
        using var a = new Aufbau();
        a.Maschine.Korridor = new KorridorStand(-23f, float.NaN, float.NaN);
        var vm = a.Schwelle();
        var k = vm.Korridor;
        Pruefe.Gleich(GateVorgaben.MindestAbstandAbWerkDb, a.Maschine.Gate.MindestAbstandDb, "an der Maschine ab Werk");
        Pruefe.Wahr(k.Sichtbar && !vm.ZeigeKorridor && !vm.ZeigeSchwelle, "Automatik: Korridor relativ zur Stimme, kein von/bis, keine feste Schwelle");

        var namen = new HashSet<string?>();
        vm.PropertyChanged += (_, e) => namen.Add(e.PropertyName);
        k.UntergrenzeZiehen(-52.5);
        Pruefe.Gleich(29.5f, a.Maschine.Gate.MindestAbstandDb, "gezogen: an der Maschine");
        Pruefe.Wahr(namen.Contains(nameof(vm.GrafikHilfe)), "die Schwelle meldet sich neu");
        k.UntergrenzeZiehen(0);
        Pruefe.Gleich(9f, a.Maschine.Gate.MindestAbstandDb, "oben begrenzt: 3 dB unter der Obergrenze (nicht bis 0)");
        k.UntergrenzeZiehen(double.NaN);
        Pruefe.Gleich(9f, a.Maschine.Gate.MindestAbstandDb, "NaN ändert nichts");
        k.UntergrenzeZiehen(-61);
        a.Ablage.Sichern();
        Pruefe.Gleich(38f, a.Speicher.Laden().Gate.MindestAbstandDb, "gespeichert");

        vm.Lernen = true;
        Pruefe.Wahr(k.Sichtbar && !vm.ZeigeKorridor, "Lernend mit Stimme: derselbe Korridor");
        vm.Automatik = false;
        Pruefe.Wahr(!k.Sichtbar && vm.ZeigeSchwelle, "Fest: die Schwelle statt des Korridors");
        Pruefe.Gleich(38f, a.Maschine.Gate.MindestAbstandDb, "die Mindest-Funklautstärke bleibt gemerkt");
        vm.Automatik = true;
        vm.Lernen = false;
        vm.ZuruecksetzenCommand.Execute(null);
        Pruefe.Gleich(GateVorgaben.MindestAbstandAbWerkDb, a.Maschine.Gate.MindestAbstandDb, "Standardwerte: wieder ab Werk");
    }

    /// <summary>Eine Datei bis Stufe 2b kennt keine Grenzlinie: ganz unten. Unsinn wird beim Laden begrenzt.</summary>
    [Test]
    public static void Schwelle_Grenzlinie_AlteDateiUndUnsinn()
    {
        using var ordner = new Testordner();
        var speicher = new EinstellungsSpeicher(ordner.Datei("e.json"));
        File.WriteAllText(speicher.Pfad, """{ "gate": { "modus": "Automatisch", "abstandDb": 12 } }""");
        var alt = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "lesbar");
        Pruefe.Gleich(GateWerte.MinDb, alt.Gate.GrenzlinieDb, "ohne Feld ganz unten");
        File.WriteAllText(speicher.Pfad, """{ "gate": { "modus": "Automatisch", "grenzlinieDb": 40 } }""");
        Pruefe.Gleich(GateWerte.GrenzeMaxDb, speicher.Laden().Gate.GrenzlinieDb, "zu hoch: bei -30 dBFS begrenzt");
        File.WriteAllText(speicher.Pfad, """{ "gate": { "modus": "Automatisch", "grenzlinieDb": -300 } }""");
        Pruefe.Gleich(GateWerte.MinDb, speicher.Laden().Gate.GrenzlinieDb, "zu tief: unten begrenzt");
        speicher.Speichern(alt);
        Pruefe.Wahr(File.ReadAllText(speicher.Pfad).Contains("\"grenzlinieDb\"", StringComparison.Ordinal), "neue Dateien tragen das Feld");
    }

    /// <summary>
    /// Sarevoc (Stufe 2c, Punkte 1 und 2) in der Grafik: Griffe nur dort, wo man ziehen kann. Fest: die Linie. Automatik (seit Klangstufe C1): der Korridor statt der
    /// Grenzlinie, nicht die rote Linie der Automatik. Lernend ohne gelernte Stimme: die zwei Griffe des Korridors von/bis. Gezeichnet
    /// wird der runde Griff nur an der Linie, die er zieht; die rote Linie hat genug Kontrast auf der Grafikfläche (3:1).
    /// </summary>
    [Test]
    public static void Schwellengrafik_GriffeNurWoManZiehenKann()
    {
        Bildlauf.Ausfuehren(_ =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau();
            var vm = a.Schwelle();
            var g = new SchwellenGrafik { Modell = vm, Width = 600, Height = 320 };
            g.Measure(new System.Windows.Size(600, 320));
            g.Arrange(new System.Windows.Rect(0, 0, 600, 320));
            var p = g.Pegelflaeche;
            System.Windows.Point An(double db) => new(p.Right - 2, SchwellenGrafik.DbZuY(db, p));

            vm.Automatik = false;
            vm.SchwelleDb = -40;
            Pruefe.Gleich("Linie", g.GriffAn(An(-40)), "Fest: die Linie hat den Griff");

            // Automatik (seit Klangstufe C1 mit dem Korridor relativ zur gelernten Stimme statt der Grenzlinie): Stimme -10, also
            // Mindest-Funklautstärke -35 und Obergrenze -16; die rote Linie steht bei -50.
            vm.Automatik = true;
            a.Maschine.Korridor = new KorridorStand(-10f, float.NaN, float.NaN);
            for (var i = 0; i < 60; i++) a.Maschine.Melden(Meldung(i * 20L, boden: -62, schwelle: -50));
            vm.Takt();
            Pruefe.Gleich(null, g.GriffAn(An(-50)), "Automatik: die rote Linie lässt sich nicht greifen");
            Pruefe.Gleich("Untergrenze", g.GriffAn(An(-35)), "Automatik: die Mindest-Funklautstärke hat den Griff");
            Pruefe.Gleich("Obergrenze", g.GriffAn(An(-16)), "Automatik: die Obergrenze hat den Griff");
            Pruefe.Gleich(null, g.GriffAn(An(-70)), "Automatik: darunter nichts");

            a.Maschine.Korridor = KorridorStand.Leer;
            vm.Takt();
            vm.Lernen = true;
            vm.KorridorVonDb = -60;
            vm.KorridorBisDb = -30;
            Pruefe.Gleich(null, g.GriffAn(An(-50)), "Lernend: die rote Linie lässt sich nicht greifen");
            Pruefe.Gleich(null, g.GriffAn(An(-70)), "Lernend: darunter nichts");
            Pruefe.Gleich("KorridorOben", g.GriffAn(new System.Windows.Point(p.Right + 6, SchwellenGrafik.DbZuY(-30, p))), "Lernend: Griff bis");
            Pruefe.Gleich("KorridorUnten", g.GriffAn(new System.Windows.Point(p.Right + 6, SchwellenGrafik.DbZuY(-60, p))), "Lernend: Griff von");

            // Gezeichnet: der runde Griff nur an der gezogenen Linie, an der Linie im Akzent, an der Grenzlinie blau. Ohne Fenster zeichnet die Grafik neu, wenn sie ihr
            // Modell (wieder) bekommt.
            var akzent = Farbvorschlaege.Anthrazit.Akzent;
            bool GriffBei(double db, System.Windows.Media.Color farbe)
            {
                g.Modell = null;
                g.Modell = vm;
                var bild = OberflaecheHilfeTests.Zeichnen(g, 1);
                var bytes = new byte[bild.PixelWidth * bild.PixelHeight * 4];
                bild.CopyPixels(bytes, bild.PixelWidth * 4, 0);
                var (x, y) = ((int)Math.Round(p.Right), (int)Math.Round(SchwellenGrafik.DbZuY(db, p)));
                var treffer = 0;
                for (var dy = -3; dy <= 3; dy++)
                    for (var dx = -3; dx <= 3; dx++)
                    {
                        var i = (((y + dy) * bild.PixelWidth) + x + dx) * 4;
                        if (Math.Abs(bytes[i + 2] - farbe.R) < 12 && Math.Abs(bytes[i + 1] - farbe.G) < 12 && Math.Abs(bytes[i] - farbe.B) < 12) treffer++;
                    }
                return treffer >= 20;
            }
            vm.Lernen = false;
            a.Maschine.Korridor = new KorridorStand(-10f, float.NaN, float.NaN);
            vm.Takt();
            Pruefe.Wahr(GriffBei(-35, akzent), "Automatik: runder Griff im Akzent an der Mindest-Funklautstärke (Klangstufe C1)");
            Pruefe.Falsch(GriffBei(-50, akzent), "Automatik: kein runder Griff an der roten Linie");
            vm.Automatik = false;
            Pruefe.Wahr(GriffBei(-40, akzent), "Fest: runder Griff im Akzent an der Linie");
        });
        // Die rote Linie trägt das Rot für Schrift der Farbstellung (der Akzent ist Petrol) und hebt sich mit mindestens 3:1 ab.
        foreach (var satz in Farbvorschlaege.Alle)
            Funk.Tests.Verarbeitung.PruefeWert.Mindestens(3, Kontrast.Verhaeltnis(satz.RotSchrift, Palette.FlaecheTief), satz.Name + ": rote Linie gegen die Grafikfläche");
    }

    [Test]
    public static void Schwelle_Lernend_KorridorGriffeHaltenMindestabstand()
    {
        using var a = new Aufbau();
        var vm = a.Schwelle();
        vm.Lernen = true;
        vm.KorridorVonDb = -55;
        vm.KorridorBisDb = -30;
        vm.KorridorZiehen(oben: true, -70);
        Pruefe.Gleich(-52.0, vm.KorridorBisDb, "bis bleibt 3 dB über von");
        vm.KorridorZiehen(oben: false, -10);
        Pruefe.Gleich(-55.0, vm.KorridorVonDb, "von bleibt 3 dB unter bis");
        vm.KorridorZiehen(oben: true, -20);
        vm.KorridorZiehen(oben: false, -60);
        Pruefe.Gleich(new GateEinstellung(GateModus.Lernend, -45, 9, -60, -20, 2, 5, 0.5f, GateWerte.MinDb), a.Maschine.Gate, "Korridor an der Maschine");
        vm.AnstiegSekunden = 0.01;
        vm.RueckkehrSekunden = 500;
        Pruefe.Gleich(GateWerte.ZeitMinSek, a.Maschine.Gate.AnstiegSekunden, "Anstieg begrenzt");
        Pruefe.Gleich(GateWerte.RueckkehrMaxSek, a.Maschine.Gate.RueckkehrSekunden, "Rückkehr begrenzt");
    }

    [Test]
    public static void Schwelle_Hoerprobe_MithoerenNurMitHeadset()
    {
        using var a = new Aufbau();
        var vm = a.Schwelle();
        Pruefe.Falsch(a.Maschine.Hoerprobe, "aus zu Beginn");
        // Frage "Headset oder Lautsprecher" noch offen: wie Lautsprecher, denn dort käme die Stimme zurück ins Mikrofon.
        vm.Hoerprobe.Mithoeren = true;
        Pruefe.Falsch(a.Maschine.Hoerprobe, "ohne Headset bleibt das Mithören aus");
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        vm.Hoerprobe.Mithoeren = true;
        Pruefe.Wahr(a.Maschine.Hoerprobe, "mit Headset an");
        vm.Hoerprobe.Mithoeren = false;
        Pruefe.Falsch(a.Maschine.Hoerprobe, "wieder aus");
    }

    [Test]
    public static async Task Schwelle_Takt_UebernimmtWerteAusFremdemThread()
    {
        using var a = new Aufbau();
        var vm = a.Schwelle();
        var neu = 0;
        vm.Aktualisiert += () => neu++;
        Pruefe.Falsch(vm.Takt(), "ohne Meldung nichts Neues");
        await Task.Run(() =>
        {
            for (var i = 0; i < 600; i++) a.Maschine.Melden(Meldung(i * 20L, pegel: -30 - (i % 7), sendet: i % 2 == 0, sprache: 0.9f));
        });
        Pruefe.Wahr(vm.Takt(), "Neues da");
        Pruefe.Gleich(1, neu, "ein Neuzeichnen je Takt");
        Pruefe.Gleich(-30.0 - (599 % 7), vm.PegelDbfs, "jüngster Pegel");
        Pruefe.Falsch(vm.Sendet, "599 ist ungerade: sendet nicht");
        Pruefe.Wahr(Math.Abs(vm.Sprachwahrscheinlichkeit - 0.9) < 1e-6, "Sprachwahrscheinlichkeit");
        Pruefe.Falsch(vm.Takt(), "zweiter Takt ohne neue Meldung");

        var puffer = new PegelMeldung[1024];
        var n = a.Verlauf.Kopieren(puffer, 11_980 - PegelVerlauf.FensterMs);
        Pruefe.Gleich(501, n, "10 s bei 50 je Sekunde, beide Enden eingeschlossen");
        Pruefe.Gleich(1980L, puffer[0].ZeitMs, "älteste im Fenster zuerst");
        Pruefe.Gleich(11_980L, puffer[n - 1].ZeitMs, "jüngste zuletzt");
    }

    [Test]
    public static void Pegelverlauf_Ring_UndBereinigung()
    {
        var v = new PegelVerlauf(8);
        Pruefe.Falsch(v.Letzte(out _), "leer");
        for (var i = 0; i < 20; i++) v.Hinzufuegen(Meldung(i));
        var puffer = new PegelMeldung[16];
        var n = v.Kopieren(puffer, 0);
        Pruefe.Gleich(8, n, "nur die Kapazität");
        Pruefe.Gleich(12L, puffer[0].ZeitMs, "die ältesten fielen heraus");
        Pruefe.Gleich(4, v.Kopieren(puffer.AsSpan(0, 4), 0), "Ziel kleiner: die jüngsten");
        Pruefe.Gleich(16L, puffer[0].ZeitMs, "jüngste vier ab 16");
        v.Hinzufuegen(new PegelMeldung(99, float.NaN, float.NegativeInfinity, float.PositiveInfinity, float.NaN, true));
        Pruefe.Wahr(v.Letzte(out var m), "da");
        Pruefe.Wahr(float.IsFinite(m.PegelDbfs) && float.IsFinite(m.RauschbodenDb) && float.IsFinite(m.SchwelleDb) && m.Sprachwahrscheinlichkeit == 0, "nicht endliche Werte ersetzt");
        Pruefe.Gleich(21L, v.Gesamt, "Gesamtzahl");
    }

    [Test]
    public static async Task Pegelverlauf_LesenOhneSperre_LiefertNurUnversehrteFolgen()
    {
        // Ein Schreiber wie die Maschine, ein Leser wie der Bildtakt, gleichzeitig. Jede gelesene Folge muss lückenlos
        // aufsteigen (nichts zerrissen, nichts überschrieben), und die jüngste darf nie zurückspringen.
        var v = new PegelVerlauf(64);
        const int anzahl = 400_000;
        var schreiber = Task.Run(() =>
        {
            for (var i = 0; i < anzahl; i++) v.Hinzufuegen(new PegelMeldung(i, -i % 90, -60, -50, 0.5f, i % 2 == 0));
        });
        var puffer = new PegelMeldung[64];
        var fehler = "";
        long letzte = -1;
        var lesungen = 0;
        while (!schreiber.IsCompleted || lesungen < 10)
        {
            var n = v.Kopieren(puffer, long.MinValue);
            for (var k = 1; k < n && fehler.Length == 0; k++)
                if (puffer[k].ZeitMs != puffer[k - 1].ZeitMs + 1) fehler = $"Lücke bei {puffer[k - 1].ZeitMs} -> {puffer[k].ZeitMs}";
            for (var k = 0; k < n && fehler.Length == 0; k++)
                if (puffer[k].Sendet != (puffer[k].ZeitMs % 2 == 0)) fehler = "zerrissene Meldung bei " + puffer[k].ZeitMs;
            if (v.Letzte(out var m))
            {
                if (m.ZeitMs < letzte) fehler = $"jüngste sprang zurück: {letzte} -> {m.ZeitMs}";
                letzte = m.ZeitMs;
            }
            lesungen++;
            if (fehler.Length > 0) break;
        }
        await schreiber;
        Pruefe.Gleich("", fehler, "Lesen während des Schreibens");
        Pruefe.Gleich(64, v.Kopieren(puffer, long.MinValue), "am Ende voll");
        Pruefe.Gleich(anzahl - 1L, puffer[63].ZeitMs, "jüngste zuletzt");
    }

    [Test]
    public static void Pegelverlauf_UndTafel_SchreibenOhneAllokation()
    {
        var v = new PegelVerlauf();
        var t = new EmpfangsPegelTafel();
        var puffer = new PegelMeldung[1024];
        // Aufwärmen (JIT), dann messen.
        for (var i = 0; i < 100; i++)
        {
            v.Hinzufuegen(new PegelMeldung(i, -30, -60, -50, 0.5f, false));
            t.Setzen((ushort)(i % 12), -20, i);
        }
        v.Kopieren(puffer, 0);
        v.Letzte(out _);
        t.Lesen(3, 100, 400, out _);
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            v.Hinzufuegen(new PegelMeldung(i, float.NaN, -60, -50, 2f, true));
            t.Setzen((ushort)(i % 12), -20, i);
        }
        var n = v.Kopieren(puffer, 0);
        v.Letzte(out _);
        t.Lesen(3, 10_000, 400, out _);
        Pruefe.Gleich(0L, GC.GetAllocatedBytesForCurrentThread() - vorher, "Schreiben und Lesen ohne Allokation");
        Pruefe.Gleich(1024, n, "Ring voll");
    }

    [Test]
    public static void EmpfangsPegelTafel_JeNetzMitAlter()
    {
        var t = new EmpfangsPegelTafel();
        Pruefe.Falsch(t.Lesen(4, 1000, 400, out _), "leer");
        t.Setzen(4, -12.5f, 1000);
        t.Setzen(4 + EmpfangsPegelTafel.Plaetze, -30f, 1000);
        Pruefe.Wahr(t.Lesen(4, 1200, 400, out var a) && Math.Abs(a - -12.5f) < 1e-6, "Netz 4");
        Pruefe.Wahr(t.Lesen(4 + EmpfangsPegelTafel.Plaetze, 1200, 400, out var b) && Math.Abs(b - -30f) < 1e-6, "gleicher Platz, anderes Netz: weitergerückt");
        Pruefe.Wahr(t.Lesen(4, 1000, 400, out _), "gleiche Millisekunde gilt als frisch");
        Pruefe.Falsch(t.Lesen(4, 1500, 400, out _), "zu alt");
        Pruefe.Falsch(t.Lesen(5, 1200, 400, out _), "anderes Netz nicht gemeldet");
        for (ushort n = 100; n < 300; n++) t.Setzen(n, -20, 2000);
        Pruefe.Wahr(t.Lesen(4, 2000, 1000, out _), "volle Tafel verdrängt nichts");
        Pruefe.Falsch(t.Lesen(299, 2000, 400, out _), "über 64 Netze: keine Anzeige, kein Fehler");
        t.Setzen(4, -6f, uint.MaxValue + 10L);
        Pruefe.Wahr(t.Lesen(4, uint.MaxValue + 20L, 400, out var c) && Math.Abs(c - -6f) < 1e-6, "Zeitüberlauf nach 49 Tagen");
    }

    [Test]
    public static void Schwellengrafik_UmrechnungDbUndPixel()
    {
        var flaeche = new System.Windows.Rect(40, 10, 600, 300);
        Pruefe.Gleich(310.0, SchwellenGrafik.DbZuY(-90, flaeche), "-90 dBFS unten");
        Pruefe.Gleich(10.0, SchwellenGrafik.DbZuY(0, flaeche), "0 dBFS oben");
        Pruefe.Gleich(10.0, SchwellenGrafik.DbZuY(20, flaeche), "darüber begrenzt");
        for (var db = -90.0; db <= 0; db += 0.5)
            Pruefe.Gleich(db, SchwellenGrafik.YZuDb(SchwellenGrafik.DbZuY(db, flaeche), flaeche), "Hin und zurück bei " + db);
        Pruefe.Gleich(-90.0, SchwellenGrafik.YZuDb(1000, flaeche), "unter der Fläche");
    }

    [Test]
    public static void Schwelle_Aenderung_MeldetAlleAbhaengigenWerte()
    {
        using var a = new Aufbau();
        var vm = a.Schwelle();
        var namen = new HashSet<string?>();
        PropertyChangedEventHandler h = (_, e) => namen.Add(e.PropertyName);
        vm.PropertyChanged += h;
        vm.KorridorBisDb = -35;
        foreach (var n in new[] { nameof(vm.KorridorBisDb), nameof(vm.KorridorVonDb), nameof(vm.LinieDb), nameof(vm.LinieText) })
            Pruefe.Wahr(namen.Contains(n), "gemeldet: " + n);
        vm.PropertyChanged -= h;
    }
}
