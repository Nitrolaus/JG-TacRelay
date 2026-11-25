// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Ordnet die Kacheln des Funkfelds in Zeilen an, so viele Spalten wie in die Breite passen, und setzt das letzte Kind
/// (Global) immer rechts außen in die unterste Zeile (A23). Bei der Wahl der Spaltenzahl gewinnt die mit den wenigsten
/// leeren Plätzen, damit ein breites Fenster eine Zeile, ein schmales zwei gleich volle Zeilen zeigt.
/// <para>
/// Gemessen wird immer die Größe des Inhalts, nie das Angebot: höchstens <see cref="BevorzugteSpalten"/> Kacheln zu
/// <see cref="MindestBreite"/> nebeneinander (das Raster 2 Reihen zu 5, die Liste eine Zeile je Feld mit Global unten),
/// schmaler nur, wenn weniger Platz angeboten ist, und so hoch wie die Kacheln. Befund vom 30.09.2026 (Sarevoc, 175 %, zwei
/// Monitore): Das Raster meldete jedes endliche Angebot als eigenen Wunsch zurück. Ein Fenster mit SizeToContent gibt
/// seinem Inhalt nicht Unendlich, sondern die größte Nachführgröße von Windows (der ganze virtuelle Bildschirm, bei Sarevoc
/// 7708 x 2188 Bildpunkte), und das Funkfeld ging über beide Bildschirme auf. Mehr Platz verteilt erst
/// <see cref="ArrangeOverride"/>, etwa wenn du das Fenster am Rand größer ziehst.
/// </para>
/// </summary>
public sealed class Kachelraster : Panel
{
    public static readonly DependencyProperty MindestBreiteProperty = DependencyProperty.Register(nameof(MindestBreite), typeof(double), typeof(Kachelraster),
        new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty AbstandProperty = DependencyProperty.Register(nameof(Abstand), typeof(double), typeof(Kachelraster),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty BevorzugteSpaltenProperty = DependencyProperty.Register(nameof(BevorzugteSpalten), typeof(int), typeof(Kachelraster),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Breite einer Kachel, wenn die Breite frei ist, und zugleich die kleinste, bevor eine Spalte wegfällt.</summary>
    public double MindestBreite
    {
        get => (double)GetValue(MindestBreiteProperty);
        set => SetValue(MindestBreiteProperty, value);
    }

    public double Abstand
    {
        get => (double)GetValue(AbstandProperty);
        set => SetValue(AbstandProperty, value);
    }

    /// <summary>Spalten, wenn die Breite frei ist; 0: alle Kacheln in einer Zeile.</summary>
    public int BevorzugteSpalten
    {
        get => (int)GetValue(BevorzugteSpaltenProperty);
        set => SetValue(BevorzugteSpaltenProperty, value);
    }

    /// <summary>Breite für <paramref name="spalten"/> Kacheln zu <paramref name="kachel"/> mit <paramref name="abstand"/> dazwischen.</summary>
    public static double Breite(int spalten, double kachel, double abstand) => (spalten * kachel) + (Math.Max(0, spalten - 1) * abstand);

    /// <summary>
    /// Wie viele Kacheln nebeneinander in <paramref name="breite"/> passen (mindestens eine). Zwei Einheiten Spiel: Die
    /// Layoutrundung auf Bildpunkte (etwa 175 %) macht eine Breite, die genau für 5 Kacheln gemessen war, sonst zu 765,7
    /// statt 766, und aus 2 Reihen zu 5 würden 3 Reihen zu 4.
    /// </summary>
    public static int Passt(double breite, double kachel, double abstand) =>
        Math.Max(1, (int)Math.Floor((breite + abstand + 2) / (kachel + abstand)));

    /// <summary>
    /// Spaltenzahl für <paramref name="anzahl"/> Kacheln, wenn höchstens <paramref name="passt"/> nebeneinander passen:
    /// so wenige Zeilen wie möglich, darunter die Aufteilung mit den wenigsten leeren Plätzen, bei Gleichstand die breitere.
    /// 10 Kacheln bei Platz für 9 ergeben so 2 Zeilen zu 5 statt 9 und 1.
    /// </summary>
    public static int Spalten(int anzahl, int passt)
    {
        if (anzahl <= 0) return 1;
        passt = Math.Clamp(passt, 1, anzahl);
        var zeilenMin = (anzahl + passt - 1) / passt;
        var beste = passt;
        var besteLuecken = int.MaxValue;
        for (var s = passt; s >= 1; s--)
        {
            var zeilen = (anzahl + s - 1) / s;
            if (zeilen > zeilenMin) break;
            var luecken = (zeilen * s) - anzahl;
            if (luecken < besteLuecken)
            {
                beste = s;
                besteLuecken = luecken;
            }
        }
        return beste;
    }

    /// <summary>Platz (Zeile, Spalte) des Kindes <paramref name="index"/>; das letzte sitzt rechts unten.</summary>
    public static (int Zeile, int Spalte) Platz(int index, int anzahl, int spalten)
    {
        var zeilen = (anzahl + spalten - 1) / spalten;
        if (index == anzahl - 1) return (zeilen - 1, spalten - 1);
        return (index / spalten, index % spalten);
    }

    /// <summary>Das letzte Angebot beim Messen, für das Messwerkzeug (--funkfeld-messen) und die Tests.</summary>
    internal Size LetztesAngebot { get; private set; }

    /// <summary>Breite des Inhalts ohne Vorgabe: <see cref="BevorzugteSpalten"/> Kacheln zu <see cref="MindestBreite"/>.</summary>
    public double NatuerlicheBreite(int anzahl) =>
        anzahl <= 0 ? 0 : Breite(BevorzugteSpalten > 0 ? Math.Min(anzahl, BevorzugteSpalten) : anzahl, MindestBreite, Abstand);

    protected override Size MeasureOverride(Size availableSize)
    {
        LetztesAngebot = availableSize;
        var n = InternalChildren.Count;
        if (n == 0) return default;
        // Nie mehr als der Inhalt, auch wenn mehr angeboten ist (Befund oben); weniger nur, wenn weniger da ist.
        var breite = Math.Min(availableSize.Width, NatuerlicheBreite(n));
        var spalten = Spalten(n, Passt(breite, MindestBreite, Abstand));
        var zeilen = (n + spalten - 1) / spalten;
        var zelle = new Size(Math.Max(0, (breite - ((spalten - 1) * Abstand)) / spalten), double.PositiveInfinity);
        var hoehe = 0.0;
        foreach (UIElement kind in InternalChildren)
        {
            kind.Measure(zelle);
            hoehe = Math.Max(hoehe, kind.DesiredSize.Height);
        }
        return new Size(breite, (zeilen * hoehe) + ((zeilen - 1) * Abstand));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var n = InternalChildren.Count;
        if (n == 0) return finalSize;
        var spalten = Spalten(n, Passt(finalSize.Width, MindestBreite, Abstand));
        var zeilen = (n + spalten - 1) / spalten;
        var b = Math.Max(0, (finalSize.Width - ((spalten - 1) * Abstand)) / spalten);
        var h = Math.Max(0, (finalSize.Height - ((zeilen - 1) * Abstand)) / zeilen);
        for (var i = 0; i < n; i++)
        {
            var (z, s) = Platz(i, n, spalten);
            InternalChildren[i].Arrange(new Rect(s * (b + Abstand), z * (h + Abstand), b, h));
        }
        return finalSize;
    }
}

/// <summary>
/// Zeigt sein Kind in der Breite, die die Anordnung übrig lässt, und wünscht selbst keine. Für die Titelzeile des
/// Funkfelds: Seine optimale Größe misst das Funkfeld mit unbegrenzter Breite (FunkfeldFenster.Wunschgroesse, früher
/// SizeToContent), und ein langer Text ("Senden: Kommando, Crew, Logistik ...") zog es sonst in die Breite; die Liste wuchs
/// so von 240 auf 470 Einheiten und am rechten Bildschirmrand aus dem Bild. So bestimmen die Kacheln die Breite, und der
/// Text wird gekürzt. Wie das Kachelraster meldet es nie das Angebot als Wunsch.
/// </summary>
public sealed class Restbreite : Decorator
{
    private double _hoehe = double.NaN;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } kind) return default;
        kind.Measure(new Size(double.IsInfinity(constraint.Width) ? 0 : constraint.Width, constraint.Height));
        _hoehe = kind.DesiredSize.Height;
        return new Size(0, _hoehe);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is { } kind)
        {
            // Gemessen wurde bei freier Breite mit 0; mit der zugeteilten Breite neu messen, damit das Kürzen stimmt.
            kind.Measure(arrangeSize);
            kind.Arrange(new Rect(arrangeSize));
        }
        return arrangeSize;
    }

    /// <summary>
    /// Die eigene Wunschgröße hängt nur an der Höhe des Kinds. Eine neue Breite aus dem Messen in
    /// <see cref="ArrangeOverride"/> löst deshalb keine neue Messung aus, sonst liefe das Layout im Kreis.
    /// </summary>
    protected override void OnChildDesiredSizeChanged(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!(Math.Abs(child.DesiredSize.Height - _hoehe) <= 0.01)) base.OnChildDesiredSizeChanged(child);
    }
}
