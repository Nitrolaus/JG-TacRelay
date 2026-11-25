// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Funk.Client.Design;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Verlaufsgrafik der Sprachaktivierung (A26-A28): die letzten 10 s mit Pegelkurve, Rauschboden, Schwellenlinie und grün
/// hinterlegten Sendeabschnitten, darunter die Sprachwahrscheinlichkeit mit der Linie der Sprachsicherheit. Einen Griff hat nur,
/// was sich ziehen lässt (Sarevoc, Stufe 2c): im Modus Fest die Linie (rund). Mit Automatik und gelernter Stimme seit Klangstufe C1 der Korridor
/// wie am Pegelbalken, hier liegend: unten die Mindest-Funklautstärke, oben die Obergrenze, je mit runden Griffen an beiden Enden, flach,
/// dazwischen die leicht getönte Zone; in ihr ziehen verschiebt beide (<see cref="KorridorViewModel"/>). Er ersetzt die Grenzlinie aus
/// Stufe 2c. Beim Lernen mit noch unbekannter Stimme gilt der Korridor von/bis aus 0.6.0 (Griffe bis und von). Die Schwelle der Automatik
/// ist eine dünne rote Linie ohne Griff, in jeder Farbstellung rot (<see cref="Akzente.AutomatikStift"/>). Tooltips je Teil in Wörtern.
/// Mit der Tastatur verschieben Pfeil hoch/runter die Linie mit dem runden Griff bzw. die Mindest-Funklautstärke (Strg: 5 dB), Bild
/// hoch/runter die Obergrenze (beim Korridor von/bis: Strg+Bild die Untergrenze).
/// <para>
/// Seit 0.6.2 (ui-fix): Die Pegelkurve ist grün, solange gesendet wird (Q41, Grün der Farbstellung), die Sendeabschnitte bleiben hinterlegt; ohne
/// bekannte Stimme stehen die Linien des Korridors grau da (Q39); wer spricht, ohne dass etwas hinausgeht, liest es oben rechts in Wörtern. Auf
/// der Seite hat die Grafik die feste Größe <see cref="SeitenGroesse"/> (Pegelfläche 600 x 260, Sprachfläche 600 x 110).
/// </para>
/// <para>
/// Seit 0.8.0 (Q43) drei Zustände der Pegelkurve: weiß, solange nichts auslöst; gelb (<see cref="Akzente.WuerdeSenden"/>, aus der Farbstellung), wo
/// die Sprachaktivierung ausgelöst hätte, aber nichts hinausging, auch ohne Kanal, denn zum Einstellen braucht man es; grün, wo gesendet wurde.
/// Die gelben Abschnitte sind wie die grünen hinterlegt, nur so hell wie diese (<see cref="Akzente.WuerdeSendenDeckkraft"/>).
/// </para>
/// <para>
/// Gezeichnet wird in ein <see cref="DrawingVisual"/> mit eingefrorenen Stiften, nur wenn neue Werte da sind, mit einem
/// wiederverwendeten Puffer für die Meldungen: keine Allokation je Meldung, eine StreamGeometry je Kurve und Bild.
/// </para>
/// </summary>
public sealed class SchwellenGrafik : FrameworkElement
{
    public static readonly DependencyProperty ModellProperty = DependencyProperty.Register(nameof(Modell), typeof(SchwellenViewModel), typeof(SchwellenGrafik),
        new FrameworkPropertyMetadata(null, (d, e) => ((SchwellenGrafik)d).ModellGewechselt(e.OldValue as SchwellenViewModel, e.NewValue as SchwellenViewModel)));

    public const double MinDb = -90;
    public const double MaxDb = 0;
    private const double RandLinks = 44;
    private const double RandRechts = 40;
    private const double RandOben = 20;
    private const double Luecke = 26;
    private const double RandUnten = 18;

    /// <summary>Feste Größe der Pegelfläche auf der Seite (ui-fix 0.6.2, Sarevoc: "fest, aber groß").</summary>
    public static Size SeitenPegel { get; } = new(600, 260);

    /// <summary>Feste Größe der Sprachfläche auf der Seite.</summary>
    public static Size SeitenSprache { get; } = new(600, 110);

