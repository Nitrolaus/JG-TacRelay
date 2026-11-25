// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using Funk.Client.Design;
using Funk.Client.Modelle;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Waagerechte Pegelanzeige in dBFS mit Schwellenmarke. Zeichnet selbst (OnRender) mit eingefrorenen Pinseln; im
/// Bildtakt ändert sich nur der Wert. Beim Senden wird der Balken grün, wenn es senden würde, aber nichts hinausgeht, gelb (Q43), sonst hellgrau.
/// <para>
/// Mit <see cref="Korridor"/> (Klangstufe C1, Entwurf "Korridor am Pegelbalken", Variante B): Zwei Linien im Akzent der Farbstellung ragen
/// oben und unten über den Balken hinaus, links die Mindest-Funklautstärke, rechts die Obergrenze, je mit zwei runden Griffen (oben, unten),
/// flach wie Schieberegler, ohne Glas. Dazwischen eine leicht getönte Zone; in ihr ziehen verschiebt den ganzen Korridor. Die rote Linie der
/// Automatik hat keinen Griff und bleibt im Balken. Tooltips nennen je Teil in Wörtern, was er bedeutet. Tastatur wie in der Schwellengrafik:
/// Pfeile verschieben die Mindest-Funklautstärke, Bild hoch/runter die Obergrenze, mit Strg in großen Schritten. Ist die Stimme noch nicht
/// gelernt, stehen die Linien seit 0.6.2 grau da, ohne Griffe und nicht ziehbar (Q39), mit dem Tooltip "Erst einmessen, ...".
/// </para>
/// <para>
/// Grün heißt seit 0.6.2 überall dasselbe (Q41): Es geht tatsächlich etwas hinaus; das Grün kommt aus der Farbstellung (<c>Farbe.Senden</c>). Wird
/// gesprochen, ohne dass etwas hinausgeht (Ruhe, stumm durch die Einsatzleitung, nicht verbunden), steht <see cref="Hinweis"/> in Wörtern im Balken
/// (in der schmalen Leiste seit dem Kleinkram nach 0.8.0 daneben, <see cref="HinweisImBalken"/>).
/// </para>
/// <para>
/// Drei Zustände seit 0.8.0 (Q43): grau, solange nichts auslöst; gelb (<see cref="WuerdeSenden"/>, <c>Farbe.WuerdeSenden</c> aus der Farbstellung),
/// wenn die Sprachaktivierung auslösen würde, aber nichts hinausgeht, auch ohne Kanal, denn zum Einstellen braucht man es; grün, sobald tatsächlich
/// etwas hinausgeht. Der Tooltip sagt es in Wörtern (<see cref="KorridorViewModel.HilfeBalken"/>).
/// </para>
/// </summary>
public sealed class Pegelbalken : FrameworkElement
{
    public static readonly DependencyProperty PegelDbProperty = DependencyProperty.Register(nameof(PegelDb), typeof(double), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(-90.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchwelleDbProperty = DependencyProperty.Register(nameof(SchwelleDb), typeof(double), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SendetProperty = DependencyProperty.Register(nameof(Sendet), typeof(bool), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WuerdeSendenProperty = DependencyProperty.Register(nameof(WuerdeSenden), typeof(bool), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinDbProperty = DependencyProperty.Register(nameof(MinDb), typeof(double), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(-90.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxDbProperty = DependencyProperty.Register(nameof(MaxDb), typeof(double), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KorridorProperty = DependencyProperty.Register(nameof(Korridor), typeof(KorridorViewModel), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, e) => ((Pegelbalken)d).KorridorGewechselt(e.OldValue as KorridorViewModel, e.NewValue as KorridorViewModel)));

    public static readonly DependencyProperty StummProperty = DependencyProperty.Register(nameof(Stumm), typeof(bool), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HinweisProperty = DependencyProperty.Register(nameof(Hinweis), typeof(string), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HinweisImBalkenProperty = DependencyProperty.Register(nameof(HinweisImBalken), typeof(bool), typeof(Pegelbalken),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Das Wort im Balken bei stummem Mikro (Klangstufe C2b, Q31).</summary>
    public const string StummWort = "stumm";

    /// <summary>Tooltip des Balkens bei stummem Mikro.</summary>
    public const string HilfeStumm = "Mikro stumm: Nichts wird gesendet, auch nicht mit der Sprechtaste. Wieder an mit dem Mikro-Knopf im Funkfeld oder der Taste.";

    private static readonly Typeface StummSchrift = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush StummText = Eingefroren(new SolidColorBrush(Palette.Text2));
    private static readonly Brush StummGrund = Eingefroren(new SolidColorBrush(Palette.FlaecheTief));

    /// <summary>Mit Korridor: so weit ragen die Linien über und unter den Balken (Einheiten), dort sitzen die Griffe.</summary>
    public const double Ueberstand = 9;

    /// <summary>Halbmesser der runden Griffe (Einheiten).</summary>
    public const double GriffRadius = 4.5;

    /// <summary>So nah (Einheiten) muss die Maus an einer Linie sein, um sie zu greifen.</summary>
    private const double Greifweite = 6;

    private static readonly Brush Spur = Eingefroren(new SolidColorBrush(Palette.FlaecheTief));
    private static readonly Brush Fuellung = Eingefroren(new SolidColorBrush(Palette.Text2));

    /// <summary>Die Linien des Korridors ohne bekannte Stimme (Q39): grau wie gesperrte Schrift, ohne Griffe.</summary>
    private static readonly Pen GrauStift = Eingefroren(new Pen(new SolidColorBrush(Palette.TextAus), 2));
    private static readonly Pen Rand = Eingefroren(new Pen(new SolidColorBrush(Palette.TextAus), 1));
    private static readonly Pen Teilung = Eingefroren(new Pen(new SolidColorBrush(Palette.Linie), 1));
    private static readonly Pen GriffRand = Eingefroren(new Pen(new SolidColorBrush(Palette.FlaecheTief), 1.5));

    private Teil _ziehen;
    private double _ziehenAbDb;
    private KorridorGriff _griff;

    public Pegelbalken()
    {
        MinHeight = 10;
        SnapsToDevicePixels = true;
        FocusVisualStyle = null;
        Loaded += (_, _) =>
        {
            if (Korridor is { } k) KorridorGewechselt(null, k);
        };
        Unloaded += (_, _) => KorridorGewechselt(Korridor, null);
        IsKeyboardFocusedChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Was an einer Stelle des Balkens liegt.</summary>
    internal enum Teil
    {
        Nichts,
        Balken,
        RoteLinie,
        Zone,
        Untergrenze,
        Obergrenze,
        GrauLinie,
    }

    public double PegelDb
    {
        get => (double)GetValue(PegelDbProperty);
        set => SetValue(PegelDbProperty, value);
    }

    /// <summary><c>NaN</c> blendet die Marke aus.</summary>
    public double SchwelleDb
    {
        get => (double)GetValue(SchwelleDbProperty);
        set => SetValue(SchwelleDbProperty, value);
    }

    public bool Sendet
    {
        get => (bool)GetValue(SendetProperty);
        set => SetValue(SendetProperty, value);
    }

    /// <summary>Q43: Würde senden, geht aber nirgends hin; der Balken gelb. Grün (<see cref="Sendet"/>) geht vor.</summary>
    public bool WuerdeSenden
    {
        get => (bool)GetValue(WuerdeSendenProperty);
        set => SetValue(WuerdeSendenProperty, value);
    }

    public double MinDb
    {
        get => (double)GetValue(MinDbProperty);
        set => SetValue(MinDbProperty, value);
    }

    public double MaxDb
    {
        get => (double)GetValue(MaxDbProperty);
        set => SetValue(MaxDbProperty, value);
    }

    /// <summary>Korridor der Sprachaktivierung (Klangstufe C1); ohne ihn ein schlichter Balken wie bisher.</summary>
    public KorridorViewModel? Korridor
    {
        get => (KorridorViewModel?)GetValue(KorridorProperty);
        set => SetValue(KorridorProperty, value);
    }

    /// <summary>Mikro stumm (Klangstufe C2b): kein Pegel, in der Mitte das Wort <see cref="StummWort"/>.</summary>
    public bool Stumm
    {
        get => (bool)GetValue(StummProperty);
        set => SetValue(StummProperty, value);
    }

    /// <summary>ui-fix 0.6.2: gesprochen, aber es geht nichts hinaus ("Ruhe: es wird nichts gesendet"); steht dann in Wörtern im Balken.</summary>
    public string? Hinweis
    {
        get => (string?)GetValue(HinweisProperty);
        set => SetValue(HinweisProperty, value);
    }

    /// <summary>
    /// Kleinkram nach 0.8.0: <c>false</c> zeichnet <see cref="Hinweis"/> nicht in den Balken; der Tooltip nennt ihn weiter. Für die schmale Leiste, wo
    /// der Satz in 10 Einheiten Höhe kaum lesbar war und als eigener Text daneben steht.
    /// </summary>
    public bool HinweisImBalken
    {
        get => (bool)GetValue(HinweisImBalkenProperty);
        set => SetValue(HinweisImBalkenProperty, value);
    }

    /// <summary>Anteil 0..1 eines Pegels zwischen <paramref name="min"/> und <paramref name="max"/>.</summary>
    public static double Anteil(double db, double min, double max) =>
        !double.IsFinite(db) || max <= min ? 0 : Math.Clamp((db - min) / (max - min), 0, 1);

    private void KorridorGewechselt(KorridorViewModel? alt, KorridorViewModel? neu)
    {
        if (alt is not null) alt.Aktualisiert -= InvalidateVisual;
        // Nur angemeldet, solange der Balken im Baum hängt: Das Modell lebt so lange wie das Programm.
        if (neu is not null && IsLoaded)
        {
            neu.Aktualisiert -= InvalidateVisual;
            neu.Aktualisiert += InvalidateVisual;
        }
        Focusable = neu is not null;
        InvalidateVisual();
    }

    // ---- Umrechnung ----

    /// <summary>Der Balken selbst: mit Korridor in der Mitte, oben und unten <see cref="Ueberstand"/> frei für Linien und Griffe.</summary>
    internal Rect Band => Korridor is null || ActualHeight < 2 * Ueberstand + 6
        ? new Rect(0, 0, ActualWidth, ActualHeight)
        : new Rect(0, Ueberstand, ActualWidth, ActualHeight - 2 * Ueberstand);

    /// <summary>x-Koordinate eines Pegels (wie die Schwellenmarke seit jeher).</summary>
    internal double XFuer(double db) => Math.Round(Anteil(db, MinDb, MaxDb) * (ActualWidth - 2)) + 1;

    /// <summary>x-Koordinate zurück in dBFS.</summary>
    internal double DbFuer(double x) =>
        ActualWidth <= 2 ? MinDb : MinDb + Math.Clamp((x - 1) / (ActualWidth - 2), 0, 1) * (MaxDb - MinDb);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        var b = ActualWidth;
        var band = Band;
        var h = band.Height;
        if (b < 2 || h < 2) return;
        var o = band.Top;
        var r = Math.Min(3, h / 2);
        var a = Akzente.Fuer(this);
        dc.DrawRoundedRectangle(Spur, Rand, new Rect(0.5, o + 0.5, b - 1, h - 1), r, r);
        var min = MinDb;
        var max = MaxDb;
        for (var db = Math.Ceiling(min / 10) * 10; db < max; db += 10)
        {
            var x = Math.Round(Anteil(db, min, max) * (b - 2)) + 1.5;
            dc.DrawLine(Teilung, new Point(x, o + 2), new Point(x, o + h - 2));
        }
        var k = Korridor;
        var korridor = k is { Sichtbar: true };
        var anteil = Stumm ? 0 : Anteil(PegelDb, min, max);
        if (anteil > 0) dc.DrawRoundedRectangle(Sendet ? a.Senden : WuerdeSenden ? a.WuerdeSenden : Fuellung, null, new Rect(1.5, o + 1.5, Math.Max(0, (b - 3) * anteil), h - 3), r, r);
        if (korridor)
        {
            // Zone zwischen den Linien, leicht getönt, über dem Pegel (sonst verschwände sie unter dem grünen Balken).
            var xu = XFuer(k!.UntergrenzeDb);
            var xo = XFuer(k.ObergrenzeDb);
            if (xo > xu) dc.DrawRectangle(a.ZoneFlaeche, null, new Rect(xu, o + 1, xo - xu, h - 2));
        }
        if (double.IsFinite(SchwelleDb))
        {
            var x = Math.Round(Anteil(SchwelleDb, min, max) * (b - 2)) + 1;
            // Mit Automatik am Korridor die rote Linie der Automatik, nur im Balken und ohne Griff; sonst die Marke im Akzent.
            if (k is { Automatik: true }) dc.DrawLine(a.AutomatikStift, new Point(x + 0.5, o), new Point(x + 0.5, o + h));
            else dc.DrawLine(a.Stift2, new Point(x, o), new Point(x, o + h));
        }
        if (korridor)
        {
            foreach (var db in new[] { k!.UntergrenzeDb, k.ObergrenzeDb })
            {
                var x = XFuer(db);
                dc.DrawLine(a.Stift2, new Point(x, GriffRadius), new Point(x, ActualHeight - GriffRadius));
                dc.DrawEllipse(a.Akzent, GriffRand, new Point(x, GriffRadius + 0.5), GriffRadius, GriffRadius);
                dc.DrawEllipse(a.Akzent, GriffRand, new Point(x, ActualHeight - GriffRadius - 0.5), GriffRadius, GriffRadius);
            }
        }
        else if (k is { LinienGrau: true })
        {
            // Q39: ohne bekannte Stimme grau, so weit wie die Linien mit Griffen, aber ohne Griffe.
            foreach (var db in new[] { k.LinieUntenDb, k.LinieObenDb }) dc.DrawLine(GrauStift, new Point(XFuer(db), 1), new Point(XFuer(db), ActualHeight - 1));
        }
        if (Stumm) StummZeichnen(dc, b, StummWort);
        else if (HinweisImBalken && Hinweis is { Length: > 0 } hinweis) StummZeichnen(dc, b, hinweis);
        if (IsKeyboardFocused && k is not null) dc.DrawRectangle(null, a.Stift2, new Rect(0, 0, b, ActualHeight));
    }

    /// <summary>Das Wort "stumm" (oder ein Hinweis) mitten auf dem Balken, auf dunklem Grund (über Linien und Zone lesbar; Systemschrift Segoe UI).</summary>
    private void StummZeichnen(DrawingContext dc, double breite, string wort)
    {
        var groesse = Math.Clamp(ActualHeight * 0.45, 9, 13);
        var text = new FormattedText(wort, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight, StummSchrift, groesse, StummText,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var x = Math.Round((breite - text.Width) / 2);
        var y = Math.Round((ActualHeight - text.Height) / 2);
        var grund = new Rect(x - 6, y - 1, text.Width + 12, text.Height + 2);
        dc.DrawRoundedRectangle(StummGrund, Rand, grund, 3, 3);
        dc.DrawText(text, new Point(x, y));
    }

    // ---- Maus, Tooltips, Tastatur ----

    /// <summary>Was an <paramref name="pos"/> liegt; Linien vor Zone vor roter Linie vor Balken (Tests: <see cref="TeilAn"/>).</summary>
    internal Teil Treffer(Point pos)
    {
        if (pos.X < -Greifweite || pos.X > ActualWidth + Greifweite || pos.Y < 0 || pos.Y > ActualHeight) return Teil.Nichts;
        var band = Band;
        var imBand = pos.Y >= band.Top && pos.Y <= band.Bottom;
        var k = Korridor;
        if (k is { Sichtbar: true })
        {
            var xu = XFuer(k.UntergrenzeDb);
            var xo = XFuer(k.ObergrenzeDb);
            var du = Math.Abs(pos.X - xu);
            var dob = Math.Abs(pos.X - xo);
            if (Math.Min(du, dob) <= Greifweite) return du <= dob ? Teil.Untergrenze : Teil.Obergrenze;
        }
        else if (k is { LinienGrau: true } && Math.Min(Math.Abs(pos.X - XFuer(k.LinieUntenDb)), Math.Abs(pos.X - XFuer(k.LinieObenDb))) <= Greifweite)
        {
            return Teil.GrauLinie;
        }
        // Die rote Linie hat keinen Griff; ihr schmaler Streifen erklärt nur sich selbst.
        if (k is { Automatik: true } && double.IsFinite(SchwelleDb) && imBand && Math.Abs(pos.X - XFuer(SchwelleDb)) <= 2) return Teil.RoteLinie;
        if (k is { Sichtbar: true } && pos.X > XFuer(k.UntergrenzeDb) && pos.X < XFuer(k.ObergrenzeDb)) return Teil.Zone;
        return imBand ? Teil.Balken : Teil.Nichts;
    }

    /// <summary>Für Tests: Name des Teils an <paramref name="pos"/> oder null.</summary>
    internal string? TeilAn(Point pos) => Treffer(pos) is var t && t != Teil.Nichts ? t.ToString() : null;

    /// <summary>Der Tooltip zu einem Teil, in Wörtern.</summary>
    internal string? Hilfe(Teil t) => t switch
    {
        Teil.Untergrenze => KorridorViewModel.HilfeUntergrenze,
        Teil.Obergrenze => KorridorViewModel.HilfeObergrenze,
        Teil.Zone => KorridorViewModel.HilfeZone,
        Teil.RoteLinie => KorridorViewModel.HilfeRoteLinie,
        Teil.GrauLinie => KorridorViewModel.HilfeGrau,
        Teil.Balken when Stumm => HilfeStumm,
        Teil.Balken when Hinweis is { Length: > 0 } hinweis => hinweis + ". " + KorridorViewModel.HilfeBalken,
        Teil.Balken when Korridor is { ZeigeNichtEingemessen: true } => KorridorViewModel.HilfeBalken + " " + KorridorViewModel.HilfeNichtEingemessen,
        Teil.Balken => KorridorViewModel.HilfeBalken,
        _ => null,
    };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (Korridor is not { Sichtbar: true }) return;
        Focus();
        if (!Druecken(e.GetPosition(this))) return;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        if (_ziehen == Teil.Nichts || Korridor is null)
        {
            var t = Treffer(pos);
            ToolTip = Hilfe(t);
            Cursor = t switch
            {
                Teil.Untergrenze or Teil.Obergrenze => Cursors.SizeWE,
                Teil.Zone => Cursors.SizeAll,
                _ => null,
            };
            return;
        }
        Bewegen(pos);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_ziehen == Teil.Nichts) return;
        Loslassen();
        ReleaseMouseCapture();
    }

    /// <summary>Maustaste gedrückt an <paramref name="pos"/>: greift Linie oder Zone. Falsch, wenn dort nichts zu greifen ist (Tests rufen es direkt).</summary>
    internal bool Druecken(Point pos)
    {
        if (Korridor is not { Sichtbar: true } k) return false;
        _ziehen = Treffer(pos) is var t && t is Teil.Untergrenze or Teil.Obergrenze or Teil.Zone ? t : Teil.Nichts;
        if (_ziehen == Teil.Nichts) return false;
        _ziehenAbDb = DbFuer(pos.X);
        _griff = k.Greifen();
        return true;
    }

    /// <summary>Die gedrückte Maus steht jetzt an <paramref name="pos"/> (Tests rufen es direkt, auch in vielen kleinen Schritten).</summary>
    internal void Bewegen(Point pos)
    {
        if (_ziehen == Teil.Nichts || Korridor is not { } k) return;
        var db = DbFuer(pos.X);
        switch (_ziehen)
        {
            case Teil.Untergrenze:
                k.UntergrenzeZiehen(db);
                break;
            case Teil.Obergrenze:
                k.ObergrenzeZiehen(db);
                break;
            case Teil.Zone:
                // Gegenprüfung C1, M2: ab dem Griff, nicht in Teilstücken (die verfielen unter 0,25 dB beim Runden).
                k.KorridorVerschieben(_griff, db - _ziehenAbDb);
                break;
        }
    }

    /// <summary>Maustaste los.</summary>
    internal void Loslassen() => _ziehen = Teil.Nichts;

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _ziehen = Teil.Nichts;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (Taste(e.Key, (Keyboard.Modifiers & ModifierKeys.Control) != 0)) e.Handled = true;
    }

    /// <summary>
    /// Tastatur wie in der Schwellengrafik: Pfeile die Mindest-Funklautstärke, Bild hoch/runter die Obergrenze, mit <paramref name="strg"/>
    /// fünf Schritte. Falsch, wenn die Taste nichts tut (Tests rufen es direkt).
    /// </summary>
    internal bool Taste(Key taste, bool strg)
    {
        if (Korridor is not { Sichtbar: true } k) return false;
        var schritt = KorridorViewModel.Schritt * (strg ? 5 : 1);
        switch (taste)
        {
            case Key.Right or Key.Up:
                k.UntergrenzeSchieben(schritt);
                break;
            case Key.Left or Key.Down:
                k.UntergrenzeSchieben(-schritt);
                break;
            case Key.PageUp:
                k.ObergrenzeSchieben(schritt);
                break;
            case Key.PageDown:
                k.ObergrenzeSchieben(-schritt);
                break;
            default:
                return false;
        }
        return true;
    }

    /// <summary>Für Tests ohne Maus: von <paramref name="von"/> nach <paramref name="nach"/> ziehen, wie Drücken, Bewegen, Loslassen.</summary>
    internal void Ziehen(Point von, Point nach)
    {
        if (Korridor is not { Sichtbar: true } k) return;
        var t = Treffer(von);
        var ab = DbFuer(von.X);
        var db = DbFuer(nach.X);
        switch (t)
        {
            case Teil.Untergrenze:
                k.UntergrenzeZiehen(db);
                break;
            case Teil.Obergrenze:
                k.ObergrenzeZiehen(db);
                break;
            case Teil.Zone:
                k.KorridorVerschieben(db - ab);
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private static T Eingefroren<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

/// <summary>Schmaler Balken für einen Anteil 0..1 (Pegel auf den Kacheln).</summary>
public sealed class Anteilbalken : FrameworkElement
{
    public static readonly DependencyProperty AnteilProperty = DependencyProperty.Register(nameof(Anteil), typeof(double), typeof(Anteilbalken),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FuellungProperty = DependencyProperty.Register(nameof(Fuellung), typeof(Brush), typeof(Anteilbalken),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpurProperty = DependencyProperty.Register(nameof(Spur), typeof(Brush), typeof(Anteilbalken),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Anteil
    {
        get => (double)GetValue(AnteilProperty);
        set => SetValue(AnteilProperty, value);
    }

    public Brush Fuellung
    {
        get => (Brush)GetValue(FuellungProperty);
        set => SetValue(FuellungProperty, value);
    }

    public Brush Spur
    {
        get => (Brush)GetValue(SpurProperty);
        set => SetValue(SpurProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        var b = ActualWidth;
        var h = ActualHeight;
        if (b < 1 || h < 1) return;
        var r = h / 2;
        dc.DrawRoundedRectangle(Spur, null, new Rect(0, 0, b, h), r, r);
        var a = double.IsFinite(Anteil) ? Math.Clamp(Anteil, 0, 1) : 0;
        if (a > 0) dc.DrawRoundedRectangle(Fuellung, null, new Rect(0, 0, Math.Max(h, b * a), h), r, r);
    }
}
