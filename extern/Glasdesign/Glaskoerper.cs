// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Glasdesign;

/// <summary>
/// Der Glaskörper von Glas 3 (Muster 3b, Sarevocs Wahl vom 02.10.2026), als ein Element gezeichnet: eine Haarlinie von genau
/// einem Gerätepixel (hell, halbdurchsichtig), darin der gewölbte Körper (oben und unten heller als in der Mitte) mit den
/// Spiegelungen aus <see cref="Glanzbild"/>: Kantenspiegel, Randlicht, Schein oben, Durchlicht unten und punktuelle
/// Glanzlichter. Darauf wahlweise ein Zeichen (Haken, Kreuz, Strich). Der weiche Umgebungsschatten ist ein
/// DropShadowEffect am Element, den Kontaktschatten zeichnet <see cref="Kontaktschatten"/> darunter.
/// Alle Kanten liegen auf ganzen Gerätepixeln. Die Pinsel kommen als dynamische Ressourcen aus der Farbstellung
/// (<see cref="Schluessel"/>); welche Körperpinsel, entscheidet <see cref="Art"/>.
/// </summary>
public sealed class Glaskoerper : FrameworkElement
{
    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(Glasform), typeof(Glaskoerper), new FrameworkPropertyMetadata(Glasform.Pille, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eine beliebige Form (Abschnitt 6); gesetzt, gilt sie statt <see cref="Form"/>.</summary>
    public static readonly DependencyProperty UmrissProperty = DependencyProperty.Register(
        nameof(Umriss), typeof(Umriss), typeof(Glaskoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Umriss? Umriss
    {
        get => (Umriss?)GetValue(UmrissProperty);
        set => SetValue(UmrissProperty, value);
    }

    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Glasart), typeof(Glaskoerper), new FrameworkPropertyMetadata(Glasart.Normal, ArtGeaendert));

    public static readonly DependencyProperty ZustandProperty = DependencyProperty.Register(
        nameof(Zustand), typeof(Glaszustand), typeof(Glaskoerper),
        new FrameworkPropertyMetadata(Glaszustand.Normal, FrameworkPropertyMetadataOptions.AffectsRender, SchattenGeaendert));

    public static readonly DependencyProperty ZeichenProperty = DependencyProperty.Register(
        nameof(Zeichen), typeof(Glaszeichen), typeof(Glaskoerper), new FrameworkPropertyMetadata(Glaszeichen.Keins, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Glaskoerper), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits, SchattenGeaendert));

