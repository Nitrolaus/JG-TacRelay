// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Funk.Admin;
using Funk.Protokoll.Identitaet;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using Funk.Server.Verwaltung;

// funk-admin: Admin-Werkzeug (3.8). Offline-Befehle arbeiten mit Dateien, alle übrigen reden über den UNIX-Socket
// mit dem laufenden Server. Das Werkzeug ersetzt vorläufig den Bot-Dienst (verknuepfen, mitgliedschaft).
// Exitcodes: 0 gut, 1 Fehler, 2 Aufruf falsch (nichts geschehen), 3 Datei gibt es schon (nichts geschehen),
// 4 einrichten wartet auf das Profil von Sarevoc.

// Umlaute auch auf der Windows-Konsole (offline-Befehle wie profil-pruefen laufen am Windows-PC); unter Linux ohnehin UTF-8.
Console.OutputEncoding = Encoding.UTF8;

// Erst prüfen, ob jede Option zum Befehl passt (Befehlsoptionen); vorher entsteht keine Datei und der Server hört nichts.
var z = new Befehlszeile(args, Befehlsoptionen.Schalter);
if (Befehlsoptionen.Pruefen(z) is { } falsch)
{
    Console.Error.WriteLine(falsch);
    return 2;
}

try
{
    switch (z.Befehl)
    {
        case "hilfe" or "--hilfe" or "-h":
            Console.WriteLine(Hilfe.Text);
            return 0;

        // ---- offline ----
        case "schluessel-erzeugen":
        {
            var pin = ServerIdentitaet.Erzeugen(z.Pflicht("zertifikat"), z.Pflicht("schluessel"), z.Wert("name") ?? "Funk-Server", z.Schalter("ersetzen"));
            Console.WriteLine("Serverschlüssel erzeugt. Der Schlüssel gehört nach $CREDENTIALS_DIRECTORY, nie ins Repository.");
            Console.WriteLine("Pin (für pins im Serverprofil): " + pin.AlsBase64());
            Console.WriteLine("Anzeigeform: " + pin.Anzeige);
            return 0;
        }
        case "geheimnis-erzeugen":
            Geheimnis.Erzeugen(z.Pflicht("datei"), z.Schalter("ersetzen"));
            Console.WriteLine("Betriebsgeheimnis (32 Zufallsbytes) geschrieben; nur für den Dienstbenutzer lesbar halten.");
            return 0;
        case "totp-schluessel-erzeugen":
            TotpSchluessel.Erzeugen(z.Pflicht("datei"), z.Schalter("ersetzen"));
            Console.WriteLine("funk-totp-schluessel (32 Zufallsbytes) geschrieben. Per LoadCredential einbinden, nie in eine Sicherung.");
            return 0;
        case "profilschluessel-erzeugen":
        {
            // Der Profilschlüssel liegt nie auf dem Server (3.1); dieser Befehl gehört an den Offline-Rechner.
            using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pem = Encoding.ASCII.GetBytes(k.ExportPkcs8PrivateKeyPem());
            try
            {
                Dateiablage.AtomarSchreiben(z.Pflicht("schluessel"), pem, nurBesitzer: true, ersetzen: z.Schalter("ersetzen"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pem);
            }
            var spki = k.ExportSubjectPublicKeyInfo();
            Console.WriteLine("Profilschlüssel geschrieben. Öffentlicher Teil (SPKI, Base64) zum Einbau in den Client:");
            Console.WriteLine(Convert.ToBase64String(spki));
            Console.WriteLine("Fingerabdruck: " + Fingerabdruck.AusSpki(spki).Anzeige);
            return 0;
        }
        case "profil-schreiben":
        {
            var hosts = z.Alle("host").Select(h => Hostangabe.Lesen(h)).ToList();
            var pins = z.Alle("pin").Select(p => Fingerabdruck.TryAusBase64(p, out var f) ? f : throw new ArgumentException("--pin ist kein Base64-Fingerabdruck: " + p)).ToList();
            var jetzt = DateTimeOffset.UtcNow;
            var tage = z.Zahl("gueltig-tage") ?? 365;
            var tcpPort = z.Zahl("tcp-port") ?? throw new ArgumentException("--tcp-port fehlt.");
            if (tcpPort is 0 or > ushort.MaxValue) throw new ArgumentException("--tcp-port muss zwischen 1 und 65535 liegen: " + tcpPort.ToString(CultureInfo.InvariantCulture));
            var profil = new Serverprofil(z.Pflicht("name"), z.Zahl("version") ?? throw new ArgumentException("--version fehlt."),
                jetzt.ToUnixTimeMilliseconds(), jetzt.AddDays(tage).ToUnixTimeMilliseconds(), hosts,
                (ushort)tcpPort, pins, [.. z.Alle("nameserver")]);
            var daten = profil.AlsJson();
            if (!Serverprofil.TryLesen(daten, out _)) throw new ArgumentException("Profil verletzt 3.1 (Hosts 1-4, Pins 2-3 und verschieden, Port, Namen).");
            Dateiablage.AtomarSchreiben(z.Pflicht("aus"), daten, nurBesitzer: false);
            Console.WriteLine("Profildaten geschrieben (" + daten.Length.ToString(CultureInfo.InvariantCulture) + " Byte). Jetzt offline signieren: profil-signieren.");
            return 0;
        }
        case "profil-signieren":
        {
            var daten = File.ReadAllBytes(z.Pflicht("daten"));
            if (!Serverprofil.TryLesen(daten, out var inhalt)) throw new ArgumentException("--daten ist kein gültiges Serverprofil (3.1).");
            using var k = ECDsa.Create();
            k.ImportFromPem(File.ReadAllText(z.Pflicht("schluessel")));
            var signiert = SigniertesProfil.Signieren(daten, k);
            Dateiablage.AtomarSchreiben(z.Pflicht("aus"), signiert.AlsJson(), nurBesitzer: false);
            Console.WriteLine("Signiertes Profil Version " + inhalt.ProfilVersion.ToString(CultureInfo.InvariantCulture) + " geschrieben.");
            return 0;
        }
        case "profil-pruefen":
        {
            if (!SigniertesProfil.TryLesen(File.ReadAllBytes(z.Pflicht("profil")), out var angebot))
            {
                Console.Error.WriteLine("Kein gültiges {daten, signatur}.");
                return 1;
            }
            var schluessel = z.Alle("spki").Select(s => Profilschluessel.AusSpki(Convert.FromBase64String(s))).ToList();
            try
            {
                var p = ProfilAnnahme.Pruefen(angebot, schluessel, null, DateTimeOffset.UtcNow);
                Console.WriteLine("Urteil: " + p.Urteil + (p.Abgelaufen ? " (abgelaufen)" : ""));
                if (p.Profil is { } inhalt)
                {
                    Console.WriteLine("Name " + inhalt.Name + ", Version " + inhalt.ProfilVersion.ToString(CultureInfo.InvariantCulture) + ", TCP " + inhalt.TcpPort.ToString(CultureInfo.InvariantCulture));
                    foreach (var h in inhalt.Hosts) Console.WriteLine("  Host " + h.Name + (h.IPv4 ? " IPv4" : "") + (h.IPv6 ? " IPv6" : ""));
                    foreach (var pin in inhalt.Pins) Console.WriteLine("  Pin " + pin.AlsBase64() + " (" + pin.Anzeige + ")");
                }
                return p.Urteil == ProfilUrteil.Uebernehmen ? 0 : 1;
            }
            finally
            {
                foreach (var s in schluessel) s.Dispose();
            }
        }
        case "konfig-schreiben":
        {
            var (pfad, hinweise) = Konfigschreiber.Schreiben(new Konfigangaben(z.Pflicht("aus"), z.Alle("binden"), z.Zahl("tcp-port"), z.Zahl("oeffentlicher-tcp-port"),
                z.Zahl("udp-port"), z.Zahl("oeffentlicher-udp-port"), z.Wert("profil"), z.Wert("zustand")), DateTimeOffset.UtcNow);
            Console.WriteLine("server.json geschrieben: " + pfad);
            foreach (var h in hinweise) Console.WriteLine("HINWEIS " + h);
            Console.WriteLine("Prüfen: funk-admin pruefen --vollstaendig --konfig " + pfad);
            return 0;
        }
        case "pruefen":
        {
            var befunde = Serverpruefung.Pruefen(KonfigPfad(z), z.Schalter("vollstaendig"), z.Wert("credentials"), z.Alle("spki"), z.Wert("host"), Systemblick.Echt);
            foreach (var b in befunde) Console.WriteLine(b.ToString());
            var fehler = befunde.Count(b => b.Stufe == Befundstufe.Fehler);
            var warnungen = befunde.Count(b => b.Stufe == Befundstufe.Warnung);
            Console.WriteLine("Ergebnis: " + fehler.ToString(CultureInfo.InvariantCulture) + " Fehler, "
                + warnungen.ToString(CultureInfo.InvariantCulture) + (warnungen == 1 ? " Warnung." : " Warnungen."));
            return fehler > 0 ? 1 : 0;
        }
        case "profil-anfrage":
        {
            var hosts = z.Alle("host");
            if (hosts.Count != 1)
                throw new ArgumentException(hosts.Count == 0 ? "--host fehlt: der Name, unter dem der Server erreichbar sein soll, etwa --host funk.example.org."
                    : "Eine Anfrage nennt genau einen Host; --host steht hier " + hosts.Count.ToString(CultureInfo.InvariantCulture) + "-mal.");
            var host = Hostangabe.Lesen(hosts[0]);
            var konfig = KonfigOderNull(z);
            var orte = konfig is null ? null : new Schluesselorte(konfig, z.Wert("credentials"));
            var port = z.Zahl("tcp-port") ?? (konfig is null ? (uint?)null : (uint)konfig.TcpPortOeffentlich)
                ?? throw new ArgumentException("--tcp-port fehlt: der Port, den die Clients erreichen (oder --konfig <server.json>, dann gilt oeffentlicherTcpPort).");
            if (port is 0 or > ushort.MaxValue) throw new ArgumentException("--tcp-port muss zwischen 1 und 65535 liegen: " + port.ToString(CultureInfo.InvariantCulture));
            var zertifikat = z.Wert("zertifikat") ?? orte?.Zertifikat ?? throw new ArgumentException("--zertifikat <server.crt> fehlt (oder --konfig <server.json>).");
            using var zert = X509Certificate2.CreateFromPem(File.ReadAllText(zertifikat));
            var anfrage = new Profilanfrage(host, (ushort)port, Fingerabdruck.AusZertifikat(zert));
            Console.WriteLine(anfrage.Zeile);
            Console.WriteLine("Kennung zum Vorlesen: " + anfrage.Kennung);
            Console.WriteLine("Schick dem Eigentümer die Zeile oben, egal auf welchem Weg; sie ist nicht geheim. Die Kennung, den Namen " + host.Name + " und den Port "
                + port.ToString(CultureInfo.InvariantCulture) + " liest du ihm am Telefon vor,");
            Console.WriteLine("damit er sicher ist, dass die Zeile von dir kommt und unterwegs nicht verändert wurde.");
            if (z.Wert("aus") is { } aus)
            {
                Dateiablage.AtomarSchreiben(aus, Encoding.UTF8.GetBytes(anfrage.Zeile + "\n"), nurBesitzer: false);
                Console.WriteLine("Auch geschrieben nach " + Path.GetFullPath(aus) + " (für den Stick: neben profil.json als " + Profilerneuerung.AnfrageDatei + ").");
            }
            return 0;
        }
        case "profil-erneuern":
            return new Profilerneuerung(Console.In, Console.Out).Ausfuehren(z.Pflicht("bisher"), z.Pflicht("schluessel"), z.Wert("anfrage"), z.Wert("reserve"),
                z.Zahl("gueltig-tage") ?? 365);
        case "umzug-packen":
            return new Umzug(Console.In, Console.Out, Systemblick.Echt).Packen(KonfigLaden(z), z.Wert("credentials"), z.Pflicht("aus"), z.Schalter("ohne-totp"),
                z.Schalter("mit-serverschluessel"), DateTimeOffset.UtcNow);
        case "umzug-einspielen":
            return new Umzug(Console.In, Console.Out, Systemblick.Echt).Einspielen(KonfigLaden(z), z.Wert("credentials"), z.Pflicht("paket"),
                z.Schalter("mit-serverschluessel"), z.Wert("besitzer") ?? "funk", DateTimeOffset.UtcNow);
        case "erster-admin":
        {
            var socket = SocketPfad(z);
            return await new ErsterAdmin(Console.In, Console.Out, (b, a) => Verwaltungsklient.SendenAsync(socket, b, a, TimeSpan.FromSeconds(30))).AusfuehrenAsync().ConfigureAwait(false);
        }
        case "einrichten":
            // Aus funk-einrichten.sh, als root auf dem Server; die Pfade stehen fest (Einrichtorte.Linux), nicht in Optionen.
            return await new Einrichtung(Console.In, Console.Out, Systemblick.Echt, Einrichtorte.Linux,
                    (socket, b, a) => Verwaltungsklient.SendenAsync(socket, b, a, TimeSpan.FromSeconds(30)))
                .AusfuehrenAsync(new Einrichtauftrag(z.Wert("profil"), z.Wert("umzug"), z.Schalter("mit-serverschluessel"))).ConfigureAwait(false);
    }

    // ---- über den Server ----
    var (befehl, argumente) = Uebersetzung.Uebersetzen(z);
    var socketPfad = SocketPfad(z);
    var frist = befehl == "verknuepfen" ? TimeSpan.FromSeconds(140) : TimeSpan.FromSeconds(30);
    var antwort = await Verwaltungsklient.SendenAsync(socketPfad, befehl, argumente, frist).ConfigureAwait(false);
    if (z.Schalter("json-aus")) Console.WriteLine(new JsonObject { ["ok"] = antwort.Ok, ["text"] = antwort.Text, ["daten"] = antwort.Daten?.DeepClone() }.ToJsonString());
    else if (antwort.Ok) Console.WriteLine(antwort.Text);
    else Console.Error.WriteLine("Fehler: " + antwort.Text);
    return antwort.Ok ? 0 : 1;
}
catch (SchonDaException e)
{
    Console.Error.WriteLine(e.Message + " Nichts geändert." + Folge(z.Befehl));
    return 3;
}
catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or FormatException or CryptographicException
    or System.Net.Sockets.SocketException or OperationCanceledException or InvalidDataException)
{
    Console.Error.WriteLine(e.Message);
    return e is ArgumentException ? 2 : 1;
}

