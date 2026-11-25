// Glasdesign - Maintainer: Sarevoc
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>Stand eines <see cref="Schritt"/>: noch offen, gerade dran oder erledigt.</summary>
public enum Schrittstand
{
    Offen,
    Aktiv,
    Erledigt,
}

/// <summary>
/// Schrittfolge (seit 0.5.0-beta.9, Stilfamilien 2 Stufe 2): ein Ablauf aus Schritten (<see cref="Schritt"/>) mit
/// Verbindungslinien; <see cref="Aktiv"/> ist die Stelle des aktuellen Schritts (von 0 an), die Schritte davor sind erledigt,
/// die danach offen (-1: alle offen, über dem letzten: alle erledigt). Die Beschriftung des aktiven Schritts steht in einer
/// Sprechblase darüber, Zipfel nach unten. Einträge sind Texte, Objekte oder Schritte (dann mit eigenem Symbol). Eine
/// Anzeige, nicht bedienbar und nicht fokussierbar. Automation: eine Liste, jeder Schritt ein Eintrag "Schritt 3 von 5:
/// Bestellung prüfen, aktiv" (auch als ToolTip). Stile: Glas.Schrittfolge und Glas.Taktil.Schrittfolge.
/// </summary>
public class Schrittfolge : ItemsControl
{
    public static readonly DependencyProperty AktivProperty = DependencyProperty.Register(
        nameof(Aktiv), typeof(int), typeof(Schrittfolge), new FrameworkPropertyMetadata(0, (d, _) => ((Schrittfolge)d).Ordnen()));

    static Schrittfolge()
    {
        FocusableProperty.OverrideMetadata(typeof(Schrittfolge), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Die Stelle des aktiven Schritts (von 0 an).</summary>
    public int Aktiv { get => (int)GetValue(AktivProperty); set => SetValue(AktivProperty, value); }

    /// <summary>Der Stand eines Schritts an Stelle <paramref name="index"/>, wenn <paramref name="aktiv"/> der aktive ist.</summary>
    public static Schrittstand Stand(int index, int aktiv) => index < aktiv ? Schrittstand.Erledigt : index == aktiv ? Schrittstand.Aktiv : Schrittstand.Offen;

    protected override bool IsItemItsOwnContainerOverride(object item) => item is Schritt;

    protected override DependencyObject GetContainerForItemOverride() => new Schritt();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is Schritt s) Einordnen(s, ItemContainerGenerator.IndexFromContainer(s));
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        Ordnen();
    }

    private void Ordnen()
    {
        for (var i = 0; i < Items.Count; i++)
            if (ItemContainerGenerator.ContainerFromIndex(i) is Schritt s) Einordnen(s, i);
    }

    private void Einordnen(Schritt s, int index)
    {
        if (index < 0) return;
        s.SetValue(Schritt.IstErsterKey, index == 0);
        s.SetCurrentValue(Schritt.NummerProperty, index + 1);
        s.SetCurrentValue(Schritt.AnzahlProperty, Items.Count);
        s.SetCurrentValue(Schritt.StandProperty, Stand(index, Aktiv));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Navigationsflaeche(this, nameof(Schrittfolge), "Schrittfolge", AutomationControlType.List);
}

/// <summary>
/// Ein Schritt der <see cref="Schrittfolge"/>: ein runder Knopf mit <see cref="Symbol"/> (erledigt ohne eigenes Symbol ein
/// Haken, sonst ohne Symbol die <see cref="Nummer"/>), davor die Linie vom vorigen Schritt; der Inhalt ist die
/// Beschriftung. <see cref="Stand"/>, <see cref="Nummer"/> und <see cref="Anzahl"/> setzt die Schrittfolge.
/// </summary>
public class Schritt : ContentControl
{
    public static readonly DependencyProperty StandProperty = DependencyProperty.Register(
        nameof(Stand), typeof(Schrittstand), typeof(Schritt), new FrameworkPropertyMetadata(Schrittstand.Offen, (d, _) => ((Schritt)d).Neu()));

    /// <summary>Ein eigenes Zeichen; Keins (Vorgabe): erledigt ein Haken, sonst die Nummer.</summary>
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(Glaszeichen), typeof(Schritt), new FrameworkPropertyMetadata(Glaszeichen.Keins, (d, _) => ((Schritt)d).Neu()));

    public static readonly DependencyProperty NummerProperty = DependencyProperty.Register(
        nameof(Nummer), typeof(int), typeof(Schritt), new FrameworkPropertyMetadata(1, (d, _) => ((Schritt)d).Neu()));

