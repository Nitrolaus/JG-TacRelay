// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glasdesign;

/// <summary>
/// Die Füllung eines Knopfs der Stilfamilie Chromrahmen (KONZEPT.md 5b.6, Vorbild 6), innerhalb des
/// <see cref="Chromrand"/> mit Lippe: hell, gebürstetes Silber (je Gerätepixelzeile ein eigener Grauwert), schwarz oder
/// Rauchglas in drei Stufen, durch das der Grund scheint. Oben ein Glanzstreif, unten ein Hauch Schatten, am Rand zum
/// Rahmen eine Rille. Überfahren heller und mit einem Funken am linken Rand, gedrückt dunkler mit Schatten oben, gesperrt
/// flach. <see cref="Schrift"/> wählt die Füllung: dunkel auf Hell, Silber und hellem Rauch, sonst hell.
/// </summary>
public sealed class Chromrahmenfuellung : Familienkoerper
{
    public static readonly DependencyProperty FuellungProperty = DependencyProperty.Register(
        nameof(Fuellung), typeof(Fuellung), typeof(Chromrahmenfuellung), new FrameworkPropertyMetadata(Fuellung.Hell, FrameworkPropertyMetadataOptions.AffectsRender, SchriftNeu));

    /// <summary>Breite des Rahmens als Anteil der kürzeren Seite der Form (ohne Lippe): so weit innen liegt die Füllung.</summary>
    public static readonly DependencyProperty RahmenProperty = DependencyProperty.Register(
        nameof(Rahmen), typeof(double), typeof(Chromrahmenfuellung), new FrameworkPropertyMetadata(0.15, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Höhe der Lippe in DIP (wie am Chromrand); die Füllung sitzt darüber.</summary>
    public static readonly DependencyProperty LippeProperty = DependencyProperty.Register(
        nameof(Lippe), typeof(double), typeof(Chromrahmenfuellung), new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender, SchriftNeu));

    private static readonly DependencyPropertyKey InhaltsrandKey = DependencyProperty.RegisterReadOnly(
        nameof(Inhaltsrand), typeof(Thickness), typeof(Chromrahmenfuellung), new FrameworkPropertyMetadata(new Thickness()));

    /// <summary>Wo der Inhalt liegt: über der Lippe (unten um sie eingerückt).</summary>
    public static readonly DependencyProperty InhaltsrandProperty = InhaltsrandKey.DependencyProperty;

    public Thickness Inhaltsrand => (Thickness)GetValue(InhaltsrandProperty);

    public static readonly DependencyProperty HellFarbeProperty = Farbwert(nameof(HellFarbe), Color.FromRgb(0xED, 0xED, 0xED));
    public static readonly DependencyProperty SilberFarbeProperty = Farbwert(nameof(SilberFarbe), Color.FromRgb(0xC6, 0xC6, 0xC8));
    public static readonly DependencyProperty SchwarzFarbeProperty = Farbwert(nameof(SchwarzFarbe), Color.FromRgb(0x14, 0x14, 0x15));
    public static readonly DependencyProperty RauchFarbeProperty = Farbwert(nameof(RauchFarbe), Color.FromRgb(0x6A, 0x6C, 0x70));

    public static readonly DependencyProperty SchriftDunkelProperty = Pinselwert(nameof(SchriftDunkel));
    public static readonly DependencyProperty SchriftHellProperty = Pinselwert(nameof(SchriftHell));

    private static readonly DependencyPropertyKey SchriftKey = DependencyProperty.RegisterReadOnly(
        nameof(Schrift), typeof(Brush), typeof(Chromrahmenfuellung), new FrameworkPropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty SchriftProperty = SchriftKey.DependencyProperty;

    private static DependencyProperty Farbwert(string name, Color vorgabe) => DependencyProperty.Register(
        name, typeof(Color), typeof(Chromrahmenfuellung), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Pinselwert(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Chromrahmenfuellung), new FrameworkPropertyMetadata(null, SchriftNeu));

    public Chromrahmenfuellung()
    {
        SetResourceReference(HellFarbeProperty, Schluessel.ChromrahmenHellFarbe);
        SetResourceReference(SilberFarbeProperty, Schluessel.ChromrahmenSilberFarbe);
        SetResourceReference(SchwarzFarbeProperty, Schluessel.ChromrahmenSchwarzFarbe);
        SetResourceReference(RauchFarbeProperty, Schluessel.ChromrahmenRauchFarbe);
        SetResourceReference(SchriftDunkelProperty, Schluessel.ChromrahmenSchriftDunkel);
        SetResourceReference(SchriftHellProperty, Schluessel.ChromrahmenSchriftHell);
    }

    public Fuellung Fuellung { get => (Fuellung)GetValue(FuellungProperty); set => SetValue(FuellungProperty, value); }
    public double Rahmen { get => (double)GetValue(RahmenProperty); set => SetValue(RahmenProperty, value); }
    public double Lippe { get => (double)GetValue(LippeProperty); set => SetValue(LippeProperty, value); }
    public Color HellFarbe { get => (Color)GetValue(HellFarbeProperty); set => SetValue(HellFarbeProperty, value); }
    public Color SilberFarbe { get => (Color)GetValue(SilberFarbeProperty); set => SetValue(SilberFarbeProperty, value); }
    public Color SchwarzFarbe { get => (Color)GetValue(SchwarzFarbeProperty); set => SetValue(SchwarzFarbeProperty, value); }
    public Color RauchFarbe { get => (Color)GetValue(RauchFarbeProperty); set => SetValue(RauchFarbeProperty, value); }
    public Brush? SchriftDunkel { get => (Brush?)GetValue(SchriftDunkelProperty); set => SetValue(SchriftDunkelProperty, value); }
    public Brush? SchriftHell { get => (Brush?)GetValue(SchriftHellProperty); set => SetValue(SchriftHellProperty, value); }

    /// <summary>Die Schrift für diese Füllung: dunkel auf Hell, Silber und hellem Rauch, sonst hell; gesperrt gedämpft.</summary>
    public Brush Schrift => (Brush)GetValue(SchriftProperty);

    /// <summary>Ob die Füllung hell ist (dunkle Schrift).</summary>
    public bool HelleFuellung => Fuellung is Fuellung.Hell or Fuellung.Silber or Fuellung.RauchHell;

    private static void SchriftNeu(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Chromrahmenfuellung)d).SchriftSetzen();

    private protected override void Nachgeaendert() => SchriftSetzen();

    private protected override void EigeneSchriftwahl() => SchriftSetzen();

    private void SchriftSetzen()
    {
        var gesperrt = Zustand == Glaszustand.Gesperrt;
        var pinsel = (HelleFuellung ? SchriftDunkel : SchriftHell) ?? (HelleFuellung ? Brushes.Black : Brushes.White);
        // Gesperrt zum Grau hin statt durchsichtig: So hält die Schrift auf jeder Füllung 3 zu 1.
        if (gesperrt && pinsel is SolidColorBrush voll)
        {
            var farbe = Farbton.Mischen(voll.Color, Color.FromRgb(0x80, 0x82, 0x85), 0.15);
            pinsel = Schrift is SolidColorBrush alt && alt.Color == farbe ? alt : Flaeche(farbe);
        }
        // Schriftwahl (0.5.0-beta.7): Hält die Schrift der Füllungsart auf den Farben der Füllung (frei wählbar) die Grenze
        // nicht, nimmt die Füllung die Kontrastschrift.
        pinsel = Schriftwahl.Waehlen(pinsel, Flaechenfarben(), gesperrt);
        if (!ReferenceEquals(Schrift, pinsel)) SetValue(SchriftKey, pinsel);
        SchriftWeitergeben(Schrift);
        var s = Glasgeometrie.Skala(this);
        var rand = new Thickness(0, 0, 0, Lippe > 0 ? Glasgeometrie.Linie(Lippe, s) / s : 0);
        if (Inhaltsrand != rand) SetValue(InhaltsrandKey, rand);
    }

    private protected override object Teilschluessel() => (Fuellung, Rahmen, Lippe, HellFarbe, SilberFarbe, SchwarzFarbe, RauchFarbe);

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        var lippe = Lippe > 0 ? Glasgeometrie.Linie(Lippe, s) : 0;
        var h = f.Hoehe - lippe;
        var b = f.Breite;
        if (h < 6) return;
        var form = Flaeche(b, h, s);
        var rahmen = Math.Max(1, Math.Round(Math.Min(b, h) * Math.Clamp(Rahmen, 0, 0.5)));
        var innen = Math.Max(0, rahmen - 1);
        var ih = h - 2 * innen;
        if (ih < 4) return;
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;

        // Schatten unter dem Rahmen (außen, vor allem unten); bei Rauchglas schwach, damit der Grund durchscheint.
        if (Schatten == Schattenstufe.Voll && aktiv && p.ChromrahmenSchatten > 0)
        {
            var rauch = Fuellung is Fuellung.Rauch or Fuellung.RauchDunkel;
            var d = p.ChromrahmenSchatten * (rauch ? 0.55 : 1) * (gedrueckt ? 0.6 : 1);
            Schicht(Ellipse(f, b / 2, h * 0.92 + lippe, b * 0.52, h * 0.42),
                Radial(f.Dip(b / 2, h * 0.92 + lippe), b * 0.52 / s, h * 0.42 / s, (Schwarz(d), 0), (Schwarz(d * 0.5), 0.6), (Schwarz(0), 1)));
        }

        var flaeche = form.Form(innen);
        var oben = innen / s;
        var unten = (h - innen) / s;
        Schicht(flaeche, Fuellpinsel(Fuellung, oben, unten, (int)ih, p, s));

        // Glanzstreif oben: in die Rundung eingerückt, 38 Prozent hoch.
        var glanz = p.ChromrahmenFuellglanz * Fuellung switch
        {
            Fuellung.Hell => 1.0, Fuellung.Silber => 0.6, Fuellung.Schwarz => 0.25, Fuellung.RauchHell => 0.5, _ => 0.18,
        } * (gedrueckt ? 0.5 : 1) * (aktiv ? 1 : 0.5);
        var ir = Math.Max(0, Math.Min(form.Radius, Math.Min(b, h) / 2) - innen);
        if (glanz > 0)
        {
            var hoch = Math.Max(2, Math.Round(ih * 0.38));
            var einzug = Math.Round(ir * 0.5);
            var y = innen + Math.Max(1, Math.Round(ih * 0.05));
            Schicht(Rechteck(form, innen + einzug, y, b - 2 * (innen + einzug), hoch, Math.Min(hoch, ir)),
                Senkrecht(y / s, (y + hoch) / s, (Weiss(glanz), 0), (Weiss(0), 1)));
        }
        // Unten ein Hauch Schatten; gedrückt zusätzlich oben.
        Schicht(flaeche, Senkrecht(oben, unten, (Schwarz(gedrueckt ? 0.3 : 0), 0), (Schwarz(0), gedrueckt ? 0.35 : 0.6), (Schwarz(0.2), 1)));
        // Rille zwischen Rahmen und Füllung: oben dunkel, unten ein Hauch Licht.
        var rille = Math.Clamp(p.ChromrahmenRille, 0, 1);
        if (rille > 0)
            Schicht(form.Ring(innen, innen + Glasgeometrie.Haarlinie), Senkrecht(oben, unten, (Schwarz(rille), 0), (Schwarz(rille * 0.5), 0.5), (Weiss(rille * 0.36), 1)));

        // Funke am linken Rand beim Überfahren.
        if (Zustand == Glaszustand.Ueberfahren && p.ChromrahmenFunke > 0)
            Funke(form, Math.Round(rahmen * 0.4), Math.Round(h * 0.32), Math.Round(h * 0.55), p.ChromrahmenFunke);
    }

