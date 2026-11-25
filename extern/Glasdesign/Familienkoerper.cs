// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Glasdesign;

/// <summary>
/// Eine Schicht eines Körpers: eine Form mit Pinsel und Stift, oder ein Bild in einem Rechteck (DIP); seit 0.5.0-beta.12
/// wahlweise auf einen <see cref="Ausschnitt"/> (DIP, auf ganzen Gerätepixeln) begrenzt, außerhalb dessen sie nichts beiträgt.
/// </summary>
internal readonly record struct Zeichenschicht(Geometry? Form, Brush? Pinsel, Pen? Stift, ImageSource? Bild, Rect Lage, RectangleGeometry? Ausschnitt = null);

/// <summary>
/// Grundlage der Körper der Stilfamilien 2 (<see cref="Taktilkoerper"/>, <see cref="Glutscheibe"/>,
/// <see cref="Dickglaskoerper"/>, <see cref="Kugelkoerper"/>, <see cref="Horizontglaskoerper"/>,
/// <see cref="Chromrahmenfuellung"/>): Zustand, Form (Rundung als Anteil der kürzeren Seite, Ecke in DIP oder ein
/// <see cref="Glasdesign.Umriss"/>), das <see cref="Stilprofil"/> der Farbstellung und ein Speicher der Schichten. Gebaut
/// wird nur, wenn sich Größe, Skalierung, Zustand, Form, Profil oder ein eigener Wert des Körpers ändert; sonst zeichnet
/// er die eingefrorenen Schichten wieder. Alle Kanten liegen auf ganzen Gerätepixeln; Deckung steckt in den Pinseln, nie in
/// PushOpacity. Nicht treffbar: Treffer gehören dem Steuerelement. Seit 0.5.0-beta.7 wählt der Körper die Schrift für den
/// Inhalt auf ihm (<see cref="Schriftwahl"/>): aus <see cref="Schriftwunsch"/> und den Farben unter dem Inhalt
/// (<see cref="Flaechenfarben"/>) die <see cref="GewaehlteSchrift"/>, mit <see cref="SchriftAnSteuerelement"/> auch für das
/// Steuerelement. Seit 0.5.0-beta.13 (A7) teilen gleiche Körper eines Fadens ihren Bau samt Zwischenbild (<see cref="Bau"/>,
/// <see cref="Teilvorrat{T}"/>): Von drei gleichen Karten rechnet nur die erste.
/// </summary>
public abstract class Familienkoerper : FrameworkElement
{
    public static readonly DependencyProperty ZustandProperty = DependencyProperty.Register(
        nameof(Zustand), typeof(Glaszustand), typeof(Familienkoerper),
        new FrameworkPropertyMetadata(Glaszustand.Normal, FrameworkPropertyMetadataOptions.AffectsRender, Geaendert));

    /// <summary>Eckradius als Anteil der kürzeren Seite: 0 eckig, ab 0,5 Pille oder Kreis. Gilt, solange <see cref="Ecke"/> NaN ist.</summary>
    public static readonly DependencyProperty RundungProperty = DependencyProperty.Register(
        nameof(Rundung), typeof(double), typeof(Familienkoerper), new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eckradius in DIP; NaN (Vorgabe) nimmt <see cref="Rundung"/>.</summary>
    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(double), typeof(Familienkoerper), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eine beliebige Form (Abschnitt 6); gesetzt, gilt sie statt Rundung und Ecke.</summary>
    public static readonly DependencyProperty UmrissProperty = DependencyProperty.Register(
        nameof(Umriss), typeof(Umriss), typeof(Familienkoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProfilProperty = DependencyProperty.Register(
        nameof(Profil), typeof(Stilprofil), typeof(Familienkoerper),
        new FrameworkPropertyMetadata(Stilprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender, Geaendert));

    /// <summary>Schatten wie bei Glas (<see cref="Glas.SchattenProperty"/>, vererbt): Aus nimmt Schatten und Farblicht unter dem Körper weg.</summary>
    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Familienkoerper), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender, Geaendert));

    // ------------------------------------------------------------------ Schriftwahl (0.5.0-beta.7)

