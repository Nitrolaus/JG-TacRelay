// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Lage;
using static Funk.Tests.S2PhasenLageTests;

namespace Funk.Tests;

/// <summary>
/// "Heute als Funker" am Zustandsschreiber (Fassung 0.5.0, RAID-S2-ENTWURF Abschnitt 1, FUNKPROTOKOLL 6.15) mit simulierter Uhr:
/// nur ein ADMIN für sich selbst, die Rechte der Einsatzleitung ruhen je Aktion und je Sprachpaket, die Einsatzleitung teilt ihn
/// ein (Verschieben, PlaetzeFreigeben, ERSETZEN, Aufstellung), stummschalten darf ihn weiter nur ein ADMIN, Ende um 04:00 über den
/// Takt, bei Stufenwechsel und Sperre, und Regel 7: die eigene Sperre und das eigene Nachziehen enden beim Einschalten.
/// </summary>
internal static class S2FunkerLageTests
{
    // Im Namensraum Funk gibt es Funk.Admin; der Name aus dem statischen Import käme dagegen nicht an.
    private const uint Admin = Funkprobe.Admin;

    private static long Bis(Funkprobe f, long stunden = 8) => f.Jetzt + stunden * Stunde;

    [Test]
    public static void Funkermodus_NurAdmin_GrenzenDerDauer_EinUndAus()
    {
        var f = Abend();
        var z = f.Z;
        var neben = f.Schreiber.Neben;
        (string Wer, uint Id)[] ohne = [("FUNKER", Funkprobe.C), ("RAIDLEITER (Führung)", Funkprobe.A), ("Beauftragter", Kobold)];
        foreach (var (wer, id) in ohne)
            Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.FunkermodusSetzen(id, true, Bis(f))).Fehler, wer + " schaltet keinen Funker-Modus");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.FunkermodusSetzen(4242, true, Bis(f))).Fehler, "unbekanntes Konto");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.FunkermodusSetzen(Habicht, true, f.Jetzt)).Fehler, "Ende nicht nach jetzt");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.FunkermodusSetzen(Habicht, true, f.Jetzt + Fristen.FunkerModusMaxMs + 1)).Fehler, "über 24 h");
        Pruefe.Wahr(ReferenceEquals(z, f.Z) && ReferenceEquals(neben, f.Schreiber.Neben), "ohne Seiteneffekt");

        var ein = Ok(f, Auftrag.FunkermodusSetzen(Habicht, true, Bis(f)), "Habicht heute als Funker");
        Pruefe.Wahr(ein.Funkermodi.Single() == new FunkermodusFolge(Habicht, true, Bis(f)), "Folge: an, bis");
        Pruefe.Wahr(ein.BetroffeneKonten.Contains(Habicht) && f.Z.Konten[Habicht].AlsFunkerBis == Bis(f), "am Konto, Mitgliedschaften an Habicht");
        var nochmal = Ok(f, Auftrag.FunkermodusSetzen(Habicht, true, Bis(f)), "noch einmal ein");
        Pruefe.Wahr(nochmal.Funkermodi.IsEmpty, "ändert nichts");
        var aus = Ok(f, Auftrag.FunkermodusSetzen(Habicht, false, 0), "wieder Admin");
        Pruefe.Wahr(aus.Funkermodi.Single() is { KontoId: Habicht, An: false, Grund: null } && f.Z.Konten[Habicht].AlsFunkerBis == 0, "Folge: aus");
        Pruefe.Wahr(Ok(f, Auftrag.FunkermodusSetzen(Habicht, false, 0), "noch einmal aus").Funkermodi.IsEmpty, "aus ist aus");
    }

    [Test]
    public static void Funkermodus_RechteDerEinsatzleitungRuhen_JeAktionUndJePaket_VerwaltungsrechteBleiben()
    {
        var f = Abend();
        Pruefe.Gleich(Fehlercode.Keiner, f.Schreiber.SendeauswahlSetzen(Habicht, [], true, f.Jetzt), "Habicht sendet in Global");
        Pruefe.Wahr(f.Z.SitzungenMitRecht(Recht.Zuweisen, f.Jetzt).Contains(Funkprobe.S(Habicht)), "vorher: Habicht mit ZUWEISEN");
        Ok(f, Auftrag.FunkermodusSetzen(Habicht, true, Bis(f)), "Habicht heute als Funker");
        var r = f.Z.RechteVon(Habicht, f.Jetzt);
        Pruefe.Falsch((r & Rechteregeln.Einsatzleitung) != 0, "keine Rechte der Einsatzleitung: " + r);
        Pruefe.Wahr(r.Hat(Recht.KontenVerwalten | Recht.NetzeVerwalten | Recht.GeraeteFreigeben), "die Verwaltungsrechte bleiben (wirken nur in der Admin-Sitzung)");
        Pruefe.Gleich(Recht.Alle, f.Z.Konten[Habicht].Rechte, "die dauerhaften Rechte bleiben");
        Pruefe.Falsch(f.Z.SitzungenMitRecht(Recht.Zuweisen, f.Jetzt).Contains(Funkprobe.S(Habicht)), "keine Pushes an ZUWEISEN");
        Pruefe.Falsch(f.Z.SitzungenMitRecht(Recht.SprechanzeigeAlle, f.Jetzt).Contains(Funkprobe.S(Habicht)), "keine Sprechanzeige aller Netze");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.Verschieben(Habicht, [new Aenderung(Anker) { FelderPatch = [new Feldpatch(9, Echo)] }])).Fehler, "Habicht teilt nicht ein");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.PlaetzeFreigeben(Habicht)).Fehler, "gibt keine Plätze frei");
        // Den Wechsel sperrt nur, wer ZUWEISEN hat: Eine Sperre wirkt nur, solange ihr Urheber es hat (F9), sonst hebt der Takt sie
        // gleich wieder auf. NETZE_VERWALTEN allein (Admin-Sitzung) hebt eine Sperre auf, setzt aber keine.
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.SelbstwahlSetzen(Habicht, false, f.Z.NetzListeVersion)).Fehler, "sperrt keinen Wechsel");
        Pruefe.Falsch(f.Schreiber.Komfort.AuswahlVon(f.Z, Habicht).Global, "Global fällt aus der Sendeauswahl");
        f.M.Leeren();
        var paket = f.Senden(Habicht, Sendeart.Netz, [Netz.GlobalId], phase: 7);
        Pruefe.Wahr(paket.Verworfen && paket.Grund == SendeFehlerCode.KeinRechtGlobal, "Global-Paket je Paket verworfen");
        f.M.Leeren();
        var durchsage = f.Senden(Habicht, Sendeart.Durchsage, [], phase: 8);
        Pruefe.Wahr(durchsage.Verworfen && durchsage.Grund == SendeFehlerCode.KeinRechtDurchsage, "keine Durchsage");
        f.M.Leeren();
        Pruefe.Falsch(f.Senden(Habicht, Sendeart.Netz, [Crew], phase: 9).Verworfen, "in seinem Netz funkt er wie jeder");

        Ok(f, Auftrag.FunkermodusSetzen(Habicht, false, 0), "wieder Admin");
        Pruefe.Wahr(f.Z.RechteVon(Habicht, f.Jetzt).Hat(Recht.Zuweisen | Recht.GlobalSenden), "Rechte sofort zurück");
        f.M.Leeren();
        Pruefe.Falsch(f.Senden(Habicht, Sendeart.Netz, [Netz.GlobalId], phase: 10).Verworfen, "Global wieder");
    }

    [Test]
    public static void Einsatzleitung_TeiltDenFunkerAdminEin_StummschaltenNurDurchAdmin()
    {
        var f = Abend();
        Ok(f, Auftrag.Verschieben(Kobold, [new Aenderung(Sarevoc) { RolleId = Gunner, SchiffId = SchiffDrei }]), "Kobold setzt Sarevoc als Gunner");
        Pruefe.Wahr(f.Z.Konten[Sarevoc] is { Platz.RolleId: Gunner, Quelle: Quelle.Verschieben, QuelleDurch: Kobold }, "Platz, Quelle VERSCHIEBEN durch Kobold");
        Ok(f, Auftrag.Verschieben(Kobold, [new Aenderung(Sarevoc) { FelderPatch = [new Feldpatch(null, Echo, Senden: true)] }]), "Zusatznetz fest");
        Pruefe.Wahr(f.Z.Konten[Sarevoc].FeldMitNetz(Echo) is { Fest: true }, "Echo fest");
        var z = f.Z;
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.Stummschalten(Kobold, Sarevoc, Crew, 600)).Fehler, "Kobold schaltet ihn nicht stumm");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.Stummschalten(Kobold, Sarevoc, 0, 3600)).Fehler, "auch nicht ganz");
        Pruefe.Wahr(ReferenceEquals(z, f.Z), "ohne Seiteneffekt");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.Verschieben(Kobold, [new Aenderung(Habicht) { RolleId = Gunner, SchiffId = SchiffDrei }])).Fehler, "Habicht ohne Funker-Modus nicht");
        Ok(f, Auftrag.Stummschalten(Admin, Sarevoc, Crew, 60), "ein ADMIN schon");

        // ERSETZEN trifft ihn (er hat kein ZUWEISEN), PlaetzeFreigeben nimmt ihn ohne Sitzung mit.
        var leer = Ok(f, Auftrag.AufstellungSpeichern(Kobold, null, null, "Leer", []), "leere Aufstellung").AufstellungsId;
        var ersetzen = Ok(f, Auftrag.AufstellungAnwenden(Kobold, leer, null, Aufstellungsmodus.Ersetzen), "ERSETZEN");
        Pruefe.Wahr(ersetzen.Zurueckgesetzt.Contains(Sarevoc) && !ersetzen.Zurueckgesetzt.Contains(Habicht), "Sarevoc zurückgesetzt, Habicht nicht");
        Ok(f, Auftrag.Verschieben(Kobold, [new Aenderung(Sarevoc) { RolleId = Marine, SchiffId = SchiffDrei }]), "wieder Marine");
        Ok(f, Auftrag.SitzungBeenden(Funkprobe.S(Sarevoc)), "Sarevoc geht");
        f.Jetzt += Fristen.WiederverbindungMs + 1;
        Ok(f, Auftrag.Verschieben(Kobold, [new Aenderung(Sarevoc) { RolleId = Marine, SchiffId = SchiffDrei }]), "reserviert");
        var frei = Ok(f, Auftrag.PlaetzeFreigeben(Kobold), "Plätze freigeben");
        Pruefe.Wahr(frei.BetroffeneKonten.Contains(Sarevoc) && f.Z.Konten[Sarevoc].Platz is null, "Sarevoc freigegeben");
        var auf = Ok(f, Auftrag.AufstellungSpeichern(Kobold, null, null, "Mit Sarevoc", [new AufstellungsEintrag { KontoId = Sarevoc, RolleId = Gunner, SchiffId = SchiffDrei }]), "Aufstellung").AufstellungsId;
        var anwenden = Ok(f, Auftrag.AufstellungAnwenden(Kobold, auf, null, Aufstellungsmodus.Ergaenzen), "angewandt");
        Pruefe.Wahr(anwenden.Uebersprungen.IsEmpty && f.Z.Konten[Sarevoc].Platz is { RolleId: Gunner }, "der Eintrag für Sarevoc wird nicht übersprungen");
    }

    [Test]
    public static void Ablauf_Um0400_UeberDenTakt_RechteSchonVorher()
    {
        var f = Abend();
        var bis = f.Jetzt + Stunde;
        Ok(f, Auftrag.FunkermodusSetzen(Habicht, true, bis), "Habicht bis in einer Stunde");
        f.Jetzt = bis - 1;
        Pruefe.Falsch(f.Z.RechteVon(Habicht, f.Jetzt).Hat(Recht.Zuweisen), "1 ms vorher: Funker");
        Pruefe.Wahr(f.Tun(Auftrag.FristenPruefen()).Funkermodi.IsEmpty, "der Takt tut noch nichts");
        f.Jetzt = bis;
        Pruefe.Wahr(f.Z.RechteVon(Habicht, f.Jetzt).Hat(Recht.Zuweisen), "ab bis wieder Admin, auch ohne Takt");
        Pruefe.Wahr(Ok(f, Auftrag.Verschieben(Habicht, [new Aenderung(Anker) { FelderPatch = [new Feldpatch(9, Echo)] }]), "Habicht teilt wieder ein").Erfolg, "einteilen geht");
        var takt = f.Tun(Auftrag.FristenPruefen());
        Pruefe.Wahr(takt.Funkermodi.Single() is { KontoId: Habicht, An: false, Grund: "ABGELAUFEN" }, "Takt: ABGELAUFEN");
        Pruefe.Wahr(takt.BetroffeneKonten.Contains(Habicht) && f.Z.Konten[Habicht].AlsFunkerBis == 0, "abgeräumt, Mitgliedschaften an Habicht");
        Pruefe.Wahr(f.Tun(Auftrag.FristenPruefen()).Funkermodi.IsEmpty, "nur einmal");
    }

    [Test]
    public static void Ende_BeiStufenwechselUndSperre()
    {
        var f = Abend();
        Ok(f, Auftrag.FunkermodusSetzen(Habicht, true, Bis(f)), "Habicht als Funker");
        var stufe = f.Tun(InterneAuftraege.KontoUebernehmen(Habicht, "Habicht", null, Stufe.Raidleiter, Recht.Keine, Recht.Keine, KontoStatus.Aktiv));
        Pruefe.Wahr(stufe.Funkermodi.Any(x => x is { KontoId: Habicht, An: false, Grund: "STUFE" }) && f.Z.Konten[Habicht].AlsFunkerBis == 0, "Stufenwechsel beendet den Modus");
        var sperre = f.Tun(InterneAuftraege.KontoUebernehmen(Sarevoc, "Sarevoc", null, Stufe.Admin, Recht.Keine, Recht.Keine, KontoStatus.Gesperrt));
        Pruefe.Wahr(sperre.Funkermodi.Any(x => x is { KontoId: Sarevoc, An: false, Grund: "GESPERRT" }) && f.Z.Konten[Sarevoc].AlsFunkerBis == 0, "Sperre beendet den Modus");
    }

    [Test]
    public static void Einschalten_BeendetEigeneSperreUndEigenesNachziehen()
    {
        var f = Abend();
        Ok(f, Auftrag.SelbstwahlSetzen(Habicht, false, f.Z.NetzListeVersion), "Habicht sperrt den Wechsel");
        var id = Ok(f, Auftrag.AufstellungSpeichern(Habicht, null, null, "Spät", [new AufstellungsEintrag { DiscordId = "888", RolleId = Marine, SchiffId = SchiffDrei }]), "Aufstellung").AufstellungsId;
        Ok(f, Auftrag.AufstellungAnwenden(Habicht, id, null, Aufstellungsmodus.Ergaenzen), "Habicht wendet an, 888 offen");
        var ein = Ok(f, Auftrag.FunkermodusSetzen(Habicht, true, Bis(f)), "Habicht als Funker");
        Pruefe.Wahr(ein.NetzListeGeaendert && f.Z.Einstellungen.Selbstwahl, "seine Sperre endet (Regel 7)");
        Pruefe.Gleich(null, f.Schreiber.Neben.Letzte, "sein Nachziehen endet");
        var neu = Ok(f, Auftrag.KontoAnlegen(new Konto { Id = 1301, Name = "Spät", DiscordId = "888" }), "888 verknüpft");
        Pruefe.Wahr(neu.Meldungen.IsEmpty && f.Z.Konten[1301].Platz is null, "nicht mehr nachgezogen");
        Ok(f, Auftrag.FunkermodusSetzen(Habicht, false, 0), "wieder Admin");
        Pruefe.Wahr(f.Z.Einstellungen.Selbstwahl, "die Sperre kommt nicht zurück");
    }
}
