// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Funk.Protokoll.Identitaet;
using Funk.Testclient;

// funk-testclient: simulierte Funker gegen einen Server (Lasttest nach server-sicherheit.md B20), auf Funk.ClientKern,
// dazu echo-probe: eine Sprechphase an den Echo-Funker und die Prüfung des Echos.
//
//   last  Frisches Zustandsverzeichnis unter tr-last\ (außerhalb der Arbeitskopie), Schlüssel und Profil offline per funk-admin, funk-testserver mit
//         Testzugang auf Loopback starten, Szenarien laufen lassen, danach alles beenden und das Verzeichnis löschen.
//   lauf  Gegen einen laufenden Server (Adresse, Port, Serverprofil); die Admin-Schritte über funk-admin am Socket.

// Umlaute auf der Windows-Konsole (Befund 01.10.2026: ohne das verstümmelt); unter Linux ohnehin UTF-8.
Console.OutputEncoding = Encoding.UTF8;

var schalter = new HashSet<string>(StringComparer.Ordinal) { "ohne-eigene-adressen" };
Befehlszeile z;
try
{
    z = new Befehlszeile(args, schalter);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

var json = new JsonSerializerOptions { WriteIndented = true };
try
{
    switch (z.Befehl)
    {
        case "last":
            return await LastAsync(z, json).ConfigureAwait(false);
        case "lauf":
            return await LaufAsync(z, json).ConfigureAwait(false);
        case "echo-probe":
            return await EchoProbeAsync(z).ConfigureAwait(false);
        default:
            Console.WriteLine(Hilfe);
            return z.Befehl is "hilfe" or "--hilfe" or "-h" ? 0 : 2;
    }
}
catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or TimeoutException or FormatException)
{
    Console.Error.WriteLine("Abbruch: " + e.Message);
    return 1;
}

static Laufoptionen Optionen(Befehlszeile z) => new(
    z.Zahl("funker", 40),
    z.Kommazahl("sekunden", 30),
    Math.Clamp(z.Zahl("opus-bytes", 160), Testrahmen.MinLaenge, 400),
    Math.Clamp(z.Zahl("parallel", 10), 1, 18),
    z.Kommazahl("aufwaermen", 3),
    z.Zahl("gruppen", 1));

static async Task<int> LastAsync(Befehlszeile z, JsonSerializerOptions json)
{
    var admin = z.Wert("admin") ?? Werkzeuge.Dll(Path.Combine("src", "Funk.Admin"), "funk-admin");
    var server = z.Wert("server") ?? Werkzeuge.Dll(Path.Combine("tests", "Funk.Testserver"), "funk-testserver");
    var wurzel = z.Wert("wurzel") ?? Werkzeuge.Lastwurzel();
    var o = Optionen(z);
    Console.WriteLine($"Lasttest: {o.Funker} Funker, {o.Sekunden} s je Szenario, Opus {o.OpusBytes} Byte, {o.Gruppen} Sendegruppe(n) je 20 ms, Pacer {z.Kommazahl("ausgang-mbit", 10_000)} Mbit/s, {Environment.ProcessorCount} logische Kerne");
    await using var e = await Einrichtung.AnlegenAsync(wurzel, admin, server, z.Kommazahl("ausgang-mbit", 10_000), Console.Out).ConfigureAwait(false);
    var test = new Lasttest(o, new AdminWerkzeug(admin, e.Socket), e.Server, Aufbauten.Test(new IPEndPoint(IPAddress.Loopback, e.Testzugang)), Console.Out);
    List<Szenarioergebnis> ergebnisse;
    try
    {
        ergebnisse = await test.LaufenAsync(z.Wert("szenarien") ?? "ABCD").ConfigureAwait(false);
    }
    catch
    {
        lock (e.ServerLog) foreach (var zeile in e.ServerLog.TakeLast(40)) Console.Error.WriteLine("  SERVER " + zeile);
        throw;
    }
    lock (e.ServerLog)
        foreach (var zeile in e.ServerLog.Where(l => l.Contains("WARNUNG", StringComparison.Ordinal) || l.Contains("FEHLER", StringComparison.Ordinal)).TakeLast(20))
            Console.WriteLine("  SERVER " + zeile);
    Ausgeben(z, json, ergebnisse);
    return 0;
}

