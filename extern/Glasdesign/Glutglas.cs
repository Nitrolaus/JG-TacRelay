// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Glasdesign;

/// <summary>
/// Eine Rauchglasscheibe der Stilfamilie Glutglas (KONZEPT.md 5b.3, Vorbild 3): dunkel getönt, ein schräger Glanz von oben
/// links, der an einer Diagonale abbricht, eine Haarlinie oben heller als unten. Wahlweise eine zweite Scheibe dahinter,
/// nach unten rechts versetzt (<see cref="Versatz"/>), deren Kante rot auslaufen kann, rotes Licht im Glas und unter der
/// Scheibe (<see cref="Glut"/>) und die <see cref="Kantenglut"/>: ein heller roter Strich an der Unterkante (und mit
/// <see cref="KantenglutOben"/> auch oben), der zu den Enden ausläuft, mit weichem Schein. Die Glutfarbe kommt aus
/// <see cref="Farbe"/> (Background, in den Stilen Glas.Glutglas.Glut), sonst Glutrot. Rot und Schein reichen über die
/// eigenen Grenzen hinaus.
/// </summary>
public sealed class Glutscheibe : Familienkoerper
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Glutscheibe), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlutFarbeProperty = Farbwert(nameof(GlutFarbe), Color.FromRgb(0xFF, 0x22, 0x36));
    public static readonly DependencyProperty ScheibeFarbeProperty = Farbwert(nameof(ScheibeFarbe), Color.FromRgb(0x16, 0x14, 0x1B));

    public static readonly DependencyProperty KanteProperty = DependencyProperty.Register(
        nameof(Kante), typeof(Brush), typeof(Glutscheibe), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Rotes Licht im Glas und darunter, 0 bis 1 (mal GlutglasGlut im Profil).</summary>
    public static readonly DependencyProperty GlutProperty = Zahl(nameof(Glut), 0);

    /// <summary>Kantenglut an der Unterkante, 0 bis 1 (mal GlutglasKantenglut).</summary>
    public static readonly DependencyProperty KantenglutProperty = Zahl(nameof(Kantenglut), 0);

    /// <summary>
    /// Seitenglut (seit 0.5.0-beta.10, Glutglas-Karte): ein heller roter Strich mit Schein an der linken Kante der Scheibe und
    /// an der rechten Kante der Versatzscheibe (ohne Versatz der Scheibe), 0 bis 1 (mal GlutglasKantenglut); 0 (Vorgabe) zeichnet nichts.
    /// </summary>
    public static readonly DependencyProperty SeitenglutProperty = Zahl(nameof(Seitenglut), 0);

    /// <summary>Die Kantenglut auch an der Oberkante (gewählter Reiter).</summary>
    public static readonly DependencyProperty KantenglutObenProperty = DependencyProperty.Register(
        nameof(KantenglutOben), typeof(bool), typeof(Glutscheibe), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Versatz der zweiten Scheibe nach unten rechts in DIP; 0 heißt keine. Sie gehört nicht zur Größe des Elements.</summary>
    public static readonly DependencyProperty VersatzProperty = Zahl(nameof(Versatz), 0);

    /// <summary>Getöntes Glas statt Rauch (Haken an): Farbe der Tönung, durchsichtig heißt Rauch.</summary>
    public static readonly DependencyProperty ToenungProperty = Farbwert(nameof(Toenung), Colors.Transparent);

    /// <summary>Keine Scheibe, nur Schein und Kantenglut (gewählter Reiter in der Leiste).</summary>
    public static readonly DependencyProperty LeerProperty = DependencyProperty.Register(
        nameof(Leer), typeof(bool), typeof(Glutscheibe), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Scheibe (Vorgabe), Spur oder Knauf eines Schalters; Spur und Knauf sind "an", solange <see cref="Glut"/> über 0 ist.</summary>
    public static readonly DependencyProperty TeilProperty = DependencyProperty.Register(
        nameof(Teil), typeof(Glutteil), typeof(Glutscheibe), new FrameworkPropertyMetadata(Glutteil.Scheibe, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Farbwert(string name, Color vorgabe) => DependencyProperty.Register(
        name, typeof(Color), typeof(Glutscheibe), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Zahl(string name, double vorgabe) => DependencyProperty.Register(
        name, typeof(double), typeof(Glutscheibe), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    public Glutscheibe()
    {
        SetResourceReference(ScheibeFarbeProperty, Schluessel.GlutglasScheibeFarbe);
        SetResourceReference(KanteProperty, Schluessel.GlutglasKante);
    }

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Color GlutFarbe { get => (Color)GetValue(GlutFarbeProperty); set => SetValue(GlutFarbeProperty, value); }
    public Color ScheibeFarbe { get => (Color)GetValue(ScheibeFarbeProperty); set => SetValue(ScheibeFarbeProperty, value); }
    public Brush? Kante { get => (Brush?)GetValue(KanteProperty); set => SetValue(KanteProperty, value); }
    public double Glut { get => (double)GetValue(GlutProperty); set => SetValue(GlutProperty, value); }
    public double Kantenglut { get => (double)GetValue(KantenglutProperty); set => SetValue(KantenglutProperty, value); }
    public double Seitenglut { get => (double)GetValue(SeitenglutProperty); set => SetValue(SeitenglutProperty, value); }
    public bool KantenglutOben { get => (bool)GetValue(KantenglutObenProperty); set => SetValue(KantenglutObenProperty, value); }
    public double Versatz { get => (double)GetValue(VersatzProperty); set => SetValue(VersatzProperty, value); }
    public Color Toenung { get => (Color)GetValue(ToenungProperty); set => SetValue(ToenungProperty, value); }
    public bool Leer { get => (bool)GetValue(LeerProperty); set => SetValue(LeerProperty, value); }
    public Glutteil Teil { get => (Glutteil)GetValue(TeilProperty); set => SetValue(TeilProperty, value); }

    /// <summary>Die wirksame Glutfarbe: <see cref="Farbe"/>, sonst <see cref="GlutFarbe"/>.</summary>
    public Color Rot => FarbeVon(Farbe, GlutFarbe);

    private protected override object Teilschluessel() => (Rot, ScheibeFarbe, Kante, Glut, Kantenglut, KantenglutOben, Versatz, Toenung, Leer, Teil, Seitenglut);

    /// <summary>
    /// Schriftwahl (0.5.0-beta.7): die Farben unter dem Inhalt einer Scheibe. Rauchglas ist durchsichtig; gerechnet wird auf
    /// dem dunklen Grund, für den die Familie gemacht ist (<see cref="Familienkoerper.Rechengrund"/>), dazu der Schrägglanz
    /// und das rote Licht im Glas, so stark, wie es unter der Schrift ankommt. Ein leerer Teil (gewählter Reiter) liegt auf
    /// der Scheibe seiner Leiste. Spur und Knauf tragen keinen Inhalt.
    /// </summary>
    private protected override IReadOnlyList<Color> Flaechenfarben() =>
        Teil != Glutteil.Scheibe ? [] : Scheibenband(WirksameScheibe, Toenung, Leer, Glut, Rot, Zustand, Profil, Anteil(ScheibeFarbe));

    /// <summary>Anteil des Schrägglanzes unter der Schrift an seiner Bruchkante (wie bis beta.12).</summary>
    private const double Glanzanteil = 0.53;

    /// <summary>
    /// Der Anteil des Schrägglanzes für die Schriftwahl: auf einer Scheibe aus dem Mittelbereich (<see cref="Schriftwahl.Mittel"/>)
    /// wie oben links, wo die Schrift beginnt (wie die Abstimmung rechnet), sonst wie bis beta.12 an der Bruchkante; die Vorgabefarben
    /// behalten so ihre Schrift, auch das Zeichen im kleinen Kästchen des Hakens.
    /// </summary>
    private static double Anteil(Color roh) => Schriftwahl.Mittel(roh) ? GlanzanteilOben : Glanzanteil;

    /// <summary>Anteil des Schrägglanzes oben links, wo die Schrift beginnt (für die Abstimmung, seit 0.5.0-beta.13).</summary>
    private const double GlanzanteilOben = 0.9;

    /// <summary>Die wirksame Scheibenfarbe (<see cref="ScheibeFarbe"/> abgestimmt, <see cref="Scheibenton"/>).</summary>
    private Color WirksameScheibe => Scheibenton(ScheibeFarbe);

    /// <summary>
    /// Die Farben unter der Schrift auf einer Scheibe (für die Schriftwahl und die Abstimmung, ohne Bezug auf einen Körper).
    /// <paramref name="glanzanteil"/> ist der Anteil des Schrägglanzes, der unter der Schrift zählt: bis beta.12 immer der Wert an
    /// seiner Bruchkante (<see cref="Glanzanteil"/>, gut die Hälfte), seit 0.5.0-beta.13 für die Abstimmung und für Scheiben aus
    /// dem Mittelbereich so stark, wie er dort ist, wo die Schrift links oben beginnt (<see cref="GlanzanteilOben"/>; mit der
    /// Hälfte hielt Weiß auf einer mittleren Scheibe nur 3,6 zu 1), siehe <see cref="Anteil"/>.
    /// </summary>
    private static IReadOnlyList<Color> Scheibenband(Color scheibe, Color toenung, bool leer, double glutwert, Color rot, Glaszustand zustand, Stilprofil p, double glanzanteil = GlanzanteilOben)
    {
        var aktiv = zustand != Glaszustand.Gesperrt;
        var glut = aktiv ? Math.Clamp(glutwert, 0, 2) * p.GlutglasGlut * (zustand == Glaszustand.Ueberfahren ? 1.2 : 1) : 0;
        Color basis;
        double glanz;
        if (leer)
        {
            basis = Schriftwahl.Ueber(Farbton.Deckend(scheibe, Math.Clamp(p.GlutglasScheibe, 0, 1)), Rechengrund);
            glanz = p.GlutglasGlanz;
        }
        else
        {
            var s = toenung.A > 0 ? toenung : scheibe;
            s = zustand switch
            {
                Glaszustand.Ueberfahren => Heller(s, p.FamilieUeberfahren * 0.6),
                Glaszustand.Gedrueckt => Dunkler(s, p.FamilieGedrueckt),
                _ => s,
            };
            var deckung = Math.Clamp(toenung.A > 0 ? 0.92 : p.GlutglasScheibe, 0, 1) * (aktiv ? 1 : 0.7);
            basis = Schriftwahl.Ueber(Farbton.Deckend(s, deckung), Rechengrund);
            glanz = p.GlutglasGlanz * (zustand == Glaszustand.Gedrueckt ? 0.6 : 1) * (aktiv ? 1 : 0.5) * (toenung.A > 0 ? 1.6 : 1);
        }
        var proben = new List<Color> { basis };
        if (glanz > 0) proben.Add(Schriftwahl.Ueber(Weiss(Math.Min(1, glanz * glanzanteil)), basis));
        if (glut > 0) proben.Add(Schriftwahl.Ueber(Farbton.Deckend(rot, Math.Min(1, (leer ? 0.2 : 0.15) * glut)), basis));
        return [.. proben];
    }

    /// <summary>Die Farben unter der Schrift auf einer Scheibe in Ruhe, abgestimmt (für <see cref="Schriftwahl.Wandler"/> mit "Glutglas.Scheibe").</summary>
    public static IReadOnlyList<Color> Schriftflaeche(Color roh, bool gesperrt = false)
    {
        var c = Scheibenton(roh);
        if (gesperrt) return Scheibenband(c, Colors.Transparent, false, 0, default, Glaszustand.Gesperrt, Stilprofil.Standard, Anteil(roh));
        return [.. new[] { Glaszustand.Normal, Glaszustand.Ueberfahren, Glaszustand.Gedrueckt }
            .SelectMany(z => Scheibenband(c, Colors.Transparent, false, 0, default, z, Stilprofil.Standard, Anteil(roh)))];
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Color, Color> Toene = new();

    /// <summary>
    /// Die Scheibenfarbe der Familie (Glas.Glutglas.Scheibe.Farbe), abgestimmt (seit 0.5.0-beta.13, <see cref="Schriftwahl.Abstimmung"/>):
    /// unverändert, solange auf der Scheibe in Ruhe, überfahren und gedrückt eine Schrift hält, sonst so wenig wie nötig abgedunkelt, bis
    /// Weiß hält. Mit den Werten des Stilprofils Standard und ohne Glut gerechnet, damit jede Scheibe dieselbe Farbe bekommt.
    /// </summary>
    public static Color Scheibenton(Color roh)
    {
        if (Toene.TryGetValue(roh, out var ton)) return ton;
        if (Toene.Count > 512) Toene.Clear();
        IReadOnlyList<IReadOnlyList<Color>> Baender(double t)
        {
            var c = Schriftwahl.Abgestimmt(roh, t);
            return [Scheibenband(c, Colors.Transparent, false, 0, default, Glaszustand.Normal, Stilprofil.Standard),
                Scheibenband(c, Colors.Transparent, false, 0, default, Glaszustand.Ueberfahren, Stilprofil.Standard),
                Scheibenband(c, Colors.Transparent, false, 0, default, Glaszustand.Gedrueckt, Stilprofil.Standard)];
        }
        ton = Schriftwahl.Abgestimmt(roh, Schriftwahl.Abstimmung(roh, Baender));
        Toene[roh] = ton;
        return ton;
    }
    /// <summary>
    /// Die graue Spur eines Schalters (aus), überfahren und gesperrt: Sie folgt der Scheibenfarbe (Glas.Glutglas.Scheibe.Farbe),
    /// um denselben Abstand heller wie in der Vorgabe (0.5.0-beta.7; bis beta.6 fest #5A5F68, #626770, #404348).
    /// </summary>
    private Color Spurgrau(bool ueberfahren, bool aktiv)
    {
        var vorgabe = Color.FromRgb(0x16, 0x14, 0x1B);
        var ziel = !aktiv ? Color.FromRgb(0x40, 0x43, 0x48) : ueberfahren ? Color.FromRgb(0x62, 0x67, 0x70) : Color.FromRgb(0x5A, 0x5F, 0x68);
        return Farbableitung.Ableiten(ScheibeFarbe, vorgabe, ziel);
    }

    private protected override void Bauen(Glasflaeche f, Stilprofil p)
    {
        if (Teil == Glutteil.Spur)
        {
            Spur(f, p);
            return;
        }
        if (Teil == Glutteil.Knauf)
        {
            Knauf(f, p);
            return;
        }
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var rot = Rot;
        var glut = aktiv ? Math.Clamp(Glut, 0, 2) * p.GlutglasGlut * (Zustand == Glaszustand.Ueberfahren ? 1.2 : 1) : 0;
        var versatz = Versatz > 0 ? Glasgeometrie.Raster(Versatz, s) : 0;
        var linie = Glasgeometrie.Haarlinie;

        // Roter Schein unter der Scheibe (außen), unten rechts am stärksten.
        if (glut > 0 && Schatten != Schattenstufe.Aus)
        {
            var mx = b * 0.55 + versatz;
            var my = h * 0.8 + versatz;
            Schicht(Ellipse(f, mx, my, b * 0.55, h * 0.75), Radial(f.Dip(mx, my), b * 0.55 / s, h * 0.75 / s,
                (Farbton.Deckend(rot, Math.Min(1, 0.32 * glut)), 0), (Farbton.Deckend(rot, Math.Min(1, 0.12 * glut)), 0.55), (Farbton.Deckend(rot, 0), 1)));
        }

        // Die zweite Scheibe dahinter, versetzt; ihre Kante läuft unten rechts rot aus, wenn es glüht.
        if (versatz > 0)
        {
            var hinten = f with { X = versatz, Y = versatz };
            var tief = Math.Clamp(p.GlutglasScheibe, 0, 1) * 0.85 * (aktiv ? 1 : 0.6);
            Schicht(hinten.Form(0), Flaeche(Farbton.Deckend(WirksameScheibe, tief)));
            if (glut > 0)
                Schicht(hinten.Form(0), Radial(hinten.Dip(b * 0.62, h * 1.05), b * 0.55 / s, h * 0.75 / s, (Farbton.Deckend(rot, Math.Min(1, 0.3 * glut)), 0), (Farbton.Deckend(rot, 0), 1)));
            var kante = glut > 0
                ? Verlauf(hinten.Dip(0, 0), hinten.Dip(b * 0.6, h), (Weiss(0.5), 0), (Weiss(0.22), 0.45), (Farbton.Deckend(Heller(rot, 0.1), Math.Min(1, 0.9 * glut)), 1))
                : Senkrecht(versatz / s, (versatz + h) / s, (Weiss(0.3), 0), (Weiss(0.1), 1));
            Schicht(hinten.Ring(0, linie), kante);
            if (glut > 0 && Kantenglut > 0)
                Kantenstrich(hinten, b * 0.2, h, b * 0.75, Kantenglut * p.GlutglasKantenglut * glut * 0.85, Glasgeometrie.Raster(1.6, s), schein: false);
        }

        // Die Scheibe selbst: Rauch (oder getönt), schräger Glanz, rotes Licht unten im Glas, Haarlinie.
        var form = f.Form(0);
        if (!Leer)
        {
            var scheibe = Toenung.A > 0 ? Toenung : WirksameScheibe;
            scheibe = Zustand switch
            {
                Glaszustand.Ueberfahren => Heller(scheibe, p.FamilieUeberfahren * 0.6),
                Glaszustand.Gedrueckt => Dunkler(scheibe, p.FamilieGedrueckt),
                _ => scheibe,
            };
            var deckung = Math.Clamp(Toenung.A > 0 ? 0.92 : p.GlutglasScheibe, 0, 1) * (aktiv ? 1 : 0.7);
            Schicht(form, Flaeche(Farbton.Deckend(scheibe, deckung)));
            var g = p.GlutglasGlanz * (Zustand == Glaszustand.Gedrueckt ? 0.6 : 1) * (aktiv ? 1 : 0.5) * (Toenung.A > 0 ? 1.6 : 1);
            if (g > 0)
                Schicht(form, Verlauf(f.Dip(0, 0), f.Dip(b, Math.Min(b * 0.55, h * 3)),
                    (Weiss(g), 0), (Weiss(g * 0.53), 0.40), (Weiss(g * 0.12), 0.415), (Weiss(g * 0.06), 1)));
            if (glut > 0)
                Schicht(form, Radial(f.Dip(b * 0.62, h * 1.05), b * 0.55 / s, h * 0.75 / s, (Farbton.Deckend(rot, Math.Min(1, 0.32 * glut)), 0), (Farbton.Deckend(rot, 0), 1)));
        }
        var rand = Kante ?? Pinselwerk.Senkrecht((Weiss(0.55), 0), (Weiss(0.25), 0.45), (Weiss(0.15), 1));
        if (!aktiv) rand = Pinselwerk.Gedaempft(rand, 0.55);
        if (glut > 0 && !Leer)
            rand = Verlauf(f.Dip(0, 0), f.Dip(b * 0.6, h), (Weiss(0.55), 0), (Weiss(0.25), 0.45), (Farbton.Deckend(Heller(rot, 0.1), Math.Min(1, glut)), 1));
        if (!Leer) Schicht(f.Ring(0, linie), rand);

        // Kantenglut: der helle rote Strich an der Unterkante (gewählt auch oben), mit Schein; gesperrt schwach und grau
        // getönt, damit ein gewählter gesperrter Reiter gewählt bleibt.
        var kantenglut = Kantenglut * p.GlutglasKantenglut * (!aktiv ? 0.3 : Zustand == Glaszustand.Ueberfahren ? 1.15 : 1);
        if (kantenglut > 0)
        {
            var dicke = Math.Max(1, Glasgeometrie.Raster(1.8, s));
            Kantenstrich(f, b * 0.18, h - dicke / 2, b * 0.64, kantenglut, dicke, schein: true);
            if (KantenglutOben) Kantenstrich(f, b * 0.18, dicke / 2, b * 0.64, kantenglut, dicke, schein: true);
        }

        // Seitenglut (Glutglas-Karte): links an der Scheibe, rechts an der Versatzscheibe, wie die Kantenglut gedämpft.
        var seitenglut = Seitenglut * p.GlutglasKantenglut * (!aktiv ? 0.3 : Zustand == Glaszustand.Ueberfahren ? 1.15 : 1);
        if (seitenglut > 0 && !Leer)
        {
            var dicke = Math.Max(1, Glasgeometrie.Raster(1.6, s));
            Seitenstrich(f, dicke / 2, h * 0.08, h * 0.84, seitenglut, dicke);
            Seitenstrich(f, versatz + b - dicke / 2, versatz + h * 0.1, h * 0.84, seitenglut, dicke);
        }
    }

    /// <summary>
    /// Ein heller roter senkrechter Strich mittig auf <paramref name="x"/> (Pixel) von <paramref name="y"/> an, zu den Enden
    /// auslaufend, mit weichem Schein nach beiden Seiten (der fällt bei Schattenstufe Aus weg) (seit 0.5.0-beta.10).
    /// </summary>
    private void Seitenstrich(Glasflaeche f, double x, double y, double laenge, double staerke, double dicke)
    {
        var s = f.Skala;
        var rot = Zustand == Glaszustand.Gesperrt ? Farbton.Mischen(Rot, Color.FromRgb(0x70, 0x72, 0x78), 0.6) : Rot;
        var mitte = y + laenge / 2;
        if (Schatten != Schattenstufe.Aus)
        {
            var rx = 15 * s;
            Schicht(Ellipse(f, x, mitte, rx, laenge * 0.62), Radial(f.Dip(x, mitte), rx / s, laenge * 0.62 / s,
                (Farbton.Deckend(rot, Math.Min(1, 0.7 * staerke)), 0), (Farbton.Deckend(rot, Math.Min(1, 0.28 * staerke)), 0.5), (Farbton.Deckend(rot, 0), 1)));
        }
        var hell = Farbton.Mischen(rot, Colors.White, 0.5);
        Schicht(Rechteck(f, Math.Round(x - dicke / 2), Math.Round(y), dicke, Math.Round(laenge)), Senkrecht(y / s, (y + laenge) / s,
            (Farbton.Deckend(rot, 0), 0), (Farbton.Deckend(Heller(rot, 0.12), Math.Min(1, staerke)), 0.3), (Farbton.Deckend(hell, Math.Min(1, staerke)), 0.5),
            (Farbton.Deckend(Heller(rot, 0.12), Math.Min(1, staerke)), 0.7), (Farbton.Deckend(rot, 0), 1)));
    }

    /// <summary>Spur eines Schalters: an satt in der Glutfarbe (nach rechts heller) mit hellem Rand und Schein darunter, aus grau.</summary>
    private void Spur(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var an = Glut > 0;
        var rot = Rot;
        var unten = h / s;
        if (an)
        {
            var c = aktiv ? rot : Farbton.Mischen(rot, Color.FromRgb(0x60, 0x62, 0x66), 0.7);
            if (aktiv && Schatten != Schattenstufe.Aus)
                Schicht(Ellipse(f, b / 2, h * 0.62, b * 0.62, h * 0.8), Radial(f.Dip(b / 2, h * 0.62), b * 0.62 / s, h * 0.8 / s,
                    (Farbton.Deckend(c, Math.Min(1, 0.45 * Glut * p.GlutglasGlut)), 0), (Farbton.Deckend(c, 0), 1)));
            Schicht(f.Form(0), Waagerecht(0, b / s, (Dunkler(c, 0.57), 0), (Dunkler(c, 0.15), 0.6), (c, 1)));
            Schicht(f.Form(0), Senkrecht(0, unten, (Weiss(0.16), 0), (Weiss(0), 0.5)));
            Schicht(f.Ring(0, Math.Max(1, Glasgeometrie.Raster(1.2, s))), Senkrecht(0, unten, (Heller(c, 0.42), 0), (Dunkler(c, 0.3), 1)));
            return;
        }
        var g = Spurgrau(Zustand == Glaszustand.Ueberfahren, aktiv);
        Schicht(f.Form(0), Senkrecht(0, unten, (g, 0), (Dunkler(g, 0.33), 1)));
        Schicht(f.Ring(0, Glasgeometrie.Haarlinie), Senkrecht(0, unten, (Weiss(aktiv ? 0.5 : 0.25), 0), (Weiss(0.1), 1)));
    }

    /// <summary>Knauf eines Schalters: eine Glaskugel, hell (an) oder dunkler (aus), mit kleinem Schatten.</summary>
    private void Knauf(Glasflaeche f, Stilprofil p)
    {
        var s = f.Skala;
        double b = f.Breite, h = f.Hoehe;
        var aktiv = Zustand != Glaszustand.Gesperrt;
        var an = Glut > 0;
        var (hell, mitte, dunkel) = an
            ? (Color.FromRgb(0xF7, 0xF8, 0xFA), Color.FromRgb(0xC9, 0xCD, 0xD4), Color.FromRgb(0x8C, 0x92, 0x9C))
            : (Color.FromRgb(0xB9, 0xBE, 0xC6), Color.FromRgb(0x7C, 0x82, 0x8C), Color.FromRgb(0x4E, 0x53, 0x5C));
        if (Zustand == Glaszustand.Ueberfahren)
        {
            hell = Heller(hell, p.FamilieUeberfahren);
            mitte = Heller(mitte, p.FamilieUeberfahren);
        }
        if (!aktiv) (hell, mitte, dunkel) = (Dunkler(hell, 0.3), Dunkler(mitte, 0.3), Dunkler(dunkel, 0.3));
        if (aktiv && Schatten != Schattenstufe.Aus)
            Schicht(Ellipse(f, b / 2, h * 0.58, b * 0.55, h * 0.55), Radial(f.Dip(b / 2, h * 0.58), b * 0.55 / s, h * 0.55 / s, (Schwarz(0.45), 0.6), (Schwarz(0), 1)));
        Schicht(f.Form(0), Radial(f.Dip(b * 0.4, h * 0.35), b * 0.7 / s, h * 0.7 / s, (hell, 0), (mitte, 0.6), (dunkel, 1)));
        // Aus liegt der dunkle Knauf auf der grauen Spur: unten eine dunklere Haarlinie, sonst verschwimmt er dort mit ihr
        // (Sarevocs Regel des gleichen Abstands misst sonst unten einen Pixel mehr).
        Schicht(f.Ring(0, Glasgeometrie.Haarlinie), Senkrecht(0, h / s, (Weiss(0.5), 0), (Schwarz(an ? 0.3 : 0.6), 1)));
    }

    /// <summary>
    /// Ein heller roter Strich mittig auf der Höhe <paramref name="y"/> (Pixel), zu den Enden auslaufend, mit weichem Schein
    /// (der reicht über die Form hinaus und fällt wie der übrige Schein bei Schattenstufe Aus weg).
    /// </summary>
    private void Kantenstrich(Glasflaeche f, double x, double y, double breite, double staerke, double dicke, bool schein)
    {
        var s = f.Skala;
        var rot = Zustand == Glaszustand.Gesperrt ? Farbton.Mischen(Rot, Color.FromRgb(0x70, 0x72, 0x78), 0.6) : Rot;
        if (schein && Schatten != Schattenstufe.Aus)
        {
            var ry = 9 * s;
            Schicht(Ellipse(f, x + breite / 2, y, breite * 0.62, ry), Radial(f.Dip(x + breite / 2, y), breite * 0.62 / s, ry / s,
                (Farbton.Deckend(rot, Math.Min(1, 0.55 * staerke)), 0), (Farbton.Deckend(rot, Math.Min(1, 0.2 * staerke)), 0.5), (Farbton.Deckend(rot, 0), 1)));
        }
        var hell = Farbton.Mischen(rot, Colors.White, 0.5);
        Schicht(Rechteck(f, x, Math.Round(y - dicke / 2), breite, dicke), Waagerecht(x / s, (x + breite) / s,
            (Farbton.Deckend(rot, 0), 0), (Farbton.Deckend(Heller(rot, 0.12), Math.Min(1, staerke)), 0.3), (Farbton.Deckend(hell, Math.Min(1, staerke)), 0.5),
            (Farbton.Deckend(Heller(rot, 0.12), Math.Min(1, staerke)), 0.7), (Farbton.Deckend(rot, 0), 1)));
    }
}

/// <summary>
/// Der rote Leuchtsaum der Stilfamilie Glutglas um Hauptknopf und Symbolknopf: eine weich gezeichnete Fläche im Ton der
/// Glut hinter dem Glas (Unschärfe als eingefrorener Effekt, Radius aus <see cref="Stilprofil.GlutglasReichweite"/>), etwas
/// nach unten versetzt und unten deckender, Stärke aus <see cref="Stilprofil.GlutglasSaum"/>. Er leuchtet ständig, solange
/// <see cref="Staerke"/> über 0 ist; die Vorlage schaltet ihn je Art. Wie der rote Schein unter der Scheibe ein Schein
/// außerhalb der Form: bei Schattenstufe Aus nicht gezeichnet. (Bis zur Prüfung zu 0.5.0-beta.6 aus Ringen wie der
/// Leuchtsaum; bei dieser Stärke zeigten die Rundungen Moiré und das gerade Stück unten harte Enden.)
/// </summary>
public sealed class Glutsaum : Familienkoerper
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Glutsaum), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlutFarbeProperty = DependencyProperty.Register(
        nameof(GlutFarbe), typeof(Color), typeof(Glutsaum), new FrameworkPropertyMetadata(Color.FromRgb(0xFF, 0x22, 0x36), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Anteil der Stärke aus dem Profil (0 aus, 1 voll).</summary>
    public static readonly DependencyProperty StaerkeProperty = DependencyProperty.Register(
        nameof(Staerke), typeof(double), typeof(Glutsaum), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public Color GlutFarbe { get => (Color)GetValue(GlutFarbeProperty); set => SetValue(GlutFarbeProperty, value); }
    public double Staerke { get => (double)GetValue(StaerkeProperty); set => SetValue(StaerkeProperty, value); }

    private protected override object Teilschluessel() => (FarbeVon(Farbe, GlutFarbe), Staerke);

    private protected override void Bauen(Glasflaeche f, Stilprofil p) { }

    private protected override void Nachgeaendert() => EffektSetzen();

    /// <summary>Die Unschärfe aus der Reichweite, eingefroren; neu nur bei neuer Reichweite.</summary>
    private void EffektSetzen()
    {
        var radius = Math.Max(1, Profil.GlutglasReichweite) * 0.6;
        if (Effect is BlurEffect alt && alt.Radius == radius) return;
        var neu = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        neu.Freeze();
        Effect = neu;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var p = Profil;
        var staerke = Staerke * p.GlutglasSaum * (Zustand == Glaszustand.Gesperrt || Schatten == Schattenstufe.Aus ? 0 : 1);
        double b = RenderSize.Width, h = RenderSize.Height;
        if (staerke <= 0 || b < 2 || h < 2) return;
        if (Effect is null) EffektSetzen();
        var rot = FarbeVon(Farbe, GlutFarbe);
        var r = Math.Min(b, h) * Math.Clamp(Rundung, 0, 0.5);
        // Unten kräftig, oben schwächer: die Fläche etwas größer als der Knopf (damit die Glut an der Kante kräftig bleibt)
        // und um ein Fünftel der Reichweite nach unten versetzt.
        var versatz = Math.Min(h * 0.25, p.GlutglasReichweite * 0.2);
        var weite = p.GlutglasReichweite * 0.15;
        var deckung = Math.Min(1, staerke);
        drawingContext.DrawRoundedRectangle(
            Senkrecht(versatz - weite / 2, h + versatz + weite / 2, (Farbton.Deckend(rot, deckung * 0.45), 0), (Farbton.Deckend(rot, deckung), 0.6), (Farbton.Deckend(rot, deckung), 1)),
            null, new Rect(-weite, versatz - weite / 2, b + 2 * weite, h + weite), r + weite, r + weite);
    }
}
