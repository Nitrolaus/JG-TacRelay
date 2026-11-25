// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Gegenprüfung des Moduls weiterleitung: Befunde, die die ersten Tests nicht abdeckten, Prüfwerte 8.8 exakt
/// für die eigene TOC-Prüfung und Randfälle (leer, zu lang, Überlauf, Grenzen).
/// </summary>
internal static class WeiterleitungGegenpruefungTests
{
    private const uint Admin = Funkprobe.Admin;
    private const uint A = Funkprobe.A;
    private const uint B = Funkprobe.B;
    private const uint C = Funkprobe.C;
    private const uint D = Funkprobe.D;
    private const uint E = Funkprobe.E;
    private const ushort Co = Funkprobe.RolleCo;
    private const ushort Schiff = Funkprobe.Schiff3;

    // ---- 6.4 mit 5.6 und C4: ENDE dreifach, neue Phase läuft schon ----

    /// <summary>
    /// F1 wie ein regelkonformer Client ihn sendet: ENDE von Phase 5 dreimal im 20-ms-Abstand (5.6), Phase 6 setzt
    /// ohne Unterbrechung fort (C4). Die späten ENDE-Kopien von Phase 5 gehen nur an deren Bediente und beenden
    /// Phase 6 nicht; kein erzeugtes ENDE und kein zweites BEGINN für Phase 6.
    /// </summary>
    [Test]
    public static void F1_EndeDreifach_FortsetzungOhneFlattern()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Netz, [2, 7], phase: 5, frame: 100);
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [2, 7], phase: 5, frame: 101);
        Pruefe.Wahr(f.FeldNehmen(A, 1).Erfolg, "Admin nimmt A das Feld 1 (Netz 7)");
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [2, 7], phase: 5, frame: 102);
        f.M.Leeren();

        uint frame6 = 200;
        for (var i = 0; i < 3; i++)
        {
            f.Jetzt += 20;
            f.Senden(A, Sendeart.Netz, [2, 7], phase: 5, frame: 103 + (uint)i, ende: true);
            f.Senden(A, Sendeart.Netz, [2], phase: 6, frame: frame6++);
        }
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [2], phase: 6, frame: frame6);

        Pruefe.Falsch(f.M.Sprache.Exists(s => s.Stempel.Erzeugt), "kein erzeugtes ENDE, Phase 6 läuft ununterbrochen");
        var phase6 = f.M.Sprache.Where(s => s.Stempel.Sprechphase == 6).ToList();
        Pruefe.Gleich(8, phase6.Count, "4 Pakete von Phase 6 an C und E");
        Pruefe.Gleich(2, phase6.Count(s => (s.Stempel.Kennzeichen & Paketkennzeichen.Beginn) != 0), "BEGINN genau einmal je Empfänger");
        var ende5 = f.M.Sprache.Where(s => s.Stempel.Sprechphase == 5).ToList();
        Pruefe.Wahr(ende5.TrueForAll(s => (s.Stempel.Kennzeichen & Paketkennzeichen.Ende) != 0), "von Phase 5 nur noch ENDE-Kopien");
        Pruefe.Gleich("C,C,C,E,E,E", string.Join(",", ende5.Select(s => Funkprobe.Buchstabe(s.Stempel.Empfaenger)).Order(StringComparer.Ordinal)),
            "ENDE-Kopien von Phase 5 nur an ihre Bedienten C und E");
        Pruefe.Falsch(f.M.Sprache.Exists(s => s.Stempel.Empfaenger == Funkprobe.S(B)), "B bekommt nach seinem erzeugten ENDE nichts mehr (P-47)");
    }

    /// <summary>Dasselbe mit einer Durchsage: kein DurchsageEnde und kein zweites DurchsageStart für die neue Phase.</summary>
    [Test]
    public static void Durchsage_EndeDreifach_NeuePhase_BannerFlackertNicht()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 10);
        f.M.Leeren();
        for (var i = 0; i < 3; i++)
        {
            f.Jetzt += 20;
            f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 11 + (uint)i, ende: true);
            f.Senden(A, Sendeart.Durchsage, [2, 7], phase: 4, frame: 20 + (uint)i);
        }
        Pruefe.Gleich(3, f.M.Enden.Count(x => x.Phase == 3), "DurchsageEnde für Phase 3 einmal je Empfänger");
        Pruefe.Gleich(0, f.M.Enden.Count(x => x.Phase == 4), "kein DurchsageEnde für die laufende Phase 4");
        Pruefe.Gleich(3, f.M.Starts.Count(x => x.Phase == 4), "DurchsageStart für Phase 4 genau einmal je Empfänger (B, C, E)");
    }

    /// <summary>Ein ENDE einer Phase, die weder läuft noch gerade abgelegt wurde, beendet die laufende Phase nicht.</summary>
    [Test]
    public static void EndeFremderPhase_StoertLaufendePhaseNicht()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [7], phase: 8, frame: 1);
        f.M.Leeren();
        f.Jetzt += 20;
        var r = f.Senden(B, Sendeart.Netz, [7], phase: 2, frame: 2, ende: true);
        Pruefe.Falsch(r.Verworfen, "nicht verworfen");
        Pruefe.Gleich(0, f.M.Sprache.Count, "niemand bedient: keine Kopie, kein erzeugtes ENDE");
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], phase: 8, frame: 3);
        Pruefe.Gleich("A:NETZ[7] E:NETZ[7]", f.M.Empfang(true), "Phase 8 läuft ohne neues BEGINN weiter");
    }

    /// <summary>
    /// 6.4 "weicht ein Paket mit gleicher Nummer ab ... neue Phase": auch ein ENDE mit gleicher Nummer und anderen
    /// Zielen löst die laufende Phase ab; ihre Bedienten bekommen ein erzeugtes ENDE (fehlerhafter Client wie F5).
    /// </summary>
    [Test]
    public static void EndeGleicherNummerAndereZiele_BeendetLaufendePhase()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [7], phase: 9, frame: 1);
        f.M.Leeren();
        f.Jetzt += 20;
        var r = f.Senden(B, Sendeart.Netz, [8], phase: 9, frame: 2, ende: true);
        Pruefe.Falsch(r.Verworfen, "nicht verworfen");
        Pruefe.Gleich("A:NETZ[7]+E+X E:NETZ[7]+E+X", f.M.Empfang(true), "A und E ein erzeugtes ENDE [7], C (Netz 8) nichts");
    }

    /// <summary>Ein Rechteverwurf für ein spätes ENDE einer abgelegten Phase schließt die laufende Phase nicht.</summary>
    [Test]
    public static void Rechteverwurf_SpaetesEnde_SchliesstLaufendePhaseNicht()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [8], phase: 1, frame: 1);
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], phase: 2, frame: 2);
        Pruefe.Wahr(f.FeldNehmen(B, 1).Erfolg, "Admin nimmt B das Netz 8");
        f.M.Leeren();
        f.Jetzt += 20;
        var r = f.Senden(B, Sendeart.Netz, [8], phase: 1, frame: 3, ende: true);
        Pruefe.Wahr(r.Verworfen && r.Grund == SendeFehlerCode.ZielEntzogen, "spätes ENDE an Netz 8 verworfen");
        Pruefe.Falsch(f.M.Sprache.Exists(s => s.Stempel.Sprechphase == 2), "Phase 2 bekommt kein erzeugtes ENDE");
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], phase: 2, frame: 4);
        Pruefe.Gleich("A:NETZ[7] E:NETZ[7]", f.M.Empfang(true), "Phase 2 läuft ohne neues BEGINN");
    }

    /// <summary>Ein nach dem ENDE eintreffendes Paket derselben Durchsage (Umordnung) öffnet kein neues Banner.</summary>
    [Test]
    public static void Durchsage_UmgeordnetesPaketNachEnde_KeinNeuesDurchsageStart()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 1);
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 3, ende: true);
        f.M.Leeren();
        f.Jetzt += 5;
        f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 2);
        Pruefe.Gleich("B:DURCHSAGE[7] C:DURCHSAGE[7] E:DURCHSAGE[7]", f.M.Empfang(true), "spätes Paket ohne BEGINN an die Bedienten");
        Pruefe.Gleich(0, f.M.Starts.Count, "kein neues DurchsageStart nach dem ENDE");
    }

    // ---- 6.10: Vorrang der Aufstellung, aber nur auf gleiche oder niedrigere Stufe ----

    [Test]
    public static void Aufstellung_VorrangVerdraengtKeineHoehereStufe()
    {
        const uint admin2 = 1008;
        // A teilt im Auftrag ein (seit 0.3.0 hat RAIDLEITER allein kein ZUWEISEN).
        var f = new Funkprobe(b => b.MitKonto(new Konto { Id = admin2, Name = "Admin2", Stufe = Stufe.Admin }).MitSitzung(Funkprobe.S(admin2), admin2)).MitAuftrag(A);
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(admin2, null, Co, Schiff)).Erfolg, "Admin2 sitzt selbst gewählt als CO");
        var id = f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "B als CO",
            [new AufstellungsEintrag { KontoId = B, RolleId = Co, SchiffId = Schiff }])).AufstellungsId;

        var vorher = f.Z;
        var neben = f.Schreiber.Neben;
        var r = f.Tun(Auftrag.AufstellungAnwenden(A, id, null, Aufstellungsmodus.Ergaenzen));
        Pruefe.Gleich(Fehlercode.PlatzBelegt, r.Fehler, "die Einsatzleitung im Auftrag verdrängt keinen ADMIN (6.10), Platz bleibt belegt");
        Pruefe.Wahr(r.Fehlerliste.Length == 1 && r.Fehlerliste[0].KontoId == B, "Eintrag von B genannt");
        Pruefe.Wahr(ReferenceEquals(vorher, f.Z) && ReferenceEquals(neben, f.Schreiber.Neben), "ohne Seiteneffekt (P-45)");

        var mit = f.Tun(Auftrag.AufstellungAnwenden(A, id, null, Aufstellungsmodus.Ergaenzen, erzwingen: true));
        Pruefe.Wahr(mit.Erfolg && mit.Verdraengt.IsEmpty, "mit erzwingen überbucht, ohne den ADMIN zu verdrängen");
        Pruefe.Gleich(Co, f.Z.Konten[admin2].Platz!.Value.RolleId, "Admin2 bleibt CO");
        Pruefe.Gleich(Co, f.Z.Konten[B].Platz!.Value.RolleId, "B ist CO");

        // Gleiche Stufe wird weiterhin verdrängt: ein ADMIN wendet dieselbe Aufstellung an.
        var g = new Funkprobe(b => b.MitKonto(new Konto { Id = admin2, Name = "Admin2", Stufe = Stufe.Admin }).MitSitzung(Funkprobe.S(admin2), admin2));
        Pruefe.Wahr(g.Tun(Auftrag.EinsteigenRolleSchiff(admin2, null, Co, Schiff)).Erfolg, "Admin2 ist CO");
        var id2 = g.Tun(Auftrag.AufstellungSpeichern(Admin, null, null, "B als CO",
            [new AufstellungsEintrag { KontoId = B, RolleId = Co, SchiffId = Schiff }])).AufstellungsId;
        var admin = g.Tun(Auftrag.AufstellungAnwenden(Admin, id2, null, Aufstellungsmodus.Ergaenzen));
        Pruefe.Wahr(admin.Erfolg && admin.Verdraengt.SequenceEqual([admin2]), "ADMIN verdrängt ADMIN");
    }

    // ---- 6.2: ein fehlerhafter Auftrag reißt die Gruppe nicht mit ----

    [Test]
    public static void GruppenCommit_AusnahmeTrifftNurDenEinenAuftrag()
    {
        var f = new Funkprobe();
        var v = f.Z.ZustandsVersion;
        f.Schreiber.Einreihen(Auftrag.HoerenSetzen(B, 7, false));
        // Ein nicht initialisiertes Feld-Array (API-Fehler eines Aufrufers) wirft erst bei der Ausführung.
        f.Schreiber.Einreihen(Auftrag.Verschieben(Admin, [new Aenderung(C) { FelderPatch = default(ImmutableArray<Feldpatch>) }]));
        f.Schreiber.Einreihen(Auftrag.HoerenSetzen(E, 7, false));
        var ergebnisse = f.Schreiber.Abarbeiten(f.Jetzt);
        Pruefe.Gleich(3, ergebnisse.Count, "jeder Auftrag bekommt eine Antwort");
        Pruefe.Gleich(Fehlercode.Intern, ergebnisse[0].Ergebnis.Fehler, "der fehlerhafte Auftrag ergibt INTERN");
        Pruefe.Wahr(ergebnisse[0].Ergebnis.Ausnahme is not null, "die Ausnahme bleibt für das Protokoll erhalten");
        Pruefe.Wahr(ergebnisse[1].Ergebnis.Erfolg && ergebnisse[2].Ergebnis.Erfolg, "die übrigen Aufträge gelingen");
        Pruefe.Gleich(v + 1, f.Z.ZustandsVersion, "ein Austausch");
        Pruefe.Wahr(f.Z.Konten[C].Felder.SequenceEqual(Funkprobe.Grundzustand().Bauen().Konten[C].Felder), "C unverändert");
        Pruefe.Falsch(f.Z.Konten[B].FeldMitNetz(7)!.Value.Hoeren, "B hört 7 nicht mehr");
        Pruefe.Gleich(0, f.Schreiber.Wartend, "Warteschlange leer");
    }

    [Test]
    public static void Auftraege_PruefenArgumenteSofort()
    {
        Pruefe.Wirft<ArgumentNullException>(() => Auftrag.Verschieben(Admin, null!), "Verschieben ohne Liste");
        Pruefe.Wirft<ArgumentNullException>(() => Auftrag.SatzAnwenden(D, null, null, null!), "SatzAnwenden ohne Felder");
        Pruefe.Wirft<ArgumentNullException>(() => Auftrag.AufstellungSpeichern(A, null, null, "X", null!), "AufstellungSpeichern ohne Einträge");
        Pruefe.Wirft<ArgumentNullException>(() => Auftrag.NetzSetzen(Admin, null!, 1), "NetzSetzen ohne Netz");
        Pruefe.Wirft<ArgumentNullException>(() => Auftrag.KontoAnlegen(null!), "KontoAnlegen ohne Konto");
        // Spätere Änderungen an der übergebenen Liste wirken nicht mehr.
        var f = new Funkprobe();
        var liste = new List<Aenderung> { new(C) { FelderPatch = [Feldpatch.Leer(0)] } };
        var auftrag = Auftrag.Verschieben(Admin, liste);
        liste[0] = new Aenderung(Admin) { FelderPatch = [Feldpatch.Leer(0)] };
        Pruefe.Wahr(f.Tun(auftrag).BetroffeneKonten.SequenceEqual([C]), "Auftrag arbeitet mit der Kopie");
    }

    // ---- 4.8, 6.3: Ratenlimit nimmt keine Marke, wenn der andere Topf ablehnt ----

    [Test]
    public static void Rate_ByteTopfLehntAb_PaketMarkeBleibt()
    {
        var f = new Funkprobe();
        byte[] gross = [0xF8, .. new byte[399]];
        byte[] klein = [0xF8, 1, 2, 3];
        uint frame = 0;
        for (var i = 0; i < 10; i++)
            Pruefe.Falsch(f.Relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(B), frame++, 1, 0, Sendeart.Netz, [7], gross), f.Jetzt, f.M).Verworfen, "Byte-Burst 4000");
        for (var i = 0; i < 10; i++)
        {
            var r = f.Relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(B), frame++, 1, 0, Sendeart.Netz, [7], gross), f.Jetzt, f.M);
            Pruefe.Wahr(r.Verworfen && r.Grund == SendeFehlerCode.Grenze, "Byte-Topf leer: GRENZE");
        }
        f.Jetzt += 100; // 6 Paketmarken und 1600 Byte kommen hinzu
        var angenommen = 0;
        for (var i = 0; i < 30; i++)
            if (!f.Relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(B), frame++, 1, 0, Sendeart.Netz, [7], klein), f.Jetzt, f.M).Verworfen) angenommen++;
        Pruefe.Wahr(angenommen >= 15, $"die 10 vom Byte-Topf abgelehnten Pakete haben keine Paketmarke verbraucht ({angenommen} angenommen)");
    }

    // ---- 8.8 Opus-TOC (5.13), für die eigene Prüfung in Senderecht ----

    [Test]
    public static void Pruefwerte88_OpusToc()
    {
        (string Anfang, byte[] Opus, bool Annehmen)[] faelle =
        [
            ("f8", [0xF8], true),
            ("78", [0x78], true),
            ("f0", [0xF0], false),
            ("f1", [0xF1], true),
            ("fc", [0xFC], false),
            ("0b 02", [0x0B, 0x02], false),
            ("0b allein", [0x0B], false),
            ("f8 mit 400 Byte", [0xF8, .. new byte[399]], true),
            ("f8 mit 401 Byte", [0xF8, .. new byte[400]], false),
        ];
        foreach (var (anfang, opus, annehmen) in faelle)
        {
            Pruefe.Gleich(annehmen, Senderecht.OpusTocGueltig(opus), anfang + ": TOC");
            var form = Senderecht.FormPruefen(new Sprachpaket(1, 1, 1, 0, Sendeart.Netz, [2], opus));
            Pruefe.Gleich(annehmen ? SendeFehlerCode.Keiner : SendeFehlerCode.Ungueltig, form, anfang + ": Form");
        }
    }

    // ---- Randfälle Weiterleitung ----

    [Test]
    public static void Senderecht_ZuKleinePuffer_ArgumentException()
    {
        var f = new Funkprobe();
        ushort[] ziele = [2, 7, 8];
        Pruefe.Wirft<ArgumentException>(() =>
        {
            Span<ushort> erlaubt = stackalloc ushort[2];
            Span<SendeFehlerEintrag> gestrichen = stackalloc SendeFehlerEintrag[64];
            Senderecht.Pruefen(f.Z, new Sprachpaket(Funkprobe.S(A), 1, 1, 0, Sendeart.Netz, ziele, Funkprobe.Opus), f.Jetzt, erlaubt, gestrichen, out _, out _);
        }, "erlaubt kleiner als die Zielzahl");
        Pruefe.Wirft<ArgumentException>(() =>
        {
            Span<ushort> erlaubt = stackalloc ushort[64];
            Span<SendeFehlerEintrag> gestrichen = stackalloc SendeFehlerEintrag[2];
            Senderecht.Pruefen(f.Z, new Sprachpaket(Funkprobe.S(A), 1, 1, 0, Sendeart.Netz, ziele, Funkprobe.Opus), f.Jetzt, erlaubt, gestrichen, out _, out _);
        }, "gestrichen kleiner als die Zielzahl");
    }

    [Test]
    public static void SendeFehler_HoechstensSiebzehnEintraege()
    {
        var f = new Funkprobe();
        ushort[] unbekannt = [.. Enumerable.Range(100, 40).Select(i => (ushort)i)];
        var r = f.Senden(A, Sendeart.Durchsage, unbekannt);
        Pruefe.Wahr(r.Verworfen && r.Grund == SendeFehlerCode.UnbekanntesNetz, "alle Ziele unbekannt: ganz verworfen");
        Pruefe.Gleich(40, r.Gestrichen, "40 Streichungen gezählt");
        Pruefe.Gleich(Fristen.SendeFehlerEintraegeMax, f.M.Fehler.Single().Fehler.Length, "SendeFehler mit höchstens 17 Einträgen (4.5)");
        Pruefe.Gleich(40L, f.Relay.Gestrichen(SendeFehlerCode.UnbekanntesNetz), "jede Streichung gezählt");
    }

    [Test]
    public static void Durchsage_VierundsechzigZiele_Angenommen()
    {
        ushort[] netze = [.. Enumerable.Range(20, 64).Select(i => (ushort)i)];
        var bauer = new SchnappschussBauer();
        foreach (var n in netze) bauer.MitNetz(new Netz(n, $"N{n}"));
        var z = bauer
            .MitKonto(new Konto { Id = 1, Name = "Leiter", Stufe = Stufe.Raidleiter })
            .MitKonto(new Konto { Id = 2, Name = "Hörer", Felder = [new Mitgliedschaft(0, 83, false, false, false), new Mitgliedschaft(1, 20, false, false, false)] })
            .MitSitzung(11, 1)
            .MitSitzung(12, 2)
            .Bauen();
        var m = new Mitschrift();
        var r = new Weiterleiter().Verarbeiten(z, new Sprachpaket(11, 1, 1, 0, Sendeart.Durchsage, netze, Funkprobe.Opus), Funkprobe.Start, m);
        Pruefe.Falsch(r.Verworfen, "64 Durchsageziele angenommen");
        Pruefe.Gleich("83,20", string.Join(",", m.Sprache.Single().Netze), "Treffer in Feldfolge des Hörers, Hören egal");
    }

    [Test]
    public static void FrameNr_Ueberlauf_ErzeugtesEndeLaeuftAufNull()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [7], phase: 1, frame: uint.MaxValue);
        Pruefe.Wahr(f.FeldNehmen(E, 0).Erfolg, "E verliert Netz 7");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], phase: 1, frame: 0);
        var ende = f.M.Sprache.Single(s => s.Stempel.Erzeugt);
        Pruefe.Gleich(0u, ende.Stempel.FrameNr, "zuletzt gestempelt + 1 mod 2^32");
    }

    [Test]
    public static void Sprechphase_Ueberlauf_NeuePhaseMitBeginn()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [7], phase: ushort.MaxValue, frame: 1);
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], phase: 0, frame: 2);
        Pruefe.Gleich("A:NETZ[7]+B A:NETZ[7]+E+X E:NETZ[7]+B E:NETZ[7]+E+X", f.M.Empfang(true), "65535 -> 0 ist eine neue Phase");
    }

    /// <summary>
    /// F12 wörtlich im Grundzustand: D hat keine Felder, sein HoerenSetzen ergibt UNBEKANNT (4.5) und ändert nichts;
    /// Verschieben(B) mit basisVersion v wird angenommen. Die Erwartung gilt also auch ohne die Vorbereitung im
    /// Test F12 (D bekommt dort erst ein Feld, damit das Umschalten wirklich eine Version erzeugt).
    /// </summary>
    [Test]
    public static void F12_WoertlichImGrundzustand()
    {
        var f = new Funkprobe();
        var v = f.Z.ZustandsVersion;
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.HoerenSetzen(D, Funkprobe.Crew, true)).Fehler, "D ohne Mitgliedschaft");
        Pruefe.Gleich(v, f.Z.ZustandsVersion, "nichts geändert");
        var r = f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(B) { FelderPatch = [new Feldpatch(null, Funkprobe.Kommando)] }], v));
        Pruefe.Wahr(r.Erfolg, "Verschieben(B) mit basisVersion v angenommen");
    }

    // ---- Randfälle Zustandsschreiber ----

    [Test]
    public static void Verschieben_LeerZuLangDoppelt_Ungueltig()
    {
        var f = new Funkprobe();
        var vorher = f.Z;
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.Verschieben(Admin, [])).Fehler, "0 Änderungen");
        var zuViele = Enumerable.Range(0, Fristen.AenderungenMax + 1).Select(_ => new Aenderung(D) { SendeFelder = [] }).ToList();
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.Verschieben(Admin, zuViele)).Fehler, "201 Änderungen");
        var doppelt = f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { FelderPatch = [new Feldpatch(0, 2)] }, new Aenderung(D) { FelderPatch = [new Feldpatch(1, 7)] }]));
        Pruefe.Gleich(Fehlercode.Ungueltig, doppelt.Fehler, "dasselbe Konto zweimal");
        Pruefe.Wahr(doppelt.Fehlerliste.Single().Eintrag == 1, "zweiter Eintrag benannt");
        Pruefe.Wahr(ReferenceEquals(vorher, f.Z), "nichts geändert");
    }

    [Test]
    public static void AufstellungSpeichern_Grenzen()
    {
        var f = new Funkprobe().MitAuftrag(A);
        var zuViele = Enumerable.Range(0, Fristen.AufstellungEintraegeMax + 1).Select(i => new AufstellungsEintrag { DiscordId = $"{i}" }).ToList();
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "X", zuViele)).Fehler, "201 Einträge");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "", [])).Fehler, "leerer Name");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.AufstellungSpeichern(A, null, null, new string('x', 49), [])).Fehler, "Name 49 Zeichen");
        Pruefe.Wahr(f.Tun(Auftrag.AufstellungSpeichern(A, null, null, new string('x', 48), [])).Erfolg, "Name 48 Zeichen");
        // Längen zählen Unicode-Skalarwerte (4.3): 48 Zeichen außerhalb der BMP sind erlaubt.
        Pruefe.Wahr(f.Tun(Auftrag.AufstellungSpeichern(A, null, null, string.Concat(Enumerable.Repeat("\U0001F680", 48)), [])).Erfolg, "48 Skalarwerte außerhalb der BMP");
        var beides = f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "X", [new AufstellungsEintrag { KontoId = D, DiscordId = "1" }]));
        Pruefe.Gleich(Fehlercode.Ungueltig, beides.Fehler, "kontoId und discordId zugleich");
        var keines = f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "X", [new AufstellungsEintrag { RolleId = Co, SchiffId = Schiff }]));
        Pruefe.Gleich(Fehlercode.Ungueltig, keines.Fehler, "weder kontoId noch discordId");
        var siebzehn = f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "X",
            [new AufstellungsEintrag { KontoId = D, Felder = [.. Enumerable.Range(0, 17).Select(i => new Feldvorgabe((byte)i, 2, true, false))] }]));
        Pruefe.Gleich(Fehlercode.Ungueltig, siebzehn.Fehler, "17 Felder");
    }

    [Test]
    public static void KontoUndSitzung_Grenzwerte()
    {
        var f = new Funkprobe();
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 0, Name = "Null" })).Fehler, "kontoId 0");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.KontoAnlegen(new Konto { Id = (uint)int.MaxValue + 1, Name = "Gross" })).Fehler, "kontoId 2^31");
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = int.MaxValue, Name = "Rand" })).Erfolg, "kontoId 2^31-1");
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1400, Name = "Zwilling", DiscordId = "5" })).Erfolg, "erstes Konto zur Discord-ID");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1401, Name = "Zwilling2", DiscordId = "5" })).Fehler, "Discord-ID eindeutig");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.SitzungAktivieren(D, 0, 1)).Fehler, "Sitzungs-ID 0");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.SitzungAktivieren(D, Funkprobe.S(A), 1)).Fehler, "Sitzungs-ID schon vergeben");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.SitzungAktivieren(4242, 77_777, 1)).Fehler, "unbekanntes Konto");
        Pruefe.Wahr(f.Tun(Auftrag.HoerenSetzen(B, 1, true)).Erfolg, "Global einschalten ist ohne Wirkung erlaubt");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.Stummschalten(Admin, D, 7, -1)).Fehler, "negative Sekunden");
        Pruefe.Wahr(f.Tun(Auftrag.Stummschalten(Admin, D, 7, Fristen.StummMaxSekunden)).Erfolg, "3600 s");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.Stummschalten(Admin, D, 99, 10)).Fehler, "unbekanntes Netz");
    }

    [Test]
    public static void Konto_DefaultArrays_WerdenLeer_KeinAbsturzImRelay()
    {
        var k = new Konto { Id = 1500, Name = "Roh", Felder = default, Stumm = default, Entzuege = default, Entfallen = default, Sendeauswahl = null! };
        Pruefe.Falsch(k.Felder.IsDefault || k.Stumm.IsDefault || k.Entzuege.IsDefault || k.Entfallen.IsDefault, "leere statt nicht initialisierter Arrays");
        Pruefe.Wahr(ReferenceEquals(Sendeauswahl.Leer, k.Sendeauswahl), "leere Sendeauswahl");
        Pruefe.Falsch((k with { Stumm = default }).Stumm.IsDefault, "auch bei with");
        var f = new Funkprobe(b => b.MitKonto(k with { Stufe = Stufe.Raidleiter }).MitSitzung(1500, 1500));
        var r = f.Relay.Verarbeiten(f.Z, new Sprachpaket(1500, 1, 1, 0, Sendeart.Durchsage, [], Funkprobe.Opus), f.Jetzt, f.M);
        Pruefe.Falsch(r.Verworfen, "Durchsage an alle geht durch");
        Pruefe.Gleich(5, f.M.Sprache.Count, "A bis E");
    }

    [Test]
    public static void Namen_LaengeInSkalarwerten_KollisionBleibtUnterGrenze()
    {
        var f = new Funkprobe();
        var lang = new string('B', Fristen.NameMax);
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1600, Name = lang })).Erfolg, "32 Zeichen");
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1601, Name = lang })).Erfolg, "Kollision");
        var zweiter = f.Z.Konten[1601].Name;
        Pruefe.Gleich(Fristen.NameMax, zweiter.Length, "mit Zusatz höchstens 32 Zeichen");
        Pruefe.Wahr(zweiter.EndsWith(" (2)", StringComparison.Ordinal), "Zusatz (2)");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1602, Name = lang + "B" })).Fehler, "33 Zeichen");
        var raketen = string.Concat(Enumerable.Repeat("\U0001F680", Fristen.NameMax));
        Pruefe.Wahr(f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Name, D, name: raketen)).Erfolg, "32 Skalarwerte außerhalb der BMP");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Name, E, name: raketen + "\U0001F680")).Fehler, "33 Skalarwerte");
        Pruefe.Wahr(f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Name, E, name: raketen)).Erfolg, "Kollision mit Ersatzpaaren");
        var gekuerzt = f.Z.Konten[E].Name;
        Pruefe.Wahr(gekuerzt.EndsWith(" (2)", StringComparison.Ordinal) && !char.IsHighSurrogate(gekuerzt[^5]), "kein getrenntes Ersatzpaar");
        Pruefe.Gleich(Fristen.NameMax, gekuerzt.EnumerateRunes().Count(), "32 Skalarwerte mit Zusatz");
    }

    [Test]
    public static void Stumm_LaeuftGenauBeiBisAb()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Stumm(B, 7, 1).Erfolg, "1 s stumm");
        Pruefe.Wahr(f.Senden(B, Sendeart.Netz, [7], frame: 1).Verworfen, "stumm");
        f.Jetzt += 999;
        Pruefe.Wahr(f.Senden(B, Sendeart.Netz, [7], frame: 2).Verworfen, "nach 999 ms noch stumm");
        f.Jetzt += 1;
        Pruefe.Falsch(f.Senden(B, Sendeart.Netz, [7], phase: 2, frame: 3).Verworfen, "nach 1000 ms wieder frei");
    }
}
