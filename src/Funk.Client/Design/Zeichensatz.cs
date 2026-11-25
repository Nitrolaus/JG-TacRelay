// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Design;

/// <summary>Die Form des Lorbeerkranzes (A49): drei Varianten zum Aussuchen, gezeichnet im Vergleichsbild zeichen-varianten.png.</summary>
public enum Kranzform
{
    /// <summary>Sechs Blattpaare je Zweig, innen und außen, am Stiel entlang: der klassische Kranz.</summary>
    Klassisch,

    /// <summary>Vier große Blätter je Zweig, nur außen: bleibt auch in 14 Bildpunkten lesbar.</summary>
    Kompakt,

    /// <summary>Fünf kantige Blätter wie Rauten, ohne Rundungen: die klare, technische Anmutung der Star-Citizen-Oberflächen.</summary>
    Kantig,
}

/// <summary>Ein Zeichen zum Anzeigen: Bild und Klartext für Tooltip und Vorleser.</summary>
public sealed record Zeichenbild(string Kennung, ImageSource Bild, string Titel);

/// <summary>
/// Die Zeichen neben Namen (A49) und seit 0.11.7 die Abzeichen. Der Server schickt nur Kennungen, der Client zeichnet aus diesem eingebauten
/// Satz. Die automatischen Zeichen sind selbst gezeichnete Vektoren (keine Vorlage von CIG, RSI oder Discord; der Lorbeerkranz ist ein
/// allgemeines Motiv): <c>ADMIN</c> ein goldener Lorbeerkranz "im SC-Stil" (klare Linien, metallischer Goldverlauf, feine Kante),
/// <c>EINSATZLEITUNG</c> ein kleiner Stern für einen laufenden Auftrag (Stufe 2). Beide messen 32 x 32 Einheiten und werden eingefroren, damit
/// sie in jeder Größe scharf bleiben und über Threads geteilt werden können. <c>FUEHRUNG</c> (silberne Winkel) war ein Platzhalter und wird
/// seit 0.11.7 nicht mehr gezeichnet (Sarevoc 14./15.10.2026); die Kennung bleibt ein Merkmal der Stufe, an dem Logik hängt.
/// Die Abzeichen des Katalogs (<see cref="Abzeichenkatalog"/>: Ränge in drei Laufbahnen, Tätigkeiten) sind PNG-Dateien in TacRelay.dll
/// (Abzeichen/KENNUNG.png, 128 x 128, selbst gezeichnet in grafik/abzeichen), einmal geladen, eingefroren und gecacht.
/// Kontrast (OberflaecheWerIstDaTests): Füllungen mindestens 3:1 gegen alle dunklen Flächen des Clients. Die Kante des Kranzes
/// ist ein mittleres Gold, das sich 3:1 von jeder Fläche des Clients (auch der Leitfarbe der Titelleiste) und von Weiß abhebt;
/// eine fast schwarze Kante verschwand auf dunklem Grund und fraß in 14 Bildpunkten die dünnen Blätter auf (gemessen).
/// </summary>
public static class Zeichensatz
{
    public const string Admin = "ADMIN";
    public const string Fuehrung = "FUEHRUNG";
    public const string Einsatzleitung = "EINSATZLEITUNG";

    /// <summary>
    /// Welcher Kranz das Admin-Zeichen ist: Klassisch, von Sarevoc im Vergleichsbild gewählt (30.09.2026, A53). Ändern heißt: diese
    /// Zeile; die Kontrastprüfung (OberflaecheWerIstDaTests) verlangt von der Voreinstellung schon 14 Bildpunkte.
    /// </summary>
    public const Kranzform Voreinstellung = Kranzform.Klassisch;

    public static Color GoldHell { get; } = Color.FromRgb(0xF6, 0xDC, 0x8A);
    public static Color GoldMitte { get; } = Color.FromRgb(0xD4, 0xA4, 0x3A);
    public static Color GoldDunkel { get; } = Color.FromRgb(0xA8, 0x7A, 0x20);
    public static Color GoldKante { get; } = Color.FromRgb(0xB8, 0x88, 0x28);
    public static Color SternFarbe { get; } = Color.FromRgb(0x7F, 0xD0, 0xFF);
    public static Color SternKante { get; } = Color.FromRgb(0x0B, 0x2E, 0x45);

