// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Raid-Seite seit 0.11.2 im Modell (Sarevoc 13.10.2026, Stammbaum nach rechts), über der Attrappe mit dem Beispielabend und drei Kanälen ohne
/// Einheit: links alle Namen (live oben nach Rang, offline darunter) mit der Zahl ihrer Kanäle; oben die Rollenwahl und daneben die ersten fünf
/// Kanäle der Rolle als Reiter (seit 0.11.3); unter dem offenen Reiter sein Kanal mit Ruhe, den besetzten Ch. und "N weitere Ch." (per Klick oder
/// beim Verweilen alle Ch.). Ein Name auf einen Ch. ergibt genau
/// ein Verschieben mit einem felderPatch (hört und sendet, fest; auf Ruhe sendet er nicht; ein Feld im selben Kanalnetz zieht um); eine Person
/// im Baum auf einen anderen Ch. setzt um, nach links nimmt sie heraus (auch ein Feld aus der Rolle); ein Name nach links tut nichts; Strg+Z
/// nimmt jeden Zug im Baum zurück. Namen erfunden wie im Beispielabend.
/// </summary>
internal static class BrettBaumTests
{
    internal const ushort KanalBruecke = 100;
    internal const ushort KanalCrew = 101;
    internal const ushort MarineNetz = 110;

    internal static ushort K(byte ch) => Kanalrechnung.Id(KanalBruecke, ch);

    private static ushort C(byte ch) => Kanalrechnung.Id(KanalCrew, ch);

