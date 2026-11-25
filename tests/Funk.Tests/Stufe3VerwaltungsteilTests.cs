// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;
using Funk.Tests.Oberflaeche;
using Funk.Verwaltung.Flotte;
using Funk.Verwaltung.Modelle;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Verwaltungsteil mit Kanälen (Fassung 0.6.0, Schritt 3.3, STUFE3-ENTWURF 4.4): der Entwurf kennt Kanäle mit Ch. (Prüfung wie der
/// Server, Vergleich nach Inhalt, Sicherung mit Ch. und Phasen), die Vorlage Star Citizen läuft durch Entwurf und Server, ein Server vor 0.6.0
/// bekommt keine Ch.; im Bereich Flotte der Reiter Kanäle mit Gruppe und Ch.-Liste, der feste Ch. eines Rollenfelds, die Vorlage mit ihren
/// gelben Annahmen und die Rückfrage, die das Versionstor ankündigt.
/// </summary>
internal static class Stufe3VerwaltungsteilTests
{
    private static Flottenentwurf MitStarCitizen(L.Zustandsschreiber s)
    {
        var e = Flottenentwurf.Aus(Funk.Server.Steuer.Abbildung.NetzListe(s.Aktuell));
        e.KanaeleErlaubt = true;
        e.PhasenErlaubt = true;
        e.VorlageAnwenden(Flottenvorlagen.StarCitizen(e.AlsNetzListe()));
        return e;
    }

    [Test]
    public static void Entwurf_StarCitizen_PaketServerUndZurueckOhneAenderung()
    {
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var e = MitStarCitizen(s);
        Pruefe.Gleich("", string.Join(" | ", e.Pruefen().Where(b => b.Sperrt).Select(b => b.Text)), "nichts sperrt");
        var paket = e.Paket().Paket!;
        Pruefe.Gleich(24, paket.Ops.Count, "10 Kanäle, 14 Rollen");
        FlottenentwurfTests.Anwenden(s, paket);
        Pruefe.Gleich(10, L.Kanaele.Anzahl(s.Aktuell.Netze), "10 Kanäle mit Ch. am Server");
        var neu = Flottenentwurf.Aus(Funk.Server.Steuer.Abbildung.NetzListe(s.Aktuell));
        neu.KanaeleErlaubt = true;
        Pruefe.Gleich("", string.Join(" | ", neu.Aenderungen()), "der Server steht wie der Entwurf; Ch.-Listen nach Inhalt verglichen");
        var bridge = neu.Netze.Single(n => n.Name == "Brücke");
        Pruefe.Gleich("Brücke Ch. 3 (Schiff 3)", neu.NetzName(Kanalrechnung.Id(bridge.Id, 3)), "Kanal mit Ch. im Klartext");
        var pilot = neu.Rollen.Single(r => r.Name == "Pilot");
        Pruefe.Gleich("Brücke (Ch. wie zuletzt, ab Werk Ruhe)", neu.Quelle(pilot.Felder[0]), "Rollenfeld ohne festen Ch. (Schiff n)");
    }

    [Test]
    public static void Entwurf_KanalRegeln_WieDerServer_UndServer050()
    {
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var e = MitStarCitizen(s);
        e.KanaeleErlaubt = false;
        Pruefe.Wahr(e.Pruefen().Any(b => b.Sperrt && b.Text.StartsWith("Kanäle mit Ch. kennt der Server erst ab Fassung 0.6.0", StringComparison.Ordinal)),
            "ein Server 0.5.0 bekommt keine Ch.");
        e.KanaeleErlaubt = true;
        var bridge = e.Netze.Single(n => n.Name == "Brücke");
        string Sperrt() => string.Join(" | ", e.Pruefen().Where(b => b.Sperrt).Select(b => b.Text));

        e.NetzSetzen(bridge with { Kanaele = ["Ruhe"] });
        Pruefe.Wahr(Sperrt().Contains("Kanal Brücke: 0 Ch.", StringComparison.Ordinal), "nur Ruhe: " + Sperrt());
        e.NetzSetzen(bridge with { Kanaele = ["Ruhe", .. Enumerable.Range(1, 31).Select(i => "Schiff " + i)] });
        Pruefe.Wahr(Sperrt().Contains("31 Ch.", StringComparison.Ordinal), "31 Ch.: " + Sperrt());
        e.NetzSetzen(bridge with { Kanaele = ["Ruhe", new string('x', 21)] });
        Pruefe.Wahr(Sperrt().Contains("länger als 20 Zeichen", StringComparison.Ordinal), "zu lange Bezeichnung: " + Sperrt());
        e.NetzSetzen(bridge);
        Pruefe.Gleich("", Sperrt(), "wieder gut");

        var co = e.Rollen.Single(r => r.Name == "Pilot");
        var felder = co.Felder.ToList();
        felder[0] = felder[0] with { Ch = 21 };
        e.RolleSetzen(co with { Felder = felder });
        Pruefe.Wahr(Sperrt().Contains("Ch. 21 gibt es nicht", StringComparison.Ordinal), "Ch. außerhalb der Liste: " + Sperrt());
        felder[0] = felder[0] with { Ch = 3 };
        // Die Vorlage lässt Global ohne Ch.; mit Ch. (wie es die Konsole setzen kann) ist Ch. 1 Global selbst und kein Ch. für ein Feld.
        e.NetzSetzen(e.Netz(Grenzen.GlobalNetzId)! with { Kanaele = ["Ruhe", "Gesamt", "Global 1", "Global 2"] });
        e.RolleSetzen(co with { Felder = [.. felder, new RollenFeld { Feld = 9, NetzId = Grenzen.GlobalNetzId, Ch = 1, Hoeren = true, Senden = true, SendenStandard = false, Fest = false }] });
        Pruefe.Wahr(Sperrt().Contains("Ch. 1 von Global ist das feste Feld", StringComparison.Ordinal), "Ch. 1 von Global als Ch.: " + Sperrt());
        e.RolleSetzen(co with { Felder = [.. felder, new RollenFeld { Feld = 9, NetzId = Grenzen.GlobalNetzId, Ch = 2, Hoeren = true, Senden = true, SendenStandard = false, Fest = false }] });
        Pruefe.Gleich("", Sperrt(), "Global 1 in einem Rollenfeld geht, wenn Global Ch. hat");

        var schiff = e.Schiffe[0];
        e.KennungSetzen(schiff.Id, "BRIDGE", bridge.Id);
        Pruefe.Wahr(Sperrt().Contains("auf keinen Ch.", StringComparison.Ordinal), "Einheit auf einen Kanal ohne Ch.: " + Sperrt());
        e.KennungSetzen(schiff.Id, "BRIDGE", Kanalrechnung.Id(bridge.Id, 3));
        Pruefe.Gleich("", Sperrt(), "Einheit gibt Ch. 3 vor");
        FlottenentwurfTests.Anwenden(s, e.Paket().Paket!);
        e.NetzLoeschen(bridge.Id);
        Pruefe.Falsch(e.Schiff(schiff.Id)!.Netze.ContainsKey("BRIDGE"), "Löschen des Kanals nimmt die Kennung auf seinen Ch. mit (wie der Server)");
    }

    [Test]
    public static void Entwurf_Sicherung_MitKanaelenUndPhasen_HinUndZurueck()
    {
        var s = FlottenentwurfTests.SchiffsflottenServer();
        var e = MitStarCitizen(s);
        var co = e.Rollen.Single(r => r.Name == "Pilot");
        e.NetzSetzen(e.Netze.Single(n => n.Name == "Jäger") with { Gruppe = "Jäger" });
        var phase = e.PhaseAnlegen("Entern", "ENT")!;
        e.VarianteSetzen(phase.Nr, co.Id, [co.Felder[0] with { Feld = 0, Ch = 4, SendenStandard = true }]);
        var datei = e.AlsJson();
        var gelesen = Flottenentwurf.JsonLesen(datei)!;
        var wieder = Flottenentwurf.Aus(e.Basis, gelesen);
        wieder.KanaeleErlaubt = true;
        wieder.PhasenErlaubt = true;
        Pruefe.Gleich(string.Join(" | ", e.Aenderungen()), string.Join(" | ", wieder.Aenderungen()), "dieselben Änderungen aus der Datei");
        Pruefe.Wahr(gelesen.Netze.Single(n => n.Name == "Jäger") is { Gruppe: "Jäger", Kanaele.Count: 6 }, "Ch.-Liste und Gruppe in der Datei");
        Pruefe.Wahr(wieder.Variante(phase.Nr, co.Id) is [{ Ch: 4 }], "der feste Ch. einer Variante in der Datei");
    }

    [Test]
    public static async Task Flotte_KanaeleReiter_ChListeGruppe_RollenfeldMitCh()
    {
        using var ordner = new Testordner();
        var d = new VerwaltungsdienstAttrappe { Ablageordner = ordner.Pfad, Eingeteilte = 0 };
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        using var _ = vm;
        vm.Bereich = Verwaltungsbereich.Flotte;
        var f = vm.Flotte;
        d.Auftraege.Clear();
        d.Senden(new Meldung { Art = MeldungsArten.Beauftragung });

        f.NetzNeuCommand.Execute(null);
        var kanal = f.Netz!;
        Pruefe.Wahr(kanal.ChAenderbar, "der Server kennt Ch.");
        kanal.Name = "Brücke";
        kanal.Gruppe = "Schiffe";
        kanal.ChListe = "Schiff 1" + Environment.NewLine;
        Pruefe.Gleich("Schiff 1" + Environment.NewLine, kanal.ChListe, "die neue Zeile beim Tippen bleibt stehen");
        kanal.ChListe = "Schiff 1" + Environment.NewLine + "Schiff 2" + Environment.NewLine + "  " + Environment.NewLine + "Schiff 3";
        var netz = f.Entwurf!.Netz(kanal.Id)!;
        Pruefe.Wahr(netz is { Gruppe: "Schiffe", Kanaele: ["Ruhe", "Schiff 1", "Schiff 2", "Schiff 3"] }, "Ch. 0 Ruhe, dann die Zeilen ohne leere");
        Pruefe.Wahr(f.Netze.Single(z => z.Id == kanal.Id).Text.Contains("3 Ch., Schiffe", StringComparison.Ordinal), "Unterzeile in der Liste: " + f.Netze.Single(z => z.Id == kanal.Id).Text);

        f.Reiter = FlotteReiter.Rollen;
        f.GewaehlteRolle = f.Rollen.Single(r => r.Name == "Kapitän");
        var feld = f.Rolle!.Felder[8];
        var vorher = feld.Quelle;
        feld.Quelle = f.Rolle.Quellen.Single(q => q.Text == "Brücke");
        Pruefe.Wahr(feld.HatCh && feld.ChWahl.Select(a => a.Text).SequenceEqual(["Ch. wie zuletzt (ab Werk Ruhe)", "0 Ruhe", "1 Schiff 1", "2 Schiff 2", "3 Schiff 3"]),
            "Ch.-Wahl: " + string.Join(", ", feld.ChWahl.Select(a => a.Text)));
        feld.Ch = feld.ChWahl.Single(a => a.Wert == 2);
        Pruefe.Wahr(f.Entwurf!.Rolle(f.Rolle.Id)!.Felder.Single(x => x.Feld == 8) is { Ch: 2 }, "fester Ch. 2 im Rollenfeld");
        feld.Ch = feld.ChWahl[0];
        Pruefe.Wahr(f.Entwurf!.Rolle(f.Rolle.Id)!.Felder.Single(x => x.Feld == 8) is { Ch: null }, "wie zuletzt: ohne festen Ch.");
        feld.Quelle = vorher;
        Pruefe.Falsch(feld.HatCh, vorher.Text + " der alten Flotte hat keine Ch.");

        d.BrauchtCode = true;
        await f.UebernehmenCommand.ExecuteAsync(null);
        Pruefe.Wahr(f.Rueckfrage is { } r && r.Contains("Danach kommen nur noch Clients ab 0.6.0 hinein", StringComparison.Ordinal), "die Rückfrage kündigt das Versionstor an: " + f.Rueckfrage);
        var ja = f.JaUebernehmenCommand.ExecuteAsync(null);
        if (vm.Abfrage.Offen)
        {
            vm.Abfrage.Eingabe = VerwaltungsdienstAttrappe.RichtigerCode;
            vm.Abfrage.OkCommand.Execute(null);
        }
        await ja;
        Pruefe.Wahr(d.Flotte.NetzListe!.Netze.Any(n => n.Name == "Brücke" && n.Kanaele is { Count: 4 }), "am Server: " + f.Fehler);
    }

    [Test]
    public static void Flotte_VorlageStarCitizen_AnnahmenGelb_NurAbServer060()
    {
        using var ordner = new Testordner();
        var d = new VerwaltungsdienstAttrappe { Ablageordner = ordner.Pfad, Eingeteilte = 0, KanaeleBekannt = false };
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        using var _ = vm;
        vm.Bereich = Verwaltungsbereich.Flotte;
        var f = vm.Flotte;
        Pruefe.Falsch(f.VorlageStarCitizenCommand.CanExecute(null), "an einem Server 0.5.0 gesperrt");
        f.GewaehltesNetz = f.Netze.First(n => n.Name == "Kommando");
        Pruefe.Wahr(f.Netz is { ChAenderbar: false } n && n.ChHinweis.Contains("0.6.0", StringComparison.Ordinal), "Ch.-Liste gesperrt mit Grund");

        d.KanaeleBekannt = true;
        f.Aktualisieren();
        Pruefe.Wahr(f.VorlageStarCitizenCommand.CanExecute(null), "ab 0.6.0 frei");
        f.VorlageStarCitizenCommand.Execute(null);
        Pruefe.Wahr(f.Annahmen.Count == Flottenvorlagen.StarCitizen().Annahmen.Count && f.Annahmen.Any(a => a.Contains("Ch. Alle am Ende", StringComparison.Ordinal)),
            "Annahmen gelb: " + string.Join(" | ", f.Annahmen));
        Pruefe.Wahr(f.Rollen.Any(r => r.Name == "Funker (Leitstand)") && f.Rollen.Count(r => r.Name == "Kapitän") == 2, "neben der alten Flotte");
        Pruefe.Falsch(f.Netze.Single(z => z.Id == Grenzen.GlobalNetzId).Text.Contains("Global-Kanäle", StringComparison.Ordinal), "Global bleibt ohne Ch.: " + f.Netze[0].Text);
        f.VorlageStarCitizenCommand.Execute(null);
        Pruefe.Wahr(f.Fehler is { } fehler && fehler.StartsWith("Es gibt schon Kanäle mit Ch.", StringComparison.Ordinal), "kein zweites Mal: " + f.Fehler);
        Pruefe.Gleich("", string.Join(" | ", f.Befunde.Where(b => b.Sperrt).Select(b => b.Text)), "nichts sperrt");
    }
}
