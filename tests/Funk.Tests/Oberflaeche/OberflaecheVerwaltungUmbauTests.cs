// JG TacRelay - Maintainer: Sarevoc
using Funk.Verwaltung.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Umbau der Verwaltung (Fassung 0.8.0, docs/entwurf/VERWALTUNG-UMBAU.md, Variante B mit dem Funkfeld-Editor aus Variante C), an den
/// Ansichtsmodellen über der Attrappe (Schiffsflotte, Trupp, Orga): Navigation in Gruppen, die Übersicht "Heute" mit Sprüngen, Listen
/// mit Suche und Herkunftsfilter, "Verwendet in", der Funkfeld-Editor der Rollen, die Werkzeuge mit Erklärung und Rückfrage und die
/// Leiste des Entwurfs. Die Bilder dazu: OberflaecheRaidBilderTests (Ansichten "heute", "werkzeuge", "flotte-*").
/// </summary>
internal static class OberflaecheVerwaltungUmbauTests
{
    private static (VerwaltungsdienstAttrappe D, VerwaltungViewModel Vm, FlotteViewModel F) Aufbauen(Testordner ordner)
    {
        var d = new VerwaltungsdienstAttrappe { Ablageordner = ordner.Pfad, Eingeteilte = 0 };
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        return (d, vm, vm.Flotte);
    }

    [Test]
    public static void Navigation_GruppenFolgenBereichUndReiter()
    {
        using var ordner = new Testordner();
        var (d, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        vm.IstKanaele = true;
        Pruefe.Wahr(vm.Bereich == Verwaltungsbereich.Flotte && f.Reiter == FlotteReiter.Netze && vm.IstKanaele && !vm.IstRollen && vm.IstFlotte, "Funk, Kanäle");
        vm.IstRollen = true;
        Pruefe.Wahr(f.Reiter == FlotteReiter.Rollen && vm.IstRollen && !vm.IstKanaele, "Funk, Rollen");
        var gemeldet = new List<string>();
        vm.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName!);
        f.Reiter = FlotteReiter.Einheiten;
        Pruefe.Wahr(vm.IstEinheiten && gemeldet.Contains(nameof(VerwaltungViewModel.IstEinheiten)) && gemeldet.Contains(nameof(VerwaltungViewModel.IstRollen)),
            "der Reiter der Flotte meldet die Einträge links: " + string.Join(", ", gemeldet));
        vm.IstWerkzeuge = true;
        Pruefe.Wahr(vm.Bereich == Verwaltungsbereich.Werkzeuge && !vm.IstEinheiten && !vm.IstFlotte, "Werkzeuge");
        vm.IstHeute = true;
        vm.IstKanaele = false;
        Pruefe.Wahr(vm.IstHeute && vm.Bereich == Verwaltungsbereich.Heute, "Heute; das Abwählen eines anderen Eintrags ändert nichts");

        // Phasen nur an einem Server, der sie kennt.
        d.PhasenBekannt = false;
        vm.Aktualisieren();
        vm.IstPhasen = true;
        Pruefe.Wahr(vm.IstHeute && !vm.IstPhasen, "ohne Phasen am Server bleibt Heute");
        d.PhasenBekannt = true;
        vm.Aktualisieren();
        vm.IstPhasen = true;
        Pruefe.Wahr(vm.IstPhasen && f.Reiter == FlotteReiter.Phasen, "mit Phasen: Funk, Phasen");
    }

