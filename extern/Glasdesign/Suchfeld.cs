// Glasdesign - Maintainer: Sarevoc
using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Glasdesign;

/// <summary>Daten von <see cref="Suchfeld.Gesucht"/>: der gesuchte Text.</summary>
public sealed class SuchfeldGesuchtEventArgs(RoutedEvent ereignis, object quelle, string text) : RoutedEventArgs(ereignis, quelle)
{
    public string Text { get; } = text;

    /// <summary>Ruft den Handler direkt (seit 0.5.0-beta.13): sonst über DynamicInvoke, und jede Ausnahme käme als TargetInvocationException an.</summary>
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ArgumentNullException.ThrowIfNull(genericHandler);
        ((EventHandler<SuchfeldGesuchtEventArgs>)genericHandler)(genericTarget, this);
    }
}

/// <summary>Daten von <see cref="Suchfeld.Gewaehlt"/>: das gewählte Ergebnis und seine Stelle in der Liste.</summary>
public sealed class SuchfeldGewaehltEventArgs(RoutedEvent ereignis, object quelle, object? eintrag, int index) : RoutedEventArgs(ereignis, quelle)
{
    public object? Eintrag { get; } = eintrag;
    public int Index { get; } = index;

    /// <summary>Ruft den Handler direkt (seit 0.5.0-beta.13, wie <see cref="SuchfeldGesuchtEventArgs"/>).</summary>
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ArgumentNullException.ThrowIfNull(genericHandler);
        ((EventHandler<SuchfeldGewaehltEventArgs>)genericHandler)(genericTarget, this);
    }
}

