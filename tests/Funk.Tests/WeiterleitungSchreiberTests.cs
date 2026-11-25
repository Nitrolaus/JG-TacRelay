// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Diagnostics;
using Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Zustandsschreiber (6.2, 6.7, 6.9, 6.10): P-09 Aufstellung atomar, P-19 Rang- und Rechteregeln, P-45 kein
/// Seiteneffekt ohne Recht, dazu Plätze, Felder, Sendeauswahl, Global und Verwaltung.
/// </summary>
internal static class WeiterleitungSchreiberTests
{
    private const uint Admin = Funkprobe.Admin;
    private const uint A = Funkprobe.A;
    private const uint B = Funkprobe.B;
    private const uint C = Funkprobe.C;
    private const uint D = Funkprobe.D;
    private const uint E = Funkprobe.E;
    private const uint Admin2 = 1008;
    private const uint Raidleiter2 = 1009;
    private const uint RlMitVerwaltung = 1010;
    private const ushort Co = Funkprobe.RolleCo;
    private const ushort Pilot = Funkprobe.RollePilot;
    private const ushort Schiff = Funkprobe.Schiff3;

    private static Funkprobe Rangprobe() => new(b => b
        .MitKonto(new Konto { Id = Admin2, Name = "Admin2", Stufe = Stufe.Admin })
        .MitKonto(new Konto { Id = Raidleiter2, Name = "RL2", Stufe = Stufe.Raidleiter })
        .MitKonto(new Konto { Id = RlMitVerwaltung, Name = "RLV", Stufe = Stufe.Raidleiter, Plus = Recht.KontenVerwalten })
        .MitSitzung(Funkprobe.S(Admin2), Admin2)
        .MitSitzung(Funkprobe.S(Raidleiter2), Raidleiter2));

    private static void OhneSeiteneffekt(Funkprobe f, Schnappschuss vorher, Nebenstand nebenVorher, Ergebnis r, Fehlercode erwartet, string was)
    {
        Pruefe.Gleich(erwartet, r.Fehler, was);
        Pruefe.Wahr(ReferenceEquals(vorher, f.Z), was + ": Schnappschuss unverändert");
        Pruefe.Wahr(ReferenceEquals(nebenVorher, f.Schreiber.Neben), was + ": Nebenstand unverändert");
    }

    // ---- P-09 ----

