// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Symbol für Glas.Knopf.Rund und Glas.Knopf.Eckig (Plus, Pfeile, Kreuz, Haken, Minus), gezeichnet von
/// <see cref="Zeichner"/>: absolut mittig in den eigenen Grenzen, auf ganzen Gerätepixeln. Die Vorlagen der beiden Stile
/// strecken den Inhalt über den ganzen Knopf, also sitzt das Symbol mittig im Glaskörper. Farbe ist die Schriftfarbe des
/// Knopfs (TextElement.Foreground, silbriger Verlauf, gesperrt gedämpft).
/// </summary>
public sealed class Glassymbol : FrameworkElement
{
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(Glaszeichen), typeof(Glassymbol), new FrameworkPropertyMetadata(Glaszeichen.Plus, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AnteilProperty = DependencyProperty.Register(
        nameof(Anteil), typeof(double), typeof(Glassymbol), new FrameworkPropertyMetadata(0.42, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrichAnteilProperty = DependencyProperty.Register(
        nameof(StrichAnteil), typeof(double), typeof(Glassymbol), new FrameworkPropertyMetadata(Zeichner.StrichAnteil, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Ein eigenes Symbol als Pfad (Abschnitt 6), gefüllt mit der Schriftfarbe; gesetzt, gilt es statt <see cref="Symbol"/>.
    /// In XAML als Pfadtext: <c>Pfad="M0,0 L1,0.5 L0,1 Z"</c>. Größe wie bei den Zeichen über <see cref="Anteil"/>.
    /// </summary>
    public static readonly DependencyProperty PfadProperty = DependencyProperty.Register(
        nameof(Pfad), typeof(Geometry), typeof(Glassymbol), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Glassymbol), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Größe, wenn der Behälter keine vorgibt (DIP).</summary>
    public const double Grundgroesse = 16;

    public Glaszeichen Symbol { get => (Glaszeichen)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }

    /// <summary>Größe des Symbols als Anteil der kürzeren Seite (Vorlage: Plus 39 bei 90).</summary>
    public double Anteil { get => (double)GetValue(AnteilProperty); set => SetValue(AnteilProperty, value); }

    /// <summary>Strichbreite als Anteil der kürzeren Seite (Vorlage: 7 bei 90).</summary>
    public double StrichAnteil { get => (double)GetValue(StrichAnteilProperty); set => SetValue(StrichAnteilProperty, value); }

    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Geometry? Pfad { get => (Geometry?)GetValue(PfadProperty); set => SetValue(PfadProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(Grundgroesse, availableSize.Width), Math.Min(Grundgroesse, availableSize.Height));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var behaelter = new Rect(0, 0, Glasgeometrie.Raster(RenderSize.Width, s), Glasgeometrie.Raster(RenderSize.Height, s));
        if (Pfad is { } pfad) Zeichner.Pfad(drawingContext, pfad, behaelter, Anteil, Foreground, s);
        else Zeichner.Zeichnen(drawingContext, Symbol, behaelter, Anteil, Foreground, s, StrichAnteil);
    }
}
