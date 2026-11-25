// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Durchsicht des echten Verbindungswegs (Modul profil), ohne Schannel und ohne Schlüsselspeicher im Profil: Auflösung nur
/// in den Familien des Profils (<c>/v4</c> fragt nur nach A-Einträgen), Adresse statt Profil-Host, Port aus dem Profil,
/// DynDNS-Wechsel mit Rückfall auf die letzte gute Adresse (7.1), lesbare Einzelheiten, der Weg des Zertifikats zu Schannel
/// für persistierte Schlüssel und kein stilles Weiterprobieren nach "Server nicht vertrauenswürdig" (4.1).
/// </summary>
internal static class ClientVerbindungswegTests
{
    private static Serverprofil Profil(bool v4 = true, bool v6 = false, ushort port = 8443, string host = "funk.example.org") =>
        new("Meine Staffel", 1, 0, long.MaxValue / 2, [new ProfilHost(host, v4, v6)], port,
            [Fingerabdruck.AusSpki([1]), Fingerabdruck.AusSpki([2])], []);

    /// <summary>Namensauflösung zum Mitschreiben: merkt die Fragen und liefert, was <see cref="Antwort"/> gerade sagt.</summary>
    private sealed class Aufloeser
    {
        public List<(string Name, AddressFamily Familie)> Fragen { get; } = [];

        public Func<IPAddress[]> Antwort { get; set; } = () => [IPAddress.Parse("2001:db8::7"), IPAddress.Parse("203.0.113.7")];

        public Task<IPAddress[]> Aufloesen(string name, AddressFamily familie, CancellationToken abbruch)
        {
            Fragen.Add((name, familie));
            return Task.FromResult(Antwort());
        }
    }

    [Test]
    public static async Task KernAufbau_V4AusDemProfil_FragtNurNachIPv4()
    {
        using var id = Geraeteidentitaet.Fluechtig();
        foreach (var (v4, v6, familie, erwartet) in new[]
                 {
                     (true, false, AddressFamily.InterNetwork, new[] { "203.0.113.7" }),
                     (false, true, AddressFamily.InterNetworkV6, new[] { "2001:db8::7" }),
                     (true, true, AddressFamily.Unspecified, new[] { "2001:db8::7", "203.0.113.7" }),
                 })
        {
            var dns = new Aufloeser();
            var aufbau = new TlsAufbau(Profil(v4, v6), id, new TlsAufbauOptionen { Aufloeser = dns.Aufloesen });
            var k = await aufbau.KandidatenAsync(CancellationToken.None);
            var was = (v4 ? "v4" : "") + (v6 ? "v6" : "");
            Pruefe.Gleich(("funk.example.org", familie), dns.Fragen.Single(), was + ": gefragte Familie");
            Pruefe.Gleich(string.Join(",", erwartet), string.Join(",", k.Select(x => x.Ip)), was + ": Adressen (IPv6 zuerst, RFC 8305)");
            Pruefe.Wahr(k.All(x => x.Host == "funk.example.org"), was + ": SNI der Name aus dem Profil");
            Pruefe.Gleich((ushort)8443, aufbau.Port, was + ": öffentlicher Port aus dem Profil");
        }

        // Ein Name im Feld Adresse übernimmt die Familien des Profils; eine IP-Adresse gilt, wie sie dasteht.
        var n = new Aufloeser();
        var mitName = new TlsAufbau(Profil(), id, new TlsAufbauOptionen { Host = "funk.heim.example", Aufloeser = n.Aufloesen });
        Pruefe.Gleich("203.0.113.7", (await mitName.KandidatenAsync(CancellationToken.None)).Single().Ip.ToString(), "Name statt Profil-Host: nur IPv4");
        Pruefe.Gleich(("funk.heim.example", AddressFamily.InterNetwork), n.Fragen.Single(), "nur A-Einträge");
        Pruefe.Gleich("funk.heim.example", mitName.Hosts().Single().Name, "SNI dann dieser Name");
        var ip = new Aufloeser();
        var mitIp = new TlsAufbau(Profil(), id, new TlsAufbauOptionen { Host = "192.168.1.250", Port = 9443, Aufloeser = ip.Aufloesen });
        Pruefe.Gleich((IPAddress.Parse("192.168.1.250"), "funk.example.org"), (await mitIp.KandidatenAsync(CancellationToken.None)).Single(), "IP: ohne DNS, SNI aus dem Profil");
        Pruefe.Gleich(0, ip.Fragen.Count, "für eine IP-Adresse keine Namensauflösung");
        Pruefe.Gleich((ushort)9443, mitIp.Port, "Port aus der Adresse");
        var mitV6 = new TlsAufbau(Profil(), id, new TlsAufbauOptionen { Host = "2001:db8::5" });
        Pruefe.Gleich("2001:db8::5", (await mitV6.KandidatenAsync(CancellationToken.None)).Single().Ip.ToString(), "ausdrücklich getippte IPv6 gilt");
    }