    /// <summary>Die Grafik auf der Seite mit ihren Rändern: so groß, dass Pegel- und Sprachfläche genau <see cref="SeitenPegel"/> und <see cref="SeitenSprache"/> haben.</summary>
    public static Size SeitenGroesse { get; } = new(RandLinks + SeitenPegel.Width + RandRechts, RandOben + SeitenPegel.Height + Luecke + SeitenSprache.Height + RandUnten);

    /// <summary>Anteil der Pegelfläche an der Höhe beider Flächen (eine Grafik ohne feste Größe wächst in diesem Verhältnis; bis 0.9.3 unten aufgeklappt).</summary>
    private static readonly double AnteilPegel = SeitenPegel.Height / (SeitenPegel.Height + SeitenSprache.Height);
    private const double Greifweite = 7;

    /// <summary>Die Fläche der Grafik und die Hinterlegung der Sendeabschnitte: Maß für die Kontrastprüfung der Linien (Stufe35OberflaecheTests).</summary>
    internal static Color HintergrundFarbe { get; } = Palette.FlaecheTief;

    internal static Color SendenFlaecheFarbe { get; } = Color.FromArgb(0x40, Palette.Senden.R, Palette.Senden.G, Palette.Senden.B);

    /// <summary>Die Hinterlegung der Abschnitte, die gesendet hätten (Q43), wie sie <see cref="Akzente"/> aus dem Gelb der Palette baut.</summary>
    internal static Color WuerdeSendenFlaecheFarbe { get; } = Akzente.WuerdeSendenFlaecheFarbe(Palette.WuerdeSenden);

    private static readonly Brush Hintergrund = Frieren(new SolidColorBrush(HintergrundFarbe));
    private static readonly Brush WahrscheinlichkeitFlaeche = Frieren(new SolidColorBrush(Color.FromArgb(0x55, Palette.Text2.R, Palette.Text2.G, Palette.Text2.B)));
    private static readonly Brush Unterlage = Frieren(new SolidColorBrush(Color.FromArgb(0xE6, Palette.Anthrazit.R, Palette.Anthrazit.G, Palette.Anthrazit.B)));
    private static readonly Brush Beschriftung = Frieren(new SolidColorBrush(Palette.Text2));
    // Beschriftete Griffe (bis, von): Leitfarbe als Fläche mit weißer Schrift (13,3:1) und Rand im Akzent. Weiß auf dem Akzent
    // hätte nur 4,1:1 für 10-Punkt-Schrift. Der runde Griff der Linie trägt keine Schrift und steht im Akzent. Akzent,
    // Leitfarbe und rote Schrift kommen aus der Farbstellung (Akzente), der Rest aus der Palette.
    private static readonly Brush GriffSchrift = Frieren(new SolidColorBrush(Palette.Text));
    private static readonly Pen Raster = Frieren(new Pen(new SolidColorBrush(Palette.Linie), 1));
    private static readonly Pen Rahmen = Frieren(new Pen(new SolidColorBrush(Palette.TextAus), 1));
    /// <summary>Breite der Pegelkurve (Einheiten): seit 0.6.0 feiner (Sarevoc, Vorgabe o); bis 0.5.x 1,5.</summary>
    public const double PegelLinienbreite = 1.0;

    private static readonly Pen PegelStift = Frieren(new Pen(new SolidColorBrush(Palette.Text), PegelLinienbreite) { LineJoin = PenLineJoin.Round });

    /// <summary>Die Linien des Korridors ohne bekannte Stimme (Q39): grau, ohne Griffe.</summary>
    private static readonly Pen GrauStift = Frieren(new Pen(new SolidColorBrush(Palette.TextAus), 2));
    private static readonly Brush GrauSchrift = Frieren(new SolidColorBrush(Palette.Text2));
    private static readonly Pen BodenStift = Frieren(new Pen(new SolidColorBrush(Palette.TextAus), 1.5) { DashStyle = new DashStyle([3, 2], 0) });
    private static readonly Pen WahrscheinlichkeitStift = Frieren(new Pen(new SolidColorBrush(Palette.Text2), 1.2));
    private static readonly Pen GriffRand = Frieren(new Pen(new SolidColorBrush(Palette.Text), 1.5));
    private static readonly Typeface Schrift = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface SchriftFett = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly DrawingVisual _bild = new();
    private readonly PegelMeldung[] _puffer = new PegelMeldung[1024];
    private Ziehen _ziehen;

