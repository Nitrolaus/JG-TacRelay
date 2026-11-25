// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using Funk.Server;
using Funk.Server.Betrieb;

// funk-server [--konfig <pfad>] [pruefen]
// Ohne --konfig: $FUNK_KONFIG, sonst server.json neben dem Programm.
var pfad = Environment.GetEnvironmentVariable("FUNK_KONFIG") ?? Path.Combine(AppContext.BaseDirectory, "server.json");
var nurPruefen = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--konfig" when i + 1 < args.Length:
            pfad = args[++i];
            break;
        case "pruefen":
            nurPruefen = true;
            break;
        case "--hilfe" or "-h":
            Console.WriteLine("funk-server [--konfig <pfad>] [pruefen]\n  pruefen: nur Konfiguration und Serverprofil lesen und prüfen, dann beenden.");
            return 0;
        default:
            Console.Error.WriteLine("Unbekanntes Argument: " + args[i]);
            return 2;
    }
}

ServerKonfiguration konfiguration;
try
{
    konfiguration = ServerKonfiguration.Laden(pfad);
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("Konfiguration nicht lesbar: " + e.Message);
    return 2;
}
var fehler = konfiguration.Pruefen();
if (fehler.Count > 0)
{
    Console.Error.WriteLine("Konfiguration ungültig:");
    foreach (var f in fehler) Console.Error.WriteLine("  " + f);
    return 2;
}
if (nurPruefen)
{
    // Das Profil wie beim Start lesen: Ohne lesbares Profil startet der Server nicht. Passt es nicht zu server.json (etwa der
    // TCP-Port), steht die Warnung hier wie beim Start im Journal.
    var (profilFehler, warnungen) = Profilabgleich.Pruefen(konfiguration);
    if (profilFehler is not null)
    {
        Console.Error.WriteLine("Konfiguration ungültig:");
        Console.Error.WriteLine("  Serverprofil: " + profilFehler);
        return 2;
    }
    // Die Uhr: Hier, in der Shell, lässt sich der Kernel lesen (im Dienst sperrt ProtectClock das).
    var kern = Uhrpruefung.KernLesen();
    Console.WriteLine(Uhrpruefung.Hinweis(kern));
    var alle = warnungen.Concat(Uhrpruefung.Warnungen(DateTimeOffset.UtcNow, kern, null)).ToList();
    foreach (var w in alle) Console.WriteLine("WARNUNG: " + w);
    Console.WriteLine(alle.Count == 0 ? "Konfiguration in Ordnung." : "Konfiguration lesbar, aber bitte die Warnung oben ansehen.");
    return 0;
}

Dienst dienst;
try
{
    dienst = Dienst.Starten(konfiguration, Console.Out);
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException
    or System.Net.Sockets.SocketException)
{
    Console.Error.WriteLine("Start gescheitert: " + e.GetType().Name + ": " + e.Message);
    return 1;
}

using var stop = new CancellationTokenSource();
void Anhalten(PosixSignalContext kontext)
{
    kontext.Cancel = true;
    stop.Cancel();
}
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Anhalten);
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Anhalten);
try
{
    await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
}
dienst.Log.Info("Geordnetes Beenden: Bye{WARTUNG} an alle Verbindungen");
await dienst.BeendenAsync().ConfigureAwait(false);
return 0;
