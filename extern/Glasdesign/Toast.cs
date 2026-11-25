// Glasdesign - Maintainer: Sarevoc
using System.Collections;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Glasdesign;

/// <summary>
/// Toast (seit 0.5.0-beta.8): eine kurze Meldung als schwebende Fläche, wahlweise mit Schließknopf (<see cref="Schliessbar"/>,
/// ein echter, fokussierbarer Knopf mit dem Automation-Namen "Schließen" und dem Befehl <see cref="Schliessen"/>; Escape im
/// Toast schließt ebenso). Mit <see cref="Dauer"/> schließt er sich selbst, aber nur auf dem Bildschirm (im Speicher, in Tests
/// und auf Glints Bühne bleibt er stehen); seit 0.5.0-beta.12 unabhängig vom Windows-Schalter "Animationen anzeigen", denn das
/// Selbstschließen ist eine Funktion, keine Animation. Solange die Maus darüber steht oder der Tastaturfokus darin ist, hält
/// die Uhr an und läuft danach mit der Restzeit weiter.
/// Geschlossen meldet er <see cref="Geschlossen"/>; ein <see cref="Toaststapel"/> nimmt ihn dann heraus, sonst blendet er sich
/// selbst aus, wenn niemand das Ereignis als behandelt markiert. Für Bildschirmleser
/// meldet er sich höflich (LiveSetting Polite), sobald er erscheint. Die Familie kommt als Stil: Glas.Toast,
/// Glas.Taktil.Toast, Glas.Glutglas.Toast.
/// </summary>
public class Toast : ContentControl
{
    public static readonly DependencyProperty SchliessbarProperty = DependencyProperty.Register(
        nameof(Schliessbar), typeof(bool), typeof(Toast), new FrameworkPropertyMetadata(true, (d, _) => CommandManager.InvalidateRequerySuggested()));

    /// <summary>Nach dieser Zeit schließt sich der Toast selbst (nur auf dem Bildschirm); TimeSpan.Zero (Vorgabe): er bleibt.</summary>
    public static readonly DependencyProperty DauerProperty = DependencyProperty.Register(
        nameof(Dauer), typeof(TimeSpan), typeof(Toast), new FrameworkPropertyMetadata(TimeSpan.Zero, (d, _) => ((Toast)d).DauerGeaendert()));

