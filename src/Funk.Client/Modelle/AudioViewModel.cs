// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Client.Modelle;

/// <summary>
/// Audio: Ein- und Ausgabegerät, Start und Stopp, Headset oder Lautsprecher (beim ersten Start umrandet auf Start), Rausch- und
/// Echounterdrückung (A11, A14), automatische Lautstärke (Klangstufe A), Pegelanzeige mit Korridor, Hörprobe, Funk-Klick, Stimmproben und die
/// Klangmodule. Zeigt, wenn ein Gerät nicht aufging (etwa das Mikrofon am Datenschutz von Windows), und die Hintergrundfehler der Maschine. Bis
/// 0.9.3 war das eine eigene Seite; seit 0.10.0 (Mischung Q87) gibt es sie nicht mehr: Geräte, Headset oder Lautsprecher, "Senden mit Stimme",
/// die Funk-Lautstärke (<see cref="FunkLautstaerke"/>, Q74, E7) und die eine Hörprobe stehen auf Start, unter Einstellungen > Senden offen die
/// Klangmodule mit Pegelbalken (seit 0.11.1), alles Übrige in den Klappbereichen Erweitert von Tasten (Funk-Klick), Empfangen (Dämpfung,
/// Pegelausgleich), Senden (feste Verstärkung, Klangfarbe, Mithören) und Fehlersuche (Klangprotokoll, Stimmproben). Die Geräteliste folgt seit 0.10.0 den Geräteereignissen von Windows
/// (<see cref="IKlangMaschine.GeraeteListeGeaendert"/>); "Geräte neu lesen", "Sprechtaste halten" und "Leises Reden aussperren" sind weg (E15).
/// Die Echounterdrückung ist dreistufig (<see cref="EchoAutomatisch"/>, <see cref="EchoAn"/>, <see cref="EchoAus"/>, E11).
/// </summary>
public sealed partial class AudioViewModel : ObservableObject
{
    private readonly IKlangMaschine _maschine;
    private readonly PegelVerlauf _verlauf;
    private readonly Einstellungsablage _ablage;
    private readonly IOberflaechentakt _takt;
    private long _gesehen = -1;
    private bool? _letzteSprachaktivierung;
    private bool _laden;

