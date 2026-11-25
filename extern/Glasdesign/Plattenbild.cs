// Glasdesign - Maintainer: Sarevoc
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glasdesign;

/// <summary>
/// Wulst, Kern und Lichtlinie einer Dickglasplatte als ein Bild (seit 0.5.0-beta.12, nur für das Zwischenbild von
/// <see cref="Dickglaskoerper"/>). Es sind dieselben Schichten wie als einzelne Formen: Ring j ist die um j Pixel eingerückte
/// Platte mit ihrem senkrechten Verlauf, darüber der nächste, dann der Kern und die Lichtlinie (der Ring zwischen dem Kern und
/// einem Pixel weiter innen). Hier je Pixel gerechnet und über die Zeilen auf mehrere Fäden verteilt: Das Software-Zeichnen
/// kostete jeden der rund 35 Ringe einer großen Karte einen festen Betrag, der sich auch auf mehrere Fäden nicht verteilen
/// ließ (WPF rastert Formen nacheinander). An den geraden Kanten liegen die Ringe auf ganzen Pixeln; dort gilt die Farbe des
/// innersten Rings, der das Pixel ganz deckt. In den Ecken nimmt das Bild die Deckung jeder Form so, wie WPF sie glättet
/// (<see cref="Eckmasken"/>), und legt Schicht über Schicht wie beim Zeichnen; so auch die Außenkante. Die Verläufe wie in
/// WPF: linear zwischen den Stopps an der Pixelmitte. Gegen die einzeln gezeichneten Schichten höchstens 2 von 255 Stufen
/// Unterschied (Rundung).
/// </summary>
internal static class Plattenbild
{
    /// <summary>
    /// Das Bild der Platte (b mal hp Pixel, Eckradius in Pixeln) aus den Verläufen der n Ringe über die Plattenhöhe und denen
    /// des Kerns und der Lichtlinie über die Kernhöhe, eingefroren; null, wenn die Platte dafür zu klein ist.
    /// </summary>
    public static BitmapSource? Rechnen(int b, int hp, double radius, int n, IReadOnlyList<(Color Farbe, double Lage)[]> ringe,
        (Color Farbe, double Lage)[] kern, (Color Farbe, double Lage)[] linie, double skala)
    {
        ArgumentNullException.ThrowIfNull(ringe);
        ArgumentNullException.ThrowIfNull(kern);
        ArgumentNullException.ThrowIfNull(linie);
        var r = (int)Math.Round(radius);
        var ecke = r + 2;
        if (b < 2 * ecke || hp < 2 * ecke || n < 1 || 2 * (n + 1) >= Math.Min(b, hp) || ringe.Count != n || Math.Abs(radius - r) > 1e-9) return null;
        var schluessel = Schluessel(b, hp, radius, n, ringe, kern, linie, skala);
        if (Vorrat.Holen(schluessel) is { } fertigesBild) return fertigesBild;
        var masken = Eckmasken.Holen(r, skala, n + 1);
        var seite = masken.Seite;
        var (von, bis) = masken.Bereiche(n);
        var kernhoehe = hp - 2.0 * n;
        var puffer = new uint[b * hp];
        // Die Verläufe als Felder (seit 0.5.0-beta.13, A7): je Stopp Lage und Kanäle, dieselbe Rechnung wie Verlauf, ohne Tupel je
        // Ring und Zeile. Haben alle Ringe dieselben Lagen (so baut sie der Dickglaskörper), gilt je Zeile ein Abschnitt für alle.
        var ringfelder = ringe.Select(Stoppfeld.Aus).ToArray();
        var kernfeld = Stoppfeld.Aus(kern);
        var linienfeld = Stoppfeld.Aus(linie);
        var gleicheLagen = ringfelder.All(f => f.Lage.AsSpan().SequenceEqual(ringfelder[0].Lage));
        void Zeile(int y, double[] kanal, uint[] fertig)
        {
            // Die Farben dieser Zeile: Ring j (0 bis n-1) und der Kern (n) als Kanäle und gepackt, dazu die Lichtlinie.
            var t = (y + 0.5) / hp;
            if (gleicheLagen)
            {
                var (i, f) = ringfelder[0].Abschnitt(t);
                for (var j = 0; j < n; j++) ringfelder[j].Kanaele(i, f, kanal, 3 * j);
            }
            else
                for (var j = 0; j < n; j++) ringfelder[j].Kanaele(t, kanal, 3 * j);
            kernfeld.Kanaele((y + 0.5 - n) / kernhoehe, kanal, 3 * n);
            for (var j = 0; j <= n; j++) fertig[j] = Packen(1, kanal[3 * j], kanal[3 * j + 1], kanal[3 * j + 2]);
            var strich = Verlauf(linie, (y + 0.5 - n) / kernhoehe);
            var mitStrich = Packen(1, strich.Rot * strich.Alpha + kanal[3 * n] * (1 - strich.Alpha), strich.Gruen * strich.Alpha + kanal[3 * n + 1] * (1 - strich.Alpha),
                strich.Blau * strich.Alpha + kanal[3 * n + 2] * (1 - strich.Alpha));
            var dy = Math.Min(y, hp - 1 - y);
            var my = y < ecke ? y : seite - (hp - y);
            var zeile = y * b;
            // An den geraden Kanten deckt der innerste Ring das Pixel ganz (Ring min(dx, dy)); die Lichtlinie liegt genau auf Ring n.
            // Seit 0.5.0-beta.13 (A7) in Stücken gefüllt statt Pixel für Pixel: Nur die äußeren Ringe links und rechts tragen eigene
            // Farben, dazwischen steht eine Farbe (die der Zeile); dieselben Pixel, ein Bruchteil der Zeit.
            uint Farbe(int d) => d == n ? mitStrich : fertig[Math.Min(d, n)];
            var reihe = puffer.AsSpan(zeile, b);
            if (dy >= ecke)
            {
                var k = Math.Min(dy, n + 1);
                for (var dx = 0; dx < k; dx++) reihe[dx] = reihe[b - 1 - dx] = Farbe(dx);
                reihe[k..(b - k)].Fill(Farbe(dy));
                return;
            }
            reihe[ecke..(b - ecke)].Fill(Farbe(dy));
            for (var e = 0; e < 2 * ecke; e++)
            {
                var x = e < ecke ? e : b - 2 * ecke + e;
                // In den Ecken: Schicht über Schicht mit der Deckung jeder Form, wie WPF sie glättet. Seit beta.13 (A7) nur über die
                // Formen, die das Pixel teilweise decken (Eckmasken.Bereiche): Was eine ganz deckende Form zudeckt, zählt nicht, und
                // hinter der ersten Form ohne Deckung deckt keine mehr. Dieselbe Rechnung, dasselbe Ergebnis.
                var stelle = my * seite + (x < ecke ? x : seite - (b - x));
                double rr = 0, gg = 0, bb = 0, aa = 0;
                for (var j = von[stelle]; j < bis[stelle]; j++)
                {
                    var c = masken.Deckung(j, stelle);
                    if (c <= 0) continue;
                    rr = kanal[3 * j] * c + rr * (1 - c);
                    gg = kanal[3 * j + 1] * c + gg * (1 - c);
                    bb = kanal[3 * j + 2] * c + bb * (1 - c);
                    aa = c + aa * (1 - c);
                }
                var cl = strich.Alpha * Math.Max(0, masken.Deckung(n, stelle) - masken.Deckung(n + 1, stelle));
                if (cl > 0)
                {
                    rr = strich.Rot * cl + rr * (1 - cl);
                    gg = strich.Gruen * cl + gg * (1 - cl);
                    bb = strich.Blau * cl + bb * (1 - cl);
                    aa = cl + aa * (1 - cl);
                }
                puffer[zeile + x] = Packen(aa, rr, gg, bb);
            }
        }
        // Große Platten in Blöcken von 64 Zeilen auf höchstens vier Fäden (seit beta.13, A7; bis dahin jede Zeile einzeln auf allen
        // Fäden: in Glint 1,1 bis 2,2 ms je Bild, weil jedes Wecken mehr kostete als die Zeile), kleine auf dem rufenden Faden.
        const int Block = 64;
        if ((long)b * hp >= 100_000)
            Parallel.For(0, (hp + Block - 1) / Block, new ParallelOptions { MaxDegreeOfParallelism = 4 }, () => (new double[(n + 1) * 3], new uint[n + 1]),
                (k, _, l) =>
                {
                    for (var y = k * Block; y < Math.Min(hp, (k + 1) * Block); y++) Zeile(y, l.Item1, l.Item2);
                    return l;
                }, _ => { });
        else
        {
            var (kanal, fertig) = (new double[(n + 1) * 3], new uint[n + 1]);
            for (var y = 0; y < hp; y++) Zeile(y, kanal, fertig);
        }
        var bild = BitmapSource.Create(b, hp, 96 * skala, 96 * skala, PixelFormats.Pbgra32, null, puffer, b * 4);
        bild.Freeze();
        Vorrat.Ablegen(schluessel, bild);
        Gerechnet++;
        return bild;
    }

