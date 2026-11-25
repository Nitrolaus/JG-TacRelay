// Glasdesign - Maintainer: Sarevoc
using System.Collections.Specialized;
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

/// <summary>Eine Krume wurde gewählt: der Eintrag (das Element der Items) und seine Stelle.</summary>
public sealed class BrotkrumeGewaehltEventArgs(RoutedEvent routedEvent, object source, object? eintrag, int index) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>Der gewählte Eintrag (Text oder Objekt aus Items oder ItemsSource).</summary>
    public object? Eintrag { get; } = eintrag;

    /// <summary>Seine Stelle, von 0 an.</summary>
    public int Index { get; } = index;

    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ArgumentNullException.ThrowIfNull(genericHandler);
        ((EventHandler<BrotkrumeGewaehltEventArgs>)genericHandler)(genericTarget, this);
    }
}

/// <summary>
/// Brotkrumen (seit 0.5.0-beta.9, Stilfamilien 2 Stufe 2): der Weg zur aktuellen Seite als Reihe von Krumen
/// (<see cref="Brotkrume"/>), dazwischen ein Winkel nach rechts (<see cref="Krumenbahn"/>). Einträge sind Texte oder
/// Objekte (Items, ItemsSource, DisplayMemberPath). Die letzte Krume ist die aktuelle Seite: nicht klickbar, nicht im
/// Tab-Weg, für Bildschirmleser "aktuelle Seite". Eine andere Krume wählen (Maus, Enter, Leertaste, Automation) meldet
/// <see cref="Gewaehlt"/> mit Eintrag und Stelle und führt <see cref="Befehl"/> mit dem Eintrag aus. Stile:
/// Glas.Brotkrumen (Krumen als Glaskörper) und Glas.Taktil.Brotkrumen (eingelassene Pille, die erste Krume im Kupferton).
/// </summary>
public class Brotkrumen : ItemsControl
{
    public static readonly RoutedEvent GewaehltEvent = EventManager.RegisterRoutedEvent(
        nameof(Gewaehlt), RoutingStrategy.Bubble, typeof(EventHandler<BrotkrumeGewaehltEventArgs>), typeof(Brotkrumen));

    /// <summary>Wird beim Wählen einer Krume mit ihrem Eintrag als Parameter ausgeführt, wenn er das erlaubt.</summary>
    public static readonly DependencyProperty BefehlProperty = DependencyProperty.Register(
        nameof(Befehl), typeof(ICommand), typeof(Brotkrumen), new FrameworkPropertyMetadata(null));

    static Brotkrumen()
    {
        FocusableProperty.OverrideMetadata(typeof(Brotkrumen), new FrameworkPropertyMetadata(false));
    }

    public ICommand? Befehl { get => (ICommand?)GetValue(BefehlProperty); set => SetValue(BefehlProperty, value); }

    public event EventHandler<BrotkrumeGewaehltEventArgs> Gewaehlt
    {
        add => AddHandler(GewaehltEvent, value);
        remove => RemoveHandler(GewaehltEvent, value);
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is Brotkrume;

    protected override DependencyObject GetContainerForItemOverride() => new Brotkrume();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not Brotkrume k) return;
        Einordnen(k, ItemContainerGenerator.IndexFromContainer(k));
        // Die Zustandsvorschau der Leiste reicht an jede Krume (Musterblatt, Tests, Glint), solange sie keine eigene hat.
        if (k.ReadLocalValue(Glas.ZustandVorschauProperty) == DependencyProperty.UnsetValue)
            BindingOperations.SetBinding(k, Glas.ZustandVorschauProperty, new Binding { Path = new PropertyPath(Glas.ZustandVorschauProperty), Source = this });
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        Ordnen();
    }

    /// <summary>Setzt an allen erzeugten Krumen, welche die erste und welche die aktuelle (letzte) ist.</summary>
    private void Ordnen()
    {
        for (var i = 0; i < Items.Count; i++)
            if (ItemContainerGenerator.ContainerFromIndex(i) is Brotkrume k) Einordnen(k, i);
    }

    private void Einordnen(Brotkrume k, int index)
    {
        if (index < 0) return;
        k.SetValue(Brotkrume.IstErsteKey, index == 0);
        k.SetCurrentValue(Brotkrume.AktuellProperty, index == Items.Count - 1);
    }

    /// <summary>Meldet die Wahl einer Krume (<see cref="Gewaehlt"/>, dann <see cref="Befehl"/>); die aktuelle meldet nichts.</summary>
    internal void Waehlen(Brotkrume k)
    {
        if (k.Aktuell) return;
        var index = ItemContainerGenerator.IndexFromContainer(k);
        var eintrag = ItemContainerGenerator.ItemFromContainer(k);
        if (eintrag == DependencyProperty.UnsetValue) eintrag = k;
        RaiseEvent(new BrotkrumeGewaehltEventArgs(GewaehltEvent, this, eintrag, index));
        if (Befehl is { } befehl && befehl.CanExecute(eintrag)) befehl.Execute(eintrag);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Navigationsflaeche(this, nameof(Brotkrumen), "Brotkrumen", AutomationControlType.Group);
}

