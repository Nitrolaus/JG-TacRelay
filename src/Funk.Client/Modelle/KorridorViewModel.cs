// JG TacRelay - Maintainer: Sarevoc
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Client.Modelle;

/// <summary>
/// Korridor der Sprachaktivierung (Klangstufe C1, Entwurf "Korridor am Pegelbalken", Q17, Q18): links die Mindest-Funklautstärke
/// (empfindlicher stellt sich die Automatik nie ein), rechts die Obergrenze (die Automatik schiebt die Schwelle nie darüber), beide relativ zur gelernten
/// Funkstimme; dazwischen bewegt sich die rote Linie der Automatik. Ein Modell für den Pegelbalken unter Einstellungen > Senden (bis 0.11.0 im Klappbereich, bis 0.9.3 auf der Startseite und im Bereich Audio)
/// und für die große Schwellengrafik, damit alle dasselbe zeigen. Dazu "Mindestlautstärke einmessen" mit zwei Sätzen
/// (<see cref="Mindesteinmessung"/>), das Grundverstärkung und Untergrenze auf einmal setzt.
/// <para>
/// Gespeichert werden Abstände unter der Stimme (<see cref="GateWerte.MindestAbstandDb"/>, <see cref="GateWerte.ObergrenzeAbstandDb"/>), gezogen
/// wird auf der dBFS-Skala des Balkens: Die Lage folgt aus der Stimme, die die Maschine meldet (<see cref="IKlangMaschine.Korridor"/>).
/// Solange sie unbekannt ist, gibt es keine Untergrenze (sonst sperrte sie ein leises Mikrofon, bevor die Pegelstufe lernen kann), keine
/// Linien und keine Griffe, nur den Hinweis "Mindestlautstärke noch nicht eingemessen".
/// </para>
/// <para>
/// Für Funker Wörter statt dB (Entwurf, Grundsätze): Tooltips und Texte nennen keine Zahlen.
/// </para>
/// </summary>
public sealed partial class KorridorViewModel : ObservableObject
{
    /// <summary>
    /// Gegenprüfung C1, N2: Die alte Fassung versprach, leisere Sprache gehe nie hinaus; das stimmte nicht (eine offene Phase bleibt bei leiserer
    /// Sprache offen, Fortsetzungen werden nicht geprüft, nach dem Einmessen gehen 83 % der Rahmen 12 dB leiserer Sprache hinaus). Ehrlich ist, was die
    /// Linie tut.
    /// </summary>
    public const string HilfeUntergrenze =
        "Mindest-Funklautstärke. Untergrenze: Die Automatik stellt sich nie empfindlicher ein als diese Linie. Griff ziehen oder Pfeil links und rechts.";

    /// <summary>Hilfetext der Knöpfe "Mindestlautstärke einmessen" (Startseite, Audio, Sprachaktivierung); seit klang-c1b liegt die Linie 3 dB unter dem leisen Satz.</summary>
    public const string HilfeEinmessen =
        "Sprich einen Satz wie am Funk, dann denselben Satz leise wie zur Familie. Die Linie der Mindest-Funklautstärke kommt knapp unter den leisen Satz. "
        + "Die Automatik stellt sich nie empfindlicher ein als diese Linie.";

    public const string HilfeObergrenze =
        "Obergrenze - höher schiebt die Automatik die Schwelle nie, damit deine normale Stimme immer durchkommt. Griff ziehen oder Bild hoch und runter.";

    public const string HilfeZone = "Hier bewegt sich die Automatik. In der getönten Zone ziehen verschiebt beide Grenzen zusammen.";

    public const string HilfeRoteLinie = "Die rote Linie ist die Schwelle der Automatik: Was darüber liegt und nach Sprache klingt, wird gesendet. Sie bewegt sich von selbst.";

    /// <summary>Q43 (0.8.0): die Farben der Pegelanzeigen in Wörtern, am Pegelbalken und in der Schwellengrafik.</summary>
    public const string HilfeFarben = "Grün heißt: wird gesendet. Gelb: würde senden, geht aber nirgends hin.";

    public const string HilfeBalken = HilfeFarben + " Grau heißt: wird nicht gesendet.";

    public const string NichtEingemessen = "Mindestlautstärke noch nicht eingemessen";

