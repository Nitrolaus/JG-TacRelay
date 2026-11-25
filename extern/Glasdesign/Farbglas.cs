// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Glasdesign;

/// <summary>
/// Das Farbglasprofil: alle Einstellwerte der Farbglasknöpfe (Sarevocs Vorbild vom 07.10.2026: satte, glänzende Knöpfe mit
/// Spiegelungen, als Prinzip übernommen, nichts nachgezeichnet). Jede Farbstellung trägt eines unter
/// <see cref="Schluessel.Farbglasprofil"/>; fehlt es, gilt <see cref="Standard"/>. Deckungen von 0 bis 1, Anteile der Höhe.
/// </summary>
public sealed record Farbglasprofil
{
    /// <summary>Das Profil mit den Vorgaben, wenn eine Farbstellung keines mitbringt.</summary>
    public static Farbglasprofil Standard { get; } = new();

    /// <summary>Kissen: Breite des gewölbten Rands, als Anteil der kürzeren Seite.</summary>
    public double KissenWulst { get; init; } = 0.17;

    /// <summary>Kissen: wie dunkel der Rand nach außen und unten wird.</summary>
    public double KissenTiefe { get; init; } = 0.55;

    /// <summary>Kissen: Licht auf der oberen Wölbung.</summary>
    public double KissenLicht { get; init; } = 0.38;

    /// <summary>Kissen: der weiße Glanzstrich in der Rundung oben rechts.</summary>
    public double KissenGlanz { get; init; } = 0.95;

    /// <summary>Kissen: Deckung des weichen Schattens unter dem Knopf.</summary>
    public double KissenSchatten { get; init; } = 0.40;

    /// <summary>Gel: wie dunkel der Farbrand ist.</summary>
    public double GelRand { get; init; } = 0.34;

    /// <summary>Gel: Stärke des hellen Glanzbands oben.</summary>
    public double GelGlanz { get; init; } = 0.78;

    /// <summary>Gel: Höhe des Glanzbands als Anteil der Körperhöhe.</summary>
    public double GelGlanzHoehe { get; init; } = 0.46;

    /// <summary>Gel: Licht, das von unten durch das Gel scheint, samt heller Linie über der Unterkante.</summary>
    public double GelBodenlicht { get; init; } = 0.50;

    /// <summary>Gel: das Funkeln an beiden Enden.</summary>
    public double GelFunkeln { get; init; } = 0.95;

    /// <summary>Gel: die Spiegelung unter dem Knopf.</summary>
    public double GelSpiegelung { get; init; } = 0.28;

    /// <summary>Gel: die feine Bodenlinie, auf der der Knopf steht.</summary>
    public double GelBodenlinie { get; init; } = 0.30;

    /// <summary>Metall: Lage der dunklen Horizontkante als Anteil der Höhe (unter der Schrift).</summary>
    public double MetallHorizont { get; init; } = 0.7;

    /// <summary>Metall: wie dunkel die Horizontkante ist (der Kontrast macht das Metall).</summary>
    public double MetallKontrast { get; init; } = 0.62;

    /// <summary>Metall: Helligkeit oben und am Unterrand.</summary>
    public double MetallGlanz { get; init; } = 0.85;

    /// <summary>Metall: kühler Ton in den hellen Stellen; überfahren etwas mehr.</summary>
    public double MetallKuehle { get; init; } = 0.25;

    /// <summary>Metall: Stärke des schrägen Lichtwischers beim Überfahren.</summary>
    public double MetallWischer { get; init; } = 0.5;

    /// <summary>Metall: Deckung des weichen Schattens unter dem Knopf.</summary>
    public double MetallSchatten { get; init; } = 0.45;

    /// <summary>Clay: Licht oben links auf der Wölbung.</summary>
    public double ClayLicht { get; init; } = 0.55;

    /// <summary>Clay: Schatten innen unten.</summary>
    public double ClayTiefe { get; init; } = 0.15;

    /// <summary>Clay: Deckung des Schattens in der eigenen Farbe unter dem Knopf.</summary>
    public double ClaySchatten { get; init; } = 0.6;

    /// <summary>Neo-Brutal: Versatz des harten Schattens in DIP (gedrückt bleiben davon 2).</summary>
    public double BrutalVersatz { get; init; } = 6;

