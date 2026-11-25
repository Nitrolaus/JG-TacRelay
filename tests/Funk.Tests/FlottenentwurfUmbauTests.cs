// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;
using Funk.Server.Steuer;
using Funk.Verwaltung.Flotte;

namespace Funk.Tests;

/// <summary>
/// Der Entwurf für den Umbau der Verwaltung (Fassung 0.8.0, docs/entwurf/VERWALTUNG-UMBAU.md): Herkunftsschild je Rolle und Kanal (gespeichert
/// oder abgeleitet, nie eine Änderung), "Was ändert sich?" je Objekt in Klartext, "Verwendet in" eines Kanals, die Hinweise der Übersicht
/// "Heute" und "Rolle verdoppeln". Am echten Zustandsschreiber mit der Schiffsflotte.
/// </summary>
internal static class FlottenentwurfUmbauTests
{
    /// <summary>Der Stand eines Servers vor 0.8.0: dieselbe Flotte ohne gespeicherte Herkunft.</summary>
    private static NetzListe OhneHerkunft(NetzListe l) => l with
    {
        Netze = [.. l.Netze.Select(n => n with { Herkunft = null })],
        Rollen = [.. l.Rollen.Select(r => r with { Herkunft = null })],
    };

    [Test]
    public static void Herkunft_GespeichertOderAbgeleitet_NieEineAenderung()
    {
        var e = Flottenentwurf.Aus(OhneHerkunft(Abbildung.NetzListe(FlottenentwurfTests.SchiffsflottenServer().Aktuell)));
        e.KanaeleErlaubt = true;
        var command = e.Netze.Single(n => n.Name == "Kommando");
        var co = e.Rollen.Single();
        Pruefe.Wahr(e.HerkunftVon(command) == Flottenentwurf.AlteFlotte && e.HerkunftVon(co) == Flottenentwurf.AlteFlotte, "vor 0.8.0 angelegt: alte Flotte");
        Pruefe.Gleich(null, e.HerkunftVon(e.Netz(Grenzen.GlobalNetzId)!), "Global ohne Schild");

        e.VorlageAnwenden(Flottenvorlagen.StarCitizen(e.AlsNetzListe()));
        var kanal = e.Netze.First(n => n.Name == "Brücke");
        var rolle = e.Rollen.Single(r => r.Name == "Pilot");
        Pruefe.Wahr(e.HerkunftVon(kanal) == "Star Citizen" && e.HerkunftVon(rolle) == "Star Citizen", "aus der Vorlage geschrieben");
        e.NetzSetzen(kanal with { Herkunft = null });
        e.RolleSetzen(rolle with { Herkunft = null });
        Pruefe.Wahr(e.HerkunftVon(e.Netz(kanal.Id)!) == Flottenentwurf.AlteVorlage && e.HerkunftVon(e.Rolle(rolle.Id)!) == Flottenentwurf.AlteVorlage,
            "ohne gespeicherte Herkunft abgeleitet: Kanal mit Ch., Rolle nur auf Kanälen mit Ch. (eine Vorlage vor 0.8.0)");
        e.NetzSetzen(kanal);
        e.RolleSetzen(rolle);

        var trupp = e.BausteinTrupp();
        var orga = e.BausteinOrga();
        Pruefe.Wahr(e.Netz(trupp.Netze["TRUPP"])!.Herkunft == Flottenentwurf.BausteinTruppHerkunft
            && e.Rollen.Single(r => r.Name == "Schütze").Herkunft == Flottenentwurf.BausteinTruppHerkunft, "Baustein Trupp");
        Pruefe.Wahr(e.Netz(orga.Netze["ORGA"])!.Herkunft == Flottenentwurf.BausteinOrgaHerkunft
            && e.Rollen.Single(r => r.Name == "Orga").Herkunft == Flottenentwurf.BausteinOrgaHerkunft, "Baustein Orga");
        Pruefe.Wahr(e.NetzAnlegen("Reserve").Herkunft == Flottenentwurf.VonHand && e.RolleAnlegen("Lotse", brauchtEinheit: false).Herkunft == Flottenentwurf.VonHand,
            "von Hand angelegt");
        var schiff = e.EinheitAnlegen(Einheitsart.Schiff);
        Pruefe.Wahr(e.NetzeFuerEinheitAnlegen(schiff.Id).All(n => n.Herkunft == Flottenentwurf.VonHand), "Kanäle für eine Einheit von Hand");
        // In der Folge der Liste: "Standard (Gast)" (Star Citizen) steht vor Kapitän der alten Flotte (Reihenfolge 12), die Bausteine dahinter.
        Pruefe.Gleich("Star Citizen, alte Flotte, Baustein Trupp, Baustein Orga, von Hand", string.Join(", ", e.Herkuenfte(rollen: true)), "Herkünfte der Rollen");

        // Nur die Herkunft ändern ist keine Änderung: Ein Server 0.7.x speichert sie nicht, der Entwurf bliebe sonst offen.
        var frisch = Flottenentwurf.Aus(OhneHerkunft(Abbildung.NetzListe(FlottenentwurfTests.SchiffsflottenServer().Aktuell)));
        frisch.NetzSetzen(frisch.Netze.Single(n => n.Name == "Kommando") with { Herkunft = Flottenentwurf.VonHand });
        frisch.RolleSetzen(frisch.Rollen.Single() with { Herkunft = "Star Citizen" });
        Pruefe.Wahr(frisch.Aenderungen().Count == 0 && frisch.Klartext().Count == 0 && frisch.Paket().Paket is null, "Herkunft allein zählt nicht");
        Pruefe.Gleich(Flottenentwurf.VonHand, frisch.HerkunftVon(frisch.Netze.Single(n => n.Name == "Kommando")), "angezeigt wird sie trotzdem");
    }

