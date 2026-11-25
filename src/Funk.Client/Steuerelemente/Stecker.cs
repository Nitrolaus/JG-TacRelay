// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using Funk.Client.Design;
using Funk.Client.Modelle;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Ein Stecker der Startseite (Server, Sprache), selbst gezeichnet aus Rechtecken und Linien (kein fremdes Bild; andere Funkprogramme zeigen
/// ein Steckersymbol, übernommen ist nur das Prinzip): links der Stecker mit zwei Stiften und Kabel, rechts die Buchse mit Kabel.
/// Getrennt grau und auseinander, beim Verbinden näher und heller, verbunden zusammengesteckt und grün leuchtend, über den
/// Tunnel zusammengesteckt und gelb. Die Farbe steht nie allein: Daneben steht immer ein Wort (getrennt, verbunden, Tunnel).
/// Alles liegt spiegelgleich um die Mitte des Elements, damit der Stecker in seinem Feld mittig sitzt (gemessen in den Tests).
/// </summary>
public sealed class Stecker : FrameworkElement
{
    public static readonly DependencyProperty ZustandProperty = DependencyProperty.Register(nameof(Zustand), typeof(Steckerzustand), typeof(Stecker),
        new FrameworkPropertyMetadata(Steckerzustand.Getrennt, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Halbe Breite eines Gehäuses, halbe Höhe, Länge der Stifte und des Kabels (Einheiten).</summary>
    private const double Gehaeuse = 6;
    private const double Halbhoehe = 7;
    private const double Stift = 7;
    private const double Kabel = 6;

    public Stecker()
    {
        Width = 56;
        Height = 30;
        SnapsToDevicePixels = true;
    }

    public Steckerzustand Zustand
    {
        get => (Steckerzustand)GetValue(ZustandProperty);
        set => SetValue(ZustandProperty, value);
    }

    /// <summary>Abstand jedes Gehäuses von der Mitte je Zustand: auseinander, näher, zusammengesteckt (Stifte stecken in der Buchse).</summary>
    internal static double Versatz(Steckerzustand z) => z switch
    {
        Steckerzustand.Getrennt => 6,
        Steckerzustand.Verbindet => 3,
        _ => -1,
    };

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        var b = ActualWidth;
        var h = ActualHeight;
        if (b < 2 || h < 2) return;
        var z = Zustand;
        var farbe = z switch
        {
            Steckerzustand.Verbunden => Farbe("Farbe.Senden", Palette.Senden),
            Steckerzustand.Tunnel => Farbe("Farbe.Tunnel", Palette.Tunnel),
            Steckerzustand.Verbindet => Farbe("Farbe.Text2", Palette.Text2),
            _ => Farbe("Farbe.TextAus", Palette.TextAus),
        };
        var zusammen = z is Steckerzustand.Verbunden or Steckerzustand.Tunnel;
        var mx = b / 2;
        var my = h / 2;
        var v = Versatz(z);

        if (zusammen)
        {
            // Leuchten: ein weicher Hof in der Farbe, mittig hinter dem gesteckten Paar.
            var hof = new RadialGradientBrush(Color.FromArgb(0x70, farbe.R, farbe.G, farbe.B), Color.FromArgb(0, farbe.R, farbe.G, farbe.B));
            hof.Freeze();
            dc.DrawEllipse(hof, null, new Point(mx, my), Math.Min(mx, 24), Math.Min(my, 14));
        }

        var stift = new Pen(new SolidColorBrush(farbe), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        stift.Freeze();
        var fuellung = new SolidColorBrush(zusammen ? farbe : Farbe("Farbe.FlaecheTief", Palette.FlaecheTief));
        fuellung.Freeze();

        // Stecker links: Kabel, Gehäuse, zwei Stifte nach rechts.
        var steckerRechts = mx - v - 1;
        var steckerLinks = steckerRechts - (2 * Gehaeuse);
        dc.DrawLine(stift, new Point(steckerLinks - Kabel, my), new Point(steckerLinks, my));
        dc.DrawLine(stift, new Point(steckerRechts, my - 3.5), new Point(steckerRechts + Stift, my - 3.5));
        dc.DrawLine(stift, new Point(steckerRechts, my + 3.5), new Point(steckerRechts + Stift, my + 3.5));
        dc.DrawRoundedRectangle(fuellung, stift, new Rect(steckerLinks, my - Halbhoehe, 2 * Gehaeuse, 2 * Halbhoehe), 2.5, 2.5);

        // Buchse rechts, spiegelgleich: Gehäuse über den Stiften (gesteckt verschwinden sie darin), Kabel nach rechts.
        var buchseLinks = mx + v + 1;
        var buchseRechts = buchseLinks + (2 * Gehaeuse);
        dc.DrawRoundedRectangle(fuellung, stift, new Rect(buchseLinks, my - Halbhoehe, 2 * Gehaeuse, 2 * Halbhoehe), 2.5, 2.5);
        dc.DrawLine(stift, new Point(buchseRechts, my), new Point(buchseRechts + Kabel, my));
    }

    private Color Farbe(string schluessel, Color ersatz) => TryFindResource(schluessel) is Color c ? c : ersatz;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
