// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.ClientKern;

namespace Funk.Client.Modelle;

/// <summary>
/// Ein Bereich des Hauptfensters (Navigation links). <paramref name="Taste"/> steht im Tooltip der Navigation;
/// <paramref name="Unterpunkt"/> rückt ihn unter den Bereich davor ein (Sprachaktivierung seit Stufe 2c, seit 0.10.0 unter Einstellungen);
/// <paramref name="Unten"/> stellt ihn ganz unten in die Navigation (Abzeichen, seit 0.11.7).
/// </summary>
public sealed record Bereich(string Kennung, string Titel, string Symbol, string Taste, object Inhalt, bool Unterpunkt = false, bool Unten = false);

/// <summary>
/// Der Bereich Verwaltung als Inhalt: Jeder Aufbau des Hauptfensters holt sich eine neue Ansicht vom Verwaltungsteil
/// (<see cref="IVerwaltungsteil.AnsichtBauen"/>), auf demselben Zustand. Der Client kennt die Verwaltung nur über diese Naht.
/// </summary>
public sealed class VerwaltungsBereich(IVerwaltungsteil teil)
{
    public IVerwaltungsteil Teil { get; } = teil;

    public object Ansicht => Teil.AnsichtBauen();
}

/// <summary>
/// Der Bereich Abzeichen als Inhalt (seit 0.11.7): eine Seite des Verwaltungsteils (<see cref="IVerwaltungsteil.AbzeichenAnsichtBauen"/>), neu bei
/// jedem Aufbau des Hauptfensters, auf demselben Zustand. In der Navigation ganz unten, nur solange die Admin-Sitzung offen ist.
/// </summary>
public sealed class AbzeichenBereich(IVerwaltungsteil teil)
{
    public IVerwaltungsteil Teil { get; } = teil;

    public object? Ansicht => Teil.AbzeichenAnsichtBauen();
}

/// <summary>
/// Hauptfenster (A25): Bereiche links, Kopfzeile mit Verbindungsstatus und Sprechern, Knopf für das Funkfeld und unten die Statuszeile
/// "Mikrofon" (seit 0.10.0, Q87: Pegel, SENDET und der Hinweissatz an einer Stelle). Seit 0.10.0 (Mischung, E1, E2) nur noch zwei Bereiche,
/// Start (Strg+1) und Einstellungen (Strg+2); dazu nur bei Bedarf Raid (Strg+5, mit ZUWEISEN), Verwaltung (Strg+9, nur Admins) und der Unterpunkt
/// Sprachaktivierung unter Einstellungen (solange sie benutzt wird, Strg+T). Bis 0.9.3 gab es dazu Audio und Meine Kanäle (Strg+2 bis Strg+4).
/// <see cref="Takt"/> läuft im Bildtakt und reicht an die Livewerte weiter.
/// </summary>
public sealed partial class HauptViewModel : ObservableObject
{
    private readonly IFunkDienst _dienst;
    private readonly IFensterDienst _fenster;
    private readonly Einstellungsablage _ablage;
    private readonly IOberflaechentakt _takt;

