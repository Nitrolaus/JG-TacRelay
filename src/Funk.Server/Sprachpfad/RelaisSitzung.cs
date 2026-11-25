// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using Funk.Protokoll.Schutz;
using Funk.Protokoll.Sprache;
using Funk.Server.Betrieb;

namespace Funk.Server.Sprachpfad;

/// <summary>Neue UDP-Quelladresse einer Sitzung, die noch geprüft wird (Begriffe, 5.10).</summary>
internal sealed class Kandidat
{
    public Adresse A;
    public SocketAddress Sock = null!;
    public int Socket;
    public readonly byte[] Challenge = new byte[Steuerpakete.Laenge];
    public long ChallengeZeit;
    public long LetztePruefung;
    public long FensterStart;
    public int ImFenster;
    public bool Bestaetigt;
    public bool GegenprobeGesendet;
}

/// <summary>
/// Stand einer Sitzung im Relay-Thread: Schlüsselbund, Adressbindung und Kandidaten (5.10), Budgets (5.9 Schritt 4),
/// Kopierprüfung und Messwerte (7.8). Gehört allein dem Relay-Thread (5.14).
/// </summary>
internal sealed class RelaisSitzung : IDisposable
{
    public const int MaxKandidaten = 2;
    public const int MaxFremdadressen = 16;
    public const int MaxKopierzaehler = 256;
    public static readonly long ChallengeGueltigTakte = Zeit.TakteAusMs(3000);
    public static readonly long WiederholungTakte = Zeit.TakteAusMs(250);
    public static readonly long UmbindungsfristTakte = Zeit.TakteAusMs(500);
    public static readonly long RueckkehrTakte = Zeit.TakteAusMs(60_000);
    public static readonly long SperreTakte = Zeit.TakteAusMs(60_000);
    public static readonly long SekundeTakte = Zeit.TakteAusMs(1000);

    public RelaisSitzung(uint id, uint kontoId, Adresse tlsIp, ServerSchluesselbund bund, SitzungsZaehler zaehler, long jetzt)
    {
        Id = id;
        KontoId = kontoId;
        TlsIp = tlsIp.OhnePort;
        Bund = bund;
        Zaehler = zaehler;
        // 5.9 Schritt 4: gebunden und Tunnel je 200 Versuche/s, fremd je Adresse 60/s Stoß 30, zusammen höchstens 200/s.
        Gebunden = new TokenEimer(200, 200, jetzt);
        TunnelBudget = new TokenEimer(200, 200, jetzt);
        FremdGesamt = new TokenEimer(200, 200, jetzt);
        Messwerte = new Messwerte(jetzt);
    }

    public uint Id { get; }
    public uint KontoId { get; }
    public Adresse TlsIp { get; }
    public ServerSchluesselbund Bund { get; }
    public SitzungsZaehler Zaehler { get; }
    public TokenEimer Gebunden { get; }
    public TokenEimer TunnelBudget { get; }
    public TokenEimer FremdGesamt { get; }
    public Messwerte Messwerte { get; }

    /// <summary>Antwort auf VoiceKeys START ist da: erst dann gilt die Sitzung im Sprachkanal (5.9 Schritt 3).</summary>
    public bool Aktiv { get; set; }

    /// <summary>Tunnelmodus (5.12): runter nur noch im Steuerkanal.</summary>
    public bool Tunnel { get; set; }

    public bool HatB { get; private set; }
    public Adresse B { get; private set; }
    public SocketAddress? BSock { get; private set; }
    public int BSocket { get; private set; }
    public long LetzteAuthVonB { get; set; }

    public readonly Kandidat?[] Kandidaten = new Kandidat?[MaxKandidaten];
    public readonly List<(Adresse A, SocketAddress Sock, int Socket, long Bis)> Rueckkehr = [];
    public readonly HashSet<ulong> KopierZaehler = [];
    public readonly Dictionary<Adresse, long> Gesperrt = [];
    public readonly byte[] Gegenprobe = new byte[Steuerpakete.Laenge];
    public bool GegenprobeOffen;
    public long GegenprobeZeit;

    private readonly (Adresse A, TokenEimer Eimer, long Zuletzt)[] _fremd = new (Adresse, TokenEimer, long)[MaxFremdadressen];
    private int _fremdAnzahl;

    public bool KandidatOffen => Kandidaten[0] is not null || Kandidaten[1] is not null;

    public int OffeneKandidaten => (Kandidaten[0] is null ? 0 : 1) + (Kandidaten[1] is null ? 0 : 1);

