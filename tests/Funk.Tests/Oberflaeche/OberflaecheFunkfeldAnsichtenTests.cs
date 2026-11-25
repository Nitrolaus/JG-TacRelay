// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Funkfeld kompakt (Sarevoc, 29.09.2026; A29 entschieden: Raster Voreinstellung, Liste Alternative, Leiste entfallen):
/// Ansichten, Durchsichtigkeit samt Anzeige ihrer Wirkung, Platz je Ansicht (Lage in Bildpunkten, Größe nur nach Ziehen), Einstellungen mit
/// alter Datei, und Lautstärke und VA/Taste je Netz im Bereich Netze, dieselben wie auf der Kachel. Ohne WPF-Fenster;
/// Größen und Bilder prüfen OberflaecheFunkfeldBilderTests und OberflaecheFunkfeldDurchsichtTests.
/// </summary>
internal static class OberflaecheFunkfeldAnsichtenTests
{
    private static List<string> Mitschreiben(INotifyPropertyChanged quelle)
    {
        var namen = new List<string>();
        quelle.PropertyChanged += (_, e) => namen.Add(e.PropertyName ?? "");
        return namen;
    }

    /// <summary>Texte für Sarevoc: als Striche nur "-" und "_", gerade Anführungszeichen, keine Auslassungszeichen.</summary>
    internal static void NurEinfacheZeichen(string text, string was) =>
        Pruefe.Falsch(text.Any(z => z is '\u2013' or '\u2014' or '\u2212' or '\u201E' or '\u201C' or '\u201D' or '\u2026' or '\u2022'), was + ": nur einfache Zeichen in: " + text);

    [Test]
    public static void Funkfeld_Darstellung_UmschaltenUndGemerkt()
    {
        Pruefe.Gleich("Raster,Liste", string.Join(",", Enum.GetNames<FunkfeldDarstellung>()), "A29: nur noch Raster und Liste");
        using var a = new Aufbau();
        var f = a.Funkfeld();
        Pruefe.Gleich(FunkfeldDarstellung.Raster, f.Darstellung, "Voreinstellung A Raster");
        Pruefe.Wahr(f.IstRaster && !f.IstListe, "genau eine Ansicht");
        var gemeldet = Mitschreiben(f);
        f.IstListe = true;
        Pruefe.Gleich(FunkfeldDarstellung.Liste, f.Darstellung, "C Liste");
        Pruefe.Wahr(gemeldet.Contains(nameof(f.IstRaster)) && gemeldet.Contains(nameof(f.IstListe)), "Umschalter werden benachrichtigt");
        f.IstRaster = false;
        Pruefe.Gleich(FunkfeldDarstellung.Liste, f.Darstellung, "Abwählen durch die Gruppe ändert nichts");
        f.Darstellung = (FunkfeldDarstellung)7;
        Pruefe.Gleich(FunkfeldDarstellung.Liste, f.Darstellung, "unbekannter Wert wird übergangen");
        f.Darstellung = (FunkfeldDarstellung)1;
        Pruefe.Gleich(FunkfeldDarstellung.Liste, f.Darstellung, "die Zahl der alten Leiste wird übergangen");
        Pruefe.Gleich(10, f.Kacheln.Count, "Ansicht ändert die Felder nicht");
        Pruefe.Wahr(f.Kacheln[^1].IstGlobal, "Global bleibt das letzte Feld (A23)");
        a.Ablage.Sichern();
        Pruefe.Gleich(FunkfeldDarstellung.Liste, a.Speicher.Laden().Funkfeld.Darstellung, "gespeichert");

        // Seit 0.10.0 (#75) steht die Wahl Raster oder Liste nur im Funkfeld selbst, nicht mehr unter Einstellungen; ein neues Funkfeld liest die
        // gemerkte Ansicht.
        Pruefe.Gleich(null, typeof(EinstellungenViewModel).GetProperty("Raster"), "unter Einstellungen keine Wahl Raster");
        Pruefe.Gleich(null, typeof(EinstellungenViewModel).GetProperty("Liste"), "unter Einstellungen keine Wahl Liste");
        var g = a.Funkfeld();
        Pruefe.Wahr(g.IstListe && !g.IstRaster, "ein neues Funkfeld nimmt die gemerkte Ansicht");
        g.Dispose();
    }