/// <summary>
/// Suchfeld (seit 0.5.0-beta.11, Stilfamilien 2 Stufe 2): ein Eingabefeld mit Lupe und einer Liste von Ergebnissen
/// darunter. <see cref="Text"/> (zwei Wege), <see cref="Platzhalter"/>, <see cref="Ergebnisse"/> (die Einträge der Liste,
/// als Text oder <see cref="Listeneintrag"/>), <see cref="IstOffen"/> (die Liste aufgeklappt, zwei Wege), <see cref="Laedt"/>
/// (in der Liste ein <see cref="Skelett"/> statt der Ergebnisse), <see cref="ListeEingebettet"/> (die Liste im Layout unter
/// dem Feld statt in einem Popup; für Bühnen, Musterblätter und Tests) und <see cref="Suchknopf"/> (rechts ein echter Knopf
/// mit Lupe, fokussierbar, "Suchen"). Enter im Feld oder der Suchknopf lösen den Befehl <see cref="Suchen"/> aus, das Feld
/// meldet ihn als <see cref="Gesucht"/>; ein Klick, Enter oder Leertaste auf einem Ergebnis meldet <see cref="Gewaehlt"/> und
/// schließt die Liste. Pfeil runter springt aus dem Feld in die Liste (und klappt sie auf), Pfeil hoch über dem ersten
/// Ergebnis zurück ins Feld; Escape schließt die Liste und leert, wenn sie schon zu ist, das Feld. Das Popup öffnet nur auf
/// dem Bildschirm (in einem Fenster); im Speicher gezeichnet bleibt es zu. Automation: ein Kombinationsfeld mit
/// ExpandCollapse und Value. Stile: Glas.Suchfeld, Glas.Glutglas.Suchfeld, Glas.Dickglas.Suchfeld.
/// </summary>
[TemplatePart(Name = TeilEingabe, Type = typeof(TextBox))]
[TemplatePart(Name = TeilEintraege, Type = typeof(Suchliste))]
[TemplatePart(Name = TeilListe, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TeilEinbettung, Type = typeof(Decorator))]
[TemplatePart(Name = TeilPopup, Type = typeof(Popup))]
[TemplatePart(Name = TeilSuchknopf, Type = typeof(ButtonBase))]
public class Suchfeld : Control
{
    public const string TeilEingabe = "PART_Eingabe";
    public const string TeilEintraege = "PART_Eintraege";
    public const string TeilListe = "PART_Liste";
    public const string TeilEinbettung = "PART_Einbettung";
    public const string TeilPopup = "PART_Popup";
    public const string TeilSuchknopf = "PART_Suchknopf";

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Suchfeld),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((Suchfeld)d).PlatzhalterPruefen()));

    public static readonly DependencyProperty PlatzhalterProperty = DependencyProperty.Register(
        nameof(Platzhalter), typeof(string), typeof(Suchfeld), new FrameworkPropertyMetadata(null, (d, _) => ((Suchfeld)d).PlatzhalterPruefen()));

    public static readonly DependencyProperty ErgebnisseProperty = DependencyProperty.Register(
        nameof(Ergebnisse), typeof(IEnumerable), typeof(Suchfeld), new FrameworkPropertyMetadata(null, (d, _) => ((Suchfeld)d).ListeZeigen()));

    public static readonly DependencyProperty IstOffenProperty = DependencyProperty.Register(
        nameof(IstOffen), typeof(bool), typeof(Suchfeld),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, IstOffenGeaendert));

    public static readonly DependencyProperty LaedtProperty = DependencyProperty.Register(
        nameof(Laedt), typeof(bool), typeof(Suchfeld), new FrameworkPropertyMetadata(false, (d, _) => ((Suchfeld)d).ListeZeigen()));

    public static readonly DependencyProperty ListeEingebettetProperty = DependencyProperty.Register(
        nameof(ListeEingebettet), typeof(bool), typeof(Suchfeld), new FrameworkPropertyMetadata(false, (d, _) => ((Suchfeld)d).Platzieren()));

    public static readonly DependencyProperty SuchknopfProperty = DependencyProperty.Register(
        nameof(Suchknopf), typeof(bool), typeof(Suchfeld), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey PlatzhalterSichtbarKey = DependencyProperty.RegisterReadOnly(
        nameof(PlatzhalterSichtbar), typeof(bool), typeof(Suchfeld), new FrameworkPropertyMetadata(false));

    /// <summary>Platzhalter gesetzt und das Feld leer (für die Vorlage).</summary>
    public static readonly DependencyProperty PlatzhalterSichtbarProperty = PlatzhalterSichtbarKey.DependencyProperty;

    public static readonly RoutedEvent GesuchtEvent = EventManager.RegisterRoutedEvent(
        nameof(Gesucht), RoutingStrategy.Bubble, typeof(EventHandler<SuchfeldGesuchtEventArgs>), typeof(Suchfeld));

    public static readonly RoutedEvent GewaehltEvent = EventManager.RegisterRoutedEvent(
        nameof(Gewaehlt), RoutingStrategy.Bubble, typeof(EventHandler<SuchfeldGewaehltEventArgs>), typeof(Suchfeld));

    /// <summary>Der Befehl des Suchknopfs und der Enter-Taste im Feld; das Feld meldet ihn als <see cref="Gesucht"/>.</summary>
    public static RoutedUICommand Suchen { get; } = new("Suchen", nameof(Suchen), typeof(Suchfeld));

    private TextBox? _eingabe;
    private Suchliste? _eintraege;
    private FrameworkElement? _liste;
    private Decorator? _einbettung;
    private Popup? _popup;

    static Suchfeld()
    {
        FocusableProperty.OverrideMetadata(typeof(Suchfeld), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.TabNavigationProperty.OverrideMetadata(typeof(Suchfeld), new FrameworkPropertyMetadata(KeyboardNavigationMode.Local));
        CommandManager.RegisterClassCommandBinding(typeof(Suchfeld), new CommandBinding(Suchen, (s, _) => ((Suchfeld)s).SuchenMelden()));
        EventManager.RegisterClassHandler(typeof(Suchfeld), ButtonBase.ClickEvent, new RoutedEventHandler((s, e) => ((Suchfeld)s).Geklickt(e)));
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Platzhalter { get => (string?)GetValue(PlatzhalterProperty); set => SetValue(PlatzhalterProperty, value); }
    public IEnumerable? Ergebnisse { get => (IEnumerable?)GetValue(ErgebnisseProperty); set => SetValue(ErgebnisseProperty, value); }
    public bool IstOffen { get => (bool)GetValue(IstOffenProperty); set => SetValue(IstOffenProperty, value); }
    public bool Laedt { get => (bool)GetValue(LaedtProperty); set => SetValue(LaedtProperty, value); }
    public bool ListeEingebettet { get => (bool)GetValue(ListeEingebettetProperty); set => SetValue(ListeEingebettetProperty, value); }
    public bool Suchknopf { get => (bool)GetValue(SuchknopfProperty); set => SetValue(SuchknopfProperty, value); }
    public bool PlatzhalterSichtbar => (bool)GetValue(PlatzhalterSichtbarProperty);

    public event EventHandler<SuchfeldGesuchtEventArgs> Gesucht
    {
        add => AddHandler(GesuchtEvent, value);
        remove => RemoveHandler(GesuchtEvent, value);
    }

    public event EventHandler<SuchfeldGewaehltEventArgs> Gewaehlt
    {
        add => AddHandler(GewaehltEvent, value);
        remove => RemoveHandler(GewaehltEvent, value);
    }

    /// <summary>Das Eingabefeld der Vorlage (PART_Eingabe), oder null.</summary>
    public TextBox? Eingabe => _eingabe;

    /// <summary>Der Suchknopf der Vorlage (PART_Suchknopf), oder null.</summary>
    public ButtonBase? Knopf => GetTemplateChild(TeilSuchknopf) as ButtonBase;

    /// <summary>Die Ergebnisse als Einträge der Liste (in ihrer Reihenfolge).</summary>
    public IReadOnlyList<Listeneintrag> Eintraege() => _eintraege?.Eintraege() ?? [];

    /// <summary>Ob die Liste etwas zeigt (Ergebnisse oder, während es lädt, das Skelett).</summary>
    public bool ListeHatInhalt => Laedt || (_eintraege is not null ? _eintraege.Items.Count > 0 : Ergebnisse?.GetEnumerator().MoveNext() ?? false);

    /// <summary>Wie viele Popups dieses Feld geöffnet hat (für die Tests: im Speicher keines).</summary>
    internal int Popupoeffnungen { get; private set; }

    public override void OnApplyTemplate()
    {
        if (_eintraege is not null) ((INotifyCollectionChanged)_eintraege.Items).CollectionChanged -= EintraegeGeaendert;
        if (_popup is not null) _popup.Closed -= PopupZu;
        if (_popup is not null) _popup.Child = null;
        if (_einbettung is not null) _einbettung.Child = null;
        base.OnApplyTemplate();
        _eingabe = GetTemplateChild(TeilEingabe) as TextBox;
        _eintraege = GetTemplateChild(TeilEintraege) as Suchliste;
        _liste = GetTemplateChild(TeilListe) as FrameworkElement;
        _einbettung = GetTemplateChild(TeilEinbettung) as Decorator;
        _popup = GetTemplateChild(TeilPopup) as Popup;
        if (_eintraege is not null) ((INotifyCollectionChanged)_eintraege.Items).CollectionChanged += EintraegeGeaendert;
        if (_popup is not null) _popup.Closed += PopupZu;
        EingabeBenennen();
        Platzieren();
    }

    /// <summary>
    /// Der Automation-Name des Eingabefelds, das den Tastaturfokus hat (das Suchfeld selbst ist nicht fokussierbar): wie das
    /// Suchfeld heißt (AutomationProperties.Name, sonst Platzhalter, sonst "Suche"). Seit 0.5.0-beta.13; bis beta.12 nahm die
    /// Vorlage nur den Platzhalter, ohne ihn war das fokussierte Feld namenlos.
    /// </summary>
    internal string Benennung()
    {
        var name = AutomationProperties.GetName(this);
        if (!string.IsNullOrEmpty(name)) return name;
        return string.IsNullOrEmpty(Platzhalter) ? "Suche" : Platzhalter;
    }

    private void EingabeBenennen()
    {
        if (_eingabe is not null && AutomationProperties.GetName(_eingabe) != Benennung()) AutomationProperties.SetName(_eingabe, Benennung());
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == AutomationProperties.NameProperty || e.Property == PlatzhalterProperty) EingabeBenennen();
    }

    private void EintraegeGeaendert(object? sender, NotifyCollectionChangedEventArgs e) => ListeZeigen();

    private void PlatzhalterPruefen()
    {
        var sichtbar = !string.IsNullOrEmpty(Platzhalter) && string.IsNullOrEmpty(Text);
        if (sichtbar == PlatzhalterSichtbar) return;
        if (sichtbar) SetValue(PlatzhalterSichtbarKey, true);
        else ClearValue(PlatzhalterSichtbarKey);
    }

    private static void IstOffenGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var feld = (Suchfeld)d;
        feld.ListeZeigen();
        if (UIElementAutomationPeer.FromElement(feld) is { } peer)
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                (bool)e.OldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                (bool)e.NewValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }

    /// <summary>Hängt die Liste ins Layout (eingebettet) oder ins Popup.</summary>
    private void Platzieren()
    {
        if (_liste is null)
        {
            ListeZeigen();
            return;
        }
        if (ListeEingebettet)
        {
            _liste.ClearValue(WidthProperty);
            if (_popup is not null && ReferenceEquals(_popup.Child, _liste)) _popup.Child = null;
            if (_einbettung is not null && !ReferenceEquals(_einbettung.Child, _liste)) _einbettung.Child = _liste;
        }
        else
        {
            if (_einbettung is not null && ReferenceEquals(_einbettung.Child, _liste)) _einbettung.Child = null;
            if (_popup is not null && !ReferenceEquals(_popup.Child, _liste)) _popup.Child = _liste;
        }
        ListeZeigen();
    }

    /// <summary>Zeigt die Liste, solange sie offen ist und etwas zu zeigen hat; das Popup nur auf dem Bildschirm.</summary>
    private void ListeZeigen()
    {
        var zeigen = IstOffen && ListeHatInhalt;
        // Die Sichtbarkeit der Einbettung führt allein das Suchfeld (seit 0.5.0-beta.12 setzt die Vorlage sie nicht mehr: ihr
        // "eingeklappt" lag unter diesem lokalen Wert und galt nie).
        if (_einbettung is not null) _einbettung.Visibility = zeigen && ListeEingebettet ? Visibility.Visible : Visibility.Collapsed;
        if (_popup is null) return;
        var auf = zeigen && !ListeEingebettet && PresentationSource.FromVisual(this) is not null;
        if (auf && !_popup.IsOpen)
        {
            Popupoeffnungen++;
            // Im Popup so breit wie das Feld (eingebettet streckt das Layout die Liste von selbst).
            if (_liste is not null) _liste.Width = _popup.PlacementTarget is FrameworkElement ziel ? ziel.ActualWidth : ActualWidth;
        }
        if (_popup.IsOpen != auf) _popup.IsOpen = auf;
    }

    private void PopupZu(object? sender, EventArgs e)
    {
        if (!ListeEingebettet && IstOffen) SetCurrentValue(IstOffenProperty, false);
    }

    /// <summary>Meldet <see cref="Gesucht"/> mit dem jetzigen Text (das tut auch der Befehl <see cref="Suchen"/>).</summary>
    public void SuchenMelden() => RaiseEvent(new SuchfeldGesuchtEventArgs(GesuchtEvent, this, Text ?? ""));

    /// <summary>Wählt ein Ergebnis: schließt die Liste, gibt den Fokus ans Feld zurück und meldet <see cref="Gewaehlt"/>.</summary>
    public void Waehlen(int index)
    {
        var e = Eintraege();
        if (index < 0 || index >= e.Count || _eintraege is null) return;
        var eintrag = _eintraege.ItemContainerGenerator.ItemFromContainer(e[index]);
        SetCurrentValue(IstOffenProperty, false);
        _eingabe?.Focus();
        RaiseEvent(new SuchfeldGewaehltEventArgs(GewaehltEvent, this, eintrag == DependencyProperty.UnsetValue ? e[index] : eintrag, index));
    }

    private void Geklickt(RoutedEventArgs e)
    {
        if (e.OriginalSource is not Listeneintrag l || _eintraege is null) return;
        var i = _eintraege.ItemContainerGenerator.IndexFromContainer(l);
        if (i < 0) return;
        e.Handled = true;
        Waehlen(i);
    }

    private bool ImFeld(object? quelle) => _eingabe is not null && quelle is DependencyObject d && (ReferenceEquals(d, _eingabe) || (d is Visual v && _eingabe.IsAncestorOf(v)));

    /// <summary>
    /// Wohin eine Taste führt (für Tastatur und Tests): aus dem Feld mit Pfeil runter zum ersten Ergebnis, aus dem ersten
    /// Ergebnis mit Pfeil hoch zurück ins Feld, zwischen den Ergebnissen hoch und runter, Pos1 und Ende; sonst null.
    /// </summary>
    public UIElement? Naechster(object? von, Key taste)
    {
        var e = Eintraege();
        if (ImFeld(von)) return taste == Key.Down && e.Count > 0 ? e[0] : null;
        if (von is not Listeneintrag l) return null;
        var i = -1;
        for (var k = 0; k < e.Count; k++)
            if (ReferenceEquals(e[k], l)) i = k;
        if (i < 0) return null;
        return taste switch
        {
            Key.Up => i == 0 ? _eingabe : e[i - 1],
            Key.Down => e[Math.Min(e.Count - 1, i + 1)],
            Key.Home => e[0],
            Key.End => e[^1],
            _ => null,
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Escape:
                if (IstOffen)
                {
                    SetCurrentValue(IstOffenProperty, false);
                    _eingabe?.Focus();
                    e.Handled = true;
                }
                else if (!string.IsNullOrEmpty(Text))
                {
                    SetCurrentValue(TextProperty, "");
                    e.Handled = true;
                }
                break;
            case Key.Enter when ImFeld(e.OriginalSource):
                Suchen.Execute(null, this);
                e.Handled = true;
                break;
            case Key.Down when ImFeld(e.OriginalSource):
                if (ListeHatInhalt && !IstOffen)
                {
                    SetCurrentValue(IstOffenProperty, true);
                    // Die Einträge einer zugeklappten Liste gibt es noch nicht (sie wurde nie angeordnet): jetzt anordnen, damit
                    // Pfeil runter gleich in die Liste springt (seit 0.5.0-beta.13; bis beta.12 klappte er nur auf).
                    UpdateLayout();
                }
                if (Naechster(e.OriginalSource, Key.Down) is { } erster)
                {
                    erster.Focus();
                    (erster as FrameworkElement)?.BringIntoView();
                    e.Handled = true;
                }
                else if (IstOffen && !Laedt && _eintraege is { Items.Count: > 0 })
                {
                    // Im Popup entstehen die Einträge erst, wenn es auf dem Bildschirm angeordnet ist.
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        if (IstOffen && Eintraege() is { Count: > 0 } liste && liste[0].Focus()) liste[0].BringIntoView();
                    }));
                    e.Handled = true;
                }
                break;
            case Key.Up or Key.Down or Key.Home or Key.End when e.OriginalSource is Listeneintrag:
                if (Naechster(e.OriginalSource, e.Key) is { } ziel)
                {
                    ziel.Focus();
                    // In der blätternden Liste ins Bild holen (der Fokus allein tut es nicht in jedem Fall).
                    (ziel as FrameworkElement)?.BringIntoView();
                    e.Handled = true;
                }
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Suchfeldflaeche(this);
}

