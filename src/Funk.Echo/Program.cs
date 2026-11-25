// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using Funk.ClientKern;
using Funk.ClientKern.Echo;
using Funk.Echo;
using Funk.Protokoll.Identitaet;

// funk-echo: Echo-Funker als eigener Prozess. Hört im Netz Echo mit und sendet jede Sprechphase nach 500 ms Pause zurück,
// damit ein Sprecher sich über den ganzen Weg hört (Server, Jitterpuffer, Mischer).
//
//   Betrieb:  funk-echo --profil /etc/funk/profil.json --identitaet /var/lib/funk-echo/geraet.pem [--adresse <host|ip>]
//             [--port <n>] [--profilschluessel <SPKI Base64>] [--netz Echo]
//   Probe:    funk-echo --testzugang 127.0.0.1:<port> [--netz Echo]
//
// Beim ersten Start mit einem neuen Schlüssel zeigt der Funker einen Registrierungscode; der Admin verknüpft ihn mit
// funk-admin verknuepfen und gibt das Gerät mit funk-admin freigeben frei. Die Rückfrage bestätigt der Funker selbst.

Console.OutputEncoding = System.Text.Encoding.UTF8;
string? profilPfad = null;
string? profilSchluessel = null;
string? adresse = null;
ushort? port = null;
string? identitaetPfad = null;
string? testzugang = null;
var netz = EchoOptionen.StandardNetz;
var name = "Echo-Funker";
var ruhe = EchoOptionen.RuheNachAbweisung;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--profil" when i + 1 < args.Length:
            profilPfad = args[++i];
            break;
        case "--profilschluessel" when i + 1 < args.Length:
            profilSchluessel = args[++i];
            break;
        case "--adresse" when i + 1 < args.Length:
            adresse = args[++i];
            break;
        case "--port" when i + 1 < args.Length:
            if (!ushort.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p == 0)
            {
                Console.Error.WriteLine("--port braucht eine Zahl von 1 bis 65535.");
                return 2;
            }
            port = p;
            break;
        case "--identitaet" when i + 1 < args.Length:
            identitaetPfad = args[++i];
            break;
        case "--testzugang" when i + 1 < args.Length:
            testzugang = args[++i];
            break;
        case "--netz" when i + 1 < args.Length:
            netz = args[++i];
            break;
        case "--name" when i + 1 < args.Length:
            name = args[++i];
            break;
        case "--ruhe" when i + 1 < args.Length:
            if (!uint.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var sek) || sek > 86_400)
            {
                Console.Error.WriteLine("--ruhe braucht Sekunden von 0 bis 86400.");
                return 2;
            }
            ruhe = TimeSpan.FromSeconds(sek);
            break;
        case "--hilfe" or "-h" or "hilfe":
            Console.WriteLine(Hilfe.Text);
            return 0;
        default:
            Console.Error.WriteLine("Unbekanntes Argument: " + args[i] + "\n\n" + Hilfe.Text);
            return 2;
    }
}

void Zeile(string text)
{
    lock (Console.Out) Console.WriteLine(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + " " + text);
}

