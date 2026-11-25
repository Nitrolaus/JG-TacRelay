// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Funk.Admin;
using Funk.Protokoll.Identitaet;
using Funk.Server.Betrieb;

namespace Funk.Tests;

/// <summary>
/// Einrichten mit funk-admin (A55, EINRICHTEN-UMZUG-ENTWURF): Die Befehle zum Erzeugen ersetzen eine vorhandene Datei nur mit
/// <c>--ersetzen</c> (bis 01.10.2026 tauschten sie still Serverschlüssel, Geheimnis oder TOTP-Schlüssel aus), <c>konfig-schreiben</c>
/// schreibt eine gültige server.json und nie über eine vorhandene, <c>pruefen --vollstaendig</c> sieht Schlüsseldateien, Pin,
/// Port und freie Ports, <c>profil-anfrage</c> schreibt die Zeile für Sarevoc. Alles in einem Testordner unter C:\Claude\tmp.
/// </summary>
internal static class AdminEinrichtungTests
{
    /// <summary>funk-admin im selben Prozess, mit <paramref name="eingabe"/> als Tastatur.</summary>
    internal static async Task<(int Code, string Ausgabe)> MitEingabe(string eingabe, params string[] argumente)
    {
        var altIn = Console.In;
        Console.SetIn(new StringReader(eingabe));
        try
        {
            return await ServerPfadTests.Werkzeug(argumente);
        }
        finally
        {
            Console.SetIn(altIn);
        }
    }

    internal static Task<(int Code, string Ausgabe)> Werkzeug(params string[] argumente) => MitEingabe("", argumente);

    internal static string Zeile(string text, string praefix) =>
        text.Split('\n', StringSplitOptions.TrimEntries).First(z => z.StartsWith(praefix, StringComparison.Ordinal))[praefix.Length..];

    internal static string Pin(string ausgabe) => Zeile(ausgabe, "Pin (für pins im Serverprofil): ");

    /// <summary>Ein Ordner unter Testserver.Wurzel(), danach gelöscht.</summary>
    internal sealed class Ordner : IDisposable
    {
        public Ordner()
        {
            Pfad = Path.Combine(Testserver.Wurzel(), "einrichten-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(Pfad);
        }

        public string Pfad { get; }

        public string this[string name] => Path.Combine(Pfad, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Pfad, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static int FreierPort(SocketType art)
    {
        using var s = new Socket(AddressFamily.InterNetwork, art, art == SocketType.Stream ? ProtocolType.Tcp : ProtocolType.Udp);
        s.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)s.LocalEndPoint!).Port;
    }

