// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>
/// Drahtform der Fassung 0.5.0 (4.5): Bodies von 0x003C, 0x003D, 0x4004, die Phasen in NetzListe mit ihren Grenzen, die neuen
/// optionalen Felder, und dass ein Leser unbekannte Felder übergeht (so lesen Clients 0.4.x die neuen Felder, ohne zu scheitern).
/// </summary>
internal static class S2FormTests
{
    private static DekodierErgebnis Lesen<T>(string json) => SteuerJson.Lesen(Encoding.UTF8.GetBytes(json), SteuerJson.Info<T>());

    /// <summary><paramref name="n"/> Felder der Variante einer Rolle, flach mit <c>rolleId</c> (6.14).</summary>
    private static string Felder(int n, int rolle = 13) => string.Join(",", Enumerable.Range(0, n).Select(i =>
        $$"""{"rolleId":{{rolle}},"feld":{{i % 16}},"netzId":{{100 + i}},"senden":false,"hoeren":true,"sendenStandard":false,"fest":false}"""));

    [Test]
    public static void Bodies_FunkermodusUndPhaseSchalten()
    {
        Pruefe.Wahr(Lesen<FunkermodusAnfrage>("""{"an":true}""").Ok, "Funkermodus {an}");
        Pruefe.Gleich(Fehlercode.Ungueltig, Lesen<FunkermodusAnfrage>("{}").Fehler, "an ist Pflicht");
        Pruefe.Wahr(Lesen<FunkermodusAnfrage>("""{"an":false,"kontoId":7}""").Ok, "ein fremdes Feld zählt nicht (es gibt kein Konto im Body)");
        Pruefe.Wahr(Lesen<PhaseSchaltenAnfrage>("""{"nr":2,"erwartet":1}""").Ok, "PhaseSchalten");
        Pruefe.Gleich(Fehlercode.Ungueltig, Lesen<PhaseSchaltenAnfrage>("""{"nr":0,"erwartet":1}""").Fehler, "nr 0");
        Pruefe.Gleich(Fehlercode.Ungueltig, Lesen<PhaseSchaltenAnfrage>("""{"nr":6,"erwartet":1}""").Fehler, "nr 6");
        Pruefe.Gleich(Fehlercode.Ungueltig, Lesen<PhaseSchaltenAnfrage>("""{"nr":2}""").Fehler, "erwartet ist Pflicht");
        var antwort = Encoding.UTF8.GetString(SteuerJson.Serialisieren(new PhaseSchaltenAntwort { Nr = 2, Umgestellt = 9, Ausgelassen = [7], ZustandsVersion = 42 }));
        Pruefe.Gleich("""{"nr":2,"umgestellt":9,"ausgelassen":[7],"zustandsVersion":42}""", antwort, "Antwort");
        var push = Encoding.UTF8.GetString(SteuerJson.Serialisieren(new PhaseGeschaltet { Nr = 2, Name = "Entern", Kurz = "ENTERN", Durch = 5, Seit = 1 }));
        Pruefe.Gleich("""{"nr":2,"name":"Entern","kurz":"ENTERN","durch":5,"seit":1}""", push, "Push");
        Pruefe.Gleich("""{"an":true,"bis":99}""", Encoding.UTF8.GetString(SteuerJson.Serialisieren(new FunkermodusAntwort { An = true, Bis = 99 })), "Funkermodus-Antwort");
        Pruefe.Gleich("""{"an":false}""", Encoding.UTF8.GetString(SteuerJson.Serialisieren(new FunkermodusAntwort { An = false })), "aus: ohne bis");
    }

