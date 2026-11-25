// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Glasdesign;

/// <summary>Kleine Umrechnungen für Bindungen in den Vorlagen der Stilfamilien 2.</summary>
public static class Glasumrechnung
{
    /// <summary>Verdoppelt eine Zahl (Breite einer Schalterspur aus ihrer Höhe).</summary>
    public static System.Windows.Data.IValueConverter Doppelt { get; } = new Faktor(2);

    /// <summary>
    /// Die Ecke einer Karte aus der Ecke der Bedienteile ihrer Familie: 1,6 mal so groß (0.5.0-beta.7, Glutglas-Feld: aus
    /// Glas.Mass.Glutglas.Ecke 5 werden 8 DIP; bis beta.6 fest 8).
    /// </summary>
    public static System.Windows.Data.IValueConverter Kartenecke { get; } = new Faktor(1.6);

    private sealed class Faktor(double faktor) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d && !double.IsNaN(d) ? d * faktor : DependencyProperty.UnsetValue;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d && !double.IsNaN(d) ? d / faktor : DependencyProperty.UnsetValue;
    }
}

/// <summary>
/// Spur mit rundem Knauf für die Schalter der Stilfamilien 2 (Taktil, Glutglas, Dickglas): Alle Kinder bis auf das letzte
/// füllen die Spur, das letzte ist der Knauf. Er ist rund, so hoch wie die Spur weniger zweimal die Luft, und sitzt aus
/// links, an rechts; die Luft (<see cref="Abstand"/> in DIP, auf ganze Gerätepixel gerundet) ist oben, unten und zur
/// Endseite gleich (Sarevocs Regel vom 02.10.2026, wie <see cref="Schaltergeometrie"/>).
/// </summary>
public sealed class Knaufbahn : Panel
{
    public static readonly DependencyProperty AnProperty = DependencyProperty.Register(
        nameof(An), typeof(bool), typeof(Knaufbahn), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty AbstandProperty = DependencyProperty.Register(
        nameof(Abstand), typeof(double), typeof(Knaufbahn), new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public bool An { get => (bool)GetValue(AnProperty); set => SetValue(AnProperty, value); }
    public double Abstand { get => (double)GetValue(AbstandProperty); set => SetValue(AbstandProperty, value); }

    /// <summary>Knauf in DIP für eine Spur der Größe <paramref name="spur"/> bei <paramref name="skala"/>: ganze Pixel, Luft rundum gleich.</summary>
    public Rect Knauf(Size spur, double skala, bool an)
    {
        var b = Glasgeometrie.Raster(spur.Width, skala);
        var h = Glasgeometrie.Raster(spur.Height, skala);
        var luft = Glasgeometrie.Linie(Abstand, skala);
        var k = Math.Max(1, h - 2 * luft);
        var x = an ? b - luft - k : luft;
        return new Rect(x / skala, luft / skala, k / skala, k / skala);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var s = Glasgeometrie.Skala(this);
        var h = double.IsNaN(Height) ? 26 : Height;
        var b = double.IsNaN(Width) ? Math.Round(h * 2.1) : Width;
        var groesse = new Size(b, h);
        var knauf = Knauf(groesse, s, An).Size;
        for (var i = 0; i < InternalChildren.Count; i++) InternalChildren[i].Measure(i == InternalChildren.Count - 1 ? knauf : groesse);
        return groesse;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var knauf = Knauf(finalSize, Glasgeometrie.Skala(this), An);
        for (var i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(i == InternalChildren.Count - 1 ? knauf : new Rect(finalSize));
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateMeasure();
}

/// <summary>
/// Ein Prozentwert als Text (Wertblase am Taktil-Regler, Anzeige im Taktil-Fortschritt): (Wert - Minimum) durch
/// (Maximum - Minimum), ganzzahlig, mit Leerzeichen vor dem Prozentzeichen. Schrift, Größe und Farbe erbt es wie Text.
/// </summary>
public sealed class Prozenttext : FrameworkElement
{
    public static readonly DependencyProperty MinimumProperty = Zahl(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Zahl(nameof(Maximum), 1);
    public static readonly DependencyProperty WertProperty = Zahl(nameof(Wert), 0);

    /// <summary>Text vor der Zahl, etwa "Lautstärke: "; leer ohne.</summary>
    public static readonly DependencyProperty VorsatzProperty = DependencyProperty.Register(
        nameof(Vorsatz), typeof(string), typeof(Prozenttext), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Prozenttext), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(Prozenttext), new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(Prozenttext), new FrameworkPropertyMetadata(new FontFamily("Segoe UI"), FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Zahl(string name, double vorgabe) => DependencyProperty.Register(
        name, typeof(double), typeof(Prozenttext), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public Prozenttext() => IsHitTestVisible = false;

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Wert { get => (double)GetValue(WertProperty); set => SetValue(WertProperty, value); }
    public string Vorsatz { get => (string)GetValue(VorsatzProperty); set => SetValue(VorsatzProperty, value); }
    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    /// <summary>Der angezeigte Text, etwa "75 %".</summary>
    public string Text
    {
        get
        {
            var spanne = Maximum - Minimum;
            var anteil = spanne > 0 ? Math.Clamp((Wert - Minimum) / spanne, 0, 1) : 0;
            return Vorsatz + Math.Round(anteil * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
        }
    }

    private FormattedText Formatiert() => new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), FontSize, Foreground ?? Brushes.White,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override Size MeasureOverride(Size availableSize)
    {
        var t = Formatiert();
        return new Size(Math.Ceiling(t.WidthIncludingTrailingWhitespace), Math.Ceiling(t.Height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var t = Formatiert();
        drawingContext.DrawText(t, new Point(Math.Round((RenderSize.Width - t.WidthIncludingTrailingWhitespace) / 2), Math.Round((RenderSize.Height - t.Height) / 2)));
    }
}

/// <summary>Skalenstriche unter einer Spur: <see cref="Anzahl"/> Abstände, jeder fünfte Strich länger; Striche auf ganzen Pixeln.</summary>
public sealed class Skalenstriche : FrameworkElement
{
    public static readonly DependencyProperty AnzahlProperty = DependencyProperty.Register(
        nameof(Anzahl), typeof(int), typeof(Skalenstriche), new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PinselProperty = DependencyProperty.Register(
        nameof(Pinsel), typeof(Brush), typeof(Skalenstriche), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Fester Abstand der Striche in DIP (Rillen); über 0 gilt er statt <see cref="Anzahl"/>, alle Striche gleich lang.</summary>
    public static readonly DependencyProperty AbstandProperty = DependencyProperty.Register(
        nameof(Abstand), typeof(double), typeof(Skalenstriche), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public Skalenstriche() => IsHitTestVisible = false;

    public double Abstand { get => (double)GetValue(AbstandProperty); set => SetValue(AbstandProperty, value); }

    public int Anzahl { get => (int)GetValue(AnzahlProperty); set => SetValue(AnzahlProperty, value); }
    public Brush? Pinsel { get => (Brush?)GetValue(PinselProperty); set => SetValue(PinselProperty, value); }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Anzahl < 1 || Pinsel is null) return;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        var strich = Glasgeometrie.Linie(1, s);
        if (Abstand > 0)
        {
            var schritt = Math.Max(strich + 1, Glasgeometrie.Raster(Abstand, s));
            for (var x = 0.0; x + strich <= b; x += schritt) drawingContext.DrawRectangle(Pinsel, null, new Rect(x / s, 0, strich / s, h / s));
            return;
        }
        for (var i = 0; i <= Anzahl; i++)
        {
            var x = Math.Round((b - strich) * i / Anzahl);
            var lang = i % 5 == 0 ? h : Math.Round(h * 0.6);
            drawingContext.DrawRectangle(Pinsel, null, new Rect(x / s, 0, strich / s, lang / s));
        }
    }
}

/// <summary>
/// Trennlinie links an einem Reiter (Stilfamilie Glutglas: Reiter mit Trennern). Gezeichnet nur, wenn das Steuerelement,
/// dessen Vorlage sie trägt, nicht das erste in seinem Panel ist; so stehen Trenner nur zwischen den Reitern. Seit
/// 0.5.0-beta.10 auch waagerecht oben (<see cref="Waagerecht"/>, zwischen Listeneinträgen und Spaltenköpfen) und wahlweise mit
/// einer zweiten Linie in <see cref="Licht"/> direkt daneben (rechts oder darunter), wie eine eingeritzte Fuge.
/// </summary>
public sealed class Trenner : FrameworkElement
{
    public static readonly DependencyProperty PinselProperty = DependencyProperty.Register(
        nameof(Pinsel), typeof(Brush), typeof(Trenner), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Länge der Linie als Anteil der eigenen Höhe (waagerecht: der Breite), mittig.</summary>
    public static readonly DependencyProperty AnteilProperty = DependencyProperty.Register(
        nameof(Anteil), typeof(double), typeof(Trenner), new FrameworkPropertyMetadata(0.6, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Waagerecht an der Oberkante statt senkrecht an der linken Kante (seit 0.5.0-beta.10).</summary>
    public static readonly DependencyProperty WaagerechtProperty = DependencyProperty.Register(
        nameof(Waagerecht), typeof(bool), typeof(Trenner), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eine zweite Linie direkt neben der ersten (rechts davon, waagerecht darunter); null: keine (seit 0.5.0-beta.10).</summary>
    public static readonly DependencyProperty LichtProperty = DependencyProperty.Register(
        nameof(Licht), typeof(Brush), typeof(Trenner), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Die Linie genau ein Gerätepixel breit (Haarlinie) statt ein DIP auf ganze Pixel gerundet (seit 0.5.0-beta.10).</summary>
    public static readonly DependencyProperty HaarlinieProperty = DependencyProperty.Register(
        nameof(Haarlinie), typeof(bool), typeof(Trenner), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Trenner()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => InvalidateVisual();
    }

    public bool Haarlinie { get => (bool)GetValue(HaarlinieProperty); set => SetValue(HaarlinieProperty, value); }

    public Brush? Pinsel { get => (Brush?)GetValue(PinselProperty); set => SetValue(PinselProperty, value); }
    public double Anteil { get => (double)GetValue(AnteilProperty); set => SetValue(AnteilProperty, value); }
    public bool Waagerecht { get => (bool)GetValue(WaagerechtProperty); set => SetValue(WaagerechtProperty, value); }
    public Brush? Licht { get => (Brush?)GetValue(LichtProperty); set => SetValue(LichtProperty, value); }

    /// <summary>Ob das Steuerelement der Vorlage das erste in seinem Panel ist (dann keine Linie).</summary>
    public bool Erster => TemplatedParent is UIElement e && VisualTreeHelper.GetParent(e) is Panel p && p.Children.IndexOf(e) == 0;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Pinsel is null || Erster) return;
        var s = Glasgeometrie.Skala(this);
        var t = Haarlinie ? Glasgeometrie.Haarlinie : Glasgeometrie.Linie(1, s);
        if (Waagerecht)
        {
            var b = Glasgeometrie.Raster(RenderSize.Width, s);
            var lw = Math.Round(b * Math.Clamp(Anteil, 0, 1));
            var x = Math.Round((b - lw) / 2);
            drawingContext.DrawRectangle(Pinsel, null, new Rect(x / s, 0, lw / s, t / s));
            if (Licht is { } licht) drawingContext.DrawRectangle(licht, null, new Rect(x / s, t / s, lw / s, t / s));
            return;
        }
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        var l = Math.Round(h * Math.Clamp(Anteil, 0, 1));
        var y = Math.Round((h - l) / 2);
        drawingContext.DrawRectangle(Pinsel, null, new Rect(0, y / s, t / s, l / s));
        if (Licht is { } seitlich) drawingContext.DrawRectangle(seitlich, null, new Rect(t / s, y / s, t / s, l / s));
    }
}

/// <summary>
/// Acht Ladepunkte im Kreis (Stilfamilie Taktil: der Knopf, solange etwas läuft), nach hinten blasser. Sie kreisen nur auf
/// dem Bildschirm und wenn Windows Animationen erlaubt, wie die <see cref="Lichtkante"/>; im Speicher stehen sie still.
/// </summary>
public sealed class Ladepunkte : FrameworkElement
{
    public static readonly DependencyProperty LaeuftProperty = DependencyProperty.Register(
        nameof(Laeuft), typeof(bool), typeof(Ladepunkte), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Ladepunkte)d).Kreisen()));

    public static readonly DependencyProperty WinkelProperty = DependencyProperty.Register(
        nameof(Winkel), typeof(double), typeof(Ladepunkte), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PinselProperty = DependencyProperty.Register(
        nameof(Pinsel), typeof(Brush), typeof(Ladepunkte), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _hoert;

    public Ladepunkte()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Kreisen();
        Unloaded += (_, _) => Anhalten();
        IsVisibleChanged += (_, _) => Kreisen();
    }

    public bool Laeuft { get => (bool)GetValue(LaeuftProperty); set => SetValue(LaeuftProperty, value); }
    public double Winkel { get => (double)GetValue(WinkelProperty); set => SetValue(WinkelProperty, value); }
    public Brush? Pinsel { get => (Brush?)GetValue(PinselProperty); set => SetValue(PinselProperty, value); }

    private void Kreisen()
    {
        var bildschirm = Animationen.AufDemBildschirm(this);
        if (Laeuft && bildschirm && !_hoert)
        {
            Animationen.Geaendert += AnimationenGeaendert;
            _hoert = true;
        }
        else if ((!Laeuft || !bildschirm) && _hoert)
        {
            Animationen.Geaendert -= AnimationenGeaendert;
            _hoert = false;
        }
        if (!Laeuft || !bildschirm || !Animationen.Erlaubt)
        {
            BeginAnimation(WinkelProperty, null);
            return;
        }
        var kreis = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1000)) { RepeatBehavior = RepeatBehavior.Forever };
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
        if (Dispatcher.CheckAccess()) Kreisen();
        else Dispatcher.BeginInvoke(new Action(Kreisen));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (!Laeuft || Pinsel is null) return;
        var d = Math.Min(RenderSize.Width, RenderSize.Height);
        if (d < 4) return;
        var m = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        var punkt = d * 0.09;
        var r = d / 2 - punkt;
        // Der hellste Punkt steht beim Winkel, die folgenden werden nach hinten blasser (in Achtelschritten).
        var schritt = Math.Floor(Winkel / 45);
        for (var i = 0; i < 8; i++)
        {
            var w = (schritt + i) * Math.PI / 4;
            var p = Pinselwerk.Gedaempft(Pinsel, 0.2 + 0.1 * i);
            drawingContext.DrawEllipse(p, null, new Point(m.X + r * Math.Sin(w), m.Y - r * Math.Cos(w)), punkt, punkt);
        }
    }
}

/// <summary>
/// Eine eingelassene runde Mulde für ein Symbol (Stilfamilie Dickglas, "Knopf mit Symbol"): dunkler Grund, oben innen
/// Schatten, heller Rand, der unten farbig ausläuft (<see cref="Licht"/>). Das Kind steht mittig darin. Seit 0.5.0-beta.7
/// folgen die Farben den Familienfarben statt fest zu sein (<see cref="Farbableitung"/>): der Grund dem Kern
/// (<see cref="Grundfarbe"/>, Vorgabe Glas.Dickglas.Kern.Farbe), die Mitte des Rands dem Licht; Weiß oben und der Schatten
/// innen sind Licht und Schatten. Seit 0.5.0-beta.12 kommt die Vorgabe des Grunds aus einem Vorgabestil (Style-Vorgabe der
/// Mulde), nicht mehr als lokaler Wert aus dem Konstruktor: Ein lokaler Wert überdeckte die Bindung jeder Vorlage
/// (Glas.Knopf.Mulde folgte so dem Dickglas-Kern statt Glas.Grund.Farbe).
/// </summary>
public sealed class Mulde : Decorator
{
    // Vor den Eigenschaften: Statische Felder werden in der Reihenfolge des Texts belegt.
    private static readonly Color VorgabeLicht = Color.FromRgb(0x7A, 0xDF, 0xFF);
    private static readonly Color VorgabeKern = Color.FromRgb(0x07, 0x07, 0x0A);

    public static readonly DependencyProperty LichtProperty = DependencyProperty.Register(
        nameof(Licht), typeof(Color), typeof(Mulde), new FrameworkPropertyMetadata(VorgabeLicht, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Die Farbe, aus der der dunkle Grund der Mulde abgeleitet ist (Vorgabe: Glas.Dickglas.Kern.Farbe der Farbstellung).</summary>
    public static readonly DependencyProperty GrundfarbeProperty = DependencyProperty.Register(
        nameof(Grundfarbe), typeof(Color), typeof(Mulde), new FrameworkPropertyMetadata(VorgabeKern, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Der Vorgabestil: Grundfarbe aus Glas.Dickglas.Kern.Farbe (DynamicResource). Als Vorgabe der Style-Eigenschaft gesetzt,
    /// gilt er, solange niemand einen Stil setzt; seine Werte stehen unter jedem Wert einer Vorlage (TemplatedParent) und jedem
    /// lokalen Wert. Ohne Farbstellung bleibt die Vorgabe der Eigenschaft (#07070A, der Kern in Standard).
    /// </summary>
    internal static Style Vorgabestil { get; } = VorgabestilBauen();

    static Mulde() => StyleProperty.OverrideMetadata(typeof(Mulde), new FrameworkPropertyMetadata(Vorgabestil));

    private static Style VorgabestilBauen()
    {
        var stil = new Style(typeof(Mulde));
        stil.Setters.Add(new Setter(GrundfarbeProperty, new DynamicResourceExtension(Schluessel.DickglasKernFarbe)));
        stil.Seal();
        return stil;
    }

    public Color Licht { get => (Color)GetValue(LichtProperty); set => SetValue(LichtProperty, value); }
    public Color Grundfarbe { get => (Color)GetValue(GrundfarbeProperty); set => SetValue(GrundfarbeProperty, value); }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(constraint);
        return Child?.DesiredSize ?? default;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var d = Glasgeometrie.Raster(Math.Min(RenderSize.Width, RenderSize.Height), s);
        if (d < 6) return;
        var f = new Glasflaeche(Math.Round((Glasgeometrie.Raster(RenderSize.Width, s) - d) / 2), Math.Round((Glasgeometrie.Raster(RenderSize.Height, s) - d) / 2), d, d, d / 2, s);
        var r = d / 2 / s;
        var m = f.Dip(d / 2, d / 2);
        var g = Grundfarbe;
        drawingContext.DrawGeometry(Pinselwerk.Rund(new Point(m.X, m.Y - 0.08 * r), r * 1.2, r * 1.2,
            (Farbableitung.Ableiten(g, VorgabeKern, Color.FromRgb(0x1A, 0x23, 0x38)), 0), (Farbableitung.Ableiten(g, VorgabeKern, Color.FromRgb(0x0A, 0x0F, 0x1C)), 1)), null, f.Form(0));
        var rand = Glasgeometrie.Linie(2.2, s);
        drawingContext.DrawGeometry(Pinselwerk.Senkrecht((Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0),
            (Farbableitung.Ableiten(Licht, VorgabeLicht, Color.FromArgb(0x59, 0xB4, 0xC8, 0xFF)), 0.5), (Farbton.Deckend(Licht, 0.7), 1)), null, f.Ring(0, rand));
        drawingContext.DrawGeometry(Pinselwerk.Senkrecht((Color.FromArgb(0x66, 0, 0, 0), 0), (Color.FromArgb(0, 0, 0, 0), 0.5)), null, f.Ring(rand, 2 * rand));
    }
}
