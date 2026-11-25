// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using Funk.Klang.Eingabe;
using Funk.Klang.Geraete;
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>Zähler der Klangmaschine, als Momentaufnahme ohne Gleichzeitigkeitsgarantie zwischen den Feldern.</summary>
public readonly record struct KlangStatistik(
    long Rahmen,
    long Pakete,
    long Kodierfehler,
    long SendeVerworfen,
    long ReferenzFehlt,
    long ReferenzVerworfen,
    long EmpfangAbgewiesen,
    int Sprecher,
    bool AufnahmeLaeuft,
    bool WiedergabeLaeuft,
    bool EchoreferenzLaeuft,
    StromFormat? Aufnahmeformat,
    StromFormat? Wiedergabeformat,
    JitterpufferStatistik Hoerprobe);

/// <summary>
/// Die Klangmaschine des Clients (<see cref="IKlangMaschine"/>): Mikrofon über WASAPI, Aufnahmekette
/// (<see cref="Sendekette"/>: AEC3 mit Loopback-Referenz, RNNoise, Sprach-Gate mit drei Modi, Sendeaufbereitung, Opus),
/// Sende-Ausgang <see cref="SprachpaketBereit"/> für den Transport, Empfang je Sprecher mit Jitterpuffer und Mischer,
/// Wiedergabe über WASAPI und die Hörprobe der eigenen Stimme wie beim Empfänger.
/// <para>
/// Threads: Die Audio-Threads der Geräte (Aufnahme, Loopback, Wiedergabe, je MMCSS "Pro Audio") rechnen ohne
/// Allokation und ohne Sperre. Ein Steuer-Thread öffnet und schließt Geräte und reagiert auf Gerätewechsel; ein
/// Melde-Thread ruft <see cref="SprachpaketBereit"/> und <see cref="Pegel"/> auf (etwa 50 Pegelmeldungen je Sekunde,
/// je zwei Aufnahmerahmen). Alle öffentlichen Methoden dürfen aus jedem Thread kommen.
/// </para>
/// <para>
/// Mischer-Netze: Die Integration bildet Funknetze auf 0 bis <see cref="IKlangMaschine.FunkNetze"/> - 1 ab
/// (<see cref="SpracheEmpfangen"/>, <see cref="SetzeNetzPanorama"/>); Netz 63 ist die Hörprobe.
/// </para>
/// <para>
/// Geräte: Mikrofon und Wiedergabe werden je für sich geöffnet. Scheitert eins (etwa das Mikrofon am Datenschutz von
/// Windows), läuft das andere, <see cref="Geraete"/> nennt den Grund, und der Steuer-Thread versucht es alle 10 s oder
/// nach einem Gerätewechsel wieder (<see cref="GeraeteWiederholen"/> sofort).
/// </para>
/// </summary>
public sealed partial class KlangMaschine : IKlangMaschine
{
    private const int SendeRingPlaetze = 128;
    private const int ReferenzRahmen = 32;
    private const long WiederholAbstandMs = 2000;
    private const long WiederholAbstandFehlerMs = 10_000;

    /// <summary>Abstand der Empfangsanzeige (<see cref="EmpfangsPegel"/>): 40 ms, 25 Meldungen je Sekunde.</summary>
    public const long EmpfangsMeldungAbstandUs = 40_000;

    private readonly KlangMaschinenOptionen _o;
    private readonly Func<long> _uhrUs;
    private readonly Steuerschleife? _steuer;
    private readonly Thread? _melder;
    private readonly AutoResetEvent _meldeSignal = new(false);
    private readonly IAudioSystem _system;
    private readonly bool _systemGehoertUns;
    private readonly bool _ausgabeStumm;
    private readonly EchoProzessor? _echo;
    private readonly RnnoiseProzessor? _rnnoise;
    private readonly RnnoiseProzessor? _referenzVad;
    private readonly Pegelstufe? _pegelstufe;
    private readonly Telemetriejournal _telemetrie = new();
    private readonly Lock _protokollSperre = new();
    private Klangprotokoll? _protokoll;
    private string? _protokollOrdner;
    private LautstaerkeEinstellung _lautstaerke = new(true, Pegelstufe.StartDb);
    private string? _lernEingang;
    private readonly IKodierer _kodierer;
    private readonly IKodierer _probeKodierer;
    private readonly Rahmenjournal _journal = new();
    private readonly PaketRing _sendeRing = new(SendeRingPlaetze, Sendeaufbereitung.MaxPaketBytes);
    private readonly RahmenRing _referenz = new(ReferenzRahmen);
    private readonly FunkSpur _funkSpur = new();
    private readonly Empfangsseite _empfang;
    private readonly Sendekette _kette;
    private readonly Lock _einstellSperre = new();
    private readonly RahmenEintrag[] _meldePuffer = new RahmenEintrag[Rahmenjournal.Kapazitaet];
    private readonly SprecherPegel[] _empfangsPuffer;

    private GateEinstellung _gate = GateVorgaben.Standard;
    private int _entsorgt;
    private int _starkUnterwegs;
    private string? _starkFehler;
    private volatile bool _echoGewuenscht = true;
    private readonly Lock _tastenSperre = new();
    private bool _taste;
    private int _ketteBelegt;
    private int _mischerBelegt;
    private int _referenzBelegt;
    private volatile bool _beendet;

    // Nur Steuer-Thread (bzw. Aufrufer ohne eigene Threads):
    private IAudioStrom? _aufnahme;
    private IAudioStrom? _loopback;
    private IAudioStrom? _wiedergabe;
    private Eingangswandler? _eingangswandler;
    private Eingangswandler? _loopbackWandler;
    private Ausgabewandler? _ausgabewandler;
    private string? _wunschEingang;
    private string? _wunschAusgang;
    private volatile bool _laeuft;
    private bool _apmMitEcho = true;
    private long _letzterVersuchMs;
    private volatile string? _aufnahmeFehler;
    private volatile string? _wiedergabeFehler;

