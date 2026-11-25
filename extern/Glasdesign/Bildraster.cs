// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Glasdesign;

/// <summary>
/// Zwischenbild (seit 0.5.0-beta.10): rechnet eine fertige Zeichnung einmal in ein Bild in Gerätepixeln, wahlweise samt
/// Effekt (Unschärfe, Schatten), und gibt Bild und Lage in DIP zurück. Das Bild beginnt und endet auf ganzen Gerätepixeln und
/// hat die Auflösung der Skalierung; gesetzt an seine Lage deckt es Pixel für Pixel dieselben Stellen wie die Zeichnung.
/// Gebraucht für große Flächen (Karten), deren Schichten das Zeichnen im Speicher sonst bei jedem Bild neu rastert
/// (<see cref="Familienkoerper.Zwischenbild"/>, <see cref="Sprechblase.Zwischenbild"/>).
/// <para>
/// Seit 0.5.0-beta.12 rechnet es große Bilder in Streifen auf mehreren Fäden (<see cref="Streifen"/>): Bei neuer Größe muss eine
/// Karte ihr Bild neu rechnen, und das Software-Zeichnen rastert jede Schicht auf einem Faden (Glutglas-Karte 480 mal 360 DIP
/// bei 175 Prozent 15 ms, auf vier Streifen rund 7 ms). Jeder Streifen zeichnet dieselbe eingefrorene Zeichnung, um ganze Pixel
/// verschoben, in ein eigenes Bild; die Pixel kommen danach in ein Bild zusammen (gegen ein Stück höchstens eine Stufe
/// Unterschied an einzelnen Pixeln). Ohne Effekt in Zeilen; mit Effekt (die Unschärfe des Dickglas-Schattens) in Spalten, Pixel für
/// Pixel gleich: Die Nachbarn, die der Effekt braucht, holt WPF selbst aus der Zeichnung (seit 0.5.0-beta.13, A8, ohne eigenen
/// Rand; bis dahin rechnete jede Spalte die Unschärfe ihrer Nachbarn um die Reichweite mit). Seit 0.5.0-beta.13 (A8) höchstens acht
/// Streifen, eine feste Zahl: Wie viele Streifen ein Bild bekommt, hängt nur an seiner Größe, nicht an den Kernen des Rechners,
/// denn ohne Effekt verschieben andere Grenzen einzelne Pixel um eine Stufe (bis beta.13 A7 halb so viele Streifen wie logische
/// Prozessoren, höchstens vier). WPF rastert Formen auf allen Fäden nacheinander, nur das Füllen läuft gleichzeitig; acht statt
/// vier brachten der Glutglas-Karte 480 mal 360 DIP bei 175 Prozent trotzdem rund 3 ms. Die Fäden sind Hintergrundfäden (STA, je
/// ein Dispatcher), werden beim ersten großen Bild angelegt und bleiben bis zum Programmende; höchstens einer weniger als Kerne,
/// die übrigen Streifen rechnet der rufende Faden nacheinander (dieselben Pixel).
/// </para>
/// </summary>
internal static class Bildraster
{
    /// <summary>Größte Seite eines Zwischenbilds in Pixeln; darüber wird direkt gezeichnet.</summary>
    public const int Hoechstens = 6000;

    /// <summary>So viele Pixel rechnet ein Streifen mindestens; kleinere Bilder bleiben auf dem rufenden Faden.</summary>
    public const int PixelJeStreifen = 60_000;

    /// <summary>Höchstens so viele Streifen oder Spalten, auf jedem Rechner gleich (seit A8 fest 8); 1 rechnet am Stück (für die Tests).</summary>
    internal static int Streifen { get; set; } = 8;

    /// <summary>Wie viele Bilder in Streifen gerechnet wurden (für die Tests).</summary>
    internal static int Gestreift { get; private set; }

    private static readonly Lock Sperre = new();
    private static readonly List<Dispatcher> Helfer = [];

