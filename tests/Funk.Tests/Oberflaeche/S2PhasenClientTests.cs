// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Ausbaustufe S2, Teile A3 und C2 (RAID-S2-ENTWURF Abschnitt 3): die Meldezeile (Vorrang, Dauer, Halten, seit Q112 eine Meldung) und
/// die Phasensteuerung an der Testuhr: erster Druck macht scharf (mit der Zahl aus NetzListe und Praesenz), der zweite derselben
/// Taste schaltet frühestens nach 300 ms und spätestens nach 5 s, die Gegenrichtung bricht ab, 3 s ohne Antwort sagen es, VERALTET
/// übernimmt die Phase des Servers, ohne ZUWEISEN einmal je Abend ein Hinweis, ohne Phasen ein Hinweis.
/// </summary>
internal static class S2PhasenClientTests
{
    private sealed class Uhr
    {
        public long Ms { get; set; }

        public DateTimeOffset Jetzt { get; set; } = Abend;
    }

    private static (FunkDienstAttrappe Dienst, Meldezeile Zeile, Phasensteuerung Steuerung, Uhr Uhr, List<string> Verlauf) Neu(FunkDienstAttrappe? dienst = null)
    {
        var d = dienst ?? MitRaidabend();
        var uhr = new Uhr();
        var zeile = new Meldezeile(() => uhr.Ms);
        var s = new Phasensteuerung(d, zeile, () => uhr.Ms, () => uhr.Jetzt);
        var verlauf = new List<string>();
        s.Verlauf += verlauf.Add;
        return (d, zeile, s, uhr, verlauf);
    }

    /// <summary>
    /// Vorrang, Dauer und Halten. Seit Sarevoc Q112 steht nur eine Meldung da (bis dahin höchstens drei untereinander, Fehler vor Leitung vor
    /// Hinweis): Der Fehler ersetzt den Hinweis, niedrigere warten hinter ihm und verfallen, wenn ihre Dauer vorher abläuft; die gehaltene
    /// kommt danach und bleibt, bis sie gelöst wird. Die Einzelheiten prüft <see cref="MeldezeileEineMeldungTests"/>. Seit 0.11.2 mit Schlüsseln aus
    /// <see cref="Meldezeile.Erlaubt"/> (bis 0.11.1 frei gewählte; andere fallen jetzt still weg).
    /// </summary>
    [Test]
    public static void Meldezeile_VorrangDauerHalten_EineMeldung()
    {
        const string a = Teilnehmermeldungen.UmstellungSchluessel;
        const string b = Teilnehmermeldungen.EntzugSchluessel;
        const string c = Phasensteuerung.Schluessel;
        long t = 0;
        var z = new Meldezeile(() => t);
        Pruefe.Falsch(z.Sichtbar, "ohne Text unsichtbar");
        z.Zeigen(a, "Eingeteilt von Ferox: Schiff 2, Gunner", Meldeart.Hinweis, 2000);
        z.Zeigen(b, "Nicht gesendet: kein Senderecht mehr in Crew 1", Meldeart.Fehler, 5000);
        Pruefe.Gleich("Nicht gesendet: kein Senderecht mehr in Crew 1", z.Text, "der Fehler ersetzt den Hinweis, eine Zeile");
        Pruefe.Wahr(z.Sichtbar && z.HatFehler && !z.Zeigt(a), "sichtbar, mit Fehler; der ersetzte Hinweis kommt nicht wieder");
        z.Zeigen(a, "Eingeteilt von Ferox: Schiff 3, Gunner", Meldeart.Hinweis, 2000);
        Pruefe.Wahr(z.Zeilen.Count == 1 && z.Zeigt(a), "der neue Hinweis wartet hinter dem Fehler");
        z.Halten(Teilnehmermeldungen.MeldenSchluessel, "Melden: an Kommando", Meldeart.Hinweis);
        z.Zeigen(c, "Phase 2 Entern läuft - 3 umgestellt", Meldeart.Leitung, 4000);
        Pruefe.Gleich(1, z.Zeilen.Count, "eine Meldung");
        Pruefe.Gleich("Nicht gesendet: kein Senderecht mehr in Crew 1", z.Zeilen[0].Text, "der Fehler vor der Leitung");
        t = 2500;
        z.Takt();
        Pruefe.Falsch(z.Zeigt(a), "nach 2 s verfallen, ohne je zu stehen");
        t = 5000;
        z.Takt();
        Pruefe.Gleich("Melden: an Kommando", z.Text, "der Fehler ist fort, die Leitung nach 4 s verfallen: die gehaltene");
        t = 60_000;
        z.Takt();
        Pruefe.Gleich("Melden: an Kommando", z.Text, "gehalten bleibt");
        z.Loesen(Teilnehmermeldungen.MeldenSchluessel);
        Pruefe.Falsch(z.Sichtbar, "losgelassen: leer und unsichtbar");
    }