    [Test]
    public static async Task Erzeugen_VerweigertVorhandeneDateienOhneErsetzen()
    {
        using var o = new Ordner();
        var (code, text) = await Werkzeug("schluessel-erzeugen", "--zertifikat", o["server.crt"], "--schluessel", o["server.key"]);
        Pruefe.Gleich(0, code, "erster Aufruf: " + text);
        var pin = Pin(text);
        var schluessel = File.ReadAllBytes(o["server.key"]);
        var zertifikat = File.ReadAllBytes(o["server.crt"]);

        (code, text) = await Werkzeug("schluessel-erzeugen", "--zertifikat", o["server.crt"], "--schluessel", o["server.key"]);
        Pruefe.Gleich(3, code, "zweiter Aufruf ohne --ersetzen: " + text);
        Pruefe.Wahr(text.Contains("gibt es schon", StringComparison.Ordinal) && text.Contains("Nichts geändert", StringComparison.Ordinal)
            && text.Contains("--ersetzen", StringComparison.Ordinal) && text.Contains("Pin", StringComparison.Ordinal), "sagt, was ist und was --ersetzen bedeutet: " + text);
        Pruefe.BytesGleich(schluessel, File.ReadAllBytes(o["server.key"]), "Schlüssel unverändert");
        Pruefe.BytesGleich(zertifikat, File.ReadAllBytes(o["server.crt"]), "Zertifikat unverändert");

        // Nur eine der beiden Dateien da: auch dann nichts.
        File.Delete(o["server.key"]);
        (code, _) = await Werkzeug("schluessel-erzeugen", "--zertifikat", o["server.crt"], "--schluessel", o["server.key"]);
        Pruefe.Gleich(3, code, "Zertifikat da, Schlüssel fehlt");
        Pruefe.Falsch(File.Exists(o["server.key"]), "kein halbes Paar geschrieben");

        (code, text) = await Werkzeug("schluessel-erzeugen", "--zertifikat", o["server.crt"], "--schluessel", o["server.key"], "--ersetzen");
        Pruefe.Gleich(0, code, "mit --ersetzen: " + text);
        Pruefe.Wahr(!string.Equals(pin, Pin(text), StringComparison.Ordinal), "neuer Schlüssel, neuer Pin");

        foreach (var (befehl, option, datei, folge) in new[]
        {
            ("geheimnis-erzeugen", "--datei", "funk-geheimnis", "Vorrangspur"),
            ("totp-schluessel-erzeugen", "--datei", "funk-totp-schluessel", "Zweitfaktor"),
            ("profilschluessel-erzeugen", "--schluessel", "profil.key", "Setup"),
        })
        {
            (code, text) = await Werkzeug(befehl, option, o[datei]);
            Pruefe.Gleich(0, code, befehl + " erstmals: " + text);
            var vorher = File.ReadAllBytes(o[datei]);
            (code, text) = await Werkzeug(befehl, option, o[datei]);
            Pruefe.Gleich(3, code, befehl + " noch einmal: " + text);
            Pruefe.Wahr(text.Contains(folge, StringComparison.Ordinal) && text.Contains("Nichts geändert", StringComparison.Ordinal), befehl + ": Folge genannt: " + text);
            Pruefe.BytesGleich(vorher, File.ReadAllBytes(o[datei]), befehl + ": unverändert");
            Pruefe.Falsch(File.Exists(o[datei] + Dateiablage.Zwischenendung), befehl + ": keine Zwischendatei");
            (code, text) = await Werkzeug(befehl, option, o[datei], "--ersetzen");
            Pruefe.Gleich(0, code, befehl + " --ersetzen: " + text);
            Pruefe.Falsch(vorher.AsSpan().SequenceEqual(File.ReadAllBytes(o[datei])), befehl + ": ersetzt");
        }
    }

