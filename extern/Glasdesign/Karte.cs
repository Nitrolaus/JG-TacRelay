// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Karte (seit 0.5.0-beta.10, Stilfamilien 2 Stufe 2): eine Fläche mit Titel (Header) und Inhalt, darüber wahlweise ein
/// <see cref="Kopfbild"/> (etwa ein <see cref="Bildplatzhalter"/> oder ein <see cref="Avatar"/>), darunter wahlweise
/// <see cref="Aktionen"/> (etwa Knöpfe) und unten rechts eine <see cref="Zahl"/>. Mit <see cref="Zurueck"/> steht vor dem
/// Titel ein Winkel nach links als echter Knopf (fokussierbar, Enter und Leertaste), der den Befehl
/// <see cref="Zurueckgehen"/> auslöst; die Karte meldet ihn als <see cref="Zurueckgegangen"/>. Der Befehl heißt nicht
/// "Zurueck", weil die Eigenschaft so heißt. Automation: eine Gruppe mit dem Titel als Namen. Stile: Glas.Inhaltskarte
/// (Glas 4), Glas.Taktil.Karte, Glas.Glutglas.Karte und Glas.Dickglas.Karte; Vorlagenteil PART_Zurueck.
/// </summary>
[TemplatePart(Name = TeilZurueck, Type = typeof(ButtonBase))]
public class Karte : HeaderedContentControl
{
    public const string TeilZurueck = "PART_Zurueck";

    /// <summary>Oben über dem Titel (Bildplatzhalter, Avatar, eigenes Bild).</summary>
    public static readonly DependencyProperty KopfbildProperty = DependencyProperty.Register(
        nameof(Kopfbild), typeof(object), typeof(Karte), new FrameworkPropertyMetadata(null));

    /// <summary>Unten links (etwa ein Knopf oder eine Reihe Knöpfe).</summary>
    public static readonly DependencyProperty AktionenProperty = DependencyProperty.Register(
        nameof(Aktionen), typeof(object), typeof(Karte), new FrameworkPropertyMetadata(null));

    /// <summary>Unten rechts (etwa eine Anzahl).</summary>
    public static readonly DependencyProperty ZahlProperty = DependencyProperty.Register(
        nameof(Zahl), typeof(object), typeof(Karte), new FrameworkPropertyMetadata(null));

    /// <summary>Ein Zurück-Knopf (Winkel nach links) vor dem Titel.</summary>
    public static readonly DependencyProperty ZurueckProperty = DependencyProperty.Register(
        nameof(Zurueck), typeof(bool), typeof(Karte), new FrameworkPropertyMetadata(false));

    public static readonly RoutedEvent ZurueckgegangenEvent = EventManager.RegisterRoutedEvent(
        nameof(Zurueckgegangen), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Karte));

    /// <summary>Der Befehl des Zurück-Knopfs; die Karte meldet ihn als <see cref="Zurueckgegangen"/>.</summary>
    public static RoutedUICommand Zurueckgehen { get; } = new("Zurück", nameof(Zurueckgehen), typeof(Karte));

    static Karte()
    {
        FocusableProperty.OverrideMetadata(typeof(Karte), new FrameworkPropertyMetadata(false));
        CommandManager.RegisterClassCommandBinding(typeof(Karte), new CommandBinding(Zurueckgehen, (s, _) => ((Karte)s).ZurueckMelden()));
    }

    public object? Kopfbild { get => GetValue(KopfbildProperty); set => SetValue(KopfbildProperty, value); }
    public object? Aktionen { get => GetValue(AktionenProperty); set => SetValue(AktionenProperty, value); }
    public object? Zahl { get => GetValue(ZahlProperty); set => SetValue(ZahlProperty, value); }
    public bool Zurueck { get => (bool)GetValue(ZurueckProperty); set => SetValue(ZurueckProperty, value); }

    public event RoutedEventHandler Zurueckgegangen
    {
        add => AddHandler(ZurueckgegangenEvent, value);
        remove => RemoveHandler(ZurueckgegangenEvent, value);
    }

    /// <summary>Meldet <see cref="Zurueckgegangen"/> (das tut auch der Befehl <see cref="Zurueckgehen"/>).</summary>
    public void ZurueckMelden() => RaiseEvent(new RoutedEventArgs(ZurueckgegangenEvent, this));

    /// <summary>Der Zurück-Knopf der Vorlage (PART_Zurueck), oder null.</summary>
    public ButtonBase? Zurueckknopf => GetTemplateChild(TeilZurueck) as ButtonBase;

    protected override AutomationPeer OnCreateAutomationPeer() => new Meldungsflaeche(this, nameof(Karte));
}

/// <summary>
/// Bildplatzhalter (seit 0.5.0-beta.10): eine eingelassene Fläche mit dem Zeichen Bild (Rahmen, Berg, Sonne) absolut mittig,
/// für Karten ohne eigenes Bild. Gezeichnet auf ganzen Gerätepixeln: <see cref="Flaeche"/>, Innenschatten oben und links
/// (<see cref="Tiefe"/>), ein Rand von einem Gerätepixel (<see cref="Rand"/>, ohne ihn oben dunkel und unten eine feine
/// Lichtkante) und das Zeichen in <see cref="Zeichenfarbe"/>. Die Farben setzt der Stil der Familie (Glas.Bildplatzhalter,
/// Glas.Taktil.Bildplatzhalter; in Karten der Familie von selbst). Je Größe, Skalierung und Werten einmal gebaut und
/// eingefroren. Automation: ein Bild namens "Bild".
/// </summary>
public sealed class Bildplatzhalter : FrameworkElement
{
    public static readonly DependencyProperty FlaecheProperty = Pinsel(nameof(Flaeche));
    public static readonly DependencyProperty RandProperty = Pinsel(nameof(Rand));
    public static readonly DependencyProperty ZeichenfarbeProperty = Pinsel(nameof(Zeichenfarbe));

