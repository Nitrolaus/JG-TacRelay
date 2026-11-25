// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Glasdesign;

/// <summary>Das Ergebnis eines <see cref="Dialog"/>: bestätigt oder abgebrochen.</summary>
public sealed class DialogGeschlossenEventArgs(RoutedEvent routedEvent, object source, bool bestaetigt) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>Wahr: bestätigt (Knopf, Enter); falsch: abgebrochen (Knopf, Escape).</summary>
    public bool Bestaetigt { get; } = bestaetigt;

    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ArgumentNullException.ThrowIfNull(genericHandler);
        ((EventHandler<DialogGeschlossenEventArgs>)genericHandler)(genericTarget, this);
    }
}

/// <summary>
/// Dialog (seit 0.5.0-beta.8): eine Bestätigungskarte im Fenster, kein eigenes Window. Titel als Header, Text als Content,
/// darüber wahlweise ein <see cref="Symbol"/>, darunter zwei Knöpfe der Familie (Abbrechen und Bestätigen, Texte über
/// <see cref="AbbrechenText"/> und <see cref="BestaetigenText"/>). Enter bestätigt (der Bestätigen-Knopf ist IsDefault,
/// dazu der Dialog selbst), Escape bricht ab (IsCancel); ein fokussierter Knopf nimmt Enter für sich. Beim Erscheinen steht
/// der Fokus auf Abbrechen, wenn der Dialog <see cref="Gefaehrlich"/> ist, sonst auf Bestätigen; Tab bleibt im Dialog.
/// Beide Wege melden <see cref="Geschlossen"/> mit dem Ergebnis; den Dialog ausblenden tut das Programm. Automation: ein
/// Dialog mit dem Titel als Namen. Stile: Glas.Dialog, Glas.Taktil.Dialog, Glas.Glutglas.Dialog; Vorlagenteile
/// PART_Bestaetigen und PART_Abbrechen. Die Zustandsvorschau (<see cref="Glas.ZustandVorschauProperty"/>) erreicht beide
/// Knöpfe, Fokus aber nur den <see cref="Startknopf"/> (seit 0.5.0-beta.13, Prüfung Glint 0.17.0).
/// </summary>
[TemplatePart(Name = TeilBestaetigen, Type = typeof(ButtonBase))]
[TemplatePart(Name = TeilAbbrechen, Type = typeof(ButtonBase))]
public class Dialog : HeaderedContentControl
{
    public const string TeilBestaetigen = "PART_Bestaetigen";
    public const string TeilAbbrechen = "PART_Abbrechen";

