// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Funk.Verwaltung;

/// <summary>
/// Zeichnet einen <see cref="QrCode"/> als Vektor: dunkle Module als Rechtecke (je Zeile zu Läufen zusammengefasst), mit
/// der Ruhezone von vier Modulen ringsum. Eine Einheit ist ein Modul; die Ansicht skaliert. Immer dunkel auf hell, wie
/// Kameras es erwarten, auch in dunklen Farbstellungen.
/// </summary>
public static class QrBild
{
    /// <summary>Breite der Ruhezone in Modulen (Standard: vier).</summary>
    public const int Ruhezone = 4;

    public static Geometry Geometrie(QrCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var z = g.Open())
        {
            for (var y = 0; y < code.Groesse; y++)
            {
                var x = 0;
                while (x < code.Groesse)
                {
                    if (!code.Modul(x, y))
                    {
                        x++;
                        continue;
                    }
                    var start = x;
                    while (x < code.Groesse && code.Modul(x, y)) x++;
                    Rechteck(z, start + Ruhezone, y + Ruhezone, x - start, 1);
                }
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>Seitenlänge samt Ruhezone in Modulen.</summary>
    public static int Seite(QrCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Groesse + (2 * Ruhezone);
    }

    private static void Rechteck(StreamGeometryContext z, double x, double y, double breite, double hoehe)
    {
        z.BeginFigure(new Point(x, y), isFilled: true, isClosed: true);
        z.PolyLineTo([new Point(x + breite, y), new Point(x + breite, y + hoehe), new Point(x, y + hoehe)], isStroked: false, isSmoothJoin: false);
    }
}
