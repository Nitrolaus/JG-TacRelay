// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Schriftwahl (0.5.0-beta.7): Die Schrift der Farbstellung bleibt die bevorzugte (der Wunsch, <see cref="Glas.SchriftwunschProperty"/>).
/// Hält sie auf der Fläche, auf der sie tatsächlich steht, weniger als 4,5 zu 1 (gesperrt 3 zu 1), nimmt das Element die
/// besser lesbare Kontrastschrift: <see cref="Hell"/> oder fast Schwarz (<see cref="Dunkel"/>), je nachdem, was an der
/// schlechtesten Stelle der Fläche den höheren Kontrast hat. Hält der Wunsch die Grenze, kommt genau sein Pinsel zurück;
/// bei den Vorgabefarben bleibt so jedes Pixel, wie es war. Gebraucht wird es über die Familienkörper
/// (<see cref="Familienkoerper.Schriftwunsch"/>, <see cref="Familienkoerper.GewaehlteSchrift"/>,
/// <see cref="Familienkoerper.SchriftAnSteuerelement"/>) und über <see cref="Wandler"/> für Zeichen mit eigenem Wunsch.
/// </summary>
public static class Schriftwahl
{
    /// <summary>Mindestkontrast der Schrift (WCAG AA), gesperrt <see cref="GrenzeGesperrt"/>.</summary>
    public const double Grenze = 4.5;

    public const double GrenzeGesperrt = 3.0;

    /// <summary>Die helle Kontrastschrift.</summary>
    public static Color Hell { get; } = Colors.White;

    /// <summary>Die dunkle Kontrastschrift (fast Schwarz, wie bei den Farbglasknöpfen).</summary>
    public static Color Dunkel { get; } = Color.FromRgb(0x1C, 0x20, 0x24);

    private static readonly SolidColorBrush HellPinsel = Eingefroren(Hell);
    private static readonly SolidColorBrush DunkelPinsel = Eingefroren(Dunkel);

    private static SolidColorBrush Eingefroren(Color c)
    {
        var p = new SolidColorBrush(c);
        p.Freeze();
        return p;
    }

    /// <summary>
    /// Der Wunsch, wenn er auf allen Farben von <paramref name="flaeche"/> die Grenze hält (gesperrt 3 zu 1), sonst die
    /// Kontrastschrift. Ohne Flächenfarben bleibt der Wunsch.
    /// </summary>
    public static Brush Waehlen(Brush wunsch, IReadOnlyList<Color> flaeche, bool gesperrt)
    {
        ArgumentNullException.ThrowIfNull(wunsch);
        ArgumentNullException.ThrowIfNull(flaeche);
        if (flaeche.Count == 0) return wunsch;
        var grenze = gesperrt ? GrenzeGesperrt : Grenze;
        if (Kleinster(wunsch, flaeche) >= grenze) return wunsch;
        var farbe = Lesbare(flaeche, grenze);
        return farbe == Hell ? HellPinsel : farbe == Dunkel ? DunkelPinsel : Eingefroren(farbe);
    }

