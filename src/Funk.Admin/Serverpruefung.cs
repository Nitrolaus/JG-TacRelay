// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Funk.Protokoll.Identitaet;
using Funk.Server.Betrieb;

namespace Funk.Admin;

/// <summary>
/// <c>funk-admin pruefen [--vollstaendig]</c> (EINRICHTEN-UMZUG-ENTWURF, "Dazu im Code"). Ohne Schalter wie
/// <c>funk-server pruefen</c>: Regeln der server.json und das Serverprofil mit dem Portabgleich. Mit <c>--vollstaendig</c> auch,
/// was bisher erst systemd mit status=243/CREDENTIALS merkte oder gar niemand: Schlüsseldateien (da, Größe, lesbar, passen
/// zusammen, Rechte), der Pin des Serverschlüssels im Profil, die Signatur (mit <c>--spki</c>), Ablauf, wohin die Hosts im DNS
/// zeigen, die Uhr (TOTP verträgt 30 s) und ob die Ports frei sind. Was sich auf diesem Rechner nicht prüfen lässt, steht als
/// Hinweis da. Geschrieben wird nichts.
/// </summary>
public static class Serverpruefung
{
    /// <summary>Gruppe und andere: Bei Schlüsseldateien darf keines davon gesetzt sein (0600).</summary>
    private const UnixFileMode Fremd = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public static IReadOnlyList<Befund> Pruefen(string konfigPfad, bool vollstaendig, string? credentials, IReadOnlyList<string> spki, string? host, Systemblick system)
    {
        ArgumentNullException.ThrowIfNull(konfigPfad);
        ArgumentNullException.ThrowIfNull(spki);
        ArgumentNullException.ThrowIfNull(system);
        var b = new List<Befund>();
        void Ok(string t) => b.Add(new(Befundstufe.Ok, t));
        void Hinweis(string t) => b.Add(new(Befundstufe.Hinweis, t));
        void Warnung(string t) => b.Add(new(Befundstufe.Warnung, t));
        void Fehler(string t) => b.Add(new(Befundstufe.Fehler, t));

        ServerKonfiguration k;
        try
        {
            k = ServerKonfiguration.Laden(konfigPfad);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Fehler("Konfiguration nicht lesbar: " + e.Message);
            return b;
        }
        var regeln = k.Pruefen();
        foreach (var f in regeln) Fehler("Konfiguration: " + f);
        if (regeln.Count == 0) Ok("Konfiguration " + Path.GetFullPath(konfigPfad) + " erfüllt die Regeln des Servers.");

        var (profilFehler, warnungen) = Profilabgleich.Pruefen(k);
        Serverprofil? profil = null;
        SigniertesProfil? signiert = null;
        if (profilFehler is not null) Fehler("Serverprofil: " + profilFehler);
        else if (k.Profil is { } pp && SigniertesProfil.TryLesen(File.ReadAllBytes(k.Aufloesen(pp)), out signiert) && Serverprofil.TryLesen(signiert.Daten, out profil))
            Ok("Serverprofil Version " + profil.ProfilVersion.ToString(CultureInfo.InvariantCulture) + ": " + string.Join(", ", profil.Hosts.Select(Hostangabe.Anzeige))
                + ", TCP-Port " + profil.TcpPort.ToString(CultureInfo.InvariantCulture) + ".");
        foreach (var w in warnungen) Warnung(w);
        if (!vollstaendig) return b;

        // ---- Schlüsseldateien ----
        var orte = new Schluesselorte(k, credentials);
        if (!Directory.Exists(orte.Ordner))
            Fehler("Schlüsselordner " + orte.Ordner + " gibt es nicht. Angeben mit --credentials <ordner>; im Dienst kommt er aus LoadCredential (/etc/funk/credentials).");
        else if (system.Modus(orte.Ordner) is { } om && (om & Fremd) != 0)
            Warnung("Schlüsselordner " + orte.Ordner + " ist für andere offen (Rechte " + Oktal(om) + "): chmod 700 " + orte.Ordner);
        var rechteUnpruefbar = false;
        bool Datei(string pfad, string was, bool geheim)
        {
            if (!File.Exists(pfad))
            {
                Fehler(was + " " + pfad + " fehlt (systemd meldet das sonst erst als status=243/CREDENTIALS).");
                return false;
            }
            if (!geheim) return true;
            var m = system.Modus(pfad);
            if (m is null) rechteUnpruefbar = true;
            else if ((m.Value & Fremd) != 0) Warnung(was + " " + pfad + " ist für andere lesbar (Rechte " + Oktal(m.Value) + "): chmod 600 " + pfad);
            return true;
        }

        Fingerabdruck? pin = null;
        if (Datei(orte.Zertifikat, "Zertifikat", geheim: false) & Datei(orte.Schluessel, "Serverschlüssel", geheim: true))
        {
            try
            {
                using var paar = X509Certificate2.CreateFromPemFile(orte.Zertifikat, orte.Schluessel);
                using var ec = paar.GetECDsaPrivateKey();
                if (ec is null || !P256Spki.IstKanonisch(paar.PublicKey.ExportSubjectPublicKeyInfo()))
                    Fehler("Serverschlüssel " + orte.Schluessel + " ist kein ECDSA-P-256-Schlüssel (3.1).");
                else
                {
                    pin = Fingerabdruck.AusZertifikat(paar);
                    Ok("Serverschlüssel " + orte.Schluessel + " und Zertifikat passen zusammen (P-256), Pin " + pin.Value.Anzeige + ".");
                }
            }
            catch (Exception e) when (e is CryptographicException or ArgumentException or IOException)
            {
                Fehler("Serverschlüssel " + orte.Schluessel + " oder Zertifikat " + orte.Zertifikat + " unlesbar oder passen nicht zusammen: " + e.Message);
            }
        }
        if (Datei(orte.Geheimnis, "Betriebsgeheimnis", geheim: true))
        {
            var n = new FileInfo(orte.Geheimnis).Length;
            if (n is < Geheimnis.MindestLaenge or > Geheimnis.HoechstLaenge)
                Fehler("Betriebsgeheimnis " + orte.Geheimnis + " hat " + n.ToString(CultureInfo.InvariantCulture) + " Byte, gebraucht sind 32 bis 4096 (funk-admin geheimnis-erzeugen).");
            else Ok("Betriebsgeheimnis " + orte.Geheimnis + ": " + n.ToString(CultureInfo.InvariantCulture) + " Byte.");
        }
        if (Datei(orte.Totp, "TOTP-Schlüssel", geheim: true))
        {
            var n = new FileInfo(orte.Totp).Length;
            if (n != TotpSchluessel.Laenge)
                Fehler("TOTP-Schlüssel " + orte.Totp + " hat " + n.ToString(CultureInfo.InvariantCulture) + " Byte, gebraucht sind genau 32.");
            else Ok("TOTP-Schlüssel " + orte.Totp + ": 32 Byte.");
        }
        if (rechteUnpruefbar) Hinweis("Rechte der Schlüsseldateien hier nicht prüfbar (kein Linux); im Container: ls -l " + orte.Ordner);

        // ---- Profil gegen Pin, Host, Signatur, Ablauf, DNS ----
        if (profil is not null)
        {
            if (pin is { } p)
            {
                if (profil.PinPasst(p)) Ok("Pin des Serverschlüssels " + p.Anzeige + " steht im Profil.");
                else Fehler("Pin des Serverschlüssels " + p.Anzeige + " steht nicht im Profil: Die Clients lehnen diesen Server ab. Ein Profil mit diesem Pin: funk-admin profil-anfrage, die Zeile an den Eigentümer.");
            }
            if (host is not null)
            {
                if (profil.Hosts.Any(h => string.Equals(h.Name, host, StringComparison.OrdinalIgnoreCase))) Ok("Der Name " + host + " steht im Profil.");
                else Fehler("Der Name " + host + " steht nicht im Profil (Hosts: " + string.Join(", ", profil.Hosts.Select(h => h.Name)) + "). Ein neues Profil: funk-admin profil-anfrage.");
            }
            SignaturPruefen(signiert!, Schluessel(spki, konfigPfad), Ok, Hinweis, Fehler);
            var bis = DateTimeOffset.FromUnixTimeMilliseconds(profil.GueltigBis);
            var tag = bis.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (profil.IstAbgelaufen(DateTimeOffset.UtcNow))
                Warnung("Das Profil ist am " + tag + " abgelaufen. Die Clients verbinden weiter, warnen aber; verlängern: der Eigentümer mit profil-erneuern.");
            else if (bis - DateTimeOffset.UtcNow < TimeSpan.FromDays(30))
                Hinweis("Das Profil läuft am " + tag + " ab; rechtzeitig verlängern (der Eigentümer mit profil-erneuern).");
            foreach (var h in profil.Hosts)
            {
                var familie = h is { IPv4: true, IPv6: true } ? AddressFamily.Unspecified : h.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
                try
                {
                    var adressen = system.Aufloesen(h.Name, familie);
                    if (adressen.Length == 0) Warnung(h.Name + " hat im DNS keine passende Adresse; die Clients finden den Server so nicht.");
                    else Hinweis(h.Name + " zeigt im DNS auf " + string.Join(", ", adressen.Select(a => a.ToString())) + ".");
                }
                catch (Exception e) when (e is SocketException or ArgumentException)
                {
                    Warnung(h.Name + " ist im DNS nicht zu finden; die Clients finden den Server unter diesem Namen nicht.");
                }
            }
        }

        // ---- Uhr ----
        switch (system.ZeitSynchron())
        {
            case "yes":
                Ok("Uhr synchronisiert (NTP).");
                break;
            case "no":
                Warnung("Uhr nicht synchronisiert: Der Zweitfaktor (TOTP) verträgt nur 30 s Abweichung. systemd-timesyncd oder chrony einrichten (timedatectl).");
                break;
            default:
                Hinweis("Uhrzeit hier nicht prüfbar (kein timedatectl, etwa im Container): auf dem Host mit chronyc tracking oder timedatectl ansehen.");
                break;
        }

        // ---- Ports ----
        if (system.DienstLaeuft(k.SocketPfad))
        {
            Hinweis("Der Dienst läuft (Verwaltungssocket antwortet); seine Ports gehören ihm, geprüft wird nur, wenn er steht.");
            return b;
        }
        if (regeln.Count > 0) return b;
        foreach (var ip in k.Bindadressen())
        {
            foreach (var (art, port, name) in new[] { (ProtocolType.Tcp, k.TcpPort, "TCP"), (ProtocolType.Udp, k.UdpPort, "UDP") })
            {
                if (port == 0) continue;
                var p = name + " " + port.ToString(CultureInfo.InvariantCulture) + " auf " + ip;
                switch (system.PortPruefen(ip, port, art))
                {
                    case null:
                        Ok(p + " frei.");
                        break;
                    case SocketError.AddressAlreadyInUse:
                        Fehler(p + " ist belegt: Ein anderes Programm hört dort (" + (art == ProtocolType.Tcp ? "ss -ltnp" : "ss -lunp") + " zeigt welches).");
                        break;
                    case SocketError.AddressNotAvailable:
                        Fehler(p + ": Die Adresse " + ip + " gibt es auf diesem Rechner nicht (binden in server.json).");
                        break;
                    case SocketError.AccessDenied:
                        Fehler(p + ": keine Berechtigung. Ports unter 1024 braucht der Dienst mit CAP_NET_BIND_SERVICE, oder eine Weiterleitung auf 8443.");
                        break;
                    case { } sonst:
                        Warnung(p + " ließ sich nicht prüfen (" + sonst + ").");
                        break;
                }
            }
        }
        return b;
    }

