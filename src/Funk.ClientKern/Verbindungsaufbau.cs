// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;

namespace Funk.ClientKern;

/// <summary>
/// Eine aufgebaute Steuerverbindung: der Datenstrom nach dem Handshake (bei TLS der <c>SslStream</c>), dazu die IP-Adresse
/// des Servers, an die später der UDP-Socket verbunden wird (5.1: kein zweites DNS), und die eigene lokale Adresse.
/// </summary>
public sealed class Steuerverbindung : IAsyncDisposable
{
    private readonly IDisposable? _halter;

    public Steuerverbindung(Stream strom, IPAddress serverIp, IPAddress lokaleIp, Socket? socket = null, IDisposable? halter = null, string? beschreibung = null)
    {
        ArgumentNullException.ThrowIfNull(strom);
        ArgumentNullException.ThrowIfNull(serverIp);
        ArgumentNullException.ThrowIfNull(lokaleIp);
        Strom = strom;
        ServerIp = serverIp.IsIPv4MappedToIPv6 ? serverIp.MapToIPv4() : serverIp;
        LokaleIp = lokaleIp.IsIPv4MappedToIPv6 ? lokaleIp.MapToIPv4() : lokaleIp;
        Socket = socket;
        _halter = halter;
        Beschreibung = beschreibung ?? "Steuerverbindung";
    }

    /// <summary>Datenstrom nach dem Handshake; Rahmen nach 4.2.</summary>
    public Stream Strom { get; }

    /// <summary>IP-Adresse der TLS-Gegenstelle; UDP geht an diese Adresse und den Port aus Welcome (5.1).</summary>
    public IPAddress ServerIp { get; }

    /// <summary>Lokale Adresse der Verbindung; an sie bindet der UDP-Socket (nie auf allen Schnittstellen, 5.1).</summary>
    public IPAddress LokaleIp { get; }

    /// <summary>TCP-Socket, falls vorhanden (Sendepuffer im Tunnelmodus).</summary>
    public Socket? Socket { get; }

    /// <summary>Kurzbeschreibung für Anzeige und Log, ohne Adressen.</summary>
    public string Beschreibung { get; }

    /// <summary>
    /// Der Pin (SHA-256 des SPKI), den der Server im Handshake gezeigt hat und der im Profil stand; <c>null</c> ohne TLS (Testzugang,
    /// prozessintern). F1 prüft daran, ob ein Profil aus Welcome den liefernden Server noch enthält (3.1).
    /// </summary>
    public Funk.Protokoll.Identitaet.Fingerabdruck? Pin { get; init; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Strom.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
        _halter?.Dispose();
        Socket?.Dispose();
    }
}

/// <summary>Wie eine Steuerverbindung entsteht: TLS mit Pin (Betrieb), Testzugang auf Loopback (Lasttest) oder prozessintern (Tests).</summary>
public interface IVerbindungsaufbau
{
    /// <summary>
    /// Baut eine Verbindung auf. <paramref name="wiederverbindung"/>: nach dem Verlust einer Sitzung, dann gilt ein
    /// Pin-Fehler zuerst als "falscher Host" (7.1 Schritt 4). Scheitert der Aufbau, kommt eine <see cref="AufbauException"/>.
    /// </summary>
    Task<Steuerverbindung> AufbauenAsync(bool wiederverbindung, CancellationToken abbruch);
}

/// <summary>Grund eines gescheiterten Aufbaus.</summary>
public enum AufbauFehler
{
    /// <summary>Namensauflösung oder TCP gescheitert.</summary>
    Netz,

    /// <summary>TLS-Handshake gescheitert (ohne Pin-Fehler).</summary>
    Tls,

    /// <summary>ALPN nicht vereinbart.</summary>
    Alpn,

    /// <summary>Pin passte nicht; beim Wiederverbinden zuerst "falscher Host".</summary>
    Pin,

    /// <summary>"Server nicht vertrauenswürdig": Pin-Fehler bei der Erstverbindung oder 2 min lang an keiner Adresse ein gültiger Pin.</summary>
    PinAlarm,
}

/// <summary>Aufbau gescheitert; <see cref="Art"/> sagt warum.</summary>
public sealed class AufbauException : Exception
{
    public AufbauException()
    {
    }

    public AufbauException(string meldung) : base(meldung)
    {
    }

    public AufbauException(string meldung, Exception innen) : base(meldung, innen)
    {
    }

    public AufbauException(AufbauFehler art, string meldung, Exception? innen = null) : base(meldung, innen) => Art = art;

    public AufbauFehler Art { get; }
}

/// <summary>Aufbau über einen Delegaten, etwa prozessintern in Zusammenspieltests.</summary>
public sealed class DelegatAufbau(Func<bool, CancellationToken, Task<Steuerverbindung>> aufbauen) : IVerbindungsaufbau
{
    public Task<Steuerverbindung> AufbauenAsync(bool wiederverbindung, CancellationToken abbruch) => aufbauen(wiederverbindung, abbruch);
}