    [Test]
    public static void P09_AustauschZwischenPaketKundKplus1()
    {
        var f = new Funkprobe();
        var alt = f.Z;
        f.Senden(A, Sendeart.Netz, [2], frame: 1);
        Pruefe.Gleich("C:NETZ[2] E:NETZ[2]", f.M.Empfang(), "Paket k: alter Stand");
        var r = f.Tun(Auftrag.Verschieben(Admin,
        [
            new Aenderung(C) { FelderPatch = [Feldpatch.Leer(0)] },
            new Aenderung(D) { FelderPatch = [new Feldpatch(3, 2)] },
        ]));
        Pruefe.Wahr(r.Erfolg, "Umstellung zweier Konten");
        Pruefe.Gleich(alt.ZustandsVersion + 1, f.Z.ZustandsVersion, "eine neue Version");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [2], frame: 2);
        Pruefe.Gleich("C:NETZ[2]+E+X D:NETZ[2]+B E:NETZ[2]", f.M.Empfang(true), "Paket k+1: neuer Stand vollständig");
    }

    [Test]
    public static void P09_Nebenlaeufig_JedesPaketSiehtEinenGanzenStand()
    {
        const uint leiter = 2999;
        const int hoerer = 40;
        var bauer = new SchnappschussBauer()
            .MitNetz(new Netz(20, "Rot"))
            .MitNetz(new Netz(21, "Blau"))
            // Seit 0.3.0 teilt RAIDLEITER nur noch mit ZUWEISEN als Einzelrecht (fester Stellvertreter) oder im Auftrag ein.
            .MitKonto(new Konto { Id = leiter, Name = "Leiter", Stufe = Stufe.Raidleiter, Plus = Recht.Zuweisen, Felder = [Funkprobe.F(0, 20, true, true), Funkprobe.F(1, 21, true, true)] })
            .MitSitzung(1, leiter);
        for (uint i = 0; i < hoerer; i++)
        {
            bauer.MitKonto(new Konto { Id = 3000 + i, Name = $"H{i}" });
            bauer.MitSitzung(100 + i, 3000 + i);
        }
        var schreiber = new Zustandsschreiber(bauer.Bauen());
        AufstellungsEintrag Eintrag(uint k, ushort netz) => new() { KontoId = k, Felder = [new Feldvorgabe(0, netz, true, false)] };
        var rot = schreiber.Ausfuehren(Auftrag.AufstellungSpeichern(leiter, null, null, "Rot",
            [.. Enumerable.Range(0, hoerer).Select(i => Eintrag(3000 + (uint)i, 20))]), 0).AufstellungsId;
        var blau = schreiber.Ausfuehren(Auftrag.AufstellungSpeichern(leiter, null, null, "Blau",
            [.. Enumerable.Range(0, hoerer).Select(i => Eintrag(3000 + (uint)i, 21))]), 0).AufstellungsId;

        using var stopp = new CancellationTokenSource();
        var anwendungen = 0;
        var schreibThread = new Thread(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                var r = schreiber.Ausfuehren(Auftrag.AufstellungAnwenden(leiter, i % 2 == 0 ? rot : blau, null, Aufstellungsmodus.Ersetzen), i);
                if (r.Erfolg) anwendungen++;
            }
            stopp.Cancel();
        });
        schreibThread.Start();

        var relay = new Weiterleiter();
        var m = new Mitschrift();
        var zahlen = new HashSet<int>();
        long jetzt = 0;
        uint frame = 0;
        while (!stopp.IsCancellationRequested || frame < 100)
        {
            jetzt += 20;
            m.Leeren();
            var z = schreiber.Aktuell; // genau einmal je Paket
            relay.Verarbeiten(z, new Sprachpaket(1, frame++, 1, 0, Sendeart.Netz, [20], Funkprobe.Opus), jetzt, m);
            var normale = m.Sprache.Count(s => !s.Stempel.Erzeugt);
            Pruefe.Wahr(normale is 0 or hoerer, $"kein gemischter Stand: {normale} Kopien");
            zahlen.Add(normale);
        }
        schreibThread.Join();
        Pruefe.Gleich(300, anwendungen, "alle Anwendungen gelungen");
        Pruefe.Wahr(frame >= 100, "Pakete verarbeitet");
    }

    [Test]
    public static void P09_Validierungsfehler_AllesOderNichts()
    {
        var f = new Funkprobe();
        var gespeichert = f.Tun(Auftrag.AufstellungSpeichern(Admin, null, null, "Test",
        [
            new AufstellungsEintrag { KontoId = B, RolleId = Pilot, SchiffId = Schiff },
            new AufstellungsEintrag { KontoId = C, Felder = [new Feldvorgabe(0, 7, true, false)] },
            new AufstellungsEintrag { KontoId = D, RolleId = Co, SchiffId = Schiff },
            new AufstellungsEintrag { KontoId = E, Felder = [new Feldvorgabe(1, 8, true, false)] },
        ]));
        Pruefe.Wahr(gespeichert.Erfolg, "gespeichert");
        // Danach verschwinden Rolle CO und Netz 8: zwei Einträge werden ungültig.
        Pruefe.Wahr(f.Tun(Auftrag.RolleLoeschen(Admin, Co, f.Z.NetzListeVersion)).Erfolg, "Rolle gelöscht");
        Pruefe.Wahr(f.Tun(Auftrag.NetzLoeschen(Admin, 8, f.Z.NetzListeVersion)).Erfolg, "Netz gelöscht");
        var vorher = f.Z;
        var neben = f.Schreiber.Neben;
        var r = f.Tun(Auftrag.AufstellungAnwenden(Admin, gespeichert.AufstellungsId, null, Aufstellungsmodus.Ergaenzen));
        OhneSeiteneffekt(f, vorher, neben, r, Fehlercode.Unbekannt, "abgelehnt");
        Pruefe.Gleich("2:Unbekannt 3:Unbekannt", string.Join(" ", r.Fehlerliste.Select(e => $"{e.Eintrag}:{e.Code}")), "vollständige Fehlerliste");

        var v = f.Tun(Auftrag.Verschieben(Admin,
        [
            new Aenderung(B) { FelderPatch = [new Feldpatch(0, 99)] },
            new Aenderung(C) { RolleId = Pilot },
            new Aenderung(D) { FelderPatch = [new Feldpatch(2, 2)] },
        ]));
        OhneSeiteneffekt(f, vorher, neben, v, Fehlercode.Unbekannt, "Verschieben abgelehnt");
        Pruefe.Gleich("0:Unbekannt 1:Ungueltig", string.Join(" ", v.Fehlerliste.Select(e => $"{e.Eintrag}:{e.Code}")), "Fehlerliste Verschieben");
    }

    [Test]
    public static void P09_Kapazitaet_AufstellungUeberbucht_OhneUndMitErzwingen()
    {
        var f = new Funkprobe();
        var id = f.Tun(Auftrag.AufstellungSpeichern(Admin, null, null, "Zu viele CO",
        [
            new AufstellungsEintrag { KontoId = B, RolleId = Co, SchiffId = Schiff },
            new AufstellungsEintrag { KontoId = C, RolleId = Co, SchiffId = Schiff },
        ])).AufstellungsId;
        var vorher = f.Z;
        var r = f.Tun(Auftrag.AufstellungAnwenden(Admin, id, null, Aufstellungsmodus.Ergaenzen));
        OhneSeiteneffekt(f, vorher, f.Schreiber.Neben, r, Fehlercode.PlatzBelegt, "PLATZ_BELEGT");
        Pruefe.Gleich(2, r.Fehlerliste.Length, "beide Einträge genannt");
        var mit = f.Tun(Auftrag.AufstellungAnwenden(Admin, id, null, Aufstellungsmodus.Ergaenzen, erzwingen: true));
        Pruefe.Wahr(mit.Erfolg && mit.Anzahl == 2, "mit erzwingen angewendet");
    }

    [Test]
    public static void P09_Version_FehlendUndVeraltet()
    {
        var f = new Funkprobe();
        var v1 = f.Tun(Auftrag.AufstellungSpeichern(Admin, null, null, "Phase 1", [new AufstellungsEintrag { KontoId = D, Felder = [new Feldvorgabe(0, 7, true, false)] }]));
        var v2 = f.Tun(Auftrag.AufstellungSpeichern(Admin, v1.AufstellungsId, v1.AufstellungsVersion, "Phase 1",
            [new AufstellungsEintrag { KontoId = D, Felder = [new Feldvorgabe(0, 8, true, false)] }]));
        Pruefe.Gleich(2u, v2.AufstellungsVersion, "Version 2");
        var veraltetGespeichert = f.Tun(Auftrag.AufstellungSpeichern(Admin, v1.AufstellungsId, 1, "alt", []));
        Pruefe.Gleich(Fehlercode.Veraltet, veraltetGespeichert.Fehler, "Speichern mit alter Version: VERALTET");

        var vorher = f.Z;
        var alt = f.Tun(Auftrag.AufstellungAnwenden(Admin, v1.AufstellungsId, 1, Aufstellungsmodus.Ergaenzen));
        OhneSeiteneffekt(f, vorher, f.Schreiber.Neben, alt, Fehlercode.Veraltet, "veraltete Version");
        var ohne = f.Tun(Auftrag.AufstellungAnwenden(Admin, v1.AufstellungsId, null, Aufstellungsmodus.Ergaenzen));
        Pruefe.Wahr(ohne.Erfolg && ohne.AufstellungsVersion == 2, "ohne Version gilt die aktuelle");
        Pruefe.Gleich((ushort)8, f.Z.Konten[D].Felder.Single().NetzId, "Version 2 angewendet");
        Pruefe.Wahr(f.Z.Konten[D].Felder.Single().Fest, "von der Aufstellung gesetzte Felder sind fest");
    }

    [Test]
    public static void P09_Ersetzen_OhneAnfragendenUndOhneZuweisen()
    {
        var f = new Funkprobe().MitAuftrag(A);
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(A, null, Co, Schiff)).Erfolg, "A (Raidleiter mit Auftrag) ist CO");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, Pilot, Schiff)).Erfolg, "B ist Pilot");
        var id = f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "Nur D", [new AufstellungsEintrag { KontoId = D, Felder = [new Feldvorgabe(0, 2, true, true)] }])).AufstellungsId;
        var r = f.Tun(Auftrag.AufstellungAnwenden(A, id, null, Aufstellungsmodus.Ersetzen));
        Pruefe.Wahr(r.Erfolg, "ERSETZEN angewendet");
        Pruefe.Wahr(r.Zurueckgesetzt.SequenceEqual([B]), "nur B fällt zurück");
        Pruefe.Gleich(Co, f.Z.Konten[A].Platz!.Value.RolleId, "der Anfragende behält seinen Platz");
        Pruefe.Gleich(null, f.Z.Konten[B].Platz, "B ohne Platz");
        Pruefe.Gleich(Quelle.Standard, f.Z.Konten[B].Quelle, "B auf STANDARD");
    }

    private sealed class Speicher : IZustandsSpeicher
    {
        public bool Werfen { get; set; }
        public bool NachDemSchreibenWerfen { get; set; }
        public Schnappschuss? Gespeichert { get; private set; }
        public Nebenstand? Neben { get; private set; }

        public void Speichern(Schnappschuss schnappschuss, Nebenstand nebenstand)
        {
            if (Werfen) throw new IOException("Platte voll");
            Gespeichert = schnappschuss;
            Neben = nebenstand;
            if (NachDemSchreibenWerfen) throw new IOException("Absturz nach dem Schreiben, vor dem Austausch");
        }
    }

    [Test]
    public static void P09_AbsturzZwischenSpeichernUndAustausch()
    {
        var speicher = new Speicher { Werfen = true };
        var schreiber = new Zustandsschreiber(Funkprobe.Grundzustand().Bauen(), null, speicher);
        var vorher = schreiber.Aktuell;
        var r = schreiber.Ausfuehren(Auftrag.Verschieben(Admin, [new Aenderung(C) { FelderPatch = [Feldpatch.Leer(0)] }]), 1);
        Pruefe.Gleich(Fehlercode.Intern, r.Fehler, "INTERN");
        Pruefe.Wahr(ReferenceEquals(vorher, schreiber.Aktuell), "alter Stand bleibt in Kraft");

        speicher.Werfen = false;
        speicher.NachDemSchreibenWerfen = true;
        r = schreiber.Ausfuehren(Auftrag.Verschieben(Admin, [new Aenderung(C) { FelderPatch = [Feldpatch.Leer(0)] }]), 2);
        Pruefe.Gleich(Fehlercode.Intern, r.Fehler, "Absturz nach dem Schreiben");
        Pruefe.Wahr(ReferenceEquals(vorher, schreiber.Aktuell), "im Speicher nicht ausgetauscht");
        var gespeichert = speicher.Gespeichert!;
        Pruefe.Gleich(0, gespeichert.Pruefen().Count, "gespeicherter Stand ist stimmig");
        Pruefe.Gleich(vorher.ZustandsVersion + 1, gespeichert.ZustandsVersion, "mit der neuen Version");
        // Neustart aus dem gespeicherten Stand: die Änderung ist ganz da.
        var neu = new Zustandsschreiber(gespeichert, speicher.Neben);
        Pruefe.Gleich(null, neu.Aktuell.Konten[C].FeldNummer(0), "Änderung vollständig");
    }

    [Test]
    public static void P09_GruppenCommit_VorrangFuerZuweisen()
    {
        var f = new Funkprobe();
        var v = f.Z.ZustandsVersion;
        f.Schreiber.Einreihen(Auftrag.HoerenSetzen(B, 7, false));
        f.Schreiber.Einreihen(Auftrag.FeldSetzen(D, 0, Funkprobe.Kommando));
        f.Schreiber.Einreihen(Auftrag.Verschieben(Admin, [new Aenderung(D) { FelderPatch = [new Feldpatch(0, 7, Fest: true)] }], v));
        var ergebnisse = f.Schreiber.Abarbeiten(f.Jetzt);
        Pruefe.Gleich("Verschieben HoerenSetzen FeldSetzen", string.Join(" ", ergebnisse.Select(e => e.Auftrag.Art)), "ZUWEISEN zuerst");
        Pruefe.Wahr(ergebnisse[0].Ergebnis.Erfolg, "Verschieben mit basisVersion v gelingt, weil es vor D's Änderung läuft");
        Pruefe.Gleich(Fehlercode.FeldFest, ergebnisse[2].Ergebnis.Fehler, "FeldSetzen trifft das feste Feld");
        Pruefe.Gleich(v + 1, f.Z.ZustandsVersion, "ein Austausch für die ganze Gruppe");
        Pruefe.Wahr(ergebnisse.Where(e => e.Ergebnis.Erfolg).All(e => e.Ergebnis.ZustandsVersion == v + 1), "Antworten nennen die neue Version");
    }

    [Test]
    public static void P09_Aufstellung40_UnterHoerenSetzenLast()
    {
        const uint leiter = 2999;
        var bauer = new SchnappschussBauer()
            .MitNetz(new Netz(20, "Rot") { Offen = Offenheit.HoerenSenden })
            .MitNetz(new Netz(21, "Blau"))
            // Seit 0.3.0 teilt RAIDLEITER nur noch mit ZUWEISEN als Einzelrecht (fester Stellvertreter) oder im Auftrag ein.
            .MitKonto(new Konto { Id = leiter, Name = "Leiter", Stufe = Stufe.Raidleiter, Plus = Recht.Zuweisen })
            .MitSitzung(1, leiter);
        for (uint i = 0; i < 40; i++)
        {
            bauer.MitKonto(new Konto { Id = 3000 + i, Name = $"H{i}", Felder = [Funkprobe.F(0, 20, true, true)] });
            bauer.MitSitzung(100 + i, 3000 + i);
        }
        var schreiber = new Zustandsschreiber(bauer.Bauen());
        var id = schreiber.Ausfuehren(Auftrag.AufstellungSpeichern(leiter, null, null, "Phase 2",
            [.. Enumerable.Range(0, 40).Select(i => new AufstellungsEintrag
            {
                KontoId = 3000 + (uint)i,
                Felder = [new Feldvorgabe(0, 20, true, true), new Feldvorgabe(1, 21, true, true)],
            })]), 0).AufstellungsId;

        double Messen(long t)
        {
            for (uint i = 0; i < 40; i++) schreiber.Einreihen(Auftrag.HoerenSetzen(3000 + i, 20, t % 2 == 0));
            schreiber.Einreihen(Auftrag.AufstellungAnwenden(leiter, id, null, Aufstellungsmodus.Ergaenzen));
            for (uint i = 0; i < 40; i++) schreiber.Einreihen(Auftrag.HoerenSetzen(3000 + i, 20, t % 2 != 0));
            var uhr = Stopwatch.StartNew();
            var ergebnisse = schreiber.Abarbeiten(t);
            uhr.Stop();
            Pruefe.Gleich("AufstellungAnwenden", ergebnisse[0].Auftrag.Art, "Aufstellung vor den Selbständerungen");
            Pruefe.Wahr(ergebnisse.All(e => e.Ergebnis.Erfolg), "alle gelungen");
            return uhr.Elapsed.TotalMilliseconds;
        }

        for (var i = 0; i < 5; i++) Messen(i);
        var zeiten = Enumerable.Range(10, 20).Select(i => Messen(i)).Order().ToList();
        var median = zeiten[zeiten.Count / 2];
        Pruefe.Wahr(schreiber.Aktuell.Konten.Values.Where(k => k.Id >= 3000).All(k => k.FeldMitNetz(21) is not null), "alle 40 hören Blau");
        Pruefe.Wahr(median < 10, $"Serverzeit für 40 Konten plus 80 HoerenSetzen unter 10 ms (Median {median:F2} ms)");
    }

    // ---- 6.5 Global ----

    [Test]
    public static void Global_Pflicht_UndVerwaltungGeschuetzt()
    {
        var f = new Funkprobe();
        Pruefe.Gleich(Fehlercode.GlobalPflicht, f.Tun(Auftrag.HoerenSetzen(B, 1, false)).Fehler, "HoerenSetzen(1, false)");
        var v = f.Z.NetzListeVersion;
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.NetzLoeschen(Admin, 1, v)).Fehler, "Global nicht löschbar");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.NetzSetzen(Admin, new Netz(1, "Global"), v)).Fehler, "Global nicht umtypisierbar");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.NetzSetzen(Admin, new Netz(30, "Zweites") { Typ = NetzTyp.Global }, v)).Fehler, "kein zweites GLOBAL");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.FeldSetzen(D, 0, 1)).Fehler, "Global ist kein Feld");
        Pruefe.Wahr(f.Z.Netze[1] is { NurTaste: true, AusloeserStandard: Ausloeser.Taste }, "Erstkonfiguration: nurTaste, TASTE");
        Pruefe.Wahr(f.Z.HoerendeSitzungen(1).Length == 5, "alle aktiven Sitzungen hören Global");
    }

    // ---- 6.9 Plätze, Felder, Sendeauswahl ----

    [Test]
    public static void Standard_AusNetzlisteNachReihenfolge()
    {
        var f = new Funkprobe(b => b
            .MitNetz(new Netz(30, "Allgemein") { Reihenfolge = 5, Standard = true, Offen = Offenheit.HoerenSenden })
            .MitNetz(new Netz(31, "Info") { Reihenfolge = 1, Standard = true, Offen = Offenheit.Hoeren })
            .MitNetz(new Netz(32, "Lobby") { Reihenfolge = 9, Standard = true, Offen = Offenheit.HoerenSenden }));
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1100, Name = "Neu", DiscordId = "42" })).Erfolg, "angelegt");
        var k = f.Z.Konten[1100];
        Pruefe.Gleich("0:31:h- 1:30:hs 2:32:hs", string.Join(" ", k.Felder.Select(m => $"{m.Feld}:{m.NetzId}:{(m.Hoeren ? "h" : "-")}{(m.Senden ? "s" : "-")}")),
            "Felder ab 0 nach reihenfolge, senden bei HOEREN_SENDEN");
        Pruefe.Falsch(k.Felder.Any(m => m.Fest), "nicht fest");
        Pruefe.Gleich("[1]", k.Sendeauswahl.ToString(), "Sendeauswahl = erstes Feld mit senden");
    }

    [Test]
    public static void FeldSetzen_Regeln()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.FeldSetzen(D, 3, Funkprobe.Kommando)).Erfolg, "offenes Netz");
        Pruefe.Gleich(new Mitgliedschaft(3, 2, true, true, false), f.Z.Konten[D].Felder.Single(), "hören, senden bei HOEREN_SENDEN");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.FeldSetzen(D, 4, 7)).Fehler, "offen NEIN");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.FeldSetzen(D, 4, Funkprobe.Kommando)).Fehler, "Netz schon in anderem Feld");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.FeldSetzen(D, 4, 77)).Fehler, "unbekanntes Netz");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.FeldSetzen(D, 16, Funkprobe.Kommando)).Fehler, "Feld 16");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { FelderPatch = [new Feldpatch(5, 7)] }])).Erfolg, "Raidleiter setzt festes Feld");
        Pruefe.Gleich(Fehlercode.FeldFest, f.Tun(Auftrag.FeldSetzen(D, 5, null)).Fehler, "festes Feld nicht leeren");
        Pruefe.Gleich(Fehlercode.FeldFest, f.Tun(Auftrag.FeldSetzen(D, 5, Funkprobe.Kommando)).Fehler, "festes Feld nicht überschreiben");
        Pruefe.Wahr(f.Tun(Auftrag.HoerenSetzen(D, 7, false)).Erfolg, "Hören im festen Feld abschaltbar (A17)");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.HoerenSetzen(D, 8, false)).Fehler, "Hören ohne Mitgliedschaft");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { FelderPatch = [new Feldpatch(5, 7, Fest: false)] }])).Erfolg, "fest aufheben");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.FeldSetzen(D, 5, null)).Fehler, "auch danach leert man nicht selbst (seit 0.8.1, Q44)");
    }

    [Test]
    public static void Rolle_DoppelteNetzeZusammenfassen_FestGewinnt()
    {
        var rolle = new Rolle(50, "Doppelt")
        {
            Felder =
            [
                new RollenFeld(2, 7, null, true, true, false, false),
                new RollenFeld(4, 7, null, true, false, false, true),
                new RollenFeld(1, 8, null, true, false, false, false),
                new RollenFeld(6, 8, null, false, false, false, false),
            ],
        };
        var felder = Feldregeln.AusRolle(rolle, null, new Funkprobe().Z.Netze).Select(r => r.Mitgliedschaft).ToList();
        Pruefe.Gleich("1:8 4:7", string.Join(" ", felder.Select(m => $"{m.Feld}:{m.NetzId}")), "festes Feld gewinnt, sonst das niedrigere");
    }

    [Test]
    public static void Selbstwahl_FesteFelderBleiben_KollisionRuecktNach_Entfallen()
    {
        var f = new Funkprobe(b => b.MitRolle(new Rolle(60, "Viel")
        {
            SelbstWaehlbar = true,
            Felder =
            [
                new RollenFeld(0, 2, null, true, true, true, false),
                new RollenFeld(1, 7, null, true, false, false, false),
                new RollenFeld(15, 8, null, true, false, false, false),
            ],
        }));
        // Der Raidleiter hat D die festen Felder 0 (Netz 7) und 15 (Netz 2) gesetzt.
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { Felder = [new Feldvorgabe(0, 7, true, false), new Feldvorgabe(15, Funkprobe.Kommando, true, false)] }])).Erfolg,
            "feste Felder 0 und 15");
        var r = f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, 60, null));
        Pruefe.Wahr(r.Erfolg, $"Selbstwahl ({r})");
        var k = f.Z.Konten[D];
        Pruefe.Gleich("0:7:F 15:2:F", string.Join(" ", k.Felder.Select(m => $"{m.Feld}:{m.NetzId}:{(m.Fest ? "F" : "-")}")),
            "Netz 2 und Netz 7 gehen in den festen Feldern auf, Netz 8 an Feld 15 hat keinen Platz mehr");
        Pruefe.Gleich("15:8:KEIN_FREIES_FELD", string.Join(" ", k.Entfallen.Select(e => $"{e.Feld}:{e.NetzId}:{e.Grund}")), "entfallen");
        Pruefe.Gleich(Quelle.Rolle, k.Quelle, "quelle ROLLE");
    }

    [Test]
    public static void Selbstwahl_RueckenAufNaechstesFreiesFeld()
    {
        var f = new Funkprobe(b => b.MitRolle(new Rolle(61, "Drei")
        {
            SelbstWaehlbar = true,
            Felder = [new RollenFeld(0, 2, null, true, true, true, false), new RollenFeld(1, 8, null, true, false, false, false)],
        }));
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { Felder = [new Feldvorgabe(0, 7, true, false)] }])).Erfolg, "festes Feld 0");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, 61, null)).Erfolg, "Selbstwahl");
        var k = f.Z.Konten[D];
        Pruefe.Gleich("0:7 1:8 2:2", string.Join(" ", k.Felder.Select(m => $"{m.Feld}:{m.NetzId}")), "Rollenfeld 0 rückt hinter Feld 1");
        Pruefe.Gleich("[2]", k.Sendeauswahl.ToString(), "Sendevorschlag folgt dem Feld");
    }

    [Test]
    public static void Selbstwahl_Pruefungen()
    {
        var f = new Funkprobe(b => b.MitRolle(new Rolle(62, "Gesperrt") { SelbstWaehlbar = false }).MitRolle(new Rolle(63, "Frei") { SelbstWaehlbar = true, Kapazitaet = 1 }))
            .MitAuftrag(A);
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, 62, null)).Fehler, "nicht selbst wählbar");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Co, null)).Fehler, "brauchtSchiff ohne schiffId");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, 63, Schiff)).Fehler, "Rolle nicht in plaetze");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, 63, null)).Erfolg, "Rolle ohne Schiff");
        Pruefe.Gleich(Fehlercode.PlatzBelegt, f.Tun(Auftrag.EinsteigenRolleSchiff(E, null, 63, null)).Fehler, "Kapazität ohne Schiff");
        // "Ganz sperren": Mit "Wechsel sperren" dürfte B ohne Platz als Nachzügler noch einsteigen (F9, RechteBeauftragungTests).
        Pruefe.Wahr(f.Tun(Auftrag.SelbstwahlSetzen(A, false, f.Z.NetzListeVersion, nachzuegler: false)).Erfolg, "die Einsatzleitung sperrt die Selbstwahl (ZUWEISEN im Auftrag)");
        Pruefe.Gleich(Fehlercode.SelbstwahlGesperrt, f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, Pilot, Schiff)).Fehler, "SELBSTWAHL_GESPERRT");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, Pilot, Schiff, erzwingen: true)).Fehler, "erzwingen nur mit ZUWEISEN");
    }

    [Test]
    public static void Sendeauswahl_OhneMehrfachNurNiedrigstes()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin,
        [
            new Aenderung(B) { SendeFelder = [0, 1] },
            new Aenderung(A) { SendeFelder = [0, 1] },
        ])).Erfolg, "sendeFelder gesetzt");
        Pruefe.Gleich("[0]", f.Z.Konten[B].Sendeauswahl.ToString(), "Funker: nur das niedrigste markierte Feld");
        Pruefe.Gleich("[0,1]", f.Z.Konten[A].Sendeauswahl.ToString(), "Raidleiter mit MEHRFACH_SENDEN: beide");
        var v = f.Z.ZustandsVersion;
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Schreiber.SendeauswahlSetzen(B, [0, 1], false, f.Jetzt), "SendeauswahlSetzen mehrfach ohne Recht");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Schreiber.SendeauswahlSetzen(B, [], true, f.Jetzt), "Global ohne GLOBAL_SENDEN");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Schreiber.SendeauswahlSetzen(C, [0], false, f.Jetzt), "Feld ohne senden");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Schreiber.SendeauswahlSetzen(A, [1, 0], false, f.Jetzt), "nicht aufsteigend");
        Pruefe.Gleich(Fehlercode.Keiner, f.Schreiber.SendeauswahlSetzen(A, [0, 1], true, f.Jetzt), "Raidleiter: Global und zwei Felder");
        Pruefe.Wahr(f.Schreiber.Komfort.SendetIn(f.Z, A).SequenceEqual(new ushort[] { 1, 2, 7 }), "sendetIn Global zuerst, dann Feldfolge");
        Pruefe.Gleich(v, f.Z.ZustandsVersion, "Komfortzustand ohne Austausch");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(A) { SendeFelder = [1] }])).Erfolg, "Umstellung setzt neue Sendeauswahl");
        Pruefe.Wahr(f.Schreiber.Komfort.SendetIn(f.Z, A).SequenceEqual(new ushort[] { 7 }), "nach der Umstellung gilt der Vorschlag des Servers");
    }

    [Test]
    public static void SatzAnwenden_UeberspringtUndBehaeltFeste()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { FelderPatch = [new Feldpatch(0, 8)] }])).Erfolg, "festes Feld 0 mit Netz 8");
        var r = f.Tun(Auftrag.SatzAnwenden(D, Pilot, Schiff,
        [
            new Feldvorgabe(0, 2, true, true),
            new Feldvorgabe(1, 2, true, true),
            new Feldvorgabe(2, 7, true, true),
            new Feldvorgabe(3, 99, true, false),
        ]));
        Pruefe.Wahr(r.Erfolg, $"angewendet ({r})");
        Pruefe.Gleich("0:FeldFest 3:Unbekannt", string.Join(" ", r.Uebersprungen.Select(u => $"{u.Eintrag}:{u.Code}")), "übersprungen und gemeldet");
        var k = f.Z.Konten[D];
        // Seit 0.8.1 (Q44) kommen die Felder aus der Rolle, aus dem Satz nur "hören" für Netze, die darin liegen.
        Pruefe.Gleich("0:8 1:7 2:2", string.Join(" ", k.Felder.Select(m => $"{m.Feld}:{m.NetzId}")), "festes Feld bleibt, die übrigen wie in der Rolle");
        Pruefe.Gleich(Quelle.Satz, k.Quelle, "quelle SATZ");
        Pruefe.Gleich(Pilot, k.Platz!.Value.RolleId, "Rolle übernommen");
        var ohneRolle = f.Tun(Auftrag.SatzAnwenden(E, null, null, [new Feldvorgabe(0, 7, true, false)]));
        Pruefe.Gleich("", string.Join(" ", ohneRolle.Uebersprungen.Select(u => $"{u.Eintrag}:{u.Code}")), "Netz 7 liegt bei E schon in einem Feld: nur hören gilt (seit 0.8.1)");
        var fremd = f.Tun(Auftrag.SatzAnwenden(E, null, null, [new Feldvorgabe(9, 8, true, false)]));
        Pruefe.Gleich("9:KeinRecht", string.Join(" ", fremd.Uebersprungen.Select(u => $"{u.Eintrag}:{u.Code}")), "Netz 8 ist nicht offen: kein Satz legt es selbst hinein");
    }

    [Test]
    public static void Plaetze_BelegtReserviertFrei()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, Pilot, Schiff)).Erfolg, "B wählt selbst");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(Admin, C, Co, Schiff)).Erfolg, "Admin setzt C");
        Pruefe.Gleich(PlatzZustand.Belegt, f.Z.PlatzZustandVon(B, f.Jetzt), "online: BELEGT");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(B))).Erfolg && f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(C))).Erfolg, "beide trennen");
        Pruefe.Gleich(PlatzZustand.Belegt, f.Z.PlatzZustandVon(B, f.Jetzt + Fristen.WiederverbindungMs - 1), "in der Frist BELEGT");
        Pruefe.Gleich(PlatzZustand.Frei, f.Z.PlatzZustandVon(B, f.Jetzt + Fristen.WiederverbindungMs), "selbst gewählt verfällt mit der Frist");
        Pruefe.Gleich(PlatzZustand.Reserviert, f.Z.PlatzZustandVon(C, f.Jetzt + Fristen.ReservierungMs - 1), "ZUWEISEN: 12 h RESERVIERT");
        Pruefe.Gleich(PlatzZustand.Frei, f.Z.PlatzZustandVon(C, f.Jetzt + Fristen.ReservierungMs), "danach frei");
        f.Jetzt += Fristen.WiederverbindungMs;
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Erfolg, "Platz von B ist nach der Frist frei");
        Pruefe.Gleich(Fehlercode.PlatzBelegt, f.Tun(Auftrag.EinsteigenRolleSchiff(E, null, Co, Schiff)).Fehler, "Platz von C bleibt reserviert");
    }

    [Test]
    public static void Anmelden_Wiederherstellen_Reservierung_Standard()
    {
        var f = new Funkprobe(b => b.MitNetz(new Netz(30, "Allgemein") { Standard = true, Offen = Offenheit.HoerenSenden }));
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, Pilot, Schiff)).Erfolg, "B wählt Pilot");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(Admin, C, Co, Schiff)).Erfolg, "Admin setzt C");
        f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(B)));
        f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(C)));
        f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(E)));

        f.Jetzt += 60_000;
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(B, 40_001, B * 10)).Erfolg, "B in der Frist zurück");
        Pruefe.Gleich(Quelle.Wiederhergestellt, f.Z.Konten[B].Quelle, "WIEDERHERGESTELLT");
        Pruefe.Gleich(Pilot, f.Z.Konten[B].Platz!.Value.RolleId, "Platz still wiederhergestellt");

        f.Jetzt += Fristen.WiederverbindungMs;
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(C, 40_002, C * 10)).Erfolg, "C nach der Frist, aber reserviert");
        // Seit 0.11.0 kommt eine Reservierung durch ZUWEISEN als VERSCHIEBEN mit durch zurück (bis 0.10.0 WIEDERHERGESTELLT): So zeigt
        // auch ein alter Client ", eingeteilt von X".
        Pruefe.Gleich(Quelle.Verschieben, f.Z.Konten[C].Quelle, "Reservierung durch ZUWEISEN: VERSCHIEBEN");
        Pruefe.Gleich(Admin, f.Z.Konten[C].QuelleDurch, "durch den Admin");
        Pruefe.Gleich(Co, f.Z.Konten[C].Platz!.Value.RolleId, "Platz still wiederhergestellt");

        var alt = f.Z.Konten[E];
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(E, 40_003, E * 10)).Erfolg, "E nach der Frist");
        var e = f.Z.Konten[E];
        Pruefe.Gleich(Quelle.Standard, e.Quelle, "STANDARD");
        Pruefe.Gleich("0:30", string.Join(" ", e.Felder.Select(m => $"{m.Feld}:{m.NetzId}")), "Felder aus STANDARD");
        Pruefe.Wahr(e.LetzterStand is { } ls && ls.Felder.SequenceEqual(alt.Felder), "letzterStand nennt den früheren Stand");
    }

    [Test]
    public static void Nachziehen_OfflineUndUnverknuepft()
    {
        var f = new Funkprobe().MitAuftrag(A);
        f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(D)));
        var id = f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "Einsatz",
        [
            new AufstellungsEintrag { KontoId = D, RolleId = Pilot, SchiffId = Schiff },
            new AufstellungsEintrag { DiscordId = "777", Felder = [new Feldvorgabe(0, 7, true, true)] },
            new AufstellungsEintrag { DiscordId = "888", Felder = [new Feldvorgabe(0, 7, true, true)] },
        ])).AufstellungsId;
        var r = f.Tun(Auftrag.AufstellungAnwenden(A, id, null, Aufstellungsmodus.Ergaenzen));
        Pruefe.Wahr(r.Offline.SequenceEqual([D]), "offline: [D]");
        Pruefe.Wahr(r.Unverknuepft.SequenceEqual(["777", "888"]), "unverknuepft");
        Pruefe.Gleich(PlatzZustand.Reserviert, f.Z.PlatzZustandVon(D, f.Jetzt), "RESERVIERT ohne Sitzung");

        f.Jetzt += 60_000;
        var anmeldung = f.Tun(Auftrag.SitzungAktivieren(D, 41_000, D * 10));
        Pruefe.Wahr(anmeldung.Meldungen.Contains(new Meldung(MeldungsArt.AufstellungNachgezogen, D, A)), "AUFSTELLUNG_NACHGEZOGEN bei Anmeldung");
        Pruefe.Gleich(Quelle.Aufstellung, f.Z.Konten[D].Quelle, "quelle AUFSTELLUNG");

        var neu = f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1200, Name = "Neuling", DiscordId = "777" }));
        Pruefe.Wahr(neu.Meldungen.Contains(new Meldung(MeldungsArt.AufstellungNachgezogen, 1200, A)), "Nachziehen bei Verknüpfung");
        Pruefe.Gleich((ushort)7, f.Z.Konten[1200].Felder.Single().NetzId, "Eintrag angewendet");

        f.Jetzt += Fristen.NachziehenMs + 1;
        var spaet = f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1201, Name = "Spät", DiscordId = "888" }));
        Pruefe.Wahr(spaet.Meldungen.IsEmpty, "nach 12 h nichts mehr nachgezogen");
    }

    [Test]
    public static void PlaetzeFreigeben_NurOhneSitzungUndNichtHoeher()
    {
        var f = Rangprobe().MitAuftrag(A);
        f.Tun(Auftrag.EinsteigenRolleSchiff(Admin, B, Pilot, Schiff));
        f.Tun(Auftrag.EinsteigenRolleSchiff(Admin, Admin2, Co, Schiff));
        f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(B)));
        f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(Admin2)));
        var r = f.Tun(Auftrag.PlaetzeFreigeben(A));
        Pruefe.Wahr(r.Erfolg && r.Anzahl == 1, "nur B (Admin2 ist höher)");
        Pruefe.Gleich(null, f.Z.Konten[B].Platz, "B frei");
        Pruefe.Gleich(Co, f.Z.Konten[Admin2].Platz!.Value.RolleId, "Admin2 behält");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.PlaetzeFreigeben(C)).Fehler, "Funker ohne ZUWEISEN");
    }

    // ---- 6.10, P-19, P-45 ----

    /// <summary>
    /// Rangmatrix seit 0.3.0 (6.10, 6.12), seit 0.5.0 mit dem Funker-Modus (6.15): Einteilen darf nur ein ADMIN (auch ohne
    /// Admin-Sitzung, nicht im Funker-Modus) oder wer einen Auftrag hat; RAIDLEITER allein (Führung) nicht mehr. ZUWEISEN wirkt auf
    /// FUNKER und RAIDLEITER, auf ADMIN-Konten nur durch einen ADMIN oder solange der ADMIN als Funker spielt; Stummschalten auf
    /// ADMIN-Konten bleibt beim ADMIN, auch im Funker-Modus. Ein abgelaufener Auftrag zählt nicht. Je Kombination und Auftrag ein
    /// frischer Stand, ohne Seiteneffekt bei KEIN_RECHT.
    /// </summary>
    [Test]
    public static void P19_RangJeKombination_ZuweisenNurAdminOderAuftrag_AufAdminNurAdminOderFunkerModus()
    {
        const long Stunden12 = 12L * 60 * 60 * 1000;
        // (Name, Konto, Auftrag: 0 keiner, 1 laufend, 2 abgelaufen, Stufe, im Funker-Modus)
        (string Name, uint Id, int Auftrag, Stufe Stufe, bool Funker)[] anfragende =
        [
            ("FUNKER", C, 0, Stufe.Funker, false),
            ("FUNKER mit Auftrag", C, 1, Stufe.Funker, false),
            ("FUNKER mit abgelaufenem Auftrag", C, 2, Stufe.Funker, false),
            ("RAIDLEITER", A, 0, Stufe.Raidleiter, false),
            ("RAIDLEITER mit Auftrag", A, 1, Stufe.Raidleiter, false),
            ("ADMIN ohne Admin-Sitzung", Admin, 0, Stufe.Admin, false),
            ("ADMIN im Funker-Modus", Admin, 0, Stufe.Admin, true),
        ];
        (string Name, uint Id, Stufe Stufe, bool Funker)[] ziele =
        [
            ("FUNKER", D, Stufe.Funker, false), ("RAIDLEITER", Raidleiter2, Stufe.Raidleiter, false),
            ("ADMIN", Admin2, Stufe.Admin, false), ("ADMIN im Funker-Modus", Admin2, Stufe.Admin, true),
        ];
        var auftraege = new (string Name, bool Stumm, Func<uint, uint, Auftrag> Bauen)[]
        {
            ("Verschieben", false, (a, z) => Auftrag.Verschieben(a, [new Aenderung(z) { FelderPatch = [new Feldpatch(9, 7)] }])),
            ("Stummschalten", true, (a, z) => Auftrag.Stummschalten(a, z, 7, 30)),
            ("EinsteigenFuerAndere", false, (a, z) => Auftrag.EinsteigenRolleSchiff(a, z, Pilot, Schiff)),
        };
        var faelle = 0;
        foreach (var (anfName, anf, auftrag, anfStufe, anfFunker) in anfragende)
        {
            foreach (var (zielName, ziel, zielStufe, zielFunker) in ziele)
            {
                foreach (var (name, stumm, bauen) in auftraege)
                {
                    var f = Rangprobe();
                    if (auftrag != 0) f.MitAuftrag(anf);
                    // Funker-Modus 24 h: Er läuft auch beim abgelaufenen Auftrag (12 h später) noch.
                    if (anfFunker) Pruefe.Wahr(f.Tun(Auftrag.FunkermodusSetzen(anf, true, f.Jetzt + 2 * Stunden12)).Erfolg, "Anfragender als Funker");
                    if (zielFunker) Pruefe.Wahr(f.Tun(Auftrag.FunkermodusSetzen(ziel, true, f.Jetzt + 2 * Stunden12)).Erfolg, "Ziel als Funker");
                    if (auftrag == 2) f.Jetzt += Stunden12;
                    var vorher = f.Z;
                    var r = f.Tun(bauen(anf, ziel));
                    var fall = $"{name} {anfName} -> {zielName}";
                    var hatZuweisen = (anfStufe == Stufe.Admin && !anfFunker) || auftrag == 1;
                    var rang = zielStufe != Stufe.Admin || anfStufe == Stufe.Admin || (zielFunker && !stumm);
                    if (hatZuweisen && rang) Pruefe.Wahr(r.Erfolg, fall + " erlaubt: " + r);
                    else OhneSeiteneffekt(f, vorher, f.Schreiber.Neben, r, Fehlercode.KeinRecht, fall + " KEIN_RECHT");
                    faelle++;
                }
            }
        }
        Pruefe.Gleich(84, faelle, "7 Anfragende x 4 Ziele x 3 Aufträge");
    }

    [Test]
    public static void P19_StummAufheben_Regeln()
    {
        // Wer stummschaltet, braucht ZUWEISEN: hier je ein Auftrag für die beiden RAIDLEITER-Konten.
        var f = Rangprobe().MitAuftrag(A, Raidleiter2);
        Pruefe.Wahr(f.Tun(Auftrag.Stummschalten(Raidleiter2, D, 7, 60)).Erfolg, "RL2 schaltet D stumm");
        Pruefe.Wahr(f.Tun(Auftrag.Stummschalten(Raidleiter2, A, 7, 60)).Erfolg, "RL2 schaltet A stumm");
        var vorher = f.Z;
        OhneSeiteneffekt(f, vorher, f.Schreiber.Neben, f.Tun(Auftrag.Stummschalten(A, A, 7, 0)), Fehlercode.KeinRecht, "Betroffener hebt fremde nicht auf");
        OhneSeiteneffekt(f, vorher, f.Schreiber.Neben, f.Tun(Auftrag.Stummschalten(A, A, 7, 1)), Fehlercode.KeinRecht, "auch nicht durch Überschreiben");
        OhneSeiteneffekt(f, vorher, f.Schreiber.Neben, f.Tun(Auftrag.Stummschalten(A, D, 7, 0)), Fehlercode.KeinRecht, "gleiche Stufe wie der Setzende");
        Pruefe.Wahr(f.Tun(Auftrag.Stummschalten(Admin2, D, 7, 0)).Erfolg, "höhere Stufe als der Setzende");
        Pruefe.Wahr(f.Tun(Auftrag.Stummschalten(Raidleiter2, A, 7, 0)).Erfolg, "wer sie gesetzt hat");
        Pruefe.Wahr(f.Tun(Auftrag.Stummschalten(Admin, Admin2, 0, 60)).Erfolg, "Admin schaltet Admin stumm");
        Pruefe.Wahr(f.Tun(Auftrag.Stummschalten(Admin, Admin2, 0, 0)).Erfolg, "ein ADMIN hebt auf");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.Stummschalten(Admin, D, 7, 3601)).Fehler, "höchstens 3600 s");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.Stummschalten(Admin, D, 7, 0)).Fehler, "nichts aufzuheben");
    }

    [Test]
    public static void P19_KontoVerwaltung_Regeln1bis6()
    {
        var f = Rangprobe();
        var vorher = f.Z;
        var neben = f.Schreiber.Neben;
        // (1) niemand vergibt ein Recht, das er nicht hat.
        OhneSeiteneffekt(f, vorher, neben, f.Tun(Auftrag.KontoVerwaltung(RlMitVerwaltung, KontoOperation.Rechte, D, rechte: Recht.NetzeVerwalten)),
            Fehlercode.KeinRecht, "Regel 1");
        // (2) niemand ändert die eigene Stufe oder die eigenen Rechte.
        OhneSeiteneffekt(f, vorher, neben, f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Stufe, Admin, stufe: Stufe.Funker)), Fehlercode.KeinRecht, "Regel 2 Stufe");
        OhneSeiteneffekt(f, vorher, neben, f.Tun(Auftrag.KontoVerwaltung(RlMitVerwaltung, KontoOperation.Rechte, RlMitVerwaltung, rechte: Recht.Keine)),
            Fehlercode.KeinRecht, "Regel 2 Rechte");
        // (3) ADMIN-Konten nur durch ADMIN.
        foreach (var op in new[] { KontoOperation.Sperren, KontoOperation.Name, KontoOperation.Loeschen, KontoOperation.Stufe })
        {
            OhneSeiteneffekt(f, vorher, neben, f.Tun(Auftrag.KontoVerwaltung(RlMitVerwaltung, op, Admin2, stufe: Stufe.Funker, name: "X")),
                Fehlercode.KeinRecht, $"Regel 3 {op}");
        }
        OhneSeiteneffekt(f, vorher, neben, f.Tun(Auftrag.KontoVerwaltung(RlMitVerwaltung, KontoOperation.Stufe, D, stufe: Stufe.Admin)),
            Fehlercode.KeinRecht, "niemand außer ADMIN macht zum ADMIN");
        // Funker ohne KONTEN_VERWALTEN.
        OhneSeiteneffekt(f, vorher, neben, f.Tun(Auftrag.KontoVerwaltung(C, KontoOperation.Sperren, D)), Fehlercode.KeinRecht, "ohne KONTEN_VERWALTEN");

        // (1) erlaubt: ein Recht, das er hat. (6) Meldung.
        var plus = f.Tun(Auftrag.KontoVerwaltung(RlMitVerwaltung, KontoOperation.Rechte, D, rechte: Recht.SprechanzeigeAlle));
        Pruefe.Wahr(plus.Erfolg, "Recht vergeben, das er selbst hat");
        Pruefe.Wahr(plus.Meldungen.Contains(new Meldung(MeldungsArt.RechteGeaendert, D, RlMitVerwaltung)), "Regel 6: RECHTE_GEAENDERT");
        Pruefe.Gleich(Recht.SprechanzeigeAlle, f.Z.Konten[D].Plus, "sichtbare Abweichung plus");
        // (5) STUFE setzt die Rechte zurück.
        Pruefe.Wahr(f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Stufe, D, stufe: Stufe.Raidleiter)).Erfolg, "Stufe RAIDLEITER");
        var d = f.Z.Konten[D];
        Pruefe.Wahr(d.Plus == Recht.Keine && d.Minus == Recht.Keine && d.Rechte == Rechteregeln.Standard(Stufe.Raidleiter), "Regel 5: Abweichungen zurückgesetzt");
        Pruefe.Wahr(f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Rechte, D, rechte: Rechteregeln.Standard(Stufe.Raidleiter) & ~Recht.GlobalSenden)).Erfolg, "minus");
        Pruefe.Gleich(Recht.GlobalSenden, f.Z.Konten[D].Minus, "sichtbare Abweichung minus");
        // Privilegiert: Geräte fallen auf WARTET.
        var priv = f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Rechte, E, rechte: Recht.GeraeteFreigeben));
        Pruefe.Wahr(priv.Erfolg && priv.GeraeteZurueckstufen, "privilegiert: Geräte auf WARTET");
    }

    [Test]
    public static void P19_LetzterAdmin_NurAdminWerkzeug()
    {
        var f = new Funkprobe();
        var vorher = f.Z;
        OhneSeiteneffekt(f, vorher, f.Schreiber.Neben, f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Sperren, Admin)), Fehlercode.NurAdminWerkzeug, "letzten Admin sperren");
        OhneSeiteneffekt(f, vorher, f.Schreiber.Neben, f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Loeschen, Admin)), Fehlercode.NurAdminWerkzeug, "letzten Admin löschen");
        var g = Rangprobe();
        Pruefe.Wahr(g.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Stufe, Admin2, stufe: Stufe.Raidleiter)).Erfolg, "zweiten Admin herabstufen");
        var sperren = g.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Sperren, B));
        Pruefe.Wahr(sperren.BeendeteSitzungen.Single() == new BeendeteSitzung(Funkprobe.S(B), B, Sitzungsende.KontoGesperrt), "Sperre beendet die Sitzung");
        Pruefe.Falsch(g.Z.IstOnline(B), "B nicht mehr aktiv");
        Pruefe.Gleich(Fehlercode.KontoGesperrt, g.Tun(Auftrag.SitzungAktivieren(B, 50_000, B * 10)).Fehler, "gesperrtes Konto kommt nicht an");
    }

    [Test]
    public static void P45_OhneRechtKeinSeiteneffekt()
    {
        var f = new Funkprobe();
        var v = f.Z.NetzListeVersion;
        var id = f.Tun(Auftrag.AufstellungSpeichern(A, null, null, "X", [])).AufstellungsId;
        var vorher = f.Z;
        var neben = f.Schreiber.Neben;
        (string, Auftrag)[] faelle =
        [
            ("Verschieben", Auftrag.Verschieben(C, [new Aenderung(D) { FelderPatch = [Feldpatch.Leer(0)] }])),
            ("Stummschalten", Auftrag.Stummschalten(C, D, 7, 10)),
            ("PlaetzeFreigeben", Auftrag.PlaetzeFreigeben(C)),
            ("EinsteigenFuerAndere", Auftrag.EinsteigenRolleSchiff(C, D, Pilot, Schiff)),
            ("AufstellungSpeichern", Auftrag.AufstellungSpeichern(C, null, null, "Y", [])),
            ("AufstellungAnwenden", Auftrag.AufstellungAnwenden(C, id, null, Aufstellungsmodus.Ersetzen)),
            ("AufstellungLoeschen", Auftrag.AufstellungLoeschen(C, id, 1)),
            ("KontoVerwaltung", Auftrag.KontoVerwaltung(A, KontoOperation.Sperren, D)),
            ("NetzSetzen", Auftrag.NetzSetzen(A, new Netz(40, "Neu"), v)),
            ("NetzLoeschen", Auftrag.NetzLoeschen(A, 7, v)),
            ("SchiffLoeschen", Auftrag.SchiffLoeschen(A, Schiff, v)),
            ("RolleLoeschen", Auftrag.RolleLoeschen(A, Co, v)),
            ("EinstellungSetzen", Auftrag.EinstellungSetzen(A, false, v)),
            ("SelbstwahlSetzen", Auftrag.SelbstwahlSetzen(C, false, v)),
            ("VerdraengungFreigeben", Auftrag.VerdraengungFreigeben(A, E)),
            ("Rang: Raidleiter verschiebt Admin", Auftrag.Verschieben(A, [new Aenderung(Admin) { FelderPatch = [new Feldpatch(0, 7)] }])),
            ("Rang: Raidleiter schaltet Admin stumm", Auftrag.Stummschalten(A, Admin, 0, 10)),
        ];
        foreach (var (was, auftrag) in faelle)
            OhneSeiteneffekt(f, vorher, neben, f.Tun(auftrag), Fehlercode.KeinRecht, was);
    }

    // ---- Verwaltung ----

    [Test]
    public static void Verwaltung_NetzLoeschen_MitgliedschaftenUndZielEntzogen()
    {
        var f = new Funkprobe();
        var v = f.Z.NetzListeVersion;
        Pruefe.Gleich(Fehlercode.Veraltet, f.Tun(Auftrag.NetzLoeschen(Admin, 7, v - 1)).Fehler, "NetzListe-Version veraltet");
        var r = f.Tun(Auftrag.NetzLoeschen(Admin, 7, v));
        Pruefe.Wahr(r.Erfolg && r.NetzListeGeaendert, "gelöscht");
        Pruefe.Wahr(r.BetroffeneKonten.Order().SequenceEqual([A, B, C, E]), "Mitgliedschaften-Push an die Betroffenen");
        Pruefe.Wahr(f.Z.Konten.Values.All(k => k.FeldMitNetz(7) is null), "keine Mitgliedschaft mehr");
        Pruefe.Wahr(f.Z.Rollen.Values.All(ro => ro.Felder.All(x => x.NetzId != 7)) && f.Z.Schiffe[Schiff].Netze.Values.All(n => n != 7), "Rollen und Schiffe bereinigt");
        Pruefe.Gleich(f.Z.ZustandsVersion, f.Z.NetzListeVersion, "NetzListe-Version neu");
        f.Senden(A, Sendeart.Netz, [2, 7]);
        Pruefe.Wahr(f.M.Fehler.Single().Fehler.Single().Code == SendeFehlerCode.UnbekanntesNetz, "Netz 7 unbekannt");
        Pruefe.Wahr(f.Z.Pruefen().Count == 0, "Stand stimmig");
    }

    [Test]
    public static void Verwaltung_SelbstwahlAuchMitZuweisen_EinstellungNur_NetzeVerwalten()
    {
        var f = new Funkprobe();
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.SelbstwahlSetzen(A, false, f.Z.NetzListeVersion)).Fehler, "Führung ohne Auftrag sperrt nicht");
        f.MitAuftrag(A);
        Pruefe.Wahr(f.Tun(Auftrag.SelbstwahlSetzen(A, false, f.Z.NetzListeVersion)).Erfolg, "SELBSTWAHL mit ZUWEISEN im Auftrag");
        Pruefe.Falsch(f.Z.Einstellungen.Selbstwahl, "gesperrt");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.EinstellungSetzen(A, false, f.Z.NetzListeVersion)).Fehler, "EINSTELLUNG nur NETZE_VERWALTEN");
        Pruefe.Wahr(f.Tun(Auftrag.EinstellungSetzen(Admin, false, f.Z.NetzListeVersion)).Erfolg, "Admin");
        Pruefe.Wahr(!f.Z.Einstellungen.DurchsageNurTaste, "durchsageNurTaste aus");
        Pruefe.Falsch(f.Senden(A, Sendeart.Durchsage, [], va: true).Verworfen, "Durchsage per VA jetzt erlaubt");
    }

    [Test]
    public static void Aufstellung_VerdraengtNurSoVieleWieNoetig_SelbstGewaehlteZuerst()
    {
        const ushort gunner = 12;
        var f = new Funkprobe(b => b
            .MitRolle(new Rolle(gunner, "Gunner") { SelbstWaehlbar = true, BrauchtSchiff = true, Felder = [new RollenFeld(0, 0, "CREW", true, true, true, false)] })
            .MitSchiff(new Schiff(Schiff, "Schiff 3")
            {
                Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
                    [new KeyValuePair<string, ushort>("CREW", 7), new KeyValuePair<string, ushort>("BRUECKE", 8)]),
                Plaetze = [new SchiffsPlatz(Co, 1), new SchiffsPlatz(Pilot, 1), new SchiffsPlatz(gunner, 2)],
            }));
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, gunner, Schiff)).Erfolg, "B wählt selbst");
        f.Jetzt += 1000;
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(Admin, C, gunner, Schiff)).Erfolg, "Admin setzt C");
        var id = f.Tun(Auftrag.AufstellungSpeichern(Admin, null, null, "D als Gunner",
            [new AufstellungsEintrag { KontoId = D, RolleId = gunner, SchiffId = Schiff }])).AufstellungsId;
        var r = f.Tun(Auftrag.AufstellungAnwenden(Admin, id, null, Aufstellungsmodus.Ergaenzen));
        Pruefe.Wahr(r.Erfolg && r.Verdraengt.SequenceEqual([B]), "genau einer weicht, der selbst gewählte");
        Pruefe.Gleich(gunner, f.Z.Konten[C].Platz!.Value.RolleId, "C bleibt");
        Pruefe.Gleich(gunner, f.Z.Konten[D].Platz!.Value.RolleId, "D ist Gunner");
        Pruefe.Wahr(r.BetroffeneKonten.Order().SequenceEqual([B, D]), "Mitgliedschaften an D und den Verdrängten");
    }

    [Test]
    public static void KontoVersion_NurBeiWeiterleitungsrelevanterAenderung()
    {
        var f = new Funkprobe();
        var a = f.Z.Konten[A].KontoVersion;
        Pruefe.Wahr(f.Tun(Auftrag.HoerenSetzen(B, 7, false)).Erfolg, "HoerenSetzen");
        Pruefe.Gleich(f.Z.ZustandsVersion, f.Z.Konten[B].KontoVersion, "kontoVersion = zustandsVersion der Änderung");
        Pruefe.Gleich(a, f.Z.Konten[A].KontoVersion, "andere Konten unverändert");
        var b = f.Z.Konten[B].KontoVersion;
        Pruefe.Wahr(f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Name, B, name: "Bravo")).Erfolg, "Name");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(B))).Erfolg, "Sitzungsende");
        Pruefe.Gleich(b, f.Z.Konten[B].KontoVersion, "Name und Sitzungsende sind nicht weiterleitungsrelevant für basisVersion");
        Pruefe.Gleich("Bravo", f.Z.Konten[B].Name, "Name gesetzt");
        var vorher = f.Z;
        Pruefe.Wahr(f.Tun(Auftrag.HoerenSetzen(A, 2, true)).Erfolg && ReferenceEquals(vorher, f.Z), "keine Änderung, kein Austausch");
    }

    [Test]
    public static void Verwaltung_RollenUndSchiffe_Pruefungen()
    {
        var f = new Funkprobe();
        var v = f.Z.NetzListeVersion;
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.RolleSetzen(Admin, new Rolle(70, "X") { Felder = [new RollenFeld(0, 7, "CREW", true, true, false, false)] }, v)).Fehler,
            "Feld mit netzId und schiffsnetz");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.RolleSetzen(Admin, new Rolle(70, "X") { Felder = [new RollenFeld(0, 1, null, true, true, false, false)] }, v)).Fehler,
            "Global in einer Rolle");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.RolleSetzen(Admin, new Rolle(70, "X") { Felder = [new RollenFeld(0, 0, "crew", true, true, false, false)] }, v)).Fehler,
            "Kennung nur [A-Z0-9_]");
        var rolle = f.Tun(Auftrag.RolleSetzen(Admin, new Rolle(70, "Späher") { SelbstWaehlbar = true, Felder = [new RollenFeld(3, 2, null, true, true, true, true)] }, v));
        Pruefe.Wahr(rolle.Erfolg && rolle.NetzListeGeaendert, "Rolle angelegt");
        Pruefe.Gleich(Fehlercode.Veraltet, f.Tun(Auftrag.SchiffSetzen(Admin, new Schiff(4, "Schiff 4"), v)).Fehler, "alte NetzListe-Version");
        v = f.Z.NetzListeVersion;
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.SchiffSetzen(Admin, new Schiff(4, "Schiff 4") { Plaetze = [new SchiffsPlatz(99, 1)] }, v)).Fehler, "unbekannte Rolle im Platz");
        Pruefe.Wahr(f.Tun(Auftrag.SchiffSetzen(Admin, new Schiff(4, "Schiff 4") { Plaetze = [new SchiffsPlatz(70, 2)] }, v)).Erfolg, "Schiff angelegt");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, 70, 4)).Erfolg, "D auf Schiff 4");
        Pruefe.Wahr(f.Z.Konten[D].FeldNummer(3) is { NetzId: 2, Fest: true }, "festes Rollenfeld");
        Pruefe.Wahr(f.Tun(Auftrag.SchiffLoeschen(Admin, 4, f.Z.NetzListeVersion)).Erfolg, "Schiff gelöscht");
        Pruefe.Gleich(null, f.Z.Konten[D].Platz, "Platz fällt weg, Felder bleiben");
        Pruefe.Wahr(f.Z.Konten[D].FeldNummer(3) is not null, "Feld bleibt");
        Pruefe.Wahr(f.Z.Pruefen().Count == 0, "Stand stimmig");
    }

    [Test]
    public static void KontoLoeschen_BeendetSitzung()
    {
        var f = new Funkprobe();
        var r = f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Loeschen, D));
        Pruefe.Wahr(r.BeendeteSitzungen.Single() == new BeendeteSitzung(Funkprobe.S(D), D, Sitzungsende.KontoGeloescht), "Sitzung endet");
        Pruefe.Falsch(f.Z.Konten.ContainsKey(D) || f.Z.IstAktiv(Funkprobe.S(D)), "Konto und Sitzung weg");
        Pruefe.Wahr(f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Sperren, E)).Erfolg, "sperren");
        Pruefe.Wahr(f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Entsperren, E)).Erfolg, "entsperren");
        Pruefe.Gleich(KontoStatus.Aktiv, f.Z.Konten[E].Status, "wieder aktiv");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(E, 60_000, E * 10)).Erfolg, "wieder anmeldbar");
    }

    [Test]
    public static void Konto_NameEindeutigNachNfkc()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1300, Name = "b" })).Erfolg, "angelegt");
        Pruefe.Gleich("b (2)", f.Z.Konten[1300].Name, "Kollision mit B nach ToUpperInvariant");
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1301, Name = "Ｂ" })).Erfolg, "Vollbreites B");
        // Mit InvariantGlobalization (Directory.Build.props) lässt .NET 10 Nicht-ASCII unverändert: dann greift NFKC nicht.
        var nfkcWirkt = "Ｂ".Normalize(System.Text.NormalizationForm.FormKC) == "B";
        Pruefe.Gleich(nfkcWirkt ? "Ｂ (3)" : "Ｂ", f.Z.Konten[1301].Name, nfkcWirkt ? "Kollision nach NFKC" : "ohne NFKC keine Kollision");
    }

    [Test]
    public static void Schnappschuss_StimmigkeitWirdGeprueft()
    {
        var fehler = Pruefe.Wirft<InvalidOperationException>(() => new SchnappschussBauer()
            .MitNetz(new Netz(2, "X"))
            .MitKonto(new Konto { Id = 1, Felder = [Funkprobe.F(0, 2, true, true), Funkprobe.F(1, 2, true, true)] })
            .MitSitzung(5, 99)
            .Bauen(), "unstimmig");
        Pruefe.Wahr(fehler.Message.Contains("mehr als einem Feld", StringComparison.Ordinal) && fehler.Message.Contains("Konto 99 fehlt", StringComparison.Ordinal),
            "Befunde benannt");
        var z = Funkprobe.Grundzustand().Bauen();
        Pruefe.Wahr(z.HoerendeSitzungen(7).SequenceEqual(new uint[] { Funkprobe.S(A), Funkprobe.S(B), Funkprobe.S(E) }), "Index Netz -> hörende Sitzungen (C hört 7 nicht)");
        Pruefe.Gleich(5, z.HoerendeSitzungen(1).Length, "Global hören alle aktiven");
    }
}