    /// <summary>
    /// Die bevorzugte Schrift für den Inhalt auf diesem Körper (in den Vorlagen die Schrift der Farbstellung, meist über
    /// <c>{TemplateBinding gd:Glas.Schriftwunsch}</c>); null: keine Schriftwahl.
    /// </summary>
    public static readonly DependencyProperty SchriftwunschProperty = DependencyProperty.Register(
        nameof(Schriftwunsch), typeof(Brush), typeof(Familienkoerper), new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Steht der Inhalt nicht auf dem Körper selbst, sondern auf einer eigenen Fläche darüber (das Feld im Dickglas-Textfeld),
    /// wählt die Schrift nach dieser Fläche (ihre Farben über denen des Körpers).
    /// </summary>
    public static readonly DependencyProperty InhaltsflaecheProperty = DependencyProperty.Register(
        nameof(Inhaltsflaeche), typeof(Brush), typeof(Familienkoerper), new FrameworkPropertyMetadata(null));

    /// <summary>Die gewählte Schrift auch als <see cref="Glas.KontrastschriftProperty"/> an das Steuerelement geben, dessen Vorlage den Körper trägt.</summary>
    public static readonly DependencyProperty SchriftAnSteuerelementProperty = DependencyProperty.Register(
        nameof(SchriftAnSteuerelement), typeof(bool), typeof(Familienkoerper), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey GewaehlteSchriftKey = DependencyProperty.RegisterReadOnly(
        nameof(GewaehlteSchrift), typeof(Brush), typeof(Familienkoerper), new FrameworkPropertyMetadata(null));

    /// <summary>Die gewählte Schrift (<see cref="Schriftwahl"/>): der Wunsch, wenn er auf der Fläche lesbar ist, sonst die Kontrastschrift.</summary>
    public static readonly DependencyProperty GewaehlteSchriftProperty = GewaehlteSchriftKey.DependencyProperty;

    private static readonly DependencyPropertyKey KoerperflaecheKey = DependencyProperty.RegisterReadOnly(
        nameof(Koerperflaeche), typeof(Brush), typeof(Familienkoerper), new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Die Farben des Körpers unter dem Inhalt im jetzigen Zustand (ohne <see cref="Inhaltsflaeche"/>), als Pinsel: für
    /// Zeichen mit eigenem Wunsch über <see cref="Schriftwahl.Wandler"/>. Null bei Körpern ohne Inhalt.
    /// </summary>
    public static readonly DependencyProperty KoerperflaecheProperty = KoerperflaecheKey.DependencyProperty;

    /// <summary>
    /// Zwischenbild (seit 0.5.0-beta.10, für große Flächen wie Karten): die Schichten samt dem Effekt des Körpers (Schatten,
    /// Unschärfe) einmal in ein Bild in Gerätepixeln rechnen (<see cref="Bildraster"/>) und bei jedem Zeichnen nur dieses Bild
    /// setzen. Im Speicher kostete eine Dickglas-Karte von 480 mal 360 DIP bei 175 Prozent sonst gut 50 ms je Bild, weil das
    /// Software-Zeichnen jede Schicht neu rastert. Vorgabe aus; aus zeichnet der Körper wie bisher.
    /// </summary>
    public static readonly DependencyProperty ZwischenbildProperty = DependencyProperty.Register(
        nameof(Zwischenbild), typeof(bool), typeof(Familienkoerper),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, ZwischenbildGeaendert));

    private object? _schluessel;
    private Bau? _bau;
    private Bau? _bildbau;
    private List<Zeichenschicht>? _ziel;
    private IReadOnlyList<Color> _flaeche = [];
    private Effect? _backeffekt;

    protected Familienkoerper()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SetResourceReference(ProfilProperty, Schluessel.Stilprofil);
    }

    public Glaszustand Zustand { get => (Glaszustand)GetValue(ZustandProperty); set => SetValue(ZustandProperty, value); }
    public double Rundung { get => (double)GetValue(RundungProperty); set => SetValue(RundungProperty, value); }
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }

    public Umriss? Umriss
    {
        get => (Umriss?)GetValue(UmrissProperty);
        set => SetValue(UmrissProperty, value);
    }

