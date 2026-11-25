// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Glasdesign;

/// <summary>Wie eine Spalte sortiert ist (<see cref="Spaltenkopf.Sortierung"/>).</summary>
public enum Sortierung
{
    Keine,
    Aufsteigend,
    Absteigend,
}

/// <summary>Eine Spalte wurde sortiert: der <see cref="Spaltenkopf"/>, die neue Richtung und seine Stelle im Tabellenkopf.</summary>
public sealed class SortiertEventArgs(RoutedEvent routedEvent, object source, Spaltenkopf spalte, Sortierung richtung, int index) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>Der Spaltenkopf, der jetzt sortiert.</summary>
    public Spaltenkopf Spalte { get; } = spalte;

    /// <summary>Die neue Richtung (Aufsteigend oder Absteigend).</summary>
    public Sortierung Richtung { get; } = richtung;

    /// <summary>Die Stelle der Spalte, von 0 an (-1 ohne Tabellenkopf).</summary>
    public int Index { get; } = index;

    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ArgumentNullException.ThrowIfNull(genericHandler);
        ((EventHandler<SortiertEventArgs>)genericHandler)(genericTarget, this);
    }
}

/// <summary>
/// Tabellenkopf (seit 0.5.0-beta.10, Stilfamilien 2 Stufe 2): die Köpfe einer Tabelle nebeneinander in einer Leiste. Einträge
/// sind <see cref="Spaltenkopf"/> oder Texte (und andere Objekte), die zu Spaltenköpfen werden; die Breite je Spalte über
/// Width am Spaltenkopf. Klick, Enter oder Leertaste auf einem sortierbaren Kopf schalten Keine, Aufsteigend, Absteigend,
/// Aufsteigend ...; die übrigen Spalten werden Keine, und <see cref="Sortiert"/> meldet Spalte und Richtung. Ein Kopf mit
/// <see cref="Spaltenkopf.Filter"/> zeigt einen Trichter statt der Pfeile und meldet beim Klick <see cref="Filtern"/>.
/// Pfeil links und rechts, Pos1 und Ende führen den Tastaturfokus durch die bedienbaren Köpfe. Stile: Glas.Tabellenkopf
/// (Leiste aus Glas) und Glas.Taktil.Tabellenkopf (gehobene Leiste).
/// </summary>
public class Tabellenkopf : ItemsControl
{
    public static readonly RoutedEvent SortiertEvent = EventManager.RegisterRoutedEvent(
        nameof(Sortiert), RoutingStrategy.Bubble, typeof(EventHandler<SortiertEventArgs>), typeof(Tabellenkopf));

    /// <summary>Ein Filterkopf wurde geklickt (dasselbe Ereignis wie <see cref="Spaltenkopf.FilternEvent"/>, hier zu fangen).</summary>
    public static readonly RoutedEvent FilternEvent = Spaltenkopf.FilternEvent.AddOwner(typeof(Tabellenkopf));

    static Tabellenkopf()
    {
        FocusableProperty.OverrideMetadata(typeof(Tabellenkopf), new FrameworkPropertyMetadata(false));
    }

    public event EventHandler<SortiertEventArgs> Sortiert
    {
        add => AddHandler(SortiertEvent, value);
        remove => RemoveHandler(SortiertEvent, value);
    }

    public event RoutedEventHandler Filtern
    {
        add => AddHandler(FilternEvent, value);
        remove => RemoveHandler(FilternEvent, value);
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is Spaltenkopf;

    protected override DependencyObject GetContainerForItemOverride() => new Spaltenkopf();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        // Die Zustandsvorschau der Leiste reicht an jeden Kopf (Musterblatt, Tests, Glint), solange er keine eigene hat.
        if (element is Spaltenkopf k && k.ReadLocalValue(Glas.ZustandVorschauProperty) == DependencyProperty.UnsetValue)
            BindingOperations.SetBinding(k, Glas.ZustandVorschauProperty, new Binding { Path = new PropertyPath(Glas.ZustandVorschauProperty), Source = this });
    }

    /// <summary>Alle Spaltenköpfe in ihrer Reihenfolge (erzeugte Behälter und eigene Köpfe).</summary>
    public IReadOnlyList<Spaltenkopf> Spalten()
    {
        var liste = new List<Spaltenkopf>();
        for (var i = 0; i < Items.Count; i++)
            if (ItemContainerGenerator.ContainerFromIndex(i) is Spaltenkopf k) liste.Add(k);
        return liste;
    }

