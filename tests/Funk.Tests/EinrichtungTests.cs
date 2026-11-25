// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Funk.Admin;
using Funk.Protokoll.Identitaet;
using Funk.Server.Betrieb;
using Funk.Server.Kern;

namespace Funk.Tests;

/// <summary>
/// <c>funk-admin einrichten</c> (A55, EINRICHTEN-UMZUG-ENTWURF "Vorschlag Einrichtung"), der Teil nach funk-einrichten.sh. Alles in
/// einem Testordner (<see cref="Einrichtorte.Unter"/>); das System ist eine Attrappe (<see cref="LinuxAttrappe"/>), die Aufrufe wie
/// useradd und systemctl aufzeichnet und wie Debian beantwortet. Geprüft wird: erster Lauf mit Fragen bis WARTET AUF PROFIL, zweiter
/// Lauf mit dem Profil bis "bereit", dritter Lauf ändert nichts; abgelehnte Profile, übersetzte Startfehler, Units mit .alt,
/// fremder Kurzbefehl, Port unter 1024 mit Drop-in, belegter Port, Firewall nur mit ja, Umzug. Was nur ein echtes Linux zeigt
/// (useradd, systemd, Rechte, journalctl), prüft der Probelauf im Wegwerf-Container (EINRICHTEN.md).
/// </summary>
internal static class EinrichtungTests
{
    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

