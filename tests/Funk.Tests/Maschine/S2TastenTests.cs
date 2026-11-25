// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using System.Text.Json.Serialization;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Klang.Eingabe;
using Funk.Tests.Oberflaeche;

namespace Funk.Tests.Maschine;

/// <summary>
/// Ausbaustufe S2, Teil A1: die neuen Tasten in den Einstellungen. Sie stehen in einer eigenen Liste mit der Funktion als Text,
/// damit ein Client 0.4.0 die Datei weiter liest (er legt sie sonst wegen eines unbekannten Enum-Namens beiseite); ein unbekannter
/// Name in der neuen Liste wird übergangen; unter Tasten gibt es die neuen Zeilen, den Abschnitt Einsatzleitung und die Wahl des
/// Meldenetzes.
/// </summary>
internal static class S2TastenTests
{
    private static readonly Tastenbelegung Strg = Tastenbelegung.Taste(0xE01D);
    private static readonly Tastenbelegung F9 = Tastenbelegung.Taste(0x0043);
    private static readonly Tastenbelegung F10 = Tastenbelegung.Taste(0x0044);
    private static readonly Tastenbelegung Stick = new(EingabeArt.Joystick, 12, @"\\?\HID#VID_3344&PID_0194&MI_00#7&1a2b3c&0&0000#{4d1e55b2}");

    /// <summary>Wie Client 0.4.0 die Tasten liest: Funktion als strenger Enum-Name mit den drei Werten von damals.</summary>
    private enum AltFunktion
    {
        Sprechen,
        NetzVor,
        NetzZurueck,
    }

    private sealed class AltEintrag
    {
        public AltFunktion Funktion { get; set; }

        public EingabeArt Art { get; set; }

        public int Code { get; set; }

        public string? Geraet { get; set; }
    }

    private sealed class AltTasten
    {
        public List<AltEintrag> Eintraege { get; } = [];

        public bool Voreingestellt { get; set; }
    }

    private sealed class AltDatei
    {
        public AltTasten Tasten { get; } = new();
    }

