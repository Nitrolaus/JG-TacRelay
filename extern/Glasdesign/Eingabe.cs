// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>
/// Eingabe (seit 0.5.0-beta.11, Stilfamilien 2 Stufe 2, Teilschritt A4): die angehängten Eigenschaften der Textfelder und
/// des Knopfs mit Mulde. Ohne sie zeichnen Glas.Textfeld, Glas.Passwortfeld und Glas.Taktil.Textfeld Pixel für Pixel wie
/// bisher und messen gleich groß.
/// </summary>
public static partial class Glas
{
    /// <summary>
    /// Platzhalter: grauer Text im leeren Feld (TextBox und PasswordBox in den Stilen Glas.Textfeld, Glas.Passwortfeld,
    /// Glas.Taktil.Textfeld). Er verschwindet mit dem ersten Zeichen; null (Vorgabe) zeigt keinen.
    /// </summary>
    public static readonly DependencyProperty PlatzhalterProperty = DependencyProperty.RegisterAttached(
        "Platzhalter", typeof(string), typeof(Glas), new FrameworkPropertyMetadata(null, Eingabefeld.Geaendert));

    /// <summary>Eine Zeile unter dem Feld (Hinweis, etwa "Wir geben sie nicht weiter."); null (Vorgabe) zeigt keine.</summary>
    public static readonly DependencyProperty HinweistextProperty = DependencyProperty.RegisterAttached(
        "Hinweistext", typeof(string), typeof(Glas), new FrameworkPropertyMetadata(null, Eingabefeld.Geaendert));

    /// <summary>
    /// Fehlertext: gesetzt (nicht leer) steht das Feld im Fehlerzustand, Rand und Text in der Fehlerfarbe (Glas 4
    /// Glas.Status.Gefahr, Taktil Kupfer), der Text unter dem Feld. Für Bildschirmleser wird er zum ItemStatus des Felds und
    /// sofort gemeldet (LiveSetting Assertive).
    /// </summary>
    public static readonly DependencyProperty FehlertextProperty = DependencyProperty.RegisterAttached(
        "Fehlertext", typeof(string), typeof(Glas), new FrameworkPropertyMetadata(null, Eingabefeld.Geaendert));

    /// <summary>
    /// Fehlerbalken: Anteil 0 bis 1 eines schmalen Balkens unter dem Feld (etwa die Stärke eines Passworts), gefüllt in der
    /// Fehlerfarbe, wenn ein Fehler steht, sonst im Akzent. NaN (Vorgabe) zeigt keinen Balken.
    /// </summary>
    public static readonly DependencyProperty FehlerbalkenProperty = DependencyProperty.RegisterAttached(
        "Fehlerbalken", typeof(double), typeof(Glas), new FrameworkPropertyMetadata(double.NaN, Eingabefeld.Geaendert));

    /// <summary>Das Zeichen in der eingelassenen runden Mulde links im Knopf (Stile Glas.Knopf.Mulde, Glas.Dickglas.Knopf.Mulde); Keins (Vorgabe) zeigt keine Mulde.</summary>
    public static readonly DependencyProperty MuldensymbolProperty = DependencyProperty.RegisterAttached(
        "Muldensymbol", typeof(Glaszeichen), typeof(Glas), new FrameworkPropertyMetadata(Glaszeichen.Keins));

    public static string? GetPlatzhalter(DependencyObject element) => (string?)Pruefen(element).GetValue(PlatzhalterProperty);

    public static void SetPlatzhalter(DependencyObject element, string? wert) => Pruefen(element).SetValue(PlatzhalterProperty, wert);

    public static string? GetHinweistext(DependencyObject element) => (string?)Pruefen(element).GetValue(HinweistextProperty);

    public static void SetHinweistext(DependencyObject element, string? wert) => Pruefen(element).SetValue(HinweistextProperty, wert);

    public static string? GetFehlertext(DependencyObject element) => (string?)Pruefen(element).GetValue(FehlertextProperty);

    public static void SetFehlertext(DependencyObject element, string? wert) => Pruefen(element).SetValue(FehlertextProperty, wert);

