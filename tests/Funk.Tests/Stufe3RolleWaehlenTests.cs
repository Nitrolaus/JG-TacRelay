// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Lage;
using static Funk.Tests.Kanalprobe;

namespace Funk.Tests;

/// <summary>
/// RolleWaehlen (Fassung 0.6.0, 0x0039, FUNKPROTOKOLL 6.16, P-61) und die Ch.-Auflösung am Zustandsschreiber: Die Rolle legt nur
/// Felder an, Platz, Reservierung und Brett bleiben, die Sperre der Selbstwahl greift nicht; feste Felder, nicht selbst wählbare
/// Rollen, Sendevorschlag, laufende Phase, Wünsche je Feld; die gewählte Rolle und wann sie entfällt; die Folge der Ch.-Auflösung (fest,
/// Einheit, aktueller Ch., Wunsch, Ruhe) auf jedem Weg, der Felder aus einer Rolle bildet.
/// </summary>
internal static class Stufe3RolleWaehlenTests
{
    private const uint Admin = Funkprobe.Admin;

    /// <summary>"2 Entern": Kapitän sendet in Kommando Ch. 3 (fest) und hört Brücke.</summary>
    private static Einsatzphase Entern => new(2, "Entern", "ENTERN")
    {
        Varianten = [new PhasenVariante(Co) { Felder = [Rf(0, Kommando, standard: true, ch: 3), Rf(1, KanalBruecke, senden: false)] }],
    };

    private static void Phase2(Funkprobe f)
    {
        Ok(f, Auftrag.PhaseSetzen(Admin, Entern, f.Z.NetzListeVersion), "Phase 2 angelegt");
        Ok(f, Auftrag.PhaseSchalten(Admin, 2, 1), "auf Phase 2 geschaltet");
    }

