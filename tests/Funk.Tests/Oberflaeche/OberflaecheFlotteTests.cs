// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung.Flotte;
using Funk.Verwaltung.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Bereich "Flotte" der Verwaltung (A40, RAIDLEITUNG-ENTWURF "Flotten-Einstellungsseite"): Einheiten, Rollen und Netze im
/// Entwurf, Klartext statt Drahtnamen, "7 Änderungen" oben, Prüfung wie am Server, Übernehmen als ein Paket mit frischem Code,
/// Rückfrage im Raid (Auftrag läuft, mehr als 5 eingeteilt, Netz mit Mitgliedern gelöscht), vorher Sicherung (die letzten 10),
/// "Letzten Stand zurückholen", Datei, Vorlagen und Bausteine. Über der Attrappe, deren Flotte ein echter Zustandsschreiber hält
/// (Schiffsflotte, ein Trupp, eine Orga-Einheit).
/// </summary>
internal static class OberflaecheFlotteTests
{
    private static (VerwaltungsdienstAttrappe D, VerwaltungViewModel Vm, FlotteViewModel F) Aufbauen(Testordner ordner, int eingeteilte = 12)
    {
        var d = new VerwaltungsdienstAttrappe { Ablageordner = ordner.Pfad, Eingeteilte = eingeteilte };
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        vm.Bereich = Verwaltungsbereich.Flotte;
        return (d, vm, vm.Flotte);
    }

    private static async Task MitCode(VerwaltungViewModel vm, Task laeuft)
    {
        if (vm.Abfrage.Offen)
        {
            vm.Abfrage.Eingabe = VerwaltungsdienstAttrappe.RichtigerCode;
            vm.Abfrage.OkCommand.Execute(null);
        }
        await laeuft;
    }

    [Test]
    public static async Task Flotte_AendernRueckfrageSicherungUndZurueckholen()
    {
        using var ordner = new Testordner();
        var (d, vm, f) = Aufbauen(ordner);
        using var _ = vm;
        Pruefe.Wahr(vm.IstFlotte && f.HatFlotte, "Flotte geladen");
        Pruefe.Gleich("Keine Änderungen", f.AenderungenText, "frisch");
        Pruefe.Falsch(f.UebernehmenCommand.CanExecute(null), "nichts zu übernehmen");
        Pruefe.Wahr(f.Einheiten.Count == 17 && f.Einheiten[0].Name == "Schiff 1" && f.Einheiten[^2].Name == "Trupp Alpha" && f.Einheiten[^1].Name == "Orga 1",
            "Schiffe, Infanterie, Orga: " + string.Join(", ", f.Einheiten.Select(e => e.Name)));
        Pruefe.Gleich("Schiff, 1 Platz, 3 Einheitskanäle", f.Einheiten[0].Text, "Unterzeile");

        f.Reiter = FlotteReiter.Netze;
        f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Reserve 6");
        var bearbeitung = f.Netz!;
        bearbeitung.Name = "Reserve";
        Pruefe.Gleich("1 Änderung", f.AenderungenText, "gezählt");
        Pruefe.Wahr(f.Aenderungen.Contains("geändert: Kanal Reserve (vorher Reserve 6)"), "im Klartext: " + string.Join(" | ", f.Aenderungen));
        Pruefe.Wahr(f.Netze.Any(n => n.Name == "Reserve") && ReferenceEquals(bearbeitung, f.Netz), "Liste zieht nach, das Formular bleibt (kein Neuaufbau beim Tippen)");

        // Rückfrage: ein Auftrag läuft (Falke), 12 sind eingeteilt.
        await f.UebernehmenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Betrifft 12 eingeteilte Personen, deren Felder neu gebildet werden. Es läuft eine Einsatzleitung (Falke). Jetzt übernehmen?", f.Rueckfrage, "Rückfrage");
        Pruefe.Falsch(d.Aufrufe.Any(a => a.StartsWith("Flotte", StringComparison.Ordinal)), "noch nichts gesendet");
        var version = d.Flotte.NetzListe!.Version;
        d.BrauchtCode = true;
        await MitCode(vm, f.JaUebernehmenCommand.ExecuteAsync(null));
        Pruefe.Gleich("Flotte 1 v" + version, d.Aufrufe.Last(a => a.StartsWith("Flotte", StringComparison.Ordinal)), "ein Paket mit einem Schritt, gegen die Version");
        Pruefe.Wahr(d.Aufrufe.Contains("Code " + VerwaltungsdienstAttrappe.RichtigerCode), "mit frischem Code");

        var sicherung = Path.Combine(ordner.Pfad, "sicherung-20260930-201200.json");
        Pruefe.Wahr(File.Exists(sicherung), "vorher gesichert");
        Pruefe.Wahr(Flottenentwurf.JsonLesen(File.ReadAllBytes(sicherung))!.Netze.Any(n => n.Name == "Reserve 6"), "die Sicherung ist der alte Stand");
        Pruefe.Gleich("Keine Änderungen", f.AenderungenText, "nach dem Übernehmen ist der Server der Entwurf");
        Pruefe.Gleich("Übernommen: 1 Änderung. Vorher gesichert als sicherung-20260930-201200.json.", f.Hinweis, "Hinweis");
        Pruefe.Wahr(f.Staende.Any(s => s.Text == "Sicherung 30.09. 20:12"), "in der Liste der Stände: " + string.Join(" | ", f.Staende.Select(s => s.Text)));

        f.LetztenStandCommand.Execute(null);
        Pruefe.Wahr(f.Aenderungen.Contains("geändert: Kanal Reserve 6 (vorher Reserve)"), "letzter Stand zurück im Entwurf: " + string.Join(" | ", f.Aenderungen));
        f.VerwerfenCommand.Execute(null);
        Pruefe.Gleich("Keine Änderungen", f.AenderungenText, "verworfen");
    }