    /// <summary>Die nächste Richtung nach <paramref name="jetzt"/>: Keine und Absteigend werden Aufsteigend, Aufsteigend wird Absteigend.</summary>
    public static Sortierung Naechste(Sortierung jetzt) => jetzt == Sortierung.Aufsteigend ? Sortierung.Absteigend : Sortierung.Aufsteigend;

    /// <summary>Sortiert nach <paramref name="spalte"/> (nächste Richtung), setzt die übrigen Spalten auf Keine und meldet <see cref="Sortiert"/>.</summary>
    internal void Sortieren(Spaltenkopf spalte)
    {
        var richtung = Naechste(spalte.Sortierung);
        foreach (var k in Spalten())
            if (!ReferenceEquals(k, spalte) && k.Sortierung != Sortierung.Keine) k.SetCurrentValue(Spaltenkopf.SortierungProperty, Sortierung.Keine);
        spalte.SetCurrentValue(Spaltenkopf.SortierungProperty, richtung);
        RaiseEvent(new SortiertEventArgs(SortiertEvent, this, spalte, richtung, ItemContainerGenerator.IndexFromContainer(spalte)));
    }

    /// <summary>
    /// Wohin der Fokus mit <paramref name="taste"/> von <paramref name="von"/> aus geht: Pfeil links und rechts zum vorigen und
    /// nächsten bedienbaren Kopf (am Rand bleibt er stehen), Pos1 und Ende zum ersten und letzten; sonst null. Rechts nach links
    /// (FlowDirection RightToLeft) gehen die Pfeile in Leserichtung: links zum nächsten (seit 0.5.0-beta.13).
    /// </summary>
    public Spaltenkopf? Naechster(Spaltenkopf von, Key taste)
    {
        var bedienbar = Spalten().Where(k => k.Focusable && k.IsEnabled && k.Visibility == Visibility.Visible).ToList();
        if (bedienbar.Count == 0) return null;
        var i = bedienbar.IndexOf(von);
        if (FlowDirection == FlowDirection.RightToLeft && taste is Key.Left or Key.Right) taste = taste == Key.Left ? Key.Right : Key.Left;
        return taste switch
        {
            Key.Home => bedienbar[0],
            Key.End => bedienbar[^1],
            Key.Left when i > 0 => bedienbar[i - 1],
            Key.Right when i >= 0 && i < bedienbar.Count - 1 => bedienbar[i + 1],
            Key.Left or Key.Right => i >= 0 ? von : null,
            _ => null,
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled || e.OriginalSource is not DependencyObject quelle) return;
        var von = quelle as Spaltenkopf;
        for (var d = quelle; von is null && d is Visual and not Tabellenkopf; d = VisualTreeHelper.GetParent(d)) von = d as Spaltenkopf;
        if (von is null || Naechster(von, e.Key) is not { } ziel) return;
        if (!ReferenceEquals(ziel, von)) ziel.Focus();
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Navigationsflaeche(this, nameof(Tabellenkopf), "Tabellenkopf", AutomationControlType.Header);
}

/// <summary>
/// Ein Kopf im <see cref="Tabellenkopf"/> (ButtonBase): der Inhalt ist die Beschriftung, daneben die Sortierpfeile
/// (<see cref="Sortierpfeile"/>: beide schwach, solange die Spalte nicht sortiert ist, der gewählte hell) oder, mit
/// <see cref="Filter"/>, ein Trichter. Sortierbar (Vorgabe) schaltet ein Klick, Enter oder die Leertaste die Richtung, ein
/// Filterkopf meldet stattdessen <see cref="FilternEvent"/>; ein Kopf, der keins von beiden ist, ist nur Beschriftung: nicht
/// fokussierbar, ohne Klick. Automation: ein Spaltenkopf mit dem Namen aus der Beschriftung und der Sortierung ("Name,
/// aufsteigend sortiert").
/// </summary>
public class Spaltenkopf : ButtonBase
{
    public static readonly DependencyProperty SortierungProperty = DependencyProperty.Register(
        nameof(Sortierung), typeof(Sortierung), typeof(Spaltenkopf),
        new FrameworkPropertyMetadata(Sortierung.Keine, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SortierbarProperty = DependencyProperty.Register(
        nameof(Sortierbar), typeof(bool), typeof(Spaltenkopf), new FrameworkPropertyMetadata(true, Bedienbarkeit));

    public static readonly DependencyProperty FilterProperty = DependencyProperty.Register(
        nameof(Filter), typeof(bool), typeof(Spaltenkopf), new FrameworkPropertyMetadata(false, Bedienbarkeit));

    /// <summary>Ein Filterkopf wurde geklickt (Maus, Enter, Leertaste, Automation); steigt zum Tabellenkopf auf.</summary>
    public static readonly RoutedEvent FilternEvent = EventManager.RegisterRoutedEvent(
        nameof(Filtern), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Spaltenkopf));

    static Spaltenkopf()
    {
        FocusableProperty.OverrideMetadata(typeof(Spaltenkopf), new FrameworkPropertyMetadata(true, null, (d, wert) => ((Spaltenkopf)d).Bedienbar && (bool)wert));
    }

    public Sortierung Sortierung { get => (Sortierung)GetValue(SortierungProperty); set => SetValue(SortierungProperty, value); }

    /// <summary>Ob ein Klick die Spalte sortiert (Vorgabe ja).</summary>
    public bool Sortierbar { get => (bool)GetValue(SortierbarProperty); set => SetValue(SortierbarProperty, value); }

    /// <summary>Ein Filterkopf: Trichter statt Pfeilen, der Klick meldet <see cref="Filtern"/> statt zu sortieren.</summary>
    public bool Filter { get => (bool)GetValue(FilterProperty); set => SetValue(FilterProperty, value); }

    /// <summary>Sortierbar oder Filter: dann fokussierbar und klickbar.</summary>
    public bool Bedienbar => Sortierbar || Filter;

    public event RoutedEventHandler Filtern
    {
        add => AddHandler(FilternEvent, value);
        remove => RemoveHandler(FilternEvent, value);
    }

    private static void Bedienbarkeit(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Spaltenkopf)d).CoerceValue(FocusableProperty);

