// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Funk.Protokoll.Identitaet;
using Funk.Server.Betrieb;
using Funk.Server.Kern;

namespace Funk.Admin;

/// <summary>
/// <c>funk-admin einrichten</c> (A55, EINRICHTEN-UMZUG-ENTWURF "Vorschlag Einrichtung"), aufgerufen von funk-einrichten.sh,
/// nachdem das Skript .NET, libicu und die Programme nach /opt/funk gebracht hat. Beliebig oft: Beim ersten Lauf (keine
/// server.json) fragt es fünfmal mit Vorschlag, danach ändert es nur, was fehlt oder vom Paket abweicht, und meldet jeden
/// Schritt mit OK, NEU, BLEIBT oder FEHLT (mit Abhilfe in einem Satz). Reihenfolge: Benutzer, Ordner, Schlüssel (nie ersetzt),
/// server.json (nie überschrieben, nur geprüft), Profilschlüssel, Units (abweichende als .alt daneben), Drop-in für Ports unter
/// 1024, echo.env, Kurzbefehl, Uhr, Firewall (nur Anzeige, ufw nur nach ja), Umzugspaket, Profil, Start mit Blick ins
/// Journal, Abschluss. Ohne passendes Profil endet es mit <see cref="WartetAufProfil"/> und der Anfragezeile für Sarevoc.
/// Fremdes fasst es nicht an: einen Kurzbefehl oder ein Drop-in ohne <see cref="Marke"/>, belegte Ports, andere Units.
/// </summary>
public sealed class Einrichtung(TextReader ein, TextWriter aus, Systemblick system, Einrichtorte orte,
    Func<string, string, JsonObject, Task<VerwaltungsAntwort>> senden, Func<DateTimeOffset>? uhr = null)
{
    /// <summary>Exitcode, solange das Profil von Sarevoc fehlt.</summary>
    public const int WartetAufProfil = 4;

    /// <summary>Steht in jeder Datei, die einrichten selbst schreibt; nur solche ersetzt es (Kurzbefehl, Drop-in).</summary>
    public const string Marke = "Angelegt von funk-admin einrichten";

    private const UnixFileMode Geheim = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode GeheimOrdner = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode Lesbar = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode Offen = GeheimOrdner | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode Fremd = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private static readonly TimeSpan Frist = TimeSpan.FromSeconds(1);
    private static readonly string[] Schluesseldateien = ["server.crt", "server.key", "funk-geheimnis", "funk-totp-schluessel"];

    private readonly Dialog _d = new(ein, aus);
    private readonly Func<DateTimeOffset> _uhr = uhr ?? (() => DateTimeOffset.UtcNow);
    private bool _neustart;
    private bool _unitsNeu;
    private bool _neuGeladen;
    private bool _echoNeu;
    private bool _profilNeu;
    private bool _kurzbefehl = true;
    private bool _umzug;
    private bool _echoOffen;

    private sealed record Antworten(ProfilHost Host, IPAddress Binden, bool Router, int Tcp, int TcpAussen, int Udp, int UdpAussen, bool Echo);

    private enum Profilstand
    {
        Bereit,
        Wartet,
        Fehler,
    }

    public async Task<int> AusfuehrenAsync(Einrichtauftrag auftrag)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        if (!system.IstLinux) return Schluss("einrichten läuft nur auf dem Linux-Server.", "dort im entpackten Ordner linux des Serverpakets: sudo bash funk-einrichten.sh.");
        if (!system.IstRoot) return Schluss("Nicht als root; einrichten legt Benutzer, Schlüssel und Units an.", "sudo bash funk-einrichten.sh.");
        foreach (var d in new[] { "funk-server.service", "funk-echo.service", "profilschluessel.txt" })
        {
            if (!File.Exists(Paket(d)))
                return Schluss(Paket(d) + " fehlt; das ist nicht das ganze Serverpaket.", "funk-einrichten.sh aus dem entpackten Ordner linux aufrufen; es legt alles nach " + orte.Programme + ".");
        }
        if (auftrag.Profil is { } p && !File.Exists(p))
            return Schluss("Das Profil " + Path.GetFullPath(p) + " gibt es nicht. Nichts geändert.", "profil.json vom Eigentümer in den Ordner legen, in dem du aufrufst, und den Namen genau so angeben.");
        if (auftrag.Umzug is { } u && !File.Exists(u))
            return Schluss("Das Umzugspaket " + Path.GetFullPath(u) + " gibt es nicht. Nichts geändert.", "den Pfad zum Paket angeben, das umzug-packen geschrieben hat.");
        aus.WriteLine("funk-admin einrichten " + Umzug.Fassung + (auftrag.Umzug is null ? "" : ", mit Umzugspaket"));

        Antworten? a = null;
        if (!File.Exists(orte.Konfig))
        {
            a = Fragen();
            if (a is null)
            {
                aus.WriteLine("Abgebrochen. Nichts geändert.");
                return 1;
            }
        }
        var echo = a?.Echo ?? File.Exists(orte.EchoEnv);

        if (!Benutzer("funk") || (echo && !Benutzer("funk-echo"))) return 1;
        Ordner(orte.Etc, Offen, null);
        Ordner(orte.Credentials, GeheimOrdner, null);
        if (echo) Ordner(orte.EchoZustand, GeheimOrdner, "funk-echo");
        if (!Schluessel()) return 1;
        if (Konfiguration(a) is not { } k) return 1;
        Schreiben(orte.Profilschluessel, Lf(File.ReadAllBytes(Paket("profilschluessel.txt"))), "Profilschlüssel des Eigentümers (öffentlich)", Lesbar, nurEigene: false);
        Units(k, echo);
        Kurzbefehl();
        Uhr();
        Firewall(k, a);

        if (auftrag.Umzug is { } paket)
        {
            var c = UmzugEinspielen(k, paket, auftrag.MitServerschluessel);
            if (c != 0) return c;
        }
        switch (Profilschritt(k, auftrag.Profil, a?.Host))
        {
            case Profilstand.Wartet:
                return WartetAufProfil;
            case Profilstand.Fehler:
                if (_umzug) aus.WriteLine("       funk-server bleibt für den Umzug angehalten, bis das Profil passt.");
                return 1;
        }
        if (!await StartenAsync(k).ConfigureAwait(false)) return 1;
        Echo(echo);
        await AbschlussAsync(k).ConfigureAwait(false);
        return 0;
    }

    // ---- Ausgabe ----

    private void Melde(string art, string text) => aus.WriteLine(art.PadRight(7) + text);

    private void Ok(string text) => Melde("OK", text);

    private void Neu(string text) => Melde("NEU", text);

    private void Bleibt(string text) => Melde("BLEIBT", text);

    private void Fehlt(string was, string abhilfe) => Melde("FEHLT", was + " Abhilfe: " + abhilfe);

    private int Schluss(string was, string abhilfe)
    {
        Fehlt(was, abhilfe);
        return 1;
    }

    private static string Z(long zahl) => zahl.ToString(CultureInfo.InvariantCulture);

    private static string Oktal(UnixFileMode m) => "0" + Convert.ToString((int)m & 0x1FF, 8);

    private static string Kurz(string text)
    {
        var t = text.Trim().Replace('\n', ' ');
        return t.Length > 200 ? t[..200] + "..." : t;
    }

    private string Paket(string name) => Path.Combine(orte.Paketkonfig, name);

    private string Zeitstempel => _uhr().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    /// <summary>Zeilenenden für Linux; eine BOM fällt weg (systemd und sh lesen sie als Zeichen).</summary>
    private static byte[] Lf(byte[] roh)
    {
        var text = Encoding.UTF8.GetString(roh.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? roh.AsSpan(3) : roh);
        return Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    // ---- Fragen beim ersten Lauf ----

    private Antworten? Fragen()
    {
        aus.WriteLine();
        aus.WriteLine("Erster Lauf (" + orte.Konfig + " gibt es noch nicht). Die Eingabetaste nimmt den Vorschlag in eckigen Klammern.");
        ProfilHost? host = null;
        for (var i = 0; host is null; i++)
        {
            if (i == 5) return null;
            var e = Frage("Öffentlicher Name, unter dem die Clients den Server finden (etwa funk.example.org)", NameVorschlag());
            if (e is null) return null;
            if (e.Length == 0)
            {
                aus.WriteLine("       Ohne Namen geht es nicht; er kommt ins Profil und in den DNS.");
                continue;
            }
            try
            {
                host = Hostangabe.Lesen(e, "Der Name");
            }
            catch (ArgumentException x)
            {
                aus.WriteLine("       " + x.Message);
            }
        }
        DnsZeigen(host);

        IPAddress? binden = null;
        for (var i = 0; binden is null; i++)
        {
            if (i == 5) return null;
            var e = Frage("Adresse dieses Rechners, an die der Server bindet", Bindvorschlag());
            if (e is null) return null;
            if (!IPAddress.TryParse(e, out var ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)
                || (ip.AddressFamily == AddressFamily.InterNetwork && e.Count(c => c == '.') != 3))
            {
                aus.WriteLine("       " + e + " ist keine einzelne IP-Adresse (ip -4 addr zeigt die dieses Rechners).");
                continue;
            }
            if (system.PortPruefen(ip, 0, ProtocolType.Tcp) == SocketError.AddressNotAvailable)
            {
                aus.WriteLine("       Die Adresse " + ip + " gibt es auf diesem Rechner nicht (ip -4 addr zeigt die vorhandenen).");
                continue;
            }
            binden = ip;
        }

        if (JaNein("Sitzt ein Router mit Portfreigabe davor (FRITZ!Box, Heimnetz)?", IstPrivat(binden)) is not { } router) return null;
        aus.WriteLine("       Zieht ein bestehender Funk hierher um, nimm denselben TCP-Port wie der alte Server (etwa 8443):");
        aus.WriteLine("       Ein Profil kennt nur einen TCP-Port für alle Hosts (UMZUG.md).");
        var tcpVorschlag = router || system.PortPruefen(binden, 443, ProtocolType.Tcp) is not null ? Konfigschreiber.StandardTcpPort : 443;
        if (Port("TCP-Port am Server", tcpVorschlag, binden, ProtocolType.Tcp) is not { } tcp) return null;
        var tcpAussen = tcp;
        if (router)
        {
            if (Zahl("TCP-Port außen am Router", tcp) is not { } ta) return null;
            tcpAussen = ta;
        }
        if (Port("UDP-Port am Server", Konfigschreiber.StandardUdpPort, binden, ProtocolType.Udp) is not { } udp) return null;
        var udpAussen = udp;
        if (router)
        {
            if (Zahl("UDP-Port außen am Router", udp) is not { } ua) return null;
            udpAussen = ua;
        }
        if (JaNein("Echo-Funker einrichten (spricht im Kanal Echo nach, zum Prüfen)?", true) is not { } echo) return null;
        return new Antworten(host, binden, router, tcp, tcpAussen, udp, udpAussen, echo);
    }

    private string? Frage(string text, string vorschlag)
    {
        var e = _d.Zeile(text + (vorschlag.Length > 0 ? " [" + vorschlag + "]" : "") + ": ");
        if (e is null)
        {
            aus.WriteLine();
            return null;
        }
        e = e.Trim();
        return e.Length == 0 ? vorschlag : e;
    }

    private bool? JaNein(string text, bool vorschlag)
    {
        for (var i = 0; i < 5; i++)
        {
            var e = _d.Zeile(text + " (j/n) [" + (vorschlag ? "j" : "n") + "]: ");
            if (e is null)
            {
                aus.WriteLine();
                return null;
            }
            switch (e.Trim().ToLowerInvariant())
            {
                case "":
                    return vorschlag;
                case "j" or "ja":
                    return true;
                case "n" or "nein":
                    return false;
            }
            aus.WriteLine("       Bitte j oder n.");
        }
        return null;
    }

    private int? Zahl(string text, int vorschlag)
    {
        for (var i = 0; i < 5; i++)
        {
            var e = Frage(text, Z(vorschlag));
            if (e is null) return null;
            if (int.TryParse(e, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is >= 1 and <= 65535) return p;
            aus.WriteLine("       Ein Port ist eine Zahl von 1 bis 65535.");
        }
        return null;
    }

    private int? Port(string text, int vorschlag, IPAddress binden, ProtocolType art)
    {
        for (var i = 0; i < 5; i++)
        {
            if (Zahl(text, vorschlag) is not { } p) return null;
            var name = (art == ProtocolType.Tcp ? "TCP " : "UDP ") + Z(p) + " auf " + binden;
            switch (system.PortPruefen(binden, p, art))
            {
                case null:
                    return p;
                case SocketError.AddressAlreadyInUse:
                    aus.WriteLine("       " + name + " ist belegt" + Inhaber(p, art) + ". Nimm einen anderen; den fremden Dienst fasst einrichten nicht an.");
                    break;
                case { } grund:
                    aus.WriteLine("       " + name + " lässt sich nicht binden (" + grund + ").");
                    break;
            }
        }
        return null;
    }

    /// <summary>Wer einen Port hält, aus <c>ss -H -ltnp sport = :port</c>; leer, wenn es sich nicht sagen lässt.</summary>
    private string Inhaber(int port, ProtocolType art)
    {
        var r = system.Ausfuehren("ss", "-H", art == ProtocolType.Tcp ? "-ltnp" : "-lunp", "sport", "=", ":" + Z(port));
        var m = Regex.Match(r.Ausgabe, @"users:\(\(""([^""]+)""", RegexOptions.None, Frist);
        return m.Success ? " (" + m.Groups[1].Value + ")" : "";
    }

    private string NameVorschlag()
    {
        var r = system.Ausfuehren("hostname", "-f");
        var name = r.Code == 0 ? r.Ausgabe.Trim() : "";
        return name.Contains('.', StringComparison.Ordinal) && Serverprofil.HostnameGueltig(name) ? name : "";
    }

    private string Bindvorschlag()
    {
        var r = system.Ausfuehren("ip", "-4", "route", "get", "1.1.1.1");
        var m = Regex.Match(r.Ausgabe, @"\bsrc\s+(\d+\.\d+\.\d+\.\d+)", RegexOptions.None, Frist);
        return r.Code == 0 && m.Success ? m.Groups[1].Value : "";
    }

    private void DnsZeigen(ProfilHost host)
    {
        try
        {
            var adressen = system.Aufloesen(host.Name, host is { IPv6: true, IPv4: false } ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
            aus.WriteLine("       " + host.Name + (adressen.Length == 0 ? " hat im DNS noch keine Adresse." : " zeigt im DNS auf " + string.Join(", ", adressen.Select(x => x.ToString())) + "."));
        }
        catch (Exception e) when (e is SocketException or ArgumentException)
        {
            aus.WriteLine("       " + host.Name + " steht noch nicht im DNS; vor dem ersten Verbinden braucht er einen A-Eintrag auf die öffentliche Adresse.");
        }
    }

    /// <summary>Privat oder nur im Heimnetz erreichbar: dann sitzt meist ein Router mit Portfreigabe davor.</summary>
    private static bool IstPrivat(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || IPAddress.IsLoopback(ip);
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    // ---- Benutzer, Ordner, Schlüssel, server.json ----

    private bool Benutzer(string name)
    {
        if (system.Ausfuehren("getent", "passwd", name).Code == 0)
        {
            Ok("Benutzer " + name);
            return true;
        }
        var r = system.Ausfuehren("useradd", "--system", "--no-create-home", "--shell", "/usr/sbin/nologin", name);
        if (r.Code != 0)
        {
            Fehlt("Benutzer " + name + " ließ sich nicht anlegen (" + Kurz(r.Ausgabe) + ").", "useradd --system --no-create-home --shell /usr/sbin/nologin " + name + " von Hand; die Meldung sagt, was fehlt.");
            return false;
        }
        Neu("Benutzer " + name + " (Systembenutzer ohne Anmeldung)");
        return true;
    }

    private void Ordner(string pfad, UnixFileMode modus, string? besitzer)
    {
        if (!Directory.Exists(pfad))
        {
            Directory.CreateDirectory(pfad);
            system.ModusSetzen(pfad, modus);
            if (besitzer is not null) system.BesitzerSetzen(pfad, besitzer);
            Neu(pfad + " (" + Oktal(modus) + ", " + (besitzer ?? "root") + ")");
            return;
        }
        if (modus == GeheimOrdner && system.Modus(pfad) is { } m && (m & Fremd) != 0)
        {
            system.ModusSetzen(pfad, modus);
            Neu("Rechte von " + pfad + " auf " + Oktal(modus) + " (waren " + Oktal(m) + ")");
            return;
        }
        Ok(pfad);
    }

    private void Rechte(string pfad)
    {
        if (system.Modus(pfad) is not { } m || (m & Fremd) == 0) return;
        system.ModusSetzen(pfad, Geheim);
        Neu("Rechte von " + pfad + " auf 0600 (waren " + Oktal(m) + ")");
    }

    private bool Schluessel()
    {
        string P(string name) => Path.Combine(orte.Credentials, name);
        var crt = P("server.crt");
        var key = P("server.key");
        if (File.Exists(crt) != File.Exists(key))
        {
            Fehlt("In " + orte.Credentials + " liegt nur " + (File.Exists(crt) ? "server.crt" : "server.key") + ", das andere fehlt.",
                "die vorhandene Datei zur Seite legen (mv), dann noch einmal aufrufen; es entsteht ein neuer Serverschlüssel mit neuem Pin.");
            return false;
        }
        if (!File.Exists(crt))
        {
            var pin = ServerIdentitaet.Erzeugen(crt, key);
            Neu("Serverschlüssel und Zertifikat in " + orte.Credentials + ", Pin " + pin.Anzeige);
        }
        else
        {
            Rechte(key);
            try
            {
                Bleibt("Serverschlüssel in " + orte.Credentials + ", Pin " + ServerPin().Anzeige);
            }
            catch (Exception e) when (e is CryptographicException or IOException or ArgumentException)
            {
                Fehlt(crt + " ist kein lesbares Zertifikat (" + e.Message + ").", "server.crt und server.key zur Seite legen und noch einmal aufrufen; es entsteht ein neuer Pin.");
                return false;
            }
        }
        foreach (var (name, was, erzeugen) in new (string, string, Action<string, bool>)[]
        {
            ("funk-geheimnis", "Betriebsgeheimnis", Geheimnis.Erzeugen),
            ("funk-totp-schluessel", "TOTP-Schlüssel", TotpSchluessel.Erzeugen),
        })
        {
            if (File.Exists(P(name)))
            {
                Rechte(P(name));
                Bleibt(was + " " + P(name));
                continue;
            }
            erzeugen(P(name), false);
            Neu(was + " " + P(name));
        }
        return true;
    }

    private Fingerabdruck ServerPin()
    {
        using var z = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(orte.Credentials, "server.crt")));
        return Fingerabdruck.AusZertifikat(z);
    }

    private ServerKonfiguration? Konfiguration(Antworten? a)
    {
        if (a is not null)
        {
            Konfigschreiber.Schreiben(new Konfigangaben(orte.Konfig, [a.Binden.ToString()], (uint)a.Tcp, a.TcpAussen != a.Tcp ? (uint)a.TcpAussen : null,
                (uint)a.Udp, a.UdpAussen != a.Udp ? (uint)a.UdpAussen : null, orte.Profil, orte.Zustand), _uhr());
            system.ModusSetzen(orte.Konfig, Lesbar);
            Neu(orte.Konfig + " (binden " + a.Binden + ", TCP " + Ports(a.Tcp, a.TcpAussen) + ", UDP " + Ports(a.Udp, a.UdpAussen) + ")");
            _neustart = true;
        }
        ServerKonfiguration k;
        try
        {
            k = ServerKonfiguration.Laden(orte.Konfig);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Fehlt(orte.Konfig + " ist nicht lesbar: " + e.Message, "berichtigen (Felder: BETRIEB.md) oder zur Seite legen (mv); dann fragt einrichten neu.");
            return null;
        }
        var fehler = k.Pruefen();
        if (fehler.Count > 0)
        {
            Fehlt(orte.Konfig + " erfüllt die Regeln des Servers nicht: " + string.Join(" ", fehler),
                "berichtigen oder zur Seite legen (mv " + orte.Konfig + " " + orte.Konfig + ".alt); dann fragt einrichten neu.");
            return null;
        }
        if (a is null) Bleibt(orte.Konfig + " (vorhanden und gültig; einrichten schreibt nie darüber)");
        return k;
    }

    private static string Ports(int innen, int aussen) => innen == aussen ? Z(innen) : Z(aussen) + " außen, " + Z(innen) + " am Server";

    // ---- Units, Drop-in, echo.env, Kurzbefehl ----

    /// <summary>
    /// Schreibt <paramref name="soll"/>, wenn die Datei fehlt oder abweicht; die bisherige bleibt als .alt daneben. Mit
    /// <paramref name="nurEigene"/> nur über eine Datei, die <see cref="Marke"/> trägt. <c>true</c>: geschrieben.
    /// </summary>
    private bool Schreiben(string ziel, byte[] soll, string was, UnixFileMode modus, bool nurEigene)
    {
        string? alt = null;
        if (File.Exists(ziel))
        {
            var ist = File.ReadAllBytes(ziel);
            if (ist.AsSpan().SequenceEqual(soll))
            {
                Ok(was + " " + ziel);
                return false;
            }
            if (nurEigene && !Encoding.UTF8.GetString(ist).Contains(Marke, StringComparison.Ordinal))
            {
                Fehlt(ziel + " gibt es schon, stammt aber nicht von JG TacRelay; einrichten lässt die Datei, wie sie ist.",
                    "selbst entscheiden, ob sie weg kann; bis dahin die Befehle ausgeschrieben nehmen (unten).");
                return false;
            }
            alt = ziel + ".alt";
            if (File.Exists(alt)) alt = ziel + ".alt-" + Zeitstempel;
            File.Move(ziel, alt);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ziel))!);
        Dateiablage.NeuSchreiben(ziel, soll, nurBesitzer: false);
        system.ModusSetzen(ziel, modus);
        Neu(was + " " + ziel + (alt is null ? "" : " (die bisherige liegt als " + Path.GetFileName(alt) + " daneben)"));
        return true;
    }

    private void Units(ServerKonfiguration k, bool echo)
    {
        if (Schreiben(orte.Unit("funk-server.service"), Lf(File.ReadAllBytes(Paket("funk-server.service"))), "Unit", Lesbar, nurEigene: false))
        {
            _unitsNeu = true;
            _neustart = true;
        }
        if (k.TcpPort is > 0 and < 1024 || k.UdpPort is > 0 and < 1024)
        {
            var text = "# " + Marke + ": Der Server bindet einen Port unter 1024 (tcpPort oder udpPort in\n"
                + "# " + orte.Konfig + "). Das darf er als Benutzer funk nur mit dieser einen Fähigkeit; die Unit selbst gibt ihm keine.\n"
                + "[Service]\nAmbientCapabilities=CAP_NET_BIND_SERVICE\nCapabilityBoundingSet=CAP_NET_BIND_SERVICE\n";
            if (Schreiben(orte.DropIn, Encoding.UTF8.GetBytes(text), "Drop-in für Ports unter 1024 (CAP_NET_BIND_SERVICE)", Lesbar, nurEigene: true))
            {
                _unitsNeu = true;
                _neustart = true;
            }
        }
        if (!echo) return;
        if (Schreiben(orte.Unit("funk-echo.service"), Lf(File.ReadAllBytes(Paket("funk-echo.service"))), "Unit", Lesbar, nurEigene: false))
        {
            _unitsNeu = true;
            _echoNeu = true;
        }
        var adresse = k.Binden.Count > 0 ? k.Binden[0] : "127.0.0.1";
        if (File.Exists(orte.EchoEnv))
        {
            Bleibt(orte.EchoEnv + " (einrichten ändert sie nicht)");
            return;
        }
        var env = "# " + Marke + ": die Adresse, über die der Echo-Funker den Server erreicht (wie \"binden\" in server.json).\n"
            + "FUNK_ECHO_ADRESSE=" + adresse + "\n";
        Dateiablage.NeuSchreiben(orte.EchoEnv, Encoding.UTF8.GetBytes(env), nurBesitzer: false);
        system.ModusSetzen(orte.EchoEnv, Lesbar);
        Neu(orte.EchoEnv + " (FUNK_ECHO_ADRESSE=" + adresse + ")");
        _echoNeu = true;
    }

    private void Kurzbefehl()
    {
        var text = "#!/bin/sh\n"
            + "# " + Marke + ": \"funk-admin <befehl>\" statt\n"
            + "# \"dotnet " + orte.Programme + "/funk-admin.dll <befehl> --konfig " + orte.Konfig + "\".\n"
            + ": \"${FUNK_KONFIG:=" + orte.Konfig + "}\"\n"
            + "export FUNK_KONFIG\n"
            + "exec " + Einrichtorte.Dotnet + " " + orte.Programme + "/funk-admin.dll \"$@\"\n";
        var ziel = orte.Kurzbefehl;
        if (File.Exists(ziel) && !Encoding.UTF8.GetString(File.ReadAllBytes(ziel)).Contains(Marke, StringComparison.Ordinal)) _kurzbefehl = false;
        Schreiben(ziel, Encoding.UTF8.GetBytes(text), "Kurzbefehl (funk-admin konten statt dotnet ... --konfig ...)", Offen, nurEigene: true);
    }

    private void Uhr()
    {
        switch (system.ZeitSynchron())
        {
            case "yes":
                Ok("Uhr synchronisiert (NTP)");
                break;
            case "no":
                Fehlt("Die Uhr ist nicht synchronisiert; der Zweitfaktor (TOTP) verträgt nur 30 s Abweichung.",
                    "apt install systemd-timesyncd (oder chrony), dann timedatectl set-ntp true; im Container gilt die Uhr des Hosts.");
                break;
            default:
                Bleibt("Uhrzeit hier nicht prüfbar (kein timedatectl, etwa im Container); auf dem Host mit chronyc tracking oder timedatectl ansehen.");
                break;
        }
    }

    // ---- Firewall und Router: nur zeigen, ufw nur nach ja ----

    private void Firewall(ServerKonfiguration k, Antworten? a)
    {
        var ip = k.Binden.Count > 0 ? k.Binden[0] : "<adresse>";
        var router = a?.Router ?? (IPAddress.TryParse(ip, out var x) && IstPrivat(x));
        aus.WriteLine();
        aus.WriteLine("Firewall und Router (einrichten zeigt, was nötig ist; angefasst wird nichts ohne dein ja):");
        if (system.Ausfuehren("systemd-detect-virt", "--container").Ausgabe.Trim() == "lxc")
        {
            aus.WriteLine("  Proxmox-Host, Datei /etc/pve/firewall/<id>.fw (<id> ist die Nummer des Containers), unter [RULES]:");
            aus.WriteLine("    IN ACCEPT -p tcp -dport " + Z(k.TcpPort));
            aus.WriteLine("    IN ACCEPT -p udp -dport " + Z(k.UdpPort));
            aus.WriteLine("  Den ganzen Block mit [OPTIONS] zeigt HEIMSERVER-PROXMOX.md, Abschnitt 7.");
        }
        if (router)
        {
            aus.WriteLine("  Portfreigaben am Router (FRITZ!Box: Internet, Freigaben, Portfreigaben), genau zwei:");
            aus.WriteLine("    TCP  außen " + Z(k.TcpPortOeffentlich) + "  an  " + ip + ":" + Z(k.TcpPort));
            aus.WriteLine("    UDP  außen " + Z(k.UdpPortOeffentlich) + "  an  " + ip + ":" + Z(k.UdpPort));
        }
        else
        {
            FirewallHier(k.TcpPort, k.UdpPort);
        }
        aus.WriteLine();
    }

    private void FirewallHier(int tcp, int udp)
    {
        var ufw = system.Ausfuehren("ufw", "status");
        if (ufw.Code == 0 && ufw.Ausgabe.Contains("Status: active", StringComparison.Ordinal))
        {
            var fehlen = new[] { Z(tcp) + "/tcp", Z(udp) + "/udp" }
                .Where(r => !Regex.IsMatch(ufw.Ausgabe, "^" + Regex.Escape(r) + @"\s+ALLOW", RegexOptions.Multiline, Frist)).ToList();
            if (fehlen.Count == 0)
            {
                Ok("ufw lässt TCP " + Z(tcp) + " und UDP " + Z(udp) + " herein");
                return;
            }
            aus.WriteLine("  ufw ist aktiv; es fehlen:");
            foreach (var r in fehlen) aus.WriteLine("    ufw allow " + r);
            if (!_d.JaNein("  Diese Regeln jetzt in ufw eintragen? Nur ein ausdrückliches j oder ja trägt sie ein"))
            {
                Bleibt("ufw unverändert; die Befehle oben bei Bedarf selbst eingeben.");
                return;
            }
            foreach (var r in fehlen)
            {
                var e = system.Ausfuehren("ufw", "allow", r);
                if (e.Code == 0) Neu("ufw allow " + r);
                else Fehlt("ufw allow " + r + " scheiterte (" + Kurz(e.Ausgabe) + ").", "von Hand eingeben, dann zeigt ufw den Grund.");
            }
            return;
        }
        var nft = system.Ausfuehren("nft", "list", "ruleset");
        if (nft.Code == 0 && nft.Ausgabe.Contains("hook input", StringComparison.Ordinal))
        {
            aus.WriteLine("  nftables filtert eingehende Verbindungen. Nötig sind in der Kette input (dauerhaft in /etc/nftables.conf):");
            aus.WriteLine("    tcp dport " + Z(tcp) + " accept");
            aus.WriteLine("    udp dport " + Z(udp) + " accept");
            Bleibt("nftables unverändert; einrichten schreibt nicht in fremde Regelwerke.");
            return;
        }
        aus.WriteLine("  Hier ist keine Firewall aktiv (ufw, nftables). Filtert der Anbieter davor, dort TCP " + Z(tcp) + " und UDP " + Z(udp) + " freigeben.");
    }

    // ---- Umzug ----

    private bool Aktiv(string dienst) => system.Ausfuehren("systemctl", "is-active", "--quiet", dienst).Code == 0;

    private bool Systemctl(params string[] argumente)
    {
        var r = system.Ausfuehren("systemctl", argumente);
        if (r.Code == 0) return true;
        Fehlt("systemctl " + string.Join(" ", argumente) + " scheiterte (" + Kurz(r.Ausgabe) + ").", "systemctl status " + argumente[^1] + " zeigt den Grund.");
        return false;
    }

    private int UmzugEinspielen(ServerKonfiguration k, string paket, bool mitServerschluessel)
    {
        var lief = system.DienstLaeuft(k.SocketPfad) || Aktiv("funk-server");
        var echoLief = Aktiv("funk-echo");
        if (lief)
        {
            if (_d.JaNein("funk-server läuft. Für das Einspielen anhalten (die Clients bekommen Bye{WARTUNG} und warten)?") is false)
                return Schluss("Umzugspaket nicht eingespielt, der Dienst läuft weiter.", "zum Umzugsabend noch einmal aufrufen und mit j antworten.");
            if (echoLief) system.Ausfuehren("systemctl", "stop", "funk-echo");
            if (!Systemctl("stop", "funk-server")) return 1;
            Neu("funk-server für den Umzug angehalten");
        }
        aus.WriteLine();
        aus.WriteLine("Umzugspaket " + Path.GetFullPath(paket) + ":");
        var code = new Umzug(ein, aus, system).Einspielen(k, orte.Credentials, paket, mitServerschluessel, "funk", _uhr(), weiterZeigen: false);
        if (code != 0)
        {
            if (lief)
            {
                system.Ausfuehren("systemctl", "start", "funk-server");
                if (echoLief) system.Ausfuehren("systemctl", "start", "funk-echo");
                aus.WriteLine("       Der alte Stand bleibt; funk-server läuft wieder.");
            }
            return code;
        }
        _umzug = true;
        _neustart = true;
        return 0;
    }

    // ---- Profil ----

    private List<Profilschluessel> ProfilschluesselLaden()
    {
        var liste = new List<Profilschluessel>();
        foreach (var wort in File.ReadAllText(Paket("profilschluessel.txt")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!wort.StartsWith("MFkw", StringComparison.Ordinal)) continue;
            try
            {
                liste.Add(Profilschluessel.AusSpki(Convert.FromBase64String(wort)));
            }
            catch (Exception e) when (e is FormatException or ArgumentException or CryptographicException)
            {
                Fehlt("In " + Paket("profilschluessel.txt") + " steht ein unlesbarer Schlüssel (" + wort[..Math.Min(12, wort.Length)] + "...).", "das Serverpaket neu entpacken.");
            }
        }
        return liste;
    }

    private ProfilHost? AnfrageHost() =>
        File.Exists(orte.Anfrage) && Profilanfrage.TryLesen(File.ReadAllText(orte.Anfrage), out var a, out _) ? a!.Host : null;

    private static byte[] OhneBom(byte[] b) => b.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? b[3..] : b;

    private Profilstand Profilschritt(ServerKonfiguration k, string? neuPfad, ProfilHost? host)
    {
        var ziel = k.Aufloesen(k.Profil!);
        var pin = ServerPin();
        var schluessel = ProfilschluesselLaden();
        try
        {
            if (schluessel.Count == 0)
            {
                Fehlt("In " + Paket("profilschluessel.txt") + " steht kein öffentlicher Profilschlüssel (eine Zeile, die mit MFkw beginnt).",
                    "das ganze Serverpaket nehmen; ohne den Schlüssel lässt sich kein Profil prüfen.");
                return Profilstand.Fehler;
            }
            var kennungen = string.Join(", ", schluessel.Select(s => s.Fingerabdruck.Anzeige));
            host ??= AnfrageHost();
            var jetzt = _uhr();

            byte[]? daBytes = null;
            ProfilPruefung? da = null;
            if (File.Exists(ziel))
            {
                daBytes = OhneBom(File.ReadAllBytes(ziel));
                if (SigniertesProfil.TryLesen(daBytes, out var s) && ProfilAnnahme.Pruefen(s, schluessel, null, jetzt) is { Profil: not null } p) da = p;
            }

            if (neuPfad is not null)
            {
                var bytes = OhneBom(File.ReadAllBytes(neuPfad));
                if (!SigniertesProfil.TryLesen(bytes, out var signiert))
                {
                    Fehlt(Path.GetFullPath(neuPfad) + " ist kein signiertes Serverprofil.", "profil.json nehmen, das der Eigentümer mit profil-erneuern geschrieben hat (Ordner zum-verschicken), nicht daten.json.");
                    return Profilstand.Fehler;
                }
                var neu = ProfilAnnahme.Pruefen(signiert, schluessel, null, jetzt);
                if (neu.Urteil == ProfilUrteil.SignaturUngueltig)
                {
                    Fehlt("Das Profil ist nicht mit dem Profilschlüssel des Eigentümers unterschrieben (" + kennungen + ").", "Den Eigentümer um sein profil.json bitten; ein anders unterschriebenes nimmt kein Client an.");
                    return Profilstand.Fehler;
                }
                if (neu.Profil is not { } inhalt)
                {
                    Fehlt("Das Profil ist richtig unterschrieben, aber sein Inhalt verletzt 3.1.", "Den Eigentümer bitten, es mit profil-erneuern neu zu schreiben.");
                    return Profilstand.Fehler;
                }
                if (da is null || !daBytes.AsSpan().SequenceEqual(bytes))
                {
                    if (da?.Profil is { } alt && inhalt.ProfilVersion <= alt.ProfilVersion)
                    {
                        Fehlt("Das Profil hat Version " + Z(inhalt.ProfilVersion) + ", eingespielt ist schon Version " + Z(alt.ProfilVersion) + ".",
                            "das neueste profil.json vom Eigentümer nehmen; die Clients nehmen nur höhere Versionen an.");
                        return Profilstand.Fehler;
                    }
                    if (!Passt(inhalt, pin, host, k, neu.Abgelaufen)) return Profilstand.Fehler;
                    if (File.Exists(ziel))
                    {
                        var dir = Path.GetDirectoryName(Path.GetFullPath(ziel))!;
                        var vorher = da?.Profil is { } v ? Path.Combine(dir, "profil-v" + Z(v.ProfilVersion) + ".json") : ziel + ".alt";
                        if (File.Exists(vorher)) vorher += "-" + Zeitstempel;
                        File.Move(ziel, vorher);
                    }
                    Dateiablage.NeuSchreiben(ziel, bytes, nurBesitzer: false);
                    system.ModusSetzen(ziel, Lesbar);
                    var unterschrift = schluessel.First(s => s.Prueft(signiert)).Fingerabdruck.Anzeige;
                    Neu("Serverprofil Version " + Z(inhalt.ProfilVersion) + " nach " + ziel + " (unterschrieben vom Eigentümer, " + unterschrift + "; Pin, Name und Port passen)");
                    _profilNeu = true;
                    return Profilstand.Bereit;
                }
            }

            if (da?.Profil is { } d)
            {
                if (Passt(d, pin, host, k, da.Abgelaufen))
                {
                    Ok("Serverprofil Version " + Z(d.ProfilVersion) + " (" + string.Join(", ", d.Hosts.Select(h => h.Name)) + ", TCP " + Z(d.TcpPort) + "), Pin und Port passen");
                    return Profilstand.Bereit;
                }
                return Warten(k, pin, host);
            }
            if (File.Exists(ziel))
                Fehlt(ziel + " ist nicht mit dem Profilschlüssel des Eigentümers unterschrieben oder kaputt; kein Client nimmt es an.", "ein Profil vom Eigentümer einspielen (unten).");
            return Warten(k, pin, host);
        }
        finally
        {
            foreach (var s in schluessel) s.Dispose();
        }
    }

    private bool Passt(Serverprofil p, Fingerabdruck pin, ProfilHost? host, ServerKonfiguration k, bool abgelaufen)
    {
        var gut = true;
        if (!p.PinPasst(pin))
        {
            Fehlt("Der Pin dieses Servers (Kennung " + pin.Anzeige + ") steht nicht im Profil Version " + Z(p.ProfilVersion) + ".",
                "Dem Eigentümer die Anfragezeile schicken (WARTET AUF PROFIL zeigt sie) und mit seinem neuen profil.json noch einmal aufrufen.");
            gut = false;
        }
        if (p.TcpPort != k.TcpPortOeffentlich)
        {
            Fehlt("Das Profil nennt TCP-Port " + Z(p.TcpPort) + ", " + orte.Konfig + " aber " + Z(k.TcpPortOeffentlich) + " (oeffentlicherTcpPort, sonst tcpPort).",
                "Leitet der Router " + Z(p.TcpPort) + " hierher, \"oeffentlicherTcpPort\": " + Z(p.TcpPort) + " in server.json eintragen; sonst den Eigentümer um ein Profil mit Port "
                + Z(k.TcpPortOeffentlich) + " bitten.");
            gut = false;
        }
        if (host is not null && !p.Hosts.Any(h => string.Equals(h.Name, host.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Fehlt("Der Name " + host.Name + " steht nicht im Profil (" + string.Join(", ", p.Hosts.Select(h => h.Name)) + ").",
                "Dem Eigentümer die Anfragezeile mit diesem Namen schicken, oder den Namen in " + orte.Anfrage + " berichtigen.");
            gut = false;
        }
        if (gut && abgelaufen)
        {
            Fehlt("Das Profil ist am " + DateTimeOffset.FromUnixTimeMilliseconds(p.GueltigBis).UtcDateTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                + " abgelaufen; die Clients verbinden weiter, warnen aber.", "Der Eigentümer verlängert es mit profil-erneuern (nur verlängern).");
        }
        return gut;
    }

    private Profilstand Warten(ServerKonfiguration k, Fingerabdruck pin, ProfilHost? host)
    {
        for (var i = 0; i < 5 && host is null; i++)
        {
            var e = Frage("Öffentlicher Name des Servers für die Anfrage an den Eigentümer (etwa funk.example.org)", "");
            if (e is null) break;
            try
            {
                host = Hostangabe.Lesen(e, "Der Name");
            }
            catch (ArgumentException x)
            {
                aus.WriteLine("       " + x.Message);
            }
        }
        if (host is null)
        {
            Fehlt("Ohne den öffentlichen Namen gibt es keine Anfragezeile.", "noch einmal aufrufen und den Namen angeben.");
            return Profilstand.Fehler;
        }
        var anfrage = new Profilanfrage(host, (ushort)k.TcpPortOeffentlich, pin);
        var zeile = Encoding.UTF8.GetBytes(anfrage.Zeile + "\n");
        if (!File.Exists(orte.Anfrage) || !File.ReadAllBytes(orte.Anfrage).AsSpan().SequenceEqual(zeile))
        {
            Dateiablage.AtomarSchreiben(orte.Anfrage, zeile, nurBesitzer: false);
            system.ModusSetzen(orte.Anfrage, Lesbar);
        }
        aus.WriteLine("WARTET AUF PROFIL");
        aus.WriteLine("Der Server startet erst mit einem Profil vom Eigentümer, in dem sein Pin und sein Name stehen. Schick dem Eigentümer diese Zeile,");
        aus.WriteLine("egal auf welchem Weg; sie ist nicht geheim und liegt auch in " + orte.Anfrage + ":");
        aus.WriteLine();
        aus.WriteLine("    " + anfrage.Zeile);
        aus.WriteLine();
        aus.WriteLine("Lies ihm am Telefon die Kennung vor: " + anfrage.Kennung + ", dazu Name und Port: " + anfrage.Host.Name + ", "
            + anfrage.Port.ToString(CultureInfo.InvariantCulture));
        aus.WriteLine("Er vergleicht alle drei in profil-erneuern; so weiß er, dass die Zeile von dir kommt und unterwegs nicht verändert wurde.");
        aus.WriteLine("Danach, mit seinem profil.json in diesem Ordner: sudo bash funk-einrichten.sh --profil profil.json");
        return Profilstand.Wartet;
    }

    // ---- Start, Journal, Abschluss ----

    private async Task<VerwaltungsAntwort> SendenAsync(ServerKonfiguration k, string befehl)
    {
        try
        {
            return await senden(k.SocketPfad, befehl, []).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException or InvalidDataException)
        {
            return VerwaltungsAntwort.Fehler(e.Message);
        }
    }

    private void Neuladen()
    {
        if (_neuGeladen) return;
        system.Ausfuehren("systemctl", "daemon-reload");
        _neuGeladen = true;
    }

    private async Task<bool> StartenAsync(ServerKonfiguration k)
    {
        var aktiv = Aktiv("funk-server");
        if (aktiv && !_neustart)
        {
            if (_unitsNeu) Neuladen();
            if (!_profilNeu)
            {
                Ok("funk-server läuft");
                return true;
            }
            var r = await SendenAsync(k, "profil-neu-laden").ConfigureAwait(false);
            if (r.Ok) Neu("Serverprofil im laufenden Dienst neu geladen (" + r.Text + ")");
            else Fehlt("profil-neu-laden scheiterte: " + r.Text, "sudo systemctl restart funk-server.");
            return r.Ok;
        }
        var seit = _uhr().ToUnixTimeSeconds() - 1;
        Neuladen();
        if (!(aktiv ? Systemctl("restart", "funk-server") : Systemctl("enable", "--now", "funk-server"))) return false;
        Neu(aktiv ? "funk-server neu gestartet (Einstellungen, Units oder Zustand geändert)" : "funk-server eingeschaltet und gestartet (systemctl enable --now)");
        return Bereit(k, seit);
    }

    private Dictionary<string, string> Zustand()
    {
        var aus2 = system.Ausfuehren("systemctl", "show", "-p", "ActiveState", "-p", "SubState", "-p", "Result", "-p", "ExecMainStatus", "-p", "NRestarts", "funk-server").Ausgabe;
        var werte = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var z in aus2.Split('\n'))
        {
            var gleich = z.IndexOf('=', StringComparison.Ordinal);
            if (gleich > 0) werte[z[..gleich].Trim()] = z[(gleich + 1)..].Trim();
        }
        return werte;
    }

    private bool Bereit(ServerKonfiguration k, long seit)
    {
        var journal = "";
        for (var i = 0; i < 30; i++)
        {
            journal = system.Ausfuehren("journalctl", "-u", "funk-server", "--since", "@" + Z(seit), "-o", "cat", "--no-pager", "-n", "200").Ausgabe;
            var zeile = journal.Split('\n').LastOrDefault(z => z.Contains("Funk-Server", StringComparison.Ordinal) && z.Contains(" bereit: ", StringComparison.Ordinal));
            if (zeile is not null)
            {
                Ok(zeile[zeile.IndexOf("Funk-Server", StringComparison.Ordinal)..].Trim());
                Lauscht(k);
                return true;
            }
            var w = Zustand();
            var gescheitert = w.GetValueOrDefault("ActiveState") is "failed" || w.GetValueOrDefault("SubState") is "failed" or "auto-restart"
                || (int.TryParse(w.GetValueOrDefault("NRestarts"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0)
                || w.GetValueOrDefault("Result") is { Length: > 0 } r && r != "success";
            if (gescheitert)
            {
                Fehlerbild(journal, w);
                return false;
            }
            system.Warten(TimeSpan.FromSeconds(1));
        }
        Fehlt("funk-server meldet nach 30 s nicht \"bereit\".", "journalctl -u funk-server -n 50 --no-pager zeigt, woran es hängt.");
        return false;
    }

    private void Fehlerbild(string journal, Dictionary<string, string> zustand)
    {
        var status = zustand.GetValueOrDefault("ExecMainStatus") ?? "";
        if (status == "243" || journal.Contains("243/CREDENTIALS", StringComparison.Ordinal))
        {
            var fehlen = Schluesseldateien.Where(n => !File.Exists(Path.Combine(orte.Credentials, n))).ToList();
            if (fehlen.Count > 0)
            {
                Fehlt("systemd findet eine Datei aus LoadCredential nicht (status=243/CREDENTIALS): " + string.Join(", ", fehlen) + " fehlt in "
                    + orte.Credentials + ".", "noch einmal aufrufen; einrichten legt fehlende Schlüssel an.");
                return;
            }
            // Prüfung der Pakete 0.4.0: Im unprivilegierten LXC ohne nesting fallen unter Debian 13 laut Proxmox-Forum viele
            // systemd-Dienste mit 243/CREDENTIALS aus, nicht mit 226/NAMESPACE (hier nicht gemessen). Liegen alle Dateien da und
            // ist es ein Container, kommt zuerst dieselbe Abhilfe wie bei 226.
            if (system.Ausfuehren("systemd-detect-virt", "--container").Ausgabe.Trim() == "lxc")
            {
                Fehlt("systemd kann die Dateien aus LoadCredential nicht bereitstellen (status=243/CREDENTIALS), obwohl alle in " + orte.Credentials
                    + " liegen. Im Proxmox-Container kommt das auch, wenn dem LXC nesting=1 fehlt (berichtet für Debian 13).",
                    "auf dem Proxmox-Host pct set <id> --features nesting=1 und pct reboot <id> (<id> ist die Nummer des Containers), dann hier noch einmal "
                    + "aufrufen. Ist nesting=1 schon gesetzt (pct config <id>): die Pfade unter LoadCredential in " + orte.Unit("funk-server.service")
                    + " mit " + orte.Credentials + " vergleichen.");
                return;
            }
            Fehlt("systemd findet eine Datei aus LoadCredential nicht (status=243/CREDENTIALS).",
                "die Pfade unter LoadCredential in " + orte.Unit("funk-server.service") + " mit " + orte.Credentials + " vergleichen.");
            return;
        }
        if (status == "226" || journal.Contains("226/NAMESPACE", StringComparison.Ordinal))
        {
            Fehlt("Der Dienst darf im Container keine eigenen Namensräume anlegen (status=226/NAMESPACE): Dem LXC fehlt nesting=1.",
                "auf dem Proxmox-Host pct set <id> --features nesting=1 und pct reboot <id> (<id> ist die Nummer des Containers), dann hier noch einmal aufrufen.");
            return;
        }
        var ungueltig = journal.IndexOf("Konfiguration ungültig", StringComparison.Ordinal);
        if (ungueltig >= 0)
        {
            var gruende = journal[ungueltig..].Split('\n').Skip(1).Select(z => z.Trim())
                .Where(z => z.Length > 0 && !z.StartsWith("funk-server.service:", StringComparison.Ordinal)).Take(8);
            Fehlt("Der Server lehnt die Konfiguration ab: " + string.Join("; ", gruende) + ".",
                "funk-admin pruefen --vollstaendig zeigt alle Befunde; " + orte.Konfig + " berichtigen und noch einmal aufrufen.");
            return;
        }
        var letzte = journal.Split('\n').Select(z => z.TrimEnd()).Where(z => z.Length > 0).TakeLast(10);
        Fehlt("funk-server startet nicht (" + string.Join(", ", zustand.Where(w => w.Key is "ActiveState" or "Result" or "ExecMainStatus").Select(w => w.Key + "=" + w.Value))
            + "). Letzte Zeilen im Journal:\n         " + string.Join("\n         ", letzte) + "\n      ",
            "journalctl -u funk-server -n 50 --no-pager zeigt mehr; danach noch einmal aufrufen.");
    }

    private void Lauscht(ServerKonfiguration k)
    {
        var tcp = system.Ausfuehren("ss", "-H", "-ltn").Ausgabe;
        var udp = system.Ausfuehren("ss", "-H", "-lun").Ausgabe;
        bool Hoert(string text, int port) => Regex.IsMatch(text, @"[:\]]" + Z(port) + @"\s", RegexOptions.None, Frist);
        if (Hoert(tcp, k.TcpPort) && Hoert(udp, k.UdpPort))
            Ok("TCP " + Z(k.TcpPort) + " und UDP " + Z(k.UdpPort) + " hören (ss)");
        else
            Fehlt("ss zeigt TCP " + Z(k.TcpPort) + " oder UDP " + Z(k.UdpPort) + " nicht.", "ss -tulpen ansehen; die Adresse in binden muss zu diesem Rechner gehören.");
    }

    private void Echo(bool echo)
    {
        if (!echo) return;
        if (!File.Exists(orte.EchoIdentitaet))
        {
            _echoOffen = true;
            return;
        }
        if (!Aktiv("funk-echo"))
        {
            if (_unitsNeu) Neuladen();
            if (Systemctl("enable", "--now", "funk-echo")) Neu("funk-echo eingeschaltet und gestartet");
        }
        else if (_echoNeu)
        {
            Neuladen();
            if (Systemctl("restart", "funk-echo")) Neu("funk-echo neu gestartet (Unit oder echo.env geändert)");
        }
        else
        {
            Ok("funk-echo läuft");
        }
    }

    private string Befehl(string befehl) => _kurzbefehl
        ? "sudo funk-admin " + befehl
        : "sudo dotnet " + orte.Programme + "/funk-admin.dll " + befehl + " --konfig " + orte.Konfig;

    private async Task AbschlussAsync(ServerKonfiguration k)
    {
        var status = await SendenAsync(k, "status").ConfigureAwait(false);
        var konten = await SendenAsync(k, "konten").ConfigureAwait(false);
        var liste = konten.Daten as JsonArray;
        var eigentuemer = liste?.FirstOrDefault(x => x?["eigentuemer"]?.GetValue<bool>() == true);
        var admins = liste?.Count(x => x?["admin"] is not null) ?? 0;
        var hosts = "";
        if (SigniertesProfil.TryLesen(OhneBom(File.ReadAllBytes(k.Aufloesen(k.Profil!))), out var s) && Serverprofil.TryLesen(s.Daten, out var p))
            hosts = string.Join(", ", p.Hosts.Select(Hostangabe.Anzeige)) + ", Profil Version " + Z(p.ProfilVersion);

        aus.WriteLine();
        aus.WriteLine("==== Funk-Server " + Umzug.Fassung + " bereit ====");
        aus.WriteLine("Name:        " + hosts);
        aus.WriteLine("Ports:       TCP " + Ports(k.TcpPort, k.TcpPortOeffentlich) + ", UDP " + Ports(k.UdpPort, k.UdpPortOeffentlich) + ", Bindadresse " + string.Join(", ", k.Binden));
        aus.WriteLine("Pin:         " + ServerPin().Anzeige);
        aus.WriteLine("Stand:       " + (status.Ok ? status.Text : "nicht abfragbar (" + status.Text + ")"));
        aus.WriteLine("Eigentümer:  " + (eigentuemer is null ? "noch keiner" : eigentuemer["name"]?.ToString() + " (Konto " + eigentuemer["kontoId"]?.ToString() + ")")
            + (admins > 0 ? ", Admins: " + Z(admins) : ""));
        // Seit 0.3.0 (A51): Die Sperrliste läuft ohne Eintrag in server.json; den Rest des Rechners härtet die Checkliste.
        aus.WriteLine("Schutz:      eingebaute Sperrliste gegen Scanner und Fluten (den Rechner selbst härten: HEIMSERVER-HAERTUNG.md), ansehen mit "
            + Befehl("sperrliste"));
        if (_umzug)
            aus.WriteLine("Weiter:      prüfen mit " + Befehl("konten") + ", " + Befehl("geraete") + " und " + Befehl("admins") + "; dann den DNS-Eintrag umstellen (UMZUG.md, Umzugsabend).");
        else if (eigentuemer is null)
            aus.WriteLine("Weiter:      JG TacRelay installieren, verbinden und auf die Freigabe warten (Antrag); dann hier: " + Befehl("erster-admin"));
        else
            aus.WriteLine("Weiter:      nichts. Prüfen jederzeit mit " + Befehl("pruefen --vollstaendig"));
        if (!_echoOffen) return;
        var adresse = k.Binden.Count > 0 ? k.Binden[0] : "<adresse>";
        aus.WriteLine();
        aus.WriteLine("Echo-Funker, wenn du magst (" + (_umzug ? "" : "nach dem ersten Admin; ") + "EINRICHTEN.md, \"Echo-Funker\"):");
        if (!_umzug) aus.WriteLine("  " + Befehl("netz-setzen --id 90 --name Echo --offen HOEREN_SENDEN --va"));
        aus.WriteLine("  sudo runuser -u funk-echo -- " + Einrichtorte.Dotnet + " " + orte.Programme + "/funk-echo.dll --profil " + orte.Profil + " --identitaet "
            + orte.EchoIdentitaet + " --adresse " + adresse);
        aus.WriteLine("  Er zeigt einen Code und die zwei Befehle zum Freigeben. Danach Strg+C und noch einmal sudo bash funk-einrichten.sh;");
        aus.WriteLine("  das startet ihn als Dienst.");
    }
}

/// <summary>Was <c>funk-admin einrichten</c> tun soll: mit einem Profil von Sarevoc, mit einem Umzugspaket, oder beides nicht.</summary>
public sealed record Einrichtauftrag(string? Profil = null, string? Umzug = null, bool MitServerschluessel = false);
