// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Funk.Client.Modelle;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Die Linien im Stammbaum der Raid-Seite (0.11.2): liegt in der Lücke zwischen einem Knoten und der Liste seiner Kinder, so hoch wie die
/// Liste, und zieht von der Mitte links (dort steht der Knoten) zu jedem Kind eine Linie wie im Stammbaum: waagerecht bis zur Mitte der
/// Lücke, senkrecht, waagerecht zum Kind, die beiden Ecken leicht abgerundet (Sarevoc 13.10.2026: "ich wollte die Ecken des Baumes abrunden, aber
/// grundsätzlich keine geschwungenen Linien"; davor kurz weiche Kurven). Kräftig zu einem Ch., in dem jemand sitzt, und zu jeder Person;
/// dünn und blass zu einem leeren Ch. Die dünnen und die kräftigen Linien gehen je als eine Zeichnung hinaus, damit sich die gemeinsamen
/// Stücke nicht aufaddieren. Gezeichnet wird neu, wenn sich die Lage der Kinder ändert, nicht bei jedem Layoutdurchlauf.
/// </summary>
public sealed class Stammbaumlinien : FrameworkElement
{
    /// <summary>Halbmesser der abgerundeten Ecken.</summary>
    public const double Rundung = 6;

    /// <summary>Kräftige Linie (zu Leuten); seit Sarevocs "die Kurvenlinien nicht so stark" 1,6 statt 2,6.</summary>
    public const double Kraeftig = 1.6;

    /// <summary>Dünne Linie (zu einem leeren Ch.).</summary>
    public const double Duenn = 1.0;

    /// <summary>Deckung der dünnen Linie.</summary>
    public const double DuennDeckung = 0.25;

