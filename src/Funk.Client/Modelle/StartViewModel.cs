// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.ClientKern;

namespace Funk.Client.Modelle;

/// <summary>Ein Stecker der Startseite: getrennt (grau, auseinander), verbindet, verbunden (grün, zusammen), Tunnel (gelb, zusammen).</summary>
public enum Steckerzustand
{
    Getrennt,
    Verbindet,
    Verbunden,
    Tunnel,
}

/// <summary>
/// Die Startseite (seit 0.10.0 nach der Mischung, Q87 bis Q92, Bilder m-1, m-2 und m-7c; bis 0.9.3 Sarevocs Skizze aus 0.6.0): Logo aus dem
/// Gildenpaket, Name, "Gerät freigegeben" oder "Wartet auf Freigabe" mit der Kennung, Verbinden oder Trennen, je ein Stecker für den
/// Steuerkanal (Server) und die Sprache mit einem Wort daneben und die Zahl derer, die online sind. Darunter alles, was man zum Funken braucht,
/// in zwei Spalten: "Sprechen" mit Mikrofon, Sprechtaste (Tastenname mit Ändern, oder die Warnung ohne Taste) und "Senden mit Stimme", "Hören"
/// mit Ausgabe, Funk-Lautstärke (E7), Headset oder Lautsprecher und der einen Hörprobe, die auch verbunden geht (Q75). Pegel und SENDET stehen
/// nur unten in der Statuszeile "Mikrofon" (Q87). Beim allerersten Start (Q76, E8) steht an der Stelle von Verbinden der Willkommenssatz, und die
/// zwei fehlenden Angaben sind umrandet: Headset oder Lautsprecher und der Schritt "Drück jetzt deine Sprechtaste", der von selbst auf die Taste
/// wartet, solange die Seite vorn ist; "Später" lässt ihn aus, danach warnt Start. Mit Einheiten folgt die Karte Einsteigen (E10). Funkfeld 5,
/// Funkfeld 10, Wer ist da, der Pegel der Ausgabe und der Hinweis zum Einmessen stehen seit 0.10.0 nicht mehr hier (E9; die Titelleiste reicht).
/// Für Funker steht nirgends Server, Host, Port oder Adresse (Q5); die Details gibt es nur im Admin-Setup unter Einstellungen (Q3, Q14).
/// Verbindung, Anmeldung und Warteraum bleiben im <see cref="VerbindungViewModel"/>; diese Seite ordnet sie an.
/// </summary>
public sealed partial class StartViewModel : ObservableObject
{
    /// <summary>Grund an Verbinden beim allerersten Start, solange die Hörweise fehlt.</summary>
    public const string ErstHoerweise = "Wähle erst, ob du mit Headset oder über Lautsprecher hörst.";

    /// <summary>Grund an Verbinden, solange nur noch der Schritt Sprechtaste offen ist (E8).</summary>
    public const string ErstSprechtaste = "Belege erst deine Sprechtaste oder wähle Später.";

    /// <summary>Neben der Hörprobe, solange sie bereit ist.</summary>
    public const string HoerprobeBereitText = "Sprich fünf Sekunden, dann hörst du dich. Geht auch verbunden.";

    /// <summary>
    /// Ab dieser Breite der Grundansicht stehen "Sprechen" und "Hören" nebeneinander (Q88: unter etwa 900 Fensterbreite untereinander). Herleitung
    /// am Fenster (gemessen in OberflaecheUmbauStartTests): Navigation 200, Ränder des Bereichs 2 x 24, Rand der Karte 2 x 18 und der Fensterrahmen
    /// lassen bei 900 Fensterbreite 613,7 für die Grundansicht, bei 760 nur 463,4, mit offener Spalte "Wer ist da" (340) bei 900 nur 263,4 und
    /// bei 1100 463,4. Mit 560 stehen die Spalten ab etwa 846 Fensterbreite nebeneinander, bei 900 auch dann, wenn ein Rollbalken (etwa 17) Platz
    /// nimmt; darunter untereinander.
    /// </summary>
    public const double ZweiSpaltenAb = 560;

    /// <summary>
    /// Ab dieser Breite steht "Später" neben dem Wartefeld, darunter unter ihm (Prüfbericht Mischung, "Zusätzlich gefunden": bei 900 stand sonst
    /// "Wartet auf deine Tas" da). Gebraucht werden Wartefeld (Zeichen und "Wartet auf deine Taste ...") und "Später" mit Lücke, gemessen 294,6
    /// (OberflaecheUmbauStartTests); dazu etwas Luft. In Klein 900 mit zwei Spalten ist die Zeile im umrandeten Schritt 270,3 breit, dort steht
    /// "Später" darunter; bei 1100 (356,0) und untereinander (unter etwa 846 Fensterbreite, bei 760 438,9) daneben.
    /// </summary>
    public const double SpaeterDanebenAb = 330;

