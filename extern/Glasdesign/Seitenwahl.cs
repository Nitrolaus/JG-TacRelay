// Glasdesign - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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

/// <summary>
/// Ein Eintrag der <see cref="Seitenwahl"/>: eine Seitenzahl oder eine Lücke ("..."). Ein Eintrag behält seine Seite, solange
/// sie zu sehen ist; nur Einträge, deren Seite herausfällt, wandern an einen neuen Platz und wechseln dort Zahl und Stand
/// (die Knöpfe bleiben dieselben). Änderungen meldet er über INotifyPropertyChanged.
/// </summary>
public sealed class Seiteneintrag : INotifyPropertyChanged
{
    private int? _nummer;
    private bool _aktuell;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Die Seite (von 1 an); null ist eine Lücke.</summary>
    public int? Nummer
    {
        get => _nummer;
        internal set
        {
            if (_nummer == value) return;
            _nummer = value;
            Melden(nameof(Nummer), nameof(IstLuecke), nameof(Text), nameof(Name));
        }
    }

    /// <summary>Die aktuelle Seite.</summary>
    public bool Aktuell
    {
        get => _aktuell;
        internal set
        {
            if (_aktuell == value) return;
            _aktuell = value;
            Melden(nameof(Aktuell), nameof(Status));
        }
    }

    public bool IstLuecke => Nummer is null;

    /// <summary>Was der Knopf zeigt: die Zahl, bei einer Lücke "...".</summary>
    public string Text => Nummer?.ToString(CultureInfo.CurrentCulture) ?? "...";

    /// <summary>Der Automation-Name des Knopfs: "Seite 3".</summary>
    public string Name => Nummer is { } n ? "Seite " + n.ToString(CultureInfo.CurrentCulture) : "weitere Seiten";

    /// <summary>Der Automation-Status: "aktuelle Seite" oder leer.</summary>
    public string Status => Aktuell ? Krumenflaeche.AktuelleSeite : "";

    private void Melden(params string[] namen)
    {
        if (PropertyChanged is not { } h) return;
        foreach (var n in namen) h(this, new PropertyChangedEventArgs(n));
    }
}

/// <summary>
/// Seitenwahl (seit 0.5.0-beta.9, Stilfamilien 2 Stufe 2): Zurück, die Seitenzahlen um die aktuelle (<see cref="Nachbarn"/>
/// links und rechts, dazu immer die erste und die letzte; Lücken als "..."), Weiter. <see cref="Seite"/> zählt von 1 bis
/// <see cref="Seiten"/>. Befehle <see cref="Zurueck"/>, <see cref="Weiter"/> und <see cref="Gehe"/> (Parameter: die Seite);
/// Zurück ist auf der ersten Seite gesperrt, Weiter auf der letzten. Tastatur: Tab durch die Knöpfe, Enter und Leertaste
/// lösen aus, Pfeil links und rechts blättern, Pos1 und Ende springen an den Anfang und ans Ende; der Fokus geht danach auf
/// die aktuelle Seite. Jede Änderung meldet <see cref="SeiteGeaendert"/>. Automation: "Seite 2 von 9" (Name, wenn keiner
/// gesetzt ist, und Status), dazu RangeValue. Stile: Glas.Seitenwahl, Glas.Taktil.Seitenwahl, Glas.Taktil.Seitenwahl.Leiste.
/// </summary>
[TemplatePart(Name = TeilZurueck, Type = typeof(ButtonBase))]
[TemplatePart(Name = TeilWeiter, Type = typeof(ButtonBase))]
public class Seitenwahl : Control
{
    public const string TeilZurueck = "PART_Zurueck";
    public const string TeilWeiter = "PART_Weiter";

