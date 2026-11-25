// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Ausbaustufe S2, Teil B2 (RAID-S2-ENTWURF Abschnitt 1): "Heute als Funker" im Client. Schalter nur für Admins an Servern ab 0.5.0,
/// Rückfrage mit den Folgen, Fußzeile, "Beim Verbinden als Funker"; am Brett ist ein Admin im Funker-Modus einteilbar (grün statt
/// rot, Marke statt Schloss), "Wie zuletzt" nennt geschützte Admins statt sie still auszulassen, "Abend beenden" nimmt Admins im
/// Funker-Modus mit, und das eigene Brett verschwindet beim Einschalten ohne Neustart.
/// </summary>
internal static class S2FunkerClientTests
{
    private static readonly Recht[] AdminRechte =
        [Recht.GlobalSenden, Recht.Durchsage, Recht.DurchsageAlle, Recht.MehrfachSenden, Recht.Zuweisen, Recht.SprechanzeigeAlle, Recht.GeraeteFreigeben, Recht.KontenVerwalten, Recht.NetzeVerwalten];

    /// <summary>Der Beispielabend aus Sicht von Nordwind: Admin, auf Schiff 1 als CO; er hat selbst "Wechsel sperren" gesetzt.</summary>
    internal static FunkDienstAttrappe AlsNordwind()
    {
        var d = MitRaidabend();
        d.LageSetzen(l => l with
        {
            Konto = new KontoInfo(Nordwind, "Nordwind", "nordwind", Stufe.Admin, AdminRechte) { ScNamen = ["Nordwind"], DiscordName = "Nordwind", Zeichen = ["ADMIN"] },
            Selbstwahl = false,
            SelbstwahlDurch = Nordwind,
            Nachzuegler = true,
        });
        return d;
    }

    /// <summary>Nordwind spielt heute als Funker (Details für die Einsatzleitung, wie sie der Server schickt).</summary>
    internal static FunkLage NordwindAlsFunker(FunkLage l) => l with
    {
        Funker = [.. l.Funker.Select(f => f.KontoId == Nordwind ? f with { Details = f.Details! with { AlsFunker = true } } : f)],
    };

    [Test]
    public static async Task Schalter_NurFuerAdmin_RueckfrageMitFolgen_FusszeileUndWiederAdmin()
    {
        var d = AlsNordwind();
        using var a = new Aufbau(dienst: d) { Jetzt = Abend };
        var f = new FunkermodusViewModel(d, a.Ablage, a.Takt, () => Abend);
        Pruefe.Wahr(f.Sichtbar && !f.An, "Nordwind ist Admin am Server 0.5.0: Schalter da, aus");
        f.EinschaltenCommand.Execute(null);
        Pruefe.Wahr(f.HatRueckfrage, "erst die Rückfrage");
        Pruefe.Falsch(d.Aufrufe.Contains("Funkermodus an"), "noch nichts geschickt");
        var r = f.Rueckfrage!;
        Pruefe.Wahr(r.Contains("Deine Sperre der Selbstwahl endet", StringComparison.Ordinal), "nennt die eigene Sperre: " + r);
        Pruefe.Wahr(r.Contains("Einteilen können dann: Mustermann", StringComparison.Ordinal), "nennt, wer dann einteilt: " + r);
        Pruefe.Wahr(r.Contains("Aufstellung", StringComparison.Ordinal), "nennt das Nachziehen");
        await f.JaCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Funkermodus an") && f.An, "eingeschaltet");
        Pruefe.Gleich("als Funker bis 04:00", f.StandText, "Fußzeile");
        Pruefe.Falsch(d.Lage.Konto!.Einsatzleitung, "die Rechte der Einsatzleitung ruhen im Client");
        await f.WiederAdminCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Funkermodus aus") && !f.An && f.StandText is null, "wieder Admin, ein Klick");
        Pruefe.Wahr(d.Lage.Konto!.Einsatzleitung, "Rechte zurück");

        // Mustermann (Funker mit Auftrag) sieht den Schalter nicht, ebenso ein Admin an einem Server 0.4.0.
        using var b = new Aufbau(dienst: MitRaidabend());
        Pruefe.Falsch(new FunkermodusViewModel(b.Dienst, b.Ablage, b.Takt).Sichtbar, "kein Admin: kein Schalter");
        d.LageSetzen(l => l with { ServerS2 = false });
        Pruefe.Falsch(f.Sichtbar, "Server 0.4.0: kein Schalter (0x003C ginge dort nicht)");
        Pruefe.Gleich("Gerade teilt sonst niemand ein: Gib zuerst jemandem den Auftrag.",
            FunkermodusViewModel.RueckfrageText(d.Lage with { Funker = [.. d.Lage.Funker.Where(x => x.KontoId != Mustermann)] }).Split(". ").First(t => t.StartsWith("Gerade", StringComparison.Ordinal)) + ".",
            "ohne andere Leitung der Hinweis");
    }

