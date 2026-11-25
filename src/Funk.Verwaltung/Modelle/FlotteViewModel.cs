// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung.Flotte;

namespace Funk.Verwaltung.Modelle;


/// <summary>
/// Bereich "Flotte" der Verwaltung (A40, RAIDLEITUNG-ENTWURF "Flotten-Einstellungsseite"): Einheiten, Rollen und Netze als
/// Entwurf (<see cref="Flottenentwurf"/>) über der NetzListe des Servers. Oben steht "7 Änderungen", darunter die Prüfung im
/// Klartext; "Übernehmen" schickt alles als ein Paket mit frischem Code, fragt im Raid nach (Auftrag läuft, mehr als 5
/// eingeteilt, ein gelöschtes Netz hat Mitglieder) und legt vorher den bisherigen Stand als Datei ab (die letzten 10).
/// "Letzten Stand zurückholen", "Als Datei speichern" und gespeicherte Stände laden laufen in den Entwurf; übernommen wird
/// immer erst mit "Übernehmen". Kommt eine neue NetzListe (ein anderer Admin), behält der Entwurf die eigenen Änderungen und
/// übernimmt die fremden je Objekt.
/// <para>
/// Die Formulare (<see cref="EinheitBearbeitung"/>, <see cref="RolleBearbeitung"/>, <see cref="NetzBearbeitung"/>) schreiben jede
/// Eingabe sofort in den Entwurf; die Listen ziehen an Ort und Stelle nach, die Formulare bleiben (kein Neuaufbau beim Tippen).
/// </para>
/// <para>
/// Seit 0.8.0 (Umbau der Verwaltung, docs/entwurf/VERWALTUNG-UMBAU.md): je Liste Suche und Filter nach Herkunft (Einheiten nach Art), das
/// Herkunftsschild je Rolle und Kanal, Sprünge zwischen Rolle, Kanal, Einheit und Phase, "Rolle verdoppeln", die Entwurfsleiste unten mit
/// "Was ändert sich?" und die Werkzeuge mit je einem Satz Erklärung und einer Rückfrage. Die Befehle von vorher bleiben, wie sie sind.
/// </para>
/// </summary>
public sealed partial class FlotteViewModel : ObservableObject
{
    /// <summary>So viele Sicherungen bleiben liegen; ältere löscht der nächste Sicherungslauf.</summary>
    public const int SicherungenMax = 10;

    private const string SicherungPraefix = "sicherung-";
    private const string EntwurfPraefix = "entwurf-";

    private readonly IVerwaltungsdienst _dienst;
    private readonly Func<DateTimeOffset> _jetzt;
    private readonly Func<string?> _laufend;
    private Flottenentwurf? _entwurf;
    private long _basisVersion = -1;
    private bool _sendet;
    private VerwaltungsPaket? _wartend;