    public static double GetFehlerbalken(DependencyObject element) => (double)Pruefen(element).GetValue(FehlerbalkenProperty);

    public static void SetFehlerbalken(DependencyObject element, double wert) => Pruefen(element).SetValue(FehlerbalkenProperty, wert);

    public static Glaszeichen GetMuldensymbol(DependencyObject element) => (Glaszeichen)Pruefen(element).GetValue(MuldensymbolProperty);

    public static void SetMuldensymbol(DependencyObject element, Glaszeichen wert) => Pruefen(element).SetValue(MuldensymbolProperty, wert);
}

/// <summary>
/// Der Zustand eines Textfelds für die Vorlagen (seit 0.5.0-beta.11), aus den Eigenschaften der Eingabe gerechnet und nur
/// lesbar: <see cref="PlatzhalterSichtbarProperty"/> (Platzhalter gesetzt und das Feld leer), <see cref="HatHinweisProperty"/>,
/// <see cref="HatFehlerProperty"/> und <see cref="HatBalkenProperty"/>. Die Vorlagen schalten damit Zeilen und Fehlerfarbe.
/// Dazu die Automation des Felds: HelpText ist der Hinweistext, ItemStatus der Fehlertext, Name ohne eigenen Namen und
/// ohne LabeledBy der Platzhalter (seit 0.5.0-beta.13), und solange ein Fehler steht, meldet das Feld neue Fehler sofort
/// (LiveSetting Assertive). Eigene Werte dieser Eigenschaften bleiben stehen.
/// </summary>
public static class Eingabefeld
{
    private static readonly DependencyPropertyKey PlatzhalterSichtbarKey = Lesbar("PlatzhalterSichtbar");
    private static readonly DependencyPropertyKey HatHinweisKey = Lesbar("HatHinweis");
    private static readonly DependencyPropertyKey HatFehlerKey = Lesbar("HatFehler");
    private static readonly DependencyPropertyKey HatBalkenKey = Lesbar("HatBalken");

    public static readonly DependencyProperty PlatzhalterSichtbarProperty = PlatzhalterSichtbarKey.DependencyProperty;
    public static readonly DependencyProperty HatHinweisProperty = HatHinweisKey.DependencyProperty;
    public static readonly DependencyProperty HatFehlerProperty = HatFehlerKey.DependencyProperty;
    public static readonly DependencyProperty HatBalkenProperty = HatBalkenKey.DependencyProperty;

    /// <summary>Was diese Klasse an der Automation gesetzt hat (damit eigene Werte stehen bleiben).</summary>
    private static readonly DependencyProperty GesetztProperty = DependencyProperty.RegisterAttached(
        "Gesetzt", typeof(Automationsstand), typeof(Eingabefeld), new FrameworkPropertyMetadata(null));

    private sealed record Automationsstand(string Hilfe, string Status, bool Live, string Name = "");

    private static DependencyPropertyKey Lesbar(string name) => DependencyProperty.RegisterAttachedReadOnly(
        name, typeof(bool), typeof(Eingabefeld), new FrameworkPropertyMetadata(false));

