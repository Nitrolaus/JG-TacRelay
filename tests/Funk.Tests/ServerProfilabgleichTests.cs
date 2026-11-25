// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Funk.Protokoll.Identitaet;
using Funk.Server;
using Funk.Server.Betrieb;

namespace Funk.Tests;

/// <summary>
/// Abgleich von server.json und Serverprofil: Nennt profil.json einen anderen TCP-Port als <c>oeffentlicherTcpPort</c>, warnt
/// der Server beim Start im Journal und bei <c>funk-server pruefen</c>. Sarevocs Fall: die Beispielkonfiguration mit 443, das Profil
/// mit 8443, weil 443 an der FRITZ!Box schon nginx gehört. Ohne TLS und ohne Schlüsselspeicher (schannelTauglich: false).
/// </summary>
internal static class ServerProfilabgleichTests
{
    private static ServerKonfiguration Konfiguration(string verzeichnis, int? oeffentlich, string profil = "profil.json") => new()
    {
        Binden = ["127.0.0.1"],
        TcpPort = 0,
        UdpPort = 0,
        OeffentlicherTcpPort = oeffentlich,
        Zustandsverzeichnis = Path.Combine(verzeichnis, "zustand-" + oeffentlich),
        Profil = Path.Combine(verzeichnis, profil),
        VerwaltungsSocket = Path.Combine(verzeichnis, "v-" + oeffentlich + ".sock"),
        Log = new ProtokollKonfiguration { Stufe = "DEBUG" },
        Test = new TestKonfiguration { ZufallsPorts = true },
        Basisverzeichnis = verzeichnis,
    };

    /// <summary>Ein signiertes Profil mit dem Pin des Servers und <paramref name="port"/>; der Profilschlüssel lebt nur im Speicher.</summary>
    private static void ProfilSchreiben(string pfad, Fingerabdruck pin, ushort port)
    {
        using var profilSchluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var reserve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jetzt = DateTimeOffset.UtcNow;
        var profil = new Serverprofil("Meine Staffel", 1, jetzt.ToUnixTimeMilliseconds(), jetzt.AddDays(30).ToUnixTimeMilliseconds(),
            [new ProfilHost("funk.example.org", true, false)], port, [pin, Fingerabdruck.AusSchluessel(reserve)], []);
        File.WriteAllBytes(pfad, SigniertesProfil.Signieren(profil.AlsJson(), profilSchluessel).AlsJson());
    }

    [Test]
    public static void PortWarnung_NurWennProfilUndServerJsonAuseinanderliegen()
    {
        var jus = new ServerKonfiguration { Binden = ["127.0.0.1"], TcpPort = 8443, OeffentlicherTcpPort = 443, Profil = "p.json" };
        var w = Profilabgleich.PortWarnung(jus, 8443);
        Pruefe.Wahr(w is not null && w.Contains("TCP-Port 8443", StringComparison.Ordinal) && w.Contains("oeffentlicherTcpPort 443", StringComparison.Ordinal)
            && w.Contains("\"oeffentlicherTcpPort\": 8443", StringComparison.Ordinal) && w.Contains("--tcp-port", StringComparison.Ordinal), "Sarevocs Fall: " + w);
        Pruefe.Falsch(w!.Any(z => z is '\u2013' or '\u2014' or '\u201E' or '\u201C' or '\u201D' or '\u2026'), "nur einfache Zeichen");
        Pruefe.Gleich(null, Profilabgleich.PortWarnung(jus, 443), "gleicher Port: nichts");

        var ohne = new ServerKonfiguration { Binden = ["127.0.0.1"], TcpPort = 8443, Profil = "p.json" };
        Pruefe.Gleich(null, Profilabgleich.PortWarnung(ohne, 8443), "ohne oeffentlicherTcpPort gilt tcpPort");
        Pruefe.Wahr(Profilabgleich.PortWarnung(ohne, 9443) is { } v && v.Contains("tcpPort (ohne oeffentlicherTcpPort) 8443", StringComparison.Ordinal),
            "sagt, woher der Wert kommt");
        var zugeteilt = new ServerKonfiguration { Binden = ["127.0.0.1"], TcpPort = 0, Profil = "p.json", Test = new TestKonfiguration { ZufallsPorts = true } };
        Pruefe.Gleich(null, Profilabgleich.PortWarnung(zugeteilt, 443), "zugeteilte Ports (Tests): nichts");
    }

    [Test]
    public static async Task Start_UndPruefen_WarnenBeiAnderemTcpPort()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "profilabgleich-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(verzeichnis);
        try
        {
            using var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var zert = new CertificateRequest("CN=Funk-Server", schluessel, HashAlgorithmName.SHA256)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddYears(1));
            ProfilSchreiben(Path.Combine(verzeichnis, "profil.json"), Fingerabdruck.AusZertifikat(zert), 8443);

            // funk-server pruefen liest das Profil wie der Start.
            var (fehler, warnungen) = Profilabgleich.Pruefen(Konfiguration(verzeichnis, 443));
            Pruefe.Gleich(null, fehler, "Profil lesbar");
            Pruefe.Wahr(warnungen.Count == 1 && warnungen[0].Contains("TCP-Port 8443", StringComparison.Ordinal), "eine Warnung: " + string.Join(" | ", warnungen));
            Pruefe.Gleich(0, Profilabgleich.Pruefen(Konfiguration(verzeichnis, 8443)).Warnungen.Count, "passend: keine Warnung");
            Pruefe.Wahr(Profilabgleich.Pruefen(Konfiguration(verzeichnis, 443, "fehlt.json")).Fehler is { } f1 && f1.Contains("Profildatei fehlt", StringComparison.Ordinal),
                "ohne Datei startet der Server nicht");
            File.WriteAllText(Path.Combine(verzeichnis, "kaputt.json"), "{}");
            Pruefe.Wahr(Profilabgleich.Pruefen(Konfiguration(verzeichnis, 443, "kaputt.json")).Fehler is { } f2 && f2.Contains("kein gültiges", StringComparison.Ordinal),
                "kein signiertes Profil");

            // Start: Die Warnung steht im Journal, genau einmal, und nur bei abweichendem Port.
            foreach (var (oeffentlich, erwartet) in new[] { (443, 1), (8443, 0) })
            {
                var zeilen = new ConcurrentQueue<string>();
                var dienst = Dienst.Starten(Konfiguration(verzeichnis, oeffentlich), null, ServerIdentitaet.AusZertifikat(zert, schannelTauglich: false),
                    Geheimnis.Zufaellig(), zeilen.Enqueue, totpSchluessel: RandomNumberGenerator.GetBytes(TotpSchluessel.Laenge));
                await dienst.DisposeAsync();
                var treffer = zeilen.Where(z => z.Contains(" WARNUNG ", StringComparison.Ordinal) && z.Contains("TCP-Port 8443", StringComparison.Ordinal)).ToList();
                Pruefe.Gleich(erwartet, treffer.Count, "oeffentlicherTcpPort " + oeffentlich + ": " + string.Join(" | ", treffer));
                Pruefe.Falsch(zeilen.Any(z => z.Contains("Pin des Serverschlüssels", StringComparison.Ordinal)), "der Pin passt (Gegenprobe der Pin-Warnung)");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(verzeichnis, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