    [Test]
    public static async Task KonfigSchreiben_GueltigUndNieUeberEineVorhandene()
    {
        using var o = new Ordner();
        var ziel = o["server.json"];
        var (code, text) = await Werkzeug("konfig-schreiben", "--aus", ziel, "--binden", "192.168.1.250", "--tcp-port", "8443", "--udp-port", "47443",
            "--oeffentlicher-tcp-port", "443", "--profil", "/etc/funk/profil.json", "--zustand", "/var/lib/funk");
        Pruefe.Gleich(0, code, "konfig-schreiben: " + text);
        var k = ServerKonfiguration.Laden(ziel);
        Pruefe.Gleich("", string.Join(" | ", k.Pruefen()), "die geschriebene server.json besteht die Prüfung des Servers");
        Pruefe.Gleich("192.168.1.250", string.Join(",", k.Binden), "binden");
        Pruefe.Gleich(8443, k.TcpPort, "tcpPort");
        Pruefe.Gleich(443, k.TcpPortOeffentlich, "oeffentlicherTcpPort");
        Pruefe.Gleich(47443, k.UdpPortOeffentlich, "udp ohne eigenen öffentlichen Port");
        Pruefe.Gleich("${CREDENTIALS_DIRECTORY}/server.key", k.Identitaet.Schluessel, "Schlüssel aus LoadCredential, nie in der Datei");
        Pruefe.Gleich("${CREDENTIALS_DIRECTORY}/funk-totp-schluessel", k.TotpSchluessel, "TOTP-Schlüssel aus LoadCredential");
        Pruefe.Gleich("${CREDENTIALS_DIRECTORY}/funk-geheimnis", k.Geheimnis, "Geheimnis aus LoadCredential");
        Pruefe.Falsch(k.TotpSchluesselFehltInDatei, "totpSchluessel steht drin (0.2.0)");
        Pruefe.Gleich("/etc/funk/profil.json", k.Profil, "Profil");
        var inhalt = File.ReadAllBytes(ziel);

        (code, text) = await Werkzeug("konfig-schreiben", "--aus", ziel, "--binden", "10.0.0.1");
        Pruefe.Gleich(3, code, "nie über eine vorhandene: " + text);
        Pruefe.Wahr(text.Contains("gibt es schon", StringComparison.Ordinal) && text.Contains("pruefen", StringComparison.Ordinal), text);
        Pruefe.BytesGleich(inhalt, File.ReadAllBytes(ziel), "unverändert");

        // Zwei Adressen, Standardports, Zustand und Profil voreingestellt.
        (code, text) = await Werkzeug("konfig-schreiben", "--aus", o["zwei.json"], "--binden", "192.168.1.250", "--binden", "2001:db8::5");
        Pruefe.Gleich(0, code, "zwei Adressen: " + text);
        var zwei = ServerKonfiguration.Laden(o["zwei.json"]);
        Pruefe.Wahr(zwei.Binden.Count == 2 && zwei.TcpPort == 8443 && zwei.UdpPort == 47443 && zwei.Zustandsverzeichnis == "/var/lib/funk"
            && zwei.Profil == "/etc/funk/profil.json" && zwei.VerwaltungsSocket == "/run/funk/verwaltung.sock", "Voreinstellungen wie in BETRIEB.md");

        foreach (var (was, args, erwartet) in new[]
        {
            ("ohne --binden", new[] { "--aus", o["a.json"] }, "--binden"),
            ("alle Adressen", ["--aus", o["b.json"], "--binden", "0.0.0.0"], "alle Adressen"),
            ("keine IP", ["--aus", o["c.json"], "--binden", "funk.example.org"], "keine IP-Adresse"),
            ("doppelt", ["--aus", o["d.json"], "--binden", "10.0.0.1", "--binden", "10.0.0.1"], "doppelt"),
            ("Port", ["--aus", o["e.json"], "--binden", "10.0.0.1", "--tcp-port", "70000"], "1 und 65535"),
            ("Port 0", ["--aus", o["f.json"], "--binden", "10.0.0.1", "--udp-port", "0"], "1 und 65535"),
        })
        {
            (code, text) = await Werkzeug(["konfig-schreiben", .. args]);
            Pruefe.Gleich(2, code, was + ": " + text);
            Pruefe.Wahr(text.Contains(erwartet, StringComparison.Ordinal) && text.Contains("Nichts geschrieben", StringComparison.Ordinal), was + ": " + text);
            Pruefe.Falsch(File.Exists(args[1]), was + ": keine Datei");
        }
    }

    /// <summary>Was ein frischer Server braucht, im Testordner: Schlüssel, Geheimnis, TOTP, Profil mit Server- und Reserve-Pin, server.json.</summary>
    internal sealed record Einrichtung(Ordner O, string Konfig, string Credentials, string Spki, string Pin, string ReservePin, int TcpPort, int UdpPort);