    private static readonly Dictionary<Kranzform, DrawingImage> Kraenze = [];
    private static readonly Lock Sperre = new();
    private static DrawingImage? _stern;
    private static readonly Dictionary<string, ImageSource> Abzeichenbilder = new(StringComparer.Ordinal);

    /// <summary>Breite, auf die ein Abzeichen beim Laden verkleinert wird: scharf bis 32 Einheiten bei 200 % Anzeigeskalierung.</summary>
    public const int AbzeichenPixel = 64;

    /// <summary>Name der eingebetteten PNG zu einer Kennung des Katalogs (Funk.Client.csproj, LogicalName).</summary>
    public static string Ressource(string kennung) => "Abzeichen." + kennung + ".png";

    /// <summary>
    /// Klartext zu einer Kennung: "Admin", "Einsatzleitung (Auftrag für heute)", aus dem Katalog "Marine: Sergeant" oder "Gunner M"; unbekannte
    /// Kennungen (etwa Abzeichen eines neueren Katalogs) bleiben, wie sie sind.
    /// </summary>
    public static string Titel(string kennung) => kennung switch
    {
        Admin => "Admin",
        Fuehrung => "Führung",
        Einsatzleitung => "Einsatzleitung (Auftrag für heute)",
        _ => Abzeichenkatalog.Titel(kennung) ?? kennung,
    };

    /// <summary>
    /// Das Bild zu einer Kennung, oder <c>null</c> für Kennungen, die dieser Client nicht zeichnet: unbekannte und seit 0.11.7 <c>FUEHRUNG</c>.
    /// </summary>
    public static ImageSource? Bild(string kennung, Kranzform kranz = Voreinstellung) => kennung switch
    {
        Admin => Kranz(kranz),
        Einsatzleitung => SternBild(),
        _ => Abzeichenkatalog.Kennt(kennung) ? AbzeichenBild(kennung) : null,
    };

    /// <summary>
    /// Die Zeichen einer Liste von Kennungen in fester Reihenfolge (<see cref="Abzeichenkatalog.Reihenfolge"/>: ADMIN, EINSATZLEITUNG, dann Ränge
    /// in Laufbahnfolge, dann Tätigkeiten in Katalogfolge); was dieser Client nicht zeichnet, fällt weg.
    /// </summary>
    public static IReadOnlyList<Zeichenbild> Bilder(IEnumerable<string>? kennungen) =>
        kennungen is null
            ? []
            : [.. kennungen.Distinct(StringComparer.Ordinal)
                .OrderBy(Abzeichenkatalog.Reihenfolge)
                .Select(k => Bild(k) is { } b ? new Zeichenbild(k, b, Titel(k)) : null)
                .OfType<Zeichenbild>()];

    /// <summary>
    /// Was andere von einer Person sehen (0.11.7): die automatischen Zeichen der Praesenz und ihre sichtbaren Abzeichen in einer Liste, geordnet
    /// wie <see cref="Bilder"/>. Ausgeblendetes schickt der Server gar nicht erst.
    /// </summary>
    public static IReadOnlyList<Zeichenbild> Sichtbare(IEnumerable<string>? zeichen, IEnumerable<string>? abzeichen) =>
        Bilder((zeichen ?? []).Concat(abzeichen ?? []));

    /// <summary>
    /// Das Abzeichen "vorn" (vor dem Namen in Wer ist da und auf der Personenkachel): das gewählte, wenn es unter den sichtbaren steht, sonst das
    /// erste sichtbare (Automatikzeichen, dann Ränge, dann Tätigkeiten); ohne sichtbare <c>null</c>.
    /// </summary>
    public static Zeichenbild? Vorn(IReadOnlyList<Zeichenbild> sichtbar, string? gewaehlt)
    {
        ArgumentNullException.ThrowIfNull(sichtbar);
        return sichtbar.FirstOrDefault(z => string.Equals(z.Kennung, gewaehlt, StringComparison.Ordinal)) ?? (sichtbar.Count > 0 ? sichtbar[0] : null);
    }