    [Test]
    public static void BeimVerbinden_SchaltetEinmalJeVerbindungEin()
    {
        var d = AlsNordwind();
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
        using var a = new Aufbau(dienst: d);
        a.Ablage.Aktuell.Verbindung.AlsFunkerBeimVerbinden = true;
        var f = new FunkermodusViewModel(d, a.Ablage, a.Takt);
        Pruefe.Falsch(d.Aufrufe.Any(x => x.StartsWith("Funkermodus", StringComparison.Ordinal)), "getrennt: nichts");
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Aktiv });
        Pruefe.Gleich(1, d.Aufrufe.Count(x => x == "Funkermodus an"), "nach dem Verbinden eingeschaltet");
        d.LageSetzen(l => l with { Sprecher = [] });
        d.LageSetzen(l => l with { Konto = l.Konto! with { AlsFunkerBis = null } });
        Pruefe.Gleich(1, d.Aufrufe.Count(x => x == "Funkermodus an"), "nur einmal je Verbindung, auch wenn er ihn danach selbst ausschaltet");
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Aktiv });
        Pruefe.Gleich(2, d.Aufrufe.Count(x => x == "Funkermodus an"), "nach dem nächsten Verbinden wieder");
        Pruefe.Wahr(f.An, "an");
    }

    /// <summary>
    /// Gegenprüfung S2 (01.10.2026): Mit "Beim Verbinden als Funker" schaltete jedes Wiederverbinden den Modus wieder ein, auch nachdem
    /// du ihn selbst mit "Wieder Admin" ausgeschaltet hattest, etwa um mitten im Raid die Leitung zu übernehmen. Ein kurzer Aussetzer
    /// (WLAN, Neustart des Servers) nahm dir dann still Brett, ZUWEISEN und Phasentasten. Dein Funker-Modus ändert sich nie ohne dich
    /// (RAID-S2-ENTWURF Abschnitt 4): Nach "Wieder Admin" schaltet erst der nächste Abend (ab 04:00) ihn wieder von selbst ein.
    /// </summary>
    [Test]
    public static async Task BeimVerbinden_NachWiederAdmin_ErstAmNaechstenAbendWieder()
    {
        var d = AlsNordwind();
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
        var jetzt = Abend;
        using var a = new Aufbau(dienst: d);
        a.Ablage.Aktuell.Verbindung.AlsFunkerBeimVerbinden = true;
        var f = new FunkermodusViewModel(d, a.Ablage, a.Takt, () => jetzt);
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Aktiv });
        Pruefe.Gleich(1, d.Aufrufe.Count(x => x == "Funkermodus an"), "beim Verbinden eingeschaltet");
        await f.WiederAdminCommand.ExecuteAsync(null);
        Pruefe.Wahr(!f.An && d.Lage.Konto!.Einsatzleitung, "Nordwind übernimmt die Leitung");

        // Aussetzer: getrennt und gleich wieder verbunden.
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Aktiv });
        Pruefe.Gleich(1, d.Aufrufe.Count(x => x == "Funkermodus an"), "nach Wieder Admin schaltet ein Wiederverbinden den Modus nicht wieder ein");
        Pruefe.Wahr(!f.An && d.Lage.Konto!.Einsatzleitung, "die Leitung bleibt bei Nordwind");

        // Der nächste Abend beginnt um 04:00: Dann gilt die Einstellung wieder.
        jetzt = Abend.AddDays(1);
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Aktiv });
        Pruefe.Gleich(2, d.Aufrufe.Count(x => x == "Funkermodus an"), "am nächsten Abend wieder beim Verbinden");
        Pruefe.Wahr(f.An, "an");
    }

    [Test]
    public static void Brett_VerschwindetBeimEinschalten_OhneNeustart()
    {
        var d = AlsNordwind();
        using var a = new Aufbau(dienst: d);
        var h = a.Haupt(out _, out _, out _, out _);
        Pruefe.Wahr(h.Bereiche.Any(b => b.Kennung == HauptViewModel.RaidKennung), "Nordwind hat als Admin das Brett");
        h.AktiverBereich = h.Bereiche.Single(b => b.Kennung == HauptViewModel.RaidKennung);
        d.LageSetzen(l => l with { Konto = l.Konto! with { AlsFunkerBis = Abend.AddHours(7) } });
        Pruefe.Falsch(h.Bereiche.Any(b => b.Kennung == HauptViewModel.RaidKennung), "als Funker: das Brett verschwindet ohne Neustart");
        Pruefe.Wahr(h.AktiverBereich.Inhalt is StartViewModel, "von dort zum Einsteigen, seit 0.10.0 auf Start (E10)");
        Pruefe.Wahr(h.Funkermodus.StandText?.StartsWith("als Funker bis ", StringComparison.Ordinal) == true, "Fußzeile: " + h.Funkermodus.StandText);
    }

    [Test]
    public static void Brett_AdminImFunkermodus_GruenStattRot_MarkeStattSchloss()
    {
        using var a = new Aufbau(dienst: MitRaidabend()) { Jetzt = Abend };
        var brett = a.Brett();
        var nordwind = brett.Karte(Nordwind)!;
        var block = brett.Gruppen.SelectMany(g => g.Spalten).Single(s => s.SchiffId == Schiff3).Bloecke.Single(b => b.RolleId == Marine);
        Pruefe.Gleich(Ablagewirkung.GehtNicht, brett.Bewerten(nordwind, block).Wirkung, "ohne Funker-Modus: rot, Mustermann ist kein Admin");
        Pruefe.Wahr(nordwind.Geschuetzt, "Schloss");
        a.Dienst.LageSetzen(NordwindAlsFunker);
        Pruefe.Gleich(Ablagewirkung.Passt, brett.Bewerten(nordwind, block).Wirkung, "als Funker: grün");
        Pruefe.Wahr(nordwind.AlsFunker && !nordwind.Geschuetzt, "Marke als Funker statt Schloss");
        Pruefe.Wahr(nordwind.Beschreibung.Contains("heute als Funker", StringComparison.Ordinal), "Beschreibung: " + nordwind.Beschreibung);
    }

    [Test]
    public static void Brett_WieZuletzt_NenntGeschuetzteAdmins_UndNimmtFunkerAdminsMit()
    {
        var d = MitRaidabend();
        // Nordwind ohne Platz, mit letztem Platz Schiff 3 Pilot.
        d.LageSetzen(l => Belegungsstand(l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId == Nordwind
                ? f with { SchiffId = null, RolleId = null, Platz = null, Details = f.Details! with { LetzterPlatz = (Schiff3, Pilot), Felder = [] } }
                : f)],
        }));
        using var a = new Aufbau(dienst: d) { Jetzt = Abend };
        var brett = a.Brett();
        Pruefe.Wahr(brett.WieZuletztVorschau.Contains("Nordwind: Admin", StringComparison.Ordinal) && brett.WieZuletztVorschau.Contains("Admin, teilt sich selbst ein", StringComparison.Ordinal),
            "die Vorschau nennt Nordwind statt ihn still auszulassen: " + brett.WieZuletztVorschau);
        d.LageSetzen(NordwindAlsFunker);
        Pruefe.Wahr(brett.WieZuletztVorschau.Contains("Nordwind -> Schiff 3", StringComparison.Ordinal), "als Funker kommt Nordwind mit: " + brett.WieZuletztVorschau);
    }

    [Test]
    public static async Task Brett_AbendBeenden_NimmtAdminsImFunkermodusMit()
    {
        var d = MitRaidabend();
        using var a = new Aufbau(dienst: d) { Jetzt = Abend };
        var brett = a.Brett();
        await brett.AbendBeendenCommand.ExecuteAsync(null);
        var ohne = d.Aufrufe.Last(x => x.StartsWith("Verschieben", StringComparison.Ordinal));
        Pruefe.Falsch(ohne.Contains(" 1:0/-", StringComparison.Ordinal) || ohne.StartsWith("Verschieben 1:0/-", StringComparison.Ordinal), "ohne Funker-Modus bleibt Nordwind: " + ohne);

        var e = MitRaidabend();
        e.LageSetzen(NordwindAlsFunker);
        using var b = new Aufbau(dienst: e) { Jetzt = Abend };
        await b.Brett().AbendBeendenCommand.ExecuteAsync(null);
        var mit = e.Aufrufe.Last(x => x.StartsWith("Verschieben", StringComparison.Ordinal));
        Pruefe.Wahr(mit.StartsWith("Verschieben 1:0/-", StringComparison.Ordinal), "als Funker zurück auf Standard wie jeder: " + mit);
    }
}
