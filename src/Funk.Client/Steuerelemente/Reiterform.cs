// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Die Form eines Reiters auf der Raid-Seite (0.11.3, Sarevoc 13.10.2026: "wenn nun die Register noch abgerundete Ecken bekommen und sich sichtbar
/// wie echte Register überlappen"): ein Trapez mit schrägen Seiten, oben abgerundet, unten zum Boden hin weich ausgestellt wie ein
/// Karteireiter. Gezeichnet in echten Bildpunkten nach der Größe des Elements, damit die Rundung bei jeder Breite gleich bleibt (ein gestrecktes
/// Path würde sie verzerren). Die Fläche deckt, was darunter liegt; so sieht man, wie ein Reiter über dem Nachbarn liegt.
/// </summary>
public sealed class Reiterform : FrameworkElement
{
    /// <summary>Wie weit die schräge Seite von unten nach oben einrückt.</summary>
    public const double Schraege = 12;

    /// <summary>Halbmesser der oberen Ecken und des Fußes unten.</summary>
    public const double Rundung = 5;

    public static readonly DependencyProperty FuellungProperty = DependencyProperty.Register(nameof(Fuellung), typeof(Brush), typeof(Reiterform),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RandProperty = DependencyProperty.Register(nameof(Rand), typeof(Brush), typeof(Reiterform),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fuellung
    {
        get => (Brush?)GetValue(FuellungProperty);
        set => SetValue(FuellungProperty, value);
    }

    public Brush? Rand
    {
        get => (Brush?)GetValue(RandProperty);
        set => SetValue(RandProperty, value);
    }

    /// <summary>Der Umriss für eine Breite und Höhe: unten offen, damit der offene Reiter ohne Linie in die Fläche darunter übergeht.</summary>
    internal static StreamGeometry Umriss(double breite, double hoehe, bool geschlossen)
    {
        var g = new StreamGeometry();
        const double fuss = Rundung;
        if (breite <= 2 * (fuss + Schraege + Rundung) || hoehe <= 2 * Rundung) return g;
        // Einheitsrichtung der linken Schräge von unten nach oben, für die Punkte vor und nach jeder Rundung.
        var laenge = Math.Sqrt(Schraege * Schraege + hoehe * hoehe);
        var dx = Schraege / laenge;
        var dy = hoehe / laenge;
        using (var z = g.Open())
        {
            // Fuß links: von der Grundlinie weich in die Schräge (sie läuft von (fuss, unten) nach (fuss + Schraege, oben)).
            z.BeginFigure(new Point(0, hoehe), geschlossen, geschlossen);
            z.QuadraticBezierTo(new Point(fuss, hoehe), new Point(fuss + Rundung * dx, hoehe - Rundung * dy), true, true);
            // Schräge hoch bis kurz vor die Ecke, Ecke rund, oben entlang.
            z.LineTo(new Point(fuss + Schraege - Rundung * dx, Rundung * dy), true, true);
            z.QuadraticBezierTo(new Point(fuss + Schraege, 0), new Point(fuss + Schraege + Rundung, 0), true, true);
            z.LineTo(new Point(breite - fuss - Schraege - Rundung, 0), true, true);
            z.QuadraticBezierTo(new Point(breite - fuss - Schraege, 0), new Point(breite - fuss - Schraege + Rundung * dx, Rundung * dy), true, true);
            // Schräge hinunter, Fuß rechts.
            z.LineTo(new Point(breite - fuss - Rundung * dx, hoehe - Rundung * dy), true, true);
            z.QuadraticBezierTo(new Point(breite - fuss, hoehe), new Point(breite, hoehe), true, true);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var w = ActualWidth;
        var h = ActualHeight;
        if (Fuellung is { } f) drawingContext.DrawGeometry(f, null, Umriss(w, h, true));
        if (Rand is { } r) drawingContext.DrawGeometry(null, new Pen(r, 1), Verschoben(Umriss(w - 1, h, false)));
    }

    /// <summary>Den Rand um einen halben Bildpunkt nach innen, damit die Linie scharf auf den Bildpunkten liegt.</summary>
    private static Geometry Verschoben(Geometry g)
    {
        var k = g.Clone();
        k.Transform = new TranslateTransform(0.5, 0.5);
        k.Freeze();
        return k;
    }
}
