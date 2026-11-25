// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Client.Klang;

/// <summary>Ein Sprachpaket, wie es bei der Attrappe ankam (Tests).</summary>
public sealed record EmpfangenesSprachpaket(uint KontoId, int Netz, ushort Sprechphase, uint FrameNr, Sprachkennzeichen Kennzeichen, byte[] Opus, long AnkunftUs, bool Durchsage);

/// <summary>
/// Attrappe von <see cref="IKlangMaschine"/> für Entwurf, Vorführung und Tests. Sie öffnet kein Audiogerät. Mit
/// <c>vorfuehrung: true</c> erzeugt sie im 20-ms-Takt eine selbst gerechnete Geräuschszene (Sprache, Tastatur, Kinder im
/// Hintergrund) und rechnet die drei Gate-Modi grob nach, damit die Oberfläche etwas zu zeigen hat. Ohne Vorführung
/// meldet sie nur, was ein Test über <see cref="Melden"/> und die anderen Einspeisungen vorgibt, und zählt die Aufrufe.
/// </summary>
public sealed class KlangMaschinenAttrappe : IKlangMaschine, IEmpfangsPegelQuelle
{
    private readonly Lock _sperre = new();
    private readonly Vorfuehrungsszene? _szene;
    private readonly List<(uint KontoId, float Db)> _funkerPegel = [];
    private readonly Dictionary<int, float> _netzLautstaerke = [];
    private readonly Dictionary<int, float> _netzPanorama = [];
    private readonly List<EmpfangenesSprachpaket> _empfangen = [];
    private GateEinstellung _gate = new Ablage.GateWerte().AlsEinstellung();
    private ProbeStand _probe;
    private LautstaerkeEinstellung _lautstaerke = new(true, Pegelstufe.StartDb);
    private EinmessStand _einmessen;
    private TelemetrieStand? _telemetrie;
    private Timer? _takt;
    private long _startMs;
    private int _tickLaeuft;

    public KlangMaschinenAttrappe(bool vorfuehrung = false)
    {
        if (vorfuehrung) _szene = new Vorfuehrungsszene(7);
    }

    public event Action<PegelMeldung>? Pegel;

    /// <summary>Empfangspegel je Funknetz; die Vorführung speist ihn direkt ein (<see cref="EmpfangsPegelMelden"/>).</summary>
    public event Action<ushort, float>? NetzPegel;

    public event SprecherPegelAusgang? EmpfangsPegel;

    public event SendeAusgang? SprachpaketBereit;

    public event EventHandler<Exception>? Fehler;

    public string? EingangId { get; private set; }

    public string? AusgangId { get; private set; }

    public bool Laeuft { get; private set; }

    public int Starts { get; private set; }

    public int Stopps { get; private set; }

    public bool TasteGedrueckt { get; private set; }

    /// <summary>Wie oft <see cref="Gate"/> gesetzt wurde.</summary>
    public int GateGesetzt { get; private set; }

    /// <summary>Test: so meldet sich <see cref="Geraete"/> nach dem Start (etwa ein vom Datenschutz gesperrtes Mikrofon).</summary>
    public string? AufnahmeFehlerBeimStart { get; set; }

    public int Wiederholungen { get; private set; }

    public int VerlustHinweis { get; private set; }

    public long JetztUs { get; set; } = 1_000_000;

    public GeraeteZustand Geraete { get; private set; }

    public IReadOnlyList<(uint KontoId, float Db)> FunkerPegel
    {
        get
        {
            lock (_sperre) return [.. _funkerPegel];
        }
    }

    public IReadOnlyList<EmpfangenesSprachpaket> Empfangen
    {
        get
        {
            lock (_sperre) return [.. _empfangen];
        }
    }

    /// <summary>Lautstärke eines Mischer-Netzes, wie zuletzt gesetzt, oder null.</summary>
    public float? NetzLautstaerke(int netz)
    {
        lock (_sperre) return _netzLautstaerke.TryGetValue(netz, out var db) ? db : null;
    }

    /// <summary>Panorama eines Mischer-Netzes, wie zuletzt gesetzt, oder null.</summary>
    public float? NetzPanorama(int netz)
    {
        lock (_sperre) return _netzPanorama.TryGetValue(netz, out var p) ? p : null;
    }

