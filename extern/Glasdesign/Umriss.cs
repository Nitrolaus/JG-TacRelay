// Glasdesign - Maintainer: Sarevoc
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Ein Umriss für Glas auf beliebiger Form (Glint Abschnitt 6): eine Beschreibung in Textform, aus der für jede Größe und
/// Skalierung die Kontur in ganzen Gerätepixeln entsteht. Die Glasfläche rückt sie um ganze Pixel ein (Haarlinie, Ringe,
/// Körper) und schiebt sie für den Leuchtsaum hinaus. Unveränderlich nach dem Setzen von <see cref="Text"/>; gleich ist,
/// was denselben Text hat.
/// <para>Formen (Maße in DIP, Lagen als Anteil der Breite und Höhe):</para>
/// <list type="bullet">
/// <item><c>Pille</c>, <c>Kreis</c>, <c>Sechseck</c>, <c>Achteck</c></item>
/// <item><c>Rechteck r</c> oder <c>Rechteck r1 r2 r3 r4</c>: Eckradien oben links, oben rechts, unten rechts, unten links</item>
/// <item><c>Fase f</c> oder <c>Fase f1 f2 f3 f4</c>: abgeschrägte Ecken wie im JG-Zeichen</item>
/// <item><c>Reiter r</c>: oben rund, unten gerade</item>
/// <item><c>Pfeil rechts s</c>, <c>Pfeil links s</c>, <c>Winkel rechts s</c>, <c>Winkel links s</c>: Spitze s als Anteil der Höhe</item>
/// <item><c>Pfad x,y,r x,y,r ...</c>: freie Form aus Knoten (Lage 0 bis 1), r rundet die Ecke am Knoten zu einem Bogen</item>
/// <item><c>Vereinigen(A; B; x y b h)</c>, <c>Ausschneiden(...)</c>, <c>Ueberschneiden(...)</c>: B in das Rechteck x y b h (Anteile) gelegt</item>
/// </list>
/// </summary>
[TypeConverter(typeof(UmrissConverter))]
public sealed class Umriss : IEquatable<Umriss>
{
    private Teil _teil = new Grundform("Pille", []);
    private string _text = "Pille";

    public Umriss()
    {
    }

    public Umriss(string text) => Text = text;

    /// <summary>Die Beschreibung; beim Setzen gelesen und in die einheitliche Schreibweise gebracht. Fehler werfen FormatException.</summary>
    public string Text
    {
        get => _text;
        set
        {
            var leser = new Leser(value ?? throw new ArgumentNullException(nameof(value)));
            var teil = leser.Teil();
            leser.Ende();
            _teil = teil;
            _text = teil.ToString()!;
        }
    }

