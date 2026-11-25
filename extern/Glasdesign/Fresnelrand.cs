// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Die Geometrie des Fresnel-Rands einer Fläche (Glanzschicht aus Glint Abschnitt 5): wo die Haarlinie und die Reihe
/// darunter stärker leuchten, weil sich die Kontur dort biegt. Gerechnet wird an der Kontur selbst
/// (<see cref="Glasflaeche.KonturPx"/>), einmal je Fläche und gemerkt; Überfahren und Drücken bauen nur neue Pinsel.
/// <para>
/// Die Kontur wird einmal abgeflacht, Bögen und Bézierkurven je für sich, damit klar bleibt, was gebogen ist. Längs der
/// Kontur entsteht daraus eine Deckung von 0 bis 1: 1 auf jedem Bogen, gleich wie groß sein Radius ist, sobald er sich um
/// mindestens <see cref="Mindestdrehung"/> Grad dreht; 1 auf jeder Fase, die der Umriss nennt (<see cref="Umriss.Fasen"/>),
/// denn an der Kontur sieht sie aus wie jede gerade Kante; 1 um jede scharfe Ecke (ein Knick um mindestens
/// <see cref="Mindestdrehung"/> Grad), <see cref="Eckenlicht"/> DIP nach beiden Seiten. Die Enden einer Fase sind keine
/// eigenen Ecken, sie gehören zur Fase; nur wo eine Fase einspringt (am Rand eines Lochs), leuchtet ihr Ende wie eine
/// Ecke weiter, sonst endete ihr Licht genau im Knick und ließe innen einen Keil ohne Licht. Hinter jedem Licht fällt die
/// Deckung über <see cref="Auslauf"/> DIP auf 0; liegen zwei Lichter näher beieinander, gilt an jeder Stelle das stärkere,
/// nie die Summe. Auf geraden Stücken bleibt 0.
/// </para>
/// <para>
/// Gezeichnet wird das als Folge von Abschnitten des Rings, die sich nicht überlappen: Jeder Abschnitt ist der Teil des
/// Rings längs eines Stücks Kontur, mit geradem Ende quer zur Kante, ohne was frühere Abschnitte schon decken (an Ecken
/// und in schmalen Stellen träfen sie sich sonst). Die vollen Abschnitte zusammen sind <see cref="Licht"/>, jeder Auslauf
/// ist ein eigener Abschnitt mit Verlauf längs der Kante (<see cref="Auslaeufe"/>). Ring und Abschnitte haben dieselben
/// Ränder, Pixel für Pixel; Enden der Lichter auf waagerechten und senkrechten Kanten liegen auf ganzen Pixeln.
/// </para>
/// Ein Verbund braucht wenig Eigenes: Seine Kontur hat jeden Bogen, der außen bleibt, genau einmal, und einen Knick, wo
/// sich die Teile schneiden; wo eine Form die gerade Kante der anderen nur berührt, biegt sich nichts. Nur die kurzen
/// geraden Stücke, die Geometry.Combine an flachen Schnittpunkten zweier Bögen und am Scheitel einer Ellipse in die Kontur
/// setzt, gehören zum Bogen: Gerade ist an einer Kontur mit Umriss nur, was auf einer geraden Kante seiner Formen liegt
/// (<see cref="Umriss.Geraden"/>). Schneiden sich zwei Bögen flach (unter <see cref="Mindestdrehung"/> Grad), läuft ihr
/// Licht ohne Lücke über die Schnittstelle.
/// </summary>
internal sealed class Fresnelrand
{
    /// <summary>Breite des Rings, auf dem der Fresnel-Rand leuchtet (Haarlinie und die Reihe darunter), in Pixeln.</summary>
    public const int Ringbreite = 2;

    /// <summary>Kleinere Drehungen (Grad) zählen nicht: Ein Knick oder ein Bogen von wenigen Grad ist keine Biegung.</summary>
    public const double Mindestdrehung = 15;

    /// <summary>So weit leuchtet eine scharfe Ecke nach beiden Seiten voll, in DIP (auf ganze Pixel gerundet, mindestens eins).</summary>
    public const double Eckenlicht = 2.5;

    /// <summary>Über diese Länge läuft jedes Licht hinter seinem Ende auf 0 aus, in DIP (auf ganze Pixel gerundet, mindestens eins).</summary>
    public const double Auslauf = 3;

