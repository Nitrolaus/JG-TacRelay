// Glasdesign - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Glasdesign;

/// <summary>
/// Der Fensterrahmen im Glaslook (0.5.0): Titelleiste mit Symbol und Titel, drei Fensterknöpfe aus Glas (Minimieren,
/// Maximieren oder Wiederherstellen, Schließen), eine Haarlinie von einem Gerätepixel um das Fenster, aktiv und inaktiv
/// unterscheidbar. Machart über <see cref="Glas.RahmenartProperty"/> (<see cref="Rahmenart"/>). Er ist der Inhalt der
/// Vorlage des Fensterstils Glas.Fenster (WindowChrome), lässt sich aber auch allein zeichnen (Stil Glas.Fensterrahmen):
/// Ohne Fenster tun die Knöpfe nichts, so messen die Tests im Speicher.
/// <para>
/// Als Vorlage eines Fensters (<see cref="Fenster"/>) richtet er ein, was WPF nicht von selbst tut: Die Knöpfe rufen
/// SystemCommands auf; maximiert rückt er den Inhalt um das ein, was Windows über den Rand des Bildschirms schiebt
/// (<see cref="Ueberstand"/>, gemessen am Fenster gegen den Arbeitsbereich seines Bildschirms); die Titelzeile des
/// WindowChrome endet genau an der Unterkante der Titelleiste (<see cref="Titelzeile"/>); und er bittet den Fenstermanager
/// von Windows 11 um eckige Ecken ohne eigenen Rand, damit die Haarlinie die einzige Linie um das Fenster ist. Diese Teile
/// sind am Bildschirm ungeprüft.
/// </para>
/// </summary>
public sealed class Fensterrahmen : ContentControl
{
    /// <summary>Höhe der Titelleiste in beiden eingebauten Farbstellungen (Glas.Mass.Fenster.Titelhoehe), in DIP.</summary>
    public const double Titelhoehe = 36;

    /// <summary>
    /// Die Titelzeile, mit der der Stil Glas.Fenster sein WindowChrome vorbelegt: so hoch, dass sie mit dem oberen Größenrand
    /// des Systems (SystemParameters.WindowResizeBorderThickness.Top) genau an der Unterkante der Titelleiste
    /// (<see cref="Titelhoehe"/>) endet. Sobald das Fenster steht, gleicht der Rahmen sie an die wirkliche Leiste an.
    /// </summary>
    public static double TitelzeileVorgabe => Titelzeile(0, Titelhoehe, SystemParameters.WindowResizeBorderThickness.Top);

    /// <summary>
    /// Höhe der Titelzeile für WindowChrome.CaptionHeight. WPF zählt die Titelzeile erst unterhalb des oberen Größenrands
    /// (ResizeBorderThickness.Top): Sie reicht von dort bis Rand plus CaptionHeight. Damit sie genau an der Unterkante der
    /// Leiste endet (Überstand oben plus Leistenhöhe), ist sie um den Rand kürzer; mindestens 0. Gemessen über
    /// WindowChromeWorker._HitTestNca (Prüfung zu 0.5.0-beta.1): mit CaptionHeight gleich der Leistenhöhe gehörte ein
    /// Streifen in Randbreite unter der Trennlinie noch zur Titelzeile, Klicks dort zogen das Fenster.
    /// <para>
    /// Auf 0,01 DIP abgerundet, nie aufgerundet (Prüfung zu 0.5.0-beta.2): Aufgerundet ragte die Titelzeile bei manchen
    /// Skalen und Randbreiten um einen Bruchteil über die Leiste, und die erste Pixelzeile unter der Trennlinie zog noch das
    /// Fenster (7 von 80 Fällen, etwa 150 Prozent mit Rand 5 Pixel). Abgerundet endet sie höchstens 0,01 DIP vor der
    /// Unterkante; die letzte Pixelzeile der Leiste bleibt Titelzeile, die erste darunter ist Inhalt.
    /// </para>
    /// </summary>
    internal static double Titelzeile(double ueberstandOben, double leiste, double randOben) =>
        Math.Max(0, Math.Floor((ueberstandOben + leiste - randOben) * 100) / 100);