    [Test]
    public static void NetzListe_Phasen_GrenzenUndFormen()
    {
        const string Kopf = """{"version":1,"einstellungen":{"durchsageNurTaste":true,"selbstwahl":true},"netze":[],"schiffe":[],"rollen":[]""";
        Pruefe.Wahr(Lesen<NetzListe>(Kopf + "}").Body is NetzListe { Phasen: null }, "ohne Phasen: das Feld fehlt");
        var gut = Kopf + $$""","phasen":[{"nr":1,"name":"Anflug","kurz":"ANFLUG"},{"nr":2,"name":"Entern","kurz":"ENTERN","felder":[{{Felder(3)}}]}]}""";
        Pruefe.Wahr(Lesen<NetzListe>(gut).Body is NetzListe { Phasen: [{ Nr: 1, Felder: null }, { Nr: 2, Felder: [{ RolleId: 13, Feld: 0 }, _, { NetzId: 102 }] }] }, "zwei Phasen");
        Pruefe.Gleich(Fehlercode.Ungueltig, Lesen<NetzListe>(Kopf + $$""","phasen":[{"nr":1,"name":"Anflug","kurz":"A","felder":[{{Felder(1)}}]}]}""").Fehler,
            "die Grundstellung trägt keine Felder");
        Pruefe.Gleich(Fehlercode.ZuGross, Lesen<NetzListe>(Kopf + $$""","phasen":[{"nr":2,"name":"{{new string('E', 25)}}","kurz":"E"}]}""").Fehler, "Name über 24");
        Pruefe.Gleich(Fehlercode.ZuGross, Lesen<NetzListe>(Kopf + $$""","phasen":[{"nr":2,"name":"E","kurz":"{{new string('E', 13)}}"}]}""").Fehler, "Kurzname über 12");
        Pruefe.Gleich(Fehlercode.ZuGross, Lesen<NetzListe>(Kopf + $$""","phasen":[{"nr":2,"name":"E","kurz":"E","felder":[{{Felder(17)}}]}]}""").Fehler,
            "17 Felder für eine Rolle");
        Pruefe.Gleich(Fehlercode.Ungueltig, Lesen<NetzListe>(Kopf + ""","phasen":[{"nr":2,"name":"E","kurz":"E","felder":[{"rolleId":13,"feld":0,"senden":false,"hoeren":true,"sendenStandard":false,"fest":false}]}]}""").Fehler,
            "Feld ohne netzId und ohne schiffsnetz");
        var phasen = Enumerable.Range(2, 4).Select(nr => $$"""{"nr":{{nr}},"name":"P","kurz":"P","felder":[{{string.Join(",", Enumerable.Range(1, 8).Select(r => Felder(16, r)))}}]}""").ToList();
        Pruefe.Wahr(Lesen<NetzListe>(Kopf + ",\"phasen\":[" + string.Join(",", phasen) + "]}").Ok, "512 Variantenfelder gehen");
        var eineMehr = phasen.Append("""{"nr":1,"name":"G","kurz":"G"}""").ToList();
        eineMehr[0] = eineMehr[0].Replace("\"felder\":[", "\"felder\":[" + Felder(1, 9) + ",", StringComparison.Ordinal);
        Pruefe.Gleich(Fehlercode.ZuGross, Lesen<NetzListe>(Kopf + ",\"phasen\":[" + string.Join(",", eineMehr) + "]}").Fehler, "513 Felder");
        // Tiefe 8 (4.3): Eine Phase in Verwaltung PAKET liegt höchstens auf Tiefe 7.
        var paket = """{"op":"PAKET","version":3,"objekt":{"ops":[{"op":"PHASE_SETZEN","objekt":{"nr":2,"name":"E","kurz":"E","felder":[""" + Felder(2) + "]}}]}}";
        var gelesen = Lesen<VerwaltungAnfrage>(paket);
        Pruefe.Wahr(gelesen.Ok, "PAKET mit einer Phase ist erlaubt: " + gelesen.Fehler);
        var schritt = ((VerwaltungAnfrage)gelesen.Body!).ObjektLesen<VerwaltungsPaket>();
        Pruefe.Wahr(schritt.Body is VerwaltungsPaket { Ops: [{ Op: VerwaltungsOperation.PhaseSetzen }] } vp && vp.Ops[0].ObjektLesen<NetzPhase>().Body is NetzPhase { Felder.Count: 2 },
            "und lesbar");
        var sechs = string.Join(",", Enumerable.Range(1, 6).Select(nr => $$"""{"nr":{{Math.Min(nr, 5)}},"name":"P","kurz":"P"}"""));
        Pruefe.Gleich(Fehlercode.ZuGross, Lesen<NetzListe>(Kopf + ",\"phasen\":[" + sechs + "]}").Fehler, "mehr als 5 Einträge");
    }

