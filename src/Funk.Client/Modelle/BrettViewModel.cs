// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Das Brett der Einsatzleitung (Bereich Raid, Strg+5; RAIDLEITUNG-ENTWURF "Das Brett", Bauplan M5). Sichtbar nur mit ZUWEISEN
/// (Admin, Einzelrecht oder Auftrag für den Abend) und ohne Neustart ein- und ausgeblendet. Links die Teilnehmer ohne Platz
/// (Nachzügler zuerst) und "Alle Mitglieder" aus dem Kontenverzeichnis, in der Mitte je Einheit eine Spalte mit Rollenblöcken,
/// rechts Global, die Sondernetze als Ziel für Zusatznetze und das Detailfeld, unten der Verlauf.
/// <para>
/// Jede Ablage wirkt sofort (LIVE): genau ein <c>Verschieben</c>, beim Tausch mit zwei Änderungen, mit den festen Zusatznetzen als
/// <c>felderPatch</c> und mit <c>basisVersion</c>, damit niemand eine Umstellung überschreibt, die er nicht gesehen hat. Bis zur
/// Antwort steht die Karte am Ziel und "wartet"; scheitert es, springt sie zurück und der Grund steht unten.
/// </para>
/// <para>
/// Die Lage kommt mit jedem Push neu, auch mit jeder Sprechanzeige (CODEMAP, Fallen). Das Brett baut deshalb nichts neu: eine
/// Karte je Konto für die ganze Lebensdauer, Spalten und Blöcke nach Schlüssel abgeglichen, Listen nur mit den nötigen
/// Verschiebungen geändert. Funkverkehr ändert nur Sprechpunkte.
/// </para>
/// </summary>
public sealed partial class BrettViewModel : ObservableObject
{
    /// <summary>So lange steht jemand, der neu in den Funk kommt und keinen Platz hat, unter den Nachzüglern.</summary>
    public static readonly TimeSpan NachzueglerDauer = TimeSpan.FromMinutes(15);

    /// <summary>Ab so viel Restzeit warnt der Kopf vor dem Ende des Auftrags.</summary>
    public static readonly TimeSpan WarnungVorEnde = TimeSpan.FromMinutes(15);

    /// <summary>Das Kontenverzeichnis liefert der Server höchstens alle 5 s (4.8).</summary>
    private static readonly TimeSpan VerzeichnisAbstand = TimeSpan.FromSeconds(5);

    /// <summary>So lange gilt eine eigene Umstellung als eigene, wenn ihr Push erst nach der Antwort kommt.</summary>
    private static readonly TimeSpan EigeneFrist = TimeSpan.FromSeconds(30);

    private readonly IFunkDienst _dienst;
    private readonly IOberflaechentakt _takt;
    private readonly Func<DateTimeOffset> _jetzt;
    private readonly TimeSpan _kartenAbstand;
    private readonly Dictionary<uint, BrettKarte> _karten = [];
    private readonly Dictionary<uint, VerzeichnisEintrag> _verzeichnis = [];
    private readonly Dictionary<uint, DateTimeOffset> _neuSeit = [];
    private readonly Dictionary<uint, (ushort? SchiffId, ushort? RolleId)> _vorlaeufig = [];
    private readonly Dictionary<uint, (ushort? SchiffId, ushort? RolleId)> _serverPlatz = [];
    private readonly Dictionary<uint, ((ushort? SchiffId, ushort? RolleId) Platz, DateTimeOffset Bis)> _eigene = [];
    private readonly Dictionary<uint, MitgliedskarteAntwort> _mitgliedskarten = [];
    private readonly HashSet<uint> _kartenAngefragt = [];
    private readonly Dictionary<RollenBlock, List<FreierPlatz>> _frei = [];
    private readonly Stack<Zug> _zuege = new();
    private RollenkatalogAntwort? _katalog;
    private FunkLage _lage = FunkLage.Leer;
    private string _struktur = "";
    private bool _gefuellt;
    private bool _geladen;
    private bool _schalterIntern;
    private DateTimeOffset _letzterTakt = DateTimeOffset.MinValue;
    private DateTimeOffset? _verzeichnisGeholt;
    private Task _kartenLaden = Task.CompletedTask;

    /// <summary>Warteschlange der Anfragen: eine nach der anderen, in der Reihenfolge der Ablagen (auf dem Oberflächen-Thread).</summary>
    private Task _kette = Task.CompletedTask;

    /// <param name="jetzt">Uhr für Fristen, Nachzügler und Verlauf (Tests: fest).</param>
    /// <param name="kartenAbstand">Abstand zwischen zwei Mitgliedskarten (Server: höchstens 5/s); ohne Angabe 250 ms, Tests 0.</param>
    public BrettViewModel(IFunkDienst dienst, IOberflaechentakt takt, Func<DateTimeOffset>? jetzt = null, TimeSpan? kartenAbstand = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _takt = takt;
        _jetzt = jetzt ?? (() => DateTimeOffset.Now);
        _kartenAbstand = kartenAbstand ?? TimeSpan.FromMilliseconds(250);
        Ziehen = new BrettZiehen(this);
        Teilnehmerziel.Brett = this;
        _dienst.LageGeaendert += () => _takt.Einreihen(Aktualisieren);
        Aktualisieren();
    }

    /// <summary>Ob es für diese Lage ein Brett gibt: verbunden und mit ZUWEISEN (dauerhaft oder aus einem laufenden Auftrag).</summary>
    public static bool Gilt(FunkLage l) => l is { IstAktiv: true, Konto.Einsatzleitung: true };

    /// <summary>
    /// Seit 0.12.0: das Feld "Meldungen" unter dem Brett, nur für wen senden darf (<see cref="MeldungenViewModel.Gilt"/>); setzt das Hauptmodell. Darf
    /// jemand senden, ohne das Brett zu haben (Führung ohne Auftrag), zeigt die Seite nur das Feld.
    /// </summary>
    [ObservableProperty]
    public partial MeldungenViewModel? Meldungen { get; set; }

    /// <summary>Ziehen von Karten (gong-wpf-dragdrop); freie Plätze lassen sich nicht ziehen.</summary>
    public GongSolutions.Wpf.DragDrop.IDragSource Ziehen { get; }

    [ObservableProperty]
    public partial bool Sichtbar { get; private set; }

    /// <summary>"Einsatzleitung bis 04:00 (von Nordwind): Op Nachtfalke", "Einsatzleitung als Admin".</summary>
    [ObservableProperty]
    public partial string AuftragText { get; private set; } = "";

    /// <summary>15 Minuten vor dem Ende des Auftrags: "Noch 12 Minuten, dann endet die Einsatzleitung."</summary>
    [ObservableProperty]
    public partial string? AuftragWarnung { get; private set; }

    /// <summary>Wer im Auftrag leitet, kann ihn abgeben (6.12 Regel 6); ein Admin hat nichts abzugeben.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbgebenCommand))]
    public partial bool KannAbgeben { get; private set; }

    /// <summary>"23 online - 19 mit Platz - 4 ohne".</summary>
    [ObservableProperty]
    public partial string Zaehler { get; private set; } = "";

    /// <summary>"Wechsel sperren": wer einen Platz hat, wechselt nicht mehr selbst; Nachzügler ohne Platz steigen noch ein (F9).</summary>
    [ObservableProperty]
    public partial bool WechselGesperrt { get; set; }

    /// <summary>"Ganz sperren": auch Nachzügler steigen nicht mehr selbst ein.</summary>
    [ObservableProperty]
    public partial bool GanzGesperrt { get; set; }

    [ObservableProperty]
    public partial string? SperrText { get; private set; }

    /// <summary>Was "Wie zuletzt für alle ohne Platz" täte, schon vor dem Klick.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WieZuletztCommand))]
    public partial string WieZuletztVorschau { get; private set; } = "";

    /// <summary>Die Karte "Abend beenden" mit ihren Häkchen ist offen.</summary>
    [ObservableProperty]
    public partial bool AbendOffen { get; set; }

    /// <summary>Beim Abend beenden die eingeteilten Online-Konten zurück auf Standard.</summary>
    [ObservableProperty]
    public partial bool AbendStandard { get; set; } = true;

    /// <summary>Beim Abend beenden den eigenen Auftrag abgeben.</summary>
    [ObservableProperty]
    public partial bool AbendAbgeben { get; set; } = true;

    /// <summary>Suche über Name, SC-Namen und Discord-Namen, auch Teile ("max" findet Mustermann).</summary>
    [ObservableProperty]
    public partial string Suche { get; set; } = "";

    /// <summary>Neu im Funk und ohne Platz, die letzten 15 Minuten.</summary>
    public ObservableCollection<BrettKarte> Nachzuegler { get; } = [];

    /// <summary>Online, ohne Platz.</summary>
    public ObservableCollection<BrettKarte> OhnePlatz { get; } = [];

    /// <summary>Offline und ohne Platz, aus dem Kontenverzeichnis: zum Vorab-Einteilen (Reservierung).</summary>
    public ObservableCollection<BrettKarte> AlleMitglieder { get; } = [];

    [ObservableProperty]
    public partial bool AlleOffen { get; set; }

    /// <summary>Die Liste links als Ablageziel: zurück auf Standard.</summary>
    public TeilnehmerZiel Teilnehmerziel { get; } = new();

    public ObservableCollection<EinheitGruppe> Gruppen { get; } = [];

    [ObservableProperty]
    public partial NetzZiel? Global { get; private set; }

    public ObservableCollection<NetzZiel> Sondernetze { get; } = [];

    /// <summary>Die gewählte Karte fürs Detailfeld.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatAuswahl))]
    [NotifyCanExecuteChangedFor(nameof(StummCommand), nameof(PlatzRaeumenCommand), nameof(SetzenCommand))]
    public partial BrettKarte? Auswahl { get; set; }

    public bool HatAuswahl => Auswahl is not null;

    public ObservableCollection<DetailFeld> AuswahlFelder { get; } = [];

    /// <summary>Platz, wie er zustande kam, Verbindung, Stummschaltung, Rollen der gewählten Person.</summary>
    [ObservableProperty]
    public partial string? AuswahlText { get; private set; }

    /// <summary>Die letzten 20 Umstellungen, neueste oben.</summary>
    public ObservableCollection<string> Verlauf { get; } = [];

    // ---------------------------------------------------------------- Phasen (6.14, RAID-S2-ENTWURF Abschnitt 3)

    /// <summary>Die Phasenleiste im Kopf: "1 Anflug | 2 Entern | 3 Rückzug", die laufende hervorgehoben.</summary>
    public ObservableCollection<PhasenKnopf> Phasen { get; } = [];

    /// <summary>Es gibt Phasen (mindestens eine über der Grundstellung) und der Server kennt sie (ab 0.5.0).</summary>
    [ObservableProperty]
    public partial bool PhasenSichtbar { get; private set; }

    /// <summary>Die Rückfrage nach dem Klick auf eine Phase, mit der Zahl der Betroffenen; Enter (Ja) schaltet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatPhasenRueckfrage))]
    [NotifyCanExecuteChangedFor(nameof(PhaseJaCommand))]
    public partial string? PhasenRueckfrage { get; private set; }

    public bool HatPhasenRueckfrage => PhasenRueckfrage is not null;

    private byte? _phaseGewaehlt;

    /// <summary>
    /// Die Phase, die lief, als die Rückfrage kam: "Ja" erwartet sie (6.14 Regel 2). Schaltete ein zweiter Leiter inzwischen, sagt der
    /// Server VERALTET, statt die Flotte von dessen Phase aus umzustellen, was niemand gesehen hat (Gegenprüfung S2).
    /// </summary>
    private byte _phaseGewaehltVon = 1;

    private (byte Nr, DateTimeOffset Seit)? _phaseGesehen;

    /// <summary>Ein Eintrag von außen, etwa ein Wechsel über die Phasentasten ("du: Phase Entern, 9 umgestellt").</summary>
    public void VerlaufEintragen(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        VerlaufDazu(text);
    }

    /// <summary>Klick auf eine Phase: Rückfrage mit der Zahl; die laufende noch einmal tut nichts.</summary>
    [RelayCommand]
    private void PhaseWaehlen(PhasenKnopf? knopf)
    {
        var l = _lage;
        if (knopf is null || knopf.Nr == l.PhaseNr || l.PhaseVon(knopf.Nr) is not { } ziel)
        {
            PhasenRueckfrage = null;
            _phaseGewaehlt = null;
            return;
        }
        var (umgestellt, ausgelassen) = Phasenrechnung.Betroffene(l, l.PhaseNr, ziel.Nr);
        _phaseGewaehlt = ziel.Nr;
        _phaseGewaehltVon = l.PhaseNr;
        PhasenRueckfrage = "Auf Phase " + Phasenrechnung.Name(ziel) + " schalten? " + umgestellt.Count.ToString(CultureInfo.CurrentCulture) + " Funker"
            + (ausgelassen.Count > 0 ? ", ausgelassen: " + Phasenrechnung.Ausgelassene(ausgelassen.Select(f => f.ScName ?? f.Name)) : "") + ".";
    }

    private bool KannPhaseJa() => _phaseGewaehlt is not null;

    [RelayCommand(CanExecute = nameof(KannPhaseJa))]
    private async Task PhaseJa()
    {
        if (_phaseGewaehlt is not { } nr) return;
        PhasenRueckfrage = null;
        _phaseGewaehlt = null;
        var l = _lage;
        var name = l.PhaseVon(nr) is { } p ? Phasenrechnung.Name(p) : nr.ToString(CultureInfo.CurrentCulture);
        var a = await _dienst.PhaseSchaltenAsync(nr, _phaseGewaehltVon).ConfigureAwait(true);
        var jetzt = _dienst.Lage;
        if (a.Ok)
        {
            var w = a.Wert!;
            Meldung = "Phase " + name + " läuft - " + w.Umgestellt.ToString(CultureInfo.CurrentCulture) + " umgestellt"
                + (w.Ausgelassen.Count > 0 ? ", ausgelassen: " + Phasenrechnung.Ausgelassene(w.Ausgelassen.Select(jetzt.HauptnameVon)) : "");
            VerlaufDazu("du: Phase " + (jetzt.PhaseVon(nr)?.Name ?? name) + ", " + w.Umgestellt.ToString(CultureInfo.CurrentCulture) + " umgestellt");
            _phaseGesehen = jetzt.LaufendePhase is { } lp ? (lp.Nr, lp.Seit) : _phaseGesehen;
        }
        else
        {
            Meldung = "Nicht geschaltet: " + (a.Code == Fehlercode.Veraltet ? "die Phase hat inzwischen gewechselt, " + Phasenrechnung.Laufend(jetzt) + " läuft" : a.Fehler);
        }
        Aktualisieren();
    }

    [RelayCommand]
    private void PhaseNein()
    {
        PhasenRueckfrage = null;
        _phaseGewaehlt = null;
    }

    /// <summary>Leiste nach der Lage; ein Wechsel, der nicht von hier kam, kommt in den Verlauf.</summary>
    private void PhasenAbgleichen(FunkLage l)
    {
        PhasenSichtbar = l.ServerS2 && l.Phasen.Count > 1;
        var phasen = PhasenSichtbar ? Phasenrechnung.Geordnet(l) : [];
        Sammlung.Abgleichen(Phasen, phasen, p => p.Nr, k => k.Nr, p => new PhasenKnopf(p.Nr), (_, _) => { });
        foreach (var (knopf, p) in Phasen.Zip(phasen))
        {
            knopf.Text = Phasenrechnung.Name(p);
            knopf.Laufend = p.Nr == l.PhaseNr;
            knopf.Beschreibung = "Phase " + knopf.Text + (knopf.Laufend ? ", läuft" : "");
        }
        if (l.LaufendePhase is not { } lp) return;
        var gesehen = _phaseGesehen;
        _phaseGesehen = (lp.Nr, lp.Seit);
        if (gesehen is null || gesehen == (lp.Nr, lp.Seit) || lp.Durch == l.Konto?.Id) return;
        VerlaufDazu(lp.Durch == 0 ? "Phase " + lp.Name + " (Rückfall oder Konsole)" : l.HauptnameVon(lp.Durch) + ": Phase " + lp.Name);
    }

