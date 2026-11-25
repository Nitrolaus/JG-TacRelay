// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using Funk.Server.Sprachpfad;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Server.Netzzugang;

/// <summary>
/// TCP-Annahme mit TLS über <c>SslStream</c> (4.1): nur ausdrücklich konfigurierte Adressen, Zulassung vor dem Handshake
/// (Töpfe je Präfix, Vorrangspur, CPU-Schutz), TLS 1.3 mit Rückfall 1.2, Client-Zertifikat Pflicht und nur P-256, ALPN
/// <c>tacrelay/1</c>, keine Renegotiation, keine Wiederaufnahme, Handshake-Frist 5 s. Danach Einstufung über den
/// SPKI-Fingerabdruck am Kontenbuch: nur unbekannte Geräte belegen die Plätze für Unfreigegebene.
/// </summary>
public sealed class TlsAnnahme : IAsyncDisposable
{
    public static readonly TimeSpan HandshakeFrist = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IPEndPoint> _binden;
    private readonly ServerIdentitaet _identitaet;
    private readonly bool _nurTls13;
    private readonly Zentrale _zentrale;
    private readonly Relais? _relais;
    private readonly Logbuch _log;
    private readonly List<Socket> _lauscher = [];
    private readonly List<Task> _schleifen = [];
    private readonly CancellationTokenSource _stop = new();

    public TlsAnnahme(IReadOnlyList<IPEndPoint> binden, ServerIdentitaet identitaet, bool nurTls13, Zentrale zentrale, Relais? relais, Logbuch log)
    {
        ArgumentNullException.ThrowIfNull(binden);
        ArgumentNullException.ThrowIfNull(identitaet);
        ArgumentNullException.ThrowIfNull(zentrale);
        ArgumentNullException.ThrowIfNull(log);
        _binden = binden;
        _identitaet = identitaet;
        _nurTls13 = nurTls13;
        _zentrale = zentrale;
        _relais = relais;
        _log = log;
    }

    /// <summary>Die tatsächlich gebundenen Endpunkte (bei Port 0 mit dem zugeteilten Port).</summary>
    public IReadOnlyList<IPEndPoint> Endpunkte => [.. _lauscher.Select(s => (IPEndPoint)s.LocalEndPoint!)];