    public const string HilfeNichtEingemessen =
        NichtEingemessen + ": Bis deine Stimme gelernt oder eingemessen ist, gibt es keine Untergrenze. Sprich ein paar Sätze oder miss sie ein.";

    /// <summary>Schritt der Tastatur in dB (mit Strg das Fünffache, wie in der Schwellengrafik).</summary>
    public const double Schritt = 1;

    private readonly IKlangMaschine _maschine;
    private readonly Einstellungsablage _ablage;
    private KorridorStand _stand = KorridorStand.Leer;
    private (MindestEinmessSchritt Schritt, int Sekunden) _einmessZuletzt = (MindestEinmessSchritt.Bereit, -1);
    private bool _uebernommen = true;
    private bool _aufnahme;

    public KorridorViewModel(IKlangMaschine maschine, Einstellungsablage ablage)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(ablage);
        _maschine = maschine;
        _ablage = ablage;
        _stand = maschine.Korridor;
    }

    /// <summary>Abstände, Modus oder Einmessen haben die Einstellung geändert (andere Modelle melden ihre Werte neu).</summary>
    public event Action? Geaendert;

    /// <summary>Neue Livewerte oder Einstellungen: Balken und Grafik zeichnen neu.</summary>
    public event Action? Aktualisiert;

    private GateWerte Werte => _ablage.Aktuell.Gate;

    /// <summary>Die Automatik läuft (Automatisch oder Lernend); nur dann gibt es den Korridor.</summary>
    public bool Automatik => Werte.Modus != GateModus.Fest;

    /// <summary>Die Maschine kennt die Funkstimme (gelernt oder eingemessen).</summary>
    public bool StimmeBekannt => _stand.StimmeBekannt;

    /// <summary>Linien, Zone und Griffe werden gezeigt.</summary>
    public bool Sichtbar => Automatik && StimmeBekannt;

    /// <summary>Statt des Korridors steht der Hinweis da.</summary>
    public bool ZeigeNichtEingemessen => Automatik && !StimmeBekannt;

    /// <summary>Tooltip der grauen Linien (Q39): Vor dem Einmessen lassen sie sich nicht ziehen.</summary>
    public const string HilfeGrau = "Erst einmessen, dann lässt sich die Mindestlautstärke einstellen";

    /// <summary>
    /// Q39 (Sarevoc 04.10.2026: "hat sie gesucht"): Mit Automatik stehen die zwei Linien immer da. Ohne bekannte Stimme grau (<see cref="LinienGrau"/>),
    /// ohne Griffe und nicht ziehbar; ziehbar sind sie nur, wenn <see cref="Sichtbar"/>.
    /// </summary>
    public bool ZeigeLinien => Automatik;

    /// <summary>Die Linien stehen grau da: Automatik, aber die Stimme ist noch nicht bekannt (Q39).</summary>
    public bool LinienGrau => Automatik && !StimmeBekannt;

    /// <summary>
    /// Wo die Linie der Mindest-Funklautstärke steht: mit Stimme <see cref="UntergrenzeDb"/>, sonst dort, wo sie bei einer Stimme am Ziel der
    /// Pegelstufe (<see cref="Pegelstufe.ZielDbfs"/>) läge (grau, Q39).
    /// </summary>
    public double LinieUntenDb => StimmeBekannt ? UntergrenzeDb : Pegelstufe.ZielDbfs - Werte.MindestAbstandDb;

    /// <summary>Wo die Obergrenze steht; ohne Stimme wie <see cref="LinieUntenDb"/> am Ziel der Pegelstufe gerechnet.</summary>
    public double LinieObenDb => StimmeBekannt ? ObergrenzeDb : Pegelstufe.ZielDbfs - Werte.ObergrenzeAbstandDb;

    /// <summary>Die gelernte Funkstimme am Gate in dBFS, NaN wenn unbekannt.</summary>
    public double StimmeDb => _stand.StimmeDb;

    /// <summary>Mindest-Funklautstärke in dBFS am Gate (aus der Einstellung, damit sie beim Ziehen sofort folgt); NaN ohne Stimme.</summary>
    public double UntergrenzeDb => StimmeBekannt ? _stand.MindestBezugDb - Werte.MindestAbstandDb : double.NaN;

    /// <summary>Obergrenze in dBFS am Gate; NaN ohne Stimme.</summary>
    public double ObergrenzeDb => StimmeBekannt ? _stand.StimmeDb - Werte.ObergrenzeAbstandDb : double.NaN;

    /// <summary>Kurz in Wörtern, neben dem Knopf und in der Sprachaktivierung.</summary>
    public string StandText => !Automatik ? "Ohne Automatik bestimmst du die Schwelle selbst."
        : !StimmeBekannt ? NichtEingemessen
        : "Die Automatik stellt sich nie empfindlicher ein als die Mindest-Funklautstärke.";

    // ---- Ziehen und Tastatur ----

    /// <summary>Die linke Linie (Mindest-Funklautstärke) auf <paramref name="db"/> gezogen; nur mit bekannter Stimme.</summary>
    public void UntergrenzeZiehen(double db)
    {
        if (!StimmeBekannt || !double.IsFinite(db)) return;
        Setzen(Runden(_stand.MindestBezugDb - db), Werte.ObergrenzeAbstandDb);
    }

    /// <summary>Die rechte Linie (Obergrenze) auf <paramref name="db"/> gezogen; nie näher an der Stimme als 6 dB, nie unter die Untergrenze.</summary>
    public void ObergrenzeZiehen(double db)
    {
        if (!StimmeBekannt || !double.IsFinite(db)) return;
        var oben = Math.Clamp(Runden(_stand.StimmeDb - db), GateVorgaben.MinObergrenzeAbstandDb, Werte.MindestAbstandDb - GateVorgaben.KorridorMindestBreiteDb);
        Setzen(Werte.MindestAbstandDb, oben);
    }

    /// <summary>In der Zone gezogen: beide Grenzen um <paramref name="deltaDb"/> (nach oben positiv), die Breite bleibt.</summary>
    public void KorridorVerschieben(double deltaDb) => KorridorVerschieben(Greifen(), deltaDb);

    /// <summary>Die Zone gegriffen (Maustaste gedrückt): die Abstände von hier aus, für <see cref="KorridorVerschieben(KorridorGriff, double)"/>.</summary>
    public KorridorGriff Greifen() => new(Werte.MindestAbstandDb, Werte.ObergrenzeAbstandDb);

    /// <summary>
    /// Die gegriffene Zone um <paramref name="deltaDb"/> ab dem Griff verschoben (nach oben positiv), die Breite bleibt. Gegenprüfung C1, M2: Gerechnet
    /// wird immer ab dem Griff, nicht in Teilstücken; ein Teilstück unter 0,25 dB verfiel sonst beim Runden auf 0,5 dB, und langsam gezogen blieb die
    /// Zone stehen.
    /// </summary>
    public void KorridorVerschieben(KorridorGriff ab, double deltaDb)
    {
        if (!StimmeBekannt || !double.IsFinite(deltaDb)) return;
        var d = Math.Clamp(deltaDb, ab.MindestAbstandDb - GateVorgaben.MaxMindestAbstandDb, ab.ObergrenzeAbstandDb - GateVorgaben.MinObergrenzeAbstandDb);
        Setzen(Runden(ab.MindestAbstandDb - d), Runden(ab.ObergrenzeAbstandDb - d));
    }

    /// <summary>Pfeil links/rechts (hoch/runter): die Mindest-Funklautstärke um <paramref name="schritt"/> dB.</summary>
    public void UntergrenzeSchieben(double schritt) => UntergrenzeZiehen(UntergrenzeDb + schritt);

    /// <summary>Bild hoch/runter: die Obergrenze um <paramref name="schritt"/> dB.</summary>
    public void ObergrenzeSchieben(double schritt) => ObergrenzeZiehen(ObergrenzeDb + schritt);

    private static float Runden(double db) => (float)(Math.Round(db * 2, MidpointRounding.AwayFromZero) / 2);

    private void Setzen(float mindestAbstand, float obergrenzeAbstand)
    {
        Werte.ObergrenzeAbstandDb = obergrenzeAbstand;
        Werte.MindestAbstandDb = mindestAbstand;
        Werte.Normalisieren();
        Anwenden();
    }

    private void Anwenden()
    {
        _maschine.Gate = Werte.AlsEinstellung();
        _ablage.Vormerken();
        Auffrischen();
        Geaendert?.Invoke();
    }

    /// <summary>Alle Werte neu melden (nach einer Änderung von außen, etwa des Modus).</summary>
    public void Auffrischen()
    {
        OnPropertyChanged(nameof(Automatik));
        OnPropertyChanged(nameof(StimmeBekannt));
        OnPropertyChanged(nameof(Sichtbar));
        OnPropertyChanged(nameof(ZeigeNichtEingemessen));
        OnPropertyChanged(nameof(StimmeDb));
        OnPropertyChanged(nameof(UntergrenzeDb));
        OnPropertyChanged(nameof(ObergrenzeDb));
        OnPropertyChanged(nameof(ZeigeLinien));
        OnPropertyChanged(nameof(LinienGrau));
        OnPropertyChanged(nameof(LinieUntenDb));
        OnPropertyChanged(nameof(LinieObenDb));
        OnPropertyChanged(nameof(StandText));
        OnPropertyChanged(nameof(ZeileText));
        OnPropertyChanged(nameof(ZeigeEinmessHinweis));
        Aktualisiert?.Invoke();
    }

    // ---- Einmessen ----

    /// <summary>Was das Einmessen gerade sagt (Wörter, keine Zahlen außer den Sekunden).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZeileText))]
    public partial string EinmessText { get; private set; } = "";

    /// <summary>Eine Zeile unter dem Balken: das Einmessen, sonst "Mindestlautstärke noch nicht eingemessen", bis einmal eingemessen wurde; sonst leer.</summary>
    public string ZeileText => EinmessText.Length > 0 ? EinmessText : ZeigeEinmessHinweis ? NichtEingemessen : "";

    /// <summary>Mikrofon beim letzten Takt, um einen Wechsel zu bemerken.</summary>
    private string? _eingang;

    /// <summary>
    /// Mit Automatik, solange auf diesem Mikrofon noch nie eingemessen wurde: Hinweis "Mindestlautstärke noch nicht eingemessen" (Startseite). Ein
    /// anderes Mikrofon als beim Einmessen bringt ihn zurück (Gegenprüfung C1, M4).
    /// </summary>
    public bool ZeigeEinmessHinweis => Automatik && !Werte.EingemessenFuer(_maschine.Telemetrie.Eingang);

    public bool EinmessenLaeuft => _maschine.MindestEinmessen.Schritt is MindestEinmessSchritt.Normal or MindestEinmessSchritt.Umschalten or MindestEinmessSchritt.Leise;

    public string EinmessKnopf => EinmessenLaeuft ? "Einmessen abbrechen" : "Mindestlautstärke einmessen";

    /// <summary>Kurz für den Knopf neben dem Balken unter Einstellungen > Senden (seit 0.10.0, Bild m-5; seit 0.11.1 offen): "Einmessen" oder "Abbrechen".</summary>
    public string EinmessKnopfKurz => EinmessenLaeuft ? "Abbrechen" : "Einmessen";

    /// <summary>Warum der Knopf gesperrt ist, oder null.</summary>
    public string? EinmessenGesperrt => _maschine.Geraete.AufnahmeLaeuft ? null : "Geht nur, wenn das Mikrofon läuft (Audio starten unter Einstellungen > Fehlersuche).";

    public bool KannEinmessen => EinmessenGesperrt is null;

    /// <summary>Die Zeile mit Knopf und Hinweis auf der Startseite: solange das Mikrofon läuft oder eingemessen wird.</summary>
    public bool ZeigeEinmessen => KannEinmessen || EinmessenLaeuft;

    [RelayCommand(CanExecute = nameof(KannEinmessen))]
    private void Einmessen()
    {
        if (EinmessenLaeuft)
        {
            _maschine.MindestEinmessenAbbrechen();
            EinmessText = "";
            PopupOffen = false;
        }
        else
        {
            _uebernommen = false;
            _maschine.MindestEinmessenStarten();
            PopupOffen = true;
        }
        Takt();
    }

    // ---- Popup des Einmessens (Q40) ----

    /// <summary>
    /// Q40 (Sarevoc 04.10.2026, hatte im Text überlesen, dass einmal laut und einmal leise gesprochen wird): Das Einmessen führt ein Popup über dem
    /// Hauptfenster, der Hintergrund weichgezeichnet, zwei deutliche Schritte mit großem Countdown und Pegel. Der Einmess-Kern bleibt.
    /// </summary>
    [ObservableProperty]
    public partial bool PopupOffen { get; private set; }

    /// <summary>Der große Satz im Popup: "Jetzt sprechen wie am Funk", "Jetzt leise, wie zu jemandem im Raum", "Fertig" ...</summary>
    [ObservableProperty]
    public partial string PopupTitel { get; private set; } = "";

    /// <summary>Der große Countdown in Sekunden; leer, wenn nichts läuft.</summary>
    [ObservableProperty]
    public partial string PopupZahl { get; private set; } = "";

    /// <summary>Die Zeile darunter: welcher Schritt und was zu tun ist.</summary>
    [ObservableProperty]
    public partial string PopupUnten { get; private set; } = "";

    /// <summary>Der linke Knopf: "Abbrechen", solange gemessen wird, danach "Schließen".</summary>
    public string PopupSchliessenText => EinmessenLaeuft ? "Abbrechen" : "Schließen";

    /// <summary>Abbrechen (bricht ein laufendes Einmessen ab) oder Schließen; das Popup geht zu.</summary>
    [RelayCommand]
    private void PopupSchliessen()
    {
        if (EinmessenLaeuft)
        {
            _maschine.MindestEinmessenAbbrechen();
            EinmessText = "";
        }
        PopupOffen = false;
        Takt();
    }

    /// <summary>Noch einmal von vorn (bricht ein laufendes Einmessen ab).</summary>
    [RelayCommand(CanExecute = nameof(KannEinmessen))]
    private void NochEinmal()
    {
        if (EinmessenLaeuft) _maschine.MindestEinmessenAbbrechen();
        _uebernommen = false;
        _einmessZuletzt = (MindestEinmessSchritt.Bereit, -1);
        _maschine.MindestEinmessenStarten();
        PopupOffen = true;
        Takt();
    }

    /// <summary>Bildtakt: Stand der Maschine holen, ein Ergebnis des Einmessens übernehmen.</summary>
    public void Takt()
    {
        var k = _maschine.Korridor;
        // Gegenprüfung C1, M4: Wechselt das Mikrofon, gilt das Einmessen dort vielleicht nicht; Hinweis neu melden.
        var eingang = _maschine.Telemetrie.Eingang;
        if (!Gleich(k, _stand) || !string.Equals(eingang, _eingang, StringComparison.Ordinal))
        {
            _stand = k;
            _eingang = eingang;
            Auffrischen();
        }
        var e = _maschine.MindestEinmessen;
        if (e.Schritt == MindestEinmessSchritt.Fertig && !_uebernommen)
        {
            _uebernommen = true;
            Uebernehmen(e);
        }
        var aufnahme = _maschine.Geraete.AufnahmeLaeuft;
        if (aufnahme != _aufnahme)
        {
            _aufnahme = aufnahme;
            OnPropertyChanged(nameof(EinmessenGesperrt));
            OnPropertyChanged(nameof(KannEinmessen));
            OnPropertyChanged(nameof(ZeigeEinmessen));
            EinmessenCommand.NotifyCanExecuteChanged();
            NochEinmalCommand.NotifyCanExecuteChanged();
        }
        var sekunden = (Math.Max(0, e.RestMs) + 999) / 1000;
        if ((e.Schritt, sekunden) == _einmessZuletzt) return;
        _einmessZuletzt = (e.Schritt, sekunden);
        EinmessText = e.Schritt switch
        {
            MindestEinmessSchritt.Normal => $"1 von 2: Sprich einen Satz so, wie du funkst. Noch {sekunden} s.",
            MindestEinmessSchritt.Umschalten => "Gleich Satz 2: derselbe Satz leise, wie zur Familie.",
            MindestEinmessSchritt.Leise => $"2 von 2: Jetzt derselbe Satz leise, wie zur Familie. Noch {sekunden} s.",
            MindestEinmessSchritt.Fertig =>
                "Eingemessen. Die Automatik stellt sich nie empfindlicher ein als die Mindest-Funklautstärke; die Lautstärke deiner Stimme ist neu eingestellt.",
            MindestEinmessSchritt.ZuWenigUnterschied =>
                "Der leise Satz war kaum leiser als der normale. Versuch es noch einmal und sprich den zweiten Satz deutlich leiser, wie zur Familie.",
            MindestEinmessSchritt.ZuWenigSprache => "Zu wenig Sprache erkannt. Sprich jeweils einen ganzen Satz und versuch es noch einmal.",
            _ => EinmessText,
        };
        var zahl = sekunden.ToString(System.Globalization.CultureInfo.InvariantCulture);
        (PopupTitel, PopupZahl, PopupUnten) = e.Schritt switch
        {
            MindestEinmessSchritt.Normal => ("Jetzt sprechen wie am Funk", zahl, "Schritt 1 von 2: einen ganzen Satz, so laut wie im Spiel."),
            MindestEinmessSchritt.Umschalten => ("Kurze Pause", zahl, "Gleich Schritt 2: derselbe Satz leise."),
            MindestEinmessSchritt.Leise => ("Jetzt leise, wie zu jemandem im Raum", zahl, "Schritt 2 von 2: derselbe Satz leise, wie zur Familie."),
            MindestEinmessSchritt.Fertig => ("Fertig", "", "Die Mindest-Funklautstärke ist eingestellt. Die Automatik stellt sich nie empfindlicher ein."),
            MindestEinmessSchritt.ZuWenigUnterschied => ("Unterschied zu klein, noch einmal", "", "Den zweiten Satz deutlich leiser sprechen, wie zu jemandem im Raum."),
            MindestEinmessSchritt.ZuWenigSprache => ("Zu wenig Sprache, noch einmal", "", "Jeweils einen ganzen Satz sprechen."),
            _ => (PopupTitel, PopupZahl, PopupUnten),
        };
        OnPropertyChanged(nameof(PopupSchliessenText));
        NochEinmalCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(EinmessenLaeuft));
        OnPropertyChanged(nameof(EinmessKnopf));
        OnPropertyChanged(nameof(EinmessKnopfKurz));
        OnPropertyChanged(nameof(EinmessenGesperrt));
        OnPropertyChanged(nameof(KannEinmessen));
        OnPropertyChanged(nameof(ZeigeEinmessen));
        EinmessenCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Das Ergebnis gilt auf einmal (Klangstufe C1): die Grundverstärkung der Pegelstufe aus dem normalen Satz (mit Automatik als gelernt für
    /// dieses Mikrofon, sonst als eingemessene feste Verstärkung) und die Mindest-Funklautstärke zwischen normal und leise.
    /// </summary>
    private void Uebernehmen(MindestEinmessStand e)
    {
        var audio = _ablage.Aktuell.Audio;
        var g = AudioEinstellungen.Verstaerkung(e.VerstaerkungDb);
        var mikrofon = _maschine.Telemetrie.Eingang is { Length: <= 512 } eingang ? eingang : null;
        // Gegenprüfung C1, H1 und M4: die gemessene Stimme und das Mikrofon mit der Verstärkung merken.
        var stimme = AudioEinstellungen.Stimme(e.NormalDbfs);
        if (audio.AutomatischeLautstaerke)
        {
            audio.GelernteVerstaerkungDb = g;
            audio.GelernteVerstaerkungEingang = mikrofon ?? audio.GelernteVerstaerkungEingang;
            audio.GelernteStimmeDbfs = stimme;
        }
        else
        {
            audio.FesteVerstaerkungDb = g;
            audio.FestEingemessen = true;
            audio.FesteStimmeDbfs = stimme;
            audio.FestEingang = mikrofon;
        }
        Werte.MindestAbstandDb = e.MindestAbstandDb;
        Werte.MindestEingemessen = true;
        Werte.MindestEingemessenEingang = mikrofon;
        Werte.Normalisieren();
        _maschine.Lautstaerke = audio.LautstaerkeFuerMaschine() with { Eingemessen = true };
        Anwenden();
    }

    private static bool Gleich(KorridorStand a, KorridorStand b) => Gleich(a.StimmeDb, b.StimmeDb) && Gleich(a.UntergrenzeDb, b.UntergrenzeDb) && Gleich(a.ObergrenzeDb, b.ObergrenzeDb);

    private static bool Gleich(float a, float b) => (float.IsNaN(a) && float.IsNaN(b)) || Math.Abs(a - b) < 0.05f;
}

/// <summary>Die Abstände des Korridors, als die Zone gegriffen wurde (Gegenprüfung C1, M2): Gezogen wird ab hier, nicht in Teilstücken.</summary>
public readonly record struct KorridorGriff(float MindestAbstandDb, float ObergrenzeAbstandDb);
