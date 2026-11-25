// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using Funk.Protokoll.Steuerung;
using Funk.Server.Kern;
using Funk.Server.Steuer;
using Funk.Verwaltung.Flotte;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Der Entwurf der Flotten-Einstellungsseite (A40, RAIDLEITUNG-ENTWURF "Flotten-Einstellungsseite"): Änderungen bleiben lokal,
/// werden im Klartext gezählt und geprüft wie am Server (6.9) und gehen als ein Verwaltungspaket (N5) hinaus, in einer Folge,
/// die der Server annimmt: erst Löschen, dann neue Einheiten als Gerüst, Netze, Rollen, Einheiten vollständig, zuletzt die
/// Einstellung. Geprüft wird am echten Zustandsschreiber: Nach dem Paket ist die NetzListe des Servers genau der Entwurf.
/// </summary>
internal static class FlottenentwurfTests
{
    private const uint Admin = 1;

    /// <summary>Server mit der Schiffsflotte (15 Schiffe, 51 Netze, Kapitän).</summary>
    internal static L.Zustandsschreiber SchiffsflottenServer()
    {
        var s = new L.Zustandsschreiber(new L.SchnappschussBauer().MitKonto(new L.Konto { Id = Admin, Name = "Test", Stufe = L.Stufe.Admin }).Bauen());
        Anwenden(s, Testflotte.Schiffsflotte().Paket);
        return s;
    }

    internal static void Anwenden(L.Zustandsschreiber s, VerwaltungsPaket paket)
    {
        var (schritte, fehler) = Zentrale.PaketLesen(paket);
        Pruefe.Wahr(schritte is not null, "Paket lesbar: " + fehler);
        var r = s.Ausfuehren(L.Auftrag.Paket(Admin, schritte!, s.Aktuell.NetzListeVersion), 1);
        Pruefe.Wahr(r.Erfolg, "der Server nimmt das Paket an: " + r);
    }

    /// <summary>Gleiche Flotte ohne Version und ohne Sperre der Selbstwahl, als vergleichbarer Text.</summary>
    private static string Inhalt(NetzListe l) => JsonSerializer.Serialize(new
    {
        l.Einstellungen.DurchsageNurTaste,
        Netze = l.Netze.OrderBy(n => n.Id).Select(n => JsonSerializer.Serialize(n, SteuerJson.Info<Netz>())),
        Schiffe = l.Schiffe.OrderBy(x => x.Id).Select(x => JsonSerializer.Serialize(x with { Netze = new SortedDictionary<string, ushort>(x.Netze.ToDictionary(), StringComparer.Ordinal) },
            SteuerJson.Info<Schiff>())),
        Rollen = l.Rollen.OrderBy(r => r.Id).Select(r => JsonSerializer.Serialize(r with { Felder = [.. r.Felder.OrderBy(f => f.Feld)] }, SteuerJson.Info<Rolle>())),
    });

    [Test]
    public static void Entwurf_OhneAenderung_NichtsZuUebernehmen()
    {
        var basis = Abbildung.NetzListe(SchiffsflottenServer().Aktuell);
        var e = Flottenentwurf.Aus(basis);
        Pruefe.Gleich(0, e.Aenderungen().Count, "frisch geladen: keine Änderung");
        Pruefe.Wahr(e.Schiffe.Count == 15 && e.Netze.Count == 52 && e.Rollen.Single().Name == "Kapitän", "alles da");
        var p = e.Paket();
        Pruefe.Wahr(p.Paket is null && p.Fehler == "Nichts zu übernehmen.", "kein leeres Paket: " + p.Fehler);
        Pruefe.Falsch(e.Pruefen().Any(b => b.Sperrt), "die Vorlage ist in Ordnung: " + string.Join(" | ", e.Pruefen().Select(b => b.Text)));
    }