    /// <summary>Die Optionen von EinstellungsSpeicher 0.4.0 (JsonStringEnumConverter, CamelCase, Populate, unbekannte Felder übergangen).</summary>
    private static readonly JsonSerializerOptions Alt040 = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    [Test]
    public static void Datei_NeueTastenInEigenerListe_Client040LiestSie()
    {
        using var o = new Testordner();
        var speicher = new EinstellungsSpeicher(o.Datei("client-einstellungen.json"));
        var e = speicher.Laden();
        e.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.Sprechen, Strg));
        e.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.Melden, F9));
        e.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.FunkfeldUmschalten, F10));
        e.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.PhaseVor, Stick));
        e.Tasten.Meldenetz = 2;
        speicher.Speichern(e);

        using var json = JsonDocument.Parse(File.ReadAllBytes(speicher.Pfad));
        var eintraege = json.RootElement.GetProperty("tasten").GetProperty("eintraege").EnumerateArray().Select(x => x.GetProperty("funktion").GetString()).ToList();
        Pruefe.Gleich("NetzVor,NetzZurueck,Sprechen", string.Join(",", eintraege.Order(StringComparer.Ordinal)), "in eintraege nur die drei Funktionen von 0.4.0");
        var weitere = json.RootElement.GetProperty("tasten").GetProperty("weitere").EnumerateArray().Select(x => x.GetProperty("funktion").GetString()).ToList();
        Pruefe.Gleich("FunkfeldUmschalten,Melden,PhaseVor", string.Join(",", weitere.Order(StringComparer.Ordinal)), "die neuen in weitere");

        // So liest Client 0.4.0: Ein unbekannter Name in eintraege würfe, die Liste weitere und meldenetz übergeht er.
        var alt = JsonSerializer.Deserialize<AltDatei>(File.ReadAllBytes(speicher.Pfad), Alt040)!;
        Pruefe.Gleich(3, alt.Tasten.Eintraege.Count, "0.4.0 liest seine drei Tasten");
        Pruefe.Wahr(alt.Tasten.Eintraege.Any(x => x.Funktion == AltFunktion.Sprechen && x.Code == 0xE01D), "Sprechtaste bleibt für 0.4.0");

        // Gegenprobe: Stünde Melden in eintraege, legte 0.4.0 die Datei beiseite.
        var falsch = File.ReadAllText(speicher.Pfad).Replace("\"funktion\": \"NetzVor\"", "\"funktion\": \"Melden\"", StringComparison.Ordinal);
        _ = Pruefe.Wirft<JsonException>(() => JsonSerializer.Deserialize<AltDatei>(falsch, Alt040), "unbekannter Name in eintraege bricht 0.4.0");

        var wieder = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "0.5.0 liest die eigene Datei");
        Pruefe.Gleich(6, wieder.Tasten.Zuordnungen().Count, "Sprechen, Numpad + und -, Melden, Funkfeld, Phase vor: " + string.Join(",", wieder.Tasten.Zuordnungen()));
        Pruefe.Gleich(Tastenfunktion.PhaseVor, wieder.Tasten.FunktionVon(Stick), "Phase vor am Stick");
        Pruefe.Gleich((ushort?)2, wieder.Tasten.Meldenetz, "Meldenetz gemerkt");
    }

    [Test]
    public static void Datei_UnbekannterNameInWeitere_WirdUebergangenUndBleibt()
    {
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        File.WriteAllText(pfad, """
            {
              "tasten": {
                "eintraege": [ { "funktion": "Sprechen", "art": "Tastatur", "code": 57373 } ],
                "weitere": [
                  { "funktion": "Melden", "art": "Tastatur", "code": 67 },
                  { "funktion": "Sonderfunktion2027", "art": "Tastatur", "code": 68 },
                  { "funktion": "Sprechen", "art": "Tastatur", "code": 69 },
                  { "funktion": "4", "art": "Tastatur", "code": 70 },
                  { "funktion": "PhaseVor", "art": "Tastatur", "code": 67 }
                ],
                "voreingestellt": true
              }
            }
            """);
        var speicher = new EinstellungsSpeicher(pfad);
        var e = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "die Datei ist lesbar");
        Pruefe.Gleich("Sprechen:57373,Melden:67", string.Join(",", e.Tasten.Zuordnungen().Select(z => z.Funktion + ":" + z.Taste.Code)),
            "unbekannter Name, alte Funktion und Zahl in weitere wirken nicht; dieselbe Taste zweimal: die erste gilt");
        Pruefe.Wahr(e.Tasten.Weitere.Any(w => w.Funktion == "Sonderfunktion2027"), "ein unbekannter Name bleibt für den neueren Client stehen");
        speicher.Speichern(e);
        Pruefe.Wahr(File.ReadAllText(pfad).Contains("Sonderfunktion2027", StringComparison.Ordinal), "und wird mit geschrieben");
    }

    [Test]
    public static async Task Tasten_NeueZeilen_UndAbschnittEinsatzleitung_InDieEigeneListe()
    {
        using var a = new Aufbau();
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        var stick = new TastenQuelleAttrappe(EingabeArt.Joystick);
        using var verbund = new SprechtastenVerbund(raw, stick);
        using var vm = new TastenViewModel(a.Ablage, verbund, Tastennamen.Kurz);
        // Seit Klangstufe C2b dazu "Dauersender stumm" (Q8) und "Mikro stumm" (Q32), ab Werk unbelegt.
        Pruefe.Gleich("Sprechen,NetzVor,NetzZurueck,Melden,FunkfeldUmschalten,DauersenderStumm,MikroStumm,Bereit", string.Join(",", vm.Funktionen.Select(f => f.Funktion)),
            "Zeilen für jeden (seit 0.12.0 mit Bereit)");
        Pruefe.Gleich("PhaseVor,PhaseZurueck", string.Join(",", vm.Leitung.Select(f => f.Funktion)), "Abschnitt Einsatzleitung");

        raw.NaechsteTaste = F9;
        await vm.Funktionen.Single(f => f.Funktion == Tastenfunktion.Melden).BelegenCommand.ExecuteAsync(null);
        raw.NaechsteTaste = null;
        stick.NaechsteTaste = Stick;
        await vm.Leitung[0].BelegenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Hinweis?.Contains("Star Citizen", StringComparison.Ordinal) == true, "Hinweis beim Joystick: " + vm.Hinweis);
        Pruefe.Gleich(Tastenfunktion.Melden, raw.Zuordnungen.Single(z => z.Taste == F9).Funktion, "Melden an Raw Input");
        Pruefe.Gleich(Tastenfunktion.PhaseVor, stick.Zuordnungen.Single().Funktion, "Phase vor an den Joystick");
        Pruefe.Gleich(0, verbund.NichtBedient.Count, "alles bedient");
        Pruefe.Wahr(vm.Leitung[0].Belegungen.Single().Verfuegbar, "Phase vor verfügbar");
        Pruefe.Gleich(2, a.Ablage.Aktuell.Tasten.Weitere.Count, "beide in der eigenen Liste");
        Pruefe.Falsch(a.Ablage.Aktuell.Tasten.Eintraege.Any(e => !Tastenfunktionen.IstAlt(e.Funktion)), "eintraege ohne neue Funktion");

        // Eine Taste gehört genau einer Funktion: F9 wandert von Melden zur Sprechtaste, und zurück in die alte Liste.
        raw.NaechsteTaste = F9;
        await vm.Funktionen[0].BelegenCommand.ExecuteAsync(null);
        Pruefe.Gleich(0, vm.Funktionen.Single(f => f.Funktion == Tastenfunktion.Melden).Belegungen.Count, "F9 nicht mehr Melden");
        Pruefe.Wahr(a.Ablage.Aktuell.Tasten.Eintraege.Any(e => e.Funktion == Tastenfunktion.Sprechen && e.Code == F9.Code), "F9 jetzt Sprechen in eintraege");
        Pruefe.Gleich(1, a.Ablage.Aktuell.Tasten.Weitere.Count, "nur noch Phase vor in weitere");

        vm.Leitung[0].Belegungen.Single().LoeschenCommand.Execute(null);
        Pruefe.Gleich(0, a.Ablage.Aktuell.Tasten.Weitere.Count, "gelöscht");
        Pruefe.Gleich(0, stick.Zuordnungen.Count, "auch an der Quelle");
    }

    [Test]
    public static void Tasten_NetztastenBelegt_ZaehltNurSendenetz()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Tasten.Eintraege.Clear();
        a.Ablage.Aktuell.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.Melden, F9));
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        using var vm = new TastenViewModel(a.Ablage, raw, Tastennamen.Kurz);
        Pruefe.Falsch(vm.NetztastenBelegt, "die Melde-Taste ist keine Netztaste: + und - im Funkfeld schalten weiter selbst");
    }

    [Test]
    public static void Meldenetz_AbWerkCommand_WahlNachNetzId()
    {
        var lage = FunkDienstAttrappe.Raidabend();
        Pruefe.Gleich((ushort)FunkDienstAttrappe.NetzKommando, Meldenetzwahl.Aufloesen(null, lage)?.Id, "ab Werk Kommando");
        Pruefe.Gleich((ushort)FunkDienstAttrappe.NetzOrga, Meldenetzwahl.Aufloesen(FunkDienstAttrappe.NetzOrga, lage)?.Id, "gewählt Orga");
        Pruefe.Gleich((ushort)FunkDienstAttrappe.NetzKommando, Meldenetzwahl.Aufloesen(999, lage)?.Id, "ein verschwundenes Netz: wieder Kommando");
        Pruefe.Gleich(null, Meldenetzwahl.Aufloesen(null, lage with { Netze = [.. lage.Netze.Where(n => n.Name != "Kommando")] }), "ohne Kommando kein Meldenetz");
        // Sarevoc hört Kommando im Beispielabend, sendet dort aber nicht.
        Pruefe.Gleich("Melden geht nicht: kein Senderecht in Kommando", Meldenetzwahl.Hindernis(Meldenetzwahl.Aufloesen(null, lage), lage), "ohne Senderecht");
        Pruefe.Gleich(null, Meldenetzwahl.Hindernis(Meldenetzwahl.Aufloesen(FunkDienstAttrappe.NetzOrga, lage), lage), "Orga mit Senderecht");
        Pruefe.Gleich("Melden geht nicht: nicht verbunden", Meldenetzwahl.Hindernis(null, lage with { Status = Verbindungsstatus.Getrennt }), "getrennt");
        // 0.10.0: Der Satz nennt den Weg, wo man den Meldekanal wählt (seit dem Umbau unter Einstellungen > Tasten im Klappbereich); wörtlich geprüft,
        // damit ein alter Weg nicht unbemerkt zurückkommt (Befund der Prüfung). Seit 0.11.0 kürzer für die einzeilige Meldezeile, seit 0.11.1 heißt
        // der Klappbereich Erweitert (Sarevoc 12.10.2026).
        Pruefe.Gleich("Melden: kein Meldekanal (Tasten > Erweitert)", Meldenetzwahl.Hindernis(null, lage),
            "ohne Meldekanal: wo man ihn wählt");

        using var a = new Aufbau(dienst: FunkDienstAttrappe.MitRaidabend());
        using var vm = new TastenViewModel(a.Ablage, null, Tastennamen.Kurz, dienst: a.Dienst, takt: a.Takt);
        Pruefe.Gleich("ab Werk: Kommando", vm.GewaehltesMeldenetz?.Name, "Voreinstellung");
        vm.GewaehltesMeldenetz = vm.Meldenetze.Single(w => w.Name == "Orga");
        Pruefe.Gleich((ushort?)FunkDienstAttrappe.NetzOrga, a.Ablage.Aktuell.Tasten.Meldenetz, "gespeichert als Netz-ID");
        var vorher = vm.Meldenetze.ToList();
        a.Dienst.LageSetzen(l => l with { Sprecher = [] });
        Pruefe.Wahr(vorher.SequenceEqual(vm.Meldenetze) && ReferenceEquals(vorher[0], vm.Meldenetze[0]), "ein Push ohne neue Netze ersetzt die Liste nicht");
        Pruefe.Gleich("Orga", vm.GewaehltesMeldenetz?.Name, "Wahl bleibt");
    }
}
