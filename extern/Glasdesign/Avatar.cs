// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>Status einer Person am <see cref="Avatar"/>: kein Punkt, anwesend, abwesend, beschäftigt, abgemeldet.</summary>
public enum Avatarstatus
{
    Keiner,
    Anwesend,
    Abwesend,
    Beschaeftigt,
    Abgemeldet,
}

/// <summary>Wo ein Kind in der <see cref="Avatarlage"/> sitzt.</summary>
public enum Avatarteil
{
    /// <summary>Der ganze Kreis.</summary>
    Voll,

    /// <summary>Der Kreis innerhalb des Rings (Fläche, Bild, Initialen).</summary>
    Kern,

    /// <summary>Der Statuspunkt unten rechts auf dem Rand.</summary>
    Punkt,
}

/// <summary>
/// Avatar (seit 0.5.0-beta.9, Stilfamilien 2 Stufe 2): eine Person als Kreis mit Initialen (<see cref="Initialen"/>, aus
/// <see cref="Personenname"/> gebildet, wenn nicht gesetzt: die ersten Buchstaben der ersten beiden Wörter) oder einem Bild,
/// das das Programm selbst setzt (<see cref="Bild"/>, rund beschnitten; die Bibliothek bringt keines mit). Unten rechts ein
/// Statuspunkt (<see cref="Status"/>) in der Statusfarbe mit einem Ring in der Grundfarbe (<see cref="Statusrand"/>). Größe
/// über Width und Height (Vorgabe Glas.Mass.Avatar.Groesse). Eine Anzeige, nicht fokussierbar; Automation: ein Bild "Jana
/// Dorn, anwesend". Stile: Glas.Avatar (runder Glaskörper), Glas.Taktil.Avatar (Metallring, dunkle Fläche) und
/// Glas.Glutglas.Avatar (kräftiger Verlauf mit hellem Ring).
/// </summary>
public class Avatar : Control
{
    /// <summary>Der Name der Person (FrameworkElement.Name ist der Name des Elements, darum ein eigener).</summary>
    public static readonly DependencyProperty PersonennameProperty = DependencyProperty.Register(
        nameof(Personenname), typeof(string), typeof(Avatar), new FrameworkPropertyMetadata(null, (d, _) => d.CoerceValue(InitialenProperty)));

    /// <summary>Die Initialen; nicht gesetzt (oder leer): aus <see cref="Personenname"/> gebildet (<see cref="Bilden"/>).</summary>
    public static readonly DependencyProperty InitialenProperty = DependencyProperty.Register(
        nameof(Initialen), typeof(string), typeof(Avatar),
        new FrameworkPropertyMetadata(null, null, (d, v) => v is string { Length: > 0 } s ? s : Bilden(((Avatar)d).Personenname)));

    /// <summary>Ein eigenes Bild statt der Initialen, rund beschnitten.</summary>
    public static readonly DependencyProperty BildProperty = DependencyProperty.Register(
        nameof(Bild), typeof(ImageSource), typeof(Avatar), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(Avatarstatus), typeof(Avatar), new FrameworkPropertyMetadata(Avatarstatus.Keiner));

    /// <summary>Die Farbe des Statuspunkts; die Stile setzen sie je Status aus Glas.Status.*.</summary>
    public static readonly DependencyProperty StatusfarbeProperty = DependencyProperty.Register(
        nameof(Statusfarbe), typeof(Brush), typeof(Avatar), new FrameworkPropertyMetadata(null));

    /// <summary>Der Ring um den Statuspunkt in der Grundfarbe, auf der der Avatar steht (er trennt den Punkt vom Kreis).</summary>
    public static readonly DependencyProperty StatusrandProperty = DependencyProperty.Register(
        nameof(Statusrand), typeof(Brush), typeof(Avatar), new FrameworkPropertyMetadata(null));

    /// <summary>Größe der Initialen als Anteil des Durchmessers.</summary>
    public static readonly DependencyProperty SchriftanteilProperty = DependencyProperty.Register(
        nameof(Schriftanteil), typeof(double), typeof(Avatar), new FrameworkPropertyMetadata(0.38, (d, _) => ((Avatar)d).Groesse()));

    private static readonly DependencyPropertyKey InitialengroesseKey = DependencyProperty.RegisterReadOnly(
        nameof(Initialengroesse), typeof(double), typeof(Avatar), new FrameworkPropertyMetadata(15d));

