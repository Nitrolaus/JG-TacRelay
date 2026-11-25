// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Client.Modelle;

/// <summary>
/// Lautstärke der eigenen Stimme (Klangstufe A), seit 0.10.0 unter Einstellungen > Senden (der Schalter seit 0.11.1 offen, das Einmessen unter Erweitert; bis 0.9.3 Bereich Audio): Schalter "Automatische Lautstärke"
/// (an: Halteregel der <see cref="Pegelstufe"/>, gleicht nur die Grundlautstärke an, in Pausen; aus: feste Verstärkung), Einmessen
/// der festen Verstärkung und das Klangprotokoll (das seit 0.10.0 unter Fehlersuche steht). Jede Änderung geht sofort an die Klangmaschine und in die Einstellungen
/// (<see cref="AudioEinstellungen"/>); die Maschine bekommt beim Anlegen, was gespeichert ist. Was die Automatik lernt, wird alle
/// paar Sekunden gemerkt, damit die nächste Sitzung dort beginnt.
/// </summary>
public sealed partial class LautstaerkeViewModel : ObservableObject
{
    /// <summary>So viele Bildtakte (etwa 2 s) zwischen zwei Blicken auf die gelernte Verstärkung.</summary>
    public const int AbgleichTakte = 60;

    /// <summary>Ab dieser Abweichung wird die gelernte Verstärkung neu gemerkt.</summary>
    public const double MerkenAbDb = 0.5;

    private readonly IKlangMaschine _maschine;
    private readonly Einstellungsablage _ablage;
    private (EinmessZustand Zustand, int Sekunden, bool Mikrofon) _zuletzt = (EinmessZustand.Bereit, -1, false);
    private bool _uebernommen = true;
    private int _takte;

