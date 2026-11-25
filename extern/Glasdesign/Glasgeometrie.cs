// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>Eine Schicht des Glaskörpers: Rechteck und Eckradius in geräteunabhängigen Einheiten (DIP).</summary>
public readonly record struct Schicht(Rect Rechteck, double Radius);

/// <summary>Die Schichten von Glas 3: der Umriss außen und der Körper innerhalb der Haarlinie.</summary>
public sealed record Glasschichten(Schicht Aussen, Schicht Koerper);

/// <summary>
/// Geometrie des Glaskörpers auf ganzen Gerätepixeln. Glas 3 (Fassung 0.3.0, Sarevocs Wahl vom 02.10.2026, Muster 3b): Die
/// Kante ist eine Haarlinie von genau einem Gerätepixel, bei jeder Skalierung ("sehr sehr feine Linien wie bei Apple"),
/// darin der Körper. Alles rechnet in Pixeln und teilt erst am Ende durch die Skalierung.
/// Voraussetzung: Das Element selbst liegt auf ganzen Pixeln (UseLayoutRounding am Fenster, LIESMICH.md, Grenzen).
/// </summary>
public static class Glasgeometrie
{
    /// <summary>Eckradius der Form Eckig als Anteil der Höhe (Vorlage: 20 bei 90).</summary>
    public const double Rundungsanteil = 0.22;

    /// <summary>Die Haarlinie: genau ein Gerätepixel, bei jeder Skalierung.</summary>
    public const int Haarlinie = 1;

    /// <summary>
    /// DIP in ganze Gerätepixel, gerundet wie die Layoutrundung von WPF (Math.Round, bei ,5 zur geraden Zahl). Nur so stimmt
    /// die gerechnete Pixelgröße mit der tatsächlich angeordneten überein: 26 DIP bei 125 % sind 32 Pixel, nicht 33.
    /// </summary>
    public static double Raster(double dip, double skala) => Math.Round(dip * skala);

    /// <summary>Eine Linienstärke in ganzen Gerätepixeln, mindestens eins.</summary>
    public static double Linie(double dip, double skala) => Math.Max(1, Raster(dip, skala));

    /// <summary>Skalierung eines Elements (Gerätepixel je DIP); ohne Quelle 1.</summary>
    public static double Skala(Visual element)
    {
        var s = VisualTreeHelper.GetDpi(element).DpiScaleX;
        return s > 0 ? s : 1;
    }

    /// <summary>Umriss und Körper für ein Element der Größe <paramref name="groesse"/> (DIP) bei <paramref name="skala"/>.</summary>
    public static Glasschichten Schichten(Size groesse, double skala, Glasform form)
    {
        var f = Glasflaeche.Aus(groesse, skala, form, null);
        return new Glasschichten(f.Schicht(0), f.Schicht(Haarlinie));
    }
}

