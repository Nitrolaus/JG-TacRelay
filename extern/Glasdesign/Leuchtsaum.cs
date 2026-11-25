// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Glasdesign;

/// <summary>
/// Der Leuchtsaum von Glas 4 (KONZEPT.md Abschnitt 4, Sarevocs Wunsch vom 06.10.2026): ein sehr sanftes, farbiges Leuchten, das
/// direkt an der Haarlinie beginnt und nach außen ausläuft, statt des weißen Fokusrahmens. In Ruhe ist er nicht da, beim
/// Überfahren und beim Tastaturfokus ein Hauch, eingerastet (gewählter Reiter) etwas mehr und im Auswahlton. Stärken,
/// Reichweite, Abfall, oben und unten stehen im <see cref="Glasdesign.Glanzprofil"/>, die Töne in der Farbstellung
/// (<see cref="Schluessel.LeuchtsaumFarbe"/>). Er blendet weich ein und aus (<see cref="Glanzprofil.LeuchtsaumEinblenden"/>).
/// <para>
/// Gezeichnet werden Ringe von je einem Gerätepixel außerhalb der eigenen Grenzen, ohne Effekt und ohne Zwischenebene
/// (<see cref="Saumbild"/>). Der Saum braucht dort Platz: Eltern mit ClipToBounds schneiden ihn ab.
/// </para>
/// </summary>
public sealed class Leuchtsaum : FrameworkElement
{
    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(Glasform), typeof(Leuchtsaum), new FrameworkPropertyMetadata(Glasform.Pille, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eine beliebige Form (Abschnitt 6); gesetzt, gilt sie statt <see cref="Form"/>.</summary>
    public static readonly DependencyProperty UmrissProperty = DependencyProperty.Register(
        nameof(Umriss), typeof(Umriss), typeof(Leuchtsaum), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Umriss? Umriss
    {
        get => (Umriss?)GetValue(UmrissProperty);
        set => SetValue(UmrissProperty, value);
    }

    /// <summary>Eckradius als Anteil der Höhe wie bei den Farbglasknöpfen; NaN (Vorgabe) nimmt die Rundung von <see cref="Form"/>.</summary>
    public static readonly DependencyProperty RundungProperty = DependencyProperty.Register(
        nameof(Rundung), typeof(double), typeof(Leuchtsaum), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eckenradius in DIP (etwa für die matten Knöpfe); NaN (Vorgabe) nimmt <see cref="Rundung"/> oder <see cref="Form"/>.</summary>
    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(double), typeof(Leuchtsaum), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UeberfahrenProperty = Schalter(nameof(Ueberfahren));
    public static readonly DependencyProperty FokusProperty = Schalter(nameof(Fokus));
    public static readonly DependencyProperty EingerastetProperty = Schalter(nameof(Eingerastet));

    public static readonly DependencyProperty FarbeProperty = Ton(nameof(Farbe));
    public static readonly DependencyProperty AuswahlFarbeProperty = Ton(nameof(AuswahlFarbe));

    /// <summary>Eigener Ton (etwa der Background eines Farbglasknopfs); ohne ihn gelten <see cref="Farbe"/> und <see cref="AuswahlFarbe"/>.</summary>
    public static readonly DependencyProperty EigenerTonProperty = DependencyProperty.Register(
        nameof(EigenerTon), typeof(Brush), typeof(Leuchtsaum), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Leuchtsaum),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender, ZielGeaendert));

    /// <summary>Die gezeichnete Deckung an der Haarlinie (Seite); blendet weich zum Ziel aus den Zuständen.</summary>
    public static readonly DependencyProperty StaerkeProperty = DependencyProperty.Register(
        nameof(Staerke), typeof(double), typeof(Leuchtsaum), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Schalter(string name) => DependencyProperty.Register(
        name, typeof(bool), typeof(Leuchtsaum), new FrameworkPropertyMetadata(false, ZielGeaendert));

    private static DependencyProperty Ton(string name) => DependencyProperty.Register(
        name, typeof(Color), typeof(Leuchtsaum), new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    private Saumbild? _bild;

    static Leuchtsaum()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Leuchtsaum), new UIPropertyMetadata(true, ZielGeaendert));
    }

    public Leuchtsaum()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SetResourceReference(FarbeProperty, Schluessel.LeuchtsaumFarbe);
        SetResourceReference(AuswahlFarbeProperty, Schluessel.LeuchtsaumAuswahlFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
    }

