// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Aufkleber (seit 0.5.0-beta.11, Stilfamilien 2 Stufe 2): ein runder Aufkleber, die Farbe über Background, der Inhalt (kurzer
/// Text wie "Neu" oder "-20 %") mittig, mit <see cref="Ecke"/> oben links umgeknickt. Kein Knopf: nicht fokussierbar, nicht
/// bedienbar. Automation: ein Text, der Name aus dem Inhalt. Stile: Glas.Aufkleber (Glas 4, rundes Farbglas im Ton der
/// Farbstellung) und Glas.Glanzkugel.Aufkleber (satte Farbe, weißer Rand, Bodenschatten, Ecke an). Rund ist er bei gleicher
/// Breite und Höhe (die Stile setzen beide auf Glas.Mass.Aufkleber.Groesse).
/// </summary>
public class Aufkleber : ContentControl
{
    /// <summary>Die Ecke oben links umgeknickt (Rückseite sichtbar, darunter ein weicher Schatten).</summary>
    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(bool), typeof(Aufkleber), new FrameworkPropertyMetadata(false));

    static Aufkleber()
    {
        FocusableProperty.OverrideMetadata(typeof(Aufkleber), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Aufkleber), new FrameworkPropertyMetadata(false));
    }

    public bool Ecke { get => (bool)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }

    /// <summary>
    /// Schriftwahl auf dem Farbglas (Glas.Aufkleber, MultiBinding): Wunsch, Background und IsEnabled. Gesperrt rechnet sie auf
    /// der Fläche, die das Farbglas dann zeichnet (die Farbe zu 70 Prozent zum Grau #8A8F94 gemischt), sonst auf dem Background.
    /// </summary>
    public static IMultiValueConverter Glasschrift { get; } = new Glasschriftwandler();

    /// <summary>
    /// Die Farbe des Farbglases, abgestimmt (seit 0.5.0-beta.13, <see cref="Schriftwahl.Abstimmung"/>): Auf einer frei gewählten
    /// Farbe aus dem Mittelbereich, auf der weder Weiß noch fast Schwarz auf dem gewölbten Glas hält, so wenig wie nötig
    /// abgedunkelt, bis Weiß hält. Converter für die Vorlage (Background zu Farbe des Körpers); dieselbe Farbe nimmt
    /// <see cref="Glasschrift"/>. Bei der Vorgabe kommt der Pinsel unverändert zurück.
    /// </summary>
    public static IValueConverter Glasfarbe { get; } = new Glasfarbwandler();

    /// <summary>Die Farben unter der Schrift auf dem gewölbten Farbglas (Kissen): oben ein Hauch heller, unten dunkler.</summary>
    private static IReadOnlyList<Color> Glasband(Color c) =>
        [Farbton.Mischen(c, Color.FromArgb(c.A, 255, 255, 255), 0.03), c, Farbton.Mischen(c, Color.FromArgb(c.A, 0, 0, 0), 0.03)];

    private static double Abstimmung(Color c) => Schriftwahl.Abstimmung(c, t => [Glasband(Schriftwahl.Abgestimmt(c, t))]);

    private sealed class Glasfarbwandler : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not SolidColorBrush { Color: var c } pinsel) return value;
            var t = Abstimmung(c);
            if (t == 0) return pinsel;
            var neu = new SolidColorBrush(Schriftwahl.Abgestimmt(c, t));
            neu.Freeze();
            return neu;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class Glasschriftwandler : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null || values.Length < 2 || values[0] is not Brush wunsch) return DependencyProperty.UnsetValue;
            var gesperrt = values.Length > 2 && values[2] is false;
            IReadOnlyList<Color> flaeche = values[1] switch
            {
                SolidColorBrush { Color: var c } => Glasband(Schriftwahl.Abgestimmt(c, Abstimmung(c))),
                Brush b => Schriftwahl.Farben(b),
                _ => [],
            };
            if (gesperrt) flaeche = [.. flaeche.Select(c => Farbton.Mischen(c, Color.FromRgb(0x8A, 0x8F, 0x94), 0.7))];
            return Schriftwahl.Waehlen(wunsch, flaeche, gesperrt);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Aufkleberflaeche(this);
}