/// <summary>
/// Die Liste der Ergebnisse im <see cref="Suchfeld"/>: ein ItemsControl, dessen Einträge <see cref="Listeneintrag"/> sind (der
/// Stil der Familie kommt über ItemContainerStyle). Automation: eine Liste namens "Ergebnisse".
/// </summary>
public class Suchliste : ItemsControl
{
    static Suchliste()
    {
        FocusableProperty.OverrideMetadata(typeof(Suchliste), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Die Einträge (Container) in ihrer Reihenfolge.</summary>
    public IReadOnlyList<Listeneintrag> Eintraege()
    {
        var e = new List<Listeneintrag>();
        for (var i = 0; i < Items.Count; i++)
            if (ItemContainerGenerator.ContainerFromIndex(i) is Listeneintrag l) e.Add(l);
        return e;
    }

    protected override DependencyObject GetContainerForItemOverride() => new Listeneintrag();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is Listeneintrag;

    protected override AutomationPeer OnCreateAutomationPeer() => new Suchlisteflaeche(this);
}

/// <summary>Automation der <see cref="Suchliste"/>: eine Liste namens "Ergebnisse".</summary>
internal sealed class Suchlisteflaeche(Suchliste liste) : FrameworkElementAutomationPeer(liste)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(Suchliste);

    protected override string GetNameCore()
    {
        var name = AutomationProperties.GetName(liste);
        return string.IsNullOrEmpty(name) ? "Ergebnisse" : name;
    }
}

/// <summary>
/// Automation des <see cref="Suchfeld"/>: ein Kombinationsfeld mit ExpandCollapse (die Liste) und Value (der Text); der Name
/// aus AutomationProperties.Name, sonst aus dem Platzhalter, sonst "Suche".
/// </summary>
internal sealed class Suchfeldflaeche(Suchfeld feld) : FrameworkElementAutomationPeer(feld), IExpandCollapseProvider, IValueProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;

    protected override string GetClassNameCore() => nameof(Suchfeld);

    protected override string GetNameCore() => feld.Benennung();

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.ExpandCollapse or PatternInterface.Value ? this : base.GetPattern(patternInterface);

    public ExpandCollapseState ExpandCollapseState => feld.IstOffen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        feld.SetCurrentValue(Suchfeld.IstOffenProperty, true);
    }

    public void Collapse()
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        feld.SetCurrentValue(Suchfeld.IstOffenProperty, false);
    }

    public string Value => feld.Text ?? "";

    public bool IsReadOnly => !feld.IsEnabled;

    public void SetValue(string value)
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        feld.SetCurrentValue(Suchfeld.TextProperty, value ?? "");
    }
}