    /// <summary>Die Schriftgröße der Initialen (DIP): Durchmesser mal <see cref="Schriftanteil"/>.</summary>
    public static readonly DependencyProperty InitialengroesseProperty = InitialengroesseKey.DependencyProperty;

    static Avatar()
    {
        FocusableProperty.OverrideMetadata(typeof(Avatar), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Avatar), new FrameworkPropertyMetadata(false));
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == WidthProperty || e.Property == HeightProperty) Groesse();
    }

    public string? Personenname { get => (string?)GetValue(PersonennameProperty); set => SetValue(PersonennameProperty, value); }
    public string? Initialen { get => (string?)GetValue(InitialenProperty); set => SetValue(InitialenProperty, value); }
    public ImageSource? Bild { get => (ImageSource?)GetValue(BildProperty); set => SetValue(BildProperty, value); }
    public Avatarstatus Status { get => (Avatarstatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public Brush? Statusfarbe { get => (Brush?)GetValue(StatusfarbeProperty); set => SetValue(StatusfarbeProperty, value); }
    public Brush? Statusrand { get => (Brush?)GetValue(StatusrandProperty); set => SetValue(StatusrandProperty, value); }
    public double Schriftanteil { get => (double)GetValue(SchriftanteilProperty); set => SetValue(SchriftanteilProperty, value); }
    public double Initialengroesse => (double)GetValue(InitialengroesseProperty);

    /// <summary>
    /// Die Initialen zu einem Namen: die ersten Buchstaben der ersten beiden Wörter, groß ("Jana Dorn" wird "JD"). Seit
    /// 0.5.0-beta.13 zählen nur Buchstaben und Ziffern: Klammern und Zeichen davor überspringt es ("(Gast)" wird "G"), ein Wort
    /// ganz ohne Buchstaben oder Ziffern zählt nicht.
    /// </summary>
    public static string Bilden(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var woerter = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(woerter.Select(ErstesZeichen).Where(z => z.Length > 0).Take(2).Select(z => z.ToUpper(CultureInfo.CurrentCulture)));
    }

    /// <summary>Das erste Textelement eines Worts, das mit einem Buchstaben oder einer Ziffer beginnt; sonst leer.</summary>
    private static string ErstesZeichen(string wort)
    {
        var e = StringInfo.GetTextElementEnumerator(wort);
        while (e.MoveNext())
            if (e.GetTextElement() is { Length: > 0 } z && Rune.TryGetRuneAt(z, 0, out var r) && Rune.IsLetterOrDigit(r)) return z;
        return "";
    }

    /// <summary>Der Status in Worten: anwesend, abwesend, beschäftigt, abgemeldet; Keiner leer.</summary>
    public static string Statustext(Avatarstatus status) => status switch
    {
        Avatarstatus.Anwesend => "anwesend",
        Avatarstatus.Abwesend => "abwesend",
        Avatarstatus.Beschaeftigt => "beschäftigt",
        Avatarstatus.Abgemeldet => "abgemeldet",
        _ => "",
    };

    /// <summary>"Jana Dorn, anwesend" (ohne Namen die Initialen, ohne Status nur der Name).</summary>
    public string Beschreibung
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Personenname) ? Initialen ?? "" : Personenname.Trim();
            var status = Statustext(Status);
            return status.Length == 0 ? name : name.Length == 0 ? status : name + ", " + status;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Groesse();
    }

    /// <summary>Die Schriftgröße der Initialen aus dem Durchmesser (Width und Height, sonst die angeordnete Größe).</summary>
    private void Groesse()
    {
        var d = !double.IsNaN(Width) && !double.IsNaN(Height) ? Math.Min(Width, Height) : Math.Min(ActualWidth, ActualHeight);
        if (d <= 0 || double.IsNaN(d) || double.IsInfinity(d)) return;
        var g = Math.Max(1, Math.Round(d * Schriftanteil * 2) / 2);
        if (g != Initialengroesse) SetValue(InitialengroesseKey, g);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Avatarflaeche(this);
}

/// <summary>Automation des <see cref="Avatar"/>: ein Bild mit Name und Status ("Jana Dorn, anwesend").</summary>
internal sealed class Avatarflaeche(Avatar avatar) : FrameworkElementAutomationPeer(avatar)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

    protected override string GetClassNameCore() => nameof(Avatar);

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? avatar.Beschreibung : name;
    }

    protected override string GetItemStatusCore() => Avatar.Statustext(avatar.Status);

    protected override bool IsControlElementCore() => true;
}