    public static readonly DependencyProperty SchattenFarbeProperty = DependencyProperty.Register(
        nameof(SchattenFarbe), typeof(Color), typeof(Glaskoerper), new FrameworkPropertyMetadata(Colors.Black, SchattenGeaendert));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Glaskoerper),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender, SchattenGeaendert));

    public static readonly DependencyProperty BauplanProperty = DependencyProperty.Register(
        nameof(Bauplan), typeof(Bauplan), typeof(Glaskoerper),
        new FrameworkPropertyMetadata(Glasdesign.Bauplan.Standard, FrameworkPropertyMetadataOptions.AffectsRender, SchattenGeaendert));

    public static readonly DependencyProperty SpiegelFarbeProperty = Farbe(nameof(SpiegelFarbe));
    public static readonly DependencyProperty SpiegelHauptFarbeProperty = Farbe(nameof(SpiegelHauptFarbe));

    public static readonly DependencyProperty KanteProperty = Pinsel(nameof(Kante));
    public static readonly DependencyProperty KanteUeberProperty = Pinsel(nameof(KanteUeber));
    public static readonly DependencyProperty KanteGesperrtProperty = Pinsel(nameof(KanteGesperrt));
    public static readonly DependencyProperty KanteHauptProperty = Pinsel(nameof(KanteHaupt));
    public static readonly DependencyProperty KanteLeuchtenProperty = Pinsel(nameof(KanteLeuchten));

    /// <summary>
    /// Eingerastet (gewählter Reiter): die Haarlinie nimmt den Ton des Leuchtsaums an (<see cref="Schluessel.KanteLeuchten"/>),
    /// solange <see cref="Glanzprofil.LeuchtsaumEingerastet"/> größer als 0 ist.
    /// </summary>
    public static readonly DependencyProperty EingerastetProperty = DependencyProperty.Register(
        nameof(Eingerastet), typeof(bool), typeof(Glaskoerper), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KoerperProperty = Pinsel(nameof(Koerper));
    public static readonly DependencyProperty KoerperUeberProperty = Pinsel(nameof(KoerperUeber));
    public static readonly DependencyProperty KoerperGedruecktProperty = Pinsel(nameof(KoerperGedrueckt));
    public static readonly DependencyProperty KoerperGesperrtProperty = Pinsel(nameof(KoerperGesperrt));
    public static readonly DependencyProperty LichtfleckProperty = Pinsel(nameof(Lichtfleck));
    public static readonly DependencyProperty InnenschattenProperty = Pinsel(nameof(Innenschatten));
    public static readonly DependencyProperty AkzentProperty = Pinsel(nameof(Akzent));
    public static readonly DependencyProperty ZeichenAusProperty = Pinsel(nameof(ZeichenAus));
    public static readonly DependencyProperty ZeichenGesperrtProperty = Pinsel(nameof(ZeichenGesperrt));

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Glaskoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Farbe(string name) => DependencyProperty.Register(
        name, typeof(Color), typeof(Glaskoerper), new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private Glanzbild? _bild;
    private Druckleuchten? _druck;

    /// <summary>Das zuletzt gezeichnete Glanzbild (die Tests prüfen daran, was Überfahren und Drücken neu bauen).</summary>
    internal Glanzbild? Bild => _bild;

    /// <summary>Die Form, auf der Treffer zählen (Glas samt Haarlinie), aus dem letzten Zeichnen; null, solange nichts gezeichnet ist.</summary>
    private Geometry? _treffer;

    public Glaskoerper()
    {
        SetResourceReference(KanteProperty, Schluessel.Kante);
        SetResourceReference(KanteUeberProperty, Schluessel.KanteUeber);
        SetResourceReference(KanteGesperrtProperty, Schluessel.KanteGesperrt);
        SetResourceReference(KanteHauptProperty, Schluessel.KanteHaupt);
        SetResourceReference(KanteLeuchtenProperty, Schluessel.KanteLeuchten);
        SetResourceReference(InnenschattenProperty, Schluessel.Innenschatten);
        SetResourceReference(AkzentProperty, Schluessel.Akzent);
        SetResourceReference(ZeichenAusProperty, Schluessel.ZeichenAus);
        SetResourceReference(ZeichenGesperrtProperty, Schluessel.ZeichenGesperrt);
        SetResourceReference(SchattenFarbeProperty, Schluessel.SchattenFarbe);
        SetResourceReference(SpiegelFarbeProperty, Schluessel.SpiegelFarbe);
        SetResourceReference(SpiegelHauptFarbeProperty, Schluessel.SpiegelHauptFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
        SetResourceReference(BauplanProperty, Schluessel.Bauplan);
        Verknuepfen(Glasart.Normal);
    }

    /// <summary>Der Bauplan der Farbstellung (Schatten, Zeichengrößen); fehlt er, gilt <see cref="Glasdesign.Bauplan.Standard"/>.</summary>
    public Bauplan Bauplan
    {
        get => GetValue(BauplanProperty) as Bauplan ?? Glasdesign.Bauplan.Standard;
        set => SetValue(BauplanProperty, value);
    }

    /// <summary>Das Glanzprofil der Farbstellung; fehlt es, gilt <see cref="Glasdesign.Glanzprofil.Standard"/>.</summary>
    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    public Glasform Form { get => (Glasform)GetValue(FormProperty); set => SetValue(FormProperty, value); }
    public Glasart Art { get => (Glasart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public Glaszustand Zustand { get => (Glaszustand)GetValue(ZustandProperty); set => SetValue(ZustandProperty, value); }
    public Glaszeichen Zeichen { get => (Glaszeichen)GetValue(ZeichenProperty); set => SetValue(ZeichenProperty, value); }
    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }
    public Color SchattenFarbe { get => (Color)GetValue(SchattenFarbeProperty); set => SetValue(SchattenFarbeProperty, value); }

    /// <summary>Farbton der Spiegelungen auf normalem Glas (weiß).</summary>
    public Color SpiegelFarbe { get => (Color)GetValue(SpiegelFarbeProperty); set => SetValue(SpiegelFarbeProperty, value); }

    /// <summary>Farbton der Spiegelungen auf dem Akzentglas der Hauptaktion.</summary>
    public Color SpiegelHauptFarbe { get => (Color)GetValue(SpiegelHauptFarbeProperty); set => SetValue(SpiegelHauptFarbeProperty, value); }

    public Brush? Kante { get => (Brush?)GetValue(KanteProperty); set => SetValue(KanteProperty, value); }
    public Brush? KanteUeber { get => (Brush?)GetValue(KanteUeberProperty); set => SetValue(KanteUeberProperty, value); }
    public Brush? KanteGesperrt { get => (Brush?)GetValue(KanteGesperrtProperty); set => SetValue(KanteGesperrtProperty, value); }
    public Brush? KanteHaupt { get => (Brush?)GetValue(KanteHauptProperty); set => SetValue(KanteHauptProperty, value); }
    public Brush? KanteLeuchten { get => (Brush?)GetValue(KanteLeuchtenProperty); set => SetValue(KanteLeuchtenProperty, value); }
    public bool Eingerastet { get => (bool)GetValue(EingerastetProperty); set => SetValue(EingerastetProperty, value); }
    public Brush? Koerper { get => (Brush?)GetValue(KoerperProperty); set => SetValue(KoerperProperty, value); }
    public Brush? KoerperUeber { get => (Brush?)GetValue(KoerperUeberProperty); set => SetValue(KoerperUeberProperty, value); }
    public Brush? KoerperGedrueckt { get => (Brush?)GetValue(KoerperGedruecktProperty); set => SetValue(KoerperGedruecktProperty, value); }
    public Brush? KoerperGesperrt { get => (Brush?)GetValue(KoerperGesperrtProperty); set => SetValue(KoerperGesperrtProperty, value); }
    public Brush? Lichtfleck { get => (Brush?)GetValue(LichtfleckProperty); set => SetValue(LichtfleckProperty, value); }
    public Brush? Innenschatten { get => (Brush?)GetValue(InnenschattenProperty); set => SetValue(InnenschattenProperty, value); }
    public Brush? Akzent { get => (Brush?)GetValue(AkzentProperty); set => SetValue(AkzentProperty, value); }
    public Brush? ZeichenAus { get => (Brush?)GetValue(ZeichenAusProperty); set => SetValue(ZeichenAusProperty, value); }
    public Brush? ZeichenGesperrt { get => (Brush?)GetValue(ZeichenGesperrtProperty); set => SetValue(ZeichenGesperrtProperty, value); }

    private static void ArtGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Glaskoerper)d).Verknuepfen((Glasart)e.NewValue);

    /// <summary>Körper und Durchlicht je Art an die Schlüssel der Farbstellung binden (dynamisch, folgt dem Umschalten).</summary>
    private void Verknuepfen(Glasart art)
    {
        var (normal, ueber, gedrueckt, gesperrt, licht) = art switch
        {
            Glasart.Haupt => (Schluessel.Haupt, Schluessel.HauptUeber, Schluessel.HauptGedrueckt, Schluessel.HauptGesperrt, Schluessel.LichtfleckHaupt),
            Glasart.Auswahl => (Schluessel.Auswahl, Schluessel.AuswahlUeber, Schluessel.AuswahlGedrueckt, Schluessel.AuswahlGesperrt, Schluessel.LichtfleckAuswahl),
            Glasart.Dunkel => (Schluessel.Dunkel, Schluessel.DunkelUeber, Schluessel.DunkelGedrueckt, Schluessel.DunkelGesperrt, Schluessel.LichtfleckDunkel),
            _ => (Schluessel.Koerper, Schluessel.KoerperUeber, Schluessel.KoerperGedrueckt, Schluessel.KoerperGesperrt, Schluessel.Lichtfleck),
        };
        SetResourceReference(KoerperProperty, normal);
        SetResourceReference(KoerperUeberProperty, ueber);
        SetResourceReference(KoerperGedruecktProperty, gedrueckt);
        SetResourceReference(KoerperGesperrtProperty, gesperrt);
        SetResourceReference(LichtfleckProperty, licht);
    }

    private static void SchattenGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Glaskoerper)d).EffektSetzen();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        EffektSetzen();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    /// <summary>
    /// Weicher Umgebungsschatten von Glas 3 (Tiefe 7 %, Unschärfe 50 % der Höhe, Deckung <see cref="Glanzprofil.Schatten"/>).
    /// Gedrückt kleiner, gesperrt und bei Schatten Einfach oder Aus keiner.
    /// </summary>
    private void EffektSetzen()
    {
        var h = RenderSize.Height;
        if (Schatten != Schattenstufe.Voll || Zustand == Glaszustand.Gesperrt || h <= 0)
        {
            if (Effect is not null) Effect = null;
            return;
        }
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        var b = Bauplan;
        var tiefe = Math.Round(h * (gedrueckt ? b.SchattenTiefeGedrueckt : b.SchattenTiefe), 2);
        var unschaerfe = Math.Round(h * (gedrueckt ? b.SchattenUnschaerfeGedrueckt : b.SchattenUnschaerfe), 2);
        var deckung = gedrueckt ? Glanzprofil.Schatten * 0.65 : Glanzprofil.Schatten;
        if (Effect is DropShadowEffect alt && alt.ShadowDepth == tiefe && alt.BlurRadius == unschaerfe && alt.Opacity == deckung && alt.Color == SchattenFarbe) return;
        var neu = new DropShadowEffect
        {
            Color = SchattenFarbe,
            Direction = 270,
            ShadowDepth = tiefe,
            BlurRadius = unschaerfe,
            Opacity = deckung,
            RenderingBias = RenderingBias.Performance,
        };
        neu.Freeze();
        Effect = neu;
    }

    /// <summary>
    /// Treffer zählen nur auf der Form des Glases samt Haarlinie, nicht auf allem, was gezeichnet ist: Die Strahlen des
    /// Sternglanzes reichen ein wenig über die Kante, dort darf die Maus weder überfahren noch klicken. Mit dem
    /// Standardprofil ist beides dieselbe Fläche.
    /// </summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(hitTestParameters);
        return _treffer is { } form && form.FillContains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;
    }

    /// <summary>Treffer über eine Form (etwa ein Auswahlrechteck): wie bei einem Punkt nur gegen die Form des Glases.</summary>
    protected override GeometryHitTestResult? HitTestCore(GeometryHitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(hitTestParameters);
        if (_treffer is not { } form) return null;
        var lage = form.FillContainsWithDetail(hitTestParameters.HitGeometry);
        return lage == IntersectionDetail.Empty ? null : new GeometryHitTestResult(this, lage);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        var s = Glasgeometrie.Skala(this);
        var f = Glasflaeche.Aus(RenderSize, s, Form, Umriss);
        _treffer = null;
        if (f.Breite < 2 || f.Hoehe < 2) return;
        var z = Zustand;
        var gesperrt = z == Glaszustand.Gesperrt;
        var gedrueckt = z == Glaszustand.Gedrueckt;
        var haupt = Art == Glasart.Haupt;
        // Der Ton der Haarlinie gehört zum Leuchten beim Einrasten: Ist es im Glanzprofil aus, bleibt die Haarlinie wie in Glas 3.
        var kante = gesperrt ? KanteGesperrt : Eingerastet && KanteLeuchten is not null && Glanzprofil.LeuchtsaumEingerastet > 0 ? KanteLeuchten
            : z == Glaszustand.Ueberfahren ? KanteUeber : haupt ? KanteHaupt : Kante;
        var koerper = z switch
        {
            Glaszustand.Ueberfahren => KoerperUeber,
            Glaszustand.Gedrueckt => KoerperGedrueckt,
            Glaszustand.Gesperrt => KoerperGesperrt,
            _ => Koerper,
        };
        var p = Glanzprofil;
        var staerke = (z switch { Glaszustand.Ueberfahren => p.Ueberfahren, Glaszustand.Gedrueckt => p.Gedrueckt, _ => 1.0 })
                      * (Art == Glasart.Dunkel ? p.Dunkel : 1.0);
        var ton = haupt ? SpiegelHauptFarbe : SpiegelFarbe;
        var akzent = Pinselwerk.Ton(Akzent, ton);
        _bild = Glanzbild.Holen(_bild, f, p, ton, staerke, Lichtfleck, akzent);
        _treffer = _bild.Flaeche.Form(0);

        dc.DrawGeometry(koerper, null, _bild.Koerper);
        dc.DrawGeometry(kante, null, _bild.Haar);
        if (!gesperrt)
        {
            // Gedrückt sinkt das Glas ein: Innenschatten über das obere Drittel bis knapp unter die Mitte.
            if (gedrueckt && Innenschatten is { } innen) dc.DrawGeometry(Pinselwerk.Oben(innen, 0.45), null, _bild.Koerper);
            // Druckleuchten (Glint Abschnitt 5): Licht sammelt sich gedrückt in der Mitte, statt dass das Glas nur dunkler wird.
            if (gedrueckt && p.DruckleuchtenStaerke > 0)
            {
                _druck = Druckleuchten.Holen(_druck, f, p, ton, akzent);
                dc.DrawGeometry(_druck.Pinsel, null, _druck.Form);
            }
            _bild.Zeichnen(dc);
        }

        if (Zeichen != Glaszeichen.Keins)
        {
            var h1 = Glasgeometrie.Haarlinie;
            var koerperPx = new Rect(h1, h1, f.Breite - 2 * h1, f.Hoehe - 2 * h1);
            var pinsel = gesperrt ? ZeichenGesperrt : Zeichen == Glaszeichen.Kreuz ? ZeichenAus : Akzent;
            var plan = Bauplan;
            var anteil = Zeichen switch
            {
                Glaszeichen.Haken => plan.ZeichenHaken,
                Glaszeichen.Strich or Glaszeichen.Minus => plan.ZeichenStrich,
                _ => plan.ZeichenUebrige,
            };
            Zeichner.Zeichnen(dc, Zeichen, koerperPx, anteil, pinsel, s, strichAnteil: plan.ZeichenStrichstaerke);
        }
    }
}

