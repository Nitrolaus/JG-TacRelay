// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Server.Lage;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Rechte und Auftrag für einen Abend (6.1, 6.10, 6.12, RAIDLEITUNG-ENTWURF M1) am Zustandsschreiber mit simulierter Uhr:
/// RAIDLEITER teilt nicht mehr ein, die neue Rangregel, Erteilen nur mit dauerhaften eigenen Rechten, Grenzen, Ablauf je
/// Aktion und je Sprachpaket, Takt, Entzug, Abgabe, Ende bei Sperre und Stufenwechsel, Regel 7 (Sperre und Nachziehen enden
/// mit dem Auftrag), Wechselsperre mit Nachzüglern (F9), fremde Aufstellungen (F10), Einheiten (F5), Paket (N5), Belegung.
/// </summary>
internal static class RechteBeauftragungTests
{
    private const uint Admin = Funkprobe.Admin;
    private const uint A = Funkprobe.A;
    private const uint B = Funkprobe.B;
    private const uint C = Funkprobe.C;
    private const uint D = Funkprobe.D;
    private const uint E = Funkprobe.E;
    private const uint Admin2 = 1008;
    private const uint Rlv = 1010;
    private const uint F = 1011;
    private const uint G = 1012;
    private const ushort Co = Funkprobe.RolleCo;
    private const ushort Pilot = Funkprobe.RollePilot;
    private const ushort Schiff = Funkprobe.Schiff3;
    private const long Stunde = 60L * 60 * 1000;

    /// <summary>Grundzustand aus 8.9, dazu ein zweiter Admin mit Sitzung, ein RAIDLEITER mit KONTEN_VERWALTEN ohne ZUWEISEN, zwei Funker.</summary>
    private static Funkprobe Probe(Func<SchnappschussBauer, SchnappschussBauer>? zusatz = null) => new(b =>
    {
        b.MitKonto(new Konto { Id = Admin2, Name = "Admin2", Stufe = Stufe.Admin })
            .MitKonto(new Konto { Id = Rlv, Name = "RLV", Stufe = Stufe.Raidleiter, Plus = Recht.KontenVerwalten })
            .MitKonto(new Konto { Id = F, Name = "F" })
            .MitKonto(new Konto { Id = G, Name = "G" })
            .MitSitzung(Funkprobe.S(Admin2), Admin2)
            .MitSitzung(Funkprobe.S(F), F);
        return zusatz?.Invoke(b) ?? b;
    });

    private static Ergebnis Erteilen(Funkprobe f, uint an, long dauer = 4 * Stunde, long? von = null, Recht? rechte = null, uint durch = Admin) =>
        f.Tun(Auftrag.BeauftragungErteilen(durch, an, von, (von ?? f.Jetzt) + dauer, rechte, "Op Nachtfalke"));

    private static Auftrag Umsetzen(uint wer, uint wen) => Auftrag.Verschieben(wer, [new Aenderung(wen) { FelderPatch = [new Feldpatch(9, 7)] }]);

    // ---------------------------------------------------------------- Rechte und Rangregel

    [Test]
    public static void Rechte_RaidleiterOhneZuweisen_InBeidenTabellenGleich()
    {
        Pruefe.Falsch(Rechteregeln.Standard(Stufe.Raidleiter).Hat(Recht.Zuweisen), "RAIDLEITER (Führung) hat kein ZUWEISEN mehr");
        Pruefe.Wahr(Rechteregeln.Standard(Stufe.Raidleiter).Hat(Recht.GlobalSenden | Recht.Durchsage | Recht.DurchsageAlle | Recht.MehrfachSenden | Recht.SprechanzeigeAlle),
            "Führung behält Global, Durchsage, Mehrfachsenden, Sprechanzeige");
        foreach (var (lage, identitaet) in new[] { (Stufe.Funker, I.Stufe.Funker), (Stufe.Raidleiter, I.Stufe.Raidleiter), (Stufe.Admin, I.Stufe.Admin) })
            Pruefe.Gleich((int)Rechteregeln.Standard(lage), (int)I.Rechteregeln.Standard(identitaet), $"Standard {lage} in beiden Kopien gleich");
        Pruefe.Gleich(Recht.Keine, Rechteregeln.Einsatzleitung & Rechteregeln.Privilegiert, "ein Auftrag trägt nie ein Verwaltungsrecht");
    }

