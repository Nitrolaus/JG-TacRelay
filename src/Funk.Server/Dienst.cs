// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Funk.Protokoll.Identitaet;
using I = Funk.Protokoll.Identitaet;
using Funk.Protokoll.Sprache;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using Funk.Server.Netzzugang;
using Funk.Server.Sprachpfad;
using Funk.Server.Verwaltung;

namespace Funk.Server;

/// <summary>
/// Der lauffähige Dienst: liest Geheimnis und Identität, lädt den Zustand, bindet TCP und UDP je konfigurierter Adresse
/// (4.1, 5.14), startet Zentrale, Relais, TLS-Annahme und Verwaltungssocket. Beim geordneten Beenden gehen
/// <c>Bye{WARTUNG, wiederInMs}</c> an alle Sitzungen (7.2).
/// </summary>
public sealed class Dienst : IAsyncDisposable
{
    /// <summary>So lange sollen die Clients nach einem geordneten Neustart warten (7.2).</summary>
    public const uint WartungWiederInMs = 10_000;

    private readonly List<Socket> _udp = [];
    private Geheimnis? _geheimnis;
    private ServerIdentitaet? _identitaet;
    private Relais? _relais;
    private TlsAnnahme? _annahme;
    private Verwaltungssocket? _verwaltung;

    private Dienst(ServerKonfiguration konfiguration, Logbuch log)
    {
        Konfiguration = konfiguration;
        Log = log;
    }

    public ServerKonfiguration Konfiguration { get; }
    public Logbuch Log { get; }
    public Zentrale Zentrale { get; private set; } = null!;
    public Relais Relais => _relais!;
    public IReadOnlyList<IPEndPoint> TcpEndpunkte => _annahme?.Endpunkte ?? [];
    public IReadOnlyList<IPEndPoint> UdpEndpunkte => [.. _udp.Select(s => (IPEndPoint)s.LocalEndPoint!)];
    public string Verwaltungspfad => _verwaltung?.Pfad ?? Konfiguration.SocketPfad;
    public Fingerabdruck Pin => _identitaet!.Pin;

    /// <summary>Für Zusammenspieltests: eine Verbindung ohne TLS über denselben Weg wie nach dem Handshake bedienen.</summary>
    internal Task NachHandshakeAsync(Stream strom, Socket? socket, Adresse quelle, Fingerabdruck geraet) =>
        _annahme!.NachHandshakeAsync(strom, socket, quelle, geraet);