    /// <summary>
    /// Ein Verlauf als Felder (seit 0.5.0-beta.13, A7): Lagen und Kanäle je Stopp (Deckung, Rot, Grün, Blau, wie Color sie hat).
    /// Rechnet wie <see cref="Verlauf"/>, Bit für Bit, ohne für jeden Wert ein Tupel zu bauen.
    /// </summary>
    private sealed class Stoppfeld
    {
        public double[] Lage { get; private init; } = [];
        private double[] _a = [], _r = [], _g = [], _b = [];

        public static Stoppfeld Aus((Color Farbe, double Lage)[] stopps) => new()
        {
            Lage = [.. stopps.Select(s => s.Lage)],
            _a = [.. stopps.Select(s => (double)s.Farbe.A)],
            _r = [.. stopps.Select(s => (double)s.Farbe.R)],
            _g = [.. stopps.Select(s => (double)s.Farbe.G)],
            _b = [.. stopps.Select(s => (double)s.Farbe.B)],
        };

        /// <summary>Der Abschnitt bei t: i (0 vor dem ersten Stopp, Länge hinter dem letzten) und der Anteil f darin.</summary>
        public (int I, double F) Abschnitt(double t)
        {
            if (t <= Lage[0]) return (0, 0);
            for (var i = 1; i < Lage.Length; i++)
            {
                if (t > Lage[i]) continue;
                return (i, Lage[i] > Lage[i - 1] ? (t - Lage[i - 1]) / (Lage[i] - Lage[i - 1]) : 1);
            }
            return (Lage.Length, 0);
        }