    [Test]
    public static void Optionale_Felder_UndUnbekannteFelderWerdenUebergangen()
    {
        // Ohne die schließende Klammer, damit die Fälle Felder anhängen können.
        const string Mg = "{\"zustandsVersion\":1,\"kontoVersion\":1,\"felder\":[],\"global\":{\"senden\":false},\"sendeauswahl\":{\"felder\":[],\"global\":false},\"stumm\":[],\"quelle\":\"VERSCHIEBEN\"";
        Pruefe.Wahr(Lesen<Mitgliedschaften>(Mg + ",\"phase\":2}").Body is Mitgliedschaften { Phase: 2 }, "Mitgliedschaften.phase");
        Pruefe.Gleich(Fehlercode.Ungueltig, Lesen<Mitgliedschaften>(Mg + ",\"phase\":6}").Fehler, "phase über 5");
        Pruefe.Wahr(Lesen<Mitgliedschaften>(Mg + "}").Body is Mitgliedschaften { Phase: null }, "ohne phase");
        // So lesen Clients 0.4.x die neuen Felder: unbekannt, also übergangen, ohne Fehler (UnmappedMemberHandling.Skip, 4.7).
        Pruefe.Wahr(Lesen<Mitgliedschaften>(Mg + ",\"zukunft\":{\"x\":[1,2]},\"anlass\":\"PHASE\"}").Ok, "unbekannte Felder übergangen");
        Pruefe.Wahr(Lesen<KontoStand>("""{"rechte":["ZUWEISEN"],"scNamen":[],"rollen":[],"lieblingsrollen":[],"zeichen":[],"zeichenZeigen":true,"alsFunkerBis":1790000000000}""").Body
            is KontoStand { AlsFunkerBis: 1790000000000 }, "konto.alsFunkerBis");
        Pruefe.Wahr(Lesen<PraesenzEintrag>("""{"kontoId":3,"name":"Test","benutzername":"test","online":true,"alsFunker":true}""").Body is PraesenzEintrag { AlsFunker: true }, "Praesenz.alsFunker");
        // Kein neuer Wert in den strengen Aufzählungen, die Clients 0.4.0 lesen: Ein unbekannter Wert ist UNGUELTIG, also verboten.
        Pruefe.Gleich(Fehlercode.Ungueltig, Lesen<Mitgliedschaften>(Mg.Replace("VERSCHIEBEN", "PHASE", StringComparison.Ordinal) + "}").Fehler,
            "MitgliedschaftsQuelle PHASE gibt es nicht (ein Client 0.4.0 scheiterte daran)");
        Pruefe.Gleich("STANDARD,ROLLE,SELBST,VERSCHIEBEN,AUFSTELLUNG,WIEDERHERGESTELLT,SATZ",
            string.Join(",", Enum.GetNames<MitgliedschaftsQuelle>().Select(n => n.ToUpperInvariant())), "Quelle unverändert");
        Pruefe.Gleich(9, Enum.GetValues<Recht>().Length, "Recht unverändert");
        Pruefe.Gleich(3, Enum.GetValues<Stufe>().Length, "Stufe unverändert");
        Pruefe.Gleich(30, Enum.GetValues<Fehlercode>().Length, "Fehlercode unverändert");
    }
}