    [Test]
    public static void Klartext_JeObjektEineZeile_MitHerkunft()
    {
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var e = Flottenentwurf.Aus(Abbildung.NetzListe(s.Aktuell));
        e.PhasenErlaubt = true;
        var co = e.Rollen.Single();
        e.RolleSetzen(co with { SelbstWaehlbar = false, Felder = [.. co.Felder.Select(f => f.Feld == 8 ? f with { Fest = true, Hoeren = false } : f)] });
        var reserve6 = e.Netze.Single(n => n.Name == "Reserve 6");
        e.NetzSetzen(reserve6 with { Name = "Reserve", NurTaste = true });
        e.NetzLoeschen(e.Netze.Single(n => n.Name == "Crew 15").Id);
        var neu = e.EinheitAnlegen(Einheitsart.Schiff);
        var schiff1 = e.Schiffe.Single(x => x.Name == "Schiff 1");
        e.SchiffSetzen(schiff1 with { Typ = "Hammerhead", Plaetze = [new SchiffsPlatz { RolleId = co.Id, Kapazitaet = 2, Mindest = 1 }] });
        e.PhaseAnlegen("Entern", "ENTERN");
        e.DurchsageNurTaste = !e.Basis.Einstellungen.DurchsageNurTaste;

        var klartext = e.Klartext();
        foreach (var zeile in new[]
                 {
                     "Einheit Schiff 1: Typ jetzt Hammerhead, Kapitän: 2 Plätze, mindestens 1 statt 1 Platz",
                     "Einheit Schiff 15: Crew der eigenen Einheit fällt weg",
                     "Einheit " + neu.Name + " kommt dazu",
                     "Rolle Kapitän (Schiffsflotte): Selbst wählbar wird aus, Feld 9 hört nicht mehr, fest",
                     "Kanal Reserve (Schiffsflotte): umbenannt, vorher Reserve 6, nur mit Sprechtaste wird an",
                     "Kanal Crew 15 (Schiffsflotte) fällt weg",
                     "Phase 2 Entern kommt dazu",
                     "Durchsagen nur mit Sprechtaste wird " + (e.DurchsageNurTaste ? "an" : "aus"),
                 })
            Pruefe.Wahr(klartext.Contains(zeile), "Zeile \"" + zeile + "\" in: " + string.Join(" | ", klartext));
        Pruefe.Gleich(e.Aenderungen().Count, klartext.Count, "je Änderung eine Zeile");

        var feld = co.Felder.Single(f => f.Feld == 0);
        var command = e.Netz(feld.NetzId!.Value)!;
        e.RolleSetzen(e.Rolle(co.Id)! with { Felder = [.. e.Rolle(co.Id)!.Felder.Where(f => f.Feld != 0)] });
        Pruefe.Wahr(e.Klartext().Any(z => z.Contains("Feld 1 leer", StringComparison.Ordinal)), "ein Feld fällt weg");
        e.RolleSetzen(e.Rolle(co.Id)! with { Felder = [.. e.Rolle(co.Id)!.Felder, feld with { SendenStandard = false }] });
        Pruefe.Wahr(e.Klartext().Any(z => z.Contains("Feld 1 nicht mehr zuerst senden", StringComparison.Ordinal)), "zuerst senden fällt weg: " + string.Join(" | ", e.Klartext()));
        Pruefe.Gleich("Kommando", command.Name, "Feld 1 ist Kommando");
    }

