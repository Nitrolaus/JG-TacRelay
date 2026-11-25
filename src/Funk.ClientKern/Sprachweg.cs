// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>Ein hoch zu versiegelndes Datagramm (Klartext aus dem Pool).</summary>
internal sealed class Sendeauftrag
{
    public PaketTyp Typ;
    public byte[] Puffer = [];
    public int Laenge;

    /// <summary>Nur über UDP (ANMELDUNG, KEEPALIVE, PFAD_ANTWORT), auch im Tunnelmodus.</summary>
    public bool NurUdp;

    public long FaelligTakte;

    public static Sendeauftrag Aus(PaketTyp typ, ReadOnlySpan<byte> klartext, bool nurUdp)
    {
        var a = new Sendeauftrag { Typ = typ, Puffer = ArrayPool<byte>.Shared.Rent(Sprachkanal.MaxKlartext), Laenge = klartext.Length, NurUdp = nurUdp };
        klartext.CopyTo(a.Puffer);
        return a;
    }

    public void Freigeben()
    {
        if (Puffer.Length == 0) return;
        ArrayPool<byte>.Shared.Return(Puffer, clearArray: true);
        Puffer = [];
    }
}

/// <summary>
/// Der Sprachkanal einer Sitzung (5.1, 5.8-5.14) auf Seite des Clients: UDP-Socket mit <c>Connect</c> auf die IP der
/// TLS-Verbindung und den UDP-Port aus Welcome, gebunden an die lokale Adresse der TLS-Verbindung; ein Sende-Thread für
/// alle hoch-Datagramme (UDP und Tunnel) mit dem hoch-Schlüssel und dem hoch-Zähler, ein Empfangs-Thread für UDP.
/// Tunnelrahmen runter prüft die Leseschleife unter derselben Sperre wie der Empfangs-Thread (5.9 Schritt 8: Sperre der
/// Sitzung). Dazu Erstbindung (ANMELDUNG alle 250 ms, höchstens 12), PFAD_ANTWORT, KEEPALIVE alle 2 s, RTT,
/// Ausfallerkennung und UDP-Probe im Tunnel (5.12).
/// </summary>
internal sealed class Sprachweg : IDisposable
{
    private const int AnmeldungMax = 12;
    private const long AnmeldungAbstandMs = 250;
    private const long KeepaliveAbstandMs = 2000;
    private const long EchoFristMs = 3000;
    private const long VerdachtFristMs = 1000;
    private static readonly TimeSpan ProbeAbstandMax = TimeSpan.FromMinutes(5);

    private readonly Sitzung _sitzung;
    private readonly FunkClientOptionen _optionen;
    private readonly Messwerte _messwerte;
    private readonly uint _sitzungsId;
    private readonly IPEndPoint _server;
    private readonly IPAddress _lokal;
    private readonly Zeitlinienbuch _zeitlinien = new();
    private readonly Lock _hochSperre = new();
    private readonly Lock _runterSperre = new();
    private readonly Lock _ablauf = new();
    private readonly byte[] _klartext = new byte[Sprachkanal.MaxKlartext];
    private readonly ConcurrentQueue<Sendeauftrag> _eilig = new();
    private readonly ConcurrentQueue<Sendeauftrag> _warte = new();
    private readonly List<Sendeauftrag> _zeitgesteuert = [];
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly Dictionary<uint, long> _probeOffen = [];
    private readonly List<long> _rueckfaelle = [];
    private SendeKanal? _hoch;
    private EmpfangsKanal? _runter;
    private Socket? _udp;
    private Thread? _sendeThread;
    private Thread? _empfangsThread;
    private volatile bool _ende;
    private volatile bool _tunnel;

    // Bindung, Keepalive, Ausfall und Probe; nur unter _ablauf.
    private int _anmeldungen;
    private long _letzteAnmeldungMs = Monouhr.Nie;
    private bool _pfadPruefungErhalten;
    private bool _gebunden;
    private long _gebundenMs = -1;
    private uint _keepaliveNr;
    private long _letzterKeepaliveMs = Monouhr.Nie;
    private long _letztesEchoMs = -1;
    private long _verdachtSeitMs = -1;
    private bool _netzGeaendert;
    private bool _pingVerdacht;
    private (long Gesendet, long Empfangen, long RunterUdp, long HochUdp)? _pingVorher;
    private TunnelGrund? _tunnelGrund;
    private TimeSpan _probeAbstand;
    private long _naechsteProbeMs = long.MaxValue;
    private long _probeSeitMs = -1;
    private long _probeSauberSeitMs = -1;
    private long _probeLetzterKaMs;
    private long _letzteRueckkehrMs = -1;
    private long _letzteRotationsAnfrageMs = Monouhr.Nie;
    private long _udpRunter;
    private long _udpHoch;

