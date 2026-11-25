// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Klangstufe C2a, Punkt 5 in der Einstellungsdatei: Schalter "Auto Dauersend-Dämpfung" (ab Werk an) und gewählte Stufe (ab Werk "deutlich
/// leiser"). Umstieg von Dateiversion 2 ohne Umstellen: Eine Datei ohne den Abschnitt bekommt die Werte ab Werk, ältere Clients übergehen ihn.
/// Die Oberfläche selbst baut C2b.
/// </summary>
internal static class OberflaecheKlangC2Tests
{
    private static ClientEinstellungen Laden(Testordner o, string json, out EinstellungsSpeicher speicher)
    {
        speicher = new EinstellungsSpeicher(o.Datei("client-einstellungen.json"));
        File.WriteAllText(speicher.Pfad, json);
        var e = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "Datei gelesen: " + json);
        return e;
    }

    [Test]
    public static void C2_Einstellungen_Version2OhneAbschnitt_AbWerkAnDeutlichLeiser_RundeReise_Nachsichtig()
    {
        using var o = new Testordner();
        var alt = Laden(o, "{ \"version\": 2, \"gate\": { \"modus\": \"Automatisch\", \"mindestAbstandDb\": 18 } }", out var speicher);
        Pruefe.Wahr(alt.Daempfung.Auto, "ab Werk an (Q1)");
        Pruefe.Gleich(Daempfungsstufe.DeutlichLeiser, alt.Daempfung.Stufe, "ab Werk deutlich leiser (Q26)");
        Pruefe.Gleich(DaempfungEinstellung.AbWerk, alt.Daempfung.AlsEinstellung(), "für die Maschine");
        Pruefe.Gleich(18f, alt.Gate.MindestAbstandDb, "das Übrige bleibt");

        alt.Daempfung.Auto = false;
        alt.Daempfung.Stufe = Daempfungsstufe.FastWeg;
        speicher.Speichern(alt);
        using (var json = JsonDocument.Parse(File.ReadAllBytes(speicher.Pfad)))
        {
            var d = json.RootElement.GetProperty("daempfung");
            Pruefe.Falsch(d.GetProperty("auto").GetBoolean(), "Schalter in der Datei");
            Pruefe.Gleich("FastWeg", d.GetProperty("stufe").GetString(), "Stufe in der Datei");
        }
        var neu = speicher.Laden();
        Pruefe.Gleich(new DaempfungEinstellung(false, Daempfungsstufe.FastWeg), neu.Daempfung.AlsEinstellung(), "gelesen wie geschrieben");

        var kaputt = Laden(o, "{ \"version\": 2, \"daempfung\": { \"auto\": \"vielleicht\", \"stufe\": \"Quatsch\" } }", out _);
        Pruefe.Gleich(DaempfungEinstellung.AbWerk, kaputt.Daempfung.AlsEinstellung(), "unlesbare Werte: ab Werk, die Datei bleibt lesbar");
    }

    [Test]
    public static void C2_Einstellungen_GehenBeimStartAnDieMaschine()
    {
        using var o = new Testordner();
        var s = new EinstellungsSpeicher(o.Datei("client-einstellungen.json"));
        var e = new ClientEinstellungen();
        e.Daempfung.Stufe = Daempfungsstufe.EtwasLeiser;
        using var ablage = new Einstellungsablage(s, e, SofortTakt.Instanz, TimeSpan.FromHours(1));
        var maschine = new KlangMaschinenAttrappe();
        _ = new AudioViewModel(maschine, new PegelVerlauf(), ablage);
        Pruefe.Gleich(new DaempfungEinstellung(true, Daempfungsstufe.EtwasLeiser), maschine.Daempfung, "Schalter und Stufe an der Maschine");
    }
}