    internal static async Task<Einrichtung> EinrichtenAsync(Ordner o, string host = "localhost")
    {
        var c = o["credentials"];
        Directory.CreateDirectory(c);
        var (code, text) = await Werkzeug("schluessel-erzeugen", "--zertifikat", Path.Combine(c, "server.crt"), "--schluessel", Path.Combine(c, "server.key"));
        Pruefe.Gleich(0, code, text);
        var pin = Pin(text);
        Pruefe.Gleich(0, (await Werkzeug("geheimnis-erzeugen", "--datei", Path.Combine(c, "funk-geheimnis"))).Code, "Geheimnis");
        Pruefe.Gleich(0, (await Werkzeug("totp-schluessel-erzeugen", "--datei", Path.Combine(c, "funk-totp-schluessel"))).Code, "TOTP");
        (code, text) = await Werkzeug("profilschluessel-erzeugen", "--schluessel", o["profil.key"]);
        Pruefe.Gleich(0, code, text);
        var spki = text.Split('\n', StringSplitOptions.TrimEntries)[1];
        (code, text) = await Werkzeug("schluessel-erzeugen", "--zertifikat", o["reserve.crt"], "--schluessel", o["reserve.key"]);
        var reserve = Pin(text);
        var tcp = FreierPort(SocketType.Stream);
        var udp = FreierPort(SocketType.Dgram);
        Pruefe.Gleich(0, (await Werkzeug("profil-schreiben", "--name", "Meine Staffel", "--version", "1", "--host", host + "/v4", "--tcp-port",
            tcp.ToString(System.Globalization.CultureInfo.InvariantCulture), "--pin", pin, "--pin", reserve, "--aus", o["daten.json"])).Code, "profil-schreiben");
        Pruefe.Gleich(0, (await Werkzeug("profil-signieren", "--daten", o["daten.json"], "--schluessel", o["profil.key"], "--aus", o["profil.json"])).Code, "profil-signieren");
        (code, text) = await Werkzeug("konfig-schreiben", "--aus", o["server.json"], "--binden", "127.0.0.1",
            "--tcp-port", tcp.ToString(System.Globalization.CultureInfo.InvariantCulture), "--udp-port", udp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--profil", o["profil.json"], "--zustand", o["zustand"]);
        Pruefe.Gleich(0, code, "konfig-schreiben: " + text);
        return new Einrichtung(o, o["server.json"], c, spki, pin, reserve, tcp, udp);
    }

    [Test]
    public static async Task Pruefen_VollstaendigSiehtSchluesselPinUndPorts()
    {
        using var o = new Ordner();
        var e = await EinrichtenAsync(o);

        var (code, text) = await Werkzeug("pruefen", "--konfig", e.Konfig);
        Pruefe.Gleich(0, code, "pruefen ohne --vollstaendig wie funk-server pruefen: " + text);
        Pruefe.Wahr(text.Contains("Konfiguration", StringComparison.Ordinal) && !text.Contains("FEHLER", StringComparison.Ordinal), text);

        (code, text) = await Werkzeug("pruefen", "--vollstaendig", "--konfig", e.Konfig, "--credentials", e.Credentials, "--spki", e.Spki);
        Pruefe.Gleich(0, code, "alles da: " + text);
        foreach (var erwartet in new[] { "server.key", "funk-geheimnis", "funk-totp-schluessel", "Pin", "Signatur", "TCP " + e.TcpPort, "UDP " + e.UdpPort, "Uhr" })
            Pruefe.Wahr(text.Contains(erwartet, StringComparison.Ordinal), "nennt " + erwartet + ":\n" + text);
        Pruefe.Wahr(Regex.IsMatch(text, @"^OK\s+.*Pin.*im Profil", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), "Pin steht im Profil:\n" + text);
        Pruefe.Wahr(Regex.IsMatch(text, @"^Ergebnis: 0 Fehler", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), "Ergebniszeile:\n" + text);

        // Port belegt.
        using (var belegt = new TcpListener(IPAddress.Loopback, e.TcpPort))
        {
            belegt.Start();
            (code, text) = await Werkzeug("pruefen", "--vollstaendig", "--konfig", e.Konfig, "--credentials", e.Credentials);
            Pruefe.Gleich(1, code, "Port belegt: " + text);
            Pruefe.Wahr(Regex.IsMatch(text, @"^FEHLER\s+TCP " + e.TcpPort + ".*belegt", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), "belegt:\n" + text);
        }

        // Fremder Profilschlüssel.
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        (code, text) = await Werkzeug("pruefen", "--vollstaendig", "--konfig", e.Konfig, "--credentials", e.Credentials,
            "--spki", Convert.ToBase64String(fremd.ExportSubjectPublicKeyInfo()));
        Pruefe.Gleich(1, code, "fremder Profilschlüssel: " + text);
        Pruefe.Wahr(Regex.IsMatch(text, @"^FEHLER\s+.*Signatur", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), "Signatur:\n" + text);

        // TOTP-Schlüssel fehlt, Geheimnis zu kurz.
        var totp = Path.Combine(e.Credentials, "funk-totp-schluessel");
        File.Move(totp, totp + ".weg");
        File.WriteAllBytes(Path.Combine(e.Credentials, "funk-geheimnis"), new byte[5]);
        (code, text) = await Werkzeug("pruefen", "--vollstaendig", "--konfig", e.Konfig, "--credentials", e.Credentials);
        Pruefe.Gleich(1, code, "Dateien: " + text);
        Pruefe.Wahr(Regex.IsMatch(text, @"^FEHLER\s+.*funk-totp-schluessel.*fehlt", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), "TOTP fehlt:\n" + text);
        Pruefe.Wahr(Regex.IsMatch(text, @"^FEHLER\s+.*funk-geheimnis.*32", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), "Geheimnis zu kurz:\n" + text);
        File.Move(totp + ".weg", totp);
        Pruefe.Gleich(0, (await Werkzeug("geheimnis-erzeugen", "--datei", Path.Combine(e.Credentials, "funk-geheimnis"), "--ersetzen")).Code, "Geheimnis neu");

        // Neuer Serverschlüssel, Profil nicht nachgezogen: Pin fehlt im Profil.
        Pruefe.Gleich(0, (await Werkzeug("schluessel-erzeugen", "--zertifikat", Path.Combine(e.Credentials, "server.crt"), "--schluessel",
            Path.Combine(e.Credentials, "server.key"), "--ersetzen")).Code, "neuer Serverschlüssel");
        (code, text) = await Werkzeug("pruefen", "--vollstaendig", "--konfig", e.Konfig, "--credentials", e.Credentials);
        Pruefe.Gleich(1, code, "Pin fehlt: " + text);
        Pruefe.Wahr(Regex.IsMatch(text, @"^FEHLER\s+.*Pin.*nicht im Profil", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), "Pin nicht im Profil:\n" + text);
        Pruefe.Wahr(text.Contains("profil-anfrage", StringComparison.Ordinal), "mit dem Weg (profil-anfrage):\n" + text);

        // Ohne Schlüsselordner: gemeldet, nichts angelegt.
        (code, text) = await Werkzeug("pruefen", "--vollstaendig", "--konfig", e.Konfig, "--credentials", o["gibt-es-nicht"]);
        Pruefe.Gleich(1, code, "ohne Schlüsselordner: " + text);
        Pruefe.Falsch(Directory.Exists(o["gibt-es-nicht"]), "nichts angelegt");
    }

    /// <summary>Die Prüfungen, die nur unter Linux gehen (Rechte, Uhr, Dienst), über eine Attrappe des Systems.</summary>
    [Test]
    public static async Task Pruefen_RechteUhrUndDnsUeberDasSystem()
    {
        using var o = new Ordner();
        var e = await EinrichtenAsync(o, "funk.example.org");
        var system = new SystemAttrappe
        {
            Linux = true,
            Uhr = "no",
            Modi = { [Path.Combine(e.Credentials, "server.key")] = (UnixFileMode)0b110_100_100, [e.Credentials] = (UnixFileMode)0b111_000_000 },
            Dns = { ["funk.example.org"] = [IPAddress.Parse("203.0.113.7")] },
        };
        var befunde = Serverpruefung.Pruefen(e.Konfig, vollstaendig: true, e.Credentials, [e.Spki], null, system);
        string Text(Befundstufe s, string teil) => string.Join("\n", befunde.Where(b => b.Stufe == s && b.Text.Contains(teil, StringComparison.Ordinal)).Select(b => b.Text));
        Pruefe.Wahr(Text(Befundstufe.Warnung, "server.key").Contains("chmod 600", StringComparison.Ordinal), "server.key für alle lesbar:\n" + string.Join("\n", befunde));
        Pruefe.Wahr(Text(Befundstufe.Warnung, "Uhr").Length > 0, "Uhr nicht synchron: Warnung");
        Pruefe.Wahr(Text(Befundstufe.Hinweis, "funk.example.org").Contains("203.0.113.7", StringComparison.Ordinal), "wohin der Name zeigt");
        Pruefe.Gleich("", Text(Befundstufe.Fehler, ""), "keine Fehler");

        system.Uhr = "yes";
        system.Dns.Clear();
        system.Modi[Path.Combine(e.Credentials, "server.key")] = (UnixFileMode)0b110_000_000;
        befunde = Serverpruefung.Pruefen(e.Konfig, vollstaendig: true, e.Credentials, [e.Spki], "neu.example.org", system);
        Pruefe.Wahr(Text(Befundstufe.Ok, "Uhr").Length > 0, "Uhr synchron");
        Pruefe.Wahr(Text(Befundstufe.Warnung, "funk.example.org").Contains("DNS", StringComparison.Ordinal), "Name nicht im DNS");
        Pruefe.Wahr(Text(Befundstufe.Fehler, "neu.example.org").Contains("nicht im Profil", StringComparison.Ordinal), "--host, der nicht im Profil steht");
        Pruefe.Gleich("", Text(Befundstufe.Warnung, "chmod"), "0600: keine Warnung");
    }

    [Test]
    public static async Task ProfilAnfrage_ZeileUndKennung()
    {
        using var o = new Ordner();
        var e = await EinrichtenAsync(o);
        var crt = Path.Combine(e.Credentials, "server.crt");
        var (code, text) = await Werkzeug("profil-anfrage", "--host", "neu.example.org", "--tcp-port", "443", "--zertifikat", crt);
        Pruefe.Gleich(0, code, "profil-anfrage: " + text);
        Pruefe.Wahr(text.Split('\n', StringSplitOptions.TrimEntries).Contains("TR-PROFILANFRAGE v1 neu.example.org 443 " + e.Pin), "die Zeile:\n" + text);
        Pruefe.Wahr(Fingerabdruck.TryAusBase64(e.Pin, out var pin) && text.Contains("Kennung zum Vorlesen: " + pin.Anzeige, StringComparison.Ordinal), "die Kennung:\n" + text);
        Pruefe.Wahr(text.Contains("Telefon", StringComparison.Ordinal), "wie die Kennung reist");

        // Aus server.json: Port und Zertifikat, dazu die Datei für den Stick.
        (code, text) = await Werkzeug("profil-anfrage", "--host", "neu.example.org/v4v6", "--konfig", e.Konfig, "--credentials", e.Credentials, "--aus", o["profilanfrage.txt"]);
        Pruefe.Gleich(0, code, "mit --konfig: " + text);
        var zeile = "TR-PROFILANFRAGE v1 neu.example.org/v4v6 " + e.TcpPort + " " + e.Pin;
        Pruefe.Gleich(zeile, File.ReadAllText(o["profilanfrage.txt"], Encoding.UTF8).Trim(), "profilanfrage.txt");

        foreach (var (was, args, erwartet) in new[]
        {
            ("https", new[] { "--host", "https://neu.example.org", "--tcp-port", "443", "--zertifikat", crt }, "ohne https"),
            ("Port", ["--host", "neu.example.org", "--tcp-port", "0", "--zertifikat", crt], "1 und 65535"),
            ("ohne Port", ["--host", "neu.example.org", "--zertifikat", crt], "--tcp-port"),
            ("zwei Hosts", ["--host", "a.example.org", "--host", "b.example.org", "--tcp-port", "443", "--zertifikat", crt], "einen Host"),
        })
        {
            (code, text) = await Werkzeug(["profil-anfrage", .. args]);
            Pruefe.Gleich(2, code, was + ": " + text);
            Pruefe.Wahr(text.Contains(erwartet, StringComparison.Ordinal), was + ": " + text);
        }
    }
}

/// <summary>Attrappe des Systems für <see cref="Serverpruefung"/> und <see cref="Umzug"/>: Linux, Rechte, Uhr, DNS, Besitzer.</summary>
internal sealed class SystemAttrappe : Systemblick
{
    public bool Linux { get; set; }
    public string? Uhr { get; set; }
    public Dictionary<string, UnixFileMode> Modi { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IPAddress[]> Dns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Pfad, string Besitzer)> Besitzer { get; } = [];

    public override bool IstLinux => Linux;

    public override UnixFileMode? Modus(string pfad) => Modi.TryGetValue(Path.GetFullPath(pfad), out var m) ? m : Linux ? (UnixFileMode)0b110_000_000 : null;

    public override string? ZeitSynchron() => Uhr;

    public override IPAddress[] Aufloesen(string host, AddressFamily familie) =>
        Dns.TryGetValue(host, out var a) ? a : throw new SocketException((int)SocketError.HostNotFound);

    public override void BesitzerSetzen(string pfad, string besitzer) => Besitzer.Add((pfad, besitzer));
}
