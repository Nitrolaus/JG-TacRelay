// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Zuordnung 0.11.0 am Zustandsschreiber (6.9): Wer offline eingeteilt wurde, kommt beim Login als VERSCHIEBEN mit <c>durch</c>
/// zurück; "zurück auf Standard" ist keine Einteilung und setzt keine Reservierung; ein Konto ohne je eine Sitzung belegt nach
/// Ablauf seiner Reservierung keinen inzwischen vergebenen Platz mehr (vermutete Doppelbelegung, bestätigt).
/// </summary>
internal static class ZuordnungServerTests
{
    private const uint Admin = Funkprobe.Admin;
    private const uint B = Funkprobe.B;
    private const uint C = Funkprobe.C;
    private const uint D = Funkprobe.D;
    private const ushort Co = Funkprobe.RolleCo;
    private const ushort Pilot = Funkprobe.RollePilot;
    private const ushort Schiff = Funkprobe.Schiff3;
    private const long Minute = 60_000;

    /// <summary>Konten, die den Platz BELEGT oder RESERVIERT halten.</summary>
    private static int Inhaber(Funkprobe f, ushort schiff, ushort rolle) =>
        f.Z.Konten.Values.Count(k => k.Platz is { } p && p.SchiffId == schiff && p.RolleId == rolle && f.Z.PlatzZustandVon(k.Id, f.Jetzt) != PlatzZustand.Frei);

