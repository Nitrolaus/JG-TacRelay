// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Glasdesign;

/// <summary>
/// Der Fortschritt (Stil Glas.Fortschritt, 0.5.0), als ein Element auf ganzen Gerätepixeln gezeichnet. Drei Macharten
/// (<see cref="Fortschrittart"/>, am ProgressBar über <see cref="Glas.FortschrittartProperty"/>):
/// <list type="bullet">
/// <item>Glasfuellung (Vorgabe): eine eingelassene Rinne mit Haarlinie von einem Gerätepixel, darin die Füllung als
/// Glaskörper im Akzent mit den Spiegelungen des Glanzprofils. Die Füllung hält oben, unten, am Anfang und am Ende
/// denselben Abstand zur Rinne (Haarlinie plus <see cref="Abstand"/>), wie der Knauf im Schalter.</item>
/// <item>Lichtlinie: eine schmale dunkle Spur, darauf eine leuchtende Linie mit weichem Schein und hellem Kopf, senkrecht
/// mittig (Dicke mit der Parität der Höhe).</item>
/// <item>Segmente: eine Reihe kleiner Glaskörper gleicher Breite mit gleichen Fugen, die Reihe mittig; leuchtend, was
/// erreicht ist, das angefangene Segment anteilig, die übrigen als eingelassene Mulden.</item>
/// </list>
/// Unbestimmt (<see cref="Unbestimmt"/>) läuft ein Läufer von links nach rechts, nur auf dem Bildschirm, nur sichtbar und
/// nur, wenn Windows Animationen erlaubt (SystemParameters.ClientAreaAnimation), wie beim Glanzwischer; schaltet jemand
/// "Animationen anzeigen" im Lauf um, folgt er (<see cref="Animationen"/>). Sonst, gesperrt und in der Zustandsvorschau
/// ruht er absolut mittig (<see cref="Ruhe"/>, <see cref="Ruhelage"/>), damit man sieht, dass etwas arbeitet; bei der
/// Lichtlinie ruhend als Licht, das nach beiden Seiten gleich ausläuft. Gesperrt ist alles flach: Rinne und Füllung in den
/// gesperrten Tönen, ohne Spiegelungen, ohne Schein.
/// </summary>
public sealed class Fortschrittsbahn : FrameworkElement
{
    /// <summary>Wo der Läufer ruht (Vorschau, Speicher, keine Animationen): in der Mitte.</summary>
    public const double Ruhe = 0.5;

