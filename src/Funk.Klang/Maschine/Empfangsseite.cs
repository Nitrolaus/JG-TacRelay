// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>
/// Empfangsanzeige eines Sprechers (A18): Konto, Mischer-Netz, lautester 10-ms-Block seit der letzten Abfrage in dBFS
/// (so, wie er ankommt, vor Funker- und Netzpegel, also auch bei stumm gestelltem Funker) und ob er in dieser Zeit
/// gesprochen hat.
/// </summary>
public readonly record struct SprecherPegel(uint KontoId, int Netz, float PegelDbfs, bool Spricht);

/// <summary>Empfangsanzeige aller bekannten Sprecher; die Spanne gilt nur während des Aufrufs.</summary>
public delegate void SprecherPegelAusgang(ReadOnlySpan<SprecherPegel> sprecher);

/// <summary>
/// Misst eine Quelle im Audio-Thread, ohne sie zu verändern: lautester Block und Aktivität seit der letzten Abfrage.
/// Schreiber ist der Audio-Thread, Leser ein beliebiger anderer; ohne Sperre und ohne Allokation. Ein Block, der genau
/// zwischen Lesen und Zurücksetzen fertig wird, kann der Anzeige entgehen.
/// </summary>
internal sealed class MessQuelle(IKlangquelle innen) : IKlangquelle
{
    private float _spitze;
    private int _aktiv;

    public QuellenEreignis Lesen(Span<float> ziel)
    {
        var ereignis = innen.Lesen(ziel);
        var leistung = Pegel.Leistung(ziel);
        if (leistung > Volatile.Read(ref _spitze)) Volatile.Write(ref _spitze, leistung);
        if ((ereignis & QuellenEreignis.Aktiv) != 0) Volatile.Write(ref _aktiv, 1);
        return ereignis;
    }

    /// <summary>Lautester Block seit dem letzten Aufruf in dBFS, danach zurückgesetzt.</summary>
    public float SpitzeHolen() => Pegel.LeistungZuDb(Interlocked.Exchange(ref _spitze, 0f));

    /// <summary>War die Quelle seit dem letzten Aufruf aktiv (Sprechphase im Jitterpuffer)? Danach zurückgesetzt.</summary>
    public bool AktivHolen() => Interlocked.Exchange(ref _aktiv, 0) != 0;
}

/// <summary>
/// Empfangsseite: je Sprecher (Konto) ein <see cref="Jitterpuffer"/> mit eigenem Dekodierer als Quelle im
/// <see cref="Mischer"/>, dazu die Hörprobe als feste Quelle auf Netz <see cref="HoerprobeNetz"/>.
/// <para>
/// Threads: <see cref="Empfangen"/>, <see cref="PegelSetzen"/> und <see cref="Aufraeumen"/> aus beliebigen Threads
/// (Netz, Oberfläche, Steuer-Thread); sie teilen sich eine Sperre, die der Audio-Thread nie nimmt. Die Sperre macht aus
/// mehreren Netz-Threads (UDP und Tunnel) den einen Schreiber, den jeder Jitterpuffer verlangt. Der Audio-Thread liest
/// nur über den Mischer. Ein Sprecher ohne Paket seit <see cref="RuheUs"/> wird entfernt; seinen Dekodierer gibt die
/// Seite erst frei, wenn der Mischer ihn nicht mehr lesen kann (<see cref="Mischer.QuelleFreigebbar"/>).
/// </para>
/// </summary>
internal sealed class Empfangsseite : IDisposable
{
    /// <summary>Mischer-Netz der Hörprobe; die Integration bildet Funknetze auf 0 bis 62 ab.</summary>
    public const int HoerprobeNetz = IKlangMaschine.FunkNetze;

    /// <summary>Sprecher ohne Paket so lange werden entfernt (60 s).</summary>
    public const long RuheUs = 60_000_000;

    private sealed class Sprecher(Jitterpuffer puffer, MessQuelle messung, IDekodierer dekodierer, int platz, int netz, bool durchsage)
    {
        public Jitterpuffer Puffer { get; } = puffer;
        public MessQuelle Messung { get; } = messung;
        public IDekodierer Dekodierer { get; } = dekodierer;
        public int Platz { get; } = platz;
        public int Netz { get; set; } = netz;
        public bool Durchsage { get; set; } = durchsage;
        public long LetzteUs { get; set; }
    }