    static Eingabefeld()
    {
        EventManager.RegisterClassHandler(typeof(TextBox), TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, _) => Leere((DependencyObject)s)), true);
        EventManager.RegisterClassHandler(typeof(PasswordBox), PasswordBox.PasswordChangedEvent, new RoutedEventHandler((s, _) => Leere((DependencyObject)s)), true);
    }

    public static bool GetPlatzhalterSichtbar(DependencyObject element) => (bool)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(PlatzhalterSichtbarProperty);

    public static bool GetHatHinweis(DependencyObject element) => (bool)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(HatHinweisProperty);

    public static bool GetHatFehler(DependencyObject element) => (bool)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(HatFehlerProperty);

    public static bool GetHatBalken(DependencyObject element) => (bool)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(HatBalkenProperty);

    /// <summary>Ob das Feld leer ist (TextBox ohne Text, PasswordBox ohne Zeichen); andere Elemente gelten als leer.</summary>
    public static bool Leer(DependencyObject element)
    {
        switch (element)
        {
            case TextBox t:
                return string.IsNullOrEmpty(t.Text);
            case PasswordBox p:
            {
                using var geheim = p.SecurePassword;
                return geheim.Length == 0;
            }
            default:
                return true;
        }
    }

    internal static void Geaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Aktualisieren(d);
        if (e.Property == Glas.FehlertextProperty && !string.IsNullOrEmpty(e.NewValue as string)) Melden(d);
    }

    private static void Leere(DependencyObject d)
    {
        if (Glas.GetPlatzhalter(d) is null && !GetPlatzhalterSichtbar(d)) return;
        Aktualisieren(d);
    }

    private static void Setzen(DependencyObject d, DependencyPropertyKey schluessel, bool wert)
    {
        if ((bool)d.GetValue(schluessel.DependencyProperty) == wert) return;
        if (wert) d.SetValue(schluessel, true);
        else d.ClearValue(schluessel);
    }

    private static void Aktualisieren(DependencyObject d)
    {
        var hinweis = Glas.GetHinweistext(d) ?? "";
        var fehler = Glas.GetFehlertext(d) ?? "";
        Setzen(d, PlatzhalterSichtbarKey, !string.IsNullOrEmpty(Glas.GetPlatzhalter(d)) && Leer(d));
        Setzen(d, HatHinweisKey, hinweis.Length > 0);
        Setzen(d, HatFehlerKey, fehler.Length > 0);
        Setzen(d, HatBalkenKey, !double.IsNaN(Glas.GetFehlerbalken(d)));

        // Automation: nur Werte ersetzen, die leer sind oder von hier stammen.
        var alt = (Automationsstand?)d.GetValue(GesetztProperty) ?? new Automationsstand("", "", false);
        var hilfe = Uebernehmen(d, AutomationProperties.HelpTextProperty, alt.Hilfe, hinweis);
        var status = Uebernehmen(d, AutomationProperties.ItemStatusProperty, alt.Status, fehler);
        // Ohne eigenen Namen und ohne Beschriftung (LabeledBy) heißt das Feld wie sein Platzhalter (seit 0.5.0-beta.13, wie das
        // Suchfeld; bis beta.12 las ein Bildschirmleser ein namenloses Feld).
        var name = AutomationProperties.GetLabeledBy(d) is null ? Uebernehmen(d, AutomationProperties.NameProperty, alt.Name, Glas.GetPlatzhalter(d) ?? "") : "";
        var live = alt.Live;
        var jetzt = AutomationProperties.GetLiveSetting(d);
        if (fehler.Length > 0 && (jetzt == AutomationLiveSetting.Off || alt.Live))
        {
            if (jetzt != AutomationLiveSetting.Assertive) AutomationProperties.SetLiveSetting(d, AutomationLiveSetting.Assertive);
            live = true;
        }
        else if (fehler.Length == 0 && alt.Live)
        {
            if (jetzt == AutomationLiveSetting.Assertive) d.ClearValue(AutomationProperties.LiveSettingProperty);
            live = false;
        }
        var neu = new Automationsstand(hilfe, status, live, name);
        if (neu != alt) d.SetValue(GesetztProperty, neu);
    }

    /// <summary>Setzt eine Automation-Zeichenkette, wenn sie leer ist oder den zuletzt von hier gesetzten Wert trägt; gibt zurück, was nun von hier stammt.</summary>
    private static string Uebernehmen(DependencyObject d, DependencyProperty eigenschaft, string zuletzt, string wert)
    {
        var jetzt = (string?)d.GetValue(eigenschaft) ?? "";
        if (jetzt.Length > 0 && jetzt != zuletzt) return "";
        if (wert.Length == 0) d.ClearValue(eigenschaft);
        else if (jetzt != wert) d.SetValue(eigenschaft, wert);
        return wert;
    }

    /// <summary>Den Bildschirmleser auf den neuen Fehler hinweisen (LiveRegionChanged), wenn einer zuhört.</summary>
    private static void Melden(DependencyObject d)
    {
        if (d is not UIElement e || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        UIElementAutomationPeer.CreatePeerForElement(e)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}

/// <summary>
/// Schrift in einer bestimmten Farbe (Fehlertext in der Statusfarbe, im Kupfer), die auf ihrem Grund lesbar bleibt (seit
/// 0.5.0-beta.11): Hält die Farbe auf allen Farben der Fläche 4,5 zu 1 (gesperrt 3 zu 1), bleibt sie genau so; sonst wird
/// sie bei gleichem Farbton und gleicher Sättigung heller (auf dunklem Grund) oder dunkler (auf hellem), bis sie die Grenze
/// mit etwas Spielraum hält. Der Ton bleibt so erhalten (Rot bleibt rot, Kupfer Kupfer), anders als bei der
/// <see cref="Schriftwahl"/>, die ganz auf Weiß oder Schwarz wechselt.
/// </summary>
public static class Farbschrift
{
    /// <summary>Zusätzlicher Abstand über der Grenze, wenn gemischt wird (Glättung der Schrift an Pixeln).</summary>
    public const double Spielraum = 0.25;

    /// <summary>Der kleinste Kontrast der Farbe auf allen Farben der Fläche (21 ohne Fläche).</summary>
    public static double Kleinster(Color farbe, IReadOnlyList<Color> flaeche)
    {
        ArgumentNullException.ThrowIfNull(flaeche);
        var kleinster = 21.0;
        foreach (var f in flaeche)
        {
            var grund = Color.FromRgb(f.R, f.G, f.B);
            kleinster = Math.Min(kleinster, Farbton.Kontrast(Schriftwahl.Ueber(farbe, grund), grund));
        }
        return kleinster;
    }

    /// <summary>
    /// Die Farbe selbst, wenn sie hält; sonst heller (auf dunklem Grund) oder dunkler (auf hellem), bei gleichem Farbton und
    /// gleicher Sättigung (Helligkeit im HSL-Raum in Schritten von 2 Prozent), bis sie die Grenze mit Spielraum hält. So bleibt
    /// ein Kupfer ein Kupfer statt ins Rosa zu verblassen; reicht das nicht, Weiß oder fast Schwarz.
    /// </summary>
    public static Color Lesbar(Color farbe, IReadOnlyList<Color> flaeche, double grenze = Schriftwahl.Grenze)
    {
        ArgumentNullException.ThrowIfNull(flaeche);
        if (flaeche.Count == 0 || Kleinster(farbe, flaeche) >= grenze) return farbe;
        var ziel = Schriftwahl.Kontrastfarbe(flaeche);
        var heller = ziel == Schriftwahl.Hell;
        var (h, s, l) = Hsl(farbe);
        for (var i = 1; i <= 50; i++)
        {
            var neu = Math.Clamp(l + (heller ? 0.02 : -0.02) * i, 0, 1);
            var c = Rgb(farbe.A, h, s, neu);
            if (Kleinster(c, flaeche) >= grenze + Spielraum) return c;
            if (neu is 0 or 1) break;
        }
        return Color.FromArgb(farbe.A, ziel.R, ziel.G, ziel.B);
    }

    /// <summary>Farbton (0 bis 360), Sättigung und Helligkeit (0 bis 1) einer Farbe.</summary>
    internal static (double H, double S, double L) Hsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;
        if (d < 1e-9) return (0, 0, l);
        var s = d / (1 - Math.Abs(2 * l - 1));
        double h;
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        return (h < 0 ? h + 360 : h, Math.Clamp(s, 0, 1), l);
    }

    /// <summary>Die Farbe zu Farbton, Sättigung und Helligkeit (HSL).</summary>
    internal static Color Rgb(byte deckung, double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        static byte K(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        return Color.FromArgb(deckung, K(r + m), K(g + m), K(b + m));
    }

    /// <summary>
    /// Für Bindungen in Vorlagen (MultiBinding): Farbe (Pinsel oder Color), Fläche (Pinsel, Farbe oder Farbliste, wie bei
    /// <see cref="Schriftwahl.Wandler"/>) und wahlweise IsEnabled; Ergebnis ein Pinsel. Hält die Farbe, kommt ihr Pinsel zurück.
    /// </summary>
    public static IMultiValueConverter Wandler { get; } = new Lesewandler();

    private sealed class Lesewandler : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null || values.Length < 2) return DependencyProperty.UnsetValue;
            Color farbe;
            Brush? pinsel = null;
            switch (values[0])
            {
                case Color c:
                    farbe = c;
                    break;
                case Brush b:
                    farbe = Farbableitung.Farbe(b);
                    pinsel = b;
                    break;
                default:
                    return DependencyProperty.UnsetValue;
            }
            IReadOnlyList<Color> flaeche = values[1] switch
            {
                Brush b => Schriftwahl.Farben(b),
                Color c => [c],
                IReadOnlyList<Color> l => l,
                _ => [],
            };
            var grenze = values.Length > 2 && values[2] is false ? Schriftwahl.GrenzeGesperrt : Schriftwahl.Grenze;
            var lesbar = Lesbar(farbe, flaeche, grenze);
            if (lesbar == farbe && pinsel is not null) return pinsel;
            return Pinselwerk.Flaeche(lesbar);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

/// <summary>
/// Ein schmaler Balken, gefüllt bis zu einem Anteil (seit 0.5.0-beta.11, der Fehlerbalken unter dem Textfeld): Spur als
/// Pille (<see cref="Spur"/>, wahlweise mit einer Haarlinie <see cref="Kante"/>), darüber die Füllung von links
/// (<see cref="Fuellung"/>) bis <see cref="Anteil"/> der Breite, mindestens so lang wie hoch. Alle Kanten auf ganzen
/// Gerätepixeln, Geometrie je Größe, Skalierung und Anteil einmal gebaut und eingefroren; NaN zeichnet nichts.
/// </summary>
public sealed class Anteilbalken : FrameworkElement
{
    public static readonly DependencyProperty AnteilProperty = DependencyProperty.Register(
        nameof(Anteil), typeof(double), typeof(Anteilbalken), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpurProperty = Pinsel(nameof(Spur));
    public static readonly DependencyProperty KanteProperty = Pinsel(nameof(Kante));
    public static readonly DependencyProperty FuellungProperty = Pinsel(nameof(Fuellung));

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Anteilbalken), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private Geometry? _spur;
    private Geometry? _kante;
    private Geometry? _fuellung;

    public Anteilbalken()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>Anteil 0 bis 1 (begrenzt); NaN: nichts.</summary>
    public double Anteil { get => (double)GetValue(AnteilProperty); set => SetValue(AnteilProperty, value); }
    public Brush? Spur { get => (Brush?)GetValue(SpurProperty); set => SetValue(SpurProperty, value); }
    public Brush? Kante { get => (Brush?)GetValue(KanteProperty); set => SetValue(KanteProperty, value); }
    public Brush? Fuellung { get => (Brush?)GetValue(FuellungProperty); set => SetValue(FuellungProperty, value); }

    /// <summary>Wie oft die Geometrie gebaut wurde (für die Tests).</summary>
    internal int Bauten { get; private set; }

    /// <summary>Breite der Füllung in Pixeln für eine Spur von <paramref name="breitePx"/> mal <paramref name="hoehePx"/> Pixeln.</summary>
    public static double Fuellbreite(double anteil, double breitePx, double hoehePx)
    {
        if (double.IsNaN(anteil) || anteil <= 0) return 0;
        var w = Math.Round(breitePx * Math.Clamp(anteil, 0, 1));
        return Math.Min(breitePx, Math.Max(w, hoehePx));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (double.IsNaN(Anteil)) return;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 2 || h < 1) return;
        var w = Fuellbreite(Anteil, b, h);
        var schluessel = (b, h, s, w);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            var r = h / 2;
            var f = new Glasflaeche(0, 0, b, h, r, s);
            _spur = f.Form(0);
            _kante = h > 2 ? f.Ring(0, Glasgeometrie.Haarlinie) : null;
            _fuellung = w > 0 ? new Glasflaeche(0, 0, w, h, Math.Min(r, w / 2), s).Form(0) : null;
            Bauten++;
        }
        if (Spur is not null) drawingContext.DrawGeometry(Spur, null, _spur);
        if (Kante is not null && _kante is not null) drawingContext.DrawGeometry(Kante, null, _kante);
        if (Fuellung is not null && _fuellung is not null) drawingContext.DrawGeometry(Fuellung, null, _fuellung);
    }
}

