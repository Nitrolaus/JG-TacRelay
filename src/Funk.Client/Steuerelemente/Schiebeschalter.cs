// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Schiebeschalter mit zwei Stellungen: ein Knauf, der in seiner Spur zwischen links (aus, im Funkfeld "5") und rechts (an,
/// "10") gleitet (A33, Sarevoc: "den 5 oder 10 Knopf weg machen, sondern einen Schiebebutton anfertigen"). Bedienung: Klick
/// schaltet um, Ziehen am Knauf legt ihn dorthin, wo er losgelassen wird (ab der Mitte rechts), die Leertaste schaltet um,
/// Pfeil links und rechts (auch Pos1 und Ende) setzen die Stellung. Die Stellung ist <see cref="ToggleButton.IsChecked"/>,
/// für Bildschirmleser ein Umschalter.
/// <para>
/// Die Vorlage (FunkfeldFenster.xaml, Stil Titel.Schalter) legt die Spur als <c>PART_Spur</c> und den Knauf als
/// <c>PART_Knauf</c> an; der Schalter verschiebt den Knauf um <see cref="KnaufVersatz"/>. <see cref="Kompakt"/> wählt die schmale
/// Form für die Liste: Der Knauf trägt die Zahl, die Beschriftungen an den Enden entfallen.
/// </para>
/// </summary>
[TemplatePart(Name = "PART_Spur", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Knauf", Type = typeof(FrameworkElement))]
public sealed class Schiebeschalter : ToggleButton
{
    public static readonly DependencyProperty KnaufVersatzProperty = DependencyProperty.Register(nameof(KnaufVersatz), typeof(double), typeof(Schiebeschalter),
        new FrameworkPropertyMetadata(0.0, (d, e) => ((Schiebeschalter)d)._verschiebung.X = (double)e.NewValue));

    public static readonly DependencyProperty KompaktProperty = DependencyProperty.Register(nameof(Kompakt), typeof(bool), typeof(Schiebeschalter),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty LinksTextProperty = DependencyProperty.Register(nameof(LinksText), typeof(string), typeof(Schiebeschalter),
        new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty RechtsTextProperty = DependencyProperty.Register(nameof(RechtsText), typeof(string), typeof(Schiebeschalter),
        new FrameworkPropertyMetadata(""));

    // Im Code statt als Bindung in der Vorlage: Ein Transform in einer Vorlage findet ihren Schalter nicht (Bindungsfehler 2).
    private readonly TranslateTransform _verschiebung = new();
    private FrameworkElement? _spur;
    private FrameworkElement? _knauf;
    private double _startX;
    private double _startAnteil;
    private bool _gedrueckt;

    /// <summary>Verschiebung des Knaufs aus der linken Lage, in Einheiten (Vorlage: TranslateTransform).</summary>
    public double KnaufVersatz
    {
        get => (double)GetValue(KnaufVersatzProperty);
        private set => SetValue(KnaufVersatzProperty, value);
    }

    /// <summary>Schmale Form: Zahl im Knauf, keine Beschriftung an den Enden (Liste, 240 breit).</summary>
    public bool Kompakt
    {
        get => (bool)GetValue(KompaktProperty);
        set => SetValue(KompaktProperty, value);
    }

    /// <summary>Beschriftung der linken Stellung, etwa "5".</summary>
    public string LinksText
    {
        get => (string)GetValue(LinksTextProperty);
        set => SetValue(LinksTextProperty, value);
    }

    /// <summary>Beschriftung der rechten Stellung, etwa "10".</summary>
    public string RechtsText
    {
        get => (string)GetValue(RechtsTextProperty);
        set => SetValue(RechtsTextProperty, value);
    }

    /// <summary>Der Knauf wird gerade gezogen (nicht nur geklickt).</summary>
    public bool Zieht { get; private set; }

    /// <summary>Wie weit der Knauf gleiten kann, in Einheiten: sein Platz in der Spur (ohne Rand) weniger ihm selbst.</summary>
    public double Weg => _knauf is null
        ? 0
        : Math.Max(0, LayoutInformation.GetLayoutSlot(_knauf).Width - _knauf.ActualWidth - _knauf.Margin.Left - _knauf.Margin.Right);

    /// <summary>Stellung nach einer Taste: links (falsch) oder rechts (wahr); <c>null</c>, wenn die Taste nichts stellt.</summary>
    public static bool? StellungNachTaste(Key taste) => taste switch
    {
        Key.Left or Key.Home => false,
        Key.Right or Key.End => true,
        _ => null,
    };

    /// <summary>Anteil des Wegs (0 links, 1 rechts), wenn der Knauf von <paramref name="startAnteil"/> um <paramref name="dx"/> gezogen ist.</summary>
    public static double AnteilBeimZiehen(double startAnteil, double dx, double weg) =>
        weg > 0 && double.IsFinite(dx) ? Math.Clamp(startAnteil + (dx / weg), 0, 1) : Math.Clamp(startAnteil, 0, 1);

    /// <summary>Losgelassen bei <paramref name="anteil"/>: ab der Mitte gilt rechts.</summary>
    public static bool StellungNachZiehen(double anteil) => anteil >= 0.5;

    /// <summary>Ab so viel Bewegung ist ein Druck ein Ziehen und kein Klick (Einheiten, wie bei Windows).</summary>
    public static double Ziehschwelle => SystemParameters.MinimumHorizontalDragDistance;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_spur is not null) _spur.SizeChanged -= GroesseGeaendert;
        if (_knauf is not null) _knauf.SizeChanged -= GroesseGeaendert;
        _spur = GetTemplateChild("PART_Spur") as FrameworkElement;
        _knauf = GetTemplateChild("PART_Knauf") as FrameworkElement;
        if (_spur is not null) _spur.SizeChanged += GroesseGeaendert;
        // Auch der Knauf: Wechselt die Ansicht (Kompakt: Knauf 16 statt 12 breit, Spur ohne Innenabstand), ändert sich der Weg,
        // die Spur selbst bleibt aber 30 breit und meldet nichts. Ohne diese Meldung stünde der Knauf auf 10 mit dem Versatz der
        // anderen Ansicht, in der Liste zwei Einheiten über dem Ende der Spur hinaus (gemessen am 02.10.2026).
        if (_knauf is not null) _knauf.SizeChanged += GroesseGeaendert;
        if (_knauf is not null) _knauf.RenderTransform = _verschiebung;
        KnaufSetzen();
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        KnaufSetzen();
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        KnaufSetzen();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (StellungNachTaste(e.Key) is { } stellung)
        {
            IsChecked = stellung;
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ZiehenBeginnt(e.GetPosition(this).X);
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_gedrueckt && IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) ZiehenBis(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!Zieht)
        {
            _gedrueckt = false;
            base.OnMouseLeftButtonUp(e);
            return;
        }
        // Gezogen: kein Klick (der schaltete sonst noch einmal um), die Stellung ergibt sich aus dem Ort des Knaufs.
        e.Handled = true;
        ZiehenEndet();
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (Zieht) ZiehenEndet();
        _gedrueckt = false;
    }

    /// <summary>Maus gedrückt bei <paramref name="x"/> (Einheiten im Schalter); auch für Tests ohne Maus.</summary>
    internal void ZiehenBeginnt(double x)
    {
        _gedrueckt = true;
        Zieht = false;
        _startX = x;
        _startAnteil = IsChecked == true ? 1 : 0;
    }

    /// <summary>Maus bewegt bis <paramref name="x"/>: ab der <see cref="Ziehschwelle"/> folgt der Knauf.</summary>
    internal void ZiehenBis(double x)
    {
        if (!_gedrueckt) return;
        var dx = x - _startX;
        if (!Zieht && Math.Abs(dx) < Ziehschwelle) return;
        Zieht = true;
        KnaufVersatz = AnteilBeimZiehen(_startAnteil, dx, Weg) * Weg;
    }

    /// <summary>Losgelassen nach dem Ziehen: Stellung nach dem Ort des Knaufs.</summary>
    internal void ZiehenEndet()
    {
        var anteil = Weg > 0 ? KnaufVersatz / Weg : _startAnteil;
        Zieht = false;
        _gedrueckt = false;
        IsChecked = StellungNachZiehen(anteil);
        KnaufSetzen();
    }

    private void GroesseGeaendert(object sender, SizeChangedEventArgs e) => KnaufSetzen();

    private void KnaufSetzen()
    {
        if (!Zieht) KnaufVersatz = IsChecked == true ? Weg : 0;
    }
}