    /// <summary>Wie genau Bögen abgeflacht und Abschnitte geschnitten werden, in Pixeln.</summary>
    private const double Toleranz = 0.02;

    /// <summary>Kürzere Kanten (Pixel) fallen beim Abflachen weg: Verbunde hinterlassen an Schnittstellen solche Splitter.</summary>
    private const double Mindestkante = 0.05;

    private static readonly Dictionary<Glasflaeche, Fresnelrand> Vorrat = [];

    /// <summary>Ein Auslauf: sein Stück Ring, der Verlauf von <see cref="Von"/> nach <see cref="Bis"/> (DIP) und die Deckung an beiden Enden (0 bis 1).</summary>
    public sealed record Abschnitt(Geometry Form, Point Von, Point Bis, double DeckungVon, double DeckungBis);

    private Fresnelrand(Glasflaeche f)
    {
        var s = f.Skala;
        // Der Ring einmal abgeflacht, und jeder Abschnitt aus genau diesem Ring geschnitten: So haben ihre Ränder dieselben
        // Knoten wie der Ring darunter. Der Rasterer rückt Knoten auf ein Achtelpixel; eigene Knoten auf derselben Linie
        // deckten am Innenrand bis zu einem Zehntel eines Pixels mehr als der Ring (gemessen am 11.10.2026).
        var ring = f.Ring(0, Ringbreite).GetFlattenedPathGeometry(Toleranz / s, ToleranceType.Absolute);
        ring.FillRule = FillRule.EvenOdd;
        ring.Freeze();
        Ring = ring;
        var ecke = Math.Max(1, Glasgeometrie.Raster(Eckenlicht, s));
        var auslauf = Math.Max(1, Glasgeometrie.Raster(Auslauf, s));
        var kontur = f.KonturPx();
        var fasen = f.Umriss?.Fasen(f.Breite, f.Hoehe, s) ?? [];
        var geraden = f.Umriss?.Geraden(f.Breite, f.Hoehe, s);
        var kerne = new List<(Zug Zug, double Von, double Bis, bool Ganz)>();
        var laeufe = new List<(Zug Zug, double Von, double Bis, double C0, double C1)>();
        foreach (var zug in Zug.Aus(kontur, geraden))
        {
            var (lichter, ganz) = Vereinigen(zug.Lichter(kontur, fasen, ecke), zug.Laenge);
            if (ganz)
            {
                kerne.Add((zug, 0, zug.Laenge, true));
                continue;
            }
            for (var k = 0; k < lichter.Count; k++)
            {
                var (von, bis) = lichter[k];
                var naechstes = k + 1 < lichter.Count ? lichter[k + 1].Von : lichter[0].Von + zug.Laenge;
                kerne.Add((zug, von, bis, false));
                // Bis zum nächsten Licht: frei auslaufen und wieder ansteigen, oder, wenn es zu nah ist, nur bis zur Mitte
                // abfallen. So gilt überall das stärkere der beiden Lichter.
                var luecke = naechstes - bis;
                if (luecke >= 2 * auslauf)
                {
                    laeufe.Add((zug, bis, bis + auslauf, 1, 0));
                    laeufe.Add((zug, naechstes - auslauf, naechstes, 0, 1));
                }
                else if (luecke > 2 * Mindestkante)
                {
                    var tief = 1 - luecke / (2 * auslauf);
                    laeufe.Add((zug, bis, bis + luecke / 2, 1, tief));
                    laeufe.Add((zug, bis + luecke / 2, naechstes, tief, 1));
                }
            }
        }

        // Erst die vollen Abschnitte, dann die Ausläufe; jeder ohne das, was frühere schon decken. Wo sich zwei treffen
        // (innen an einer Ecke, in einer schmalen Stelle), behält so das hellere Licht die Fläche.
        var belegt = new List<Geometry>();
        var licht = new GeometryGroup();
        foreach (var (zug, von, bis, ganz) in kerne)
            if (Abschnittsform(f, zug, von, bis, ganz, ring, belegt) is { } form) licht.Children.Add(form);
        var auslaeufe = new List<Abschnitt>();
        foreach (var (zug, von, bis, c0, c1) in laeufe)
        {
            if (Abschnittsform(f, zug, von, bis, false, ring, belegt) is not { } form) continue;
            var (a, e) = (zug.Punkt(von), zug.Punkt(bis));
            auslaeufe.Add(new Abschnitt(form, f.Dip(a.X, a.Y), f.Dip(e.X, e.Y), c0, c1));
        }
        licht.Freeze();
        Licht = licht.Children.Count > 0 ? licht : null;
        Auslaeufe = auslaeufe;
    }