    /// <summary>
    /// Die Kontrastschrift für <paramref name="flaeche"/> mit der Grenze <paramref name="grenze"/> (seit 0.5.0-beta.13): Weiß
    /// oder fast Schwarz wie <see cref="Kontrastfarbe(IReadOnlyList{Color})"/>, wenn eine von beiden die Grenze hält. Auf einer
    /// Fläche aus dem Mittelbereich, auf der keine der beiden hält (etwa Grau #7A7A7A, Rot #E03A2E, Blau #2070F0), gilt
    /// derselbe Grundsatz wie bei <see cref="Abzeichen.LesbareFarbe"/>: so weit abdunkeln, bis es hält. Hier wird die dunkle
    /// Schrift dunkler, mit <see cref="Luft"/> für Glanz und Verlauf, die die Fläche um das Schriftband herum noch hat; bis
    /// Schwarz. Auf einer einfarbigen Fläche hält dann immer eine der beiden (mindestens 4,58 zu 1). Hält auch Schwarz nicht
    /// (eine Fläche, die von hell bis dunkel reicht), bleibt die Schrift mit dem höheren Kontrast. Die Fläche ändert diese Wahl
    /// nie; das tut, wo nötig, der Körper mit <see cref="Abstimmung"/>. Bei den Vorgabefarben ändert sich nichts.
    /// </summary>
    public static Color Lesbare(IReadOnlyList<Color> flaeche, double grenze = Grenze)
    {
        ArgumentNullException.ThrowIfNull(flaeche);
        if (flaeche.Count == 0) return Hell;
        double Schlechteste(Color schrift) => flaeche.Min(f => Farbton.Kontrast(schrift, Color.FromRgb(f.R, f.G, f.B)));
        var (hell, dunkel) = (Schlechteste(Hell), Schlechteste(Dunkel));
        if (Math.Max(hell, dunkel) >= grenze) return hell >= dunkel ? Hell : Dunkel;
        for (var t = 0.1; t < 1; t += 0.1)
        {
            var c = Farbton.Mischen(Dunkel, Colors.Black, t);
            if (Schlechteste(c) >= grenze + Luft) return c;
        }
        var schwarz = Schlechteste(Colors.Black);
        return schwarz >= Math.Max(hell, dunkel) ? Colors.Black : hell >= dunkel ? Hell : Dunkel;
    }

    /// <summary>Spielraum über der Grenze, wenn die dunkle Schrift abgedunkelt wird (Glanz und Verlauf neben dem Schriftband).</summary>
    public const double Luft = 0.3;

    /// <summary>Ob auf <paramref name="flaeche"/> eine Schrift hält (Weiß, fast Schwarz oder bis Schwarz abgedunkelt, <see cref="Lesbare"/>).</summary>
    public static bool Lesbar(IReadOnlyList<Color> flaeche, double grenze = Grenze)
    {
        ArgumentNullException.ThrowIfNull(flaeche);
        if (flaeche.Count == 0) return true;
        var c = Lesbare(flaeche, grenze);
        return flaeche.Min(f => Farbton.Kontrast(c, Color.FromRgb(f.R, f.G, f.B))) >= grenze;
    }

    /// <summary>Spielraum der Prüfung, ob eine Fläche bleiben darf (<see cref="Abstimmung"/>): Das Band ist gerechnet, die Pixel liegen um Zehntel daneben.</summary>
    public const double Pruefluft = 0.1;

    /// <summary>Schrittweite der <see cref="Abstimmung"/> (Anteil Schwarz oder Weiß, wie bei <see cref="Abzeichen.LesbareFarbe"/>).</summary>
    public const double Schritt = 0.02;

