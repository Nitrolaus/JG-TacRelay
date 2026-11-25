// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Funk.Client;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Steuerelemente;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Oberfläche im Speicher zeichnen, ohne Fensterhandle und ohne App (C:\Claude\CLAUDE.md, Abschnitt Bildschirm; CODEMAP,
/// Fallen: <c>new App()</c> stellt den Programmstart in die Warteschlange). Die Ressourcen kommen in eine schlichte
/// <see cref="Application"/>, die nichts startet; gibt es schon eine (die App aus OberflaecheFensterTests), bekommt sie
/// frisch geladene Farben, Symbole und Stile. Fenster werden nur angelegt, nie gezeigt: kein Show, kein EnsureHandle, keine
/// HwndSource, kein Audiogerät, kein Eingabegerät. Darum darf ein Bildlauf auch neben einem Spiel laufen; am Ende prüft er
/// selbst, dass kein Fenster einen Handle bekam.
/// </summary>
internal static class Bildlauf
{
    private const string Paket = "pack://application:,,,/TacRelay;component/";

    /// <summary>
    /// Führt <paramref name="aktion"/> auf einem eigenen STA-Thread aus, mit Zuhörer für Bindungsfehler samt Gegenprobe.
    /// Wirft, wenn die Aktion scheitert, WPF einen Bindungsfehler meldet oder ein Fenster einen Handle bekam. Die Farben sind
    /// zu Beginn die der Voreinstellung (Anthrazit), wie im Programm und wie in den Einstellungen des <see cref="Aufbau"/>; sonst
    /// rechnete das Funkfeld seine Deckung für eine andere Leitfarbe, als es zeigt.
    /// </summary>
    public static void Ausfuehren(Action<List<Window>> aktion)
    {
        var bindungsfehler = new List<string>();
        var fenster = new List<Window>();
        Exception? fehler = null;
        var t = new Thread(() =>
        {
            var zuhoerer = new Sammelzuhoerer(bindungsfehler);
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(zuhoerer);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            try
            {
                Gegenprobe(bindungsfehler);
                App.SpracheSetzen();
                App.HilfeEinrichten();
                RessourcenSetzen(Farbvorschlaege.Voreinstellung);
                try
                {
                    aktion(fenster);
                }
                finally
                {
                    RessourcenSetzen(Farbvorschlaege.Voreinstellung);
                }
                Leeren();
                foreach (var w in fenster)
                    Pruefe.Gleich(IntPtr.Zero, new WindowInteropHelper(w).Handle, "kein Fensterhandle, nichts auf dem Bildschirm: " + w.GetType().Name);
                Pruefe.Gleich(0, PresentationSource.CurrentSources.Cast<object>().Count(), "keine PresentationSource (HwndSource) im Bildlauf");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                fehler = e;
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(zuhoerer);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (fehler is PruefungException p) throw p;
        if (fehler is not null) throw new PruefungException("Bildlauf gescheitert: " + fehler);
        lock (bindungsfehler) Pruefe.Gleich(0, bindungsfehler.Count, "Bindungsfehler: " + string.Join(" | ", bindungsfehler.Take(4)));
    }

    /// <summary>
    /// Die Wörterbücher in der Reihenfolge von App.xaml: Farben, Symbole, Farbstellung Anthrazit und Stile von Glasdesign, die
    /// eigenen Stile (OberflaecheGlasTests.Glas_AppUndBildlaufLadenDasselbe hält beide gleich).
    /// </summary>
    internal static readonly string[] Woerterbuecher =
    [
        Paket + "Design/Farben.xaml",
        Paket + "Design/Symbole.xaml",
        "pack://application:,,,/Glasdesign;component/Farbstellungen/Anthrazit.xaml",
        "pack://application:,,,/Glasdesign;component/Glas.Stile.xaml",
        Paket + "Design/Stile.xaml",
    ];

    /// <summary>
    /// Farben, Symbole und Stile frisch in die Ressourcen der Anwendung, mit der Farbstellung dahinter. Frisch geladen, weil
    /// StaticResource einmal auflöst: Schon benutzte Stile behielten sonst die Pinsel der vorigen Stellung. Legt eine
    /// schlichte <see cref="Application"/> an, wenn es keine gibt; nie die App.
    /// </summary>
    public static void RessourcenSetzen(Farbstellung stellung) => RessourcenSetzen(Farbvorschlaege.Von(stellung));

    /// <summary>Wie <see cref="RessourcenSetzen(Farbstellung)"/> mit einem beliebigen Farbsatz (auch einem, der keine Stellung ist).</summary>
    public static void RessourcenSetzen(Farbsatz satz)
    {
        if (App.AlsProgrammGestartet) throw new PruefungException("Der Testprozess gilt als TacRelay.exe.");
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Pruefe.Falsch(Application.Current is App, "schlichte Application, nicht die App");
        }
        var ressourcen = Application.Current!.Resources;
        foreach (var (schluessel, wert) in new (string, object)[]
                 {
                     ("SichtbarWenn", new SichtbarWenn()), ("SichtbarWennNicht", new SichtbarWenn { Umkehren = true }),
                     ("SichtbarWennWert", new SichtbarWennWert()), ("Ressource", new Ressource()),
                 })
        {
            if (!ressourcen.Contains(schluessel)) ressourcen[schluessel] = wert;
        }
        ressourcen.MergedDictionaries.Clear();
        foreach (var quelle in Woerterbuecher) ressourcen.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(quelle) });
        Farbvorschlaege.Anwenden(ressourcen, satz);
    }