    [Test]
    public static async Task Phase_ErsterDruckScharf_ZweiterSchaltet_ErgebnisUndVerlauf()
    {
        var (d, zeile, s, uhr, verlauf) = Neu();
        await s.TasteAsync(+1);
        Pruefe.Gleich(Phasenzustand.Scharf, s.Zustand, "scharf");
        Pruefe.Gleich("Phase 2/3 ENTERN? Nochmal drücken - 3 Funker, ausgelassen: Nordwind (Admin)", zeile.Text, "Vorschau mit Zahl");
        Pruefe.Falsch(d.Aufrufe.Any(a => a.StartsWith("Phase", StringComparison.Ordinal)), "an den Server geht nichts");
        uhr.Ms = 400;
        await s.TasteAsync(+1);
        Pruefe.Wahr(d.Aufrufe.Contains("Phase 2 erwartet 1"), "geschaltet mit erwartet 1");
        Pruefe.Gleich("Phase 2 Entern läuft - 3 umgestellt, ausgelassen: Nordwind (Admin)", zeile.Text, "Ergebnis");
        Pruefe.Gleich(Phasenzustand.Ruhe, s.Zustand, "wieder Ruhe");
        Pruefe.Gleich("du: Phase Entern, 3 umgestellt", string.Join(" | ", verlauf), "Eintrag im Verlauf");
        Pruefe.Gleich((byte)2, d.Lage.PhaseNr, "Entern läuft");
        uhr.Ms = 400 + Meldezeiten.GeschaltetMs + 1;
        s.Takt();
        zeile.Takt();
        Pruefe.Falsch(zeile.Sichtbar, "nach 4 s fort");

        // Zurück: zweimal "Phase zurück".
        uhr.Ms = 20_000;
        await s.TasteAsync(-1);
        Pruefe.Wahr(zeile.Text.StartsWith("Phase 1/3 ANFLUG?", StringComparison.Ordinal), "zurück scharf: " + zeile.Text);
        uhr.Ms = 20_500;
        await s.TasteAsync(-1);
        Pruefe.Wahr(d.Aufrufe.Contains("Phase 1 erwartet 2") && d.Lage.PhaseNr == 1, "zurück auf Anflug");
    }

    [Test]
    public static async Task Phase_ZweiterDruckZuSchnell_ZaehltNicht_UndNach5sVerfallen()
    {
        var (d, zeile, s, uhr, _) = Neu();
        await s.TasteAsync(+1);
        uhr.Ms = 100;
        await s.TasteAsync(+1);
        Pruefe.Wahr(s.Zustand == Phasenzustand.Scharf && !d.Aufrufe.Any(a => a.StartsWith("Phase", StringComparison.Ordinal)), "nach 100 ms zählt der Druck nicht (Prellen)");
        uhr.Ms = Meldezeiten.ScharfMs + 1;
        s.Takt();
        zeile.Takt();
        Pruefe.Gleich(Phasenzustand.Ruhe, s.Zustand, "nach 5 s verfallen");
        Pruefe.Falsch(zeile.Sichtbar, "still verfallen");
        uhr.Ms = Meldezeiten.ScharfMs + 100;
        await s.TasteAsync(+1);
        Pruefe.Wahr(s.Zustand == Phasenzustand.Scharf && !d.Aufrufe.Any(a => a.StartsWith("Phase", StringComparison.Ordinal)), "ein Druck danach ist wieder der erste");
        uhr.Ms = Meldezeiten.ScharfMs + 100 + Meldezeiten.ZweiterDruckAbMs;
        await s.TasteAsync(+1);
        Pruefe.Wahr(d.Aufrufe.Contains("Phase 2 erwartet 1"), "ab 300 ms schaltet er");
    }

