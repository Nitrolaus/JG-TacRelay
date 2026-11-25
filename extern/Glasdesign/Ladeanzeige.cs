// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Glasdesign;

/// <summary>Wie ein <see cref="Spinner"/> dreht: als leuchtender Bogen (Glas 4) oder als Zahnrad aus Metall (Taktil).</summary>
public enum Spinnerart
{
    Bogen,
    Zahnrad,
}

/// <summary>
/// Skelettzeilen (seit 0.5.0-beta.8): Platzhalter, solange Inhalt lädt. <see cref="Zeilen"/> Balken untereinander, vorn
/// wahlweise ein <see cref="Kreis"/>; die Längen fest abgestuft (100, 62, 78, 50 Prozent, dann von vorn) oder eigene
/// (<see cref="Laengen"/>, Anteile der Breite). Farben über <see cref="Balken"/>, <see cref="Rand"/> und
/// <see cref="Schimmer"/>, damit jede Familie (und das Suchfeld im Glutglas) eigene nehmen kann. Der Schimmer läuft nur auf
/// dem Bildschirm und wenn Windows Animationen erlaubt; im Speicher ruht er an einer festen Stelle. Automation: ein
/// Fortschritt mit dem Namen "Wird geladen". Stile: Glas.Skelett (Balken als Mulde), Glas.Taktil.Skelett (eingelassene Tafel).
/// </summary>
public class Skelett : Control
{
    public static readonly DependencyProperty ZeilenProperty = DependencyProperty.Register(
        nameof(Zeilen), typeof(int), typeof(Skelett), new FrameworkPropertyMetadata(4));