/// <summary>
/// Panel des <see cref="Avatar"/>: ein Quadrat mittig im Platz (ganze Gerätepixel), darin die Kinder nach
/// <see cref="TeilProperty"/>: Voll den ganzen Kreis, Kern den Kreis innerhalb des Rings (<see cref="Ringanteil"/> des
/// Durchmessers, mindestens <see cref="Mindestrand"/> Pixel), Punkt den Statuspunkt (<see cref="Punktanteil"/> des
/// Durchmessers), dessen Mitte unten rechts auf dem Kreis unter 45 Grad liegt.
/// </summary>
public sealed class Avatarlage : Panel
{
    public static readonly DependencyProperty TeilProperty = DependencyProperty.RegisterAttached(
        "Teil", typeof(Avatarteil), typeof(Avatarlage), new FrameworkPropertyMetadata(Avatarteil.Voll, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty RinganteilProperty = DependencyProperty.Register(
        nameof(Ringanteil), typeof(double), typeof(Avatarlage), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty MindestrandProperty = DependencyProperty.Register(
        nameof(Mindestrand), typeof(int), typeof(Avatarlage), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty PunktanteilProperty = DependencyProperty.Register(
        nameof(Punktanteil), typeof(double), typeof(Avatarlage), new FrameworkPropertyMetadata(0.28, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static Avatarteil GetTeil(DependencyObject element) => (Avatarteil)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(TeilProperty);

    public static void SetTeil(DependencyObject element, Avatarteil wert) => (element ?? throw new ArgumentNullException(nameof(element))).SetValue(TeilProperty, wert);

    public double Ringanteil { get => (double)GetValue(RinganteilProperty); set => SetValue(RinganteilProperty, value); }
    public int Mindestrand { get => (int)GetValue(MindestrandProperty); set => SetValue(MindestrandProperty, value); }
    public double Punktanteil { get => (double)GetValue(PunktanteilProperty); set => SetValue(PunktanteilProperty, value); }

    /// <summary>Kreis, Kern und Punkt in Gerätepixeln für einen Platz von <paramref name="breitePx"/> mal <paramref name="hoehePx"/>.</summary>
    public static (Rect Kreis, Rect Kern, Rect Punkt) Lagen(double breitePx, double hoehePx, double ringanteil, int mindestrand, double punktanteil)
    {
        var d = Math.Min(breitePx, hoehePx);
        var x = Math.Floor((breitePx - d) / 2);
        var y = Math.Floor((hoehePx - d) / 2);
        var r = Math.Max(mindestrand, ringanteil > 0 ? Math.Max(1, Math.Round(d * ringanteil)) : 0);
        r = Math.Min(r, Math.Floor(d / 2) - 1);
        var p = Math.Max(4, Math.Round(d * punktanteil));
        var mitte = d / 2 * (1 + Math.Sqrt(0.5));
        var lage = Math.Min(d - p, Math.Round(mitte - p / 2));
        return (new Rect(x, y, d, d), new Rect(x + r, y + r, Math.Max(0, d - 2 * r), Math.Max(0, d - 2 * r)), new Rect(x + lage, y + lage, p, p));
    }

    private Rect Teilrect(UIElement kind, Size groesse, double s)
    {
        var (kreis, kern, punkt) = Lagen(Glasgeometrie.Raster(groesse.Width, s), Glasgeometrie.Raster(groesse.Height, s), Ringanteil, Mindestrand, Punktanteil);
        var r = GetTeil(kind) switch
        {
            Avatarteil.Kern => kern,
            Avatarteil.Punkt => punkt,
            _ => kreis,
        };
        return new Rect(r.X / s, r.Y / s, r.Width / s, r.Height / s);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 40 : availableSize.Width;
        var h = double.IsInfinity(availableSize.Height) ? 40 : availableSize.Height;
        var groesse = new Size(Math.Min(w, h), Math.Min(w, h));
        var s = Glasgeometrie.Skala(this);
        foreach (UIElement kind in InternalChildren) kind.Measure(Teilrect(kind, groesse, s).Size);
        return groesse;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = Glasgeometrie.Skala(this);
        foreach (UIElement kind in InternalChildren) kind.Arrange(Teilrect(kind, finalSize, s));
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateMeasure();
}

/// <summary>
/// Ein Bild rund beschnitten (das eigene Bild des Avatars): als Pinsel (UniformToFill) auf einem Kreis in ganzen
/// Gerätepixeln; gesperrt mit halber Deckung im Pinsel. Je Bild, Größe und Zustand einmal gebaut; eingefroren nur, wenn das
/// Bild es zulässt (ein Bild, das noch lädt, bleibt ungefroren und erscheint, sobald es da ist).
/// </summary>
public sealed class Avatarbild : FrameworkElement
{
    public static readonly DependencyProperty BildProperty = DependencyProperty.Register(
        nameof(Bild), typeof(ImageSource), typeof(Avatarbild), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private (Geometry Form, Brush Pinsel)? _schicht;

    static Avatarbild()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Avatarbild), new UIPropertyMetadata(true, (d, _) => ((UIElement)d).InvalidateVisual()));
    }

    public Avatarbild()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public ImageSource? Bild { get => (ImageSource?)GetValue(BildProperty); set => SetValue(BildProperty, value); }

    internal int Bauten { get; private set; }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Bild, IsEnabled);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            _schicht = null;
            var d = Math.Min(Glasgeometrie.Raster(RenderSize.Width, s), Glasgeometrie.Raster(RenderSize.Height, s));
            if (Bild is { } bild && d >= 2)
            {
                var kreis = new EllipseGeometry(new Rect(0, 0, d / s, d / s));
                kreis.Freeze();
                var pinsel = new ImageBrush(bild) { Stretch = Stretch.UniformToFill, Opacity = IsEnabled ? 1 : 0.45 };
                // Nur einfrieren, was sich einfrieren lässt (seit 0.5.0-beta.13): Ein Bild, das noch aus dem Netz lädt, oder eine
                // Zeichnung mit gebundenem Pinsel lässt sich nicht einfrieren; Freeze warf dann im Render-Durchgang. Ungefroren
                // zeichnet der Pinsel das Bild nach, sobald es geladen ist.
                if (pinsel.CanFreeze) pinsel.Freeze();
                _schicht = (kreis, pinsel);
            }
            Bauten++;
        }
        if (_schicht is { } z) drawingContext.DrawGeometry(z.Pinsel, null, z.Form);
    }
}

/// <summary>
/// Der Statuspunkt des <see cref="Avatar"/>: ein Kreis in <see cref="Farbe"/> mit einem Ring in <see cref="Ring"/> (ganze
/// Gerätepixel, ein Fünftel des Punkts, mindestens einer), innen oben ein Hauch Licht. Je Größe und Farben einmal gebaut.
/// </summary>
public sealed class Statuspunkt : FrameworkElement
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Statuspunkt), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingProperty = DependencyProperty.Register(
        nameof(Ring), typeof(Brush), typeof(Statuspunkt), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    public Statuspunkt()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Brush? Ring { get => (Brush?)GetValue(RingProperty); set => SetValue(RingProperty, value); }

    internal int Bauten { get; private set; }

    /// <summary>Breite des Rings in Pixeln für einen Punkt von <paramref name="durchmesserPx"/>.</summary>
    public static double Ringbreite(double durchmesserPx) => Math.Max(1, Math.Round(durchmesserPx * 0.2));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Farbe, Ring);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            _schichten.Clear();
            Bauen(s);
            Bauten++;
        }
        foreach (var (form, pinsel) in _schichten) drawingContext.DrawGeometry(pinsel, null, form);
    }

    private void Bauen(double s)
    {
        var d = Math.Min(Glasgeometrie.Raster(RenderSize.Width, s), Glasgeometrie.Raster(RenderSize.Height, s));
        if (d < 3 || Farbe is not { } farbe) return;
        EllipseGeometry Kreis(double einzug)
        {
            var g = new EllipseGeometry(new Rect(einzug / s, einzug / s, Math.Max(0, d - 2 * einzug) / s, Math.Max(0, d - 2 * einzug) / s));
            g.Freeze();
            return g;
        }
        var r = Ringbreite(d);
        if (Ring is { } ring) _schichten.Add((Kreis(0), ring));
        var c = Pinselwerk.Ton(farbe, Colors.Gray);
        var innen = d - 2 * r;
        _schichten.Add((Kreis(r), Pinselwerk.Verlauf(new Point(0, r / s), new Point(0, (r + innen) / s), Farbton.Mischen(c, Colors.White, 0.18), Farbton.Mischen(c, Colors.Black, 0.12))));
    }
}

