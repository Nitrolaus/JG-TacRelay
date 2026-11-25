// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Dunkler Kontaktschatten unter einem Glaskörper, Glas 3: eine flache Ellipse knapp unter der Unterkante (Mitte bei 90 %
/// der Höhe, 72 % breit, 40 % hoch), radialer Verlauf, Mitte am dunkelsten. Gezeichnet über die eigenen Grenzen hinaus,
/// also ohne Einfluss auf das Layout; nicht treffbar (IsHitTestVisible aus), damit die Maus über dem Schatten nicht als
/// "überfahren" gilt. Gedrückt kleiner und blasser, gesperrt und bei Schatten Aus keiner. Die Deckung steckt im Pinsel,
/// nicht in PushOpacity (keine Zwischenebene je Knopf).
/// </summary>
public sealed class Kontaktschatten : FrameworkElement
{
    public static readonly DependencyProperty ZustandProperty = DependencyProperty.Register(
        nameof(Zustand), typeof(Glaszustand), typeof(Kontaktschatten), new FrameworkPropertyMetadata(Glaszustand.Normal, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Kontaktschatten), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PinselProperty = DependencyProperty.Register(
        nameof(Pinsel), typeof(Brush), typeof(Kontaktschatten), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BauplanProperty = DependencyProperty.Register(
        nameof(Bauplan), typeof(Bauplan), typeof(Kontaktschatten),
        new FrameworkPropertyMetadata(Glasdesign.Bauplan.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    public Kontaktschatten()
    {
        IsHitTestVisible = false;
        SetResourceReference(PinselProperty, Schluessel.Kontaktschatten);
        SetResourceReference(BauplanProperty, Schluessel.Bauplan);
    }

    /// <summary>Der Bauplan der Farbstellung (Lage, Größe, Deckung); fehlt er, gilt <see cref="Glasdesign.Bauplan.Standard"/>.</summary>
    public Bauplan Bauplan
    {
        get => GetValue(BauplanProperty) as Bauplan ?? Glasdesign.Bauplan.Standard;
        set => SetValue(BauplanProperty, value);
    }

    public Glaszustand Zustand { get => (Glaszustand)GetValue(ZustandProperty); set => SetValue(ZustandProperty, value); }
    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }
    public Brush? Pinsel { get => (Brush?)GetValue(PinselProperty); set => SetValue(PinselProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Schatten == Schattenstufe.Aus || Zustand == Glaszustand.Gesperrt || Pinsel is null) return;
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        if (w <= 0 || h <= 0) return;
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        var b = Bauplan;
        var (my, bw, bh, deckung) = gedrueckt
            ? (b.KontaktLageGedrueckt, b.KontaktBreiteGedrueckt, b.KontaktHoeheGedrueckt, b.KontaktDeckungGedrueckt)
            : (b.KontaktLage, b.KontaktBreite, b.KontaktHoehe, b.KontaktDeckung);
        drawingContext.DrawEllipse(Pinselwerk.Gedaempft(Pinsel, deckung), null, new Point(w / 2, h * my), w * bw / 2, h * bh / 2);
    }
}