    [Test]
    public static async Task Flotte_PruefungSperrt_ZehnSicherungen_Datei_Veraltet_FremdeAenderung()
    {
        using var ordner = new Testordner();
        var (d, vm, f) = Aufbauen(ordner, eingeteilte: 2);
        using var _ = vm;
        d.Auftraege.Clear();
        d.Senden(new Meldung { Art = MeldungsArten.Beauftragung });

        f.Reiter = FlotteReiter.Rollen;
        f.GewaehlteRolle = f.Rollen.Single(r => r.Name == "Kapitän");
        var feld6 = f.Rolle!.Felder[5];
        var ausweich3 = feld6.Quelle;
        feld6.Quelle = f.Rolle.Quellen.Single(q => q.Text == "Kommando");
        Pruefe.Wahr(f.HatSperre && f.Befunde.Any(b => b.Sperrt && b.Text == "Rolle Kapitän: Feld 1 und Feld 6 nutzen beide Kommando."), "Prüfung");
        await f.UebernehmenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Erst beheben: Rolle Kapitän: Feld 1 und Feld 6 nutzen beide Kommando.", f.Fehler, "sperrt");
        Pruefe.Falsch(d.Aufrufe.Any(a => a.StartsWith("Flotte", StringComparison.Ordinal)), "nichts gesendet");
        feld6.Quelle = ausweich3;
        Pruefe.Falsch(f.HatSperre, "behoben");
        Pruefe.Gleich("Keine Änderungen", f.AenderungenText, "wieder wie der Server");

        // Elf alte Sicherungen; nach dem Übernehmen bleiben die zehn neuesten.
        var inhalt = Flottenentwurf.Aus(d.Flotte.NetzListe!).AlsJson();
        for (var i = 1; i <= 11; i++) File.WriteAllBytes(Path.Combine(ordner.Pfad, $"sicherung-202609{i:00}-200000.json"), inhalt);
        f.Reiter = FlotteReiter.Netze;
        f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Reserve 5");
        f.Netz!.NurTaste = true;
        await f.UebernehmenCommand.ExecuteAsync(null);
        Pruefe.Gleich(null, f.Rueckfrage, "zwei eingeteilt, kein Auftrag: keine Rückfrage");
        var sicherungen = Directory.GetFiles(ordner.Pfad, "sicherung-*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Pruefe.Wahr(sicherungen.Count == 10 && sicherungen[^1] == "sicherung-20260930-201200.json" && !sicherungen.Contains("sicherung-20260901-200000.json")
            && !sicherungen.Contains("sicherung-20260902-200000.json"), "die zehn neuesten: " + string.Join(", ", sicherungen));

        // Als Datei speichern.
        f.Netz!.Name = "Reserve 5 neu";
        f.SpeichernCommand.Execute(null);
        var datei = Path.Combine(ordner.Pfad, "entwurf-20260930-201200.json");
        Pruefe.Wahr(File.Exists(datei) && f.Hinweis == "Entwurf gespeichert: " + datei, "Datei: " + f.Hinweis);
        Pruefe.Wahr(f.Staende.Any(s => s.Text == "Entwurf 30.09. 20:12"), "in der Liste");

        // Inzwischen ändert ein anderer Admin die Flotte: der Entwurf behält die eigene Änderung und übernimmt die fremde.
        d.FlotteVonAussen(e => e.NetzSetzen(e.Netze.Single(n => n.Name == "Kommando") with { Name = "Flottenkommando" }));
        Pruefe.Gleich("1 Änderung", f.AenderungenText, "nur die eigene Änderung bleibt");
        Pruefe.Wahr(f.Netze.Any(n => n.Name == "Flottenkommando") && f.Netze.Any(n => n.Name == "Reserve 5 neu"), "beides im Entwurf");
        Pruefe.Gleich("Die Flotte wurde inzwischen geändert. Dein Entwurf zählt jetzt gegen den neuen Stand.", f.Hinweis, "Hinweis");

        // VERALTET vom Server: Fehler, der Entwurf bleibt.
        d.NaechsteFlotteVeraltet = true;
        await f.UebernehmenCommand.ExecuteAsync(null);
        Pruefe.Wahr(f.Fehler!.Contains("inzwischen geändert", StringComparison.Ordinal) && f.AenderungenText == "1 Änderung", "veraltet: " + f.Fehler);
    }