    public static readonly DependencyProperty MinimumProperty = Zahl(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Zahl(nameof(Maximum), 100);
    public static readonly DependencyProperty WertProperty = Zahl(nameof(Wert), 0);

    public static readonly DependencyProperty UnbestimmtProperty = DependencyProperty.Register(
        nameof(Unbestimmt), typeof(bool), typeof(Fortschrittsbahn),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Fortschrittsbahn)d).Steuern()));

    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Fortschrittart), typeof(Fortschrittsbahn), new FrameworkPropertyMetadata(Fortschrittart.Glasfuellung, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Die Zustandsvorschau des Steuerelements (Glas.ZustandVorschau): jede außer Keine lässt den Läufer ruhen.</summary>
    public static readonly DependencyProperty VorschauProperty = DependencyProperty.Register(
        nameof(Vorschau), typeof(Zustandvorschau), typeof(Fortschrittsbahn),
        new FrameworkPropertyMetadata(Zustandvorschau.Keine, (d, _) => ((Fortschrittsbahn)d).Steuern()));

    /// <summary>Wo der unbestimmte Läufer steht: 0 ganz vor der Spur (links), 1 ganz dahinter (rechts).</summary>
    public static readonly DependencyProperty LaufProperty = DependencyProperty.Register(
        nameof(Lauf), typeof(double), typeof(Fortschrittsbahn), new FrameworkPropertyMetadata(Ruhe, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AbstandProperty = Zahl(nameof(Abstand), 1);
    public static readonly DependencyProperty LinieProperty = Zahl(nameof(Linie), 2);
    public static readonly DependencyProperty ScheinProperty = Zahl(nameof(Schein), 4);
    public static readonly DependencyProperty SegmentProperty = Zahl(nameof(Segment), 12);
    public static readonly DependencyProperty FugeProperty = Zahl(nameof(Fuge), 2);
    public static readonly DependencyProperty LaengeProperty = Zahl(nameof(Laenge), 0.3);

    public static readonly DependencyProperty UmlaufProperty = DependencyProperty.Register(
        nameof(Umlauf), typeof(double), typeof(Fortschrittsbahn), new FrameworkPropertyMetadata(1600d, (d, _) => ((Fortschrittsbahn)d).Neustart()));

    public static readonly DependencyProperty SpurProperty = Pinsel(nameof(Spur));
    public static readonly DependencyProperty SpurKanteProperty = Pinsel(nameof(SpurKante));
    public static readonly DependencyProperty SpurGesperrtProperty = Pinsel(nameof(SpurGesperrt));
    public static readonly DependencyProperty FuellungProperty = Pinsel(nameof(Fuellung));
    public static readonly DependencyProperty FuellungKanteProperty = Pinsel(nameof(FuellungKante));
    public static readonly DependencyProperty FuellungGesperrtProperty = Pinsel(nameof(FuellungGesperrt));
    public static readonly DependencyProperty KanteGesperrtProperty = Pinsel(nameof(KanteGesperrt));
    public static readonly DependencyProperty LichtfleckProperty = Pinsel(nameof(Lichtfleck));
    public static readonly DependencyProperty InnenschattenProperty = Pinsel(nameof(Innenschatten));
    public static readonly DependencyProperty SpiegelFarbeProperty = Farbe(nameof(SpiegelFarbe), Colors.White);
    public static readonly DependencyProperty LichtFarbeProperty = Farbe(nameof(LichtFarbe), Color.FromRgb(0x5F, 0xE0, 0xEA));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Fortschrittsbahn),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Zahl(string name, double vorgabe) => DependencyProperty.Register(
        name, typeof(double), typeof(Fortschrittsbahn), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Fortschrittsbahn), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Farbe(string name, Color vorgabe) => DependencyProperty.Register(
        name, typeof(Color), typeof(Fortschrittsbahn), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Glanzbilder der Füllung: voll und für das anteilig leuchtende Segment, je Größe gemerkt.</summary>
    private Glanzbild? _bild, _bildTeil;

    private bool _laeuft;
    private bool _hoert;

    static Fortschrittsbahn()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Fortschrittsbahn), new UIPropertyMetadata(true, (d, _) =>
        {
            var f = (Fortschrittsbahn)d;
            f.Steuern();
            f.InvalidateVisual();
        }));
    }

    public Fortschrittsbahn()
    {
        Focusable = false;
        SetResourceReference(AbstandProperty, Schluessel.MassFortschrittAbstand);
        SetResourceReference(LinieProperty, Schluessel.MassFortschrittLinie);
        SetResourceReference(ScheinProperty, Schluessel.MassFortschrittSchein);
        SetResourceReference(SegmentProperty, Schluessel.MassFortschrittSegment);
        SetResourceReference(FugeProperty, Schluessel.MassFortschrittFuge);
        SetResourceReference(LaengeProperty, Schluessel.MassFortschrittLaenge);
        SetResourceReference(UmlaufProperty, Schluessel.MassFortschrittUmlauf);
        SetResourceReference(SpurProperty, Schluessel.FortschrittSpur);
        SetResourceReference(SpurKanteProperty, Schluessel.FortschrittSpurKante);
        SetResourceReference(SpurGesperrtProperty, Schluessel.FortschrittSpurGesperrt);
        SetResourceReference(FuellungProperty, Schluessel.FortschrittFuellung);
        SetResourceReference(FuellungKanteProperty, Schluessel.FortschrittFuellungKante);
        SetResourceReference(FuellungGesperrtProperty, Schluessel.FortschrittFuellungGesperrt);
        SetResourceReference(KanteGesperrtProperty, Schluessel.KanteGesperrt);
        SetResourceReference(LichtfleckProperty, Schluessel.LichtfleckHaupt);
        SetResourceReference(InnenschattenProperty, Schluessel.Innenschatten);
        SetResourceReference(SpiegelFarbeProperty, Schluessel.SpiegelHauptFarbe);
        SetResourceReference(LichtFarbeProperty, Schluessel.FortschrittLichtFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
        Loaded += (_, _) => Steuern();
        Unloaded += (_, _) => Anhalten();
        IsVisibleChanged += (_, _) => Steuern();
    }

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Wert { get => (double)GetValue(WertProperty); set => SetValue(WertProperty, value); }
    public bool Unbestimmt { get => (bool)GetValue(UnbestimmtProperty); set => SetValue(UnbestimmtProperty, value); }
    public Fortschrittart Art { get => (Fortschrittart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public Zustandvorschau Vorschau { get => (Zustandvorschau)GetValue(VorschauProperty); set => SetValue(VorschauProperty, value); }
    public double Lauf { get => (double)GetValue(LaufProperty); set => SetValue(LaufProperty, value); }

    /// <summary>Luft zwischen der Haarlinie der Rinne und der Glasfüllung (DIP, auf ganze Pixel gerundet, 0 erlaubt).</summary>
    public double Abstand { get => (double)GetValue(AbstandProperty); set => SetValue(AbstandProperty, value); }

    /// <summary>Dicke der Lichtlinie (DIP, mindestens ein Pixel, mit der Parität der Höhe).</summary>
    public double Linie { get => (double)GetValue(LinieProperty); set => SetValue(LinieProperty, value); }

    /// <summary>Wie weit der Schein der Lichtlinie reicht (DIP), höchstens bis an den Rand des Elements.</summary>
    public double Schein { get => (double)GetValue(ScheinProperty); set => SetValue(ScheinProperty, value); }

    /// <summary>Breite eines Segments (DIP).</summary>
    public double Segment { get => (double)GetValue(SegmentProperty); set => SetValue(SegmentProperty, value); }

    /// <summary>Fuge zwischen zwei Segmenten (DIP).</summary>
    public double Fuge { get => (double)GetValue(FugeProperty); set => SetValue(FugeProperty, value); }

    /// <summary>Länge des unbestimmten Läufers als Anteil der Spur (bei Segmenten: der Segmente).</summary>
    public double Laenge { get => (double)GetValue(LaengeProperty); set => SetValue(LaengeProperty, value); }

    /// <summary>Dauer eines Laufs von links nach rechts (Millisekunden); 0 heißt: er ruht immer.</summary>
    public double Umlauf { get => (double)GetValue(UmlaufProperty); set => SetValue(UmlaufProperty, value); }

    public Brush? Spur { get => (Brush?)GetValue(SpurProperty); set => SetValue(SpurProperty, value); }
    public Brush? SpurKante { get => (Brush?)GetValue(SpurKanteProperty); set => SetValue(SpurKanteProperty, value); }
    public Brush? SpurGesperrt { get => (Brush?)GetValue(SpurGesperrtProperty); set => SetValue(SpurGesperrtProperty, value); }
    public Brush? Fuellung { get => (Brush?)GetValue(FuellungProperty); set => SetValue(FuellungProperty, value); }
    public Brush? FuellungKante { get => (Brush?)GetValue(FuellungKanteProperty); set => SetValue(FuellungKanteProperty, value); }
    public Brush? FuellungGesperrt { get => (Brush?)GetValue(FuellungGesperrtProperty); set => SetValue(FuellungGesperrtProperty, value); }
    public Brush? KanteGesperrt { get => (Brush?)GetValue(KanteGesperrtProperty); set => SetValue(KanteGesperrtProperty, value); }
    public Brush? Lichtfleck { get => (Brush?)GetValue(LichtfleckProperty); set => SetValue(LichtfleckProperty, value); }
    public Brush? Innenschatten { get => (Brush?)GetValue(InnenschattenProperty); set => SetValue(InnenschattenProperty, value); }
    public Color SpiegelFarbe { get => (Color)GetValue(SpiegelFarbeProperty); set => SetValue(SpiegelFarbeProperty, value); }
    public Color LichtFarbe { get => (Color)GetValue(LichtFarbeProperty); set => SetValue(LichtFarbeProperty, value); }

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    /// <summary>Der Stand als Anteil von 0 bis 1 (Wert zwischen Minimum und Maximum).</summary>
    public double Anteil
    {
        get
        {
            var spanne = Maximum - Minimum;
            if (!(spanne > 0) || double.IsNaN(Wert)) return 0;
            return Math.Clamp((Wert - Minimum) / spanne, 0, 1);
        }
    }

    /// <summary>Was der unbestimmte Läufer tut, entschieden von <see cref="Weg"/>.</summary>
    internal enum Laufweg
    {
        /// <summary>Bestimmt: kein Läufer, die Füllung zeigt den Wert.</summary>
        Aus,

        /// <summary>Der Läufer ruht in der Mitte.</summary>
        Ruhend,

        /// <summary>Der Läufer läuft, als Animation.</summary>
        Laufen,
    }

    /// <summary>
    /// Die Regel, ohne Element prüfbar: Bestimmt gibt es keinen Läufer. Unbestimmt läuft er nur auf dem Bildschirm, nur
    /// bedienbar, ohne Zustandsvorschau und nur, wenn Windows Animationen erlaubt; sonst ruht er in der Mitte.
    /// </summary>
    internal static Laufweg Weg(bool unbestimmt, bool vorschau, bool bedienbar, bool bildschirm, bool animationen)
    {
        if (!unbestimmt) return Laufweg.Aus;
        if (vorschau || !bedienbar || !bildschirm || !animationen) return Laufweg.Ruhend;
        return Laufweg.Laufen;
    }

    /// <summary>
    /// Was der Läufer jetzt tun soll: <see cref="Weg"/> mit dem Zustand des Elements (Unbestimmt, Zustandsvorschau,
    /// bedienbar) und seiner Umgebung (<see cref="Animationen"/>: auf dem Bildschirm, Animationen erlaubt). Steuern fragt nur hier.
    /// </summary>
    internal Laufweg Jetzt => Weg(Unbestimmt, Vorschau != Zustandvorschau.Keine, IsEnabled, Animationen.AufDemBildschirm(this), Animationen.Erlaubt);

    /// <summary>Ob das Element gerade auf das Umschalten von "Animationen anzeigen" hört.</summary>
    internal bool Hoert => _hoert;

    /// <summary>Ob der unbestimmte Läufer ruht: keine Animation, und er steht auf <see cref="Ruhe"/>.</summary>
    private bool Ruhend => !_laeuft && Lauf == Ruhe;

    private void Neustart()
    {
        _laeuft = false;
        Steuern();
    }

    private void Steuern()
    {
        // Auf "Animationen anzeigen" hören, solange ein unbestimmter Läufer auf dem Bildschirm steht; nur dann kann das
        // Umschalten etwas ändern, und nur so lange hält Windows (über SystemParameters) einen Verweis auf das Element.
        if (Unbestimmt && Animationen.AufDemBildschirm(this)) Anmelden();
        else Abmelden();
        if (Jetzt == Laufweg.Laufen && Umlauf > 0)
        {
            if (_laeuft) return;
            var lauf = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Umlauf))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            lauf.Freeze();
            BeginAnimation(LaufProperty, lauf);
            _laeuft = true;
            return;
        }
        Ruhen();
    }

    private void Ruhen()
    {
        _laeuft = false;
        BeginAnimation(LaufProperty, null);
        SetCurrentValue(LaufProperty, Ruhe);
    }

    /// <summary>Aus dem Baum genommen: nicht mehr zuhören, der Läufer ruht.</summary>
    private void Anhalten()
    {
        Abmelden();
        Ruhen();
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
        if (Dispatcher.CheckAccess()) Steuern();
        else Dispatcher.BeginInvoke(new Action(Steuern));
    }

    /// <summary>Einzug der Glasfüllung in Pixeln: Haarlinie der Rinne plus Abstand, oben, unten, am Anfang und am Ende gleich.</summary>
    internal static int Einzug(double abstand, double skala) => Glasgeometrie.Haarlinie + (int)Math.Max(0, Glasgeometrie.Raster(abstand, skala));

    /// <summary>Breite der Füllung in Pixeln auf einem Weg von <paramref name="weg"/> Pixeln.</summary>
    internal static int Breite(double anteil, int weg) => (int)Math.Round(Math.Clamp(anteil, 0, 1) * weg);

    /// <summary>
    /// Der laufende Läufer auf einem Weg von <paramref name="weg"/> Pixeln: Länge <paramref name="laenge"/> mal der Weg,
    /// bei <paramref name="lauf"/> 0 ganz vor dem Weg, bei 1 ganz dahinter, an den Enden gekürzt statt abgeschnitten (so
    /// bleibt er eine runde Kapsel). Ruhend gilt <see cref="Ruhelage"/>.
    /// </summary>
    internal static (int Von, int Bis) Laeufer(int weg, double lauf, double laenge)
    {
        if (weg < 1) return (0, 0);
        var l = Math.Clamp((int)Math.Round(weg * Math.Clamp(laenge, 0.02, 1)), 1, weg);
        var start = (int)Math.Round(Math.Clamp(lauf, 0, 1) * (weg + l)) - l;
        return (Math.Max(0, start), Math.Min(weg, start + l));
    }

    /// <summary>
    /// Der ruhende Läufer auf einem Weg von <paramref name="weg"/> Stellen (Pixel, bei Segmenten Segmente): absolut mittig,
    /// links so viel frei wie rechts. Dafür bekommt seine Länge die Parität des Wegs (<see cref="Mitlaenge"/>), ist also
    /// höchstens eine Stelle länger oder kürzer als im Lauf.
    /// </summary>
    internal static (int Von, int Bis) Ruhelage(int weg, double laenge)
    {
        if (weg < 1) return (0, 0);
        var l = Mitlaenge(weg, weg * Math.Clamp(laenge, 0.02, 1));
        var von = (weg - l) / 2;
        return (von, von + l);
    }

    /// <summary>Eine Länge zwischen 1 und <paramref name="weg"/> mit der Parität des Wegs, so nah wie möglich an <paramref name="genau"/>.</summary>
    internal static int Mitlaenge(int weg, double genau)
    {
        var l = Math.Clamp((int)Math.Round(genau), 1, weg);
        if ((weg - l) % 2 == 0) return l;
        return l > 1 && genau < l ? l - 1 : l + 1;
    }

    /// <summary>Lichtlinie: Lage und Dicke der Linie (Pixel, senkrecht mittig, gleiche Parität wie die Höhe) und Reichweite des Scheins.</summary>
    internal static (int Y, int Dicke, int Schein) Linienmass(int hoehe, double linie, double schein, double skala)
    {
        var t = Math.Min(hoehe, (int)Glasgeometrie.Linie(linie, skala));
        if ((hoehe - t) % 2 != 0) t = t < hoehe ? t + 1 : t - 1;
        var y = (hoehe - t) / 2;
        return (y, t, Math.Clamp((int)Glasgeometrie.Raster(schein, skala), 0, y));
    }

    /// <summary>
    /// Segmente: Lage des ersten (Pixel), Breite, Fuge und Anzahl. Alle Segmente gleich breit, alle Fugen gleich; was nicht
    /// mehr für ein ganzes Segment reicht, liegt zu gleichen Teilen links und rechts (bei ungerader Zahl rechts ein Pixel mehr).
    /// </summary>
    internal static (int X0, int Breite, int Fuge, int Anzahl) Segmentmass(int breite, double segment, double fuge, double skala)
    {
        var w = (int)Glasgeometrie.Linie(segment, skala);
        var g = (int)Math.Max(0, Glasgeometrie.Raster(fuge, skala));
        if (w >= breite) return (0, breite, g, 1);
        var n = Math.Max(1, (breite + g) / (w + g));
        var rest = breite - (n * w + (n - 1) * g);
        return (rest / 2, w, g, n);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var b = (int)Glasgeometrie.Raster(RenderSize.Width, s);
        var h = (int)Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 4 || h < 4) return;
        var gesperrt = !IsEnabled;
        switch (Art)
        {
            case Fortschrittart.Lichtlinie:
                Lichtlinienbild(drawingContext, b, h, s, gesperrt);
                break;
            case Fortschrittart.Segmente:
                Segmentbild(drawingContext, b, h, s, gesperrt);
                break;
            default:
                Glasfuellungsbild(drawingContext, b, h, s, gesperrt);
                break;
        }
    }

    /// <summary>Eine eingelassene Mulde: Fläche, oben ein Innenschatten, darum die Haarlinie (gesperrt flach).</summary>
    private void Rinne(DrawingContext dc, Glasflaeche f, bool gesperrt)
    {
        var koerper = f.Form(Glasgeometrie.Haarlinie);
        dc.DrawGeometry(gesperrt ? SpurGesperrt : Spur, null, koerper);
        if (!gesperrt && Innenschatten is { } innen) dc.DrawGeometry(Pinselwerk.Oben(innen, 0.6), null, koerper);
        dc.DrawGeometry(gesperrt ? KanteGesperrt : SpurKante, null, f.Ring(0, Glasgeometrie.Haarlinie));
    }

    /// <summary>
    /// Ein Glaskörper der Füllung an der Pixelstelle (<paramref name="x"/>, <paramref name="y"/>): Körper, Haarlinie und
    /// Spiegelungen wie beim Glaskörper der Hauptaktion; mit <paramref name="staerke"/> unter 1 entsprechend schwächer
    /// (das angefangene Segment). Gezeichnet um ganze Pixel verschoben, damit das Glanzbild je Größe nur einmal entsteht.
    /// </summary>
    private Glanzbild Kapsel(DrawingContext dc, Glanzbild? gemerkt, int x, int y, int breite, int hoehe, double radius, double s, bool gesperrt, double staerke = 1)
    {
        var f = new Glasflaeche(0, 0, breite, hoehe, radius, s);
        var bild = Glanzbild.Holen(gemerkt, f, Glanzprofil, SpiegelFarbe, staerke, Lichtfleck, Pinselwerk.Ton(Fuellung, SpiegelFarbe));
        var koerper = gesperrt ? FuellungGesperrt : Fuellung;
        var kante = gesperrt ? KanteGesperrt : FuellungKante;
        if (staerke < 1)
        {
            if (koerper is not null) koerper = Pinselwerk.Gedaempft(koerper, staerke);
            if (kante is not null) kante = Pinselwerk.Gedaempft(kante, staerke);
        }
        var verschub = new TranslateTransform(x / s, y / s);
        verschub.Freeze();
        dc.PushTransform(verschub);
        dc.DrawGeometry(koerper, null, bild.Koerper);
        dc.DrawGeometry(kante, null, bild.Haar);
        if (!gesperrt) bild.Zeichnen(dc);
        dc.Pop();
        return bild;
    }

    private void Glasfuellungsbild(DrawingContext dc, int b, int h, double s, bool gesperrt)
    {
        Rinne(dc, new Glasflaeche(0, 0, b, h, h / 2.0, s), gesperrt);
        var a = Einzug(Abstand, s);
        var (hoehe, weg) = (h - 2 * a, b - 2 * a);
        if (hoehe < 2 || weg < 1) return;
        var (von, bis) = !Unbestimmt ? (0, Breite(Anteil, weg)) : Ruhend ? Ruhelage(weg, Laenge) : Laeufer(weg, Lauf, Laenge);
        var breite = bis - von;
        if (breite < 1) return;
        _bild = Kapsel(dc, _bild, a + von, a, breite, hoehe, Math.Min(breite, hoehe) / 2.0, s, gesperrt);
    }

    private void Lichtlinienbild(DrawingContext dc, int b, int h, double s, bool gesperrt)
    {
        var (y, t, schein) = Linienmass(h, Linie, Schein, s);
        var weg = b - 2 * schein;
        if (weg < 2 || t < 1) return;
        dc.DrawGeometry(gesperrt ? SpurGesperrt : Spur, null, new Glasflaeche(schein, y, weg, t, t / 2.0, s).Form(0));
        var ruhend = Unbestimmt && Ruhend;
        var (von, bis) = !Unbestimmt ? (0, Breite(Anteil, weg)) : ruhend ? Ruhelage(weg, Laenge) : Laeufer(weg, Lauf, Laenge);
        var breite = bis - von;
        if (breite < 1) return;
        var linie = new Glasflaeche(schein + von, y, breite, t, Math.Min(breite, t) / 2.0, s);
        if (gesperrt)
        {
            dc.DrawGeometry(FuellungGesperrt, null, linie.Form(0));
            return;
        }
        var licht = LichtFarbe;
        var kern = Farbton.Mischen(licht, Color.FromArgb(licht.A, 255, 255, 255), 0.55);
        var aus = Pinselwerk.Alpha(licht, 0);
        // Schein: Ringe von je einem Gerätepixel um die Linie, nach außen schwächer wie der Leuchtsaum. Beim laufenden Kometen
        // läuft er mit dem Schweif nach links aus, sonst läge um den durchsichtigen Schweif ein hohler Umriss; ruhend läuft er
        // nach beiden Seiten gleich aus (ein Licht in der Mitte, links wie rechts).
        for (var i = 0; i < schein; i++)
        {
            var deckung = 0.5 * Math.Pow(1 - (i + 0.5) / schein, 1.5) * licht.A / 255.0;
            if (deckung * 255 < 0.5) continue;
            var an = Pinselwerk.Alpha(licht, deckung);
            Brush ring = ruhend ? Pinselwerk.Waagerecht((aus, 0), (an, 0.5), (aus, 1))
                : Unbestimmt ? Pinselwerk.Waagerecht((aus, 0), (an, 0.6), (an, 1))
                : Pinselwerk.Flaeche(an);
            dc.DrawGeometry(ring, null, linie.Ring(-(i + 1), -i));
        }
        // Die Linie selbst: bestimmt in der Mitte heller, laufend ein Komet (Schweif links durchsichtig, Kopf hell), ruhend
        // in der Mitte am hellsten und nach beiden Enden durchsichtig.
        Brush koerper = ruhend ? Pinselwerk.Waagerecht((aus, 0), (licht, 0.3), (kern, 0.5), (licht, 0.7), (aus, 1))
            : Unbestimmt ? Pinselwerk.Waagerecht((aus, 0), (licht, 0.6), (kern, 1))
            : Pinselwerk.Senkrecht((licht, 0), (kern, 0.5), (licht, 1));
        dc.DrawGeometry(koerper, null, linie.Form(0));
        // Der Kopf: ein weicher heller Schein am Ende der Linie (ruhend in ihrer Mitte), nicht über das Element hinaus.
        var kopf = linie.Dip(ruhend ? breite / 2.0 : breite - t / 2.0, t / 2.0);
        var (rx, ry) = ((schein + t) / s, (schein + t / 2.0) / s);
        var feld = new RectangleGeometry(new Rect(new Point(Math.Max(0, kopf.X - rx), 0), new Point(Math.Min(b / s, kopf.X + rx), h / s)));
        feld.Freeze();
        dc.DrawGeometry(Pinselwerk.Strahl(kopf, rx, ry, Pinselwerk.Alpha(kern, 0.55)), null, feld);
    }

    private void Segmentbild(DrawingContext dc, int b, int h, double s, bool gesperrt)
    {
        var (x0, w, g, n) = Segmentmass(b, Segment, Fuge, s);
        var r = Glasgeometrie.Raster(Math.Min(w, h) * Glasgeometrie.Rundungsanteil, 1);
        int voll = 0, zugVon = 0, zugBis = 0;
        double teil = 0;
        if (Unbestimmt && Ruhend)
        {
            // Ruhend absolut mittig: so viele Segmente frei links wie rechts (die Zahl der leuchtenden mit der Parität von n).
            (zugVon, zugBis) = Ruhelage(n, Laenge);
        }
        else if (Unbestimmt)
        {
            var m = Math.Clamp((int)Math.Round(Laenge * n), 1, n);
            zugVon = (int)Math.Round(Math.Clamp(Lauf, 0, 1) * (n + m)) - m;
            zugBis = zugVon + m;
        }
        else
        {
            var stand = Anteil * n;
            voll = (int)Math.Floor(stand + 1e-9);
            teil = stand - voll;
        }
        for (var i = 0; i < n; i++)
        {
            var x = x0 + i * (w + g);
            var an = Unbestimmt ? i >= zugVon && i < zugBis : i < voll;
            if (an)
            {
                _bild = Kapsel(dc, _bild, x, 0, w, h, r, s, gesperrt);
                continue;
            }
            Rinne(dc, new Glasflaeche(x, 0, w, h, r, s), gesperrt);
            if (!Unbestimmt && i == voll && teil > 0.02)
                _bildTeil = Kapsel(dc, _bildTeil, x, 0, w, h, r, s, gesperrt, Math.Round(teil, 2));
        }
    }
}
