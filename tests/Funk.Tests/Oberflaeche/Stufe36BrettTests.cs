// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Das Brett mit Kanälen (Fassung 0.6.0, Schritt 3.6, STUFE3-ENTWURF 5.6, FUNKPROTOKOLL 6.16 und 6.17) über der Attrappe mit dem
/// Beispielabend, ergänzt um das Kanalnetz Brücke (Ruhe, Schiff 1 bis 20): im Detailfeld je Kanal "sperren" mit Rückfrage bis 04:00
/// und "freigeben" (auch für eine Sperre auf das ganze Kanalnetz und für eine Sperre ohne Feld), nie für Global, eine Ruhe, an einen
/// Server 0.5.x oder gegen einen Admin ohne eigenen Admin-Rang; die Ablage auf ein Kanalnetz fragt mit dem Ch.-Raster nach dem Ch.
/// (ohne Ruhe, gesperrte und schon gehaltene Ch. nicht wählbar); die Kanalliste zählt und zeigt Sprecher je Kanalnetz. Dazu die
/// Abbildung der Sperren aus den Praesenz-Details. Seit 0.11.0 (Q46) "Ch." je Feld im Detail: dasselbe Raster für ein Feld der Person,
/// gesendet als felderPatch mit Feldnummer.
/// </summary>
internal static class Stufe36BrettTests
{
    internal const ushort KanalBruecke = 100;

    internal static ushort K(byte ch) => Kanalrechnung.Id(KanalBruecke, ch);

    private static readonly DateTimeOffset Vier = new(new DateTime(2026, 10, 3, 4, 0, 0, DateTimeKind.Local));

    /// <summary>Der Beispielabend mit Brücke; Kobold hält in Feld 5 Schiff 3, Brandung in Feld 6 Schiff 3.</summary>
    internal static (Aufbau A, BrettViewModel B) Abend(bool serverKanaele = true, Func<FunkerInfo, FunkerInfo>? anpassen = null)
    {
        var a = new Aufbau(dienst: MitRaidabend());
        var namen = new List<string> { "Ruhe" };
        for (var i = 1; i <= 20; i++) namen.Add("Schiff " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        a.Dienst.LageSetzen(l => l with
        {
            Netze = [.. l.Netze, new NetzInfo(KanalBruecke, "Brücke", false, false, true, 60) { Kanaele = namen }],
            Funker = [.. l.Funker.Select(f => (f.KontoId, f.Details) switch
            {
                (Kobold, { } d) => f with { Details = d with { Felder = [.. d.Felder, new FeldInfo(5, K(3), true, true, false)] } },
                (Brandung, { } d) => f with { Details = d with { Felder = [.. d.Felder, new FeldInfo(6, K(3), true, false, false)] } },
                _ => f,
            }).Select(f => anpassen?.Invoke(f) ?? f)],
            ServerKanaele = serverKanaele,
        });
        return (a, a.Brett());
    }

    internal static FunkerInfo MitSperre(FunkerInfo f, uint konto, ushort netz) =>
        f.KontoId == konto && f.Details is { } d ? f with { Details = d with { Gesperrt = [new KanalsperreInfo(netz, Vier, Nordwind, false)] } } : f;

    // ---------------------------------------------------------------- Detailfeld: sperren und freigeben

    [Test]
    public static async Task Brett_Detailfeld_SperrenMitRueckfrage_DannFreigeben()
    {
        var (a, b) = Abend();
        using var _ = a;
        b.WaehlenCommand.Execute(b.Karte(Kobold));
        var feld = b.AuswahlFelder.Single(f => f.NetzId == K(3));
        Pruefe.Wahr(feld is { Netz: "Brücke (Schiff 3)", Sperrbar: true, Gesperrt: false }, "Kanal mit Ch., sperrbar");
        Pruefe.Wahr(b.AuswahlFelder.Where(f => f.NetzId != K(3)).All(f => f.Sperrbar), "gewöhnliche Netze lassen sich auch sperren");

        b.KanalSperrenCommand.Execute(feld);
        Pruefe.Wahr(b.SperrRueckfrage is { } r && r.Contains("Kobold", StringComparison.Ordinal) && r.Contains("Brücke (Schiff 3)", StringComparison.Ordinal)
            && r.Contains("04:00", StringComparison.Ordinal), "Rückfrage mit Person, Kanal und Dauer: " + b.SperrRueckfrage);
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("KanalSperre", StringComparison.Ordinal)), "vor dem Ja geht nichts auf den Draht");
        b.SperrenNeinCommand.Execute(null);
        Pruefe.Wahr(b.SperrRueckfrage is null && !b.SperrenJaCommand.CanExecute(null), "Nein schließt die Rückfrage");