    public LautstaerkeViewModel(IKlangMaschine maschine, Einstellungsablage ablage)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(ablage);
        _maschine = maschine;
        _ablage = ablage;
        _maschine.Lautstaerke = Werte.LautstaerkeFuerMaschine();
        _maschine.KlangprotokollOrdner = Werte.Klangprotokoll ? Protokollordner : null;
    }

    /// <summary>Steht unter dem Schalter.</summary>
    public static string Erklaerung =>
        "Gleicht nur die Grundlautstärke deiner Stimme an: in Sprechpausen und in kleinen Schritten, nie mitten im Satz. "
        + "Aus: eine feste Verstärkung, die du einmal einmisst.";

    /// <summary>Was beim Einmessen zu tun ist; steht über dem Knopf.</summary>
    public static string EinmessHinweis =>
        $"Sprich nach dem Klick {Pegelstufe.EinmessRahmen / 100} Sekunden lang: erst ein paar Sätze normal, dann einen Satz deutlich lauter.";

    /// <summary>Ordner des Klangprotokolls: der Ordner der Einstellungsdatei.</summary>
    public string Protokollordner => Path.GetDirectoryName(_ablage.Pfad) ?? AppContext.BaseDirectory;

    private AudioEinstellungen Werte => _ablage.Aktuell.Audio;

    /// <summary>
    /// Automatische Lautstärke an oder aus. Beim Ausschalten ohne eingemessenen Wert gilt die Verstärkung, die die Automatik gerade
    /// hat, damit die Lautstärke nicht springt.
    /// </summary>
    public bool Automatisch
    {
        get => Werte.AutomatischeLautstaerke;
        set
        {
            if (value == Werte.AutomatischeLautstaerke) return;
            if (!value && Werte.FesteVerstaerkungDb is null) Werte.FesteVerstaerkungDb = AudioEinstellungen.Verstaerkung(JetzigeVerstaerkung());
            Werte.AutomatischeLautstaerke = value;
            Anwenden();
            OnPropertyChanged();
            OnPropertyChanged(nameof(FestText));
            OnPropertyChanged(nameof(EinmessenGesperrt));
            OnPropertyChanged(nameof(EinmessHinweisText));
            EinmessenCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Die feste Verstärkung, wie sie gilt, wenn die Automatik aus ist.</summary>
    public string FestText => Werte.FesteVerstaerkungDb is { } db
        ? "Feste Verstärkung: " + Db(db) + "."
        : "Noch nicht eingemessen; ausgeschaltet gilt die Verstärkung, die die Automatik gerade hat.";

    /// <summary>Stand des Einmessens neben dem Knopf, oder was fehlt; leer, wenn nichts läuft.</summary>
    [ObservableProperty]
    public partial string EinmessText { get; private set; } = "";

    /// <summary>
    /// Aufschrift des Knopfs: "Feste Verstärkung einmessen" oder, während es läuft, "Abbrechen". Seit 0.10.0 mit Namen wie in Vorlage m-5 (Befund der
    /// Prüfung): Unter Senden steht offen das Einmessen der Mindest-Funklautstärke, das "Einmessen" heißt.
    /// </summary>
    public string EinmessKnopf => _maschine.Einmessen.Zustand == EinmessZustand.Laeuft ? "Abbrechen" : "Feste Verstärkung einmessen";

    /// <summary>Sperrgrund, solange die automatische Lautstärke an ist (Vorlage m-5: "Nur ohne Automatik nötig").</summary>
    public const string NurOhneAutomatik = "Nur ohne Automatik nötig: Solange die automatische Lautstärke an ist, gilt die feste Verstärkung nicht. Schalte die "
        + "Automatik aus, dann misst du sie hier ein.";

    /// <summary>Sperrgrund ohne laufendes Mikrofon; "Audio starten" steht seit 0.10.0 auf Start.</summary>
    public const string OhneMikrofon = "Einmessen geht nur, wenn das Mikrofon läuft; Audio starten unter Einstellungen > Fehlersuche.";

    /// <summary>Warum Einmessen gesperrt ist (für <c>Hilfe.Gesperrt</c>), sonst <c>null</c>: mit Automatik nicht nötig, ohne Mikrofon nicht möglich.</summary>
    public string? EinmessenGesperrt => KannEinmessen() ? null : Werte.AutomatischeLautstaerke ? NurOhneAutomatik : OhneMikrofon;

    /// <summary>Unter dem Knopf: mit Automatik "Nur ohne Automatik nötig." (m-5), sonst was beim Einmessen zu tun ist (<see cref="EinmessHinweis"/>).</summary>
    public string EinmessHinweisText => Werte.AutomatischeLautstaerke ? "Nur ohne Automatik nötig." : EinmessHinweis;

    /// <summary>Klangprotokoll schreiben (Fehlersuche).</summary>
    public bool Klangprotokoll
    {
        get => Werte.Klangprotokoll;
        set
        {
            if (value == Werte.Klangprotokoll) return;
            Werte.Klangprotokoll = value;
            _maschine.KlangprotokollOrdner = value ? Protokollordner : null;
            _ablage.Vormerken();
            OnPropertyChanged();
        }
    }

    /// <summary>Ein laufendes Einmessen lässt sich immer abbrechen; neu nur mit Mikrofon und ohne Automatik (dort gälte das Ergebnis nicht, m-5).</summary>
    private bool KannEinmessen() =>
        _maschine.Einmessen.Zustand == EinmessZustand.Laeuft || (_maschine.Geraete.AufnahmeLaeuft && !Werte.AutomatischeLautstaerke);

    [RelayCommand(CanExecute = nameof(KannEinmessen))]
    private void Einmessen()
    {
        if (_maschine.Einmessen.Zustand == EinmessZustand.Laeuft)
        {
            _maschine.EinmessenAbbrechen();
        }
        else
        {
            _maschine.EinmessenStarten();
            _uebernommen = false;
        }
        Takt();
    }

    /// <summary>Bildtakt: Einmessen verfolgen, ein Ergebnis übernehmen, die gelernte Verstärkung ab und zu merken.</summary>
    public void Takt()
    {
        var e = _maschine.Einmessen;
        var stand = (e.Zustand, (Math.Max(0, e.RestMs) + 999) / 1000, _maschine.Geraete.AufnahmeLaeuft);
        if (e.Zustand == EinmessZustand.Fertig && !_uebernommen)
        {
            _uebernommen = true;
            Werte.FesteVerstaerkungDb = AudioEinstellungen.Verstaerkung(e.ErgebnisDb);
            Werte.FestEingemessen = true;
            // Gegenprüfung C1, M4: eingemessen gilt für dieses Mikrofon; die Stimme rechnet die Maschine aus der Verstärkung (nicht an der Grenze).
            Werte.FesteStimmeDbfs = null;
            Werte.FestEingang = _maschine.Telemetrie.Eingang is { Length: <= 512 } eingang ? eingang : null;
            Anwenden();
            OnPropertyChanged(nameof(FestText));
        }
        if (stand != _zuletzt)
        {
            _zuletzt = stand;
            EinmessText = e.Zustand switch
            {
                EinmessZustand.Laeuft => $"Sprich jetzt. Noch {stand.Item2} s.",
                EinmessZustand.Fertig => "Eingemessen: " + Db(e.ErgebnisDb) + (Werte.AutomatischeLautstaerke ? "; gilt, wenn die Automatik aus ist." : "."),
                EinmessZustand.ZuWenigSprache => "Zu wenig Sprache erkannt. Sprich die ganze Zeit und versuch es noch einmal.",
                _ => "",
            };
            OnPropertyChanged(nameof(EinmessKnopf));
            OnPropertyChanged(nameof(EinmessenGesperrt));
            EinmessenCommand.NotifyCanExecuteChanged();
        }
        if (++_takte < AbgleichTakte) return;
        _takte = 0;
        GelerntMerken();
    }

    /// <summary>
    /// Was die Automatik gerade hat, für die nächste Sitzung merken (nur bei laufendem Mikrofon), mit dem Mikrofon, auf dem sie es
    /// gelernt hat: Läuft die nächste Sitzung auf einem anderen, lernt sie dort neu. Seit Klangstufe C1 nur, wenn sie die Stimme wirklich
    /// gelernt hat (<see cref="KorridorStand.StimmeBekannt"/>): Bis 0.6.0 wurde auch der unberührte Startwert gemerkt, und die nächste
    /// Sitzung hielte ihn für gelernt (die Mindest-Funklautstärke sperrte dann ein leises Mikrofon).
    /// </summary>
    private void GelerntMerken()
    {
        if (!Werte.AutomatischeLautstaerke || !_maschine.Geraete.AufnahmeLaeuft || !_maschine.Korridor.StimmeBekannt) return;
        var t = _maschine.Telemetrie;
        if (t.Rahmen == 0) return;
        var jetzt = AudioEinstellungen.Verstaerkung(t.Letzter.PegelstufeDb);
        if (jetzt is null) return;
        var gleichesGeraet = string.Equals(Werte.GelernteVerstaerkungEingang, t.Eingang, StringComparison.Ordinal);
        // Gegenprüfung C1, H1: der gelernte Anker (Funkstimme vor der Pegelstufe) mit der Verstärkung.
        var anker = AudioEinstellungen.Stimme(_maschine.Korridor.AnkerDbfs);
        var ankerGleich = anker is null || (Werte.GelernteStimmeDbfs is { } a && Math.Abs(a - anker.Value) < MerkenAbDb);
        if (gleichesGeraet && ankerGleich && Werte.GelernteVerstaerkungDb is { } alt && Math.Abs(alt - jetzt.Value) < MerkenAbDb) return;
        Werte.GelernteVerstaerkungDb = jetzt;
        Werte.GelernteVerstaerkungEingang = t.Eingang is { Length: <= 512 } e ? e : null;
        Werte.GelernteStimmeDbfs = anker;
        _ablage.Vormerken();
    }

    private double JetzigeVerstaerkung()
    {
        var t = _maschine.Telemetrie;
        return t.Rahmen > 0 ? t.Letzter.PegelstufeDb : _maschine.Lautstaerke.FestDb;
    }

    private void Anwenden()
    {
        _maschine.Lautstaerke = Werte.LautstaerkeFuerMaschine();
        _ablage.Vormerken();
    }

    private static string Db(double db) => db.ToString("+0.0;-0.0", CultureInfo.CurrentCulture) + " dB";
}
