// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;

namespace Funk.Verwaltung.Ansichten;

/// <summary>
/// Zwei Spalten der Verwaltung (Liste links, Karte rechts), die in schmalen Fenstern untereinander rutschen (seit 0.11.1 AP4, Sarevocs Bild vom
/// 12.10.2026: Bei 760 Breite ragte die Karte rechts aus dem Bild, und links war die Überschrift beschnitten). Bis dahin ein Grid mit
/// MinWidth je Spalte: Reichte der Platz nicht, hielt es beide Mindestbreiten und lief rechts über den Rand. Die ersten zwei Kinder sind die
/// Spalten; ab <see cref="Ab"/> angebotener Breite stehen sie nebeneinander (die erste <see cref="Anteil"/> der Breite, nie unter
/// <see cref="ErsteMin"/> und nie über <see cref="ErsteMax"/>, die zweite den Rest nach der <see cref="Luecke"/>), schmaler untereinander über die
/// ganze Breite; weitere Kinder stehen immer darunter. Ein eingeklapptes zweites Kind lässt der ersten Spalte ihre Breite, wie das Grid vorher.
/// <para>
/// Reine Rechnung in Measure und Arrange, ohne Ressource und ohne Verweis auf den Client (der Verwaltungsteil kennt dessen
/// <c>Spaltenwechsel</c> nicht). <see cref="ArrangeOverride"/> misst nie neu (CODEMAP, Fallen: ein Element, das beim Anordnen neu misst,
/// lässt das Layout im Kreis laufen).
/// </para>
/// </summary>
public sealed class Zweispalter : Panel
{
    public static readonly DependencyProperty AbProperty = DependencyProperty.Register(nameof(Ab), typeof(double), typeof(Zweispalter),
        new FrameworkPropertyMetadata(600.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty AnteilProperty = DependencyProperty.Register(nameof(Anteil), typeof(double), typeof(Zweispalter),
        new FrameworkPropertyMetadata(0.4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ErsteMinProperty = DependencyProperty.Register(nameof(ErsteMin), typeof(double), typeof(Zweispalter),
        new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ErsteMaxProperty = DependencyProperty.Register(nameof(ErsteMax), typeof(double), typeof(Zweispalter),
        new FrameworkPropertyMetadata(420.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LueckeProperty = DependencyProperty.Register(nameof(Luecke), typeof(double), typeof(Zweispalter),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Ab dieser angebotenen Breite stehen die zwei Spalten nebeneinander; schmaler untereinander.</summary>
    public double Ab
    {
        get => (double)GetValue(AbProperty);
        set => SetValue(AbProperty, value);
    }

    /// <summary>Anteil der Breite für die erste Spalte (0,4 heißt zwei Fünftel), begrenzt durch <see cref="ErsteMin"/> und <see cref="ErsteMax"/>.</summary>
    public double Anteil
    {
        get => (double)GetValue(AnteilProperty);
        set => SetValue(AnteilProperty, value);
    }

    /// <summary>Die erste Spalte nie schmaler als dies, solange die Spalten nebeneinander stehen.</summary>
    public double ErsteMin
    {
        get => (double)GetValue(ErsteMinProperty);
        set => SetValue(ErsteMinProperty, value);
    }

    /// <summary>Die erste Spalte nie breiter als dies.</summary>
    public double ErsteMax
    {
        get => (double)GetValue(ErsteMaxProperty);
        set => SetValue(ErsteMaxProperty, value);
    }

    /// <summary>Abstand zwischen den Spalten, wenn sie nebeneinander stehen.</summary>
    public double Luecke
    {
        get => (double)GetValue(LueckeProperty);
        set => SetValue(LueckeProperty, value);
    }

    /// <summary>Beim letzten Messen standen die Spalten nebeneinander (Tests).</summary>
    public bool Nebeneinander { get; private set; }

    /// <summary>Die Breiten der zwei Spalten in <paramref name="breite"/>, nie negativ; die erste nach Anteil, Min und Max, die zweite der Rest.</summary>
    public static (double Erste, double Zweite) Spalten(double breite, double anteil, double ersteMin, double ersteMax, double luecke)
    {
        var rest = Math.Max(0, breite - Math.Max(0, luecke));
        var min = double.IsFinite(ersteMin) ? Math.Max(0, ersteMin) : 0;
        var max = double.IsFinite(ersteMax) ? Math.Max(min, ersteMax) : double.PositiveInfinity;
        var erste = Math.Clamp(Math.Clamp(double.IsFinite(anteil) ? breite * anteil : 0, min, max), 0, rest);
        return (erste, rest - erste);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var breite = availableSize.Width;
        var unendlich = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var kinder = InternalChildren;
        // Ohne Breitenvorgabe (frei gemessen) nebeneinander, sonst nach der Grenze.
        Nebeneinander = kinder.Count >= 2 && (double.IsInfinity(breite) || breite >= Ab);
        double gesamtBreite = 0, hoehe = 0;
        var start = 0;
        if (Nebeneinander)
        {
            var a = kinder[0];
            var b = kinder[1];
            if (double.IsInfinity(breite))
            {
                a.Measure(unendlich);
                b.Measure(unendlich);
                gesamtBreite = Math.Max(ErsteMin, a.DesiredSize.Width) + Luecke + b.DesiredSize.Width;
            }
            else
            {
                var (erste, zweite) = Spalten(breite, Anteil, ErsteMin, ErsteMax, Luecke);
                a.Measure(new Size(erste, double.PositiveInfinity));
                b.Measure(new Size(zweite, double.PositiveInfinity));
                gesamtBreite = breite;
            }
            hoehe = Math.Max(a.DesiredSize.Height, b.DesiredSize.Height);
            start = 2;
        }
        for (var i = start; i < kinder.Count; i++)
        {
            var k = kinder[i];
            k.Measure(new Size(breite, double.PositiveInfinity));
            hoehe += k.DesiredSize.Height;
            gesamtBreite = Math.Max(gesamtBreite, k.DesiredSize.Width);
        }
        if (double.IsFinite(breite)) gesamtBreite = Math.Min(gesamtBreite, breite);
        return new Size(gesamtBreite, hoehe);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var breite = finalSize.Width;
        var kinder = InternalChildren;
        double y = 0;
        var start = 0;
        if (Nebeneinander && kinder.Count >= 2)
        {
            var (erste, zweite) = Spalten(breite, Anteil, ErsteMin, ErsteMax, Luecke);
            var zeile = Math.Max(kinder[0].DesiredSize.Height, kinder[1].DesiredSize.Height);
            kinder[0].Arrange(new Rect(0, 0, erste, zeile));
            kinder[1].Arrange(new Rect(breite - zweite, 0, zweite, zeile));
            y = zeile;
            start = 2;
        }
        for (var i = start; i < kinder.Count; i++)
        {
            var k = kinder[i];
            k.Arrange(new Rect(0, y, breite, k.DesiredSize.Height));
            y += k.DesiredSize.Height;
        }
        return finalSize;
    }
}
