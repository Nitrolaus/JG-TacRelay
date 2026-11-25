// Glasdesign - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Glasdesign;

/// <summary>
/// Der Grund einer Milchglasplatte (Stilfamilie 4, Stil Glas.Milchglas): zeigt ein Element dahinter (<see cref="Quelle"/>,
/// etwa der Hintergrund des Fensters) unscharf, auf die eigene Fläche mit Eckenradius geschnitten. Die Unschärfe ist ein
/// <see cref="BlurEffect"/> auf einem inneren Element, das um den Radius größer gezeichnet wird, damit der Rand nicht
/// ausfranst; der Schnitt liegt auf diesem Grund selbst. Die Quelle muss hinter der Platte liegen und darf sie nicht
/// enthalten (ein Geschwister, kein Elternteil), sonst sähe die Platte sich selbst.
/// </summary>
public sealed class Milchglasgrund : FrameworkElement
{
    public static readonly DependencyProperty QuelleProperty = DependencyProperty.Register(
        nameof(Quelle), typeof(Visual), typeof(Milchglasgrund), new FrameworkPropertyMetadata(null, (d, _) => ((Milchglasgrund)d)._unschaerfe.InvalidateVisual()));

    public static readonly DependencyProperty UnschaerfeProperty = DependencyProperty.Register(
        nameof(Unschaerfe), typeof(double), typeof(Milchglasgrund), new FrameworkPropertyMetadata(26.0, FrameworkPropertyMetadataOptions.AffectsArrange, (d, _) => ((Milchglasgrund)d).EffektSetzen()));

    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(double), typeof(Milchglasgrund), new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    private readonly Unscharf _unschaerfe;

    public Milchglasgrund()
    {
        IsHitTestVisible = false;
        _unschaerfe = new Unscharf(this);
        AddVisualChild(_unschaerfe);
        EffektSetzen();
        LayoutUpdated += (_, _) => _unschaerfe.LageGeaendert();
    }

    /// <summary>Das Element hinter der Platte, das unscharf durchscheint; null zeigt nur Tönung und Körnung.</summary>
    public Visual? Quelle { get => (Visual?)GetValue(QuelleProperty); set => SetValue(QuelleProperty, value); }

    /// <summary>Radius der Unschärfe in DIP.</summary>
    public double Unschaerfe { get => (double)GetValue(UnschaerfeProperty); set => SetValue(UnschaerfeProperty, value); }

    /// <summary>Eckenradius in DIP.</summary>
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => index == 0 ? _unschaerfe : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        _unschaerfe.Measure(availableSize);
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rand = Math.Max(0, Unschaerfe);
        _unschaerfe.Arrange(new Rect(-rand, -rand, finalSize.Width + 2 * rand, finalSize.Height + 2 * rand));
        var r = Math.Min(Math.Max(0, Ecke), Math.Min(finalSize.Width, finalSize.Height) / 2);
        var schnitt = new RectangleGeometry(new Rect(finalSize), r, r);
        schnitt.Freeze();
        Clip = schnitt;
        return finalSize;
    }

    private void EffektSetzen()
    {
        var unschaerfe = new BlurEffect { Radius = Math.Max(0, Unschaerfe), RenderingBias = RenderingBias.Performance, KernelType = KernelType.Gaussian };
        unschaerfe.Freeze();
        _unschaerfe.Effect = Unschaerfe > 0 ? unschaerfe : null;
    }

    /// <summary>Zeichnet die Quelle an genau der Stelle, an der die Platte über ihr liegt, samt Rand für die Unschärfe.</summary>
    private sealed class Unscharf(Milchglasgrund grund) : FrameworkElement
    {
        private Rect _zuletzt = Rect.Empty;

        public void LageGeaendert()
        {
            if (Ausschnitt() is { } a && a != _zuletzt) InvalidateVisual();
        }

        private Rect? Ausschnitt()
        {
            // Nicht IsVisible: Das ist ohne Fenster (Musterblatt, Vorschaubilder) immer falsch.
            if (grund.Quelle is not Visual q || grund.Visibility != Visibility.Visible || VisualTreeHelper.GetParent(this) is null) return null;
            try
            {
                // Gemeinsamer Vorfahre nötig; ohne ihn (anderer Baum) gibt es nichts zu zeigen.
                if (q.FindCommonVisualAncestor(this) is null) return null;
                var t = TransformToVisual(q);
                return t.TransformBounds(new Rect(RenderSize));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (grund.Quelle is not Visual q || Ausschnitt() is not { } a) return;
            _zuletzt = a;
            var pinsel = new VisualBrush(q) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = a, Stretch = Stretch.Fill, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            drawingContext.DrawRectangle(pinsel, null, new Rect(RenderSize));
        }
    }
}