    public Sprachweg(Sitzung sitzung, FunkClientOptionen optionen, Messwerte messwerte, uint sitzungsId, IPEndPoint server, IPAddress lokal)
    {
        _sitzung = sitzung;
        _optionen = optionen;
        _messwerte = messwerte;
        _sitzungsId = sitzungsId;
        _server = server;
        _lokal = lokal;
        _probeAbstand = optionen.TunnelProbeAbstand;
    }

    /// <summary>Test: UDP gesperrt (nichts geht hinaus, nichts wird angenommen), wie eine Firewall, die UDP verwirft.</summary>
    public bool UdpSperre { get; set; }

    public bool Tunnel => _tunnel;

    public bool Gebunden
    {
        get
        {
            lock (_ablauf) return _gebunden;
        }
    }

    public byte Epoche
    {
        get
        {
            lock (_hochSperre) return _hoch?.Epoche ?? 0;
        }
    }

    public bool HatSchluessel
    {
        get
        {
            lock (_hochSperre) return _hoch is not null;
        }
    }

    public int OffeneZeitlinien
    {
        get
        {
            lock (_runterSperre) return _zeitlinien.Offen;
        }
    }

    /// <summary>Zählerstände für die Ausfallerkennung über Ping (5.12).</summary>
    public long UdpRunter => Interlocked.Read(ref _udpRunter);

    public long UdpHoch => Interlocked.Read(ref _udpHoch);

    // ---- Schlüssel (5.8) ----

    /// <summary>
    /// VoiceKeys übernehmen: START nur Epoche 0 und nur einmal, ROTATION nur die nächste Epoche. Die Schlüssel gehen in neue
    /// <c>AesGcm</c>-Instanzen; den Puffer nullt der Aufrufer. <c>false</c> heißt PROTOKOLL (4.5).
    /// </summary>
    public bool SchluesselInstallieren(byte epoche, VoiceKeysGrund grund, ReadOnlySpan<byte> material)
    {
        var jetzt = Monouhr.Takte();
        lock (_hochSperre)
        {
            lock (_runterSperre)
            {
                if (grund == VoiceKeysGrund.Start)
                {
                    if (_hoch is not null || epoche != 0) return false;
                    _hoch = new SendeKanal(_sitzungsId, Richtung.Hoch, Schluesselmaterial.Hoch(epoche, material), jetzt);
                    _runter = new EmpfangsKanal(_sitzungsId, Richtung.Runter, Schluesselmaterial.Runter(epoche, material));
                    return true;
                }
                if (_hoch is null || _runter is null || epoche != unchecked((byte)(_hoch.Epoche + 1))) return false;
                _hoch.Umschalten(Schluesselmaterial.Hoch(epoche, material), jetzt);
                _runter.Umschalten(Schluesselmaterial.Runter(epoche, material), jetzt);
                return true;
            }
        }
    }

    // ---- Start und Ende ----

    /// <summary>
    /// Abonniert Adressänderungen (für die Pfadprüfung nach einem Netzwechsel, 5.12). Unter Linux braucht .NET dafür einen
    /// Netlink-Socket; eine Unit mit <c>RestrictAddressFamilies</c> ohne <c>AF_NETLINK</c> lässt das scheitern
    /// (<see cref="NetworkInformationException"/>). Bis 0.2.0 endete die Sitzung dann mit "Interner Fehler"; jetzt läuft der
    /// Sprachweg ohne Adressmeldung weiter (Keepalive und Ausfallerkennung bemerken einen Netzwechsel trotzdem, nur später).
    /// </summary>
    /// <returns><c>false</c>, wenn es keine Adressmeldung gibt.</returns>
    internal static bool AdressmeldungAbonnieren(NetworkAddressChangedEventHandler behandler, Action<NetworkAddressChangedEventHandler> abonnieren)
    {
        try
        {
            abonnieren(behandler);
            return true;
        }
        catch (Exception ex) when (ex is NetworkInformationException or SocketException)
        {
            return false;
        }
    }