    /// <summary>
    /// Die Abstimmung einer frei gewählten Fläche, auf der keine Schrift hält (seit 0.5.0-beta.13, an einer Stelle für alle
    /// Familien): derselbe Grundsatz wie <see cref="Abzeichen.LesbareFarbe"/>, so wenig wie nötig abdunkeln, bis Weiß hält.
    /// Liegt Weiß an einer Stelle immer zu hell auf (ein weißer Glanz quer über der Schrift, Horizontglas), wird die Fläche
    /// stattdessen so wenig wie nötig aufgehellt, bis fast Schwarz hält. <paramref name="baender"/> liefert für eine Abstimmung
    /// t (größer 0 abgedunkelt, kleiner 0 aufgehellt, als Anteil Schwarz oder Weiß) die Farben unter der Schrift, ein Band je
    /// Zustand (etwa Ruhe und überfahren). Ergebnis: 0, wenn schon jetzt in jedem Band eine Schrift hält (die Vorgabefarben
    /// bleiben so Pixel für Pixel, wie sie sind); sonst das kleinste t, bei dem Weiß (oder fast Schwarz) mit <see cref="Luft"/>
    /// hält; 0, wenn nichts hilft. Abgestimmt wird nur eine Farbe aus dem Mittelbereich (<see cref="Mittel"/>, gemessen an
    /// <paramref name="roh"/> selbst): Eine klar dunkle oder helle Farbe, auf der nur ein Glanz die Schrift bricht (dunkles Blau
    /// auf dem Horizontglas), bleibt, wie sie gewählt ist; sie würde sonst eine ganz andere Farbe.
    /// </summary>
    public static double Abstimmung(Color roh, Func<double, IReadOnlyList<IReadOnlyList<Color>>> baender)
    {
        ArgumentNullException.ThrowIfNull(baender);
        if (!Mittel(roh)) return 0;
        // Eine Schrift für alle Zustände: Wer die Schrift nur nach der Farbe wählt (ohne Zustand), braucht eine, die überall hält.
        if (Lesbar([.. baender(0).SelectMany(b => b)], Grenze + Pruefluft)) return 0;
        static bool Haelt(IReadOnlyList<IReadOnlyList<Color>> l, Color schrift) =>
            l.All(b => b.Count == 0 || b.Min(f => Farbton.Kontrast(schrift, Color.FromRgb(f.R, f.G, f.B))) >= Grenze + Luft);
        for (var t = Schritt; t < 1 - 1e-9; t += Schritt)
        {
            if (Haelt(baender(t), Hell)) return t;
            if (Haelt(baender(-t), Dunkel)) return -t;
        }
        return 0;
    }

    /// <summary>
    /// Ob <paramref name="farbe"/> im Mittelbereich liegt: Weder Weiß noch fast Schwarz (#1C2024) erreicht auf ihr selbst 7 zu 1
    /// (die strengere Stufe AAA der WCAG). Nur solche Farben stimmt <see cref="Abstimmung"/> ab; auf allen übrigen hält eine der
    /// beiden Schriften mit großem Abstand, und was dort bricht, ist ein Glanz der Familie (Grenzen in LIESMICH 4n).
    /// </summary>
    public static bool Mittel(Color farbe)
    {
        var c = Color.FromRgb(farbe.R, farbe.G, farbe.B);
        return Farbton.Kontrast(Hell, c) < Mittelgrenze && Farbton.Kontrast(Dunkel, c) < Mittelgrenze;
    }

    /// <summary>Die Grenze des Mittelbereichs (<see cref="Mittel"/>): 7 zu 1.</summary>
    public const double Mittelgrenze = 7;

    /// <summary><paramref name="farbe"/> abgestimmt (<see cref="Abstimmung"/>): größer 0 zum Schwarz, kleiner 0 zum Weiß gemischt, Deckung bleibt.</summary>
    public static Color Abgestimmt(Color farbe, double t) => t switch
    {
        > 0 => Farbton.Mischen(farbe, Color.FromArgb(farbe.A, 0, 0, 0), Math.Min(1, t)),
        < 0 => Farbton.Mischen(farbe, Color.FromArgb(farbe.A, 255, 255, 255), Math.Min(1, -t)),
        _ => farbe,
    };

    /// <summary>Der kleinste Kontrast aller Farben des Pinsels (samt Deckung) auf allen Farben der Fläche.</summary>
    public static double Kleinster(Brush schrift, IReadOnlyList<Color> flaeche)
    {
        ArgumentNullException.ThrowIfNull(schrift);
        ArgumentNullException.ThrowIfNull(flaeche);
        var kleinster = double.MaxValue;
        foreach (var s in Farben(schrift))
            foreach (var f in flaeche)
            {
                var grund = Color.FromRgb(f.R, f.G, f.B);
                var oben = Farbton.Deckend(s, s.A / 255.0 * Math.Clamp(schrift.Opacity, 0, 1));
                kleinster = Math.Min(kleinster, Farbton.Kontrast(Ueber(oben, grund), grund));
            }
        return kleinster == double.MaxValue ? 21 : kleinster;
    }

