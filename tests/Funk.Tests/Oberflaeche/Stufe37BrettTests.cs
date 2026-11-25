// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// "Für alle sperren" an der Kanalliste des Bretts (Gegenprüfung 3.7; STUFE3-ENTWURF 5.6, Sarevoc Q9 ja, FUNKPROTOKOLL 6.17): Bis 3.6 konnte das
/// nur die Konsole, eine Einsatzleitung ohne Konsole gar nicht. Über der Attrappe mit dem Beispielabend und Brücke (Ruhe, Schiff 1
/// bis 20): Kanal wählen, bei Ch. den Ch. oder "alle Ch.", Rückfrage bis 04:00, erst "Ja" geht auf den Draht; die laufenden Sperren für
/// alle stehen mit "freigeben" da; Global nur mit seinen Ch., nie Gesamt; an einen Server 0.5.x nichts davon.
/// </summary>
internal static class Stufe37BrettTests
{
    private static ushort K(byte ch) => Stufe36BrettTests.K(ch);

    private static string Id(ushort id) => id.ToString(CultureInfo.InvariantCulture);

    [Test]
    public static async Task Brett_FuerAlle_ChWaehlen_Rueckfrage_SperrenUndFreigeben()
    {
        var (a, b) = Stufe36BrettTests.Abend();
        using var _ = a;
        await b.SperrenFuerAlleLadenAsync();
        Pruefe.Wahr(b.FuerAlleSichtbar && b.SperrenFuerAlle.Count == 0 && !b.HatSperrenFuerAlle, "Abschnitt da, noch keine Sperre für alle");

        b.FuerAlleSperrenCommand.Execute(null);
        Pruefe.Wahr(b.HatAlleWahl && b.AlleFrage is { } frage && frage.Contains("Kanal", StringComparison.Ordinal), "erst die Frage nach dem Kanal: " + b.AlleFrage);
        var bridge = b.AlleKnoepfe.Single(k => k.NetzId == Stufe36BrettTests.KanalBruecke);
        Pruefe.Wahr(bridge is { Weiter: true, Waehlbar: true }, "Brücke hat Ch.: weiter zur Wahl des Ch.");
        Pruefe.Falsch(b.AlleKnoepfe.Any(k => a.Dienst.Lage.NetzVon(k.NetzId) is { IstGlobal: true }), "Global ohne Ch. steht nicht da (dafür bleibt stumm)");

        b.AlleKnopfWaehlenCommand.Execute(bridge);
        Pruefe.Wahr(b.AlleKnoepfe.Count == 21 && b.AlleKnoepfe[0] is { NetzId: Stufe36BrettTests.KanalBruecke, Text: "alle Ch.", Weiter: false }
            && b.AlleKnoepfe[1] is { Text: "Schiff 1" } && b.AlleKnoepfe.All(k => k.Text != "Ruhe"), "alle Ch. und Schiff 1 bis 20, ohne Ruhe");
        b.AlleKnopfWaehlenCommand.Execute(b.AlleKnoepfe.Single(k => k.NetzId == K(3)));
        Pruefe.Wahr(!b.HatAlleWahl && b.AlleRueckfrage is { } r && r.Contains("Brücke (Schiff 3)", StringComparison.Ordinal) && r.Contains("für alle", StringComparison.Ordinal)
            && r.Contains("04:00", StringComparison.Ordinal) && r.Contains("Einsatzleitung", StringComparison.Ordinal), "Rückfrage mit Kanal, Dauer und Ausnahme: " + b.AlleRueckfrage);
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("KanalSperre", StringComparison.Ordinal)), "vor dem Ja geht nichts auf den Draht");

        await b.AlleSperrenJaCommand.ExecuteAsync(null);
        Pruefe.Gleich("KanalSperre SETZEN 0 " + Id(K(3)), a.Dienst.Aufrufe[^1], "für alle (Konto 0) auf genau diesen Ch.");
        Pruefe.Wahr(b.SperrenFuerAlle.Single() is { Kanal: "Brücke (Schiff 3)" } s && s.Text.StartsWith("bis 04:00 (von dir)", StringComparison.Ordinal) && b.HatSperrenFuerAlle,
            "steht in der Liste: " + string.Join(" | ", b.SperrenFuerAlle.Select(x => x.Kanal + " " + x.Text)));
        Pruefe.Wahr(!b.HatAlleRueckfrage && b.Meldung is { } m && m.Contains("für alle gesperrt", StringComparison.Ordinal), "Meldung: " + b.Meldung);
        Pruefe.Gleich("20:52 du: Brücke (Schiff 3) für alle gesperrt", b.Verlauf[0], "Verlauf");

        // Noch einmal: Schiff 3 ist schon gesperrt und nicht wählbar.
        b.FuerAlleSperrenCommand.Execute(null);
        b.AlleKnopfWaehlenCommand.Execute(b.AlleKnoepfe.Single(k => k.NetzId == Stufe36BrettTests.KanalBruecke));
        Pruefe.Wahr(b.AlleKnoepfe.Single(k => k.NetzId == K(3)) is { Waehlbar: false } drei && drei.Hilfe.Contains("schon für alle gesperrt", StringComparison.Ordinal),
            "Schiff 3 nicht noch einmal");
        b.AlleAbbrechenCommand.Execute(null);
        Pruefe.Wahr(!b.HatAlleWahl && b.AlleKnoepfe.Count == 0, "Abbrechen schließt die Auswahl");

        await b.AlleFreigebenCommand.ExecuteAsync(b.SperrenFuerAlle.Single());
        Pruefe.Gleich("KanalSperre AUFHEBEN 0 " + Id(K(3)), a.Dienst.Aufrufe[^1], "freigeben, sofort");
        Pruefe.Wahr(b.SperrenFuerAlle.Count == 0 && !b.HatSperrenFuerAlle, "Liste wieder leer");
        Pruefe.Gleich("20:52 du: Brücke (Schiff 3) für alle frei", b.Verlauf[0], "Verlauf");
    }

    [Test]
    public static async Task Brett_FuerAlle_GanzesKanalnetz_GewoehnlichesNetz_Nein()
    {
        var (a, b) = Stufe36BrettTests.Abend();
        using var _ = a;
        await b.SperrenFuerAlleLadenAsync();
        b.FuerAlleSperrenCommand.Execute(null);
        b.AlleKnopfWaehlenCommand.Execute(b.AlleKnoepfe.Single(k => k.NetzId == Stufe36BrettTests.KanalBruecke));
        b.AlleKnopfWaehlenCommand.Execute(b.AlleKnoepfe.Single(k => k.Text == "alle Ch."));
        Pruefe.Wahr(b.AlleRueckfrage is { } r && r.StartsWith("Brücke (alle Ch.) für alle sperren", StringComparison.Ordinal), "ganzes Kanalnetz: " + b.AlleRueckfrage);
        await b.AlleSperrenJaCommand.ExecuteAsync(null);
        Pruefe.Gleich("KanalSperre SETZEN 0 " + Id(Stufe36BrettTests.KanalBruecke), a.Dienst.Aufrufe[^1], "Sperre auf das Kanalnetz");
        b.FuerAlleSperrenCommand.Execute(null);
        Pruefe.Wahr(b.AlleKnoepfe.Single(k => k.NetzId == Stufe36BrettTests.KanalBruecke) is { Waehlbar: false }, "Brücke ist ganz gesperrt, nicht noch einmal");

        // Ein gewöhnlicher Kanal: gleich die Rückfrage; "Nein" schickt nichts.
        var gewoehnlich = b.AlleKnoepfe.First(k => !k.Weiter && k.Waehlbar);
        b.AlleKnopfWaehlenCommand.Execute(gewoehnlich);
        Pruefe.Wahr(b.HatAlleRueckfrage && b.AlleRueckfrage!.StartsWith(gewoehnlich.Text + " für alle sperren", StringComparison.Ordinal), "Rückfrage: " + b.AlleRueckfrage);
        var vorher = a.Dienst.Aufrufe.Count;
        b.AlleAbbrechenCommand.Execute(null);
        Pruefe.Wahr(!b.HatAlleRueckfrage && !b.AlleSperrenJaCommand.CanExecute(null) && a.Dienst.Aufrufe.Count == vorher, "Nein: nichts gesperrt");
    }

    [Test]
    public static async Task Brett_FuerAlle_GlobalNurMitCh_OhneGesamt_UndServer050()
    {
        var (a, b) = Stufe36BrettTests.Abend();
        using var _ = a;
        a.Dienst.LageSetzen(l => l with
        {
            Netze = [.. l.Netze.Select(n => n.IstGlobal ? n with { Kanaele = ["Ruhe", "Gesamt", "Global 1", "Global 2"] } : n)],
        });
        await b.SperrenFuerAlleLadenAsync();
        b.FuerAlleSperrenCommand.Execute(null);
        var global = b.AlleKnoepfe.Single(k => a.Dienst.Lage.NetzVon(k.NetzId) is { IstGlobal: true });
        Pruefe.Wahr(global.Weiter, "Global mit Ch.: weiter zum Ch.");
        b.AlleKnopfWaehlenCommand.Execute(global);
        Pruefe.Wahr(b.AlleKnoepfe.Select(k => k.Text).SequenceEqual(["Global 1", "Global 2"]), "nur Global 1 und 2: kein 'alle Ch.' und nie Gesamt: " + string.Join(",", b.AlleKnoepfe.Select(k => k.Text)));
        b.AlleKnopfWaehlenCommand.Execute(b.AlleKnoepfe[0]);
        await b.AlleSperrenJaCommand.ExecuteAsync(null);
        Pruefe.Gleich("KanalSperre SETZEN 0 " + Id(Kanalrechnung.Id(1, 2)), a.Dienst.Aufrufe[^1], "Global 1 für alle gesperrt");

        var (a2, b2) = Stufe36BrettTests.Abend(serverKanaele: false);
        using var __ = a2;
        b2.FuerAlleSperrenCommand.Execute(null);
        Pruefe.Wahr(!b2.FuerAlleSichtbar && !b2.HatAlleWahl, "ein Server 0.5.x kennt keine Sperren für alle: kein Abschnitt");
    }

    /// <summary>Sichtprobe für Sarevoc (nur mit <c>FUNK_BILDER</c>): das Brett mit einer Sperre für alle und offener Rückfrage, Anthrazit.</summary>
    [Test]
    public static void Sichtprobe_37_BrettFuerAlle()
    {
        if (Bildablage.Ordner() is null) return;
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            var (a, _) = Stufe36BrettTests.Abend();
            using (a)
            {
                a.Dienst.KanalSperreAsync(0, Stufe36BrettTests.KanalBruecke, sperren: true).GetAwaiter().GetResult();
                var h = a.Haupt(out _, out _, out _, out _);
                var b = h.Brett;
                b.KartenAbwartenAsync().GetAwaiter().GetResult();
                h.BereichWaehlenCommand.Execute("5");
                b.SperrenFuerAlleLadenAsync().GetAwaiter().GetResult();
                b.FuerAlleSperrenCommand.Execute(null);
                b.AlleKnopfWaehlenCommand.Execute(b.AlleKnoepfe.First(k => !k.Weiter && k.Waehlbar));
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
                Bildablage.Ablegen(bild, "37-01-brett-fuer-alle-gesperrt-und-rueckfrage-anthrazit");
            }
        });
    }
}
