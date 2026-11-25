// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Funk.ClientKern;
using Funk.Server;
using Funk.Server.Betrieb;
using Funk.Server.Netzzugang;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Testserver;

/// <summary>
/// Der Testzugang auf Loopback: statt TLS beweist der Client den Besitz seines Geräteschlüssels mit einer Signatur über einen
/// Zufallswert (<see cref="Funk.ClientKern.Testzugang"/>), danach geht die Verbindung denselben Weg wie nach einem
/// TLS-Handshake. Zulassung wie bei TLS (Töpfe je Präfix, gleichzeitige Verbindungen). Mit <paramref name="freigabe"/>
/// wird ein unbekanntes Gerät vor Hello ohne Handarbeit freigegeben (<see cref="Probefreigabe"/>); das gibt es nur hier.
/// </summary>
public sealed class Testzugangsannahme : IAsyncDisposable
{
    private readonly Socket _lauscher;
    private readonly Dienst _dienst;
    private readonly Func<I.Fingerabdruck, Task<string?>>? _freigabe;
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _annahme;

    /// <param name="freigabe">Für unbekannte Geräte vor Hello; liefert <c>null</c> bei Erfolg, sonst den Grund.</param>
    public Testzugangsannahme(Dienst dienst, IPEndPoint ort, Func<I.Fingerabdruck, Task<string?>>? freigabe = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(ort);
        if (!Testzugang.IstLoopback(ort.Address)) throw new ArgumentException("Den Testzugang gibt es nur auf Loopback.", nameof(ort));
        _dienst = dienst;
        _freigabe = freigabe;
        _log = log;
        _lauscher = new Socket(ort.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            _lauscher.Bind(ort);
            _lauscher.Listen(512);
        }
        catch
        {
            _lauscher.Dispose();
            throw;
        }
        Endpunkt = (IPEndPoint)_lauscher.LocalEndPoint!;
        _annahme = Task.Run(AnnehmenAsync);
    }

    /// <summary>Der gebundene Endpunkt (bei Port 0 mit dem zugeteilten Port).</summary>
    public IPEndPoint Endpunkt { get; }

    private async Task AnnehmenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket s;
            try
            {
                s = await _lauscher.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (_stop.IsCancellationRequested || ex is ObjectDisposedException) return;
                continue;
            }
            _ = Task.Run(() => BehandelnAsync(s), CancellationToken.None);
        }
    }

    // Zulassung wie bei TLS, dann der Besitznachweis, dann (auf Wunsch) die Freigabe, dann der Weg nach dem Handshake.
    private async Task BehandelnAsync(Socket s)
    {
        if (s.RemoteEndPoint is not IPEndPoint gegenstelle || !Testzugang.IstLoopback(gegenstelle.Address))
        {
            s.Dispose();
            return;
        }
        var quelle = Adresse.Aus(gegenstelle);
        var zulassung = _dienst.Zentrale.Zulassung;
        if (zulassung.TcpPruefen(quelle, Zeit.MonoMs()) != Zulassungsurteil.Zugelassen)
        {
            s.Dispose();
            return;
        }
        try
        {
            s.NoDelay = true;
            var strom = new NetworkStream(s, ownsSocket: true);
            I.Fingerabdruck geraet;
            try
            {
                using var frist = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var anfang = new byte[Testzugang.AnfangLaenge];
                await strom.ReadExactlyAsync(anfang, frist.Token).ConfigureAwait(false);
                if (!anfang.AsSpan(0, 5).SequenceEqual(Testzugang.Kennung))
                {
                    await strom.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                var zufall = RandomNumberGenerator.GetBytes(Testzugang.ZufallLaenge);
                await strom.WriteAsync(zufall, frist.Token).ConfigureAwait(false);
                var signatur = new byte[Testzugang.SignaturLaenge];
                await strom.ReadExactlyAsync(signatur, frist.Token).ConfigureAwait(false);
                if (!Testzugang.NachweisPruefen(anfang.AsSpan(5), zufall, signatur, out geraet))
                {
                    await strom.DisposeAsync().ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or EndOfStreamException or SocketException)
            {
                await strom.DisposeAsync().ConfigureAwait(false);
                return;
            }
            if (_freigabe is not null && await _freigabe(geraet).ConfigureAwait(false) is { } grund)
                _log?.Invoke("Automatische Freigabe gescheitert (" + geraet.Anzeige + "): " + grund);
            await _dienst.NachHandshakeAsync(strom, s, quelle, geraet).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException
            or InvalidOperationException)
        {
            s.Dispose();
        }
        finally
        {
            zulassung.TcpBeendet(quelle);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _lauscher.Dispose();
        try
        {
            await _annahme.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
        }
        _stop.Dispose();
    }
}