    /// <param name="laufenderAuftrag">Wer gerade im Auftrag leitet (für die Rückfrage), sonst <c>null</c>.</param>
    public FlotteViewModel(IVerwaltungsdienst dienst, Func<DateTimeOffset> jetzt, Func<string?> laufenderAuftrag)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(jetzt);
        ArgumentNullException.ThrowIfNull(laufenderAuftrag);
        _dienst = dienst;
        _jetzt = jetzt;
        _laufend = laufenderAuftrag;
        Annahmen.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HatAnnahmen));
        Staende.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HatStaende));
        Vorlagen =
        [
            Werkzeug("Vorlage Star Citizen", "Legt die Vorlage Star Citizen neben die vorhandene Flotte in den Entwurf: 10 Kanäle mit ihren Ch. (Kommando, Flotte, "
                + "Brücke, Crew, Jäger, Bodentrupp, Logistik, Bergung und Bergbau, Sanität, Allgemein) und 14 Rollen von Standard (Gast) bis Funker (Leitstand).",
                "Die Vorlage Star Citizen in den Entwurf legen? Die vorhandene Flotte bleibt; am Server ändert sich erst mit Übernehmen etwas.", "Ja, in den Entwurf",
                VorlageStarCitizenCommand, "Kanäle mit Ch. kennt der Server erst ab Fassung 0.6.0."),
        ];
        Bausteine =
        [
            Werkzeug("Baustein Trupp", "Fügt Infanterie mit Truppkanal hinzu, mit den Plätzen Truppführer, Sanitäter und Schützen.",
                "Einen Trupp in den Entwurf legen?", "Ja, Trupp dazu", BausteinTruppCommand),
            Werkzeug("Baustein Orga-Einheit", "Fügt eine Orga-Einheit mit eigenem Kanal und vier Plätzen der Rolle Orga hinzu.",
                "Eine Orga-Einheit in den Entwurf legen?", "Ja, Orga-Einheit dazu", BausteinOrgaCommand),
        ];
        Sicherung =
        [
            Werkzeug("Als Datei speichern", "Legt den Entwurf als Datei im Ablageordner ab, etwa um ihn jemandem zu schicken.",
                "Den Entwurf jetzt als Datei ablegen?", "Ja, speichern", SpeichernCommand),
            Werkzeug("Letzten Stand zurückholen", "Holt die neueste Sicherung in den Entwurf; eine Sicherung legt der Client vor jedem Übernehmen an.",
                "Die neueste Sicherung in den Entwurf holen? Dein jetziger Entwurf wird ersetzt; am Server ändert sich erst mit Übernehmen etwas.", "Ja, zurückholen",
                LetztenStandCommand),
            Werkzeug("Gewählten Stand laden", "Holt den Stand, der in der Liste darüber gewählt ist (eine Sicherung oder eine gespeicherte Datei), in den Entwurf.",
                "Den gewählten Stand in den Entwurf holen? Dein jetziger Entwurf wird ersetzt.", "Ja, laden", StandLadenCommand,
                "Wähle erst einen gespeicherten Stand aus der Liste."),
        ];
        Leeren = Werkzeug("Leer (nur Global)", "Leert den Entwurf bis auf Global; übernimmst du danach, sind alle Einheiten, Rollen und Kanäle am Server weg.",
            "Wirklich alles bis auf Global aus dem Entwurf nehmen? Mit Übernehmen löscht das alle Einheiten, Rollen und Kanäle am Server.", "Ja, leeren",
            VorlageLeerCommand, gefaehrlich: true);
    }

    private WerkzeugZeile Werkzeug(string titel, string erklaerung, string frage, string ja, IRelayCommand aktion, string? gesperrt = null, bool gefaehrlich = false) =>
        new(titel, erklaerung, frage, ja, aktion, z =>
        {
            foreach (var w in Vorlagen.Concat(Bausteine).Concat(Sicherung).Append(Leeren))
                if (!ReferenceEquals(w, z)) w.Schliessen();
        }, gesperrt, gefaehrlich);

    /// <summary>Werkzeuge (seit 0.8.0): die Vorlage Star Citizen (seit 19.10.2026 die einzige).</summary>
    public IReadOnlyList<WerkzeugZeile> Vorlagen { get; }

    /// <summary>Werkzeuge: Bausteine Trupp und Orga-Einheit.</summary>
    public IReadOnlyList<WerkzeugZeile> Bausteine { get; }

    /// <summary>Werkzeuge: Als Datei speichern, Letzten Stand zurückholen, einen gespeicherten Stand laden.</summary>
    public IReadOnlyList<WerkzeugZeile> Sicherung { get; }

    /// <summary>Werkzeug "Leer (nur Global)", rot.</summary>
    public WerkzeugZeile Leeren { get; }

    public static IReadOnlyList<Auswahl<Einheitsart>> Arten { get; } =
        [new(Einheitsart.Schiff, "Schiff"), new(Einheitsart.Trupp, "Infanterie"), new(Einheitsart.Orga, "Orga")];

    public static IReadOnlyList<Auswahl<NetzOffen>> Offenwahl { get; } =
        [new(NetzOffen.Nein, "nein"), new(NetzOffen.Hoeren, "nur hören"), new(NetzOffen.HoerenSenden, "hören und sprechen")];

    internal Flottenentwurf? Entwurf => _entwurf;

    internal IVerwaltungsdienst Dienst => _dienst;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstEinheiten), nameof(IstRollen), nameof(IstNetze), nameof(IstPhasen))]
    public partial FlotteReiter Reiter { get; set; }

    /// <summary>Nur wählbar, wenn der Server Phasen kennt.</summary>
    public bool IstPhasen
    {
        get => Reiter == FlotteReiter.Phasen;
        set
        {
            if (value && PhasenSichtbar) Reiter = FlotteReiter.Phasen;
        }
    }

    /// <summary>Der Server kennt Phasen (ab 0.5.0): Nur dann gibt es den Reiter "Phasen", und nur dann gehen Phasen ins Paket.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PhaseNeuCommand))]
    public partial bool PhasenSichtbar { get; private set; }

    /// <summary>Der Server kennt Kanäle mit Ch. (ab 0.6.0): Nur dann lässt sich eine Ch.-Liste eintragen und die Vorlage Star Citizen nehmen.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VorlageStarCitizenCommand))]
    public partial bool KanaeleSichtbar { get; private set; }

    /// <summary>Die laufende Phase nach dem letzten 0x4004, sonst 1.</summary>
    internal byte LaufendePhase { get; private set; } = 1;

    /// <summary>Die Grundstellung zuerst (immer), dann die Phasen 2 bis 5; die ID ist die Nummer.</summary>
    public ObservableCollection<FlottenZeile> Phasen { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PhaseLoeschenCommand))]
    public partial FlottenZeile? GewaehltePhase { get; set; }

    [ObservableProperty]
    public partial PhaseBearbeitung? Phase { get; private set; }

    public bool IstEinheiten
    {
        get => Reiter == FlotteReiter.Einheiten;
        set
        {
            if (value) Reiter = FlotteReiter.Einheiten;
        }
    }

    public bool IstRollen
    {
        get => Reiter == FlotteReiter.Rollen;
        set
        {
            if (value) Reiter = FlotteReiter.Rollen;
        }
    }

    public bool IstNetze
    {
        get => Reiter == FlotteReiter.Netze;
        set
        {
            if (value) Reiter = FlotteReiter.Netze;
        }
    }

    /// <summary>Die NetzListe ist da (verbunden und geladen).</summary>
    public bool HatFlotte => _entwurf is not null;

    /// <summary>Schiffe, dann Infanterie, dann Orga, je nach Reihenfolge.</summary>
    public ObservableCollection<FlottenZeile> Einheiten { get; } = [];

    public ObservableCollection<FlottenZeile> Rollen { get; } = [];

    /// <summary>Global zuerst, dann nach Reihenfolge.</summary>
    public ObservableCollection<FlottenZeile> Netze { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EinheitVerdoppelnCommand), nameof(NetzeAnlegenCommand), nameof(EinheitLoeschenCommand), nameof(EinheitHochCommand),
        nameof(EinheitRunterCommand))]
    public partial FlottenZeile? GewaehlteEinheit { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RolleLoeschenCommand), nameof(RolleHochCommand), nameof(RolleRunterCommand), nameof(RolleVerdoppelnCommand))]
    public partial FlottenZeile? GewaehlteRolle { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NetzLoeschenCommand), nameof(NetzHochCommand), nameof(NetzRunterCommand))]
    public partial FlottenZeile? GewaehltesNetz { get; set; }

    [ObservableProperty]
    public partial EinheitBearbeitung? Einheit { get; private set; }

    [ObservableProperty]
    public partial RolleBearbeitung? Rolle { get; private set; }

    [ObservableProperty]
    public partial NetzBearbeitung? Netz { get; private set; }

    /// <summary>Jede Änderung gegen den Server im Klartext.</summary>
    public ObservableCollection<string> Aenderungen { get; } = [];

    /// <summary>"Keine Änderungen", "1 Änderung", "7 Änderungen".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeisteText))]
    public partial string AenderungenText { get; private set; } = "Keine Änderungen";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UebernehmenCommand), nameof(VerwerfenCommand))]
    [NotifyPropertyChangedFor(nameof(LeisteSichtbar))]
    public partial bool HatAenderungen { get; private set; }

    /// <summary>Was der Server nicht annähme (rot) und Hinweise (Felder, die leer bleiben).</summary>
    public ObservableCollection<Befund> Befunde { get; } = [];

    [ObservableProperty]
    public partial bool HatSperre { get; private set; }

    /// <summary>Annahmen einer Vorlage oder eines Bausteins, gelb markiert, bis übernommen oder verworfen.</summary>
    public ObservableCollection<string> Annahmen { get; } = [];

    public bool HatAnnahmen => Annahmen.Count > 0;

    public bool HatStaende => Staende.Count > 0;

    /// <summary>Sicherungen und gespeicherte Entwürfe im Ablageordner, neueste zuerst.</summary>
    public ObservableCollection<StandDatei> Staende { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StandLadenCommand))]
    public partial StandDatei? GewaehlterStand { get; set; }

    /// <summary>Die Rückfrage vor dem Übernehmen im Raid, sonst <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeisteSichtbar))]
    public partial string? Rueckfrage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeisteSichtbar), nameof(HatMeldung))]
    public partial string? Fehler { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeisteSichtbar), nameof(HatMeldung))]
    public partial string? Hinweis { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UebernehmenCommand), nameof(JaUebernehmenCommand))]
    public partial bool Beschaeftigt { get; private set; }

    // ================================================================ Seit 0.8.0: Suche und Herkunft, Entwurfsleiste

    /// <summary>Erster Eintrag der Herkunftsfilter: zeigt alles.</summary>
    public const string AlleHerkuenfte = "alle Herkünfte";

    /// <summary>Filter der Einheiten nach Art (Einheiten haben keine Herkunft), "alle Arten" zuerst.</summary>
    public static IReadOnlyList<Auswahl<Einheitsart?>> Artfilter { get; } =
        [new(null, "alle Arten"), new(Einheitsart.Schiff, "Schiff"), new(Einheitsart.Trupp, "Infanterie"), new(Einheitsart.Orga, "Orga")];

    /// <summary>Die Kanäle, die Suche und Herkunftsfilter zeigen, in der Folge von <see cref="Netze"/>; der gewählte bleibt immer darin.</summary>
    public ObservableCollection<FlottenZeile> SichtbareKanaele { get; } = [];

    /// <summary>Die Rollen, die Suche und Herkunftsfilter zeigen; die gewählte bleibt immer darin.</summary>
    public ObservableCollection<FlottenZeile> SichtbareRollen { get; } = [];

    /// <summary>Die Einheiten, die Suche und Artfilter zeigen; die gewählte bleibt immer darin.</summary>
    public ObservableCollection<FlottenZeile> SichtbareEinheiten { get; } = [];

    /// <summary>Suche in den Namen der Kanäle, auch Teile, ohne Rücksicht auf Groß- und Kleinschreibung.</summary>
    [ObservableProperty]
    public partial string KanalSuche { get; set; } = "";

    /// <summary>Gewählte Herkunft der Kanäle, <see cref="AlleHerkuenfte"/> zeigt alle.</summary>
    [ObservableProperty]
    public partial string? KanalHerkunft { get; set; } = AlleHerkuenfte;

    /// <summary>"alle Herkünfte" und die Herkunftsschilder der Kanäle.</summary>
    public ObservableCollection<string> KanalHerkuenfte { get; } = [AlleHerkuenfte];

    [ObservableProperty]
    public partial string RollenSuche { get; set; } = "";

    [ObservableProperty]
    public partial string? RollenHerkunft { get; set; } = AlleHerkuenfte;

    public ObservableCollection<string> RollenHerkuenfte { get; } = [AlleHerkuenfte];

    [ObservableProperty]
    public partial string EinheitenSuche { get; set; } = "";

    [ObservableProperty]
    public partial Auswahl<Einheitsart?>? EinheitenArt { get; set; } = Artfilter[0];

    partial void OnKanalSucheChanged(string value) => Filtern();

    partial void OnKanalHerkunftChanged(string? value) => Filtern();

    partial void OnRollenSucheChanged(string value) => Filtern();

    partial void OnRollenHerkunftChanged(string? value) => Filtern();

    partial void OnEinheitenSucheChanged(string value) => Filtern();

    partial void OnEinheitenArtChanged(Auswahl<Einheitsart?>? value) => Filtern();

    /// <summary>"7 Änderungen nicht übernommen", vorn in der Leiste unten.</summary>
    public string LeisteText => AenderungenText + " nicht übernommen";

    /// <summary>Die Leiste unten steht, solange etwas offen ist: Änderungen, eine Rückfrage, ein Fehler oder ein Hinweis.</summary>
    public bool LeisteSichtbar => HatAenderungen || Rueckfrage is not null || Fehler is not null || Hinweis is not null;

    /// <summary>Ein Fehler oder Hinweis der Flotte steht in der Leiste, bis "Schließen" ihn wegnimmt.</summary>
    public bool HatMeldung => Fehler is not null || Hinweis is not null;

    /// <summary>"Was ändert sich?" ist aufgeklappt: die Änderungen in Klartext, die Prüfung und die Annahmen über der Leiste.</summary>
    [ObservableProperty]
    public partial bool EntwurfOffen { get; set; }

    /// <summary>Je Änderung eine Zeile in Klartext mit Herkunft (<see cref="Flottenentwurf.Klartext"/>).</summary>
    public ObservableCollection<string> AenderungenKlartext { get; } = [];

    /// <summary>"2 Fehler: so nähme der Server den Entwurf nicht an", solange die Prüfung sperrt; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? SperrText { get; private set; }

    /// <summary>Was das Übernehmen sperrt; steht immer in der Leiste, auch wenn "Was ändert sich?" zu ist.</summary>
    public ObservableCollection<Befund> SperrBefunde { get; } = [];

    /// <summary>Hinweise der Prüfung, die nicht sperren (Felder, die leer bleiben); unter "Was ändert sich?".</summary>
    public ObservableCollection<Befund> HinweisBefunde { get; } = [];

    /// <summary>Nach jedem Nachziehen des Entwurfs; die Übersicht "Heute" baut daran ihre Hinweise neu.</summary>
    public event Action? Nachgezogen;

    partial void OnHatAenderungenChanged(bool value)
    {
        if (!value) EntwurfOffen = false;
    }

    [RelayCommand]
    private void WasAendertSich() => EntwurfOffen = !EntwurfOffen;

    /// <summary>"Schließen" an Fehler oder Hinweis der Flotte in der Leiste.</summary>
    [RelayCommand]
    private void MeldungSchliessen()
    {
        Fehler = null;
        Hinweis = null;
    }

    // ================================================================ Stand vom Server

    /// <summary>Holt die NetzListe aus dem Dienst; bei einer neuen Version wird der Entwurf umgesetzt, die eigenen Änderungen bleiben.</summary>
    public void Aktualisieren()
    {
        var lage = _dienst.Flotte;
        PhasenlageSetzen(lage);
        if (lage.NetzListe is not { } liste) return;
        if (_entwurf is null)
        {
            _basisVersion = liste.Version;
            Setzen(Flottenentwurf.Aus(liste));
            StaendeLaden();
            return;
        }
        if (liste.Version == _basisVersion) return;
        _basisVersion = liste.Version;
        var neu = _entwurf.Umsetzen(liste);
        Setzen(neu);
        if (!_sendet && HatAenderungen) Hinweis = "Die Flotte wurde inzwischen geändert. Dein Entwurf zählt jetzt gegen den neuen Stand.";
    }

    private void Setzen(Flottenentwurf entwurf)
    {
        if (_entwurf is not null) _entwurf.Geaendert -= Nachziehen;
        _entwurf = entwurf;
        entwurf.PhasenErlaubt = PhasenSichtbar;
        entwurf.KanaeleErlaubt = KanaeleSichtbar;
        entwurf.Geaendert += Nachziehen;
        OnPropertyChanged(nameof(HatFlotte));
        // Neue Formulare: Ein umgesetzter Entwurf ist ein anderes Objekt. Die Phase behält die gewählte Rolle.
        var rolleDerPhase = Phase?.GewaehlteRolle?.RolleId;
        Nachziehen();
        Einheit = GewaehlteEinheit is { } e && entwurf.Schiff(e.Id) is not null ? new EinheitBearbeitung(this, e.Id) : null;
        Rolle = GewaehlteRolle is { } r && entwurf.Rolle(r.Id) is not null ? new RolleBearbeitung(this, r.Id) : null;
        Netz = GewaehltesNetz is { } n && entwurf.Netz(n.Id) is not null ? new NetzBearbeitung(this, n.Id) : null;
        Phase = GewaehltePhase is { } p && PhaseDa(p.Id) ? new PhaseBearbeitung(this, (byte)p.Id, rolleDerPhase) : null;
    }

    /// <summary>Phasen kennt der Server, und welche läuft: Reiter zeigen oder verbergen, der Entwurf schickt Phasen nur dann.</summary>
    private void PhasenlageSetzen(Flottenlage lage)
    {
        var laufend = lage.PhasenBekannt ? lage.LaufendePhase : (byte)1;
        if (PhasenSichtbar == lage.PhasenBekannt && LaufendePhase == laufend && KanaeleSichtbar == lage.KanaeleBekannt) return;
        LaufendePhase = laufend;
        PhasenSichtbar = lage.PhasenBekannt;
        KanaeleSichtbar = lage.KanaeleBekannt;
        if (!PhasenSichtbar && Reiter == FlotteReiter.Phasen) Reiter = FlotteReiter.Einheiten;
        if (_entwurf is not { } e) return;
        e.PhasenErlaubt = PhasenSichtbar;
        e.KanaeleErlaubt = KanaeleSichtbar;
        Nachziehen();
    }

    /// <summary>Die Grundstellung gibt es immer (der Server nennt sie selbst), die übrigen, solange sie im Entwurf stehen.</summary>
    private bool PhaseDa(ushort nr) => PhasenSichtbar && (nr == 1 || (_entwurf?.Phase((byte)nr) is not null && nr <= Grenzen.PhaseNrMax));

    /// <summary>Nach jeder Änderung am Entwurf: Zählung, Prüfung, Listen an Ort und Stelle, Formulare abgleichen.</summary>
    private void Nachziehen()
    {
        if (_entwurf is not { } e) return;
        var aenderungen = e.Aenderungen();
        if (!Aenderungen.SequenceEqual(aenderungen))
        {
            Aenderungen.Clear();
            foreach (var a in aenderungen) Aenderungen.Add(a);
        }
        AenderungenText = aenderungen.Count switch
        {
            0 => "Keine Änderungen",
            1 => "1 Änderung",
            var n => n.ToString(CultureInfo.CurrentCulture) + " Änderungen",
        };
        HatAenderungen = aenderungen.Count > 0;
        var klartext = e.Klartext();
        if (!AenderungenKlartext.SequenceEqual(klartext))
        {
            AenderungenKlartext.Clear();
            foreach (var k in klartext) AenderungenKlartext.Add(k);
        }
        var befunde = e.Pruefen().OrderByDescending(b => b.Sperrt).ToList();
        if (!Befunde.SequenceEqual(befunde))
        {
            Befunde.Clear();
            foreach (var b in befunde) Befunde.Add(b);
        }
        HatSperre = befunde.Any(b => b.Sperrt);
        Gleichziehen(SperrBefunde, befunde.Where(b => b.Sperrt));
        Gleichziehen(HinweisBefunde, befunde.Where(b => !b.Sperrt));
        var sperren = befunde.Count(b => b.Sperrt);
        SperrText = sperren == 0 ? null : Mehrzahl(sperren, "Fehler", "Fehler") + ": so nähme der Server den Entwurf nicht an";

        Abgleichen(Einheiten, e.Schiffe.Select(s => (s.Id, s.Name, Einheitstext(s), (string?)null)));
        Abgleichen(Rollen, e.Rollen.Select(r => (r.Id, r.Name, Rollentext(r), (string?)e.HerkunftVon(r))));
        Abgleichen(Netze, e.Netze.Select(n => (n.Id, n.Name, Netztext(e, n), e.HerkunftVon(n))));
        Abgleichen(Phasen, PhasenSichtbar ? Phasenzeilen(e) : []);
        Herkunftswahl(KanalHerkuenfte, e.Herkuenfte(rollen: false), KanalHerkunft);
        Herkunftswahl(RollenHerkuenfte, e.Herkuenfte(rollen: true), RollenHerkunft);
        Filtern();

        if (Einheit is { } eb && e.Schiff(eb.Id) is null) Einheit = null;
        if (Rolle is { } rb && e.Rolle(rb.Id) is null) Rolle = null;
        if (Netz is { } nb && e.Netz(nb.Id) is null) Netz = null;
        if (Phase is { } pb && !PhaseDa(pb.Nr)) Phase = null;
        Einheit?.Abgleichen();
        Rolle?.Abgleichen();
        Netz?.Abgleichen();
        Phase?.Abgleichen();
        PhaseNeuCommand.NotifyCanExecuteChanged();
        Nachgezogen?.Invoke();
    }

    private static void Gleichziehen(ObservableCollection<Befund> liste, IEnumerable<Befund> soll)
    {
        var neu = soll.ToList();
        if (liste.SequenceEqual(neu)) return;
        liste.Clear();
        foreach (var b in neu) liste.Add(b);
    }

    /// <summary>Die Liste eines Herkunftsfilters an Ort und Stelle: "alle Herkünfte", die Schilder, und die gewählte bleibt darin.</summary>
    private static void Herkunftswahl(ObservableCollection<string> liste, IReadOnlyList<string> herkuenfte, string? gewaehlt)
    {
        var soll = new List<string> { AlleHerkuenfte };
        soll.AddRange(herkuenfte);
        if (gewaehlt is not null && !soll.Contains(gewaehlt)) soll.Add(gewaehlt);
        for (var i = 0; i < soll.Count; i++)
        {
            var j = liste.IndexOf(soll[i]);
            if (j < 0) liste.Insert(i, soll[i]);
            else if (j != i) liste.Move(j, i);
        }
        while (liste.Count > soll.Count) liste.RemoveAt(liste.Count - 1);
    }

    /// <summary>Suche und Filter auf die drei Listen anwenden; die gewählte Zeile bleibt jeweils sichtbar.</summary>
    private void Filtern()
    {
        Sichten(SichtbareKanaele, Netze, z => Passt(z, KanalSuche, KanalHerkunft), GewaehltesNetz);
        Sichten(SichtbareRollen, Rollen, z => Passt(z, RollenSuche, RollenHerkunft), GewaehlteRolle);
        Sichten(SichtbareEinheiten, Einheiten,
            z => Passt(z, EinheitenSuche, null) && (EinheitenArt?.Wert is not { } art || _entwurf?.Schiff(z.Id)?.Art == art), GewaehlteEinheit);
    }

    private static bool Passt(FlottenZeile z, string? suche, string? herkunft) =>
        (string.IsNullOrWhiteSpace(suche) || z.Name.Contains(suche.Trim(), StringComparison.OrdinalIgnoreCase))
        && (herkunft is null or AlleHerkuenfte || string.Equals(z.Herkunft, herkunft, StringComparison.Ordinal));

    /// <summary>Die sichtbare Liste in der Folge der ganzen, an Ort und Stelle (die Auswahl bleibt); die gewählte Zeile bleibt darin.</summary>
    private static void Sichten(ObservableCollection<FlottenZeile> sichtbar, ObservableCollection<FlottenZeile> alle, Func<FlottenZeile, bool> passt, FlottenZeile? gewaehlt)
    {
        var soll = alle.Where(z => ReferenceEquals(z, gewaehlt) || passt(z)).ToList();
        for (var i = 0; i < soll.Count; i++)
        {
            var j = sichtbar.IndexOf(soll[i]);
            if (j < 0) sichtbar.Insert(i, soll[i]);
            else if (j != i) sichtbar.Move(j, i);
        }
        while (sichtbar.Count > soll.Count) sichtbar.RemoveAt(sichtbar.Count - 1);
    }

    /// <summary>"2 Entern" mit "ENTERN, 2 Rollen mit Variante, läuft gerade"; die Grundstellung auch, wenn der Server keine nennt.</summary>
    private IEnumerable<(ushort Id, string Name, string Text, string? Herkunft)> Phasenzeilen(Flottenentwurf e)
    {
        var grund = e.Phase(1) ?? new NetzPhase { Nr = 1, Name = Flottenentwurf.GrundstellungName, Kurz = Flottenentwurf.GrundstellungKurz };
        foreach (var p in e.Phasen.Where(p => p.Nr > 1).Prepend(grund))
        {
            var text = p.Kurz + ", " + (p.Nr == 1 ? "die Felder der Rollen" : e.RollenMitVariante(p.Nr).Count switch
            {
                0 => "noch keine Variante",
                var n => Mehrzahl(n, "Rolle", "Rollen") + " mit Variante",
            });
            // Die Grundstellung läuft fast immer; "läuft gerade" sagt nur bei einer Phase etwas.
            if (p.Nr > 1 && p.Nr == LaufendePhase) text += ", läuft gerade";
            yield return (p.Nr, p.Nr.ToString(CultureInfo.CurrentCulture) + " " + p.Name, text, null);
        }
    }

    /// <summary>Liste an Ort und Stelle: vorhandene Zeilen bleiben (die Auswahl auch), nur Reihenfolge, Texte und Herkunft ändern sich.</summary>
    private static void Abgleichen(ObservableCollection<FlottenZeile> liste, IEnumerable<(ushort Id, string Name, string Text, string? Herkunft)> soll)
    {
        var i = 0;
        foreach (var (id, name, text, herkunft) in soll)
        {
            var j = -1;
            for (var k = i; k < liste.Count; k++)
                if (liste[k].Id == id)
                {
                    j = k;
                    break;
                }
            if (j < 0) liste.Insert(i, new FlottenZeile(id));
            else if (j != i) liste.Move(j, i);
            liste[i].Name = name;
            liste[i].Text = text;
            liste[i].Herkunft = herkunft;
            i++;
        }
        while (liste.Count > i) liste.RemoveAt(liste.Count - 1);
    }

    internal static string Mehrzahl(int n, string eins, string mehr) => n.ToString(CultureInfo.CurrentCulture) + " " + (n == 1 ? eins : mehr);

    private static string Einheitstext(Schiff s) =>
        Flottenentwurf.Artname(s.Art) + (s.Typ is { Length: > 0 } t ? ", " + t : "") + ", " + Mehrzahl(s.Plaetze.Count, "Platz", "Plätze") + ", "
        + Mehrzahl(s.Netze.Count, "Einheitskanal", "Einheitskanäle");

    private static string Rollentext(Rolle r) =>
        (r.BrauchtSchiff ? "auf einer Einheit" : "ohne Einheit" + (r.Kapazitaet is { } k ? ", bis " + k.ToString(CultureInfo.CurrentCulture) : ""))
        + ", " + Mehrzahl(r.Felder.Count, "Feld", "Felder") + (r.SelbstWaehlbar ? "" : ", teilt die Einsatzleitung zu");

    private static string Netztext(Flottenentwurf e, Netz n)
    {
        if (n.Typ == NetzTyp.Global) return "hört jeder, festes Feld" + (n.Kanaele is { Count: > 2 } g ? ", dazu " + Mehrzahl(g.Count - 2, "Global-Kanal", "Global-Kanäle") : "");
        var teile = new List<string> { n.SchiffId is { } s && e.Schiff(s) is { } sc ? "Einheit " + sc.Name : "ganze Flotte" };
        if (n.Kanaele is { } liste) teile.Add(Mehrzahl(liste.Count - 1, "Ch.", "Ch.") + (n.Gruppe is { } gruppe ? ", " + gruppe : ""));
        if (n.Standard) teile.Add("haben alle ohne Platz");
        if (n.NurTaste) teile.Add("nur mit Sprechtaste");
        return string.Join(", ", teile);
    }

    partial void OnGewaehlteEinheitChanged(FlottenZeile? value) =>
        Einheit = value is not null && _entwurf?.Schiff(value.Id) is not null ? new EinheitBearbeitung(this, value.Id) : null;

    partial void OnGewaehlteRolleChanged(FlottenZeile? value) =>
        Rolle = value is not null && _entwurf?.Rolle(value.Id) is not null ? new RolleBearbeitung(this, value.Id) : null;

    partial void OnGewaehltesNetzChanged(FlottenZeile? value) =>
        Netz = value is not null && _entwurf?.Netz(value.Id) is not null ? new NetzBearbeitung(this, value.Id) : null;

    partial void OnGewaehltePhaseChanged(FlottenZeile? value) =>
        Phase = value is not null && _entwurf is not null && PhaseDa(value.Id) ? new PhaseBearbeitung(this, (byte)value.Id, null) : null;

    // ================================================================ Einheiten, Rollen, Netze

    [RelayCommand]
    private void EinheitNeu(string? art)
    {
        if (_entwurf is not { } e) return;
        var a = art switch { "Trupp" => Einheitsart.Trupp, "Orga" => Einheitsart.Orga, _ => Einheitsart.Schiff };
        var s = e.EinheitAnlegen(a);
        Reiter = FlotteReiter.Einheiten;
        GewaehlteEinheit = Einheiten.FirstOrDefault(z => z.Id == s.Id);
    }

    private bool MitEinheit() => GewaehlteEinheit is not null;

    [RelayCommand(CanExecute = nameof(MitEinheit))]
    private void EinheitVerdoppeln()
    {
        if (_entwurf is not { } e || GewaehlteEinheit is not { } z) return;
        var kopie = e.EinheitVerdoppeln(z.Id);
        GewaehlteEinheit = Einheiten.FirstOrDefault(x => x.Id == kopie.Id);
    }

    [RelayCommand(CanExecute = nameof(MitEinheit))]
    private void NetzeAnlegen()
    {
        if (_entwurf is not { } e || GewaehlteEinheit is not { } z) return;
        var neu = e.NetzeFuerEinheitAnlegen(z.Id);
        Hinweis = neu.Count == 0 ? "Die Einheit hat ihre Kanäle schon." : "Angelegt: " + string.Join(", ", neu.Select(n => n.Name)) + ".";
    }

    [RelayCommand(CanExecute = nameof(MitEinheit))]
    private void EinheitLoeschen()
    {
        if (_entwurf is not { } e || GewaehlteEinheit is not { } z) return;
        e.SchiffLoeschen(z.Id);
        GewaehlteEinheit = null;
    }

    [RelayCommand(CanExecute = nameof(MitEinheit))]
    private void EinheitHoch() => _entwurf?.SchiffVerschieben(GewaehlteEinheit!.Id, -1);

    [RelayCommand(CanExecute = nameof(MitEinheit))]
    private void EinheitRunter() => _entwurf?.SchiffVerschieben(GewaehlteEinheit!.Id, 1);

    [RelayCommand]
    private void RolleNeu()
    {
        if (_entwurf is not { } e) return;
        var r = e.RolleAnlegen("Neue Rolle", brauchtEinheit: true);
        Reiter = FlotteReiter.Rollen;
        GewaehlteRolle = Rollen.FirstOrDefault(z => z.Id == r.Id);
    }

    private bool MitRolle() => GewaehlteRolle is not null;

    [RelayCommand(CanExecute = nameof(MitRolle))]
    private void RolleLoeschen()
    {
        if (_entwurf is not { } e || GewaehlteRolle is not { } z) return;
        e.RolleLoeschen(z.Id);
        GewaehlteRolle = null;
    }

    /// <summary>"Rolle verdoppeln" (seit 0.8.0): Kopie mit Feldern, Einstellungen, Herkunft und Varianten, ohne Plätze; danach gewählt.</summary>
    [RelayCommand(CanExecute = nameof(MitRolle))]
    private void RolleVerdoppeln()
    {
        if (_entwurf is not { } e || GewaehlteRolle is not { } z) return;
        var kopie = e.RolleVerdoppeln(z.Id);
        GewaehlteRolle = Rollen.FirstOrDefault(x => x.Id == kopie.Id);
        Filtern();
        Hinweis = "Rolle " + kopie.Name + " angelegt, wie " + z.Name + "."
            + (kopie.BrauchtSchiff ? " Plätze auf Einheiten bekommt sie erst, wenn du sie dort einträgst." : "");
    }

    [RelayCommand(CanExecute = nameof(MitRolle))]
    private void RolleHoch() => _entwurf?.RolleVerschieben(GewaehlteRolle!.Id, -1);

    [RelayCommand(CanExecute = nameof(MitRolle))]
    private void RolleRunter() => _entwurf?.RolleVerschieben(GewaehlteRolle!.Id, 1);

    [RelayCommand]
    private void NetzNeu()
    {
        if (_entwurf is not { } e) return;
        var n = e.NetzAnlegen("Neuer Kanal");
        Reiter = FlotteReiter.Netze;
        GewaehltesNetz = Netze.FirstOrDefault(z => z.Id == n.Id);
    }

    private bool MitNetz() => GewaehltesNetz is { Id: not Grenzen.GlobalNetzId };

    [RelayCommand(CanExecute = nameof(MitNetz))]
    private void NetzLoeschen()
    {
        if (_entwurf is not { } e || GewaehltesNetz is not { } z) return;
        e.NetzLoeschen(z.Id);
        GewaehltesNetz = null;
    }

    [RelayCommand(CanExecute = nameof(MitNetz))]
    private void NetzHoch() => _entwurf?.NetzVerschieben(GewaehltesNetz!.Id, -1);

    [RelayCommand(CanExecute = nameof(MitNetz))]
    private void NetzRunter() => _entwurf?.NetzVerschieben(GewaehltesNetz!.Id, 1);

    // ================================================================ Sprünge (seit 0.8.0)

    /// <summary>Zu einem Kanal: Reiter Kanäle, Suche leer, Herkunft wie angegeben (sonst alle), der Kanal gewählt.</summary>
    public void KanalZeigen(ushort id, string? herkunft = null)
    {
        if (Netze.FirstOrDefault(z => z.Id == id) is not { } zeile) return;
        Reiter = FlotteReiter.Netze;
        KanalSuche = "";
        KanalHerkunft = herkunft ?? AlleHerkuenfte;
        GewaehltesNetz = zeile;
        Filtern();
    }

    /// <summary>Zu einer Rolle; mit <paramref name="suche"/> steht der Name in der Suche (doppelte Namen nebeneinander).</summary>
    public void RolleZeigen(ushort id, string? suche = null)
    {
        if (Rollen.FirstOrDefault(z => z.Id == id) is not { } zeile) return;
        Reiter = FlotteReiter.Rollen;
        RollenSuche = suche ?? "";
        RollenHerkunft = AlleHerkuenfte;
        GewaehlteRolle = zeile;
        Filtern();
    }

    /// <summary>Zu einer Einheit: Reiter Einheiten, ohne Suche, alle Arten.</summary>
    public void EinheitZeigen(ushort id)
    {
        if (Einheiten.FirstOrDefault(z => z.Id == id) is not { } zeile) return;
        Reiter = FlotteReiter.Einheiten;
        EinheitenSuche = "";
        EinheitenArt = Artfilter[0];
        GewaehlteEinheit = zeile;
        Filtern();
    }

    /// <summary>Zu einer Phase, nur wenn der Server Phasen kennt.</summary>
    public void PhaseZeigen(byte nr)
    {
        if (!PhasenSichtbar || Phasen.FirstOrDefault(z => z.Id == nr) is not { } zeile) return;
        Reiter = FlotteReiter.Phasen;
        GewaehltePhase = zeile;
    }

    /// <summary>Aus "Verwendet in": zur Rolle, zur Phase oder zur Einheit.</summary>
    internal void Zeigen(Verwendungsstelle v)
    {
        switch (v.Art)
        {
            case VerwendungsArt.Rolle:
                RolleZeigen(v.Id);
                break;
            case VerwendungsArt.Phase:
                PhaseZeigen((byte)v.Id);
                break;
            default:
                EinheitZeigen(v.Id);
                break;
        }
    }

    // ================================================================ Phasen (ab Server 0.5.0)

    private bool KannPhaseNeu() => PhasenSichtbar && _entwurf?.FreiePhase() is not null;

    /// <summary>Neue Phase mit der nächsten freien Nummer, "Phase 3" und "P3", noch ohne Varianten.</summary>
    [RelayCommand(CanExecute = nameof(KannPhaseNeu))]
    private void PhaseNeu()
    {
        if (_entwurf is not { } e || e.FreiePhase() is not { } nr) return;
        var text = nr.ToString(CultureInfo.InvariantCulture);
        e.PhaseAnlegen("Phase " + text, "P" + text);
        Reiter = FlotteReiter.Phasen;
        GewaehltePhase = Phasen.FirstOrDefault(z => z.Id == nr);
    }

    private bool MitPhase() => GewaehltePhase is { Id: > 1 };

    /// <summary>Löscht die gewählte Phase; die Grundstellung bleibt.</summary>
    [RelayCommand(CanExecute = nameof(MitPhase))]
    private void PhaseLoeschen()
    {
        if (_entwurf is not { } e || GewaehltePhase is not { Id: > 1 } z) return;
        e.PhaseLoeschen((byte)z.Id);
        GewaehltePhase = null;
    }

    // ================================================================ Vorlagen und Bausteine

    /// <summary>
    /// "Vorlage Star Citizen" (Sarevoc, 19.10.2026, Q26): 10 Kanäle mit ihren Ch.-Listen und 14 Rollen in den Entwurf, neben die vorhandene Flotte;
    /// die Annahmen stehen gelb darüber. Nur an einem Server ab 0.6.0 und solange es außer Global keinen Kanal mit Ch. gibt.
    /// </summary>
    [RelayCommand(CanExecute = nameof(KanaeleSichtbar))]
    private void VorlageStarCitizen()
    {
        if (_entwurf is not { } e) return;
        if (e.Netze.Any(n => n.Id != Grenzen.GlobalNetzId && n.Kanaele is not null))
        {
            Fehler = "Es gibt schon Kanäle mit Ch. Die Vorlage legt alles neu an und gehört auf eine Flotte ohne sie; erst diese löschen.";
            return;
        }
        var vorlage = Flottenvorlagen.StarCitizen(e.AlsNetzListe());
        e.VorlageAnwenden(vorlage);
        Annahmen.Clear();
        foreach (var a in vorlage.Annahmen) Annahmen.Add(a.Text);
        Fehler = null;
        Hinweis = "Vorlage " + vorlage.Name + " im Entwurf, neben der bisherigen Flotte. Die gelb markierten Annahmen sind zu prüfen, bevor du übernimmst.";
        EntwurfOffen = true;
    }

    [RelayCommand]
    private void VorlageLeer()
    {
        if (_entwurf is not { } e) return;
        e.Leeren();
        Annahmen.Clear();
        Hinweis = "Leer: nur Global. Übernehmen löscht alle Einheiten, Rollen und Kanäle am Server.";
    }

    [RelayCommand]
    private void BausteinTrupp()
    {
        if (_entwurf is not { } e) return;
        var t = e.BausteinTrupp();
        Annahmen.Add("Baustein " + t.Name + ": Truppführer 1 (mindestens 1), Sanitäter 1 und Schütze 6 sind ein Vorschlag.");
        Reiter = FlotteReiter.Einheiten;
        GewaehlteEinheit = Einheiten.FirstOrDefault(z => z.Id == t.Id);
        EntwurfOffen = true;
    }

    [RelayCommand]
    private void BausteinOrga()
    {
        if (_entwurf is not { } e) return;
        var o = e.BausteinOrga();
        Annahmen.Add("Baustein " + o.Name + ": vier Plätze der Rolle Orga sind ein Vorschlag.");
        Reiter = FlotteReiter.Einheiten;
        GewaehlteEinheit = Einheiten.FirstOrDefault(z => z.Id == o.Id);
        EntwurfOffen = true;
    }

    [RelayCommand(CanExecute = nameof(HatAenderungen))]
    private void Verwerfen()
    {
        if (_entwurf is not { } e) return;
        e.Laden(e.Basis);
        Annahmen.Clear();
        Rueckfrage = null;
        _wartend = null;
        Fehler = null;
        Hinweis = null;
    }

    // ================================================================ Übernehmen

    private bool KannUebernehmen() => HatAenderungen && !Beschaeftigt;

    /// <summary>Prüfen, Paket bauen, im Raid nachfragen, sonst gleich senden.</summary>
    [RelayCommand(CanExecute = nameof(KannUebernehmen))]
    private async Task Uebernehmen()
    {
        if (_entwurf is not { } e) return;
        Fehler = null;
        Hinweis = null;
        Rueckfrage = null;
        _wartend = null;
        if (Befunde.FirstOrDefault(b => b.Sperrt) is { } sperre)
        {
            Fehler = "Erst beheben: " + sperre.Text;
            return;
        }
        var p = e.Paket();
        if (p.Paket is not { } paket)
        {
            Fehler = p.Fehler;
            return;
        }
        var lage = _dienst.Flotte;
        var teile = new List<string>();
        // Die laufende Phase ändern stellt sofort um (6.14); andere Phasen wirken erst beim nächsten Schalten und bilden keine Felder neu.
        var laufend = PhasenSichtbar && LaufendePhase > 1 && e.PhaseGeaendert(LaufendePhase) ? e.Basis.Phasen?.FirstOrDefault(x => x.Nr == LaufendePhase) : null;
        var nurPhasen = paket.Ops.All(o => o.Op is VerwaltungsOperation.PhaseSetzen or VerwaltungsOperation.PhaseLoeschen);
        if (lage.Eingeteilte > 5 && (!nurPhasen || laufend is not null))
            teile.Add("Betrifft " + lage.Eingeteilte.ToString(CultureInfo.CurrentCulture) + " eingeteilte Personen, deren Felder neu gebildet werden.");
        if (laufend is not null)
        {
            var welche = "Phase " + laufend.Nr.ToString(CultureInfo.CurrentCulture) + " " + laufend.Name + " läuft gerade: ";
            teile.Add(welche + (e.Phase(laufend.Nr) is null ? "Löschen schaltet die Flotte auf die Grundstellung zurück." : "Wen sie trifft, stellt der Server beim Übernehmen sofort um."));
        }
        if (_laufend() is { } wer) teile.Add("Es läuft eine Einsatzleitung (" + wer + ").");
        // Versionstor (4.7, 6.16): Mit dem ersten Kanal mit Ch. lässt der Server nur noch Clients ab 0.6.0 zu.
        if (!e.Basis.Netze.Any(n => n.Kanaele is not null) && e.Netze.Any(n => n.Kanaele is not null))
            teile.Add("Danach kommen nur noch Clients ab 0.6.0 hinein; wer noch 0.5 hat, wird getrennt. Die Setups 0.6.0 vorher verteilen.");
        foreach (var n in e.Basis.Netze.Where(n => e.Netz(n.Id) is null).OrderBy(n => n.Reihenfolge))
            if (lage.Mitglieder.GetValueOrDefault(n.Id) is > 0 and var m)
                teile.Add("Kanal " + n.Name + " löschen trifft " + Mehrzahl(m, "Mitglied", "Mitglieder") + ".");
        if (teile.Count > 0)
        {
            _wartend = paket;
            Rueckfrage = string.Join(" ", teile) + " Jetzt übernehmen?";
            JaUebernehmenCommand.NotifyCanExecuteChanged();
            return;
        }
        await SendenAsync(paket).ConfigureAwait(true);
    }

    private bool KannJa() => _wartend is not null && !Beschaeftigt;

    [RelayCommand(CanExecute = nameof(KannJa))]
    private async Task JaUebernehmen()
    {
        if (_wartend is not { } paket) return;
        Rueckfrage = null;
        _wartend = null;
        await SendenAsync(paket).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Nein()
    {
        Rueckfrage = null;
        _wartend = null;
        JaUebernehmenCommand.NotifyCanExecuteChanged();
    }

    private async Task SendenAsync(VerwaltungsPaket paket)
    {
        if (_entwurf is not { } e) return;
        var zahl = Aenderungen.Count;
        Beschaeftigt = true;
        _sendet = true;
        try
        {
            var sicherung = Sichern(e.Basis);
            var f = await _dienst.FlotteUebernehmenAsync(paket, e.Basis.Version).ConfigureAwait(true);
            if (f is not null)
            {
                Fehler = f + " Dein Entwurf bleibt.";
                return;
            }
            Annahmen.Clear();
            Aktualisieren();
            Hinweis = "Übernommen: " + (zahl == 1 ? "1 Änderung" : zahl.ToString(CultureInfo.CurrentCulture) + " Änderungen") + "."
                + (sicherung is { } s ? " Vorher gesichert als " + Path.GetFileName(s) + "." : " Ohne Sicherung: kein Ablageordner.");
        }
        finally
        {
            _sendet = false;
            Beschaeftigt = false;
            StaendeLaden();
        }
    }

    /// <summary>Legt den bisherigen Stand ab und lässt nur die letzten <see cref="SicherungenMax"/> liegen.</summary>
    private string? Sichern(NetzListe stand)
    {
        if (_dienst.Ablageordner is not { } ordner) return null;
        try
        {
            Directory.CreateDirectory(ordner);
            var pfad = Path.Combine(ordner, SicherungPraefix + Zeitstempel() + ".json");
            File.WriteAllBytes(pfad, SteuerJson.Serialisieren(stand));
            foreach (var alt in Directory.GetFiles(ordner, SicherungPraefix + "*.json").Order(StringComparer.Ordinal).SkipLast(SicherungenMax))
                File.Delete(alt);
            return pfad;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fehler = "Sicherung ging nicht: " + ex.Message;
            return null;
        }
    }

    private string Zeitstempel() => _jetzt().ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    // ================================================================ Dateien

    /// <summary>"Als Datei speichern": der Entwurf als JSON im Ablageordner, etwa um ihn jemandem zu schicken.</summary>
    [RelayCommand]
    private void Speichern()
    {
        if (_entwurf is not { } e) return;
        if (_dienst.Ablageordner is not { } ordner)
        {
            Fehler = "Kein Ordner zum Speichern.";
            return;
        }
        try
        {
            Directory.CreateDirectory(ordner);
            var pfad = Path.Combine(ordner, EntwurfPraefix + Zeitstempel() + ".json");
            File.WriteAllBytes(pfad, e.AlsJson());
            Fehler = null;
            Hinweis = "Entwurf gespeichert: " + pfad;
            StaendeLaden();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fehler = "Speichern ging nicht: " + ex.Message;
        }
    }

    private bool MitStand() => GewaehlterStand is not null;

    /// <summary>Einen gespeicherten Stand in den Entwurf laden; übernommen wird erst mit "Übernehmen".</summary>
    [RelayCommand(CanExecute = nameof(MitStand))]
    private void StandLaden()
    {
        if (GewaehlterStand is { } s) Laden(s);
    }

    /// <summary>"Letzten Stand zurückholen": die neueste Sicherung in den Entwurf.</summary>
    [RelayCommand]
    private void LetztenStand()
    {
        if (Staende.FirstOrDefault(s => s.IstSicherung) is { } s) Laden(s);
        else Fehler = "Es gibt noch keine Sicherung.";
    }

    private void Laden(StandDatei datei)
    {
        if (_entwurf is not { } e) return;
        NetzListe? liste;
        try
        {
            liste = Flottenentwurf.JsonLesen(File.ReadAllBytes(datei.Pfad));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fehler = "Lesen ging nicht: " + ex.Message;
            return;
        }
        if (liste is null)
        {
            Fehler = "Die Datei " + Path.GetFileName(datei.Pfad) + " ist keine Flotte.";
            return;
        }
        e.Laden(liste);
        Annahmen.Clear();
        Fehler = null;
        Hinweis = datei.Text + " im Entwurf. Übernehmen schickt ihn an den Server.";
    }

    /// <summary>Die JSON-Dateien im Ablageordner, neueste zuerst.</summary>
    public void StaendeLaden()
    {
        var neu = new List<StandDatei>();
        if (_dienst.Ablageordner is { } ordner && Directory.Exists(ordner))
        {
            try
            {
                foreach (var pfad in Directory.GetFiles(ordner, "*.json"))
                    neu.Add(StandDatei.Aus(pfad));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fehler = "Ablageordner nicht lesbar: " + ex.Message;
            }
        }
        neu = [.. neu.OrderByDescending(s => s.Zeit).ThenBy(s => s.Pfad, StringComparer.Ordinal)];
        var gewaehlt = GewaehlterStand?.Pfad;
        Staende.Clear();
        foreach (var s in neu) Staende.Add(s);
        GewaehlterStand = Staende.FirstOrDefault(s => string.Equals(s.Pfad, gewaehlt, StringComparison.Ordinal));
    }
}