    // Nur Wiedergabe-Thread:
    private FunkKlick? _klickGesehen;
    private long _drueckeGesehen;

    // Nur Melde-Thread (bzw. Zustellen):
    private long _journalGelesen;
    private bool _halbeMeldung;
    private RahmenEintrag _ersteHaelfte;
    private long _letzteEmpfangsMeldungUs = long.MinValue / 2;
    private int _empfangZuletzt;

    public KlangMaschine(KlangMaschinenOptionen? optionen = null)
    {
        _o = optionen ?? new KlangMaschinenOptionen();
        if (_o.AudioSystem is null && EchteGeraeteSperre() is { } sperre)
        {
            _meldeSignal.Dispose();
            throw new InvalidOperationException(sperre);
        }
        _ausgabeStumm = _o.AusgabeStumm || (_o.AudioSystem is null && !ImProgramm);
        _uhrUs = _o.UhrUs ?? Jitterpuffer.MonotonUs;
        _empfangsPuffer = new SprecherPegel[Math.Max(1, _o.Mischer?.MaxQuellen ?? new MischerEinstellungen().MaxQuellen)];
        if (_o.EigeneThreads) _steuer = new Steuerschleife("Funk Klang-Steuerung", Takt, FehlerMelden);
        try
        {
            if (_o.AudioSystem is { } system)
            {
                _system = system;
            }
            else
            {
                // WASAPI im Steuer-Thread anlegen, damit die COM-Objekte dort leben, wo sie benutzt werden.
                _system = _steuer is null ? new WasapiSystem() : _steuer.Ausfuehren<IAudioSystem>(() => new WasapiSystem()).GetAwaiter().GetResult();
                _systemGehoertUns = true;
            }
            _system.GeraeteGeaendert += GeraeteGeaendert;

            if (_o.MitApm)
            {
                _echo = new EchoProzessor();
                // Vor dem Einschalten des AEC, damit das APM nur einmal aufgebaut wird.
                if (_o.Aec3 is { } aec3) _echo.Aec3Einstellen(aec3);
                _echo.Konfigurieren(ApmFuer(echo: true));
                _ = _echo.SetzeVerzoegerung(_o.EchoVerzoegerungMs);
            }
            if (_o.MitRnnoise)
            {
                _rnnoise = new RnnoiseProzessor();
                // Zweite Instanz nur für die Sprachschätzung der Echoreferenz (Echoschutz), etwa 0,1 ms je 10 ms.
                _referenzVad = new RnnoiseProzessor();
            }
            _kodierer = _o.KodiererFabrik?.Invoke() ?? new OpusKodierer(_o.OpusAnwendung, _o.Bitrate);
            // Eigener Kodierer für die Hörprobe als Aufnahme, gleich eingestellt: Sie läuft ohne Gate neben dem Senden her.
            _probeKodierer = _o.KodiererFabrik?.Invoke() ?? new OpusKodierer(_o.OpusAnwendung, _o.Bitrate);
            var dekodierer = _o.DekodiererFabrik ?? (() => new OpusDekodierer());
            // Klangstufe C2a: Sprachanteil von Funkern, die lange senden, mit RNNoise (Urteil Dauersenden auf der Empfängerseite).
            var mischer = _o.Mischer ?? new MischerEinstellungen();
            if (_o.MitRnnoise && mischer.Sprachschaetzer is null) mischer = mischer.MitSprachschaetzer(() => new RnnoiseSchaetzer());
            _empfang = new Empfangsseite(dekodierer, _o.Jitterpuffer, _uhrUs, mischer);
            _empfang.Mischer.Spur = _funkSpur;
            if (_o.KlickStartwert is { } saat) _empfang.Mischer.Klick = new FunkKlick(saat: saat);
            if (_o.MitPegelstufe) _pegelstufe = new Pegelstufe(_lautstaerke);
            _kette = new Sendekette(_kodierer, _echo, _rnnoise, _o.Sprachwahrscheinlichkeit, _sendeRing, _journal, _referenz, _empfang.Hoerprobe, _uhrUs,
                _referenzVad, _probeKodierer, _pegelstufe, _telemetrie, _funkSpur, _empfang.Mischer)
            {
                EchoAn = false,
            };
            _kette.TasteBeendet = TasteBeendetKlicken;
            _kette.GateSetzen(_gate);
        }
        catch
        {
            // Was schon angelegt ist, wieder freigeben (native Handles, COM im Steuer-Thread).
            _empfang?.Dispose();
            _kodierer?.Dispose();
            _probeKodierer?.Dispose();
            _rnnoise?.Dispose();
            _referenzVad?.Dispose();
            _echo?.Dispose();
            if (_systemGehoertUns && _system is not null)
            {
                if (_steuer is null) _system.Dispose();
                else _steuer.Ausfuehren(_system.Dispose).GetAwaiter().GetResult();
            }
            _steuer?.Dispose();
            _meldeSignal.Dispose();
            throw;
        }

        if (_o.EigeneThreads)
        {
            _melder = new Thread(MeldeSchleife) { IsBackground = true, Name = "Funk Klang-Meldungen" };
            _melder.Start();
        }
    }

    public event Action<PegelMeldung>? Pegel;

    /// <summary>Kodierte eigene Sprache für den Transport, im Melde-Thread (siehe <see cref="SendeRahmen"/>).</summary>
    public event SendeAusgang? SprachpaketBereit;

    /// <summary>Fehler im Hintergrund (Gerät verschwunden, Treiberfehler, Ausnahme eines Ereignisempfängers).</summary>
    public event EventHandler<Exception>? Fehler;

    /// <summary>
    /// Empfangsanzeige (A18: wer gerade spricht und wie laut, je Mischer-Netz), aus dem Melde-Thread etwa alle
    /// <see cref="EmpfangsMeldungAbstandUs"/>, solange Sprecher bekannt sind, und einmal leer, wenn der letzte entfernt
    /// wurde. Die Hörprobe ist nicht dabei.
    /// </summary>
    public event SprecherPegelAusgang? EmpfangsPegel;

