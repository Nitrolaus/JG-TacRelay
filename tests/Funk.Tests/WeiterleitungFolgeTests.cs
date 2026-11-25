// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>Prüfwerte 8.10 (Folge-Matrix, P-17) mit simulierter Uhr, Grundzustand wie 8.9.</summary>
internal static class WeiterleitungFolgeTests
{
    private const uint A = Funkprobe.A;
    private const uint B = Funkprobe.B;
    private const uint C = Funkprobe.C;
    private const uint D = Funkprobe.D;
    private const uint E = Funkprobe.E;

    [Test]
    public static void F1_UmstellungMittenImSpruch()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Netz, [2, 7], phase: 5, frame: 100);
        Pruefe.Gleich("B:NETZ[7]+B C:NETZ[2]+B E:NETZ[7,2]+B", f.M.Empfang(true), "Paket 1 mit BEGINN");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [2, 7], phase: 5, frame: 101);
        Pruefe.Gleich("B:NETZ[7] C:NETZ[2] E:NETZ[7,2]", f.M.Empfang(true), "Paket 2 ohne BEGINN");

        f.M.Leeren();
        Pruefe.Wahr(f.FeldNehmen(A, 1).Erfolg, "Admin nimmt A das Feld 1 (Netz 7)");
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [2, 7], phase: 5, frame: 102);
        Pruefe.Gleich("B:NETZ[7]+E+X C:NETZ[2] E:NETZ[2]", f.M.Empfang(true), "Paket 3: B erzeugtes ENDE, C und E ohne BEGINN");
        var ende = f.M.Sprache.Single(s => s.Stempel.Erzeugt);
        Pruefe.Gleich(102u, ende.Stempel.FrameNr, "Frame-Nr des erzeugten ENDE = zuletzt an B + 1");
        Pruefe.Gleich((ushort)5, ende.Stempel.Sprechphase, "Sprechphase der Phase");
        Pruefe.Gleich(0, ende.Opus, "ohne Opus");
        Pruefe.Gleich(1, f.M.Fehler.Count, "A bekommt sofort SendeFehler");
        Pruefe.Gleich(SendeFehlerCode.ZielEntzogen, f.M.Fehler[0].Fehler[0].Code, "ZIEL_ENTZOGEN");
        Pruefe.Gleich((ushort)7, f.M.Fehler[0].Fehler[0].NetzId, "Netz 7");

        // A's Client beendet Phase 5 und setzt ohne Unterbrechung Phase 6 an [2] fort (C4).
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [2, 7], phase: 5, frame: 103, ende: true);
        Pruefe.Gleich("C:NETZ[2]+E E:NETZ[2]+E", f.M.Empfang(true), "ENDE von Phase 5 nur an die Bedienten");
        f.M.Leeren();
        f.Senden(A, Sendeart.Netz, [2], phase: 6, frame: 104);
        Pruefe.Gleich("C:NETZ[2]+B E:NETZ[2]+B", f.M.Empfang(true), "Phase 6 mit BEGINN, B nichts");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [2], phase: 6, frame: 105);
        Pruefe.Gleich("C:NETZ[2] E:NETZ[2]", f.M.Empfang(true), "Phase 6 läuft");
    }

    [Test]
    public static void F2_HoerenEinschaltenMittenInDerPhase()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [7], frame: 100);
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], frame: 101);
        Pruefe.Wahr(f.Tun(Auftrag.HoerenSetzen(C, 7, true)).Erfolg, "C HoerenSetzen(7, true)");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], frame: 102);
        Pruefe.Gleich("A:NETZ[7] C:NETZ[7]+B E:NETZ[7]", f.M.Empfang(true), "C mit BEGINN, A und E unverändert");
    }

    [Test]
    public static void F3_EmpfaengerFaelltHeraus_EinmalEnde()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [7], frame: 100);
        Pruefe.Wahr(f.FeldNehmen(E, 0).Erfolg, "Admin verschiebt E aus Netz 7");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], frame: 101);
        Pruefe.Gleich("A:NETZ[7] E:NETZ[7]+E+X", f.M.Empfang(true), "E einmal erzeugtes ENDE [7], A unverändert");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], frame: 102);
        f.Senden(B, Sendeart.Netz, [7], frame: 103, ende: true);
        Pruefe.Falsch(f.M.Sprache.Exists(s => s.Stempel.Empfaenger == Funkprobe.S(E)), "danach nichts an E (P-47)");
    }

    [Test]
    public static void F4_DurchsageNeuesMitglied_BeginnUndDurchsageStart()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Durchsage, [7], frame: 100);
        Pruefe.Gleich("B:DURCHSAGE[7]+B C:DURCHSAGE[7]+B E:DURCHSAGE[7]+B", f.M.Empfang(true), "Beginn der Durchsage");
        var gib = Auftrag.Verschieben(Funkprobe.Admin, [new Aenderung(D) { FelderPatch = [new Feldpatch(0, 7)] }]);
        Pruefe.Wahr(f.Tun(gib).Erfolg, "Admin gibt D ein Feld mit Netz 7");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Durchsage, [7], frame: 101);
        Pruefe.Gleich("B:DURCHSAGE[7] C:DURCHSAGE[7] D:DURCHSAGE[7]+B E:DURCHSAGE[7]", f.M.Empfang(true), "D mit BEGINN");
        Pruefe.Gleich(1, f.M.Starts.Count, "genau ein DurchsageStart");
        var s = f.M.Starts[0];
        Pruefe.Gleich(Funkprobe.S(D), s.Empfaenger, "an D");
        Pruefe.Gleich(1001u, s.Sprecher, "sprecher 1001");
        Pruefe.Gleich(Stempelart.Durchsage, s.Art, "art DURCHSAGE");
        Pruefe.Wahr(s.Netze.SequenceEqual(new ushort[] { 7 }), "netze [7]");
    }

    [Test]
    public static void F5_ZielwechselInnerhalbDerPhase_NeuePhase()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [7], phase: 9, frame: 100);
        f.M.Leeren();
        f.Jetzt += 20;
        var r = f.Senden(B, Sendeart.Netz, [8], phase: 9, frame: 101);
        Pruefe.Falsch(r.Verworfen, "nichts wird verworfen");
        Pruefe.Gleich("A:NETZ[7]+E+X C:NETZ[8]+B E:NETZ[7]+E+X", f.M.Empfang(true), "A und E ENDE [7], C BEGINN [8]");
    }

    [Test]
    public static void F6_EndeVerloren_SpaeteresPaketOhneBeginn()
    {
        var f = new Funkprobe();
        for (uint i = 0; i < 3; i++)
        {
            f.Senden(B, Sendeart.Netz, [7], phase: 3, frame: 100 + i);
            f.Jetzt += 20;
        }
        // Alle drei ENDE gehen verloren; nach 300 ms läuft der Takt, bei NETZ ohne Folgen.
        f.M.Leeren();
        f.Relay.Takt(f.Z, f.Jetzt + 300, f.M);
        Pruefe.Gleich(0, f.M.Enden.Count + f.M.Sprache.Count, "kein DurchsageEnde, kein erzeugtes ENDE bei NETZ");
        f.Jetzt += 400;
        f.Senden(B, Sendeart.Netz, [7], phase: 3, frame: 103);
        Pruefe.Gleich("A:NETZ[7] E:NETZ[7]", f.M.Empfang(true), "Paket 4 setzt fort, ohne BEGINN");

        // Nach mehr als 2 s ohne Paket ist die Phase geschlossen: dasselbe Paar beginnt neu.
        f.M.Leeren();
        f.Jetzt += Fristen.PhaseOffenMs + 1;
        f.Relay.Takt(f.Z, f.Jetzt, f.M);
        f.Senden(B, Sendeart.Netz, [7], phase: 3, frame: 200);
        Pruefe.Gleich("A:NETZ[7]+B E:NETZ[7]+B", f.M.Empfang(true), "nach 2 s neue Phase mit BEGINN");
    }

    [Test]
    public static void F7_AuswahlwechselBeiGehaltenerTaste_Serverseite()
    {
        // Die Entprellung (250 ms) ist Client-Regel C2. Geprüft wird, was der Server aus der Folge macht,
        // die ein regelkonformer Client sendet: Phase 9 an [7], ENDE dreifach, dann Phase 10 an [7] ohne Pre-roll.
        var f = new Funkprobe();
        uint frame = 100;
        for (var t = 0; t < 100; t += 20)
        {
            f.Senden(B, Sendeart.Netz, [7], phase: 9, frame: frame++);
            f.Jetzt += 20;
        }
        f.M.Leeren();
        for (var i = 0; i < 3; i++)
        {
            f.Senden(B, Sendeart.Netz, [7], phase: 9, frame: frame++, ende: true);
            f.Jetzt += 20;
        }
        Pruefe.Gleich("A:NETZ[7]+E A:NETZ[7]+E A:NETZ[7]+E E:NETZ[7]+E E:NETZ[7]+E E:NETZ[7]+E", f.M.Empfang(true),
            "ENDE-Kopien nur an die Bedienten");
        f.M.Leeren();
        f.Jetzt += 250;
        f.Senden(B, Sendeart.Netz, [7], phase: 10, frame: frame++);
        Pruefe.Gleich("A:NETZ[7]+B E:NETZ[7]+B", f.M.Empfang(true), "neue Phase an [7] mit BEGINN");
        Pruefe.Falsch(f.M.Sprache.Exists(s => (s.Stempel.Kennzeichen & Paketkennzeichen.Preroll) != 0), "ohne Pre-roll");
        Pruefe.Falsch(f.M.Sprache.Exists(s => s.Stempel.Empfaenger == Funkprobe.S(C)), "Netz 8 (C) bekommt nichts");
    }

    [Test]
    public static void F8_DurchsageStummFuerAlle_EndeUndDurchsageEnde()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Durchsage, [], phase: 4, frame: 100);
        Pruefe.Gleich(4, f.M.Starts.Count, "DurchsageStart an B, C, D, E");
        Pruefe.Wahr(f.Stumm(A, 0).Erfolg, "Admin schaltet A für alle stumm");
        f.M.Leeren();
        f.Jetzt += 20;
        var r = f.Senden(A, Sendeart.Durchsage, [], phase: 4, frame: 101);
        Pruefe.Wahr(r.Verworfen && r.Grund == SendeFehlerCode.Stummgeschaltet, "verworfen STUMMGESCHALTET");
        Pruefe.Gleich("B:DURCHSAGE[]+E+X C:DURCHSAGE[]+E+X D:DURCHSAGE[]+E+X E:DURCHSAGE[]+E+X", f.M.Empfang(true), "erzeugtes ENDE an alle");
        Pruefe.Gleich(4, f.M.Enden.Count, "DurchsageEnde an alle");
        Pruefe.Wahr(f.M.Enden.TrueForAll(x => x.Sprecher == A && x.Phase == 4), "DurchsageEnde{1001, 4}");
        f.M.Leeren();
        f.Jetzt += 20;
        Pruefe.Wahr(f.Senden(A, Sendeart.Durchsage, [], phase: 4, frame: 102).Verworfen, "weitere Pakete verworfen");
        Pruefe.Gleich(0, f.M.Sprache.Count + f.M.Enden.Count, "danach nichts mehr");
    }

    [Test]
    public static void F9_SendeauswahlFlut_Zusammengefasst()
    {
        var f = new Funkprobe();
        var fasser = new Zusammenfasser<byte[]>(f.Jetzt);
        int sofort = 0, ueberholt = 0, spaeter = 0;
        byte[] letzte = [];
        for (var i = 0; i < 300; i++)
        {
            f.Jetzt += 33; // 30 je Sekunde über 10 s
            letzte = [(byte)(i % 2)];
            if (fasser.Faellig(f.Jetzt, out var wartend))
            {
                Pruefe.Gleich(Fehlercode.Keiner, f.Schreiber.SendeauswahlSetzen(B, wartend!, false, f.Jetzt), "wartende angewendet");
                spaeter++;
            }
            if (fasser.Einreichen(letzte, f.Jetzt, out _, out var hatUeberholt) == Einreichung.Sofort)
            {
                Pruefe.Gleich(Fehlercode.Keiner, f.Schreiber.SendeauswahlSetzen(B, letzte, false, f.Jetzt), "sofort angewendet");
                sofort++;
            }
            if (hatUeberholt) ueberholt++;
        }
        while (fasser.Wartet)
        {
            f.Jetzt += 50;
            if (fasser.Faellig(f.Jetzt, out var wartend))
            {
                Pruefe.Gleich(Fehlercode.Keiner, f.Schreiber.SendeauswahlSetzen(B, wartend!, false, f.Jetzt), "letzte wartende angewendet");
                spaeter++;
            }
        }
        Pruefe.Wahr(ueberholt > 200, $"überzählige mit UEBERHOLT beantwortet ({ueberholt})");
        Pruefe.Wahr(sofort + spaeter is >= 45 and <= 52, $"angewendet im Takt des Topfs 4/s, Burst 8 ({sofort} sofort, {spaeter} später)");
        Pruefe.Gleich(300, sofort + ueberholt + spaeter, "jede Anfrage angewendet oder überholt, keine verworfen");
        var sendetIn = f.Schreiber.Komfort.SendetIn(f.Z, B);
        Pruefe.Wahr(sendetIn.SequenceEqual(new ushort[] { 8 }), $"am Ende gilt der letzte Stand in Praesenz.sendetIn ([{string.Join(",", sendetIn)}])");
    }

    [Test]
    public static void F10_AufstellungVerdraengtSelbstEingetragenen()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(E, null, Funkprobe.RolleCo, Funkprobe.Schiff3)).Erfolg, "E trägt sich als CO ein");
        var speichern = f.Tun(Auftrag.AufstellungSpeichern(Funkprobe.Admin, null, null, "Phase 2",
            [new AufstellungsEintrag { KontoId = B, RolleId = Funkprobe.RolleCo, SchiffId = Funkprobe.Schiff3 }]));
        Pruefe.Wahr(speichern.Erfolg, "Aufstellung gespeichert");
        var r = f.Tun(Auftrag.AufstellungAnwenden(Funkprobe.Admin, speichern.AufstellungsId, null, Aufstellungsmodus.Ergaenzen));
        Pruefe.Wahr(r.Erfolg, $"angewendet ({r})");
        Pruefe.Gleich(new Platz(Funkprobe.Schiff3, Funkprobe.RolleCo, f.Jetzt), f.Z.Konten[B].Platz, "B ist CO");
        Pruefe.Gleich(null, f.Z.Konten[E].Platz, "E ohne Platz");
        Pruefe.Gleich(Quelle.Standard, f.Z.Konten[E].Quelle, "E fällt auf STANDARD");
        Pruefe.Wahr(r.Verdraengt.SequenceEqual([E]), "verdraengt: [1005]");
    }

    [Test]
    public static void F11_TauschInEinerAnfrage()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(A, null, Funkprobe.RolleCo, Funkprobe.Schiff3)).Erfolg, "A ist CO");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, Funkprobe.RollePilot, Funkprobe.Schiff3)).Erfolg, "B ist Pilot");
        var einzeln = f.Tun(Auftrag.Verschieben(Funkprobe.Admin, [new Aenderung(A) { RolleId = Funkprobe.RollePilot, SchiffId = Funkprobe.Schiff3 }]));
        Pruefe.Gleich(Fehlercode.PlatzBelegt, einzeln.Fehler, "einzeln scheitert es an der Kapazität");
        var tausch = f.Tun(Auftrag.Verschieben(Funkprobe.Admin,
        [
            new Aenderung(A) { RolleId = Funkprobe.RollePilot, SchiffId = Funkprobe.Schiff3 },
            new Aenderung(B) { RolleId = Funkprobe.RolleCo, SchiffId = Funkprobe.Schiff3 },
        ]));
        Pruefe.Wahr(tausch.Erfolg, $"Tausch angenommen ({tausch})");
        Pruefe.Gleich(Funkprobe.RollePilot, f.Z.Konten[A].Platz!.Value.RolleId, "A ist Pilot");
        Pruefe.Gleich(Funkprobe.RolleCo, f.Z.Konten[B].Platz!.Value.RolleId, "B ist CO");
    }

    [Test]
    public static void F12_BasisVersionNurFuerBetroffeneKonten()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.FeldSetzen(D, 0, Funkprobe.Kommando)).Erfolg, "D bekommt ein Feld (Vorbereitung)");
        var v = f.Z.ZustandsVersion;
        Pruefe.Wahr(f.Tun(Auftrag.HoerenSetzen(D, Funkprobe.Kommando, false)).Erfolg, "D schaltet sein Hören um");
        var verschieben = Auftrag.Verschieben(Funkprobe.Admin, [new Aenderung(B) { FelderPatch = [new Feldpatch(null, Funkprobe.Kommando)] }], v);
        Pruefe.Wahr(f.Tun(verschieben).Erfolg, "Verschieben(B) angenommen");

        var v2 = f.Z.ZustandsVersion;
        Pruefe.Wahr(f.Tun(Auftrag.HoerenSetzen(B, Funkprobe.Crew, false)).Erfolg, "B schaltet sein Hören um");
        var veraltet = f.Tun(Auftrag.Verschieben(Funkprobe.Admin, [new Aenderung(B) { FelderPatch = [Feldpatch.Leer(0)] }], v2));
        Pruefe.Gleich(Fehlercode.Veraltet, veraltet.Fehler, "VERALTET");
        Pruefe.Wahr(veraltet.VeralteteKonten.Select(k => k.KontoId).SequenceEqual([B]), "details.konten [1002]");
        Pruefe.Gleich(f.Z.Konten[B].KontoVersion, veraltet.VeralteteKonten[0].KontoVersion, "mit kontoVersion");
    }

    [Test]
    public static void F13_HaeufigesVerdraengen_Sperre()
    {
        var f = new Funkprobe();
        const uint geraet1 = E * 10;
        const uint geraet2 = 77;
        uint sitzung = 20_000;
        var r1 = f.Tun(Auftrag.SitzungAktivieren(E, ++sitzung, geraet2));
        Pruefe.Wahr(r1.Erfolg && r1.BeendeteSitzungen.Single().Grund == Sitzungsende.Verdraengt, "1. Verdrängung");
        f.Jetzt += 60_000;
        var r2 = f.Tun(Auftrag.SitzungAktivieren(E, ++sitzung, geraet1));
        Pruefe.Wahr(r2.Erfolg && r2.Meldungen.IsEmpty, "2. Verdrängung ohne Meldung");
        f.Jetzt += 60_000;
        var r3 = f.Tun(Auftrag.SitzungAktivieren(E, ++sitzung, geraet2));
        Pruefe.Wahr(r3.Erfolg, "3. Verdrängung findet statt");
        Pruefe.Wahr(r3.Meldungen.Contains(new Meldung(MeldungsArt.VerdraengungHaeufig, E)), "beim dritten Mal VERDRAENGUNG_HAEUFIG");
        var laufend = f.Z.SitzungVonKonto(E);
        f.Jetzt += 60_000;
        var r4 = f.Tun(Auftrag.SitzungAktivieren(E, ++sitzung, geraet1));
        Pruefe.Gleich(Fehlercode.VerdraengenGesperrt, r4.Fehler, "weitere Verdrängung gesperrt");
        Pruefe.Gleich(laufend, f.Z.SitzungVonKonto(E), "die laufende Sitzung bleibt");
        // Dasselbe Gerät ersetzt ohne Zählung.
        var gleich = f.Tun(Auftrag.SitzungAktivieren(E, ++sitzung, geraet2));
        Pruefe.Wahr(gleich.Erfolg && gleich.BeendeteSitzungen.Single().Grund == Sitzungsende.Ersetzt, "ERSETZT bei gleichem Gerät");
        // Nach 30 min oder Admin-Entscheid wieder möglich.
        Pruefe.Wahr(f.Tun(Auftrag.VerdraengungFreigeben(Funkprobe.Admin, E)).Erfolg, "Admin entscheidet");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(E, ++sitzung, geraet1)).Erfolg, "danach wieder erlaubt");
    }
}