    /// <summary>Das PNG eines Abzeichens aus dem Katalog, einmal geladen und eingefroren; <c>null</c>, wenn es in TacRelay.dll fehlt.</summary>
    public static ImageSource? AbzeichenBild(string kennung)
    {
        ArgumentNullException.ThrowIfNull(kennung);
        lock (Sperre)
        {
            if (Abzeichenbilder.TryGetValue(kennung, out var b)) return b;
            using var strom = typeof(Zeichensatz).Assembly.GetManifestResourceStream(Ressource(kennung));
            if (strom is null) return null;
            var bild = new BitmapImage();
            bild.BeginInit();
            bild.CacheOption = BitmapCacheOption.OnLoad;
            bild.DecodePixelWidth = AbzeichenPixel;
            bild.StreamSource = strom;
            bild.EndInit();
            bild.Freeze();
            Abzeichenbilder[kennung] = bild;
            return bild;
        }
    }

    public static DrawingImage Kranz(Kranzform form)
    {
        lock (Sperre)
        {
            if (Kraenze.TryGetValue(form, out var b)) return b;
            b = Einfrieren(KranzZeichnung(form));
            Kraenze[form] = b;
            return b;
        }
    }

    public static DrawingImage SternBild()
    {
        lock (Sperre) return _stern ??= Einfrieren(Stern());
    }

    // ---------------------------------------------------------------- Zeichnungen

    private static DrawingImage Einfrieren(Drawing d)
    {
        var bild = new DrawingImage(d);
        bild.Freeze();
        return bild;
    }

    /// <summary>Metallischer Verlauf von links oben (hell) nach rechts unten (dunkel).</summary>
    private static LinearGradientBrush Verlauf(Color hell, Color mitte, Color dunkel)
    {
        var p = new LinearGradientBrush([new GradientStop(hell, 0), new GradientStop(mitte, 0.5), new GradientStop(dunkel, 1)], new Point(0, 0), new Point(1, 1));
        p.Freeze();
        return p;
    }