    /// <summary>Uhr der Maschine in Mikrosekunden; mit ihr stempelt der Transport die Ankunft empfangener Pakete.</summary>
    public long JetztUs => _uhrUs();

    /// <summary>
    /// Warum diese Maschine keine echten Geräte (WASAPI) anlegen darf, oder null. Echte Geräte gibt es nur im Programm
    /// selbst (<see cref="ImProgramm"/>) oder mit der ausdrücklichen Freigabe <c>FUNK_RAUCHTEST=1</c>. Ein Test, der die
    /// Maschine ohne <see cref="KlangMaschinenOptionen.AudioSystem"/> anlegt, öffnete sonst beim Start Mikrofon, Loopback
    /// und eine hörbare Wiedergabe. Außerhalb des Programms bleibt die Wiedergabe echter Geräte immer stumm.
    /// </summary>
    internal static string? EchteGeraeteSperre() =>
        ImProgramm || Environment.GetEnvironmentVariable("FUNK_RAUCHTEST") == "1"
            ? null
            : "Echte Audiogeräte nur in JG TacRelay selbst oder mit FUNK_RAUCHTEST=1; Tests geben KlangMaschinenOptionen.AudioSystem eine Attrappe.";

    /// <summary>Wahr, wenn TacRelay.exe selbst läuft (Einstiegsassembly), nicht ein Test oder Werkzeug.</summary>
    internal static bool ImProgramm => string.Equals(Assembly.GetEntryAssembly()?.GetName().Name, "TacRelay", StringComparison.Ordinal);

    /// <summary>Die Aufnahmekette (Tests und Diagnose).</summary>
    internal Sendekette Kette => _kette;

    /// <summary>Der Mischer der Wiedergabe mit dem Funk-Klick (Tests und Diagnose).</summary>
    internal Mischer Mischer => _empfang.Mischer;

    public GateEinstellung Gate
    {
        get
        {
            lock (_einstellSperre) return _gate;
        }
        set
        {
            lock (_einstellSperre)
            {
                _gate = GateVorgaben.Bereinigen(value);
                _kette.GateSetzen(_gate);
            }
        }
    }

    public bool Sprachaktivierung
    {
        get => _kette.Sprachaktivierung;
        set => _kette.Sprachaktivierung = value;
    }

    public bool Hoerprobe
    {
        get => _kette.HoerprobeAn;
        set => _kette.HoerprobeAn = value;
    }

    /// <summary>Hörprobe als Aufnahme (<see cref="IKlangMaschine.ProbeStarten"/>); greift ab dem nächsten Aufnahmerahmen.</summary>
    public void ProbeStarten() => _kette.ProbeStarten();

    public void ProbeAbbrechen() => _kette.ProbeAbbrechen();

    public ProbeStand Probe => _kette.Probe;

    /// <summary>Stimmprobe (<see cref="IKlangMaschine.StimmprobeStarten"/>): an die Sendekette.</summary>
    public void StimmprobeStarten(int ms) => _kette.StimmprobeStarten(ms);

    public StimmprobeStand Stimmprobe => _kette.Stimmprobe;

    public float[]? StimmprobeHolen() => _kette.StimmprobeHolen();

    /// <summary>
    /// Rauschfilter (<see cref="IKlangMaschine.Rauschfilter"/>). Stark legt DeepFilterNet3 an, sobald keiner da ist: mit eigenen Threads
    /// im Threadpool (etwa 200 ms, nie im Audio-Thread), sonst gleich im Aufrufer. Ein neues Wählen von Stark hebt einen Rückfall auf.
    /// </summary>
    public Rauschfilter Rauschfilter
    {
        get => _kette.Rauschfilter;
        set
        {
            var vorher = _kette.Rauschfilter;
            _kette.Rauschfilter = value;
            if (value != Rauschfilter.Stark) return;
            if (vorher != Rauschfilter.Stark) _kette.RueckfallSetzen(0);
            StarkVorbereiten();
        }
    }

    public RauschfilterStand Rauschfilterstand => new(_kette.Rauschfilter, _kette.RauschfilterWirksam, _kette.Rueckfall switch
    {
        0 => null,
        Sendekette.RueckfallRechenzeit => "Stark brauchte mehrmals länger als die 10 ms, die ein Rahmen Zeit hat. Standard wirkt, bis Stark neu gewählt oder Audio neu gestartet wird.",
        Sendekette.RueckfallFehler => "Stark meldete einen Fehler. Standard wirkt, bis Stark neu gewählt oder Audio neu gestartet wird.",
        _ => "Stark ließ sich nicht laden" + (Volatile.Read(ref _starkFehler) is { } f ? " (" + f + ")" : "") + ". Standard wirkt.",
    });

    /// <summary>Legt DeepFilterNet3 an, wenn Stark gewünscht ist und die Kette noch keinen hat (und keiner unterwegs ist).</summary>
    private void StarkVorbereiten()
    {
        if (_rnnoise is null || _kette.HatStark || Interlocked.CompareExchange(ref _starkUnterwegs, 1, 0) != 0) return;
        if (_o.EigeneThreads) _ = Task.Run(StarkAnlegen);
        else StarkAnlegen();
    }

    private void StarkAnlegen()
    {
        try
        {
            var df = _o.DeepFilterFabrik?.Invoke() ?? new DeepFilterProzessor(schwellen: DeepFilterSchwellen.Voll);
            if (Volatile.Read(ref _entsorgt) != 0)
            {
                df.Dispose();
                return;
            }
            _kette.StarkUebergeben(df)?.Dispose();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or ArgumentException or BadImageFormatException)
        {
            // Für die Oberfläche kurz und deutsch; die englische Meldung von .NET beim Laden sagt dem Funker nichts.
            Volatile.Write(ref _starkFehler, ex switch
            {
                DllNotFoundException => "deepfilternet.dll fehlt",
                EntryPointNotFoundException or BadImageFormatException => "deepfilternet.dll passt nicht zu dieser Fassung",
                _ => ex.Message,
            });
            _kette.RueckfallSetzen(Sendekette.RueckfallNichtVerfuegbar);
        }
        finally
        {
            Volatile.Write(ref _starkUnterwegs, 0);
        }
    }