/// <summary>Automation des <see cref="Aufkleber"/>: ein Text, der Name aus AutomationProperties.Name, sonst aus dem Inhalt.</summary>
internal sealed class Aufkleberflaeche(Aufkleber aufkleber) : FrameworkElementAutomationPeer(aufkleber)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => nameof(Aufkleber);

    protected override string GetNameCore()
    {
        var name = AutomationProperties.GetName(aufkleber);
        return string.IsNullOrEmpty(name) ? Rueckmeldungstext.Von(aufkleber.Content) : name;
    }

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;
}

/// <summary>
/// Die Form des umgeknickten Aufklebers, am Vorbild 4 gemessen (16.10.2026, Ausschnitt in tmp, Radius 108 Pixel): Die
/// Schnittlinie reicht über 83 Grad des Rands (von 93,5 bis 176,5 Grad, um die Richtung oben links), als Sehne also 1,33
/// Radien lang und 0,75 Radien von der Mitte; sie ist nicht gerade, sondern um 0,044 Radien nach außen gewölbt (dort rollt
/// sich das Blatt). Der umgeschlagene Teil reicht von ihr 0,32 Radien zur Mitte hin (die Spitze bleibt 0,43 Radien vor der
/// Mitte), seine freie Kante ist ein Bogen; dahinter fällt ein schmaler weicher Schatten auf die Farbfläche. Die Stilstudie
/// hatte die Sehne bei 0,64 Radien und als Umschlag die an ihr gespiegelte Kappe (zu groß, gerade Schnittlinie).
/// </summary>
public static class Aufkleberform
{
    /// <summary>Halber Winkel der Schnittlinie um die Richtung oben links (135 Grad), in Grad.</summary>
    public const double Halbwinkel = 41.5;

    /// <summary>Wölbung der Schnittlinie nach außen, Anteil des Radius.</summary>
    public const double Bauch = 0.044;

    /// <summary>Tiefe des Umschlags von der Sehne zur Mitte hin, Anteil des Radius.</summary>
    public const double Umschlag = 0.324;

    /// <summary>Breite des Rands (Glanzkugel: weißes Papier), Anteil des Radius.</summary>
    public const double Rand = 0.1;

    /// <summary>Der Kreis des Aufklebers in Pixeln eines Elements von <paramref name="breitePx"/> mal <paramref name="hoehePx"/>: so groß wie die kürzere Seite, mittig.</summary>
    public static Rect Kreis(double breitePx, double hoehePx)
    {
        var d = Math.Min(breitePx, hoehePx);
        return new Rect(Math.Round((breitePx - d) / 2), Math.Round((hoehePx - d) / 2), d, d);
    }

    /// <summary>Die Enden der Schnittlinie auf dem Rand (Mitte und Radius in derselben Einheit).</summary>
    public static (Point Oben, Point Links) Enden(Point mitte, double radius)
    {
        static Point P(Point m, double r, double grad)
        {
            var w = grad * Math.PI / 180;
            return new Point(m.X + r * Math.Cos(w), m.Y - r * Math.Sin(w));
        }
        return (P(mitte, radius, 135 - Halbwinkel), P(mitte, radius, 135 + Halbwinkel));
    }

    /// <summary>Richtung von oben links zur Mitte (Einheitsvektor).</summary>
    private static Vector Innen => new(Math.Sqrt(0.5), Math.Sqrt(0.5));

    /// <summary>Ein Kreisbogen von <paramref name="von"/> nach <paramref name="bis"/>, der die Sehne um <paramref name="tiefe"/> (vorzeichenbehaftet, positiv zur Mitte hin) verlässt.</summary>
    private static ArcSegment Bogen(Point von, Point bis, double tiefe)
    {
        var c = (bis - von).Length;
        var s = Math.Max(1e-6, Math.Abs(tiefe));
        var r = (c * c / 4 + s * s) / (2 * s);
        var mitte = new Point((von.X + bis.X) / 2, (von.Y + bis.Y) / 2) + Innen * tiefe;
        var kreuz = (bis.X - von.X) * (mitte.Y - von.Y) - (bis.Y - von.Y) * (mitte.X - von.X);
        return new ArcSegment(bis, new Size(r, r), 0, false, kreuz > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true);
    }