    /// <summary>
    /// Schriftwahl (0.5.0-beta.7): die Farben der Füllung im Schriftband, wie <see cref="Fuellpinsel"/> sie zeichnet, samt
    /// Schatten unten (gedrückt oben). Rauchglas ist durchsichtig: gerechnet auf dem dunklen Grund, für den die Familie
    /// gemacht ist; Silber ohne das Rauschen der Bürstung.
    /// </summary>
    private protected override IReadOnlyList<Color> Flaechenfarben()
    {
        var p = Profil;
        Color Z(Color c) => Zustand == Glaszustand.Gesperrt ? Farbton.Mischen(c, Color.FromArgb(c.A, 0x80, 0x82, 0x85), 0.5) : Zustandsfarbe(c, p);
        var rauch = Math.Clamp(p.ChromrahmenRauch, 0, 2);
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        Color Fuellfarbe(double t)
        {
            switch (Fuellung)
            {
                case Fuellung.Silber:
                {
                    var c = Z(SilberFarbe);
                    var licht = 1.08 - 0.24 * t + 0.09 * Math.Exp(-Math.Pow((t - 0.25) / 0.12, 2));
                    byte K(byte v) => (byte)Math.Clamp(v * licht, 0, 255);
                    return Color.FromRgb(K(c.R), K(c.G), (byte)Math.Min(255, K(c.B) + 3));
                }
                case Fuellung.Schwarz:
                {
                    var c = Z(SchwarzFarbe);
                    return Verlaufsfarbe(t, (Heller(c, 0.11), 0), (c, 0.55), (Dunkler(c, 0.7), 1));
                }
                case Fuellung.RauchHell:
                {
                    var c = Z(RauchFarbe);
                    return Schriftwahl.Ueber(Verlaufsfarbe(t, (Farbton.Deckend(Heller(c, 0.66), Math.Min(1, 0.96 * rauch)), 0),
                        (Farbton.Deckend(Heller(c, 0.56), Math.Min(1, 0.94 * rauch)), 0.5), (Farbton.Deckend(Heller(c, 0.42), Math.Min(1, 0.92 * rauch)), 1)), Rechengrund);
                }
                case Fuellung.Rauch:
                {
                    var c = Z(RauchFarbe);
                    return Schriftwahl.Ueber(Verlaufsfarbe(t, (Farbton.Deckend(c, Math.Min(1, 0.26 * rauch)), 0), (Farbton.Deckend(Dunkler(c, 0.25), Math.Min(1, 0.22 * rauch)), 1)), Rechengrund);
                }
                case Fuellung.RauchDunkel:
                {
                    var c = Z(RauchFarbe);
                    return Schriftwahl.Ueber(Verlaufsfarbe(t, (Farbton.Deckend(Dunkler(c, 0.75), Math.Min(1, 0.38 * rauch)), 0), (Farbton.Deckend(Dunkler(c, 0.85), Math.Min(1, 0.46 * rauch)), 1)), Rechengrund);
                }
                default:
                {
                    var c = Z(HellFarbe);
                    return Verlaufsfarbe(t, (Heller(c, 0.4), 0), (c, 0.5), (Dunkler(c, 0.1), 1));
                }
            }
        }
        return Band(t => Schriftwahl.Ueber(Verlaufsfarbe(t, (Schwarz(gedrueckt ? 0.3 : 0), 0), (Schwarz(0), gedrueckt ? 0.35 : 0.6), (Schwarz(0.2), 1)), Fuellfarbe(t)));
    }