    private static Pen Stift(Color farbe, double staerke)
    {
        var p = new Pen(new SolidColorBrush(farbe), staerke) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    /// <summary>Fester Rahmen 32 x 32, damit alle Zeichen gleich groß und gleich ausgerichtet erscheinen.</summary>
    private static GeometryDrawing Rahmen() => new(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32)));

    private static DrawingGroup KranzZeichnung(Kranzform form)
    {
        const double cx = 16, cy = 16.5, r = 10.6;
        var (anzahl, laenge, breite, innen) = form switch
        {
            Kranzform.Klassisch => (6, 6.4, 2.9, true),
            Kranzform.Kompakt => (4, 9.0, 4.6, false),
            _ => (5, 7.8, 4.0, false),
        };
        var blaetter = new PathGeometry { FillRule = FillRule.Nonzero };
        var stiel = new PathGeometry();
        foreach (var seite in new[] { -1, 1 })
        {
            // Bildschirmwinkel: 90° unten, 180° links, 270° oben. Der linke Zweig wächst von unten (100°) nach oben (248°); der
            // rechte ist sein Spiegelbild an der senkrechten Mitte. Oben bleibt eine Lücke wie bei jedem Lorbeerkranz.
            const double start = 100, ende = 248;
            stiel.Figures.Add(Bogen(cx, cy, r, start, ende, seite));
            for (var i = 0; i < anzahl; i++)
            {
                var t = (i + 0.55) / anzahl;
                var theta = (start + (t * (ende - start))) * Math.PI / 180;
                var p = new Point(cx + (r * Math.Cos(theta)), cy + (r * Math.Sin(theta)));
                var tangente = new Vector(-Math.Sin(theta), Math.Cos(theta));
                var aussen = new Vector(Math.Cos(theta), Math.Sin(theta));
                if (seite == 1)
                {
                    p = new Point((2 * cx) - p.X, p.Y);
                    tangente = new Vector(-tangente.X, tangente.Y);
                    aussen = new Vector(-aussen.X, aussen.Y);
                }
                // Nach oben kleiner werdend, wie ein Zweig ausläuft.
                var l = laenge * (1.08 - (0.28 * t));
                var b = breite * (1.08 - (0.28 * t));
                blaetter.Figures.Add(Blatt(p, Richtung(tangente, aussen, 38), l, b, form == Kranzform.Kantig));
                if (innen) blaetter.Figures.Add(Blatt(p, Richtung(tangente, aussen, -30), l * 0.8, b * 0.85, false));
            }
        }
        var gruppe = new DrawingGroup();
        gruppe.Children.Add(Rahmen());
        var gold = Verlauf(GoldHell, GoldMitte, GoldDunkel);
        gruppe.Children.Add(new GeometryDrawing(null, Stift(GoldKante, form == Kranzform.Kompakt ? 2.0 : 1.5), stiel));
        gruppe.Children.Add(new GeometryDrawing(gold, Stift(GoldKante, form == Kranzform.Kantig ? 0.8 : 0.9), blaetter));
        if (form != Kranzform.Kantig)
        {
            // Die gekreuzten Stielenden unten, fein wie ein Band.
            var band = new PathGeometry();
            band.Figures.Add(new PathFigure(new Point(cx - 3.2, cy + r + 1.6), [new LineSegment(new Point(cx + 1.2, cy + r - 1.4), true)], false));
            band.Figures.Add(new PathFigure(new Point(cx + 3.2, cy + r + 1.6), [new LineSegment(new Point(cx - 1.2, cy + r - 1.4), true)], false));
            gruppe.Children.Add(new GeometryDrawing(null, Stift(GoldMitte, 1.4), band));
        }
        gruppe.Freeze();
        return gruppe;
    }

    /// <summary>Richtung eines Blatts: die Tangente um <paramref name="grad"/> nach außen (positiv) oder innen gedreht.</summary>
    private static Vector Richtung(Vector tangente, Vector aussen, double grad)
    {
        var a = grad * Math.PI / 180;
        var v = (tangente * Math.Cos(a)) + (aussen * Math.Sin(a));
        v.Normalize();
        return v;
    }

    /// <summary>Ein Blatt vom Ansatz <paramref name="basis"/> in Richtung <paramref name="d"/>: spitz an beiden Enden oder als Raute.</summary>
    private static PathFigure Blatt(Point basis, Vector d, double laenge, double breite, bool kantig)
    {
        var n = new Vector(-d.Y, d.X);
        var spitze = basis + (d * laenge);
        var mitte = basis + (d * laenge * (kantig ? 0.42 : 0.5));
        if (kantig)
            return new PathFigure(basis, [new PolyLineSegment([mitte + (n * breite * 0.5), spitze, mitte - (n * breite * 0.5)], true)], true);
        return new PathFigure(basis,
        [
            new QuadraticBezierSegment(mitte + (n * breite), spitze, true),
            new QuadraticBezierSegment(mitte - (n * breite), basis, true),
        ], true);
    }

    private static PathFigure Bogen(double cx, double cy, double r, double vonGrad, double bisGrad, int seite)
    {
        Point Punkt(double grad)
        {
            var a = grad * Math.PI / 180;
            var x = cx + (r * Math.Cos(a));
            return new Point(seite == 1 ? (2 * cx) - x : x, cy + (r * Math.Sin(a)));
        }
        var start = Punkt(vonGrad);
        var ende = Punkt(bisGrad);
        // Links läuft der Bogen im Uhrzeigersinn von unten über links nach oben, rechts gespiegelt gegen den Uhrzeigersinn.
        return new PathFigure(start, [new ArcSegment(ende, new Size(r, r), 0, false, seite == -1 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true)], false);
    }

    /// <summary>Fünfzackiger Stern für die Einsatzleitung (laufender Auftrag, Stufe 2).</summary>
    private static DrawingGroup Stern()
    {
        var punkte = new List<Point>(10);
        for (var i = 0; i < 10; i++)
        {
            var a = (-90 + (i * 36)) * Math.PI / 180;
            var r = i % 2 == 0 ? 13.0 : 5.4;
            punkte.Add(new Point(16 + (r * Math.Cos(a)), 16.8 + (r * Math.Sin(a))));
        }
        var g = new PathGeometry();
        g.Figures.Add(new PathFigure(punkte[0], [new PolyLineSegment(punkte.Skip(1), true)], true));
        var gruppe = new DrawingGroup();
        gruppe.Children.Add(Rahmen());
        var fuellung = new SolidColorBrush(SternFarbe);
        fuellung.Freeze();
        gruppe.Children.Add(new GeometryDrawing(fuellung, Stift(SternKante, 0.8), g));
        gruppe.Freeze();
        return gruppe;
    }
}