    public static readonly RoutedEvent GeschlossenEvent = EventManager.RegisterRoutedEvent(
        nameof(Geschlossen), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Toast));

    /// <summary>Schließt den Toast, an dem er ausgeführt wird (Schließknopf, Escape).</summary>
    public static RoutedUICommand Schliessen { get; } = new("Schließen", nameof(Schliessen), typeof(Toast));

    private DispatcherTimer? _uhr;
    private TimeSpan _rest;
    private long _gestartet;
    private bool _maus;
    private bool _fokus;

    static Toast()
    {
        AutomationProperties.LiveSettingProperty.OverrideMetadata(typeof(Toast), new FrameworkPropertyMetadata(AutomationLiveSetting.Polite));
        FocusableProperty.OverrideMetadata(typeof(Toast), new FrameworkPropertyMetadata(false));
        IsEnabledProperty.OverrideMetadata(typeof(Toast), new UIPropertyMetadata(true, (d, _) => ((Toast)d).Pruefen()));
        CommandManager.RegisterClassCommandBinding(typeof(Toast), new CommandBinding(Schliessen,
            (s, e) => ((Toast)s).Zumachen(), (s, e) => e.CanExecute = ((Toast)s).Schliessbar && !((Toast)s).IstGeschlossen));
    }

    public Toast()
    {
        Loaded += (_, _) =>
        {
            Pruefen();
            Melden();
        };
        Unloaded += (_, _) => Anhalten();
        IsVisibleChanged += (_, _) => Pruefen();
        IsKeyboardFocusWithinChanged += (_, _) => Pruefen();
        // Der Tastaturfokus im Toast (auch auf dem Schließknopf oder einem Knopf im Inhalt): die Fokusereignisse steigen auf.
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) => FokusGeaendert(e.NewFocus)), true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) => FokusGeaendert(e.NewFocus)), true);
    }

    private void FokusGeaendert(IInputElement? neu)
    {
        _fokus = neu is DependencyObject d && (ReferenceEquals(d, this) || (d is Visual v && IsAncestorOf(v)));
        Pruefen();
    }

    public bool Schliessbar { get => (bool)GetValue(SchliessbarProperty); set => SetValue(SchliessbarProperty, value); }
    public TimeSpan Dauer { get => (TimeSpan)GetValue(DauerProperty); set => SetValue(DauerProperty, value); }

    /// <summary>Ob der Toast geschlossen wurde (ein zweites Schließen meldet nichts mehr).</summary>
    public bool IstGeschlossen { get; private set; }

    public event RoutedEventHandler Geschlossen
    {
        add => AddHandler(GeschlossenEvent, value);
        remove => RemoveHandler(GeschlossenEvent, value);
    }

    /// <summary>Ob die Uhr des Selbstschließens gerade läuft (für die Tests).</summary>
    internal bool UhrLaeuft => _uhr?.IsEnabled == true;

    /// <summary>Die Zeit bis zum Selbstschließen, die beim letzten Anhalten übrig war (für die Tests).</summary>
    internal TimeSpan Rest => _rest;

    /// <summary>
    /// Schließt den Toast und meldet <see cref="Geschlossen"/> (einmal). Behandelt niemand das Ereignis (Handled; der
    /// <see cref="Toaststapel"/> tut es, wenn er den Toast herausnimmt), blendet sich der Toast selbst aus (Visibility
    /// Collapsed, seit 0.5.0-beta.13; bis beta.12 blieb ein Toast ohne Stapel mit gesperrtem Schließknopf stehen).
    /// </summary>
    public void Zumachen()
    {
        if (IstGeschlossen) return;
        IstGeschlossen = true;
        Anhalten();
        CommandManager.InvalidateRequerySuggested();
        var meldung = new RoutedEventArgs(GeschlossenEvent, this);
        RaiseEvent(meldung);
        if (!meldung.Handled) SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || !Schliessbar) return;
        Zumachen();
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _maus = true;
        Pruefen();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _maus = false;
        Pruefen();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Melden();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Meldungsflaeche(this, "Toast");

    /// <summary>Den Bildschirmleser auf die neue Meldung hinweisen (LiveRegionChanged), wenn einer zuhört.</summary>
    private void Melden()
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void DauerGeaendert()
    {
        _rest = Dauer;
        if (UhrLaeuft) _uhr!.Stop();
        Pruefen();
    }

    /// <summary>
    /// Läuft die Uhr? Nur, solange eine Dauer gesetzt ist, der Toast offen, bedienbar und auf dem Bildschirm steht und weder die
    /// Maus darüber noch der Tastaturfokus darin ist. Der Windows-Schalter "Animationen anzeigen" spielt seit 0.5.0-beta.12 keine
    /// Rolle mehr: Das Selbstschließen ist keine Animation, und ohne es bliebe ein Toast für immer stehen.
    /// </summary>
    internal void Pruefen()
    {
        var soll = !IstGeschlossen && Dauer > TimeSpan.Zero && IsEnabled && Animationen.AufDemBildschirm(this) && !_maus && !_fokus && !IsKeyboardFocusWithin;
        if (soll && !UhrLaeuft) Starten();
        else if (!soll && UhrLaeuft) Anhalten();
    }

    /// <summary>Ob die Uhr gerade wegen des Tastaturfokus im Toast steht (für die Tests).</summary>
    internal bool FokusDarin => _fokus || IsKeyboardFocusWithin;

    private void Starten()
    {
        if (_rest <= TimeSpan.Zero) _rest = Dauer;
        if (_uhr is null)
        {
            _uhr = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            _uhr.Tick += (_, _) =>
            {
                _uhr!.Stop();
                // Länger als die Uhr auf einmal zählen kann (gut 24 Tage): mit der Restzeit weiter.
                var vorbei = Stopwatch.GetElapsedTime(_gestartet);
                if (_rest > Laengste && vorbei < _rest)
                {
                    _rest -= vorbei;
                    Starten();
                    return;
                }
                _rest = TimeSpan.Zero;
                Zumachen();
            };
        }
        // DispatcherTimer nimmt höchstens Int32.MaxValue Millisekunden; eine längere Dauer läuft in Abschnitten (seit 0.5.0-beta.13,
        // bis beta.12 warf eine Dauer über 24,8 Tagen).
        _uhr.Interval = _rest < Laengste ? _rest : Laengste;
        _gestartet = Stopwatch.GetTimestamp();
        _uhr.Start();
    }

    /// <summary>Die längste Zeit, die ein DispatcherTimer auf einmal zählt.</summary>
    internal static readonly TimeSpan Laengste = TimeSpan.FromMilliseconds(int.MaxValue);

    private void Anhalten()
    {
        if (!UhrLaeuft) return;
        _uhr!.Stop();
        var vorbei = Stopwatch.GetElapsedTime(_gestartet);
        _rest = vorbei >= _rest ? TimeSpan.FromMilliseconds(1) : _rest - vorbei;
    }
}