    public GateEinstellung Gate
    {
        get
        {
            lock (_sperre) return _gate;
        }
        set
        {
            lock (_sperre)
            {
                _gate = value;
                GateGesetzt++;
            }
        }
    }

    public bool Sprachaktivierung { get; set; } = true;

    public bool Hoerprobe { get; set; }

    /// <summary>Wie oft die Hörprobe als Aufnahme gestartet bzw. abgebrochen wurde.</summary>
    public int ProbeStarts { get; private set; }

    public int ProbeAbbrueche { get; private set; }

    /// <summary>
    /// Stand der Probe. Die Attrappe spielt nichts ab; in der Vorführung läuft der Fortschritt im Takt, sonst setzt ihn ein Test.
    /// </summary>
    public ProbeStand Probe
    {
        get
        {
            lock (_sperre) return _probe;
        }
        set
        {
            lock (_sperre) _probe = value;
        }
    }

    public void ProbeStarten()
    {
        lock (_sperre)
        {
            ProbeStarts++;
            _probe = new ProbeStand(ProbeZustand.Aufnahme, IKlangMaschine.ProbeDauerMs);
        }
    }

    /// <summary>Stimmprobe in der Attrappe: sofort fertig, ein leiser 200-Hz-Ton der verlangten Länge (Tests der Oberfläche).</summary>
    public void StimmprobeStarten(int ms)
    {
        StimmprobeGestartet.Add(ms);
        _stimmprobe = new float[Math.Max(1, ms) * 48];
        for (var i = 0; i < _stimmprobe.Length; i++) _stimmprobe[i] = 0.1f * MathF.Sin(2 * MathF.PI * 200 * i / 48000f);
    }

    private float[]? _stimmprobe;

    /// <summary>Welche Stimmproben die Oberfläche verlangt hat (Dauer in ms).</summary>
    public List<int> StimmprobeGestartet { get; } = [];

    /// <summary>Hält die Probe zurück, solange der Test es will (läuft noch).</summary>
    public bool StimmprobeHalten { get; set; }

    public StimmprobeStand Stimmprobe => _stimmprobe is { } d ? new StimmprobeStand(StimmprobeHalten, !StimmprobeHalten, StimmprobeHalten ? 0 : d.Length / 48, d.Length / 48) : default;

    public float[]? StimmprobeHolen()
    {
        if (StimmprobeHalten || _stimmprobe is not { } d) return null;
        _stimmprobe = null;
        return d;
    }

    public void ProbeAbbrechen()
    {
        lock (_sperre)
        {
            ProbeAbbrueche++;
            _probe = new ProbeStand(ProbeZustand.Bereit, 0);
        }
    }

    public Rauschfilter Rauschfilter { get; set; }

    /// <summary>Rückfall von Stark auf Standard nachstellen (Tests): solange gesetzt, wirkt bei Stark nur Standard, mit diesem Grund.</summary>
    public string? RauschfilterRueckfall { get; set; }

    /// <summary>Die Attrappe filtert nicht; sie meldet den gewählten Filter als wirksam, bei einem Rückfall Standard.</summary>
    public RauschfilterStand Rauschfilterstand =>
        Rauschfilter == Rauschfilter.Stark && RauschfilterRueckfall is { } grund
            ? new(Rauschfilter, Rauschfilter.Standard, grund)
            : new(Rauschfilter, Rauschfilter, null);

    public bool Echounterdrueckung { get; set; } = true;

    private float _funkLautstaerke = 1f;

    /// <summary>Funk-Lautstärke (E7): begrenzt wie beim echten Mischer gemerkt (keine Zahl gilt als 1); die Attrappe spielt nichts.</summary>
    public float FunkLautstaerke
    {
        get => Volatile.Read(ref _funkLautstaerke);
        set => Volatile.Write(ref _funkLautstaerke, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 1f);
    }

    public bool Echoschutz { get; set; } = true;
    public bool MindestFunklautstaerke { get; set; } = true;
    public bool Pegelausgleich { get; set; } = true;

    private int _tastenNachlaufMs;

    /// <summary>Nachlauf der Sprechtaste (Q66): begrenzt wie bei der echten Maschine gemerkt; die Attrappe sendet nichts.</summary>
    public int TastenNachlaufMs
    {
        get => Volatile.Read(ref _tastenNachlaufMs);
        set
        {
            Volatile.Write(ref _tastenNachlaufMs, IKlangMaschine.TastenNachlaufBegrenzen(value));
            Interlocked.Increment(ref _tastenNachlaufGesetzt);
        }
    }

