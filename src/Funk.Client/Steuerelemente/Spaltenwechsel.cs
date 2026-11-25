// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;

namespace Funk.Client.Steuerelemente;

/// <summary>Wie <see cref="Spaltenwechsel"/> die Breite auf die zwei Spalten verteilt.</summary>
public enum SpaltenAufteilung
{
    /// <summary>Beide Spalten gleich breit.</summary>
    Gleich,

    /// <summary>Die erste Spalte <see cref="Spaltenwechsel.ErsteBreite"/> breit (höchstens so breit wie Platz ist), die zweite nimmt den Rest.</summary>
    ErsteFest,

    /// <summary>
    /// Die zweite Spalte so breit wie ihr Inhalt, die erste nimmt den Rest (seit 0.10.0, AP2): ein Feld mit einem Knopf daneben, etwa "Wartet auf
    /// deine Taste ..." und "Später" auf Start; ist es zu schmal, steht der Knopf darunter.
    /// </summary>
    ZweiteNachInhalt,
}

/// <summary>
/// Zwei Spalten, die in schmalen Fenstern untereinander rutschen (seit 0.10.0, Mischung Q88): Die ersten zwei Kinder stehen nebeneinander,
/// wenn die angebotene Breite mindestens <see cref="Ab"/> ist, sonst untereinander; weitere Kinder stehen darunter über die ganze Breite. Für
/// Start ("Sprechen" und "Hören") und die Abschnitte der Einstellungen (Name und Kasten "Läuft von selbst", Senden mit zwei Spalten).
/// <para>
/// Reine Rechnung in Measure und Arrange, ohne Wandler und ohne Ressource (sonst müsste sie auch in App.xaml und im Bildlauf der Tests
/// stehen). Ob die Spalten nebeneinander stehen, entscheidet nur <see cref="MeasureOverride"/>; <see cref="ArrangeOverride"/> misst nie neu
/// (Falle aus der CODEMAP: ein Element, das beim Anordnen neu misst, lässt das Layout im Kreis laufen). Ausgeblendete Kinder zählen nicht,
/// auch nicht für die Lücken.
/// </para>
/// </summary>
public sealed class Spaltenwechsel : Panel
{
    public static readonly DependencyProperty AbProperty = DependencyProperty.Register(nameof(Ab), typeof(double), typeof(Spaltenwechsel),
        new FrameworkPropertyMetadata(600.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LueckeProperty = DependencyProperty.Register(nameof(Luecke), typeof(double), typeof(Spaltenwechsel),
        new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ZeilenlueckeProperty = DependencyProperty.Register(nameof(Zeilenluecke), typeof(double), typeof(Spaltenwechsel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty AufteilungProperty = DependencyProperty.Register(nameof(Aufteilung), typeof(SpaltenAufteilung), typeof(Spaltenwechsel),
        new FrameworkPropertyMetadata(SpaltenAufteilung.Gleich, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ErsteBreiteProperty = DependencyProperty.Register(nameof(ErsteBreite), typeof(double), typeof(Spaltenwechsel),
        new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Ab dieser angebotenen Breite stehen die ersten zwei Kinder nebeneinander, darunter untereinander.</summary>
    public double Ab
    {
        get => (double)GetValue(AbProperty);
        set => SetValue(AbProperty, value);
    }

    /// <summary>Abstand zwischen den zwei Spalten, wenn sie nebeneinander stehen.</summary>
    public double Luecke
    {
        get => (double)GetValue(LueckeProperty);
        set => SetValue(LueckeProperty, value);
    }

    /// <summary>Abstand zwischen zwei Zeilen: zwischen den Spalten, wenn sie untereinander stehen, und vor jedem weiteren Kind.</summary>
    public double Zeilenluecke
    {
        get => (double)GetValue(ZeilenlueckeProperty);
        set => SetValue(ZeilenlueckeProperty, value);
    }

    /// <summary>Wie die Breite auf die zwei Spalten verteilt wird.</summary>
    public SpaltenAufteilung Aufteilung
    {
        get => (SpaltenAufteilung)GetValue(AufteilungProperty);
        set => SetValue(AufteilungProperty, value);
    }

    /// <summary>Breite der ersten Spalte bei <see cref="SpaltenAufteilung.ErsteFest"/>.</summary>
    public double ErsteBreite
    {
        get => (double)GetValue(ErsteBreiteProperty);
        set => SetValue(ErsteBreiteProperty, value);
    }

    /// <summary>Beim letzten Messen standen die ersten zwei Kinder nebeneinander (Tests).</summary>
    public bool Nebeneinander { get; private set; }

    /// <summary>
    /// Die Breiten der zwei Spalten in <paramref name="breite"/>: gleich geteilt, die erste fest (höchstens so breit, wie nach der Lücke Platz
    /// ist) oder die zweite so breit wie <paramref name="zweiteWunsch"/> (ebenso höchstens der Platz); nie negativ.
    /// </summary>
    public static (double Erste, double Zweite) Spalten(double breite, double luecke, SpaltenAufteilung aufteilung, double ersteBreite, double zweiteWunsch = 0)
    {
        var rest = Math.Max(0, breite - Math.Max(0, luecke));
        if (aufteilung == SpaltenAufteilung.ErsteFest)
        {
            var erste = Math.Clamp(double.IsFinite(ersteBreite) ? ersteBreite : 0, 0, rest);
            return (erste, rest - erste);
        }
        if (aufteilung == SpaltenAufteilung.ZweiteNachInhalt)
        {
            var zweite = Math.Clamp(double.IsFinite(zweiteWunsch) ? zweiteWunsch : 0, 0, rest);
            return (rest - zweite, zweite);
        }
        return (rest / 2, rest / 2);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var kinder = Sichtbare();
        var breite = availableSize.Width;
        var unendlich = new Size(double.PositiveInfinity, double.PositiveInfinity);
        // Ohne Breitenvorgabe (frei gemessen) nebeneinander, sonst nach der Grenze.
        Nebeneinander = kinder.Count >= 2 && (double.IsInfinity(breite) || breite >= Ab);
        double gesamtBreite = 0, hoehe = 0;
        var start = 0;
        if (Nebeneinander)
        {
            var a = kinder[0];
            var b = kinder[1];
            if (double.IsInfinity(breite))
            {
                a.Measure(unendlich);
                b.Measure(unendlich);
                gesamtBreite = Aufteilung switch
                {
                    SpaltenAufteilung.Gleich => (2 * Math.Max(a.DesiredSize.Width, b.DesiredSize.Width)) + Luecke,
                    SpaltenAufteilung.ErsteFest => Math.Max(ErsteBreite, a.DesiredSize.Width) + Luecke + b.DesiredSize.Width,
                    _ => a.DesiredSize.Width + Luecke + b.DesiredSize.Width,
                };
            }
            else
            {
                // Nach Inhalt: erst die zweite frei messen, die erste bekommt den Rest.
                double wunsch = 0;
                if (Aufteilung == SpaltenAufteilung.ZweiteNachInhalt)
                {
                    b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    wunsch = b.DesiredSize.Width;
                }
                var (erste, zweite) = Spalten(breite, Luecke, Aufteilung, ErsteBreite, wunsch);
                a.Measure(new Size(erste, double.PositiveInfinity));
                b.Measure(new Size(zweite, double.PositiveInfinity));
                gesamtBreite = breite;
            }
            hoehe = Math.Max(a.DesiredSize.Height, b.DesiredSize.Height);
            start = 2;
        }
        for (var i = start; i < kinder.Count; i++)
        {
            var k = kinder[i];
            k.Measure(new Size(breite, double.PositiveInfinity));
            if (i > 0) hoehe += Zeilenluecke;
            hoehe += k.DesiredSize.Height;
            gesamtBreite = Math.Max(gesamtBreite, k.DesiredSize.Width);
        }
        if (double.IsFinite(breite)) gesamtBreite = Math.Min(gesamtBreite, breite);
        return new Size(gesamtBreite, hoehe);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var kinder = Sichtbare();
        var breite = finalSize.Width;
        double y = 0;
        var start = 0;
        if (Nebeneinander && kinder.Count >= 2)
        {
            var (erste, zweite) = Spalten(breite, Luecke, Aufteilung, ErsteBreite, kinder[1].DesiredSize.Width);
            var zeile = Math.Max(kinder[0].DesiredSize.Height, kinder[1].DesiredSize.Height);
            kinder[0].Arrange(new Rect(0, 0, erste, zeile));
            kinder[1].Arrange(new Rect(breite - zweite, 0, zweite, zeile));
            y = zeile;
            start = 2;
        }
        for (var i = start; i < kinder.Count; i++)
        {
            var k = kinder[i];
            if (i > 0) y += Zeilenluecke;
            k.Arrange(new Rect(0, y, breite, k.DesiredSize.Height));
            y += k.DesiredSize.Height;
        }
        // Ausgeblendete Kinder bekommen keinen Platz, aber ein Anordnen, damit WPF sie nicht als ungeordnet führt.
        foreach (UIElement k in InternalChildren)
            if (k.Visibility == Visibility.Collapsed) k.Arrange(new Rect(0, 0, 0, 0));
        return finalSize;
    }

    private List<UIElement> Sichtbare()
    {
        var liste = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement k in InternalChildren)
            if (k is not null && k.Visibility != Visibility.Collapsed) liste.Add(k);
        return liste;
    }
}