static async Task<int> LaufAsync(Befehlszeile z, JsonSerializerOptions json)
{
    var angebot = SigniertesProfil.TryLesen(File.ReadAllBytes(z.Pflicht("profil")), out var p) ? p : throw new ArgumentException("--profil ist kein {daten, signatur}.");
    Serverprofil profil;
    if (z.Wert("profilschluessel") is { } spki)
    {
        using var schluessel = Profilschluessel.AusSpki(Convert.FromBase64String(spki));
        var pruefung = ProfilAnnahme.Pruefen(angebot, [schluessel], null, DateTimeOffset.UtcNow);
        profil = pruefung.Urteil == ProfilUrteil.Uebernehmen && pruefung.Profil is { } pr ? pr : throw new ArgumentException("Profil: " + pruefung.Urteil);
    }
    else
    {
        profil = Serverprofil.TryLesen(angebot.Daten, out var pr) ? pr : throw new ArgumentException("Profildaten ungültig.");
        Console.WriteLine("HINWEIS: Profilsignatur nicht geprüft (--profilschluessel fehlt).");
    }
    var adresse = z.Wert("adresse");
    var port = z.Wert("port") is { } ps ? ushort.Parse(ps, CultureInfo.InvariantCulture) : profil.TcpPort;
    var zugang = z.Wert("zugang") ?? "tls";
    Func<int, Funk.ClientKern.Geraeteidentitaet, Funk.ClientKern.IVerbindungsaufbau> aufbau;
    if (zugang == "test")
    {
        var ip = IPAddress.Parse(adresse ?? "127.0.0.1");
        aufbau = Aufbauten.Test(new IPEndPoint(ip, port));
    }
    else
    {
        if (OperatingSystem.IsWindows() && !string.Equals(Environment.GetEnvironmentVariable("FUNK_TLS_IM_PROFIL"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("TLS unter Windows legt je Gerät einen Schlüssel im Benutzerprofil ab (Schannel); nur mit FUNK_TLS_IM_PROFIL=1, sonst --zugang test.");
        var loopback = adresse is not null && IPAddress.TryParse(adresse, out var a) && IPAddress.IsLoopback(a);
        aufbau = Aufbauten.Tls(profil, adresse, port, loopback && !z.Schalter("ohne-eigene-adressen"));
    }
    Process? server = z.Wert("server-pid") is { } pid ? Process.GetProcessById(int.Parse(pid, CultureInfo.InvariantCulture)) : null;
    var admin = new AdminWerkzeug(z.Pflicht("admin"), z.Pflicht("socket"));
    var test = new Lasttest(Optionen(z), admin, server, aufbau, Console.Out);
    var ergebnisse = await test.LaufenAsync(z.Wert("szenarien") ?? "ABCD").ConfigureAwait(false);
    Ausgeben(z, json, ergebnisse);
    return 0;
}

static async Task<int> EchoProbeAsync(Befehlszeile z)
{
    var identitaet = Funk.ClientKern.Geraeteidentitaet.Fluechtig();
    try
    {
        Funk.ClientKern.IVerbindungsaufbau aufbau;
        TimeSpan frist;
        if (z.Wert("testzugang") is { } tz)
        {
            if (!IPEndPoint.TryParse(tz, out var ziel) || !Funk.ClientKern.Testzugang.IstLoopback(ziel.Address) || ziel.Port == 0)
                throw new ArgumentException("--testzugang braucht die Form 127.0.0.1:Port (nur Loopback).");
            aufbau = new Funk.ClientKern.TestzugangAufbau(ziel, identitaet, IPAddress.Parse(z.Wert("lokal") ?? "127.0.0.3"));
            frist = TimeSpan.FromSeconds(15);
        }
        else
        {
            if (OperatingSystem.IsWindows() && !string.Equals(Environment.GetEnvironmentVariable("FUNK_TLS_IM_PROFIL"), "1", StringComparison.Ordinal))
                throw new InvalidOperationException("TLS unter Windows legt den Schlüssel im Benutzerprofil ab (Schannel); nur mit FUNK_TLS_IM_PROFIL=1, sonst --testzugang.");
            var angebot = SigniertesProfil.TryLesen(File.ReadAllBytes(z.Pflicht("profil")), out var p) ? p : throw new ArgumentException("--profil ist kein {daten, signatur}.");
            var profil = Serverprofil.TryLesen(angebot.Daten, out var pr) ? pr : throw new ArgumentException("Profildaten ungültig.");
            aufbau = new Funk.ClientKern.TlsAufbau(profil, identitaet, new Funk.ClientKern.TlsAufbauOptionen
            {
                Host = z.Wert("adresse"),
                Port = z.Wert("port") is { } ps ? ushort.Parse(ps, CultureInfo.InvariantCulture) : null,
            });
            // Ein neues Gerät braucht die Freigabe durch den Admin; so lange wartet die Probe.
            frist = TimeSpan.FromMinutes(5);
        }
        return await Echoprobe.LaufenAsync(aufbau, identitaet, z.Wert("netz") ?? "Echo", Math.Clamp(z.Zahl("frames", 50), 2, 1500),
            z.Zahl("preroll", 3), frist, Console.Out).ConfigureAwait(false);
    }
    finally
    {
        identitaet.Dispose();
    }
}

static void Ausgeben(Befehlszeile z, JsonSerializerOptions json, List<Szenarioergebnis> ergebnisse)
{
    var tabelle = Szenarioergebnis.Tabelle(ergebnisse);
    Console.WriteLine();
    Console.WriteLine(tabelle);
    if (z.Wert("aus") is { } a) File.WriteAllText(a, JsonSerializer.Serialize(ergebnisse, json), new UTF8Encoding(false));
    if (z.Wert("tabelle") is { } t) File.WriteAllText(t, tabelle, new UTF8Encoding(false));
}

internal static partial class Program
{
    private const string Hilfe = """
        funk-testclient: simulierte Funker gegen einen Server (Szenarien A-D nach server-sicherheit.md B20, J und K mit Kanälen ab 0.6.0)

          last  [--funker 40] [--sekunden 30] [--szenarien ABCD] [--opus-bytes 160] [--ausgang-mbit 10000]
                [--parallel 10] [--aufwaermen 3] [--gruppen 1] [--wurzel <tr-last>] [--aus <ergebnis.json>] [--tabelle <tabelle.md>]
                [--admin <funk-admin.dll>] [--server <funk-testserver.dll>]
                Frisches Zustandsverzeichnis, funk-testserver mit Testzugang auf Loopback, Szenarien, Aufräumen.

          lauf  --profil <profil.json> [--profilschluessel <SPKI Base64>] [--adresse <host|ip>] [--port <n>]
                [--zugang tls|test] --admin <funk-admin.dll> --socket <verwaltungs-socket> [--server-pid <pid>]
                [--ohne-eigene-adressen] und die Laufwerte wie bei last.
                Gegen einen laufenden Server. TLS unter Windows nur mit FUNK_TLS_IM_PROFIL=1 (Schlüssel im Profil).

          echo-probe (--testzugang 127.0.0.1:<port> [--lokal 127.0.0.3] | --profil <profil.json> [--adresse <host|ip>] [--port <n>])
                [--netz Echo] [--frames 50] [--preroll 3]
                Spricht eine synthetische Phase ins Echo-Netz und prüft das Echo: vollständig, Reihenfolge, BEGINN, ENDE,
                Pause ab 500 ms. Exitcode 0 = bestanden. TLS unter Windows nur mit FUNK_TLS_IM_PROFIL=1.

        Gemessen je Empfänger: Verlust, Laufzeit Mund-Server-Ohr (Zeitstempel im Testrahmen), Jitter (RFC 3550),
        Pakete/s und Bandbreite; am Server CPU und Speicher des Prozesses und die Relais-Messwerte.
        """;
}