    public void Starten()
    {
        foreach (var ep in _binden)
        {
            var s = new Socket(ep.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (ep.AddressFamily == AddressFamily.InterNetworkV6) s.DualMode = false;
            s.Bind(ep);
            s.Listen(512);
            _lauscher.Add(s);
        }
        foreach (var s in _lauscher) _schleifen.Add(Task.Run(() => AnnehmenAsync(s)));
    }

    private async Task AnnehmenAsync(Socket lauscher)
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket s;
            try
            {
                s = await lauscher.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TooManyOpenSockets or SocketError.NoBufferSpaceAvailable)
            {
                // Keine Dateideskriptoren oder kein Pufferplatz: kurz warten statt heiß zu drehen. Nur hier, denn ein
                // Warten bei jedem abgebrochenen Verbindungsaufbau ließe sich von außen erzwingen.
                if (!await KurzWartenAsync().ConfigureAwait(false)) return;
                continue;
            }
            catch (SocketException)
            {
                continue;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Ohne diesen Fang endete die Annahmeschleife still, und der Server nähme keine Verbindung mehr an.
                _log.Fehler("TLS-Annahme, Lauscher", ex);
                if (!await KurzWartenAsync().ConfigureAwait(false)) return;
                continue;
            }
            _ = Task.Run(() => BehandelnAsync(s));
        }
    }

    /// <summary>50 ms warten; <c>false</c>, wenn die Annahme inzwischen beendet wird.</summary>
    private async Task<bool> KurzWartenAsync()
    {
        try
        {
            await Task.Delay(50, _stop.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task BehandelnAsync(Socket s)
    {
        if (s.RemoteEndPoint is not IPEndPoint gegenstelle)
        {
            s.Dispose();
            return;
        }
        var quelle = Adresse.Aus(gegenstelle);
        var sperrliste = _zentrale.Sperrliste;
        // A51: Eine gesperrte Adresse kostet nur accept und ein RST, kein TLS und keinen Topf.
        if (sperrliste.Pruefen(quelle, Zeit.UnixMs()) == Sperrurteil.Gesperrt)
        {
            Hart(s);
            return;
        }
        var zulassung = _zentrale.Zulassung;
        var urteil = zulassung.TcpPruefen(quelle, Zeit.MonoMs());
        if (urteil != Zulassungsurteil.Zugelassen)
        {
            Hart(s);
            if (urteil == Zulassungsurteil.PraefixTopfLeer) sperrliste.Vermerken(quelle, Sperranlass.HandshakeFlut, Zeit.UnixMs());
            _log.Debug("Verbindung abgewiesen (" + urteil + "): " + _log.Schwaerzung.Praefix(quelle.Praefix));
            return;
        }
        try
        {
            s.NoDelay = true;
            if (OperatingSystem.IsLinux()) NotSentLowat(s);
            var strom = new NetworkStream(s, ownsSocket: true);
            var ssl = new SslStream(strom, leaveInnerStreamOpen: false);
            X509Certificate2? zertifikat = null;
            try
            {
                using var frist = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                frist.CancelAfter(HandshakeFrist);
                await ssl.AuthenticateAsServerAsync(Optionen(), frist.Token).ConfigureAwait(false);
                zertifikat = ssl.RemoteCertificate as X509Certificate2;
                if (ssl.NegotiatedApplicationProtocol != new SslApplicationProtocol(Versionen.Alpn)
                    || !I.Geraetezertifikat.TryAnnehmen(zertifikat, out var geraet))
                {
                    // Ohne vereinbartes ALPN oder ohne P-256-Zertifikat: trennen (4.1, 4.6 TLS).
                    sperrliste.Vermerken(quelle, Sperranlass.OhneGeraetezertifikat, Zeit.UnixMs());
                    await ssl.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                await NachHandshakeAsync(ssl, s, quelle, geraet).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException or OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // Gescheiterter oder abgebrochener Handshake (Scanner, Browser, Abbruch nach dem Serverzertifikat); nicht beim Beenden.
                if (zertifikat is null && !_stop.IsCancellationRequested) sperrliste.Vermerken(quelle, Sperranlass.Handshake, Zeit.UnixMs());
                await ssl.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                zertifikat?.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Fehler("TLS-Annahme", ex);
            s.Dispose();
        }
        finally
        {
            zulassung.TcpBeendet(quelle);
        }
    }

    /// <summary>
    /// Nach dem Handshake (4.1): Gerätesperre (4.8), Einstufung "bekannt"/"unbekannt" über den Fingerabdruck, Platz für
    /// Unfreigegebene (je Präfix 2, global 20; freigegebene Geräte zählen nie), dann die Verbindung bis zu ihrem Ende.
    /// Zusammenspieltests rufen das ohne TLS direkt auf, damit sie denselben Weg gehen.
    /// </summary>
    internal async Task NachHandshakeAsync(Stream strom, Socket? socket, Adresse quelle, I.Fingerabdruck geraet)
    {
        var zulassung = _zentrale.Zulassung;
        var bekannt = IstBekannt(geraet);
        // A51: Ein freigegebenes Gerät hebt eine Sperre seiner Adresse auf (Familie, CGNAT); es kam über den Durchlass.
        if (bekannt && _zentrale.Sperrliste.Entlasten(quelle, Zeit.UnixMs()))
            _log.Info("Sperrliste: Sperre für " + _log.Schwaerzung.Praefix(quelle.Praefix) + " aufgehoben (freigegebenes Gerät)");
        if (zulassung.GeraetGesperrt(geraet, Zeit.MonoMs()))
        {
            await strom.DisposeAsync().ConfigureAwait(false);
            return;
        }
        if (!bekannt && !zulassung.UnbekanntBelegen(quelle))
        {
            _log.Debug("Unfreigegebene Verbindung abgewiesen (Grenze): " + _log.Schwaerzung.Praefix(quelle.Praefix));
            await strom.DisposeAsync().ConfigureAwait(false);
            return;
        }
        using var v = new Verbindung(strom, socket, quelle, geraet, haeltUnbekannt: !bekannt, _zentrale, _relais, _log);
        await v.LaufenAsync().ConfigureAwait(false);
        // Protokollmüll oder gesprengte Ratengrenzen vor der Anmeldung (nur unfreigegebene Geräte, die nie AKTIV wurden).
        if (!bekannt && !v.WarAktiv && v.Verstoss is { } verstoss)
            _zentrale.Sperrliste.Vermerken(quelle, verstoss == Verstossart.Ratengrenze ? Sperranlass.Ratengrenze : Sperranlass.Protokollfehler, Zeit.UnixMs());
    }

    /// <summary>Schließt eine gerade angenommene Verbindung mit RST: kein FIN-Austausch, kein TIME_WAIT auf dem Server.</summary>
    private static void Hart(Socket s)
    {
        try
        {
            s.LingerState = new LingerOption(true, 0);
        }
        catch (SocketException)
        {
        }
        s.Dispose();
    }

    /// <summary>"Bekannt" heißt FREIGEGEBEN mit aktivem Konto (4.1); gelesen am Stand des Kontenbuchs, ohne ihn zu ändern.</summary>
    private bool IstBekannt(I.Fingerabdruck geraet)
    {
        var stand = _zentrale.Konten.Stand;
        return stand.GeraetZuFingerabdruck(geraet) is { Status: I.GeraetStatus.Freigegeben } g
            && stand.Konten.TryGetValue(g.KontoId, out var k) && k.Status == I.KontoStatus.Aktiv;
    }

    private SslServerAuthenticationOptions Optionen()
    {
        var o = new SslServerAuthenticationOptions
        {
            ServerCertificateContext = _identitaet.Kontext,
            EnabledSslProtocols = _nurTls13 ? SslProtocols.Tls13 : SslProtocols.Tls13 | SslProtocols.Tls12,
            ClientCertificateRequired = true,
            // Nur ein Blattzertifikat mit P-256-Schlüssel; Kette und Gültigkeit zählen nicht (3.2, 4.1).
            RemoteCertificateValidationCallback = static (_, zert, _, _) => zert is X509Certificate2 z && I.Geraetezertifikat.TryAnnehmen(z, out _),
            ApplicationProtocols = [new SslApplicationProtocol(Versionen.Alpn)],
            AllowRenegotiation = false,
            AllowTlsResume = false,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };
        if (OperatingSystem.IsLinux()) o.CipherSuitesPolicy = Suiten();
        return o;
    }

    /// <summary>Cipher-Suites nach 4.1 (wirkt nur unter Linux).</summary>
    [SupportedOSPlatform("linux")]
    private static CipherSuitesPolicy Suiten() => new(
    [
        TlsCipherSuite.TLS_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
    ]);

    /// <summary>TCP_NOTSENT_LOWAT = 16 KiB (Ebene IPPROTO_TCP 6, Option 25), damit sich im Kernel keine Sekunden Sprache stauen.</summary>
    [SupportedOSPlatform("linux")]
    private static void NotSentLowat(Socket s)
    {
        try
        {
            s.SetRawSocketOption(6, 25, BitConverter.GetBytes(16 * 1024));
        }
        catch (SocketException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        foreach (var s in _lauscher) s.Dispose();
        foreach (var t in _schleifen)
        {
            try
            {
                await t.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
            }
        }
        _stop.Dispose();
    }
}
