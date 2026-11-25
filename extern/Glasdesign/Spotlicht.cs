// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Das Spotlicht (Stilfamilie 1, KONZEPT.md Abschnitt 5a, Sarevocs "mach bitte" vom 08.10.2026): Die Haarlinie leuchtet dort
/// auf, wo die Maus ist, innen nur ein Hauch; nach außen fällt das Licht weich auf null. Es liegt in den Vorlagen über dem
/// Körper und unter dem Inhalt, hat dieselbe Form wie er und folgt der Maus über dem Steuerelement, zu dem die Vorlage
/// gehört (TemplatedParent). Kommt die Maus, blendet es ein, geht sie, blendet es aus. Ohne Maus (Tastatur) gibt es kein
/// Spotlicht; die Vorschau "überfahren" zeigt es oben rechts. Stärken und Radius stehen im <see cref="Glanzprofil"/>,
/// der Ton unter <see cref="Schluessel.SpotlichtFarbe"/>. Gezeichnet mit einem radialen Pinsel auf der Haarlinie
/// (ein Gerätepixel) und auf der Fläche darin, keine Effekte, keine Zwischenebene.
/// </summary>
public sealed class Spotlicht : FrameworkElement
{
    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(Glasform), typeof(Spotlicht), new FrameworkPropertyMetadata(Glasform.Pille, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eine beliebige Form (Abschnitt 6); gesetzt, gilt sie statt <see cref="Form"/>.</summary>
    public static readonly DependencyProperty UmrissProperty = DependencyProperty.Register(
        nameof(Umriss), typeof(Umriss), typeof(Spotlicht), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Umriss? Umriss
    {
        get => (Umriss?)GetValue(UmrissProperty);
        set => SetValue(UmrissProperty, value);
    }

    /// <summary>Eckenradius in DIP; NaN (Vorgabe) nimmt die Rundung von <see cref="Form"/>.</summary>
    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(double), typeof(Spotlicht), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Wo das Licht steht, in DIP dieses Elements.</summary>
    public static readonly DependencyProperty StelleProperty = DependencyProperty.Register(
        nameof(Stelle), typeof(Point), typeof(Spotlicht), new FrameworkPropertyMetadata(new Point(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Ob das Licht an ist (die Maus ist da); die Stärke blendet dorthin.</summary>
    public static readonly DependencyProperty AktivProperty = DependencyProperty.Register(
        nameof(Aktiv), typeof(bool), typeof(Spotlicht), new FrameworkPropertyMetadata(false, (d, _) => ((Spotlicht)d).Blenden()));

    /// <summary>Vorschau ohne Maus: das Licht oben rechts (Musterblatt, Glint, Tests).</summary>
    public static readonly DependencyProperty VorschauProperty = DependencyProperty.Register(
        nameof(Vorschau), typeof(bool), typeof(Spotlicht), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Spotlicht)d).Blenden()));

    /// <summary>Anteil der Werte aus dem Glanzprofil (Matt nimmt weniger).</summary>
    public static readonly DependencyProperty FaktorProperty = DependencyProperty.Register(
        nameof(Faktor), typeof(double), typeof(Spotlicht), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Color), typeof(Spotlicht), new FrameworkPropertyMetadata(Color.FromRgb(0xBF, 0xF4, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Spotlicht),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gezeichnete Stärke von 0 bis 1; blendet weich, wenn die Maus kommt oder geht.</summary>
    public static readonly DependencyProperty StaerkeProperty = DependencyProperty.Register(
        nameof(Staerke), typeof(double), typeof(Spotlicht), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private UIElement? _quelle;
    private (Glasflaeche Flaeche, Geometry Innen, Geometry Rand)? _formen;

    static Spotlicht()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Spotlicht), new UIPropertyMetadata(true, (d, _) => ((Spotlicht)d).Blenden()));
    }

    public Spotlicht()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SetResourceReference(FarbeProperty, Schluessel.SpotlichtFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
        Loaded += (_, _) => Verbinden();
        Unloaded += (_, _) => Trennen();
    }

    public Glasform Form { get => (Glasform)GetValue(FormProperty); set => SetValue(FormProperty, value); }
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }
    public Point Stelle { get => (Point)GetValue(StelleProperty); set => SetValue(StelleProperty, value); }
    public bool Aktiv { get => (bool)GetValue(AktivProperty); set => SetValue(AktivProperty, value); }
    public bool Vorschau { get => (bool)GetValue(VorschauProperty); set => SetValue(VorschauProperty, value); }
    public double Faktor { get => (double)GetValue(FaktorProperty); set => SetValue(FaktorProperty, value); }
    public Color Farbe { get => (Color)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public double Staerke { get => (double)GetValue(StaerkeProperty); set => SetValue(StaerkeProperty, value); }

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    /// <summary>Folgt der Maus über dem Steuerelement der Vorlage (oder dem nächsten Elternteil).</summary>
    private void Verbinden()
    {
        Trennen();
        _quelle = TemplatedParent as UIElement ?? VisualTreeHelper.GetParent(this) as UIElement;
        if (_quelle is null) return;
        _quelle.MouseMove += Bewegt;
        _quelle.MouseLeave += Verlassen;
    }

    private void Trennen()
    {
        if (_quelle is null) return;
        _quelle.MouseMove -= Bewegt;
        _quelle.MouseLeave -= Verlassen;
        _quelle = null;
    }

    private void Bewegt(object sender, MouseEventArgs e)
    {
        Stelle = e.GetPosition(this);
        Aktiv = true;
    }

    private void Verlassen(object sender, MouseEventArgs e) => Aktiv = false;

    private void Blenden() => Saumblende.Setzen(this, StaerkeProperty, (Aktiv || Vorschau) && IsEnabled ? 1 : 0, Glanzprofil.SpotlichtEinblenden);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var p = Glanzprofil;
        var staerke = Staerke * Faktor;
        if (staerke <= 0 || p.SpotlichtRadius <= 0 || (p.SpotlichtRand <= 0 && p.SpotlichtInnen <= 0)) return;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 4 || h < 4) return;
        var kurz = Math.Min(b, h);
        var r = !double.IsNaN(Ecke) ? Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), kurz / 2)
            : Form == Glasform.Pille ? kurz / 2 : Glasgeometrie.Raster(kurz * Glasgeometrie.Rundungsanteil, 1);
        var f = Umriss is { } u ? new Glasflaeche(0, 0, b, h, u.Eckradius(b, h, s), s) { Umriss = u } : new Glasflaeche(0, 0, b, h, r, s);
        var stelle = Vorschau && !Aktiv ? new Point(RenderSize.Width * 0.72, 0) : Stelle;
        var rand = Licht(stelle, p.SpotlichtRadius, Math.Clamp(p.SpotlichtRand * staerke, 0, 1));
        var innen = Licht(stelle, p.SpotlichtRadius * 1.15, Math.Clamp(p.SpotlichtInnen * staerke, 0, 1));
        // Die Formen hängen nur an der Fläche; beim Bewegen der Maus wechseln allein die Pinsel.
        if (_formen?.Flaeche != f) _formen = (f, f.Form(Glasgeometrie.Haarlinie), f.Ring(0, Glasgeometrie.Haarlinie));
        if (innen is not null) drawingContext.DrawGeometry(innen, null, _formen.Value.Innen);
        if (rand is not null) drawingContext.DrawGeometry(rand, null, _formen.Value.Rand);
    }

    private RadialGradientBrush? Licht(Point mitte, double radius, double deckung)
    {
        if (deckung <= 0) return null;
        var b = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, Center = mitte, GradientOrigin = mitte, RadiusX = radius, RadiusY = radius,
            GradientStops =
            {
                new GradientStop(Farbton.Deckend(Farbe, deckung * Farbe.A / 255.0), 0),
                new GradientStop(Farbton.Deckend(Farbe, deckung * 0.35 * Farbe.A / 255.0), 0.5),
                new GradientStop(Farbton.Deckend(Farbe, 0), 1),
            },
        };
        b.Freeze();
        return b;
    }
}