/// <summary>Eine Zeile in einer der drei Listen; bleibt für ihre ID bestehen, nur Name und Text ändern sich.</summary>
public sealed partial class FlottenZeile(ushort id) : ObservableObject
{
    public ushort Id { get; } = id;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Unterzeile, etwa "Schiff, Hammerhead, 4 Plätze, 3 Einheitsnetze".</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>Herkunftsschild (seit 0.8.0), etwa "Star Citizen" oder "alte Flotte"; bei Einheiten, Phasen und Global leer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatHerkunft))]
    public partial string? Herkunft { get; set; }

    public bool HatHerkunft => Herkunft is not null;
}

/// <summary>
/// Ein Werkzeug der Verwaltung (seit 0.8.0, Bereich Werkzeuge): Knopf, ein Satz Erklärung und eine Rückfrage, bevor etwas geschieht. Die
/// Handlung selbst ist der Befehl der Flotte von vorher; es fragt immer nur ein Werkzeug zugleich. <see cref="Gefaehrlich"/> färbt
/// "Leer (nur Global)" rot.
/// </summary>
public sealed partial class WerkzeugZeile : ObservableObject
{
    private readonly IRelayCommand _aktion;
    private readonly Action<WerkzeugZeile> _fragt;

    internal WerkzeugZeile(string titel, string erklaerung, string frage, string ja, IRelayCommand aktion, Action<WerkzeugZeile> fragt, string? gesperrt,
        bool gefaehrlich)
    {
        Titel = titel;
        Erklaerung = erklaerung;
        Frage = frage;
        JaText = ja;
        Gesperrt = gesperrt;
        Gefaehrlich = gefaehrlich;
        _aktion = aktion;
        _fragt = fragt;
        aktion.CanExecuteChanged += (_, _) => FragenCommand.NotifyCanExecuteChanged();
    }

