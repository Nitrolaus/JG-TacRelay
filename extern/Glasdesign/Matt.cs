// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glasdesign;

/// <summary>Machart der matten Knöpfe: flache Fläche, Hauptaktion in der Akzentfarbe, oder nur die Linie.</summary>
public enum Mattart
{
    Flaeche,
    Haupt,
    Kontur,
}

/// <summary>
/// Das Mattprofil (Stilfamilie Matt, Sarevocs Wunsch vom 08.10.2026: "angenehm matt, grau, dunkel, ohne Spiegelungen, fein,
/// dezent, unaufdringlich"). Matt kennt keinen Glanz: Zustände sind kleine Helligkeitsstufen der Fläche. Jede Farbstellung
/// trägt eines unter <see cref="Schluessel.Mattprofil"/>; fehlt es, gilt <see cref="Standard"/>. Die Varianten der
/// Stilstudie (Schiefer, Graphit gekörnt, Eingelassen) sind nur Werte dieses Profils, Kontur ist ein eigener Stil.
/// </summary>
public sealed record Mattprofil
{
    /// <summary>Das Profil mit den Vorgaben (Variante Schiefer), wenn eine Farbstellung keines mitbringt.</summary>
    public static Mattprofil Standard { get; } = new();

    /// <summary>Überfahren: so viel Weiß wird in die Fläche gemischt (bei Kontur: Deckung der Fläche).</summary>
    public double Ueberfahren { get; init; } = 0.05;

    /// <summary>Gedrückt: so viel Schwarz wird in die Fläche gemischt.</summary>
    public double Gedrueckt { get; init; } = 0.06;

    /// <summary>Feine Körnung wie Pulverlack, Deckung 0 bis 0,1 (Variante Graphit: 0,04).</summary>
    public double Koernung { get; init; }

    /// <summary>Eingelassen statt aufgelegt, 0 bis 1: Fläche dunkler, oben die Kante dunkel, unten ein Hauch Licht.</summary>
    public double Einlassen { get; init; }

    /// <summary>Gesperrt: Deckung von Fläche und Rand.</summary>
    public double Gesperrt { get; init; } = 0.55;

    /// <summary>Spotlicht auf der matten Linie, als Anteil der Werte im Glanzprofil (dezenter als beim Glas).</summary>
    public double Spotlicht { get; init; } = 0.6;

    /// <summary>Alle Werte mit Namen, in der Reihenfolge der Deklaration (für Export und Glint).</summary>
    public IReadOnlyList<(string Name, double Wert)> Werte() => Wertesatz.Lesen(this);
}