    private static string Id(ushort id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Der Beispielabend mit drei Kanälen ohne Einheit: Brücke (Ruhe, Schiff 1 bis 20) und Crew (Ruhe, Schiff 1 bis 5) in der Gruppe Schiffe,
    /// Marine (ohne Ch.) in der Gruppe Marine; Kommando und Orga ohne Gruppe ("Kanäle"). Kobold hört und sendet fest in Brücke (Schiff 3), Feld 6;
    /// Brandung hört dort fest, Feld 7. Vorn in der Rollenwahl die Rolle "Brett-Probe" mit diesen fünf Kanälen (Reiter der Raid-Seite seit 0.11.3).
    /// </summary>
    internal static FunkLage MitKanalBruecke(FunkLage l)
    {
        ArgumentNullException.ThrowIfNull(l);
        var bridge = new List<string> { "Ruhe" };
        for (var i = 1; i <= 20; i++) bridge.Add("Schiff " + i.ToString(CultureInfo.InvariantCulture));
        return l with
        {
            Netze =
            [
                .. l.Netze,
                new NetzInfo(KanalBruecke, "Brücke", false, false, true, 60) { Kanaele = bridge, Gruppe = "Schiffe" },
                new NetzInfo(KanalCrew, "Crew", false, false, true, 61) { Kanaele = ["Ruhe", "Schiff 1", "Schiff 2", "Schiff 3", "Schiff 4", "Schiff 5"], Gruppe = "Schiffe" },
                new NetzInfo(MarineNetz, "Marine", false, false, true, 62) { Gruppe = "Marine" },
            ],
            Funker = [.. l.Funker.Select(f => (f.KontoId, f.Details) switch
            {
                (Kobold, { } d) => f with { Details = d with { Felder = [.. d.Felder, new FeldInfo(5, K(3), true, true, true)] } },
                (Brandung, { } d) => f with { Details = d with { Felder = [.. d.Felder, new FeldInfo(6, K(3), true, false, true)] } },
                _ => f,
            })],
            ServerKanaele = true,
            Rollen =
            [
                new RolleInfo(99, "Brett-Probe", 0, false, true, ["Kommando", "Orga", "Brücke", "Crew", "Marine"])
                {
                    Felder = [.. new[] { NetzKommando, NetzOrga, KanalBruecke, KanalCrew, MarineNetz }.Select((n, i) => new RollenfeldInfo((byte)i, n, null, true, false, false, false))],
                },
                .. l.Rollen,
            ],
        };
    }

    private static (Aufbau A, BrettViewModel B) Abend(Func<FunkerInfo, FunkerInfo>? anpassen = null)
    {
        var a = new Aufbau(dienst: MitRaidabend());
        a.Dienst.LageSetzen(l =>
        {
            var m = MitKanalBruecke(l);
            return anpassen is null ? m : m with { Funker = [.. m.Funker.Select(anpassen)] };
        });
        return (a, a.Brett());
    }

    /// <summary>Öffnet den Reiter des Kanals (die Rolle Brett-Probe hat alle fünf), wie ein Klick, und gibt den Kanal darunter.</summary>
    private static BaumKanal Kanal(BrettViewModel b, string name)
    {
        b.ReiterWaehlenCommand.Execute(b.Reiter.Single(r => r.Name == name));
        return b.Baum.Single();
    }

    private static BaumCh Ch(BrettViewModel b, string kanal, string ch) => Kanal(b, kanal).Ch.Single(c => !c.IstWeitere && c.Name == ch);

    private static BaumCh Weitere(BrettViewModel b, string kanal) => Kanal(b, kanal).Ch.Single(c => c.IstWeitere);

    private static IReadOnlyList<FeldInfo> Felder(Aufbau a, uint konto) => a.Dienst.Lage.Funker.Single(f => f.KontoId == konto).Details!.Felder;

    private static List<FeldInfo> BridgeFelder(Aufbau a, uint konto) =>
        [.. Felder(a, konto).Where(f => Kanalrechnung.Zerlegen(f.NetzId, out var n, out _) && n == KanalBruecke)];

    private static int Verschiebungen(Aufbau a) => a.Dienst.Aufrufe.Count(x => x.StartsWith("Verschieben", StringComparison.Ordinal));

    private static string Namen(IEnumerable<BrettKarte> karten) => string.Join("|", karten.Select(k => k.Name));

    // ---------------------------------------------------------------- Links: Namen und Kanalzahl

    [Test]
    public static async Task Baum_NamenLinks_LiveNachRang_OfflineDarunter_MitKanalzahl()
    {
        var (a, b) = Abend();
        using var _ = a;
        await b.VerzeichnisLadenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Nordwind|Ferox|Mustermann|Anker|Brandung|Distel|Eule|Falke|Hagel|Kobold|Pollux|Quarz|Zinnober", Namen(b.NamenLive),
            "live: Admin, Führung, Einsatzleitung, dann nach Namen");
        Pruefe.Gleich("Kranich", Namen(b.NamenOffline), "offline darunter, aus dem Kontenverzeichnis");

        // Die Zahl rechts im Namen (Sarevoc: "1 heißt in einem Kanal"): Kanäle des Baums, ohne Global und Einheitsnetze, Ruhe eingeschlossen.
        Pruefe.Wahr(b.Karte(Kobold) is { Kanalzahl: 1, KanalzahlText: "1", KanalzahlHilfe: "Kobold sitzt in einem Kanal." }, "Kobold: Brücke");
        Pruefe.Wahr(b.Karte(Brandung) is { Kanalzahl: 2, KanalzahlText: "2", KanalzahlHilfe: "Brandung sitzt in 2 Kanälen." }, "Brandung: Kommando und Brücke");
        Pruefe.Wahr(b.Karte(Nordwind) is { Kanalzahl: 1 } && b.Karte(Distel) is { Kanalzahl: 1 }, "Kommando aus der Rolle zählt");
        Pruefe.Wahr(b.Karte(Eule) is { Kanalzahl: 0, KanalzahlText: "" } && b.Karte(Zinnober) is { Kanalzahl: 0, KanalzahlText: "" } && b.Karte(Kranich) is { Kanalzahl: 0 },
            "nur Einheitsnetze oder nichts: keine Zahl");

        // Wer offline geht, rutscht nach unten; die Zahl folgt jedem Push.
        a.Dienst.LageSetzen(l => l with { Funker = [.. l.Funker.Select(f => f.KontoId != Falke ? f : f with { Online = false })] });
        Pruefe.Falsch(b.NamenLive.Contains(b.Karte(Falke)!), "Falke nicht mehr live");
        Pruefe.Gleich("Falke|Kranich", Namen(b.NamenOffline), "offline, nach Namen");
        a.Dienst.LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId != Eule ? f : f with { Details = f.Details! with { Felder = [.. f.Details.Felder, new FeldInfo(5, C(0), true, false, true), new FeldInfo(6, NetzOrga, true, true, true)] } })],
        });
        Pruefe.Wahr(b.Karte(Eule) is { Kanalzahl: 2, KanalzahlText: "2" }, "Eule auf Ruhe in Crew und in Orga: 2");
    }

    // ---------------------------------------------------------------- Reiter und Stammbaum

    [Test]
    public static void Baum_RollenwahlUndKanalReiter_KanalZeigtRuheBesetzteChUndWeitere()
    {
        var (a, b) = Abend();
        using var _ = a;
        // Sarevoc 13.10.2026 nach 0.11.2: das erste Register ist das Haupt-Dropdown wie am Funkfeld, es beginnt mit der ersten Rolle der Liste;
        // daneben die Kanäle der Rolle ("da reichen derzeit 5"), darunter der Stammbaum des offenen Kanals.
        Pruefe.Wahr(b.Rollenwahl.Count > 1 && b.Rollenwahl[0].Name == "0 Brett-Probe" && b.GewaehlteRolle == b.Rollenwahl[0], "Rollenwahl mit Nummer wie im Funkfeld, die erste gewählt");
        Pruefe.Gleich("Kommando|Orga|Brücke|Crew|Marine", string.Join("|", b.Reiter.Select(r => r.Name)), "die Kanäle der Rolle in der Folge ihrer Felder");
        Pruefe.Wahr(b.Reiter[0].Aktiv && b.Reiter.Count(r => r.Aktiv) == 1 && b.Baum.Single().Name == "Kommando", "der erste Reiter ist offen, darunter nur sein Kanal");
        var command = Kanal(b, "Kommando");
        Pruefe.Wahr(command is { IstKanalnetz: false, Offen: true, Besetzt: true } && command.Ch.Count == 0, "Kommando ohne Ch., besetzt und offen");
        Pruefe.Gleich("Nordwind|Ferox|Brandung|Distel", string.Join("|", command.Leute.Select(p => p.Name)), "die Leute: live, nach Rang, dann nach Namen");
        Pruefe.Wahr(command.Leute[0].Erster && command.Leute[^1].Letzter && command.Leute.Count(p => p.Erster || p.Letzter) == 2, "Erster und Letzter für die Linie");
        Pruefe.Wahr(Kanal(b, "Orga") is { Offen: true, Besetzt: false } && b.Reiter[1].Aktiv && !b.Reiter[0].Aktiv, "Orga leer, allein unter seinem Reiter offen");

        var bridge = Kanal(b, "Brücke");
        Pruefe.Wahr(bridge is { IstKanalnetz: true, Offen: true, Besetzt: true } && bridge.Leute.Count == 0, "Brücke: Kanalnetz, besetzt, offen");
        Pruefe.Gleich("Ruhe|Schiff 3|19 weitere Ch.", string.Join("|", bridge.Ch.Select(c => c.Name)), "Ruhe immer, die besetzten Ch., dann \"weitere Ch.\"");
        Pruefe.Wahr(bridge.Ch[0] is { IstRuhe: true, Besetzt: false, Erster: true } && bridge.Ch[2] is { IstWeitere: true, Letzter: true }, "Ruhe vorn, weitere hinten");
        Pruefe.Gleich("Brandung|Kobold", string.Join("|", Ch(b, "Brücke", "Schiff 3").Leute.Select(p => p.Name)), "Schiff 3: Brandung und Kobold");
        var crew = Kanal(b, "Crew");
        Pruefe.Wahr(crew is { Offen: true, Besetzt: false }, "Crew leer, offen");
        Pruefe.Gleich("Ruhe|5 weitere Ch.", string.Join("|", crew.Ch.Select(c => c.Name)), "leer: Ruhe und die weiteren");

        // Klick auf "weitere Ch.": alle Ch., dann wieder zu.
        b.WeitereUmschaltenCommand.Execute(Weitere(b, "Brücke"));
        Pruefe.Gleich(22, bridge.Ch.Count, "Ruhe, Schiff 1 bis 20 und \"leere Ch. zu\"");
        Pruefe.Wahr(bridge.Ch[1].Name == "Schiff 1" && bridge.Ch[^1] is { IstWeitere: true, Name: "leere Ch. zu" }, "in der Reihenfolge der Liste");
        b.WeitereUmschaltenCommand.Execute(Weitere(b, "Brücke"));
        Pruefe.Gleich("Ruhe|Schiff 3|19 weitere Ch.", string.Join("|", bridge.Ch.Select(c => c.Name)), "wieder nur die besetzten");

        // Ein Push mit Sprechanzeige baut nichts neu: dieselben Objekte.
        var ch3 = Ch(b, "Brücke", "Schiff 3");
        a.Dienst.LageSetzen(l => l with { Sprecher = [new SprecherInfo(Kobold, "Kobold", [K(3)], false)] });
        Pruefe.Wahr(ReferenceEquals(bridge, Kanal(b, "Brücke")) && ReferenceEquals(ch3, Ch(b, "Brücke", "Schiff 3")), "dieselben Knoten nach einem Push");

        // Eine andere Rolle: ihre Kanäle (ohne Einheitsnetze) als Reiter, höchstens fünf; offen ist ihr erster.
        var andere = b.Rollenwahl[1];
        b.RolleWaehlenCommand.Execute(andere);
        Pruefe.Wahr(b.GewaehlteRolle == andere && b.Reiter.Count is > 0 and <= 5 && b.Reiter.All(r => andere.Netze.Contains(r.NetzId)), "die Reiter folgen der Rolle");
        Pruefe.Wahr(b.Reiter[0].Aktiv && b.Baum.Single() == b.Reiter[0], "offen ist der erste Kanal der Rolle");
    }

    [Test]
    public static void Baum_BeimZiehen_ReiterOeffnet_WeitereKlappenNachVerweilenAuf_UndDanachZu()
    {
        var (a, b) = Abend();
        using var _ = a;
        var ueber = new Ablageinfo(b.Karte(Pollux)!, null);
        b.Reiter.Single(r => r.Name == "Crew").DragOver(ueber);
        var crew = b.Baum.Single();
        Pruefe.Wahr(crew is { Name: "Crew", Offen: true }, "ein Name über dem Reiter öffnet ihn");
        crew.DragOver(ueber);
        Pruefe.Gleich("auf einen Ch. von Crew ziehen", ueber.EffectText, "ein Kanalnetz nimmt nur auf einem Ch. etwas an");
        Pruefe.Gleich(System.Windows.DragDropEffects.None, ueber.Effects, "kein Ablegen auf dem Kanal selbst");
        var weitere = crew.Ch.Single(c => c.IstWeitere);
        weitere.DragOver(ueber);
        Pruefe.Gleich(2, crew.Ch.Count, "über \"weitere Ch.\": erst nach dem Verweilen");
        a.Jetzt = a.Jetzt.AddMilliseconds(800);
        weitere.DragOver(ueber);
        Pruefe.Gleich("Ruhe|Schiff 1|Schiff 2|Schiff 3|Schiff 4|Schiff 5|leere Ch. zu", string.Join("|", crew.Ch.Select(c => c.Name)), "alle Ch. aufgeklappt");
        b.Ziehen.DragCancelled();
        Pruefe.Wahr(crew.Offen && crew.Ch.Count == 2 && b.Baum.Single() == crew, "nach dem Ziehen die leeren Ch. wieder zu, der Reiter bleibt offen");
    }

    // ---------------------------------------------------------------- Ablegen

    [Test]
    public static async Task Baum_NameAufCh_EinFelderPatch_HoertSendetFest_UmzugImKanalnetz_RuheOhneSenden()
    {
        var (a, b) = Abend();
        using var _ = a;
        b.WeitereUmschaltenCommand.Execute(Weitere(b, "Brücke"));

        // Pollux ohne Feld auf Schiff 7: genau ein Verschieben, ein felderPatch ohne Feldnummer, hört und sendet, fest; der Platz bleibt.
        var pollux = b.Karte(Pollux)!;
        Pruefe.Wahr(b.Bewerten(pollux, Ch(b, "Brücke", "Schiff 7")) is { Wirkung: Ablagewirkung.Passt, Text: "Pollux nach Brücke (Schiff 7)" }, "grün mit Satz");
        var vorher = Verschiebungen(a);
        Pruefe.Wahr(await b.AblegenAsync(pollux, Ch(b, "Brücke", "Schiff 7")), "abgelegt");
        Pruefe.Gleich(vorher + 1, Verschiebungen(a), "genau ein Verschieben");
        Pruefe.Gleich("Verschieben 14:-/- +" + Id(K(7)) + "s @100", a.Dienst.Aufrufe[^1], "ein felderPatch, ohne Rolle");
        Pruefe.Gleich(new FeldInfo(0, K(7), true, true, true), Felder(a, Pollux).Single(), "Pollux: Schiff 7, hört und sendet, fest");
        Pruefe.Wahr(pollux.RolleId is null && pollux.Kanalzahl == 1, "ohne Platz, in einem Kanal");
        Pruefe.Wahr(Ch(b, "Brücke", "Schiff 7").Leute.Single().Karte == pollux, "der Baum zeigt ihn an Schiff 7");
        Pruefe.Gleich("20:52 du: Pollux -> Brücke (Schiff 7)", b.Verlauf[0], "Verlauf");

        // Schon dort, Kanal ohne Ch. als Ziel, das Kanalnetz selbst, ein Admin.
        Pruefe.Wahr(b.Bewerten(pollux, Ch(b, "Brücke", "Schiff 7")) is { Wirkung: Ablagewirkung.Keine, Text: "Pollux ist schon in Brücke (Schiff 7)" }, "schon dort");
        Pruefe.Wahr(b.Bewerten(pollux, Kanal(b, "Brücke")) is { Wirkung: Ablagewirkung.Keine, Text: "auf einen Ch. von Brücke ziehen" }, "das Kanalnetz selbst nicht");
        Pruefe.Falsch(await b.AblegenAsync(pollux, Kanal(b, "Brücke")), "nicht abgelegt");
        Pruefe.Wahr(b.Bewerten(b.Karte(Nordwind)!, Ch(b, "Brücke", "Schiff 8")) is { Wirkung: Ablagewirkung.GehtNicht, Text: "Nordwind ist Admin - teilt sich selbst ein" },
            "ein Admin teilt sich selbst ein");

        // Kobold hat Brücke schon (Feld 6, Schiff 3): auf Schiff 9 zieht genau dieses Feld um, kein zweites im selben Kanalnetz.
        var kobold = b.Karte(Kobold)!;
        Pruefe.Gleich("Kobold nach Brücke (Schiff 9)", b.Bewerten(kobold, Ch(b, "Brücke", "Schiff 9")).Text, "Satz an der Maus");
        vorher = Verschiebungen(a);
        Pruefe.Wahr(await b.AblegenAsync(kobold, Ch(b, "Brücke", "Schiff 9")), "Kobold umgezogen");
        Pruefe.Gleich(vorher + 1, Verschiebungen(a), "genau ein Verschieben");
        Pruefe.Gleich("Verschieben 4:-/- +" + Id(K(9)) + "s#f5 @101", a.Dienst.Aufrufe[^1], "felderPatch mit Feldnummer 6");
        Pruefe.Gleich(new FeldInfo(5, K(9), true, true, true), BridgeFelder(a, Kobold).Single(), "ein Brücke-Feld, Schiff 9");
        Pruefe.Wahr(kobold is { SchiffId: Schiff1, RolleId: Pilot, Kanalzahl: 1 }, "der Platz bleibt");

        // Eule auf Ruhe: im Kanal, sendet dort aber nicht. Dann von der Ruhe auf Schiff 4: dasselbe Feld, jetzt hört und sendet.
        var eule = b.Karte(Eule)!;
        Pruefe.Wahr(await b.AblegenAsync(eule, Ch(b, "Brücke", "Ruhe")), "Eule auf Ruhe");
        Pruefe.Gleich("Verschieben 5:-/- +" + Id(K(0)) + "h @102", a.Dienst.Aufrufe[^1], "Ruhe: hört, sendet nicht");
        Pruefe.Gleich(new FeldInfo(3, K(0), true, false, true), BridgeFelder(a, Eule).Single(), "Eule auf Ruhe in Feld 4, fest, ohne Senden");
        Pruefe.Wahr(eule.Kanalzahl == 1 && Ch(b, "Brücke", "Ruhe").Leute.Single().Karte == eule, "Ruhe zählt, der Baum zeigt sie dort");
        Pruefe.Wahr(await b.AblegenAsync(eule, Ch(b, "Brücke", "Schiff 4")), "Eule von der Ruhe auf Schiff 4");
        Pruefe.Gleich("Verschieben 5:-/- +" + Id(K(4)) + "s#f3 @103", a.Dienst.Aufrufe[^1], "dasselbe Feld, jetzt sendet es");
        Pruefe.Gleich(new FeldInfo(3, K(4), true, true, true), BridgeFelder(a, Eule).Single(), "ein Feld, Schiff 4");

        // Ein Kanal ohne Ch. (Kommando) nimmt den Namen direkt.
        Pruefe.Gleich("Pollux nach Kommando", b.Bewerten(pollux, Kanal(b, "Kommando")).Text, "Kanal ohne Ch.");
        Pruefe.Wahr(await b.AblegenAsync(pollux, Kanal(b, "Kommando")), "Pollux in Kommando");
        Pruefe.Gleich("Verschieben 14:-/- +" + Id(NetzKommando) + "s @104", a.Dienst.Aufrufe[^1], "felderPatch auf Kommando");
        Pruefe.Wahr(pollux.Kanalzahl == 2 && Kanal(b, "Kommando").Leute.Any(p => p.Karte == pollux), "Pollux jetzt in zwei Kanälen");
    }

    [Test]
    public static async Task Baum_Gesperrt_UndUeberGong()
    {
        var (a, b) = Abend(f => Stufe36BrettTests.MitSperre(f, Kobold, K(4)));
        using var _ = a;
        b.WeitereUmschaltenCommand.Execute(Weitere(b, "Brücke"));
        Pruefe.Wahr(b.Bewerten(b.Karte(Kobold)!, Ch(b, "Brücke", "Schiff 4")) is { Wirkung: Ablagewirkung.GehtNicht, Text: "Kobold ist in Brücke (Schiff 4) gesperrt" },
            "gesperrter Ch.: rot mit Grund");
        Pruefe.Falsch(await b.AblegenAsync(b.Karte(Kobold)!, Ch(b, "Brücke", "Schiff 4")), "nicht abgelegt");
        Pruefe.Gleich("Kobold ist in Brücke (Schiff 4) gesperrt", b.Meldung, "der Grund steht unten");

        // Über gong: grün über dem Ch., dann loslassen.
        var zehn = Ch(b, "Brücke", "Schiff 10");
        var ueber = new Ablageinfo(b.Karte(Hagel)!, null);
        zehn.DragOver(ueber);
        Pruefe.Wahr(ueber.Effects == System.Windows.DragDropEffects.Move && zehn.Ablagezustand == Ablagewirkung.Passt && ueber.EffectText == "Hagel nach Brücke (Schiff 10)",
            "grün über dem Ch.: " + ueber.EffectText);
        zehn.Drop(ueber);
        Pruefe.Wahr(SpinWait.SpinUntil(() => a.Dienst.Aufrufe.Contains("Verschieben 10:-/- +" + Id(K(10)) + "s @100"), 5000), "Ablage über gong: " + string.Join(" | ", a.Dienst.Aufrufe));
        Pruefe.Gleich(Ablagewirkung.Keine, zehn.Ablagezustand, "nach dem Ablegen zurück");
    }

    [Test]
    public static async Task Baum_PersonUmsetzen_NachLinksHeraus_NameNachLinksNichts()
    {
        var (a, b) = Abend();
        using var _ = a;
        b.WeitereUmschaltenCommand.Execute(Weitere(b, "Brücke"));

        // Brandung im Baum (Feld 7, hört) auf Schiff 9: das Feld zieht um, jetzt hört und sendet.
        var brandung = Ch(b, "Brücke", "Schiff 3").Leute.Single(p => p.Karte.KontoId == Brandung);
        Pruefe.Wahr(b.Ziehen.CanStartDrag(new Ziehinfo(brandung)), "eine Person im Baum lässt sich ziehen");
        Pruefe.Gleich("Brandung von Brücke (Schiff 3) nach Brücke (Schiff 9)", b.Bewerten(brandung, Ch(b, "Brücke", "Schiff 9")).Text, "Satz an der Maus");
        Pruefe.Wahr(await b.AblegenAsync(brandung, Ch(b, "Brücke", "Schiff 9")), "umgesetzt");
        Pruefe.Gleich("Verschieben 13:-/- +" + Id(K(9)) + "s#f6 @100", a.Dienst.Aufrufe[^1], "felderPatch mit Feldnummer 7");
        Pruefe.Gleich(new FeldInfo(6, K(9), true, true, true), BridgeFelder(a, Brandung).Single(), "ein Feld, Schiff 9, hört und sendet");
        Pruefe.Wahr(Ch(b, "Brücke", "Schiff 3").Leute.All(p => p.Karte.KontoId != Brandung) && Ch(b, "Brücke", "Schiff 9").Leute.Any(p => p.Karte.KontoId == Brandung),
            "der Baum folgt");

        // Auf einen Ch. eines anderen Kanalnetzes: das Feld zieht um, Brücke ist danach frei von ihr.
        b.WeitereUmschaltenCommand.Execute(Weitere(b, "Crew"));
        brandung = Ch(b, "Brücke", "Schiff 9").Leute.Single(p => p.Karte.KontoId == Brandung);
        Pruefe.Wahr(await b.AblegenAsync(brandung, Ch(b, "Crew", "Schiff 2")), "nach Crew");
        Pruefe.Gleich("Verschieben 13:-/- +" + Id(C(2)) + "s#f6 @101", a.Dienst.Aufrufe[^1], "dasselbe Feld, anderer Kanal");
        Pruefe.Wahr(BridgeFelder(a, Brandung).Count == 0 && Felder(a, Brandung).Single(f => f.Feld == 6).NetzId == C(2), "Brandung ist aus Brücke heraus, in Crew");

        // Eine Person nach links: das Feld wird geleert.
        var kobold = Ch(b, "Brücke", "Schiff 3").Leute.Single();
        Pruefe.Wahr(b.Bewerten(kobold, b.Teilnehmerziel) is { Wirkung: Ablagewirkung.Passt, Text: "Kobold aus Brücke (Schiff 3) heraus" }, "nach links: heraus");
        Pruefe.Wahr(await b.AblegenAsync(kobold, b.Teilnehmerziel), "heraus");
        Pruefe.Gleich("Verschieben 4:-/- -f5 @102", a.Dienst.Aufrufe[^1], "Feld 6 leeren");
        Pruefe.Wahr(BridgeFelder(a, Kobold).Count == 0 && b.Karte(Kobold) is { Kanalzahl: 0, SchiffId: Schiff1, RolleId: Pilot }, "aus dem Kanal, der Platz bleibt");
        Pruefe.Gleich("20:52 du: Kobold - Brücke (Schiff 3)", b.Verlauf[0], "Verlauf");

        // Auch ein Feld aus der Rolle (Distel, Kommando als Truppführer) nimmt die Person nach links heraus, für diesen Abend.
        var distel = Kanal(b, "Kommando").Leute.Single(p => p.Karte.KontoId == Distel);
        Pruefe.Falsch(distel.Zusatz, "Kommando kommt aus der Rolle");
        Pruefe.Gleich("Distel aus Kommando heraus", b.Bewerten(distel, b.Teilnehmerziel).Text, "auch aus der Rolle heraus");
        Pruefe.Wahr(await b.AblegenAsync(distel, b.Teilnehmerziel), "Distel heraus");
        Pruefe.Gleich("Verschieben 11:-/- -f1 @103", a.Dienst.Aufrufe[^1], "Feld 2 leeren");
        Pruefe.Wahr(Kanal(b, "Kommando").Leute.All(p => p.Karte.KontoId != Distel) && b.Karte(Distel) is { RolleId: Truppfuehrer }, "aus Kommando heraus, Truppführer bleibt");

        // Ein Name von links nach links: nichts, auch nicht "auf Standard" (bis 0.11.1).
        var vorher = a.Dienst.Aufrufe.Count;
        foreach (var konto in new[] { Eule, Pollux, Zinnober, Brandung })
        {
            var karte = b.Karte(konto)!;
            Pruefe.Gleich(Ablagebewertung.Nichts, b.Bewerten(karte, b.Teilnehmerziel), karte.Name + " nach links: nichts");
            Pruefe.Falsch(await b.AblegenAsync(karte, b.Teilnehmerziel), karte.Name + ": nicht abgelegt");
        }
        Pruefe.Gleich(vorher, a.Dienst.Aufrufe.Count, "nichts geschickt");
        var links = new Ablageinfo(b.Karte(Eule)!, null);
        b.Teilnehmerziel.DragOver(links);
        Pruefe.Wahr(links.Effects == System.Windows.DragDropEffects.None && b.Teilnehmerziel.Ablagezustand == Ablagewirkung.Keine, "über gong: kein Ablegen links");
    }

    [Test]
    public static async Task Baum_StrgZ_NimmtJedenZugZurueck()
    {
        var (a, b) = Abend();
        using var _ = a;
        b.WeitereUmschaltenCommand.Execute(Weitere(b, "Brücke"));

        // Ein Name auf einen Ch., zurück: das neue Feld wird geleert.
        Pruefe.Wahr(await b.AblegenAsync(b.Karte(Pollux)!, Ch(b, "Brücke", "Schiff 7")), "Pollux auf Schiff 7");
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Verschieben 14:-/- -f0 @101", a.Dienst.Aufrufe[^1], "zurück: Feld 1 leeren, mit basisVersion");
        Pruefe.Wahr(Felder(a, Pollux).Count == 0 && b.Karte(Pollux)!.Kanalzahl == 0, "Pollux wie vorher");
        Pruefe.Gleich("20:52 du: Pollux zurück", b.Verlauf[0], "Verlauf");

        // Ein Umzug im Kanalnetz, zurück: das Feld wieder auf Schiff 3, wie es war.
        Pruefe.Wahr(await b.AblegenAsync(b.Karte(Kobold)!, Ch(b, "Brücke", "Schiff 9")), "Kobold auf Schiff 9");
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Verschieben 4:-/- +" + Id(K(3)) + "s#f5 @103", a.Dienst.Aufrufe[^1], "zurück: Feld 6 wieder auf Schiff 3");
        Pruefe.Gleich(new FeldInfo(5, K(3), true, true, true), BridgeFelder(a, Kobold).Single(), "Kobold wie vorher");

        // Eine Person nach links, zurück: das Feld ist wieder da (Brandung hört nur).
        var brandung = Ch(b, "Brücke", "Schiff 3").Leute.Single(p => p.Karte.KontoId == Brandung);
        Pruefe.Wahr(await b.AblegenAsync(brandung, b.Teilnehmerziel), "Brandung heraus");
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Verschieben 13:-/- +" + Id(K(3)) + "h#f6 @105", a.Dienst.Aufrufe[^1], "zurück: Feld 7 wieder, hört");
        Pruefe.Gleich(new FeldInfo(6, K(3), true, false, true), BridgeFelder(a, Brandung).Single(), "Brandung wie vorher");

        // Hat jemand anderes inzwischen umgestellt (VERALTET), bleibt dessen Stand.
        Pruefe.Wahr(await b.AblegenAsync(b.Karte(Hagel)!, Ch(b, "Brücke", "Schiff 12")), "Hagel auf Schiff 12");
        a.Dienst.NaechstesVeraltet = true;
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Nicht zurückgenommen: Hagel wurde danach umgestellt.", b.Meldung, "VERALTET");
        Pruefe.Gleich(K(12), BridgeFelder(a, Hagel).Single().NetzId, "Hagels Stand bleibt");
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Nichts zum Zurücknehmen.", b.Meldung, "jeder Zug nur einmal");
    }
}
