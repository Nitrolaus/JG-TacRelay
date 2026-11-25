// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using Funk.Client.Steuerelemente;

namespace Funk.Client.Fenster;

/// <summary>
/// Sprachaktivierung genau einstellen (A26-A28), seit 0.10.0 nur noch als Seite (Unterpunkt unter Einstellungen, Strg+T): mit Überschrift,
/// links die Verlaufsgrafik in fester Größe, darunter die Mindest-Funklautstärke, rechts daneben Schalter und Werte, in schmalen Fenstern
/// darunter. Bis 0.9.3 stand derselbe Bereich auch unten im Hauptfenster unter seiner Leiste (Stufe 2b, zwei Spalten, die Grafik füllte den
/// gezogenen Bereich); seit 0.10.0 steht dort die Statuszeile (Q87). Die Hörprobe hier entfiel mit 0.10.0 (E19).
/// </summary>
public partial class Schwellenbereich : UserControl
{
    /// <summary>
    /// Feste Größe der Verlaufsgrafik: seit ui-fix 0.6.2 groß (Sarevoc 04.10.2026: die 244 x 120 aus Klangstufe C1 waren viel zu klein, die
    /// Beschriftungen überlappten), Pegelfläche 600 x 260 und Sprachfläche 600 x 110 (<see cref="SchwellenGrafik.SeitenGroesse"/>). Sie wächst und
    /// schrumpft nicht mit dem Fenster; die Seite rollt um sie herum.
    /// </summary>
    public static Size SeitenGrafik => SchwellenGrafik.SeitenGroesse;

    /// <summary>
    /// So breit (Rand 20 und 14, Grafik, Abstand 16, Spalte 300), damit die Einstellungen rechts neben der Grafik stehen; schmaler stehen
    /// sie darunter (ui-fix 0.6.2).
    /// </summary>
    public static double SeiteRechtsAb => 20 + SeitenGrafik.Width + 16 + 300 + 14;

    private bool _darunter;

    public Schwellenbereich()
    {
        InitializeComponent();
        AnordnungSetzen();
    }

    /// <summary>Stehen die Einstellungen gerade unter der Grafik statt rechts daneben (Tests)?</summary>
    public bool EinstellungenDarunter => _darunter;

    /// <summary>Vor dem Messen: die Einstellungen rechts, wenn Platz ist, sonst darunter (ui-fix 0.6.2).</summary>
    protected override Size MeasureOverride(Size constraint)
    {
        EinstellungenSetzen(constraint.Width < SeiteRechtsAb);
        return base.MeasureOverride(constraint);
    }

    private void EinstellungenSetzen(bool darunter)
    {
        if (darunter == _darunter) return;
        _darunter = darunter;
        Grid.SetRow(EinstellRahmen, darunter ? 2 : 1);
        Grid.SetColumn(EinstellRahmen, darunter ? 0 : 1);
        Grid.SetColumnSpan(EinstellRahmen, darunter ? 2 : 1);
        Raster.ColumnDefinitions[1].Width = new GridLength(darunter ? 0 : 300);
        // Darunter rollt die Seite, nicht die Spalte; die Spalte bleibt so schmal wie rechts.
        EinstellRahmen.VerticalScrollBarVisibility = darunter ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        EinstellRahmen.Margin = darunter ? new Thickness(-8, 16, 0, 0) : new Thickness(-8, 0, 0, 0);
        // Darunter braucht die Grafik keinen Abstand nach rechts; mit ihm wäre das Raster in der Mindestbreite 7 zu schmal und schnitte ab
        // (auch die Schatten der Glasknöpfe links).
        Links.Margin = darunter ? new Thickness(0) : new Thickness(0, 0, 16, 0);
        if (darunter)
        {
            EinstellRahmen.Width = 300 + 8;
            EinstellRahmen.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            EinstellRahmen.ClearValue(WidthProperty);
            EinstellRahmen.ClearValue(HorizontalAlignmentProperty);
        }
    }

    /// <summary>Die Verlaufsgrafik (Tastatur: Pfeile verschieben die Linie mit dem runden Griff).</summary>
    public SchwellenGrafik Verlaufsgrafik => Grafik;

    /// <summary>
    /// Die Anordnung als Seite: Überschrift, eine feste Grafik oben links, die Zeile so hoch wie sie; die Mindest-Funklautstärke links unter der
    /// Legende. Rechts hätte sie in Lernend (Korridor von/bis, bis die Stimme bekannt ist) die Seite in 960 x 700 rollen lassen. Bis 0.9.3 stand
    /// darunter auch die Hörprobe mit Mithören (seit 0.10.0 eine Hörprobe, auf Start, und Mithören unter Senden > Erweitert, E19).
    /// </summary>
    private void AnordnungSetzen()
    {
        Ueberschrift.Visibility = Visibility.Visible;
        // Punkt 9 (Klangstufe C1): eine feste Grafik oben links, die Zeile so hoch wie sie.
        Grafik.Width = SeitenGrafik.Width;
        Grafik.Height = SeitenGrafik.Height;
        Grafik.HorizontalAlignment = HorizontalAlignment.Left;
        Grafik.VerticalAlignment = VerticalAlignment.Top;
        Links.RowDefinitions[1].Height = GridLength.Auto;
        // ui-fix 0.6.2: Die linke Spalte so breit wie die Grafik, die Einstellungen direkt rechts daneben (sonst stünden sie am rechten Rand).
        Links.Width = SeitenGrafik.Width;
        Raster.ColumnDefinitions[0].Width = GridLength.Auto;
        Umsetzen(MindestTeil, 3);
    }

    /// <summary>Setzt einen Teil aus der rechten Spalte links unter die Legende, in <paramref name="zeile"/>.</summary>
    private void Umsetzen(FrameworkElement teil, int zeile)
    {
        if (ReferenceEquals(teil.Parent, Links)) return;
        ((Panel)teil.Parent).Children.Remove(teil);
        Grid.SetRow(teil, zeile);
        Links.Children.Add(teil);
    }
}