    /// <param name="hoerprobe">Die Hörprobe, die auch die Sprachaktivierung zeigt; ohne Angabe eine eigene.</param>
    /// <param name="korridor">Der Korridor (Klangstufe C1), derselbe wie in der Sprachaktivierung; ohne Angabe ein eigener.</param>
    public AudioViewModel(IKlangMaschine maschine, PegelVerlauf verlauf, Einstellungsablage ablage, IOberflaechentakt? takt = null,
        HoerprobeViewModel? hoerprobe = null, KorridorViewModel? korridor = null)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(verlauf);
        ArgumentNullException.ThrowIfNull(ablage);
        _maschine = maschine;
        _verlauf = verlauf;
        _ablage = ablage;
        _takt = takt ?? SofortTakt.Instanz;
        Hoerprobe = hoerprobe ?? new HoerprobeViewModel(maschine, ablage);
        Korridor = korridor ?? new KorridorViewModel(maschine, ablage);
        Klick = new KlickViewModel(maschine, ablage);
        Lautstaerke = new LautstaerkeViewModel(maschine, ablage);
        Stimmproben = new StimmprobenViewModel(maschine, ablage);
        // Die Antwort auf "Headset oder Lautsprecher?" hat die Echounterdrückung schon beim Wählen gesetzt; eine spätere
        // Umstellung von Hand bleibt so auch über den Neustart erhalten.
        var audio = ablage.Aktuell.Audio;
        _maschine.Rauschfilter = audio.Rauschfilter;
        _maschine.Echounterdrueckung = audio.Echounterdrueckung;
        // Klangmodule (0.9.0, Q48 bis Q50): wie zuletzt geschaltet, ab Werk an.
        _maschine.Echoschutz = audio.Echoschutz;
        _maschine.MindestFunklautstaerke = audio.MindestFunklautstaerke;
        _maschine.Pegelausgleich = audio.Pegelausgleich;
        // Funk-Lautstärke (0.10.0, Q74, E7): der gemerkte Wert, ab Werk ganz rechts (unverändert laut).
        _maschine.FunkLautstaerke = AudioEinstellungen.FunkLautstaerkeFaktor(audio.FunkLautstaerke);
        // Klangfarbe (0.9.3, Q70 bis Q72): gibt der Maschine gleich den gemerkten Stand (ab Werk neutral). Seit 0.10.0 ein Teil von
        // Senden > Erweitert (E6); den Klappbereich baut das Modell der Einstellungen.
        Klangfarbe = new KlangfarbeTeil(ablage, maschine);
        // Klangstufe C2a: Auto Dauersend-Dämpfung und Stufe aus der Datei; seit 0.10.0 unter Empfangen > Erweitert.
        _maschine.Daempfung = ablage.Aktuell.Daempfung.AlsEinstellung();
        _rauschfilterStand = _maschine.Rauschfilterstand;
        _maschine.Fehler += MaschinenFehler;
        // Seit 0.10.0: Die Geräteliste folgt den Geräteereignissen (Headset eingesteckt, Gerät entfernt), auch ohne laufendes Audio; der Knopf
        // "Geräte neu lesen" entfällt. Das Ereignis kommt vom Steuer-Thread der Maschine, gelesen wird auf dem Oberflächen-Thread.
        _maschine.GeraeteListeGeaendert += () => _takt.Einreihen(GeraeteLesen);
        PegelDbfs = GateWerte.MinDb;
        SchwelleDb = GateWerte.MinDb;
        GeraeteLesen();
    }

    // ---- Erster Start: Headset oder Lautsprecher (A14) ----

    /// <summary>Die Frage "Headset oder Lautsprecher?" ist noch offen (Karte oben im Hauptfenster).</summary>
    public bool FrageOffen => _ablage.Aktuell.Audio.Hoerweise is null;

    public bool Headset
    {
        get => _ablage.Aktuell.Audio.Hoerweise == Hoerweise.Headset;
        set
        {
            if (value) HoerweiseSetzen(Hoerweise.Headset);
        }
    }

    public bool Lautsprecher
    {
        get => _ablage.Aktuell.Audio.Hoerweise == Hoerweise.Lautsprecher;
        set
        {
            if (value) HoerweiseSetzen(Hoerweise.Lautsprecher);
        }
    }

    /// <summary>Hovertext an "Headset" auf Start (seit 0.10.0 dort dauerhaft, beim ersten Start umrandet).</summary>
    public const string HeadsetHilfe = "Headset: Du hörst den Funk über Kopfhörer oder ein Headset. Dann bleibt die Echounterdrückung aus (solange sie "
        + "unter Einstellungen > Senden auf Automatisch steht), denn aus Kopfhörern kommt kein Echo ins Mikrofon, und deine Wortanfänge "
        + "kommen auch bei lautem Spielton sauber durch. Mithören und den eigenen Klick gibt es nur mit Headset. Wechselst du auf Lautsprecher, stell "
        + "es hier um.";

    /// <summary>Hovertext an "Lautsprecher" auf Start.</summary>
    public const string LautsprecherHilfe = "Lautsprecher: Du hörst den Funk über Lautsprecher, etwa am Monitor. Dann ist die Echounterdrückung an (solange "
        + "sie unter Einstellungen > Senden auf Automatisch steht), sonst kommt der Funk aus den Lautsprechern zurück ins Mikrofon, und "
        + "die anderen hören sich selbst. Mithören und der eigene Klick sind dann aus. Wechselst du auf ein Headset, stell es hier um.";

    [RelayCommand]
    private void HeadsetWaehlen() => HoerweiseSetzen(Hoerweise.Headset);

    [RelayCommand]
    private void LautsprecherWaehlen() => HoerweiseSetzen(Hoerweise.Lautsprecher);

    /// <summary>
    /// Headset: Echounterdrückung aus (AEC3 dämpft bei laufendem Spielton sonst die Anlaute der eigenen Stimme, auch ohne
    /// Echo). Lautsprecher: an, sonst hören die anderen sich selbst. Gemerkt und auf Start jederzeit änderbar. Seit 0.10.0 (E11) ändert ein
    /// Wechsel nur, was auf Automatisch steht; eine Echounterdrückung von Hand (An oder Aus unter Einstellungen > Senden) bleibt.
    /// </summary>
    private void HoerweiseSetzen(Hoerweise h)
    {
        var a = _ablage.Aktuell.Audio;
        if (a.Hoerweise == h) return;
        a.Hoerweise = h;
        a.Echounterdrueckung = a.EchoWirksam;
        _maschine.Echounterdrueckung = a.Echounterdrueckung;
        _ablage.Vormerken();
        // Mithören nur mit Headset: Mit Lautsprechern käme die eigene Stimme zurück ins Mikrofon. Ebenso der eigene Klick (Q6).
        Hoerprobe.HoerweiseGeaendert();
        Klick.HoerweiseGeaendert();
        OnPropertyChanged(nameof(FrageOffen));
        OnPropertyChanged(nameof(Headset));
        OnPropertyChanged(nameof(Lautsprecher));
        EchoMelden();
    }

    // ---- Funk-Lautstärke (0.10.0, Q74, E7): ein Regler auf Start für alles, was JG TacRelay abspielt ----

    /// <summary>Hovertext am Regler, ausführlich und ohne Zahlen wie bei den Klangmodulen.</summary>
    public const string FunkLautstaerkeHilfe = "Funk-Lautstärke: Wie laut du den ganzen Funk hörst, alle Kanäle, die Klicks und die Hörprobe zusammen. "
        + "Windows und das Spiel bleiben, wie sie sind; so stellst du den Funk gegen das Spiel ein, ohne den Mixer von Windows. Leiser, wenn dir der "
        + "Funk über dem Spiel zu laut ist oder du ihn nur nebenbei hören willst. Lauter, wenn du die anderen neben Triebwerken und Schüssen schlecht "
        + "verstehst. Einzelne Kanäle und Personen stellst du unter Einstellungen > Empfangen > Erweitert ein. Ganz links ist der Funk stumm. Ab Werk "
        + "steht der Regler ganz rechts, so laut wie bisher.";

    /// <summary>Schrittweite des Reglers in Prozent.</summary>
    public const int FunkLautstaerkeSchritt = 5;

    /// <summary>
    /// Funk-Lautstärke für den Regler auf Start, 0 bis 100 (Prozent): gemerkt und als Faktor an die Maschine
    /// (<see cref="AudioEinstellungen.FunkLautstaerkeFaktor"/>). Der Regler setzt in Schritten zu <see cref="FunkLautstaerkeSchritt"/>, ein Wert
    /// dazwischen wird gerundet.
    /// </summary>
    public double FunkLautstaerke
    {
        get => _ablage.Aktuell.Audio.FunkLautstaerke;
        set
        {
            if (!double.IsFinite(value)) return;
            var prozent = (int)(Math.Round(Math.Clamp(value, 0, 100) / FunkLautstaerkeSchritt, MidpointRounding.AwayFromZero) * FunkLautstaerkeSchritt);
            var audio = _ablage.Aktuell.Audio;
            if (prozent == audio.FunkLautstaerke) return;
            audio.FunkLautstaerkeProzent = prozent;
            _maschine.FunkLautstaerke = AudioEinstellungen.FunkLautstaerkeFaktor(prozent);
            _ablage.Vormerken();
            OnPropertyChanged();
            OnPropertyChanged(nameof(FunkLautstaerkeText));
        }
    }

    /// <summary>Neben dem Regler, etwa "80 %".</summary>
    public string FunkLautstaerkeText => _ablage.Aktuell.Audio.FunkLautstaerke.ToString(CultureInfo.CurrentCulture) + " %";

    // ---- Geräte und Fehler ----

    /// <summary>Warum das Mikrofon nicht läuft (etwa Datenschutz), oder null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeraeteWarnung), nameof(GeraeteWarnungKurz), nameof(HatGeraeteWarnung))]
    public partial string? MikrofonFehler { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeraeteWarnung), nameof(GeraeteWarnungKurz), nameof(HatGeraeteWarnung))]
    public partial string? WiedergabeFehler { get; private set; }

    /// <summary>Die ganze Warnung: was nicht aufging, warum, und was trotzdem geht (Hilfetext der Warnung auf der Startseite).</summary>
    public string? GeraeteWarnung => (MikrofonFehler, WiedergabeFehler) switch
    {
        (null, null) => null,
        ({ } m, null) => "Mikrofon aus: " + m + " Die Kanäle sind trotzdem zu hören; gesendet wird erst mit Mikrofon.",
        (null, { } w) => "Wiedergabe aus: " + w + " Senden geht, hören nicht.",
        ({ } m, { } w) => "Mikrofon aus: " + m + " Wiedergabe aus: " + w,
    };

    /// <summary>
    /// Die Warnung kurz, für die Startseite beim Mikrofon (Q23e: lange Hinweise kürzen, der Rest im Hilfetext): was aus ist und
    /// der erste Satz des Grunds, ohne die Folgen. Der Grund von Windows beim Datenschutz hat zwei Sätze (wo man es erlaubt);
    /// ganz stünde er in drei Zeilen, und die Startseite rollte (Q12, Gegenprüfung Stufe 2). Ganz steht er im Hilfetext.
    /// </summary>
    public string? GeraeteWarnungKurz => (MikrofonFehler, WiedergabeFehler) switch
    {
        (null, null) => null,
        ({ } m, null) => "Mikrofon aus: " + ErsterSatz(m),
        (null, { } w) => "Wiedergabe aus: " + ErsterSatz(w),
        _ => "Mikrofon und Wiedergabe aus.",
    };

    private static string ErsterSatz(string text)
    {
        var ende = text.IndexOf(". ", StringComparison.Ordinal);
        return ende < 0 ? text : text[..(ende + 1)];
    }

    public bool HatGeraeteWarnung => GeraeteWarnung is not null;

    /// <summary>Letzter Hintergrundfehler der Maschine mit Uhrzeit, für Einstellungen > Fehlersuche (bis 0.9.3 Bereich Audio) und die Meldezeile.</summary>
    [ObservableProperty]
    public partial string? LetzterFehler { get; private set; }

    /// <summary>Fehlende Geräte sofort neu öffnen (nach dem Erlauben im Datenschutz oder dem Einstecken).</summary>
    [RelayCommand]
    private void Wiederholen()
    {
        _maschine.GeraeteWiederholen();
        GeraeteZustandLesen();
    }

    private void MaschinenFehler(object? sender, Exception e)
    {
        var text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture) + "  " + e.Message;
        _takt.Einreihen(() => LetzterFehler = text);
    }

    private void GeraeteZustandLesen()
    {
        var z = _maschine.Geraete;
        MikrofonFehler = z.Gestartet && !z.AufnahmeLaeuft ? z.AufnahmeFehler ?? "Das Mikrofon ist nicht offen." : null;
        WiedergabeFehler = z.Gestartet && !z.WiedergabeLaeuft ? z.WiedergabeFehler ?? "Die Wiedergabe ist nicht offen." : null;
    }

    public ObservableCollection<AudioGeraet> Eingaenge { get; } = [];

    public ObservableCollection<AudioGeraet> Ausgaenge { get; } = [];

    [ObservableProperty]
    public partial AudioGeraet? Eingang { get; set; }

    [ObservableProperty]
    public partial AudioGeraet? Ausgang { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartenCommand), nameof(StoppenCommand))]
    [NotifyPropertyChangedFor(nameof(ZustandText))]
    public partial bool Laeuft { get; private set; }

    public string ZustandText => Laeuft ? "Audio läuft" : "Audio gestoppt";

    /// <summary>Warum Audio nicht startete oder sich nicht sauber stoppen ließ, sonst null; ganz unter Einstellungen > Fehlersuche > Erweitert.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HinweisKurz))]
    public partial string? Hinweis { get; private set; }

    /// <summary>
    /// <see cref="Hinweis"/> gekürzt auf den ersten Satz, für Start unter "Mikrofon und Wiedergabe sind zu." (seit 0.10.0, Befund der Prüfung; bis 0.9.3
    /// stand er im Bereich Audio unter "Audio starten"). Ganz steht er im Hilfetext daneben, gekürzt wie <see cref="GeraeteWarnungKurz"/>.
    /// </summary>
    public string? HinweisKurz => Hinweis is { } h ? ErsterSatz(h) : null;

    // ---- Rauschfilter (Klangstufe B): Standard, Stark oder Aus ----

    /// <summary>Hilfetexte der drei Knöpfe.</summary>
    public const string StandardHilfe = "Filtert gleichmäßiges Rauschen wie Lüfter aus deiner Stimme, mit wenig Rechenzeit. Ab Werk.";

    public const string StarkHilfe = "Stärkerer Filter mit größerem Modell: deine Stimme bleibt näher am Original, Musik im Hintergrund verschwindet besser. "
        + "Braucht etwa sechsmal so viel Rechenzeit und 10 ms mehr Verzögerung. Reicht die Rechenzeit nicht, schaltet JG TacRelay selbst auf Standard zurück.";

    public const string AusHilfe = "Kein Rauschfilter: Was das Mikrofon hört, geht hinaus, auch Lüfter und Tastatur.";

    /// <summary>Der gewählte Rauschfilter; gemerkt und an die Maschine gegeben.</summary>
    public Rauschfilter Rauschfilter
    {
        get => _ablage.Aktuell.Audio.Rauschfilter;
        set
        {
            if (value == _ablage.Aktuell.Audio.Rauschfilter) return;
            _ablage.Aktuell.Audio.Rauschfilter = value;
            _maschine.Rauschfilter = value;
            _ablage.Vormerken();
            OnPropertyChanged();
            OnPropertyChanged(nameof(RauschfilterStandard));
            OnPropertyChanged(nameof(RauschfilterStark));
            OnPropertyChanged(nameof(RauschfilterAus));
            OnPropertyChanged(nameof(Rauschunterdrueckung));
            RauschfilterStandLesen();
        }
    }

    public bool RauschfilterStandard
    {
        // Ausgeschaltet zeigt die Wahl, was beim Einschalten wieder gilt (gesperrt, Klangmodule 0.9.0).
        get => Rauschfilter == Rauschfilter.Standard || (Rauschfilter == Rauschfilter.Aus && !_ablage.Aktuell.Audio.StarkerRauschfilter);
        set
        {
            if (value) Rauschfilter = Rauschfilter.Standard;
        }
    }

    public bool RauschfilterStark
    {
        get => Rauschfilter == Rauschfilter.Stark || (Rauschfilter == Rauschfilter.Aus && _ablage.Aktuell.Audio.StarkerRauschfilter);
        set
        {
            if (value) Rauschfilter = Rauschfilter.Stark;
        }
    }

    public bool RauschfilterAus
    {
        get => Rauschfilter == Rauschfilter.Aus;
        set
        {
            if (value) Rauschfilter = Rauschfilter.Aus;
        }
    }

    /// <summary>Was gerade wirkt, unter den Knöpfen; bei einem Rückfall dessen Grund.</summary>
    public string RauschfilterHinweis => _rauschfilterStand switch
    {
        { Rueckfall: { } grund } => grund,
        { Gewaehlt: Rauschfilter.Stark, Wirksam: Rauschfilter.Stark } => "DeepFilterNet3 wirkt.",
        { Gewaehlt: Rauschfilter.Stark } => "DeepFilterNet3 läuft an, bis dahin wirkt Standard.",
        { Gewaehlt: Rauschfilter.Aus } => "Kein Filter.",
        _ => "Der Standardfilter wirkt.",
    };

    /// <summary>Ein neuer Rückfall von Stark auf Standard (Grund als Text), auf dem Oberflächen-Thread; die App zeigt ihn in der Meldezeile.</summary>
    public event Action<string>? RauschfilterRueckfall;

    /// <summary>
    /// Der Rückfall in der Meldezeile (App.OnStartup), seit 0.11.0 eine Zeile ohne den Grund (bis dahin "Rauschfilter: " und der ganze Grund);
    /// der Grund steht unter den Knöpfen in Einstellungen > Senden (<see cref="RauschfilterHinweis"/>).
    /// </summary>
    public const string RueckfallMeldung = "Rauschfilter: Standard statt Stark";

    private RauschfilterStand _rauschfilterStand;

    private void RauschfilterStandLesen()
    {
        var neu = _maschine.Rauschfilterstand;
        if (neu == _rauschfilterStand) return;
        var vorher = _rauschfilterStand;
        _rauschfilterStand = neu;
        OnPropertyChanged(nameof(RauschfilterHinweis));
        if (neu.Rueckfall is { } grund && vorher.Rueckfall is null) RauschfilterRueckfall?.Invoke(grund);
    }

    // ---- Auto Dauersend-Dämpfung (Klangstufe C2b; Q1, Q2, Q13, Q26): Schalter und Stufe für andere in Wörtern ----

    /// <summary>Tooltip des Schalters.</summary>
    public const string DaempfungHilfe =
        "Erkennt Dauersenden: Dein eigenes Echo, Spielton oder leises Reden im Raum geht nicht hinaus, eine klemmende Sprechtaste schaltet sich ab, "
        + "und andere Dauersender werden bei dir leiser. Ab Werk an.";

    /// <summary>Unter dem Schalter.</summary>
    public const string DaempfungErklaerung =
        "Wenn jemand dauernd sendet, ohne zu sprechen: Echo aus dem Lautsprecher, Spielton, Lärm im Raum, eine klemmende Taste. Ein gelbes Zeichen zeigt es an.";

    public const string EtwasLeiserHilfe = "Andere Dauersender werden bei dir höchstens etwas leiser: noch gut zu hören.";

    public const string DeutlichLeiserHilfe = "Andere Dauersender werden bei dir höchstens deutlich leiser: noch zu verstehen, stören aber kaum. Ab Werk.";

    public const string FastWegHilfe = "Andere Dauersender werden bei dir bis fast weg leiser: kaum noch zu hören.";

    public const string StufeGesperrt = "Geht nur mit eingeschalteter Auto Dauersend-Dämpfung.";

    /// <summary>Schalter "Auto Dauersend-Dämpfung" (Q1, ab Werk an); gemerkt und an die Maschine.</summary>
    public bool DaempfungAn
    {
        get => _ablage.Aktuell.Daempfung.Auto;
        set
        {
            if (value == _ablage.Aktuell.Daempfung.Auto) return;
            _ablage.Aktuell.Daempfung.Auto = value;
            DaempfungAnwenden();
        }
    }

    /// <summary>Stufe für andere Dauersender (Q2, Q26, ab Werk "deutlich leiser"); gemerkt und an die Maschine.</summary>
    public Daempfungsstufe Stufe
    {
        get => _ablage.Aktuell.Daempfung.Stufe;
        set
        {
            if (!Enum.IsDefined(value) || value == _ablage.Aktuell.Daempfung.Stufe) return;
            _ablage.Aktuell.Daempfung.Stufe = value;
            DaempfungAnwenden();
        }
    }

    public bool StufeEtwasLeiser
    {
        get => Stufe == Daempfungsstufe.EtwasLeiser;
        set
        {
            if (value) Stufe = Daempfungsstufe.EtwasLeiser;
        }
    }

    public bool StufeDeutlichLeiser
    {
        get => Stufe == Daempfungsstufe.DeutlichLeiser;
        set
        {
            if (value) Stufe = Daempfungsstufe.DeutlichLeiser;
        }
    }

    public bool StufeFastWeg
    {
        get => Stufe == Daempfungsstufe.FastWeg;
        set
        {
            if (value) Stufe = Daempfungsstufe.FastWeg;
        }
    }

    /// <summary>Unter den Stufen: was gerade gilt, in Wörtern.</summary>
    public string DaempfungText => DaempfungAn
        ? "Wer dauernd sendet, ohne zu sprechen, wird bei dir schrittweise leiser, höchstens " + Daempfungsworte.Wort(Stufe)
            + ". Am gelben Zeichen an seinem Namen kannst du ihn stumm schalten oder normal lassen."
        : "Aus: Niemand wird leiser gestellt, und dein eigenes Echo wird nicht zurückgehalten. Das gelbe Zeichen zeigt Dauersender weiter an.";

    private void DaempfungAnwenden()
    {
        _maschine.Daempfung = _ablage.Aktuell.Daempfung.AlsEinstellung();
        _ablage.Vormerken();
        OnPropertyChanged(nameof(DaempfungAn));
        OnPropertyChanged(nameof(Stufe));
        OnPropertyChanged(nameof(StufeEtwasLeiser));
        OnPropertyChanged(nameof(StufeDeutlichLeiser));
        OnPropertyChanged(nameof(StufeFastWeg));
        OnPropertyChanged(nameof(DaempfungText));
    }

    /// <summary>Mikro stumm (Klangstufe C2b, Q31): Der Pegelbalken zeigt "stumm" statt des Pegels.</summary>
    [ObservableProperty]
    public partial bool Stumm { get; private set; }

    /// <summary>
    /// Die Echounterdrückung, die gilt (A14): folgt Headset oder Lautsprecher, lässt sich aber von Hand umstellen. Setzen heißt seit 0.10.0 "von
    /// Hand" (<see cref="EchoAn"/> oder <see cref="EchoAus"/>, E11).
    /// </summary>
    public bool Echounterdrueckung
    {
        get => _ablage.Aktuell.Audio.Echounterdrueckung;
        set => EchoSetzen(value);
    }

    /// <summary>Echounterdrückung von Hand: <c>null</c> heißt Automatisch (E11).</summary>
    public bool? EchoVonHand
    {
        get => _ablage.Aktuell.Audio.EchounterdrueckungVonHand;
        set => EchoSetzen(value);
    }

    /// <summary>Echounterdrückung Automatisch (ab Werk, E11): folgt Headset oder Lautsprecher auf Start.</summary>
    public bool EchoAutomatisch
    {
        get => EchoVonHand is null;
        set
        {
            if (value) EchoSetzen(null);
        }
    }

    /// <summary>Echounterdrückung immer an, egal was auf Start gewählt ist.</summary>
    public bool EchoAn
    {
        get => EchoVonHand == true;
        set
        {
            if (value) EchoSetzen(true);
        }
    }

    /// <summary>Echounterdrückung immer aus, egal was auf Start gewählt ist.</summary>
    public bool EchoAus
    {
        get => EchoVonHand == false;
        set
        {
            if (value) EchoSetzen(false);
        }
    }

    /// <summary>Unter den drei Knöpfen: was Automatisch heißt und was gerade gilt.</summary>
    public string EchoHinweis => (EchoVonHand, Echounterdrueckung) switch
    {
        (null, var an) => "Automatisch folgt sie Headset oder Lautsprecher auf Start: mit Headset aus, mit Lautsprechern an. Gerade " + (an ? "an." : "aus."),
        (true, _) => "Immer an, egal was auf Start gewählt ist.",
        _ => "Immer aus, egal was auf Start gewählt ist. Über Lautsprecher hören die anderen sich dann selbst.",
    };

    private void EchoSetzen(bool? vonHand)
    {
        var a = _ablage.Aktuell.Audio;
        if (vonHand == a.EchounterdrueckungVonHand) return;
        a.EchounterdrueckungVonHand = vonHand;
        a.Echounterdrueckung = a.EchoWirksam;
        _maschine.Echounterdrueckung = a.Echounterdrueckung;
        _ablage.Vormerken();
        EchoMelden();
    }

    private void EchoMelden()
    {
        OnPropertyChanged(nameof(Echounterdrueckung));
        OnPropertyChanged(nameof(EchoVonHand));
        OnPropertyChanged(nameof(EchoAutomatisch));
        OnPropertyChanged(nameof(EchoAn));
        OnPropertyChanged(nameof(EchoAus));
        OnPropertyChanged(nameof(EchoHinweis));
    }

    // ---- Klangmodule (0.9.0, Sarevoc 06.10.2026, Q48 bis Q52): je ein Schiebeschalter, ab Werk an, danach wie zuletzt geschaltet ----

    public const string SendenHilfe = "Senden: alles, was mit deinem Mikrofon passiert, bevor deine Stimme bei den anderen ankommt. Deine Stimme geht "
        + "der Reihe nach durch diese Stationen: Erst nimmt die Echounterdrückung den Klang deiner Lautsprecher heraus, dann gleicht die automatische "
        + "Lautstärke deine Stimme an, dann filtert die Rauschunterdrückung die Geräusche heraus, dann bekommt deine Stimme ihre Klangfarbe, und "
        + "zuletzt entscheidet die Sprachaktivierung, ob gesendet wird. Unter Einstellungen > Senden stehen sie nicht ganz in dieser "
        + "Reihenfolge: links Echo und Rauschunterdrückung, rechts Lautstärke und Sprachaktivierung, darunter die Funkschwelle mit dem "
        + "Pegelbalken, die Klangfarbe unter Erweitert. Jedes Modul lässt sich einzeln ausschalten. Ab Werk "
        + "sind alle an und die Klangfarbe ist neutral, danach bleibt jeder Schalter so, wie du ihn zuletzt gestellt hast. Klingt etwas komisch, "
        + "schalte immer nur ein Modul aus und mach eine Hörprobe auf Start.";

    public const string EmpfangenHilfe = "Empfangen: was mit den Stimmen der anderen passiert, bevor du sie hörst. Das betrifft nur, was bei dir "
        + "ankommt; was die anderen senden oder hören, bleibt unverändert. Ab Werk sind beide Module an, danach bleibt jeder Schalter so, wie du "
        + "ihn zuletzt gestellt hast.";

    public const string EchounterdrueckungErklaerung = "Nimmt den Klang deiner Lautsprecher aus dem Mikrofon.";

    public const string EchounterdrueckungHilfe = "Echounterdrückung: Nimmt den Klang deiner Lautsprecher aus dem Mikrofon, damit die anderen sich "
        + "nicht selbst als Echo hören. Mit Lautsprechern ist sie Pflicht. Mit Headset kannst du sie ausschalten: Dann klingen deine Wortanfänge "
        + "bei lautem Spielton sauberer, weil nichts abgezogen wird, was gar nicht da ist. Ab Werk steht sie auf Automatisch und folgt dem, was du "
        + "auf Start unter \"Ich höre mit\" gewählt hast.";

    /// <summary>Hovertext an "Automatisch" (E11).</summary>
    public const string EchoAutomatischHilfe = "Echounterdrückung Automatisch: Sie folgt dem, was du auf Start unter \"Ich höre mit\" wählst. Mit Headset ist "
        + "sie aus, denn aus Kopfhörern kommt kein Echo ins Mikrofon, und deine Wortanfänge bleiben bei lautem Spielton sauber. Über Lautsprecher ist sie "
        + "an, sonst hören die anderen sich selbst. Wechselst du dort, stellt sie sich mit um. Ab Werk; für fast alle das Richtige.";

    /// <summary>Hovertext an "An" (E11).</summary>
    public const string EchoAnHilfe = "Echounterdrückung immer an, auch mit Headset. Nimm das, wenn die anderen dich trotz Headset als Echo hören, etwa "
        + "weil dein Headset sehr laut ist oder das Mikrofon den Kopfhörer aufnimmt. Kostet bei lautem Spielton etwas von deinen Wortanfängen. Zurück "
        + "auf Automatisch, wenn das Echo weg ist.";

    /// <summary>Hovertext an "Aus" (E11).</summary>
    public const string EchoAusHilfe = "Echounterdrückung immer aus, auch über Lautsprecher. Nur sinnvoll mit Headset; über Lautsprecher hören die "
        + "anderen sich dann selbst als Echo. Nimm lieber Automatisch und wähle auf Start Headset, das schaltet sie dort ebenso aus.";

    public const string EchoschutzErklaerung = "Das Echo der anderen öffnet deine Sprachaktivierung nicht.";

    public const string EchoschutzHilfe = "Echoschutz: Auch mit Echounterdrückung bleibt ein kleiner Rest vom Echo im Mikrofon, und dieser Rest ist "
        + "Sprache, nämlich die Stimme der anderen. Der Echoschutz sorgt dafür, dass deine Sprachaktivierung darauf nicht anspringt: Solange die "
        + "anderen reden, musst du etwas lauter sein als ihr Echo, um zu senden. Eine Sprechphase, die schon läuft, bleibt offen. Aus: Mit "
        + "Lautsprechern sendet deine Sprachaktivierung die anderen zurück, und es entsteht eine Schleife. Nur mit Headset ausschalten, etwa wenn dir "
        + "Wortanfänge fehlen, während andere reden. Die Sprechtaste betrifft er nie.";

    public const string EchoschutzWarnung = "Aus: Mit Lautsprechern sendest du die anderen zurück. Nur mit Headset ausschalten.";

    public const string RauschunterdrueckungErklaerung = "Filtert Lüfter, Tastatur und Rauschen.";

    public const string RauschunterdrueckungHilfe = "Rauschunterdrückung: Filtert Lüfter, Tastatur, Klicks und Rauschen aus deinem Mikrofon, bevor "
        + "gesendet wird. Standard (RNNoise) ist leicht und reicht fast immer. Stark (DeepFilterNet3) filtert gründlicher, braucht aber mehr "
        + "Rechenzeit; fehlt sie, wirkt von selbst wieder Standard. Aus: Was das Mikrofon hört, geht hinaus, auch Geräusche, und die "
        + "Sprachaktivierung springt leichter auf Geräusche an. Beim Wiedereinschalten gilt wieder deine Wahl Standard oder Stark.";

    public const string AutomatikErklaerung = "Hält deine Stimme am Funk gleich laut.";

    public const string AutomatikHilfe = "Automatische Lautstärke: Gleicht die Grundlautstärke deiner Stimme an, damit du bei den anderen immer "
        + "gleich laut ankommst, auch wenn das Mikrofon mal näher und mal weiter weg ist. Sie ändert nur in Sprechpausen und in kleinen Schritten; "
        + "mitten im Satz wird nichts lauter oder leiser, ein Ruf bleibt ein Ruf. Aus: Es gilt eine feste Verstärkung, die du unter Erweitert einmisst. "
        + "Das passt, wenn dein Mikrofon immer gleich weit weg ist und du alles selbst bestimmen willst.";

    public const string SprachaktivierungErklaerung = "Sendet, sobald du sprichst. Aus: nur mit Taste.";

    public const string SprachaktivierungHilfe = "Sprachaktivierung: Sendet, sobald du sprichst, ohne dass du eine Taste drückst. Ob ein Kanal mit "
        + "Sprachaktivierung oder mit Taste sendet, stellst du im Funkfeld je Kanal mit VA oder Taste ein. Aus: Alle Kanäle senden nur noch mit der "
        + "Sprechtaste, egal was im Funkfeld steht; die Schalter VA dort sind dann gesperrt. Mit der Sprechtaste geht auch ein kurzer Ruf wie "
        + "\"Hit\" immer sofort hinaus.";

    public const string MindestErklaerung = "Leises Reden im Raum geht nicht raus. Wirkt nach dem Einmessen.";

    public const string MindestHilfe = "Mindest-Funklautstärke: Was du leise sagst, etwa zu jemandem im Raum, geht nicht hinaus. Eine Sprechphase "
        + "wird erst gesendet, wenn ihr Anfang so laut ist wie deine Mindest-Funklautstärke; danach bleibt sie offen, auch wenn einzelne Silben "
        + "leiser werden. Sie wirkt erst, wenn deine Stimme bekannt ist: nach \"Mindestlautstärke einmessen\" oder nach einer Weile Funk. Wie weit "
        + "sie unter deiner Stimme liegt, stellst du mit dem linken Griff am Pegelbalken ein. Aus: Jede erkannte Sprache über der Linie geht "
        + "hinaus, auch leise.";

    public const string PegelausgleichErklaerung = "Gleicht zu laute Funker langsam an, nur leiser.";

    public const string PegelausgleichHilfe = "Pegelausgleich: Manche senden viel zu laut, etwa mit aufgedrehtem Mikrofon. Der Pegelausgleich macht "
        + "solche Funker bei dir langsam leiser: nach jeder Äußerung nur ein kleines Stück, insgesamt nur begrenzt, und nie lauter. Ein "
        + "einzelner Schrei zählt nicht. Was sie senden und was die anderen hören, bleibt unverändert. Aus: Du hörst jeden so laut, wie er sendet; "
        + "die Lautstärke je Kanal stellst du weiter im Funkfeld ein.";

    /// <summary>Rauschunterdrückung an (Standard oder Stark, wie zuletzt gewählt) oder aus; aus merkt sich die Wahl Stark.</summary>
    public bool Rauschunterdrueckung
    {
        get => Rauschfilter != Rauschfilter.Aus;
        set
        {
            if (value == Rauschunterdrueckung) return;
            var audio = _ablage.Aktuell.Audio;
            var stark = audio.StarkerRauschfilter;
            Rauschfilter = value ? (stark ? Rauschfilter.Stark : Rauschfilter.Standard) : Rauschfilter.Aus;
            audio.StarkerRauschfilter = stark;
            _ablage.Vormerken();
        }
    }

    /// <summary>Klangmodul Echoschutz (A14): Das Echo der Lautsprecher öffnet die Sprachaktivierung nicht.</summary>
    public bool Echoschutz
    {
        get => _ablage.Aktuell.Audio.Echoschutz;
        set
        {
            if (value == _ablage.Aktuell.Audio.Echoschutz) return;
            _ablage.Aktuell.Audio.Echoschutz = value;
            _maschine.Echoschutz = value;
            _ablage.Vormerken();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Klangmodul Sprachaktivierung: aus heißt, jedes Feld sendet nur mit Taste. Das Funkfeld liest den Schalter (A10); das Hauptmodell
    /// meldet ihm die Änderung.
    /// </summary>
    public bool SprachaktivierungAn
    {
        get => _ablage.Aktuell.Audio.Sprachaktivierung;
        set
        {
            if (value == _ablage.Aktuell.Audio.Sprachaktivierung) return;
            _ablage.Aktuell.Audio.Sprachaktivierung = value;
            _ablage.Vormerken();
            OnPropertyChanged();
        }
    }

    /// <summary>Klangmodul Mindest-Funklautstärke (Klangstufe C1).</summary>
    public bool MindestFunklautstaerke
    {
        get => _ablage.Aktuell.Audio.MindestFunklautstaerke;
        set
        {
            if (value == _ablage.Aktuell.Audio.MindestFunklautstaerke) return;
            _ablage.Aktuell.Audio.MindestFunklautstaerke = value;
            _maschine.MindestFunklautstaerke = value;
            _ablage.Vormerken();
            OnPropertyChanged();
        }
    }

    /// <summary>Klangmodul Pegelausgleich beim Empfänger (Q29), unabhängig von der Auto Dauersend-Dämpfung.</summary>
    public bool Pegelausgleich
    {
        get => _ablage.Aktuell.Audio.Pegelausgleich;
        set
        {
            if (value == _ablage.Aktuell.Audio.Pegelausgleich) return;
            _ablage.Aktuell.Audio.Pegelausgleich = value;
            _maschine.Pegelausgleich = value;
            _ablage.Vormerken();
            OnPropertyChanged();
        }
    }


    /// <summary>Hörprobe als Aufnahme (geht immer) und Mithören beim Senden (nur mit Headset).</summary>
    public HoerprobeViewModel Hoerprobe { get; }

    /// <summary>Korridor am Pegelbalken und "Mindestlautstärke einmessen" (Klangstufe C1), auf der Startseite und in diesem Bereich.</summary>
    public KorridorViewModel Korridor { get; }

    /// <summary>Funk-Klick (A63), eigene Karte in diesem Bereich; stellt beim Anlegen die Maschine ein.</summary>
    public KlickViewModel Klick { get; }

    /// <summary>Stimmproben für Klangmessungen (Q60, seit 0.9.2), eigene Karte in diesem Bereich.</summary>
    public StimmprobenViewModel Stimmproben { get; }

    /// <summary>Klangfarbe (Q70 bis Q72, seit 0.9.3), seit 0.10.0 ein Teil von Senden > Erweitert.</summary>
    public KlangfarbeTeil Klangfarbe { get; }

    /// <summary>Automatische Lautstärke und Einmessen (Klangstufe A; der Schalter offen unter Senden, das Einmessen unter Senden > Erweitert), Klangprotokoll (unter Fehlersuche).</summary>
    public LautstaerkeViewModel Lautstaerke { get; }

    /// <summary>Wie gerade ausgelöst wird; folgt den gewählten Sendenetzen (A10).</summary>
    public string AusloesungText => _maschine.Sprachaktivierung ? "Sprachaktivierung" : "nur Sprechtaste";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PegelText))]
    public partial double PegelDbfs { get; private set; }

    [ObservableProperty]
    public partial double SchwelleDb { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendetText))]
    public partial bool Sendet { get; private set; }

    public string PegelText => PegelDbfs <= GateWerte.MinDb ? "-- dBFS" : PegelDbfs.ToString("0", CultureInfo.CurrentCulture) + " dBFS";

    public string SendetText => Sendet ? "SENDET" : "Ruhe";

    /// <summary>
    /// Liest die Audiogeräte von Windows neu: beim Anlegen und nach jedem Geräteereignis (seit 0.10.0; bis 0.9.3 der Knopf "Geräte neu lesen"). Die
    /// gemerkte Wahl bleibt, auch wenn ihr Gerät gerade fehlt: Die Auswahl zeigt dann den Standard, ohne die Einstellung zu überschreiben, und ohne
    /// Audio neu zu starten; kommt das Gerät zurück, steht es wieder da.
    /// </summary>
    public void GeraeteLesen()
    {
        _laden = true;
        try
        {
            Eingaenge.Clear();
            foreach (var g in _maschine.Eingaenge()) Eingaenge.Add(g);
            Ausgaenge.Clear();
            foreach (var g in _maschine.Ausgaenge()) Ausgaenge.Add(g);
            var a = _ablage.Aktuell.Audio;
            Eingang = Eingaenge.FirstOrDefault(g => string.Equals(g.Id, a.EingangId, StringComparison.Ordinal))
                ?? Eingaenge.FirstOrDefault(g => g.IstStandard) ?? Eingaenge.FirstOrDefault();
            Ausgang = Ausgaenge.FirstOrDefault(g => string.Equals(g.Id, a.AusgangId, StringComparison.Ordinal))
                ?? Ausgaenge.FirstOrDefault(g => g.IstStandard) ?? Ausgaenge.FirstOrDefault();
        }
        finally
        {
            _laden = false;
        }
    }

    [RelayCommand(CanExecute = nameof(KannStarten))]
    private async Task StartenAsync()
    {
        Hinweis = null;
        try
        {
            await _maschine.StartenAsync(Eingang?.Id, Ausgang?.Id, CancellationToken.None).ConfigureAwait(true);
            Laeuft = true;
            GeraeteZustandLesen();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Hinweis = "Audio ließ sich nicht starten: " + ex.Message;
        }
    }

    private bool KannStarten() => !Laeuft;

    [RelayCommand(CanExecute = nameof(Laeuft))]
    private async Task StoppenAsync()
    {
        try
        {
            await _maschine.StoppenAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Hinweis = "Audio ließ sich nicht sauber stoppen: " + ex.Message;
        }
        Laeuft = false;
        GeraeteZustandLesen();
    }

    /// <summary>Bildtakt: jüngste Pegelmeldung übernehmen.</summary>
    public void Takt()
    {
        // Nur beim Wechsel melden, sonst wertet WPF die Bindung 30-mal je Sekunde neu aus (die generierten Setter
        // melden nur echte Änderungen).
        GeraeteZustandLesen();
        RauschfilterStandLesen();
        Hoerprobe.Takt();
        Lautstaerke.Takt();
        Stimmproben.Takt();
        Korridor.Takt();
        // Klangstufe C2b: Mikro stumm am Pegelbalken; wie PegelMeldung.Stumm, aber auch ohne laufendes Audio richtig.
        Stumm = _maschine.MikroStumm;
        var va = _maschine.Sprachaktivierung;
        if (va != _letzteSprachaktivierung)
        {
            _letzteSprachaktivierung = va;
            OnPropertyChanged(nameof(AusloesungText));
        }
        var gesamt = _verlauf.Gesamt;
        if (gesamt == _gesehen || !_verlauf.Letzte(out var m)) return;
        _gesehen = gesamt;
        PegelDbfs = m.PegelDbfs;
        SchwelleDb = m.SchwelleDb;
        Sendet = m.Sendet;
        WuerdeSenden = m.WuerdeSenden;
        NichtsGesendet = _verlauf.Hinweis(m);
    }

    /// <summary>
    /// ui-fix 0.6.2: Gesprochen, aber es geht nichts hinaus (Ruhe, stumm durch die Einsatzleitung, nicht verbunden): der Satz dazu für den
    /// Pegelbalken, etwa "Ruhe: es wird nichts gesendet"; sonst <c>null</c>. Grün heißt dann nicht.
    /// </summary>
    [ObservableProperty]
    public partial string? NichtsGesendet { get; private set; }

    /// <summary>Q43 (0.8.0): Die Sprachaktivierung würde auslösen, aber es geht nichts hinaus; der Pegelbalken gelb (<see cref="PegelMeldung.WuerdeSenden"/>).</summary>
    [ObservableProperty]
    public partial bool WuerdeSenden { get; private set; }

    partial void OnEingangChanged(AudioGeraet? value)
    {
        if (_laden || value is null) return;
        _ablage.Aktuell.Audio.EingangId = value.Id;
        _ablage.Vormerken();
        _ = NeuStartenAsync();
    }

    partial void OnAusgangChanged(AudioGeraet? value)
    {
        if (_laden || value is null) return;
        _ablage.Aktuell.Audio.AusgangId = value.Id;
        _ablage.Vormerken();
        _ = NeuStartenAsync();
    }

    /// <summary>Gerätewechsel im Lauf: stoppen und mit dem neuen Gerät starten.</summary>
    private async Task NeuStartenAsync()
    {
        if (!Laeuft) return;
        await StoppenAsync().ConfigureAwait(true);
        await StartenAsync().ConfigureAwait(true);
    }
}
