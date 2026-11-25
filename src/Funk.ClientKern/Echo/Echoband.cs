// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Security.Cryptography;
using Funk.Protokoll.Sprache;

namespace Funk.ClientKern.Echo;

/// <summary>Ein empfangenes Sprachpaket, aus <see cref="SprachEmpfang"/> kopiert. <see cref="Netz"/> ist das Echo-Netz, über das es kam.</summary>
public readonly record struct EchoPaket(uint Absender, ushort Sprechphase, uint FrameNr, Kennzeichen Kennzeichen, Spruchart Art, ushort Netz,
    byte[] Opus, long AnkunftMs)
{
    public bool IstEnde => (Kennzeichen & Kennzeichen.Ende) != 0;
}

/// <summary>Ein Frame der Wiedergabe: Opus unverändert, Pre-roll-Kennzeichen und Sendezeit ab Beginn der Wiedergabe.</summary>
public readonly record struct EchoFrame(byte[] Opus, bool Preroll, long VersatzMs);

/// <summary>Eine aufgenommene Phase, fertig zur Wiedergabe: Frames nach Frame-Nr geordnet, Zeitabstände aus der Frame-Nr.</summary>
public sealed record Echophase(uint Absender, ushort Sprechphase, ushort Netz, bool Va, IReadOnlyList<EchoFrame> Frames, bool Gekuerzt)
{
    /// <summary>Hörbare Länge (20 ms je Frame).</summary>
    public int DauerMs => Frames.Count * 20;
}

/// <summary>Zustand des Echobands: immer genau eine Phase zur Zeit.</summary>
public enum EchoZustand
{
    Bereit,
    Aufnahme,
    Pause,
    Wiedergabe,
}

/// <summary>Was mit einem Paket geschah.</summary>
public enum EchoUrteil
{
    /// <summary>Erstes Audio-Paket einer neuen Phase.</summary>
    Begonnen,
    Aufgenommen,

    /// <summary>ENDE der aufgenommenen Phase; die Pause läuft (oder die Phase wurde verworfen, siehe Zähler).</summary>
    Beendet,

    /// <summary>Pause oder Wiedergabe läuft, oder das Paket kam an, bevor das Band wieder bereit war.</summary>
    Beschaeftigt,

    /// <summary>Der eigene Spruch (der Server schickt ihn nie zurück, 6.4; hier nur doppelt gesichert).</summary>
    Eigen,

    /// <summary>Keine NETZ-Sprache im Echo-Netz (Durchsage, Global, anderes Netz).</summary>
    KeinNetz,

    /// <summary>Ein anderer Sprecher oder eine andere Phase, während eine aufgenommen wird.</summary>
    AnderePhase,

    /// <summary>ENDE ohne vorherige Aufnahme.</summary>
    OhneAudio,

    /// <summary>Die Phase hat ihre Höchstdauer erreicht; der Rest wird nicht aufgenommen.</summary>
    Voll,
    Doppelt,

    /// <summary>Eine Phase ohne BEGINN und ohne Pre-roll am Anfang: Rest einer Phase, die während der Wiedergabe begann.</summary>
    Mitten,

    /// <summary>Der gerade zurückgespielte Sprecher setzt vor Ablauf von <see cref="EchoOptionen.Nachlauf"/> neu an.</summary>
    Nachlauf,
}

/// <summary>
/// Die Logik des Echo-Funkers ohne Netz und ohne Uhr (Zeit kommt als Millisekunden von außen, Tests laufen mit simulierter
/// Zeit). Ablauf: <see cref="EchoZustand.Bereit"/> -> erstes Audio-Paket einer NETZ-Phase im Echo-Netz ->
/// <see cref="EchoZustand.Aufnahme"/> (nur diese Phase, höchstens <see cref="EchoOptionen.MaxFrames"/> Frames) -> ENDE oder
/// Leerlauf -> <see cref="EchoZustand.Pause"/> -> nach der Pause liefert <see cref="Takt"/> die Phase ->
/// <see cref="EchoZustand.Wiedergabe"/> -> <see cref="WiedergabeBeendet"/> -> wieder bereit.
/// <para>
/// Schleifenschutz: Der eigene Absender wird nie aufgenommen; ein Paket, das ankam, bevor das Band wieder bereit war, gilt
/// als verspätet; eine Aufnahme beginnt nur mit BEGINN (vom Server je Empfänger gestempelt, 6.4) oder einem Pre-roll-Frame,
/// also nie mitten in einer Phase, die während der Wiedergabe begann; der zurückgespielte Sprecher ruht danach
/// <see cref="EchoOptionen.Nachlauf"/> lang (Rückkopplung über Lautsprecher, neu kodiert und damit mit anderen Bytes); und
/// eine Phase, deren Frames zur Hälfte oder mehr bitgleich mit kürzlich selbst gesendeten sind, ist das eigene Echo auf dem
/// Rückweg über einen zweiten Echo-Funker und wird verworfen. Die Opus-Pakete werden nicht dekodiert.
/// </para>
/// Nicht threadsicher; gehört einem Arbeitsfaden.
/// </summary>
public sealed class Echoband
{
    /// <summary>Höchstens so viele Pre-roll-Frames am Anfang (5.6).</summary>
    public const int MaxPreroll = SprechphasenGeber.MaxPreroll;