    /// <summary>Klangmodul Echoschutz (<see cref="IKlangMaschine.Echoschutz"/>): an die Sendekette.</summary>
    public bool Echoschutz
    {
        get => _kette.EchoschutzAn;
        set => _kette.EchoschutzAn = value;
    }

    /// <summary>Klangmodul Mindest-Funklautstärke (<see cref="IKlangMaschine.MindestFunklautstaerke"/>): an die Gate-Führung.</summary>
    public bool MindestFunklautstaerke
    {
        get => _kette.Fuehrung.MindestAn;
        set => _kette.Fuehrung.MindestAn = value;
    }

    /// <summary>Klangmodul Pegelausgleich (<see cref="IKlangMaschine.Pegelausgleich"/>): an den Wächter der Empfängerseite im Mischer.</summary>
    public bool Pegelausgleich
    {
        get => _empfang.Mischer.Waechter.AngleichungAn;
        set => _empfang.Mischer.Waechter.AngleichungAn = value;
    }

    /// <summary>Klangfarbe (<see cref="IKlangMaschine.Klangfarbe"/>): begrenzt an die Sendekette; ab Werk neutral.</summary>
    public KlangfarbeEinstellung Klangfarbe
    {
        get => _kette.Klangfarbe;
        set => _kette.Klangfarbe = value.Begrenzt();
    }

    public bool Echounterdrueckung
    {
        get => _echoGewuenscht;
        set
        {
            if (_echoGewuenscht == value) return;
            _echoGewuenscht = value;
            Steuern(EchoAnpassen);
        }
    }

    /// <summary>
    /// Lautstärke (<see cref="IKlangMaschine.Lautstaerke"/>): bereinigt (<see cref="LautstaerkeEinstellung.Bereinigt"/>) und an die
    /// <see cref="Pegelstufe"/> gegeben. Ohne Pegelstufe (<see cref="KlangMaschinenOptionen.MitPegelstufe"/> falsch, die alte Kette)
    /// nur gemerkt.
    /// </summary>
    public LautstaerkeEinstellung Lautstaerke
    {
        get
        {
            lock (_einstellSperre) return _lautstaerke;
        }
        set
        {
            lock (_einstellSperre)
            {
                _lautstaerke = value.Bereinigt();
                _pegelstufe?.Setzen(_lautstaerke);
            }
        }
    }

    public void EinmessenStarten() => _pegelstufe?.EinmessenStarten();

    public void EinmessenAbbrechen() => _pegelstufe?.EinmessenAbbrechen();

    public EinmessStand Einmessen => _pegelstufe?.Einmessen ?? default;

    /// <summary>Korridor der Sprachaktivierung, wie er im letzten Aufnahmerahmen wirkte (Klangstufe C1).</summary>
    public KorridorStand Korridor => _kette.Korridor;

    /// <summary>"Mindestlautstärke einmessen" (<see cref="IKlangMaschine.MindestEinmessenStarten"/>); läuft in der Sendekette.</summary>
    public void MindestEinmessenStarten() => _kette.Mindesteinmessung.Starten();

    public void MindestEinmessenAbbrechen() => _kette.Mindesteinmessung.Abbrechen();

    public MindestEinmessStand MindestEinmessen => _kette.Mindesteinmessung.Stand;

    public TelemetrieStand Telemetrie => _telemetrie.Stand() with { Eingang = Volatile.Read(ref _lernEingang) };

    /// <summary>
    /// Steuer-Thread, nach dem Öffnen eines Mikrofons: Die Halteregel lernt neu (<see cref="Pegelstufe.NeuLernen"/>), wenn es nicht
    /// das Gerät ist, für das sie gelernt hat: beim ersten Öffnen das Gerät der Einstellung (<see cref="LautstaerkeEinstellung.Eingang"/>,
    /// unbekannt gilt als passend), danach das zuletzt geöffnete. Sonst fände ein anderes Mikrofon nur in Schritten von 1 dB je Pause
    /// seinen Pegel (Gegenprüfung Klangstufe A).
    /// </summary>
    private void LernEingangPruefen(string id)
    {
        var bisher = Volatile.Read(ref _lernEingang);
        if (string.Equals(bisher, id, StringComparison.Ordinal)) return;
        var e = Lautstaerke;
        var anderes = bisher is not null || (e.Gelernt && e.Eingang is not null && !string.Equals(e.Eingang, id, StringComparison.Ordinal));
        if (anderes)
        {
            _pegelstufe?.NeuLernen();
            // Gegenprüfung C2a, M4: Konto, Kopplung und Funkspur-Schutz gehören zum alten Mikrofon.
            _kette.WaechterZuruecksetzen();
        }
        Volatile.Write(ref _lernEingang, id);
    }

    /// <summary>
    /// Telemetrie je Aufnahmerahmen ab Nummer <paramref name="von"/> (die letzten gut 10 s); Rückgabe und
    /// <paramref name="ersteGueltige"/> wie beim Lesen des Rahmenjournals. Aus jedem Thread.
    /// </summary>
    public int TelemetrieLesen(long von, Span<TelemetrieEintrag> ziel, out long ersteGueltige) => _telemetrie.Lesen(von, ziel, out ersteGueltige);