        b.KanalSperrenCommand.Execute(feld);
        await b.SperrenJaCommand.ExecuteAsync(null);
        Pruefe.Gleich("KanalSperre SETZEN 4 " + K(3).ToString(System.Globalization.CultureInfo.InvariantCulture), a.Dienst.Aufrufe[^1], "Sperre auf genau diesen Kanal");
        var gesperrt = b.AuswahlFelder.Single(f => f.NetzId == K(3));
        Pruefe.Wahr(gesperrt is { Gesperrt: true } && gesperrt.Art.StartsWith("gesperrt bis 04:00", StringComparison.Ordinal), "im Detail gesperrt: " + gesperrt.Art);
        Pruefe.Wahr(b.SperrRueckfrage is null && b.Meldung is { } m && m.Contains("gesperrt", StringComparison.Ordinal), "Meldung: " + b.Meldung);
        Pruefe.Gleich("20:52 du: Kobold gesperrt in Brücke (Schiff 3)", b.Verlauf[0], "Verlauf");

        await b.KanalFreigebenCommand.ExecuteAsync(gesperrt.Sperre);
        Pruefe.Gleich("KanalSperre AUFHEBEN 4 " + K(3).ToString(System.Globalization.CultureInfo.InvariantCulture), a.Dienst.Aufrufe[^1], "aufheben");
        Pruefe.Wahr(b.AuswahlFelder.Single(f => f.NetzId == K(3)) is { Gesperrt: false, Art: "hört und sendet" }, "wieder frei");
        Pruefe.Gleich("20:52 du: Kobold frei in Brücke (Schiff 3)", b.Verlauf[0], "Verlauf");
    }

    [Test]
    public static async Task Brett_SperreAufsKanalnetz_UndSperreOhneFeld()
    {
        var (a, b) = Abend(anpassen: f => MitSperre(f, Kobold, KanalBruecke));
        using var _ = a;
        b.WaehlenCommand.Execute(b.Karte(Kobold));
        var feld = b.AuswahlFelder.Single(f => f.NetzId == K(3));
        Pruefe.Wahr(feld is { Gesperrt: true, Sperre.NetzId: KanalBruecke }, "die Sperre auf das ganze Kanalnetz trifft auch Schiff 3");
        Pruefe.Wahr(b.AuswahlSperren.Count == 0, "sie steht am Feld, nicht noch einmal darunter");
        await b.KanalFreigebenCommand.ExecuteAsync(feld.Sperre);
        Pruefe.Gleich("KanalSperre AUFHEBEN 4 100", a.Dienst.Aufrufe[^1], "Freigeben hebt die Sperre des Kanalnetzes auf");

        // Brandung ist auf Schiff 5 gesperrt, hält aber Schiff 3: Die Sperre steht unter den Feldern, mit "freigeben".
        var (a2, b2) = Abend(anpassen: f => MitSperre(f, Brandung, K(5)));
        using var __ = a2;
        b2.WaehlenCommand.Execute(b2.Karte(Brandung));
        Pruefe.Wahr(b2.AuswahlFelder.All(f => !f.Gesperrt), "kein Feld gesperrt");
        var ohne = b2.AuswahlSperren.Single();
        Pruefe.Wahr(ohne.Kanal == "Brücke (Schiff 5)" && ohne.Text.StartsWith("gesperrt bis 04:00", StringComparison.Ordinal), "Sperre ohne Feld: " + ohne.Kanal + " " + ohne.Text);
        await b2.KanalFreigebenCommand.ExecuteAsync(ohne.Sperre);
        Pruefe.Gleich("KanalSperre AUFHEBEN 13 " + K(5).ToString(System.Globalization.CultureInfo.InvariantCulture), a2.Dienst.Aufrufe[^1], "freigegeben");
        Pruefe.Wahr(b2.AuswahlSperren.Count == 0, "Liste leer");
    }

    [Test]
    public static void Brett_NichtSperrbar_GlobalRuheAdminUndServer050()
    {
        var (a, b) = Abend(anpassen: f => f.KontoId == Kobold && f.Details is { } d
            ? f with { Details = d with { Felder = [.. d.Felder, new FeldInfo(7, K(0), true, false, false), new FeldInfo(8, Kanalrechnung.GlobalSelbstId, true, false, true)] } }
            : f);
        using var _ = a;
        b.WaehlenCommand.Execute(b.Karte(Kobold));
        Pruefe.Falsch(b.AuswahlFelder.Single(f => f.NetzId == K(0)).Sperrbar, "eine Ruhe sperrt niemand");
        Pruefe.Falsch(b.AuswahlFelder.Single(f => f.NetzId == Kanalrechnung.GlobalSelbstId).Sperrbar, "Global (Gesamt) sperrt niemand, dafür bleibt stumm");
        b.KanalSperrenCommand.Execute(b.AuswahlFelder.Single(f => f.NetzId == K(0)));
        Pruefe.Wahr(b.SperrRueckfrage is null, "keine Rückfrage für eine Ruhe");

        // Nordwind ist Admin ohne Funker-Modus: Mustermann leitet im Auftrag und sperrt ihn nicht (Rangregel 6.10).
        b.WaehlenCommand.Execute(b.Karte(Nordwind));
        Pruefe.Wahr(b.AuswahlFelder.Count > 0 && b.AuswahlFelder.All(f => !f.Sperrbar), "gegen einen Admin nur mit Admin-Rang");

        var (a2, b2) = Abend(serverKanaele: false);
        using var __ = a2;
        b2.WaehlenCommand.Execute(b2.Karte(Kobold));
        Pruefe.Wahr(b2.AuswahlFelder.All(f => !f.Sperrbar), "ein Server 0.5.x kennt keine Kanalsperren");
    }

    // ---------------------------------------------------------------- Ablage auf ein Kanalnetz: Ch.-Raster

    [Test]
    public static async Task Brett_AblageAufKanalnetz_FragtMitDemChRaster()
    {
        var (a, b) = Abend(anpassen: f => MitSperre(f, Kobold, K(4)));
        using var _ = a;
        var kobold = b.Karte(Kobold)!;
        var bridge = b.Sondernetze.Single(n => n.NetzId == KanalBruecke);
        Pruefe.Wahr(bridge.IstKanalnetz, "Brücke ist ein Kanalnetz");
        Pruefe.Gleich("Brücke als Zusatzkanal: hört und sendet, Ch. danach wählen", b.Bewerten(kobold, bridge.HoerenSenden).Text, "Text an der Maus");

        Pruefe.Falsch(await b.AblegenAsync(kobold, bridge.HoerenSenden), "erst nach der Wahl des Ch.");
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("Verschieben", StringComparison.Ordinal)), "noch nichts verschoben");
        Pruefe.Wahr(b.HatChWahl && b.ChFrage is { } frage && frage.Contains("Kobold", StringComparison.Ordinal) && frage.Contains("Brücke", StringComparison.Ordinal),
            "Frage nach dem Ch.: " + b.ChFrage);
        Pruefe.Wahr(b.ChKnoepfe.Count == 20 && b.ChKnoepfe[0] is { Ch: 1, Text: "Schiff 1", Waehlbar: true } && b.ChKnoepfe.All(k => k.Ch != 0), "Schiff 1 bis 20, ohne Ruhe");
        Pruefe.Wahr(b.ChKnoepfe.Single(k => k.Ch == 3) is { Waehlbar: false } drei && drei.Hilfe.Contains("schon", StringComparison.Ordinal), "Schiff 3 hat Kobold schon");
        Pruefe.Wahr(b.ChKnoepfe.Single(k => k.Ch == 4) is { Waehlbar: false } vier && vier.Hilfe.StartsWith("gesperrt", StringComparison.Ordinal), "Schiff 4 ist für Kobold gesperrt");

        await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 4));
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("Verschieben", StringComparison.Ordinal)), "ein nicht wählbarer Ch. tut nichts");
        await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 7));
        Pruefe.Gleich("Verschieben 4:-/- +" + K(7).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s @100", a.Dienst.Aufrufe[^1], "Zusatzkanal mit Kanal-ID");
        Pruefe.Wahr(!b.HatChWahl && b.ChKnoepfe.Count == 0, "Raster zu");
        Pruefe.Gleich("20:52 du: Kobold + Brücke (Schiff 7) (hört und sendet)", b.Verlauf[0], "Verlauf");

        // Abbrechen: nichts geht auf den Draht.
        var vorher = a.Dienst.Aufrufe.Count;
        await b.AblegenAsync(b.Karte(Eule)!, bridge.Hoeren);
        Pruefe.Wahr(b.HatChWahl, "Raster für Eule");
        b.ChAbbrechenCommand.Execute(null);
        Pruefe.Wahr(!b.HatChWahl && a.Dienst.Aufrufe.Count == vorher, "abgebrochen, nichts geschickt");

        // Ganz gesperrt: die Ablage geht nicht.
        var (a2, b2) = Abend(anpassen: f => MitSperre(f, Kobold, KanalBruecke));
        using var __ = a2;
        var bewertung = b2.Bewerten(b2.Karte(Kobold)!, b2.Sondernetze.Single(n => n.NetzId == KanalBruecke).Hoeren);
        Pruefe.Wahr(bewertung.Wirkung == Ablagewirkung.GehtNicht && bewertung.Text.Contains("gesperrt", StringComparison.Ordinal), "ganzes Kanalnetz gesperrt: " + bewertung.Text);
    }

    // ---------------------------------------------------------------- Ch. je Person im Detailfeld (Q46, Fassung 0.11.0)

    [Test]
    public static async Task Brett_ChJePerson_ImDetailfeld()
    {
        var (a, b) = Abend(anpassen: f => MitSperre(f, Kobold, K(4)) is var g && g.KontoId == Kobold && g.Details is { } d
            ? g with { Details = d with { Felder = [.. d.Felder, new FeldInfo(7, K(0), true, true, false)] } }
            : g);
        using var _ = a;
        b.WaehlenCommand.Execute(b.Karte(Kobold));
        var feld = b.AuswahlFelder.Single(f => f.NetzId == K(3));
        Pruefe.Wahr(feld is { ChWaehlbar: true, Kanalnetz: KanalBruecke, Ch: 3, Hoeren: true, Senden: true, Fest: false }, "Brücke (Schiff 3) in Feld 6: Ch. wählbar");
        Pruefe.Wahr(b.AuswahlFelder.Single(f => f.NetzId == K(0)) is { ChWaehlbar: true, Ch: 0 }, "auch ein Feld auf Ruhe bekommt einen Ch.");
        Pruefe.Wahr(b.AuswahlFelder.Where(f => !Kanalrechnung.IstKanalbereich(f.NetzId)).All(f => !f.ChWaehlbar && f.Kanalnetz is null), "gewöhnliche Kanäle haben keinen Ch.");

        // Abbrechen schickt nichts.
        b.FeldChWaehlenCommand.Execute(feld);
        Pruefe.Gleich("Kobold, Feld 6 Brücke: welcher Ch.?", b.ChFrage, "Frage nach dem Ch. für dieses Feld");
        Pruefe.Wahr(b.ChKnoepfe.Count == 20 && b.ChKnoepfe.All(k => k.Ch != 0), "Schiff 1 bis 20, ohne Ruhe");
        Pruefe.Wahr(b.ChKnoepfe.Single(k => k.Ch == 3) is { Waehlbar: false } drei && drei.Hilfe.Contains("schon", StringComparison.Ordinal), "den aktuellen Ch. hat Kobold schon");
        Pruefe.Wahr(b.ChKnoepfe.Single(k => k.Ch == 4) is { Waehlbar: false } vier && vier.Hilfe.StartsWith("gesperrt", StringComparison.Ordinal), "Schiff 4 ist für Kobold gesperrt");
        Pruefe.Wahr(b.ChKnoepfe.Single(k => k.Ch == 7) is { Waehlbar: true } sieben && sieben.Hilfe == "Kobold: Feld 6 auf Schiff 7 stellen", "Hilfe am Knopf");
        var vorher = a.Dienst.Aufrufe.Count;
        b.ChAbbrechenCommand.Execute(null);
        Pruefe.Wahr(!b.HatChWahl && a.Dienst.Aufrufe.Count == vorher, "abgebrochen, nichts geschickt");

        // Wahl: ein Verschieben mit felderPatch und Feldnummer; hören, senden und fest gehen unverändert mit.
        b.FeldChWaehlenCommand.Execute(feld);
        await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 4));
        Pruefe.Gleich(vorher, a.Dienst.Aufrufe.Count, "ein gesperrter Ch. tut nichts");
        await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 7));
        Pruefe.Gleich("Verschieben 4:-/- +" + K(7).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s#f5 @100", a.Dienst.Aufrufe[^1], "Feld 6 auf Schiff 7");
        Pruefe.Wahr(!b.HatChWahl && b.ChKnoepfe.Count == 0, "Raster zu");
        Pruefe.Gleich("20:52 du: Kobold Feld 6: Brücke (Schiff 7)", b.Verlauf[0], "Verlauf");
        Pruefe.Wahr(b.AuswahlFelder.Single(f => f.Feld == 5) is { Netz: "Brücke (Schiff 7)", Ch: 7, Fest: false }, "das Detail zeigt Schiff 7: " + string.Join(", ", b.AuswahlFelder.Select(f => f.Netz)));
        Pruefe.Wahr(b.Karte(Kobold) is { SchiffId: Schiff1, RolleId: Pilot }, "der Platz bleibt");
        await b.RueckgaengigCommand.ExecuteAsync(null);
        Pruefe.Gleich("Nichts zum Zurücknehmen.", b.Meldung, "Strg+Z nimmt nur Plätze zurück");

        // Wechselt die Auswahl, schließt das Raster für ein Feld.
        b.FeldChWaehlenCommand.Execute(b.AuswahlFelder.Single(f => f.Feld == 5));
        b.WaehlenCommand.Execute(b.Karte(Brandung));
        Pruefe.Falsch(b.HatChWahl, "andere Person gewählt: Raster zu");
    }

    [Test]
    public static void Brett_ChJePerson_KeinKnopf_Server050GeschuetztGanzGesperrt()
    {
        var (a, b) = Abend(serverKanaele: false);
        using var _ = a;
        b.WaehlenCommand.Execute(b.Karte(Kobold));
        Pruefe.Wahr(b.AuswahlFelder.All(f => !f.ChWaehlbar), "ein Server 0.5.x kennt keine Kanal-IDs");

        // Nordwind ist Admin ohne Funker-Modus: Mustermann leitet im Auftrag und wählt für ihn keinen Ch. (Rangregel 6.10).
        var (a2, b2) = Abend(anpassen: f => f.KontoId == Nordwind && f.Details is { } d ? f with { Details = d with { Felder = [.. d.Felder, new FeldInfo(6, K(5), true, false, false)] } } : f);
        using var __ = a2;
        b2.WaehlenCommand.Execute(b2.Karte(Nordwind));
        Pruefe.Falsch(b2.AuswahlFelder.Single(f => f.NetzId == K(5)).ChWaehlbar, "gegen einen Admin nicht");
        b2.FeldChWaehlenCommand.Execute(b2.AuswahlFelder.Single(f => f.NetzId == K(5)));
        Pruefe.Falsch(b2.HatChWahl, "kein Raster");

        var (a3, b3) = Abend(anpassen: f => MitSperre(f, Kobold, KanalBruecke));
        using var ___ = a3;
        b3.WaehlenCommand.Execute(b3.Karte(Kobold));
        Pruefe.Falsch(b3.AuswahlFelder.Single(f => f.NetzId == K(3)).ChWaehlbar, "das ganze Kanalnetz ist für Kobold gesperrt");
    }

    /// <summary>
    /// Ein festes Rollenfeld mit Ch. galt bis 0.10.0 als Zusatzkanal (die Rolle nennt das Kanalnetz, das Feld den Kanal): mit "entfernen" im
    /// Detail und beim Umsetzen ohne Feldnummer mitgeschickt. Seit 0.11.0 zählt es zur Rolle; ein echter Zusatzkanal bleibt einer.
    /// </summary>
    [Test]
    public static async Task Brett_FestesRollenfeldMitCh_IstKeinZusatzkanal()
    {
        var (a, b) = Abend(anpassen: f => f.KontoId == Kobold && f.Details is { } d
            ? f with { Details = d with { Felder = [.. d.Felder.Select(x => x.Feld == 5 ? x with { Fest = true } : x), new FeldInfo(3, K(2), true, true, true)] } }
            : f);
        using var _ = a;
        a.Dienst.LageSetzen(l => l with
        {
            Rollen = [.. l.Rollen.Select(r => r.Id == Pilot ? r with { Felder = [.. r.Felder, new RollenfeldInfo(3, KanalBruecke, null, true, true, false, true)] } : r)],
        });
        b.WaehlenCommand.Execute(b.Karte(Kobold));
        Pruefe.Wahr(b.AuswahlFelder.Single(f => f.Feld == 3) is { Zusatz: false, ChWaehlbar: true }, "Feld 4 kommt aus der Rolle Pilot, mit Ch.");
        Pruefe.Wahr(b.AuswahlFelder.Single(f => f.Feld == 5) is { Zusatz: true }, "Feld 6 ist ein fester Zusatzkanal auf demselben Kanalnetz");
        Pruefe.Wahr(await b.AblegenAsync(b.Karte(Kobold)!, OberflaecheBrettTests.Block(b, Schiff3, Pilot)), "Kobold als Pilot auf Schiff 3");
        Pruefe.Gleich("Verschieben 4:2/3 +" + K(3).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s @100", a.Dienst.Aufrufe[^1],
            "umgesetzt: nur der Zusatzkanal geht als felderPatch mit, nicht das Rollenfeld");
    }

    [Test]
    public static void Brett_Kanalliste_ZaehltUndZeigtSprecherJeKanalnetz()
    {
        var (a, b) = Abend();
        using var _ = a;
        var bridge = b.Sondernetze.Single(n => n.NetzId == KanalBruecke);
        Pruefe.Gleich("2 drin", bridge.Mitglieder, "Kobold und Brandung in Schiff 3 zählen für Brücke");
        Pruefe.Falsch(bridge.Spricht, "niemand spricht");
        a.Dienst.LageSetzen(l => l with { Sprecher = [.. l.Sprecher, new SprecherInfo(Kobold, "Kobold", [K(3)], false)] });
        Pruefe.Wahr(bridge.Spricht, "Kobold spricht in Schiff 3: Punkt an Brücke");
    }

    // ---------------------------------------------------------------- Personen in der Kanalliste (0.11.1, Sarevocs Befund: Person bleibt kleben)

    /// <summary>Der Abend mit festen Zusatzkanälen: Kobold Feld 6 Schiff 3 (hört und sendet), Brandung Feld 7 Schiff 3 (hört), Pollux ohne Platz mit Orga.</summary>
    private static (Aufbau A, BrettViewModel B) AbendMitZusatz() => Abend(anpassen: f => (f.KontoId, f.Details) switch
    {
        (Kobold, { } d) => f with { Details = d with { Felder = [.. d.Felder.Select(x => x.Feld == 5 ? x with { Fest = true } : x)] } },
        (Brandung, { } d) => f with { Details = d with { Felder = [.. d.Felder.Select(x => x.Feld == 6 ? x with { Fest = true } : x)] } },
        (Pollux, { } d) => f with { Details = d with { Felder = [new FeldInfo(2, NetzOrga, true, true, true)] } },
        _ => f,
    });

    private static string Kanal(ushort id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public static async Task Brett_Kanalliste_PersonenJeKanal_HerausziehenUndKreuzNurAmZusatzkanal()
    {
        var (a, b) = AbendMitZusatz();
        using var _ = a;
        var bridge = b.Sondernetze.Single(n => n.NetzId == KanalBruecke);
        var orga = b.Sondernetze.Single(n => n.NetzId == NetzOrga);
        Pruefe.Gleich("Brandung (Schiff 3), hört|Kobold (Schiff 3)", string.Join("|", bridge.Personen.Select(p => p.Text)), "je Feld eine Mini-Kachel, nach Namen, mit Ch.");
        var kobold = bridge.Personen.Single(p => p.Karte.KontoId == Kobold);
        Pruefe.Wahr(kobold is { Feld: 5, Senden: true, Fest: true, Zusatz: true } && kobold.KreuzName == "Zusatzkanal Brücke (Schiff 3) von Kobold entfernen"
            && kobold.Beschreibung.StartsWith("Kobold in Brücke (Schiff 3): hört und sendet, Feld 6, Zusatzkanal.", StringComparison.Ordinal), "Kobold: Zusatzkanal mit Kreuz: " + kobold.Beschreibung);
        Pruefe.Wahr(b.Ziehen.CanStartDrag(new Ziehinfo(kobold)), "ein Zusatzkanal lässt sich ziehen");
        var command = b.Sondernetze.Single(n => n.Name == "Kommando");
        Pruefe.Wahr(command.Personen.Count == 4 && command.Personen.All(p => !p.Zusatz) && command.Personen.Select(p => p.Text).SequenceEqual(["Brandung, hört", "Distel, hört", "Ferox", "Nordwind"]),
            "Kommando: vier aus ihren Rollen, ohne Kreuz: " + string.Join("|", command.Personen.Select(p => p.Text)));

        // Herausziehen in die Liste links: der Zusatzkanal ist weg (felderPatch mit leeren und Feldnummer); die Liste folgt dem Stand. Seit 0.11.2
        // heißt der Satz "aus ... heraus" (bis 0.11.1 "Zusatzkanal ... weg").
        Pruefe.Gleich("Kobold aus Brücke (Schiff 3) heraus", b.Bewerten(kobold, b.Teilnehmerziel).Text, "Satz an der Maus");
        Pruefe.Wahr(await b.AblegenAsync(kobold, b.Teilnehmerziel), "abgelegt");
        Pruefe.Gleich("Verschieben 4:-/- -f5 @100", a.Dienst.Aufrufe[^1], "Feld 6 leeren");
        Pruefe.Wahr(bridge.Personen.All(p => p.Karte.KontoId != Kobold) && bridge.Mitglieder == "1 drin", "Kobold ist aus der Kanalliste weg");
        Pruefe.Gleich("20:52 du: Kobold - Brücke (Schiff 3)", b.Verlauf[0], "Verlauf");

        // Das Kreuz: dasselbe für Brandung.
        var brandung = bridge.Personen.Single();
        await b.KanalPersonEntfernenCommand.ExecuteAsync(brandung);
        Pruefe.Gleich("Verschieben 13:-/- -f6 @101", a.Dienst.Aufrufe[^1], "Kreuz: Feld 7 leeren");
        Pruefe.Wahr(bridge.Personen.Count == 0 && bridge.Mitglieder == "0 drin", "Brücke leer");

        // Ein Feld aus der Rolle (Distel, Truppführer): kein Kreuz (tut nichts). Bis 0.11.1 war die Kachel nicht ziehbar (Sarevoc Q116); seit 0.11.2
        // (Sarevoc 13.10.2026, Stammbaum) lässt sich jede Person ziehen, und nach links gezogen nimmt sie auch ein Feld aus der Rolle heraus, für diesen Abend.
        var distel = command.Personen.Single(p => p.Karte.KontoId == Distel);
        var vorher = a.Dienst.Aufrufe.Count;
        await b.KanalPersonEntfernenCommand.ExecuteAsync(distel);
        Pruefe.Gleich(vorher, a.Dienst.Aufrufe.Count, "kein Kreuz an einem Feld aus der Rolle");
        Pruefe.Wahr(b.Ziehen.CanStartDrag(new Ziehinfo(distel)), "aus der Rolle: seit 0.11.2 ziehbar");
        var bewertung = b.Bewerten(distel, b.Teilnehmerziel);
        Pruefe.Wahr(bewertung is { Wirkung: Ablagewirkung.Passt, Text: "Distel aus Kommando heraus" }, "nach links: heraus, auch aus der Rolle: " + bewertung.Text);
        Pruefe.Wahr(await b.AblegenAsync(distel, b.Teilnehmerziel), "abgelegt");
        Pruefe.Gleich("Verschieben 11:-/- -f1 @102", a.Dienst.Aufrufe[^1], "Feld 2 (Kommando aus der Rolle) leeren");
        Pruefe.Wahr(command.Personen.All(p => p.Karte.KontoId != Distel), "Distel ist aus Kommando heraus");
        // Ein Admin ohne Funker-Modus (Nordwind) bleibt auch in der Kanalliste geschützt.
        Pruefe.Gleich("Nordwind ist Admin - teilt sich selbst ein", b.Bewerten(command.Personen.Single(p => p.Karte.KontoId == Nordwind), orga.Hoeren).Text, "Admin geschützt");
        Pruefe.Gleich("Nordwind ist Admin - teilt sich selbst ein", b.Bewerten(command.Personen.Single(p => p.Karte.KontoId == Nordwind), b.Teilnehmerziel).Text,
            "Admin geschützt, auch nach links");

        // Die Karte selbst (ein Name) in die Liste links tut seit 0.11.2 nichts mehr (bis 0.11.1 "auf Standard: die Zusatzkanäle weg").
        var pollux = b.Karte(Pollux)!;
        Pruefe.Gleich(Ablagebewertung.Nichts, b.Bewerten(pollux, b.Teilnehmerziel), "ohne Platz, mit Zusatzkanal: nichts");
        vorher = a.Dienst.Aufrufe.Count;
        Pruefe.Falsch(await b.AblegenAsync(pollux, b.Teilnehmerziel), "nicht abgelegt");
        Pruefe.Gleich(vorher, a.Dienst.Aufrufe.Count, "nichts geschickt");
        Pruefe.Gleich(Ablagebewertung.Nichts, b.Bewerten(b.Karte(Zinnober)!, b.Teilnehmerziel), "ohne Platz und ohne Zusatzkanal: nichts");
    }

    [Test]
    public static async Task Brett_Kanalliste_Umziehen_MitFeldnummer_KeinZweitesFeldUndRasterSchliesst()
    {
        var (a, b) = AbendMitZusatz();
        using var _ = a;
        var bridge = b.Sondernetze.Single(n => n.NetzId == KanalBruecke);
        var orga = b.Sondernetze.Single(n => n.NetzId == NetzOrga);

        // Mini-Kachel auf hört + sendet desselben Kanalnetzes: das Raster bietet Schiff 3 an (hört -> hört und sendet), das Feld bleibt Feld 7.
        var brandung = bridge.Personen.Single(p => p.Karte.KontoId == Brandung);
        Pruefe.Gleich("Brandung in Brücke: hört und sendet, Ch. danach wählen", b.Bewerten(brandung, bridge.HoerenSenden).Text, "Satz an der Maus");
        Pruefe.Falsch(await b.AblegenAsync(brandung, bridge.HoerenSenden), "erst der Ch.");
        Pruefe.Gleich("Brandung von Brücke (Schiff 3) auf Brücke (hört und sendet): welcher Ch.?", b.ChFrage, "Frage");
        var drei = b.ChKnoepfe.Single(k => k.Ch == 3);
        Pruefe.Wahr(drei.Waehlbar && drei.Hilfe == "Brandung: Schiff 3 auf hört und sendet stellen", "Schiff 3 wählbar: " + drei.Hilfe);
        await b.ChWaehlenCommand.ExecuteAsync(drei);
        Pruefe.Gleich("Verschieben 13:-/- +" + Kanal(K(3)) + "s#f6 @100", a.Dienst.Aufrufe[^1], "felderPatch mit Feldnummer 7, hört und sendet");
        Pruefe.Gleich("20:52 du: Brandung Feld 7: Brücke (Schiff 3) (hört und sendet)", b.Verlauf[0], "Verlauf");
        Pruefe.Wahr(!b.HatChWahl && bridge.Personen.Single(p => p.Karte.KontoId == Brandung) is { Senden: true, Text: "Brandung (Schiff 3)" }, "die Liste folgt");

        // Anderer Ch. (Schiff 7): das Feld wandert, kein zweites Brücke-Feld; derselbe Ch. ist jetzt "hat schon".
        brandung = bridge.Personen.Single(p => p.Karte.KontoId == Brandung);
        Pruefe.Falsch(await b.AblegenAsync(brandung, bridge.Hoeren), "erst der Ch.");
        Pruefe.Wahr(b.ChKnoepfe.Single(k => k.Ch == 3).Waehlbar && b.ChKnoepfe.Single(k => k.Ch == 3).Hilfe == "Brandung: Schiff 3 auf hört stellen", "Schiff 3: zurück auf hört ginge");
        await b.ChWaehlenCommand.ExecuteAsync(b.ChKnoepfe.Single(k => k.Ch == 7));
        Pruefe.Gleich("Verschieben 13:-/- +" + Kanal(K(7)) + "h#f6 @101", a.Dienst.Aufrufe[^1], "Schiff 7 in Feld 7, hört");
        var felder = a.Dienst.Lage.Funker.Single(f => f.KontoId == Brandung).Details!.Felder;
        Pruefe.Wahr(felder.Count(f => Kanalrechnung.Zerlegen(f.NetzId, out var n, out var c) && n == KanalBruecke && c >= 0) == 1 && felder.Single(f => f.Feld == 6).NetzId == K(7), "ein Brücke-Feld, Schiff 7");
        Pruefe.Gleich("Brandung (Schiff 7), hört|Kobold (Schiff 3)", string.Join("|", bridge.Personen.Select(p => p.Text)), "Liste");

        // Auf einen anderen Kanal (Orga): das Feld zieht um; danach steht Brandung unter Orga, nicht mehr unter Brücke.
        brandung = bridge.Personen.Single(p => p.Karte.KontoId == Brandung);
        Pruefe.Gleich("Brandung: von Brücke (Schiff 7) auf Orga (hört und sendet)", b.Bewerten(brandung, orga.HoerenSenden).Text, "Satz an der Maus");
        Pruefe.Wahr(await b.AblegenAsync(brandung, orga.HoerenSenden), "abgelegt");
        Pruefe.Gleich("Verschieben 13:-/- +" + Kanal(NetzOrga) + "s#f6 @102", a.Dienst.Aufrufe[^1], "Orga in Feld 7");
        Pruefe.Wahr(bridge.Personen.All(p => p.Karte.KontoId != Brandung) && orga.Personen.Any(p => p.Karte.KontoId == Brandung && p.Feld == 6), "unter Orga, nicht mehr unter Brücke");

        // Die Karte selbst auf denselben Ch. mit anderem hören/senden: das Raster bietet ihn an und stellt das Feld um, statt "hat schon".
        var kobold = b.Karte(Kobold)!;
        Pruefe.Falsch(await b.AblegenAsync(kobold, bridge.Hoeren), "erst der Ch.");
        var k3 = b.ChKnoepfe.Single(k => k.Ch == 3);
        Pruefe.Wahr(k3.Waehlbar && k3.Hilfe == "Kobold: Schiff 3 auf hört stellen", "Schiff 3 für Kobold: umstellen: " + k3.Hilfe);
        await b.ChWaehlenCommand.ExecuteAsync(k3);
        Pruefe.Gleich("Verschieben 4:-/- +" + Kanal(K(3)) + "h#f5 @103", a.Dienst.Aufrufe[^1], "Feld 6 auf hört");
        Pruefe.Falsch(await b.AblegenAsync(kobold, bridge.Hoeren), "noch einmal");
        var schon = b.ChKnoepfe.Single(k => k.Ch == 3);
        Pruefe.Wahr(!schon.Waehlbar && schon.Hilfe == "Kobold hat Schiff 3 schon (hört)", "so hat er ihn schon: " + schon.Hilfe);

        // Ein offenes Raster schließt mit der nächsten Ablage (Sarevocs Befund: es blieb kleben), und die nächste Wahl trifft niemanden mehr.
        Pruefe.Wahr(b.HatChWahl, "Raster offen");
        // Seit 0.11.2 tut ein Name in der Liste links nichts mehr; die andere Ablage ist deshalb Orga als Zusatzkanal für Falke.
        Pruefe.Wahr(await b.AblegenAsync(b.Karte(Falke)!, orga.Hoeren), "eine andere Ablage");
        Pruefe.Falsch(b.HatChWahl, "das Raster ist zu");
        var vorher = a.Dienst.Aufrufe.Count;
        await b.ChWaehlenCommand.ExecuteAsync(schon with { Waehlbar = true });
        Pruefe.Gleich(vorher, a.Dienst.Aufrufe.Count, "nichts mehr unterwegs");
    }

    // ---------------------------------------------------------------- Abbildung

    [Test]
    public static void Abbildung_PraesenzGesperrt_InDenDetails()
    {
        var bis = Vier.ToUnixTimeMilliseconds();
        var praesenz = new Dictionary<uint, PraesenzEintrag>
        {
            [7] = new()
            {
                KontoId = 7, Name = "Otto", Benutzername = "otto", Online = true,
                Felder = [new Mitgliedschaft { Feld = 0, NetzId = K(3), Hoeren = true, Senden = true, Fest = false }],
                Gesperrt = [new Kanalsperre { NetzId = K(3), Bis = bis, Durch = 2 }, new Kanalsperre { NetzId = KanalBruecke, Bis = bis, Durch = 2, Alle = true }],
            },
            [8] = new() { KontoId = 8, Name = "Gina", Benutzername = "gina", Online = true, Felder = [] },
        };
        var l = LageAbbildung.Abbilden(new LageEingabe(ClientZustand.AktivUdp, null, null, null, praesenz, [], false, []));
        var otto = l.Funker.Single(f => f.KontoId == 7).Details!;
        Pruefe.Wahr(otto.Gesperrt.Count == 2 && otto.Gesperrt[0] == new KanalsperreInfo(K(3), Vier, 2, false) && otto.Gesperrt[1].Alle, "Sperren mit Ende, Urheber und für alle");
        Pruefe.Gleich(K(3), otto.SperreVon(K(3))?.NetzId, "Sperre auf den Kanal");
        Pruefe.Gleich(KanalBruecke, otto.SperreVon(K(5))?.NetzId, "die auf das Kanalnetz trifft jeden Ch.");
        Pruefe.Wahr(otto.SperreVon(K(0)) is null, "eine Ruhe ist nie gesperrt");
        Pruefe.Wahr(l.Funker.Single(f => f.KontoId == 8).Details!.Gesperrt.Count == 0, "ohne Sperren leer");
    }
}
