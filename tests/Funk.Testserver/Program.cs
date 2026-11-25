// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using Funk.ClientKern.Echo;
using Funk.Server.Betrieb;
using Funk.Testserver;

// funk-testserver --konfig <server.json> --testzugang <port> [--testzugang-adresse 127.0.0.1] [--echo [--echo-netz Echo]]
//                 [--freigabe-automatisch STUFE]
// funk-testserver --probe <probefunk.json> [--zustand <verzeichnis>] --testzugang <port|0> [--echo] [--port-datei <pfad>]
//                 [--client <programm> [--client-argumente "<argumente mit {testzugang}>"] [--client-freigegeben]]
//
// Der unveränderte Dienst (Funk.Server.Dienst) mit einem zusätzlichen Testzugang auf Loopback: statt TLS beweist der
// Client den Besitz seines Geräteschlüssels mit einer Signatur über einen Zufallswert (Funk.ClientKern.Testzugang),
// danach geht die Verbindung denselben Weg wie nach einem TLS-Handshake. Grund: Unter Windows braucht Schannel für jeden
// Handshake einen Schlüssel im Benutzerprofil; der Lasttest und die Probe sollen dort nichts ablegen. Nur Testwerkzeug,
// nie im Betrieb.
//
// --probe: Probefunk auf dem eigenen PC. Schlüssel, Geheimnis und Profil entstehen im Speicher bzw. im frischen
// Zustandsverzeichnis, das beim Beenden gelöscht wird; Geräte über den Testzugang werden ohne Handarbeit freigegeben.
// --echo: Echo-Funker im selben Prozess (Funk.ClientKern.Echo). --client: startet ein Begleitprogramm (den Client mit
// --testzugang 127.0.0.1:<port> oder funk-testclient echo-probe) und endet mit ihm, mit dessen Exitcode. Der Client
// JG TacRelay zeigt Fenster und öffnet Geräte; ihn startet der Testserver nur mit --client-freigegeben
// (Probefunk starten.cmd). Beenden: Zeile "ende" auf stdin, stdin schließen oder Strg+C; dann Bye{WARTUNG} an alle (7.2).

Console.OutputEncoding = System.Text.Encoding.UTF8;
string? konfigPfad = null;
string? probePfad = null;
string? zustand = null;
string? portDatei = null;
string? socketPfad = null;
string? clientPfad = null;
string? clientArgumente = null;
string? echoNetz = null;
string? freigabeStufe = null;
var echo = false;
var clientFreigegeben = false;
var zugangPort = -1;
var zugangAdresse = IPAddress.Loopback;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--konfig" when i + 1 < args.Length:
            konfigPfad = args[++i];
            break;
        case "--probe" when i + 1 < args.Length:
            probePfad = args[++i];
            break;
        case "--zustand" when i + 1 < args.Length:
            zustand = args[++i];
            break;
        case "--testzugang" when i + 1 < args.Length:
            zugangPort = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--testzugang-adresse" when i + 1 < args.Length:
            zugangAdresse = IPAddress.Parse(args[++i]);
            break;
        case "--echo":
            echo = true;
            break;
        case "--echo-netz" when i + 1 < args.Length:
            echoNetz = args[++i];
            break;
        case "--freigabe-automatisch" when i + 1 < args.Length:
            freigabeStufe = args[++i];
            break;
        case "--socket" when i + 1 < args.Length:
            socketPfad = args[++i];
            break;
        case "--port-datei" when i + 1 < args.Length:
            portDatei = args[++i];
            break;
        case "--client" when i + 1 < args.Length:
            clientPfad = args[++i];
            break;
        case "--client-argumente" when i + 1 < args.Length:
            clientArgumente = args[++i];
            break;
        case "--client-freigegeben":
            clientFreigegeben = true;
            break;
        default:
            Console.Error.WriteLine(Hilfe.Text);
            return 2;
    }
}
if ((konfigPfad is null) == (probePfad is null) || zugangPort is < 0 or > 65535)
{
    Console.Error.WriteLine("Genau eines von --konfig und --probe, dazu --testzugang, sind Pflicht.\n\n" + Hilfe.Text);
    return 2;
}
if (!Funk.ClientKern.Testzugang.IstLoopback(zugangAdresse))
{
    Console.Error.WriteLine("Den Testzugang gibt es nur auf Loopback.");
    return 2;
}
// Vor dem Start des Servers: JG TacRelay als Begleitprogramm nur ausdrücklich.
if (clientPfad is not null && Begleitprogramm.Sperre(clientPfad, clientFreigegeben) is { } sperre)
{
    Console.Error.WriteLine(sperre);
    return 2;
}