        /// <summary>Rot, Grün und Blau bei t nach <paramref name="ziel"/> ab <paramref name="stelle"/>.</summary>
        public void Kanaele(double t, double[] ziel, int stelle)
        {
            var (i, f) = Abschnitt(t);
            Kanaele(i, f, ziel, stelle);
        }

        /// <summary>Rot, Grün und Blau im Abschnitt i mit dem Anteil f (aus <see cref="Abschnitt"/>).</summary>
        public void Kanaele(int i, double f, double[] ziel, int stelle)
        {
            if (i == 0 || i == Lage.Length)
            {
                var k = i == 0 ? 0 : Lage.Length - 1;
                (ziel[stelle], ziel[stelle + 1], ziel[stelle + 2]) = (_r[k], _g[k], _b[k]);
                return;
            }
            ziel[stelle] = _r[i - 1] + (_r[i] - _r[i - 1]) * f;
            ziel[stelle + 1] = _g[i - 1] + (_g[i] - _g[i - 1]) * f;
            ziel[stelle + 2] = _b[i - 1] + (_b[i] - _b[i - 1]) * f;
        }
    }

    /// <summary>
    /// Die zuletzt gerechneten Plattenbilder je Faden (seit 0.5.0-beta.13, A7): Ein Wert, der nur Glanz oder Licht ändert (Glanzregler
    /// der Familie), baut den Körper neu, die Platte bleibt dieselbe. Höchstens 32 Bilder und 16 MB.
    /// </summary>
    [ThreadStatic]
    private static Teilvorrat<BitmapSource>? _vorrat;

