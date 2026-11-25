// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Funk.Protokoll.Identitaet;
using Funk.Server;
using Funk.Server.Betrieb;
using Schluessel = Funk.Tests.Oberflaeche.OberflaecheProfilTests.Schluessel;

namespace Funk.Tests;

/// <summary>
/// 0.4.0, offener Punkt der Gegenprüfung "Einrichten und Umzug": Der Server warnt, wenn das geladene Serverprofil seinen eigenen
/// Pin nicht nennt, beim Start wie bei <c>profil-neu-laden</c>, jeweils mit Version und Kennung. Bis 0.3.0 warnte nur der Start,
/// und <c>profil-neu-laden</c> antwortete "Profil geladen", auch wenn Sarevoc aus Versehen das Abschlussprofil eines Umzugs (Version 3,
/// ohne seinen Server) auf den alten Server legte (UMZUG.md, Schritt 4). Clients ab 0.4.0 übernehmen so ein Profil nicht
/// (<c>TlsAufbau.LetzterPin</c>); ein Setup mit diesem Profil fände den Server aber nie.
/// </summary>
internal static class ProfilPinWarnungTests
{
    private static readonly Fingerabdruck Fremd = Fingerabdruck.AusSpki([7]);
    private static readonly Fingerabdruck Reserve = Fingerabdruck.AusSpki([8]);

    [Test]
    public static async Task ProfilNeuLaden_WarntOhneEigenenPin()
    {
        using var k = new Schluessel();
        await using var t = await Testserver.StartenAsync();
        var pfad = Path.Combine(t.Verzeichnis, "profil.json");
        var pin = t.Dienst.Pin;

        File.WriteAllBytes(pfad, ProfiluebernahmeTests.Signiert(k, 3, ["funk.example.org"], [Fremd, Reserve]));
        var a = await t.AdminOk("profil-neu-laden");
        Pruefe.Wahr(a.Text.StartsWith("Profil Version 3 geladen.", StringComparison.Ordinal), "die Antwort nennt die Version: " + a.Text);
        Pruefe.Wahr(a.Text.Contains("WARNUNG", StringComparison.Ordinal) && a.Text.Contains(pin.Anzeige, StringComparison.Ordinal),
            "die Antwort warnt mit der Kennung dieses Servers: " + a.Text);
        Pruefe.Wahr(t.Logbuch.Any(z => z.Contains("steht nicht im Serverprofil Version 3", StringComparison.Ordinal)),
            "dieselbe Warnung im Logbuch: " + string.Join(" | ", t.Logbuch.TakeLast(5)));

        // Die Anleitungen zitieren die Warnung (HEIMSERVER-PROXMOX.md, Abschnitt 7; UMZUG.md, Schritt 4): bis zum Doppelpunkt so,
        // wie sie im Journal steht, mit XXXX für die Kennung.
        var echt = t.Logbuch.Last(z => z.Contains("steht nicht im Serverprofil", StringComparison.Ordinal));
        echt = echt[echt.IndexOf("WARNUNG ", StringComparison.Ordinal)..].Replace(pin.Anzeige, "XXXX-XXXX-XXXX-XXXX", StringComparison.Ordinal);
        var kopf = Regex.Replace(echt[..(echt.IndexOf(": ", StringComparison.Ordinal) + 1)], @"Version \d+", "Version N", RegexOptions.None, TimeSpan.FromSeconds(1));
        foreach (var datei in new[] { "HEIMSERVER-PROXMOX.md", "UMZUG.md" })
        {
            var text = Regex.Replace(File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "docs", datei)), @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
            var m = Regex.Match(text, @"`(WARNUNG Der Pin des Serverschlüssels [^`]*?Version \d+:)", RegexOptions.None, TimeSpan.FromSeconds(1));
            Pruefe.Wahr(m.Success, datei + " zitiert die Warnung");
            Pruefe.Gleich(kopf, Regex.Replace(m.Groups[1].Value, @"Version \d+", "Version N", RegexOptions.None, TimeSpan.FromSeconds(1)), datei + ": Zitat wie im Journal");
        }