Probeserver server;
try
{
    var ort = new IPEndPoint(zugangAdresse, zugangPort);
    if (probePfad is not null)
    {
        var probe = Probekonfiguration.Laden(probePfad);
        if (probe.Pruefen() is { Count: > 0 } fehler)
        {
            foreach (var f in fehler) Console.Error.WriteLine("Probe-Konfiguration: " + f);
            return 2;
        }
        var wurzel = zustand ?? Path.Combine(AppContext.BaseDirectory, "probe-zustand");
        server = await Probeserver.ProbeStartenAsync(probe, wurzel, ort, echo, Console.Out, socket: socketPfad).ConfigureAwait(false);
    }
    else
    {
        var konfiguration = ServerKonfiguration.Laden(konfigPfad!);
        if (konfiguration.Pruefen() is { Count: > 0 } fehler)
        {
            foreach (var f in fehler) Console.Error.WriteLine("Konfiguration: " + f);
            return 2;
        }
        Funk.Protokoll.Identitaet.Stufe? stufe = null;
        if (freigabeStufe is not null)
        {
            stufe = new ProbeFreigabe { Stufe = freigabeStufe }.StufeLesen();
            if (stufe is null)
            {
                Console.Error.WriteLine("--freigabe-automatisch: ADMIN, RAIDLEITER oder FUNKER.");
                return 2;
            }
        }
        var echoOptionen = new EchoOptionen { Netz = echoNetz ?? EchoOptionen.StandardNetz };
        server = await Probeserver.KonfigStartenAsync(konfiguration, ort, echo, echoOptionen, stufe, Console.Out).ConfigureAwait(false);
    }
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
    or InvalidOperationException or System.Security.Cryptography.CryptographicException or System.Net.Sockets.SocketException)
{
    Console.Error.WriteLine("Start gescheitert: " + e.GetType().Name + ": " + e.Message);
    return 1;
}

var dienst = server.Dienst;
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"TESTZUGANG {server.Testzugang.Port} TCP {dienst.TcpEndpunkte[0].Port} UDP {dienst.UdpEndpunkte[0].Port} PID {Environment.ProcessId} TLS {(Probeserver.TlsImProfil ? "an" : "aus")}"));
if (server.Echo is { } e1) Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ECHO Netz {e1.NetzId} Konto {e1.Client.Lage.KontoId}"));
if (portDatei is not null) File.WriteAllText(portDatei, server.Testzugang.Port.ToString(CultureInfo.InvariantCulture));
Console.WriteLine("bereit");
Console.Out.Flush();

using var stop = new CancellationTokenSource();
void Anhalten(PosixSignalContext kontext)
{
    kontext.Cancel = true;
    stop.Cancel();
}
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Anhalten);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Anhalten);
var eingabe = Task.Run(async () =>
{
    while (await Console.In.ReadLineAsync(stop.Token).ConfigureAwait(false) is { } zeile)
    {
        if (zeile.Trim() != "ende") continue;
        await stop.CancelAsync().ConfigureAwait(false);
        return;
    }
    // Geschlossenes stdin heißt Ende, außer ein Begleitprogramm läuft: dann endet der Server mit ihm.
    if (clientPfad is null) await stop.CancelAsync().ConfigureAwait(false);
});

Process? client = null;
var clientCode = 0;
if (clientPfad is not null)
{
    // Das Begleitprogramm (Client oder Prüfung) zeigt sich selbst; der Testserver endet mit ihm und übernimmt seinen Exitcode.
    var si = new ProcessStartInfo(clientPfad) { UseShellExecute = false };
    var testzugangText = "127.0.0.1:" + server.Testzugang.Port.ToString(CultureInfo.InvariantCulture);
    foreach (var a in Begleitprogramm.Argumente(clientArgumente ?? "--testzugang {testzugang}")) si.ArgumentList.Add(a.Replace("{testzugang}", testzugangText, StringComparison.Ordinal));
    try
    {
        client = new Process { StartInfo = si, EnableRaisingEvents = true };
        client.Exited += (_, _) =>
        {
            try
            {
                stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        };
        client.Start();
        Console.WriteLine("Gestartet: " + Path.GetFileName(clientPfad) + " (PID " + client.Id.ToString(CultureInfo.InvariantCulture) + "); der Testserver endet mit ihm.");
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
        Console.Error.WriteLine("Nicht startbar: " + clientPfad + ": " + ex.Message);
        clientCode = 1;
        await stop.CancelAsync().ConfigureAwait(false);
    }
}

try
{
    await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
}
if (client is { HasExited: true }) clientCode = client.ExitCode;
client?.Dispose();
await server.DisposeAsync().ConfigureAwait(false);
Console.WriteLine("beendet");
return clientCode;