// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Funk.Protokoll.Sprache;
using Funk.Server.Betrieb;

namespace Funk.Server.Sprachpfad;

/// <summary>Ein versiegeltes Datagramm auf dem Weg zum Socket, aus einem Vorrat.</summary>
internal sealed class Ausgangspaket
{
    public readonly byte[] Puffer = new byte[Sprachkanal.MaxDatagramm];
    public int Laenge;
    public SocketAddress? Ziel;
    public int Socket;
    public int Rang;
    public long Eingereiht;
    public bool V6;
}

/// <summary>
/// Pacer und Sende-Thread (5.14, 7.4): Der Relay-Thread reiht fertig versiegelte Pakete ein, ein eigener Thread macht
/// die Systemaufrufe. Ein Token-Topf auf Leitungsbytes (UDP-Nutzlast + 28 bzw. 48 + Zuschlag) begrenzt auf
/// <c>ausgangMbit</c>, Stoß höchstens 5 ms Leitungszeit; so ist der Server die Engstelle und seine Rangfolge wirkt:
/// (0) PFAD_PRUEFUNG und KEEPALIVE_ECHO, (1) Sprache GLOBAL oder DURCHSAGE, (2) Sprache NETZ. Ein Paket, das länger
/// als 20 ms wartet, wird verworfen statt verspätet gesendet.
/// </summary>
internal sealed class Ausgang : IDisposable
{
    public const int Raenge = 3;
    public static readonly long HoechstalterTakte = Zeit.TakteAusMs(20);

    private readonly ConcurrentQueue<Ausgangspaket>[] _schlangen = [new(), new(), new()];
    private readonly ConcurrentQueue<Ausgangspaket> _vorrat = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly IReadOnlyList<Socket> _sockets;
    private readonly double _bytesJeTakt;
    private readonly double _stoss;
    private readonly int _zuschlag;
    private readonly Thread _faden;
    private readonly Action<Exception>? _fehler;
    private volatile bool _stop;
    private double _stand;
    private long _letzter;
    private long _wartezeitSumme;
    private long _wartezeitAnzahl;
    private long _bytesImFenster;
    private long _fensterStart;
    private long _letzterFehlerbericht = -Stopwatch.Frequency;

    /// <param name="fehler">Bekommt unerwartete Ausnahmen des Sende-Threads (höchstens eine je Sekunde).</param>
    public Ausgang(IReadOnlyList<Socket> sockets, double ausgangMbit, int zuschlag, Action<Exception>? fehler = null)
    {
        _sockets = sockets;
        _fehler = fehler;
        var bytesJeSekunde = ausgangMbit * 1_000_000 / 8;
        _bytesJeTakt = bytesJeSekunde / Stopwatch.Frequency;
        _stoss = Math.Max(2 * Sprachkanal.MaxDatagramm, bytesJeSekunde * 0.005);
        _stand = _stoss;
        _zuschlag = zuschlag;
        _letzter = Zeit.Takte();
        _fensterStart = _letzter;
        _faden = new Thread(Schleife) { IsBackground = true, Name = "Funk-Ausgang", Priority = ThreadPriority.AboveNormal };
    }

    public long Gesendet;
    public long Altersverwurf;
    public long Sendefehler;

    /// <summary>Gemessene Ausgangsrate des letzten Fensters.</summary>
    public double MbitGemessen { get; private set; }

    public void Starten() => _faden.Start();

    public Ausgangspaket Leihen() => _vorrat.TryDequeue(out var p) ? p : new Ausgangspaket();

    public void Zurueckgeben(Ausgangspaket p)
    {
        p.Ziel = null;
        _vorrat.Enqueue(p);
    }

    /// <summary>Reiht ein Paket ein (nur vom Relay-Thread).</summary>
    public void Einreihen(Ausgangspaket p)
    {
        p.Eingereiht = Zeit.Takte();
        _schlangen[Math.Clamp(p.Rang, 0, Raenge - 1)].Enqueue(p);
        _signal.Release();
    }

