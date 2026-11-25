// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>Art eines <see cref="Hinweisbanner"/>: bestimmt Statusfarbe, Zeichen und wie dringlich Bildschirmleser melden.</summary>
public enum Hinweisart
{
    Hinweis,
    Erfolg,
    Warnung,
    Fehler,
}

/// <summary>
/// Hinweisbanner (seit 0.5.0-beta.8): eine Zeile Rückmeldung mit Zeichen nach <see cref="Art"/> (Hinweis: Kreis mit i,
/// Erfolg: Haken, Warnung: Warndreieck, Fehler: Kreuz) in der Statusfarbe (<see cref="Statusfarbe"/>, die Stile setzen
/// Glas.Status.Info, .Erfolg, .Warnung oder .Gefahr). Bildschirmleser melden Warnung und Fehler sofort (LiveSetting
/// Assertive), Hinweis und Erfolg höflich (Polite), jeweils bei einer neuen Art oder einem neuen Inhalt; ein eigenes
/// LiveSetting bleibt stehen. Stile:
/// Glas.Hinweisbanner (wie Glas.Feld mit Leiste in der Statusfarbe) und Glas.Taktil.Hinweisbanner (gehobene, warm getönte
/// Fläche, gefülltes Zeichen).
/// </summary>
public class Hinweisbanner : ContentControl
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Hinweisart), typeof(Hinweisbanner), new FrameworkPropertyMetadata(Hinweisart.Hinweis, (d, _) => ((Hinweisbanner)d).ArtGeaendert()));

    /// <summary>Die Farbe der Art (Leiste, Zeichen, Tönung); setzen die Stile je Art, ein eigener Wert gilt vor.</summary>
    public static readonly DependencyProperty StatusfarbeProperty = DependencyProperty.Register(
        nameof(Statusfarbe), typeof(Brush), typeof(Hinweisbanner), new FrameworkPropertyMetadata(null));

    static Hinweisbanner()
    {
        AutomationProperties.LiveSettingProperty.OverrideMetadata(typeof(Hinweisbanner), new FrameworkPropertyMetadata(AutomationLiveSetting.Polite));
        FocusableProperty.OverrideMetadata(typeof(Hinweisbanner), new FrameworkPropertyMetadata(false));
    }

    public Hinweisart Art { get => (Hinweisart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public Brush? Statusfarbe { get => (Brush?)GetValue(StatusfarbeProperty); set => SetValue(StatusfarbeProperty, value); }

    /// <summary>Wie dringlich Bildschirmleser die Art melden: Warnung und Fehler sofort, sonst höflich.</summary>
    public static AutomationLiveSetting Dringlichkeit(Hinweisart art) => art is Hinweisart.Warnung or Hinweisart.Fehler ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite;

    /// <summary>Das Zeichen der Art.</summary>
    public static Glaszeichen Zeichen(Hinweisart art) => art switch
    {
        Hinweisart.Erfolg => Glaszeichen.Haken,
        Hinweisart.Warnung => Glaszeichen.Warnung,
        Hinweisart.Fehler => Glaszeichen.Kreuz,
        _ => Glaszeichen.Info,
    };

    private void ArtGeaendert()
    {
        // Ein eigenes LiveSetting (lokal, im Stil, gebunden) bleibt stehen (seit 0.5.0-beta.13, wie beim Textfeld); bis beta.12
        // überschrieb die Art es.
        var quelle = DependencyPropertyHelper.GetValueSource(this, AutomationProperties.LiveSettingProperty);
        if (quelle.BaseValueSource == BaseValueSource.Default && !quelle.IsExpression)
            SetCurrentValue(AutomationProperties.LiveSettingProperty, Dringlichkeit(Art));
        Melden();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Melden();
    }

    private void Melden()
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Meldungsflaeche(this, "Hinweisbanner");
}

/// <summary>
/// Das Zeichen einer Statusart (Hinweisbanner): als Linie in der Statusfarbe (<see cref="Gefuellt"/> falsch, über
/// <see cref="Zeichner"/>) oder gefüllt (Stilfamilie Taktil, Vorbild: Warndreieck in Bernstein): Dreieck bei Warnung,
/// sonst Kreis, gefüllt mit <see cref="Farbe"/>, darin das Zeichen (Ausrufezeichen, i, Haken, Kreuz) in der Kontrastschrift
/// der Farbe (Weiß oder fast Schwarz, <see cref="Schriftwahl.Kontrastfarbe"/>). Absolut mittig auf ganzen Gerätepixeln; die
/// gefüllte Form ist eigene Geometrie, je Größe, Art und Farbe einmal gebaut.
/// </summary>
public sealed class Statuszeichen : FrameworkElement
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(Hinweisart), typeof(Statuszeichen), new FrameworkPropertyMetadata(Hinweisart.Hinweis, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Statuszeichen), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GefuelltProperty = DependencyProperty.Register(
        nameof(Gefuellt), typeof(bool), typeof(Statuszeichen), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private readonly List<(Geometry Form, Brush? Pinsel, Pen? Stift)> _schichten = [];

    public Statuszeichen()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Hinweisart Art { get => (Hinweisart)GetValue(ArtProperty); set => SetValue(ArtProperty, value); }
    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public bool Gefuellt { get => (bool)GetValue(GefuelltProperty); set => SetValue(GefuelltProperty, value); }

    internal int Bauten { get; private set; }

    /// <summary>Größe, wenn der Behälter keine vorgibt (DIP).</summary>
    public const double Grundgroesse = 18;

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(Grundgroesse, availableSize.Width), Math.Min(Grundgroesse, availableSize.Height));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var behaelter = new Rect(0, 0, Glasgeometrie.Raster(RenderSize.Width, s), Glasgeometrie.Raster(RenderSize.Height, s));
        if (!Gefuellt)
        {
            Zeichner.Zeichnen(drawingContext, Hinweisbanner.Zeichen(Art), behaelter, 1, Farbe, s, 0.1);
            return;
        }
        var schluessel = (behaelter.Size, s, Art, Farbe);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            _schichten.Clear();
            if (Farbe is { } farbe) Bauen(behaelter, s, farbe);
            Bauten++;
        }
        foreach (var (form, pinsel, stift) in _schichten) drawingContext.DrawGeometry(pinsel, stift, form);
    }

    private void Bauen(Rect behaelter, double s, Brush farbe)
    {
        var c = Pinselwerk.Ton(farbe, Colors.Orange);
        var glyph = Pinselwerk.Flaeche(Schriftwahl.Kontrastfarbe([c, Farbton.Mischen(c, Colors.White, 0.18)]));
        // Die Form: Box gleicher Parität wie der Behälter, absolut mittig.
        var mass = Zeichner.Mass(Art == Hinweisart.Warnung ? Glaszeichen.Warnung : Glaszeichen.Info, behaelter, 1, 0.1);
        var b = mass.Box;
        var flaeche = Pinselwerk.Verlauf(new Point(0, b.Top / s), new Point(0, b.Bottom / s), Farbton.Mischen(c, Colors.White, 0.18), Farbton.Mischen(c, Colors.Black, 0.1));
        var strich = Math.Max(1, Math.Round(Math.Min(b.Width, b.Height) * 0.12));
        if (Art == Hinweisart.Warnung)
        {
            // Dreieck mit runden Ecken: gefüllt und mit einem Strich in derselben Farbe, der die Ecken rundet.
            var r = strich / 2;
            var dreieck = Vieleck(s, (b.Left + b.Width / 2, b.Top + r), (b.Right - r, b.Bottom - r), (b.Left + r, b.Bottom - r));
            var stift = new Pen(flaeche, strich / s) { LineJoin = PenLineJoin.Round };
            stift.Freeze();
            _schichten.Add((dreieck, flaeche, stift));
            var mx = b.Left + b.Width / 2;
            var h = b.Height;
            Linie(s, glyph, strich, (mx, b.Top + h * 0.38), (mx, b.Top + h * 0.62));
            Linie(s, glyph, strich, (mx, b.Top + h * 0.79), (mx, b.Top + h * 0.79 + 0.01));
            return;
        }
        var kreis = new EllipseGeometry(new Rect(b.Left / s, b.Top / s, b.Width / s, b.Height / s));
        kreis.Freeze();
        _schichten.Add((kreis, flaeche, null));
        var cx = b.Left + b.Width / 2;
        var cy = b.Top + b.Height / 2;
        var d = b.Width;
        switch (Art)
        {
            case Hinweisart.Erfolg:
                Linie(s, glyph, strich, (cx - d * 0.24, cy + d * 0.01), (cx - d * 0.06, cy + d * 0.19), (cx + d * 0.25, cy - d * 0.16));
                break;
            case Hinweisart.Fehler:
                Linie(s, glyph, strich, (cx - d * 0.18, cy - d * 0.18), (cx + d * 0.18, cy + d * 0.18));
                Linie(s, glyph, strich, (cx + d * 0.18, cy - d * 0.18), (cx - d * 0.18, cy + d * 0.18));
                break;
            default:
                Linie(s, glyph, strich, (cx, cy - d * 0.04), (cx, cy + d * 0.24));
                Linie(s, glyph, strich, (cx, cy - d * 0.23), (cx, cy - d * 0.23 + 0.01));
                break;
        }
    }

    private void Linie(double s, Brush pinsel, double strich, params (double X, double Y)[] punkte)
    {
        var g = new StreamGeometry();
        using (var k = g.Open())
        {
            k.BeginFigure(new Point(punkte[0].X / s, punkte[0].Y / s), false, false);
            foreach (var (x, y) in punkte.Skip(1)) k.LineTo(new Point(x / s, y / s), true, true);
        }
        g.Freeze();
        var stift = new Pen(pinsel, strich / s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        stift.Freeze();
        _schichten.Add((g, null, stift));
    }

    private static PathGeometry Vieleck(double s, params (double X, double Y)[] punkte)
    {
        var f = new PathFigure { StartPoint = new Point(punkte[0].X / s, punkte[0].Y / s), IsClosed = true, IsFilled = true };
        foreach (var (x, y) in punkte.Skip(1)) f.Segments.Add(new LineSegment(new Point(x / s, y / s), true));
        var g = new PathGeometry([f]);
        g.Freeze();
        return g;
    }
}

