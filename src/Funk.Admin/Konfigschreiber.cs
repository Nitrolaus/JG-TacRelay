// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Funk.Server.Betrieb;

namespace Funk.Admin;

/// <summary>Die Angaben für <c>funk-admin konfig-schreiben</c>; ohne Angabe gelten die Werte aus BETRIEB.md.</summary>
public sealed record Konfigangaben(string Aus, IReadOnlyList<string> Binden, uint? TcpPort = null, uint? OeffentlicherTcpPort = null,
    uint? UdpPort = null, uint? OeffentlicherUdpPort = null, string? Profil = null, string? Zustand = null);

/// <summary>
/// <c>funk-admin konfig-schreiben</c> (EINRICHTEN-UMZUG-ENTWURF, Abschnitt 7 ohne nano): schreibt eine gültige server.json aus
/// wenigen Angaben. Schlüssel, Geheimnis und TOTP-Schlüssel stehen darin nur als <c>${CREDENTIALS_DIRECTORY}/...</c> wie in
/// funk-server.service. Geschrieben wird erst, wenn der Server selbst die Datei annähme (<see cref="ServerKonfiguration.Pruefen"/>),
/// und nie über eine vorhandene: Die bleibt und wird nur mit <c>funk-admin pruefen</c> angesehen.
/// </summary>
public static class Konfigschreiber
{
    public const ushort StandardTcpPort = 8443;
    public const ushort StandardUdpPort = 47443;
    public const string StandardZustand = "/var/lib/funk";
    public const string StandardProfil = "/etc/funk/profil.json";
    public const string StandardSocket = "/run/funk/verwaltung.sock";

    private const string Nichts = " Nichts geschrieben.";

    /// <summary>Schreibt die Datei und liefert Pfad und Hinweise (Profil fehlt noch, Portabgleich).</summary>
    public static (string Pfad, IReadOnlyList<string> Hinweise) Schreiben(Konfigangaben a, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(a);
        var ziel = Path.GetFullPath(a.Aus);
        if (File.Exists(ziel))
            throw new SchonDaException(ziel + " gibt es schon. konfig-schreiben schreibt nie über eine vorhandene server.json; ansehen mit funk-admin pruefen --konfig " + ziel + ".", new IOException(ziel));
        var adressen = Adressen(a.Binden);
        var tcp = Port(a.TcpPort, StandardTcpPort, "--tcp-port");
        var udp = Port(a.UdpPort, StandardUdpPort, "--udp-port");
        int? tcpAussen = a.OeffentlicherTcpPort is { } ot ? Port(ot, 0, "--oeffentlicher-tcp-port") : null;
        int? udpAussen = a.OeffentlicherUdpPort is { } ou ? Port(ou, 0, "--oeffentlicher-udp-port") : null;

        var bytes = Json(adressen, tcp, tcpAussen, udp, udpAussen, a.Zustand ?? StandardZustand, a.Profil ?? StandardProfil, jetzt);
        var basis = Path.GetDirectoryName(ziel) ?? Environment.CurrentDirectory;
        var k = ServerKonfiguration.Lesen(bytes, basis);
        var fehler = k.Pruefen();
        if (fehler.Count > 0) throw new ArgumentException("Die Angaben ergeben keine gültige server.json: " + string.Join(" ", fehler) + Nichts);
        Dateiablage.NeuSchreiben(ziel, bytes, nurBesitzer: false);

        var hinweise = new List<string>();
        var profil = k.Aufloesen(k.Profil!);
        if (!File.Exists(profil))
            hinweise.Add("Das Serverprofil fehlt noch (" + profil + "). Der Server startet erst mit ihm: funk-admin profil-anfrage, die Zeile an den Eigentümer, sein profil.json nach " + profil + ".");
        else
        {
            var (profilFehler, warnungen) = Profilabgleich.Pruefen(k);
            if (profilFehler is not null) hinweise.Add("Serverprofil: " + profilFehler);
            hinweise.AddRange(warnungen);
        }
        return (ziel, hinweise);
    }