    /// <summary>Mittlere Wartezeit im Pacer seit der letzten Abfrage in ms (Überlaststufe 1, 7.4).</summary>
    public double WartezeitAbholen()
    {
        var n = Interlocked.Exchange(ref _wartezeitAnzahl, 0);
        var s = Interlocked.Exchange(ref _wartezeitSumme, 0);
        return n == 0 ? 0 : Zeit.MsAusTakten(s / n);
    }

    private void Schleife()
    {
        while (!_stop)
        {
            try
            {
                _signal.Wait(50);
                while (!_stop && Naechstes(out var p))
                {
                    try
                    {
                        Senden(p);
                    }
                    finally
                    {
                        Zurueckgeben(p);
                    }
                }
                Fenster();
            }
            catch (ObjectDisposedException)
            {
                // Nach Dispose (Join abgelaufen): der Thread endet still.
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Eine unbehandelte Ausnahme in einem eigenen Thread beendet den ganzen Prozess. Das Paket ist verloren,
                // der Sprachpfad sendet weiter.
                Interlocked.Increment(ref Sendefehler);
                var jetzt = Zeit.Takte();
                if (jetzt - _letzterFehlerbericht >= Stopwatch.Frequency)
                {
                    _letzterFehlerbericht = jetzt;
                    _fehler?.Invoke(ex);
                }
            }
        }
    }

    private bool Naechstes(out Ausgangspaket paket)
    {
        foreach (var s in _schlangen)
            if (s.TryDequeue(out paket!)) return true;
        paket = null!;
        return false;
    }

    private void Senden(Ausgangspaket p)
    {
        var leitung = p.Laenge + (p.V6 ? 48 : 28) + _zuschlag;
        var warten = new SpinWait();
        while (true)
        {
            var jetzt = Zeit.Takte();
            if (jetzt - p.Eingereiht > HoechstalterTakte)
            {
                Interlocked.Increment(ref Altersverwurf);
                return;
            }
            Auffuellen(jetzt);
            if (_stand >= leitung) break;
            // Fehlbetrag in ms: länger als 2 ms schlafen (Linux ~1 ms Auflösung); kürzer drehen, denn Thread.Sleep(1)
            // dauert unter Windows ~15 ms und würde den Topf (Stoß 5 ms) nie ausschöpfen.
            var fehlMs = (leitung - _stand) / _bytesJeTakt * 1000.0 / Stopwatch.Frequency;
            if (fehlMs >= 2 && !OperatingSystem.IsWindows()) Thread.Sleep(1);
            else warten.SpinOnce(sleep1Threshold: -1);
        }
        _stand -= leitung;
        Interlocked.Add(ref _wartezeitSumme, Zeit.Takte() - p.Eingereiht);
        Interlocked.Increment(ref _wartezeitAnzahl);
        try
        {
            _sockets[p.Socket].SendTo(p.Puffer.AsSpan(0, p.Laenge), SocketFlags.None, p.Ziel!);
            Interlocked.Increment(ref Gesendet);
            _bytesImFenster += leitung;
        }
        catch (SocketException)
        {
            Interlocked.Increment(ref Sendefehler);
        }
        catch (ObjectDisposedException)
        {
            _stop = true;
        }
    }

    private void Auffuellen(long jetzt)
    {
        if (jetzt <= _letzter) return;
        _stand = Math.Min(_stoss, _stand + (jetzt - _letzter) * _bytesJeTakt);
        _letzter = jetzt;
    }

    private void Fenster()
    {
        var jetzt = Zeit.Takte();
        var dauer = jetzt - _fensterStart;
        if (dauer < Stopwatch.Frequency) return;
        MbitGemessen = _bytesImFenster * 8.0 / 1_000_000 / ((double)dauer / Stopwatch.Frequency);
        _bytesImFenster = 0;
        _fensterStart = jetzt;
    }

    public void Dispose()
    {
        _stop = true;
        _signal.Release();
        if (_faden.IsAlive) _faden.Join(1000);
        _signal.Dispose();
    }
}
