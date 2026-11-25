// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>Art eines <see cref="Abzeichen"/>: bestimmt seine Farbe (die Stile setzen Background je Art).</summary>
public enum Abzeichenart
{
    Neutral,
    Akzent,
    Erfolg,
    Warnung,
    Gefahr,
}

/// <summary>
/// Abzeichen (seit 0.5.0-beta.9, Stilfamilien 2 Stufe 2): eine kleine Pille mit kurzem Text ("Neu", "Angebot", "Favorit").
/// Die Farbe kommt aus <see cref="Art"/> (Neutral, Akzent, Erfolg, Warnung, Gefahr; die Stile setzen Background aus der
/// Farbstellung und den Statusfarben) oder frei über Background. Die Schrift wählt der Körper über die Schriftwahl; liegt
/// eine Farbe im Mittelbereich, in dem weder Weiß noch fast Schwarz 4,5 zu 1 erreicht, dunkelt das Abzeichen sie so weit ab,
/// bis Weiß es tut (<see cref="LesbareFarbe"/>). Eine Anzeige, nicht fokussierbar; Automation: ein Text mit dem Inhalt.
/// Stile: Glas.Abzeichen (getöntes Glas) und Glas.Taktil.Abzeichen (gehobene Pille, Statusfarben satt und dunkler).
/// </summary>
public class Abzeichen : ContentControl
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Abzeichenart), typeof(Abzeichen), new FrameworkPropertyMetadata(Abzeichenart.Neutral));

    static Abzeichen()
    {
        FocusableProperty.OverrideMetadata(typeof(Abzeichen), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Abzeichen), new FrameworkPropertyMetadata(false));
    }

    public Abzeichenart Art { get => (Abzeichenart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }

    /// <summary>
    /// Wie stark die Körper der Abzeichen ihre Farbe im Schriftband höchstens aufhellen oder abdunkeln (Glanz oben, Verlauf):
    /// Die Prüfung rechnet mit diesem Spielraum, damit die Schrift auf dem gezeichneten Körper sicher hält.
    /// </summary>
    internal const double Spielraum = 0.12;

    /// <summary>
    /// Eine Farbe, auf der Weiß oder fast Schwarz (<see cref="Schriftwahl"/>) 4,5 zu 1 hält, auch an der hellsten und
    /// dunkelsten Stelle des Körpers: die Farbe selbst, wenn es schon so ist, sonst so weit abgedunkelt, bis Weiß hält. Mit
    /// <paramref name="hellBevorzugt"/> wird abgedunkelt, bis Weiß hält, auch wenn fast Schwarz schon ginge (die satten,
    /// dunkleren Statusfarben im Taktil-Vorbild).
    /// </summary>
    public static Color LesbareFarbe(Color farbe, bool hellBevorzugt = false) => LesbareFarbe(farbe, hellBevorzugt, Spielraum);

    /// <summary>Wie <see cref="LesbareFarbe(Color, bool)"/> mit einem eigenen Spielraum für Glanz und Verlauf des Körpers.</summary>
    internal static Color LesbareFarbe(Color farbe, bool hellBevorzugt, double spielraum)
    {
        if (HellHaelt(farbe, spielraum) || (!hellBevorzugt && Haelt(farbe, farbe, spielraum))) return farbe;
        for (var t = Schriftwahl.Schritt; t < 1; t += Schriftwahl.Schritt)
        {
            var c = Farbton.Mischen(farbe, Color.FromArgb(farbe.A, 0, 0, 0), t);
            if (HellHaelt(c, spielraum)) return c;
        }
        return Color.FromArgb(farbe.A, 0, 0, 0);
    }

    /// <summary>
    /// Spielraum des Glas-4-Abzeichens (seit 0.5.0-beta.13): Sein Glas ist oben bis zu einem Zehntel heller und trägt dort einen
    /// Schein; die Punkte von Ä, Ö, Ü stehen darin. Mit dem Spielraum der übrigen Körper hielt Weiß auf dem Akzent mit "Ändern"
    /// nur 4,44 zu 1.
    /// </summary>
    internal const double Glasspielraum = 0.18;

    /// <summary>
    /// Ob auf einer Fläche von <paramref name="hellste"/> bis <paramref name="dunkelste"/> eine der beiden Kontrastschriften
    /// überall 4,5 zu 1 hält (mit 0,1 Luft): Weiß auf der hellsten, um <paramref name="spielraum"/> aufgehellt, oder fast Schwarz
    /// auf der dunkelsten, um ebenso viel abgedunkelt.
    /// </summary>
    internal static bool Haelt(Color hellste, Color dunkelste, double spielraum)
    {
        if (Farbton.Leuchtdichte(hellste) < Farbton.Leuchtdichte(dunkelste)) (hellste, dunkelste) = (dunkelste, hellste);
        var grenze = Schriftwahl.Grenze + 0.1;
        return HellHaelt(hellste, spielraum) || Farbton.Kontrast(Schriftwahl.Dunkel, Opak(Farbton.Mischen(dunkelste, Colors.Black, spielraum))) >= grenze;
    }

    private static bool HellHaelt(Color c, double spielraum) =>
        Farbton.Kontrast(Schriftwahl.Hell, Opak(Farbton.Mischen(c, Colors.White, spielraum))) >= Schriftwahl.Grenze + 0.1;

    private static Color Opak(Color c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>Converter: Farbe oder Pinsel (bei Verläufen der erste Stopp) zu einer lesbaren Farbe (<see cref="LesbareFarbe"/>), als Color oder eingefrorener Pinsel.</summary>
    public static IValueConverter Lesbar { get; } = new Lesbarwandler(false);

    /// <summary>Wie <see cref="Lesbar"/> mit dem Spielraum des Glas-4-Abzeichens (<see cref="Glasspielraum"/>, Glanz oben; seit 0.5.0-beta.13).</summary>
    public static IValueConverter GlasLesbar { get; } = new Lesbarwandler(false, Glasspielraum);

    /// <summary>Wie <see cref="Lesbar"/>, aber dunkelt ab, bis Weiß hält (für die Vorgabefarben der Taktil-Abzeichen).</summary>
    public static IValueConverter HellLesbar { get; } = new Lesbarwandler(true);

    private sealed class Lesbarwandler(bool hell, double spielraum = Spielraum) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not (Color or SolidColorBrush or GradientBrush)) return DependencyProperty.UnsetValue;
            var c = LesbareFarbe(Farbableitung.Farbe(value), hell, spielraum);
            if (targetType == typeof(Color)) return c;
            var p = new SolidColorBrush(c);
            p.Freeze();
            return p;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Abzeichenflaeche(this);
}