    /// <summary>
    /// Ab dieser Breite stehen die zwei Stecker (Server und Sprache) nebeneinander, darunter Sprache unter Server (Befund der Prüfung: Bei Klein 900
    /// mit offener Spalte "Wer ist da", 263,4 breit, schnitt das Raster die Wörter ab, "Serve" und "verb"). Ein Stecker mit seinem Wort braucht 56
    /// für den Stecker, 20 Rand und das längste Wort ("verbunden", "verbindet"), gemessen gut 140; zwei davon mit etwas Luft
    /// (OberflaecheUmbauStartTests prüft die Herleitung).
    /// </summary>
    public const double SteckerNebeneinanderAb = 300;

    /// <summary>
    /// Ab dieser Breite steht "Online: 12" rechts neben den Steckern, darunter unter ihnen: <see cref="SteckerNebeneinanderAb"/> plus "Online" mit
    /// zweistelliger Zahl (gemessen gut 60) und Luft. Bei 760 Fensterbreite (463,4) steht die Zeile wie bisher in einer Reihe, bei Klein 900 mit
    /// "Wer ist da" (263,4) untereinander.
    /// </summary>
    public const double OnlineDanebenAb = 380;

    private readonly Einstellungsablage _ablage;
    private readonly IFunkDienst _dienst;

    /// <summary>Esc im Schritt Sprechtaste: Die Seite wartet in dieser Sitzung nicht mehr von selbst, dort steht "Belegen".</summary>
    private bool _erwartenAbgebrochen;

    /// <summary>Das laufende Warten auf die Sprechtaste hat diese Seite begonnen (Erststart, Ändern, Belegen); nur das beendet sie beim Verlassen.</summary>
    private bool _startWartet;

    private int _warteNummer;