    [Test]
    public static async Task Flotte_VorlagenBausteineUndEditoren()
    {
        using var ordner = new Testordner();
        var (d, vm, f) = Aufbauen(ordner, eingeteilte: 0);
        using var _ = vm;
        d.Auftraege.Clear();
        d.Senden(new Meldung { Art = MeldungsArten.Beauftragung });
        var command = d.Flotte.NetzListe!.Netze.Single(n => n.Name == "Kommando");
        d.NetzMitglieder[command.Id] = 4;

        f.VorlageLeerCommand.Execute(null);
        Pruefe.Wahr(f.Einheiten.Count == 0 && f.Rollen.Count == 0 && f.Netze.Select(n => n.Name).SequenceEqual(["Global"]), "Leer: nur Global");
        await f.UebernehmenCommand.ExecuteAsync(null);
        Pruefe.Wahr(f.Rueckfrage is { } r && r.Contains("Kanal Kommando löschen trifft 4 Mitglieder.", StringComparison.Ordinal), "Löschen nennt die Mitglieder: " + f.Rueckfrage);
        f.NeinCommand.Execute(null);
        Pruefe.Wahr(f.Rueckfrage is null && !d.Aufrufe.Any(a => a.StartsWith("Flotte", StringComparison.Ordinal)), "abgebrochen");

        f.VorlageStarCitizenCommand.Execute(null);
        Pruefe.Wahr(f.Einheiten.Count == 0 && f.Netze.Count == 11 && f.Annahmen.Count == 6 && f.Annahmen[0].StartsWith("Schiff n, Staffel n und Trupp n", StringComparison.Ordinal),
            "Vorlage mit Annahmen (gelb): " + string.Join(" | ", f.Annahmen));
        f.VerwerfenCommand.Execute(null);
        Pruefe.Wahr(f.Einheiten.Count == 17 && f.Annahmen.Count == 0 && f.AenderungenText == "Keine Änderungen", "verworfen");

        // Einheiten: neu, Netze anlegen, Platz dazu, verdoppeln, löschen.
        f.EinheitNeuCommand.Execute("Schiff");
        Pruefe.Wahr(f.GewaehlteEinheit?.Name == "Schiff 16" && f.Einheit?.Name == "Schiff 16", "neue Einheit gewählt");
        f.NetzeAnlegenCommand.Execute(null);
        Pruefe.Gleich("Brücke der eigenen Einheit: Brücke 16 | Crew der eigenen Einheit: Crew 16 | Durchsagen der eigenen Einheit: Durchsagen 16",
            string.Join(" | ", f.Einheit!.Kennungen.Select(k => k.Text + ": " + k.Netz?.Text)), "drei Einheitsnetze in einem Klick");
        f.Einheit.PlatzDazuCommand.Execute(null);
        f.Einheit.Plaetze[0].Rolle = f.Einheit.Rollenwahl.Single(x => x.Text == "Kapitän");
        f.Einheit.Plaetze[0].Mindest = 1;
        f.Einheit.Typ = "Hammerhead";
        var neu = f.Einheiten.Single(e => e.Name == "Schiff 16");
        Pruefe.Gleich("Schiff, Hammerhead, 1 Platz, 3 Einheitskanäle", neu.Text, "Unterzeile folgt");
        f.EinheitVerdoppelnCommand.Execute(null);
        Pruefe.Wahr(f.GewaehlteEinheit?.Name == "Schiff 17" && f.Einheit!.Kennungen.Count == 3 && f.Einheit.Plaetze.Single().Mindest == 1, "verdoppelt");
        f.GewaehlteEinheit = f.Einheiten.Single(e => e.Name == "Trupp Alpha");
        f.EinheitLoeschenCommand.Execute(null);
        Pruefe.Wahr(f.Aenderungen.Contains("gelöscht: Einheit Trupp Alpha") && f.Aenderungen.Contains("neu: Einheit Schiff 17"), "Änderungen: " + string.Join(" | ", f.Aenderungen));

        // Rollen: der Felder-Editor wie das Funkfeld, Vorschau mit echten Netznamen.
        f.Reiter = FlotteReiter.Rollen;
        f.GewaehlteRolle = f.Rollen.Single(r => r.Name == "Kapitän");
        var rolle = f.Rolle!;
        Pruefe.Wahr(rolle.Felder.Count == 9 && rolle.Felder[0] is { Quelle.Text: "Kommando", Vorschlag: true, Hoeren: true, Senden: true }
            && rolle.Felder[2].Quelle.Text == "Crew der eigenen Einheit", "Felder im Klartext");
        rolle.VorschauEinheit = rolle.VorschauEinheiten.Single(x => x.Text == "Schiff 2");
        Pruefe.Gleich("So sieht Kapitän auf Schiff 2 aus: 1 Kommando (Sendevorschlag), 2 Durchsagen 2, 3 Crew 2, 4 Brücke 2, 5 Reserve 2, 6 Reserve 3, 7 Reserve 4, 8 Reserve 5, 9 Reserve 6, dazu Global.",
            rolle.VorschauText, "Vorschau");
        rolle.Felder[8].Quelle = rolle.Quellen[0];
        Pruefe.Wahr(!rolle.Felder[8].Aktiv && f.Aenderungen.Contains("geändert: Rolle Kapitän"), "Feld 9 geleert");
        Pruefe.Wahr(rolle.VorschauText.Contains("8 Reserve 5, dazu Global.", StringComparison.Ordinal), "Vorschau folgt: " + rolle.VorschauText);

        // Netze: Klartext statt Drahtnamen.
        f.Reiter = FlotteReiter.Netze;
        f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Kommando");
        Pruefe.Wahr(f.Netz is { Taste: false, NurTaste: false, Standard: false, Offen.Text: "nein" }, "Kommando: Sprachaktivierung (seit 0.9.1, Q53), nicht offen");
        Pruefe.Gleich("Für die ganze Flotte, keine Einheit.", f.Netz!.Zugehoerigkeit, "Zugehörigkeit");
        Pruefe.Gleich("4 Mitglieder", f.Netz.MitgliederText, "Mitglieder");
        f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Crew 1");
        Pruefe.Gleich("Gehört zu Schiff 1 als Crew der eigenen Einheit.", f.Netz!.Zugehoerigkeit, "Einheitsnetz");
    }
}
