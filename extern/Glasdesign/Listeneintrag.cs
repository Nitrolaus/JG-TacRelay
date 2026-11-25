// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Glasdesign;

/// <summary>
/// Listeneintrag (seit 0.5.0-beta.10, Stilfamilien 2 Stufe 2): ein klickbarer Eintrag einer Liste (ButtonBase). Der Titel ist
/// der Inhalt (Content), darunter wahlweise der <see cref="Untertitel"/>, links wahlweise ein <see cref="Symbol"/>, rechts
/// der Winkel (<see cref="Pfeil"/>, Vorgabe an). Steht der Eintrag in einem Panel nicht an erster Stelle, zieht er oben eine
/// Trennlinie (<see cref="Trenner"/> waagerecht). Tab erreicht ihn, Enter und Leertaste lösen ihn aus (Click, Command).
/// Automation: ein Listeneintrag mit Invoke, der Name aus Titel und Untertitel. Stile: Glas.Listeneintrag,
/// Glas.Taktil.Listeneintrag (Symbol in einer kleinen Mulde) und Glas.Dickglas.Listeneintrag (schlicht, für die
/// Glasblock-Karte).
/// </summary>
public class Listeneintrag : ButtonBase
{
    public static readonly DependencyProperty UntertitelProperty = DependencyProperty.Register(
        nameof(Untertitel), typeof(object), typeof(Listeneintrag), new FrameworkPropertyMetadata(null));

    /// <summary>Ein Glaszeichen links; Keins (Vorgabe) zeigt keines.</summary>
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(Glaszeichen), typeof(Listeneintrag), new FrameworkPropertyMetadata(Glaszeichen.Keins));

    /// <summary>Der Winkel nach rechts am Ende (Vorgabe an).</summary>
    public static readonly DependencyProperty PfeilProperty = DependencyProperty.Register(
        nameof(Pfeil), typeof(bool), typeof(Listeneintrag), new FrameworkPropertyMetadata(true));

    public object? Untertitel { get => GetValue(UntertitelProperty); set => SetValue(UntertitelProperty, value); }
    public Glaszeichen Symbol { get => (Glaszeichen)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public bool Pfeil { get => (bool)GetValue(PfeilProperty); set => SetValue(PfeilProperty, value); }

    /// <summary>Der Name für Bildschirmleser: Titel und Untertitel, durch Komma getrennt.</summary>
    public string Beschreibung
    {
        get
        {
            var titel = Rueckmeldungstext.Von(Content);
            var unter = Rueckmeldungstext.Von(Untertitel);
            return unter.Length == 0 ? titel : titel.Length == 0 ? unter : titel + ", " + unter;
        }
    }

    /// <summary>Klick über Automation (Invoke).</summary>
    internal void PerAutomation() => OnClick();

    protected override AutomationPeer OnCreateAutomationPeer() => new Listeneintragflaeche(this);
}

/// <summary>Automation eines <see cref="Listeneintrag"/>: ein Listeneintrag mit Invoke, der Name aus Titel und Untertitel.</summary>
internal sealed class Listeneintragflaeche(Listeneintrag eintrag) : ButtonBaseAutomationPeer(eintrag), IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => nameof(Listeneintrag);

    protected override string GetNameCore()
    {
        var name = AutomationProperties.GetName(eintrag);
        return string.IsNullOrEmpty(name) ? eintrag.Beschreibung : name;
    }

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

    void IInvokeProvider.Invoke()
    {
        if (!IsEnabled()) throw new ElementNotEnabledException();
        eintrag.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(eintrag.PerAutomation));
    }
}
