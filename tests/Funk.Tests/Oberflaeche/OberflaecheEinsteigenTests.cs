// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Teilnehmersicht "Einsteigen" (Stufe 2, RAIDLEITUNG-ENTWURF "Vor dem Raid: jeder steigt selbst ein", M4): Reiter Schiffe,
/// Infanterie, Orga und ohne Einheit; je Einheit die Plätze mit Belegung, wie der Server zählt (K1: auch reservierte); was voll
/// ist, nennt den Grund; "wie letztes Mal" mit einem Klick, nur wenn Einheit und Rolle noch da sind; bei gesperrter Selbstwahl der
/// Grund mit Namen statt eines grauen Knopfs, Nachzügler ohne Platz dürfen trotzdem; wer eingeteilt hat, steht beim Platz.
/// Über dem Beispielabend aus Anhang A, gesehen von einem Funker ohne Auftrag.
/// </summary>
internal static class OberflaecheEinsteigenTests
{
    /// <summary>Der Beispielabend aus der Sicht eines einfachen Funkers (ohne Auftrag), auf seinem Platz aus der Praesenz.</summary>
    internal static FunkDienstAttrappe Als(uint konto)
    {
        var d = MitRaidabend();
        d.LageSetzen(l =>
        {
            var f = l.Funker.Single(x => x.KontoId == konto);
            return l with
            {
                Konto = new KontoInfo(konto, f.Name, f.Name.ToLowerInvariant(), Stufe.Funker, []) { ScNamen = f.ScName is null ? [] : [f.ScName] },
                SchiffId = f.SchiffId,
                RolleId = f.RolleId,
                Platz = f.Platz,
                Quelle = f.Details?.Quelle,
            };
        });
        return d;
    }

    [Test]
    public static async Task Einsteigen_ReiterNachArt_PlaetzeMitBelegungUndGrund()
    {
        using var a = new Aufbau(dienst: Als(Pollux));
        var vm = a.Netze();
        Pruefe.Wahr(vm.Reiter.Select(r => r.Titel).SequenceEqual(["Schiffe (3)", "Infanterie (1)", "Orga (1)", "Ohne Einheit"]), "Reiter: " + string.Join(" | ", vm.Reiter.Select(r => r.Titel)));
        Pruefe.Wahr(vm.GewaehlterReiter?.Art == Einheitsart.Schiff && vm.Schiffe.Count == 3, "ohne Platz: Schiffe zuerst");
        vm.GewaehlterReiter = vm.Reiter[1];
        Pruefe.Wahr(vm.Schiffe.Select(s => s.Name).SequenceEqual(["Trupp Alpha"]) && vm.Schiffe[0].Belegung == "1/8 belegt", "Infanterie: " + vm.Schiffe[0].Belegung);
        vm.GewaehltesSchiff = vm.Schiffe[0];
        Pruefe.Wahr(vm.Plaetze.Select(p => p.Text).SequenceEqual(["Truppführer 1/1", "Schütze 0/6", "Sanitäter 0/1"]), "Plätze mit Belegung: " + string.Join(" | ", vm.Plaetze.Select(p => p.Text)));
        var tf = vm.Plaetze[0];
        Pruefe.Wahr(tf is { Voll: true, Grund: "voll" }, "voll mit Grund");
        vm.GewaehlterPlatz = tf;
        Pruefe.Wahr(vm.GewaehlteRolle?.Name == "Truppführer" && !vm.EinsteigenCommand.CanExecute(null), "voller Platz: Einsteigen geht nicht");
        vm.GewaehlterPlatz = vm.Plaetze.Single(p => p.Text.StartsWith("Schütze", StringComparison.Ordinal));
        Pruefe.Wahr(vm.NetzVorschau == "Trupp Alpha" && vm.EinsteigenCommand.CanExecute(null), "Netze der Rolle auf dieser Einheit (A22): " + vm.NetzVorschau);
        await vm.EinsteigenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Einsteigen 6 10", a.Dienst.Aufrufe[^1], "Rolle und Einheit in einem Schritt");

        // K1: Eine Reservierung belegt den Platz wie beim Server.
        a.Dienst.LageSetzen(l => Belegungsstand(l with
        {
            Funker = [.. l.Funker, new FunkerInfo(30, "Rabe", false, Schiff2, Gunner) { ScName = "Rabe", Platz = PlatzStatus.Reserviert }],
        }));
        vm.GewaehlterReiter = vm.Reiter[0];
        vm.GewaehltesSchiff = vm.Schiffe.Single(s => s.Name == "Schiff 2");
        Pruefe.Gleich("Gunner 1/2", vm.Plaetze.Single(p => p.Text.StartsWith("Gunner", StringComparison.Ordinal)).Text, "reserviert zählt mit");

        // Ohne Einheit: Rollen ohne Einheit mit ihrer Kapazität.
        vm.GewaehlterReiter = vm.Reiter[3];
        Pruefe.Wahr(vm.Schiffe.Count == 0 && vm.Plaetze.Select(p => p.Text).SequenceEqual(["Flottenführung 0/2"]), "ohne Einheit");
        vm.GewaehlterPlatz = vm.Plaetze[0];
        await vm.EinsteigenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Einsteigen 9 -", a.Dienst.Aufrufe[^1], "ohne Einheit");
    }