    public string Titel { get; }

    /// <summary>Text des Knopfs: der Titel mit "...", denn erst kommt die Rückfrage.</summary>
    public string KnopfText => Titel + " ...";

    /// <summary>Ein Satz, was das Werkzeug tut.</summary>
    public string Erklaerung { get; }

    /// <summary>Die Rückfrage, bevor es geschieht.</summary>
    public string Frage { get; }

    /// <summary>Text des Knopfs, der die Rückfrage bejaht, etwa "Ja, leeren".</summary>
    public string JaText { get; }

    /// <summary>Warum das Werkzeug gerade nicht geht (Hilfe am gesperrten Knopf), sonst <c>null</c>.</summary>
    public string? Gesperrt { get; }

    public bool Gefaehrlich { get; }

    /// <summary>Die Rückfrage steht offen.</summary>
    [ObservableProperty]
    public partial bool Fragt { get; private set; }

    private bool Kann() => _aktion.CanExecute(null);

    [RelayCommand(CanExecute = nameof(Kann))]
    private void Fragen()
    {
        _fragt(this);
        Fragt = true;
    }

    [RelayCommand]
    private void Abbrechen() => Fragt = false;

    [RelayCommand]
    private void Ja()
    {
        Fragt = false;
        if (_aktion.CanExecute(null)) _aktion.Execute(null);
    }