    private readonly Mischer _mischer;
    private readonly Func<IDekodierer> _dekodiererFabrik;
    private readonly JitterpufferEinstellungen? _jitterpuffer;
    private readonly Func<long> _uhrUs;
    private readonly Lock _sperre = new();
    private readonly Dictionary<uint, Sprecher> _sprecher = [];
    private readonly Dictionary<uint, float> _funkerPegel = [];
    private readonly List<(long Marke, IDekodierer Dekodierer)> _freigeben = [];
    private readonly IDekodierer _hoerprobeDekodierer;
    private long _abgewiesen;

    public Empfangsseite(Func<IDekodierer> dekodiererFabrik, JitterpufferEinstellungen? jitterpuffer, Func<long> uhrUs, MischerEinstellungen? mischer = null)
    {
        ArgumentNullException.ThrowIfNull(dekodiererFabrik);
        ArgumentNullException.ThrowIfNull(uhrUs);
        _dekodiererFabrik = dekodiererFabrik;
        _jitterpuffer = jitterpuffer;
        _uhrUs = uhrUs;
        _mischer = new Mischer(mischer);
        _hoerprobeDekodierer = dekodiererFabrik();
        Hoerprobe = new Jitterpuffer(_hoerprobeDekodierer, jitterpuffer, uhrUs);
        if (_mischer.QuelleHinzufuegen(Hoerprobe, HoerprobeNetz, funk: false) < 0)
            throw new InvalidOperationException("Kein Platz für die Hörprobe im Mischer.");
    }

    private readonly Dictionary<uint, (bool Stumm, bool NichtDaempfen)> _vorgaben = [];

    /// <summary>
    /// Wahl am gelben Zeichen (Klangstufe C2a), je Konto gemerkt für diese Sitzung und für künftige Sprecher mit derselben Konto-ID; aus jedem
    /// Thread. "Wieder normal" hebt beides auf und vergisst Urteil und Dämpfung.
    /// </summary>
    public void Handeln(uint konto, FunkerAktion aktion)
    {
        lock (_sperre)
        {
            var v = _vorgaben.GetValueOrDefault(konto);
            v = aktion switch
            {
                FunkerAktion.Stumm => v with { Stumm = true },
                FunkerAktion.NichtMehrDaempfen => v with { NichtDaempfen = true },
                _ => default,
            };
            if (v == default) _vorgaben.Remove(konto);
            else _vorgaben[konto] = v;
            if (!_sprecher.TryGetValue(konto, out var s)) return;
            _mischer.SetzeWaechterVorgabe(s.Platz, v.Stumm, v.NichtDaempfen);
            if (aktion == FunkerAktion.WiederNormal) _mischer.WaechterZuruecksetzen(s.Platz);
        }
    }

    /// <summary>
    /// Die Funker mit Urteil, Dämpfung oder Wahl am gelben Zeichen nach <paramref name="ziel"/> (Klangstufe C2a); <paramref name="zuletzt"/> ist der
    /// zuletzt gedämpfte Funker (0: keiner). Aus jedem Thread.
    /// </summary>
    public void Dauersender(List<FunkerDaempfung> ziel, out uint zuletzt)
    {
        ziel.Clear();
        zuletzt = 0;
        long folge = 0;
        lock (_sperre)
        {
            foreach (var (konto, s) in _sprecher)
            {
                var z = _mischer.Zustand(s.Platz);
                if (z.GedaempftFolge > folge)
                {
                    folge = z.GedaempftFolge;
                    zuletzt = konto;
                }
                if (z.Urteil == DauersendUrteil.Keins && !(z.DaempfungDb < -0.05f) && !z.Stumm && !z.NichtDaempfen) continue;
                ziel.Add(new FunkerDaempfung(konto, z.Urteil, z.DaempfungDb, z.Wort, z.Stumm, z.NichtDaempfen));
            }
        }
    }

    public Mischer Mischer => _mischer;

    /// <summary>Jitterpuffer der eigenen Stimme; einziger Schreiber ist die Sendekette.</summary>
    public Jitterpuffer Hoerprobe { get; }

    /// <summary>Pakete, die mangels Mischerplatz oder wegen eines vollen Übergaberings nicht angenommen wurden.</summary>
    public long Abgewiesen => Interlocked.Read(ref _abgewiesen);

    public int AnzahlSprecher
    {
        get
        {
            lock (_sperre) return _sprecher.Count;
        }
    }

