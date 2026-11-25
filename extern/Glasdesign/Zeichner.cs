// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>Box eines Zeichens in Gerätepixeln und seine Strichbreite (Pixel).</summary>
public readonly record struct Zeichenmass(Rect Box, double Strich);

/// <summary>
/// Zeichnet Haken, Kreuz, Plus, Pfeile, Striche und die Fensterzeichen Maximieren und Wiederherstellen absolut mittig (Sarevoc, 02.10.2026: das Plus in muster4 saß gefühlt nicht
/// mittig). Ausgerichtet wird nach der Geometrie, nicht nach den Strichkappen: Jedes Zeichen bekommt eine Box in ganzen
/// Gerätepixeln, deren Rand zum Behälter links gleich rechts und oben gleich unten ist (dafür hat die Box dieselbe
/// Geradzahligkeit wie der Behälter). Die Mittellinien liegen um die halbe Strichbreite eingerückt, und alle Enden und
/// Ecken sind rund; so reicht die Strichbegrenzung an jeder Seite genau bis an die Box, auch bei schiefen Zeichen wie
/// Haken und Pfeil. Alle Werte in Pixeln; erst beim Zeichnen wird durch die Skalierung geteilt.
/// </summary>
public static partial class Zeichner
{
    /// <summary>Strichbreite als Anteil der kürzeren Behälterseite, wenn nichts anderes gesagt ist.</summary>
    public const double StrichAnteil = 0.085;

    /// <summary>Breite und Höhe der Box je Zeichen, bezogen auf die Grundgröße.</summary>
    private static (double Breite, double Hoehe) Verhaeltnis(Glaszeichen z) => z switch
    {
        Glaszeichen.Haken => (1, 0.72),
        Glaszeichen.PfeilHoch or Glaszeichen.PfeilRunter => (0.84, 1),
        Glaszeichen.PfeilLinks or Glaszeichen.PfeilRechts => (1, 0.84),
        Glaszeichen.Winkel => (1, 0.56),
        _ => VerhaeltnisStufe2(z),
    };

    /// <summary>
    /// Box für <paramref name="zeichen"/> mittig in <paramref name="behaelterPx"/> (ganze Pixel erwartet). Die Grundgröße ist
    /// <paramref name="anteil"/> mal die kürzere Seite, die Strichbreite <paramref name="strichAnteil"/> mal die kürzere Seite.
    /// </summary>
    public static Zeichenmass Mass(Glaszeichen zeichen, Rect behaelterPx, double anteil, double strichAnteil = StrichAnteil)
    {
        var bc = (int)Math.Round(behaelterPx.Width);
        var hc = (int)Math.Round(behaelterPx.Height);
        var kurz = Math.Min(bc, hc);
        var basis = kurz * anteil;
        var t = Math.Max(1, (int)Math.Round(kurz * strichAnteil, MidpointRounding.AwayFromZero));
        var (vb, vh) = Verhaeltnis(zeichen);
        var gb = Math.Max(t, (int)Math.Round(basis * vb, MidpointRounding.AwayFromZero));
        int gh;
        if (zeichen is Glaszeichen.Strich or Glaszeichen.Minus)
        {
            if ((hc - t) % 2 != 0) t++;
            gh = t;
        }
        else
        {
            gh = Math.Max(t, (int)Math.Round(basis * vh, MidpointRounding.AwayFromZero));
        }
        if ((bc - gb) % 2 != 0) gb = gb > t ? gb - 1 : gb + 1;
        if ((hc - gh) % 2 != 0) gh = gh > t ? gh - 1 : gh + 1;
        var x = behaelterPx.X + (bc - gb) / 2;
        var y = behaelterPx.Y + (hc - gh) / 2;
        return new Zeichenmass(new Rect(x, y, gb, gh), t);
    }