    /// <summary>
    /// Was abgeschnitten ist: zwischen der gewölbten Schnittlinie und dem Rand des Kreises, um <paramref name="ueber"/> nach
    /// außen verlängert (damit auch Kantenglättung und Schatten des Körpers dort wegfallen).
    /// </summary>
    public static Geometry Schnittflaeche(Point mitte, double radius, double ueber)
    {
        var (oben, links) = Enden(mitte, radius);
        var weit = radius + Math.Max(0, ueber);
        var (obenWeit, linksWeit) = Enden(mitte, weit);
        var f = new PathFigure { StartPoint = oben, IsClosed = true, IsFilled = true };
        f.Segments.Add(Bogen(oben, links, -Bauch * radius));
        f.Segments.Add(new LineSegment(linksWeit, true));
        f.Segments.Add(new ArcSegment(obenWeit, new Size(weit, weit), 0, false, SweepDirection.Clockwise, true));
        var g = new PathGeometry([f]);
        g.Freeze();
        return g;
    }

    /// <summary>Der umgeschlagene Teil: zwischen der gewölbten Schnittlinie und der freien Kante (Bogen zur Mitte hin).</summary>
    public static Geometry Lasche(Point mitte, double radius, double tiefe = Umschlag)
    {
        var (oben, links) = Enden(mitte, radius);
        var f = new PathFigure { StartPoint = oben, IsClosed = true, IsFilled = true };
        f.Segments.Add(Bogen(oben, links, -Bauch * radius));
        f.Segments.Add(Bogen(links, oben, tiefe * radius));
        var g = new PathGeometry([f]);
        g.Freeze();
        return g;
    }

    /// <summary>Die freie Kante des Umschlags um <paramref name="mehr"/> (gleiche Einheit) tiefer, als offene Linie (für den Schatten).</summary>
    public static Geometry Kante(Point mitte, double radius, double mehr)
    {
        var (oben, links) = Enden(mitte, radius);
        var f = new PathFigure { StartPoint = links, IsClosed = false, IsFilled = false };
        f.Segments.Add(Bogen(links, oben, Umschlag * radius + mehr));
        var g = new PathGeometry([f]);
        g.Freeze();
        return g;
    }