    /// <summary>Der letzte Fehler oder Hinweis in Klartext.</summary>
    [ObservableProperty]
    public partial string? Meldung { get; private set; }

    /// <summary>Alle Plätze für "Setzen auf ..." (Kontextmenü, Tastatur).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SetzenZiel> Ziele { get; private set; } = [];

    /// <summary>Die Karte eines Kontos, wenn das Brett es kennt.</summary>
    public BrettKarte? Karte(uint kontoId) => _karten.GetValueOrDefault(kontoId);

    /// <summary>Bildtakt (30/s); gearbeitet wird höchstens einmal je Sekunde: Warnung vor dem Ende, Nachzügler werden älter.</summary>
    public void Takt()
    {
        var jetzt = _jetzt();
        if ((jetzt - _letzterTakt).Duration() < TimeSpan.FromSeconds(1)) return;
        _letzterTakt = jetzt;
        if (!Sichtbar) return;
        KopfSetzen(_lage);
        if (FuerAlleSichtbar && _sperrenAlleGeholt is { } geholt && (jetzt - geholt).Duration() >= SperrenAlleAbstand) _ = SperrenFuerAlleLadenAsync();
        // Wer seit 15 Minuten ohne Platz ist, ist kein Nachzügler mehr: einmal umsortieren und vergessen. Blieb der Eintrag
        // stehen, rechnete das Brett danach jede Sekunde ganz neu (Gegenprüfung 01.10.2026).
        var abgelaufen = _neuSeit.Where(p => jetzt - p.Value >= NachzueglerDauer).Select(p => p.Key).ToList();
        if (abgelaufen.Count == 0) return;
        foreach (var id in abgelaufen) _neuSeit.Remove(id);
        Aktualisieren();
    }

    /// <summary>Bis alle angefragten Mitgliedskarten da sind (Tests).</summary>
    internal Task KartenAbwartenAsync() => _kartenLaden;

    /// <summary>Wie oft das Brett die Lage ganz abgeglichen hat (Messwert für Tests).</summary>
    internal int Aufbauten { get; private set; }

    // ================================================================ Lage auf das Brett

    private void Aktualisieren()
    {
        Aufbauten++;
        var l = _dienst.Lage;
        _lage = l;
        Sichtbar = Gilt(l);
        if (!Sichtbar)
        {
            AbendOffen = false;
            ChAbbrechen();
            SperrenNein();
            AlleAbbrechen();
            // Nach dem Wiederverbinden gleich neu holen, nicht erst im Takt.
            _sperrenAlleGeholt = null;
            SperrenFuerAlle.Clear();
            HatSperrenFuerAlle = false;
            return;
        }
        if (!_geladen)
        {
            _geladen = true;
            _ = NachschlagenAsync();
        }
        FuerAlleSichtbar = l.ServerKanaele;
        if (!FuerAlleSichtbar) AlleAbbrechen();
        // Die Sperren für alle beim ersten Zeigen holen (und wenn der Server sie erst jetzt kennt); danach im Takt alle 10 s.
        if (FuerAlleSichtbar && _sperrenAlleGeholt is null) _ = SperrenFuerAlleLadenAsync();
        KopfSetzen(l);
        PhasenAbgleichen(l);
        StrukturAbgleichen(l);
        KartenAbgleichen(l);
        PlaetzeVerteilen(l);
        NetzeAbgleichen(l);
        NamenAbgleichen(l);
        BaumAbgleichen(l);
        DetailSetzen();
        WieZuletztVorschau = WieZuletztPlan(l).Text;
        _gefuellt = true;
    }

    /// <summary>Katalog und Verzeichnis, einmal beim ersten Zeigen; die Mitgliedskarten danach in Ruhe.</summary>
    private async Task NachschlagenAsync()
    {
        var k = await _dienst.RollenkatalogAsync().ConfigureAwait(true);
        if (k.Ok) _katalog = k.Wert;
        await VerzeichnisHolenAsync(erzwingen: false).ConfigureAwait(true);
        if (Sichtbar) Aktualisieren();
    }

    private void KopfSetzen(FunkLage l)
    {
        var konto = l.Konto!;
        if (konto.Beauftragung is { Aktiv: true } b && !konto.HatDauerhaft(Recht.Zuweisen))
        {
            AuftragText = "Einsatzleitung bis " + Zeit(b.Bis, l) + " (von " + l.HauptnameVon(b.Durch) + ")" + (b.Grund is { Length: > 0 } g ? ": " + g : "");
            KannAbgeben = true;
            var rest = b.Bis - (_jetzt() + l.ZeitVersatz);
            AuftragWarnung = rest > TimeSpan.Zero && rest <= WarnungVorEnde ? Minuten((int)Math.Ceiling(rest.TotalMinutes)) + ", dann endet die Einsatzleitung." : null;
        }
        else
        {
            AuftragText = konto.Stufe == Stufe.Admin ? "Einsatzleitung als Admin" : "Einsatzleitung (dauerhaftes Recht)";
            KannAbgeben = false;
            AuftragWarnung = null;
        }
        Zaehler = string.Create(CultureInfo.CurrentCulture, $"{l.Online} online - {l.MitPlatz} mit Platz - {Math.Max(0, l.Online - l.MitPlatz)} ohne");
        _schalterIntern = true;
        try
        {
            WechselGesperrt = !l.Selbstwahl;
            GanzGesperrt = !l.Selbstwahl && !l.Nachzuegler;
        }
        finally
        {
            _schalterIntern = false;
        }
        if (l.Selbstwahl)
        {
            SperrText = null;
            return;
        }
        var wer = l.SelbstwahlDurch switch
        {
            null or 0 => null,
            { } d when d == konto.Id => "dich",
            { } d => l.HauptnameVon(d),
        };
        var teil = (wer is null ? "" : " durch " + wer) + (l.SelbstwahlBis is { } bis ? " bis " + Zeit(bis, l) : "");
        SperrText = l.Nachzuegler ? "Wechsel gesperrt" + teil + " - Nachzügler ohne Platz steigen noch selbst ein." : "Einsteigen ganz gesperrt" + teil + ".";
    }

    private static string Minuten(int n) => n == 1 ? "Noch 1 Minute" : "Noch " + n.ToString(CultureInfo.CurrentCulture) + " Minuten";

    /// <summary>Eine Zeit des Servers als Uhrzeit dieses PCs: "04:00", mit Datum, wenn sie weiter weg ist.</summary>
    private string Zeit(DateTimeOffset server, FunkLage l) => Uhrzeit((server - l.ZeitVersatz).ToLocalTime());

    private string Uhrzeit(DateTimeOffset lokal) => (lokal - _jetzt()).Duration() < TimeSpan.FromHours(20)
        ? lokal.ToString("HH:mm", CultureInfo.CurrentCulture)
        : lokal.ToString("ddd dd.MM. HH:mm", CultureInfo.CurrentCulture);

    // ---------------------------------------------------------------- Spalten und Blöcke

    /// <summary>Einheiten, Rollen und Einheitsnetze; nur bei einer Änderung der Flotte wird etwas umgebaut, nach Schlüssel.</summary>
    private void StrukturAbgleichen(FunkLage l)
    {
        var stand = Strukturstand(l);
        if (string.Equals(stand, _struktur, StringComparison.Ordinal)) return;
        _struktur = stand;
        var gruppen = new List<(string Schluessel, string Titel, List<SchiffInfo?> Spalten)>();
        foreach (var (art, titel) in new[] { (Einheitsart.Schiff, "Schiffe"), (Einheitsart.Trupp, "Infanterie"), (Einheitsart.Orga, "Orga") })
        {
            var spalten = l.Schiffe.Where(s => s.Art == art).Select(s => (SchiffInfo?)s).ToList();
            if (spalten.Count > 0) gruppen.Add((art.ToString(), titel + " (" + spalten.Count.ToString(CultureInfo.CurrentCulture) + ")", spalten));
        }
        if (l.Rollen.Any(r => !r.BrauchtSchiff)) gruppen.Add(("OHNE", "Rollen ohne Einheit", [null]));

        Sammlung.Abgleichen(Gruppen, gruppen, g => g.Schluessel, g => g.Schluessel, g => new EinheitGruppe(g.Schluessel), (_, _) => { });
        foreach (var (gruppe, soll) in Gruppen.Zip(gruppen))
        {
            gruppe.Titel = soll.Titel;
            Sammlung.Abgleichen(gruppe.Spalten, soll.Spalten, s => s?.Id ?? 0, s => s.SchiffId ?? 0, s => new EinheitSpalte(s?.Id) { Brett = this }, (_, _) => { });
            foreach (var (spalte, einheit) in gruppe.Spalten.Zip(soll.Spalten)) SpalteBauen(spalte, einheit, l);
        }
        var bloecke = Gruppen.SelectMany(g => g.Spalten).SelectMany(s => s.Bloecke).ToHashSet();
        foreach (var weg in _frei.Keys.Where(b => !bloecke.Contains(b)).ToList()) _frei.Remove(weg);
    }

    private void SpalteBauen(EinheitSpalte spalte, SchiffInfo? einheit, FunkLage l)
    {
        spalte.Name = einheit?.Name ?? "Ohne Einheit";
        spalte.Typ = einheit?.Typ;
        var rollen = einheit is null
            ? l.Rollen.Where(r => !r.BrauchtSchiff).Select(r => r.Id).ToList()
            : [.. einheit.Plaetze.Select(p => p.RolleId).OrderBy(id => l.RolleVon(id)?.Reihenfolge ?? int.MaxValue).ThenBy(id => id)];
        Sammlung.Abgleichen(spalte.Bloecke, rollen, r => r, b => b.RolleId, r => new RollenBlock(einheit?.Id, r) { Brett = this }, (_, _) => { });
        foreach (var block in spalte.Bloecke)
        {
            var rolle = l.RolleVon(block.RolleId);
            block.Titel = rolle?.Name ?? "Rolle " + block.RolleId.ToString(CultureInfo.InvariantCulture);
            block.Netze = rolle is null ? "" : string.Join(", ", rolle.KanaeleAuf(einheit).Select(l.KanalName));
        }
        var netze = einheit is null
            ? []
            : einheit.Netze.Values.Distinct().Select(id => l.NetzVon(id)).OfType<NetzInfo>().OrderBy(n => n.Reihenfolge).ThenBy(n => n.Id).ToList();
        Sammlung.Abgleichen(spalte.Netze, netze, n => n.Id, n => n.NetzId, n => new EinheitNetz(n.Id) { Name = n.Name }, (z, n) => z.Name = n.Name);
    }

    private static string Strukturstand(FunkLage l) =>
        string.Join("|", l.Schiffe.Select(s => string.Create(CultureInfo.InvariantCulture,
            $"{s.Id}:{s.Name}:{s.Art}:{s.Typ}:{string.Join(",", s.Plaetze.Select(p => p.RolleId + "/" + p.Kapazitaet + "/" + p.Mindest))}:{string.Join(",", s.Netze.Select(n => n.Key + "=" + n.Value))}")))
        + "#" + string.Join("|", l.Rollen.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Id}:{r.Name}:{r.Reihenfolge}:{r.BrauchtSchiff}:{r.Kapazitaet}:{string.Join(",", r.Netze)}")))
        + "#" + string.Join("|", l.Netze.Select(n => string.Create(CultureInfo.InvariantCulture, $"{n.Id}:{n.Name}:{n.Reihenfolge}:{n.SchiffId}")));

    // ---------------------------------------------------------------- Karten

    /// <summary>Jede Person aus Praesenz und Verzeichnis bekommt genau eine Karte; ihre Angaben werden gesetzt, nie die Karte ersetzt.</summary>
    private void KartenAbgleichen(FunkLage l)
    {
        var jetzt = _jetzt();
        var suche = Suche.Trim();
        var gesehen = new HashSet<uint>();
        foreach (var f in l.Funker)
        {
            gesehen.Add(f.KontoId);
            var neu = !_karten.TryGetValue(f.KontoId, out var k);
            if (k is null)
            {
                k = new BrettKarte(f.KontoId) { Brett = this };
                _karten[f.KontoId] = k;
            }
            // Nachzügler: neu im Funk ohne Platz (nicht beim ersten Stand des Bretts, nicht nach dem Räumen eines Platzes).
            if (f.Online && f.RolleId is null && _gefuellt && (neu || !k.Online) && !_neuSeit.ContainsKey(f.KontoId)) _neuSeit[f.KontoId] = jetzt;
            if (!f.Online || f.RolleId is not null) _neuSeit.Remove(f.KontoId);

            var server = (f.SchiffId, f.RolleId);
            if (_gefuellt && _serverPlatz.TryGetValue(f.KontoId, out var vorher) && vorher != server) Umstellung(l, f, server, jetzt);
            _serverPlatz[f.KontoId] = server;

            var platz = _vorlaeufig.TryGetValue(f.KontoId, out var v) ? v : server;
            KarteSetzen(k, l, f.KontoId, f.ScName ?? f.Name, f.Name, f.ScName is null, f.DiscordName, f.Online, f.Zeichen, f.Abzeichen, f.AbzeichenVorn,
                f.Platz, f.Details, platz, f.Details?.LetzterPlatz, suche);
        }
        foreach (var e in _verzeichnis.Values)
        {
            if (gesehen.Contains(e.KontoId)) continue;
            if (!_karten.TryGetValue(e.KontoId, out var k))
            {
                k = new BrettKarte(e.KontoId) { Brett = this };
                _karten[e.KontoId] = k;
            }
            _serverPlatz[e.KontoId] = (null, null);
            var platz = _vorlaeufig.TryGetValue(e.KontoId, out var v) ? v : (null, null);
            KarteSetzen(k, l, e.KontoId, e.ScName ?? e.Name, e.Name, e.ScName is null, e.DiscordName, false, [], [], null, null, null, platz,
                e.LetzterPlatz is { } lp ? (lp.SchiffId, lp.RolleId) : null, suche);
        }
        foreach (var weg in _karten.Keys.Where(id => !gesehen.Contains(id) && !_verzeichnis.ContainsKey(id)).ToList())
        {
            _karten.Remove(weg);
            _serverPlatz.Remove(weg);
            _neuSeit.Remove(weg);
            if (Auswahl?.KontoId == weg) Auswahl = null;
        }
        foreach (var (id, k) in _karten) k.Spricht = l.Sprecher.Any(s => s.KontoId == id);
        KartenAnfragen();
    }

