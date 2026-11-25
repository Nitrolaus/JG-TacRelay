// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>Stand der Messwerte einer Sitzung (7.8), als unveränderlicher Schnappschuss.</summary>
public sealed record MesswerteStand(
    long HochUdp,
    long HochTunnel,
    long HochVerworfen,
    long RunterEmpfangen,
    long RunterVerloren,
    long RunterDoppelt,
    long RunterUmsortiert,
    long RunterUdp,
    long RunterTunnel,
    long Ungueltig,
    long Sprachpakete,
    long OpusVerworfen,
    double? RttP50Ms,
    double? RttP95Ms,
    double JitterMs,
    byte? VerlustHinweis,
    long Pfadwechsel,
    long TunnelWechsel);

/// <summary>
/// Messwerte des Clients (7.8): Verlust runter aus den Lücken des runter-Zählers (alle Pakettypen, UDP und Tunnel),
/// doppelte und umsortierte Pakete, RTT aus KEEPALIVE_ECHO (p50/p95 über die letzten 64), Interarrival-Jitter nach
/// RFC 3550 Abschnitt 6.4.1 über die runter-Sprache (S = Frame-Nr x 20 ms, nur aufeinanderfolgende Frames einer
/// Sprechphase, ohne PREROLL; berichtet wird das Maximum der Ströme der letzten 5 s). Threadsicher.
/// </summary>
public sealed class Messwerte
{
    private const int RttProben = 64;

    private readonly Lock _sperre = new();
    private readonly double[] _rtt = new double[RttProben];
    private readonly Dictionary<(uint, ushort), Strom> _stroeme = [];
    private int _rttAnzahl;
    private int _rttPos;
    private ulong _hoechster;
    private bool _erstes = true;
    private long _hochUdp, _hochTunnel, _hochVerworfen;
    private long _empfangen, _verloren, _doppelt, _umsortiert, _runterUdp, _runterTunnel, _ungueltig, _sprache, _opusVerworfen;
    private long _pfadwechsel, _tunnelwechsel;
    private int _verlustHinweis = -1;
    private (long Empfangen, long Verloren, long Doppelt, long Umsortiert) _berichtBasis;

    private sealed class Strom
    {
        public uint LetzteFrame;
        public double LetzteAnkunftMs;
        public double Jitter;
        public long LetzteMs;
    }

    internal void HochGesendet(bool tunnel)
    {
        if (tunnel) Interlocked.Increment(ref _hochTunnel);
        else Interlocked.Increment(ref _hochUdp);
    }

    internal void HochVerworfen() => Interlocked.Increment(ref _hochVerworfen);

    internal void Ungueltig() => Interlocked.Increment(ref _ungueltig);

    internal void OpusVerworfen() => Interlocked.Increment(ref _opusVerworfen);

    internal void Doppelt() => Interlocked.Increment(ref _doppelt);

    internal void Pfadwechsel() => Interlocked.Increment(ref _pfadwechsel);

    internal void TunnelWechsel() => Interlocked.Increment(ref _tunnelwechsel);

    internal void VerlustHinweisSetzen(byte prozent) => Volatile.Write(ref _verlustHinweis, prozent);

    /// <summary>Ein runter-Paket wurde angenommen: Lücken im Zähler zählen als verloren, ältere als umsortiert.</summary>
    internal void RunterAngenommen(ulong zaehler, bool tunnel)
    {
        lock (_sperre)
        {
            _empfangen++;
            if (tunnel) _runterTunnel++;
            else _runterUdp++;
            if (_erstes)
            {
                _erstes = false;
                _verloren += (long)Math.Min(zaehler, 1_000_000);
                _hoechster = zaehler;
                return;
            }
            if (zaehler > _hoechster)
            {
                _verloren += (long)Math.Min(zaehler - _hoechster - 1, 1_000_000);
                _hoechster = zaehler;
            }
            else
            {
                _umsortiert++;
                if (_verloren > 0) _verloren--;
            }
        }
    }

    /// <summary>Jitter (RFC 3550) für ein Sprachpaket; PREROLL zählt nicht mit.</summary>
    internal void Sprache(uint absender, ushort phase, uint frame, bool preroll, double ankunftMs)
    {
        lock (_sperre)
        {
            _sprache++;
            if (preroll) return;
            var ms = (long)ankunftMs;
            if (!_stroeme.TryGetValue((absender, phase), out var s))
            {
                if (_stroeme.Count > 512) Aufraeumen(ms);
                _stroeme[(absender, phase)] = new Strom { LetzteFrame = frame, LetzteAnkunftMs = ankunftMs, LetzteMs = ms };
                return;
            }
            if (frame == unchecked(s.LetzteFrame + 1))
            {
                var d = ankunftMs - s.LetzteAnkunftMs - 20.0;
                s.Jitter += (Math.Abs(d) - s.Jitter) / 16.0;
            }
            if (unchecked((int)(frame - s.LetzteFrame)) > 0)
            {
                s.LetzteFrame = frame;
                s.LetzteAnkunftMs = ankunftMs;
            }
            s.LetzteMs = ms;
        }
    }

