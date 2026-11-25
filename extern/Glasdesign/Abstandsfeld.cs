// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Vorzeichenbehafteter Abstand zur Kontur einer <see cref="Glasflaeche"/> in Gerätepixeln (innen negativ) samt Richtung
/// nach außen, für jede Pixelmitte (KONZEPT.md 5b: das Abstandsfeld für beliebige Umrisse, Grundlage des
/// <see cref="Chromrand"/>). Die Kontur kommt aus <see cref="Glasflaeche.KonturPx"/> (Rundung, Ecke oder Umriss), fein
/// abgeflacht.
/// <list type="number">
/// <item>Nahe der Kontur (bis 1,5 Pixel) genau: Abstand zum nächsten Stück, die Richtung zwischen den Ecken eines Stücks
/// geglättet wie Normalen eines Netzes, außer an echten Ecken (über 30 Grad), die scharf bleiben.</item>
/// <item>Weiter innen und außen über zwei Durchgänge, die den nächsten Konturpunkt der Nachbarn weitergeben (dead
/// reckoning): Der Abstand ist der echte zu diesem Punkt, die Richtung die seine. Fehler höchstens Bruchteile eines
/// Pixels, dafür linear in der Pixelzahl statt je Pixel alle nahen Stücke.</item>
/// <item>Innen oder außen nach der Füllregel der Kontur, je Pixelzeile über die Schnittpunkte.</item>
/// </list>
/// Je Fläche einmal gerechnet und geteilt (je Faden die letzten 24); eine andere Randbreite braucht kein neues Feld.
/// Ohne Umriss (Rundung oder Ecke: ein abgerundetes Rechteck) gilt statt dessen die geschlossene Formel, Pixel für Pixel
/// genau und rund zwanzigmal schneller; ein gezogener Größenregler rechnet so je Schritt ein neues Feld in Bruchteilen
/// einer Millisekunde statt in rund 14 (Prüfung zu 0.5.0-beta.6, Pille 180 x 44 DIP bei 175 Prozent).
/// </summary>
internal sealed class Abstandsfeld
{
    /// <summary>Abstand außerhalb des Felds (weit draußen).</summary>
    public const float Weit = 1e6f;

    [ThreadStatic]
    private static List<Abstandsfeld>? _geteilt;

    private const int GeteiltHoechstens = 24;

    private readonly Glasflaeche _flaeche;
    private readonly int _b, _h;
    private readonly float[] _abstand;
    private readonly float[] _nx, _ny;

    /// <summary>Wie viele Felder bisher gerechnet wurden (für die Tests).</summary>
    internal static int Gerechnet { get; private set; }

    public static Abstandsfeld Holen(Glasflaeche f)
    {
        var geteilt = _geteilt ??= [];
        for (var i = geteilt.Count - 1; i >= 0; i--)
            if (geteilt[i]._flaeche == f) return geteilt[i];
        var neu = new Abstandsfeld(f, allgemein: false);
        Gerechnet++;
        if (geteilt.Count >= GeteiltHoechstens) geteilt.RemoveAt(0);
        geteilt.Add(neu);
        return neu;
    }

    public int Breite => _b;
    public int Hoehe => _h;

    /// <summary>
    /// Das abgerundete Rechteck (0, 0, Breite, Hoehe) mit Radius <paramref name="r"/> in geschlossener Form, wie
    /// <see cref="Glasflaeche.KonturPx"/> es ohne Umriss beschreibt: Abstand zur Kontur, Richtung nach außen zum nächsten
    /// Konturpunkt (an den Seiten senkrecht, in den Rundungen vom Mittelpunkt des Bogens weg).
    /// </summary>
    private void Rundrechteck(double r)
    {
        double hx = _flaeche.Breite / 2, hy = _flaeche.Hoehe / 2;
        double ix = hx - r, iy = hy - r;
        for (var y = 0; y < _h; y++)
        {
            var py = y + 0.5 - hy;
            var qy = Math.Abs(py) - iy;
            var sy = py < 0 ? -1 : 1;
            for (var x = 0; x < _b; x++)
            {
                var px = x + 0.5 - hx;
                var qx = Math.Abs(px) - ix;
                var sx = px < 0 ? -1 : 1;
                double d, nx, ny;
                if (qx > 0 && qy > 0)
                {
                    var l = Math.Sqrt(qx * qx + qy * qy);
                    d = l - r;
                    (nx, ny) = (sx * qx / l, sy * qy / l);
                }
                else if (qx > qy)
                {
                    d = qx - r;
                    (nx, ny) = (sx, 0);
                }
                else
                {
                    d = qy - r;
                    (nx, ny) = (0, sy);
                }
                var o = y * _b + x;
                _abstand[o] = (float)d;
                _nx[o] = (float)nx;
                _ny[o] = (float)ny;
            }
        }
    }

