// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Feiner heller Rahmen für den Tastaturfokus: eine Linie von einem Gerätepixel (bei höherer Skalierung entsprechend mehr),
/// mit zwei DIP Luft außerhalb der eigenen Grenzen, Rundung wie der Glaskörper. Steht in den FocusVisualStyles
/// Glas.Fokus.Pille und Glas.Fokus.Eckig (echter Tastaturfokus, nur bei Tastaturbedienung) und, für die Vorschau, in den
/// Vorlagen selbst.
/// </summary>
public sealed class Fokusring : FrameworkElement
{
    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(Glasform), typeof(Fokusring), new FrameworkPropertyMetadata(Glasform.Pille, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PinselProperty = DependencyProperty.Register(
        nameof(Pinsel), typeof(Brush), typeof(Fokusring), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Luft zwischen Element und Rahmen in DIP.</summary>
    public const double Luft = 2;

    public Fokusring()
    {
        IsHitTestVisible = false;
        SetResourceReference(PinselProperty, Schluessel.Fokus);
    }

    public Glasform Form { get => (Glasform)GetValue(FormProperty); set => SetValue(FormProperty, value); }
    public Brush? Pinsel { get => (Brush?)GetValue(PinselProperty); set => SetValue(PinselProperty, value); }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Pinsel is null || RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        var luft = Glasgeometrie.Linie(Luft, s);
        var t = Glasgeometrie.Linie(1, s);
        var r0 = Form == Glasform.Pille ? Math.Min(b, h) / 2 : Glasgeometrie.Raster(Math.Min(b, h) * Glasgeometrie.Rundungsanteil, 1);
        var aussen = luft + t / 2;
        var rechteck = new Rect(-aussen / s, -aussen / s, (b + 2 * aussen) / s, (h + 2 * aussen) / s);
        var radius = (r0 + aussen) / s;
        var stift = new Pen(Pinsel, t / s);
        if (stift.CanFreeze) stift.Freeze();
        drawingContext.DrawRoundedRectangle(null, stift, rechteck, radius, radius);
    }
}