    [Test]
    public static void VerwendetIn_RolleDirektUndUeberEinheit_PhaseUndEinheit()
    {
        var e = Flottenentwurf.Aus(Abbildung.NetzListe(FlottenentwurfTests.SchiffsflottenServer().Aktuell));
        e.PhasenErlaubt = true;
        var co = e.Rollen.Single();
        var command = e.Netze.Single(n => n.Name == "Kommando");
        var crew1 = e.Netze.Single(n => n.Name == "Crew 1");
        Pruefe.Gleich("Rolle Kapitän (Schiffsflotte): Feld 1", string.Join(" | ", e.VerwendetIn(command.Id).Select(v => v.Text)), "Kommando direkt");
        Pruefe.Gleich("Rolle Kapitän (Schiffsflotte): Feld 3 auf Schiff 1 | Einheit Schiff 1 als Crew der eigenen Einheit",
            string.Join(" | ", e.VerwendetIn(crew1.Id).Select(v => v.Text)), "Crew 1 über die Kennung der Einheit");
        Pruefe.Wahr(e.VerwendetIn(crew1.Id) is [{ Art: VerwendungsArt.Rolle, RolleId: { } r }, { Art: VerwendungsArt.Einheit, RolleId: null }] && r == co.Id, "Sprungziele");

        var phase = e.PhaseAnlegen("Entern", "ENTERN")!;
        e.VarianteAnlegen(phase.Nr, co.Id);
        Pruefe.Wahr(e.VerwendetIn(command.Id).Any(v => v is { Art: VerwendungsArt.Phase, Text: "Phase 2 Entern, Rolle Kapitän: Feld 1" } && v.Id == phase.Nr),
            "die Variante in Phase 2: " + string.Join(" | ", e.VerwendetIn(command.Id).Select(v => v.Text)));
        Pruefe.Gleich(0, e.VerwendetIn(e.NetzAnlegen("Reserve").Id).Count, "ein neuer Kanal steht nirgends");
    }

