// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Funk.Verwaltung.Modelle;

namespace Funk.Verwaltung.Ansichten;

/// <summary>Ansicht des Bereichs Verwaltung; der Zustand liegt im <see cref="Modelle.VerwaltungViewModel"/>.</summary>
public partial class VerwaltungAnsicht : UserControl
{
    /// <summary>
    /// Ab dieser Breite der Verwaltung steht die Navigation links (seit 0.8.0); schmaler (Wer ist da offen, kleines Fenster) steht sie oben,
    /// damit die Bereiche ihre Breite behalten. Bei 1120 Bildpunkten Fensterbreite ist die Verwaltung 920 breit.
    /// </summary>
    internal const double NaviLinksAb = 900;

    /// <summary>Ab dieser Breite steht das angeklickte Feld rechts neben dem Funkfeld, schmaler darunter.</summary>
    internal const double FeldRechtsAb = 760;

    /// <summary>Breite des Felds rechts neben dem Funkfeld.</summary>
    private const double FeldBreite = 300;

    public VerwaltungAnsicht()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is VerwaltungViewModel alt) alt.Abfrage.PropertyChanged -= AbfrageGeaendert;
            if (e.NewValue is VerwaltungViewModel neu) neu.Abfrage.PropertyChanged += AbfrageGeaendert;
        };
    }

    /// <summary>Navigation links oder oben, je nach Breite (seit 0.8.0).</summary>
    private void WurzelGroesse(object sender, SizeChangedEventArgs e)
    {
        var links = e.NewSize.Width >= NaviLinksAb;
        NaviLinks.Visibility = links ? Visibility.Visible : Visibility.Collapsed;
        NaviOben.Visibility = links ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Das angeklickte Feld rechts neben dem Funkfeld oder darunter, je nach Breite des Rollen-Editors (seit 0.8.0).</summary>
    private void RollenGroesse(object sender, SizeChangedEventArgs e)
    {
        var rechts = e.NewSize.Width >= FeldRechtsAb;
        Grid.SetRow(FeldKarte, rechts ? 0 : 1);
        Grid.SetColumn(FeldKarte, rechts ? 1 : 0);
        FeldKarte.Width = rechts ? FeldBreite : double.NaN;
        FeldKarte.Margin = rechts ? new Thickness(12, 0, 0, 12) : new Thickness(0, 0, 0, 12);
    }

    /// <summary>
    /// Seit 0.6.4: Öffnet sich die Code-Abfrage (frische Bestätigung, 3.9), rollt sie ins Bild, und der Cursor steht im Codefeld.
    /// Sie steht oben in der Verwaltung; wer unten auf einer Mitglieds- oder Antragskarte "Endgültig löschen" oder "Freigeben"
    /// klickte, sah sie nicht, und die Handlung schien nichts zu tun (Sarevocs Test vom 04.10.2026). Seit 0.8.0 rollt der eigene Bildlauf
    /// der Verwaltung.
    /// </summary>
    private void AbfrageGeaendert(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CodeAbfrage.Offen) || sender is not CodeAbfrage { Offen: true }) return;
        // Erst nach dem Layout: Vorher ist die Karte noch eingeklappt und hat keine Lage, in die sich rollen ließe.
        _ = Dispatcher.InvokeAsync(() =>
        {
            AbfrageKarte.BringIntoView();
            AbfrageCode.Focus();
        }, DispatcherPriority.Loaded);
    }
}