    private void KarteSetzen(BrettKarte k, FunkLage l, uint id, string name, string kontoname, bool ohneScName, string? discord, bool online,
        IReadOnlyList<string> zeichen, IReadOnlyList<string> abzeichen, string? vorn, PlatzStatus? platz, FunkerDetails? details, (ushort? SchiffId, ushort? RolleId) ort,
        (ushort? SchiffId, ushort? RolleId)? zuletzt, string suche)
    {
        var (schiff, rolle) = ort;
        k.Name = name;
        k.OhneScName = ohneScName;
        k.DiscordName = discord;
        k.Online = online;
        k.SchiffId = schiff;
        k.RolleId = rolle;
        // Seit 0.11.7 mit den sichtbaren Abzeichen; vorn steht das gewählte, sonst das erste (die Logik unten hängt weiter nur an den Zeichen).
        var kennungen = string.Join(",", zeichen) + "|" + string.Join(",", abzeichen) + "|" + vorn;
        if (!string.Equals(kennungen, k.ZeichenStand, StringComparison.Ordinal))
        {
            k.ZeichenStand = kennungen;
            k.AbzeichenVorn = vorn;
            k.Zeichen = Zeichensatz.Sichtbare(zeichen, abzeichen);
        }
        k.IstAdmin = (_verzeichnis.TryGetValue(id, out var ve) && ve.Stufe == Stufe.Admin) || zeichen.Contains(Zeichensatz.Admin);
        // 6.15: Details (Praesenz) gelten vor dem Verzeichnis, das nur auf Anfrage kommt.
        k.AlsFunker = k.IstAdmin && (details is not null ? details.AlsFunker : ve?.AlsFunker == true);
        k.Geschuetzt = k.IstAdmin && !k.AlsFunker;
        k.Marke = rolle is null ? null
            : !online && (platz == PlatzStatus.Reserviert || _vorlaeufig.ContainsKey(id)) ? "reserviert"
            : details?.Quelle switch
            {
                MitgliedschaftsQuelle.Selbst or MitgliedschaftsQuelle.Rolle or MitgliedschaftsQuelle.Satz => "selbst",
                MitgliedschaftsQuelle.Verschieben or MitgliedschaftsQuelle.Aufstellung when details.Durch is { } d => d == l.Konto!.Id ? "von dir" : "von " + l.HauptnameVon(d),
                // Ein Server bis 0.10.0 meldet eine Einteilung nach dem Login als WIEDERHERGESTELLT mit durch (seit 0.11.0 wie VERSCHIEBEN).
                MitgliedschaftsQuelle.Wiederhergestellt when details.Durch is { } d and not 0 && d != id => d == l.Konto!.Id ? "von dir" : "von " + l.HauptnameVon(d),
                MitgliedschaftsQuelle.Wiederhergestellt => "wie zuvor",
                _ => null,
            };
        k.Zuletzt = rolle is null && zuletzt is { } z && WerIstDaZeile.Platztext(l, z.SchiffId, z.RolleId) is { } t ? "zuletzt: " + t : null;
        if (_mitgliedskarten.TryGetValue(id, out var karte))
        {
            k.Rolle = ErsteRolle(karte);
            k.Rollen = RollenText(karte);
        }
        k.Treffer = suche.Length == 0
            || name.Contains(suche, StringComparison.CurrentCultureIgnoreCase)
            || kontoname.Contains(suche, StringComparison.CurrentCultureIgnoreCase)
            || discord?.Contains(suche, StringComparison.CurrentCultureIgnoreCase) == true;
        var platzText = rolle is null ? "ohne Platz" : WerIstDaZeile.Platztext(l, schiff, rolle);
        k.Beschreibung = string.Join(", ", new[]
        {
            name, discord is not null && !string.Equals(discord, name, StringComparison.Ordinal) ? "Discord " + discord : null,
            k.Zeichen.Count == 0 ? null : "Abzeichen " + string.Join(", ", k.Zeichen.Select(z => z.Titel)), k.Rolle, platzText, k.Marke,
            online ? "online" : "offline", k.IstAdmin ? k.AlsFunker ? "Admin, heute als Funker" : "Admin, teilt sich selbst ein" : null, k.Zuletzt,
        }.Where(x => !string.IsNullOrEmpty(x)));
    }

    private string? ErsteRolle(MitgliedskarteAntwort k)
    {
        if (k.Rollen is [var erste, ..]) return Katalogname(erste);
        if ((k.ErsteWahl ?? (k.Lieblingsrollen is [var lieb, ..] ? lieb : null)) is { } w and not 0) return "Wunsch " + Katalogname(w);
        return null;
    }

    private string? RollenText(MitgliedskarteAntwort k)
    {
        var teile = new List<string>();
        if (k.Rollen is { Count: > 0 } r) teile.Add("Rollen: " + string.Join(", ", r.Select(Katalogname)));
        if (k.Lieblingsrollen is { Count: > 0 } w) teile.Add("Wünsche: " + string.Join(", ", w.Select(Katalogname)));
        return teile.Count == 0 ? null : string.Join("; ", teile);
    }

    private string Katalogname(ushort id) => _katalog?.Eintraege.FirstOrDefault(e => e.Id == id)?.Name ?? "Rolle " + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Mitgliedskarten nachholen (erste Mitgliederrolle, Wünsche), in Ruhe, höchstens 4 je Sekunde (Server: 5).</summary>
    private void KartenAnfragen()
    {
        if (_karten.Keys.All(_kartenAngefragt.Contains) || !_kartenLaden.IsCompleted) return;
        _kartenLaden = KartenLadenAsync();
    }

    private async Task KartenLadenAsync()
    {
        while (Sichtbar)
        {
            // Wer keinen Platz hat, zuerst: dort hilft die Rolle beim Einteilen.
            var naechste = _karten.Values.Where(k => !_kartenAngefragt.Contains(k.KontoId)).OrderBy(k => k.RolleId is null ? 0 : 1).ThenBy(k => k.KontoId).FirstOrDefault();
            if (naechste is null) return;
            _kartenAngefragt.Add(naechste.KontoId);
            var a = await _dienst.MitgliedskarteAsync(naechste.KontoId).ConfigureAwait(true);
            if (a.Ok)
            {
                _mitgliedskarten[naechste.KontoId] = a.Wert!;
                naechste.Rolle = ErsteRolle(a.Wert!);
                naechste.Rollen = RollenText(a.Wert!);
                if (ReferenceEquals(Auswahl, naechste)) DetailSetzen();
            }
            if (_kartenAbstand > TimeSpan.Zero) await Task.Delay(_kartenAbstand).ConfigureAwait(true);
        }
    }

    /// <summary>Eine Umstellung, die nicht von hier kam, in den Verlauf (eigene schreibt <see cref="AusfuehrenAsync"/>).</summary>
    private void Umstellung(FunkLage l, FunkerInfo f, (ushort? SchiffId, ushort? RolleId) neu, DateTimeOffset jetzt)
    {
        if (_eigene.TryGetValue(f.KontoId, out var eigen))
        {
            if (eigen.Platz == neu || jetzt > eigen.Bis) _eigene.Remove(f.KontoId);
            if (eigen.Platz == neu) return;
        }
        var d = f.Details;
        if (d is { Quelle: MitgliedschaftsQuelle.Verschieben or MitgliedschaftsQuelle.Aufstellung } && d.Durch == l.Konto!.Id) return;
        var name = f.ScName ?? f.Name;
        var ziel = f.RolleId is null ? "ohne Platz" : WerIstDaZeile.Platztext(l, f.SchiffId, f.RolleId);
        VerlaufDazu(d?.Quelle switch
        {
            MitgliedschaftsQuelle.Verschieben or MitgliedschaftsQuelle.Aufstellung when d.Durch is { } durch => l.HauptnameVon(durch) + ": " + name + " -> " + ziel,
            MitgliedschaftsQuelle.Selbst or MitgliedschaftsQuelle.Rolle or MitgliedschaftsQuelle.Satz => name + " steigt ein: " + ziel,
            _ => name + " -> " + ziel,
        });
    }

    private void VerlaufDazu(string text)
    {
        Verlauf.Insert(0, _jetzt().ToString("HH:mm", CultureInfo.CurrentCulture) + " " + text);
        while (Verlauf.Count > 20) Verlauf.RemoveAt(Verlauf.Count - 1);
    }

    // ---------------------------------------------------------------- Verteilen

    /// <summary>
    /// Bringt jede Karte in ihre Liste: den Rollenblock ihres Platzes, sonst links (Nachzügler, ohne Platz, alle Mitglieder). Nur
    /// Listen, deren Inhalt sich ändert, bekommen Änderungen, und auch die nur so viele wie nötig.
    /// </summary>
    private void PlaetzeVerteilen(FunkLage l)
    {
        var jetzt = _jetzt();
        var bloecke = Gruppen.SelectMany(g => g.Spalten).SelectMany(s => s.Bloecke).ToDictionary(b => (b.SchiffId ?? 0, b.RolleId));
        var inBlock = bloecke.Values.ToDictionary(b => b, _ => new List<BrettKarte>());
        var nachzuegler = new List<BrettKarte>();
        var ohne = new List<BrettKarte>();
        var alle = new List<BrettKarte>();
        var praesent = l.Funker.Select(f => f.KontoId).ToHashSet();
        foreach (var k in _karten.Values)
        {
            if (k.RolleId is { } r && bloecke.TryGetValue(((ushort)(k.SchiffId ?? 0), r), out var b))
            {
                inBlock[b].Add(k);
                continue;
            }
            if (!praesent.Contains(k.KontoId) || !k.Online) alle.Add(k);
            else if (_neuSeit.TryGetValue(k.KontoId, out var seit) && jetzt - seit < NachzueglerDauer) nachzuegler.Add(k);
            else ohne.Add(k);
        }
        foreach (var (block, karten) in inBlock)
        {
            Sortieren(karten);
            var kapazitaet = Kapazitaet(l, block);
            var frei = kapazitaet is { } kap ? Math.Max(0, kap - karten.Count) : 0;
            if (!_frei.TryGetValue(block, out var platzhalter)) _frei[block] = platzhalter = [];
            while (platzhalter.Count < frei) platzhalter.Add(new FreierPlatz(block, platzhalter.Count));
            var soll = new List<object>(karten.Count + frei + 1);
            soll.AddRange(karten);
            soll.AddRange(platzhalter.Take(frei));
            // Ohne Grenze gibt es keine freien Plätze; die Zeile der Vorschau bleibt, solange sie steht, auch über einen Push hinweg (Sprechanzeige).
            if (_vorschauEingehaengt && _vorschau is { Vorschau: { } gezogen } v && ReferenceEquals(v.Block, block)) soll.Insert(Einfuegestelle(karten, gezogen), v);
            Angleichen(block.Eintraege, soll);
            block.Belegung = kapazitaet is { } k2 ? string.Create(CultureInfo.CurrentCulture, $"{karten.Count}/{k2}") : karten.Count.ToString(CultureInfo.CurrentCulture);
        }
        Angleichen(Nachzuegler, Sortieren(nachzuegler));
        Angleichen(OhnePlatz, Sortieren(ohne));
        Angleichen(AlleMitglieder, Sortieren(alle));

        foreach (var spalte in Gruppen.SelectMany(g => g.Spalten))
        {
            var belegt = spalte.Bloecke.Sum(b => b.Eintraege.OfType<BrettKarte>().Count());
            var kap = spalte.Bloecke.Sum(b => Kapazitaet(l, b) ?? 0);
            spalte.Belegung = spalte.SchiffId is null ? belegt.ToString(CultureInfo.CurrentCulture) : string.Create(CultureInfo.CurrentCulture, $"{belegt}/{kap}");
            (spalte.Ampel, spalte.AmpelText) = Ampel(l, spalte);
        }
        var ziele = Gruppen.SelectMany(g => g.Spalten).SelectMany(s => s.Bloecke.Select(b => new SetzenZiel((s.SchiffId is null ? b.Titel : s.Name + " - " + b.Titel) + " (" + b.Belegung + ")", b))).ToList();
        if (!ziele.SequenceEqual(Ziele)) Ziele = ziele;
    }

    private static List<BrettKarte> Sortieren(List<BrettKarte> k)
    {
        k.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return k;
    }

    /// <summary>Wo <paramref name="karte"/> unter den nach Namen sortierten <paramref name="karten"/> stünde (die Zeile der Vorschau ohne Platzgrenze).</summary>
    private static int Einfuegestelle(IEnumerable<BrettKarte> karten, BrettKarte karte) =>
        karten.Count(k => !ReferenceEquals(k, karte) && StringComparer.CurrentCultureIgnoreCase.Compare(k.Name, karte.Name) < 0);

    /// <summary>Bringt <paramref name="ziel"/> auf <paramref name="soll"/> (gleiche Objekte), mit so wenigen Änderungen wie möglich.</summary>
    private static void Angleichen<T>(ObservableCollection<T> ziel, IReadOnlyList<T> soll) where T : class
    {
        var bleibt = new HashSet<T>(soll, ReferenceEqualityComparer.Instance);
        for (var i = ziel.Count - 1; i >= 0; i--)
            if (!bleibt.Contains(ziel[i])) ziel.RemoveAt(i);
        for (var i = 0; i < soll.Count; i++)
        {
            if (i < ziel.Count && ReferenceEquals(ziel[i], soll[i])) continue;
            var alt = -1;
            for (var j = i + 1; j < ziel.Count; j++)
            {
                if (ReferenceEquals(ziel[j], soll[i]))
                {
                    alt = j;
                    break;
                }
            }
            if (alt >= 0) ziel.Move(alt, i);
            else ziel.Insert(i, soll[i]);
        }
    }

    /// <summary>Plätze einer Rolle auf der Einheit, ohne Einheit die Kapazität der Rolle; <c>null</c> heißt unbegrenzt.</summary>
    private static int? Kapazitaet(FunkLage l, RollenBlock b) => b.SchiffId is { } s
        ? l.SchiffVon(s)?.PlatzVon(b.RolleId)?.Kapazitaet ?? 0
        : l.RolleVon(b.RolleId)?.Kapazitaet;

    /// <summary>
    /// Ampel aus der Mindestbesetzung (F5): grün, wenn alle Mindestplätze besetzt sind, rot, wenn mehr als die Hälfte fehlt,
    /// sonst gelb; der Text nennt, was fehlt ("kein Pilot", "Gunner 1 von 2"). Ohne Mindestbesetzung keine Ampel.
    /// </summary>
    private static (Ampelstufe, string?) Ampel(FunkLage l, EinheitSpalte spalte)
    {
        if (spalte.SchiffId is not { } s || l.SchiffVon(s) is not { } einheit) return (Ampelstufe.Keine, null);
        var pflicht = 0;
        var fehlt = 0;
        var texte = new List<string>();
        foreach (var block in spalte.Bloecke)
        {
            var mindest = einheit.PlatzVon(block.RolleId)?.Mindest ?? 0;
            if (mindest == 0) continue;
            var da = block.Eintraege.OfType<BrettKarte>().Count();
            pflicht += mindest;
            if (da >= mindest) continue;
            fehlt += mindest - da;
            texte.Add(da == 0 ? "kein " + block.Titel : string.Create(CultureInfo.CurrentCulture, $"{block.Titel} {da} von {mindest}"));
        }
        if (pflicht == 0) return (Ampelstufe.Keine, null);
        if (fehlt == 0) return (Ampelstufe.Gruen, "Mindestbesetzung da");
        return (fehlt * 2 > pflicht ? Ampelstufe.Rot : Ampelstufe.Gelb, string.Join(", ", texte));
    }

    // ---------------------------------------------------------------- Netze und Detail

    /// <summary>Ein Kanal (6.16) zählt für sein Kanalnetz: Die Kanalliste zeigt Brücke, nicht jeden Ch.</summary>
    private static ushort Listennetz(ushort id) => Kanalrechnung.Zerlegen(id, out var n, out _) ? n : id;

    private void NetzeAbgleichen(FunkLage l)
    {
        // Wer in einer Ruhe steht, ist nicht drin.
        var mitglieder = new Dictionary<ushort, int>();
        foreach (var f in l.Funker)
            foreach (var id in (f.Details?.Felder ?? []).Where(x => !Kanalrechnung.IstRuhe(x.NetzId)).Select(x => Listennetz(x.NetzId)).Distinct())
                mitglieder[id] = mitglieder.GetValueOrDefault(id) + 1;
        bool Spricht(ushort id) => l.Sprecher.Any(s => s.Netze.Any(n => Listennetz(n) == id));
        if (l.Global is { } g)
        {
            if (Global?.NetzId != g.Id) Global = new NetzZiel(g.Id, true);
            Global.Name = g.Name;
            Global.Mitglieder = "alle";
            Global.Spricht = Spricht(g.Id);
        }
        else
        {
            Global = null;
        }
        var sonder = l.Netze.Where(n => !n.IstGlobal && n.SchiffId is null).ToList();
        Sammlung.Abgleichen(Sondernetze, sonder, n => n.Id, z => z.NetzId, NetzZielNeu, (z, n) =>
        {
            z.Name = n.Name;
            z.IstKanalnetz = n.IstKanalnetz;
        });
        foreach (var z in Sondernetze)
        {
            z.Mitglieder = mitglieder.GetValueOrDefault(z.NetzId).ToString(CultureInfo.CurrentCulture) + " drin";
            z.Spricht = Spricht(z.NetzId);
        }
        PersonenAbgleichen(l);
        foreach (var spalte in Gruppen.SelectMany(x => x.Spalten))
            foreach (var n in spalte.Netze) n.Spricht = Spricht(n.NetzId);
    }

