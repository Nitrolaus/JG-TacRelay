// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Spur des Schiebeschalters (Glas.Schalter), Glas 3: Haarlinie (an im Akzentton), gewölbter Körper (an im Akzent, aus
/// dunkel) mit den Spiegelungen aus <see cref="Glanzbild"/>. Ihr einziges Kind, den Knauf, setzt sie pixelgenau nach
/// <see cref="Schaltergeometrie"/>: gleicher Abstand oben, unten und zur Endseite, in beiden Stellungen und bei jeder
/// Skalierung. Ohne gesetzte Größe ist die Spur <see cref="Schaltergeometrie.Hoehe"/> hoch und
/// <see cref="Schaltergeometrie.Seitenverhaeltnis"/> mal so breit.
/// </summary>
public sealed class Schalterbahn : Panel
{
    public static readonly DependencyProperty AnProperty = DependencyProperty.Register(
        nameof(An), typeof(bool), typeof(Schalterbahn),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZustandProperty = DependencyProperty.Register(
        nameof(Zustand), typeof(Glaszustand), typeof(Schalterbahn), new FrameworkPropertyMetadata(Glaszustand.Normal, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AbstandProperty = DependencyProperty.Register(
        nameof(Abstand), typeof(double), typeof(Schalterbahn),
        new FrameworkPropertyMetadata(Schaltergeometrie.Abstand, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpurAnProperty = Pinsel(nameof(SpurAn));
    public static readonly DependencyProperty SpurAusProperty = Pinsel(nameof(SpurAus));
    public static readonly DependencyProperty SpurGesperrtProperty = Pinsel(nameof(SpurGesperrt));
    public static readonly DependencyProperty KanteProperty = Pinsel(nameof(Kante));
    public static readonly DependencyProperty KanteHauptProperty = Pinsel(nameof(KanteHaupt));
    public static readonly DependencyProperty KanteGesperrtProperty = Pinsel(nameof(KanteGesperrt));
    public static readonly DependencyProperty LichtfleckHauptProperty = Pinsel(nameof(LichtfleckHaupt));
    public static readonly DependencyProperty LichtfleckDunkelProperty = Pinsel(nameof(LichtfleckDunkel));

    /// <summary>Die Akzentfarbe (Glas.Akzent), in die das Bodenleuchten des Glanzprofils tönt, wie beim Glaskörper.</summary>
    public static readonly DependencyProperty AkzentProperty = Pinsel(nameof(Akzent));

    public static readonly DependencyProperty SpiegelFarbeProperty = DependencyProperty.Register(
        nameof(SpiegelFarbe), typeof(Color), typeof(Schalterbahn), new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpiegelHauptFarbeProperty = DependencyProperty.Register(
        nameof(SpiegelHauptFarbe), typeof(Color), typeof(Schalterbahn), new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Schalterbahn),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BauplanProperty = DependencyProperty.Register(
        nameof(Bauplan), typeof(Bauplan), typeof(Schalterbahn),
        new FrameworkPropertyMetadata(Glasdesign.Bauplan.Standard,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Schalterbahn), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Der Bauplan der Farbstellung (Spurhöhe, Seitenverhältnis, Abstand, Knaufbreite).</summary>
    public Bauplan Bauplan
    {
        get => GetValue(BauplanProperty) as Bauplan ?? Glasdesign.Bauplan.Standard;
        set => SetValue(BauplanProperty, value);
    }

    /// <summary>Das Glanzprofil der Farbstellung; fehlt es, gilt <see cref="Glasdesign.Glanzprofil.Standard"/>.</summary>
    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    private Glanzbild? _bild;

    public Schalterbahn()
    {
        SetResourceReference(SpurAnProperty, Schluessel.SpurAn);
        SetResourceReference(SpurAusProperty, Schluessel.SpurAus);
        SetResourceReference(SpurGesperrtProperty, Schluessel.SpurGesperrt);
        SetResourceReference(KanteProperty, Schluessel.Kante);
        SetResourceReference(KanteHauptProperty, Schluessel.KanteHaupt);
        SetResourceReference(KanteGesperrtProperty, Schluessel.KanteGesperrt);
        SetResourceReference(LichtfleckHauptProperty, Schluessel.LichtfleckHaupt);
        SetResourceReference(LichtfleckDunkelProperty, Schluessel.LichtfleckDunkel);
        SetResourceReference(AkzentProperty, Schluessel.Akzent);
        SetResourceReference(SpiegelFarbeProperty, Schluessel.SpiegelFarbe);
        SetResourceReference(SpiegelHauptFarbeProperty, Schluessel.SpiegelHauptFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
        SetResourceReference(BauplanProperty, Schluessel.Bauplan);
    }

    public bool An { get => (bool)GetValue(AnProperty); set => SetValue(AnProperty, value); }
    public Glaszustand Zustand { get => (Glaszustand)GetValue(ZustandProperty); set => SetValue(ZustandProperty, value); }

    /// <summary>Luft zwischen Haarlinie der Spur und Knauf in DIP (auf ganze Pixel gerundet, mindestens eins).</summary>
    public double Abstand { get => (double)GetValue(AbstandProperty); set => SetValue(AbstandProperty, value); }

    public Brush? SpurAn { get => (Brush?)GetValue(SpurAnProperty); set => SetValue(SpurAnProperty, value); }
    public Brush? SpurAus { get => (Brush?)GetValue(SpurAusProperty); set => SetValue(SpurAusProperty, value); }
    public Brush? SpurGesperrt { get => (Brush?)GetValue(SpurGesperrtProperty); set => SetValue(SpurGesperrtProperty, value); }
    public Brush? Kante { get => (Brush?)GetValue(KanteProperty); set => SetValue(KanteProperty, value); }
    public Brush? KanteHaupt { get => (Brush?)GetValue(KanteHauptProperty); set => SetValue(KanteHauptProperty, value); }
    public Brush? KanteGesperrt { get => (Brush?)GetValue(KanteGesperrtProperty); set => SetValue(KanteGesperrtProperty, value); }
    public Brush? LichtfleckHaupt { get => (Brush?)GetValue(LichtfleckHauptProperty); set => SetValue(LichtfleckHauptProperty, value); }
    public Brush? LichtfleckDunkel { get => (Brush?)GetValue(LichtfleckDunkelProperty); set => SetValue(LichtfleckDunkelProperty, value); }
    public Brush? Akzent { get => (Brush?)GetValue(AkzentProperty); set => SetValue(AkzentProperty, value); }
    public Color SpiegelFarbe { get => (Color)GetValue(SpiegelFarbeProperty); set => SetValue(SpiegelFarbeProperty, value); }
    public Color SpiegelHauptFarbe { get => (Color)GetValue(SpiegelHauptFarbeProperty); set => SetValue(SpiegelHauptFarbeProperty, value); }

    /// <summary>Maße bei der aktuellen Skalierung, für Tests und Messungen.</summary>
    /// <remarks>Der Abstand kommt aus dem Bauplan, außer er ist am Element selbst gesetzt.</remarks>
    public Schaltermass Mass(Size spur)
    {
        var plan = Bauplan;
        var abstand = ReadLocalValue(AbstandProperty) == DependencyProperty.UnsetValue ? plan.SchalterAbstand : Abstand;
        return Schaltergeometrie.Berechnen(spur, Glasgeometrie.Skala(this), An, abstand, plan.SchalterKnaufZugabe);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var plan = Bauplan;
        var h = double.IsNaN(Height) ? plan.SchalterHoehe : Height;
        var b = double.IsNaN(Width) ? Math.Round(h * plan.SchalterSeitenverhaeltnis) : Width;
        var groesse = new Size(b, h);
        var knauf = Mass(groesse).KnaufDip(Glasgeometrie.Skala(this));
        foreach (UIElement kind in InternalChildren) kind.Measure(knauf.Size);
        return groesse;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var knauf = Mass(finalSize).KnaufDip(Glasgeometrie.Skala(this));
        foreach (UIElement kind in InternalChildren) kind.Arrange(knauf);
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        base.OnRender(dc);
        var s = Glasgeometrie.Skala(this);
        var m = Mass(RenderSize);
        if (m.SpurBreite <= 2 || m.SpurHoehe <= 2) return;
        var gesperrt = Zustand == Glaszustand.Gesperrt;
        var f = new Glasflaeche(0, 0, m.SpurBreite, m.SpurHoehe, m.SpurHoehe / 2.0, s);
        var ton = An ? SpiegelHauptFarbe : SpiegelFarbe;
        _bild = Glanzbild.Holen(_bild, f, Glanzprofil, ton, An ? 1.0 : 0.6, An ? LichtfleckHaupt : LichtfleckDunkel, Pinselwerk.Ton(Akzent, ton));
        dc.DrawGeometry(gesperrt ? SpurGesperrt : An ? SpurAn : SpurAus, null, _bild.Koerper);
        dc.DrawGeometry(gesperrt ? KanteGesperrt : An ? KanteHaupt : Kante, null, _bild.Haar);
        if (!gesperrt) _bild.Zeichnen(dc);
    }
}