    internal void Schliessen() => Fragt = false;
}

/// <summary>Das feste Feld rechts unten im Funkfeld-Editor (seit 0.8.0): Global hat jeder, niemand kann es wegnehmen.</summary>
public sealed class GlobalKachel
{
    public string Name { get; } = "Feld 10: Global, fest";

    public string Text { get; } = "hört jeder, niemand kann es wegnehmen";
}

/// <summary>Eine Sicherung oder ein gespeicherter Entwurf im Ablageordner.</summary>
public sealed record StandDatei(string Pfad, string Text, DateTime Zeit, bool IstSicherung)
{
    public static StandDatei Aus(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        var name = Path.GetFileNameWithoutExtension(pfad);
        foreach (var (praefix, wort, sicherung) in new[] { ("sicherung-", "Sicherung", true), ("entwurf-", "Entwurf", false) })
        {
            if (name.StartsWith(praefix, StringComparison.Ordinal)
                && DateTime.TryParseExact(name[praefix.Length..], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var zeit))
                return new StandDatei(pfad, wort + " " + zeit.ToString("dd.MM. HH:mm", CultureInfo.InvariantCulture), zeit, sicherung);
        }
        var geaendert = File.GetLastWriteTime(pfad);
        return new StandDatei(pfad, "Datei " + Path.GetFileName(pfad), geaendert, false);
    }

    public override string ToString() => Text;
}

/// <summary>Formular einer Einheit: Name, Typ, Art, Einheitsnetze und Plätze; jede Eingabe geht sofort in den Entwurf.</summary>
public sealed partial class EinheitBearbeitung : ObservableObject
{
    private readonly FlotteViewModel _f;
    private bool _abgleich;

    internal EinheitBearbeitung(FlotteViewModel f, ushort id)
    {
        _f = f;
        Id = id;
        Abgleichen();
    }

    public ushort Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Frei, etwa "Hammerhead" (bis 32 Zeichen).</summary>
    [ObservableProperty]
    public partial string Typ { get; set; } = "";

    [ObservableProperty]
    public partial Auswahl<Einheitsart>? Art { get; set; }

    /// <summary>Kennung und Netz: "Crew der eigenen Einheit: Crew 4".</summary>
    public ObservableCollection<KennungZeile> Kennungen { get; } = [];

    public ObservableCollection<PlatzZeile> Plaetze { get; } = [];

    /// <summary>Alle Netze außer Global, für die Kennungen.</summary>
    public ObservableCollection<Auswahl<ushort>> Netzwahl { get; } = [];

    /// <summary>Alle Rollen, für die Plätze.</summary>
    public ObservableCollection<Auswahl<ushort>> Rollenwahl { get; } = [];

    /// <summary>Für "Einheitsnetz dazu": Kennung (wie CREW) und Netz.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KennungDazuCommand))]
    public partial string NeueKennung { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KennungDazuCommand))]
    public partial Auswahl<ushort>? NeuesNetz { get; set; }

    /// <summary>Stand aus dem Entwurf übernehmen, ohne die eigenen Eingaben zu stören (nur was abweicht).</summary>
    internal void Abgleichen()
    {
        if (_f.Entwurf?.Schiff(Id) is not { } s) return;
        var e = _f.Entwurf!;
        _abgleich = true;
        try
        {
            if (Name != s.Name) Name = s.Name;
            if (Typ != (s.Typ ?? "")) Typ = s.Typ ?? "";
            var art = FlotteViewModel.Arten.First(a => a.Wert == (s.Art ?? Einheitsart.Schiff));
            if (Art != art) Art = art;
            Wahl(Netzwahl, e.Netze.Where(n => n.Id != Grenzen.GlobalNetzId).Select(n => new Auswahl<ushort>(n.Id, n.Name)));
            Wahl(Rollenwahl, e.Rollen.Select(r => new Auswahl<ushort>(r.Id, r.Name)));
            var kennungen = s.Netze.ToList();
            if (Kennungen.Count != kennungen.Count || Kennungen.Zip(kennungen).Any(p => p.First.Kennung != p.Second.Key))
            {
                Kennungen.Clear();
                foreach (var p in kennungen) Kennungen.Add(new KennungZeile(this, p.Key, Flottenentwurf.Kennungstext(p.Key)));
            }
            for (var i = 0; i < kennungen.Count; i++)
            {
                var soll = Netzwahl.FirstOrDefault(n => n.Wert == kennungen[i].Value);
                if (Kennungen[i].Netz != soll) Kennungen[i].NetzStill(soll);
            }
            if (Plaetze.Count != s.Plaetze.Count)
            {
                Plaetze.Clear();
                foreach (var _ in s.Plaetze) Plaetze.Add(new PlatzZeile(this));
            }
            for (var i = 0; i < s.Plaetze.Count; i++)
                Plaetze[i].Still(Rollenwahl.FirstOrDefault(r => r.Wert == s.Plaetze[i].RolleId), s.Plaetze[i].Kapazitaet, s.Plaetze[i].Mindest ?? 0);
        }
        finally
        {
            _abgleich = false;
        }
    }

    /// <summary>Auswahlliste nur ersetzen, wenn sich etwas geändert hat (sonst verlöre ein offenes Auswahlfeld seine Wahl).</summary>
    internal static void Wahl<T>(ObservableCollection<Auswahl<T>> liste, IEnumerable<Auswahl<T>> soll)
    {
        var neu = soll.ToList();
        if (liste.SequenceEqual(neu)) return;
        liste.Clear();
        foreach (var x in neu) liste.Add(x);
    }

    partial void OnNameChanged(string value) => Schreiben();

    partial void OnTypChanged(string value) => Schreiben();

    partial void OnArtChanged(Auswahl<Einheitsart>? value) => Schreiben();

    /// <summary>Alles aus dem Formular in den Entwurf.</summary>
    internal void Schreiben()
    {
        if (_abgleich || _f.Entwurf?.Schiff(Id) is not { } s) return;
        var netze = new SortedDictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var k in Kennungen)
            if (k.Netz is { } n) netze[k.Kennung] = n.Wert;
        _f.Entwurf!.SchiffSetzen(s with
        {
            Name = Name,
            Typ = Typ.Trim().Length == 0 ? null : Typ.Trim(),
            Art = Art?.Wert ?? Einheitsart.Schiff,
            Netze = netze,
            Plaetze = [.. Plaetze.Where(p => p.Rolle is not null).Select(p => new SchiffsPlatz
            {
                RolleId = p.Rolle!.Wert,
                Kapazitaet = (byte)Math.Clamp(p.Kapazitaet, 0, 255),
                Mindest = p.Mindest > 0 ? (byte)Math.Clamp(p.Mindest, 0, 255) : null,
            })],
        });
    }

    /// <summary>Kennungen setzt der Entwurf selbst, damit das Netz zur Einheit gehört.</summary>
    internal void KennungSchreiben(KennungZeile zeile, ushort? netz)
    {
        if (_abgleich) return;
        _f.Entwurf?.KennungSetzen(Id, zeile.Kennung, netz);
    }

    internal void Entfernen(KennungZeile zeile) => _f.Entwurf?.KennungSetzen(Id, zeile.Kennung, null);

    internal void Entfernen(PlatzZeile zeile)
    {
        Plaetze.Remove(zeile);
        Schreiben();
    }

    private bool KannKennungDazu() => Flottenentwurf.KennungGueltig(NeueKennung.Trim().ToUpperInvariant()) && NeuesNetz is not null;

    [RelayCommand(CanExecute = nameof(KannKennungDazu))]
    private void KennungDazu()
    {
        if (NeuesNetz is not { } n) return;
        _f.Entwurf?.KennungSetzen(Id, NeueKennung.Trim().ToUpperInvariant(), n.Wert);
        NeueKennung = "";
        NeuesNetz = null;
    }

    /// <summary>Ein Platz dazu: die erste Rolle, die hier noch keinen hat, ein Platz.</summary>
    [RelayCommand]
    private void PlatzDazu()
    {
        if (_f.Entwurf?.Schiff(Id) is not { } s) return;
        var frei = Rollenwahl.FirstOrDefault(r => s.Plaetze.All(p => p.RolleId != r.Wert)) ?? Rollenwahl.FirstOrDefault();
        if (frei is null)
        {
            return;
        }
        var zeile = new PlatzZeile(this);
        zeile.Still(frei, 1, 0);
        Plaetze.Add(zeile);
        Schreiben();
    }
}

/// <summary>Eine Kennung einer Einheit mit ihrem Netz.</summary>
public sealed partial class KennungZeile : ObservableObject
{
    private readonly EinheitBearbeitung _einheit;
    private bool _still;

    internal KennungZeile(EinheitBearbeitung einheit, string kennung, string text)
    {
        _einheit = einheit;
        Kennung = kennung;
        Text = text;
    }

    public string Kennung { get; }

    /// <summary>"Crew der eigenen Einheit".</summary>
    public string Text { get; }

    [ObservableProperty]
    public partial Auswahl<ushort>? Netz { get; set; }

    public ObservableCollection<Auswahl<ushort>> Netzwahl => _einheit.Netzwahl;

    internal void NetzStill(Auswahl<ushort>? netz)
    {
        _still = true;
        Netz = netz;
        _still = false;
    }

    partial void OnNetzChanged(Auswahl<ushort>? value)
    {
        if (!_still) _einheit.KennungSchreiben(this, value?.Wert);
    }

    [RelayCommand]
    private void Entfernen() => _einheit.Entfernen(this);
}

/// <summary>Ein Platz einer Einheit: Rolle, Kapazität, Mindestbesetzung (0 = keine).</summary>
public sealed partial class PlatzZeile : ObservableObject
{
    private readonly EinheitBearbeitung _einheit;
    private bool _still;

    internal PlatzZeile(EinheitBearbeitung einheit) => _einheit = einheit;

    [ObservableProperty]
    public partial Auswahl<ushort>? Rolle { get; set; }

    [ObservableProperty]
    public partial int Kapazitaet { get; set; }

    [ObservableProperty]
    public partial int Mindest { get; set; }

    public ObservableCollection<Auswahl<ushort>> Rollenwahl => _einheit.Rollenwahl;

    internal void Still(Auswahl<ushort>? rolle, int kapazitaet, int mindest)
    {
        _still = true;
        if (Rolle != rolle) Rolle = rolle;
        if (Kapazitaet != kapazitaet) Kapazitaet = kapazitaet;
        if (Mindest != mindest) Mindest = mindest;
        _still = false;
    }