    public Glasform Form { get => (Glasform)GetValue(FormProperty); set => SetValue(FormProperty, value); }
    public double Rundung { get => (double)GetValue(RundungProperty); set => SetValue(RundungProperty, value); }
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }
    public bool Ueberfahren { get => (bool)GetValue(UeberfahrenProperty); set => SetValue(UeberfahrenProperty, value); }

    /// <summary>Tastaturfokus; die Stile setzen ihn über <see cref="Glas.TastaturfokusProperty"/>, also nicht nach einem Mausklick.</summary>
    public bool Fokus { get => (bool)GetValue(FokusProperty); set => SetValue(FokusProperty, value); }

    public bool Eingerastet { get => (bool)GetValue(EingerastetProperty); set => SetValue(EingerastetProperty, value); }
    public Color Farbe { get => (Color)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Color AuswahlFarbe { get => (Color)GetValue(AuswahlFarbeProperty); set => SetValue(AuswahlFarbeProperty, value); }
    public Brush? EigenerTon { get => (Brush?)GetValue(EigenerTonProperty); set => SetValue(EigenerTonProperty, value); }
    public double Staerke { get => (double)GetValue(StaerkeProperty); set => SetValue(StaerkeProperty, value); }

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    /// <summary>Die Zielstärke aus den Zuständen: die stärkste; eingerastet und dazu überfahren oder im Fokus etwas mehr.</summary>
    public static double Ziel(Glanzprofil p, bool ueberfahren, bool fokus, bool eingerastet, bool bedienbar = true)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (!bedienbar) return 0;
        var hauch = Math.Max(ueberfahren ? p.LeuchtsaumUeberfahren : 0, fokus ? p.LeuchtsaumFokus : 0);
        return eingerastet ? p.LeuchtsaumEingerastet + hauch / 2 : hauch;
    }

    private static void ZielGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (Leuchtsaum)d;
        Saumblende.Setzen(s, StaerkeProperty, Ziel(s.Glanzprofil, s.Ueberfahren, s.Fokus, s.Eingerastet, s.IsEnabled), s.Glanzprofil.LeuchtsaumEinblenden);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var staerke = Staerke;
        if (staerke <= 0) return;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 2 || h < 2) return;
        var kurz = Math.Min(b, h);
        var r = !double.IsNaN(Ecke) ? Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), kurz / 2)
            : !double.IsNaN(Rundung) ? (Rundung >= 0.5 ? kurz / 2 : Math.Round(kurz * Math.Clamp(Rundung, 0, 0.5)))
            : Form == Glasform.Pille ? kurz / 2 : Glasgeometrie.Raster(kurz * Glasgeometrie.Rundungsanteil, 1);
        var ton = EigenerTon switch
        {
            SolidColorBrush { Color.A: > 0 } e => e.Color,
            GradientBrush { GradientStops.Count: > 0 } g => g.GradientStops[0].Color,
            _ => Eingerastet ? AuswahlFarbe : Farbe,
        };
        var flaeche = Umriss is { } u ? new Glasflaeche(0, 0, b, h, u.Eckradius(b, h, s), s) { Umriss = u } : new Glasflaeche(0, 0, b, h, r, s);
        _bild = Saumbild.Holen(_bild, flaeche, Glanzprofil, ton, staerke);
        _bild.Zeichnen(drawingContext);
    }
}

/// <summary>
/// Die Ringe eines Leuchtsaums um eine Glasfläche: außen je ein Gerätepixel bis zur Reichweite, Deckung
/// <c>Stärke * (1 - x) ^ Abfall</c> (x: Abstand der Ringmitte zur Haarlinie durch die Reichweite, also an der Haarlinie
/// fast voll, am Ende null), jeder Ring mit einem senkrechten Verlauf von "oben" über die Seite (1) nach "unten". Wahlweise
/// innen ein Drittel so weit (<see cref="Glanzprofil.LeuchtsaumInnen"/>). Formen eingefroren und je Fläche einmal gebaut;
/// die Pinsel je Stärke, damit das Einblenden nur Pinsel tauscht.
/// </summary>
internal sealed class Saumbild
{
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    private Saumbild(Glasflaeche f, Glanzprofil p, Color ton, double staerke, Saumbild? alt)
    {
        Flaeche = f;
        Profil = p;
        Ton = ton;
        Staerke = staerke;
        Weite = Math.Max(1, (int)Glasgeometrie.Raster(p.LeuchtsaumReichweite, f.Skala));
        Innen = p.LeuchtsaumInnen > 0 ? Math.Max(1, Weite / 3) : 0;
        // Die Ringe hängen nur an Fläche und Weite; beim Einblenden bleiben sie, nur die Pinsel wechseln. Gebaut wird ein
        // Ring erst, wenn er sichtbar ist (0.5.0-beta.5): Der äußerste blieb meist unsichtbar und brauchte doch die teuerste
        // Kontur.
        Ringe = alt is not null && alt.Flaeche == f && alt.Weite == Weite && alt.Innen == Innen ? alt.Ringe : new Geometry?[Weite + Innen];
        for (var i = 0; i < Weite; i++) Hinzu(i, staerke * Abfall((i + 0.5) / Weite, p.LeuchtsaumAbfall));
        for (var i = 0; i < Innen; i++) Hinzu(Weite + i, staerke * p.LeuchtsaumInnen * Abfall((i + 0.5) / Innen, p.LeuchtsaumAbfall));
    }