    [Test]
    public static void Durchsicht_GrenzenStandardUndWirkung()
    {
        Pruefe.Gleich(new Durchsicht(1, 1), Durchsicht.Aus(0, false), "0 %: deckend");
        Pruefe.Gleich(new Durchsicht(1, 1), Durchsicht.Aus(0, true), "0 %: deckend, auch deckend halten");
        Pruefe.Wahr(Math.Abs(Durchsicht.Aus(40, false).Fenster - 0.6) < 1e-9 && Durchsicht.Aus(40, false).Hintergrund == 1, "40 %: das ganze Fenster (Hauptweg)");
        Pruefe.Wahr(Math.Abs(Durchsicht.Aus(40, true).Hintergrund - 0.6) < 1e-9 && Durchsicht.Aus(40, true).Fenster == 1, "40 % deckend halten: nur der Hintergrund");
        Pruefe.Wahr(Math.Abs(Durchsicht.Aus(70, false).Fenster - 0.3) < 1e-9, "70 % ganzes Fenster wirkt als 70 % (die Grenze 48 % ist entfallen)");
        Pruefe.Wahr(Math.Abs(Durchsicht.Aus(85, false).Fenster - 0.15) < 1e-9, "85 %: 15 % Deckkraft");
        Pruefe.Wahr(Math.Abs(Durchsicht.Aus(100, false).Fenster - Durchsicht.MindestDeckkraft) < 1e-9, "über 85 % bleibt es bei 15 %");
        Pruefe.Wahr(Math.Abs(Durchsicht.MindestDeckkraft - 0.15) < 1e-9, "nie unter 15 % Deckkraft");
        Pruefe.Wahr(Math.Abs(Durchsicht.Aus(85, true).Hintergrund - Durchsicht.MindestAlpha) < 1e-9, "deckend halten, 85 %: Lücken zu 20 % deckend");
        Pruefe.Gleich(new Durchsicht(1, 1), Durchsicht.Aus(-10, false), "unter 0: deckend");
        Pruefe.Wahr(Durchsicht.MindestAlpha * 255 >= 50 && Durchsicht.MindestDeckkraft * 255 >= 38, "mindestens Alpha 38 von 255: Klicks fallen nicht durch");
        Pruefe.Gleich(null, typeof(Durchsicht).GetProperty("Geschichtet"), "keine Frage der Schichtung mehr: das Funkfeld ist immer geschichtet");
        Pruefe.Gleich(null, typeof(Durchsicht).GetField("GanzesFensterMax"), "die Grenze 48 % ist entfallen");

        using var a = new Aufbau();
        var f = a.Funkfeld();
        Pruefe.Gleich(0, f.Durchsichtigkeit, "Standard: deckend");
        Pruefe.Falsch(f.SchriftDeckend, "Standard: das ganze Fenster, Schrift nicht deckend gehalten");
        Pruefe.Gleich(FunkfeldDeckung.Deckend, f.Deckung, "Standard: alle Flächen deckend");
        var gemeldet = Mitschreiben(f);
        f.Durchsichtigkeit = 95;
        Pruefe.Gleich(FunkfeldEinstellungen.DurchsichtigkeitMax, f.Durchsichtigkeit, "oben auf 85 % begrenzt");
        Pruefe.Wahr(gemeldet.Contains(nameof(f.Durchsicht)) && gemeldet.Contains(nameof(f.Deckung)), "das Fenster wird benachrichtigt (live)");
        f.Durchsichtigkeit = -3;
        Pruefe.Gleich(0, f.Durchsichtigkeit, "unten auf 0 begrenzt");
        gemeldet.Clear();
        f.SchriftDeckend = true;
        Pruefe.Wahr(gemeldet.Contains(nameof(f.Deckung)), "die Option wirkt live");
        f.SchriftDeckend = false;

        var h = a.Haupt(out var g, out _, out _, out _);
        var e = (EinstellungenViewModel)Seiten.Einstellungen(h).Inhalt;
        Pruefe.Gleich("Deckend.", e.DurchsichtWirkung, "0 %: Anzeige");
        var eGemeldet = Mitschreiben(e);
        e.Durchsichtigkeit = 42.4;
        Pruefe.Gleich(42, g.Durchsichtigkeit, "Regler in den Einstellungen, ganze Prozent");
        Pruefe.Gleich("42 %", e.DurchsichtigkeitText, "Anzeige");
        Pruefe.Wahr(eGemeldet.Contains(nameof(e.DurchsichtWirkung)), "die Wirkung zieht mit");
        Pruefe.Wahr(e.DurchsichtWirkung.Contains("ganze Funkfeld ist 42 % durchsichtig", StringComparison.Ordinal), "Hauptweg: " + e.DurchsichtWirkung);
        e.Durchsichtigkeit = 85;
        Pruefe.Wahr(e.DurchsichtWirkung.Contains("85 % durchsichtig, Schrift und Zustandsfarben mit", StringComparison.Ordinal), "bis 85 %, ohne Grenze: " + e.DurchsichtWirkung);
        e.SchriftDeckend = true;
        Pruefe.Wahr(g.SchriftDeckend, "Option in den Einstellungen");
        e.Durchsichtigkeit = 70;
        var d = g.Deckung;
        Pruefe.Wahr(e.DurchsichtWirkung.StartsWith("Lücken und Titelzeile 70 % durchsichtig, Kacheln " + FunkfeldDeckung.Prozent(d.Kachel) + " %", StringComparison.Ordinal),
            "deckend halten: was durchsichtig wird: " + e.DurchsichtWirkung);
        Pruefe.Wahr(FunkfeldDeckung.Prozent(d.Kachel) < 70, "die Kacheln bleiben deckender als die Lücken");
        Pruefe.Wahr(EinstellungenViewModel.SchriftDeckendHinweis.Contains("ganze Funkfeld", StringComparison.Ordinal), "der Hinweis nennt den Hauptweg");
        foreach (var (text, was) in new[] { (e.DurchsichtWirkung, "Wirkung"), (EinstellungenViewModel.SchriftDeckendHinweis, "Hinweis") })
            NurEinfacheZeichen(text, was);
        e.Durchsichtigkeit = double.NaN;
        Pruefe.Gleich(0, g.Durchsichtigkeit, "NaN wird 0");
        a.Ablage.Sichern();
        var geladen = a.Speicher.Laden();
        Pruefe.Wahr(geladen.Funkfeld.SchriftDeckend, "gemerkt");
        var json = File.ReadAllText(a.Speicher.Pfad);
        Pruefe.Wahr(json.Contains("\"schriftDeckend\": true", StringComparison.Ordinal) && !json.Contains("ganzesFenster", StringComparison.Ordinal),
            "als schriftDeckend geschrieben, das alte Feld nicht mehr");
    }

