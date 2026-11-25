// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Einsatzphasen am Zustandsschreiber (Fassung 0.5.0, RAID-S2-ENTWURF Abschnitt 3, FUNKPROTOKOLL 6.14) mit simulierter Uhr und dem
/// Beispielabend: "2 Entern" gibt den Marines Trupp Alpha als Sendenetz, der CO hört Trupp Alpha mit; Pilot und Gunner bleiben
/// unberührt. Geprüft werden die Regeln 1 bis 13 (Recht, VERALTET, unbekannte Nummer, wer betroffen ist, Zusatznetze, "Hören aus",
/// Sendeauswahl aus dem Komfortzustand, unveränderte Konten, Entzug, Lagezustand, Folgen, Rückfall nach 12 h, Offline), jeder Weg
/// zum Platz in Phase 2, das Anlegen mit seinen Grenzen und die Angriffe.
/// </summary>
internal static class S2PhasenLageTests
{
    internal const uint Admin = Funkprobe.Admin;
    internal const uint Kobold = 1101;
    internal const uint Anker = 1102;
    internal const uint Quarz = 1103;
    internal const uint Brandung = 1104;
    internal const uint Capo = 1105;
    internal const uint Flieger = 1106;
    internal const uint Gunny = 1107;
    internal const uint Habicht = 1108;
    internal const uint Sarevoc = 1109;
    internal const uint Hagel = 1110;
    internal const uint Neo = 1111;
    internal const uint Ole = 1112;
    internal const uint Zweiter = 1113;

    internal const ushort Kommando = Funkprobe.Kommando;
    internal const ushort Crew = Funkprobe.Crew;
    internal const ushort Bruecke = Funkprobe.Bruecke;
    internal const ushort TruppAlpha = 50;
    internal const ushort Echo = 90;
    internal const ushort Co = Funkprobe.RolleCo;
    internal const ushort Pilot = Funkprobe.RollePilot;
    internal const ushort Gunner = 12;
    internal const ushort Marine = 13;
    internal const ushort SchiffDrei = Funkprobe.Schiff3;
    internal const long Stunde = 60L * 60 * 1000;

    private static RollenFeld Rf(byte feld, ushort netz, bool hoeren, bool senden, bool standard = false, bool fest = false) =>
        new(feld, netz, null, hoeren, senden, standard, fest);

    private static RollenFeld Kennung(byte feld, string kennung, bool hoeren, bool senden, bool standard = false, bool fest = false) =>
        new(feld, 0, kennung, hoeren, senden, standard, fest);

    /// <summary>"2 Entern": Marines senden in Trupp Alpha und hören Crew (ohne senden) und Kommando; der CO hört Trupp Alpha mit.</summary>
    internal static Einsatzphase Entern => new(2, "Entern", "ENTERN")
    {
        Varianten =
        [
            new PhasenVariante(Marine) { Felder = [Kennung(0, "CREW", true, false), Rf(1, Kommando, true, false), Rf(2, TruppAlpha, true, true, standard: true)] },
            new PhasenVariante(Co) { Felder = [Kennung(0, "BRUECKE", true, true, standard: true), Kennung(1, "CREW", true, true), Rf(3, TruppAlpha, true, false)] },
        ],
    };

    /// <summary>"3 Rückzug": Marines senden wieder in der Crew und dürfen in Kommando senden.</summary>
    internal static Einsatzphase Rueckzug => new(3, "Rückzug", "RUECKZUG")
    {
        Varianten = [new PhasenVariante(Marine) { Felder = [Kennung(0, "CREW", true, true, standard: true), Rf(1, Kommando, true, true)] }],
    };

