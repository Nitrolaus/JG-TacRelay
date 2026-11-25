// JG TacRelay - Maintainer: Sarevoc
using Funk.Verwaltung.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Ausbaustufe S2, Teil C1 im Verwaltungsteil (RAID-S2-ENTWURF Abschnitt 3 "Anlegen"): der Reiter <i>Flotte &gt; Phasen</i>. Liste
/// mit Nummer, Name und Kurzname, je Phase die Rollen mit Variante; "Variante anlegen" kopiert die Felder der Grundstellung in den
/// Felder-Editor 2x5 mit Vorschau, "wie Grundstellung" entfernt die Variante, darunter steht, wen sie trifft. Übernehmen wie alles
/// der Flotte (Paket, Sicherung, Rückfrage), dazu die Rückfrage, wenn die laufende Phase sich ändert. Ohne Server 0.5.0 kein Reiter.
/// </summary>
internal static class S2FlottePhasenOberflaecheTests
{
    private static (VerwaltungsdienstAttrappe D, VerwaltungViewModel Vm, FlotteViewModel F) Aufbauen(Testordner ordner, bool phasen = true)
    {
        var d = new VerwaltungsdienstAttrappe { Ablageordner = ordner.Pfad, Eingeteilte = 0, PhasenBekannt = phasen };
        d.Auftraege.Clear();
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        vm.Bereich = Verwaltungsbereich.Flotte;
        return (d, vm, vm.Flotte);
    }