    /// <summary>
    /// Die Personen in den Kanälen der Kanalliste (0.11.1): je Feld einer Person auf einem Kanal rechts (bei einem Kanalnetz: ein Ch., keine
    /// Ruhe) eine <see cref="KanalPerson"/>, nach Namen. Ein Durchgang über alle Funker für alle Kanäle zugleich, die Netze der Rolle nur bei
    /// einem Treffer (Befund der Prüfung 0.11.1: je Kanal ein Durchgang mit einem Netz-Set je Funker, bei jedem Push, auch jeder
    /// Sprechanzeige, bei 30 Kanälen und 100 Leuten rund 3000 Sets je Takt). Nur bei einer Änderung wird eine Liste neu gefüllt, sonst
    /// bliebe nach jedem Push nichts stehen.
    /// </summary>
    private void PersonenAbgleichen(FunkLage l)
    {
        var ziele = new Dictionary<ushort, NetzZiel>();
        foreach (var z in Sondernetze) ziele[z.NetzId] = z;
        var listen = new Dictionary<ushort, List<KanalPerson>>();
        var netze = new Dictionary<ushort, NetzInfo?>();
        foreach (var f in l.Funker)
        {
            if (f.Details is not { } d || !_karten.TryGetValue(f.KontoId, out var karte)) continue;
            HashSet<ushort>? rollenNetze = null;
            foreach (var x in d.Felder)
            {
                if (Kanalrechnung.IstRuhe(x.NetzId) || !ziele.TryGetValue(Listennetz(x.NetzId), out var z)) continue;
                rollenNetze ??= RollenNetze(l, f.SchiffId, f.RolleId);
                if (!netze.TryGetValue(z.NetzId, out var netz)) netze[z.NetzId] = netz = l.NetzVon(z.NetzId);
                var zusatz = x.Fest && !AusDerRolle(l, f.SchiffId, f.RolleId, rollenNetze, x);
                var ch = l.ChVon(x.NetzId) is { } c && netz?.Kanaele is { } namen && c < namen.Count ? namen[c] : null;
                var art = x.Hoeren ? x.Senden ? "hört und sendet" : "hört" : x.Senden ? "sendet" : "aus";
                if (!listen.TryGetValue(z.NetzId, out var liste)) listen[z.NetzId] = liste = [];
                // Q116 (Sarevoc, 12.10.2026): Eine Kachel aus der Rolle ist nicht ziehbar, der Hilfetext sagt es; kein roter Satz beim Ziehen.
                liste.Add(new KanalPerson(karte, z, x.Feld, x.NetzId, l.KanalName(x.NetzId), x.Hoeren, x.Senden, x.Fest, zusatz)
                {
                    Text = karte.Name + (ch is null ? "" : " (" + ch + ")") + (x.Senden ? "" : ", hört"),
                    Beschreibung = karte.Name + " in " + l.KanalName(x.NetzId) + ": " + art + ", Feld " + (x.Feld + 1).ToString(CultureInfo.InvariantCulture)
                        + (zusatz ? ", Zusatzkanal. Ziehen zieht um; in die Liste links oder das Kreuz nimmt den Zusatzkanal weg."
                            : ", aus der Rolle: Den Kanal bestimmt der Platz, die Kachel lässt sich nicht ziehen."),
                });
            }
        }
        foreach (var z in Sondernetze)
        {
            var liste = listen.GetValueOrDefault(z.NetzId) ?? [];
            liste.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name) is var n and not 0 ? n : a.Feld.CompareTo(b.Feld));
            if (z.Personen.SequenceEqual(liste)) continue;
            z.Personen.Clear();
            foreach (var p in liste) z.Personen.Add(p);
        }
    }

    private NetzZiel NetzZielNeu(NetzInfo n)
    {
        var z = new NetzZiel(n.Id, false) { Name = n.Name, IstKanalnetz = n.IstKanalnetz };
        z.Hoeren.Brett = this;
        z.HoerenSenden.Brett = this;
        return z;
    }

    partial void OnAuswahlChanged(BrettKarte? oldValue, BrettKarte? newValue)
    {
        if (oldValue is not null) oldValue.Gewaehlt = false;
        if (newValue is not null) newValue.Gewaehlt = true;
        SperrenNein();
        // Das Raster für ein Feld gehört zur bisherigen Person (seit 0.11.0); eine Ablage auf ein Kanalnetz bleibt offen.
        if (_chAblage is { Feld: not null }) ChAbbrechen();
        DetailSetzen();
    }

    private void DetailSetzen()
    {
        if (Auswahl is not { } k || !Sichtbar)
        {
            if (AuswahlFelder.Count > 0) AuswahlFelder.Clear();
            if (AuswahlSperren.Count > 0) AuswahlSperren.Clear();
            AuswahlText = null;
            return;
        }
        var l = _lage;
        var d = l.Funker.FirstOrDefault(x => x.KontoId == k.KontoId)?.Details;
        var rollenNetze = RollenNetze(l, k.SchiffId, k.RolleId);
        // Kanalsperren (6.17) nur an Server ab 0.6.0; ein ADMIN-Konto sperrt nur ein ADMIN, auch im Funker-Modus (Rangregel 6.10).
        var sperrenErlaubt = l.ServerKanaele && (!k.IstAdmin || l.Konto?.Stufe == Stufe.Admin);
        var sperren = d?.Gesperrt ?? [];
        var geschuetzt = Geschuetzt(k);
        var felder = (d?.Felder ?? []).Select(x =>
        {
            var sperre = KanalsperreInfo.Treffer(sperren, x.NetzId);
            var art = sperre is not null ? Sperrangabe(sperre, l)
                : (x.Hoeren ? x.Senden ? "hört und sendet" : "hört" : x.Senden ? "sendet" : "aus") + (x.Fest ? ", fest" : "");
            // Q46 (0.11.0): Den Ch. eines Felds wählt die Einsatzleitung für die Person, wenn das Feld ein Kanalnetz trägt (nicht Ch. 1 von Global).
            var ch = l.ChVon(x.NetzId);
            var kanalnetz = ch is not null && Kanalrechnung.Zerlegen(x.NetzId, out var kn, out _) ? kn : (ushort?)null;
            var chWaehlbar = l.ServerKanaele && kanalnetz is { } netz && x.NetzId != Kanalrechnung.GlobalSelbstId && !geschuetzt && !sperren.Any(s => s.NetzId == netz);
            return new DetailFeld(x.Feld, x.NetzId, l.KanalName(x.NetzId), art, x.Fest && !AusDerRolle(l, k.SchiffId, k.RolleId, rollenNetze, x))
            {
                Sperrbar = sperrenErlaubt && Sperrbar(l, x.NetzId),
                Sperre = sperre,
                Kanalnetz = kanalnetz,
                Ch = kanalnetz is null ? null : ch,
                Hoeren = x.Hoeren,
                Senden = x.Senden,
                Fest = x.Fest,
                ChWaehlbar = chWaehlbar,
            };
        }).ToList();
        if (!AuswahlFelder.SequenceEqual(felder))
        {
            AuswahlFelder.Clear();
            foreach (var x in felder) AuswahlFelder.Add(x);
        }
        var ohneFeld = sperren.Where(s => !felder.Any(f => f.Sperre == s)).Select(s => new DetailSperre(s, SperrKanal(l, s.NetzId), Sperrangabe(s, l))).ToList();
        if (!AuswahlSperren.SequenceEqual(ohneFeld))
        {
            AuswahlSperren.Clear();
            foreach (var x in ohneFeld) AuswahlSperren.Add(x);
        }
        var seit = d?.Seit is { } s ? " seit " + DateTimeOffset.FromUnixTimeMilliseconds(s).Subtract(l.ZeitVersatz).ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) : "";
        var teile = new List<string>
        {
            k.RolleId is null ? "ohne Platz" : WerIstDaZeile.Platztext(l, k.SchiffId, k.RolleId) + (k.Marke is { } m ? " (" + m + seit + ")" : ""),
            !k.Online ? "offline" : d?.Verbindung is { } v ? (v == Verbindungsweg.Udp ? "UDP" : "Tunnel") + (d.Guete is { } q ? ", Güte " + Guete(q) : "") : "online",
        };
        var jetzt = (_jetzt() + l.ZeitVersatz).ToUnixTimeMilliseconds();
        foreach (var st in (d?.Stumm ?? []).Where(x => x.Bis > jetzt))
        {
            teile.Add((st.NetzId switch { 0 => "stumm", 1 => "stumm in Global", _ => "stumm in " + l.KanalName(st.NetzId) })
                + " bis " + DateTimeOffset.FromUnixTimeMilliseconds(st.Bis).Subtract(l.ZeitVersatz).ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture));
        }
        if (k.Rollen is { } r) teile.Add(r);
        AuswahlText = string.Join("\n", teile);
    }

    private static string Guete(Guete q) => q switch { Protokoll.Steuerung.Guete.Gut => "gut", Protokoll.Steuerung.Guete.Maessig => "mäßig", _ => "schlecht" };

    /// <summary>Die Netze, die die Rolle auf ihrer Einheit mitbringt; was darüber hinaus fest ist, ist ein Zusatznetz.</summary>
    private static HashSet<ushort> RollenNetze(FunkLage l, ushort? schiff, ushort? rolle) =>
        rolle is { } r && l.RolleVon(r) is { } ro ? [.. ro.NetzeAuf(l.SchiffVon(schiff))] : [];

    /// <summary>
    /// Ob ein Feld aus der Rolle stammt: sein Netz steht unter <paramref name="rollenNetze"/>, oder es trägt einen Ch. des Kanalnetzes, das
    /// die Rolle in derselben Feldnummer hat. Bis 0.10.0 zählte nur das Erste; ein festes Rollenfeld mit Ch. galt deshalb als Zusatznetz,
    /// stand mit "entfernen" im Detail und ging beim Umsetzen ohne Feldnummer mit (seit 0.11.0 nicht mehr).
    /// </summary>
    private static bool AusDerRolle(FunkLage l, ushort? schiff, ushort? rolle, HashSet<ushort> rollenNetze, FeldInfo x)
    {
        if (rollenNetze.Contains(x.NetzId)) return true;
        if (!Kanalrechnung.Zerlegen(x.NetzId, out var kanalnetz, out _) || l.RolleVon(rolle) is not { } ro) return false;
        var einheit = l.SchiffVon(schiff);
        foreach (var rf in ro.Felder)
        {
            if (rf.Feld != x.Feld) continue;
            var netz = rf.NetzId ?? (einheit is not null && rf.Kennung is not null && einheit.Netze.TryGetValue(rf.Kennung, out var e) ? e : (ushort)0);
            if (netz == kanalnetz || (Kanalrechnung.Zerlegen(netz, out var n, out _) && n == kanalnetz)) return true;
        }
        return false;
    }

    // ================================================================ Bewerten und Ablegen

    /// <summary>Wie eine Ablage von <paramref name="karte"/> auf <paramref name="ziel"/> wirken würde, mit Satz für die Anzeige.</summary>
    public Ablagebewertung Bewerten(BrettKarte karte, IAblageziel ziel)
    {
        ArgumentNullException.ThrowIfNull(karte);
        ArgumentNullException.ThrowIfNull(ziel);
        if (!Sichtbar) return new(Ablagewirkung.GehtNicht, "Ohne Einsatzleitung teilt niemand ein.");
        if (Geschuetzt(karte)) return new(Ablagewirkung.GehtNicht, karte.Name + " ist Admin - teilt sich selbst ein");
        switch (ziel)
        {
            case RollenBlock b:
                return PlatzBewerten(karte, b.SchiffId, b.RolleId);
            case BrettKarte andere:
                if (ReferenceEquals(andere, karte)) return Ablagebewertung.Nichts;
                if (andere.RolleId is null) return Bewerten(karte, Teilnehmerziel);
                if (Geschuetzt(andere)) return new(Ablagewirkung.GehtNicht, andere.Name + " ist Admin - teilt sich selbst ein");
                if (andere.SchiffId == karte.SchiffId && andere.RolleId == karte.RolleId) return new(Ablagewirkung.Keine, "steht schon da");
                return new(Ablagewirkung.Tausch, karte.RolleId is null ? "Tausch mit " + andere.Name + ": " + andere.Name + " geht auf Standard" : "Tausch mit " + andere.Name);
            case EinheitSpalte s:
                return BesterPlatz(karte, s) is { } p ? PlatzBewerten(karte, p.SchiffId, p.RolleId) : new(Ablagewirkung.GehtNicht, s.Name + " ist voll");
            case TeilnehmerZiel:
                // 0.11.2: Ein Name von links zurück nach links tut nichts (Sarevocs Befund in 0.11.1: eine Person landete aus Versehen auf "ohne Platz").
                // Herausnehmen geht über die Person im Stammbaum, je Kanal.
                return Ablagebewertung.Nichts;
            case BaumCh or BaumKanal:
                return BaumBewerten(karte, null, ziel);
            case NetzAblage n:
                var art = n.Senden ? "hört und sendet" : "hört";
                var details = _lage.Funker.FirstOrDefault(f => f.KontoId == karte.KontoId)?.Details;
                if (n.Netz.IstKanalnetz)
                {
                    return details?.Gesperrt.Any(s => s.NetzId == n.Netz.NetzId) == true
                        ? new(Ablagewirkung.GehtNicht, karte.Name + " ist in " + n.Netz.Name + " gesperrt (alle Ch.)")
                        : new(Ablagewirkung.Passt, n.Netz.Name + " als Zusatzkanal: " + art + ", Ch. danach wählen");
                }
                var feld = details?.Felder.FirstOrDefault(f => f.NetzId == n.Netz.NetzId);
                if (feld is { Hoeren: true } && feld.Senden == n.Senden) return new(Ablagewirkung.Keine, karte.Name + " hat " + n.Netz.Name + " schon");
                return new(Ablagewirkung.Passt, feld is null ? n.Netz.Name + " als Zusatzkanal: " + art : n.Netz.Name + ": jetzt " + art);
            default:
                return Ablagebewertung.Nichts;
        }
    }

    private Ablagebewertung PlatzBewerten(BrettKarte karte, ushort? schiff, ushort rolle)
    {
        if (karte.SchiffId == schiff && karte.RolleId == rolle) return new(Ablagewirkung.Keine, "steht schon da");
        var l = _lage;
        var ort = WerIstDaZeile.Platztext(l, schiff, rolle) ?? "";
        if (l.RolleVon(rolle) is { BrauchtSchiff: true } r && schiff is null) return new(Ablagewirkung.GehtNicht, r.Name + " braucht eine Einheit");
        var block = Block(schiff, rolle);
        var besetzt = block?.Eintraege.OfType<BrettKarte>().Count(k => !ReferenceEquals(k, karte)) ?? 0;
        if (block is not null && Kapazitaet(l, block) is { } kap && besetzt >= kap)
            return new(Ablagewirkung.GehtNicht, ort + " ist voll (" + besetzt.ToString(CultureInfo.CurrentCulture) + "/" + kap.ToString(CultureInfo.CurrentCulture) + ")");
        var netze = l.RolleVon(rolle)?.KanaeleAuf(l.SchiffVon(schiff)).Select(l.KanalName).ToList() ?? [];
        return new(Ablagewirkung.Passt, "passt: " + ort + (netze.Count > 0 ? " - Kanäle " + string.Join(", ", netze) : "") + (karte.Online ? "" : " (reserviert, offline)"));
    }

    private RollenBlock? Block(ushort? schiff, ushort rolle) =>
        Gruppen.SelectMany(g => g.Spalten).Where(s => s.SchiffId == schiff).SelectMany(s => s.Bloecke).FirstOrDefault(b => b.RolleId == rolle);

    /// <summary>ADMIN-Konten bewegt nur ein ADMIN (Rangregel 6.10), außer sie spielen heute als Funker (6.15).</summary>
    private bool Geschuetzt(BrettKarte k) => k.IstAdmin && !k.AlsFunker && _lage.Konto?.Stufe != Stufe.Admin;

    /// <summary>
    /// Bester freier Platz auf einer Einheit (Ablage auf den Kopf): die bisherige Rolle, sonst die Mitgliederrollen, die erste Wahl
    /// und die Wünsche (über den Rollenkatalog auf Rollen der Flotte), dann der letzte Platz, sonst die Rolle mit den meisten
    /// freien Plätzen. Ohne freien Platz <c>null</c>.
    /// </summary>
    private (ushort? SchiffId, ushort RolleId)? BesterPlatz(BrettKarte karte, EinheitSpalte spalte)
    {
        var l = _lage;
        var frei = spalte.Bloecke
            .Select(b => (b.RolleId, Frei: Kapazitaet(l, b) is { } kap ? kap - b.Eintraege.OfType<BrettKarte>().Count(k => !ReferenceEquals(k, karte)) : int.MaxValue))
            .Where(x => x.Frei > 0).ToList();
        if (frei.Count == 0) return null;
        var wunsch = new List<ushort>();
        if (karte.RolleId is { } bisher) wunsch.Add(bisher);
        if (_mitgliedskarten.TryGetValue(karte.KontoId, out var mk))
        {
            foreach (var k in (mk.Rollen ?? []).Concat(mk.ErsteWahl is { } e ? [e] : []).Concat(mk.Lieblingsrollen ?? []))
                wunsch.AddRange(_katalog?.Eintraege.FirstOrDefault(x => x.Id == k)?.Rollen ?? []);
        }
        var zuletzt = l.Funker.FirstOrDefault(f => f.KontoId == karte.KontoId)?.Details?.LetzterPlatz
            ?? (_verzeichnis.TryGetValue(karte.KontoId, out var v) && v.LetzterPlatz is { } lp ? (lp.SchiffId, lp.RolleId) : null);
        if (zuletzt is { RolleId: { } letzte }) wunsch.Add(letzte);
        foreach (var w in wunsch)
            if (frei.Any(x => x.RolleId == w)) return (spalte.SchiffId, w);
        var beste = frei.OrderByDescending(x => x.Frei).ThenBy(x => l.RolleVon(x.RolleId)?.Reihenfolge ?? int.MaxValue).First();
        return (spalte.SchiffId, beste.RolleId);
    }

    /// <summary>
    /// Legt ab: ein Verschieben (Tausch mit zwei Änderungen), sofort sichtbar mit "wartet", bei einem Fehler zurück mit Grund.
    /// <c>true</c>, wenn der Server zugestimmt hat. Mit <paramref name="punkt"/> (Loslassen mit der Maus) meldet <see cref="Gelandet"/> die
    /// Karte in ihrem Block, sobald sie vorläufig dort steht, damit die Ansicht sie dorthin gleiten lässt (0.11.1, Punkt 4).
    /// </summary>
    public async Task<bool> AblegenAsync(BrettKarte karte, IAblageziel ziel, Ablagepunkt? punkt = null)
    {
        ArgumentNullException.ThrowIfNull(karte);
        ArgumentNullException.ThrowIfNull(ziel);
        // Ein offenes Ch.-Raster gehört zur vorigen Ablage; mit der nächsten ist es vorbei (0.11.1: sonst blieb es stehen, und ein späterer
        // Klick auf einen Ch. traf noch die alte Person).
        ChAbbrechen();
        var bewertung = Bewerten(karte, ziel);
        if (!bewertung.Moeglich)
        {
            if (bewertung.Wirkung == Ablagewirkung.GehtNicht) Meldung = bewertung.Text;
            return false;
        }
        if (ziel is BaumCh or BaumKanal) return await BaumAblegenAsync(karte, null, ziel).ConfigureAwait(true);
        // Ein Kanalnetz (6.16) nimmt nur einen Kanal auf: erst der Ch., dann das Verschieben (ChWaehlen).
        if (ziel is NetzAblage { Netz.IstKanalnetz: true } kanalnetz)
        {
            ChRasterOeffnen(karte, kanalnetz);
            return false;
        }
        var l = _lage;
        var aenderungen = new List<Aenderung>();
        switch (ziel)
        {
            case RollenBlock b:
                aenderungen.Add(Umsetzen(l, karte, b.SchiffId, b.RolleId));
                break;
            case EinheitSpalte s when BesterPlatz(karte, s) is { } p:
                aenderungen.Add(Umsetzen(l, karte, p.SchiffId, p.RolleId));
                break;
            case BrettKarte andere when andere.RolleId is { } r:
                aenderungen.Add(Umsetzen(l, karte, andere.SchiffId, r));
                aenderungen.Add(karte.RolleId is { } meine ? Umsetzen(l, andere, karte.SchiffId, meine) : Standard(andere));
                break;
            case BrettKarte or TeilnehmerZiel:
                aenderungen.Add(Standard(karte));
                break;
            case NetzAblage n:
                aenderungen.Add(Zusatzkanal(karte, n.Netz.NetzId, n.Senden, l));
                break;
            default:
                return false;
        }
        // Den Block vor dem Ausführen bestimmen: Beim Tausch wandert die andere Karte gleich weiter.
        var landeblock = punkt is null ? null : Landeblock(karte, ziel);
        // Bis zum ersten Warten stellt AusfuehrenAsync die Karten schon ans Ziel (vorläufig, "wartet"); dann steht die Karte im Block.
        var lauf = AusfuehrenAsync(aenderungen, l.ZustandsVersion, merken: true);
        if (landeblock is not null && landeblock.Eintraege.Contains(karte)) Gelandet?.Invoke(this, new Landung(karte, landeblock, punkt!));
        return (await lauf.ConfigureAwait(true)).Ok;
    }

    // ---------------------------------------------------------------- Vorschau beim Ziehen und Landung (0.11.1, Punkt 4)

    /// <summary>Der freie Platz, der gerade die Vorschau zeigt, sonst <c>null</c>.</summary>
    private FreierPlatz? _vorschau;

    /// <summary>
    /// Das Ziel, das die Vorschau zuletzt gesetzt oder bestätigt hat (Block oder Spaltenkopf). Beim Verlassen nimmt nur dieses Ziel sie weg
    /// (Befund der Prüfung 0.11.1: Vom Block auf den Spaltenkopf gewechselt, dessen bester Platz derselbe Block ist, blendete das nachgereichte
    /// Verlassen des Blocks die Vorschau aus, und sie blieb weg, bis die Maus sich rührte).
    /// </summary>
    private IAblageziel? _vorschauZiel;

    /// <summary>
    /// Die Zeile der Vorschau ist nur für die Vorschau eingehängt: Ein Block ohne Platzgrenze hat keine freien Plätze (Rollen ohne Einheit:
    /// 21 Rollen ohne Einheit und ohne Kapazität); dort steht sie an der Stelle, an der die Karte nach Namen einsortiert würde, und geht mit der
    /// Vorschau wieder fort (Befund der Prüfung 0.11.1: ohne sie gab es dort keine Vorschau, nur das Gleiten nach dem Loslassen).
    /// </summary>
    private bool _vorschauEingehaengt;

    /// <summary>
    /// Eine Karte steht nach einer Ablage mit der Maus vorläufig in ihrem Block: Die Ansicht lässt sie vom Loslasspunkt dorthin gleiten
    /// (<c>BrettAnsicht</c>). Nur für Plätze (Block, Spaltenkopf, Tausch); nach links und auf ein Netz gleitet nichts.
    /// </summary>
    internal event EventHandler<Landung>? Gelandet;

    /// <summary>
    /// Zeigt beim Ziehen, wo die Karte landen würde: gedämpft am ersten freien Platz des Blocks unter der Maus, beim Spaltenkopf am besten
    /// Platz der Einheit (die Ansicht blendet weich ein); in einem Block ohne Platzgrenze (keine freien Plätze) in einer Zeile, die nur dafür
    /// an der Stelle eingehängt wird, an der die Karte nach Namen stünde. Nur wenn die Ablage passt (grün); beim Tausch steht die gelbe Karte,
    /// bei rot und in einem vollen Block nichts. gong ruft das bei jeder Mausbewegung: Steht die Vorschau schon dort, ändert sich nichts (nur das
    /// Ziel wird gemerkt), sonst nur der alte und der neue Platz.
    /// </summary>
    internal void VorschauZeigen(BrettKarte karte, IAblageziel ziel, Ablagebewertung bewertung)
    {
        ArgumentNullException.ThrowIfNull(bewertung);
        var block = bewertung.Wirkung == Ablagewirkung.Passt ? Landeblock(karte, ziel) : null;
        var platz = block?.Eintraege.OfType<FreierPlatz>().FirstOrDefault();
        var einhaengen = platz is null && block is not null && Kapazitaet(_lage, block) is null;
        if (einhaengen) platz = Vorschauzeile(block!);
        if (platz is not null && ReferenceEquals(platz, _vorschau) && ReferenceEquals(platz.Vorschau, karte))
        {
            _vorschauZiel = ziel;
            return;
        }
        VorschauVerbergen(null, sofort: false);
        if (platz is null) return;
        platz.Vorschau = karte;
        platz.VorschauSichtbar = true;
        _vorschau = platz;
        _vorschauZiel = ziel;
        _vorschauEingehaengt = einhaengen;
        if (einhaengen) block!.Eintraege.Insert(Einfuegestelle(block.Eintraege.OfType<BrettKarte>(), karte), platz);
    }

    /// <summary>Die Zeile der Vorschau für einen Block ohne Platzgrenze: der erste Platz aus seinem Vorrat, angelegt, wenn es keinen gibt.</summary>
    private FreierPlatz Vorschauzeile(RollenBlock block)
    {
        if (!_frei.TryGetValue(block, out var vorrat)) _frei[block] = vorrat = [];
        if (vorrat.Count == 0) vorrat.Add(new FreierPlatz(block, 0));
        return vorrat[0];
    }

    /// <summary>
    /// Nimmt die Vorschau weg: weich beim Verlassen (die Karte bleibt bis zum Ende des Ausblendens stehen) oder <paramref name="sofort"/> beim
    /// Loslassen (die Karte kommt an die Stelle; die Lücke rückt eine Zeile tiefer und soll dort nicht nachblenden). Mit <paramref name="ziel"/>
    /// nur, wenn dieses Ziel die Vorschau gesetzt hat (oder die Spalte um diesen Block): gong meldet das Verlassen des Blocks erst nach dem
    /// Betreten des Spaltenkopfs, und der kann die Vorschau an demselben Platz bestätigt haben. Eine nur eingehängte Zeile (Block ohne
    /// Platzgrenze) geht sofort und ohne Blende: Ohne sie gäbe es dort keine Zeile.
    /// </summary>
    internal void VorschauVerbergen(IAblageziel? ziel, bool sofort)
    {
        if (_vorschau is not { } platz) return;
        if (ziel is not null && !(ReferenceEquals(ziel, _vorschauZiel) || (ziel is EinheitSpalte s && _vorschauZiel is RollenBlock b && s.Bloecke.Contains(b)))) return;
        if (sofort || _vorschauEingehaengt) platz.Vorschau = null;
        platz.VorschauSichtbar = false;
        if (_vorschauEingehaengt) platz.Block.Eintraege.Remove(platz);
        _vorschau = null;
        _vorschauZiel = null;
        _vorschauEingehaengt = false;
    }

    /// <summary>Der Block, in dem die Karte nach einer Ablage auf <paramref name="ziel"/> stünde (Block, bester Platz der Spalte, Block der anderen Karte beim Tausch), sonst <c>null</c>.</summary>
    private RollenBlock? Landeblock(BrettKarte karte, IAblageziel ziel) => ziel switch
    {
        RollenBlock b => b,
        EinheitSpalte s when BesterPlatz(karte, s) is { } p => Block(p.SchiffId, p.RolleId),
        BrettKarte andere when !ReferenceEquals(andere, karte) && andere.RolleId is { } r => Block(andere.SchiffId, r),
        _ => null,
    };

    // ---------------------------------------------------------------- Personen in der Kanalliste (0.11.1)

    /// <summary>
    /// Wie eine Ablage einer Person aus der Kanalliste (ein Feld) wirken würde: in die Liste links nimmt den Zusatzkanal weg; auf denselben
    /// Kanal stellt hören und senden um (bei einem Kanalnetz nach dem Ch.); auf einen anderen Kanal zieht ein Zusatzkanal um; auf einen
    /// Platz, eine Spalte oder eine Karte wie die Karte der Person.
    /// </summary>
    public Ablagebewertung Bewerten(KanalPerson person, IAblageziel ziel)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(ziel);
        if (!Sichtbar) return new(Ablagewirkung.GehtNicht, "Ohne Einsatzleitung teilt niemand ein.");
        if (Geschuetzt(person.Karte)) return new(Ablagewirkung.GehtNicht, person.Name + " ist Admin - teilt sich selbst ein");
        switch (ziel)
        {
            case TeilnehmerZiel:
                // 0.11.2: Herausnehmen geht immer, auch bei einem Kanal aus der Rolle (für diesen Abend).
                return new(Ablagewirkung.Passt, person.Name + " aus " + person.Kanal + " heraus");
            case BaumCh or BaumKanal:
                return BaumBewerten(person.Karte, person, ziel);
            case NetzAblage n:
                var art = n.Senden ? "hört und sendet" : "hört";
                if (n.Netz.NetzId == person.Netz.NetzId)
                {
                    if (n.Netz.IstKanalnetz) return new(Ablagewirkung.Passt, person.Name + " in " + n.Netz.Name + ": " + art + ", Ch. danach wählen");
                    return n.Senden == person.Senden && person.Hoeren
                        ? new(Ablagewirkung.Keine, person.Name + " " + art + " in " + n.Netz.Name + " schon")
                        : new(Ablagewirkung.Passt, person.Name + " in " + n.Netz.Name + ": jetzt " + art);
                }
                if (!person.Zusatz) return new(Ablagewirkung.GehtNicht, person.Kanal + " kommt aus der Rolle von " + person.Name + " - den Kanal bestimmt der Platz");
                var details = _lage.Funker.FirstOrDefault(f => f.KontoId == person.Karte.KontoId)?.Details;
                if (n.Netz.IstKanalnetz)
                {
                    return details?.Gesperrt.Any(s => s.NetzId == n.Netz.NetzId) == true
                        ? new(Ablagewirkung.GehtNicht, person.Name + " ist in " + n.Netz.Name + " gesperrt (alle Ch.)")
                        : new(Ablagewirkung.Passt, person.Name + ": von " + person.Kanal + " auf " + n.Netz.Name + " (" + art + "), Ch. danach wählen");
                }
                if (details?.Felder.Any(f => f.NetzId == n.Netz.NetzId) == true) return new(Ablagewirkung.Keine, person.Name + " hat " + n.Netz.Name + " schon");
                return new(Ablagewirkung.Passt, person.Name + ": von " + person.Kanal + " auf " + n.Netz.Name + " (" + art + ")");
            default:
                return Bewerten(person.Karte, ziel);
        }
    }

    /// <summary>Legt eine Person aus der Kanalliste ab (<see cref="Bewerten(KanalPerson, IAblageziel)"/>); <c>true</c>, wenn der Server zugestimmt hat.</summary>
    public async Task<bool> AblegenAsync(KanalPerson person, IAblageziel ziel, Ablagepunkt? punkt = null)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(ziel);
        ChAbbrechen();
        var bewertung = Bewerten(person, ziel);
        if (!bewertung.Moeglich)
        {
            if (bewertung.Wirkung == Ablagewirkung.GehtNicht) Meldung = bewertung.Text;
            return false;
        }
        var l = _lage;
        switch (ziel)
        {
            case TeilnehmerZiel:
                return await BaumAusfuehrenAsync(person.Karte, [new FeldPatch { Feld = person.Feld, Leeren = true }], person.Name + " - " + person.Kanal).ConfigureAwait(true);
            case BaumCh or BaumKanal:
                return await BaumAblegenAsync(person.Karte, person, ziel).ConfigureAwait(true);
            case NetzAblage { Netz.IstKanalnetz: true } kanalnetz:
                ChRasterOeffnen(person.Karte, kanalnetz, person);
                return false;
            case NetzAblage n:
                return (await AusfuehrenAsync([FeldUmziehen(person.Karte, person.Feld, n.Netz.NetzId, n.Senden, person.Fest, l)], l.ZustandsVersion, merken: false).ConfigureAwait(true)).Ok;
            default:
                return await AblegenAsync(person.Karte, ziel, punkt).ConfigureAwait(true);
        }
    }

    /// <summary>Das Kreuz an der Mini-Kachel: nimmt der Person den Zusatzkanal weg (ein <c>felderPatch</c> mit <c>leeren</c>).</summary>
    [RelayCommand]
    private async Task KanalPersonEntfernen(KanalPerson? person)
    {
        if (person is not { Zusatz: true } || !Sichtbar) return;
        await AusfuehrenAsync([FeldLeeren(person.Karte, person.Feld, person.Kanal)], _lage.ZustandsVersion, merken: false).ConfigureAwait(true);
    }

    /// <summary>Ein Feld leeren: im Verlauf "Kobold - Brücke (Schiff 3)".</summary>
    private static Aenderung FeldLeeren(BrettKarte k, byte feld, string kanal) =>
        new(k, null, new Verschiebung { KontoId = k.KontoId, FelderPatch = [new FeldPatch { Feld = feld, Leeren = true }] }, k.Name + " - " + kanal);

    /// <summary>
    /// Ein Feld umziehen (0.11.1): <c>felderPatch</c> mit Feldnummer, der Kanal (oder derselbe mit anderem hören/senden) ersetzt genau dieses
    /// Feld; so entsteht kein zweites Feld im selben Kanalnetz. Im Verlauf "Kobold Feld 6: Brücke (Schiff 7) (hört und sendet)".
    /// </summary>
    private static Aenderung FeldUmziehen(BrettKarte k, byte feld, ushort kanalId, bool senden, bool fest, FunkLage l) =>
        new(k, null, new Verschiebung
        {
            KontoId = k.KontoId,
            FelderPatch = [new FeldPatch { Feld = feld, NetzId = kanalId, Hoeren = true, Senden = senden, Fest = fest }],
        }, k.Name + " Feld " + (feld + 1).ToString(CultureInfo.InvariantCulture) + ": " + l.KanalName(kanalId) + " (" + (senden ? "hört und sendet" : "hört") + ")");

    /// <summary>Eine Änderung: wer, wohin (Platz, <c>null</c> für nur Felder), was auf den Draht geht und was im Verlauf steht.</summary>
    private sealed record Aenderung(BrettKarte Karte, (ushort? SchiffId, ushort? RolleId)? Ziel, Verschiebung Verschiebung, string Verlauf);

    private static Aenderung Umsetzen(FunkLage l, BrettKarte k, ushort? schiff, ushort rolle) =>
        new(k, (schiff, rolle), new Verschiebung
        {
            KontoId = k.KontoId,
            RolleId = rolle,
            SchiffId = schiff,
            FelderPatch = Zusatznetze(l, k) is { Count: > 0 } z ? z : null,
        }, k.Name + " -> " + WerIstDaZeile.Platztext(l, schiff, rolle));

    private static Aenderung Standard(BrettKarte k) => new(k, (null, null), new Verschiebung { KontoId = k.KontoId, RolleId = 0 }, k.Name + " -> ohne Platz");

    /// <summary>Ein Zusatzkanal als felderPatch ohne Feldnummer (der Server nimmt ein freies Feld); der Platz bleibt.</summary>
    private static Aenderung Zusatzkanal(BrettKarte k, ushort netzId, bool senden, FunkLage l) =>
        new(k, null, new Verschiebung { KontoId = k.KontoId, FelderPatch = [new FeldPatch { NetzId = netzId, Hoeren = true, Senden = senden }] },
            k.Name + " + " + l.KanalName(netzId) + " (" + (senden ? "hört und sendet" : "hört") + ")");

    /// <summary>
    /// Die festen Zusatznetze einer Person: Felder, die nicht aus ihrer Rolle stammen. <c>Verschieben</c> mit Rolle baut die Felder
    /// neu (Zustandsschreiber.Zuweisen), deshalb gehen sie als <c>felderPatch</c> mit, ohne Feldnummer (der Server nimmt ein freies).
    /// </summary>
    private static List<FeldPatch> Zusatznetze(FunkLage l, BrettKarte k)
    {
        var f = l.Funker.FirstOrDefault(x => x.KontoId == k.KontoId);
        if (f?.Details is not { } d) return [];
        var rollenNetze = RollenNetze(l, f.SchiffId, f.RolleId);
        return [.. d.Felder.Where(x => x.Fest && !AusDerRolle(l, f.SchiffId, f.RolleId, rollenNetze, x)).Select(x => new FeldPatch { NetzId = x.NetzId, Hoeren = x.Hoeren, Senden = x.Senden })];
    }

    /// <summary>
    /// Schickt die Änderungen in der Warteschlange (eine Anfrage nach der anderen). Vorher stehen die Karten am Ziel und warten,
    /// danach gilt der Stand des Servers. Mit <paramref name="merken"/> lässt sich der Zug mit Strg+Z zurücknehmen.
    /// </summary>
    private async Task<Dienstantwort<VerschiebenAntwort>> AusfuehrenAsync(List<Aenderung> aenderungen, long? basisVersion, bool merken)
    {
        var l = _lage;
        var vorher = aenderungen.Select(a => (a.Karte.KontoId, a.Karte.SchiffId, a.Karte.RolleId, Zusatznetze(l, a.Karte))).ToList();
        var phaseVorher = l.LaufendePhase;
        var bis = _jetzt() + EigeneFrist;
        foreach (var a in aenderungen)
        {
            if (a.Ziel is { } ort)
            {
                _vorlaeufig[a.Karte.KontoId] = ort;
                _eigene[a.Karte.KontoId] = (ort, bis);
            }
            a.Karte.Wartet = true;
        }
        Meldung = null;
        Aktualisieren();
        Dienstantwort<VerschiebenAntwort> antwort;
        var vorher2 = _kette;
        var fertig = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _kette = fertig.Task;
        try
        {
            await vorher2.ConfigureAwait(true);
            antwort = await _dienst.VerschiebenAsync([.. aenderungen.Select(x => x.Verschiebung)], basisVersion).ConfigureAwait(true);
        }
        finally
        {
            fertig.SetResult();
            foreach (var a in aenderungen)
            {
                _vorlaeufig.Remove(a.Karte.KontoId);
                a.Karte.Wartet = false;
            }
        }
        if (antwort.Ok)
        {
            foreach (var a in aenderungen) VerlaufDazu("du: " + a.Verlauf);
            if (merken && aenderungen.Any(a => a.Ziel is not null)) _zuege.Push(new Zug(vorher, antwort.Wert!.ZustandsVersion));
        }
        else
        {
            foreach (var a in aenderungen) _eigene.Remove(a.Karte.KontoId);
            var erste = aenderungen[0].Karte;
            var jetzt = _dienst.Lage;
            // Ein Phasenwechsel während des Zugs hebt die kontoVersion der Umgestellten (6.14): dann das, nicht ein nackter Fehler.
            Meldung = antwort.Code != Fehlercode.Veraltet ? erste.Name + ": " + antwort.Fehler
                : jetzt.LaufendePhase is { } lp && lp != phaseVorher ? "Inzwischen durch Phase " + Phasenrechnung.Laufend(jetzt) + " umgestellt - bitte noch einmal ziehen."
                : Schneller(jetzt, erste);
        }
        Aktualisieren();
        return antwort;
    }

    /// <summary>"Ferox war schneller: Kobold ist jetzt auf Schiff 2, Gunner."</summary>
    private static string Schneller(FunkLage l, BrettKarte k)
    {
        var f = l.Funker.FirstOrDefault(x => x.KontoId == k.KontoId);
        var wo = f?.RolleId is null ? "ohne Platz" : "auf " + WerIstDaZeile.Platztext(l, f.SchiffId, f.RolleId);
        return f?.Details?.Durch is { } d && d != l.Konto?.Id
            ? l.HauptnameVon(d) + " war schneller: " + k.Name + " ist jetzt " + wo + "."
            : "Der Stand hat sich inzwischen geändert: " + k.Name + " ist jetzt " + wo + ".";
    }

    /// <summary>
    /// Was ein Zug geändert hat: je Konto der Platz und die Zusatznetze davor, dazu die Version danach (für VERALTET); bei einem Zug im
    /// Stammbaum (0.11.2) stattdessen die Felder davor.
    /// </summary>
    private sealed record Zug(IReadOnlyList<(uint KontoId, ushort? SchiffId, ushort? RolleId, List<FeldPatch> Zusatz)> Vorher, long Version,
        IReadOnlyList<(uint KontoId, IReadOnlyList<FeldInfo> Felder)>? Felder = null);

    /// <summary>Strg+Z: den letzten eigenen Zug zurück, mit basisVersion; hat inzwischen jemand anderes umgestellt, bleibt dessen Stand.</summary>
    [RelayCommand]
    private async Task Rueckgaengig()
    {
        if (!_zuege.TryPop(out var zug))
        {
            Meldung = "Nichts zum Zurücknehmen.";
            return;
        }
        if (zug.Felder is not null)
        {
            await BaumZurueckAsync(zug).ConfigureAwait(true);
            return;
        }
        var l = _lage;
        var aenderungen = new List<Aenderung>();
        foreach (var (konto, schiff, rolle, zusatz) in zug.Vorher)
        {
            if (Karte(konto) is not { } k) continue;
            aenderungen.Add(rolle is { } r
                ? new Aenderung(k, (schiff, r), new Verschiebung { KontoId = konto, RolleId = r, SchiffId = schiff, FelderPatch = zusatz.Count > 0 ? zusatz : null },
                    k.Name + " zurück -> " + WerIstDaZeile.Platztext(l, schiff, r))
                : new Aenderung(k, (null, null), new Verschiebung { KontoId = konto, RolleId = 0 }, k.Name + " zurück -> ohne Platz"));
        }
        if (aenderungen.Count == 0) return;
        var a = await AusfuehrenAsync(aenderungen, zug.Version, merken: false).ConfigureAwait(true);
        if (a.Code != Fehlercode.Veraltet) return;
        var jetzt = _dienst.Lage;
        var wer = aenderungen.Select(x => (x.Karte, Durch: jetzt.Funker.FirstOrDefault(f => f.KontoId == x.Karte.KontoId)?.Details?.Durch))
            .FirstOrDefault(x => x.Durch is { } d && d != jetzt.Konto?.Id);
        Meldung = wer.Karte is not null
            ? "Nicht zurückgenommen: " + wer.Karte.Name + " wurde danach von " + jetzt.HauptnameVon(wer.Durch!.Value) + " verschoben."
            : "Nicht zurückgenommen: " + aenderungen[0].Karte.Name + " wurde danach umgestellt.";
    }

    // ================================================================ Kopf und Detail: Befehle

    private bool KannAbgebenAusfuehren() => KannAbgeben;

    [RelayCommand(CanExecute = nameof(KannAbgebenAusfuehren))]
    private async Task Abgeben()
    {
        var f = await _dienst.AuftragAbgebenAsync().ConfigureAwait(true);
        Meldung = f ?? "Auftrag abgegeben.";
    }

    partial void OnWechselGesperrtChanged(bool value)
    {
        if (_schalterIntern) return;
        _ = SperreSetzenAsync(value, !GanzGesperrt);
    }

    partial void OnGanzGesperrtChanged(bool value)
    {
        if (_schalterIntern) return;
        if (value || WechselGesperrt) _ = SperreSetzenAsync(true, !value);
    }

    private async Task SperreSetzenAsync(bool gesperrt, bool nachzuegler)
    {
        var f = await _dienst.SelbstwahlSetzenAsync(!gesperrt, nachzuegler).ConfigureAwait(true);
        if (f is not null) Meldung = f;
        Aktualisieren();
    }

    partial void OnSucheChanged(string value)
    {
        if (Sichtbar) KartenAbgleichen(_lage);
    }

    /// <summary>"Wie zuletzt" für alle online ohne Platz: wer einen letzten Platz hat, der noch frei ist; der Rest steht in der Vorschau.</summary>
    private (string Text, List<(BrettKarte Karte, ushort? SchiffId, ushort RolleId)> Passt) WieZuletztPlan(FunkLage l)
    {
        var frei = new Dictionary<(ushort, ushort), int>();
        foreach (var b in Gruppen.SelectMany(g => g.Spalten).SelectMany(s => s.Bloecke))
            frei[(b.SchiffId ?? 0, b.RolleId)] = Kapazitaet(l, b) is { } kap ? kap - b.Eintraege.OfType<BrettKarte>().Count() : int.MaxValue;
        var passt = new List<(BrettKarte, ushort?, ushort)>();
        var nicht = new List<string>();
        var ohne = new List<string>();
        var geschuetzt = new List<string>();
        foreach (var k in _karten.Values.Where(k => k.Online && k.RolleId is null).OrderBy(k => k.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            // Ein Admin ohne Funker-Modus würde das Verschieben aller scheitern lassen (alles oder nichts): nennen statt still auslassen.
            if (Geschuetzt(k))
            {
                geschuetzt.Add(k.Name + ": Admin, teilt sich selbst ein");
                continue;
            }
            var zuletzt = l.Funker.FirstOrDefault(f => f.KontoId == k.KontoId)?.Details?.LetzterPlatz;
            if (zuletzt is not { RolleId: { } r } z || l.RolleVon(r) is null || (z.SchiffId is { } s && l.SchiffVon(s) is null))
            {
                ohne.Add(k.Name);
                continue;
            }
            if (frei.TryGetValue((z.SchiffId ?? 0, r), out var n) && n > 0)
            {
                frei[(z.SchiffId ?? 0, r)] = n - 1;
                passt.Add((k, z.SchiffId, r));
            }
            else
            {
                nicht.Add(k.Name + " (" + WerIstDaZeile.Platztext(l, z.SchiffId, r) + " ist voll)");
            }
        }
        var nichtDabei = geschuetzt.Count > 0 ? " Nicht dabei: " + string.Join(", ", geschuetzt) + "." : "";
        if (passt.Count == 0) return ("Niemand ohne Platz hat einen freien letzten Platz." + nichtDabei, []);
        var text = passt.Count.ToString(CultureInfo.CurrentCulture) + (passt.Count == 1 ? " passt: " : " passen: ")
            + string.Join(", ", passt.Select(p => p.Item1.Name + " -> " + WerIstDaZeile.Platztext(l, p.Item2, p.Item3))) + ".";
        if (nicht.Count > 0) text += " Passt nicht: " + string.Join(", ", nicht) + ".";
        if (ohne.Count > 0) text += " Ohne letzten Platz: " + string.Join(", ", ohne) + ".";
        return (text + nichtDabei, passt);
    }

    private bool KannWieZuletzt() => Sichtbar && !WieZuletztVorschau.StartsWith("Niemand", StringComparison.Ordinal);

    /// <summary>"Wie zuletzt für alle ohne Platz": ein Verschieben für alle, die passen; ohne basisVersion, es gilt der Stand jetzt.</summary>
    [RelayCommand(CanExecute = nameof(KannWieZuletzt))]
    private async Task WieZuletzt()
    {
        var l = _lage;
        var (_, passt) = WieZuletztPlan(l);
        if (passt.Count == 0) return;
        var a = await AusfuehrenAsync([.. passt.Select(p => Umsetzen(l, p.Karte, p.SchiffId, p.RolleId))], null, merken: true).ConfigureAwait(true);
        if (a.Ok) Meldung = "Wie zuletzt: " + passt.Count.ToString(CultureInfo.CurrentCulture) + (passt.Count == 1 ? " Person eingeteilt." : " Personen eingeteilt.");
    }

    /// <summary>
    /// "Abend beenden" (RAIDLEITUNG-ENTWURF, Nach dem Raid): Selbsteinteilung wieder an, Plätze der Offline-Konten frei, eingeteilte
    /// Online-Konten zurück auf Standard (abwählbar; Admins teilen sich selbst ein) und der eigene Auftrag abgegeben (abwählbar).
    /// Beim Zurücksetzen merkt sich der Server den letzten Platz für "wie zuletzt".
    /// </summary>
    [RelayCommand]
    private async Task AbendBeenden()
    {
        var fehler = new List<string>();
        // Zuerst zurück auf die Grundstellung (6.14 Regel 12): Sonst trüge der letzte Stand ("wie letztes Mal") die Felder einer Phase.
        if (_lage is { ServerS2: true, PhaseNr: not 1 } vorher && vorher.Phasen.Count > 1
            && await _dienst.PhaseSchaltenAsync(1, vorher.PhaseNr).ConfigureAwait(true) is { Ok: false } p) fehler.Add("Phase 1: " + p.Fehler);
        if (!_lage.Selbstwahl && await _dienst.SelbstwahlSetzenAsync(true).ConfigureAwait(true) is { } f1) fehler.Add(f1);
        var frei = await _dienst.PlaetzeFreigebenAsync().ConfigureAwait(true);
        if (!frei.Ok) fehler.Add(frei.Fehler!);
        if (AbendStandard)
        {
            // Admins teilen sich selbst ein und bleiben, außer sie spielen heute als Funker (6.15): die nehmen wir mit wie jeden.
            var zurueck = _dienst.Lage.Funker.Where(x => x.Online && x.RolleId is not null
                    && ((Karte(x.KontoId)?.IstAdmin != true && !x.Zeichen.Contains(Zeichensatz.Admin)) || x.Details?.AlsFunker == true))
                .OrderBy(x => x.KontoId).Select(x => new Verschiebung { KontoId = x.KontoId, RolleId = 0 }).ToList();
            if (zurueck.Count > 0 && await _dienst.VerschiebenAsync(zurueck).ConfigureAwait(true) is { Ok: false } a) fehler.Add(a.Fehler!);
        }
        // Seit 0.11.5 schaltet "Wer ist da" stumm, bis der zweite Klick oder "Abend beenden" es aufhebt (Sarevoc 12.10.2026: "Klick = stumm, Klick =
        // wieder normal"): Hier gehen die laufenden Stummschaltungen weg, die du gesetzt hast, als Admin alle. Vor dem Abgeben, denn Aufheben
        // braucht ZUWEISEN.
        foreach (var (konto, name, netz) in StummZumAufheben(_dienst.Lage))
        {
            if (await _dienst.StummschaltenAsync(konto, netz, 0).ConfigureAwait(true) is { } fs) fehler.Add(name + ": " + fs);
        }
        if (AbendAbgeben && KannAbgeben && await _dienst.AuftragAbgebenAsync().ConfigureAwait(true) is { } f2) fehler.Add(f2);
        AbendOffen = false;
        Meldung = fehler.Count == 0 ? "Abend beendet." : "Abend beendet, aber: " + string.Join(" ", fehler);
        Aktualisieren();
    }

    /// <summary>
    /// Die laufenden Stummschaltungen, die "Abend beenden" aufhebt: die selbst gesetzten, als Admin alle (die Rangregel des Servers, 6.10, ließe
    /// die anderer sonst ohnehin nicht zu), nur nie die eigene durch jemand anderen: Die hebt der Server auch einem Admin nicht auf
    /// (Zustandsschreiber, DarfStummAufheben), und "Abend beendet, aber: ..." stand dann jedes Mal da (Gegenprüfung 0.11.5). Je Konto und Netz ein
    /// Eintrag mit dem Hauptnamen für die Meldung.
    /// </summary>
    private List<(uint Konto, string Name, ushort Netz)> StummZumAufheben(FunkLage l)
    {
        var ich = l.Konto;
        var jetzt = (_jetzt() + l.ZeitVersatz).ToUnixTimeMilliseconds();
        return [.. l.Funker.SelectMany(f => (f.Details?.Stumm ?? [])
            .Where(s => s.Bis > jetzt && (s.Durch == ich?.Id || (ich?.Stufe == Stufe.Admin && f.KontoId != ich.Id)))
            .Select(s => (f.KontoId, f.ScName ?? f.Name, s.NetzId)))];
    }

    [RelayCommand]
    private Task VerzeichnisLaden() => VerzeichnisHolenAsync(erzwingen: true);

    /// <summary>Kontenverzeichnis (N4, höchstens 1 je 5 s); von selbst nur, wenn es älter ist, auf Knopfdruck immer.</summary>
    private async Task VerzeichnisHolenAsync(bool erzwingen)
    {
        var jetzt = _jetzt();
        if (!erzwingen && _verzeichnisGeholt is { } zuletzt && (jetzt - zuletzt).Duration() < VerzeichnisAbstand) return;
        _verzeichnisGeholt = jetzt;
        var a = await _dienst.KontenVerzeichnisAsync().ConfigureAwait(true);
        if (!a.Ok)
        {
            Meldung = "Alle Mitglieder: " + a.Fehler;
            return;
        }
        _verzeichnis.Clear();
        foreach (var v in a.Wert!) _verzeichnis[v.KontoId] = v;
        if (Sichtbar) Aktualisieren();
    }

    private bool MitAuswahl() => Auswahl is not null;

    /// <summary>Rechtsklick auf die Karte: stumm für 1, 5 oder 15 Minuten in allen Netzen, "0" hebt auf.</summary>
    [RelayCommand(CanExecute = nameof(MitAuswahl))]
    private async Task Stumm(string? sekunden)
    {
        if (Auswahl is not { } k || !ushort.TryParse(sekunden, NumberStyles.None, CultureInfo.InvariantCulture, out var s)) return;
        var f = await _dienst.StummschaltenAsync(k.KontoId, 0, s).ConfigureAwait(true);
        Meldung = f is not null ? k.Name + ": " + f : s == 0 ? k.Name + " ist wieder zu hören." : k.Name + " stumm für " + (s / 60).ToString(CultureInfo.CurrentCulture) + " min.";
        DetailSetzen();
    }

    [RelayCommand(CanExecute = nameof(MitAuswahl))]
    private async Task PlatzRaeumen()
    {
        // 0.11.2: Die Liste links nimmt keine Namen mehr an (Bewerten gibt dort nichts, Sarevoc 13.10.2026). Bis dahin ging "Platz räumen" über diese
        // Ablage und hätte seitdem still nichts getan; es setzt deshalb selbst auf Standard, mit denselben Regeln wie bis 0.11.1.
        if (Auswahl is not { } k || !Sichtbar) return;
        ChAbbrechen();
        if (Geschuetzt(k))
        {
            Meldung = k.Name + " ist Admin - teilt sich selbst ein";
            return;
        }
        if (k.RolleId is null && Zusatznetze(_lage, k).Count == 0) return;
        await AusfuehrenAsync([Standard(k)], _lage.ZustandsVersion, merken: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ZusatznetzEntfernen(DetailFeld? feld)
    {
        if (Auswahl is not { } k || feld is null) return;
        await AusfuehrenAsync([FeldLeeren(k, feld.Feld, feld.Netz)], _lage.ZustandsVersion, merken: false).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(MitAuswahl))]
    private async Task Setzen(SetzenZiel? ziel)
    {
        if (Auswahl is { } k && ziel is not null) await AblegenAsync(k, ziel.Block).ConfigureAwait(true);
    }

    // ================================================================ Kanäle: Ch.-Raster und Sperren (Stufe 3, STUFE3-ENTWURF 5.6)

    public ObservableCollection<DetailSperre> AuswahlSperren { get; } = [];

    public ObservableCollection<ChKnopf> ChKnoepfe { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatChWahl))]
    public partial string? ChFrage { get; private set; }

    public bool HatChWahl => ChFrage is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatSperrRueckfrage))]
    [NotifyCanExecuteChangedFor(nameof(SperrenJaCommand))]
    public partial string? SperrRueckfrage { get; private set; }

    public bool HatSperrRueckfrage => SperrRueckfrage is not null;

    /// <summary>Wen und was "Ja, sperren" sperrt: Konto, Kanal-ID, Name und Kanal so, wie die Rückfrage sie nannte.</summary>
    private (uint KontoId, ushort NetzId, string Name, string Kanal)? _sperrZiel;

    /// <summary>
    /// Worauf das Ch.-Raster wartet: eine Ablage auf ein Kanalnetz (Karte und Ziel, hören oder hören und senden; seit 0.11.1 mit
    /// <c>VonFeld</c>, wenn eine Person aus der Kanalliste umzieht) oder, seit 0.11.0, ein Feld der gewählten Karte (Q46, Ch. je Person).
    /// Genau eins von <c>Ziel</c> und <c>Feld</c> ist gesetzt.
    /// </summary>
    private (BrettKarte Karte, NetzAblage? Ziel, DetailFeld? Feld, byte? VonFeld)? _chAblage;

    /// <summary>Ob sich ein Kanal sperren lässt (6.17): nie eine Ruhe, Global oder dessen Ch. 1, nichts Unbekanntes.</summary>
    private static bool Sperrbar(FunkLage l, ushort id) => !Kanalrechnung.IstRuhe(id) && l.NetzVon(id) is { IstGlobal: false };

    /// <summary>"Brücke (Schiff 3)", für eine Sperre auf das ganze Kanalnetz "Brücke (alle Ch.)".</summary>
    private static string SperrKanal(FunkLage l, ushort id) =>
        !Kanalrechnung.IstKanalbereich(id) && l.NetzVon(id) is { IstKanalnetz: true } n ? n.Name + " (alle Ch.)" : l.KanalName(id);

    /// <summary>"gesperrt bis 04:00 (von Nordwind)", "(von dir)".</summary>
    private string Sperrangabe(KanalsperreInfo s, FunkLage l) =>
        "gesperrt bis " + Zeit(s.Bis, l) + " (von " + (s.Durch == l.Konto?.Id ? "dir" : l.HauptnameVon(s.Durch)) + ")";

    /// <summary>"sperren" im Detailfeld: erst die Rückfrage mit Person, Kanal und Dauer.</summary>
    [RelayCommand]
    private void KanalSperren(DetailFeld? feld)
    {
        if (Auswahl is not { } k || feld is not { Sperrbar: true, Gesperrt: false }) return;
        _sperrZiel = (k.KontoId, feld.NetzId, k.Name, feld.Netz);
        SperrRueckfrage = k.Name + " in " + feld.Netz + " sperren, bis 04:00? " + k.Name + " hört und sendet dort dann nicht; freigeben geht jederzeit.";
    }

    private bool KannSperrenJa() => SperrRueckfrage is not null;

    [RelayCommand(CanExecute = nameof(KannSperrenJa))]
    private async Task SperrenJa()
    {
        if (_sperrZiel is not { } z) return;
        SperrenNein();
        var f = await _dienst.KanalSperreAsync(z.KontoId, z.NetzId, sperren: true).ConfigureAwait(true);
        var l = _dienst.Lage;
        if (f is not null)
        {
            Meldung = z.Name + ": " + f;
        }
        else
        {
            var bis = l.Funker.FirstOrDefault(x => x.KontoId == z.KontoId)?.Details?.SperreVon(z.NetzId) is { } s ? " bis " + Zeit(s.Bis, l) : "";
            Meldung = z.Name + " in " + z.Kanal + " gesperrt" + bis + ".";
            VerlaufDazu("du: " + z.Name + " gesperrt in " + z.Kanal);
        }
        Aktualisieren();
    }

    [RelayCommand]
    private void SperrenNein()
    {
        _sperrZiel = null;
        SperrRueckfrage = null;
    }

    /// <summary>"freigeben" an einem gesperrten Feld oder an einer Sperre ohne Feld: sofort, ohne Rückfrage.</summary>
    [RelayCommand]
    private async Task KanalFreigeben(KanalsperreInfo? sperre)
    {
        if (Auswahl is not { } k || sperre is null) return;
        var kanal = SperrKanal(_lage, sperre.NetzId);
        var f = await _dienst.KanalSperreAsync(k.KontoId, sperre.NetzId, sperren: false).ConfigureAwait(true);
        if (f is not null)
        {
            Meldung = k.Name + ": " + f;
        }
        else
        {
            Meldung = k.Name + " in " + kanal + " freigegeben.";
            VerlaufDazu("du: " + k.Name + " frei in " + kanal);
        }
        Aktualisieren();
    }

    /// <summary>
    /// Nach einer Ablage auf ein Kanalnetz: das Ch.-Raster ohne Ruhe; gesperrte Ch. sind nicht wählbar, ein Ch., den die Person so schon hat,
    /// auch nicht. Hat sie ihn mit anderem hören/senden, lässt er sich wählen und stellt das Feld um (0.11.1). Kommt die Person aus der
    /// Kanalliste (<paramref name="von"/>), zieht ihr Feld um: Der gewählte Ch. ersetzt es, ein zweites Feld entsteht nicht.
    /// </summary>
    private void ChRasterOeffnen(BrettKarte karte, NetzAblage ziel, KanalPerson? von = null)
    {
        if (_lage.NetzVon(ziel.Netz.NetzId) is not { IstKanalnetz: true } netz) return;
        var art = ziel.Senden ? "hört und sendet" : "hört";
        ChRasterFuellen(karte, netz, ziel.Senden, von?.Feld, name => karte.Name + " auf " + netz.Name + " (" + name + ", " + art + ") legen");
        _chAblage = (karte, ziel, null, von?.Feld);
        ChFrage = (von is null ? karte.Name + " auf " + netz.Name : karte.Name + " von " + von.Kanal + " auf " + netz.Name) + " (" + art + "): welcher Ch.?";
    }

    /// <summary>
    /// "Ch." an einem Feld im Detail (Q46, seit 0.11.0): dasselbe Raster für dieses Feld der gewählten Person, ohne Ruhe und ohne Ch. 1 von Global; der
    /// Ch., den sie schon hat (auch in einem anderen Feld), und gesperrte Ch. sind nicht wählbar.
    /// </summary>
    [RelayCommand]
    private void FeldChWaehlen(DetailFeld? feld)
    {
        if (Auswahl is not { } k || feld is not { ChWaehlbar: true, Kanalnetz: { } kanalnetz } || _lage.NetzVon(kanalnetz) is not { IstKanalnetz: true } netz) return;
        SperrenNein();
        ChRasterFuellen(k, netz, null, null, name => k.Name + ": Feld " + feld.FeldText + " auf " + name + " stellen");
        _chAblage = (k, null, feld, null);
        ChFrage = k.Name + ", Feld " + feld.FeldText + " " + netz.Name + ": welcher Ch.?";
    }

    /// <param name="senden">Wie die Ablage senden soll; <c>null</c> für "Ch." an einem Feld (hören und senden bleiben).</param>
    /// <param name="vonFeld">Das Feld, das umzieht (Person aus der Kanalliste), sonst <c>null</c>.</param>
    private void ChRasterFuellen(BrettKarte karte, NetzInfo netz, bool? senden, byte? vonFeld, Func<string, string> waehlen)
    {
        var l = _lage;
        var d = l.Funker.FirstOrDefault(f => f.KontoId == karte.KontoId)?.Details;
        ChKnoepfe.Clear();
        for (var ch = 1; ch < netz.Kanaele!.Count && ch <= Kanalrechnung.ChMax; ch++)
        {
            // Ch. 1 von Global ist Global selbst und kein Ch., den man wählt (6.16).
            if (netz.IstGlobal && ch == Kanalrechnung.GlobalSelbst) continue;
            var id = Kanalrechnung.Id(netz.Id, (byte)ch);
            var name = netz.Kanaele[ch];
            var vorhanden = d?.Felder.FirstOrDefault(f => f.NetzId == id);
            bool waehlbar;
            string hilfe;
            if (vorhanden is { } v)
            {
                // Den Ch. hat die Person schon: wählbar nur, wenn die Ablage ihn anders hören/senden lässt und es ihr Feld ist, das umzieht.
                var umstellen = senden is { } s && v.Hoeren && v.Senden != s && (vonFeld is null || vonFeld == v.Feld);
                waehlbar = umstellen;
                hilfe = umstellen
                    ? karte.Name + ": " + name + " auf " + (senden == true ? "hört und sendet" : "hört") + " stellen"
                    : karte.Name + " hat " + name + " schon" + (senden is { } s2 && v.Senden == s2 ? " (" + (s2 ? "hört und sendet" : "hört") + ")" : "");
            }
            else if (d?.SperreVon(id) is { } sperre)
            {
                waehlbar = false;
                hilfe = Sperrangabe(sperre, l);
            }
            else
            {
                waehlbar = true;
                hilfe = waehlen(name);
            }
            ChKnoepfe.Add(new ChKnopf((byte)ch, name, waehlbar, hilfe));
        }
    }

    [RelayCommand]
    private async Task ChWaehlen(ChKnopf? knopf)
    {
        if (knopf is not { Waehlbar: true } || _chAblage is not { } a) return;
        ChAbbrechen();
        var l = _lage;
        if (a.Feld is { Kanalnetz: { } kanalnetz } gewaehlt)
        {
            // Inzwischen geänderte Werte des Felds (hören, senden, fest) gelten, solange es dasselbe Kanalnetz trägt.
            var feld = ReferenceEquals(Auswahl, a.Karte) && AuswahlFelder.FirstOrDefault(x => x.Feld == gewaehlt.Feld && x.Kanalnetz == kanalnetz) is { } jetzt ? jetzt : gewaehlt;
            // Q46 und Q45: felderPatch mit Feldnummer ersetzt genau dieses Feld; hören, senden und fest gehen unverändert mit (ohne
            // "senden" setzte der Server false). Der Ch. ist ein Vorschlag, die Person darf ihn ändern. Strg+Z nimmt nur Plätze zurück.
            await AusfuehrenAsync([FeldCh(a.Karte, feld, Kanalrechnung.Id(kanalnetz, knopf.Ch), l)], l.ZustandsVersion, merken: false).ConfigureAwait(true);
            return;
        }
        if (a.Ziel is not { } ziel) return;
        var id = Kanalrechnung.Id(ziel.Netz.NetzId, knopf.Ch);
        // Ein Feld, das umzieht, oder ein Ch., den die Person mit anderem hören/senden schon hat (0.11.1): felderPatch mit Feldnummer ersetzt
        // genau dieses Feld, fest bleibt, wie es war. Sonst ein Zusatzkanal in einem freien Feld.
        var felder = l.Funker.FirstOrDefault(f => f.KontoId == a.Karte.KontoId)?.Details?.Felder ?? [];
        var umzug = a.VonFeld is { } von ? felder.FirstOrDefault(f => f.Feld == von) : felder.FirstOrDefault(f => f.NetzId == id);
        if (umzug is { } u)
        {
            await AusfuehrenAsync([FeldUmziehen(a.Karte, u.Feld, id, ziel.Senden, u.Fest, l)], l.ZustandsVersion, merken: false).ConfigureAwait(true);
            return;
        }
        await AusfuehrenAsync([Zusatzkanal(a.Karte, id, ziel.Senden, l)], l.ZustandsVersion, merken: true).ConfigureAwait(true);
    }

    /// <summary>Der Ch. eines Felds der Person als felderPatch mit Feldnummer; im Verlauf "Kobold Feld 6: Brücke (Schiff 7)".</summary>
    private static Aenderung FeldCh(BrettKarte k, DetailFeld f, ushort kanalId, FunkLage l) =>
        new(k, null, new Verschiebung
        {
            KontoId = k.KontoId,
            FelderPatch = [new FeldPatch { Feld = f.Feld, NetzId = kanalId, Hoeren = f.Hoeren, Senden = f.Senden, Fest = f.Fest }],
        }, k.Name + " Feld " + f.FeldText + ": " + l.KanalName(kanalId));

    [RelayCommand]
    private void ChAbbrechen()
    {
        _chAblage = null;
        ChKnoepfe.Clear();
        ChFrage = null;
    }

    // ================================================================ Sperren für alle an der Kanalliste (6.17, Entwurf 5.6, Sarevoc Q9; Gegenprüfung 3.7)

    /// <summary>So oft holt das sichtbare Brett die Sperren für alle neu (andere Leiter, Konsole, Ablauf); nach einer eigenen sofort.</summary>
    internal static readonly TimeSpan SperrenAlleAbstand = TimeSpan.FromSeconds(10);

    private DateTimeOffset? _sperrenAlleGeholt;

    /// <summary>Wen "Ja, für alle sperren" sperrt: Kanal-ID, Kanalnetz oder Netz, und der Kanal so, wie die Rückfrage ihn nannte.</summary>
    private (ushort NetzId, string Kanal)? _alleZiel;

    /// <summary>Die laufenden Sperren für alle ohne Einsatzleitung, je mit "freigeben".</summary>
    public ObservableCollection<SperreFuerAlle> SperrenFuerAlle { get; } = [];

    /// <summary>Es gibt mindestens eine Sperre für alle (sonst steht "Kein Kanal ist für alle gesperrt." da).</summary>
    [ObservableProperty]
    public partial bool HatSperrenFuerAlle { get; private set; }

    /// <summary>Der Server kennt Kanalsperren (ab 0.6.0): Dann gibt es an der Kanalliste "Für alle gesperrt".</summary>
    [ObservableProperty]
    public partial bool FuerAlleSichtbar { get; private set; }

    /// <summary>Die Frage beim Sperren für alle: erst nach dem Kanal, bei einem Kanal mit Ch. dann nach dem Ch.; <c>null</c>, wenn zu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatAlleWahl))]
    public partial string? AlleFrage { get; private set; }

    public bool HatAlleWahl => AlleFrage is not null;

    /// <summary>Die Knöpfe zur <see cref="AlleFrage"/>: Kanäle, danach "alle Ch." und die Ch.</summary>
    public ObservableCollection<AlleSperrKnopf> AlleKnoepfe { get; } = [];

    /// <summary>Die Rückfrage vor dem Sperren für alle, mit Kanal und Dauer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatAlleRueckfrage))]
    [NotifyCanExecuteChangedFor(nameof(AlleSperrenJaCommand))]
    public partial string? AlleRueckfrage { get; private set; }

    public bool HatAlleRueckfrage => AlleRueckfrage is not null;

    /// <summary>"Kanal für alle sperren": zuerst die Kanäle als Knöpfe; Global nur mit seinen Ch. (nie Ch. 1, Global selbst), schon gesperrte nicht wählbar.</summary>
    [RelayCommand]
    private void FuerAlleSperren()
    {
        AlleAbbrechen();
        var l = _lage;
        if (!l.ServerKanaele) return;
        foreach (var n in l.Netze.Where(n => !n.IstGlobal || n.IstKanalnetz).OrderBy(n => n.IstGlobal ? 1 : 0).ThenBy(n => n.Reihenfolge).ThenBy(n => n.Id))
        {
            var schon = SperrenFuerAlle.Any(s => s.Sperre.NetzId == n.Id);
            AlleKnoepfe.Add(new AlleSperrKnopf(n.Id, n.Name, !schon,
                schon ? n.Name + " ist schon für alle gesperrt" : n.IstKanalnetz ? n.Name + " für alle sperren: danach den Ch. wählen" : n.Name + " für alle sperren",
                n.IstKanalnetz));
        }
        AlleFrage = "Welchen Kanal für alle sperren? Die Einsatzleitung bleibt ausgenommen.";
    }

    /// <summary>Ein Knopf der Frage: bei einem Kanal mit Ch. weiter zur Wahl des Ch. ("alle Ch." nicht bei Global), sonst die Rückfrage.</summary>
    [RelayCommand]
    private void AlleKnopfWaehlen(AlleSperrKnopf? knopf)
    {
        if (knopf is not { Waehlbar: true }) return;
        var l = _lage;
        if (knopf.Weiter && l.NetzVon(knopf.NetzId) is { IstKanalnetz: true } netz)
        {
            AlleKnoepfe.Clear();
            var ganz = SperrenFuerAlle.Any(s => s.Sperre.NetzId == netz.Id);
            if (!netz.IstGlobal) AlleKnoepfe.Add(new AlleSperrKnopf(netz.Id, "alle Ch.", true, netz.Name + " mit allen Ch. für alle sperren", false));
            for (var ch = 1; ch < netz.Kanaele!.Count; ch++)
            {
                if (netz.IstGlobal && ch == Kanalrechnung.GlobalSelbst) continue;
                var id = Kanalrechnung.Id(netz.Id, (byte)ch);
                var name = netz.Kanaele[ch];
                var schon = ganz || SperrenFuerAlle.Any(s => s.Sperre.NetzId == id);
                AlleKnoepfe.Add(new AlleSperrKnopf(id, name, !schon, schon ? l.KanalName(id) + " ist schon für alle gesperrt" : l.KanalName(id) + " für alle sperren", false));
            }
            AlleFrage = netz.Name + " für alle sperren: alle Ch. oder welcher Ch.?";
            return;
        }
        var kanal = SperrKanal(l, knopf.NetzId);
        _alleZiel = (knopf.NetzId, kanal);
        AlleFrage = null;
        AlleKnoepfe.Clear();
        AlleRueckfrage = kanal + " für alle sperren, bis 04:00? Dort hört und sendet dann niemand mehr, nur die Einsatzleitung; freigeben geht jederzeit.";
    }

    private bool KannAlleSperrenJa() => AlleRueckfrage is not null;

    [RelayCommand(CanExecute = nameof(KannAlleSperrenJa))]
    private async Task AlleSperrenJa()
    {
        if (_alleZiel is not { } z) return;
        AlleAbbrechen();
        var f = await _dienst.KanalSperreAsync(0, z.NetzId, sperren: true).ConfigureAwait(true);
        if (f is not null)
        {
            Meldung = z.Kanal + ": " + f;
        }
        else
        {
            Meldung = z.Kanal + " für alle gesperrt, bis 04:00.";
            VerlaufDazu("du: " + z.Kanal + " für alle gesperrt");
        }
        await SperrenFuerAlleLadenAsync().ConfigureAwait(true);
    }

    /// <summary>Schließt Frage und Rückfrage, ohne etwas zu sperren (Abbrechen, Nein, Esc).</summary>
    [RelayCommand]
    private void AlleAbbrechen()
    {
        _alleZiel = null;
        AlleKnoepfe.Clear();
        AlleFrage = null;
        AlleRueckfrage = null;
    }

    /// <summary>"freigeben" an einer Sperre für alle: sofort, ohne Rückfrage.</summary>
    [RelayCommand]
    private async Task AlleFreigeben(SperreFuerAlle? sperre)
    {
        if (sperre is null) return;
        var f = await _dienst.KanalSperreAsync(0, sperre.Sperre.NetzId, sperren: false).ConfigureAwait(true);
        if (f is not null)
        {
            Meldung = sperre.Kanal + ": " + f;
        }
        else
        {
            Meldung = sperre.Kanal + " für alle wieder frei.";
            VerlaufDazu("du: " + sperre.Kanal + " für alle frei");
        }
        await SperrenFuerAlleLadenAsync().ConfigureAwait(true);
    }

    /// <summary>Die Sperren für alle vom Server holen (LISTE); ohne Kanalsperren am Server (vor 0.6.0) bleibt die Liste leer.</summary>
    internal async Task SperrenFuerAlleLadenAsync()
    {
        _sperrenAlleGeholt = _jetzt();
        if (!_lage.ServerKanaele)
        {
            SperrenFuerAlle.Clear();
            HatSperrenFuerAlle = false;
            return;
        }
        var a = await _dienst.SperrenFuerAlleAsync().ConfigureAwait(true);
        if (!a.Ok) return;
        var l = _lage;
        var neu = a.Wert!.Select(s => new SperreFuerAlle(s, SperrKanal(l, s.NetzId), "bis " + Zeit(s.Bis, l) + " (von " + (s.Durch == l.Konto?.Id ? "dir" : l.HauptnameVon(s.Durch)) + ")"))
            .OrderBy(s => s.Kanal, StringComparer.CurrentCulture).ToList();
        if (!SperrenFuerAlle.SequenceEqual(neu))
        {
            SperrenFuerAlle.Clear();
            foreach (var s in neu) SperrenFuerAlle.Add(s);
        }
        HatSperrenFuerAlle = SperrenFuerAlle.Count > 0;
    }

    /// <summary>Klick oder Enter auf eine Karte: wählen (noch einmal: abwählen).</summary>
    [RelayCommand]
    private void Waehlen(BrettKarte? karte) => Auswahl = ReferenceEquals(Auswahl, karte) ? null : karte;

    /// <summary>Nach dem Ziehen alle Ziele wieder neutral, keine Karte mehr gezogen, die Vorschau blendet aus.</summary>
    internal void AblagezustaendeLeeren()
    {
        IEnumerable<AblagezielBasis> alle = [Teilnehmerziel, .. Gruppen.SelectMany(g => g.Spalten), .. Gruppen.SelectMany(g => g.Spalten).SelectMany(s => s.Bloecke),
            .. Sondernetze.SelectMany(n => new AblagezielBasis[] { n.Hoeren, n.HoerenSenden }), .. _karten.Values,
            .. _baumKanaele.Values, .. _baumCh.Values, .. _baumKanaele.Values.Select(k => k.Weitere).OfType<BaumCh>()];
        _beruehrt = null;
        foreach (var z in alle)
        {
            z.Ablagezustand = Ablagewirkung.Keine;
            z.AblageText = "";
        }
        foreach (var k in _karten.Values) k.Gezogen = false;
        VorschauVerbergen(null, sofort: false);
        NachDemZiehen();
    }
}