    [Test]
    public static void RolleWaehlen_PlatzReservierungBrettBleiben_RolleGewaehlt()
    {
        var f = Neu();
        Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(F) { RolleId = Co, SchiffId = Schiff4 }]), "Einsatzleitung setzt F als CO auf Schiff 4");
        Pruefe.Gleich("0:101/0hs 1:100/4hs 2:102/4hs", Felder(f, F), "Ch. aus der Einheit, Kommando auf Ruhe");
        var vorher = Konto(f, F);
        var belegung = f.Z.Belegung(f.Jetzt);
        var r = Ok(f, Auftrag.RolleWaehlen(F, JaegerRolle), "F wählt Jägerpilot");
        Pruefe.Wahr(r.Uebersprungen.IsEmpty, "nichts übersprungen");
        var k = Konto(f, F);
        Pruefe.Gleich("0:103/0h 1:100/2hs 2:1/2h", Felder(f, F), "Jäger auf Ruhe, Brücke fest Ch. 2, Global 1");
        Pruefe.Gleich(vorher.Platz, k.Platz, "der Platz bleibt");
        Pruefe.Gleich(vorher.Reservierung, k.Reservierung, "die Reservierung bleibt");
        Pruefe.Wahr(belegung.Gleich(f.Z.Belegung(f.Jetzt)), "das Brett bleibt");
        Pruefe.Gleich((ushort?)JaegerRolle, k.GewaehlteRolle, "gewählte Rolle");
        Pruefe.Gleich(Quelle.Selbst, k.Quelle, "Quelle SELBST");
        Pruefe.Wahr(k.Sendeauswahl.Felder.SequenceEqual([(byte)1]), "Sendevorschlag Brücke");
        Pruefe.Gleich(0, r.BetroffeneKonten.Count(x => x != F), "nur F betroffen");
    }

    [Test]
    public static void RolleWaehlen_SperreDerSelbstwahlGreiftNicht_NichtSelbstwaehlbarUndUnbekannt()
    {
        var f = Neu();
        Ok(f, Auftrag.SelbstwahlSetzen(Admin, false, f.Z.NetzListeVersion, nachzuegler: false), "ganz gesperrt");
        Pruefe.Gleich(Fehlercode.SelbstwahlGesperrt, Code(f, Auftrag.EinsteigenRolleSchiff(G, null, Co, Schiff4)), "Einsteigen gesperrt");
        Ok(f, Auftrag.RolleWaehlen(G, Co), "die Rolle wählen geht trotzdem");
        Pruefe.Wahr(Konto(f, G).Platz is null, "ohne Platz");
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.RolleWaehlen(G, FestRolle)), "nicht selbst wählbar");
        f.MitAuftrag(Funkprobe.A);
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.RolleWaehlen(Funkprobe.A, FestRolle)), "auch mit Auftrag nicht");
        Pruefe.Gleich(Fehlercode.Unbekannt, Code(f, Auftrag.RolleWaehlen(G, 999)), "unbekannte Rolle");
        Pruefe.Gleich((ushort?)Co, Konto(f, G).GewaehlteRolle, "abgewiesene Wahlen ändern nichts");
    }

    [Test]
    public static void RolleWaehlen_FesteFelderBleiben_UeberlaufInUebersprungen()
    {
        var f = Neu();
        var vorgaben = Enumerable.Range(0, 15).Select(i => new Feldvorgabe((byte)i, K(KanalBruecke, (byte)(i + 1)), true, false)).ToArray();
        Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(G) { Felder = [.. vorgaben] }]), "15 feste Felder");
        var r = Ok(f, Auftrag.RolleWaehlen(G, Co), "G wählt CO");
        var k = Konto(f, G);
        Pruefe.Wahr(k.Felder.Take(15).All(m => m.Fest), "die festen Felder bleiben");
        Pruefe.Wahr(k.FeldNummer(15) is { NetzId: var cmd } && cmd == K(Kommando, 0), "Kommando rückt auf Feld 15 nach");
        Pruefe.Wahr(k.FeldMitNetz(K(KanalBruecke, 2)) is { Feld: 1, Fest: true }, "Brücke geht im festen Feld mit dem aktuellen Ch. (Feld 1) auf");
        Pruefe.Wahr(r.Uebersprungen.Length == 1 && r.Uebersprungen[0] is { Eintrag: 2, Code: Fehlercode.Ungueltig }, "Crew passt nicht mehr: übersprungen");
        Pruefe.Wahr(k.Entfallen.Length == 1 && k.Entfallen[0].Feld == 2, "und in entfallen");
    }

    [Test]
    public static void RolleWaehlen_MitLaufenderPhase_UndPhaseRichtetSichNachDerGewaehltenRolle()
    {
        var f = Neu();
        Ok(f, Auftrag.RolleWaehlen(F, Co), "F wählt CO in der Grundstellung");
        Ok(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 8)), "F auf Schiff 8");
        Phase2(f);
        Pruefe.Gleich("0:101/3hs 1:100/8h", Felder(f, F), "Phase 2 trifft F über die gewählte Rolle, ohne Platz; der Ch. 8 bleibt");
        Pruefe.Wahr(Konto(f, F).Sendeauswahl.Felder.SequenceEqual([(byte)0]), "Sendevorschlag der Phase: Kommando Ch. 3");
        Ok(f, Auftrag.RolleWaehlen(H, Co), "H wählt CO in Phase 2");
        Pruefe.Gleich("0:101/3hs 1:100/0h", Felder(f, H), "die Felder kommen aus der Variante");
        Ok(f, Auftrag.PhaseSchalten(Admin, 1, 2), "zurück auf die Grundstellung");
        Pruefe.Gleich("0:101/3hs 1:100/8hs 2:102/0hs", Felder(f, F), "Grundstellung: Kommando behält Ch. 3 (aktueller Ch.), Crew auf Ruhe");
    }

    [Test]
    public static void RolleWaehlen_Wuensche_GeprueftAberNichtAngewandt()
    {
        // Seit 0.8.1 (Q46, Sarevoc am 06.10.2026): Nach der Rollenwahl beginnt jeder Ch. auf Ruhe; Wünsche eines Clients bis 0.8.0 prüft der
        // Server noch (ungültige stehen weiter in uebersprungen), wendet sie aber nicht mehr an.
        var f = Neu();
        var r = Ok(f, Auftrag.RolleWaehlen(F, Co, new Dictionary<byte, byte> { [1] = 7, [2] = 30, [5] = 3 }), "F wählt CO mit Wünschen");
        Pruefe.Gleich("0:101/0hs 1:100/0hs 2:102/0hs", Felder(f, F), "der Wunsch Ch. 7 gilt nicht mehr: alles auf Ruhe");
        Pruefe.Gleich("2:UNGUELTIG 5:UNGUELTIG", string.Join(" ", r.Uebersprungen.Select(u => u.Eintrag + ":" + Drahtnamen.Von(u.Code))),
            "Ch. 30 gibt es in Crew nicht, Feld 5 hat die Rolle nicht");
        Ok(f, Auftrag.RolleWaehlen(F, JaegerRolle, new Dictionary<byte, byte> { [0] = 24, [1] = 9 }), "F wählt Jaeger mit Wünschen");
        Pruefe.Gleich("0:103/0h 1:100/2hs 2:1/2h", Felder(f, F), "Jäger auf Ruhe trotz Wunsch; der feste Ch. 2 gilt");
        var orgaweit = Ok(f, Auftrag.RolleWaehlen(F, JaegerRolle, new Dictionary<byte, byte> { [2] = 1 }), "Wunsch Gesamt für das Global-Feld");
        Pruefe.Wahr(orgaweit.Uebersprungen is [{ Eintrag: 2, Code: Fehlercode.Ungueltig }] && Konto(f, F).FeldNummer(2) is { NetzId: var g } && g == K(Netz.GlobalId, 2),
            "Gesamt ist kein Ch. für ein Feld: übersprungen, Global 1 bleibt");
    }

    [Test]
    public static void RolleWaehlen_GewaehlteRolle_EntfaelltMitAndererVorgabe()
    {
        var f = Neu();
        Ok(f, Auftrag.RolleWaehlen(F, Co), "gewählt");
        Ok(f, Auftrag.FeldSetzen(F, 5, K(KanalBruecke, 9)), "eigenes Feld");
        Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(F) { FelderPatch = [new Feldpatch(6, Funkprobe.Kommando)] }]), "Zusatznetz der Leitung");
        Pruefe.Gleich((ushort?)Co, Konto(f, F).GewaehlteRolle, "eigene Felder und felderPatch lassen sie stehen");
        Ok(f, Auftrag.EinsteigenRolleSchiff(F, null, BrueckeRolle, Schiff4), "Einsteigen");
        Pruefe.Gleich((ushort?)null, Konto(f, F).GewaehlteRolle, "Einsteigen: die Rolle des Platzes gilt");
        Ok(f, Auftrag.RolleWaehlen(F, Co), "wieder gewählt");
        Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(F) { RolleId = Co, SchiffId = Schiff4 }]), "Verschieben mit Rolle");
        Pruefe.Gleich((ushort?)null, Konto(f, F).GewaehlteRolle, "Verschieben mit Rolle");
        Ok(f, Auftrag.RolleWaehlen(F, JaegerRolle), "wieder gewählt");
        Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(F) { RolleId = 0 }]), "zurück auf STANDARD");
        Pruefe.Gleich((ushort?)null, Konto(f, F).GewaehlteRolle, "STANDARD");
        Ok(f, Auftrag.RolleWaehlen(F, JaegerRolle), "wieder gewählt");
        Ok(f, Auftrag.RolleLoeschen(Admin, JaegerRolle, f.Z.NetzListeVersion), "Rolle gelöscht");
        Pruefe.Wahr(Konto(f, F) is { GewaehlteRolle: null, Felder.Length: 3 }, "die gelöschte Rolle entfällt, die Felder bleiben");
        Pruefe.Gleich("", string.Join("; ", f.Z.Pruefen()), "stimmig");
    }

    // ---------------------------------------------------------------- Ch.-Auflösung

    [Test]
    public static void ChAufloesung_FolgeFestEinheitAktuellRuhe_OhneWunsch()
    {
        var f = Neu();
        // (1) fest schlägt den aktuellen Ch.
        Ok(f, Auftrag.FeldSetzen(G, 0, K(KanalBruecke, 3)), "G auf Schiff 3");
        Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(G) { RolleId = FestRolle }]), "feste Rolle mit Ch. 5");
        Pruefe.Gleich("0:100/5hsf", Felder(f, G), "(1) fester Ch. 5");
        // (2) die Einheit schlägt den aktuellen Ch.
        Ok(f, Auftrag.FeldSetzen(H, 1, K(KanalBruecke, 9)), "H auf Schiff 9");
        Ok(f, Auftrag.EinsteigenRolleSchiff(H, null, Co, Schiff4), "H steigt in Schiff 4 ein");
        Pruefe.Gleich("0:101/0hs 1:100/4hs 2:102/4hs", Felder(f, H), "(2) Schiff 4 gibt Ch. 4");
        // (3) der aktuelle Ch. bleibt; (4) der Wunsch gilt seit 0.8.1 nicht mehr (Q46), also (5) Ruhe.
        Ok(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 9)), "F auf Schiff 9");
        Ok(f, Auftrag.RolleWaehlen(F, Co, new Dictionary<byte, byte> { [1] = 7, [0] = 2 }), "F wählt CO mit Wünschen");
        Pruefe.Gleich("0:101/0hs 1:100/9hs 2:102/0hs", Felder(f, F), "(3) Schiff 9 bleibt, Kommando trotz Wunsch und Crew auf Ruhe");
        // Ein aktueller Ch. im selben Feld geht vor einem anderen Feld desselben Kanalnetzes.
        Ok(f, Auftrag.FeldSetzen(F, 7, K(KanalBruecke, 11)), "F hat Brücke zweimal");
        Ok(f, Auftrag.RolleWaehlen(F, Co), "noch einmal");
        Pruefe.Wahr(Konto(f, F).FeldNummer(1) is { NetzId: var n } && n == K(KanalBruecke, 9), "Feld 1 behält seinen Ch.");
    }

    [Test]
    public static void ChAufloesung_AufJedemWeg_UndStandardAufRuhe()
    {
        var f = Neu();
        // Aufstellung: die Einheit gibt den Ch.
        var gespeichert = Ok(f, Auftrag.AufstellungSpeichern(Admin, null, null, "Abend", [new AufstellungsEintrag { KontoId = G, RolleId = Co, SchiffId = Schiff4 }]), "Aufstellung");
        Ok(f, Auftrag.AufstellungAnwenden(Admin, gespeichert.AufstellungsId, null, Aufstellungsmodus.Ergaenzen), "angewandt");
        Pruefe.Gleich("0:101/0hs 1:100/4hs 2:102/4hs", Felder(f, G), "Aufstellung");
        // Verschieben mit Rolle ohne Schiff: der aktuelle Ch. bleibt.
        Ok(f, Auftrag.FeldSetzen(H, 1, K(KanalBruecke, 6)), "H auf Schiff 6");
        Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(H) { RolleId = JaegerRolle }]), "Verschieben in Jägerpilot");
        Pruefe.Gleich("0:103/0h 1:100/2hs 2:1/2h", Felder(f, H), "Verschieben: fester Ch. 2 vor dem aktuellen Ch. 6");
        // "Wie letztes Mal" in einer Phase: die Felder aus der Phase mit dem aktuellen Ch.
        Ok(f, Auftrag.RolleWaehlen(F, Co), "F wählt CO");
        Ok(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 12)), "F auf Schiff 12");
        Phase2(f);
        Ok(f, Auftrag.SatzAnwenden(F, Co, Schiff4, [new Feldvorgabe(1, K(KanalBruecke, 4), false, false)]), "wie letztes Mal");
        Pruefe.Gleich("0:101/3hs 1:100/4", Felder(f, F), "Phase 2 auf Schiff 4: Kommando Ch. 3 fest, Brücke Ch. 4 aus der Einheit, hören aus dem Satz");
        // STANDARD: ein Kanalnetz mit standard liegt auf Ruhe und sendet nicht.
        var s = Neu(b => b.MitNetz(new Netz(KanalBruecke, "Brücke") { Kanaele = Liste("Schiff", 20), Offen = Offenheit.HoerenSenden, Standard = true }));
        Ok(s, Auftrag.KontoAnlegen(new Konto { Id = 1009, Name = "Neu" }), "neues Konto mit STANDARD");
        Pruefe.Gleich("0:100/0hs", Felder(s, 1009), "Brücke auf Ruhe");
        Pruefe.Wahr(s.Z.Konten[1009].Sendeauswahl.Felder.IsEmpty, "die Ruhe ist kein Sendevorschlag");
    }
}