    partial void OnRolleChanged(Auswahl<ushort>? value) => Melden();

    partial void OnKapazitaetChanged(int value) => Melden();

    partial void OnMindestChanged(int value) => Melden();

    private void Melden()
    {
        if (!_still) _einheit.Schreiben();
    }

    [RelayCommand]
    private void Entfernen() => _einheit.Entfernen(this);
}

/// <summary>
/// Formular einer Rolle: Name, mit oder ohne Einheit, selbst wählbar, Kapazität ohne Einheit und der Felder-Editor wie das
/// Funkfeld (2x5, Global fest rechts unten). Daneben die Vorschau "So sieht Kapitän auf Schiff 2 aus". Seit 0.8.0 der
/// Funkfeld-Editor (VERWALTUNG-UMBAU.md, Variante C): Kacheln je Feld wie beim Funker, ein Feld anklicken und rechts ändern, "+ Feld"
/// wählt das erste freie; der Kanal einer Kachel führt per Klick zum Kanal.
/// </summary>
public sealed partial class RolleBearbeitung : ObservableObject, IFelderEditor
{
    private readonly FlotteViewModel _f;

    Flottenentwurf? IFelderEditor.Entwurf => _f.Entwurf;
    private bool _abgleich;
    private IReadOnlyList<RollenFeld> _weitere = [];

    internal RolleBearbeitung(FlotteViewModel f, ushort id)
    {
        _f = f;
        Id = id;
        for (byte i = 0; i < Flottenentwurf.EditorFelder; i++) Felder.Add(new FeldBearbeitung(this, i));
        Kacheln = [.. Felder, new GlobalKachel()];
        Abgleichen();
        GewaehltesFeld = Felder[0];
    }

    public ushort Id { get; }

    /// <summary>Herkunftsschild der Rolle (seit 0.8.0).</summary>
    [ObservableProperty]
    public partial string Herkunft { get; private set; } = "";

    /// <summary>Das Funkfeld wie beim Funker: Feld 1 bis 9 und Global fest rechts unten.</summary>
    public IReadOnlyList<object> Kacheln { get; }

    /// <summary>Das angeklickte Feld; rechts daneben stehen Kanal, hört, sendet, zuerst senden und fest.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FeldLeerenCommand))]
    public partial FeldBearbeitung? GewaehltesFeld { get; set; }

    partial void OnGewaehltesFeldChanged(FeldBearbeitung? oldValue, FeldBearbeitung? newValue)
    {
        if (oldValue is not null) oldValue.Gewaehlt = false;
        if (newValue is not null) newValue.Gewaehlt = true;
    }

    [RelayCommand]
    private void FeldWaehlen(FeldBearbeitung? feld)
    {
        if (feld is not null) GewaehltesFeld = feld;
    }

    private bool HatFreiesFeld() => Felder.Any(f => !f.Aktiv);

    /// <summary>"+ Feld": das erste freie Feld anklicken; rechts dann den Kanal aussuchen.</summary>
    [RelayCommand(CanExecute = nameof(HatFreiesFeld))]
    private void FeldDazu() => GewaehltesFeld = Felder.FirstOrDefault(f => !f.Aktiv) ?? GewaehltesFeld;

    private bool FeldBelegt() => GewaehltesFeld is { Aktiv: true };

    /// <summary>Das gewählte Feld leeren; die Rolle hat den Kanal dann nicht mehr.</summary>
    [RelayCommand(CanExecute = nameof(FeldBelegt))]
    private void FeldLeeren()
    {
        if (GewaehltesFeld is { } f) f.Quelle = Leer;
    }

    /// <summary>Zum Kanal eines Felds (Kachel oder rechts); bei einer Kennung der Einheit gibt es keinen festen Kanal.</summary>
    [RelayCommand]
    private void KanalOeffnen(FeldBearbeitung? feld)
    {
        if (feld?.KanalId is { } id) _f.KanalZeigen(id);
    }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Mit Einheit (auf einem Platz einer Einheit) oder ohne (etwa Flottenführung).</summary>
    [ObservableProperty]
    public partial bool BrauchtEinheit { get; set; }

    /// <summary>Jeder darf sie selbst wählen; sonst teilt die Einsatzleitung zu.</summary>
    [ObservableProperty]
    public partial bool SelbstWaehlbar { get; set; }

    /// <summary>Kapazität ohne Einheit; leer heißt ohne Grenze.</summary>
    [ObservableProperty]
    public partial string Kapazitaet { get; set; } = "";

    /// <summary>Feld 1 bis 9; Global ist das feste zehnte.</summary>
    public ObservableCollection<FeldBearbeitung> Felder { get; } = [];

    /// <summary>"leer", die Einheitsnetze ("Crew der eigenen Einheit") und alle Netze außer Global.</summary>
    public ObservableCollection<Auswahl<string>> Quellen { get; } = [];

    public ObservableCollection<Auswahl<ushort>> VorschauEinheiten { get; } = [];

    [ObservableProperty]
    public partial Auswahl<ushort>? VorschauEinheit { get; set; }

    [ObservableProperty]
    public partial string VorschauText { get; private set; } = "";

    internal static Auswahl<string> Leer => Felderform.Leer;

    internal void Abgleichen()
    {
        if (_f.Entwurf?.Rolle(Id) is not { } r) return;
        var e = _f.Entwurf!;
        _abgleich = true;
        try
        {
            if (Name != r.Name) Name = r.Name;
            if (BrauchtEinheit != r.BrauchtSchiff) BrauchtEinheit = r.BrauchtSchiff;
            if (SelbstWaehlbar != r.SelbstWaehlbar) SelbstWaehlbar = r.SelbstWaehlbar;
            var kap = r.Kapazitaet?.ToString(CultureInfo.InvariantCulture) ?? "";
            if (Kapazitaet != kap) Kapazitaet = kap;
            Herkunft = e.HerkunftVon(r);
            Felderform.QuellenAbgleichen(Quellen, e, r.Felder, Herkunft);
            Felderform.Einlesen(Felder, r.Felder, Quellen);
            _weitere = [.. r.Felder.Where(f => f.Feld >= Flottenentwurf.EditorFelder)];
            Felderform.EinheitenAbgleichen(VorschauEinheiten, e, r);
            if (VorschauEinheit is null || !VorschauEinheiten.Contains(VorschauEinheit)) VorschauEinheit = VorschauEinheiten.FirstOrDefault();
        }
        finally
        {
            _abgleich = false;
        }
        VorschauSetzen();
        FeldDazuCommand.NotifyCanExecuteChanged();
        FeldLeerenCommand.NotifyCanExecuteChanged();
    }

    partial void OnNameChanged(string value) => Schreiben();

    partial void OnBrauchtEinheitChanged(bool value) => Schreiben();

    partial void OnSelbstWaehlbarChanged(bool value) => Schreiben();

    partial void OnKapazitaetChanged(string value) => Schreiben();

    partial void OnVorschauEinheitChanged(Auswahl<ushort>? value) => VorschauSetzen();

    void IFelderEditor.Schreiben() => Schreiben();

    internal void Schreiben()
    {
        if (_abgleich || _f.Entwurf?.Rolle(Id) is not { } r) return;
        var felder = Felderform.Auslesen(Felder);
        felder.AddRange(_weitere);
        _f.Entwurf!.RolleSetzen(r with
        {
            Name = Name,
            BrauchtSchiff = BrauchtEinheit,
            SelbstWaehlbar = SelbstWaehlbar,
            Kapazitaet = !BrauchtEinheit && byte.TryParse(Kapazitaet.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var k) ? k : null,
            Felder = felder,
        });
    }

    private void VorschauSetzen()
    {
        if (_f.Entwurf is not { } e || e.Rolle(Id) is not { } r)
        {
            VorschauText = "";
            return;
        }
        VorschauText = Felderform.Vorschau(e, r, r.Felder, VorschauEinheit, "");
    }
}

/// <summary>Was ein Feld im Felder-Editor braucht: die Auswahl der Netze und wohin eine Eingabe geht (eine Rolle oder ihre Variante in einer Phase).</summary>
internal interface IFelderEditor
{
    ObservableCollection<Auswahl<string>> Quellen { get; }

    /// <summary>Der Entwurf, aus dem ein Feld die Ch.-Liste seines Kanals nimmt.</summary>
    Flottenentwurf? Entwurf { get; }

    void Schreiben();
}

/// <summary>Der Felder-Editor 2x5 für Rollen und für Varianten einer Phase: Auswahl, Einlesen, Auslesen und Vorschau.</summary>
internal static class Felderform
{
    internal static Auswahl<string> Leer { get; } = new("", "leer");

    private static readonly string[] Grundkennungen = ["DURCHSAGEN", "CREW", "BRUECKE", "TRUPP", "ORGA"];

    /// <summary>
    /// "leer", die Einheitsnetze und alle Netze außer Global; nur ersetzt, wenn sich etwas geändert hat. Seit 0.8.0 stehen die Kanäle
    /// derselben Herkunft wie die Rolle (<paramref name="herkunft"/>) vorn, die übrigen danach (abweichend wählbar); gibt es einen Namen
    /// mehrfach, steht die Herkunft dahinter ("Kommando (alte Flotte)").
    /// </summary>
    internal static void QuellenAbgleichen(ObservableCollection<Auswahl<string>> quellen, Flottenentwurf e, IEnumerable<RollenFeld> felder, string? herkunft)
    {
        var kennungen = Grundkennungen
            .Concat(e.Schiffe.SelectMany(s => s.Netze.Keys))
            .Concat(felder.Select(f => f.Schiffsnetz).OfType<string>())
            .Distinct(StringComparer.Ordinal);
        // Global nur, wenn es Ch. hat: dann sind Global 1 bis 20 gewöhnliche Kanäle für ein Feld (6.16).
        var netze = e.Netze.Where(n => n.Id != Grenzen.GlobalNetzId || n.Kanaele is not null).ToList();
        var doppelt = netze.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Text(Netz n) => doppelt.Contains(n.Name) && e.HerkunftVon(n) is { } h ? n.Name + " (" + h + ")" : n.Name;
        var geordnet = netze.OrderBy(n => n.Id == Grenzen.GlobalNetzId ? 0 : herkunft is not null && e.HerkunftVon(n) == herkunft ? 1 : 2);
        EinheitBearbeitung.Wahl(quellen, new[] { Leer }
            .Concat(kennungen.Select(k => new Auswahl<string>("K:" + k, Flottenentwurf.Kennungstext(k))))
            .Concat(geordnet.Select(n => new Auswahl<string>("N:" + n.Id.ToString(CultureInfo.InvariantCulture), Text(n)))));
    }

    /// <summary>Die Felder in den Editor, ohne Schreiben auszulösen.</summary>
    internal static void Einlesen(IEnumerable<FeldBearbeitung> editor, IReadOnlyList<RollenFeld> felder, ObservableCollection<Auswahl<string>> quellen)
    {
        foreach (var feld in editor)
        {
            var rf = felder.FirstOrDefault(f => f.Feld == feld.Nummer);
            var schluessel = rf is null ? "" : rf.NetzId is { } id ? "N:" + id.ToString(CultureInfo.InvariantCulture) : "K:" + rf.Schiffsnetz;
            feld.Still(quellen.FirstOrDefault(q => q.Wert == schluessel) ?? Leer, rf?.Hoeren ?? false, rf?.Senden ?? false, rf?.SendenStandard ?? false, rf?.Fest ?? false, rf?.Ch);
        }
    }

    /// <summary>Die belegten Felder des Editors als Rollenfelder.</summary>
    internal static List<RollenFeld> Auslesen(IEnumerable<FeldBearbeitung> editor)
    {
        var felder = new List<RollenFeld>();
        foreach (var f in editor.Where(f => f.Aktiv))
        {
            var wert = f.Quelle.Wert;
            felder.Add(new RollenFeld
            {
                Feld = f.Nummer,
                NetzId = wert.StartsWith("N:", StringComparison.Ordinal) ? ushort.Parse(wert[2..], CultureInfo.InvariantCulture) : null,
                Schiffsnetz = wert.StartsWith("K:", StringComparison.Ordinal) ? wert[2..] : null,
                Hoeren = f.Hoeren,
                Senden = f.Senden,
                SendenStandard = f.Vorschlag,
                Fest = f.Fest,
                Ch = f.HatCh && f.Ch is { Wert: >= 0 } ch ? (byte)ch.Wert : null,
            });
        }
        return felder;
    }

    /// <summary>Die Einheiten für die Vorschau: wo die Rolle Plätze hat, sonst alle (mit Einheit).</summary>
    internal static void EinheitenAbgleichen(ObservableCollection<Auswahl<ushort>> liste, Flottenentwurf e, Rolle r)
    {
        var einheiten = e.Schiffe.Where(s => s.Plaetze.Any(p => p.RolleId == r.Id)).ToList();
        if (einheiten.Count == 0 && r.BrauchtSchiff) einheiten = [.. e.Schiffe];
        EinheitBearbeitung.Wahl(liste, einheiten.Select(s => new Auswahl<ushort>(s.Id, s.Name)));
    }

