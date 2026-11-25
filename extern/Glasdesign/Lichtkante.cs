// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Glasdesign;

/// <summary>
/// Die Lichtkante (Stilfamilie 2, KONZEPT.md Abschnitt 5a, Sarevocs "mach die Lichtkante" vom 08.10.2026): Solange etwas läuft
/// (<see cref="Glas.LaeuftProperty"/>, etwa Aufnahme, Senden, Verbinden), liegt auf dem Rand des Knopfs ein Farbverlauf
/// rundum mit einem helleren "Kometen", der langsam kreist, dazu ein weicher Schein nach außen. Steht Windows auf "keine
/// Animationen", kreist er nicht, die Kante bleibt aber sichtbar. Die drei Farben stehen in der Farbstellung
/// (<see cref="Schluessel.LichtkanteAnfangFarbe"/> und folgende), Breite, Schein, Reichweite, Komet und Umlaufzeit im
/// <see cref="Glanzprofil"/>.
/// <para>
/// WPF kennt keinen konischen Verlauf. Er wird deshalb einmal je Farben als Bild gerechnet (Farbe nach dem Winkel um die
/// Mitte) und als Pinsel über den Rand gelegt; das Kreisen dreht nur den Pinsel. Die Ringe sind je Fläche eingefroren.
/// </para>
/// </summary>
public sealed class Lichtkante : FrameworkElement
{
    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(Glasform), typeof(Lichtkante), new FrameworkPropertyMetadata(Glasform.Pille, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eine beliebige Form (Abschnitt 6); gesetzt, gilt sie statt <see cref="Form"/>.</summary>
    public static readonly DependencyProperty UmrissProperty = DependencyProperty.Register(
        nameof(Umriss), typeof(Umriss), typeof(Lichtkante), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Umriss? Umriss
    {
        get => (Umriss?)GetValue(UmrissProperty);
        set => SetValue(UmrissProperty, value);
    }

    /// <summary>Eckenradius in DIP; NaN (Vorgabe) nimmt die Rundung von <see cref="Form"/>.</summary>
    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(double), typeof(Lichtkante), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Rundung als Anteil der kürzeren Seite (0,5 rund), wie bei den Körpern der Stilfamilien 2 (seit 0.5.0-beta.6); NaN
    /// (Vorgabe) nimmt <see cref="Form"/>. <see cref="Ecke"/> geht vor.
    /// </summary>
    public static readonly DependencyProperty RundungProperty = DependencyProperty.Register(
        nameof(Rundung), typeof(double), typeof(Lichtkante), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LaeuftProperty = DependencyProperty.Register(
        nameof(Laeuft), typeof(bool), typeof(Lichtkante), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Lichtkante)d).Kreisen()));

