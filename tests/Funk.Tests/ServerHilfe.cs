// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;
using Funk.Server;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using Funk.Server.Verwaltung;
using I = Funk.Protokoll.Identitaet;
using SigniertesProfilI = Funk.Protokoll.Identitaet.SigniertesProfil;

namespace Funk.Tests;

/// <summary>
/// Ein Server auf Loopback (127.0.0.1, zugeteilte Ports) mit Zustand in einem eigenen Testverzeichnis, Profil und
/// Identität nur zur Laufzeit erzeugt. Ohne <c>tls</c> gehen Verbindungen am Handshake vorbei über denselben Weg
/// wie danach (Dienst.NachHandshakeAsync); so schreibt unter Windows nichts ins Benutzerprofil.
/// </summary>
internal sealed class Testserver : IAsyncDisposable
{
    private readonly X509Certificate2 _zertifikat;
    private readonly ConcurrentQueue<string> _log = new();

    private Testserver(string verzeichnis, Dienst dienst, X509Certificate2 zertifikat)
    {
        Verzeichnis = verzeichnis;
        Dienst = dienst;
        _zertifikat = zertifikat;
    }

    public string Verzeichnis { get; }
    public Dienst Dienst { get; private set; }
    public IReadOnlyCollection<string> Logbuch => _log;

    /// <summary>TOTP-Schlüssel dieses Servers (nur Tests, nur im Speicher).</summary>
    public byte[] TotpSchluessel { get; private set; } = [];

    private static readonly ConcurrentDictionary<string, byte[]> TotpJeVerzeichnis = new(StringComparer.OrdinalIgnoreCase);

    private static byte[] TotpSchluesselFuer(string verzeichnis) =>
        TotpJeVerzeichnis.GetOrAdd(Path.GetFullPath(verzeichnis), _ => RandomNumberGenerator.GetBytes(32));

    /// <summary>Unter Windows braucht ein echter Handshake einen persistierten Schlüssel im Benutzerprofil (Schannel).</summary>
    public static bool TlsErlaubt => !OperatingSystem.IsWindows()
        || string.Equals(Environment.GetEnvironmentVariable("FUNK_TLS_IM_PROFIL"), "1", StringComparison.Ordinal);

    /// <summary>
    /// Wurzel aller Testdaten: <c>FUNK_TESTVERZEICHNIS</c>, sonst unter Windows <c>C:\Claude\tmp\tr-servertests</c>
    /// (angelegt vom ersten Test, der schreibt). Ohne C:\Claude bricht der Lauf ab, statt ins Temp-Verzeichnis des
    /// Benutzers auszuweichen: Dort blieben nach einem Absturz Schlüssel, Profile und Einstellungen liegen. Unter Linux das
    /// Temp-Verzeichnis.
    /// </summary>
    public static string Wurzel()
    {
        var gesetzt = Environment.GetEnvironmentVariable("FUNK_TESTVERZEICHNIS");
        if (!string.IsNullOrEmpty(gesetzt)) return gesetzt;
        if (!OperatingSystem.IsWindows()) return Path.Combine(Path.GetTempPath(), "tr-servertests");
        if (!Directory.Exists(@"C:\Claude"))
            throw new InvalidOperationException(@"C:\Claude fehlt: FUNK_TESTVERZEICHNIS auf einen Testordner setzen. Ins Temp-Verzeichnis des Benutzers weichen die Tests nicht aus.");
        return @"C:\Claude\tmp\tr-servertests";
    }

    /// <param name="profilBehalten">
    /// Bei einem Neustart im selben Verzeichnis das vorhandene profil.json lassen, wie <c>systemctl restart</c> es tut (UMZUG.md,
    /// Schritt 4). Ohne den Schalter schreibt jeder Start ein frisches Profil mit dem Pin des neuen Testzertifikats.
    /// </param>
    public static async Task<Testserver> StartenAsync(bool tls = false, GrenzenKonfiguration? grenzen = null, string? verzeichnis = null,
        int? rotationSekunden = null, TimeProvider? zeit = null, KartenKonfiguration? karten = null, SperrlistenKonfiguration? sperrliste = null,
        bool profilBehalten = false)
    {
        verzeichnis ??=Path.Combine(Wurzel(), Guid.NewGuid().ToString("N")[..16]);
        Directory.CreateDirectory(verzeichnis);
        using var serverSchluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var anfrage = new CertificateRequest("CN=Funk-Server", serverSchluessel, HashAlgorithmName.SHA256);
        var zert = anfrage.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddYears(1));
        var profil = Path.Combine(verzeichnis, "profil.json");
        if (!profilBehalten || !File.Exists(profil)) ProfilSchreiben(profil, I.Fingerabdruck.AusZertifikat(zert));