/// <summary>Automation des <see cref="Abzeichen"/>: ein Text, Name aus dem Inhalt.</summary>
internal sealed class Abzeichenflaeche(Abzeichen abzeichen) : FrameworkElementAutomationPeer(abzeichen)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => nameof(Abzeichen);

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? Rueckmeldungstext.Von(abzeichen.Content) : name;
    }

    protected override bool IsControlElementCore() => true;
}

/// <summary>
/// Der Körper des Glas-4-Abzeichens: getöntes Glas in <see cref="Farbe"/> als Pille. Eine Haarlinie von genau einem
/// Gerätepixel (oben hell, an den Seiten schwach, unten mittel, wie die Haarlinie von Glas 4, aus Weiß über der Farbe), die
/// Fläche oben etwas heller, unten etwas dunkler, oben ein Schein. Gesperrt flach und zum Grau gemischt. Schriftwahl über
/// die Farben im Schriftband (<see cref="Familienkoerper"/>).
/// </summary>
public sealed class Abzeichenglas : Familienkoerper
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Abzeichenglas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Ersatz, wenn keine Farbe gesetzt ist: das Anthrazit von Glas.Koerper in Standard.</summary>
    private static readonly Color Ersatz = Color.FromRgb(0x30, 0x37, 0x3B);

    /// <summary>Das Grau, zu dem ein gesperrtes Abzeichen gemischt wird.</summary>
    private static readonly Color Gesperrtgrau = Color.FromRgb(0x3A, 0x3F, 0x44);

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }

    private Color Grundfarbe => Zustand == Glaszustand.Gesperrt ? Farbton.Mischen(FarbeVon(Farbe, Ersatz), Gesperrtgrau, 0.65) : FarbeVon(Farbe, Ersatz);

    private protected override object Teilschluessel() => Grundfarbe;

    /// <summary>Die Stopps der Fläche (oben, Mitte, unten) und des Scheins.</summary>
    private (Color Oben, Color Mitte, Color Unten) Flaechenstopps()
    {
        var c = Grundfarbe;
        return (Heller(c, 0.12), c, Dunkler(c, 0.1));
    }

    private double Scheinstaerke => Zustand == Glaszustand.Gesperrt ? 0 : 0.16;

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        var unten = f.Hoehe / s;
        var (o, m, u) = Flaechenstopps();
        Schicht(f.Form(Glasgeometrie.Haarlinie), Senkrecht(0, unten, (o, 0), (m, 0.5), (u, 1)));
        if (Scheinstaerke > 0)
            Schicht(f.Form(Glasgeometrie.Haarlinie), Senkrecht(0, unten, (Weiss(Scheinstaerke), 0), (Weiss(Scheinstaerke * 0.3), 0.4), (Weiss(0), 0.5)));
        var c = Grundfarbe;
        var gesperrt = Zustand == Glaszustand.Gesperrt;
        Schicht(f.Ring(0, Glasgeometrie.Haarlinie), Senkrecht(0, unten,
            (Schriftwahl.Ueber(Weiss(gesperrt ? 0.25 : 0.55), c), 0), (Schriftwahl.Ueber(Weiss(gesperrt ? 0.1 : 0.2), c), 0.3),
            (Schriftwahl.Ueber(Weiss(gesperrt ? 0.08 : 0.14), c), 0.7), (Schriftwahl.Ueber(Weiss(gesperrt ? 0.15 : 0.34), c), 1)));
    }

    private protected override IReadOnlyList<Color> Flaechenfarben()
    {
        var (o, m, u) = Flaechenstopps();
        var schein = Scheinstaerke;
        return Band(t =>
        {
            var grund = Verlaufsfarbe(t, (o, 0), (m, 0.5), (u, 1));
            return schein > 0 ? Schriftwahl.Ueber(Verlaufsfarbe(t, (Weiss(schein), 0), (Weiss(schein * 0.3), 0.4), (Weiss(0), 0.5)), grund) : grund;
        });
    }
}