    /// <summary>
    /// Der Ring der Haarlinie und der Reihe darunter, auf dem der Fresnel-Rand überall ein wenig leuchtet; einmal abgeflacht,
    /// alle Abschnitte sind aus ihm geschnitten.
    /// </summary>
    public Geometry Ring { get; }

    /// <summary>Die vollen Abschnitte (Deckung 1) zusammen; null, wenn sich die Kontur nirgends biegt.</summary>
    public Geometry? Licht { get; }

    /// <summary>Die Ausläufe hinter und zwischen den Lichtern, je mit eigenem Verlauf.</summary>
    public IReadOnlyList<Abschnitt> Auslaeufe { get; }

    /// <summary>Der Fresnel-Rand der Fläche, je Fläche einmal gebaut und gemerkt; begrenzt wie die Konturen.</summary>
    public static Fresnelrand Holen(Glasflaeche f)
    {
        lock (Vorrat)
        {
            if (Vorrat.TryGetValue(f, out var fertig)) return fertig;
            var neu = new Fresnelrand(f);
            if (Vorrat.Count > 256) Vorrat.Clear();
            Vorrat[f] = neu;
            return neu;
        }
    }

    /// <summary>
    /// Ein Abschnitt des Rings: der Ring neben dem Stück Kontur (ein Band einen Viertelpixel breiter als der Ring, mit
    /// geraden Enden quer zur Kante und runden Knicken, damit innen der Rand des Rings gilt), ohne die schon belegten
    /// Abschnitte; eingefroren und dort vermerkt. Null, wenn nichts übrig bleibt.
    /// </summary>
    private static PathGeometry? Abschnittsform(Glasflaeche f, Zug zug, double von, double bis, bool ganz, Geometry ring, List<Geometry> belegt)
    {
        var punkte = ganz ? zug.Knoten : zug.Strecke(von, bis);
        if (punkte.Count < 2) return null;
        var figur = new PathFigure { StartPoint = f.Dip(punkte[0].X, punkte[0].Y), IsClosed = ganz, IsFilled = false };
        figur.Segments.Add(new PolyLineSegment(punkte.Skip(1).Select(q => f.Dip(q.X, q.Y)), true));
        var stift = new Pen(Brushes.Black, 2.0 * (Ringbreite + 0.25) / f.Skala)
        {
            StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Round,
        };
        var toleranz = Toleranz / f.Skala;
        var band = new PathGeometry([figur]).GetWidenedPathGeometry(stift, toleranz, ToleranceType.Absolute);
        var form = Gestrafft(Geometry.Combine(ring, band, GeometryCombineMode.Intersect, null, toleranz, ToleranceType.Absolute), toleranz);
        foreach (var frueher in belegt)
            if (frueher.Bounds.IntersectsWith(form.Bounds))
                form = Gestrafft(Geometry.Combine(form, frueher, GeometryCombineMode.Exclude, null, toleranz, ToleranceType.Absolute), toleranz);
        if (form.IsEmpty() || form.GetArea(toleranz, ToleranceType.Absolute) * f.Skala * f.Skala < 0.01) return null;
        form.Freeze();
        belegt.Add(form);
        return form;
    }