    public Stilprofil Profil
    {
        get => GetValue(ProfilProperty) as Stilprofil ?? Stilprofil.Standard;
        set => SetValue(ProfilProperty, value);
    }

    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }

    public bool Zwischenbild { get => (bool)GetValue(ZwischenbildProperty); set => SetValue(ZwischenbildProperty, value); }

    /// <summary>
    /// Wie oft dieser Körper ein neues Zwischenbild bekommen hat, selbst gerechnet oder von einem gleichen Körper geteilt (für
    /// die Tests; wie oft wirklich gerechnet wurde, zählt <see cref="Gerastert"/>).
    /// </summary>
    internal int Zwischenbilder { get; private set; }

    private static void ZwischenbildGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var k = (Familienkoerper)d;
        // Neu bauen: Ein Körper darf seine Schichten für das Zwischenbild anders legen (Dickglas: Wulstringe als Bänder).
        k._schluessel = null;
        if ((bool)e.NewValue)
        {
            k._backeffekt = k.Effect;
            k.Effect = null;
            RenderOptions.SetBitmapScalingMode(k, BitmapScalingMode.NearestNeighbor);
        }
        else
        {
            k.Effect = k._backeffekt;
            k._backeffekt = null;
            k.ClearValue(RenderOptions.BitmapScalingModeProperty);
        }
    }

    /// <summary>Der Effekt des Körpers (mit <see cref="Zwischenbild"/> der eingebackene, sonst <see cref="UIElement.Effect"/>).</summary>
    private protected Effect? Koerpereffekt => Zwischenbild ? _backeffekt : Effect;

    /// <summary>
    /// Setzt den Effekt des Körpers; mit <see cref="Zwischenbild"/> wird er beim nächsten Zeichnen in das Bild eingebacken (er
    /// gehört dann mit seinen Werten zum Schlüssel des Baus, <see cref="Effektwerte"/>).
    /// </summary>
    private protected void KoerpereffektSetzen(Effect? effekt)
    {
        if (!Zwischenbild)
        {
            Effect = effekt;
            return;
        }
        if (ReferenceEquals(_backeffekt, effekt)) return;
        _backeffekt = effekt;
        InvalidateVisual();
    }

    public Brush? Schriftwunsch { get => (Brush?)GetValue(SchriftwunschProperty); set => SetValue(SchriftwunschProperty, value); }
    public Brush? Inhaltsflaeche { get => (Brush?)GetValue(InhaltsflaecheProperty); set => SetValue(InhaltsflaecheProperty, value); }
    public bool SchriftAnSteuerelement { get => (bool)GetValue(SchriftAnSteuerelementProperty); set => SetValue(SchriftAnSteuerelementProperty, value); }
    public Brush? GewaehlteSchrift => (Brush?)GetValue(GewaehlteSchriftProperty);
    public Brush? Koerperflaeche => (Brush?)GetValue(KoerperflaecheProperty);

    /// <summary>
    /// Die Farben unter dem Inhalt im jetzigen Zustand, deckend: an der Stelle, an der Schrift steht (das mittlere Band der
    /// Fläche, 35 bis 65 Prozent der Höhe), wie Bauen sie zeichnet. Leer: der Körper trägt keinen Inhalt.
    /// </summary>
    private protected virtual IReadOnlyList<Color> Flaechenfarben() => [];

    /// <summary>
    /// Die drei Proben des Schriftbands einer senkrechten Farbfunktion: oben, Mitte, unten (Vorgabe 35, 50 und 65 Prozent der
    /// Höhe, so weit reicht die Schrift auf den Knöpfen der Familien).
    /// </summary>
    private protected static IReadOnlyList<Color> Band(Func<double, Color> farbeBei, double oben = 0.35, double unten = 0.65) =>
        [farbeBei(oben), farbeBei((oben + unten) / 2), farbeBei(unten)];

    /// <summary>Die Farbe eines senkrechten Verlaufs bei <paramref name="t"/> (0 oben, 1 unten), linear zwischen den Stopps.</summary>
    private protected static Color Verlaufsfarbe(double t, params (Color Farbe, double Lage)[] stopps)
    {
        if (t <= stopps[0].Lage) return stopps[0].Farbe;
        for (var i = 1; i < stopps.Length; i++)
            if (t <= stopps[i].Lage)
            {
                var (a, b) = (stopps[i - 1], stopps[i]);
                return Farbton.Mischen(a.Farbe, b.Farbe, b.Lage > a.Lage ? (t - a.Lage) / (b.Lage - a.Lage) : 1);
            }
        return stopps[^1].Farbe;
    }

    /// <summary>Die Farben als Pinsel: eine Fläche oder ein senkrechter Verlauf (relativ, gleich verteilte Stopps); null ohne Farben.</summary>
    private static Brush? Bandpinsel(IReadOnlyList<Color> farben)
    {
        if (farben.Count == 0) return null;
        if (farben.Count == 1) return Pinselwerk.Flaeche(farben[0]);
        var p = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (var i = 0; i < farben.Count; i++) p.GradientStops.Add(new GradientStop(farben[i], i / (double)(farben.Count - 1)));
        p.Freeze();
        return p;
    }

    /// <summary>Der dunkle Grund, auf den durchsichtige Familien (Glutglas, Rauchglas) für die Schriftwahl rechnen.</summary>
    private protected static Color Rechengrund { get; } = Color.FromRgb(0x10, 0x11, 0x15);

    private bool _waehlt;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Jede eigene Eigenschaft eines Familienkörpers (Farben, Zustand, Profil, Ebene, Wunsch) kann die Fläche unter dem
        // Inhalt ändern: neu wählen. Ergebnisse (nur lesbare Eigenschaften) lösen nichts aus.
        if (_waehlt || e.Property.ReadOnly) return;
        if (typeof(Familienkoerper).IsAssignableFrom(e.Property.OwnerType) || e.Property == OpacityProperty) Schriftwaehlen();
    }

    /// <summary>Für Körper, die ihre Schrift selbst nach der Fläche wählen (Chromrahmen): nach jeder Änderung neu wählen.</summary>
    private protected virtual void EigeneSchriftwahl()
    {
    }

    /// <summary>Wählt die Schrift neu (<see cref="Schriftwahl.Waehlen"/>) und gibt sie, wenn verlangt, an das Steuerelement.</summary>
    private protected void Schriftwaehlen()
    {
        _waehlt = true;
        try
        {
            var flaeche = Flaechenfarben();
            if (!flaeche.SequenceEqual(_flaeche))
            {
                _flaeche = flaeche;
                SetValue(KoerperflaecheKey, Bandpinsel(flaeche));
            }
            EigeneSchriftwahl();
            if (Schriftwunsch is not { } wunsch)
            {
                if (GewaehlteSchrift is not null) SetValue(GewaehlteSchriftKey, null);
                return;
            }
            if (Inhaltsflaeche is { } feld && Schriftwahl.Farben(feld) is { Count: > 0 } feldfarben)
                flaeche = [.. feldfarben.SelectMany(f => (flaeche.Count == 0 ? [Rechengrund] : flaeche).Select(k => Schriftwahl.Ueber(f, k)))];
            var gewaehlt = Schriftwahl.Waehlen(wunsch, flaeche, Zustand == Glaszustand.Gesperrt);
            if (!ReferenceEquals(GewaehlteSchrift, gewaehlt)) SetValue(GewaehlteSchriftKey, gewaehlt);
            if (SchriftAnSteuerelement) SchriftWeitergeben(gewaehlt);
        }
        finally
        {
            _waehlt = false;
        }
    }

    private object? _abstimmSchluessel;
    private double _abstimmung;

    /// <summary>
    /// Die Abstimmung einer frei gewählten Farbe dieses Körpers (seit 0.5.0-beta.13, <see cref="Schriftwahl.Abstimmung"/>):
    /// <paramref name="roh"/> ist die gewählte Farbe, <paramref name="band"/> liefert die Farben unter der Schrift für die um t
    /// abgestimmte Farbe in einem Zustand; gerechnet
    /// über Ruhe, überfahren und gedrückt (alle Zustände gleich abgestimmt), gemerkt, solange <paramref name="schluessel"/>
    /// gleich bleibt. Ergebnis: t für <see cref="Schriftwahl.Abgestimmt"/>, 0, wenn schon eine Schrift hält.
    /// </summary>
    private protected double Abstimmen(object schluessel, Color roh, Func<double, Glaszustand, IReadOnlyList<Color>> band)
    {
        ArgumentNullException.ThrowIfNull(band);
        if (Equals(schluessel, _abstimmSchluessel)) return _abstimmung;
        _abstimmSchluessel = schluessel;
        _abstimmung = Schriftwahl.Abstimmung(roh, t => [band(t, Glaszustand.Normal), band(t, Glaszustand.Ueberfahren), band(t, Glaszustand.Gedrueckt)]);
        return _abstimmung;
    }

    /// <summary>
    /// Wie oft dieser Körper neue Schichten bekommen hat, selbst gebaut oder von einem gleichen Körper geteilt (für die Tests:
    /// Zeichnen allein baut nichts; wie oft wirklich gebaut wurde, zählt <see cref="Gebaut"/>).
    /// </summary>
    internal int Bauten { get; private set; }

    private static void Geaendert(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Familienkoerper)d).Nachgeaendert();

    /// <summary>Nach einem Wechsel von Zustand oder Profil (etwa für den Schatten als Effekt).</summary>
    private protected virtual void Nachgeaendert()
    {
    }

    /// <summary>Die eigenen Werte des Körpers, die das Bild bestimmen (ein Tupel; gleich heißt: nichts neu bauen).</summary>
    private protected abstract object Teilschluessel();

    /// <summary>
    /// Die Werte des Stilprofils, die <see cref="Bauen"/> liest (für den Schlüssel des Baus); Vorgabe: das ganze Profil. Ein Körper,
    /// der nur wenige Werte liest, nennt nur diese (seit 0.5.0-beta.13, A7): Dann baut er bei einem anderen Wert des Profils
    /// nichts neu (der Dickglas-Schatten rechnete bei jedem Schritt des Glanzreglers seine Unschärfe neu).
    /// </summary>
    private protected virtual object Profilwerte(Stilprofil p) => p;

    /// <summary>Ob gleiche Körper dieser Art ihren Bau teilen dürfen (aus, wenn <see cref="Bauen"/> etwas am Körper selbst ablegt).</summary>
    private protected virtual bool Teilbar => true;

    /// <summary>Baut die Schichten für die Fläche <paramref name="f"/> (ganze Gerätepixel) neu.</summary>
    private protected abstract void Bauen(Glasflaeche f, Stilprofil p);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        InvalidateVisual();
        Nachgeaendert();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Nachgeaendert();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        // Der Schlüssel des Baus: Art, Größe, Skalierung, Zustand, Form, die gelesenen Werte des Profils, Schatten, eigene Werte;
        // mit Zwischenbild dazu der eingebackene Effekt (als Werte) und die Zahl der Streifen (Tests rechnen beide Wege).
        var schluessel = (GetType(), RenderSize, s, Zustand, Rundung, Ecke, Umriss, Profilwerte(Profil), Schatten, Teilschluessel(),
            Zwischenbild ? (object)(Effektwerte(_backeffekt), Bildraster.Streifen) : null);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            _bau = Beziehen(schluessel, s);
            Bauten++;
        }
        var bau = _bau!;
        if (Zwischenbild && bau.Schichten.Count > 0)
        {
            if (!bau.Gerastert)
            {
                bau.Bild = Bildraster.Rastern(Zeichnung(bau.Schichten), s, _backeffekt);
                bau.Gerastert = true;
                _gerastert++;
                Baustelle.Bemessen(bau.Schluessel, bau);
            }
            if (!ReferenceEquals(_bildbau, bau))
            {
                _bildbau = bau;
                Zwischenbilder++;
            }
            if (bau.Bild is { } z0)
            {
                drawingContext.DrawImage(z0.Bild, z0.Lage);
                return;
            }
        }
        foreach (var z in bau.Schichten)
        {
            if (z.Ausschnitt is { } a) drawingContext.PushClip(a);
            if (z.Bild is { } bild) drawingContext.DrawImage(bild, z.Lage);
            else if (z.Form is { } form) drawingContext.DrawGeometry(z.Pinsel, z.Stift, form);
            if (z.Ausschnitt is not null) drawingContext.Pop();
        }
    }

    /// <summary>
    /// Der Bau zum <paramref name="schluessel"/>: ein geteilter, wenn ein gleicher Körper ihn auf diesem Faden schon gebaut hat,
    /// sonst neu gebaut (und, wenn teilbar, für die nächsten gleichen Körper abgelegt).
    /// </summary>
    private Bau Beziehen(object schluessel, double s)
    {
        var teilbar = Teilbar && Schluesselteilbar(schluessel);
        if (teilbar && Baustelle.Holen(schluessel) is { } geteilt) return geteilt;
        var bau = new Bau(schluessel);
        _ziel = bau.Schichten;
        try
        {
            var b = Glasgeometrie.Raster(RenderSize.Width, s);
            var h = Glasgeometrie.Raster(RenderSize.Height, s);
            if (b >= 4 && h >= 4) Bauen(Flaeche(b, h, s), Profil);
        }
        finally
        {
            _ziel = null;
        }
        _gebaut++;
        if (teilbar) Baustelle.Ablegen(schluessel, bau);
        return bau;
    }

    /// <summary>
    /// Ob ein Schlüssel geteilt werden darf: nicht, wenn er etwas Veränderliches enthält (ein nicht eingefrorener Pinsel oder
    /// Effekt kann sich ändern, ohne dass sich der Schlüssel ändert; der Körper behält seinen Bau dann für sich, wie bis beta.12).
    /// </summary>
    private static bool Schluesselteilbar(object? teil) => teil switch
    {
        null => true,
        Freezable f => f.IsFrozen,
        System.Runtime.CompilerServices.ITuple t => Enumerable.Range(0, t.Length).All(i => Schluesselteilbar(t[i])),
        _ => true,
    };

    /// <summary>Die Werte eines Effekts für den Schlüssel (zwei gleiche Effekte sind verschiedene Objekte), sonst der Effekt selbst.</summary>
    private static object? Effektwerte(Effect? effekt) => effekt switch
    {
        BlurEffect { IsFrozen: true } b => (b.Radius, b.KernelType, b.RenderingBias),
        DropShadowEffect { IsFrozen: true } d => (d.Color, d.Direction, d.ShadowDepth, d.BlurRadius, d.Opacity, d.RenderingBias),
        _ => effekt,
    };

    /// <summary>Die Schichten als eine eingefrorene Zeichnung (für das Zwischenbild).</summary>
    private static DrawingGroup Zeichnung(List<Zeichenschicht> schichten)
    {
        var gruppe = new DrawingGroup();
        foreach (var z in schichten)
        {
            Drawing? teil = z.Bild is { } bild ? new ImageDrawing(bild, z.Lage) : z.Form is { } form ? new GeometryDrawing(z.Pinsel, z.Stift, form) : null;
            if (teil is null) continue;
            if (teil is ImageDrawing)
            {
                // Bilder Pixel für Pixel (ein Bild in Gerätepixeln an seinem Platz, etwa das Plattenbild des Dickglases).
                var pixelgenau = new DrawingGroup();
                RenderOptions.SetBitmapScalingMode(pixelgenau, BitmapScalingMode.NearestNeighbor);
                pixelgenau.Children.Add(teil);
                teil = pixelgenau;
            }
            if (z.Ausschnitt is { } a)
            {
                var begrenzt = new DrawingGroup { ClipGeometry = a };
                begrenzt.Children.Add(teil);
                teil = begrenzt;
            }
            gruppe.Children.Add(teil);
        }
        gruppe.Freeze();
        return gruppe;
    }

    /// <summary>Die Fläche aus Umriss, Ecke oder Rundung, in ganzen Gerätepixeln.</summary>
    private protected Glasflaeche Flaeche(double b, double h, double s)
    {
        if (Umriss is { } u) return new Glasflaeche(0, 0, b, h, u.Eckradius(b, h, s), s) { Umriss = u };
        var kurz = Math.Min(b, h);
        var r = !double.IsNaN(Ecke) ? Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), kurz / 2)
            : Rundung >= 0.5 ? kurz / 2 : Math.Round(kurz * Math.Clamp(Rundung, 0, 0.5));
        return new Glasflaeche(0, 0, b, h, r, s);
    }

    /// <summary>Die Schichten, in die <see cref="Bauen"/> gerade baut (nur während des Baus gesetzt).</summary>
    private List<Zeichenschicht> Ziel => _ziel ?? throw new InvalidOperationException("Schichten nur in Bauen anlegen");

    private protected void Schicht(Geometry form, Brush? pinsel, Pen? stift = null)
    {
        if (pinsel is { CanFreeze: true, IsFrozen: false }) pinsel.Freeze();
        if (stift is { CanFreeze: true, IsFrozen: false }) stift.Freeze();
        if (form.CanFreeze && !form.IsFrozen) form.Freeze();
        Ziel.Add(new Zeichenschicht(form, pinsel, stift, null, default));
    }

    private protected void Schicht(ImageSource bild, Rect lage) => Ziel.Add(new Zeichenschicht(null, null, null, bild, lage));

    /// <summary>
    /// Eine Schicht, die nur im Rechteck (x0, y0) bis (x1, y1) in Pixeln der Fläche etwas beiträgt (seit 0.5.0-beta.12): Außerhalb
    /// ist ihr Pinsel durchsichtig oder sie liegt unter deckenden Schichten. Begrenzt gerastert spart das Software-Zeichnen die
    /// leeren Zeilen; das Bild bleibt gleich, weil der Ausschnitt auf ganzen Gerätepixeln liegt (nach außen gerundet).
    /// </summary>
    private protected void Schicht(Geometry form, Brush? pinsel, Glasflaeche f, double x0, double y0, double x1, double y1)
    {
        Schicht(form, pinsel);
        var (l, o, r, u) = (Math.Floor(x0) - 1, Math.Floor(y0) - 1, Math.Ceiling(x1) + 1, Math.Ceiling(y1) + 1);
        if (r <= l || u <= o) return;
        var ausschnitt = new RectangleGeometry(new Rect(f.Dip(l, o), f.Dip(r, u)));
        ausschnitt.Freeze();
        Ziel[^1] = Ziel[^1] with { Ausschnitt = ausschnitt };
    }

    /// <summary>Zahl der gebauten Schichten (für Tests).</summary>
    internal int Schichtzahl => _bau?.Schichten.Count ?? 0;

    // ------------------------------------------------------------------ Geteilte Bauten (seit 0.5.0-beta.13, A7)

    /// <summary>
    /// Ein Bau: die eingefrorenen Schichten eines Körpers und, mit <see cref="Zwischenbild"/>, sein Bild samt Lage. Gleiche Körper
    /// (gleicher <see cref="Schluessel"/>: Art, Größe, Skalierung, Zustand, Form, Profilwerte, Schatten, eigene Werte, Effekt)
    /// teilen ihn; nach dem Bau ändert sich nichts mehr daran (Formen, Pinsel und Bild eingefroren).
    /// </summary>
    private sealed class Bau(object schluessel)
    {
        public object Schluessel { get; } = schluessel;
        public List<Zeichenschicht> Schichten { get; } = [];
        public (ImageSource Bild, Rect Lage)? Bild { get; set; }
        public bool Gerastert { get; set; }

        /// <summary>Wie viele Bytes das Bild belegt (0 ohne Bild).</summary>
        public long Bytes => Bild?.Bild is BitmapSource b ? 4L * b.PixelWidth * b.PixelHeight : 0;
    }

    /// <summary>So viele Bauten bleiben je Faden zum Teilen bereit (die zuletzt gebrauchten).</summary>
    internal const int HoechstensBauten = 96;

    /// <summary>So viele Bytes Zwischenbild bleiben je Faden zum Teilen bereit (48 MB).</summary>
    internal const long HoechstensBytes = 48L << 20;

    [ThreadStatic]
    private static Teilvorrat<Bau>? _baustelle;

    /// <summary>Die zuletzt gebauten Körper dieses Fadens (<see cref="Teilvorrat{T}"/>).</summary>
    private static Teilvorrat<Bau> Baustelle => _baustelle ??= new Teilvorrat<Bau>(HoechstensBauten, HoechstensBytes, b => b.Bytes);

    [ThreadStatic]
    private static int _gebaut;

    [ThreadStatic]
    private static int _gerastert;

    /// <summary>Wie oft auf diesem Faden wirklich gebaut wurde (alle Familienkörper; für die Tests).</summary>
    internal static int Gebaut => _gebaut;

    /// <summary>Wie oft auf diesem Faden ein Zwischenbild wirklich gerechnet wurde (für die Tests).</summary>
    internal static int Gerastert => _gerastert;

    /// <summary>Wie oft auf diesem Faden ein Körper den Bau eines gleichen übernommen hat (für die Tests).</summary>
    internal static int Geteilt => Baustelle.Treffer;

    /// <summary>Wie viele Bauten und Bytes Zwischenbild auf diesem Faden zum Teilen bereitliegen (für die Tests).</summary>
    internal static (int Zahl, long Bytes) Vorrat => (Baustelle.Zahl, Baustelle.Bytes);

    /// <summary>Den Vorrat dieses Fadens leeren (für die Tests: ein Körper baut dann selbst).</summary>
    internal static void VorratLeeren() => Baustelle.Leeren();

    // ------------------------------------------------------------------ Farb- und Pinselhelfer

    private protected static Color Weiss(double deckung) => Farbton.Deckend(Colors.White, deckung);

    private protected static Color Schwarz(double deckung) => Farbton.Deckend(Colors.Black, deckung);

    private protected static Color Heller(Color c, double t) => Farbton.Mischen(c, Color.FromArgb(c.A, 255, 255, 255), t);

    private protected static Color Dunkler(Color c, double t) => Farbton.Mischen(c, Color.FromArgb(c.A, 0, 0, 0), t);

    /// <summary>Die Farbe im Zustand: überfahren heller, gedrückt dunkler (Werte aus dem Profil), sonst unverändert.</summary>
    private protected Color Zustandsfarbe(Color c, Stilprofil p) => Zustandsfarbe(c, p, Zustand);

    /// <summary>Wie <see cref="Zustandsfarbe(Color, Stilprofil)"/> für einen anderen Zustand (Abstimmung der Familienfarben).</summary>
    private protected static Color Zustandsfarbe(Color c, Stilprofil p, Glaszustand zustand) => zustand switch
    {
        Glaszustand.Ueberfahren => Heller(c, p.FamilieUeberfahren),
        Glaszustand.Gedrueckt => Dunkler(c, p.FamilieGedrueckt),
        _ => c,
    };

    /// <summary>Senkrechter Verlauf mit fester Lage in DIP.</summary>
    private protected static LinearGradientBrush Senkrecht(double oben, double unten, params (Color Farbe, double Lage)[] stopps)
    {
        var p = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, oben), EndPoint = new Point(0, unten) };
        foreach (var (farbe, lage) in stopps) p.GradientStops.Add(new GradientStop(farbe, lage));
        p.Freeze();
        return p;
    }

    /// <summary>Waagerechter Verlauf mit fester Lage in DIP.</summary>
    private protected static LinearGradientBrush Waagerecht(double links, double rechts, params (Color Farbe, double Lage)[] stopps)
    {
        var p = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(links, 0), EndPoint = new Point(rechts, 0) };
        foreach (var (farbe, lage) in stopps) p.GradientStops.Add(new GradientStop(farbe, lage));
        p.Freeze();
        return p;
    }

    /// <summary>Linearer Verlauf mit fester Lage von <paramref name="von"/> nach <paramref name="bis"/> (DIP).</summary>
    private protected static LinearGradientBrush Verlauf(Point von, Point bis, params (Color Farbe, double Lage)[] stopps)
    {
        var p = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = von, EndPoint = bis };
        foreach (var (farbe, lage) in stopps) p.GradientStops.Add(new GradientStop(farbe, lage));
        p.Freeze();
        return p;
    }

    /// <summary>Radialer Verlauf mit fester Lage und Halbachsen in DIP.</summary>
    private protected static RadialGradientBrush Radial(Point mitte, double rx, double ry, params (Color Farbe, double Lage)[] stopps) =>
        Pinselwerk.Rund(mitte, rx, ry, stopps);

    private protected static SolidColorBrush Flaeche(Color c) => Pinselwerk.Flaeche(c);

    /// <summary>Ein Rechteck in Pixeln der Fläche, als Geometrie in DIP (Radius in Pixeln).</summary>
    private protected static RectangleGeometry Rechteck(Glasflaeche f, double x, double y, double b, double h, double radius = 0)
    {
        var g = new RectangleGeometry(new Rect(f.Dip(x, y), f.Dip(x + b, y + h)), radius / f.Skala, radius / f.Skala);
        g.Freeze();
        return g;
    }

    /// <summary>Eine Ellipse um den Pixelpunkt (x, y) mit Halbachsen in Pixeln, als Geometrie in DIP.</summary>
    private protected static EllipseGeometry Ellipse(Glasflaeche f, double x, double y, double rx, double ry)
    {
        var g = new EllipseGeometry(f.Dip(x, y), rx / f.Skala, ry / f.Skala);
        g.Freeze();
        return g;
    }

    /// <summary>Die Farbe eines Pinsels (einfarbig oder erster Stopp), sonst <paramref name="ersatz"/>; durchsichtig gilt als nicht gesetzt.</summary>
    private protected static Color FarbeVon(Brush? pinsel, Color ersatz) => pinsel switch
    {
        SolidColorBrush { Color.A: > 0 } s => s.Color,
        GradientBrush { GradientStops.Count: > 0 } g => g.GradientStops[0].Color,
        _ => ersatz,
    };

    /// <summary>
    /// Ein vierstrahliger Funke (Lichtpunkt auf Chrom oder Glas) um den Pixelpunkt (x, y), <paramref name="groesse"/> Pixel
    /// breit: weicher Kern, waagerecht ein langer, senkrecht ein kürzerer Strahl, alle auslaufend.
    /// </summary>
    private protected void Funke(Glasflaeche f, double x, double y, double groesse, double deckung)
    {
        if (deckung <= 0 || groesse < 2) return;
        var s = f.Skala;
        var kern = groesse * 0.19;
        Schicht(Ellipse(f, x, y, kern, kern), Radial(f.Dip(x, y), kern / s, kern / s, (Weiss(deckung), 0), (Weiss(0), 1)));
        var dicke = Math.Max(1, Math.Round(groesse * 0.045));
        Schicht(Rechteck(f, x - groesse / 2, y - dicke / 2, groesse, dicke),
            Waagerecht((x - groesse / 2) / s, (x + groesse / 2) / s, (Weiss(0), 0), (Weiss(deckung), 0.5), (Weiss(0), 1)));
        var lang = groesse * 0.7;
        Schicht(Rechteck(f, x - dicke / 2, y - lang / 2, dicke, lang),
            Senkrecht((y - lang / 2) / s, (y + lang / 2) / s, (Weiss(0), 0), (Weiss(deckung), 0.5), (Weiss(0), 1)));
    }

    /// <summary>Die gewählte Schrift an den Knopf geben, dessen Vorlage diesen Körper trägt (<see cref="Glas.KontrastschriftProperty"/>).</summary>
    private protected void SchriftWeitergeben(Brush schrift)
    {
        if (TemplatedParent is { } knopf && !ReferenceEquals(Glas.GetKontrastschrift(knopf), schrift)) Glas.SetKontrastschrift(knopf, schrift);
    }

    /// <summary>Weiß oder fast Schwarz, je nachdem, was auf <paramref name="grund"/> den höheren Kontrast hat (WCAG, <see cref="Schriftwahl.Kontrastfarbe"/>).</summary>
    internal static Color Kontrastschrift(Color grund) => Schriftwahl.Kontrastfarbe([grund]);

    /// <summary>Die letzte Farbe eines Verlaufs (die zweite Farbe des Lichts), sonst die einzige.</summary>
    private protected static Color Zweitfarbe(Brush? pinsel, Color ersatz) => pinsel switch
    {
        GradientBrush { GradientStops.Count: > 1 } g => g.GradientStops[^1].Color,
        _ => FarbeVon(pinsel, ersatz),
    };
}