    /// <summary>
    /// Beispielabend: Netze Kommando, Crew 3, Brücke 3 (Grundzustand), dazu Trupp Alpha (zu) und Echo (offen); Rollen CO, Pilot (wie
    /// im Grundzustand), Gunner, Marine; Schiff 3 mit CO 1, Pilot 2, Gunner 2, Marine 12. Kobold leitet im Auftrag; Habicht ist ADMIN,
    /// Sarevoc ADMIN im Funker-Modus. Ohne Phasen; <see cref="MitPhasen"/> legt sie an.
    /// </summary>
    internal static Funkprobe Abend()
    {
        var f = new Funkprobe(b =>
        {
            b.MitNetz(new Netz(TruppAlpha, "Trupp Alpha") { Reihenfolge = 5 })
                .MitNetz(new Netz(Echo, "Echo") { Reihenfolge = 9, Offen = Offenheit.HoerenSenden })
                .MitRolle(new Rolle(Gunner, "Gunner") { SelbstWaehlbar = true, BrauchtSchiff = true, Felder = [Kennung(0, "CREW", true, true, standard: true)] })
                .MitRolle(new Rolle(Marine, "Marine") { SelbstWaehlbar = true, BrauchtSchiff = true, Felder = [Kennung(0, "CREW", true, true, standard: true), Rf(1, Kommando, true, false)] })
                .MitSchiff(new Schiff(SchiffDrei, "Schiff 3")
                {
                    Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
                        [new KeyValuePair<string, ushort>("CREW", Crew), new KeyValuePair<string, ushort>("BRUECKE", Bruecke)]),
                    Plaetze = [new SchiffsPlatz(Co, 1), new SchiffsPlatz(Pilot, 2), new SchiffsPlatz(Gunner, 2), new SchiffsPlatz(Marine, 12)],
                });
            foreach (var (id, name) in new[] { (Kobold, "Kobold"), (Anker, "Anker"), (Quarz, "Quarz"), (Brandung, "Brandung"), (Capo, "Capo"),
                (Flieger, "Flieger"), (Gunny, "Gunny"), (Neo, "Neo"), (Ole, "Ole"), (Zweiter, "Zweiter") })
                b.MitKonto(new Konto { Id = id, Name = name }).MitSitzung(Funkprobe.S(id), id, id * 10);
            b.MitKonto(new Konto { Id = Habicht, Name = "Habicht", Stufe = Stufe.Admin }).MitSitzung(Funkprobe.S(Habicht), Habicht, Habicht * 10);
            b.MitKonto(new Konto { Id = Sarevoc, Name = "Sarevoc", Stufe = Stufe.Admin }).MitSitzung(Funkprobe.S(Sarevoc), Sarevoc, Sarevoc * 10);
            b.MitKonto(new Konto { Id = Hagel, Name = "Hagel" });
            return b;
        });
        f.MitAuftrag(Kobold);
        Ok(f, Auftrag.EinsteigenRolleSchiff(Anker, null, Marine, SchiffDrei), "Anker Marine");
        Ok(f, Auftrag.EinsteigenRolleSchiff(Quarz, null, Marine, SchiffDrei), "Quarz Marine");
        Ok(f, Auftrag.HoerenSetzen(Quarz, Kommando, false), "Quarz hört Kommando nicht");
        Ok(f, Auftrag.Verschieben(Kobold, [new Aenderung(Brandung) { RolleId = Marine, SchiffId = SchiffDrei, FelderPatch = [new Feldpatch(null, Echo, Senden: true)] }]),
            "Brandung Marine mit Zusatznetz Echo");
        Ok(f, Auftrag.EinsteigenRolleSchiff(Capo, null, Co, SchiffDrei), "Capo CO");
        Pruefe.Gleich(Fehlercode.Keiner, f.Schreiber.SendeauswahlSetzen(Capo, [1], false, f.Jetzt), "Capo sendet selbst in die Crew");
        Ok(f, Auftrag.EinsteigenRolleSchiff(Flieger, null, Pilot, SchiffDrei), "Flieger Pilot");
        Ok(f, Auftrag.EinsteigenRolleSchiff(Gunny, null, Gunner, SchiffDrei), "Gunny Gunner");
        Ok(f, Auftrag.EinsteigenRolleSchiff(Habicht, null, Marine, SchiffDrei), "Habicht (ADMIN) Marine");
        Ok(f, Auftrag.EinsteigenRolleSchiff(Sarevoc, null, Marine, SchiffDrei), "Sarevoc (ADMIN) Marine");
        Ok(f, Auftrag.FunkermodusSetzen(Sarevoc, true, f.Jetzt + 8 * Stunde), "Sarevoc heute als Funker");
        Ok(f, Auftrag.Verschieben(Kobold, [new Aenderung(Hagel) { RolleId = Marine, SchiffId = SchiffDrei }]), "Hagel offline gesetzt");
        return f;
    }

    internal static Funkprobe MitPhasen(this Funkprobe f)
    {
        Ok(f, Auftrag.PhaseSetzen(Admin, new Einsatzphase(1, "Anflug", "ANFLUG"), f.Z.NetzListeVersion), "Phase 1 Anflug");
        Ok(f, Auftrag.PhaseSetzen(Admin, Entern, f.Z.NetzListeVersion), "Phase 2 Entern");
        Ok(f, Auftrag.PhaseSetzen(Admin, Rueckzug, f.Z.NetzListeVersion), "Phase 3 Rückzug");
        return f;
    }

    internal static Ergebnis Ok(Funkprobe f, Auftrag a, string was)
    {
        var r = f.Tun(a);
        Pruefe.Wahr(r.Erfolg, was + ": " + r);
        return r;
    }

    private static Mitgliedschaft F(byte feld, ushort netz, bool hoeren, bool senden, bool fest = false) => new(feld, netz, hoeren, senden, fest);

    private static string Felder(Konto k) => string.Join(" ", k.Felder.Select(m => $"{m.Feld}:{m.NetzId}{(m.Hoeren ? "h" : "")}{(m.Senden ? "s" : "")}{(m.Fest ? "f" : "")}"));

    // ---------------------------------------------------------------- Schalten

    [Test]
    public static void Entern_StelltNurRollenMitVarianteUm_NetzbildWieGeplant()
    {
        var f = Abend().MitPhasen();
        var vorher = f.Z;
        var r = f.Tun(Auftrag.PhaseSchalten(Kobold, 2, 1));
        Pruefe.Wahr(r.Erfolg, "Kobold schaltet auf Entern: " + r);
        var pw = r.Phasenwechsel;
        Pruefe.Wahr(pw is { Nr: 2, Vorher: 1, Durch: Kobold }, "Phasenwechsel 1 -> 2 durch Kobold");
        Pruefe.Gleich("1102,1103,1104,1105,1109,1110", string.Join(",", pw!.Umgestellt.Order()), "umgestellt: Anker, Quarz, Brandung, Capo, Sarevoc (Funker-Modus), Hagel (offline)");
        Pruefe.Gleich("1108", string.Join(",", pw.Ausgelassen), "ausgelassen: Habicht (ADMIN ohne Funker-Modus)");
        Pruefe.Wahr(r.BetroffeneKonten.Order().SequenceEqual(pw.Umgestellt.Order()), "Mitgliedschaften genau an die Umgestellten");
        Pruefe.Wahr(f.Schreiber.Neben.Phase is { Nr: 2, Durch: Kobold } p && p.Seit == f.Jetzt, "laufende Phase im Lagezustand");
        Pruefe.Gleich(vorher.NetzListeVersion, f.Z.NetzListeVersion, "die NetzListe ändert sich beim Schalten nicht");

        var anker = f.Z.Konten[Anker];
        Pruefe.Gleich("0:7h 1:2h 2:50hs", Felder(anker), "Anker: Crew ohne senden, Kommando, Trupp Alpha");
        Pruefe.Wahr(anker.Sendeauswahl.Felder.SequenceEqual([(byte)2]), "Anker sendet jetzt in Trupp Alpha");
        Pruefe.Wahr(Schnappschuss.IstEntzogen(anker, Crew, f.Jetzt, out var entzug) && entzug.Durch == Kobold, "Entzug der Crew vermerkt (ZIEL_ENTZOGEN)");
        Pruefe.Gleich("0:7h 1:2 2:50hs", Felder(f.Z.Konten[Quarz]), "Quarz: Hören aus für Kommando bleibt");
        Pruefe.Gleich("0:7h 1:2h 2:90hsf 3:50hs", Felder(f.Z.Konten[Brandung]), "Brandung: Zusatznetz Echo bleibt auf Feld 2, Trupp Alpha rückt auf 3");
        Pruefe.Wahr(f.Z.Konten[Brandung].Sendeauswahl.Felder.SequenceEqual([(byte)3]), "Brandung sendet in Trupp Alpha (Feld 3)");
        var capo = f.Z.Konten[Capo];
        Pruefe.Gleich("0:8hs 1:7hs 3:50h", Felder(capo), "Capo hört Trupp Alpha mit");
        Pruefe.Wahr(capo.Sendeauswahl.Felder.SequenceEqual([(byte)1]), "Capos selbst gewählte Crew bleibt (Komfortzustand)");
        Pruefe.Gleich("0:7h 1:2h 2:50hs", Felder(f.Z.Konten[Hagel]), "Hagel offline nachgeführt");
        Pruefe.Wahr(f.Z.Konten[Hagel].Platz is { RolleId: Marine } && f.Z.Konten[Hagel].Reservierung is not null && f.Z.Konten[Hagel].Quelle == Quelle.Verschieben,
            "Platz, Reservierung und Quelle bleiben");
        Pruefe.Gleich("0:7hs 1:2h", Felder(f.Z.Konten[Habicht]), "Habicht unverändert");

        foreach (var id in new[] { Flieger, Gunny, Habicht, Kobold })
            Pruefe.Gleich(vorher.Konten[id].KontoVersion, f.Z.Konten[id].KontoVersion, $"Konto {id} nicht angefasst (keine neue kontoVersion)");
        Pruefe.Falsch(r.BetroffeneKonten.Contains(Flieger) || r.BetroffeneKonten.Contains(Gunny), "kein Push an Pilot und Gunner");
    }

    [Test]
    public static void Zurueck_StelltDieGrundstellungHer_AuchSendeauswahlUndZusatznetz()
    {
        var f = Abend().MitPhasen();
        var vorher = f.Z;
        Ok(f, Auftrag.PhaseSchalten(Kobold, 2, 1), "Entern");
        var r = Ok(f, Auftrag.PhaseSchalten(Kobold, 1, 2), "zurück auf Anflug");
        Pruefe.Wahr(r.Phasenwechsel is { Nr: 1, Vorher: 2 }, "Phasenwechsel 2 -> 1");
        foreach (var id in new[] { Anker, Quarz, Brandung, Hagel, Sarevoc })
        {
            Pruefe.Gleich(Felder(vorher.Konten[id]), Felder(f.Z.Konten[id]), $"Konto {id}: Felder wie vorher");
            Pruefe.Gleich(vorher.Konten[id].Sendeauswahl, f.Z.Konten[id].Sendeauswahl, $"Konto {id}: Sendeauswahl wie vorher");
        }
        Pruefe.Gleich(Felder(vorher.Konten[Capo]), Felder(f.Z.Konten[Capo]), "Capo: Felder wie vorher");
        Pruefe.Wahr(f.Z.Konten[Capo].Sendeauswahl.Felder.SequenceEqual([(byte)1]), "Capo sendet weiter in der Crew");
        Pruefe.Wahr(f.Schreiber.Neben.Phase is { Nr: 1, Durch: Kobold }, "Grundstellung mit Urheber");
    }

    [Test]
    public static void Veraltet_ZweiLeiter_DerZweiteSchaltetNichtNochmal()
    {
        var f = Abend().MitPhasen();
        Ok(f, Auftrag.PhaseSchalten(Kobold, 2, 1), "Kobold schaltet zuerst");
        var z = f.Z;
        var neben = f.Schreiber.Neben;
        var r = f.Tun(Auftrag.PhaseSchalten(Admin, 2, 1));
        Pruefe.Gleich(Fehlercode.Veraltet, r.Fehler, "der zweite Leiter erwartete Phase 1");
        Pruefe.Wahr(r.PhaseStand is { Nr: 2, Durch: Kobold }, "VERALTET nennt die laufende Phase und wer geschaltet hat");
        Pruefe.Wahr(ReferenceEquals(z, f.Z) && ReferenceEquals(neben, f.Schreiber.Neben), "nichts geändert");
        var gleich = f.Tun(Auftrag.PhaseSchalten(Admin, 2, 2));
        Pruefe.Wahr(gleich.Erfolg && gleich.Phasenwechsel is null && ReferenceEquals(z, f.Z), "auf die laufende Phase schalten: Ok, nichts umgestellt");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.PhaseSchalten(Admin, 4, 2)).Fehler, "Phase 4 gibt es nicht");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.PhaseSchalten(Admin, 0, 2)).Fehler, "Nummer 0");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.PhaseSchalten(Admin, 6, 2)).Fehler, "Nummer über 5");
        Pruefe.Wahr(ReferenceEquals(z, f.Z) && ReferenceEquals(neben, f.Schreiber.Neben), "ohne Seiteneffekt");
    }

    [Test]
    public static void Rangregel_AdminSchaltetAlleUm_FunkerAdminKommtMit()
    {
        var f = Abend().MitPhasen();
        var r = Ok(f, Auftrag.PhaseSchalten(Admin, 2, 1), "der Admin schaltet");
        Pruefe.Wahr(r.Phasenwechsel!.Ausgelassen.IsEmpty && r.Phasenwechsel.Umgestellt.Contains(Habicht), "ein ADMIN stellt auch Habicht um");
        Pruefe.Gleich("0:7h 1:2h 2:50hs", Felder(f.Z.Konten[Habicht]), "Habicht wie alle Marines");
        Pruefe.Gleich("0:7h 1:2h 2:50hs", Felder(f.Z.Konten[Sarevoc]), "Sarevoc im Funker-Modus ebenso");
    }

    [Test]
    public static void Angriffe_OhneZuweisen_AbgelaufenerAuftrag_AdminAlsFunker_KeinRecht()
    {
        var f = Abend().MitPhasen();
        var z = f.Z;
        var neben = f.Schreiber.Neben;
        (string Wer, uint Id)[] ohne =
        [
            ("FUNKER", Funkprobe.C), ("RAIDLEITER (Führung)", Funkprobe.A), ("Marine Anker", Anker), ("ADMIN im Funker-Modus", Sarevoc),
        ];
        foreach (var (wer, id) in ohne)
            Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.PhaseSchalten(id, 2, 1)).Fehler, wer + " schaltet keine Phase");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.PhaseSchalten(4242, 2, 1)).Fehler, "ein unbekanntes Konto auch nicht");
        Pruefe.Wahr(ReferenceEquals(z, f.Z) && ReferenceEquals(neben, f.Schreiber.Neben), "ohne Seiteneffekt");
        // Abgelaufener Auftrag: 1 ms nach bis, bevor der Takt ihn abräumt.
        f.Jetzt += 12 * Stunde;
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.PhaseSchalten(Kobold, 2, 1)).Fehler, "Kobold nach dem Ablauf seines Auftrags");
        Pruefe.Wahr(f.Z.Konten[Kobold].Beauftragung is not null, "der Takt lief noch nicht");
    }

    [Test]
    public static void Offline_WirdNachgefuehrt_BeimAnmeldenStimmenDieFelder()
    {
        var f = Abend().MitPhasen();
        Ok(f, Auftrag.SitzungBeenden(Funkprobe.S(Quarz)), "Quarz trennt");
        Ok(f, Auftrag.PhaseSchalten(Kobold, 2, 1), "Entern");
        Pruefe.Gleich("0:7h 1:2 2:50hs", Felder(f.Z.Konten[Quarz]), "Quarz in der Wiederverbindungsfrist nachgeführt");
        f.Jetzt += 60_000;
        Ok(f, Auftrag.SitzungAktivieren(Quarz, 77_777, Quarz * 10), "Quarz kommt zurück");
        Pruefe.Gleich("0:7h 1:2 2:50hs", Felder(f.Z.Konten[Quarz]), "beim Anmelden die Felder der Phase");
    }

    [Test]
    public static void Phase2_JederWegZumPlatz_NimmtDieVariante()
    {
        var f = Abend().MitPhasen();
        // Ole steigt in der Grundstellung als Marine ein, nimmt das offene Echo dazu und hört Kommando nicht; dann nimmt Kobold ihn auf
        // Standard: Sein letzter Stand trägt die Felder der Grundstellung ("wie letztes Mal").
        Ok(f, Auftrag.EinsteigenRolleSchiff(Ole, null, Marine, SchiffDrei), "Ole Marine");
        Ok(f, Auftrag.FeldSetzen(Ole, 4, Echo), "Ole nimmt Echo");
        Ok(f, Auftrag.HoerenSetzen(Ole, Kommando, false), "Ole hört Kommando nicht");
        Ok(f, Auftrag.Verschieben(Kobold, [new Aenderung(Ole) { RolleId = 0 }]), "Ole auf Standard");
        var letzter = f.Z.Konten[Ole].LetzterStand!;
        Pruefe.Gleich("0:7hs 1:2 4:90hs", string.Join(" ", letzter.Felder.Select(m => $"{m.Feld}:{m.NetzId}{(m.Hoeren ? "h" : "")}{(m.Senden ? "s" : "")}")), "letzter Stand");
        Ok(f, Auftrag.PhaseSchalten(Kobold, 2, 1), "Entern");

        Ok(f, Auftrag.EinsteigenRolleSchiff(Neo, null, Marine, SchiffDrei), "Neo steigt in Phase 2 ein");
        Pruefe.Gleich("0:7h 1:2h 2:50hs", Felder(f.Z.Konten[Neo]), "Einsteigen: Variante");
        Pruefe.Wahr(f.Z.Konten[Neo].Sendeauswahl.Felder.SequenceEqual([(byte)2]), "und ihr Sendevorschlag");

        Ok(f, Auftrag.Verschieben(Kobold, [new Aenderung(Flieger) { RolleId = Marine, SchiffId = SchiffDrei }]), "Kobold zieht Flieger zu den Marines (wie zuletzt)");
        Pruefe.Gleich("0:7h 1:2h 2:50hs", Felder(f.Z.Konten[Flieger]), "Verschieben mit rolleId: Variante");

        var auf = Ok(f, Auftrag.AufstellungSpeichern(Kobold, null, null, "Nachschub",
            [new AufstellungsEintrag { KontoId = Gunny, RolleId = Marine, SchiffId = SchiffDrei }, new AufstellungsEintrag { DiscordId = "777", RolleId = Marine, SchiffId = SchiffDrei }]), "Aufstellung").AufstellungsId;
        Ok(f, Auftrag.AufstellungAnwenden(Kobold, auf, null, Aufstellungsmodus.Ergaenzen), "angewandt");
        Pruefe.Gleich("0:7h 1:2h 2:50hs", Felder(f.Z.Konten[Gunny]), "Aufstellung: Variante");
        var nachzug = Ok(f, Auftrag.KontoAnlegen(new Konto { Id = 1300, Name = "Spät", DiscordId = "777" }), "Nachzügler verknüpft");
        Pruefe.Wahr(nachzug.Meldungen.Any(m => m.Art == MeldungsArt.AufstellungNachgezogen), "nachgezogen");
        Pruefe.Gleich("0:7h 1:2h 2:50hs", Felder(f.Z.Konten[1300]), "Nachziehen: Variante");

        // "Wie letztes Mal": SatzAnwenden mit den Feldern der Grundstellung. Die Rolle hat in Phase 2 eine Variante: Die Felder kommen
        // aus der Phase, aus dem Satz nur "hören" (Kommando aus) und das offene Echo; Crew darf nicht wieder senden.
        var satz = Ok(f, Auftrag.SatzAnwenden(Ole, Marine, SchiffDrei, [.. letzter.Felder.Select(m => new Feldvorgabe(m.Feld, m.NetzId, m.Hoeren, m.Senden))], letzter.Sendeauswahl),
            "wie letztes Mal");
        Pruefe.Gleich("0:7h 1:2 2:50hs 4:90hs", Felder(f.Z.Konten[Ole]), "Satz in Phase 2: Variante, Kommando aus, Echo dazu");
        Pruefe.Wahr(f.Z.Konten[Ole].Sendeauswahl.Felder.SequenceEqual([(byte)2]), "Sendenetz der Phase (Trupp Alpha), nicht die alte Crew");
        Pruefe.Wahr(f.Z.Konten[Ole].Platz is { RolleId: Marine, SchiffId: SchiffDrei } && satz.Uebersprungen.IsEmpty, "Platz genommen, nichts übersprungen");

        // Rollen ohne Variante bleiben wie in der Grundstellung, auch in Phase 2.
        Ok(f, Auftrag.EinsteigenRolleSchiff(Zweiter, null, Gunner, SchiffDrei), "Zweiter Gunner");
        Pruefe.Gleich("0:7hs", Felder(f.Z.Konten[Zweiter]), "Gunner ohne Variante: Grundstellung");
    }

    [Test]
    public static void Rueckfall_Nach12Stunden_AufDieGrundstellung()
    {
        var f = Abend().MitPhasen();
        var vorher = f.Z;
        Ok(f, Auftrag.PhaseSchalten(Kobold, 2, 1), "Entern");
        f.Jetzt += Fristen.PhaseRueckfallMs - 1;
        var frueh = f.Tun(Auftrag.FristenPruefen());
        Pruefe.Wahr(frueh.Phasenwechsel is null && f.Schreiber.Neben.LaufendeNr() == 2, "vor 12 h läuft Entern weiter");
        f.Jetzt += 1;
        var takt = f.Tun(Auftrag.FristenPruefen());
        Pruefe.Wahr(takt.Phasenwechsel is { Nr: 1, Vorher: 2, Durch: 0 }, "der Takt fällt auf die Grundstellung zurück: " + takt);
        Pruefe.Wahr(takt.Phasenwechsel!.Umgestellt.Contains(Anker) && takt.Phasenwechsel.Umgestellt.Contains(Habicht) is false, "Anker zurück, Habicht war nie umgestellt");
        Pruefe.Gleich(Felder(vorher.Konten[Anker]), Felder(f.Z.Konten[Anker]), "Anker wie vorher");
        Pruefe.Wahr(f.Schreiber.Neben.Phase is { Nr: 1, Durch: 0 }, "Grundstellung durch den Takt");
        Pruefe.Wahr(f.Tun(Auftrag.FristenPruefen()).Phasenwechsel is null, "und nur einmal");
    }

    [Test]
    public static void Flut_VielePhasenwechsel_BleibenStimmig()
    {
        var f = Abend().MitPhasen();
        var vorher = f.Z;
        byte laufend = 1;
        for (var i = 0; i < 60; i++)
        {
            var nr = (byte)(i % 3 + 1 == laufend ? (laufend % 3) + 1 : i % 3 + 1);
            Ok(f, Auftrag.PhaseSchalten(Kobold, nr, laufend), $"Wechsel {i}: {laufend} -> {nr}");
            laufend = nr;
        }
        Ok(f, Auftrag.PhaseSchalten(Kobold, 1, laufend), "zurück auf 1");
        foreach (var id in new[] { Anker, Quarz, Brandung, Hagel })
            Pruefe.Gleich(Felder(vorher.Konten[id]), Felder(f.Z.Konten[id]), $"Konto {id}: nach 60 Wechseln wie vorher");
        Pruefe.Gleich(Felder(vorher.Konten[Capo]), Felder(f.Z.Konten[Capo]), "Capo wie vorher");
        Pruefe.Wahr(f.Z.Pruefen().Count == 0, "Stand stimmig");
    }

    // ---------------------------------------------------------------- Anlegen, Grenzen, Verweise

    [Test]
    public static void Anlegen_NurNetzeVerwalten_GrenzenUndVerweise()
    {
        var f = Abend();
        var v = f.Z.NetzListeVersion;
        var z = f.Z;
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.PhaseSetzen(Kobold, Entern, v)).Fehler, "Beauftragter legt keine Phase an");
        Pruefe.Gleich(Fehlercode.Veraltet, f.Tun(Auftrag.PhaseSetzen(Admin, Entern, v - 1)).Fehler, "NetzListe-Version");
        (string Was, Einsatzphase Phase)[] ungueltig =
        [
            ("Nummer 0", Entern with { Nr = 0 }),
            ("Nummer 6", Entern with { Nr = 6 }),
            ("Name leer", Entern with { Name = " " }),
            ("Name über 24", Entern with { Name = new string('E', 25) }),
            ("Kurzname über 12", Entern with { Kurz = new string('E', 13) }),
            ("Grundstellung mit Variante", Entern with { Nr = 1 }),
            ("unbekannte Rolle", Entern with { Varianten = [new PhasenVariante(77) { Felder = [Rf(0, Kommando, true, true)] }] }),
            ("unbekanntes Netz", Entern with { Varianten = [new PhasenVariante(Marine) { Felder = [Rf(0, 4711, true, true)] }] }),
            ("Global im Feld", Entern with { Varianten = [new PhasenVariante(Marine) { Felder = [Rf(0, Netz.GlobalId, true, true)] }] }),
            ("Feld doppelt", Entern with { Varianten = [new PhasenVariante(Marine) { Felder = [Rf(0, Kommando, true, true), Rf(0, Echo, true, true)] }] }),
            ("Rolle doppelt", Entern with { Varianten = [new PhasenVariante(Marine) { Felder = [Rf(0, Kommando, true, true)] }, new PhasenVariante(Marine) { Felder = [Rf(1, Echo, true, true)] }] }),
            ("Variante ohne Feld", Entern with { Varianten = [new PhasenVariante(Marine)] }),
            ("Kennung ungültig", Entern with { Varianten = [new PhasenVariante(Marine) { Felder = [Kennung(0, "crew", true, true)] }] }),
        ];
        foreach (var (was, phase) in ungueltig)
            Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.PhaseSetzen(Admin, phase, v)).Fehler, was);
        Pruefe.Wahr(ReferenceEquals(z, f.Z), "ohne Seiteneffekt");

        var r = Ok(f, Auftrag.PhaseSetzen(Admin, Entern, v), "Entern angelegt");
        Pruefe.Wahr(r.NetzListeGeaendert && f.Z.NetzListeVersion > v && f.Z.Phasen[2].Varianten.Length == 2, "NetzListe neu, Phase gespeichert");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.PhaseSchalten(Kobold, 3, 1)).Fehler, "3 gibt es noch nicht");
        for (byte nr = 3; nr <= 5; nr++) Ok(f, Auftrag.PhaseSetzen(Admin, Entern with { Nr = nr, Name = "Phase " + nr }, f.Z.NetzListeVersion), "Phase " + nr);
        Pruefe.Gleich(4, f.Z.Phasen.Count, "vier Phasen über der Grundstellung");

        // 512 Variantenfelder über alle Phasen: vier Phasen zu je 8 Rollen mit 16 Feldern sind 512, ein Feld mehr ist zu viel.
        var g = Abend();
        var rollen = Enumerable.Range(20, 8).Select(i => new Rolle((ushort)i, "R" + i) { Felder = [] }).ToList();
        foreach (var rolle in rollen) Ok(g, Auftrag.RolleSetzen(Admin, rolle, g.Z.NetzListeVersion), "Rolle " + rolle.Id);
        // Ein Netz steht je Variante nur in einem Feld; also 16 Felder mit lauter verschiedenen Netzen.
        var netze = Enumerable.Range(200, 16).Select(i => new Netz((ushort)i, "N" + i)).ToList();
        foreach (var n in netze) Ok(g, Auftrag.NetzSetzen(Admin, n, g.Z.NetzListeVersion), "Netz " + n.Id);
        var voll = netze.Select((n, i) => Rf((byte)i, n.Id, true, false)).ToImmutableArray();
        for (byte nr = 2; nr <= 5; nr++)
            Ok(g, Auftrag.PhaseSetzen(Admin, new Einsatzphase(nr, "P" + nr, "P" + nr) { Varianten = [.. rollen.Select(x => new PhasenVariante(x.Id) { Felder = voll })] }, g.Z.NetzListeVersion), "Phase " + nr + " voll");
        Pruefe.Gleich(512, g.Z.Phasen.Values.Sum(p => p.FelderAnzahl()), "genau 512 Felder");
        var neunte = new Rolle(28, "R28") { Felder = [] };
        Ok(g, Auftrag.RolleSetzen(Admin, neunte, g.Z.NetzListeVersion), "Rolle 28");
        var zuViel = g.Z.Phasen[5] with { Varianten = [.. g.Z.Phasen[5].Varianten, new PhasenVariante(28) { Felder = [Rf(0, Kommando, true, false)] }] };
        Pruefe.Gleich(Fehlercode.Ungueltig, g.Tun(Auftrag.PhaseSetzen(Admin, zuViel, g.Z.NetzListeVersion)).Fehler, "513 Felder");
    }

    [Test]
    public static void Aendern_DerLaufendenPhase_StelltSofortUm_LoeschenFaelltAufGrundstellung()
    {
        var f = Abend().MitPhasen();
        Ok(f, Auftrag.PhaseSchalten(Kobold, 2, 1), "Entern");
        // Der Admin ändert Entern im Raid: Marines senden jetzt auch in Kommando.
        var neu = Entern with { Varianten = [new PhasenVariante(Marine) { Felder = [Kennung(0, "CREW", true, false), Rf(1, Kommando, true, true), Rf(2, TruppAlpha, true, true, standard: true)] }] };
        var r = Ok(f, Auftrag.PhaseSetzen(Admin, neu, f.Z.NetzListeVersion), "Entern geändert, während sie läuft");
        Pruefe.Wahr(r.NetzListeGeaendert && r.BetroffeneKonten.Contains(Anker) && r.BetroffeneKonten.Contains(Capo), "Marines und CO umgestellt");
        Pruefe.Gleich("0:7h 1:2hs 2:50hs", Felder(f.Z.Konten[Anker]), "Anker darf jetzt in Kommando senden");
        Pruefe.Gleich("0:8hs 1:7hs", Felder(f.Z.Konten[Capo]), "der CO hat in Entern keine Variante mehr: Grundstellung");
        Pruefe.Gleich((byte)2, f.Schreiber.Neben.LaufendeNr(), "Entern läuft weiter");

        var loeschen = Ok(f, Auftrag.PhaseLoeschen(Admin, 2, f.Z.NetzListeVersion), "laufende Phase gelöscht");
        Pruefe.Wahr(loeschen.Phasenwechsel is { Nr: 1, Vorher: 2, Durch: Admin } && loeschen.NetzListeGeaendert, "fällt auf die Grundstellung zurück");
        Pruefe.Gleich("0:7hs 1:2h", Felder(f.Z.Konten[Anker]), "Anker in der Grundstellung");
        Pruefe.Falsch(f.Z.Phasen.ContainsKey(2), "Entern gelöscht");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Tun(Auftrag.PhaseLoeschen(Admin, 2, f.Z.NetzListeVersion)).Fehler, "nichts mehr zu löschen");
        Pruefe.Gleich(Fehlercode.Ungueltig, f.Tun(Auftrag.PhaseLoeschen(Admin, 1, f.Z.NetzListeVersion)).Fehler, "die Grundstellung löscht niemand");
    }

    [Test]
    public static void RolleUndNetzLoeschen_NehmenIhreVariantenMit_PaketAtomar()
    {
        var f = Abend().MitPhasen();
        Ok(f, Auftrag.NetzLoeschen(Admin, TruppAlpha, f.Z.NetzListeVersion), "Trupp Alpha gelöscht");
        Pruefe.Falsch(f.Z.Phasen[2].Varianten.Any(v => v.Felder.Any(x => x.NetzId == TruppAlpha)), "das Netz fällt aus den Varianten");
        Ok(f, Auftrag.RolleLoeschen(Admin, Co, f.Z.NetzListeVersion), "CO gelöscht");
        Pruefe.Wahr(f.Z.Phasen[2].VarianteFuer(Co) is null && f.Z.Phasen[2].VarianteFuer(Marine) is not null, "die Variante der Rolle fällt weg");
        Pruefe.Wahr(f.Z.Pruefen().Count == 0, "Stand stimmig");

        var g = Abend();
        var v = g.Z.NetzListeVersion;
        Verwaltungsschritt[] schritte = [new Verwaltungsschritt.PhaseSetzen(Entern), new Verwaltungsschritt.PhaseSetzen(Rueckzug), new Verwaltungsschritt.PhaseLoeschen(4)];
        var z = g.Z;
        var r = g.Tun(Auftrag.Paket(Admin, schritte, v));
        Pruefe.Wahr(r.Fehler == Fehlercode.Unbekannt && r.Fehlerliste.Single().Eintrag == 2 && ReferenceEquals(z, g.Z), "Paket: Fehler im dritten Schritt, nichts angewendet");
        var ok = g.Tun(Auftrag.Paket(Admin, schritte[..2], v));
        Pruefe.Wahr(ok.Erfolg && g.Z.Phasen.Count == 2 && g.Z.NetzListeVersion == g.Z.ZustandsVersion, "Paket mit zwei Phasen, eine NetzListe");
    }

    /// <summary>
    /// Gegenprüfung S2 (01.10.2026): Löscht der Admin ein Netz, das in einer Variante das einzige Feld war (an der Konsole mit
    /// netz-loeschen oder aus einem Verwaltungsteil 0.4.0, der keine Phasen kennt), blieb eine Variante ohne Feld stehen. Die gibt es
    /// auf dem Draht nicht, PhasePruefen lehnt sie ab, und beim nächsten Start lehnte der SchnappschussBauer den gespeicherten Stand
    /// ganz ab: Der Server startete nicht mehr. Jetzt fällt die Variante weg, die Rolle steht in der Phase wie in der Grundstellung,
    /// und läuft die Phase gerade, bekommt die Rolle sofort die Felder der Grundstellung.
    /// </summary>
    [Test]
    public static void NetzLoeschen_LetztesFeldEinerVariante_VarianteFaelltWeg_StandBleibtLadbar()
    {
        var f = Abend().MitPhasen();
        var stille = new Einsatzphase(4, "Stille", "STILLE") { Varianten = [new PhasenVariante(Gunner) { Felder = [Rf(0, Echo, true, false)] }] };
        Ok(f, Auftrag.PhaseSetzen(Admin, stille, f.Z.NetzListeVersion), "Phase 4 Stille: Gunner hören nur Echo");
        Ok(f, Auftrag.PhaseSchalten(Kobold, 4, 1), "Stille läuft");
        Pruefe.Gleich("0:90h", Felder(f.Z.Konten[Gunny]), "Gunny hört in Stille nur Echo");
        var flieger = f.Z.Konten[Flieger];

        var r = Ok(f, Auftrag.NetzLoeschen(Admin, Echo, f.Z.NetzListeVersion), "Echo gelöscht");
        var probleme = f.Z.Pruefen();
        Pruefe.Gleich(0, probleme.Count, "Stand stimmig: " + string.Join("; ", probleme));
        Pruefe.Wahr(f.Z.Phasen[4].VarianteFuer(Gunner) is null, "die Variante ohne Feld fällt weg");
        Pruefe.Gleich("0:7hs", Felder(f.Z.Konten[Gunny]), "Gunny hat in der laufenden Phase die Felder der Grundstellung");
        Pruefe.Wahr(f.Z.Konten[Gunny].Sendeauswahl.Felder.SequenceEqual([(byte)0]), "und sendet wieder in der Crew");
        Pruefe.Wahr(r.BetroffeneKonten.Contains(Gunny), "Gunny bekommt neue Mitgliedschaften");
        Pruefe.Gleich(flieger.KontoVersion, f.Z.Konten[Flieger].KontoVersion, "der Pilot bleibt unberührt");

        // So lädt der Server beim nächsten Start: über die Zustandsablage und den SchnappschussBauer.
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "s2-netzloeschen-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(verzeichnis);
        try
        {
            var ablage = new Funk.Server.Kern.Zustandsablage(verzeichnis);
            ablage.Speichern(f.Z, f.Schreiber.Neben);
            var (geladen, neben) = ablage.LageLaden();
            Pruefe.Wahr(geladen.Phasen[4].Varianten.IsEmpty && neben.Phase is { Nr: 4 }, "gespeichert und wieder geladen, Stille läuft weiter");
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    /// <summary>
    /// Gegenprüfung S2: Ein Stand mit einer Variante ohne Feld (geschrieben vom Bau vor dieser Korrektur) lädt trotzdem; die Variante
    /// fällt beim Laden weg, statt den Start des Servers zu verhindern.
    /// </summary>
    [Test]
    public static void Laden_VarianteOhneFeld_FaelltWeg_ServerStartet()
    {
        var f = Abend().MitPhasen();
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "s2-leervariante-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(verzeichnis);
        try
        {
            var ablage = new Funk.Server.Kern.Zustandsablage(verzeichnis);
            ablage.Speichern(f.Z, f.Schreiber.Neben);
            var pfad = Path.Combine(verzeichnis, Funk.Server.Kern.Zustandsablage.LageDatei);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(pfad))!;
            var entern = json["phasen"]!.AsArray().Single(p => p!["nr"]!.GetValue<int>() == 2)!;
            var co = entern["varianten"]!.AsArray().Single(v => v!["rolleId"]!.GetValue<int>() == Co)!;
            co["felder"] = new System.Text.Json.Nodes.JsonArray();
            File.WriteAllText(pfad, json.ToJsonString());

            var (geladen, _) = ablage.LageLaden();
            Pruefe.Wahr(geladen.Phasen[2].VarianteFuer(Co) is null && geladen.Phasen[2].VarianteFuer(Marine) is not null,
                "die leere Variante des CO fällt beim Laden weg, die der Marines bleibt");
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }
}