    private static Teilvorrat<BitmapSource> Vorrat => _vorrat ??= new Teilvorrat<BitmapSource>(32, 16L << 20, z => 4L * z.PixelWidth * z.PixelHeight);

    /// <summary>Wie oft auf diesem Faden ein Plattenbild gerechnet wurde (für die Tests: gleiche Platten rechnen keines).</summary>
    [ThreadStatic]
    private static int _gerechnet;

    internal static int Gerechnet { get => _gerechnet; private set => _gerechnet = value; }

    /// <summary>Den Vorrat dieses Fadens leeren (für die Tests).</summary>
    internal static void VorratLeeren() => Vorrat.Leeren();

    /// <summary>Der Schlüssel eines Plattenbilds: Maße und alle Farben und Lagen der Verläufe als Text (Werte, keine Objekte).</summary>
    private static string Schluessel(int b, int hp, double radius, int n, IReadOnlyList<(Color Farbe, double Lage)[]> ringe,
        (Color Farbe, double Lage)[] kern, (Color Farbe, double Lage)[] linie, double skala)
    {
        var t = new System.Text.StringBuilder();
        t.Append(System.Globalization.CultureInfo.InvariantCulture, $"{b} {hp} {radius:R} {n} {skala:R}");
        void Stopps((Color Farbe, double Lage)[] stopps)
        {
            t.Append('|');
            foreach (var (f, l) in stopps) t.Append(System.Globalization.CultureInfo.InvariantCulture, $"{f}@{l:R};");
        }
        foreach (var r in ringe) Stopps(r);
        Stopps(kern);
        Stopps(linie);
        return t.ToString();
    }

    /// <summary>Ein Pixel vormultipliert (Deckung 0 bis 1, Kanäle schon mit der Deckung multipliert) als BGRA.</summary>
    private static uint Packen(double alpha, double rot, double gruen, double blau) =>
        ((uint)Math.Round(alpha * 255) << 24) | ((uint)Math.Round(rot) << 16) | ((uint)Math.Round(gruen) << 8) | (uint)Math.Round(blau);

    /// <summary>Die Farbe eines Verlaufs bei t (außerhalb von 0 bis 1 die Endfarbe), linear zwischen den Stopps je Kanal; Deckung 0 bis 1.</summary>
    private static (double Alpha, double Rot, double Gruen, double Blau) Verlauf((Color Farbe, double Lage)[] stopps, double t)
    {
        static (double, double, double, double) Von(Color c) => (c.A / 255.0, c.R, c.G, c.B);
        if (t <= stopps[0].Lage) return Von(stopps[0].Farbe);
        for (var i = 1; i < stopps.Length; i++)
        {
            if (t > stopps[i].Lage) continue;
            var (a, z) = (stopps[i - 1].Farbe, stopps[i].Farbe);
            var f = stopps[i].Lage > stopps[i - 1].Lage ? (t - stopps[i - 1].Lage) / (stopps[i].Lage - stopps[i - 1].Lage) : 1;
            return ((a.A + (z.A - a.A) * f) / 255.0, a.R + (z.R - a.R) * f, a.G + (z.G - a.G) * f, a.B + (z.B - a.B) * f);
        }
        return Von(stopps[^1].Farbe);
    }
}

