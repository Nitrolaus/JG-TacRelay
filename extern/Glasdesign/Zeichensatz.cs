// Glasdesign - Maintainer: Sarevoc
using System.Windows;

namespace Glasdesign;

/// <summary>
/// Die Zeichen der Stilfamilien 2, Stufe 2 (seit 0.5.0-beta.8): Winkel nach rechts, links und oben, Zahnrad, Person, Bild,
/// Raute, Warnung, Info, Kette, Uhr, Liste, Korb, Dokument, Etikett, Doppelwinkel, Sortieren, Filter. Eigene Geometrie,
/// keine fremden Icons. Jedes Zeichen ist als Linienzüge in einem eigenen Rechenraster beschrieben und wird danach in die
/// Box von <see cref="Mass"/> eingepasst: Die äußersten Punkte der Mittellinien kommen genau auf die um die halbe
/// Strichbreite eingerückte Box. Mit runden Kappen und Ecken reicht der Strich dann an allen vier Seiten genau bis an die
/// Box, und das Zeichen sitzt absolut mittig wie die bisherigen (gemessen an Pixeln in RueckmeldungTests).
/// </summary>
public static partial class Zeichner
{
    /// <summary>Breite und Höhe der Box der neuen Zeichen, bezogen auf die Grundgröße; die bisherigen sind (1, 1).</summary>
    private static (double Breite, double Hoehe) VerhaeltnisStufe2(Glaszeichen z) => z switch
    {
        Glaszeichen.WinkelRechts or Glaszeichen.WinkelLinks => (0.56, 1),
        Glaszeichen.WinkelHoch => (1, 0.56),
        Glaszeichen.Doppelwinkel => (1, 0.84),
        Glaszeichen.Sortieren => (0.6, 1),
        Glaszeichen.Filter => (1, 0.9),
        Glaszeichen.Warnung => (1, 0.88),
        Glaszeichen.Person => (0.9, 1),
        Glaszeichen.Bild => (1, 0.8),
        Glaszeichen.Liste => (1, 0.8),
        Glaszeichen.Korb => (1, 0.9),
        Glaszeichen.Dokument => (0.8, 1),
        _ => (1, 1),
    };

    /// <summary>Die Linienzüge eines neuen Zeichens, eingepasst in die eingerückte Box (Pixel); leer für unbekannte.</summary>
    private static Point[][] ZuegeStufe2(Glaszeichen zeichen, double x0, double y0, double x1, double y1)
    {
        var roh = Roh(zeichen);
        return roh.Length == 0 ? [] : Einpassen(roh, x0, y0, x1, y1);
    }