    /// <summary>Der Pinsel der Füllung im Zustand; Silber als gebürstetes Bild.</summary>
    private Brush Fuellpinsel(Fuellung art, double oben, double unten, int hoehePx, Stilprofil p, double s)
    {
        Color Z(Color c) => Zustand switch
        {
            Glaszustand.Gesperrt => Farbton.Mischen(c, Color.FromArgb(c.A, 0x80, 0x82, 0x85), 0.5),
            _ => Zustandsfarbe(c, p),
        };
        var rauch = Math.Clamp(p.ChromrahmenRauch, 0, 2);
        switch (art)
        {
            case Fuellung.Silber:
                return Gebuerstet(Z(SilberFarbe), hoehePx, s);
            case Fuellung.Schwarz:
            {
                var c = Z(SchwarzFarbe);
                return Senkrecht(oben, unten, (Heller(c, 0.11), 0), (c, 0.55), (Dunkler(c, 0.7), 1));
            }
            case Fuellung.RauchHell:
            {
                var c = Z(RauchFarbe);
                return Senkrecht(oben, unten, (Farbton.Deckend(Heller(c, 0.66), Math.Min(1, 0.96 * rauch)), 0),
                    (Farbton.Deckend(Heller(c, 0.56), Math.Min(1, 0.94 * rauch)), 0.5), (Farbton.Deckend(Heller(c, 0.42), Math.Min(1, 0.92 * rauch)), 1));
            }
            case Fuellung.Rauch:
            {
                var c = Z(RauchFarbe);
                return Senkrecht(oben, unten, (Farbton.Deckend(c, Math.Min(1, 0.26 * rauch)), 0), (Farbton.Deckend(Dunkler(c, 0.25), Math.Min(1, 0.22 * rauch)), 1));
            }
            case Fuellung.RauchDunkel:
            {
                var c = Z(RauchFarbe);
                return Senkrecht(oben, unten, (Farbton.Deckend(Dunkler(c, 0.75), Math.Min(1, 0.38 * rauch)), 0), (Farbton.Deckend(Dunkler(c, 0.85), Math.Min(1, 0.46 * rauch)), 1));
            }
            default:
            {
                var c = Z(HellFarbe);
                return Senkrecht(oben, unten, (Heller(c, 0.4), 0), (c, 0.5), (Dunkler(c, 0.1), 1));
            }
        }
    }