    /// <summary>
    /// Das Ergebnis eines Schnitts als Vielecke ohne Zwischenknoten: Geometry.Combine teilt Kanten an Stellen, die auf ihnen
    /// liegen. Solche Knoten mitten auf einer Kante des Rings rundet der Rasterer anders als die Kante des Rings selbst, und
    /// ein Abschnitt deckte dort ein Randpixel um einige Hundertstel mehr als der Ring darunter.
    /// </summary>
    private static PathGeometry Gestrafft(Geometry form, double toleranz)
    {
        var flach = form.GetFlattenedPathGeometry(toleranz, ToleranceType.Absolute);
        var neu = new PathGeometry { FillRule = flach.FillRule };
        foreach (var figur in flach.Figures)
        {
            var p = new List<Point> { figur.StartPoint };
            p.AddRange(Knotenfolge(figur));
            var k = new List<Point>();
            for (var i = 0; i < p.Count; i++)
            {
                if (k.Count > 0 && (p[i] - k[^1]).Length < 1e-7) continue;
                // Ein Knoten fällt weg, wenn er auf der Geraden vom letzten behaltenen zum nächsten liegt.
                if (k.Count > 0 && i + 1 < p.Count)
                {
                    var (a, b) = (p[i + 1] - k[^1], p[i] - k[^1]);
                    if (a.Length > 1e-7 && Math.Abs(Vector.CrossProduct(a, b)) / a.Length < 1e-5 && b * a > 0 && b.Length < a.Length) continue;
                }
                k.Add(p[i]);
            }
            if (k.Count > 1 && (k[^1] - k[0]).Length < 1e-7) k.RemoveAt(k.Count - 1);
            if (k.Count < 3) continue;
            var f = new PathFigure { StartPoint = k[0], IsClosed = true, IsFilled = true };
            f.Segments.Add(new PolyLineSegment(k.Skip(1), true));
            neu.Figures.Add(f);
        }
        return neu;
    }

    /// <summary>Die Punkte einer abgeflachten Figur nach ihrem Anfang, in ihrer Reihenfolge.</summary>
    private static IEnumerable<Point> Knotenfolge(PathFigure figur)
    {
        foreach (var segment in figur.Segments)
        {
            if (segment is PolyLineSegment pl)
                foreach (var q in pl.Points) yield return q;
            else if (segment is LineSegment l) yield return l.Point;
        }
    }

    /// <summary>
    /// Die Lichter eines Zugs zusammengelegt, wo sie sich berühren oder überlappen, nach ihrer Lage sortiert und über das
    /// Ende des Zugs hinweg verbunden. Ganz, wenn sie den Zug ringsum decken.
    /// </summary>
    private static (List<(double Von, double Bis)> Lichter, bool Ganz) Vereinigen(List<(double Von, double Bis)> lichter, double laenge)
    {
        var liste = new List<(double Von, double Bis)>();
        foreach (var (von, bis) in lichter)
        {
            if (bis - von >= laenge - 1e-6) return ([], true);
            var v = von - Math.Floor(von / laenge) * laenge;
            liste.Add((v, v + bis - von));
        }
        liste.Sort();
        var zusammen = new List<(double Von, double Bis)>();
        foreach (var l in liste)
        {
            if (zusammen.Count > 0 && l.Von <= zusammen[^1].Bis + 1e-6) zusammen[^1] = (zusammen[^1].Von, Math.Max(zusammen[^1].Bis, l.Bis));
            else zusammen.Add(l);
        }
        // Reicht das letzte über das Ende hinaus bis an das erste, gehören beide zusammen.
        while (zusammen.Count > 1 && zusammen[^1].Bis >= zusammen[0].Von + laenge - 1e-6)
        {
            zusammen[^1] = (zusammen[^1].Von, Math.Max(zusammen[^1].Bis, zusammen[0].Bis + laenge));
            zusammen.RemoveAt(0);
        }
        if (zusammen.Count == 1 && zusammen[0].Bis - zusammen[0].Von >= laenge - 1e-6) return ([], true);
        return (zusammen, false);
    }

    /// <summary>
    /// Ein geschlossener Zug der abgeflachten Kontur in Pixeln der Fläche: seine Knoten, ob die Kante ab einem Knoten aus
    /// einem Bogen stammt, Länge und Lage jeder Kante längs des Zugs und wie weit sich die Kontur an jedem Knoten dreht.
    /// </summary>
    private sealed class Zug
    {
        private readonly Point[] _p;
        private readonly bool[] _bogen;
        private readonly double[] _l;
        private readonly double[] _u;
        private readonly double[] _drehung;

        private Zug(List<Point> p, List<bool> bogen)
        {
            _p = [.. p];
            _bogen = [.. bogen];
            var n = _p.Length;
            _l = new double[n];
            _u = new double[n];
            _drehung = new double[n];
            for (var i = 0; i < n; i++)
            {
                _l[i] = (P(i + 1) - _p[i]).Length;
                if (i > 0) _u[i] = _u[i - 1] + _l[i - 1];
                _drehung[i] = Drehung(P(i - 1), _p[i], P(i + 1));
            }
            Laenge = _u[n - 1] + _l[n - 1];
        }

