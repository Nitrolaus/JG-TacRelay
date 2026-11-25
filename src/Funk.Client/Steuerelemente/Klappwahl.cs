// JG TacRelay - Maintainer: Sarevoc
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Klappfeld des Funkfelds (Stufe 3, Schritt 3.4): ein flacher Umschaltknopf, der eine Liste aufklappt, für die Rollenliste oben
/// links und den Ch.-Wähler (bis 0.8.0 auch für die Kanalwahl am Feldnamen, die es seit 0.8.1 nicht mehr gibt, Q44). Anders als eine
/// ComboBox wählt nur ein Klick oder Enter in der offenen Liste (<see cref="Waehlen"/>, sofort); Pfeiltasten in der Liste bewegen nur
/// die Markierung. Am zugeklappten Feld gehen Mausrad und Pfeil hoch und runter einen Schritt (<see cref="Schritt"/> mit +1 oder -1);
/// das Modell wartet dann 400 ms Ruhe, bevor es sendet. Nicht wählbare Einträge (gesperrt, schon in einem anderen Feld) stehen grau
/// mit ihrem Grund im Tooltip da. Die Vorlage liegt im Funkfeld (<c>Klappwahl.Flach</c>) mit <c>PART_Popup</c> und <c>PART_Liste</c>.
/// </summary>
public sealed class Klappwahl : ToggleButton
{
    public static readonly DependencyProperty EintraegeProperty = DependencyProperty.Register(nameof(Eintraege), typeof(IEnumerable), typeof(Klappwahl));

    public static readonly DependencyProperty GewaehltProperty = DependencyProperty.Register(nameof(Gewaehlt), typeof(object), typeof(Klappwahl));

    public static readonly DependencyProperty WaehlenProperty = DependencyProperty.Register(nameof(Waehlen), typeof(ICommand), typeof(Klappwahl));

    public static readonly DependencyProperty SchrittProperty = DependencyProperty.Register(nameof(Schritt), typeof(ICommand), typeof(Klappwahl));

    public static readonly DependencyProperty EintragVorlageProperty = DependencyProperty.Register(nameof(EintragVorlage), typeof(DataTemplate), typeof(Klappwahl));

    public static readonly DependencyProperty ListenBreiteProperty = DependencyProperty.Register(nameof(ListenBreite), typeof(double), typeof(Klappwahl),
        new FrameworkPropertyMetadata(0.0));

    private Popup? _popup;

    /// <summary>
    /// Höchste Höhe der offenen Liste (0.9.1, Sarevoc 08.10.2026: "ohne Scrollen bei allen Ch."): Jede Ch.-Liste steht ganz da, auch die längste
    /// (Pack, Ruhe und 24 Einträge) und die Rollenliste mit 21 Rollen. Bis 0.9.0 waren es 300, dann sah man nur bis Ch. 14.
    /// </summary>
    public const double ListenHoeheMax = 700;
    private ListBox? _liste;

    /// <summary>Die Einträge der Liste (Rollen, Kanalnetze, Ch.).</summary>
    public IEnumerable? Eintraege
    {
        get => (IEnumerable?)GetValue(EintraegeProperty);
        set => SetValue(EintraegeProperty, value);
    }

    /// <summary>Der gerade gewählte Eintrag; die Liste markiert ihn beim Aufklappen.</summary>
    public object? Gewaehlt
    {
        get => GetValue(GewaehltProperty);
        set => SetValue(GewaehltProperty, value);
    }

    /// <summary>Ein Klick oder Enter auf einen wählbaren Eintrag; Parameter ist der Eintrag.</summary>
    public ICommand? Waehlen
    {
        get => (ICommand?)GetValue(WaehlenProperty);
        set => SetValue(WaehlenProperty, value);
    }

    /// <summary>Mausrad oder Pfeiltaste am zugeklappten Feld: +1 weiter, -1 zurück.</summary>
    public ICommand? Schritt
    {
        get => (ICommand?)GetValue(SchrittProperty);
        set => SetValue(SchrittProperty, value);
    }