    /// <summary>Abstand der Pixelmitte (x, y) zur Kontur in Pixeln, innen negativ; außerhalb des Felds <see cref="Weit"/>.</summary>
    public float Abstand(int x, int y) => (uint)x < (uint)_b && (uint)y < (uint)_h ? _abstand[y * _b + x] : Weit;

    /// <summary>Richtung nach außen (Einheitsvektor, y nach unten) an der Pixelmitte (x, y).</summary>
    public (double X, double Y) Normale(int x, int y) =>
        (uint)x < (uint)_b && (uint)y < (uint)_h ? (_nx[y * _b + x], _ny[y * _b + x]) : (0, 0);

    /// <summary>Das Feld immer auf dem allgemeinen Weg über die Kontur gerechnet, ohne Teilen (für die Tests).</summary>
    internal static Abstandsfeld Allgemein(Glasflaeche f) => new(f, allgemein: true);

    private Abstandsfeld(Glasflaeche f, bool allgemein)
    {
        _flaeche = f;
        _b = (int)f.Breite;
        _h = (int)f.Hoehe;
        var n = _b * _h;
        _abstand = new float[n];
        _nx = new float[n];
        _ny = new float[n];
        if (f.Umriss is null && !allgemein)
        {
            Rundrechteck(Math.Min(f.Radius, Math.Min(f.Breite, f.Hoehe) / 2));
            return;
        }

        var (stuecke, gerade) = Stuecke(f.KonturPx());
        var innen = Innenmaske(stuecke, gerade);
        var (nStueck, nAnfang, nEnde) = Normalen(stuecke, gerade);

        // Nächster Konturpunkt je Pixel (qx, qy) und seine Richtung; unbekannt: NaN.
        var qx = new double[n];
        var qy = new double[n];
        var d2 = new double[n];
        Array.Fill(qx, double.NaN);
        Array.Fill(d2, double.MaxValue);

        // 1. Genau nahe der Kontur: je Stück die Pixel in seinem um 1,5 erweiterten Rechteck.
        const double Nah = 1.5;
        for (var i = 0; i < stuecke.Count; i++)
        {
            var (a, bp, _) = stuecke[i];
            double vx = bp.X - a.X, vy = bp.Y - a.Y;
            var l2 = vx * vx + vy * vy;
            var x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, bp.X) - Nah - 0.5));
            var x1 = Math.Min(_b - 1, (int)Math.Ceiling(Math.Max(a.X, bp.X) + Nah - 0.5));
            var y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, bp.Y) - Nah - 0.5));
            var y1 = Math.Min(_h - 1, (int)Math.Ceiling(Math.Max(a.Y, bp.Y) + Nah - 0.5));
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    double xc = x + 0.5, yc = y + 0.5;
                    var t = Math.Clamp(((xc - a.X) * vx + (yc - a.Y) * vy) / l2, 0, 1);
                    double px = a.X + t * vx, py = a.Y + t * vy;
                    var dd = (px - xc) * (px - xc) + (py - yc) * (py - yc);
                    var o = y * _b + x;
                    if (dd >= d2[o] || dd > Nah * Nah) continue;
                    d2[o] = dd;
                    qx[o] = px;
                    qy[o] = py;
                    var nv = nAnfang[i] * (1 - t) + nEnde[i] * t;
                    if (nv.Length < 1e-9) nv = nStueck[i];
                    nv.Normalize();
                    _nx[o] = (float)nv.X;
                    _ny[o] = (float)nv.Y;
                }
        }

        // 2. Weitergeben: den nächsten Konturpunkt der Nachbarn übernehmen, wenn er näher liegt.
        void Pruefen(int o, int nb, double xc, double yc)
        {
            if (double.IsNaN(qx[nb])) return;
            var dx = qx[nb] - xc;
            var dy = qy[nb] - yc;
            var dd = dx * dx + dy * dy;
            if (dd >= d2[o]) return;
            d2[o] = dd;
            qx[o] = qx[nb];
            qy[o] = qy[nb];
            _nx[o] = _nx[nb];
            _ny[o] = _ny[nb];
        }
        // Je Zeile hin und zurück (wie 8SSEDT): abwärts erst links, oben links, oben, oben rechts, dann rückwärts rechts;
        // aufwärts gespiegelt. Zweimal, damit auch Punkte nahe der Mittelachse runder Formen ihren nächsten Konturpunkt finden.
        for (var runde = 0; runde < 2; runde++)
        {
            for (var y = 0; y < _h; y++)
            {
                double yc = y + 0.5;
                for (var x = 0; x < _b; x++)
                {
                    var o = y * _b + x;
                    double xc = x + 0.5;
                    if (x > 0) Pruefen(o, o - 1, xc, yc);
                    if (y > 0)
                    {
                        if (x > 0) Pruefen(o, o - _b - 1, xc, yc);
                        Pruefen(o, o - _b, xc, yc);
                        if (x + 1 < _b) Pruefen(o, o - _b + 1, xc, yc);
                    }
                }
                for (var x = _b - 2; x >= 0; x--) Pruefen(y * _b + x, y * _b + x + 1, x + 0.5, yc);
            }
            for (var y = _h - 1; y >= 0; y--)
            {
                double yc = y + 0.5;
                for (var x = _b - 1; x >= 0; x--)
                {
                    var o = y * _b + x;
                    double xc = x + 0.5;
                    if (x + 1 < _b) Pruefen(o, o + 1, xc, yc);
                    if (y + 1 < _h)
                    {
                        if (x + 1 < _b) Pruefen(o, o + _b + 1, xc, yc);
                        Pruefen(o, o + _b, xc, yc);
                        if (x > 0) Pruefen(o, o + _b - 1, xc, yc);
                    }
                }
                for (var x = 1; x < _b; x++) Pruefen(y * _b + x, y * _b + x - 1, x + 0.5, yc);
            }
        }

        for (var o = 0; o < n; o++)
        {
            var d = d2[o] == double.MaxValue ? Weit : (float)Math.Sqrt(d2[o]);
            _abstand[o] = innen[o] ? -d : d;
        }
    }

    /// <summary>Die Kontur fein abgeflacht als Stücke (Anfang, Ende, Figur) in Pixeln, dazu, ob sie gerade-ungerade füllt.</summary>
    private static (List<(Point A, Point B, int Figur)> Stuecke, bool Gerade) Stuecke(Geometry kontur)
    {
        var flach = kontur.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute);
        var matrix = flach.Transform?.Value ?? Matrix.Identity;
        var stuecke = new List<(Point A, Point B, int Figur)>();
        var figur = 0;
        foreach (var fig in flach.Figures)
        {
            var punkte = new List<Point> { matrix.Transform(fig.StartPoint) };
            foreach (var seg in fig.Segments)
            {
                switch (seg)
                {
                    case LineSegment l:
                        punkte.Add(matrix.Transform(l.Point));
                        break;
                    case PolyLineSegment pl:
                        foreach (var p in pl.Points) punkte.Add(matrix.Transform(p));
                        break;
                }
            }
            if (punkte.Count > 1 && (punkte[0] - punkte[^1]).Length > 1e-9) punkte.Add(punkte[0]);
            for (var i = 0; i + 1 < punkte.Count; i++)
                if ((punkte[i + 1] - punkte[i]).Length > 1e-9) stuecke.Add((punkte[i], punkte[i + 1], figur));
            figur++;
        }
        return (stuecke, flach.FillRule == FillRule.EvenOdd);
    }

    /// <summary>Innen je Pixelmitte über die Schnittpunkte jeder Zeile mit der Kontur.</summary>
    private bool[] Innenmaske(List<(Point A, Point B, int Figur)> stuecke, bool gerade)
    {
        var zeilen = new List<(double X, int Richtung)>[_h];
        for (var y = 0; y < _h; y++) zeilen[y] = [];
        foreach (var (a, bp, _) in stuecke)
        {
            if (a.Y == bp.Y) continue;
            var (y0, y1) = a.Y < bp.Y ? (a.Y, bp.Y) : (bp.Y, a.Y);
            var richtung = bp.Y > a.Y ? 1 : -1;
            for (var y = Math.Max(0, (int)Math.Floor(y0 - 0.5)); y < _h; y++)
            {
                var yc = y + 0.5;
                if (yc < y0) continue;
                if (yc >= y1) break;
                zeilen[y].Add((a.X + (yc - a.Y) * (bp.X - a.X) / (bp.Y - a.Y), richtung));
            }
        }
        var innen = new bool[_b * _h];
        for (var y = 0; y < _h; y++)
        {
            var schnitte = zeilen[y];
            schnitte.Sort((p, q) => p.X.CompareTo(q.X));
            int k = 0, w = 0, zahl = 0;
            for (var x = 0; x < _b; x++)
            {
                var xc = x + 0.5;
                while (k < schnitte.Count && schnitte[k].X < xc)
                {
                    w += schnitte[k].Richtung;
                    zahl++;
                    k++;
                }
                innen[y * _b + x] = gerade ? zahl % 2 == 1 : w != 0;
            }
        }
        return innen;
    }

    /// <summary>
    /// Je Stück die Richtung nach außen und geglättete Richtungen an Anfang und Ende (Mittel mit dem Nachbarn derselben
    /// Figur, außer an echten Ecken). Welche Seite außen ist, wird je Figur einmal am längsten Stück geprüft.
    /// </summary>
    private static (Vector[] Stueck, Vector[] Anfang, Vector[] Ende) Normalen(List<(Point A, Point B, int Figur)> stuecke, bool gerade)
    {
        bool Innen(double x, double y)
        {
            int w = 0, zahl = 0;
            foreach (var (a, bp, _) in stuecke)
            {
                if ((a.Y <= y) == (bp.Y <= y)) continue;
                var xs = a.X + (y - a.Y) * (bp.X - a.X) / (bp.Y - a.Y);
                if (xs >= x) continue;
                w += bp.Y > a.Y ? 1 : -1;
                zahl++;
            }
            return gerade ? zahl % 2 == 1 : w != 0;
        }

        var aussenLinks = new Dictionary<int, bool>();
        foreach (var gruppe in stuecke.GroupBy(s => s.Figur))
        {
            var (a, bp, _) = gruppe.MaxBy(s => (s.B - s.A).Length);
            var d = bp - a;
            d.Normalize();
            var mitte = new Point((a.X + bp.X) / 2, (a.Y + bp.Y) / 2);
            // Linke Seite der Laufrichtung in Bildkoordinaten (y nach unten): (d.Y, -d.X).
            aussenLinks[gruppe.Key] = !Innen(mitte.X + d.Y * 0.05, mitte.Y - d.X * 0.05);
        }
        var stueck = new Vector[stuecke.Count];
        for (var i = 0; i < stuecke.Count; i++)
        {
            var d = stuecke[i].B - stuecke[i].A;
            d.Normalize();
            stueck[i] = aussenLinks[stuecke[i].Figur] ? new Vector(d.Y, -d.X) : new Vector(-d.Y, d.X);
        }
        var anfang = (Vector[])stueck.Clone();
        var ende = (Vector[])stueck.Clone();
        var ersteDerFigur = new Dictionary<int, int>();
        for (var i = 0; i < stuecke.Count; i++) ersteDerFigur.TryAdd(stuecke[i].Figur, i);
        var grenze = Math.Cos(30 * Math.PI / 180);
        for (var i = 0; i < stuecke.Count; i++)
        {
            var j = i + 1 < stuecke.Count && stuecke[i + 1].Figur == stuecke[i].Figur ? i + 1 : ersteDerFigur[stuecke[i].Figur];
            if ((stuecke[j].A - stuecke[i].B).Length > 1e-6 || Vector.Multiply(stueck[i], stueck[j]) < grenze) continue;
            var m = stueck[i] + stueck[j];
            m.Normalize();
            ende[i] = m;
            anfang[j] = m;
        }
        return (stueck, anfang, ende);
    }
}