    [Test]
    public static async Task Phase_GegenrichtungBrichtAb()
    {
        var (d, zeile, s, uhr, _) = Neu();
        await s.TasteAsync(+1);
        uhr.Ms = 500;
        await s.TasteAsync(-1);
        Pruefe.Gleich(Phasenzustand.Ruhe, s.Zustand, "abgebrochen");
        Pruefe.Gleich("Nicht geschaltet: abgebrochen", zeile.Text, "Satz");
        Pruefe.Wahr(zeile.HatFehler, "als Fehler");
        Pruefe.Falsch(d.Aufrufe.Any(a => a.StartsWith("Phase", StringComparison.Ordinal)), "nichts geschickt");
    }

    [Test]
    public static async Task Phase_KeineAntwortIn3s_DannDasErgebnis()
    {
        var (d, zeile, s, uhr, _) = Neu();
        var halten = new TaskCompletionSource();
        d.PhaseHalten = halten;
        await s.TasteAsync(+1);
        uhr.Ms = 400;
        var laeuft = s.TasteAsync(+1);
        Pruefe.Gleich(Phasenzustand.Wartet, s.Zustand, "wartet auf den Server");
        uhr.Ms = 400 + Meldezeiten.AntwortMs + 1;
        s.Takt();
        Pruefe.Gleich("Keine Antwort - Stand folgt", zeile.Text, "nach 3 s");
        halten.SetResult();
        await laeuft;
        Pruefe.Wahr(zeile.Text.StartsWith("Phase 2 Entern läuft", StringComparison.Ordinal), "die Antwort kommt doch: " + zeile.Text);
    }

    [Test]
    public static async Task Phase_Veraltet_UebernimmtDiePhaseDesServers()
    {
        var (d, zeile, s, uhr, verlauf) = Neu();
        await s.TasteAsync(+1);
        d.PhaseVeraltet = (2, Ferox);
        uhr.Ms = 400;
        await s.TasteAsync(+1);
        Pruefe.Gleich("Nicht geschaltet: Ferox war schneller, 2 Entern läuft", zeile.Text, "VERALTET im Klartext");
        Pruefe.Gleich((byte)2, d.Lage.PhaseNr, "die Phase des Servers gilt");
        Pruefe.Gleich(0, verlauf.Count, "kein eigener Eintrag");
        uhr.Ms = 10_000;
        await s.TasteAsync(+1);
        Pruefe.Wahr(zeile.Text.StartsWith("Phase 3/3 RÜCKZUG?", StringComparison.Ordinal), "ab der laufenden weiter: " + zeile.Text);
    }

    /// <summary>
    /// VERALTET ohne Urheber (Konsole oder Rückfall, durch 0), seit 0.11.0 als eine Zeile: "inzwischen läuft 2 Entern" (bis dahin "die Phase hat
    /// inzwischen gewechselt, 2 Entern läuft").
    /// </summary>
    [Test]
    public static async Task Phase_Veraltet_OhneUrheber_KurzeZeile()
    {
        var (d, zeile, s, uhr, _) = Neu();
        await s.TasteAsync(+1);
        d.PhaseVeraltet = (2, 0);
        uhr.Ms = 400;
        await s.TasteAsync(+1);
        Pruefe.Gleich("Nicht geschaltet: inzwischen läuft 2 Entern", zeile.Text, "VERALTET ohne Urheber");
        Pruefe.Wahr(zeile.HatFehler, "als Fehler");
    }