/// <summary>
/// Der Belag einer Milchglasplatte über dem unscharfen Grund: Tönung (senkrechter Verlauf, oben heller), feine Körnung
/// gegen Streifen in der Unschärfe (ein Punkt je Gerätepixel) und die Kante als Haarlinie von einem Gerätepixel, oben
/// hell wie Licht auf der Glaskante. Auf ganzen Gerätepixeln, je Größe eingefroren.
/// </summary>
public sealed class Milchglasbelag : FrameworkElement
{
    public static readonly DependencyProperty FlaecheProperty = Pinsel(nameof(Flaeche));
    public static readonly DependencyProperty KanteProperty = Pinsel(nameof(Kante));

    public static readonly DependencyProperty EckeProperty = DependencyProperty.Register(
        nameof(Ecke), typeof(double), typeof(Milchglasbelag), new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KoernungProperty = DependencyProperty.Register(
        nameof(Koernung), typeof(double), typeof(Milchglasbelag), new FrameworkPropertyMetadata(0.05, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Milchglasbelag), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Milchglasbelag()
    {
        IsHitTestVisible = false;
        SetResourceReference(FlaecheProperty, Schluessel.MilchglasFlaeche);
        SetResourceReference(KanteProperty, Schluessel.MilchglasKante);
    }

    public Brush? Flaeche { get => (Brush?)GetValue(FlaecheProperty); set => SetValue(FlaecheProperty, value); }
    public Brush? Kante { get => (Brush?)GetValue(KanteProperty); set => SetValue(KanteProperty, value); }
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }
    public double Koernung { get => (double)GetValue(KoernungProperty); set => SetValue(KoernungProperty, value); }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 4 || h < 4) return;
        var r = Math.Min(Glasgeometrie.Raster(Math.Max(0, Ecke), s), Math.Min(b, h) / 2);
        var f = new Glasflaeche(0, 0, b, h, r, s);
        var innen = f.Form(Glasgeometrie.Haarlinie);
        if (Flaeche is not null) drawingContext.DrawGeometry(Flaeche, null, innen);
        if (Koernung > 0) drawingContext.DrawGeometry(Mattkoerper.Koernung(s, Math.Clamp(Koernung, 0, 1)), null, innen);
        if (Kante is not null) drawingContext.DrawGeometry(Kante, null, f.Ring(0, Glasgeometrie.Haarlinie));
    }
}

/// <summary>Welches Glas Windows 11 hinter ein ganzes Fenster legt (<see cref="Fensterglas"/>).</summary>
public enum Fensterglasart
{
    Aus = 1,

    /// <summary>Mica: undurchsichtig, leicht in der Farbe des Desktops getönt; für Hauptfenster.</summary>
    Mica = 2,

    /// <summary>Acryl: durchscheinendes Milchglas; für kurzlebige Fenster wie Menüs und Dialoge.</summary>
    Acryl = 3,

    /// <summary>Mica für Fenster mit Reitern (etwas kräftiger getönt).</summary>
    Tabs = 4,
}

/// <summary>
/// Milchglas für ganze Fenster (Stilfamilie 4): Windows 11 legt Mica oder Acryl hinter das Fenster (DWM, ab Build 22621).
/// Das ist eine Funktion des Systems, kein fremdes Design. Sichtbar wird es dort, wo das Fenster durchsichtig ist: also
/// <c>Background="Transparent"</c> am Fenster und auf den Flächen, durch die es scheinen soll. Auf älterem Windows passiert
/// nichts, das Fenster bleibt, wie es ist. Am echten Bildschirm von Sarevoc zu prüfen; die Tests zeichnen ohne Fenster.
/// </summary>
public static partial class Fensterglas
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.RegisterAttached(
        "Art", typeof(Fensterglasart), typeof(Fensterglas), new FrameworkPropertyMetadata(Fensterglasart.Aus, Geaendert));

    public static Fensterglasart GetArt(DependencyObject element) => (Fensterglasart)element.GetValue(ArtProperty);

    public static void SetArt(DependencyObject element, Fensterglasart wert) => element.SetValue(ArtProperty, wert);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;

    private static void Geaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window fenster) return;
        if (new WindowInteropHelper(fenster).Handle != IntPtr.Zero) Anwenden(fenster);
        else fenster.SourceInitialized += (_, _) => Anwenden(fenster);
    }

    /// <summary>Setzt das Glas; gibt false zurück, wenn Windows es nicht kann (dann bleibt das Fenster unverändert).</summary>
    public static bool Anwenden(Window fenster)
    {
        ArgumentNullException.ThrowIfNull(fenster);
        var hwnd = new WindowInteropHelper(fenster).Handle;
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return false;
        var art = (int)GetArt(fenster);
        var dunkel = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dunkel, sizeof(int));
        if (art != (int)Fensterglasart.Aus)
        {
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } ziel) ziel.BackgroundColor = Colors.Transparent;
            var rand = new Raender { Links = -1, Rechts = -1, Oben = -1, Unten = -1 };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref rand);
        }
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref art, sizeof(int)) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Raender
    {
        public int Links;
        public int Rechts;
        public int Oben;
        public int Unten;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribut, ref int wert, int groesse);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Raender raender);
}