    public Glasflaeche Flaeche { get; }
    public Glanzprofil Profil { get; }
    public Color Ton { get; }
    public double Staerke { get; }

    /// <summary>Ringe außen (Gerätepixel bis zur Reichweite) und innen.</summary>
    public int Weite { get; }
    public int Innen { get; }

    /// <summary>Außen die Ringe von der Haarlinie weg, danach die inneren unter der Haarlinie; null, solange keiner ihn zeichnete.</summary>
    private Geometry?[] Ringe { get; }

    private static double Abfall(double x, double hochzahl) => Math.Pow(Math.Max(0, 1 - x), Math.Max(0.1, hochzahl));

    /// <summary>Der Ring an Stelle <paramref name="i"/>, beim ersten Mal gebaut (geteilt mit allen Säumen derselben Fläche und Weite).</summary>
    private Geometry Ring(int i)
    {
        if (Ringe[i] is { } fertig) return fertig;
        var h = Glasgeometrie.Haarlinie;
        return Ringe[i] = i < Weite ? Flaeche.Ring(-(i + 1), -i) : Flaeche.Ring(h + i - Weite, h + i - Weite + 1);
    }

    private void Hinzu(int ring, double deckung)
    {
        var p = Profil;
        // Unter einer halben Stufe von 255 bleibt der Ring unsichtbar; er kostet dann nichts, auch keine Kontur.
        if (deckung * Math.Max(1, Math.Max(p.LeuchtsaumOben, p.LeuchtsaumUnten)) * Ton.A < 0.5) return;
        var a = Ton.A / 255.0;
        _schichten.Add((Ring(ring), Pinselwerk.Senkrecht(
            (Pinselwerk.Alpha(Ton, deckung * p.LeuchtsaumOben * a), 0),
            (Pinselwerk.Alpha(Ton, deckung * a), 0.5),
            (Pinselwerk.Alpha(Ton, deckung * p.LeuchtsaumUnten * a), 1))));
    }

    /// <summary>
    /// Die zuletzt gebauten Säume, geteilt von allen Elementen mit gleicher Fläche, gleichem Profil, Ton und gleicher Stärke
    /// (0.5.0-beta.5, wie <see cref="Glanzbild"/>): Überfahren und Fokus haben im Standardprofil dieselbe Stärke und bauten
    /// bis beta.4 je Element dieselben Ringe und Pinsel. Unveränderlich nach dem Bau; je Faden.
    /// </summary>
    [ThreadStatic]
    private static List<Saumbild>? _geteilt;

    private const int GeteiltHoechstens = 32;

    private bool Passt(Glasflaeche f, Glanzprofil p, Color ton, double staerke) =>
        Flaeche == f && Profil == p && Ton == ton && Staerke.Equals(staerke);

    public static Saumbild Holen(Saumbild? alt, Glasflaeche f, Glanzprofil p, Color ton, double staerke)
    {
        if (alt is not null && alt.Passt(f, p, ton, staerke)) return alt;
        var geteilt = _geteilt ??= [];
        for (var i = geteilt.Count - 1; i >= 0; i--)
            if (geteilt[i].Passt(f, p, ton, staerke)) return geteilt[i];
        var neu = new Saumbild(f, p, ton, staerke, alt);
        if (geteilt.Count >= GeteiltHoechstens) geteilt.RemoveAt(0);
        geteilt.Add(neu);
        return neu;
    }

    public void Zeichnen(DrawingContext dc)
    {
        foreach (var (form, pinsel) in _schichten) dc.DrawGeometry(pinsel, null, form);
    }
}

/// <summary>Weiches Ein- und Ausblenden einer Stärke: animiert, wenn das Element auf dem Bildschirm steht, sonst sofort.</summary>
internal static class Saumblende
{
    public static void Setzen(UIElement element, DependencyProperty eigenschaft, double ziel, double dauerMs)
    {
        var jetzt = (double)element.GetValue(eigenschaft);
        if (jetzt.Equals(ziel) && !element.HasAnimatedProperties) return;
        // Ohne Bildschirm (Musterblatt, Tests, RenderTargetBitmap) gilt der Zielwert gleich, sonst zeigte das Bild den Anfang.
        if (dauerMs <= 0 || PresentationSource.FromVisual(element) is null)
        {
            element.BeginAnimation(eigenschaft, null);
            element.SetValue(eigenschaft, ziel);
            return;
        }
        element.SetValue(eigenschaft, ziel);
        var blende = new DoubleAnimation(jetzt, ziel, TimeSpan.FromMilliseconds(dauerMs)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        blende.Freeze();
        element.BeginAnimation(eigenschaft, blende);
    }
}