    /// <summary>"So sieht Kapitän auf Schiff 2 aus: 1 Kommando (Sendevorschlag), ..., dazu Global."</summary>
    internal static string Vorschau(Flottenentwurf e, Rolle r, IReadOnlyList<RollenFeld> felder, Auswahl<ushort>? einheit, string zusatz)
    {
        var schiff = einheit is { } v ? e.Schiff(v.Wert) : null;
        var aufgeloest = e.Aufloesen(r with { Felder = felder }, r.BrauchtSchiff ? schiff : null);
        var teile = aufgeloest.Select(f => (f.Feld + 1).ToString(CultureInfo.InvariantCulture) + " " + f.Netz
            + (f.Vorschlag ? " (Sendevorschlag)" : "") + (!f.Senden ? " (nur hören)" : "") + (f.Fest ? " (fest)" : "") + (f.Leer ? " (fehlt dort)" : "")).ToList();
        var wo = r.BrauchtSchiff && schiff is not null ? " auf " + schiff.Name : "";
        return "So sieht " + r.Name + wo + zusatz + " aus: " + (teile.Count == 0 ? "nur Global." : string.Join(", ", teile) + ", dazu Global.");
    }
}

/// <summary>Ein Feld einer Rolle oder einer Variante: Netz oder Einheitsnetz, hören, senden, Sendevorschlag, fest.</summary>
public sealed partial class FeldBearbeitung : ObservableObject
{
    private readonly IFelderEditor _editor;
    private bool _still;

    internal FeldBearbeitung(IFelderEditor editor, byte nummer)
    {
        _editor = editor;
        Nummer = nummer;
    }

    /// <summary>Feld auf dem Draht (0 bis 8); angezeigt wird es eins höher.</summary>
    public byte Nummer { get; }

    public string Titel => "Feld " + (Nummer + 1).ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Aktiv), nameof(KachelName), nameof(KanalId), nameof(HatKanal), nameof(KachelZustand), nameof(KachelZusatz), nameof(KachelCh))]
    public partial Auswahl<string> Quelle { get; set; } = Felderform.Leer;

    public bool Aktiv => Quelle.Wert.Length > 0;

    public ObservableCollection<Auswahl<string>> Quellen => _editor.Quellen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KachelZustand))]
    public partial bool Hoeren { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KachelZustand))]
    public partial bool Senden { get; set; }

    /// <summary>Sendevorschlag: dahin sendet man nach dem Einsteigen zuerst.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KachelZusatz))]
    public partial bool Vorschlag { get; set; }

    /// <summary>Kann der Funker nicht wegnehmen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KachelZusatz))]
    public partial bool Fest { get; set; }

    // ---- Kachel im Funkfeld-Editor (seit 0.8.0)

    /// <summary>Im Funkfeld-Editor angeklickt.</summary>
    [ObservableProperty]
    public partial bool Gewaehlt { get; set; }

    /// <summary>Die Netz-ID des Kanals, bei einer Kennung der Einheit oder ohne Kanal <c>null</c> (dann gibt es keinen Sprung zum Kanal).</summary>
    public ushort? KanalId =>
        Quelle.Wert.StartsWith("N:", StringComparison.Ordinal) && ushort.TryParse(Quelle.Wert[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>Das Feld nennt einen festen Kanal (keine Kennung der Einheit): Dann führt sein Name zum Kanal.</summary>
    public bool HatKanal => KanalId is not null;

    /// <summary>Der Kanal in der Kachel: sein Name (ohne Herkunft), eine Kennung im Klartext, ohne Kanal "frei".</summary>
    public string KachelName => !Aktiv ? "frei" : KanalId is { } id && _editor.Entwurf?.Netz(id) is { } n ? n.Name : Quelle.Text;

    /// <summary>"Ch. wie zuletzt" oder "Ch. 3 Schiff 3"; leer, wenn der Kanal keine Ch. hat.</summary>
    public string KachelCh => !HatCh ? "" : Ch is { Wert: >= 0 } c ? "Ch. " + c.Text : "Ch. wie zuletzt";

    /// <summary>"hört und sendet", "nur hören" oder "nur senden"; leer ohne Kanal.</summary>
    public string KachelZustand => !Aktiv ? "" : Hoeren && Senden ? "hört und sendet" : Hoeren ? "nur hören" : Senden ? "nur senden" : "hört und sendet nicht";

    /// <summary>"zuerst senden, fest", soweit es gilt; leer ohne Kanal.</summary>
    public string KachelZusatz => !Aktiv ? "" : string.Join(", ", new[] { Vorschlag ? "zuerst senden" : null, Fest ? "fest" : null }.OfType<string>());

    /// <summary>"Ch. wie zuletzt" und die Ch. des gewählten Kanals (6.16); leer, wenn der Kanal keine Ch. hat.</summary>
    public ObservableCollection<Auswahl<int>> ChWahl { get; } = [];

    /// <summary>Der Kanal des Felds hat Ch.: Dann gibt es die Wahl "wie zuletzt" oder einen festen Ch.</summary>
    public bool HatCh => ChWahl.Count > 0;

    /// <summary>Fester Ch. des Felds, -1 = wie zuletzt (der Ch., den der Funker in diesem Kanal hat, ab Werk Ruhe).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KachelCh))]
    public partial Auswahl<int>? Ch { get; set; }

    internal void Still(Auswahl<string> quelle, bool hoeren, bool senden, bool vorschlag, bool fest, byte? ch = null)
    {
        _still = true;
        if (Quelle != quelle) Quelle = quelle;
        if (Hoeren != hoeren) Hoeren = hoeren;
        if (Senden != senden) Senden = senden;
        if (Vorschlag != vorschlag) Vorschlag = vorschlag;
        if (Fest != fest) Fest = fest;
        ChAbgleichen(ch);
        _still = false;
    }

    /// <summary>Die Ch.-Wahl zum Kanal des Felds: "wie zuletzt", dann "0 Ruhe", "1 Schiff 1" usw.; Ch. 1 von Global steht nie darin.</summary>
    private void ChAbgleichen(byte? ch)
    {
        IReadOnlyList<string>? liste = null;
        ushort id = 0;
        if (Quelle.Wert.StartsWith("N:", StringComparison.Ordinal) && ushort.TryParse(Quelle.Wert[2..], NumberStyles.None, CultureInfo.InvariantCulture, out id))
            liste = _editor.Entwurf?.Netz(id)?.Kanaele;
        var soll = liste is null ? [] : new List<Auswahl<int>> { WieZuletzt }
            .Concat(liste.Select((name, i) => new Auswahl<int>(i, i.ToString(CultureInfo.InvariantCulture) + " " + name))
                .Where(a => !(id == Grenzen.GlobalNetzId && a.Wert == Kanalrechnung.GlobalSelbst)))
            .ToList();
        if (!ChWahl.SequenceEqual(soll))
        {
            ChWahl.Clear();
            foreach (var a in soll) ChWahl.Add(a);
            OnPropertyChanged(nameof(HatCh));
            OnPropertyChanged(nameof(KachelCh));
        }
        var gewaehlt = ch is { } c ? ChWahl.FirstOrDefault(a => a.Wert == c) : ChWahl.FirstOrDefault();
        if (Ch != gewaehlt) Ch = gewaehlt;
    }

    private static readonly Auswahl<int> WieZuletzt = new(-1, "Ch. wie zuletzt (ab Werk Ruhe)");

    partial void OnQuelleChanged(Auswahl<string> oldValue, Auswahl<string> newValue)
    {
        if (_still) return;
        _still = true;
        ChAbgleichen(null);
        _still = false;
        // Ein frisch belegtes Feld hört und sendet (A7); wer nur hören will, nimmt das Häkchen weg.
        if (oldValue.Wert.Length == 0 && newValue.Wert.Length > 0 && !Hoeren && !Senden)
        {
            _still = true;
            Hoeren = true;
            Senden = true;
            _still = false;
        }
        _editor.Schreiben();
    }

    partial void OnHoerenChanged(bool value) => Melden();

    partial void OnSendenChanged(bool value) => Melden();

    partial void OnVorschlagChanged(bool value) => Melden();

    partial void OnFestChanged(bool value) => Melden();

    partial void OnChChanged(Auswahl<int>? value) => Melden();

    private void Melden()
    {
        if (!_still) _editor.Schreiben();
    }
}

/// <summary>
/// Formular einer Phase (6.14): Name und Kurzname, je Rolle "wie Grundstellung" oder eine eigene Variante im Felder-Editor, darunter
/// wen sie trifft. Die Grundstellung trägt nur Name und Kurzname; ihre Felder sind die der Rollen.
/// </summary>
public sealed partial class PhaseBearbeitung : ObservableObject
{
    private readonly FlotteViewModel _f;
    private bool _abgleich;

    internal PhaseBearbeitung(FlotteViewModel f, byte nr, ushort? rolle)
    {
        _f = f;
        Nr = nr;
        Abgleichen();
        if (rolle is { } r) GewaehlteRolle = Rollen.FirstOrDefault(z => z.RolleId == r);
    }

    public byte Nr { get; }

    public bool IstGrundstellung => Nr == 1;

    public bool HatVarianten => !IstGrundstellung;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Kurzname für Leiste und Meldezeile, etwa ENTERN (bis 12 Zeichen).</summary>
    [ObservableProperty]
    public partial string Kurz { get; set; } = "";

    /// <summary>Läuft diese Phase gerade, stellt der Server beim Übernehmen sofort um; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? LaeuftText { get; private set; }

    /// <summary>"Wen sie trifft: Kapitän (alle Schiffe)".</summary>
    [ObservableProperty]
    public partial string BetroffeneText { get; private set; } = "";

    /// <summary>Jede Rolle mit "wie Grundstellung" oder "eigene Variante, 8 Felder"; bei der Grundstellung leer.</summary>
    public ObservableCollection<VarianteZeile> Rollen { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VarianteAnlegenCommand), nameof(WieGrundstellungCommand))]
    public partial VarianteZeile? GewaehlteRolle { get; set; }

    /// <summary>Der Felder-Editor der gewählten Rolle, wenn sie eine Variante hat.</summary>
    [ObservableProperty]
    public partial VarianteBearbeitung? Variante { get; private set; }

    private NetzPhase? Stand(Flottenentwurf e) =>
        e.Phase(Nr) ?? (Nr == 1 ? new NetzPhase { Nr = 1, Name = Flottenentwurf.GrundstellungName, Kurz = Flottenentwurf.GrundstellungKurz } : null);

    internal void Abgleichen()
    {
        if (_f.Entwurf is not { } e || Stand(e) is not { } p) return;
        _abgleich = true;
        try
        {
            if (Name != p.Name) Name = p.Name;
            if (Kurz != p.Kurz) Kurz = p.Kurz;
        }
        finally
        {
            _abgleich = false;
        }
        LaeuftText = Nr > 1 && Nr == _f.LaufendePhase ? "Diese Phase läuft gerade. Was du hier änderst, stellt der Server beim Übernehmen sofort um." : null;
        if (IstGrundstellung)
        {
            BetroffeneText = "";
            return;
        }
        var mit = e.RollenMitVariante(Nr).ToHashSet();
        var i = 0;
        foreach (var r in e.Rollen)
        {
            var j = -1;
            for (var k = i; k < Rollen.Count; k++)
                if (Rollen[k].RolleId == r.Id)
                {
                    j = k;
                    break;
                }
            if (j < 0) Rollen.Insert(i, new VarianteZeile(r.Id));
            else if (j != i) Rollen.Move(j, i);
            var zeile = Rollen[i];
            zeile.Name = r.Name;
            zeile.HatVariante = mit.Contains(r.Id);
            zeile.Text = zeile.HatVariante ? "eigene Variante, " + FlotteViewModel.Mehrzahl(e.Variante(Nr, r.Id).Count, "Feld", "Felder") : "wie Grundstellung";
            i++;
        }
        while (Rollen.Count > i) Rollen.RemoveAt(Rollen.Count - 1);
        if (GewaehlteRolle is { } g && !Rollen.Contains(g)) GewaehlteRolle = null;
        BetroffeneText = "Wen sie trifft: " + e.Betroffene(Nr);
        if (Variante is { } v && (GewaehlteRolle is not { HatVariante: true } gv || gv.RolleId != v.RolleId)) Variante = null;
        else if (Variante is null && GewaehlteRolle is { HatVariante: true } neu) Variante = new VarianteBearbeitung(_f, Nr, neu.RolleId);
        Variante?.Abgleichen();
        VarianteAnlegenCommand.NotifyCanExecuteChanged();
        WieGrundstellungCommand.NotifyCanExecuteChanged();
    }

    partial void OnNameChanged(string value) => Schreiben();

    partial void OnKurzChanged(string value) => Schreiben();

    partial void OnGewaehlteRolleChanged(VarianteZeile? value) =>
        Variante = value is { HatVariante: true } ? new VarianteBearbeitung(_f, Nr, value.RolleId) : null;

    private void Schreiben()
    {
        if (_abgleich || _f.Entwurf is not { } e || Stand(e) is not { } p) return;
        e.PhaseSetzen(p with { Name = Name, Kurz = Kurz });
    }

    private bool KannAnlegen() => !IstGrundstellung && GewaehlteRolle is { HatVariante: false } g && _f.Entwurf?.Rolle(g.RolleId) is { Felder.Count: > 0 };

    /// <summary>"Variante anlegen": die Felder der Grundstellung der Rolle als Ausgangspunkt.</summary>
    [RelayCommand(CanExecute = nameof(KannAnlegen))]
    private void VarianteAnlegen()
    {
        if (GewaehlteRolle is { } g) _f.Entwurf?.VarianteAnlegen(Nr, g.RolleId);
    }

    private bool KannZurueck() => GewaehlteRolle is { HatVariante: true };

    /// <summary>"wie Grundstellung": Die Variante fällt weg, die Rolle behält in dieser Phase ihre Felder.</summary>
    [RelayCommand(CanExecute = nameof(KannZurueck))]
    private void WieGrundstellung()
    {
        if (GewaehlteRolle is { } g) _f.Entwurf?.VarianteEntfernen(Nr, g.RolleId);
    }
}