    [Test]
    public static void Rangregel_ZuweisenWirktAufFunkerUndFuehrung_AufAdminNurDurchAdmin()
    {
        const long Jetzt = 1_790_000_000_000;
        foreach (var anf in Enum.GetValues<Stufe>())
        {
            foreach (var ziel in Enum.GetValues<Stufe>())
            {
                foreach (var funker in new[] { false, true })
                {
                    // Seit 0.5.0 (6.15): ein ADMIN im Funker-Modus ist einteilbar, stummschalten darf ihn weiter nur ein ADMIN.
                    var konto = new Konto { Id = 7, Stufe = ziel, AlsFunkerBis = funker ? Jetzt + 1 : 0 };
                    var erwartet = ziel != Stufe.Admin || anf == Stufe.Admin || funker;
                    Pruefe.Gleich(erwartet, Rechteregeln.DarfZuweisen(anf, konto, Jetzt), $"DarfZuweisen({anf}, {ziel}, Funker {funker})");
                    var abgelaufen = konto with { AlsFunkerBis = funker ? Jetzt : 0 };
                    Pruefe.Gleich(ziel != Stufe.Admin || anf == Stufe.Admin, Rechteregeln.DarfZuweisen(anf, abgelaufen, Jetzt), $"abgelaufener Funker-Modus zählt nicht ({anf}, {ziel})");
                    Pruefe.Gleich(ziel != Stufe.Admin || anf == Stufe.Admin, Rechteregeln.DarfStummschalten(anf, ziel), $"DarfStummschalten({anf}, {ziel})");
                }
            }
        }
        var f = Probe();
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Umsetzen(A, D)).Fehler, "RAIDLEITER ohne Auftrag teilt nicht mehr ein");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.PlaetzeFreigeben(A)).Fehler, "RAIDLEITER gibt keine Plätze frei");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.SelbstwahlSetzen(A, false, f.Z.NetzListeVersion)).Fehler, "RAIDLEITER sperrt nicht");
        Pruefe.Wahr(f.Tun(Umsetzen(Admin, A)).Erfolg, "ADMIN teilt auch ohne Admin-Sitzung ein (Brett ohne Code)");
    }

    [Test]
    public static void FunkerMitAuftrag_VerschiebtFuehrungUndFunker_AberKeinenAdmin()
    {
        var f = Probe();
        Pruefe.Wahr(Erteilen(f, B).Erfolg, "Auftrag für B");
        Pruefe.Wahr(f.Tun(Umsetzen(B, A)).Erfolg, "FUNKER mit Auftrag setzt die Führung um");
        Pruefe.Wahr(f.Tun(Umsetzen(B, D)).Erfolg, "und einen Funker");
        Pruefe.Wahr(f.Tun(Auftrag.Stummschalten(B, A, 7, 30)).Erfolg, "und schaltet die Führung stumm");
        var vorher = f.Z;
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Umsetzen(B, Admin)).Fehler, "einen ADMIN nicht");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.Stummschalten(B, Admin2, 0, 30)).Fehler, "einen ADMIN auch nicht stumm");
        Pruefe.Wahr(ReferenceEquals(vorher, f.Z), "ohne Seiteneffekt");
    }

    // ---------------------------------------------------------------- Erteilen

    [Test]
    public static void Erteilen_NurMitKontenVerwalten_UndNurMitEigenenDauerhaftenRechten()
    {
        var f = Probe();
        var vorher = f.Z;
        Pruefe.Gleich(Fehlercode.KeinRecht, Erteilen(f, D, durch: C).Fehler, "FUNKER ohne KONTEN_VERWALTEN");
        Pruefe.Gleich(Fehlercode.KeinRecht, Erteilen(f, D, durch: A).Fehler, "RAIDLEITER ohne KONTEN_VERWALTEN");
        // Rechteausweitung über den Auftrag (Regel 1): KONTEN_VERWALTEN ohne ZUWEISEN verteilt kein ZUWEISEN.
        Pruefe.Gleich(Fehlercode.KeinRecht, Erteilen(f, D, durch: Rlv).Fehler, "RAIDLEITER mit KONTEN_VERWALTEN, aber ohne ZUWEISEN");
        Pruefe.Gleich(Fehlercode.KeinRecht, Erteilen(f, Rlv, durch: Rlv).Fehler, "sich selbst nie");
        Pruefe.Wahr(ReferenceEquals(vorher, f.Z), "ohne Seiteneffekt");
        var r = Erteilen(f, D);
        Pruefe.Wahr(r.Erfolg, "ADMIN erteilt: " + r);
        var b = f.Z.Konten[D].Beauftragung;
        Pruefe.Wahr(b is { Durch: Admin, Grund: "Op Nachtfalke", Begonnen: true } && b.Rechte == Rechteregeln.Einsatzleitung, "alle sechs Rechte, gleich begonnen");
        Pruefe.Wahr(r.Beauftragungen.Single() is { KontoId: D, Anlass: BeauftragungsAnlass.Erteilt }, "Folge ERTEILT");
        Pruefe.Wahr(r.BetroffeneKonten.Contains(D), "Mitgliedschaften an D");
        Pruefe.Gleich(Recht.Keine, f.Z.Konten[D].Rechte, "die dauerhaften Rechte bleiben die eines FUNKERS");
        Pruefe.Wahr(f.Z.RechteVon(D, f.Jetzt).Hat(Recht.Zuweisen | Recht.GlobalSenden), "wirksam mit dem Auftrag");
    }

    [Test]
    public static void Erteilen_Grenzen_Ungueltig()
    {
        var f = Probe();
        var vorher = f.Z;
        Pruefe.Gleich(Fehlercode.Ungueltig, Erteilen(f, D, rechte: Recht.Zuweisen | Recht.SprechanzeigeAlle | Recht.KontenVerwalten).Fehler, "Verwaltungsrecht im Auftrag");
        Pruefe.Gleich(Fehlercode.Ungueltig, Erteilen(f, D, rechte: Recht.GlobalSenden | Recht.SprechanzeigeAlle).Fehler, "ohne ZUWEISEN ist es kein Auftrag");
        Pruefe.Gleich(Fehlercode.Ungueltig, Erteilen(f, D, dauer: 24 * Stunde + 1).Fehler, "bis über 24 h nach von");
        Pruefe.Gleich(Fehlercode.Ungueltig, Erteilen(f, D, von: f.Jetzt + Fristen.AuftragVorausMs + 1).Fehler, "von über 14 Tage voraus");
        Pruefe.Gleich(Fehlercode.Ungueltig, Erteilen(f, D, dauer: 0).Fehler, "bis nicht nach von");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.BeauftragungErteilen(Admin, D, f.Jetzt - 3 * Stunde, f.Jetzt - Stunde)).Fehler, "schon abgelaufen");
        Pruefe.Gleich(Fehlercode.Ungueltig, Erteilen(f, Admin2).Fehler, "nicht für ADMIN-Konten");
        Pruefe.Gleich(Fehlercode.Unbekannt, Erteilen(f, 4242).Fehler, "unbekanntes Konto");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.BeauftragungErteilen(Admin, D, null, f.Jetzt + Stunde, null, new string('x', 49))).Fehler, "Grund über 48 Zeichen");
        Pruefe.Wahr(ReferenceEquals(vorher, f.Z), "ohne Seiteneffekt");

        Pruefe.Wahr(Erteilen(f, D, von: f.Jetzt + Fristen.AuftragVorausMs, dauer: Fristen.AuftragDauerMs).Erfolg, "genau 14 Tage voraus und 24 h lang");
        Pruefe.Wahr(f.Tun(Auftrag.BeauftragungErteilen(Admin, E, f.Jetzt - Stunde, f.Jetzt + Stunde)).Erfolg, "von in der Vergangenheit gilt ab jetzt");
        Pruefe.Gleich(f.Jetzt, f.Z.Konten[E].Beauftragung!.Von, "von auf jetzt gesetzt");
        Pruefe.Wahr(Erteilen(f, B).Erfolg && Erteilen(f, C).Erfolg, "dritter und vierter Auftrag");
        Pruefe.Gleich(Fehlercode.Ungueltig, Erteilen(f, F).Fehler, "höchstens 4 laufende oder geplante");
        var laenger = Erteilen(f, B, dauer: 8 * Stunde);
        Pruefe.Wahr(laenger.Erfolg && f.Z.Konten[B].Beauftragung!.Bis == f.Jetzt + 8 * Stunde, "erneut erteilen ersetzt (verlängern) und zählt nicht doppelt");
        Pruefe.Wahr(Erteilen(f, D, rechte: Rechteregeln.EinsatzleitungPflicht).Erfolg, "nur die Pflichtrechte");
        Pruefe.Wahr(f.Z.RechteVon(D, f.Jetzt).Hat(Recht.Zuweisen) && !f.Z.RechteVon(D, f.Jetzt).Hat(Recht.GlobalSenden), "Global abgewählt, ZUWEISEN bleibt");

        f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Sperren, G));
        Pruefe.Gleich(Fehlercode.Ungueltig, Erteilen(f, G).Fehler, "nicht für gesperrte Konten");
    }

    // ---------------------------------------------------------------- Weitergeben und Rechteausweitung

    [Test]
    public static void Beauftragter_GibtNichtsWeiter_UndVerwaltetNichts()
    {
        var f = Probe();
        Pruefe.Wahr(Erteilen(f, B).Erfolg, "Auftrag für B");
        var vorher = f.Z;
        var neben = f.Schreiber.Neben;
        var v = f.Z.NetzListeVersion;
        (string Was, Auftrag Auftrag)[] faelle =
        [
            ("Auftrag weitergeben", Auftrag.BeauftragungErteilen(B, D, null, f.Jetzt + Stunde)),
            ("fremden Auftrag entziehen", Auftrag.BeauftragungEntziehen(B, B)),
            ("Konto sperren", Auftrag.KontoVerwaltung(B, KontoOperation.Sperren, D)),
            ("Stufe setzen", Auftrag.KontoVerwaltung(B, KontoOperation.Stufe, D, stufe: Stufe.Raidleiter)),
            ("Rechte setzen", Auftrag.KontoVerwaltung(B, KontoOperation.Rechte, D, rechte: Recht.Zuweisen)),
            ("Netz anlegen", Auftrag.NetzSetzen(B, new Netz(40, "Neu"), v)),
            ("Paket", Auftrag.Paket(B, [new Verwaltungsschritt.NetzSetzen(new Netz(40, "Neu"))], v)),
            ("Verdrängung freigeben", Auftrag.VerdraengungFreigeben(B, E)),
        ];
        foreach (var (was, auftrag) in faelle)
        {
            var r = f.Tun(auftrag);
            Pruefe.Gleich(Fehlercode.KeinRecht, r.Fehler, was);
        }
        Pruefe.Wahr(ReferenceEquals(vorher, f.Z) && ReferenceEquals(neben, f.Schreiber.Neben), "ohne Seiteneffekt");
    }

    // ---------------------------------------------------------------- Ablauf

    [Test]
    public static void GeplanterAuftrag_WirktErstAbVon_AuchOhneTakt()
    {
        var f = Probe();
        var von = f.Jetzt + Stunde;
        var r = Erteilen(f, B, von: von);
        Pruefe.Wahr(r.Erfolg && f.Z.Konten[B].Beauftragung is { Begonnen: false }, "geplant, noch nicht begonnen");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Umsetzen(B, D)).Fehler, "vor von: KEIN_RECHT");
        Verworfen(f, B, SendeFehlerCode.KeinRechtGlobal, phase: 1);
        f.Jetzt = von;
        Pruefe.Wahr(f.Tun(Umsetzen(B, D)).Erfolg, "ab von: einteilen, ohne dass der Takt lief");
        f.M.Leeren();
        var g = f.Senden(B, Sendeart.Netz, [1], phase: 2);
        Pruefe.Falsch(g.Verworfen, "ab von: Global senden");
        var takt = f.Tun(Auftrag.FristenPruefen());
        Pruefe.Wahr(takt.Beauftragungen.Single() is { KontoId: B, Anlass: BeauftragungsAnlass.Begonnen }, "der Takt meldet den Beginn einmal");
        Pruefe.Wahr(f.Tun(Auftrag.FristenPruefen()).Beauftragungen.IsEmpty, "und nicht zweimal");
    }

    [Test]
    public static void Ablauf_EineMsNachBis_KeinRecht_UndGlobalPaketVerworfen_ObwohlDerTaktNochNichtLief()
    {
        var f = Probe();
        Pruefe.Wahr(Erteilen(f, B, dauer: Stunde).Erfolg, "Auftrag für eine Stunde");
        var bis = f.Jetzt + Stunde;
        f.Jetzt = bis - 1;
        Pruefe.Wahr(f.Tun(Umsetzen(B, D)).Erfolg, "1 ms vor bis");
        f.M.Leeren();
        Pruefe.Falsch(f.Senden(B, Sendeart.Netz, [1], phase: 3).Verworfen, "1 ms vor bis: Global geht");
        f.Jetzt = bis + 1;
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Umsetzen(B, E)).Fehler, "1 ms nach bis: Verschieben KEIN_RECHT");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.PlaetzeFreigeben(B)).Fehler, "PlaetzeFreigeben KEIN_RECHT");
        f.M.Leeren();
        Verworfen(f, B, SendeFehlerCode.KeinRechtGlobal, phase: 4);
        f.M.Leeren();
        var durchsage = f.Senden(B, Sendeart.Durchsage, [], phase: 5);
        Pruefe.Wahr(durchsage.Verworfen && durchsage.Grund == SendeFehlerCode.KeinRechtDurchsage, "Durchsage KEIN_RECHT_DURCHSAGE");
        Pruefe.Wahr(f.Z.Konten[B].Beauftragung is not null, "der Takt lief noch nicht: der Auftrag steht noch im Stand");

        var takt = f.Tun(Auftrag.FristenPruefen());
        Pruefe.Wahr(takt.Erfolg && takt.Beauftragungen.Single() is { KontoId: B, Anlass: BeauftragungsAnlass.Abgelaufen }, "Takt: ABGELAUFEN");
        Pruefe.Gleich(null, f.Z.Konten[B].Beauftragung, "abgeräumt");
        Pruefe.Wahr(takt.BetroffeneKonten.Contains(B), "Mitgliedschaften an B");
    }

    private static void Verworfen(Funkprobe f, uint wer, SendeFehlerCode code, ushort phase)
    {
        f.M.Leeren();
        var r = f.Senden(wer, Sendeart.Netz, [1], phase: phase);
        Pruefe.Wahr(r.Verworfen, "Global-Paket verworfen");
        Pruefe.Gleich(code, r.Grund, "Grund");
        // Eine noch offene frühere Phase bekommt vom Server ihr ENDE (6.4); weitergeleitet wird nichts.
        Pruefe.Wahr(f.M.Sprache.TrueForAll(s => s.Stempel.Erzeugt), "keine Kopie des verworfenen Pakets");
    }

    [Test]
    public static void EntziehenUndAbgeben_WirkenSofort()
    {
        var f = Probe();
        Pruefe.Wahr(Erteilen(f, B).Erfolg && Erteilen(f, C).Erfolg, "zwei Aufträge");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.BeauftragungEntziehen(A, B)).Fehler, "RAIDLEITER entzieht nichts");
        var r = f.Tun(Auftrag.BeauftragungEntziehen(Admin2, B));
        Pruefe.Wahr(r.Erfolg && r.Beauftragungen.Single() is { KontoId: B, Anlass: BeauftragungsAnlass.Entzogen, Durch: Admin2 }, "ein anderer Admin entzieht");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Umsetzen(B, D)).Fehler, "sofort ohne ZUWEISEN");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.BeauftragungEntziehen(Admin, B)).Fehler, "nichts mehr zu entziehen");
        var ab = f.Tun(Auftrag.BeauftragungAbgeben(C));
        Pruefe.Wahr(ab.Erfolg && ab.Beauftragungen.Single() is { KontoId: C, Anlass: BeauftragungsAnlass.Abgegeben }, "C gibt selbst ab");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.BeauftragungAbgeben(D)).Fehler, "wer keinen hat, gibt nichts ab");
    }

    [Test]
    public static void SperreStufenwechselUndErteiler_BeendenDenAuftrag()
    {
        var f = Probe();
        Pruefe.Wahr(Erteilen(f, B).Erfolg && Erteilen(f, C).Erfolg && Erteilen(f, D, durch: Admin2).Erfolg, "drei Aufträge");
        var sperre = f.Tun(Auftrag.KontoVerwaltung(Admin, KontoOperation.Sperren, B));
        Pruefe.Wahr(sperre.Beauftragungen.Any(x => x is { KontoId: B, Anlass: BeauftragungsAnlass.Beendet, Grund: "GESPERRT" }), "Sperre beendet den Auftrag");
        Pruefe.Gleich(null, f.Z.Konten[B].Beauftragung, "fort");
        var stufe = f.Tun(InterneAuftraege.KontoUebernehmen(C, "C", null, Stufe.Raidleiter, Recht.Keine, Recht.Keine, KontoStatus.Aktiv));
        Pruefe.Wahr(stufe.Beauftragungen.Any(x => x is { KontoId: C, Anlass: BeauftragungsAnlass.Beendet, Grund: "STUFE" }), "Stufenwechsel (Spiegel des Kontenbuchs) beendet ihn");
        // Der Erteilende verliert die Stufe ADMIN: was er erteilt hat, endet.
        var erteiler = f.Tun(InterneAuftraege.KontoUebernehmen(Admin2, "Admin2", null, Stufe.Raidleiter, Recht.Keine, Recht.Keine, KontoStatus.Aktiv));
        Pruefe.Wahr(erteiler.Beauftragungen.Any(x => x is { KontoId: D, Anlass: BeauftragungsAnlass.Beendet, Grund: "ERTEILER" }), "Erteiler ohne KONTEN_VERWALTEN beendet");
        Pruefe.Gleich(null, f.Z.Konten[D].Beauftragung, "D ohne Auftrag");

        var g = Probe();
        Pruefe.Wahr(Erteilen(g, E).Erfolg, "Auftrag für E");
        var loeschen = g.Tun(InterneAuftraege.KontoEntfernen(E));
        Pruefe.Wahr(loeschen.Beauftragungen.Any(x => x is { KontoId: E, Anlass: BeauftragungsAnlass.Beendet, Grund: "GELOESCHT" }), "mit dem Konto gelöscht");
        Pruefe.Wahr(Erteilen(g, F, durch: Admin2).Erfolg, "Auftrag für F");
        Pruefe.Wahr(g.Tun(InterneAuftraege.KontoEntfernen(Admin2)).Beauftragungen.Any(x => x is { KontoId: F, Grund: "ERTEILER" }), "Erteiler gelöscht");
    }

    // ---------------------------------------------------------------- Regel 7, Nachziehen, Wechselsperre (F9)

    [Test]
    public static void Selbstwahlsperre_EndetMitDemAuftrag_OhneAuftragNach12Stunden()
    {
        var f = Probe();
        Pruefe.Wahr(Erteilen(f, B).Erfolg, "Auftrag für B");
        var sperren = f.Tun(Auftrag.SelbstwahlSetzen(B, false, f.Z.NetzListeVersion));
        Pruefe.Wahr(sperren.Erfolg && sperren.NetzListeGeaendert, "B sperrt den Wechsel");
        Pruefe.Wahr(f.Z.Einstellungen is { Selbstwahl: false, SelbstwahlDurch: B, Nachzuegler: true } e && e.SelbstwahlBis == f.Jetzt + Fristen.SelbstwahlSperreMs,
            "Urheber, Frist 12 h, Nachzügler frei");
        var ende = f.Tun(Auftrag.BeauftragungEntziehen(Admin, B));
        Pruefe.Wahr(ende.NetzListeGeaendert && f.Z.Einstellungen.Selbstwahl, "mit dem Auftrag endet seine Sperre (Regel 7)");

        Pruefe.Wahr(f.Tun(Auftrag.SelbstwahlSetzen(Admin, false, f.Z.NetzListeVersion)).Erfolg, "der Admin sperrt");
        f.Jetzt += Fristen.SelbstwahlSperreMs - 1;
        Pruefe.Wahr(f.Tun(Auftrag.FristenPruefen()) is { NetzListeGeaendert: false } && !f.Z.Einstellungen.Selbstwahl, "vor 12 h bleibt sie");
        f.Jetzt += 1;
        Pruefe.Gleich(Fehlercode.Keiner, f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Fehler, "nach 12 h wählt D wieder selbst, auch vor dem Takt");
        var takt = f.Tun(Auftrag.FristenPruefen());
        Pruefe.Wahr(takt.NetzListeGeaendert && f.Z.Einstellungen is { Selbstwahl: true, SelbstwahlDurch: 0 }, "der Takt hebt sie auf");
    }

    [Test]
    public static void Wechselsperre_NachzueglerOhnePlatzSteigtEin_MitPlatzNicht_GanzSperrenSperrtAlle()
    {
        var f = Probe(b => b.MitRolle(new Rolle(63, "Frei") { SelbstWaehlbar = true }));
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, Pilot, Schiff)).Erfolg, "B hat einen Platz");
        Pruefe.Wahr(f.Tun(Auftrag.SelbstwahlSetzen(Admin, false, f.Z.NetzListeVersion)).Erfolg, "Wechsel sperren");
        Pruefe.Gleich(Fehlercode.SelbstwahlGesperrt, f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, 63, null)).Fehler, "mit Platz: SELBSTWAHL_GESPERRT");
        Pruefe.Gleich(Fehlercode.SelbstwahlGesperrt, f.Tun(Auftrag.SatzAnwenden(B, 63, null, [])).Fehler, "auch über SatzAnwenden");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Co, Schiff)).Erfolg, "Nachzügler ohne Platz nimmt einen freien");
        Pruefe.Gleich(Fehlercode.SelbstwahlGesperrt, f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, 63, null)).Fehler, "danach hat er einen Platz");
        Pruefe.Wahr(f.Tun(Auftrag.SelbstwahlSetzen(Admin, false, f.Z.NetzListeVersion, nachzuegler: false)).Erfolg, "Ganz sperren");
        Pruefe.Gleich(Fehlercode.SelbstwahlGesperrt, f.Tun(Auftrag.EinsteigenRolleSchiff(E, null, 63, null)).Fehler, "jetzt auch ohne Platz gesperrt");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(Admin, E, 63, null)).Erfolg, "die Einsatzleitung teilt weiter ein");
    }

    [Test]
    public static void Nachziehen_EndetMitDemAuftragDesAnwenders()
    {
        var f = Probe();
        Pruefe.Wahr(Erteilen(f, B).Erfolg, "Auftrag für B");
        var id = f.Tun(Auftrag.AufstellungSpeichern(B, null, null, "Einsatz",
            [new AufstellungsEintrag { DiscordId = "777", Felder = [new Feldvorgabe(0, 7, true, true)] }])).AufstellungsId;
        Pruefe.Wahr(f.Tun(Auftrag.AufstellungAnwenden(B, id, null, Aufstellungsmodus.Ergaenzen)).Erfolg, "B wendet an, 777 offen");
        Pruefe.Wahr(f.Tun(Auftrag.BeauftragungAbgeben(B)).Erfolg, "B gibt ab");
        var neu = f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1300, Name = "Spät", DiscordId = "777" }));
        Pruefe.Wahr(neu.Erfolg && neu.Meldungen.IsEmpty && f.Z.Konten[1300].Felder.All(x => x.NetzId != 7), "nach dem Auftrag wird nicht mehr nachgezogen");

        var g = Probe();
        Pruefe.Wahr(Erteilen(g, B, dauer: Stunde).Erfolg, "Auftrag für B");
        var id2 = g.Tun(Auftrag.AufstellungSpeichern(B, null, null, "Einsatz",
            [new AufstellungsEintrag { DiscordId = "888", Felder = [new Feldvorgabe(0, 7, true, true)] }])).AufstellungsId;
        Pruefe.Wahr(g.Tun(Auftrag.AufstellungAnwenden(B, id2, null, Aufstellungsmodus.Ergaenzen)).Erfolg, "angewendet");
        g.Jetzt += Stunde + 1;
        var spaet = g.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1301, Name = "Spät2", DiscordId = "888" }));
        Pruefe.Wahr(spaet.Meldungen.IsEmpty && g.Z.Konten[1301].Felder.All(x => x.NetzId != 7), "auch nach dem Ablauf, bevor der Takt lief");
    }

    [Test]
    public static void Ersetzen_LaesstBeauftragteInRuhe()
    {
        var f = Probe();
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Erfolg, "D Pilot");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(F, null, Co, Schiff)).Erfolg, "F CO");
        Pruefe.Wahr(Erteilen(f, F).Erfolg, "F bekommt einen Auftrag");
        var id = f.Tun(Auftrag.AufstellungSpeichern(Admin, null, null, "Leer", [])).AufstellungsId;
        var r = f.Tun(Auftrag.AufstellungAnwenden(Admin, id, null, Aufstellungsmodus.Ersetzen));
        Pruefe.Wahr(r.Erfolg && r.Zurueckgesetzt.Contains(D) && !r.Zurueckgesetzt.Contains(F), "D zurückgesetzt, der Beauftragte F nicht");
        Pruefe.Gleich(Co, f.Z.Konten[F].Platz!.Value.RolleId, "F bleibt CO");
    }

    // ---------------------------------------------------------------- Aufstellungen anderer (F10)

    [Test]
    public static void FremdeAufstellung_AendernUndLoeschenNurErstellerOderAdmin()
    {
        var f = Probe();
        Pruefe.Wahr(Erteilen(f, B).Erfolg && Erteilen(f, C).Erfolg, "zwei Beauftragte");
        var habicht = f.Tun(Auftrag.AufstellungSpeichern(Admin, null, null, "Habichts Plan", []));
        Pruefe.Gleich(Admin, f.Schreiber.Neben.Aufstellungen[habicht.AufstellungsId].Ersteller, "Ersteller gemerkt");
        var vorher = f.Schreiber.Neben;
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.AufstellungSpeichern(B, habicht.AufstellungsId, 1, "Überschrieben", [])).Fehler, "fremde ändern: KEIN_RECHT");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.AufstellungLoeschen(B, habicht.AufstellungsId, 1)).Fehler, "fremde löschen: KEIN_RECHT");
        Pruefe.Wahr(ReferenceEquals(vorher, f.Schreiber.Neben), "ohne Seiteneffekt");
        Pruefe.Wahr(f.Tun(Auftrag.AufstellungAnwenden(B, habicht.AufstellungsId, null, Aufstellungsmodus.Ergaenzen)).Erfolg, "anwenden darf er jede");
        var eigene = f.Tun(Auftrag.AufstellungSpeichern(B, null, null, "Bs Plan", []));
        Pruefe.Wahr(f.Tun(Auftrag.AufstellungSpeichern(B, eigene.AufstellungsId, 1, "Bs Plan 2", [])).Erfolg, "eigene ändern");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.AufstellungLoeschen(C, eigene.AufstellungsId, 2)).Fehler, "ein anderer Beauftragter nicht");
        Pruefe.Wahr(f.Tun(Auftrag.AufstellungLoeschen(Admin2, eigene.AufstellungsId, 2)).Erfolg, "ein ADMIN schon");
        Pruefe.Wahr(f.Tun(Auftrag.AufstellungLoeschen(Admin, habicht.AufstellungsId, 1)).Erfolg, "der Ersteller schon");
    }

    // ---------------------------------------------------------------- Zuweisen, Tauschen, wie letztes Mal

    [Test]
    public static void Tausch_ZweiAenderungenInEinerAnfrage_KapazitaetAmEndzustand()
    {
        var f = Probe();
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Erfolg && f.Tun(Auftrag.EinsteigenRolleSchiff(E, null, Co, Schiff)).Erfolg, "D Pilot, E CO");
        Pruefe.Wahr(Erteilen(f, B).Erfolg, "B leitet");
        Pruefe.Gleich(Fehlercode.PlatzBelegt, f.Tun(Auftrag.Verschieben(B, [new Aenderung(D) { RolleId = Co, SchiffId = Schiff }])).Fehler, "einzeln voll");
        var r = f.Tun(Auftrag.Verschieben(B,
        [
            new Aenderung(D) { RolleId = Co, SchiffId = Schiff },
            new Aenderung(E) { RolleId = Pilot, SchiffId = Schiff },
        ]));
        Pruefe.Wahr(r.Erfolg, "Tausch in einer Anfrage: " + r);
        Pruefe.Wahr(f.Z.Konten[D].Platz is { RolleId: Co } && f.Z.Konten[E].Platz is { RolleId: Pilot }, "getauscht");
        Pruefe.Wahr(f.Z.Konten[D].Quelle == Quelle.Verschieben && f.Z.Konten[D].QuelleDurch == B, "quelle VERSCHIEBEN durch B");
    }

    [Test]
    public static void AufStandard_MerktDenLetztenPlatz_FuerWieLetztesMal()
    {
        var f = Probe();
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Erfolg, "D Pilot");
        var felder = f.Z.Konten[D].Felder;
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { RolleId = 0 }])).Erfolg, "zurück auf Standard");
        var d = f.Z.Konten[D];
        var ls = d.LetzterStand;
        Pruefe.Wahr(d.Platz is null && ls is { Platz: { RolleId: Pilot, SchiffId: Schiff } } && ls.Felder.SequenceEqual(felder), "letzter Stand: Schiff 3, Pilot");
        // "Wie letztes Mal" ist SatzAnwenden mit dem letzten Stand (A21); bei gesperrtem Wechsel als Nachzügler ohne Platz erlaubt.
        Pruefe.Wahr(f.Tun(Auftrag.SelbstwahlSetzen(Admin, false, f.Z.NetzListeVersion)).Erfolg, "Wechsel gesperrt");
        var wie = f.Tun(Auftrag.SatzAnwenden(D, ls!.Platz!.Value.RolleId, ls.Platz.Value.SchiffId, [.. ls.Felder.Select(x => new Feldvorgabe(x.Feld, x.NetzId, x.Hoeren, x.Senden))]));
        Pruefe.Wahr(wie.Erfolg && f.Z.Konten[D].Platz is { RolleId: Pilot }, "wie letztes Mal: " + wie);
    }

    // ---------------------------------------------------------------- Einheiten (F5) und Paket (N5)

    [Test]
    public static void Einheiten_ArtTypMindest_GeprueftUndAlsSchiffBehandelt()
    {
        var f = Probe(b => b
            .MitNetz(new Netz(50, "Trupp Alpha"))
            .MitRolle(new Rolle(60, "Schütze") { SelbstWaehlbar = true, BrauchtSchiff = true, Felder = [new RollenFeld(0, 0, "TRUPP", true, true, true, false)] }));
        var v = f.Z.NetzListeVersion;
        var trupp = new Schiff(20, "Trupp Alpha")
        {
            Art = Einheitsart.Trupp,
            Typ = "Infanterie",
            Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [new KeyValuePair<string, ushort>("TRUPP", 50)]),
            Plaetze = [new SchiffsPlatz(60, 6) { Mindest = 2 }],
        };
        Pruefe.Wahr(f.Tun(Auftrag.SchiffSetzen(Admin, trupp, v)).Erfolg, "Trupp angelegt");
        Pruefe.Wahr(f.Z.Schiffe[20] is { Art: Einheitsart.Trupp, Typ: "Infanterie" } s && s.Plaetze[0].Mindest == 2, "Art, Typ und Mindestbesetzung gespeichert");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, 60, 20)).Erfolg, "Einsteigen in einen Trupp wie in ein Schiff");
        Pruefe.Gleich((ushort)50, f.Z.Konten[D].Felder.Single().NetzId, "Truppnetz über die Kennung");
        v = f.Z.NetzListeVersion;
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.SchiffSetzen(Admin, trupp with { Plaetze = [new SchiffsPlatz(60, 2) { Mindest = 3 }] }, v)).Fehler, "Mindest über der Kapazität");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.SchiffSetzen(Admin, trupp with { Typ = new string('T', 33) }, v)).Fehler, "Typ über 32");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.SchiffSetzen(Admin, trupp with { Art = (Einheitsart)7 }, v)).Fehler, "unbekannte Art");
        Pruefe.Gleich(Einheitsart.Schiff, f.Z.Schiffe[Schiff].Art, "Einheiten ohne Art sind Schiffe");
    }

    [Test]
    public static void Paket_AtomarVersionEinmalEineNetzListe()
    {
        var f = Probe();
        var v = f.Z.NetzListeVersion;
        Verwaltungsschritt[] gut =
        [
            new Verwaltungsschritt.SchiffSetzen(new Schiff(30, "Schiff 30")),
            new Verwaltungsschritt.NetzSetzen(new Netz(70, "Crew 30") { SchiffId = 30, Schiffsnetz = "CREW" }),
            new Verwaltungsschritt.SchiffSetzen(new Schiff(30, "Schiff 30")
            {
                Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [new KeyValuePair<string, ushort>("CREW", 70)]),
                Plaetze = [new SchiffsPlatz(Pilot, 2)],
            }),
            new Verwaltungsschritt.Einstellung(false),
        ];
        var vorher = f.Z;
        Verwaltungsschritt[] schlecht = [.. gut, new Verwaltungsschritt.NetzLoeschen(4711)];
        var r1 = f.Tun(Auftrag.Paket(Admin, schlecht, v));
        Pruefe.Gleich(Fehlercode.Unbekannt, r1.Fehler, "Fehler im letzten Schritt");
        Pruefe.Wahr(r1.Fehlerliste.Single().Eintrag == 4, "nennt den Schritt");
        Pruefe.Wahr(ReferenceEquals(vorher, f.Z), "nichts halb angewendet");
        Pruefe.Gleich(Fehlercode.Veraltet, f.Tun(Auftrag.Paket(Admin, gut, v - 1)).Fehler, "Version einmal am Anfang");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.Paket(A, gut, v)).Fehler, "nur NETZE_VERWALTEN");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.Paket(Admin, [], v)).Fehler, "leer");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.Paket(Admin, [.. Enumerable.Repeat<Verwaltungsschritt>(new Verwaltungsschritt.Einstellung(true), 201)], v)).Fehler,
            "mehr als 200");
        var r = f.Tun(Auftrag.Paket(Admin, gut, v));
        Pruefe.Wahr(r.Erfolg && r.NetzListeGeaendert, "angewendet: " + r);
        Pruefe.Gleich(vorher.ZustandsVersion + 1, f.Z.ZustandsVersion, "ein Austausch");
        Pruefe.Gleich(f.Z.ZustandsVersion, f.Z.NetzListeVersion, "eine neue NetzListe-Version für das ganze Paket");
        Pruefe.Wahr(f.Z.Schiffe[30].Netze["CREW"] == 70 && !f.Z.Einstellungen.DurchsageNurTaste, "alle Schritte wirken");
    }

    // ---------------------------------------------------------------- Belegung

    [Test]
    public static void Belegung_ZaehltBelegtUndReserviertJePlatz()
    {
        var f = Probe(b => b.MitRolle(new Rolle(63, "Frei") { SelbstWaehlbar = true, Kapazitaet = 3 }));
        Pruefe.Wahr(f.Tun(Auftrag.SchiffSetzen(Admin, f.Z.Schiffe[Schiff] with { Plaetze = [new SchiffsPlatz(Co, 1) { Mindest = 1 }, new SchiffsPlatz(Pilot, 2)] },
            f.Z.NetzListeVersion)).Erfolg, "Mindestbesetzung CO");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Erfolg, "D Pilot (selbst, online)");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(Admin, G, Pilot, Schiff)).Erfolg, "G Pilot (gesetzt, offline: RESERVIERT)");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(E, null, 63, null)).Erfolg, "E Rolle ohne Einheit");
        var b = f.Z.Belegung(f.Jetzt);
        var pilot = b.Plaetze.Single(p => p.SchiffId == Schiff && p.RolleId == Pilot);
        Pruefe.Wahr(pilot is { Belegt: 1, Reserviert: 1, Kapazitaet: 2 }, "Pilot 1 belegt, 1 reserviert, von 2: " + pilot);
        Pruefe.Wahr(b.Plaetze.Single(p => p.SchiffId == Schiff && p.RolleId == Co) is { Belegt: 0, Reserviert: 0, Mindest: 1 }, "CO leer, Mindest 1");
        Pruefe.Wahr(b.Plaetze.Single(p => p.SchiffId == 0 && p.RolleId == 63) is { Belegt: 1, Kapazitaet: 3 }, "Rolle ohne Einheit");
        Pruefe.Gleich(f.Z.Sitzungen.Count, b.Online, "online");
        Pruefe.Gleich(2, b.MitPlatz, "zwei online mit Platz (D und E)");
        Pruefe.Wahr(b.Gleich(f.Z.Belegung(f.Jetzt)), "gleich gerechnet ist gleich");
    }
}