    /// <summary>Ob Adressänderungen gemeldet werden (siehe <see cref="AdressmeldungAbonnieren"/>).</summary>
    public bool MitAdressmeldung { get; private set; }

    /// <summary>Öffnet den UDP-Socket und startet Sende- und Empfangs-Thread (AKTIV/UDP_BINDEN).</summary>
    public void Starten()
    {
        if (_udp is not null) return;
        var s = new Socket(_server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // ICMP "port unreachable" soll den Empfang nicht abbrechen (5.9: SIO_UDP_CONNRESET aus).
            if (OperatingSystem.IsWindows()) s.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
            s.ReceiveTimeout = 250;
            s.Bind(new IPEndPoint(_lokal, 0));
            s.Connect(_server);
        }
        catch
        {
            s.Dispose();
            throw;
        }
        _udp = s;
        lock (_ablauf) _letztesEchoMs = Monouhr.Ms();
        _sendeThread = new Thread(SendeSchleife) { IsBackground = true, Name = "Funk hoch", Priority = ThreadPriority.AboveNormal };
        _empfangsThread = new Thread(EmpfangsSchleife) { IsBackground = true, Name = "Funk runter", Priority = ThreadPriority.AboveNormal };
        _sendeThread.Start();
        _empfangsThread.Start();
        MitAdressmeldung = AdressmeldungAbonnieren(AdresseGeaendert, h => NetworkChange.NetworkAddressChanged += h);
    }

    public void Dispose()
    {
        if (_ende) return;
        _ende = true;
        if (MitAdressmeldung) NetworkChange.NetworkAddressChanged -= AdresseGeaendert;
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
        _udp?.Dispose();
        if (_sendeThread is not null && Thread.CurrentThread != _sendeThread) _sendeThread.Join(1000);
        if (_empfangsThread is not null && Thread.CurrentThread != _empfangsThread) _empfangsThread.Join(1000);
        lock (_hochSperre)
        {
            _hoch?.Dispose();
            _hoch = null;
        }
        lock (_runterSperre)
        {
            _runter?.Dispose();
            _runter = null;
            Array.Clear(_klartext);
        }
        while (_eilig.TryDequeue(out var a)) a.Freigeben();
        while (_warte.TryDequeue(out var a)) a.Freigeben();
        lock (_zeitgesteuert)
        {
            foreach (var a in _zeitgesteuert) a.Freigeben();
            _zeitgesteuert.Clear();
        }
        _signal.Dispose();
    }

    private void AdresseGeaendert(object? sender, EventArgs e)
    {
        lock (_ablauf) _netzGeaendert = true;
    }

    // ---- Senden (Sende-Thread) ----

    /// <summary>Reiht ein Sprachpaket ein (Audio-Thread, blockiert nie).</summary>
    public void SpracheEinreihen(Sendeauftrag a, long faelligTakte = 0)
    {
        if (_ende)
        {
            a.Freigeben();
            return;
        }
        if (faelligTakte > 0)
        {
            a.FaelligTakte = faelligTakte;
            lock (_zeitgesteuert) _zeitgesteuert.Add(a);
        }
        else _warte.Enqueue(a);
        Wecken();
    }

    private void SteuerpaketEinreihen(PaketTyp typ, ReadOnlySpan<byte> klartext, bool eilig = false, long faelligTakte = 0)
    {
        if (_ende) return;
        var a = Sendeauftrag.Aus(typ, klartext, nurUdp: true);
        if (faelligTakte > 0)
        {
            a.FaelligTakte = faelligTakte;
            lock (_zeitgesteuert) _zeitgesteuert.Add(a);
        }
        else if (eilig) _eilig.Enqueue(a);
        else _warte.Enqueue(a);
        Wecken();
    }

