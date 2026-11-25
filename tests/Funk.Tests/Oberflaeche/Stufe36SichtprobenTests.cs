// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Sichtproben des Schritts 3.6 für Sarevoc, nur mit <c>FUNK_BILDER</c> (Präfix 36-), im Speicher gezeichnet ohne Fensterhandle: das Brett
/// mit gesperrtem Kanal, Sperre ohne Feld und offener Rückfrage, das Brett mit dem Ch.-Raster nach einer Ablage auf ein Kanalnetz, die
/// Startseite, das Funkfeld mit Kanälen und Ch., "Meine Kanäle" und der Bereich Audio mit "Automatische Lautstärke". Alles in Anthrazit.
/// </summary>
internal static class Stufe36SichtprobenTests
{
    private static readonly Bildschirm Monitor100 = new(new Pixelrechteck(0, 0, 3840, 2160), new Pixelrechteck(0, 0, 3840, 2076), 1.0, true);

    private static FesteBildschirme Monitore { get; } = new([Monitor100]);

    [Test]
    public static void Sichtproben_36_FuerJu()
    {
        if (Bildablage.Ordner() is null) return;
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);

            // Brett: Kobold in Schiff 3 gesperrt (von Nordwind), auf Schiff 5 gesperrt ohne Feld, Rückfrage für Brücke 1 offen.
            var (a1, _) = Stufe36BrettTests.Abend(anpassen: f => f.KontoId == Kobold && f.Details is { } d
                ? f with { Details = d with { Gesperrt = [new(Stufe36BrettTests.K(3), new DateTimeOffset(new DateTime(2026, 10, 3, 4, 0, 0, DateTimeKind.Local)), Nordwind, false),
                    new(Stufe36BrettTests.K(5), new DateTimeOffset(new DateTime(2026, 10, 3, 4, 0, 0, DateTimeKind.Local)), Nordwind, false)] } }
                : f);
            using (a1)
            {
                var h = a1.Haupt(out _, out _, out _, out _);
                var b = h.Brett;
                b.KartenAbwartenAsync().GetAwaiter().GetResult();
                h.BereichWaehlenCommand.Execute("5");
                b.WaehlenCommand.Execute(b.Karte(Kobold));
                b.KanalSperrenCommand.Execute(b.AuswahlFelder.First(x => x.Sperrbar && !x.Gesperrt));
                var haupt = new Hauptfenster(h, a1.Ablage);
                fenster.Add(haupt);
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
                Bildablage.Ablegen(bild, "36-01-brett-sperre-und-rueckfrage-anthrazit");
            }

            // Brett: Eule auf Brücke (hört und sendet) gezogen, das Ch.-Raster fragt nach dem Ch.
            var (a2, _) = Stufe36BrettTests.Abend();
            using (a2)
            {
                var h = a2.Haupt(out _, out _, out _, out _);
                var b = h.Brett;
                b.KartenAbwartenAsync().GetAwaiter().GetResult();
                h.BereichWaehlenCommand.Execute("5");
                var bridge = b.Sondernetze.Single(n => n.NetzId == Stufe36BrettTests.KanalBruecke);
                b.AblegenAsync(b.Karte(Eule)!, bridge.HoerenSenden).GetAwaiter().GetResult();
                var haupt = new Hauptfenster(h, a2.Ablage);
                fenster.Add(haupt);
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
                Bildablage.Ablegen(bild, "36-02-brett-ch-raster-anthrazit");
            }

            using (var a = new Aufbau(KanalBeispiel()))
            {
                a.Ablage.Aktuell.Farbstellung = Farbstellung.Anthrazit;
                var h = a.Haupt(out _, out _, out _, out _);
                var haupt = new Hauptfenster(h, a.Ablage, Monitore);
                fenster.Add(haupt);
                h.AktiverBereich = h.Bereiche.Single(x => x.Inhalt is StartViewModel);
                Bildablage.Ablegen(Ansichtspruefung.Ganz(haupt, 960), "36-03-start-anthrazit");
                // Die Bereiche Meine Kanäle und Audio (Bilder 36-05 und 36-06) gibt es seit 0.10.0 nicht mehr; ihr Inhalt steht unter
                // Einstellungen, die Bilder dazu machen die Klassen OberflaecheUmbau*. Gefunden im Lauf mit FUNK_BILDER (0.11.0).
            }

            using (var a = new Aufbau(KanalBeispiel()))
            {
                var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
                var ff = new FunkfeldFenster(f, a.Ablage, Monitore);
                fenster.Add(ff);
                var (raster, _) = Bildlauf.Zeichnen(ff, 1.75);
                Bildablage.Ablegen(raster, "36-04-funkfeld-raster-anthrazit");
                f.Dispose();
            }
        });
    }
}