    [Test]
    public static void Login_NachVorabEinteilung_KommtAlsVerschiebenMitDurch()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(C))).Erfolg, "C offline");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(C) { RolleId = Co, SchiffId = Schiff }])).Erfolg, "Admin teilt C offline ein");
        Pruefe.Gleich(PlatzZustand.Reserviert, f.Z.PlatzZustandVon(C, f.Jetzt), "reserviert");
        var version = f.Z.Konten[C].KontoVersion;

        f.Jetzt += 11 * Minute;
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(C, 40_101, C * 10)).Erfolg, "C meldet sich nach 11 min an");
        var c = f.Z.Konten[C];
        Pruefe.Gleich(Quelle.Verschieben, c.Quelle, "VERSCHIEBEN statt WIEDERHERGESTELLT (seit 0.11.0)");
        Pruefe.Gleich(Admin, c.QuelleDurch, "durch den Admin");
        Pruefe.Gleich(Co, c.Platz!.Value.RolleId, "der Platz bleibt");
        Pruefe.Gleich(version, c.KontoVersion, "kein neuer kontoVersion: der Login ist keine Umstellung");

        // Gegenprobe: selbst gewählt, ohne Reservierung, innerhalb von 10 min zurück: WIEDERHERGESTELLT ohne durch.
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(B, null, Pilot, Schiff)).Erfolg, "B wählt selbst");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(B))).Erfolg, "B trennt");
        f.Jetzt += 5 * Minute;
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(B, 40_102, B * 10)).Erfolg, "B in der Frist zurück");
        Pruefe.Gleich(Quelle.Wiederhergestellt, f.Z.Konten[B].Quelle, "WIEDERHERGESTELLT");
        Pruefe.Gleich(0u, f.Z.Konten[B].QuelleDurch, "ohne durch");
    }

    [Test]
    public static void ZurueckAufStandard_SetztKeineReservierung()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(C) { RolleId = Co, SchiffId = Schiff }])).Erfolg, "Admin teilt C ein");
        Pruefe.Wahr(f.Z.Konten[C].Reservierung is { Quelle: Quelle.Verschieben } r && r.Durch == Admin, "eine Einteilung reserviert");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(C) { RolleId = 0 }])).Erfolg, "zurück auf Standard");
        var c = f.Z.Konten[C];
        Pruefe.Gleich(null, c.Reservierung, "keine Reservierung");
        Pruefe.Gleich(null, c.Platz, "kein Platz");
        Pruefe.Gleich(Co, c.LetzterStand!.Platz!.Value.RolleId, "der letzte Platz bleibt für \"wie zuletzt\"");

        Pruefe.Wahr(f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(C))).Erfolg, "C trennt");
        f.Jetzt += 11 * Minute;
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(C, 40_201, C * 10)).Erfolg, "C nach 11 min");
        Pruefe.Gleich(Quelle.Standard, f.Z.Konten[C].Quelle, "STANDARD: der Client darf die gemerkte Rolle wieder wählen");

        // Mit Feldern ist es eine Einteilung, auch mit rolleId 0.
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { RolleId = 0, FelderPatch = [new Feldpatch(0, Funkprobe.Kommando)] }])).Erfolg,
            "Standard mit einem Feld");
        Pruefe.Wahr(f.Z.Konten[D].Reservierung is { Quelle: Quelle.Verschieben }, "das reserviert weiter");
    }

    /// <summary>
    /// Praesenz seit 0.11.2 (Sarevoc 13.10.2026: der Stammbaum "kann in der Vorplanung verwendet werden, um die zu erwartenden Teilnehmer vorzusortieren"):
    /// Wer offline nur in Kanäle eingeteilt wurde (frische Reservierung ohne Platz), erscheint in Praesenz, damit die Raid-Seite ihn im Baum zeigt;
    /// bis 0.11.1 nur mit Sitzung oder Platz. Ohne Einteilung nicht, und nach Ablauf der Reservierung nicht mehr. Online und mit Platz wie bisher.
    /// </summary>
    [Test]
    public static void Praesenz_OfflineMitFrischerReservierungOhnePlatz()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(Funk.Server.Steuer.Abbildung.InPraesenz(f.Z, f.Z.Konten[D], f.Jetzt), "D online: in Praesenz");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(D))).Erfolg, "D offline");
        Pruefe.Falsch(Funk.Server.Steuer.Abbildung.InPraesenz(f.Z, f.Z.Konten[D], f.Jetzt), "offline, ohne Platz, ohne Einteilung: nicht in Praesenz");

        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(D) { RolleId = 0, FelderPatch = [new Feldpatch(0, Funkprobe.Kommando)] }])).Erfolg, "vorab nur in einen Kanal");
        var d = f.Z.Konten[D];
        Pruefe.Wahr(d is { Platz: null, Reservierung: { } } && !f.Z.IstOnline(D), "offline, ohne Platz, mit Reservierung");
        Pruefe.Gleich(PlatzZustand.Frei, f.Z.PlatzZustandVon(D, f.Jetzt), "kein Platz belegt oder reserviert");
        Pruefe.Wahr(Funk.Server.Steuer.Abbildung.InPraesenz(f.Z, d, f.Jetzt), "erscheint in Praesenz (seit 0.11.2)");

        f.Jetzt += Fristen.ReservierungMs - 1;
        Pruefe.Wahr(Funk.Server.Steuer.Abbildung.InPraesenz(f.Z, f.Z.Konten[D], f.Jetzt), "kurz vor Ablauf noch");
        f.Jetzt += 1;
        Pruefe.Falsch(Funk.Server.Steuer.Abbildung.InPraesenz(f.Z, f.Z.Konten[D], f.Jetzt), "nach Ablauf der Reservierung (12 h) nicht mehr");

        // Offline mit Platz (reserviert) wie bisher.
        Pruefe.Wahr(f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(C))).Erfolg, "C offline");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(C) { RolleId = Co, SchiffId = Schiff }])).Erfolg, "C vorab auf CO");
        Pruefe.Wahr(Funk.Server.Steuer.Abbildung.InPraesenz(f.Z, f.Z.Konten[C], f.Jetzt), "offline mit Platz: in Praesenz");
    }

    /// <summary>
    /// Vermutete Doppelbelegung (ist-stand.md, bestätigt am 12.10.2026): Ein Konto ohne je eine Sitzung kehrte beim Login vor jeder
    /// Prüfung zurück (SitzungsEnde 0) und behielt seinen Platz, auch nach Ablauf der Reservierung und obwohl der Platz inzwischen
    /// vergeben war. Seit 0.11.0 behält nur ein Konto ohne jede Reservierung seinen Anfangsstand.
    /// </summary>
    [Test]
    public static void Login_OhneJeEineSitzung_NachAblaufDerReservierung_KeineDoppelbelegung()
    {
        const uint neuling = 1200;
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = neuling, Name = "Neuling" })).Erfolg, "Konto ohne Sitzung");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(neuling) { RolleId = Pilot, SchiffId = Schiff }])).Erfolg, "vorab auf Pilot");
        Pruefe.Gleich(0L, f.Z.Konten[neuling].SitzungsEnde, "nie angemeldet");

        f.Jetzt += Fristen.ReservierungMs;
        Pruefe.Gleich(PlatzZustand.Frei, f.Z.PlatzZustandVon(neuling, f.Jetzt), "nach 12 h frei");
        Pruefe.Wahr(f.Tun(Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Erfolg, "D nimmt den freien Platz");

        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(neuling, 41_200, 12_000)).Erfolg, "der Neuling meldet sich an");
        var k = f.Z.Konten[neuling];
        Pruefe.Gleich(1, Inhaber(f, Schiff, Pilot), "ein Inhaber auf einem Platz mit Kapazität 1");
        Pruefe.Gleich(null, k.Platz, "ohne Platz");
        Pruefe.Gleich(Quelle.Standard, k.Quelle, "STANDARD");
        Pruefe.Gleich(Pilot, k.LetzterStand!.Platz!.Value.RolleId, "der verfallene Platz steht im letzten Stand");
        Pruefe.Gleich(Pilot, f.Z.Konten[D].Platz!.Value.RolleId, "D behält den Platz");

        // Ein Konto, das nie eingeteilt wurde, behält seinen Anfangsstand wie bisher.
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1201, Name = "Frisch", Felder = [Funkprobe.F(0, Funkprobe.Kommando, true, true)] })).Erfolg, "Konto mit Feldern");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(1201, 41_201, 12_010)).Erfolg, "erste Anmeldung");
        Pruefe.Gleich(Funkprobe.Kommando, f.Z.Konten[1201].Felder.Single().NetzId, "Anfangsstand bleibt");
    }

    /// <summary>
    /// Grenzfall aus AP1: "zurück auf Standard" an einem Konto, das nie angemeldet war, ließ beim ersten Login VERSCHIEBEN mit durch
    /// stehen (keine Reservierung, also kein Weg über AufStandard). Seit 0.11.0 kommt es als STANDARD, der Client sagt nichts an.
    /// </summary>
    [Test]
    public static void Login_OhneJeEineSitzung_NachZurueckAufStandard_KommtAlsStandard()
    {
        const uint neuling = 1300;
        var f = new Funkprobe();
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = neuling, Name = "Neuling" })).Erfolg, "Konto ohne Sitzung");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(neuling) { RolleId = Pilot, SchiffId = Schiff }])).Erfolg, "vorab auf Pilot");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(neuling) { RolleId = 0 }])).Erfolg, "zurück auf Standard");
        Pruefe.Wahr(f.Z.Konten[neuling] is { Quelle: Quelle.Verschieben, Reservierung: null, Platz: null }, "VERSCHIEBEN ohne Reservierung");
        var version = f.Z.Konten[neuling].KontoVersion;

        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(neuling, 41_300, 13_000)).Erfolg, "erste Anmeldung");
        var k = f.Z.Konten[neuling];
        Pruefe.Gleich(Quelle.Standard, k.Quelle, "STANDARD statt einer Einteilung");
        Pruefe.Gleich(0u, k.QuelleDurch, "ohne durch");
        Pruefe.Gleich(version, k.KontoVersion, "kein neuer kontoVersion");
        Pruefe.Gleich(Pilot, k.LetzterStand!.Platz!.Value.RolleId, "der letzte Platz bleibt für \"wie zuletzt\"");

        // Mit Feldern bleibt es eine Einteilung (Reservierung): VERSCHIEBEN mit durch.
        Pruefe.Wahr(f.Tun(Auftrag.KontoAnlegen(new Konto { Id = 1301, Name = "Zweiter" })).Erfolg, "zweites Konto ohne Sitzung");
        Pruefe.Wahr(f.Tun(Auftrag.Verschieben(Admin, [new Aenderung(1301) { RolleId = 0, FelderPatch = [new Feldpatch(0, Funkprobe.Kommando)] }])).Erfolg, "nur ein Feld");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(1301, 41_301, 13_010)).Erfolg, "erste Anmeldung");
        Pruefe.Wahr(f.Z.Konten[1301] is { Quelle: Quelle.Verschieben } z && z.QuelleDurch == Admin, "eingeteilt von Admin");
    }

    /// <summary>
    /// Ch. je Person (Q46 für die Einsatzleitung, seit 0.11.0): Verschieben mit felderPatch und Feldnummer ersetzt nur den Ch. dieses
    /// Felds; Platz und gewählte Rolle bleiben. Der Funker darf den Ch. danach selbst ändern (Q45, auch im festen Feld), und eine neue
    /// Wahl der Rolle behält den Ch. der Leitung (Ch.-Auflösung Schritt 3, der aktuelle Ch. im Kanalnetz).
    /// </summary>
    [Test]
    public static void ChJePerson_FelderPatchMitFeld_ErsetztNurDenCh()
    {
        const uint F = Kanalprobe.F;
        var f = Kanalprobe.Neu();
        Kanalprobe.Ok(f, Auftrag.RolleWaehlen(F, Kanalprobe.Co), "F wählt Kapitän");
        Pruefe.Gleich("0:101/0hs 1:100/0hs 2:102/0hs", Kanalprobe.Felder(f, F), "alles auf Ruhe");

        Kanalprobe.Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(F) { FelderPatch = [new Feldpatch(1, Kanalprobe.K(Kanalprobe.KanalBruecke, 7), true, true, false)] }]),
            "die Leitung stellt Feld 2 auf Schiff 7");
        var k = Kanalprobe.Konto(f, F);
        Pruefe.Gleich("0:101/0hs 1:100/7hs 2:102/0hs", Kanalprobe.Felder(f, F), "nur Feld 2 ändert sich, hören und senden wie geschickt, nicht fest");
        Pruefe.Gleich((ushort?)Kanalprobe.Co, k.GewaehlteRolle, "die gewählte Rolle bleibt");
        Pruefe.Gleich(null, k.Platz, "kein Platz");
        Pruefe.Wahr(k is { Quelle: Quelle.Verschieben, Reservierung.Quelle: Quelle.Verschieben } && k.QuelleDurch == Admin, "eingeteilt von Admin, reserviert");

        Kanalprobe.Ok(f, Auftrag.RolleWaehlen(F, Kanalprobe.Co), "F wählt die Rolle noch einmal");
        Pruefe.Gleich("0:101/0hs 1:100/7hs 2:102/0hs", Kanalprobe.Felder(f, F), "der Ch. der Leitung bleibt");

        Kanalprobe.Ok(f, Auftrag.FeldSetzen(F, 1, Kanalprobe.K(Kanalprobe.KanalBruecke, 3)), "F stellt selbst Schiff 3 ein (Q45)");
        Pruefe.Gleich("0:101/0hs 1:100/3hs 2:102/0hs", Kanalprobe.Felder(f, F), "der Vorschlag ist geändert");

        // Mit Platz: der Platz bleibt, auch ein festes Feld behält fest.
        Kanalprobe.Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(Kanalprobe.G) { RolleId = Kanalprobe.FestRolle }]), "G auf die feste Brücke");
        Pruefe.Gleich("0:100/5hsf", Kanalprobe.Felder(f, Kanalprobe.G), "Ch. 5 fest aus der Rolle");
        Kanalprobe.Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(Kanalprobe.G) { FelderPatch = [new Feldpatch(0, Kanalprobe.K(Kanalprobe.KanalBruecke, 9), true, true, true)] }]),
            "die Leitung wählt Schiff 9");
        Pruefe.Gleich("0:100/9hsf", Kanalprobe.Felder(f, Kanalprobe.G), "Schiff 9, weiter fest");
        Pruefe.Gleich(Kanalprobe.FestRolle, Kanalprobe.Konto(f, Kanalprobe.G).Platz!.Value.RolleId, "der Platz bleibt");
        Kanalprobe.Ok(f, Auftrag.FeldSetzen(Kanalprobe.G, 0, Kanalprobe.K(Kanalprobe.KanalBruecke, 2)), "G ändert den Ch. im festen Feld (Q45)");
        Pruefe.Gleich("0:100/2hsf", Kanalprobe.Felder(f, Kanalprobe.G), "geändert");
    }

    /// <summary>
    /// Befund der Prüfung 0.11.0: Online eingeteilt, danach selbst einen Ch. oder eine Rolle gewählt (Quelle SELBST, die Reservierung
    /// bleibt), dann kurz getrennt. Bis zum Fix kam der Login als VERSCHIEBEN durch den Admin zurück: Das Brett zeigte "von Admin" statt
    /// "selbst", alte Clients ", eingeteilt von", und ein Client 0.11.0 sagte die Einteilung noch einmal an. Jetzt bleibt SELBST; ohne
    /// eigene Änderung kommt weiter VERSCHIEBEN mit durch.
    /// </summary>
    [Test]
    public static void Login_NachEigenerAenderung_BleibtSelbst()
    {
        var f = Kanalprobe.Neu();
        foreach (var konto in new[] { Kanalprobe.F, Kanalprobe.G, Kanalprobe.H })
            Kanalprobe.Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(konto) { RolleId = Kanalprobe.Co }]), "Admin teilt " + konto + " online ein");
        Kanalprobe.Ok(f, Auftrag.FeldSetzen(Kanalprobe.F, 1, Kanalprobe.K(Kanalprobe.KanalBruecke, 3)), "F stellt selbst Ch. 3 ein");
        Kanalprobe.Ok(f, Auftrag.RolleWaehlen(Kanalprobe.G, Kanalprobe.Co), "G wählt selbst seine Rolle");
        var versionF = Kanalprobe.Konto(f, Kanalprobe.F).KontoVersion;
        foreach (var konto in new[] { Kanalprobe.F, Kanalprobe.G, Kanalprobe.H }) Kanalprobe.Ok(f, Auftrag.SitzungBeenden(Funkprobe.S(konto)), konto + " trennt");

        f.Jetzt += Minute;
        Kanalprobe.Ok(f, Auftrag.SitzungAktivieren(Kanalprobe.F, 50_001, Kanalprobe.F * 10), "F nach 1 min zurück");
        Kanalprobe.Ok(f, Auftrag.SitzungAktivieren(Kanalprobe.G, 50_002, Kanalprobe.G * 10), "G nach 1 min zurück");
        Kanalprobe.Ok(f, Auftrag.SitzungAktivieren(Kanalprobe.H, 50_003, Kanalprobe.H * 10), "H nach 1 min zurück");
        var fk = Kanalprobe.Konto(f, Kanalprobe.F);
        Pruefe.Wahr(fk is { Quelle: Quelle.Selbst, QuelleDurch: 0 }, "F: SELBST bleibt, ohne durch (" + fk.Quelle + ", " + fk.QuelleDurch + ")");
        Pruefe.Gleich(versionF, fk.KontoVersion, "F: kein neuer kontoVersion");
        Pruefe.Gleich(Kanalprobe.Co, fk.Platz!.Value.RolleId, "F: der Platz bleibt");
        Pruefe.Gleich("0:101/0hs 1:100/3hs 2:102/0hs", Kanalprobe.Felder(f, Kanalprobe.F), "F: sein Ch. bleibt");
        Pruefe.Wahr(Kanalprobe.Konto(f, Kanalprobe.G) is { Quelle: Quelle.Selbst, QuelleDurch: 0 }, "G: SELBST nach der eigenen Rollenwahl");
        Pruefe.Wahr(Kanalprobe.Konto(f, Kanalprobe.H) is { Quelle: Quelle.Verschieben, QuelleDurch: Admin }, "H ohne eigene Änderung: VERSCHIEBEN durch den Admin");

        // Nach der Frist von 10 min hält die Reservierung den Platz weiter; die Quelle bleibt die eigene.
        Kanalprobe.Ok(f, Auftrag.SitzungBeenden(50_001), "F trennt noch einmal");
        f.Jetzt += 11 * Minute;
        Kanalprobe.Ok(f, Auftrag.SitzungAktivieren(Kanalprobe.F, 50_004, Kanalprobe.F * 10), "F nach 11 min zurück");
        Pruefe.Wahr(Kanalprobe.Konto(f, Kanalprobe.F) is { Quelle: Quelle.Selbst, Platz.RolleId: Kanalprobe.Co }, "F: SELBST, Platz reserviert");
    }

    /// <summary>
    /// Befund der Prüfung 0.11.0: Nach der Rollenwahl steht alles auf Ruhe, und nichts ist Sendeziel. Stellt die Leitung per Ch. je Person
    /// ein sendendes Feld von der Ruhe auf einen Ch., wird es Sendeziel wie bei FeldSetzen Regel 7 (0.9.1); bis zum Fix hörte die Person
    /// den Ch., sendete aber nirgendwohin. Eine vorhandene Auswahl bleibt, ein Feld ohne senden wird kein Ziel.
    /// </summary>
    [Test]
    public static void ChJePerson_VonRuheAufCh_WirdSendeziel()
    {
        var f = Kanalprobe.Neu();
        foreach (var konto in new[] { Kanalprobe.F, Kanalprobe.G, Kanalprobe.H }) Kanalprobe.Ok(f, Auftrag.RolleWaehlen(konto, Kanalprobe.Co), konto + " wählt Co");
        Pruefe.Gleich("[]", Kanalprobe.Konto(f, Kanalprobe.F).Sendeauswahl.ToString(), "nach der Rollenwahl kein Sendeziel");

        Kanalprobe.Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(Kanalprobe.F) { FelderPatch = [new Feldpatch(1, Kanalprobe.K(Kanalprobe.KanalBruecke, 7), true, true, false)] }]),
            "die Leitung stellt F Feld 2 auf Schiff 7");
        Pruefe.Gleich("[1]", Kanalprobe.Konto(f, Kanalprobe.F).Sendeauswahl.ToString(), "F sendet jetzt in Schiff 7");

        Kanalprobe.Ok(f, Auftrag.FeldSetzen(Kanalprobe.G, 0, Kanalprobe.K(Kanalprobe.Kommando, 2)), "G stellt selbst Kommando 2 ein");
        Pruefe.Gleich("[0]", Kanalprobe.Konto(f, Kanalprobe.G).Sendeauswahl.ToString(), "G sendet in Kommando 2 (Regel 7)");
        Kanalprobe.Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(Kanalprobe.G) { FelderPatch = [new Feldpatch(1, Kanalprobe.K(Kanalprobe.KanalBruecke, 7), true, true, false)] }]),
            "die Leitung stellt G Feld 2 auf Schiff 7");
        Pruefe.Gleich("[0]", Kanalprobe.Konto(f, Kanalprobe.G).Sendeauswahl.ToString(), "G: die vorhandene Auswahl bleibt");

        Kanalprobe.Ok(f, Auftrag.Verschieben(Admin, [new Aenderung(Kanalprobe.H) { FelderPatch = [new Feldpatch(1, Kanalprobe.K(Kanalprobe.KanalBruecke, 7), true, false, false)] }]),
            "die Leitung stellt H Feld 2 nur zum Hören auf Schiff 7");
        Pruefe.Gleich("[]", Kanalprobe.Konto(f, Kanalprobe.H).Sendeauswahl.ToString(), "H: ohne senden kein Sendeziel");
    }
}