    private static string Oktal(UnixFileMode m) => "0" + Convert.ToString((int)m & 0x1FF, 8);

    /// <summary>Öffentliche Profilschlüssel: aus <c>--spki</c>, sonst aus profilschluessel.txt neben server.json (Zeilen mit MFkw).</summary>
    private static List<string> Schluessel(IReadOnlyList<string> spki, string konfigPfad)
    {
        if (spki.Count > 0) return [.. spki];
        var datei = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(konfigPfad)) ?? ".", "profilschluessel.txt");
        if (!File.Exists(datei)) return [];
        return [.. File.ReadAllText(datei).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(w => w.StartsWith("MFkw", StringComparison.Ordinal))];
    }

    private static void SignaturPruefen(SigniertesProfil signiert, List<string> spki, Action<string> ok, Action<string> hinweis, Action<string> fehler)
    {
        if (spki.Count == 0)
        {
            hinweis("Signatur des Profils nicht geprüft: --spki <öffentlicher Profilschlüssel> angeben (die Zeile, die mit MFkw beginnt) oder profilschluessel.txt neben server.json legen.");
            return;
        }
        var schluessel = new List<Profilschluessel>();
        try
        {
            foreach (var s in spki)
            {
                try
                {
                    schluessel.Add(Profilschluessel.AusSpki(Convert.FromBase64String(s)));
                }
                catch (Exception e) when (e is FormatException or ArgumentException or CryptographicException)
                {
                    fehler("--spki ist kein öffentlicher P-256-Profilschlüssel: " + (s.Length > 12 ? s[..12] + "..." : s));
                }
            }
            if (schluessel.Count == 0) return;
            var p = ProfilAnnahme.Pruefen(signiert, schluessel, null, DateTimeOffset.UtcNow);
            if (p.Urteil == ProfilUrteil.Uebernehmen)
                ok("Signatur des Profils stimmt (Profilschlüssel " + schluessel.First(k => k.Prueft(signiert)).Fingerabdruck.Anzeige + ").");
            else
                fehler("Signatur des Profils passt zu keinem angegebenen Profilschlüssel (" + p.Urteil + "): Das Profil ist nicht vom Eigentümer, oder der Schlüssel ist ein anderer.");
        }
        finally
        {
            foreach (var s in schluessel) s.Dispose();
        }
    }
}

/// <summary>Wie schwer ein Befund wiegt.</summary>
public enum Befundstufe
{
    Ok,
    Hinweis,
    Warnung,
    Fehler,
}

/// <summary>Eine Zeile von <c>funk-admin pruefen</c>.</summary>
public sealed record Befund(Befundstufe Stufe, string Text)
{
    /// <summary>So steht die Zeile an der Konsole: Stufe links, acht Zeichen breit.</summary>
    public override string ToString() => (Stufe switch
    {
        Befundstufe.Ok => "OK",
        Befundstufe.Hinweis => "HINWEIS",
        Befundstufe.Warnung => "WARNUNG",
        _ => "FEHLER",
    }).PadRight(8) + Text;
}
