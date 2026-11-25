// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Funk.ClientKern;
using Funk.ClientKern.Echo;
using Funk.Server;
using Funk.Server.Betrieb;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Testserver;

/// <summary>
/// Der Testserver als Ganzes: der unveränderte <see cref="Dienst"/>, der <see cref="Testzugangsannahme"/> auf Loopback und
/// auf Wunsch der <see cref="EchoFunker"/> im selben Prozess, der sich über den Testzugang anmeldet wie jeder Client.
/// <para>
/// Probebetrieb (<see cref="ProbeStartenAsync"/>): Serverschlüssel, Betriebsgeheimnis und Profilschlüssel entstehen im
/// Speicher, das Zustandsverzeichnis ist frisch und wird beim Beenden gelöscht, Netze, Schiffe und Rollen kommen aus der
/// <see cref="Probekonfiguration"/>. Nichts davon ist für den Betrieb gedacht; dort gibt es weder Testzugang noch
/// automatische Freigabe.
/// </para>
/// </summary>
public sealed class Probeserver : IAsyncDisposable
{
    /// <summary>Eigene Loopback-Adresse des Echo-Funkers: eigenes Präfix, eigene Töpfe (4.8).</summary>
    public static readonly IPAddress EchoAdresse = new([127, 0, 0, 2]);

    private readonly X509Certificate2? _zertifikat;
    private readonly string? _loeschen;
    private readonly Action<string> _log;

    private Probeserver(Dienst dienst, X509Certificate2? zertifikat, string? loeschen, Action<string> log)
    {
        Dienst = dienst;
        _zertifikat = zertifikat;
        _loeschen = loeschen;
        _log = log;
    }

    public Dienst Dienst { get; }

    public Testzugangsannahme Annahme { get; private set; } = null!;

    public IPEndPoint Testzugang => Annahme.Endpunkt;

    public EchoFunker? Echo { get; private set; }

    /// <summary>Wurzel des Laufs (Profil, Zustand, Socket); im Probebetrieb frisch und beim Beenden gelöscht.</summary>
    public string Verzeichnis => Dienst.Konfiguration.Basisverzeichnis;