/// <summary>
/// Ziehen auf dem Brett (gong-wpf-dragdrop): Karten und, seit 0.11.1, Personen aus der Kanalliste (<see cref="KanalPerson"/>), als
/// Verschieben. Die gezogene Karte ist <see cref="BrettKarte.Gezogen"/> (in den Listen links und in der Kanalliste gedimmt), bis das Ziehen
/// endet oder abbricht.
/// </summary>
internal sealed class BrettZiehen(BrettViewModel brett) : GongSolutions.Wpf.DragDrop.IDragSource
{
    public void StartDrag(GongSolutions.Wpf.DragDrop.IDragInfo dragInfo)
    {
        ArgumentNullException.ThrowIfNull(dragInfo);
        dragInfo.Data = dragInfo.SourceItem;
        dragInfo.Effects = System.Windows.DragDropEffects.Move;
        if (dragInfo.SourceItem is BrettKarte karte) karte.Gezogen = true;
        if (dragInfo.SourceItem is KanalPerson person) person.Karte.Gezogen = true;
    }

    /// <summary>
    /// Karten und die Mini-Kacheln von Zusatzkanälen; eine Kachel aus der Rolle nicht (Sarevoc Q116, 12.10.2026: "schlicht nicht ziehbar, der
    /// Hilfetext sagt es", kein roter Satz; bis zur Prüfung 0.11.1 ließ sie sich ziehen und antwortete links rot).
    /// </summary>
    public bool CanStartDrag(GongSolutions.Wpf.DragDrop.IDragInfo dragInfo) => dragInfo?.SourceItem is BrettKarte or KanalPerson && brett.Sichtbar;

    public void Dropped(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
    {
    }

    public void DragDropOperationFinished(System.Windows.DragDropEffects operationResult, GongSolutions.Wpf.DragDrop.IDragInfo dragInfo) => brett.AblagezustaendeLeeren();

    public void DragCancelled() => brett.AblagezustaendeLeeren();

    public bool TryCatchOccurredException(Exception exception) => false;
}