    /// <param name="werIstDa">"Wer ist da" (A48); ohne Angabe baut das Modell eine eigene über demselben Dienst.</param>
    /// <param name="meinProfil">Namen, Rollen, Zeichen unter Einstellungen (A46, A49); ohne Angabe eine eigene.</param>
    /// <param name="verwaltung">Der geladene Verwaltungsteil (nur mit dem Admin-Setup, A45), sonst <c>null</c>.</param>
    /// <param name="gilde">Logo der Gilde für die Startseite; ohne Angabe der eingebaute Platzhalter.</param>
    /// <param name="klangtreiber">Der Klangtreiber, etwa "Klang: WASAPI" (seit 0.10.0 unter Einstellungen > Fehlersuche, E13).</param>
    /// <param name="starthinweise">Warnungen beim Start (etwa "Globale Tasten nicht verfügbar: ..."), links in der Fußzeile; sonst <c>null</c>.</param>
    public HauptViewModel(VerbindungViewModel verbindung, AudioViewModel audio, NetzeViewModel netze, EinstellungenViewModel einstellungen,
        FunkfeldViewModel funkfeld, SchwellenViewModel schwelle, IFunkDienst dienst, IFensterDienst fenster, Einstellungsablage ablage,
        IOberflaechentakt takt, string klangtreiber, WerIstDaViewModel? werIstDa = null, MeinProfilViewModel? meinProfil = null,
        IVerwaltungsteil? verwaltung = null, BrettViewModel? brett = null, Gildenpaket? gilde = null, string? starthinweise = null,
        MeldungenViewModel? meldungen = null)
    {
        ArgumentNullException.ThrowIfNull(verbindung);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(netze);
        ArgumentNullException.ThrowIfNull(einstellungen);
        ArgumentNullException.ThrowIfNull(funkfeld);
        ArgumentNullException.ThrowIfNull(schwelle);
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(fenster);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(takt);
        Verbindung = verbindung;
        Audio = audio;
        Netze = netze;
        Einstellungen = einstellungen;
        Funkfeld = funkfeld;
        // Klangmodul Sprachaktivierung (0.9.0): Der Schalter steht in Audio, wirken muss er im Funkfeld (A10).
        audio.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AudioViewModel.SprachaktivierungAn)) return;
            funkfeld.SprachaktivierungModulGeaendert();
            // Seit dem Befund der Prüfung hängt "Genau einstellen ..." (Strg+T) an diesem Schalter, nicht nur am Unterpunkt.
            if (Navigation is not null) SprachaktivierungZeigen();
        };
        Schwelle = schwelle;
        _dienst = dienst;
        _fenster = fenster;
        _ablage = ablage;
        _takt = takt;
        Klangtreiber = klangtreiber;
        Starthinweise = string.IsNullOrWhiteSpace(starthinweise) ? null : starthinweise.Trim();
        // Seit 0.11.5 ohne das gelbe "!" an Dauersendern (das bleibt im Funkfeld); verdrahtet wird die Spalte unten, wenn Raid und Verwaltung da sind.
        WerIstDa = werIstDa ?? new WerIstDaViewModel(dienst, ablage, takt);
        MeinProfil = meinProfil ?? new MeinProfilViewModel(dienst, ablage, takt);
        Brett = brett ?? new BrettViewModel(dienst, takt);
        // Meldesystem (0.12.0): das Feld der Leitung unter dem Brett und "Bereit melden" in der Titelleiste.
        Meldungen = meldungen ?? new MeldungenViewModel(dienst, () => einstellungen.Meldungen.Aktuell, () => Environment.TickCount64);
        Brett.Meldungen = Meldungen;
        Bereit = new Bereitmeldung(dienst);
        einstellungen.Meldungen.PropertyChanged += (_, _) => Meldungen.BilderLaden();
        // Seit 0.10.0 (Mischung): Start trägt die Sprechtaste (dieselben Tasten wie unter Einstellungen) und, mit Einheiten, Einsteigen (E10).
        Start = new StartViewModel(verbindung, audio, WerIstDa, einstellungen.Tasten, netze, dienst, ablage, gilde ?? Gildenpaket.Eingebaut, verwaltung is not null);
        // E12: "Beim Start verbinden" nach der Freigabe einmal an; der Schalter unter Einstellungen zieht nach.
        Start.VerbindenVoreingestellt += einstellungen.VerbindenBeimStartGeaendert;
        einstellungen.MeinProfil = MeinProfil;
        einstellungen.Details = verwaltung is null ? null : verbindung;
        // Seit 0.10.0 (E6): die Abschnitte mit ihren Klappbereichen, sobald Audio, Kanäle und Profil bekannt sind (die Sprachaktivierung seit Q117 nicht mehr dabei).
        einstellungen.AbschnitteBauen(audio, netze, Klangtreiber, Starthinweise);
        MeinProfil.AnzeigeGeaendert += () =>
        {
            WerIstDa.Aktualisieren();
            Funkfeld.ZeichenGeaendert();
        };
        _verwaltungsteil = verwaltung;
        _verwaltung = verwaltung is null ? null : new Bereich(VerwaltungKennung, "Verwaltung", "Symbol.Schluessel", "Strg+9", new VerwaltungsBereich(verwaltung));
        // Abzeichen (0.11.7, Sarevoc): ganz unten in der Navigation, nur nach der Admin-Anmeldung (laufende Admin-Sitzung); ohne Tastenkürzel.
        _abzeichen = verwaltung is null ? null : new Bereich(AbzeichenKennung, "Abzeichen", "Symbol.Abzeichen", AbzeichenTooltip, new AbzeichenBereich(verwaltung), Unten: true);
        if (verwaltung is not null) verwaltung.AdminSitzungGeaendert += () => _takt.Einreihen(() => AbzeichenZeigen(_dienst.Lage));
        // Seit 0.10.0 (Mischung Q87, E1): Start und Einstellungen; Audio und Meine Kanäle sind aufgelöst (Inhalt auf Start und unter Einstellungen).
        Bereiche =
        [
            new Bereich(StartKennung, "Start", "Symbol.Start", "Strg+1", Start),
            new Bereich(EinstellungenKennung, "Einstellungen", "Symbol.Einstellungen", "Strg+2", einstellungen),
        ];
        // Sarevoc 14.10.2026: "wir nennen die Seite nun Funkfeld Zuteilung" (0.11.2 bis 0.11.5 "Raid", bis 0.11.1 "das Brett").
        _raid = new Bereich(RaidKennung, "Funkfeld-Zuteilung", "Symbol.Brett", "Strg+5", Brett);
        _sprachaktivierung = new Bereich(SprachaktivierungKennung, "Sprachaktivierung", "Symbol.Schwelle",
            "Sprachaktivierung genau einstellen (Strg+T)", schwelle, Unterpunkt: true);
        // Statuszeile "Mikrofon" (0.10.0, E4): dieselben Meldungen wie die Grafik der Sprachaktivierung, Getrennt nach der Lage.
        // Bis zur ersten Pegelmeldung steht die Linie, wo die Sprachaktivierung sie hat (Befund der Prüfung, sonst am linken Rand).
        Statuszeile = new StatuszeileViewModel(schwelle.Verlauf, audio, () => _dienst.Lage.IstAktiv, () => schwelle.LinieDb);
        Navigation = [.. Bereiche];
        Bereiche.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Tastenhinweis));
            NavigationAbgleichen();
        };
        funkfeld.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FunkfeldViewModel.SprachaktivierungBenutzt)) SprachaktivierungZeigen();
        };
        RaidZeigen(_dienst.Lage);
        VerwaltungZeigen(_dienst.Lage);
        AbzeichenZeigen(_dienst.Lage);
        // Wer ist da (seit 0.11.5, Sarevoc 14.10.2026): die Wippe über den Pegel je Person unter Empfangen, der Name öffnet die Raid-Seite, der Status
        // im Admin-Setup die Verwaltung bei diesem Mitglied.
        WerIstDa.Anbinden(netze, RaidOeffnen, verwaltung is null ? null : MitgliedZeigen);
        SprachaktivierungZeigen();
        // Q25 (Sarevoc, Klangstufe C1): Jeder Start beginnt auf der Startseite. Den zuletzt offenen Bereich merkt nur die laufende Sitzung
        // (dieses Modell lebt so lange wie das Programm); bis 0.6.0 stellte die Einstellungsdatei ihn beim Start wieder her.
        AktiverBereich = Bereiche[0];
        _dienst.LageGeaendert += () => _takt.Einreihen(Aktualisieren);
        funkfeld.Dauersend.Geaendert += Aktualisieren;
        // Registrierungscode und Rückfrage "Mit Konto X verknüpfen?" stehen auf der Startseite und warten dort nur kurz
        // (Code 5 min, Rückfrage 2 min); sie holen ihn nach vorn, egal wo Sarevoc gerade ist.
        // Nur beim Erscheinen, damit Sarevoc danach in Ruhe woanders hinschauen kann.
        var (frage, code) = (false, false);
        verbindung.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(VerbindungViewModel.HatRueckfrage) or nameof(VerbindungViewModel.IstRegistrierung))) return;
            if ((verbindung.HatRueckfrage && !frage) || (verbindung.IstRegistrierung && !code)) AktiverBereich = Bereiche[0];
            (frage, code) = (verbindung.HatRueckfrage, verbindung.IstRegistrierung);
        };
        Aktualisieren();
    }

    public VerbindungViewModel Verbindung { get; }

    public StartViewModel Start { get; }

    public AudioViewModel Audio { get; }

    public NetzeViewModel Netze { get; }

    public EinstellungenViewModel Einstellungen { get; }

    public FunkfeldViewModel Funkfeld { get; }

    public SchwellenViewModel Schwelle { get; }

    /// <summary>Die Statuszeile "Mikrofon" unten im Fenster (seit 0.10.0, Q87, E4): Pegel, SENDET, Ruhe oder Getrennt und der Hinweissatz.</summary>
    public StatuszeileViewModel Statuszeile { get; }

    /// <summary>"Wer ist da" (A48): Spalte rechts, Knopf in der Titelleiste, Strg+6.</summary>
    public WerIstDaViewModel WerIstDa { get; }

    /// <summary>Namen, eigene Rollen und Zeichen (A46, A49), unter Einstellungen.</summary>
    public MeinProfilViewModel MeinProfil { get; }

    /// <summary>"Heute als Funker" (6.15): Schalter in "Ich", Text in der Fußzeile ("als Funker bis 04:00"), Menü des Symbols.</summary>
    public FunkermodusViewModel Funkermodus => MeinProfil.Funkermodus;

    /// <summary>Das Brett der Einsatzleitung (Bereich Raid, Strg+5), nur mit ZUWEISEN in der Navigation.</summary>
    public BrettViewModel Brett { get; }

    /// <summary>Seit 0.12.0: das Feld "Meldungen" auf der Funkfeld-Zuteilung, nur für wen senden darf.</summary>
    public MeldungenViewModel Meldungen { get; }

    /// <summary>Seit 0.12.0: "Bereit melden" in der Titelleiste und für die Taste "Bereit", solange eine Bereit-Abfrage offen ist.</summary>
    public Bereitmeldung Bereit { get; }

    /// <summary>Das Brett ist vorn: Es braucht die ganze Höhe, ohne den Bildlauf der übrigen Bereiche.</summary>
    public bool IstBrett => AktiverBereich?.Inhalt is BrettViewModel;

    /// <summary>
    /// Die Seite Sprachaktivierung (Unterpunkt unter Einstellungen, seit 0.10.0; bis 0.9.3 unter Audio) ist vorn. Sie hat einen eigenen Platz
    /// ohne den Bildlauf der Bereiche und rollt um ihre feste Grafik herum. Seit 0.10.0 steht der Bereich fest dort (bis 0.9.3 setzte das
    /// Hauptfenster ihn zwischen unten und der Seite um).
    /// </summary>
    public bool IstSprachaktivierungsseite => ReferenceEquals(AktiverBereich, _sprachaktivierung);

    /// <summary>
    /// Die Verwaltung ist vorn (seit 0.8.0): Sie rollt selbst, damit die Leiste ihres Entwurfs unten stehen bleibt, und hat deshalb wie
    /// das Brett einen eigenen Platz ohne den Bildlauf der Bereiche.
    /// </summary>
    public bool IstVerwaltungsseite => AktiverBereich?.Inhalt is VerwaltungsBereich;

    /// <summary>Die Seite Abzeichen ist vorn (seit 0.11.7): Wie die Verwaltung rollt sie selbst und hat einen eigenen Platz ohne den Bildlauf der Bereiche.</summary>
    public bool IstAbzeichenseite => AktiverBereich?.Inhalt is AbzeichenBereich;

    /// <summary>Der Bereich steht im Bildlauf (alle außer Brett, Seite Sprachaktivierung, Verwaltung und Abzeichen).</summary>
    public bool MitBildlauf => !IstBrett && !IstSprachaktivierungsseite && !IstVerwaltungsseite && !IstAbzeichenseite;

    /// <summary>Was im Bildlauf der Bereiche steht; Brett, Seite Sprachaktivierung und Verwaltung haben ihren eigenen Platz ohne Bildlauf.</summary>
    public object? SeitenInhalt => MitBildlauf ? AktiverBereich?.Inhalt : null;

    /// <summary>Das Brett, solange es vorn ist; sonst nichts, damit seine Ansicht nicht im Verborgenen mitläuft.</summary>
    public BrettViewModel? BrettInhalt => IstBrett ? Brett : null;

    /// <summary>Die Verwaltung, solange sie vorn ist (seit 0.8.0, eigener Platz).</summary>
    public object? VerwaltungInhalt => IstVerwaltungsseite ? AktiverBereich?.Inhalt : null;

    /// <summary>Die Seite Abzeichen, solange sie vorn ist (seit 0.11.7, eigener Platz).</summary>
    public object? AbzeichenInhalt => IstAbzeichenseite ? AktiverBereich?.Inhalt : null;

    /// <summary>
    /// Die Bereiche mit Tastenkürzel: Start (Strg+1), Einstellungen (Strg+2), Raid (Strg+5), Verwaltung (Strg+9). Raid und Verwaltung erscheinen
    /// nur, wenn es sie gibt (ZUWEISEN; Verwaltungsteil und passende Meldung vom Server), und verschwinden ohne Neustart.
    /// </summary>
    public ObservableCollection<Bereich> Bereiche { get; }

    /// <summary>
    /// Was die Leiste links zeigt: die <see cref="Bereiche"/> in ihrer Reihenfolge und unter Einstellungen (seit 0.10.0, E3; bis 0.9.3 unter
    /// Audio) der Unterpunkt Sprachaktivierung, solange sie benutzt wird (<see cref="FunkfeldViewModel.SprachaktivierungBenutzt"/>). Eigene Liste,
    /// damit Strg+1, Strg+2 und alles, was nach Stellen zählt, bei den Bereichen bleibt.
    /// </summary>
    public ObservableCollection<Bereich> Navigation { get; }

    /// <summary>Kennung des Unterpunkts Sprachaktivierung (seit Stufe 2c; seit 0.10.0 unter Einstellungen).</summary>
    public const string SprachaktivierungKennung = "sprachaktivierung";

    /// <summary>Kennung des Bereichs Einstellungen; unter ihm steht seit 0.10.0 der Unterpunkt Sprachaktivierung.</summary>
    public const string EinstellungenKennung = "einstellungen";

    private readonly Bereich _sprachaktivierung;

    public const string VerwaltungKennung = "verwaltung";

    /// <summary>Kennung der Startseite (bis 0.5.x "verbindung"; eine alte Einstellungsdatei landet über Bereiche[0] ohnehin hier).</summary>
    public const string StartKennung = "start";

    /// <summary>Kennung des Bereichs Raid (Brett der Einsatzleitung, Strg+5).</summary>
    public const string RaidKennung = "raid";

    private readonly Bereich _raid;

    private readonly Bereich? _verwaltung;
    private readonly IVerwaltungsteil? _verwaltungsteil;

    /// <summary>Kennung des Bereichs Abzeichen (seit 0.11.7, ganz unten, nur in der Admin-Sitzung).</summary>
    public const string AbzeichenKennung = "abzeichen";

    /// <summary>Tooltip am Eintrag Abzeichen (statt eines Tastenkürzels).</summary>
    public const string AbzeichenTooltip = "Abzeichen vergeben: Ränge und Tätigkeiten der Mitglieder (nur in der Admin-Sitzung)";

    private readonly Bereich? _abzeichen;

    // ---- Beenden mit offener Admin-Sitzung (0.9.1, Sarevoc 08.10.2026): ein Klick mehr zur Bestätigung ----

    public const string BeendenFrageText = "Du bist als Admin freigeschaltet. Wirklich beenden?";
    public const string BeendenFrageHinweis = "Beim Beenden endet auch die Admin-Sitzung; danach brauchst du wieder einen Code aus der App.";

    /// <summary>Die Admin-Sitzung ist offen: Das Hauptfenster fragt beim Beenden nach.</summary>
    public bool AdminSitzungOffen => _verwaltungsteil?.AdminSitzungOffen == true;

    /// <summary>Die Rückfrage "Wirklich beenden?" liegt über dem Fenster.</summary>
    [ObservableProperty]
    public partial bool BeendenFrageOffen { get; set; }

    /// <summary>Bestätigt: Die App schließt das Programm, diesmal ohne Rückfrage.</summary>
    public event Action? BeendenBestaetigt;

    [RelayCommand]
    private void BeendenBestaetigen()
    {
        BeendenFrageOffen = false;
        BeendenBestaetigt?.Invoke();
    }

    [RelayCommand]
    private void BeendenAbbrechen() => BeendenFrageOffen = false;

    /// <summary>
    /// Der Klangtreiber, etwa "Klang: WASAPI" oder "Klang: Attrappe, kein Audiogerät geöffnet" (seit 0.10.0, E13): steht unter Einstellungen >
    /// Fehlersuche, nicht mehr in der Fußzeile (bis 0.9.3 zusammen mit den Warnungen als "KlangHinweis").
    /// </summary>
    public string Klangtreiber { get; }

    /// <summary>
    /// Warnungen beim Start, etwa "Globale Tasten nicht verfügbar: ..." oder "Klangmaschine nicht verfügbar: ..." (seit 0.10.0, E13): links in der
    /// Fußzeile, damit sie nicht in einem zugeklappten Bereich verschwinden; ohne Warnung <c>null</c>.
    /// </summary>
    public string? Starthinweise { get; }

    /// <summary>
    /// Die Tastenkürzel in der Fußzeile, nur für Bereiche, die es gerade gibt (seit 0.10.0, E2): "Strg+1 Start  Strg+2 Einstellungen", Strg+5 mit
    /// dem Brett (ZUWEISEN), "Strg+6 Wer ist da", Strg+9 mit der Verwaltung, "Strg+F Funkfeld". Strg+T steht nicht da: Es öffnet nur die Seite
    /// Sprachaktivierung, und die gibt es nur, wenn sie benutzt wird. Bis 0.9.3 "Strg+1-4 Bereiche"; die Karte Tastatur unter Einstellungen ist
    /// seitdem weg, die Kürzel stehen hier.
    /// </summary>
    public string Tastenhinweis =>
        "Strg+1 Start  Strg+2 Einstellungen  " + (Bereiche.Contains(_raid) ? "Strg+5 Funkfeld-Zuteilung  " : "") + "Strg+6 Wer ist da  "
        + (_verwaltung is not null && Bereiche.Contains(_verwaltung) ? "Strg+9 Verwaltung  " : "") + "Strg+F Funkfeld";

    [ObservableProperty]
    public partial Bereich AktiverBereich { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    [ObservableProperty]
    public partial bool IstAktiv { get; private set; }

    /// <summary>Wer gerade spricht, für die Kopfzeile.</summary>
    [ObservableProperty]
    public partial string Sprechend { get; private set; } = "";

    /// <summary>Die Ansicht soll den Tastaturfokus auf die Schwellengrafik legen (nach Strg+T oder "Genau einstellen ...").</summary>
    public event Action? SchwelleFokussieren;

    [RelayCommand]
    private void FunkfeldZeigen() => _fenster.FunkfeldZeigen();

    /// <summary>
    /// Strg+T und "Genau einstellen ..." (seit 0.10.0, E2 und E3): öffnet die Seite Sprachaktivierung und legt den Fokus auf die Grafik. Seit dem
    /// Befund der Prüfung geht das, solange "Senden mit Stimme" an ist, auch ohne den Unterpunkt (getrennt oder mit allen Kanälen auf Taste), damit
    /// sich die Sprachaktivierung vor dem Verbinden prüfen und einstellen lässt wie bis 0.9.3; der Unterpunkt steht dann da, solange die Seite vorn
    /// ist. Ist "Senden mit Stimme" aus, bewirkt es nichts. Bis 0.9.3 klappte Strg+T den Bereich unten im Fenster auf und zu.
    /// </summary>
    [RelayCommand(CanExecute = nameof(KannSprachaktivierungEinstellen))]
    private void SprachaktivierungEinstellen()
    {
        if (!KannSprachaktivierungEinstellen()) return;
        // Erst den Unterpunkt in die Liste, dann wählen: Die Liste links bindet SelectedItem an AktiverBereich, und einen Eintrag, den sie nicht
        // hat, setzte sie auf nichts zurück.
        _seiteOeffnet = true;
        try
        {
            NavigationAbgleichen();
            AktiverBereich = _sprachaktivierung;
        }
        finally
        {
            _seiteOeffnet = false;
        }
        SchwelleFokussieren?.Invoke();
    }

    /// <summary>Strg+T oder "Genau einstellen ..." öffnet gerade die Seite; der Unterpunkt kommt schon vorher in die Liste.</summary>
    private bool _seiteOeffnet;

    /// <summary>Den Unterpunkt Sprachaktivierung gibt es gerade (<see cref="Navigation"/>), oder "Senden mit Stimme" ist an.</summary>
    private bool KannSprachaktivierungEinstellen() => Navigation.Contains(_sprachaktivierung) || Audio.SprachaktivierungAn;

    /// <summary>Gesperrt-Grund von "Genau einstellen ..." (E3, seit dem Befund der Prüfung): nur bei "Senden mit Stimme" aus.</summary>
    public const string SprachaktivierungGesperrt = "Senden mit Stimme ist aus; schalte es auf Start an, dann lässt sich die Sprachaktivierung hier genau einstellen.";

    /// <summary>
    /// Strg+1 Start und Strg+2 Einstellungen; Strg+5 das Brett und Strg+9 die Verwaltung, wenn es sie gibt (E2). Strg+3 und Strg+4 gibt es seit
    /// 0.10.0 nicht mehr (bis 0.9.3 Meine Kanäle und Einstellungen); eine andere Zahl bewirkt nichts.
    /// </summary>
    [RelayCommand]
    private void BereichWaehlen(string? nummer)
    {
        if (!int.TryParse(nummer, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)) return;
        switch (n)
        {
            case 1:
                AktiverBereich = Bereiche.First(b => b.Kennung == StartKennung);
                break;
            case 2:
                AktiverBereich = Bereiche.First(b => b.Kennung == EinstellungenKennung);
                break;
            case 5:
                if (Bereiche.Contains(_raid)) AktiverBereich = _raid;
                break;
            case 9:
                if (Bereiche.FirstOrDefault(b => b.Kennung == VerwaltungKennung) is { } v) AktiverBereich = v;
                break;
        }
    }

    /// <summary>
    /// Raid in der Navigation nur mit ZUWEISEN (Admin, Einzelrecht oder laufender Auftrag), gleich nach Einstellungen; es erscheint und verschwindet
    /// ohne Neustart. Endet der Auftrag, während das Brett vorn ist, geht es zum Einsteigen: seit 0.10.0 auf Start (E10; bis 0.9.3 Meine Kanäle).
    /// </summary>
    private void RaidZeigen(FunkLage l)
    {
        // Seit 0.12.0 auch, wer Meldungen senden darf, ohne das Brett zu haben (Führung mit Haken): Die Seite zeigt dann nur das Feld "Meldungen".
        var soll = BrettViewModel.Gilt(l) || MeldungenViewModel.Gilt(l);
        var da = Bereiche.Contains(_raid);
        if (soll && !da) Bereiche.Insert(Bereiche.IndexOf(Bereiche.First(b => b.Kennung == EinstellungenKennung)) + 1, _raid);
        if (!soll && da)
        {
            if (ReferenceEquals(AktiverBereich, _raid)) AktiverBereich = Bereiche[0];
            Bereiche.Remove(_raid);
        }
    }

    /// <summary>
    /// Ein Name in "Wer ist da" (seit 0.11.5, Sarevoc 14.10.2026: "Wenn der Name des Funkers geklickt wird, macht die Raid-Einteilung-Seite auf"): die
    /// Raid-Seite vorn, nur wenn es sie gibt (ZUWEISEN); die Navigation wird vorher auf den Stand der Lage gebracht.
    /// </summary>
    private void RaidOeffnen()
    {
        RaidZeigen(_dienst.Lage);
        if (Bereiche.Contains(_raid)) AktiverBereich = _raid;
    }

    /// <summary>
    /// Der Status in "Wer ist da" im Admin-Setup (seit 0.11.5): die Verwaltung vorn, Seite Mitglieder, dieses Konto gewählt
    /// (<see cref="IVerwaltungsteil.MitgliedZeigen"/>); ohne Bereich Verwaltung nichts.
    /// </summary>
    private void MitgliedZeigen(uint kontoId)
    {
        VerwaltungZeigen(_dienst.Lage);
        if (_verwaltung is null || _verwaltungsteil is null || !Bereiche.Contains(_verwaltung)) return;
        _verwaltungsteil.MitgliedZeigen(kontoId);
        AktiverBereich = _verwaltung;
    }

    /// <summary>
    /// Unterpunkt Sprachaktivierung unter Einstellungen (seit 0.10.0, E3) nur, solange sie benutzt wird
    /// (<see cref="FunkfeldViewModel.SprachaktivierungBenutzt"/>) oder ihre Seite vorn ist (über "Genau einstellen ..." auch ohne Benutzung, Befund der
    /// Prüfung). Geht "Senden mit Stimme" aus, während die Seite vorn ist und sie nicht benutzt wird, geht es zu Einstellungen.
    /// </summary>
    private void SprachaktivierungZeigen()
    {
        if (!Funkfeld.SprachaktivierungBenutzt && !Audio.SprachaktivierungAn && IstSprachaktivierungsseite)
            AktiverBereich = Bereiche.First(b => b.Kennung == EinstellungenKennung);
        NavigationAbgleichen();
    }

    /// <summary>
    /// Bringt <see cref="Navigation"/> auf den Stand der Bereiche und des Unterpunkts, Schritt für Schritt statt neu: Fiele der
    /// gewählte Eintrag kurz heraus, setzte die Liste links die Auswahl und damit den Bereich auf nichts.
    /// </summary>
    private void NavigationAbgleichen()
    {
        var soll = new List<Bereich>();
        foreach (var b in Bereiche)
        {
            soll.Add(b);
            if (b.Kennung == EinstellungenKennung && (Funkfeld.SprachaktivierungBenutzt || IstSprachaktivierungsseite || _seiteOeffnet)) soll.Add(_sprachaktivierung);
        }
        for (var i = Navigation.Count - 1; i >= 0; i--)
            if (!soll.Contains(Navigation[i])) Navigation.RemoveAt(i);
        for (var i = 0; i < soll.Count; i++)
        {
            if (i < Navigation.Count && ReferenceEquals(Navigation[i], soll[i])) continue;
            var da = Navigation.IndexOf(soll[i]);
            if (da >= 0) Navigation.Move(da, i);
            else Navigation.Insert(i, soll[i]);
        }
        SprachaktivierungEinstellenCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Verwaltung in der Navigation nur mit geladenem Teil und wenn der Server für dieses Konto einen Admin-Zustand oder die
    /// Vorsehung meldet (ANMELDUNG-SICHERHEIT, Admin-Teile). Ein Funker mit Admin-Setup sieht nichts Neues.
    /// </summary>
    private void VerwaltungZeigen(FunkLage l)
    {
        if (_verwaltung is null) return;
        var soll = l.Konto?.Verwaltung == true;
        var da = Bereiche.Contains(_verwaltung);
        // Vor Abzeichen, das immer der letzte Bereich bleibt (seit 0.11.7).
        if (soll && !da)
        {
            if (_abzeichen is not null && Bereiche.Contains(_abzeichen)) Bereiche.Insert(Bereiche.IndexOf(_abzeichen), _verwaltung);
            else Bereiche.Add(_verwaltung);
        }
        if (!soll && da)
        {
            if (ReferenceEquals(AktiverBereich, _verwaltung)) AktiverBereich = Bereiche[0];
            Bereiche.Remove(_verwaltung);
        }
    }

    /// <summary>
    /// Abzeichen (seit 0.11.7, Sarevoc: "ganz unten, nur nach der Admin-Anmeldung") als letzter Bereich, solange es die Verwaltung gibt, die Admin-Sitzung
    /// offen ist und der Verwaltungsteil die Seite baut. Geht die Sitzung zu, während die Seite vorn ist, geht es auf Start.
    /// </summary>
    private void AbzeichenZeigen(FunkLage l)
    {
        if (_abzeichen is null || _verwaltungsteil is null) return;
        var soll = l.Konto?.Verwaltung == true && _verwaltungsteil.AdminSitzungOffen;
        var da = Bereiche.Contains(_abzeichen);
        if (soll && !da) Bereiche.Add(_abzeichen);
        if (!soll && da)
        {
            if (ReferenceEquals(AktiverBereich, _abzeichen)) AktiverBereich = Bereiche[0];
            Bereiche.Remove(_abzeichen);
        }
    }

    /// <summary>Bildtakt, etwa 30 je Sekunde.</summary>
    public void Takt()
    {
        Audio.Takt();
        Funkfeld.Takt();
        Brett.Takt();
        Meldungen.Takt();
        // Seit der Gegenprüfung 0.11.5: Eine abgelaufene Stummschaltung nimmt das Blutrot vom Mikrofon in Wer ist da.
        WerIstDa.Takt();
        Schwelle.Takt();
        Verbindung.Takt();
        Statuszeile.Takt();
    }

    /// <summary>
    /// Das Hauptfenster kam nach vorn: Steht die Startseite offen, Serverprofil und Profilschlüssel neu prüfen. Fehlte
    /// profil.json beim Start (Stick nicht eingesteckt) oder wurde die Datei inzwischen ersetzt, stünde sonst der alte Befund
    /// da. Nur auf Anlass, nie im Takt, und nur dort, wo man es sieht: Ein Profil auf einem nicht erreichbaren Netzlaufwerk
    /// hielte die Oberfläche sonst bei jedem Wechsel ins Fenster kurz an. Seit 0.10.0 wartet Start dann wieder auf die Sprechtaste, wenn ihr
    /// Schritt offen ist (E8).
    /// </summary>
    public void FensterAktiviert()
    {
        _fensterVorn = true;
        if (AktiverBereich?.Inhalt is StartViewModel s) s.Verbindung.Pruefen();
        ErwartenAbgleichen();
    }

    /// <summary>
    /// Das Hauptfenster ist nicht mehr vorn (ein anderes Programm, minimiert, in den Infobereich gelegt): Der Schritt Sprechtaste auf Start hört
    /// auf, ohne Frist auf die Taste zu warten (seit 0.10.0, E8). Sonst würde beim ersten Start die erste Taste in einem anderen Programm zur
    /// Sprechtaste. "Ändern" und "Belegen" mit Frist warten weiter.
    /// </summary>
    public void FensterDeaktiviert()
    {
        _fensterVorn = false;
        ErwartenAbgleichen();
    }

    /// <summary>
    /// Ob das Hauptfenster vorn ist. Bis es das erste Mal aktiv wird, gilt es als nicht vorn: Ging es beim Start nicht nach vorn (Windows lässt
    /// einem Programm, das im Hintergrund startet, den Fokus nicht), wartet Start nicht schon auf eine Taste, die woanders gedrückt wird.
    /// </summary>
    private bool _fensterVorn;

    /// <summary>
    /// Der Schritt Sprechtaste wartet nur, solange Start vorn und das Fenster aktiv ist (E8); sonst endet das Warten still. "Ändern" und "Belegen"
    /// mit Frist enden nur beim Verlassen von Start, nicht wenn das Fenster nicht mehr vorn ist (Befund der Prüfung, wie unter Einstellungen).
    /// </summary>
    private void ErwartenAbgleichen()
    {
        var startVorn = ReferenceEquals(AktiverBereich?.Inhalt, Start);
        if (_fensterVorn && startVorn) Start.ErwartenStarten();
        else Start.ErwartenBeenden(nurOhneFrist: startVorn);
    }

    /// <summary>Das TOTP-Geheimnis steht nur, solange der Bereich Verwaltung offen ist (Gegenprüfung Stufe 1): beim Verlassen fort.</summary>
    partial void OnAktiverBereichChanged(Bereich oldValue, Bereich newValue)
    {
        if (oldValue?.Inhalt is VerwaltungsBereich v && !ReferenceEquals(oldValue, newValue)) v.Teil.Verlassen();
        // Die Seite Sprachaktivierung war ohne Benutzung offen (Strg+T): Ihr Unterpunkt geht mit ihr, aber erst nachdem die Liste links den Wechsel
        // übernommen hat (OnPropertyChanged); hier hält sie ihn noch gewählt und setzte den Bereich sonst auf nichts.
        if (ReferenceEquals(oldValue, _sprachaktivierung) && !ReferenceEquals(newValue, _sprachaktivierung)) _unterpunktPruefen = true;
    }

    /// <summary>Nach dem Verlassen der Seite Sprachaktivierung prüfen, ob ihr Unterpunkt bleibt (<see cref="OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs)"/>).</summary>
    private bool _unterpunktPruefen;

    /// <summary>
    /// Nach der Meldung eines neuen Bereichs (die Liste links hat ihn dann übernommen): Ging die Seite Sprachaktivierung, die ohne Benutzung offen war,
    /// fällt ihr Unterpunkt weg; im Betrieb über den Takt danach, damit die Liste ihre Auswahl erst zu Ende führt.
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(AktiverBereich) || !_unterpunktPruefen) return;
        _unterpunktPruefen = false;
        if (!Funkfeld.SprachaktivierungBenutzt) _takt.Einreihen(NavigationAbgleichen);
    }

    partial void OnAktiverBereichChanged(Bereich value)
    {
        OnPropertyChanged(nameof(IstBrett));
        OnPropertyChanged(nameof(IstSprachaktivierungsseite));
        OnPropertyChanged(nameof(IstVerwaltungsseite));
        OnPropertyChanged(nameof(MitBildlauf));
        OnPropertyChanged(nameof(SeitenInhalt));
        OnPropertyChanged(nameof(BrettInhalt));
        OnPropertyChanged(nameof(VerwaltungInhalt));
        OnPropertyChanged(nameof(IstAbzeichenseite));
        OnPropertyChanged(nameof(AbzeichenInhalt));
        // Zeigen der Startseite prüft neu, aus demselben Grund wie FensterAktiviert.
        if (value?.Inhalt is StartViewModel s) s.Verbindung.Pruefen();
        // E8: Auf Start wartet der Schritt Sprechtaste von selbst auf die Taste, beim Verlassen endet das Warten still.
        ErwartenAbgleichen();
        // Unter Einstellungen stehen die eigenen Rollen; den Katalog dazu holen, wenn man hinschaut.
        if (value?.Inhalt is EinstellungenViewModel) _ = MeinProfil.LadenAsync();
    }

    private void Aktualisieren()
    {
        var l = _dienst.Lage;
        Meldungen.Aktualisieren();
        Bereit.Aktualisieren();
        RaidZeigen(l);
        VerwaltungZeigen(l);
        AbzeichenZeigen(l);
        IstAktiv = l.IstAktiv;
        StatusText = VerbindungViewModel.Text(l.Status);
        Sprechend = l.Sprecher.Count == 0 ? "" : "spricht: " + string.Join(", ", l.Sprecher.Select(s => s.Name).Distinct());
        SprechendZeichen = l.Sprecher.Where(s => s.KontoId != l.Konto?.Id).Select(s => Funkfeld.Dauersend.ZeichenVon(s.KontoId)).FirstOrDefault(z => z is not null);
    }

    /// <summary>
    /// Das gelbe "!" in der Kopfzeile hinter "spricht: ..." (Klangstufe C2b), wenn einer der Sprechenden ein Dauersender ist: das Zeichen des ersten
    /// mit Tooltip und Auswahl; sonst <c>null</c>.
    /// </summary>
    [ObservableProperty]
    public partial DauersendZeichen? SprechendZeichen { get; private set; }
}