/// <summary>
/// Eine Krume der <see cref="Brotkrumen"/> (ButtonBase): klickbar wie ein Knopf (Maus, Enter, Leertaste), außer sie ist die
/// aktuelle Seite (<see cref="Aktuell"/>, setzt die Leiste an der letzten): dann nicht fokussierbar, ohne Klick und für
/// Bildschirmleser ein Text mit dem Status "aktuelle Seite". <see cref="IstErste"/> setzt die Leiste (Taktil zeigt die erste
/// im Kupferton).
/// </summary>
public class Brotkrume : ButtonBase
{
    public static readonly DependencyProperty AktuellProperty = DependencyProperty.Register(
        nameof(Aktuell), typeof(bool), typeof(Brotkrume), new FrameworkPropertyMetadata(false, (d, _) => ((Brotkrume)d).CoerceValue(FocusableProperty)));

    internal static readonly DependencyPropertyKey IstErsteKey = DependencyProperty.RegisterReadOnly(
        nameof(IstErste), typeof(bool), typeof(Brotkrume), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IstErsteProperty = IstErsteKey.DependencyProperty;

    static Brotkrume()
    {
        FocusableProperty.OverrideMetadata(typeof(Brotkrume), new FrameworkPropertyMetadata(true, null, (d, wert) => ((Brotkrume)d).Aktuell ? false : wert));
    }

    /// <summary>Die aktuelle Seite (die letzte Krume): nicht klickbar, nicht fokussierbar.</summary>
    public bool Aktuell { get => (bool)GetValue(AktuellProperty); set => SetValue(AktuellProperty, value); }

    public bool IstErste => (bool)GetValue(IstErsteProperty);

    protected override void OnClick()
    {
        if (Aktuell) return;
        base.OnClick();
        (ItemsControl.ItemsControlFromItemContainer(this) as Brotkrumen)?.Waehlen(this);
    }

    /// <summary>Klick über Automation (Invoke).</summary>
    internal void PerAutomation() => OnClick();

    protected override AutomationPeer OnCreateAutomationPeer() => new Krumenflaeche(this);
}

/// <summary>Automation einer <see cref="Brotkrume"/>: ein Link mit Invoke, die aktuelle ein Text mit Status "aktuelle Seite".</summary>
internal sealed class Krumenflaeche(Brotkrume krume) : ButtonBaseAutomationPeer(krume), IInvokeProvider
{
    public const string AktuelleSeite = "aktuelle Seite";

    protected override AutomationControlType GetAutomationControlTypeCore() => krume.Aktuell ? AutomationControlType.Text : AutomationControlType.Hyperlink;

    protected override string GetClassNameCore() => nameof(Brotkrume);

    protected override string GetItemStatusCore() => krume.Aktuell ? AktuelleSeite : base.GetItemStatusCore();

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? (krume.Aktuell ? null : this) : base.GetPattern(patternInterface);

    void IInvokeProvider.Invoke()
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        krume.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(krume.PerAutomation));
    }
}

/// <summary>
/// Automation einer Navigationsleiste (Brotkrumen, Schrittfolge): eine Gruppe oder Liste mit eigenem Klassennamen; Name
/// aus AutomationProperties.Name, sonst der Vorgabename.
/// </summary>
internal sealed class Navigationsflaeche(FrameworkElement element, string klasse, string vorgabe, AutomationControlType art) : FrameworkElementAutomationPeer(element)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => art;

    protected override string GetClassNameCore() => klasse;

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? vorgabe : name;
    }
}