    protected override void OnClick()
    {
        if (!Bedienbar) return;
        base.OnClick();
        if (Filter)
        {
            RaiseEvent(new RoutedEventArgs(FilternEvent, this));
            return;
        }
        if (ItemsControl.ItemsControlFromItemContainer(this) is Tabellenkopf kopf) kopf.Sortieren(this);
        else SetCurrentValue(SortierungProperty, Tabellenkopf.Naechste(Sortierung));
    }

    /// <summary>Klick über Automation (Invoke).</summary>
    internal void PerAutomation() => OnClick();

    /// <summary>Der Zusatz für Bildschirmleser: "aufsteigend sortiert", "absteigend sortiert", "nicht sortiert", "Filter" oder leer.</summary>
    public string Zustandstext => Filter ? "Filter" : !Sortierbar ? "" : Sortierung switch
    {
        Sortierung.Aufsteigend => "aufsteigend sortiert",
        Sortierung.Absteigend => "absteigend sortiert",
        _ => "nicht sortiert",
    };

    protected override AutomationPeer OnCreateAutomationPeer() => new Spaltenkopfflaeche(this);
}

/// <summary>Automation eines <see cref="Spaltenkopf"/>: ein HeaderItem "Name, aufsteigend sortiert" mit Invoke, wenn er bedienbar ist.</summary>
internal sealed class Spaltenkopfflaeche(Spaltenkopf kopf) : ButtonBaseAutomationPeer(kopf), IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;

    protected override string GetClassNameCore() => nameof(Spaltenkopf);

    protected override string GetNameCore()
    {
        var name = AutomationProperties.GetName(kopf);
        if (string.IsNullOrEmpty(name)) name = Rueckmeldungstext.Von(kopf.Content);
        var zusatz = kopf.Zustandstext;
        return zusatz.Length == 0 ? name : name.Length == 0 ? zusatz : name + ", " + zusatz;
    }

    // Kein eigener ItemStatus (seit 0.5.0-beta.13): Die Sortierung steht schon im Namen, ein Bildschirmleser las sie sonst
    // zweimal ("Status, aufsteigend sortiert, aufsteigend sortiert"). Ein eigener ItemStatus des Kopfs gilt wie gewohnt.

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? (kopf.Bedienbar ? this : null) : base.GetPattern(patternInterface);

    void IInvokeProvider.Invoke()
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        kopf.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(kopf.PerAutomation));
    }
}

