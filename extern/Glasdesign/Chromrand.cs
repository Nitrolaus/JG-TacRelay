// Glasdesign - Maintainer: Sarevoc
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glasdesign;

/// <summary>
/// Der Chromrand (KONZEPT.md 5b, gemeinsamer Baustein von Glanzkugel, Horizontglas und Chromrahmen): ein polierter
/// Metallring innen an der Form, Pixel für Pixel gerechnet. Über die Breite ein Profil (außen steigend, in der Mitte
/// flach, innen fallend), die Farbe kommt aus einer gespiegelten Umgebung statt aus Licht: heller Himmel, dunkler
/// Horizont, darunter heller werdender Boden, dazu ein Glanzpunkt von oben links. So ist der Rand oben außen hell und
/// innen dunkel, unten außen dunkel und innen hell, wie in allen drei Vorbildern. Wahlweise unten eine Lippe (die Seite
/// des Rahmens). Die Werte je Familie stehen im <see cref="Stilprofil"/> (<see cref="Chromfamilie"/>).
/// <para>
/// Gerechnet wird je Größe, Skalierung, Form, Breite und Werten einmal (<see cref="Chrombild"/>), eingefroren und von allen
/// gleichen Rändern geteilt; beim Zeichnen nie neu. Die Form folgt Rundung, Ecke oder einem <see cref="Glasdesign.Umriss"/>
/// (<see cref="Abstandsfeld"/>). Das Element selbst ist die Form samt Lippe; innen bleibt es durchsichtig.
/// </para>
/// </summary>
public sealed class Chromrand : Familienkoerper
{
    public static readonly DependencyProperty FamilieProperty = DependencyProperty.Register(
        nameof(Familie), typeof(Chromfamilie), typeof(Chromrand), new FrameworkPropertyMetadata(Chromfamilie.Chromrahmen, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Breite des Rands als Anteil der kürzeren Seite der Form (ohne Lippe), mindestens ein Gerätepixel.</summary>
    public static readonly DependencyProperty AnteilProperty = DependencyProperty.Register(
        nameof(Anteil), typeof(double), typeof(Chromrand), new FrameworkPropertyMetadata(0.1, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Höhe der Lippe unter dem Rand in DIP (0: keine); sie gehört zur Höhe des Elements.</summary>
    public static readonly DependencyProperty LippeProperty = DependencyProperty.Register(
        nameof(Lippe), typeof(double), typeof(Chromrand), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public Chromfamilie Familie { get => (Chromfamilie)GetValue(FamilieProperty); set => SetValue(FamilieProperty, value); }
    public double Anteil { get => (double)GetValue(AnteilProperty); set => SetValue(AnteilProperty, value); }
    public double Lippe { get => (double)GetValue(LippeProperty); set => SetValue(LippeProperty, value); }

    /// <summary>Das zuletzt gezeichnete Bild (für die Tests).</summary>
    internal Chrombild? Bild { get; private set; }

    private protected override object Teilschluessel() => (Familie, Anteil, Lippe);

    /// <summary>Bauen legt das Bild am Rand selbst ab (<see cref="Bild"/>); geteilt wird schon das Chrombild.</summary>
    private protected override bool Teilbar => false;

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        // Die Lippe gehört zur Höhe des Elements: die Form ist um sie niedriger.
        var lippe = Lippe > 0 ? Glasgeometrie.Linie(Lippe, f.Skala) : 0;
        var form = lippe > 0 ? Flaeche(f.Breite, Math.Max(4, f.Hoehe - lippe), f.Skala) : f;
        var breite = Math.Max(1, Math.Round(Math.Min(form.Breite, form.Hoehe) * Math.Clamp(Anteil, 0, 0.5)));
        var art = p.Chrom(Familie);
        if (Zustand == Glaszustand.Gesperrt) art = art.Gesperrt();
        Bild = Chrombild.Holen(form, (int)breite, (int)lippe, art);
        Schicht(Bild.Quelle, new Rect(0, 0, Bild.Breite / f.Skala, Bild.Hoehe / f.Skala));
    }
}

/// <summary>
/// Ein gerechneter Chromrand: Pbgra32 mit der Auflösung der Skalierung, eingefroren. Die zuletzt gerechneten (je Faden 48)
/// teilen sich alle Ränder gleicher Form, Breite, Lippe und Werte; ein Regler, der einen Wert zieht, rechnet je Schritt
/// also jeden verschiedenen Rand einmal, nicht jeden Knopf.
/// </summary>
internal sealed class Chrombild
{
    [ThreadStatic]
    private static List<Chrombild>? _geteilt;

    private const int GeteiltHoechstens = 48;

    private Chrombild(Glasflaeche form, int breite, int lippe, Chromart art, BitmapSource quelle)
    {
        Form = form;
        Randbreite = breite;
        Lippe = lippe;
        Art = art;
        Quelle = quelle;
    }

    public Glasflaeche Form { get; }
    public int Randbreite { get; }
    public int Lippe { get; }
    public Chromart Art { get; }
    public BitmapSource Quelle { get; }
    public int Breite => Quelle.PixelWidth;
    public int Hoehe => Quelle.PixelHeight;

    /// <summary>Wie viele Bilder bisher gerechnet wurden (für die Tests: gleiche Ränder rechnen nichts neu).</summary>
    internal static int Gerechnet { get; private set; }

    public static Chrombild Holen(Glasflaeche form, int breite, int lippe, Chromart art)
    {
        var geteilt = _geteilt ??= [];
        for (var i = geteilt.Count - 1; i >= 0; i--)
        {
            var g = geteilt[i];
            if (g.Form == form && g.Randbreite == breite && g.Lippe == lippe && g.Art == art) return g;
        }
        var neu = new Chrombild(form, breite, lippe, art, Rechnen(form, breite, lippe, art));
        Gerechnet++;
        if (geteilt.Count >= GeteiltHoechstens) geteilt.RemoveAt(0);
        geteilt.Add(neu);
        return neu;
    }

    /// <summary>Neigung des Profils (Tangens) bei <paramref name="u"/> (0 außen bis 1 innen).</summary>
    internal static double Neigung(double u, Chromart a)
    {
        if (u < a.Aussen) return a.Steil * Math.Cos(Math.PI / 2 * u / Math.Max(1e-6, a.Aussen));
        if (u > 1 - a.Innen) return -a.Steil * Math.Sin(Math.PI / 2 * (u - (1 - a.Innen)) / Math.Max(1e-6, a.Innen));
        return 0;
    }

    private static double Glatt(double a, double b, double x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // Glanzpunkt von oben links (Licht), einmal normiert.
    private static readonly (double X, double Y, double Z) Licht = Norm(-0.45, -0.7, 0.55);

    private static (double X, double Y, double Z) Norm(double x, double y, double z)
    {
        var l = Math.Sqrt(x * x + y * y + z * z);
        return (x / l, y / l, z / l);
    }

    /// <summary>Helligkeit 0 bis 1 der gespiegelten Umgebung für die Normale (nx, ny, nz) an der Lage (px, py) 0 bis 1.</summary>
    internal static double Spiegel(double nx, double ny, double nz, double px, double py, Chromart a)
    {
        // Flache Teile gelten als leicht gewölbt: oben schauen sie zum Himmel, unten zum Horizont.
        (nx, ny, nz) = Norm(nx + (px - 0.5) * a.Woelbung * 0.35, ny + (py - 0.5) * a.Woelbung, nz);
        var rx = 2 * nz * nx;
        var ry = 2 * nz * ny;
        var rz = 2 * nz * nz - 1;
        double env;
        if (ry < a.HorizontLage - 0.38) env = a.Himmel;
        else if (ry < a.HorizontLage) env = a.Himmel + (a.Horizont - a.Himmel) * Glatt(a.HorizontLage - 0.38, a.HorizontLage, ry);
        else env = a.Horizont + (a.Boden - a.Horizont) * Glatt(a.HorizontLage, 1.0, ry);
        env += -0.05 * rx;
        var glanz = Math.Max(0, rx * Licht.X + ry * Licht.Y + rz * Licht.Z);
        // glanz^36 über wiederholtes Quadrieren (glanz^32 * glanz^4): schneller als Math.Pow je Pixel.
        var g2 = glanz * glanz;
        var g4 = g2 * g2;
        var g32 = g4 * g4;
        g32 *= g32;
        g32 *= g32;
        return Math.Clamp(env + g32 * g4 * a.Glanz, 0, 1);
    }

    private static BitmapSource Rechnen(Glasflaeche form, int breite, int lippe, Chromart art)
    {
        var b = (int)form.Breite;
        var h = (int)form.Hoehe;
        var hoehe = h + lippe;
        var feld = Abstandsfeld.Holen(form);
        var px = new byte[b * hoehe * 4];
        // Große Bilder zeilenweise parallel: jede Zeile schreibt nur ihre eigenen Pixel, das Ergebnis bleibt gleich.
        if (b * hoehe >= 40000)
            Parallel.For(0, hoehe, y => Zeile(y, b, h, lippe, breite, art, feld, px));
        else
            for (var y = 0; y < hoehe; y++) Zeile(y, b, h, lippe, breite, art, feld, px);
        var bild = BitmapSource.Create(b, hoehe, 96 * form.Skala, 96 * form.Skala, PixelFormats.Pbgra32, null, px, b * 4);
        bild.Freeze();
        return bild;
    }

    private static void Zeile(int y, int b, int h, int lippe, int breite, Chromart art, Abstandsfeld feld, byte[] px)
    {
        const double Ton = 0.015;
        for (var x = 0; x < b; x++)
        {
            var d = y < h ? feld.Abstand(x, y) : Abstandsfeld.Weit;
            // Schnell vorbei an allem, was weder Rand noch Lippe ist (außen oder tief innen).
            if (lippe == 0 && (d > 0.5 || -d > breite + 1)) continue;
            double r = 0, g = 0, bl = 0, a = 0;
            if (lippe > 0 && d > -0.5)
            {
                // Die Lippe: die Form um die Lippe nach unten versetzt, wo die Form selbst nicht mehr deckt. An der Fuge
                // hell, unten dunkel, an den Seiten etwas dunkler.
                var ys = y - lippe;
                var ds = ys >= 0 ? feld.Abstand(x, ys) : Abstandsfeld.Weit;
                var deckS = Math.Clamp(0.5 - ds, 0, 1);
                if (deckS > 0)
                {
                    var w = Math.Clamp(Math.Max(0, d) / lippe, 0, 1);
                    var nx = ys >= 0 ? feld.Normale(x, ys).X : 0;
                    var v = Math.Clamp(0.30 + 0.38 * Glatt(0, 0.5, w) - 0.25 * Glatt(0.6, 1, w) - 0.1 * nx * nx, 0, 1);
                    (r, g, bl, a) = Ueber(r, g, bl, a, v * (1 - Ton), v, Math.Min(1, v * (1 + Ton * 1.5)), deckS);
                }
            }
            var deck = Math.Clamp(0.5 - d, 0, 1);
            var e = -d;
            if (deck > 0 && e <= breite + 1)
            {
                var innen = Math.Clamp(0.5 + (breite - e), 0, 1);
                var u = Math.Clamp(e / breite, 0, 1);
                var (ox, oy) = feld.Normale(x, y);
                var k = Neigung(u, art);
                var (nx, ny, nz) = Norm(k * ox, k * oy, 1);
                var v = Spiegel(nx, ny, nz, (x + 0.5) / b, (y + 0.5) / h, art);
                (r, g, bl, a) = Ueber(r, g, bl, a, v * (1 - Ton), v, Math.Min(1, v * (1 + Ton * 1.5)), deck * innen);
            }
            if (a <= 0) continue;
            var o = (y * b + x) * 4;
            px[o] = (byte)Math.Round(Math.Min(bl, a) * 255);
            px[o + 1] = (byte)Math.Round(Math.Min(g, a) * 255);
            px[o + 2] = (byte)Math.Round(Math.Min(r, a) * 255);
            px[o + 3] = (byte)Math.Round(a * 255);
        }
    }

    /// <summary>Vormultipliziert eine Farbe mit Deckung <paramref name="d"/> über (r, g, b, a) legen.</summary>
    private static (double R, double G, double B, double A) Ueber(double r, double g, double b, double a, double fr, double fg, double fb, double d)
    {
        if (d <= 0) return (r, g, b, a);
        d = Math.Min(1, d);
        return (fr * d + r * (1 - d), fg * d + g * (1 - d), fb * d + b * (1 - d), d + a * (1 - d));
    }
}