/// <summary>
/// Panel der <see cref="Brotkrumen"/>: die Krumen nebeneinander, dazwischen <see cref="Abstand"/> (DIP, ganze
/// Gerätepixel), und in jeder Lücke ein Winkel nach rechts (<see cref="Glaszeichen.WinkelRechts"/>) in
/// <see cref="Trenner"/>, absolut mittig (<see cref="Zeichner"/>), Größe <see cref="Trennergroesse"/> als Anteil der Lücke.
/// </summary>
public sealed class Krumenbahn : Panel
{
    public static readonly DependencyProperty AbstandProperty = DependencyProperty.Register(
        nameof(Abstand), typeof(double), typeof(Krumenbahn), new FrameworkPropertyMetadata(20d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrennerProperty = DependencyProperty.Register(
        nameof(Trenner), typeof(Brush), typeof(Krumenbahn), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrennergroesseProperty = DependencyProperty.Register(
        nameof(Trennergroesse), typeof(double), typeof(Krumenbahn), new FrameworkPropertyMetadata(0.45, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Strichbreite des Winkels als Anteil der Lücke.</summary>
    public const double Strich = 0.075;

    public double Abstand { get => (double)GetValue(AbstandProperty); set => SetValue(AbstandProperty, value); }
    public Brush? Trenner { get => (Brush?)GetValue(TrennerProperty); set => SetValue(TrennerProperty, value); }
    public double Trennergroesse { get => (double)GetValue(TrennergroesseProperty); set => SetValue(TrennergroesseProperty, value); }

    private double Luecke()
    {
        var s = Glasgeometrie.Skala(this);
        return Glasgeometrie.Raster(Math.Max(0, Abstand), s) / s;
    }

    private IEnumerable<UIElement> Sichtbare() => InternalChildren.OfType<UIElement>().Where(k => k.Visibility != Visibility.Collapsed);

    protected override Size MeasureOverride(Size availableSize)
    {
        double b = 0, h = 0;
        var n = 0;
        foreach (UIElement kind in InternalChildren)
        {
            kind.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (kind.Visibility == Visibility.Collapsed) continue;
            b += kind.DesiredSize.Width;
            h = Math.Max(h, kind.DesiredSize.Height);
            n++;
        }
        return new Size(b + Math.Max(0, n - 1) * Luecke(), h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = Glasgeometrie.Skala(this);
        var x = 0.0;
        var luecke = Luecke();
        var erste = true;
        foreach (UIElement kind in InternalChildren)
        {
            if (kind.Visibility == Visibility.Collapsed)
            {
                kind.Arrange(new Rect(x, 0, 0, 0));
                continue;
            }
            if (!erste) x += luecke;
            erste = false;
            var breite = Glasgeometrie.Raster(kind.DesiredSize.Width, s) / s;
            kind.Arrange(new Rect(x, 0, breite, finalSize.Height));
            x += breite;
        }
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateMeasure();

    /// <summary>Die Lücken zwischen den sichtbaren Kindern in Gerätepixeln (für die Tests und zum Zeichnen).</summary>
    internal IReadOnlyList<Rect> Luecken()
    {
        var s = Glasgeometrie.Skala(this);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        var liste = new List<Rect>();
        UIElement? vorher = null;
        foreach (var kind in Sichtbare())
        {
            if (vorher is not null)
            {
                var links = Glasgeometrie.Raster(LayoutInformation.GetLayoutSlot((FrameworkElement)vorher).Right, s);
                var rechts = Glasgeometrie.Raster(LayoutInformation.GetLayoutSlot((FrameworkElement)kind).Left, s);
                if (rechts - links >= 2) liste.Add(new Rect(links, 0, rechts - links, h));
            }
            vorher = kind;
        }
        return liste;
    }

    protected override void OnRender(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        base.OnRender(dc);
        if (Trenner is not { } pinsel) return;
        var s = Glasgeometrie.Skala(this);
        // Der Zeichner setzt den Winkel absolut mittig in die Lücke (Box gleicher Parität), Größe aus ihrer kürzeren Seite.
        foreach (var luecke in Luecken())
            Zeichner.Zeichnen(dc, Glaszeichen.WinkelRechts, luecke, Trennergroesse, pinsel, s, Strich);
    }
}