static string Folge(string befehl) => befehl switch
{
    "schluessel-erzeugen" => " Mit --ersetzen entsteht ein neuer Schlüssel mit neuem Pin; die Clients nehmen den Server dann erst an, wenn der neue Pin im Profil steht (profil-anfrage an den Eigentümer).",
    "geheimnis-erzeugen" => " Mit --ersetzen entsteht ein neues Betriebsgeheimnis; die Vorrangspur fängt dann leer an, und die Schwärzung im Logbuch passt nicht mehr zu älteren Zeilen.",
    "totp-schluessel-erzeugen" => " Mit --ersetzen entsteht ein neuer Schlüssel; jeder eingerichtete Zweitfaktor ist dann unlesbar, und jeder Admin richtet ihn an der Konsole neu ein (admin-einrichtung-neu).",
    "profilschluessel-erzeugen" => " Mit --ersetzen entsteht ein neuer Profilschlüssel; kein Client nimmt damit signierte Profile an, bis jeder ein Setup mit dem neuen Schlüssel hat.",
    "umzug-packen" => " Ein vorhandenes Paket bleibt; einen anderen Namen mit --aus wählen.",
    _ => "",
};

static string KonfigPfad(Befehlszeile z) => z.Wert("konfig") ?? Environment.GetEnvironmentVariable("FUNK_KONFIG")
    ?? throw new ArgumentException("--konfig <server.json> angeben (oder FUNK_KONFIG setzen).");

static ServerKonfiguration KonfigLaden(Befehlszeile z) => ServerKonfiguration.Laden(KonfigPfad(z));

static ServerKonfiguration? KonfigOderNull(Befehlszeile z) =>
    (z.Wert("konfig") ?? Environment.GetEnvironmentVariable("FUNK_KONFIG")) is { } pfad ? ServerKonfiguration.Laden(pfad) : null;

static string SocketPfad(Befehlszeile z)
{
    if (z.Wert("socket") is { } s) return s;
    var konfig = z.Wert("konfig") ?? Environment.GetEnvironmentVariable("FUNK_KONFIG")
        ?? throw new ArgumentException("--socket oder --konfig angeben (oder FUNK_KONFIG setzen).");
    return ServerKonfiguration.Laden(konfig).SocketPfad;
}