    [Test]
    public static async Task Phasen_AnlegenVarianteWieGrundstellungUndUebernehmen()
    {
        using var ordner = new Testordner();
        var (d, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        Pruefe.Wahr(f.PhasenSichtbar, "Server 0.5.0: Reiter da");
        f.IstPhasen = true;
        Pruefe.Gleich(FlotteReiter.Phasen, f.Reiter, "Reiter gewählt");
        Pruefe.Gleich("1 Grundstellung: GRUND, die Felder der Rollen", string.Join(" | ", f.Phasen.Select(p => p.Name + ": " + p.Text)), "die Grundstellung steht immer da");

        f.PhaseNeuCommand.Execute(null);
        var phase = f.Phase!;
        Pruefe.Wahr(f.GewaehltePhase?.Id == 2 && phase.Nr == 2 && phase.Name == "Phase 2" && phase.Kurz == "P2", "neue Phase gewählt");
        phase.Name = "Entern";
        phase.Kurz = "ENTERN";
        Pruefe.Gleich("2 Entern: ENTERN, noch keine Variante", f.Phasen[1].Name + ": " + f.Phasen[1].Text, "Liste folgt");
        Pruefe.Gleich("Wen sie trifft: niemand: alle Rollen wie in der Grundstellung", phase.BetroffeneText, "noch niemand");

        // Variante anlegen: die Felder der Grundstellung im Felder-Editor, mit Vorschau.
        phase.GewaehlteRolle = phase.Rollen.Single(r => r.Name == "Kapitän");
        Pruefe.Gleich("wie Grundstellung", phase.GewaehlteRolle.Text, "noch ohne Variante");
        Pruefe.Wahr(phase.VarianteAnlegenCommand.CanExecute(null) && !phase.WieGrundstellungCommand.CanExecute(null), "anlegen geht, zurück noch nicht");
        phase.VarianteAnlegenCommand.Execute(null);
        var v = phase.Variante!;
        Pruefe.Wahr(v.Felder.Count == 9 && v.Felder[0] is { Quelle.Text: "Kommando", Vorschlag: true } && v.Felder[2].Quelle.Text == "Crew der eigenen Einheit",
            "die Felder der Grundstellung");
        Pruefe.Gleich("Kapitän in Phase 2 Entern", v.Titel, "Titel");
        v.Felder[8].Quelle = v.Quellen[0];
        v.VorschauEinheit = v.VorschauEinheiten.Single(x => x.Text == "Schiff 2");
        Pruefe.Gleich("So sieht Kapitän auf Schiff 2 in Phase 2 Entern aus: 1 Kommando (Sendevorschlag), 2 Durchsagen 2, 3 Crew 2, 4 Brücke 2, 5 Reserve 2, 6 Reserve 3, 7 Reserve 4, 8 Reserve 5, dazu Global.",
            v.VorschauText, "Vorschau ohne Feld 9");
        Pruefe.Gleich("eigene Variante, 8 Felder", phase.GewaehlteRolle.Text, "Zeile folgt");
        Pruefe.Gleich("Wen sie trifft: Kapitän (alle Schiffe)", phase.BetroffeneText, "Betroffene");
        Pruefe.Gleich("2 Entern: ENTERN, 1 Rolle mit Variante", f.Phasen[1].Name + ": " + f.Phasen[1].Text, "Liste zählt die Varianten");
        Pruefe.Gleich("neu: Phase 2 Entern", string.Join(" | ", f.Aenderungen), "eine Änderung, ohne die Grundstellung");

        // Übernehmen: ein Paket mit PHASE_SETZEN, ohne Rückfrage (niemand eingeteilt, kein Auftrag, die Phase läuft nicht).
        var version = d.Flotte.NetzListe!.Version;
        await f.UebernehmenCommand.ExecuteAsync(null);
        Pruefe.Gleich(null, f.Rueckfrage, "keine Rückfrage");
        Pruefe.Wahr(d.Aufrufe.Contains("Flotte 1 v" + version), "ein Schritt: " + string.Join(" | ", d.Aufrufe));
        Pruefe.Gleich("Keine Änderungen", f.AenderungenText, "der Server steht wie der Entwurf");
        Pruefe.Wahr(f.Phase is { Nr: 2, Name: "Entern" } && f.Phase.Variante is not null, "Formular und Variante bleiben nach dem Übernehmen");

        // Die laufende Phase ändern: Rückfrage, denn der Server stellt sofort um.
        d.LaufendePhase = 2;
        vm.Aktualisieren();
        Pruefe.Gleich("2 Entern: ENTERN, 1 Rolle mit Variante, läuft gerade", f.Phasen[1].Name + ": " + f.Phasen[1].Text, "läuft");
        Pruefe.Wahr(f.Phase!.LaeuftText is { Length: > 0 }, "Hinweis im Formular");
        f.Phase.GewaehlteRolle = f.Phase.Rollen.Single(r => r.Name == "Kapitän");
        f.Phase.WieGrundstellungCommand.Execute(null);
        Pruefe.Wahr(f.Phase.Variante is null && f.Phase.GewaehlteRolle!.Text == "wie Grundstellung", "wie Grundstellung entfernt die Variante");
        Pruefe.Gleich("geändert: Phase 2 Entern", string.Join(" | ", f.Aenderungen), "gezählt");
        await f.UebernehmenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Phase 2 Entern läuft gerade: Wen sie trifft, stellt der Server beim Übernehmen sofort um. Jetzt übernehmen?", f.Rueckfrage, "Rückfrage");
        f.NeinCommand.Execute(null);

        // Löschen: nicht die Grundstellung.
        f.GewaehltePhase = f.Phasen[0];
        Pruefe.Falsch(f.PhaseLoeschenCommand.CanExecute(null), "die Grundstellung bleibt");
        Pruefe.Wahr(f.Phase is { IstGrundstellung: true } && f.Phase.Rollen.Count == 0, "die Grundstellung hat keine Varianten");
        f.GewaehltePhase = f.Phasen[1];
        f.PhaseLoeschenCommand.Execute(null);
        Pruefe.Wahr(f.Phasen.Count == 1 && f.Phase is null && f.Aenderungen.Contains("gelöscht: Phase 2 Entern"), "gelöscht: " + string.Join(" | ", f.Aenderungen));
    }

    [Test]
    public static void Phasen_AnEinemServer040_KeinReiter()
    {
        using var ordner = new Testordner();
        var (d, vm, f) = Aufbauen(ordner, phasen: false);
        using var _ = vm;
        Pruefe.Falsch(f.PhasenSichtbar, "kein Reiter");
        f.IstPhasen = true;
        Pruefe.Gleich(FlotteReiter.Einheiten, f.Reiter, "der Reiter lässt sich nicht wählen");
        Pruefe.Falsch(f.PhaseNeuCommand.CanExecute(null), "keine neue Phase");

        // Wird der Server neuer (neue Verbindung), kommt der Reiter; wird er älter, geht er und der Reiter fällt zurück.
        d.PhasenBekannt = true;
        vm.Aktualisieren();
        Pruefe.Wahr(f.PhasenSichtbar && f.PhaseNeuCommand.CanExecute(null), "Server 0.5.0");
        f.IstPhasen = true;
        d.PhasenBekannt = false;
        vm.Aktualisieren();
        Pruefe.Wahr(!f.PhasenSichtbar && f.Reiter == FlotteReiter.Einheiten, "zurück auf Einheiten");
    }
}