    [Test]
    public static async Task KernAufbau_DynDnsWechsel_NeueAdresseUndLetzteGuteAlsRueckfall()
    {
        using var id = Geraeteidentitaet.Fluechtig();
        var dns = new Aufloeser { Antwort = () => [IPAddress.Parse("203.0.113.7")] };
        var aufbau = new TlsAufbau(Profil(), id, new TlsAufbauOptionen { Aufloeser = dns.Aufloesen });
        var erste = (await aufbau.KandidatenAsync(CancellationToken.None)).Single();
        aufbau.Gelungen(erste);

        // Zwangstrennung der FRITZ!Box: neue IPv4, der DynDNS-Name zeigt schon darauf. Jeder Versuch löst neu auf (7.1 Schritt 1).
        dns.Antwort = () => [IPAddress.Parse("198.51.100.9")];
        var zweite = await aufbau.KandidatenAsync(CancellationToken.None);
        Pruefe.Gleich("198.51.100.9", zweite.Single().Ip.ToString(), "die neue Adresse, nicht die alte");
        aufbau.Gelungen(zweite.Single());

        // Die Auflösung scheitert (DNS gerade weg): die letzte gute Adresse (Schritt 3).
        dns.Antwort = () => throw new SocketException((int)SocketError.HostNotFound);
        Pruefe.Gleich("198.51.100.9", (await aufbau.KandidatenAsync(CancellationToken.None)).Single().Ip.ToString(), "letzte gute Adresse");
        dns.Antwort = () => [IPAddress.Parse("2001:db8::7")];
        Pruefe.Gleich("198.51.100.9", (await aufbau.KandidatenAsync(CancellationToken.None)).Single().Ip.ToString(), "nur AAAA beim /v4-Host zählt nicht");
        Pruefe.Wahr(dns.Fragen.All(f => f.Familie == AddressFamily.InterNetwork), "immer nur A-Einträge");

        // Ohne gute Adresse: der Grund je Host, in Worten.
        var neu = new TlsAufbau(Profil(), id, new TlsAufbauOptionen { Aufloeser = dns.Aufloesen });
        var e = await Wirft(() => neu.KandidatenAsync(CancellationToken.None));
        Pruefe.Wahr(e is AufbauException { Art: AufbauFehler.Netz } && e.Message == "funk.example.org hat keine IPv4-Adresse", "nur AAAA: " + e?.Message);
        dns.Antwort = () => throw new SocketException((int)SocketError.HostNotFound);
        e = await Wirft(() => neu.KandidatenAsync(CancellationToken.None));
        Pruefe.Wahr(e is AufbauException { Art: AufbauFehler.Netz } && e.Message == "funk.example.org ist im DNS nicht zu finden", "unbekannt: " + e?.Message);
        dns.Antwort = () => throw new ArgumentException("ungültiger Name");
        e = await Wirft(() => neu.KandidatenAsync(CancellationToken.None));
        Pruefe.Wahr(e is AufbauException { Art: AufbauFehler.Netz }, "ArgumentException des Betriebssystems wird zum Aufbaufehler, war " + e?.GetType().Name);
    }

    [Test]
    public static void KernAufbau_TcpFehlerInWorten()
    {
        var v4 = IPAddress.Parse("203.0.113.7");
        Pruefe.Gleich("203.0.113.7:8443 lehnt die Verbindung ab (kein Dienst auf dem Port oder keine Portfreigabe)",
            TlsAufbau.TcpText(v4, 8443, new SocketException((int)SocketError.ConnectionRefused)), "abgelehnt");
        Pruefe.Gleich("203.0.113.7:8443 antwortet nicht", TlsAufbau.TcpText(v4, 8443, new OperationCanceledException()), "Frist von 3 s");
        Pruefe.Gleich("203.0.113.7:8443 antwortet nicht", TlsAufbau.TcpText(v4, 8443, new SocketException((int)SocketError.TimedOut)), "Zeitüberschreitung");
        Pruefe.Gleich("[2001:db8::7]:8443 ist nicht erreichbar", TlsAufbau.TcpText(IPAddress.Parse("2001:db8::7"), 8443, new SocketException((int)SocketError.NetworkUnreachable)), "IPv6 in Klammern");
    }