/// <summary>
/// Die Deckung der eingerückten Formen in den vier Ecken einer Platte, so wie WPF sie glättet (seit 0.5.0-beta.12, für das
/// <see cref="Plattenbild"/>): Form j (um j Pixel eingerückt, Eckradius r minus j) einmal an einer kleinen Platte von WPF
/// gezeichnet, die Deckung der vier Eckquadrate (r plus 2 Pixel) gemerkt. Die Ecken liegen, um ganze Pixel verschoben, genau so
/// an jeder größeren Platte. WPF glättet nicht nach der Fläche (am Bogen rund ein Zehntel weniger, nicht spiegelgleich); darum
/// gemessen statt gerechnet. Gemerkt je Eckradius und Skalierung (höchstens 16), Formen nach Bedarf ergänzt.
/// </summary>
internal sealed class Eckmasken
{
    private static readonly Dictionary<(int R, double S), Eckmasken> Vorrat = [];
    private static readonly Lock Sperre = new();
    private readonly int _r;
    private readonly double _s;
    private readonly List<byte[]> _deckung = [];

    /// <summary>Wie oft eine Form für die Masken gezeichnet wurde (für die Tests: neue Größen zeichnen keine).</summary>
    internal static int Gezeichnet { get; private set; }

    private Eckmasken(int r, double s) => (_r, _s, Seite) = (r, s, 2 * (r + 2));

    /// <summary>Seite der kleinen Platte in Pixeln; die Ecken sind ihre vier Viertel.</summary>
    public int Seite { get; }

    /// <summary>Die Masken für Radius <paramref name="r"/> (Pixel) und Skalierung <paramref name="s"/> mit mindestens den Formen 0 bis <paramref name="bis"/>.</summary>
    public static Eckmasken Holen(int r, double s, int bis)
    {
        lock (Sperre)
        {
            if (!Vorrat.TryGetValue((r, s), out var m))
            {
                if (Vorrat.Count >= 16) Vorrat.Clear();
                m = new Eckmasken(r, s);
                Vorrat[(r, s)] = m;
            }
            m.Ergaenzen(bis);
            return m;
        }
    }

    /// <summary>Deckung der Form j am Pixel <paramref name="stelle"/> der kleinen Platte (0 bis 1).</summary>
    public double Deckung(int j, int stelle) => _deckung[j][stelle] / 255.0;

    private readonly Dictionary<int, (int[] Von, int[] Bis)> _bereiche = [];

    /// <summary>
    /// Je Pixel der kleinen Platte die Formen aus 0 bis <paramref name="n"/>, die zählen (seit 0.5.0-beta.13, A7): von der innersten,
    /// die das Pixel ganz deckt (sie deckt alle äußeren zu), bis hinter die innerste, die es überhaupt deckt. Je n einmal gerechnet.
    /// </summary>
    public (int[] Von, int[] Bis) Bereiche(int n)
    {
        lock (Sperre)
        {
            if (_bereiche.TryGetValue(n, out var fertig)) return fertig;
            Ergaenzen(n);
            var flaeche = Seite * Seite;
            var (von, bis) = (new int[flaeche], new int[flaeche]);
            for (var p = 0; p < flaeche; p++)
                for (var j = 0; j <= n; j++)
                {
                    var d = _deckung[j][p];
                    if (d == 255) von[p] = j;
                    if (d > 0) bis[p] = j + 1;
                }
            _bereiche[n] = (von, bis);
            return (von, bis);
        }
    }

    private void Ergaenzen(int bis)
    {
        var f = new Glasflaeche(0, 0, Seite, Seite, _r, _s);
        while (_deckung.Count <= bis)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawGeometry(Brushes.White, null, f.Form(_deckung.Count));
            var bild = new RenderTargetBitmap(Seite, Seite, 96 * _s, 96 * _s, PixelFormats.Pbgra32);
            bild.Render(visual);
            var pixel = new byte[Seite * Seite * 4];
            bild.CopyPixels(pixel, Seite * 4, 0);
            var alpha = new byte[Seite * Seite];
            for (var i = 0; i < alpha.Length; i++) alpha[i] = pixel[4 * i + 3];
            _deckung.Add(alpha);
            Gezeichnet++;
        }
    }
}