        var g = grenzen ?? new GrenzenKonfiguration();
        var k = new ServerKonfiguration
        {
            Binden = ["127.0.0.1"],
            TcpPort = 0,
            UdpPort = 0,
            Zustandsverzeichnis = Path.Combine(verzeichnis, "zustand"),
            Profil = Path.Combine(verzeichnis, "profil.json"),
            VerwaltungsSocket = Path.Combine(verzeichnis, "v.sock"),
            MindestClientVersion = "0.1.0",
            Grenzen = g,
            Log = new ProtokollKonfiguration { Stufe = "DEBUG" },
            Test = new TestKonfiguration { ZufallsPorts = true, RotationSekunden = rotationSekunden },
            Mitgliedskarte = karten ?? new KartenKonfiguration(),
            Sperrliste = sperrliste ?? new SperrlistenKonfiguration(),
            Basisverzeichnis = verzeichnis,
        };
        var t = new Testserver(verzeichnis, null!, zert);
        // Der TOTP-Schlüssel lebt nur im Speicher; ein Neustart im selben Verzeichnis (7.2) nimmt denselben.
        t.TotpSchluessel = TotpSchluesselFuer(verzeichnis);
        t.Dienst = Dienst.Starten(k, null, ServerIdentitaet.AusZertifikat(zert, schannelTauglich: tls), Geheimnis.Zufaellig(), z => t._log.Enqueue(z),
            zeit, t.TotpSchluessel.ToArray());
        await Task.Yield();
        return t;
    }

    /// <summary>Ein signiertes Profil mit dem Pin des Servers und einem Reserve-Pin; der Profilschlüssel lebt nur im Speicher.</summary>
    private static void ProfilSchreiben(string pfad, I.Fingerabdruck pin)
    {
        using var profilSchluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var reserve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jetzt = DateTimeOffset.UtcNow;
        var profil = new Serverprofil("Test", 2, jetzt.ToUnixTimeMilliseconds(), jetzt.AddDays(30).ToUnixTimeMilliseconds(),
            [new ProfilHost("localhost", true, false)], 443, [pin, I.Fingerabdruck.AusSchluessel(reserve)], []);
        File.WriteAllBytes(pfad, SigniertesProfilI.Signieren(profil.AlsJson(), profilSchluessel).AlsJson());
    }

    public Task<VerwaltungsAntwort> Admin(string befehl, JsonObject? argumente = null) =>
        Verwaltungsklient.SendenAsync(Dienst.Verwaltungspfad, befehl, argumente ?? [], TimeSpan.FromSeconds(150));

    public async Task<VerwaltungsAntwort> AdminOk(string befehl, JsonObject? argumente = null)
    {
        var a = await Admin(befehl, argumente);
        Pruefe.Wahr(a.Ok, $"Admin {befehl}: {a.Text}");
        return a;
    }

    /// <summary>Verbindung ohne TLS über den Weg nach dem Handshake.</summary>
    public async Task<Testfunker> VerbindenAsync(ECDsa? schluessel = null, IPAddress? lokal = null)
    {
        schluessel ??= ECDsa.Create(ECCurve.NamedCurves.nistP256);
        lokal ??= IPAddress.Loopback;
        var fp = I.Fingerabdruck.AusSchluessel(schluessel);
        using var lauscher = new TcpListener(IPAddress.Loopback, 0);
        lauscher.Start();
        var client = new TcpClient(new IPEndPoint(lokal, 0)) { NoDelay = true };
        var annahme = lauscher.AcceptSocketAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)lauscher.LocalEndpoint).Port);
        var serverSeite = await annahme;
        var quelle = Adresse.Aus((IPEndPoint)serverSeite.RemoteEndPoint!);
        var lauf = Task.Run(() => Dienst.NachHandshakeAsync(new NetworkStream(serverSeite, ownsSocket: true), serverSeite, quelle, fp));
        return new Testfunker(client.GetStream(), client, schluessel, fp, lauf, this) { Lokal = lokal };
    }

    /// <summary>Verbindung mit echtem TLS und Client-Zertifikat an den TCP-Endpunkt des Dienstes.</summary>
    public async Task<(SslStream? Strom, Exception? Fehler)> TlsVerbindenAsync(X509Certificate2? clientZertifikat, string alpn = Versionen.Alpn)
    {
        var client = new TcpClient();
        await client.ConnectAsync(Dienst.TcpEndpunkte[0]);
        var ssl = new SslStream(client.GetStream(), false);
        var pin = Dienst.Pin;
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                ClientCertificates = clientZertifikat is null ? null : [clientZertifikat],
                ApplicationProtocols = [new SslApplicationProtocol(alpn)],
                RemoteCertificateValidationCallback = (_, z, _, _) => z is X509Certificate2 x && I.Fingerabdruck.AusZertifikat(x) == pin,
                AllowRenegotiation = false,
            }).WaitAsync(TimeSpan.FromSeconds(10));
            return (ssl, null);
        }
        catch (Exception e) when (e is AuthenticationException or IOException or TimeoutException)
        {
            await ssl.DisposeAsync();
            client.Dispose();
            return (null, e);
        }
    }

    /// <summary>Bringt ein neues Gerät bis AKTIV: Hello, Registrierung, Verknüpfen per Admin-Werkzeug, Freigabe, Sync.</summary>
    public async Task<Testfunker> AktivAsync(string discord, string benutzer, string stufe = "FUNKER", IPAddress? lokal = null)
    {
        var f = await VerbindenAsync(null, lokal);
        var w = await f.HelloAsync();
        Pruefe.Gleich(WelcomeStatus.Unbekannt, w.Status, "Welcome für ein neues Gerät");
        var code = await f.RegistrierenAsync("PC " + benutzer);
        var verknuepfen = Admin("verknuepfen", new JsonObject { ["code"] = code, ["discord"] = discord, ["benutzername"] = benutzer, ["stufe"] = stufe });
        var rueckfrage = await f.ErwarteAsync(Nachrichtentyp.VerknuepfungBestaetigen);
        await f.AntwortAsync(Nachrichtentyp.VerknuepfungBestaetigen, rueckfrage.Kopf.AnfrageNr, new VerknuepfungBestaetigenAntwort { Ja = true });
        var v = await verknuepfen;
        Pruefe.Wahr(v.Ok, "verknuepfen: " + v.Text);
        var wartet = (await f.ErwarteAsync(Nachrichtentyp.GeraetStatus)).Json<Funk.Protokoll.Steuerung.GeraetStatus>();
        Pruefe.Gleich(GeraeteStatus.Wartet, wartet.Status, "GeraetStatus nach der Verknüpfung");
        await AdminOk("freigeben", new JsonObject { ["geraet"] = f.Fingerabdruck.AlsBase64() });
        await f.SyncErwartenAsync();
        return f;
    }

    /// <summary>Beendet nur den Dienst; das Verzeichnis bleibt für einen Neustart (7.2).</summary>
    public async Task DienstBeendenAsync()
    {
        _dienstBeendet = true;
        await Dienst.DisposeAsync();
    }

    private bool _dienstBeendet;

    /// <summary>Geordnetes Beenden mit Bye{WARTUNG} (7.2).</summary>
    public async Task GeordnetBeendenAsync(uint wiederInMs)
    {
        _dienstBeendet = true;
        await Dienst.BeendenAsync(wiederInMs);
    }

    /// <summary>Verbindung mit echtem TLS; danach derselbe Testfunker wie ohne TLS.</summary>
    public async Task<(Testfunker? Funker, Exception? Fehler)> TlsFunkerAsync(ECDsa schluessel, X509Certificate2 zertifikat, string alpn = Versionen.Alpn)
    {
        var (ssl, fehler) = await TlsVerbindenAsync(zertifikat, alpn);
        if (ssl is null) return (null, fehler);
        return (new Testfunker(ssl, ssl, schluessel, I.Fingerabdruck.AusSchluessel(schluessel), Task.CompletedTask, this), null);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_dienstBeendet) await Dienst.DisposeAsync();
        _zertifikat.Dispose();
        try
        {
            Directory.Delete(Verzeichnis, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Ein empfangener Steuerrahmen.</summary>
internal sealed record Rahmenkopie(RahmenKopf Kopf, byte[] Body)
{
    public Nachrichtentyp Typ => (Nachrichtentyp)Kopf.Typ;

    public T Json<T>() where T : class => SteuerJson.LesenOderWerfen<T>(Body);
}

/// <summary>
/// Minimaler Client für Zusammenspieltests: Rahmen senden und sammeln, Ping alle 2 s, VoiceKeys selbst beantworten
/// (START und ROTATION), UDP mit Anmeldung, Pfadprüfung, Keepalive und Sprache.
/// </summary>
internal sealed class Testfunker : IAsyncDisposable
{
    private readonly Stream _strom;
    private readonly IDisposable _halter;
    private readonly RahmenSchreiber _schreiber;
    private readonly SemaphoreSlim _schreibSperre = new(1, 1);
    private readonly List<Rahmenkopie> _eingang = [];
    private readonly ConcurrentQueue<Rahmenkopie> _verlauf = new();
    private readonly SemaphoreSlim _neu = new(0, int.MaxValue);
    private readonly CancellationTokenSource _ende = new();
    private readonly Task _lesen;
    private readonly Task _ping;
    private readonly Testserver _server;
    private uint _nr;
    private Socket? _udp;
    private SendeKanal? _hoch;
    private EmpfangsKanal? _runter;
    private readonly byte[] _klartext = new byte[Sprachkanal.MaxKlartext];

    public Testfunker(Stream strom, IDisposable halter, ECDsa schluessel, I.Fingerabdruck fp, Task serverLauf, Testserver server)
    {
        _strom = strom;
        _halter = halter;
        Schluessel = schluessel;
        Fingerabdruck = fp;
        ServerLauf = serverLauf;
        _server = server;
        _schreiber = new RahmenSchreiber(strom);
        _lesen = Task.Run(LeseschleifeAsync);
        _ping = Task.Run(PingschleifeAsync);
    }

    public ECDsa Schluessel { get; }
    public I.Fingerabdruck Fingerabdruck { get; }
    public Task ServerLauf { get; }
    public bool Getrennt { get; private set; }
    public uint SitzungsId { get; private set; }
    public uint KontoId { get; private set; }
    public byte Epoche => _hoch?.Epoche ?? 0;
    public bool VoiceKeysAutomatisch { get; set; } = true;

    /// <summary>Alle empfangenen Rahmen in der Reihenfolge der Ankunft, auch die schon entnommenen.</summary>
    public IReadOnlyList<Rahmenkopie> Verlauf => [.. _verlauf];

    /// <summary>Lokale Adresse für TCP und UDP (verschiedene 127.0.0.x ergeben verschiedene Präfixe).</summary>
    public IPAddress Lokal { get; init; } = IPAddress.Loopback;
    public int VoiceKeysErhalten { get; private set; }

    private async Task LeseschleifeAsync()
    {
        using var leser = new RahmenLeser(_strom);
        try
        {
            while (true)
            {
                var r = await leser.LesenAsync(Rahmengrenzen.ClientEmpfang, _ende.Token);
                if (r.Status != LeseStatus.Rahmen) break;
                var kopie = new Rahmenkopie(r.Kopf, r.Body.ToArray());
                if (kopie.Typ == Nachrichtentyp.VoiceKeys && kopie.Kopf.Art == Nachrichtenart.Anfrage && VoiceKeysAutomatisch)
                {
                    var epoche = kopie.Body[0];
                    VoiceKeysInstallieren(kopie.Body);
                    await RohAsync((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, kopie.Kopf.AnfrageNr, [epoche]);
                }
                if (kopie.Typ == Nachrichtentyp.Welcome && kopie.Json<Welcome>() is { Status: WelcomeStatus.Freigegeben } w)
                {
                    SitzungsId = w.SitzungsId ?? 0;
                    KontoId = w.Konto!.Id;
                }
                _verlauf.Enqueue(kopie);
                lock (_eingang) _eingang.Add(kopie);
                _neu.Release();
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            Getrennt = true;
            _neu.Release();
        }
    }

    private async Task PingschleifeAsync()
    {
        try
        {
            while (!_ende.IsCancellationRequested)
            {
                await Task.Delay(1500, _ende.Token);
                // Ping alle 2 s ab Hello (4.5), nicht vorher: in WARTE_HELLO ist nur Hello erlaubt.
                if (!Getrennt && _helloGesendet) await SendenAsync(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, NaechsteNr());
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or NotSupportedException)
        {
        }
    }

    public uint NaechsteNr() => Interlocked.Increment(ref _nr);

    public async Task SendenAsync<T>(Nachrichtentyp typ, T body, uint nr = 0, byte flags = 0)
    {
        await _schreibSperre.WaitAsync();
        try
        {
            await _schreiber.SchreibenAsync(typ, flags, nr, body);
        }
        finally
        {
            _schreibSperre.Release();
        }
    }

    public async Task RohAsync(ushort typ, byte flags, uint nr, byte[] body)
    {
        await _schreibSperre.WaitAsync();
        try
        {
            await _schreiber.SchreibenAsync(typ, flags, nr, body);
        }
        finally
        {
            _schreibSperre.Release();
        }
    }

    public Task AntwortAsync<T>(Nachrichtentyp typ, uint nr, T body) => SendenAsync(typ, body, nr, Rahmen.FlagAntwort);

    /// <summary>Stellt eine Anfrage und wartet auf ihre Antwort (gleicher Typ oder Fehler).</summary>
    public async Task<Rahmenkopie> AnfrageAsync<T>(Nachrichtentyp typ, T body, TimeSpan? frist = null)
    {
        var nr = NaechsteNr();
        await SendenAsync(typ, body, nr);
        return await ErwarteAsync(r => r.Kopf.IstAntwort && r.Kopf.AnfrageNr == nr, frist);
    }

    public Task<Rahmenkopie> ErwarteAsync(Nachrichtentyp typ, TimeSpan? frist = null) =>
        ErwarteAsync(r => r.Typ == typ && !r.Kopf.IstAntwort, frist);

    public async Task<Rahmenkopie> ErwarteAsync(Func<Rahmenkopie, bool> passt, TimeSpan? frist = null)
    {
        using var abbruch = new CancellationTokenSource(frist ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            lock (_eingang)
            {
                var i = _eingang.FindIndex(r => passt(r));
                if (i >= 0)
                {
                    var r = _eingang[i];
                    _eingang.RemoveAt(i);
                    return r;
                }
                if (Getrennt) throw new PruefungException("Verbindung getrennt, erwarteter Rahmen kam nicht. Vorhanden: " + Vorhanden());
            }
            try
            {
                await _neu.WaitAsync(abbruch.Token);
            }
            catch (OperationCanceledException)
            {
                lock (_eingang) throw new PruefungException("Zeitablauf, erwarteter Rahmen kam nicht. Vorhanden: " + Vorhanden());
            }
        }
    }

    /// <summary>
    /// Nur unter der Sperre von _eingang: die Typen im Eingang, ein Fehler mit seinem Inhalt (Code, Bezug, Text). Ohne ihn ließ
    /// sich der seltene Ausfall in HaertungTests (Reparaturweg A, nur in ganzen Läufen, 01.10.2026) nicht deuten.
    /// </summary>
    private string Vorhanden() => string.Join(", ", _eingang.Select(e => e.Typ == Nachrichtentyp.Fehler
        ? "Fehler " + System.Text.Encoding.UTF8.GetString(e.Body.AsSpan(0, Math.Min(e.Body.Length, 300)))
        : e.Typ.ToString()));

    /// <summary>Ob bis jetzt ein Rahmen dieser Art ankam (ohne ihn zu entnehmen).</summary>
    public bool Hat(Func<Rahmenkopie, bool> passt)
    {
        lock (_eingang) return _eingang.Exists(r => passt(r));
    }

    public async Task WartenAufTrennungAsync(TimeSpan frist)
    {
        var bis = DateTime.UtcNow + frist;
        while (!Getrennt && DateTime.UtcNow < bis) await Task.Delay(20);
        Pruefe.Wahr(Getrennt, "Server hat getrennt");
    }

    private volatile bool _helloGesendet;

    public async Task<Welcome> HelloAsync(string version = "1.0.0")
    {
        _helloGesendet = true;
        await SendenAsync(Nachrichtentyp.Hello, new Hello
        {
            Minor = 0,
            Client = new ClientAngabe { Name = "Testfunker", Version = version },
            SchluesselArt = Funk.Protokoll.Steuerung.SchluesselArt.Software,
            Opus = "1.6.1",
            Faehigkeiten = ["tunnel"],
        });
        return (await ErwarteAsync(Nachrichtentyp.Welcome)).Json<Welcome>();
    }

    public async Task<string> RegistrierenAsync(string geraetename)
    {
        var a = await AnfrageAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = geraetename });
        Pruefe.Gleich(Nachrichtentyp.Registrierung, a.Typ, "Antwort auf Registrierung");
        return a.Json<RegistrierungAntwort>().Code ?? throw new PruefungException("Registrierung ohne Code");
    }

    /// <summary>Nach der Freigabe: GeraetStatus FREIGEGEBEN, Welcome, NetzListe, Mitgliedschaften, Praesenz, Pegel, Zustand.</summary>
    public async Task SyncErwartenAsync()
    {
        var g = (await ErwarteAsync(Nachrichtentyp.GeraetStatus)).Json<Funk.Protokoll.Steuerung.GeraetStatus>();
        Pruefe.Gleich(GeraeteStatus.Freigegeben, g.Status, "GeraetStatus nach der Freigabe");
        var w = (await ErwarteAsync(Nachrichtentyp.Welcome)).Json<Welcome>();
        Pruefe.Gleich(WelcomeStatus.Freigegeben, w.Status, "Welcome nach der Freigabe");
        await ErwarteAsync(Nachrichtentyp.NetzListe);
        await ErwarteAsync(Nachrichtentyp.Mitgliedschaften);
        await ErwarteAsync(Nachrichtentyp.Praesenz);
        await ErwarteAsync(Nachrichtentyp.PegelVoreinstellungen);
        await ErwarteAsync(Nachrichtentyp.Zustand);
    }

    // ---- Sprachkanal ----

    private void VoiceKeysInstallieren(byte[] body)
    {
        var epoche = body[0];
        var material = body.AsSpan(2, Schluesselmaterial.Laenge);
        var jetzt = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (_klartext)
        {
            if (_hoch is null)
            {
                _hoch = new SendeKanal(SitzungsId, Richtung.Hoch, Schluesselmaterial.Hoch(epoche, material), jetzt);
                _runter = new EmpfangsKanal(SitzungsId, Richtung.Runter, Schluesselmaterial.Runter(epoche, material));
            }
            else
            {
                _hoch.Umschalten(Schluesselmaterial.Hoch(epoche, material), jetzt);
                _runter!.Umschalten(Schluesselmaterial.Runter(epoche, material), jetzt);
            }
            VoiceKeysErhalten++;
        }
        CryptographicOperations.ZeroMemory(body);
    }

    /// <summary>UDP-Socket auf einem Ephemeral-Port, verbunden mit dem UDP-Endpunkt des Servers (5.1, 5.14).</summary>
    public void UdpVerbinden()
    {
        _udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows()) _udp.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        _udp.Bind(new IPEndPoint(Lokal, 0));
        _udp.Connect(_server.Dienst.UdpEndpunkte[0]);
    }

    public void Versiegeln(PaketTyp typ, ReadOnlySpan<byte> klartext, out byte[] datagramm)
    {
        var puffer = new byte[Sprachkanal.MaxDatagramm];
        lock (_klartext)
        {
            if (!_hoch!.Versiegeln(typ, klartext, puffer, System.Diagnostics.Stopwatch.GetTimestamp(), out var n))
                throw new PruefungException("Versiegeln verweigert");
            datagramm = puffer[..n];
        }
    }

    public void UdpSenden(PaketTyp typ, ReadOnlySpan<byte> klartext)
    {
        Versiegeln(typ, klartext, out var d);
        _udp!.Send(d);
    }

    public void RohUdpSenden(byte[] datagramm) => _udp!.Send(datagramm);

    /// <summary>NAT-Rebinding nachstellen: neuer Socket, neuer Quellport, dieselben Schlüssel und Zähler.</summary>
    public void UdpPortWechseln()
    {
        _udp?.Dispose();
        UdpVerbinden();
    }

    /// <summary>Beantwortet die nächste PFAD_PRUEFUNG sofort mit PFAD_ANTWORT (5.10).</summary>
    public async Task<bool> PfadPruefungBeantwortenAsync(TimeSpan frist)
    {
        var bis = DateTime.UtcNow + frist;
        while (DateTime.UtcNow < bis)
        {
            var r = await UdpEmpfangenAsync(bis - DateTime.UtcNow);
            if (r is not { } p) return false;
            if (p.Kopf.Typ != PaketTyp.PfadPruefung) continue;
            UdpSenden(PaketTyp.PfadAntwort, p.Klartext);
            return true;
        }
        return false;
    }

    /// <summary>Wartet auf ein gültiges runter-Datagramm und entschlüsselt es.</summary>
    public async Task<(SprachKopf Kopf, byte[] Klartext)?> UdpEmpfangenAsync(TimeSpan frist)
    {
        var puffer = new byte[2048];
        using var abbruch = new CancellationTokenSource(frist);
        while (true)
        {
            int n;
            try
            {
                n = await _udp!.ReceiveAsync(puffer, SocketFlags.None, abbruch.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            if (Oeffnen(puffer.AsSpan(0, n)) is { } ergebnis) return ergebnis;
        }
    }

    public (SprachKopf Kopf, byte[] Klartext)? Oeffnen(ReadOnlySpan<byte> datagramm)
    {
        lock (_klartext)
        {
            if (Empfangspruefung.Vorpruefen(datagramm, false, Richtung.Runter, out var kopf) != Empfangsergebnis.Angenommen) return null;
            if (_runter!.Oeffnen(datagramm, _klartext, System.Diagnostics.Stopwatch.GetTimestamp()) != Empfangsergebnis.Angenommen) return null;
            return (kopf, _klartext[..(datagramm.Length - Sprachkanal.Ueberhang)]);
        }
    }

    /// <summary>Erstbindung (5.10): ANMELDUNG alle 250 ms, PFAD_PRUEFUNG beantworten, bis UdpStatus{GEBUNDEN} kommt.</summary>
    public async Task UdpBindenAsync()
    {
        var anmeldung = new byte[Steuerpakete.Laenge];
        for (var versuch = 0; versuch < 12; versuch++)
        {
            UdpSenden(PaketTyp.Anmeldung, anmeldung);
            var r = await UdpEmpfangenAsync(TimeSpan.FromMilliseconds(250));
            if (r is not { } p || p.Kopf.Typ != PaketTyp.PfadPruefung) continue;
            UdpSenden(PaketTyp.PfadAntwort, p.Klartext);
            var status = (await ErwarteAsync(Nachrichtentyp.UdpStatus)).Json<UdpStatus>();
            Pruefe.Gleich(UdpZustand.Gebunden, status.Zustand, "UdpStatus nach der Pfadprüfung");
            return;
        }
        throw new PruefungException("Keine PFAD_PRUEFUNG auf ANMELDUNG");
    }

    /// <summary>SPRACHE_HOCH (5.4) mit Zielart NETZ oder DURCHSAGE.</summary>
    public void SpracheSenden(ushort[] ziele, ReadOnlySpan<byte> opus, uint frame, ushort phase, Kennzeichen kz = Kennzeichen.None, Zielart art = Zielart.Netz)
    {
        Span<byte> klartext = stackalloc byte[Sprachkanal.MaxKlartext];
        var n = SpracheHoch.Schreiben(klartext, frame, phase, kz, art, ziele, opus);
        UdpSenden(PaketTyp.SpracheHoch, klartext[..n]);
    }

    /// <summary>Datagramm als SpracheTunnel-Rahmen (5.12).</summary>
    public async Task TunnelSendenAsync(PaketTyp typ, byte[] klartext)
    {
        Versiegeln(typ, klartext, out var d);
        await RohAsync((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, d);
    }

    public async ValueTask DisposeAsync()
    {
        await _ende.CancelAsync();
        _udp?.Dispose();
        _halter.Dispose();
        await _strom.DisposeAsync();
        try
        {
            await Task.WhenAll(_lesen, _ping).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException)
        {
        }
        try
        {
            await ServerLauf.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception e) when (e is TimeoutException or IOException or OperationCanceledException)
        {
        }
        _schreiber.Dispose();
        _hoch?.Dispose();
        _runter?.Dispose();
        Schluessel.Dispose();
        _ende.Dispose();
    }
}