    public static readonly DependencyProperty SeiteProperty = DependencyProperty.Register(
        nameof(Seite), typeof(int), typeof(Seitenwahl),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SeiteNeu, (d, v) => ((Seitenwahl)d).Begrenzen((int)v)));

    public static readonly DependencyProperty SeitenProperty = DependencyProperty.Register(
        nameof(Seiten), typeof(int), typeof(Seitenwahl), new FrameworkPropertyMetadata(1, SeitenNeu, (_, v) => Math.Max(1, (int)v)));

    /// <summary>Wie viele Zahlen links und rechts der aktuellen stehen (Vorgabe 1); 0 zeigt nur erste, aktuelle und letzte.</summary>
    public static readonly DependencyProperty NachbarnProperty = DependencyProperty.Register(
        nameof(Nachbarn), typeof(int), typeof(Seitenwahl), new FrameworkPropertyMetadata(1, (d, _) => ((Seitenwahl)d).Ordnen(), (_, v) => Math.Max(0, (int)v)));

    public static readonly DependencyProperty ZurueckTextProperty = DependencyProperty.Register(
        nameof(ZurueckText), typeof(object), typeof(Seitenwahl), new FrameworkPropertyMetadata("Zurück"));

    public static readonly DependencyProperty WeiterTextProperty = DependencyProperty.Register(
        nameof(WeiterText), typeof(object), typeof(Seitenwahl), new FrameworkPropertyMetadata("Weiter"));

    private static readonly DependencyPropertyKey EintraegeKey = DependencyProperty.RegisterReadOnly(
        nameof(Eintraege), typeof(ReadOnlyObservableCollection<Seiteneintrag>), typeof(Seitenwahl), new FrameworkPropertyMetadata(null));

    /// <summary>Die Einträge zwischen Zurück und Weiter (für die Vorlagen; ItemsSource der Seitenzahlen).</summary>
    public static readonly DependencyProperty EintraegeProperty = EintraegeKey.DependencyProperty;

    public static readonly RoutedEvent SeiteGeaendertEvent = EventManager.RegisterRoutedEvent(
        nameof(SeiteGeaendert), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<int>), typeof(Seitenwahl));

    /// <summary>Eine Seite zurück (gesperrt auf der ersten).</summary>
    public static RoutedUICommand Zurueck { get; } = new("Zurück", nameof(Zurueck), typeof(Seitenwahl));

    /// <summary>Eine Seite weiter (gesperrt auf der letzten).</summary>
    public static RoutedUICommand Weiter { get; } = new("Weiter", nameof(Weiter), typeof(Seitenwahl));

    /// <summary>Zur Seite im Parameter (Zahl oder Text einer Zahl).</summary>
    public static RoutedUICommand Gehe { get; } = new("Gehe zu Seite", nameof(Gehe), typeof(Seitenwahl));

    private readonly ObservableCollection<Seiteneintrag> _eintraege = [];

    static Seitenwahl()
    {
        FocusableProperty.OverrideMetadata(typeof(Seitenwahl), new FrameworkPropertyMetadata(false));
        IsEnabledProperty.OverrideMetadata(typeof(Seitenwahl), new UIPropertyMetadata(true, (_, _) => CommandManager.InvalidateRequerySuggested()));
        CommandManager.RegisterClassCommandBinding(typeof(Seitenwahl), new CommandBinding(Zurueck,
            (s, _) => ((Seitenwahl)s).Gehen(((Seitenwahl)s).Seite - 1), (s, e) => e.CanExecute = ((Seitenwahl)s).Erlaubt(((Seitenwahl)s).Seite - 1)));
        CommandManager.RegisterClassCommandBinding(typeof(Seitenwahl), new CommandBinding(Weiter,
            (s, _) => ((Seitenwahl)s).Gehen(((Seitenwahl)s).Seite + 1), (s, e) => e.CanExecute = ((Seitenwahl)s).Erlaubt(((Seitenwahl)s).Seite + 1)));
        CommandManager.RegisterClassCommandBinding(typeof(Seitenwahl), new CommandBinding(Gehe,
            (s, e) =>
            {
                if (Zahl(e.Parameter) is { } n) ((Seitenwahl)s).Gehen(n);
            },
            (s, e) => e.CanExecute = Zahl(e.Parameter) is { } n && ((Seitenwahl)s).Erlaubt(n)));
    }

    public Seitenwahl()
    {
        SetValue(EintraegeKey, new ReadOnlyObservableCollection<Seiteneintrag>(_eintraege));
        Ordnen();
    }

    public int Seite { get => (int)GetValue(SeiteProperty); set => SetValue(SeiteProperty, value); }
    public int Seiten { get => (int)GetValue(SeitenProperty); set => SetValue(SeitenProperty, value); }
    public int Nachbarn { get => (int)GetValue(NachbarnProperty); set => SetValue(NachbarnProperty, value); }
    public object ZurueckText { get => GetValue(ZurueckTextProperty); set => SetValue(ZurueckTextProperty, value); }
    public object WeiterText { get => GetValue(WeiterTextProperty); set => SetValue(WeiterTextProperty, value); }
    public ReadOnlyObservableCollection<Seiteneintrag> Eintraege => (ReadOnlyObservableCollection<Seiteneintrag>)GetValue(EintraegeProperty);

    public event RoutedPropertyChangedEventHandler<int> SeiteGeaendert
    {
        add => AddHandler(SeiteGeaendertEvent, value);
        remove => RemoveHandler(SeiteGeaendertEvent, value);
    }

    /// <summary>"Seite 2 von 9": Automation-Name (ohne eigenen) und Status.</summary>
    public string Beschreibung => string.Create(CultureInfo.CurrentCulture, $"Seite {Seite} von {Seiten}");

    /// <summary>
    /// Die Seitenzahlen für <paramref name="seite"/> von <paramref name="seiten"/>: immer die erste und die letzte, dazu
    /// <paramref name="nachbarn"/> links und rechts der aktuellen; eine Lücke ist null. Fehlt dazwischen nur eine einzige
    /// Seite, steht sie da statt der Lücke ("..." ersetzt immer mindestens zwei Seiten).
    /// </summary>
    public static IReadOnlyList<int?> Seitenliste(int seite, int seiten, int nachbarn)
    {
        seiten = Math.Max(1, seiten);
        seite = Math.Clamp(seite, 1, seiten);
        nachbarn = Math.Max(0, nachbarn);
        var zeigen = new SortedSet<int> { 1, seiten };
        for (var i = seite - nachbarn; i <= seite + nachbarn; i++)
            if (i >= 1 && i <= seiten) zeigen.Add(i);
        var liste = new List<int?>();
        var vorher = 0;
        foreach (var n in zeigen)
        {
            if (n - vorher == 2) liste.Add(n - 1);
            else if (n - vorher > 2) liste.Add(null);
            liste.Add(n);
            vorher = n;
        }
        return liste;
    }

    /// <summary>Geht zu <paramref name="seite"/>, wenn es sie gibt und die Seitenwahl bedienbar ist; wahr, wenn sie jetzt gilt.</summary>
    public bool Gehen(int seite)
    {
        if (!Erlaubt(seite)) return false;
        SetCurrentValue(SeiteProperty, seite);
        return Seite == seite;
    }

    private bool Erlaubt(int seite) => IsEnabled && seite >= 1 && seite <= Seiten;

    private static int? Zahl(object? parameter) => parameter switch
    {
        int n => n,
        double d when !double.IsNaN(d) => (int)Math.Round(d),
        string t when int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    private int Begrenzen(int seite) => Math.Clamp(seite, 1, Math.Max(1, Seiten));

    private static void SeiteNeu(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (Seitenwahl)d;
        w.Ordnen();
        CommandManager.InvalidateRequerySuggested();
        w.RaiseEvent(new RoutedPropertyChangedEventArgs<int>((int)e.OldValue, (int)e.NewValue, SeiteGeaendertEvent));
        if (AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged) && UIElementAutomationPeer.FromElement(w) is { } peer)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, (double)(int)e.OldValue, (double)(int)e.NewValue);
    }

    private static void SeitenNeu(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (Seitenwahl)d;
        w.CoerceValue(SeiteProperty);
        w.Ordnen();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Bringt die Einträge auf den Stand von Seite, Seiten und Nachbarn. Ein Eintrag behält seine Seite, solange sie in der
    /// Liste bleibt (seit 0.5.0-beta.13): Der Knopf unter dem Fokus zeigt nach Enter auf einer Zahl weiter diese Seite, die jetzt
    /// die aktuelle ist (bis beta.12 rückten die Zahlen an Ort und Stelle weiter, und ein zweites Enter auf demselben Knopf
    /// blätterte noch eine Seite). Nur Einträge, deren Seite herausfällt, und die Lücken wandern an ihren neuen Platz (Move: der
    /// Knopf bleibt derselbe, nur seine Zahl wechselt); bleibende Einträge werden nie verschoben.
    /// </summary>
    private void Ordnen()
    {
        var liste = Seitenliste(Seite, Seiten, Nachbarn);
        var neu = new HashSet<int>(liste.OfType<int>());
        var bleibend = new Dictionary<int, Seiteneintrag>();
        foreach (var e in _eintraege)
            if (e.Nummer is { } n && neu.Contains(n)) bleibend[n] = e;
        var behalten = new HashSet<Seiteneintrag>(bleibend.Values);
        var frei = _eintraege.Where(e => !behalten.Contains(e)).ToList();
        // Der Sollstand: bleibende Einträge an ihrer Seite, die übrigen Plätze aus den freien (Lücken bevorzugt an Lücken).
        var ziel = new List<Seiteneintrag>(liste.Count);
        foreach (var n in liste)
        {
            if (n is { } z && bleibend.TryGetValue(z, out var e))
            {
                ziel.Add(e);
                continue;
            }
            var k = frei.FindIndex(f => f.IstLuecke == (n is null));
            if (k < 0 && frei.Count > 0) k = 0;
            if (k < 0)
            {
                ziel.Add(new Seiteneintrag());
                continue;
            }
            ziel.Add(frei[k]);
            frei.RemoveAt(k);
        }
        foreach (var f in frei) _eintraege.Remove(f);
        for (var i = 0; i < ziel.Count; i++)
        {
            var soll = ziel[i];
            var j = _eintraege.IndexOf(soll);
            if (j == i) continue;
            if (j < 0) _eintraege.Insert(i, soll);
            else if (!behalten.Contains(soll)) _eintraege.Move(j, i);
            else
            {
                // Ein bleibender Eintrag steht weiter hinten: Davor stehen nur freie (die bleibenden behalten ihre Reihenfolge);
                // die wandern ans Ende und kommen später an ihren Platz.
                while (!ReferenceEquals(_eintraege[i], soll)) _eintraege.Move(i, _eintraege.Count - 1);
            }
        }
        for (var i = 0; i < liste.Count; i++)
        {
            _eintraege[i].Nummer = liste[i];
            _eintraege[i].Aktuell = liste[i] == Seite;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled) return;
        // Rechts nach links (FlowDirection RightToLeft) steht Weiter links: Pfeil links blättert dann weiter (seit 0.5.0-beta.13).
        var rtl = FlowDirection == FlowDirection.RightToLeft;
        int? ziel = e.Key switch
        {
            Key.Left => rtl ? Seite + 1 : Seite - 1,
            Key.Right => rtl ? Seite - 1 : Seite + 1,
            Key.Home => 1,
            Key.End => Seiten,
            _ => null,
        };
        if (ziel is not { } z) return;
        e.Handled = true;
        if (!Gehen(z)) return;
        if (IsKeyboardFocusWithin) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => AktuellerKnopf()?.Focus()));
    }

    /// <summary>Der Knopf der aktuellen Seite in der Vorlage, sobald sie angewandt ist; sonst null.</summary>
    public ButtonBase? AktuellerKnopf() => Nachfahren(this).OfType<ButtonBase>().FirstOrDefault(k => k.DataContext is Seiteneintrag { Aktuell: true });

    private static IEnumerable<DependencyObject> Nachfahren(DependencyObject wurzel)
    {
        var n = VisualTreeHelper.GetChildrenCount(wurzel);
        for (var i = 0; i < n; i++)
        {
            var kind = VisualTreeHelper.GetChild(wurzel, i);
            yield return kind;
            foreach (var weiter in Nachfahren(kind)) yield return weiter;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Seitenwahlflaeche(this);
}

/// <summary>Automation der <see cref="Seitenwahl"/>: eine Gruppe "Seite 2 von 9" mit RangeValue (Seite, 1 bis Seiten).</summary>
internal sealed class Seitenwahlflaeche(Seitenwahl wahl) : FrameworkElementAutomationPeer(wahl), IRangeValueProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(Seitenwahl);

    protected override string GetLocalizedControlTypeCore() => "Seitenwahl";

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? wahl.Beschreibung : name;
    }

    protected override string GetItemStatusCore() => wahl.Beschreibung;

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

    public double Value => wahl.Seite;
    public double Minimum => 1;
    public double Maximum => wahl.Seiten;
    public double SmallChange => 1;
    public double LargeChange => 1;
    public bool IsReadOnly => !wahl.IsEnabled;

    public void SetValue(double value)
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        if (double.IsNaN(value) || value < 1 || value > wahl.Seiten) throw new ArgumentOutOfRangeException(nameof(value));
        wahl.Gehen((int)Math.Round(value));
    }
}