/// <summary>
/// Der Punkt im gewählten Optionsfeld (seit 0.5.0-beta.11): ein gefüllter Kreis absolut mittig auf ganzen Gerätepixeln
/// (gleiche Parität wie das Feld), Durchmesser <see cref="Anteil"/> der kürzeren Seite, in <see cref="Farbe"/>; mit
/// <see cref="Glanz"/> gewölbt (oben links heller, unten rechts dunkler, ein Lichtpunkt), mit <see cref="Schein"/> ein
/// weiches Leuchten in der eigenen Farbe außen (Ringe von je einem Gerätepixel). Je Größe und Werten einmal gebaut.
/// </summary>
public sealed class Optionspunkt : FrameworkElement
{
    public static readonly DependencyProperty FarbeProperty = DependencyProperty.Register(
        nameof(Farbe), typeof(Brush), typeof(Optionspunkt), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AnteilProperty = Zahl(nameof(Anteil), 0.42);
    public static readonly DependencyProperty GlanzProperty = Zahl(nameof(Glanz), 0);
    public static readonly DependencyProperty ScheinProperty = Zahl(nameof(Schein), 0);

    private static DependencyProperty Zahl(string name, double vorgabe) => DependencyProperty.Register(
        name, typeof(double), typeof(Optionspunkt), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private object? _schluessel;
    private readonly List<(Geometry Form, Brush Pinsel)> _schichten = [];

    public Optionspunkt()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Brush? Farbe { get => (Brush?)GetValue(FarbeProperty); set => SetValue(FarbeProperty, value); }
    public double Anteil { get => (double)GetValue(AnteilProperty); set => SetValue(AnteilProperty, value); }
    public double Glanz { get => (double)GetValue(GlanzProperty); set => SetValue(GlanzProperty, value); }
    public double Schein { get => (double)GetValue(ScheinProperty); set => SetValue(ScheinProperty, value); }

    internal int Bauten { get; private set; }

    /// <summary>Lage des Punkts in Pixeln in einem Feld von <paramref name="breitePx"/> mal <paramref name="hoehePx"/>: absolut mittig, gleiche Parität.</summary>
    public static Rect Lage(double breitePx, double hoehePx, double anteil)
    {
        var d = Math.Min(breitePx, hoehePx);
        var p = Math.Max(2, Math.Round(d * Math.Clamp(anteil, 0, 1)));
        if ((breitePx - p) % 2 != 0) p++;
        return new Rect((breitePx - p) / 2, Math.Round((hoehePx - p) / 2), p, p);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Farbe is null) return;
        var s = Glasgeometrie.Skala(this);
        var b = Glasgeometrie.Raster(RenderSize.Width, s);
        var h = Glasgeometrie.Raster(RenderSize.Height, s);
        if (b < 4 || h < 4) return;
        var c = Pinselwerk.Ton(Farbe, Colors.White);
        var schluessel = (b, h, s, c, Anteil, Glanz, Schein);
        if (!Equals(_schluessel, schluessel))
        {
            _schluessel = schluessel;
            Bauen(b, h, s, c);
            Bauten++;
        }
        foreach (var (form, pinsel) in _schichten) drawingContext.DrawGeometry(pinsel, null, form);
    }

    private void Bauen(double b, double h, double s, Color c)
    {
        _schichten.Clear();
        var l = Lage(b, h, Anteil);
        var f = new Glasflaeche(l.X, l.Y, l.Width, l.Height, l.Width / 2, s);
        if (Schein > 0)
        {
            var weite = Math.Max(1, (int)Math.Round(l.Width * 0.45 * Math.Clamp(Schein, 0, 2)));
            for (var i = 0; i < weite; i++)
            {
                var t = (i + 0.5) / weite;
                var deckung = Math.Clamp(0.42 * Schein * Math.Pow(1 - t, 1.6), 0, 1);
                if (deckung < 0.004) continue;
                _schichten.Add((f.Ring(-(i + 1), -i), Pinselwerk.Flaeche(Farbton.Deckend(c, deckung * c.A / 255.0))));
            }
        }
        if (Glanz <= 0)
        {
            _schichten.Add((f.Form(0), Pinselwerk.Flaeche(c)));
            return;
        }
        var g = Math.Clamp(Glanz, 0, 1);
        var r = l.Width / 2 / s;
        var m = f.Dip(l.Width / 2, l.Height / 2);
        var licht = new Point(m.X - 0.32 * r, m.Y - 0.36 * r);
        _schichten.Add((f.Form(0), Pinselwerk.Rund(licht, r * 1.55, r * 1.55,
            (Farbton.Mischen(c, Colors.White, 0.42 * g), 0), (c, 0.5), (Farbton.Mischen(c, Colors.Black, 0.32 * g), 1))));
        var punkt = new EllipseGeometry(licht, r * 0.36, r * 0.3);
        punkt.Freeze();
        _schichten.Add((punkt, Pinselwerk.Rund(licht, r * 0.36, r * 0.3, (Farbton.Deckend(Colors.White, 0.7 * g), 0), (Farbton.Deckend(Colors.White, 0), 1))));
    }
}

/// <summary>
/// Panel der Textfeld-Vorlagen (seit 0.5.0-beta.13): das Feld (erstes Kind) und darunter die Zeilen (zweites Kind: Hinweis,
/// Balken, Fehler). Mit genug Platz wie ein Grid aus zwei Zeilen (Feld "*", Zeilen "Auto"), Pixel für Pixel gleich. Lässt eine
/// feste Höhe des Textfelds (Height) keinen Platz für die Zeilen, behält das Feld seine Höhe (mindestens seine MinHeight), und
/// die Zeilen hängen darunter über die Grenze des Textfelds hinaus, statt auf null Höhe zu schrumpfen (bis beta.12 war der
/// Fehlertext dann unsichtbar). Darunter Platz lassen oder MinHeight statt Height setzen.
/// </summary>
public sealed class Feldzeilen : Panel
{
    private (UIElement? Feld, UIElement? Zeilen) Kinder() =>
        (InternalChildren.Count > 0 ? InternalChildren[0] : null, InternalChildren.Count > 1 ? InternalChildren[1] : null);

