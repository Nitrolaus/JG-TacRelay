// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Klang.Maschine;

namespace Funk.Tests.Oberflaeche;

/// <summary>Einstellungen als JSON: Rundlauf, Standardwerte, beschädigte Datei, Grenzen, Umgebungsvariable, gebündeltes Schreiben, Fensterplatz.</summary>
internal static class OberflaecheEinstellungenTests
{
    [Test]
    public static void Einstellungen_Rundlauf_BehaeltAlleWerte()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        var e = new ClientEinstellungen();
        e.Gate.Uebernehmen(new GateEinstellung(GateModus.Lernend, -41.5f, 12f, -58f, -28f, 3f, 9f, 0.65f, -63f));
        e.Funkfeld.Ansicht = 5;
        e.Funkfeld.BeimStartZeigen = false;
        e.Funkfeld.Fenster = new FensterStand(100, 50, 820, 230);
        e.HauptfensterOrt = new FensterOrt(18, 35, 1200, 800, Maximiert: true);
        e.NetzFuer(4).Ausloesung = Ausloesung.Sprechtaste;
        e.NetzFuer(4).LautstaerkeDb = -6f;
        e.Audio.EingangId = "ein-2";
        e.Audio.Echounterdrueckung = false;
        e.Anmeldung.ScName = "Mustermann";
        e.Anmeldung.DiscordName = "Mustermann/Max";
        e.Anmeldung.Bestaetigt = true;
        e.Anzeige.ZeichenImFunkfeld = true;
        e.Bereich = "audio";
        s.Speichern(e);

