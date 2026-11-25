// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using Funk.Protokoll.Steuerung;
using Funk.Server.Steuer;
using Funk.Verwaltung.Flotte;

namespace Funk.Tests;

/// <summary>
/// Ausbaustufe S2, Teil C1 am Flottenentwurf (RAID-S2-ENTWURF Abschnitt 3 "Anlegen"): Phasen sind eigene Objekte im Entwurf.
/// "Variante anlegen" kopiert die Felder der Grundstellung der Rolle, eine Variante ergibt ein Paket mit PHASE_SETZEN nach den
/// Rollen, "wie Grundstellung" entfernt sie, gelöschte Phasen gehen zuerst. Die Grundstellung, die der Server selbst nennt, ist
/// keine Änderung. An einem Server 0.4.0 geht kein Phasenschritt hinaus. Geprüft am echten Zustandsschreiber.
/// </summary>
internal static class S2FlottePhasenTests
{
    private static string Phasentext(NetzListe l) => JsonSerializer.Serialize(
        (l.Phasen ?? []).Select(p => p with { Felder = p.Felder is null ? null : [.. p.Felder.OrderBy(f => f.RolleId).ThenBy(f => f.Feld)] })
            .Select(p => JsonSerializer.Serialize(p, SteuerJson.Info<NetzPhase>())));

    [Test]
    public static void Variante_ErgibtPhaseSetzen_WieGrundstellungEntferntSie()
    {
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var e = Flottenentwurf.Aus(Abbildung.NetzListe(s.Aktuell));
        e.PhasenErlaubt = true;
        Pruefe.Gleich(0, e.Phasen.Count, "ein frischer Server hat keine Phasen");

        var entern = e.PhaseAnlegen("Entern", "ENTERN");
        Pruefe.Wahr(entern is { Nr: 2, Name: "Entern", Kurz: "ENTERN" }, "die nächste freie Nummer");
        Pruefe.Gleich("1 Grundstellung|2 Entern", string.Join("|", e.Phasen.Select(p => p.Nr + " " + p.Name)), "die Grundstellung kommt mit");
        Pruefe.Gleich("neu: Phase 2 Entern", string.Join(" | ", e.Aenderungen()), "die Grundstellung des Servers ist keine Änderung");

        var co = e.Rollen.Single(r => r.Name == "Kapitän");
        e.VarianteAnlegen(2, co.Id);
        Pruefe.Gleich(co.Felder.Count, e.Variante(2, co.Id).Count, "Variante anlegen kopiert die Felder der Grundstellung");
        Pruefe.Gleich("Kapitän (alle Schiffe)", e.Betroffene(2), "wen sie trifft");
        // In Phase 2 hört der CO Reserve 6 nicht mehr und sendet zuerst in die Crew der eigenen Einheit.
        e.VarianteSetzen(2, co.Id, [.. e.Variante(2, co.Id).Where(f => f.Feld != 8).Select(f => f with { SendenStandard = f.Schiffsnetz == "CREW" })]);
        Pruefe.Falsch(e.Pruefen().Any(b => b.Sperrt), "nichts, was den Server stört: " + string.Join(" | ", e.Pruefen().Select(b => b.Text)));

        var p = e.Paket();
        Pruefe.Wahr(p.Paket is not null, "Paket: " + p.Fehler);
        var ops = p.Paket!.Ops.Select(o => o.Op).ToList();
        Pruefe.Gleich("PhaseSetzen", string.Join(",", ops), "ein Schritt, ohne die Grundstellung");
        var objekt = p.Paket.Ops[0].Objekt.Deserialize(SteuerJson.Info<NetzPhase>())!;
        Pruefe.Wahr(objekt.Nr == 2 && objekt.Felder!.Count == co.Felder.Count - 1 && objekt.Felder.All(f => f.RolleId == co.Id), "die Variante flach mit ihrer Rolle");

        FlottenentwurfTests.Anwenden(s, p.Paket);
        var nachher = Abbildung.NetzListe(s.Aktuell);
        Pruefe.Gleich(Phasentext(e.AlsNetzListe()), Phasentext(nachher), "der Server steht wie der Entwurf");
        var neu = Flottenentwurf.Aus(nachher);
        neu.PhasenErlaubt = true;
        Pruefe.Gleich(0, neu.Aenderungen().Count, "neu geladen: keine Änderung");

        // "wie Grundstellung": die Variante fällt weg, die Phase bleibt.
        neu.VarianteEntfernen(2, co.Id);
        Pruefe.Gleich("geändert: Phase 2 Entern", string.Join(" | ", neu.Aenderungen()), "gezählt");
        Pruefe.Gleich("niemand: alle Rollen wie in der Grundstellung", neu.Betroffene(2), "trifft niemanden mehr");
        FlottenentwurfTests.Anwenden(s, neu.Paket().Paket!);
        var leer = Abbildung.NetzListe(s.Aktuell).Phasen!.Single(x => x.Nr == 2);
        Pruefe.Wahr(leer.Felder is null && leer.Name == "Entern", "am Server ohne Variante");
    }