    [ThreadStatic]
    private static Dictionary<(Color, int, double), ImageBrush>? _gebuerstet;

    /// <summary>
    /// Gebürstetes Silber: je Gerätepixelzeile ein leicht anderer Grauwert (fester Zufall), über einen senkrechten Verlauf
    /// mit hellem Band oben, waagerecht gestreckt. Je Farbe, Höhe und Skalierung einmal.
    /// </summary>
    private static ImageBrush Gebuerstet(Color c, int zeilen, double s)
    {
        var vorrat = _gebuerstet ??= [];
        if (vorrat.TryGetValue((c, zeilen, s), out var fertig)) return fertig;
        zeilen = Math.Max(2, zeilen);
        var zufall = new Random(11);
        var px = new byte[zeilen * 4 * 4];
        for (var y = 0; y < zeilen; y++)
        {
            var t = y / (double)zeilen;
            var licht = 1.08 - 0.24 * t + 0.09 * Math.Exp(-Math.Pow((t - 0.25) / 0.12, 2));
            var rauschen = 1 + (zufall.NextDouble() - 0.5) * 0.08;
            byte K(byte v) => (byte)Math.Clamp(v * licht * rauschen, 0, 255);
            for (var x = 0; x < 4; x++)
            {
                var o = (y * 4 + x) * 4;
                px[o] = (byte)Math.Min(255, K(c.B) + 3);
                px[o + 1] = K(c.G);
                px[o + 2] = K(c.R);
                px[o + 3] = c.A;
            }
        }
        var bild = BitmapSource.Create(4, zeilen, 96 * s, 96 * s, PixelFormats.Bgra32, null, px, 16);
        bild.Freeze();
        var pinsel = new ImageBrush(bild) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(pinsel, BitmapScalingMode.NearestNeighbor);
        pinsel.Freeze();
        if (vorrat.Count > 32) vorrat.Clear();
        vorrat[(c, zeilen, s)] = pinsel;
        return pinsel;
    }
}
