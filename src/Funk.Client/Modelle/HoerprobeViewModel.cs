// JG TacRelay - Maintainer: Sarevoc
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Klang.Maschine;

namespace Funk.Client.Modelle;

/// <summary>
/// Die eine Hörprobe (seit 0.10.0 auf Start, auch verbunden, Q75, E19; bis 0.9.3 im Bereich Audio und auf der Seite Sprachaktivierung, im
/// Programm eine Instanz für alle) und Mithören (seit 0.10.0 unter Einstellungen > Senden > Erweitert). Nach Sarevocs Fehlerbericht
/// vom 01.10.2026 ("ich höre mich bei Aktivierung nicht"; Tischmikrofon, Lautsprecher, Echounterdrückung an) zwei Teile:
/// <list type="bullet">
/// <item>Hörprobe als Aufnahme (<see cref="ProbeCommand"/>): 5 s sprechen, danach hören, so wie die anderen einen empfangen.
/// Geht immer, auch mit Lautsprechern und mit Sendenetzen auf Taste: Die Maschine nimmt ohne Gate auf und spielt erst danach ab,
/// nie beides zugleich, und sendet dabei nichts aus der Sprachaktivierung (<see cref="IKlangMaschine.ProbeStarten"/>).</item>
/// <item>Mithören beim Senden (<see cref="Mithoeren"/>, bis 0.4.0 hieß das "Hörprobe"): nur mit Headset. Über Lautsprecher kommt
/// die eigene Stimme verzögert zurück ins Mikrofon; mit Echounterdrückung kommen eigene Anlaute dann schlechter durch, ohne hält
/// das Echo das Gate offen (gemessen in <c>HoerprobeTests</c>). Man hört sich nur, solange man sendet; der Text sagt, wann,
/// und zuerst, ob der Schalter an ist (Prüfung 0.4.1: Er versprach das Mithören auch bei ausgeschaltetem Schalter).</item>
/// </list>
/// Hält keinen eigenen Stand der Maschine: <see cref="Takt"/> liest ihn und meldet nur Änderungen, so zeigen auch zwei Instanzen
/// dasselbe.
/// </summary>
public sealed partial class HoerprobeViewModel : ObservableObject
{
    private readonly IKlangMaschine _maschine;
    private readonly Einstellungsablage _ablage;
    private bool _mithoerenZuletzt;
    private bool _kannZuletzt;
    private string _textZuletzt;
    private (ProbeZustand Zustand, int Sekunden, AudioStand Audio) _probeZuletzt;

    private enum AudioStand { Gestoppt, Unvollstaendig, Laeuft }