    [Test]
    public static void Hinweise_UebersichtHeute()
    {
        var e = Flottenentwurf.Aus(OhneHerkunft(Abbildung.NetzListe(FlottenentwurfTests.SchiffsflottenServer().Aktuell)));
        e.KanaeleErlaubt = true;
        Pruefe.Gleich("", string.Join(" | ", e.Hinweise().Select(h => h.Text)), "die Schiffsflotte allein ist stimmig");

        e.VorlageAnwenden(Flottenvorlagen.StarCitizen(e.AlsNetzListe()));
        var co = e.Rollen.Single(r => r.Name == "Kapitän" && r.Herkunft is null);
        var command = e.Netze.First(n => n.Name == "Kommando" && n.Kanaele is null);
        string Texte() => string.Join(" | ", e.Hinweise().Select(h => h.Text));
        var doppelt = e.Hinweise().Single(h => h.Text.StartsWith("Die Rolle Kapitän gibt es", StringComparison.Ordinal));
        Pruefe.Gleich("Die Rolle Kapitän gibt es zweimal, beide selbst wählbar (Star Citizen, alte Flotte): In der Rollenwahl stehen sie nebeneinander.",
            doppelt.Text, "doppelter Name");
        Pruefe.Wahr(doppelt is { Ziel: HinweisZiel.Rolle, Suche: "Kapitän" } && doppelt.Id == e.Rollen.First(r => r.Name == "Kapitän" && r.SelbstWaehlbar).Id,
            "Sprung in die Rollen mit Suche, zur ersten der beiden");
        var alt = e.Hinweise().Single(h => h.Ziel == HinweisZiel.KanaeleAlteFlotte);
        Pruefe.Gleich("Nur noch Rollen der alten Flotte benutzen 51 Kanäle: Kommando, Reserve 2, Reserve 3 und 48 weitere.", alt.Text, "alte Kanäle");
        Pruefe.Gleich(command.Id, alt.Id, "Sprung zum ersten");

        e.RolleSetzen(co with { SelbstWaehlbar = false });
        Pruefe.Falsch(Texte().Contains("gibt es zweimal", StringComparison.Ordinal), "nur eine selbst wählbar: kein Hinweis");
        var leer = e.RolleAnlegen("Lotse", brauchtEinheit: false);
        var ohneEinheit = e.RolleAnlegen("Bordarzt", brauchtEinheit: true);
        // Ein eigener Name: "Reserve" steht schon in den Ch. der Vorlage (Allgemein: Reserve 1 bis 3) und in der Schiffsflotte.
        var reserve = e.NetzAnlegen("Ersatzkanal");
        var schiff = e.EinheitAnlegen(Einheitsart.Schiff);
        foreach (var (text, ziel, id) in new[]
                 {
                     ("Die Rolle Lotse hat keine Felder: Wer sie wählt, hat nur Global.", HinweisZiel.Rolle, leer.Id),
                     ("Die Rolle Bordarzt braucht eine Einheit, steht aber auf keiner: Niemand kann sie wählen.", HinweisZiel.Rolle, ohneEinheit.Id),
                     ("Die Einheit Schiff 16 hat keine Plätze: Dort kann niemand einsteigen.", HinweisZiel.Einheit, schiff.Id),
                     ("Der Kanal Ersatzkanal steht in keiner Rolle und ist nicht offen: Von selbst bekommt ihn niemand.", HinweisZiel.Kanal, reserve.Id),
                 })
            Pruefe.Wahr(e.Hinweise().Any(h => h.Text == text && h.Ziel == ziel && h.Id == id), "Hinweis \"" + text + "\" in: " + Texte());
        e.NetzSetzen(reserve with { Offen = NetzOffen.Hoeren });
        Pruefe.Falsch(Texte().Contains("Ersatzkanal", StringComparison.Ordinal), "ein offener Kanal braucht keine Rolle");

        for (var i = 0; i < 3; i++) e.EinheitAnlegen(Einheitsart.Schiff);
        Pruefe.Wahr(e.Hinweise().Any(h => h.Text == "4 Einheiten haben keine Plätze: Schiff 16, Schiff 17, Schiff 18 und 1 weitere. Dort kann niemand einsteigen." && h.Id == schiff.Id),
            "mehr als drei als ein Hinweis: " + Texte());
    }

    [Test]
    public static void RolleVerdoppeln_KopieMitFeldernUndVarianten_OhnePlaetze()
    {
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var e = Flottenentwurf.Aus(Abbildung.NetzListe(s.Aktuell));
        e.PhasenErlaubt = true;
        var co = e.Rollen.Single();
        var phase = e.PhaseAnlegen("Entern", "ENTERN")!;
        e.VarianteAnlegen(phase.Nr, co.Id);
        var kopie = e.RolleVerdoppeln(co.Id);
        Pruefe.Wahr(kopie is { Name: "Kapitän 2", SelbstWaehlbar: true, BrauchtSchiff: true, Herkunft: Testflotte.Name } && kopie.Id != co.Id
            && kopie.Felder.SequenceEqual(co.Felder) && kopie.Reihenfolge > co.Reihenfolge, "Kopie mit Feldern und Herkunft");
        Pruefe.Wahr(e.Variante(phase.Nr, kopie.Id).SequenceEqual(e.Variante(phase.Nr, co.Id)), "die Variante in Phase 2 kommt mit");
        Pruefe.Wahr(e.Schiffe.All(x => x.Plaetze.All(p => p.RolleId != kopie.Id)), "keine Plätze auf Einheiten");
        Pruefe.Gleich("Kapitän 3", e.RolleVerdoppeln(kopie.Id).Name, "die nächste Nummer");
        Pruefe.Wahr(e.Klartext().Contains("Rolle Kapitän 2 (Schiffsflotte) kommt dazu"), "im Klartext: " + string.Join(" | ", e.Klartext()));
        Pruefe.Falsch(e.Pruefen().Any(b => b.Sperrt), "nichts sperrt: " + string.Join(" | ", e.Pruefen().Select(b => b.Text)));

        FlottenentwurfTests.Anwenden(s, e.Paket().Paket!);
        var nachher = Abbildung.NetzListe(s.Aktuell);
        Pruefe.Wahr(nachher.Rollen.Single(r => r.Name == "Kapitän 2").Herkunft == Testflotte.Name, "der Server hat die Kopie mit Herkunft");
        Pruefe.Gleich(0, Flottenentwurf.Aus(nachher).Aenderungen().Count, "danach keine Änderung");
    }
}
