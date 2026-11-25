// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Maschine;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Funkfeld (A17, A18, A23, A24): 5er/10er-Ansicht mit Global rechts außen, Zustände mit Text, Sendewahl, Aus-Schalter,
/// VA/Taste je Netz, Lautstärke, Livepegel, Threadwechsel.
/// </summary>
internal static class OberflaecheFunkfeldTests
{
    private static KachelViewModel Kachel(FunkfeldViewModel f, string name) => f.Kacheln.Single(k => k.Name == name);

    [Test]
    public static void Funkfeld_Zehner_NeunFelderUndGlobalRechtsAussen()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        Pruefe.Gleich(10, f.Kacheln.Count, "10 Kacheln");
        Pruefe.Wahr(f.Kacheln[^1].IstGlobal, "Global ist die letzte (rechts außen)");
        Pruefe.Gleich(1, f.Kacheln.Count(k => k.IstGlobal), "genau ein Global");
        Pruefe.Wahr(f.Kacheln.Take(9).Select(k => k.Platz).SequenceEqual(Enumerable.Range(0, 9)), "Plätze 0-8 = Feldnummern");
        Pruefe.Gleich("0", f.Kacheln[^1].Taste, "Global auf Taste 0");
        Pruefe.Gleich("Kommando", f.Kacheln[0].Name, "Feld 0");
        Pruefe.Gleich("Global", f.Kacheln[^1].Name, "Name aus der NetzListe");
    }

    [Test]
    public static void Funkfeld_Fuenfer_VierFelderUndGlobal_Gemerkt()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        f.AnsichtWechselnCommand.Execute(null);
        Pruefe.Gleich(5, f.Ansicht, "5er");
        Pruefe.Gleich(5, f.Kacheln.Count, "5 Kacheln");
        Pruefe.Wahr(f.Kacheln[^1].IstGlobal && f.Kacheln.Take(4).All(k => !k.IstGlobal), "4 Felder und Global");
        a.Ablage.Sichern();
        Pruefe.Gleich(5, a.Speicher.Laden().Funkfeld.Ansicht, "gespeichert");
        f.IstZehner = true;
        Pruefe.Gleich(10, f.Kacheln.Count, "zurück auf 10");
    }

    [Test]
    public static void Funkfeld_Zustaende_MitTextUndSprecher()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        var crew = Kachel(f, "Crew");
        Pruefe.Gleich(KachelZustand.Senden, crew.Zustand, "Crew in der Sendeauswahl");
        Pruefe.Gleich("SENDET", crew.ZustandText, "Text SENDET");
        Pruefe.Gleich("Kobold", crew.Sprecher, "Sprecher live");
        var command = Kachel(f, "Kommando");
        Pruefe.Gleich(KachelZustand.Hoeren, command.Zustand, "Kommando hört");
        Pruefe.Gleich("HÖRT", command.ZustandText, "Text HÖRT");
        Pruefe.Gleich("Falke", command.Sprecher, "Falke spricht in Kommando");
        var jaeger = Kachel(f, "Jäger");
        Pruefe.Gleich(KachelZustand.Aus, jaeger.Zustand, "Jäger aus (A17)");
        Pruefe.Gleich("AUS", jaeger.ZustandText, "Text AUS");
        Pruefe.Wahr(!jaeger.SprecherAktiv, "Ruhe");
        var global = f.Kacheln[^1];
        Pruefe.Gleich(KachelZustand.Hoeren, global.Zustand, "Global hört");
        Pruefe.Falsch(global.KannAusschalten, "Global nicht abschaltbar (A23)");
        Pruefe.Wahr(global.NurTaste, "Global nur mit Taste");
        Pruefe.Gleich("Crew", crew.Name, "Name ohne Sortiernummer (A16)");
        Pruefe.Gleich("Senden: Crew", f.Kopfzeile, "Kopfzeile");

        Pruefe.Gleich(KachelZustand.Leer, KachelViewModel.Bestimmen(false, true, true), "ohne Netz leer");
        Pruefe.Gleich(KachelZustand.Senden, KachelViewModel.Bestimmen(true, false, true), "Senden vor Hören");
        Pruefe.Gleich(KachelZustand.Hoeren, KachelViewModel.Bestimmen(true, true, false), "Hören");
        Pruefe.Gleich(KachelZustand.Aus, KachelViewModel.Bestimmen(true, false, false), "Aus");
        Pruefe.Gleich("FREI", KachelViewModel.Text(KachelZustand.Leer), "leer");
    }

    [Test]
    public static async Task Funkfeld_SendenWaehlen_ErsetztAuswahl()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        await Kachel(f, "Kommando").SendenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Senden [0]"), "Anfrage: " + string.Join(" | ", a.Dienst.Aufrufe));
        Pruefe.Gleich(KachelZustand.Senden, Kachel(f, "Kommando").Zustand, "Kommando sendet");
        Pruefe.Gleich(KachelZustand.Hoeren, Kachel(f, "Crew").Zustand, "Crew hört nur noch");
        Pruefe.Gleich(null, f.Hinweis, "kein Hinweis");
    }

    [Test]
    public static async Task Funkfeld_StrgKlick_NurMitRechtMehrfach()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        await Kachel(f, "Kommando").SendenHinzufuegenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Senden [0,2]"), "mit Recht MEHRFACH_SENDEN dazu: " + string.Join(" | ", a.Dienst.Aufrufe));
        await Kachel(f, "Crew").SendenHinzufuegenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Senden [0]"), "zweites Strg+Klick nimmt heraus");

        using var b = new Aufbau(FunkDienstAttrappe.Beispiel() with { Konto = new KontoInfo(9, "Neu", "neu", Stufe.Funker, []) });
        var g = b.Funkfeld();
        await Kachel(g, "Kommando").SendenHinzufuegenCommand.ExecuteAsync(null);
        Pruefe.Wahr(b.Dienst.Aufrufe.Contains("Senden [0]"), "ohne Recht ersetzt: " + string.Join(" | ", b.Dienst.Aufrufe));
        Pruefe.Wahr(g.Hinweis?.Contains("Recht", StringComparison.Ordinal) == true, "Hinweis auf das Recht");
    }

    [Test]
    public static async Task Funkfeld_NichtErlaubt_KeineAnfrage()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        await Kachel(f, "Durchsagen").SendenCommand.ExecuteAsync(null);
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("Senden", StringComparison.Ordinal)), "keine Anfrage");
        Pruefe.Wahr(f.Hinweis?.Contains("Durchsagen", StringComparison.Ordinal) == true, "Hinweis nennt das Netz");

        a.Dienst.NaechsterFehler = "Dafür fehlt das Recht.";
        await Kachel(f, "Brücke").SendenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Dafür fehlt das Recht.", f.Hinweis, "Ablehnung des Servers wird gezeigt");
        Pruefe.Gleich(KachelZustand.Senden, Kachel(f, "Crew").Zustand, "Auswahl unverändert");
    }

    [Test]
    public static async Task Funkfeld_Tasten_GlobalUndWeiterschalten()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        await f.TasteAsync(0);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Senden [] +Global"), "0 wählt Global: " + string.Join(" | ", a.Dienst.Aufrufe));
        Pruefe.Gleich(KachelZustand.Senden, f.Kacheln[^1].Zustand, "Global sendet");
        await f.TasteAsync(3);
        Pruefe.Gleich(KachelZustand.Senden, Kachel(f, "Crew").Zustand, "3 wählt Feld 3 (Crew)");

        // Kandidaten mit Senderecht: Kommando (0), Crew (2), Brücke (3), Global.
        await f.WeiterschaltenAsync(+1);
        Pruefe.Gleich(KachelZustand.Senden, Kachel(f, "Brücke").Zustand, "+ von Crew auf Brücke");
        await f.WeiterschaltenAsync(+1);
        Pruefe.Gleich(KachelZustand.Senden, f.Kacheln[^1].Zustand, "+ auf Global");
        await f.WeiterschaltenAsync(+1);
        Pruefe.Gleich(KachelZustand.Senden, Kachel(f, "Kommando").Zustand, "+ rundum auf Kommando");
        await f.WeiterschaltenAsync(-1);
        Pruefe.Gleich(KachelZustand.Senden, f.Kacheln[^1].Zustand, "- zurück auf Global");
    }

    [Test]
    public static async Task Funkfeld_AusSchalter_SchaltetHoeren()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        var jaeger = Kachel(f, "Jäger");
        Pruefe.Gleich("An", jaeger.AusSchalterText, "aus: Knopf heißt An");
        await jaeger.HoerenUmschaltenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Hoeren 6 an"), "HoerenSetzen");
        Pruefe.Gleich(KachelZustand.Hoeren, jaeger.Zustand, "hört wieder");
        Pruefe.Gleich("Aus", jaeger.AusSchalterText, "Knopf heißt Aus");
        await f.HoerenSetzenAsync(f.Kacheln[^1], false);
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("Hoeren 1", StringComparison.Ordinal)), "Global wird nie abgeschaltet");
    }

    [Test]
    public static void Funkfeld_Ausloesung_JeNetzUndAnDerMaschine()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        var crew = Kachel(f, "Crew");
        Pruefe.Wahr(crew.Sprachaktivierung, "Crew: Standard des Netzes ist VA");
        Pruefe.Wahr(a.Maschine.Sprachaktivierung, "Sendenetz Crew auf VA: Maschine mit Sprachaktivierung");
        crew.AusloesungUmschaltenCommand.Execute(null);
        Pruefe.Falsch(crew.Sprachaktivierung, "jetzt Taste");
        Pruefe.Gleich("Taste", crew.AusloesungText, "Text");
        Pruefe.Falsch(a.Maschine.Sprachaktivierung, "Maschine nur Sprechtaste");
        Pruefe.Gleich(Ausloesung.Sprechtaste, a.Ablage.Aktuell.NetzOderNull(4)?.Ausloesung, "je Netz gemerkt (A10)");
        var durchsagen = Kachel(f, "Durchsagen");
        durchsagen.Sprachaktivierung = true;
        Pruefe.Falsch(durchsagen.Sprachaktivierung, "nurTaste lässt sich nicht auf VA stellen");
        Pruefe.Falsch(durchsagen.KannSprachaktivierung, "Umschalter gesperrt");

        // Neues Funkfeld auf denselben Einstellungen: die Wahl bleibt.
        var g = a.Funkfeld();
        Pruefe.Falsch(Kachel(g, "Crew").Sprachaktivierung, "gemerkt");
    }

    [Test]
    public static void Funkfeld_Lautstaerke_JeNetzGerundetUndBegrenzt()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        var crew = Kachel(f, "Crew");
        crew.LautstaerkeDb = -3.3;
        Pruefe.Gleich(-3.5, crew.LautstaerkeDb, "auf 0,5 dB gerundet");
        Pruefe.Gleich(-3.5f, a.Ablage.Aktuell.NetzOderNull(4)?.LautstaerkeDb, "je Netz gespeichert");
        crew.LautstaerkeDb = 40;
        Pruefe.Gleich((double)NetzEinstellung.LautstaerkeMaxDb, crew.LautstaerkeDb, "oben begrenzt");
        var leer = new Aufbau(FunkLage.Leer);
        using (leer)
        {
            var g = leer.Funkfeld();
            g.Kacheln[0].LautstaerkeDb = -6;
            Pruefe.Gleich(0, leer.Ablage.Aktuell.Netze.Count, "leeres Feld merkt nichts");
            Pruefe.Wahr(g.Kacheln.All(k => k.IstLeer), "ohne Verbindung alles leer");
            Pruefe.Gleich("nicht verbunden", g.Kopfzeile, "Kopfzeile");
        }
    }

    [Test]
    public static void Funkfeld_Takt_EigeneSendungUndEmpfangspegel()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        a.Maschine.Melden(new PegelMeldung(10, -30, -60, -50, 0.9f, true));
        f.Takt();
        var crew = Kachel(f, "Crew");
        Pruefe.Wahr(crew.EigeneSendung, "eigene Sendung im Sendenetz");
        Pruefe.Wahr(Math.Abs(crew.PegelAnteil - 0.5) < 1e-9, "eigener Pegel -30 dBFS = halb");
        Pruefe.Falsch(Kachel(f, "Kommando").EigeneSendung, "nicht in anderen Netzen");

        a.Maschine.Melden(new PegelMeldung(30, -70, -60, -50, 0.1f, false));
        a.Maschine.EmpfangsPegelMelden(2, -15);
        f.Takt();
        Pruefe.Falsch(crew.EigeneSendung, "Sendung vorbei");
        Pruefe.Wahr(Math.Abs(Kachel(f, "Kommando").PegelAnteil - 0.75) < 1e-6, "Empfangspegel -15 dBFS in Kommando");
        a.Uhr += 1000;
        f.Takt();
        Pruefe.Wahr(Math.Abs(Kachel(f, "Kommando").PegelAnteil - 0.5) < 1e-9, "veraltet: nur noch 'spricht'");
        Pruefe.Gleich(0.0, Kachel(f, "Brücke").PegelAnteil, "Ruhe: leer");
        Pruefe.Gleich(0.0, FunkfeldViewModel.Anteil(double.NaN), "NaN");
        Pruefe.Gleich(1.0, FunkfeldViewModel.Anteil(6), "über 0 dBFS voll");
    }

    /// <summary>
    /// Leistung neben Star Citizen: Der Bildtakt läuft mit 30 je Sekunde, die Kacheln bekommen den Pegel aber höchstens 15
    /// mal je Sekunde und nur in Achtelstufen. Jede Änderung einer Kachel überträgt in einem geschichteten Fenster das
    /// ganze Bild. Simulierte Uhr, wechselnder Pegel in jedem Takt wie bei Sprache.
    /// </summary>
    [Test]
    public static void Funkfeld_Takt_Hoechstens15JeSekundeInStufen()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        var command = Kachel(f, "Kommando");
        var werte = new List<double>();
        command.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(KachelViewModel.PegelAnteil)) werte.Add(command.PegelAnteil);
        };
        var zufall = new Random(7);
        const int takte = 90;
        for (var i = 0; i < takte; i++)
        {
            a.Maschine.EmpfangsPegelMelden(2, -50 + (float)(zufall.NextDouble() * 48));
            f.Takt();
            a.Uhr += 33;
        }
        var sekunden = takte * 33 / 1000.0;
        Console.WriteLine(string.Create(CultureInfo.GetCultureInfo("de-DE"),
            $"MASS Funkfeld-Takt: {werte.Count} Pegeländerungen in {sekunden:0.0} s bei {takte} Takten ({werte.Count / sekunden:0.0} je Sekunde)"));
        Pruefe.Wahr(werte.Count <= Math.Ceiling(sekunden * 15), $"höchstens 15 je Sekunde, waren {werte.Count} in {sekunden} s");
        Pruefe.Wahr(werte.Count >= 10, "der Pegel lebt noch: " + werte.Count);
        Pruefe.Wahr(werte.All(w => Math.Abs((w * FunkfeldViewModel.PegelStufen) - Math.Round(w * FunkfeldViewModel.PegelStufen)) < 1e-9),
            "nur ganze Achtel: " + string.Join(" ", werte.Take(8)));
        Pruefe.Gleich(0.125, FunkfeldViewModel.Stufe(0.1), "0,1 wird ein Achtel");
        Pruefe.Gleich(0.0, FunkfeldViewModel.Stufe(0.06), "fast leer wird leer");
        Pruefe.Gleich(1.0, FunkfeldViewModel.Stufe(0.97), "fast voll wird voll");
        Pruefe.Gleich(0.0, FunkfeldViewModel.Stufe(double.NaN), "NaN");

        // Die eigene Sendung erscheint sofort, auch mitten im Abstand.
        a.Uhr += 5;
        a.Maschine.Melden(new PegelMeldung(a.Uhr, -30, -60, -50, 0.9f, true));
        f.Takt();
        Pruefe.Wahr(Kachel(f, "Crew").EigeneSendung, "eigene Sendung ohne Warten");
        a.Uhr += 10;
        a.Maschine.Melden(new PegelMeldung(a.Uhr, -70, -60, -50, 0.1f, false));
        f.Takt();
        Pruefe.Falsch(Kachel(f, "Crew").EigeneSendung, "und endet ohne Warten");

        // Gegenprobe: Mit jedem Takt fällig (Uhr springt um den Abstand) kämen deutlich mehr Änderungen.
        werte.Clear();
        for (var i = 0; i < takte; i++)
        {
            a.Maschine.EmpfangsPegelMelden(2, -50 + (float)(zufall.NextDouble() * 48));
            a.Uhr += FunkfeldViewModel.PegelAbstandMs;
            f.Takt();
        }
        Pruefe.Wahr(werte.Count > takte / 2, "Gegenprobe: ohne Drosselung ändert fast jeder Takt die Kachel, waren " + werte.Count);
        f.Dispose();

        // Die Obergrenze ist erreichbar: Bei einem genauen Bildtakt von 33 ms (Testuhr) wirkt jeder zweite Takt, 15 je Sekunde.
        // Mit 67 ms war es nur jeder dritte, also 10 je Sekunde. Der Pegel wechselt alle zwei Takte, so ändert jeder wirksame
        // Takt die Kachel. Der echte Takt ist gröber (gemessen etwa 47 ms, siehe FunkfeldViewModel.PegelAbstandMs).
        Pruefe.Wahr(FunkfeldViewModel.PegelAbstandMs <= 2 * 33 && FunkfeldViewModel.PegelAbstandMs > 33, "Abstand zwischen einem und zwei Bildtakten");
        using var b = new Aufbau();
        var g = b.Funkfeld();
        var command2 = Kachel(g, "Kommando");
        var aenderungen = 0;
        command2.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(KachelViewModel.PegelAnteil)) aenderungen++;
        };
        for (var i = 0; i < 30; i++)
        {
            b.Maschine.EmpfangsPegelMelden(2, (i / 2) % 2 == 0 ? -50 : -6);
            g.Takt();
            b.Uhr += 33;
        }
        Pruefe.Gleich(15, aenderungen, "30 Bildtakte zu 33 ms (knapp 1 s): 15 Pegeländerungen");
        g.Dispose();
    }

    [Test]
    public static async Task Funkfeld_LageAusFremdemThread_UeberDenTakt()
    {
        var takt = new WarteschlangenTakt();
        using var a = new Aufbau(takt: takt);
        var f = a.Funkfeld();
        await Task.Run(() => a.Dienst.LageSetzen(l => l with { Sprecher = [new SprecherInfo(5, "Kranich", [5], false)] }));
        Pruefe.Gleich("Kobold", Kachel(f, "Crew").Sprecher, "vor dem Takt unverändert");
        Pruefe.Wahr(takt.Abarbeiten() >= 1, "Änderung liegt auf dem Oberflächen-Takt");
        Pruefe.Gleich("", Kachel(f, "Crew").Sprecher, "Crew still");
        Pruefe.Gleich("Kranich", Kachel(f, "Brücke").Sprecher, "Kranich in der Brücke");
        f.Dispose();
        a.Dienst.LageSetzen(l => l);
        Pruefe.Gleich(0, takt.Anzahl, "nach Dispose abgemeldet");
    }

    [Test]
    public static void Kachelraster_SpaltenUndPlatzVonGlobal()
    {
        Pruefe.Gleich(10, Kachelraster.Spalten(10, 12), "breit: eine Zeile");
        Pruefe.Gleich(5, Kachelraster.Spalten(10, 9), "Platz für 9: zwei Zeilen zu 5");
        Pruefe.Gleich(4, Kachelraster.Spalten(10, 4), "Platz für 4: drei Zeilen zu 4");
        Pruefe.Gleich(3, Kachelraster.Spalten(5, 4), "5 Kacheln bei Platz für 4: 3+2 statt 4+1");
        Pruefe.Gleich(1, Kachelraster.Spalten(5, 0), "schmal: eine Spalte");
        Pruefe.Gleich((1, 4), Kachelraster.Platz(9, 10, 5), "Global unten rechts");
        Pruefe.Gleich((0, 9), Kachelraster.Platz(9, 10, 10), "Global in einer Zeile rechts außen");
        Pruefe.Gleich((2, 3), Kachelraster.Platz(9, 10, 4), "Global unten rechts auch mit Lücke");
        Pruefe.Gleich((1, 2), Kachelraster.Platz(4, 5, 3), "5er: Global unten rechts");
    }
}