    [Test]
    public static void KernIdentitaet_PersistierterSchluesselGehtDirektAnSchannel()
    {
        // Befund: Der Rückfall ohne TPM ist ein persistierter Software-Schlüssel mit ExportPolicy None. Der PKCS#12-Umweg
        // exportiert den privaten Schlüssel und wäre daran gescheitert. Persistiert heißt jetzt: direkt.
        Pruefe.Gleich(Geraeteidentitaet.TlsWeg.Direkt, Geraeteidentitaet.WegFuer(windows: true, persistiert: true), "TPM oder Software-KSP unter Windows");
        Pruefe.Gleich(Geraeteidentitaet.TlsWeg.Pkcs12Import, Geraeteidentitaet.WegFuer(windows: true, persistiert: false), "flüchtig unter Windows");
        Pruefe.Gleich(Geraeteidentitaet.TlsWeg.Direkt, Geraeteidentitaet.WegFuer(windows: false, persistiert: false), "Linux (funk-echo, Testclient)");
        using var fluechtig = Geraeteidentitaet.Fluechtig();
        Pruefe.Falsch(fluechtig.Persistiert, "Fluechtig ist nicht persistiert");
        // Ein flüchtiger CNG-Schlüssel ohne Namen gilt nicht als persistiert (Prüfung über CngKey.IsEphemeral). Angelegt
        // wie von ECDsa.Create unter Windows, ohne Namen und ohne CngSchluesselspeicher: nichts landet im Profil.
        using var cng = Geraeteidentitaet.Aus(new ECDsaCng(ECCurve.NamedCurves.nistP256), Protokoll.Steuerung.SchluesselArt.Software);
        Pruefe.Falsch(cng.Persistiert, "CNG ohne Namen: flüchtig, also PKCS#12-Weg (hier nicht ausgeführt)");
        // Der direkte Weg fasst keinen Schlüsselspeicher an: FuerTls liefert das Zertifikat selbst (hier über die Art TPM).
        using var tpm = Geraeteidentitaet.Aus(ECDsa.Create(ECCurve.NamedCurves.nistP256), Protokoll.Steuerung.SchluesselArt.Tpm);
        Pruefe.Wahr(tpm.Persistiert, "TPM gilt als persistiert");
        // Erst den Weg prüfen, dann FuerTls aufrufen (Gegenprüfung): Wiche der Weg je auf den Import aus, schriebe FuerTls den
        // Schlüssel in den Speicher des Benutzers. So bricht der Test vorher ab, statt ins Profil zu schreiben.
        Pruefe.Gleich(Geraeteidentitaet.TlsWeg.Direkt, Geraeteidentitaet.WegFuer(OperatingSystem.IsWindows(), tpm.Persistiert), "direkter Weg, bevor FuerTls läuft");
        Pruefe.Wahr(ReferenceEquals(tpm.Zertifikat, tpm.FuerTls()), "direkt, ohne Import");
    }

    [Test]
    public static async Task KernClient_ServerNichtVertrauenswuerdig_KeinStillerNeuversuch()
    {
        var versuche = 0;
        var art = AufbauFehler.PinAlarm;
        using var id = Geraeteidentitaet.Fluechtig();
        await using var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = new DelegatAufbau((_, _) =>
            {
                Interlocked.Increment(ref versuche);
                throw new AufbauException(art, "Pin passt nicht an 203.0.113.7:8443");
            }),
            Identitaet = id,
            Zufall = () => 0.0,
        });
        var enden = new List<Sitzungsende>();
        client.SitzungBeendet += (_, e) =>
        {
            lock (enden) enden.Add(e);
        };
        client.Starten();
        Pruefe.Wahr(await Warten(() => Volatile.Read(ref versuche) == 1 && client.NeuversuchGesperrt, 3_000), "erster Versuch, dann gesperrt");
        Pruefe.Wahr(await Warten(() => { lock (enden) return enden.Count == 1; }, 2_000), "Ende gemeldet");
        Sitzungsende ende;
        lock (enden) ende = enden[0];
        Pruefe.Gleich(AufbauFehler.PinAlarm, ende.Aufbau, "Art im Ende");
        Pruefe.Gleich("Pin passt nicht an 203.0.113.7:8443", ende.Einzelheit, "Einzelheit im Ende");
        // Der Backoff wäre nach 3,2 s (4 s mit Zufall 0) wieder dran; gesperrt bleibt es bei einem Versuch.
        await Task.Delay(3_600);
        Pruefe.Gleich(1, Volatile.Read(ref versuche), "kein stiller Neuversuch nach dem Alarm (4.1)");

        // Nur der Nutzer verbindet neu; ein Netzfehler sperrt nicht.
        art = AufbauFehler.Netz;
        client.NeuVerbinden();
        Pruefe.Wahr(await Warten(() => Volatile.Read(ref versuche) == 2, 2_000), "Verbinden vom Nutzer versucht es wieder");
        Pruefe.Wahr(await Warten(() => !client.NeuversuchGesperrt, 1_000), "nach einem Netzfehler nicht gesperrt");
    }

    private static async Task<bool> Warten(Func<bool> bedingung, int ms)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(10);
        }
        return true;
    }

    private static async Task<Exception?> Wirft(Func<Task> aktion)
    {
        try
        {
            await aktion();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}