/// <summary>
/// Die Sortierpfeile eines Spaltenkopfs: ein kleines Dreieck nach oben über einem nach unten, zusammen absolut mittig in den
/// eigenen Grenzen (Breite und Höhe der Pfeile mit der Parität des Platzes, Kanten auf ganzen Gerätepixeln). Beide in
/// <see cref="Gedaempft"/>, solange <see cref="Sortierung"/> Keine ist; sonst der gewählte (oben aufsteigend, unten
/// absteigend) in <see cref="Pinsel"/>. Die Formen werden je Größe und Skalierung einmal gebaut und eingefroren.
/// </summary>
public sealed class Sortierpfeile : FrameworkElement
{
    public static readonly DependencyProperty SortierungProperty = DependencyProperty.Register(
        nameof(Sortierung), typeof(Sortierung), typeof(Sortierpfeile), new FrameworkPropertyMetadata(Sortierung.Keine, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PinselProperty = DependencyProperty.Register(
        nameof(Pinsel), typeof(Brush), typeof(Sortierpfeile), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GedaempftProperty = DependencyProperty.Register(
        nameof(Gedaempft), typeof(Brush), typeof(Sortierpfeile), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Breite der Pfeile als Anteil der Breite des Platzes.</summary>
    public const double Breitenanteil = 0.8;

    private object? _schluessel;
    private Geometry? _hoch;
    private Geometry? _runter;

    public Sortierpfeile()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Sortierung Sortierung { get => (Sortierung)GetValue(SortierungProperty); set => SetValue(SortierungProperty, value); }
    public Brush? Pinsel { get => (Brush?)GetValue(PinselProperty); set => SetValue(PinselProperty, value); }
    public Brush? Gedaempft { get => (Brush?)GetValue(GedaempftProperty); set => SetValue(GedaempftProperty, value); }

    /// <summary>Wie oft die Formen gebaut wurden (für die Tests: eine neue Richtung baut nichts).</summary>
    internal int Bauten { get; private set; }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(10, availableSize.Width), Math.Min(14, availableSize.Height));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    /// <summary>
    /// Lage der beiden Dreiecke in Pixeln für einen Platz von <paramref name="b"/> mal <paramref name="h"/> Pixeln: Breite mit
    /// der Parität von b, Höhe gut die halbe Breite, Lücke mindestens ein Pixel und so, dass oben und unten gleich viel Platz
    /// bleibt. Ergebnis: links, oben (des oberen Dreiecks), Breite, Höhe eines Dreiecks, Lücke.
    /// </summary>
    internal static (double X, double Y, double Breite, double Hoehe, double Luecke) Lage(double b, double h)
    {
        var breite = Math.Max(2, Math.Round(b * Breitenanteil));
        if ((b - breite) % 2 != 0) breite--;
        var hoehe = Math.Max(1, Math.Round(breite * 0.55));
        var luecke = Math.Max(1, Math.Round(breite * 0.3));
        while (2 * hoehe + luecke > h && hoehe > 1) hoehe--;
        // Oben und unten gleich viel Platz: die Lücke trägt die Parität (eins mehr, wenn Platz ist, sonst eins weniger).
        if ((h - (2 * hoehe + luecke)) % 2 != 0) luecke += 2 * hoehe + luecke + 1 <= h ? 1 : luecke > 1 ? -1 : 0;
        var gesamt = 2 * hoehe + luecke;
        return ((b - breite) / 2, Math.Floor((h - gesamt) / 2), breite, hoehe, luecke);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 3 || h < 4) return;
        var schluessel = (b, h, s);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            var (x, y, breite, hoehe, luecke) = Lage(b, h);
            _hoch = Dreieck(s, (x, y + hoehe), (x + breite, y + hoehe), (x + breite / 2, y));
            var u = y + hoehe + luecke;
            _runter = Dreieck(s, (x, u), (x + breite, u), (x + breite / 2, u + hoehe));
            Bauten++;
        }
        drawingContext.DrawGeometry(Sortierung == Sortierung.Aufsteigend ? Pinsel : Gedaempft, null, _hoch);
        drawingContext.DrawGeometry(Sortierung == Sortierung.Absteigend ? Pinsel : Gedaempft, null, _runter);
    }

    private static PathGeometry Dreieck(double s, params (double X, double Y)[] punkte)
    {
        var f = new PathFigure { StartPoint = new Point(punkte[0].X / s, punkte[0].Y / s), IsClosed = true, IsFilled = true };
        foreach (var (x, y) in punkte.Skip(1)) f.Segments.Add(new LineSegment(new Point(x / s, y / s), true));
        var g = new PathGeometry([f]);
        g.Freeze();
        return g;
    }
}
