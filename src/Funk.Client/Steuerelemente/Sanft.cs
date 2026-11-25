// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Funk.Client.Steuerelemente;

/// <summary>
/// Weiche Übergänge auf dem Brett (0.11.1, Sarevocs Punkt 4 "Ziehen mit Vorschau, sanft"): die Vorschau am freien Platz blendet ein und aus
/// (<see cref="Weichblende"/>), die abgelegte Karte gleitet vom Loslasspunkt an ihren Platz (<see cref="Gleiten"/>). Jeder Übergang ist ein
/// <see cref="Sanftlauf"/>: ein paar Uhren mit Endwert, die sich nach dem Ende selbst wegräumen. Keine Daueranimation: Nach dem Ende steht der
/// Wert als gewöhnlicher Wert, und keine Uhr hängt mehr an der Eigenschaft; was nicht läuft, zeichnet nichts neu. Die Zahlen stehen hier, damit
/// Ansicht und Tests dieselben nehmen.
/// </summary>
public static class Sanft
{
    /// <summary>Deckung der Vorschau am freien Platz (Sarevoc: "gedämpft, etwa 0,45").</summary>
    public const double VorschauDeckung = 0.45;

    /// <summary>Einblenden der Vorschau.</summary>
    public static TimeSpan Einblenden { get; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Ausblenden der Vorschau beim Verlassen der Zeile.</summary>
    public static TimeSpan Ausblenden { get; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Die Karte gleitet nach dem Loslassen in dieser Zeit an ihren Platz (Sarevoc: "etwa 150 ms").</summary>
    public static TimeSpan Landung { get; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Lässt <paramref name="element"/> von <paramref name="von"/> (in seinen eigenen Einheiten, dort stand das Ziehbild beim Loslassen) an seinen
    /// Platz gleiten und dabei von <paramref name="deckungVon"/> voll werden, abgebremst zum Ende hin; am Ende ist die Verschiebung wieder weg.
    /// </summary>
    public static Sanftlauf Gleiten(UIElement element, Vector von, double deckungVon, TimeSpan dauer)
    {
        ArgumentNullException.ThrowIfNull(element);
        // Die Anfangswerte auch als gewöhnliche Werte: Bis die Uhren beim nächsten Takt anlaufen, steht die Karte schon am Anfang.
        var schub = new TranslateTransform(von.X, von.Y);
        element.RenderTransform = schub;
        element.Opacity = deckungVon;
        var lauf = new Sanftlauf(() => element.RenderTransform = null);
        lauf.Dazu(schub, TranslateTransform.XProperty, von.X, 0, dauer);
        lauf.Dazu(schub, TranslateTransform.YProperty, von.Y, 0, dauer);
        lauf.Dazu(element, UIElement.OpacityProperty, deckungVon, 1, dauer);
        return lauf;
    }
}

/// <summary>
/// Ein laufender weicher Übergang: seine Uhren mit Endwert und ob er fertig ist. Fertig heißt: Jeder Endwert steht als gewöhnlicher Wert an
/// seiner Eigenschaft, keine Uhr hängt mehr daran. <see cref="Beenden"/> spult ans Ende (Tests, Abbruch); <see cref="Verwerfen"/> gibt die
/// Eigenschaften an einen Nachfolger ab, ohne ihre Werte anzufassen (der Nachfolger beginnt beim gerade gezeigten Wert).
/// </summary>
public sealed class Sanftlauf
{
    private readonly List<(DependencyObject Ziel, DependencyProperty Eigenschaft, AnimationClock Uhr, double Endwert)> _teile = [];
    private readonly Action? _danach;

    internal Sanftlauf(Action? danach = null) => _danach = danach;

    /// <summary>Am Ende angekommen und aufgeräumt.</summary>
    public bool Fertig { get; private set; }

    /// <summary>Von einem Nachfolger abgelöst; dieser Lauf fasst nichts mehr an.</summary>
    public bool Verworfen { get; private set; }

    /// <summary>
    /// Eine Uhr dazu: von <paramref name="von"/> (ohne: vom gerade gezeigten Wert) nach <paramref name="bis"/> in <paramref name="dauer"/>,
    /// abgebremst zum Ende hin. Die Uhr ersetzt, was an der Eigenschaft lief, und nimmt dessen Stand als Anfang.
    /// </summary>
    internal void Dazu(DependencyObject ziel, DependencyProperty eigenschaft, double? von, double bis, TimeSpan dauer)
    {
        var bewegung = von is { } v ? new DoubleAnimation(v, bis, new Duration(dauer)) : new DoubleAnimation(bis, new Duration(dauer));
        bewegung.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        bewegung.FillBehavior = FillBehavior.HoldEnd;
        bewegung.Freeze();
        var uhr = bewegung.CreateClock();
        uhr.Completed += (_, _) => Abschliessen(erzwungen: false);
        ((IAnimatable)ziel).ApplyAnimationClock(eigenschaft, uhr);
        _teile.Add((ziel, eigenschaft, uhr, bis));
    }

    /// <summary>Stellt alle Uhren auf <paramref name="zeit"/> nach ihrem Anfang und hält sie dort an (Tests: ein Zwischenstand, der nicht weiterläuft).</summary>
    internal void Spulen(TimeSpan zeit)
    {
        if (Fertig || Verworfen) return;
        foreach (var t in _teile)
        {
            t.Uhr.Controller?.Seek(zeit, TimeSeekOrigin.BeginTime);
            t.Uhr.Controller?.Pause();
        }
    }

    /// <summary>Spult ans Ende: Die Endwerte stehen, die Uhren sind weg.</summary>
    public void Beenden()
    {
        if (Fertig || Verworfen) return;
        foreach (var t in _teile) t.Uhr.Controller?.SkipToFill();
        Abschliessen(erzwungen: true);
    }

    /// <summary>Gibt die Eigenschaften ab: Die Uhren gehen aus dem Takt, die Werte bleiben, wie sie gerade stehen (für den Nachfolger).</summary>
    public void Verwerfen()
    {
        if (Fertig || Verworfen) return;
        Verworfen = true;
        foreach (var t in _teile) t.Uhr.Controller?.Remove();
    }

    /// <summary>Wenn alle Uhren am Ende sind: Endwert als gewöhnlichen Wert setzen, dann die Uhr von der Eigenschaft nehmen (kein Sprung dazwischen).</summary>
    private void Abschliessen(bool erzwungen)
    {
        if (Fertig || Verworfen) return;
        if (!erzwungen && _teile.Any(t => t.Uhr.CurrentState == ClockState.Active)) return;
        Fertig = true;
        foreach (var (ziel, eigenschaft, _, endwert) in _teile)
        {
            ziel.SetValue(eigenschaft, endwert);
            ((IAnimatable)ziel).ApplyAnimationClock(eigenschaft, null);
        }
        _danach?.Invoke();
    }
}

/// <summary>
/// Blendet ihr Kind weich ein und aus: <see cref="Sichtbar"/> an hebt die Deckung in <see cref="Sanft.Einblenden"/> auf <see cref="Deckung"/>,
/// aus senkt sie in <see cref="Sanft.Ausblenden"/> auf 0; ein Wechsel mittendrin geht vom gerade gezeigten Wert weiter. Ohne Sichtbar steht die
/// Deckung auf 0, ohne Uhr. Nicht trefferfähig, damit die Vorschau beim Ziehen kein Ziel wird. Auf dem Brett die Vorschau am freien Platz
/// (BrettAnsicht.xaml, <c>FreierPlatz</c>).
/// </summary>
public sealed class Weichblende : Decorator
{
    public static readonly DependencyProperty SichtbarProperty = DependencyProperty.Register(nameof(Sichtbar), typeof(bool), typeof(Weichblende),
        new PropertyMetadata(false, (d, _) => ((Weichblende)d).Umschalten()));

    public static readonly DependencyProperty DeckungProperty = DependencyProperty.Register(nameof(Deckung), typeof(double), typeof(Weichblende),
        new PropertyMetadata(Sanft.VorschauDeckung));

    public Weichblende()
    {
        IsHitTestVisible = false;
        Opacity = 0;
    }

    public bool Sichtbar
    {
        get => (bool)GetValue(SichtbarProperty);
        set => SetValue(SichtbarProperty, value);
    }

    /// <summary>Deckung, wenn sichtbar (ab Werk <see cref="Sanft.VorschauDeckung"/>).</summary>
    public double Deckung
    {
        get => (double)GetValue(DeckungProperty);
        set => SetValue(DeckungProperty, value);
    }

    /// <summary>Die laufende Blende, auch wenn sie fertig ist (Tests spulen vor); <c>null</c>, solange nie geschaltet wurde.</summary>
    internal Sanftlauf? Lauf { get; private set; }

    private void Umschalten()
    {
        var alt = Lauf;
        var lauf = new Sanftlauf();
        // Erst die neue Uhr (sie nimmt den gerade gezeigten Wert als Anfang), dann die alte aus dem Takt.
        lauf.Dazu(this, OpacityProperty, null, Sichtbar ? Deckung : 0, Sichtbar ? Sanft.Einblenden : Sanft.Ausblenden);
        alt?.Verwerfen();
        Lauf = lauf;
        // Eingeklappt sieht niemand eine Blende (nach dem Loslassen geht die Vorschau sofort): gleich der Endwert, keine Uhr.
        if (Visibility != Visibility.Visible) lauf.Beenden();
    }

    /// <summary>Wird die Blende mittendrin eingeklappt, springt die Uhr ans Ende: Nichts läuft an etwas, das niemand sieht.</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == VisibilityProperty && Visibility != Visibility.Visible) Lauf?.Beenden();
    }
}