    [Test]
    public static void Heute_AnstehtUndHinweise_SpringenDorthin()
    {
        using var ordner = new Testordner();
        var (_, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        vm.IstHeute = true;
        Pruefe.Gleich("2 Anträge warten auf Freigabe. | Kein offener Entwurf. | Es läuft eine Einsatzleitung: Falke.", string.Join(" | ", vm.Ansteht.Select(z => z.Text)),
            "was ansteht");
        Pruefe.Wahr(vm.KeineHinweise && vm.Hinweise.Count == 0, "die Flotte der Attrappe gibt keinen Hinweis: " + string.Join(" | ", vm.Hinweise.Select(z => z.Text)));

        // Eine neue Rolle ohne Felder und ohne Einheit, ein neuer Kanal in keiner Rolle: drei Hinweise, je mit Sprung.
        vm.IstRollen = true;
        f.RolleNeuCommand.Execute(null);
        f.NetzNeuCommand.Execute(null);
        vm.IstHeute = true;
        Pruefe.Gleich("Entwurf: 2 Änderungen nicht übernommen.", vm.Ansteht[1].Text, "der offene Entwurf");
        Pruefe.Gleich("Die Rolle Neue Rolle hat keine Felder: Wer sie wählt, hat nur Global. | Die Rolle Neue Rolle braucht eine Einheit, steht aber auf keiner: "
            + "Niemand kann sie wählen. | Der Kanal Neuer Kanal steht in keiner Rolle und ist nicht offen: Von selbst bekommt ihn niemand.",
            string.Join(" | ", vm.Hinweise.Select(z => z.Text)), "Hinweise");
        Pruefe.Wahr(!vm.KeineHinweise && vm.Hinweise.All(z => z is { HatSprung: true, Knopf: "Zeigen" }), "je mit Zeigen");

        vm.SpringenCommand.Execute(vm.Hinweise[2]);
        Pruefe.Wahr(vm.IstKanaele && f.GewaehltesNetz?.Name == "Neuer Kanal" && f.Netz is not null, "Sprung zum Kanal");
        vm.IstHeute = true;
        vm.SpringenCommand.Execute(vm.Hinweise[0]);
        Pruefe.Wahr(vm.IstRollen && f.GewaehlteRolle?.Name == "Neue Rolle" && f.Rolle is not null, "Sprung zur Rolle");
        vm.IstHeute = true;
        vm.SpringenCommand.Execute(vm.Ansteht[1]);
        Pruefe.Wahr(f.EntwurfOffen && vm.IstHeute, "Was ändert sich? klappt auf, die Übersicht bleibt");
        vm.SpringenCommand.Execute(vm.Ansteht[2]);
        Pruefe.Wahr(vm.IstEinsatzleitung, "zur Einsatzleitung");
        vm.IstHeute = true;
        vm.SpringenCommand.Execute(vm.Ansteht[0]);
        Pruefe.Wahr(vm.IstAntraege, "zu den Anträgen");
    }

    [Test]
    public static void Listen_SucheUndHerkunft_GewaehlteBleibtSichtbar()
    {
        using var ordner = new Testordner();
        var (_, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        vm.IstKanaele = true;
        Pruefe.Gleich("alle Herkünfte, Schiffsflotte, Baustein Trupp, Baustein Orga", string.Join(", ", f.KanalHerkuenfte), "Herkünfte der Kanäle");
        Pruefe.Gleich(f.Netze.Count, f.SichtbareKanaele.Count, "ohne Suche alle");
        Pruefe.Wahr(f.Netze.Single(n => n.Name == "Kommando").Herkunft == "Schiffsflotte" && f.Netze[0] is { Name: "Global", Herkunft: null, HatHerkunft: false },
            "Schild je Kanal, Global ohne");

        f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Kommando");
        f.KanalSuche = "crew";
        Pruefe.Wahr(f.SichtbareKanaele.Count == 16 && f.SichtbareKanaele[0].Name == "Kommando" && f.SichtbareKanaele.Skip(1).All(z => z.Name.StartsWith("Crew ", StringComparison.Ordinal)),
            "Suche ohne Rücksicht auf Groß und klein, der gewählte bleibt: " + string.Join(", ", f.SichtbareKanaele.Select(z => z.Name)));
        f.KanalSuche = "";
        f.KanalHerkunft = "Baustein Trupp";
        Pruefe.Gleich("Kommando, Trupp Alpha", string.Join(", ", f.SichtbareKanaele.Select(z => z.Name)), "Herkunft Baustein Trupp, dazu der gewählte");
        f.GewaehltesNetz = f.SichtbareKanaele.Single(z => z.Name == "Trupp Alpha");
        f.KanalSuche = "trupp";
        Pruefe.Gleich("Trupp Alpha", string.Join(", ", f.SichtbareKanaele.Select(z => z.Name)), "der vorher gewählte fällt beim nächsten Filtern weg");

        vm.IstEinheiten = true;
        f.EinheitenArt = FlotteViewModel.Artfilter.Single(a => a.Text == "Infanterie");
        Pruefe.Gleich("Trupp Alpha", string.Join(", ", f.SichtbareEinheiten.Select(z => z.Name)), "nur Infanterie");
        f.EinheitenArt = FlotteViewModel.Artfilter[0];
        f.EinheitenSuche = "Schiff 1";
        Pruefe.Gleich("Schiff 1, Schiff 10, Schiff 11, Schiff 12, Schiff 13, Schiff 14, Schiff 15", string.Join(", ", f.SichtbareEinheiten.Select(z => z.Name)), "Suche bei Einheiten");

        vm.IstRollen = true;
        Pruefe.Gleich("alle Herkünfte, Schiffsflotte, Baustein Trupp, Baustein Orga", string.Join(", ", f.RollenHerkuenfte), "Herkünfte der Rollen");
        f.RollenHerkunft = "Baustein Trupp";
        Pruefe.Gleich("Truppführer, Sanitäter, Schütze", string.Join(", ", f.SichtbareRollen.Select(z => z.Name)), "die Rollen des Bausteins");
        f.RollenHerkunft = FlotteViewModel.AlleHerkuenfte;
        Pruefe.Gleich(f.Rollen.Count, f.SichtbareRollen.Count, "wieder alle");
    }

    [Test]
    public static void Kanal_VerwendetIn_SprungZurRolleZumKanalZurEinheit()
    {
        using var ordner = new Testordner();
        var (_, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        vm.IstKanaele = true;
        f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Crew 1");
        var netz = f.Netz!;
        Pruefe.Gleich("Schiffsflotte", netz.Herkunft, "Herkunftsschild am Kanal");
        Pruefe.Gleich("Rolle Kapitän (Schiffsflotte): Feld 3 auf Schiff 1 | Einheit Schiff 1 als Crew der eigenen Einheit",
            string.Join(" | ", netz.VerwendetIn.Select(v => v.Text)), "Verwendet in");
        netz.VerwendungZeigenCommand.Execute(netz.VerwendetIn[0]);
        Pruefe.Wahr(vm.IstRollen && f.GewaehlteRolle?.Name == "Kapitän" && f.Rolle is not null, "Sprung zur Rolle, links Rollen gewählt");

        var rolle = f.Rolle!;
        rolle.KanalOeffnenCommand.Execute(rolle.Felder[0]);
        Pruefe.Wahr(vm.IstKanaele && f.GewaehltesNetz?.Name == "Kommando", "der Kanal einer Kachel führt zum Kanal");
        f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Crew 1");
        f.Netz!.VerwendungZeigenCommand.Execute(f.Netz.VerwendetIn[1]);
        Pruefe.Wahr(vm.IstEinheiten && f.GewaehlteEinheit?.Name == "Schiff 1" && f.Einheit is not null, "Sprung zur Einheit");
    }

    [Test]
    public static void Rollen_FunkfeldEditor_KachelnFeldwahlVerdoppeln()
    {
        using var ordner = new Testordner();
        var (_, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        vm.IstRollen = true;
        f.GewaehlteRolle = f.Rollen.Single(r => r.Name == "Kapitän");
        var rolle = f.Rolle!;
        Pruefe.Wahr(rolle.Kacheln.Count == 10 && rolle.Kacheln[9] is GlobalKachel { Name: "Feld 10: Global, fest" } && rolle.Herkunft == "Schiffsflotte",
            "neun Felder und Global fest rechts unten");
        var feld1 = rolle.Felder[0];
        Pruefe.Wahr(ReferenceEquals(rolle.GewaehltesFeld, feld1) && feld1.Gewaehlt && !rolle.Felder[1].Gewaehlt, "Feld 1 ist angeklickt");
        Pruefe.Wahr(feld1 is { KachelName: "Kommando", KachelZustand: "hört und sendet", KachelZusatz: "zuerst senden", HatKanal: true, KachelCh: "" }, "Kachel Kommando");
        Pruefe.Wahr(rolle.Felder[2] is { KachelName: "Crew der eigenen Einheit", HatKanal: false }, "eine Kennung der Einheit: kein Sprung");

        rolle.FeldWaehlenCommand.Execute(rolle.Felder[2]);
        Pruefe.Wahr(rolle.Felder[2].Gewaehlt && !feld1.Gewaehlt, "ein Klick wählt das Feld");
        rolle.Felder[2].Fest = true;
        rolle.Felder[2].Hoeren = false;
        Pruefe.Wahr(rolle.Felder[2] is { KachelZustand: "nur senden", KachelZusatz: "fest" }, "die Kachel folgt");
        Pruefe.Wahr(f.AenderungenKlartext.Contains("Rolle Kapitän (Schiffsflotte): Feld 3 hört nicht mehr, fest"), "Klartext: " + string.Join(" | ", f.AenderungenKlartext));
        Pruefe.Falsch(rolle.FeldDazuCommand.CanExecute(null), "alle neun Felder belegt");
        rolle.FeldLeerenCommand.Execute(null);
        Pruefe.Wahr(!rolle.Felder[2].Aktiv && rolle.Felder[2].KachelName == "frei" && rolle.FeldDazuCommand.CanExecute(null), "Feld 3 geleert");
        rolle.FeldWaehlenCommand.Execute(feld1);
        rolle.FeldDazuCommand.Execute(null);
        Pruefe.Wahr(ReferenceEquals(rolle.GewaehltesFeld, rolle.Felder[2]), "+ Feld wählt das erste freie");

        f.RolleVerdoppelnCommand.Execute(null);
        Pruefe.Wahr(f.GewaehlteRolle?.Name == "Kapitän 2" && f.Rolle!.Herkunft == "Schiffsflotte"
            && f.Hinweis!.StartsWith("Rolle Kapitän 2 angelegt, wie Kapitän.", StringComparison.Ordinal), "verdoppelt und gewählt: " + f.Hinweis);
        Pruefe.Wahr(f.Rollen.Single(r => r.Name == "Kapitän 2").Herkunft == "Schiffsflotte", "mit Schild in der Liste");

        // Mit der Vorlage Star Citizen daneben gibt es Kommando zweimal: Die Auswahl nennt dann die Herkunft, und die der Rolle steht zuerst.
        f.VerwerfenCommand.Execute(null);
        f.VorlageStarCitizenCommand.Execute(null);
        f.GewaehlteRolle = f.Rollen.Single(r => r.Name == "Flottenführung");
        var namen = f.Rolle!.Quellen.Select(q => q.Text).ToList();
        Pruefe.Wahr(namen.IndexOf("Kommando (Star Citizen)") is >= 0 and var neu && namen.IndexOf("Kommando (Schiffsflotte)") is var alt && alt > neu,
            "doppelte Namen mit Herkunft, die eigene zuerst: " + string.Join(", ", namen));
        Pruefe.Gleich("Kommando", f.Rolle.Felder[0].KachelName, "in der Kachel ohne Herkunft");
    }

    [Test]
    public static void Werkzeuge_ErklaerungUndRueckfrage_LeerRot()
    {
        using var ordner = new Testordner();
        var (d, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        vm.IstWerkzeuge = true;
        var alle = f.Vorlagen.Concat(f.Bausteine).Concat(f.Sicherung).Append(f.Leeren).ToList();
        Pruefe.Gleich("Vorlage Star Citizen, Baustein Trupp, Baustein Orga-Einheit, Als Datei speichern, Letzten Stand zurückholen, "
            + "Gewählten Stand laden, Leer (nur Global)", string.Join(", ", alle.Select(w => w.Titel)), "die Werkzeuge");
        Pruefe.Wahr(alle.All(w => w.Erklaerung.EndsWith('.') && w.Frage.Contains('?', StringComparison.Ordinal) && w.KnopfText == w.Titel + " ..."),
            "je ein Satz Erklärung und eine Rückfrage");
        Pruefe.Wahr(alle.Count(w => w.Gefaehrlich) == 1 && f.Leeren.Gefaehrlich, "nur Leer ist rot");

        var leer = f.Leeren;
        leer.FragenCommand.Execute(null);
        Pruefe.Wahr(leer.Fragt && f.Einheiten.Count == 17, "erst die Rückfrage, geleert ist nichts");
        f.Vorlagen[0].FragenCommand.Execute(null);
        Pruefe.Wahr(f.Vorlagen[0].Fragt && !leer.Fragt, "es fragt immer nur ein Werkzeug");
        f.Vorlagen[0].AbbrechenCommand.Execute(null);
        Pruefe.Wahr(!f.Vorlagen[0].Fragt && f.AenderungenText == "Keine Änderungen", "abgebrochen: nichts geändert");
        leer.FragenCommand.Execute(null);
        leer.JaCommand.Execute(null);
        Pruefe.Wahr(!leer.Fragt && f.Einheiten.Count == 0 && f.Netze.Select(n => n.Name).SequenceEqual(["Global"]) && f.LeisteSichtbar, "Ja: nur Global, die Leiste steht");
        Pruefe.Wahr(f.AenderungenKlartext.Contains("Einheit Schiff 1 fällt weg") && f.AenderungenKlartext.Contains("Kanal Kommando (Schiffsflotte) fällt weg"),
            "was sich ändert: " + string.Join(" | ", f.AenderungenKlartext.Take(5)));
        f.VerwerfenCommand.Execute(null);

        // Gespeicherten Stand laden geht erst mit einer Wahl; die Vorlage Star Citizen erst an einem Server ab 0.6.0, mit Grund.
        Pruefe.Falsch(f.Sicherung[2].FragenCommand.CanExecute(null), "ohne gewählten Stand gesperrt");
        f.Sicherung[0].FragenCommand.Execute(null);
        f.Sicherung[0].JaCommand.Execute(null);
        f.GewaehlterStand = f.Staende[0];
        Pruefe.Wahr(f.Sicherung[2].FragenCommand.CanExecute(null), "mit gewähltem Stand frei");
        d.KanaeleBekannt = false;
        f.Aktualisieren();
        Pruefe.Wahr(!f.Vorlagen[0].FragenCommand.CanExecute(null) && f.Vorlagen[0].Gesperrt!.Contains("0.6.0", StringComparison.Ordinal), "Star Citizen gesperrt, mit Grund");
    }

    [Test]
    public static void Entwurfsleiste_NurWennOffen_WasAendertSich_SperrtStehtImmerDa()
    {
        using var ordner = new Testordner();
        var (_, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        Pruefe.Falsch(f.LeisteSichtbar, "ohne Änderung keine Leiste");
        vm.IstKanaele = true;
        f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Reserve 6");
        f.Netz!.Name = "Reserve";
        Pruefe.Wahr(f.LeisteSichtbar && f.LeisteText == "1 Änderung nicht übernommen" && !f.EntwurfOffen, "die Leiste steht, zugeklappt");
        Pruefe.Gleich("Kanal Reserve (Schiffsflotte): umbenannt, vorher Reserve 6", string.Join(" | ", f.AenderungenKlartext), "Klartext mit Herkunft");
        f.WasAendertSichCommand.Execute(null);
        Pruefe.Wahr(f.EntwurfOffen, "aufgeklappt");
        f.WasAendertSichCommand.Execute(null);
        Pruefe.Falsch(f.EntwurfOffen, "wieder zu");

        // Ein Fehler, der das Übernehmen sperrt, steht auch zugeklappt in der Leiste.
        f.Netz.Name = "";
        Pruefe.Wahr(f.SperrBefunde.Count == 1 && f.SperrBefunde[0].Text.EndsWith("Name fehlt.", StringComparison.Ordinal)
            && f.SperrText == "1 Fehler: so nähme der Server den Entwurf nicht an", "sperrt: " + f.SperrText);
        f.Netz.Name = "Reserve";
        Pruefe.Wahr(f.SperrBefunde.Count == 0 && f.SperrText is null, "behoben");

        // Verwerfen: keine Änderung mehr, die Leiste geht, "Was ändert sich?" klappt zu.
        f.WasAendertSichCommand.Execute(null);
        f.VerwerfenCommand.Execute(null);
        Pruefe.Wahr(!f.LeisteSichtbar && !f.EntwurfOffen, "verworfen: keine Leiste");

        // Eine Meldung hält die Leiste, bis "Schließen".
        f.SpeichernCommand.Execute(null);
        Pruefe.Wahr(f.LeisteSichtbar && f.HatMeldung && !f.HatAenderungen, "Meldung in der Leiste");
        f.MeldungSchliessenCommand.Execute(null);
        Pruefe.Wahr(!f.LeisteSichtbar && f.Hinweis is null, "geschlossen");
    }
}
