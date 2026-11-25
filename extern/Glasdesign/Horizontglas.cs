// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Das Glas der Stilfamilie Horizontglas (KONZEPT.md 5b.5, Vorbild 5): farbiges Glas innerhalb eines dünnen
/// <see cref="Chromrand"/>. Die obere Hälfte spiegelt den Himmel und ist heller, an einer Horizontlinie bricht der Glanz
/// ab (<see cref="Glanzart"/>: gerade, Ellipse, Welle, Bogen, schräg); darunter satte Farbe, zum Boden dunkler, ein Hauch
/// Licht von unten, unter dem Rand ein schmaler Innenschatten, oben links ein Funke, darunter ein sehr schwacher Schatten.
/// Die Farbe kommt aus <see cref="Farbe"/> (Background des Knopfs, in den Stilen Glas.Horizontglas), sonst Blau.
/// <see cref="Schrift"/> ist weiß oder fast schwarz, was auf der Farbe den höheren Kontrast hat (wie bei den Farbglasknöpfen).
/// </summary>
public sealed class Horizontglaskoerper : Familienkoerper
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Horizontglaskoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SchriftNeu));

    public static readonly DependencyProperty VorgabeFarbeProperty = DependencyProperty.Register(
        nameof(VorgabeFarbe), typeof(Color), typeof(Horizontglaskoerper), new FrameworkPropertyMetadata(Color.FromRgb(0x00, 0x80, 0xEB), FrameworkPropertyMetadataOptions.AffectsRender, SchriftNeu));

    public static readonly DependencyProperty GlanzartProperty = DependencyProperty.Register(
        nameof(Glanzart), typeof(Horizontglanz), typeof(Horizontglaskoerper), new FrameworkPropertyMetadata(Horizontglanz.Gerade, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Breite des Chromrands als Anteil der kürzeren Seite: so weit liegt das Glas innen (es reicht ein Pixel unter den Rand).</summary>
    public static readonly DependencyProperty RandProperty = DependencyProperty.Register(
        nameof(Rand), typeof(double), typeof(Horizontglaskoerper), new FrameworkPropertyMetadata(0.08, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyPropertyKey SchriftKey = DependencyProperty.RegisterReadOnly(
        nameof(Schrift), typeof(Brush), typeof(Horizontglaskoerper), new FrameworkPropertyMetadata(Brushes.White));

    public static readonly DependencyProperty SchriftProperty = SchriftKey.DependencyProperty;

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Color VorgabeFarbe { get => (Color)GetValue(VorgabeFarbeProperty); set => SetValue(VorgabeFarbeProperty, value); }
    public Horizontglanz Glanzart { get => (Horizontglanz)GetValue(GlanzartProperty); set => SetValue(GlanzartProperty, value); }
    public double Rand { get => (double)GetValue(RandProperty); set => SetValue(RandProperty, value); }

    /// <summary>Weiß oder fast Schwarz, was auf Farbe und Himmelsglanz des Zustands besser lesbar ist; gesperrt gedämpft.</summary>
    public Brush Schrift => (Brush)GetValue(SchriftProperty);

    /// <summary>Die wirksame Grundfarbe: <see cref="Farbe"/>, sonst <see cref="VorgabeFarbe"/>.</summary>
    public Color Grundfarbe => FarbeVon(Farbe, VorgabeFarbe);

    private static void SchriftNeu(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Horizontglaskoerper)d).SchriftSetzen();

    private protected override void Nachgeaendert() => SchriftSetzen();

    private void SchriftSetzen()
    {
        // Die Schrift liegt über Farbe und Himmelsglanz zugleich: Gewählt wird, was an der schlechtesten dieser Stellen den
        // höheren Kontrast hat (auf dem Blau des Vorbilds fast Schwarz, wie bei Aqua). Gesperrt wird sie nur gedämpft
        // (sonst kippte sie auf dem grauen Glas ins Dunkle).
        var gesperrt = Zustand == Glaszustand.Gesperrt;
        var proben = Proben(Wirksam, Profil, Zustand);
        double Schlechteste(Color schrift) => proben.Min(g => Farbton.Kontrast(schrift, g));
        var dunkel = Color.FromRgb(0x0B, 0x0D, 0x10);
        var farbe = Schlechteste(Colors.White) >= Schlechteste(dunkel) ? Colors.White : dunkel;
        if (gesperrt) farbe = Farbton.Mischen(farbe, Gesperrt(Wirksam), 0.25);
        if (Schrift is not SolidColorBrush alt || alt.Color != farbe) SetValue(SchriftKey, Flaeche(farbe));
        SchriftWeitergeben(Schrift);
    }

    /// <summary>Die Farben unter der Schrift im Zustand: Farbe, zum Boden etwas dunkler, und der Himmelsglanz darüber.</summary>
    private static IReadOnlyList<Color> Proben(Color grund, Stilprofil p, Glaszustand zustand)
    {
        var c = zustand == Glaszustand.Gesperrt ? Gesperrt(grund) : Glasfarbe(grund, p, zustand);
        var glanz = Math.Clamp(Glanz(p, zustand), 0, 1);
        return [c, Ton(c, -0.06), Farbton.Mischen(c, Colors.White, glanz * 0.66), Farbton.Mischen(c, Colors.White, glanz)];
    }

    /// <summary>
    /// Die Grundfarbe abgestimmt (seit 0.5.0-beta.13, <see cref="Schriftwahl.Abstimmung"/>): Hält auf einer frei gewählten Farbe
    /// aus dem Mittelbereich weder Weiß noch fast Schwarz auf Farbe und Glanz zugleich, wird sie so wenig wie nötig aufgehellt,
    /// bis fast Schwarz hält (Weiß hielte auf dem Glanz nie), sonst abgedunkelt; die Vorgabe bleibt, wie sie ist. Eine dunkle
    /// Farbe liegt nicht in der Mitte (<see cref="Schriftwahl.Mittel"/>) und bleibt dunkel mit weißer Schrift (Grenzen in LIESMICH 4n).
    /// </summary>
    private Color Wirksam
    {
        get
        {
            var (g, p) = (Grundfarbe, Profil);
            return Schriftwahl.Abgestimmt(g, Abstimmen((g, p), g, (t, z) => Proben(Schriftwahl.Abgestimmt(g, t), p, z)));
        }
    }

    private static Color Gesperrt(Color c) => Farbton.Mischen(c, Color.FromRgb(0x8A, 0x8F, 0x94), 0.6);

    /// <summary>
    /// Die Farbe des Glases im Zustand: überfahren heller; gedrückt nicht dunkler, nur mit schwächerem Glanz (sonst fände
    /// keine Schrift auf Farbe und Glanz zugleich 4,5 zu 1).
    /// </summary>
    private static Color Glasfarbe(Color grund, Stilprofil p, Glaszustand zustand) => zustand == Glaszustand.Ueberfahren ? Zustandsfarbe(grund, p, zustand) : grund;

    /// <summary>Die Deckung des Himmelsglanzes im Zustand.</summary>
    private static double Glanz(Stilprofil p, Glaszustand zustand) =>
        p.HorizontglasHimmelsglanz * (zustand switch { Glaszustand.Ueberfahren => 1.12, Glaszustand.Gedrueckt => 0.7, Glaszustand.Gesperrt => 0.5, _ => 1 });

    private protected override object Teilschluessel() => (Grundfarbe, Glanzart, Rand);

    /// <summary>Die Farbe um <paramref name="k"/> heller (positiv) oder dunkler (negativ).</summary>
    private static Color Ton(Color c, double k) => k >= 0 ? Heller(c, k) : Dunkler(c, -k);

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var c = aktiv ? Glasfarbe(Wirksam, p, Zustand) : Gesperrt(Wirksam);
        var rand = Math.Max(1, Math.Round(Math.Min(b, h) * Math.Clamp(Rand, 0, 0.5)));
        var innen = Math.Max(0, rand - 1);
        var ih = h - 2 * innen;
        var iw = b - 2 * innen;
        if (ih < 4 || iw < 4) return;

        // Sehr schwacher Schatten unter dem Knopf (außerhalb der eigenen Grenzen).
        if (Schatten == Schattenstufe.Voll && aktiv && p.HorizontglasSchatten > 0)
        {
            var schatten = Color.FromRgb(0x27, 0x31, 0x3D);
            Schicht(Ellipse(f, b / 2, h * 1.0, b * 0.46, h * 0.16),
                Radial(f.Dip(b / 2, h), b * 0.46 / s, h * 0.16 / s, (Farbton.Deckend(schatten, p.HorizontglasSchatten), 0), (Farbton.Deckend(schatten, 0), 1)));
        }

        var form = f.Form(innen);
        var oben = innen / s;
        var unten = (h - innen) / s;
        // Zum Boden dunkler, erst unter der Schrift (bis drei Viertel der Höhe fast die Grundfarbe).
        Schicht(form, Senkrecht(oben, unten, (Ton(c, 0.02), 0), (c, 0.45), (Ton(c, -0.03), 0.75), (Ton(c, -0.12), 1)));
        var boden = p.HorizontglasBodenlicht * (aktiv ? 1 : 0.4);
        if (boden > 0)
            Schicht(form, Radial(f.Dip(b / 2, h - innen + ih * 0.04), iw * 0.62 / s, ih * 0.36 / s, (Farbton.Deckend(Ton(c, 0.34), boden), 0), (Farbton.Deckend(Ton(c, 0.34), 0), 1)));
        // Innenschatten unter dem Rand: ein schmaler Ring, oben dunkler.
        var schattenRing = Math.Max(1, Glasgeometrie.Raster(1.5, s));
        Schicht(f.Ring(innen, innen + schattenRing), Senkrecht(oben, unten, (Farbton.Deckend(Ton(c, -0.55), 0.8), 0), (Farbton.Deckend(Ton(c, -0.3), 0.8), 0.5), (Farbton.Deckend(Ton(c, -0.1), 0.8), 1)));

        // Himmelsglanz bis zur Horizontlinie, je nach Glanzart.
        var glanz = Glanz(p, Zustand);
        if (glanz > 0)
        {
            var lage = Math.Clamp(p.HorizontglasHorizontlage, 0.1, 0.9);
            var himmel = Himmel(Glanzart, innen, innen, iw, ih, lage, s);
            var geschnitten = Geometry.Combine(form, himmel, GeometryCombineMode.Intersect, null, 0.05 / s, ToleranceType.Absolute);
            Schicht(geschnitten, Senkrecht(oben, oben + ih * Math.Max(lage, 0.5) / s,
                (Weiss(glanz), 0), (Weiss(glanz * 0.73), 0.92), (Weiss(glanz * 0.66), 1)));
        }

        // Funke oben links auf dem Glas.
        if (aktiv && p.HorizontglasFunke > 0)
        {
            var groesse = Math.Min(16 * s, ih * 0.32);
            var ir = Math.Max(0, Math.Min(f.Radius, Math.Min(b, h) / 2) - innen);
            Funke(f, innen + Math.Max(4 * s, ir * 0.42) + groesse * 0.1, innen + Math.Max(2 * s, ih * 0.1), groesse, p.HorizontglasFunke * (Zustand == Glaszustand.Ueberfahren ? 1 : 0.85));
        }
    }

    /// <summary>Die Fläche des Himmelsglanzes in DIP: oben bis zur Horizontlinie bei <paramref name="lage"/> der Innenhöhe, je nach Art.</summary>
    private static Geometry Himmel(Horizontglanz art, double x, double y, double w, double h, double lage, double s)
    {
        Point P(double px, double py) => new((x + px * w) / s, (y + py * h) / s);
        var g = new StreamGeometry();
        using (var z = g.Open())
        {
            switch (art)
            {
                case Horizontglanz.Ellipse:
                    break;
                case Horizontglanz.Welle:
                    z.BeginFigure(P(-0.02, -0.02), true, true);
                    z.LineTo(P(1.02, -0.02), false, false);
                    z.LineTo(P(1.02, lage - 0.10), false, false);
                    z.BezierTo(P(0.72, lage + 0.14), P(0.40, lage - 0.20), P(-0.02, lage + 0.06), false, false);
                    break;
                case Horizontglanz.Bogen:
                    z.BeginFigure(P(-0.02, -0.02), true, true);
                    z.LineTo(P(1.02, -0.02), false, false);
                    z.LineTo(P(1.02, lage + 0.08), false, false);
                    z.QuadraticBezierTo(P(0.5, lage - 0.14), P(-0.02, lage + 0.08), false, false);
                    break;
                case Horizontglanz.Schraeg:
                    z.BeginFigure(P(-0.02, -0.02), true, true);
                    z.LineTo(P(1.02, -0.02), false, false);
                    z.LineTo(P(1.02, lage - 0.28), false, false);
                    z.LineTo(P(0.40, 1.02), false, false);
                    z.LineTo(P(-0.02, 1.02), false, false);
                    break;
                default:
                    z.BeginFigure(P(-0.02, -0.02), true, true);
                    z.LineTo(P(1.02, -0.02), false, false);
                    z.LineTo(P(1.02, lage), false, false);
                    z.LineTo(P(-0.02, lage), false, false);
                    break;
            }
        }
        if (art == Horizontglanz.Ellipse)
        {
            var e = new EllipseGeometry(P(0.5, lage * 0.12), w * 0.62 / s, h * lage / s);
            e.Freeze();
            return e;
        }
        g.Freeze();
        return g;
    }
}