    /// <summary>Den Text lesen; null, wenn er keine gültige Form beschreibt.</summary>
    public static Umriss? Lesen(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return new Umriss(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Die Kontur in Gerätepixeln für eine Fläche von <paramref name="breite"/> mal <paramref name="hoehe"/> Pixeln, Ursprung oben links.</summary>
    public Geometry Kontur(double breite, double hoehe, double skala)
    {
        var g = _teil.Bauen(breite, hoehe, skala);
        g.Freeze();
        return g;
    }

    /// <summary>Der Radius der Ecke oben links in Pixeln; daran richten sich Randglanz und Gegenglanz aus.</summary>
    public double Eckradius(double breite, double hoehe, double skala) => _teil.Eckradius(breite, hoehe, skala);

    /// <summary>
    /// Die Fasen der Kontur (für den Fresnel-Rand), in Pixeln wie <see cref="Kontur"/>, je Anfang und Ende: gerade Stücke,
    /// die wie eine Rundung leuchten. Bögen und scharfe Ecken findet der Fresnel-Rand an der Kontur selbst (<see cref="Fresnelrand"/>);
    /// eine Fase sieht dort aus wie jede andere gerade Kante, deshalb nennt die Form sie. Ein Verbund nennt die Fasen beider
    /// Teile; welche davon und wie viel auf seiner Kontur liegt, entscheidet der Fresnel-Rand.
    /// </summary>
    internal (Point A, Point B)[] Fasen(double breite, double hoehe, double skala) => [.. _teil.Fasen(breite, hoehe, skala)];

    /// <summary>
    /// Die geraden Kanten der Formen, aus denen die Kontur entsteht (für den Fresnel-Rand), in Pixeln wie <see cref="Kontur"/>,
    /// je Anfang und Ende. Eine einfache Form nennt die geraden Stücke ihrer Kontur, ein Verbund die seiner beiden Teile:
    /// Geometry.Combine setzt an flachen Schnittpunkten zweier Bögen und am Scheitel einer Ellipse kurze gerade Stücke in
    /// seine Kontur, die in Wahrheit auf einem Bogen liegen. Gerade ist an der Kontur nur, was auf einer dieser Kanten liegt.
    /// </summary>
    internal (Point A, Point B)[] Geraden(double breite, double hoehe, double skala) => [.. _teil.Geraden(breite, hoehe, skala)];

    public override string ToString() => _text;

    public bool Equals(Umriss? other) => other is not null && other._text == _text;

    public override bool Equals(object? obj) => Equals(obj as Umriss);

    public override int GetHashCode() => _text.GetHashCode(StringComparison.Ordinal);

    // ------------------------------------------------------------------ Teile

    private abstract class Teil
    {
        public abstract Geometry Bauen(double b, double h, double s);

        public virtual double Eckradius(double b, double h, double s) => 0;

        /// <summary>Die Fasen der Kontur, die <see cref="Bauen"/> für dieselbe Größe liefert; die meisten Formen haben keine.</summary>
        public virtual IEnumerable<(Point A, Point B)> Fasen(double b, double h, double s) => [];

        /// <summary>Die geraden Kanten der Kontur, die <see cref="Bauen"/> für dieselbe Größe liefert.</summary>
        public virtual IEnumerable<(Point A, Point B)> Geraden(double b, double h, double s) => Strecken(Bauen(b, h, s));
    }

    /// <summary>Die Strecken einer Geometrie (Linien, Linienzüge und die Kante, die eine Figur schließt), je Anfang und Ende.</summary>
    private static IEnumerable<(Point A, Point B)> Strecken(Geometry g)
    {
        foreach (var figur in PathGeometry.CreateFromGeometry(g).Figures)
        {
            var p = figur.StartPoint;
            foreach (var segment in figur.Segments)
            {
                if (segment is LineSegment l)
                {
                    yield return (p, l.Point);
                    p = l.Point;
                }
                else if (segment is PolyLineSegment pl)
                {
                    foreach (var q in pl.Points)
                    {
                        yield return (p, q);
                        p = q;
                    }
                }
                else p = Ende(segment, p);
            }
            if (figur.IsClosed && p != figur.StartPoint) yield return (p, figur.StartPoint);
        }
    }

    /// <summary>Der Endpunkt eines gebogenen Segments; ohne Punkte der Anfang <paramref name="p"/>.</summary>
    private static Point Ende(PathSegment segment, Point p) => segment switch
    {
        ArcSegment a => a.Point,
        BezierSegment z => z.Point3,
        QuadraticBezierSegment q => q.Point2,
        PolyBezierSegment { Points.Count: > 0 } pz => pz.Points[^1],
        PolyQuadraticBezierSegment { Points.Count: > 0 } pq => pq.Points[^1],
        _ => p,
    };

    private static string Zahl(double z) => z.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Eine fertige Form mit ihren Zahlen (und für Pfeil und Winkel der Richtung).</summary>
    private sealed class Grundform(string name, double[] zahlen, bool links = false) : Teil
    {
        public override string ToString() =>
            name + (name is "Pfeil" or "Winkel" ? (links ? " links" : " rechts") : "") + string.Concat(zahlen.Select(z => " " + Zahl(z)));

        private double Z(int i, double vorgabe) => i < zahlen.Length ? zahlen[i] : vorgabe;

        /// <summary>Vier Werte je Ecke aus einem oder vier Zahlen, in ganzen Pixeln, höchstens die halbe kurze Seite.</summary>
        private double[] Ecken(double s, double grenze)
        {
            var e = zahlen.Length >= 4 ? zahlen[..4] : [Z(0, 0), Z(0, 0), Z(0, 0), Z(0, 0)];
            return e.Select(w => Math.Clamp(Glasgeometrie.Raster(w, s), 0, grenze)).ToArray();
        }

        public override double Eckradius(double b, double h, double s) => name switch
        {
            "Pille" or "Kreis" => Math.Min(b, h) / 2,
            "Rechteck" or "Reiter" => Ecken(s, Math.Min(b, h) / 2)[0],
            _ => 0,
        };

        public override Geometry Bauen(double b, double h, double s)
        {
            var kurz = Math.Min(b, h);
            switch (name)
            {
                case "Pille":
                    return new RectangleGeometry(new Rect(0, 0, b, h), kurz / 2, kurz / 2);
                case "Kreis":
                    return new EllipseGeometry(new Rect(0, 0, b, h));
                case "Rechteck":
                case "Reiter":
                {
                    var r = Radien(b, h, s);
                    return Abgerundet(b, h, r[0], r[1], r[2], r[3]);
                }
                case "Fase":
                case "Achteck":
                case "Sechseck":
                case "Pfeil":
                case "Winkel":
                    return Vieleck(Ecklage(b, h, s));
                default:
                    throw new FormatException("Unbekannte Form: " + name);
            }
        }

        /// <summary>Fase und Achteck: die vier schrägen Stücke, soweit sie eine Länge haben (<c>Fase 8 0 8 0</c> hat zwei).</summary>
        public override IEnumerable<(Point A, Point B)> Fasen(double b, double h, double s)
        {
            if (name is not ("Fase" or "Achteck")) yield break;
            // Die acht Knoten liegen paarweise an den Fasen: Knoten 1 bis 2 ist die Fase oben rechts, 3 bis 4 unten rechts usw.
            var p = Ecklage(b, h, s).Select(q => new Point(q.X, q.Y)).ToArray();
            for (var k = 0; k < 4; k++)
            {
                var (von, bis) = (p[2 * k + 1], p[(2 * k + 2) % 8]);
                if ((bis - von).Length > 0) yield return (von, bis);
            }
        }

        /// <summary>Die vier Eckradien von Rechteck und Reiter in Pixeln: oben links, oben rechts, unten rechts, unten links.</summary>
        private double[] Radien(double b, double h, double s)
        {
            var kurz = Math.Min(b, h);
            if (name == "Rechteck") return Ecken(s, kurz / 2);
            var r = Math.Clamp(Glasgeometrie.Raster(Z(0, 8), s), 0, kurz / 2);
            return [r, r, 0, 0];
        }

        /// <summary>Die Knoten der Vielecke (Fase, Achteck, Sechseck, Pfeil, Winkel) in Umlaufrichtung, ab oben links.</summary>
        private (double X, double Y)[] Ecklage(double b, double h, double s)
        {
            var kurz = Math.Min(b, h);
            switch (name)
            {
                case "Fase":
                case "Achteck":
                {
                    var f = name == "Fase" ? Ecken(s, kurz / 2) : [.. Enumerable.Repeat(Math.Round(kurz * (1 - 1 / Math.Sqrt(2))), 4)];
                    return [(f[0], 0), (b - f[1], 0), (b, f[1]), (b, h - f[2]), (b - f[2], h), (f[3], h), (0, h - f[3]), (0, f[0])];
                }
                case "Sechseck":
                {
                    var k = Math.Min(b / 2, Math.Round(h * Math.Tan(Math.PI / 6) / 2));
                    return [(k, 0), (b - k, 0), (b, h / 2), (b - k, h), (k, h), (0, h / 2)];
                }
                default:
                {
                    var p = Math.Min(b / 2, Math.Round(Math.Clamp(Z(0, 0.5), 0, 2) * h));
                    (double X, double Y)[] punkte = name == "Pfeil"
                        ? [(0, 0), (b - p, 0), (b, h / 2), (b - p, h), (0, h)]
                        : [(0, 0), (b - p, 0), (b, h / 2), (b - p, h), (0, h), (p, h / 2)];
                    if (links) punkte = punkte.Select(q => (b - q.X, q.Y)).Reverse().ToArray();
                    return punkte;
                }
            }
        }
    }

    /// <summary>Freie Form: Knoten mit Lage (Anteil) und Rundung (DIP); Ecken mit Rundung werden zu einem Bogen.</summary>
    private sealed class Freiform((double X, double Y, double R)[] knoten) : Teil
    {
        public override string ToString() =>
            "Pfad" + string.Concat(knoten.Select(k => " " + Zahl(k.X) + "," + Zahl(k.Y) + (k.R > 0 ? "," + Zahl(k.R) : "")));

        public override double Eckradius(double b, double h, double s) => 0;

        public override Geometry Bauen(double b, double h, double s)
        {
            // Knoten auf ganze Pixel, damit senkrechte und waagerechte Kanten scharf bleiben.
            var p = knoten.Select(k => new Point(Math.Round(k.X * b), Math.Round(k.Y * h))).ToArray();
            var n = p.Length;
            var figur = new PathFigure { IsClosed = true, IsFilled = true };
            for (var i = 0; i < n; i++)
            {
                var vor = p[(i + n - 1) % n];
                var hier = p[i];
                var nach = p[(i + 1) % n];
                var r = Glasgeometrie.Raster(knoten[i].R, s);
                Point ein = hier, aus = hier;
                if (r > 0)
                {
                    // Auf beiden Kanten um r zurück, höchstens bis zur halben Kante; dazwischen ein Bogen mit der Ecke als Stütze.
                    double Laenge(Point a, Point c) => Math.Sqrt((a.X - c.X) * (a.X - c.X) + (a.Y - c.Y) * (a.Y - c.Y));
                    var d = Math.Min(r, Math.Min(Laenge(vor, hier), Laenge(hier, nach)) / 2);
                    Point Zurueck(Point von, Point zu) { var l = Laenge(von, zu); return l == 0 ? zu : new Point(zu.X + (von.X - zu.X) * d / l, zu.Y + (von.Y - zu.Y) * d / l); }
                    ein = Zurueck(vor, hier);
                    aus = Zurueck(nach, hier);
                }
                if (i == 0) figur.StartPoint = ein;
                else figur.Segments.Add(new LineSegment(ein, true));
                if (r > 0) figur.Segments.Add(new QuadraticBezierSegment(hier, aus, true));
            }
            figur.Segments.Add(new LineSegment(figur.StartPoint, true));
            return new PathGeometry([figur]);
        }
    }

    /// <summary>Zwei Formen verbunden; die zweite liegt im Rechteck (Anteile) der ersten.</summary>
    private sealed class Verbund(GeometryCombineMode modus, Teil a, Teil b, Rect lage) : Teil
    {
        private static readonly Dictionary<GeometryCombineMode, string> Namen = new()
        {
            [GeometryCombineMode.Union] = "Vereinigen", [GeometryCombineMode.Exclude] = "Ausschneiden", [GeometryCombineMode.Intersect] = "Ueberschneiden",
        };

        public static GeometryCombineMode? Modus(string name) => Namen.FirstOrDefault(n => n.Value == name) is { Value: not null } m ? m.Key : null;

        public override string ToString() =>
            $"{Namen[modus]}({a}; {b}; {Zahl(lage.X)} {Zahl(lage.Y)} {Zahl(lage.Width)} {Zahl(lage.Height)})";

        public override double Eckradius(double breite, double hoehe, double s) => a.Eckradius(breite, hoehe, s);

        public override Geometry Bauen(double breite, double hoehe, double s)
        {
            var (x, y, bb, bh) = Platz(breite, hoehe);
            var zweite = b.Bauen(bb, bh, s).Clone();
            zweite.Transform = new TranslateTransform(x, y);
            return Geometry.Combine(a.Bauen(breite, hoehe, s), zweite, modus, null, 0.01, ToleranceType.Absolute);
        }

        /// <summary>Die Fasen beider Formen, die der zweiten an ihren Platz verschoben; was davon auf der Kontur liegt, prüft der Fresnel-Rand.</summary>
        public override IEnumerable<(Point A, Point B)> Fasen(double breite, double hoehe, double s)
        {
            var (x, y, bb, bh) = Platz(breite, hoehe);
            var d = new Vector(x, y);
            return a.Fasen(breite, hoehe, s).Concat(b.Fasen(bb, bh, s).Select(f => (f.A + d, f.B + d)));
        }

        /// <summary>
        /// Die geraden Kanten beider Formen, die der zweiten an ihren Platz verschoben; nicht die eigene Kontur, denn darin
        /// stehen auch die geraden Stücke, die Geometry.Combine in Bögen setzt.
        /// </summary>
        public override IEnumerable<(Point A, Point B)> Geraden(double breite, double hoehe, double s)
        {
            var (x, y, bb, bh) = Platz(breite, hoehe);
            var d = new Vector(x, y);
            return a.Geraden(breite, hoehe, s).Concat(b.Geraden(bb, bh, s).Select(g => (g.A + d, g.B + d)));
        }

        /// <summary>Lage und Größe der zweiten Form in ganzen Pixeln.</summary>
        private (double X, double Y, double Breite, double Hoehe) Platz(double breite, double hoehe) =>
            (Math.Round(lage.X * breite), Math.Round(lage.Y * hoehe), Math.Max(1, Math.Round(lage.Width * breite)), Math.Max(1, Math.Round(lage.Height * hoehe)));
    }

    private static PathGeometry Abgerundet(double b, double h, double r1, double r2, double r3, double r4)
    {
        var f = new PathFigure { StartPoint = new Point(r1, 0), IsClosed = true, IsFilled = true };
        void Bogen(double r, Point ende)
        {
            if (r > 0) f.Segments.Add(new ArcSegment(ende, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        }
        f.Segments.Add(new LineSegment(new Point(b - r2, 0), true));
        Bogen(r2, new Point(b, r2));
        f.Segments.Add(new LineSegment(new Point(b, h - r3), true));
        Bogen(r3, new Point(b - r3, h));
        f.Segments.Add(new LineSegment(new Point(r4, h), true));
        Bogen(r4, new Point(0, h - r4));
        f.Segments.Add(new LineSegment(new Point(0, r1), true));
        Bogen(r1, new Point(r1, 0));
        return new PathGeometry([f]);
    }

    private static PathGeometry Vieleck((double X, double Y)[] punkte)
    {
        var f = new PathFigure { StartPoint = new Point(punkte[0].X, punkte[0].Y), IsClosed = true, IsFilled = true };
        foreach (var (x, y) in punkte.Skip(1)) f.Segments.Add(new LineSegment(new Point(x, y), true));
        return new PathGeometry([f]);
    }

    // ------------------------------------------------------------------ Lesen

    /// <summary>Liest die Textform: Wörter, Zahlen mit Punkt, Klammern und Strichpunkte für Verbunde.</summary>
    private sealed class Leser(string text)
    {
        private int _i;

        private void Leer()
        {
            while (_i < text.Length && char.IsWhiteSpace(text[_i])) _i++;
        }

        public void Ende()
        {
            Leer();
            if (_i < text.Length) throw new FormatException($"Unerwartet ab Stelle {_i + 1}: \"{text[_i..]}\"");
        }

        private string Wort()
        {
            Leer();
            var start = _i;
            while (_i < text.Length && char.IsLetter(text[_i])) _i++;
            if (_i == start) throw new FormatException($"Form erwartet an Stelle {_i + 1} in \"{text}\"");
            return text[start.._i];
        }

        private bool Zeichen(char c)
        {
            Leer();
            if (_i < text.Length && text[_i] == c)
            {
                _i++;
                return true;
            }
            return false;
        }

        private double? Zahl()
        {
            Leer();
            var start = _i;
            while (_i < text.Length && (char.IsDigit(text[_i]) || text[_i] is '.' or '-' or '+')) _i++;
            if (_i == start) return null;
            if (!double.TryParse(text[start.._i], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) || double.IsNaN(z) || double.IsInfinity(z))
                throw new FormatException($"Keine Zahl: \"{text[start.._i]}\"");
            return z;
        }

        private List<double> Zahlen()
        {
            var z = new List<double>();
            while (Zahl() is { } w) z.Add(w);
            return z;
        }

        public Teil Teil()
        {
            var name = Wort();
            if (Verbund.Modus(name) is { } modus)
            {
                if (!Zeichen('(')) throw new FormatException(name + ": \"(\" erwartet");
                var a = Teil();
                if (!Zeichen(';')) throw new FormatException(name + ": \";\" nach der ersten Form erwartet");
                var b = Teil();
                if (!Zeichen(';')) throw new FormatException(name + ": \";\" vor der Lage erwartet");
                var l = Zahlen();
                if (l.Count != 4 || l[2] <= 0 || l[3] <= 0) throw new FormatException(name + ": Lage aus vier Zahlen x y Breite Höhe erwartet");
                if (!Zeichen(')')) throw new FormatException(name + ": \")\" erwartet");
                return new Verbund(modus, a, b, new Rect(l[0], l[1], l[2], l[3]));
            }
            if (name == "Pfad")
            {
                var knoten = new List<(double, double, double)>();
                while (true)
                {
                    if (Zahl() is not { } x) break;
                    if (!Zeichen(',') || Zahl() is not { } y) throw new FormatException("Pfad: Knoten als x,y oder x,y,r erwartet");
                    var r = Zeichen(',') ? Zahl() ?? throw new FormatException("Pfad: Rundung nach dem zweiten Komma erwartet") : 0;
                    if (r < 0) throw new FormatException("Pfad: Rundung nicht negativ");
                    knoten.Add((x, y, r));
                }
                if (knoten.Count < 3) throw new FormatException("Pfad: mindestens drei Knoten");
                return new Freiform([.. knoten]);
            }
            var links = false;
            if (name is "Pfeil" or "Winkel")
            {
                var richtung = Wort();
                links = richtung switch
                {
                    "links" => true,
                    "rechts" => false,
                    _ => throw new FormatException(name + ": links oder rechts erwartet"),
                };
            }
            var zahlen = Zahlen();
            var erlaubt = name switch
            {
                "Pille" or "Kreis" or "Sechseck" or "Achteck" => new[] { 0 },
                "Rechteck" or "Fase" => [1, 4],
                "Reiter" or "Pfeil" or "Winkel" => [0, 1],
                _ => throw new FormatException("Unbekannte Form: " + name),
            };
            if (!erlaubt.Contains(zahlen.Count)) throw new FormatException($"{name}: {string.Join(" oder ", erlaubt)} Zahlen erwartet");
            if (zahlen.Any(z => z < 0)) throw new FormatException(name + ": keine negativen Maße");
            return new Grundform(name, [.. zahlen], links);
        }
    }
}

/// <summary>Umriss aus Text in XAML (<c>gd:Glas.Umriss="Fase 6"</c>) und zurück.</summary>
public sealed class UmrissConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string s ? new Umriss(s) : base.ConvertFrom(context, culture, value);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is Umriss u ? u.Text : base.ConvertTo(context, culture, value, destinationType);
}