Geraeteidentitaet identitaet;
IVerbindungsaufbau aufbau;
try
{
    if (testzugang is not null)
    {
        if (!IPEndPoint.TryParse(testzugang, out var ziel) || !Testzugang.IstLoopback(ziel.Address) || ziel.Port == 0)
        {
            Console.Error.WriteLine("--testzugang braucht die Form 127.0.0.1:Port (nur Loopback).");
            return 2;
        }
        // Im Testzugang gibt der Testserver jedes Gerät frei; ein flüchtiger Schlüssel reicht.
        identitaet = Geraeteidentitaet.Fluechtig();
        aufbau = new TestzugangAufbau(ziel, identitaet);
    }
    else
    {
        if (profilPfad is null || identitaetPfad is null)
        {
            Console.Error.WriteLine("--profil und --identitaet sind Pflicht (oder --testzugang).\n\n" + Hilfe.Text);
            return 2;
        }
        if (OperatingSystem.IsWindows() && !string.Equals(Environment.GetEnvironmentVariable("FUNK_TLS_IM_PROFIL"), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("TLS unter Windows legt den Schlüssel im Benutzerprofil ab (Schannel); nur mit FUNK_TLS_IM_PROFIL=1, sonst --testzugang.");
            return 2;
        }
        var profil = ProfilLesen(profilPfad, profilSchluessel, Zeile);
        var (schluessel, neu) = Schluesseldatei.LadenOderAnlegen(identitaetPfad);
        identitaet = Geraeteidentitaet.Aus(schluessel, Funk.Protokoll.Steuerung.SchluesselArt.Software);
        if (neu) Zeile("Neuer Geräteschlüssel in " + identitaetPfad + " (nur für den Besitzer lesbar).");
        aufbau = new TlsAufbau(profil, identitaet, new TlsAufbauOptionen { Host = adresse, Port = port });
    }
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or FormatException
    or System.Security.Cryptography.CryptographicException)
{
    Console.Error.WriteLine("Start gescheitert: " + e.Message);
    return 1;
}

Zeile("Echo-Funker, Gerät " + identitaet.Fingerabdruck.Anzeige + ", Netz '" + netz + "'");
var anzeige = identitaet.Fingerabdruck.Anzeige;
await using var funker = new EchoFunker(aufbau, identitaet, new EchoOptionen { Netz = netz }, Zeile, name);
funker.Client.CodeErhalten += (_, c) =>
{
    // Der Code gehört dem Admin, der ihn einlöst; er steht nur hier, nie in einem Log des Servers.
    var bis = DateTimeOffset.FromUnixTimeMilliseconds(c.GueltigBis).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    Zeile("Registrierungscode " + c.Code + " (gültig bis " + bis + "). Auf dem Server:");
    foreach (var befehl in EchoFunker.Adminbefehle(c.Code, anzeige)) Zeile("  " + befehl);
};
// Verdrängt oder abgewiesen: Der Funker versucht es nicht selbst neu. Nach der Ruhezeit endet funk-echo mit Code 3 bzw. 4, und
// systemd (Restart=on-failure) startet ihn neu; die Ruhezeit verhindert Ping-Pong mit einem Gerät desselben Kontos.
var abschied = new TaskCompletionSource<EchoAbschied>(TaskCreationOptions.RunContinuationsAsynchronously);
funker.Abgewiesen += (_, a) => abschied.TrySetResult(a);
funker.Starten();

using var stop = new CancellationTokenSource();
void Anhalten(PosixSignalContext kontext)
{
    kontext.Cancel = true;
    stop.Cancel();
}
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Anhalten);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Anhalten);
var code = 0;
try
{
    var ende = await Task.WhenAny(Task.Delay(Timeout.Infinite, stop.Token), abschied.Task).ConfigureAwait(false);
    if (ende == abschied.Task)
    {
        var a = await abschied.Task.ConfigureAwait(false);
        Zeile(a.Text);
        Zeile(string.Create(CultureInfo.InvariantCulture, $"Ruhezeit {ruhe.TotalMinutes:0.#} min, dann Ende mit Code {a.Code}."));
        await Task.Delay(ruhe, stop.Token).ConfigureAwait(false);
        code = a.Code;
    }
}
catch (OperationCanceledException)
{
}
Zeile(string.Create(CultureInfo.InvariantCulture, $"Beendet (Code {code}): {funker.Aufnahmen} Aufnahmen, {funker.Wiedergaben} Wiedergaben, {funker.Schleifen} Schleifen verworfen"));
return code;

static Serverprofil ProfilLesen(string pfad, string? spki, Action<string> zeile)
{
    if (!SigniertesProfil.TryLesen(File.ReadAllBytes(pfad), out var angebot)) throw new InvalidDataException("--profil ist kein {daten, signatur}.");
    if (spki is null)
    {
        zeile("HINWEIS: Profilsignatur nicht geprüft (--profilschluessel fehlt); der Pin des Servers wird trotzdem geprüft.");
        return Serverprofil.TryLesen(angebot.Daten, out var ungeprueft) ? ungeprueft : throw new InvalidDataException("Profildaten ungültig.");
    }
    using var schluessel = Profilschluessel.AusSpki(Convert.FromBase64String(spki));
    var pruefung = ProfilAnnahme.Pruefen(angebot, [schluessel], null, DateTimeOffset.UtcNow);
    return pruefung.Urteil == ProfilUrteil.Uebernehmen && pruefung.Profil is { } p ? p : throw new InvalidDataException("Profil: " + pruefung.Urteil);
}