    /// <summary>Wie weit ein Effekt über die Zeichnung hinausreicht (DIP, großzügig: Radien gelten in DIP oder Pixeln).</summary>
    public static double Reichweite(Effect? effekt) => effekt switch
    {
        null => 0,
        BlurEffect b => b.Radius + 2,
        DropShadowEffect d => d.BlurRadius + d.ShadowDepth + 2,
        _ => 32,
    };

    /// <summary>Die Zeichnung (samt <paramref name="effekt"/>) als eingefrorenes Bild bei <paramref name="skala"/>; null, wenn leer oder zu groß.</summary>
    public static (ImageSource Bild, Rect Lage)? Rastern(Drawing zeichnung, double skala, Effect? effekt = null)
    {
        ArgumentNullException.ThrowIfNull(zeichnung);
        var g = zeichnung.Bounds;
        if (g.IsEmpty || g.Width <= 0 || g.Height <= 0 || skala <= 0) return null;
        var rand = Reichweite(effekt);
        g.Inflate(rand, rand);
        var x0 = Math.Floor(g.Left * skala);
        var y0 = Math.Floor(g.Top * skala);
        var b = (int)(Math.Ceiling(g.Right * skala) - x0);
        var h = (int)(Math.Ceiling(g.Bottom * skala) - y0);
        if (b <= 0 || h <= 0 || b > Hoechstens || h > Hoechstens) return null;
        var lage = new Rect(x0 / skala, y0 / skala, b / skala, h / skala);
        // Mit Effekt (Unschärfe) in Spalten, jede mindestens so breit wie die Reichweite des Effekts (die Nachbarn holt WPF selbst);
        // er kostet je Pixel ein Vielfaches, darum schon ab einem Viertel der Pixel.
        var randPx = effekt is null ? 0 : (int)Math.Ceiling(Reichweite(effekt) * skala);
        var teile = zeichnung.IsFrozen && effekt is null or { IsFrozen: true }
            ? (int)Math.Clamp((long)b * h / (effekt is null ? PixelJeStreifen : PixelJeStreifen / 4), 1, Math.Max(1, Streifen)) : 1;
        teile = Math.Min(teile, effekt is null ? h : b / Math.Max(1, randPx));
        if (teile > 1 && StreifenRechnen(zeichnung, skala, x0, y0, b, h, teile, effekt) is { } streifen)
        {
            Gestreift++;
            return (streifen, lage);
        }
        // Am Stück in ein eigenes Bild, das selbst das Ergebnis ist (eine Kopie aus dem Zeichenbrett kostete mehr, als sie spart).
        var bild = Rechnen(zeichnung, skala, x0, y0, b, h, effekt, eigenes: true);
        bild.Freeze();
        return (bild, lage);
    }