/// <summary>
/// Stapel für Toasts (seit 0.5.0-beta.8): zeigt sie untereinander mit <see cref="Abstand"/> (DIP, auf ganze Gerätepixel
/// gerundet). <see cref="Zeigen"/> legt einen neuen Toast unten an und gibt ihn zurück; schließt er sich, nimmt der Stapel
/// ihn heraus (über ItemsSource aus der Quelle, wenn sie eine veränderbare Liste ist). Die Familie der Toasts kommt über
/// ItemContainerStyle (etwa Glas.Taktil.Toast), sonst gilt der Vorgabestil.
/// </summary>
public class Toaststapel : ItemsControl
{
    public static readonly DependencyProperty AbstandProperty = DependencyProperty.Register(
        nameof(Abstand), typeof(double), typeof(Toaststapel), new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.Inherits));

    static Toaststapel()
    {
        FocusableProperty.OverrideMetadata(typeof(Toaststapel), new FrameworkPropertyMetadata(false));
    }

    public Toaststapel() => AddHandler(Toast.GeschlossenEvent, new RoutedEventHandler(ToastGeschlossen));

    public double Abstand { get => (double)GetValue(AbstandProperty); set => SetValue(AbstandProperty, value); }

    /// <summary>Zeigt <paramref name="inhalt"/> als neuen Toast unten im Stapel; mit <paramref name="dauer"/> schließt er sich selbst.</summary>
    public Toast Zeigen(object inhalt, TimeSpan? dauer = null)
    {
        var toast = new Toast { Content = inhalt };
        if (dauer is { } d) toast.Dauer = d;
        Items.Add(toast);
        return toast;
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is Toast;

    protected override DependencyObject GetContainerForItemOverride() => new Toast();

    /// <summary>
    /// Nimmt einen geschlossenen Toast heraus, an seiner Stelle (seit 0.5.0-beta.13; bis beta.12 nahm Items.Remove bei gleichen
    /// Einträgen, etwa zweimal "Gespeichert", den ersten gleichen statt des geschlossenen). Über ItemsSource nimmt der Stapel ihn
    /// aus der Quelle, wenn sie eine veränderbare Liste ist (etwa eine ObservableCollection); sonst bleibt das Ereignis
    /// unbehandelt, der Toast blendet sich selbst aus, und das Programm nimmt den Eintrag über <see cref="Toast.Geschlossen"/>
    /// aus seiner Quelle.
    /// </summary>
    private void ToastGeschlossen(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Toast toast) return;
        var i = ItemContainerGenerator.IndexFromContainer(toast);
        if (i < 0) return;
        if (ItemsSource is null)
        {
            Items.RemoveAt(i);
            e.Handled = true;
            return;
        }
        var eintrag = ItemContainerGenerator.ItemFromContainer(toast);
        if (ItemsSource is IList { IsReadOnly: false, IsFixedSize: false } quelle && i < quelle.Count && Equals(quelle[i], eintrag)
            && Items.SortDescriptions.Count == 0 && Items.Filter is null && Items.GroupDescriptions is not { Count: > 0 })
        {
            quelle.RemoveAt(i);
            e.Handled = true;
        }
    }
}

/// <summary>
/// Senkrechter Stapel mit gleichem Abstand (Toaststapel): Kinder untereinander, so breit wie das Panel, dazwischen
/// <see cref="Abstand"/>, auf ganze Gerätepixel gerundet.
/// </summary>
public sealed class Abstandsstapel : Panel
{
    public static readonly DependencyProperty AbstandProperty = Toaststapel.AbstandProperty.AddOwner(
        typeof(Abstandsstapel), new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.Inherits));

    public double Abstand { get => (double)GetValue(AbstandProperty); set => SetValue(AbstandProperty, value); }

    private double Luecke()
    {
        var s = Glasgeometrie.Skala(this);
        return Glasgeometrie.Raster(Math.Max(0, Abstand), s) / s;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double b = 0, h = 0;
        var n = 0;
        foreach (UIElement kind in InternalChildren)
        {
            kind.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (kind.Visibility == Visibility.Collapsed) continue;
            b = Math.Max(b, kind.DesiredSize.Width);
            h += kind.DesiredSize.Height;
            n++;
        }
        return new Size(b, h + Math.Max(0, n - 1) * Luecke());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        var luecke = Luecke();
        var erster = true;
        foreach (UIElement kind in InternalChildren)
        {
            if (kind.Visibility == Visibility.Collapsed)
            {
                kind.Arrange(new Rect(0, y, finalSize.Width, 0));
                continue;
            }
            if (!erster) y += luecke;
            erster = false;
            kind.Arrange(new Rect(0, y, finalSize.Width, kind.DesiredSize.Height));
            y += kind.DesiredSize.Height;
        }
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateMeasure();
}

/// <summary>
/// Automation der Meldungen (Toast, Hinweisbanner, Leerzustand): eine Gruppe, deren Name der Text ihres Inhalts (oder ihres
/// Titels) ist, wenn kein AutomationProperties.Name gesetzt ist.
/// </summary>
internal sealed class Meldungsflaeche(FrameworkElement element, string klasse) : FrameworkElementAutomationPeer(element)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => klasse;

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        if (!string.IsNullOrEmpty(name)) return name;
        return Owner switch
        {
            HeaderedContentControl k => Rueckmeldungstext.Von(k.Header),
            ContentControl k => Rueckmeldungstext.Von(k.Content),
            _ => "",
        };
    }
}

/// <summary>Der lesbare Text eines Inhalts für Automation-Namen: Text, TextBlock, AutomationProperties.Name, sonst ToString.</summary>
internal static class Rueckmeldungstext
{
    public static string Von(object? inhalt) => inhalt switch
    {
        null => "",
        string s => s,
        TextBlock t => t.Text,
        AccessText a => a.Text,
        DependencyObject d when AutomationProperties.GetName(d) is { Length: > 0 } n => n,
        Panel p => string.Join(" ", p.Children.OfType<object>().Select(Von).Where(t => t.Length > 0)),
        ContentControl c => Von(c.Content),
        Visual => "",
        _ => inhalt.ToString() ?? "",
    };
}
