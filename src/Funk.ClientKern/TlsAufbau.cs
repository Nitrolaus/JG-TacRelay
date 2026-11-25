// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>Einstellungen für <see cref="TlsAufbau"/>.</summary>
public sealed class TlsAufbauOptionen
{
    /// <summary>Lokale Adresse für TCP (und damit UDP); ohne Angabe wählt das System.</summary>
    public IPAddress? LokaleAdresse { get; init; }

    /// <summary>
    /// Statt der Hosts aus dem Profil diesen Host oder diese IP verbinden (Testclient: "Adresse"). Ein Name übernimmt die
    /// Adressfamilien des Profils (Heimphase nur IPv4); eine IP-Adresse gilt, wie sie dasteht.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>Statt des TCP-Ports aus dem Profil.</summary>
    public ushort? Port { get; init; }

    /// <summary>Uhr für den Pin-Wächter (7.1 Schritt 4).</summary>
    public TimeProvider Zeit { get; init; } = TimeProvider.System;

    /// <summary>
    /// Namensauflösung; ohne Angabe das Betriebssystem (<see cref="Dns.GetHostAddressesAsync(string, AddressFamily, CancellationToken)"/>).
    /// Die Familie ist <see cref="AddressFamily.InterNetwork"/> für einen Host nur mit IPv4 (dann wird nur nach A-Einträgen
    /// gefragt), <see cref="AddressFamily.InterNetworkV6"/> nur mit IPv6, sonst <see cref="AddressFamily.Unspecified"/>.
    /// </summary>
    public Func<string, AddressFamily, CancellationToken, Task<IPAddress[]>>? Aufloeser { get; init; }