    [Test]
    public static async Task Phase_OhneZuweisen_EinmalJeAbend_UndOhnePhasen()
    {
        var d = MitRaidabend();
        d.LageSetzen(l => l with { Konto = l.Konto! with { Beauftragung = null } });
        var (_, zeile, s, uhr, _) = Neu(d);
        await s.TasteAsync(+1);
        Pruefe.Gleich("Phasen schaltet die Einsatzleitung", zeile.Text, "ohne ZUWEISEN");
        Pruefe.Gleich(Phasenzustand.Ruhe, s.Zustand, "nichts scharf");
        uhr.Ms = Meldezeiten.OhneRechtMs + 1;
        zeile.Takt();
        await s.TasteAsync(-1);
        Pruefe.Falsch(zeile.Sichtbar, "einmal je Abend");
        uhr.Jetzt = Abend.AddDays(1);
        await s.TasteAsync(+1);
        Pruefe.Gleich("Phasen schaltet die Einsatzleitung", zeile.Text, "am nächsten Abend wieder");

        var e = MitRaidabend();
        e.LageSetzen(l => l with { Phasen = [], LaufendePhase = null });
        var (_, zeile2, s2, _, _) = Neu(e);
        await s2.TasteAsync(+1);
        // Seit 0.11.0 eine Zeile (bis dahin "Keine Phasen - der Admin legt sie in Flotte > Phasen an").
        Pruefe.Gleich("Keine Phasen angelegt (Flotte > Phasen)", zeile2.Text, "ohne Phasen");
        e.LageSetzen(l => l with { Phasen = RaidPhasen(l.Rollen), ServerS2 = false });
        await s2.TasteAsync(+1);
        Pruefe.Wahr(zeile2.Text.StartsWith("Keine Phasen", StringComparison.Ordinal) && !e.Aufrufe.Any(a => a.StartsWith("Phase", StringComparison.Ordinal)),
            "Server 0.4.0: keine Phasen, nichts geschickt");
    }

    [Test]
    public static async Task Phase_AmEnde_SagtDieLetzte()
    {
        var d = MitRaidabend();
        d.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(3, "Rückzug", "RÜCKZUG", Mustermann, Abend) });
        var (_, zeile, s, _, _) = Neu(d);
        await s.TasteAsync(+1);
        Pruefe.Gleich("Letzte Phase: 3 Rückzug läuft", zeile.Text, "vor geht nicht weiter");
        Pruefe.Gleich(Phasenzustand.Ruhe, s.Zustand, "nichts scharf");
        var (_, zeile1, s1, _, _) = Neu();
        await s1.TasteAsync(-1);
        Pruefe.Gleich("Erste Phase: 1 Anflug läuft", zeile1.Text, "zurück geht nicht weiter");
    }

    [Test]
    public static void Rechnung_Betroffene_AdminSchaltetAlleMit_FunkerAdminKommtMit()
    {
        var l = MitRaidabend().Lage;
        var (um, aus) = Phasenrechnung.Betroffene(l, 1, 2);
        Pruefe.Gleich("Anker,Ferox,Quarz", string.Join(",", um.Select(f => f.Name).Order(StringComparer.Ordinal)), "COs und Marines mit Platz");
        Pruefe.Gleich("Nordwind", string.Join(",", aus.Select(f => f.Name)), "der Admin ohne Funker-Modus ausgelassen");
        var alsFunker = l with { Funker = [.. l.Funker.Select(f => f.KontoId == Nordwind ? f with { Details = f.Details! with { AlsFunker = true } } : f)] };
        Pruefe.Gleich(0, Phasenrechnung.Betroffene(alsFunker, 1, 2).Ausgelassen.Count, "als Funker kommt er mit");
        var alsAdmin = l with { Konto = l.Konto! with { } };
        alsAdmin = alsAdmin with { Konto = new KontoInfo(Nordwind, "Nordwind", "nordwind", Stufe.Admin, [Recht.Zuweisen]) };
        Pruefe.Gleich(0, Phasenrechnung.Betroffene(alsAdmin, 1, 2).Ausgelassen.Count, "schaltet ein Admin, sind alle dabei");
    }
}