    public SchwellenGrafik()
    {
        AddVisualChild(_bild);
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        MinHeight = 200;
        SizeChanged += (_, _) => Zeichnen();
        IsKeyboardFocusedChanged += (_, _) => Zeichnen();
        // Zugeklappt (A26) wird nicht gezeichnet; beim Aufklappen einmal nachholen.
        IsVisibleChanged += (_, _) => Zeichnen();
        Unloaded += (_, _) => ModellGewechselt(Modell, null);
        Loaded += (_, _) =>
        {
            if (Modell is { } m) ModellGewechselt(null, m);
        };
    }

    private enum Ziehen
    {
        Nichts,
        Linie,
        KorridorOben,
        KorridorUnten,
        Untergrenze,
        Obergrenze,
        Zone,
    }

    private double _ziehenAbDb;

    public SchwellenViewModel? Modell
    {
        get => (SchwellenViewModel?)GetValue(ModellProperty);
        set => SetValue(ModellProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => index == 0 ? _bild : throw new ArgumentOutOfRangeException(nameof(index));

    // ---- Umrechnung ----

    private Rect PegelFlaeche
    {
        get
        {
            var hoehe = Math.Max(40, Math.Round((ActualHeight - RandOben - Luecke - RandUnten) * AnteilPegel));
            return new Rect(RandLinks, RandOben, Math.Max(10, ActualWidth - RandLinks - RandRechts), hoehe);
        }
    }

    private Rect SprachFlaeche
    {
        get
        {
            var p = PegelFlaeche;
            var oben = p.Bottom + Luecke;
            return new Rect(p.Left, oben, p.Width, Math.Max(20, ActualHeight - oben - RandUnten));
        }
    }

    /// <summary>Pegel in dBFS auf die y-Koordinate der Pegelfläche; was keine Zahl ist (NaN), steht ganz unten wie die Stille.</summary>
    public static double DbZuY(double db, Rect flaeche) =>
        flaeche.Bottom - (double.IsNaN(db) ? 0 : Math.Clamp((db - MinDb) / (MaxDb - MinDb), 0, 1) * flaeche.Height);

    /// <summary>y-Koordinate zurück in dBFS, auf 0,5 dB gerundet.</summary>
    public static double YZuDb(double y, Rect flaeche)
    {
        if (flaeche.Height <= 0 || double.IsNaN(y)) return MinDb;
        var anteil = Math.Clamp((flaeche.Bottom - y) / flaeche.Height, 0, 1);
        return Math.Round((MinDb + (anteil * (MaxDb - MinDb))) * 2, MidpointRounding.AwayFromZero) / 2;
    }

    // ---- Zeichnen ----

    private void ModellGewechselt(SchwellenViewModel? alt, SchwellenViewModel? neu)
    {
        if (alt is not null) alt.Aktualisiert -= Zeichnen;
        if (neu is not null && IsLoaded)
        {
            neu.Aktualisiert -= Zeichnen;
            neu.Aktualisiert += Zeichnen;
        }
        Zeichnen();
    }

    private void Zeichnen()
    {
        var m = Modell;
        // In einem angezeigten Fenster, aber zugeklappt (A26): nicht zeichnen, damit im Bildtakt kein Müll entsteht. Ohne
        // Fensterhandle (Rauchtest, RenderTargetBitmap) ist nichts "sichtbar"; dort wird immer gezeichnet.
        if (!IsVisible && PresentationSource.FromVisual(this) is not null) return;
        using var dc = _bild.RenderOpen();
        if (m is null || ActualWidth < 60 || ActualHeight < 80) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var p = PegelFlaeche;
        var s = SprachFlaeche;
        var a = Akzente.Fuer(this);

        dc.DrawRoundedRectangle(Hintergrund, IsKeyboardFocused ? a.Stift2 : Rahmen, p, 3, 3);
        dc.DrawRoundedRectangle(Hintergrund, Rahmen, s, 3, 3);

        // Raster und Achsen
        for (var db = -80; db <= 0; db += 20)
        {
            var y = Math.Round(DbZuY(db, p)) + 0.5;
            dc.DrawLine(Raster, new Point(p.Left, y), new Point(p.Right, y));
            Text(dc, db.ToString(CultureInfo.CurrentCulture), new Point(p.Left - 6, y), Ausrichtung.Rechts, dpi, 11, Beschriftung);
        }
        Text(dc, "dBFS", new Point(p.Left - 6, p.Top - 19), Ausrichtung.RechtsOben, dpi, 10, Beschriftung);

        var n = m.Verlauf.Kopieren(_puffer, long.MinValue);
        var jetzt = n > 0 ? _puffer[n - 1].ZeitMs : 0;
        var ab = jetzt - Klang.PegelVerlauf.FensterMs;
        var start = 0;
        while (start < n && _puffer[start].ZeitMs < ab) start++;
        double X(long ms) => p.Right - ((jetzt - ms) / (double)Klang.PegelVerlauf.FensterMs * p.Width);

        // Sendeabschnitte grün hinterlegt, Abschnitte, die gesendet hätten, gelb (Q43); über beide Flächen
        Hinterlegen(dc, a.SendenFlaeche, start, n, X, p, s, gelb: false);
        Hinterlegen(dc, a.WuerdeSendenFlaeche, start, n, X, p, s, gelb: true);

        // Korridor von/bis aus 0.6.0 (Lernend, solange die Stimme unbekannt ist)
        if (m.ZeigeKorridor)
        {
            var yBis = DbZuY(m.KorridorBisDb, p);
            var yVon = DbZuY(m.KorridorVonDb, p);
            dc.DrawRectangle(a.KorridorFlaeche, null, new Rect(p.Left, yBis, p.Width, Math.Max(0, yVon - yBis)));
            dc.DrawLine(a.KorridorStift, new Point(p.Left, yBis), new Point(p.Right, yBis));
            dc.DrawLine(a.KorridorStift, new Point(p.Left, yVon), new Point(p.Right, yVon));
            Griff(dc, p.Right + 4, yBis, "bis", dpi, a);
            Griff(dc, p.Right + 4, yVon, "von", dpi, a);
        }

        // Korridor relativ zur Stimme (Klangstufe C1): Zone zwischen Mindest-Funklautstärke und Obergrenze, die Linien kommen nach den Kurven.
        var korr = m.Korridor;
        var yOben = korr.Sichtbar ? DbZuY(korr.ObergrenzeDb, p) : double.NaN;
        var yUnten = korr.Sichtbar ? DbZuY(korr.UntergrenzeDb, p) : double.NaN;
        if (korr.Sichtbar) dc.DrawRectangle(a.ZoneFlaeche, null, new Rect(p.Left, yOben, p.Width, Math.Max(0, yUnten - yOben)));

        if (n - start >= 2)
        {
            Kurve(dc, BodenStift, start, n, X, k => DbZuY(_puffer[k].RauschbodenDb, p));
            Func<int, double> pegelY = k => DbZuY(_puffer[k].PegelDbfs, p);
            Kurve(dc, PegelStift, start, n, X, pegelY);
            // Q41: grün, solange gesendet wird; Q43: gelb, wo es gesendet hätte. Darüber gezeichnet, je Abschnitt eine Figur.
            Abschnitte(dc, a.SendenStift, start, n, X, pegelY, gelb: false);
            Abschnitte(dc, a.WuerdeSendenStift, start, n, X, pegelY, gelb: true);
            if (!m.ZeigeSchwelle) Kurve(dc, a.AutomatikStift, start, n, X, k => DbZuY(_puffer[k].SchwelleDb, p));
            Flaeche(dc, start, n, X, s);
        }

        // Schwellenlinie: im Modus Fest als Gerade mit rundem Griff. Mit Automatik nur der Wert am Ende der roten Linie, ohne Griff.
        var yLinie = DbZuY(m.LinieDb, p);
        if (m.ZeigeSchwelle)
        {
            dc.DrawLine(a.SchwellenStift, new Point(p.Left, yLinie), new Point(p.Right, yLinie));
            dc.DrawEllipse(a.Akzent, GriffRand, new Point(p.Right, yLinie), 6, 6);
        }
        // Die zwei Linien des Korridors im Akzent, mit runden Griffen an beiden Enden (wie oben und unten am Pegelbalken), flach.
        if (korr.Sichtbar)
        {
            foreach (var y in new[] { yOben, yUnten })
            {
                dc.DrawLine(a.Stift2, new Point(p.Left, y), new Point(p.Right, y));
                dc.DrawEllipse(a.Akzent, GriffRand, new Point(p.Left, y), 5, 5);
                dc.DrawEllipse(a.Akzent, GriffRand, new Point(p.Right, y), 5, 5);
            }
            Text(dc, "Obergrenze", new Point(p.Left + 10, yOben - 4), Ausrichtung.LinksUnten, dpi, 10, GriffSchrift, unterlegt: true);
            Text(dc, "Mindest-Funklautstärke", new Point(p.Left + 10, yUnten + 4), Ausrichtung.LinksOben, dpi, 10, GriffSchrift, unterlegt: true);
        }
        else if (korr.ZeigeNichtEingemessen)
        {
            // Q39: Die Linien stehen trotzdem da, grau und ohne Griffe; ziehen lassen sie sich erst nach dem Einmessen.
            var yGo = DbZuY(korr.LinieObenDb, p);
            var yGu = DbZuY(korr.LinieUntenDb, p);
            dc.DrawLine(GrauStift, new Point(p.Left, yGo), new Point(p.Right, yGo));
            dc.DrawLine(GrauStift, new Point(p.Left, yGu), new Point(p.Right, yGu));
            Text(dc, "Obergrenze", new Point(p.Left + 10, yGo - 4), Ausrichtung.LinksUnten, dpi, 10, GrauSchrift, unterlegt: true);
            Text(dc, "Mindest-Funklautstärke", new Point(p.Left + 10, yGu + 4), Ausrichtung.LinksOben, dpi, 10, GrauSchrift, unterlegt: true);
            Text(dc, KorridorViewModel.NichtEingemessen, new Point(p.Left + 8, p.Top + 6), Ausrichtung.LinksOben, dpi, 11, GriffSchrift, unterlegt: true);
        }
        // ui-fix 0.6.2: gesprochen, aber es geht nichts hinaus (Ruhe, stumm durch die Einsatzleitung, nicht verbunden).
        if (m.NichtsGesendet is { Length: > 0 } nichts)
            Text(dc, nichts, new Point(p.Right - 8, p.Top + 6), Ausrichtung.RechtsOben, dpi, 12, GriffSchrift, fett: true, unterlegt: true);
        Text(dc, m.LinieText, new Point(p.Right - 12, yLinie - 6), Ausrichtung.RechtsUnten, dpi, 11, GriffSchrift, fett: true, unterlegt: true);

        // Sprachsicherheit
        var ySicher = s.Bottom - (Math.Clamp(m.Sprachsicherheit, 0, 1) * s.Height);
        dc.DrawLine(a.SicherheitStift, new Point(s.Left, ySicher), new Point(s.Right, ySicher));
        Text(dc, "Sprache", new Point(s.Left - 6, s.Top + 1), Ausrichtung.RechtsOben, dpi, 10, Beschriftung);
        Text(dc, "ab " + (m.Sprachsicherheit * 100).ToString("0", CultureInfo.CurrentCulture) + " %", new Point(s.Left - 6, ySicher), Ausrichtung.Rechts, dpi, 10, Beschriftung);

        // Zeitachse
        foreach (var (sek, text) in Zeitmarken)
        {
            var x = p.Right - (sek / 10.0 * p.Width);
            Text(dc, text, new Point(x, s.Bottom + 3), sek == 0 ? Ausrichtung.RechtsOben : Ausrichtung.MitteOben, dpi, 10, Beschriftung);
        }
    }

    private static readonly (int Sekunden, string Text)[] Zeitmarken = [(10, "-10 s"), (5, "-5 s"), (0, "jetzt")];

    private void Kurve(DrawingContext dc, Pen stift, int start, int n, Func<long, double> x, Func<int, double> y)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x(_puffer[start].ZeitMs), y(start)), false, false);
            for (var k = start + 1; k < n; k++) c.LineTo(new Point(x(_puffer[k].ZeitMs), y(k)), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, stift, g);
    }

    /// <summary>
    /// Ob die Meldung <paramref name="k"/> zu einem Abschnitt gehört: gesendet (Q41) oder mit <paramref name="gelb"/> "würde senden, geht aber nirgends
    /// hin" (Q43). Beides schließt sich aus (<see cref="PegelMeldung.WuerdeSenden"/>).
    /// </summary>
    private bool Teil(int k, bool gelb) => gelb ? _puffer[k].WuerdeSenden : _puffer[k].Sendet;

    /// <summary>Hinterlegt die Abschnitte (<see cref="Teil"/>) über Pegel- und Sprachfläche.</summary>
    private void Hinterlegen(DrawingContext dc, Brush flaeche, int start, int n, Func<long, double> x, Rect p, Rect s, bool gelb)
    {
        var i = start;
        while (i < n)
        {
            if (!Teil(i, gelb))
            {
                i++;
                continue;
            }
            var von = i;
            while (i < n && Teil(i, gelb)) i++;
            var x0 = Math.Max(p.Left, x(_puffer[von].ZeitMs) - 1);
            var x1 = i < n ? x(_puffer[i].ZeitMs) : p.Right;
            if (x1 > x0)
            {
                dc.DrawRectangle(flaeche, null, new Rect(x0, p.Top + 1, x1 - x0, p.Height - 2));
                dc.DrawRectangle(flaeche, null, new Rect(x0, s.Top + 1, x1 - x0, s.Height - 2));
            }
        }
    }

    /// <summary>Wie <see cref="Kurve"/>, aber nur die Abschnitte (<see cref="Teil"/>), je eine Figur (Q41, Q43).</summary>
    private void Abschnitte(DrawingContext dc, Pen stift, int start, int n, Func<long, double> x, Func<int, double> y, bool gelb)
    {
        var g = new StreamGeometry();
        var leer = true;
        using (var c = g.Open())
        {
            var k = start;
            while (k < n)
            {
                if (!Teil(k, gelb))
                {
                    k++;
                    continue;
                }
                // Vom Punkt davor an, damit der Abschnitt an die weiße Kurve anschließt.
                var von = Math.Max(start, k - 1);
                c.BeginFigure(new Point(x(_puffer[von].ZeitMs), y(von)), false, false);
                for (var j = von + 1; j < n && (j == von + 1 || Teil(j - 1, gelb)); j++)
                {
                    c.LineTo(new Point(x(_puffer[j].ZeitMs), y(j)), true, false);
                    k = j + 1;
                }
                leer = false;
            }
        }
        if (leer) return;
        g.Freeze();
        dc.DrawGeometry(null, stift, g);
    }

    private void Flaeche(DrawingContext dc, int start, int n, Func<long, double> x, Rect s)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x(_puffer[start].ZeitMs), s.Bottom), true, true);
            for (var k = start; k < n; k++)
                c.LineTo(new Point(x(_puffer[k].ZeitMs), s.Bottom - (Math.Clamp(_puffer[k].Sprachwahrscheinlichkeit, 0f, 1f) * s.Height)), true, false);
            c.LineTo(new Point(x(_puffer[n - 1].ZeitMs), s.Bottom), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(WahrscheinlichkeitFlaeche, WahrscheinlichkeitStift, g);
    }

    private static void Griff(DrawingContext dc, double x, double y, string text, double dpi, Akzente a)
    {
        var r = new Rect(x, y - 9, RandRechts - 8, 18);
        dc.DrawRoundedRectangle(a.Leitfarbe, a.SchildRand, r, 3, 3);
        Text(dc, text, new Point(r.Left + (r.Width / 2), y), Ausrichtung.Mitte, dpi, 10, GriffSchrift, fett: true);
    }

    private enum Ausrichtung
    {
        Rechts,
        RechtsOben,
        RechtsUnten,
        MitteOben,
        Mitte,
        LinksOben,
        LinksUnten,
    }

    private static void Text(DrawingContext dc, string text, Point anker, Ausrichtung a, double dpi, double groesse, Brush pinsel, bool fett = false,
        bool unterlegt = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, fett ? SchriftFett : Schrift, groesse, pinsel, dpi);
        var o = a switch
        {
            Ausrichtung.Rechts => new Point(anker.X - ft.Width, anker.Y - (ft.Height / 2)),
            Ausrichtung.RechtsOben => new Point(anker.X - ft.Width, anker.Y),
            Ausrichtung.RechtsUnten => new Point(anker.X - ft.Width, anker.Y - ft.Height),
            Ausrichtung.MitteOben => new Point(anker.X - (ft.Width / 2), anker.Y),
            Ausrichtung.LinksOben => anker,
            Ausrichtung.LinksUnten => new Point(anker.X, anker.Y - ft.Height),
            _ => new Point(anker.X - (ft.Width / 2), anker.Y - (ft.Height / 2)),
        };
        if (unterlegt) dc.DrawRoundedRectangle(Unterlage, null, new Rect(o.X - 4, o.Y - 1, ft.Width + 8, ft.Height + 2), 3, 3);
        dc.DrawText(ft, o);
    }

    private static T Frieren<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    // ---- Maus und Tastatur ----

    /// <summary>Die Pegelfläche in Einheiten des Steuerelements (Tests).</summary>
    internal Rect Pegelflaeche => PegelFlaeche;

    /// <summary>Die Fläche der Sprachwahrscheinlichkeit (Tests).</summary>
    internal Rect Sprachflaeche => SprachFlaeche;

    /// <summary>Neu zeichnen (Tests und Sichtproben): Ohne Fenster kommt kein Loaded, und die Grafik hört dann nicht auf ihr Modell.</summary>
    internal void NeuZeichnen() => Zeichnen();

    /// <summary>
    /// Was an <paramref name="pos"/> zu greifen ist ("Linie", "KorridorOben", "KorridorUnten", "Untergrenze", "Obergrenze", "Zone"), sonst
    /// <c>null</c> (Tests).
    /// </summary>
    internal string? GriffAn(Point pos) => Treffer(pos) is var t && t != Ziehen.Nichts ? t.ToString() : null;

    /// <summary>Der Tooltip an <paramref name="pos"/>, in Wörtern (Tests).</summary>
    internal string? HilfeAn(Point pos) => Hilfe(pos);

    /// <summary>Nur was einen Griff hat, lässt sich greifen; die rote Linie der Automatik nie.</summary>
    private Ziehen Treffer(Point pos)
    {
        if (Modell is not { } m) return Ziehen.Nichts;
        var p = PegelFlaeche;
        if (pos.Y < p.Top - Greifweite || pos.Y > p.Bottom + Greifweite || pos.X < p.Left - Greifweite) return Ziehen.Nichts;
        if (m.ZeigeKorridor && pos.X >= p.Right - Greifweite)
        {
            if (Math.Abs(pos.Y - DbZuY(m.KorridorBisDb, p)) <= Greifweite + 2) return Ziehen.KorridorOben;
            if (Math.Abs(pos.Y - DbZuY(m.KorridorVonDb, p)) <= Greifweite + 2) return Ziehen.KorridorUnten;
        }
        if (m.ZeigeSchwelle && Math.Abs(pos.Y - DbZuY(m.SchwelleDb, p)) <= Greifweite) return Ziehen.Linie;
        if (m.Korridor is { Sichtbar: true } k && pos.X <= p.Right + Greifweite)
        {
            var yOben = DbZuY(k.ObergrenzeDb, p);
            var yUnten = DbZuY(k.UntergrenzeDb, p);
            var dOben = Math.Abs(pos.Y - yOben);
            var dUnten = Math.Abs(pos.Y - yUnten);
            if (Math.Min(dOben, dUnten) <= Greifweite) return dUnten <= dOben ? Ziehen.Untergrenze : Ziehen.Obergrenze;
            if (pos.Y > yOben && pos.Y < yUnten) return Ziehen.Zone;
        }
        return Ziehen.Nichts;
    }

    /// <summary>Tooltip je Teil (Klangstufe C1, Wörter statt dB); wo nichts liegt, die allgemeine Hilfe des Modus.</summary>
    private string? Hilfe(Point pos)
    {
        if (Modell is not { } m) return null;
        var t = Treffer(pos);
        switch (t)
        {
            case Ziehen.Untergrenze: return KorridorViewModel.HilfeUntergrenze;
            case Ziehen.Obergrenze: return KorridorViewModel.HilfeObergrenze;
            case Ziehen.Zone: return KorridorViewModel.HilfeZone;
        }
        var p = PegelFlaeche;
        // Q39: die grauen Linien vor dem Einmessen erklären, warum sie sich nicht ziehen lassen.
        if (t == Ziehen.Nichts && m.Korridor is { LinienGrau: true } g && pos.X >= p.Left - Greifweite && pos.X <= p.Right + Greifweite
            && Math.Min(Math.Abs(pos.Y - DbZuY(g.LinieObenDb, p)), Math.Abs(pos.Y - DbZuY(g.LinieUntenDb, p))) <= Greifweite)
            return KorridorViewModel.HilfeGrau;
        if (t == Ziehen.Nichts && m.Automatik && pos.X >= p.Left && pos.X <= p.Right && Math.Abs(pos.Y - DbZuY(m.LinieDb, p)) <= 3)
            return KorridorViewModel.HilfeRoteLinie;
        return m.GrafikHilfe;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (!Druecken(e.GetPosition(this))) return;
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Maustaste gedrückt an <paramref name="pos"/>: greift Linie, Griff oder Zone. Falsch, wenn dort nichts zu greifen ist (Tests rufen es direkt).</summary>
    internal bool Druecken(Point pos)
    {
        _ziehen = Treffer(pos);
        if (_ziehen == Ziehen.Nichts) return false;
        _ziehenAbDb = YZuDb(pos.Y, PegelFlaeche);
        if (Modell is { } m) _griff = m.Korridor.Greifen();
        return true;
    }

    private KorridorGriff _griff;

    /// <summary>Maustaste los (Tests rufen es direkt).</summary>
    internal void Loslassen() => _ziehen = Ziehen.Nichts;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        if (_ziehen == Ziehen.Nichts || Modell is null)
        {
            var t = Treffer(pos);
            Cursor = t switch
            {
                Ziehen.Nichts => null,
                Ziehen.Zone => Cursors.SizeAll,
                _ => Cursors.SizeNS,
            };
            // Den Tooltip nur setzen, nicht binden: Die Bindung an GrafikHilfe aus dem XAML bleibt als Grundwert.
            SetCurrentValue(ToolTipProperty, Hilfe(pos));
            return;
        }
        Bewegen(pos);
    }

    /// <summary>Die gedrückte Maus steht jetzt an <paramref name="pos"/> (Tests rufen es direkt, auch in vielen kleinen Schritten).</summary>
    internal void Bewegen(Point pos)
    {
        if (_ziehen == Ziehen.Nichts || Modell is not { } m) return;
        var db = YZuDb(pos.Y, PegelFlaeche);
        switch (_ziehen)
        {
            case Ziehen.Linie:
                m.LinieZiehen(db);
                break;
            case Ziehen.KorridorOben:
                m.KorridorZiehen(oben: true, db);
                break;
            case Ziehen.KorridorUnten:
                m.KorridorZiehen(oben: false, db);
                break;
            case Ziehen.Untergrenze:
                m.Korridor.UntergrenzeZiehen(db);
                break;
            case Ziehen.Obergrenze:
                m.Korridor.ObergrenzeZiehen(db);
                break;
            case Ziehen.Zone:
                // Gegenprüfung C1, M2: ab dem Griff, nicht in Teilstücken (die verfielen unter 0,25 dB beim Runden).
                m.Korridor.KorridorVerschieben(_griff, db - _ziehenAbDb);
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_ziehen == Ziehen.Nichts) return;
        Loslassen();
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _ziehen = Ziehen.Nichts;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (Modell is not { } m) return;
        var strg = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var schritt = strg ? 5.0 : 1.0;
        switch (e.Key)
        {
            case Key.Up when !m.ZeigeKorridor:
                m.LinieSchieben(schritt);
                break;
            case Key.Down when !m.ZeigeKorridor:
                m.LinieSchieben(-schritt);
                break;
            case Key.PageUp when m.ZeigeKorridor:
                if (strg) m.KorridorVonDb += 1;
                else m.KorridorBisDb += 1;
                break;
            case Key.PageDown when m.ZeigeKorridor:
                if (strg) m.KorridorVonDb -= 1;
                else m.KorridorBisDb -= 1;
                break;
            case Key.PageUp when m.Korridor.Sichtbar:
                m.Korridor.ObergrenzeSchieben(schritt);
                break;
            case Key.PageDown when m.Korridor.Sichtbar:
                m.Korridor.ObergrenzeSchieben(-schritt);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