    private int _tastenNachlaufGesetzt;

    /// <summary>Wie oft <see cref="TastenNachlaufMs"/> gesetzt wurde (Tests).</summary>
    public int TastenNachlaufGesetzt => Volatile.Read(ref _tastenNachlaufGesetzt);

    private KlangfarbeEinstellung _klangfarbe;

    /// <summary>Klangfarbe (Q70 bis Q72): begrenzt wie bei der echten Maschine gemerkt; die Attrappe färbt nichts.</summary>
    public KlangfarbeEinstellung Klangfarbe
    {
        get
        {
            lock (_sperre) return _klangfarbe;
        }
        set
        {
            lock (_sperre)
            {
                _klangfarbe = value.Begrenzt();
                KlangfarbeGesetzt++;
            }
        }
    }

    /// <summary>Wie oft <see cref="Klangfarbe"/> gesetzt wurde (Tests).</summary>
    public int KlangfarbeGesetzt { get; private set; }

    /// <summary>Wie zuletzt gesetzt; die Attrappe verstärkt nichts.</summary>
    public LautstaerkeEinstellung Lautstaerke
    {
        get
        {
            lock (_sperre) return _lautstaerke;
        }
        set
        {
            lock (_sperre)
            {
                _lautstaerke = value;
                LautstaerkeGesetzt++;
            }
        }
    }

    /// <summary>Wie oft <see cref="Lautstaerke"/> gesetzt wurde.</summary>
    public int LautstaerkeGesetzt { get; private set; }

    /// <summary>Stand des Einmessens. Die Attrappe misst nichts; ein Test setzt das Ergebnis.</summary>
    public EinmessStand Einmessen
    {
        get
        {
            lock (_sperre) return _einmessen;
        }
        set
        {
            lock (_sperre) _einmessen = value;
        }
    }

    public void EinmessenStarten()
    {
        lock (_sperre) _einmessen = new EinmessStand(EinmessZustand.Laeuft, Pegelstufe.EinmessRahmen * 10, 0f);
    }

    public void EinmessenAbbrechen()
    {
        lock (_sperre) _einmessen = new EinmessStand(EinmessZustand.Bereit, 0, 0f);
    }

    /// <summary>
    /// Korridor der Sprachaktivierung (Klangstufe C1). Ohne Vorgabe wie die echte Maschine: Ist die Lautstärke als gelernt oder eingemessen
    /// gesetzt, liegt die Stimme am Ziel der Pegelstufe, die Grenzen folgen aus dem Gate (in Fest keine); sonst ist die Stimme unbekannt. Ein
    /// Test oder die Vorführung kann einen Stand vorgeben.
    /// </summary>
    public KorridorStand Korridor
    {
        get
        {
            lock (_sperre)
            {
                if (_korridor is { } k) return k;
                if (!(_lautstaerke.Gelernt || _lautstaerke.Eingemessen)) return KorridorStand.Leer;
                var stimme = Pegelstufe.ZielDbfs;
                return _gate.Modus == GateModus.Fest
                    ? new KorridorStand(stimme, float.NaN, float.NaN)
                    : new KorridorStand(stimme, stimme - _gate.MindestAbstandDb, stimme - _gate.ObergrenzeAbstandDb);
            }
        }
        set
        {
            lock (_sperre) _korridor = value;
        }
    }

    private KorridorStand? _korridor;
    private MindestEinmessStand _mindestEinmessen;

    /// <summary>Stand von "Mindestlautstärke einmessen". Die Attrappe misst nichts; ein Test setzt das Ergebnis.</summary>
    public MindestEinmessStand MindestEinmessen
    {
        get
        {
            lock (_sperre) return _mindestEinmessen;
        }
        set
        {
            lock (_sperre) _mindestEinmessen = value;
        }
    }

    /// <summary>Wie oft "Mindestlautstärke einmessen" begonnen wurde.</summary>
    public int MindestEinmessStarts { get; private set; }

