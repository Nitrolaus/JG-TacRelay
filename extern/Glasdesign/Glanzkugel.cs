// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Der Körper der Stilfamilie Glanzkugel (KONZEPT.md 5b.4, Vorbild 4: Glanzstil der Nullerjahre), meist innerhalb eines
/// breiten <see cref="Chromrand"/> (<see cref="Rand"/>):
/// <list type="bullet">
/// <item>Kuppel: dunkle Glaskuppel, großer Kuppelglanz oben, Fensterspiegelung als Bögen links, innen der Leuchtkern
/// (leuchtet immer, wie im Vorbild).</item>
/// <item>Kugel: satte Kugel in der Farbe des Knopfs (Background, Vorgabe Orange), Kuppelglanz und Fensterreflex.</item>
/// <item>Welle: dunkle Fläche mit Wellenglanz und schwarzem Rand (ohne Chromrand), auf jedem Umriss.</item>
/// <item>Rahmen: farbige Fläche mit Glut in der Mitte und Glanz oben, im Chromrahmen.</item>
/// <item>Bahn: dunkle Bahn eines Schiebers, oben grau, an einer harten Kante unten schwarz.</item>
/// </list>
/// Darunter ein flacher Bodenschatten (außerhalb der eigenen Grenzen). <see cref="Schrift"/> ist hell auf dunklem und
/// dunkel auf hellem Körper.
/// </summary>
public sealed class Kugelkoerper : Familienkoerper
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Kugelart), typeof(Kugelkoerper), new FrameworkPropertyMetadata(Kugelart.Kuppel, FrameworkPropertyMetadataOptions.AffectsRender, SchriftNeu));

    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Kugelkoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SchriftNeu));

    public static readonly DependencyProperty VorgabeFarbeProperty = Farbwert(nameof(VorgabeFarbe), Color.FromRgb(0xFF, 0x93, 0x00));
    public static readonly DependencyProperty KuppelFarbeProperty = Farbwert(nameof(KuppelFarbe), Color.FromRgb(0x1C, 0x1C, 0x1C));
    public static readonly DependencyProperty LeuchtkernFarbeProperty = Farbwert(nameof(LeuchtkernFarbe), Color.FromRgb(0xFF, 0xE0, 0x4E));

    /// <summary>Breite des Chromrands als Anteil der kürzeren Seite; 0: kein Rand (Welle und Bahn zeichnen dann ihre eigene Kante).</summary>
    public static readonly DependencyProperty RandProperty = DependencyProperty.Register(
        nameof(Rand), typeof(double), typeof(Kugelkoerper), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Den Bodenschatten zeichnen (Knöpfe ja, Knauf eines Schiebers nein).</summary>
    public static readonly DependencyProperty BodenProperty = DependencyProperty.Register(
        nameof(Boden), typeof(bool), typeof(Kugelkoerper), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyPropertyKey SchriftKey = DependencyProperty.RegisterReadOnly(
        nameof(Schrift), typeof(Brush), typeof(Kugelkoerper), new FrameworkPropertyMetadata(Brushes.White));

    public static readonly DependencyProperty SchriftProperty = SchriftKey.DependencyProperty;

    private static DependencyProperty Farbwert(string name, Color vorgabe) => DependencyProperty.Register(
        name, typeof(Color), typeof(Kugelkoerper), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender, SchriftNeu));

    public Kugelkoerper()
    {
        SetResourceReference(KuppelFarbeProperty, Schluessel.GlanzkugelKuppelFarbe);
        SetResourceReference(LeuchtkernFarbeProperty, Schluessel.GlanzkugelLeuchtkernFarbe);
    }

    public Kugelart Art { get => (Kugelart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Color VorgabeFarbe { get => (Color)GetValue(VorgabeFarbeProperty); set => SetValue(VorgabeFarbeProperty, value); }
    public Color KuppelFarbe { get => (Color)GetValue(KuppelFarbeProperty); set => SetValue(KuppelFarbeProperty, value); }
    public Color LeuchtkernFarbe { get => (Color)GetValue(LeuchtkernFarbeProperty); set => SetValue(LeuchtkernFarbeProperty, value); }
    public double Rand { get => (double)GetValue(RandProperty); set => SetValue(RandProperty, value); }
    public bool Boden { get => (bool)GetValue(BodenProperty); set => SetValue(BodenProperty, value); }

    /// <summary>
    /// Weiß oder fast Schwarz, was auf den Farben unter dem Inhalt (Mitte und Rand des Leuchtkerns, Licht und Farbe der
    /// Kugel, Glut und Fläche des Rahmens, Fläche und Glanz der Welle) an der schlechtesten Stelle besser lesbar ist.
    /// </summary>
    public Brush Schrift => (Brush)GetValue(SchriftProperty);

    /// <summary>Die wirksame Farbe von Kugel und Rahmen: <see cref="Farbe"/>, sonst <see cref="VorgabeFarbe"/>.</summary>
    public Color Grundfarbe => FarbeVon(Farbe, VorgabeFarbe);

    private static void SchriftNeu(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Kugelkoerper)d).SchriftSetzen();

    private protected override void Nachgeaendert() => SchriftSetzen();

    private void SchriftSetzen()
    {
        // Wie in Bauen: gesperrt gedämpft, der Leuchtkern dann nur noch schwach.
        var gesperrt = Zustand == Glaszustand.Gesperrt;
        var proben = Proben(Art, Wirksam, WirksameKuppel, LeuchtkernFarbe, Profil, Zustand, Grundfarbe);
        double Schlechteste(Color schrift) => proben.Min(g => Farbton.Kontrast(schrift, g));
        var dunkel = Color.FromRgb(0x0B, 0x0D, 0x10);
        var farbe = Schlechteste(Colors.White) >= Schlechteste(dunkel) ? Colors.White : dunkel;
        if (gesperrt) farbe = Farbton.Mischen(farbe, proben[0], 0.25);
        if (Schrift is not SolidColorBrush alt || alt.Color != farbe) SetValue(SchriftKey, Flaeche(farbe));
        SchriftWeitergeben(Schrift);
    }

    /// <summary>
    /// Die Farben unter dem Inhalt je Art im Zustand: Mitte und Rand des Leuchtkerns, Licht und Farbe der Kugel, Glut und Fläche
    /// des Rahmens, Fläche und Glanz der Welle. <paramref name="gewaehlt"/> ist die gewählte Grundfarbe vor der Abstimmung; aus ihr
    /// kommt der Ton der Glut im Rahmen.
    /// </summary>
    private static IReadOnlyList<Color> Proben(Kugelart art, Color grund, Color kuppel, Color leuchtkern, Stilprofil p, Glaszustand zustand, Color gewaehlt)
    {
        var gesperrt = zustand == Glaszustand.Gesperrt;
        // Wie Bauen: Kugel, Rahmen und Welle im Zustand heller oder dunkler (seit 0.5.0-beta.13 auch hier; gedrückt fiel die Schrift
        // auf einer frei gewählten Farbe sonst unter 4,5 zu 1).
        var c = gesperrt ? Gesperrt(grund) : art is Kugelart.Kugel or Kugelart.Rahmen ? Zustandsfarbe(grund, p, zustand) : grund;
        var k = gesperrt ? Gesperrt(kuppel) : art == Kugelart.Welle ? Zustandsfarbe(kuppel, p, zustand) : kuppel;
        switch (art)
        {
            case Kugelart.Kuppel:
            {
                var kern = Math.Clamp(p.GlanzkugelLeuchtkern * (gesperrt ? 0.2 : 1), 0, 1);
                return kern <= 0 ? [k] : [Farbton.Mischen(k, leuchtkern, kern), Farbton.Mischen(k, Kernring(leuchtkern), 0.95 * kern)];
            }
            case Kugelart.Kugel:
            {
                var waerme = Math.Clamp((c.R - c.B) / 255.0, 0, 1);
                var licht = Farbton.Mischen(Heller(c, 0.5), Color.FromRgb(0xFF, 0xF0, 0x7A), 0.6 * waerme);
                return [c, Farbton.Mischen(c, licht, 0.45), licht];
            }
            case Kugelart.Rahmen:
            {
                var hoch = Math.Max(c.R, Math.Max(c.G, c.B));
                var satt = hoch > 0 ? (hoch - Math.Min(c.R, Math.Min(c.G, c.B))) / (double)hoch : 0;
                return [c, Heller(c, 0.3), Farbton.Mischen(Heller(c, 0.5), Rahmenglut(gewaehlt), 0.75 * satt)];
            }
            case Kugelart.Welle:
            {
                // Die Fläche von Heller(k, 0,12) oben zu Dunkler(k, 0,5) unten, die Schrift von 35 bis 65 Prozent der Höhe; darüber
                // der Wellenglanz (seit 0.5.0-beta.13 so gerechnet, wie Bauen ihn zeichnet; bis beta.12 nur k und Heller(k, 0,35)).
                Color Flaeche(double y) => Farbton.Mischen(Heller(k, 0.12), Dunkler(k, 0.5), y);
                var g = Math.Min(1, p.GlanzkugelKuppelglanz * (zustand switch { Glaszustand.Gedrueckt => 0.75, Glaszustand.Ueberfahren => 1.1, _ => 1 }) * (gesperrt ? 0.6 : 1));
                var glanz = Schriftwahl.Ueber(Farbton.Deckend(Farbton.Mischen(Heller(k, 0.5), Heller(k, 0.26), 0.35 / 0.6), g), Flaeche(0.35));
                // k zuerst: Gesperrt mischt die Schrift mit der ersten Probe.
                return [k, glanz, Flaeche(0.35), Flaeche(0.5), Flaeche(0.65)];
            }
            default:
                return [k, Heller(k, 0.35)];
        }
    }

    /// <summary>
    /// Die Farbe von Kugel und Rahmen, abgestimmt (seit 0.5.0-beta.13, <see cref="Schriftwahl.Abstimmung"/>): Hält auf einer frei
    /// gewählten Farbe aus dem Mittelbereich weder Weiß noch fast Schwarz auf Farbe, Licht und Glut zugleich, wird sie so wenig
    /// wie nötig abgedunkelt (oder aufgehellt), bis eine hält; die Vorgabe bleibt, wie sie ist. Kuppel und Welle: unverändert.
    /// </summary>
    private Color Wirksam
    {
        get
        {
            var g = Grundfarbe;
            if (Art is not (Kugelart.Kugel or Kugelart.Rahmen)) return g;
            var (art, k, l, p) = (Art, KuppelFarbe, LeuchtkernFarbe, Profil);
            return Schriftwahl.Abgestimmt(g, Abstimmen((g, art, p), g, (t, z) => Proben(art, Schriftwahl.Abgestimmt(g, t), k, l, p, z, g)));
        }
    }

    /// <summary>Die Kuppelfarbe der Welle (Glas.Glanzkugel.Kuppel.Farbe), abgestimmt wie <see cref="Wirksam"/>; die übrigen Arten nehmen sie, wie sie ist.</summary>
    private Color WirksameKuppel
    {
        get
        {
            var k = KuppelFarbe;
            if (Art != Kugelart.Welle) return k;
            var (g, l, p) = (Grundfarbe, LeuchtkernFarbe, Profil);
            return Schriftwahl.Abgestimmt(k, Abstimmen((k, Kugelart.Welle, p), k, (t, z) => Proben(Kugelart.Welle, g, Schriftwahl.Abgestimmt(k, t), l, p, z, g)));
        }
    }

    private protected override object Teilschluessel() => (Art, Grundfarbe, KuppelFarbe, LeuchtkernFarbe, Rand, Boden);

    private static Color Gesperrt(Color c) => Farbton.Mischen(c, Color.FromRgb(0x6E, 0x70, 0x73), 0.65);

    /// <summary>Vorgabe des Leuchtkerns (Glas.Glanzkugel.Leuchtkern.Farbe in Standard), zu der Ring und Saum abgeleitet sind.</summary>
    private static readonly Color VorgabeKern = Color.FromRgb(0xFF, 0xE0, 0x4E);

    /// <summary>
    /// Ring und Saum des Leuchtkerns folgen seiner Farbe (0.5.0-beta.7, <see cref="Farbableitung"/>): um denselben Abstand
    /// wärmer und dunkler wie bei der Vorgabe (bis beta.6 zu 45 und 70 Prozent feste Orange- und Brauntöne beigemischt).
    /// </summary>
    private static Color Kernring(Color l) => Farbableitung.Ableiten(l, VorgabeKern, Farbton.Mischen(VorgabeKern, Color.FromRgb(0xF0, 0x90, 0x1A), 0.45));

    private static Color Kernsaum(Color l) => Farbableitung.Ableiten(l, VorgabeKern, Farbton.Mischen(VorgabeKern, Color.FromRgb(0x90, 0x50, 0x08), 0.7));

    /// <summary>Vorgabe von Kugel und Rahmen (Glas.Glanzkugel.Kugel in Standard) und das Gelb ihrer Glut.</summary>
    private static readonly Color VorgabeKugel = Color.FromRgb(0xFF, 0x93, 0x00);

    private static readonly Color VorgabeGlut = Color.FromRgb(0xFF, 0xEE, 0x5A);

    /// <summary>
    /// Wohin die Glut in der Mitte des Rahmens mischt (0.5.0-beta.7): bei der satten Farbe des Rahmens deren helle Glut, aus
    /// ihr abgeleitet wie das Gelb #FFEE5A aus dem Orange der Vorgabe (<see cref="Farbableitung"/>; bei Blau ein helles Blau
    /// statt Gelb), um so mehr, je satter die Farbe ist; bei Grau bleibt das Gelb der Vorgabe (dort mischt die Glut ohnehin
    /// kaum, ihr Anteil ist 0,75 mal die Sättigung). Die Vorgabe ergibt in jedem Zustand genau #FFEE5A wie bis beta.6, ebenso
    /// die fast graue Silberbahn.
    /// </summary>
    private static Color Rahmenglut(Color grund)
    {
        var hoch = Math.Max(grund.R, Math.Max(grund.G, grund.B));
        var satt = hoch > 0 ? (hoch - Math.Min(grund.R, Math.Min(grund.G, grund.B))) / (double)hoch : 0;
        return Farbton.Mischen(VorgabeGlut, Farbableitung.Ableiten(grund, VorgabeKugel, VorgabeGlut), satt);
    }

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        var kurz = Math.Min(b, h);

        // Bodenschatten: flache dunkle Ellipse unter dem Körper.
        if (Boden && Schatten == Schattenstufe.Voll && aktiv && p.GlanzkugelSchatten > 0)
        {
            var d = p.GlanzkugelSchatten * (gedrueckt ? 0.7 : 1);
            var bx = b / 2;
            var by = h + kurz * 0.06;
            Schicht(Ellipse(f, bx, by, b * 0.34, kurz * 0.07), Radial(f.Dip(bx, by), b * 0.34 / s, kurz * 0.07 / s, (Schwarz(d), 0), (Schwarz(d * 0.5), 0.55), (Schwarz(0), 1)));
        }

        var rand = Rand > 0 ? Math.Max(1, Math.Round(kurz * Math.Clamp(Rand, 0, 0.5))) - 1 : 0;
        var iw = b - 2 * rand;
        var ih = h - 2 * rand;
        if (iw < 4 || ih < 4) return;
        var form = f.Form(rand);
        var ox = rand;
        var oy = rand;
        Point P(double x, double y) => f.Dip(ox + x * iw, oy + y * ih);

        switch (Art)
        {
            case Kugelart.Kuppel:
            {
                var k = aktiv ? KuppelFarbe : Gesperrt(KuppelFarbe);
                Schicht(form, Radial(P(0.5, 0.55), iw * 0.55 / s, ih * 0.55 / s, (k, 0), (Dunkler(k, 0.55), 0.7), (Dunkler(k, 1), 1)));
                var kern = p.GlanzkugelLeuchtkern * Zustand switch
                {
                    Glaszustand.Ueberfahren => 1.15, Glaszustand.Gedrueckt => 0.8, Glaszustand.Gesperrt => 0.2, _ => 1,
                };
                if (kern > 0)
                {
                    var l = LeuchtkernFarbe;
                    var mitte = Farbton.Mischen(l, Colors.White, Math.Max(0, kern - 1) * 1.5);
                    Schicht(form, Radial(P(0.5, 0.60), iw * 0.44 / s, ih * 0.40 / s,
                        (Farbton.Deckend(mitte, Math.Min(1, kern)), 0), (Farbton.Deckend(Kernring(l), Math.Min(1, 0.95 * kern)), 0.35),
                        (Farbton.Deckend(Kernsaum(l), Math.Min(1, 0.5 * kern)), 0.68), (Farbton.Deckend(l, 0), 1)));
                }
                Kuppelglanz(f, P, iw, ih, p.GlanzkugelKuppelglanz * (gedrueckt ? 0.75 : 1) * (aktiv ? 1 : 0.6), s);
                if (aktiv && p.GlanzkugelFenster > 0) Fensterboegen(f, rand, iw, p.GlanzkugelFenster);
                Schicht(f.Ring(rand, rand + Math.Max(1, Glasgeometrie.Raster(1.5, s))), Flaeche(Schwarz(0.85)));
                break;
            }
            case Kugelart.Kugel:
            {
                var c = aktiv ? Zustandsfarbe(Wirksam, p) : Gesperrt(Wirksam);
                // Das Licht in der Kugel: heller, bei warmen Farben zum Gelb hin (Orange wird innen gelb wie im Vorbild).
                var waerme = Math.Clamp((c.R - c.B) / 255.0, 0, 1);
                var licht = Farbton.Mischen(Heller(c, 0.5), Color.FromRgb(0xFF, 0xF0, 0x7A), 0.6 * waerme);
                Schicht(form, Radial(P(0.5, 0.72), iw * 0.62 / s, ih * 0.62 / s,
                    (licht, 0), (Farbton.Mischen(c, licht, 0.45), 0.38), (c, 0.7), (Dunkler(c, 0.06), 1)));
                Schicht(f.Ring(rand, rand + Math.Max(1, Math.Round(iw * 0.04))), Senkrecht((oy) / s, (oy + ih) / s, (Farbton.Deckend(Dunkler(c, 0.4), 0.25), 0), (Farbton.Deckend(c, 0), 1)));
                Kuppelglanz(f, P, iw, ih, p.GlanzkugelKuppelglanz * 0.6 * (gedrueckt ? 0.75 : 1) * (aktiv ? 1 : 0.6), s, kugel: true);
                if (aktiv && p.GlanzkugelFenster > 0) Fensterreflex(f, ox + iw * 0.17, oy + ih * 0.12, iw * 0.22, p.GlanzkugelFenster);
                break;
            }
            case Kugelart.Rahmen:
            {
                var c = aktiv ? Zustandsfarbe(Wirksam, p) : Gesperrt(Wirksam);
                Schicht(form, Senkrecht(oy / s, (oy + ih) / s, (Heller(c, 0.3), 0), (c, 0.5), (Dunkler(c, 0.08), 1)));
                // Glut in der Mitte: bei satter Farbe zu deren heller Glut (Rahmenglut, bei Orange das Gelb des Vorbilds), bei
                // Grau und Weiß nur heller. Der Ton der Glut kommt aus der gewählten Farbe, nicht aus der abgestimmten (seit
                // 0.5.0-beta.13): Hellt die Abstimmung ein Blau für dunkle Schrift auf, bleibt die Glut blau statt türkis.
                var satt = Math.Max(c.R, Math.Max(c.G, c.B)) is var hoch and > 0 ? (hoch - Math.Min(c.R, Math.Min(c.G, c.B))) / (double)hoch : 0;
                var glut = Farbton.Mischen(Heller(c, 0.5), Rahmenglut(Grundfarbe), 0.75 * satt);
                Schicht(form, Radial(P(0.55, 0.62), iw * 0.42 / s, ih * 0.36 / s, (glut, 0), (Farbton.Deckend(c, 0), 1)));
                var band = Math.Round(ih * 0.45);
                var g = p.GlanzkugelKuppelglanz * (gedrueckt ? 0.5 : 0.65) * (aktiv ? 1 : 0.5);
                Schicht(Rechteck(f, ox, oy, iw, band, Math.Max(0, f.Radius - rand)), Senkrecht(oy / s, (oy + band) / s, (Weiss(g), 0), (Weiss(g * 0.25), 1)));
                Schicht(f.Ring(rand, rand + Glasgeometrie.Haarlinie), Senkrecht(oy / s, (oy + ih) / s, (Schwarz(0.5), 0), (Schwarz(0.2), 1)));
                break;
            }
            case Kugelart.Bahn:
            {
                var k = aktiv ? KuppelFarbe : Gesperrt(KuppelFarbe);
                Schicht(form, Senkrecht(oy / s, (oy + ih) / s, (Heller(k, 0.2), 0), (Heller(k, 0.06), 0.49), (Dunkler(k, 0.9), 0.5), (Dunkler(k, 1), 1)));
                Schicht(f.Ring(0, Math.Max(1, Glasgeometrie.Raster(1.5, s))), Flaeche(Schwarz(0.9)));
                break;
            }
            default:
            {
                // Welle: dunkle Fläche, oben ein Glanz mit welliger Grenze, schwarzer Rand (ohne Chromrand).
                var k = aktiv ? Zustandsfarbe(WirksameKuppel, p) : Gesperrt(WirksameKuppel);
                Schicht(form, Senkrecht(oy / s, (oy + ih) / s, (Heller(k, 0.12), 0), (Dunkler(k, 0.5), 1)));
                var g = p.GlanzkugelKuppelglanz * (gedrueckt ? 0.75 : Zustand == Glaszustand.Ueberfahren ? 1.1 : 1) * (aktiv ? 1 : 0.6);
                var welle = new StreamGeometry();
                using (var z = welle.Open())
                {
                    const double Lage = 0.56;
                    z.BeginFigure(P(-0.02, -0.02), true, true);
                    z.LineTo(P(1.02, -0.02), false, false);
                    z.LineTo(P(1.02, Lage - 0.08), false, false);
                    z.BezierTo(P(0.72, Lage - 0.22), P(0.38, Lage + 0.12), P(-0.02, Lage - 0.02), false, false);
                }
                welle.Freeze();
                var geschnitten = Geometry.Combine(form, welle, GeometryCombineMode.Intersect, null, 0.05 / s, ToleranceType.Absolute);
                Schicht(geschnitten, Senkrecht(oy / s, (oy + ih * 0.6) / s, (Farbton.Deckend(Heller(k, 0.5), Math.Min(1, g)), 0), (Farbton.Deckend(Heller(k, 0.26), Math.Min(1, g)), 1)));
                if (Rand <= 0) Schicht(f.Ring(0, Glasgeometrie.Linie(2.5, s)), Flaeche(Color.FromRgb(0x05, 0x05, 0x05)));
                break;
            }
        }
    }

    /// <summary>Großer Kuppelglanz: Ellipse oben, weiß nach unten auslaufend.</summary>
    private void Kuppelglanz(Glasflaeche f, Func<double, double, Point> p, double iw, double ih, double staerke, double s, bool kugel = false)
    {
        if (staerke <= 0) return;
        var breite = iw * (kugel ? 0.84 : 0.66);
        var hoehe = ih * (kugel ? 0.52 : 0.44);
        var oben = p(0.5, kugel ? 0.03 : 0.05);
        var g = new EllipseGeometry(new Point(oben.X, oben.Y + hoehe / 2 / s), breite / 2 / s, hoehe / 2 / s);
        g.Freeze();
        Schicht(g, Senkrecht(oben.Y, oben.Y + hoehe / s, (Weiss(staerke), 0), (Weiss(staerke * 0.3), 0.65), (Weiss(0), 1)));
    }

    /// <summary>Fensterspiegelung der Kuppel: drei Bögen am linken Rand.</summary>
    private void Fensterboegen(Glasflaeche f, double rand, double d, double staerke)
    {
        var r = d / 2;
        var mx = rand + r;
        var my = rand + r;
        void Bogen(double radius, double von, double bis, double dicke, double deckung)
        {
            Point P(double w) => f.Dip(mx + radius * Math.Cos(w * Math.PI / 180), my + radius * Math.Sin(w * Math.PI / 180));
            var g = new StreamGeometry();
            using (var z = g.Open())
            {
                z.BeginFigure(P(von), false, false);
                z.ArcTo(P(bis), new Size(radius / f.Skala, radius / f.Skala), 0, false, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            Schicht(g, null, new Pen(Flaeche(Weiss(deckung * staerke)), Math.Max(1, d * dicke) / f.Skala) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        }
        Bogen(r * 0.90, 150, 215, 0.035, 0.85);
        Bogen(r * 0.80, 160, 200, 0.018, 0.6);
        Bogen(r * 0.93, 112, 135, 0.015, 0.5);
    }

    /// <summary>Fensterreflex der Kugel: vier kleine Scheiben, leicht schräg.</summary>
    private void Fensterreflex(Glasflaeche f, double x, double y, double groesse, double staerke)
    {
        var s = f.Skala;
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        var teil = groesse / 2 - groesse * 0.04;
        foreach (var (dx, dy, r) in new[] { (0.0, 0.0, 0.3), (groesse - teil, 0.0, 0.2), (0.0, groesse - teil, 0.2), (groesse - teil, groesse - teil, 0.2) })
            g.Children.Add(new RectangleGeometry(new Rect((x + dx) / s, (y + dy) / s, teil / s, teil / s), teil * r / s, teil * r / s));
        var mitte = new Point((x + groesse / 2) / s, (y + groesse / 2) / s);
        g.Transform = new TransformGroup { Children = { new SkewTransform(-14, 6, mitte.X, mitte.Y), new RotateTransform(-28, mitte.X, mitte.Y) } };
        g.Freeze();
        Schicht(g, Senkrecht(y / s, (y + groesse) / s, (Weiss(0.9 * staerke), 0), (Weiss(0.4 * staerke), 1)));
    }
}