    /// <summary>
    /// Startet den Dienst. <paramref name="identitaet"/>, <paramref name="geheimnis"/> und <paramref name="totpSchluessel"/>
    /// ersetzen die Dateien aus der Konfiguration (Tests); <paramref name="abgriff"/> bekommt jede Logbuchzeile zusätzlich.
    /// </summary>
    public static Dienst Starten(ServerKonfiguration konfiguration, TextWriter? konsole = null, ServerIdentitaet? identitaet = null,
        Geheimnis? geheimnis = null, Action<string>? abgriff = null, TimeProvider? zeit = null, byte[]? totpSchluessel = null)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);
        var fehler = konfiguration.Pruefen();
        if (fehler.Count > 0) throw new InvalidDataException("Konfiguration ungültig:\n  " + string.Join("\n  ", fehler));

        geheimnis ??= Geheimnis.Laden(konfiguration.Aufloesen(konfiguration.Geheimnis));
        var schwaerzung = new Schwaerzung(geheimnis.Ableiten("funk logbuch"));
        var log = new Logbuch(konfiguration.Log.StufeLesen()!.Value, schwaerzung, konsole,
            konfiguration.Log.Datei is { } d ? konfiguration.Aufloesen(d) : null, abgriff);
        var dienst = new Dienst(konfiguration, log) { _geheimnis = geheimnis };
        try
        {
            dienst.Aufbauen(identitaet, zeit, totpSchluessel);
            return dienst;
        }
        catch
        {
            dienst.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private void Aufbauen(ServerIdentitaet? identitaet, TimeProvider? zeit, byte[]? totpSchluessel)
    {
        var k = Konfiguration;
        _identitaet = identitaet ?? ServerIdentitaet.Laden(k.Aufloesen(k.Identitaet.Zertifikat), k.Aufloesen(k.Identitaet.Schluessel));
        var spur = new Vorrangspur(_geheimnis!.Ableiten("funk vorrangspur"));
        var weiterleiter = k.VertrauenswuerdigeWeiterleiter.Select(IPAddress.Parse).ToList();
        var zulassung = new Zulassung(k.Grenzen, spur, weiterleiter, Zeit.MonoMs());
        var ablage = new Zustandsablage(k.Aufloesen(k.Zustandsverzeichnis));
        var schluessel = totpSchluessel ?? TotpSchluessel.Laden(k.Aufloesen(k.TotpSchluessel));
        I.TotpTresor tresor;
        try
        {
            tresor = new I.TotpTresor(schluessel);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(schluessel);
        }
        try
        {
            Zentrale = new Zentrale(k, Log, ablage, zulassung, spur, tresor, zeit);
        }
        catch
        {
            // Etwa eine unlesbare admins.json: der Schlüssel im Tresor wird trotzdem genullt.
            tresor.Dispose();
            throw;
        }
        // A51: vertrauenswürdige Weiterleiter stehen wie die Ausnahmen nie auf der Sperrliste (aller Verkehr käme von ihnen).
        Zentrale.SperrlisteEinrichten(new Sperrliste(k.Sperrliste, _geheimnis.Ableiten("funk sperrliste"), Log.Schwaerzung, spur,
            [.. k.Sperrliste.AusnahmeNetze(), .. weiterleiter.Select(ip => new IPNetwork(ip, ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128))]));
        // TOTP verträgt nur 30 s: eine offensichtlich falsche Uhr beim Start sagen (im Dienst sperrt ProtectClock das Lesen des Kernels).
        foreach (var warnung in Uhrpruefung.Warnungen(Zentrale.Uhr.GetUtcNow(), Uhrpruefung.KernLesen(), Zentrale.Journal.Juengster)) Log.Warnung(warnung);
        if (!k.Sperrliste.Aktiv) Log.Warnung("Sperrliste ausgeschaltet (sperrliste.aktiv = false): kein Schutz gegen Scanner und Fluten (A51)");
        Zentrale.EigenerPin = _identitaet.Pin;
        if (Zentrale.ProfilFehlerBeimStart is { } profilFehler) throw new InvalidDataException("Serverprofil: " + profilFehler);
        if (Zentrale.ProfilPinWarnung() is { } pinWarnung) Log.Warnung(pinWarnung);
        // Profil und server.json nennen verschiedene öffentliche TCP-Ports: Eines davon stimmt nicht (Profilabgleich).
        if (Zentrale.Profil is { } q && I.Serverprofil.TryLesen(Convert.FromBase64String(q.Daten), out var profil)
            && Profilabgleich.PortWarnung(k, profil.TcpPort) is { } portWarnung)
            Log.Warnung(portWarnung);

        var adressen = k.Bindadressen();
        if (k.AlleAdressen) Log.Warnung("Bindung an alle Adressen (alleAdressen = true), ausdrücklich konfiguriert (4.1)");
        // Prüfung beim Start (ANMELDUNG-SICHERHEIT, Server): ohne Eigentümer gibt es niemanden, der Admins ernennen darf;
        // weitere UIDs am Verwaltungssocket zählen wie root (Nicht abgedeckt: Übernahme des Dienstbenutzers).
        if (Zentrale.Konten.Stand.Eigentuemer is null)
            Log.Warnung("Kein Eigentümer gesetzt: funk-admin eigentuemer --konto <id> (3.10). Bis dahin ist kein Konto im Client vor anderen Admins geschützt.");
        // A53: Discord-Namen sind eindeutig. Ein älterer Stand kann Doppelte tragen; die bleiben, bis ein Admin sie ändert.
        foreach (var doppelt in Zentrale.Konten.Stand.DoppelteDiscordNamen())
            Log.Warnung("Konto " + Log.Schwaerzung.Konto(doppelt.KontoId) + " trägt einen Discord-Namen, den schon ein anderes Konto trägt: funk-admin discordname --konto <id> ändern (konten zeigt die Namen)");
        if (k.VerwaltungsUids.Count > 0)
            Log.Warnung("verwaltungsUids ist nicht leer: diese Benutzer haben am Verwaltungssocket alle Rechte des Eigentümers (3.10).");
        if (weiterleiter.Count > 0) Log.Warnung("Vertrauenswürdige Weiterleiter konfiguriert: für sie gelten nur globale Töpfe (7.6)");

        foreach (var ip in adressen)
        {
            var s = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) s.DualMode = false;
            s.ReceiveBufferSize = k.Grenzen.UdpEmpfangspuffer;
            // ICMP "port unreachable" soll den Empfang nicht abbrechen (SIO_UDP_CONNRESET aus).
            if (OperatingSystem.IsWindows()) s.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
            s.Bind(new IPEndPoint(ip, _udp.Count == 0 ? k.UdpPort : UdpEndpunkte[0].Port));
            _udp.Add(s);
        }
        var udpPort = k.UdpPort == 0 ? (ushort)UdpEndpunkte[0].Port : (ushort)k.UdpPortOeffentlich;
        Zentrale.UdpPortFuerWelcome = k.OeffentlicherUdpPort is { } oeffentlich ? (ushort)oeffentlich : udpPort;

        var rotation = k.Test?.RotationSekunden is { } sek
            ? Rotationsgrenzen.Standard with { EpochenDauer = TimeSpan.FromSeconds(sek) }
            : Rotationsgrenzen.Standard;
        _relais = new Relais(() => Zentrale.Schreiber.Aktuell, new RelaisBruecke(Zentrale), Log,
            new RelaisOptionen { AusgangMbit = k.Grenzen.AusgangMbit, LeitungsZuschlag = k.Grenzen.LeitungsZuschlag, Kapazitaet = k.Grenzen.RelaisKapazitaet, Rotation = rotation },
            _udp);
        Zentrale.Relais = _relais;
        Zentrale.Starten();
        _relais.Starten();

        _annahme = new TlsAnnahme([.. adressen.Select(ip => new IPEndPoint(ip, k.TcpPort))], _identitaet, k.NurTls13, Zentrale, _relais, Log);
        _annahme.Starten();
        _verwaltung = new Verwaltungssocket(k.SocketPfad, Zentrale, Log, k.VerwaltungsUids);
        _verwaltung.Starten();

        var tcp = TcpEndpunkte[0].Port;
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Funk-Server {Zentrale.Version} bereit: {adressen.Count} Adresse(n), TCP Bindport {tcp} (öffentlich {(k.TcpPort == 0 ? tcp : k.TcpPortOeffentlich)}), UDP Bindport {UdpEndpunkte[0].Port} (öffentlich {Zentrale.UdpPortFuerWelcome}), TLS {(k.NurTls13 ? "1.3" : "1.3/1.2")}, Pin {_identitaet.Pin.Anzeige}"));
    }

    /// <summary>Geordnetes Beenden (7.2): Bye{WARTUNG} an alle, dann alles schließen.</summary>
    public async Task BeendenAsync(uint wiederInMs = WartungWiederInMs)
    {
        if (Zentrale is not null && _relais is not null)
        {
            try
            {
                await Zentrale.Ausfuehren(() =>
                {
                    // 7.2: an alle, auch an Verbindungen in REGISTRIERUNG und WARTERAUM (Bye ist in jedem Zustand erlaubt).
                    foreach (var v in Zentrale.AlleVerbindungen()) v.Wartung(wiederInMs);
                    return 0;
                }).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                await Task.Delay(300).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
            {
            }
        }

        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_verwaltung is not null) await _verwaltung.DisposeAsync().ConfigureAwait(false);
        if (_annahme is not null) await _annahme.DisposeAsync().ConfigureAwait(false);
        _relais?.Dispose();
        foreach (var s in _udp) s.Dispose();
        _udp.Clear();
        if (Zentrale is not null) await Zentrale.DisposeAsync().ConfigureAwait(false);
        _identitaet?.Dispose();
        _geheimnis?.Dispose();
        Log.Dispose();
    }
}