    [Test]
    public static async Task Einsteigen_WieLetztesMal_NurWennEinheitUndRolleNochDaSind()
    {
        using var a = new Aufbau(dienst: Als(Pollux));
        var vm = a.Netze();
        // Der letzte Platz (Schiff 2 Pilot) ist gerade besetzt.
        Pruefe.Wahr(vm.WieLetztesMalText == "Wie letztes Mal: Schiff 2, Pilot (gerade besetzt)" && !vm.WieLetztesMalCommand.CanExecute(null), "besetzt: " + vm.WieLetztesMalText);
        a.Dienst.LageSetzen(l => l with { LetzterStand = l.LetzterStand! with { SchiffId = Schiff3, RolleId = Gunner } });
        Pruefe.Wahr(vm.WieLetztesMalText == "Wie letztes Mal: Schiff 3, Gunner" && vm.WieLetztesMalCommand.CanExecute(null), "frei: " + vm.WieLetztesMalText);
        await vm.WieLetztesMalCommand.ExecuteAsync(null);
        Pruefe.Gleich("WieLetztesMal", a.Dienst.Aufrufe[^1], "SatzAnwenden mit dem letzten Stand (A21)");
        Pruefe.Wahr(vm.WieLetztesMalText is null && vm.AktuellerPlatz.StartsWith("Gunner auf Schiff 3", StringComparison.Ordinal), "danach: " + vm.AktuellerPlatz);

        // Gibt es die Einheit oder die Rolle nicht mehr, kein Vorschlag.
        a.Dienst.LageSetzen(l => l with { SchiffId = null, RolleId = null, LetzterStand = l.LetzterStand! with { SchiffId = 99 } });
        Pruefe.Gleich(null, vm.WieLetztesMalText, "Einheit weg");
        a.Dienst.LageSetzen(l => l with { LetzterStand = l.LetzterStand! with { SchiffId = Schiff3, RolleId = 77 } });
        Pruefe.Gleich(null, vm.WieLetztesMalText, "Rolle weg");
    }

    [Test]
    public static void Einsteigen_Sperre_GrundStattGrauemKnopf_NachzueglerDuerfen()
    {
        // Pollux hat keinen Platz: "Wechsel sperren" lässt ihn als Nachzügler noch einsteigen.
        using var a = new Aufbau(dienst: Als(Pollux));
        a.Dienst.LageSetzen(l => l with { Selbstwahl = false, SelbstwahlDurch = Mustermann, SelbstwahlBis = Abend.AddHours(12), Nachzuegler = true });
        var vm = a.Netze();
        vm.GewaehltesSchiff = vm.Schiffe.Single(s => s.Name == "Schiff 3");
        vm.GewaehlterPlatz = vm.Plaetze.Single(p => p.Text.StartsWith("Gunner", StringComparison.Ordinal));
        Pruefe.Wahr(vm.EinsteigenCommand.CanExecute(null), "Nachzügler ohne Platz steigt trotz Wechselsperre ein");
        Pruefe.Gleich("Wechsel gesperrt durch die Einsatzleitung (Mustermann). Du hast noch keinen Platz und kannst einmal selbst einsteigen.", vm.SperrGrund, "Hinweis");

        // "Ganz sperren": auch ohne Platz nicht mehr.
        a.Dienst.LageSetzen(l => l with { Nachzuegler = false });
        Pruefe.Falsch(vm.EinsteigenCommand.CanExecute(null), "ganz gesperrt");
        Pruefe.Gleich("Einsteigen ganz gesperrt durch die Einsatzleitung (Mustermann) - melde dich bei ihr.", vm.SperrGrund, "Grund mit Namen");

        // Wer einen Platz hat, wechselt bei Wechselsperre nicht.
        using var b = new Aufbau(dienst: Als(Kobold));
        b.Dienst.LageSetzen(l => l with { Selbstwahl = false, SelbstwahlDurch = Mustermann, Nachzuegler = true });
        var k = b.Netze();
        k.GewaehltesSchiff = k.Schiffe.Single(s => s.Name == "Schiff 3");
        k.GewaehlterPlatz = k.Plaetze.Single(p => p.Text.StartsWith("Gunner", StringComparison.Ordinal));
        Pruefe.Falsch(k.EinsteigenCommand.CanExecute(null), "mit Platz: gesperrt");
        Pruefe.Gleich("Wechsel gesperrt durch die Einsatzleitung (Mustermann) - melde dich bei ihr.", k.SperrGrund, "Grund statt grauem Knopf");

        // Die Einsatzleitung selbst ist nie gesperrt.
        b.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Beauftragung = new BeauftragungInfo(Abend, Abend.AddHours(4), [Recht.Zuweisen], Nordwind, null, true) } });
        Pruefe.Wahr(k.EinsteigenCommand.CanExecute(null) && k.SperrGrund is null, "mit ZUWEISEN nie gesperrt");
    }

    [Test]
    public static void Einsteigen_ZeigtWerEingeteiltHat()
    {
        using var a = new Aufbau(dienst: Als(Eule));
        var vm = a.Netze();
        Pruefe.Gleich("Gunner auf Schiff 1 (selbst gewählt)", vm.AktuellerPlatz, "selbst");
        a.Dienst.LageSetzen(l => l with { SchiffId = Schiff3, RolleId = Gunner, Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = Mustermann, Platz = PlatzStatus.Belegt });
        Pruefe.Gleich("Gunner auf Schiff 3, eingeteilt von Mustermann", vm.AktuellerPlatz, "eingeteilt");
        Pruefe.Wahr(vm.GewaehlterReiter?.Art == Einheitsart.Schiff && vm.GewaehltesSchiff?.Name == "Schiff 3", "Auswahl folgt dem eigenen Platz");
    }
}