/// <summary>
/// Der Körper der matten Knöpfe (Stile Glas.Matt.Knopf, .Haupt, Glas.Matt.Kontur): eine flache Fläche mit Eckenradius und
/// einer Haarlinie von genau einem Gerätepixel, keine Spiegelung, kein Verlauf, kein Schatten. Überfahren etwas heller,
/// gedrückt etwas dunkler, gesperrt gedämpft. Wahlweise feine Körnung (ein Bildpunkt der Körnung ist ein Gerätepixel) und
/// eingelassen. Alles auf ganzen Gerätepixeln, je Größe und Zustand einmal gebaut und eingefroren.
/// </summary>
public sealed class Mattkoerper : FrameworkElement
{
    public static readonly DependencyProperty ArtProperty = Wert(nameof(Art), Mattart.Flaeche);
    public static readonly DependencyProperty ZustandProperty = Wert(nameof(Zustand), Glaszustand.Normal);
    public static readonly DependencyProperty EckeProperty = Wert(nameof(Ecke), 7.0);
    public static readonly DependencyProperty FarbeProperty = Wert(nameof(Farbe), Color.FromRgb(0x36, 0x3B, 0x40));
    public static readonly DependencyProperty HauptFarbeProperty = Wert(nameof(HauptFarbe), Color.FromRgb(0x1F, 0x5F, 0x67));
    public static readonly DependencyProperty RandFarbeProperty = Wert(nameof(RandFarbe), Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    public static readonly DependencyProperty RandUeberFarbeProperty = Wert(nameof(RandUeberFarbe), Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    public static readonly DependencyProperty ProfilProperty = DependencyProperty.Register(
        nameof(Profil), typeof(Mattprofil), typeof(Mattkoerper), new FrameworkPropertyMetadata(Mattprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Wert<T>(string name, T vorgabe) => DependencyProperty.Register(
        name, typeof(T), typeof(Mattkoerper), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Dictionary<double, ImageBrush> Koernungen = [];
    private static readonly Lock Sperre = new();

    private object? _schluessel;
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    public Mattkoerper()
    {
        IsHitTestVisible = false;
        SetResourceReference(FarbeProperty, Schluessel.MattFarbe);
        SetResourceReference(HauptFarbeProperty, Schluessel.MattHauptFarbe);
        SetResourceReference(RandFarbeProperty, Schluessel.MattRandFarbe);
        SetResourceReference(RandUeberFarbeProperty, Schluessel.MattRandUeberFarbe);
        SetResourceReference(ProfilProperty, Schluessel.Mattprofil);
        SetResourceReference(EckeProperty, Schluessel.MassMattRundung);
    }

    public Mattart Art { get => (Mattart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public Glaszustand Zustand { get => (Glaszustand)GetValue(ZustandProperty); set => SetValue(ZustandProperty, value); }

    /// <summary>Eckenradius in DIP (Glas.Mass.Matt.Rundung).</summary>
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }

    public Color Farbe { get => (Color)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Color HauptFarbe { get => (Color)GetValue(HauptFarbeProperty); set => SetValue(HauptFarbeProperty, value); }
    public Color RandFarbe { get => (Color)GetValue(RandFarbeProperty); set => SetValue(RandFarbeProperty, value); }
    public Color RandUeberFarbe { get => (Color)GetValue(RandUeberFarbeProperty); set => SetValue(RandUeberFarbeProperty, value); }

    public Mattprofil Profil
    {
        get => GetValue(ProfilProperty) as Mattprofil ?? Mattprofil.Standard;
        set => SetValue(ProfilProperty, value);
    }

    /// <summary>Die Farbe der Fläche im aktuellen Zustand (für Tests und Glint).</summary>
    public Color Flaeche
    {
        get
        {
            var p = Profil;
            if (Art == Mattart.Kontur)
            {
                var deckung = Zustand switch { Glaszustand.Ueberfahren => p.Ueberfahren, Glaszustand.Gedrueckt => p.Ueberfahren * 0.6, _ => 0 };
                return Farbton.Deckend(Colors.White, deckung);
            }
            var grund = Art == Mattart.Haupt ? HauptFarbe : Farbe;
            if (p.Einlassen > 0) grund = Farbton.Mischen(grund, Colors.Black, 0.35 * Math.Clamp(p.Einlassen, 0, 1));
            return Zustand switch
            {
                Glaszustand.Ueberfahren => Farbton.Mischen(grund, Colors.White, p.Ueberfahren),
                Glaszustand.Gedrueckt => Farbton.Mischen(grund, Colors.Black, p.Gedrueckt),
                Glaszustand.Gesperrt => Farbton.Deckend(grund, p.Gesperrt * grund.A / 255.0),
                _ => grund,
            };
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Art, Zustand, Ecke, Farbe, HauptFarbe, RandFarbe, RandUeberFarbe, Profil);
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
        var p = Profil;
        var r = Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), Math.Min(b, h) / 2);
        var f = new Glasflaeche(0, 0, b, h, r, s);
        var linie = Glasgeometrie.Haarlinie;
        var koerper = f.Form(linie);

        var flaeche = Flaeche;
        if (flaeche.A > 0) Hinzu(koerper, Eingefroren(new SolidColorBrush(flaeche)));
        if (p.Koernung > 0 && Art != Mattart.Kontur) Hinzu(koerper, Koernung(s, Math.Clamp(p.Koernung, 0, 1) * (Zustand == Glaszustand.Gesperrt ? p.Gesperrt : 1)));

        // Haarlinie: Kontur nimmt die kräftigere Linie, überfahren noch etwas mehr; gesperrt gedämpft.
        var rand = Art == Mattart.Kontur || Zustand == Glaszustand.Ueberfahren ? RandUeberFarbe : RandFarbe;
        if (Art == Mattart.Kontur && Zustand == Glaszustand.Ueberfahren) rand = Farbton.Deckend(rand, Math.Min(1, rand.A / 255.0 * 1.5));
        if (Zustand == Glaszustand.Gesperrt) rand = Farbton.Deckend(rand, rand.A / 255.0 * p.Gesperrt);
        var einlassen = Math.Clamp(p.Einlassen, 0, 1);
        Brush randPinsel = einlassen <= 0
            ? Eingefroren(new SolidColorBrush(rand))
            : Pinselwerk.Senkrecht((Farbton.Mischen(rand, Color.FromArgb(0x40, 0, 0, 0), einlassen), 0), (rand, 0.5),
                (Farbton.Mischen(rand, Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF), einlassen), 1));
        Hinzu(f.Ring(0, linie), randPinsel);
    }

    /// <summary>Körnung: zufälliges Grau, gekachelt so, dass ein Punkt genau ein Gerätepixel ist; je Skalierung einmal.</summary>
    internal static Brush Koernung(double s, double deckung)
    {
        ImageBrush grund;
        lock (Sperre)
        {
            if (!Koernungen.TryGetValue(s, out var k))
            {
                const int N = 64;
                var zufall = new Random(1977);
                var px = new byte[N * N * 4];
                for (var i = 0; i < N * N; i++)
                {
                    var v = (byte)zufall.Next(256);
                    px[i * 4] = v;
                    px[i * 4 + 1] = v;
                    px[i * 4 + 2] = v;
                    px[i * 4 + 3] = 255;
                }
                var bild = BitmapSource.Create(N, N, 96 * s, 96 * s, PixelFormats.Bgra32, null, px, N * 4);
                bild.Freeze();
                k = new ImageBrush(bild) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, N / s, N / s), Stretch = Stretch.None };
                k.Freeze();
                if (Koernungen.Count > 8) Koernungen.Clear();
                Koernungen[s] = k;
            }
            grund = k;
        }
        return Pinselwerk.Gedaempft(grund, deckung);
    }

    private void Hinzu(Geometry form, Brush pinsel) => _schichten.Add((form, pinsel));

    private static Brush Eingefroren(Brush p)
    {
        if (p.CanFreeze) p.Freeze();
        return p;
    }
}