    private static List<IPAddress> Adressen(IReadOnlyList<string> binden)
    {
        if (binden.Count == 0)
            throw new ArgumentException("--binden fehlt: die IP-Adresse, an die der Server bindet, etwa --binden 192.168.1.250 (die feste Adresse des Containers)." + Nichts);
        var liste = new List<IPAddress>();
        foreach (var b in binden)
        {
            if (!IPAddress.TryParse(b, out var ip) || (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && b.Count(c => c == '.') != 3))
                throw new ArgumentException("--binden " + b + " ist keine IP-Adresse. Gemeint ist die Adresse dieses Servers, etwa 192.168.1.250." + Nichts);
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
                throw new ArgumentException("--binden " + b + " hieße alle Adressen; das schreibt konfig-schreiben nicht (4.1). Nimm die Adresse dieses Servers." + Nichts);
            if (liste.Contains(ip)) throw new ArgumentException("--binden " + b + " steht doppelt da." + Nichts);
            liste.Add(ip);
        }
        return liste;
    }

    private static int Port(uint? wert, ushort standard, string option)
    {
        var p = wert ?? standard;
        if (p is 0 or > ushort.MaxValue)
            throw new ArgumentException(option + " muss zwischen 1 und 65535 liegen: " + p.ToString(CultureInfo.InvariantCulture) + "." + Nichts);
        return (int)p;
    }

    private static byte[] Json(List<IPAddress> adressen, int tcp, int? tcpAussen, int udp, int? udpAussen, string zustand, string profil, DateTimeOffset jetzt)
    {
        using var puffer = new MemoryStream();
        var kopf = "// Geschrieben von funk-admin konfig-schreiben am " + jetzt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC.\n"
            + "// Felder: BETRIEB.md, Abschnitt Konfiguration. Keine Geheimnisse: Schlüssel, Betriebsgeheimnis und TOTP-Schlüssel\n"
            + "// kommen per LoadCredential aus /etc/funk/credentials (funk-server.service).\n";
        puffer.Write(Encoding.UTF8.GetBytes(kopf));
        using (var w = new Utf8JsonWriter(puffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteStartArray("binden");
            foreach (var ip in adressen) w.WriteStringValue(ip.ToString());
            w.WriteEndArray();
            w.WriteBoolean("alleAdressen", false);
            w.WriteNumber("tcpPort", tcp);
            if (tcpAussen is { } ta) w.WriteNumber("oeffentlicherTcpPort", ta);
            w.WriteNumber("udpPort", udp);
            if (udpAussen is { } ua) w.WriteNumber("oeffentlicherUdpPort", ua);
            w.WriteBoolean("nurTls13", false);
            w.WriteString("zustandsverzeichnis", zustand);
            w.WriteStartObject("identitaet");
            w.WriteString("zertifikat", "${CREDENTIALS_DIRECTORY}/server.crt");
            w.WriteString("schluessel", "${CREDENTIALS_DIRECTORY}/server.key");
            w.WriteEndObject();
            w.WriteString("geheimnis", "${CREDENTIALS_DIRECTORY}/funk-geheimnis");
            w.WriteString("totpSchluessel", "${CREDENTIALS_DIRECTORY}/funk-totp-schluessel");
            w.WriteString("profil", profil);
            w.WriteString("mindestClientVersion", new ServerKonfiguration().MindestClientVersion);
            w.WriteStartObject("mitgliedskarte");
            w.WriteBoolean("rollenFuerAlle", true);
            w.WriteEndObject();
            w.WriteString("verwaltungsSocket", StandardSocket);
            w.WriteStartObject("log");
            w.WriteString("stufe", "INFO");
            w.WriteEndObject();
            w.WriteEndObject();
        }
        puffer.WriteByte((byte)'\n');
        return puffer.ToArray();
    }
}