    /// <summary>Ein Zeichen über dem Titel; Keins (Vorgabe) zeigt keines. Im Glutglas ist die Raute ein gefülltes Glas.</summary>
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(Glaszeichen), typeof(Dialog), new FrameworkPropertyMetadata(Glaszeichen.Keins));

    public static readonly DependencyProperty BestaetigenTextProperty = DependencyProperty.Register(
        nameof(BestaetigenText), typeof(object), typeof(Dialog), new FrameworkPropertyMetadata("Bestätigen"));

    public static readonly DependencyProperty AbbrechenTextProperty = DependencyProperty.Register(
        nameof(AbbrechenText), typeof(object), typeof(Dialog), new FrameworkPropertyMetadata("Abbrechen"));

    /// <summary>Gefährlich (Löschen und dergleichen): der Fokus steht beim Erscheinen auf Abbrechen.</summary>
    public static readonly DependencyProperty GefaehrlichProperty = DependencyProperty.Register(
        nameof(Gefaehrlich), typeof(bool), typeof(Dialog), new FrameworkPropertyMetadata(false));

    public static readonly RoutedEvent GeschlossenEvent = EventManager.RegisterRoutedEvent(
        nameof(Geschlossen), RoutingStrategy.Bubble, typeof(EventHandler<DialogGeschlossenEventArgs>), typeof(Dialog));

    public static RoutedUICommand Bestaetigen { get; } = new("Bestätigen", nameof(Bestaetigen), typeof(Dialog));

    public static RoutedUICommand Abbrechen { get; } = new("Abbrechen", nameof(Abbrechen), typeof(Dialog));

    private ButtonBase? _bestaetigen;
    private ButtonBase? _abbrechen;

    static Dialog()
    {
        FocusableProperty.OverrideMetadata(typeof(Dialog), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.TabNavigationProperty.OverrideMetadata(typeof(Dialog), new FrameworkPropertyMetadata(KeyboardNavigationMode.Cycle));
        CommandManager.RegisterClassCommandBinding(typeof(Dialog), new CommandBinding(Bestaetigen, (s, _) => ((Dialog)s).Antworten(true)));
        CommandManager.RegisterClassCommandBinding(typeof(Dialog), new CommandBinding(Abbrechen, (s, _) => ((Dialog)s).Antworten(false)));
    }

    public Dialog()
    {
        Loaded += (_, _) => SpaeterFokussieren();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) SpaeterFokussieren();
        };
    }

    public Glaszeichen Symbol { get => (Glaszeichen)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public object BestaetigenText { get => GetValue(BestaetigenTextProperty); set => SetValue(BestaetigenTextProperty, value); }
    public object AbbrechenText { get => GetValue(AbbrechenTextProperty); set => SetValue(AbbrechenTextProperty, value); }
    public bool Gefaehrlich { get => (bool)GetValue(GefaehrlichProperty); set => SetValue(GefaehrlichProperty, value); }

    public event EventHandler<DialogGeschlossenEventArgs> Geschlossen
    {
        add => AddHandler(GeschlossenEvent, value);
        remove => RemoveHandler(GeschlossenEvent, value);
    }

    /// <summary>Der Knopf, der beim Erscheinen den Fokus bekommt: Abbrechen, wenn gefährlich, sonst Bestätigen.</summary>
    public ButtonBase? Startknopf => Gefaehrlich ? _abbrechen : _bestaetigen;

    /// <summary>Meldet <see cref="Geschlossen"/> mit dem Ergebnis (das tun auch die Befehle und Enter und Escape).</summary>
    public void Antworten(bool bestaetigt) => RaiseEvent(new DialogGeschlossenEventArgs(GeschlossenEvent, this, bestaetigt));

    /// <summary>Setzt den Tastaturfokus auf den <see cref="Startknopf"/>; falsch, wenn das nicht ging (etwa ohne Fenster).</summary>
    public bool Fokussieren() => Startknopf?.Focus() ?? false;

    private void SpaeterFokussieren() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Fokussieren()));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _bestaetigen = GetTemplateChild(TeilBestaetigen) as ButtonBase;
        _abbrechen = GetTemplateChild(TeilAbbrechen) as ButtonBase;
        if (_bestaetigen is Button b) b.IsDefault = true;
        if (_abbrechen is Button a) a.IsCancel = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Escape)
        {
            Antworten(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Antworten(true);
            e.Handled = true;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Dialogflaeche(this);
}

/// <summary>Automation des <see cref="Dialog"/>: ein Dialog (IsDialog) mit dem Titel als Namen.</summary>
internal sealed class Dialogflaeche(Dialog dialog) : FrameworkElementAutomationPeer(dialog)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

    protected override string GetLocalizedControlTypeCore() => "Dialog";

    protected override string GetClassNameCore() => "Dialog";

    protected override bool IsDialogCore() => true;

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? Rueckmeldungstext.Von(((Dialog)Owner).Header) : name;
    }
}

/// <summary>
/// Bronzefassung um eine Karte der Stilfamilie Taktil (Dialog im Vorbild): ein Ring von <see cref="Breite"/> DIP (ganze
/// Gerätepixel) innen an der Form, oben heller, unten dunkler, aus <see cref="Farbe"/> (in den Stilen
/// Glas.Taktil.Fassung.Farbe); innen eine feine dunkle Fuge. Je Größe und Werten einmal gebaut und eingefroren.
/// </summary>
public sealed class Fassungsrand : FrameworkElement
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Color), typeof(Fassungsrand), new FrameworkPropertyMetadata(Color.FromRgb(0x3A, 0x33, 0x2E), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BreiteProperty = DependencyProperty.Register(
        nameof(Breite), typeof(double), typeof(Fassungsrand), new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(double), typeof(Fassungsrand), new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    public Fassungsrand()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Color Farbe { get => (Color)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public double Breite { get => (double)GetValue(BreiteProperty); set => SetValue(BreiteProperty, value); }
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }

    internal int Bauten { get; private set; }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Farbe, Breite, Ecke);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            _schichten.Clear();
            var b = Glasgeometrie.Raster(RenderSize.Width, s);
            var h = Glasgeometrie.Raster(RenderSize.Height, s);
            if (b >= 4 && h >= 4)
            {
                var r = Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), Math.Min(b, h) / 2);
                var f = new Glasflaeche(0, 0, b, h, r, s);
                var w = Glasgeometrie.Linie(Breite, s);
                var fa = Farbe;
                var unten = h / s;
                Hinzu(f.Ring(0, w), Verlauf(unten, (Farbton.Mischen(fa, Colors.White, 0.32), 0), (Farbton.Mischen(fa, Colors.White, 0.08), 0.3), (fa, 0.6), (Farbton.Mischen(fa, Colors.Black, 0.35), 1)));
                Hinzu(f.Ring(0, Glasgeometrie.Haarlinie), Verlauf(unten, (Farbton.Deckend(Colors.White, 0.22), 0), (Farbton.Deckend(Colors.White, 0.04), 0.5), (Farbton.Deckend(Colors.Black, 0.3), 1)));
                Hinzu(f.Ring(w, w + Glasgeometrie.Haarlinie), Verlauf(unten, (Farbton.Deckend(Colors.Black, 0.55), 0), (Farbton.Deckend(Colors.Black, 0.3), 1)));
            }
            Bauten++;
        }
        foreach (var (form, pinsel) in _schichten) drawingContext.DrawGeometry(pinsel, null, form);
    }

    private void Hinzu(Geometry form, Brush pinsel) => _schichten.Add((form, pinsel));

    private static LinearGradientBrush Verlauf(double unten, params (Color Farbe, double Lage)[] stopps)
    {
        var p = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, unten) };
        foreach (var (farbe, lage) in stopps) p.GradientStops.Add(new GradientStop(farbe, lage));
        p.Freeze();
        return p;
    }
}