    /// <summary>Frist je Adresse für TCP (7.1 Schritt 2).</summary>
    public TimeSpan TcpFrist { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Versatz zwischen parallelen Versuchen nach RFC 8305.</summary>
    public TimeSpan Versatz { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Frist für den TLS-Handshake.</summary>
    public TimeSpan HandshakeFrist { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Verbindungsaufbau im Betrieb (4.1, 7.1): alle Hosts des Profils neu auflösen (nur erlaubte Familien), Adressen nach
/// RFC 8305 um 250 ms versetzt parallel versuchen (je Adresse 3 s), bei gescheiterter Auflösung die letzte erfolgreiche
/// Adresse; dann TLS mit Gerätezertifikat, SNI aus dem Profil, ALPN <c>tacrelay/1</c>, ohne Renegotiation und
/// Wiederaufnahme, Pin-Prüfung über SHA-256(SPKI) gegen <c>pins</c>. Ein Pin-Fehler bei der Erstverbindung führt zu "Server
/// nicht vertrauenswürdig", sobald keine Adresse dieses Versuchs einen gültigen Pin zeigt; die übrigen Hosts des Profils kommen
/// vorher noch dran (sonst hielte im Übergangsprofil eines Umzugs ein fremder Dienst unter einem der Namen alle Clients vom
/// echten Server fern). Beim Wiederverbinden gilt ein Pin-Fehler zuerst als falscher Host (Adresse 60 s meiden), der Alarm
/// kommt erst nach 2 min ohne gültigen Pin (<see cref="PinWaechter"/>). Ein vom Server übernommenes, neueres Profil
/// (<see cref="ProfilSetzen"/>, F1) gilt ab dem nächsten Versuch, auch beim automatischen Wiederverbinden.
/// </summary>
public sealed class TlsAufbau : IVerbindungsaufbau
{
    private volatile Serverprofil _profil;
    private readonly Geraeteidentitaet _identitaet;
    private readonly TlsAufbauOptionen _optionen;
    private readonly PinWaechter _pins;
    private readonly Lock _sperre = new();
    private (IPAddress Ip, string Host)? _letzte;
    private Fingerabdruck? _letzterPin;

    public TlsAufbau(Serverprofil profil, Geraeteidentitaet identitaet, TlsAufbauOptionen? optionen = null)
    {
        ArgumentNullException.ThrowIfNull(profil);
        ArgumentNullException.ThrowIfNull(identitaet);
        _profil = profil;
        _identitaet = identitaet;
        _optionen = optionen ?? new TlsAufbauOptionen();
        _pins = new PinWaechter(_optionen.Zeit);
    }

    /// <summary>
    /// Ein neueres, schon geprüftes Profil (F1: aus <c>Welcome.profil</c>, signiert mit einem eingebauten Schlüssel, Version
    /// höher). Es gilt ab dem nächsten Versuch; ein laufender Versuch und die bestehende Verbindung bleiben beim alten.
    /// </summary>
    public void ProfilSetzen(Serverprofil neu)
    {
        ArgumentNullException.ThrowIfNull(neu);
        _profil = neu;
    }

    /// <summary>Das Profil, mit dem der nächste Versuch beginnt.</summary>
    public Serverprofil Profil => _profil;

    /// <summary>
    /// Der Pin, den der Server der zuletzt aufgebauten Verbindung gezeigt hat (<see cref="Steuerverbindung.Pin"/>); <c>null</c> vor der
    /// ersten. F1: Ein Profil aus <c>Welcome.profil</c>, in dem dieser Pin fehlt, übernimmt der Client nicht (3.1).
    /// </summary>
    public Fingerabdruck? LetzterPin
    {
        get
        {
            lock (_sperre) return _letzterPin;
        }
    }

    /// <summary>Tests: ersetzt den TLS-Handshake je Adresse (ohne Schannel); <c>null</c> im Betrieb.</summary>
    internal Func<Socket, (IPAddress Ip, string Host), ushort, Serverprofil, CancellationToken, Task<Steuerverbindung>>? HandshakeErsatz { get; set; }

    /// <summary>Ob der Pin-Alarm gilt ("Server nicht vertrauenswürdig").</summary>
    public bool PinAlarm
    {
        get
        {
            lock (_sperre) return _pins.Alarm;
        }
    }

    public async Task<Steuerverbindung> AufbauenAsync(bool wiederverbindung, CancellationToken abbruch)
    {
        // Ein Versuch, ein Profil: Ein übernommenes Profil (ProfilSetzen) gilt erst beim nächsten Aufruf.
        var profil = _profil;
        var port = PortFuer(profil);
        var kandidaten = await KandidatenAsync(profil, abbruch).ConfigureAwait(false);

        Exception? letzterFehler = null;
        AufbauException? erstverbindungPin = null;
        var gemieden = false;
        while (kandidaten.Count > 0)
        {
            lock (_sperre) gemieden |= kandidaten.RemoveAll(k => _pins.IstGemieden(Schluessel(k.Ip, port))) > 0;
            if (kandidaten.Count == 0) break;
            Socket socket;
            (IPAddress Ip, string Host) ziel;
            try
            {
                (socket, ziel) = await TcpAsync(kandidaten, port, abbruch).ConfigureAwait(false);
            }
            catch (AufbauException e) when (erstverbindungPin is not null)
            {
                // Erstverbindung: Eine Adresse zeigte einen fremden Pin, die übrigen antworten nicht. Das bleibt der Alarm (4.1).
                throw new AufbauException(AufbauFehler.PinAlarm, erstverbindungPin.Message, e);
            }
            kandidaten.Remove(ziel);
            try
            {
                var v = HandshakeErsatz is { } ersatz
                    ? await ersatz(socket, ziel, port, profil, abbruch).ConfigureAwait(false)
                    : await TlsAsync(socket, ziel, port, profil, abbruch).ConfigureAwait(false);
                Gelungen(ziel);
                lock (_sperre) _letzterPin = v.Pin;
                return v;
            }
            catch (AufbauException e) when (e.Art == AufbauFehler.Pin)
            {
                letzterFehler = e;
                if (!wiederverbindung)
                {
                    // 4.1: kein Ausnahmeknopf und keine Verbindung zu diesem Server. Die übrigen Adressen dieses Versuchs (andere
                    // Hosts des Profils) kommen noch dran; zeigt keine einen gültigen Pin, folgt der Alarm.
                    erstverbindungPin ??= e;
                    continue;
                }
                bool alarm;
                lock (_sperre) alarm = _pins.PinFehler(Schluessel(ziel.Ip, port));
                if (alarm) throw new AufbauException(AufbauFehler.PinAlarm, "2 min lang kein gültiger Pin, zuletzt " + Ziel(ziel.Ip, port), e);
            }
            catch (AufbauException e)
            {
                letzterFehler = e;
            }
        }
        // Die Meldung ist nur die Einzelheit; "Server nicht vertrauenswürdig" sagt die Oberfläche selbst (4.1).
        if (erstverbindungPin is not null) throw new AufbauException(AufbauFehler.PinAlarm, erstverbindungPin.Message, erstverbindungPin);
        throw letzterFehler as AufbauException
            ?? new AufbauException(AufbauFehler.Netz, gemieden ? "alle Adressen nach einem Pin-Fehler für 60 s gemieden" : "keine Adresse erreichbar", letzterFehler);
    }

    /// <summary>Der TCP-Port dieses Aufbaus: aus dem Profil (etwa 8443 in der Heimphase), außer die Optionen nennen einen.</summary>
    internal ushort Port => PortFuer(_profil);

    private ushort PortFuer(Serverprofil profil) => _optionen.Port ?? profil.TcpPort;

    /// <summary>
    /// Die Hosts eines Versuchs: aus dem Profil oder, mit <see cref="TlsAufbauOptionen.Host"/>, dieser eine. Ein Name statt der
    /// Profil-Hosts übernimmt deren Familien (7.1 Schritt 1: nur die im Profil erlaubten, Heimphase nur IPv4); eine
    /// IP-Adresse hat ihre Familie schon.
    /// </summary>
    internal IReadOnlyList<ProfilHost> Hosts() => Hosts(_profil);

    private IReadOnlyList<ProfilHost> Hosts(Serverprofil profil) => _optionen.Host is not { } h
        ? profil.Hosts
        : IPAddress.TryParse(h, out _)
            ? [new ProfilHost(h, true, true)]
            : [new ProfilHost(h, profil.Hosts.Any(x => x.IPv4), profil.Hosts.Any(x => x.IPv6))];

    /// <summary>
    /// Die Adressen dieses Versuchs: alle Hosts neu aufgelöst (7.1 Schritt 1, jeder Versuch neu, so kommt ein DynDNS-Wechsel
    /// an), ohne Ergebnis die letzte erfolgreiche Adresse (Schritt 3). Ohne beides scheitert der Aufbau mit dem Grund je Host.
    /// </summary>
    internal Task<List<(IPAddress Ip, string Host)>> KandidatenAsync(CancellationToken abbruch) => KandidatenAsync(_profil, abbruch);

    private async Task<List<(IPAddress Ip, string Host)>> KandidatenAsync(Serverprofil profil, CancellationToken abbruch)
    {
        var fehler = new List<string>();
        var kandidaten = await AufloesenAsync(profil, fehler, abbruch).ConfigureAwait(false);
        if (kandidaten.Count > 0) return kandidaten;
        (IPAddress Ip, string Host)? letzte;
        lock (_sperre) letzte = _letzte;
        if (letzte is { } l) return [l];
        throw new AufbauException(AufbauFehler.Netz, fehler.Count > 0 ? string.Join("; ", fehler) : "keine Adresse im Profil");
    }

    /// <summary>Ein Aufbau zu <paramref name="ziel"/> ist gelungen: Pin-Wächter zurück, Adresse für Schritt 3 merken.</summary>
    internal void Gelungen((IPAddress Ip, string Host) ziel)
    {
        lock (_sperre)
        {
            _pins.PinGueltig();
            _letzte = ziel;
        }
    }

    private static string Schluessel(IPAddress ip, ushort port) => ip + ":" + port.ToString(CultureInfo.InvariantCulture);

    /// <summary>Adresse und Port für Meldungen, IPv6 in eckigen Klammern.</summary>
    internal static string Ziel(IPAddress ip, ushort port) =>
        (ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString()) + ":" + port.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Alle Hosts neu auflösen, nur die erlaubten Familien (7.1 Schritt 1): Ein Host nur mit IPv4 fragt nur nach A-Einträgen.
    /// IPv6 und IPv4 abwechselnd (RFC 8305). Was je Host nicht klappte, landet in <paramref name="fehler"/>.
    /// </summary>
    private async Task<List<(IPAddress Ip, string Host)>> AufloesenAsync(Serverprofil profil, List<string> fehler, CancellationToken abbruch)
    {
        var v6 = new List<(IPAddress, string)>();
        var v4 = new List<(IPAddress, string)>();
        foreach (var host in Hosts(profil))
        {
            var familie = host switch
            {
                { IPv4: true, IPv6: true } => AddressFamily.Unspecified,
                { IPv6: true } => AddressFamily.InterNetworkV6,
                _ => AddressFamily.InterNetwork,
            };
            IPAddress[] adressen;
            if (IPAddress.TryParse(host.Name, out var direkt)) adressen = [direkt];
            else
            {
                try
                {
                    adressen = _optionen.Aufloeser is { } a
                        ? await a(host.Name, familie, abbruch).ConfigureAwait(false)
                        : await Dns.GetHostAddressesAsync(host.Name, familie, abbruch).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or ArgumentException)
                {
                    // Unbekannter Name, kein DNS erreichbar oder ein Name, den das Betriebssystem gar nicht erst nachschlägt.
                    fehler.Add(host.Name + " ist im DNS nicht zu finden");
                    continue;
                }
            }
            var sni = _optionen.Host is not null && IPAddress.TryParse(_optionen.Host, out _) && profil.Hosts.Count > 0 ? profil.Hosts[0].Name : host.Name;
            var vorher = v4.Count + v6.Count;
            foreach (var ip in adressen)
            {
                var n = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
                if (n.AddressFamily == AddressFamily.InterNetwork && host.IPv4) v4.Add((n, sni));
                else if (n.AddressFamily == AddressFamily.InterNetworkV6 && host.IPv6) v6.Add((n, sni));
            }
            if (v4.Count + v6.Count == vorher)
                fehler.Add(host.Name + " hat keine " + (familie switch { AddressFamily.InterNetwork => "IPv4-", AddressFamily.InterNetworkV6 => "IPv6-", _ => "" }) + "Adresse");
        }
        var ergebnis = new List<(IPAddress, string)>();
        for (var i = 0; i < Math.Max(v4.Count, v6.Count); i++)
        {
            if (i < v6.Count && !ergebnis.Contains(v6[i])) ergebnis.Add(v6[i]);
            if (i < v4.Count && !ergebnis.Contains(v4[i])) ergebnis.Add(v4[i]);
        }
        return ergebnis;
    }

    /// <summary>RFC 8305: Versuche um 250 ms versetzt (früher, wenn einer scheitert), je Adresse 3 s; der erste Erfolg gewinnt.</summary>
    internal async Task<(Socket Socket, (IPAddress Ip, string Host) Ziel)> TcpAsync(List<(IPAddress Ip, string Host)> kandidaten, ushort port, CancellationToken abbruch)
    {
        using var alle = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
        var laufend = new List<Task<(Socket? Socket, (IPAddress, string) Ziel, Exception? Fehler)>>();
        var naechste = 0;
        Exception? letzter = null;
        var texte = new List<string>();
        try
        {
            while (true)
            {
                if (naechste < kandidaten.Count) laufend.Add(VersuchAsync(kandidaten[naechste++], port, alle.Token));
                if (laufend.Count == 0) throw new AufbauException(AufbauFehler.Netz, texte.Count > 0 ? string.Join("; ", texte) : "keine Adresse", letzter);
                var takt = naechste < kandidaten.Count ? Task.Delay(_optionen.Versatz, alle.Token) : Task.Delay(Timeout.Infinite, alle.Token);
                var fertig = await Task.WhenAny([.. laufend.Cast<Task>(), takt]).ConfigureAwait(false);
                if (fertig == takt) continue;
                var versuch = (Task<(Socket? Socket, (IPAddress, string) Ziel, Exception? Fehler)>)fertig;
                laufend.Remove(versuch);
                var (socket, ziel, fehler) = await versuch.ConfigureAwait(false);
                if (socket is not null)
                {
                    await alle.CancelAsync().ConfigureAwait(false);
                    foreach (var rest in laufend) _ = rest.ContinueWith(static t => t.Result.Socket?.Dispose(), TaskScheduler.Default);
                    return (socket, ziel);
                }
                letzter = fehler;
                texte.Add(TcpText(ziel.Item1, port, fehler));
                abbruch.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (!abbruch.IsCancellationRequested)
        {
            throw new AufbauException(AufbauFehler.Netz, "TCP abgebrochen", letzter);
        }
    }

    /// <summary>Was an einer Adresse schiefging, in Worten für die Anzeige.</summary>
    internal static string TcpText(IPAddress ip, ushort port, Exception? fehler) => Ziel(ip, port) + (fehler switch
    {
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => " lehnt die Verbindung ab (kein Dienst auf dem Port oder keine Portfreigabe)",
        SocketException { SocketErrorCode: SocketError.HostUnreachable or SocketError.NetworkUnreachable } => " ist nicht erreichbar",
        SocketException { SocketErrorCode: SocketError.TimedOut } or OperationCanceledException => " antwortet nicht",
        SocketException s => " antwortet nicht (" + s.SocketErrorCode + ")",
        _ => " antwortet nicht",
    });

    private async Task<(Socket?, (IPAddress, string), Exception?)> VersuchAsync((IPAddress Ip, string Host) ziel, ushort port, CancellationToken abbruch)
    {
        var s = new Socket(ziel.Ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true, SendBufferSize = 32 * 1024 };
        try
        {
            if (_optionen.LokaleAdresse is { } lokal && lokal.AddressFamily == ziel.Ip.AddressFamily) s.Bind(new IPEndPoint(lokal, 0));
            using var frist = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
            frist.CancelAfter(_optionen.TcpFrist);
            await s.ConnectAsync(ziel.Ip, port, frist.Token).ConfigureAwait(false);
            return (s, ziel, null);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            s.Dispose();
            return (null, ziel, ex);
        }
    }

    private async Task<Steuerverbindung> TlsAsync(Socket socket, (IPAddress Ip, string Host) ziel, ushort port, Serverprofil profil, CancellationToken abbruch)
    {
        var lokal = ((IPEndPoint)socket.LocalEndPoint!).Address;
        X509Certificate2 zertifikat;
        try
        {
            zertifikat = _identitaet.FuerTls();
        }
        catch (CryptographicException ex)
        {
            // Sonst liefe die Ausnahme an der Verbindungsschleife vorbei, und die Oberfläche stünde ohne Grund auf "Verbinde...".
            socket.Dispose();
            throw new AufbauException(AufbauFehler.Tls, "Der Geräteschlüssel lässt sich für TLS nicht benutzen: " + ex.Message, ex);
        }
        var ssl = new SslStream(new NetworkStream(socket, ownsSocket: true), leaveInnerStreamOpen: false);
        var pinGeprueft = false;
        var pinPasst = false;
        Fingerabdruck? pin = null;
        var optionen = new SslClientAuthenticationOptions
        {
            TargetHost = ziel.Host,
            EnabledSslProtocols = SslProtocols.None,
            ClientCertificates = [zertifikat],
            ApplicationProtocols = [new SslApplicationProtocol(Versionen.Alpn)],
            AllowRenegotiation = false,
            AllowTlsResume = false,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            // 4.1: nur der Pin zählt, nicht Name, Kette oder Gültigkeit.
            RemoteCertificateValidationCallback = (_, zert, _, _) =>
            {
                pinGeprueft = true;
                pinPasst = zert is X509Certificate2 x && profil.ZertifikatPasst(x);
                if (pinPasst) pin = Fingerabdruck.AusZertifikat((X509Certificate2)zert!);
                return pinPasst;
            },
        };
        try
        {
            using var frist = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
            frist.CancelAfter(_optionen.HandshakeFrist);
            await ssl.AuthenticateAsClientAsync(optionen, frist.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException or OperationCanceledException)
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            if (pinGeprueft && !pinPasst) throw new AufbauException(AufbauFehler.Pin, "Pin passt nicht an " + Ziel(ziel.Ip, port), ex);
            abbruch.ThrowIfCancellationRequested();
            var grund = ex is OperationCanceledException ? "keine Antwort binnen " + _optionen.HandshakeFrist.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s"
                : (ex.InnerException ?? ex).Message;
            throw new AufbauException(AufbauFehler.Tls, "TLS mit " + Ziel(ziel.Ip, port) + " gescheitert: " + grund, ex);
        }
        if (ssl.NegotiatedApplicationProtocol != new SslApplicationProtocol(Versionen.Alpn))
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw new AufbauException(AufbauFehler.Alpn, Ziel(ziel.Ip, port) + " spricht nicht tacrelay/1 (ALPN)");
        }
        return new Steuerverbindung(ssl, ziel.Ip, lokal, socket: null, halter: null, beschreibung: "TLS " + ssl.SslProtocol + " Port " + port.ToString(CultureInfo.InvariantCulture))
        {
            Pin = pin,
        };
    }
}