    [Test]
    public static void Entwurf_Aenderungen_PaketInDerFolgeDesServers_ServerStehtDanachWieDerEntwurf()
    {
        var s = SchiffsflottenServer();
        var basis = Abbildung.NetzListe(s.Aktuell);
        var e = Flottenentwurf.Aus(basis);

        // Ein Netz umbenennen, Schiff 15 samt Netzen löschen, Schiff 1 verdoppeln, ein Trupp als Baustein, ein Feld fest.
        var ausweich6 = e.Netze.Single(n => n.Name == "Reserve 6");
        e.NetzSetzen(ausweich6 with { Name = "Reserve" });
        var schiff1 = e.Schiffe.Single(x => x.Name == "Schiff 1");
        var kopie = e.EinheitVerdoppeln(schiff1.Id);
        Pruefe.Gleich("Schiff 16", kopie.Name, "die Nummer nach der höchsten");
        var schiff15 = e.Schiffe.Single(x => x.Name == "Schiff 15");
        var netze15 = schiff15.Netze.Values.ToList();
        e.SchiffLoeschen(schiff15.Id);
        foreach (var n in netze15) e.NetzLoeschen(n);
        Pruefe.Gleich("Brücke 16,Crew 16,Durchsagen 16", string.Join(',', kopie.Netze.Values.Select(id => e.Netz(id)!.Name).Order(StringComparer.Ordinal)), "eigene Netze für die Kopie");
        Pruefe.Wahr(kopie.Plaetze.Single() is { Kapazitaet: 1 } && kopie.Netze.Values.All(id => e.Netz(id)!.SchiffId == kopie.Id), "Plätze und Zugehörigkeit wie beim Vorbild");
        var trupp = e.BausteinTrupp();
        Pruefe.Wahr(trupp is { Name: "Trupp Alpha", Art: Einheitsart.Trupp } && trupp.Netze.Keys.Single() == "TRUPP", "Trupp mit Truppnetz");
        Pruefe.Gleich("Truppführer 1 (mind. 1), Sanitäter 1, Schütze 6",
            string.Join(", ", trupp.Plaetze.Select(p => e.Rolle(p.RolleId)!.Name + " " + p.Kapazitaet + (p.Mindest is { } m ? " (mind. " + m + ")" : ""))), "Plätze des Bausteins");
        var co = e.Rollen.Single(r => r.Name == "Kapitän");
        e.RolleSetzen(co with { Felder = [.. co.Felder.Select(f => f.Feld == 8 ? f with { Fest = true } : f)] });
        e.DurchsageNurTaste = !basis.Einstellungen.DurchsageNurTaste;

        var aenderungen = e.Aenderungen();
        foreach (var text in new[]
                 {
                     "gelöscht: Einheit Schiff 15", "neu: Einheit Schiff 16", "neu: Einheit Trupp Alpha", "neu: Rolle Truppführer", "geändert: Rolle Kapitän",
                     "geändert: Kanal Reserve (vorher Reserve 6)", "gelöscht: Kanal Crew 15", "neu: Kanal Trupp Alpha", "geändert: Durchsagen nur mit Sprechtaste",
                 })
            Pruefe.Wahr(aenderungen.Contains(text), "Änderung \"" + text + "\" in: " + string.Join(" | ", aenderungen));
        Pruefe.Falsch(e.Pruefen().Any(b => b.Sperrt), "nichts, was den Server stört: " + string.Join(" | ", e.Pruefen().Select(b => b.Text)));

        var p = e.Paket();
        Pruefe.Wahr(p.Paket is not null && p.Fehler is null, "Paket gebaut: " + p.Fehler);
        var ops = p.Paket!.Ops.Select(o => o.Op).ToList();
        int Erstes(VerwaltungsOperation op) => ops.IndexOf(op);
        int Letztes(VerwaltungsOperation op) => ops.LastIndexOf(op);
        Pruefe.Wahr(Letztes(VerwaltungsOperation.SchiffLoeschen) < Erstes(VerwaltungsOperation.SchiffSetzen)
            && Letztes(VerwaltungsOperation.NetzLoeschen) < Erstes(VerwaltungsOperation.SchiffSetzen), "erst löschen: " + string.Join(",", ops));
        Pruefe.Wahr(Erstes(VerwaltungsOperation.SchiffSetzen) < Erstes(VerwaltungsOperation.NetzSetzen)
            && Letztes(VerwaltungsOperation.NetzSetzen) < Erstes(VerwaltungsOperation.RolleSetzen)
            && Letztes(VerwaltungsOperation.RolleSetzen) < Letztes(VerwaltungsOperation.SchiffSetzen)
            && ops[^1] == VerwaltungsOperation.Einstellung, "Gerüst, Netze, Rollen, Einheiten, Einstellung: " + string.Join(",", ops));
        Pruefe.Gleich(2 + 2, ops.Count(o => o == VerwaltungsOperation.SchiffSetzen), "zwei neue Einheiten: Gerüst und vollständig");

        Anwenden(s, p.Paket!);
        var nachher = Abbildung.NetzListe(s.Aktuell);
        Pruefe.Gleich(Inhalt(e.AlsNetzListe()), Inhalt(nachher), "der Server steht wie der Entwurf");
        Pruefe.Gleich(0, Flottenentwurf.Aus(nachher).Aenderungen().Count, "neu geladen: keine Änderung");
    }