    /// <summary>Die Mittellinien des Zeichens (Pixel) als Linienzüge.</summary>
    public static IReadOnlyList<Point[]> Zuege(Glaszeichen zeichen, Zeichenmass mass)
    {
        var r = mass.Strich / 2;
        var b = mass.Box;
        double x0 = b.Left + r, x1 = b.Right - r, y0 = b.Top + r, y1 = b.Bottom - r;
        double cx = (b.Left + b.Right) / 2, cy = (b.Top + b.Bottom) / 2;
        switch (zeichen)
        {
            case Glaszeichen.Plus:
                return [[new(x0, cy), new(x1, cy)], [new(cx, y0), new(cx, y1)]];
            case Glaszeichen.Minus:
            case Glaszeichen.Strich:
                return [[new(x0, cy), new(x1, cy)]];
            case Glaszeichen.Kreuz:
                return [[new(x0, y0), new(x1, y1)], [new(x1, y0), new(x0, y1)]];
            case Glaszeichen.Haken:
                return [[new(x0, y0 + 0.45 * (y1 - y0)), new(x0 + 0.36 * (x1 - x0), y1), new(x1, y0)]];
            case Glaszeichen.PfeilHoch:
            {
                var d = (x1 - x0) / 2;
                return [[new(x0, y0 + d), new(cx, y0), new(x1, y0 + d)], [new(cx, y0), new(cx, y1)]];
            }
            case Glaszeichen.PfeilRunter:
            {
                var d = (x1 - x0) / 2;
                return [[new(x0, y1 - d), new(cx, y1), new(x1, y1 - d)], [new(cx, y1), new(cx, y0)]];
            }
            case Glaszeichen.PfeilLinks:
            {
                var d = (y1 - y0) / 2;
                return [[new(x0 + d, y0), new(x0, cy), new(x0 + d, y1)], [new(x0, cy), new(x1, cy)]];
            }
            case Glaszeichen.PfeilRechts:
            {
                var d = (y1 - y0) / 2;
                return [[new(x1 - d, y0), new(x1, cy), new(x1 - d, y1)], [new(x1, cy), new(x0, cy)]];
            }
            case Glaszeichen.Maximieren:
                return [[new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1), new(x0, y0)]];
            case Glaszeichen.Winkel:
                return [[new(x0, y0), new(cx, y1), new(x1, y0)]];
            case Glaszeichen.Lupe:
            {
                // Kreis oben links (Durchmesser 70 Prozent der Box), Griff unter 45 Grad bis in die Ecke unten rechts.
                var rk = (x1 - x0) * 0.35;
                var mx = x0 + rk;
                var my = y0 + rk;
                var kreis = new Point[33];
                for (var i = 0; i <= 32; i++)
                {
                    var w = i * Math.PI / 16;
                    kreis[i] = new Point(mx + rk * Math.Cos(w), my + rk * Math.Sin(w));
                }
                var a = rk * Math.Sqrt(0.5);
                return [kreis, [new(mx + a, my + a), new(x1, y1)]];
            }
            case Glaszeichen.Wiederherstellen:
            {
                // Vorn ein Quadrat unten links, dahinter oben rechts ein zweites, von dem nur die Ober- und die rechte Kante
                // zu sehen sind. Der Versatz ist ganzzahlig (die Linien bleiben auf ganzen Pixeln) und lässt zwischen den
                // beiden Oberkanten mindestens ein Pixel Luft.
                var strich = mass.Strich;
                var v = Math.Max(strich + Math.Max(1, Math.Round(strich / 2)), Math.Round((x1 - x0) * 0.32));
                return
                [
                    [new(x0, y0 + v), new(x1 - v, y0 + v), new(x1 - v, y1), new(x0, y1), new(x0, y0 + v)],
                    [new(x0 + v, y0 + v), new(x0 + v, y0), new(x1, y0), new(x1, y1 - v), new(x1 - v, y1 - v)],
                ];
            }
            default:
                return ZuegeStufe2(zeichen, x0, y0, x1, y1);
        }
    }

    /// <summary>
    /// Ein eigener Pfad als Symbol, gefüllt: in ein Feld der Größe <paramref name="anteil"/> mal kürzere Seite gesetzt, das
    /// absolut mittig im Behälter liegt (Rest links und rechts, oben und unten gleich, auf ganzen Pixeln), seitentreu
    /// eingepasst und darin mittig.
    /// </summary>
    public static void Pfad(DrawingContext dc, Geometry pfad, Rect behaelterPx, double anteil, Brush? pinsel, double skala)
    {
        ArgumentNullException.ThrowIfNull(dc);
        ArgumentNullException.ThrowIfNull(pfad);
        var grenzen = pfad.Bounds;
        if (pinsel is null || grenzen.IsEmpty || behaelterPx.Width < 2 || behaelterPx.Height < 2) return;
        var g = Math.Max(2, Math.Round(Math.Min(behaelterPx.Width, behaelterPx.Height) * anteil));
        // Gleiche Parität wie der Behälter, sonst bliebe links und rechts (oben und unten) ein halbes Pixel Unterschied.
        var gx = g + (behaelterPx.Width - g) % 2;
        var gy = g + (behaelterPx.Height - g) % 2;
        var feld = new Rect(behaelterPx.X + (behaelterPx.Width - gx) / 2, behaelterPx.Y + (behaelterPx.Height - gy) / 2, gx, gy);
        var f = Math.Min(gx / Math.Max(1e-9, grenzen.Width), gy / Math.Max(1e-9, grenzen.Height));
        var x = feld.X + (gx - grenzen.Width * f) / 2;
        var y = feld.Y + (gy - grenzen.Height * f) / 2;
        var gesetzt = pfad.Clone();
        gesetzt.Transform = new MatrixTransform(f / skala, 0, 0, f / skala, (x - grenzen.X * f) / skala, (y - grenzen.Y * f) / skala);
        gesetzt.Freeze();
        dc.DrawGeometry(pinsel, null, gesetzt);
    }

    /// <summary>Zeichnet das Zeichen in <paramref name="behaelterPx"/> (Pixel des Elements) mit <paramref name="pinsel"/>.</summary>
    public static void Zeichnen(DrawingContext dc, Glaszeichen zeichen, Rect behaelterPx, double anteil, Brush? pinsel, double skala, double strichAnteil = StrichAnteil)
    {
        ArgumentNullException.ThrowIfNull(dc);
        if (zeichen == Glaszeichen.Keins || pinsel is null || behaelterPx.Width < 2 || behaelterPx.Height < 2) return;
        var mass = Mass(zeichen, behaelterPx, anteil, strichAnteil);
        var geometrie = new StreamGeometry();
        using (var g = geometrie.Open())
        {
            foreach (var zug in Zuege(zeichen, mass))
            {
                g.BeginFigure(new Point(zug[0].X / skala, zug[0].Y / skala), false, false);
                for (var i = 1; i < zug.Length; i++) g.LineTo(new Point(zug[i].X / skala, zug[i].Y / skala), true, true);
            }
        }
        geometrie.Freeze();
        var stift = new Pen(pinsel, mass.Strich / skala)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        if (stift.CanFreeze) stift.Freeze();
        dc.DrawGeometry(null, stift, geometrie);
    }
}