    /// <summary>
    /// Führt den Befehl eines Fensterknopfs aus (Vorgabe: SystemCommands). Ohne HWND tun die SystemCommands nichts; die
    /// Tests setzen hier einen Rekorder ein, um die Verdrahtung der Knöpfe an einem nie gezeigten Fenster zu prüfen.
    /// </summary>
    internal static Action<Window, Fensterbefehl> Ausfuehren { get; set; } = Systembefehl;

    private static void Systembefehl(Window w, Fensterbefehl befehl)
    {
        switch (befehl)
        {
            case Fensterbefehl.Minimieren:
                SystemCommands.MinimizeWindow(w);
                break;
            case Fensterbefehl.Maximieren:
                SystemCommands.MaximizeWindow(w);
                break;
            case Fensterbefehl.Wiederherstellen:
                SystemCommands.RestoreWindow(w);
                break;
            default:
                SystemCommands.CloseWindow(w);
                break;
        }
    }

    public static readonly DependencyProperty TitelProperty = DependencyProperty.Register(
        nameof(Titel), typeof(string), typeof(Fensterrahmen), new FrameworkPropertyMetadata(""));

    /// <summary>Das Fenstersymbol (Window.Icon); ohne Symbol rückt der Titel nach links.</summary>
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(ImageSource), typeof(Fensterrahmen), new FrameworkPropertyMetadata(null));

    /// <summary>Das Fenster ist aktiv (Window.IsActive): Haarlinie, Titel und Glasleiste kräftiger. Allein gezeichnet: aktiv.</summary>
    public static readonly DependencyProperty AktivProperty = DependencyProperty.Register(
        nameof(Aktiv), typeof(bool), typeof(Fensterrahmen), new FrameworkPropertyMetadata(true));

    /// <summary>Maximiert: ohne Haarlinie, das Zeichen des mittleren Knopfs wird Wiederherstellen, der Inhalt rückt um den <see cref="Ueberstand"/> ein.</summary>
    public static readonly DependencyProperty MaximiertProperty = DependencyProperty.Register(
        nameof(Maximiert), typeof(bool), typeof(Fensterrahmen), new FrameworkPropertyMetadata(false, (d, _) => ((Fensterrahmen)d).Spaeter()));

    /// <summary>Wie bei Window.ResizeMode: NoResize zeigt nur Schließen, CanMinimize Minimieren und Schließen.</summary>
    public static readonly DependencyProperty GroessenmodusProperty = DependencyProperty.Register(
        nameof(Groessenmodus), typeof(ResizeMode), typeof(Fensterrahmen), new FrameworkPropertyMetadata(ResizeMode.CanResize));

    /// <summary>Einzug aller Teile, solange das Fenster maximiert über den Bildschirmrand ragt (DIP); sonst 0.</summary>
    public static readonly DependencyProperty UeberstandProperty = DependencyProperty.Register(
        nameof(Ueberstand), typeof(Thickness), typeof(Fensterrahmen), new FrameworkPropertyMetadata(default(Thickness)));

    private ButtonBase? _minimieren, _maximieren, _schliessen;
    private FrameworkElement? _leiste;
    private bool _eingerichtet;

    static Fensterrahmen()
    {
        FocusableProperty.OverrideMetadata(typeof(Fensterrahmen), new FrameworkPropertyMetadata(false));
    }

    public Fensterrahmen() => Loaded += (_, _) => Einrichten();

    public string Titel { get => (string)GetValue(TitelProperty); set => SetValue(TitelProperty, value); }
    public ImageSource? Symbol { get => (ImageSource?)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public bool Aktiv { get => (bool)GetValue(AktivProperty); set => SetValue(AktivProperty, value); }
    public bool Maximiert { get => (bool)GetValue(MaximiertProperty); set => SetValue(MaximiertProperty, value); }
    public ResizeMode Groessenmodus { get => (ResizeMode)GetValue(GroessenmodusProperty); set => SetValue(GroessenmodusProperty, value); }
    public Thickness Ueberstand { get => (Thickness)GetValue(UeberstandProperty); set => SetValue(UeberstandProperty, value); }

    /// <summary>Das Fenster, dessen Vorlage dieser Rahmen ist; null, wenn er allein steht (dann tun die Knöpfe nichts).</summary>
    public Window? Fenster => TemplatedParent as Window;

    public override void OnApplyTemplate()
    {
        if (_minimieren is not null) _minimieren.Click -= Minimieren;
        if (_maximieren is not null) _maximieren.Click -= Maximieren;
        if (_schliessen is not null) _schliessen.Click -= Schliessen;
        if (_leiste is not null) _leiste.SizeChanged -= LeisteGeaendert;
        base.OnApplyTemplate();
        _minimieren = GetTemplateChild("PART_Minimieren") as ButtonBase;
        _maximieren = GetTemplateChild("PART_Maximieren") as ButtonBase;
        _schliessen = GetTemplateChild("PART_Schliessen") as ButtonBase;
        _leiste = GetTemplateChild("PART_Leiste") as FrameworkElement;
        if (_minimieren is not null) _minimieren.Click += Minimieren;
        if (_maximieren is not null) _maximieren.Click += Maximieren;
        if (_schliessen is not null) _schliessen.Click += Schliessen;
        if (_leiste is not null) _leiste.SizeChanged += LeisteGeaendert;
    }

    private void Minimieren(object sender, RoutedEventArgs e)
    {
        if (Fenster is { } w) Ausfuehren(w, Fensterbefehl.Minimieren);
    }

    private void Maximieren(object sender, RoutedEventArgs e)
    {
        if (Fenster is not { } w) return;
        Ausfuehren(w, w.WindowState == WindowState.Maximized ? Fensterbefehl.Wiederherstellen : Fensterbefehl.Maximieren);
    }

    private void Schliessen(object sender, RoutedEventArgs e)
    {
        if (Fenster is { } w) Ausfuehren(w, Fensterbefehl.Schliessen);
    }

    private void LeisteGeaendert(object sender, SizeChangedEventArgs e) => Anpassen();

    private void Einrichten()
    {
        if (Fenster is not { } w) return;
        if (!_eingerichtet)
        {
            _eingerichtet = true;
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero) Fensterlage.Rahmenstil(hwnd);
            else w.SourceInitialized += (_, _) => Fensterlage.Rahmenstil(new WindowInteropHelper(w).Handle);
        }
        Anpassen();
    }

    /// <summary>Nach dem Maximieren steht die neue Lage des Fensters erst fest, wenn Windows es verschoben hat.</summary>
    private void Spaeter()
    {
        if (Fenster is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Anpassen));
    }

    /// <summary>
    /// Überstand (maximiert) messen und die Titelzeile des WindowChrome so setzen, dass sie an der Unterkante der Titelleiste
    /// endet (<see cref="Titelzeile"/>). Ohne HWND (nie gezeigtes Fenster) ohne Überstand.
    /// </summary>
    private void Anpassen()
    {
        if (Fenster is not { } w) return;
        var hwnd = new WindowInteropHelper(w).Handle;
        var ueber = Maximiert && hwnd != IntPtr.Zero ? Fensterlage.Ueberstand(hwnd, Glasgeometrie.Skala(this)) : default;
        if (Ueberstand != ueber) SetCurrentValue(UeberstandProperty, ueber);
        if (_leiste is not { ActualHeight: > 0 } leiste || WindowChrome.GetWindowChrome(w) is not { } chrome) return;
        var kopf = Titelzeile(ueber.Top, leiste.ActualHeight, chrome.ResizeBorderThickness.Top);
        if (Math.Abs(chrome.CaptionHeight - kopf) < 0.001) return;
        var neu = (WindowChrome)chrome.Clone();
        neu.CaptionHeight = kopf;
        WindowChrome.SetWindowChrome(w, neu);
    }
}