/// <summary>
/// Mischt zwei Farben (für Vorlagen, MultiBinding): Werte Grund und Farbe (Color oder Pinsel, bei Verläufen der erste Stopp),
/// <see cref="Anteil"/> der Farbe. Ergebnis: Color oder ein eingefrorener Pinsel, je nach Ziel. So folgt etwa die warm
/// getönte Fläche des Taktil-Hinweisbanners der Familienfläche und der Statusfarbe, ohne eigenen Schlüssel.
/// </summary>
public sealed class Farbmischung : IMultiValueConverter
{
    /// <summary>Anteil der zweiten Farbe (0 bis 1).</summary>
    public double Anteil { get; set; } = 0.25;

    /// <summary>Die Mischung als Farbe (für Code und Tests).</summary>
    public static Color Mischen(Color grund, Color farbe, double anteil) => Farbton.Mischen(grund, farbe, Math.Clamp(anteil, 0, 1));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2 || values[0] is not (Color or Brush) || values[1] is not (Color or Brush)) return DependencyProperty.UnsetValue;
        var c = Mischen(Farbableitung.Farbe(values[0]), Farbableitung.Farbe(values[1]), Anteil);
        if (targetType == typeof(Color)) return c;
        var p = new SolidColorBrush(c);
        p.Freeze();
        return p;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Leerzustand (seit 0.5.0-beta.8): wenn eine Liste nichts zeigt. Ein großes <see cref="Symbol"/> (Vorgabe die getrennte
/// Kette), darunter der Titel (Header), darunter die Aktion als Inhalt, etwa ein Knopf. Stile: Glas.Leerzustand und
/// Glas.Taktil.Leerzustand (eingelassene Tafel, Kette in Metall).
/// </summary>
public class Leerzustand : HeaderedContentControl
{
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(Glaszeichen), typeof(Leerzustand), new FrameworkPropertyMetadata(Glaszeichen.Kette));

    static Leerzustand()
    {
        FocusableProperty.OverrideMetadata(typeof(Leerzustand), new FrameworkPropertyMetadata(false));
    }

    public Glaszeichen Symbol { get => (Glaszeichen)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }

    protected override AutomationPeer OnCreateAutomationPeer() => new Meldungsflaeche(this, "Leerzustand");
}