    /// <summary>Wo der Komet steht, in Grad (0 = oben, im Uhrzeigersinn); kreist, solange es läuft.</summary>
    public static readonly DependencyProperty WinkelProperty = DependencyProperty.Register(
        nameof(Winkel), typeof(double), typeof(Lichtkante), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AnfangFarbeProperty = Farbe(nameof(AnfangFarbe), Color.FromRgb(0x2E, 0xE6, 0xD6));
    public static readonly DependencyProperty MitteFarbeProperty = Farbe(nameof(MitteFarbe), Color.FromRgb(0x7C, 0x5C, 0xFF));
    public static readonly DependencyProperty EndeFarbeProperty = Farbe(nameof(EndeFarbe), Color.FromRgb(0xFF, 0x4F, 0xA3));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Lichtkante),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Lichtkante)d).Kreisen()));

    private static DependencyProperty Farbe(string name, Color vorgabe) => DependencyProperty.Register(
        name, typeof(Color), typeof(Lichtkante), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Dictionary<(Color, Color, Color, double), BitmapSource> Bilder = [];
    private static readonly Lock Sperre = new();

    private (Glasflaeche Flaeche, int Breite, int Weite, Geometry Kante, Geometry[] Schein)? _formen;

    static Lichtkante()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Lichtkante), new UIPropertyMetadata(true, (d, _) => ((Lichtkante)d).Kreisen()));
    }

    public Lichtkante()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SetResourceReference(AnfangFarbeProperty, Schluessel.LichtkanteAnfangFarbe);
        SetResourceReference(MitteFarbeProperty, Schluessel.LichtkanteMitteFarbe);
        SetResourceReference(EndeFarbeProperty, Schluessel.LichtkanteEndeFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
        Loaded += (_, _) => Kreisen();
        Unloaded += (_, _) => Anhalten();
        IsVisibleChanged += (_, _) => Kreisen();
    }

    private bool _hoert;

    /// <summary>Ob das Element gerade auf das Umschalten von "Animationen anzeigen" hört.</summary>
    internal bool Hoert => _hoert;

    public Glasform Form { get => (Glasform)GetValue(FormProperty); set => SetValue(FormProperty, value); }
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }
    public double Rundung { get => (double)GetValue(RundungProperty); set => SetValue(RundungProperty, value); }
    public bool Laeuft { get => (bool)GetValue(LaeuftProperty); set => SetValue(LaeuftProperty, value); }
    public double Winkel { get => (double)GetValue(WinkelProperty); set => SetValue(WinkelProperty, value); }
    public Color AnfangFarbe { get => (Color)GetValue(AnfangFarbeProperty); set => SetValue(AnfangFarbeProperty, value); }
    public Color MitteFarbe { get => (Color)GetValue(MitteFarbeProperty); set => SetValue(MitteFarbeProperty, value); }
    public Color EndeFarbe { get => (Color)GetValue(EndeFarbeProperty); set => SetValue(EndeFarbeProperty, value); }

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    /// <summary>Sichtbar und gezeichnet nur, wenn es läuft und das Element bedienbar ist.</summary>
    public bool Sichtbar => Laeuft && IsEnabled;

    /// <summary>
    /// Kreist, solange es läuft, auf dem Bildschirm steht und Windows Animationen erlaubt; sonst steht der Komet. Solange
    /// sie sichtbar auf dem Bildschirm steht, hört sie auf das Umschalten von "Animationen anzeigen" (<see cref="Animationen"/>).
    /// </summary>
    private void Kreisen()
    {
        var bildschirm = Animationen.AufDemBildschirm(this);
        if (Sichtbar && bildschirm) Anmelden();
        else Abmelden();
        var umlauf = Glanzprofil.LichtkanteUmlauf;
        if (!Sichtbar || umlauf <= 0 || !Animationen.Erlaubt || !bildschirm)
        {
            BeginAnimation(WinkelProperty, null);
            return;
        }
        var kreis = new DoubleAnimation(Winkel % 360, Winkel % 360 + 360, TimeSpan.FromMilliseconds(umlauf)) { RepeatBehavior = RepeatBehavior.Forever };
        kreis.Freeze();
        BeginAnimation(WinkelProperty, kreis);
    }

    /// <summary>Aus dem Baum genommen: nicht mehr zuhören, der Komet steht.</summary>
    private void Anhalten()
    {
        Abmelden();
        BeginAnimation(WinkelProperty, null);
    }

    private void Anmelden()
    {
        if (_hoert) return;
        Animationen.Geaendert += AnimationenGeaendert;
        _hoert = true;
    }

    private void Abmelden()
    {
        if (!_hoert) return;
        Animationen.Geaendert -= AnimationenGeaendert;
        _hoert = false;
    }

    private void AnimationenGeaendert(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Kreisen();
        else Dispatcher.BeginInvoke(new Action(Kreisen));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (!Sichtbar) return;
        var p = Glanzprofil;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 4 || h < 4) return;
        var kurz = Math.Min(b, h);
        var r = !double.IsNaN(Ecke) ? Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), kurz / 2)
            : !double.IsNaN(Rundung) ? (Rundung >= 0.5 ? kurz / 2 : Math.Round(kurz * Math.Clamp(Rundung, 0, 0.5)))
            : Form == Glasform.Pille ? kurz / 2 : Glasgeometrie.Raster(kurz * Glasgeometrie.Rundungsanteil, 1);
        var f = Umriss is { } u ? new Glasflaeche(0, 0, b, h, u.Eckradius(b, h, s), s) { Umriss = u } : new Glasflaeche(0, 0, b, h, r, s);
        var breite = (int)Glasgeometrie.Linie(Math.Max(0.1, p.LichtkanteBreite), s);
        var weite = p.LichtkanteSchein > 0 ? Math.Max(1, (int)Glasgeometrie.Raster(p.LichtkanteReichweite, s)) : 0;
        if (_formen is not { } fo || fo.Flaeche != f || fo.Breite != breite || fo.Weite != weite)
        {
            var schein = new Geometry[weite];
            for (var i = 0; i < weite; i++) schein[i] = f.Ring(-(i + 1), -i);
            _formen = (f, breite, weite, f.Ring(0, breite), schein);
        }
        var formen = _formen.Value;
        var bild = Bild(AnfangFarbe, MitteFarbe, EndeFarbe, Math.Clamp(p.LichtkanteKomet, 0, 1));
        for (var i = 0; i < formen.Weite; i++)
        {
            var deckung = p.LichtkanteSchein * Math.Pow(1 - (i + 0.5) / formen.Weite, 1.5);
            if (deckung * 255 < 0.5) continue;
            drawingContext.DrawGeometry(Pinsel(bild, Winkel, deckung), null, formen.Schein[i]);
        }
        drawingContext.DrawGeometry(Pinsel(bild, Winkel, 1), null, formen.Kante);
    }

    /// <summary>Der konische Verlauf als Pinsel über die ganze Fläche, um ihre Mitte gedreht, mit der gewünschten Deckung.</summary>
    private static ImageBrush Pinsel(BitmapSource bild, double winkel, double deckung)
    {
        // Das Bild ist ein Kreis um die Mitte; vergrößert deckt es beim Drehen auch die Ecken der gestreckten Fläche.
        var drehung = new TransformGroup { Children = { new ScaleTransform(1.5, 1.5, 0.5, 0.5), new RotateTransform(winkel, 0.5, 0.5) } };
        var pinsel = new ImageBrush(bild) { Stretch = Stretch.Fill, RelativeTransform = drehung, Opacity = Math.Clamp(deckung, 0, 1) };
        pinsel.Freeze();
        return pinsel;
    }

    /// <summary>Farbe nach dem Winkel (oben beginnend im Uhrzeigersinn: Anfang, Mitte, Ende, zurück), der Komet oben heller.</summary>
    internal static BitmapSource Bild(Color anfang, Color mitte, Color ende, double komet)
    {
        lock (Sperre)
        {
            var schluessel = (anfang, mitte, ende, komet);
            if (Bilder.TryGetValue(schluessel, out var fertig)) return fertig;
            const int N = 128;
            var farben = new[] { anfang, mitte, ende, anfang };
            var px = new byte[N * N * 4];
            for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++)
                {
                    var w = (Math.Atan2(x - N / 2.0 + 0.5, -(y - N / 2.0 + 0.5)) * 180 / Math.PI + 360) % 360;
                    var t = w / 360 * 3;
                    var i = Math.Min(2, (int)t);
                    var a = farben[i];
                    var c = farben[i + 1];
                    var u = t - i;
                    // Der Komet: am hellsten oben (0 Grad), nach hinten lang auslaufend.
                    var abstand = (360 - w) % 360;
                    var licht = 1 - komet + komet * Math.Pow(Math.Max(0, 1 - abstand / 240), 2);
                    var o = (y * N + x) * 4;
                    px[o] = (byte)Math.Round((a.B + (c.B - a.B) * u) * licht);
                    px[o + 1] = (byte)Math.Round((a.G + (c.G - a.G) * u) * licht);
                    px[o + 2] = (byte)Math.Round((a.R + (c.R - a.R) * u) * licht);
                    px[o + 3] = 255;
                }
            var bild = BitmapSource.Create(N, N, 96, 96, PixelFormats.Bgra32, null, px, N * 4);
            bild.Freeze();
            if (Bilder.Count > 16) Bilder.Clear();
            Bilder[schluessel] = bild;
            return bild;
        }
    }
}
