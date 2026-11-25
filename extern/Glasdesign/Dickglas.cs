// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Glasdesign;

/// <summary>
/// Die dicke Glasplatte der Stilfamilie Dickglas (KONZEPT.md 5b.1, Vorbild 1): eine klare Platte als Pille oder
/// abgerundetes Rechteck, deren Dicke unten als Seite zu sehen ist. Um den Kern liegt ein breiter Wulst (<see cref="Wulst"/>,
/// Anteil der Plattenhöhe), außen steil, nach innen flach: Ringe von je einem Gerätepixel, die oben den Himmel und unten
/// den Boden spiegeln (Fresnel), dazu ein Glanzstreif auf der oberen Kante, rechts kräftiger. Der Kern ist dunkel,
/// getönt oder klar (<see cref="Kern"/>). Durch das Glas fällt farbiges Licht in zwei Farben (<see cref="Licht"/>, ein
/// Verlauf von links nach rechts, aus dem Background): in die Seite, unten in Wulst und Kern. Den farbigen Schatten auf der
/// Unterlage zeichnet <see cref="Dickglasschatten"/> darunter. Gedrückt sinkt die Platte um die halbe Dicke ein.
/// </summary>
public sealed class Dickglaskoerper : Familienkoerper
{
    public static readonly DependencyProperty LichtProperty = DependencyProperty.Register(
        nameof(Licht), typeof(Brush), typeof(Dickglaskoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KernProperty = DependencyProperty.Register(
        nameof(Kern), typeof(Dickkern), typeof(Dickglaskoerper), new FrameworkPropertyMetadata(Dickkern.Dunkel, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KernFarbeProperty = DependencyProperty.Register(
        nameof(KernFarbe), typeof(Color), typeof(Dickglaskoerper), new FrameworkPropertyMetadata(Color.FromRgb(0x07, 0x07, 0x0A), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Breite des Wulsts als Anteil der Plattenhöhe.</summary>
    public static readonly DependencyProperty WulstProperty = DependencyProperty.Register(
        nameof(Wulst), typeof(double), typeof(Dickglaskoerper), new FrameworkPropertyMetadata(0.15, FrameworkPropertyMetadataOptions.AffectsRender, RaenderNeu));

    /// <summary>Sichtbare Dicke unten als Anteil der ganzen Höhe (sie gehört zur Höhe des Elements).</summary>
    public static readonly DependencyProperty DickeProperty = DependencyProperty.Register(
        nameof(Dicke), typeof(double), typeof(Dickglaskoerper), new FrameworkPropertyMetadata(0.19, FrameworkPropertyMetadataOptions.AffectsRender, RaenderNeu));

    private static readonly DependencyPropertyKey InhaltsrandKey = DependencyProperty.RegisterReadOnly(
        nameof(Inhaltsrand), typeof(Thickness), typeof(Dickglaskoerper), new FrameworkPropertyMetadata(new Thickness()));

    /// <summary>Wo der Inhalt liegt: auf der Platte ohne die Seite darunter (gedrückt mit ihr eingesunken).</summary>
    public static readonly DependencyProperty InhaltsrandProperty = InhaltsrandKey.DependencyProperty;

    private static readonly DependencyPropertyKey KernrandKey = DependencyProperty.RegisterReadOnly(
        nameof(Kernrand), typeof(Thickness), typeof(Dickglaskoerper), new FrameworkPropertyMetadata(new Thickness()));

    /// <summary>Wo der Kern liegt: auf der Platte innerhalb des Wulsts (für Felder und Spuren darin).</summary>
    public static readonly DependencyProperty KernrandProperty = KernrandKey.DependencyProperty;

    public Dickglaskoerper() => SetResourceReference(KernFarbeProperty, Schluessel.DickglasKernFarbe);

    /// <summary>
    /// Im Zwischenbild sparsam rastern (seit 0.5.0-beta.12: Wulstringe als Bänder, Schichten auf ihren Ausschnitt); aus nur für
    /// die Tests (Vergleich mit den ganzen Schichten).
    /// </summary>
    internal static bool Sparsam { get; set; } = true;

    public Thickness Inhaltsrand => (Thickness)GetValue(InhaltsrandProperty);
    public Thickness Kernrand => (Thickness)GetValue(KernrandProperty);

    private static void RaenderNeu(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Dickglaskoerper)d).RaenderSetzen();

    private protected override void Nachgeaendert() => RaenderSetzen();

    /// <summary>Inhalts- und Kernrand aus Größe, Dicke, Wulst und Zustand, in ganzen Gerätepixeln.</summary>
    private void RaenderSetzen()
    {
        if (Raender(RenderSize.Height, Glasgeometrie.Skala(this)) is not { } r) return;
        if (Inhaltsrand != r.Inhalt) SetValue(InhaltsrandKey, r.Inhalt);
        if (Kernrand != r.Kern) SetValue(KernrandKey, r.Kern);
    }

    /// <summary>
    /// Inhalts- und Kernrand (DIP, auf ganzen Gerätepixeln) für eine Platte von <paramref name="hoehe"/> DIP bei
    /// <paramref name="skala"/>, mit Dicke, Wulst und Zustand dieses Körpers; null ohne Höhe. Dieselbe Regel nimmt der
    /// <see cref="Kernplatz"/> (seit 0.5.0-beta.13), damit Inhalt und Platte zusammenpassen.
    /// </summary>
    internal (Thickness Inhalt, Thickness Kern)? Raender(double hoehe, double skala)
    {
        var gesamt = Glasgeometrie.Raster(hoehe, skala);
        if (gesamt <= 0 || double.IsNaN(gesamt) || double.IsInfinity(gesamt)) return null;
        var dicke = Math.Round(gesamt * Math.Clamp(Dicke, 0, 0.5));
        var sinken = Zustand == Glaszustand.Gedrueckt ? Einsinken(gesamt, Dicke) : 0;
        var hp = gesamt - dicke;
        var n = Math.Max(2, Math.Round(hp * Math.Clamp(Wulst, 0.02, 0.45)));
        var s = skala;
        return (new Thickness(0, sinken / s, 0, (dicke - sinken) / s), new Thickness(n / s, (sinken + n) / s, n / s, (dicke - sinken + n) / s));
    }

    public Brush? Licht { get => (Brush?)GetValue(LichtProperty); set => SetValue(LichtProperty, value); }
    public Dickkern Kern { get => (Dickkern)GetValue(KernProperty); set => SetValue(KernProperty, value); }
    public Color KernFarbe { get => (Color)GetValue(KernFarbeProperty); set => SetValue(KernFarbeProperty, value); }
    public double Wulst { get => (double)GetValue(WulstProperty); set => SetValue(WulstProperty, value); }
    public double Dicke { get => (double)GetValue(DickeProperty); set => SetValue(DickeProperty, value); }

    /// <summary>Die beiden Farben des Lichts (links, rechts): erster und letzter Stopp des Verlaufs, sonst zweimal dieselbe.</summary>
    public (Color Links, Color Rechts) Lichtfarben => (FarbeVon(Licht, Color.FromRgb(0x3B, 0x5B, 0xFF)), Zweitfarbe(Licht, Color.FromRgb(0xA0, 0x3D, 0xFF)));

    /// <summary>Wie viele Pixel die Platte gedrückt einsinkt: die halbe Dicke (für die Vorlage, die den Inhalt mitnimmt).</summary>
    internal static double Einsinken(double hoehePx, double dicke) => Math.Round(Math.Round(hoehePx * Math.Clamp(dicke, 0, 0.5)) / 2);

    private protected override object Teilschluessel() => (Lichtfarben, Kern, KernFarbe, Wulst, Dicke, Zwischenbild && Sparsam);

    /// <summary>Bauen liest vom Stilprofil nur Fresnel, Licht und Glanz (seit 0.5.0-beta.13, A7).</summary>
    private protected override object Profilwerte(Stilprofil p) => (p.DickglasFresnel, p.DickglasLicht, p.DickglasGlanz);

    /// <summary>Was durch das klare Glas zu sehen ist (die helle Unterlage, für die die Platte gerechnet ist).</summary>
    private static readonly Color Durchblick = Color.FromRgb(0xE0, 0xE0, 0xE6);

    /// <summary>
    /// Die Farben im Zustand, für Bauen und die Schriftwahl gleich: Licht (links, rechts; gesperrt zum Grau), Stärke des
    /// Lichts, klar, Mischton, Kern und sein Verlauf oben und unten.
    /// </summary>
    private (Color A, Color C, double Glut, bool Klar, Color Ton, Color Kern, Color KernOben, Color KernUnten) Farben(Stilprofil p)
    {
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var (a, c) = Lichtfarben;
        if (!aktiv)
        {
            a = Farbton.Mischen(a, Color.FromRgb(0x9A, 0x9C, 0xA4), 0.75);
            c = Farbton.Mischen(c, Color.FromRgb(0x9A, 0x9C, 0xA4), 0.75);
        }
        var glut = Math.Clamp(p.DickglasLicht, 0, 2) * (Zustand == Glaszustand.Ueberfahren ? 1.25 : 1) * (aktiv ? 1 : 0.4);
        var klar = Kern == Dickkern.Klar;
        var ton = Farbton.Mischen(a, c, 0.5);
        var kern = Kern switch
        {
            Dickkern.Klar => Farbton.Mischen(Durchblick, ton, 0.1),
            Dickkern.Getoent => Farbton.Mischen(KernFarbe, c, 0.35),
            _ => KernFarbe,
        };
        if (!aktiv) kern = Farbton.Mischen(kern, Color.FromRgb(0x50, 0x52, 0x58), 0.3);
        var kernUnten = Kern == Dickkern.Getoent ? Farbton.Mischen(KernFarbe, c, aktiv ? 0.5 : 0.3) : kern;
        var kernOben = Kern == Dickkern.Getoent ? Farbton.Mischen(KernFarbe, c, aktiv ? 0.2 : 0.15) : klar ? Heller(kern, 0.02) : Heller(kern, 0.05);
        return (a, c, glut, klar, ton, kern, kernOben, kernUnten);
    }

    /// <summary>
    /// Schriftwahl (0.5.0-beta.7): die Farben des Kerns im Schriftband (sein Verlauf), dazu das farbige Licht, so stark, wie
    /// es dort ankommt (wie Bauen: links die erste, rechts die zweite Farbe aus dem Wulst, unten der Mischton im Kern), bei
    /// 30, 50 und 70 Prozent der Breite. Das Band reicht von 25 bis 75 Prozent der Kernhöhe: Die Schrift steht mittig auf der
    /// Platte und reicht damit tiefer in den Kern, wo das Licht unten heller wird.
    /// </summary>
    private protected override IReadOnlyList<Color> Flaechenfarben()
    {
        var (a, c, glut, klar, ton, _, kernOben, kernUnten) = Farben(Profil);
        var g = glut > 0 ? Math.Min(1, 0.85 * Math.Pow(glut, 1.2)) : 0;
        var w = Math.Clamp(Wulst, 0.02, 0.45);
        // Deckung der Lichter über dem Abstand (in Halbachsen): Wulstlicht mit den Stopps 1, 0,35 bei der Hälfte, 0; Kernlicht linear.
        static double Wulstlicht(double d) => d >= 1 ? 0 : d <= 0.5 ? 1 - 1.3 * d : 0.7 * (1 - d);
        static double Abstand(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);
        var proben = new List<Color>();
        foreach (var x in new[] { 0.3, 0.5, 0.7 })
            proben.AddRange(Band(t =>
            {
                var farbe = Verlaufsfarbe(t, (kernOben, 0), (Farbton.Mischen(kernOben, kernUnten, 0.5), 0.6), (kernUnten, 1));
                if (g <= 0) return farbe;
                var y = w + t * (1 - 2 * w);
                farbe = Schriftwahl.Ueber(Farbton.Deckend(a, g * Wulstlicht(Abstand((x - 0.12) / 0.42, (1.02 - y) / 0.62))), farbe);
                farbe = Schriftwahl.Ueber(Farbton.Deckend(c, g * Wulstlicht(Abstand((0.88 - x) / 0.42, (1.02 - y) / 0.62))), farbe);
                var kernlicht = Math.Max(0, 1 - Abstand((x - 0.5) / 0.5, (1.05 - y) / 0.42));
                return Schriftwahl.Ueber(Farbton.Deckend(ton, g * (klar ? 0.25 : 0.45) * kernlicht), farbe);
            }, 0.25, 0.75));
        return proben;
    }

    private static double Glatt(double a, double b, double x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        double b = f.Breite, gesamt = f.Hoehe;
        var dicke = Math.Round(gesamt * Math.Clamp(Dicke, 0, 0.5));
        var sinken = Zustand == Glaszustand.Gedrueckt ? Einsinken(gesamt, Dicke) : 0;
        var hp = gesamt - dicke;
        if (hp < 6) return;
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var (a, c, glut, klar, ton, kern, kernOben, kernUnten) = Farben(p);
        var grund = Durchblick;

        var platte = Flaeche(b, hp, s) with { Y = sinken };
        var seite = Flaeche(b, hp, s) with { Y = dicke };
        var r = Math.Min(platte.Radius, Math.Min(b, hp) / 2);

        // Die Farben der Wulstringe (unten gezeichnet): außen steil (spiegelt viel), innen flach (zeigt den Kern).
        var n = Math.Max(2, (int)Math.Round(hp * Math.Clamp(Wulst, 0.02, 0.45)));
        var fresnelStaerke = Math.Clamp(p.DickglasFresnel, 0, 1.5);
        var ringe = new List<(Color Oben, Color Unten)>(n);
        for (var i = 0; i < n; i++)
        {
            var u = (i + 0.5) / n;
            var k = 2.8 * Math.Pow(1 - u, 1.5);
            var nz = 1 / Math.Sqrt(1 + k * k);
            var fresnel = (0.03 + 0.97 * Math.Pow(1 - nz, 3)) * fresnelStaerke;
            var durch = klar
                ? Farbton.Mischen(grund, Colors.Black, 0.03 + 0.30 * Glatt(0.5, 1.0, u))
                : Farbton.Mischen(Farbton.Mischen(grund, kern, 0.28), kern, Glatt(0.3, 0.95, u));
            ringe.Add((Farbton.Mischen(durch, Color.FromRgb(0xF7, 0xF7, 0xFA), Math.Clamp(fresnel, 0, 1)),
                Farbton.Mischen(durch, Color.FromRgb(0xB3, 0xB3, 0xBA), Math.Clamp(fresnel, 0, 1))));
        }
        // Mit Zwischenbild (große Flächen, seit 0.5.0-beta.12) rastert der Körper sparsam: Wulst und Kern rechnet er als ein Bild
        // (Plattenbild; jeder Ring einzeln kostete das Software-Zeichnen je Ring einen festen Betrag, der sich auch auf mehrere
        // Fäden nicht verteilen lässt), nur die Außenkante zeichnet WPF; Schichten, die nur in einem Teil etwas beitragen (die
        // Seite unter der deckenden Platte, Lichter und Glanz außerhalb ihres Verlaufs durchsichtig), nur in ihrem Ausschnitt.
        // Nur auf dem abgerundeten Rechteck und nur, wenn Ringe und Kern deckend sind.
        var sparsam = Zwischenbild && Sparsam && Umriss is null && kernOben.A == 255 && kernUnten.A == 255 && ringe.TrueForAll(x => x.Oben.A == 255 && x.Unten.A == 255);
        void Teil(Geometry form, Brush pinsel, double x0, double y0, double x1, double y1)
        {
            if (sparsam) Schicht(form, pinsel, f, x0, y0, x1, y1);
            else Schicht(form, pinsel);
        }

        // Die Seite: die Dicke der Platte unter ihr, klares Glas, durch das das Licht scheint, an den Enden farbiger. Zu sehen
        // ist sie nur unter der Platte und neben deren unteren Rundungen (oben deckt die Platte sie ganz, solange sie tiefer
        // beginnt als deren obere Rundungen).
        if (dicke - sinken > 0)
        {
            var seitenGrund = klar ? Farbton.Mischen(grund, Colors.Black, 0.1) : Farbton.Mischen(Farbton.Mischen(KernFarbe, grund, 0.2), grund, 0.1);
            var t = Math.Clamp(glut * (klar ? 0.7 : 0.62), 0, 0.95);
            var sichtbar = dicke >= sinken + r + 1 ? Math.Max(dicke, sinken + hp - r - 1) : dicke;
            var so = dicke / s;
            var su = (dicke + hp) / s;
            Teil(seite.Form(0), Waagerecht(0, b / s,
                (Farbton.Deckend(Farbton.Mischen(seitenGrund, a, Math.Min(0.95, t * 1.3)), 0.92), 0), (Farbton.Deckend(Farbton.Mischen(seitenGrund, a, t * 0.55), 0.86), 0.25),
                (Farbton.Deckend(Farbton.Mischen(seitenGrund, ton, t * 0.35), 0.84), 0.5), (Farbton.Deckend(Farbton.Mischen(seitenGrund, c, t * 0.55), 0.86), 0.75),
                (Farbton.Deckend(Farbton.Mischen(seitenGrund, c, Math.Min(0.95, t * 1.3)), 0.92), 1)), 0, sichtbar, b, dicke + hp);
            Teil(seite.Form(0), Senkrecht(so, su, (Weiss(0), 0), (Weiss(0), Math.Max(0, 1 - 1.2 * dicke / hp)), (Weiss(0.18), Math.Max(0, 1 - 0.75 * dicke / hp)), (Schwarz(0.35), 1)),
                0, sichtbar, b, dicke + hp);
            Teil(seite.Ring(0, Glasgeometrie.Haarlinie), Senkrecht(so, su, (Weiss(0), 0), (Weiss(0), 0.5), (Schwarz(0.3), 1)), 0, sichtbar, b, dicke + hp);
        }

        // Der Wulst als Ringe von je einem Gerätepixel, darin der Kern (oben eine Spur heller).
        var po = sinken / s;
        var pu = (sinken + hp) / s;
        (Color, double)[] Ringverlauf(int i) =>
            [(ringe[i].Oben, 0), (Farbton.Mischen(ringe[i].Oben, ringe[i].Unten, 0.6), 0.32), (ringe[i].Unten, 0.55), (ringe[i].Unten, 1)];
        (Color, double)[] kernverlauf = [(kernOben, 0), (Farbton.Mischen(kernOben, kernUnten, 0.5), 0.6), (kernUnten, 1)];
        var kernForm = platte.Form(n);
        var ko = (sinken + n) / s;
        var ku = (sinken + hp - n) / s;
        // Eine feine Lichtlinie oben im Kern (ab 30 Prozent der Kernhöhe durchsichtig).
        (Color, double)[] kernlinie = [(Weiss(klar ? 0.12 : 0.24), 0), (Weiss(0), 0.3)];
        if (sparsam && Plattenbild.Rechnen((int)b, (int)hp, platte.Radius, n, [.. Enumerable.Range(0, n).Select(Ringverlauf)], kernverlauf, kernlinie, s) is { } plattenbild)
        {
            // Sparsam: Ringe, Kern und Lichtlinie als ein Bild (Plattenbild), mit der Glättung der Außenkante wie in WPF.
            Schicht(plattenbild, new Rect(platte.Dip(0, 0), platte.Dip(b, hp)));
        }
        else
        {
            // Jeder Ring als ganze, um i eingerückte Form von außen nach innen: Die nächste deckt die vorige innen ganz zu,
            // so bleiben an den Rundungen keine Fugen zwischen zwei Ringen (geglättete Kanten addieren sich sonst nicht zu 1).
            for (var i = 0; i < n; i++) Schicht(platte.Form(i), Senkrecht(po, pu, Ringverlauf(i)));
            Schicht(kernForm, Senkrecht(ko, ku, kernverlauf));
            Schicht(platte.Ring(n, n + Glasgeometrie.Haarlinie), Senkrecht(ko, ku, kernlinie));
        }

        // Farbiges Licht unten in Wulst und Kern: links die erste, rechts die zweite Farbe, an den Enden stärker. Jedes Licht ist
        // ein Verlauf um eine Mitte, außerhalb seiner Halbachsen durchsichtig: Ausschnitt ist das umschließende Rechteck.
        void Licht(Geometry form, double mx, double my, double rx, double ry, params (Color Farbe, double Lage)[] stopps) =>
            Teil(form, Radial(platte.Dip(mx, my), rx / s, ry / s, stopps), mx - rx, sinken + my - ry, mx + rx, sinken + my + ry);
        if (glut > 0)
        {
            var g = Math.Min(1, 0.85 * Math.Pow(glut, 1.2));
            var plattenForm = platte.Form(0);
            Licht(plattenForm, b * 0.12, hp * 1.02, b * 0.42, hp * 0.62, (Farbton.Deckend(a, g), 0), (Farbton.Deckend(a, g * 0.35), 0.5), (Farbton.Deckend(a, 0), 1));
            Licht(plattenForm, b * 0.88, hp * 1.02, b * 0.42, hp * 0.62, (Farbton.Deckend(c, g), 0), (Farbton.Deckend(c, g * 0.35), 0.5), (Farbton.Deckend(c, 0), 1));
            Licht(kernForm, b * 0.5, hp + hp * 0.05, b * 0.5, hp * 0.42, (Farbton.Deckend(ton, g * (klar ? 0.25 : 0.45)), 0), (Farbton.Deckend(ton, 0), 1));
        }

        // Glanzstreif auf der oberen Kante, rechts kräftiger, und Glanzlichter auf den Rundungen oben rechts und unten links.
        var glanz = Math.Clamp(p.DickglasGlanz, 0, 2) * (Zustand == Glaszustand.Gedrueckt ? 0.7 : 1) * (aktiv ? 1 : 0.5);
        if (glanz > 0)
        {
            var band = platte.Ring(1, Math.Max(2, Math.Round(n * 0.45)));
            Licht(band, b * 0.74, 0, b * 0.42, n * 1.4, (Weiss(Math.Min(1, 0.85 * glanz)), 0), (Weiss(Math.Min(1, 0.35 * glanz)), 0.5), (Weiss(0), 1));
            var rx = Math.Max(2, r * 0.55);
            Licht(platte.Ring(0, Math.Max(2, Math.Round(n * 0.6))), b - r * 0.55, r * 0.42, rx, rx * 0.8, (Weiss(Math.Min(1, 0.9 * glanz)), 0), (Weiss(0), 1));
            Licht(platte.Ring(0, Math.Max(2, Math.Round(n * 0.5))), r * 0.5, hp - r * 0.45, rx * 0.8, rx * 0.6, (Weiss(Math.Min(1, 0.45 * glanz)), 0), (Weiss(0), 1));
        }
        // Die Kante (0.5.0-beta.7, Entscheidung 4 aus KONZEPT 5b: auf hellem und dunklem Grund scharf). Ein breiter Wulst bildet
        // sie selbst: Seine äußeren Ringe spiegeln hell und heben sich von hellem wie von dunklem Grund mit dem ersten Pixel ab.
        // Bis beta.6 lag dort eine dunkle Haarlinie; auf dunklem Grund verschwand sie fast und lief mit dem hellen Wulst
        // dahinter über zwei Pixel aus (Glints Prüfstand: weich, 2 Pixel). Ein schmaler Wulst (Glasblock, unter WulstMitKante)
        // spiegelt außen zu wenig, um sich auf heller Unterlage abzuheben; er behält die dunkle Außenkante.
        if (Wulst < WulstMitKante)
            Schicht(platte.Ring(0, Glasgeometrie.Haarlinie), Senkrecht(po, pu, (Schwarz(0.35), 0), (Schwarz(0.45), 1)));
    }

    /// <summary>Unter diesem Anteil (schmaler Wulst, etwa der Glasblock mit 0,06) bekommt die Platte eine dunkle Außenkante.</summary>
    internal const double WulstMitKante = 0.1;
}

/// <summary>
/// Der Platz des Inhalts im Kern einer Dickglasplatte, deren Höhe der Inhalt bestimmt (Glasblock: Glas.Dickglas.Feld,
/// Dickglas-Karte, Liste des Dickglas-Suchfelds), seit 0.5.0-beta.13 statt eines Rands, der an
/// <see cref="Dickglaskoerper.Kernrand"/> gebunden war. Der Kernrand hängt an der Höhe der Platte und die Höhe am Inhalt samt
/// Kernrand; über die Bindung fand das Layout je nach Weg verschiedene stabile Höhen (Dickglas-Karte mit drei Einträgen frisch
/// 189 DIP, nach einer größeren Schrift und zurück 191 DIP), und das erste Bild im Speicher war zu klein, weil der Kernrand
/// erst nach dem ersten Anordnen kam. Der Kernplatz rechnet beim Messen die kleinste Höhe, die zum Inhalt samt dem Kernrand
/// dieser Höhe passt (immer dieselbe, egal auf welchem Weg), und ordnet den Inhalt nach dem Kernrand der tatsächlichen Höhe an
/// (dieselbe Regel wie der Körper, <see cref="Dickglaskoerper.Raender"/>). <see cref="Koerper"/> ist die Platte in derselben
/// Zelle. Der Inhalt liegt in einem inneren Rahmen, der um den Kernrand versetzt steht: So rundet das Layout den Kernrand und
/// den Rand des Inhalts je für sich auf Gerätepixel, wie beim Border mit gebundenem Margin bis beta.12 (in einem Schritt
/// gerundet stand der Inhalt bei 125 Prozent einen Pixel anders).
/// </summary>
[System.Windows.Markup.ContentProperty(nameof(Child))]
public sealed class Kernplatz : FrameworkElement
{
    public static readonly DependencyProperty KoerperProperty = DependencyProperty.Register(
        nameof(Koerper), typeof(Dickglaskoerper), typeof(Kernplatz), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, KoerperGeaendert));

    /// <summary>
    /// Der Kernrand der Platte, nur als Anstoß: Dicke, Wulst und Zustand (gedrückt sinkt der Inhalt mit) melden sich so; neu gemessen
    /// wird nur, wenn sich die Regel geändert hat (<see cref="PlattenrandGeaendert"/>).
    /// </summary>
    private static readonly DependencyProperty PlattenrandProperty = DependencyProperty.Register(
        "Plattenrand", typeof(Thickness), typeof(Kernplatz), new FrameworkPropertyMetadata(default(Thickness), PlattenrandGeaendert));

    private readonly Border _rahmen = new();

    public Kernplatz()
    {
        AddVisualChild(_rahmen);
        AddLogicalChild(_rahmen);
    }

    public Dickglaskoerper? Koerper { get => (Dickglaskoerper?)GetValue(KoerperProperty); set => SetValue(KoerperProperty, value); }

    /// <summary>Der Inhalt im Kern.</summary>
    public UIElement? Child { get => _rahmen.Child; set => _rahmen.Child = value; }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => index == 0 ? _rahmen : throw new ArgumentOutOfRangeException(nameof(index));

    protected override System.Collections.IEnumerator LogicalChildren => new[] { _rahmen }.GetEnumerator();

    private static void KoerperGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is Dickglaskoerper neu)
            BindingOperations.SetBinding(d, PlattenrandProperty, new Binding(nameof(Dickglaskoerper.Kernrand)) { Source = neu, Mode = BindingMode.OneWay });
        else BindingOperations.ClearBinding(d, PlattenrandProperty);
    }

    private Thickness Rand(double hoehe) => Koerper?.Raender(hoehe, Glasgeometrie.Skala(this))?.Kern ?? default;

    /// <summary>Was <see cref="Dickglaskoerper.Raender"/> außer der Höhe liest (Dicke, Wulst, gedrückt, Skalierung), beim letzten Messen.</summary>
    private object? _regel;

    private object? Regel() => Koerper is { } k ? (k.Dicke, k.Wulst, k.Zustand == Glaszustand.Gedrueckt, Glasgeometrie.Skala(this)) : null;

    /// <summary>
    /// Der Kernrand der Platte hat sich geändert: neu messen nur, wenn sich die Regel des Rands geändert hat (seit 0.5.0-beta.13, A7).
    /// Nach einer neuen Größe der Platte kommt ihr Kernrand nach, die Regel bleibt; der Kernplatz rechnet den Rand ohnehin aus der
    /// Höhe. Bis dahin maß er dann ein zweites Mal, je Messen den Inhalt zweimal mit verschiedener Breite (Glint G3: 7,7 statt 6
    /// Messungen je Schritt der Dickglas-Karten).
    /// </summary>
    private static void PlattenrandGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var platz = (Kernplatz)d;
        if (!Equals(platz._regel, platz.Regel())) platz.InvalidateMeasure();
    }

    /// <summary>Wie oft der Kernplatz gemessen hat (für die Tests: nach neuer Schrift einmal).</summary>
    internal int Messungen { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        Messungen++;
        _regel = Regel();
        // Von unten an die kleinste passende Höhe: Inhalt messen, Kernrand der so entstehenden Höhe, wieder messen, bis sie steht.
        var rand = default(Thickness);
        var hoehe = 0.0;
        for (var schritt = 0; schritt < 16; schritt++)
        {
            _rahmen.Measure(new Size(Math.Max(0, availableSize.Width - rand.Left - rand.Right), Math.Max(0, availableSize.Height - rand.Top - rand.Bottom)));
            var neu = _rahmen.DesiredSize.Height + rand.Top + rand.Bottom;
            if (Math.Abs(neu - hoehe) < 1e-9) break;
            hoehe = neu;
            rand = Rand(hoehe);
        }
        return new Size(_rahmen.DesiredSize.Width + rand.Left + rand.Right, hoehe);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Der Rahmen steht um den Kernrand versetzt und rundet diesen Versatz selbst; der Inhalt darin rundet seinen eigenen Rand.
        var r = Rand(finalSize.Height);
        _rahmen.Arrange(new Rect(r.Left, r.Top, Math.Max(0, finalSize.Width - r.Left - r.Right), Math.Max(0, finalSize.Height - r.Top - r.Bottom)));
        return finalSize;
    }
}
/// <summary>
/// Der Schatten unter einer Dickglasplatte (KONZEPT.md 5b.1): nach unten links versetzt, blaugrau statt grau, darin
/// farbiges Licht in den Farben der Platte (links die erste, rechts die zweite), dazu ein dunkler Kontaktschatten unter
/// der Kante. Ein eigenes Element mit Weichzeichner, weil es weich sein muss; es reicht weit über die eigenen Grenzen
/// hinaus (bis 45 Prozent der Höhe nach links und unten) und braucht dort Platz. Mit Glas.Schatten Aus zeichnet es nichts.
/// </summary>
public sealed class Dickglasschatten : Familienkoerper
{
    public static readonly DependencyProperty LichtProperty = DependencyProperty.Register(
        nameof(Licht), typeof(Brush), typeof(Dickglasschatten), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchattenFarbeProperty = DependencyProperty.Register(
        nameof(SchattenFarbe), typeof(Color), typeof(Dickglasschatten), new FrameworkPropertyMetadata(Color.FromRgb(0x1C, 0x22, 0x32), FrameworkPropertyMetadataOptions.AffectsRender));

    public Dickglasschatten() => SetResourceReference(SchattenFarbeProperty, Schluessel.DickglasSchattenFarbe);

    public Brush? Licht { get => (Brush?)GetValue(LichtProperty); set => SetValue(LichtProperty, value); }
    public Color SchattenFarbe { get => (Color)GetValue(SchattenFarbeProperty); set => SetValue(SchattenFarbeProperty, value); }

    private protected override object Teilschluessel() => (FarbeVon(Licht, Color.FromRgb(0x3B, 0x5B, 0xFF)), Zweitfarbe(Licht, Color.FromRgb(0xA0, 0x3D, 0xFF)), SchattenFarbe);

    /// <summary>
    /// Bauen liest vom Stilprofil nur Schatten und Licht (seit 0.5.0-beta.13, A7): Der Glanzregler der Familie rechnete bis dahin
    /// bei jedem Schritt die Unschärfe jedes Dickglas-Schattens neu (Glint G3: 5 Schatten je Schritt, rund 20 ms).
    /// </summary>
    private protected override object Profilwerte(Stilprofil p) => (p.DickglasSchatten, p.DickglasLicht);

    private protected override void Nachgeaendert() => EffektSetzen();

    /// <summary>Weichzeichner mit einem Radius von einem Viertel der Höhe (gesperrt und ohne Schatten keiner).</summary>
    private void EffektSetzen()
    {
        var h = RenderSize.Height;
        if (h <= 0 || Schatten != Schattenstufe.Voll || Zustand == Glaszustand.Gesperrt)
        {
            if (Koerpereffekt is not null) KoerpereffektSetzen(null);
            return;
        }
        var radius = Math.Round(h * 0.26, 1);
        if (Koerpereffekt is BlurEffect alt && alt.Radius == radius) return;
        var neu = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        neu.Freeze();
        KoerpereffektSetzen(neu);
    }

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        if (Schatten != Schattenstufe.Voll || Zustand == Glaszustand.Gesperrt) return;
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var a = FarbeVon(Licht, Color.FromRgb(0x3B, 0x5B, 0xFF));
        var c = Zweitfarbe(Licht, Color.FromRgb(0xA0, 0x3D, 0xFF));
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        var weite = gedrueckt ? 0.7 : 1;
        var r = Math.Min(f.Radius, Math.Min(b, h) / 2);
        // Schlagschatten nach unten links.
        var schatten = Math.Clamp(p.DickglasSchatten, 0, 1);
        if (schatten > 0)
            Schicht(Rechteck(f, -0.42 * h * weite, 0.46 * h * weite, b, h * 0.8, r), Flaeche(Farbton.Deckend(SchattenFarbe, schatten)));
        // Farbiges Licht auf der Unterlage: ein breiter Fleck von links nach rechts, links ein kräftigerer.
        var glut = Math.Min(1, 1.1 * Math.Pow(Math.Clamp(p.DickglasLicht, 0, 2) * (Zustand == Glaszustand.Ueberfahren ? 1.2 : 1), 1.6));
        if (glut > 0)
        {
            var x = -0.32 * h * weite;
            var y = h * (0.62 + 0.1 * weite);
            Schicht(Rechteck(f, x, y, b * 0.96, h * 0.55, r * 0.6),
                Waagerecht(x / s, (x + b * 0.96) / s, (Farbton.Deckend(a, glut), 0.05), (Farbton.Deckend(c, glut), 0.95)));
            Schicht(Rechteck(f, -0.28 * h * weite, h * 0.5, b * 0.42, h * 0.62, r * 0.6), Flaeche(Farbton.Deckend(a, Math.Min(1, 0.8 * glut))));
        }
        // Kontaktschatten unter der Kante.
        Schicht(Rechteck(f, b * 0.04, h * 0.9, b * 0.92, h * 0.16, h * 0.08), Flaeche(Farbton.Deckend(Dunkler(SchattenFarbe, 0.3), 0.55)));
    }
}

