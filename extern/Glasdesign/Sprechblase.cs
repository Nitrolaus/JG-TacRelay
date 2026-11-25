// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glasdesign;

/// <summary>An welcher Seite der <see cref="Sprechblase"/> ihr Zipfel sitzt (er zeigt von dort nach außen, zum Ziel).</summary>
public enum Zipfelseite
{
    Oben,
    Unten,
    Links,
    Rechts,

    /// <summary>Kein Zipfel: eine schwebende Fläche (Toast, Dialog im Glas 4).</summary>
    Keine,
}

/// <summary>
/// Sprechblase (seit 0.5.0-beta.8): eine Fläche mit abgerundeten Ecken und einem Zipfel an einer Seite
/// (<see cref="Zipfelseite"/>, Lage längs der Seite als Anteil <see cref="Zipfellage"/>), Rand genau ein Gerätepixel, Füllung
/// und Rand als Pinsel. Das Kind steht in der Fläche ohne den Zipfel, um <see cref="Innenabstand"/> eingerückt. Gebraucht für
/// den Tooltip mit Zipfel (Stile Glas.Tooltip.Zipfel und Glas.Taktil.Tooltip), die Beschriftung des aktiven Schritts der
/// Schrittfolge und als schwebende Fläche ohne Zipfel (Toast, Dialog). Der Zipfel ist ein rechtwinkliges Dreieck (Höhe
/// <see cref="Zipfelgroesse"/>, Basis doppelt so breit); die Geometrie liegt auf ganzen Gerätepixeln, wird je Größe,
/// Skalierung, Seite, Lage und Maß einmal gebaut und eingefroren. Schwebend wirft die Fläche einen weichen Schatten nach unten
/// (Deckung aus dem Glanzprofil wie bei Glas 4, Farbe Glas.Schatten.Farbe): Ringe von je einem Gerätepixel um die nach unten
/// versetzte Form, nach außen schwächer, einmal gebaut und eingefroren statt eines Unschärfeeffekts (der kostete eine große
/// Karte bei 175 Prozent im Speicher über 40 ms je Bild). <see cref="Glas.SchattenProperty"/> Voll zeigt ihn, Einfach und Aus
/// nicht. Er liegt unter Füllung und Kind; die Schrift wirft keinen.
/// </summary>
public sealed class Sprechblase : Decorator
{
    public static readonly DependencyProperty ZipfelseiteProperty = DependencyProperty.Register(
        nameof(Zipfelseite), typeof(Zipfelseite), typeof(Sprechblase), new FrameworkPropertyMetadata(Zipfelseite.Unten, FrameworkPropertyMetadataOptions.AffectsMeasure, Neu));