    public void MindestEinmessenStarten()
    {
        lock (_sperre)
        {
            MindestEinmessStarts++;
            _mindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Normal, Mindesteinmessung.SatzRahmen * 10, 0f, 0f, float.NaN, float.NaN);
        }
    }

    public void MindestEinmessenAbbrechen()
    {
        lock (_sperre) _mindestEinmessen = new MindestEinmessStand(MindestEinmessSchritt.Bereit, 0, 0f, 0f, float.NaN, float.NaN);
    }

    /// <summary>Leere Telemetrie mit der eingestellten Verstärkung; ein Test kann einen Stand vorgeben.</summary>
    public TelemetrieStand Telemetrie
    {
        get
        {
            lock (_sperre)
            {
                return _telemetrie ?? new TelemetrieStand(0, 0, 10_000, [], default(TelemetrieEintrag) with { PegelstufeDb = _lautstaerke.FestDb });
            }
        }
        set
        {
            lock (_sperre) _telemetrie = value;
        }
    }

    public string? KlangprotokollOrdner { get; set; }

    private IReadOnlyList<AudioGeraet> _eingaenge =
    [
        new("attrappe-ein-1", "Mikrofon (Attrappe)", true),
        new("attrappe-ein-2", "Headset-Mikrofon (Attrappe)", false),
    ];

    private IReadOnlyList<AudioGeraet> _ausgaenge =
    [
        new("attrappe-aus-1", "Lautsprecher (Attrappe)", true),
        new("attrappe-aus-2", "Headset (Attrappe)", false),
    ];

    public IReadOnlyList<AudioGeraet> Eingaenge()
    {
        lock (_sperre) return _eingaenge;
    }

    public IReadOnlyList<AudioGeraet> Ausgaenge()
    {
        lock (_sperre) return _ausgaenge;
    }

    public event Action? GeraeteListeGeaendert;

    /// <summary>
    /// Tests: Windows meldet neue Geräte (seit 0.10.0): Die Listen werden ersetzt (ohne Angabe bleibt die bisherige), dann kommt
    /// <see cref="GeraeteListeGeaendert"/> wie aus der echten Maschine.
    /// </summary>
    public void GeraeteSetzen(IReadOnlyList<AudioGeraet>? eingaenge = null, IReadOnlyList<AudioGeraet>? ausgaenge = null)
    {
        lock (_sperre)
        {
            if (eingaenge is not null) _eingaenge = [.. eingaenge];
            if (ausgaenge is not null) _ausgaenge = [.. ausgaenge];
        }
        GeraeteListeGeaendert?.Invoke();
    }

    /// <summary>Tests: So scheitert das nächste <see cref="StartenAsync"/> (seit 0.10.0, Grund auf Start); null startet wie sonst.</summary>
    public Exception? StartFehler { get; set; }

    public Task StartenAsync(string? eingangId, string? ausgangId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (StartFehler is { } fehler) return Task.FromException(fehler);
        lock (_sperre)
        {
            EingangId = eingangId;
            AusgangId = ausgangId;
            Laeuft = true;
            Starts++;
            Geraete = new GeraeteZustand(true, AufnahmeFehlerBeimStart is null, true, AufnahmeFehlerBeimStart, null);
            if (_szene is not null && _takt is null)
            {
                _startMs = Environment.TickCount64;
                _takt = new Timer(_ => Tick(), null, 0, 20);
            }
        }
        return Task.CompletedTask;
    }

    public Task StoppenAsync()
    {
        Timer? t;
        lock (_sperre)
        {
            t = _takt;
            _takt = null;
            Laeuft = false;
            Stopps++;
            Geraete = default;
        }
        t?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Die Attrappe gibt beim Wiederholen das Mikrofon frei (als hätte der Nutzer es erlaubt).</summary>
    public void GeraeteWiederholen()
    {
        lock (_sperre)
        {
            Wiederholungen++;
            AufnahmeFehlerBeimStart = null;
            if (Laeuft) Geraete = new GeraeteZustand(true, true, true, null, null);
        }
    }

    public void SprechtasteGedrueckt()
    {
        if (!TasteGedrueckt && MikroStumm) SprechtasteBeiStumm?.Invoke();
        TasteGedrueckt = true;
    }

    /// <summary>Wie die Maschine; merkt sich, ob der Druck einen Nachlauf fortsetzen durfte (<see cref="LetzterDruckFortsetzen"/>).</summary>
    public void SprechtasteGedrueckt(bool fortsetzen)
    {
        LetzterDruckFortsetzen = fortsetzen;
        SprechtasteGedrueckt();
    }

    /// <summary>Was der letzte Druck über <see cref="SprechtasteGedrueckt(bool)"/> sagte; <c>null</c>, solange keiner kam (Tests).</summary>
    public bool? LetzterDruckFortsetzen { get; private set; }

    public void SprechtasteLosgelassen() => TasteGedrueckt = false;

    /// <summary>Der zuletzt gesetzte Funk-Klick (A63); ab Werk wie die echte Maschine.</summary>
    public FunkKlickEinstellungen Klick
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = new();

    /// <summary>Wie oft "Anhören" kam.</summary>
    public int KlickAnhoerungen { get; private set; }

    /// <summary>Was "Anhören" antwortet; ohne Angabe wie die echte Maschine: nur bei laufender Wiedergabe.</summary>
    public bool? KlickAnhoerenErgebnis { get; set; }

    public bool KlickAnhoeren()
    {
        KlickAnhoerungen++;
        return KlickAnhoerenErgebnis ?? Geraete.WiedergabeLaeuft;
    }

    /// <summary>Die Signaltöne des Meldesystems in ihrer Folge (seit 0.12.0), auch ohne laufende Wiedergabe gemerkt.</summary>
    public List<Signaltonart> Toene { get; } = [];

    public bool TonSpielen(Signaltonart art)
    {
        lock (_sperre) Toene.Add(art);
        return Geraete.WiedergabeLaeuft;
    }

    public void PegelFuerFunker(uint kontoId, float db)
    {
        lock (_sperre) _funkerPegel.Add((kontoId, db));
    }

    public void SetzeNetzLautstaerke(int netz, float db)
    {
        lock (_sperre) _netzLautstaerke[netz] = db;
    }

    public void SetzeNetzPanorama(int netz, float panorama)
    {
        lock (_sperre) _netzPanorama[netz] = panorama;
    }

    public void SetzeVerlustHinweis(int prozent) => VerlustHinweis = prozent;

    // ---- Klangstufe C2a ----

    private readonly List<FunkerDaempfung> _dauersender = [];
    private readonly List<(uint KontoId, FunkerAktion Aktion)> _aktionen = [];
    private uint? _q8Stumm;

    public DaempfungEinstellung Daempfung { get; set; } = DaempfungEinstellung.AbWerk;

    public WaechterStand EigenesUrteil { get; private set; } = WaechterStand.Keins;

    public event Action<WaechterStand>? EigenesUrteilGeaendert;

    public IReadOnlyList<FunkerDaempfung> Dauersender
    {
        get
        {
            lock (_sperre) return [.. _dauersender];
        }
    }

    public event Action? DauersenderGeaendert;

    /// <summary>Wahlen am gelben Zeichen, in der Reihenfolge (Test).</summary>
    public IReadOnlyList<(uint KontoId, FunkerAktion Aktion)> FunkerAktionen
    {
        get
        {
            lock (_sperre) return [.. _aktionen];
        }
    }

    public void FunkerHandeln(uint kontoId, FunkerAktion aktion)
    {
        lock (_sperre)
        {
            _aktionen.Add((kontoId, aktion));
            var i = _dauersender.FindIndex(f => f.KontoId == kontoId);
            var f = i >= 0 ? _dauersender[i] : new FunkerDaempfung(kontoId, DauersendUrteil.Keins, 0f, Daempfungsworte.Wort(0f), false, false);
            f = aktion switch
            {
                FunkerAktion.Stumm => f with { Stumm = true, Wort = Daempfungsworte.Wort(float.NegativeInfinity) },
                FunkerAktion.NichtMehrDaempfen => f with { NichtDaempfen = true, DaempfungDb = 0f, Wort = Daempfungsworte.Wort(0f) },
                _ => f with { Stumm = false, NichtDaempfen = false, DaempfungDb = 0f, Urteil = DauersendUrteil.Keins, Wort = Daempfungsworte.Wort(0f) },
            };
            if (i >= 0) _dauersender[i] = f;
            else _dauersender.Add(f);
        }
        DauersenderGeaendert?.Invoke();
    }

    public FunkerDaempfung? ZuletztGedaempftenUmschalten()
    {
        uint konto;
        bool stumm;
        lock (_sperre)
        {
            if (_q8Stumm is { } k)
            {
                konto = k;
                stumm = false;
                _q8Stumm = null;
            }
            else
            {
                var z = _dauersender.LastOrDefault(f => f.DaempfungDb < 0f && !f.Stumm);
                if (z.KontoId == 0) return null;
                konto = z.KontoId;
                stumm = true;
                _q8Stumm = konto;
            }
        }
        FunkerHandeln(konto, stumm ? FunkerAktion.Stumm : FunkerAktion.WiederNormal);
        lock (_sperre) return _dauersender.Single(f => f.KontoId == konto);
    }

    public bool MikroStumm { get; set; }

    public event Action? SprechtasteBeiStumm;

    /// <summary>Test und Vorführung: ein eigenes Urteil, als käme es vom Wächter.</summary>
    public void UrteilMelden(WaechterStand stand)
    {
        EigenesUrteil = stand;
        EigenesUrteilGeaendert?.Invoke(stand);
    }

    /// <summary>Test und Vorführung: der Stand eines Funkers, als käme er von der Empfängerseite.</summary>
    public void DauersenderMelden(FunkerDaempfung funker)
    {
        lock (_sperre)
        {
            var i = _dauersender.FindIndex(f => f.KontoId == funker.KontoId);
            if (i >= 0) _dauersender[i] = funker;
            else _dauersender.Add(funker);
        }
        DauersenderGeaendert?.Invoke();
    }

    public bool SpracheEmpfangen(uint kontoId, int netz, ushort sprechphase, uint frameNr, Sprachkennzeichen kennzeichen, ReadOnlySpan<byte> opus, long ankunftUs, bool durchsage = false)
    {
        lock (_sperre) _empfangen.Add(new EmpfangenesSprachpaket(kontoId, netz, sprechphase, frameNr, kennzeichen, opus.ToArray(), ankunftUs, durchsage));
        return true;
    }

    /// <summary>Test: meldet einen Pegel, als käme er aus der Kette.</summary>
    public void Melden(PegelMeldung meldung) => Pegel?.Invoke(meldung);

    /// <summary>Test und Vorführung: Pegel dessen, was in einem Funknetz ankommt.</summary>
    public void EmpfangsPegelMelden(ushort netzId, float db) => NetzPegel?.Invoke(netzId, db);

    /// <summary>Test: Empfangsanzeige je Sprecher und Mischer-Netz, wie aus dem Melde-Thread der Maschine.</summary>
    public void SprecherPegelMelden(params SprecherPegel[] sprecher) => EmpfangsPegel?.Invoke(sprecher);

    /// <summary>Test: ein Sprachpaket (oder ENDE mit leerem Opus), als käme es aus der Aufnahmekette.</summary>
    public void SendenSimulieren(Sprachkennzeichen kennzeichen, ReadOnlySpan<byte> opus, long aufnahmeTakte = 0)
    {
        var rahmen = new SendeRahmen(opus, kennzeichen, aufnahmeTakte);
        SprachpaketBereit?.Invoke(in rahmen);
    }

    /// <summary>Test: ein Hintergrundfehler der Maschine.</summary>
    public void FehlerMelden(Exception fehler) => Fehler?.Invoke(this, fehler);

    public async ValueTask DisposeAsync() => await StoppenAsync().ConfigureAwait(false);

    private void Tick()
    {
        if (Interlocked.Exchange(ref _tickLaeuft, 1) == 1) return;
        try
        {
            GateEinstellung gate;
            lock (_sperre)
            {
                if (_takt is null) return;
                gate = _gate;
                // Vorführung: Die Probe nimmt 5 s auf und spielt 5 s ab, ohne Ton.
                if (_probe.Zustand != ProbeZustand.Bereit)
                {
                    _probe = _probe.RestMs > 20 ? _probe with { RestMs = _probe.RestMs - 20 }
                        : _probe.Zustand == ProbeZustand.Aufnahme ? new ProbeStand(ProbeZustand.Wiedergabe, IKlangMaschine.ProbeDauerMs)
                        : new ProbeStand(ProbeZustand.Bereit, 0);
                }
            }
            var m = _szene!.Naechste(Environment.TickCount64 - _startMs, gate, Sprachaktivierung, TasteGedrueckt, Korridor);
            Pegel?.Invoke(m);
        }
        finally
        {
            Volatile.Write(ref _tickLaeuft, 0);
        }
    }
}