/// <summary>
/// Ein runder Umriss in ganzen Gerätepixeln (Lage, Breite, Höhe, Eckradius) samt Skalierung. Liefert Formen und Ringe,
/// um ganze Pixel eingerückt, als eingefrorene Geometrie in DIP. Eine Pille hat den Radius der halben Höhe. Mit
/// <see cref="Umriss"/> (Abschnitt 6) gilt dessen Kontur; eingerückt wird sie über ihre verbreiterte Kontur, einmal je
/// Fläche und Einzug berechnet und eingefroren, und ohne die teure Faltung breiter Linien an engen Bögen und Löchern.
/// </summary>
internal readonly record struct Glasflaeche(double X, double Y, double Breite, double Hoehe, double Radius, double Skala)
{
    /// <summary>Eine beliebige Form statt des abgerundeten Rechtecks; null: das abgerundete Rechteck mit <see cref="Radius"/>.</summary>
    public Umriss? Umriss { get; init; }

    public static Glasflaeche Aus(Size groesse, double skala, Glasform form) => Aus(groesse, skala, form, null);

    public static Glasflaeche Aus(Size groesse, double skala, Glasform form, Umriss? umriss)
    {
        var b = Glasgeometrie.Raster(groesse.Width, skala);
        var h = Glasgeometrie.Raster(groesse.Height, skala);
        if (umriss is not null) return new Glasflaeche(0, 0, b, h, umriss.Eckradius(b, h, skala), skala) { Umriss = umriss };
        var r = form == Glasform.Pille ? Math.Min(b, h) / 2 : Glasgeometrie.Raster(Math.Min(b, h) * Glasgeometrie.Rundungsanteil, 1);
        return new Glasflaeche(0, 0, b, h, r, skala);
    }

    /// <summary>Eingerückte Konturen je Fläche und Einzug; begrenzt, damit ein Regler, der die Größe zieht, nicht alles behält.</summary>
    private static readonly Dictionary<(Glasflaeche, double), Geometry> Konturen = [];

    public Schicht Schicht(double rand) => new(
        new Rect((X + rand) / Skala, (Y + rand) / Skala, Math.Max(0, Breite - 2 * rand) / Skala, Math.Max(0, Hoehe - 2 * rand) / Skala),
        Math.Max(0, Radius - rand) / Skala);

    /// <summary>Die Form, um <paramref name="rand"/> Pixel eingerückt (negativ: hinausgeschoben, für den Leuchtsaum).</summary>
    public Geometry Form(double rand)
    {
        if (Umriss is not null) return Kontur(rand);
        var s = Schicht(rand);
        var g = new RectangleGeometry(s.Rechteck, s.Radius, s.Radius);
        g.Freeze();
        return g;
    }

    /// <summary>
    /// Ring zwischen den Einrückungen <paramref name="aussen"/> und <paramref name="innen"/> (Pixel): zwei ineinander liegende
    /// Formen mit EvenOdd-Füllung. Keine CombinedGeometry: deren Mengenoperation rechnet WPF bei jedem Zeichnen neu
    /// (gemessen am 02.10.2026: 60 Leuchtschalter bei 175 % 91 statt rund 50 ms).
    /// </summary>
    public Geometry Ring(double aussen, double innen)
    {
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(Form(aussen));
        g.Children.Add(Form(innen));
        g.Freeze();
        return g;
    }

    /// <summary>
    /// Je Fläche und Richtung (1 nach innen, -1 nach außen) der größte Einzug in ganzen Pixeln, dessen verbreiterte Linie nicht
    /// faltet (<see cref="Faltgrenze"/>); geleert mit den Konturen.
    /// </summary>
    private static readonly Dictionary<(Glasflaeche, int), int> Faltgrenzen = [];

    /// <summary>
    /// Die Kontur des Umrisses um <paramref name="rand"/> Pixel eingerückt: die Kontur ohne (oder mit) ihrer um
    /// 2 mal rand verbreiterten Linie (<see cref="Eingerueckt"/>). Gerade Kanten auf ganzen Pixeln bleiben dabei auf ganzen Pixeln.
    /// </summary>
    private Geometry Kontur(double rand)
    {
        if (Konturen.TryGetValue((this, rand), out var fertig)) return fertig;
        var aussen = Umriss!.Kontur(Breite, Hoehe, Skala).Clone();
        aussen.Transform = new MatrixTransform(1 / Skala, 0, 0, 1 / Skala, X / Skala, Y / Skala);
        var g = rand == 0 ? aussen : Eingerueckt(aussen, rand);
        g.Freeze();
        if (Konturen.Count > 512)
        {
            Konturen.Clear();
            Faltgrenzen.Clear();
        }
        Konturen[(this, rand)] = g;
        return g;
    }

    /// <summary>
    /// Die Kontur ohne (oder mit) ihrer verbreiterten Linie. Ist die Linie breiter als der engste Bogen der Kontur (ein
    /// Leuchtsaum von 24 Pixeln um ein Rechteck mit 6 DIP Ecke) oder als ein Loch darin, faltet WPF sie zu einem Fächer aus
    /// Dutzenden Stücken, und Geometry.Combine braucht dafür das Hundertfache (gemessen am 14.10.2026 bei 175 Prozent:
    /// 18 bis 30 ms je Ring statt 0,1 ms, am Loch eines Verbunds bis 600 ms; ein Regler in Glint brauchte so 0,4 bis 2,4 s
    /// je Schritt). Dann, in dieser Reihenfolge und nur, wenn danach nichts mehr faltet:
    /// <list type="number">
    /// <item>Löcher, die der Abstand nach außen sicher zudeckt, fallen weg (<see cref="OhneZugedeckteLoecher"/>): am
    /// Ergebnis ändern sie nichts.</item>
    /// <item>Zwei Züge statt einem: von der größten Einrückung aus, deren Linie nicht faltet (<see cref="Faltgrenze"/>), den
    /// Rest. Nach außen wie nach innen ist Abstand a, dann b dasselbe wie a + b (Minkowski mit Kreisscheiben); der Bogen ist
    /// nach dem ersten Zug um a weiter, der zweite faltet nicht mehr. Die Kanten weichen von der direkten Rechnung im Mittel
    /// um höchstens 0,003 DIP ab, weniger als deren eigene Toleranz von 0,01.</item>
    /// <item>Sonst wie bisher, nach außen ohne die Fächerstücke, die ganz in der Form liegen (<see cref="OhneInnere"/>):
    /// Punkt für Punkt dasselbe Ergebnis, nur schneller verbunden.</item>
    /// </list>
    /// Faltet die Linie nicht, bleibt alles wie bis 0.5.0-beta.3.
    /// </summary>
    private PathGeometry Eingerueckt(Geometry aussen, double rand)
    {
        var modus = rand > 0 ? GeometryCombineMode.Exclude : GeometryCombineMode.Union;
        var linie = Verbreitert(aussen, rand);
        if (!Gefaltet(linie, aussen)) return Verbunden(aussen, linie, modus);
        var basis = aussen;
        if (OhneZugedeckteLoecher(aussen, rand) is { } voll)
        {
            basis = voll;
            linie = Verbreitert(voll, rand);
            if (!Gefaltet(linie, voll)) return Verbunden(voll, linie, modus);
        }
        if (Faltgrenze(aussen, rand) is var erster && erster != 0)
        {
            var zwischen = Kontur(erster);
            if (OhneZugedeckteLoecher(zwischen, rand - erster) is { } zwischenVoll) zwischen = zwischenVoll;
            var rest = Verbreitert(zwischen, rand - erster);
            if (!Gefaltet(rest, zwischen)) return Verbunden(zwischen, rest, modus);
        }
        GefalteteVerbindungen++;
        return Verbunden(basis, rand < 0 ? OhneInnere(linie, basis) : linie, modus);
    }

    /// <summary>Die Linie der Kontur, 2 mal <paramref name="rand"/> Pixel breit, mit runden Ecken (so breit wie der Abstand nach beiden Seiten).</summary>
    private PathGeometry Verbreitert(Geometry form, double rand) =>
        form.GetWidenedPathGeometry(new Pen(Brushes.Black, 2 * Math.Abs(rand) / Skala) { LineJoin = PenLineJoin.Round }, 0.01, ToleranceType.Absolute);

    private static PathGeometry Verbunden(Geometry form, Geometry linie, GeometryCombineMode modus) =>
        Geometry.Combine(form, linie, modus, null, 0.01, ToleranceType.Absolute);

    /// <summary>
    /// Der größte Einzug in ganzen Pixeln in Richtung von <paramref name="rand"/> und kleiner als dieser, dessen Linie nicht
    /// faltet; 0, wenn keiner. Falten wächst mit dem Abstand: halbierend gesucht bis 64 Pixel, einmal je Fläche und Richtung.
    /// </summary>
    private double Faltgrenze(Geometry aussen, double rand)
    {
        var richtung = Math.Sign(rand);
        if (!Faltgrenzen.TryGetValue((this, richtung), out var grenze))
        {
            int gut = 0, schlecht = 65;
            while (schlecht - gut > 1)
            {
                var mitte = (gut + schlecht) / 2;
                if (Gefaltet(Verbreitert(aussen, richtung * mitte), aussen)) schlecht = mitte;
                else gut = mitte;
            }
            Faltgrenzen[(this, richtung)] = grenze = gut;
        }
        return richtung * Math.Min(grenze, Math.Abs(rand) - 1);
    }

    /// <summary>
    /// Die Form ohne die Löcher, die ein Abstand von <paramref name="rand"/> Pixeln nach außen sicher zudeckt; null, wenn
    /// <paramref name="rand"/> nach innen geht oder keines wegfällt. Loch heißt: eine Figur, deren Grenzen ganz in denen einer
    /// anderen liegen und deren Mitte sie selbst füllt, die Form aber nicht. Kein Punkt eines Lochs liegt weiter als die halbe
    /// kürzere Seite seiner Grenzen von dessen Rand; ist der Abstand mindestens so groß, ist das Loch zu, und jeder Punkt nahe
    /// dem Loch liegt auch nahe der Form. Das Ergebnis bleibt also dasselbe.
    /// </summary>
    private PathGeometry? OhneZugedeckteLoecher(Geometry form, double rand)
    {
        if (rand >= 0) return null;
        var p = form as PathGeometry ?? PathGeometry.CreateFromGeometry(form);
        if (p.Figures.Count < 2) return null;
        var einzeln = p.Figures.Select(f => new PathGeometry([f.Clone()]) { Transform = p.Transform }).ToList();
        var bleiben = new PathGeometry { FillRule = p.FillRule, Transform = p.Transform };
        var weg = 0;
        for (var i = 0; i < einzeln.Count; i++)
        {
            var b = einzeln[i].Bounds;
            var mitte = new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
            var loch = einzeln.Where((_, j) => j != i).Any(e => e.Bounds.Contains(b)) && einzeln[i].FillContains(mitte) && !p.FillContains(mitte);
            if (loch && -rand / Skala >= Math.Min(b.Width, b.Height) / 2) weg++;
            else bleiben.Figures.Add(p.Figures[i].Clone());
        }
        return weg == 0 ? null : bleiben;
    }

    /// <summary>
    /// Die Linie ohne die Stücke, die ganz in der Form liegen: die Fächer der Innenseite, die nach außen (vereinigt mit der
    /// Form) nichts beitragen. Das Ergebnis bleibt Punkt für Punkt dasselbe (verglichen an 1440 Konturen am 14.10.2026).
    /// </summary>
    private static PathGeometry OhneInnere(PathGeometry linie, Geometry form)
    {
        var flach = form.GetFlattenedPathGeometry(0.001, ToleranceType.Absolute);
        var rest = new PathGeometry { FillRule = linie.FillRule };
        foreach (var f in linie.Figures)
            if (flach.FillContainsWithDetail(new PathGeometry([f]), 0.001, ToleranceType.Absolute) != IntersectionDetail.FullyContains) rest.Figures.Add(f);
        return rest;
    }

    /// <summary>Wie oft eine gefaltete Linie doch direkt verbunden wurde: die teuren Fälle, die übrig bleiben (für die Tests).</summary>
    internal static int GefalteteVerbindungen { get; private set; }

    /// <summary>
    /// Ob die verbreiterte Linie gefaltet ist: Ungefaltet gibt WPF je Figur der Kontur höchstens zwei Figuren zurück, gefaltet
    /// einen Fächer aus Dutzenden, die sich überdecken.
    /// </summary>
    private static bool Gefaltet(PathGeometry linie, Geometry form) => linie.Figures.Count > 2 * Figuren(form);

    private static int Figuren(Geometry g) => (g as PathGeometry ?? PathGeometry.CreateFromGeometry(g)).Figures.Count;

    /// <summary>
    /// Die äußere Kontur in Pixeln dieser Fläche (Ursprung oben links, ohne Lage): die des Umrisses oder das abgerundete
    /// Rechteck mit <see cref="Radius"/>. Bögen bleiben Bögen; der Fresnel-Rand liest daraus, wo sich die Kontur biegt.
    /// </summary>
    public Geometry KonturPx()
    {
        if (Umriss is not null) return Umriss.Kontur(Breite, Hoehe, Skala);
        var r = Math.Min(Radius, Math.Min(Breite, Hoehe) / 2);
        var g = new RectangleGeometry(new Rect(0, 0, Breite, Hoehe), r, r);
        g.Freeze();
        return g;
    }

    /// <summary>Ein Punkt in Pixeln dieser Fläche, als DIP im Element.</summary>
    public Point Dip(double x, double y) => new((X + x) / Skala, (Y + y) / Skala);
}