    /// <summary>Weiß oder fast Schwarz, was an der schlechtesten Stelle der Fläche den höheren Kontrast hat.</summary>
    public static Color Kontrastfarbe(IReadOnlyList<Color> flaeche)
    {
        ArgumentNullException.ThrowIfNull(flaeche);
        if (flaeche.Count == 0) return Hell;
        return flaeche.Min(f => Farbton.Kontrast(Hell, f)) >= flaeche.Min(f => Farbton.Kontrast(Dunkel, f)) ? Hell : Dunkel;
    }

    /// <summary>Die Farben eines Pinsels: die eine einer Fläche, die Stopps eines Verlaufs; sonst keine.</summary>
    public static IReadOnlyList<Color> Farben(Brush? pinsel) => pinsel switch
    {
        SolidColorBrush s => [s.Color],
        GradientBrush { GradientStops.Count: > 0 } g => g.GradientStops.Select(x => x.Color).ToList(),
        _ => [],
    };

    /// <summary><paramref name="oben"/> mit seiner Deckung über <paramref name="unten"/> gelegt.</summary>
    public static Color Ueber(Color oben, Color unten)
    {
        var a = oben.A / 255.0;
        var b = unten.A / 255.0;
        var aus = a + b * (1 - a);
        if (aus <= 0) return Colors.Transparent;
        byte K(byte o, byte u) => (byte)Math.Round((o * a + u * b * (1 - a)) / aus);
        return Color.FromArgb((byte)Math.Round(aus * 255), K(oben.R, unten.R), K(oben.G, unten.G), K(oben.B, unten.B));
    }

    /// <summary>
    /// Für Bindungen in Vorlagen (MultiBinding): Werte Wunsch (Pinsel), Fläche (Pinsel, Farbe oder Farbliste, etwa
    /// <see cref="Familienkoerper.Koerperflaeche"/>) und wahlweise IsEnabled; Ergebnis der gewählte Pinsel. Seit 0.5.0-beta.13
    /// nennt ConverterParameter wahlweise, welche Familienfläche eine einzelne Farbe ist ("Taktil.Flaeche", "Taktil.Mulde",
    /// "Glutglas.Scheibe", "Glas.Koerper"): Dann wählt die Schrift auf dieser Fläche, wie ihr Körper sie zeichnet (abgestimmt,
    /// mit Verlauf, Rauchglas auf dunklem Grund oder Glanz), in jedem bedienbaren Zustand, statt auf der rohen Farbe.
    /// </summary>
    public static IMultiValueConverter Wandler { get; } = new Wahlwandler();

    /// <summary>Die Farben unter der Schrift auf einer Familienfläche aus einer einzelnen Farbe (siehe <see cref="Wandler"/>); null für eine unbekannte Art.</summary>
    public static IReadOnlyList<Color>? Familienflaeche(string art, Color roh, bool gesperrt = false) => art switch
    {
        "Taktil.Flaeche" => Taktilkoerper.Schriftflaeche(Taktilebene.Karte, roh, gesperrt),
        "Taktil.Mulde" => Taktilkoerper.Schriftflaeche(Taktilebene.Eingelassen, roh, gesperrt),
        "Glutglas.Scheibe" => Glutscheibe.Schriftflaeche(roh, gesperrt),
        // Glas 4: der Glaskörper glänzt oben bis gut ein Zehntel heller als seine Farbe.
        "Glas.Koerper" => [roh, Farbton.Mischen(roh, Color.FromArgb(roh.A, 255, 255, 255), 0.12)],
        _ => null,
    };

    private sealed class Wahlwandler : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null || values.Length < 2 || values[0] is not Brush wunsch) return DependencyProperty.UnsetValue;
            IReadOnlyList<Color> flaeche = values[1] switch
            {
                Brush b => Farben(b),
                Color c => [c],
                IReadOnlyList<Color> l => l,
                _ => [],
            };
            var gesperrt = values.Length > 2 && values[2] is false;
            if (parameter is string art && flaeche.Count == 1 && Familienflaeche(art, flaeche[0], gesperrt) is { } familie) flaeche = familie;
            return Waehlen(wunsch, flaeche, gesperrt);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