/// <summary>
/// Das Druckleuchten (Glanzschicht der Glint Abschnitt 5): gedrückt sammelt sich Licht in der Mitte des Glases, ein weicher
/// runder Schein etwas unter der Mitte, so groß wie <see cref="Glanzprofil.DruckleuchtenGroesse"/> mal der Körper, zwischen
/// dem Ton der Spiegelungen und der Akzentfarbe getönt. Form und Pinsel je Fläche, Profil und Farbe einmal gebaut und
/// eingefroren; der <see cref="Glaskoerper"/> zeichnet es nur im Zustand gedrückt, über dem Innenschatten.
/// </summary>
internal sealed class Druckleuchten
{
    private Druckleuchten(Glasflaeche f, Glanzprofil p, Color ton, Color akzent)
    {
        Flaeche = f;
        Profil = p;
        Ton = ton;
        Akzent = akzent;
        var s = f.Skala;
        var g = Math.Clamp(p.DruckleuchtenGroesse, 0.05, 1.5);
        var m = f.Dip(f.Breite / 2, f.Hoehe * 0.56);
        var (rx, ry) = (f.Breite * 0.5 * g / s, f.Hoehe * 0.65 * g / s);
        var farbe = Farbton.Mischen(Color.FromRgb(ton.R, ton.G, ton.B), Color.FromRgb(akzent.R, akzent.G, akzent.B), p.DruckleuchtenToenung);
        var a = p.DruckleuchtenStaerke;
        var ellipse = new EllipseGeometry(m, rx, ry);
        var form = Geometry.Combine(f.Form(Glasgeometrie.Haarlinie), ellipse, GeometryCombineMode.Intersect, null);
        form.Freeze();
        Form = form;
        Pinsel = Pinselwerk.Rund(m, rx, ry, (Pinselwerk.Alpha(farbe, a), 0), (Pinselwerk.Alpha(farbe, a * 0.5), 0.45), (Pinselwerk.Alpha(farbe, 0), 1));
    }

    public Glasflaeche Flaeche { get; }
    public Glanzprofil Profil { get; }
    public Color Ton { get; }
    public Color Akzent { get; }
    public Geometry Form { get; }
    public Brush Pinsel { get; }

    public static Druckleuchten Holen(Druckleuchten? alt, Glasflaeche f, Glanzprofil p, Color ton, Color akzent) =>
        alt is not null && alt.Flaeche == f && alt.Profil == p && alt.Ton == ton && alt.Akzent == akzent ? alt : new Druckleuchten(f, p, ton, akzent);
}