    /// <summary>
    /// Ein Sprachpaket eines Sprechers übergeben. <paramref name="netz"/> ist das Mischer-Netz (0 bis 62) für
    /// Lautstärke und Panorama, <paramref name="ankunftUs"/> der Empfangsstempel auf der Uhr der Maschine.
    /// </summary>
    public bool Empfangen(uint konto, int netz, ushort sprechphase, uint frameNr, Sprachkennzeichen kennzeichen, ReadOnlySpan<byte> opus, long ankunftUs, bool durchsage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(netz);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(netz, HoerprobeNetz);
        lock (_sperre)
        {
            if (!_sprecher.TryGetValue(konto, out var s))
            {
                s = Anlegen(konto, netz, durchsage);
                if (s is null)
                {
                    _abgewiesen++;
                    return false;
                }
            }
            if (s.Netz != netz)
            {
                _mischer.SetzeNetz(s.Platz, netz);
                s.Netz = netz;
            }
            if (s.Durchsage != durchsage)
            {
                _mischer.SetzeDurchsage(s.Platz, durchsage);
                s.Durchsage = durchsage;
            }
            s.LetzteUs = ankunftUs;
            if (s.Puffer.Einreihen(ankunftUs, sprechphase, frameNr, kennzeichen, opus)) return true;
            _abgewiesen++;
            return false;
        }
    }

    /// <summary>Pegel für einen Funker in dB (A4); gilt sofort und für künftige Sprecher mit dieser Konto-ID.</summary>
    public void PegelSetzen(uint konto, float db)
    {
        lock (_sperre)
        {
            _funkerPegel[konto] = db;
            if (_sprecher.TryGetValue(konto, out var s)) _mischer.SetzeFunkerPegel(s.Platz, db);
        }
    }

    /// <summary>
    /// Empfangsanzeige aller bekannten Sprecher nach <paramref name="ziel"/> (höchstens dessen Länge); setzt Spitze und
    /// Aktivität zurück. Rückgabe: Zahl der Einträge. Allokiert nicht.
    /// </summary>
    public int PegelLesen(Span<SprecherPegel> ziel)
    {
        lock (_sperre)
        {
            var n = 0;
            foreach (var (konto, s) in _sprecher)
            {
                if (n >= ziel.Length) break;
                ziel[n++] = new SprecherPegel(konto, s.Netz, s.Messung.SpitzeHolen(), s.Messung.AktivHolen());
            }
            return n;
        }
    }

    /// <summary>Entfernt ruhende Sprecher und gibt Dekodierer frei, die der Mischer nicht mehr lesen kann.</summary>
    public void Aufraeumen()
    {
        var jetzt = _uhrUs();
        lock (_sperre)
        {
            List<uint>? weg = null;
            foreach (var (konto, s) in _sprecher)
            {
                if (jetzt - s.LetzteUs > RuheUs) (weg ??= []).Add(konto);
            }
            if (weg is not null)
            {
                foreach (var konto in weg)
                {
                    var s = _sprecher[konto];
                    _sprecher.Remove(konto);
                    _freigeben.Add((_mischer.QuelleEntfernen(s.Platz), s.Dekodierer));
                }
            }
            for (var i = _freigeben.Count - 1; i >= 0; i--)
            {
                if (!_mischer.QuelleFreigebbar(_freigeben[i].Marke)) continue;
                _freigeben[i].Dekodierer.Dispose();
                _freigeben.RemoveAt(i);
            }
        }
    }

    /// <summary>Erst aufrufen, wenn kein Audio-Thread mehr mischt.</summary>
    public void Dispose()
    {
        lock (_sperre)
        {
            foreach (var s in _sprecher.Values) s.Dekodierer.Dispose();
            _sprecher.Clear();
            foreach (var (_, d) in _freigeben) d.Dispose();
            _freigeben.Clear();
        }
        _hoerprobeDekodierer.Dispose();
        _mischer.SchaetzerFreigeben();
    }

    private Sprecher? Anlegen(uint konto, int netz, bool durchsage)
    {
        var dekodierer = _dekodiererFabrik();
        var puffer = new Jitterpuffer(dekodierer, _jitterpuffer, _uhrUs);
        var messung = new MessQuelle(puffer);
        var pegel = _funkerPegel.TryGetValue(konto, out var db) ? db : 0f;
        var platz = _mischer.QuelleHinzufuegen(messung, netz, pegel, durchsage);
        if (platz < 0)
        {
            dekodierer.Dispose();
            return null;
        }
        var s = new Sprecher(puffer, messung, dekodierer, platz, netz, durchsage);
        _sprecher[konto] = s;
        if (_vorgaben.TryGetValue(konto, out var v)) _mischer.SetzeWaechterVorgabe(platz, v.Stumm, v.NichtDaempfen);
        return s;
    }
}
