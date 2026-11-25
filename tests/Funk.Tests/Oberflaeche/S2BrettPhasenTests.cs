// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Ausbaustufe S2, Teil C2 am Brett (RAID-S2-ENTWURF Abschnitt 3): die Phasenleiste im Kopf ("1 Anflug | 2 Entern | 3 Rückzug", die
/// laufende hervorgehoben), Klick, Rückfrage mit der Zahl, Ja schaltet; der Verlauf nennt eigene und fremde Wechsel; "Abend beenden"
/// schaltet zuerst auf Phase 1; ein Zug, der nach einem Phasenwechsel VERALTET bekommt, sagt das statt eines nackten Fehlers; ohne
/// Phasen oder an einem Server 0.4.0 keine Leiste.
/// </summary>
internal static class S2BrettPhasenTests
{
    [Test]
    public static async Task Phasenleiste_KlickRueckfrageJa_SchaltetUndVerlauf()
    {
        using var a = new Aufbau(dienst: MitRaidabend()) { Jetzt = Abend };
        var brett = a.Brett();
        Pruefe.Wahr(brett.PhasenSichtbar, "Leiste da");
        Pruefe.Gleich("1 Anflug*|2 Entern|3 Rückzug", string.Join("|", brett.Phasen.Select(p => p.Text + (p.Laufend ? "*" : ""))), "die laufende hervorgehoben");
        brett.PhaseWaehlenCommand.Execute(brett.Phasen[1]);
        Pruefe.Gleich("Auf Phase 2 Entern schalten? 3 Funker, ausgelassen: Nordwind (Admin).", brett.PhasenRueckfrage, "Rückfrage mit der Zahl");
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("Phase", StringComparison.Ordinal)), "noch nichts geschickt");
        await brett.PhaseJaCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Phase 2 erwartet 1"), "Ja schaltet");
        Pruefe.Gleich(null, brett.PhasenRueckfrage, "Rückfrage zu");
        Pruefe.Gleich("Phase 2 Entern läuft - 3 umgestellt, ausgelassen: Nordwind (Admin)", brett.Meldung, "Ergebnis unten");
        Pruefe.Gleich("1 Anflug|2 Entern*|3 Rückzug", string.Join("|", brett.Phasen.Select(p => p.Text + (p.Laufend ? "*" : ""))), "Leiste folgt");
        Pruefe.Gleich("20:52 du: Phase Entern, 3 umgestellt", brett.Verlauf[0], "Verlauf");

        brett.PhaseWaehlenCommand.Execute(brett.Phasen[1]);
        Pruefe.Gleich(null, brett.PhasenRueckfrage, "die laufende noch einmal: keine Rückfrage");
        brett.PhaseWaehlenCommand.Execute(brett.Phasen[2]);
        brett.PhaseNeinCommand.Execute(null);
        Pruefe.Gleich(null, brett.PhasenRueckfrage, "Nein schließt die Rückfrage");

        // Ein anderer Leiter schaltet: Die Leiste folgt per Push, der Verlauf nennt ihn.
        a.Dienst.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(3, "Rückzug", "RÜCKZUG", Ferox, Abend.AddMinutes(1)) });
        Pruefe.Gleich("20:52 Ferox: Phase Rückzug", brett.Verlauf[0], "fremder Wechsel im Verlauf");
        Pruefe.Wahr(brett.Phasen[2].Laufend, "Leiste auf 3");
        a.Dienst.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(1, "Anflug", "ANFLUG", 0, Abend.AddMinutes(2)) });
        Pruefe.Gleich("20:52 Phase Anflug (Rückfall oder Konsole)", brett.Verlauf[0], "Rückfall ohne Namen");

        // Tasten und Brett teilen den Verlauf.
        brett.VerlaufEintragen("du: Phase Entern, 2 umgestellt");
        Pruefe.Gleich("20:52 du: Phase Entern, 2 umgestellt", brett.Verlauf[0], "Eintrag von der Phasentaste");
    }

    /// <summary>
    /// Gegenprüfung S2 (01.10.2026): Die Rückfrage am Brett zeigt den Wechsel, den du gesehen hast ("Auf Phase 2 Entern schalten? 3
    /// Funker" von 1 aus). Schaltete ein zweiter Leiter inzwischen auf 3, schickte "Ja" bisher die Phase, die gerade lief, als
    /// <c>erwartet</c>, und die Flotte ging von 3 zurück auf 2: ein Wechsel, den niemand bestätigt hatte. Jetzt erwartet "Ja" die
    /// Phase der Rückfrage; der Server sagt VERALTET, und nichts wird umgestellt.
    /// </summary>
    [Test]
    public static async Task Rueckfrage_ZweiterLeiterSchalteteInzwischen_KeinUngesehenerWechsel()
    {
        using var a = new Aufbau(dienst: MitRaidabend()) { Jetzt = Abend };
        var brett = a.Brett();
        brett.PhaseWaehlenCommand.Execute(brett.Phasen[1]);
        Pruefe.Wahr(brett.PhasenRueckfrage?.StartsWith("Auf Phase 2 Entern schalten?", StringComparison.Ordinal) == true, "Rückfrage 1 -> 2");
        a.Dienst.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(3, "Rückzug", "RÜCKZUG", Ferox, Abend.AddMinutes(1)) });
        await brett.PhaseJaCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Phase 2 erwartet 1"), "Ja erwartet die Phase der Rückfrage: " + string.Join(" | ", a.Dienst.Aufrufe));
        Pruefe.Gleich((byte)3, a.Dienst.Lage.PhaseNr, "Rückzug läuft weiter, nichts zurückgeschaltet");
        Pruefe.Wahr(brett.Meldung?.Contains("3 Rückzug läuft", StringComparison.Ordinal) == true, "sagt, was jetzt läuft: " + brett.Meldung);
    }

    [Test]
    public static void Phasenleiste_OhnePhasenOderAmServer040_Fehlt()
    {
        var d = MitRaidabend();
        d.LageSetzen(l => l with { Phasen = [], LaufendePhase = null });
        using var a = new Aufbau(dienst: d) { Jetzt = Abend };
        var brett = a.Brett();
        Pruefe.Falsch(brett.PhasenSichtbar, "ohne Phasen keine Leiste");
        d.LageSetzen(l => l with { Phasen = RaidPhasen(l.Rollen), ServerS2 = false });
        Pruefe.Falsch(brett.PhasenSichtbar, "Server 0.4.0: keine Leiste");
        d.LageSetzen(l => l with { ServerS2 = true });
        Pruefe.Wahr(brett.PhasenSichtbar, "mit Phasen am Server 0.5.0");
    }

    [Test]
    public static async Task AbendBeenden_SchaltetZuerstAufPhase1()
    {
        var d = MitRaidabend();
        d.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", Mustermann, Abend) });
        using var a = new Aufbau(dienst: d) { Jetzt = Abend };
        var brett = a.Brett();
        await brett.AbendBeendenCommand.ExecuteAsync(null);
        var aufrufe = d.Aufrufe.ToList();
        var phase = aufrufe.IndexOf("Phase 1 erwartet 2");
        Pruefe.Wahr(phase >= 0, "zurück auf die Grundstellung: " + string.Join(" | ", aufrufe));
        Pruefe.Wahr(phase < aufrufe.FindIndex(x => x.StartsWith("Verschieben", StringComparison.Ordinal)), "vor dem Zurücksetzen der Plätze, damit der letzte Stand die Grundstellung trägt");
        Pruefe.Gleich((byte)1, d.Lage.PhaseNr, "Anflug läuft");
    }

    [Test]
    public static async Task Zug_VeraltetNachEinemPhasenwechsel_SagtDas()
    {
        var d = MitRaidabend();
        using var a = new Aufbau(dienst: d) { Jetzt = Abend };
        var brett = a.Brett();
        var halten = new TaskCompletionSource();
        d.VerschiebenHalten = halten;
        var block = brett.Gruppen.SelectMany(g => g.Spalten).Single(s => s.SchiffId == Schiff3).Bloecke.Single(b => b.RolleId == Marine);
        var zug = brett.AblegenAsync(brett.Karte(Hagel)!, block);
        d.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", Ferox, Abend.AddSeconds(5)) });
        d.NaechstesVeraltet = true;
        halten.SetResult();
        Pruefe.Falsch(await zug, "abgelehnt");
        Pruefe.Wahr(brett.Meldung?.StartsWith("Inzwischen durch Phase 2 Entern umgestellt", StringComparison.Ordinal) == true, "statt eines nackten Fehlers: " + brett.Meldung);
    }
}