    /// <summary>Wie viele Schritte die Folge hat (für "Schritt 3 von 5").</summary>
    public static readonly DependencyProperty AnzahlProperty = DependencyProperty.Register(
        nameof(Anzahl), typeof(int), typeof(Schritt), new FrameworkPropertyMetadata(1, (d, _) => ((Schritt)d).Neu()));

    internal static readonly DependencyPropertyKey IstErsterKey = DependencyProperty.RegisterReadOnly(
        nameof(IstErster), typeof(bool), typeof(Schritt), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IstErsterProperty = IstErsterKey.DependencyProperty;

    private static readonly DependencyPropertyKey ZeichenKey = DependencyProperty.RegisterReadOnly(
        nameof(Zeichen), typeof(Glaszeichen), typeof(Schritt), new FrameworkPropertyMetadata(Glaszeichen.Keins));

    /// <summary>Das gezeichnete Zeichen: <see cref="Symbol"/>, erledigt ohne Symbol ein Haken, sonst Keins (dann die Nummer).</summary>
    public static readonly DependencyProperty ZeichenProperty = ZeichenKey.DependencyProperty;

    private string? _eigenerTipp;

    static Schritt()
    {
        FocusableProperty.OverrideMetadata(typeof(Schritt), new FrameworkPropertyMetadata(false));
    }

    public Schritt() => Neu();

    public Schrittstand Stand { get => (Schrittstand)GetValue(StandProperty); set => SetValue(StandProperty, value); }
    public Glaszeichen Symbol { get => (Glaszeichen)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public int Nummer { get => (int)GetValue(NummerProperty); set => SetValue(NummerProperty, value); }
    public int Anzahl { get => (int)GetValue(AnzahlProperty); set => SetValue(AnzahlProperty, value); }
    public bool IstErster => (bool)GetValue(IstErsterProperty);
    public Glaszeichen Zeichen => (Glaszeichen)GetValue(ZeichenProperty);

    /// <summary>Der Stand in Worten: offen, aktiv, erledigt.</summary>
    public static string Standtext(Schrittstand stand) => stand switch
    {
        Schrittstand.Aktiv => "aktiv",
        Schrittstand.Erledigt => "erledigt",
        _ => "offen",
    };

    /// <summary>"Schritt 3 von 5: Bestellung prüfen, aktiv" (Automation-Name und ToolTip).</summary>
    public string Beschreibung
    {
        get
        {
            var text = Rueckmeldungstext.Von(Content);
            var kopf = string.Create(CultureInfo.CurrentCulture, $"Schritt {Nummer} von {Math.Max(Nummer, Anzahl)}");
            return text.Length > 0 ? $"{kopf}: {text}, {Standtext(Stand)}" : $"{kopf}, {Standtext(Stand)}";
        }
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Neu();
    }

    /// <summary>Zeichen und ToolTip neu; einen eigenen ToolTip lässt er stehen.</summary>
    private void Neu()
    {
        var zeichen = Symbol != Glaszeichen.Keins ? Symbol : Stand == Schrittstand.Erledigt ? Glaszeichen.Haken : Glaszeichen.Keins;
        if (Zeichen != zeichen) SetValue(ZeichenKey, zeichen);
        if (ToolTip is null || (ToolTip is string t && t == _eigenerTipp))
        {
            _eigenerTipp = Beschreibung;
            SetCurrentValue(ToolTipProperty, _eigenerTipp);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Schrittflaeche(this);
}

/// <summary>Automation eines <see cref="Schritt"/>: ein Listeneintrag mit der Beschreibung als Namen und dem Stand als Status.</summary>
internal sealed class Schrittflaeche(Schritt schritt) : FrameworkElementAutomationPeer(schritt)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => nameof(Schritt);

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        // Der Grundname wäre der ToolTip-Text oder der Inhalt; maßgeblich ist die Beschreibung, außer man setzt selbst einen.
        return AutomationProperties.GetName(schritt) is { Length: > 0 } eigen ? eigen : schritt.Beschreibung.Length > 0 ? schritt.Beschreibung : name;
    }

    protected override string GetItemStatusCore() => Schritt.Standtext(schritt.Stand);

    protected override bool IsKeyboardFocusableCore() => false;
}

/// <summary>
/// Die Verbindungslinie vor einem Schritt: ein waagerechter Balken von <see cref="Dicke"/> (ganze Gerätepixel, senkrecht
/// absolut mittig) in <see cref="Farbe"/>, mit <see cref="Schein"/> darüber und darunter ein weicher Saum in derselben
/// Farbe (Ringe je Gerätepixel). Je Größe und Werten einmal gebaut und eingefroren.
/// </summary>
public sealed class Schrittlinie : FrameworkElement
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Schrittlinie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DickeProperty = DependencyProperty.Register(
        nameof(Dicke), typeof(double), typeof(Schrittlinie), new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Stärke des Saums (0 bis 1); 0 zeichnet keinen.</summary>
    public static readonly DependencyProperty ScheinProperty = DependencyProperty.Register(
        nameof(Schein), typeof(double), typeof(Schrittlinie), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Reichweite des Saums in DIP.</summary>
    public const double Scheinweite = 4;

    private object? _schluessel;
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    public Schrittlinie()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public double Dicke { get => (double)GetValue(DickeProperty); set => SetValue(DickeProperty, value); }
    public double Schein { get => (double)GetValue(ScheinProperty); set => SetValue(ScheinProperty, value); }

    internal int Bauten { get; private set; }

    /// <summary>Die Linie in Pixeln: oben und Dicke, so dass über und unter ihr gleich viel Platz bleibt.</summary>
    public static (double Oben, double Dicke) Lage(double hoehePx, double dicke, double skala)
    {
        var t = Glasgeometrie.Linie(dicke, skala);
        if ((hoehePx - t) % 2 != 0) t += 1;
        return ((hoehePx - t) / 2, t);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var s = Glasgeometrie.Skala(this);
        var schluessel = (RenderSize, s, Farbe, Dicke, Schein);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            _schichten.Clear();
            Bauen(s);
            Bauten++;
        }
        foreach (var (form, pinsel) in _schichten) drawingContext.DrawGeometry(pinsel, null, form);
    }

    private void Bauen(double s)
    {
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 1 || h < 1 || Farbe is not { } farbe) return;
        var (oben, t) = Lage(h, Dicke, s);
        Rect Px(double y, double hoehe) => new(0, y / s, b / s, hoehe / s);
        if (Schein > 0)
        {
            var ton = Pinselwerk.Ton(farbe, Colors.White);
            var weite = Math.Max(1, (int)Glasgeometrie.Raster(Scheinweite, s));
            for (var i = 0; i < weite; i++)
            {
                var a = Math.Clamp(Schein * 0.5 * Math.Pow(1 - (i + 0.5) / weite, 1.5), 0, 1) * ton.A / 255.0;
                if (a * 255 < 0.5) continue;
                var pinsel = Pinselwerk.Flaeche(Farbton.Deckend(ton, a));
                Hinzu(new RectangleGeometry(Px(oben - i - 1, 1)), pinsel);
                Hinzu(new RectangleGeometry(Px(oben + t + i, 1)), pinsel);
            }
        }
        Hinzu(new RectangleGeometry(Px(oben, t)), farbe);
    }

    private void Hinzu(Geometry form, Brush pinsel)
    {
        form.Freeze();
        _schichten.Add((form, pinsel));
    }
}

/// <summary>
/// Panel für eine schwebende Beschriftung (die Sprechblase über dem aktiven Schritt): jedes Kind in seiner eigenen Breite
/// mittig über der Mitte des Panels, auch wenn es breiter ist (es wird nicht beschnitten); das Panel selbst nimmt keine
/// Breite in Anspruch, nur die Höhe. Die Breite des Kinds wird um ein Gerätepixel erweitert, wenn sonst seine Mitte nicht
/// genau auf der Mitte des Panels läge (gleiche Parität), damit der Zipfel absolut mittig zeigt.
/// </summary>
public sealed class Schwebemitte : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double h = 0;
        foreach (UIElement kind in InternalChildren)
        {
            kind.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            h = Math.Max(h, kind.DesiredSize.Height);
        }
        return new Size(0, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = Glasgeometrie.Skala(this);
        var breitePx = Glasgeometrie.Raster(finalSize.Width, s);
        foreach (UIElement kind in InternalChildren)
        {
            var kb = Glasgeometrie.Raster(kind.DesiredSize.Width, s);
            if ((breitePx - kb) % 2 != 0) kb += 1;
            var kh = kind.DesiredSize.Height;
            kind.Arrange(new Rect((breitePx - kb) / 2 / s, finalSize.Height - kh, kb / s, kh));
        }
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateMeasure();
}
