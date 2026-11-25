// JG TacRelay - Maintainer: Sarevoc
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Funk.Admin;

namespace Funk.Tests;

/// <summary>
/// <c>funk-admin umzug-packen</c> und <c>umzug-einspielen</c> (EINRICHTEN-UMZUG-ENTWURF, Vorschlag Umzug): Zustand und
/// TOTP-Schlüssel in einem mit AES-256-GCM verschlüsselten Paket, Schlüssel per PBKDF2-SHA256 aus einer Passphrase, die das
/// Werkzeug erzeugt und einmal zeigt. Angriffe: falsche Passphrase, verändertes, abgeschnittenes und kein Paket, Pfadausbruch und
/// symbolische Verweise beim Einspielen, Manifest passt nicht, Serverschlüssel ohne Schalter, laufender Dienst.
/// </summary>
internal static partial class UmzugTests
{
    private static readonly Regex Passphrase = new(@"\b([A-Z2-7]{4}-){6}[A-Z2-7]{2}\b", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Ein Server im Testordner: Zustand, Schlüsselordner und server.json, die auf beides zeigt.</summary>
    private sealed record Server(string Wurzel, string Konfig, string Zustand, string Credentials)
    {
        public string Datei(string name) => Path.Combine(Zustand, name);

        public string Schluessel(string name) => Path.Combine(Credentials, name);
    }

    private static async Task<Server> ServerAnlegenAsync(string wurzel, string? socket = null)
    {
        var zustand = Path.Combine(wurzel, "zustand");
        var c = Path.Combine(wurzel, "credentials");
        Directory.CreateDirectory(zustand);
        Directory.CreateDirectory(c);
        Pruefe.Gleich(0, (await AdminEinrichtungTests.Werkzeug("schluessel-erzeugen", "--zertifikat", Path.Combine(c, "server.crt"), "--schluessel", Path.Combine(c, "server.key"))).Code, "Serverschlüssel");
        Pruefe.Gleich(0, (await AdminEinrichtungTests.Werkzeug("geheimnis-erzeugen", "--datei", Path.Combine(c, "funk-geheimnis"))).Code, "Geheimnis");
        var konfig = Path.Combine(wurzel, "server.json");
        var k = new JsonObject
        {
            ["binden"] = new JsonArray("127.0.0.1"),
            ["tcpPort"] = 8443,
            ["udpPort"] = 47443,
            ["zustandsverzeichnis"] = zustand,
            ["identitaet"] = new JsonObject { ["zertifikat"] = "${CREDENTIALS_DIRECTORY}/server.crt", ["schluessel"] = "${CREDENTIALS_DIRECTORY}/server.key" },
            ["geheimnis"] = "${CREDENTIALS_DIRECTORY}/funk-geheimnis",
            ["totpSchluessel"] = "${CREDENTIALS_DIRECTORY}/funk-totp-schluessel",
            ["profil"] = Path.Combine(wurzel, "profil.json"),
            ["verwaltungsSocket"] = socket ?? Path.Combine(wurzel, "kein-dienst.sock"),
        };
        await File.WriteAllTextAsync(konfig, k.ToJsonString());
        return new Server(wurzel, konfig, zustand, c);
    }

    /// <summary>Der alte Server: ein Testserver mit Konten und Netz, danach beendet; sein TOTP-Schlüssel liegt als Datei daneben.</summary>
    private static async Task<(Server S, Testserver T)> AlterServerAsync()
    {
        var t = await Testserver.StartenAsync();
        await t.AdminOk("konto-anlegen", new JsonObject { ["discord"] = "940000000000000001", ["benutzername"] = "sarevoc", ["name"] = "Sarevoc", ["stufe"] = "ADMIN" });
        await t.AdminOk("konto-anlegen", new JsonObject { ["discord"] = "940000000000000002", ["benutzername"] = "otto", ["name"] = "Otto" });
        await t.AdminOk("netz-setzen", new JsonObject
        {
            ["objekt"] = new JsonObject { ["id"] = 2, ["name"] = "Brücke", ["typ"] = "NORMAL", ["reihenfolge"] = 0, ["nurTaste"] = false, ["ausloeserStandard"] = "TASTE", ["offen"] = "HOEREN_SENDEN", ["standard"] = true },
        });
        await t.AdminOk("sicherung");
        var s = await ServerAnlegenAsync(t.Verzeichnis, t.Dienst.Verwaltungspfad);
        await File.WriteAllBytesAsync(s.Schluessel("funk-totp-schluessel"), t.TotpSchluessel);
        return (s, t);
    }

    private static async Task<(int Code, string Text, string? Passphrase)> PackenAsync(Server s, string ziel, params string[] mehr)
    {
        var (code, text) = await AdminEinrichtungTests.Werkzeug(["umzug-packen", "--konfig", s.Konfig, "--credentials", s.Credentials, "--aus", ziel, .. mehr]);
        var m = Passphrase.Match(text);
        return (code, text, m.Success ? m.Value : null);
    }

    private static Task<(int Code, string Ausgabe)> Einspielen(Server s, string paket, string eingabe, params string[] mehr) =>
        AdminEinrichtungTests.MitEingabe(eingabe, ["umzug-einspielen", "--konfig", s.Konfig, "--credentials", s.Credentials, "--paket", paket, .. mehr]);

    private static Dictionary<string, byte[]> Stand(Server s) =>
        Directory.GetFiles(s.Zustand).Concat(Directory.GetFiles(s.Credentials)).ToDictionary(Path.GetFullPath, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static void Unveraendert(Dictionary<string, byte[]> vorher, Server s, string was)
    {
        var nachher = Stand(s);
        Pruefe.Gleich(string.Join(",", vorher.Keys.Order(StringComparer.Ordinal)), string.Join(",", nachher.Keys.Order(StringComparer.Ordinal)), was + ": dieselben Dateien");
        foreach (var (pfad, inhalt) in vorher) Pruefe.BytesGleich(inhalt, nachher[pfad], was + ": " + Path.GetFileName(pfad));
    }

    [Test]
    public static async Task Umzug_PackenUndEinspielen_Rundlauf()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var (alt, t) = await AlterServerAsync();
        await using (t)
        {
            var paket = o["umzug.tru"];
            var (code, text, _) = await PackenAsync(alt, paket);
            Pruefe.Gleich(1, code, "bei laufendem Dienst: " + text);
            Pruefe.Wahr(text.Contains("systemctl stop funk-server", StringComparison.Ordinal) && !File.Exists(paket), "erst anhalten, nichts geschrieben: " + text);

            await t.DienstBeendenAsync();
            (code, text, var passphrase) = await PackenAsync(alt, paket);
            Pruefe.Gleich(0, code, "packen: " + text);
            Pruefe.Wahr(passphrase is not null, "Passphrase in Vierergruppen gezeigt:\n" + text);
            Pruefe.Gleich(26, passphrase!.Replace("-", "", StringComparison.Ordinal).Length, "26 Zeichen Base32");
            Pruefe.Wahr(text.Contains("Telefon", StringComparison.Ordinal) && text.Contains("funk-geheimnis", StringComparison.Ordinal), "Hinweise: Telefon, was fehlt:\n" + text);
            // Seit 0.3.0 gibt es sperrliste.json; wie die Vorrangspur passt sie nur zum alten Betriebsgeheimnis (Zusammenführung 0.4.0).
            Pruefe.Wahr(text.Contains("sperrliste.json", StringComparison.Ordinal), "nennt die Sperrliste unter 'Nicht darin':\n" + text);
            var roh = File.ReadAllBytes(paket);
            foreach (var klartext in new[] { "Sarevoc", "konten", "Brücke", "MANIFEST" })
                Pruefe.Falsch(roh.AsSpan().IndexOf(Encoding.UTF8.GetBytes(klartext)) >= 0, "verschlüsselt, kein Klartext: " + klartext);
            Pruefe.Gleich(3, (await PackenAsync(alt, paket)).Code, "nie über ein vorhandenes Paket");

            // Der neue Server hat schon eigenen Stand und eigenen TOTP-Schlüssel (frisch eingerichtet): beides wird zur Seite gelegt.
            var neu = await ServerAnlegenAsync(o["neu"]);
            await File.WriteAllTextAsync(neu.Datei("konten.json"), "{\"vom\":\"neuen Server\"}");
            await File.WriteAllBytesAsync(neu.Schluessel("funk-totp-schluessel"), RandomNumberGenerator.GetBytes(32));
            var neuVorher = Stand(neu);
            var schluesselVorher = File.ReadAllBytes(neu.Schluessel("server.key"));

            // Passphrase klein, mit Leerzeichen statt Bindestrichen: so tippt man sie nach dem Telefonat ab.
            var getippt = passphrase.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant() + "\n";
            (code, text) = await Einspielen(neu, paket, getippt);
            Pruefe.Gleich(0, code, "einspielen: " + text);
            foreach (var datei in new[] { "konten.json", "lage.json" })
                Pruefe.BytesGleich(File.ReadAllBytes(alt.Datei(datei)), File.ReadAllBytes(neu.Datei(datei)), datei + " bytegleich");
            Pruefe.BytesGleich(File.ReadAllBytes(alt.Schluessel("funk-totp-schluessel")), File.ReadAllBytes(neu.Schluessel("funk-totp-schluessel")), "TOTP-Schlüssel mitgenommen");
            Pruefe.BytesGleich(schluesselVorher, File.ReadAllBytes(neu.Schluessel("server.key")), "der Serverschlüssel des neuen Servers bleibt");
            Pruefe.Falsch(File.Exists(neu.Datei("vorrangspur.json")) && File.ReadAllBytes(neu.Datei("vorrangspur.json")).AsSpan().SequenceEqual(File.ReadAllBytes(alt.Datei("vorrangspur.json"))),
                "die Vorrangspur kommt nicht mit");
            if (File.Exists(alt.Datei("sperrliste.json")))
                Pruefe.Falsch(File.Exists(neu.Datei("sperrliste.json")) && File.ReadAllBytes(neu.Datei("sperrliste.json")).AsSpan().SequenceEqual(File.ReadAllBytes(alt.Datei("sperrliste.json"))),
                    "die Sperrliste kommt nicht mit");
            var beiseite = Directory.GetFiles(neu.Zustand, "konten.json.vor-umzug-*").Single();
            Pruefe.BytesGleich(neuVorher[Path.GetFullPath(neu.Datei("konten.json"))], File.ReadAllBytes(beiseite), "der alte Stand des neuen Servers liegt daneben");
            var totpBeiseite = Directory.GetFiles(neu.Credentials, "funk-totp-schluessel.vor-umzug-*").Single();
            Pruefe.BytesGleich(neuVorher[Path.GetFullPath(neu.Schluessel("funk-totp-schluessel"))], File.ReadAllBytes(totpBeiseite), "sein TOTP-Schlüssel ebenso");
            Pruefe.Wahr(text.Contains("systemctl start funk-server", StringComparison.Ordinal), "nächster Schritt:\n" + text);

            // Der neue Server startet mit dem Stand des alten: Konten und Netz sind da.
            await using var t2 = await Testserver.StartenAsync(verzeichnis: neu.Wurzel);
            var konten = await t2.AdminOk("konten");
            Pruefe.Wahr(konten.Text.Contains("Sarevoc", StringComparison.Ordinal) && konten.Text.Contains("Otto", StringComparison.Ordinal), "Konten umgezogen: " + konten.Text);
            Pruefe.Wahr((await t2.AdminOk("netzliste")).Text.Contains("Brücke", StringComparison.Ordinal), "Netz umgezogen");
        }
    }

    [Test]
    public static async Task Umzug_FalschVeraendertAbgeschnitten()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var (alt, t) = await AlterServerAsync();
        await using (t)
        {
            await t.DienstBeendenAsync();
            var paket = o["umzug.tru"];
            var (code, text, passphrase) = await PackenAsync(alt, paket);
            Pruefe.Gleich(0, code, text);
            var neu = await ServerAnlegenAsync(o["neu"]);
            var vorher = Stand(neu);
            var roh = File.ReadAllBytes(paket);
            var richtig = passphrase! + "\n";

            // Falsche Passphrase, dreimal: Abbruch, nichts geändert. Eine ungültige Form zählt nicht als Versuch.
            var falsch = "AAAA-AAAA-AAAA-AAAA-AAAA-AAAA-AA\n";
            (code, text) = await Einspielen(neu, paket, "zu kurz\n" + falsch + falsch + falsch);
            Pruefe.Gleich(1, code, "falsche Passphrase: " + text);
            Pruefe.Wahr(text.Contains("26 Zeichen", StringComparison.Ordinal) && text.Contains("Passphrase falsch", StringComparison.Ordinal), text);
            Unveraendert(vorher, neu, "falsche Passphrase");

            void Variante(string name, byte[] inhalt) => File.WriteAllBytes(o[name], inhalt);
            var gekippt = roh.ToArray();
            gekippt[^40] ^= 0x01;
            Variante("gekippt.tru", gekippt);
            var kopf = roh.ToArray();
            kopf[10] ^= 0x01;
            Variante("kopf.tru", kopf);
            Variante("kurz.tru", roh[..^10]);
            Variante("stumpf.tru", roh[..20]);
            Variante("leer.tru", []);
            Variante("fremd.tru", RandomNumberGenerator.GetBytes(4096));
            foreach (var (datei, erwartet) in new[]
            {
                ("gekippt.tru", "verändert"), ("kopf.tru", "verändert"), ("kurz.tru", "abgeschnitten"), ("stumpf.tru", "kein Umzugspaket"),
                ("leer.tru", "kein Umzugspaket"), ("fremd.tru", "kein Umzugspaket"),
            })
            {
                (code, text) = await Einspielen(neu, o[datei], richtig + richtig + richtig);
                Pruefe.Gleich(1, code, datei + ": " + text);
                Pruefe.Wahr(text.Contains(erwartet, StringComparison.Ordinal), datei + ": " + text);
                Unveraendert(vorher, neu, datei);
            }

            // Ohne Paket.
            (code, text) = await Einspielen(neu, o["gibt-es-nicht.tru"], richtig);
            Pruefe.Gleich(1, code, "fehlt: " + text);
        }
    }