/// <summary>
/// Selbst gerechnete Geräuschszene, 20 s in Schleife: Stille, Sprache, Tastaturklappern, Sprache, Kinder im Hintergrund
/// (Rauschboden steigt), Sprache über dem Lärm, Rückkehr. Dazu ein grobes Gate nach den drei Modi, nur für die Anzeige.
/// Mit festem Startwert; keine fremden Aufnahmen.
/// </summary>
internal sealed class Vorfuehrungsszene(int startwert)
{
    private readonly Random _zufall = new(startwert);
    private float _boden = -62f;
    private float _schwelle = -53f;
    private long _sendenBis = -1;
    private long _letzteMs = -1;

    /// <param name="korridor">Seit Klangstufe C1: Mit bekannter Stimme bleibt die Linie in Automatisch und Lernend zwischen Unter- und Obergrenze.</param>
    public PegelMeldung Naechste(long ms, GateEinstellung gate, bool sprachaktivierung, bool taste, KorridorStand? korridor = null)
    {
        var dt = _letzteMs < 0 ? 0.02f : Math.Clamp((ms - _letzteMs) / 1000f, 0.001f, 0.5f);
        _letzteMs = ms;
        var (pegel, sprache, hintergrund) = Signal(ms);
        var zeitBoden = hintergrund > _boden ? 1.5f : 0.3f;
        _boden += (hintergrund - _boden) * Math.Min(1f, dt / zeitBoden);
        var soll = gate.Modus switch
        {
            GateModus.Fest => gate.SchwelleDb,
            GateModus.Automatisch => Math.Max(_boden + gate.AbstandDb, gate.GrenzlinieDb),
            _ => Math.Clamp(_boden + gate.AbstandDb, gate.KorridorVonDb, Math.Max(gate.KorridorVonDb, gate.KorridorBisDb)),
        };
        if (gate.Modus != GateModus.Fest && korridor is { StimmeBekannt: true } k)
            soll = Math.Max(k.MindestBezugDb - gate.MindestAbstandDb, Math.Min(Math.Max(_boden + gate.AbstandDb, -60f), k.StimmeDb - gate.ObergrenzeAbstandDb));
        if (gate.Modus == GateModus.Automatisch) _schwelle = soll;
        else if (gate.Modus == GateModus.Fest) _schwelle = soll;
        else
        {
            var tau = soll > _schwelle ? gate.AnstiegSekunden : gate.RueckkehrSekunden;
            _schwelle += (soll - _schwelle) * Math.Min(1f, dt / Math.Max(0.05f, tau));
        }
        if (sprachaktivierung && pegel > _schwelle && sprache >= gate.Sprachsicherheit) _sendenBis = ms + 300;
        var sendet = taste || (sprachaktivierung && ms <= _sendenBis);
        return new PegelMeldung(ms, pegel, _boden, _schwelle, sprache, sendet);
    }