    /// <summary>Lässt den Dispatcher alles abarbeiten, was bis zur Leerlaufstufe ansteht (Bindungen, Layout).</summary>
    public static void Leeren() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));

    /// <summary>
    /// Wendet die Vorlage an, misst die Wurzel des Fensters (ohne <paramref name="groesse"/> frei wie SizeToContent), ordnet
    /// sie an und zeichnet sie mit <paramref name="skala"/> (1,75 = Sarevocs 175 %). Das Fenster selbst bleibt ohne Handle.
    /// </summary>
    public static (RenderTargetBitmap Bild, Size Groesse) Zeichnen(Window fenster, double skala = 1, Size? groesse = null)
    {
        if (groesse is { } fest)
        {
            fenster.Width = fest.Width;
            fenster.Height = fest.Height;
        }
        fenster.ApplyTemplate();
        var wurzel = (FrameworkElement)VisualTreeHelper.GetChild(fenster, 0);
        var frei = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var g = groesse ?? default;
        if (groesse is null)
        {
            // Zweimal: Beim ersten Messen entstehen die Kacheln erst, beim zweiten stehen ihre Bindungen.
            wurzel.Measure(frei);
            Leeren();
            wurzel.Measure(frei);
            g = wurzel.DesiredSize;
        }
        wurzel.Measure(g);
        wurzel.Arrange(new Rect(g));
        wurzel.UpdateLayout();
        Leeren();
        wurzel.UpdateLayout();
        var bild = new RenderTargetBitmap((int)Math.Ceiling(g.Width * skala), (int)Math.Ceiling(g.Height * skala), 96 * skala, 96 * skala, PixelFormats.Pbgra32);
        bild.Render(wurzel);
        bild.Freeze();
        return (bild, new Size(wurzel.ActualWidth, wurzel.ActualHeight));
    }

    /// <summary>Die Wurzel des Fensters (Rahmen aus der Fenstervorlage) für Messungen im Baum.</summary>
    public static FrameworkElement Wurzel(Window fenster) => (FrameworkElement)VisualTreeHelper.GetChild(fenster, 0);

    /// <summary>Alle Nachfahren eines Typs im sichtbaren Baum.</summary>
    public static IEnumerable<T> Nachfahren<T>(DependencyObject wurzel) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(wurzel); i++)
        {
            var kind = VisualTreeHelper.GetChild(wurzel, i);
            if (kind is T t) yield return t;
            foreach (var n in Nachfahren<T>(kind)) yield return n;
        }
    }

    /// <summary>Pixel an (<paramref name="x"/>, <paramref name="y"/>) in Einheiten, als Farbe mit Alpha (vormultipliziert zurückgerechnet).</summary>
    public static Color Pixel(BitmapSource bild, double x, double y, double skala = 1)
    {
        var p = new byte[4];
        bild.CopyPixels(new Int32Rect((int)(x * skala), (int)(y * skala), 1, 1), p, 4, 0);
        var a = p[3];
        byte Z(byte v) => a == 0 ? (byte)0 : (byte)Math.Min(255, Math.Round(v * 255.0 / a));
        return Color.FromArgb(a, Z(p[2]), Z(p[1]), Z(p[0]));
    }

    private static void Gegenprobe(List<string> bindungsfehler)
    {
        // Eine absichtlich kaputte Bindung muss beim Zuhörer ankommen, sonst beweist "keine Fehler" nichts.
        var probe = new System.Windows.Controls.TextBlock();
        probe.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("GibtEsNicht"));
        probe.DataContext = new object();
        Leeren();
        lock (bindungsfehler)
        {
            if (bindungsfehler.Count == 0) throw new PruefungException("Gegenprobe: der Zuhörer meldet keine Bindungsfehler");
            bindungsfehler.Clear();
        }
    }

    private sealed class Sammelzuhoerer(List<string> ziel) : TraceListener
    {
        private string _zeile = "";

        public override void Write(string? message) => _zeile += message;

        public override void WriteLine(string? message)
        {
            lock (ziel) ziel.Add(_zeile + message);
            _zeile = "";
        }
    }
}