        var g = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "kein Fehler");
        Pruefe.Gleich(new GateEinstellung(GateModus.Lernend, -41.5f, 12f, -58f, -28f, 3f, 9f, 0.65f, -63f), g.Gate.AlsEinstellung(), "Gate samt Grenzlinie");
        Pruefe.Gleich(5, g.Funkfeld.Ansicht, "Ansicht");
        Pruefe.Falsch(g.Funkfeld.BeimStartZeigen, "Funkfeld beim Start");
        Pruefe.Gleich(new FensterStand(100, 50, 820, 230), g.Funkfeld.Fenster, "Funkfeld-Fenster");
        Pruefe.Gleich(new FensterOrt(18, 35, 1200, 800, true), g.HauptfensterOrt, "Hauptfenster");
        Pruefe.Gleich(null, g.Hauptfenster, "kein alter Stand");
        Pruefe.Falsch(File.ReadAllText(s.Pfad).Contains("\"hauptfenster\":", StringComparison.Ordinal), "das alte Feld wird nicht geschrieben, solange es leer ist");
        Pruefe.Gleich(Ausloesung.Sprechtaste, g.NetzOderNull(4)?.Ausloesung, "Auslösung Netz 4");
        Pruefe.Gleich(-6f, g.NetzOderNull(4)?.LautstaerkeDb, "Lautstärke Netz 4");
        Pruefe.Gleich("ein-2", g.Audio.EingangId, "Eingang");
        Pruefe.Falsch(g.Audio.Echounterdrueckung, "Echo aus");
        Pruefe.Wahr(g.Anmeldung is { ScName: "Mustermann", DiscordName: "Mustermann/Max", Bestaetigt: true }, "Namen für den Antrag, bestätigt");
        Pruefe.Wahr(g.Anzeige is { ZeichenNebenNamen: true, ZeichenImFunkfeld: true }, "Zeichen neben Namen und im Funkfeld");
        Pruefe.Gleich("audio", g.Bereich, "Bereich");
        Pruefe.Falsch(File.Exists(s.Pfad + ".neu"), "keine Zwischendatei bleibt liegen");
        Pruefe.Wahr(File.ReadAllText(s.Pfad).Contains("\"Lernend\"", StringComparison.Ordinal), "Aufzählungen als Text");
    }

    /// <summary>
    /// ANMELDUNG-SICHERHEIT P1: Serverprofil und Profilschlüssel kommen nicht mehr aus der Einstellungsdatei. Eine ältere Datei
    /// mit profilDatei und profilSchluessel lädt ohne Fehler, die Felder werden übergangen und beim nächsten Speichern nicht
    /// mehr geschrieben. Ungültige Namen fallen weg und nehmen die Bestätigung mit.
    /// </summary>
    [Test]
    public static void Einstellungen_ProfilfelderAlterDateiWerdenUebergangen()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        File.WriteAllText(s.Pfad, "{ \"verbindung\": { \"profilDatei\": \"C:\\\\Fremd\\\\profil.json\", \"profilSchluessel\": [\"MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE\"], "
            + "\"adresse\": \"192.168.1.250\" }, \"anmeldung\": { \"scName\": \"Sa\\u200Brevoc\", \"discordName\": \"Mustermann/Max\", \"bestaetigt\": true } }");
        var e = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "alte Datei lädt ohne Fehler");
        Pruefe.Gleich("192.168.1.250", e.Verbindung.Adresse, "die Adresse bleibt");
        Pruefe.Wahr(e.Anmeldung is { ScName: null, DiscordName: "Mustermann/Max", Bestaetigt: false }, "unsichtbares Zeichen im SC-Namen: weg, und nicht mehr bestätigt");
        s.Speichern(e);
        var text = File.ReadAllText(s.Pfad);
        Pruefe.Falsch(text.Contains("profil", StringComparison.OrdinalIgnoreCase), "Profilfelder werden nicht mehr geschrieben");
        Pruefe.Falsch(typeof(VerbindungsEinstellungen).GetProperties().Any(p => p.Name.Contains("Profil", StringComparison.Ordinal)),
            "VerbindungsEinstellungen kennt kein Profil mehr");
    }

    [Test]
    public static void Einstellungen_OhneDatei_Standardwerte()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("fehlt.json"));
        var e = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "kein Fehler");
        Pruefe.Gleich(GateModus.Automatisch, e.Gate.Modus, "Standardmodus");
        Pruefe.Gleich(10, e.Funkfeld.Ansicht, "10er-Ansicht");
        Pruefe.Wahr(e.Audio.Rauschunterdrueckung && e.Audio.Echounterdrueckung, "Rausch- und Echounterdrückung an (A11, A14)");
        Pruefe.Falsch(File.Exists(s.Pfad), "Laden legt nichts an");
    }

    [Test]
    public static void Einstellungen_KaputteDatei_WirdBeiseitegelegt()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        File.WriteAllText(s.Pfad, "{ \"gate\": { \"modus\": ");
        var e = s.Laden();
        Pruefe.Wahr(s.LetzterFehler is not null, "Fehler gemeldet");
        Pruefe.Gleich(GateModus.Automatisch, e.Gate.Modus, "Standardwerte");
        Pruefe.Falsch(File.Exists(s.Pfad), "kaputte Datei nicht mehr am alten Platz");
        Pruefe.Gleich(1, Directory.GetFiles(o.Pfad, "e.json.defekt-*").Length, "als .defekt beiseitegelegt, nicht gelöscht");

        File.WriteAllText(s.Pfad, "{ \"funkfeld\": { \"ansicht\": \"zehn\" } }");
        s.Laden();
        Pruefe.Wahr(s.LetzterFehler is not null, "falscher Typ gilt als unlesbar");
    }

    [Test]
    public static void Einstellungen_Normalisieren_BringtWerteInGrenzen()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        File.WriteAllText(s.Pfad, """
            {
              "gate": { "modus": "Fest", "schwelleDb": -300, "abstandDb": 99, "korridorVonDb": -20, "korridorBisDb": -40,
                        "anstiegSekunden": 0, "rueckkehrSekunden": 1000, "sprachsicherheit": 2 },
              "funkfeld": { "ansicht": 7, "fenster": { "links": 1, "oben": 1, "breite": -5, "hoehe": 10 } },
              "netze": { "4": { "lautstaerkeDb": 50 }, "abc": {}, "0": {}, "65535": {}, "007": {} }
            }
            """);
        var e = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "lesbar");
        Pruefe.Gleich(GateWerte.MinDb, e.Gate.SchwelleDb, "Schwelle unten begrenzt");
        Pruefe.Gleich(GateWerte.AbstandMaxDb, e.Gate.AbstandDb, "Abstand oben begrenzt");
        Pruefe.Wahr(e.Gate.KorridorBisDb - e.Gate.KorridorVonDb >= GateWerte.KorridorMinBreiteDb, "Korridor mindestens 3 dB breit");
        Pruefe.Gleich(-20f, e.Gate.KorridorVonDb, "von bleibt");
        Pruefe.Gleich(GateWerte.ZeitMinSek, e.Gate.AnstiegSekunden, "Anstieg unten begrenzt");
        Pruefe.Gleich(GateWerte.RueckkehrMaxSek, e.Gate.RueckkehrSekunden, "Rückkehr oben begrenzt");
        Pruefe.Gleich(1f, e.Gate.Sprachsicherheit, "Sprachsicherheit höchstens 1");
        Pruefe.Gleich(10, e.Funkfeld.Ansicht, "nur 5 oder 10");
        Pruefe.Gleich(null, e.Funkfeld.Fenster, "Fenster ohne Fläche verworfen");
        Pruefe.Gleich(1, e.Netze.Count, "nur gültige Netz-IDs bleiben");
        Pruefe.Gleich(NetzEinstellung.LautstaerkeMaxDb, e.NetzOderNull(4)!.LautstaerkeDb, "Lautstärke begrenzt");

        var w = new GateWerte { SchwelleDb = float.NaN, AbstandDb = float.PositiveInfinity };
        w.Normalisieren();
        Pruefe.Gleich(-45f, w.SchwelleDb, "NaN wird Standard");
        Pruefe.Gleich(9f, w.AbstandDb, "Unendlich wird Standard");
    }

    [Test]
    public static void Einstellungen_Schwellenbereich_UndLernen()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        // Stand der ersten Lieferung: eigenes Schwellen-Fenster. Das alte Feld wird still übergangen (A26).
        File.WriteAllText(s.Pfad, """
            {
              "schwellenfenster": { "links": 10, "oben": 10, "breite": 900, "hoehe": 700 },
              "schwellenbereich": { "offen": true, "hoehe": 99999 },
              "gate": { "modus": "Automatisch", "lernen": true }
            }
            """);
        var e = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "alte Datei lesbar");
        Pruefe.Wahr(e.Schwellenbereich.Offen, "offen");
        Pruefe.Gleich(SchwellenbereichEinstellungen.MaxHoehe, e.Schwellenbereich.Hoehe, "Höhe begrenzt");
        Pruefe.Falsch(e.Gate.Lernen, "mit Automatik folgt Lernen dem Modus (Automatisch: aus)");

        e.Schwellenbereich.Hoehe = double.NaN;
        e.Gate.Modus = GateModus.Fest;
        e.Gate.Lernen = true;
        e.Normalisieren();
        Pruefe.Gleich(SchwellenbereichEinstellungen.StandardHoehe, e.Schwellenbereich.Hoehe, "NaN wird Standard");
        Pruefe.Wahr(e.Gate.Lernen, "ohne Automatik bleibt die Wahl Lernen gemerkt");
        s.Speichern(e);
        var text = File.ReadAllText(s.Pfad);
        Pruefe.Falsch(text.Contains("schwellenfenster", StringComparison.Ordinal), "altes Feld wird nicht mehr geschrieben");
        Pruefe.Falsch(text.Contains("\"automatik\"", StringComparison.Ordinal), "abgeleitete Werte werden nicht geschrieben");
        Pruefe.Wahr(s.Laden().Gate.Lernen, "Lernen gespeichert");
        Pruefe.Gleich(GateModus.Lernend, GateWerte.ModusAus(automatik: true, lernen: true), "beide an: Lernend");
        Pruefe.Gleich(GateModus.Automatisch, GateWerte.ModusAus(automatik: true, lernen: false), "nur Automatik: Automatisch");
        Pruefe.Gleich(GateModus.Fest, GateWerte.ModusAus(automatik: false, lernen: true), "ohne Automatik: Fest");
    }

    [Test]
    public static void Einstellungsablage_Schreibfehler_ErreichtDieAnzeige()
    {
        using var o = new Testordner();
        // Ein Ordner an der Stelle der Datei: Ersetzen scheitert, wie bei fehlenden Rechten.
        Directory.CreateDirectory(o.Datei("e.json"));
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        using var a = new Einstellungsablage(s, new ClientEinstellungen(), SofortTakt.Instanz, TimeSpan.FromHours(1));
        var vm = new Funk.Client.Modelle.EinstellungenViewModel(a, new Funk.Client.Modelle.FunkfeldViewModel(
            new FunkDienstAttrappe(), new Funk.Client.Klang.KlangMaschinenAttrappe(), new Funk.Client.Klang.PegelVerlauf(), a, SofortTakt.Instanz));
        var gemeldet = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Fehler)) gemeldet++;
        };
        a.Vormerken();
        a.Sichern();
        Pruefe.Wahr(vm.Fehler?.StartsWith("Einstellungen nicht gespeichert", StringComparison.Ordinal) == true, "Fehler sichtbar: " + vm.Fehler);
        Pruefe.Gleich(1, gemeldet, "Anzeige benachrichtigt");
        Directory.Delete(o.Datei("e.json"));
        a.Vormerken();
        a.Sichern();
        Pruefe.Gleich(null, vm.Fehler, "nach erfolgreichem Schreiben weg");
        Pruefe.Gleich(2, gemeldet, "auch das gemeldet");
    }

    [Test]
    public static void Einstellungen_Umgebungsvariable_LegtPfadFest()
    {
        var vorher = Environment.GetEnvironmentVariable(EinstellungsSpeicher.Umgebungsvariable);
        try
        {
            using var o = new Testordner();
            Environment.SetEnvironmentVariable(EinstellungsSpeicher.Umgebungsvariable, o.Datei("eigen.json"));
            Pruefe.Gleich(o.Datei("eigen.json"), EinstellungsSpeicher.StandardPfad(), "Pfad aus der Umgebungsvariable");
            Environment.SetEnvironmentVariable(EinstellungsSpeicher.Umgebungsvariable, null);
            var standard = EinstellungsSpeicher.StandardPfad();
            Pruefe.Wahr(standard.EndsWith(Path.Combine("JG-TacRelay", "client-einstellungen.json"), StringComparison.OrdinalIgnoreCase), "Standard im Benutzerordner der App: " + standard);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EinstellungsSpeicher.Umgebungsvariable, vorher);
        }
    }

    [Test]
    public static void Einstellungsablage_SchreibtGebuendelt()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("e.json"));
        using (var a = new Einstellungsablage(s, s.Laden(), SofortTakt.Instanz, TimeSpan.FromHours(1)))
        {
            a.Sichern();
            Pruefe.Gleich(0, a.Schreibvorgaenge, "ohne Vormerken wird nicht geschrieben");
            a.Aktuell.Funkfeld.Ansicht = 5;
            a.Vormerken();
            a.Vormerken();
            a.Sichern();
            a.Sichern();
            Pruefe.Gleich(1, a.Schreibvorgaenge, "einmal geschrieben");
            Pruefe.Gleich(5, s.Laden().Funkfeld.Ansicht, "Wert steht in der Datei");
        }

        // Der Zeitgeber schreibt nach der Verzögerung von selbst, und zehn Änderungen kurz hintereinander nur einmal.
        using var b = new Einstellungsablage(s, s.Laden(), SofortTakt.Instanz, TimeSpan.FromMilliseconds(150));
        for (var i = 0; i < 10; i++)
        {
            // Merkwert die Adresse: Den Gerätenamen macht das Laden seit 0.6.3 immer neutral.
            b.Aktuell.Verbindung.Adresse = "host" + i;
            b.Vormerken();
        }
        var bis = DateTime.UtcNow.AddSeconds(5);
        while (b.Schreibvorgaenge == 0 && DateTime.UtcNow < bis) Thread.Sleep(20);
        Thread.Sleep(300);
        Pruefe.Gleich(1, b.Schreibvorgaenge, "gebündelt: ein Schreibvorgang");
        Pruefe.Gleich("host9", s.Laden().Verbindung.Adresse, "letzter Stand");
    }

    /// <summary>
    /// Gemerkter Platz gegen die Monitore (A32; früher gegen den ganzen virtuellen Bildschirm, so kam eine Übergröße über zwei
    /// Monitore zurück): sichtbar bleibt genau so, ein fehlender Monitor rückt auf den nächsten, eine Übergröße wird auf den
    /// Arbeitsbereich eines Monitors begrenzt, ein zu kleines Fenster auf die Mindestgröße gebracht.
    /// </summary>
    [Test]
    public static void Fensterplatz_PrueftGegenDieMonitore()
    {
        var b = new Bildschirm(new Pixelrechteck(0, 0, 1920, 1080), new Pixelrechteck(0, 0, 1920, 1040), 1, true);
        var links = new Bildschirm(new Pixelrechteck(-1920, 0, 1920, 1080), new Pixelrechteck(-1920, 0, 1920, 1080), 1, false);
        var min = new Size(340, 170);
        var wunsch = new Size(1120, 780);
        Pruefe.Gleich(new Platz(100, 100, 800, 600, b), Platzierung.Wiederherstellen(new FensterOrt(100, 100, 800, 600), wunsch, [b], min), "sichtbar bleibt");
        Pruefe.Gleich(new Platz(1120, 100, 800, 600, b), Platzierung.Wiederherstellen(new FensterOrt(3000, 100, 800, 600), wunsch, [b], min), "Monitor fehlt: an den Rand des nächsten");
        Pruefe.Gleich(0, Platzierung.Wiederherstellen(new FensterOrt(100, -500, 800, 600), wunsch, [b], min).Oben, "Titelleiste über dem Bildschirm: hereingeholt");
        Pruefe.Gleich(1120, Platzierung.Wiederherstellen(new FensterOrt(1900, 100, 800, 600), wunsch, [b], min).Links, "nur 20 Pixel sichtbar: hereingeholt");
        Pruefe.Gleich(new Size(340, 170), Platzierung.Wiederherstellen(new FensterOrt(100, 100, 10, 10), wunsch, [b], min) is var k ? new Size(k.Breite, k.Hoehe) : default, "zu klein wird vergrößert");
        var breit = Platzierung.Wiederherstellen(new FensterOrt(0, 0, 5000, 600), wunsch, [b, links], min);
        Pruefe.Wahr(breit.Breite == 1920 && breit.Bildschirm == b, "zu breit wird auf den Arbeitsbereich eines Monitors begrenzt, nicht auf beide");
        Pruefe.Gleich(links, Platzierung.Wiederherstellen(new FensterOrt(-1500, 100, 800, 600), wunsch, [b, links], min).Bildschirm, "linker Zweitmonitor");
        Pruefe.Gleich(new Platz(-1500, 100, 800, 600, links), Platzierung.Wiederherstellen(new FensterOrt(-1500, 100, 800, 600), wunsch, [b, links], min), "dort genau so");
    }
}