    private const int FrameMs = 20;

    private readonly EchoOptionen _o;
    private readonly Action<string>? _log;
    private readonly SortedDictionary<int, (byte[] Opus, bool Preroll)> _frames = [];
    private readonly Dictionary<ulong, long> _gesendet = [];
    private readonly Queue<(long Ms, ulong Hash)> _gesendetFolge = new();
    private uint _absender;
    private ushort _phase;
    private ushort _netz;
    private bool _va;
    private uint _basis;
    private int _min;
    private int _max;
    private long _letztesMs;
    private long _faelligMs;
    private long _bereitSeit = long.MinValue;
    private uint _ruhendAbsender;
    private long _ruhendBis = long.MinValue;
    private bool _gekuerzt;
    private Echophase? _laufend;

    public Echoband(EchoOptionen? optionen = null, Action<string>? log = null)
    {
        _o = optionen ?? new EchoOptionen();
        _o.Pruefen();
        _log = log;
    }

    public EchoZustand Zustand { get; private set; }

    /// <summary>Konto-ID des Echo-Funkers (aus Welcome); 0 = noch unbekannt.</summary>
    public uint EigenesKonto { get; set; }

    /// <summary>Aufgenommene und zur Wiedergabe gegebene Phasen.</summary>
    public int Aufnahmen => Volatile.Read(ref _aufnahmen);

    public int Wiedergaben => Volatile.Read(ref _wiedergaben);

    /// <summary>Als eigenes Echo erkannte und verworfene Phasen.</summary>
    public int Schleifen => Volatile.Read(ref _schleifen);

    /// <summary>Phasen, die über die Höchstdauer hinausgingen.</summary>
    public int Gekuerzt => Volatile.Read(ref _gekuerztZahl);

    private int _aufnahmen;
    private int _wiedergaben;
    private int _schleifen;
    private int _gekuerztZahl;

    /// <summary>Nimmt ein Paket entgegen. <paramref name="jetztMs"/> ist die monotone Zeit der Verarbeitung.</summary>
    public EchoUrteil Paket(in EchoPaket p, long jetztMs)
    {
        if (Zustand is EchoZustand.Pause or EchoZustand.Wiedergabe || p.AnkunftMs < _bereitSeit) return EchoUrteil.Beschaeftigt;
        if (EigenesKonto != 0 && p.Absender == EigenesKonto) return EchoUrteil.Eigen;
        if (p.Art != Spruchart.Netz || p.Netz == 0) return EchoUrteil.KeinNetz;

        if (Zustand == EchoZustand.Bereit)
        {
            if (p.IstEnde) return EchoUrteil.OhneAudio;
            if ((p.Kennzeichen & (Kennzeichen.Beginn | Kennzeichen.Preroll)) == 0) return EchoUrteil.Mitten;
            if (p.Absender == _ruhendAbsender && p.AnkunftMs < _ruhendBis) return EchoUrteil.Nachlauf;
            _frames.Clear();
            _absender = p.Absender;
            _phase = p.Sprechphase;
            _netz = p.Netz;
            _va = (p.Kennzeichen & Kennzeichen.Va) != 0;
            _basis = p.FrameNr;
            _min = 0;
            _max = 0;
            _gekuerzt = false;
            _frames[0] = (p.Opus, (p.Kennzeichen & Kennzeichen.Preroll) != 0);
            _letztesMs = jetztMs;
            Zustand = EchoZustand.Aufnahme;
            return EchoUrteil.Begonnen;
        }

        // Aufnahme: nur diese eine Phase.
        if (p.Absender != _absender || p.Sprechphase != _phase) return EchoUrteil.AnderePhase;
        _letztesMs = jetztMs;
        if (p.IstEnde)
        {
            Abschliessen(jetztMs, "ENDE");
            return EchoUrteil.Beendet;
        }
        var d = unchecked((int)(p.FrameNr - _basis));
        if (_frames.ContainsKey(d)) return EchoUrteil.Doppelt;
        var min = Math.Min(_min, d);
        var max = Math.Max(_max, d);
        if (_frames.Count >= _o.MaxFrames || (long)max - min + 1 > _o.MaxFrames)
        {
            if (!_gekuerzt) _log?.Invoke($"Phase erreicht die Höchstdauer von {_o.MaxPhase.TotalSeconds:0.#} s; der Rest wird nicht aufgenommen");
            _gekuerzt = true;
            return EchoUrteil.Voll;
        }
        _min = min;
        _max = max;
        _frames[d] = (p.Opus, (p.Kennzeichen & Kennzeichen.Preroll) != 0);
        return EchoUrteil.Aufgenommen;
    }