    /// <summary>
    /// Die Zeichnung, um (x0, y0) Pixel verschoben, in ein Bild von b mal h Pixeln (<paramref name="eigenes"/>) oder oben links in das
    /// Zeichenbrett dieses Fadens (<see cref="Brett"/>, für Streifen); die Pixel stehen danach in dessen Bereich b mal h.
    /// </summary>
    private static RenderTargetBitmap Rechnen(Drawing zeichnung, double skala, double x0, double y0, int b, int h, Effect? effekt = null, bool eigenes = false)
    {
        var visual = new DrawingVisual();
        if (effekt is not null) visual.Effect = effekt;
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-x0 / skala, -y0 / skala));
            dc.DrawDrawing(zeichnung);
            dc.Pop();
        }
        var brett = eigenes ? new RenderTargetBitmap(b, h, 96 * skala, 96 * skala, PixelFormats.Pbgra32) : Brett(b, h, skala);
        brett.Render(visual);
        return brett;
    }

    private static int _bretter;

    /// <summary>
    /// Ein Zeichenbrett dieses Fadens für einen Streifen, mindestens b mal h Pixel bei <paramref name="skala"/>, leer (seit
    /// 0.5.0-beta.13, A7). Bis dahin legte jeder Streifen ein eigenes RenderTargetBitmap an; jedes meldet seinen Speicher dem Garbage
    /// Collector, und der räumte in Glint bei einem gezogenen Regler fast bei jedem Schritt ganz auf (31 volle Durchgänge in 40
    /// Schritten, Dickglas-Karte; danach 5). Die Bretter werden auf 64 Pixel gerundet angelegt und vor jedem Streifen geleert; die
    /// Pixel kommen ohnehin in den Puffer des Bilds. Bretter je Faden (der rufende und die Hilfsfäden), weil Bilder an ihrem Faden hängen.
    /// </summary>
    private static RenderTargetBitmap Brett(int b, int h, double skala)
    {
        // Höchstens drei Bretter je Faden (Streifen und Spalten verschiedener Bilder); genommen wird das kleinste, das passt und
        // höchstens doppelt so viele Pixel hat (Leeren kostet je Pixel). Große Streifen (über eine Million Pixel) bekommen ein Brett nur
        // für sich: Der Speicher je Faden bleibt so unter 12 MB.
        var bretter = _bretter4 ??= [];
        RenderTargetBitmap? brett = null;
        foreach (var x in bretter)
            if (x.PixelWidth >= b && x.PixelHeight >= h && x.DpiX.Equals(96 * skala) && (long)x.PixelWidth * x.PixelHeight <= 2L * Math.Max(4096, b * h)
                && (brett is null || (long)x.PixelWidth * x.PixelHeight < (long)brett.PixelWidth * brett.PixelHeight))
                brett = x;
        if (brett is not null)
        {
            bretter.Remove(brett);
            bretter.Add(brett);
            brett.Clear();
            return brett;
        }
        static int Gerundet(int n) => (n + 63) / 64 * 64;
        // Höhe mit Platz für einen Streifen weniger (seit A8, acht Streifen): Wird ein Bild etwas kleiner, fällt es auf sieben Streifen,
        // jeder ein Siebtel höher; ohne den Platz legte jede solche Größe neue Bretter an. Nur, wenn das Brett dafür wiederverwendbar bleibt.
        var (bb, bh) = (Gerundet(b), Gerundet(h + h / 7));
        if ((long)bb * bh > 2L * Math.Max(4096, b * h)) bh = Gerundet(h);
        brett = new RenderTargetBitmap(bb, bh, 96 * skala, 96 * skala, PixelFormats.Pbgra32);
        Interlocked.Increment(ref _bretter);
        if ((long)b * h > 1_000_000) return brett;
        if (bretter.Count >= 3) bretter.RemoveAt(0);
        bretter.Add(brett);
        return brett;
    }

    [ThreadStatic]
    private static List<RenderTargetBitmap>? _bretter4;

    [ThreadStatic]
    private static byte[]? _puffer;

    /// <summary>Wie oft ein Zeichenbrett angelegt wurde, auf allen Fäden (für die Tests: neue Größen legen kaum eines an).</summary>
    internal static int Bretter => Volatile.Read(ref _bretter);

    /// <summary>Der Puffer dieses Fadens für die Pixel eines Bilds (wächst nur; BitmapSource.Create kopiert daraus).</summary>
    private static byte[] Puffer(long laenge)
    {
        if (_puffer is null || _puffer.LongLength < laenge) _puffer = new byte[Math.Max(laenge, (_puffer?.LongLength ?? 0) * 5 / 4)];
        return _puffer;
    }

    /// <summary>
    /// Rechnet <paramref name="teile"/> Streifen gleichzeitig (ohne Effekt Zeilen, mit Effekt Spalten): den ersten auf dem rufenden Faden, die übrigen auf den Hilfsfäden; die Pixel landen in einem
    /// Puffer, daraus ein eingefrorenes Bild. Null, wenn ein Hilfsfaden scheitert oder nicht rechtzeitig fertig wird (dann rechnet
    /// der Aufrufer das Bild wie bisher ganz).
    /// </summary>
    private static BitmapSource? StreifenRechnen(Drawing zeichnung, double skala, double x0, double y0, int b, int h, int teile, Effect? effekt)
    {
        // Höchstens einer weniger Hilfsfäden als Kerne; die übrigen Streifen rechnet der rufende Faden (dieselben Pixel).
        var helfer = HelferHolen(Math.Min(teile - 1, Math.Max(0, Environment.ProcessorCount - 1)));
        var schritt = b * 4;
        var puffer = Puffer((long)schritt * h);
        var auftrag = new Streifenauftrag(helfer.Count);
        // Ohne Effekt bekommt jeder Streifen nur die Teile der Zeichnung, die seine Zeilen berühren (seit 0.5.0-beta.13, A8).
        var teilzeichnungen = effekt is null ? Zeilenweise(zeichnung, skala, y0, h, teile) : null;
        void Einer(int k)
        {
            if (teilzeichnungen is not null)
            {
                var oben = h * k / teile;
                var unten = h * (k + 1) / teile;
                var zeilen = Rechnen(teilzeichnungen[k], skala, x0, y0 + oben, b, unten - oben);
                zeilen.CopyPixels(new Int32Rect(0, 0, b, unten - oben), puffer, schritt, schritt * oben);
                return;
            }
            // Nur die Spalte selbst (seit 0.5.0-beta.13, A8): Die Nachbarn, die der Effekt braucht, holt WPF selbst aus der
            // Zeichnung; ein eigener Rand rechnete die Unschärfe dort ein zweites Mal, Pixel für Pixel dasselbe (Dickglas-Karte
            // 360 mal 260 bei 175 Prozent 7,3 statt 5,6 ms je neue Breite).
            var von = b * k / teile;
            var bis = b * (k + 1) / teile;
            var spalten = Rechnen(zeichnung, skala, x0 + von, y0, bis - von, h, effekt);
            spalten.CopyPixels(new Int32Rect(0, 0, bis - von, h), puffer, schritt, von * 4);
        }
        for (var k = 1; k <= helfer.Count; k++)
        {
            var kk = k;
            helfer[k - 1].BeginInvoke(DispatcherPriority.Send, new Action(() => auftrag.Ausfuehren(() => Einer(kk))));
        }
        Einer(0);
        for (var k = helfer.Count + 1; k < teile; k++) Einer(k);
        if (!auftrag.Abwarten()) return null;
        var ergebnis = BitmapSource.Create(b, h, 96 * skala, 96 * skala, PixelFormats.Pbgra32, null, puffer, schritt);
        ergebnis.Freeze();
        return ergebnis;
    }

    /// <summary>
    /// Je Streifen (Zeilen von oben nach unten) nur die Teile einer Zeichnung, die seine Zeilen berühren, in derselben Reihenfolge
    /// (seit 0.5.0-beta.13, A8). WPF rastert Formen auch auf mehreren Fäden nacheinander und zahlt je Form und Streifen einen festen
    /// Betrag; ein Licht unten in der Dickglas-Platte oder der Glanz oben kostete so jeden Streifen. Ein Teil, das die Zeilen eines
    /// Streifens nicht berührt (Grenzen mit zwei Pixeln Sicherheit für die Glättung), trägt dort kein Pixel bei: dieselben Pixel.
    /// Nur für eine schlichte Gruppe (ohne Transformation, Ausschnitt, Deckung, Maske und eigene Zeichenoptionen); sonst die
    /// ganze Zeichnung für jeden Streifen.
    /// </summary>
    private static Drawing[] Zeilenweise(Drawing zeichnung, double skala, double y0, int h, int teile)
    {
        var ganz = Enumerable.Repeat(zeichnung, teile).ToArray();
        if (zeichnung is not DrawingGroup { Transform: null, ClipGeometry: null, OpacityMask: null, GuidelineSet: null } gruppe || gruppe.Opacity != 1
            || gruppe.Children.Count < 2 || gruppe.ReadLocalValue(RenderOptions.BitmapScalingModeProperty) != DependencyProperty.UnsetValue
            || gruppe.ReadLocalValue(RenderOptions.EdgeModeProperty) != DependencyProperty.UnsetValue
            || gruppe.ReadLocalValue(RenderOptions.ClearTypeHintProperty) != DependencyProperty.UnsetValue
            || gruppe.ReadLocalValue(RenderOptions.CachingHintProperty) != DependencyProperty.UnsetValue)
            return ganz;
        var grenzen = gruppe.Children.Select(x => x.Bounds).ToArray();
        var sicherheit = 2 / skala;
        for (var k = 0; k < teile; k++)
        {
            var oben = (y0 + h * k / teile) / skala - sicherheit;
            var unten = (y0 + h * (k + 1) / teile) / skala + sicherheit;
            var teil = new DrawingGroup();
            for (var i = 0; i < grenzen.Length; i++)
                if (!grenzen[i].IsEmpty && grenzen[i].Bottom >= oben && grenzen[i].Top <= unten)
                    teil.Children.Add(gruppe.Children[i]);
            if (teil.Children.Count == gruppe.Children.Count) continue;
            teil.Freeze();
            ganz[k] = teil;
            Interlocked.Increment(ref _ausgelassen);
        }
        return ganz;
    }

    private static int _ausgelassen;

    /// <summary>Wie oft ein Streifen nur einen Teil der Zeichnung bekam (für die Tests).</summary>
    internal static int Ausgelassen => Volatile.Read(ref _ausgelassen);

    /// <summary>Zählt die Streifen der Hilfsfäden ab; ein Fehler dort bleibt dort und lässt den Aufrufer ganz rechnen.</summary>
    private sealed class Streifenauftrag(int zahl)
    {
        private readonly object _sperre = new();
        private int _offen = zahl;
        private bool _gescheitert;

        public void Ausfuehren(Action streifen)
        {
            var gut = false;
            try
            {
                streifen();
                gut = true;
            }
#pragma warning disable CA1031 // Ein gescheiterter Streifen fällt auf das Rechnen in einem Stück zurück, statt den Hilfsfaden zu beenden.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
            finally
            {
                lock (_sperre)
                {
                    _gescheitert |= !gut;
                    if (--_offen == 0) Monitor.PulseAll(_sperre);
                }
            }
        }

        /// <summary>Wartet auf alle Streifen (höchstens zehn Sekunden); wahr, wenn alle ohne Fehler fertig sind.</summary>
        public bool Abwarten()
        {
            var bis = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            lock (_sperre)
            {
                while (_offen > 0)
                {
                    var rest = bis - DateTime.UtcNow;
                    if (rest <= TimeSpan.Zero || !Monitor.Wait(_sperre, rest)) return false;
                }
                return !_gescheitert;
            }
        }
    }

    /// <summary>Bis zu <paramref name="zahl"/> Hilfsfäden (angelegt beim ersten Bedarf, Hintergrund, STA, je ein Dispatcher).</summary>
    private static List<Dispatcher> HelferHolen(int zahl)
    {
        lock (Sperre)
        {
            while (Helfer.Count < zahl)
            {
                // Nicht entsorgt: Ein verspäteter Faden setzt es sonst nach dem Entsorgen und beendet das Programm.
                var bereit = new ManualResetEventSlim();
                Dispatcher? d = null;
                var faden = new Thread(() =>
                {
                    d = Dispatcher.CurrentDispatcher;
                    bereit.Set();
                    Dispatcher.Run();
                })
                {
                    IsBackground = true,
                    Name = "Glasdesign Bildraster " + (Helfer.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
                faden.SetApartmentState(ApartmentState.STA);
                faden.Start();
                if (!bereit.Wait(TimeSpan.FromSeconds(5)) || d is null) break;
                Helfer.Add(d);
            }
            return Helfer.Take(zahl).ToList();
        }
    }
}
