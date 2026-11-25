// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Klang.Maschine;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Das Funkfeld (A24): 5 oder 10 Kacheln, das Sonderfeld Global immer rechts außen (A23). Wählt die Sendenetze, schaltet
/// Hören je Feld (A17) und stellt die Sprachaktivierung der Maschine nach der Auslösung der gewählten Netze (A10).
/// Lautstärke und Panorama je Netz (A9) gehen an den <see cref="INetzKlang"/>, sobald ein Netz in einer Kachel steht
/// oder sich ein Wert ändert; von dort kommt auch der Empfangspegel je Netz (A18).
/// </summary>
public sealed partial class FunkfeldViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Mindestabstand der Pegelmeldungen im Funkfeld: höchstens 15 je Sekunde. Bei einem genauen Bildtakt von 33 ms trifft
    /// das jeden zweiten Takt; mit 67 war es nur jeder dritte. Gemessen (Gegenprüfung 30.09.2026, ohne Fenster) tickt der
    /// DispatcherTimer mit 33 ms aber etwa alle 47 ms, solange der Prozess die Zeitgeberauflösung von 15,6 ms nicht anhebt,
    /// und <see cref="Environment.TickCount64"/> zählt im selben Raster: Dann kommt jede zweite Meldung durch, etwa 11 je
    /// Sekunde, mit 66 wie mit 67.
    /// </summary>
    public const long PegelAbstandMs = 66;

    /// <summary>Stufen des Pegels auf den Kacheln: Achtel, also 7,5 dB je Stufe zwischen -60 und 0 dBFS.</summary>
    public const int PegelStufen = 8;

    /// <summary>Wie lange ein Empfangspegel als aktuell gilt.</summary>
    private const long EmpfangGueltigMs = 400;

    private readonly IFunkDienst _dienst;
    private readonly IKlangMaschine _maschine;
    private readonly PegelVerlauf _verlauf;
    private readonly IOberflaechentakt _takt;
    private readonly EmpfangsPegelTafel _empfang = new();
    private readonly Func<long> _uhr;
    private readonly INetzKlang? _netzKlang;
    private readonly IEmpfangsPegelQuelle? _pegelQuelle;
    private readonly Dictionary<ushort, (float Db, float Panorama)> _netzGemeldet = [];
    private long _pegelZuletzt = long.MinValue / 2;
    private bool _eigenZuletzt;
    private bool _gelbZuletzt;

    /// <param name="netzKlang">Lautstärke, Panorama und Empfangspegel je Netz; ohne ihn liefert eine Maschine, die
    /// <see cref="IEmpfangsPegelQuelle"/> kann, nur den Pegel.</param>
    /// <param name="jetzt">Die eigene Uhr für Stummschaltungen der Leitung (Klangstufe C2b); ohne Angabe die des Systems.</param>
    public FunkfeldViewModel(IFunkDienst dienst, IKlangMaschine maschine, PegelVerlauf verlauf, Einstellungsablage ablage, IOberflaechentakt takt,
        Func<long>? uhr = null, INetzKlang? netzKlang = null, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(verlauf);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _maschine = maschine;
        _verlauf = verlauf;
        Ablage = ablage;
        _takt = takt;
        _uhr = uhr ?? (() => Environment.TickCount64);
        _netzKlang = netzKlang;
        _pegelQuelle = netzKlang ?? maschine as IEmpfangsPegelQuelle;
        // Klangstufe C2b: das gelbe "!" und der Mikro-Knopf; ihre Änderungen ziehen die Kacheln nach (Zeichen an Sprechern, rote Punkte).
        Dauersend = new DauersendViewModel(maschine, dienst, takt);
        Mikro = new MikroViewModel(maschine, dienst, takt, jetzt);
        Dauersend.Geaendert += Aktualisieren;
        Mikro.Geaendert += Aktualisieren;
        _dienst.LageGeaendert += LageGeaendert;
        if (_pegelQuelle is not null) _pegelQuelle.NetzPegel += EmpfangsPegel;
        KachelnAufbauen();
        Aktualisieren();
    }

    /// <summary>
    /// Die globalen Netztasten (Raw Input) sind belegt; dann schaltet das Funkfeld + und - nicht selbst, sonst zählte ein
    /// Druck doppelt, wenn es den Fokus hat.
    /// </summary>
    public bool NetztastenGlobal { get; set; }

    /// <summary>
    /// Das Funkfeld liegt im Infobereich (RAID-S2-ENTWURF Abschnitt 5): Pegel und Kacheln rechnet der Bildtakt dann nicht, denn das
    /// geschichtete Fenster zeichnet WPF in Software und überträgt bei jeder Änderung das ganze Bild.
    /// </summary>
    public bool Abgelegt { get; set; }

    /// <summary>Tooltip des X in der Titelzeile: Es legt ab und sagt, womit das Funkfeld zurückkommt (die belegte Taste, sonst das Symbol).</summary>
    [ObservableProperty]
    public partial string AblegenTooltip { get; set; } = FunkfeldAblage.AblegenTooltip([]);

    /// <summary>
    /// Die Melde-Taste ist gehalten (A4): Die Sprachaktivierung bleibt aus, bis sie los ist, auch wenn die Lage sich ändert. Wird aus dem
    /// Thread der Tastenquelle gesetzt.
    /// </summary>
    public bool SprachaktivierungRuht
    {
        get => Volatile.Read(ref _sprachaktivierungRuht);
        set
        {
            Volatile.Write(ref _sprachaktivierungRuht, value);
            // ui-fix 0.6.2: Die Melde-Taste spricht ins Meldenetz, auch wenn die Sendeauswahl auf Ruhe steht; beim Loslassen rechnet
            // AusloesungGeaendert das Ziel neu.
            if (value) _verlauf.Weg = Sendeweg.Offen;
        }
    }

    private bool _sprachaktivierungRuht;

    public ObservableCollection<KachelViewModel> Kacheln { get; } = [];

    /// <summary>
    /// Das gelbe "!" der Auto Dauersend-Dämpfung (Klangstufe C2b): das eigene Urteil vor der eigenen Sendeanzeige in der Titelzeile, das Zeichen an
    /// Sprechern auf den Kacheln; dasselbe Exemplar für "Wer ist da" (HauptViewModel).
    /// </summary>
    public DauersendViewModel Dauersend { get; }

    /// <summary>Mikro stumm (Klangstufe C2b, Q31 bis Q36): der Knopf neben dem Regler in der Titelzeile, auch stumm durch die Einsatzleitung.</summary>
    public MikroViewModel Mikro { get; }

    /// <summary>Die Meldezeile für das eigene Urteil, die Taste "Dauersender stumm" und "Mikro ist stumm" (App.OnStartup).</summary>
    public Meldezeile? Meldezeile
    {
        get => Dauersend.Meldezeile;
        set
        {
            Dauersend.Meldezeile = value;
            Mikro.Meldezeile = value;
        }
    }

    /// <summary>"Zeichen im Funkfeld" wurde umgeschaltet (A49): Kacheln neu aus der Lage.</summary>
    public void ZeichenGeaendert() => Aktualisieren();

    internal Einstellungsablage Ablage { get; }

    internal ClientEinstellungen Einstellungen => Ablage.Aktuell;

    /// <summary>5 oder 10 Kacheln, Global eingerechnet.</summary>
    public int Ansicht
    {
        get => Einstellungen.Funkfeld.Ansicht;
        set
        {
            var neu = value >= 10 ? 10 : 5;
            if (neu == Einstellungen.Funkfeld.Ansicht) return;
            Einstellungen.Funkfeld.Ansicht = neu;
            Ablage.Vormerken();
            KachelnAufbauen();
            Aktualisieren();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IstZehner));
        }
    }

    public bool IstZehner
    {
        get => Ansicht == 10;
        set => Ansicht = value ? 10 : 5;
    }

    /// <summary>
    /// A Raster (Voreinstellung) oder C Liste (Sarevoc, 29.09.2026: kompakt, weil es als Overlay läuft; A29 nach den
    /// Vergleichsbildern, die Leiste ist entfallen); gemerkt.
    /// </summary>
    public FunkfeldDarstellung Darstellung
    {
        get => Einstellungen.Funkfeld.Darstellung;
        set
        {
            if (!Enum.IsDefined(value) || value == Einstellungen.Funkfeld.Darstellung) return;
            Einstellungen.Funkfeld.Darstellung = value;
            Ablage.Vormerken();
            // Die Liste zeigt den Regler erst nach einem Klick auf sein Symbol; nach dem Wechsel beginnt sie zugeklappt.
            ReglerOffen = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IstRaster));
            OnPropertyChanged(nameof(IstListe));
            ReglerAnsichtMelden();
        }
    }

    public bool IstRaster
    {
        get => Darstellung == FunkfeldDarstellung.Raster;
        set
        {
            if (value) Darstellung = FunkfeldDarstellung.Raster;
        }
    }

    public bool IstListe
    {
        get => Darstellung == FunkfeldDarstellung.Liste;
        set
        {
            if (value) Darstellung = FunkfeldDarstellung.Liste;
        }
    }

    /// <summary>
    /// Durchsichtigkeit in Prozent, 0 bis <see cref="FunkfeldEinstellungen.DurchsichtigkeitMax"/>; gemerkt je Nutzer, wirkt
    /// sofort und stufenlos, auch während der Regler gezogen wird (das Funkfeld ist immer geschichtet und öffnet sich dafür
    /// nicht neu). Zwei Regler stellen sie: unter Einstellungen und in der Titelzeile des Funkfelds (<see cref="ReglerWert"/>).
    /// </summary>
    public int Durchsichtigkeit
    {
        get => Einstellungen.Funkfeld.Durchsichtigkeit;
        set
        {
            var neu = Math.Clamp(value, 0, FunkfeldEinstellungen.DurchsichtigkeitMax);
            if (neu == Einstellungen.Funkfeld.Durchsichtigkeit) return;
            Einstellungen.Funkfeld.Durchsichtigkeit = neu;
            Ablage.Vormerken();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Durchsicht));
            OnPropertyChanged(nameof(Deckung));
            ReglerWertMelden();
        }
    }

    /// <summary>
    /// Der Regler in der Titelzeile des Funkfelds (A30, Sarevoc: "links neben der Windows-Leiste"), 0 bis 85 %: dieselbe
    /// Einstellung wie <see cref="Durchsichtigkeit"/>, beide Regler ziehen sich nach. Jeder Wert wirkt sofort, auch beim
    /// Ziehen über 0 hinweg (A33, Sarevoc: "beim Schieben ... blendet das Fenster aus, das soll so nicht sein"): Das Funkfeld ist
    /// immer geschichtet, es gibt kein Neuöffnen mehr, bei dem der Regler mit seinem Fenster verschwände.
    /// </summary>
    public double ReglerWert
    {
        get => Durchsichtigkeit;
        set => Durchsichtigkeit = double.IsFinite(value) ? (int)Math.Round(value) : 0;
    }

    /// <summary>Wertanzeige neben dem Regler.</summary>
    public string ReglerText => Durchsichtigkeit.ToString(CultureInfo.CurrentCulture) + " %";

    /// <summary>Tooltip des Reglers in der Titelzeile.</summary>
    public string ReglerHinweis =>
        $"Durchsichtigkeit des Funkfelds: {ReglerText}, dieselbe wie unter Einstellungen. Pfeiltasten 5 %, Bild auf und ab 10 %.";

    private void ReglerWertMelden()
    {
        OnPropertyChanged(nameof(ReglerWert));
        OnPropertyChanged(nameof(ReglerText));
        OnPropertyChanged(nameof(ReglerHinweis));
    }

    /// <summary>
    /// Nur in der Liste: Der Regler ist über sein Symbol aufgeklappt und nimmt dann die Breite der Sendenetze und der
    /// Ansichtsknöpfe ein. In 240 Einheiten Breite hätte er neben ihnen dauerhaft nur etwa 35 Einheiten, und "Senden: Crew"
    /// würde gekürzt; die Liste soll aber nicht breiter werden. Im Raster steht er immer da. Nicht gemerkt, aber über einen
    /// Neuaufbau des Fensters für eine andere Farbstellung hinweg erhalten (das Modell bleibt, das Fenster wechselt).
    /// </summary>
    public bool ReglerOffen
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            ReglerAnsichtMelden();
        }
    }

    /// <summary>Der Regler ist zu sehen: im Raster immer, in der Liste aufgeklappt.</summary>
    public bool ReglerSichtbar => IstRaster || ReglerOffen;

    /// <summary>Liste mit aufgeklapptem Regler: Er füllt die Titelzeile bis zu seinem Symbol, Sendenetze und Ansichten ruhen.</summary>
    public bool ReglerBreit => IstListe && ReglerOffen;

    private void ReglerAnsichtMelden()
    {
        OnPropertyChanged(nameof(ReglerSichtbar));
        OnPropertyChanged(nameof(ReglerBreit));
    }

    /// <summary>
    /// Die Option "Schrift und Symbole deckend halten" (ab Werk aus): Wahr, dann werden nur Lücken und Titelzeile
    /// durchsichtig, und Kacheln, Schrift und Zustandsfarben bleiben über hellem Spielbild lesbar. Falsch, der Hauptweg
    /// (Sarevoc, 30.09.2026): Das ganze Funkfeld wird gleichmäßig durchsichtig; wie weit, ist deine Wahl.
    /// </summary>
    public bool SchriftDeckend
    {
        get => Einstellungen.Funkfeld.SchriftDeckend;
        set
        {
            if (value == Einstellungen.Funkfeld.SchriftDeckend) return;
            Einstellungen.Funkfeld.SchriftDeckend = value;
            Ablage.Vormerken();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Durchsicht));
            OnPropertyChanged(nameof(Deckung));
        }
    }

    /// <summary>Deckkraft von Lücken, Titelzeile und Fenster nach <see cref="Durchsichtigkeit"/> und <see cref="SchriftDeckend"/>.</summary>
    public Durchsicht Durchsicht => Durchsicht.Aus(Durchsichtigkeit, SchriftDeckend);

    /// <summary>Deckkraft jeder Fläche und des ganzen Fensters (<see cref="FunkfeldDeckung"/>).</summary>
    public FunkfeldDeckung Deckung => FunkfeldDeckung.Berechnen(Durchsicht, Farbvorschlaege.Von(Einstellungen.Farbstellung));

    /// <summary>
    /// Das Fenster soll seinen Platz neu anwenden, ohne dass sich Ansicht oder Feldzahl geändert haben (Größe vergessen).
    /// </summary>
    public event Action? PlatzNeu;

    /// <summary>
    /// "Größe nach Inhalt" unter Einstellungen: vergisst die selbst gezogenen Größen aller Ansichten; das Funkfeld nimmt
    /// wieder die optimale Größe, gerade so, dass alles angezeigt wird. Die Lage bleibt.
    /// </summary>
    public void GroesseNachInhalt()
    {
        if (Einstellungen.Funkfeld.GroessenVergessen()) Ablage.Vormerken();
        PlatzNeu?.Invoke();
    }


    /// <summary>
    /// Lautstärke oder Auslösung eines Netzes hat sich geändert, hier oder unter Einstellungen > Empfangen (A9, A10; bis 0.9.3 im Bereich Netze). Die Kacheln ziehen
    /// schon selbst nach; der Bereich Netze hört hier mit.
    /// </summary>
    public event Action<ushort>? NetzGeaendert;

    /// <summary>Letzte Ablehnung oder Fehlermeldung.</summary>
    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    /// <summary>Kurzer Stand für die Titelleiste des Funkfelds.</summary>
    [ObservableProperty]
    public partial string Kopfzeile { get; private set; } = "";

    [ObservableProperty]
    public partial bool Verbunden { get; private set; }

    [RelayCommand]
    private void AnsichtWechseln() => Ansicht = Ansicht == 10 ? 5 : 10;

    /// <summary>Taste 1-9 wählt das Sendenetz im Feld, 0 Global.</summary>
    public Task TasteAsync(int ziffer)
    {
        if (ziffer is < 0 or > 9) return Task.CompletedTask;
        var k = ziffer == 0 ? Kacheln.LastOrDefault() : Kacheln.FirstOrDefault(x => !x.IstGlobal && x.Platz == ziffer - 1);
        return k is null ? Task.CompletedTask : SendenWaehlenAsync(k, hinzufuegen: false);
    }

    /// <summary>A7: Sendenetz weiterschalten (+1) oder zurück (-1), wie Numpad + und -.</summary>
    public Task WeiterschaltenAsync(int richtung)
    {
        var kandidaten = Kacheln.Where(k => k.KannSenden && !k.IstLeer).ToList();
        if (kandidaten.Count == 0) return Task.CompletedTask;
        var aktuell = kandidaten.FindIndex(k => k.IstSenden);
        var naechste = aktuell < 0 ? (richtung >= 0 ? 0 : kandidaten.Count - 1) : (aktuell + Math.Sign(richtung) + kandidaten.Count) % kandidaten.Count;
        return SendenWaehlenAsync(kandidaten[naechste], hinzufuegen: false);
    }

    /// <summary>
    /// Wählt <paramref name="kachel"/> als Sendenetz. Ohne <paramref name="hinzufuegen"/> ersetzt die Wahl die bisherige;
    /// mit (Strg+Klick) kommt sie hinzu oder fällt heraus, sofern das Konto mehrfach senden darf (A7).
    /// </summary>
    public async Task SendenWaehlenAsync(KachelViewModel kachel, bool hinzufuegen)
    {
        ArgumentNullException.ThrowIfNull(kachel);
        var lage = _dienst.Lage;
        if (kachel.IstLeer) return;
        if (!kachel.KannSenden)
        {
            Hinweis = kachel.Zustand switch
            {
                KachelZustand.Gesperrt => kachel.KanalText + ": gesperrt, hier sendest du nicht. " + kachel.GesperrtText + ".",
                KachelZustand.Ruhe => kachel.Name + " steht auf Ruhe: erst einen Ch. wählen.",
                _ => kachel.Name + ": hier darfst du nicht senden.",
            };
            return;
        }
        // Mehrfachsenden (Stufe 3, gleichzeitig in mehrere Kanäle): mit dem Schalter nimmt jeder Klick dazu oder heraus,
        // Strg+Klick bleibt das Kürzel dafür.
        var dazu = hinzufuegen || (MehrfachAn && MehrfachSichtbar);
        var mehrfach = dazu && lage.Konto?.Hat(Recht.MehrfachSenden) == true;
        var felder = new SortedSet<byte>(lage.SendeFelder);
        var global = lage.GlobalSenden;
        var platz = kachel.IstGlobal ? -1 : kachel.Platz;
        if (mehrfach)
        {
            var heraus = kachel.IstGlobal ? global : felder.Contains((byte)kachel.Platz);
            if (kachel.IstGlobal) global = !global;
            else if (!felder.Remove((byte)kachel.Platz)) felder.Add((byte)kachel.Platz);
            _zuletztGewaehlt.Remove(platz);
            if (!heraus) _zuletztGewaehlt.Add(platz);
        }
        else
        {
            felder.Clear();
            global = kachel.IstGlobal;
            if (!kachel.IstGlobal) felder.Add((byte)kachel.Platz);
            _zuletztGewaehlt.Clear();
            _zuletztGewaehlt.Add(platz);
        }
        Hinweis = dazu && !mehrfach ? "Mehrere Sendekanäle gleichzeitig braucht das Recht dazu." : null;
        await AuswahlSetzenAsync(felder, global).ConfigureAwait(true);
    }

    /// <summary>Die Sendeauswahl setzen: lokal sofort (C1), dann die Antwort des Servers.</summary>
    private async Task AuswahlSetzenAsync(IEnumerable<byte> felder, bool global)
    {
        var anfrage = _dienst.SendeauswahlSetzenAsync([.. felder], global);
        // Die Auswahl gilt lokal sofort (C1): Kacheln und Sprachaktivierung (A10) gleich nachziehen, nicht erst nach der
        // Antwort des Servers. Sonst zählte ein schnelles zweites Numpad + vom alten Stand aus, und die Maschine hörte
        // bis zur Antwort noch auf die Sprachaktivierung des alten Netzes.
        Aktualisieren();
        var fehler = await anfrage.ConfigureAwait(true);
        if (fehler is not null) Hinweis = fehler;
        Aktualisieren();
    }

    public async Task HoerenSetzenAsync(KachelViewModel kachel, bool hoeren)
    {
        ArgumentNullException.ThrowIfNull(kachel);
        if (!kachel.KannAusschalten || kachel.NetzId is not { } id) return;
        var fehler = await _dienst.HoerenSetzenAsync(id, hoeren).ConfigureAwait(true);
        Hinweis = fehler;
        Aktualisieren();
    }

    /// <summary>Klangmodul Sprachaktivierung (0.9.0, Q48): aus heißt, jedes Feld sendet nur mit Taste.</summary>
    internal bool SprachaktivierungModulAn => Einstellungen.Audio.Sprachaktivierung;

    /// <summary>Das Klangmodul Sprachaktivierung wurde in Audio geschaltet: Kacheln neu lesen, Auslösung neu rechnen.</summary>
    internal void SprachaktivierungModulGeaendert()
    {
        foreach (var k in Kacheln) k.ModulGeaendert();
        // Auch die Kanäle unter Einstellungen > Empfangen lesen den Schalter (NetzRegelung; bis 0.9.3 die Seite Meine Kanäle).
        foreach (var id in Kacheln.Select(k => k.NetzId).OfType<ushort>().Distinct().ToList()) NetzGeaendert?.Invoke(id);
        AusloesungGeaendert();
    }

    /// <summary>
    /// A10: Sprachaktivierung nur, wenn alle gewählten Sendenetze auf VA stehen; eines auf Taste genügt für Taste. Ist das Klangmodul
    /// Sprachaktivierung aus, gilt überall Taste (die Wahl je Kanal bleibt gemerkt).
    /// </summary>
    internal void AusloesungGeaendert()
    {
        var gewaehlt = Kacheln.Where(k => k.IstSenden).ToList();
        var modul = SprachaktivierungModulAn;
        _maschine.Sprachaktivierung = modul && !SprachaktivierungRuht && gewaehlt.Count > 0 && gewaehlt.All(k => k.Sprachaktivierung);
        SprachaktivierungBenutzt = modul && Kacheln.Any(k => !k.IstLeer && k.KannSenden && k.Sprachaktivierung);
        _verlauf.Weg = WegNachLage(gewaehlt);
    }

    /// <summary>
    /// ui-fix 0.6.2 (Grün-Fehler): Wohin die eigene Stimme ginge. Auf Ruhe (kein gewählter Kanal oder nur Ruhe), ohne Verbindung oder von der
    /// Einsatzleitung in allen gewählten Kanälen stummgeschaltet geht nichts hinaus; dann wird nirgends grün, und wer spricht, liest es in Wörtern.
    /// Seit 0.10.0 auch, wenn ein gewählter Kanal die Sprechtaste braucht (die Sprachaktivierung der Maschine ist aus) und keine belegt ist
    /// (<see cref="Sendeweg.OhneSprechtaste"/>, Statuszeile "Kanal braucht die Sprechtaste - keine belegt").
    /// </summary>
    private Sendeweg WegNachLage(List<KachelViewModel> gewaehlt)
    {
        if (SprachaktivierungRuht) return Sendeweg.Offen;
        if (!_dienst.Lage.IstAktiv) return Sendeweg.Getrennt;
        if (gewaehlt.Count == 0) return Sendeweg.Ruhe;
        if (Mikro.LeitungAlle || gewaehlt.All(k => k.StummGeschaltet)) return Sendeweg.Leitung;
        if (!_maschine.Sprachaktivierung && !SprechtasteBelegt) return Sendeweg.OhneSprechtaste;
        return Sendeweg.Offen;
    }

    /// <summary>
    /// Mindestens eine Sprechtaste ist belegt (seit 0.10.0, für <see cref="Sendeweg.OhneSprechtaste"/>). Das gilt auch, wenn "Senden mit Stimme"
    /// aus ist: Dann sendet jeder Kanal nur mit der Taste, und eine belegte genügt.
    /// </summary>
    internal bool SprechtasteBelegt => Einstellungen.Tasten.Zuordnungen().Any(z => z.Funktion == Funk.Klang.Eingabe.Tastenfunktion.Sprechen);

    /// <summary>Q41: Es geht gerade etwas hinaus; "Senden: ..." in der Titelzeile steht dann im Grün der Farbstellung.</summary>
    [ObservableProperty]
    public partial bool SendetGerade { get; private set; }

    /// <summary>Meldezeile: Schlüssel des Hinweises "Ruhe: es wird nichts gesendet" (bis 0.11.1; seit 0.11.2 nicht mehr über dem Spiel).</summary>
    public const string HinweisSchluessel = "nichts-gesendet";

    /// <summary>
    /// Die Sprachaktivierung wird benutzt (Stufe 2c, Unterpunkt unter Einstellungen, bis 0.9.3 unter Audio): Mindestens ein Feld trägt einen Kanal, auf dem du
    /// senden darfst und der auf Sprachaktivierung steht. Nur dann kann die Maschine sie je einschalten (A10: das Sendenetz wählt
    /// man über die Felder, und sie gilt nur, wenn alle gewählten auf VA stehen). Einen Modus "nur Sprechtaste" hat das Gate
    /// nicht; Fest, Automatisch und Lernend regeln nur, wann es öffnet. Das kurze Ruhen beim Melden zählt nicht, sonst
    /// verschwände der Unterpunkt bei jedem Druck auf die Melde-Taste.
    /// </summary>
    [ObservableProperty]
    public partial bool SprachaktivierungBenutzt { get; private set; }

    internal bool SprachaktivierungFuer(ushort? netzId, bool nurTaste, bool vaStandard)
    {
        if (netzId is not { } id || nurTaste) return false;
        return Einstellungen.NetzOderNull(id)?.Ausloesung switch
        {
            Ausloesung.Sprachaktivierung => true,
            Ausloesung.Sprechtaste => false,
            _ => vaStandard,
        };
    }

    /// <summary>
    /// Das Netz erlaubt nur die Sprechtaste; Global ohne eigene Angabe auch (A20, wie auf der Kachel). Bei Global mit Ch. gilt das nur
    /// für Ch. 1, Global selbst: die übrigen Ch. von Global erlauben Sprachaktivierung (6.5).
    /// </summary>
    internal bool NurTasteVon(ushort netzId)
    {
        var lage = _dienst.Lage;
        return lage.NetzVon(netzId) is not { } n || (n.NurTaste && !(n.IstGlobal && lage.ChVon(netzId) is not null));
    }

    /// <summary>A10 für ein Netz nach der Lage, ob es in einer Kachel steht oder nicht (Bereich Netze).</summary>
    internal bool SprachaktivierungVon(ushort netzId)
    {
        var n = _dienst.Lage.NetzVon(netzId);
        return SprachaktivierungFuer(netzId, NurTasteVon(netzId), n is { IstGlobal: false, SprachaktivierungStandard: true });
    }

    /// <summary>A10: Sprachaktivierung oder Sprechtaste für ein Netz setzen, gemerkt je Netz; nicht bei "nur Taste".</summary>
    internal void SprachaktivierungSetzen(ushort netzId, bool an)
    {
        if (NurTasteVon(netzId)) return;
        Einstellungen.NetzFuer(netzId).Ausloesung = an ? Ausloesung.Sprachaktivierung : Ausloesung.Sprechtaste;
        Ablage.Vormerken();
        NetzMelden(netzId);
        AusloesungGeaendert();
    }

    /// <summary>A9: Lautstärke eines Netzes in dB.</summary>
    internal double LautstaerkeVon(ushort netzId) => Einstellungen.NetzOderNull(netzId)?.LautstaerkeDb ?? 0;

    /// <summary>
    /// A9: Lautstärke setzen, auf 0,5 dB gerundet und begrenzt, gemerkt je Netz und sofort an den Klang. Was keine Zahl ist
    /// (NaN), lässt der Regler und die Einstellung unberührt: Ein Wert ohne Platz auf der Bahn würde gemerkt und beim nächsten
    /// Start als Knauf ins Leere laufen.
    /// </summary>
    internal void LautstaerkeSetzen(ushort netzId, double db)
    {
        if (double.IsNaN(db)) return;
        var n = Einstellungen.NetzFuer(netzId);
        var neu = (float)Math.Clamp(Math.Round(db * 2) / 2, NetzEinstellung.LautstaerkeMinDb, NetzEinstellung.LautstaerkeMaxDb);
        if (Math.Abs(n.LautstaerkeDb - neu) < 0.01f) return;
        n.LautstaerkeDb = neu;
        Ablage.Vormerken();
        NetzKlangAnwenden(netzId);
        NetzMelden(netzId);
    }

    /// <summary>Anzeige einer Lautstärke, etwa "-3,5 dB".</summary>
    public static string LautstaerkeText(double db) => db.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB";

    /// <summary>
    /// Eine Einstellung des Netzes hat sich geändert: Seit 0.6.0 gilt sie für alle Ch. eines Kanalnetzes (Sarevoc Q15), also für jede Kachel
    /// mit demselben Schlüssel, und der Klang bekommt sie für jeden dieser Kanäle.
    /// </summary>
    private void NetzMelden(ushort netzId)
    {
        var schluessel = ClientEinstellungen.Schluessel(netzId);
        foreach (var k in Kacheln)
        {
            if (k.NetzId is not { } id || ClientEinstellungen.Schluessel(id) != schluessel) continue;
            k.NetzEinstellungNeu();
            NetzKlangAnwenden(id);
        }
        NetzGeaendert?.Invoke(netzId);
    }

    /// <summary>Lautstärke, Ohr oder Auslösung wurden anderswo geändert (Bereich Netze): Kacheln und Klang ziehen für alle Ch. nach.</summary>
    internal void NetzEinstellungGeaendert(ushort netzId) => NetzMelden(netzId);

    /// <summary>
    /// Bildtakt (etwa 30 je Sekunde): eigener Sendepegel und Empfangspegel in die Kacheln, aber höchstens alle
    /// <see cref="PegelAbstandMs"/> und in <see cref="PegelStufen"/> Stufen. Jede Änderung auf einer Kachel lässt ein
    /// geschichtetes Fenster sein ganzes Bild neu übertragen, neben Star Citizen; so ändern sich die Kacheln seltener.
    /// Beginnt oder endet die eigene Sendung, zeigt der nächste Takt es sofort; ebenso, wenn sie gesendet hätte, aber nichts hinausgeht (Q43, gelb
    /// auf den Feldern der Sendeauswahl, auch auf Ch. Ruhe).
    /// </summary>
    public void Takt()
    {
        var jetzt = _uhr();
        VormerkungenPruefen(jetzt, alle: false);
        // Eine Stummschaltung der Leitung endet zur Zeit des Servers; auch abgelegt (die Meldezeile und der nächste Blick sollen stimmen).
        Mikro.Takt();
        var eigen = _verlauf.Letzte(out var m) && m.Sendet;
        // Q43: würde senden, geht aber nirgends hin (nie zusammen mit "sendet").
        var gelb = !eigen && m.WuerdeSenden;
        // ui-fix 0.6.2: auch abgelegt, denn dann zeigt nur die Meldezeile, ob etwas hinausgeht oder nichts (Q41).
        MeldezeileNachfuehren(eigen, m);
        if (Abgelegt) return;
        if (eigen == _eigenZuletzt && gelb == _gelbZuletzt && jetzt - _pegelZuletzt < PegelAbstandMs) return;
        _pegelZuletzt = jetzt;
        _eigenZuletzt = eigen;
        _gelbZuletzt = gelb;
        SendetGerade = eigen;
        var eigenAnteil = eigen || gelb ? Stufe(Anteil(m.PegelDbfs)) : 0;
        foreach (var k in Kacheln)
        {
            if (k.IstSenden && eigen)
            {
                k.LiveSetzen(true, eigenAnteil);
                continue;
            }
            if (k.Sendeziel && gelb)
            {
                k.LiveSetzen(false, eigenAnteil, wuerdeSenden: true);
                continue;
            }
            var anteil = 0.0;
            if (k.NetzId is { } id && _empfang.Lesen(id, jetzt, EmpfangGueltigMs, out var db)) anteil = Stufe(Anteil(db));
            else if (k.SprecherAktiv) anteil = 0.5;
            k.LiveSetzen(false, anteil);
        }
    }

    /// <summary>
    /// Q41 und ui-fix 0.6.2: In der Meldezeile steht grün, was gerade hinausgeht (<see cref="Modelle.Meldezeile.Sendet"/>), und wer spricht, ohne
    /// dass etwas hinausgeht (Ruhe, stumm durch die Einsatzleitung, nicht verbunden, seit 0.10.0 ein Kanal auf Taste ohne Sprechtaste), liest es
    /// dort in Wörtern; je neuem Anlass einmal. "Stimme unter der Mindest-Funklautstärke" (<see cref="PegelVerlauf.UnterMindestText"/>) steht nur in
    /// der Statuszeile und an der Grafik, nicht hier über dem Spiel (Befund der Prüfung): Die Mindest-Funklautstärke soll leises Reden im Raum
    /// draußen halten, und jedes Gespräch im Raum blendete den Satz sonst ein; die übrigen Sätze melden eine Fehlbedienung, dieser eine Automatik,
    /// die richtig arbeitet.
    /// </summary>
    private void MeldezeileNachfuehren(bool eigen, in PegelMeldung m)
    {
        if (Meldezeile is not { } zeile) return;
        zeile.Sendet = eigen;
        // 0.11.2 (Sarevoc 13.10.2026: "die Overlay-Meldung 'es wird nichts gesendet' will ich nicht ... das dauernde Spammen sofort raus"): Die
        // Sätze, dass nichts hinausgeht, stehen nur noch in der Statuszeile und am Pegelbalken, nicht mehr über dem Spiel.
        _ = m;
    }

    /// <summary>dBFS auf 0..1 für die Anzeige: -60 dBFS ist leer, 0 dBFS voll.</summary>
    public static double Anteil(double dbfs) => double.IsFinite(dbfs) ? Math.Clamp((dbfs + 60.0) / 60.0, 0.0, 1.0) : 0.0;

    /// <summary>Ein Anteil 0..1 auf die nächste der <see cref="PegelStufen"/> Stufen gerundet.</summary>
    public static double Stufe(double anteil) =>
        double.IsFinite(anteil) ? Math.Round(Math.Clamp(anteil, 0, 1) * PegelStufen, MidpointRounding.AwayFromZero) / PegelStufen : 0;

    // ---------------------------------------------------------------- Stufe 3: Rollenliste, Ch., Mehrfachsenden, Skalierung

    /// <summary>
    /// So lange Ruhe nach einer Wahl mit Mausrad oder Pfeilen, bevor sie hinausgeht (STUFE3-ENTWURF 5.1): Jeder Ch.-Wechsel ist am
    /// Server ein Commit mit einem Schreiben von lage.json (6.16, Last).
    /// </summary>
    public const long RuheMs = 400;

    /// <summary>Die Rollenliste oben links (Q1, Q2): selbst wählbare Rollen mit Nummer davor, nach Reihenfolge.</summary>
    public ObservableCollection<RollenEintrag> Rollen { get; } = [];

    /// <summary>
    /// Die gewählte Rolle; Setzen wählt eine Rolle mit 400 ms Ruhe vor dem Senden (Mausrad, Pfeile), ein Klick in der Liste sofort
    /// (<see cref="RolleWaehlenCommand"/>). Die Rolle legt nur Kanäle in die Felder, der Platz auf dem Brett bleibt (6.16).
    /// </summary>
    public RollenEintrag? Rolle
    {
        get => _rolleVorgemerkt?.Eintrag ?? Rollen.FirstOrDefault(r => r.Id == _dienst.Lage.Rolle);
        set
        {
            if (value is null || !RolleWaehlbar)
            {
                OnPropertyChanged();
                return;
            }
            _rolleVorgemerkt = (value, _uhr());
            OnPropertyChanged();
            OnPropertyChanged(nameof(RolleText));
            OnPropertyChanged(nameof(RolleName));
            OnPropertyChanged(nameof(RolleMarkiert));
        }
    }

    /// <summary>
    /// Was die aufgeklappte Rollenliste markiert und wovon Mausrad und Pfeile ausgehen: die gewählte Rolle, sonst die Rolle des eigenen
    /// Platzes, wenn sie in der Liste steht (seit 0.11.0, Befund der Prüfung: sonst sprang ein Ruck von "3 Gunner" auf den ersten Eintrag).
    /// Nur Anzeige; gewählt wird erst mit Klick, Enter oder Mausrad.
    /// </summary>
    public RollenEintrag? RolleMarkiert => Rolle ?? (_dienst.Lage.RolleId is { } id ? Rollen.FirstOrDefault(r => r.Id == id) : null);

    /// <summary>
    /// Was oben links steht: die gewählte Rolle, sonst seit 0.11.0 die Rolle des eigenen Platzes (etwa nach einer Einteilung), sonst
    /// "Rolle wählen"; ohne die Markierung "(geändert)".
    /// </summary>
    public string RolleName => Rolle?.Text ?? PlatzRolleText ?? "Rolle wählen";

    /// <summary>
    /// Die Rolle des eigenen Platzes (Zuordnung Punkt 3, seit 0.11.0): wie in der Rollenliste mit Nummer, sonst ihr Name; ohne Platz
    /// <c>null</c>. Nur Anzeige: Gewählt ist in der Liste weiter nur <see cref="Rolle"/>.
    /// </summary>
    public string? PlatzRolleText
    {
        get
        {
            var lage = _dienst.Lage;
            if (lage.RolleId is not { } id) return null;
            return Rollen.FirstOrDefault(r => r.Id == id)?.Text ?? lage.RolleVon(id)?.Name;
        }
    }

    /// <summary>
    /// Deine Felder weichen von denen der gewählten Rolle ab (<see cref="Abweichung"/>): Dahinter steht "(geändert)". Die Rolle noch
    /// einmal zu wählen stellt ihre Kanäle wieder her.
    /// </summary>
    [ObservableProperty]
    public partial bool RolleGeaendert { get; private set; }

    /// <summary>Die Rolle mit Markierung, "3 Kapitän (geändert)", für Zugriffsnamen und Tests; "Rolle wählen" ohne Rolle.</summary>
    public string RolleText => RolleName + (RolleGeaendert && Rolle is not null ? " (geändert)" : "");

    /// <summary>
    /// Ob deine Felder von denen der gewählten Rolle abweichen (Entwurf 5.1): ein Feld der Rolle liegt auf einem anderen Kanalnetz oder ist
    /// leer, auf dem Kanalnetz liegt nicht der feste Ch. der Rolle, oder du hast ein Feld, das die Rolle nicht kennt. Der Ch. selbst
    /// zählt nicht, wenn die Rolle keinen festen nennt: Den wählst du je Kanalnetz selbst. Ein festes Feld der Einsatzleitung zählt nicht
    /// als deine Änderung. Ohne gewählte Rolle gibt es nichts, wovon abzuweichen wäre.
    /// </summary>
    internal static bool Abweichung(FunkLage lage)
    {
        ArgumentNullException.ThrowIfNull(lage);
        if (lage.Rolle is not { } id || lage.RolleVon(id) is not { } rolle) return false;
        var einheit = lage.SchiffVon(lage.SchiffId);
        var erwartet = new Dictionary<byte, (ushort Netz, byte? Ch)>();
        foreach (var rf in lage.PhaseVon(lage.PhaseNr)?.FelderVon(rolle) ?? rolle.Felder)
        {
            ushort? n = rf.NetzId ?? (rf.Kennung is { } k && einheit is not null && einheit.Netze.TryGetValue(k, out var e) ? e : null);
            if (n is { } netz) erwartet[rf.Feld] = (netz, rf.Ch);
        }
        foreach (var f in lage.Felder)
        {
            if (!erwartet.TryGetValue(f.Feld, out var soll))
            {
                if (f.Fest) continue;
                return true;
            }
            var ist = Kanalrechnung.Zerlegen(f.NetzId, out var kanalnetz, out var ch) ? kanalnetz : f.NetzId;
            if (ist != soll.Netz) return true;
            if (soll.Ch is { } fest && (!Kanalrechnung.Zerlegen(f.NetzId, out _, out var istCh) || istCh != fest)) return true;
        }
        return erwartet.Keys.Any(feld => lage.FeldVon(feld) is null);
    }

    /// <summary>Die Rollenliste lässt sich bedienen: verbunden, der Server kennt Kanäle (ab 0.6.0) und es gibt selbst wählbare Rollen.</summary>
    [ObservableProperty]
    public partial bool RolleWaehlbar { get; private set; }

    /// <summary>Tooltip der Rollenliste: was sie tut, oder warum sie gerade nicht geht.</summary>
    [ObservableProperty]
    public partial string RolleHilfe { get; private set; } = "";

    /// <summary>Klick oder Enter in der Rollenliste: sofort.</summary>
    [RelayCommand]
    private void RolleWaehlen(RollenEintrag eintrag)
    {
        Rolle = eintrag;
        VormerkungenAbschicken();
    }

    /// <summary>Mausrad oder Pfeile an der zugeklappten Rollenliste: die nächste oder vorige Rolle, gesendet nach 400 ms Ruhe.</summary>
    [RelayCommand]
    private void RolleSchritt(int richtung)
    {
        if (!RolleWaehlbar || Rollen.Count == 0 || richtung == 0) return;
        var i = RolleMarkiert is { } r ? Rollen.IndexOf(r) : -1;
        var j = Math.Clamp(i + Math.Sign(richtung), 0, Rollen.Count - 1);
        if (j != i) Rolle = Rollen[j];
    }

    /// <summary>"Mehrere senden" ist zu sehen: nur mit dem Recht MEHRFACH_SENDEN (Führung, Einsatzleitung, Admin; nicht im Funker-Modus).</summary>
    [ObservableProperty]
    public partial bool MehrfachSichtbar { get; private set; }

    /// <summary>
    /// Mehrfachsenden (gleichzeitig in mehrere Kanäle, STUFE3-ENTWURF 5.4): An nimmt jeder Klick auf eine Kachel ihren Kanal zur
    /// Sendeauswahl dazu oder heraus; aus ersetzt er. Beim Ausschalten bleibt der zuletzt gewählte Kanal. Beim Start immer aus.
    /// </summary>
    public bool MehrfachAn
    {
        get;
        set
        {
            if (field == value || (value && !MehrfachSichtbar)) return;
            field = value;
            OnPropertyChanged();
            if (!value) _ = AufDenLetztenAsync();
        }
    }

    /// <summary>
    /// Skalierung des Funkfelds in Prozent (Sarevoc Q12), -30 bis +30, unter Einstellungen. Die Mindestgröße wächst und schrumpft mit; eine
    /// am Rand gezogene Größe gilt danach nicht mehr, die Lage bleibt.
    /// </summary>
    public int Skalierung
    {
        get => Einstellungen.Funkfeld.Skalierung;
        set
        {
            var neu = Math.Clamp(value, FunkfeldEinstellungen.SkalierungMin, FunkfeldEinstellungen.SkalierungMax);
            if (neu == Einstellungen.Funkfeld.Skalierung) return;
            Einstellungen.Funkfeld.Skalierung = neu;
            Einstellungen.Funkfeld.GroessenVergessen();
            Ablage.Vormerken();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Skala));
            PlatzNeu?.Invoke();
        }
    }

    /// <summary>Faktor der Skalierung: 0,7 bis 1,3.</summary>
    public double Skala => (100 + Skalierung) / 100.0;

    /// <summary>Alle vorgemerkten Wahlen (Rolle, Ch.) sofort hinaus, etwa wenn eine Liste per Klick zuklappt.</summary>
    public void VormerkungenAbschicken() => VormerkungenPruefen(_uhr(), alle: true);

    private readonly Dictionary<int, long> _chVorgemerkt = [];
    private (RollenEintrag Eintrag, long Um)? _rolleVorgemerkt;
    private readonly List<int> _zuletztGewaehlt = [];
    private bool _neueSitzung = true;
    private long _kontoVersionGesehen;

    /// <summary>Ein Ch. ist gewählt (Kachel): zeigen, nach <see cref="RuheMs"/> ohne weitere Wahl hinaus.</summary>
    internal void ChVormerken(KachelViewModel kachel, byte ch)
    {
        kachel.ChVormerkungSetzen(ch);
        _chVorgemerkt[kachel.Platz] = _uhr();
    }

    /// <summary>Die Kachel hat ihre Vormerkung verworfen (anderes Kanalnetz, gesperrt, fest).</summary>
    internal void ChVormerkungVerworfen(KachelViewModel kachel) => _chVorgemerkt.Remove(kachel.Platz);

    private void VormerkungenPruefen(long jetzt, bool alle)
    {
        if (_rolleVorgemerkt is { } r && (alle || jetzt - r.Um >= RuheMs))
        {
            _rolleVorgemerkt = null;
            _ = RolleSendenAsync(r.Eintrag.Id);
        }
        foreach (var (platz, um) in _chVorgemerkt.ToList())
        {
            if (!alle && jetzt - um < RuheMs) continue;
            _chVorgemerkt.Remove(platz);
            if (Kacheln.FirstOrDefault(k => !k.IstGlobal && k.Platz == platz) is { ChVorgemerkt: { } ch, Kanalnetz: { } netz } kachel)
                _ = ChSendenAsync(kachel, netz, ch);
        }
    }

    private async Task ChSendenAsync(KachelViewModel kachel, ushort kanalnetz, byte ch)
    {
        var fehler = await _dienst.FeldSetzenAsync((byte)kachel.Platz, Kanalrechnung.Id(kanalnetz, ch)).ConfigureAwait(true);
        // Seit 0.8.1 (Q46) merkt sich der Client den Ch. nicht mehr: Nach der Rollenwahl beginnt jeder Ch. auf Ruhe.
        Hinweis = fehler;
        kachel.ChVorgemerktLoeschen();
        Aktualisieren();
    }

    /// <summary>
    /// RolleWaehlen ohne Wünsche (seit 0.8.1, Q46: jeder Ch. beginnt auf Ruhe, der Funker wechselt selbst); gemerkt wird die Rolle, wenn
    /// der Server sie annimmt.
    /// </summary>
    private async Task RolleSendenAsync(ushort rolleId)
    {
        var fehler = await _dienst.RolleWaehlenAsync(rolleId, []).ConfigureAwait(true);
        if (fehler is null)
        {
            Einstellungen.Funkfeld.Rolle = rolleId;
            Ablage.Vormerken();
        }
        Hinweis = fehler;
        Aktualisieren();
    }

    /// <summary>Mehrfachsenden aus: nur der zuletzt gewählte Kanal bleibt in der Sendeauswahl.</summary>
    private async Task AufDenLetztenAsync()
    {
        var lage = _dienst.Lage;
        if (lage.SendeFelder.Count + (lage.GlobalSenden ? 1 : 0) <= 1) return;
        var bleibt = _zuletztGewaehlt.LastOrDefault(p => p < 0 ? lage.GlobalSenden : lage.SendeFelder.Contains((byte)p), int.MinValue);
        if (bleibt == int.MinValue) bleibt = lage.SendeFelder.Count > 0 ? lage.SendeFelder.Min() : -1;
        _zuletztGewaehlt.Clear();
        _zuletztGewaehlt.Add(bleibt);
        await AuswahlSetzenAsync(bleibt < 0 ? [] : [(byte)bleibt], bleibt < 0).ConfigureAwait(true);
    }

    /// <summary>Die Rollenliste aus der Lage: selbst wählbare Rollen, Nummer davor (aus der Reihenfolge, wenn der Name keine trägt).</summary>
    private static List<RollenEintrag> RollenAus(FunkLage lage) =>
    [
        .. lage.Rollen.Where(r => r.SelbstWaehlbar).OrderBy(r => r.Reihenfolge).ThenBy(r => r.Id).Select(r => new RollenEintrag(r.Id,
            r.Name.Length > 0 && char.IsAsciiDigit(r.Name[0]) && r.Name.Contains(' ', StringComparison.Ordinal)
                ? r.Name
                : r.Reihenfolge.ToString(CultureInfo.InvariantCulture) + " " + r.Name,
            r.Netze.Count > 0 ? "Kanäle: " + string.Join(", ", r.Netze) : "Ohne Kanäle")),
    ];

    /// <summary>
    /// Rolle nach der Wiederverbindung (6.16, 6.9): Nach der Frist von 10 min setzt der Server beim Anmelden STANDARD und vergisst die
    /// gewählte Rolle; der Client stellt sie dann mit RolleWaehlen wieder her, einmal je Sitzung, nach dem Abgleich. Gemerkt wird nur,
    /// was der Server als deine gewählte Rolle meldet oder was du selbst wählst. Teilt dich jemand anders ein (Quelle nicht STANDARD,
    /// keine Rolle), bleibt die gemerkte Rolle seit 0.11.0 stehen: Die Einteilung gilt für den Abend, und ohne Einteilung kommt beim
    /// nächsten Login wieder deine Rolle (bis 0.10.0 wurde sie dabei vergessen).
    /// </summary>
    private void RolleBeobachten(FunkLage lage)
    {
        if (!lage.IstAktiv)
        {
            _neueSitzung = true;
            return;
        }
        if (!lage.Abgeglichen) return;
        var gemerkt = Einstellungen.Funkfeld.Rolle;
        if (_neueSitzung)
        {
            _neueSitzung = false;
            _kontoVersionGesehen = lage.KontoVersion;
            if (lage.Rolle is { } r)
            {
                RolleMerken(r);
                return;
            }
            if (lage.Quelle == MitgliedschaftsQuelle.Standard && gemerkt is { } g && lage.ServerKanaele
                && lage.RolleVon((ushort)g) is { SelbstWaehlbar: true } rolle)
            {
                _rolleVorgemerkt = null;
                _ = RolleSendenAsync(rolle.Id);
            }
            return;
        }
        if (lage.KontoVersion == _kontoVersionGesehen) return;
        _kontoVersionGesehen = lage.KontoVersion;
        if (lage.Rolle is { } neu) RolleMerken(neu);
    }

    private void RolleMerken(ushort rolle)
    {
        if (Einstellungen.Funkfeld.Rolle == rolle) return;
        Einstellungen.Funkfeld.Rolle = rolle;
        Ablage.Vormerken();
    }

    public void Dispose()
    {
        _dienst.LageGeaendert -= LageGeaendert;
        if (_pegelQuelle is not null) _pegelQuelle.NetzPegel -= EmpfangsPegel;
        Dauersend.Geaendert -= Aktualisieren;
        Mikro.Geaendert -= Aktualisieren;
        Dauersend.Dispose();
        Mikro.Dispose();
        // Ohne Funkfeld gilt wieder, was die Maschine meldet (ui-fix 0.6.2).
        _verlauf.Weg = Sendeweg.Offen;
    }

    /// <summary>A9: Lautstärke und Panorama eines Netzes aus den Einstellungen an den Klang, wenn sie sich geändert haben.</summary>
    internal void NetzKlangAnwenden(ushort netzId)
    {
        if (_netzKlang is null) return;
        var n = Einstellungen.NetzOderNull(netzId);
        var wert = (n?.LautstaerkeDb ?? 0f, n?.Panorama ?? 0f);
        if (_netzGemeldet.TryGetValue(netzId, out var alt) && alt == wert) return;
        _netzGemeldet[netzId] = wert;
        _netzKlang.NetzEinstellen(netzId, wert.Item1, wert.Item2);
    }

    /// <summary>Aus dem Thread der Maschine: ohne Sperre, ohne Allokation.</summary>
    private void EmpfangsPegel(ushort netzId, float db) => _empfang.Setzen(netzId, db, _uhr());

    private void LageGeaendert() => _takt.Einreihen(Aktualisieren);

    private void KachelnAufbauen()
    {
        Kacheln.Clear();
        for (var i = 0; i < Ansicht - 1; i++) Kacheln.Add(new KachelViewModel(this, i, istGlobal: false));
        Kacheln.Add(new KachelViewModel(this, Ansicht - 1, istGlobal: true));
    }

    internal void Aktualisieren()
    {
        var lage = _dienst.Lage;
        foreach (var k in Kacheln)
        {
            k.Aktualisieren(lage);
            if (k.NetzId is { } id) NetzKlangAnwenden(id);
        }
        Verbunden = lage.IstAktiv;
        var senden = Kacheln.Where(k => k.IstSenden).Select(k => k.KanalText).ToList();
        Kopfzeile = !lage.IstAktiv
            ? "nicht verbunden"
            : senden.Count == 0 ? "kein Sendekanal" : "Senden: " + string.Join(", ", senden);

        // Rollenliste (nur bei geändertem Inhalt ersetzen, sonst ginge eine offene Liste zu)
        var rollen = RollenAus(lage);
        if (!Rollen.SequenceEqual(rollen))
        {
            Rollen.Clear();
            foreach (var r in rollen) Rollen.Add(r);
        }
        RolleWaehlbar = lage.IstAktiv && lage.ServerKanaele && Rollen.Count > 0;
        RolleGeaendert = Abweichung(lage);
        RolleHilfe = !lage.IstAktiv ? "Nicht verbunden."
            : !lage.ServerKanaele ? "Der Server kennt die Rollenliste noch nicht (erst ab Fassung 0.6.0)."
            : Rollen.Count == 0 ? "Die Flotte hat keine Rolle, die du selbst wählen kannst."
            : "Rolle wählen: legt die Kanäle der Rolle in deine Felder, dein Platz bleibt. Mausrad oder Pfeile blättern." + (RolleGeaendert ? " Deine Felder weichen von der Rolle ab (geändert); sie noch einmal zu wählen stellt ihre Kanäle wieder her." : "")
                + (Rolle is null && lage.RolleId is not null && WerIstDaZeile.Platztext(lage, lage.SchiffId, lage.RolleId) is { } platz
                    ? " Dein Platz: " + platz + (lage.Quelle is MitgliedschaftsQuelle.Verschieben or MitgliedschaftsQuelle.Aufstellung ? " (eingeteilt)" : "")
                        + ". Eine eigene Rolle legt ihre Kanäle in deine Felder, der Platz bleibt."
                    : "");
        RolleBeobachten(lage);
        OnPropertyChanged(nameof(Rolle));
        OnPropertyChanged(nameof(RolleText));
        OnPropertyChanged(nameof(RolleName));
        OnPropertyChanged(nameof(RolleMarkiert));

        MehrfachSichtbar = lage.Konto?.Hat(Recht.MehrfachSenden) == true;
        if (!MehrfachSichtbar && MehrfachAn)
        {
            // Recht weg (Auftrag zu Ende, Funker-Modus): der Schalter geht aus; die Sendeauswahl prüft der Server ohnehin.
            MehrfachAn = false;
        }
        AusloesungGeaendert();
    }
}
