// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Funk.Protokoll.Identitaet;
using Funk.Server.Betrieb;
using Funk.Server.Kern;
using Funk.Server.Netzzugang;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>Grundschicht des Dienstes: Konfiguration, Zulassung (4.8), Vorrangspur, Schwärzung (7.5), Ablage (7.2).</summary>
internal static class ServerGrundTests
{
    private static string RepoWurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new PruefungException("Repository nicht gefunden");
    }

    private static ServerKonfiguration Konfig(string json)
    {
        var pfad = Path.Combine(Testserver.Wurzel(), "konfig-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        Directory.CreateDirectory(Testserver.Wurzel());
        File.WriteAllText(pfad, json);
        try
        {
            return ServerKonfiguration.Laden(pfad);
        }
        finally
        {
            File.Delete(pfad);
        }
    }

    [Test]
    public static void Konfig_BeispielDateiIstGueltigUndOhneGeheimnisse()
    {
        var pfad = Path.Combine(RepoWurzel(), "konfig", "server.beispiel.json");
        var text = File.ReadAllText(pfad);
        Pruefe.Falsch(text.Contains("PRIVATE KEY", StringComparison.Ordinal), "Beispiel enthält keinen Schlüssel");
        var k = ServerKonfiguration.Laden(pfad);
        Pruefe.Gleich(0, k.Pruefen().Count, "Beispielkonfiguration prüft ohne Fehler: " + string.Join("; ", k.Pruefen()));
        Pruefe.Gleich(443, k.TcpPortOeffentlich, "öffentlicher TCP-Port");
        Pruefe.Gleich(8443, k.TcpPort, "TCP-Bindport");
        Pruefe.Gleich(47443, k.UdpPortOeffentlich, "öffentlicher UDP-Port");
        Pruefe.Gleich(1, k.Bindadressen().Count, "eine Bindadresse");
        // Stufe 1 (3.9, 6.13): TOTP-Schlüssel aus dem Credential, das die Unit auch lädt; Mitgliedskarte mit Voreinstellung.
        Pruefe.Wahr(k.TotpSchluessel.Contains("funk-totp-schluessel", StringComparison.Ordinal), "totpSchluessel aus $CREDENTIALS_DIRECTORY: " + k.TotpSchluessel);
        Pruefe.Wahr(k.Mitgliedskarte.RollenFuerAlle, "rollenFuerAlle wie vorgeschlagen");
        var unit = File.ReadAllText(Path.Combine(RepoWurzel(), "konfig", "funk-server.service"));
        Pruefe.Wahr(unit.Contains("LoadCredential=funk-totp-schluessel:", StringComparison.Ordinal), "Unit lädt funk-totp-schluessel");
    }

    [Test]
    public static void Konfig_OhneBindadresseStartetNicht()
    {
        var k = Konfig("""{"binden": [], "profil": "p.json"}""");
        Pruefe.Wahr(k.Pruefen().Any(f => f.Contains("Keine Bindadresse", StringComparison.Ordinal)), "leere Liste ist ein Fehler (4.1)");
    }

    [Test]
    public static void Konfig_AlleAdressenNurAusdruecklich()
    {
        var k = Konfig("""{"binden": ["0.0.0.0"], "profil": "p.json"}""");
        Pruefe.Wahr(k.Pruefen().Any(f => f.Contains("alleAdressen", StringComparison.Ordinal)), "0.0.0.0 in binden ist ein Fehler");
        var k2 = Konfig("""{"alleAdressen": true, "profil": "p.json", "totpSchluessel": "t.bin"}""");
        Pruefe.Gleich(0, k2.Pruefen().Count, "alleAdressen = true ist erlaubt: " + string.Join("; ", k2.Pruefen()));
        Pruefe.Gleich(2, k2.Bindadressen().Count, "IPv4 und IPv6");
        var k3 = Konfig("""{"alleAdressen": true, "binden": ["127.0.0.1"], "profil": "p.json"}""");
        Pruefe.Wahr(k3.Pruefen().Count > 0, "beides zugleich ist ein Fehler");
    }

    [Test]
    public static void Konfig_UnbekanntesFeldUndFalscherPortSindFehler()
    {
        Pruefe.Wirft<InvalidDataException>(() => Konfig("""{"binden": ["127.0.0.1"], "tcpPrt": 1}"""), "Tippfehler im Feldnamen");
        var k = Konfig("""{"binden": ["127.0.0.1"], "tcpPort": 70000, "profil": "p.json"}""");
        Pruefe.Wahr(k.Pruefen().Any(f => f.Contains("tcpPort", StringComparison.Ordinal)), "Port außerhalb 1-65535");
        var k2 = Konfig("""{"binden": ["127.0.0.1"], "tcpPort": 0, "profil": "p.json"}""");
        Pruefe.Wahr(k2.Pruefen().Count > 0, "Port 0 nur im Testmodus");
        var k3 = Konfig("""{"binden": ["127.0.0.1"]}""");
        Pruefe.Wahr(k3.Pruefen().Any(f => f.Contains("profil", StringComparison.Ordinal)), "Profil ist Pflicht");
    }

    /// <summary>
    /// Übergang von 0.1.0 (Heimserver seit 30.09.2026): Die server.json von damals kennt <c>totpSchluessel</c> nicht. Statt still
    /// den Standardpfad zu nehmen und beim Start an einer fehlenden Datei zu scheitern, sagt schon <c>pruefen</c>, was fehlt, mit
    /// den drei Schritten. Eine server.json von 0.1.0 hat sonst keine Felder, die 0.2.0 nicht kennt.
    /// </summary>
    [Test]
    public static void Konfig_ServerJsonVon010_PruefenNenntDenTotpSchluessel()
    {
        var alt = Konfig("""
            {
              // wie konfig\server.beispiel.json im Stand 5053590 (0.1.0)
              "binden": ["192.168.1.250"], "alleAdressen": false, "tcpPort": 8443, "oeffentlicherTcpPort": 8443,
              "udpPort": 47443, "oeffentlicherUdpPort": 47443, "nurTls13": false, "zustandsverzeichnis": "/var/lib/funk",
              "identitaet": { "zertifikat": "${CREDENTIALS_DIRECTORY}/server.crt", "schluessel": "${CREDENTIALS_DIRECTORY}/server.key" },
              "geheimnis": "${CREDENTIALS_DIRECTORY}/funk-geheimnis", "profil": "/etc/funk/profil.json", "mindestClientVersion": "0.1.0",
              "verwaltungsSocket": "/run/funk/verwaltung.sock", "verwaltungsUids": [], "vertrauenswuerdigeWeiterleiter": [],
              "grenzen": { "ausgangMbit": 35 }, "log": { "stufe": "INFO", "datei": null }
            }
            """);
        var fehler = alt.Pruefen();
        Pruefe.Gleich(1, fehler.Count, "genau ein Fehler: " + string.Join("; ", fehler));
        Pruefe.Wahr(fehler[0].Contains("totpSchluessel", StringComparison.Ordinal) && fehler[0].Contains("totp-schluessel-erzeugen", StringComparison.Ordinal)
            && fehler[0].Contains("${CREDENTIALS_DIRECTORY}/funk-totp-schluessel", StringComparison.Ordinal) && fehler[0].Contains("LoadCredential", StringComparison.Ordinal),
            "Klartext mit den drei Schritten: " + fehler[0]);
        var neu = Konfig("""{"binden": ["127.0.0.1"], "profil": "p.json", "totpSchluessel": "${CREDENTIALS_DIRECTORY}/funk-totp-schluessel"}""");
        Pruefe.Gleich(0, neu.Pruefen().Count, "mit dem Feld in Ordnung");
        Pruefe.Gleich(0, new ServerKonfiguration { Binden = ["127.0.0.1"], Profil = "p.json" }.Pruefen().Count, "im Code gebaut (Tests, Probefunk): Standardpfad");
    }

    [Test]
    public static void Konfig_PfadeMitUmgebungsvariableUndRelativ()
    {
        Environment.SetEnvironmentVariable("FUNK_TEST_CRED", Path.Combine(Testserver.Wurzel(), "cred"));
        try
        {
            var k = Konfig("""{"binden": ["127.0.0.1"], "profil": "p.json", "geheimnis": "${FUNK_TEST_CRED}/g"}""");
            Pruefe.Gleich(Path.GetFullPath(Path.Combine(Testserver.Wurzel(), "cred", "g")), k.Aufloesen(k.Geheimnis), "Variable ersetzt");
            Pruefe.Gleich(Path.GetFullPath(Path.Combine(Testserver.Wurzel(), "p.json")), k.Aufloesen(k.Profil!), "relativ zur Konfigurationsdatei");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FUNK_TEST_CRED", null);
        }
    }

    // ---- Adressen und Präfixe (Abschnitt 0) ----

    [Test]
    public static void Adresse_Ipv4AbgebildetUndPraefix64()
    {
        var a = Adresse.Aus(IPAddress.Parse("::ffff:203.0.113.7"), 5000);
        var b = Adresse.Aus(IPAddress.Parse("203.0.113.7"), 5000);
        Pruefe.Gleich(b, a, "IPv4 in IPv6-Form wird auf IPv4 zurückgeführt");
        var x = Adresse.Aus(IPAddress.Parse("2001:db8:1:2::1"), 1);
        var y = Adresse.Aus(IPAddress.Parse("2001:db8:1:2:ffff::9"), 2);
        var z = Adresse.Aus(IPAddress.Parse("2001:db8:1:3::1"), 1);
        Pruefe.Gleich(x.Praefix, y.Praefix, "gleiches /64");
        Pruefe.Falsch(x.Praefix == z.Praefix, "anderes /64");
        Pruefe.Falsch(b.Praefix == Adresse.Aus(IPAddress.Parse("203.0.113.8"), 1).Praefix, "IPv4: jede Adresse eigenes Präfix");

        var sa = new IPEndPoint(IPAddress.Parse("198.51.100.20"), 47443).Serialize();
        Pruefe.Wahr(Adresse.TryAus(sa, out var ausSocket), "SocketAddress lesbar");
        Pruefe.Gleich(Adresse.Aus(IPAddress.Parse("198.51.100.20"), 47443), ausSocket, "SocketAddress IPv4");
        var sa6 = new IPEndPoint(IPAddress.Parse("2001:db8::5"), 9).Serialize();
        Pruefe.Wahr(Adresse.TryAus(sa6, out var ausSocket6), "SocketAddress v6 lesbar");
        Pruefe.Gleich(Adresse.Aus(IPAddress.Parse("2001:db8::5"), 9), ausSocket6, "SocketAddress IPv6");
        Pruefe.Falsch(a.ToString().Contains("203.0.113.7", StringComparison.Ordinal), "ToString zeigt keine Klartext-IP (P-46)");
    }

    // ---- Zulassung (4.8, Prüfbericht zu 4.8) ----

    private static Zulassung NeueZulassung(out Vorrangspur spur, Action<GrenzenKonfiguration>? anpassen = null, IEnumerable<IPAddress>? weiterleiter = null)
    {
        spur = new Vorrangspur(RandomNumberGenerator.GetBytes(32));
        var g = new GrenzenKonfiguration();
        anpassen?.Invoke(g);
        return new Zulassung(g, spur, weiterleiter, 0);
    }

    [Test]
    public static void Zulassung_HandshakesJePraefix_AnderePraefixeUnberuehrt()
    {
        var z = NeueZulassung(out _);
        var angreifer = Adresse.Aus(IPAddress.Parse("203.0.113.66"), 40000);
        var mitglied = Adresse.Aus(IPAddress.Parse("198.51.100.10"), 40000);
        for (var i = 0; i < 5; i++)
        {
            Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(angreifer, 0), "Stoß 5 je Präfix");
            z.TcpBeendet(angreifer);
        }
        Pruefe.Gleich(Zulassungsurteil.PraefixTopfLeer, z.TcpPruefen(angreifer, 0), "sechster Handshake in derselben Minute");
        Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(mitglied, 0), "anderes Präfix ist nicht betroffen (keine globale Sperre)");
        z.TcpBeendet(mitglied);
        Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(angreifer, 6_100), "nach 6 s ist eine Marke nachgelaufen (10 je Minute)");
    }

    [Test]
    public static void Zulassung_Ipv6ZaehltJe64()
    {
        var z = NeueZulassung(out _);
        for (var i = 0; i < 5; i++)
        {
            var a = Adresse.Aus(IPAddress.Parse("2001:db8:aa:bb::" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), 1);
            Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(a, 0), "Adresse " + i);
            z.TcpBeendet(a);
        }
        Pruefe.Gleich(Zulassungsurteil.PraefixTopfLeer, z.TcpPruefen(Adresse.Aus(IPAddress.Parse("2001:db8:aa:bb::99"), 1), 0),
            "wechselnde Adressen im selben /64 teilen sich den Topf");
        Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(Adresse.Aus(IPAddress.Parse("2001:db8:aa:bc::1"), 1), 0), "anderes /64");
    }

    [Test]
    public static void Zulassung_VorrangspurGehtAnGlobalemTopfVorbei()
    {
        var spur = new Vorrangspur(RandomNumberGenerator.GetBytes(32));
        var z2 = new Zulassung(new GrenzenKonfiguration { HandshakesGlobalJeSekunde = 3 }, spur, null, 0);
        var mitglied = Adresse.Aus(IPAddress.Parse("198.51.100.10"), 1);
        spur.Eintragen(mitglied.Praefix, Zeit.UnixMs());
        // Eine Flut Unbekannter leert den globalen CPU-Schutztopf ...
        for (var i = 0; i < 3; i++) Pruefe.Gleich(Zulassungsurteil.Zugelassen, z2.TcpPruefen(Adresse.Aus(IPAddress.Parse("203.0.113." + (i + 1)), 1), 0), "unbekannt " + i);
        Pruefe.Gleich(Zulassungsurteil.GlobalTopfLeer, z2.TcpPruefen(Adresse.Aus(IPAddress.Parse("203.0.113.50"), 1), 0), "globaler Topf leer");
        // ... das Mitglied aus der Vorrangspur kommt trotzdem herein, und mit größerem Stoß (10).
        for (var i = 0; i < 8; i++)
        {
            Pruefe.Gleich(Zulassungsurteil.Zugelassen, z2.TcpPruefen(mitglied, 0), "Vorrangspur " + i);
            z2.TcpBeendet(mitglied);
        }
    }

    [Test]
    public static void Zulassung_GleichzeitigeVerbindungenJePraefix()
    {
        var z = NeueZulassung(out var spur);
        var nat = Adresse.Aus(IPAddress.Parse("198.51.100.77"), 1);
        spur.Eintragen(nat.Praefix, Zeit.UnixMs());
        for (var i = 0; i < 8; i++) Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(nat, 0), "Verbindung " + i);
        Pruefe.Gleich(Zulassungsurteil.ZuVieleVerbindungen, z.TcpPruefen(nat, 0), "neunte gleichzeitige Verbindung");
        z.TcpBeendet(nat);
        Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(nat, 0), "nach dem Ende einer Verbindung wieder frei");
    }

    [Test]
    public static void Zulassung_UnfreigegebeneJePraefixUndGlobal()
    {
        var z = NeueZulassung(out _);
        var a = Adresse.Aus(IPAddress.Parse("203.0.113.1"), 1);
        Pruefe.Wahr(z.UnbekanntBelegen(a), "erste unbekannte");
        Pruefe.Wahr(z.UnbekanntBelegen(a), "zweite unbekannte");
        Pruefe.Falsch(z.UnbekanntBelegen(a), "dritte je Präfix");
        for (var i = 2; i < 20; i++) Pruefe.Wahr(z.UnbekanntBelegen(Adresse.Aus(IPAddress.Parse("203.0.113." + i), 1)), "global " + i);
        Pruefe.Gleich(20, z.UnbekannteVerbindungen, "20 belegt");
        Pruefe.Falsch(z.UnbekanntBelegen(Adresse.Aus(IPAddress.Parse("198.51.100.1"), 1)), "global 20 erreicht");
        // Freigegebene Geräte belegen nie einen dieser Plätze; die TCP-Zulassung bleibt davon unberührt.
        var mitglied = Adresse.Aus(IPAddress.Parse("198.51.100.2"), 1);
        Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(mitglied, 0), "bekannte Geräte kommen trotz voller Unbekannten-Plätze an");
        z.UnbekanntFreigeben(a);
        Pruefe.Wahr(z.UnbekanntBelegen(Adresse.Aus(IPAddress.Parse("198.51.100.1"), 1)), "nach Freigabe wieder ein Platz");
        z.Ueberlast = true;
        z.UnbekanntFreigeben(a);
        Pruefe.Falsch(z.UnbekanntBelegen(a), "Überlaststufe 2: keine neuen Unfreigegebenen (7.4)");
    }

    [Test]
    public static void Zulassung_GeraetesperreTrifftFingerabdruckNichtIp()
    {
        var z = NeueZulassung(out _);
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var g1 = Fingerabdruck.AusSchluessel(k1);
        var g2 = Fingerabdruck.AusSchluessel(k2);
        z.GeraetSperren(g1, 1000, 60_000);
        Pruefe.Wahr(z.GeraetGesperrt(g1, 30_000), "Gerät gesperrt");
        Pruefe.Falsch(z.GeraetGesperrt(g2, 30_000), "anderes Gerät derselben IP nicht");
        Pruefe.Falsch(z.GeraetGesperrt(g1, 61_000), "nach 60 s frei");
    }

    [Test]
    public static void Zulassung_WeiterleiterNurGlobal()
    {
        var z = NeueZulassung(out _, null, [IPAddress.Parse("10.0.0.1")]);
        var vorposten = Adresse.Aus(IPAddress.Parse("10.0.0.1"), 5555);
        for (var i = 0; i < 30; i++) Pruefe.Gleich(Zulassungsurteil.Zugelassen, z.TcpPruefen(vorposten, 0), "Weiterleiter " + i);
        for (var i = 0; i < 5; i++) Pruefe.Wahr(z.UnbekanntBelegen(vorposten), "keine Grenze je Präfix für den Weiterleiter " + i);
    }

    // ---- Vorrangspur ----

    [Test]
    public static void Vorrangspur_HmacUnd14TageUndNeustart()
    {
        var schluessel = RandomNumberGenerator.GetBytes(32);
        var spur = new Vorrangspur(schluessel);
        var p = Adresse.Aus(IPAddress.Parse("198.51.100.10"), 1).Praefix;
        var jetzt = Zeit.UnixMs();
        spur.Eintragen(p, jetzt);
        var json = spur.AlsJson();
        Pruefe.Falsch(Encoding.UTF8.GetString(json).Contains("198.51.100", StringComparison.Ordinal), "keine Klartext-Adresse in der Ablage");
        var neu = new Vorrangspur(schluessel);
        neu.AusJsonLaden(json, jetzt);
        Pruefe.Wahr(neu.Enthaelt(p, jetzt), "übersteht den Neustart");
        Pruefe.Falsch(neu.Enthaelt(p, jetzt + Vorrangspur.HaltedauerMs), "nach 14 Tagen nicht mehr");
        var fremd = new Vorrangspur(RandomNumberGenerator.GetBytes(32));
        fremd.AusJsonLaden(json, jetzt);
        Pruefe.Falsch(fremd.Enthaelt(p, jetzt), "mit anderem Schlüssel nicht zuzuordnen");
        for (var i = 0; i < Vorrangspur.MaxEintraege + 10; i++)
            spur.Eintragen(Adresse.Aus(new IPAddress(i + 1), 1).Praefix, jetzt + i);
        Pruefe.Gleich(Vorrangspur.MaxEintraege, spur.Anzahl, "höchstens 2000");
    }

    // ---- Schwärzung (7.5, P-46) ----

    [Test]
    public static void Schwaerzung_KeineKlartextAdressenUndKonten()
    {
        var zeilen = new List<string>();
        using var log = Logbuch.Fuer(zeilen.Add);
        var s = log.Schwaerzung;
        var ip = IPAddress.Parse("203.0.113.9");
        log.Info("Verbindung von " + s.Ip(ip) + " Konto " + s.Konto(4242));
        Pruefe.Falsch(zeilen[0].Contains("203.0.113.9", StringComparison.Ordinal), "keine Klartext-IP");
        Pruefe.Falsch(zeilen[0].Contains("4242", StringComparison.Ordinal), "keine Klartext-Konto-ID");
        Pruefe.Gleich(s.Ip(ip), s.Ip(ip), "stabil innerhalb eines Schlüssels");
        Pruefe.Falsch(s.Ip(ip) == Schwaerzung.Zufaellig().Ip(ip), "anderer Schlüssel, anderes Pseudonym");
        Pruefe.Falsch(s.Konto(1) == s.Konto(2), "verschiedene Konten unterscheidbar");
    }

    // ---- Ablage (7.2) ----

    [Test]
    public static void Ablage_AtomarUndZwischendateiVerworfen()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "ablage-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var pfad = Path.Combine(verzeichnis, "x.json");
            Dateiablage.AtomarSchreiben(pfad, "{\"a\":1}"u8);
            Dateiablage.AtomarSchreiben(pfad, "{\"a\":2}"u8);
            Pruefe.Falsch(File.Exists(pfad + Dateiablage.Zwischenendung), "keine Zwischendatei nach dem Ersetzen");
            File.WriteAllText(pfad + Dateiablage.Zwischenendung, "halb");
            Pruefe.Gleich("{\"a\":2}", Encoding.UTF8.GetString(Dateiablage.LesenFallsVorhanden(pfad)!), "vollständiger Stand");
            Pruefe.Falsch(File.Exists(pfad + Dateiablage.Zwischenendung), "liegengebliebene Zwischendatei verworfen");
        }
        finally
        {
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, true);
        }
    }

    [Test]
    public static void Ablage_LageUndKontenUeberstehenNeustart()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "ablage-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var ablage = new Zustandsablage(verzeichnis);
            var (start, neben) = ablage.LageLaden();
            var zs = new L.Zustandsschreiber(start, neben, ablage);
            var admin = L.InterneAuftraege.Verwaltung("netz", z => z.NetzSetzen(new L.Netz(2, "Kommando") { Standard = true, Offen = L.Offenheit.HoerenSenden }));
            Pruefe.Wahr(zs.Ausfuehren(admin, Zeit.LageMs()).Erfolg, "Netz über das Admin-Werkzeug");
            Pruefe.Wahr(zs.Ausfuehren(L.InterneAuftraege.KontoUebernehmen(7, "Test", "123456789012345678", L.Stufe.Admin, L.Recht.Keine, L.Recht.Keine, L.KontoStatus.Aktiv), Zeit.LageMs()).Erfolg,
                "Konto gespiegelt");
            var schiff = new L.Schiff(3, "Schiff 3") { Netze = System.Collections.Immutable.ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [new KeyValuePair<string, ushort>("CREW", 2)]) };
            Pruefe.Wahr(zs.Ausfuehren(L.InterneAuftraege.Verwaltung("schiff", z => z.SchiffSetzen(schiff)), Zeit.LageMs()).Erfolg, "Schiff");

            var (geladen, _) = new Zustandsablage(verzeichnis).LageLaden();
            Pruefe.Gleich(zs.Aktuell.ZustandsVersion, geladen.ZustandsVersion, "zustandsVersion");
            Pruefe.Wahr(geladen.Netze.ContainsKey(2), "Netz geladen");
            Pruefe.Wahr(geladen.Konten[7].FeldMitNetz(2) is { Senden: true }, "STANDARD-Feld auf Netz 2");
            Pruefe.Gleich((ushort)2, geladen.Schiffe[3].Netze["CREW"], "Schiffsnetz geladen");
            Pruefe.Falsch(geladen.Konten.ContainsKey(L.InterneAuftraege.AdminWerkzeug), "das flüchtige Admin-Konto wird nie gespeichert");
            Pruefe.Gleich(0, geladen.Sitzungen.Count, "Sitzungen sind flüchtig");

            var buch = new Kontenbuch(TimeProvider.System);
            var e = buch.KontoAnlegen(Akteur.AdminWerkzeug, "123456789012345678", "test", "Test", Stufe.Admin);
            Pruefe.Wahr(e.Ok, "Konto im Kontenbuch");
            ablage.KontenSpeichern(buch.Stand);
            var stand = new Zustandsablage(verzeichnis).KontenLaden();
            Pruefe.Gleich(buch.Stand.Konten.Count, stand.Konten.Count, "Konten geladen");
            Pruefe.Wahr(stand.KontoZuDiscordId("123456789012345678") is { Name: "Test" }, "Index nach Discord-ID");
            Pruefe.Gleich(buch.Stand.NachNamensschluessel.Count, stand.NachNamensschluessel.Count, "Namensindex");
        }
        finally
        {
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, true);
        }
    }

    /// <summary>
    /// Fassung 0.7.0 (Sarevoc, 04.10.2026: Löschen heißt vollständig entfernen, gesperrt wird nur bewusst): Ein Stand bis 0.6.x verliert
    /// beim ersten Laden einmal die Sperren, zu denen kein entzogenes Gerät mehr steht (so blieb dort ein abgelehnter Antrag gesperrt),
    /// und die abgelehnten Geräte. Die Sperre eines entzogenen Geräts bleibt, sonst käme etwa ein gestohlener PC wieder frei. Danach
    /// trägt konten.json das Kennzeichen, und die Sperre übersteht jedes weitere Laden.
    /// </summary>
    [Test]
    public static void Ablage_SperrenAus06_NurBewussteBleiben()
    {
        static Fingerabdruck Fp(int n) => new(Enumerable.Repeat((byte)n, 32).ToArray());
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "altsperren-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // Wie ein Stand von 0.6.x: ein bewusst gesperrter Antrag (ENTZOGEN, Fingerabdruck auf der Sperrliste), ein Antrag, dessen
            // Gerät auf ABGELEHNT steht, und eine Sperre ohne Gerät (ein abgelehnter Antrag, den die Frist schon abgeräumt hat).
            var buch = new Kontenbuch(TimeProvider.System);
            var gestohlen = buch.AntragStellen(Fp(41), "PC-2929", SchluesselArt.Software, "Gestohlen", "Gestohlen/DC");
            var abgelehnt = buch.AntragStellen(Fp(42), "PC-2A2A", SchluesselArt.Software, "Abgelehnt", "Abgelehnt/DC");
            Pruefe.Wahr(gestohlen.Ok && abgelehnt.Ok, "zwei Anträge");
            Pruefe.Wahr(buch.GeraetEntfernen(Akteur.AdminWerkzeug, gestohlen.GeraetId, true, "gestohlen").Ok, "bewusst gesperrt");
            new Zustandsablage(verzeichnis).KontenSpeichern(buch.Stand);
            var pfad = Path.Combine(verzeichnis, Zustandsablage.KontenDatei);
            var json = JsonNode.Parse(File.ReadAllBytes(pfad))!.AsObject();
            Pruefe.Wahr(json["nurBewussteSperren"]?.GetValue<bool>() == true, "0.7.0 schreibt das Kennzeichen");
            Pruefe.Wahr(json.Remove("nurBewussteSperren"), "Kennzeichen weg wie in 0.6.x");
            json["geraete"]!.AsArray().Single(g => (uint)g!["geraetId"]! == abgelehnt.GeraetId)!["status"] = (int)GeraetStatus.Abgelehnt;
            json["gesperrt"]!.AsArray().Add(Fp(44).AlsBase64());
            File.WriteAllText(pfad, json.ToJsonString());

            var alt = new Zustandsablage(verzeichnis);
            var nachher = new Kontenbuch(TimeProvider.System, alt.KontenLaden());
            Pruefe.Gleich(2, alt.AltsperrenVerworfen, "verworfen: die Sperre ohne Gerät und das abgelehnte Gerät");
            Pruefe.Wahr(alt.VerworfeneSperren.ToHashSet().SetEquals([Fp(42), Fp(44)]), "welche frei wurden, für das Journal");
            Pruefe.Wahr(nachher.Stand.GesperrteFingerabdruecke.SetEquals([Fp(41)]), "die bewusste Sperre bleibt");
            Pruefe.Falsch(nachher.Stand.Geraete.ContainsKey(abgelehnt.GeraetId), "abgelehntes Gerät fort");
            Pruefe.Gleich(EinstufungsArt.Unbekannt, nachher.Einstufen(Fp(42)).Art, "der abgelehnte PC darf neu beantragen");
            Pruefe.Gleich(EinstufungsArt.Unbekannt, nachher.Einstufen(Fp(44)).Art, "der früher abgelehnte auch");
            nachher.FristenAnwenden();
            Pruefe.Falsch(nachher.Stand.Konten.ContainsKey(gestohlen.KontoId) || nachher.Stand.Konten.ContainsKey(abgelehnt.KontoId), "Antragskonten ohne Gerät fort");
            Pruefe.Gleich(EinstufungsArt.GeraetGesperrt, nachher.Einstufen(Fp(41)).Art, "der gestohlene PC bleibt gesperrt");

            new Zustandsablage(verzeichnis).KontenSpeichern(nachher.Stand);
            var wieder = new Zustandsablage(verzeichnis);
            var stand = wieder.KontenLaden();
            Pruefe.Gleich(0, wieder.AltsperrenVerworfen, "Stand von 0.7.0: nichts verworfen");
            Pruefe.Gleich(0, wieder.VerworfeneSperren.Count, "und nichts fürs Journal");
            Pruefe.Wahr(stand.GesperrteFingerabdruecke.SetEquals([Fp(41)]), "die bewusste Sperre übersteht das Laden");
        }
        finally
        {
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, true);
        }
    }
}