    public HoerprobeViewModel(IKlangMaschine maschine, Einstellungsablage ablage)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(ablage);
        _maschine = maschine;
        _ablage = ablage;
        _mithoerenZuletzt = maschine.Hoerprobe;
        _kannZuletzt = KannMithoeren;
        _textZuletzt = MithoerenText;
        _probeZuletzt = ProbeAnzeige();
    }

    // ---- Mithören beim Senden ----

    /// <summary>Mithören nur mit Headset; bei Lautsprechern und offener Frage nicht.</summary>
    public bool KannMithoeren => _ablage.Aktuell.Audio.Hoerweise == Hoerweise.Headset;

    public bool Mithoeren
    {
        get => _maschine.Hoerprobe;
        set
        {
            if (value && !KannMithoeren)
            {
                // Der Schalter springt zurück.
                OnPropertyChanged();
                return;
            }
            if (value == _maschine.Hoerprobe) return;
            _maschine.Hoerprobe = value;
            _mithoerenZuletzt = value;
            OnPropertyChanged();
            TextMelden();
        }
    }

    /// <summary>
    /// Unter dem Schalter: ohne Headset, warum nicht; mit Headset zuerst, ob Mithören an ist, dann die Auslösung des Sendenetzes.
    /// Bis zur Prüfung 0.4.1 hing der Text nicht am Schalter und sagte auch ausgeschaltet "Halte sie, um dich zu hören".
    /// </summary>
    public string MithoerenText =>
        !KannMithoeren
            ? "Nur mit Headset. Über Lautsprecher käme deine Stimme verzögert zurück ins Mikrofon; nimm dafür die Hörprobe."
            : !_maschine.Hoerprobe
                ? _maschine.Sprachaktivierung
                    ? "Aus. Eingeschaltet hörst du mit, was du sendest, gerade per Sprachaktivierung."
                    : "Aus. Eingeschaltet hörst du mit, was du sendest, gerade nur mit der Sprechtaste."
                : _maschine.Sprachaktivierung
                    ? "Du hörst mit, was du sendest. Gerade per Sprachaktivierung: sobald du über der Schwelle sprichst."
                    : "Du hörst mit, was du sendest. Gerade nur mit der Sprechtaste: Halte sie, um dich zu hören.";

    /// <summary>Meldet <see cref="MithoerenText"/> nur, wenn er sich geändert hat (Schalter, Hörweise oder Auslösung).</summary>
    private void TextMelden()
    {
        var text = MithoerenText;
        if (string.Equals(text, _textZuletzt, StringComparison.Ordinal)) return;
        _textZuletzt = text;
        OnPropertyChanged(nameof(MithoerenText));
    }

    // ---- Hörprobe als Aufnahme ----

    /// <summary>Zweites Wort auf dem Knopf (das erste trägt die Zugriffstaste): aufnehmen oder abbrechen.</summary>
    public string ProbeKnopfZusatz => _maschine.Probe.Zustand == ProbeZustand.Bereit ? "aufnehmen" : "abbrechen";

    /// <summary>
    /// Was die Hörprobe tut, immer gleich. Steht über dem Knopf und ist der längste Text der Karte, damit der wechselnde
    /// <see cref="ProbeText"/> die Breite des Bereichs nicht bei jedem Klick verschiebt.
    /// </summary>
    public static string ProbeHinweis =>
        $"Sprich nach dem Klick {IKlangMaschine.ProbeDauerMs / 1000} Sekunden lang, danach hörst du dich so, wie die anderen dich "
        + "empfangen. Das geht auch mit Lautsprechern. Gesendet wird dabei nur, was du mit der Sprechtaste sprichst.";

    /// <summary>Stand neben dem Knopf: Aufnahme oder Wiedergabe mit Restzeit, oder was fehlt; leer, wenn alles bereit ist.</summary>
    public string ProbeText
    {
        get
        {
            var p = _maschine.Probe;
            return p.Zustand switch
            {
                ProbeZustand.Aufnahme => $"Aufnahme läuft, sprich jetzt. Noch {Sekunden(p.RestMs)} s.",
                ProbeZustand.Wiedergabe => $"So hören dich die anderen. Noch {Sekunden(p.RestMs)} s.",
                _ => Audio() switch
                {
                    // Seit 0.10.0 steht "Audio starten" auch auf Start, gleich neben der Hörprobe (E9).
                    AudioStand.Gestoppt => "Geht erst, wenn das Audio läuft.",
                    AudioStand.Unvollstaendig => "Für die Hörprobe müssen Mikrofon und Wiedergabe laufen; was fehlt, steht auf Start.",
                    _ => "",
                },
            };
        }
    }

    /// <summary>
    /// Warum der Knopf gesperrt ist (für <c>Hilfe.Gesperrt</c>), sonst <c>null</c>: Ohne Mikrofon und Wiedergabe gibt es nichts
    /// aufzunehmen. Bis Stufe 2 blieb der Knopf dann klickbar und tat nichts (Befund N3 der Gegenprüfung Stufe 2).
    /// </summary>
    public string? ProbeGesperrt => KannProbe() ? null : ProbeText;

    /// <summary>Abbrechen geht immer, aufnehmen nur mit laufendem Mikrofon und laufender Wiedergabe.</summary>
    private bool KannProbe() => _maschine.Probe.Zustand != ProbeZustand.Bereit || Audio() == AudioStand.Laeuft;

    /// <summary>Bereit: aufnehmen (wenn Mikrofon und Wiedergabe laufen). Sonst: abbrechen.</summary>
    [RelayCommand(CanExecute = nameof(KannProbe))]
    private void Probe()
    {
        if (_maschine.Probe.Zustand != ProbeZustand.Bereit) _maschine.ProbeAbbrechen();
        else if (Audio() == AudioStand.Laeuft) _maschine.ProbeStarten();
        Takt();
    }

    /// <summary>Bildtakt (und nach einem Wechsel von Headset und Lautsprecher): Stand der Maschine lesen, nur Änderungen melden.</summary>
    public void Takt()
    {
        // Die Hörweise ändert sich auch ohne diese Instanz (Headset oder Lautsprecher auf Start).
        var kann = KannMithoeren;
        if (!kann && _maschine.Hoerprobe) _maschine.Hoerprobe = false;
        var mithoeren = _maschine.Hoerprobe;
        if (mithoeren != _mithoerenZuletzt)
        {
            _mithoerenZuletzt = mithoeren;
            OnPropertyChanged(nameof(Mithoeren));
        }
        if (kann != _kannZuletzt)
        {
            _kannZuletzt = kann;
            OnPropertyChanged(nameof(KannMithoeren));
        }
        TextMelden();
        var probe = ProbeAnzeige();
        if (probe != _probeZuletzt)
        {
            _probeZuletzt = probe;
            OnPropertyChanged(nameof(ProbeText));
            OnPropertyChanged(nameof(ProbeKnopfZusatz));
            OnPropertyChanged(nameof(ProbeGesperrt));
            ProbeCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Headset oder Lautsprecher wurde gerade gewählt: Mithören gleich abgleichen, nicht erst im nächsten Takt.</summary>
    internal void HoerweiseGeaendert() => Takt();

    private (ProbeZustand, int, AudioStand) ProbeAnzeige()
    {
        var p = _maschine.Probe;
        return (p.Zustand, Sekunden(p.RestMs), Audio());
    }

    private AudioStand Audio()
    {
        var g = _maschine.Geraete;
        if (!g.Gestartet) return AudioStand.Gestoppt;
        return g.AufnahmeLaeuft && g.WiedergabeLaeuft ? AudioStand.Laeuft : AudioStand.Unvollstaendig;
    }

    private static int Sekunden(int ms) => (Math.Max(0, ms) + 999) / 1000;
}
