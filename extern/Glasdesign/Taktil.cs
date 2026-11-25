// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Glasdesign;

/// <summary>
/// Eine Fläche der Stilfamilie Taktil (KONZEPT.md 5b.2, Vorbild 2: neo-skeuomorph, dunkles warmes Grau, ein Akzent
/// Kupfer, Licht oben links). Tiefe entsteht nur aus Ebenen als Schattenstufen (<see cref="Taktilebene"/>):
/// <list type="bullet">
/// <item>Gehoben und Karte: oben heller, Lichtkante oben, weicher Schatten nach unten (Karte eine Stufe höher).</item>
/// <item>Eingelassen: dunkler als der Grund, oben und links innen Schatten, unten eine feine Lichtkante; wahlweise ein
/// eigener Rand (<see cref="Randfarbe"/>, im Fokus <see cref="Kupferrand"/>) mit weichem Leuchten (<see cref="Leuchten"/>).</item>
/// <item>Haupt: Kupfer in einer Bronzefassung, Glanz oben; gedrückt dunkler mit Innenschatten.</item>
/// <item>Kupfer und KupferEingelassen: Kupfer ohne Fassung, gehoben oder als Spur eingelassen.</item>
/// <item>Metall: gebürsteter Knauf mit konzentrischen Ringen (rund).</item>
/// </list>
/// Gedrückt sinken gehobene Flächen ein; gesperrt sind sie flach und ohne Schatten. Der Schatten ist ein eingefrorener
/// Effekt am Körper (nicht bei Eingelassen); Kupfer kommt aus <see cref="Farbe"/> (Background, in den Stilen
/// Glas.Taktil.Kupfer), sonst das Kupfer der Studie. Seit 0.5.0-beta.13 sind Fläche, Mulde und Kupfer abgestimmt
/// (<see cref="Flaechenton"/>, <see cref="Muldenton"/>, <see cref="Kupferton"/>): Hält auf einer frei gewählten Farbe aus dem
/// Mittelbereich keine Schrift, dunkelt jeder Taktil-Körper sie gleich ab (<see cref="Schriftwahl.Abstimmung"/>).
/// </summary>
public sealed class Taktilkoerper : Familienkoerper
{
    public static readonly DependencyProperty EbeneProperty = DependencyProperty.Register(
        nameof(Ebene), typeof(Taktilebene), typeof(Taktilkoerper), new FrameworkPropertyMetadata(Taktilebene.Gehoben, FrameworkPropertyMetadataOptions.AffectsRender, Neu));