/// <summary>
/// Die Scheibe des Glutglas-Avatars (Vorbild 3, Karte mit Avatar): ein kräftiger Verlauf aus <see cref="Farbe"/> (zwei
/// Stopps: oben, unten), ein heller Ring (<see cref="Ringanteil"/> des Durchmessers, ganze Gerätepixel, aus der Farbe zum Weiß
/// gemischt) und oben ein weicher Glanz. Gesperrt flach und zum Grau gemischt. Schriftwahl für die Initialen über die Farben
/// im Schriftband (<see cref="Familienkoerper"/>).
/// </summary>
public sealed class Avatarscheibe : Familienkoerper
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Avatarscheibe), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RinganteilProperty = DependencyProperty.Register(
        nameof(Ringanteil), typeof(double), typeof(Avatarscheibe), new FrameworkPropertyMetadata(0.045, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Ersatz ohne Farbe: das Violett des Vorbilds (Glas.Glutglas.Symbol.Farbe abgeleitet, Standard).</summary>
    private static readonly Color ErsatzOben = Color.FromRgb(0x5E, 0x56, 0xB8);

    private static readonly Color ErsatzUnten = Color.FromRgb(0x42, 0x4C, 0x98);

    /// <summary>Wie stark der Glanz die Scheibe im Schriftband höchstens aufhellt (für die Lesbarkeitsprüfung).</summary>
    private const double Glanzspielraum = 0.08;

    private static readonly Color Gesperrtgrau = Color.FromRgb(0x30, 0x32, 0x38);

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public double Ringanteil { get => (double)GetValue(RinganteilProperty); set => SetValue(RinganteilProperty, value); }

    private (Color Oben, Color Unten) Stopps()
    {
        var oben = FarbeVon(Farbe, ErsatzOben);
        var unten = Zweitfarbe(Farbe, Farbe is null ? ErsatzUnten : oben);
        // Wie beim Abzeichen: Hält auf dem Verlauf weder Weiß (an der hellsten Stelle, samt Glanz) noch fast Schwarz (an der
        // dunkelsten) 4,5 zu 1, wird er gleichmäßig abgedunkelt, bis Weiß hält.
        var t = 0.0;
        while (t < 1 && !Abzeichen.Haelt(Dunkler(oben, t), Dunkler(unten, t), Glanzspielraum)) t += 0.02;
        if (t > 0) (oben, unten) = (Dunkler(oben, t), Dunkler(unten, t));
        if (Zustand != Glaszustand.Gesperrt) return (oben, unten);
        return (Farbton.Mischen(oben, Gesperrtgrau, 0.7), Farbton.Mischen(unten, Gesperrtgrau, 0.7));
    }

    private double Glanz => Zustand == Glaszustand.Gesperrt ? 0 : 0.22;

    private protected override object Teilschluessel() => (Stopps(), Ringanteil);

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        var unten = f.Hoehe / s;
        var (o, u) = Stopps();
        var ring = Math.Max(1, Math.Round(Math.Min(f.Breite, f.Hoehe) * Math.Max(0, Ringanteil)));
        Schicht(f.Form(0), Senkrecht(0, unten, (o, 0), (u, 1)));
        if (Glanz > 0)
            Schicht(f.Form(ring), Senkrecht(0, unten, (Weiss(Glanz), 0), (Weiss(Glanz * 0.25), 0.3), (Weiss(0), 0.48)));
        var hell = Zustand == Glaszustand.Gesperrt ? 0.3 : 0.62;
        Schicht(f.Ring(0, ring), Senkrecht(0, unten, (Farbton.Mischen(o, Colors.White, hell), 0), (Farbton.Mischen(u, Colors.White, hell * 0.75), 1)));
    }

    private protected override IReadOnlyList<Color> Flaechenfarben()
    {
        var (o, u) = Stopps();
        var glanz = Glanz;
        // Die Initialen sind groß: das Band reicht von 28 bis 72 Prozent der Höhe.
        return Band(t =>
        {
            var grund = Verlaufsfarbe(t, (o, 0), (u, 1));
            return glanz > 0 ? Schriftwahl.Ueber(Verlaufsfarbe(t, (Weiss(glanz), 0), (Weiss(glanz * 0.25), 0.3), (Weiss(0), 0.48)), grund) : grund;
        }, 0.28, 0.72);
    }
}
