// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>Wessen Schatten ein <see cref="Rundschatten"/> nachbildet: den des Glaskörpers (Glas 4) oder einer Taktil-Ebene.</summary>
public enum Rundschattenart
{
    Glas,
    Taktil,
}

/// <summary>
/// Der weiche Umgebungsschatten eines runden Knopfs als radialer Verlauf statt als Unschärfeeffekt (seit 0.5.0-beta.9, für
/// die runden Knöpfe in Reihe: Seitenzahlen, Schritte). Ein DropShadowEffect rechnet im Speicher (Software) bei jedem Bild
/// neu; neun Seitenknöpfe bei 175 Prozent kosteten so rund 16 ms je Bild, mit dem Verlauf 3 bis 5 (gemessen in
/// NavigationTests). Gebildet wird er wie der Schatten des Leuchtschalters: deckender Kern bis knapp innerhalb des Rands, dann
/// auslaufend. Die Werte kommen aus der Farbstellung: Glas 4 aus Bauplan (Tiefe, Unschärfe, gedrückt kleiner),
/// Glanzprofil (Deckung) und Glas.Schatten.Farbe wie <see cref="Glaskoerper"/>; Taktil aus dem Stilprofil (Stufe je
/// <see cref="Ebene"/>, Richtung 290 Grad, gedrückt keiner) wie <see cref="Taktilkoerper"/>. Nur bei Schattenstufe Voll und
/// bedienbar. Der Körper darüber zeichnet dann keinen eigenen Effekt: Die Vorlagen geben ihm über <see cref="OhneEffekt"/>
/// Einfach statt Voll. Pinsel je Werten einmal gebaut und eingefroren, nicht treffbar.
/// </summary>
public sealed class Rundschatten : FrameworkElement
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Rundschattenart), typeof(Rundschatten), new FrameworkPropertyMetadata(Rundschattenart.Glas, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Taktil: die Ebene des Körpers (wie hoch er liegt).</summary>
    public static readonly DependencyProperty EbeneProperty = DependencyProperty.Register(
        nameof(Ebene), typeof(Taktilebene), typeof(Rundschatten), new FrameworkPropertyMetadata(Taktilebene.Gehoben, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GedruecktProperty = DependencyProperty.Register(
        nameof(Gedrueckt), typeof(bool), typeof(Rundschatten), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Rundschatten), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchattenFarbeProperty = DependencyProperty.Register(
        nameof(SchattenFarbe), typeof(Color), typeof(Rundschatten), new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Rundschatten), new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BauplanProperty = DependencyProperty.Register(
        nameof(Bauplan), typeof(Bauplan), typeof(Rundschatten), new FrameworkPropertyMetadata(Glasdesign.Bauplan.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProfilProperty = DependencyProperty.Register(
        nameof(Profil), typeof(Stilprofil), typeof(Rundschatten), new FrameworkPropertyMetadata(Stilprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Die Farbe des Taktil-Schattens (wie am <see cref="Taktilkoerper"/>).</summary>
    private static readonly Color Taktilfarbe = Color.FromRgb(0x05, 0x05, 0x05);

    private object? _schluessel;
    private RadialGradientBrush? _pinsel;

    static Rundschatten()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Rundschatten), new UIPropertyMetadata(true, (d, _) => ((UIElement)d).InvalidateVisual()));
    }

    public Rundschatten()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SetResourceReference(SchattenFarbeProperty, Schluessel.SchattenFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
        SetResourceReference(BauplanProperty, Schluessel.Bauplan);
        SetResourceReference(ProfilProperty, Schluessel.Stilprofil);
    }

    public Rundschattenart Art { get => (Rundschattenart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public Taktilebene Ebene { get => (Taktilebene)GetValue(EbeneProperty); set => SetValue(EbeneProperty, value); }
    public bool Gedrueckt { get => (bool)GetValue(GedruecktProperty); set => SetValue(GedruecktProperty, value); }
    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }
    public Color SchattenFarbe { get => (Color)GetValue(SchattenFarbeProperty); set => SetValue(SchattenFarbeProperty, value); }

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    public Bauplan Bauplan
    {
        get => GetValue(BauplanProperty) as Bauplan ?? Glasdesign.Bauplan.Standard;
        set => SetValue(BauplanProperty, value);
    }

    public Stilprofil Profil
    {
        get => GetValue(ProfilProperty) as Stilprofil ?? Stilprofil.Standard;
        set => SetValue(ProfilProperty, value);
    }

    internal int Bauten { get; private set; }

    /// <summary>
    /// Converter für die Schattenstufe des Körpers über dem Rundschatten: Voll wird Einfach (der Körper zeichnet dann nur
    /// noch den Kontaktschatten, den Umgebungsschatten zeichnet der Rundschatten), Einfach und Aus bleiben.
    /// </summary>
    public static IValueConverter OhneEffekt { get; } = new Ohnewandler();

    private sealed class Ohnewandler : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Schattenstufe s ? (s == Schattenstufe.Voll ? Schattenstufe.Einfach : s) : DependencyProperty.UnsetValue;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Tiefe und Unschärfe (DIP), Deckung, Richtung (Grad, gegen den Uhrzeigersinn von rechts) und Farbe des Schattens; null: keiner.</summary>
    internal (double Tiefe, double Unschaerfe, double Deckung, double Richtung, Color Farbe)? Werte(double durchmesser)
    {
        if (Schatten != Schattenstufe.Voll || !IsEnabled || durchmesser <= 0) return null;
        if (Art == Rundschattenart.Glas)
        {
            var b = Bauplan;
            return (durchmesser * (Gedrueckt ? b.SchattenTiefeGedrueckt : b.SchattenTiefe), durchmesser * (Gedrueckt ? b.SchattenUnschaerfeGedrueckt : b.SchattenUnschaerfe),
                Gedrueckt ? Glanzprofil.Schatten * 0.65 : Glanzprofil.Schatten, 270, SchattenFarbe);
        }
        var p = Profil;
        var hoehe = Ebene switch
        {
            Taktilebene.Gehoben => 1.0,
            Taktilebene.Karte => 1.6,
            Taktilebene.Haupt => 1.2,
            Taktilebene.Kupfer => 0.8,
            Taktilebene.Metall => 0.9,
            _ => 0,
        };
        var stufe = hoehe * p.TaktilStufe;
        if (Gedrueckt || stufe <= 0 || p.TaktilSchatten <= 0) return null;
        return (1.5 + 2.2 * stufe, 6 + 7 * stufe, Math.Clamp(p.TaktilSchatten * (0.55 + 0.08 * stufe) / 0.63, 0, 1), 290, Taktilfarbe);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var d = Math.Min(RenderSize.Width, RenderSize.Height);
        if (Werte(d) is not { } w) return;
        var r = d / 2 + w.Unschaerfe * 0.6;
        var schluessel = (d, w);
        if (!Equals(_schluessel, schluessel) || _pinsel is null)
        {
            _schluessel = schluessel;
            var kern = Math.Max(0, d / 2 - w.Unschaerfe * 0.35) / r;
            var a = (byte)Math.Round(w.Farbe.A * Math.Clamp(w.Deckung, 0, 1));
            var c = w.Farbe;
            var p = new RadialGradientBrush();
            p.GradientStops.Add(new GradientStop(Color.FromArgb(a, c.R, c.G, c.B), 0));
            p.GradientStops.Add(new GradientStop(Color.FromArgb(a, c.R, c.G, c.B), kern));
            p.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a / 3), c.R, c.G, c.B), kern + (1 - kern) * 0.45));
            p.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            p.Freeze();
            _pinsel = p;
            Bauten++;
        }
        var winkel = w.Richtung * Math.PI / 180;
        var mitte = new Point(RenderSize.Width / 2 + w.Tiefe * Math.Cos(winkel), RenderSize.Height / 2 - w.Tiefe * Math.Sin(winkel));
        drawingContext.DrawEllipse(_pinsel, null, mitte, r, r);
    }
}