    public bool IstB(Adresse a) => HatB && B == a;

    public Kandidat? KandidatVon(Adresse a)
    {
        foreach (var k in Kandidaten)
            if (k is not null && k.A == a) return k;
        return null;
    }

    public bool IstGesperrt(Adresse a, long jetzt)
    {
        if (Gesperrt.Count == 0 || !Gesperrt.TryGetValue(a, out var bis)) return false;
        if (jetzt < bis) return true;
        Gesperrt.Remove(a);
        return false;
    }

    /// <summary>Fremdbudget je (Sitzung, Quelladresse); höchstens 16 Adressen, die älteste fällt heraus (5.9 Schritt 4).</summary>
    public bool FremdNehmen(Adresse a, long jetzt)
    {
        var i = 0;
        for (; i < _fremdAnzahl; i++)
            if (_fremd[i].A == a) break;
        if (i == _fremdAnzahl)
        {
            if (_fremdAnzahl < MaxFremdadressen)
            {
                i = _fremdAnzahl++;
            }
            else
            {
                i = 0;
                for (var j = 1; j < _fremdAnzahl; j++)
                    if (_fremd[j].Zuletzt < _fremd[i].Zuletzt) i = j;
            }
            _fremd[i] = (a, new TokenEimer(30, 60, jetzt), jetzt);
        }
        _fremd[i].Zuletzt = jetzt;
        // Beide Töpfe müssen reichen; ein Verwurf kostet keinen von beiden.
        _fremd[i].Eimer.Nehmen(jetzt, 0);
        FremdGesamt.Nehmen(jetzt, 0);
        if (_fremd[i].Eimer.Stand < 1 || FremdGesamt.Stand < 1) return false;
        _fremd[i].Eimer.Nehmen(jetzt);
        FremdGesamt.Nehmen(jetzt);
        return true;
    }

    /// <summary>Bindet <paramref name="a"/> als neue Adresse B; die alte gilt 60 s lang als geprüft (Rückkehr).</summary>
    public void Binden(Adresse a, SocketAddress sock, int socket, long jetzt)
    {
        if (HatB && BSock is not null && B != a)
        {
            Rueckkehr.RemoveAll(r => r.A == B || r.Bis <= jetzt);
            Rueckkehr.Add((B, BSock, BSocket, jetzt + RueckkehrTakte));
            if (Rueckkehr.Count > 4) Rueckkehr.RemoveAt(0);
        }
        HatB = true;
        B = a;
        BSock = sock;
        BSocket = socket;
        LetzteAuthVonB = jetzt;
        Kandidaten[0] = null;
        Kandidaten[1] = null;
        KopierZaehler.Clear();
        GegenprobeOffen = false;
        Rueckkehr.RemoveAll(r => r.A == a);
    }

    /// <summary>Adresse, die in den letzten 60 s gebunden war und deshalb als geprüft gilt (5.10 Rückkehr).</summary>
    public bool RueckkehrSuchen(Adresse a, long jetzt, out SocketAddress sock, out int socket)
    {
        foreach (var r in Rueckkehr)
        {
            if (r.A != a || r.Bis <= jetzt) continue;
            sock = r.Sock;
            socket = r.Socket;
            return true;
        }
        sock = null!;
        socket = 0;
        return false;
    }

    public void KandidatenVerwerfen()
    {
        Kandidaten[0] = null;
        Kandidaten[1] = null;
        KopierZaehler.Clear();
        GegenprobeOffen = false;
    }

    public void Dispose() => Bund.Dispose();
}

/// <summary>
/// Messwerte einer Sitzung in 10-s-Fenstern (7.8): hoch aus den Lücken des Zählers (alle Pakettypen, beide Wege),
/// Interarrival-Jitter nach RFC 3550 6.4.1 über Sprache einer Sprechphase ohne PREROLL, runter gesendet, Tunnelanteil,
/// Pfadwechsel, Kopierverdacht und die Werte aus dem EmpfangsBericht des Clients.
/// </summary>
internal sealed class Messwerte(long jetzt)
{
    public const long FensterMs = 10_000;

    private ulong _hoechster;
    private bool _erster = true;
    private ulong _fensterStartHoechster;
    private long _angenommen;
    private long _angenommenAlt;
    private long _umsortiert;
    private long _doppelt;
    private long _tunnel;
    private long _runter;
    private ushort _jitterPhase;
    private uint _jitterFrame;
    private long _jitterEmpfang;
    private bool _jitterGueltig;