    public static readonly DependencyProperty ListeProperty = DependencyProperty.Register(nameof(Liste), typeof(ItemsControl), typeof(Stammbaumlinien),
        new FrameworkPropertyMetadata(null, (d, e) => ((Stammbaumlinien)d).ListeGewechselt((ItemsControl?)e.OldValue, (ItemsControl?)e.NewValue)));

    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(nameof(Farbe), typeof(Brush), typeof(Stammbaumlinien),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Die Höhe der Knotenmitte von oben (seit 0.11.2, Sarevoc 13.10.2026: "der Baum soll nur nach rechts und unten wachsen"): Der Knoten steht oben
    /// auf der Höhe seines ersten Kinds, die Linien gehen von dort nach rechts und nach unten. Ohne Angabe die Mitte der Höhe.
    /// </summary>
    public static readonly DependencyProperty KnotenMitteProperty = DependencyProperty.Register(nameof(KnotenMitte), typeof(double), typeof(Stammbaumlinien),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public double KnotenMitte
    {
        get => (double)GetValue(KnotenMitteProperty);
        set => SetValue(KnotenMitteProperty, value);
    }

    private string _stand = "";

    /// <summary>Die Liste der Kinder (Ch. oder Leute), deren Mitten die Kurven treffen.</summary>
    public ItemsControl? Liste
    {
        get => (ItemsControl?)GetValue(ListeProperty);
        set => SetValue(ListeProperty, value);
    }

    public Brush? Farbe
    {
        get => (Brush?)GetValue(FarbeProperty);
        set => SetValue(FarbeProperty, value);
    }

    private void ListeGewechselt(ItemsControl? alt, ItemsControl? neu)
    {
        if (alt is not null) alt.LayoutUpdated -= LageGeprueft;
        if (neu is not null) neu.LayoutUpdated += LageGeprueft;
        _stand = "";
        InvalidateVisual();
    }

    /// <summary>Nach einem Layoutdurchlauf: nur neu zeichnen, wenn sich Höhe oder eine Mitte der Kinder geändert hat (sonst liefe das im Kreis).</summary>
    private void LageGeprueft(object? sender, EventArgs e)
    {
        // Nicht IsVisible: Das ist ohne Bildschirm (Bildlauf der Tests) immer falsch.
        if (Visibility != Visibility.Visible) return;
        var stand = Stand();
        if (string.Equals(stand, _stand, StringComparison.Ordinal)) return;
        _stand = stand;
        InvalidateVisual();
    }

    private string Stand()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(ActualHeight.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (y, kraeftig) in Kinder()) sb.Append(';').Append(y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append(kraeftig ? '+' : '-');
        return sb.ToString();
    }

    /// <summary>Die Mitte jedes sichtbaren Kinds in eigenen Einheiten und ob die Linie dorthin kräftig ist.</summary>
    private List<(double Y, bool Kraeftig)> Kinder()
    {
        var kinder = new List<(double, bool)>();
        if (Liste is not { } liste) return kinder;
        foreach (var item in liste.Items)
        {
            if (liste.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement behaelter || behaelter.Visibility != Visibility.Visible || behaelter.ActualHeight <= 0) continue;
            if (behaelter.FindCommonVisualAncestor(this) is null) continue;
            // Das Kind steht wie der Knoten oben in seiner Zeile (KnotenMitte von oben), sonst in der Mitte.
            var y = behaelter.TranslatePoint(new Point(0, double.IsFinite(KnotenMitte) ? KnotenMitte : behaelter.ActualHeight / 2), this).Y;
            kinder.Add((y, item is not BaumCh ch || ch.Besetzt));
        }
        return kinder;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Farbe is not { } farbe || ActualWidth <= 0) return;
        var w = ActualWidth;
        var mitte = double.IsFinite(KnotenMitte) ? KnotenMitte : ActualHeight / 2;
        var kinder = Kinder();
        // Erst die dünnen, darüber die kräftigen; je eine Zeichnung.
        var duenn = Linien(kinder.Where(k => !k.Kraeftig).Select(k => k.Y), w, mitte);
        if (duenn is not null)
        {
            drawingContext.PushOpacity(DuennDeckung);
            drawingContext.DrawGeometry(null, new Pen(farbe, Duenn) { LineJoin = PenLineJoin.Round }, duenn);
            drawingContext.Pop();
        }
        var kraeftig = Linien(kinder.Where(k => k.Kraeftig).Select(k => k.Y), w, mitte);
        if (kraeftig is not null) drawingContext.DrawGeometry(null, new Pen(farbe, Kraeftig) { LineJoin = PenLineJoin.Round }, kraeftig);
    }

    /// <summary>
    /// Je Kind eine Figur von (0, <paramref name="mitte"/>) zu (<paramref name="w"/>, y): waagerecht bis zur Mitte der Lücke, senkrecht,
    /// waagerecht weiter, die Ecken mit <see cref="Rundung"/> abgerundet; liegt das Kind auf der Höhe des Knotens, eine gerade Linie.
    /// </summary>
    private static StreamGeometry? Linien(IEnumerable<double> ziele, double w, double mitte)
    {
        var liste = ziele.ToList();
        if (liste.Count == 0) return null;
        var x = Math.Round(w / 2);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            foreach (var y in liste)
            {
                ctx.BeginFigure(new Point(0, mitte), false, false);
                var abstand = y - mitte;
                if (Math.Abs(abstand) < 0.5)
                {
                    ctx.LineTo(new Point(w, y), true, true);
                    continue;
                }
                var r = Math.Min(Rundung, Math.Min(Math.Abs(abstand) / 2, x));
                var s = Math.Sign(abstand);
                ctx.LineTo(new Point(x - r, mitte), true, true);
                ctx.QuadraticBezierTo(new Point(x, mitte), new Point(x, mitte + (s * r)), true, true);
                ctx.LineTo(new Point(x, y - (s * r)), true, true);
                ctx.QuadraticBezierTo(new Point(x, y), new Point(x + r, y), true, true);
                ctx.LineTo(new Point(w, y), true, true);
            }
        }
        g.Freeze();
        return g;
    }
}