        // Das Übergangsprofil mit dem eigenen Pin: keine Warnung.
        File.WriteAllBytes(pfad, ProfiluebernahmeTests.Signiert(k, 2, ["heim.example.org", "funk.example.org"], [pin, Fremd, Reserve]));
        a = await t.AdminOk("profil-neu-laden");
        Pruefe.Gleich("Profil Version 2 geladen.", a.Text, "mit dem eigenen Pin ohne Warnung");
    }

    /// <summary>
    /// Befund der Prüfung der Pakete 0.4.0 (mittel): profil-neu-laden warnte nur beim fehlenden Pin, nicht bei einem anderen
    /// TCP-Port. Ein Übergangsprofil mit Port 443 (der Server-Admin hatte beim Einrichten 443 genommen) lud Sarevocs Server ohne ein Wort, und
    /// jeder Client 0.4.0 übernahm es. Jetzt kommt dieselbe Warnung wie beim Start und bei <c>pruefen</c>
    /// (<see cref="Profilabgleich.PortWarnung"/>), in der Antwort und im Journal; geladen ist das Profil trotzdem, wie beim Start.
    /// </summary>
    [Test]
    public static async Task ProfilNeuLaden_WarntBeiAnderemTcpPort()
    {
        using var k = new Schluessel();
        await using var t = await Testserver.StartenAsync();
        await t.DienstBeendenAsync();

        using var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var zert = new CertificateRequest("CN=Funk-Server", schluessel, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pin = Fingerabdruck.AusZertifikat(zert);
        var pfad = Path.Combine(t.Verzeichnis, "profil.json");
        File.WriteAllBytes(pfad, ProfiluebernahmeTests.Signiert(k, 1, ["heim.example.org"], [pin, Reserve], port: 8443));

        // Wie bei Sarevoc: Die Clients erreichen den Server unter 8443 (server.json: tcpPort 8443, hier über oeffentlicherTcpPort,
        // weil der Testserver einen zugeteilten Port bindet).
        var a = t.Dienst.Konfiguration;
        var konfig = new ServerKonfiguration
        {
            Binden = a.Binden,
            TcpPort = a.TcpPort,
            UdpPort = a.UdpPort,
            OeffentlicherTcpPort = 8443,
            Zustandsverzeichnis = a.Zustandsverzeichnis,
            Profil = a.Profil,
            VerwaltungsSocket = a.VerwaltungsSocket,
            MindestClientVersion = a.MindestClientVersion,
            Grenzen = a.Grenzen,
            Log = a.Log,
            Test = a.Test,
            Mitgliedskarte = a.Mitgliedskarte,
            Sperrliste = a.Sperrliste,
            Basisverzeichnis = a.Basisverzeichnis,
        };
        var log = new ConcurrentQueue<string>();
        await using var d = Dienst.Starten(konfig, null, ServerIdentitaet.AusZertifikat(zert, schannelTauglich: false), Geheimnis.Zufaellig(),
            log.Enqueue, null, t.TotpSchluessel.ToArray());
        Pruefe.Falsch(log.Any(z => z.Contains("nennt TCP-Port", StringComparison.Ordinal)), "beim Start mit 8443 keine Portwarnung");

        // Version 2 mit Port 443: geladen, aber mit Warnung in Antwort und Journal.
        File.WriteAllBytes(pfad, ProfiluebernahmeTests.Signiert(k, 2, ["heim.example.org", "funk.example.org"], [pin, Fremd, Reserve], port: 443));
        var antwort = await Funk.Server.Verwaltung.Verwaltungsklient.SendenAsync(d.Verwaltungspfad, "profil-neu-laden", [], TimeSpan.FromSeconds(60));
        Pruefe.Wahr(antwort.Ok && antwort.Text.StartsWith("Profil Version 2 geladen.", StringComparison.Ordinal), "geladen: " + antwort.Text);
        Pruefe.Wahr(antwort.Text.Contains("\nWARNUNG Das Serverprofil nennt TCP-Port 443", StringComparison.Ordinal), "die Antwort warnt vor dem Port: " + antwort.Text);
        Pruefe.Wahr(log.Any(z => z.Contains("Das Serverprofil nennt TCP-Port 443", StringComparison.Ordinal)), "dieselbe Warnung im Journal");
        Pruefe.Falsch(antwort.Text.Contains("steht nicht im Serverprofil", StringComparison.Ordinal), "der eigene Pin steht drin: " + antwort.Text);

        // Zurück auf eine Fassung mit 8443: ohne Warnung.
        File.WriteAllBytes(pfad, ProfiluebernahmeTests.Signiert(k, 3, ["heim.example.org", "funk.example.org"], [pin, Fremd, Reserve], port: 8443));
        antwort = await Funk.Server.Verwaltung.Verwaltungsklient.SendenAsync(d.Verwaltungspfad, "profil-neu-laden", [], TimeSpan.FromSeconds(60));
        Pruefe.Gleich("Profil Version 3 geladen.", antwort.Text, "mit 8443 ohne Warnung");
    }

    [Test]
    public static async Task Start_WarntOhneEigenenPin_MitVersionUndKennung()
    {
        using var k = new Schluessel();
        await using var t = await Testserver.StartenAsync();
        await t.DienstBeendenAsync();
        File.WriteAllBytes(Path.Combine(t.Verzeichnis, "profil.json"), ProfiluebernahmeTests.Signiert(k, 3, ["funk.example.org"], [Fremd, Reserve]));

        using var schluessel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var zert = new CertificateRequest("CN=Funk-Server", schluessel, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddYears(1));
        var log = new ConcurrentQueue<string>();
        await using var d = Dienst.Starten(t.Dienst.Konfiguration, null, ServerIdentitaet.AusZertifikat(zert, schannelTauglich: false), Geheimnis.Zufaellig(),
            log.Enqueue, null, t.TotpSchluessel.ToArray());
        Pruefe.Wahr(log.Any(z => z.Contains("steht nicht im Serverprofil Version 3", StringComparison.Ordinal) && z.Contains(d.Pin.Anzeige, StringComparison.Ordinal)),
            "Warnung beim Start mit Version und Kennung: " + string.Join(" | ", log.Where(z => z.Contains("WARN", StringComparison.Ordinal))));
        // Bis 0.4.0 lud der Start das Profil zweimal, und das Journal nannte es doppelt (Übergangsmessung 01.10.2026).
        Pruefe.Gleich(1, log.Count(z => z.Contains("Serverprofil Version 3 mit 2 Pins geladen", StringComparison.Ordinal)), "das Profil einmal geladen");
    }
}