    internal void Rtt(double ms)
    {
        lock (_sperre)
        {
            _rtt[_rttPos] = ms;
            _rttPos = (_rttPos + 1) % RttProben;
            if (_rttAnzahl < RttProben) _rttAnzahl++;
        }
    }

    /// <summary>Jitter der letzten 5 s: Maximum über die Ströme.</summary>
    public double JitterMs()
    {
        lock (_sperre) return JitterOhneSperre(Monouhr.Ms());
    }

    public MesswerteStand Stand()
    {
        lock (_sperre)
        {
            var (p50, p95) = RttQuantile();
            return new MesswerteStand(Interlocked.Read(ref _hochUdp), Interlocked.Read(ref _hochTunnel), Interlocked.Read(ref _hochVerworfen),
                _empfangen, _verloren, Interlocked.Read(ref _doppelt), _umsortiert, _runterUdp, _runterTunnel, Interlocked.Read(ref _ungueltig),
                _sprache, Interlocked.Read(ref _opusVerworfen), p50, p95, JitterOhneSperre(Monouhr.Ms()),
                Volatile.Read(ref _verlustHinweis) is var v and >= 0 ? (byte)v : null, Interlocked.Read(ref _pfadwechsel), Interlocked.Read(ref _tunnelwechsel));
        }
    }

    /// <summary>EmpfangsBericht (4.5, alle 5 s): Zähler seit dem letzten Bericht.</summary>
    internal EmpfangsBericht Bericht(ushort zeitraumMs, Verbindungsweg weg, double zielVerzoegerungMs, uint spaetVerworfen, uint unterlaeufe)
    {
        lock (_sperre)
        {
            var b = _berichtBasis;
            _berichtBasis = (_empfangen, _verloren, Interlocked.Read(ref _doppelt), _umsortiert);
            var (p50, p95) = RttQuantile();
            return new EmpfangsBericht
            {
                ZeitraumMs = zeitraumMs,
                Weg = weg,
                Runter = new RunterZaehler
                {
                    Empfangen = Kappen(_empfangen - b.Empfangen),
                    Verloren = Kappen(_verloren - b.Verloren),
                    Doppelt = Kappen(Interlocked.Read(ref _doppelt) - b.Doppelt),
                    Umsortiert = Kappen(_umsortiert - b.Umsortiert),
                },
                RttMs = weg == Verbindungsweg.Udp && p50 is { } a && p95 is { } c
                    ? new RttWerte { P50 = (ushort)Math.Clamp(Math.Round(a), 0, ushort.MaxValue), P95 = (ushort)Math.Clamp(Math.Round(c), 0, ushort.MaxValue) }
                    : null,
                JitterMs = Math.Round(JitterOhneSperre(Monouhr.Ms()), 2),
                ZielVerzoegerungMs = Math.Max(0, zielVerzoegerungMs),
                SpaetVerworfen = spaetVerworfen,
                Unterlaeufe = unterlaeufe,
            };
        }
    }

    private static uint Kappen(long wert) => (uint)Math.Clamp(wert, 0, uint.MaxValue);

    private (double?, double?) RttQuantile()
    {
        if (_rttAnzahl == 0) return (null, null);
        Span<double> werte = stackalloc double[RttProben];
        _rtt.AsSpan(0, _rttAnzahl).CopyTo(werte);
        werte = werte[.._rttAnzahl];
        werte.Sort();
        return (Quantil(werte, 0.50), Quantil(werte, 0.95));
    }

    internal static double Quantil(ReadOnlySpan<double> sortiert, double q)
    {
        if (sortiert.IsEmpty) return 0;
        var i = (int)Math.Ceiling(q * sortiert.Length) - 1;
        return sortiert[Math.Clamp(i, 0, sortiert.Length - 1)];
    }

    private double JitterOhneSperre(long jetztMs)
    {
        var j = 0.0;
        foreach (var s in _stroeme.Values)
            if (jetztMs - s.LetzteMs <= 5000 && s.Jitter > j) j = s.Jitter;
        return j;
    }

    private void Aufraeumen(long jetztMs)
    {
        foreach (var (k, s) in _stroeme.ToList())
            if (jetztMs - s.LetzteMs > 5000) _stroeme.Remove(k);
    }
}