    private void Wecken()
    {
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void SendeSchleife()
    {
        var datagramm = new byte[Sprachkanal.MaxDatagramm];
        try
        {
            while (!_ende)
            {
                _signal.Wait(Wartezeit());
                ZeitgesteuerteUmlegen();
                while (!_ende && (_eilig.TryDequeue(out var a) || _warte.TryDequeue(out a)))
                {
                    try
                    {
                        Senden(a, datagramm);
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or ObjectDisposedException))
                    {
                        // Eine unbehandelte Ausnahme in einem eigenen Thread beendet den Prozess; dieses Paket ist verloren.
                        _messwerte.HochVerworfen();
                        _sitzung.FehlerMelden("Sende-Thread", ex);
                    }
                    finally
                    {
                        a.Freigeben();
                    }
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            Array.Clear(datagramm);
        }
    }

    private int Wartezeit()
    {
        lock (_zeitgesteuert)
        {
            if (_zeitgesteuert.Count == 0) return 100;
            var naechste = long.MaxValue;
            foreach (var a in _zeitgesteuert) naechste = Math.Min(naechste, a.FaelligTakte);
            var ms = Monouhr.MsGenau(naechste - Monouhr.Takte());
            return (int)Math.Clamp(Math.Ceiling(ms), 0, 100);
        }
    }

    private void ZeitgesteuerteUmlegen()
    {
        lock (_zeitgesteuert)
        {
            if (_zeitgesteuert.Count == 0) return;
            var jetzt = Monouhr.Takte();
            // Reihenfolge der Fälligkeit bleibt erhalten (ENDE 2 vor ENDE 3).
            _zeitgesteuert.Sort(static (x, y) => x.FaelligTakte.CompareTo(y.FaelligTakte));
            var n = 0;
            while (n < _zeitgesteuert.Count && _zeitgesteuert[n].FaelligTakte <= jetzt) _warte.Enqueue(_zeitgesteuert[n++]);
            _zeitgesteuert.RemoveRange(0, n);
        }
    }

    private void Senden(Sendeauftrag a, byte[] ziel)
    {
        var tunnel = _tunnel && !a.NurUdp;
        if (!tunnel && (UdpSperre || _udp is null)) return;
        int laenge;
        lock (_hochSperre)
        {
            if (_hoch is null) return;
            if (!_hoch.Versiegeln(a.Typ, a.Puffer.AsSpan(0, a.Laenge), ziel, Monouhr.Takte(), out laenge))
            {
                _messwerte.HochVerworfen();
                return;
            }
        }
        if (tunnel)
        {
            _sitzung.TunnelHoch(ziel.AsSpan(0, laenge).ToArray());
            _messwerte.HochGesendet(true);
            return;
        }
        try
        {
            _udp!.Send(ziel.AsSpan(0, laenge), SocketFlags.None);
            Interlocked.Increment(ref _udpHoch);
            _messwerte.HochGesendet(false);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Senden gescheitert (etwa kein Netz): wie ein verlorenes Datagramm, die Ausfallerkennung greift.
        }
    }

    // ---- Empfangen ----

    private void EmpfangsSchleife()
    {
        var puffer = new byte[Sprachkanal.EmpfangspufferLaenge];
        try
        {
            while (!_ende)
            {
                int n;
                try
                {
                    n = _udp!.Receive(puffer, SocketFlags.None);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.MessageSize)
                {
                    // Abgeschnitten (5.9 Schritt 1): verwerfen.
                    _messwerte.Ungueltig();
                    continue;
                }
                catch (SocketException)
                {
                    if (_ende) return;
                    continue;
                }
                var t = Monouhr.Takte();
                if (UdpSperre) continue;
                try
                {
                    Datagramm(puffer.AsSpan(0, n), n == puffer.Length, Verbindungsweg.Udp, t);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or ObjectDisposedException))
                {
                    // Eine unbehandelte Ausnahme in einem eigenen Thread beendet den Prozess; das Datagramm ist verloren.
                    _messwerte.Ungueltig();
                    _sitzung.FehlerMelden("Empfangs-Thread", ex);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            Array.Clear(puffer);
        }
    }

    /// <summary>SpracheTunnel runter aus der Leseschleife (5.12): dieselbe Prüfung, dasselbe Fenster.</summary>
    public void TunnelEingang(ReadOnlySpan<byte> datagramm) => Datagramm(datagramm, false, Verbindungsweg.Tunnel, Monouhr.Takte());

    /// <summary>Empfangsprüfung 5.9 für runter (ohne Schritt 4 und 9), dann nach Typ.</summary>
    private void Datagramm(ReadOnlySpan<byte> d, bool abgeschnitten, Verbindungsweg weg, long t)
    {
        if (Empfangspruefung.Vorpruefen(d, abgeschnitten, Richtung.Runter, out var kopf) != Empfangsergebnis.Angenommen || kopf.SitzungsId != _sitzungsId)
        {
            _messwerte.Ungueltig();
            return;
        }
        lock (_runterSperre)
        {
            if (_runter is null) return;
            _runter.Takt(t);
            var e = _runter.Oeffnen(d, _klartext, t);
            if (e != Empfangsergebnis.Angenommen)
            {
                if (e is Empfangsergebnis.Replay or Empfangsergebnis.ReplayNachTag) _messwerte.Doppelt();
                else _messwerte.Ungueltig();
                return;
            }
            _messwerte.RunterAngenommen(kopf.Zaehler, weg == Verbindungsweg.Tunnel);
            if (weg == Verbindungsweg.Udp) Interlocked.Increment(ref _udpRunter);
            var klartext = _klartext.AsSpan(0, d.Length - Sprachkanal.Ueberhang);
            switch (kopf.Typ)
            {
                case PaketTyp.PfadPruefung when weg == Verbindungsweg.Udp:
                    // 5.10: jede gültige PFAD_PRUEFUNG sofort beantworten, über den verbundenen Socket.
                    lock (_ablauf) _pfadPruefungErhalten = true;
                    SteuerpaketEinreihen(PaketTyp.PfadAntwort, klartext, eilig: true);
                    break;
                case PaketTyp.KeepaliveEcho when weg == Verbindungsweg.Udp:
                    Echo(klartext);
                    break;
                case PaketTyp.SpracheRunter:
                    Sprache(klartext, t, weg);
                    break;
            }
        }
    }

    private void Echo(ReadOnlySpan<byte> klartext)
    {
        if (!KeepaliveEcho.TryLesen(klartext, out var echo)) return;
        var jetzt = Monouhr.Ms();
        var rtt = unchecked((uint)jetzt - echo.ZeitMs);
        if (rtt < 60_000) _messwerte.Rtt(rtt);
        lock (_ablauf)
        {
            _letztesEchoMs = jetzt;
            if (_probeSeitMs >= 0 && _probeOffen.Remove(echo.Nr) && _probeSauberSeitMs < 0 && _gebunden) _probeSauberSeitMs = jetzt;
        }
    }

    private void Sprache(ReadOnlySpan<byte> klartext, long t, Verbindungsweg weg)
    {
        if (SpracheRunter.Lesen(klartext, out var p) != Formfehler.Keiner)
        {
            _messwerte.Ungueltig();
            return;
        }
        // 5.13: jede empfangene Opus-Nutzlast muss genau 960 Samples ergeben, sonst verwerfen.
        if (!p.IstEnde && _optionen.OpusPruefer.Samples(p.Opus) != OpusPruefung.SamplesJeFrame)
        {
            _messwerte.OpusVerworfen();
            return;
        }
        var ms = Monouhr.MsAus(t);
        if (!_zeitlinien.Paket(p.Absender, p.Sprechphase, p.FrameNr, p.IstEnde, ms, out var neu, out var fortsetzung, out var ende)) return;
        if (!p.IstEnde) _messwerte.Sprache(p.Absender, p.Sprechphase, p.FrameNr, (p.Kennzeichen & Kennzeichen.Preroll) != 0, Monouhr.MsGenau(t));
        for (var i = 0; i < p.NetzAnzahl; i++) _sitzung.NetzImStempel(p.Netz(i), p.Art);
        if (_optionen.Empfaenger is not { } empfaenger) return;
        try
        {
            empfaenger.Empfangen(new SprachEmpfang(p, neu, fortsetzung, ende, t, weg));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Die Audio-Schicht läuft auf dem Empfangs-Thread (UDP) bzw. der Leseschleife (Tunnel). Eine Ausnahme dort
            // beendete sonst den Prozess (Thread) oder die Sitzung (Leseschleife); das Paket ist verloren, der Empfang läuft.
            _messwerte.Ungueltig();
            _sitzung.FehlerMelden("Sprachempfaenger", ex);
        }
    }

    // ---- Ablauf: Bindung, Keepalive, Ausfall, Probe (Takt der Sitzung) ----

    /// <summary><c>UdpStatus{GEBUNDEN}</c> kam an.</summary>
    public void GebundenGemeldet()
    {
        lock (_ablauf)
        {
            if (_gebunden) _messwerte.Pfadwechsel();
            _gebunden = true;
            _gebundenMs = Monouhr.Ms();
            _letztesEchoMs = Math.Max(_letztesEchoMs, _gebundenMs);
        }
    }

    /// <summary>Tunnel an oder aus; hoch-Sprache geht ab sofort über den gewählten Weg (der Server nimmt beide an).</summary>
    public void TunnelSetzen(bool an, TunnelGrund? grund)
    {
        var jetzt = Monouhr.Ms();
        lock (_ablauf)
        {
            _tunnel = an;
            _tunnelGrund = an ? grund : null;
            _probeSeitMs = -1;
            _probeSauberSeitMs = -1;
            _probeOffen.Clear();
            if (an && grund is TunnelGrund.UdpAusfall)
            {
                if (_letzteRueckkehrMs >= 0) _rueckfaelle.Add(jetzt);
                _rueckfaelle.RemoveAll(r => jetzt - r > 600_000);
                // 5.12: nach zwei Rückfällen binnen 10 min den Probenabstand verdoppeln, bis höchstens 5 min.
                if (_rueckfaelle.Count >= 2)
                {
                    _probeAbstand = TimeSpan.FromTicks(Math.Min(_probeAbstand.Ticks * 2, ProbeAbstandMax.Ticks));
                    _rueckfaelle.Clear();
                }
            }
            _naechsteProbeMs = an && grund != TunnelGrund.Nutzer ? jetzt + (long)_probeAbstand.TotalMilliseconds : long.MaxValue;
            if (!an)
            {
                _letzteRueckkehrMs = jetzt;
                _letztesEchoMs = jetzt;
                _verdachtSeitMs = -1;
            }
        }
        _messwerte.TunnelWechsel();
    }

    /// <summary>Ping-Antwort mit den Zählern des Servers: Ausfallerkennung hoch und runter (5.12).</summary>
    public void PingAntwort(long udpGesendet, long udpEmpfangen)
    {
        var jetzt = (udpGesendet, udpEmpfangen, UdpRunter, UdpHoch);
        lock (_ablauf)
        {
            if (_pingVorher is { } v && _sitzung.Zustand == ClientZustand.AktivUdp)
            {
                var runterAusfall = udpGesendet - v.Gesendet >= 5 && jetzt.Item3 - v.RunterUdp == 0;
                var hochAusfall = udpEmpfangen - v.Empfangen == 0 && jetzt.Item4 - v.HochUdp >= 3;
                if (runterAusfall || hochAusfall) _pingVerdacht = true;
            }
            _pingVorher = jetzt;
        }
    }

    /// <summary>Takt der Sitzung (alle 50 ms).</summary>
    public void Takt(long jetztMs)
    {
        lock (_runterSperre) _zeitlinien.Takt(jetztMs, ZeitlinieBeendet);
        RotationPruefen(jetztMs);
        var zustand = _sitzung.Zustand;
        bool ausfall = false, sauber = false;
        lock (_ablauf)
        {
            switch (zustand)
            {
                case ClientZustand.AktivUdpBinden:
                    if (!_pfadPruefungErhalten && _anmeldungen < AnmeldungMax && jetztMs - _letzteAnmeldungMs >= AnmeldungAbstandMs)
                    {
                        _anmeldungen++;
                        _letzteAnmeldungMs = jetztMs;
                        Anmeldung();
                    }
                    break;
                case ClientZustand.AktivUdp:
                    if (jetztMs - _letzterKeepaliveMs >= KeepaliveAbstandMs || _netzGeaendert) KeepaliveSenden(jetztMs);
                    ausfall = Ausfallerkennung(jetztMs);
                    break;
                case ClientZustand.AktivTunnel:
                    sauber = Probe(jetztMs);
                    break;
            }
        }
        if (ausfall) _sitzung.UdpAusfall();
        if (sauber) _sitzung.ProbeSauber();
    }

    private void ZeitlinieBeendet(uint absender, ushort phase)
    {
        try
        {
            _optionen.Empfaenger?.ZeitlinieBeendet(absender, phase);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Sonst bräche der Takt hier ab, und Keepalive, Ausfallerkennung und Rotation fielen aus.
            _sitzung.FehlerMelden("Sprachempfaenger", ex);
        }
    }

    private void RotationPruefen(long jetztMs)
    {
        bool anfordern, neu;
        byte epoche;
        lock (_hochSperre)
        {
            if (_hoch is null) return;
            anfordern = _hoch.RotationAnfordern;
            neu = _hoch.NeueSitzungNoetig(Monouhr.Takte());
            epoche = _hoch.Epoche;
        }
        if (neu)
        {
            // 5.8: länger als 5 s an der harten Grenze, also eine neue Sitzung.
            _sitzung.NeueSitzung("harte Grenze des hoch-Zählers");
            return;
        }
        if (anfordern && jetztMs - _letzteRotationsAnfrageMs >= 10_000)
        {
            _letzteRotationsAnfrageMs = jetztMs;
            _sitzung.KeyRotationAnfordern(epoche);
        }
    }

    /// <summary>Nur unter _ablauf.</summary>
    private void Anmeldung()
    {
        Span<byte> k = stackalloc byte[Steuerpakete.Laenge];
        Steuerpakete.AnmeldungSchreiben(k);
        SteuerpaketEinreihen(PaketTyp.Anmeldung, k);
    }

    /// <summary>Nur unter _ablauf.</summary>
    private uint KeepaliveSenden(long jetztMs, long faelligTakte = 0)
    {
        _netzGeaendert = false;
        _letzterKeepaliveMs = jetztMs;
        var nr = ++_keepaliveNr;
        Span<byte> k = stackalloc byte[Steuerpakete.Laenge];
        new Keepalive(nr, unchecked((uint)Monouhr.Ms())).Schreiben(k);
        SteuerpaketEinreihen(PaketTyp.Keepalive, k, faelligTakte: faelligTakte);
        return nr;
    }

    /// <summary>5.12, nur unter _ablauf: Verdacht, dann 3 KEEPALIVE im Abstand von 100 ms, nach 1 s ohne Echo oder Bindung Ausfall.</summary>
    private bool Ausfallerkennung(long jetztMs)
    {
        var verdacht = _pingVerdacht || (_letztesEchoMs >= 0 && jetztMs - _letztesEchoMs > EchoFristMs);
        _pingVerdacht = false;
        if (verdacht && _verdachtSeitMs < 0)
        {
            _verdachtSeitMs = jetztMs;
            var t = Monouhr.Takte();
            KeepaliveSenden(jetztMs);
            KeepaliveSenden(jetztMs, t + Monouhr.TakteAus(100));
            KeepaliveSenden(jetztMs, t + Monouhr.TakteAus(200));
            return false;
        }
        if (_verdachtSeitMs < 0) return false;
        if (_letztesEchoMs >= _verdachtSeitMs || _gebundenMs >= _verdachtSeitMs)
        {
            _verdachtSeitMs = -1;
            return false;
        }
        if (jetztMs - _verdachtSeitMs < VerdachtFristMs) return false;
        _verdachtSeitMs = -1;
        return true;
    }

    /// <summary>
    /// UDP-Probe im Tunnel (5.12), nur unter _ablauf: alle 30 s ANMELDUNG und KEEPALIVE; steht die Bindung und liefern die
    /// Keepalives (hier jede Sekunde eines) 10 s lang Echos ohne Verlust, geht es zurück auf UDP.
    /// </summary>
    private bool Probe(long jetztMs)
    {
        if (_tunnelGrund is null or TunnelGrund.Nutzer) return false;
        if (_probeSeitMs < 0)
        {
            if (jetztMs < _naechsteProbeMs) return false;
            _probeSeitMs = jetztMs;
            _probeSauberSeitMs = -1;
            _probeOffen.Clear();
            Anmeldung();
            if (_gebunden) _probeOffen[KeepaliveSenden(jetztMs)] = jetztMs;
            _probeLetzterKaMs = jetztMs;
            return false;
        }
        var gescheitert = false;
        foreach (var (_, gesendet) in _probeOffen)
            if (jetztMs - gesendet > 1000) gescheitert = true;
        if (!_gebunden && jetztMs - _probeSeitMs > 3000) gescheitert = true;
        if (gescheitert)
        {
            _probeSeitMs = -1;
            _probeOffen.Clear();
            _naechsteProbeMs = jetztMs + (long)_probeAbstand.TotalMilliseconds;
            return false;
        }
        if (jetztMs - _probeLetzterKaMs >= 1000)
        {
            _probeLetzterKaMs = jetztMs;
            if (_gebunden) _probeOffen[KeepaliveSenden(jetztMs)] = jetztMs;
            else Anmeldung();
        }
        if (_probeSauberSeitMs >= 0 && _gebunden && jetztMs - _probeSauberSeitMs >= (long)_optionen.TunnelProbeDauer.TotalMilliseconds)
        {
            _probeSeitMs = -1;
            return true;
        }
        return false;
    }
}