    /// <summary>Lage der Zipfelmitte längs ihrer Seite (0 links oder oben, 1 rechts oder unten); er bleibt neben den Rundungen.</summary>
    public static readonly DependencyProperty ZipfellageProperty = DependencyProperty.Register(
        nameof(Zipfellage), typeof(double), typeof(Sprechblase), new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsArrange, Neu));

    /// <summary>Höhe des Zipfels in DIP (die Basis ist doppelt so breit); in den Stilen Glas.Mass.Sprechblase.Zipfel.</summary>
    public static readonly DependencyProperty ZipfelgroesseProperty = DependencyProperty.Register(
        nameof(Zipfelgroesse), typeof(double), typeof(Sprechblase), new FrameworkPropertyMetadata(7d, FrameworkPropertyMetadataOptions.AffectsMeasure, Neu));

    /// <summary>Eckenradius in DIP.</summary>
    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(double), typeof(Sprechblase), new FrameworkPropertyMetadata(6d, Neu));

    public static readonly DependencyProperty FuellungProperty = DependencyProperty.Register(
        nameof(Fuellung), typeof(Brush), typeof(Sprechblase), new FrameworkPropertyMetadata(null, Neu));

    /// <summary>Der Rand, genau ein Gerätepixel breit; null: keiner.</summary>
    public static readonly DependencyProperty RandProperty = DependencyProperty.Register(
        nameof(Rand), typeof(Brush), typeof(Sprechblase), new FrameworkPropertyMetadata(null, Neu));

    public static readonly DependencyProperty InnenabstandProperty = DependencyProperty.Register(
        nameof(Innenabstand), typeof(Thickness), typeof(Sprechblase), new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Schwebend: der weiche Schatten von Glas 4 unter der Fläche.</summary>
    public static readonly DependencyProperty SchwebendProperty = DependencyProperty.Register(
        nameof(Schwebend), typeof(bool), typeof(Sprechblase), new FrameworkPropertyMetadata(true, SchattenNeu));

    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Sprechblase), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits, SchattenNeu));

    public static readonly DependencyProperty SchattenFarbeProperty = DependencyProperty.Register(
        nameof(SchattenFarbe), typeof(Color), typeof(Sprechblase), new FrameworkPropertyMetadata(Colors.Black, SchattenNeu));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Sprechblase), new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, SchattenNeu));

    /// <summary>
    /// Zwischenbild (seit 0.5.0-beta.10, für große Karten): Schatten, Füllung und Rand einmal in ein Bild in Gerätepixeln
    /// rechnen (<see cref="Bildraster"/>) und nur dieses setzen; die Ringe des Schattens kosteten eine Karte von 480 mal 360 DIP
    /// bei 175 Prozent im Speicher sonst rund 9 ms je Bild. Vorgabe aus.
    /// </summary>
    public static readonly DependencyProperty ZwischenbildProperty = DependencyProperty.Register(
        nameof(Zwischenbild), typeof(bool), typeof(Sprechblase), new FrameworkPropertyMetadata(false, Neu));

    /// <summary>Tiefe (nach unten) und Weite des Schattens schwebender Flächen in DIP, Licht von oben wie bei Glas 4.</summary>
    public const double Schattentiefe = 4, Schattenweite = 14;

    private readonly Blasenflaeche _flaeche;

    public Sprechblase()
    {
        _flaeche = new Blasenflaeche(this);
        // Nur für das Zwischenbild (die Fläche zeichnet sonst keine Bilder): Pixel für Pixel, ohne Glätten beim Setzen.
        RenderOptions.SetBitmapScalingMode(_flaeche, BitmapScalingMode.NearestNeighbor);
        AddVisualChild(_flaeche);
        SetResourceReference(SchattenFarbeProperty, Schluessel.SchattenFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
    }

    public Zipfelseite Zipfelseite { get => (Zipfelseite)GetValue(ZipfelseiteProperty); set => SetValue(ZipfelseiteProperty, value); }
    public double Zipfellage { get => (double)GetValue(ZipfellageProperty); set => SetValue(ZipfellageProperty, value); }
    public double Zipfelgroesse { get => (double)GetValue(ZipfelgroesseProperty); set => SetValue(ZipfelgroesseProperty, value); }
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }
    public Brush? Fuellung { get => (Brush?)GetValue(FuellungProperty); set => SetValue(FuellungProperty, value); }
    public Brush? Rand { get => (Brush?)GetValue(RandProperty); set => SetValue(RandProperty, value); }
    public Thickness Innenabstand { get => (Thickness)GetValue(InnenabstandProperty); set => SetValue(InnenabstandProperty, value); }
    public bool Schwebend { get => (bool)GetValue(SchwebendProperty); set => SetValue(SchwebendProperty, value); }
    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }
    public Color SchattenFarbe { get => (Color)GetValue(SchattenFarbeProperty); set => SetValue(SchattenFarbeProperty, value); }
    public bool Zwischenbild { get => (bool)GetValue(ZwischenbildProperty); set => SetValue(ZwischenbildProperty, value); }

    /// <summary>Wie oft das Zwischenbild gerechnet wurde (für die Tests).</summary>
    internal int Zwischenbilder => _flaeche.Zwischenbilder;

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    /// <summary>Wie oft die Geometrie gebaut wurde (für die Tests: Zeichnen und neue Pinsel bauen nichts).</summary>
    internal int Bauten => _flaeche.Bauten;

    /// <summary>Die Fläche mit dem Schatten (für die Tests).</summary>
    internal FrameworkElement Flaeche => _flaeche;

    /// <summary>
    /// Platzierung für einen ToolTip (Placement Custom): mittig über dem Ziel, der Zipfel unten zeigt auf dessen Mitte; als
    /// statischer Wert im Stil: <c>CustomPopupPlacementCallback="{x:Static gd:Sprechblase.Darueber}"</c>. Horizontal- und
    /// VerticalOffset des ToolTips verschieben wie gewohnt. Den Abstand zum Ziel gibt der Rand um die Blase in der Vorlage.
    /// </summary>
    public static CustomPopupPlacementCallback Darueber { get; } = (popup, ziel, versatz) =>
        [new CustomPopupPlacement(new Point((ziel.Width - popup.Width) / 2 + versatz.X, -popup.Height + versatz.Y), PopupPrimaryAxis.Horizontal)];

    protected override int VisualChildrenCount => Child is null ? 1 : 2;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _flaeche,
        1 when Child is { } kind => kind,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Der Zipfel in DIP, wie ihn das Layout freihält (0 ohne Zipfel).</summary>
    private double ZipfelDip => Zipfelseite == Zipfelseite.Keine ? 0 : Math.Max(0, Zipfelgroesse);

    private Thickness Rahmen
    {
        get
        {
            var z = ZipfelDip;
            var p = Innenabstand;
            return new Thickness(p.Left + (Zipfelseite == Zipfelseite.Links ? z : 0), p.Top + (Zipfelseite == Zipfelseite.Oben ? z : 0),
                p.Right + (Zipfelseite == Zipfelseite.Rechts ? z : 0), p.Bottom + (Zipfelseite == Zipfelseite.Unten ? z : 0));
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var r = Rahmen;
        var aussen = new Size(r.Left + r.Right, r.Top + r.Bottom);
        _flaeche.Measure(constraint);
        if (Child is not { } kind) return aussen;
        kind.Measure(new Size(Math.Max(0, constraint.Width - aussen.Width), Math.Max(0, constraint.Height - aussen.Height)));
        return new Size(kind.DesiredSize.Width + aussen.Width, kind.DesiredSize.Height + aussen.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        _flaeche.Arrange(new Rect(arrangeSize));
        var r = Rahmen;
        Child?.Arrange(new Rect(r.Left, r.Top, Math.Max(0, arrangeSize.Width - r.Left - r.Right), Math.Max(0, arrangeSize.Height - r.Top - r.Bottom)));
        return arrangeSize;
    }

    private static void Neu(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Sprechblase)d)._flaeche.InvalidateVisual();

    private static void SchattenNeu(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Sprechblase)d)._flaeche.InvalidateVisual();

    /// <summary>
    /// Die Kontur in Pixeln, um <paramref name="d"/> Pixel nach innen versetzt: gerade Kanten um d, die Rundungen mit Radius
    /// minus d, die Kanten des Zipfels (45 Grad) senkrecht um d; die Zipfelmitte bleibt, wo sie ist. Seit 0.5.0-beta.13 (A7) als
    /// StreamGeometry statt PathGeometry mit einzelnen Segmenten: dieselben Punkte und Bögen, Pixel für Pixel dasselbe Bild,
    /// aber ohne ein Objekt je Segment (die 25 Schattenringe einer Blase bei 175 Prozent 0,2 statt 0,9 ms).
    /// </summary>
    internal static Geometry Kontur(double b, double h, double skala, Zipfelseite seite, double lage, double zipfel, double ecke, double d)
    {
        var t = seite == Zipfelseite.Keine ? 0 : Math.Max(0, Glasgeometrie.Raster(zipfel, skala));
        double links = seite == Zipfelseite.Links ? t : 0, oben = seite == Zipfelseite.Oben ? t : 0;
        double rechts = b - (seite == Zipfelseite.Rechts ? t : 0), unten = h - (seite == Zipfelseite.Unten ? t : 0);
        var r = Math.Max(0, Math.Min(Glasgeometrie.Raster(Math.Max(0, ecke), skala), Math.Min(rechts - links, unten - oben) / 2));
        // Mitte des Zipfels längs seiner Seite, neben den Rundungen.
        var waagerecht = seite is Zipfelseite.Oben or Zipfelseite.Unten;
        var (von, bis) = waagerecht ? (links, rechts) : (oben, unten);
        var c = von + Math.Clamp(double.IsNaN(lage) ? 0.5 : lage, 0, 1) * (bis - von);
        c = von + r + t <= bis - r - t ? Math.Clamp(c, von + r + t, bis - r - t) : (von + bis) / 2;

        double l = links + d, o = oben + d, re = rechts - d, u = unten - d, rr = Math.Max(0, r - d);
        var halb = t - d * (Math.Sqrt(2) - 1);
        var spitze = t + d - d * Math.Sqrt(2);
        var g = new StreamGeometry();
        var f = g.Open();
        f.BeginFigure(P(l + rr, o), true, true);
        var bogen = new Size(rr / skala, rr / skala);

        void Linie(double x, double y) => f.LineTo(P(x, y), true, false);
        void Bogen(double x, double y)
        {
            if (rr > 0) f.ArcTo(P(x, y), bogen, 0, false, SweepDirection.Clockwise, true, false);
            else Linie(x, y);
        }
        Point P(double x, double y) => new(x / skala, y / skala);

        if (seite == Zipfelseite.Oben && t > 0)
        {
            Linie(c - halb, o);
            Linie(c, o - spitze);
            Linie(c + halb, o);
        }
        Linie(re - rr, o);
        Bogen(re, o + rr);
        if (seite == Zipfelseite.Rechts && t > 0)
        {
            Linie(re, c - halb);
            Linie(re + spitze, c);
            Linie(re, c + halb);
        }
        Linie(re, u - rr);
        Bogen(re - rr, u);
        if (seite == Zipfelseite.Unten && t > 0)
        {
            Linie(c + halb, u);
            Linie(c, u + spitze);
            Linie(c - halb, u);
        }
        Linie(l + rr, u);
        Bogen(l, u - rr);
        if (seite == Zipfelseite.Links && t > 0)
        {
            Linie(l, c + halb);
            Linie(l - spitze, c);
            Linie(l, c - halb);
        }
        Linie(l, o + rr);
        Bogen(l + rr, o);
        f.Close();
        g.Freeze();
        return g;
    }

    /// <summary>
    /// Zeichnet Schatten, Füllung und Rand der Sprechblase unter dem Kind. Die Formen werden je Größe, Skalierung, Seite, Lage
    /// und Maß einmal gebaut (<see cref="Bauten"/>), die Schattenringe zusätzlich je Schattenstufe, Farbe und Deckung. Seit
    /// 0.5.0-beta.13 (Teilschritt A7) teilen gleiche Blasen eines Fadens Formen, Schattenringe und Zwischenbild
    /// (<see cref="Teilvorrat{T}"/>): Bei einem neuen Maß rechnet nur die erste Blase jeder Art (Glint G3: die Blasen der
    /// Schrittfolgen, Toasts und Tooltips einer Familie bauten je Schritt jede für sich rund 25 Ringe).
    /// </summary>
    private sealed class Blasenflaeche(Sprechblase blase) : FrameworkElement
    {
        private object? _schluessel;
        private object? _schattenschluessel;
        private Blasenformen? _formen;
        private Schattenringe _schatten = Schattenringe.Keine;
        private object? _bildschluessel;
        private Rasterbild? _bild;

        public int Bauten { get; private set; }

        public int Zwischenbilder { get; private set; }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

        protected override void OnRender(DrawingContext drawingContext)
        {
            ArgumentNullException.ThrowIfNull(drawingContext);
            var s = Glasgeometrie.Skala(this);
            var b = Glasgeometrie.Raster(RenderSize.Width, s);
            var h = Glasgeometrie.Raster(RenderSize.Height, s);
            if (b < 4 || h < 4) return;
            var (seite, lage, zipfel, ecke) = (blase.Zipfelseite, blase.Zipfellage, blase.Zipfelgroesse, blase.Ecke);
            var schluessel = (b, h, s, seite, lage, zipfel, ecke);
            if (!Equals(_schluessel, schluessel))
            {
                _schluessel = schluessel;
                _schattenschluessel = null;
                _formen = Formvorrat.Holen(schluessel);
                if (_formen is null)
                {
                    var aussen = Kontur(b, h, s, seite, lage, zipfel, ecke, 0);
                    var innen = Kontur(b, h, s, seite, lage, zipfel, ecke, Glasgeometrie.Haarlinie);
                    var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
                    ring.Children.Add(aussen);
                    ring.Children.Add(innen);
                    ring.Freeze();
                    _formen = new Blasenformen(innen, ring);
                    Formvorrat.Ablegen(schluessel, _formen);
                }
                Bauten++;
            }
            var (innenform, ringform) = (_formen!.Innen, _formen.Ring);
            var mitSchatten = blase.Schwebend && blase.Schatten == Schattenstufe.Voll;
            var deckung = Math.Clamp(blase.Glanzprofil.Schatten, 0, 1) * 0.5;
            var farbe = blase.SchattenFarbe;
            // Im Zwischenbild setzt sich der Schatten einer Blase ohne Zipfel aus Stücken zusammen (seit 0.5.0-beta.12), statt
            // bei jeder neuen Größe alle Ringe neu zu bauen und zu rastern.
            var stuecke = blase.Zwischenbild && mitSchatten && deckung > 0 && seite == Zipfelseite.Keine
                ? Schattenstuecke.Holen(s, ecke, farbe, deckung, b, h) : null;
            var schattenschluessel = (mitSchatten, farbe, deckung, stuecke is null);
            if (!Equals(_schattenschluessel, schattenschluessel))
            {
                _schattenschluessel = schattenschluessel;
                _schatten = Schattenringe.Keine;
                if (mitSchatten && deckung > 0 && stuecke is null)
                {
                    var ringschluessel = (schluessel, farbe, deckung);
                    _schatten = Ringvorrat.Holen(ringschluessel) ?? new Schattenringe(Ringe(b, h, s, seite, lage, zipfel, ecke, farbe, deckung));
                    Ringvorrat.Ablegen(ringschluessel, _schatten);
                }
            }
            if (blase.Zwischenbild)
            {
                // Schatten, Füllung und Rand als ein Bild, neu nur bei neuer Form, neuem Schatten oder neuen Pinseln (einfarbige
                // nach ihrem Wert); dazu, wie gerechnet wird (Streifen, Schatten aus Stücken: die Tests rechnen beide Wege).
                var (fuellung, rand) = (Pinselwert(blase.Fuellung), Pinselwert(blase.Rand));
                var bildschluessel = (_schluessel, _schattenschluessel, fuellung, rand, Bildraster.Streifen, Schattenstuecke.An);
                if (!Equals(_bildschluessel, bildschluessel))
                {
                    _bildschluessel = bildschluessel;
                    var teilbar = fuellung.Anderer is not { IsFrozen: false } && rand.Anderer is not { IsFrozen: false };
                    _bild = teilbar ? Bildvorrat.Holen(bildschluessel) : null;
                    if (_bild is null)
                    {
                        var gruppe = new DrawingGroup();
                        stuecke?.Legen(gruppe, b, h, s);
                        foreach (var (form, pinsel) in _schatten.Liste) gruppe.Children.Add(new GeometryDrawing(pinsel, null, form));
                        if (blase.Fuellung is { } f) gruppe.Children.Add(new GeometryDrawing(f, null, innenform));
                        if (blase.Rand is { } r) gruppe.Children.Add(new GeometryDrawing(r, null, ringform));
                        gruppe.Freeze();
                        _bild = new Rasterbild(Bildraster.Rastern(gruppe, s));
                        _gerastert++;
                        if (teilbar) Bildvorrat.Ablegen(bildschluessel, _bild);
                    }
                    Zwischenbilder++;
                }
                if (_bild!.Bild is { } z)
                {
                    drawingContext.DrawImage(z.Bild, z.Lage);
                    return;
                }
            }
            foreach (var (form, pinsel) in _schatten.Liste) drawingContext.DrawGeometry(pinsel, null, form);
            if (blase.Fuellung is { } fuellungspinsel) drawingContext.DrawGeometry(fuellungspinsel, null, innenform);
            if (blase.Rand is { } randpinsel) drawingContext.DrawGeometry(randpinsel, null, ringform);
        }
    }

    /// <summary>Ein Pinsel für den Schlüssel des Zwischenbilds: einfarbige nach Farbe und Deckung, die übrigen als sie selbst (Anderer).</summary>
    private static (Color Farbe, double Deckung, Brush? Anderer) Pinselwert(Brush? pinsel) =>
        pinsel is SolidColorBrush f ? (f.Color, f.Opacity, null) : (default, 1, pinsel);

    /// <summary>Innenfläche und Rand einer Blase (eingefroren, geteilt).</summary>
    private sealed record Blasenformen(Geometry Innen, Geometry Ring);

    /// <summary>Die Schattenringe einer Blase (eingefroren, geteilt).</summary>
    private sealed class Schattenringe(List<(Geometry Form, Brush Pinsel)> liste)
    {
        public static Schattenringe Keine { get; } = new([]);

        public IReadOnlyList<(Geometry Form, Brush Pinsel)> Liste { get; } = liste;
    }

    /// <summary>Ein Zwischenbild samt Lage (null: leer oder zu groß, dann zeichnet die Blase direkt).</summary>
    private sealed class Rasterbild((ImageSource Bild, Rect Lage)? bild)
    {
        public (ImageSource Bild, Rect Lage)? Bild { get; } = bild;

        public long Bytes => Bild?.Bild is BitmapSource b ? 4L * b.PixelWidth * b.PixelHeight : 0;
    }

    [ThreadStatic]
    private static Teilvorrat<Blasenformen>? _formvorrat;

    [ThreadStatic]
    private static Teilvorrat<Schattenringe>? _ringvorrat;

    [ThreadStatic]
    private static Teilvorrat<Rasterbild>? _bildvorrat;

    [ThreadStatic]
    private static int _gerastert;

    private static Teilvorrat<Blasenformen> Formvorrat => _formvorrat ??= new Teilvorrat<Blasenformen>(64);

    private static Teilvorrat<Schattenringe> Ringvorrat => _ringvorrat ??= new Teilvorrat<Schattenringe>(64);

    private static Teilvorrat<Rasterbild> Bildvorrat => _bildvorrat ??= new Teilvorrat<Rasterbild>(48, 16L << 20, z => z.Bytes);

    /// <summary>Wie oft auf diesem Faden Schattenringe einer Blase geteilt statt gebaut wurden (für die Tests).</summary>
    internal static int RingeGeteilt => Ringvorrat.Treffer;

    /// <summary>Wie oft auf diesem Faden ein Zwischenbild einer Blase wirklich gerechnet wurde (für die Tests).</summary>
    internal static int Gerastert => _gerastert;

    /// <summary>Wie oft auf diesem Faden ein Zwischenbild einer Blase geteilt statt gerechnet wurde (für die Tests).</summary>
    internal static int BilderGeteilt => Bildvorrat.Treffer;

    /// <summary>Die Vorräte dieses Fadens leeren (für die Tests: eine Blase baut dann selbst).</summary>
    internal static void VorratLeeren()
    {
        Formvorrat.Leeren();
        Ringvorrat.Leeren();
        Bildvorrat.Leeren();
    }

    /// <summary>
    /// Der Schatten: die Form um <see cref="Schattentiefe"/> nach unten versetzt, darum Ringe von je einem Gerätepixel bis
    /// <see cref="Schattenweite"/>, die nach außen quadratisch schwächer werden (wie ein weicher Schlagschatten).
    /// </summary>
    private static List<(Geometry Form, Brush Pinsel)> Ringe(double b, double h, double s, Zipfelseite seite, double lage, double zipfel, double ecke, Color farbe, double deckung)
    {
        var ringe = new List<(Geometry Form, Brush Pinsel)>();
        var versatz = new TranslateTransform(0, Glasgeometrie.Raster(Schattentiefe, s) / s);
        versatz.Freeze();
        var weite = Math.Max(1, (int)Glasgeometrie.Raster(Schattenweite, s));
        Geometry Versetzt(Geometry g)
        {
            var gruppe = new GeometryGroup { FillRule = FillRule.EvenOdd, Transform = versatz };
            gruppe.Children.Add(g);
            gruppe.Freeze();
            return gruppe;
        }
        var innen = Kontur(b, h, s, seite, lage, zipfel, ecke, 0);
        ringe.Add((Versetzt(innen), Pinselwerk.Flaeche(Farbton.Deckend(farbe, deckung))));
        for (var i = 0; i < weite; i++)
        {
            var aussen = Kontur(b, h, s, seite, lage, zipfel, ecke, -(i + 1));
            var a = deckung * Math.Pow(1 - (i + 0.5) / weite, 2);
            if (a * 255 >= 0.5)
            {
                var ring = new GeometryGroup { FillRule = FillRule.EvenOdd, Transform = versatz };
                ring.Children.Add(aussen);
                ring.Children.Add(innen);
                ring.Freeze();
                ringe.Add((ring, Pinselwerk.Flaeche(Farbton.Deckend(farbe, a))));
            }
            innen = aussen;
        }
        return ringe;
    }

    /// <summary>
    /// Der Schatten einer Blase ohne Zipfel in Stücken (seit 0.5.0-beta.12, nur für das Zwischenbild): einmal je Skalierung,
    /// Ecke, Farbe und Deckung an einer kleinen Blase gerastert (Ecken samt Schatten, dazu je eine Spalte und eine Zeile der
    /// geraden Kanten). Jede größere Blase setzt sich daraus zusammen: Ecken an ihren Platz, die eine Spalte und die eine Zeile
    /// gestreckt (Pixel für Pixel), die Mitte als Fläche im Ton der Form. Die Ringe liegen um jede Ecke konzentrisch und an den
    /// geraden Kanten auf ganzen Pixeln; darum gleicht das Ergebnis dem der Ringe an der großen Blase (bis auf Rundung), und
    /// eine neue Größe kostet keine 25 Ringe mehr (Glas.Inhaltskarte 480 mal 360 DIP bei 175 Prozent rund 7 statt unter 1 ms).
    /// </summary>
    internal sealed class Schattenstuecke
    {
        private static readonly Dictionary<(double S, double Ecke, Color Farbe, double Deckung), Schattenstuecke?> Vorrat = [];
        private static readonly Lock Sperre = new();

        // Die acht Stücke um die Mitte: oben links, oben, oben rechts, links, rechts, unten links, unten, unten rechts.
        private readonly BitmapSource?[] _teile = new BitmapSource?[8];
        private readonly int _x0, _y0, _xm, _ym, _b0, _h0, _bild, _hbild;
        private readonly Brush _mitte;

        /// <summary>Wie viele Muster gerastert wurden (für die Tests: neue Größen rastern keines).</summary>
        internal static int Muster { get; private set; }

        /// <summary>Aus nur für die Tests (Vergleich mit den Ringen): dann zeichnet die Blase ihre Ringe wie bis 0.5.0-beta.11.</summary>
        internal static bool An { get; set; } = true;

        private Schattenstuecke(BitmapSource bild, int x0, int y0, int xm, int ym, int b0, int h0, Brush mitte)
        {
            (_x0, _y0, _xm, _ym, _b0, _h0, _mitte) = (x0, y0, xm, ym, b0, h0, mitte);
            (_bild, _hbild) = (bild.PixelWidth, bild.PixelHeight);
            int[] sx = [0, xm - x0, xm - x0 + 1, _bild];
            int[] sy = [0, ym - y0, ym - y0 + 1, _hbild];
            var k = 0;
            for (var zeile = 0; zeile < 3; zeile++)
                for (var spalte = 0; spalte < 3; spalte++)
                {
                    if (zeile == 1 && spalte == 1) continue;
                    var r = new Int32Rect(sx[spalte], sy[zeile], sx[spalte + 1] - sx[spalte], sy[zeile + 1] - sy[zeile]);
                    if (r.Width > 0 && r.Height > 0)
                    {
                        var teil = new CroppedBitmap(bild, r);
                        teil.Freeze();
                        _teile[k] = teil;
                    }
                    k++;
                }
        }

        /// <summary>
        /// Die Stücke für eine Blase von <paramref name="b"/> mal <paramref name="h"/> Pixeln; null, wenn sie zu klein ist (dann
        /// zeichnet sie die Ringe selbst). Gemerkt je Skalierung, Ecke, Farbe und Deckung (höchstens 64).
        /// </summary>
        public static Schattenstuecke? Holen(double s, double ecke, Color farbe, double deckung, double b, double h)
        {
            var r = Glasgeometrie.Raster(Math.Max(0, ecke), s);
            var groesse = 2 * r + 2;
            if (!An || b < groesse || h < groesse) return null;
            var schluessel = (s, ecke, farbe, deckung);
            lock (Sperre)
            {
                if (Vorrat.TryGetValue(schluessel, out var fertig)) return fertig;
                if (Vorrat.Count >= 64) Vorrat.Clear();
                var gruppe = new DrawingGroup();
                foreach (var (form, pinsel) in Ringe(groesse, groesse, s, Zipfelseite.Keine, 0.5, 0, ecke, farbe, deckung))
                    gruppe.Children.Add(new GeometryDrawing(pinsel, null, form));
                gruppe.Freeze();
                Schattenstuecke? neu = null;
                if (Bildraster.Rastern(gruppe, s) is { Bild: BitmapSource bild } z)
                {
                    var x0 = (int)Math.Round(z.Lage.X * s);
                    var y0 = (int)Math.Round(z.Lage.Y * s);
                    var xm = (int)r;
                    var ym = (int)(Glasgeometrie.Raster(Schattentiefe, s) + r);
                    if (x0 < xm && y0 < ym && xm + 1 < x0 + bild.PixelWidth && ym + 1 < y0 + bild.PixelHeight)
                        neu = new Schattenstuecke(bild, x0, y0, xm, ym, (int)groesse, (int)groesse, Pinselwerk.Flaeche(Farbton.Deckend(farbe, deckung)));
                    Muster++;
                }
                Vorrat[schluessel] = neu;
                return neu;
            }
        }

        /// <summary>Legt den Schatten einer Blase von <paramref name="b"/> mal <paramref name="h"/> Pixeln (DIP-Lage aus Pixeln) in <paramref name="ziel"/>.</summary>
        public void Legen(DrawingGroup ziel, double b, double h, double s)
        {
            var dx = (int)b - _b0;
            var dy = (int)h - _h0;
            int[] tx = [_x0, _xm, _xm + dx + 1, _x0 + _bild + dx];
            int[] ty = [_y0, _ym, _ym + dy + 1, _y0 + _hbild + dy];
            var stuecke = new DrawingGroup();
            // Pixel für Pixel setzen und strecken, ohne Glätten.
            RenderOptions.SetBitmapScalingMode(stuecke, BitmapScalingMode.NearestNeighbor);
            Rect Lage(int spalte, int zeile) => new(tx[spalte] / s, ty[zeile] / s, (tx[spalte + 1] - tx[spalte]) / s, (ty[zeile + 1] - ty[zeile]) / s);
            var k = 0;
            for (var zeile = 0; zeile < 3; zeile++)
                for (var spalte = 0; spalte < 3; spalte++)
                {
                    if (zeile == 1 && spalte == 1)
                    {
                        var mitte = new RectangleGeometry(Lage(1, 1));
                        mitte.Freeze();
                        stuecke.Children.Add(new GeometryDrawing(_mitte, null, mitte));
                        continue;
                    }
                    if (_teile[k] is { } teil) stuecke.Children.Add(new ImageDrawing(teil, Lage(spalte, zeile)));
                    k++;
                }
            stuecke.Freeze();
            ziel.Children.Add(stuecke);
        }
    }
}