/// <summary>Eine Rolle in der Liste einer Phase; bleibt für ihre ID bestehen.</summary>
public sealed partial class VarianteZeile(ushort rolleId) : ObservableObject
{
    public ushort RolleId { get; } = rolleId;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>"wie Grundstellung" oder "eigene Variante, 8 Felder".</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial bool HatVariante { get; set; }
}

/// <summary>
/// Die Variante einer Rolle in einer Phase im Felder-Editor 2x5, mit Vorschau ("So sieht Kapitän auf Schiff 2 in Phase 2
/// Entern aus"). Ohne belegtes Feld fällt die Variante weg: Der Draht kennt keine leere Variante, es gilt die Grundstellung.
/// </summary>
public sealed partial class VarianteBearbeitung : ObservableObject, IFelderEditor
{
    private readonly FlotteViewModel _f;

    Flottenentwurf? IFelderEditor.Entwurf => _f.Entwurf;
    private bool _abgleich;
    private IReadOnlyList<RollenFeld> _weitere = [];

    internal VarianteBearbeitung(FlotteViewModel f, byte nr, ushort rolleId)
    {
        _f = f;
        Nr = nr;
        RolleId = rolleId;
        for (byte i = 0; i < Flottenentwurf.EditorFelder; i++) Felder.Add(new FeldBearbeitung(this, i));
        Abgleichen();
    }

    public byte Nr { get; }

    public ushort RolleId { get; }

    /// <summary>"Kapitän in Phase 2 Entern".</summary>
    [ObservableProperty]
    public partial string Titel { get; private set; } = "";

    public ObservableCollection<FeldBearbeitung> Felder { get; } = [];

    public ObservableCollection<Auswahl<string>> Quellen { get; } = [];

    public ObservableCollection<Auswahl<ushort>> VorschauEinheiten { get; } = [];

    [ObservableProperty]
    public partial Auswahl<ushort>? VorschauEinheit { get; set; }

    [ObservableProperty]
    public partial string VorschauText { get; private set; } = "";

    internal void Abgleichen()
    {
        if (_f.Entwurf is not { } e || e.Rolle(RolleId) is not { } r || e.Phase(Nr) is not { } p) return;
        var felder = e.Variante(Nr, RolleId);
        _abgleich = true;
        try
        {
            Titel = r.Name + " in Phase " + Nr.ToString(CultureInfo.CurrentCulture) + " " + p.Name;
            Felderform.QuellenAbgleichen(Quellen, e, felder, e.HerkunftVon(r));
            Felderform.Einlesen(Felder, felder, Quellen);
            _weitere = [.. felder.Where(f => f.Feld >= Flottenentwurf.EditorFelder)];
            Felderform.EinheitenAbgleichen(VorschauEinheiten, e, r);
            if (VorschauEinheit is null || !VorschauEinheiten.Contains(VorschauEinheit)) VorschauEinheit = VorschauEinheiten.FirstOrDefault();
        }
        finally
        {
            _abgleich = false;
        }
        VorschauSetzen();
    }

    partial void OnVorschauEinheitChanged(Auswahl<ushort>? value) => VorschauSetzen();

    void IFelderEditor.Schreiben()
    {
        if (_abgleich || _f.Entwurf is not { } e) return;
        var felder = Felderform.Auslesen(Felder);
        felder.AddRange(_weitere);
        e.VarianteSetzen(Nr, RolleId, felder);
    }

    private void VorschauSetzen()
    {
        if (_f.Entwurf is not { } e || e.Rolle(RolleId) is not { } r || e.Phase(Nr) is not { } p)
        {
            VorschauText = "";
            return;
        }
        VorschauText = Felderform.Vorschau(e, r, e.Variante(Nr, RolleId), VorschauEinheit, " in Phase " + Nr.ToString(CultureInfo.CurrentCulture) + " " + p.Name);
    }
}

/// <summary>
/// Formular eines Kanals im Klartext: nur mit Sprechtaste, Voreinstellung VA oder Taste, offen, Standard, seit 0.6.0 Gruppe und Ch.-Liste
/// (6.16). An Global lassen sich nur Ch.-Liste, Gruppe und "offen" ändern: Ch. 1 bleibt das feste Feld, die übrigen Ch. sind Kanäle.
/// </summary>
public sealed partial class NetzBearbeitung : ObservableObject
{
    private readonly FlotteViewModel _f;
    private bool _abgleich;

    internal NetzBearbeitung(FlotteViewModel f, ushort id)
    {
        _f = f;
        Id = id;
        Abgleichen();
    }

    public ushort Id { get; }

    /// <summary>Global ist fest; das Formular zeigt es nur.</summary>
    public bool IstGlobal => Id == Grenzen.GlobalNetzId;

    public bool Aenderbar => !IstGlobal;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>"nur mit Sprechtaste": Sprachaktivierung geht in diesem Netz nicht.</summary>
    [ObservableProperty]
    public partial bool NurTaste { get; set; }

    /// <summary>Voreinstellung Sprechtaste statt Sprachaktivierung (A20); jeder kann es für sich umstellen.</summary>
    [ObservableProperty]
    public partial bool Taste { get; set; }

    /// <summary>"Jeder darf es selbst dazunehmen: nein / nur hören / hören und sprechen".</summary>
    [ObservableProperty]
    public partial Auswahl<NetzOffen>? Offen { get; set; }

    /// <summary>"haben alle, die keinen Platz haben".</summary>
    [ObservableProperty]
    public partial bool Standard { get; set; }

    /// <summary>Gruppe der Kanalwahl im Funkfeld ("Schiffe"); leer ohne.</summary>
    [ObservableProperty]
    public partial string Gruppe { get; set; } = "";

    /// <summary>
    /// Die Ch. nach der Ruhe, eine Bezeichnung je Zeile ("Schiff 1"); leer heißt: ein Kanal ohne Ch. Ch. 0 ist immer die Ruhe und ab Werk
    /// eingestellt (Q6); ihr Name steht nicht hier.
    /// </summary>
    [ObservableProperty]
    public partial string ChListe { get; set; } = "";

    /// <summary>Ch. lassen sich nur an einem Server ab 0.6.0 eintragen.</summary>
    public bool ChAenderbar => _f.KanaeleSichtbar;

    /// <summary>Wer etwas am Kanal selbst ändern darf: bei Global nur, wenn es Ch. hat (für Global 1 bis 20).</summary>
    public bool OffenAenderbar => !IstGlobal || _f.Entwurf?.Netz(Id)?.Kanaele is not null;

    /// <summary>Was zur Ch.-Liste zu sagen ist: ab Werk Ruhe, Grenzen, oder dass der Server keine Ch. kennt.</summary>
    public string ChHinweis => ChAenderbar
        ? "Ch. 0 ist immer Ruhe und ab Werk eingestellt. Darunter eine Bezeichnung je Zeile, höchstens 30 zu je 20 Zeichen. Leer: ein Kanal ohne Ch."
        : "Ch. kennt der Server erst ab Fassung 0.6.0.";

    /// <summary>"Gehört zu Schiff 1 als Crew der eigenen Einheit." oder "Für die ganze Flotte, keine Einheit."</summary>
    [ObservableProperty]
    public partial string Zugehoerigkeit { get; private set; } = "";

    /// <summary>"4 Mitglieder" (aus den Details, die ein Admin sieht).</summary>
    [ObservableProperty]
    public partial string MitgliederText { get; private set; } = "";

    /// <summary>Herkunftsschild (seit 0.8.0), bei Global <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Herkunft { get; private set; }

    /// <summary>"Verwendet in" (seit 0.8.0): Rollen, Varianten in Phasen und Einheiten, die den Kanal nennen; ein Klick springt hin.</summary>
    public ObservableCollection<Verwendungsstelle> VerwendetIn { get; } = [];

    public bool HatVerwendungen => VerwendetIn.Count > 0;

    [RelayCommand]
    private void VerwendungZeigen(Verwendungsstelle? v)
    {
        if (v is not null) _f.Zeigen(v);
    }

    internal void Abgleichen()
    {
        if (_f.Entwurf?.Netz(Id) is not { } n) return;
        var e = _f.Entwurf!;
        _abgleich = true;
        try
        {
            if (Name != n.Name) Name = n.Name;
            if (NurTaste != n.NurTaste) NurTaste = n.NurTaste;
            var taste = n.AusloeserStandard == Ausloeser.Taste;
            if (Taste != taste) Taste = taste;
            var offen = FlotteViewModel.Offenwahl.First(o => o.Wert == n.Offen);
            if (Offen != offen) Offen = offen;
            if (Standard != n.Standard) Standard = n.Standard;
            if (Gruppe != (n.Gruppe ?? "")) Gruppe = n.Gruppe ?? "";
            // Nur ersetzen, wenn der Inhalt abweicht: Beim Tippen stünde sonst eine neue leere Zeile sofort wieder weg.
            if (!Zeilen(ChListe).SequenceEqual(n.Kanaele?.Skip(1) ?? [])) ChListe = string.Join(Environment.NewLine, n.Kanaele?.Skip(1) ?? []);
        }
        finally
        {
            _abgleich = false;
        }
        OnPropertyChanged(nameof(OffenAenderbar));
        OnPropertyChanged(nameof(ChAenderbar));
        OnPropertyChanged(nameof(ChHinweis));
        var einheit = n.SchiffId is { } sid ? e.Schiff(sid) : e.Schiffe.FirstOrDefault(s => s.Netze.Values.Contains(Id));
        var kennung = n.Schiffsnetz ?? einheit?.Netze.FirstOrDefault(p => p.Value == Id).Key;
        Zugehoerigkeit = IstGlobal ? "Global hört jeder, im festen Feld rechts unten."
            : einheit is not null ? "Gehört zu " + einheit.Name + (kennung is { } k ? " als " + Flottenentwurf.Kennungstext(k) : "") + "."
            : "Für die ganze Flotte, keine Einheit.";
        var m = _f.Dienst.Flotte.Mitglieder.GetValueOrDefault(Id);
        MitgliederText = m == 0 ? "keine Mitglieder" : FlotteViewModel.Mehrzahl(m, "Mitglied", "Mitglieder");
        Herkunft = e.HerkunftVon(n);
        var verwendet = e.VerwendetIn(Id);
        if (!VerwendetIn.SequenceEqual(verwendet))
        {
            VerwendetIn.Clear();
            foreach (var v in verwendet) VerwendetIn.Add(v);
            OnPropertyChanged(nameof(HatVerwendungen));
        }
    }

    partial void OnNameChanged(string value) => Schreiben();

    partial void OnNurTasteChanged(bool value) => Schreiben();

    partial void OnTasteChanged(bool value) => Schreiben();

    partial void OnOffenChanged(Auswahl<NetzOffen>? value) => Schreiben();

    partial void OnStandardChanged(bool value) => Schreiben();

    partial void OnGruppeChanged(string value) => Schreiben();

    partial void OnChListeChanged(string value) => Schreiben();

    /// <summary>Die Zeilen der Ch.-Liste ohne leere und ohne Leerzeichen am Rand.</summary>
    internal static List<string> Zeilen(string text) =>
        [.. text.Split('\n').Select(z => z.Trim()).Where(z => z.Length > 0)];

    private void Schreiben()
    {
        if (_abgleich || _f.Entwurf?.Netz(Id) is not { } n) return;
        var zeilen = Zeilen(ChListe);
        IReadOnlyList<string>? kanaele = zeilen.Count == 0 ? null : [n.Kanaele is [var ruhe, ..] ? ruhe : "Ruhe", .. zeilen];
        var gruppe = string.IsNullOrWhiteSpace(Gruppe) ? null : Gruppe.Trim();
        // Global behält Name, Auslöser und "nur Taste" (Ch. 1 ist immer nur Taste, A64); Offen gilt für die übrigen Ch.
        _f.Entwurf!.NetzSetzen(IstGlobal
            ? n with { Kanaele = kanaele, Gruppe = gruppe, Offen = kanaele is null ? n.Offen : Offen?.Wert ?? NetzOffen.Nein }
            : n with
            {
                Name = Name,
                NurTaste = NurTaste,
                AusloeserStandard = Taste ? Ausloeser.Taste : Ausloeser.Va,
                Offen = Offen?.Wert ?? NetzOffen.Nein,
                Standard = Standard,
                Kanaele = kanaele,
                Gruppe = gruppe,
            });
    }
}

/// <summary>Die Reiter der Flottenseite; "Phasen" nur an einem Server ab 0.5.0.</summary>
public enum FlotteReiter
{
    Einheiten,
    Rollen,
    Netze,
    Phasen,
}