        /// <summary>Länge des Zugs in Pixeln.</summary>
        public double Laenge { get; }

        /// <summary>Alle Knoten in Umlaufrichtung (der Zug schließt sich vom letzten zum ersten).</summary>
        public IReadOnlyList<Point> Knoten => _p;

        private int Anzahl => _p.Length;

        private Point P(int i) => _p[((i % Anzahl) + Anzahl) % Anzahl];

        /// <summary>
        /// Die Züge einer Kontur: jeder Bogen und jede Bézierkurve wird für sich abgeflacht und als gebogen vermerkt.
        /// Strecken bleiben Strecken, wenn sie auf einer der <paramref name="geraden"/> Kanten der Formen liegen
        /// (<see cref="Umriss.Geraden"/>; ohne Umriss null, dann jede): Geometry.Combine setzt in die Kontur eines Verbunds
        /// an flachen Schnittpunkten zweier Bögen und am Scheitel einer Ellipse kurze gerade Stücke, die auf keiner liegen.
        /// Sie gehören zum Bogen, sonst risse sein Licht dort ab. Splitter unter <see cref="Mindestkante"/> fallen weg.
        /// </summary>
        public static IEnumerable<Zug> Aus(Geometry kontur, IReadOnlyList<(Point A, Point B)>? geraden)
        {
            bool Gebogen(Point a, Point b) => geraden is not null && !geraden.Any(g => Auf(a, b, g));
            foreach (var figur in PathGeometry.CreateFromGeometry(kontur).Figures)
            {
                var roh = new List<(Point P, bool Bogen)> { (figur.StartPoint, false) };
                foreach (var segment in figur.Segments)
                {
                    switch (segment)
                    {
                        case LineSegment l:
                            roh.Add((l.Point, Gebogen(roh[^1].P, l.Point)));
                            break;
                        case PolyLineSegment pl:
                            foreach (var q in pl.Points) roh.Add((q, Gebogen(roh[^1].P, q)));
                            break;
                        default:
                            var stueck = new PathGeometry([new PathFigure(roh[^1].P, [segment.Clone()], false)]);
                            foreach (var flach in stueck.GetFlattenedPathGeometry(Toleranz, ToleranceType.Absolute).Figures)
                                foreach (var q in Knotenfolge(flach)) roh.Add((q, true));
                            break;
                    }
                }
                roh.Add((figur.StartPoint, Gebogen(roh[^1].P, figur.StartPoint)));
                if (Bereinigt(roh) is { } zug) yield return zug;
            }
        }

        /// <summary>
        /// Ob die Strecke von <paramref name="a"/> nach <paramref name="b"/> auf der Kante <paramref name="g"/> liegt: beide
        /// Enden höchstens <see cref="Mindestkante"/> neben ihr und nicht vor ihrem Anfang oder hinter ihrem Ende.
        /// </summary>
        private static bool Auf(Point a, Point b, (Point A, Point B) g)
        {
            var r = g.B - g.A;
            var l = r.Length;
            if (l < Mindestkante) return false;
            r /= l;
            bool Nah(Point q)
            {
                var v = q - g.A;
                var t = v * r;
                return Math.Abs(Vector.CrossProduct(r, v)) <= Mindestkante && t >= -Mindestkante && t <= l + Mindestkante;
            }
            return Nah(a) && Nah(b);
        }

        /// <summary>
        /// Kanten unter <see cref="Mindestkante"/> verschmelzen mit der nächsten. Ob die verschmolzene gebogen ist, sagt das
        /// Stück, mit dem sie den Splitter verlässt: Ein Splitter ist keine Biegung, und eine gerade Kante wird nicht krumm,
        /// weil an ihrem Anfang der Rest eines Bogens hängt.
        /// </summary>
        private static Zug? Bereinigt(List<(Point P, bool Bogen)> roh)
        {
            var p = new List<Point> { roh[0].P };
            var bogen = new List<bool>();
            for (var i = 1; i < roh.Count; i++)
            {
                if ((roh[i].P - p[^1]).Length < Mindestkante) continue;
                p.Add(roh[i].P);
                bogen.Add(roh[i].Bogen);
            }
            // Der letzte Knoten ist der Anfang selbst oder liegt dicht daran: Die Kante dorthin schließt den Zug.
            if (p.Count > 1 && (p[^1] - p[0]).Length < Mindestkante) p.RemoveAt(p.Count - 1);
            else bogen.Add(roh[^1].Bogen);
            return p.Count >= 3 ? new Zug(p, bogen) : null;
        }