    /// <summary>
    /// Klangprotokoll (<see cref="IKlangMaschine.KlangprotokollOrdner"/>): Setzen öffnet eine neue Datei im Ordner, <c>null</c> schließt
    /// sie mit einer Zusammenfassung. Geschrieben wird beim Zustellen (Melde-Thread). Lässt sich die Datei nicht anlegen, kommt der
    /// Grund über <see cref="Fehler"/>, und das Protokoll bleibt aus.
    /// </summary>
    public string? KlangprotokollOrdner
    {
        get
        {
            lock (_protokollSperre) return _protokollOrdner;
        }
        set
        {
            lock (_protokollSperre)
            {
                if (string.Equals(value, _protokollOrdner, StringComparison.Ordinal)) return;
                _protokoll?.Beenden(_telemetrie);
                _protokoll = null;
                _protokollOrdner = null;
                if (value is null) return;
                try
                {
                    _protokoll = Klangprotokoll.Oeffnen(value, _telemetrie, DateTime.Now);
                    _protokollOrdner = value;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    FehlerMelden(new InvalidOperationException("Das Klangprotokoll ließ sich nicht anlegen: " + ex.Message, ex));
                }
            }
        }
    }

    private void ProtokollFortschreiben()
    {
        lock (_protokollSperre)
        {
            if (_protokoll is null) return;
            try
            {
                _protokoll.Fortschreiben(_telemetrie);
            }
            catch (IOException ex)
            {
                _protokoll.Dispose();
                _protokoll = null;
                _protokollOrdner = null;
                FehlerMelden(new InvalidOperationException("Das Klangprotokoll ließ sich nicht weiterschreiben: " + ex.Message, ex));
            }
        }
    }

    public KlangStatistik Statistik => new(
        _kette.VerarbeiteteRahmen,
        _kette.Pakete,
        _kette.Sendeaufbereitung.Kodierfehler,
        _kette.SendeVerworfen,
        _kette.ReferenzFehlt,
        _kette.ReferenzVerworfen,
        _empfang.Abgewiesen,
        _empfang.AnzahlSprecher,
        Volatile.Read(ref _aufnahme) is not null,
        Volatile.Read(ref _wiedergabe) is not null,
        Volatile.Read(ref _loopback) is not null,
        Volatile.Read(ref _aufnahme)?.Format,
        Volatile.Read(ref _wiedergabe)?.Format,
        _empfang.Hoerprobe.Statistik);

    public IReadOnlyList<AudioGeraet> Eingaenge() => Auf(() => _system.Eingaenge());

    public IReadOnlyList<AudioGeraet> Ausgaenge() => Auf(() => _system.Ausgaenge());

