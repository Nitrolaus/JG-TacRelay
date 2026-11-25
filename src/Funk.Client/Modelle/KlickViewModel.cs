// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Klang.Maschine;

namespace Funk.Client.Modelle;

/// <summary>
/// Funk-Klick einstellen (A11, A63, seit 0.5.0), seit 0.10.0 unter Einstellungen > Tasten > Erweitert (seit 0.6.0 Stufe 2b als Karte im
/// Bereich Audio, bis dahin unten in der Leiste Sprachaktivierung): Klick bei der eigenen Taste und bei anderen Sprechern je an oder aus mit Lautstärke, bei
/// Sprachaktivierung dezent oder aus, "Anhören". Der Klang ist fest ein kurzes Rauschen (<see cref="Funk.Klang.Verarbeitung.FunkKlick"/>).
/// Jede Änderung geht sofort an <see cref="IKlangMaschine.Klick"/> und in die Einstellungen (<see cref="KlickEinstellungen"/>); die
/// Maschine bekommt beim Start, was gespeichert ist.
/// </summary>
public sealed partial class KlickViewModel : ObservableObject
{
    /// <summary>Was "Anhören" sagt, wenn die Wiedergabe nicht läuft.</summary>
    public const string OhneWiedergabe = "Zum Anhören muss die Wiedergabe laufen; Audio starten unter Einstellungen > Fehlersuche.";

    private readonly IKlangMaschine _maschine;
    private readonly Einstellungsablage _ablage;

    public KlickViewModel(IKlangMaschine maschine, Einstellungsablage ablage)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(ablage);
        _maschine = maschine;
        _ablage = ablage;
        Werte.Normalisieren();
        _maschine.Klick = Werte.FuerMaschine(_ablage.Aktuell.Audio.Hoerweise);
    }

    /// <summary>Über Lautsprecher spielt der eigene Klick nicht (Klangstufe C1, Q6); dann sagt die Karte das.</summary>
    public bool EigenerKlickHoerbar => _ablage.Aktuell.Audio.Hoerweise == Hoerweise.Headset;

    /// <summary>Unter dem Schalter der eigenen Taste: warum man ihn gerade nicht hört.</summary>
    public string EigenerKlickHinweis => EigenerKlickHoerbar ? ""
        : _ablage.Aktuell.Audio.Hoerweise is null ? "Erst wenn du Headset gewählt hast: Über Lautsprecher hörst du deinen eigenen Klick nicht."
        : "Über Lautsprecher hörst du deinen eigenen Klick nicht; die anderen hören den Klick weiter.";

    /// <summary>Headset oder Lautsprecher wurde umgestellt: der Maschine den passenden Klick geben.</summary>
    public void HoerweiseGeaendert()
    {
        _maschine.Klick = Werte.FuerMaschine(_ablage.Aktuell.Audio.Hoerweise);
        OnPropertyChanged(nameof(EigenerKlickHoerbar));
        OnPropertyChanged(nameof(EigenerKlickHinweis));
    }

    /// <summary>Steht über den Schaltern.</summary>
    public static string Erklaerung =>
        "Ein kurzes Rauschen wie am Funkgerät. Die Lautstärke ist auf eine Stimme bezogen: -12 dB heißt 12 dB leiser als Sprache.";

    private KlickEinstellungen Werte => _ablage.Aktuell.Klick;

    /// <summary>Klick beim Drücken und Loslassen der eigenen Sprech- oder Melde-Taste, auch am Joystick.</summary>
    public bool EigeneAn
    {
        get => Werte.EigeneAn;
        set => Aendern(w => w.EigeneAn = value, nameof(EigeneAn));
    }

    /// <summary>Lautstärke des eigenen Klicks in ganzen dB, -30 bis -6.</summary>
    public double EigeneDb
    {
        get => Werte.EigeneDb;
        set => Aendern(w => w.EigeneDb = KlickEinstellungen.Begrenzen(value, w.EigeneDb), nameof(EigeneDb), nameof(EigeneText));
    }

    public string EigeneText => Db(Werte.EigeneDb);

    /// <summary>Klick, wenn andere zu sprechen beginnen und aufhören.</summary>
    public bool AndereAn
    {
        get => Werte.AndereAn;
        set => Aendern(w => w.AndereAn = value, nameof(AndereAn));
    }

    public double AndereDb
    {
        get => Werte.AndereDb;
        set => Aendern(w => w.AndereDb = KlickEinstellungen.Begrenzen(value, w.AndereDb), nameof(AndereDb), nameof(AndereText));
    }

    public string AndereText => Db(Werte.AndereDb);

    /// <summary>Andere, die per Sprachaktivierung sprechen: dezent klicken (an) oder gar nicht (aus).</summary>
    public bool BeiSprachaktivierung
    {
        get => Werte.BeiSprachaktivierung;
        set => Aendern(w => w.BeiSprachaktivierung = value, nameof(BeiSprachaktivierung));
    }

    /// <summary>Neben dem Knopf: was "Anhören" gerade spielt oder warum nichts.</summary>
    [ObservableProperty]
    public partial string Hinweis { get; private set; } = "";

    [RelayCommand]
    private void Anhoeren()
    {
        var w = Werte;
        var eigene = w.EigeneAn && EigenerKlickHoerbar;
        if (!eigene && !w.AndereAn)
        {
            Hinweis = w.EigeneAn ? "Deine Taste klickt nur mit Headset, und der Klick anderer Sprecher ist aus." : "Beide Klicks sind aus.";
            return;
        }
        if (!_maschine.KlickAnhoeren())
        {
            Hinweis = OhneWiedergabe;
            return;
        }
        Hinweis = eigene && w.AndereAn ? "Erst deine Taste, dann andere Sprecher." : eigene ? "Deine Taste." : "Andere Sprecher.";
    }

    /// <summary>Ändert die Einstellung, gibt sie der Maschine (ein neuer Klick im Mischer) und merkt sie zum Speichern vor.</summary>
    private void Aendern(Action<KlickEinstellungen> aendern, params string[] namen)
    {
        aendern(Werte);
        Werte.Normalisieren();
        _maschine.Klick = Werte.FuerMaschine(_ablage.Aktuell.Audio.Hoerweise);
        _ablage.Vormerken();
        Hinweis = "";
        foreach (var n in namen) OnPropertyChanged(n);
    }

    private static string Db(double db) => db.ToString("0", CultureInfo.InvariantCulture) + " dB";
}