    internal static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden.");
    }

    private static bool Zeile(string text, string muster) => Regex.IsMatch(text, muster, RegexOptions.Multiline, Frist);

    /// <summary>Ein frischer Server im Testordner: das Paket in opt/funk mit den Units aus dem Repository und einem Test-Profilschlüssel.</summary>
    internal sealed class Server : IDisposable
    {
        public Server()
        {
            O = new AdminEinrichtungTests.Ordner();
            Orte = Einrichtorte.Unter(O.Pfad);
            Directory.CreateDirectory(Orte.Paketkonfig);
            foreach (var u in new[] { "funk-server.service", "funk-echo.service" })
                File.Copy(Path.Combine(Repo(), "konfig", u), Path.Combine(Orte.Paketkonfig, u));
            Profilschluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(Path.Combine(Orte.Paketkonfig, "profilschluessel.txt"),
                "# Öffentlicher Profilschlüssel (Test)\n" + Convert.ToBase64String(Profilschluessel.ExportSubjectPublicKeyInfo()) + "\n");
            using var reserve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Reserve = Fingerabdruck.AusSchluessel(reserve);
            System.Ss = SsAusgabe;
        }

        public AdminEinrichtungTests.Ordner O { get; }
        public Einrichtorte Orte { get; }
        public ECDsa Profilschluessel { get; }
        public Fingerabdruck Reserve { get; }
        public LinuxAttrappe System { get; } = new();
        public List<string> Gesendet { get; } = [];
        public JsonArray Konten { get; set; } = [];
        public DateTimeOffset Jetzt { get; } = DateTimeOffset.UtcNow;

        /// <summary>Wie viele Antworten der letzte Lauf gelesen hat (je Frage eine Zeile).</summary>
        public int Antworten { get; private set; }

        public async Task<(int Code, string Text)> EinrichtenAsync(string eingabe, Einrichtauftrag? auftrag = null)
        {
            var aus = new StringWriter();
            var ein = new ZaehlenderLeser(eingabe);
            var e = new Einrichtung(ein, aus, System, Orte, Senden, () => Jetzt);
            var code = await e.AusfuehrenAsync(auftrag ?? new Einrichtauftrag());
            Antworten = ein.Gelesen;
            return (code, aus.ToString());
        }

        private sealed class ZaehlenderLeser(string text) : StringReader(text)
        {
            public int Gelesen { get; private set; }

            public override string? ReadLine()
            {
                var z = base.ReadLine();
                if (z is not null) Gelesen++;
                return z;
            }
        }

        private Task<VerwaltungsAntwort> Senden(string socket, string befehl, JsonObject argumente)
        {
            Gesendet.Add(befehl);
            if (!System.Aktiv("funk-server")) throw new SocketException((int)SocketError.ConnectionRefused);
            return Task.FromResult(befehl switch
            {
                "status" => VerwaltungsAntwort.Gut("Funk-Server 0.2.0: 0 Verbindungen, 0 Sitzungen, " + Konten.Count.ToString(CultureInfo.InvariantCulture) + " Konten, 0 Geräte, 0 Netze"),
                "konten" => VerwaltungsAntwort.Gut("", Konten.DeepClone()),
                "profil-neu-laden" => VerwaltungsAntwort.Gut("Profil Version neu geladen"),
                _ => VerwaltungsAntwort.Fehler("unbekannter Befehl " + befehl),
            });
        }

        /// <summary>Was ss im laufenden Dienst zeigt: die Ports aus server.json.</summary>
        private string SsAusgabe(string art)
        {
            if (!System.Aktiv("funk-server") || !File.Exists(Orte.Konfig)) return "";
            var k = ServerKonfiguration.Laden(Orte.Konfig);
            return art == "t"
                ? "LISTEN 0 512 " + k.Binden[0] + ":" + k.TcpPort.ToString(CultureInfo.InvariantCulture) + " 0.0.0.0:*\n"
                : "UNCONN 0 0 " + k.Binden[0] + ":" + k.UdpPort.ToString(CultureInfo.InvariantCulture) + " 0.0.0.0:*\n";
        }

        public Fingerabdruck ServerPin
        {
            get
            {
                using var z = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(Orte.Credentials, "server.crt")));
                return Fingerabdruck.AusZertifikat(z);
            }
        }

        /// <summary>Ein Profil wie von profil-erneuern, signiert mit dem Testschlüssel des Pakets oder <paramref name="schluessel"/>.</summary>
        public string ProfilSchreiben(uint version, string host, int port, ECDsa? schluessel = null, bool mitPin = true)
        {
            var pins = new List<Fingerabdruck> { Reserve };
            if (mitPin) pins.Insert(0, ServerPin);
            else
            {
                using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                pins.Add(Fingerabdruck.AusSchluessel(fremd));
            }
            var p = new Serverprofil("Meine Staffel", version, Jetzt.ToUnixTimeMilliseconds(), Jetzt.AddDays(365).ToUnixTimeMilliseconds(),
                [new ProfilHost(host, true, false)], (ushort)port, pins, []);
            var datei = O["profil-" + Guid.NewGuid().ToString("N")[..6] + "-v" + version.ToString(CultureInfo.InvariantCulture) + ".json"];
            File.WriteAllBytes(datei, SigniertesProfil.Signieren(p.AlsJson(), schluessel ?? Profilschluessel).AlsJson());
            return datei;
        }

        /// <summary>Alle Dateien des Testservers mit Inhalt, für "nichts geändert".</summary>
        public Dictionary<string, byte[]> Stand() =>
            Directory.GetFiles(O.Pfad, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            Profilschluessel.Dispose();
            O.Dispose();
        }
    }

    /// <summary>Heimserver hinter der FRITZ!Box: Name, Adresse aus ip route (Vorschlag), Router ja, Ports 8443/47443, Echo ja.</summary>
    private const string HeimAntworten = "funk.example.org\n\n\n\n\n\n\n\n";

    private static void UnveraendertAusser(Dictionary<string, byte[]> vorher, Dictionary<string, byte[]> nachher, string was)
    {
        var anders = vorher.Keys.Union(nachher.Keys).Where(k => !vorher.TryGetValue(k, out var a) || !nachher.TryGetValue(k, out var b) || !a.AsSpan().SequenceEqual(b)).ToList();
        Pruefe.Gleich("", string.Join(", ", anders), was);
    }

    [Test]
    public static async Task Einrichten_ErsterLauf_FragtLegtAnUndWartetAufProfil()
    {
        using var s = new Server();
        s.System.Dns["funk.example.org"] = [IPAddress.Parse("203.0.113.7")];
        var (code, text) = await s.EinrichtenAsync(HeimAntworten);
        Pruefe.Gleich(Einrichtung.WartetAufProfil, code, "erster Lauf endet mit 4:\n" + text);

        // server.json aus den Antworten, mit den Pfaden der Einrichtung
        var k = ServerKonfiguration.Laden(s.Orte.Konfig);
        Pruefe.Gleich("", string.Join(" | ", k.Pruefen()), "server.json besteht die Prüfung des Servers");
        Pruefe.Gleich("192.168.1.250", string.Join(",", k.Binden), "Bindadresse aus ip route");
        Pruefe.Gleich(8443, k.TcpPort, "TCP hinter dem Router");
        Pruefe.Gleich(8443, k.TcpPortOeffentlich, "außen wie innen");
        Pruefe.Gleich(47443, k.UdpPort, "UDP");
        Pruefe.Gleich(s.Orte.Profil, k.Aufloesen(k.Profil!), "Profilpfad");
        Pruefe.Gleich(s.Orte.Zustand, k.Aufloesen(k.Zustandsverzeichnis), "Zustand");

        // Benutzer, Ordner, Schlüssel
        Pruefe.Wahr(s.System.Befehle.Contains("useradd --system --no-create-home --shell /usr/sbin/nologin funk"), "Benutzer funk:\n" + string.Join("\n", s.System.Befehle));
        Pruefe.Wahr(s.System.Befehle.Contains("useradd --system --no-create-home --shell /usr/sbin/nologin funk-echo"), "Benutzer funk-echo");
        foreach (var d in new[] { "server.crt", "server.key", "funk-geheimnis", "funk-totp-schluessel" })
            Pruefe.Wahr(File.Exists(Path.Combine(s.Orte.Credentials, d)), d + " erzeugt");
        Pruefe.Gleich((UnixFileMode)0b111_000_000, s.System.Modi[Path.GetFullPath(s.Orte.Credentials)], "credentials 0700");
        Pruefe.Gleich((UnixFileMode)0b111_000_000, s.System.Modi[Path.GetFullPath(s.Orte.EchoZustand)], "/var/lib/funk-echo 0700");
        Pruefe.Wahr(s.System.Besitzer.Contains((s.Orte.EchoZustand, "funk-echo")), "/var/lib/funk-echo gehört funk-echo");

        // Units, echo.env, Profilschlüssel, Kurzbefehl
        var unit = File.ReadAllText(s.Orte.Unit("funk-server.service"));
        Pruefe.Gleich(File.ReadAllText(Path.Combine(Repo(), "konfig", "funk-server.service")).Replace("\r\n", "\n", StringComparison.Ordinal), unit, "funk-server.service wie im Paket, mit LF");
        Pruefe.Wahr(File.Exists(s.Orte.Unit("funk-echo.service")), "funk-echo.service");
        Pruefe.Wahr(File.ReadAllText(s.Orte.EchoEnv).Contains("\nFUNK_ECHO_ADRESSE=192.168.1.250\n", StringComparison.Ordinal), "echo.env mit der Bindadresse");
        Pruefe.Falsch(File.Exists(s.Orte.DropIn), "8443 braucht kein Drop-in");
        Pruefe.BytesGleich(File.ReadAllBytes(Path.Combine(s.Orte.Paketkonfig, "profilschluessel.txt")), File.ReadAllBytes(s.Orte.Profilschluessel), "profilschluessel.txt neben server.json");
        var kurz = File.ReadAllText(s.Orte.Kurzbefehl);
        Pruefe.Wahr(kurz.StartsWith("#!/bin/sh\n", StringComparison.Ordinal) && kurz.Contains("FUNK_KONFIG:=" + s.Orte.Konfig, StringComparison.Ordinal)
            && kurz.Contains("export FUNK_KONFIG", StringComparison.Ordinal) && kurz.Contains("/usr/bin/dotnet " + s.Orte.Programme, StringComparison.Ordinal)
            && !kurz.Contains('\r', StringComparison.Ordinal), "Kurzbefehl:\n" + kurz);
        Pruefe.Gleich((UnixFileMode)0b111_101_101, s.System.Modi[Path.GetFullPath(s.Orte.Kurzbefehl)], "Kurzbefehl 0755");

        // Profil fehlt: Anfrage, kein Start
        var anfrage = "TR-PROFILANFRAGE v1 funk.example.org 8443 " + s.ServerPin.AlsBase64();
        Pruefe.Wahr(text.Contains("WARTET AUF PROFIL", StringComparison.Ordinal) && text.Contains(anfrage, StringComparison.Ordinal)
            && text.Contains(s.ServerPin.Anzeige, StringComparison.Ordinal) && text.Contains("--profil profil.json", StringComparison.Ordinal), "Anfrage und Kennung:\n" + text);
        // Die Kennung deckt nur den Pin; Name und Port gehen mit ans Telefon (Gegenprüfung 01.10.2026).
        Pruefe.Wahr(text.Contains("Kennung vor: " + s.ServerPin.Anzeige + ", dazu Name und Port: funk.example.org, 8443", StringComparison.Ordinal),
            "Name und Port am Telefon:\n" + text);
        Pruefe.Gleich(anfrage, File.ReadAllText(s.Orte.Anfrage).Trim(), "profilanfrage.txt");
        Pruefe.Falsch(s.System.Befehle.Any(b => b.StartsWith("systemctl enable", StringComparison.Ordinal) || b.StartsWith("systemctl start", StringComparison.Ordinal)), "ohne Profil kein Start");

        // Ausgabe: DNS, Zeilen mit OK/NEU, Router-Freigaben
        Pruefe.Wahr(text.Contains("203.0.113.7", StringComparison.Ordinal), "zeigt, wohin der Name im DNS zeigt");
        Pruefe.Wahr(Zeile(text, @"^NEU\s+Benutzer funk\b") && Zeile(text, @"^NEU\s+.*server\.json") && Zeile(text, @"^OK\s+Uhr"), "Zeilen mit NEU und OK:\n" + text);
        Pruefe.Wahr(Zeile(text, @"TCP\s+außen 8443.*192\.168\.1\.250:8443") && Zeile(text, @"UDP\s+außen 47443.*192\.168\.1\.250:47443"), "Portfreigaben am Router:\n" + text);
        Pruefe.Falsch(Zeile(text, @"^FEHLT"), "nichts fehlt außer dem Profil (das steht als WARTET da):\n" + text);
    }

    /// <summary>
    /// Die Hilfe sagte bis 0.5.0 "beim ersten Lauf fünf Fragen". Gestellt werden sechs (öffentlicher Name, Bindadresse, Router, TCP-Port,
    /// UDP-Port, Echo-Funker), mit Router acht (dazu TCP- und UDP-Port außen am Router). Gezählt an den Antworten, die der erste Lauf
    /// liest; jede Antwort ist die Eingabetaste oder ein Wert, und mit einer Antwort weniger bräche der Lauf ab.
    /// </summary>
    [Test]
    public static async Task Einrichten_ErsterLauf_SechsFragenMitRouterAcht_WieInDerHilfe()
    {
        foreach (var (was, antworten, fragen) in new[] { ("mit Router", HeimAntworten, 8), ("ohne Router", "funk.example.org\n\nn\n\n\n\n", 6) })
        {
            using var s = new Server();
            s.System.Dns["funk.example.org"] = [IPAddress.Parse("203.0.113.7")];
            var (code, text) = await s.EinrichtenAsync(antworten);
            Pruefe.Gleich(Einrichtung.WartetAufProfil, code, was + ": erster Lauf endet mit 4:\n" + text);
            Pruefe.Gleich(fragen, s.Antworten, was + ": Fragen beim ersten Lauf");
            using var knapp = new Server();
            var (abbruch, _) = await knapp.EinrichtenAsync(antworten[..antworten.LastIndexOf('\n')]);
            Pruefe.Gleich(1, abbruch, was + ": eine Antwort weniger bricht ab");
        }
        var hilfe = File.ReadAllText(Path.Combine(Repo(), "src", "Funk.Admin", "Hilfe.cs"));
        Pruefe.Wahr(hilfe.Contains("beim ersten Lauf sechs Fragen (mit Router acht)", StringComparison.Ordinal) && !hilfe.Contains("fünf Fragen", StringComparison.Ordinal),
            "Hilfe nennt sechs Fragen, mit Router acht");
    }

    [Test]
    public static async Task Einrichten_ZweiterLaufMitProfil_StartetUndDritterAendertNichts()
    {
        using var s = new Server();
        await s.EinrichtenAsync(HeimAntworten);
        var profil = s.ProfilSchreiben(2, "funk.example.org", 8443);
        var vorBefehle = s.System.Befehle.Count;

        var (code, text) = await s.EinrichtenAsync("", new Einrichtauftrag(Profil: profil));
        Pruefe.Gleich(0, code, "zweiter Lauf ohne eine Frage:\n" + text);
        Pruefe.BytesGleich(File.ReadAllBytes(profil), File.ReadAllBytes(s.Orte.Profil), "Profil eingespielt");
        var neu = s.System.Befehle.Skip(vorBefehle).ToList();
        Pruefe.Wahr(neu.Contains("systemctl daemon-reload") && neu.Contains("systemctl enable --now funk-server"), "daemon-reload und Start:\n" + string.Join("\n", neu));
        Pruefe.Falsch(neu.Any(b => b.StartsWith("useradd", StringComparison.Ordinal)), "Benutzer gibt es schon");
        Pruefe.Wahr(Zeile(text, @"^OK\s+Funk-Server 0\.2\.0 bereit") && Zeile(text, @"^OK\s+.*TCP 8443.*UDP 47443"), "bereit und Ports:\n" + text);
        Pruefe.Wahr(text.Contains("erster-admin", StringComparison.Ordinal) && text.Contains("Eigentümer", StringComparison.Ordinal) && text.Contains(s.ServerPin.Anzeige, StringComparison.Ordinal),
            "Abschlussblock mit nächster Handlung:\n" + text);
        // Seit 0.3.0 schützt sich der Server selbst mit der Sperrliste (A51); der Abschluss nennt, wo man sie sieht, und die
        // Checkliste für den Rest des Rechners (Zusammenführung 0.4.0: einrichten kannte beides nicht).
        Pruefe.Wahr(text.Contains("sudo funk-admin sperrliste", StringComparison.Ordinal) && text.Contains("HEIMSERVER-HAERTUNG.md", StringComparison.Ordinal),
            "Abschluss nennt Sperrliste und Härtung:\n" + text);
        Pruefe.Wahr(text.Contains("funk-echo.dll", StringComparison.Ordinal) && !neu.Contains("systemctl enable --now funk-echo"), "Echo noch nicht registriert: Hinweis statt Start:\n" + text);

        // Dritter Lauf: nichts mehr zu tun.
        var stand = s.Stand();
        vorBefehle = s.System.Befehle.Count;
        (code, text) = await s.EinrichtenAsync("");
        Pruefe.Gleich(0, code, "dritter Lauf:\n" + text);
        Pruefe.Falsch(Zeile(text, @"^(NEU|FEHLT)"), "nichts neu, nichts fehlt:\n" + text);
        UnveraendertAusser(stand, s.Stand(), "dritter Lauf ändert keine Datei");
        neu = s.System.Befehle.Skip(vorBefehle).ToList();
        Pruefe.Falsch(neu.Any(b => Regex.IsMatch(b, @"^(useradd|systemctl (enable|start|restart|stop|daemon-reload))", RegexOptions.None, Frist)), "kein Eingriff:\n" + string.Join("\n", neu));

        // Echo registriert (geraet.pem da): jetzt startet einrichten auch den Echo-Funker.
        File.WriteAllText(s.Orte.EchoIdentitaet, "Test");
        (code, text) = await s.EinrichtenAsync("");
        Pruefe.Gleich(0, code, text);
        Pruefe.Wahr(s.System.Befehle.Contains("systemctl enable --now funk-echo"), "funk-echo gestartet");

        // Eigentümer gesetzt: keine Aufforderung mehr zu erster-admin.
        s.Konten = [new JsonObject { ["kontoId"] = 3, ["name"] = "Sarevoc", ["eigentuemer"] = true, ["admin"] = "AKTIV" }];
        (code, text) = await s.EinrichtenAsync("");
        Pruefe.Gleich(0, code, text);
        Pruefe.Wahr(text.Contains("Sarevoc (Konto 3)", StringComparison.Ordinal) && !text.Contains("erster-admin", StringComparison.Ordinal), "Eigentümer genannt:\n" + text);
    }

    [Test]
    public static async Task Einrichten_ProfilAbgelehnt_NichtsEingespielt()
    {
        using var s = new Server();
        await s.EinrichtenAsync(HeimAntworten);
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(s.O["kaputt.json"], "{\"daten\": \"kein Profil\"}");
        foreach (var (was, datei, erwartet) in new[]
        {
            ("fremd signiert", s.ProfilSchreiben(2, "funk.example.org", 8443, fremd), "nicht mit dem Profilschlüssel"),
            ("ohne Pin des Servers", s.ProfilSchreiben(2, "funk.example.org", 8443, mitPin: false), "Pin dieses Servers"),
            ("anderer Port", s.ProfilSchreiben(2, "funk.example.org", 443), "TCP-Port 443"),
            ("anderer Name", s.ProfilSchreiben(2, "heim.example.org", 8443), "funk.example.org steht nicht im Profil"),
            ("kein Profil", s.O["kaputt.json"], "kein signiertes Serverprofil"),
            ("gibt es nicht", s.O["fehlt.json"], "gibt es nicht"),
        })
        {
            var vorBefehle = s.System.Befehle.Count;
            var (code, text) = await s.EinrichtenAsync("", new Einrichtauftrag(Profil: datei));
            Pruefe.Gleich(1, code, was + ":\n" + text);
            Pruefe.Wahr(Zeile(text, "^FEHLT.*" + Regex.Escape(erwartet)) && text.Contains("Abhilfe", StringComparison.Ordinal), was + ": Grund und Abhilfe:\n" + text);
            Pruefe.Falsch(File.Exists(s.Orte.Profil), was + ": nichts eingespielt");
            Pruefe.Falsch(s.System.Befehle.Skip(vorBefehle).Any(b => b.StartsWith("systemctl enable", StringComparison.Ordinal)), was + ": kein Start");
        }

        // Älter als das eingespielte: abgelehnt, das neuere bleibt.
        var v3 = s.ProfilSchreiben(3, "funk.example.org", 8443);
        Pruefe.Gleich(0, (await s.EinrichtenAsync("", new Einrichtauftrag(Profil: v3))).Code, "Version 3");
        var (c2, t2) = await s.EinrichtenAsync("", new Einrichtauftrag(Profil: s.ProfilSchreiben(2, "funk.example.org", 8443)));
        Pruefe.Gleich(1, c2, "Version 2 nach 3:\n" + t2);
        Pruefe.Wahr(Zeile(t2, "^FEHLT.*Version 2.*Version 3"), "nennt beide Versionen:\n" + t2);
        Pruefe.BytesGleich(File.ReadAllBytes(v3), File.ReadAllBytes(s.Orte.Profil), "Version 3 bleibt");

        // Neueres Profil bei laufendem Dienst: eingespielt, alte Fassung daneben, Server lädt neu statt Neustart.
        var vorBefehle2 = s.System.Befehle.Count;
        var v4 = s.ProfilSchreiben(4, "funk.example.org", 8443);
        var (c4, t4) = await s.EinrichtenAsync("", new Einrichtauftrag(Profil: v4));
        Pruefe.Gleich(0, c4, "Version 4:\n" + t4);
        Pruefe.BytesGleich(File.ReadAllBytes(v3), File.ReadAllBytes(Path.Combine(s.Orte.Etc, "profil-v3.json")), "Version 3 als profil-v3.json daneben");
        Pruefe.Wahr(s.Gesendet.Contains("profil-neu-laden"), "profil-neu-laden über den Verwaltungssocket");
        Pruefe.Falsch(s.System.Befehle.Skip(vorBefehle2).Any(b => b.StartsWith("systemctl restart", StringComparison.Ordinal)), "kein Neustart nur fürs Profil");
    }

    [Test]
    public static async Task Einrichten_Startfehler_Uebersetzt()
    {
        foreach (var (status, journal, erwartet) in new[]
        {
            ("243", "funk-server.service: Failed to set up credentials: No such file or directory\nfunk-server.service: Main process exited, code=exited, status=243/CREDENTIALS", "243/CREDENTIALS"),
            ("226", "funk-server.service: Failed to set up mount namespacing: Permission denied\nfunk-server.service: Main process exited, code=exited, status=226/NAMESPACE", "pct set <id> --features nesting=1"),
            ("2", "Konfiguration ungültig:\n  binden: 10.9.9.9 gibt es hier nicht\nfunk-server.service: Main process exited, code=exited, status=2/INVALIDARGUMENT", "binden: 10.9.9.9"),
            ("1", "Unhandled exception. System.Exception: irgendwas\nfunk-server.service: Main process exited, code=exited, status=1/FAILURE", "irgendwas"),
        })
        {
            using var s = new Server();
            await s.EinrichtenAsync(HeimAntworten);
            s.System.Startfehler = status;
            s.System.Journal = journal;
            var (code, text) = await s.EinrichtenAsync("", new Einrichtauftrag(Profil: s.ProfilSchreiben(2, "funk.example.org", 8443)));
            Pruefe.Gleich(1, code, status + ":\n" + text);
            Pruefe.Wahr(Zeile(text, "^FEHLT") && text.Contains(erwartet, StringComparison.Ordinal) && text.Contains("Abhilfe", StringComparison.Ordinal), status + " übersetzt:\n" + text);
            Pruefe.Falsch(text.Contains("====", StringComparison.Ordinal), status + ": kein Abschlussblock");
        }
    }

    /// <summary>
    /// Befund der Prüfung der Pakete 0.4.0 (mittel): Im unprivilegierten LXC ohne nesting fallen unter Debian 13 laut
    /// Proxmox-Forum viele systemd-Dienste mit 243/CREDENTIALS aus, nicht mit 226/NAMESPACE (hier nicht gemessen, kein Linux).
    /// einrichten deutete 243 nur als fehlende Schlüsseldatei oder falschen Pfad, und die Anleitungen nannten nesting nur bei
    /// 226; wer auf Debian 13 nesting vergessen hatte, suchte an der falschen Stelle. Liegen alle Schlüsseldateien da und ist es
    /// ein Container (systemd-detect-virt: lxc), nennt einrichten jetzt dieselbe Abhilfe wie bei 226, und die Anleitungen
    /// verlangen nesting=1 schon unter "Was du brauchst".
    /// </summary>
    [Test]
    public static async Task Einrichten_243ImContainer_NenntNesting()
    {
        using (var s = new Server())
        {
            s.System.Container = "lxc";
            await s.EinrichtenAsync(HeimAntworten);
            s.System.Startfehler = "243";
            s.System.Journal = "funk-server.service: Failed to set up credentials: Protocol error\nfunk-server.service: Main process exited, code=exited, status=243/CREDENTIALS";
            var (code, text) = await s.EinrichtenAsync("", new Einrichtauftrag(Profil: s.ProfilSchreiben(2, "funk.example.org", 8443)));
            Pruefe.Gleich(1, code, text);
            Pruefe.Wahr(Zeile(text, "^FEHLT.*243/CREDENTIALS") && text.Contains("pct set <id> --features nesting=1", StringComparison.Ordinal),
                "243 im Container ohne fehlende Datei nennt nesting:\n" + text);
        }

        // Ohne Container bleibt es beim Vergleich der Pfade unter LoadCredential.
        using (var s = new Server())
        {
            await s.EinrichtenAsync(HeimAntworten);
            s.System.Startfehler = "243";
            s.System.Journal = "funk-server.service: Main process exited, code=exited, status=243/CREDENTIALS";
            var (code, text) = await s.EinrichtenAsync("", new Einrichtauftrag(Profil: s.ProfilSchreiben(2, "funk.example.org", 8443)));
            Pruefe.Gleich(1, code, text);
            Pruefe.Wahr(text.Contains("LoadCredential", StringComparison.Ordinal) && !text.Contains("nesting", StringComparison.Ordinal), "ohne Container kein nesting:\n" + text);
        }

        var docs = Path.Combine(Repo(), "docs");
        var einrichten = File.ReadAllText(Path.Combine(docs, "EINRICHTEN.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var brauchst = einrichten[einrichten.IndexOf("## Was du brauchst", StringComparison.Ordinal)..einrichten.IndexOf("## 1.", StringComparison.Ordinal)];
        Pruefe.Wahr(brauchst.Contains("nesting=1", StringComparison.Ordinal), "EINRICHTEN.md, Was du brauchst: nesting=1 im Container");
        var zeile243 = einrichten.Split('\n').Single(z => z.StartsWith("| `status=243/CREDENTIALS`", StringComparison.Ordinal));
        Pruefe.Wahr(zeile243.Contains("nesting=1", StringComparison.Ordinal), "Tabelle: 243 nennt nesting: " + zeile243);
        var probe = einrichten[einrichten.IndexOf("6. Fehlerbilder", StringComparison.Ordinal)..];
        probe = probe[..probe.IndexOf("\n7.", StringComparison.Ordinal)];
        Pruefe.Wahr(probe.Contains("226/NAMESPACE", StringComparison.Ordinal) && probe.Contains("243/CREDENTIALS", StringComparison.Ordinal),
            "Probelauf: ohne nesting kommt die pct-Zeile bei 226 oder bei 243: " + probe);
        var heim = File.ReadAllText(Path.Combine(docs, "HEIMSERVER-PROXMOX.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var abschnitt7 = heim[heim.IndexOf("## 7.", StringComparison.Ordinal)..heim.IndexOf("## 8.", StringComparison.Ordinal)];
        var absatz243 = abschnitt7[abschnitt7.IndexOf("`status=243/CREDENTIALS`", StringComparison.Ordinal)..];
        absatz243 = absatz243[..absatz243.IndexOf("\n- ", StringComparison.Ordinal)];
        Pruefe.Wahr(absatz243.Contains("nesting=1", StringComparison.Ordinal), "HEIMSERVER-PROXMOX.md, Abschnitt 7: 243 nennt nesting: " + absatz243);
    }

    [Test]
    public static async Task Einrichten_UnitsKurzbefehlUndRechte_NurWasFehltOderAbweicht()
    {
        using var s = new Server();
        await s.EinrichtenAsync(HeimAntworten);
        await s.EinrichtenAsync("", new Einrichtauftrag(Profil: s.ProfilSchreiben(2, "funk.example.org", 8443)));

        // Eine von Hand geänderte Unit: die neue aus dem Paket, die alte als .alt daneben, Neustart.
        var unit = s.Orte.Unit("funk-server.service");
        File.AppendAllText(unit, "# von Hand\n");
        var geaendert = File.ReadAllBytes(unit);
        // Ein fremder Kurzbefehl bleibt.
        File.WriteAllText(s.Orte.Kurzbefehl, "#!/bin/sh\necho fremd\n");
        // Eine Unit im Paket mit CRLF (etwa aus einem Windows-Ordner): geschrieben wird sie mit LF.
        var echoPaket = Path.Combine(s.Orte.Paketkonfig, "funk-echo.service");
        File.WriteAllText(echoPaket, File.ReadAllText(echoPaket).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) + "# neu im Paket\r\n");
        // Schlüssel für andere lesbar.
        var key = Path.Combine(s.Orte.Credentials, "server.key");
        s.System.Modi[Path.GetFullPath(key)] = (UnixFileMode)0b110_100_100;
        var vorBefehle = s.System.Befehle.Count;

        var (code, text) = await s.EinrichtenAsync("");
        Pruefe.Gleich(0, code, text);
        Pruefe.BytesGleich(geaendert, File.ReadAllBytes(unit + ".alt"), "alte Unit als .alt");
        Pruefe.Gleich(File.ReadAllText(Path.Combine(s.Orte.Paketkonfig, "funk-server.service")).Replace("\r\n", "\n", StringComparison.Ordinal), File.ReadAllText(unit), "Unit aus dem Paket");
        Pruefe.Wahr(Zeile(text, @"^NEU\s+.*funk-server\.service.*\.alt"), "meldet .alt:\n" + text);
        var echoUnit = File.ReadAllText(s.Orte.Unit("funk-echo.service"));
        Pruefe.Wahr(!echoUnit.Contains('\r', StringComparison.Ordinal) && echoUnit.EndsWith("# neu im Paket\n", StringComparison.Ordinal), "Echo-Unit mit LF");
        Pruefe.Gleich("#!/bin/sh\necho fremd\n", File.ReadAllText(s.Orte.Kurzbefehl), "fremder Kurzbefehl unverändert");
        Pruefe.Wahr(Zeile(text, @"^FEHLT\s+.*" + Regex.Escape(s.Orte.Kurzbefehl) + ".*nicht"), "fremder Kurzbefehl gemeldet:\n" + text);
        Pruefe.Wahr(text.Contains("--konfig " + s.Orte.Konfig, StringComparison.Ordinal), "ohne Kurzbefehl die ausgeschriebenen Befehle:\n" + text);
        Pruefe.Gleich((UnixFileMode)0b110_000_000, s.System.Modi[Path.GetFullPath(key)], "server.key wieder 0600");
        Pruefe.Wahr(Zeile(text, @"^NEU\s+.*server\.key.*0600"), "Rechte gemeldet:\n" + text);
        var neu = s.System.Befehle.Skip(vorBefehle).ToList();
        Pruefe.Wahr(neu.Contains("systemctl daemon-reload") && neu.Contains("systemctl restart funk-server"), "geänderte Unit: daemon-reload und Neustart:\n" + string.Join("\n", neu));
    }

    [Test]
    public static async Task Einrichten_EchterServer_Port443MitDropInUndBelegterPort()
    {
        using var s = new Server();
        s.System.Adresse = "203.0.113.7";
        s.System.Belegt.Add((443, ProtocolType.Tcp));
        // Name, Adresse (Vorschlag), kein Router (Vorschlag bei öffentlicher Adresse), TCP 443 belegt -> 8443 vorgeschlagen; erst 443, dann 8443.
        var (code, text) = await s.EinrichtenAsync("funk.example.org\n\n\n443\n8443\n\nn\n");
        Pruefe.Gleich(Einrichtung.WartetAufProfil, code, text);
        Pruefe.Wahr(text.Contains("TCP-Port am Server [8443]", StringComparison.Ordinal), "443 belegt: 8443 vorgeschlagen:\n" + text);
        Pruefe.Wahr(Regex.IsMatch(text, "443.*belegt.*nginx", RegexOptions.None, Frist), "sagt, wer 443 hat:\n" + text);
        var k = ServerKonfiguration.Laden(s.Orte.Konfig);
        Pruefe.Gleich(8443, k.TcpPort, "8443 genommen");
        Pruefe.Gleich(null, k.OeffentlicherTcpPort, "ohne Router kein eigener Port außen");
        Pruefe.Falsch(File.Exists(s.Orte.DropIn), "8443 ohne Drop-in");
        Pruefe.Falsch(File.Exists(s.Orte.EchoEnv) || s.System.Benutzer.Contains("funk-echo"), "Echo nein: kein Benutzer, keine echo.env");

        // Ein zweiter Server mit freiem 443: Vorschlag 443, Drop-in mit CAP_NET_BIND_SERVICE.
        using var s2 = new Server();
        s2.System.Adresse = "203.0.113.8";
        (code, text) = await s2.EinrichtenAsync("funk.example.org\n\n\n\n\nn\n");
        Pruefe.Gleich(Einrichtung.WartetAufProfil, code, text);
        Pruefe.Gleich(443, ServerKonfiguration.Laden(s2.Orte.Konfig).TcpPort, "443 frei: vorgeschlagen und genommen");
        var dropIn = File.ReadAllText(s2.Orte.DropIn);
        Pruefe.Wahr(dropIn.Contains("[Service]\nAmbientCapabilities=CAP_NET_BIND_SERVICE\nCapabilityBoundingSet=CAP_NET_BIND_SERVICE\n", StringComparison.Ordinal), "Drop-in:\n" + dropIn);
        Pruefe.Wahr(text.Contains("TR-PROFILANFRAGE v1 funk.example.org 443 ", StringComparison.Ordinal), "Anfrage mit 443:\n" + text);
    }

    [Test]
    public static async Task Einrichten_Firewall_NurAnzeigenUfwNurMitJa()
    {
        // Im LXC hinter dem Router: Block für die Proxmox-Firewall, Freigaben am Router, keine Frage.
        using (var s = new Server())
        {
            s.System.Container = "lxc";
            var (_, text) = await s.EinrichtenAsync(HeimAntworten);
            Pruefe.Wahr(text.Contains("/etc/pve/firewall/", StringComparison.Ordinal) && text.Contains("IN ACCEPT -p tcp -dport 8443", StringComparison.Ordinal)
                && text.Contains("IN ACCEPT -p udp -dport 47443", StringComparison.Ordinal), "Proxmox-Block:\n" + text);
        }

        // Echter Server mit aktivem ufw: zeigen, fragen, nur mit ja eintragen.
        using var s2 = new Server();
        s2.System.Adresse = "203.0.113.7";
        s2.System.Ufw = "Status: active\n\nTo                         Action      From\n--                         ------      ----\n22/tcp                     ALLOW       Anywhere\n";
        var (code, t2) = await s2.EinrichtenAsync("funk.example.org\n\n\n\n\nn\n\nn\n");
        Pruefe.Gleich(Einrichtung.WartetAufProfil, code, t2);
        Pruefe.Wahr(t2.Contains("ufw allow 443/tcp", StringComparison.Ordinal) && t2.Contains("ufw allow 47443/udp", StringComparison.Ordinal), "zeigt die Regeln:\n" + t2);
        Pruefe.Falsch(s2.System.Befehle.Any(b => b.StartsWith("ufw allow", StringComparison.Ordinal)), "Eingabetaste und n: nichts eingetragen");
        (code, t2) = await s2.EinrichtenAsync("ja\n");
        Pruefe.Gleich(Einrichtung.WartetAufProfil, code, t2);
        Pruefe.Wahr(s2.System.Befehle.Contains("ufw allow 443/tcp") && s2.System.Befehle.Contains("ufw allow 47443/udp"), "mit ja eingetragen");
        (_, t2) = await s2.EinrichtenAsync("");
        Pruefe.Wahr(Zeile(t2, @"^OK\s+ufw"), "danach: OK, keine Frage mehr:\n" + t2);
    }

    [Test]
    public static async Task Einrichten_Umzug_HaeltAnSpieltEinStartet()
    {
        using var s = new Server();
        await s.EinrichtenAsync(HeimAntworten);
        await s.EinrichtenAsync("", new Einrichtauftrag(Profil: s.ProfilSchreiben(2, "funk.example.org", 8443)));
        Pruefe.Wahr(s.System.Aktiv("funk-server"), "läuft vor dem Umzug");

        var konten = Encoding.UTF8.GetBytes("{\"konten\": \"vom alten Server\"}");
        var dateien = new List<Umzugsdatei> { new("konten.json", konten), new("lage.json", "{}"u8.ToArray()), new(Umzugspaket.TotpName, new byte[32]) };
        var passphrase = Umzugspaket.PassphraseErzeugen();
        var paket = s.O["umzug.tru"];
        File.WriteAllBytes(paket, Umzugspaket.Verschluesseln(Umzugspaket.Packen(dateien, s.Jetzt, "0.2.0"), passphrase));

        // Nein zum Anhalten: nichts geschieht.
        var vorBefehle = s.System.Befehle.Count;
        var (code, text) = await s.EinrichtenAsync("n\n", new Einrichtauftrag(Umzug: paket));
        Pruefe.Gleich(1, code, text);
        Pruefe.Falsch(s.System.Befehle.Skip(vorBefehle).Any(b => b.StartsWith("systemctl stop", StringComparison.Ordinal)), "nicht angehalten");
        Pruefe.Falsch(File.Exists(Path.Combine(s.Orte.Zustand, "konten.json")), "nichts eingespielt");

        // Falsche Passphrase: der alte Stand bleibt, der Dienst läuft wieder.
        (code, text) = await s.EinrichtenAsync("j\n" + string.Concat(Enumerable.Repeat("AAAA-AAAA-AAAA-AAAA-AAAA-AAAA-AA\n", 3)), new Einrichtauftrag(Umzug: paket));
        Pruefe.Gleich(1, code, text);
        Pruefe.Wahr(s.System.Aktiv("funk-server"), "nach falscher Passphrase läuft er wieder:\n" + text);
        Pruefe.Falsch(File.Exists(Path.Combine(s.Orte.Zustand, "konten.json")), "nichts eingespielt");

        // Richtig: angehalten, eingespielt, gestartet, Abschluss mit den Prüfbefehlen.
        vorBefehle = s.System.Befehle.Count;
        (code, text) = await s.EinrichtenAsync("j\n" + Umzugspaket.PassphraseAnzeigen(passphrase) + "\n", new Einrichtauftrag(Umzug: paket));
        Pruefe.Gleich(0, code, text);
        Pruefe.BytesGleich(konten, File.ReadAllBytes(Path.Combine(s.Orte.Zustand, "konten.json")), "konten.json eingespielt");
        Pruefe.BytesGleich(new byte[32], File.ReadAllBytes(Path.Combine(s.Orte.Credentials, "funk-totp-schluessel")), "TOTP-Schlüssel aus dem Paket");
        var neu = s.System.Befehle.Skip(vorBefehle).ToList();
        var stop = neu.IndexOf("systemctl stop funk-server");
        var start = neu.FindIndex(b => b is "systemctl enable --now funk-server" or "systemctl start funk-server");
        Pruefe.Wahr(stop >= 0 && start > stop, "erst anhalten, dann starten:\n" + string.Join("\n", neu));
        Pruefe.Wahr(text.Contains("Eingespielt", StringComparison.Ordinal) && text.Contains("konten", StringComparison.Ordinal)
            && text.Contains("admins", StringComparison.Ordinal) && text.Contains("geraete", StringComparison.Ordinal), "Abschluss mit den Prüfbefehlen:\n" + text);
    }

    [Test]
    public static async Task Einrichten_VerweigertOhneRootUndBeiAbbruch_NichtsGeaendert()
    {
        using var s = new Server();
        s.System.Root = false;
        var (code, text) = await s.EinrichtenAsync(HeimAntworten);
        Pruefe.Gleich(1, code, text);
        Pruefe.Wahr(Zeile(text, "^FEHLT.*root"), text);
        Pruefe.Falsch(Directory.Exists(s.Orte.Etc), "nichts angelegt");

        s.System.Root = true;
        (code, text) = await s.EinrichtenAsync("funk.example.org\n\n");
        Pruefe.Gleich(1, code, "Eingabe endet mitten in den Fragen:\n" + text);
        Pruefe.Wahr(text.Contains("Nichts geändert", StringComparison.Ordinal), text);
        Pruefe.Falsch(Directory.Exists(s.Orte.Etc) || s.System.Befehle.Any(b => b.StartsWith("useradd", StringComparison.Ordinal)), "nichts angelegt");

        // Ohne Units im Paket (falscher Ordner): sofort Schluss.
        File.Delete(Path.Combine(s.Orte.Paketkonfig, "funk-server.service"));
        (code, text) = await s.EinrichtenAsync(HeimAntworten);
        Pruefe.Gleich(1, code, text);
        Pruefe.Wahr(Zeile(text, "^FEHLT.*funk-server.service"), text);
    }
}

/// <summary>
/// Attrappe eines Debian-Servers für <see cref="Einrichtung"/>: zeichnet jeden Programmaufruf auf und antwortet wie getent,
/// useradd, systemctl, journalctl, ss, ip, systemd-detect-virt und ufw. Dienste haben einen Zustand; ein gesetzter
/// <see cref="Startfehler"/> lässt den Start scheitern wie systemd (Type=simple: systemctl meldet Erfolg, der Dienst fällt danach).
/// </summary>
internal sealed class LinuxAttrappe : Systemblick
{
    public bool Root { get; set; } = true;
    public HashSet<string> Benutzer { get; } = new(StringComparer.Ordinal) { "root" };
    public Dictionary<string, string> Dienste { get; } = new(StringComparer.Ordinal);
    public List<string> Befehle { get; } = [];
    public Dictionary<string, UnixFileMode> Modi { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Pfad, string Besitzer)> Besitzer { get; } = [];
    public HashSet<(int Port, ProtocolType Art)> Belegt { get; } = [];
    public Dictionary<string, IPAddress[]> Dns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Uhr { get; set; } = "yes";
    public string Container { get; set; } = "none";
    public string? Ufw { get; set; }
    public string Adresse { get; set; } = "192.168.1.250";
    public string Journal { get; set; } = "Funk-Server 0.2.0 bereit: 1 Adresse(n), TCP Bindport 8443 (öffentlich 8443), UDP Bindport 47443 (öffentlich 47443), TLS 1.3/1.2, Pin AAAA-BBBB-CCCC-DDDD\n";
    public string? Startfehler { get; set; }
    public Func<string, string>? Ss { get; set; }
    public int Wartezeiten { get; private set; }

    public bool Aktiv(string dienst) => Dienste.TryGetValue(dienst, out var z) && z == "active";

    public override bool IstLinux => true;
    public override bool IstRoot => Root;

    public override UnixFileMode? Modus(string pfad) =>
        Modi.TryGetValue(Path.GetFullPath(pfad), out var m) ? m : Directory.Exists(pfad) ? (UnixFileMode)0b111_000_000 : File.Exists(pfad) ? (UnixFileMode)0b110_000_000 : null;

    public override void ModusSetzen(string pfad, UnixFileMode modus) => Modi[Path.GetFullPath(pfad)] = modus;
    public override void BesitzerSetzen(string pfad, string besitzer) => Besitzer.Add((pfad, besitzer));
    public override string? ZeitSynchron() => Uhr;
    public override void Warten(TimeSpan dauer) => Wartezeiten++;

    public override IPAddress[] Aufloesen(string host, AddressFamily familie) =>
        Dns.TryGetValue(host, out var a) ? a : throw new SocketException((int)SocketError.HostNotFound);

    public override bool DienstLaeuft(string socketPfad) => Aktiv("funk-server");

    public override SocketError? PortPruefen(IPAddress adresse, int port, ProtocolType art)
    {
        if (!adresse.Equals(IPAddress.Parse(Adresse))) return SocketError.AddressNotAvailable;
        return port != 0 && Belegt.Contains((port, art)) ? SocketError.AddressAlreadyInUse : null;
    }

    public override Prozessergebnis Ausfuehren(string programm, params string[] argumente)
    {
        Befehle.Add(string.Join(" ", [programm, .. argumente]));
        switch (programm)
        {
            case "getent":
                return Benutzer.Contains(argumente[^1]) ? new(0, argumente[^1] + ":x:998:998::/nonexistent:/usr/sbin/nologin\n") : new(2, "");
            case "useradd":
                Benutzer.Add(argumente[^1]);
                return new(0, "");
            case "hostname":
                return new(0, "debian\n");
            case "ip":
                return new(0, "1.1.1.1 via 192.168.1.1 dev eth0 src " + Adresse + " uid 0 \n    cache \n");
            case "systemd-detect-virt":
                return new(Container == "none" ? 1 : 0, Container + "\n");
            case "systemctl":
                return Systemctl(argumente);
            case "journalctl":
                return new(0, Journal);
            case "ss":
                if (argumente.Contains("sport"))
                {
                    var port = int.Parse(argumente[^1].TrimStart(':'), CultureInfo.InvariantCulture);
                    return Belegt.Any(b => b.Port == port)
                        ? new(0, "LISTEN 0 511 0.0.0.0:" + port.ToString(CultureInfo.InvariantCulture) + " 0.0.0.0:* users:((\"nginx\",pid=812,fd=6))\n")
                        : new(0, "");
                }
                return new(0, Ss?.Invoke(argumente.Any(a => a.Contains('t', StringComparison.Ordinal) && a.StartsWith('-')) ? "t" : "u") ?? "");
            case "ufw":
                if (Ufw is null) return new(-1, "ufw ließ sich nicht starten");
                if (argumente[0] == "allow")
                {
                    Ufw += argumente[1].PadRight(27) + "ALLOW       Anywhere\n";
                    return new(0, "Rule added\n");
                }
                return new(0, Ufw);
            default:
                return new(-1, programm + " ließ sich nicht starten (Attrappe)");
        }
    }

    private Prozessergebnis Systemctl(string[] a)
    {
        var dienst = a[^1];
        switch (a[0])
        {
            case "daemon-reload":
                return new(0, "");
            case "is-active":
                return Aktiv(dienst) ? new(0, "active\n") : new(3, (Dienste.GetValueOrDefault(dienst) ?? "inactive") + "\n");
            case "enable" or "start" or "restart":
                Dienste[dienst] = dienst == "funk-server" && Startfehler is not null ? "failed" : "active";
                return new(0, "");
            case "stop":
                Dienste[dienst] = "inactive";
                return new(0, "");
            case "show":
                return Startfehler is null || dienst != "funk-server"
                    ? new(0, "ActiveState=" + (Aktiv(dienst) ? "active" : "inactive") + "\nSubState=running\nResult=success\nExecMainStatus=0\nNRestarts=0\n")
                    : new(0, "ActiveState=failed\nSubState=failed\nResult=exit-code\nExecMainStatus=" + Startfehler + "\nNRestarts=0\n");
            default:
                return new(1, "Unknown command verb " + a[0]);
        }
    }
}