    public static readonly DependencyProperty KreisProperty = DependencyProperty.Register(
        nameof(Kreis), typeof(bool), typeof(Skelett), new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty LaengenProperty = DependencyProperty.Register(
        nameof(Laengen), typeof(DoubleCollection), typeof(Skelett), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty BalkenProperty = DependencyProperty.Register(
        nameof(Balken), typeof(Brush), typeof(Skelett), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty RandProperty = DependencyProperty.Register(
        nameof(Rand), typeof(Brush), typeof(Skelett), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SchimmerProperty = DependencyProperty.Register(
        nameof(Schimmer), typeof(Brush), typeof(Skelett), new FrameworkPropertyMetadata(null));

    /// <summary>Höhe eines Balkens in DIP (in den Stilen Glas.Mass.Skelett.Balken); der Abstand ist gleich groß, der Kreis zwei Balken und ein Abstand hoch.</summary>
    public static readonly DependencyProperty BalkenhoeheProperty = DependencyProperty.Register(
        nameof(Balkenhoehe), typeof(double), typeof(Skelett), new FrameworkPropertyMetadata(10d));

    static Skelett()
    {
        FocusableProperty.OverrideMetadata(typeof(Skelett), new FrameworkPropertyMetadata(false));
    }

    public int Zeilen { get => (int)GetValue(ZeilenProperty); set => SetValue(ZeilenProperty, value); }
    public bool Kreis { get => (bool)GetValue(KreisProperty); set => SetValue(KreisProperty, value); }
    public DoubleCollection? Laengen { get => (DoubleCollection?)GetValue(LaengenProperty); set => SetValue(LaengenProperty, value); }
    public Brush? Balken { get => (Brush?)GetValue(BalkenProperty); set => SetValue(BalkenProperty, value); }
    public Brush? Rand { get => (Brush?)GetValue(RandProperty); set => SetValue(RandProperty, value); }
    public Brush? Schimmer { get => (Brush?)GetValue(SchimmerProperty); set => SetValue(SchimmerProperty, value); }
    public double Balkenhoehe { get => (double)GetValue(BalkenhoeheProperty); set => SetValue(BalkenhoeheProperty, value); }

    protected override AutomationPeer OnCreateAutomationPeer() => new Ladeflaeche(this, "Skelett", "Wird geladen");
}

/// <summary>
/// Zeichnet die Skelettzeilen (<see cref="Skelett"/>): Kreis und Balken als Pillen auf ganzen Gerätepixeln (Geometrie je
/// Größe und Werten einmal gebaut und eingefroren), gefüllt mit <see cref="Balken"/>, mit einem Rand von einem Gerätepixel
/// (<see cref="Rand"/>) und dem <see cref="Schimmer"/> als weichem Lichtband bei <see cref="Schimmerlage"/>. Läuft, auf dem
/// Bildschirm und mit erlaubten Animationen, wandert das Band über die Balken; sonst ruht es bei <see cref="Ruhelage"/>.
/// </summary>
public sealed class Skelettbild : FrameworkElement
{
    /// <summary>Wo der Schimmer ruht (Anteil der Breite), im Speicher und ohne Animationen.</summary>
    public const double Ruhelage = 0.3;

    /// <summary>Die fest abgestuften Längen (Anteil der Breite hinter dem Kreis), von vorn wiederholt.</summary>
    public static IReadOnlyList<double> Abstufung { get; } = [1.0, 0.62, 0.78, 0.5];

    public static readonly DependencyProperty ZeilenProperty = Wert(nameof(Zeilen), typeof(int), 4, FrameworkPropertyMetadataOptions.AffectsMeasure);
    public static readonly DependencyProperty KreisProperty = Wert(nameof(Kreis), typeof(bool), true, FrameworkPropertyMetadataOptions.AffectsMeasure);
    public static readonly DependencyProperty LaengenProperty = Wert(nameof(Laengen), typeof(DoubleCollection), null, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty BalkenProperty = Wert(nameof(Balken), typeof(Brush), null, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty RandProperty = Wert(nameof(Rand), typeof(Brush), null, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty SchimmerProperty = Wert(nameof(Schimmer), typeof(Brush), null, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty BalkenhoeheProperty = Wert(nameof(Balkenhoehe), typeof(double), 10d, FrameworkPropertyMetadataOptions.AffectsMeasure);
    public static readonly DependencyProperty SchimmerlageProperty = Wert(nameof(Schimmerlage), typeof(double), Ruhelage, FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty LaeuftProperty = DependencyProperty.Register(
        nameof(Laeuft), typeof(bool), typeof(Skelettbild), new FrameworkPropertyMetadata(true, (d, _) => ((Skelettbild)d).Bewegen()));

    private static DependencyProperty Wert(string name, Type typ, object? vorgabe, FrameworkPropertyMetadataOptions optionen) =>
        DependencyProperty.Register(name, typ, typeof(Skelettbild), new FrameworkPropertyMetadata(vorgabe, optionen));

    private object? _schluessel;
    private Geometry? _flaeche;
    private Geometry? _ring;
    private bool _hoert;

    static Skelettbild()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Skelettbild), new UIPropertyMetadata(true, (d, _) => ((Skelettbild)d).Bewegen()));
    }

    public Skelettbild()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Loaded += (_, _) => Bewegen();
        Unloaded += (_, _) => Anhalten();
        IsVisibleChanged += (_, _) => Bewegen();
    }

    public int Zeilen { get => (int)GetValue(ZeilenProperty); set => SetValue(ZeilenProperty, value); }
    public bool Kreis { get => (bool)GetValue(KreisProperty); set => SetValue(KreisProperty, value); }
    public DoubleCollection? Laengen { get => (DoubleCollection?)GetValue(LaengenProperty); set => SetValue(LaengenProperty, value); }
    public Brush? Balken { get => (Brush?)GetValue(BalkenProperty); set => SetValue(BalkenProperty, value); }
    public Brush? Rand { get => (Brush?)GetValue(RandProperty); set => SetValue(RandProperty, value); }
    public Brush? Schimmer { get => (Brush?)GetValue(SchimmerProperty); set => SetValue(SchimmerProperty, value); }
    public double Balkenhoehe { get => (double)GetValue(BalkenhoeheProperty); set => SetValue(BalkenhoeheProperty, value); }
    public double Schimmerlage { get => (double)GetValue(SchimmerlageProperty); set => SetValue(SchimmerlageProperty, value); }
    public bool Laeuft { get => (bool)GetValue(LaeuftProperty); set => SetValue(LaeuftProperty, value); }

    internal int Bauten { get; private set; }

    /// <summary>Ob es gerade auf das Umschalten von "Animationen anzeigen" hört (für die Tests).</summary>
    internal bool Hoert => _hoert;

    /// <summary>Die Balken (und der Kreis) in Pixeln: Lage und Größe, wie <see cref="OnRender"/> sie zeichnet.</summary>
    internal IReadOnlyList<Rect> Teile(double breitePx, double skala)
    {
        var bh = Math.Max(2, Glasgeometrie.Raster(Balkenhoehe, skala));
        var teile = new List<Rect>();
        var (x, y) = (0.0, 0.0);
        if (Kreis)
        {
            // Der Kreis vorn, zweieinhalb Balken hoch; der erste Balken steht auf seiner Mitte, die übrigen darunter.
            var d = Math.Round(bh * 2.5);
            if ((d - bh) % 2 != 0) d++;
            teile.Add(new Rect(0, 0, d, d));
            x = d + bh;
            y = (d - bh) / 2;
        }
        IReadOnlyList<double> laengen = Laengen is { Count: > 0 } eigene ? [.. eigene] : Abstufung;
        var platz = Math.Max(bh, breitePx - x);
        for (var i = 0; i < Math.Max(0, Zeilen); i++)
        {
            var l = Math.Max(bh, Math.Round(platz * Math.Clamp(laengen[i % laengen.Count], 0, 1)));
            teile.Add(new Rect(x, y + i * 2 * bh, l, bh));
        }
        return teile;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var bh = Math.Max(1, Balkenhoehe);
        var zeilen = Math.Max(0, Zeilen);
        var d = Kreis ? bh * 2.5 + 1 : 0;
        var hoehe = Math.Max(d, (Kreis ? (d - bh) / 2 : 0) + zeilen * 2 * bh - bh);
        var breite = double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width;
        return new Size(breite, Math.Ceiling(hoehe));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var laengen = Laengen is { Count: > 0 } l ? string.Join(";", l) : "";
        var schluessel = (b, RenderSize.Height, s, Zeilen, Kreis, laengen, Balkenhoehe);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            var flaeche = new GeometryGroup { FillRule = FillRule.Nonzero };
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            foreach (var t in Teile(b, s))
            {
                var r = Math.Min(t.Width, t.Height) / 2;
                var f = new Glasflaeche(t.X, t.Y, t.Width, t.Height, r, s);
                flaeche.Children.Add(f.Form(0));
                ring.Children.Add(f.Form(0));
                ring.Children.Add(f.Form(Glasgeometrie.Haarlinie));
            }
            flaeche.Freeze();
            ring.Freeze();
            (_flaeche, _ring) = (flaeche, ring);
            Bauten++;
        }
        if (Balken is { } balken) drawingContext.DrawGeometry(balken, null, _flaeche);
        if (Schimmer is { } schimmer && !double.IsNaN(Schimmerlage))
        {
            // Ein weiches Band, ein Drittel der Breite, mit der Farbe des Schimmers in der Mitte.
            var c = Pinselwerk.Ton(schimmer, Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            var w = RenderSize.Width;
            var mitte = Schimmerlage * w;
            var band = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(mitte - w / 6, 0), EndPoint = new Point(mitte + w / 6, 0) };
            band.GradientStops.Add(new GradientStop(Farbton.Deckend(c, 0), 0));
            band.GradientStops.Add(new GradientStop(c, 0.5));
            band.GradientStops.Add(new GradientStop(Farbton.Deckend(c, 0), 1));
            band.Freeze();
            drawingContext.DrawGeometry(band, null, _flaeche);
        }
        if (Rand is { } rand) drawingContext.DrawGeometry(rand, null, _ring);
    }

    /// <summary>Der Schimmer wandert nur auf dem Bildschirm, mit erlaubten Animationen und bedienbar; sonst ruht er.</summary>
    internal void Bewegen()
    {
        var bildschirm = Animationen.AufDemBildschirm(this);
        var soll = Laeuft && IsEnabled && bildschirm;
        if (soll && !_hoert)
        {
            Animationen.Geaendert += AnimationenGeaendert;
            _hoert = true;
        }
        else if (!soll && _hoert)
        {
            Animationen.Geaendert -= AnimationenGeaendert;
            _hoert = false;
        }
        if (!soll || !Animationen.Erlaubt)
        {
            BeginAnimation(SchimmerlageProperty, null);
            return;
        }
        var lauf = new DoubleAnimation(-0.4, 1.4, TimeSpan.FromMilliseconds(1600)) { RepeatBehavior = RepeatBehavior.Forever };
        lauf.Freeze();
        BeginAnimation(SchimmerlageProperty, lauf);
    }

    private void Anhalten()
    {
        if (_hoert) Animationen.Geaendert -= AnimationenGeaendert;
        _hoert = false;
        BeginAnimation(SchimmerlageProperty, null);
    }

    private void AnimationenGeaendert(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Bewegen();
        else Dispatcher.BeginInvoke(new Action(Bewegen));
    }
}

/// <summary>
/// Spinner (seit 0.5.0-beta.8): zeigt, dass etwas lädt. <see cref="Art"/> Bogen (Glas 4: ein Glasring, auf dem ein Bogen im
/// Licht des Fortschritts kreist) oder Zahnrad (Taktil: gebürstetes Metall mit Loch, Licht oben links). Er dreht nur, solange
/// <see cref="Laeuft"/> (Vorgabe an), auf dem Bildschirm und wenn Windows Animationen erlaubt; im Speicher ruht er in fester
/// Lage. Automation: ein Fortschritt mit dem Namen "Lädt". Stile: Glas.Spinner, Glas.Taktil.Spinner.
/// </summary>
public class Spinner : Control
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Spinnerart), typeof(Spinner), new FrameworkPropertyMetadata(Spinnerart.Bogen));

    public static readonly DependencyProperty LaeuftProperty = DependencyProperty.Register(
        nameof(Laeuft), typeof(bool), typeof(Spinner), new FrameworkPropertyMetadata(true));

    static Spinner()
    {
        FocusableProperty.OverrideMetadata(typeof(Spinner), new FrameworkPropertyMetadata(false));
    }

    public Spinnerart Art { get => (Spinnerart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public bool Laeuft { get => (bool)GetValue(LaeuftProperty); set => SetValue(LaeuftProperty, value); }

    protected override AutomationPeer OnCreateAutomationPeer() => new Ladeflaeche(this, "Spinner", "Lädt");
}

/// <summary>
/// Zeichnet den <see cref="Spinner"/> absolut mittig auf ganzen Gerätepixeln. Bogen: Spur als Ring (<see cref="Spur"/> mit
/// Haarlinie <see cref="Kante"/>), darauf ein Bogen von 110 Grad im <see cref="Licht"/>, zum Ende hin ausblassend, mit
/// weichem Schein (nur mit Schatten). Zahnrad: acht Zähne und ein Loch als gefüllte eigene Geometrie in
/// <see cref="Metall"/>, gebürstete Ringe, Licht oben links, das beim Drehen stehen bleibt (die Pinsel liegen fest, nur die
/// Form dreht sich), Schatten nach unten. Gedreht wird über <see cref="Winkel"/>; die Formen sind je Größe einmal gebaut.
/// </summary>
public sealed class Spinnerbild : FrameworkElement
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Spinnerart), typeof(Spinnerbild), new FrameworkPropertyMetadata(Spinnerart.Bogen, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Spinnerbild)d).Neu()));

    public static readonly DependencyProperty LaeuftProperty = DependencyProperty.Register(
        nameof(Laeuft), typeof(bool), typeof(Spinnerbild), new FrameworkPropertyMetadata(true, (d, _) => ((Spinnerbild)d).Drehen()));

    public static readonly DependencyProperty WinkelProperty = DependencyProperty.Register(
        nameof(Winkel), typeof(double), typeof(Spinnerbild), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpurProperty = DependencyProperty.Register(
        nameof(Spur), typeof(Brush), typeof(Spinnerbild), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KanteProperty = DependencyProperty.Register(
        nameof(Kante), typeof(Brush), typeof(Spinnerbild), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LichtProperty = DependencyProperty.Register(
        nameof(Licht), typeof(Color), typeof(Spinnerbild), new FrameworkPropertyMetadata(Color.FromRgb(0x5F, 0xE0, 0xEA), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MetallProperty = DependencyProperty.Register(
        nameof(Metall), typeof(Color), typeof(Spinnerbild), new FrameworkPropertyMetadata(Color.FromRgb(0x7A, 0x72, 0x6B), FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Spinnerbild)d).Neu()));

    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Spinnerbild), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Spinnerbild)d).Neu()));

    private object? _schluessel;
    private Geometry? _form;
    private Geometry? _neben;
    private bool _hoert;

    static Spinnerbild()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Spinnerbild), new UIPropertyMetadata(true, (d, _) => ((Spinnerbild)d).Drehen()));
    }

    public Spinnerbild()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Loaded += (_, _) => Drehen();
        Unloaded += (_, _) => Anhalten();
        IsVisibleChanged += (_, _) => Drehen();
    }

    public Spinnerart Art { get => (Spinnerart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public bool Laeuft { get => (bool)GetValue(LaeuftProperty); set => SetValue(LaeuftProperty, value); }
    public double Winkel { get => (double)GetValue(WinkelProperty); set => SetValue(WinkelProperty, value); }
    public Brush? Spur { get => (Brush?)GetValue(SpurProperty); set => SetValue(SpurProperty, value); }
    public Brush? Kante { get => (Brush?)GetValue(KanteProperty); set => SetValue(KanteProperty, value); }
    public Color Licht { get => (Color)GetValue(LichtProperty); set => SetValue(LichtProperty, value); }
    public Color Metall { get => (Color)GetValue(MetallProperty); set => SetValue(MetallProperty, value); }
    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }

    internal int Bauten { get; private set; }

    internal bool Hoert => _hoert;

    /// <summary>Größe, wenn der Behälter keine vorgibt (DIP).</summary>
    public const double Grundgroesse = 32;

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(Grundgroesse, availableSize.Width), Math.Min(Grundgroesse, availableSize.Height));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Neu();
    }

    /// <summary>Der Schatten des Zahnrads als eingefrorener Effekt (nur Zahnrad, Schattenstufe Voll, bedienbar).</summary>
    private void Neu()
    {
        if (Art != Spinnerart.Zahnrad || Schatten != Schattenstufe.Voll || !IsEnabled)
        {
            if (Effect is not null) Effect = null;
            return;
        }
        if (Effect is DropShadowEffect) return;
        var e = new DropShadowEffect { Color = Color.FromRgb(0x05, 0x05, 0x05), Direction = 290, ShadowDepth = 2.5, BlurRadius = 7, Opacity = 0.7, RenderingBias = RenderingBias.Performance };
        e.Freeze();
        Effect = e;
    }

    /// <summary>Das Quadrat, in dem gezeichnet wird: ganze Pixel, gleiche Parität wie der Behälter, absolut mittig.</summary>
    internal static (double X, double Y, double D) Feld(double bPx, double hPx)
    {
        var d = Math.Min(bPx, hPx);
        var dx = d - (bPx - d) % 2;
        var dy = d - (hPx - d) % 2;
        var e = Math.Min(dx, dy);
        return ((bPx - e) / 2, (hPx - e) / 2, e);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var (x, y, d) = Feld(Glasgeometrie.Raster(RenderSize.Width, s), Glasgeometrie.Raster(RenderSize.Height, s));
        if (d < 8) return;
        var mitte = new Point((x + d / 2) / s, (y + d / 2) / s);
        var r = d / 2 / s;
        var aktiv = IsEnabled;
        if (Art == Spinnerart.Zahnrad)
        {
            Zahnrad(drawingContext, s, d, mitte, r, aktiv);
            return;
        }
        Bogen(drawingContext, s, d, mitte, r, aktiv);
    }

    private void Bogen(DrawingContext dc, double s, double d, Point mitte, double r, bool aktiv)
    {
        var schluessel = (Spinnerart.Bogen, mitte, d, s);
        var dicke = Math.Max(2, Math.Round(d * 0.14));
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            var aussen = new EllipseGeometry(mitte, r, r);
            var innen = new EllipseGeometry(mitte, r - dicke / s, r - dicke / s);
            var spur = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { aussen, innen } };
            spur.Freeze();
            _neben = spur;
            var haar = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { aussen, new EllipseGeometry(mitte, r - 1 / s, r - 1 / s) } };
            haar.Freeze();
            _form = haar;
            Bauten++;
        }
        if (Spur is { } spurPinsel) dc.DrawGeometry(aktiv ? spurPinsel : Pinselwerk.Gedaempft(spurPinsel, 0.6), null, _neben);
        if (Kante is { } kante) dc.DrawGeometry(kante, null, _form);
        // Der Bogen: 110 Grad ab dem Winkel (0 heißt oben), auf der Mitte der Spur, in Stücken zum Ende hin blasser.
        var licht = aktiv ? Licht : Farbton.Mischen(Licht, Color.FromRgb(0x60, 0x66, 0x6A), 0.7);
        var rm = r - dicke / 2 / s;
        const int Stuecke = 6;
        const double Laenge = 110;
        if (aktiv && Schatten != Schattenstufe.Aus)
            Bogenstueck(dc, mitte, rm, Winkel, Laenge, Farbton.Deckend(licht, 0.22), dicke * 2.2 / s);
        for (var i = 0; i < Stuecke; i++)
        {
            var anfang = Winkel + i * Laenge / Stuecke;
            var deckung = 1 - (Stuecke - 1 - i) * 0.14;
            Bogenstueck(dc, mitte, rm, anfang, Laenge / Stuecke + (i < Stuecke - 1 ? 1 : 0), Farbton.Deckend(licht, deckung), dicke * 0.62 / s, rund: i == Stuecke - 1);
        }
    }

    private static void Bogenstueck(DrawingContext dc, Point m, double r, double anfang, double laenge, Color farbe, double breite, bool rund = false)
    {
        static Point Auf(Point m, double r, double grad) => new(m.X + r * Math.Sin(grad * Math.PI / 180), m.Y - r * Math.Cos(grad * Math.PI / 180));
        var g = new StreamGeometry();
        using (var k = g.Open())
        {
            k.BeginFigure(Auf(m, r, anfang), false, false);
            k.ArcTo(Auf(m, r, anfang + laenge), new Size(r, r), 0, laenge > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        var stift = new Pen(Pinselwerk.Flaeche(farbe), breite) { StartLineCap = PenLineCap.Flat, EndLineCap = rund ? PenLineCap.Round : PenLineCap.Flat };
        stift.Freeze();
        dc.DrawGeometry(null, stift, g);
    }

    private void Zahnrad(DrawingContext dc, double s, double d, Point mitte, double r, bool aktiv)
    {
        var schluessel = (Spinnerart.Zahnrad, mitte, d, s);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            // Zahnkranz und Loch in DIP um die Mitte; gerade Flanken, acht Zähne wie im Vorbild.
            var kranz = Zeichner.Zahnkranz(r, r * 0.74, 8, 9, 15);
            var f = new PathFigure { StartPoint = Verschoben(kranz[0], mitte), IsClosed = true, IsFilled = true };
            foreach (var p in kranz.Skip(1)) f.Segments.Add(new LineSegment(Verschoben(p, mitte), true));
            var rad = new GeometryGroup { FillRule = FillRule.EvenOdd };
            rad.Children.Add(new PathGeometry([f]));
            rad.Children.Add(new EllipseGeometry(mitte, r * 0.3, r * 0.3));
            rad.Freeze();
            _form = rad;
            var ringe = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ringe.Children.Add(new EllipseGeometry(mitte, r * 0.58, r * 0.58));
            ringe.Children.Add(new EllipseGeometry(mitte, r * 0.58 - 1 / s, r * 0.58 - 1 / s));
            ringe.Freeze();
            _neben = ringe;
            Bauten++;
        }
        var m = aktiv ? Metall : Farbton.Mischen(Metall, Color.FromRgb(0x2E, 0x2E, 0x2F), 0.5);
        var gedreht = _form!.Clone();
        gedreht.Transform = new RotateTransform(Winkel % 45, mitte.X, mitte.Y);
        gedreht.Freeze();
        // Licht oben links: Der Pinsel liegt fest im Element, die Form dreht sich darunter.
        var licht = new Point(mitte.X - r * 0.35, mitte.Y - r * 0.4);
        dc.DrawGeometry(Pinselwerk.Rund(licht, r * 1.5, r * 1.5,
            (Farbton.Mischen(m, Colors.White, 0.38), 0), (m, 0.38), (Farbton.Mischen(m, Colors.Black, 0.2), 0.7), (Farbton.Mischen(m, Colors.Black, 0.45), 1)), null, gedreht);
        // Gebürstet: ein feiner Ring, oben dunkel, unten hell; dazu die Kante des Lochs, oben dunkel.
        dc.DrawGeometry(Pinselwerk.Verlauf(new Point(0, mitte.Y - r), new Point(0, mitte.Y + r), Farbton.Deckend(Colors.Black, 0.3), Farbton.Deckend(Colors.White, 0.22)), null, _neben);
        var loch = new GeometryGroup { FillRule = FillRule.EvenOdd };
        loch.Children.Add(new EllipseGeometry(mitte, r * 0.3 + 1 / s, r * 0.3 + 1 / s));
        loch.Children.Add(new EllipseGeometry(mitte, r * 0.3, r * 0.3));
        loch.Freeze();
        dc.DrawGeometry(Pinselwerk.Verlauf(new Point(0, mitte.Y - r * 0.3), new Point(0, mitte.Y + r * 0.3), Farbton.Deckend(Colors.Black, 0.5), Farbton.Deckend(Colors.White, 0.3)), null, loch);
    }

    private static Point Verschoben(Point p, Point m) => new(m.X + p.X, m.Y + p.Y);

    /// <summary>Dreht nur auf dem Bildschirm, mit erlaubten Animationen, bedienbar und solange es läuft; sonst in Ruhelage (Winkel 0).</summary>
    internal void Drehen()
    {
        var bildschirm = Animationen.AufDemBildschirm(this);
        var soll = Laeuft && IsEnabled && bildschirm;
        if (soll && !_hoert)
        {
            Animationen.Geaendert += AnimationenGeaendert;
            _hoert = true;
        }
        else if (!soll && _hoert)
        {
            Animationen.Geaendert -= AnimationenGeaendert;
            _hoert = false;
        }
        Neu();
        if (!soll || !Animationen.Erlaubt)
        {
            BeginAnimation(WinkelProperty, null);
            return;
        }
        var kreis = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(Art == Spinnerart.Zahnrad ? 2400 : 1000)) { RepeatBehavior = RepeatBehavior.Forever };
        kreis.Freeze();
        BeginAnimation(WinkelProperty, kreis);
    }

    private void Anhalten()
    {
        if (_hoert) Animationen.Geaendert -= AnimationenGeaendert;
        _hoert = false;
        BeginAnimation(WinkelProperty, null);
    }

    private void AnimationenGeaendert(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Drehen();
        else Dispatcher.BeginInvoke(new Action(Drehen));
    }
}

/// <summary>Automation von Skelett und Spinner: ein Fortschritt (unbestimmt), Name "Wird geladen" oder "Lädt", wenn keiner gesetzt ist.</summary>
internal sealed class Ladeflaeche(FrameworkElement element, string klasse, string vorgabe) : FrameworkElementAutomationPeer(element)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;

    protected override string GetClassNameCore() => klasse;

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? vorgabe : name;
    }

    protected override bool IsKeyboardFocusableCore() => false;
}