    public long FensterBeginn { get; private set; } = jetzt;
    public double JitterMs { get; private set; }
    public long Pfadwechsel { get; set; }
    public long Kopierverdacht { get; set; }
    public double? RttMs { get; set; }
    public double? RunterVerlustProzent { get; set; }
    public double? ClientJitterMs { get; set; }
    public bool SpracheImFenster { get; private set; }
    public long SprachpaketeGesamt { get; private set; }

    /// <summary>Stand des zuletzt abgeschlossenen Fensters.</summary>
    public SitzungsMesswerteRoh Letztes { get; private set; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Verlust hoch der letzten drei Fenster in Prozent (für EIGENER_VERLUST nach 30 s).</summary>
    public Queue<double> VerlustVerlauf { get; } = new();

    public long LetzterHinweis { get; set; }
    public byte LetzterHinweisWert { get; set; } = 255;
    public long LetzteEigenerVerlustMeldung { get; set; }

    public void Angenommen(ulong zaehler, bool tunnel)
    {
        if (_erster)
        {
            _erster = false;
            _hoechster = zaehler;
            _fensterStartHoechster = zaehler == 0 ? 0 : zaehler - 1;
        }
        _angenommen++;
        if (tunnel) _tunnel++;
        if (zaehler <= _fensterStartHoechster) _angenommenAlt++;
        if (zaehler < _hoechster) _umsortiert++;
        else _hoechster = zaehler;
    }

    public void Doppelt() => _doppelt++;

    public void RunterGesendet() => _runter++;

    /// <summary>RFC 3550 6.4.1 mit S = Frame-Nr x 20 ms und R = Empfangsstempel, nur innerhalb einer Sprechphase ohne PREROLL.</summary>
    public void Sprache(ushort phase, uint frame, bool preroll, long empfangenTakte)
    {
        SpracheImFenster = true;
        SprachpaketeGesamt++;
        if (preroll) return;
        if (_jitterGueltig && phase == _jitterPhase && frame > _jitterFrame)
        {
            var r = Zeit.MsAusTakten(empfangenTakte - _jitterEmpfang);
            var s = (long)(frame - _jitterFrame) * 20;
            var d = Math.Abs(r - s);
            JitterMs += (d - JitterMs) / 16.0;
        }
        _jitterGueltig = true;
        _jitterPhase = phase;
        _jitterFrame = frame;
        _jitterEmpfang = empfangenTakte;
    }

    /// <summary>Schließt das Fenster ab, wenn 10 s um sind. <c>true</c> heißt: neues <see cref="Letztes"/>.</summary>
    public bool FensterAbschliessen(long jetztTakte)
    {
        if (Zeit.MsAusTakten(jetztTakte - FensterBeginn) < FensterMs) return false;
        var erwartet = _erster ? 0 : (long)(_hoechster - _fensterStartHoechster);
        var neu = _angenommen - _angenommenAlt;
        var verloren = Math.Max(0, erwartet - neu);
        Letztes = new SitzungsMesswerteRoh(_angenommen, verloren, _umsortiert, _doppelt, _runter, _tunnel, erwartet);
        var prozent = erwartet <= 0 ? 0 : 100.0 * verloren / erwartet;
        VerlustVerlauf.Enqueue(prozent);
        while (VerlustVerlauf.Count > 3) VerlustVerlauf.Dequeue();
        _fensterStartHoechster = _hoechster;
        _angenommen = _angenommenAlt = _umsortiert = _doppelt = _runter = _tunnel = 0;
        SpracheImFenster = false;
        FensterBeginn = jetztTakte;
        return true;
    }

    public double VerlustProzent => Letztes.Erwartet <= 0 ? 0 : 100.0 * Letztes.Verloren / Letztes.Erwartet;

    /// <summary>Güteklasse für Praesenz (7.8): GUT unter 2 % und 20 ms, MAESSIG unter 8 % und 50 ms, sonst SCHLECHT.</summary>
    public string Guete()
    {
        var verlust = Math.Max(VerlustProzent, RunterVerlustProzent ?? 0);
        var jitter = Math.Max(JitterMs, ClientJitterMs ?? 0);
        if (verlust < 2 && jitter < 20) return "GUT";
        if (verlust < 8 && jitter < 50) return "MAESSIG";
        return "SCHLECHT";
    }
}

/// <summary>Zahlen eines abgeschlossenen Messfensters.</summary>
internal sealed record SitzungsMesswerteRoh(long Empfangen, long Verloren, long Umsortiert, long Doppelt, long RunterGesendet, long Tunnel, long Erwartet);