    /// <summary>
    /// Probebetrieb: frisches Verzeichnis unter <paramref name="wurzel"/>, alles Geheime nur im Speicher, Lage aus
    /// <paramref name="probe"/>, Testzugang auf <paramref name="testzugang"/> (Port 0: frei) mit automatischer Freigabe
    /// laut Konfiguration, Echo-Funker mit <paramref name="echo"/>.
    /// </summary>
    public static async Task<Probeserver> ProbeStartenAsync(Probekonfiguration probe, string wurzel, IPEndPoint testzugang, bool echo,
        TextWriter? konsole = null, Action<string>? abgriff = null, EchoOptionen? echoOptionen = null, string? socket = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(wurzel);
        ArgumentNullException.ThrowIfNull(testzugang);
        if (probe.Pruefen() is { Count: > 0 } fehler) throw new InvalidDataException("Probe-Konfiguration: " + string.Join(" ", fehler));
        if (!Funk.ClientKern.Testzugang.IstLoopback(testzugang.Address)) throw new ArgumentException("Den Testzugang gibt es nur auf Loopback.", nameof(testzugang));

        var verzeichnis = Path.Combine(Path.GetFullPath(wurzel), DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(verzeichnis);
        X509Certificate2? zert = null;
        Dienst? dienst = null;
        try
        {
            using (var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var anfrage = new CertificateRequest("CN=Funk-Testserver", schluessel, HashAlgorithmName.SHA256);
                zert = anfrage.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
            }
            ProfilSchreiben(Path.Combine(verzeichnis, "profil.json"), probe.Name, I.Fingerabdruck.AusZertifikat(zert));
            var socketPfad = socket is not null ? Path.GetFullPath(socket) : SocketPfad(verzeichnis);
            var k = new ServerKonfiguration
            {
                Binden = ["127.0.0.1"],
                TcpPort = 0,
                UdpPort = 0,
                Zustandsverzeichnis = "zustand",
                Profil = "profil.json",
                VerwaltungsSocket = socketPfad,
                Log = new ProtokollKonfiguration { Stufe = "INFO" },
                Test = new TestKonfiguration { ZufallsPorts = true },
                Basisverzeichnis = verzeichnis,
            };
            // Wie Serverschlüssel und Betriebsgeheimnis lebt der TOTP-Schlüssel im Probebetrieb nur im Speicher.
            dienst = Dienst.Starten(k, konsole, ServerIdentitaet.AusZertifikat(zert, schannelTauglich: TlsImProfil), Geheimnis.Zufaellig(), abgriff,
                totpSchluessel: RandomNumberGenerator.GetBytes(TotpSchluessel.Laenge));
            var s = new Probeserver(dienst, zert, verzeichnis, Protokoll(konsole, abgriff));
            dienst = null;
            if (!socketPfad.StartsWith(verzeichnis, StringComparison.OrdinalIgnoreCase)) s._log("Verwaltungssocket: " + socketPfad);
            try
            {
                await s.LageAnlegenAsync(probe).ConfigureAwait(false);
                var stufe = probe.Freigabe.StufeLesen()!.Value;
                s.Annahme = new Testzugangsannahme(s.Dienst, testzugang,
                    probe.Freigabe.Automatisch ? g => s.FreigebenAsync(g, stufe, probe.Freigabe.Name) : null, s._log);
                if (echo) await s.EchoStartenAsync(echoOptionen ?? probe.Echo.Optionen(), netzPflicht: true).ConfigureAwait(false);
                return s;
            }
            catch
            {
                await s.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            if (dienst is not null) await dienst.DisposeAsync().ConfigureAwait(false);
            // Das Verzeichnis ist frisch und gehört nur diesem Lauf.
            Loeschen(verzeichnis);
            zert?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Mit einer Serverkonfiguration (Lasttest, eigene Zustände): Identität aus den Dateien der Konfiguration, Zustand bleibt.
    /// Automatische Freigabe nur mit <paramref name="freigabeStufe"/>; der Echo-Funker bekommt sein Konto immer.
    /// </summary>
    public static async Task<Probeserver> KonfigStartenAsync(ServerKonfiguration konfiguration, IPEndPoint testzugang, bool echo,
        EchoOptionen? echoOptionen = null, I.Stufe? freigabeStufe = null, TextWriter? konsole = null)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);
        ArgumentNullException.ThrowIfNull(testzugang);
        if (!Funk.ClientKern.Testzugang.IstLoopback(testzugang.Address)) throw new ArgumentException("Den Testzugang gibt es nur auf Loopback.", nameof(testzugang));
        if (!konfiguration.Bindadressen().All(Funk.ClientKern.Testzugang.IstLoopback)) throw new ArgumentException("Der Testserver bindet nur Loopback-Adressen.", nameof(konfiguration));
        ServerIdentitaet identitaet;
        using (var zert = X509Certificate2.CreateFromPemFile(konfiguration.Aufloesen(konfiguration.Identitaet.Zertifikat), konfiguration.Aufloesen(konfiguration.Identitaet.Schluessel)))
            identitaet = ServerIdentitaet.AusZertifikat(zert, schannelTauglich: TlsImProfil);
        Dienst dienst;
        try
        {
            dienst = Dienst.Starten(konfiguration, konsole, identitaet);
        }
        catch
        {
            identitaet.Dispose();
            throw;
        }
        var s = new Probeserver(dienst, null, null, Protokoll(konsole, null));
        try
        {
            s.Annahme = new Testzugangsannahme(s.Dienst, testzugang,
                freigabeStufe is { } stufe ? g => s.FreigebenAsync(g, stufe, "Probefunker") : null, s._log);
            if (echo) await s.EchoStartenAsync(echoOptionen ?? new EchoOptionen(), netzPflicht: false).ConfigureAwait(false);
            return s;
        }
        catch
        {
            await s.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Pfad des Verwaltungssockets im Probebetrieb. UNIX-Sockets erlauben höchstens 108 Byte im Pfad, auch unter
    /// Windows, und .NET zählt sie in UTF-8 samt Nullbyte: Ein Umlaut kostet zwei. Liegt das Paket tief (etwa in einem
    /// synchronisierten Dokumente-Ordner), weicht er mit zufälligem Namen nach <see cref="Ausweichordner"/> aus. Der
    /// Dienst löscht ihn beim Beenden.
    /// </summary>
    public static string SocketPfad(string verzeichnis) => SocketPfad(verzeichnis, Ausweichordner);

    /// <summary>Wie <see cref="SocketPfad(string)"/>, mit vorgegebenem Ausweichordner (Tests).</summary>
    public static string SocketPfad(string verzeichnis, string ausweichordner)
    {
        ArgumentNullException.ThrowIfNull(verzeichnis);
        ArgumentNullException.ThrowIfNull(ausweichordner);
        var imVerzeichnis = Path.Combine(verzeichnis, "v.sock");
        return Encoding.UTF8.GetByteCount(imVerzeichnis) <= MaxSocketPfad
            ? imVerzeichnis
            : Path.Combine(ausweichordner, "tr-probe-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)) + ".sock");
    }

    /// <summary>Höchstlänge eines Socketpfads in UTF-8-Bytes, mit Abstand zur Grenze von 108 Byte.</summary>
    public const int MaxSocketPfad = 100;

    /// <summary>Kurzer Ordner der Arbeitszone; Probeläufe durch Claude legen dort ab, nie im Profil.</summary>
    public const string Zonenordner = @"C:\Claude\tmp";

    /// <summary>
    /// Wohin ein zu langer Socketpfad ausweicht: <see cref="Zonenordner"/>, wenn es ihn gibt, sonst (auf einem PC ohne
    /// C:\Claude, unter Linux) das Temp-Verzeichnis.
    /// </summary>
    public static string Ausweichordner => OperatingSystem.IsWindows() && Directory.Exists(Zonenordner) ? Zonenordner : Path.GetTempPath();

    /// <summary>Unter Windows TLS nur mit ausdrücklicher Freigabe: Schannel legt sonst Schlüssel im Benutzerprofil ab.</summary>
    public static bool TlsImProfil => !OperatingSystem.IsWindows()
        || string.Equals(Environment.GetEnvironmentVariable("FUNK_TLS_IM_PROFIL"), "1", StringComparison.Ordinal);

    private Task<string?> FreigebenAsync(I.Fingerabdruck geraet, I.Stufe stufe, string name) =>
        Probefreigabe.FreigebenAsync(Dienst.Zentrale, geraet, "Probe-Gerät", Probefreigabe.DiscordIdAus(geraet),
            Probefreigabe.BenutzernameAus(geraet), name, stufe);

    /// <summary>
    /// Netze ohne Schiffsbindung, Rollen, Schiffe, dann die Schiffsnetze mit Bindung (ein Schiffsnetz braucht das Schiff, das
    /// Schiff seine Netze), zuletzt die Einstellungen; alles über die Befehle des Admin-Werkzeugs.
    /// </summary>
    private async Task LageAnlegenAsync(Probekonfiguration probe)
    {
        foreach (var n in probe.Netze)
        {
            var ohne = (JsonObject)n.DeepClone();
            ohne.Remove("schiffId");
            ohne.Remove("schiffsnetz");
            await BefehlAsync("netz-setzen", ohne).ConfigureAwait(false);
        }
        foreach (var r in probe.Rollen) await BefehlAsync("rolle-setzen", r).ConfigureAwait(false);
        foreach (var sch in probe.Schiffe) await BefehlAsync("schiff-setzen", sch).ConfigureAwait(false);
        foreach (var n in probe.Netze.Where(n => n.ContainsKey("schiffId") || n.ContainsKey("schiffsnetz")))
            await BefehlAsync("netz-setzen", n).ConfigureAwait(false);
        if (probe.Einstellungen is { } e)
        {
            var a = new JsonObject();
            if (e.Selbstwahl is { } sw) a["selbstwahl"] = sw;
            if (e.DurchsageNurTaste is { } dt) a["durchsageNurTaste"] = dt;
            var antwort = await Dienst.Zentrale.VerwaltungAsync("einstellung", a).ConfigureAwait(false);
            if (!antwort.Ok) throw new InvalidDataException("einstellung: " + antwort.Text);
        }
        _log(string.Create(CultureInfo.InvariantCulture, $"Probe-Lage angelegt: {probe.Netze.Count} Netze, {probe.Schiffe.Count} Schiffe, {probe.Rollen.Count} Rollen"));
    }

    private async Task BefehlAsync(string befehl, JsonObject objekt)
    {
        var antwort = await Dienst.Zentrale.VerwaltungAsync(befehl, new JsonObject { ["objekt"] = objekt.DeepClone() }).ConfigureAwait(false);
        if (!antwort.Ok) throw new InvalidDataException(befehl + " " + objekt.ToJsonString() + ": " + antwort.Text);
    }

    /// <summary>Echo-Konto freigeben (FUNKER, alte Geräte weg), dann den Funker über den Testzugang anmelden und auf AKTIV warten.</summary>
    private async Task EchoStartenAsync(EchoOptionen optionen, bool netzPflicht)
    {
        var identitaet = Geraeteidentitaet.Fluechtig();
        var grund = await Probefreigabe.FreigebenAsync(Dienst.Zentrale, identitaet.Fingerabdruck, "Echo-Funker", Probefreigabe.EchoDiscordId,
            "echo-funker", "Echo", I.Stufe.Funker, alteGeraeteEntfernen: true).ConfigureAwait(false);
        if (grund is not null)
        {
            identitaet.Dispose();
            throw new InvalidOperationException("Echo-Funker nicht freigegeben: " + grund);
        }
        var ziel = new IPEndPoint(Testzugang.Address.Equals(IPAddress.Any) ? IPAddress.Loopback : Testzugang.Address, Testzugang.Port);
        var lokal = ziel.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? EchoAdresse : null;
        Echo = new EchoFunker(new TestzugangAufbau(ziel, identitaet, lokal), identitaet, optionen, t => _log("Echo: " + t));
        Echo.Starten();
        if (!await Echo.Client.AktivAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false))
            throw new InvalidOperationException("Echo-Funker nicht aktiv, Zustand " + Echo.Client.Zustand);
        for (var i = 0; i < 500 && Echo.NetzId == 0; i++) await Task.Delay(10).ConfigureAwait(false);
        if (Echo.NetzId != 0) return;
        if (netzPflicht) throw new InvalidOperationException("Echo-Funker findet den Kanal '" + optionen.Netz + "' nicht.");
        _log("Echo-Funker wartet: Kanal '" + optionen.Netz + "' fehlt (funk-admin netz-setzen ... --offen HOEREN_SENDEN --standard).");
    }

    /// <summary>Signiertes Profil mit dem Pin des Servers und einem Reserve-Pin; der Profilschlüssel lebt nur im Speicher.</summary>
    private static void ProfilSchreiben(string pfad, string name, I.Fingerabdruck pin)
    {
        using var profilSchluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var reserve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jetzt = DateTimeOffset.UtcNow;
        var profil = new I.Serverprofil(name, 1, jetzt.ToUnixTimeMilliseconds(), jetzt.AddDays(30).ToUnixTimeMilliseconds(),
            [new I.ProfilHost("localhost", true, false)], 443, [pin, I.Fingerabdruck.AusSchluessel(reserve)], []);
        File.WriteAllBytes(pfad, I.SigniertesProfil.Signieren(profil.AlsJson(), profilSchluessel).AlsJson());
    }

    private static Action<string> Protokoll(TextWriter? konsole, Action<string>? abgriff) => text =>
    {
        abgriff?.Invoke(text);
        if (konsole is null) return;
        lock (konsole) konsole.WriteLine(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + " TESTSERVER " + text);
    };

    /// <summary>Echo-Funker, Testzugang, Dienst (mit Bye{WARTUNG}), dann im Probebetrieb das Verzeichnis.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Echo is not null) await Echo.DisposeAsync().ConfigureAwait(false);
        if (Annahme is not null) await Annahme.DisposeAsync().ConfigureAwait(false);
        await Dienst.BeendenAsync(1000).ConfigureAwait(false);
        _zertifikat?.Dispose();
        if (_loeschen is not null) Loeschen(_loeschen);
    }

    private static void Loeschen(string verzeichnis)
    {
        for (var versuch = 0; versuch < 10 && Directory.Exists(verzeichnis); versuch++)
        {
            try
            {
                Directory.Delete(verzeichnis, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