    [Test]
    public static void Phasen_LoeschenZuerst_FolgenVonRolleUndNetz_DateiUndGrenzen()
    {
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var e = Flottenentwurf.Aus(Abbildung.NetzListe(s.Aktuell));
        e.PhasenErlaubt = true;
        var co = e.Rollen.Single(r => r.Name == "Kapitän");
        e.PhaseAnlegen("Anflug", "ANFLUG");
        e.PhaseAnlegen("Entern", "ENTERN");
        e.VarianteAnlegen(3, co.Id);
        FlottenentwurfTests.Anwenden(s, e.Paket().Paket!);

        // Datei und Sicherung tragen die Phasen.
        var datei = Flottenentwurf.JsonLesen(Flottenentwurf.Aus(Abbildung.NetzListe(s.Aktuell)).AlsJson())!;
        Pruefe.Gleich("1,2,3", string.Join(",", datei.Phasen!.Select(p => p.Nr)), "die Phasen stehen in der Datei");

        e = Flottenentwurf.Aus(Abbildung.NetzListe(s.Aktuell));
        e.PhasenErlaubt = true;
        e.PhaseLoeschen(1);
        Pruefe.Wahr(e.Phase(1) is not null, "die Grundstellung bleibt");
        e.PhaseLoeschen(2);
        var ausweich6 = e.Netze.Single(n => n.Name == "Reserve 6");
        e.NetzLoeschen(ausweich6.Id);
        Pruefe.Falsch(e.Variante(3, co.Id).Any(f => f.NetzId == ausweich6.Id), "ein gelöschtes Netz fällt aus der Variante");
        var ops = e.Paket().Paket!.Ops.Select(o => o.Op).ToList();
        Pruefe.Wahr(ops[0] == VerwaltungsOperation.PhaseLoeschen && ops[^1] == VerwaltungsOperation.PhaseSetzen && ops.IndexOf(VerwaltungsOperation.NetzLoeschen) > 0,
            "erst Phasen löschen, zuletzt setzen: " + string.Join(",", ops));
        FlottenentwurfTests.Anwenden(s, e.Paket().Paket!);
        Pruefe.Gleich("1,3", string.Join(",", Abbildung.NetzListe(s.Aktuell).Phasen!.Select(p => p.Nr)), "Phase 2 ist weg");

        e = Flottenentwurf.Aus(Abbildung.NetzListe(s.Aktuell));
        e.PhasenErlaubt = true;
        e.RolleLoeschen(co.Id);
        Pruefe.Gleich(0, e.RollenMitVariante(3).Count, "mit der Rolle fällt ihre Variante");

        // Grenzen: vier Phasen über der Grundstellung, Namen, Global gehört nicht in eine Variante.
        e = Flottenentwurf.Aus(Abbildung.NetzListe(FlottenentwurfTests.SchiffsflottenServer().Aktuell));
        e.PhasenErlaubt = true;
        for (var i = 0; i < 4; i++) Pruefe.Wahr(e.PhaseAnlegen("P" + i, "P" + i) is not null, "Phase " + (i + 2));
        Pruefe.Gleich(null, e.PhaseAnlegen("Zu viel", "ZUVIEL"), "mehr als vier gehen nicht");
        e.PhaseSetzen(e.Phase(2)! with { Name = new string('x', Grenzen.PhaseNameMax + 1), Kurz = "" });
        string Texte() => string.Join(" | ", e.Pruefen().Where(b => b.Sperrt).Select(b => b.Text));
        Pruefe.Wahr(Texte().Contains("Phase 2 " + new string('x', Grenzen.PhaseNameMax + 1) + ": Name länger als 24 Zeichen.", StringComparison.Ordinal)
            && Texte().Contains(": Kurzname fehlt.", StringComparison.Ordinal), "Namen: " + Texte());
        e.VarianteSetzen(3, co.Id, [new RollenFeld { Feld = 0, NetzId = Grenzen.GlobalNetzId, Hoeren = true, Senden = true, SendenStandard = false, Fest = false }]);
        Pruefe.Wahr(Texte().Contains("Phase 3 P1, Kapitän: Feld 1 nutzt Global", StringComparison.Ordinal), "Global: " + Texte());
    }

    [Test]
    public static void Phasen_AnEinemServer040_KeinPhasenschritt()
    {
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var basis = Abbildung.NetzListe(s.Aktuell);
        var mitPhasen = Flottenentwurf.Aus(basis);
        mitPhasen.PhasenErlaubt = true;
        mitPhasen.PhaseAnlegen("Entern", "ENTERN");
        mitPhasen.VarianteAnlegen(2, mitPhasen.Rollen.Single().Id);

        // Eine Sicherung mit Phasen, in den Entwurf eines Servers 0.4.0 geladen: Netze und Rollen gehen, Phasen nicht.
        var e = Flottenentwurf.Aus(basis, mitPhasen.AlsNetzListe());
        Pruefe.Falsch(e.PhasenErlaubt, "ohne Zusage des Servers keine Phasen");
        Pruefe.Gleich(0, e.Aenderungen().Count, "keine Änderung, die hinausginge");
        Pruefe.Gleich("Nichts zu übernehmen.", e.Paket().Fehler, "kein PHASE_SETZEN an einen Server 0.4.0");
        e.NetzSetzen(e.Netze.Single(n => n.Name == "Reserve 6") with { Name = "Reserve" });
        Pruefe.Falsch(e.Paket().Paket!.Ops.Any(o => o.Op is VerwaltungsOperation.PhaseSetzen or VerwaltungsOperation.PhaseLoeschen), "nur die Netzänderung");
    }
}