    public Task StartenAsync(string? eingangId, string? ausgangId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_beendet, this);
        // Ein Neustart gibt Stark eine neue Gelegenheit (Rückfall wegen Rechenzeit oder Fehler gilt bis hierher).
        if (_kette.Rauschfilter == Rauschfilter.Stark)
        {
            _kette.RueckfallSetzen(0);
            StarkVorbereiten();
        }
        return AufAsync(() => Starten(eingangId, ausgangId));
    }

    public Task StoppenAsync() => _beendet ? Task.CompletedTask : AufAsync(Stoppen);

    public void GeraeteWiederholen()
    {
        if (_beendet) return;
        Steuern(() =>
        {
            if (_laeuft) GeraeteAbgleichen();
        });
    }

    public GeraeteZustand Geraete => new(
        _laeuft,
        Volatile.Read(ref _aufnahme) is not null,
        Volatile.Read(ref _wiedergabe) is not null,
        _aufnahmeFehler,
        _wiedergabeFehler);

    public void SprechtasteGedrueckt() => SprechtasteGedrueckt(fortsetzen: true);

    /// <inheritdoc/>
    public void SprechtasteGedrueckt(bool fortsetzen)
    {
        // Merker und Kette unter einer Sperre ändern: Kämen Drücken und Loslassen verschränkt aus zwei Threads, könnte
        // sonst der Merker "los" und die Kette "gedrückt" stehen bleiben, also das Mikrofon offen. Der Audio-Thread nimmt
        // die Sperre nie, er liest nur den Tastenzustand der Kette.
        TastenDruck druck;
        lock (_tastenSperre)
        {
            if (_taste) return;
            _taste = true;
            druck = _kette.TasteDruecken(fortsetzen);
        }
        // Mikro stumm (Q31): nichts geht hinaus, also auch kein eigener Klick; die Oberfläche sagt "Mikro ist stumm".
        if (_kette.Stumm)
        {
            Interlocked.Increment(ref _stummHinweise);
            return;
        }
        // Im Nachlauf wieder gedrückt (Q66): Derselbe Spruch läuft weiter, also kein neuer Klick (und der vom Loslassen kam noch nicht). Gilt der
        // Druck einem anderen Ziel, endet der alte Spruch hier statt nach dem Nachlauf: Es klickt für ihn beim Loslassen und für den neuen beim Drücken.
        if (druck == TastenDruck.Fortgesetzt) return;
        if (druck == TastenDruck.Geschnitten) _empfang.Mischer.Klick?.Anfordern(KlickArt.Loslassen);
        _empfang.Mischer.Klick?.Anfordern(KlickArt.Druck);
    }

    public void SprechtasteLosgelassen()
    {
        lock (_tastenSperre)
        {
            if (!_taste) return;
            _taste = false;
            _kette.TasteLoslassen();
        }
        // Den Klick beim Loslassen fordert die Kette an, sobald sie nach dem Nachlauf aufhört zu senden (TasteBeendet). Ohne Mikrofon
        // rechnet keine Kette: dann gleich, wie bis 0.9.2.
        if (Volatile.Read(ref _aufnahme) is null && _kette.TasteAbschliessen() && !_kette.Stumm) _empfang.Mischer.Klick?.Anfordern(KlickArt.Loslassen);
    }

    /// <summary>
    /// Nachlauf der Sprechtaste (<see cref="IKlangMaschine.TastenNachlaufMs"/>): begrenzt auf 0 bis <see cref="IKlangMaschine.TastenNachlaufHoechstMs"/>
    /// in 10-ms-Schritten und an die Kette. Ab Werk 0 (wie bis 0.9.2); der Client setzt seine Einstellung.
    /// </summary>
    public int TastenNachlaufMs
    {
        get => _kette.TastenNachlaufMs;
        set => _kette.TastenNachlaufMs = IKlangMaschine.TastenNachlaufBegrenzen(value);
    }

    /// <summary>Aufnahme-Thread, am Ende des Sendens nach dem Loslassen (nach dem Nachlauf): der Klick beim Loslassen.</summary>
    private void TasteBeendetKlicken() => _empfang.Mischer.Klick?.Anfordern(KlickArt.Loslassen);

    /// <summary>
    /// Funk-Klick (<see cref="IKlangMaschine.Klick"/>): Eine neue Einstellung ist ein neuer <see cref="FunkKlick"/> im Mischer (seine
    /// Puffer entstehen hier, das Rauschen je Klick im Audio-Thread); was der alte gerade spielte, bricht ab.
    /// </summary>
    public FunkKlickEinstellungen Klick
    {
        get => _empfang.Mischer.Klick?.Einstellungen ?? new FunkKlickEinstellungen();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _empfang.Mischer.Klick = new FunkKlick(value, _o.KlickStartwert);
        }
    }

    /// <summary>"Anhören" (A63): nur bei laufender Wiedergabe, sonst käme die Folge erst beim nächsten Start.</summary>
    public bool KlickAnhoeren() =>
        !_beendet && Volatile.Read(ref _wiedergabe) is not null && _empfang.Mischer.Klick is { } k && k.AnhoerenAnfordern();

    /// <summary>Signalton des Meldesystems (seit 0.12.0): nur bei laufender Wiedergabe, sonst käme er erst beim nächsten Start.</summary>
    public bool TonSpielen(Signaltonart art) => !_beendet && Volatile.Read(ref _wiedergabe) is not null && _empfang.Mischer.Ton.Anfordern(art);

    /// <summary>Verbindet eine Sprechtasten-Quelle (Raw Input, später DirectInput) mit der Maschine.</summary>
    public IDisposable SprechtasteVerbinden(ISprechtasteQuelle quelle)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        quelle.Gedrueckt += SprechtasteGedrueckt;
        quelle.Losgelassen += SprechtasteLosgelassen;
        return new Abmeldung(() =>
        {
            quelle.Gedrueckt -= SprechtasteGedrueckt;
            quelle.Losgelassen -= SprechtasteLosgelassen;
            SprechtasteLosgelassen();
        });
    }

    public void PegelFuerFunker(uint kontoId, float db) => _empfang.PegelSetzen(kontoId, db);

    /// <summary>Funk-Lautstärke (<see cref="IKlangMaschine.FunkLautstaerke"/>): an den Mischer, der sie auf die ganze Summe legt.</summary>
    public float FunkLautstaerke
    {
        get => _empfang.Mischer.FunkLautstaerke;
        set => _empfang.Mischer.FunkLautstaerke = value;
    }

    // ---------------------------------------------------------------- Klangstufe C2a: Auto Dauersend-Dämpfung, Mikro stumm

    private DaempfungEinstellung _daempfung = DaempfungEinstellung.AbWerk;
    private readonly Lock _waechterSperre = new();
    private uint _q8Stumm;
    private int _stummHinweise;
    private int _stummHinweiseGemeldet;
    private DauersendUrteil _urteilGemeldet;
    private int _dauersenderTakt;
    private int _dauersenderSignatur;
    private readonly List<FunkerDaempfung> _dauersenderPuffer = [];
    private readonly Dictionary<string, float> _grenzen = new(StringComparer.Ordinal);
    private string? _grenzeGeraet;

    /// <summary>Schalter "Auto Dauersend-Dämpfung" und Stufe (<see cref="IKlangMaschine.Daempfung"/>): an Sendekette (eigene Seite) und Mischer (Empfängerseite).</summary>
    public DaempfungEinstellung Daempfung
    {
        get
        {
            lock (_einstellSperre) return _daempfung;
        }
        set
        {
            lock (_einstellSperre)
            {
                _daempfung = value;
                _kette.Daempfung = value;
                _empfang.Mischer.Daempfung = value;
            }
        }
    }

    public WaechterStand EigenesUrteil => Stand(_kette.Urteil);

    /// <summary>Das Urteil mit Ursache; bei Echo ohne Echounterdrückung mit dem Hinweis, sie einzuschalten.</summary>
    private WaechterStand Stand(DauersendUrteil u) =>
        u == DauersendUrteil.Echo && !_echoGewuenscht ? new WaechterStand(u, Daempfungsworte.EchoOhneEchounterdrueckung) : WaechterStand.Aus(u);

    public event Action<WaechterStand>? EigenesUrteilGeaendert;

    public IReadOnlyList<FunkerDaempfung> Dauersender
    {
        get
        {
            var liste = new List<FunkerDaempfung>();
            _empfang.Dauersender(liste, out _);
            return liste;
        }
    }

    public event Action? DauersenderGeaendert;

    public void FunkerHandeln(uint kontoId, FunkerAktion aktion)
    {
        lock (_waechterSperre)
        {
            if (kontoId == _q8Stumm) _q8Stumm = 0;
            _empfang.Handeln(kontoId, aktion);
        }
    }

    /// <summary>Taste "Dauersender stumm" (Q8): erster Druck den zuletzt gedämpften Funker stumm, zweiter Druck wieder normal.</summary>
    public FunkerDaempfung? ZuletztGedaempftenUmschalten()
    {
        lock (_waechterSperre)
        {
            var liste = new List<FunkerDaempfung>();
            uint konto;
            if (_q8Stumm != 0)
            {
                konto = _q8Stumm;
                _q8Stumm = 0;
                _empfang.Handeln(konto, FunkerAktion.WiederNormal);
            }
            else
            {
                _empfang.Dauersender(liste, out konto);
                if (konto == 0) return null;
                _empfang.Handeln(konto, FunkerAktion.Stumm);
                _q8Stumm = konto;
            }
            _empfang.Dauersender(liste, out _);
            foreach (var f in liste)
            {
                if (f.KontoId == konto) return f;
            }
            return new FunkerDaempfung(konto, DauersendUrteil.Keins, 0f, Daempfungsworte.Wort(0f), false, false);
        }
    }

    /// <summary>Mikro stumm (<see cref="IKlangMaschine.MikroStumm"/>); greift ab dem nächsten Aufnahmerahmen.</summary>
    public bool MikroStumm
    {
        get => _kette.Stumm;
        set => _kette.Stumm = value;
    }

    public event Action? SprechtasteBeiStumm;

    /// <summary>
    /// Steuer-Thread, beim Öffnen einer Wiedergabe: Die Grenzlautstärke des bisherigen Geräts merken (für diese Sitzung) und die des neuen setzen;
    /// die ganze Funk-Wiedergabe beginnt beim neuen Gerät ohne Absenkung.
    /// </summary>
    private void GrenzeWechseln(string geraet)
    {
        lock (_waechterSperre)
        {
            if (string.Equals(geraet, _grenzeGeraet, StringComparison.Ordinal)) return;
            if (_grenzeGeraet is not null) _grenzen[_grenzeGeraet] = _kette.Waechter.GrenzeDb;
            _kette.Waechter.GrenzeDb = _grenzen.GetValueOrDefault(geraet);
            _empfang.Mischer.FunkGesamtDb = 0f;
            _grenzeGeraet = geraet;
        }
    }

    /// <summary>Melde-Thread: Urteil, Liste der Dauersender (alle 100 ms) und Hinweis "Mikro ist stumm" melden, wenn sich etwas geändert hat.</summary>
    private void WaechterMelden()
    {
        var urteil = _kette.Urteil;
        if (urteil != _urteilGemeldet)
        {
            _urteilGemeldet = urteil;
            Melden(() => EigenesUrteilGeaendert?.Invoke(Stand(urteil)));
        }
        var hinweise = Volatile.Read(ref _stummHinweise);
        if (hinweise != _stummHinweiseGemeldet)
        {
            _stummHinweiseGemeldet = hinweise;
            Melden(() => SprechtasteBeiStumm?.Invoke());
        }
        if (++_dauersenderTakt < 10) return;
        _dauersenderTakt = 0;
        _empfang.Dauersender(_dauersenderPuffer, out _);
        var signatur = 17;
        foreach (var f in _dauersenderPuffer) signatur = HashCode.Combine(signatur, f.KontoId, f.Urteil, f.Wort, f.Stumm, f.NichtDaempfen);
        if (signatur == _dauersenderSignatur) return;
        _dauersenderSignatur = signatur;
        Melden(() => DauersenderGeaendert?.Invoke());
    }

    private void Melden(Action aktion)
    {
        try
        {
            aktion();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FehlerMelden(ex);
        }
    }

    /// <summary>
    /// Ein empfangenes, geprüftes Sprachpaket (Netz- oder Tunnel-Thread). <paramref name="netz"/>: Mischer-Netz 0 bis 62;
    /// <paramref name="ankunftUs"/> auf der Uhr <see cref="JetztUs"/>. Falsch, wenn es nicht angenommen wurde.
    /// </summary>
    public bool SpracheEmpfangen(uint kontoId, int netz, ushort sprechphase, uint frameNr, Sprachkennzeichen kennzeichen, ReadOnlySpan<byte> opus, long ankunftUs, bool durchsage = false) =>
        _empfang.Empfangen(kontoId, netz, sprechphase, frameNr, kennzeichen, opus, ankunftUs, durchsage);

    /// <summary>Lautstärke eines Mischer-Netzes in dB.</summary>
    public void SetzeNetzLautstaerke(int netz, float db) => _empfang.Mischer.SetzeNetzLautstaerke(netz, db);

    /// <summary>Panorama eines Mischer-Netzes (A9): -1 links, 0 beide Ohren, +1 rechts.</summary>
    public void SetzeNetzPanorama(int netz, float panorama) => _empfang.Mischer.SetzeNetzPanorama(netz, panorama);

    /// <summary>Erwarteter Paketverlust aus den Empfängerberichten, für die In-band-FEC des Kodierers.</summary>
    public void SetzeVerlustHinweis(int prozent) => _kette.Sendeaufbereitung.SetzeVerlustHinweis(prozent);

    /// <summary>
    /// Sende-Ausgang und Pegelmeldungen zustellen: was seit dem letzten Aufruf anfiel. Läuft sonst im Melde-Thread; ohne
    /// eigene Threads (Tests) ruft der Aufrufer es selbst auf, nie gleichzeitig aus zwei Threads.
    /// </summary>
    public void Zustellen()
    {
        while (_sendeRing.Spaehen(out var i))
        {
            var rahmen = new SendeRahmen(_sendeRing.Daten(i), _sendeRing.Kennzeichen(i), _sendeRing.AnkunftUs(i));
            try
            {
                SprachpaketBereit?.Invoke(in rahmen);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                FehlerMelden(ex);
            }
            _sendeRing.Freigeben();
        }

        var anzahl = _journal.Lesen(_journalGelesen, _meldePuffer, out var erste);
        if (erste != _journalGelesen) _halbeMeldung = false; // übersprungen: neu paaren
        _journalGelesen = erste + anzahl;
        for (var i = 0; i < anzahl; i++)
        {
            var r = _meldePuffer[i];
            if (!_halbeMeldung)
            {
                _ersteHaelfte = r;
                _halbeMeldung = true;
                continue;
            }
            _halbeMeldung = false;
            var a = _ersteHaelfte;
            var leistung = 0.5f * (MathF.Pow(10f, a.PegelDbfs / 10f) + MathF.Pow(10f, r.PegelDbfs / 10f));
            var meldung = new PegelMeldung(
                r.ZeitMs,
                Verarbeitung.Pegel.LeistungZuDb(leistung),
                r.RauschbodenDb,
                r.LinieDb,
                MathF.Max(a.Sprachwahrscheinlichkeit, r.Sprachwahrscheinlichkeit),
                a.Sendet || r.Sendet,
                a.Stumm || r.Stumm,
                Zurueckgehalten: a.Zurueckgehalten || r.Zurueckgehalten);
            try
            {
                Pegel?.Invoke(meldung);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                FehlerMelden(ex);
            }
        }

        EmpfangMelden();
        WaechterMelden();
        ProtokollFortschreiben();
        // Einen DeepFilterNet3, den die Kette nicht mehr braucht, hier freigeben, nie im Audio-Thread.
        _kette.AusgedientenHolen()?.Dispose();
    }

    private void EmpfangMelden()
    {
        if (EmpfangsPegel is not { } ausgang) return;
        var jetzt = _uhrUs();
        if (jetzt - _letzteEmpfangsMeldungUs < EmpfangsMeldungAbstandUs) return;
        _letzteEmpfangsMeldungUs = jetzt;
        var n = _empfang.PegelLesen(_empfangsPuffer);
        if (n == 0 && _empfangZuletzt == 0) return;
        _empfangZuletzt = n;
        try
        {
            ausgang(_empfangsPuffer.AsSpan(0, n));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FehlerMelden(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Auch zwei gleichzeitige Aufrufe (etwa Oberfläche und Programmende) geben nur einmal frei.
        if (Interlocked.Exchange(ref _entsorgt, 1) != 0) return;
        await StoppenAsync().ConfigureAwait(false);
        _beendet = true;
        _meldeSignal.Set();
        _melder?.Join();
        KlangprotokollOrdner = null;
        _system.GeraeteGeaendert -= GeraeteGeaendert;
        if (_steuer is not null)
        {
            if (_systemGehoertUns) await _steuer.Ausfuehren(_system.Dispose).ConfigureAwait(false);
            _steuer.Dispose();
        }
        else if (_systemGehoertUns)
        {
            _system.Dispose();
        }
        _empfang.Dispose();
        _kodierer.Dispose();
        _probeKodierer.Dispose();
        // Ein DeepFilterNet3, der gerade angelegt wird, sieht _entsorgt und gibt sich selbst frei; danach alle der Kette.
        SpinWait.SpinUntil(() => Volatile.Read(ref _starkUnterwegs) == 0, 2000);
        _kette.StarkFreigeben();
        _rnnoise?.Dispose();
        _referenzVad?.Dispose();
        _echo?.Dispose();
        _meldeSignal.Dispose();
    }

    // ---------------------------------------------------------------- Audio-Threads

    private void Aufnahmerahmen(Span<float> rahmen)
    {
        // Nie zwei Aufnahme-Threads zugleich in der Kette (etwa kurz beim Gerätewechsel): Der zweite verwirft seinen
        // Rahmen, statt zu warten.
        if (Interlocked.CompareExchange(ref _ketteBelegt, 1, 0) != 0) return;
        try
        {
            _kette.Verarbeiten(rahmen);
        }
        finally
        {
            Volatile.Write(ref _ketteBelegt, 0);
        }
        if (_melder is not null) _meldeSignal.Set();
    }

    private void Referenzrahmen(Span<float> rahmen)
    {
        if (Interlocked.CompareExchange(ref _referenzBelegt, 1, 0) != 0) return;
        _ = _referenz.Schreiben(rahmen);
        Volatile.Write(ref _referenzBelegt, 0);
    }

    private void Mischen(Span<float> ziel, int kanaele)
    {
        if (Interlocked.CompareExchange(ref _mischerBelegt, 1, 0) != 0)
        {
            ziel.Clear();
            return;
        }
        try
        {
            _empfang.Mischer.Mischen(ziel, kanaele);
            EigenenKlickPruefen();
            if (_ausgabeStumm) ziel.Clear();
            // Was der eigene Mischer hörbar spielt, ist Sprache: voller Echoschutz, auch wenn daneben Musik läuft.
            else if (Verarbeitung.Pegel.Dbfs(ziel) >= global::Funk.Klang.Maschine.Echoschutz.AktivAbDb) _kette.FunkWiedergabeMelden();
        }
        finally
        {
            Volatile.Write(ref _mischerBelegt, 0);
        }
    }

    /// <summary>
    /// Wiedergabe-Thread: Hat der Mischer gerade den eigenen Druck-Klick begonnen, erfährt es die Sendekette (sie nimmt ihn aus der
    /// Echoreferenz). Eine neue Klick-Einstellung ist eine neue Instanz mit eigenem Zähler; ihr Stand gilt dann als gesehen.
    /// </summary>
    private void EigenenKlickPruefen()
    {
        if (_empfang.Mischer.Klick is not { } klick) return;
        var gestartet = klick.EigeneDrueckeGestartet;
        if (!ReferenceEquals(klick, _klickGesehen))
        {
            _klickGesehen = klick;
            _drueckeGesehen = gestartet;
            return;
        }
        if (gestartet == _drueckeGesehen) return;
        _drueckeGesehen = gestartet;
        _kette.EigenenKlickMelden();
    }

    // ---------------------------------------------------------------- Hilfen

    private void MeldeSchleife()
    {
        while (!_beendet)
        {
            _ = _meldeSignal.WaitOne(50);
            Zustellen();
        }
        Zustellen();
    }

    private void Steuern(Action auftrag)
    {
        if (_steuer is not null) _steuer.Vormerken(auftrag);
        else auftrag();
    }

    private T Auf<T>(Func<T> auftrag) =>
        _steuer is null ? auftrag() : _steuer.Ausfuehren(auftrag).GetAwaiter().GetResult();

    private Task AufAsync(Action auftrag)
    {
        if (_steuer is not null) return _steuer.Ausfuehren(auftrag);
        auftrag();
        return Task.CompletedTask;
    }

    private void FehlerMelden(Exception ex)
    {
        try
        {
            Fehler?.Invoke(this, ex);
        }
        catch (Exception innen) when (innen is not OutOfMemoryException)
        {
            // Ein Fehler im Fehlerempfänger darf keinen Hintergrund-Thread beenden.
        }
    }

    private sealed class Abmeldung(Action aktion) : IDisposable
    {
        private Action? _aktion = aktion;

        public void Dispose() => Interlocked.Exchange(ref _aktion, null)?.Invoke();
    }
}
