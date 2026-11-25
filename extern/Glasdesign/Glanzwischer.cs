// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Glasdesign;

/// <summary>
/// Der Glanzwischer (Glanzschicht der Glint Abschnitt 5): Beim Überfahren läuft einmal ein schräger Lichtstreifen von
/// links nach rechts über das Glas. Er liegt in den Vorlagen der Glasknöpfe wie <see cref="Spotlicht"/> über dem Körper und
/// unter dem Inhalt, auf derselben Form geschnitten (auch <see cref="Glas.UmrissProperty"/>). Stärke, Neigung, Breite und
/// Dauer stehen im <see cref="Glanzprofil"/> (<c>Glanzwischer*</c>, Stärke 0 heißt aus und ist die Vorgabe), der Ton wie
/// bei den Spiegelungen des Körpers unter <see cref="Schluessel.SpiegelFarbe"/>, auf dem Akzentglas der Hauptaktion
/// (<see cref="Art"/> Haupt) unter <see cref="Schluessel.SpiegelHauptFarbe"/>.
/// <para>
/// Er läuft nur auf dem Bildschirm (mit PresentationSource) und nur, wenn Windows Animationen erlaubt
/// (SystemParameters.ClientAreaAnimation); sonst zeigt er beim Überfahren nichts. Im Speicher (Tests, Vorschaubilder) und
/// bei der Zustandsvorschau "überfahren" steht er ruhend in der Mitte seines Laufs, damit man ihn sieht. Die Form ist je
/// Fläche eingefroren, der Pinsel der Ruhelage je Profil und Farbe; nur während des Laufs entsteht je Bild ein neuer
/// Pinsel. Außerhalb eines Laufs und ohne Vorschau zeichnet er gar nichts.
/// </para>
/// </summary>
public sealed class Glanzwischer : FrameworkElement
{
    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(Glasform), typeof(Glanzwischer), new FrameworkPropertyMetadata(Glasform.Pille, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eine beliebige Form (Abschnitt 6); gesetzt, gilt sie statt <see cref="Form"/>.</summary>
    public static readonly DependencyProperty UmrissProperty = DependencyProperty.Register(
        nameof(Umriss), typeof(Umriss), typeof(Glanzwischer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Die Maus ist über dem Steuerelement: Das startet einen Lauf (die Vorlagen setzen es über IsMouseOver).</summary>
    public static readonly DependencyProperty AktivProperty = DependencyProperty.Register(
        nameof(Aktiv), typeof(bool), typeof(Glanzwischer), new FrameworkPropertyMetadata(false, (d, _) => ((Glanzwischer)d).Steuern()));

    /// <summary>Vorschau ohne Maus (Glas.ZustandVorschau überfahren): der Streifen ruhend in der Mitte seines Laufs.</summary>
    public static readonly DependencyProperty VorschauProperty = DependencyProperty.Register(
        nameof(Vorschau), typeof(bool), typeof(Glanzwischer), new FrameworkPropertyMetadata(false, (d, _) => ((Glanzwischer)d).Steuern()));

    /// <summary>
    /// Wo der Streifen steht: 0 ganz vor dem Glas (links), 1 ganz dahinter (rechts), 0,5 in der Mitte. Außerhalb von 0
    /// bis 1, und auf 0 oder 1 selbst, zeichnet er nichts. Die Vorgabe 1 heißt: in Ruhe, nicht zu sehen.
    /// </summary>
    public static readonly DependencyProperty LaufProperty = DependencyProperty.Register(
        nameof(Lauf), typeof(double), typeof(Glanzwischer), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Color), typeof(Glanzwischer), new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Die Art des Glases darunter (die Vorlagen binden sie an den Körper): Auf dem Akzentglas der Hauptaktion nimmt der
    /// Streifen den Ton <see cref="Schluessel.SpiegelHauptFarbe"/> wie die übrigen Spiegelungen dort, sonst
    /// <see cref="Schluessel.SpiegelFarbe"/>.
    /// </summary>
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Glasart), typeof(Glanzwischer), new FrameworkPropertyMetadata(Glasart.Normal, (d, e) => ((Glanzwischer)d).Verknuepfen((Glasart)e.NewValue)));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Glanzwischer),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Glanzwischer)d).Steuern()));

    /// <summary>Der Lauf ruht in der Mitte (Vorschau, Speicher).</summary>
    public const double Mitte = 0.5;

    private (Glasflaeche Flaeche, Geometry Form)? _form;

    /// <summary>Der Pinsel der Ruhelage (Mitte des Laufs), je Fläche, Profil und Farbe einmal gebaut.</summary>
    private (Glasflaeche Flaeche, Glanzprofil Profil, Color Farbe, Brush Pinsel)? _ruhe;

    static Glanzwischer()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Glanzwischer), new UIPropertyMetadata(true, (d, _) => ((Glanzwischer)d).Steuern()));
    }

    public Glanzwischer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Verknuepfen(Glasart.Normal);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
    }

    /// <summary>Den Ton je Art an die Farbstellung binden (dynamisch, folgt dem Umschalten).</summary>
    private void Verknuepfen(Glasart art) =>
        SetResourceReference(FarbeProperty, art == Glasart.Haupt ? Schluessel.SpiegelHauptFarbe : Schluessel.SpiegelFarbe);

    public Glasform Form { get => (Glasform)GetValue(FormProperty); set => SetValue(FormProperty, value); }

    public Glasart Art { get => (Glasart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }

    public Umriss? Umriss
    {
        get => (Umriss?)GetValue(UmrissProperty);
        set => SetValue(UmrissProperty, value);
    }

    public bool Aktiv { get => (bool)GetValue(AktivProperty); set => SetValue(AktivProperty, value); }
    public bool Vorschau { get => (bool)GetValue(VorschauProperty); set => SetValue(VorschauProperty, value); }
    public double Lauf { get => (double)GetValue(LaufProperty); set => SetValue(LaufProperty, value); }
    public Color Farbe { get => (Color)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    /// <summary>Was der Wischer gerade tut, entschieden von <see cref="Weg"/>.</summary>
    internal enum Wischweg
    {
        /// <summary>Nichts zu sehen.</summary>
        Aus,

        /// <summary>Ruhend in der Mitte (Vorschau, oder überfahren ohne Bildschirm).</summary>
        Ruhend,

        /// <summary>Ein Lauf von links nach rechts, als Animation.</summary>
        Laufen,
    }

    /// <summary>
    /// Die Regel, ohne Element prüfbar: Ohne Stärke oder gesperrt nichts; die Vorschau ruht in der Mitte; überfahren läuft
    /// er nur mit Bildschirm und erlaubten Animationen, ohne Bildschirm ruht er, mit abgeschalteten Animationen bleibt er weg.
    /// </summary>
    internal static Wischweg Weg(bool aktiv, bool vorschau, bool bedienbar, bool bildschirm, bool animationen, Glanzprofil p)
    {
        if (!bedienbar || p.GlanzwischerStaerke <= 0 || p.GlanzwischerBreite <= 0) return Wischweg.Aus;
        if (vorschau) return Wischweg.Ruhend;
        if (!aktiv) return Wischweg.Aus;
        if (!bildschirm) return Wischweg.Ruhend;
        return animationen && p.GlanzwischerDauer > 0 ? Wischweg.Laufen : Wischweg.Aus;
    }

    private void Steuern()
    {
        var p = Glanzprofil;
        var bildschirm = PresentationSource.FromVisual(this) is not null;
        switch (Weg(Aktiv, Vorschau, IsEnabled, bildschirm, SystemParameters.ClientAreaAnimation, p))
        {
            case Wischweg.Laufen:
                var lauf = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(p.GlanzwischerDauer))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    FillBehavior = FillBehavior.HoldEnd,
                };
                lauf.Freeze();
                BeginAnimation(LaufProperty, lauf);
                break;
            case Wischweg.Ruhend:
                BeginAnimation(LaufProperty, null);
                Lauf = Mitte;
                break;
            default:
                // Verlässt die Maus das Glas während eines Laufs, läuft er zu Ende (er endet hinter dem Glas, unsichtbar).
                if (!Aktiv && IsEnabled && p.GlanzwischerStaerke > 0 && HasAnimatedProperties) return;
                BeginAnimation(LaufProperty, null);
                Lauf = 1;
                break;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var p = Glanzprofil;
        var t = Lauf;
        if (t <= 0 || t >= 1 || p.GlanzwischerStaerke <= 0 || p.GlanzwischerBreite <= 0) return;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 4 || h < 4) return;
        var f = Glasflaeche.Aus(RenderSize, s, Form, Umriss);
        if (_form?.Flaeche != f) _form = (f, f.Form(Glasgeometrie.Haarlinie));
        Brush pinsel;
        if (t == Mitte)
        {
            if (_ruhe is not { } ruhe || ruhe.Flaeche != f || ruhe.Profil != p || ruhe.Farbe != Farbe)
                _ruhe = ruhe = (f, p, Farbe, Streifen(f.Breite / s, f.Hoehe / s, t, p, Farbe));
            pinsel = ruhe.Pinsel;
        }
        else pinsel = Streifen(f.Breite / s, f.Hoehe / s, t, p, Farbe);
        drawingContext.DrawGeometry(pinsel, null, _form.Value.Form);
    }

    /// <summary>
    /// Der Streifen als Verlauf quer zu seiner Richtung, an der Stelle <paramref name="t"/> des Laufs über ein Glas der Größe
    /// <paramref name="breite"/> mal <paramref name="hoehe"/> (DIP): bei 0 ganz links vor dem Glas, bei 1 ganz rechts dahinter.
    /// </summary>
    internal static LinearGradientBrush Streifen(double breite, double hoehe, double t, Glanzprofil p, Color farbe)
    {
        var w = Math.Clamp(p.GlanzwischerWinkel, -60, 60) * Math.PI / 180;
        var (ax, ay) = (Math.Cos(w), Math.Sin(w));
        var halb = Math.Max(0.5, p.GlanzwischerBreite) / 2;
        // So weit muss die Mitte links und rechts vor dem Glas stehen, damit kein Teil des Streifens es berührt.
        var vor = (halb + hoehe / 2 * Math.Abs(ay)) / ax + 1;
        var x = -vor + (breite + 2 * vor) * t;
        var mitte = new Point(x, hoehe / 2);
        var a = Math.Clamp(p.GlanzwischerStaerke, 0, 1) * farbe.A / 255.0;
        var pinsel = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(mitte.X - ax * halb, mitte.Y - ay * halb),
            EndPoint = new Point(mitte.X + ax * halb, mitte.Y + ay * halb),
            GradientStops =
            {
                new GradientStop(Farbton.Deckend(farbe, 0), 0),
                new GradientStop(Farbton.Deckend(farbe, a * 0.45), 0.3),
                new GradientStop(Farbton.Deckend(farbe, a), 0.5),
                new GradientStop(Farbton.Deckend(farbe, a * 0.45), 0.7),
                new GradientStop(Farbton.Deckend(farbe, 0), 1),
            },
        };
        pinsel.Freeze();
        return pinsel;
    }
}