    private (float Pegel, float Sprache, float Hintergrund) Signal(long ms)
    {
        var t = ms % 20_000 / 1000f;
        var rauschen = (float)((_zufall.NextDouble() * 3.0) - 1.5);
        var hintergrund = -62f + (1.5f * MathF.Sin(ms / 2700f));
        if (t is >= 10.5f and < 16f) hintergrund = -46f + (2f * MathF.Sin(ms / 400f));
        var pegel = hintergrund + rauschen;
        var sprache = 0.05f + ((float)_zufall.NextDouble() * 0.1f);
        if (t is (>= 2f and < 4.6f) or (>= 8f and < 10.2f) or (>= 13.2f and < 15.4f))
        {
            var silbe = 0.5f + (0.5f * MathF.Sin(ms / 45f) * MathF.Sin(ms / 170f));
            pegel = Math.Max(pegel, -34f + (14f * silbe) + rauschen);
            sprache = 0.75f + (0.2f * (float)_zufall.NextDouble());
        }
        else if (t is >= 5.5f and < 6.3f && ms / 90 % 2 == 0)
        {
            pegel = Math.Max(pegel, -31f + (rauschen * 2f));
            sprache = 0.08f + ((float)_zufall.NextDouble() * 0.1f);
        }
        else if (t is >= 10.5f and < 13.2f)
        {
            pegel = Math.Max(pegel, -40f + (6f * MathF.Sin(ms / 130f)) + rauschen);
            sprache = 0.25f + (0.2f * (float)_zufall.NextDouble());
        }
        return (pegel, sprache, hintergrund);
    }
}