/// <summary>
/// Farbableitung (0.5.0-beta.7): die Farbe eines Teils, die einer Familienfarbe folgt, statt fest zu sein. Die Vorlage nennt
/// das Paar der Vorgaben: <see cref="Von"/> (die Familienfarbe in der Farbstellung Standard) und <see cref="Zu"/> (die Farbe
/// des Teils dazu). Jede andere Quelle wird je Kanal um denselben Abstand verschoben (Zu minus Von, samt Deckung, begrenzt
/// auf 0 bis 255); bei der Vorgabe kommt so genau die bisherige Farbe heraus. Quelle: Color, Pinsel oder Verlauf (dann die
/// Farbe bei <see cref="Lage"/>). Ergebnis: Color oder ein eingefrorener Pinsel, mit <see cref="ZuUnten"/> ein senkrechter
/// Verlauf. Ohne Von und Zu bleibt die Farbe, wie sie ist (etwa nur die Farbe eines Verlaufs bei einer Lage).
/// </summary>
public sealed class Farbableitung : IValueConverter
{
    private Color _zuUnten;
    private bool _verlauf;

    /// <summary>Die Vorgabe der Quelle (die Familienfarbe in Standard).</summary>
    public Color Von { get; set; }

    /// <summary>Die Vorgabe des Ziels (oben, oder die einzige Farbe).</summary>
    public Color Zu { get; set; }

    /// <summary>Gesetzt: die zweite Farbe eines senkrechten Verlaufs (unten), ebenso abgeleitet.</summary>
    public Color ZuUnten
    {
        get => _zuUnten;
        set
        {
            _zuUnten = value;
            _verlauf = true;
        }
    }

    /// <summary>Bei einem Verlauf als Quelle: die Stelle (0 bis 1), deren Farbe gilt; NaN (Vorgabe) nimmt den ersten Stopp.</summary>
    public double Lage { get; set; } = double.NaN;

    /// <summary>Die Quelle um den Abstand von <paramref name="von"/> nach <paramref name="zu"/> verschoben, je Kanal begrenzt.</summary>
    public static Color Ableiten(Color quelle, Color von, Color zu)
    {
        static byte K(byte q, byte v, byte z) => (byte)Math.Clamp(q + z - v, 0, 255);
        return Color.FromArgb(K(quelle.A, von.A, zu.A), K(quelle.R, von.R, zu.R), K(quelle.G, von.G, zu.G), K(quelle.B, von.B, zu.B));
    }

    /// <summary>Die Farbe eines Pinsels an der Stelle <paramref name="lage"/> (Verlauf linear zwischen den Stopps).</summary>
    public static Color Farbe(object? quelle, double lage = double.NaN)
    {
        switch (quelle)
        {
            case Color c: return c;
            case SolidColorBrush s: return s.Color;
            case GradientBrush g when g.GradientStops.Count > 0:
            {
                var stopps = g.GradientStops.OrderBy(x => x.Offset).ToList();
                if (double.IsNaN(lage)) return g.GradientStops[0].Color;
                if (lage <= stopps[0].Offset) return stopps[0].Color;
                for (var i = 1; i < stopps.Count; i++)
                    if (lage <= stopps[i].Offset)
                    {
                        var (a, b) = (stopps[i - 1], stopps[i]);
                        var t = b.Offset > a.Offset ? (lage - a.Offset) / (b.Offset - a.Offset) : 1;
                        return Farbton.Mischen(a.Color, b.Color, t);
                    }
                return stopps[^1].Color;
            }
            default: return Colors.Transparent;
        }
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not (Color or Brush)) return DependencyProperty.UnsetValue;
        var q = Farbe(value, Lage);
        var oben = Ableiten(q, Von, Zu);
        if (targetType == typeof(Color)) return oben;
        Brush pinsel = _verlauf
            ? new LinearGradientBrush(oben, Ableiten(q, Von, ZuUnten), new Point(0, 0), new Point(0, 1))
            : new SolidColorBrush(oben);
        pinsel.Freeze();
        return pinsel;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