/// <summary>
/// Die Raute über dem Glutglas-Dialog (Vorbild 3): ein gefülltes Glas in kühlem Licht (<see cref="Farbe"/>, in den Stilen
/// Glas.Glutglas.Symbol.Farbe), oben links hell, unten rechts satt, die linke Facette heller, eine feine helle Kante und ein
/// weicher Schein um sie (nur mit Schatten, <see cref="Glas.SchattenProperty"/>). Absolut mittig auf ganzen Gerätepixeln;
/// je Größe und Farbe einmal gebaut.
/// </summary>
public sealed class Glutraute : FrameworkElement
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Color), typeof(Glutraute), new FrameworkPropertyMetadata(Color.FromRgb(0x5A, 0xB0, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Glutraute), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    public Glutraute()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Color Farbe { get => (Color)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }

    internal int Bauten { get; private set; }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(30, availableSize.Width), Math.Min(30, availableSize.Height));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Farbe, Schatten);
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
        var bc = Glasgeometrie.Raster(RenderSize.Width, s);
        var hc = Glasgeometrie.Raster(RenderSize.Height, s);
        var d = Math.Min(bc, hc);
        if (d < 6) return;
        // Gleiche Parität wie der Behälter: Rest links gleich rechts, oben gleich unten.
        var dx = d - (bc - d) % 2;
        var dy = d - (hc - d) % 2;
        var x0 = (bc - dx) / 2;
        var y0 = (hc - dy) / 2;
        double mx = x0 + dx / 2.0, my = y0 + dy / 2.0;
        Point P(double x, double y) => new(x / s, y / s);
        var c = Farbe;
        if (Schatten != Schattenstufe.Aus)
        {
            var schein = new EllipseGeometry(P(mx, my), dx * 0.62 / s, dy * 0.62 / s);
            schein.Freeze();
            _schichten.Add((schein, Pinselwerk.Rund(P(mx, my), dx * 0.62 / s, dy * 0.62 / s, (Farbton.Deckend(c, 0.5), 0), (Farbton.Deckend(c, 0.16), 0.55), (Farbton.Deckend(c, 0), 1))));
        }
        var raute = Vieleck(s, (mx, y0), (x0 + dx, my), (mx, y0 + dy), (x0, my));
        _schichten.Add((raute, Pinselwerk.Verlauf(P(x0 + dx * 0.25, y0 + dy * 0.2), P(x0 + dx * 0.8, y0 + dy * 0.85),
            Farbton.Mischen(c, Colors.White, 0.78), Farbton.Mischen(c, Colors.Black, 0.28))));
        // Die linke obere Facette heller, die rechte untere satter: Licht von oben links.
        _schichten.Add((Vieleck(s, (mx, y0), (mx, my), (x0, my)), Pinselwerk.Flaeche(Farbton.Deckend(Colors.White, 0.28))));
        _schichten.Add((Vieleck(s, (x0 + dx, my), (mx, y0 + dy), (mx, my)), Pinselwerk.Flaeche(Farbton.Deckend(Colors.Black, 0.16))));
        var kante = new Pen(Pinselwerk.Flaeche(Farbton.Deckend(Colors.White, 0.55)), 1 / s) { LineJoin = PenLineJoin.Miter };
        kante.Freeze();
        var umriss = Vieleck(s, (mx, y0 + 0.5), (x0 + dx - 0.5, my), (mx, y0 + dy - 0.5), (x0 + 0.5, my));
        var rand = umriss.GetWidenedPathGeometry(kante);
        rand.Freeze();
        _schichten.Add((rand, Pinselwerk.Flaeche(Farbton.Deckend(Colors.White, 0.55))));
    }

    private static PathGeometry Vieleck(double s, params (double X, double Y)[] punkte)
    {
        var f = new PathFigure { StartPoint = new Point(punkte[0].X / s, punkte[0].Y / s), IsClosed = true, IsFilled = true };
        foreach (var (x, y) in punkte.Skip(1)) f.Segments.Add(new LineSegment(new Point(x / s, y / s), true));
        var g = new PathGeometry([f]);
        g.Freeze();
        return g;
    }
}
