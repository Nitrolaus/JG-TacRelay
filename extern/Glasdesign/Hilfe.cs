// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;

namespace Glasdesign;

/// <summary>
/// Hilfetexte an Steuerelementen: ein gemeinsames System statt loser ToolTips. Gleiche Namen und Regeln wie
/// Funk.Client.Design.Hilfe in JG TacRelay (Zweig modul/ui1), damit JG TacRelay ohne Bruch umsteigen kann. Drei angehängte
/// Eigenschaften, aus denen der ToolTip entsteht:
/// <list type="bullet">
/// <item><c>Hilfe.Text</c>: was das Element tut, ein bis zwei Sätze.</item>
/// <item><c>Hilfe.Taste</c>: sein Tastenkürzel, angehängt als "(Strg+F)".</item>
/// <item><c>Hilfe.Gesperrt</c>: warum es gerade nicht geht. Dann zeigt es auch gesperrt einen ToolTip (ShowOnDisabled), und
/// zwar den Grund statt des Texts.</item>
/// </list>
/// Für alle ToolTips gilt eine halbe Sekunde Verzögerung und eine lange Anzeigedauer (<see cref="Einrichten"/>, ruft
/// <see cref="Thema.Setzen(Application, string)"/> selbst auf). <see cref="Aktiv"/> schaltet alle ToolTips auf einmal ab, auch
/// die ohne Hilfe.Text. Das Aussehen steht im Stil Glas.Tooltip.
/// </summary>
public static class Hilfe
{
    /// <summary>So lange ruht die Maus, bis ein ToolTip erscheint (Millisekunden).</summary>
    public const int Verzoegerung = 500;

    /// <summary>So lange bleibt er stehen (Millisekunden); lang genug für zwei Sätze.</summary>
    public const int Anzeigedauer = 20_000;

    /// <summary>Höchstbreite der Hilfekarte (DIP); längere Texte brechen um.</summary>
    public const double Hoechstbreite = 320;

    private static bool _eingerichtet;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Hilfe), new PropertyMetadata(null, Geaendert));

    public static readonly DependencyProperty TasteProperty =
        DependencyProperty.RegisterAttached("Taste", typeof(string), typeof(Hilfe), new PropertyMetadata(null, Geaendert));

    public static readonly DependencyProperty GesperrtProperty =
        DependencyProperty.RegisterAttached("Gesperrt", typeof(string), typeof(Hilfe), new PropertyMetadata(null, Geaendert));

    /// <summary>Globaler Schalter: aus, und kein ToolTip öffnet sich mehr (ab Werk an).</summary>
    public static bool Aktiv { get; set; } = true;

    /// <summary>Derselbe Schalter unter dem Namen aus JG TacRelay ("Tooltips zeigen").</summary>
    public static bool Zeigen
    {
        get => Aktiv;
        set => Aktiv = value;
    }

    /// <summary>
    /// Verzögerung und Anzeigedauer für jedes Element und ein Ereignis für jedes Öffnen eines ToolTips, damit
    /// <see cref="Aktiv"/> auch dort greift, wo kein Hilfe.Text steht. Mehrfach aufrufbar. Hat schon jemand anders die
    /// Metadaten überschrieben (etwa eine alte eigene Hilfe-Klasse), bleiben dessen Werte stehen.
    /// </summary>
    public static void Einrichten()
    {
        if (_eingerichtet) return;
        _eingerichtet = true;
        Ueberschreiben(ToolTipService.InitialShowDelayProperty, Verzoegerung);
        Ueberschreiben(ToolTipService.ShowDurationProperty, Anzeigedauer);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent, new ToolTipEventHandler(Oeffnet));
    }

    private static void Ueberschreiben(DependencyProperty eigenschaft, int wert)
    {
        try
        {
            eigenschaft.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(wert));
        }
        catch (ArgumentException)
        {
            // Schon überschrieben; WPF erlaubt das nur einmal je Typ.
        }
    }

    public static string? GetText(DependencyObject element) => (string?)Element(element).GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? wert) => Element(element).SetValue(TextProperty, wert);

    public static string? GetTaste(DependencyObject element) => (string?)Element(element).GetValue(TasteProperty);

    public static void SetTaste(DependencyObject element, string? wert) => Element(element).SetValue(TasteProperty, wert);

    public static string? GetGesperrt(DependencyObject element) => (string?)Element(element).GetValue(GesperrtProperty);

    public static void SetGesperrt(DependencyObject element, string? wert) => Element(element).SetValue(GesperrtProperty, wert);

    private static DependencyObject Element(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element;
    }

    /// <summary>
    /// Was der ToolTip sagt: aktiv der Text mit der Taste in Klammern, gesperrt der Grund (ohne Taste), gesperrt ohne Grund
    /// wieder der Text. Ohne Text nichts, auch nicht die Taste allein; ein gesperrtes Element mit Grund sagt ihn trotzdem.
    /// </summary>
    public static string? Inhalt(string? text, string? taste, string? gesperrt, bool aktiv)
    {
        var grund = gesperrt?.Trim();
        if (!aktiv && !string.IsNullOrEmpty(grund)) return grund;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        var k = taste?.Trim();
        return string.IsNullOrEmpty(k) ? t : t + " (" + k + ")";
    }

    private static void Geaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        Einrichten();
        // Einmal je Element: IsEnabledChanged hält den ToolTip beim Sperren und Freigeben auf Stand.
        element.IsEnabledChanged -= AktivGeaendert;
        element.IsEnabledChanged += AktivGeaendert;
        Setzen(element);
    }

    private static void AktivGeaendert(object sender, DependencyPropertyChangedEventArgs e) => Setzen((UIElement)sender);

    private static void Setzen(UIElement element)
    {
        var gesperrt = GetGesperrt(element);
        ToolTipService.SetShowOnDisabled(element, !string.IsNullOrWhiteSpace(gesperrt));
        var inhalt = Inhalt(GetText(element), GetTaste(element), gesperrt, element.IsEnabled);
        if (inhalt is null) element.ClearValue(FrameworkElement.ToolTipProperty);
        else element.SetValue(FrameworkElement.ToolTipProperty, inhalt);
    }

    private static void Oeffnet(object sender, ToolTipEventArgs e)
    {
        if (!Aktiv) e.Handled = true;
    }
}