    [Test]
    public static void Entwurf_Pruefen_KlartextWieDerServer()
    {
        var e = Flottenentwurf.Aus(Abbildung.NetzListe(SchiffsflottenServer().Aktuell));
        var co = e.Rollen.Single();
        var command = e.Netze.Single(n => n.Name == "Kommando");
        var crew1 = e.Netze.Single(n => n.Name == "Crew 1");
        // Feld 5 (Reserve 3) auf Kommando: zwei Felder mit demselben Netz.
        e.RolleSetzen(co with { Felder = [.. co.Felder.Select(f => f.Feld == 5 ? f with { NetzId = command.Id } : f)] });
        string Texte() => string.Join(" | ", e.Pruefen().Select(b => b.Text));
        Pruefe.Wahr(e.Pruefen().Any(b => b.Sperrt && b.Text == "Rolle Kapitän: Feld 1 und Feld 6 nutzen beide Kommando."), "doppeltes Netz: " + Texte());

        // Feld 5 auf Crew 1: kollidiert nur auf Schiff 1 mit der Kennung CREW.
        e.RolleSetzen(co with { Felder = [.. co.Felder.Select(f => f.Feld == 5 ? f with { NetzId = crew1.Id } : f)] });
        Pruefe.Wahr(e.Pruefen().Any(b => b.Sperrt && b.Text == "Rolle Kapitän auf Schiff 1: Feld 3 und Feld 6 nutzen beide Crew 1."), "Kollision auf einer Einheit: " + Texte());
        e.RolleSetzen(co);

        var schiff1 = e.Schiffe.Single(x => x.Name == "Schiff 1");
        e.SchiffSetzen(schiff1 with { Plaetze = [new SchiffsPlatz { RolleId = co.Id, Kapazitaet = 1, Mindest = 2 }] });
        Pruefe.Wahr(e.Pruefen().Any(b => b.Sperrt && b.Text == "Schiff 1, Kapitän: Mindestbesetzung 2 über den 1 Plätzen."), "Mindest über Kapazität: " + Texte());
        e.SchiffSetzen(schiff1 with { Netze = new Dictionary<string, ushort>(schiff1.Netze) { ["crew"] = crew1.Id } });
        Pruefe.Wahr(e.Pruefen().Any(b => b.Sperrt && b.Text == "Schiff 1: Die Kennung crew geht nicht (nur A-Z, 0-9 und _, höchstens 16 Zeichen)."), "Kennung: " + Texte());
        e.SchiffSetzen(schiff1 with { Name = " " });
        Pruefe.Wahr(e.Pruefen().Any(b => b.Sperrt && b.Text.StartsWith("Einheit Nummer " + schiff1.Id, StringComparison.Ordinal) && b.Text.EndsWith("Name fehlt.", StringComparison.Ordinal)), "Name: " + Texte());
        e.SchiffSetzen(schiff1);

        // Hinweise, die nicht sperren: eine Kennung, die eine Einheit nicht hat; eine Rolle ohne Einheit mit Einheitsnetz.
        var trupp = e.BausteinTrupp();
        e.SchiffSetzen(trupp with { Plaetze = [.. trupp.Plaetze, new SchiffsPlatz { RolleId = co.Id, Kapazitaet = 1 }] });
        Pruefe.Wahr(e.Pruefen().Any(b => !b.Sperrt && b.Text == "Kapitän auf Trupp Alpha: Feld 2 (Durchsagen der eigenen Einheit) gibt es dort nicht und bleibt leer."),
            "Hinweis Kennung: " + Texte());
        Pruefe.Falsch(e.Pruefen().Any(b => b.Sperrt), "Hinweise sperren nicht: " + Texte());
    }

    [Test]
    public static void Entwurf_GrenzeVonZweihundertSchritten()
    {
        var e = Flottenentwurf.Aus(Abbildung.NetzListe(SchiffsflottenServer().Aktuell));
        e.Leeren();
        Pruefe.Wahr(e.Netze.Count == 1 && e.Netze[0].Id == Grenzen.GlobalNetzId && e.Schiffe.Count == 0 && e.Rollen.Count == 0, "Leer: nur Global");
        var leer = e.Paket();
        Pruefe.Wahr(leer.Paket is { Ops.Count: 67 }, "51 Netze, 15 Schiffe und eine Rolle löschen: " + leer.Paket?.Ops.Count);
        // Die erste Vorlage nimmt dieselben IDs wie der Bestand und stellt ihn wieder her (kein Schritt); jede weitere bringt
        // 15 Gerüste, 51 Netze, eine Rolle und 15 Einheiten, also 82 Schritte. Drei davon sind 246.
        e.VorlageAnwenden(Testflotte.Schiffsflotte(e.AlsNetzListe()));
        Pruefe.Gleich(0, e.Aenderungen().Count, "die Vorlage auf leerer Flotte ist wieder der Bestand");
        for (var i = 0; i < 3; i++) e.VorlageAnwenden(Testflotte.Schiffsflotte(e.AlsNetzListe()));
        var zuViel = e.Paket();
        Pruefe.Wahr(zuViel.Paket is null && zuViel.Fehler!.StartsWith("Zu viele Änderungen für einen Schritt:", StringComparison.Ordinal), "Grenze: " + zuViel.Fehler);
    }

    [Test]
    public static void Entwurf_AlsDatei_HinUndZurueck()
    {
        var e = Flottenentwurf.Aus(Abbildung.NetzListe(SchiffsflottenServer().Aktuell));
        e.BausteinOrga();
        var daten = e.AlsJson();
        var gelesen = Flottenentwurf.JsonLesen(daten);
        Pruefe.Wahr(gelesen is not null, "lesbar");
        Pruefe.Gleich(Inhalt(e.AlsNetzListe()), Inhalt(gelesen!), "gleicher Inhalt");
        Pruefe.Gleich(null, Flottenentwurf.JsonLesen("{\"kaputt\":1}"u8.ToArray()), "Fremdes wird nicht gelesen");
        var zurueck = Flottenentwurf.Aus(e.Basis, gelesen);
        Pruefe.Wahr(zurueck.Aenderungen().Contains("neu: Einheit Orga 1"), "aus der Datei: dieselben Änderungen gegen den Server");
    }
}