    protected override Size MeasureOverride(Size availableSize)
    {
        var (feld, zeilen) = Kinder();
        if (feld is null) return default;
        zeilen?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var l = zeilen?.DesiredSize.Height ?? 0;
        feld.Measure(new Size(availableSize.Width, Math.Max(0, availableSize.Height - l)));
        // Nie mehr verlangen, als da ist: sonst schneidet WPF das Textfeld (und mit ihm die Zeilen) an seiner Höhe ab.
        return new Size(Math.Max(feld.DesiredSize.Width, zeilen?.DesiredSize.Width ?? 0), Math.Min(availableSize.Height, feld.DesiredSize.Height + l));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (feld, zeilen) = Kinder();
        if (feld is null) return finalSize;
        var l = zeilen?.DesiredSize.Height ?? 0;
        var mindest = feld is FrameworkElement f ? f.MinHeight : 0;
        var h = Math.Max(finalSize.Height - l, Math.Min(finalSize.Height, mindest));
        feld.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(0, h)));
        zeilen?.Arrange(new Rect(0, Math.Max(0, h), finalSize.Width, l));
        return finalSize;
    }
}

/// <summary>
/// Ein quadratischer Platz (seit 0.5.0-beta.11, für die Mulde im Knopf): so breit wie hoch, die Höhe gibt der Behälter vor;
/// das Kind füllt das Quadrat, senkrecht mittig.
/// </summary>
public sealed class Quadratplatz : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        double seite;
        if (!double.IsInfinity(constraint.Height)) seite = constraint.Height;
        else if (!double.IsInfinity(constraint.Width)) seite = constraint.Width;
        else
        {
            Child?.Measure(constraint);
            var g = Child?.DesiredSize ?? default;
            seite = Math.Max(g.Width, g.Height);
        }
        Child?.Measure(new Size(seite, seite));
        return new Size(seite, seite);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var seite = Math.Min(arrangeSize.Width, arrangeSize.Height);
        Child?.Arrange(new Rect(0, Math.Round((arrangeSize.Height - seite) / 2), seite, seite));
        return arrangeSize;
    }
}