    /// <summary>
    /// Fristen: Leerlauf beendet eine Aufnahme ohne ENDE, das Ende der Pause gibt die Phase zur Wiedergabe heraus (dann
    /// <see cref="EchoZustand.Wiedergabe"/>). Räumt das Gedächtnis der gesendeten Frames auf.
    /// </summary>
    public Echophase? Takt(long jetztMs)
    {
        Vergessen(jetztMs);
        if (Zustand == EchoZustand.Aufnahme && jetztMs - _letztesMs >= (long)_o.Leerlauf.TotalMilliseconds)
            Abschliessen(jetztMs, "Leerlauf");
        if (Zustand != EchoZustand.Pause || jetztMs < _faelligMs) return null;
        Zustand = EchoZustand.Wiedergabe;
        _laufend = PhaseBauen();
        _frames.Clear();
        return _laufend;
    }

    /// <summary>Die Wiedergabe ist vorbei (auch abgebrochen): Frames ins Gedächtnis, das Band ist wieder bereit.</summary>
    public void WiedergabeBeendet(long jetztMs, bool vollstaendig = true)
    {
        if (Zustand != EchoZustand.Wiedergabe) return;
        if (_laufend is { } ph)
        {
            foreach (var f in ph.Frames) Merken(Hash(f.Opus), jetztMs);
            if (vollstaendig) Interlocked.Increment(ref _wiedergaben);
            _ruhendAbsender = ph.Absender;
            _ruhendBis = jetztMs + (long)_o.Nachlauf.TotalMilliseconds;
        }
        _laufend = null;
        Zustand = EchoZustand.Bereit;
        _bereitSeit = jetztMs;
    }

    private void Abschliessen(long jetztMs, string anlass)
    {
        var anzahl = _frames.Count;
        var eigene = 0;
        foreach (var (_, f) in _frames)
            if (_gesendet.ContainsKey(Hash(f.Opus))) eigene++;
        if (anzahl > 0 && 2 * eigene >= anzahl)
        {
            Interlocked.Increment(ref _schleifen);
            _log?.Invoke($"Phase von Konto {_absender} verworfen: {eigene} von {anzahl} Frames sind das eigene Echo (Schleifenschutz)");
            _frames.Clear();
            Zustand = EchoZustand.Bereit;
            _bereitSeit = jetztMs;
            return;
        }
        if (_gekuerzt) Interlocked.Increment(ref _gekuerztZahl);
        Interlocked.Increment(ref _aufnahmen);
        _faelligMs = jetztMs + (long)_o.Pause.TotalMilliseconds;
        Zustand = EchoZustand.Pause;
        _log?.Invoke($"Aufnahme von Konto {_absender} beendet ({anlass}): {anzahl} Frames, {anzahl * FrameMs} ms{(_gekuerzt ? ", gekürzt" : "")}; Wiedergabe in {_o.Pause.TotalMilliseconds:0} ms");
    }

    /// <summary>
    /// Ordnet nach Frame-Nr. Führende Pre-roll-Frames einer VA-Phase (höchstens 7) gehen als Stoß zum Zeitpunkt 0, wie beim
    /// Sprecher; jeder weitere Frame im 20-ms-Raster seiner Frame-Nr. So bleiben Pausen durch verlorene Pakete erhalten,
    /// Jitter und Umsortierung des Hinwegs aber nicht.
    /// </summary>
    private Echophase PhaseBauen()
    {
        var liste = new List<EchoFrame>(_frames.Count);
        int? anker = null;
        var preroll = 0;
        var letzterPreroll = 0;
        foreach (var (d, f) in _frames)
        {
            if (anker is null && _va && f.Preroll && preroll < MaxPreroll && preroll == liste.Count)
            {
                preroll++;
                letzterPreroll = d;
                liste.Add(new EchoFrame(f.Opus, true, 0));
                continue;
            }
            // Nach dem Stoß geht es im Raster ab dem letzten Pre-roll-Frame weiter (der nächste folgt 20 ms später);
            // ohne Pre-roll beginnt die Wiedergabe mit dem ersten Frame.
            anker ??= preroll > 0 ? letzterPreroll : d;
            liste.Add(new EchoFrame(f.Opus, false, (long)(d - anker.Value) * FrameMs));
        }
        return new Echophase(_absender, _phase, _netz, _va, liste, _gekuerzt);
    }

    private void Merken(ulong hash, long jetztMs)
    {
        _gesendet[hash] = jetztMs;
        _gesendetFolge.Enqueue((jetztMs, hash));
    }

    private void Vergessen(long jetztMs)
    {
        var grenze = jetztMs - (long)_o.Gedaechtnis.TotalMilliseconds;
        while (_gesendetFolge.TryPeek(out var e) && e.Ms < grenze)
        {
            _gesendetFolge.Dequeue();
            if (_gesendet.TryGetValue(e.Hash, out var zuletzt) && zuletzt == e.Ms) _gesendet.Remove(e.Hash);
        }
    }

    /// <summary>64 Bit aus SHA-256 über die Opus-Nutzlast; Kollisionen sind ohne Belang.</summary>
    internal static ulong Hash(ReadOnlySpan<byte> opus)
    {
        Span<byte> h = stackalloc byte[32];
        SHA256.HashData(opus, h);
        return BinaryPrimitives.ReadUInt64BigEndian(h);
    }
}