    public DataTemplate? EintragVorlage
    {
        get => (DataTemplate?)GetValue(EintragVorlageProperty);
        set => SetValue(EintragVorlageProperty, value);
    }

    /// <summary>Mindestbreite der aufgeklappten Liste (Einheiten); 0 heißt so breit wie das Feld.</summary>
    public double ListenBreite
    {
        get => (double)GetValue(ListenBreiteProperty);
        set => SetValue(ListenBreiteProperty, value);
    }

    /// <summary>Der Inhalt des Klappfelds (die Liste mit Rahmen), für die Sichtproben, die ihn ohne Fenster zeichnen.</summary>
    public FrameworkElement? Liste => _popup?.Child as FrameworkElement;

    public override void OnApplyTemplate()
    {
        if (_liste is not null)
        {
            _liste.PreviewMouseLeftButtonUp -= ListeKlick;
            _liste.PreviewKeyDown -= ListeTaste;
        }
        if (_popup is not null)
        {
            _popup.Opened -= Aufgeklappt;
            _popup.Closed -= Zugeklappt;
        }
        base.OnApplyTemplate();
        _popup = GetTemplateChild("PART_Popup") as Popup;
        _liste = GetTemplateChild("PART_Liste") as ListBox;
        if (_liste is not null)
        {
            _liste.PreviewMouseLeftButtonUp += ListeKlick;
            _liste.PreviewKeyDown += ListeTaste;
        }
        if (_popup is not null)
        {
            _popup.Opened += Aufgeklappt;
            _popup.Closed += Zugeklappt;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (IsChecked != true && IsEnabled && Schritt is { } s)
        {
            var richtung = e.Delta < 0 ? 1 : -1;
            if (s.CanExecute(richtung)) s.Execute(richtung);
            e.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (IsChecked != true && e.Key is Key.Up or Key.Down && Schritt is { } s)
        {
            var richtung = e.Key == Key.Down ? 1 : -1;
            if (s.CanExecute(richtung)) s.Execute(richtung);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Aufgeklappt: den gewählten Eintrag markieren und die Liste in den Fokus, damit Pfeile und Enter dort wirken.</summary>
    private void Aufgeklappt(object? sender, EventArgs e)
    {
        // Ein Klick auf das Feld selbst klappt sonst zu und gleich wieder auf (das Klappfeld schließt beim Klick daneben).
        IsHitTestVisible = false;
        if (_liste is null) return;
        // Ohne die Bindung an Gewaehlt zu ersetzen: Pfeile in der Liste bewegen nur die Markierung, beim nächsten Aufklappen gilt wieder die Wahl.
        _liste.SetCurrentValue(Selector.SelectedItemProperty, Gewaehlt);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_liste.SelectedItem is { } s) _liste.ScrollIntoView(s);
            var ziel = (_liste.SelectedItem is { } g ? _liste.ItemContainerGenerator.ContainerFromItem(g) : null) as UIElement ?? _liste;
            ziel.Focus();
        }));
    }

    private void Zugeklappt(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => IsHitTestVisible = true));

    private void ListeKlick(object sender, MouseButtonEventArgs e)
    {
        if (_liste is null || e.OriginalSource is not DependencyObject quelle) return;
        if (ItemsControl.ContainerFromElement(_liste, quelle) is not ListBoxItem { IsEnabled: true } eintrag) return;
        e.Handled = true;
        Uebernehmen(_liste.ItemContainerGenerator.ItemFromContainer(eintrag));
    }

    private void ListeTaste(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Space when _liste?.SelectedItem is { } s && _liste.ItemContainerGenerator.ContainerFromItem(s) is ListBoxItem { IsEnabled: true }:
                e.Handled = true;
                Uebernehmen(s);
                break;
            case Key.Escape:
                e.Handled = true;
                IsChecked = false;
                Focus();
                break;
        }
    }

    private void Uebernehmen(object eintrag)
    {
        IsChecked = false;
        if (Waehlen is { } w && w.CanExecute(eintrag)) w.Execute(eintrag);
        Focus();
    }
}