    /// <summary>Ein verschlüsseltes Paket mit beliebigem Inhalt, wie es ein Angreifer bauen könnte, der die Passphrase kennt.</summary>
    private static byte[] Gebaut(string passphrase, string manifest, params (string Name, byte[] Inhalt, TarEntryType Art, string? Ziel)[] eintraege)
    {
        using var tar = new MemoryStream();
        using (var w = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "MANIFEST.json") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(manifest)) });
            foreach (var (name, inhalt, art, ziel) in eintraege)
            {
                var e = new PaxTarEntry(art, name);
                if (art == TarEntryType.RegularFile) e.DataStream = new MemoryStream(inhalt);
                if (ziel is not null) e.LinkName = ziel;
                w.WriteEntry(e);
            }
        }
        return Umzugspaket.Verschluesseln(tar.ToArray(), passphrase);
    }

    private static string Manifest(params (string Name, byte[] Inhalt)[] dateien) => new JsonObject
    {
        ["format"] = "tr-umzug 1",
        ["erstellt"] = "2026-10-01T12:00:00Z",
        ["fassung"] = "0.2.0",
        ["dateien"] = new JsonArray([.. dateien.Select(d => (JsonNode)new JsonObject
        {
            ["name"] = d.Name,
            ["groesse"] = d.Inhalt.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(d.Inhalt)),
        })]),
    }.ToJsonString();

    [Test]
    public static async Task Umzug_PfadausbruchUndManifest()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var neu = await ServerAnlegenAsync(o["neu"]);
        await File.WriteAllBytesAsync(neu.Schluessel("funk-totp-schluessel"), RandomNumberGenerator.GetBytes(32));
        var vorher = Stand(neu);
        const string P = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var konten = Encoding.UTF8.GetBytes("{}");
        var boese = Encoding.UTF8.GetBytes("{\"boese\":true}");
        var faelle = new (string Was, byte[] Paket, string Erwartet)[]
        {
            ("Pfad nach oben", Gebaut(P, Manifest(("../boese.json", boese)), ("../boese.json", boese, TarEntryType.RegularFile, null)), "boese.json"),
            ("Pfad absolut", Gebaut(P, Manifest(("/tmp/boese.json", boese)), ("/tmp/boese.json", boese, TarEntryType.RegularFile, null)), "boese.json"),
            ("Laufwerk", Gebaut(P, Manifest(("C:\\boese.json", boese)), ("C:\\boese.json", boese, TarEntryType.RegularFile, null)), "boese.json"),
            ("Unterordner", Gebaut(P, Manifest(("zustand/konten.json", konten)), ("zustand/konten.json", konten, TarEntryType.RegularFile, null)), "konten.json"),
            ("Verweis", Gebaut(P, Manifest(("konten.json", [])), ("konten.json", [], TarEntryType.SymbolicLink, "../../boese.json")), "keine gewöhnliche Datei"),
            ("harter Verweis", Gebaut(P, Manifest(("konten.json", [])), ("konten.json", [], TarEntryType.HardLink, "/etc/passwd")), "keine gewöhnliche Datei"),
            ("nicht im Manifest", Gebaut(P, Manifest(("konten.json", konten)), ("konten.json", konten, TarEntryType.RegularFile, null), ("lage.json", konten, TarEntryType.RegularFile, null)), "lage.json"),
            ("Prüfsumme", Gebaut(P, Manifest(("konten.json", konten)), ("konten.json", boese, TarEntryType.RegularFile, null)), "Prüfsumme"),
            ("fehlt", Gebaut(P, Manifest(("konten.json", konten), ("lage.json", konten)), ("konten.json", konten, TarEntryType.RegularFile, null)), "lage.json"),
            ("doppelt", Gebaut(P, Manifest(("konten.json", konten)), ("konten.json", konten, TarEntryType.RegularFile, null), ("konten.json", konten, TarEntryType.RegularFile, null)), "doppelt"),
            ("Format", Gebaut(P, "{\"format\":\"etwas anderes\",\"dateien\":[]}"), "Format"),
        };
        foreach (var (was, paket, erwartet) in faelle)
        {
            var datei = o[Guid.NewGuid().ToString("N")[..8] + ".tru"];
            await File.WriteAllBytesAsync(datei, paket);
            var (code, text) = await Einspielen(neu, datei, P + "\n");
            Pruefe.Gleich(1, code, was + ": " + text);
            Pruefe.Wahr(text.Contains(erwartet, StringComparison.Ordinal) && text.Contains("Nichts geändert", StringComparison.Ordinal), was + ": " + text);
            Unveraendert(vorher, neu, was);
            Pruefe.Falsch(File.Exists(Path.Combine(neu.Wurzel, "boese.json")) || File.Exists(o["boese.json"]) || File.Exists(Path.Combine(Path.GetPathRoot(o.Pfad)!, "boese.json")),
                was + ": nichts außerhalb geschrieben");
        }

        // Ein MANIFEST, das fehlt, und eines, das nicht vorn steht.
        using var tar = new MemoryStream();
        using (var w = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
            w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "konten.json") { DataStream = new MemoryStream(konten) });
        await File.WriteAllBytesAsync(o["ohne.tru"], Umzugspaket.Verschluesseln(tar.ToArray(), P));
        var (c2, t2) = await Einspielen(neu, o["ohne.tru"], P + "\n");
        Pruefe.Wahr(c2 == 1 && t2.Contains("MANIFEST", StringComparison.Ordinal), "ohne MANIFEST: " + t2);
        Unveraendert(vorher, neu, "ohne MANIFEST");
    }

    [Test]
    public static async Task Umzug_SchalterUndLaufenderDienst()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var (alt, t) = await AlterServerAsync();
        await using (t)
        {
            await t.DienstBeendenAsync();

            // Mit Serverschlüssel (Variante B) nur ausdrücklich, beim Packen und beim Einspielen.
            var (code, text, passphrase) = await PackenAsync(alt, o["mit.tru"], "--mit-serverschluessel");
            Pruefe.Gleich(0, code, "mit Serverschlüssel: " + text);
            Pruefe.Wahr(text.Contains("server.key", StringComparison.Ordinal), "nennt den Schlüssel im Paket:\n" + text);
            var neu = await ServerAnlegenAsync(o["neu"]);
            var vorher = Stand(neu);
            (code, text) = await Einspielen(neu, o["mit.tru"], passphrase + "\n");
            Pruefe.Gleich(1, code, "Serverschlüssel ohne Schalter: " + text);
            Pruefe.Wahr(text.Contains("--mit-serverschluessel", StringComparison.Ordinal), text);
            Unveraendert(vorher, neu, "ohne Schalter");
            (code, text) = await Einspielen(neu, o["mit.tru"], passphrase + "\n", "--mit-serverschluessel");
            Pruefe.Gleich(0, code, "mit Schalter: " + text);
            Pruefe.BytesGleich(File.ReadAllBytes(alt.Schluessel("server.key")), File.ReadAllBytes(neu.Schluessel("server.key")), "Serverschlüssel mitgenommen");
            Pruefe.BytesGleich(File.ReadAllBytes(alt.Schluessel("server.crt")), File.ReadAllBytes(neu.Schluessel("server.crt")), "Zertifikat mitgenommen");
            Pruefe.Wahr(Directory.GetFiles(neu.Credentials, "server.key.vor-umzug-*").Length == 1, "der eigene Schlüssel des neuen Servers liegt daneben");

            // Ohne TOTP-Schlüssel: der des neuen Servers bleibt.
            (code, text, passphrase) = await PackenAsync(alt, o["ohne.tru"], "--ohne-totp");
            Pruefe.Gleich(0, code, "--ohne-totp: " + text);
            var neu2 = await ServerAnlegenAsync(o["neu2"]);
            var eigenerTotp = RandomNumberGenerator.GetBytes(32);
            await File.WriteAllBytesAsync(neu2.Schluessel("funk-totp-schluessel"), eigenerTotp);
            (code, text) = await Einspielen(neu2, o["ohne.tru"], passphrase + "\n");
            Pruefe.Gleich(0, code, "einspielen ohne TOTP: " + text);
            Pruefe.BytesGleich(eigenerTotp, File.ReadAllBytes(neu2.Schluessel("funk-totp-schluessel")), "eigener TOTP-Schlüssel bleibt");
            Pruefe.Wahr(text.Contains("Zweitfaktor", StringComparison.Ordinal), "sagt, dass die Admins den Zweitfaktor neu einrichten:\n" + text);

            // Läuft der neue Dienst, wird nichts eingespielt.
            await using var t3 = await Testserver.StartenAsync();
            var laufend = await ServerAnlegenAsync(o["laufend"], t3.Dienst.Verwaltungspfad);
            var stand = Stand(laufend);
            (code, text) = await Einspielen(laufend, o["ohne.tru"], passphrase + "\n");
            Pruefe.Gleich(1, code, "laufender Dienst: " + text);
            Pruefe.Wahr(text.Contains("systemctl stop funk-server", StringComparison.Ordinal), text);
            Unveraendert(stand, laufend, "laufender Dienst");
        }
    }

    /// <summary>Unter Linux (hier die Attrappe): Besitzer funk für Zustandsordner und Zustandsdateien, nie für die Schlüssel.</summary>
    [Test]
    public static async Task Umzug_UnterLinuxBesitzerNurFuerDenZustand()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var (alt, t) = await AlterServerAsync();
        await using (t)
        {
            await t.DienstBeendenAsync();
            var system = new SystemAttrappe { Linux = true };
            var aus = new StringWriter();
            var k = Funk.Server.Betrieb.ServerKonfiguration.Laden(alt.Konfig);
            Pruefe.Gleich(0, new Umzug(new StringReader(""), aus, system).Packen(k, alt.Credentials, o["umzug.tru"], false, true, DateTimeOffset.UtcNow), "packen: " + aus);
            var passphrase = Passphrase.Match(aus.ToString()).Value;
            Pruefe.Gleich(0, system.Besitzer.Count, "packen setzt keine Besitzer");

            var neu = await ServerAnlegenAsync(o["neu"]);
            aus = new StringWriter();
            var kNeu = Funk.Server.Betrieb.ServerKonfiguration.Laden(neu.Konfig);
            var code = new Umzug(new StringReader(passphrase + "\n"), aus, system).Einspielen(kNeu, neu.Credentials, o["umzug.tru"], true, "funk", DateTimeOffset.UtcNow);
            Pruefe.Gleich(0, code, "einspielen: " + aus);
            var gesetzt = system.Besitzer.Select(b => (Path.GetFullPath(b.Pfad), b.Besitzer)).ToList();
            Pruefe.Wahr(gesetzt.All(b => b.Besitzer == "funk"), "immer funk");
            Pruefe.Wahr(gesetzt.Contains((Path.GetFullPath(neu.Zustand), "funk")), "der Zustandsordner");
            foreach (var datei in Directory.GetFiles(alt.Zustand, "*.json").Select(Path.GetFileName).Where(n => Umzugspaket.Zustand.Contains(n!)))
                Pruefe.Wahr(gesetzt.Contains((Path.GetFullPath(neu.Datei(datei!)), "funk")), "Besitzer für " + datei);
            Pruefe.Falsch(gesetzt.Any(b => b.Item1.StartsWith(Path.GetFullPath(neu.Credentials), StringComparison.OrdinalIgnoreCase)), "die Schlüssel bleiben bei root");
            Pruefe.Wahr(aus.ToString().Contains("Besitzer funk:funk", StringComparison.Ordinal), "sagt es: " + aus);
        }
    }

    /// <summary>
    /// Gegenprüfung 01.10.2026: root schreibt beim Einspielen nach /var/lib/funk, das dem Dienstbenutzer gehört. Eine dort
    /// untergeschobene Zwischendatei <c>konten.json.neu</c>, die ein Verweis auf eine fremde Datei ist, ließ Dateiablage bis dahin
    /// mit FileMode.Create durchschreiben, und chown machte das Ziel des Verweises zum Eigentum von funk. Unter Windows lassen sich
    /// symbolische Verweise ohne Adminrechte nicht anlegen; ein harter Verweis zeigt dasselbe (FileMode.Create öffnet die vorhandene
    /// Datei und schreibt in ihren Inhalt).
    /// </summary>
    [Test]
    public static async Task Umzug_UntergeschobeneZwischendatei_RootSchreibtNichtHindurch()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var (alt, t) = await AlterServerAsync();
        await using (t)
        {
            await t.DienstBeendenAsync();
            var (code, text, passphrase) = await PackenAsync(alt, o["umzug.tru"]);
            Pruefe.Gleich(0, code, "packen: " + text);
            var neu = await ServerAnlegenAsync(o["neu"]);
            var fremd = o["fremd-gehoert-root.txt"];
            var fremdInhalt = Encoding.UTF8.GetBytes("root:x:0:0:Datei von root, nicht anfassen\n");
            await File.WriteAllBytesAsync(fremd, fremdInhalt);
            var zwischen = neu.Datei("konten.json" + Funk.Server.Betrieb.Dateiablage.Zwischenendung);
            Pruefe.Wahr(HartVerweisen(zwischen, fremd), "harter Verweis angelegt (NTFS, ohne Adminrechte)");

            (code, text) = await Einspielen(neu, o["umzug.tru"], passphrase + "\n");
            Pruefe.Gleich(0, code, "einspielen: " + text);
            Pruefe.BytesGleich(fremdInhalt, File.ReadAllBytes(fremd), "die fremde Datei hinter dem Verweis bleibt, wie sie war");
            Pruefe.BytesGleich(File.ReadAllBytes(alt.Datei("konten.json")), File.ReadAllBytes(neu.Datei("konten.json")), "konten.json kam trotzdem an");
            Pruefe.Falsch(File.Exists(zwischen), "keine Zwischendatei übrig");
        }

        // Dasselbe ohne Umzug, direkt an der Ablage: eine liegengebliebene Zwischendatei mit längerem Inhalt.
        var ziel = o["datei.json"];
        await File.WriteAllTextAsync(ziel + Funk.Server.Betrieb.Dateiablage.Zwischenendung, new string('x', 4096));
        Funk.Server.Betrieb.Dateiablage.AtomarSchreiben(ziel, "{}"u8);
        Pruefe.Gleich("{}", await File.ReadAllTextAsync(ziel), "nur der neue Inhalt, nichts vom Rest");
    }

    /// <summary>chown nur mit -h: Ein Verweis bekommt selbst den Besitzer, nie die Datei, auf die er zeigt.</summary>
    [Test]
    public static void Umzug_ChownFolgtKeinemVerweis()
    {
        var system = new ChownAttrappe();
        system.BesitzerSetzen("/var/lib/funk/konten.json", "funk");
        Pruefe.Gleich(1, system.Aufrufe.Count, "ein Aufruf: " + string.Join(" | ", system.Aufrufe));
        Pruefe.Gleich("chown -h funk:funk -- /var/lib/funk/konten.json", system.Aufrufe[0], "mit -h und -- vor dem Pfad");
        system.Code = 1;
        Pruefe.Wirft<IOException>(() => system.BesitzerSetzen("/var/lib/funk/lage.json", "funk"), "Fehler von chown kommt an");
        Pruefe.Gleich(0, new ChownAttrappe { Linux = false }.Pruefen(), "unter Windows kein Aufruf");
    }

    private sealed class ChownAttrappe : Systemblick
    {
        public List<string> Aufrufe { get; } = [];
        public int Code { get; set; }
        public bool Linux { get; init; } = true;

        public override bool IstLinux => Linux;

        public override Prozessergebnis Ausfuehren(string programm, params string[] argumente)
        {
            Aufrufe.Add(programm + " " + string.Join(" ", argumente));
            return new Prozessergebnis(Code, Code == 0 ? "" : "chown: Fehler");
        }

        public int Pruefen()
        {
            BesitzerSetzen("/x", "funk");
            return Aufrufe.Count;
        }
    }

    /// <summary>Zertifikat ohne Serverschlüssel (ein Paket, wie es jemand mit der Passphrase bauen könnte) ändert die Identität ebenso.</summary>
    [Test]
    public static async Task Umzug_ZertifikatAlleinNurMitSchalterUndNieOhneSchluessel()
    {
        using var o = new AdminEinrichtungTests.Ordner();
        var neu = await ServerAnlegenAsync(o["neu"]);
        var vorher = Stand(neu);
        const string P = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var konten = Encoding.UTF8.GetBytes("{}");
        var crt = Encoding.ASCII.GetBytes("-----BEGIN CERTIFICATE-----\nMA==\n-----END CERTIFICATE-----\n");
        await File.WriteAllBytesAsync(o["crt.tru"], Gebaut(P, Manifest(("konten.json", konten), ("server.crt", crt)),
            ("konten.json", konten, TarEntryType.RegularFile, null), ("server.crt", crt, TarEntryType.RegularFile, null)));
        var (code, text) = await Einspielen(neu, o["crt.tru"], P + "\n");
        Pruefe.Gleich(1, code, "Zertifikat ohne Schalter: " + text);
        Pruefe.Wahr(text.Contains("--mit-serverschluessel", StringComparison.Ordinal) && text.Contains("Nichts geändert", StringComparison.Ordinal), text);
        Unveraendert(vorher, neu, "Zertifikat ohne Schalter");
        (code, text) = await Einspielen(neu, o["crt.tru"], P + "\n", "--mit-serverschluessel");
        Pruefe.Gleich(1, code, "Zertifikat ohne Schlüssel, mit Schalter: " + text);
        Pruefe.Wahr(text.Contains("gehören zusammen", StringComparison.Ordinal), text);
        Unveraendert(vorher, neu, "Zertifikat ohne Schlüssel");
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string neu, string vorhanden, nint sicherheit);

    private static bool HartVerweisen(string neu, string vorhanden) => CreateHardLinkW(neu, vorhanden, 0);

    [Test]
    public static void Umzugspaket_Passphrase()
    {
        Pruefe.Gleich("ABCDEFGHIJKLMNOPQRSTUVWXYZ", Umzugspaket.PassphraseNormalisieren(" abcd-efgh ijkl mnop-qrst-uvwx-yz "), "Leerzeichen, Bindestriche, klein");
        Pruefe.Gleich("OIBCDEFGHIJKLMNOPQRSTUVWXY", Umzugspaket.PassphraseNormalisieren("01BC-DEFG-HIJK-LMNO-PQRS-TUVW-XY"), "0 und 1 verlesen als O und I");
        Pruefe.Gleich(null, Umzugspaket.PassphraseNormalisieren("ABCD"), "zu kurz");
        Pruefe.Gleich(null, Umzugspaket.PassphraseNormalisieren("ABCD-EFGH-IJKL-MNOP-QRST-UVWX-Y9"), "9 gibt es in Base32 nicht");
        Pruefe.Gleich(null, Umzugspaket.PassphraseNormalisieren(null), "nichts");
    }
}