/// <summary>Was ein Fensterknopf auslöst (<see cref="Fensterrahmen.Ausfuehren"/>).</summary>
internal enum Fensterbefehl
{
    Minimieren,
    Maximieren,
    Wiederherstellen,
    Schliessen,
}

/// <summary>
/// Die Fläche des Fensterrahmens unter dem Inhalt: der Grund, die Titelleiste je <see cref="Rahmenart"/> und die
/// Trennlinie unter ihr, eine Pixelreihe. Glasleiste: ein gewölbter Verlauf (Glas.Fenster.Leiste) mit dem Kantenspiegel
/// in der ersten Reihe unter der Haarlinie und einem Schein oben, Stärken aus dem Glanzprofil; inaktiv flach
/// (Glas.Fenster.Leiste.Inaktiv) ohne Spiegelung. Schlicht: nur der Grund und die Trennlinie.
/// </summary>
public sealed class Fensterflaeche : FrameworkElement
{
    public static readonly DependencyProperty GrundProperty = Pinsel(nameof(Grund));
    public static readonly DependencyProperty LeisteProperty = Pinsel(nameof(Leiste));
    public static readonly DependencyProperty LeisteInaktivProperty = Pinsel(nameof(LeisteInaktiv));
    public static readonly DependencyProperty TrennlinieProperty = Pinsel(nameof(Trennlinie));

    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Rahmenart), typeof(Fensterflaeche), new FrameworkPropertyMetadata(Rahmenart.Glasleiste, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AktivProperty = DependencyProperty.Register(
        nameof(Aktiv), typeof(bool), typeof(Fensterflaeche), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TitelhoeheProperty = DependencyProperty.Register(
        nameof(Titelhoehe), typeof(double), typeof(Fensterflaeche), new FrameworkPropertyMetadata(36d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpiegelFarbeProperty = DependencyProperty.Register(
        nameof(SpiegelFarbe), typeof(Color), typeof(Fensterflaeche), new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Fensterflaeche),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Fensterflaeche), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Fensterflaeche()
    {
        IsHitTestVisible = false;
        SetResourceReference(LeisteProperty, Schluessel.FensterLeiste);
        SetResourceReference(LeisteInaktivProperty, Schluessel.FensterLeisteInaktiv);
        SetResourceReference(TrennlinieProperty, Schluessel.FensterTrennlinie);
        SetResourceReference(TitelhoeheProperty, Schluessel.MassFensterTitelhoehe);
        SetResourceReference(SpiegelFarbeProperty, Schluessel.SpiegelFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
    }

    public Brush? Grund { get => (Brush?)GetValue(GrundProperty); set => SetValue(GrundProperty, value); }
    public Brush? Leiste { get => (Brush?)GetValue(LeisteProperty); set => SetValue(LeisteProperty, value); }
    public Brush? LeisteInaktiv { get => (Brush?)GetValue(LeisteInaktivProperty); set => SetValue(LeisteInaktivProperty, value); }
    public Brush? Trennlinie { get => (Brush?)GetValue(TrennlinieProperty); set => SetValue(TrennlinieProperty, value); }
    public Rahmenart Art { get => (Rahmenart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public bool Aktiv { get => (bool)GetValue(AktivProperty); set => SetValue(AktivProperty, value); }
    public double Titelhoehe { get => (double)GetValue(TitelhoeheProperty); set => SetValue(TitelhoeheProperty, value); }
    public Color SpiegelFarbe { get => (Color)GetValue(SpiegelFarbeProperty); set => SetValue(SpiegelFarbeProperty, value); }

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 1 || h < 1) return;
        dc.DrawRectangle(Grund, null, new Rect(0, 0, b / s, h / s));
        var t = Math.Min(h, Glasgeometrie.Raster(Titelhoehe, s));
        if (t < 3) return;
        if (Art == Rahmenart.Glasleiste)
        {
            dc.DrawRectangle(Aktiv ? Leiste : LeisteInaktiv, null, new Rect(0, 0, b / s, t / s));
            if (Aktiv)
            {
                var p = Glanzprofil;
                var ton = SpiegelFarbe;
                // Schein über der oberen Hälfte und der Kantenspiegel in der ersten Reihe unter der Haarlinie, zur Mitte hin am
                // hellsten, wie auf dem gewölbten Glas der Knöpfe.
                var oben = Math.Max(1, Math.Round(t * 0.5));
                dc.DrawRectangle(Pinselwerk.Senkrecht((Pinselwerk.Alpha(ton, p.Schein * 0.18), 0), (Pinselwerk.Alpha(ton, 0), 1)), null, new Rect(0, 0, b / s, oben / s));
                var spiegel = Pinselwerk.Alpha(ton, p.Kantenspiegel * 0.35);
                dc.DrawRectangle(Pinselwerk.Waagerecht((Pinselwerk.Alpha(ton, 0), 0), (spiegel, 0.2), (spiegel, 0.8), (Pinselwerk.Alpha(ton, 0), 1)), null, new Rect(0, 1 / s, b / s, 1 / s));
            }
        }
        dc.DrawRectangle(Trennlinie, null, new Rect(0, (t - 1) / s, b / s, 1 / s));
    }
}

/// <summary>
/// Die Haarlinie um das ganze Fenster, genau ein Gerätepixel, über dem Inhalt gezeichnet (nicht treffbar): aktiv
/// Glas.Fenster.Kante (oben hell, Seiten schwach, unten mittel wie die Haarlinie der Knöpfe), inaktiv
/// Glas.Fenster.Kante.Inaktiv. Maximiert blendet der Fensterrahmen sie aus.
/// </summary>
public sealed class Fensterkante : FrameworkElement
{
    public static readonly DependencyProperty AktivProperty = DependencyProperty.Register(
        nameof(Aktiv), typeof(bool), typeof(Fensterkante), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KanteProperty = DependencyProperty.Register(
        nameof(Kante), typeof(Brush), typeof(Fensterkante), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KanteInaktivProperty = DependencyProperty.Register(
        nameof(KanteInaktiv), typeof(Brush), typeof(Fensterkante), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Fensterkante()
    {
        IsHitTestVisible = false;
        SetResourceReference(KanteProperty, Schluessel.FensterKante);
        SetResourceReference(KanteInaktivProperty, Schluessel.FensterKanteInaktiv);
    }

    public bool Aktiv { get => (bool)GetValue(AktivProperty); set => SetValue(AktivProperty, value); }
    public Brush? Kante { get => (Brush?)GetValue(KanteProperty); set => SetValue(KanteProperty, value); }
    public Brush? KanteInaktiv { get => (Brush?)GetValue(KanteInaktivProperty); set => SetValue(KanteInaktivProperty, value); }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 3 || h < 3) return;
        var f = new Glasflaeche(0, 0, b, h, 0, s);
        drawingContext.DrawGeometry(Aktiv ? Kante : KanteInaktiv, null, f.Ring(0, Glasgeometrie.Haarlinie));
    }
}

/// <summary>
/// Das Fenstersymbol in der Titelleiste: ein Bild (Window.Icon), quadratisch in <see cref="Groesse"/>, senkrecht absolut
/// mittig in der Höhe des Elements (Kantenlänge mit der Parität der Höhe, auf ganzen Gerätepixeln).
/// </summary>
public sealed class Fenstersymbol : FrameworkElement
{
    public static readonly DependencyProperty QuelleProperty = DependencyProperty.Register(
        nameof(Quelle), typeof(ImageSource), typeof(Fenstersymbol),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GroesseProperty = DependencyProperty.Register(
        nameof(Groesse), typeof(double), typeof(Fenstersymbol),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public Fenstersymbol() => IsHitTestVisible = false;

    public ImageSource? Quelle { get => (ImageSource?)GetValue(QuelleProperty); set => SetValue(QuelleProperty, value); }
    public double Groesse { get => (double)GetValue(GroesseProperty); set => SetValue(GroesseProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => Quelle is null ? default : new Size(Groesse, Math.Min(Groesse, availableSize.Height));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Quelle is not { } bild) return;
        var s = Glasgeometrie.Skala(this);
        var feld = Pixelmitte.Feld(Glasgeometrie.Raster(RenderSize.Width, s), Glasgeometrie.Raster(RenderSize.Height, s), Groesse, s);
        if (feld.Width < 1) return;
        drawingContext.DrawImage(bild, new Rect(feld.X / s, feld.Y / s, feld.Width / s, feld.Height / s));
    }
}

/// <summary>
/// Setzt sein Kind als Quadrat der Kantenlänge <see cref="Groesse"/> absolut mittig auf ganze Gerätepixel: Die Kantenlänge
/// bekommt die Parität der Höhe, also ist der Rest oben gleich unten; ist der Platz quadratisch, auch links gleich rechts.
/// So sitzen die runden Glasknöpfe der Titelleiste mittig in ihrer Trefferfläche, bei jeder Skalierung.
/// </summary>
public sealed class Pixelmitte : Decorator
{
    public static readonly DependencyProperty GroesseProperty = DependencyProperty.Register(
        nameof(Groesse), typeof(double), typeof(Pixelmitte),
        new FrameworkPropertyMetadata(22d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Groesse { get => (double)GetValue(GroesseProperty); set => SetValue(GroesseProperty, value); }

    /// <summary>Das Feld des Kinds in Pixeln für einen Platz von <paramref name="breite"/> mal <paramref name="hoehe"/> Pixeln.</summary>
    internal static Rect Feld(double breite, double hoehe, double groesse, double skala)
    {
        var g = Math.Min(Math.Min(breite, hoehe), Glasgeometrie.Raster(groesse, skala));
        if ((hoehe - g) % 2 != 0) g -= 1;
        g = Math.Max(0, g);
        return new Rect(Math.Floor((breite - g) / 2), (hoehe - g) / 2, g, g);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var g = Math.Min(Groesse, Math.Min(constraint.Width, constraint.Height));
        Child?.Measure(new Size(g, g));
        return new Size(g, g);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var s = Glasgeometrie.Skala(this);
        var feld = Feld(Glasgeometrie.Raster(arrangeSize.Width, s), Glasgeometrie.Raster(arrangeSize.Height, s), Groesse, s);
        Child?.Arrange(new Rect(feld.X / s, feld.Y / s, feld.Width / s, feld.Height / s));
        return arrangeSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateArrange();
}

/// <summary>Was der Fensterrahmen bei Windows erfragt und einstellt (nur, wenn er die Vorlage eines Fensters ist).</summary>
internal static partial class Fensterlage
{
    private const int DwmwaDunkel = 20, DwmwaEcken = 33, DwmwaRandfarbe = 34;
    private const int EckenNichtRunden = 1;
    private const uint RandfarbeKeine = 0xFFFFFFFE;
    private const uint MonitorNaechster = 2;

    /// <summary>
    /// Windows 11: dunkle Systemteile, eckige Ecken und kein Rand des Fenstermanagers, damit die Haarlinie des Rahmens die
    /// einzige Linie um das Fenster ist. Auf älterem Windows nichts. Am Bildschirm ungeprüft.
    /// </summary>
    public static void Rahmenstil(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var dunkel = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaDunkel, ref dunkel, sizeof(int));
        var ecken = EckenNichtRunden;
        _ = DwmSetWindowAttribute(hwnd, DwmwaEcken, ref ecken, sizeof(int));
        var rand = unchecked((int)RandfarbeKeine);
        _ = DwmSetWindowAttribute(hwnd, DwmwaRandfarbe, ref rand, sizeof(int));
    }

    /// <summary>
    /// Wie weit ein maximiertes Fenster über den Arbeitsbereich seines Bildschirms hinausragt, je Seite in DIP: Windows legt
    /// es um den Rahmen größer, den WindowChrome ausblendet. Geht die Messung nicht, gilt SystemParameters.WindowResizeBorderThickness.
    /// </summary>
    public static Thickness Ueberstand(IntPtr hwnd, double skala)
    {
        var info = new Monitorinfo { Groesse = Marshal.SizeOf<Monitorinfo>() };
        if (!GetWindowRect(hwnd, out var r)) return SystemParameters.WindowResizeBorderThickness;
        var monitor = MonitorFromWindow(hwnd, MonitorNaechster);
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WindowResizeBorderThickness;
        var a = info.Arbeit;
        double Dip(int px) => Math.Max(0, px) / skala;
        return new Thickness(Dip(a.Links - r.Links), Dip(a.Oben - r.Oben), Dip(r.Rechts - a.Rechts), Dip(r.Unten - a.Unten));
    }

#pragma warning disable CS0649 // Die Felder füllt Windows über die Aufrufe unten.
    [StructLayout(LayoutKind.Sequential)]
    private struct Rechteck
    {
        public int Links;
        public int Oben;
        public int Rechts;
        public int Unten;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Monitorinfo
    {
        public int Groesse;
        public Rechteck Bildschirm;
        public Rechteck Arbeit;
        public uint Merkmale;
    }
#pragma warning restore CS0649

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribut, ref int wert, int groesse);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out Rechteck rechteck);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint merkmale);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref Monitorinfo info);
}