    /// <summary>
    /// Die Züge linear auf das Rechteck (x0, y0) bis (x1, y1) abgebildet: Die Grenzen aller Punkte landen genau auf seinen
    /// Seiten. Das Verhältnis der Box (<see cref="VerhaeltnisStufe2"/>) passt zum Rechenraster, die Verzerrung bleibt im
    /// Bruchteil eines Pixels.
    /// </summary>
    private static Point[][] Einpassen(IReadOnlyList<Point[]> roh, double x0, double y0, double x1, double y1)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var zug in roh)
            foreach (var p in zug)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }
        var bx = maxX - minX;
        var by = maxY - minY;
        double X(double x) => bx < 1e-9 ? (x0 + x1) / 2 : x0 + (x - minX) / bx * (x1 - x0);
        double Y(double y) => by < 1e-9 ? (y0 + y1) / 2 : y0 + (y - minY) / by * (y1 - y0);
        return [.. roh.Select(zug => zug.Select(p => new Point(X(p.X), Y(p.Y))).ToArray())];
    }

    /// <summary>Die Mittellinien im Rechenraster (nur ihre Verhältnisse zählen).</summary>
    private static Point[][] Roh(Glaszeichen z)
    {
        switch (z)
        {
            case Glaszeichen.WinkelRechts:
                return [[new(0, 0), new(1, 0.5), new(0, 1)]];
            case Glaszeichen.WinkelLinks:
                return [[new(1, 0), new(0, 0.5), new(1, 1)]];
            case Glaszeichen.WinkelHoch:
                return [[new(0, 1), new(0.5, 0), new(1, 1)]];
            case Glaszeichen.Doppelwinkel:
                return [[new(0, 0), new(0.42, 0.5), new(0, 1)], [new(0.58, 0), new(1, 0.5), new(0.58, 1)]];
            case Glaszeichen.Sortieren:
                return [[new(0, 0.36), new(0.5, 0), new(1, 0.36)], [new(0, 0.64), new(0.5, 1), new(1, 0.64)]];
            case Glaszeichen.Filter:
                return [[new(0, 0), new(1, 0), new(0.6, 0.5), new(0.6, 0.86), new(0.4, 1), new(0.4, 0.5), new(0, 0)]];
            case Glaszeichen.Raute:
                return [[new(0.5, 0), new(1, 0.5), new(0.5, 1), new(0, 0.5), new(0.5, 0)]];
            case Glaszeichen.Warnung:
                return [[new(0.5, 0), new(1, 0.88), new(0, 0.88), new(0.5, 0)], [new(0.5, 0.31), new(0.5, 0.54)], Punkt(0.5, 0.7)];
            case Glaszeichen.Info:
                return [Kreis(0.5, 0.5, 0.5, 0.5), [new(0.5, 0.44), new(0.5, 0.75)], Punkt(0.5, 0.27)];
            case Glaszeichen.Uhr:
                return [Kreis(0.5, 0.5, 0.5, 0.5), [new(0.5, 0.22), new(0.5, 0.5), new(0.71, 0.63)]];
            case Glaszeichen.Liste:
                return
                [
                    Punkt(0, 0), [new(0.3, 0), new(1, 0)],
                    Punkt(0, 0.4), [new(0.3, 0.4), new(1, 0.4)],
                    Punkt(0, 0.8), [new(0.3, 0.8), new(1, 0.8)],
                ];
            case Glaszeichen.Korb:
            {
                // Henkel als Halbellipse über dem Rand, Rand quer, darunter der Korb schräg nach innen.
                var henkel = new Point[17];
                for (var i = 0; i <= 16; i++)
                {
                    var w = Math.PI + i * Math.PI / 16;
                    henkel[i] = new Point(0.5 + 0.27 * Math.Cos(w), 0.4 + 0.4 * Math.Sin(w));
                }
                return [henkel, [new(0, 0.4), new(1, 0.4)], [new(0.1, 0.4), new(0.22, 0.9), new(0.78, 0.9), new(0.9, 0.4)], [new(0.5, 0.56), new(0.5, 0.74)]];
            }
            case Glaszeichen.Dokument:
                return
                [
                    [new(0, 0), new(0.5, 0), new(0.8, 0.3), new(0.8, 1), new(0, 1), new(0, 0)],
                    [new(0.5, 0), new(0.5, 0.3), new(0.8, 0.3)],
                    [new(0.18, 0.56), new(0.62, 0.56)], [new(0.18, 0.78), new(0.62, 0.78)],
                ];
            case Glaszeichen.Etikett:
            {
                // Waagerecht mit der Spitze links gezeichnet, dann um 45 Grad gedreht: Die Spitze zeigt nach oben links.
                Point[][] waagerecht =
                [
                    [new(0, 0.5), new(0.32, 0.18), new(1, 0.18), new(1, 0.82), new(0.32, 0.82), new(0, 0.5)],
                    Kreis(0.3, 0.5, 0.085, 0.085),
                ];
                return Gedreht(waagerecht, 45, 0.5, 0.5);
            }
            case Glaszeichen.Kette:
            {
                // Zwei Glieder auf einer Achse, in der Mitte getrennt, quer dazu zwei Funken (mit Abstand zu den Gliedern, auch bei
                // dickem Strich); die Achse läuft von unten links nach oben rechts.
                Point[][] achse =
                [
                    Glied(-0.32, 0.42, 0.24), Glied(0.32, 0.42, 0.24),
                    [new(0, 0.17), new(0, 0.34)], [new(0, -0.17), new(0, -0.34)],
                ];
                return Gedreht(achse, -45, 0, 0);
            }
            case Glaszeichen.Person:
            {
                var kopf = Kreis(0.5, 0.21, 0.21, 0.21);
                var schultern = new Point[18];
                for (var i = 0; i <= 16; i++)
                {
                    var w = Math.PI + i * Math.PI / 16;
                    schultern[i] = new Point(0.5 + 0.5 * Math.Cos(w), 1 + 0.38 * Math.Sin(w));
                }
                schultern[17] = schultern[0];
                return [kopf, schultern];
            }
            case Glaszeichen.Bild:
                return
                [
                    [new(0, 0), new(1, 0), new(1, 0.8), new(0, 0.8), new(0, 0)],
                    [new(0.14, 0.64), new(0.38, 0.36), new(0.58, 0.56), new(0.68, 0.46), new(0.86, 0.64)],
                    Kreis(0.71, 0.23, 0.075, 0.075),
                ];
            case Glaszeichen.Zahnrad:
                return [Zahnkranz(0.5, 0.37, 8, 8, 14), Kreis(0, 0, 0.16, 0.16)];
            default:
                return [];
        }
    }

    /// <summary>Ein Punkt als sehr kurzer Strich: Die runden Kappen machen daraus einen Punkt in Strichbreite.</summary>
    private static Point[] Punkt(double x, double y) => [new(x, y), new(x, y + 0.004)];

    /// <summary>Ein geschlossener Kreis (Ellipse) aus 32 Stücken, mit Punkten genau oben, unten, links und rechts.</summary>
    private static Point[] Kreis(double mx, double my, double rx, double ry)
    {
        var k = new Point[33];
        for (var i = 0; i <= 32; i++)
        {
            var w = i * Math.PI / 16;
            k[i] = new Point(mx + rx * Math.Cos(w), my + ry * Math.Sin(w));
        }
        return k;
    }

    /// <summary>Ein Kettenglied auf der waagerechten Achse: Stadion um (<paramref name="mitte"/>, 0), lang und breit.</summary>
    private static Point[] Glied(double mitte, double lang, double breit)
    {
        var r = breit / 2;
        var gerade = lang / 2 - r;
        var p = new List<Point>();
        for (var i = 0; i <= 12; i++)
        {
            var w = -Math.PI / 2 + i * Math.PI / 12;
            p.Add(new Point(mitte + gerade + r * Math.Cos(w), r * Math.Sin(w)));
        }
        for (var i = 0; i <= 12; i++)
        {
            var w = Math.PI / 2 + i * Math.PI / 12;
            p.Add(new Point(mitte - gerade + r * Math.Cos(w), r * Math.Sin(w)));
        }
        p.Add(p[0]);
        return [.. p];
    }

    /// <summary>
    /// Der Umriss eines Zahnrads um (0, 0): <paramref name="zaehne"/> Zähne mit Kopfradius <paramref name="aussen"/> und
    /// Fußradius <paramref name="innen"/>, halber Kopfwinkel <paramref name="kopf"/> und halber Fußwinkel
    /// <paramref name="fuss"/> in Grad; der erste Zahn zeigt nach rechts, also liegen bei acht Zähnen je einer an jeder Seite.
    /// </summary>
    internal static Point[] Zahnkranz(double aussen, double innen, int zaehne, double kopf, double fuss)
    {
        var p = new List<Point>();
        var teil = 360.0 / zaehne;
        for (var k = 0; k < zaehne; k++)
        {
            var a = k * teil;
            p.Add(Polar(innen, a - fuss));
            p.Add(Polar(aussen, a - kopf));
            p.Add(Polar(aussen, a + kopf));
            p.Add(Polar(innen, a + fuss));
            p.Add(Polar(innen, a + teil / 2));
        }
        p.Add(p[0]);
        return [.. p];

        static Point Polar(double r, double grad) => new(r * Math.Cos(grad * Math.PI / 180), r * Math.Sin(grad * Math.PI / 180));
    }

    /// <summary>Alle Züge um (<paramref name="mx"/>, <paramref name="my"/>) gedreht (Grad, im Uhrzeigersinn auf dem Bildschirm).</summary>
    private static Point[][] Gedreht(IReadOnlyList<Point[]> zuege, double grad, double mx, double my)
    {
        var w = grad * Math.PI / 180;
        var (c, s) = (Math.Cos(w), Math.Sin(w));
        return [.. zuege.Select(z => z.Select(p => new Point(mx + (p.X - mx) * c - (p.Y - my) * s, my + (p.X - mx) * s + (p.Y - my) * c)).ToArray())];
    }
}