    /// <param name="tasten">Die globalen Tasten: die Sprechtaste auf Start und ihr Schritt beim ersten Start.</param>
    /// <param name="netze">Die Kanäle, für die Karte Einsteigen (nur mit Einheiten).</param>
    /// <param name="adminSetup">Der Verwaltungsteil ist geladen (Admin-Setup): Dann gibt es die Details.</param>
    public StartViewModel(VerbindungViewModel verbindung, AudioViewModel audio, WerIstDaViewModel werIstDa, TastenViewModel tasten, NetzeViewModel netze,
        IFunkDienst dienst, Einstellungsablage ablage, Gildenpaket gilde, bool adminSetup)
    {
        ArgumentNullException.ThrowIfNull(verbindung);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(werIstDa);
        ArgumentNullException.ThrowIfNull(tasten);
        ArgumentNullException.ThrowIfNull(netze);
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(gilde);
        Verbindung = verbindung;
        Audio = audio;
        WerIstDa = werIstDa;
        Tasten = tasten;
        Netze = netze;
        Sprechtaste = tasten.Funktionen.Single(f => f.Funktion == Funk.Klang.Eingabe.Tastenfunktion.Sprechen);
        _dienst = dienst;
        _ablage = ablage;
        Logo = gilde.Logo;
        IstAdminSetup = adminSetup;
        // Q3 und Q14: Die Adresse gibt es nur unter Details im Admin-Setup. Bis 0.5.x stand das Feld für alle da; eine Adresse von
        // damals gälte im Funker-Setup sonst unsichtbar weiter (statt der Hosts aus dem Profil), ließe sich nicht mehr ändern, und
        // eine ungültige verwiese auf Details, die es dort nicht gibt (Gegenprüfung Stufe 2). Im Funker-Setup fällt sie weg.
        if (!adminSetup && verbindung.Adresse.Length > 0) verbindung.Adresse = "";
        // E8 (Q76): Beim allerersten Start (die Hörweise fehlt noch) ist der Schritt Sprechtaste offen, außer es ist schon eine belegt. Eine Datei
        // von vorher mit gewählter Hörweise bekommt keinen Schritt (null bleibt null).
        var t = ablage.Aktuell.Tasten;
        if (t.ErststartSprechtaste is null && ablage.Aktuell.Audio.Hoerweise is null && tasten.OhneSprechtaste)
        {
            t.ErststartSprechtaste = false;
            ablage.Vormerken();
        }
        // E12 (Endfassung #83): Ein schon freigegebenes Gerät (auch aus einer Datei vor 0.10.0) verbindet ab jetzt beim Start; einmal, danach gilt
        // der Schalter. Hier beim Aufbau, also bevor die App entscheidet, ob sie gleich verbindet.
        if (ablage.Aktuell.Verbindung.NachFreigabeVoreinstellen()) ablage.Vormerken();
        verbindung.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(VerbindungViewModel.Status) or nameof(VerbindungViewModel.Konto) or nameof(VerbindungViewModel.IstWarteraum)
                or nameof(VerbindungViewModel.WasFehlt) or nameof(VerbindungViewModel.Entfernt))
                Aktualisieren();
        };
        verbindung.VerbindenCommand.CanExecuteChanged += (_, _) => VerbindenCommand.NotifyCanExecuteChanged();
        audio.PropertyChanged += AudioGeaendert;
        audio.Hoerprobe.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HoerprobeViewModel.ProbeText)) OnPropertyChanged(nameof(HoerprobeText));
            if (e.PropertyName == nameof(HoerprobeViewModel.ProbeKnopfZusatz)) OnPropertyChanged(nameof(HoerprobeKnopfText));
        };
        audio.Hoerprobe.ProbeCommand.CanExecuteChanged += (_, _) =>
        {
            HoerprobeCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HoerprobeGesperrt));
        };
        werIstDa.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WerIstDaViewModel.Anzahl)) OnPropertyChanged(nameof(OnlineText));
        };
        tasten.Geaendert += ErststartGeaendert;
        Sprechtaste.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TastenFunktionZeile.Wartet)) ErststartGeaendert();
        };
        Aktualisieren();
    }

    /// <summary>Verbindung, Anmeldung, Warteraum, Code und Rückfrage; dazu die Werte für die Details im Admin-Setup.</summary>
    public VerbindungViewModel Verbindung { get; }

    /// <summary>"Beim Start verbinden" ist mit der ersten Freigabe einmal angegangen (E12); der Schalter unter Einstellungen zieht nach.</summary>
    public event Action? VerbindenVoreingestellt;

    /// <summary>Geräte, Headset oder Lautsprecher, Senden mit Stimme, Funk-Lautstärke, Gerätewarnung, Hörprobe.</summary>
    public AudioViewModel Audio { get; }

    public WerIstDaViewModel WerIstDa { get; }

    /// <summary>Die globalen Tasten (dieselben wie unter Einstellungen).</summary>
    public TastenViewModel Tasten { get; }

    /// <summary>Die Zeile Sprechtaste der globalen Tasten: ihre Marken, ob sie gerade wartet.</summary>
    public TastenFunktionZeile Sprechtaste { get; }

    /// <summary>Die Kanäle; Start zeigt ihre Karte Einsteigen, wenn die Flotte Einheiten hat (E10).</summary>
    public NetzeViewModel Netze { get; }

    /// <summary>Das Logo der Gilde (bis Stufe 4 der eingebaute Platzhalter).</summary>
    public ImageSource Logo { get; }

    /// <summary>Der Verwaltungsteil ist geladen (Admin-Setup): Nur dann gibt es unter Einstellungen die Details und das Feld Adresse.</summary>
    public bool IstAdminSetup { get; }

    /// <summary>Discord-Name des Kontos, sonst der aus der Anmeldung, sonst der SC-Name.</summary>
    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial Steckerzustand Server { get; private set; }

    [ObservableProperty]
    public partial Steckerzustand Sprache { get; private set; }

    public string ServerWort => Wort(Server);

    public string SpracheWort => Wort(Sprache);

    /// <summary>"Online: 12" aus Wer ist da, getrennt "Online: -".</summary>
    public string OnlineText => Verbindung.IstAktiv ? "Online: " + WerIstDa.Anzahl.ToString(CultureInfo.CurrentCulture) : "Online: -";

    /// <summary>Dieses Gerät ist freigegeben: verbunden mit Konto, oder getrennt nach einer früheren Freigabe.</summary>
    [ObservableProperty]
    public partial bool GeraetFreigegeben { get; private set; }

    /// <summary>
    /// Ob das Gerät freigegeben ist, weiß der Client noch nicht: Die Einstellungsdatei kommt aus einer Fassung vor 0.6.0 oder das Gerät
    /// war noch nie verbunden. Dann sagt die Startseite "Wird beim Verbinden geprüft" statt einer falschen Aussage (N4, Gegenprüfung Stufe 2).
    /// </summary>
    [ObservableProperty]
    public partial bool GeraetStandUnbekannt { get; private set; }

    /// <summary>Der Antrag liegt beim Admin; die Kennung steht groß zum Vorlesen da.</summary>
    public bool WartetAufFreigabe => Verbindung.IstWarteraum;

    /// <summary>Der Satz unter dem Namen.</summary>
    public string GeraetText =>
        WartetAufFreigabe ? "Wartet auf Freigabe"
        : Verbindung.Status == Verbindungsstatus.Registrierung ? "Gerät wird angemeldet"
        : GeraetFreigegeben ? "Gerät freigegeben"
        : GeraetStandUnbekannt ? "Wird beim Verbinden geprüft"
        : "Gerät noch nicht freigegeben";

    /// <summary>Getrennt steht Verbinden da, beim allerersten Start erst nach den zwei Angaben (Hörweise und Schritt Sprechtaste).</summary>
    public bool ZeigtVerbinden => Verbindung.IstGetrennt && !ErststartOffen;

    /// <summary>Sobald eine Verbindung steht oder versucht wird: Trennen.</summary>
    public bool ZeigtTrennen => !Verbindung.IstGetrennt;

    /// <summary>
    /// Die Zeile mit Verbinden und Trennen: nicht, solange der erste Start offen ist, außer es läuft schon eine Verbindung (etwa "Beim Start
    /// verbinden"): Dann bleibt Trennen da (N9, Gegenprüfung Stufe 2).
    /// </summary>
    public bool ZeigtKnopfZeile => !ErststartOffen || ZeigtTrennen;

    // ---- Erster Start (Q10, Q76, E8) ----

    /// <summary>Der Schritt "Drück jetzt deine Sprechtaste" ist offen (<see cref="TastenEinstellungen.ErststartSprechtaste"/> falsch).</summary>
    public bool SprechtasteSchrittOffen => _ablage.Aktuell.Tasten.ErststartSprechtaste == false;

    /// <summary>Beim allerersten Start: Die Hörweise fehlt oder der Schritt Sprechtaste ist offen. Verbinden kommt erst danach.</summary>
    public bool ErststartOffen => Audio.FrageOffen || SprechtasteSchrittOffen;

    /// <summary>
    /// Der Willkommenssatz steht an der Stelle von Verbinden: solange der erste Start offen ist und keine Verbindung läuft. Läuft schon eine (etwa
    /// "Beim Start verbinden"), steht dort Trennen mit der Meldung, die umrandeten Angaben bleiben (N9; so rollt Start auch dann nicht).
    /// </summary>
    public bool ZeigtErststartHinweis => ErststartOffen && !ZeigtTrennen;

    /// <summary>Der Willkommenssatz an der Stelle von Verbinden.</summary>
    public string ErststartSatz => Audio.FrageOffen && SprechtasteSchrittOffen
        ? "Willkommen. Zwei Angaben fehlen noch, dann kommt hier Verbinden."
        : "Willkommen. Eine Angabe fehlt noch, dann kommt hier Verbinden.";

    /// <summary>Was unten umrandet ist, unter dem Willkommenssatz.</summary>
    public string ErststartUmrandet => (Audio.FrageOffen, SprechtasteSchrittOffen) switch
    {
        (true, true) => "Unten umrandet: womit du hörst und deine Sprechtaste. Die Sprechtaste geht auch später.",
        (true, false) => "Unten umrandet: womit du hörst.",
        _ => "Unten umrandet: deine Sprechtaste. Sie geht auch später.",
    };

    /// <summary>Im Schritt Sprechtaste das Wartefeld "Wartet auf deine Taste ...", sonst (nach Esc, ohne globale Tasten) "Belegen".</summary>
    public bool ZeigtWarteFeld => Sprechtaste.Wartet;

    /// <inheritdoc cref="ZeigtWarteFeld"/>
    public bool ZeigtSchrittBelegen => !Sprechtaste.Wartet;

    /// <summary>Die belegten Sprechtasten als Marken mit Entfernen und "Ändern" (E17), außerhalb des Schritts.</summary>
    public bool ZeigtSprechtasteBelegt => !SprechtasteSchrittOffen && !Sprechtaste.Wartet && !Tasten.OhneSprechtaste;

    /// <summary>"Ändern" oder "Belegen" wartet auf die Taste: das Wartefeld mit "Abbrechen".</summary>
    public bool ZeigtSprechtasteWarten => !SprechtasteSchrittOffen && Sprechtaste.Wartet;

    /// <summary>Keine Sprechtaste und kein Schritt (nach "Später" oder alle entfernt): die Warnung mit "Belegen" (Q76).</summary>
    public bool ZeigtSprechtasteWarnung => !SprechtasteSchrittOffen && !Sprechtaste.Wartet && Tasten.OhneSprechtaste;

    /// <summary>Hovertext an "Ändern", ausführlich wie bei den Klangmodulen.</summary>
    public const string AendernHilfe = "Ändern: Wartet auf die nächste Taste, Maustaste oder den Knopf am Joystick und macht sie zu deiner Sprechtaste; "
        + "die bisherigen Sprechtasten fallen dabei weg. Sie wirkt auch, wenn das Spiel vorn ist; nimm einen Knopf, den das Spiel nicht braucht, "
        + "etwa auf einer Shift-Ebene des Joysticks. Esc bricht ab und lässt alles, wie es war. Mehrere Sprechtasten und ihren Nachlauf stellst du "
        + "unter Einstellungen > Tasten > Erweitert ein.";

    /// <summary>Hovertext an "Belegen" im Schritt und in der Warnung.</summary>
    public const string BelegenHilfe = "Belegen: Wartet auf die nächste Taste, Maustaste oder den Knopf am Joystick und macht sie zu deiner Sprechtaste. "
        + "Solange du sie hältst, sendest du in deinen Sendekanal, auch in Kanälen auf Taste wie Global. Sie wirkt auch, wenn das Spiel vorn ist; "
        + "nimm einen Knopf, den das Spiel nicht braucht. Esc bricht ab.";

    /// <summary>Hovertext an "Später".</summary>
    public const string SpaeterHilfe = "Später: Lässt die Sprechtaste für jetzt aus, danach kommt Verbinden. Senden mit Stimme geht trotzdem; in Kanälen "
        + "auf Taste, etwa Global, geht ohne Sprechtaste aber nichts hinaus. Start erinnert dich daran, bis eine belegt ist, und dort belegst du sie "
        + "mit einem Klick.";

    /// <summary>
    /// Hovertext am Wartefeld im Schritt Sprechtaste beim ersten Start (Befund der Prüfung): Der Schritt wartet von selbst, solange Start vorn ist,
    /// darum zählt nicht, womit man das Fenster bedient.
    /// </summary>
    public const string WarteFeldHilfe = "Wartet auf deine Taste: Die nächste Taste, Maustaste oder der nächste Knopf am Joystick wird deine Sprechtaste, "
        + "solange Start vorn ist. Womit du das Fenster bedienst, zählt hier nicht: Tab, Pfeile, Eingabe, Leertaste, Bild auf und ab und ein Klick mit "
        + "der linken, rechten oder mittleren Maustaste. Strg, Alt, Umschalt und Windows zählen nur allein, beim Loslassen; mit einer anderen Taste "
        + "zusammen, etwa beim Wechsel der Seite, ist es ein Kürzel und zählt nicht. Esc bricht ab. Unter Einstellungen > Tasten > Erweitert "
        + "lässt sich jede Taste belegen.";

    /// <summary>Hovertext an "Abbrechen", während Start auf die Taste wartet.</summary>
    public const string AbbrechenHilfe = "Abbrechen: Hört auf, auf die Taste zu warten; deine Sprechtasten bleiben, wie sie waren. Esc auf der Tastatur "
        + "tut dasselbe.";

    /// <summary>
    /// Beginnt das Warten auf die Sprechtaste, ohne Frist, wenn der Schritt offen ist, es globale Tasten gibt und Esc es in dieser Sitzung nicht
    /// abgebrochen hat (E8). Das Hauptmodell ruft es, solange Start vorn und das Fenster aktiv ist; ohne Quelle geschieht nichts, und es kommt
    /// kein Hinweis.
    /// </summary>
    public void ErwartenStarten()
    {
        if (!SprechtasteSchrittOffen || !Tasten.Verfuegbar || Sprechtaste.Wartet || _erwartenAbgebrochen) return;
        Warten = WartenAsync(Timeout.InfiniteTimeSpan, ersetzen: false);
    }

    /// <summary>
    /// Beendet ein Warten, das diese Seite begonnen hat, still (E8). Beim Verlassen von Start jedes; ist nur das Fenster nicht mehr vorn
    /// (<paramref name="nurOhneFrist"/>), nur das Warten ohne Frist des Schritts beim ersten Start (Befund der Prüfung): "Ändern" und "Belegen" in der
    /// Warnung warten mit Frist weiter wie unter Einstellungen, damit sich eine rechte oder mittlere Maustaste mit einem Klick außerhalb belegen lässt.
    /// </summary>
    public void ErwartenBeenden(bool nurOhneFrist = false)
    {
        if (_startWartet && Sprechtaste.Wartet && (!nurOhneFrist || _startWartetOhneFrist)) Tasten.WartenBeenden();
    }

    /// <summary>Das letzte Warten, das diese Seite begonnen hat (Tests warten darauf).</summary>
    internal Task? Warten { get; private set; }

    /// <summary>Das laufende Warten dieser Seite hat keine Frist (der Schritt beim ersten Start, E8).</summary>
    private bool _startWartetOhneFrist;

    /// <summary>
    /// Wartet auf die Sprechtaste. Ohne Frist ist es der Schritt beim ersten Start: Dann vorsichtig (Befund der Prüfung), denn er wartet auch, wenn
    /// man das Fenster nur bedient; Tab, Pfeile, Eingabe und Leertaste bedienen dann das Fenster, Strg, Alt, Umschalt und Windows zählen nur allein
    /// (<see cref="Funk.Klang.Eingabe.ISprechtasteQuelle.NaechsteTasteAsync(bool, CancellationToken)"/>).
    /// </summary>
    private async Task WartenAsync(TimeSpan? frist, bool ersetzen)
    {
        var nummer = ++_warteNummer;
        var ohneFrist = frist == Timeout.InfiniteTimeSpan;
        _startWartet = true;
        _startWartetOhneFrist = ohneFrist;
        try
        {
            var ergebnis = await Tasten.BelegenAsync(Sprechtaste, frist, ersetzen, vorsichtig: ohneFrist).ConfigureAwait(true);
            // Esc oder Abbrechen im Schritt: nicht von selbst wieder warten, dort steht jetzt "Belegen".
            if (ergebnis == Belegergebnis.Abgebrochen && SprechtasteSchrittOffen) _erwartenAbgebrochen = true;
        }
        finally
        {
            if (nummer == _warteNummer)
            {
                _startWartet = false;
                _startWartetOhneFrist = false;
            }
            ErststartGeaendert();
        }
    }

    private bool TastenDa() => Tasten.Verfuegbar;

    /// <summary>Gesperrt-Grund von "Belegen" und "Ändern" ohne globale Tasten.</summary>
    public static string OhneTastenGrund => TastenViewModel.OhneQuelleGrund;

    /// <summary>"Ändern" (E17): wartet auf eine Taste, die alle bisherigen Sprechtasten ersetzt; Esc lässt alles stehen.</summary>
    [RelayCommand(CanExecute = nameof(TastenDa))]
    private void SprechtasteAendern() => Warten = WartenAsync(null, ersetzen: true);

    /// <summary>
    /// "Belegen": im Schritt (nach Esc) wieder ohne Frist und danach wieder von selbst; in der Warnung mit der üblichen Frist (<see cref="TastenViewModel.Frist"/>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(TastenDa))]
    private void SprechtasteBelegen()
    {
        if (Sprechtaste.Wartet) return;
        var schritt = SprechtasteSchrittOffen;
        if (schritt) _erwartenAbgebrochen = false;
        Warten = WartenAsync(schritt ? Timeout.InfiniteTimeSpan : null, ersetzen: false);
    }

    /// <summary>"Abbrechen" während Start wartet: wie Esc, die Sprechtasten bleiben.</summary>
    [RelayCommand]
    private void SprechtasteAbbrechen()
    {
        if (Sprechtaste.Wartet) _ = Tasten.BelegenAsync(Sprechtaste);
    }

    /// <summary>"Später" (Q76): erledigt den Schritt Sprechtaste ohne Taste; danach warnt Start, bis eine belegt ist.</summary>
    [RelayCommand(CanExecute = nameof(SprechtasteSchrittOffen))]
    private void Spaeter()
    {
        var t = _ablage.Aktuell.Tasten;
        if (t.ErststartSprechtaste != false) return;
        if (_startWartet) Tasten.WartenBeenden();
        t.ErststartSprechtaste = true;
        _ablage.Vormerken();
        ErststartGeaendert();
    }

    /// <summary>Nach jeder Änderung an Sprechtaste, Schritt oder Hörweise: alles, was davon abhängt.</summary>
    private void ErststartGeaendert()
    {
        OnPropertyChanged(nameof(SprechtasteSchrittOffen));
        OnPropertyChanged(nameof(ErststartOffen));
        OnPropertyChanged(nameof(ZeigtErststartHinweis));
        OnPropertyChanged(nameof(ErststartSatz));
        OnPropertyChanged(nameof(ErststartUmrandet));
        OnPropertyChanged(nameof(ZeigtWarteFeld));
        OnPropertyChanged(nameof(ZeigtSchrittBelegen));
        OnPropertyChanged(nameof(ZeigtSprechtasteBelegt));
        OnPropertyChanged(nameof(ZeigtSprechtasteWarten));
        OnPropertyChanged(nameof(ZeigtSprechtasteWarnung));
        OnPropertyChanged(nameof(ZeigtVerbinden));
        OnPropertyChanged(nameof(ZeigtKnopfZeile));
        OnPropertyChanged(nameof(VerbindenGesperrt));
        VerbindenCommand.NotifyCanExecuteChanged();
        SpaeterCommand.NotifyCanExecuteChanged();
    }

    private void AudioGeaendert(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AudioViewModel.HatGeraeteWarnung):
                OnPropertyChanged(nameof(ZeigtHoerprobe));
                OnPropertyChanged(nameof(ZeigtAudioAus));
                break;
            case nameof(AudioViewModel.Laeuft):
                OnPropertyChanged(nameof(ZeigtAudioAus));
                break;
            case nameof(AudioViewModel.FrageOffen):
                ErststartGeaendert();
                break;
        }
    }

    // ---- Hören: Hörprobe und Audio (Q75, E9) ----

    /// <summary>
    /// Die Hörprobe steht da, außer bei einer Gerätewarnung: Dann läuft Mikrofon oder Wiedergabe nicht, ohne beide geht keine Hörprobe, und
    /// die Warnung mit "Erneut versuchen" nimmt ihren Platz ein. So bleibt Start in Klein auch mit Gerätewarnung ohne Rollbalken.
    /// </summary>
    public bool ZeigtHoerprobe => !Audio.HatGeraeteWarnung;

    /// <summary>
    /// Audio läuft nicht (etwa "Audio beim Start öffnen" aus), ohne Gerätewarnung: an ihrer Stelle "Mikrofon und Wiedergabe sind zu" mit
    /// "Audio starten" (E9; bis 0.9.3 ging das nur im Bereich Audio).
    /// </summary>
    public bool ZeigtAudioAus => !Audio.Laeuft && !Audio.HatGeraeteWarnung;

    /// <summary>Hovertext an "Audio starten".</summary>
    public const string AudioStartenHilfe = "Audio starten: Öffnet Mikrofon und Wiedergabe. Ab Werk geschieht das beim Start von selbst; hast du "
        + "\"Audio beim Start öffnen\" ausgeschaltet oder ging es nicht auf, startest du es hier. Ohne Audio hörst du nichts, und es geht nichts hinaus.";

    /// <summary>Die eine Hörprobe (Q75): geht getrennt und verbunden, nur mit laufendem Audio.</summary>
    private bool KannHoerprobe() => Audio.Hoerprobe.ProbeCommand.CanExecute(null);

    /// <summary>Warum die Hörprobe gerade nicht geht, für den Hilfetext des gesperrten Knopfs (aus der Hörprobe selbst).</summary>
    public string HoerprobeGesperrt => Audio.Hoerprobe.ProbeGesperrt ?? "Für die Hörprobe müssen Mikrofon und Wiedergabe laufen.";

    /// <summary>Neben dem Knopf: bereit der Satz, was sie tut; sonst der Stand der Hörprobe (Aufnahme, Wiedergabe, was fehlt).</summary>
    public string HoerprobeText => Audio.Hoerprobe.ProbeText is { Length: > 0 } t ? t : HoerprobeBereitText;

    /// <summary>"Hörprobe", während einer Probe "Abbrechen".</summary>
    public string HoerprobeKnopfText => Audio.Hoerprobe.ProbeKnopfZusatz == "abbrechen" ? "Abbrechen" : "Hörprobe";

    /// <summary>Hovertext an der Hörprobe.</summary>
    public const string HoerprobeHilfe = "Hörprobe: Nimmt dich fünf Sekunden auf und spielt es dir vor, so wie die anderen dich hören, mit Rauschfilter, "
        + "Klangfarbe und Funkkodierung. Das geht getrennt und verbunden; gesendet wird dabei nur, was du mit der Sprechtaste sprichst. Während einer "
        + "Probe bricht der Knopf sie ab.";

    [RelayCommand(CanExecute = nameof(KannHoerprobe))]
    private void Hoerprobe() => Audio.Hoerprobe.ProbeCommand.Execute(null);

    // ---- Verbinden ----

    /// <summary>Warum Verbinden gerade nicht geht, für den Hilfetext des gesperrten Knopfs.</summary>
    public string VerbindenGesperrt =>
        Audio.FrageOffen ? ErstHoerweise : SprechtasteSchrittOffen ? ErstSprechtaste : Verbindung.WasFehlt ?? "Erst trennen, dann wieder verbinden.";

    private bool KannVerbinden() => !ErststartOffen && Verbindung.VerbindenCommand.CanExecute(null);

    [RelayCommand(CanExecute = nameof(KannVerbinden))]
    private Task VerbindenAsync() => Verbindung.VerbindenCommand.ExecuteAsync(null);

    private static string Wort(Steckerzustand z) => z switch
    {
        Steckerzustand.Verbindet => "verbindet",
        Steckerzustand.Verbunden => "verbunden",
        Steckerzustand.Tunnel => "Tunnel",
        _ => "getrennt",
    };

    private void Aktualisieren()
    {
        var status = Verbindung.Status;
        Server = status switch
        {
            Verbindungsstatus.Getrennt => Steckerzustand.Getrennt,
            Verbindungsstatus.Verbinden => Steckerzustand.Verbindet,
            _ => Steckerzustand.Verbunden,
        };
        Sprache = status switch
        {
            Verbindungsstatus.Aktiv => Steckerzustand.Verbunden,
            Verbindungsstatus.AktivTunnel => Steckerzustand.Tunnel,
            Verbindungsstatus.Abgleich => Steckerzustand.Verbindet,
            _ => Steckerzustand.Getrennt,
        };
        // Freigegeben merken: Mit Konto verbunden heißt freigegeben; ein neuer Antrag (Gerät entfernt) vergisst es wieder.
        var v = _ablage.Aktuell.Verbindung;
        // Seit 0.6.3 verbindet der Client nach Bye{ENTFERNT} nicht mehr von selbst (kein stiller neuer Antrag). Dann ist das Gerät
        // nicht mehr freigegeben; sonst sagte die Startseite "Gerät freigegeben" neben "vom Funk abgemeldet". Der Zustand kommt
        // ausdrücklich aus der Lage (FunkLage.Entfernt), nicht aus dem Text des Hinweises, und löst selbst ein Neurechnen aus.
        var entfernt = Verbindung.Entfernt;
        var frei = status switch
        {
            Verbindungsstatus.Abgleich or Verbindungsstatus.Aktiv or Verbindungsstatus.AktivTunnel => true,
            Verbindungsstatus.Registrierung or Verbindungsstatus.Warteraum => false,
            _ when entfernt => false,
            _ => v.Freigegeben,
        };
        if (frei is { } bekannt && bekannt != v.Freigegeben)
        {
            v.Freigegeben = bekannt;
            _ablage.Vormerken();
        }
        // E12: Mit der ersten Freigabe geht "Beim Start verbinden" einmal an (danach gilt der Schalter unter Einstellungen > Funkfeld und Darstellung).
        if (v.NachFreigabeVoreinstellen())
        {
            _ablage.Vormerken();
            VerbindenVoreingestellt?.Invoke();
        }
        GeraetFreigegeben = frei == true;
        GeraetStandUnbekannt = frei is null;
        var a = _ablage.Aktuell.Anmeldung;
        Name = Erster(_dienst.Lage.Konto?.DiscordName, a.DiscordName, a.ScName) ?? "Noch kein Name";
        OnPropertyChanged(nameof(ServerWort));
        OnPropertyChanged(nameof(SpracheWort));
        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(WartetAufFreigabe));
        OnPropertyChanged(nameof(GeraetText));
        OnPropertyChanged(nameof(ZeigtVerbinden));
        OnPropertyChanged(nameof(ZeigtTrennen));
        OnPropertyChanged(nameof(ZeigtKnopfZeile));
        OnPropertyChanged(nameof(ZeigtErststartHinweis));
        OnPropertyChanged(nameof(VerbindenGesperrt));
        HoerprobeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HoerprobeGesperrt));
    }

    private static string? Erster(params string?[] namen) => namen.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
}