    /// <summary>
    /// Zum Vergleich (Bericht, Musterbild): die Ecke der Stilstudie vom 15.10.2026, gerade Sehne bei 0,64 Radien, der Umschlag
    /// die an ihr gespiegelte Kappe.
    /// </summary>
    public static (Geometry Schnitt, Geometry Lasche) Studie(Point mitte, double radius)
    {
        var a = Math.Acos(0.64) * 180 / Math.PI;
        Point P(double grad)
        {
            var w = grad * Math.PI / 180;
            return new Point(mitte.X + radius * Math.Cos(w), mitte.Y - radius * Math.Sin(w));
        }
        var (oben, links) = (P(135 - a), P(135 + a));
        var schnitt = new PathFigure { StartPoint = oben, IsClosed = true, IsFilled = true };
        schnitt.Segments.Add(new LineSegment(links, true));
        schnitt.Segments.Add(new ArcSegment(oben, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        var lasche = new PathFigure { StartPoint = oben, IsClosed = true, IsFilled = true };
        lasche.Segments.Add(new LineSegment(links, true));
        lasche.Segments.Add(new ArcSegment(oben, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        var spiegel = new PathGeometry([lasche]);
        // Spiegeln an der Sehne: x + y = k (k aus einem Endpunkt), also x' = k - y, y' = k - x.
        var k = oben.X + oben.Y;
        spiegel.Transform = new MatrixTransform(new Matrix(0, -1, -1, 0, k, k));
        var gespiegelt = spiegel.GetFlattenedPathGeometry();
        var s = new PathGeometry([schnitt]);
        s.Freeze();
        gespiegelt.Freeze();
        return (s, gespiegelt);
    }
}

/// <summary>
/// Der Körper des Glanzkugel-Aufklebers: ein weicher Bodenschatten (mit Schattenstufe Voll; Deckung GlanzkugelSchatten im
/// Stilprofil), der Rand aus Papier (<see cref="Papier"/>, 0,1 Radien breit) und die satte Farbfläche (<see cref="Farbe"/>)
/// mit einem schrägen Verlauf von oben links (wärmer, dunkler) nach unten rechts (heller), bei der Vorgabe genau die
/// gemessenen Töne des Vorbilds. Mit <see cref="Umgeknickt"/> fehlt oben links, was <see cref="Aufkleberform"/> abschneidet;
/// den umgeschlagenen Teil zeichnet die <see cref="Aufkleberecke"/> darüber. Gesperrt zum Grau hin. Die Schrift wählt er auf
/// der Farbfläche (Schriftwahl).
/// </summary>
public sealed class Aufkleberkoerper : Familienkoerper
{
    /// <summary>Die Vorgabe der Farbfläche (Glas.Glanzkugel.Aufkleber.Flaeche in Standard), Mitte des Verlaufs.</summary>
    public static readonly Color Vorgabe = Color.FromRgb(0xF8, 0xC6, 0x05);

    private static readonly Color VorgabeOben = Color.FromRgb(0xEB, 0xA3, 0x0C);
    private static readonly Color VorgabeUnten = Color.FromRgb(0xFD, 0xD4, 0x03);
    private static readonly Color Grau = Color.FromRgb(0x8A, 0x8C, 0x90);

    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Aufkleberkoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PapierProperty = DependencyProperty.Register(
        nameof(Papier), typeof(Brush), typeof(Aufkleberkoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UmgeknicktProperty = DependencyProperty.Register(
        nameof(Umgeknickt), typeof(bool), typeof(Aufkleberkoerper), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Brush? Papier { get => (Brush?)GetValue(PapierProperty); set => SetValue(PapierProperty, value); }
    public bool Umgeknickt { get => (bool)GetValue(UmgeknicktProperty); set => SetValue(UmgeknicktProperty, value); }

    /// <summary>Die Grundfarbe der Fläche: <see cref="Farbe"/>, sonst <see cref="Vorgabe"/>.</summary>
    public Color Grundfarbe => FarbeVon(Farbe, Vorgabe);

    private Color Papierfarbe => FarbeVon(Papier, Color.FromRgb(0xE7, 0xE7, 0xE7));

    /// <summary>Die drei Farben des schrägen Verlaufs (oben links, Mitte, unten rechts) im jetzigen Zustand.</summary>
    private (Color Oben, Color Mitte, Color Unten) Verlaufsfarben() => Verlaufsfarben(Wirksam, Zustand);

    private static (Color Oben, Color Mitte, Color Unten) Verlaufsfarben(Color g, Glaszustand zustand)
    {
        var (o, m, u) = (Farbableitung.Ableiten(g, Vorgabe, VorgabeOben), g, Farbableitung.Ableiten(g, Vorgabe, VorgabeUnten));
        if (zustand != Glaszustand.Gesperrt) return (o, m, u);
        return (Farbton.Mischen(o, Grau, 0.6), Farbton.Mischen(m, Grau, 0.6), Farbton.Mischen(u, Grau, 0.6));
    }

    /// <summary>
    /// Die Farben unter der Schrift: der schräge Verlauf so weit, wie die Schrift über die Mitte hinaus reicht (seit 0.5.0-beta.13
    /// bis zu 0,35 und 0,65 statt nur bis zur Hälfte zwischen Mitte und Rand; auf Rot und Blau hielt die Schrift sonst nur 4,2 zu 1).
    /// </summary>
    private static IReadOnlyList<Color> Schriftband(Color g, Glaszustand zustand)
    {
        var (o, m, u) = Verlaufsfarben(g, zustand);
        return [Farbton.Mischen(o, m, 0.35), m, Farbton.Mischen(m, u, 0.65)];
    }

    /// <summary>
    /// Die Farbe abgestimmt (seit 0.5.0-beta.13, <see cref="Schriftwahl.Abstimmung"/>): Auf einer frei gewählten Farbe aus dem
    /// Mittelbereich, auf der weder Weiß noch fast Schwarz hält, so wenig wie nötig abgedunkelt, bis Weiß hält (auf Rot wie im
    /// Vorbild Weiß); die Vorgabe Gelb bleibt, wie sie ist.
    /// </summary>
    private Color Wirksam
    {
        get
        {
            var g = Grundfarbe;
            return Schriftwahl.Abgestimmt(g, Abstimmen(g, g, (t, z) => Schriftband(Schriftwahl.Abgestimmt(g, t), z)));
        }
    }

    private protected override object Teilschluessel() => (Grundfarbe, Papierfarbe, Umgeknickt);

    private protected override IReadOnlyList<Color> Flaechenfarben() => Schriftband(Wirksam, Zustand);

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        var k = Aufkleberform.Kreis(f.Breite, f.Hoehe);
        if (k.Width < 8) return;
        var kreis = new Glasflaeche(k.X, k.Y, k.Width, k.Height, k.Width / 2, s);
        var r = k.Width / 2 / s;
        var m = kreis.Dip(k.Width / 2, k.Height / 2);
        var schnitt = Umgeknickt ? Aufkleberform.Schnittflaeche(m, r, 4 / s) : null;
        Geometry Ohne(Geometry g)
        {
            if (schnitt is null) return g;
            var c = Geometry.Combine(g, schnitt, GeometryCombineMode.Exclude, null);
            c.Freeze();
            return c;
        }

        // Bodenschatten: unter dem unteren Rand am dunkelsten, rund 0,2 Radien darunter ausgelaufen; seitlich keiner.
        if (Schatten == Schattenstufe.Voll && p.GlanzkugelSchatten > 0 && Zustand != Glaszustand.Gesperrt)
        {
            var deckung = Math.Clamp(p.GlanzkugelSchatten, 0, 1);
            var sm = new Point(m.X, m.Y + 0.08 * r);
            var form = new EllipseGeometry(sm, r, r * 1.12);
            form.Freeze();
            Schicht(Ohne(form), Radial(sm, r, r * 1.12, (Schwarz(deckung), 0), (Schwarz(deckung), 0.8), (Schwarz(0), 1)));
        }

        var papier = Zustand == Glaszustand.Gesperrt ? Farbton.Mischen(Papierfarbe, Grau, 0.35) : Papierfarbe;
        Schicht(Ohne(kreis.Form(0)), Flaeche(papier));
        var rand = Math.Max(1, Math.Round(Aufkleberform.Rand * k.Width / 2));
        var (o, mi, u) = Verlaufsfarben();
        var n = new Vector(Math.Sqrt(0.5), Math.Sqrt(0.5)) * (0.6 * r);
        Schicht(Ohne(kreis.Form(rand)), Verlauf(m - n, m + n, (o, 0), (mi, 0.5), (u, 1)));
    }
}

/// <summary>
/// Die umgeknickte Ecke eines <see cref="Aufkleber"/> (Form aus <see cref="Aufkleberform"/>): der umgeschlagene Teil als
/// gewölbtes Blatt (<see cref="Rueckseite"/>, quer zur Schnittlinie verlaufend: an der Schnittlinie grauer, wo es sich rollt,
/// ein heller Streif, zur freien Kante wieder grauer) und dahinter ein schmaler weicher Schatten auf der Farbfläche.
/// <see cref="Schnitt"/> ist die Fläche ohne das Abgeschnittene, als Clip für einen Körper, der selbst keine Ecke kennt (das
/// Farbglas in Glas.Aufkleber). Je Größe und Werten einmal gebaut und eingefroren.
/// </summary>
public sealed class Aufkleberecke : FrameworkElement
{
    private static readonly Color VorgabePapier = Color.FromRgb(0xE7, 0xE7, 0xE7);

    public static readonly DependencyProperty RueckseiteProperty = DependencyProperty.Register(
        nameof(Rueckseite), typeof(Color), typeof(Aufkleberecke), new FrameworkPropertyMetadata(VorgabePapier, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyPropertyKey SchnittKey = DependencyProperty.RegisterReadOnly(
        nameof(Schnitt), typeof(Geometry), typeof(Aufkleberecke), new FrameworkPropertyMetadata(null));

    /// <summary>Die Fläche des Elements (weit über die Grenzen) ohne das Abgeschnittene, in DIP; als Clip zu binden.</summary>
    public static readonly DependencyProperty SchnittProperty = SchnittKey.DependencyProperty;

    private object? _schluessel;
    private object? _schnittschluessel;
    private readonly List<(Geometry Form, Brush? Pinsel, Pen? Stift)> _schichten = [];

    public Aufkleberecke()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>Die Farbe der Rückseite (Papier, beim Glas eine helle Tönung der Glasfarbe); die Töne des Blatts sind von ihr abgeleitet.</summary>
    public Color Rueckseite { get => (Color)GetValue(RueckseiteProperty); set => SetValue(RueckseiteProperty, value); }

    public Geometry? Schnitt => (Geometry?)GetValue(SchnittProperty);

    internal int Bauten { get; private set; }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = Glasgeometrie.Skala(this);
        var schluessel = (finalSize, s);
        if (!Equals(_schnittschluessel, schluessel))
        {
            _schnittschluessel = schluessel;
            var (m, r) = Mitte(finalSize, s);
            if (r < 4) SetValue(SchnittKey, null);
            else
            {
                var weit = new RectangleGeometry(new Rect(-finalSize.Width, -finalSize.Height, 3 * finalSize.Width, 3 * finalSize.Height));
                var g = Geometry.Combine(weit, Aufkleberform.Schnittflaeche(m, r, 4 / s), GeometryCombineMode.Exclude, null);
                g.Freeze();
                SetValue(SchnittKey, g);
            }
        }
        return base.ArrangeOverride(finalSize);
    }

    /// <summary>Mitte und Radius (DIP) des Kreises auf ganzen Gerätepixeln, wie <see cref="Aufkleberkoerper"/> ihn zeichnet.</summary>
    private static (Point Mitte, double Radius) Mitte(Size groesse, double s)
    {
        var k = Aufkleberform.Kreis(Glasgeometrie.Raster(groesse.Width, s), Glasgeometrie.Raster(groesse.Height, s));
        return (new Point((k.X + k.Width / 2) / s, (k.Y + k.Height / 2) / s), k.Width / 2 / s);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _schnittschluessel = null;
        InvalidateArrange();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Rueckseite, IsEnabled);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            Bauen(s);
            Bauten++;
        }
        foreach (var (form, pinsel, stift) in _schichten) drawingContext.DrawGeometry(pinsel, stift, form);
    }

    private void Bauen(double s)
    {
        _schichten.Clear();
        var (m, r) = Mitte(RenderSize, s);
        if (r < 4) return;

        // Schatten der freien Kante auf der Farbfläche: Linien von je einem Gerätepixel, nach innen schwächer.
        var n = Math.Max(1, (int)Math.Round(0.03 * r * s));
        for (var i = 0; i < n; i++)
        {
            var deckung = 0.3 * Math.Pow(1 - (i + 0.5) / n, 1.2) * (IsEnabled ? 1 : 0.5);
            var stift = new Pen(Pinselwerk.Flaeche(Farbton.Deckend(Colors.Black, deckung)), 1 / s);
            stift.Freeze();
            _schichten.Add((Aufkleberform.Kante(m, r, (i + 0.5) / s), null, stift));
        }

        // Das gewölbte Blatt: quer zur Schnittlinie von außen (grauer) über einen hellen Streif zur freien Kante (grauer).
        var c = IsEnabled ? Rueckseite : Farbton.Mischen(Rueckseite, Color.FromRgb(0x8A, 0x8C, 0x90), 0.35);
        Color T(int um) => Farbableitung.Ableiten(c, VorgabePapier, Color.FromArgb(0xFF, (byte)Math.Clamp(0xE7 + um, 0, 255), (byte)Math.Clamp(0xE7 + um, 0, 255), (byte)Math.Clamp(0xE7 + um, 0, 255)));
        var innen = new Vector(Math.Sqrt(0.5), Math.Sqrt(0.5));
        var sehne = m - innen * (r * Math.Cos(Aufkleberform.Halbwinkel * Math.PI / 180));
        var aussen = sehne - innen * (Aufkleberform.Bauch * r);
        var frei = sehne + innen * (Aufkleberform.Umschlag * r);
        var blatt = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = aussen, EndPoint = frei };
        foreach (var (um, lage) in new[] { (-0x24, 0.0), (-0x10, 0.25), (0x01, 0.45), (0x0F, 0.6), (-0x03, 0.78), (-0x12, 1.0) })
            blatt.GradientStops.Add(new GradientStop(T(um), lage));
        blatt.Freeze();
        _schichten.Add((Aufkleberform.Lasche(m, r), blatt, null));
    }
}