    /// <summary>Neo-Brutal: Breite des Rands in DIP.</summary>
    public double BrutalRand { get; init; } = 2.5;

    /// <summary>Überfahren: so viel heller.</summary>
    public double Ueberfahren { get; init; } = 0.08;

    /// <summary>Gedrückt: so viel dunkler.</summary>
    public double Gedrueckt { get; init; } = 0.12;

    /// <summary>Alle Werte mit Namen, in der Reihenfolge der Deklaration (für Export und Glint).</summary>
    public IReadOnlyList<(string Name, double Wert)> Werte() => Wertesatz.Lesen(this);
}

/// <summary>
/// Der Körper der Farbglasknöpfe, in zwei Macharten (<see cref="Farbglasart"/>): Kissen, satt und weich gewölbt mit
/// Glanzstrich oben rechts und weichem Schatten; Gel, mit dunklerem Farbrand, Glanzband oben, Licht von unten, Funkeln an
/// den Enden, Bodenlinie und Spiegelung darunter. Die Farbe kommt aus <see cref="Farbe"/> (die Vorlage reicht den
/// Background des Knopfs durch), die Rundung aus <see cref="Rundung"/> (Anteil der Höhe, 0,5 ist eine Pille oder ein
/// Kreis). Alle Kanten liegen auf ganzen Gerätepixeln; Pinsel mit fester Lage und eingefroren, keine PushOpacity. Bodenlinie,
/// Spiegelung und Schatten reichen über die eigenen Grenzen hinaus und brauchen Platz darunter. <see cref="Schrift"/> ist
/// die Textfarbe mit dem besseren Kontrast zur Grundfarbe (weiß oder fast schwarz).
/// </summary>
public sealed class Farbglaskoerper : FrameworkElement
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Farbglasart), typeof(Farbglaskoerper),
        new FrameworkPropertyMetadata(Farbglasart.Gel, FrameworkPropertyMetadataOptions.AffectsRender, Geaendert));

    public static readonly DependencyProperty ZustandProperty = DependencyProperty.Register(
        nameof(Zustand), typeof(Glaszustand), typeof(Farbglaskoerper),
        new FrameworkPropertyMetadata(Glaszustand.Normal, FrameworkPropertyMetadataOptions.AffectsRender, Geaendert));

    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Farbglaskoerper),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Geaendert));

    public static readonly DependencyProperty VorgabeFarbeProperty = DependencyProperty.Register(
        nameof(VorgabeFarbe), typeof(Color), typeof(Farbglaskoerper),
        new FrameworkPropertyMetadata(Color.FromRgb(0x17, 0x7A, 0x86), FrameworkPropertyMetadataOptions.AffectsRender, Geaendert));

    public static readonly DependencyProperty RundungProperty = Glas.RundungProperty.AddOwner(
        typeof(Farbglaskoerper), new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Farbglaskoerper), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits, Geaendert));

    public static readonly DependencyProperty ProfilProperty = DependencyProperty.Register(
        nameof(Profil), typeof(Farbglasprofil), typeof(Farbglaskoerper),
        new FrameworkPropertyMetadata(Farbglasprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender, Geaendert));

    private static readonly DependencyPropertyKey InhaltsrandKey = DependencyProperty.RegisterReadOnly(
        nameof(Inhaltsrand), typeof(Thickness), typeof(Farbglaskoerper), new FrameworkPropertyMetadata(new Thickness()));

    /// <summary>Wo der Inhalt liegt: bei Neo-Brutal auf der versetzten Fläche (gedrückt mit ihr), sonst über dem ganzen Knopf.</summary>
    public static readonly DependencyProperty InhaltsrandProperty = InhaltsrandKey.DependencyProperty;

    private static readonly DependencyPropertyKey SchriftKey = DependencyProperty.RegisterReadOnly(
        nameof(Schrift), typeof(Brush), typeof(Farbglaskoerper), new FrameworkPropertyMetadata(Brushes.White));

    public static readonly DependencyProperty SchriftProperty = SchriftKey.DependencyProperty;

    private static readonly Color Hell = Colors.White;
    private static readonly Color Dunkel = Color.FromRgb(0x1C, 0x20, 0x24);
    private static readonly Color Gesperrt = Color.FromRgb(0x8A, 0x8F, 0x94);

    private (Size Groesse, double Skala, Farbglasart Art, double Rundung, Color Farbe, Glaszustand Zustand, Farbglasprofil Profil)? _schluessel;
    private readonly List<(Geometry Form, Brush? Pinsel, Pen? Stift)> _schichten = [];

    public Farbglaskoerper()
    {
        IsHitTestVisible = false;
        SetResourceReference(ProfilProperty, Schluessel.Farbglasprofil);
        SetResourceReference(VorgabeFarbeProperty, Schluessel.FarbglasFarbe);
    }

    public Farbglasart Art { get => (Farbglasart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public Glaszustand Zustand { get => (Glaszustand)GetValue(ZustandProperty); set => SetValue(ZustandProperty, value); }

    /// <summary>Die Grundfarbe als Pinsel (Background des Knopfs); Volltonfarbe oder der erste Stopp eines Verlaufs.</summary>
    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }

    /// <summary>Grundfarbe der Farbstellung, wenn <see cref="Farbe"/> fehlt oder durchsichtig ist.</summary>
    public Color VorgabeFarbe { get => (Color)GetValue(VorgabeFarbeProperty); set => SetValue(VorgabeFarbeProperty, value); }

    /// <summary>Eckradius als Anteil der Höhe: 0 eckig, 0,5 Pille (oder Kreis bei gleicher Breite).</summary>
    public double Rundung { get => (double)GetValue(RundungProperty); set => SetValue(RundungProperty, value); }

    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }

    public Farbglasprofil Profil
    {
        get => GetValue(ProfilProperty) as Farbglasprofil ?? Farbglasprofil.Standard;
        set => SetValue(ProfilProperty, value);
    }

    /// <summary>Weiß oder fast Schwarz, je nachdem, was auf der Grundfarbe den höheren Kontrast hat.</summary>
    public Brush Schrift => (Brush)GetValue(SchriftProperty);

    public Thickness Inhaltsrand => (Thickness)GetValue(InhaltsrandProperty);

    /// <summary>Neo-Brutal: wie weit die Fläche gegenüber dem Schatten versetzt ist (normal ganz, gedrückt bis auf 2 DIP).</summary>
    private (double Versatz, double Gedrueckt) Brutalversatz()
    {
        var v = Math.Max(0, Profil.BrutalVersatz);
        return (v, Zustand == Glaszustand.Gedrueckt ? Math.Max(0, v - 2) : 0);
    }

    /// <summary>Die wirksame Grundfarbe: <see cref="Farbe"/>, sonst <see cref="VorgabeFarbe"/>.</summary>
    public Color Grundfarbe => Farbe switch
    {
        SolidColorBrush { Color.A: > 0 } s => s.Color,
        GradientBrush { GradientStops.Count: > 0 } g => g.GradientStops[0].Color,
        _ => VorgabeFarbe,
    };

    private static void Geaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var k = (Farbglaskoerper)d;
        k.SchriftSetzen();
        k.EffektSetzen();
        var (v, g) = k.Brutalversatz();
        k.SetValue(InhaltsrandKey, k.Art == Farbglasart.Brutal ? new Thickness(g, g, v - g, v - g) : new Thickness());
    }

    private void SchriftSetzen()
    {
        var grund = Zustand == Glaszustand.Gesperrt ? Farbton.Mischen(Grundfarbe, Gesperrt, 0.7) : Grundfarbe;
        var farbe = Farbton.Kontrast(Hell, grund) >= Farbton.Kontrast(Dunkel, grund) ? Hell : Dunkel;
        if (Zustand == Glaszustand.Gesperrt) farbe = Farbton.Mischen(farbe, grund, 0.45);
        if (Schrift is SolidColorBrush alt && alt.Color == farbe) return;
        var pinsel = new SolidColorBrush(farbe);
        pinsel.Freeze();
        SetValue(SchriftKey, pinsel);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        EffektSetzen();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    /// <summary>Kissen: weicher Schatten nach unten (Tiefe 6 %, Unschärfe 30 % der Höhe); gedrückt halb, gesperrt keiner.</summary>
    private void EffektSetzen()
    {
        var h = RenderSize.Height;
        if (Art is Farbglasart.Gel or Farbglasart.Brutal || Schatten != Schattenstufe.Voll || Zustand == Glaszustand.Gesperrt || h <= 0)
        {
            if (Effect is not null) Effect = null;
            return;
        }
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        var tiefe = Math.Round(h * (gedrueckt ? 0.03 : 0.06), 2);
        var unschaerfe = Math.Round(h * (gedrueckt ? 0.18 : 0.30), 2);
        var deckung = (Art switch { Farbglasart.Metall => Profil.MetallSchatten, Farbglasart.Clay => Profil.ClaySchatten, _ => Profil.KissenSchatten }) * (gedrueckt ? 0.6 : 1);
        // Clay wirft einen weichen Schatten in der eigenen, dunkleren Farbe, tiefer und weiter als Kissen und Metall.
        var farbe = Art == Farbglasart.Clay ? Farbton.Mischen(Grundfarbe, Colors.Black, 0.35) : Colors.Black;
        if (Art == Farbglasart.Clay)
        {
            tiefe = Math.Round(h * (gedrueckt ? 0.08 : 0.18), 2);
            unschaerfe = Math.Round(h * (gedrueckt ? 0.3 : 0.55), 2);
        }
        if (Effect is DropShadowEffect alt && alt.ShadowDepth == tiefe && alt.BlurRadius == unschaerfe && alt.Opacity == deckung && alt.Color == farbe) return;
        var neu = new DropShadowEffect { Color = farbe, Direction = 270, ShadowDepth = tiefe, BlurRadius = unschaerfe, Opacity = deckung, RenderingBias = RenderingBias.Performance };
        neu.Freeze();
        Effect = neu;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Art, Rundung, Grundfarbe, Zustand, Profil);
        if (_schluessel != schluessel)
        {
            _schluessel = schluessel;
            _schichten.Clear();
            var b = Glasgeometrie.Raster(RenderSize.Width, s);
            var h = Glasgeometrie.Raster(RenderSize.Height, s);
            if (b >= 4 && h >= 4)
            {
                var kurz = Math.Min(b, h);
                var r = Rundung >= 0.5 ? kurz / 2 : Math.Round(kurz * Math.Clamp(Rundung, 0, 0.5));
                var f = new Glasflaeche(0, 0, b, h, r, s);
                if (Art == Farbglasart.Kissen) Kissen(f);
                else if (Art == Farbglasart.Metall) Metall(f);
                else if (Art == Farbglasart.Clay) Clay(f);
                else if (Art == Farbglasart.Brutal) Brutal(f);
                else Gel(f);
            }
        }
        foreach (var (form, pinsel, stift) in _schichten) drawingContext.DrawGeometry(pinsel, stift, form);
    }

    /// <summary>Grundfarbe im Zustand: überfahren heller, gedrückt dunkler, gesperrt zum Grau hin.</summary>
    private Color Zustandsfarbe(Farbglasprofil p) => Zustand switch
    {
        Glaszustand.Ueberfahren => Farbton.Mischen(Grundfarbe, Colors.White, p.Ueberfahren),
        Glaszustand.Gedrueckt => Farbton.Mischen(Grundfarbe, Colors.Black, p.Gedrueckt),
        Glaszustand.Gesperrt => Farbton.Mischen(Grundfarbe, Gesperrt, 0.7),
        _ => Grundfarbe,
    };

    private void Schicht(Geometry form, Brush? pinsel, Pen? stift = null)
    {
        pinsel?.Freeze();
        if (stift is not null && stift.CanFreeze) stift.Freeze();
        _schichten.Add((form, pinsel, stift));
    }

    /// <summary>Senkrechter Verlauf mit fester Lage von <paramref name="oben"/> bis <paramref name="unten"/> (DIP).</summary>
    private static LinearGradientBrush Senkrecht(double oben, double unten, params (Color Farbe, double Lage)[] stopps)
    {
        var p = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, oben), EndPoint = new Point(0, unten) };
        foreach (var (farbe, lage) in stopps) p.GradientStops.Add(new GradientStop(farbe, lage));
        return p;
    }

    private static LinearGradientBrush Waagerecht(double links, double rechts, params (Color Farbe, double Lage)[] stopps)
    {
        var p = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(links, 0), EndPoint = new Point(rechts, 0) };
        foreach (var (farbe, lage) in stopps) p.GradientStops.Add(new GradientStop(farbe, lage));
        return p;
    }

    private static Color Weiss(double deckung) => Farbton.Deckend(Colors.White, deckung);

    private static Color Schwarz(double deckung) => Farbton.Deckend(Colors.Black, deckung);

    private static RectangleGeometry Rechteck(Glasflaeche f, double x, double y, double breite, double hoehe, double radius = 0)
    {
        var g = new RectangleGeometry(new Rect(f.Dip(x, y), f.Dip(x + breite, y + hoehe)), radius / f.Skala, radius / f.Skala);
        g.Freeze();
        return g;
    }

    /// <summary>
    /// Kissen: satter Körper, darauf der gewölbte Rand als Ringe von je einem Gerätepixel (außen am dunkelsten, unten
    /// dunkler als oben), Licht auf der oberen Wölbung und der Glanzstrich in der Rundung oben rechts.
    /// </summary>
    private void Kissen(Glasflaeche f)
    {
        var p = Profil;
        var farbe = Zustandsfarbe(p);
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        var hoehe = f.Hoehe / f.Skala;
        Schicht(f.Form(0), Senkrecht(0, hoehe, (Farbton.Mischen(farbe, Colors.White, 0.05), 0), (farbe, 0.55), (Farbton.Mischen(farbe, Colors.Black, 0.04), 1)));

        var n = Math.Max(2, (int)Math.Round(Math.Min(f.Breite, f.Hoehe) * p.KissenWulst));
        var tiefe = p.KissenTiefe * (gedrueckt ? 0.8 : 1);
        var licht = aktiv ? p.KissenLicht * (gedrueckt ? 0.5 : 1) : p.KissenLicht * 0.4;
        for (var i = 0; i < n; i++)
        {
            var t = (i + 0.5) / n;
            var ring = f.Ring(i, i + 1);
            // Rundum dunkel nach außen, unten kräftiger: die Wölbung dreht vom Licht weg.
            var aussen = tiefe * Math.Pow(1 - t, 1.25);
            Schicht(ring, Senkrecht(0, hoehe, (Schwarz(aussen * 0.32), 0), (Schwarz(aussen * 0.5), 0.45), (Schwarz(Math.Min(1, aussen * 1.1)), 1)));
            // Oben fängt die Wölbung Licht, am stärksten in ihrer Mitte.
            var oben = licht * Math.Sin(Math.PI * t);
            if (oben > 0.003) Schicht(ring, Senkrecht(0, hoehe, (Weiss(oben), 0), (Weiss(oben * 0.35), 0.35), (Weiss(0), 0.6)));
        }
        if (!aktiv || p.KissenGlanz <= 0) return;

        // Glanzstrich: ein Bogen in der Rundung oben rechts, mitten auf der Wölbung, an den Enden auslaufend.
        var r = f.Radius;
        var mitte = Math.Max(r - n * 0.5, Math.Min(r, n * 0.6));
        var dicke = Math.Max(1.5, Math.Round(n * 0.5));
        var cx = f.Breite - r;
        var cy = r;
        Point Punkt(double grad) => f.Dip(cx + mitte * Math.Cos(grad * Math.PI / 180), cy - mitte * Math.Sin(grad * Math.PI / 180));
        var (von, bis) = r >= n ? (78.0, 18.0) : (90.0, 30.0);
        var bogen = new StreamGeometry();
        using (var g = bogen.Open())
        {
            g.BeginFigure(Punkt(von), false, false);
            g.ArcTo(Punkt(bis), new Size(mitte / f.Skala, mitte / f.Skala), 0, false, SweepDirection.Clockwise, true, true);
        }
        bogen.Freeze();
        var a = Punkt(von);
        var e = Punkt(bis);
        var glanz = p.KissenGlanz * (gedrueckt ? 0.55 : 1);
        var pinsel = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, StartPoint = a, EndPoint = e,
            GradientStops = { new GradientStop(Weiss(glanz * 0.1), 0), new GradientStop(Weiss(glanz), 0.5), new GradientStop(Weiss(glanz * 0.35), 1) },
        };
        pinsel.Freeze();
        Schicht(bogen, null, new Pen(pinsel, dicke / f.Skala) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    }

    /// <summary>
    /// Metall (Flüssigmetall): ein senkrechter Verlauf wie die Spiegelung auf einem Zylinder, oben hell, zur Mitte etwas
    /// dunkler, dann die harte dunkle Horizontkante unter der Schrift und darunter wieder Licht. Die hellen Stellen kühl
    /// getönt, die Kante oben hell und unten dunkel. Überfahren kühler und mit einem schrägen Lichtwischer.
    /// </summary>
    private void Metall(Glasflaeche f)
    {
        var p = Profil;
        var farbe = Zustandsfarbe(p);
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var ueber = Zustand == Glaszustand.Ueberfahren;
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        var h = f.Hoehe / f.Skala;
        var kuehl = Color.FromRgb(0xE2, 0xF0, 0xFF);
        var kuehle = Math.Clamp(p.MetallKuehle * (ueber ? 1.6 : 1), 0, 1);
        Color Hell(double t) => Farbton.Mischen(Farbton.Mischen(farbe, Colors.White, t), kuehl, kuehle * t);
        var kontrast = p.MetallKontrast * (aktiv ? 1 : 0.4) * (gedrueckt ? 0.85 : 1);
        var glanz = p.MetallGlanz * (aktiv ? 1 : 0.5);
        var hz = Math.Clamp(p.MetallHorizont + (gedrueckt ? -0.03 : 0), 0.2, 0.9);
        Schicht(f.Form(0), Senkrecht(0, h,
            (Hell(glanz * 0.85), 0), (Hell(glanz * 0.45), hz * 0.45), (farbe, hz - 0.06),
            (Farbton.Mischen(farbe, Colors.Black, kontrast), hz), (Farbton.Mischen(farbe, Colors.Black, kontrast * 0.5), hz + 0.08),
            (Hell(glanz * 0.25), Math.Min(0.97, hz + 0.24)), (Hell(glanz * 0.6), 1)));
        // Die Kante: oben ein heller, unten ein dunkler Saum von einem Gerätepixel.
        Schicht(f.Ring(0, Glasgeometrie.Haarlinie), Senkrecht(0, h, (Weiss(0.75 * glanz), 0), (Weiss(0.1), 0.45), (Schwarz(0.35 + kontrast * 0.3), 1)));
        if (ueber && p.MetallWischer > 0)
        {
            var wisch = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = { new GradientStop(Weiss(0), 0.30), new GradientStop(Weiss(p.MetallWischer), 0.42), new GradientStop(Weiss(0), 0.54) },
            };
            Schicht(f.Form(Glasgeometrie.Haarlinie), wisch);
        }
    }

    /// <summary>Clay: satte Fläche mit leichtem Verlauf, Licht oben links auf der Wölbung, Schatten innen unten; der weiche
    /// Schatten in der eigenen Farbe kommt aus dem Effekt.</summary>
    private void Clay(Glasflaeche f)
    {
        var p = Profil;
        var farbe = Zustandsfarbe(p);
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var h = f.Hoehe / f.Skala;
        Schicht(f.Form(0), Senkrecht(0, h, (Farbton.Mischen(farbe, Colors.White, 0.12), 0), (farbe, 0.6), (Farbton.Mischen(farbe, Colors.Black, 0.06), 1)));
        var licht = p.ClayLicht * (aktiv ? 1 : 0.4) * (Zustand == Glaszustand.Gedrueckt ? 0.6 : 1);
        if (licht > 0)
            Schicht(f.Form(0), new RadialGradientBrush
            {
                Center = new Point(0.28, 0.12), GradientOrigin = new Point(0.28, 0.12), RadiusX = 0.55, RadiusY = 0.9,
                GradientStops = { new GradientStop(Weiss(licht), 0), new GradientStop(Weiss(0), 1) },
            });
        if (p.ClayTiefe > 0)
            Schicht(f.Form(0), Senkrecht(0, h, (Schwarz(0), 0.55), (Schwarz(p.ClayTiefe * (Zustand == Glaszustand.Gedrueckt ? 1.4 : 1)), 1)));
    }

    /// <summary>Neo-Brutal: harter Schatten ohne Unschärfe, darauf die flache Fläche mit dickem dunklem Rand; gedrückt rutscht
    /// die Fläche bis auf 2 DIP in den Schatten. Alles auf ganzen Gerätepixeln.</summary>
    private void Brutal(Glasflaeche f)
    {
        var p = Profil;
        var farbe = Zustandsfarbe(p);
        var s = f.Skala;
        var (v, g) = Brutalversatz();
        var vp = Glasgeometrie.Raster(v, s);
        var gp = Glasgeometrie.Raster(g, s);
        var b = f.Breite - vp;
        var h = f.Hoehe - vp;
        if (b < 4 || h < 4) return;
        var r = Math.Min(f.Radius, Math.Min(b, h) / 2);
        var rand = Glasgeometrie.Linie(Math.Max(0.5, p.BrutalRand), s);
        var dunkel = Zustand == Glaszustand.Gesperrt ? Color.FromRgb(0x55, 0x58, 0x5C) : Color.FromRgb(0x11, 0x11, 0x11);
        var schatten = new Glasflaeche(vp, vp, b, h, r, s);
        if (Zustand != Glaszustand.Gesperrt) Schicht(schatten.Form(0), new SolidColorBrush(dunkel));
        var flaeche = new Glasflaeche(gp, gp, b, h, r, s);
        Schicht(flaeche.Form(0), new SolidColorBrush(dunkel));
        Schicht(flaeche.Form(rand), new SolidColorBrush(farbe));
    }

    /// <summary>
    /// Gel: Bodenlinie und Spiegelung hinter dem Knopf, dunklerer Farbrand, Körper oben und unten heller, Licht von unten
    /// mit heller Linie über der Unterkante, das Glanzband oben und das Funkeln an beiden Enden.
    /// </summary>
    private void Gel(Glasflaeche f)
    {
        var p = Profil;
        var farbe = Zustandsfarbe(p);
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        double b = f.Breite, h = f.Hoehe, s = f.Skala;

        if (aktiv && p.GelBodenlinie > 0)
        {
            var dick = Math.Max(1, Math.Round(h * 0.03));
            var weit = Math.Round(b * 0.08);
            Schicht(Rechteck(f, -weit, h - dick, b + 2 * weit, dick),
                Waagerecht(-weit / s, (b + weit) / s, (Schwarz(0), 0), (Schwarz(p.GelBodenlinie), 0.5), (Schwarz(0), 1)));
        }
        if (aktiv && p.GelSpiegelung > 0)
        {
            var abstand = Math.Max(1, Math.Round(h * 0.06));
            var spiegel = new RectangleGeometry(new Rect(f.Dip(0, h + abstand), f.Dip(b, 2 * h + abstand)), f.Radius / s, f.Radius / s);
            spiegel.Freeze();
            var oben = (h + abstand) / s;
            Schicht(spiegel, Senkrecht(oben, oben + h * 0.42 / s, (Farbton.Deckend(farbe, p.GelSpiegelung), 0), (Farbton.Deckend(farbe, 0), 1)));
        }

        var rand = Math.Max(1, Math.Round(h * 0.045));
        var tiefe = p.GelRand * (gedrueckt ? 1.15 : 1);
        Schicht(f.Form(0), Senkrecht(0, h / s, (Farbton.Mischen(farbe, Colors.Black, tiefe * 0.75), 0), (Farbton.Mischen(farbe, Colors.Black, tiefe), 1)));
        var boden = p.GelBodenlicht * (aktiv ? 1 : 0.4);
        Schicht(f.Form(rand), Senkrecht(rand / s, (h - rand) / s,
            (Farbton.Mischen(farbe, Colors.White, 0.10), 0), (farbe, 0.5), (Farbton.Mischen(farbe, Colors.Black, 0.06), 0.78),
            (Farbton.Mischen(farbe, Colors.White, boden * 0.45), 1)));

        // Licht von unten: breiter, weicher Schein über der Unterkante, Mitte am hellsten.
        if (boden > 0)
        {
            var schein = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute, Center = f.Dip(b / 2, h), GradientOrigin = f.Dip(b / 2, h),
                RadiusX = b * 0.48 / s, RadiusY = h * 0.55 / s,
                GradientStops = { new GradientStop(Weiss(boden * 0.55), 0), new GradientStop(Weiss(boden * 0.2), 0.55), new GradientStop(Weiss(0), 1) },
            };
            Schicht(f.Form(rand), schein);
            var linie = Math.Max(1, Math.Round(h * 0.03));
            var y = h - rand - Math.Max(2, Math.Round(h * 0.07)) - linie;
            Schicht(Rechteck(f, b * 0.12, y, b * 0.76, linie, linie / 2),
                Waagerecht(b * 0.12 / s, b * 0.88 / s, (Weiss(0), 0), (Weiss(boden * 0.75), 0.5), (Weiss(0), 1)));
        }

        // Glanzband oben: hell, nach unten schwächer, mit klarer Unterkante.
        var glanz = p.GelGlanz * (gedrueckt ? 0.6 : 1) * (aktiv ? 1 : 0.5);
        if (glanz > 0)
        {
            var einzug = Math.Max(rand + 1, Math.Round(h * 0.09));
            var oben = rand + Math.Max(1, Math.Round(h * 0.035));
            var band = Math.Max(2, Math.Round((h - 2 * rand) * p.GelGlanzHoehe));
            var radius = Math.Min(Math.Max(0, f.Radius - einzug), band / 2);
            Schicht(Rechteck(f, einzug, oben, b - 2 * einzug, band, radius),
                Senkrecht(oben / s, (oben + band) / s, (Weiss(glanz * 0.85), 0), (Weiss(glanz * 0.45), 0.55), (Weiss(glanz * 0.16), 1)));
        }

        // Funkeln an beiden Enden, knapp über der Mitte auf dem Rand: heller Kern und zwei feine Strahlen.
        if (aktiv && p.GelFunkeln > 0)
        {
            var kern = Math.Max(1.5, h * 0.075);
            var strahl = Math.Max(4, Math.Round(h * 0.42));
            var dicke = Math.Max(1, Math.Round(h * 0.025));
            foreach (var x in new[] { rand * 0.8, b - rand * 0.8 })
            {
                var y = Math.Round(h * 0.44);
                var punkt = new EllipseGeometry(f.Dip(x, y), kern / s, kern / s);
                punkt.Freeze();
                Schicht(punkt, new RadialGradientBrush(Weiss(p.GelFunkeln), Weiss(0)));
                Schicht(Rechteck(f, x - strahl / 2, y - dicke / 2, strahl, dicke),
                    Waagerecht((x - strahl / 2) / s, (x + strahl / 2) / s, (Weiss(0), 0), (Weiss(p.GelFunkeln * 0.9), 0.5), (Weiss(0), 1)));
                Schicht(Rechteck(f, x - dicke / 2, y - strahl / 4, dicke, strahl / 2),
                    Senkrecht((y - strahl / 4) / s, (y + strahl / 4) / s, (Weiss(0), 0), (Weiss(p.GelFunkeln * 0.6), 0.5), (Weiss(0), 1)));
            }
        }
    }
}

/// <summary>Farbrechnung für die Farbglasknöpfe: mischen, Deckung, Kontrast nach WCAG.</summary>
public static class Farbton
{
    /// <summary>Linear von <paramref name="a"/> nach <paramref name="b"/> (0 bis 1), samt Deckung.</summary>
    public static Color Mischen(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        static byte M(byte x, byte y, double t) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(M(a.A, b.A, t), M(a.R, b.R, t), M(a.G, b.G, t), M(a.B, b.B, t));
    }

    public static Color Deckend(Color c, double deckung) => Color.FromArgb((byte)Math.Round(Math.Clamp(deckung, 0, 1) * 255), c.R, c.G, c.B);

    /// <summary>Relative Leuchtdichte nach WCAG 2.</summary>
    public static double Leuchtdichte(Color c)
    {
        static double K(byte x)
        {
            var v = x / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * K(c.R) + 0.7152 * K(c.G) + 0.0722 * K(c.B);
    }

    /// <summary>Kontrastverhältnis zweier Farben (1 bis 21).</summary>
    public static double Kontrast(Color a, Color b)
    {
        var (la, lb) = (Leuchtdichte(a), Leuchtdichte(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
