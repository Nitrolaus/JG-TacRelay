// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Der Körper eines Felds (Stil Glas.Feld): eine umrandete Fläche mit Eckenradius, wie sie Programme um Statuszeilen und
/// Gruppen legen (Sarevocs Wunsch vom 08.10.2026, Vorbild das Statusfeld in Joystick Guardian). Schichten von außen nach innen:
/// weiches Leuchten in der Randfarbe (Ringe von je einem Gerätepixel, Abfall (1-x)^1,5 wie beim Leuchtsaum), der Rand in
/// ganzen Gerätepixeln, die Fläche, links wahlweise eine Akzentleiste in der Rundung, oben eine feine Glanzlinie. Alles
/// liegt auf ganzen Gerätepixeln; die Geometrie entsteht je Größe einmal und ist eingefroren. Leuchten reicht über die
/// eigenen Grenzen hinaus und braucht Platz.
/// </summary>
public sealed class Feldkoerper : FrameworkElement
{
    public static readonly DependencyProperty FlaecheProperty = Pinsel(nameof(Flaeche));
    public static readonly DependencyProperty RandProperty = Pinsel(nameof(Rand));
    public static readonly DependencyProperty AkzentProperty = Pinsel(nameof(Akzent));
    public static readonly DependencyProperty EckeProperty = Zahl(nameof(Ecke), 12);
    public static readonly DependencyProperty RandbreiteProperty = Zahl(nameof(Randbreite), 1);
    public static readonly DependencyProperty AkzentbreiteProperty = Zahl(nameof(Akzentbreite), 0);
    public static readonly DependencyProperty LeuchtenProperty = Zahl(nameof(Leuchten), 0);
    public static readonly DependencyProperty LeuchtweiteProperty = Zahl(nameof(Leuchtweite), 8);
    public static readonly DependencyProperty GlanzProperty = Zahl(nameof(Glanz), 0);

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Feldkoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Zahl(string name, double vorgabe) => DependencyProperty.Register(
        name, typeof(double), typeof(Feldkoerper), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    public Feldkoerper() => IsHitTestVisible = false;

    public Brush? Flaeche { get => (Brush?)GetValue(FlaecheProperty); set => SetValue(FlaecheProperty, value); }
    public Brush? Rand { get => (Brush?)GetValue(RandProperty); set => SetValue(RandProperty, value); }

    /// <summary>Farbe der Akzentleiste links; ohne Pinsel oder mit Breite 0 keine Leiste.</summary>
    public Brush? Akzent { get => (Brush?)GetValue(AkzentProperty); set => SetValue(AkzentProperty, value); }

    /// <summary>Eckenradius in DIP.</summary>
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }

    /// <summary>Breite des Rands in DIP, mindestens ein Gerätepixel; 0 heißt kein Rand.</summary>
    public double Randbreite { get => (double)GetValue(RandbreiteProperty); set => SetValue(RandbreiteProperty, value); }

    public double Akzentbreite { get => (double)GetValue(AkzentbreiteProperty); set => SetValue(AkzentbreiteProperty, value); }

    /// <summary>Stärke des weichen Leuchtens außen in der Randfarbe (0 bis 1).</summary>
    public double Leuchten { get => (double)GetValue(LeuchtenProperty); set => SetValue(LeuchtenProperty, value); }

    /// <summary>Wie weit das Leuchten reicht, in DIP.</summary>
    public double Leuchtweite { get => (double)GetValue(LeuchtweiteProperty); set => SetValue(LeuchtweiteProperty, value); }

    /// <summary>Deckung der feinen Glanzlinie oben innen (0 bis 1).</summary>
    public double Glanz { get => (double)GetValue(GlanzProperty); set => SetValue(GlanzProperty, value); }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Flaeche, Rand, Akzent, Ecke, Randbreite, Akzentbreite, Leuchten, Leuchtweite, Glanz);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            Bauen(s);
        }
        foreach (var (form, pinsel) in _schichten) drawingContext.DrawGeometry(pinsel, null, form);
    }

    private void Bauen(double s)
    {
        _schichten.Clear();
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 4 || h < 4) return;
        var r = Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), Math.Min(b, h) / 2);
        var f = new Glasflaeche(0, 0, b, h, r, s);
        var rand = Randbreite <= 0 ? 0 : Glasgeometrie.Linie(Randbreite, s);

        // Leuchten: Ringe außerhalb, je ein Gerätepixel, nach außen schwächer.
        if (Leuchten > 0 && Rand is SolidColorBrush { Color: var leucht })
        {
            var weite = Math.Max(1, (int)Glasgeometrie.Raster(Leuchtweite, s));
            for (var i = 0; i < weite; i++)
            {
                var t = (i + 0.5) / weite;
                var deckung = Math.Clamp(Leuchten * 0.55 * Math.Pow(1 - t, 1.5), 0, 1);
                if (deckung < 0.004) continue;
                Schicht(f.Ring(-(i + 1), -i), Farbton.Deckend(leucht, deckung * leucht.A / 255.0));
            }
        }
        if (rand > 0 && Rand is not null) Schicht(f.Ring(0, rand), Rand);
        if (Flaeche is not null) Schicht(f.Form(rand), Flaeche);

        // Akzentleiste links: Fläche in der Form, durch einen Pinsel mit fester Kante auf die Breite begrenzt.
        var akzent = Glasgeometrie.Raster(Akzentbreite, s);
        if (akzent > 0 && Akzent is SolidColorBrush { Color: var a })
        {
            var grenze = (rand + akzent) / s;
            var leiste = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(b / s, 0),
                GradientStops = { new GradientStop(a, 0), new GradientStop(a, grenze / (b / s)), new GradientStop(Colors.Transparent, grenze / (b / s)) },
            };
            _schichten.Add((f.Form(rand), Eingefroren(leiste)));
        }

        // Glanzlinie: die oberste Pixelreihe der Fläche zwischen den Rundungen, zur Mitte hin am hellsten.
        if (Glanz > 0 && b - 2 * Math.Max(r, rand) > 2)
        {
            var links = Math.Max(r, rand);
            var linie = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(links / s, 0), EndPoint = new Point((b - links) / s, 0),
                GradientStops = { new GradientStop(Farbton.Deckend(Colors.White, 0), 0), new GradientStop(Farbton.Deckend(Colors.White, Glanz), 0.5), new GradientStop(Farbton.Deckend(Colors.White, 0), 1) },
            };
            var form = new RectangleGeometry(new Rect(f.Dip(links, rand), f.Dip(b - links, rand + 1)));
            form.Freeze();
            _schichten.Add((form, Eingefroren(linie)));
        }
    }

    private void Schicht(Geometry form, Color farbe) => _schichten.Add((form, Eingefroren(new SolidColorBrush(farbe))));

    private void Schicht(Geometry form, Brush pinsel) => _schichten.Add((form, pinsel));

    private static Brush Eingefroren(Brush p)
    {
        if (p.CanFreeze) p.Freeze();
        return p;
    }
}