    [Test]
    public static void Einstellungen_Funkfeld_RundlaufUndAlteDatei()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        var e = new ClientEinstellungen();
        e.Funkfeld.Darstellung = FunkfeldDarstellung.Liste;
        e.Funkfeld.Durchsichtigkeit = 55;
        e.Funkfeld.SchriftDeckend = true;
        e.Funkfeld.Merken(FunkfeldDarstellung.Liste, 10, new FensterOrt(2888, 210, 240, 310), groesseGezogen: true);
        e.Funkfeld.Merken(FunkfeldDarstellung.Raster, 10, new FensterOrt(175, 70, 776, 193), groesseGezogen: false);
        e.Farbstellung = Farbstellung.Anthrazit;
        s.Speichern(e);
        var g = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "lesbar");
        Pruefe.Gleich(FunkfeldDarstellung.Liste, g.Funkfeld.Darstellung, "Ansicht");
        Pruefe.Gleich(55, g.Funkfeld.Durchsichtigkeit, "Durchsichtigkeit");
        Pruefe.Wahr(g.Funkfeld.SchriftDeckend, "Schrift und Symbole deckend halten");
        Pruefe.Gleich(Farbstellung.Anthrazit, g.Farbstellung, "Farbstellung");
        Pruefe.Gleich(new FensterOrt(2888, 210, 240, 310), g.Funkfeld.OrtFuer(FunkfeldDarstellung.Liste, 10, 1.75), "gezogene Größe je Ansicht");
        Pruefe.Gleich(new FensterOrt(175, 70), g.Funkfeld.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "nicht gezogen: nur die Lage");
        var json = File.ReadAllText(s.Pfad);
        Pruefe.Wahr(json.Contains("\"Liste\"", StringComparison.Ordinal) && json.Contains("\"Anthrazit\"", StringComparison.Ordinal), "Aufzählungen als Text");
        Pruefe.Wahr(json.Contains("\"fensterorte\"", StringComparison.Ordinal) && !json.Contains("\"orte\"", StringComparison.Ordinal)
            && !json.Contains("\"plaetze\"", StringComparison.Ordinal), "Plätze unter fensterorte, die alten Felder nicht mehr");

        // Datei aus der Zeit vor den Ansichten: nur Ansicht, Startverhalten und der alte, breite Platz.
        File.WriteAllText(s.Pfad, """
            { "funkfeld": { "ansicht": 5, "beimStartZeigen": false, "fenster": { "links": 300, "oben": 40, "breite": 1640, "hoehe": 226 } } }
            """);
        var alt = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "alte Datei lesbar");
        Pruefe.Gleich(5, alt.Funkfeld.Ansicht, "5er bleibt");
        Pruefe.Falsch(alt.Funkfeld.BeimStartZeigen, "Startverhalten bleibt");
        Pruefe.Gleich(FunkfeldDarstellung.Raster, alt.Funkfeld.Darstellung, "ohne Angabe A Raster");
        Pruefe.Gleich(Farbstellung.Anthrazit, alt.Farbstellung, "ohne Angabe die Voreinstellung Anthrazit");
        Pruefe.Gleich(0, alt.Funkfeld.Durchsichtigkeit, "ohne Angabe deckend");
        Pruefe.Falsch(alt.Funkfeld.SchriftDeckend, "ohne Angabe das ganze Fenster");
        Pruefe.Gleich(0, alt.Funkfeld.Fensterorte.Count, "noch kein Platz je Ansicht");
        Pruefe.Gleich(new FensterOrt(525, 70), alt.Funkfeld.OrtFuer(FunkfeldDarstellung.Raster, 5, 1.75), "nur seine Lage, mit 175 % in Bildpunkte umgesetzt");

        // Dateien vom 29. und 30.09.2026 mit der alten Wahl "ganzesFenster": false war die alte Voreinstellung, keine Wahl, und
        // Sarevoc hat genau diese Wirkung verworfen; true ist der neue Hauptweg. Beide werden zum ganzen Fenster, die Stellung bleibt.
        foreach (var wahl in new[] { "false", "true" })
        {
            File.WriteAllText(s.Pfad, "{ \"funkfeld\": { \"durchsichtigkeit\": 70, \"ganzesFenster\": " + wahl + " } }");
            var w = s.Laden();
            Pruefe.Gleich(null, s.LetzterFehler, "ganzesFenster " + wahl + ": lesbar");
            Pruefe.Falsch(w.Funkfeld.SchriftDeckend, "ganzesFenster " + wahl + ": das ganze Fenster");
            Pruefe.Gleich(70, w.Funkfeld.Durchsichtigkeit, "ganzesFenster " + wahl + ": die Stellung des Reglers bleibt");
            s.Speichern(w);
            Pruefe.Falsch(File.ReadAllText(s.Pfad).Contains("ganzesFenster", StringComparison.Ordinal), "ganzesFenster wird nicht mehr geschrieben");
        }

        // Datei aus modul/funkfeld: Leiste gewählt, Plätze mit Größen, die das Programm selbst gemerkt hatte.
        File.WriteAllText(s.Pfad, """
            { "funkfeld": { "darstellung": "Leiste",
                            "plaetze": { "Raster-10": { "links": 10, "oben": 20, "breite": 776, "hoehe": 193, "maximiert": false },
                                         "Leiste-10": { "links": 5, "oben": 5, "breite": 580, "hoehe": 115 } } } }
            """);
        var funkfeld1 = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "gemerkte Leiste macht die Datei nicht unlesbar");
        Pruefe.Gleich(FunkfeldDarstellung.Raster, funkfeld1.Funkfeld.Darstellung, "Leiste wird still zu Raster");
        Pruefe.Gleich("Raster-10", string.Join(",", funkfeld1.Funkfeld.Orte!.Keys), "Platz der Leiste fällt weg");
        Pruefe.Gleich(new FensterOrt(18, 35), funkfeld1.Funkfeld.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "alte Größen vergessen, ihre Lage bleibt");

        // Datei vom 30.09.2026 (Paket 20260930_0049): orte in Einheiten, eine davon mit Übergröße vom Riesenfenster.
        File.WriteAllText(s.Pfad, """
            { "funkfeld": { "orte": { "Raster-10": { "links": 80, "oben": 60, "breite": 4404.6, "hoehe": 1250.3 },
                                      "Quer-10": { "links": 1, "oben": 1 }, "Liste-5": { "links": 1, "oben": 1, "breite": -4, "hoehe": 100 }, "Raster-5": null } } }
            """);
        var riese = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "lesbar trotz Unsinn");
        Pruefe.Gleich("Raster-10,Liste-5", string.Join(",", riese.Funkfeld.Orte!.Keys), "nur gültige alte Plätze bleiben");
        Pruefe.Gleich(new FensterOrt(140, 105), riese.Funkfeld.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "die Übergröße kommt nicht zurück, nur die Lage");
        Pruefe.Gleich(new FensterOrt(140, 105), riese.Funkfeld.OrtFuer(FunkfeldDarstellung.Raster, 5, 1.75), "der 5er nimmt die Lage des 10ers");

        // Unsinn in den neuen Plätzen wird begrenzt oder verworfen, ohne die Datei zu verlieren.
        File.WriteAllText(s.Pfad, """
            { "farbstellung": "Lila",
              "funkfeld": { "darstellung": 7, "durchsichtigkeit": 300,
                            "fensterorte": { "Raster-10": { "links": 10, "oben": 10, "breite": 700, "hoehe": 200, "maximiert": true },
                                             "Quer-10": { "links": 1, "oben": 1 },
                                             "Liste-5": { "links": 1, "oben": 1, "breite": -4, "hoehe": 100 },
                                             "Liste-10": { "links": 99999999, "oben": 1 }, "Raster-5": null } } }
            """);
        var falsch = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "lesbar trotz Unsinn");
        Pruefe.Gleich(FunkfeldDarstellung.Raster, falsch.Funkfeld.Darstellung, "unbekannte Ansicht wird Raster");
        Pruefe.Gleich(Farbstellung.Anthrazit, falsch.Farbstellung, "unbekannte Farbstellung wird die Voreinstellung Anthrazit");
        Pruefe.Gleich(FunkfeldEinstellungen.DurchsichtigkeitMax, falsch.Funkfeld.Durchsichtigkeit, "auf 85 % begrenzt");
        Pruefe.Gleich("Raster-10,Liste-5", string.Join(",", falsch.Funkfeld.Fensterorte.Keys), "nur gültige Plätze bleiben");
        Pruefe.Gleich(new FensterOrt(10, 10, 700, 200), falsch.Funkfeld.Fensterorte["Raster-10"], "gezogene Größe bleibt, das Funkfeld ist nie maximiert");
        Pruefe.Gleich(new FensterOrt(1, 1), falsch.Funkfeld.Fensterorte["Liste-5"], "unbrauchbare Größe fällt weg, die Lage bleibt");
        File.WriteAllText(s.Pfad, """{ "farbstellung": 2, "funkfeld": { "durchsichtigkeit": -20, "darstellung": "liste" } }""");
        var zahl = s.Laden();
        Pruefe.Gleich(0, zahl.Funkfeld.Durchsichtigkeit, "unter 0 wird 0");
        Pruefe.Gleich(Farbstellung.Anthrazit, zahl.Farbstellung, "die alte Zahl 2 (P3) wird Anthrazit");
        Pruefe.Gleich(FunkfeldDarstellung.Liste, zahl.Funkfeld.Darstellung, "Name ohne Rücksicht auf Groß- und Kleinschreibung");
        File.WriteAllText(s.Pfad, """{ "farbstellung": 9, "funkfeld": { "darstellung": "1" } }""");
        var neun = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "lesbar");
        Pruefe.Gleich(Farbstellung.Anthrazit, neun.Farbstellung, "unbekannte Zahl wird die Voreinstellung");
        Pruefe.Gleich(FunkfeldDarstellung.Raster, neun.Funkfeld.Darstellung, "Zahl als Text zählt nicht");

        // Seit 19.10.2026 gibt es nur Anthrazit: Jede alte Wahl (P1 bis P3, als Name oder Zahl) wird still Anthrazit, die Datei bleibt lesbar.
        foreach (var altWert in new[] { "\"Heute\"", "\"Weinrot\"", "\"Dunkel\"", "0", "1", "2", "null" })
        {
            File.WriteAllText(s.Pfad, "{ \"farbstellung\": " + altWert + ", \"funkfeld\": { \"durchsichtigkeit\": 40 } }");
            var gelesen = s.Laden();
            Pruefe.Gleich(null, s.LetzterFehler, "alte Farbstellung " + altWert + ": lesbar");
            Pruefe.Gleich(Farbstellung.Anthrazit, gelesen.Farbstellung, "alte Farbstellung " + altWert + " wird Anthrazit");
            Pruefe.Gleich(40, gelesen.Funkfeld.Durchsichtigkeit, "alte Farbstellung " + altWert + ": der Rest der Datei gilt");
        }
        Pruefe.Gleich(Farbstellung.Anthrazit, new ClientEinstellungen().Farbstellung, "neue Einstellungen: Anthrazit");
        s.Speichern(new ClientEinstellungen());
        Pruefe.Wahr(File.ReadAllText(s.Pfad).Contains("\"farbstellung\": \"Anthrazit\"", StringComparison.Ordinal), "geschrieben wird Anthrazit");
        Pruefe.Gleich(Farbstellung.Anthrazit, s.Laden().Farbstellung, "und so wieder gelesen");
    }

    [Test]
    public static void Funkfeld_Platz_JeAnsichtUndFeldzahl()
    {
        var f = new FunkfeldEinstellungen();
        Pruefe.Gleich("Raster-10", FunkfeldEinstellungen.Schluessel(FunkfeldDarstellung.Raster, 10), "Schlüssel");
        Pruefe.Gleich("Liste-5", FunkfeldEinstellungen.Schluessel(FunkfeldDarstellung.Liste, 5), "Schlüssel 5er");
        Pruefe.Gleich(null, f.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "ohne alles kein Anhalt");

        f.Merken(FunkfeldDarstellung.Raster, 10, new FensterOrt(175, 88, 776, 193, Maximiert: true), groesseGezogen: false);
        Pruefe.Gleich(new FensterOrt(175, 88), f.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "nur verschoben: nur die Lage");
        Pruefe.Gleich(new FensterOrt(175, 88), f.OrtFuer(FunkfeldDarstellung.Raster, 5, 1.75), "5er kommt an dieselbe Stelle, ohne Größe");
        Pruefe.Gleich(null, f.OrtFuer(FunkfeldDarstellung.Liste, 10, 1.75), "andere Ansicht: eigener Platz");

        f.Merken(FunkfeldDarstellung.Raster, 10, new FensterOrt(175, 88, 700, 210, Maximiert: true), groesseGezogen: true);
        Pruefe.Gleich(new FensterOrt(175, 88, 700, 210), f.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "gezogen: mit Größe, nie maximiert");
        f.Merken(FunkfeldDarstellung.Raster, 5, new FensterOrt(525, 158, 776, 111), groesseGezogen: false);
        Pruefe.Gleich(new FensterOrt(525, 158, 700, 210), f.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "Lage gilt für beide Feldzahlen, Größe je Feldzahl");
        Pruefe.Gleich(new FensterOrt(525, 158), f.OrtFuer(FunkfeldDarstellung.Raster, 5, 1.75), "der 5er hat keine Größe");
        f.Merken(FunkfeldDarstellung.Raster, 10, new FensterOrt(600, 158, 700, 210), groesseGezogen: false);
        Pruefe.Gleich(new FensterOrt(600, 158, 700, 210), f.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "nur verschoben: die gezogene Größe bleibt");
        f.Merken(FunkfeldDarstellung.Liste, 10, new FensterOrt(2905, 175, 240, 313), groesseGezogen: false);
        Pruefe.Gleich(new FensterOrt(600, 158, 700, 210), f.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "Liste lässt das Raster, wo es ist");
        f.Merken(FunkfeldDarstellung.Liste, 5, new FensterOrt(5_000_000, 0, 10, 10), groesseGezogen: true);
        Pruefe.Gleich(new FensterOrt(2905, 175), f.OrtFuer(FunkfeldDarstellung.Liste, 5, 1.75), "Unsinn wird nicht gemerkt, der 5er nimmt die Lage des 10ers");

        Pruefe.Wahr(f.GroessenVergessen(), "Größe nach Inhalt: es gab eine gezogene Größe");
        Pruefe.Gleich(new FensterOrt(600, 158), f.OrtFuer(FunkfeldDarstellung.Raster, 10, 1.75), "vergessen, die Lage bleibt");
        Pruefe.Falsch(f.GroessenVergessen(), "ein zweites Mal: nichts zu tun");

        var alt = new FunkfeldEinstellungen { Fenster = new FensterStand(20, 30, 1640, 226, Maximiert: true) };
        Pruefe.Gleich(new FensterOrt(35, 53), alt.OrtFuer(FunkfeldDarstellung.Liste, 5, 1.75), "ohne eigenen Platz die Lage des alten Funkfelds, in Bildpunkten, nie maximiert");
        Pruefe.Gleich(new FensterOrt(20, 30), alt.OrtFuer(FunkfeldDarstellung.Liste, 5, 1.0), "bei 100 % dieselben Zahlen");
    }

    [Test]
    public static void Kachelraster_NatuerlicheBreiteUndRundung()
    {
        Pruefe.Gleich(766.0, Kachelraster.Breite(5, 150, 4), "5 Kacheln Raster");
        Pruefe.Gleich(5, Kachelraster.Passt(766, 150, 4), "genau 5");
        Pruefe.Gleich(5, Kachelraster.Passt(765.7, 150, 4), "gerundet auf Bildpunkte bei 175 % bleiben es 5, nicht 4");
        Pruefe.Gleich(4, Kachelraster.Passt(700, 150, 4), "schmaler: 4");
        Pruefe.Gleich(10, Kachelraster.Passt(1600, 150, 4), "breit: 10 in einer Reihe");
        Pruefe.Gleich(1, Kachelraster.Passt(0, 226, 2), "mindestens eine Spalte");
        Pruefe.Gleich(1, Kachelraster.Passt(232, 232, 2), "Liste: eine Spalte");
        Pruefe.Gleich(5, Kachelraster.Spalten(10, Kachelraster.Passt(Kachelraster.Breite(5, 150, 4), 150, 4)), "Raster: 2 Reihen zu 5");
    }

    [Test]
    public static void Netze_LautstaerkeUndAusloesung_WieAufDerKachel()
    {
        using var a = new Aufbau();
        a.Haupt(out var f, out _, out _, out var netze);
        var zeile = netze.Felder.Single(z => z.Netz == "Crew");
        var regel = zeile.Regelung!;
        var kachel = f.Kacheln.Single(k => k.Name == "Crew");
        var kachelGemeldet = Mitschreiben(kachel);
        var regelGemeldet = Mitschreiben(regel);

        regel.LautstaerkeDb = -6.3;
        Pruefe.Gleich(-6.5, regel.LautstaerkeDb, "auf 0,5 dB gerundet");
        Pruefe.Gleich(-6.5, kachel.LautstaerkeDb, "dieselbe Lautstärke auf der Kachel");
        Pruefe.Wahr(kachelGemeldet.Contains(nameof(kachel.LautstaerkeDb)), "die Kachel zieht nach");
        Pruefe.Gleich("-6,5 dB", regel.LautstaerkeText, "Anzeige");
        Pruefe.Gleich(-6.5f, a.Ablage.Aktuell.NetzOderNull(4)?.LautstaerkeDb, "je Netz gemerkt");

        Pruefe.Wahr(regel.Sprachaktivierung && a.Maschine.Sprachaktivierung, "Crew steht auf VA, die Maschine auch");
        regel.Sprachaktivierung = false;
        Pruefe.Falsch(kachel.Sprachaktivierung, "Taste auch auf der Kachel");
        Pruefe.Falsch(a.Maschine.Sprachaktivierung, "die Maschine folgt (A10)");
        Pruefe.Gleich("Taste", regel.AusloesungText, "Text");

        regelGemeldet.Clear();
        kachel.LautstaerkeDb = 3;
        kachel.Sprachaktivierung = true;
        Pruefe.Gleich(3.0, regel.LautstaerkeDb, "von der Kachel in den Bereich Netze");
        Pruefe.Wahr(regelGemeldet.Contains(nameof(regel.LautstaerkeDb)) && regelGemeldet.Contains(nameof(regel.Sprachaktivierung)), "der Bereich zieht nach");

        var durchsagen = netze.Felder.Single(z => z.Netz == "Durchsagen").Regelung!;
        Pruefe.Falsch(durchsagen.KannSprachaktivierung, "nur Taste: gesperrt");
        durchsagen.Sprachaktivierung = true;
        Pruefe.Falsch(durchsagen.Sprachaktivierung, "lässt sich nicht auf VA stellen");
        Pruefe.Gleich("Global", netze.GlobalRegelung?.Netz, "Global mit Lautstärke");
        Pruefe.Falsch(netze.GlobalRegelung!.KannSprachaktivierung, "Global nur Taste (A20)");

        // Im 5er-Feld fehlen Felder 5 bis 9 als Kacheln; im Bereich Netze bleiben sie regelbar.
        f.Ansicht = 5;
        var ausweich = netze.Felder.Single(z => z.Netz == "Reserve 2").Regelung!;
        ausweich.LautstaerkeDb = -12;
        Pruefe.Gleich(-12f, a.Ablage.Aktuell.NetzOderNull(7)?.LautstaerkeDb, "Feld ohne Kachel gemerkt");

        var ohne = a.Netze();
        Pruefe.Wahr(ohne.Felder.All(z => z.Regelung is null) && ohne.GlobalRegelung is null, "ohne Funkfeld keine Regler");
    }
}