    /// <summary>Stärke des Innenschattens (0 flach, 1 kräftig).</summary>
    public static readonly DependencyProperty TiefeProperty = Zahl(nameof(Tiefe), 0.6);

    /// <summary>Eckradius in DIP.</summary>
    public static readonly DependencyProperty EckeProperty = Zahl(nameof(Ecke), 8);

    /// <summary>Größe des Zeichens als Anteil der kürzeren Seite.</summary>
    public static readonly DependencyProperty ZeichenanteilProperty = Zahl(nameof(Zeichenanteil), 0.44);

    /// <summary>Strichbreite des Zeichens als Anteil seiner Größe.</summary>
    public static readonly DependencyProperty StrichanteilProperty = Zahl(nameof(Strichanteil), 0.07);

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Bildplatzhalter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Zahl(string name, double vorgabe) => DependencyProperty.Register(
        name, typeof(double), typeof(Bildplatzhalter), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private DrawingGroup? _bild;

    public Bildplatzhalter()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Brush? Flaeche { get => (Brush?)GetValue(FlaecheProperty); set => SetValue(FlaecheProperty, value); }
    public Brush? Rand { get => (Brush?)GetValue(RandProperty); set => SetValue(RandProperty, value); }
    public Brush? Zeichenfarbe { get => (Brush?)GetValue(ZeichenfarbeProperty); set => SetValue(ZeichenfarbeProperty, value); }
    public double Tiefe { get => (double)GetValue(TiefeProperty); set => SetValue(TiefeProperty, value); }
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }
    public double Zeichenanteil { get => (double)GetValue(ZeichenanteilProperty); set => SetValue(ZeichenanteilProperty, value); }
    public double Strichanteil { get => (double)GetValue(StrichanteilProperty); set => SetValue(StrichanteilProperty, value); }

    /// <summary>Wie oft das Bild gebaut wurde (für die Tests: neu zeichnen baut nichts).</summary>
    internal int Bauten { get; private set; }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 96 : Math.Min(96, availableSize.Height));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 4 || h < 4) return;
        var schluessel = (b, h, s, Flaeche, Rand, Zeichenfarbe, Tiefe, Ecke, Zeichenanteil, Strichanteil);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            _bild = Bauen(b, h, s);
            Bauten++;
        }
        if (_bild is not null) drawingContext.DrawDrawing(_bild);
    }

    private DrawingGroup Bauen(double b, double h, double s)
    {
        var gruppe = new DrawingGroup();
        using (var dc = gruppe.Open())
        {
            var r = Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), Math.Min(b, h) / 2);
            var f = new Glasflaeche(0, 0, b, h, r, s);
            var form = f.Form(0);
            var t = Math.Clamp(Tiefe, 0, 1.5);
            if (Flaeche is { } flaeche) dc.DrawGeometry(flaeche, null, form);
            if (t > 0)
            {
                dc.DrawGeometry(Pinselwerk.Senkrecht((Farbton.Deckend(Colors.Black, 0.5 * t), 0), (Farbton.Deckend(Colors.Black, 0.14 * t), 0.18), (Farbton.Deckend(Colors.Black, 0), 0.42)), null, form);
                var links = Math.Max(1, Math.Min(b * 0.06, h * 0.4));
                var waagerecht = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(links / s, 0) };
                waagerecht.GradientStops.Add(new GradientStop(Farbton.Deckend(Colors.Black, 0.22 * t), 0));
                waagerecht.GradientStops.Add(new GradientStop(Farbton.Deckend(Colors.Black, 0), 1));
                waagerecht.Freeze();
                dc.DrawGeometry(waagerecht, null, form);
            }
            var ring = f.Ring(0, Glasgeometrie.Haarlinie);
            dc.DrawGeometry(Rand ?? Pinselwerk.Senkrecht((Farbton.Deckend(Colors.Black, Math.Min(1, 0.45 * Math.Max(t, 0.4))), 0), (Farbton.Deckend(Colors.Black, 0), 0.5),
                (Farbton.Deckend(Colors.White, Math.Min(1, 0.16 * Math.Max(t, 0.4))), 1)), null, ring);
            // Das Zeichen: Zeichner rückt die Box mit der Parität des Platzes absolut mittig ein; Strich aus seiner Größe.
            var anteil = Math.Clamp(Zeichenanteil, 0.05, 1);
            Zeichner.Zeichnen(dc, Glaszeichen.Bild, new Rect(0, 0, b, h), anteil, Zeichenfarbe, s, anteil * Math.Clamp(Strichanteil, 0.01, 0.3));
        }
        gruppe.Freeze();
        return gruppe;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Bildflaeche(this);
}

/// <summary>Automation des <see cref="Bildplatzhalter"/>: ein Bild, Name aus AutomationProperties.Name, sonst "Bild".</summary>
internal sealed class Bildflaeche(Bildplatzhalter platzhalter) : FrameworkElementAutomationPeer(platzhalter)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

    protected override string GetClassNameCore() => nameof(Bildplatzhalter);

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? "Bild" : name;
    }

    protected override bool IsControlElementCore() => true;
}