    /// <summary>Die Farbe des Kupfers (Background); fehlt sie, gilt <see cref="KupferFarbe"/>.</summary>
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Taktilkoerper), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FlaecheFarbeProperty = Farbwert(nameof(FlaecheFarbe), Color.FromRgb(0x2E, 0x2E, 0x2F));
    public static readonly DependencyProperty MuldeFarbeProperty = Farbwert(nameof(MuldeFarbe), Color.FromRgb(0x1B, 0x1B, 0x1C));
    public static readonly DependencyProperty KupferFarbeProperty = Farbwert(nameof(KupferFarbe), Color.FromRgb(0xA8, 0x55, 0x3A));
    public static readonly DependencyProperty FassungFarbeProperty = Farbwert(nameof(FassungFarbe), Color.FromRgb(0x3A, 0x33, 0x2E));
    public static readonly DependencyProperty MetallFarbeProperty = Farbwert(nameof(MetallFarbe), Color.FromRgb(0x7A, 0x72, 0x6B));

    /// <summary>Eigener Rand einer eingelassenen Fläche (etwa Kupfer im Fokus); durchsichtig heißt: der eigene Schattenrand.</summary>
    public static readonly DependencyProperty RandfarbeProperty = Farbwert(nameof(Randfarbe), Colors.Transparent);

    /// <summary>Den eigenen Rand im Kupfer zeichnen (<see cref="Kupfer"/>, etwa im Fokus) statt in <see cref="Randfarbe"/>.</summary>
    public static readonly DependencyProperty KupferrandProperty = DependencyProperty.Register(
        nameof(Kupferrand), typeof(bool), typeof(Taktilkoerper), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Breite dieses Rands in DIP (mindestens ein Gerätepixel).</summary>
    public static readonly DependencyProperty RandbreiteProperty = DependencyProperty.Register(
        nameof(Randbreite), typeof(double), typeof(Taktilkoerper), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Weiches Leuchten außen in der Randfarbe (0 bis 1, Reichweite 12 DIP), etwa für den Fokus.</summary>
    public static readonly DependencyProperty LeuchtenProperty = DependencyProperty.Register(
        nameof(Leuchten), typeof(double), typeof(Taktilkoerper), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Metall dunkler (Knauf auf der Kupferspur).</summary>
    public static readonly DependencyProperty DunkelProperty = DependencyProperty.Register(
        nameof(Dunkel), typeof(bool), typeof(Taktilkoerper), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Breite der Bronzefassung (Ebene Haupt) in DIP.</summary>
    public static readonly DependencyProperty FassungProperty = DependencyProperty.Register(
        nameof(Fassung), typeof(double), typeof(Taktilkoerper), new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Farbwert(string name, Color vorgabe) => DependencyProperty.Register(
        name, typeof(Color), typeof(Taktilkoerper), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    public Taktilkoerper()
    {
        SetResourceReference(FlaecheFarbeProperty, Schluessel.TaktilFlaecheFarbe);
        SetResourceReference(MuldeFarbeProperty, Schluessel.TaktilMuldeFarbe);
        SetResourceReference(FassungFarbeProperty, Schluessel.TaktilFassungFarbe);
        SetResourceReference(MetallFarbeProperty, Schluessel.TaktilMetallFarbe);
    }

    public Taktilebene Ebene { get => (Taktilebene)GetValue(EbeneProperty); set => SetValue(EbeneProperty, value); }
    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Color FlaecheFarbe { get => (Color)GetValue(FlaecheFarbeProperty); set => SetValue(FlaecheFarbeProperty, value); }
    public Color MuldeFarbe { get => (Color)GetValue(MuldeFarbeProperty); set => SetValue(MuldeFarbeProperty, value); }
    public Color KupferFarbe { get => (Color)GetValue(KupferFarbeProperty); set => SetValue(KupferFarbeProperty, value); }
    public Color FassungFarbe { get => (Color)GetValue(FassungFarbeProperty); set => SetValue(FassungFarbeProperty, value); }
    public Color MetallFarbe { get => (Color)GetValue(MetallFarbeProperty); set => SetValue(MetallFarbeProperty, value); }
    public Color Randfarbe { get => (Color)GetValue(RandfarbeProperty); set => SetValue(RandfarbeProperty, value); }
    public bool Kupferrand { get => (bool)GetValue(KupferrandProperty); set => SetValue(KupferrandProperty, value); }
    public double Randbreite { get => (double)GetValue(RandbreiteProperty); set => SetValue(RandbreiteProperty, value); }
    public double Leuchten { get => (double)GetValue(LeuchtenProperty); set => SetValue(LeuchtenProperty, value); }
    public bool Dunkel { get => (bool)GetValue(DunkelProperty); set => SetValue(DunkelProperty, value); }
    public double Fassung { get => (double)GetValue(FassungProperty); set => SetValue(FassungProperty, value); }

    /// <summary>Das wirksame Kupfer: <see cref="Farbe"/>, sonst <see cref="KupferFarbe"/>, abgestimmt (<see cref="Kupferton"/>).</summary>
    public Color Kupfer => Kupferton(FarbeVon(Farbe, KupferFarbe));

    /// <summary>Die wirksame Fläche (<see cref="FlaecheFarbe"/> abgestimmt, <see cref="Flaechenton"/>).</summary>
    private Color WirksameFlaeche => Flaechenton(FlaecheFarbe);

    /// <summary>Die wirksame Mulde (<see cref="MuldeFarbe"/> abgestimmt, <see cref="Muldenton"/>).</summary>
    private Color WirksameMulde => Muldenton(MuldeFarbe);

    /// <summary>Die Farbe des eigenen Rands: Kupfer oder <see cref="Randfarbe"/>.</summary>
    private Color Rand => Kupferrand ? Kupfer : Randfarbe;

    private static void Neu(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((Taktilkoerper)d).EffektSetzen();

    private protected override void Nachgeaendert() => EffektSetzen();

    private protected override object Teilschluessel() =>
        (Ebene, Kupfer, FlaecheFarbe, MuldeFarbe, FassungFarbe, MetallFarbe, Rand, Randbreite, Leuchten, Dunkel, Fassung);

    /// <summary>Wie hoch die Ebene liegt (Faktor auf die Stufe im Profil); 0: kein Schatten.</summary>
    private double Hoehe() => Ebene switch
    {
        Taktilebene.Gehoben => 1.0,
        Taktilebene.Karte => 1.6,
        Taktilebene.Haupt => 1.2,
        Taktilebene.Kupfer => 0.8,
        Taktilebene.Metall => 0.9,
        _ => 0,
    };

    /// <summary>Weicher Schatten nach unten (Richtung 290 Grad, Licht oben links), gedrückt und gesperrt keiner.</summary>
    private void EffektSetzen()
    {
        var p = Profil;
        var stufe = Hoehe() * p.TaktilStufe;
        if (stufe <= 0 || p.TaktilSchatten <= 0 || Zustand is Glaszustand.Gedrueckt or Glaszustand.Gesperrt || Schatten != Schattenstufe.Voll || RenderSize.Height <= 0)
        {
            if (Koerpereffekt is not null) KoerpereffektSetzen(null);
            return;
        }
        var unschaerfe = Math.Round(6 + 7 * stufe, 2);
        var tiefe = Math.Round(1.5 + 2.2 * stufe, 2);
        var deckung = Math.Clamp(p.TaktilSchatten * (0.55 + 0.08 * stufe) / 0.63, 0, 1);
        if (Koerpereffekt is DropShadowEffect alt && alt.BlurRadius == unschaerfe && alt.ShadowDepth == tiefe && alt.Opacity == deckung) return;
        var neu = new DropShadowEffect { Color = Color.FromRgb(0x05, 0x05, 0x05), Direction = 290, ShadowDepth = tiefe, BlurRadius = unschaerfe, Opacity = deckung, RenderingBias = RenderingBias.Performance };
        neu.Freeze();
        KoerpereffektSetzen(neu);
    }

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        var h = f.Hoehe;
        var oben = 0.0;
        var unten = h / s;
        var gesperrt = Zustand == Glaszustand.Gesperrt;
        var gedrueckt = Zustand == Glaszustand.Gedrueckt;
        var linie = Glasgeometrie.Haarlinie;
        var licht = Math.Clamp(p.TaktilLicht / 0.24, 0, 3);

        // Leuchten außen in der Randfarbe: Ringe von je einem Gerätepixel, nach außen schwächer.
        if (Leuchten > 0 && Rand.A > 0)
        {
            var weite = Math.Max(1, (int)Glasgeometrie.Raster(12, s));
            for (var i = 0; i < weite; i++)
            {
                var deckung = Math.Clamp(Leuchten * 0.55 * Math.Pow(1 - (i + 0.5) / weite, 1.5), 0, 1);
                if (deckung * 255 < 0.5) continue;
                Schicht(f.Ring(-(i + 1), -i), Flaeche(Farbton.Deckend(Rand, deckung)));
            }
        }

        switch (Ebene)
        {
            case Taktilebene.Gehoben or Taktilebene.Karte when !gedrueckt:
            {
                var c = gesperrt ? Dunkler(WirksameFlaeche, 0.08) : Zustandsfarbe(Ebene == Taktilebene.Karte ? Heller(WirksameFlaeche, 0.015) : WirksameFlaeche, p);
                Schicht(f.Form(linie), Senkrecht(oben, unten, (Heller(c, gesperrt ? 0.02 : 0.045), 0), (Dunkler(c, 0.1), 1)));
                Schicht(f.Ring(0, linie), Senkrecht(oben, unten,
                    (Heller(c, (gesperrt ? 0.06 : 0.16) * licht), 0), (Heller(c, 0.02), 0.35), (Dunkler(c, 0.42), 1)));
                break;
            }
            case Taktilebene.Gehoben or Taktilebene.Karte or Taktilebene.Eingelassen:
                Mulde(f, gedrueckt && Ebene != Taktilebene.Eingelassen ? Dunkler(WirksameFlaeche, 0.18) : WirksameMulde, p, gesperrt);
                break;
            case Taktilebene.Haupt:
            {
                var fassung = Math.Max(1, Glasgeometrie.Raster(Fassung, s));
                var fa = gesperrt ? Dunkler(FassungFarbe, 0.1) : FassungFarbe;
                Schicht(f.Form(0), Senkrecht(oben, unten, (Heller(fa, 0.1), 0), (fa, 0.5), (Dunkler(fa, 0.45), 1)));
                Schicht(f.Ring(0, linie), Senkrecht(oben, unten, (Heller(fa, 0.45 * licht), 0), (Heller(fa, 0.15), 0.5), (Dunkler(fa, 0.3), 1)));
                Kupferkoerper(f, fassung, p, gesperrt, gedrueckt, licht);
                break;
            }
            case Taktilebene.Kupfer:
                Kupferkoerper(f, 0, p, gesperrt, gedrueckt, licht);
                break;
            case Taktilebene.KupferEingelassen:
            {
                var k = gesperrt ? Farbton.Mischen(Kupfer, WirksameMulde, 0.6) : Dunkler(Kupfer, 0.05);
                Schicht(f.Form(0), Flaeche(k));
                Schicht(f.Form(0), Senkrecht(oben, unten, (Schwarz(0.42 * p.TaktilTiefe / 0.6), 0), (Schwarz(0), 0.5)));
                Schicht(f.Ring(0, linie), Senkrecht(oben, unten, (Farbton.Deckend(Dunkler(k, 0.55), 0.9), 0), (Farbton.Deckend(k, 0), 0.5), (Farbton.Deckend(Heller(k, 0.55), 0.35), 1)));
                break;
            }
            default:
                Metall(f, p, gesperrt, licht);
                break;
        }
    }

    /// <summary>
    /// Schriftwahl (0.5.0-beta.7): die Farben im Schriftband je Ebene und Zustand, wie <see cref="Bauen"/> sie zeichnet (Fläche
    /// und Verlauf samt Innenschatten, ohne Ränder und Glanz oben, die die Schrift nicht erreichen).
    /// </summary>
    private protected override IReadOnlyList<Color> Flaechenfarben()
    {
        // Durchsichtig gestellt (nicht gewählter Reiter in seiner Glas.Taktil.Mulde) liegt die Schrift auf der Mulde, darüber
        // der Körper mit seiner Deckung.
        var farben = Ebenenfarben();
        if (Opacity >= 1) return farben;
        // Seit 0.5.0-beta.13 die Mulde samt ihrem Innenschatten (bis beta.12 ihre flache Farbe), Stelle für Stelle im Band.
        var mulde = Ebenenband(Taktilebene.Eingelassen, Glaszustand.Normal, WirksameFlaeche, WirksameMulde, Kupfer, MetallFarbe, Profil);
        var deckung = Math.Clamp(Opacity, 0, 1);
        return [.. farben.Select((f, i) => Schriftwahl.Ueber(Farbton.Deckend(f, deckung), mulde[Math.Min(i, mulde.Count - 1)]))];
    }

    private IReadOnlyList<Color> Ebenenfarben() => Ebene == Taktilebene.Karte
        ? Ebenenband(Ebene, Zustand, WirksameFlaeche, WirksameMulde, Kupfer, MetallFarbe, Profil)
        : Ebenenband(Ebene, Zustand, WirksameFlaeche, WirksameMulde, Kupfer, Dunkel ? Dunkler(MetallFarbe, 0.4) : MetallFarbe, Profil);

    /// <summary>
    /// Die Farben im Schriftband einer Ebene im Zustand <paramref name="zustand"/> (für die Schriftwahl des Körpers und die
    /// Abstimmung der Familienfarben, darum ohne Bezug auf einen Körper): Fläche und Verlauf samt Innenschatten.
    /// </summary>
    private static IReadOnlyList<Color> Ebenenband(Taktilebene ebene, Glaszustand zustand, Color flaeche, Color mulde, Color kupfer, Color metall, Stilprofil p)
    {
        // Wie weit die Schrift reicht: auf der Karte über die ganze Höhe (Titel oben, Text darunter), in der Mulde von den
        // Punkten der Umlaute (ein Viertel der Höhe, wo ihr Innenschatten noch liegt) bis unter die Mitte, sonst das Band der Knöpfe.
        var (oben, unten) = ebene switch { Taktilebene.Karte => (0.06, 0.94), Taktilebene.Eingelassen => (0.25, 0.65), _ => (0.35, 0.65) };
        var gesperrt = zustand == Glaszustand.Gesperrt;
        var gedrueckt = zustand == Glaszustand.Gedrueckt;
        switch (ebene)
        {
            case Taktilebene.Gehoben or Taktilebene.Karte when !gedrueckt:
            {
                var c = gesperrt ? Dunkler(flaeche, 0.08) : Zustandsfarbe(ebene == Taktilebene.Karte ? Heller(flaeche, 0.015) : flaeche, p, zustand);
                var (hell, dunkel) = (Heller(c, gesperrt ? 0.02 : 0.045), Dunkler(c, 0.1));
                return Band(t => Verlaufsfarbe(t, (hell, 0), (dunkel, 1)), oben, unten);
            }
            case Taktilebene.Gehoben or Taktilebene.Karte or Taktilebene.Eingelassen:
            {
                var c = gedrueckt && ebene != Taktilebene.Eingelassen ? Dunkler(flaeche, 0.18) : mulde;
                if (zustand == Glaszustand.Ueberfahren && ebene == Taktilebene.Eingelassen) c = Heller(c, p.FamilieUeberfahren * 0.5);
                var tiefe = Math.Clamp(p.TaktilTiefe, 0, 1.5) * (gesperrt ? 0.5 : 1);
                return Band(t => Schriftwahl.Ueber(Verlaufsfarbe(t, (Schwarz(0.6 * tiefe), 0), (Schwarz(0.2 * tiefe), 0.22), (Schwarz(0), 0.5)), c), oben, unten);
            }
            case Taktilebene.Haupt or Taktilebene.Kupfer:
            {
                if (gesperrt)
                {
                    var k = Farbton.Mischen(kupfer, flaeche, 0.68);
                    return Band(t => Verlaufsfarbe(t, (Heller(k, 0.03), 0), (Dunkler(k, 0.06), 1)));
                }
                if (gedrueckt)
                    return Band(t => Schriftwahl.Ueber(Verlaufsfarbe(t, (Schwarz(0.55), 0), (Schwarz(0.1), 0.35), (Schwarz(0), 0.55)),
                        Verlaufsfarbe(t, (Dunkler(kupfer, 0.42), 0), (Dunkler(kupfer, 0.28), 0.35), (Dunkler(kupfer, 0.17), 1))));
                var c = zustand == Glaszustand.Ueberfahren ? Heller(kupfer, p.FamilieUeberfahren * 0.5) : kupfer;
                return Band(t => Verlaufsfarbe(t, (Heller(c, 0.08), 0), (c, Kupferglanz), (c, 0.45), (Dunkler(c, 0.16), 1)));
            }
            case Taktilebene.KupferEingelassen:
            {
                var k = gesperrt ? Farbton.Mischen(kupfer, mulde, 0.6) : Dunkler(kupfer, 0.05);
                return Band(t => Schriftwahl.Ueber(Verlaufsfarbe(t, (Schwarz(0.42 * p.TaktilTiefe / 0.6), 0), (Schwarz(0), 0.5)), k));
            }
            default:
                return [gesperrt ? Farbton.Mischen(metall, flaeche, 0.5) : Zustandsfarbe(metall, p, zustand)];
        }
    }

    // ------------------------------------------------------------------ Abstimmung der Familienfarben (0.5.0-beta.13)

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Rolle, Color Farbe), Color> Toene = new();

    /// <summary>
    /// Die Fläche der Familie (Glas.Taktil.Flaeche.Farbe), abgestimmt: unverändert, solange auf der gehobenen Fläche und der
    /// Karte in Ruhe und überfahren eine Schrift hält; sonst so wenig wie nötig abgedunkelt, bis Weiß hält (oder aufgehellt, bis
    /// fast Schwarz hält), nach <see cref="Schriftwahl.Abstimmung"/>. Gerechnet mit den Werten des Stilprofils Standard, ohne
    /// Bezug auf einen Körper: Jeder Taktil-Körper und jede Schriftwahl auf dieser Fläche kommt so auf dieselbe Farbe.
    /// </summary>
    public static Color Flaechenton(Color roh) => Ton(0, roh, Flaechenbaender);

    /// <summary>Die Bänder einer Fläche: gehoben in Ruhe, überfahren und gedrückt, die Karte über ihre ganze Höhe (Titel oben, Text darunter).</summary>
    private static IReadOnlyList<IReadOnlyList<Color>> Flaechenbaender(Color c) =>
    [
        Ebenenband(Taktilebene.Gehoben, Glaszustand.Normal, c, c, c, c, Stilprofil.Standard),
        Ebenenband(Taktilebene.Gehoben, Glaszustand.Ueberfahren, c, c, c, c, Stilprofil.Standard),
        Ebenenband(Taktilebene.Gehoben, Glaszustand.Gedrueckt, c, c, c, c, Stilprofil.Standard),
        Ebenenband(Taktilebene.Karte, Glaszustand.Normal, c, c, c, c, Stilprofil.Standard),
        Ebenenband(Taktilebene.Karte, Glaszustand.Ueberfahren, c, c, c, c, Stilprofil.Standard),
    ];

    /// <summary>Die Mulde der Familie (Glas.Taktil.Mulde.Farbe), abgestimmt wie <see cref="Flaechenton"/> auf der eingelassenen Fläche.</summary>
    public static Color Muldenton(Color roh) => Ton(1, roh, c =>
    [
        Ebenenband(Taktilebene.Eingelassen, Glaszustand.Normal, c, c, c, c, Stilprofil.Standard),
        Ebenenband(Taktilebene.Eingelassen, Glaszustand.Ueberfahren, c, c, c, c, Stilprofil.Standard),
    ]);

    /// <summary>Das Kupfer (Glas.Taktil.Kupfer oder Background), abgestimmt wie <see cref="Flaechenton"/> auf der Kupferfläche.</summary>
    public static Color Kupferton(Color roh) => Ton(2, roh, c =>
    [
        Ebenenband(Taktilebene.Kupfer, Glaszustand.Normal, c, c, c, c, Stilprofil.Standard),
        Ebenenband(Taktilebene.Kupfer, Glaszustand.Ueberfahren, c, c, c, c, Stilprofil.Standard),
    ]);

    /// <summary>
    /// Die Farben unter der Schrift auf einer Fläche dieser Familie in Ruhe, abgestimmt, mit den Werten des Stilprofils Standard:
    /// für Schrift, die ihre Fläche als einzelne Farbe kennt (<see cref="Schriftwahl.Wandler"/> mit "Taktil.Flaeche" oder
    /// "Taktil.Mulde"). Gehoben und Karte: <paramref name="roh"/> ist die Fläche; Eingelassen: die Mulde; Kupfer: das Kupfer.
    /// </summary>
    public static IReadOnlyList<Color> Schriftflaeche(Taktilebene ebene, Color roh, bool gesperrt = false)
    {
        var (f, m, k) = (Flaechenton(roh), Muldenton(roh), Kupferton(roh));
        if (gesperrt) return Ebenenband(ebene, Glaszustand.Gesperrt, f, m, k, roh, Stilprofil.Standard);
        // Die Schrift kennt nur die Farbe, nicht ihren Zustand: alle bedienbaren Zustände zusammen.
        return [.. new[] { Glaszustand.Normal, Glaszustand.Ueberfahren, Glaszustand.Gedrueckt }
            .SelectMany(z => Ebenenband(ebene, z, f, m, k, roh, Stilprofil.Standard))];
    }
    private static Color Ton(int rolle, Color roh, Func<Color, IReadOnlyList<IReadOnlyList<Color>>> baender)
    {
        if (Toene.TryGetValue((rolle, roh), out var ton)) return ton;
        if (Toene.Count > 512) Toene.Clear();
        ton = Schriftwahl.Abgestimmt(roh, Schriftwahl.Abstimmung(roh, t => baender(Schriftwahl.Abgestimmt(roh, t))));
        Toene[(rolle, roh)] = ton;
        return ton;
    }
    /// <summary>Eingelassene Mulde: Fläche, Innenschatten oben und links, Rand oben dunkel und unten mit feiner Lichtkante (oder der eigene Rand).</summary>
    private void Mulde(Glasflaeche f, Color c, Stilprofil p, bool gesperrt)
    {
        var s = f.Skala;
        var b = f.Breite;
        var h = f.Hoehe;
        var tiefe = Math.Clamp(p.TaktilTiefe, 0, 1.5) * (gesperrt ? 0.5 : 1);
        if (Zustand == Glaszustand.Ueberfahren && Ebene == Taktilebene.Eingelassen) c = Heller(c, p.FamilieUeberfahren * 0.5);
        var form = f.Form(0);
        Schicht(form, Flaeche(c));
        if (tiefe > 0)
        {
            Schicht(form, Senkrecht(0, h / s, (Schwarz(0.6 * tiefe), 0), (Schwarz(0.2 * tiefe), 0.22), (Schwarz(0), 0.5)));
            var links = Math.Min(b * 0.12, h * 0.5);
            Schicht(form, Waagerecht(0, links / s, (Schwarz(0.3 * tiefe), 0), (Schwarz(0), 1)));
        }
        var breite = Glasgeometrie.Linie(Randbreite, s);
        if (Rand.A > 0)
            Schicht(f.Ring(0, breite), Senkrecht(0, h / s, (Heller(Rand, 0.25), 0), (Rand, 0.5), (Dunkler(Rand, 0.2), 1)));
        else
            Schicht(f.Ring(0, Glasgeometrie.Haarlinie), Senkrecht(0, h / s, (Schwarz(0.5), 0), (Schwarz(0), 0.5), (Weiss(Zustand == Glaszustand.Ueberfahren ? 0.26 : 0.2), 1)));
    }

    /// <summary>Kupfer gehoben (oder gedrückt eingesunken), um <paramref name="einzug"/> Pixel innen.</summary>
    private void Kupferkoerper(Glasflaeche f, double einzug, Stilprofil p, bool gesperrt, bool gedrueckt, double licht)
    {
        var s = f.Skala;
        var h = f.Hoehe;
        var oben = einzug / s;
        var unten = (h - einzug) / s;
        var rand = Math.Max(1, Glasgeometrie.Raster(1.2, s));
        if (gesperrt)
        {
            var k = Farbton.Mischen(Kupfer, WirksameFlaeche, 0.68);
            Schicht(f.Form(einzug), Senkrecht(oben, unten, (Heller(k, 0.03), 0), (Dunkler(k, 0.06), 1)));
            Schicht(f.Ring(einzug, einzug + rand), Senkrecht(oben, unten, (Heller(k, 0.12), 0), (Dunkler(k, 0.25), 1)));
            return;
        }
        if (gedrueckt)
        {
            var k = Kupfer;
            Schicht(f.Form(einzug), Senkrecht(oben, unten, (Dunkler(k, 0.42), 0), (Dunkler(k, 0.28), 0.35), (Dunkler(k, 0.17), 1)));
            Schicht(f.Form(einzug), Senkrecht(oben, unten, (Schwarz(0.55), 0), (Schwarz(0.1), 0.35), (Schwarz(0), 0.55)));
            Schicht(f.Ring(einzug, einzug + rand), Senkrecht(oben, unten, (Dunkler(k, 0.65), 0), (Dunkler(k, 0.1), 1)));
            return;
        }
        // Überfahren nur halb so viel heller: Weiße Schrift hält auf Kupfer sonst keine 4,5 zu 1. Seit 0.5.0-beta.12 bleiben das
        // hellere Kupfer oben und der Glanz im oberen Fünftel (Kupferglanz): Die Punkte von Ä, Ö, Ü und die Oberkante großer
        // Buchstaben reichen bis rund 25 Prozent der Höhe, dort hielt Weiß überfahren nur 4,46 zu 1 (Glint G2).
        var c = Zustand == Glaszustand.Ueberfahren ? Heller(Kupfer, p.FamilieUeberfahren * 0.5) : Kupfer;
        Schicht(f.Form(einzug), Senkrecht(oben, unten, (Heller(c, 0.08), 0), (c, Kupferglanz), (c, 0.45), (Dunkler(c, 0.16), 1)));
        Schicht(f.Ring(einzug, einzug + rand), Senkrecht(oben, unten, (Heller(c, 0.55), 0), (Heller(c, 0.1), 0.5), (Dunkler(c, 0.5), 1)));
        var glanz = 0.24 * licht;
        if (glanz > 0)
            Schicht(f.Form(einzug + 2 * rand), Senkrecht(oben, unten, (Weiss(glanz), 0), (Weiss(glanz * 0.15), Kupferglanz * 0.8), (Weiss(0), Kupferglanz)));
    }

    /// <summary>Bis zu diesem Anteil der Höhe reichen das hellere Kupfer oben und sein Glanz (seit 0.5.0-beta.12; bis beta.11 0,45 und 0,32).</summary>
    internal const double Kupferglanz = 0.2;

    /// <summary>Gebürsteter Knauf: konzentrische Ringe, Kante oben hell und unten dunkel, innerer Ring, Glanz oben.</summary>
    private void Metall(Glasflaeche f, Stilprofil p, bool gesperrt, double licht)
    {
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var m = Dunkel ? Dunkler(MetallFarbe, 0.4) : MetallFarbe;
        if (gesperrt) m = Farbton.Mischen(m, WirksameFlaeche, 0.5);
        else m = Zustandsfarbe(m, p);
        var mitte = f.Dip(b * 0.45, h * 0.4);
        var r = Math.Max(b, h) * 0.62 / s;
        Schicht(f.Form(0), Radial(mitte, r, r,
            (Heller(m, 0.25), 0), (m, 0.28), (Heller(m, 0.1), 0.42), (Dunkler(m, 0.13), 0.58), (Heller(m, 0.03), 0.74), (Dunkler(m, 0.35), 1)));
        var rand = Math.Max(1, Glasgeometrie.Raster(1.2, s));
        Schicht(f.Ring(0, rand), Senkrecht(0, h / s, (Heller(m, 0.45 * licht), 0), (Dunkler(m, 0.5), 1)));
        var innen = Math.Round(Math.Min(b, h) * 0.14);
        if (innen > rand + 1)
            Schicht(f.Ring(innen, innen + Glasgeometrie.Haarlinie), Senkrecht(0, h / s, (Schwarz(0.35), 0), (Weiss(0.2), 1)));
        Schicht(f.Form(2 * rand), Senkrecht(0, h / s, (Weiss(0.22 * licht), 0), (Weiss(0), 0.5)));
    }
}