        /// <summary>Wie weit sich die Kontur am Knoten <paramref name="hier"/> dreht, in Grad mit Vorzeichen (0 gerade).</summary>
        private static double Drehung(Point vor, Point hier, Point nach)
        {
            var (a, b) = (hier - vor, nach - hier);
            return Math.Atan2(Vector.CrossProduct(a, b), a * b) * 180 / Math.PI;
        }

        /// <summary>
        /// Wo der Zug voll leuchtet, als Lagen von bis (Pixel längs des Zugs; sie dürfen vor dem Anfang beginnen und über
        /// das Ende reichen): Läufe gebogener Kanten ohne Knick dazwischen, die sich zusammen um mindestens
        /// <see cref="Mindestdrehung"/> drehen; die Stücke gerader Kanten, die auf einer genannten Fase liegen; je
        /// <paramref name="ecke"/> Pixel um jeden Knick ab <see cref="Mindestdrehung"/>, außer an den Enden einer Fase, wo sich
        /// die Kontur nach außen dreht (dort gehört der Knick zur Fase). Die Enden auf waagerechten und senkrechten Kanten
        /// rücken nach außen auf ganze Pixel.
        /// </summary>
        public List<(double Von, double Bis)> Lichter(Geometry kontur, IReadOnlyList<(Point A, Point B)> fasen, double ecke)
        {
            var n = Anzahl;
            var lichter = new List<(double Von, double Bis)>();
            if (Array.TrueForAll(_bogen, b => b) && Array.TrueForAll(_drehung, d => Math.Abs(d) < Mindestdrehung))
            {
                // Ringsum gebogen ohne Knick: Kreis, Ellipse, rundes Loch.
                if (_drehung.Sum(Math.Abs) >= Mindestdrehung) lichter.Add((0, Laenge));
                return lichter;
            }
            for (var a = 0; a < n; a++)
            {
                // Ein Lauf beginnt an einer gebogenen Kante, vor der keine gebogene ohne Knick liegt.
                if (!_bogen[a] || (_bogen[(a + n - 1) % n] && Math.Abs(_drehung[a]) < Mindestdrehung)) continue;
                var (drehung, laenge) = (0.0, _l[a]);
                for (var k = (a + 1) % n; k != a && _bogen[k] && Math.Abs(_drehung[k]) < Mindestdrehung; k = (k + 1) % n)
                {
                    drehung += Math.Abs(_drehung[k]);
                    laenge += _l[k];
                }
                if (drehung >= Mindestdrehung) lichter.Add((_u[a], _u[a] + laenge));
            }
            var fasenlichter = new List<(double Von, double Bis)>();
            for (var i = 0; i < n; i++)
            {
                if (_bogen[i] || _l[i] < 0.5) continue;
                var d = (P(i + 1) - _p[i]) / _l[i];
                foreach (var (fa, fb) in fasen)
                {
                    var r = fb - fa;
                    if (r.Length < 0.5) continue;
                    r.Normalize();
                    // Parallel und auf derselben Geraden (auf einen halben Pixel), dann zählt das gemeinsame Stück.
                    if (Math.Abs(Vector.CrossProduct(d, r)) > 0.02 || Math.Abs(Vector.CrossProduct(fa - _p[i], d)) > 0.5) continue;
                    var (ta, tb) = ((fa - _p[i]) * d, (fb - _p[i]) * d);
                    var (t0, t1) = (Math.Max(0, Math.Min(ta, tb)), Math.Min(_l[i], Math.Max(ta, tb)));
                    if (t1 - t0 > 0.5) fasenlichter.Add((_u[i] + t0, _u[i] + t1));
                }
            }
            lichter.AddRange(fasenlichter);
            for (var i = 0; i < n; i++)
            {
                if (Math.Abs(_drehung[i]) < Mindestdrehung) continue;
                var u = _u[i];
                if (fasenlichter.Exists(l => Liegt(u, l)) && Aussen(kontur, i)) continue;
                lichter.Add((u - ecke, u + ecke));
            }
            return lichter.ConvertAll(l => (Gerastert(l.Von, false), Gerastert(l.Bis, true)));
        }