/// <summary>
/// Der Ablageort der Messdateien der Oberflächentests (Werte zum Nachlesen: Kontrast, Renderzeit, Maße der Startseite). Bis Schritt 3.5
/// stand er fest auf <c>C:\Claude\tmp\tr-ui2</c> in vier Tests (Gegenprüfung Stufe 2, N8); jetzt entscheidet ein Ort: mit
/// <c>FUNK_MESSDATEIEN=&lt;Ordner unter C:\Claude&gt;</c> dorthin, sonst in den Testordner (<see cref="Testserver.Wurzel"/>, wird mit ihm aufgeräumt).
/// </summary>
internal static class Messdateien
{
    public static string Ordner()
    {
        var eigen = Environment.GetEnvironmentVariable("FUNK_MESSDATEIEN");
        if (string.IsNullOrWhiteSpace(eigen)) return Path.Combine(Testserver.Wurzel(), "messdateien");
        var ziel = Path.GetFullPath(eigen.Trim());
        if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_MESSDATEIEN muss unter C:\Claude liegen: " + ziel);
        return ziel;
    }

    public static void Schreiben(string name, IEnumerable<string> zeilen)
    {
        var ordner = Ordner();
        Directory.CreateDirectory(ordner);
        File.WriteAllLines(Path.Combine(ordner, name), zeilen);
    }
}
/// <summary>
/// Bilder der Oberfläche für Sarevoc, ohne Programmstart: Mit <c>FUNK_BILDER=&lt;Ordner unter C:\Claude&gt;</c> legt ein Test jede
/// benannte Ansicht als PNG ab; ohne die Variable entsteht keine Datei.
/// </summary>
internal static class Bildablage
{
    public static string? Ordner()
    {
        var ordner = Environment.GetEnvironmentVariable("FUNK_BILDER");
        if (string.IsNullOrWhiteSpace(ordner)) return null;
        var ziel = Path.GetFullPath(ordner.Trim());
        if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_BILDER muss unter C:\Claude liegen: " + ziel);
        return ziel;
    }

    public static void Ablegen(BitmapSource bild, string name)
    {
        if (Ordner() is not { } ziel) return;
        Directory.CreateDirectory(ziel);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bild));
        using var datei = File.Create(Path.Combine(ziel, name + ".png"));
        png.Save(datei);
    }
}

/// <summary>
/// Ein Bogen zum Vergleichen: Bilder, Rahmen und Beschriftungen auf einer Fläche, in Einheiten gesetzt und mit einer Skala
/// gezeichnet. Schrift nur Segoe UI (Systemschrift). Beschriftungen für Sarevoc: als Striche nur "-" und "_", gerade
/// Anführungszeichen, nicht gegendert.
/// </summary>
internal sealed class Bogen
{
    private static readonly Typeface Schrift = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface SchriftFett = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly DrawingVisual _bild = new();
    private readonly DrawingContext _dc;
    private readonly double _skala;

    public Bogen(double breite, double hoehe, double skala, Color grund)
    {
        Breite = breite;
        Hoehe = hoehe;
        _skala = skala;
        _dc = _bild.RenderOpen();
        _dc.DrawRectangle(new SolidColorBrush(grund), null, new Rect(0, 0, breite, hoehe));
    }

    public double Breite { get; }

    public double Hoehe { get; }

    public static SolidColorBrush Pinsel(Color c)
    {
        var p = new SolidColorBrush(c);
        p.Freeze();
        return p;
    }

    /// <summary>Text mit Anker oben links; liefert die Breite.</summary>
    public double Text(string text, double x, double y, double groesse = 13, Color? farbe = null, bool fett = false)
    {
        Pruefe.Falsch(text.Any(z => z is '\u2013' or '\u2014' or '\u2212' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), "nur einfache Zeichen in Beschriftungen: " + text);
        var ft = new FormattedText(text, CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight, fett ? SchriftFett : Schrift, groesse,
            Pinsel(farbe ?? Colors.White), _skala);
        _dc.DrawText(ft, new Point(x, y));
        return ft.Width;
    }

    public void Bild(BitmapSource bild, double x, double y, double breite, double hoehe, double deckkraft = 1)
    {
        if (deckkraft < 1) _dc.PushOpacity(deckkraft);
        _dc.DrawImage(bild, new Rect(x, y, breite, hoehe));
        if (deckkraft < 1) _dc.Pop();
    }

    public void Rahmen(Rect r, Color farbe, double staerke = 1, bool gestrichelt = false)
    {
        var stift = new Pen(Pinsel(farbe), staerke);
        if (gestrichelt) stift.DashStyle = new DashStyle([6, 4], 0);
        stift.Freeze();
        _dc.DrawRectangle(null, stift, r);
    }

    public void Zeichnung(Action<DrawingContext> zeichnen)
    {
        ArgumentNullException.ThrowIfNull(zeichnen);
        zeichnen(_dc);
    }

    public RenderTargetBitmap Fertig()
    {
        _dc.Close();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(Breite * _skala), (int)Math.Ceiling(Hoehe * _skala), 96 * _skala, 96 * _skala, PixelFormats.Pbgra32);
        bmp.Render(_bild);
        bmp.Freeze();
        return bmp;
    }
}

/// <summary>
/// Selbst gezeichneter, bunter Hintergrund, der für ein Spielbild steht (keine fremde Grafik, kein Spiel-Screenshot):
/// heller Himmel mit Verlauf, eine grelle Sonne, dunkle Formen und etwas heller Text wie ein Spiel-HUD. So zeigt ein Bild,
/// wie das Funkfeld über hellen und dunklen Stellen wirkt.
/// </summary>
internal static class Spielbild
{
    public static void Zeichnen(DrawingContext dc, Rect r)
    {
        var himmel = new LinearGradientBrush(
            [new GradientStop(Color.FromRgb(0x7F, 0xB2, 0xEB), 0), new GradientStop(Color.FromRgb(0xF4, 0xD0, 0x8E), 0.55), new GradientStop(Color.FromRgb(0x3A, 0x28, 0x52), 1)],
            new Point(0, 0), new Point(0, 1));
        himmel.Freeze();
        dc.PushClip(new RectangleGeometry(r));
        dc.DrawRectangle(himmel, null, r);
        var sonne = new RadialGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xF2), Color.FromArgb(0x00, 0xFF, 0xE0, 0x90)) { GradientOrigin = new Point(0.5, 0.5) };
        sonne.Freeze();
        dc.DrawEllipse(sonne, null, new Point(r.Left + (r.Width * 0.72), r.Top + (r.Height * 0.32)), r.Width * 0.22, r.Width * 0.22);
        dc.DrawEllipse(Bogen.Pinsel(Colors.White), null, new Point(r.Left + (r.Width * 0.72), r.Top + (r.Height * 0.32)), r.Width * 0.06, r.Width * 0.06);
        // Dunkle Formen: ein Bergrücken und ein Schiffsumriss
        var berg = new StreamGeometry();
        using (var g = berg.Open())
        {
            g.BeginFigure(new Point(r.Left, r.Bottom), true, true);
            g.LineTo(new Point(r.Left, r.Top + (r.Height * 0.78)), true, false);
            g.LineTo(new Point(r.Left + (r.Width * 0.18), r.Top + (r.Height * 0.62)), true, false);
            g.LineTo(new Point(r.Left + (r.Width * 0.33), r.Top + (r.Height * 0.74)), true, false);
            g.LineTo(new Point(r.Left + (r.Width * 0.52), r.Top + (r.Height * 0.58)), true, false);
            g.LineTo(new Point(r.Left + (r.Width * 0.7), r.Top + (r.Height * 0.8)), true, false);
            g.LineTo(new Point(r.Right, r.Top + (r.Height * 0.66)), true, false);
            g.LineTo(new Point(r.Right, r.Bottom), true, false);
        }
        berg.Freeze();
        dc.DrawGeometry(Bogen.Pinsel(Color.FromRgb(0x1C, 0x14, 0x2A)), null, berg);
        var schiff = new StreamGeometry();
        using (var g = schiff.Open())
        {
            var x = r.Left + (r.Width * 0.3);
            var y = r.Top + (r.Height * 0.28);
            g.BeginFigure(new Point(x, y), true, true);
            g.LineTo(new Point(x + 90, y - 14), true, false);
            g.LineTo(new Point(x + 160, y - 4), true, false);
            g.LineTo(new Point(x + 90, y + 10), true, false);
        }
        schiff.Freeze();
        dc.DrawGeometry(Bogen.Pinsel(Color.FromRgb(0x2A, 0x2E, 0x3A)), null, schiff);
        // Helles HUD: Linien und Text
        var hud = new Pen(Bogen.Pinsel(Color.FromArgb(0xC0, 0xE8, 0xF4, 0xFF)), 1.2);
        hud.Freeze();
        dc.DrawLine(hud, new Point(r.Left + 20, r.Bottom - 38), new Point(r.Right - 20, r.Bottom - 38));
        dc.DrawEllipse(null, hud, new Point(r.Left + (r.Width * 0.5), r.Top + (r.Height * 0.5)), 26, 26);
        var ft = new FormattedText("SCM 212 m/s   QT BEREIT   SCHILD 100 %   ZIEL 4,2 km", CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 15, Bogen.Pinsel(Color.FromRgb(0xEE, 0xF6, 0xFF)), 1);
        dc.DrawText(ft, new Point(r.Left + 24, r.Bottom - 32));
        dc.Pop();
    }
}