        /// <summary>Ob die Lage <paramref name="u"/> (auch eine Runde früher oder später) in dem Licht oder höchstens einen halben Pixel daneben liegt.</summary>
        private bool Liegt(double u, (double Von, double Bis) licht) =>
            new[] { u - Laenge, u, u + Laenge }.Any(w => w >= licht.Von - 0.5 && w <= licht.Bis + 0.5);

        /// <summary>
        /// Ob sich die Kontur am Knoten <paramref name="i"/> nach außen dreht (eine vorspringende Ecke): Dann liegt der Winkel
        /// zwischen den beiden Kanten in der Form. An einer einspringenden (etwa am Rand eines Lochs) liegt er draußen.
        /// </summary>
        private bool Aussen(Geometry kontur, int i)
        {
            var (a, b) = (P(i - 1) - _p[i], P(i + 1) - _p[i]);
            a.Normalize();
            b.Normalize();
            var m = a + b;
            if (m.Length < 1e-6) return true;
            m.Normalize();
            return kontur.FillContains(_p[i] + m * 0.35, 0.001, ToleranceType.Absolute);
        }

        /// <summary>
        /// Die Lage, auf einer waagerechten oder senkrechten geraden Kante bis zur nächsten ganzen Pixelkante verschoben
        /// (<paramref name="vor"/>: in Umlaufrichtung, sonst zurück), solange das auf der Kante bleibt. So liegt der Schnitt
        /// zwischen Licht und Auslauf auf einer Pixelgrenze, und kein Pixel wird halb von beiden gedeckt.
        /// </summary>
        private double Gerastert(double u, bool vor)
        {
            var i = Kante(u, out var t);
            if (_bogen[i]) return u;
            var d = P(i + 1) - _p[i];
            var (waagerecht, senkrecht) = (Math.Abs(d.Y) < 1e-9, Math.Abs(d.X) < 1e-9);
            if (!waagerecht && !senkrecht) return u;
            var q = Punkt(u);
            var x = waagerecht ? q.X : q.Y;
            var richtung = Math.Sign(waagerecht ? d.X : d.Y) * (vor ? 1 : -1);
            var ziel = richtung > 0 ? Math.Ceiling(x - 1e-6) : Math.Floor(x + 1e-6);
            var weg = Math.Abs(ziel - x);
            return weg <= (vor ? _l[i] - t : t) + 1e-9 ? u + (vor ? weg : -weg) : u;
        }

        /// <summary>Der Punkt bei der Lage <paramref name="u"/> längs des Zugs (beliebig oft herum).</summary>
        public Point Punkt(double u)
        {
            var i = Kante(u, out var t);
            return _l[i] > 0 ? _p[i] + (P(i + 1) - _p[i]) * (t / _l[i]) : _p[i];
        }

        /// <summary>Die Kante, auf der die Lage liegt, und wie weit hinter ihrem Anfang.</summary>
        private int Kante(double u, out double t)
        {
            u -= Math.Floor(u / Laenge) * Laenge;
            var i = Array.BinarySearch(_u, u);
            if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, Anzahl - 1);
            t = Math.Clamp(u - _u[i], 0, _l[i]);
            return i;
        }

        /// <summary>
        /// Die Punkte von Lage <paramref name="von"/> bis <paramref name="bis"/> (bis darf über das Ende des Zugs hinaus
        /// reichen): der Anfang, die Knoten dazwischen und das Ende.
        /// </summary>
        public List<Point> Strecke(double von, double bis)
        {
            var punkte = new List<Point> { Punkt(von) };
            var i = Kante(von, out var t);
            var lage = von - t;
            for (var k = 1; k <= Anzahl; k++)
            {
                lage += _l[(i + k - 1) % Anzahl];
                if (lage >= bis - 1e-9) break;
                punkte.Add(P(i + k));
            }
            punkte.Add(Punkt(bis));
            return punkte;
        }
    }
}
